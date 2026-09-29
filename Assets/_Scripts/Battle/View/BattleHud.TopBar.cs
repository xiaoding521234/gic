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
    /// 回合中枢/选择倒计时/战斗时钟/我方信息块（左上角摩拉·体力物品牌计数，B6d）/设置钮
    /// 的引用解析 + 运行时刷新。寻址依赖 Build 分件先建好的布局槽表（槽内控件名=旧构建命名）。
    /// 敌方信息块已移除（2026-09-25 拍板「不需要显示敌人的资源等信息」）；
    /// **攻速队列条已退役（2026-09-29 拍板整体删除——执行阶段改由 BattleExecutionPreview 多行
    /// 行动预览接管，槽/prefab 代码/布局键全清；QueueSlot.prefab 保留作预览单位块嵌套件）**。
    /// </summary>
    public partial class BattleHud
    {
        // 顶栏：回合中枢 + 倒计时 + 时钟（本地化 = TextCombiner；数字 = 静态条目）
        private TMP_Text _turnPhaseText;
        private TextCombiner _turnPhaseCombiner;
        private TMP_Text _countdownText;
        private TextCombiner _countdownCombiner;
        /// <summary>倒计时基准字号（2026-09-26 拍板 3 倍烘焙=61.2；告急脉动以它为基线，解析时捕获）</summary>
        private float _countdownBaseFontSize = 61.2f;
        /// <summary>倒计时 SDF 描边材质实例（2026-09-26 报障返修：UGUI Outline 固定像素描边在缩放视口下
        /// ~1.5 屏幕像素不可见+大字对角断缝——改 SDF 着色器原生 _OutlineWidth（相对字形、随缩放恒定）。
        /// 独立实例勿污染共享字体材质；主分件 OnDestroy 释放（docs/14 §63 生命周期纪律）</summary>
        private Material _countdownOutlineMat;
        private TMP_Text _clockText;
        private TextCombiner _clockCombiner;

        // 我方资源（B6d 经济闭环）：左上角 myinfo 块的摩拉/体力=物品牌计数（用户拍板
        // 「体力/摩拉实际也是手牌（物品牌）+屏幕左上角显示数量」）——公用组件 ItemCounterChip
        // （抽自祈愿界面货币显示改造公用化）。_myMora/_myStamina=当前值缓存（快照权威重置+命令增量维护）。
        // 敌方信息块已整体移除（2026-09-25 用户拍板「不需要显示敌人的资源等信息」——布局键与槽一并退役）。
        private ItemCounterChip _myMoraChip;
        private ItemCounterChip _myStaminaChip;
        private int _myMora;
        private int _myStamina;

        private void ResolveTopBar()
        {
            // 回合中枢（顶部中央）：回合数 + 阶段徽章
            _turnPhaseText = FindSlotText("turn", "TurnPhaseText");
            _turnPhaseCombiner = FindSlotCombiner("turn", "TurnPhaseText");
            RecolorText(_turnPhaseCombiner, Palette.文字米白);

            // 选择倒计时（选择阶段常显；数字条目。2026-09-26 拍板：3 倍字号+黑描边+<5 秒红色脉动——
            // 字号烘 prefab，描边=SDF 原生（运行时独立材质实例），告急脉动/变色在主分件 UpdateCountdownUrgency）
            _countdownText = FindSlotText("countdown", "CountdownText");
            _countdownCombiner = FindSlotCombiner("countdown", "CountdownText");
            RecolorText(_countdownCombiner, Palette.暖金);
            if (_countdownText != null)
            {
                _countdownBaseFontSize = _countdownText.fontSize;
                var sharedMat = _countdownText.fontSharedMaterial;
                if (sharedMat != null && sharedMat.HasProperty("_OutlineWidth"))
                {
                    _countdownOutlineMat = new Material(sharedMat);
                    _countdownOutlineMat.SetColor("_OutlineColor", Palette.伤害数字描边色); // 黑描边与伤害数字同源
                    _countdownOutlineMat.SetFloat("_OutlineWidth", 倒计时描边宽度);
                    _countdownText.fontMaterial = _countdownOutlineMat;
                }
                else GICLog.Warn("[BattleHud] 倒计时字体材质不支持 SDF 描边（_OutlineWidth），描边不生效");
            }

            // 战斗时钟（独立时钟 6:00 起 +20/回合）
            _clockText = FindSlotText("clock", "ClockText");
            _clockCombiner = FindSlotCombiner("clock", "ClockText");
            RecolorText(_clockCombiner, Palette.文字米白);

            // 我方信息块（左上角）：队营色 chrome 保留，体力/摩拉换物品牌计数 chip（B6d——
            // 结构契约=槽内 MoraChip/StaminaChip 两枚 ItemCounterChip；**图标初始化挪到 Bind 注入后**——
            // ResolveHudReferences 先于 Wargame.Context.Inject 跑，此处 _itemConfig 尚为 null，
            // InitItem 会 SetIcon(null) 把图标节点隐藏（首版实测"只有数字"的根因））。
            // 徽标已移除（2026-09-29 用户拍板「移除 HUD 左上角的徽标和血条，协议核心不需要额外显示」——
            // prefab myinfo 槽 Emblem 节点同批删除）
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

        /// <summary>信息块 chrome 解析（我方块）：队营色 accent 下划线（Palette 活色）。
        /// 徽标已移除（2026-09-29 拍板：myinfo 只余摩拉/体力 chip + accent 下划线，协议核心不做顶栏显示）</summary>
        private void ResolveBlockChrome(RectTransform root, Color accent)
        {
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
    }
}
