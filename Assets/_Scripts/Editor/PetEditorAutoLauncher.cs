using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using GIC.Framework;

namespace GIC.Editor
{
    /// <summary>
    /// 编辑器联调辅助（docs/19 §5.8）：编辑器进入 Play 时自动拉起上次构建的桌宠进程
    /// （Builds/PetSpike/gic.exe --pet-mode），实现"编辑器里一起测试派蒙"。
    /// 退出 Play 时的去留跟随游戏设置 closePetOnExit（与生产 GameScene.OnApplicationQuit 同语义）：
    /// 开=连带关闭自己拉起的那只；关=独立存活（下轮 Play Mutex 探测到即跳过拉起）。
    /// 按 Temp pid 文件记账（域重载会清空静态字段），只回收自己拉起的；
    /// 手动从构建目录启动的派蒙不受影响（Mutex 探测到已存在则跳过拉起）。
    /// 桌宠本体停留在上次构建产物，改模型/动画后需重新构建（Tools/桌宠/构建 Windows 桌宠测试包）。
    /// </summary>
    [InitializeOnLoad]
    public static class PetEditorAutoLauncher
    {
        private const string MenuPath = "Tools/桌宠/Play 时自动拉起派蒙";
        private const string ToggleKey = "GIC.Pet.EditorAutoLaunch";
        private const string PidFile = "Temp/_editor_pet.pid";
        private const string PetArgs = "--pet-mode -screen-fullscreen 0 -screen-width 550 -screen-height 825";

        private static bool AutoLaunch
        {
            get => EditorPrefs.GetBool(ToggleKey, true);
            set => EditorPrefs.SetBool(ToggleKey, value);
        }

        static PetEditorAutoLauncher()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                TryLaunch();
            }
            else if (state == PlayModeStateChange.ExitingPlayMode)
            {
                // ExitingPlayMode 阶段 Play 域对象仍存活，可读运行中的 SaveManager
                HandlePlayExit();
            }
        }

        /// <summary>退出 Play：按设置 closePetOnExit 决定编辑器拉起的派蒙去留（与生产退出语义一致）。</summary>
        private static void HandlePlayExit()
        {
            bool close = ReadClosePetOnExit();
            if (!close)
            {
                GICLog.Info("[PetMode][Editor] 设置=退出不关闭派蒙，她将独立存活（下轮 Play 探测到即跳过拉起）");
                return;
            }
            TryKillEditorPet();
        }

        /// <summary>
        /// 读 closePetOnExit：优先读运行中容器（Boot 启动、设置改动即时生效）；
        /// Wargame 未起（直开非 Boot 场景 Play）时落盘读存档 JSON；均失败按默认值 true。
        /// </summary>
        private static bool ReadClosePetOnExit()
        {
            var save = GIC.Framework.Wargame.Instance?.Context?.Get<GIC.Framework.SaveManager>()?.CurrentSave;
            if (save != null) return save.closePetOnExit;

            try
            {
                // 复刻 SaveManager.SavePath 的目录规则（含 ParrelSync clone 独立子目录）
                string dir = Application.persistentDataPath;
                string cloneMarker = Path.Combine(Application.dataPath, "..", ".clone");
                if (File.Exists(cloneMarker)) dir = Path.Combine(dir, "clone");
                string json = File.ReadAllText(Path.Combine(dir, "gic_save.json"));
                return JsonUtility.FromJson<PetSaveProbe>(json).closePetOnExit;
            }
            catch
            {
                return true; // 无存档/读取失败：与 PlayerSaveData 默认值一致
            }
        }

        [System.Serializable]
        private class PetSaveProbe
        {
            public bool closePetOnExit = true;
        }

        private static void TryLaunch()
        {
            if (!AutoLaunch) return;

            // 与生产 PetProcessLauncher 相同的防重探测：桌面已有派蒙（含手动从构建目录启动的）则跳过
            if (GIC.Pet.PetSingleInstance.Probe())
            {
                GICLog.Info("[PetMode][Editor] 桌宠已在桌面（Mutex 探测到），跳过拉起");
                return;
            }

            string exe = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName, "Builds", "PetSpike", "gic.exe");
            if (!File.Exists(exe))
            {
                GICLog.Info("[PetMode][Editor] 未找到 Builds/PetSpike/gic.exe，本次 Play 不带派蒙（构建：Tools/桌宠/构建 Windows 桌宠测试包）");
                return;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = PetArgs,
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(exe),
                };
                var child = Process.Start(psi);
                File.WriteAllText(PidFile, child.Id.ToString());
                GICLog.Info($"[PetMode][Editor] 已拉起桌宠测试进程 pid={child.Id}（退出 Play 按设置 closePetOnExit 决定去留）");
                child.Dispose(); // 不持句柄：退出回收走 pid 文件
            }
            catch (System.Exception ex)
            {
                GICLog.Warn($"[PetMode][Editor] 拉起桌宠失败: {ex.Message}");
            }
        }

        private static void TryKillEditorPet()
        {
            try
            {
                if (!File.Exists(PidFile)) return; // 本次 Play 没拉起（或已回收）
                string text = File.ReadAllText(PidFile).Trim();
                File.Delete(PidFile);
                if (!int.TryParse(text, out int pid)) return;

                var p = Process.GetProcessById(pid); // 进程已不存在会抛 ArgumentException，接住即跳过
                if (p == null || p.ProcessName != "gic") return; // 防 PID 复用误杀（与 TryKillPet 同校验）

                p.Kill();
                GICLog.Info($"[PetMode][Editor] 退出 Play，关闭编辑器拉起的派蒙 pid={pid}");
            }
            catch (System.ArgumentException)
            {
                // 派蒙已被双击关闭/自行退出
            }
            catch (System.Exception ex)
            {
                GICLog.Warn($"[PetMode][Editor] 关闭派蒙失败（可双击派蒙手动关闭）: {ex.Message}");
            }
        }

        // ==================== 菜单开关 ====================

        [MenuItem(MenuPath)]
        private static void ToggleAutoLaunch()
        {
            AutoLaunch = !AutoLaunch;
            GICLog.Info($"[PetMode][Editor] Play 自动拉起派蒙: {(AutoLaunch ? "开" : "关")}");
        }

        [MenuItem(MenuPath, true)]
        private static bool ToggleAutoLaunchValidate()
        {
            Menu.SetChecked(MenuPath, AutoLaunch);
            return true;
        }
    }
}
