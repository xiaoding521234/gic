using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Localization;
using GIC.Data;
using GIC.Tool;
using GIC.UI;

namespace GIC.Battle
{
    /// <summary>
    /// BattleHud 顶栏分件：回合中枢（回合数+阶段徽章）/选择倒计时/战斗时钟/攻速队列条/双方信息块的
    /// 字段、构建与刷新（2026-09-18 B 案 partial 拆分，纯搬运零逻辑变化）。
    /// </summary>
    public partial class BattleHud
    {
        // 顶栏：回合中枢 + 倒计时 + 时钟（本地化 = TextCombiner；数字 = 静态条目）
        private TMP_Text _turnPhaseText;
        private TextCombiner _turnPhaseCombiner;
        private TMP_Text _countdownText;
        private TextCombiner _countdownCombiner;
        private TMP_Text _clockText;
        private TextCombiner _clockCombiner;

        // 攻速队列条（选择阶段=执行预览；执行阶段高亮当前片）
        private RectTransform _queueBar;
        private TMP_Text _queueLabelText;
        private TextCombiner _queueLabelCombiner;
        private readonly List<QueueSlot> _queueSlots = new List<QueueSlot>();

        // 双方信息块（徽标 + 体力/摩拉 chip；协议核心血条 B8 接入）
        private PlayerBlock _myBlock;
        private PlayerBlock _enemyBlock;

        private class QueueSlot
        {
            public Image frame;
            public TextCombiner speed;
            public int attackSpeed;
            public Color baseColor;
        }

        private class PlayerBlock
        {
            public TextCombiner stamina;
            public TextCombiner mora;
        }

        private void BuildTopBar()
        {
            var bar = MakeRect("TopBar", _canvas.transform);
            bar.anchorMin = bar.anchorMax = new Vector2(0.5f, 1f);
            bar.pivot = new Vector2(0.5f, 1f);
            bar.anchoredPosition = Vector2.zero;
            bar.sizeDelta = new Vector2(2200f, 260f);

            // 回合中枢（顶部中央）：回合数 + 阶段徽章
            _turnPhaseText = MakeText("TurnPhaseText", bar, 顶栏字号, Palette.文字米白);
            SetRect(_turnPhaseText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -14f), new Vector2(1100f, 56f));
            _turnPhaseCombiner = AttachCombiner(_turnPhaseText);

            // 选择倒计时（选择阶段常显；数字条目）
            _countdownText = MakeText("CountdownText", bar, 顶栏字号 * 0.6f, Palette.暖金);
            SetRect(_countdownText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -76f), new Vector2(300f, 44f));
            _countdownCombiner = AttachCombiner(_countdownText);
            _countdownText.gameObject.SetActive(false);

