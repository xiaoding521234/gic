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
        
        /// <summary>
        /// 获取时间段后缀
        /// </summary>
        public static string GetTimeSuffix()
        {
            string result = GetTimeSuffix(DayStartHour, DayEndHour);
            GICLog.Info($"[TimeUtility] GetTimeSuffix() 返回: {result} (当前时间: {DateTime.Now:HH:mm})");
            return result;
        }
        
        /// <summary>
        /// 获取时间段后缀（自定义时间范围）
        /// </summary>
        /// <param name="dayStartHour">白天开始小时（如 8）</param>
        /// <param name="dayEndHour">白天结束小时（如 20）</param>
        public static string GetTimeSuffix(int dayStartHour, int dayEndHour)
        {
            int hour = DateTime.Now.Hour;
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
            GICLog.Info($"[TimeUtility] GetCurrentTimePeriod() 返回: {result} (当前时间: {DateTime.Now:HH:mm})");
            return result;
        }
        
        public static TimePeriod GetCurrentTimePeriod(int dayStartHour, int dayEndHour)
        {
            int hour = DateTime.Now.Hour;
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


