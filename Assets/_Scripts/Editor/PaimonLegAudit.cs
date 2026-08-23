using System.Text;
using UnityEditor;
using UnityEngine;
using System.Linq;
using System.Collections.Generic;

namespace GIC.Editor.Retarget
{
    /// <summary>
    /// 腿部方向审计（docs/19 §2.5 病灶排查 2026-08-23）：
    /// 用管线同款对齐 R（Standby f0 盆/头/双手正交基，rotY≈150.2°）直接对比
    /// 输出 _MMD clip FK 的腿段方向 vs R⁻¹·(GI 源腿段方向)——纯向量夹角，
    /// 无重建参考系歧义（此前多轮排查被"模型自身前后左右重建"反复误导的教训）。
    /// 用法：菜单 Tools/桌宠/腿部方向审计。每 clip 报两条链：
    /// 标准链膝段（Calf→Foot vs ひざ→足首，代表骨 v6 关节角传递，零蒙皮）与
    /// D 链（足D→ひざD 大腿段、ひざD→足首D 膝段——**腿部蒙皮全在 D 链**，v14 绝对世界
    /// 跟随公式的真实可视效果；2026-08-23 v13 回归实证：只审标准链会漏掉 D 链 15cm 侧漂）。
    /// 最大方向误差与超 20° 帧数；>15° 即不健康。
    /// </summary>
    public static class PaimonLegAudit
    {
        const string GI_FBX = "Assets/Art/PaimonPet/Model/NPC_Kanban_Paimon_Model.fbx";
        const string MMD_FBX = "Assets/Art/PaimonPet/Model/Paimon_MMD.fbx";
        const string STANDBY_GI = "Assets/Art/PaimonPet/Animations/Ani_NPC_Kanban_Paimon_Standby.anim";
        const string SRC_DIR = "Assets/Art/PaimonPet/Animations";
        const string OUT_DIR = "Assets/Art/PaimonPet/Animations/MMD";

        [MenuItem("Tools/桌宠/腿部方向审计")]
        public static void Run()
        {
            var log = new StringBuilder();
            try
            {
                RunInternal(log);
                Debug.Log("[LegAudit] 完成\n" + log);
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[LegAudit] 失败: " + ex + "\n" + log);
            }
        }

