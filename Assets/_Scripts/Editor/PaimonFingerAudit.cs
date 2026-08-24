using System.Text;
using UnityEditor;
using UnityEngine;
using System.Linq;
using System.Collections.Generic;

namespace GIC.Editor.Retarget
{
    /// <summary>
    /// 手指方向审计（2026-08-23 手指病灶排查）：数值定位"所有动作手指不对"。
    /// 三段对比：GI 源（Standby f0 参考姿势 + 逐 key 帧）vs MMD 绑定姿势 vs MMD 输出 _MMD clip。
    /// 指标：①指节弯曲角（旋转不变量，跨骨架直接可比）②第一节骨段方向误差（R⁻¹ 共轭）
    /// ③中节关节位置误差（align 映射，mm）④掌心法向对比 ⑤GI 源手指活动度（源数据有没有动）。
    /// 采样只在 GI 曲线 key 时刻（q≡-q 符号跳变铁律，见 PaimonLegAudit 备忘）。
    /// 用法：菜单 Tools/桌宠/手指方向审计；日志落盘 Logs/finger_audit.txt。
    /// </summary>
    public static class PaimonFingerAudit
    {
        const string GI_FBX = "Assets/Art/PaimonPet/Model/NPC_Kanban_Paimon_Model.fbx";
        const string MMD_FBX = "Assets/Art/PaimonPet/Model/Paimon_MMD.fbx";
        const string STANDBY_GI = "Assets/Art/PaimonPet/Animations/Ani_NPC_Kanban_Paimon_Standby.anim";
        const string SRC_DIR = "Assets/Art/PaimonPet/Animations";
        const string OUT_DIR = "Assets/Art/PaimonPet/Animations/MMD";

        class FC { public string label, giRoot, giMid, m1, m2, m3; }

        static FC[] Fingers(string s) => new[]
        {
            // R 拇指：GI R 手无 Finger0/01——拇指链是 DMZ R 01→DMZ R 02→Finger0Nub（骨架实测）
            new FC{ label="拇指", giRoot=s=="R"?"DMZ R 01":$"Bip001 {s} Finger0", giMid=s=="R"?"DMZ R 02":$"Bip001 {s} Finger01", m1=$"親指０.{s}", m2=$"親指１.{s}", m3=$"親指２.{s}" },
            new FC{ label="食指", giRoot=$"Bip001 {s} Finger1", giMid=$"Bip001 {s} Finger11", m1=$"人指１.{s}", m2=$"人指２.{s}", m3=$"人指３.{s}" },
            new FC{ label="中指", giRoot=$"Bip001 {s} Finger2", giMid=$"Bip001 {s} Finger21", m1=$"中指１.{s}", m2=$"中指２.{s}", m3=$"中指３.{s}" },
            new FC{ label="环指", giRoot=$"Bip001 {s} Finger3", giMid=$"Bip001 {s} Finger31", m1=$"薬指１.{s}", m2=$"薬指２.{s}", m3=$"薬指３.{s}" },
            new FC{ label="小指", giRoot=$"Bip001 {s} Finger4", giMid=$"Bip001 {s} Finger41", m1=$"小指１.{s}", m2=$"小指２.{s}", m3=$"小指３.{s}" },
        };

        [MenuItem("Tools/桌宠/手指方向审计")]
        public static void Run()
        {
            var log = new StringBuilder();
            try
            {
                RunInternal(log);
                Debug.Log("[FingerAudit] 完成\n" + log);
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[FingerAudit] 失败: " + ex + "\n" + log);
            }
            finally
            {
                try
                {
                    var logPath = System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "Logs", "finger_audit.txt");
                    System.IO.Directory.CreateDirectory(System.IO.Directory.GetParent(logPath).FullName);
                    System.IO.File.WriteAllText(logPath, log.ToString());
                }
                catch (System.Exception exF) { Debug.LogError("[FingerAudit] 日志写盘失败: " + exF.Message); }
            }
        }

