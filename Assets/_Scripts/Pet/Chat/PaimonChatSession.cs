using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GIC.Pet.Chat
{
    /// <summary>
    /// 派蒙对话会话（docs/19 §6.5）：人设 system prompt（稳定前缀——DeepSeek 上下文缓存命中价约为
    /// 未命中的 1/31，人设卡保持逐字节稳定）+ 滑窗对话历史 + 长期记忆（Mem0 式：LLM 经 memory_update
    /// 工具写入/更新事实条目，JSON 文件持久化，规模小到全量注入&lt;2K token——不上向量库不上摘要管线，
    /// 2026-08-28 网检拍板）。
    /// 会话消息模型与工具定义经 DeepSeekClient 序列化；工具循环（收到 tool_calls→执行→append role:"tool"→
    /// 再请求）由本组件驱动。
    /// </summary>
    public class PaimonChatSession : MonoBehaviour
    {
        [Header("人设（system prompt——保持稳定吃缓存，改人设=缓存全失效）")]
        [TextArea(3, 10)]
        [SerializeField] private string 人设提示 = "你是派蒙——原神中旅行者的应急食品兼最好的伙伴。性格：活泼、贪吃、有点小得意、对摩拉和美食毫无抵抗力，但关键时刻永远支持旅行者。说话风格：短句、口语化、常自称'派蒙'，偶尔用'欸嘿'，对旅行者称呼'你'。回答保持简短（一般不超过两三句话），符合派蒙的语气。这是一款原神题材的自走棋卡牌游戏（GIC），你是玩家的桌面/游戏内伙伴。";

        [Header("历史窗口")]
        [Tooltip("保留最近 N 轮原文（1 轮=user+assistant）；更早的历史直接丢弃（长期记忆由 memory_update 承担）")]
        [SerializeField] private int 历史轮数 = 12;

        [Header("长期记忆（Mem0 式工具写入）")]
        [Tooltip("长期记忆上限条数（满时丢最旧的）——规模小到全量注入 system prompt")]
        [SerializeField] private int 记忆上限 = 32;
        [Tooltip("持久化文件名（persistentDataPath 下；桌宠进程独立文件，不碰主存档——双进程铁律）")]
        [SerializeField] private string 记忆文件名 = "pet_chat_memory.json";

        [Header("引用")]
        [SerializeField] private DeepSeekClient 客户端;

        /// <summary>客户端公开只读（UI 层发送前接流式事件）</summary>
        public DeepSeekClient 客户端引用 => 客户端;

        // 运行时状态
        private readonly List<DeepSeekClient.ChatMessage> _历史 = new List<DeepSeekClient.ChatMessage>();
        private readonly List<string> _长期记忆 = new List<string>();
        private List<DeepSeekClient.ToolDefinition> _工具表 = new List<DeepSeekClient.ToolDefinition>();
        private Func<string, string, string> _工具执行器; // (工具名, 参数JSON) → 结果JSON
        private bool _请求中;
        // 会话层事件处理器（持引用可摘——工具循环多轮重接防堆叠；**禁止整体置 null**，见 组装消息并请求）
        private Action<List<DeepSeekClient.ToolCallResult>> _工具调用处理器;
        private Action<string> _完成处理器;
        private Action<string> _错误处理器;

        /// <summary>当前是否在等待/流式接收回复（UI 输入禁用用）</summary>
        public bool 请求中 => _请求中;

        void Awake()
        {
            if (客户端 == null) 客户端 = GetComponent<DeepSeekClient>();
            载入长期记忆();
        }

        /// <summary>注册工具表与执行器（Intent 层接线：游戏内形态=直调主进程系统；
        /// 桌面形态=走 IPC 转发到主进程。工具集由调用方组装，本层不关心语义）</summary>
        public void 注册工具(List<DeepSeekClient.ToolDefinition> 工具列表, Func<string, string, string> 执行器)
        {
            _工具表 = 工具列表 ?? new List<DeepSeekClient.ToolDefinition>();
            _工具执行器 = 执行器;
        }

        /// <summary>用户输入 → 流式回复（含工具循环）。事件转发客户端流式事件。
        /// 工具循环：收到 tool_calls → 本地执行 → append assistant(tool_calls)+tool 回执 → 再请求，
        /// 直到无工具调用（最多 3 层防失控）。</summary>
        public void 发送(string 用户输入)
        {
            if (_请求中 || 客户端 == null) return;
            _请求中 = true;
            发送内部(用户输入, 0);
        }

        private void 发送内部(string 用户输入, int 工具深度)
        {
            if (!string.IsNullOrEmpty(用户输入))
            {
                _历史.Add(new DeepSeekClient.ChatMessage("user", 用户输入));
            }
            组装消息并请求(工具深度);
        }

        private void 组装消息并请求(int 工具深度)
        {
            var 消息表 = new List<DeepSeekClient.ChatMessage>();
            消息表.Add(new DeepSeekClient.ChatMessage("system", 组装系统提示()));
            // 滑窗：保留最近 历史轮数*2 条（user+assistant 成对；含工具回执轮）
            int 保留条数 = Mathf.Min(_历史.Count, 历史轮数 * 4);
            消息表.AddRange(_历史.Skip(_历史.Count - 保留条数));

            // 事件接线（2026-08-29 修"发送后无回复无报错"）：旧版把客户端事件整体置 null 再重接——
            // 但 UI 层（PetChatUIController.发送）先一步接了 正文增量（打字机）与 错误（气泡报错），
            // 整体置 null 把它们一并抹掉=流式正文与错误全部静默（气泡停在"…"数秒后淡出）。
            // 现改为只摘会话**自己**的旧处理器（防工具循环多轮堆叠），UI 层处理器不动。
            if (_工具调用处理器 != null) 客户端.工具调用 -= _工具调用处理器;
            if (_完成处理器 != null) 客户端.完成 -= _完成处理器;
            if (_错误处理器 != null) 客户端.错误 -= _错误处理器;

            _工具调用处理器 = 调用列表 =>
            {
                try
                {
                    // assistant 消息带 tool_calls 进历史（OpenAI 协议：下一轮要回执）
                    var 助手消息 = new DeepSeekClient.ChatMessage("assistant", "") { tool_calls = 调用列表 };
                    _历史.Add(助手消息);
                    foreach (var 调用 in 调用列表)
                    {
                        string 结果 = "{}";
                        try { 结果 = 执行工具(调用.function.name, 调用.function.arguments); }
                        catch (Exception e) { 结果 = Newtonsoft.Json.JsonConvert.SerializeObject(new { error = e.Message }); }
                        _历史.Add(new DeepSeekClient.ChatMessage("tool", 结果) { tool_call_id = 调用.id });
                    }
                    if (工具深度 < 3)
                    {
                        组装消息并请求(工具深度 + 1); // 工具循环：带着结果再请求
                    }
                }
                catch (Exception e) { Debug.LogError($"[PetChat] 工具循环异常: {e}"); _请求中 = false; }
            };

            _完成处理器 = 全量 =>
            {
                // 正常完成：assistant 回复进历史
                if (!string.IsNullOrEmpty(全量))
                    _历史.Add(new DeepSeekClient.ChatMessage("assistant", 全量));
                // 注意：工具调用轮的 完成 全量为空，不进历史（assistant 消息已在工具回调里登记）
                _请求中 = false;
            };

            _错误处理器 = 错误信息 =>
            {
                Debug.LogWarning($"[PetChat] DeepSeek 错误: {错误信息}");
                _请求中 = false;
            };

            客户端.工具调用 += _工具调用处理器;
            客户端.完成 += _完成处理器;
            客户端.错误 += _错误处理器;

            客户端.请求流式(消息表, _工具表.Count > 0 ? _工具表 : null);
        }

        private string 执行工具(string 工具名, string 参数)
        {
            // 内置记忆工具直接消化；其余转发给注册的执行器（Intent 层）
            if (工具名 == "memory_update")
            {
                try
                {
                    var 参数对象 = Newtonsoft.Json.Linq.JObject.Parse(参数);
                    return 记忆写入(参数对象["content"]?.Value<string>(), 参数对象["op"]?.Value<string>() ?? "add");
                }
                catch (Exception e) { return Newtonsoft.Json.JsonConvert.SerializeObject(new { error = e.Message }); }
            }
            return _工具执行器 != null ? _工具执行器(工具名, 参数) : "{}";
        }

        /// <summary>system prompt = 人设 + 长期记忆 + 输出约定（长期记忆变化会破坏缓存——可接受：
        /// 记忆更新频率远低于对话频率）</summary>
        private string 组装系统提示()
        {
            var 拼接器 = new System.Text.StringBuilder();
            拼接器.Append(人设提示);
            if (_长期记忆.Count > 0)
            {
                拼接器.Append("\n\n关于旅行者的长期记忆：\n");
                foreach (var 条目 in _长期记忆) 拼接器.Append("- ").Append(条目).Append('\n');
            }
            return 拼接器.ToString();
        }

        // ---- 长期记忆（JSON 文件持久化，persistentDataPath） ----

        /// <summary>记忆写入（memory_update 工具的执行体；也供本地代码调用）</summary>
        public string 记忆写入(string 内容, string 操作 = "add")
        {
            内容 = 内容?.Trim();
            if (string.IsNullOrEmpty(内容)) return "{}";
            if (操作 == "delete")
            {
                _长期记忆.RemoveAll(m => m.Contains(内容));
            }
            else
            {
                // 去重：已存在相近条目则替换
                _长期记忆.RemoveAll(m => m.Contains(内容) || 内容.Contains(m));
                _长期记忆.Add(内容);
                while (_长期记忆.Count > 记忆上限) _长期记忆.RemoveAt(0);
            }
            保存长期记忆();
            return "{\"ok\":true}";
        }

        public IReadOnlyList<string> 长期记忆列表 => _长期记忆;

        private void 载入长期记忆()
        {
            try
            {
                string 路径 = System.IO.Path.Combine(Application.persistentDataPath, 记忆文件名);
                if (System.IO.File.Exists(路径))
                {
                    var 记忆数组 = Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(System.IO.File.ReadAllText(路径));
                    if (记忆数组 != null) _长期记忆.AddRange(记忆数组);
                }
            }
            catch (Exception e) { Debug.LogWarning($"[PetChat] 记忆载入失败: {e.Message}"); }
        }

        private void 保存长期记忆()
        {
            try
            {
                string 路径 = System.IO.Path.Combine(Application.persistentDataPath, 记忆文件名);
                System.IO.File.WriteAllText(路径, Newtonsoft.Json.JsonConvert.SerializeObject(_长期记忆));
            }
            catch (Exception e) { Debug.LogWarning($"[PetChat] 记忆保存失败: {e.Message}"); }
        }

        /// <summary>内置记忆工具定义（Intent 层组装工具表时并入）</summary>
        public static DeepSeekClient.ToolDefinition 记忆工具定义()
        {
            return new DeepSeekClient.ToolDefinition
            {
                function = new DeepSeekClient.ToolDefinition.ToolFunction
                {
                    name = "memory_update",
                    description = "记住或更新关于旅行者（用户）的长期事实，供以后对话使用。只在有值得长期记住的新信息时调用（偏好/习惯/重要事件），闲聊不要调用。",
                    parameters = "{\"type\":\"object\",\"properties\":{\"content\":{\"type\":\"string\",\"description\":\"要记住的事实，一句话\"},\"op\":{\"type\":\"string\",\"enum\":[\"add\",\"delete\"],\"description\":\"add=记住/更新，delete=删除\"}},\"required\":[\"content\"]}",
                }
            };
        }

        /// <summary>清空会话历史（长期记忆保留）</summary>
        public void 清空历史() => _历史.Clear();
    }
}
