using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Serialization;

namespace GIC.Pet.Chat
{
    /// <summary>
    /// 派蒙对话会话（docs/19 §6.5）：人设 system prompt（稳定前缀——DeepSeek 上下文缓存命中价约为
    /// 未命中的 1/31，人设卡保持逐字节稳定）+ 滑窗对话历史 + 长期记忆（Mem0 式：LLM 经 memory_update
    /// 工具写入/更新事实条目，JSON 文件持久化，规模小到全量注入&lt;2K token——不上向量库不上摘要管线，
    /// 2026-08-28 网检拍板）。
    /// 会话消息模型与工具定义经 PetChatClient 序列化；工具循环（收到 tool_calls→执行→append role:"tool"→
    /// 再请求）由本组件驱动。
    /// </summary>
    public class PaimonChatSession : MonoBehaviour
    {
        [Header("人设（system prompt——保持稳定吃缓存，改人设=缓存全失效）")]
        [TextArea(3, 10)]
        [InspectorName("人设提示")]
        [SerializeField] private string personaPrompt = "你是派蒙——原神中旅行者的应急食品兼最好的伙伴。性格：活泼、贪吃、有点小得意、对摩拉和美食毫无抵抗力，但关键时刻永远支持旅行者。说话风格：短句、口语化、常自称'派蒙'，偶尔用'欸嘿'，对旅行者称呼'你'。回答保持简短（一般不超过两三句话），符合派蒙的语气。这是一款原神题材的自走棋卡牌游戏（GIC），你是玩家的桌面/游戏内伙伴。";

        [Header("历史窗口")]
        [Tooltip("保留最近 N 轮原文（1 轮=user+assistant）；更早的历史直接丢弃（长期记忆由 memory_update 承担）")]
        [InspectorName("历史轮数")]
        [SerializeField] private int historyRounds = 12;

        [Header("长期记忆（Mem0 式工具写入）")]
        [Tooltip("长期记忆上限条数（满时丢最旧的）——规模小到全量注入 system prompt")]
        [InspectorName("记忆上限")]
        [SerializeField] private int memoryCap = 32;
        [Tooltip("持久化文件名（persistentDataPath 下；桌宠进程独立文件，不碰主存档——双进程铁律）")]
        [InspectorName("记忆文件名")]
        [SerializeField] private string memoryFileName = "pet_chat_memory.json";

        [Header("引用")]
        [InspectorName("客户端")]
        [SerializeField] private PetChatClient client;

        /// <summary>客户端公开只读（UI 层发送前接流式事件）</summary>
        public PetChatClient clientRef => client;

        /// <summary>人设提示词公开只读（反应生成复用——PetReactionConsumer 按同一人设现编反馈）</summary>
        public string PersonaPrompt => personaPrompt;

        // 运行时状态
        private readonly List<PetChatClient.ChatMessage> _history = new List<PetChatClient.ChatMessage>();
        private readonly List<string> _longTermMemory = new List<string>();
        private List<PetChatClient.ToolDefinition> _toolTable = new List<PetChatClient.ToolDefinition>();
        private Func<string, string, string> _toolExecutor; // (工具名, 参数JSON) → 结果JSON
        private bool _busy;
        // 会话层事件处理器（持引用可摘——工具循环多轮重接防堆叠；**禁止整体置 null**，见 组装消息并请求）
        private Action<List<PetChatClient.ToolCallResult>> _toolCallHandler;
        private Action<string> _completeHandler;
        private Action<string> _errorHandler;

        /// <summary>当前是否在等待/流式接收回复（UI 输入禁用用）</summary>
        public bool IsBusy => _busy;

        void Awake()
        {
            if (client == null) client = GetComponent<PetChatClient>();
            LoadLongTermMemory();
        }

        /// <summary>注册工具表与执行器（Intent 层接线：游戏内形态=直调主进程系统；
        /// 桌面形态=走 IPC 转发到主进程。工具集由调用方组装，本层不关心语义）</summary>
        public void RegisterTools(List<PetChatClient.ToolDefinition> tools, Func<string, string, string> executor)
        {
            _toolTable = tools ?? new List<PetChatClient.ToolDefinition>();
            _toolExecutor = executor;
        }

        /// <summary>动作播放回调（宿主接线：LLM 对话自主选动作 2026-08-31）。参数=完整 clip 名；
        /// 宿主接 PlayReaction（拖拽/退场中静默跳过）。null=未接线，do_action 回执错误。</summary>
        [HideInInspector] public Action<string> onPlayAction;

