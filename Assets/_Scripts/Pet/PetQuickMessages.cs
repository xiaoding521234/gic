using System;
using System.Collections.Generic;
using System.Linq;
using GIC.Framework;

namespace GIC.Pet
{
    /// <summary>
    /// 快捷消息（2026-09-13 拖拽派蒙气泡）：对话开着时在派蒙身上拖拽弹出的快捷消息（≤5 条），
    /// 松手在气泡上=快捷发送（与手打同链路："/"开头走本地指令模式 docs/25 §8，否则发 LLM 对话）。
    /// 持久层=pet.json quickMessages（PetPrefs v7 直读直写通道——两进程共享 persistentDataPath，
    /// 桌宠进程展开气泡时读、qm 指令编辑时写，绕缓存互相立即可见）。
    /// 编辑入口三合一（同一 Execute 实现）：设置页/游戏内聊天框 qm 指令、桌宠本地拦截 qm、
    /// 与派蒙对话（run_command 工具目录自动含 qm）。
    /// 初始预设（用户拍板 2026-09-13）：抽温迪卡池10次 / 关闭游戏 / 启动游戏（主游戏自动记录
    /// 上次窗口位置，GameWindowState）。文案=开发者基调（docs/25 §5）。
    /// </summary>
    public static class PetQuickMessages
    {
        /// <summary>上限（气泡布局按 5 条设计）</summary>
        public const int Max = 5;

        /// <summary>初始预设：前三条 /开头=指令行（wish 温迪 10 / quit 关闭游戏 / start_game 启动游戏）；
        /// 第四条"你好"=纯文本消息（2026-09-13 用户拍板追加——快捷消息本质=免重复输入任意消息，
        /// 非指令类松手后走 LLM 对话）</summary>
        static readonly string[] Presets = { "/wish 温迪 10", "/quit", "/start_game", "你好" };

        /// <summary>读全量（首次读取回灌预设——quickSeeded 区分"从未初始化"与"用户清空"；
        /// v8 老档补齐：v7 已种子档缺"你好"预设，一次性补进（version 推进后用户再删除不回灌）；
        /// 返回副本防调用方直改列表）。空列表=合法状态。</summary>
        public static List<string> ReadAll()
        {
            var list = PetPrefs.ReadQuickMessages();
            if (!PetPrefs.ReadQuickSeeded())
            {
                list = new List<string>(Presets);
                PetPrefs.WriteQuickMessages(list, true);
            }
            else if (PetPrefs.ReadVersion() < 8 && !list.Contains(Presets[3]) && list.Count < Max)
            {
                list.Add(Presets[3]);
                PetPrefs.WriteQuickMessages(list, true);
            }
            return new List<string>(list ?? new List<string>());
        }

        /// <summary>qm 指令分发（游戏内=CommandSystem 注册的 qm；桌面=PetLocalCommands 本地拦截——
        /// 同一实现两进程零分叉）。无参=列表；add/set/del/clear 编辑。</summary>
        public static CommandResult Execute(CommandArgs args)
        {
            string op = args.Count > 0 ? args.Get(0).ToLowerInvariant() : "";
            switch (op)
            {
                case "": return List();
                case "add": return Add(JoinFrom(args, 1));
                case "set": return Set(args);
                case "del":
                case "rm": return Del(args);
                case "clear": return Clear();
                default: return CommandResult.Error($"未知子指令: {op}（add/set/del/clear）");
            }
        }

        // ---- 子操作（全部读改写经 PetPrefs.WriteQuickMessages，磁盘现值为准） ----

        static CommandResult List()
        {
            var list = ReadAll();
            if (list.Count == 0) return CommandResult.Ok("空");
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < list.Count; i++)
                sb.Append(i + 1).Append(". ").Append(list[i]).Append(i < list.Count - 1 ? "\n" : "");
            return CommandResult.Ok(sb.ToString());
        }

        static CommandResult Add(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return CommandResult.Error("用法: qm add <消息>");
            var list = ReadAll();
            if (list.Count >= Max) return CommandResult.Error($"快捷消息已满（{Max}）");
            list.Add(message.Trim());
            PetPrefs.WriteQuickMessages(list, true);
            return CommandResult.Ok($"已添加: {message.Trim()}（{list.Count}/{Max}）");
        }

        static CommandResult Set(CommandArgs args)
        {
            if (args.Count < 3) return CommandResult.Error("用法: qm set <序号> <消息>");
            var list = ReadAll();
            if (!TryIndex(args.Get(1), list.Count, out int index)) return IndexError(list.Count);
            string message = JoinFrom(args, 2).Trim();
            if (string.IsNullOrWhiteSpace(message)) return CommandResult.Error("用法: qm set <序号> <消息>");
            list[index] = message;
            PetPrefs.WriteQuickMessages(list, true);
            return CommandResult.Ok($"已更新: {index + 1}. {message}");
        }

        static CommandResult Del(CommandArgs args)
        {
            if (args.Count < 2) return CommandResult.Error("用法: qm del <序号>");
            var list = ReadAll();
            if (!TryIndex(args.Get(1), list.Count, out int index)) return IndexError(list.Count);
            string removed = list[index];
            list.RemoveAt(index);
            PetPrefs.WriteQuickMessages(list, true);
            return CommandResult.Ok($"已删除: {index + 1}. {removed}（剩 {list.Count}）");
        }

        static CommandResult Clear()
        {
            PetPrefs.WriteQuickMessages(new List<string>(), true);
            return CommandResult.Ok("已清空");
        }

        // ---- 工具 ----

        /// <summary>参数拼接（快捷消息原文可含空格：tokens 从 from 起以空格连回）</summary>
        static string JoinFrom(CommandArgs args, int from) =>
            string.Join(" ", args.tokens.Skip(Math.Max(0, from)));

        /// <summary>1 基序号解析（qm set 2 = 第 2 条）</summary>
        static bool TryIndex(string token, int count, out int index)
        {
            index = -1;
            return int.TryParse(token, out int n) && n >= 1 && n <= count && (index = n - 1) >= 0;
        }

        static CommandResult IndexError(int count) =>
            CommandResult.Error(count == 0 ? "空（无条目可操作）" : $"序号须为 1-{count}");
    }
}