        /// <summary>
        /// 审计结论备忘（2026-08-23 定案）：
        /// ① GI 源曲线存在真实的 q≡-q 符号跳变（Standby/C06 的 Bip001 R Thigh 实测@0.27/0.82/1.35s 等）。
        /// 本审计用 AnimationMode 采样 GI 源，跨跳变的分量插值会产出假翻转姿势——所以本工具报出的
        /// 单帧大误差（Standby R 171°@1.34s、C06 R 118°@0.83s）与跳变时刻吻合者为**审计伪影**，
        /// 非输出错误（管线 v11 已做 BuildTrack 符号连续化 + 输出逐帧连续化，key 时刻取值精确）。
        /// ② C05_01 源曲线路径全为哈希数字（path: 1315740152...），管线按设计忽略 → 输出为静态
        /// Standby 姿势（非该动画的真实重定向）。审计 GI 侧对哈希路径无效 → C05_01 报告值全为伪影。
        /// 该 clip 已从 CLIPS 剔除。
        /// </summary>
        static void DetectSignJumps(StringBuilder log)
        {
            log.AppendLine();
            log.AppendLine("== GI 源全骨符号跳变检测 ==");
            string[] files =
            {
                "Assets/Art/PaimonPet/Animations/Ani_NPC_Kanban_Paimon_Standby.anim",
                "Assets/Art/PaimonPet/Animations/Ani_Cs_NPC_Kanban_Paimon_C06.anim",
                "Assets/Art/PaimonPet/Animations/Ani_Cs_NPC_Kanban_Paimon_C05_01.anim",
            };
            foreach (var f in files)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(f);
                if (clip == null) { log.AppendLine($"{f}: 缺"); continue; }
                var groups = AnimationUtility.GetCurveBindings(clip)
                    .Where(b => b.propertyName.StartsWith("m_LocalRotation") && !long.TryParse(b.path, out _))
                    .GroupBy(b => b.path).ToList();
                int bonesWithJumps = 0;
                var details = new List<string>();
                foreach (var g in groups)
                {
                    var cs = new AnimationCurve[4];
                    foreach (var b in g) cs["xyzw".IndexOf(b.propertyName[b.propertyName.Length - 1])] = AnimationUtility.GetEditorCurve(clip, b);
                    if (cs.Any(c => c == null)) continue;
                    var times = new SortedSet<float>();
                    foreach (var c in cs) foreach (var k in c.keys) times.Add(k.time);
                    var tl = times.ToList();
                    int jumps = 0; var jumpAt = new List<float>();
                    for (int i = 1; i < tl.Count; i++)
                    {
                        var q0 = new Quaternion(cs[0].Evaluate(tl[i - 1]), cs[1].Evaluate(tl[i - 1]), cs[2].Evaluate(tl[i - 1]), cs[3].Evaluate(tl[i - 1])).normalized;
                        var q1 = new Quaternion(cs[0].Evaluate(tl[i]), cs[1].Evaluate(tl[i]), cs[2].Evaluate(tl[i]), cs[3].Evaluate(tl[i])).normalized;
                        if (Quaternion.Dot(q0, q1) < 0f) { jumps++; jumpAt.Add(tl[i]); }
                    }
                    if (jumps > 0)
                    {
                        bonesWithJumps++;
                        if (details.Count < 8) details.Add($"{g.Key.Split('/').Last()}@{string.Join(",", jumpAt.Take(3).Select(x => x.ToString("F2")).ToArray())}");
                    }
                }
                log.AppendLine($"  {System.IO.Path.GetFileNameWithoutExtension(f)}: 骨={groups.Count} 含跳变骨={bonesWithJumps} {(details.Count > 0 ? string.Join(" | ", details.ToArray()) : "")}");
            }
        }

        static void RunInternal(StringBuilder log)
        {
            var giFbx = AssetDatabase.LoadAssetAtPath<GameObject>(GI_FBX);
            var mmdFbx = AssetDatabase.LoadAssetAtPath<GameObject>(MMD_FBX);
            if (giFbx == null || mmdFbx == null) throw new System.InvalidOperationException("FBX 缺失");
            var gi = (GameObject)PrefabUtility.InstantiatePrefab(giFbx);
            var mmd = (GameObject)PrefabUtility.InstantiatePrefab(mmdFbx);
            try
            {
                AnimationMode.StartAnimationMode();
                Transform GIB(string n) => gi.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == n);
                Transform MIB(string n) => mmd.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == n);
                Transform bip = null;
                foreach (var t in gi.GetComponentsInChildren<Transform>(true))
                    if (t.name == "Bip001" && t.Find("Bip001 Pelvis") != null) { bip = t; break; }
                if (bip == null) throw new System.InvalidOperationException("Bip001 未找到");

                // 对齐 R：Standby f0（管线同款）
                var stb = AssetDatabase.LoadAssetAtPath<AnimationClip>(STANDBY_GI);
                AnimationMode.SampleAnimationClip(bip.parent.gameObject, stb, 0f);
                Vector3 P(Transform t) => t.position;
                void Orthon(Vector3 pelvis, Vector3 head, Vector3 handL, Vector3 handR, out Vector3 y, out Vector3 z)
                { y = (head - pelvis).normalized; var xr = handL - handR; var x = (xr - Vector3.Project(xr, y)).normalized; z = Vector3.Cross(x, y).normalized; }
                Orthon(P(GIB("Bip001 Pelvis")), P(GIB("Bip001 Head")), P(GIB("Bip001 L Hand")), P(GIB("Bip001 R Hand")), out var gy, out var gz);
                Orthon(P(MIB("腰")), P(MIB("頭")), P(MIB("手首.L")), P(MIB("手首.R")), out var my, out var mz);
                var Rinv = Quaternion.Inverse(Quaternion.LookRotation(gz, gy) * Quaternion.Inverse(Quaternion.LookRotation(mz, my)));
                log.AppendLine($"[align] R 重算完成");

                foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { OUT_DIR }).OrderBy(g => g))
                {
                    var outPath = AssetDatabase.GUIDToAssetPath(guid);
                    var mdClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(outPath);
                    if (mdClip == null) continue;
                    var baseName = System.IO.Path.GetFileNameWithoutExtension(outPath).Replace("_MMD", "");
                    var giClip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{SRC_DIR}/{baseName}.anim");
                    // Cs 系列源文件名带 Cs 前缀（Ani_Cs_...）
                    if (giClip == null)
                        giClip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{SRC_DIR}/{baseName.Replace("Ani_NPC_", "Ani_Cs_NPC_")}.anim");
                    if (giClip == null) { log.AppendLine($"{baseName}: GI 源缺失"); continue; }

                    float maxL = 0, maxR = 0; float tL = 0, tR = 0; int badL = 0, badR = 0; int total = 0;
                    float maxThD = 0, maxKnD = 0; float tThD = 0, tKnD = 0; int badD = 0; string badDSide = "";
                    const int steps = 32;
                    for (int i = 0; i <= steps; i++)
                    {
                        float t = Mathf.Min(mdClip.length * i / steps, mdClip.length);
                        AnimationMode.SampleAnimationClip(bip.parent.gameObject, giClip, t);
                        AnimationMode.SampleAnimationClip(mmd, mdClip, t);
                        total++;
                        foreach (var side in new[] { "L", "R" })
                        {
                            var gThigh = GIB($"Bip001 {side} Thigh"); var gKnee = GIB($"Bip001 {side} Calf"); var gAnkle = GIB($"Bip001 {side} Foot");
                            var mKnee = MIB($"ひざ.{side}"); var mAnkle = MIB($"足首.{side}");
                            if (gKnee != null && gAnkle != null && mKnee != null && mAnkle != null)
                            {
                                var gDir = (gAnkle.position - gKnee.position).normalized;
                                var mDir = (mAnkle.position - mKnee.position).normalized;
                                float err = Vector3.Angle(mDir, Rinv * gDir);
                                if (side == "L") { if (err > maxL) { maxL = err; tL = t; } if (err > 20f) badL++; }
                                else { if (err > maxR) { maxR = err; tR = t; } if (err > 20f) badR++; }
                            }
                            // D 链（可视蒙皮链）：大腿段 足D→ひざD 对 Thigh→Calf；膝段 ひざD→足首D 对 Calf→Foot
                            var mThD = MIB($"足D.{side}"); var mKnD = MIB($"ひざD.{side}"); var mFoD = MIB($"足首D.{side}");
                            if (gThigh == null || gKnee == null || gAnkle == null || mThD == null || mKnD == null || mFoD == null) continue;
                            float eTh = Vector3.Angle((mKnD.position - mThD.position).normalized, Rinv * (gKnee.position - gThigh.position).normalized);
                            float eKn = Vector3.Angle((mFoD.position - mKnD.position).normalized, Rinv * (gAnkle.position - gKnee.position).normalized);
                            if (eTh > maxThD) { maxThD = eTh; tThD = t; }
                            if (eKn > maxKnD) { maxKnD = eKn; tKnD = t; }
                            if (eTh > 20f || eKn > 20f) { badD++; badDSide = $"{side}大腿{eTh:F0}/膝{eKn:F0}@{t:F2}s"; }
                        }
                    }
                    string flag = (maxL > 15f || maxR > 15f || maxThD > 15f || maxKnD > 15f) ? " ⚠" : " ✓";
                    log.AppendLine($"{baseName}: 标准L={maxL:F1}°@{tL:F2}s R={maxR:F1}°@{tR:F2}s 超20° L={badL}/{total} R={badR}/{total} | D链 大腿={maxThD:F1}°@{tThD:F2}s 膝段={maxKnD:F1}°@{tKnD:F2}s 超20°={badD}/{total * 2}{(badD > 0 ? " 末次:" + badDSide : "")}{flag}");
                }
            }
            finally
            {
                AnimationMode.StopAnimationMode();
                Object.DestroyImmediate(gi);
                Object.DestroyImmediate(mmd);
            }
        }
    }
}
