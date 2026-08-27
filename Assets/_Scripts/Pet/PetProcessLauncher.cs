using System;
using System.Diagnostics;
using UnityEngine;

namespace GIC.Pet
{
    /// <summary>
    /// 主进程侧：游戏启动时拉起桌宠子进程（同 exe --pet-mode，docs/19 §5.2）。
    /// 生命周期设计（2026-08-21 拍板：拉起但不绑命）——派蒙独立存活：
    /// ①拉起前 Mutex 探测，桌面已有派蒙则跳过（防重复）；
    /// ②不绑 Job Object、不持有子进程句柄——游戏退出/崩溃，派蒙照常留在桌面。
    /// 桌宠进程自身不会执行本类（GameScene 提前走了宠物分支）；编辑器内为 no-op。
    /// 桌面桌宠仅限 Windows——Android 无独立进程形态（游戏内悬浮走别的路径，docs/19 §6），
    /// 平台守卫避免安卓启动时 kernel32 P/Invoke 抛 DllNotFoundException。
    /// </summary>
    public static class PetProcessLauncher
    {
        public static void Launch()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (PetMode.Enabled) return; // 双保险：宠物进程不再拉新进程

            try
            {
                if (PetSingleInstance.Probe())
                {
                    UnityEngine.Debug.Log("[PetMode] 桌宠已在桌面，跳过拉起");
                    return;
                }

                // 注意：UnityEngine.Application 无 ExecutablePath；Process.MainModule 拿当前 exe 全路径
                string exe = Process.GetCurrentProcess().MainModule.FileName;
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = PetMode.LaunchArgs,
                    UseShellExecute = false,
                    WorkingDirectory = System.IO.Path.GetDirectoryName(exe),
                };
                var child = Process.Start(psi);
                UnityEngine.Debug.Log($"[PetMode] 桌宠子进程已启动 pid={child.Id}（独立存活，不随主进程退出）");
                child.Dispose(); // 立即放掉句柄：不做任何父子绑定
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[PetMode] 桌宠子进程启动失败: {ex.Message}");
            }
#endif
        }
    }
}
