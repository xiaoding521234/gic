using System;
using Newtonsoft.Json.Linq;
using GIC.Tool;

namespace GIC.Pet.Chat
{
    /// <summary>
    /// 派蒙对话 Intent 层（docs/19 §6.5，2026-08-29）：LLM 工具定义 + 本地执行。
    /// 游戏内形态直调主进程系统——set_game_time/get_game_time 走 TimeUtility（广播
    /// OnGameTimeChangedEvent 刷新大厅背景/位置音乐）。
    /// 桌面形态是独立进程、无主进程引用——同款工具经 PetIntentIpc 文件通道转发
    /// 主进程执行（PetIntentIpcHost 消费，行为对齐）。
    /// 注：open_screen 界面切换指令已按用户拍板移除（2026-08-30）——界面切换手动足够快。
    /// </summary>
    public static class PetChatIntent
    {
        // ==================== 工具定义（OpenAI function calling） ====================

        /// <summary>设置游戏内时间（派蒙对话首批指令）</summary>
        public static PetChatClient.ToolDefinition SetGameTimeTool()
        {
            return new PetChatClient.ToolDefinition
            {
                function = new PetChatClient.ToolDefinition.ToolFunction
                {
                    name = "set_game_time",
                    description = "设置游戏内时间（影响大厅背景与音乐的白天/夜晚）。游戏启动时默认为系统时间。用户说'调到晚上/白天'或指定时刻（如'调到早上8点'）时调用。白天=8:00-20:00，其余为夜晚。",
                    parameters = "{\"type\":\"object\",\"properties\":{\"op\":{\"type\":\"string\",\"enum\":[\"set\",\"reset\"],\"description\":\"set=设置时间，reset=恢复跟随系统时间\"},\"hour\":{\"type\":\"integer\",\"description\":\"小时 0-23（op=set 时必填）\"},\"minute\":{\"type\":\"integer\",\"description\":\"分钟 0-59，默认 0\"}},\"required\":[\"op\"]}",
                }
            };
        }

        /// <summary>查询游戏内时间（用户问"现在几点"时调用）</summary>
        public static PetChatClient.ToolDefinition GetGameTimeTool()
        {
            return new PetChatClient.ToolDefinition
            {
                function = new PetChatClient.ToolDefinition.ToolFunction
                {
                    name = "get_game_time",
                    description = "查询当前游戏内时间与时段（白天/夜晚）。用户问现在游戏里几点/是白天还是晚上时调用。",
                    parameters = "{\"type\":\"object\",\"properties\":{}}",
                }
            };
        }

        // ==================== 执行入口（PaimonChatSession 工具循环回调） ====================

        /// <summary>执行工具。返回 JSON 字符串作 role:"tool" 回执，
        /// message 字段给 LLM 组织回复。</summary>
        public static string Execute(string toolName, string args)
        {
            try
            {
                switch (toolName)
                {
                    case "set_game_time": return ExecuteSetGameTime(args);
                    case "get_game_time": return ExecuteGetGameTime();
                }
                return Error($"未知工具: {toolName}");
            }
            catch (Exception e)
            {
                return Error(e.Message);
            }
        }

        // ---- 游戏时间 ----

        static string ExecuteSetGameTime(string args)
        {
            var p = JObject.Parse(args);
            string op = p["op"]?.Value<string>() ?? "set";
            if (op == "reset")
            {
                TimeUtility.ResetToSystemTime();
                return Ok($"游戏时间已恢复系统时间（{TimeUtility.Now:HH:mm}，{PeriodName(TimeUtility.GetCurrentTimePeriod())}）");
            }

            int hour = p["hour"] != null ? p["hour"].Value<int>() : -1;
            if (hour < 0 || hour > 23) return Error("hour 取值须为 0-23");
            int minute = p["minute"] != null ? p["minute"].Value<int>() : 0;
            if (minute < 0 || minute > 59) return Error("minute 取值须为 0-59");

            TimeUtility.SetGameTime(hour, minute);
            return Ok($"游戏时间已设为 {hour:00}:{minute:00}（{PeriodName(TimeUtility.GetCurrentTimePeriod())}），背景和音乐会随时段切换");
        }

        static string ExecuteGetGameTime()
        {
            bool overridden = TimeUtility.IsGameTimeOverridden;
            string source = overridden ? "已设置的游戏内时间" : "系统时间";
            return Ok($"当前游戏内时间 {TimeUtility.Now:HH:mm}（{PeriodName(TimeUtility.GetCurrentTimePeriod())}，来源：{source}）");
        }

        static string PeriodName(TimePeriod period) => period == TimePeriod.Daytime ? "白天" : "夜晚";

        // ---- 回执 JSON ----

        static string Ok(string message) => $"{{\"ok\":true,\"message\":\"{message}\"}}";
        static string Error(string message) => $"{{\"error\":\"{message}\"}}";
    }
}
