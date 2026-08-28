using System;
using System.Runtime.InteropServices;
using System.Text;

namespace GIC.Pet
{
    /// <summary>
    /// 桌宠 Win32 互操作集中声明（2026-08-27 从 PetWindowController/PetEdgeSitController 抽取去重）：
    /// 两控制器原本各自维护一套 user32/dwmapi 声明，POINT/RECT/MONITORINFO 与 GetWindowRect/IsIconic/
    /// GetWindowLong/MonitorFromPoint/GetMonitorInfoW 等完全重复。集中后各文件经 using static 引入，
    /// 调用点零改动。新增窗口级子系统（边缘探头等）一律从这里取声明，勿再复制。
    /// 声明本身全平台可编译；调用侧属 Windows 桌宠形态，平台守卫在调用方（PetProcessLauncher 同例）。
    /// </summary>
    internal static class PetWin32
    {
        // ───────────── 结构体 ─────────────

        [StructLayout(LayoutKind.Sequential)] internal struct POINT { public int X; public int Y; }

        /// <summary>窗口/客户区矩形（物理像素）。宽/高为求值属性，不占内存布局（EdgeSit 用）</summary>
        [StructLayout(LayoutKind.Sequential)] internal struct RECT
        {
            public int Left; public int Top; public int Right; public int Bottom;
            public int 宽 => Right - Left;
            public int 高 => Bottom - Top;
        }

        [StructLayout(LayoutKind.Sequential)] internal struct MARGINS { public int cxLeftWidth; public int cxRightWidth; public int cyTopHeight; public int cyBottomHeight; }
        [StructLayout(LayoutKind.Sequential)] internal struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public int dwFlags; }

        // ───────────── 委托 ─────────────

        internal delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);          // WH_MOUSE_LL 钩子回调
        internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);                   // EnumWindows 回调

        // ───────────── user32 ─────────────

        [DllImport("user32.dll")] internal static extern IntPtr GetActiveWindow();
        [DllImport("user32.dll")] internal static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] internal static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")] internal static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint crKey, byte bAlpha, uint dwFlags);
        [DllImport("user32.dll")] internal static extern bool GetCursorPos(out POINT lpPoint);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")] internal static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")] internal static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);
        [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr hWnd);
        [DllImport("user32.dll")] internal static extern bool SystemParametersInfo(uint uiAction, uint uiParam, out RECT pvParam, uint fWinIni);
        [DllImport("user32.dll")] internal static extern int GetSystemMetrics(int nIndex);
        [DllImport("user32.dll")] internal static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO lpmi);
        [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] internal static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")] internal static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
        [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd); // Z 序导航（GW_HWNDPREV=紧邻上方窗口）
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        // ───────────── dwmapi / kernel32 ─────────────

        [DllImport("dwmapi.dll")] internal static extern uint DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset);
        [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);
        [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);
        [DllImport("dwmapi.dll")] internal static extern int DwmFlush(); // 阻塞到下一次 DWM 合成完成（帧节拍整律：分层窗口 present 不阻塞、vsync 无效时的唯一对齐手段）
        [DllImport("kernel32.dll")] internal static extern IntPtr GetModuleHandle(string lpModuleName);

        // ───────────── 常量 ─────────────

        internal const int GWL_STYLE = -16;
        internal const int GWL_EXSTYLE = -20;
        internal const int WS_CAPTION = 0x00C00000;
        internal const int WS_THICKFRAME = 0x00040000;
        internal const int WS_SYSMENU = 0x00080000;
        internal const int WS_MINIMIZEBOX = 0x00020000;
        internal const int WS_MAXIMIZEBOX = 0x00010000;
        internal const int WS_EX_LAYERED = 0x00080000;
        internal const int WS_EX_TRANSPARENT = 0x00000020;
        internal const int WS_EX_TOOLWINDOW = 0x00000080;
        internal const uint LWA_COLORKEY = 0x00000001;
        internal const uint SWP_NOSIZE = 0x0001;
        internal const uint SWP_NOMOVE = 0x0002;
        internal const uint SWP_NOZORDER = 0x0004;
        internal const uint SWP_NOACTIVATE = 0x0010;
        internal const uint SWP_SHOWWINDOW = 0x0040;
        internal const uint SWP_FRAMECHANGED = 0x0020;
        internal const uint GW_HWNDPREV = 3; // GetWindow：Z 序中紧邻上方窗口（链顶返回 NULL）
        internal const uint SPI_GETWORKAREA = 0x0030;
        internal const int SM_XVIRTUALSCREEN = 76;
        internal const int SM_YVIRTUALSCREEN = 77;
        internal const int SM_CXVIRTUALSCREEN = 78;
        internal const int SM_CYVIRTUALSCREEN = 79;
        internal const uint MONITOR_DEFAULTTONEAREST = 2;
        internal const int SW_SHOWNOACTIVATE = 4;
        internal const int VK_LBUTTON = 0x01;
        internal const int WH_MOUSE_LL = 14;
        internal const int WM_MOUSEWHEEL = 0x020A;
        internal const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;   // DWM 可见帧（Win10/11 不含阴影边）
        internal const int DWMWA_CLOAKED = 14;                // 挂起/虚拟桌面隐身窗口
        internal static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    }
}
