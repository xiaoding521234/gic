using System;
using UnityEngine;

namespace GIC.Framework
{
    /// <summary>拖拽起手模式（docs/24 §4.3）</summary>
    public enum DragBeginMode
    {
        /// <summary>抓住即拖零延迟（相机面已验证手感；Map/Battle）</summary>
        Immediate,
        /// <summary>位移过 slop 才起手（纯 UI 拖放推荐；B6 若需）</summary>
        OnSlop,
        /// <summary>位移过 slop **或**按住超 HoldToDragTimeout 才起手（桌宠"0.15s 或 8px 升级"语义）</summary>
        OnSlopOrHold
    }

    /// <summary>
    /// 拖拽识别器（连续）。逐帧回调屏幕位移与当前位置——世界坐标换算/EMA 末速/滑行衰减/贴边 clamp
    /// 全部留消费者（相机域逻辑，docs/24 §5）；Stationary 帧以零位移继续回调（Map"按住不动松手不滑"实证）。
    /// Immediate 模式可开 ShortTap 复合发射：抬起总位移 &lt; slop 且不在 UI 上 → OnShortTap 伴发
    /// （libGDX/Android GestureDetector 的 tap+pan 同体模式——Map"抓住即拖+抬起按位移判点击"的机制化，
    /// 不与 TapRecognizer 竞争仲裁）。
    /// </summary>
    public class DragRecognizer : GestureRecognizer
    {
        /// <summary>拖拽起手（参数=起手屏幕点）</summary>
        public event Action<Vector2> OnDragBegan;
        /// <summary>拖拽逐帧（参数=本帧屏幕位移、当前屏幕点；按住静止帧位移=零向量）</summary>
        public event Action<Vector2, Vector2> OnDragDelta;
        /// <summary>拖拽结束（参数=抬起点；末速原料=最后一次 OnDragDelta）</summary>
        public event Action<Vector2> OnDragEnded;
        /// <summary>Immediate 模式短位移点击复合发射（参数=抬起点；此时 OnDragEnded 也已派发）</summary>
        public event Action<Vector2> OnShortTap;
        /// <summary>
        /// 升级式拖拽（OnSlop/OnSlopOrHold）的"早退点击候选"：抬起时未过 slop 也未过 hold 时限
        /// （=还停在 Possible）→ 转 Failed 前派发（参数=抬起点）。桌宠单击（旧"按下待定期间松手
        /// 且几乎没动=单击"语义）与未来 B6 格点点击消费。Immediate 模式走 OnShortTap 不发本事件。
        /// </summary>
        public event Action<Vector2> OnTapCandidate;

        private readonly DragBeginMode _mode;
        private readonly bool _emitShortTap;

        private int _pointerId = int.MinValue;
        private bool _pressing;
        private Vector2 _downPos;
        private Vector2 _lastPos;
        private PointerKind _kind;
        private double _holdDeadline = -1;

        public DragRecognizer(DragBeginMode mode, bool emitShortTap = false)
        {
            _mode = mode;
            _emitShortTap = emitShortTap;
        }

        protected override void OnPointerEvent(in PointerEvent e)
        {
            switch (e.Phase)
            {
                case PointerPhase.Began:
                    if (State != GestureState.Idle) return; // 单主指针：序列中忽略新指针
                    _pointerId = e.Id;
                    _pressing = true;
                    _downPos = e.Position;
                    _lastPos = e.Position;
                    _kind = e.Kind;
                    if (_mode == DragBeginMode.Immediate)
                    {
                        SetBegan(); // 宣胜（同面如有 Tap/LongPress 由 hub 仲裁 Failed）
                        OnDragBegan?.Invoke(e.Position);
                    }
                    else
                    {
                        SetPossible();
                        if (_mode == DragBeginMode.OnSlopOrHold)
                            _holdDeadline = e.Time + GestureMetrics.HoldToDragTimeout;
                    }
                    break;

                case PointerPhase.Moved:
                case PointerPhase.Stationary:
                    if (e.Id != _pointerId || !_pressing) return;
                    if (State == GestureState.Possible)
                    {
                        float slop = GestureMetrics.SlopFor(_kind);
                        if ((e.Position - _downPos).sqrMagnitude >= slop * slop)
                            UpgradeToBegan(e.Position); // 位移过 slop 升级（OnSlop/OnSlopOrHold 同路径）
                    }
                    else if (State == GestureState.Began || State == GestureState.Changed)
                    {
                        Vector2 delta = e.Position - _lastPos;
                        _lastPos = e.Position;
                        SetChanged();
                        OnDragDelta?.Invoke(delta, e.Position);
                    }
                    break;

                case PointerPhase.Ended:
                    if (e.Id != _pointerId || !_pressing) return;
                    _pressing = false;
                    if (State == GestureState.Possible)
                    {
                        // 升级式早退（未过 slop 也未过 hold 时限）=点击候选：先派发让消费方接手单击语义，再转 Failed
                        OnTapCandidate?.Invoke(e.Position);
                        SetFailed();
                        return;
                    }
                    if (State == GestureState.Began || State == GestureState.Changed)
                    {
                        // ShortTap 先于 OnDragEnded 派发：消费方在 ShortTap 里清滑行速度后，
                        // OnDragEnded 的滑行判定自然不成立（Map"点击松手不滑行"现语义，零一帧滑移）
                        if (_emitShortTap)
                        {
                            float slopEnd = GestureMetrics.SlopFor(_kind);
                            if ((e.Position - _downPos).sqrMagnitude < slopEnd * slopEnd && !e.OverUI)
                                OnShortTap?.Invoke(e.Position); // 短位移点击伴发（Map 现语义：轻点锚点）
                        }
                        OnDragEnded?.Invoke(e.Position);
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
            if (_pressing && State == GestureState.Possible && _mode == DragBeginMode.OnSlopOrHold && now >= _holdDeadline)
                UpgradeToBegan(_lastPos); // 按住超时升级为真拖拽（原桌宠 0.15s 拍板值，已入 GestureMetrics）
        }

        private void UpgradeToBegan(Vector2 pos)
        {
            SetBegan(); // 宣胜——同面 TapRecognizer 连击由 hub 仲裁 ForceFail（真实拖拽打断连击计次）
            OnDragBegan?.Invoke(pos);
        }

        protected override void OnReset()
        {
            _pointerId = int.MinValue;
            _pressing = false;
            _holdDeadline = -1;
        }
    }
}
