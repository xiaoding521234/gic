using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GIC.Editor.Rerig
{
    /// <summary>
    /// 方案A管线 v2（docs/19 §2.3）：MMD 网格重蒙皮到 GI Bip001 骨架。
    /// v1 教训：骨骼最近匹配在两个不一致的坐标空间进行（MMD 资产世界 vs GI model 空间），
    /// 权重指错骨 → 动起来网格碎裂。v2 全程统一同一临时场景的世界空间：
    /// ① GI 实例骨骼用 .anim t=0 绑定姿势覆写局部变换，向场景世界合成；
    /// ② MMD 实例同场景读骨骼/网格世界矩阵；
    /// ③ 盆骨/头/双手/双脚关键点求刚体对齐（旋转+缩放+平移），MMD 骨投影到 GI 空间后做最近骨匹配；
    /// ④ 顶点按 Σ w·(G·B⁻¹)·v 换姿势（对齐已内含在逐骨乘积中），再转 model 节点局部存网格；
    /// ⑤ bindpose = (L·G)⁻¹（L=model 节点世界逆）；SMR bones 直接引用场景实例骨骼，存 prefab 自动重映射。
    /// </summary>
    public static class PaimonRerigPipeline
    {
        const string ANIM = "Assets/Art/PaimonPet/Animations/Ani_NPC_Kanban_Paimon_Standby.anim";
        const string ANIM_LEGACY = "Assets/Art/PaimonPet/Animations/Ani_NPC_Kanban_Paimon_Standby_Legacy.anim";
        const string MMD_FBX_PATH = "Assets/Art/PaimonPet/Model/Paimon_MMD.fbx";
        const string GI_FBX_PATH = "Assets/Art/PaimonPet/Model/NPC_Kanban_Paimon_Model.fbx";
        const string OUT_DIR = "Assets/Art/PaimonPet/Model";
        const string MESH_ASSET = OUT_DIR + "/Paimon_GIRigMesh.asset";
        const string PREFAB_OUT = OUT_DIR + "/Paimon_GIRig.prefab";
        const string TEST_SCENE = "Assets/Scenes/PaimonRerigTest.unity";
        const string GO_NAME = "Paimon_GIRig";

        [MenuItem("Tools/桌宠/方案A: MMD网格重蒙皮到GI骨架")]
        public static void Run()
        {
            var log = new StringBuilder();
            try
            {
                RunInternal(log);
                Debug.Log("[RerigV2] 完成\n" + log);
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[RerigV2] 失败: " + ex + "\n" + log);
            }
            finally
            {
                // 完整日志落盘（console 接口只读首行，诊断靠文件）
                try
                {
                    var logPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Logs", "rerig_log.txt");
                    Directory.CreateDirectory(Directory.GetParent(logPath).FullName);
                    File.WriteAllText(logPath, log.ToString());
                }
                catch (System.Exception exF) { Debug.LogError("[RerigV2] 日志写盘失败: " + exF.Message); }
            }
        }

        static void RunInternal(StringBuilder log)
        {
            // ---------- 1. 解析 .anim 绑定姿势（t=0） ----------
            var text = File.ReadAllText(ANIM);
            string Section(string start, string end)
            {
                var i = text.IndexOf(start);
                var j = text.IndexOf(end, i + 1);
                if (i < 0 || j < 0 || j < i) throw new System.InvalidOperationException($"段落异常: {start} i={i} j={j}");
                return text.Substring(i, j - i);
            }
            var posSection = Section("m_PositionCurves:", "m_ScaleCurves:");
            var rotSection = Section("m_RotationCurves:", "m_CompressedRotationCurves:");
            var bindPos = new Dictionary<string, Vector3>();
            var bindRot = new Dictionary<string, Quaternion>();
            foreach (var raw in posSection.Split(new[] { "- curve:" }, System.StringSplitOptions.RemoveEmptyEntries))
                ParseVec(raw, bindPos, v => new Vector3(v[0], v[1], v[2]));
            foreach (var raw in rotSection.Split(new[] { "- curve:" }, System.StringSplitOptions.RemoveEmptyEntries))
                ParseVec(raw, bindRot, v => new Quaternion(v[0], v[1], v[2], v[3]).normalized);
            log.AppendLine($"1) 绑定姿势: pos={bindPos.Count} rot={bindRot.Count}");

            // ---------- 2. 临时场景：两个 FBX 实例同空间 ----------
            var tmpScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var mmdFbx = AssetDatabase.LoadAssetAtPath<GameObject>(MMD_FBX_PATH);
            var giFbx = AssetDatabase.LoadAssetAtPath<GameObject>(GI_FBX_PATH);
            var mmdInst = (GameObject)PrefabUtility.InstantiatePrefab(mmdFbx);
            var giInst = (GameObject)PrefabUtility.InstantiatePrefab(giFbx);
            mmdInst.transform.position = giInst.transform.position = Vector3.zero;

            // GI 骨架：找正确 Bip001（子含 Pelvis），覆写绑定姿势
            Transform rightBip = null;
            foreach (var t in giInst.GetComponentsInChildren<Transform>(true))
                if (t.name == "Bip001" && t.Find("Bip001 Pelvis") != null) { rightBip = t; break; }
            var modelNode = rightBip.parent;

            var boneByPath = new Dictionary<string, Transform>();
            void WalkPath(Transform t, string path)
            {
                boneByPath[path] = t;
                foreach (Transform ch in t) WalkPath(ch, path + "/" + ch.name);
            }
            boneByPath["Bip001"] = rightBip;
            foreach (Transform ch in rightBip) WalkPath(ch, "Bip001/" + ch.name);

            string PathOfBone(Transform t)
            {
                var names = new List<string>();
                var cur = t;
                while (cur != modelNode) { names.Add(cur.name); cur = cur.parent; }
                names.Reverse();
                return string.Join("/", names);
            }
            int overridden = 0;
            foreach (var kv in boneByPath)
            {
                var p = PathOfBone(kv.Value);
                if (bindPos.TryGetValue(p, out var lp) && bindRot.TryGetValue(p, out var lr))
                {
                    kv.Value.localPosition = lp;
                    kv.Value.localRotation = lr;
                    kv.Value.localScale = Vector3.one;
                    overridden++;
                }
            }
            log.AppendLine($"2) GI 骨架 {boneByPath.Count} 骨，绑定姿势覆写 {overridden}（差={boneByPath.Count - overridden} 为 Nub/无曲线骨，保持原局部）");

            // ---------- 3. 世界矩阵（同一场景） ----------
            var giWorld = boneByPath.Values.ToDictionary(b => b, b => b.localToWorldMatrix);

            // 诊断: GI 骨架世界空间分布（塌缩则覆写失败）
            {
                float minY = float.MaxValue, maxY = float.MinValue;
                foreach (var m in giWorld.Values)
                {
                    minY = Mathf.Min(minY, m.m13); maxY = Mathf.Max(maxY, m.m13);
                }
                var pelvisM = giWorld[rightBip.Find("Bip001 Pelvis")];
                log.AppendLine($"3) GI世界Y范围[{minY:F3},{maxY:F3}] Pelvis世界=({pelvisM.m03:F3},{pelvisM.m13:F3},{pelvisM.m23:F3})");
            }

            // ---------- 4. MMD 网格与骨骼 ----------
            var mmdSmr = mmdInst.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .OrderByDescending(r => r.sharedMesh != null ? r.sharedMesh.vertexCount : 0).First();
            var srcMesh = mmdSmr.sharedMesh;
            var meshL2W = mmdSmr.transform.localToWorldMatrix;
            var mmdBones = mmdSmr.bones;
            var mmdWorld = new Dictionary<Transform, Matrix4x4>();
            foreach (var b in mmdBones.Where(b => b != null).Distinct())
                mmdWorld[b] = b.localToWorldMatrix;
            log.AppendLine($"4) MMD 网格 {srcMesh.vertexCount} 顶点 / {mmdWorld.Count} 骨");

            // 诊断: MMD 世界空间 + meshL2W 细节
            {
                float minY = float.MaxValue, maxY = float.MinValue;
                foreach (var m in mmdWorld.Values)
                {
                    minY = Mathf.Min(minY, m.m13); maxY = Mathf.Max(maxY, m.m13);
                }
                var t = meshL2W.transpose; // 提取缩放（列长度）
                var sc = new Vector3(
                    new Vector4(meshL2W.m00, meshL2W.m10, meshL2W.m20, meshL2W.m30).magnitude,
                    new Vector4(meshL2W.m01, meshL2W.m11, meshL2W.m21, meshL2W.m31).magnitude,
                    new Vector4(meshL2W.m02, meshL2W.m12, meshL2W.m22, meshL2W.m32).magnitude);
                log.AppendLine($"4a) MMD骨世界Y范围[{minY:F3},{maxY:F3}] meshL2W平移=({meshL2W.m03:F3},{meshL2W.m13:F3},{meshL2W.m23:F3}) 缩放={sc} SMR节点={mmdSmr.transform.name} 父链={string.Join("/", mmdSmr.transform.parent != null && mmdSmr.transform.parent.parent != null ? new[] { mmdSmr.transform.parent.parent.name, mmdSmr.transform.parent.name } : new[] { mmdSmr.transform.parent != null ? mmdSmr.transform.parent.name : "none" })} 源mesh.bounds={srcMesh.bounds.size}");
            }

            // ---------- 5. 关键点刚体对齐（MMD 世界 → GI 世界） ----------
            Transform FindName(Transform root, string n)
            {
                foreach (Transform c in root)
                {
                    if (c.name == n) return c;
                    var r = FindName(c, n);
                    if (r != null) return r;
                }
                return null;
            }
            Vector3 GiPos(string n) => boneByPath.TryGetValue(n, out var b) ? new Vector3(giWorld[b].m03, giWorld[b].m13, giWorld[b].m23) : Vector3.zero;
            Vector3 MmdPos(string n)
            {
                var b = FindName(mmdInst.transform, n);
                return b != null && mmdWorld.ContainsKey(b) ? new Vector3(mmdWorld[b].m03, mmdWorld[b].m13, mmdWorld[b].m23) : Vector3.zero;
            }
            var gp = GiPos("Bip001/Bip001 Pelvis"); var gh = GiPos("Bip001/Bip001 Pelvis/Bip001 Spine/Bip001 Spine1/Bip001 Neck/Bip001 Head");
            var glh = GiPos("Bip001/Bip001 Pelvis/Bip001 Spine/Bip001 Spine1/Bip001 L Clavicle/Bip001 L UpperArm/Bip001 L Forearm/Bip001 L Hand");
            var grh = GiPos("Bip001/Bip001 Pelvis/Bip001 Spine/Bip001 Spine1/Bip001 R Clavicle/Bip001 R UpperArm/Bip001 R Forearm/Bip001 R Hand");
            var mp = MmdPos("\u8170"); var mh = MmdPos("\u982d");
            var mlh = MmdPos("\u624b\u9996.L"); var mrh = MmdPos("\u624b\u9996.R");
            if (gp == Vector3.zero || mp == Vector3.zero || mh == Vector3.zero)
                throw new System.InvalidOperationException($"关键点缺失 gp={gp} mp={mp} mh={mh}");

            // v4c 朝向: 回退手轴基（v3 实证: 目.L→+EyeBone 距离仅 8mm，眼睛对位=朝向正确）。
            // 脚尖参考不可用——派蒙看板待机悬空，脚不朝解剖前方（v4b 实测 GI 脚尖=(−0.90,0,0.43) 全错，误差 mean 0.067→0.159）
            Vector3 OrthonormalBasis(Vector3 pelvis, Vector3 head, Vector3 handL, Vector3 handR, out Vector3 yOut, out Vector3 xOut, out Vector3 zOut)
            {
                yOut = (head - pelvis).normalized;
                var xr = handL - handR;
                xOut = (xr - Vector3.Project(xr, yOut)).normalized;
                zOut = Vector3.Cross(xOut, yOut).normalized;
                return yOut;
            }
            OrthonormalBasis(gp, gh, glh, grh, out var gy, out var gx, out var gz);
            OrthonormalBasis(mp, mh, mlh, mrh, out var my, out var mx, out var mz);
            // R: mmd→gi（列基 mmd 行基 gi）
            var R = Quaternion.LookRotation(gz, gy) * Quaternion.Inverse(Quaternion.LookRotation(mz, my));
            float scale = Vector3.Distance(gp, gh) / Mathf.Max(Vector3.Distance(mp, mh), 1e-5f);
            var T = gp - scale * (R * mp);
            Matrix4x4 Align(int _) => Matrix4x4.TRS(T, R, Vector3.one * scale);
            var align = Align(0);
            // R 质量指标: 对齐后 MMD 眼骨到 GI 眼骨的距离（应 <2cm）
            var eyeL = MmdPos("\u76ee.L"); var eyeR = MmdPos("\u76ee.R");
            var geL = GiPos("Bip001/Bip001 Pelvis/Bip001 Spine/Bip001 Spine1/Bip001 Neck/Bip001 Head/+EyeBone L A01");
            var geR = GiPos("Bip001/Bip001 Pelvis/Bip001 Spine/Bip001 Spine1/Bip001 Neck/Bip001 Head/+EyeBone R A01");
            log.AppendLine($"5) 对齐: scale={scale:F3} rotY={Quaternion.Angle(Quaternion.identity, R):F1}\u00b0 T={T} 眼距L={Vector3.Distance(align.MultiplyPoint3x4(eyeL), geL):F3} 眼距R={Vector3.Distance(align.MultiplyPoint3x4(eyeR), geR):F3}");
            log.AppendLine($"5a) GI: 盆={gp} 头={gh} 手L={glh} 手R={grh}");
            log.AppendLine($"5b) MMD: 盆={mp} 头={mh} 手L={mlh} 手R={mrh}");
            log.AppendLine($"5c) 对齐检验(align·mmd盆≈gi盆?): {align.MultiplyPoint3x4(mp)} vs {gp}");

            // ---------- 6. 骨骼映射（投影后最近 GI 骨段） ----------
            var giSegs = new List<(Transform bone, Vector3 a, Vector3 b)>();
            foreach (var kv in boneByPath)
            {
                var m = giWorld[kv.Value];
                var head = new Vector3(m.m03, m.m13, m.m23);
                Vector3 tail;
                var child = kv.Value.Cast<Transform>().FirstOrDefault(c => boneByPath.ContainsValue(c));
                if (child != null) { var cm = giWorld[child]; tail = new Vector3(cm.m03, cm.m13, cm.m23); }
                else tail = head + (m.rotation * Vector3.up) * 0.05f;
                giSegs.Add((kv.Value, head, tail));
            }
            Transform NearestGi(Vector3 p)
            {
                Transform best = null; var bestD = float.MaxValue;
                foreach (var (b, a, t2) in giSegs)
                {
                    var ab = t2 - a;
                    var tt = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-10f));
                    var d = (p - (a + ab * tt)).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = b; }
                }
                return best;
            }
            // 骨映射 v3：同名 → MMD标准名显式表 → 祖先继承。
            // v2 教训：纯最近骨段匹配在四肢末端漂移 5-10cm（超过左右脚间距），
            // 主承载骨被映射到错误部位/错误侧（腕→Clavicle、手首→Finger1、つま先.L→R Toe0Nub），
            // 对应网格区域被逐骨刚体变换"传送"→ 碎片。
            var giByName = new Dictionary<string, Transform>();
            foreach (var b in boneByPath.Values)
                if (!giByName.ContainsKey(b.name)) giByName[b.name] = b;
            Transform GiGet(string name) => giByName.TryGetValue(name, out var t) ? t : null;
            Transform GiSide(string tmpl, string s) => s.Length == 0 ? null : GiGet(tmpl.Replace("{S}", s + " "));

            Transform MapMmd(string raw)
            {
                var n = raw;
                if (n.StartsWith("_shadow_")) n = n.Substring(8);
                else if (n.StartsWith("_dummy_")) n = n.Substring(7);
                if (n.StartsWith("+")) n = n.Substring(1);
                string s = n.EndsWith(".L") ? "L" : n.EndsWith(".R") ? "R" : "";
                if (s.Length > 0)
                {
                    // D骨（足D/ひざD/足首D = IK用副腿骨，常承载裙摆权重）优先
                    if (n.StartsWith("足首D")) return GiSide("Bip001 {S}Foot", s);
                    if (n.StartsWith("ひざ")) return GiSide("Bip001 {S}Calf", s);
                    if (n.StartsWith("足D")) return GiSide("Bip001 {S}Thigh", s);
                    if (n.StartsWith("肩")) return GiSide("Bip001 {S}Clavicle", s);
                    if (n.StartsWith("腕捩") || n.StartsWith("腕")) return GiSide("Bip001 {S}UpperArm", s);
                    if (n.StartsWith("ひじ")) return GiSide("Bip001 {S}Forearm", s);
                    if (n.StartsWith("手捩")) return GiSide("Bip001 {S}Forearm", s);
                    if (n.StartsWith("手首")) return GiSide("Bip001 {S}Hand", s) ?? GiSide("Bip001 {S}Forearm", s);
                    if (n.StartsWith("手先") || n.StartsWith("親指") || n.StartsWith("人指") || n.StartsWith("中指") || n.StartsWith("薬指") || n.StartsWith("小指"))
                        return GiSide("Bip001 {S}Hand", s) ?? GiSide("Bip001 {S}Finger1", s);
                    if (n == "ダミー.L" || n == "ダミー.R") return GiSide("Bip001 {S}Hand", s);
                    if (n.StartsWith("つま先") || n.StartsWith("足先EX")) return GiSide("Bip001 {S}Toe0", s);
                    if (n.StartsWith("足IK親") || n.StartsWith("足ＩＫ") || n.StartsWith("足首")) return GiSide("Bip001 {S}Foot", s);
                    if (n.StartsWith("足")) return GiSide("Bip001 {S}Thigh", s);
                    if (n.StartsWith("目")) return GiSide("+EyeBone {S}A01", s) ?? GiGet("Bip001 Head");
                    if (n.StartsWith("腰キャンセル")) return GiSide("Bip001 {S}Thigh", s);
                }
                switch (n)
                {
                    case "センター": case "腰": case "下半身": case "グルーブ": case "腰パーツ親":
                    case "全ての親": case "操作中心": return GiGet("Bip001 Pelvis");
                    case "上半身": return GiGet("Bip001 Spine");
                    case "上半身1": case "上半身2": return GiGet("Bip001 Spine1");
                    case "首": return GiGet("Bip001 Neck");
                    case "頭": case "両目": return GiGet("Bip001 Head");
                }
                return null;
            }

            var mmdAll = mmdBones.Where(b => b != null).Distinct().ToList();
            var mmd2gi = new Dictionary<Transform, Transform>();
            // ① 同名（+HairS/+EarB 等物理骨在 GI 侧同名存在，头发/耳物理动画直接可用）
            foreach (var mb in mmdAll)
                if (giByName.TryGetValue(mb.name, out var same)) mmd2gi[mb] = same;
            int byName = mmd2gi.Count;
            // ② MMD 标准名显式表
            foreach (var mb in mmdAll)
                if (!mmd2gi.ContainsKey(mb))
                {
                    var t = MapMmd(mb.name);
                    if (t != null) mmd2gi[mb] = t;
                }
            int byTable = mmd2gi.Count - byName;
            // ③ 其余（物理链/副本/IK骨）沿父链继承最近已映射祖先的目标
            var inheritMapped = new HashSet<Transform>();
            foreach (var mb in mmdAll)
            {
                if (mmd2gi.ContainsKey(mb)) continue;
                var p = mb.parent;
                Transform got = null;
                while (p != null && p != mmdInst.transform)
                {
                    if (mmd2gi.TryGetValue(p, out var t)) { got = t; break; }
                    got = MapMmd(p.name);
                    if (got != null) break;
                    p = p.parent;
                }
                mmd2gi[mb] = got ?? GiGet("Bip001 Pelvis");
                inheritMapped.Add(mb);
            }
            int byInherit = mmd2gi.Count - byName - byTable;
            log.AppendLine($"6) 骨映射v3 {mmd2gi.Count} 条: 同名={byName} 显式表={byTable} 祖先继承={byInherit}");
            // 诊断: 映射目标直方图（大量骨映射到同一 GI 骨 = 对齐/匹配坏了）
            foreach (var g in mmd2gi.Values.GroupBy(v => v).OrderByDescending(gr => gr.Count()).Take(6))
                log.AppendLine($"6a) 映射直方图: {g.Key.name} ← {g.Count()} 个MMD骨");
            // 诊断: 全量映射 dump + 匹配距离（人工审阅，日文骨名描述性强）
            {
                var rows = new List<(string mmd, string gi, float dist)>();
                foreach (var kv in mmd2gi)
                {
                    var pMmd = align.MultiplyPoint3x4(new Vector3(mmdWorld[kv.Key].m03, mmdWorld[kv.Key].m13, mmdWorld[kv.Key].m23));
                    var gPos = new Vector3(giWorld[kv.Value].m03, giWorld[kv.Value].m13, giWorld[kv.Value].m23);
                    rows.Add((kv.Key.name, kv.Value.name, Vector3.Distance(pMmd, gPos)));
                }
                foreach (var r in rows.OrderByDescending(r => r.dist).Take(30))
                    log.AppendLine($"6m) 最差匹配: {r.mmd} → {r.gi} 距离={r.dist:F3}m");
                log.AppendLine("6m) --- 全量映射 ---");
                foreach (var r in rows.OrderBy(r => r.mmd, System.StringComparer.Ordinal))
                    log.AppendLine($"6m) {r.mmd} → {r.gi} ({r.dist:F3})");
            }

            // ---------- 7. 顶点换姿势（世界→model 局部） ----------
            var L = modelNode.worldToLocalMatrix; // 网格将挂 model 节点下（恒等局部）
            var srcVerts = srcMesh.vertices;
            var srcNormals = srcMesh.normals;
            var boneWeights = srcMesh.GetAllBoneWeights();
            var bpm = srcMesh.GetBonesPerVertex();
            var boneIdx2T = new Transform[srcMesh.bindposes.Length];
            for (int i = 0; i < srcMesh.bindposes.Length && i < mmdBones.Length; i++) boneIdx2T[i] = mmdBones[i];

            var newVerts = new Vector3[srcVerts.Length];
            var newNormals = new Vector3[srcVerts.Length];
            var newGroups = new Dictionary<Transform, List<(int vi, float w)>>();
            // 关键: 换姿势用"解剖学方向传递"，不能用骨骼原始局部旋转。
            // MMD 骨世界矩阵带 ×100 缩放且旋转≈identity；GI Biped 骨旋转沿骨轴指向（Max 导出约定），
            // 直接 G·B⁻¹ 偏移被旋转到任意方向+缩 100 倍 → 塌缩+断肢（"只剩骨头/砍碎尸体"两个症状同源）。
            // 正确传递: Q = FromToRotation(R·mmdDir, giDir)·R（骨方向=父→本骨向量，解剖语义稳定）；
            // xform·p = gPos + s·Q·(p − bPos)；祖先继承骨（披风物理链）不做方向吸附，Q=R 随全局。
            Vector3 POf(Matrix4x4 m) => new Vector3(m.m03, m.m13, m.m23);
            // 骨方向: 肢体链骨用"父→主子骨"贯穿段方向（肩→肘=上臂方向）；
            // 短根骨（大腿根/上臂根离父仅几 cm，self−parent 是横向噪声，v4c 实测足.L 被甩 162°）；
            // 中枢骨（腰/脊椎/头）与短附属骨（眼/发/耳）保持 self−parent。
            bool IsGiLimb(string n) =>
                n.Contains("Thigh") || n.Contains("Calf") || n.Contains("Foot") || n.Contains("Toe")
                || n.Contains("UpperArm") || n.Contains("Forearm") || n.Contains("Hand")
                || n.Contains("Finger") || n.Contains("Clavicle") || n.StartsWith("DMZ");
            bool IsMmdLimb(string n) =>
                n.StartsWith("足") || n.StartsWith("ひざ") || n.StartsWith("つま先")
                || n.StartsWith("腕") || n.StartsWith("ひじ") || n.StartsWith("手首") || n.StartsWith("手捩")
                || n.StartsWith("肩") || n.StartsWith("手先") || n.StartsWith("ダミー")
                || n.StartsWith("人指") || n.StartsWith("中指") || n.StartsWith("薬指") || n.StartsWith("小指") || n.StartsWith("親指")
                || n.Contains("先EX");
            Vector3 GiDirOf(Transform gb)
            {
                var p = gb.parent;
                while (p != null && p != modelNode && !giWorld.ContainsKey(p)) p = p.parent;
                var pOk = p != null && p != modelNode && giWorld.ContainsKey(p);
                var selfDir = pOk ? POf(giWorld[gb]) - POf(giWorld[p]) : Vector3.zero;
                if (IsGiLimb(gb.name) && pOk)
                {
                    // 贯穿段: 父骨→本骨主子骨
                    if (GiChildDirOf(gb, out var ch) is var cd && ch != null && cd.HasValue)
                    {
                        var seg = POf(giWorld[ch]) - POf(giWorld[p]);
                        if (seg.sqrMagnitude > 1e-8f) return seg.normalized;
                    }
                }
                if (selfDir.sqrMagnitude > 1e-8f) return selfDir.normalized;
                return Vector3.up;
            }
            Vector3 MmdDirOf(Transform mb)
            {
                var p = mb.parent;
                while (p != null && p != mmdInst.transform && !mmdWorld.ContainsKey(p)) p = p.parent;
                var pOk = p != null && p != mmdInst.transform && mmdWorld.ContainsKey(p);
                var selfDir = pOk ? POf(mmdWorld[mb]) - POf(mmdWorld[p]) : Vector3.zero;
                if (IsMmdLimb(mb.name) && pOk)
                {
                    if (MmdChildDirOf(mb, out var ch) is var cd && ch != null && cd.HasValue)
                    {
                        var seg = POf(mmdWorld[ch]) - POf(mmdWorld[p]);
                        if (seg.sqrMagnitude > 1e-8f) return seg.normalized;
                    }
                }
                if (selfDir.sqrMagnitude > 1e-8f) return selfDir.normalized;
                return Vector3.up;
            }
            Vector3? MmdChildDirOf(Transform mb, out Transform childBone)
            {
                // 主子骨 = 偏移最大的直接子骨（跳过零偏移副本骨）
                childBone = null; float bestD = 1e-6f;
                foreach (Transform c in mb)
                {
                    if (!mmdWorld.ContainsKey(c)) continue;
                    var d = (POf(mmdWorld[c]) - POf(mmdWorld[mb])).sqrMagnitude;
                    if (d > bestD) { bestD = d; childBone = c; }
                }
                return childBone != null ? (POf(mmdWorld[childBone]) - POf(mmdWorld[mb])).normalized : (Vector3?)null;
            }
            Vector3? GiChildDirOf(Transform gb, out Transform childBone)
            {
                childBone = null; float bestD = 1e-8f;
                foreach (Transform c in gb)
                {
                    if (!giWorld.ContainsKey(c)) continue;
                    var d = (POf(giWorld[c]) - POf(giWorld[gb])).sqrMagnitude;
                    if (d > bestD) { bestD = d; childBone = c; }
                }
                return childBone != null ? (POf(giWorld[childBone]) - POf(giWorld[gb])).normalized : (Vector3?)null;
            }
            var xferByMmd = new Dictionary<Transform, Matrix4x4>();
            var giDirCache = new Dictionary<Transform, Vector3>();
            var mmdDirCache = new Dictionary<Transform, Vector3>();
            int dirSnap = 0, dirGlobal = 0, rollFix = 0;
            var rollLog = new List<(string n, float a)>();
            foreach (var kv in mmd2gi)
            {
                var mb = kv.Key; var gb = kv.Value;
                Quaternion Q = R;
                if (!inheritMapped.Contains(mb))
                {
                    if (!giDirCache.TryGetValue(gb, out var dg)) giDirCache[gb] = dg = GiDirOf(gb);
                    if (!mmdDirCache.TryGetValue(mb, out var dm)) mmdDirCache[mb] = dm = MmdDirOf(mb);
                    var dmA = R * dm;
                    if ((dg - dmA).sqrMagnitude > 1e-10f && (dg + dmA).sqrMagnitude > 1e-10f)
                    {
                        Q = Quaternion.FromToRotation(dmA, dg) * R;
                        dirSnap++;
                    }
                    else dirGlobal++;
                    // 滚转修正: 仅当"本骨主子骨"在两侧也是对应映射对时（如 上臂→肘→前臂 链），
                    // 用弯平面方向钉死绕骨轴滚转。多对一映射（手指→Hand、腰→Pelvis）子骨不对应，
                    // 强行滚转会把簇甩飞（v4a 教训: 指骨滚 150°+、腰滚 166°，误差 mean 0.067→0.163）
                    var cm = MmdChildDirOf(mb, out var mcBone);
                    var cg = GiChildDirOf(gb, out var gcBone);
                    bool pairOk = cm.HasValue && cg.HasValue
                        && mcBone != null && gcBone != null
                        && mmd2gi.TryGetValue(mcBone, out var mcTarget) && mcTarget == gcBone;
                    if (pairOk)
                    {
                        var cmW = Q * cm.Value;
                        var cmP = cmW - Vector3.Project(cmW, dg);
                        var cgP = cg.Value - Vector3.Project(cg.Value, dg);
                        if (cmP.sqrMagnitude > 1e-6f && cgP.sqrMagnitude > 1e-6f
                            && (cmP - cgP).sqrMagnitude > 1e-10f && (cmP + cgP).sqrMagnitude > 1e-10f)
                        {
                            Q = Quaternion.FromToRotation(cmP.normalized, cgP.normalized) * Q;
                            rollFix++;
                        }
                    }
                    rollLog.Add((mb.name, Quaternion.Angle(Q, R)));
                }
                else dirGlobal++;
                var bPos = POf(mmdWorld[mb]);
                var gPos = POf(giWorld[gb]);
                xferByMmd[mb] = Matrix4x4.TRS(gPos - scale * (Q * bPos), Q, Vector3.one * scale);
            }
            log.AppendLine($"7pre) 换姿势矩阵: 方向吸附={dirSnap} 滚转修正(配对子骨)={rollFix} 全局姿态={dirGlobal}");
            foreach (var r in rollLog.OrderByDescending(x => x.a).Take(10))
                log.AppendLine($"7pre) 滚转Top: {r.n} ΔR={r.a:F1}\u00b0");
            // 诊断: 顶点重姿势误差 = |vOut − align·vWorld|（好映射下 ≈0；被传送的碎片区域会很大）
            float errMax = 0, errSum = 0; int err5 = 0, err20 = 0;
            var errByDomBone = new Dictionary<string, float>();
            var weightByBone = new Dictionary<string, float>();
            int wi = 0;
            for (int vi = 0; vi < srcVerts.Length; vi++)
            {
                var vWorld = meshL2W.MultiplyPoint3x4(srcVerts[vi]);
                var nWorld = meshL2W.MultiplyVector(srcNormals[vi]);
                var vOut = Vector3.zero; var nOut = Vector3.zero;
                var topPairs = new List<(Transform giBone, float w)>();
                string domBone = "?"; float domW = 0;
                int n = (int)bpm[vi];
                for (int k = 0; k < n; k++)
                {
                    var bw = boneWeights[wi++];
                    var mb = boneIdx2T[bw.boneIndex];
                    if (mb == null) continue;
                    if (!mmd2gi.TryGetValue(mb, out var gb) || gb == null) continue;
                    var xform = xferByMmd[mb];
                    vOut += xform.MultiplyPoint3x4(vWorld) * bw.weight;
                    nOut += xform.MultiplyVector(nWorld) * bw.weight;
                    topPairs.Add((gb, bw.weight));
                    if (bw.weight > domW) { domW = bw.weight; domBone = mb.name; }
                }
                weightByBone[domBone] = weightByBone.TryGetValue(domBone, out var wAcc) ? wAcc + domW : domW;
                var errVi = Vector3.Distance(vOut, align.MultiplyPoint3x4(vWorld));
                errMax = Mathf.Max(errMax, errVi); errSum += errVi;
                if (errVi > 0.05f) err5++;
                if (errVi > 0.2f) err20++;
                errByDomBone[domBone] = Mathf.Max(errByDomBone.TryGetValue(domBone, out var eAcc) ? eAcc : 0, errVi);
                if (topPairs.Count == 0)
                {
                    var fallback = NearestGi(align.MultiplyPoint3x4(vWorld));
                    vOut = align.MultiplyPoint3x4(vWorld); nOut = align.MultiplyVector(nWorld);
                    topPairs.Add((fallback, 1f));
                }
                newVerts[vi] = L.MultiplyPoint3x4(vOut);
                newNormals[vi] = L.MultiplyVector(nOut).normalized;
                foreach (var g in topPairs.GroupBy(p => p.giBone))
                {
                    if (!newGroups.ContainsKey(g.Key)) newGroups[g.Key] = new List<(int, float)>();
                    newGroups[g.Key].Add((vi, g.Sum(p => p.w)));
                }
            }
            log.AppendLine($"7) 换姿势完成，新顶点组 {newGroups.Count}");
            // 诊断: 误差汇总（碎片定位）
            log.AppendLine($"7c) 顶点误差: max={errMax:F3}m mean={errSum / srcVerts.Length:F4}m >5cm={err5} >20cm={err20}（/ {srcVerts.Length}）");
            foreach (var kv in errByDomBone.OrderByDescending(k => k.Value).Take(15))
            {
                weightByBone.TryGetValue(kv.Key, out var w2);
                log.AppendLine($"7c) 误差Top: {kv.Key} maxErr={kv.Value:F3}m 总权重={w2:F0}");
            }
            // 诊断: newVerts 直接 bounds（L 之前/之后各一份，定位塌缩发生层）
            {
                var bmin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                var bmax = new Vector3(float.MinValue, float.MinValue, float.MinValue);
                foreach (var v in newVerts)
                {
                    bmin = Vector3.Min(bmin, v); bmax = Vector3.Max(bmax, v);
                }
                log.AppendLine($"7a) newVerts bounds(L后): center={(bmin + bmax) / 2} size={bmax - bmin}");
                bmin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                bmax = new Vector3(float.MinValue, float.MinValue, float.MinValue);
                foreach (var v in srcVerts)
                {
                    var vw = meshL2W.MultiplyPoint3x4(v);
                    bmin = Vector3.Min(bmin, vw); bmax = Vector3.Max(bmax, vw);
                }
                log.AppendLine($"7b) vWorld bounds(换姿势前): center={(bmin + bmax) / 2} size={bmax - bmin}");
            }
            // 顶点诊断采样（前 3 个 + 中间 1 个）
            wi = 0;
            int[] probes = { 0, 1, 2, srcVerts.Length / 2 };
            var probedVerts = new HashSet<int>(probes);
            for (int vi = 0; vi < srcVerts.Length; vi++)
            {
                int n = (int)bpm[vi];
                if (probedVerts.Contains(vi))
                {
                    var vWorld = meshL2W.MultiplyPoint3x4(srcVerts[vi]);
                    var desc = new StringBuilder();
                    for (int k = 0; k < n; k++)
                    {
                        var bw = boneWeights[wi + k];
                        var mb = boneIdx2T[bw.boneIndex];
                        if (mb == null) { desc.Append("[null骨]"); continue; }
                        var gb = mmd2gi[mb];
                        var bPos = new Vector3(mmdWorld[mb].m03, mmdWorld[mb].m13, mmdWorld[mb].m23);
                        var gPos = gb != null ? new Vector3(giWorld[gb].m03, giWorld[gb].m13, giWorld[gb].m23) : Vector3.zero;
                        desc.Append($"[{mb.name}→{(gb != null ? gb.name : "none")} w={bw.weight:F2} B={bPos.ToString("F2")} G={gPos.ToString("F2")}] ");
                    }
                    log.AppendLine($"   v[{vi}] world={vWorld} → out={newVerts[vi]}  {desc}");
                }
                wi += n;
            }

            // ---------- 8. Mesh 资产 + SMR（挂 GI 实例 model 节点下） ----------
            var mesh = Object.Instantiate(srcMesh);
            mesh.name = "Paimon_GIRigMesh";
            mesh.vertices = newVerts;
            mesh.normals = newNormals;
            // 关键: Instantiate 克隆了源 mesh 的塌缩 bounds（MMD 资产空间 0.01 单位），
            // vertices 赋值不会自动重算，必须显式 RecalculateBounds，否则视锥裁剪异常
            mesh.RecalculateBounds();
            if (mesh.bounds.size.y < 0.1f)
                throw new System.InvalidOperationException($"产物自检失败: mesh.bounds={mesh.bounds.size}（顶点疑似塌缩，勿入库）");
            if (AssetDatabase.LoadAssetAtPath<Mesh>(MESH_ASSET) != null) AssetDatabase.DeleteAsset(MESH_ASSET);
            AssetDatabase.CreateAsset(mesh, MESH_ASSET);

            var boneList = newGroups.Keys.ToList();
            var bps = new Matrix4x4[boneList.Count];
            for (int g = 0; g < boneList.Count; g++)
                bps[g] = (L * giWorld[boneList[g]]).inverse; // bindpose = (L·G)^-1
            var vertWeights = new Dictionary<int, List<(int, float)>>();
            for (int g = 0; g < boneList.Count; g++)
                foreach (var (vi, w) in newGroups[boneList[g]])
                {
                    if (!vertWeights.TryGetValue(vi, out var l)) vertWeights[vi] = l = new List<(int, float)>();
                    l.Add((g, w));
                }
            var bwList = new List<BoneWeight1>();
            var perVertex = new List<byte>();
            for (int vi = 0; vi < mesh.vertexCount; vi++)
            {
                var l = vertWeights.TryGetValue(vi, out var lw) ? lw : new List<(int, float)>();
                int cnt = Mathf.Min(l.Count, 4);
                foreach (var pair in l.OrderByDescending(x => x.Item2).Take(cnt))
                    bwList.Add(new BoneWeight1 { boneIndex = pair.Item1, weight = pair.Item2 });
                if (cnt == 0) { bwList.Add(new BoneWeight1 { boneIndex = 0, weight = 1f }); cnt = 1; }
                perVertex.Add((byte)cnt);
            }
            using (var naP = new Unity.Collections.NativeArray<byte>(perVertex.ToArray(), Unity.Collections.Allocator.Temp))
            using (var naW = new Unity.Collections.NativeArray<BoneWeight1>(bwList.ToArray(), Unity.Collections.Allocator.Temp))
                mesh.SetBoneWeights(naP, naW);
            mesh.bindposes = bps;

            var meshGo = new GameObject("Paimon_GIRigMesh");
            meshGo.transform.SetParent(modelNode, false);
            var smr = meshGo.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.bones = boneList.ToArray();               // 场景实例骨骼——存 prefab 时自动重映射
            smr.rootBone = rightBip;
            smr.sharedMaterials = mmdSmr.sharedMaterials; // 8 材质 1:1
            smr.updateWhenOffscreen = true;
            log.AppendLine($"8a) mesh.bounds={mesh.bounds.size} SMR骨头={boneList.Count} 材质={smr.sharedMaterials.Length}");

            // Animation（model 节点，自动播 legacy）
            var clipLegacy = AssetDatabase.LoadAssetAtPath<AnimationClip>(ANIM_LEGACY);
            if (clipLegacy == null)
            {
                var orig = AssetDatabase.LoadAssetAtPath<AnimationClip>(ANIM);
                clipLegacy = Object.Instantiate(orig);
                clipLegacy.legacy = true;
                AssetDatabase.CreateAsset(clipLegacy, ANIM_LEGACY);
            }
            var oldAnim = modelNode.GetComponent<Animation>();
            if (oldAnim != null) Object.DestroyImmediate(oldAnim);
            var anim = modelNode.gameObject.AddComponent<Animation>();
            clipLegacy.wrapMode = WrapMode.Loop;
            anim.clip = clipLegacy;
            anim.playAutomatically = true;
            anim.cullingType = AnimationCullingType.AlwaysAnimate;

            // ---------- 9. 存 prefab + 重建测试场景 ----------
            PrefabUtility.SaveAsPrefabAsset(giInst, PREFAB_OUT);
            log.AppendLine($"8) prefab 已存: {PREFAB_OUT}");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var lightGo = new GameObject("MainLight");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.2f;
            lightGo.transform.rotation = Quaternion.Euler(35, -155, 20);

            var rig = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_OUT));
            rig.name = GO_NAME;
            // 摆到原点：把 model 节点链的世界偏移归零（根反移）
            rig.transform.position = Vector3.zero;
            var camGo = new GameObject("MainCamera"); camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.15f, 0.15f, 0.18f);
            var rigSmr = rig.GetComponentInChildren<SkinnedMeshRenderer>();
            var c = rigSmr.bounds.center;
            var s = rigSmr.bounds.size;
            camGo.transform.position = c + new Vector3(0, 0.05f, s.z + 1.1f);
            camGo.transform.LookAt(c);
            EditorSceneManager.SaveScene(scene, TEST_SCENE);
            AssetDatabase.SaveAssets();
            log.AppendLine($"9) 测试场景已建: {TEST_SCENE}（模型 bounds {s}）");
        }

        static void ParseVec<T>(string raw, Dictionary<string, T> dict, System.Func<float[], T> make)
        {
            var pi = raw.IndexOf("path: ");
            if (pi < 0) return;
            var ls = raw.IndexOf("\n", pi);
            var path = raw.Substring(pi + 6, ls - pi - 6).Trim();
            if (long.TryParse(path, out _)) return;
            var vi = raw.IndexOf("value: {");
            if (vi < 0) return;
            var vs = raw.Substring(vi + 8);
            var ve = vs.IndexOf("}");
            var parts = vs.Substring(0, ve).Split(',')
                .Select(s => float.Parse(s.Trim().Split(':')[1], CultureInfo.InvariantCulture)).ToArray();
            dict[path] = make(parts);
        }
    }
}
