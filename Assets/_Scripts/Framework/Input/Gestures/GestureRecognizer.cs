using System;

namespace GIC.Framework
{
    /// <summary>
    /// 手势识别器基类 —— 纯 C# 状态机（docs/24 §4.6 铁律：不依赖 UnityEngine.Time/单例，时间由
    /// PointerEvent.Time / Tick 注入，可离线合成事件流断言）。事件唯一入口=HandlePointerEvent/Tick/ForceFail/ForceCancel（hub 调用）。
    /// 宣胜（SetBegan/SetRecognized）瞬间回调 hub（Won 事件）→ hub 仲裁同 surface 其余识别器（first-accept-wins，docs/24 §7.3）。
    /// 终态（Recognized/Ended/Failed/Cancelled）后自动复位回 Idle；连击等待期可停在 Possible。
    /// </summary>
    public abstract class GestureRecognizer
    {
        /// <summary>所需最大指针数（TouchScript 指针数阈值；hub 双指起手取消单指识别器时用它判别）</summary>
        public int MaxPointers { get; protected set; } = 1;

        public GestureState State { get; private set; } = GestureState.Idle;

        /// <summary>宣胜事件——hub 仲裁钩子（Began/Recognized 瞬间触发一次；internal=仅 hub 订阅）</summary>
        internal event Action<GestureRecognizer> Won;

        /// <summary>状态迁移通知（旧态, 新态；含终态与复位回 Idle）——消费者订阅做收尾/表现（如桌宠拖拽物理收口）</summary>
        public event Action<GestureState, GestureState> StateChanged;

        // ── 事件入口（运行时仅 GestureHub 调用；public 供合成事件流离线断言——docs/24 §4.6 铁律，外部业务勿直调）──

        public void HandlePointerEvent(in PointerEvent e) => OnPointerEvent(e);

        public void Tick(double now) => OnTick(now);

        /// <summary>仲裁败方（被同 surface 其它识别器宣胜挤出——TouchScript "failed by another recognized gesture"）</summary>
        public void ForceFail() => Terminate(GestureState.Failed);

        /// <summary>外部打断（输入锁 / 双指起手取代单指）——进行中手势语义为"取消"而非"失败"</summary>
        public void ForceCancel() => Terminate(GestureState.Cancelled);

        // ── 子类钩子 ──

        protected abstract void OnPointerEvent(in PointerEvent e);

        /// <summary>每帧心跳（hub 喂；长按 deadline/按住升级/连击窗口在此自查，无协程）</summary>
        protected virtual void OnTick(double now) { }

        /// <summary>序列复位钩子（清指针簿记；State 已是终态或即将回 Idle）</summary>
        protected virtual void OnReset() { }

        // ── 状态迁移出口（子类经 protected 调用；私设 State 编译不过）──

        protected void SetPossible() => Transition(GestureState.Possible);

        /// <summary>连续手势起手（宣胜——hub 随即 fail 同 surface 其余识别器）</summary>
        protected void SetBegan()
        {
            Transition(GestureState.Began);
            Won?.Invoke(this);
        }

        protected void SetChanged() => Transition(GestureState.Changed);

        /// <summary>连续手势正常收尾 → 通知后复位回 Idle</summary>
        protected void SetEnded()
        {
            Transition(GestureState.Ended);
            Finish();
        }

        /// <summary>离散手势完成（宣胜）→ 通知后复位回 Idle</summary>
        protected void SetRecognized()
        {
            Transition(GestureState.Recognized);
            Won?.Invoke(this);
            Finish();
        }

        protected void SetFailed() => Terminate(GestureState.Failed);

        protected void SetCancelled() => Terminate(GestureState.Cancelled);

        // ── 内部 ──

        private void Terminate(GestureState terminal)
        {
            if (State == GestureState.Idle) return; // 未起手：无事发生（幂等，hub 锁冻结可反复调）
            Transition(terminal);
            Finish();
        }

        private void Finish()
        {
            OnReset();
            Transition(GestureState.Idle);
        }

        private void Transition(GestureState s)
        {
            if (State == s) return;
            GestureState old = State;
            State = s;
            StateChanged?.Invoke(old, s);
        }
    }
}
