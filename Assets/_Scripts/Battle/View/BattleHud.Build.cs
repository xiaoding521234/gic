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
    /// BattleHud 程序化 UI 构建分件：BuildUi 总装 + 各区构建 + UI 基础件（Make/SetRect/AttachCombiner）
    /// + 技能盘按钮注册（表驱动 RegisterSkillButton，2026-09-18 A 案）+ 技能详情面板接线
    /// （B 案 partial 拆分，构建逻辑零变化）。
    /// </summary>
    public partial class BattleHud
    {
        private void BuildUi()
        {
            var canvasGo = new GameObject("BattleHudCanvas");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 40;
            canvasGo.AddComponent<GraphicRaycaster>();
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(2560f, 1440f);
            scaler.matchWidthOrHeight = 0.5f;

            // 高亮根（世界层，非 Canvas 子级）
            var highlightGo = new GameObject("AimHighlightRoot");
            highlightGo.transform.SetParent(transform, false);
            _highlightRoot = highlightGo.transform;

            BuildTopBar();
            BuildSkillButtons();
            BuildHandZone();
            BuildTipBar();
            BuildCancelButton();
            BuildSkillPopup();
            BuildSelectMarker();

            // 初始 = 手牌态
            if (_moveDef != null) _moveDef.rect.gameObject.SetActive(false);
            _skillZone.gameObject.SetActive(false);
            _cancelButton.gameObject.SetActive(false);
            SetTip("Battle_TipSelect");
        }

        private void BuildSkillButtons()
        {
            // 盘心（爆发圆心）屏幕坐标：距右 300 / 距底 300
            _skillZone = MakeRect("SkillZone", _canvas.transform);
            _skillZone.anchorMin = _skillZone.anchorMax = new Vector2(1f, 0f);
            _skillZone.pivot = new Vector2(1f, 0f);
            _skillZone.anchoredPosition = Vector2.zero;
            _skillZone.sizeDelta = new Vector2(爆发圆心距右 + 400f, 900f);

            // 表驱动四键（2026-09-18 A 案）：加技能键=加一行 RegisterSkillButton；
            // 爆发圆心 in-zone 坐标（zone 右下角为原点，pivot 右下）
            var burstCenter = new Vector2(爆发圆心距右, 爆发圆心距底);
            RegisterSkillButton("burst", SkillType.Burst, burstCenter);
            RegisterSkillButton("skill", SkillType.Normal, burstCenter + new Vector2(-围绕圆心距, 0f));
            RegisterSkillButton("enso", SkillType.Enso, burstCenter + new Vector2(0f, 围绕圆心距));
            RegisterSkillButton("move", SkillType.Move, null); // null=移动专位左下（决策六：移动特殊位）
        }

        /// <summary>注册技能盘按钮：Skill.prefab 实例+接线+归表。inZonePos=围绕爆发的弧位；
        /// null=移动专位左下（距左 64、圆心距底 500，设计稿 v1 定稿）。直径：爆发=技能按钮直径×爆发倍率。</summary>
        private void RegisterSkillButton(string key, SkillType type, Vector2? inZonePos)
        {
            float diameter = type == SkillType.Burst ? 技能按钮直径 * 爆发倍率 : 技能按钮直径;
            var rect = MakeSkillIconButton($"{key}_SkillIcon", diameter);
            if (rect == null) return;

            if (inZonePos.HasValue)
            {
                PlaceInZone(rect, _skillZone, inZonePos.Value);
            }
            else
            {
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 0f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = new Vector2(移动按钮距左 + 技能按钮直径 * 0.5f, 移动按钮圆心距底);
            }

            var def = new SkillButtonDef { key = key, type = type };
            WireSkillIconButton(rect, def);
            _skillButtons.Add(def);
            if (def.IsMove) _moveDef = def;
        }

        /// <summary>实例化现成 Skill.prefab（根 100×100）并等比缩放到目标直径（子件锚点全拉伸随动）</summary>
        private RectTransform MakeSkillIconButton(string name, float diameter)
        {
            var prefab = Resources.Load<GameObject>("Prefabs/UI/Skill/Skill");
            if (prefab == null)
            {
                GICLog.Error("[BattleHud] Skill.prefab 未找到，技能盘不可用");
                return null;
            }
            var instance = Instantiate(prefab, _canvas.transform, false);
            instance.name = name;
            var rect = instance.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(diameter, diameter);
            return rect;
        }

        /// <summary>接线：SkillIconView 视觉引用 + 点击转发 + 底部名称。
        /// 注意：Toggle 与 Button 同为 Selectable 不能共存（UGUI 单 Selectable 限制，2026-09-18 NRE 实锤）——
        /// 点击事件走非 Selectable 的 IPointerClickHandler 转发件；Toggle 自身 isOn 随点击翻转无副作用
        /// （ViewType.OnlyDisplay 拦截其跳转/选中环逻辑），瞄准反馈由 SetAimSelectRing 管。
        /// 四键（含移动）点击通道全统一走 OnSkillButtonClicked 点击式三情况；转发闭包直捕 def（零查找）。
        /// 名称 fallback = SkillType 表现成键（移动/战技/爆发/延奏）；选中单位后由 RefreshSkillButtons 换技能名。</summary>
        private void WireSkillIconButton(RectTransform buttonRect, SkillButtonDef def)
        {
            def.rect = buttonRect;
            def.view = buttonRect.GetComponent<SkillIconView>();

            var forwarder = buttonRect.gameObject.AddComponent<SkillClickForwarder>();
            forwarder.onClick = () => OnSkillButtonClicked(def);

            // 底部名称（prefab 无名字文本；技能名本地化条目由 RefreshSkillButtons 填）
            var labelGo = new GameObject("Name");
            var labelRect = labelGo.AddComponent<RectTransform>();
            labelRect.SetParent(buttonRect, false);
            labelRect.anchorMin = labelRect.anchorMax = new Vector2(0.5f, 0f);
            labelRect.pivot = new Vector2(0.5f, 1f);
            labelRect.anchoredPosition = new Vector2(0f, -8f);
            labelRect.sizeDelta = new Vector2(260f, 按钮名字号 + 8f);
            var labelText = labelGo.AddComponent<TextMeshProUGUI>();
            labelText.fontSize = 按钮名字号;
            labelText.color = Palette.文字米白;
            labelText.alignment = TextAlignmentOptions.Center;
            labelText.raycastTarget = false;
            var combiner = labelGo.AddComponent<TextCombiner>();
            combiner.AddEntry(def.type.GetEntry());
            def.nameText = combiner;
        }

        /// <summary>技能按钮点击转发（非 Selectable，可与 Toggle 共存——Toggle/Button 单 Selectable 限制绕行）</summary>
        private class SkillClickForwarder : MonoBehaviour, UnityEngine.EventSystems.IPointerClickHandler
        {
            public Action onClick;
            public void OnPointerClick(UnityEngine.EventSystems.PointerEventData eventData) => onClick?.Invoke();
        }

        private static void PlaceInZone(RectTransform rect, Transform zone, Vector2 inZonePos)
        {
            rect.SetParent(zone, false);
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = inZonePos;
        }

        private void BuildHandZone()
        {
            _handZone = MakeRect("HandZone", _canvas.transform);
            _handZone.anchorMin = _handZone.anchorMax = new Vector2(0.5f, 0f);
            _handZone.pivot = new Vector2(0.5f, 0f);
            _handZone.anchoredPosition = new Vector2(0f, 30f);
            _handZone.sizeDelta = new Vector2(900f, 120f);

            var handText = MakeText("HandText", _handZone, 顶栏字号 * 0.45f, Palette.文字米白);
            SetRect(handText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(880f, 44f));
            _handTextCombiner = AttachCombiner(handText); // 手牌 label + 数量（RefreshFromSnapshot 填；卡列表 B8）
        }

        /// <summary>全局提示条（独立于手牌区——选中态手牌隐藏时提示仍可见）</summary>
        private void BuildTipBar()
        {
            var rect = MakeRect("TipBar", _canvas.transform);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 168f);
            rect.sizeDelta = new Vector2(1200f, 52f);

            _tipText = MakeText("TipText", rect, 顶栏字号 * 0.5f, Palette.暖金);
            SetRect(_tipText.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            _tipCombiner = AttachCombiner(_tipText);
        }

        private void BuildCancelButton()
        {
            float d = 取消按钮直径;
            // 2026-09-18 统一化：取消钮并入 MakeActionButton（红染底盘+居中名称），不再内联手搓
            var cancel = MakeActionButton("CancelButton", null, d,
                new Color(Palette.敌方主色.r, Palette.敌方主色.g, Palette.敌方主色.b, 0.35f), labelCentered: true);
            cancel.anchorMin = cancel.anchorMax = new Vector2(1f, 1f);
            cancel.anchoredPosition = new Vector2(-取消按钮距右 - d * 0.5f, -取消按钮距顶 - d * 0.5f);

            var cancelCombiner = cancel.Find("Label")?.GetComponent<TextCombiner>();
            if (cancelCombiner != null)
                cancelCombiner.SetSingleEntry(new LocalizedString("UIText", "Cancel")); // 复用现有键（9018）

            cancel.GetComponent<Button>().onClick.AddListener(OnCancelButtonClicked);
            _cancelButton = cancel;
        }

        private void BuildSkillPopup()
        {
            // 复用现有技能详情面板（Resources/Prefabs/UI/Skill/SkillDetailPanel.prefab——背包/卡牌详情同款，
            // 图标/类型/名称/描述/参数行全本地化，自带滑入动画与点外关闭）
            var prefab = Resources.Load<GameObject>("Prefabs/UI/Skill/SkillDetailPanel");
            if (prefab == null)
            {
                GICLog.Warn("[BattleHud] SkillDetailPanel.prefab 未找到，技能详情不可用");
                return;
            }
            var popup = Instantiate(prefab, _canvas.transform, false);
            popup.name = "BattleSkillDetail";
            _skillDetailView = popup.GetComponent<SkillDetailView>();
            if (_skillDetailView == null)
            {
                GICLog.Warn("[BattleHud] SkillDetailPanel.prefab 根缺 SkillDetailView 组件");
                return;
            }

            // 关掉面板自带的点外自动关闭：与按钮点击同帧竞态——按下帧关面板抢跑抬起帧才派发的
            // PointerClick，「面板开着再点同键进瞄准」永远走重开分支（2026-09-18 实测，docs/14 §64b）；
            // 战斗的点外收面板由 OnBoardTap 已有分支承接（点棋盘=收面板保持选中）
            _skillDetailView.点外关闭 = false;

            // 面板初始隐藏，摆到技能盘左侧。关键：prefab 根即面板本体（skillDetailPanel 字段引用根），
            // 原锚=全拉伸+负边距（背包左侧全高栏设计）；曾只点锚化不落尺寸——负边距直接变负宽、
            // y 拉伸变零高，面板开而不渲染（战技/爆发/延奏"点击无反应"真身，活体实锤 rect=(-1807,0)，
            // docs/14 §63⑥）。修法：先按拉伸语义捕获设计宽（宽=参考宽+sizeDelta.x≈752@2560），
            // 点锚化后显式落尺寸。
            _skillDetailView.skillDetailPanel.SetActive(false);
            if (_skillDetailView.relatedPanel != null)
                _skillDetailView.relatedPanel.SetActive(false);
            var rect = _skillDetailView.skillDetailPanel.GetComponent<RectTransform>();
            var canvasScaler = _canvas.GetComponent<CanvasScaler>();
            float refWidth = canvasScaler != null ? canvasScaler.referenceResolution.x : 2560f;
            float designWidth = refWidth + rect.sizeDelta.x; // 拉伸语义宽=参考宽+负边距≈752
            // 勿用 canvasRect.rect.width 实时捕获——BuildUi 时 CanvasScaler 尚未应用，
            // 拿到的是裸屏宽（2026-09-18 实测 3174→面板 1366 宽，docs/14 §63⑥）
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            var pos = new Vector2(-爆发圆心距右 - 400f, 0f);
            rect.sizeDelta = new Vector2(designWidth, 详情面板高度);
            rect.anchoredPosition = pos;
            _skillDetailView.RepositionPanel(pos);

            // 关联面板（描述 <link> 跳转打开）：同病同修（原锚 y 拉伸+负边距/位是背包接线值——
            // 点锚化后高 -337、位在面板右缘屏外）。注意它是面板本体子物体（锚参照=面板 rect
            // 非画布，活体实测落点实锤）：挂面板左缘锚向左展开，滑入目标同步重定
            if (_skillDetailView.relatedPanel != null)
            {
                var relatedRect = _skillDetailView.relatedPanel.GetComponent<RectTransform>();
                if (relatedRect != null)
                {
                    float relatedWidth = relatedRect.sizeDelta.x; // 原锚 x=点锚，sizeDelta.x 即设计宽
                    relatedRect.anchorMin = relatedRect.anchorMax = new Vector2(0f, 0.5f);
                    relatedRect.pivot = new Vector2(1f, 0.5f);
                    relatedRect.sizeDelta = new Vector2(relatedWidth, 详情面板高度);
                    _skillDetailView.RepositionRelatedPanel(new Vector2(-详情面板间距, 0f));
                }
            }
        }

        /// <summary>选中单位脚下金色圆盘（世界层 quad，法线朝上；工厂出品，OnDestroy 释放材质）</summary>
        private void BuildSelectMarker()
        {
            _selectMarkerMaterial = BattleViewFactory.CreateUnlitMaterial(Palette.高亮金);
            var quad = BattleViewFactory.CreateQuad(_highlightRoot, "SelectMarker", _selectMarkerMaterial);
            quad.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            quad.transform.localScale = new Vector3(0.55f, 0.55f, 1f);
            quad.SetActive(false);
            _selectMarker = quad;
        }

        // ==================== UI 基础件 ====================

        /// <summary>程序化 UI 挂 TextCombiner：TMP 与 Combiner 必须同物体（docs/20 §2）</summary>
        private static TextCombiner AttachCombiner(TMP_Text text)
        {
            return text.gameObject.AddComponent<TextCombiner>();
        }

        private static RectTransform MakeRect(string name, Transform parent)
        {
            var go = new GameObject(name);
            var rect = go.AddComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
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

        /// <summary>圆形行动按钮（底盘 circle + 统一按压反馈 + 图标位 + 名称位；2026-09-18 统一化：
        /// 移动/取消两套手搓建法并此一件，labelCentered=true 时名称居中铺满（取消钮），
        /// false 时名称挂按钮下方。名称文本一律 TextCombiner 条目由调用方挂。</summary>
        private RectTransform MakeActionButton(string name, string iconPath, float diameter, Color baseColor, bool labelCentered = false)
        {
            var rect = MakeRect(name, _canvas.transform);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(diameter, diameter);

            var image = rect.gameObject.AddComponent<Image>();
            var circle = Resources.Load<Sprite>("UI/Skills/circle");
            image.sprite = circle;
            image.color = baseColor;
            image.raycastTarget = true;

            var button = rect.gameObject.AddComponent<Button>();
            ApplyActionButtonColors(button);

            // 图标位（iconPath null 时也建空位——RefreshSkillButtons 按选中单位填）
            {
                var iconGo = new GameObject("Icon");
                var iconRect = iconGo.AddComponent<RectTransform>();
                iconRect.SetParent(rect, false);
                iconRect.anchorMin = iconRect.anchorMax = new Vector2(0.5f, 0.5f);
                iconRect.anchoredPosition = Vector2.zero;
                iconRect.sizeDelta = new Vector2(diameter * 0.56f, diameter * 0.56f);
                var icon = iconGo.AddComponent<Image>();
                if (iconPath != null) icon.sprite = Resources.Load<Sprite>(iconPath);
                icon.preserveAspect = true;
                icon.raycastTarget = false;
            }

            var labelGo = new GameObject("Label");
            var labelRect = labelGo.AddComponent<RectTransform>();
            labelRect.SetParent(rect, false);
            if (labelCentered)
            {
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.anchoredPosition = Vector2.zero;
                labelRect.sizeDelta = Vector2.zero;
            }
            else
            {
                labelRect.anchorMin = labelRect.anchorMax = new Vector2(0.5f, 0f);
                labelRect.anchoredPosition = new Vector2(0f, -10f);
                labelRect.sizeDelta = new Vector2(240f, 按钮名字号 + 8f);
            }
            var labelText = labelGo.AddComponent<TextMeshProUGUI>();
            labelText.fontSize = labelCentered ? diameter * 0.16f : 按钮名字号;
            labelText.color = Palette.文字米白;
            labelText.alignment = TextAlignmentOptions.Center;
            labelText.raycastTarget = false;
            labelGo.AddComponent<TextCombiner>(); // 名称条目由调用方按需挂（取消=Cancel 键）

            return rect;
        }

        /// <summary>行动按钮统一按压反馈（设置/取消钮此前无反馈，2026-09-18 统一化补齐）</summary>
        private static void ApplyActionButtonColors(Button button)
        {
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.06f, 1.03f, 0.92f);
            colors.pressedColor = new Color(0.85f, 0.83f, 0.75f);
            button.colors = colors;
        }

        private Button MakeIconButton(string name, Transform parent, string iconPath,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size)
        {
            var rect = MakeRect(name, parent);
            SetRect(rect, anchorMin, anchorMax, position, size);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = Resources.Load<Sprite>(iconPath);
            var button = rect.gameObject.AddComponent<Button>();
            ApplyActionButtonColors(button); // 与行动按钮同款按压反馈（2026-09-18 统一化）
            return button;
        }
    }
}
