using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using GIC.Data;

namespace GIC.Framework
{
    /// <summary>指令回执：设置页转 toast，派蒙转 Ok/Error JSON（LLM 读）——两个前端同一份结果</summary>
    public struct CommandResult
    {
        public bool ok;
        public string message;

        public static CommandResult Ok(string message) => new CommandResult { ok = true, message = message };
        public static CommandResult Error(string message) => new CommandResult { ok = false, message = message };
    }

    /// <summary>补全建议项（IDE 式）：main=补全插入的词，hint=灰色说明，trailingSpace=补全后追加空格</summary>
    public struct CommandSuggestion
    {
        public string main;
        public string hint;
        public bool trailingSpace;
    }

    /// <summary>指令参数（去掉命令词后的切分结果）</summary>
    public struct CommandArgs
    {
        /// <summary>按空白切分的参数（不含命令词）</summary>
        public string[] tokens;
        /// <summary>原始输入（含命令词，已去 / 前缀）</summary>
        public string line;

        public int Count => tokens?.Length ?? 0;
        public string Get(int index) => index < Count ? tokens[index] : null;
    }

    /// <summary>
    /// 指令特性 — 标记静态指令处理方法并自动注册（SkillFactory [SkillAttribute] 同款反射扫描先例）。
    /// 方法签名须为 static CommandResult Xxx(CommandArgs args)；同类内可另写
    /// static List&lt;CommandSuggestion&gt; XxxSuggest(int argIndex, string partial) 作参数补全提供器，
    /// 方法名写进 Suggester 字段（找不到则该指令无参数补全）。
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    public class GameCommandAttribute : Attribute
    {
        /// <summary>命令名（小写英文，作为规范名）</summary>
        public string Name;
        /// <summary>中文别名（可空）</summary>
        public string Alias;
        /// <summary>用法串（help 目录与错误提示用）</summary>
        public string Usage;
        /// <summary>一句话描述（help 目录与补全提示用）</summary>
        public string Description;
        /// <summary>参数补全提供器方法名（同类静态方法，可空）</summary>
        public string Suggester;

        public GameCommandAttribute(string name, string alias, string usage, string description, string suggester = null)
        {
            Name = name;
            Alias = alias;
            Usage = usage;
            Description = description;
            Suggester = suggester;
        }
    }

    /// <summary>
    /// 统一指令系统（2026-09-13，拍板见会话记录）：设置页输入弹窗与 AI 派蒙聊天共用同一套指令。
    /// 注册=[GameCommand] 属性反射扫描（全程序集，懒扫描首用触发——各模块新指令即扫即用，无中央接线）；
    /// 执行=Execute(命令行)：解析（/ 前缀可选、全角空格归一、空白切分、大小写不敏感、中文别名）→注册表分发。
    /// 补全=Suggest(当前输入)：命令词阶段匹配命令名/别名前缀，参数阶段交各指令的 Suggester。
    /// 设计边界：纯语法/注册表层在本类；指令实现按模块分文件（Framework 核心=GameCommands.Core，Pet=GameCommands.Pet）。
    /// </summary>
    public static class CommandSystem
    {
        private class CommandEntry
        {
            public string name;
            public string alias;
            public string usage;
            public string description;
            public Func<CommandArgs, CommandResult> handler;
            public Func<int, string, List<CommandSuggestion>> suggester;
        }

        private static Dictionary<string, CommandEntry> _commands;   // 键=规范名+别名（均小写化；中文别名原样）
        private static List<CommandEntry> _ordered;                // help 目录稳定序（扫描序）

        // ==================== 对外入口 ====================

