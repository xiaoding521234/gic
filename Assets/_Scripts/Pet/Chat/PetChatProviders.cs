namespace GIC.Pet.Chat
{
    /// <summary>
    /// 对话模型供应商注册表（docs/19 §6.5，2026-08-29）：Spring AI 式"选供应商+填自己的 key"——
    /// 五家全部 OpenAI Chat Completions 兼容（同一套传输层/DTO/工具调用协议，仅 baseUrl+模型名不同），
    /// 端点与模型名均为 2026-08-29 官方文档网检确认（勿凭记忆改，改前先网检）。
    ///
    /// **表下标=存档值（PlayerSaveData.pet.petChatProvider / pet.json chatProvider），只增不删不重排**——
    /// 删改中段会静默错位所有玩家的供应商选择。新供应商 append 到表尾。
    /// 玩家只填 key（设置→派蒙→对话 API Key）；模型名/端点对玩家不可见，默认值在此表维护，
    /// Inspector 端点/模型覆盖字段留给开发调试。
    /// </summary>
    public static class PetChatProviders
    {
        public class ProviderInfo
        {
            /// <summary>标识（日志/诊断用）</summary>
            public string id;
            /// <summary>OpenAI 兼容端点（不含尾 /chat/completions；结尾 /v1 因家而异，按各家文档原样）</summary>
            public string baseUrl;
            /// <summary>默认模型（陪伴对话取向：便宜+快优先）</summary>
            public string model;
            /// <summary>开发环境变量兜底（玩家密文缺省时）</summary>
            public string envKey;
            /// <summary>是否随请求发 temperature——gpt-5 系只支持默认值 1（发 0.8 会 400），OpenAI 恒不发</summary>
            public bool sendTemperature;
        }

        public static readonly ProviderInfo[] table =
        {
            new ProviderInfo { id = "deepseek", baseUrl = "https://api.deepseek.com", model = "deepseek-v4-flash", envKey = "DEEPSEEK_API_KEY", sendTemperature = true },
            new ProviderInfo { id = "kimi", baseUrl = "https://api.moonshot.cn/v1", model = "kimi-k3", envKey = "MOONSHOT_API_KEY", sendTemperature = true },
            new ProviderInfo { id = "glm", baseUrl = "https://open.bigmodel.cn/api/paas/v4", model = "glm-4.7", envKey = "ZHIPU_API_KEY", sendTemperature = true },
            new ProviderInfo { id = "qwen", baseUrl = "https://dashscope.aliyuncs.com/compatible-mode/v1", model = "qwen-plus", envKey = "DASHSCOPE_API_KEY", sendTemperature = true },
            new ProviderInfo { id = "openai", baseUrl = "https://api.openai.com/v1", model = "gpt-5.4-mini", envKey = "OPENAI_API_KEY", sendTemperature = false },
        };

        /// <summary>按存档值取供应商（非法值/越界钳回 0=DeepSeek，老档默认无缝兼容）</summary>
        public static ProviderInfo Resolve(int index)
        {
            if (index < 0 || index >= table.Length) return table[0];
            return table[index];
        }
    }
}
