using System;
using GIC.Framework;
using UnityEngine;

namespace GIC.Pet
{
    /// <summary>
    /// 桌宠进程本地指令拦截（2026-09-13 快捷消息）：run_command 的少部分指令在桌宠进程内即可——
    /// 甚至只有在此才能完成（主游戏没开时 IPC 文件通道无消费者，转发主进程是死路）：
    /// - start_game：启动主游戏（桌宠唯一"游戏没开也有效"的游戏操作）。已开判定=GameWindowState
    ///   心跳新鲜（防双开——两个游戏进程会双消费 pet_intent IPC 造成指令重复执行）；
    ///   窗口位置恢复由主游戏自己按 game_window.json 记录执行（"自动记录最后一次主游戏所在位置"）。
    /// - qm：编辑 pet.json 快捷消息（pet.json 是桌宠域数据，本地读改写；主进程侧同一实现走
    ///   CommandSystem 的 qm 注册——两进程零分叉）。
    /// 其余指令返回 null，调用方（PetWindowController.ExecuteChatTool）照旧 IPC 转发主进程。
    /// 回执 JSON 与 PetChatIntent 同格式（{"ok":true,"message":…}/{"error":…}）。
    /// </summary>
    public static class PetLocalCommands
    {
        [Serializable]
        private class RunCommandArgs { public string command; }

        /// <summary>尝试本地执行；非本地指令（tool 非 run_command/命令词不在本地表/参数解析失败）
        /// 返回 null 交调用方走 IPC。</summary>
        public static string TryExecuteJson(string toolName, string toolArgsJson)
        {
            if (toolName != "run_command") return null;
            string command = null;
            try
            {
                var a = JsonUtility.FromJson<RunCommandArgs>(toolArgsJson ?? "{}");
                command = a?.command;
            }
            catch { return null; } // 解析不了=交主进程（它的错误回执更具体）
            if (string.IsNullOrWhiteSpace(command)) return null;

            var tokens = CommandSystem.TokenizeLine(command);
            if (tokens.Length == 0) return null;
            switch (tokens[0].ToLowerInvariant())
            {
                case "start_game":
                case "启动游戏":
                    return Json(StartGame());
                case "qm":
                case "快捷消息":
                    return Json(PetQuickMessages.Execute(ToArgs(tokens)));
                default:
                    return null;
            }
        }

        /// <summary>启动主游戏：同 exe 双形态（docs/19 §5.2）——不带 --pet-mode 启动自身路径=主游戏</summary>
        static CommandResult StartGame()
        {
            if (GameWindowState.IsGameAlive())
                return CommandResult.Error("游戏已在运行");
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            try
            {
                string exe = Process.GetCurrentProcess().MainModule.FileName;
                Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(exe),
                });
                return CommandResult.Ok("游戏启动中");
            }
            catch (Exception e)
            {
                return CommandResult.Error($"启动失败: {e.Message}");
            }
#else
            return CommandResult.Error("仅 Windows 构建可用");
#endif
        }

        /// <summary>CommandArgs 组装（tokens[0]=命令词，其余=参数——与 CommandSystem.Execute 同构）</summary>
        static CommandArgs ToArgs(string[] tokens) => new CommandArgs
        {
            tokens = tokens.Length > 1 ? tokens[1..] : Array.Empty<string>(),
            line = string.Join(" ", tokens),
        };

        /// <summary>CommandResult → PetChatIntent 同款回执 JSON（message 经 JsonConvert 序列化防拼坏）</summary>
        static string Json(CommandResult result) =>
            result.ok
                ? "{\"ok\":true,\"message\":" + Newtonsoft.Json.JsonConvert.SerializeObject(result.message) + "}"
                : "{\"error\":" + Newtonsoft.Json.JsonConvert.SerializeObject(result.message) + "}";
    }
}
