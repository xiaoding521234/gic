using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Serialization;

namespace GIC.Pet.Chat
{
    /// <summary>
    /// DeepSeek 对话客户端（docs/19 §6.5，2026-08-28 框架拍板：自研薄 Intent 层——C# HttpClient 直连
    /// OpenAI 兼容端点，不引入 C# agent 框架【2026-08-28 网检结论：SK/MAF/MEAI/LangChain.NET 的
    /// net8+/System.Text.Json 依赖链与 Unity .NET Standard 2.1+IL2CPP 正面冲突，无官方支持无先例；
    /// 同类项目 VPet AI Pet Edition/ChatdollKit 全部走薄层直连】）。
    ///
    /// 职责（薄层，不做编排）：
    /// - OpenAI 兼容 /chat/completions：非流式与流式 SSE（手写解析——Unity 无 EventSource，
    ///   HttpClient.ReadAsStreamAsync + 逐行读 data: 块）
    /// - 消息/工具调用的 JSON 序列化：Newtonsoft（Unity 事实标准 com.unity.nuget.newtonsoft-json，
    ///   System.Text.Json 在 IL2CPP 有 AOT 反射坑——勿用）
    /// - 工具调用（function calling）：deepseek-v4 系全线支持 tool calls（含思考模式）；
    ///   建议开 strict 模式（base_url 用 /beta 端点 + 工具定义 strict:true，服务端校验 JSON Schema）
    ///
    /// 模型名注意（2026-08-28 网检）：deepseek-chat/deepseek-reasoner 已于 2026-07-24 停用——
    /// 默认 deepseek-v4-flash（便宜+并发 2500，桌宠够用），deepseek-v4-pro 留作配置项。
    /// 模型名/端点全部 Inspector 可配，勿硬编码进调用点。
    ///
    /// 密钥管理（2026-08-28 用户拍板：玩家自输 key，存档加密存储）：
    /// ①玩家在设置「派蒙→对话 API Key」输入 → PetApiKeyCrypto 加密存 PlayerSaveData.petApiKeyCipher
    ///   （主进程）+ 同步密文到 pet.json（PetPrefs.对话密文——桌面进程永不读主存档，靠这条共享通道）；
    /// ②本客户端取 key：pet.json 密文解密（两形态通吃，主来源）；
    /// ③开发兜底：环境变量 DEEPSEEK_API_KEY / Inspector override（仅本机，优先级低于存档密文）。
    ///
    /// SSE 解析规格（2026-08-28 网检 big-AGI issue #908 实证）：思考模式先流 reasoning_content
    /// 增量再出正文或 tool_calls 增量——delta 同时含 reasoning_content/content/tool_calls 三通道，
    /// tool_calls 需按 index 拼接（function.name 首块给出、arguments 分片累加）。
    /// </summary>
    public class DeepSeekClient : MonoBehaviour
    {
        [Header("端点与模型（模型名 2026-07-24 已换代，勿用 deepseek-chat）")]
        [Tooltip("OpenAI 兼容端点。strict 工具校验用 https://api.deepseek.com/beta（Beta）")]
        [FormerlySerializedAs("baseUrl")]
        [SerializeField] private string 端点地址 = "https://api.deepseek.com";
        [Tooltip("模型名：deepseek-v4-flash（默认，便宜/并发高）/ deepseek-v4-pro（高质量配置项）")]
        [FormerlySerializedAs("model")]
        [SerializeField] private string 模型名 = "deepseek-v4-flash";
        [Tooltip("温度。陪伴对话 0.7~0.9 自然；工具规划场景可低")]
        [FormerlySerializedAs("temperature")]
        [SerializeField, Range(0f, 2f)] private float 温度 = 0.8f;

        [Header("密钥（留空=读玩家存档密文，再兜底环境变量）")]
        [FormerlySerializedAs("apiKeyOverride")]
        [SerializeField] private string 密钥覆盖 = "";

        [Header("网络")]
        [Tooltip("请求超时秒（流式=首字节超时；流开始后不限）")]
        [FormerlySerializedAs("timeoutSeconds")]
        [SerializeField] private float 超时秒 = 30f;

        // 薄层回调（非 event：字段可直接赋值。约定——会话层只摘自己接的处理器（持引用 -=），
        // 勿整体置 null：UI 层的 正文增量/错误 先于会话接线，整体置 null 会抹掉它们——
        // 2026-08-29 "发送后无回复无报错" 事故实证）
        [Tooltip("流式事件：正文增量（打字机）/思考增量（可忽略显示）/工具调用完成（Intent 层执行）/完成/错误")]
        public Action<string> 正文增量;
        public Action<string> 思考增量;
        public Action<List<ToolCallResult>> 工具调用;
        public Action<string> 完成;       // 全量正文（无工具时=回复全文）
        public Action<string> 错误;

        private HttpClient _网络客户端;