        /// <summary>执行一行指令（两前端共用）：设置页弹窗回调 / 派蒙 run_command 工具都走这里</summary>
        public static CommandResult Execute(string input)
        {
            EnsureScanned();
            string[] tokens = Tokenize(input);
            if (tokens.Length == 0)
                return CommandResult.Error("请输入指令（help 查看全部）");

            var entry = Lookup(tokens[0]);
            if (entry == null)
                return CommandResult.Error($"未知指令: {tokens[0]}（help 查看全部）");

            try
            {
                var result = entry.handler(new CommandArgs
                {
                    tokens = tokens.Skip(1).ToArray(),
                    line = string.Join(" ", tokens),
                });
                GICLog.Info($"[CommandSystem] {(result.ok ? "✓" : "✗")} {string.Join(" ", tokens)} → {result.message}");
                return result;
            }
            catch (Exception e)
            {
                GICLog.Warn($"[CommandSystem] 指令异常 {string.Join(" ", tokens)}: {e.Message}");
                return CommandResult.Error($"指令执行出错：{e.Message}");
            }
        }

        /// <summary>指令目录（help 正文 / 派蒙 run_command 工具描述共用，注册表即文档）</summary>
        public static string HelpText()
        {
            EnsureScanned();
            var sb = new StringBuilder();
            foreach (var c in _ordered)
            {
                sb.Append(c.usage);
                if (!string.IsNullOrEmpty(c.alias)) sb.Append($"（{c.alias}）");
                sb.Append(" — ").Append(c.description).Append('\n');
            }
            return sb.ToString().TrimEnd('\n');
        }

        /// <summary>
        /// 补全提示源：输入的命令词未打完→匹配命令名/别名前缀；已进入参数→交给该指令的 Suggester。
        /// 尾随空格=当前词已完 → 直接进入下一参数阶段（IDE 同款："give " 即出物品列表而非再列指令）。
        /// 返回条目按推荐序（前缀匹配优先于包含匹配），空列表=无提示。
        /// </summary>
        public static List<CommandSuggestion> Suggest(string input)
        {
            EnsureScanned();
            string[] tokens = Tokenize(input);

            // 尾随空格：首词已是可识别命令 → 跳到参数阶段（partial 空），不再按命令词前缀过滤
            bool trailingSpace = !string.IsNullOrEmpty(input)
                && (input[input.Length - 1] == ' ' || input[input.Length - 1] == '\t' || input[input.Length - 1] == (char)0x3000);
            if (trailingSpace && tokens.Length >= 1)
            {
                var done = Lookup(tokens[0]);
                if (done?.suggester != null)
                    return done.suggester(tokens.Length - 1, "");
                if (done != null) return new List<CommandSuggestion>();   // 命令无参数补全
                // 首词不是命令 → 落回命令词阶段（按 partial 过滤，如 "gi " 仍列 give）
            }

            // 命令词阶段：只打了一个词（含空输入，IDE 空行展示全部）
            if (tokens.Length <= 1)
            {
                string partial = tokens.Length == 1 ? tokens[0].ToLowerInvariant() : "";
                var list = new List<(CommandSuggestion s, int rank)>();
                foreach (var c in _ordered)
                {
                    int rankCmd = PrefixRank(c.name, partial);
                    int rankAlias = string.IsNullOrEmpty(c.alias) ? int.MaxValue : PrefixRank(c.alias, partial);
                    int rank = Math.Min(rankCmd, rankAlias);
                    if (rank == int.MaxValue) continue;

                    // 前缀命中别名则补别名（中文输入流不被打断），否则补规范名。
                    // hint 只放描述（行内单行预算 ~326px@size26——"别名「X」·"前缀与长描述会换行叠行，
                    // 2026-09-13 目检实证；别名的发现走 help 目录与中文前缀输入）
                    bool byAlias = rankAlias < rankCmd || (rankAlias == rankCmd && !string.IsNullOrEmpty(partial) && c.alias != null && c.alias.StartsWith(partial, StringComparison.Ordinal));
                    var s = new CommandSuggestion
                    {
                        main = byAlias && !string.IsNullOrEmpty(c.alias) ? c.alias : c.name,
                        hint = c.description,
                        trailingSpace = true,
                    };
                    list.Add((s, rank * 100 + _ordered.IndexOf(c)));   // 同档按目录序稳定
                }
                return list.OrderBy(x => x.rank).Select(x => x.s).ToList();
            }

            // 参数阶段：命令已可识别才给参数提示
            var entry = Lookup(tokens[0]);
            if (entry?.suggester == null) return new List<CommandSuggestion>();
            return entry.suggester(tokens.Length - 2, tokens[tokens.Length - 1]);
        }

