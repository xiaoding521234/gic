namespace GIC.Framework
{
    /// <summary>
    /// 手势语义常量表 —— 全局唯一权威（2026-09-13 用户拍板：全局统一，不做 per-surface 覆写）。
    /// 取值基准（源码级调研，信源=.codely-cli/webrefs/gesture-input/，笔记 notes/gesture-system-research_2026-09-13.md）：
    /// - Android ViewConfiguration：TOUCH_SLOP=8dp（3x 屏≈24 物理 px）、DOUBLE_TAP_TIMEOUT=300ms、LONG_PRESS_TIMEOUT=400ms
    /// - Flutter constants.dart：kTouchSlop=18 逻辑px（原 8 因"点不中"投诉上调）、kPrecisePointerHitSlop=1px（鼠标档）、kLongPressTimeout=500ms
    /// - 本项目桌宠拍板值：按住 0.15s 升级拖拽 → 直接入表为 HoldToDragTimeout
    /// 注意：滚轮步进/滑行衰减/EMA 常数/缩放平滑等**域参数不属手势语义**，仍留各控制器序列化字段。
    /// </summary>
    public static class GestureMetrics
    {
        // ── 位移档（物理像素；按指针精度双档——Flutter 实证：触摸 8px 太紧（后升 18），鼠标可到 1px）──

        /// <summary>触摸指针的点击/判别 slop（≈Android 8dp@3x 屏物理等效）</summary>
        public const float TouchSlop = 24f;

        /// <summary>精确指针（鼠标/触控板）的点击/判别 slop（Flutter 精确档 1px 过极端，取 4）</summary>
        public const float PreciseSlop = 4f;

        /// <summary>连击（tap chain）窗口内相邻两次 tap 的落点距离上限（Android DOUBLE_TAP_SLOP=100px）</summary>
        public const float TapChainSlop = 100f;

        // ── 时间档（秒；Android/Flutter 基准）──

        /// <summary>按下不动多久升级为真拖拽（DragRecognizer.OnSlopOrHold 模式；原桌宠 0.15s 用户拍板值入表）</summary>
        public const float HoldToDragTimeout = 0.150f;

        /// <summary>长按判定时长（Android 400ms/Flutter 500ms 取中）</summary>
        public const float LongPressTimeout = 0.450f;

        /// <summary>tap 连击窗口（Android/Flutter DOUBLE_TAP_TIMEOUT=300ms；窗口空档按累计 count 派发）</summary>
        public const float TapChainWindow = 0.300f;

        /// <summary>按指针精度档取判别 slop（双档入口）</summary>
        public static float SlopFor(PointerKind kind) => kind == PointerKind.Touch ? TouchSlop : PreciseSlop;
    }
}
