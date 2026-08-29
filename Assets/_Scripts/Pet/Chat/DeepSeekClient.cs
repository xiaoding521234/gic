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
        [InspectorName("端点地址")]
        [SerializeField] private string endpoint = "https://api.deepseek.com";
        [Tooltip("模型名：deepseek-v4-flash（默认，便宜/并发高）/ deepseek-v4-pro（高质量配置项）")]
        [InspectorName("模型名")]
        [SerializeField] private string modelName = "deepseek-v4-flash";
        [Tooltip("温度。陪伴对话 0.7~0.9 自然；工具规划场景可低")]
        [InspectorName("温度")]
        [SerializeField, Range(0f, 2f)] private float temperature = 0.8f;

        [Header("密钥（留空=读玩家存档密文，再兜底环境变量）")]
        [InspectorName("密钥覆盖")]
        [SerializeField] private string apiKeyOverride = "";

        [Header("网络")]
        [Tooltip("请求超时秒（流式=首字节超时；流开始后不限）")]
        [InspectorName("超时秒")]
        [SerializeField] private float timeoutSec = 30f;

        // 薄层回调（非 event：字段可直接赋值。约定——会话层只摘自己接的处理器（持引用 -=），
        // 勿整体置 null：UI 层的 正文增量/错误 先于会话接线，整体置 null 会抹掉它们——
        // 2026-08-29 "发送后无回复无报错" 事故实证）
        [Tooltip("流式事件：正文增量（打字机）/思考增量（可忽略显示）/工具调用完成（Intent 层执行）/完成/错误")]
        public Action<string> onContentDelta;
        public Action<string> onReasoningDelta;
        public Action<List<ToolCallResult>> onToolCalls;
        public Action<string> onComplete;       // 全量正文（无工具时=回复全文）
        public Action<string> onError;

        private HttpClient _http;

        /// <summary>取 key（2026-08-29 改为不缓存——每请求现读 pet.json 解密）：原"缓存到首次成功"
        /// 有死雷——设置里改 key 后无人调 清除密钥缓存（方法零调用方），游戏内形态实例不重启=永远用
        /// 旧 key（上轮被截断的残 key 重输也无效）。聊天是用户触发的低频操作，每请求一次小文件读+
        /// AES 解密开销可忽略，换来"改 key 即生效"。优先级：Inspector override > 存档密文 > 环境变量。</summary>
        private string apiKey
        {
            get
            {
                if (!string.IsNullOrEmpty(apiKeyOverride)) return apiKeyOverride;
                // 玩家设置的 key：设置界面写入 pet.json 对话密文（桌面进程与主进程共用 persistentDataPath）
                string saveCipher = PetPrefs.Load().chatCipher;
                string plain = PetApiKeyCrypto.Decrypt(saveCipher);
                if (!string.IsNullOrEmpty(plain)) return plain;
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
            _http = new HttpClient(new HttpClientHandler());
            _http.Timeout = TimeSpan.FromSeconds(timeoutSec);
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        void OnDestroy()
        {
            _http?.Dispose();
            _http = null;
        }

        /// <summary>流式对话（桌宠主路径：打字机气泡+工具指令）。可取消（CancellationToken）。
        /// 完成时 完成 给全量正文；有工具调用时 工具调用 给解析好的调用列表（Intent 层执行后
        /// 把 role:"tool" 结果 append 进历史再发起下一轮流式——工具循环由调用方驱动，本层不自动循环）。</summary>
        public async void RequestStream(List<ChatMessage> messages, List<ToolDefinition> tools = null, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(apiKey))
            {
                onError?.Invoke("未设置对话 API Key（设置→派蒙→对话 API Key；开发可用环境变量 DEEPSEEK_API_KEY）");
                return;
            }
            try
            {
                var requestBody = BuildRequestBody(messages, tools, stream: true);
                using (var request = new HttpRequestMessage(HttpMethod.Post, endpoint.TrimEnd('/') + "/chat/completions"))
                {
                    request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                    using (var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct))
                    {
                        response.EnsureSuccessStatusCode();
                        await ParseStreamResponse(response, ct);
                    }
                }
            }
            catch (Exception e)
            {
                if (!(e is OperationCanceledException))
                    onError?.Invoke(e.Message);
            }
        }

        // ---- 内部：请求体与 SSE ----

        private string BuildRequestBody(List<ChatMessage> messages, List<ToolDefinition> tools, bool stream)
        {
            var rootObj = new Newtonsoft.Json.Linq.JObject
            {
                ["model"] = modelName,
                ["messages"] = SerializeMessages(messages),
                ["stream"] = stream,
                ["temperature"] = temperature,
            };
            if (tools != null && tools.Count > 0)
            {
                var toolsArr = new Newtonsoft.Json.Linq.JArray();
                foreach (var tool in tools)
                    toolsArr.Add(new Newtonsoft.Json.Linq.JObject
                    {
                        ["type"] = "function",
                        ["function"] = new Newtonsoft.Json.Linq.JObject
                        {
                            ["name"] = tool.function.name,
                            ["description"] = tool.function.description,
                            ["parameters"] = Newtonsoft.Json.Linq.JObject.Parse(tool.function.parameters),
                        }
                    });
                rootObj["tools"] = toolsArr;
            }
            return rootObj.ToString(Newtonsoft.Json.Formatting.None);
        }

        /// <summary>消息序列化（含 assistant.tool_calls / role:"tool" 回执——薄层全量支持，勿裁剪）</summary>
        internal static Newtonsoft.Json.Linq.JArray SerializeMessages(List<ChatMessage> messages)
        {
            var arr = new Newtonsoft.Json.Linq.JArray();
            foreach (var msg in messages)
            {
                var msgObj = new Newtonsoft.Json.Linq.JObject { ["role"] = msg.role };
                if (msg.content != null) msgObj["content"] = msg.content; else msgObj["content"] = "";
                if (msg.tool_calls != null && msg.tool_calls.Count > 0)
                {
                    var callArr = new Newtonsoft.Json.Linq.JArray();
                    foreach (var call in msg.tool_calls)
                        callArr.Add(new Newtonsoft.Json.Linq.JObject
                        {
                            ["id"] = call.id,
                            ["type"] = "function",
                            ["function"] = new Newtonsoft.Json.Linq.JObject
                            {
                                ["name"] = call.function.name,
                                ["arguments"] = call.function.arguments,
                            }
                        });
                    msgObj["tool_calls"] = callArr;
                }
                if (!string.IsNullOrEmpty(msg.tool_call_id)) msgObj["tool_call_id"] = msg.tool_call_id;
                arr.Add(msgObj);
            }
            return arr;
        }

        /// <summary>SSE 流式响应逐行解析（2026-08-28 网检规格）：delta 三通道（reasoning_content 思考
        /// 增量 / content 正文增量 / tool_calls 按 index 拼接：name 首块、arguments 分片累加）；
        /// data: [DONE] 结束。finish_reason=tool_calls 时把拼接结果经 工具调用 交 Intent 层。</summary>
        private async Task ParseStreamResponse(HttpResponseMessage response, CancellationToken ct)
        {
            var contentJoin = new StringBuilder();
            var toolCallTable = new Dictionary<int, ToolCallResult>();
            string finishReason = null;

            using (var 流 = await response.Content.ReadAsStreamAsync())
            using (var reader = new System.IO.StreamReader(流, Encoding.UTF8))
            {
                while (!reader.EndOfStream && !ct.IsCancellationRequested)
                {
                    string line = await reader.ReadLineAsync();
                    if (string.IsNullOrEmpty(line) || !line.StartsWith("data:", StringComparison.Ordinal)) continue;
                    string data = line.Substring(5).Trim();
                    if (data == "[DONE]") break;

                    var block = Newtonsoft.Json.Linq.JObject.Parse(data);
                    var delta = block["choices"]?[0]?["delta"];
                    if (delta == null) continue;

                    if (delta["reasoning_content"] is Newtonsoft.Json.Linq.JValue thinkingValue && thinkingValue.Value != null)
                        onReasoningDelta?.Invoke(thinkingValue.Value<string>());

                    if (delta["content"] is Newtonsoft.Json.Linq.JValue contentValue && contentValue.Value != null)
                    {
                        string clip = contentValue.Value<string>();
                        contentJoin.Append(clip);
                        onContentDelta?.Invoke(clip);
                    }

                    if (delta["tool_calls"] is Newtonsoft.Json.Linq.JArray callBlocks)
                    {
                        foreach (var callBlock in callBlocks)
                        {
                            int serialNum = callBlock["index"]?.Value<int>() ?? 0;
                            if (!toolCallTable.TryGetValue(serialNum, out var callResult))
                                toolCallTable[serialNum] = callResult = new ToolCallResult { index = serialNum };
                            if (callBlock["id"] is Newtonsoft.Json.Linq.JValue idValue && idValue.Value != null)
                                callResult.id = idValue.Value<string>();
                            var 函数块 = callBlock["function"];
                            if (函数块?["name"] is Newtonsoft.Json.Linq.JValue nameValue && nameValue.Value != null)
                                callResult.function.name = nameValue.Value<string>();
                            if (函数块?["arguments"] is Newtonsoft.Json.Linq.JValue paramValue && paramValue.Value != null)
                                callResult.function.arguments = (callResult.function.arguments ?? "") + paramValue.Value<string>();
                        }
                    }

                    var finishReasonBlock = block["choices"]?[0]?["finish_reason"];
                    if (finishReasonBlock is Newtonsoft.Json.Linq.JValue finishReasonValue && finishReasonValue.Value != null)
                        finishReason = finishReasonValue.Value<string>();
                }
            }

            if (!string.IsNullOrEmpty(finishReason) && finishReason == "tool_calls")
            {
                var callList = new List<ToolCallResult>(toolCallTable.Values);
                callList.Sort((a, b) => a.index.CompareTo(b.index));
                onToolCalls?.Invoke(callList);
            }
            onComplete?.Invoke(contentJoin.ToString());
        }
    }
}
