using System;
using System.IO;
using UnityEngine;

namespace GIC.Framework
{
    /// <summary>
    /// 主游戏窗口状态记录（2026-09-13 快捷消息"启动游戏"）：主进程周期心跳+窗口矩形写
    /// {persistentDataPath}/game_window.json——两个用途：
    /// ①桌宠进程读心跳判"主游戏在不在运行"（start_game 前置检查防双开——两个游戏进程会双消费
    ///   pet_intent IPC 造成指令重复执行）；
    /// ②主游戏启动时把窗口摆回上次窗口化的位置（用户拍板"自动记录最后一次主游戏所在位置"）。
    /// 心跳新鲜度 15s（写间隔 5s，容两个周期丢失）；进程崩溃=心跳停更自然判死，无清理负担。
    /// 编辑器只写 pid/时间戳（矩形恒 windowed=false——防编辑器窗口位置污染真实恢复；但心跳照写，
    /// 编辑器 Play 也算"游戏在运行"，与 IPC 编辑器可消费的语义一致）。
    /// 桌宠进程不写（GameScene 进宠物分支前早退）；Win32 仅 Windows 桌面，非 Windows=空实现。
    /// </summary>
    public static class GameWindowState
    {
        [Serializable]
        class WindowState
        {
            public int pid;
            public double t;        // Unix 秒（心跳时间戳）
            public bool windowed;   // 记录时是否窗口化（false=全屏/编辑器，恢复跳过）
            public int x, y, w, h;  // 窗口左上角屏幕坐标+尺寸（物理像素，Win32 坐标系 y 向下）
        }

        static string SavePath => Path.Combine(Application.persistentDataPath, "game_window.json");
        static float _lastWriteAt = -999f;
        const float HeartbeatInterval = 5f;
        const float AliveWindowSec = 15f;

        // ==================== 写侧（主进程） ====================

        /// <summary>心跳帧（GameScene.Update 每帧调，内部按 5s 节流；全屏也照写——窗口化标志位才是恢复判据）</summary>
        public static void HeartbeatFrame()
        {
            if (Time.realtimeSinceStartup - _lastWriteAt < HeartbeatInterval) return;
            _lastWriteAt = Time.realtimeSinceStartup;
            WriteNow();
        }

        /// <summary>立即写一次（周期心跳/退出终写共用）</summary>
        public static void WriteNow()
        {
            try
            {
                var s = new WindowState
                {
                    pid = System.Diagnostics.Process.GetCurrentProcess().Id,
                    t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0,
                };
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
                if (!Screen.fullScreen && TryGetOwnWindow(out IntPtr hwnd) && GetWindowRect(hwnd, out RECT r))
                {
                    s.windowed = true;
                    s.x = r.Left;
                    s.y = r.Top;
                    s.w = r.Right - r.Left;
                    s.h = r.Bottom - r.Top;
                }
#endif
                File.WriteAllText(SavePath, JsonUtility.ToJson(s));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[GameWindow] 状态写入失败：{e.Message}");
            }
        }

        // ==================== 读侧（桌宠进程） ====================

        /// <summary>主游戏是否在运行（心跳 15s 内=活着；文件缺失/损坏/时间戳过期=没在运行）</summary>
        public static bool IsGameAlive()
        {
            try
            {
                if (!File.Exists(SavePath)) return false;
                var s = JsonUtility.FromJson<WindowState>(File.ReadAllText(SavePath));
                if (s == null || s.t <= 0) return false;
                return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0 - s.t <= AliveWindowSec;
            }
            catch { return false; }
        }

        // ==================== 启动恢复（主进程，存档显示设置应用后） ====================

        /// <summary>把窗口摆回上次窗口化的记录位（上次全屏/本次全屏/编辑器=跳过）。
        /// 由 GameScene 在 SettingsApplier.ApplyFromSave 之后延迟 0.2s 调用——SetResolution 落地
        /// 前 SetWindowPos 可能被分辨率切换覆盖。</summary>
        public static void RestoreWindowPosition()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            try
            {
                if (!File.Exists(SavePath)) return;
                var s = JsonUtility.FromJson<WindowState>(File.ReadAllText(SavePath));
                if (s == null || !s.windowed || s.w <= 0 || s.h <= 0) return; // 上次不是窗口化=不恢复
                if (Screen.fullScreen) return;                               // 本次全屏=位置无意义
                if (!TryGetOwnWindow(out IntPtr hwnd)) return;
                SetWindowPos(hwnd, IntPtr.Zero, s.x, s.y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOZORDER);
                Debug.Log($"[GameWindow] 窗口位置恢复到 ({s.x},{s.y}) {s.w}x{s.h}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[GameWindow] 位置恢复失败：{e.Message}");
            }
#endif
        }

        // ==================== Win32（本类自包含声明：编辑器不编译此段，勿在此调用于编辑器路径） ====================

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr hwnd);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

        const uint SWP_NOSIZE = 0x0001, SWP_NOACTIVATE = 0x0010, SWP_NOZORDER = 0x0004;

        /// <summary>找本进程的主窗口（枚举顶层窗：pid 匹配+可见+尺寸够大——Unity 播放器主窗唯一，
        /// 过滤托盘/工具小窗）。命中值经局部变量带出——匿名委托不能捕获 out 参数（CS1628，
        /// 本块 #if !UNITY_EDITOR 编辑器不编译、构建期才暴露，2026-09-13 实证）</summary>
        static bool TryGetOwnWindow(out IntPtr hwnd)
        {
            IntPtr found = IntPtr.Zero;
            uint ownPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            EnumWindows(delegate (IntPtr h, IntPtr l)
            {
                GetWindowThreadProcessId(h, out uint pid);
                if (pid != ownPid || !IsWindowVisible(h)) return true;
                if (!GetWindowRect(h, out RECT r)) return true;
                if (r.Right - r.Left < 200 || r.Bottom - r.Top < 200) return true; // 跳过小工具窗
                found = h;
                return false; // 命中主窗口，停止枚举
            }, IntPtr.Zero);
            hwnd = found;
            return found != IntPtr.Zero;
        }
#endif
    }
}
