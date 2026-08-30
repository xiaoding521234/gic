using System;
using UnityEngine;
using GIC.UI;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.Battle;
namespace GIC.Tool
{


    public static class TimeUtility
    {
        /// <summary>
        /// 默认白天开始时间（8点）
        /// </summary>
        public static int DayStartHour = 8;

        /// <summary>
        /// 默认白天结束时间（20点）
        /// </summary>
        public static int DayEndHour = 20;

        // 游戏内时间偏移（秒）：false=未设置=直接用系统时间。"每次游戏启动初始为系统时间"由
        // 进程内静态状态天然保证——偏移不持久化，重启即回系统时间。派蒙对话 set_game_time 工具
        // （PetChatIntent，2026-08-29）写入；变更广播 OnGameTimeChangedEvent，大厅背景/位置音乐
        // 订阅方自行刷新白天/夜晚表现。
        private static double _gameTimeOffsetSec;
        private static bool _gameTimeOverridden;

        /// <summary>游戏内时间（设了偏移后仍随真实时间流逝继续走——平移不冻结）</summary>
        public static DateTime Now => _gameTimeOverridden
            ? DateTime.Now.AddSeconds(_gameTimeOffsetSec)
            : DateTime.Now;

        /// <summary>是否已设置游戏内时间（false=跟随系统时间）</summary>
        public static bool IsGameTimeOverridden => _gameTimeOverridden;

        /// <summary>设置游戏内时间为当天 hour:minute（广播时间变更事件，订阅方刷新背景/音乐）</summary>
        public static void SetGameTime(int hour, int minute)
        {
            hour = Mathf.Clamp(hour, 0, 23);
            minute = Mathf.Clamp(minute, 0, 59);
            var target = DateTime.Now.Date.AddHours(hour).AddMinutes(minute);
            _gameTimeOffsetSec = (target - DateTime.Now).TotalSeconds;
            _gameTimeOverridden = true;
            GICLog.Info($"[TimeUtility] 游戏内时间已设置: {hour:00}:{minute:00}");
            NotifyGameTimeChanged();
        }

        /// <summary>恢复跟随系统时间（广播时间变更事件）</summary>
        public static void ResetToSystemTime()
        {
            _gameTimeOffsetSec = 0;
            _gameTimeOverridden = false;
            GICLog.Info("[TimeUtility] 游戏内时间已恢复系统时间");
            NotifyGameTimeChanged();
        }

        static void NotifyGameTimeChanged()
        {
            EventBusHub.Instance?.SendImmediate(new OnGameTimeChangedEvent
            {
                NewPeriod = GetCurrentTimePeriod()
            });
        }

        /// <summary>
        /// 获取时间段后缀
        /// </summary>
        public static string GetTimeSuffix()
        {
            string result = GetTimeSuffix(DayStartHour, DayEndHour);
            GICLog.Info($"[TimeUtility] GetTimeSuffix() 返回: {result} (游戏内时间: {Now:HH:mm})");
            return result;
        }

        /// <summary>
        /// 获取时间段后缀（自定义时间范围）
        /// </summary>
        /// <param name="dayStartHour">白天开始小时（如 8）</param>
        /// <param name="dayEndHour">白天结束小时（如 20）</param>
        public static string GetTimeSuffix(int dayStartHour, int dayEndHour)
        {
            int hour = Now.Hour;
            bool isDaytime = hour >= dayStartHour && hour < dayEndHour;

            string result = isDaytime ? "daytime" : "night";

            GICLog.Info($"[TimeUtility] GetTimeSuffix({dayStartHour}, {dayEndHour}) - 当前小时: {hour}, 是否为白天: {isDaytime}, 返回: {result}");

            return result;
        }

        /// <summary>
        /// 获取时间段枚举
        /// </summary>
        public static TimePeriod GetCurrentTimePeriod()
        {
            TimePeriod result = GetCurrentTimePeriod(DayStartHour, DayEndHour);
            GICLog.Info($"[TimeUtility] GetCurrentTimePeriod() 返回: {result} (游戏内时间: {Now:HH:mm})");
            return result;
        }

        public static TimePeriod GetCurrentTimePeriod(int dayStartHour, int dayEndHour)
        {
            int hour = Now.Hour;
            bool isDaytime = hour >= dayStartHour && hour < dayEndHour;

            TimePeriod result = isDaytime ? TimePeriod.Daytime : TimePeriod.Night;

            GICLog.Info($"[TimeUtility] GetCurrentTimePeriod({dayStartHour}, {dayEndHour}) - 当前小时: {hour}, 是否为白天: {isDaytime}, 返回: {result}");

            return result;
        }
    }

    public enum TimePeriod
    {
        Daytime,
        Night
    }
}
