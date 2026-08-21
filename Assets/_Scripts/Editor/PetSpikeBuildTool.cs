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

        private static void BuildInternal()
        {
            Debug.Log("[PetSpikeBuild] START");
            try
            {
                // 强制刷新资产库，防止 player 构建吃到不含新脚本的陈旧输入（曾致 GIC.Pet CS0234）
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

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
