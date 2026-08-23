using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

namespace GIC.Editor.Retarget
{
    /// <summary>
    /// 全身部位审计（2026-08-23 固化，v15 定案后）：
    /// 对全部 _MMD clip 的 14 个部位（锁骨/上臂/前臂/躯干/颈头/D 链腿/足尖 ×LR）做
    /// GI 源 vs 输出的骨段方向误差统计，定位跨部位异常（与 PaimonLegAudit 的腿部深查互补）。
    /// 用法：菜单 Tools/桌宠/全身部位审计。
    ///
    /// 采样约定（防 GI 符号跳变伪影——2026-08-23 多轮实测）：
    /// GI 源四元数存在 q≡-q 符号跳变（Standby R Thigh@1.083s 等），AnimationMode 均匀采样
    /// 跨跳变逐分量插值产出假翻转（171°@1.34s 类），**本工具仅在 GI 曲线的 key 时刻采样**
    ///（key 时刻取值恒精确，与管线 v11 一致）；每个 clip 采样点 = 该 clip 全骨 key 时刻并集
    /// 的 1/3 抽稀（速度/精度折中）。残留 <5° 误差为抽稀位置与管线 BuildTrack 容差，可忽略。
    /// 判定：>15° 即不健康（需人工核对该时刻是否恰好是 GI 源符号跳变的相邻 key）。
    /// </summary>
    public static class PaimonBodyAudit
    {
        const string GI_FBX = "Assets/Art/PaimonPet/Model/NPC_Kanban_Paimon_Model.fbx";
        const string MMD_FBX = "Assets/Art/PaimonPet/Model/Paimon_MMD.fbx";
        const string SRC_DIR = "Assets/Art/PaimonPet/Animations";
        const string OUT_DIR = "Assets/Art/PaimonPet/Animations/MMD";

        [MenuItem("Tools/桌宠/全身部位审计")]
        public static void Run()
        {
            var log = new StringBuilder();
            try
            {
                RunInternal(log);
                Debug.Log("[BodyAudit] 完成\n" + log);
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[BodyAudit] 失败: " + ex + "\n" + log);
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
                Transform bip = null;
                foreach (var t in gi.GetComponentsInChildren<Transform>(true))
                    if (t.name == "Bip001" && t.Find("Bip001 Pelvis") != null) { bip = t; break; }
                if (bip == null) throw new System.InvalidOperationException("Bip001 未找到");
                var giRootGo = bip.parent.gameObject;
                Transform GIB(string n) => gi.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == n);
                Transform MIB(string n) => mmd.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == n);

                void Orthon(Vector3 pelvis, Vector3 head, Vector3 handL, Vector3 handR, out Vector3 y, out Vector3 z)
                { y = (head - pelvis).normalized; var xr = handL - handR; var x = (xr - Vector3.Project(xr, y)).normalized; z = Vector3.Cross(x, y).normalized; }

                // 部位：MMD 父→子 vs GI 父→子（标准链 + D 链 + 足尖全覆盖）
                (string label, string mP, string mC, string gP, string gC)[] parts = {
                    ("锁骨L", "肩.L", "腕.L", "Bip001 L Clavicle", "Bip001 L UpperArm"),
                    ("上臂L", "腕.L", "ひじ.L", "Bip001 L UpperArm", "Bip001 L Forearm"),
                    ("前臂L", "ひじ.L", "手首.L", "Bip001 L Forearm", "Bip001 L Hand"),
                    ("锁骨R", "肩.R", "腕.R", "Bip001 R Clavicle", "Bip001 R UpperArm"),
                    ("上臂R", "腕.R", "ひじ.R", "Bip001 R UpperArm", "Bip001 R Forearm"),
                    ("前臂R", "ひじ.R", "手首.R", "Bip001 R Forearm", "Bip001 R Hand"),
                    ("躯干", "腰", "上半身2", "Bip001 Pelvis", "Bip001 Spine1"),
                    ("颈头", "首", "頭", "Bip001 Neck", "Bip001 Head"),
                    ("大腿L", "足D.L", "ひざD.L", "Bip001 L Thigh", "Bip001 L Calf"),
                    ("膝段L", "ひざD.L", "足首D.L", "Bip001 L Calf", "Bip001 L Foot"),
                    ("大腿R", "足D.R", "ひざD.R", "Bip001 R Thigh", "Bip001 R Calf"),
                    ("膝段R", "ひざD.R", "足首D.R", "Bip001 R Calf", "Bip001 R Foot"),
                    ("足尖L", "足首D.L", "足先EX.L", "Bip001 L Foot", "Bip001 L Toe0"),
                    ("足尖R", "足首D.R", "足先EX.R", "Bip001 R Foot", "Bip001 R Toe0"),
                };
                var partMax = new Dictionary<string, float>();
                var partWorst = new Dictionary<string, string>();

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

