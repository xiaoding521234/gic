using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.Localization;
using UnityEngine.UI;
using GIC.Framework;
using GIC.Battle;
using GIC.Data;
using GIC.Tool;

namespace GIC.UI
{
    /// <summary>
    /// 卡组管理面板（DeckSwitchPanel）的单行视图（v2 运行时实例化，预制体=Resources/Prefabs/Backpack/DeckRow.prefab）：
    /// 拖拽把手 + 编号 + 名称（点击改名）+ 8 张完整卡牌预览容器 + 复制/粘贴/导出/删除按钮 + 当前卡组高亮框。
    /// 行身份不落在行上——面板按"容器下标=显示顺序=deckOrder 下标"实时解析。
    /// 手势分工：行背景点击=选中当前卡组；拖拽把手（DeckRowDragHandle）=拖拽排序（把手同时消耗点击，不触发行选中）。
    /// 预览卡牌由面板通过共享 CardPool 填充（previewRoot 只做容器），本类不持有卡牌。
    /// </summary>
    public class DeckRowView : MonoBehaviour, IPointerClickHandler
    {
        [InspectorName("编号文本")] public TextMeshProUGUI numberText;
        [InspectorName("名称文本")] public TextMeshProUGUI nameText;
        [InspectorName("改名按钮（覆盖在名称上）")] public Button nameButton;
        [InspectorName("行背景")] public Image rowBackground;
        [InspectorName("当前卡组高亮框")] public Image currentHighlight;
        [InspectorName("拖拽浮层（拖动时的视觉反馈）")] public Image dragOverlay;
        [InspectorName("拖拽把手")] public DeckRowDragHandle dragHandle;
        [InspectorName("卡牌预览容器")] public Transform previewRoot;
        [InspectorName("复制按钮")] public Button copyButton;
        [InspectorName("粘贴按钮")] public Button pasteButton;
        [InspectorName("导出密语按钮")] public Button exportButton;
        [InspectorName("删除按钮")] public Button deleteButton;

        /// <summary>所属面板（Init 时注入，按钮与把手向它转发事件）</summary>
        public DeckSwitchPanel Panel { get; private set; }

        /// <summary>显示位（=容器内下标，显示编号-1）；卡组 Id 由面板经 deckOrder 解析</summary>
        public int DisplayIndex => transform.GetSiblingIndex();

        private TextCombiner _numberCombiner;
        private TextCombiner _nameCombiner;

        public void Init(DeckSwitchPanel panel)
        {
            Panel = panel;

            nameButton?.onClick.AddListener(() => Panel?.OnRenameClicked(this));
            copyButton?.onClick.AddListener(() => Panel?.OnCopyClicked(this));
            pasteButton?.onClick.AddListener(() => Panel?.OnPasteClicked(this));
            exportButton?.onClick.AddListener(() => Panel?.OnExportClicked(this));
            deleteButton?.onClick.AddListener(() => Panel?.OnDeleteClicked(this));
            dragHandle?.Init(this);
        }

        // ==================== 刷新（由面板驱动） ====================

        /// <summary>显示编号（拖拽/重排/增删后调用）</summary>
        public void SetNumber(int displayNumber)
        {
            EnsureCombiners();
            _numberCombiner.SetSingleEntry(displayNumber.ToString());
        }

        /// <summary>显示名称：自定义名优先，空 = 本地化默认"卡组N"</summary>
        public void SetName(string customName, LocalizedString defaultName)
        {
            EnsureCombiners();
            if (!string.IsNullOrEmpty(customName))
                _nameCombiner.SetSingleEntry(customName);
            else
                _nameCombiner.SetSingleEntry(defaultName);
        }

        /// <summary>当前卡组高亮（纯显隐切换，样式在预制体里调）</summary>
        public void SetCurrent(bool isCurrent)
        {
            if (currentHighlight != null)
                currentHighlight.gameObject.SetActive(isCurrent);
        }

        /// <summary>拖拽中视觉反馈（浮层显隐；置顶换序由面板负责）</summary>
        public void SetDragState(bool dragging)
        {
            if (dragOverlay != null)
                dragOverlay.gameObject.SetActive(dragging);
        }

        // ==================== 手势（点击选中；拖拽排序走把手转发面板） ====================

        /// <summary>行背景点击 = 选中该卡组为当前卡组（面板保持打开）；把手/按钮区域的点击不会到达这里</summary>
        public void OnPointerClick(PointerEventData eventData)
        {
            Panel?.OnRowSelected(this);
        }

        public void OnDestroy()
        {
            // 实例化行随 RebuildRows 销毁：只清自身监听（面板侧列表随重建同步）
            nameButton?.onClick.RemoveAllListeners();
            copyButton?.onClick.RemoveAllListeners();
            pasteButton?.onClick.RemoveAllListeners();
            exportButton?.onClick.RemoveAllListeners();
            deleteButton?.onClick.RemoveAllListeners();
        }

        private void EnsureCombiners()
        {
            if (_numberCombiner == null && numberText != null)
                _numberCombiner = numberText.GetComponent<TextCombiner>() ?? numberText.gameObject.AddComponent<TextCombiner>();
            if (_nameCombiner == null && nameText != null)
                _nameCombiner = nameText.GetComponent<TextCombiner>() ?? nameText.gameObject.AddComponent<TextCombiner>();
        }
    }
}
