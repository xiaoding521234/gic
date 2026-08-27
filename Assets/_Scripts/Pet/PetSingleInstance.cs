using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace GIC.Pet
{
    /// <summary>
    /// 桌宠单例（命名 Mutex）：桌宠进程与游戏主进程是同一个 exe 的两种形态，
    /// 且桌宠独立于游戏存活（游戏重启不得拉出第二只）——用内核对象做全局唯一标记。
    /// 派蒙进程启动时 Acquire() 声明自己（重复则退出）；游戏侧用 Probe() 只读探测。
    /// Mutex 随持有进程结束被内核自动释放，崩溃场景无残留。
    /// </summary>
    public static class PetSingleInstance
    {
        private const string MutexName = @"Local\GIC_PaimonPet_SingleInstance";

        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateMutex(IntPtr lpMutexAttributes, bool bInitialOwner, string lpName);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenMutex(uint dwDesiredAccess, bool bInheritHandle, string lpName);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ReleaseMutex(IntPtr hMutex);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr hObject);

        private const uint SYNCHRONIZE = 0x00100000;
        private const uint MUTEX_QUERY_STATE = 0x0001;
        private const int ERROR_ALREADY_EXISTS = 183;

        private static IntPtr ownedMutex = IntPtr.Zero;

        /// <summary>
        /// 派蒙进程启动早期调用：声明单例。返回 false = 已有一只派蒙，调用方应立即退出。
        /// </summary>
        public static bool Acquire()
        {
            if (ownedMutex != IntPtr.Zero) return true;
            IntPtr h = CreateMutex(IntPtr.Zero, false, MutexName);
            if (h == IntPtr.Zero) return true; // 创建失败（罕见）：放行，宁可重复不要拒活
            if (Marshal.GetLastWin32Error() == ERROR_ALREADY_EXISTS)
            {
                CloseHandle(h);
                return false;
            }
            ownedMutex = h; // 持有到进程结束，故意不释放
            return true;
        }

        /// <summary>
        /// 游戏侧探测：桌宠是否已在桌面。只读打开，绝不创建。
        /// </summary>
        public static bool Probe()
        {
            IntPtr h = OpenMutex(SYNCHRONIZE | MUTEX_QUERY_STATE, false, MutexName);
            if (h == IntPtr.Zero) return false;
            CloseHandle(h);
            return true;
        }

        #region pid 文件（主进程退出时按 pid 找到派蒙进程——跨"游戏重启"场景仍有效，Mutex 只能探测不能定位进程）

        private static string PidFilePath => System.IO.Path.Combine(Application.persistentDataPath, "pet.pid");

        /// <summary>派蒙进程启动时登记自己的 pid；进程被杀时文件残留无害（下次覆盖，读取侧有进程名校验）。</summary>
        public static void WritePidFile()
        {
            try
            {
                System.IO.File.WriteAllText(PidFilePath, System.Diagnostics.Process.GetCurrentProcess().Id.ToString());
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PetMode] 写 pet.pid 失败（退出连带关闭将失效）: {ex.Message}");
            }
        }

        /// <summary>
        /// 主进程退出钩子调用：按 pid 文件找到派蒙并结束（文件残留无害，下次覆盖）。
        /// </summary>
        public static void TryKillPet()
        {
#if !UNITY_EDITOR
            TryKillByPidFile(PidFilePath, "游戏退出");
#endif
        }

        /// <summary>按 pid 文件结束派蒙进程（生产 pet.pid / 编辑器 Temp/_editor_pet.pid 共用一份逻辑，
        /// 2026-08-27 抽取去重）。校验进程名防 PID 复用误杀；文件缺失/pid 无效/进程已退/名字不符=静默跳过。
        /// 动作描述仅用于日志（如"游戏退出"）。文件删除策略由调用方自理（生产残留无害，编辑器要删）。
        /// public：编辑器程序集（PetEditorAutoLauncher）也要调用，internal 跨程序集不可见。</summary>
        public static bool TryKillByPidFile(string pidFile, string 动作描述)
        {
            try
            {
                if (!System.IO.File.Exists(pidFile)) return false;
                if (!int.TryParse(System.IO.File.ReadAllText(pidFile).Trim(), out int pid)) return false;

                var p = System.Diagnostics.Process.GetProcessById(pid); // 不存在会抛 ArgumentException，接住即跳过
                if (p == null || p.ProcessName != "gic") return false;

                p.Kill();
                Debug.Log($"[PetMode] {动作描述}，已关闭派蒙 pid={pid}");
                return true;
            }
            catch (ArgumentException)
            {
                // 派蒙进程已不存在（正常：她可能已被用户双击关闭）
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PetMode] {动作描述}，关闭派蒙失败（她将独立存活）: {ex.Message}");
                return false;
            }
        }

        #endregion
    }
}
