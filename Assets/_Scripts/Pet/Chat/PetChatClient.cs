using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace GIC.Pet.Chat
{
    /// <summary>
    /// 派蒙对话客户端（docs/19 §6.5）：OpenAI 兼容 /chat/completions 薄层——多供应商
    /// （PetChatProviders 注册表：DeepSeek/Kimi/GLM/通义/OpenAI，端点+模型名按设置里选的供应商
    /// 现取，2026-08-29 起不再绑死 DeepSeek）+ SSE 流式解析 + 工具调用（function calling）。
    ///
    /// 传输层=UnityWebRequest + DownloadHandlerScript（2026-08-29 重写，替换 HttpClient）：
    /// HttpClient 版实测"整局卡住→一次性出完整回复"——Unity Mono 的 HttpContent.ReadAsStreamAsync
    /// 沿 .NET Framework 血统**整体缓冲响应**（MS 文档：Task 在全部内容读完后才完成），SSE 增量
    /// 无法逐块送达，且 StreamReader.EndOfStream 的同步读可能落在主线程上；社区通行方案即
    /// UnityWebRequest+DownloadHandlerScript（官方文档：下载在 worker 线程，ReceiveData 回调
    /// 在主线程逐块送达）——打字机气泡吃到的就是真正的流式增量。
    ///
    /// 超时=手工看门狗（UnityWebRequest.timeout 语义含糊会腰斩长流式回复，恒置 0）：
    /// 首字节超过 超时秒 / 流中 90s 无数据 → Abort 并报错；流一旦开始则不限总时长。
    ///
    /// 密钥链路（2026-08-28 用户拍板：玩家自输 key，存档加密存储）：设置界面经 PetApiKeyCrypto
    /// 加密写主存档+pet.json（WriteChatCipher 直写）；本层每次请求 PetPrefs.ReadChatCipher 直读
    /// 磁盘解密（绕进程缓存=改 key 即生效+跨进程新鲜），兜底供应商环境变量（开发用）。
    ///
    /// SSE 解析规格：delta 三通道（reasoning_content 思考增量 / content 正文增量 / tool_calls 按
    /// index 拼接：name 首块、arguments 分片累加），data: [DONE] 结束；HTTP 错误响应体（非 SSE，
    /// 无 data: 前缀）累积在 rawBody 供完成时提取 error.message。
    ///
    /// 薄层回调（非 event：字段可直接赋值）。约定——任何一方只摘自己接的处理器（持引用 -=），
    /// 勿整体置 null：UI 层的 正文增量/错误 先于会话接线，整体置 null 会抹掉它们
    /// （2026-08-29 "发送后无回复无报错" 事故实证）。
    /// </summary>
    public class PetChatClient : MonoBehaviour
    {
        [Header("供应商（设置→派蒙→对话模型供应商；覆盖项留空=按供应商表取）")]
        [Tooltip("端点覆盖（开发调试用；留空=按设置的供应商取 PetChatProviders 表）")]
        [InspectorName("端点覆盖")]
        [SerializeField] private string endpointOverride = "";
        [Tooltip("模型名覆盖（开发调试用；留空=按设置的供应商取表内默认模型）")]
        [InspectorName("模型覆盖")]
        [SerializeField] private string modelOverride = "";
        [Tooltip("温度。陪伴对话 0.7~0.9 自然；工具规划场景可低")]
        [InspectorName("温度")]
        [SerializeField, Range(0f, 2f)] private float temperature = 0.8f;

        [Header("密钥（留空=读玩家存档密文，再兜底供应商环境变量）")]
        [InspectorName("密钥覆盖")]
        [SerializeField] private string apiKeyOverride = "";

        [Header("网络")]
        [Tooltip("首字节超时秒（流开始后改为 90s 无数据才算卡死）")]
        [InspectorName("超时秒")]
        [SerializeField] private float timeoutSec = 30f;

        // 薄层回调（见类头注释的摘挂约定）
        public Action<string> onContentDelta;
        public Action<string> onReasoningDelta;
        public Action<List<ToolCallResult>> onToolCalls;
        public Action<string> onComplete;       // 全量正文（无工具时=回复全文）
        public Action<string> onError;

        // 运行时请求状态（工具循环会串行重入：完成回调里会话层同步发下一轮）
        private UnityWebRequest _activeRequest;
        private bool _watchdogAbort;

        /// <summary>当前供应商（存档值 → 表；非法钳 0）</summary>
        private PetChatProviders.ProviderInfo provider => PetChatProviders.Resolve(PetPrefs.ReadChatProvider());

        private string endpoint => !string.IsNullOrEmpty(endpointOverride)
            ? endpointOverride.TrimEnd('/')
            : provider.baseUrl.TrimEnd('/');

        private string modelName => !string.IsNullOrEmpty(modelOverride) ? modelOverride : provider.model;

        /// <summary>取 key（每请求现读 pet.json 解密——绕缓存：改 key 即生效、跨进程新鲜）。
        /// 优先级：Inspector override > 存档密文 > 供应商环境变量（开发兜底）。</summary>
        private string apiKey
        {
            get
            {
                if (!string.IsNullOrEmpty(apiKeyOverride)) return apiKeyOverride;
                string plain = PetApiKeyCrypto.Decrypt(PetPrefs.ReadChatCipher());
                if (!string.IsNullOrEmpty(plain)) return plain;
                return Environment.GetEnvironmentVariable(provider.envKey);
            }
        }

        // ---- OpenAI 兼容消息模型（Newtonsoft 序列化） ----
        // 命名规范例外（docs/20 §1.1）：本节 DTO 的公有字段名=线上 JSON 协议键（role/content/
        // tool_calls/id/name/arguments 等，Newtonsoft 按字段名序列化），保持英文勿改中文。

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

        void OnDestroy()
        {
            AbortActive();
        }

        /// <summary>中止在途请求（宿主退场/组件销毁时防泄漏）</summary>
        public void AbortActive()
        {
            if (_activeRequest != null && !_activeRequest.isDone)
            {
                _watchdogAbort = true; // 静默：销毁路径不触发 onError
                try { _activeRequest.Abort(); } catch (Exception) { }
            }
            _activeRequest = null;
        }

        /// <summary>流式对话（桌宠主路径：打字机气泡+工具指令）。完成时 完成 给全量正文；
        /// 有工具调用时 工具调用 给解析好的调用列表（会话层执行后把 role:"tool" 结果 append 进
        /// 历史再发起下一轮流式——工具循环由调用方驱动，本层不自动循环）。</summary>
        public void RequestStream(List<ChatMessage> messages, List<ToolDefinition> tools = null)
        {
            string key = apiKey;
            if (string.IsNullOrEmpty(key))
            {
                onError?.Invoke($"未设置对话 API Key（设置→派蒙→对话 API Key；开发可用环境变量 {provider.envKey}）");
                return;
            }

            AbortActive(); // 理论不会重入（会话层 IsBusy 拦截），防御性清场
            _watchdogAbort = false;
            // 每请求重置解析累积器（实例字段复用防 GC）
            _contentJoin.Length = 0;
            _toolCallTable.Clear();
            _finishReason = null;

            var handler = new SseDownloadHandler();
            handler.onSseLine += ProcessSseLine;

            var req = new UnityWebRequest(endpoint + "/chat/completions", "POST");
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(BuildRequestBody(messages, tools, stream: true)))
            {
                contentType = "application/json",
            };
            req.downloadHandler = handler;
            req.SetRequestHeader("Accept", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + key);
            // 显式禁压缩：gzip 过的 SSE 在中间层可能被整段缓冲（流式失效），identity 保证逐块送达
            req.SetRequestHeader("Accept-Encoding", "identity");
            req.timeout = 0; // 超时交手工看门狗（见类头：UnityWebRequest.timeout 会腰斩长流式回复）
            _activeRequest = req;

            StartCoroutine(sendRoutine(req, handler));
        }

        IEnumerator sendRoutine(UnityWebRequest req, SseDownloadHandler handler)
        {
            var op = req.SendWebRequest();
            while (!op.isDone)
            {
                yield return null;
                // 看门狗（每帧查，开销=两次浮点比较）：首字节 超时秒 / 流中 90s 无数据 → Abort
                float idle = Time.realtimeSinceStartup - handler.lastDataAt;
                if (idle > (handler.receivedAny ? 90f : timeoutSec))
                {
                    _watchdogAbort = true;
                    req.Abort();
                    break;
                }
            }
            while (!op.isDone) yield return null; // Abort 后等状态落定

            if (_activeRequest == req) _activeRequest = null;   // 工具循环已换新请求时不碰
            finishRequest(req, handler);
        }

        /// <summary>请求收尾：成功→流已解析完，触发 完成/工具调用；失败→错误文案（含 HTTP 错误体
        /// 里的 error.message 提取）。</summary>
        void finishRequest(UnityWebRequest req, SseDownloadHandler handler)
        {
            try
            {
                if (_watchdogAbort)
                {
                    onError?.Invoke(handler.receivedAny
                        ? $"连接中断（{timeoutSec:0}s 无数据）：请重试"
                        : $"请求超时（{timeoutSec:0}s 未收到首字节）：请检查网络后重试");
                    return;
                }
                if (req.result == UnityWebRequest.Result.ConnectionError
                    || req.result == UnityWebRequest.Result.DataProcessingError)
                {
                    onError?.Invoke(string.IsNullOrEmpty(req.error) ? "网络连接失败" : req.error);
                    return;
                }
                if (req.responseCode >= 400)
                {
                    onError?.Invoke(extractErrorMessage(handler.rawBody.ToString(), req.responseCode));
                    return;
                }
                handler.flushPendingLine(); // 收尾兜底：连接关闭时未带换行的残行
                fireCompletion();
            }
            finally
            {
                req.Dispose();
            }
        }

        // ---- 请求体构建与消息序列化 ----

        private string BuildRequestBody(List<ChatMessage> messages, List<ToolDefinition> tools, bool stream)
        {
            var rootObj = new JObject
            {
                ["model"] = modelName,
                ["messages"] = SerializeMessages(messages),
                ["stream"] = stream,
            };
            // gpt-5 系只支持默认温度 1（发 0.8 直接 400），按供应商开关
            if (provider.sendTemperature) rootObj["temperature"] = temperature;
            if (tools != null && tools.Count > 0)
            {
                var toolsArr = new JArray();
                foreach (var tool in tools)
                    toolsArr.Add(new JObject
                    {
                        ["type"] = "function",
                        ["function"] = new JObject
                        {
                            ["name"] = tool.function.name,
                            ["description"] = tool.function.description,
                            ["parameters"] = JObject.Parse(tool.function.parameters),
                        }
                    });
                rootObj["tools"] = toolsArr;
            }
            return rootObj.ToString(Newtonsoft.Json.Formatting.None);
        }

        /// <summary>消息序列化（含 assistant.tool_calls / role:"tool" 回执——薄层全量支持，勿裁剪）</summary>
        internal static JArray SerializeMessages(List<ChatMessage> messages)
        {
            var arr = new JArray();
            foreach (var msg in messages)
            {
                var msgObj = new JObject { ["role"] = msg.role };
                msgObj["content"] = msg.content ?? "";
                if (msg.tool_calls != null && msg.tool_calls.Count > 0)
                {
                    var callArr = new JArray();
                    foreach (var call in msg.tool_calls)
                        callArr.Add(new JObject
                        {
                            ["id"] = call.id,
                            ["type"] = "function",
                            ["function"] = new JObject
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

        // ---- SSE 解析（行级：传输层负责切块分行，这里只认 data: 行） ----

        private readonly StringBuilder _contentJoin = new StringBuilder();
        private readonly Dictionary<int, ToolCallResult> _toolCallTable = new Dictionary<int, ToolCallResult>();
        private string _finishReason;

        /// <summary>SSE 单行处理：data: {json} 三通道增量；[DONE] 忽略（完成由连接收尾触发）。
        /// 残行/坏行静默跳过（SSE 中途断流的容错优先于严格报错）。</summary>
        void ProcessSseLine(string line)
        {
            if (string.IsNullOrEmpty(line) || !line.StartsWith("data:", StringComparison.Ordinal)) return;
            string data = line.Substring(5).Trim();
            if (data == "[DONE]") return;

            JObject block;
            try { block = JObject.Parse(data); }
            catch (Exception) { return; } // 半截 JSON（断流截断）等——跳过

            var delta = block["choices"]?[0]?["delta"];
            if (delta == null) return;

            if (delta["reasoning_content"] is JValue thinkingValue && thinkingValue.Value != null)
                onReasoningDelta?.Invoke(thinkingValue.Value<string>());

            if (delta["content"] is JValue contentValue && contentValue.Value != null)
            {
                string clip = contentValue.Value<string>();
                _contentJoin.Append(clip);
                onContentDelta?.Invoke(clip);
            }

            if (delta["tool_calls"] is JArray callBlocks)
            {
                foreach (var callBlock in callBlocks)
                {
                    int serialNum = callBlock["index"]?.Value<int>() ?? 0;
                    if (!_toolCallTable.TryGetValue(serialNum, out var callResult))
                        _toolCallTable[serialNum] = callResult = new ToolCallResult { index = serialNum };
                    if (callBlock["id"] is JValue idValue && idValue.Value != null)
                        callResult.id = idValue.Value<string>();
                    var functionBlock = callBlock["function"];
                    if (functionBlock?["name"] is JValue nameValue && nameValue.Value != null)
                        callResult.function.name = nameValue.Value<string>();
                    if (functionBlock?["arguments"] is JValue paramValue && paramValue.Value != null)
                        callResult.function.arguments = (callResult.function.arguments ?? "") + paramValue.Value<string>();
                }
            }

            var finishReasonBlock = block["choices"]?[0]?["finish_reason"];
            if (finishReasonBlock is JValue finishReasonValue && finishReasonValue.Value != null)
                _finishReason = finishReasonValue.Value<string>();
        }

        /// <summary>触发完成回调（成功收尾时）：工具调用优先交会话层，随后 完成 给全量正文
        ///（工具轮全量为空——会话层按空值忽略，见其 完成 处理器）。全量先取局部快照：
        /// onToolCalls 里会话层会同步发起下一轮请求（重置累积器），快照防丢本轮内容。</summary>
        void fireCompletion()
        {
            string full = _contentJoin.ToString();
            if (!string.IsNullOrEmpty(_finishReason) && _finishReason == "tool_calls")
            {
                var callList = new List<ToolCallResult>(_toolCallTable.Values);
                callList.Sort((a, b) => a.index.CompareTo(b.index));
                onToolCalls?.Invoke(callList);
            }
            onComplete?.Invoke(full);
        }

        /// <summary>HTTP 错误体提取 {"error":{"message":...}}（五家供应商同构；解析失败回退状态码文案）</summary>
        static string extractErrorMessage(string body, long code)
        {
            try
            {
                var err = JObject.Parse(body)?["error"]?["message"];
                if (err is JValue v && v.Value != null)
                {
                    string msg = v.Value<string>();
                    if (!string.IsNullOrEmpty(msg)) return $"HTTP {code}：{msg}";
                }
            }
            catch (Exception) { }
            return $"HTTP {code}：请求失败";
        }

        // ---- 传输层：SSE 下载处理器（逐块→UTF-8 解码→按行切片，主线程回调） ----

        /// <summary>DownloadHandlerScript 子类：ReceiveData 在主线程被逐块调用（官方文档）。
        /// 职责仅限"字节→字符→行"：跨块 UTF-8 多字节序列用持久 Decoder 续解（直接
        /// Encoding.UTF8.GetString(data) 在中文被块边界切开时会产生替换字符）；行以 \n 切
        ///（\r\n 容错剥 \r）。非 data: 行（HTTP 错误体等）累积进 rawBody 供收尾提取。</summary>
        class SseDownloadHandler : DownloadHandlerScript
        {
            public readonly StringBuilder rawBody = new StringBuilder();
            public bool receivedAny;                    // 看门狗用：是否已收到首块数据
            public float lastDataAt;                    // 看门狗用：最近一次数据时刻（realtimeSinceStartup）
            public Action<string> onSseLine;

            private readonly Decoder _utf8Decoder = Encoding.UTF8.GetDecoder();
            private readonly StringBuilder _lineBuf = new StringBuilder();
            // 解码缓冲 ≥ 字节缓冲 + 挂起余量：UTF-8 输出字符数上限=输入字节数（纯 ASCII 1:1，多字节只缩不涨）
            // +Decoder 挂起字节 ≤3——原 4096 容不下单回调最大 16KB 块（流快时多 TCP 段合并成一次回调），
            // GetChars 直接抛 chars 溢出=整请求崩（curl error 23，2026-08-30 "打开界面后报错"实证）
            private readonly char[] _charBuf = new char[16400];

            public SseDownloadHandler() : base(new byte[16384]) // 预分配缓冲：单回调最多搬运 16KB
            {
                lastDataAt = Time.realtimeSinceStartup; // 看门狗基准=请求起点（0 会被当成"早已超时"）
            }

            protected override bool ReceiveData(byte[] data, int dataLength)
            {
                if (data == null || dataLength <= 0) return true;
                receivedAny = true;
                lastDataAt = Time.realtimeSinceStartup;

                // 溢出免疫解码：GetCharCount 先数（含 Decoder 挂起字节，永不抛），缓冲不够临时扩
                //（理论不触发——_charBuf 已按最大块+余量分配，防御数据层超尺寸传入的极端情形）
                int need = _utf8Decoder.GetCharCount(data, 0, dataLength);
                char[] charsBuf = need > _charBuf.Length ? new char[need] : _charBuf;
                int chars = _utf8Decoder.GetChars(data, 0, dataLength, charsBuf, 0);
                for (int i = 0; i < chars; i++)
                {
                    char c = charsBuf[i];
                    if (c == '\n')
                    {
                        int len = _lineBuf.Length;
                        if (len > 0 && _lineBuf[len - 1] == '\r') _lineBuf.Length = len - 1; // \r\n 容错
                        string line = _lineBuf.ToString();
                        _lineBuf.Length = 0;
                        rawBody.Append(line).Append('\n');
                        onSseLine?.Invoke(line);
                    }
                    else _lineBuf.Append(c);
                }
                return true; // false 会中止请求——恒 true
            }

            /// <summary>连接关闭时未带换行的残行（正常 SSE 不出现；断流兜底）</summary>
            public void flushPendingLine()
            {
                if (_lineBuf.Length == 0) return;
                string line = _lineBuf.ToString();
                _lineBuf.Length = 0;
                rawBody.Append(line).Append('\n');
                onSseLine?.Invoke(line);
            }
        }
    }
}
