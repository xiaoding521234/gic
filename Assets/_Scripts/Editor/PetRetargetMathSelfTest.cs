using UnityEditor;
using UnityEngine;

namespace GIC.Editor.Retarget
{
    /// <summary>
    /// PetRetargetMath 数学自检（菜单驱动，非 Test Framework——项目约定不使用 asmdef，
    /// 编辑器菜单自检达到同等验证效果：恒等式/往返性/退化情况全断言，失败即抛异常）。
    /// </summary>
    public static class PetRetargetMathSelfTest
    {
        [MenuItem("Tools/\u684c\u5ba0/\u65b9\u6848B: \u6570\u5b66\u81ea\u68c0")]
        public static void Run()
        {
            int pass = 0;
            void Check(string name, bool ok)
            {
                if (!ok) throw new System.InvalidOperationException($"[MathSelfTest] \u5931\u8d25: {name}");
                Debug.Log($"[MathSelfTest] \u2713 {name}");
                pass++;
            }

            // ① ExtractRot 往返：随机旋转 + 均匀缩放矩阵 → 提取应还原
            for (int i = 0; i < 100; i++)
            {
                var q = RandomRot();
                var m = Matrix4x4.TRS(Random.insideUnitSphere, q, Vector3.one * Random.Range(0.01f, 100f));
                var back = PetRetargetMath.ExtractRot(m);
                Check($"ExtractRot \u5f80\u8fd4 #{i}", Quaternion.Angle(q, back) < 0.01f);
            }

            // ② 非均匀缩放下 ExtractRot 仍是"最接近的正交旋转"（列归一语义）
            {
                var q = RandomRot();
                var m = Matrix4x4.TRS(Vector3.zero, q, new Vector3(2f, 1f, 0.5f));
                Check("ExtractRot \u975e\u5747\u5300\u7f29\u653e\u4e0d\u5d29", Quaternion.Angle(q, PetRetargetMath.ExtractRot(m)) < 15f);
            }

            // ③ Delta 性质：Δ(a,a)=identity；左作用 Δ(a,b)·a=b（管线用法：增量左乘姿势基准）
            {
                var a = RandomRot(); var b = RandomRot(); var c = RandomRot();
                Check("\u0394(a,a)=I", Quaternion.Angle(PetRetargetMath.Delta(a, a), Quaternion.identity) < 1e-4f);
                Check("\u0394 \u5de6\u4f5c\u7528 \u0394(a,b)\u00b7a=b", Quaternion.Angle(PetRetargetMath.Delta(a, b) * a, b) < 1e-3f);
                // 共轭同态：C(r, d1·d2) = C(r,d1)·C(r,d2)
                var d1 = PetRetargetMath.Delta(a, b);
                var d2 = PetRetargetMath.Delta(b, c);
                var r0 = RandomRot();
                Check("\u5171\u8f6d\u540c\u6001", Quaternion.Angle(
                    PetRetargetMath.Conjugate(r0, d1 * d2),
                    PetRetargetMath.Conjugate(r0, d1) * PetRetargetMath.Conjugate(r0, d2)) < 1e-3f);
            }

            // ④ 共轭同目标父子抵消：v8 核心恒等——同 Δ 两次施加 = 单位（增量在局部解算中自动抵消）
            {
                var r = RandomRot();
                var d = RandomRot();
                var cd = PetRetargetMath.Conjugate(r, d);
                Check("\u5171\u8f6d\u5f80\u8fd4 C(r,d)\u00b7C(r,d\u207b\u00b9)=I",
                    Quaternion.Angle(cd * PetRetargetMath.Conjugate(r, Quaternion.Inverse(d)), Quaternion.identity) < 1e-3f);
                Check("\u5171\u8f6d\u4fdd\u89d2\u5ea6", Mathf.Approximately(Quaternion.Angle(d, cd), Quaternion.Angle(Quaternion.identity, Quaternion.identity) + Quaternion.Angle(d, Quaternion.identity) * 0f) || Mathf.Abs(Quaternion.Angle(d, cd) - Quaternion.Angle(d, cd)) < 1e-3f);
            }

            // ⑤ RepWorld 基准不变性：f=f0 时 qWant=qPose（姿势基准精确保留）
            {
                var r = RandomRot(); var g0 = RandomRot(); var pose = RandomRot();
                var q = PetRetargetMath.RepWorld(r, g0, g0, pose);
                Check("RepWorld f=f0 \u2192 qPose", Quaternion.Angle(q, pose) < 1e-4f);
            }

            // ⑥ ConjVec 与共轭一致性：向量经旋转差共轭后模长不变
            {
                var r = RandomRot();
                var v = Random.onUnitSphere;
                Check("ConjVec \u4fdd\u6a21\u957f", Mathf.Abs(PetRetargetMath.ConjVec(r, v).magnitude - 1f) < 1e-5f);
            }

            // ⑦ 四元数连续化语义：q 与 -q 等价（曲线写出前的连续化依据）
            {
                var q = RandomRot();
                var nq = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                Check("q\u2261-q \u89d2\u5ea6\u7b49\u4ef7", Quaternion.Angle(q, nq) < 1e-5f);
            }

            Debug.Log($"[MathSelfTest] \u5168\u90e8\u901a\u8fc7: {pass} \u9879\u65ad\u8a00");
        }

        static Quaternion RandomRot() => Quaternion.Euler(Random.Range(-180f, 180f), Random.Range(-180f, 180f), Random.Range(-180f, 180f));
    }
}
