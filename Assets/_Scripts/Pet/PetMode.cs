namespace GIC.Pet
{
    /// <summary>
    /// 双形态进程开关：同一 exe 附加 --pet-mode 参数启动即进入桌宠形态（跳过游戏初始化，只跑宠物场景）。
    /// 编辑器内恒为 false（编辑器测试走正常游戏流程）。
    /// </summary>
    public static class PetMode
    {
        private const string ArgName = "--pet-mode";

        /// <summary>桌宠子进程统一启动参数（PetProcessLauncher 生产拉起 / PetEditorAutoLauncher 编辑器
        /// 联调共用一份，防止两处各写一份漂移）。窗口尺寸与 PetWindowController 窗口逻辑宽配套。</summary>
        public const string LaunchArgs = "--pet-mode -screen-fullscreen 0 -screen-width 550 -screen-height 825";

        public static bool Enabled { get; } = Detect();

        private static bool Detect()
        {
#if UNITY_EDITOR
            return false;
#else
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], ArgName, System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
#endif
        }
    }
}
