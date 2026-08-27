using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace GIC.Editor
{
    /// <summary>
    /// 桌宠 spike 构建工具：构建 Windows 测试包（含 PaimonPet 场景），同 exe 支持 --pet-mode 双形态。
    /// 构建经 EditorApplication.update 调度且同步执行（会阻塞主线程数分钟），期间禁止走 Codely 桥——只能从 Editor.log 轮询 [PetSpikeBuild] 标记。
    /// 调度勿用 delayCall（2026-08-27 实证根因）：delayCall 挂在 inspector/repaint 循环——编辑器后台空闲时
    /// 不 repaint，delayCall 永不 flush，构建要等用户点开编辑器才弹窗（数分钟延迟全源于此）；
    /// EditorApplication.update 挂 editor tick，空闲节流下照常执行（探针实测后台 30s 内触发）。
    /// </summary>
    public static class PetSpikeBuildTool
    {
        private const string 输出目录 = "Builds/PetSpike";
        private const string 输出路径 = 输出目录 + "/gic.exe";

        [MenuItem("Tools/桌宠/构建 Windows 桌宠测试包")]
        public static void Build()
        {
            经update调度(BuildInternal);
        }

        /// <summary>同步直调入口（供 execute_csharp_script 调用）——调度器在编辑器空闲时不执行，菜单调度会被吞。
        /// 仅热缓存快速路径（<60s）可用：冷缓存构建 >330s 会阻塞桥超时并触发无上限自动重试（串行叠多场幂等构建）。</summary>
        public static void BuildImmediate()
        {
            BuildInternal();
        }

        /// <summary>异步调度入口（2026-08-26 终案，供 execute_csharp_script 调用）：毫秒级返回，构建经
        /// EditorApplication.update 在编辑器下个 tick 执行——根治 BuildImmediate 同步阻塞桥 → 超时 → 自动重试
        /// → 串行叠构建的顽疾（2026-08-26 三次实证，用户被迫手动中断）；2026-08-27 delayCall→update：
        /// 后台空闲编辑器 delayCall 永不执行（用户被迫手动点开编辑器弹窗才构建），update 空闲照常跑。
        /// 构建期间禁止任何桥调用（主线程忙 → TCP 断连×10 报错）；判建成只信 Editor.log 的
        /// [PetSpikeBuild] RESULT 行（PowerShell 后台轮询，基线计数+1）。排队守卫防重复叠场。</summary>
        public static void ScheduleBuild()
        {
            if (_已排队) return;
            _已排队 = true;
            经update调度(队列构建);
        }

        private static bool _已排队;

        /// <summary>update 一次性调度（替代 delayCall 的标准姿势）：注册 update 回调，首个 tick 执行后自注销。
        /// 空闲节流下编辑器 tick 变慢但不断（后台实测 30s 内必触发）；delayCall 则要等 repaint/交互。</summary>
        private static void 经update调度(System.Action 动作)
        {
            EditorApplication.CallbackFunction cb = null;
            cb = () =>
            {
                EditorApplication.update -= cb;
                动作();
            };
            EditorApplication.update += cb;
        }

        private static void 队列构建()
        {
            _已排队 = false;
            BuildInternal();
        }

        private static void BuildInternal()
        {
            // 编译竞态守卫：update 排到本帧时若 Refresh 触发的脚本编译/导入尚未落地，
            // 直接 BuildPlayer 会报 "Error building Player because scripts are compiling"（errors=0 size=0 假失败）。
            // 持续推迟到编译落地再构建。
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                _已排队 = true; // 保持排队态：推迟期间 ScheduleBuild 再调直接吞（防叠场）
                经update调度(队列构建);
                return;
            }

            Debug.Log("[PetSpikeBuild] START");
            try
            {
                // 强制刷新资产库，防止 player 构建吃到不含新脚本的陈旧输入（曾致 GIC.Pet CS0234）
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

                // Refresh 可能又排了一轮编译；再次守卫，确保 BuildPlayer 在编译真落地后才调用
                if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                {
                    _已排队 = true; // 同上：推迟期间保持排队态
                    经update调度(队列构建);
                    return;
                }

                string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
                var options = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = 输出路径,
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.Development,
                };
                BuildReport report = BuildPipeline.BuildPlayer(options);
                bool ok = report.summary.result == BuildResult.Succeeded && report.summary.totalErrors == 0;
                Debug.Log($"[PetSpikeBuild] RESULT {(ok ? "OK" : "FAIL")} errors={report.summary.totalErrors} size={report.summary.totalSize} path={输出路径}");
            }
            catch (System.Exception ex)
            {
                Debug.Log($"[PetSpikeBuild] RESULT FAIL exception={ex.Message}");
            }
        }
    }
}