        // ---- LLM 可选动作表（do_action 工具；key=LLM 枚举值 → clip 全名） ----

        public static readonly (string key, string clip, string desc)[] ActionTable =
        {
            ("greet",      "Ani_NPC_Kanban_Paimon_Greet",      "打招呼/迎接"),
            ("clap",       "Ani_NPC_Kanban_Paimon_Clap01",     "拍手/庆祝/叫好"),
            ("show",       "Ani_NPC_Kanban_Paimon_Show_1",     "得意展示/炫耀好东西"),
            ("shy",        "Ani_NPC_Kanban_Paimon_Shy01AS",    "害羞/不好意思"),
            ("anger",      "Ani_NPC_Kanban_Paimon_Anger",      "生气/不满/抗议"),
            ("confuse",    "Ani_NPC_Kanban_Paimon_Confuse01AS","困惑/不解/犯难"),
            ("nod",        "Ani_NPC_Kanban_Paimon_Nod01",      "点头/赞同/肯定"),
            ("shake_head", "Ani_NPC_Kanban_Paimon_ShakeHead01","摇头/否认/惋惜"),
            ("sneer",      "Ani_NPC_Kanban_Paimon_Sneer01",    "撇嘴/吐槽/小嫌弃"),
            ("hope",       "Ani_NPC_Kanban_Paimon_Hope",       "期待/盼望/憧憬"),
            ("refuse",     "Ani_NPC_Kanban_Paimon_Refuse01",   "拒绝/才不要"),
        };

        /// <summary>do_action 工具定义（聊天情绪动作——LLM 在回复时自主挑选；宿主记得同时接 onPlayAction）</summary>
        public static PetChatClient.ToolDefinition DoActionTool()
        {
            // enum 数组元素手拼带引号（string.Join 已含引号分隔符——勿再用 Replace 转义）
            var enums = string.Join(",", System.Linq.Enumerable.Select(ActionTable, a => $"\"{a.key}\""));
            var descs = string.Join("；", System.Linq.Enumerable.Select(ActionTable, a => $"{a.key}={a.desc}"));
            return new PetChatClient.ToolDefinition
            {
                function = new PetChatClient.ToolDefinition.ToolFunction
                {
                    name = "do_action",
                    description = $"让你说话时身体也做出对应的动作。当你这句回复带有明显情绪（庆祝、害羞、生气、困惑、期待等）时，选一个最贴合的动作一起表达。每次回复最多调用一次；普通平淡的回复不用调用。可选动作：{descs}。",
                    parameters = $"{{\"type\":\"object\",\"properties\":{{\"action\":{{\"type\":\"string\",\"enum\":[{enums}],\"description\":\"要做的动作\"}}}},\"required\":[\"action\"]}}",
                }
            };
        }

        /// <summary>用户输入 → 流式回复（含工具循环）。事件转发客户端流式事件。
        /// 工具循环：收到 tool_calls → 本地执行 → append assistant(tool_calls)+tool 回执 → 再请求，
        /// 直到无工具调用（最多 3 层防失控）。</summary>
        public void Send(string userInput)
        {
            if (_busy || client == null) return;
            _busy = true;
            SendInternal(userInput, 0);
        }

        private void SendInternal(string userInput, int toolDepth)
        {
            if (!string.IsNullOrEmpty(userInput))
            {
                _history.Add(new PetChatClient.ChatMessage("user", userInput));
            }
            TrimHistory();
            BuildMessagesAndRequest(toolDepth);
        }

        /// <summary>滑窗历史物理封顶（2026-08-31 修复"只增不裁"内存缓增）：保留最近 历史轮数*4 条
        ///（user+assistant+工具回执轮），更旧的从 List 头部移除——与 BuildMessagesAndRequest 的
        /// 发送滑窗取值完全一致（取的就是最后 N 条），裁掉头部不改变发送内容。</summary>
        void TrimHistory()
        {
            int retain = historyRounds * 4;
            if (_history.Count > retain) _history.RemoveRange(0, _history.Count - retain);
        }

