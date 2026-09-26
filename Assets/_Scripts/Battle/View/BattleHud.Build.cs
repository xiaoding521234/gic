using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Localization;
using GIC.Framework;
using GIC.Data;
using GIC.Tool;
using GIC.UI;

namespace GIC.Battle
{
    /// <summary>
    /// BattleHud prefab 接线分件（2026-09-22 全项目统一批次：程序化构建退役为 prefab 装配）。
    /// 结构真源 = Resources/Prefabs/Battle/BattleHud.prefab（一次性迁移工具 BattleHudPrefabMigration
    /// 从旧 Build* 烘焙，零视觉漂移；改结构/摆位/字体/尺寸在编辑器里做——本分件与 TopBar/Layout
    /// 分件只做运行时寻址+接线+Palette 活色）。节点契约：画布下 Slots/{key}（槽内首子级=控件本体）、
    /// 控件名沿用旧构建命名（TurnPhaseText/HandZone/CancelButton…）。
    /// 动态件仍为运行时建：选中标记（世界层材质不可入库）、队列槽重建、伤害数字等。
    /// </summary>
    public partial class BattleHud
    {
        private void ResolveHudReferences()
        {
            _canvas = GetComponentInChildren<Canvas>();
            if (_canvas == null)
            {
                GICLog.Error("[BattleHud] prefab 缺 Canvas，HUD 不可用");
                return;
            }
            var scaler = _canvas.GetComponent<CanvasScaler>();
            _refSize = scaler != null ? scaler.referenceResolution : new Vector2(2560f, 1440f);

            _highlightRoot = transform.Find("AimHighlightRoot");
            if (_highlightRoot == null) GICLog.Warn("[BattleHud] prefab 缺 AimHighlightRoot（世界层高亮根）");

            ResolveLayoutSlots();  // Layout 分件：14 槽寻址+编辑态附件+默认 UV 捕获（先建槽表，后续寻址全依赖它）
            ResolveTopBar();       // TopBar 分件：回合中枢/倒计时/时钟/队列/双方信息块/设置钮
            ResolveSkillButtons(); // 技能盘四键（SkillIconView/点击转发/名称条）
            ResolveMiscWidgets();   // 手牌/提示/取消/布局入口/编辑工具栏
            ResolveSkillPopup();   // 技能详情面板（prefab 嵌套实例接线）

            if (Application.isPlaying) CreateSelectMarker(); // 世界层运行时材质，不入 prefab

            // 初始 = 手牌态（件显隐统一走 ApplyStateVisibility；方案应用在 Bind 尾按存档激活槽执行）
            ApplyStateVisibility();
            SetTip("Battle_TipSelect");
        }

        /// <summary>技能盘四键接线（表驱动不变，2026-09-18 A 案）：prefab 槽内 Skill 嵌套实例寻址。
        /// 加技能键=BattleHud.prefab 加槽节点（内放 Skill.prefab 实例+Name 标签）+ 此表加一行。</summary>
        private void ResolveSkillButtons()
        {
            var table = new (string key, SkillType type)[]
            {
                ("burst", SkillType.Burst),
                ("skill", SkillType.Normal),
                ("enso", SkillType.Enso),
                ("move", SkillType.Move),
            };
            foreach (var row in table)
            {
                if (!_layoutByKey.TryGetValue(row.key, out var def))
                {
                    GICLog.Warn($"[BattleHud] 布局槽缺失：{row.key}");
                    continue;
                }
                var view = def.content.GetComponent<SkillIconView>();
                if (view == null)
                {
                    GICLog.Warn($"[BattleHud] 槽 {row.key} 控件缺 SkillIconView");
                    continue;
                }

                var buttonDef = new SkillButtonDef { key = row.key, type = row.type, rect = def.content, view = view };
                var forwarder = def.content.GetComponent<SkillClickForwarder>();
                if (forwarder == null)
                    forwarder = def.content.gameObject.AddComponent<SkillClickForwarder>();
                var captured = buttonDef; // 闭包捕获
                forwarder.onClick = () => OnSkillButtonClicked(captured);
                // 拖动式瞄准转发（B4，2026-09-26）：拖过 UGUI 阈值起拖动瞄准——按下未拖/拖回原键松手
                // 仍走点击式三情况（指针未离键时 UGUI 不判拖拽起手/点击照发）
                var dragForwarder = def.content.GetComponent<SkillDragForwarder>();
                if (dragForwarder == null)
                    dragForwarder = def.content.gameObject.AddComponent<SkillDragForwarder>();
                dragForwarder.onBeginDrag = e => OnSkillButtonDragBegin(captured, e);
                dragForwarder.onDrag = e => OnSkillButtonDrag(captured, e);
                dragForwarder.onEndDrag = e => OnSkillButtonDragEnd(captured, e);
                buttonDef.nameText = def.content.Find("Name")?.GetComponent<TextCombiner>();
                _skillButtons.Add(buttonDef);
                if (buttonDef.IsMove) _moveDef = buttonDef;
            }
        }

