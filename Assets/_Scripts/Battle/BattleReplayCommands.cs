using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using GIC.Data;
using GIC.Framework;

namespace GIC.Battle
{


    /// <summary>
    /// 战斗域指令（Replay harness 批 2026-10-20）：回放录制开关与文件查询。
    /// 录制文件=persistentDataPath/Replays/*.jsonl；回放/回归断言=编辑器菜单
    /// Tools/TG/回放回归（Editor/Tool/ReplayRunner.cs）。
    /// 文案=开发者向（2026-09-13 拍板基调：短、干、事实化）。
    /// </summary>
    public static class BattleReplayCommands
    {
        [GameCommand("replay", "回放", "replay <on|off|status|list>", "战斗回放录制开关（回归 harness）", nameof(ReplaySuggest))]
        private static CommandResult Replay(CommandArgs args)
        {
            if (args.Count == 0)
                return CommandResult.Error("用法: replay <on|off|status|list>");

            switch (args.Get(0))
            {
                case "on":
                    BattleRecorder.Enabled = true;
                    return CommandResult.Ok($"回放录制开（下一局起生效；文件={ReplayDir()}）");
                case "off":
                    BattleRecorder.Enabled = false;
                    return CommandResult.Ok("回放录制关");
                case "status":
                {
                    var path = BattleRecorder.CurrentPath;
                    return CommandResult.Ok($"录制={OnOff(BattleRecorder.Enabled)}，当前局={path ?? "无"}");
                }
                case "list":
                    return ListFiles();
                default:
                    return CommandResult.Error($"未知参数: {args.Get(0)}（on/off/status/list）");
            }
        }

        private static CommandResult ListFiles()
        {
            var dir = ReplayDir();
            if (!Directory.Exists(dir) || Directory.GetFiles(dir, "*.jsonl").Length == 0)
                return CommandResult.Ok($"无录制文件（{dir}）");

            var sb = new StringBuilder();
            var files = Directory.GetFiles(dir, "*.jsonl");
            Array.Sort(files); // 文件名含时间戳=按局序
            int take = Math.Min(10, files.Length);
            for (int i = 0; i < take; i++)
            {
                var fi = new FileInfo(files[i]);
                sb.Append($"\n{fi.Name}  {fi.Length / 1024f:F0}KB");
            }
            if (files.Length > take) sb.Append($"\n…共 {files.Length} 个");
            return CommandResult.Ok($"录制文件（最新在后）：{sb}");
        }

        private static string ReplayDir()
            => Path.Combine(Application.persistentDataPath, "Replays");

        private static string OnOff(bool b) => b ? "开" : "关";

        /// <summary>参数补全</summary>
        private static List<CommandSuggestion> ReplaySuggest(int argIndex, string partial)
        {
            var result = new List<CommandSuggestion>();
            if (argIndex != 0) return result;
            if ("on".StartsWith(partial, StringComparison.OrdinalIgnoreCase))
                result.Add(new CommandSuggestion { main = "on", hint = "开录制（下一局起）" });
            if ("off".StartsWith(partial, StringComparison.OrdinalIgnoreCase))
                result.Add(new CommandSuggestion { main = "off", hint = "关录制" });
            if ("status".StartsWith(partial, StringComparison.OrdinalIgnoreCase))
                result.Add(new CommandSuggestion { main = "status", hint = "录制状态" });
            if ("list".StartsWith(partial, StringComparison.OrdinalIgnoreCase))
                result.Add(new CommandSuggestion { main = "list", hint = "录制文件列表" });
            return result;
        }
    }
}
