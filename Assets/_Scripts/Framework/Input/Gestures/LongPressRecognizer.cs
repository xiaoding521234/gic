using System;
using UnityEngine;

namespace GIC.Framework
{
    /// <summary>
    /// 长按识别器（连续，Apple 语义；docs/24 §4.3/§7.6）：按下挂 deadline=GestureMetrics.LongPressTimeout；
    /// 三取消源=①位移出 slop（deadline 前）②提前抬起 ③双指起手取代单指（hub 统一取消单指识别器）。
    /// deadline 到 → Began 宣胜独占（hub 仲裁同面其余识别器 Failed），此后按住移动 → Changed（消费方
    /// 可只订阅 OnLongPressBegan 当离散事件用），抬起 → Ended。
    /// P1 落地暂无消费者——B6 战斗选格/卡牌长按详情预留（docs/24 §8）。
    /// </summary>
    public class LongPressRecognizer : GestureRecognizer
    {
        /// <summary>长按成立（deadline 到；参数=按下点）</summary>
        public event Action<Vector2> OnLongPressBegan;
        /// <summary>长按成立后按住移动（Apple continuous 语义；参数=当前位置）</summary>
        public event Action<Vector2> OnLongPressMoved;
        /// <summary>长按结束（抬起）</summary>
        public event Action OnLongPressEnded;

        private int _pointerId = int.MinValue;
        private bool _pressing;
        private Vector2 _downPos;
        private PointerKind _kind;
        private double _deadline = -1;

        protected override void OnPointerEvent(in PointerEvent e)
        {
            switch (e.Phase)
            {
                case PointerPhase.Began:
                    if (State != GestureState.Idle) return; // 序列进行中，忽略新指针
                    _pointerId = e.Id;
                    _pressing = true;
                    _downPos = e.Position;
                    _kind = e.Kind;
                    _deadline = e.Time + GestureMetrics.LongPressTimeout;
                    SetPossible();
                    break;

                case PointerPhase.Moved:
                    if (e.Id != _pointerId || !_pressing) return;
                    if (State == GestureState.Possible)
                    {
                        float slop = GestureMetrics.SlopFor(_kind);
                        if ((e.Position - _downPos).sqrMagnitude >= slop * slop)
                        {
                            SetFailed(); // 取消源①：未到 deadline 先出界
                            return;
                        }
                    }
                    else if (State == GestureState.Began || State == GestureState.Changed)
                    {
                        SetChanged();
                        OnLongPressMoved?.Invoke(e.Position);
                    }
                    break;

                case PointerPhase.Stationary:
                    break;

                case PointerPhase.Ended:
                    if (e.Id != _pointerId || !_pressing) return;
                    _pressing = false;
                    if (State == GestureState.Possible)
                        SetFailed(); // 取消源②：提前抬起
                    else if (State == GestureState.Began || State == GestureState.Changed)
                    {
                        OnLongPressEnded?.Invoke();
                        SetEnded();
                    }
                    break;

                case PointerPhase.Cancelled:
                    _pressing = false;
                    SetCancelled();
                    break;
            }
        }

        protected override void OnTick(double now)
        {
            if (_pressing && State == GestureState.Possible && now >= _deadline)
            {
                SetBegan(); // 宣胜独占——同面 tap/drag 由 hub 仲裁 Failed
                OnLongPressBegan?.Invoke(_downPos);
            }
        }

        protected override void OnReset()
        {
            _pointerId = int.MinValue;
            _pressing = false;
            _deadline = -1;
        }
    }
}