        /// <summary>手牌/提示/取消 + 布局编辑入口与工具栏（Layout 分件）寻址接线</summary>
        private void ResolveMiscWidgets()
        {
            if (_layoutByKey.TryGetValue("hand", out var hand))
            {
                _handZone = hand.content;
                _handTextCombiner = _handZone.Find("HandText")?.GetComponent<TextCombiner>();
                RecolorText(_handTextCombiner, Palette.文字米白);

                // 手牌滚动壳寻址（2026-09-23 审查 R1：HandCards→HandScroll→HandViewport→HandContent 四层
                // 结构真源=BattleHud.prefab 的 hand 槽内——pivot/锚点/Elastic/敏感度 30 全在编辑器调，
                // 代码只寻址+建卡条目，勿再程序化建壳）
                var shell = _handZone.Find("HandCards");
                if (shell == null)
                {
                    GICLog.Warn("[BattleHud] prefab 缺手牌滚动壳（Slots/hand 内 HandCards），手牌不显示");
                }
                else
                {
                    _handScroll = shell.Find("HandScroll")?.GetComponent<UnityEngine.UI.ScrollRect>();
                    _handContent = shell.Find("HandScroll/HandViewport/HandContent") as RectTransform;
                    _handCardsRect = shell as RectTransform; // 手牌下沉热区的被移动体（2026-09-26）
                    if (_handScroll == null || _handContent == null)
                        GICLog.Warn("[BattleHud] 手牌滚动壳不完整（需 HandScroll(ScrollRect)/HandViewport(RectMask2D)/HandContent）");
                }
            }
            else GICLog.Warn("[BattleHud] 布局槽缺失：hand");

            if (_layoutByKey.TryGetValue("tip", out var tip))
            {
                _tipText = tip.content.Find("TipText")?.GetComponent<TMP_Text>();
                _tipCombiner = _tipText != null ? _tipText.GetComponent<TextCombiner>() : null;
                if (_tipText != null) _tipText.color = Palette.暖金;
            }
            else GICLog.Warn("[BattleHud] 布局槽缺失：tip");

            if (_layoutByKey.TryGetValue("cancel", out var cancel))
            {
                _cancelButton = cancel.content;
                var image = _cancelButton.GetComponent<Image>();
                if (image != null)
                    image.color = new Color(Palette.敌方主色.r, Palette.敌方主色.g, Palette.敌方主色.b, 0.35f);
                var button = _cancelButton.GetComponent<Button>();
                if (button != null) button.onClick.AddListener(OnCancelButtonClicked);
            }
            else GICLog.Warn("[BattleHud] 布局槽缺失：cancel");

            // 完成选择按钮（2026-09-26：顶部阶段级按钮，prefab 烘焙=祈愿 Marketplace 同款样式；
            // 标签本地化=运行时 AddEntry（同技能 Name 标签模式），prefab 烘焙文本仅兜底预览）
            if (_layoutByKey.TryGetValue("confirm", out var confirm))
            {
                _confirmButton = confirm.content.GetComponent<Button>();
                if (_confirmButton != null)
                    _confirmButton.onClick.AddListener(OnConfirmButtonClicked);
                else GICLog.Warn("[BattleHud] confirm 槽控件缺 Button，完成选择不可用");
                var label = confirm.content.Find("Text (TMP)")?.GetComponent<TextCombiner>();
                if (label != null)
                {
                    label.ClearAllEntries();
                    label.AddEntry(new LocalizedString("UIText", "Battle_ConfirmSelect"));
                }
                else GICLog.Warn("[BattleHud] confirm 槽缺 Text (TMP) 标签（TextCombiner）");
            }
            else GICLog.Warn("[BattleHud] 布局槽缺失：confirm");

            ResolveLayoutEntryButton(); // Layout 分件
            ResolveLayoutToolbar();     // Layout 分件
        }

