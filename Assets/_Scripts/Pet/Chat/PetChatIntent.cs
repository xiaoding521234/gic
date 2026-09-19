using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using GIC.Framework;

namespace GIC.Pet.Chat
{
    /// <summary>
    /// 派蒙对话 Intent 层（docs/19 §6.5）：LLM 工具定义 + 执行分发。
    /// 2026-09-13 指令系统统一重构：原 set_game_time/get_game_time/open_screen/auto_wish 四个游戏态工具
    /// 合并为单一 run_command 工具——派蒙与设置页指令输入共用 CommandSystem 同一套指令（行为对齐，
    /// 新指令注册即对派蒙自动可用）；指令实现分别在 GameCommandsCore（Framework：help/give/card/time）
    /// 与 GameCommandsPet（Pet：open/wish）。
    /// 会话层本地工具（memory_update/do_action/search_knowledge）不经本类；pet_go_ingame 是桌宠
    /// 三连击手势的 IPC 内部通道工具（非 LLM 生成），保持独立。
    /// 桌面形态是独立进程、无主进程引用——同款工具经 PetIntentIpc 文件通道转发主进程执行
    /// （PetIntentIpcHost 消费，协议不变）。
    /// </summary>
    public static class PetChatIntent
    {
        // ==================== 工具定义（OpenAI function calling） ====================

        /// <summary>执行游戏指令（游戏操作统一入口，指令目录=CommandSystem 注册表自动生成）</summary>
        public static PetChatClient.ToolDefinition RunCommandTool()
        {
            return new PetChatClient.ToolDefinition
            {
                function = new PetChatClient.ToolDefinition.ToolFunction
                {
                    name = "run_command",
                    description = "执行游戏指令，是游戏操作的唯一入口（给物品/给角色卡/调时间/开界面/自动抽卡等都走它）。可用指令：\n"
                        + CommandSystem.HelpText()
                        + "\n把用户的自然语言请求翻译成一条指令执行。例：'给我1000原石'→command='give 原石 1000'；"
                          + "'我想要温迪'→'card 温迪'；'调到晚上8点'→'time set 20:00'；'现在几点'→'time'；"
                          + "'打开背包'→'open backpack'；'帮我抽5次卡'→'wish 5'；不确定就执行 'help' 查目录。"
                          + "回执 message 是简短的开发者格式（如 '原石 +1000（持有 17711）'）——那是原始事实，"
                          + "请翻译成派蒙的口吻转告玩家；失败则转述原因，指令写错可按回执纠正后重试。",
                    parameters = "{\"type\":\"object\",\"properties\":{\"command\":{\"type\":\"string\",\"description\":\"指令行（不带斜杠），如 give 原石 1000 / card 温迪 / time set 20:00 / open map / wish 5 / help\"}},\"required\":[\"command\"]}",
                }
            };
        }

        // ==================== 执行入口（PaimonChatSession 工具循环回调） ====================

        /// <summary>执行工具。返回 JSON 字符串作 role:"tool" 回执，
        /// message 字段给 LLM 组织回复。异步指令（界面导航/自动抽卡）在 CommandSystem 内部
        /// 的隐藏常驻宿主上启动协程后立即返回——LLM 先回复"正在去做"，实际执行经反应通道反馈。</summary>
        public static string Execute(string toolName, string args)
        {
            try
            {
                switch (toolName)
                {
                    case "run_command":
                    {
                        var p = JObject.Parse(args ?? "{}");
                        string command = p["command"]?.Value<string>();
                        if (string.IsNullOrWhiteSpace(command))
                            return Error("command 参数为空（help 查全部指令）");
                        var result = CommandSystem.Execute(command);
                        return result.ok ? Ok(result.message) : Error(result.message);
                    }
                    case "pet_go_ingame": return ExecutePetGoInGame();
                }
                return Error($"未知工具: {toolName}");
            }
            catch (Exception e)
            {
                return Error(e.Message);
            }
        }

        // ---- 桌宠三连击形态接管（2026-09-01，非 LLM 工具——桌宠退出手势经 IPC 触发，共用本通道） ----

        /// <summary>桌宠三连击触发：主游戏在运行则把派蒙切换为游戏内形态（PetInGameHost.GestureSwitchTo
        /// ——存档 petForm=1 + 热切换；内部写 quit_request 优雅退出桌宠、等本进程退出后才创建游戏内
        /// 实例=游戏运行时恒有且只有 1 个派蒙）。桌宠自身照常播 Disappear 退出（发送方
        /// fire-and-forget，本回执无人读）。已处于游戏内形态（玩家手动多开桌宠）=no-op，
        /// 桌宠退出后仍只剩游戏内一个。</summary>
        static string ExecutePetGoInGame()
        {
            GIC.Pet.PetInGameHost.GestureSwitchTo(GIC.Pet.PetInGameHost.FormInGame);
            return Ok("已切换为游戏内形态");
        }

        // ---- 回执 JSON（message 经 JsonConvert 序列化——help 等多行/含引号文案不会被拼坏） ----

        static string Ok(string message) => "{\"ok\":true,\"message\":" + JsonConvert.SerializeObject(message) + "}";
        static string Error(string message) => "{\"error\":" + JsonConvert.SerializeObject(message) + "}";

        /// <summary>解析 Ok/Error 回执 JSON 为展示文本（聊天框 "/" 指令模式 UI 层用，2026-09-13）：
        /// ok→message 原文（开发者格式直出，不经 LLM）；error→错误文本；非预期格式原样兜底。</summary>
        public static string ResultMessageOf(string resultJson)
        {
            try
            {
                var p = JObject.Parse(resultJson ?? "");
                if (p["ok"]?.Value<bool>() == true) return p["message"]?.Value<string>() ?? "完成";
                return p["error"]?.Value<string>() ?? "指令执行失败";
            }
            catch { return resultJson ?? ""; }
        }
    }
}