        static Vector3 PalmNormal(Vector3 iRoot, Vector3 mRoot, Vector3 pRoot, Vector3 thRoot)
        {
            var n = Vector3.Cross(mRoot - iRoot, pRoot - iRoot);
            if (n.sqrMagnitude < 1e-12f) return Vector3.zero;
            n.Normalize();
            return Vector3.Dot(thRoot - iRoot, n) > 0 ? n : -n;
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

                // ---- §0 GI 手子树清单（确认 GI 手指骨结构与 Nub）
                foreach (var s in new[] { "L", "R" })
                {
                    var hand = GIB($"Bip001 {s} Hand");
                    log.AppendLine($"== GI {s} Hand 子树 ==");
                    void Dump(Transform t, int d)
                    {
                        log.AppendLine($"  {new string(' ', d * 2)}{t.name}");
                        foreach (Transform c in t) Dump(c, d + 1);
                    }
                    if (hand != null) Dump(hand, 0); else log.AppendLine("  (缺)");
                }

                // ---- MMD 绑定姿势快照（采样前）
                var bindPos = new Dictionary<string, Vector3>();
                foreach (var t in mmd.GetComponentsInChildren<Transform>(true))
                    if (!bindPos.ContainsKey(t.name)) bindPos[t.name] = t.position;

                // ---- 对齐（管线同款：Standby f0 盆/头/双手正交基 + scale + T）
                var stb = AssetDatabase.LoadAssetAtPath<AnimationClip>(STANDBY_GI);
                AnimationMode.SampleAnimationClip(bip.parent.gameObject, stb, 0f);
                Vector3 P(Transform t) => t.position;
                void Orthon(Vector3 pelvis, Vector3 head, Vector3 handL, Vector3 handR, out Vector3 y, out Vector3 z)
                { y = (head - pelvis).normalized; var xr = handL - handR; var x = (xr - Vector3.Project(xr, y)).normalized; z = Vector3.Cross(x, y).normalized; }
                Orthon(P(GIB("Bip001 Pelvis")), P(GIB("Bip001 Head")), P(GIB("Bip001 L Hand")), P(GIB("Bip001 R Hand")), out var gy, out var gz);
                Orthon(P(MIB("腰")), P(MIB("頭")), P(MIB("手首.L")), P(MIB("手首.R")), out var my, out var mz);
                var R = Quaternion.LookRotation(gz, gy) * Quaternion.Inverse(Quaternion.LookRotation(mz, my));
                var gp2 = P(GIB("Bip001 Pelvis")); var gh2 = P(GIB("Bip001 Head"));
                var mp2 = P(MIB("腰")); var mh2 = P(MIB("頭"));
                float scale = Vector3.Distance(gp2, gh2) / Mathf.Max(Vector3.Distance(mp2, mh2), 1e-5f);
                var T = gp2 - scale * (R * mp2);
                Quaternion Rinv = Quaternion.Inverse(R);
                Vector3 Align(Vector3 m) => T + scale * (R * m);
                log.AppendLine($"[align] scale={scale:F3}");

                // ---- §1 f0 姿态表：GI f0 vs MMD 绑定 vs MMD 输出 Standby f0
                var stbMmd = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{OUT_DIR}/Ani_NPC_Kanban_Paimon_Standby_MMD.anim");
                if (stbMmd == null) throw new System.InvalidOperationException("Standby_MMD 缺（先跑重定向管线）");
                AnimationMode.SampleAnimationClip(mmd, stbMmd, 0f);

                log.AppendLine();
                log.AppendLine("== f0 姿态表（弯曲角°：旋转不变量，三栏直接可比；seg方向误差/中节位置误差=输出 vs GI 共轭）==");
                foreach (var s in new[] { "L", "R" })
                {
                    var gWrist = GIB($"Bip001 {s} Hand");
                    var mWrist = MIB($"手首.{s}");
                    if (gWrist == null || mWrist == null) { log.AppendLine($"{s} 手腕缺"); continue; }
                    // 掌心（R 拇指根 = DMZ R 01；任一缺骨跳过）
                    var gI = GIB($"Bip001 {s} Finger1"); var gM = GIB($"Bip001 {s} Finger2"); var gP = GIB($"Bip001 {s} Finger4");
                    var gTh = s == "R" ? GIB("DMZ R 01") : GIB($"Bip001 {s} Finger0");
                    if (gI != null && gM != null && gP != null && gTh != null)
                    {
                        var giPalm = PalmNormal(P(gI), P(gM), P(gP), P(gTh));
                        var mBindPalm = PalmNormal(bindPos[$"人指１.{s}"], bindPos[$"中指１.{s}"], bindPos[$"小指１.{s}"], bindPos[$"親指０.{s}"]);
                        var mOutPalm = PalmNormal(P(MIB($"人指１.{s}")), P(MIB($"中指１.{s}")), P(MIB($"小指１.{s}")), P(MIB($"親指０.{s}")));
                        log.AppendLine($"{s} 掌心: GI={giPalm} MMD绑定={mBindPalm} MMD输出={mOutPalm} | 输出vs GI共轭 误差={Vector3.Angle(mOutPalm, Rinv * giPalm):F1}° 绑定vs GI共轭={Vector3.Angle(mBindPalm, Rinv * giPalm):F1}°");
                    }
                    foreach (var fc in Fingers(s))
                    {
                        var gr = GIB(fc.giRoot); var gm = GIB(fc.giMid);
                        var o1 = MIB(fc.m1); var o2 = MIB(fc.m2); var o3 = MIB(fc.m3);
                        if (gr == null || gm == null || o1 == null || o2 == null || o3 == null)
                        { log.AppendLine($"  {s}{fc.label}: 骨缺 gr={gr != null} gm={gm != null}"); continue; }
                        var gTip = gm.childCount > 0 ? gm.GetChild(0) : null; // GI 末节尖（Nub）
                        // GI：腕→根→中 弯曲角
                        var giDirW = (P(gr) - P(gWrist)).normalized;
                        var giDir1 = (P(gm) - P(gr)).normalized;
                        float giBend = Vector3.Angle(giDirW, giDir1);
                        float giBend2 = -1f; var giDir2 = Vector3.zero;
                        if (gTip != null) { giDir2 = (P(gTip) - P(gm)).normalized; giBend2 = Vector3.Angle(giDir1, giDir2); }
                        // MMD 绑定：腕→1→2→3
                        var bDirW = (bindPos[fc.m1] - bindPos[$"手首.{s}"]).normalized;
                        var bDir1 = (bindPos[fc.m2] - bindPos[fc.m1]).normalized;
                        var bDir2 = (bindPos[fc.m3] - bindPos[fc.m2]).normalized;
                        float bBend1 = Vector3.Angle(bDirW, bDir1);
                        float bBend2 = Vector3.Angle(bDir1, bDir2);
                        // MMD 输出 f0
                        var oDirW = (P(o1) - P(mWrist)).normalized;
                        var oDir1 = (P(o2) - P(o1)).normalized;
                        var oDir2 = (P(o3) - P(o2)).normalized;
                        float oBend1 = Vector3.Angle(oDirW, oDir1);
                        float oBend2 = Vector3.Angle(oDir1, oDir2);
                        // 方向/位置误差（输出 vs GI 共轭）
                        float segErr = Vector3.Angle(oDir1, Rinv * giDir1);
                        float seg2Err = gTip != null ? Vector3.Angle(oDir2, Rinv * giDir2) : -1f;
                        float posErrMm = Vector3.Distance(Align(P(o2)), P(gm)) * 1000f;
                        log.AppendLine($"  {s}{fc.label}: 根节弯 GI={giBend:F0} 绑定={bBend1:F0} 输出={oBend1:F0} | 中节弯 GI={giBend2:F0} 绑定={bBend2:F0} 输出={oBend2:F0} | seg误差={segErr:F1}°/{seg2Err:F1}° 中节位置={posErrMm:F0}mm");
                    }
                }

                // ---- §2 逐 clip：GI 源手指活动度 + 输出跟踪误差（key 时刻采样）
                log.AppendLine();
                log.AppendLine("== 逐clip 跟踪（key 时刻采样）: GI活动度=首段骨段方向全程最大变化；segErr=输出首段方向 vs GI共轭；posErr=中节位置 ==");
                foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { OUT_DIR }).OrderBy(g => g))
                {
                    var outPath = AssetDatabase.GUIDToAssetPath(guid);
                    var mdClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(outPath);
                    if (mdClip == null) continue;
                    var baseName = System.IO.Path.GetFileNameWithoutExtension(outPath).Replace("_MMD", "");
                    var giClip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{SRC_DIR}/{baseName}.anim");
                    if (giClip == null)
                        giClip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{SRC_DIR}/{baseName.Replace("Ani_NPC_", "Ani_Cs_NPC_")}.anim");
                    if (giClip == null) { log.AppendLine($"{baseName}: GI 源缺失"); continue; }

                    // GI key 时刻并集（符号跳变铁律）
                    var times = new SortedSet<float>();
                    foreach (var b in AnimationUtility.GetCurveBindings(giClip))
                        if (b.type == typeof(Transform) && !long.TryParse(b.path, out _))
                        {
                            var c = AnimationUtility.GetEditorCurve(giClip, b);
                            if (c != null) foreach (var k in c.keys) times.Add(k.time);
                        }
                    var tl = times.ToList();

                    // 手指骨基准（f0 方向，算活动度）
                    var giBaseDir = new Dictionary<string, Vector3>();
                    AnimationMode.SampleAnimationClip(bip.parent.gameObject, giClip, tl[0]);
                    foreach (var s in new[] { "L", "R" })
                        foreach (var fc in Fingers(s))
                        {
                            var gr = GIB(fc.giRoot); var gm = GIB(fc.giMid);
                            if (gr != null && gm != null) giBaseDir[s + fc.label] = (gm.position - gr.position).normalized;
                        }

                    float maxSeg = 0, maxSeg2 = 0, maxPos = 0, maxAct = 0, tSeg = 0, tSeg2 = 0, tPos = 0, tAct = 0;
                    string wSeg = "", wSeg2 = "", wPos = "", wAct = "";
                    foreach (var t in tl)
                    {
                        // 无曲线骨残留陷阱（2026-08-24 实证）：多数 clip 缺 R环指/小指（Finger3/31/4/41）曲线，
                        // AnimationMode 只采样有曲线骨，无曲线骨残留上一 clip 采样末态——曾致 8 clip 报
                        // 恒定 61.2°/140.9° 假误差（残留态 vs 输出）。管线语义 = 无曲线骨回退 Standby f0 局部
                        //（giRefRot），故每帧先回 Standby 参考姿势再采样目标 clip，与管线逐字对齐。
                        AnimationMode.SampleAnimationClip(bip.parent.gameObject, stb, 0f);
                        AnimationMode.SampleAnimationClip(bip.parent.gameObject, giClip, t);
                        AnimationMode.SampleAnimationClip(mmd, mdClip, t);
                        foreach (var s in new[] { "L", "R" })
                            foreach (var fc in Fingers(s))
                            {
                                var gr = GIB(fc.giRoot); var gm = GIB(fc.giMid);
                                var o1 = MIB(fc.m1); var o2 = MIB(fc.m2); var o3 = MIB(fc.m3);
                                if (gr == null || gm == null || o1 == null || o2 == null) continue;
                                var gDir = (gm.position - gr.position).normalized;
                                var mDir = (o2.position - o1.position).normalized;
                                float segErr = Vector3.Angle(mDir, Rinv * gDir);
                                if (segErr > maxSeg) { maxSeg = segErr; tSeg = t; wSeg = s + fc.label; }
                                var gTip = gm.childCount > 0 ? gm.GetChild(0) : null;
                                if (gTip != null && o3 != null)
                                {
                                    float seg2 = Vector3.Angle((o3.position - o2.position).normalized, Rinv * (gTip.position - gm.position).normalized);
                                    if (seg2 > maxSeg2) { maxSeg2 = seg2; tSeg2 = t; wSeg2 = s + fc.label; }
                                }
                                float posMm = Vector3.Distance(Align(o2.position), gm.position) * 1000f;
                                if (posMm > maxPos) { maxPos = posMm; tPos = t; wPos = s + fc.label; }
                                float act = Vector3.Angle(gDir, giBaseDir[s + fc.label]);
                                if (act > maxAct) { maxAct = act; tAct = t; wAct = s + fc.label; }
                            }
                    }
                    string flag = maxSeg > 15f || maxSeg2 > 15f ? " ⚠" : " ✓";
                    log.AppendLine($"{baseName}: GI活动度max={maxAct:F0}°({wAct}@{tAct:F2}s) segErr={maxSeg:F1}°({wSeg}@{tSeg:F2}s) seg2Err={maxSeg2:F1}°({wSeg2}@{tSeg2:F2}s) posErr={maxPos:F0}mm({wPos}@{tPos:F2}s){flag}");
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
