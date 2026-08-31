using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace GIC.Pet.Chat
{
    /// <summary>
    /// 派蒙反应通道（2026-08-30，AI 抽卡配套）：主进程产生"派蒙反应事件"（事件描述+动作+兜底文案+LLM 注记），
    /// 桌面宠/游戏内宠两形态共用同一条消费路径——persistentDataPath/pet_react.jsonl 追加式 JSON 行
    ///（与 PetIntentIpc 文件通道同构，方向相反：主进程→宠物）。
    ///
    /// 协议：每行一个 JSON 对象带单调递增 seq。seq 跨主进程重启单调（首写时从文件尾行续号）——
    /// 宠物进程消费方按"启动时读到的最大 seq 为基线，只处理更大的"取增量，重启竞态下不丢不错。
    /// 写半行被读到：解析失败跳过，下轮重读全文按 seq 补收，天然不丢事件。
    ///
    /// 文本生成链路（2026-08-31 改版）：主进程只写 eventDesc（结构化事件短句，"第3发抽到五星「温迪」"）
    /// +fallback（本地化模板兜底）——反应话语由宠进程侧 LLM 现编（PetReactionConsumer 复用 PetChatClient，
    /// 按派蒙人设流式生成），动作 anim 即时播不等 LLM。主进程不做文本生成（会话组件/密钥链路全在宠进程）。
    ///
    /// 为什么游戏内形态也走文件而不走进程内事件：桌面宠是独立进程必须走文件；游戏内形态同走
    /// 文件=两形态单一代码路径、行为天然对齐（写入方恒为主进程；同进程一次文件 IO 的开销在
    /// 反应场景——0.5s 轮询+几行文本——可忽略）。
    /// </summary>
    public static class PetReactionChannel
    {
        [Serializable]
        public class ReactionEvent
        {
            public long seq;
            public string anim;      // 单次动作 clip 全名（空=不播；消费侧即时播放不等 LLM）
            public string eventDesc;  // 事件描述（LLM 生成素材——"第3发抽到五星「温迪」"式短句；空=无文本反应）
            public string fallback;  // 兜底文案（LLM 失败/无 key/对话中错失时直出，本地化模板）
            public string note;      // LLM 会话历史注记（空=不注入；结算用）
        }

        public static string FilePath => Path.Combine(Application.persistentDataPath, "pet_react.jsonl");

        static long _seq;
        static bool _seqInitialized;
        const int HousekeepingLines = 500; // 超过即瘦身（只留末 100 行，seq 保留不重排）
        const int KeepLines = 100;

        /// <summary>产生一条反应事件（主进程调用；追加写。反应是尽力而为的反馈，失败只告警）</summary>
        public static void Push(string eventDesc, string anim = null, string fallback = null, string note = null)
        {
            try
            {
                EnsureSeqInitialized();
                var evt = new ReactionEvent
                {
                    seq = ++_seq,
                    eventDesc = eventDesc ?? "",
                    anim = anim ?? "",
                    fallback = fallback ?? "",
                    note = note ?? "",
                };
                File.AppendAllText(FilePath, JsonUtility.ToJson(evt) + "\n");
                Housekeeping();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PetReact] 反应事件写入失败：{e.Message}");
            }
        }

        /// <summary>seq 续号：首写时读文件尾行，从其 seq 续——跨主进程重启单调，
        /// 宠物侧基线比较才不会漏事件</summary>
        static void EnsureSeqInitialized()
        {
            if (_seqInitialized) return;
            _seqInitialized = true;
            try
            {
                if (!File.Exists(FilePath)) return;
                var lines = File.ReadAllLines(FilePath);
                for (int i = lines.Length - 1; i >= 0; i--)
                {
                    if (string.IsNullOrWhiteSpace(lines[i])) continue;
                    try
                    {
                        var last = JsonUtility.FromJson<ReactionEvent>(lines[i]);
                        if (last != null && last.seq > _seq) _seq = last.seq;
                    }
                    catch { /* 半行/坏行：继续往前找 */ }
                    break; // 只看最后一个非空行
                }
            }
            catch { /* 读失败从 0 开号（罕见，可接受） */ }
        }

        /// <summary>文件瘦身（单写方做，读方按 seq 增量天然兼容）：超 500 行只留末 100 行。
        /// 瘦身瞬间读方可能撞到半行——解析失败跳过，下轮重读补收</summary>
        static void Housekeeping()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                var lines = File.ReadAllLines(FilePath);
                if (lines.Length <= HousekeepingLines) return;
                var keep = new List<string>();
                for (int i = Math.Max(0, lines.Length - KeepLines); i < lines.Length; i++)
                    if (!string.IsNullOrWhiteSpace(lines[i])) keep.Add(lines[i]);
                File.WriteAllLines(FilePath, keep);
            }
            catch { /* 瘦身失败无碍主流程 */ }
        }
    }

    /// <summary>
    /// 反应消费组件（两形态宿主各挂一个，接聊天 后接线）：0.5s 轮询通道增量。
    /// 三路派发：①动作——事件到达即播（不等 LLM）；②文本——eventDesc 累积合并，LLM 空闲时
    /// 按派蒙人设流式生成一条反应（抽卡快于生成时自然合并成一条综合反应，防请求轰炸）；
    /// ③note——进会话历史。对话进行中（IsBusy）文本错失——用户对话优先，fallback 也不打。
    /// LLM 失败/无 key：fallback 模板直出（退化为固定文案，不静默）。
    /// </summary>
    public class PetReactionConsumer : MonoBehaviour
    {
        PetBehaviorController _behavior;
        PetChatUIController _chatUI;
        PaimonChatSession _session;
        float _nextPollAt;
        long _lastSeq;

        // 文本反应生成状态
        readonly List<string> _pendingDescs = new();   // 待生成的事件描述（累积合并）
        float _nextGenAt;                                // 下次允许发起生成的时刻（节流）
        bool _generating;                                // LLM 生成中（防叠请求）
        string _currentFallback;                         // 本批事件的兜底文案（失败时直出最后一条）

        [Tooltip("文本反应生成的最小间隔秒（事件快于生成时自动合并——攒几发一起说）")]
        [InspectorName("生成间隔秒")]
        [SerializeField] private float generateIntervalSec = 2.5f;

        /// <summary>接线（宿主聊天接线后调用；任一引用空=对应反馈通道缺席，其余照常）</summary>
        public void Wire(PetBehaviorController behavior, PetChatUIController chatUI)
        {
            _behavior = behavior;
            _chatUI = chatUI;
            _session = chatUI != null ? chatUI.sessionRef : null;
        }

        void Start()
        {
            // 启动基线=文件当前最大 seq（无文件=0）：往期会话遗留全部跳过；此后只处理更大 seq。
            // 基线必须在启动时确立而非"首次见到文件"——主游戏晚于桌宠启动时，首批事件从 seq 1 开号，
            // 若按首见文件取基线会把它们当存量跳过（丢反应）。
            try
            {
                if (File.Exists(PetReactionChannel.FilePath))
                {
                    foreach (var line in File.ReadAllLines(PetReactionChannel.FilePath))
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        try
                        {
                            var evt = JsonUtility.FromJson<PetReactionChannel.ReactionEvent>(line);
                            if (evt != null && evt.seq > _lastSeq) _lastSeq = evt.seq;
                        }
                        catch { /* 坏行/半行：跳过 */ }
                    }
                }
            }
            catch { /* 读失败：基线 0，最坏重放一轮存量（可接受） */ }
            _nextGenAt = Time.unscaledTime + generateIntervalSec;
        }

        void Update()
        {
            float now = Time.unscaledTime;
            if (now >= _nextPollAt)
            {
                _nextPollAt = now + 0.5f;
                Poll();
            }
            TryGenerate();
        }

        void Poll()
        {
            try
            {
                if (!File.Exists(PetReactionChannel.FilePath)) return;
                foreach (var line in File.ReadAllLines(PetReactionChannel.FilePath))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    PetReactionChannel.ReactionEvent evt = null;
                    try { evt = JsonUtility.FromJson<PetReactionChannel.ReactionEvent>(line); }
                    catch { /* 写半行：跳过，下轮重读按 seq 补收 */ }
                    if (evt == null || evt.seq <= _lastSeq) continue;
                    _lastSeq = evt.seq;

                    // ①动作：即时播（不等 LLM——动作秒到，话语慢半拍更自然）
                    if (!string.IsNullOrEmpty(evt.anim))
                        _behavior?.PlayReaction(evt.anim);

                    // ②文本素材：累积待生成（含 note 事件——结算类事件 desc+note 同发）
                    if (!string.IsNullOrEmpty(evt.eventDesc))
                    {
                        _pendingDescs.Add(evt.eventDesc);
                        if (!string.IsNullOrEmpty(evt.fallback)) _currentFallback = evt.fallback;
                    }

                    // ③历史注记
                    if (!string.IsNullOrEmpty(evt.note))
                        _session?.AppendBackgroundNote(evt.note);
                }
            }
            catch { /* 读撞写锁：本轮放弃，0.5s 后重试 */ }
        }

        /// <summary>文本反应生成：pending 非空 + 到节流点 + LLM 空闲 + 会话空闲 → 合并全部待生成事件
        /// 发起一次流式请求。对话进行中不抢（用户优先，文本错失——动作已播，可接受）。</summary>
        void TryGenerate()
        {
            if (_pendingDescs.Count == 0 || _generating) return;
            if (Time.unscaledTime < _nextGenAt) return;
            var client = _session != null ? _session.clientRef : null;
            if (client == null) { FlushAsFallback(); return; }
            if (_session.IsBusy) { FlushAsFallback(); return; } // 用户对话优先：错失文本（下次事件再生成）

            // 合并事件描述
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < _pendingDescs.Count; i++)
            {
                if (i > 0) sb.Append('；');
                sb.Append(_pendingDescs[i]);
            }
            string events = sb.ToString();
            _pendingDescs.Clear();

            var messages = new System.Collections.Generic.List<PetChatClient.ChatMessage>
            {
                new PetChatClient.ChatMessage("system", _session.PersonaPrompt
                    + "\n现在你不在对话中，是主动向玩家做出反馈。要求：一两句以内、符合派蒙语气、直接说内容不带引号、不要称呼工具名。"),
                new PetChatClient.ChatMessage("user", $"（事件通知）{events}\n请用一句简短的派蒙式反应表达你的感受。"),
            };

            _generating = true;
            _nextGenAt = Time.unscaledTime + generateIntervalSec;

            // 复用会话客户端（key/供应商链路现成）。事件处理器接在 client 上：
            // 用户发起对话时 UI 层会重接这些字段（对话优先，反应被 Abort 属预期）
            _chatUI.BeginProactiveStream();
            client.onContentDelta = delta => _chatUI.ProactiveDelta(delta);
            client.onError = err =>
            {
                _generating = false;
                ShowFallback();
            };
            client.onComplete = full =>
            {
                _generating = false;
                if (string.IsNullOrEmpty(full)) ShowFallback(); // 空回复（异常）也走兜底
                else _chatUI.ProactiveStreamDone(); // 完成通知（UI 起自动淡出计时）
            };
            client.RequestStream(messages);
        }

        /// <summary>兜底直出（无 client/对话中/生成失败）：退化为本地化模板文案</summary>
        void FlushAsFallback()
        {
            _pendingDescs.Clear();
            ShowFallback();
        }

        void ShowFallback()
        {
            if (string.IsNullOrEmpty(_currentFallback)) return;
            _chatUI?.ShowProactive(_currentFallback);
            _currentFallback = null;
        }
    }
}
