using UnityEngine;

namespace GIC.Editor.Retarget
{
    /// <summary>
    /// 方案B重定向纯数学核心（无 Unity 对象依赖，可自检）。
    /// 全部为跨骨架重定向的标准量：世界旋转增量（骨轴约定无关）、共轭变换、
    /// 带缩放矩阵的旋转提取、骨段方向对齐。公式语义见 PaimonRetargetPipeline 头注。
    /// </summary>
    public static class PetRetargetMath
    {
        /// <summary>从（可带均匀缩放的）矩阵提取旋转：列向量归一后 LookRotation</summary>
        public static Quaternion ExtractRot(Matrix4x4 m)
        {
            var z = (Vector3)m.GetColumn(2);
            var y = (Vector3)m.GetColumn(1);
            var zf = z / Mathf.Max(z.magnitude, 1e-8f);
            var yf = y / Mathf.Max(y.magnitude, 1e-8f);
            var yO = yf - Vector3.Project(yf, zf);
            if (yO.sqrMagnitude < 1e-10f)
            {
                yO = Vector3.Cross(zf, Vector3.up);
                if (yO.sqrMagnitude < 1e-6f) yO = Vector3.Cross(zf, Vector3.right);
            }
            return Quaternion.LookRotation(zf, yO.normalized);
        }

        /// <summary>世界旋转增量：B 相对 A 的物理旋转变化（qB·qA⁻¹，约定无关）</summary>
        public static Quaternion Delta(Quaternion qA, Quaternion qB) => qB * Quaternion.Inverse(qA);

        /// <summary>基准旋转差共轭：把 R 空间的旋转差搬到 MMD 空间（R⁻¹·d·R）</summary>
        public static Quaternion Conjugate(Quaternion r, Quaternion d) => Quaternion.Inverse(r) * d * r;

        /// <summary>v8 代表骨公式：qWant = C·Δ(Qg0,Qgf)·C⁻¹？——注意此处 R⁻¹·Δ·R 即 Conjugate(R, Δ)</summary>
        public static Quaternion RepWorld(Quaternion r, Quaternion giF, Quaternion giF0, Quaternion qPose)
            => Conjugate(r, Delta(giF0, giF)) * qPose;

        /// <summary>位置向量共轭方向：R⁻¹·d</summary>
        public static Vector3 ConjVec(Quaternion r, Vector3 d) => Quaternion.Inverse(r) * d;
    }
}
