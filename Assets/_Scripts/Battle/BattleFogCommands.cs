using System.Collections.Generic;
using UnityEngine;
using GIC.Data;
using GIC.Framework;

namespace GIC.Battle
{


    /// <summary>
    /// 战斗域指令（迷雾批 2026-10-17）：迷雾测试局——任务系统尚无实装消费方，经本指令开
    /// P1 真人 vs P2 AI 的指定迷雾模式对局供实机体验（对战迷雾走房间天然生效无须指令）。
    /// 文案=开发者向（2026-09-13 拍板基调：短、干、事实化）。
    /// </summary>
    public static class BattleFogCommands
    {
        [GameCommand("fog", "迷雾", "fog <mission|skirmish>", "开一局 P1 vs AI 迷雾测试局", nameof(FogSuggest))]
        private static CommandResult Fog(CommandArgs args)
        {
            if (args.Count == 0)
                return CommandResult.Error("用法: fog <mission|skirmish>");

            var mode = args.Get(0);
            BattleFogMode fogMode;
            if (mode == "mission" || mode == "任务" || mode == "m") fogMode = BattleFogMode.Mission;
            else if (mode == "skirmish" || mode == "对战" || mode == "s") fogMode = BattleFogMode.Skirmish;
            else return CommandResult.Error($"未知模式: {mode}（mission=任务迷雾 / skirmish=对战迷雾）");

            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == SceneType.BattleScreen)
                return CommandResult.Error("已在战斗中，先退出再开");

            var config = new BattleLaunchConfig
            {
                MapConfigName = BattleLaunchConfig.DefaultMapName,
                // 种子用表现层随机（开局壳层，不入模拟流；模拟核心禁该随机源）
                Seed = UnityEngine.Random.Range(0, int.MaxValue),
                FogMode = fogMode,
                Players = new List<BattlePlayerSetup>
                {
                    new BattlePlayerSetup
                    {
                        PlayerId = BattleDebugPlayerIds.P1,
                        DisplayName = "探索者",
                        IsAI = false,
                        Team = TeamType.A,
                        Color = PlayerColor.Red,
                    },
                    new BattlePlayerSetup
                    {
                        PlayerId = BattleDebugPlayerIds.P2,
                        DisplayName = "AI",
                        IsAI = true, // AI 卡组=BuildAIDefaultDeck（安柏/凯亚/芭芭拉+体力摩拉）
                        Team = TeamType.B,
                        Color = PlayerColor.Blue,
                    },
                },
            };
            BattleLaunchConfig.Launch(config);
            return CommandResult.Ok($"迷雾测试局启动：{(fogMode == BattleFogMode.Mission ? "任务迷雾（混沌盖）" : "对战迷雾（半透明）")}，P1 vs AI");
        }

        /// <summary>模式参数补全</summary>
        private static List<CommandSuggestion> FogSuggest(int argIndex, string partial)
        {
            var result = new List<CommandSuggestion>();
            if (argIndex != 0) return result;
            if ("mission".StartsWith(partial, System.StringComparison.OrdinalIgnoreCase))
                result.Add(new CommandSuggestion { main = "mission", hint = "任务迷雾" });
            if ("skirmish".StartsWith(partial, System.StringComparison.OrdinalIgnoreCase))
                result.Add(new CommandSuggestion { main = "skirmish", hint = "对战迷雾" });
            return result;
        }
    }
}
