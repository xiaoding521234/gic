using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using GIC.Data;
using GIC.Tool;

namespace GIC.Framework
{
    /// <summary>
    /// 核心指令集（GIC.Framework 层，只依赖 Framework/Data）：help / give / card / time / quit。
    /// 界面导航 open、自动抽卡 wish、快捷消息 qm、启动游戏 start_game 是 Pet 域指令，
    /// 在 GameCommands.Pet.cs（依赖方向：Pet→Framework/Data/UI）。
    /// 存档变更一律走 SaveManager.ModifyNow（货币/进度=丢了会疼的数据，立即落盘）。
    /// 文案基调=开发者向（2026-09-13 用户拍板）：短、干、事实化——派蒙的口吻由 LLM 层转述时自加，回执只给原始事实。
    /// </summary>
    public static class GameCommandsCore
    {
        // ==================== help ====================

        [GameCommand("help", "帮助", "help", "指令列表", null)]
        private static CommandResult Help(CommandArgs args)
        {
            return CommandResult.Ok(CommandSystem.HelpText());
        }

        // ==================== give（物品/货币） ====================

        [GameCommand("give", "给", "give <物品名> [数量]", "给物品（默认 1）", nameof(GiveSuggest))]
        private static CommandResult Give(CommandArgs args)
        {
            var saveManager = Wargame.Instance?.Context?.Get<SaveManager>();
            if (saveManager == null || saveManager.CurrentSave == null)
                return CommandResult.Error("存档未就绪");

            if (args.Count == 0)
                return CommandResult.Error("用法: give <物品名> [数量]");

            // 参数自适应：数字串=数量、其余=物品名，两参顺序随意；单参时视为物品名、数量默认 1
            string itemToken = null, countToken = null;
            foreach (var t in args.tokens)
            {
                if (int.TryParse(t, out _)) countToken ??= t;
                else itemToken ??= t;
            }
            if (itemToken == null && countToken != null) { itemToken = countToken; countToken = null; }   // 单参数恰为数字=按物品ID
            if (itemToken == null) return CommandResult.Error("用法: give <物品名> [数量]");

            var item = EnumNameResolver.ResolveItem(itemToken);
            if (item == ItemName.None)
                return CommandResult.Error($"未知物品: {itemToken}");

            int count = 1;
            if (countToken != null && !int.TryParse(countToken, out count))
                return CommandResult.Error($"无效数量: {countToken}");
            if (count <= 0)
                return CommandResult.Error("数量须为正整数");
            if (count > 1000000) count = 1000000;   // 单次上限，防手滑天量

            int before = saveManager.CurrentSave.GetItemCount(item);
            saveManager.ModifyNow(s => s.AddItemCount(item, count));
            int after = saveManager.CurrentSave.GetItemCount(item);

            string cn = EnumNameResolver.ItemDisplayName(item);
            return CommandResult.Ok($"{cn} +{count}（持有 {after}）");
        }

        /// <summary>give 参数补全：第 0 参=物品名前缀匹配（中文名/英文名双向提示）</summary>
        private static List<CommandSuggestion> GiveSuggest(int argIndex, string partial)
        {
            if (argIndex != 0) return new List<CommandSuggestion>();
            return EnumNameResolver.SuggestItemNames(partial, 10);
        }

        // ==================== card（角色卡直给，纯作弊不奖星辉——不动祈愿经济记账） ====================

        [GameCommand("card", "角色", "card <角色名>", "获得角色卡（不计星辉）", nameof(CardSuggest))]
        private static CommandResult Card(CommandArgs args)
        {
            var saveManager = Wargame.Instance?.Context?.Get<SaveManager>();
            if (saveManager == null || saveManager.CurrentSave == null)
                return CommandResult.Error("存档未就绪");

            if (args.Count == 0)
                return CommandResult.Error("用法: card <角色名>");

            // 多字角色名暂无空格词，只取首参
            var unit = EnumNameResolver.ResolveUnit(args.Get(0));
            if (unit == null)
                return CommandResult.Error($"未知角色: {args.Get(0)}");
            UnitName unitValue = unit.Value;

            string cn = EnumNameResolver.UnitDisplayName(unitValue);
            // 闭包捕获结果：ModifyNow 的闭包内不能 return，用包装变量带出判定
            bool revived = false, owned = false;
            saveManager.ModifyNow(s =>
            {
                foreach (var card in s.progress.ownedUnits)
                {
                    if (card.id.AsUnitName() == unitValue)
                    {
                        if (card.count > 0) { owned = true; return; }
                        card.count = 1;      // count=0：重新获得（祈愿同款复活语义）
                        revived = true;
                        return;
                    }
                }
                var newCard = new SaveCardData();
                newCard.SaveUnit(unitValue, 1);
                s.AddOwnedUnit(newCard);
            });

            if (owned) return CommandResult.Ok($"已拥有: {cn}");
            return CommandResult.Ok(revived ? $"重新获得: {cn}" : $"已获得: {cn}");
        }

