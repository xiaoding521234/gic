using System;
using System.Collections;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using GIC.Data;
using GIC.Framework;
using GIC.Tool;
using GIC.UI;

namespace GIC.Pet.Chat
{
    /// <summary>
    /// 派蒙对话 Intent 层（docs/19 §6.5 首批指令，2026-08-29）：LLM 工具定义 + 本地执行。
    /// 游戏内形态直调主进程系统——set_game_time/get_game_time 走 TimeUtility（广播
    /// OnGameTimeChangedEvent 刷新大厅背景/位置音乐），open_screen 走 GameScene 场景导航。
    /// 桌面形态是独立进程、无主进程引用（IPC 通道未建，docs/19 §8 遗留），不注册这批工具。
    ///
    /// 场景导航规则（与现有场景架构严格一致，docs/17）：各功能界面是大厅上的 Additive 弹层、
    /// 互为平级——从界面 A 打开界面 B 必须先**标准关闭** A（ScreenBase.Close：退场动画+音乐
    /// 恢复+GoBack 回大厅）再加载 B，绝不叠层。联机界面（CoopScreen）有房间状态机
    /// （建房/离房流程特殊），不纳入 AI 导航——其打开时本工具直接回"请先手动退出联机界面"。
    /// </summary>
    public static class PetChatIntent
    {
        // ---- 界面导航表（AI 可打开的弹层界面；联机/战斗不纳入——房间状态机/未实现） ----

        private class ScreenEntry
        {
            public readonly string key;      // LLM 枚举值
            public readonly string cnName;   // 中文名（工具描述映射 + 回复文案）
            public readonly SceneType scene;

            public ScreenEntry(string key, string cnName, SceneType scene)
            {
                this.key = key;
                this.cnName = cnName;
                this.scene = scene;
            }
        }

        private static readonly ScreenEntry[] _screens =
        {
            new ScreenEntry("hall", "大厅", null), // null=回大厅（关闭当前弹层界面，不新开）
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

        /// <summary>打开指定游戏界面（派蒙对话首批指令）</summary>
        public static PetChatClient.ToolDefinition OpenScreenTool()
        {
            return new PetChatClient.ToolDefinition
            {
                function = new PetChatClient.ToolDefinition.ToolFunction
                {
                    name = "open_screen",
                    description = "打开游戏主界面。若当前已在其它界面，会自动先退出回大厅再打开目标界面。screen 取值：hall=回大厅（关闭当前界面）、map=地图、backpack=背包、wish=祈愿、settings=设置。注意：联机界面打开期间不可用。",
                    parameters = "{\"type\":\"object\",\"properties\":{\"screen\":{\"type\":\"string\",\"enum\":[\"hall\",\"map\",\"backpack\",\"wish\",\"settings\"],\"description\":\"要打开的界面\"}},\"required\":[\"screen\"]}",
                }
            };
        }

        // ==================== 执行入口（PaimonChatSession 工具循环回调） ====================

        /// <summary>执行工具（runner=宿主 MonoBehaviour，界面导航协程跑在它身上——
        /// 游戏内宿主 DontDestroyOnLoad，跨场景存活）。返回 JSON 字符串作 role:"tool" 回执，
        /// message 字段给 LLM 组织回复。</summary>
        public static string Execute(string toolName, string args, MonoBehaviour runner)
        {
            try
            {
                switch (toolName)
                {
                    case "set_game_time": return ExecuteSetGameTime(args);
                    case "get_game_time": return ExecuteGetGameTime();
                    case "open_screen": return ExecuteOpenScreen(args, runner);
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

        static string ExecuteOpenScreen(string args, MonoBehaviour runner)
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

            // 联机界面打开中：房间状态机（建房/离房/准备流程）特殊，AI 导航会让流程打架
            if (SceneManager.GetSceneByName("CoopScreen").isLoaded)
                return Error("联机界面打开中，请先手动退出联机界面再让派蒙打开其它界面");

            bool inPopup = gs.CurrentScene != null && gs.CurrentScene.LoadMode == LoadSceneMode.Additive && gs.HasPreviousScene();

            // hall=回大厅：已在大厅无事可做；在弹层界面=标准关闭它（不新开）
            if (entry.scene == null)
            {
                if (!inPopup) return Ok("现在就在大厅");
                if (runner == null) return Error("宿主未就绪，无法导航");
                runner.StartCoroutine(NavigateRoutine(gs, entry));
                return Ok("正在回大厅");
            }

            // 目标已打开：无事可做
            if (gs.CurrentScene != null && gs.CurrentScene.SceneName == entry.scene.SceneName)
                return Ok($"{entry.cnName}界面已经打开了");

            if (runner == null) return Error("宿主未就绪，无法导航");

            runner.StartCoroutine(NavigateRoutine(gs, entry));
            return Ok($"正在打开{entry.cnName}界面");
        }

        /// <summary>导航协程：当前在其它弹层界面 → 标准关闭（退场动画+音乐恢复+GoBack 回大厅）
        /// → 等转场锁释放 → 加载目标界面（entry.scene==null 即 hall=只回大厅不新开）。
        /// 等不到（关闭异常）超时兜底仍尝试加载。</summary>
        static IEnumerator NavigateRoutine(GameScene gs, ScreenEntry entry)
        {
            // 1. 当前在弹层界面（历史栈非空=下面还压着大厅）→ 先标准关闭回大厅
            if (gs.CurrentScene != null && gs.CurrentScene.LoadMode == LoadSceneMode.Additive && gs.HasPreviousScene())
            {
                CloseCurrentScreen(gs.CurrentScene.SceneName);
                // 等关闭流程走完（历史栈清空=已回根场景）；退场动画最长约 1s，3s 超时兜底
                float deadline = Time.unscaledTime + 3f;
                while (gs.HasPreviousScene() && Time.unscaledTime < deadline) yield return null;
            }

            // 2. hall（scene==null）：关闭流程已回到大厅，无事可做
            if (entry.scene == null) yield break;

            // 3. 等转场锁释放（GoBack 协程末尾才 pop；持锁期间 Load 会被拦截丢弃）
            float deadline2 = Time.unscaledTime + 3f;
            while (gs.IsTransitioning && Time.unscaledTime < deadline2) yield return null;

            // 4. 打开目标界面（LoadSceneWithConfig 自带 SceneTransition 锁+历史记录）
            entry.scene.Load();
        }

        /// <summary>标准关闭当前弹层界面：场景内找 ScreenBase 走 Close()（与用户按 ESC 同路径：
        /// 退场动画+音乐恢复+GoBack）；找不到（异常态）兜底直接 GoBack。</summary>
        static void CloseCurrentScreen(string sceneName)
        {
            var scene = SceneManager.GetSceneByName(sceneName);
            if (scene.isLoaded)
            {
                foreach (var root in scene.GetRootGameObjects())
                {
                    var screen = root.GetComponentInChildren<ScreenBase>(true);
                    if (screen != null)
                    {
                        screen.Close();
                        return;
                    }
                }
            }
            GameScene.Instance.GoBack();
        }

        // ---- 回执 JSON ----

        static string Ok(string message) => $"{{\"ok\":true,\"message\":\"{message}\"}}";
        static string Error(string message) => $"{{\"error\":\"{message}\"}}";
    }
}
