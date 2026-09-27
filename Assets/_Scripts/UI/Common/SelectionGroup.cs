using System;
using UnityEngine;
using UnityEngine.Events;

namespace GIC.UI
{
    /// <summary>
    /// 单选互斥组（SelectButton 配套；取代 UGUI ToggleGroup）：
    /// 零登记——只持 Current 引用、不维护成员表，成员经 SelectButton.所属选中组 持本组，
    /// 池化成员即插即拔无注册时序坑（ToggleGroup 注册绑 OnEnable、DestroyImmediate 后 EnsureValidState 的坑全不存在）。
    /// 选中写入口=Select/ClearSelection；组只负责互斥与换选通知，视觉仍由各按钮的 onSelectedChanged 消费方自绘。
    /// </summary>
    public class SelectionGroup : MonoBehaviour
    {
        [Serializable]
        public class CurrentChangedEvent : UnityEvent<SelectButton> {}

        [Tooltip("允许全部取消（点击已选中项可清空；默认 false=FGUI Radio 语义）")]
        public bool 允许全部取消 = false;

        [SerializeField] private CurrentChangedEvent _onCurrentChanged = new();

        /// <summary>当前选中项（无=null；被销毁后 Unity 假 null 自动失效）</summary>
        public SelectButton Current { get; private set; }

        public CurrentChangedEvent onCurrentChanged => _onCurrentChanged;

        public void Select(SelectButton item) => Select(item, true);

        /// <summary>选中 item：已是 Current=无操作；旧者退选→新者选中（notify 同 flag 透传两方）。
        /// item=null 等价 ClearSelection。</summary>
        public void Select(SelectButton item, bool notify)
        {
            if (item == null)
            {
                ClearSelection(notify);
                return;
            }
            if (Current == item) return;
            var old = Current;
            Current = item;
            if (old != null) old.ApplySelected(false, notify);
            item.ApplySelected(true, notify);
            _onCurrentChanged.Invoke(item);
        }

        public void ClearSelection() => ClearSelection(true);

        /// <summary>清空选中（旧者退选；notify 透传）</summary>
        public void ClearSelection(bool notify)
        {
            var old = Current;
            Current = null;
            if (old != null) old.ApplySelected(false, notify);
            _onCurrentChanged.Invoke(null);
        }
    }
}
