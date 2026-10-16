#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using GIC.Framework;

namespace GIC.Editor
{
    /// <summary>
    /// 构建看门狗——「Detecting Android SDK 停摆」自愈（2026-10-15/17 三例实证的 dl.google.com CDN 间歇坏连接病）。
    ///
    /// 机制（构建期间主线程被 BuildPlayer 占死，唯一能自救的就是本后台线程）：
    /// 1. 构建开始时快照现存 java 进程为基线；基线之后新出现的第一个 java = SDK 检测步的 sdkmanager
    ///    （Android 后处理里 SDK 检测先于 Gradle；IL2CPP 是原生进程不产 java，构建早期无其它 java）。
    /// 2. 监控该 java 的 CPU：存活 ≥90s 且近 60s CPU 增长 &lt;0.5s = 冻结（健康 sdkmanager --list 实测 ≤45s 完成）
    ///    → 击杀并置 KilledThisAttempt。
    /// 3. 击杀后引擎 AndroidPostProcessor 会弹原生「Build failure」模态框并阻塞 BuildPlayer 返回——
    ///    看门狗 P/Invoke 找到该弹窗自动点掉，让 BuildPlayer 正常返回失败、上层自动重试
    ///    （QuickAPKBuilder.BuildWithAutoRetry）。sdkmanager 自己正常退出则本尝试停止监控
    ///    （保护 Gradle：依赖下载期 java 合法低 CPU，不可误杀）。
    ///
    /// 线程纪律：状态字段全部 volatile；基线集合与样本列表用「整体换引用」防读写竞态
    /// （主线程只做生命周期 Begin/EndAttempt/Stop 与引用替换，后台线程每步读快照自纠）。
    /// 无域重载防僵尸检查：看门狗只在 BuildWithAutoRetry 的 try/finally 内存活（构建期间主线程被占、
    /// 域重载无法插入），Stop() 由 finally 兜底必达；线程 IsBackground 随编辑器进程消亡。
    /// </summary>
    public static class QuickBuildWatchdog
    {
        // ==================== 调参常量 ====================
        private const int 轮询间隔毫秒 = 1500;
        private const double 最小冻结年龄秒 = 90.0;      // 健康 sdkmanager --list 实测 ≤45s；90s 后近零 CPU 才判冻结，防误杀慢而健康的运行
        private const double CPU观察窗秒 = 60.0;
        private const double 窗内CPU增长上限秒 = 0.5;
        private const double 弹窗等待上限秒 = 120.0;
        private const string 引擎失败弹窗标题 = "Build failure";   // AndroidPostProcessor catch 块原生弹窗（2026-10-17 实证为英文标题；勿加「构建失败」——那是 QuickAPKBuilder 自己的结果框，须留给用户看）

        // ==================== 状态（volatile / 换引用） ====================
        private static volatile bool _active;
        private static volatile bool _killed;
        private static volatile bool _sdkPassed;
        private static volatile bool _dismissing;
        private static DateTime _dismissStart;
        private static Thread _thread;
        private static volatile HashSet<int> _baselinePids = new HashSet<int>();
        private static volatile List<(DateTime t, double cpu)> _samples = new List<(DateTime t, double cpu)>();
        private static int _targetPid;
        private static DateTime _targetFirstSeen;

        /// <summary>本尝试内看门狗是否击杀过冻结的 sdkmanager（上层据此决定是否自动重试）</summary>
        public static bool KilledThisAttempt => _killed;

        // ==================== 生命周期（主线程持有） ====================

        /// <summary>开始监控（整个构建会话，含全部重试尝试）。先 Begin 再起构建——新 sdkmanager 生成于基线之后才被识别。</summary>
        public static void Begin()
        {
            _active = true;
            ResetAttemptState();
            if (_thread == null || !_thread.IsAlive)
            {
                _thread = new Thread(WatchLoop) { IsBackground = true, Name = "QuickBuildWatchdog" };
                _thread.Start();
            }
        }

        /// <summary>单次尝试结束（自动重试前调）：重置击杀旗标与 java 基线，看门狗继续盯下一次尝试。
        /// 下一次的 sdkmanager 只会随下一次 BuildPlayer 生成，先于它重快照基线即天然排除旧进程。</summary>
        public static void EndAttempt() => ResetAttemptState();

