namespace GIC.Framework
{
    /// <summary>
    /// 资源加载优先级 — 高优先级请求插队，低优先级请求延后
    /// </summary>
    public enum LoadPriority
    {
        /// <summary>后台预加载，不紧急</summary>
        Low = 0,
        /// <summary>默认优先级</summary>
        Normal = 1,
        /// <summary>用户当前可见区域，需尽快加载</summary>
        High = 2,
        /// <summary>阻塞型加载，必须立即处理</summary>
        Critical = 3,
    }
}