        private void BuildMessagesAndRequest(int toolDepth)
        {
            var msgTable = new List<PetChatClient.ChatMessage>();
            msgTable.Add(new PetChatClient.ChatMessage("system", BuildSystemPrompt()));
            // 滑窗：保留最近 历史轮数*2 条（user+assistant 成对；含工具回执轮）
            int retainCount = Mathf.Min(_history.Count, historyRounds * 4);
            msgTable.AddRange(_history.Skip(_history.Count - retainCount));

            // 事件接线（2026-08-29 修"发送后无回复无报错"）：旧版把客户端事件整体置 null 再重接——
            // 但 UI 层（PetChatUIController.发送）先一步接了 正文增量（打字机）与 错误（气泡报错），
            // 整体置 null 把它们一并抹掉=流式正文与错误全部静默（气泡停在"…"数秒后淡出）。
            // 现改为只摘会话**自己**的旧处理器（防工具循环多轮堆叠），UI 层处理器不动。
            if (_toolCallHandler != null) client.onToolCalls -= _toolCallHandler;
            if (_completeHandler != null) client.onComplete -= _completeHandler;
            if (_errorHandler != null) client.onError -= _errorHandler;

            _toolCallHandler = callList =>
            {
                try
                {
                    // assistant 消息带 tool_calls 进历史（OpenAI 协议：下一轮要回执）
                    var assistantMsg = new PetChatClient.ChatMessage("assistant", "") { tool_calls = callList };
                    _history.Add(assistantMsg);
                    foreach (var call in callList)
                    {
                        string result = "{}";
                        try { result = ExecuteTool(call.function.name, call.function.arguments); }
                        catch (Exception e) { result = Newtonsoft.Json.JsonConvert.SerializeObject(new { error = e.Message }); }
                        _history.Add(new PetChatClient.ChatMessage("tool", result) { tool_call_id = call.id });
                    }
                    if (toolDepth < 3)
                    {
                        BuildMessagesAndRequest(toolDepth + 1); // 工具循环：带着结果再请求
                    }
                }
                catch (Exception e) { Debug.LogError($"[PetChat] 工具循环异常: {e}"); _busy = false; }
            };

            _completeHandler = 全量 =>
            {
                // 正常完成：assistant 回复进历史
                if (!string.IsNullOrEmpty(全量))
                    _history.Add(new PetChatClient.ChatMessage("assistant", 全量));
                // 注意：工具调用轮的 完成 全量为空，不进历史（assistant 消息已在工具回调里登记）
                _busy = false;
            };

            _errorHandler = 错误信息 =>
            {
                Debug.LogWarning($"[PetChat] 对话请求错误: {错误信息}");
                _busy = false;
            };

            client.onToolCalls += _toolCallHandler;
            client.onComplete += _completeHandler;
            client.onError += _errorHandler;

            client.RequestStream(msgTable, _toolTable.Count > 0 ? _toolTable : null);
        }

        private string ExecuteTool(string toolName, string args)
        {
            // 本地工具（宠物进程内直接消化，不经注册执行器/IPC）：
            // ①memory_update 记忆 ②do_action 情绪动作（模型在本进程，绝不能转发主进程）
            if (toolName == "memory_update")
            {
                try
                {
                    var paramObj = Newtonsoft.Json.Linq.JObject.Parse(args);
                    return WriteMemory(paramObj["content"]?.Value<string>(), paramObj["op"]?.Value<string>() ?? "add");
                }
                catch (Exception e) { return Newtonsoft.Json.JsonConvert.SerializeObject(new { error = e.Message }); }
            }
            if (toolName == "do_action")
            {
                try
                {
                    var paramObj = Newtonsoft.Json.Linq.JObject.Parse(args);
                    string key = paramObj["action"]?.Value<string>() ?? "";
                    var entry = Array.Find(ActionTable, a => a.key == key);
                    if (entry.clip == null)
                        return Newtonsoft.Json.JsonConvert.SerializeObject(new { error = $"未知动作 {key}" });
                    if (onPlayAction == null)
                        return Newtonsoft.Json.JsonConvert.SerializeObject(new { error = "动作播放未接线" });
                    onPlayAction.Invoke(entry.clip);
                    return "{\"ok\":true,\"message\":\"已播放动作\"}";
                }
                catch (Exception e) { return Newtonsoft.Json.JsonConvert.SerializeObject(new { error = e.Message }); }
            }
            return _toolExecutor != null ? _toolExecutor(toolName, args) : "{}";
        }

