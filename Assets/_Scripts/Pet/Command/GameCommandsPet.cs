using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using GIC.Framework;
using GIC.Data;
using GIC.UI;
using GIC.Pet.Chat;

namespace GIC.Pet
{
    /// <summary>
    /// Pet 域指令集（依赖 UI/PetGameBridge/PetWishAutoRunner，故不进 Framework 核心）：open / wish。
    /// 2026-09-13 自 PetChatIntent 迁移（指令系统统一，设置页与派蒙共用）；扫描由 CommandSystem
    /// 反射发现（GIC.Pet 程序集内即可），无需中央接线。行为与迁移前完全一致：同路径导航铁律、
    /// 联机/战斗互斥守卫、原石预检、反应通道照旧。
    /// </summary>
    public static class GameCommandsPet
    {
        // ---- 界面导航表（AI/指令可打开的弹层界面；联机/战斗不纳入——房间状态机/对局进行中） ----

        private class ScreenEntry
        {
            public readonly string key;      // 规范名（指令参数）
            public readonly string cnName;   // 中文名（参数别名 + 回复文案）
            public readonly SceneType scene; // null=回大厅（关闭当前弹层界面，不新开）

            public ScreenEntry(string key, string cnName, SceneType scene)
            {
                this.key = key;
                this.cnName = cnName;
                this.scene = scene;
            }
        }

        private static readonly ScreenEntry[] _screens =
        {
            new ScreenEntry("hall", "大厅", null),
            new ScreenEntry("map", "地图", SceneType.MapScreen),
            new ScreenEntry("backpack", "背包", SceneType.BackpackScreen),
            new ScreenEntry("wish", "祈愿", SceneType.WishScreen),
            new ScreenEntry("settings", "设置", SceneType.SettingsScreen),
        };

        // ==================== open（界面导航） ====================

        // 枚举进 usage（help/LLM 工具描述保留全量）；description 保持单行短文案（补全行 hint 宽度预算 ~326px）
        [GameCommand("open", "打开", "open <hall/map/backpack/wish/settings>", "打开界面（中文同认）", nameof(OpenSuggest))]
        private static CommandResult Open(CommandArgs args)
        {
            if (args.Count == 0)
                return CommandResult.Error("用法: open <界面名>");

            // 规范名/中文名双匹配
            var token = args.Get(0);
            var entry = Array.Find(_screens, s => s.key.Equals(token, StringComparison.OrdinalIgnoreCase) || s.cnName == token);
            if (entry == null)
                return CommandResult.Error($"未知界面: {token}");

            var gs = GameScene.Instance;
            if (gs == null) return CommandResult.Error("场景管理器未就绪");

            string busy = BlockedSceneCheck();
            if (busy != null) return CommandResult.Error(busy);

            // 在弹层界面=UIManager 栈上有弹层（root=context 不入栈，docs/23 D12）
            var ui = UIManager.Instance;
            bool inPopup = ui != null && ui.StackCount > 0;

            // hall=回大厅：已在大厅无事可做；在弹层界面=标准关闭它（不新开）
            if (entry.scene == null)
            {
                if (!inPopup) return CommandResult.Ok("已在大厅");
                PetGameBridge.Run(PetGameBridge.NavigateToScreen(null));
                return CommandResult.Ok("正在回大厅");
            }

            // 目标已打开：场景制看 CurrentScene，面板制看 UIManager 栈（P2 起两类并存）
            bool alreadyOpen = (gs.CurrentScene != null && gs.CurrentScene.SceneName == entry.scene.SceneName)
                            || (UIManager.Instance != null && UIManager.Instance.IsOpen(Screens.FromSceneName(entry.scene.SceneName)));
            if (alreadyOpen)
                return CommandResult.Ok($"{entry.cnName}界面已打开");

            PetGameBridge.Run(PetGameBridge.NavigateToScreen(entry.scene));
            return CommandResult.Ok($"正在打开{entry.cnName}");
        }

