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
        public const string BattleExitConfirm = "BattleExitConfirm";   // 战斗退出确认弹窗期间
    }

    /// <summary>
    /// InputLock 静态门面 — 压缩调用点（内部转发 InputManager，语义与实例方法一致）。
    /// 后端由 InputManager 在 [PostConstruct] 中注册；注册前调用为安全空操作（与容器未就绪时行为一致）。
    /// </summary>
    public static class InputLocks
    {
        private static InputManager _backend;

        /// <summary>InputManager 初始化时注册自身</summary>
        public static void RegisterBackend(InputManager backend) => _backend = backend;

        public static bool IsLocked => _backend?.IsInputLocked ?? false;

        /// <summary>是否持有指定原因的锁（如 GameScene 查询 SceneTransition）</summary>
        public static bool HasLock(string reason) => _backend?.HasInputLock(reason) ?? false;

        public static void Push(object owner, string reason)
            => _backend?.PushInputLock(owner, reason);

        public static void Pop(object owner, string reason)
            => _backend?.PopInputLock(owner, reason);

        /// <summary>OnDestroy 兜底：释放 owner 的全部锁（清理到泄漏锁时会 LogWarning）</summary>
        public static void PopAll(object owner)
            => _backend?.PopAllInputLocks(owner);
    }
}
