using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

namespace GIC.UI
{
    /// <summary>选中语义（对齐 FGUI ButtonMode）：Common=无选中态纯按钮 / Check=复选（点按翻转）/ Radio=单选（组内唯一，点已选项 no-op）</summary>
    public enum SelectMode
    {
        Common,
        Check,
        Radio,
    }

    /// <summary>
    /// 选中态按钮（2026-09-27 拍板自研，全面取代项目内 UGUI Toggle 用法）：
    /// - 继承 Button：onClick 边沿语义（Press 内含 IsActive/IsInteractable 门，置灰自动拦点击）+
    ///   Selectable 悬停/按下染色全继承——替代 Toggle「无 onClick、组内点已选强制回 on 仍发事件」的拧巴语义。
    /// - 零渲染意见：不带 graphic/checkmark 字段——选中视觉一律由各 View 自绘（select 图/弧光/pop 动画），
    ///   视觉订阅 onSelectedChanged（状态驱动），动作订阅 onClick（边沿驱动）。
    /// - 互斥 = SelectionGroup（零登记：组只持 Current，不维护成员表——池化成员即插即拔，
    ///   无 ToggleGroup 注册绑 OnEnable 时序坑）；本按钮经「所属选中组」字段持组引用。
    /// </summary>
    public class SelectButton : Button
    {
        [Serializable]
        public class SelectedChangedEvent : UnityEvent<bool> {}

        [Header("选中")]
        [SerializeField] private SelectMode 选中模式 = SelectMode.Radio;
        [Tooltip("点击是否改变选中态（FGUI changeStateOnClick 同款）。纯边沿场景设 false：卡牌编辑模式、战斗技能键（选中态由 BattleHud 状态机管理）")]
        [SerializeField] private bool 点按改变选中 = true;
        [Tooltip("激活时若未选中则选中一次（池化复用须自行复位，如 CardPool.Release）")]
        [SerializeField] private bool 初始选中 = false;
        [SerializeField] private SelectionGroup 所属选中组;

        [SerializeField] private SelectedChangedEvent _onSelectedChanged = new();

        private bool _selected;

        /// <summary>当前选中态（真源；视觉由 onSelectedChanged 消费方自绘）</summary>
        public bool Selected => _selected;

        /// <summary>所属互斥组（运行时可挂/可摘，如卡牌池 Spawn/Release）</summary>
        public SelectionGroup Group
        {
            get => 所属选中组;
            set => 所属选中组 = value;
        }

        public SelectedChangedEvent onSelectedChanged => _onSelectedChanged;

        /// <summary>选中模式运行时开关（默认按 Inspector 配置）</summary>
        public SelectMode Mode
        {
            get => 选中模式;
            set => 选中模式 = value;
        }

        /// <summary>点按改变选中 运行时开关（Card 编辑/卡组模式切换用）</summary>
        public bool ClickChangesSelected
        {
            get => 点按改变选中;
            set => 点按改变选中 = value;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (初始选中 && !_selected)
                SetSelected(true);
        }

        /// <summary>点击链：先走 Button 原生 onClick（含置灰门），再按模式改选中态</summary>
        public override void OnPointerClick(PointerEventData eventData)
        {
            base.OnPointerClick(eventData);
            if (!IsActive() || !IsInteractable()) return;
            if (!点按改变选中 || 选中模式 == SelectMode.Common) return;

            if (选中模式 == SelectMode.Check)
            {
                SetSelected(!_selected);
                return;
            }
            // Radio：未选中才置选中；已选中=无操作（除非组允许全部取消）
            if (_selected)
            {
                if (所属选中组 != null && 所属选中组.允许全部取消)
                    所属选中组.ClearSelection();
                return;
            }
            SetSelected(true);
        }

        /// <summary>设置选中态（带通知）。true 且在组内→路由组单选（保证互斥不变量）；false 且为组当前项→组清空</summary>
        public void SetSelected(bool value)
        {
            SetSelectedInternal(value, true);
        }

        /// <summary>设置选中态不发本组件事件（本组件视觉消费方不触发；组内换选时旧项是否发事件与调用方同 flag——UGUI SetIsOnWithoutNotify 同款透传）</summary>
        public void SetSelectedWithoutNotify(bool value)
        {
            SetSelectedInternal(value, false);
        }

        private void SetSelectedInternal(bool value, bool notify)
        {
            if (value)
            {
                if (所属选中组 != null)
                {
                    所属选中组.Select(this, notify);
                    return;
                }
                ApplySelected(true, notify);
            }
            else
            {
                if (所属选中组 != null && 所属选中组.Current == this)
                {
                    所属选中组.ClearSelection(notify);
                    return;
                }
                ApplySelected(false, notify);
            }
        }

        /// <summary>状态落值+事件（仅 SelectionGroup 调用；未变化早退）</summary>
        internal void ApplySelected(bool value, bool notify)
        {
            if (_selected == value) return;
            _selected = value;
            if (notify)
                _onSelectedChanged.Invoke(value);
        }
    }
}
