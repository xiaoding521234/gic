using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace GIC.Framework
{
    /// <summary>
    /// LAN 排查文件日志（2026-10-14 LAN 批）：kcp/Telepathy 联机链路关键点落盘取证——
    /// 手机 Release 包上 GICLog.Info 被 [Conditional] 编译删除、logcat 又需 adb，
    /// 文件日志是手机侧唯一免工具取证通道。写 persistentDataPath/lan_debug.log（时间戳+逐行 flush），
    /// 超 1MB 重开（防无限增长）。写失败静默吞（排查日志绝不反噬主流程）。
    /// 用法：LanDebugFileLog.WriteLine("任意消息")。
    /// </summary>
    public static class LanDebugFileLog
    {
        static readonly object _lock = new object();
        static string _path;
        const long MaxBytes = 1024 * 1024;

        public static string Path => _path ??= System.IO.Path.Combine(Application.persistentDataPath, "lan_debug.log");

        static bool _hookInstalled;

        /// <summary>把 Unity 全局 Error/Exception/Assert 落盘（vivo Android 16 对 adb logcat 锁死应用日志，
        /// 此通道是手机端异常的唯一可见出口）。重复调用安全（幂等）。</summary>
        public static void InstallGlobalHook()
        {
            if (_hookInstalled) return;
            _hookInstalled = true;
            Application.logMessageReceived += (condition, stackTrace, type) =>
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                    WriteLine($"[{type}] {condition}\n{stackTrace}");
            };
        }

        public static void WriteLine(string msg)
        {
            try
            {
                lock (_lock)
                {
                    var fi = new FileInfo(Path);
                    if (fi.Exists && fi.Length > MaxBytes) fi.Delete();
                    File.AppendAllText(Path, $"{DateTime.Now:HH:mm:ss.fff} {msg}\n", Encoding.UTF8);
                }
            }
            catch { /* 取证日志绝不反噬主流程 */ }
        }
    }
}