        /// <summary>system prompt = 人设 + 长期记忆 + 输出约定（长期记忆变化会破坏缓存——可接受：
        /// 记忆更新频率远低于对话频率）</summary>
        private string BuildSystemPrompt()
        {
            var assembler = new System.Text.StringBuilder();
            assembler.Append(personaPrompt);
            if (_longTermMemory.Count > 0)
            {
                assembler.Append("\n\n关于旅行者的长期记忆：\n");
                foreach (var entry in _longTermMemory) assembler.Append("- ").Append(entry).Append('\n');
            }
            return assembler.ToString();
        }

        // ---- 长期记忆（JSON 文件持久化，persistentDataPath） ----

        /// <summary>记忆写入（memory_update 工具的执行体；也供本地代码调用）</summary>
        public string WriteMemory(string content, string op = "add")
        {
            content = content?.Trim();
            if (string.IsNullOrEmpty(content)) return "{}";
            if (op == "delete")
            {
                _longTermMemory.RemoveAll(m => m.Contains(content));
            }
            else
            {
                // 去重：已存在相近条目则替换
                _longTermMemory.RemoveAll(m => m.Contains(content) || content.Contains(m));
                _longTermMemory.Add(content);
                while (_longTermMemory.Count > memoryCap) _longTermMemory.RemoveAt(0);
            }
            SaveLongTermMemory();
            return "{\"ok\":true}";
        }

        public IReadOnlyList<string> longTermMemories => _longTermMemory;

        private void LoadLongTermMemory()
        {
            try
            {
                string path = System.IO.Path.Combine(Application.persistentDataPath, memoryFileName);
                if (System.IO.File.Exists(path))
                {
                    var memoryList = Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(System.IO.File.ReadAllText(path));
                    if (memoryList != null) _longTermMemory.AddRange(memoryList);
                }
            }
            catch (Exception e) { Debug.LogWarning($"[PetChat] 记忆载入失败: {e.Message}"); }
        }

        private void SaveLongTermMemory()
        {
            try
            {
                string path = System.IO.Path.Combine(Application.persistentDataPath, memoryFileName);
                System.IO.File.WriteAllText(path, Newtonsoft.Json.JsonConvert.SerializeObject(_longTermMemory));
            }
            catch (Exception e) { Debug.LogWarning($"[PetChat] 记忆保存失败: {e.Message}"); }
        }

        /// <summary>内置记忆工具定义（Intent 层组装工具表时并入）</summary>
        public static PetChatClient.ToolDefinition MemoryToolDefinition()
        {
            return new PetChatClient.ToolDefinition
            {
                function = new PetChatClient.ToolDefinition.ToolFunction
                {
                    name = "memory_update",
                    description = "记住或更新关于旅行者（用户）的长期事实，供以后对话使用。只在有值得长期记住的新信息时调用（偏好/习惯/重要事件），闲聊不要调用。",
                    parameters = "{\"type\":\"object\",\"properties\":{\"content\":{\"type\":\"string\",\"description\":\"要记住的事实，一句话\"},\"op\":{\"type\":\"string\",\"enum\":[\"add\",\"delete\"],\"description\":\"add=记住/更新，delete=删除\"}},\"required\":[\"content\"]}",
                }
            };
        }

        /// <summary>清空会话历史（长期记忆保留）</summary>
        public void ClearHistory() => _history.Clear();

        /// <summary>预热（首次打开输入条时 UI 层调用一次）：用与真实对话一致的 system 前缀发
        /// max_tokens=1 极小请求——提前建 DNS/TLS 连接与供应商前缀缓存，缓解"首次对话明显
        /// 慢于后续"（2026-09-01 用户实测反馈）。不进历史、不触发任何回调。</summary>
        public void Prewarm()
        {
            if (client == null || _busy) return;
            client.SendWarmup(new List<PetChatClient.ChatMessage>
            {
                new PetChatClient.ChatMessage("system", BuildSystemPrompt()),
                new PetChatClient.ChatMessage("user", "ping"),
            });
        }

        /// <summary>后台事件注记（AI 抽卡结果等，2026-08-30）：以 system 消息进历史——
        /// 不触发回复，但下次对话时 LLM 可引用（用户问"刚才抽得怎么样"能答上）。
        /// 五家供应商均为 OpenAI 兼容，messages 中段 system 消息合法。</summary>
        public void AppendBackgroundNote(string note)
        {
            if (string.IsNullOrEmpty(note)) return;
            _history.Add(new PetChatClient.ChatMessage("system", $"（事件通知，无需回应）{note}"));
            TrimHistory(); // 后台注记也计入滑窗预算（高频事件不撑大 List）
        }
    }
}
