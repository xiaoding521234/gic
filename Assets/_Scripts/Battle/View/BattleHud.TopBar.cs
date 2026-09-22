using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Localization;
using GIC.Framework;
using GIC.Data;
using GIC.Tool;

namespace GIC.Battle
{
    /// <summary>
    /// BattleHud 顶栏分件（2026-09-22 prefab 化：构建退役为寻址接线；结构改在 prefab 编辑器里做）：
    /// 回合中枢/选择倒计时/战斗时钟/攻速队列条/双方信息块/设置钮 的引用解析 + 运行时刷新
    /// （刷新逻辑纯搬运零变化）。寻址依赖 Build 分件先建好的布局槽表（槽内控件名=旧构建命名）。
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

        private void ResolveTopBar()
        {
            // 回合中枢（顶部中央）：回合数 + 阶段徽章
            _turnPhaseText = FindSlotText("turn", "TurnPhaseText");
            _turnPhaseCombiner = FindSlotCombiner("turn", "TurnPhaseText");
            RecolorText(_turnPhaseCombiner, Palette.文字米白);

            // 选择倒计时（选择阶段常显；数字条目）
            _countdownText = FindSlotText("countdown", "CountdownText");
            _countdownCombiner = FindSlotCombiner("countdown", "CountdownText");
            RecolorText(_countdownCombiner, Palette.暖金);

            // 战斗时钟（独立时钟 6:00 起 +20/回合）
            _clockText = FindSlotText("clock", "ClockText");
            _clockCombiner = FindSlotCombiner("clock", "ClockText");
            RecolorText(_clockCombiner, Palette.文字米白);

            // 攻速队列条：槽内容=QueueBar 容器，内含 QueueLabel
            if (_layoutByKey.TryGetValue("queue", out var queue))
            {
                _queueBar = queue.content;
                _queueLabelText = _queueBar.Find("QueueLabel")?.GetComponent<TMP_Text>();
                _queueLabelCombiner = _queueLabelText != null ? _queueLabelText.GetComponent<TextCombiner>() : null;
                RecolorText(_queueLabelCombiner, Palette.暖金);
            }
            else GICLog.Warn("[BattleHud] 布局槽缺失：queue");

            // 双方信息块（左右镜像；协议核心血条 B8 接入）
            _myBlock = ResolvePlayerBlock("myinfo", Palette.我方主色);
            _enemyBlock = ResolvePlayerBlock("enemyinfo", Palette.敌方主色);

            // 设置/退出（走 BattleExitConfirmDialog 确认；布局编辑期点击让位——入口钮接管编辑开关）
            if (_layoutByKey.TryGetValue("settings", out var settings))
            {
                var button = settings.content.GetComponent<Button>();
                if (button != null)
                    button.onClick.AddListener(() => { if (!_layoutEditing) _onCloseBattle?.Invoke(); });
            }
            else GICLog.Warn("[BattleHud] 布局槽缺失：settings");
        }

        // 纯文本件（turn/countdown/clock 槽内容=文本本体）与容器件（槽内容包子级文本）两种结构并存，
        // 寻址先自检内容本体名、再下探子级——曾按容器件一刀切致三文本件寻空静默失显（2026-09-22）
        private TMP_Text FindSlotText(string key, string contentName)
        {
            if (!_layoutByKey.TryGetValue(key, out var def) || def.content == null) return null;
            return (def.content.name == contentName
                ? def.content.GetComponent<TMP_Text>()
                : def.content.Find(contentName)?.GetComponent<TMP_Text>())
                ?? WarnIfNull<TMP_Text>(key, contentName);
        }

        private TextCombiner FindSlotCombiner(string key, string contentName)
        {
            if (!_layoutByKey.TryGetValue(key, out var def) || def.content == null) return null;
            return (def.content.name == contentName
                ? def.content.GetComponent<TextCombiner>()
                : def.content.Find(contentName)?.GetComponent<TextCombiner>())
                ?? WarnIfNull<TextCombiner>(key, contentName);
        }

        private static T WarnIfNull<T>(string key, string contentName) where T : Component
        {
            GICLog.Warn($"[BattleHud] 槽 {key} 寻不到控件 {contentName}（<{typeof(T).Name}>），对应显隐将失效");
            return null;
        }

        /// <summary>玩家信息块解析：势力徽标（地图势力，ElementFactionConfig 现成链）+ 队营色 accent + 体力/摩拉 chip 数字</summary>
        private PlayerBlock ResolvePlayerBlock(string key, Color accent)
        {
            var block = new PlayerBlock();
            if (!_layoutByKey.TryGetValue(key, out var def))
            {
                GICLog.Warn($"[BattleHud] 布局槽缺失：{key}");
                return block;
            }
            var root = def.content;

            // 徽标（地图所属势力；正式化后按玩家势力）
            var emblem = root.Find("Emblem")?.GetComponent<Image>();
            if (emblem != null)
            {
                emblem.sprite = _session != null && _session.Sim != null && _session.Sim.Map != null
                    && ElementFactionConfig.Instance != null
                    ? ElementFactionConfig.Instance.GetFactionIcon((FactionType)(_session.Sim.Map.faction))
                    : null;
            }

            // 队营色 accent 下划线（Palette 活色）
            var accentImage = root.Find("Accent")?.GetComponent<Image>();
            if (accentImage != null) accentImage.color = accent;

            block.stamina = root.Find("StaminaChipNum")?.GetComponent<TextCombiner>();
            block.mora = root.Find("MoraChipNum")?.GetComponent<TextCombiner>();
            RecolorText(block.stamina, Palette.文字米白);
            RecolorText(block.mora, Palette.文字米白);
            return block;
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