        /// <summary>取 key（2026-08-29 改为不缓存——每请求现读 pet.json 解密）：原"缓存到首次成功"
        /// 有死雷——设置里改 key 后无人调 清除密钥缓存（方法零调用方），游戏内形态实例不重启=永远用
        /// 旧 key（上轮被截断的残 key 重输也无效）。聊天是用户触发的低频操作，每请求一次小文件读+
        /// AES 解密开销可忽略，换来"改 key 即生效"。优先级：Inspector override > 存档密文 > 环境变量。</summary>
        private string 密钥
        {
            get
            {
                if (!string.IsNullOrEmpty(密钥覆盖)) return 密钥覆盖;
                // 玩家设置的 key：设置界面写入 pet.json 对话密文（桌面进程与主进程共用 persistentDataPath）
                string 存档密文 = PetPrefs.读取().对话密文;
                string 明文 = PetApiKeyCrypto.解密(存档密文);
                if (!string.IsNullOrEmpty(明文)) return 明文;
                // 开发兜底：环境变量（编辑器/本机调试）
                return Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
            }
        }

        // ---- OpenAI 兼容消息模型（Newtonsoft 序列化） ----
        // 命名规范例外（2026-08-29 统一规范）：本节 DTO 的公有字段名=线上 JSON 协议键（role/content/
        // tool_calls/id/name/arguments 等，Newtonsoft 按字段名序列化），保持英文勿改中文；
        // 类名英文=项目类名惯例。

        [Serializable]
        public class ChatMessage
        {
            // role: system / user / assistant / tool
            public string role;
            public string content;
            [NonSerialized] public List<ToolCallResult> tool_calls; // assistant 消息的工具调用
            [NonSerialized] public string tool_call_id;             // role=tool 回执
            public ChatMessage(string r, string c) { role = r; content = c; }
        }

        [Serializable]
        public class ToolCallResult
        {
            public string id;
            public int index;
            public string type = "function";
            public FunctionCall function = new FunctionCall();
            [Serializable]
            public class FunctionCall
            {
                public string name;
                public string arguments; // JSON 字符串（分片累加后完整）
            }
        }

        [Serializable]
        public class ToolDefinition
        {
            public string type = "function";
            public ToolFunction function = new ToolFunction();
            [Serializable]
            public class ToolFunction
            {
                public string name;
                public string description;
                public string parameters; // JSON Schema 字符串（Newtonsoft JToken 解析后发送）
            }
        }

