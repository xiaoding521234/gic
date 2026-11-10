using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using GIC.Data;
using GIC.Framework;
using GIC.Tool;
using GIC.UI;

namespace GIC.Battle
{
    public partial class BattleHud
    {
        private GameObject _constellationPanel;
        private bool _upgradeHasBranch;
        private int _constellationChoice;
        private readonly List<Image> _constellationCardBackgrounds = new List<Image>();
        private readonly List<Image> _constellationBranchBadges = new List<Image>();
        private readonly List<Outline> _constellationCardOutlines = new List<Outline>();
        private readonly List<int> _constellationCardIds = new List<int>();

        /// <summary>分支命座双卡选择；旧单体命座不创建面板，原确认链保持不变。</summary>
        private void ShowConstellationOptions(int unitNameValue)
        {
            HideConstellationOptions();
            var config = Wargame.Instance?.Context?.Get<UnitConfig>();
            var data = config?.GetUnitData((UnitName)unitNameValue);
            if (data == null || !data.HasBranchConstellation || CanvasRect == null) return;

            int currentLevel = 0;
            var snapshot = _session?.Player?.LatestSnapshot;
            if (snapshot?.units != null)
                foreach (var unit in snapshot.units)
                    if (unit.playerId == _myPlayerId && unit.unitName == data.unitName.ToString())
                    {
                        currentLevel = unit.constellation;
                        break;
                    }
            int nextLevel = currentLevel + 1;
            if (nextLevel > data.constellationTiers.Count) return;
            var options = data.constellationTiers[nextLevel - 1].options;
            if (options == null || options.Length < 2 || options[0]?.skill?.data == null
                || options[1]?.skill?.data == null) return;

            _upgradeHasBranch = true;
            _constellationChoice = 0;
            _constellationPanel = new GameObject("ConstellationChoices", typeof(RectTransform), typeof(Image));
            _constellationPanel.transform.SetParent(CanvasRect, false);
            var panelRect = (RectTransform)_constellationPanel.transform;
            panelRect.anchorMin = panelRect.anchorMax = panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = Vector2.zero;
            panelRect.sizeDelta = new Vector2(760f, 390f);
            var panelImage = _constellationPanel.GetComponent<Image>();
            panelImage.color = new Color(0.025f, 0.035f, 0.05f, 0.94f);
            panelImage.raycastTarget = true;

            BuildConstellationCard(panelRect, options[0].skill.data, nextLevel, 0, new Vector2(-185f, 0f));
            BuildConstellationCard(panelRect, options[1].skill.data, nextLevel, 1, new Vector2(185f, 0f));
        }

        private void BuildConstellationCard(RectTransform parent, SkillConfig.SkillData data, int level,
            int branch, Vector2 position)
        {
            int choiceId = (int)data.skillID;
            var go = new GameObject($"C{level}{(branch == 0 ? "A" : "B")}",
                typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(340f, 340f);
            var background = go.GetComponent<Image>();
            background.color = new Color(0.10f, 0.13f, 0.17f, 0.98f);
            _constellationCardBackgrounds.Add(background);
            var outline = go.GetComponent<Outline>();
            outline.effectColor = new Color(0.40f, 0.46f, 0.52f, 0.45f);
            outline.effectDistance = new Vector2(2f, 2f);
            _constellationCardOutlines.Add(outline);
            _constellationCardIds.Add(choiceId);
            var button = go.GetComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(() => SelectConstellationOption(choiceId));

            CreateConstellationLabel(rect, "Tier", new Vector2(-120f, 145f), new Vector2(62f, 26f), 16f,
                $"{level}命", null, new Color(0.66f, 0.72f, 0.78f, 1f), null);
            CreateConstellationBranchBadge(rect, branch == 0 ? "A" : "B");

            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(rect, false);
            var iconRect = (RectTransform)iconGo.transform;
            iconRect.anchoredPosition = new Vector2(0f, 75f);
            iconRect.sizeDelta = new Vector2(82f, 82f);
            var icon = iconGo.GetComponent<Image>();
            icon.preserveAspect = true;
            MissingImageGuard.Assign(icon, data.icon);
            icon.raycastTarget = false;

            CreateConstellationLabel(rect, "Name", new Vector2(0f, 5f), new Vector2(310f, 40f), 25f,
                null, data.GetNameEntry(), null);
            CreateConstellationLabel(rect, "Tag", new Vector2(0f, -32f), new Vector2(150f, 30f), 19f,
                null, data.GetTagEntry(), new Color(0.95f, 0.76f, 0.30f, 1f));
            CreateConstellationLabel(rect, "Description", new Vector2(0f, -112f), new Vector2(300f, 125f), 20f,
                null, data.GetDescriptionEntry(), null, data);
        }

        private void CreateConstellationBranchBadge(Transform parent, string text)
        {
            var go = new GameObject("Branch", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchoredPosition = new Vector2(120f, 145f);
            rect.sizeDelta = new Vector2(34f, 26f);
            var background = go.GetComponent<Image>();
            background.color = new Color(0.28f, 0.34f, 0.40f, 0.8f);
            _constellationBranchBadges.Add(background);
            CreateConstellationLabel(rect, "Text", Vector2.zero, new Vector2(32f, 26f), 16f,
                text, null, Color.white, null);
        }

        private TextMeshProUGUI CreateConstellationLabel(Transform parent, string name, Vector2 position,
            Vector2 size, float fontSize, string literal, TextEntry entry, Color? color,
            SkillConfig.SkillData descriptionData = null)
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
            text.alignment = TextAlignmentOptions.Center;
            text.color = color ?? Color.white;
            text.raycastTarget = false;
            text.overflowMode = TextOverflowModes.Overflow;
            if (literal != null) text.text = literal;
            if (entry != null)
            {
                var combiner = go.AddComponent<TextCombiner>();
                combiner.最大宽度 = size.x;
                combiner.最小字号倍率 = 0.65f;
                if (descriptionData != null)
                    combiner.textProcessor = template => SkillDescriptionBuilder.Build(template,
                        descriptionData.customParams);
                combiner.AddEntry(entry);
            }
            return text;
        }

        private void SelectConstellationOption(int choiceId)
        {
            if (!_upgradeHasBranch) return;
            _constellationChoice = choiceId;
            for (int i = 0; i < _constellationCardBackgrounds.Count; i++)
            {
                bool selected = _constellationCardIds[i] == choiceId;
                _constellationCardBackgrounds[i].color = selected
                    ? new Color(0.42f, 0.31f, 0.10f, 1f)
                    : new Color(0.10f, 0.13f, 0.17f, 0.98f);
                if (i < _constellationBranchBadges.Count)
                    _constellationBranchBadges[i].color = selected
                        ? new Color(0.95f, 0.76f, 0.30f, 0.95f)
                        : new Color(0.28f, 0.34f, 0.40f, 0.8f);
                if (i < _constellationCardOutlines.Count)
                    _constellationCardOutlines[i].effectColor = selected
                        ? new Color(0.95f, 0.76f, 0.30f, 0.95f)
                        : new Color(0.40f, 0.46f, 0.52f, 0.45f);
            }
        }

        private void HideConstellationOptions()
        {
            if (_constellationPanel != null) Destroy(_constellationPanel);
            _constellationPanel = null;
            _constellationCardBackgrounds.Clear();
            _constellationBranchBadges.Clear();
            _constellationCardOutlines.Clear();
            _constellationCardIds.Clear();
            _constellationChoice = 0;
            _upgradeHasBranch = false;
        }
    }
}
