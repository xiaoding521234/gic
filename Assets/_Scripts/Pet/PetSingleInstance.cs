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
    }
}
