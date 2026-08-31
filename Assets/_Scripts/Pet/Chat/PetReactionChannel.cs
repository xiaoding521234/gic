using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace GIC.Pet.Chat
{
    /// <summary>
    /// 派蒙反应通道（2026-08-30，AI 抽卡配套）：主进程产生"派蒙反应事件"（气泡文本+单次动作+LLM 注记），
    /// 桌面宠/游戏内宠两形态共用同一条消费路径——persistentDataPath/pet_react.jsonl 追加式 JSON 行
    ///（与 PetIntentIpc 文件通道同构，方向相反：主进程→宠物）。
    ///
    /// 协议：每行一个 JSON 对象带单调递增 seq。seq 跨主进程重启单调（首写时从文件尾行续号）——
    /// 宠物进程消费方按"启动时读到的最大 seq 为基线，只处理更大的"取增量，重启竞态下不丢不错。
    /// 写半行被读到：解析失败跳过，下轮重读全文按 seq 补收，天然不丢事件。
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
            public string text; // 气泡文本（空=不显示）
            public string anim; // 单次动作 clip 全名（空=不播）
            public string note; // LLM 后台注记（空=不注入会话历史）
        }

        public static string FilePath => Path.Combine(Application.persistentDataPath, "pet_react.jsonl");

        static long _seq;
        static bool _seqInitialized;
        const int HousekeepingLines = 500; // 超过即瘦身（只留末 100 行，seq 保留不重排）
        const int KeepLines = 100;

        /// <summary>产生一条反应事件（主进程调用；追加写。反应是尽力而为的反馈，失败只告警）</summary>
        public static void Push(string text, string anim = null, string note = null)
        {
            try
            {
                EnsureSeqInitialized();
                var evt = new ReactionEvent { seq = ++_seq, text = text ?? "", anim = anim ?? "", note = note ?? "" };
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
    /// 反应消费组件（两形态宿主各挂一个，接聊天 后接线）：0.5s 轮询通道增量入队，
    /// 按 ≥2.2s 间隔依次派发（动作+气泡+LLM 注记三路独立，引用空=该路缺席）。
    /// 启动基线=首次轮询读到的最大 seq（跳过往期会话遗留），此后只处理更大 seq。
    /// </summary>
    public class PetReactionConsumer : MonoBehaviour
    {
        PetBehaviorController _behavior;
        PetChatUIController _chatUI;
        PaimonChatSession _session;
        readonly Queue<PetReactionChannel.ReactionEvent> _pending = new();
        float _nextPollAt;
        float _nextDispatchAt;
        long _lastSeq;

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
        }

        void Update()
        {
            float now = Time.unscaledTime;
            if (now >= _nextPollAt)
            {
                _nextPollAt = now + 0.5f;
                Poll();
            }
            if (_pending.Count > 0 && now >= _nextDispatchAt)
            {
                _nextDispatchAt = now + 2.2f; // 相邻反应至少隔 2.2s：不轰炸，也让动作播得完
                Dispatch(_pending.Dequeue());
            }
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
                    if (evt == null) continue;
                    if (evt.seq > _lastSeq)
                    {
                        _lastSeq = evt.seq;
                        _pending.Enqueue(evt);
                    }
                }
            }
            catch { /* 读撞写锁：本轮放弃，0.5s 后重试 */ }
        }

        void Dispatch(PetReactionChannel.ReactionEvent evt)
        {
            if (!string.IsNullOrEmpty(evt.anim))
                _behavior?.PlayReaction(evt.anim);
            // 气泡文本：LLM 流式回复进行中不打扰（会话文本会被打字机追加混排）——动作照播，文本错失可接受
            if (!string.IsNullOrEmpty(evt.text) && (_session == null || !_session.IsBusy))
                _chatUI?.ShowProactive(evt.text);
            if (!string.IsNullOrEmpty(evt.note))
                _session?.AppendBackgroundNote(evt.note);
        }
    }
}