                    // R：本 clip f0 对齐（每 clip 各自，剔除跨 clip 残留）
                    AnimationMode.SampleAnimationClip(giRootGo, giClip, 0f);
                    AnimationMode.SampleAnimationClip(mmd, mdClip, 0f);
                    Orthon(GIB("Bip001 Pelvis").position, GIB("Bip001 Head").position, GIB("Bip001 L Hand").position, GIB("Bip001 R Hand").position, out var gy, out var gz);
                    Orthon(MIB("腰").position, MIB("頭").position, MIB("手首.L").position, MIB("手首.R").position, out var my, out var mz);
                    var Rinv = Quaternion.Inverse(Quaternion.LookRotation(gz, gy) * Quaternion.Inverse(Quaternion.LookRotation(mz, my)));

                    // GI 侧 key 时刻并集（1/3 抽稀）
                    var times = new SortedSet<float>();
                    foreach (var b in AnimationUtility.GetCurveBindings(giClip))
                    {
                        if (!b.propertyName.StartsWith("m_LocalRotation")) continue;
                        var c = AnimationUtility.GetEditorCurve(giClip, b);
                        if (c != null) foreach (var k in c.keys) times.Add(k.time);
                    }
                    var tl = times.ToList();
                    var errs = new Dictionary<string, float>();
                    var errAt = new Dictionary<string, float>();
                    int total = 0;
                    for (int i = 0; i < tl.Count; i += 3)
                    {
                        float t = tl[i];
                        AnimationMode.SampleAnimationClip(giRootGo, giClip, t);
                        AnimationMode.SampleAnimationClip(mmd, mdClip, t);
                        total++;
                        foreach (var (label, mP, mC, gP, gC) in parts)
                        {
                            var mp = MIB(mP); var mc = MIB(mC); var gp = GIB(gP); var gc = GIB(gC);
                            if (mp == null || mc == null || gp == null || gc == null) continue;
                            float e = Vector3.Angle((mc.position - mp.position).normalized, Rinv * (gc.position - gp.position).normalized);
                            if (!errs.ContainsKey(label) || e > errs[label]) { errs[label] = e; errAt[label] = t; }
                        }
                    }
                    var worst = errs.Where(kv => kv.Value > 8f).OrderByDescending(kv => kv.Value).Take(6).ToList();
                    log.AppendLine($"{baseName}: {(worst.Count == 0 ? "全 <8° ✓" : string.Join(" | ", worst.Select(kv => $"{kv.Key}={kv.Value:F0}°@{errAt[kv.Key]:F2}s")))}");
                    foreach (var kv in errs)
                        if (!partMax.ContainsKey(kv.Key) || kv.Value > partMax[kv.Key]) { partMax[kv.Key] = kv.Value; partWorst[kv.Key] = $"{baseName}@{errAt[kv.Key]:F2}s"; }
                }

                log.AppendLine();
                log.AppendLine("== 跨 clip 部位汇总 ==");
                foreach (var kv in partMax.OrderByDescending(kv => kv.Value))
                    log.AppendLine($"{kv.Key}: {kv.Value:F1}° ({partWorst[kv.Key]}){(kv.Value > 15 ? " ⚠" : "")}");
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