        /// <summary>open 参数补全：界面名（英文规范名/中文别名双向）</summary>
        private static List<CommandSuggestion> OpenSuggest(int argIndex, string partial)
        {
            var list = new List<CommandSuggestion>();
            if (argIndex != 0) return list;
            foreach (var s in _screens)
            {
                int rankKey = Rank(s.key, partial);
                int rankCn = Rank(s.cnName, partial);
                if (rankKey == int.MaxValue && rankCn == int.MaxValue) continue;
                bool byCn = rankCn < rankKey || (rankCn == rankKey && !string.IsNullOrEmpty(partial) && s.cnName.StartsWith(partial, StringComparison.Ordinal));
                list.Add(new CommandSuggestion
                {
                    main = byCn ? s.cnName : s.key,
                    hint = byCn ? s.key : s.cnName,
                    trailingSpace = false,
                });
            }
            return list;
        }

        /// <summary>联机/战斗界面互斥检查（两者有进行中的状态机，强切会打断流程）</summary>
        private static string BlockedSceneCheck()
        {
            // P4 修正：联机已面板化（场景已删，按场景判据恒 false=拦截失效）——改查 UIManager 栈
            var ui = UIManager.Instance;
            if (ui != null && ui.IsOpen(Screens.Coop))
                return "联机界面打开中";
            if (SceneManager.GetSceneByName("BattleScreen").isLoaded)
                return "战斗进行中";
            return null;
        }

        private static int Rank(string candidate, string partial)
        {
            if (string.IsNullOrEmpty(partial)) return 2;
            if (candidate.Equals(partial, StringComparison.OrdinalIgnoreCase)) return 0;
            if (candidate.StartsWith(partial, StringComparison.OrdinalIgnoreCase)) return 1;
            if (candidate.Contains(partial, StringComparison.OrdinalIgnoreCase)) return 2;
            return int.MaxValue;
        }

        // ==================== wish（派蒙自动抽卡） ====================

        [GameCommand("wish", "抽卡", "wish <次数>", "自动抽卡（160 原石/次）", null)]
        private static CommandResult Wish(CommandArgs args)
        {
            int count = 1;
            if (args.Count > 0 && (!int.TryParse(args.Get(0), out count) || count < 1 || count > 10))
                return CommandResult.Error("次数须为 1-10");

            var gs = GameScene.Instance;
            if (gs == null) return CommandResult.Error("场景管理器未就绪");

            string busy = BlockedSceneCheck();
            if (busy != null) return CommandResult.Error(busy);

            if (PetWishAutoRunner.IsBusy)
                return CommandResult.Error("抽卡进行中");

            // 祈愿界面已开且抽卡进行中（含最终展示期）→ 拒绝
            // （P4 修正：祈愿已面板化，场景判据失效——改查 UIManager 栈）
            if (UIManager.Instance != null && UIManager.Instance.IsOpen(Screens.Wish))
            {
                var wish = UnityEngine.Object.FindFirstObjectByType<GIC.UI.WishScreen>(FindObjectsInactive.Exclude);
                if (wish != null && wish.IsWishInProgress)
                    return CommandResult.Error("抽卡进行中");
            }

            // 原石同步预检（不足立即回执，LLM/玩家转述；游戏内抽卡场景的后续变化由运行时守卫兜底）
            var save = Wargame.Instance?.Context?.Get<SaveManager>()?.CurrentSave;
            if (save == null) return CommandResult.Error("存档未就绪");
            int cost = GIC.UI.WishManager.SingleWishCost * count;
            int have = save.GetItemCount(ItemName.Primogem);
            if (have < cost)
                return CommandResult.Error($"原石不足: 需 {cost}，持有 {have}");

            if (!PetWishAutoRunner.Begin(count))
                return CommandResult.Error("抽卡进行中");

            return CommandResult.Ok($"开始自动抽卡 ×{count}");
        }
    }
}