            // 战斗时钟（独立时钟 6:00 起 +20/回合）
            _clockText = MakeText("ClockText", bar, 顶栏字号 * 0.55f, Palette.文字米白);
            SetRect(_clockText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -126f), new Vector2(300f, 40f));
            _clockCombiner = AttachCombiner(_clockText);

            // 攻速队列条（顶栏下缘）：label + 槽位
            _queueBar = MakeRect("QueueBar", bar);
            _queueBar.anchorMin = _queueBar.anchorMax = new Vector2(0.5f, 1f);
            _queueBar.pivot = new Vector2(0.5f, 1f);
            _queueBar.anchoredPosition = new Vector2(0f, -170f);
            _queueBar.sizeDelta = new Vector2(800f, 84f);

            _queueLabelText = MakeText("QueueLabel", _queueBar, 顶栏字号 * 0.45f, Palette.暖金);
            SetRect(_queueLabelText.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                Vector2.zero, new Vector2(150f, 44f));
            _queueLabelText.alignment = TextAlignmentOptions.Left;
            _queueLabelCombiner = AttachCombiner(_queueLabelText);

            // 双方信息块（左右镜像；协议核心血条 B8 接入）
            _myBlock = BuildPlayerBlock("MyInfoBlock", left: true, Palette.我方主色);
            _enemyBlock = BuildPlayerBlock("EnemyInfoBlock", left: false, Palette.敌方主色);

            // 设置/退出（右上角，走 BattleExitConfirmDialog 确认；图标=项目现成 settings_button）
            var settings = MakeIconButton("SettingsButton", bar, "UI/Buttons/settings_button",
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-56f, -56f), new Vector2(84f, 84f));
            settings.onClick.AddListener(() => _onCloseBattle?.Invoke());
        }

        /// <summary>玩家信息块：势力徽标（地图势力，ElementFactionConfig 现成链）+ 队营色 accent + 体力/摩拉 chip（现成物品图标）</summary>
        private PlayerBlock BuildPlayerBlock(string name, bool left, Color accent)
        {
            var root = MakeRect(name, _canvas.transform);
            root.anchorMin = root.anchorMax = left ? new Vector2(0f, 1f) : new Vector2(1f, 1f);
            root.pivot = left ? new Vector2(0f, 1f) : new Vector2(1f, 1f);
            root.anchoredPosition = left ? new Vector2(信息块距左, -26f) : new Vector2(-信息块距右, -26f);
            root.sizeDelta = new Vector2(430f, 100f);

            float dir = left ? 1f : -1f; // 镜像：右侧块向左排布
            Vector2 EdgeAnchor(float along) => left
                ? new Vector2(along, 0.5f)
                : new Vector2(1f - along, 0.5f);

            // 徽标（地图所属势力；正式化后按玩家势力）
            var emblemGo = new GameObject("Emblem");
            var emblemRect = emblemGo.AddComponent<RectTransform>();
            emblemRect.SetParent(root, false);
            emblemRect.anchorMin = emblemRect.anchorMax = EdgeAnchor(0f);
            emblemRect.pivot = new Vector2(left ? 0f : 1f, 0.5f);
            emblemRect.anchoredPosition = Vector2.zero;
            emblemRect.sizeDelta = new Vector2(徽标尺寸, 徽标尺寸);
            var emblem = emblemGo.AddComponent<Image>();
            emblem.sprite = ElementFactionConfig.Instance != null
                ? ElementFactionConfig.Instance.GetFactionIcon((FactionType)(_session.Sim.Map.faction))
                : null;
            emblem.preserveAspect = true;

            // 队营色 accent 下划线
            var accentGo = new GameObject("Accent");
            var accentRect = accentGo.AddComponent<RectTransform>();
            accentRect.SetParent(root, false);
            accentRect.anchorMin = accentRect.anchorMax = EdgeAnchor(0f);
            accentRect.pivot = new Vector2(left ? 0f : 1f, 0.5f);
            accentRect.anchoredPosition = new Vector2(0f, -徽标尺寸 * 0.5f - 5f);
            accentRect.sizeDelta = new Vector2(徽标尺寸, 6f);
            var accentImage = accentGo.AddComponent<Image>();
            accentImage.color = accent;

            // 体力/摩拉 chip（图标=ItemConfig 同源现成图）
            var block = new PlayerBlock();
            block.stamina = BuildChip(root, "StaminaChip", "UI/Items/stamina", EdgeAnchor, dir, 108f);
            block.mora = BuildChip(root, "MoraChip", "UI/Items/mora", EdgeAnchor, dir, 224f);
            return block;
        }

        /// <summary>资源 chip：图标 + 数字（TextCombiner 静态条目）；along = 距块内侧缘的偏移</summary>
        private TextCombiner BuildChip(RectTransform root, string name, string iconPath,
            Func<float, Vector2> edgeAnchor, float dir, float along)
        {
            bool left = dir > 0f;
            var iconGo = new GameObject(name + "Icon");
            var iconRect = iconGo.AddComponent<RectTransform>();
            iconRect.SetParent(root, false);
            iconRect.anchorMin = iconRect.anchorMax = edgeAnchor(0f);
            iconRect.pivot = new Vector2(left ? 0f : 1f, 0.5f);
            iconRect.anchoredPosition = new Vector2(dir * along, 6f);
            iconRect.sizeDelta = new Vector2(36f, 36f);
            var icon = iconGo.AddComponent<Image>();
            icon.sprite = Resources.Load<Sprite>(iconPath);
            icon.preserveAspect = true;

            var numGo = new GameObject(name + "Num");
            var numRect = numGo.AddComponent<RectTransform>();
            numRect.SetParent(root, false);
            numRect.anchorMin = numRect.anchorMax = edgeAnchor(0f);
            numRect.pivot = new Vector2(left ? 0f : 1f, 0.5f);
            numRect.anchoredPosition = new Vector2(dir * (along + 44f), 2f);
            numRect.sizeDelta = new Vector2(72f, 44f);
            var numText = numGo.AddComponent<TextMeshProUGUI>();
            numText.fontSize = 顶栏字号 * 0.55f;
            numText.color = Palette.文字米白;
            numText.alignment = left ? TextAlignmentOptions.Left : TextAlignmentOptions.Right;
            numText.raycastTarget = false;
            return AttachCombiner(numText);
        }

        private void UpdatePlayerBlock(PlayerBlock block, BattleSnapshot snapshot, string playerId)
        {
            if (block == null) return;
            var res = string.IsNullOrEmpty(playerId)
                ? null
                : snapshot.resources.FirstOrDefault(r => r.playerId == playerId);
            block.stamina.SetSingleEntry(res != null ? res.stamina.ToString() : "—");
            block.mora.SetSingleEntry(res != null ? res.mora.ToString() : "—");
        }

        private void UpdateQueueLabel()
        {
            if (_queueLabelCombiner == null) return;
            string key = _session.Flow.Phase == BattlePhase.Selecting ? "Battle_QueuePreview" : "Battle_Executing";
            _queueLabelCombiner.SetSingleEntry(new LocalizedString("UIText", key));
        }

        /// <summary>重建队列槽：存活单位按攻速降序（统一执行阶段跨双方），头像+攻速+队营色框</summary>
        private void RebuildQueue(BattleSnapshot snapshot)
        {
            ClearQueueSlots();
            if (_queueBar == null) return;

            var order = snapshot.units
                .Where(u => u.isCorpse == 0)
                .OrderByDescending(u => u.attackSpeed)
                .Take(Mathf.Max(1, 队列槽位数));

            int index = 0;
            foreach (var u in order)
            {
                var slotGo = new GameObject($"QueueSlot_{u.unitName}");
                var rect = slotGo.AddComponent<RectTransform>();
                rect.SetParent(_queueBar, false);
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                float slotSize = 队列槽边长;
                rect.anchoredPosition = new Vector2(160f + index * (slotSize + 队列槽间距) + slotSize * 0.5f, 0f);
                rect.sizeDelta = new Vector2(slotSize, slotSize);

                // 队营色底盘框（circle 现成底盘图）
                var frame = slotGo.AddComponent<Image>();
                frame.sprite = Resources.Load<Sprite>("UI/Skills/circle");
                Color baseColor = u.playerId == _myPlayerId ? Palette.我方主色 : Palette.敌方主色;
                frame.color = new Color(baseColor.r, baseColor.g, baseColor.b, 0.55f);

                // 头像（UnitConfig 数据链）
                var avatarGo = new GameObject("Avatar");
                var avatarRect = avatarGo.AddComponent<RectTransform>();
                avatarRect.SetParent(rect, false);
                avatarRect.anchorMin = avatarRect.anchorMax = new Vector2(0.5f, 1f);
                avatarRect.pivot = new Vector2(0.5f, 1f);
                avatarRect.anchoredPosition = Vector2.zero;
                float avatarSize = slotSize * 0.62f;
                avatarRect.sizeDelta = new Vector2(avatarSize, avatarSize);
                var avatar = avatarGo.AddComponent<Image>();
                avatar.sprite = GetAvatarSprite(u.unitName);
                avatar.preserveAspect = true;

                // 攻速数字（槽底）
                var speedText = MakeText("Speed", rect, 顶栏字号 * 0.62f, Palette.暖金);
                SetRect(speedText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                    new Vector2(0f, 2f), new Vector2(slotSize, 顶栏字号 * 0.62f + 4f));
                var speedCombiner = AttachCombiner(speedText);
                speedCombiner.SetSingleEntry(u.attackSpeed.ToString());

                _queueSlots.Add(new QueueSlot
                {
                    frame = frame,
                    speed = speedCombiner,
                    attackSpeed = u.attackSpeed,
                    baseColor = baseColor,
                });
                index++;
            }
        }

        private void ClearQueueSlots()
        {
            foreach (var slot in _queueSlots)
                if (slot.frame != null) Destroy(slot.frame.gameObject);
            _queueSlots.Clear();
        }

        private Sprite GetAvatarSprite(string unitName)
        {
            if (_unitConfig == null)
                _unitConfig = Resources.Load<UnitConfig>("Configs/UnitConfig");
            return _unitConfig != null && Enum.TryParse<UnitName>(unitName, out var name)
                && _unitConfig.TryGetUnitData(name, out var data)
                ? data.avatar : null;
        }
    }
}
