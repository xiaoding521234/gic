using UnityEngine;

namespace GIC.Framework
{
    /// <summary>指针精度档（Flutter：precise pointer 单独 slop 档——鼠标/触摸同流不同档）</summary>
    public enum PointerKind
    {
        /// <summary>精确指针（鼠标/触控板）</summary>
        Precise,
        /// <summary>触摸</summary>
        Touch
    }

    /// <summary>指针相位（对齐 Unity TouchPhase 语义；鼠标归一到 Began/Moved/Ended）</summary>
    public enum PointerPhase
    {
        Began,
        Moved,
        Stationary,
        Ended,
        Cancelled
    }

    /// <summary>
    /// 归一化指针事件 —— PointerInputPump 每帧由 legacy Input 产出，GestureHub 三门后分发到 surface。
    /// 纯数据：识别器只依赖它 + 注入时间戳，可用合成事件流离线断言（docs/24 §4.6 铁律）。
    /// </summary>
    public struct PointerEvent
    {
        /// <summary>指针 id：鼠标=-1（EventSystem fingerId 约定），触摸=Touch.fingerId</summary>
        public int Id;

        public PointerKind Kind;

        /// <summary>屏幕坐标（物理像素）</summary>
        public Vector2 Position;

        public PointerPhase Phase;

        /// <summary>事件时刻（秒，Time.unscaledTimeAsDouble；合成测试直接注入任意单调值）</summary>
        public double Time;

        /// <summary>
        /// 本事件位置是否落在 UI 上（hub 门2 计算；仅 Began/Ended 帧保证新鲜）：
        /// 单指 Began=id 式（Began 帧可靠）；双指 Began=位置式兼查既有指（Map L470 实证教训）；
        /// Ended=tap 判定用（"按下在地图、滑到按钮上抬起"不判点击——Map 现规则）。
        /// </summary>
        public bool OverUI;
    }
}
