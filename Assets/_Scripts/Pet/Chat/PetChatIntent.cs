using System;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using GIC.Framework;
using GIC.Tool;
using GIC.Data;

namespace GIC.Pet.Chat
{
    /// <summary>
    /// 派蒙对话 Intent 层（docs/19 §6.5）：LLM 工具定义 + 本地执行。
    /// 游戏内形态直调主进程系统——set_game_time/get_game_time 走 TimeUtility（广播
    /// OnGameTimeChangedEvent 刷新大厅背景/位置音乐）；open_screen/auto_wish 的异步执行
    /// 走 PetGameBridge/PetWishAutoRunner（隐藏常驻宿主上的协程，Execute 立即返回）。
    /// 桌面形态是独立进程、无主进程引用——同款工具经 PetIntentIpc 文件通道转发
    /// 主进程执行（PetIntentIpcHost 消费，行为对齐）。
    ///
    /// open_screen 于 2026-08-30 重做恢复（AI 抽卡需要切界面能力）：导航与手动完全同路径
    /// （回大厅后 MainHallScreen.ExitToSceneAsync），根治首版直接 Load 的大厅按钮透出叠穿
    /// （教训留档 docs/19 §6.5）。联机（房间状态机）/战斗界面不纳入 AI 导航。
    /// </summary>
    public static class PetChatIntent
    {
        // ---- 界面导航表（AI 可打开的弹层界面；联机/战斗不纳入——房间状态机/对局进行中） ----

        private class ScreenEntry
        {
            public readonly string key;      // LLM 枚举值
            public readonly string cnName;   // 中文名（工具描述映射 + 回复文案）
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

        /// <summary>打开指定游戏界面（2026-08-30 恢复：AI 抽卡等指令需要切界面能力；
        /// 导航与手动完全同路径——先退出当前界面回大厅，再走大厅的切换流程）</summary>
        public static PetChatClient.ToolDefinition OpenScreenTool()
        {
            return new PetChatClient.ToolDefinition
            {
                function = new PetChatClient.ToolDefinition.ToolFunction
                {
                    name = "open_screen",
                    description = "打开游戏主界面。若当前已在其它界面，会自动先退出回大厅再打开目标界面。screen 取值：hall=回大厅（关闭当前界面）、map=地图、backpack=背包、wish=祈愿、settings=设置。注意：联机/战斗界面打开期间不可用。",
                    parameters = "{\"type\":\"object\",\"properties\":{\"screen\":{\"type\":\"string\",\"enum\":[\"hall\",\"map\",\"backpack\",\"wish\",\"settings\"],\"description\":\"要打开的界面\"}},\"required\":[\"screen\"]}",
                }
            };
        }

        /// <summary>帮玩家自动抽卡（2026-08-30）：自动打开祈愿界面并以自动射击模式连续抽卡，
        /// 抽到五星/四星或连续烂卡时派蒙会做出动作与消息反应</summary>
        public static PetChatClient.ToolDefinition AutoWishTool()
        {
            return new PetChatClient.ToolDefinition
            {
                function = new PetChatClient.ToolDefinition.ToolFunction
                {
                    name = "auto_wish",
                    description = "帮玩家自动抽卡（祈愿）。会自动打开祈愿界面并自动连续抽 count 次（不需要玩家操作）。抽到五星/四星或连续抽到烂卡时你会做出动作和消息反应。每次祈愿消耗160原石，原石不足会失败。用户说'帮我抽卡/帮我抽N次'时调用；用户没说次数时 count 默认为 1。",
                    parameters = "{\"type\":\"object\",\"properties\":{\"count\":{\"type\":\"integer\",\"description\":\"抽卡次数 1-10，默认 1\"}}}",
                }
            };
        }

        // ==================== 执行入口（PaimonChatSession 工具循环回调） ====================