        /// <summary>结束监控（所有尝试完成后调；Join 兜底回收线程）</summary>
        public static void Stop()
        {
            _active = false;
            _thread?.Join(3000);
            _thread = null;
        }

        private static void ResetAttemptState()
        {
            _killed = false;
            _sdkPassed = false;
            _dismissing = false;
            _targetPid = 0;
            _samples = new List<(DateTime t, double cpu)>();   // 整体换引用——后台线程持有旧列表读快照，无并发修改
            _baselinePids = SnapshotJavaPids();
        }

        private static HashSet<int> SnapshotJavaPids()
        {
            var set = new HashSet<int>();
            foreach (var p in SafeGetJavas()) set.Add(p.Id);
            return set;
        }

        private static List<Process> SafeGetJavas()
        {
            try { return Process.GetProcessesByName("java").ToList(); }
            catch { return new List<Process>(); }
        }

        // ==================== 后台监控线程 ====================

        private static void WatchLoop()
        {
            try
            {
                while (_active)
                {
                    Thread.Sleep(轮询间隔毫秒);
                    if (!_active) return;
                    if (_dismissing) { DismissStep(); continue; }
                    if (_sdkPassed) continue;
                    MonitorStep();
                }
            }
            catch (Exception ex)
            {
                GICLog.Error($"[QuickBuild] 看门狗线程异常退出: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void MonitorStep()
        {
            var javas = SafeGetJavas();

            // —— 已有目标：采样判冻结 ——
            if (_targetPid != 0)
            {
                var target = javas.FirstOrDefault(p => p.Id == _targetPid);
                if (target == null)
                {
                    // 目标已退出：非看门狗击杀 = SDK 检测正常通过，本尝试停止监控（防误杀 Gradle）
                    if (!_killed)
                    {
                        var survived = (DateTime.Now - _targetFirstSeen).TotalSeconds;
                        GICLog.Info($"[QuickBuild] 看门狗: SDK 检测 java 正常结束（存活 {survived:F0}s）——本尝试停止 java 监控");
                        _sdkPassed = true;
                    }
                    _targetPid = 0;
                    return;
                }

                double cpu;
                try { cpu = target.TotalProcessorTime.TotalSeconds; }
                catch { return; }   // 采样失败（进程将退出等）：下轮再看
                var samples = _samples;
                samples.Add((DateTime.Now, cpu));
                TrimSamples(samples, CPU观察窗秒 + 15);

                var age = (DateTime.Now - _targetFirstSeen).TotalSeconds;
                var growth = TrailingGrowth(samples, CPU观察窗秒);
                if (FreezeVerdict(age, growth))
                {
                    GICLog.Warn($"[QuickBuild] 看门狗: sdkmanager 停摆判定（PID {_targetPid}，存活 {age:F0}s，近{CPU观察窗秒:F0}s CPU 增长 {growth:F2}s）→ 击杀；等引擎弹窗由看门狗点掉，上层将自动重试");
                    try { target.Kill(); _killed = true; }
                    catch (Exception ex) { GICLog.Error($"[QuickBuild] 看门狗: 击杀 PID {_targetPid} 失败: {ex.Message}"); return; }
                    _dismissing = true;
                    _dismissStart = DateTime.Now;
                }
                return;
            }

            // —— 无目标：基线外新 java 的最早者 = sdkmanager ——
            var baseline = _baselinePids;
            Process candidate = null;
            foreach (var p in javas)
            {
                if (baseline.Contains(p.Id)) continue;
                if (!LooksLikeAndroidJdk(p)) continue;
                try { if (candidate == null || p.StartTime < candidate.StartTime) candidate = p; }
                catch { }
            }
            if (candidate == null) return;
            _targetPid = candidate.Id;
            try { _targetFirstSeen = candidate.StartTime; } catch { _targetFirstSeen = DateTime.Now; }
            GICLog.Info($"[QuickBuild] 看门狗: 开始监控 SDK 检测 java（PID {_targetPid}）——冻结判据=存活≥{最小冻结年龄秒:F0}s 且近{CPU观察窗秒:F0}s CPU 增长<{窗内CPU增长上限秒}s");
        }

        /// <summary>java 进程是否来自本机 AndroidPlayer 的 OpenJDK。
        /// 主模块可读时必须含 AndroidPlayer（排除用户无关 java）；读不到时按进程名放行
        /// （同用户同位数常态可读；误杀还需叠加冻结判据，风险可忽略）。</summary>
        private static bool LooksLikeAndroidJdk(Process p)
        {
            try
            {
                var path = p.MainModule?.FileName;
                if (string.IsNullOrEmpty(path)) return true;
                return path.IndexOf("AndroidPlayer", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return true; }
        }

        private static void TrimSamples(List<(DateTime t, double cpu)> samples, double keepSeconds)
        {
            var cutoff = DateTime.Now.AddSeconds(-keepSeconds);
            var remove = 0;
            while (remove < samples.Count && samples[remove].t < cutoff) remove++;
            if (remove > 0) samples.RemoveRange(0, remove);
        }

        /// <summary>近 windowSeconds 的 CPU 增长量：锚点取「观察窗起点处或更早的最后一个样本」，
        /// 样本不足两枚时返回 MaxSize（保守不判冻结）。</summary>
        private static double TrailingGrowth(List<(DateTime t, double cpu)> samples, double windowSeconds)
        {
            if (samples.Count < 2) return double.MaxValue;
            var cutoff = DateTime.Now.AddSeconds(-windowSeconds);
            var anchor = samples[0];
            foreach (var s in samples)
            {
                if (s.t <= cutoff) anchor = s;
                else break;
            }
            return samples[samples.Count - 1].cpu - anchor.cpu;
        }

        /// <summary>冻结判定（纯函数，供编辑器侧反射断言自测）：年龄达标且观察窗内 CPU 几乎零增长</summary>
        internal static bool FreezeVerdict(double ageSeconds, double trailingCpuGrowthSeconds)
            => ageSeconds >= 最小冻结年龄秒 && trailingCpuGrowthSeconds < 窗内CPU增长上限秒;

        // ==================== 弹窗自动关闭 ====================

        private static void DismissStep()
        {
            if (TryDismissTitled(引擎失败弹窗标题))
            {
                GICLog.Info("[QuickBuild] 看门狗: 已自动关闭引擎「Build failure」弹窗——BuildPlayer 即将返回失败，上层自动重试");
                _dismissing = false;
                return;
            }
            if ((DateTime.Now - _dismissStart).TotalSeconds > 弹窗等待上限秒)
            {
                GICLog.Warn($"[QuickBuild] 看门狗: {弹窗等待上限秒:F0}s 内未见「{引擎失败弹窗标题}」弹窗——若稍后弹出请手动点击（自动重试链不受影响）");
                _dismissing = false;
            }
        }

        /// <summary>单次尝试关闭指定标题的模态框（Win32 路线：先 WM_CLOSE，不吃再找 Ok 按钮 BM_CLICK）。
        /// 返回 true=找到并已关闭；未找到返回 false（调用方轮询至超时）。
        /// public 供编辑器侧自测脚本直接调用。仅可在后台线程调（内含 Thread.Sleep，且模态框属主线程消息泵）。</summary>
        public static bool TryDismissTitled(string title)
        {
#if UNITY_EDITOR_WIN
            var hwnd = FindWindow(null, title);
            if (hwnd == IntPtr.Zero) return false;
            PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            Thread.Sleep(1200);
            if (FindWindow(null, title) == IntPtr.Zero) return true;
            ClickOkButtonOf(hwnd);
            Thread.Sleep(600);
            return FindWindow(null, title) == IntPtr.Zero;
#else
            return false;
#endif
        }

#if UNITY_EDITOR_WIN
        private static void ClickOkButtonOf(IntPtr dialog)
        {
            EnumChildWindows(dialog, (child, _) =>
            {
                var cls = new StringBuilder(64);
                GetClassName(child, cls, 64);
                if (!string.Equals(cls.ToString(), "Button", StringComparison.OrdinalIgnoreCase)) return true;
                var text = new StringBuilder(64);
                GetWindowText(child, text, 64);
                var label = text.ToString().Trim();
                if (label == "Ok" || label == "OK" || label == "确定")
                {
                    PostMessage(child, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
                    return false;
                }
                return true;
            }, IntPtr.Zero);
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr hWnd, EnumChildProc cb, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);
        private delegate bool EnumChildProc(IntPtr hWnd, IntPtr lParam);
        private const uint WM_CLOSE = 0x0010;
        private const uint BM_CLICK = 0x00F5;
#endif
    }
}
#endif