        /// <summary>card 参数补全：第 0 参=角色名前缀匹配</summary>
        private static List<CommandSuggestion> CardSuggest(int argIndex, string partial)
        {
            if (argIndex != 0) return new List<CommandSuggestion>();
            return EnumNameResolver.SuggestUnitNames(partial, 10);
        }

        // ==================== time（游戏内时间） ====================

        [GameCommand("time", "时间", "time [set HH:mm | reset]（无参=查询）", "查询/设置游戏时间", nameof(TimeSuggest))]
        private static CommandResult Time(CommandArgs args)
        {
            // 不带参数=查询（承接派蒙旧 get_game_time 工具）
            if (args.Count == 0)
            {
                bool overridden = TimeUtility.IsGameTimeOverridden;
                string source = overridden ? "已设置" : "系统时间";
                return CommandResult.Ok($"游戏内时间 {TimeUtility.Now:HH:mm}（{PeriodName(TimeUtility.GetCurrentTimePeriod())}，{source}）");
            }

            string op = args.Get(0).ToLowerInvariant();
            if (op == "reset")
            {
                TimeUtility.ResetToSystemTime();
                return CommandResult.Ok($"已恢复系统时间（{TimeUtility.Now:HH:mm}，{PeriodName(TimeUtility.GetCurrentTimePeriod())}）");
            }
            if (op == "set")
            {
                if (args.Count < 2) return CommandResult.Error("用法: time set HH:mm");
                return SetTime(args.Get(1));
            }
            if (op.Contains(':')) return SetTime(op);                    // time 08:30 简写
            if (args.Count >= 2 && int.TryParse(args.Get(0), out _) && int.TryParse(args.Get(1), out _))
                return SetTime($"{args.Get(0)}:{args.Get(1)}");          // time 8 30 简写
            return CommandResult.Error("用法: time set HH:mm / time reset");
        }

        private static CommandResult SetTime(string text)
        {
            var parts = text.Split(':');
            if (parts.Length != 2 || !int.TryParse(parts[0], out int hour) || !int.TryParse(parts[1], out int minute)
                || hour < 0 || hour > 23 || minute < 0 || minute > 59)
                return CommandResult.Error($"无效时间: {text}（HH:mm）");

            TimeUtility.SetGameTime(hour, minute);
            return CommandResult.Ok($"时间已设为 {hour:00}:{minute:00}（{PeriodName(TimeUtility.GetCurrentTimePeriod())}）");
        }

        private static string PeriodName(TimePeriod period) => period == TimePeriod.Daytime ? "白天" : "夜晚";