        /// <summary>技能详情面板接线（prefab 内嵌套实例；点锚化落位已烘焙，读回显式重定一次滑入目标防漂移）</summary>
        private void ResolveSkillPopup()
        {
            var popup = _canvas.transform.Find("BattleSkillDetail");
            _skillDetailView = popup != null ? popup.GetComponent<SkillDetailView>() : null;
            if (_skillDetailView == null)
            {
                GICLog.Warn("[BattleHud] prefab 缺技能详情面板（BattleSkillDetail），技能详情不可用");
                return;
            }

            // 点外自动关闭让位：与按钮点击同帧竞态（docs/14 §64b）——战斗的点外收面板由 OnBoardTap 承接
            _skillDetailView.点外关闭 = false;

            var rect = _skillDetailView.skillDetailPanel.GetComponent<RectTransform>();
            _skillDetailView.RepositionPanel(rect.anchoredPosition);
            if (_skillDetailView.relatedPanel != null)
            {
                var relatedRect = _skillDetailView.relatedPanel.GetComponent<RectTransform>();
                _skillDetailView.RepositionRelatedPanel(relatedRect.anchoredPosition);
            }
        }

        /// <summary>选中单位脚下金色圆盘标记（世界层 quad，法线朝上；工厂出品，OnDestroy 释放材质）——运行时建（材质不可入 prefab）</summary>
        private void CreateSelectMarker()
        {
            _selectMarkerMaterial = BattleViewFactory.CreateUnlitMaterial(Palette.高亮金);
            var quad = BattleViewFactory.CreateQuad(_highlightRoot, "SelectMarker", _selectMarkerMaterial);
            quad.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            quad.transform.localScale = new Vector3(0.55f, 0.55f, 1f);
            quad.SetActive(false);
            _selectMarker = quad;
        }

        // ==================== 运行时 UI 基础件（动态件仍用：队列槽重建/文本挂接） ====================

        /// <summary>程序化 UI 挂 TextCombiner：TMP 与 Combiner 必须同物体（docs/20 §2）</summary>
        private static TextCombiner AttachCombiner(TMP_Text text)
        {
            return text.gameObject.AddComponent<TextCombiner>();
        }

        /// <summary>Palette 活色：TextCombiner 的 TMP 同步染色（烘焙色仅兜底，改资产随下局生效）</summary>
        private static void RecolorText(TextCombiner combiner, Color color)
        {
            if (combiner?.textComponent != null) combiner.textComponent.color = color;
        }

        private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private TextMeshProUGUI MakeText(string name, Transform parent, float fontSize, Color color)
        {
            var go = new GameObject(name);
            var rect = go.AddComponent<RectTransform>();
            rect.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>技能按钮点击转发（非 Selectable，可与 Toggle 共存——Toggle/Button 单 Selectable 限制绕行）</summary>
        private class SkillClickForwarder : MonoBehaviour, UnityEngine.EventSystems.IPointerClickHandler
        {
            public Action onClick;
            public void OnPointerClick(UnityEngine.EventSystems.PointerEventData eventData) => onClick?.Invoke();
        }

        /// <summary>技能按钮拖动转发（拖动式瞄准，B4 2026-09-26）：与 SkillClickForwarder 同思路——
        /// Toggle 是 Selectable 非 IDragHandler 宿主，拖拽事件由独立转发件承载。事件冒泡说明：
        /// 命中在控件/图标上时冒泡至本件（content 层）；布局编辑期命中在拖拽板（DragPlate，slot 直属）
        /// 上时冒泡至 slot 的 LayoutDragHandler，与本件互不串扰（编辑期拖动瞄准由
        /// OnSkillButtonDragBegin 的 _layoutEditing 守卫双保险）。</summary>
        private class SkillDragForwarder : MonoBehaviour,
            UnityEngine.EventSystems.IBeginDragHandler,
            UnityEngine.EventSystems.IDragHandler,
            UnityEngine.EventSystems.IEndDragHandler
        {
            public Action<UnityEngine.EventSystems.PointerEventData> onBeginDrag;
            public Action<UnityEngine.EventSystems.PointerEventData> onDrag;
            public Action<UnityEngine.EventSystems.PointerEventData> onEndDrag;

            public void OnBeginDrag(UnityEngine.EventSystems.PointerEventData eventData) => onBeginDrag?.Invoke(eventData);
            public void OnDrag(UnityEngine.EventSystems.PointerEventData eventData) => onDrag?.Invoke(eventData);
            public void OnEndDrag(UnityEngine.EventSystems.PointerEventData eventData) => onEndDrag?.Invoke(eventData);
        }
    }
}
