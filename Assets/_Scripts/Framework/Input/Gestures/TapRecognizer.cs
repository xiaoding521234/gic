using System;
using UnityEngine;

namespace GIC.Framework
{
    /// <summary>
    /// 点击识别器（离散；docs/24 §4.3）。判定 = 抬起时落点位移 &lt; slop（GestureMetrics 按指针精度双档；
    /// 业界三源共识：tap 只因移动失效，不因超时失效）+ 抬起点不在 UI 上（Map 现规则收编）。
    /// ChainMode（连击计次，桌宠用）：count 命中 dispatchAtCounts 档位立即派发，其余档位持有
    /// TapChainWindow 等下一次按下升级、窗口空档按累计 count 派发——Flutter hold/release"双击等待"
    /// 机制化 + 桌宠"双击退出立即响应"语义。
    /// 与升级式拖拽同面竞争：位移出 slop 自判 Failed；拖拽升级宣胜时被 hub 仲裁 ForceFail（连击清零）。
    /// 注意：Immediate 拖拽面（Map/Battle 相机）的"短位移点击"不走本识别器——由 DragRecognizer 的
    /// ShortTap 复合发射（libGDX/Android tap+pan 同体模式，docs/24 §7.10）。
    /// </summary>
    public class TapRecognizer : GestureRecognizer
    {
        /// <summary>点击结果：Count=连击数（非链恒 1）、Position=末次抬起落点、Kind=指针精度档</summary>
        public struct TapInfo
        {
            public int Count;
            public Vector2 Position;
            public PointerKind Kind;
        }

        /// <summary>点击派发（非链：抬起即派发 Count=1；链：按 dispatchAtCounts/窗口规则派发）</summary>
        public event Action<TapInfo> OnTap;

        private readonly bool _chain;
        private readonly int[] _dispatchAt;   // 命中即立即派发的 count 档位（如桌宠 {2,3}）

        private int _pointerId = int.MinValue;
        private bool _pressing;
        private Vector2 _downPos;
        private PointerKind _kind;
        private int _count;
        private double _windowDeadline = -1; // >0 = 连击等待中
        private Vector2 _lastTapPos;

        /// <param name="chain">连击模式；关闭=普通单击（未来战斗格点点击用），抬起即派发</param>
        /// <param name="dispatchAt">命中即立即派发的 count 档位（如桌宠 {2,3}：双击/三连击即时响应、单击过窗口才派发）；空=全部走窗口</param>
        public TapRecognizer(bool chain = false, int[] dispatchAt = null)
        {
            _chain = chain;
            _dispatchAt = dispatchAt ?? (_chain ? new int[0] : new[] { 1 });
        }

        protected override void OnPointerEvent(in PointerEvent e)
        {
            switch (e.Phase)
            {
                case PointerPhase.Began:
                    if (_windowDeadline > 0)
                    {
                        // 连击等待中的续击：落点离上一 tap 太远 → 链重启（libGDX tap 矩形语义）
                        if ((e.Position - _lastTapPos).sqrMagnitude > GestureMetrics.TapChainSlop * GestureMetrics.TapChainSlop)
                            _count = 0;
                        _windowDeadline = -1;
                    }
                    _pointerId = e.Id;
                    _pressing = true;
                    _downPos = e.Position;
                    _kind = e.Kind;
                    SetPossible();
                    break;

                case PointerPhase.Moved:
                    if (e.Id != _pointerId || !_pressing || State != GestureState.Possible) return;
                    float slop = GestureMetrics.SlopFor(_kind);
                    if ((e.Position - _downPos).sqrMagnitude >= slop * slop)
                        SetFailed();
                    break;

                case PointerPhase.Stationary:
                    break;

                case PointerPhase.Ended:
                    if (e.Id != _pointerId || !_pressing) return;
                    _pressing = false;
                    if (State != GestureState.Possible) return; // 已被移动/仲裁判死
                    float slopEnd = GestureMetrics.SlopFor(_kind);
                    if ((e.Position - _downPos).sqrMagnitude >= slopEnd * slopEnd || e.OverUI)
                    {
                        SetFailed(); // 移出 slop / 抬在 UI 上（按下地图滑到按钮上抬起≠点击）
                        return;
                    }
                    _count++;
                    _lastTapPos = e.Position;
                    if (!_chain || Array.IndexOf(_dispatchAt, _count) >= 0)
                        Dispatch(); // 非链立即派发 / 链命中即时档位立即派发
                    else
                        _windowDeadline = e.Time + GestureMetrics.TapChainWindow; // 持窗口等下一次按下升级
                    break;

                case PointerPhase.Cancelled:
                    _pressing = false;
                    SetCancelled();
                    break;
            }
        }

        protected override void OnTick(double now)
        {
            if (_windowDeadline > 0 && now >= _windowDeadline)
            {
                _windowDeadline = -1;
                Dispatch(); // 窗口空档：按累计 count 派发（单击也在此出口——"过窗口才开对话"）
            }
        }

        private void Dispatch()
        {
            var info = new TapInfo { Count = _count, Position = _lastTapPos, Kind = _kind };
            _count = 0;
            OnTap?.Invoke(info);
            SetRecognized();
        }

        protected override void OnReset()
        {
            _pointerId = int.MinValue;
            _pressing = false;
            _count = 0;
            _windowDeadline = -1;
        }
    }
}
