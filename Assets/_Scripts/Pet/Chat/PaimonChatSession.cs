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
            BuildMessagesAndRequest(toolDepth);
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
            // 内置记忆工具直接消化；其余转发给注册的执行器（Intent 层）
            if (toolName == "memory_update")
            {
                try
                {
                    var paramObj = Newtonsoft.Json.Linq.JObject.Parse(args);
                    return WriteMemory(paramObj["content"]?.Value<string>(), paramObj["op"]?.Value<string>() ?? "add");
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
                    var 记忆数组 = Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(System.IO.File.ReadAllText(path));
                    if (记忆数组 != null) _longTermMemory.AddRange(记忆数组);
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
    }
}
