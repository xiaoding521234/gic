using System;
using UnityEngine;

namespace GIC.Framework
{
    /// <summary>
    /// 双指捏合识别器（连续；MaxPointers=2）。第二指落下即宣胜起手（hub 先已按"双指取代单指"
    /// 取消同面单指识别器——Map/桌宠现有语义）；比例式回调（消费者做 pinchStartSize * ratio，Map 现公式）。
    /// 起手门槛=两指都不在 UI 上（hub 门2 在双指 Began 时按位置式兼查既有指，Map L470 实证教训）。
    /// 一指抬起 → Ended；剩余指不自动续拖（Map 现语义；消费者可自行接管，libGDX 无缝转拖见 docs/24 §8）。
    /// </summary>
    public class PinchRecognizer : GestureRecognizer
    {
        /// <summary>捏合起手（参数=起手两指距离 px）</summary>
        public event Action<float> OnPinchBegan;
        /// <summary>捏合逐帧（参数=startDist/当前两指距离 的比例；&gt;1 张开 &lt;1 收拢）</summary>
        public event Action<float> OnPinchRatio;
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
                        _startDist = Vector2.Distance(_p0, _p1);
                        SetBegan(); // 宣胜
                        OnPinchBegan?.Invoke(_startDist);
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
                            OnPinchRatio?.Invoke(_startDist / dist);
                        }
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

        protected override void OnReset()
        {
            _id0 = int.MinValue;
            _id1 = int.MinValue;
        }
    }
}