        // ==================== 解析 ====================

        /// <summary>归一化切分：去首尾空白、去可选 / 前缀、全角空格归一、按空白切分</summary>
        private static string[] Tokenize(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return Array.Empty<string>();
            string s = input.Trim().TrimStart('/');
            s = s.Replace((char)0x3000, ' ');   // 全角空格
            return s.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static CommandEntry Lookup(string token)
        {
            if (_commands.TryGetValue(token.ToLowerInvariant(), out var byName)) return byName;
            if (_commands.TryGetValue(token, out var byAlias)) return byAlias;   // 中文别名原样键
            return null;
        }

        /// <summary>前缀匹配分级：0=全等 1=前缀 2=包含 int.MaxValue=不匹配（help 空输入全列，由调用方特判）</summary>
        private static int PrefixRank(string candidate, string partial)
        {
            if (string.IsNullOrEmpty(partial)) return 2;   // 空输入全部可列（包含档）
            if (candidate.Equals(partial, StringComparison.OrdinalIgnoreCase)) return 0;
            if (candidate.StartsWith(partial, StringComparison.OrdinalIgnoreCase)) return 1;
            if (candidate.Contains(partial, StringComparison.OrdinalIgnoreCase)) return 2;
            return int.MaxValue;
        }

        // ==================== 注册（反射扫描） ====================

        private static void EnsureScanned()
        {
            if (_commands != null) return;
            _commands = new Dictionary<string, CommandEntry>();
            _ordered = new List<CommandEntry>();

            foreach (var method in AppDomain.CurrentDomain.GetAssemblies()
                         .SelectMany(asm => { try { return asm.GetTypes(); } catch { return Type.EmptyTypes; } })
                         .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                         .Where(m => m.GetCustomAttribute<GameCommandAttribute>() != null))
            {
                var attr = method.GetCustomAttribute<GameCommandAttribute>();
                var entry = new CommandEntry
                {
                    name = attr.Name,
                    alias = attr.Alias,
                    usage = attr.Usage,
                    description = attr.Description,
                    handler = (Func<CommandArgs, CommandResult>)Delegate.CreateDelegate(
                        typeof(Func<CommandArgs, CommandResult>), method),
                };

                if (!string.IsNullOrEmpty(attr.Suggester))
                {
                    var suggester = method.DeclaringType.GetMethod(attr.Suggester,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (suggester != null)
                        entry.suggester = (Func<int, string, List<CommandSuggestion>>)Delegate.CreateDelegate(
                            typeof(Func<int, string, List<CommandSuggestion>>), suggester);
                    else
                        GICLog.Warn($"[CommandSystem] {attr.Name} 的补全提供器 {attr.Suggester} 不存在，指令可用但无参数补全");
                }

                if (_commands.ContainsKey(entry.name))
                {
                    GICLog.Warn($"[CommandSystem] 指令重名被忽略: {entry.name}（{method.DeclaringType.Name}.{method.Name}）");
                    continue;
                }
                _commands[entry.name] = entry;
                if (!string.IsNullOrEmpty(entry.alias)) _commands[entry.alias] = entry;
                _ordered.Add(entry);
                GICLog.Info($"[CommandSystem] 注册指令: {entry.name}{(string.IsNullOrEmpty(entry.alias) ? "" : " / " + entry.alias)}");
            }
        }
    }
}
