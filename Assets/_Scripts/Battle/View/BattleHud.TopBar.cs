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
using GIC.UI;

namespace GIC.Battle
{
    /// <summary>
    /// BattleHud 顶栏分件（2026-09-22 prefab 化：构建退役为寻址接线；结构改在 prefab 编辑器里做）：
    /// 回合中枢/选择倒计时/战斗时钟/攻速队列条/我方信息块（左上角摩拉·体力物品牌计数，B6d）/
    /// 设置钮 的引用解析 + 运行时刷新。寻址依赖 Build 分件先建好的布局槽表（槽内控件名=旧构建命名）。
    /// 敌方信息块已移除（2026-09-25 拍板「不需要显示敌人的资源等信息」）。
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

        // 我方资源（B6d 经济闭环）：左上角 myinfo 块的摩拉/体力=物品牌计数（用户拍板
        // 「体力/摩拉实际也是手牌（物品牌）+屏幕左上角显示数量」）——公用组件 ItemCounterChip
        // （抽自祈愿界面货币显示改造公用化）。_myMora/_myStamina=当前值缓存（快照权威重置+命令增量维护）。
        // 敌方信息块已整体移除（2026-09-25 用户拍板「不需要显示敌人的资源等信息」——布局键与槽一并退役）。
        private ItemCounterChip _myMoraChip;
        private ItemCounterChip _myStaminaChip;
        private int _myMora;
        private int _myStamina;

        private class QueueSlot
        {
            public Image frame;
            public TextCombiner speed;
            public int attackSpeed;
            public Color baseColor;
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

            // 我方信息块（左上角）：徽标+队营色 chrome 保留，体力/摩拉换物品牌计数 chip（B6d——
            // 结构契约=槽内 MoraChip/StaminaChip 两枚 ItemCounterChip；**图标初始化挪到 Bind 注入后**——
            // ResolveHudReferences 先于 Wargame.Context.Inject 跑，此处 _itemConfig 尚为 null，
            // InitItem 会 SetIcon(null) 把图标节点隐藏（首版实测"只有数字"的根因））
            if (_layoutByKey.TryGetValue("myinfo", out var myinfo))
            {
                ResolveBlockChrome(myinfo.content, Palette.我方主色);
                _myMoraChip = FindChip(myinfo.content, "MoraChip");
                _myStaminaChip = FindChip(myinfo.content, "StaminaChip");
            }
            else GICLog.Warn("[BattleHud] 布局槽缺失：myinfo");

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

        /// <summary>信息块 chrome 解析（我方块）：势力徽标（地图势力，ElementFactionConfig 现成链）+ 队营色 accent 下划线</summary>
        private void ResolveBlockChrome(RectTransform root, Color accent)
        {
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
        }

        /// <summary>信息块内寻物品牌计数 chip（B6d 结构契约：槽内 MoraChip/StaminaChip）</summary>
        private ItemCounterChip FindChip(RectTransform root, string chipName)
        {
            var chip = root.Find(chipName)?.GetComponent<ItemCounterChip>();
            if (chip == null)
                GICLog.Warn($"[BattleHud] myinfo 槽寻不到 {chipName}（<ItemCounterChip>），左上角资源计数将不显示");
            return chip;
        }

        /// <summary>左上角 chip 图标初始化（B6d：Bind 在 Wargame.Context.Inject 之后调用——
        /// _itemConfig 属 [Autowired] 注入，寻址阶段（ResolveHudReferences）拿不到；图标走 ItemConfig 单源）</summary>
        private void InitMyResourceChipIcons()
        {
            if (_itemConfig == null)
                GICLog.Warn("[BattleHud] ItemConfig 未注入（DI 容器缺 ItemConfig Bean？）——左上角 chip 图标将不显示");
            if (_myMoraChip != null) _myMoraChip.InitItem(_itemConfig, ItemName.Mora);
            if (_myStaminaChip != null) _myStaminaChip.InitItem(_itemConfig, ItemName.Stamina);
        }

        /// <summary>我方摩拉/体力刷新（快照权威：缓存重置；chip+手牌货币卡由 RefreshMyResourceChips 统一刷新）</summary>
        private void UpdateMyResources(BattleSnapshot snapshot)
        {
            var res = snapshot.resources.FirstOrDefault(r => r.playerId == _myPlayerId);
            _myMora = res != null ? res.mora : 0;
            _myStamina = res != null ? res.stamina : 0;
            RefreshMyResourceChips();
        }

        /// <summary>资源显示统一刷新口（左上角 chip+手牌两张货币物品牌卡数量——快照与命令增量共用）</summary>
        private void RefreshMyResourceChips()
        {
            if (_myMoraChip != null) _myMoraChip.SetCount(_myMora);
            if (_myStaminaChip != null) _myStaminaChip.SetCount(_myStamina);
            RefreshHandCurrencyCards();
        }

        /// <summary>我方资源命令增量（B6d；BattlePlayer.OnResourceDelta→主件 OnResourceDeltaHandler 分流至此）：
        /// 部署扣费/回合结束发放/行动消耗的命令流即时刷新，快照权威兜底校正</summary>
        public void ApplyMyResourceDelta(int statKind, int delta)
        {
            if (statKind == BattleCommand.StatKindMora) _myMora = Mathf.Max(0, _myMora + delta);
            else if (statKind == BattleCommand.StatKindStamina) _myStamina = Mathf.Max(0, _myStamina + delta);
            else return;
            RefreshMyResourceChips();
        }

        /// <summary>我方体力是否够放此技能（B6d 置灰判定；消耗口径单源=BattleSimState.GetStaminaCost，
        /// 值=缓存 _myStamina（快照权威+命令增量），仅战技/爆发消耗）</summary>
        private bool HasStaminaForSkill(SkillConfig.SkillData skillData)
        {
            int cost = BattleSimState.GetStaminaCost(skillData);
            return cost <= 0 || _myStamina >= cost;
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
            // _unitConfig=[Autowired] 注入（主分件声明；Y10），不再懒加载
            return _unitConfig != null && Enum.TryParse<UnitName>(unitName, out var name)
                && _unitConfig.TryGetUnitData(name, out var data)
                ? data.avatar : null;
        }
    }
}
