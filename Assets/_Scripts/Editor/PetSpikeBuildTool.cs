using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace GIC.Editor
{
    /// <summary>
    /// 桌宠 spike 构建工具：构建 Windows 测试包（含 PaimonPet 场景），同 exe 支持 --pet-mode 双形态。
    /// 构建经 delayCall 调度且同步执行（会阻塞主线程数分钟），期间禁止走 Codely 桥——只能从 Editor.log 轮询 [PetSpikeBuild] 标记。
    /// </summary>
    public static class PetSpikeBuildTool
    {
        private const string 输出目录 = "Builds/PetSpike";
        private const string 输出路径 = 输出目录 + "/gic.exe";

        [MenuItem("Tools/桌宠/构建 Windows 桌宠测试包")]
        public static void Build()
        {
            EditorApplication.delayCall += BuildInternal;
        }

        /// <summary>同步直调入口（供 execute_csharp_script 调用）——delayCall 在脚本上下文空闲时不执行，菜单调度会被吞。
        /// 仅热缓存快速路径（<60s）可用：冷缓存构建 >330s 会阻塞桥超时并触发无上限自动重试（串行叠多场幂等构建）。</summary>
        public static void BuildImmediate()
        {
            BuildInternal();
        }

        /// <summary>异步调度入口（2026-08-26 终案，供 execute_csharp_script 调用）：毫秒级返回，构建经
        /// delayCall 在编辑器空闲帧执行——根治 BuildImmediate 同步阻塞桥 → 超时 → 自动重试 → 串行叠构建
        /// 的顽疾（2026-08-26 三次实证，用户被迫手动中断）。构建期间禁止任何桥调用（主线程忙 → TCP 断连
        /// ×10 报错）；判建成只信 Editor.log 的 [PetSpikeBuild] RESULT 行（PowerShell 后台轮询，基线计数+1）。
        /// 偶发 delayCall 被吞（60s 内无 START 标记）重调本方法一次即可——排队守卫防重复叠场。</summary>
        public static void ScheduleBuild()
        {
            if (_已排队) return;
            _已排队 = true;
            EditorApplication.delayCall += 队列构建;
        }

        private static bool _已排队;

        private static void 队列构建()
        {
            _已排队 = false;
            BuildInternal();
        }

        private static void BuildInternal()
        {
            // 编译竞态守卫：delayCall 排到本帧时若 Refresh 触发的脚本编译/导入尚未落地，
            // 直接 BuildPlayer 会报 "Error building Player because scripts are compiling"（errors=0 size=0 假失败）。
            // 持续推迟到编译落地再构建。
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                _已排队 = true; // 保持排队态：推迟期间 ScheduleBuild 再调直接吞（防叠场）
                EditorApplication.delayCall += 队列构建;
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
                    EditorApplication.delayCall += 队列构建;
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
