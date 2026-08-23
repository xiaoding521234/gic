using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace GIC.Editor.Retarget
{
    /// <summary>
    /// 表情曲线清理（docs/19 §2.6，v4 定案）：派蒙表情全部走运行时层（行业通行 face/body 分层），
    /// **不烘焙进任何身体动画 clip**——本工具职责仅为清空 _MMD.anim 中残留的 blendShape 曲线
    /// （v1-v3 曾注入过眨眼/怒り曲线，迁移架构后须清除干净，否则 clip 曲线会覆盖运行时组件的权重）。
    /// 三层架构：身体=Animation clip（方案B管线）；情绪=PetEmotionController（指令式，怒り等）；
    /// 微动作=PetBlinkController（随机 timer 眨眼）。morph 独占约定：组件管理的 morph 不得出现在
    /// 任何 clip 曲线中（Animation 只覆盖 clip 中存在的曲线）。
    /// 幂等：可重复执行。
    /// </summary>
    public static class PaimonFacePipeline
    {
        const string MMD_DIR = "Assets/Art/PaimonPet/Animations/MMD";

        [MenuItem("Tools/桌宠/表情曲线清理(迁移运行时架构)")]
        public static void Run()
        {
            var log = new StringBuilder();
            try
            {
                int cleared = 0;
                foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { MMD_DIR }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                    if (clip == null) continue;
                    var bs = AnimationUtility.GetCurveBindings(clip)
                        .Where(b => b.propertyName.StartsWith("blendShape.")).ToList();
                    foreach (var b in bs)
                    {
                        AnimationUtility.SetEditorCurve(clip, b, null);
                        log.AppendLine($"  清除 {clip.name}: {b.propertyName}");
                        cleared++;
                    }
                }
                AssetDatabase.SaveAssets();
                Debug.Log($"[Face] 完成：清除 {cleared} 条表情曲线（表情已全部迁移运行时层）\n{log}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[Face] 失败: " + ex + "\n" + log);
            }
        }
    }
}
