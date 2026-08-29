using UnityEngine;

namespace GIC.Pet
{
    /// <summary>
    /// 桌宠模型网格查询小工具（2026-08-27 抽取去重）：眨眼/情绪两控制器各自复制过
    /// "取 morph 最多的蒙皮渲染器"循环——多 SMR 模型下 Face 网格持有全部 BlendShape，
    /// 按 morph 数取最大者即取到脸网格；MMD 单 SMR 时代亦兼容。
    /// </summary>
    internal static class PetMeshQuery
    {
        /// <summary>取子树下 BlendShape 数最多的蒙皮渲染器（morph 全在 Face 网格上）。
        /// 无 SMR 或网格全空时返回 null。</summary>
        public static SkinnedMeshRenderer GetRichestMorphRenderer(Transform root)
        {
            SkinnedMeshRenderer best = null;
            foreach (var s in root.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (s.sharedMesh == null) continue;
                if (best == null || s.sharedMesh.blendShapeCount > best.sharedMesh.blendShapeCount) best = s;
            }
            return best;
        }
    }
}