        void Awake()
        {
            _网络客户端 = new HttpClient(new HttpClientHandler());
            _网络客户端.Timeout = TimeSpan.FromSeconds(超时秒);
            _网络客户端.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        void OnDestroy()
        {
            _网络客户端?.Dispose();
            _网络客户端 = null;
        }

        /// <summary>流式对话（桌宠主路径：打字机气泡+工具指令）。可取消（CancellationToken）。
        /// 完成时 完成 给全量正文；有工具调用时 工具调用 给解析好的调用列表（Intent 层执行后
        /// 把 role:"tool" 结果 append 进历史再发起下一轮流式——工具循环由调用方驱动，本层不自动循环）。</summary>
        public async void 请求流式(List<ChatMessage> 消息列表, List<ToolDefinition> 工具列表 = null, CancellationToken 取消令牌 = default)
        {
            if (string.IsNullOrEmpty(密钥))
            {
                错误?.Invoke("未设置对话 API Key（设置→派蒙→对话 API Key；开发可用环境变量 DEEPSEEK_API_KEY）");
                return;
            }
            try
            {
                var 请求体 = 构建请求体(消息列表, 工具列表, 流式: true);
                using (var 请求 = new HttpRequestMessage(HttpMethod.Post, 端点地址.TrimEnd('/') + "/chat/completions"))
                {
                    请求.Content = new StringContent(请求体, Encoding.UTF8, "application/json");
                    请求.Headers.Authorization = new AuthenticationHeaderValue("Bearer", 密钥);
                    using (var 响应 = await _网络客户端.SendAsync(请求, HttpCompletionOption.ResponseHeadersRead, 取消令牌))
                    {
                        响应.EnsureSuccessStatusCode();
                        await 解析流式响应(响应, 取消令牌);
                    }
                }
            }
            catch (Exception e)
            {
                if (!(e is OperationCanceledException))
                    错误?.Invoke(e.Message);
            }
        }

        // ---- 内部：请求体与 SSE ----

        private string 构建请求体(List<ChatMessage> 消息列表, List<ToolDefinition> 工具列表, bool 流式)
        {
            var 根对象 = new Newtonsoft.Json.Linq.JObject
            {
                ["model"] = 模型名,
                ["messages"] = 序列化消息(消息列表),
                ["stream"] = 流式,
                ["temperature"] = 温度,
            };
            if (工具列表 != null && 工具列表.Count > 0)
            {
                var 工具数组 = new Newtonsoft.Json.Linq.JArray();
                foreach (var 工具 in 工具列表)
                    工具数组.Add(new Newtonsoft.Json.Linq.JObject
                    {
                        ["type"] = "function",
                        ["function"] = new Newtonsoft.Json.Linq.JObject
                        {
                            ["name"] = 工具.function.name,
                            ["description"] = 工具.function.description,
                            ["parameters"] = Newtonsoft.Json.Linq.JObject.Parse(工具.function.parameters),
                        }
                    });
                根对象["tools"] = 工具数组;
            }
            return 根对象.ToString(Newtonsoft.Json.Formatting.None);
        }

        /// <summary>消息序列化（含 assistant.tool_calls / role:"tool" 回执——薄层全量支持，勿裁剪）</summary>
        internal static Newtonsoft.Json.Linq.JArray 序列化消息(List<ChatMessage> 消息列表)
        {
            var 数组 = new Newtonsoft.Json.Linq.JArray();
            foreach (var 消息 in 消息列表)
            {
                var 消息对象 = new Newtonsoft.Json.Linq.JObject { ["role"] = 消息.role };
                if (消息.content != null) 消息对象["content"] = 消息.content; else 消息对象["content"] = "";
                if (消息.tool_calls != null && 消息.tool_calls.Count > 0)
                {
                    var 调用数组 = new Newtonsoft.Json.Linq.JArray();
                    foreach (var 调用 in 消息.tool_calls)
                        调用数组.Add(new Newtonsoft.Json.Linq.JObject
                        {
                            ["id"] = 调用.id,
                            ["type"] = "function",
                            ["function"] = new Newtonsoft.Json.Linq.JObject
                            {
                                ["name"] = 调用.function.name,
                                ["arguments"] = 调用.function.arguments,
                            }
                        });
                    消息对象["tool_calls"] = 调用数组;
                }
                if (!string.IsNullOrEmpty(消息.tool_call_id)) 消息对象["tool_call_id"] = 消息.tool_call_id;
                数组.Add(消息对象);
            }
            return 数组;
        }

        /// <summary>SSE 流式响应逐行解析（2026-08-28 网检规格）：delta 三通道（reasoning_content 思考
        /// 增量 / content 正文增量 / tool_calls 按 index 拼接：name 首块、arguments 分片累加）；
        /// data: [DONE] 结束。finish_reason=tool_calls 时把拼接结果经 工具调用 交 Intent 层。</summary>
        private async Task 解析流式响应(HttpResponseMessage 响应, CancellationToken 取消令牌)
        {
            var 正文拼接 = new StringBuilder();
            var 工具调用表 = new Dictionary<int, ToolCallResult>();
            string 结束原因 = null;

            using (var 流 = await 响应.Content.ReadAsStreamAsync())
            using (var 读取器 = new System.IO.StreamReader(流, Encoding.UTF8))
            {
                while (!读取器.EndOfStream && !取消令牌.IsCancellationRequested)
                {
                    string 行 = await 读取器.ReadLineAsync();
                    if (string.IsNullOrEmpty(行) || !行.StartsWith("data:", StringComparison.Ordinal)) continue;
                    string 数据 = 行.Substring(5).Trim();
                    if (数据 == "[DONE]") break;

                    var 块 = Newtonsoft.Json.Linq.JObject.Parse(数据);
                    var 增量 = 块["choices"]?[0]?["delta"];
                    if (增量 == null) continue;

                    if (增量["reasoning_content"] is Newtonsoft.Json.Linq.JValue 思考值 && 思考值.Value != null)
                        思考增量?.Invoke(思考值.Value<string>());

                    if (增量["content"] is Newtonsoft.Json.Linq.JValue 正文值 && 正文值.Value != null)
                    {
                        string 片段 = 正文值.Value<string>();
                        正文拼接.Append(片段);
                        正文增量?.Invoke(片段);
                    }

                    if (增量["tool_calls"] is Newtonsoft.Json.Linq.JArray 调用块数组)
                    {
                        foreach (var 调用块 in 调用块数组)
                        {
                            int 序号 = 调用块["index"]?.Value<int>() ?? 0;
                            if (!工具调用表.TryGetValue(序号, out var 调用结果))
                                工具调用表[序号] = 调用结果 = new ToolCallResult { index = 序号 };
                            if (调用块["id"] is Newtonsoft.Json.Linq.JValue 标识值 && 标识值.Value != null)
                                调用结果.id = 标识值.Value<string>();
                            var 函数块 = 调用块["function"];
                            if (函数块?["name"] is Newtonsoft.Json.Linq.JValue 名称值 && 名称值.Value != null)
                                调用结果.function.name = 名称值.Value<string>();
                            if (函数块?["arguments"] is Newtonsoft.Json.Linq.JValue 参数值 && 参数值.Value != null)
                                调用结果.function.arguments = (调用结果.function.arguments ?? "") + 参数值.Value<string>();
                        }
                    }

                    var 结束原因块 = 块["choices"]?[0]?["finish_reason"];
                    if (结束原因块 is Newtonsoft.Json.Linq.JValue 结束原因值 && 结束原因值.Value != null)
                        结束原因 = 结束原因值.Value<string>();
                }
            }

            if (!string.IsNullOrEmpty(结束原因) && 结束原因 == "tool_calls")
            {
                var 调用列表 = new List<ToolCallResult>(工具调用表.Values);
                调用列表.Sort((a, b) => a.index.CompareTo(b.index));
                工具调用?.Invoke(调用列表);
            }
            完成?.Invoke(正文拼接.ToString());
        }
    }
}
