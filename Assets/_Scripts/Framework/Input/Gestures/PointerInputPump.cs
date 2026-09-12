using System.Collections.Generic;
using UnityEngine;

namespace GIC.Framework
{
    /// <summary>
    /// 指针事件源 —— legacy Input 的唯一轮询点（docs/24 §3）：鼠标 + 触摸归一为 PointerEvent 流。
    /// 触摸激活期间完全抑制鼠标流（Unity 触摸→鼠标模拟的镜像事件；同 InputManager"触屏忽略鼠标键位"
    /// 补丁的另一半——2026-09-02 真机实证的双指=右键陷阱由此根治）。鼠标只取左键（右键=CloseUI 走
    /// InputManager 绑定）；鼠标 id=-1（EventSystem fingerId 约定），触摸 id=Touch.fingerId。
    /// 按住期间每帧产 Moved（含零位移——消费者 EMA 依赖静止帧喂 0 衰减，Map"按住不动松手不滑"实证）。
    /// </summary>
    internal class PointerInputPump
    {
        private bool _mouseDown;

        public void Poll(double now, List<PointerEvent> events)
        {
            if (Input.touchCount > 0)
            {
                _mouseDown = false; // 触摸期间鼠标按下状态失真（模拟事件）——触摸结束不误发鼠标 Ended
                for (int i = 0; i < Input.touchCount; i++)
                {
                    Touch t = Input.GetTouch(i);
                    events.Add(new PointerEvent
                    {
                        Id = t.fingerId,
                        Kind = PointerKind.Touch,
                        Position = t.position,
                        Phase = ToPhase(t.phase),
                        Time = now
                    });
                }
                return;
            }

            bool down = Input.GetMouseButton(0);
            Vector2 pos = Input.mousePosition;
            if (down && !_mouseDown)
                events.Add(Make(pos, PointerPhase.Began));
            else if (down)
                events.Add(Make(pos, PointerPhase.Moved));
            else if (_mouseDown)
                events.Add(Make(pos, PointerPhase.Ended));
            _mouseDown = down;

            PointerEvent Make(Vector2 position, PointerPhase phase) =>
                new PointerEvent { Id = -1, Kind = PointerKind.Precise, Position = position, Phase = phase, Time = now };
        }

        private static PointerPhase ToPhase(TouchPhase phase)
        {
            switch (phase)
            {
                case TouchPhase.Began: return PointerPhase.Began;
                case TouchPhase.Moved: return PointerPhase.Moved;
                case TouchPhase.Stationary: return PointerPhase.Stationary;
                case TouchPhase.Ended: return PointerPhase.Ended;
                default: return PointerPhase.Cancelled; // TouchPhase.Canceled
            }
        }
    }
}