        /// <summary>执行工具。返回 JSON 字符串作 role:"tool" 回执，
        /// message 字段给 LLM 组织回复。异步指令（界面导航/自动抽卡）在隐藏常驻宿主上
        /// 启动协程后立即返回——LLM 先回复"正在去做"，实际执行经反应通道反馈。</summary>
        public static string Execute(string toolName, string args)
        {
            try
            {
                switch (toolName)
                {
                    case "set_game_time": return ExecuteSetGameTime(args);
                    case "get_game_time": return ExecuteGetGameTime();
                    case "open_screen": return ExecuteOpenScreen(args);
                    case "auto_wish": return ExecuteAutoWish(args);
                    case "pet_go_ingame": return ExecutePetGoInGame();
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

        // ---- 界面导航 ----

        static string ExecuteOpenScreen(string args)
        {
            var p = JObject.Parse(args);
            string key = p["screen"]?.Value<string>();
            var entry = Array.Find(_screens, s => s.key == key);
            if (entry == null)
            {
                var keys = new string[_screens.Length];
                for (int i = 0; i < _screens.Length; i++) keys[i] = _screens[i].key;
                return Error($"未知界面 {key}（可选：{string.Join("/", keys)}）");
            }

            var gs = GameScene.Instance;
            if (gs == null) return Error("主进程场景管理器未就绪");

            string busy = BlockedSceneCheck();
            if (busy != null) return Error(busy);

            bool inPopup = gs.CurrentScene != null && gs.CurrentScene.LoadMode == LoadSceneMode.Additive && gs.HasPreviousScene();

            // hall=回大厅：已在大厅无事可做；在弹层界面=标准关闭它（不新开）
            if (entry.scene == null)
            {
                if (!inPopup) return Ok("现在就在大厅");
                PetGameBridge.Run(PetGameBridge.NavigateToScreen(null));
                return Ok("正在回大厅");
            }

            // 目标已打开：无事可做
            if (gs.CurrentScene != null && gs.CurrentScene.SceneName == entry.scene.SceneName)
                return Ok($"{entry.cnName}界面已经打开了");

            PetGameBridge.Run(PetGameBridge.NavigateToScreen(entry.scene));
            return Ok($"正在打开{entry.cnName}界面");
        }

        /// <summary>联机/战斗界面互斥检查（两者有进行中的状态机，AI 强切会打断流程）</summary>
        static string BlockedSceneCheck()
        {
            if (SceneManager.GetSceneByName("CoopScreen").isLoaded)
                return "联机界面打开中，请先手动退出联机界面再让派蒙操作";
            if (SceneManager.GetSceneByName("BattleScreen").isLoaded)
                return "战斗进行中，请先手动结束战斗再让派蒙操作";
            return null;
        }

        // ---- 自动抽卡 ----

        static string ExecuteAutoWish(string args)
        {
            var p = JObject.Parse(args);
            int count = 1;
            if (p["count"] != null) count = p["count"].Value<int>();
            count = Mathf.Clamp(count, 1, 10);

            var gs = GameScene.Instance;
            if (gs == null) return Error("主进程场景管理器未就绪");

            string busy = BlockedSceneCheck();
            if (busy != null) return Error(busy);

            if (PetWishAutoRunner.IsBusy)
                return Error("派蒙已经在抽卡啦，等这轮结束再说");

            // 祈愿界面已开且抽卡进行中（含最终展示期）→ 拒绝
            if (SceneManager.GetSceneByName("WishScreen").isLoaded)
            {
                var wish = UnityEngine.Object.FindFirstObjectByType<GIC.UI.WishScreen>(FindObjectsInactive.Exclude);
                if (wish != null && wish.IsWishInProgress)
                    return Error("现在正在抽卡呀，等这轮结束再说吧");
            }

            // 原石同步预检（不足立即回执，LLM 向用户转述；游戏内抽卡场景的后续变化由运行时守卫兜底）
            var save = Wargame.Instance?.Context?.Get<SaveManager>()?.CurrentSave;
            if (save == null) return Error("存档未就绪，稍等一下再试");
            int cost = GIC.UI.WishManager.SingleWishCost * count;
            int have = save.GetItemCount(ItemName.Primogem);
            if (have < cost)
                return Error($"原石不够：抽{count}次需要{cost}原石，现在只有{have}。请转告旅行者先攒攒原石。");

            if (!PetWishAutoRunner.Begin(count))
                return Error("派蒙已经在抽卡啦，等这轮结束再说");

            return Ok($"好嘞！派蒙这就去抽{count}次卡！（会自动打开祈愿界面，抽到好卡坏卡派蒙都会有反应）");
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

        // ---- 回执 JSON ----

        static string Ok(string message) => $"{{\"ok\":true,\"message\":\"{message}\"}}";
        static string Error(string message) => $"{{\"error\":\"{message}\"}}";
    }
}