        /// <summary>time 参数补全：第 0 参=set/reset；第 1 参=常用时刻</summary>
        private static List<CommandSuggestion> TimeSuggest(int argIndex, string partial)
        {
            var list = new List<CommandSuggestion>();
            if (argIndex == 0)
            {
                foreach (var (op, hint) in new[] { ("set", "设置时刻"), ("reset", "恢复系统时间") })
                    if (op.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
                        list.Add(new CommandSuggestion { main = op, hint = hint, trailingSpace = true });
            }
            else if (argIndex == 1)
            {
                foreach (var t in new[] { "08:00", "12:00", "18:00", "20:00", "22:00" })
                    if (t.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
                        list.Add(new CommandSuggestion { main = t, hint = "HH:mm", trailingSpace = false });
            }
            return list;
        }

        // ==================== quit（退出游戏，2026-09-13 快捷消息"关闭游戏"预设） ====================

        [GameCommand("quit", "关闭游戏", "quit", "退出游戏（自动存档）", null)]
        private static CommandResult Quit(CommandArgs args)
        {
            QuitDelayer.Request();
            return CommandResult.Ok("正在退出");
        }
    }

    /// <summary>延迟退出宿主（quit 指令专用）：回执/IPC 响应先落盘、气泡先显示，0.3s 后再真正退出——
    /// 同帧立即 Application.Quit 会吞掉桌宠 IPC 的响应回写（PetIntentIpcHost 在 Execute 返回后才
    /// WriteResponse，进程当帧即停=桌宠侧必超时报"游戏没在运行"）。真正的清理（标脏存档兜底落盘+
    /// closePetOnExit 连带关派蒙）全在 GameScene.OnApplicationQuit，本类零退出逻辑。</summary>
    internal class QuitDelayer : MonoBehaviour
    {
        static QuitDelayer _pending;

        public static void Request()
        {
            if (_pending != null) return; // 已在退出流程：幂等
            var go = new GameObject("[QuitDelayer]");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _pending = go.AddComponent<QuitDelayer>();
            _pending.StartCoroutine(DoQuit());
        }

        static IEnumerator DoQuit()
        {
            yield return new WaitForSecondsRealtime(0.3f);
            Application.Quit();
        }
    }

    /// <summary>
    /// 物品/角色名解析与补全（give/card 指令共用）：枚举英文名（不区分大小写）/数值 ID/InspectorName 中文名三路匹配；
    /// 补全按中文名+英文名前缀双向提示。独立小类避免 GameCommandsCore 膨胀。
    /// </summary>
    public static class EnumNameResolver
    {
        // ---- 解析 ----

        public static ItemName ResolveItem(string token)
        {
            if (string.IsNullOrEmpty(token)) return ItemName.None;
            // 纯数字=按枚举数值解析（MC 风格 ID，如 give 1005）
            if (int.TryParse(token, out int numeric) && Enum.IsDefined(typeof(ItemName), numeric))
                return (ItemName)numeric;
            foreach (var field in typeof(ItemName).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                var value = (ItemName)field.GetRawConstantValue();
                if (value == ItemName.None) continue;
                if (field.Name.Equals(token, StringComparison.OrdinalIgnoreCase)) return value;
                var display = field.GetCustomAttribute<InspectorNameAttribute>()?.displayName;
                if (display == token) return value;
            }
            return ItemName.None;
        }

        /// <summary>解析角色名（无 None 哨兵成员——用可空返回表达未匹配）</summary>
        public static UnitName? ResolveUnit(string token)
        {
            if (string.IsNullOrEmpty(token)) return null;
            if (int.TryParse(token, out int numeric) && Enum.IsDefined(typeof(UnitName), numeric))
                return (UnitName)numeric;
            foreach (var field in typeof(UnitName).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                var value = (UnitName)field.GetRawConstantValue();
                if (field.Name.Equals(token, StringComparison.OrdinalIgnoreCase)) return value;
                var display = field.GetCustomAttribute<InspectorNameAttribute>()?.displayName;
                if (display == token) return value;
            }
            return null;
        }

        // ---- 展示名 ----

        public static string ItemDisplayName(ItemName item) =>
            typeof(ItemName).GetField(item.ToString())?.GetCustomAttribute<InspectorNameAttribute>()?.displayName ?? item.ToString();

        public static string UnitDisplayName(UnitName unit) =>
            typeof(UnitName).GetField(unit.ToString())?.GetCustomAttribute<InspectorNameAttribute>()?.displayName ?? unit.ToString();

        // ---- 补全（前缀优先、包含次之；中文命中补中文名、英文命中补英文名，hint 互为对照） ----

        public static List<CommandSuggestion> SuggestItemNames(string partial, int max)
            => SuggestNames<ItemName>(partial, max, ItemDisplayName);

        public static List<CommandSuggestion> SuggestUnitNames(string partial, int max)
            => SuggestNames<UnitName>(partial, max, UnitDisplayName);

        private static List<CommandSuggestion> SuggestNames<T>(string partial, int max, Func<T, string> displayName) where T : struct, Enum
        {
            var list = new List<(CommandSuggestion s, int rank, int order)>();
            int order = 0;
            foreach (var field in typeof(T).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                var value = (T)field.GetRawConstantValue();
                if (EqualityComparer<T>.Default.Equals(value, default)) { order++; continue; }   // None/未定义 0 值跳过

                var display = field.GetCustomAttribute<InspectorNameAttribute>()?.displayName;
                int rankEn = Rank(field.Name, partial);
                int rankCn = display == null ? int.MaxValue : Rank(display, partial);
                if (rankEn == int.MaxValue && rankCn == int.MaxValue) { order++; continue; }

                // 主词选择：空 partial 浏览态/中文前缀命中→补中文名（中文优先），英文前缀命中→补英文名
                bool byCn = rankCn < rankEn
                    || (rankCn == rankEn && display != null
                        && (string.IsNullOrEmpty(partial) || display.StartsWith(partial, StringComparison.Ordinal)));
                string main = byCn ? display : field.Name;
                string hint = byCn ? field.Name : display;   // hint=对照名
                list.Add((new CommandSuggestion { main = main, hint = hint, trailingSpace = true },
                    Math.Min(rankEn, rankCn), order));
                order++;
            }
            return list.OrderBy(x => x.rank).ThenBy(x => x.order).Take(max).Select(x => x.s).ToList();
        }

        private static int Rank(string candidate, string partial)
        {
            if (string.IsNullOrEmpty(partial)) return 2;
            if (candidate.Equals(partial, StringComparison.OrdinalIgnoreCase)) return 0;
            if (candidate.StartsWith(partial, StringComparison.OrdinalIgnoreCase)) return 1;
            if (candidate.Contains(partial, StringComparison.OrdinalIgnoreCase)) return 2;
            return int.MaxValue;
        }
    }
}
