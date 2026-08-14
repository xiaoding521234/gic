using System;
using UnityEngine;

namespace GIC.Framework
{
    /// <summary>
    /// 日志统一门面
    /// - Editor：全量输出（与引入前行为一致）
    /// - 打包：仅 Warn/Error/Exception 输出；Info 调用被编译器整体删除（含调用点字符串插值，零 GC）
    /// - 真机排查需要 Info 时：Player Settings 的 Scripting Define Symbols 加 GIC_LOG 宏重新打包
    ///   （opt-in：忘加只是少日志，绝不会误删数据或误过滤）
    ///
    /// 设计守则（旧版日志封装的常见 bug，本实现刻意规避）：
    /// 1. 不在初始化中设置 Debug.unityLogger.filterLogType 作为唯一手段 —— 见 ConfigureForRelease 注释
    /// 2. 不做运行时 if 开关 —— 那消除不了调用点的字符串插值分配；用 [Conditional] 编译期删除
    /// 3. [Conditional] 多特性为 OR 语义：UNITY_EDITOR 或 GIC_LOG 任一定义即保留
    /// 4. [Conditional] 方法只能作独立语句调用，不可作为方法组/委托传递（本项目无此用法）
    /// 5. 本文件内部必须直接调用 UnityEngine.Debug.* ——
    ///    严禁批量替换脚本处理此文件，否则方法体变成自我调用导致 StackOverflow（2026-08-14 实际踩坑）
    /// </summary>
    public static class GICLog
    {
        /// <summary>普通信息流：Editor 恒输出；打包默认编译期删除，加 GIC_LOG 宏启用</summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("GIC_LOG")]
        public static void Info(object message, UnityEngine.Object context = null)
            => Debug.Log(message, context);

        /// <summary>警告：所有构建恒输出（不加 [Conditional]）</summary>
        public static void Warn(object message, UnityEngine.Object context = null)
            => Debug.LogWarning(message, context);

        /// <summary>错误：所有构建恒输出</summary>
        public static void Error(object message, UnityEngine.Object context = null)
            => Debug.LogError(message, context);

        /// <summary>异常（保留完整堆栈）：所有构建恒输出</summary>
        public static void Exception(Exception exception, UnityEngine.Object context = null)
            => Debug.LogException(exception, context);

        /// <summary>
        /// Release 构建初始化：全局压制 Info 级输出（保留 Warning 及以上）。
        /// 必要性：项目内嵌的 Mirror 版本无日志分级 API（无 LogFactory），
        /// 其内部 Debug.Log 只能靠全局过滤器压制。
        /// 定义 GIC_LOG 宏做真机 verbose 排查时自动跳过压制，Info 可正常输出。
        /// </summary>
        public static void ConfigureForRelease()
        {
#if !UNITY_EDITOR && !GIC_LOG
            Debug.unityLogger.filterLogType = LogType.Warning;
#endif
        }
    }
}
