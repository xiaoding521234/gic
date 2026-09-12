using System;
using UnityEngine;

namespace GIC.Framework
{
    /// <summary>
    /// 双指捏合识别器（连续；MaxPointers=2）。第二指落下即宣胜起手（hub 先已按"双指取代单指"
    /// 取消同面单指识别器——Map/桌宠现有语义）；比例式回调 startDist/当前距离（消费者做
    /// pinchStartSize * ratio，Map 现公式），附双指中点屏幕位（消费者做锚定缩放）。
    /// **纯触屏手势**（PointerKind.Touch 才追踪——原 Map/pet 语义：鼠标不参与捏合；2026-09-13
    /// P2 回归修复：曾把鼠标追踪为第一指+晚起手用未初始化 _p1=(0,0) 算基准距离，单鼠标拖拽被
    /// 误判为捏合缩放，docs/14 §41）。
    /// 起手门槛=两指都不在 UI 上（hub 门2 在双指 Began 时按位置式兼查既有指，Map L470 实证教训）。
    /// 两指同点（距离 ≤1px 无比例基准）不起手，**两指齐备后**分开可晚起手（Map 现行为：每帧重试起手判定）。
    /// 注意：第二指起手瞬间在 UI 上则本识别器不追踪它——比旧实现略收紧（旧=两指都离开 UI 后还能
    /// 补起手）；"手指先按在按钮上再拖出"的补起手路径视为按钮意图，目检若异议再放宽。
    /// 一指抬起 → Ended；剩余指不自动续拖（Map 现语义；消费者可自行接管，libGDX 无缝转拖见 docs/24 §8）。
    /// </summary>
    public class PinchRecognizer : GestureRecognizer
    {
        /// <summary>捏合起手（参数=起手两指距离 px、双指中点屏幕位）</summary>
        public event Action<float, Vector2> OnPinchBegan;
        /// <summary>捏合逐帧（参数=startDist/当前两指距离 的比例、双指中点屏幕位；&gt;1 张开 &lt;1 收拢）</summary>
        public event Action<float, Vector2> OnPinchRatio;
        /// <summary>捏合结束（一指抬起）</summary>
        public event Action OnPinchEnded;

        private int _id0 = int.MinValue;
        private int _id1 = int.MinValue;
        private Vector2 _p0;
        private Vector2 _p1;
        private float _startDist;

        public PinchRecognizer() => MaxPointers = 2;

        protected override void OnPointerEvent(in PointerEvent e)
        {
            if (e.Kind != PointerKind.Touch) return; // 纯触屏手势：非触摸指针一律不追踪（鼠标拖拽归 DragRecognizer）

            switch (e.Phase)
            {
                case PointerPhase.Began:
                    if (_id0 == int.MinValue)
                    {
                        _id0 = e.Id;
                        _p0 = e.Position;
                        SetPossible();
                        return;
                    }
                    if (_id1 == int.MinValue && e.Id != _id0)
                    {
                        if (e.OverUI) return; // 一指在 UI 上不捏合（Map"捏按钮归按钮"规则；OverUI=hub 双指位置式兼查结果）
                        _id1 = e.Id;
                        _p1 = e.Position;
                        TryBegin(); // 两指同点则不起手，等分开后晚起手
                    }
                    return;

                case PointerPhase.Moved:
                case PointerPhase.Stationary:
                    if (e.Id == _id0) _p0 = e.Position;
                    else if (e.Id == _id1) _p1 = e.Position;
                    else return;
                    if (State == GestureState.Began || State == GestureState.Changed)
                    {
                        float dist = Vector2.Distance(_p0, _p1);
                        if (dist > 1f) // 两指同点无比例基准（Map 现守卫）
                        {
                            SetChanged();
                            OnPinchRatio?.Invoke(_startDist / dist, (_p0 + _p1) * 0.5f);
                        }
                    }
                    else if (State == GestureState.Possible && _id1 != int.MinValue)
                    {
                        TryBegin(); // 晚起手：两指齐备前提下，从同点分开时补起手（单指在屏期间绝不自起手——P2 回归教训）
                    }
                    return;

                case PointerPhase.Ended:
                case PointerPhase.Cancelled:
                    if (e.Id == _id0)
                    {
                        _id0 = _id1;
                        _p0 = _p1;
                        _id1 = int.MinValue;
                    }
                    else if (e.Id == _id1)
                        _id1 = int.MinValue;
                    else return;
                    if (State == GestureState.Began || State == GestureState.Changed)
                    {
                        OnPinchEnded?.Invoke();
                        SetEnded();
                    }
                    else if (State == GestureState.Possible)
                        SetFailed(); // 第二指从未准入（如在 UI 上），第一指抬起时收尸
                    return;
            }
        }

        /// <summary>起手判定：两指齐备且距离 &gt;1px 才有比例基准；起手即宣胜（hub 仲裁同面单指——CancelSingles 已先行）</summary>
        private void TryBegin()
        {
            if (_id1 == int.MinValue) return; // 铁闸：两指未齐备绝不自起手（防默认 _p1=(0,0) 产出垃圾基准距离）
            _startDist = Vector2.Distance(_p0, _p1);
            if (_startDist <= 1f) return;
            SetBegan();
            OnPinchBegan?.Invoke(_startDist, (_p0 + _p1) * 0.5f);
        }

        protected override void OnReset()
        {
            _id0 = int.MinValue;
            _id1 = int.MinValue;
        }
    }
}
