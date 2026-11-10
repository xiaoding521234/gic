using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using GIC.Battle;
using GIC.Data;
using GIC.Tool;

namespace GIC.UI
{
    /// <summary>分支命座专属卡片墙：三级两列，主标题只写技能名，层级与 A/B 降为角标。</summary>
    public class ConstellationDetailView : MonoBehaviour
    {
        private RectTransform _root;
        private RectTransform _content;

        public void Init(UnitConfig.UnitData unitData, IReadOnlyList<int> selectedChoices = null)
        {
            EnsureRoot();
            for (int i = _content.childCount - 1; i >= 0; i--)
                Destroy(_content.GetChild(i).gameObject);
            if (unitData == null || !unitData.HasBranchConstellation)
            {
                _root.gameObject.SetActive(false);
                return;
            }

            _root.gameObject.SetActive(true);
            int tiers = Mathf.Min(3, unitData.constellationTiers.Count);
            for (int level = 1; level <= tiers; level++)
            {
                var options = unitData.constellationTiers[level - 1].options;
                if (options == null || options.Length < 2) continue;
                var row = NewRow(level);
                BuildCard(row, options[0]?.skill?.data, level, 0, selectedChoices);
                BuildCard(row, options[1]?.skill?.data, level, 1, selectedChoices);
            }
            RefreshLayout();
        }

        public void Hide() => _root?.gameObject.SetActive(false);

