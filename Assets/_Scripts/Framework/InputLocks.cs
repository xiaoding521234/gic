namespace GIC.Framework
{
    /// <summary>
    /// InputLock 原因常量 — 统一定义防止调用点字符串手误，并支持 HasInputLock(reason) 精确查询。
    /// </summary>
    public static class InputLockReason
    {
        public const string SceneTransition = "SceneTransition";       // 场景切换期间
        public const string SplashProtection = "SplashProtection";     // 启动保护（前 0.3 秒）
        public const string Entering = "Entering";                     // Screen 入场动画期间
        public const string Closing = "Closing";                       // Screen 关闭退场动画期间
        public const string MapEntering = "MapEntering";               // 地图入场动画期间
        public const string PopupEntering = "PopupEntering";           // 弹窗淡入期间
        public const string InputPopupEntering = "InputPopupEntering"; // 输入弹窗淡入期间
        public const string WishInProgress = "WishInProgress";         // 祈愿抽卡进行中
    }

    /// <summary>
    /// InputLock 静态门面 — 压缩调用点（内部转发 InputManager，语义与实例方法一致）。
    /// </summary>
    public static class InputLocks
    {
        public static bool IsLocked => Wargame.Instance?.InputManager?.IsInputLocked ?? false;

        public static void Push(object owner, string reason)
            => Wargame.Instance?.InputManager?.PushInputLock(owner, reason);

        public static void Pop(object owner, string reason)
            => Wargame.Instance?.InputManager?.PopInputLock(owner, reason);

        /// <summary>OnDestroy 兜底：释放 owner 的全部锁（清理到泄漏锁时会 LogWarning）</summary>
        public static void PopAll(object owner)
            => Wargame.Instance?.InputManager?.PopAllInputLocks(owner);
    }
}