        private void EnsureRoot()
        {
            if (_root != null) return;
            var go = new GameObject("ConstellationDetail", typeof(RectTransform), typeof(ScrollRect));
            go.transform.SetParent(transform, false);
            _root = (RectTransform)go.transform;
            _root.anchorMin = Vector2.zero;
            _root.anchorMax = Vector2.one;
            _root.offsetMin = new Vector2(10f, 10f);
            _root.offsetMax = new Vector2(-10f, -10f);

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            viewport.transform.SetParent(_root, false);
            var viewportRect = (RectTransform)viewport.transform;
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = viewportRect.offsetMax = Vector2.zero;
            viewport.GetComponent<Image>().color = Color.white; // Mask 模板写入要求 alpha=1；showMaskGraphic=false 隐去底板
            viewport.GetComponent<Mask>().showMaskGraphic = false;

            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter));
            content.transform.SetParent(viewportRect, false);
            _content = (RectTransform)content.transform;
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.anchoredPosition = Vector2.zero;
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 12f;
            layout.padding = new RectOffset(4, 4, 4, 4);
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = go.GetComponent<ScrollRect>();
            scroll.viewport = viewportRect;
            scroll.content = _content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28f;
        }

        private RectTransform NewRow(int level)
        {
            var go = new GameObject($"Tier{level}", typeof(RectTransform), typeof(HorizontalLayoutGroup),
                typeof(LayoutElement));
            go.transform.SetParent(_content, false);
            var layout = go.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 12f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            go.GetComponent<LayoutElement>().preferredHeight = 208f;
            return (RectTransform)go.transform;
        }

        private void BuildCard(Transform parent, SkillConfig.SkillData data, int level, int branch,
            IReadOnlyList<int> selectedChoices)
        {
            if (data == null) return;
            int choiceId = (int)data.skillID;
            bool hasRuntimeChoices = selectedChoices != null;
            bool locked = hasRuntimeChoices && selectedChoices.Count < level;
            bool selected = hasRuntimeChoices && selectedChoices.Count >= level
                            && selectedChoices[level - 1] == choiceId;
            bool siblingSelected = hasRuntimeChoices && selectedChoices.Count >= level && !selected;

            var go = new GameObject(data.skillID.ToString(), typeof(RectTransform), typeof(Image),
                typeof(CanvasGroup), typeof(LayoutElement), typeof(Outline));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(250f, 208f);
            go.GetComponent<LayoutElement>().preferredHeight = 208f;
            go.GetComponent<LayoutElement>().flexibleWidth = 1f;
            var background = go.GetComponent<Image>();
            background.color = locked ? new Color(0.035f, 0.045f, 0.06f, 0.98f)
                : selected ? new Color(0.24f, 0.20f, 0.10f, 0.98f)
                : new Color(0.075f, 0.10f, 0.14f, 0.98f);
            var outline = go.GetComponent<Outline>();
            outline.effectColor = locked ? new Color(0.18f, 0.21f, 0.24f, 0.35f)
                : selected ? new Color(0.95f, 0.76f, 0.30f, 0.95f)
                : new Color(0.40f, 0.46f, 0.52f, 0.45f);
            outline.effectDistance = new Vector2(2f, 2f);
            go.GetComponent<CanvasGroup>().alpha = locked ? 0.42f
                : selected ? 1f : siblingSelected ? 0.52f : 0.78f;

            NewText(rect, "Tier", new Vector2(-94f, 86f), new Vector2(58f, 24f), 15f,
                $"{level}命", new Color(0.66f, 0.72f, 0.78f, 1f), null, null, TextAlignmentOptions.Left);
            NewBadge(rect, branch == 0 ? "A" : "B", selected);
            if (locked) NewLockIcon(rect);

            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(rect, false);
            var iconRect = (RectTransform)iconGo.transform;
            iconRect.anchoredPosition = new Vector2(0f, 53f);
            iconRect.sizeDelta = new Vector2(50f, 50f);
            var icon = iconGo.GetComponent<Image>();
            icon.preserveAspect = true;
            MissingImageGuard.Assign(icon, data.icon);
            icon.raycastTarget = false;

            NewText(rect, "Name", new Vector2(0f, 3f), new Vector2(230f, 34f), 22f,
                null, Color.white, data.GetNameEntry(), null);
            NewText(rect, "Tag", new Vector2(0f, -28f), new Vector2(120f, 24f), 15f,
                null, new Color(0.95f, 0.76f, 0.30f, 1f), data.GetTagEntry(), null);
            NewText(rect, "Description", new Vector2(0f, -82f), new Vector2(220f, 76f), 16f,
                null, new Color(0.86f, 0.88f, 0.90f, 1f), data.GetDescriptionEntry(), data);
        }

        private void NewLockIcon(Transform parent)
        {
            var go = new GameObject("Lock", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(98f, -86f);
            rect.sizeDelta = new Vector2(24f, 24f);
            var color = new Color(0.66f, 0.72f, 0.78f, 0.9f);
            NewLockBar(rect, "ShackleTop", new Vector2(0f, 7f), new Vector2(12f, 2f), color);
            NewLockBar(rect, "ShackleLeft", new Vector2(-5f, 3f), new Vector2(2f, 8f), color);
            NewLockBar(rect, "ShackleRight", new Vector2(5f, 3f), new Vector2(2f, 8f), color);
            NewLockBar(rect, "Body", new Vector2(0f, -5f), new Vector2(18f, 13f), color);
        }

        private static void NewLockBar(Transform parent, string name, Vector2 position, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        private void NewBadge(Transform parent, string text, bool selected)
        {
            var go = new GameObject("Branch", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchoredPosition = new Vector2(98f, 86f);
            rect.sizeDelta = new Vector2(32f, 24f);
            var image = go.GetComponent<Image>();
            image.color = selected ? new Color(0.95f, 0.76f, 0.30f, 0.95f)
                : new Color(0.28f, 0.34f, 0.40f, 0.8f);
            NewText(rect, "Text", Vector2.zero, new Vector2(30f, 24f), 15f, text,
                selected ? new Color(0.10f, 0.08f, 0.03f, 1f) : Color.white, null, null);
        }

        private TextMeshProUGUI NewText(Transform parent, string name, Vector2 position, Vector2 size,
            float fontSize, string literal, Color color, TextEntry entry, SkillConfig.SkillData data,
            TextAlignmentOptions? alignment = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var text = go.AddComponent<TextMeshProUGUI>();
            text.font = BattleViewFactory.WorldTextFont;
            text.fontSize = fontSize;
            text.alignment = alignment ?? TextAlignmentOptions.Center;
            text.color = color;
            text.raycastTarget = false;
            text.overflowMode = TextOverflowModes.Overflow;
            if (literal != null) text.text = literal;
            if (entry != null)
            {
                var combiner = go.AddComponent<TextCombiner>();
                combiner.最大宽度 = size.x;
                combiner.最小字号倍率 = 0.65f;
                if (data != null)
                    combiner.textProcessor = template => SkillDescriptionBuilder.Build(template, data.customParams);
                combiner.AddEntry(entry);
            }
            return text;
        }

        public void RefreshLayout()
        {
            if (_content == null) return;
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_root);
        }
    }
}
