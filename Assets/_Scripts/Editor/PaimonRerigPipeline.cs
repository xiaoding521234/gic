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

            // 三轴基构造旋转（Y=盆→头，X=手L-手R 正交化，Z=叉积），缩放=身高比
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
            log.AppendLine($"5) 对齐: scale={scale:F3} rotY={Quaternion.Angle(Quaternion.identity, R):F1}\u00b0 T={T}");

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
            var mmd2gi = new Dictionary<Transform, Transform>();
            foreach (var mb in mmdBones.Where(b => b != null).Distinct())
            {
                var pMmd = new Vector3(mmdWorld[mb].m03, mmdWorld[mb].m13, mmdWorld[mb].m23);
                mmd2gi[mb] = NearestGi(align.MultiplyPoint3x4(pMmd));
            }
            log.AppendLine($"6) 骨映射 {mmd2gi.Count} 条（\u8170\u2192{(mmd2gi.FirstOrDefault(k => k.Key.name == "\u8170").Value?.name)}, \u982d\u2192{(mmd2gi.FirstOrDefault(k => k.Key.name == "\u982d").Value?.name)}）");

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
            int wi = 0;
            for (int vi = 0; vi < srcVerts.Length; vi++)
            {
                var vWorld = meshL2W.MultiplyPoint3x4(srcVerts[vi]);
                var nWorld = meshL2W.MultiplyVector(srcNormals[vi]);
                var vOut = Vector3.zero; var nOut = Vector3.zero;
                var topPairs = new List<(Transform giBone, float w)>();
                int n = (int)bpm[vi];
                for (int k = 0; k < n; k++)
                {
                    var bw = boneWeights[wi++];
                    var mb = boneIdx2T[bw.boneIndex];
                    if (mb == null) continue;
                    if (!mmd2gi.TryGetValue(mb, out var gb) || gb == null) continue;
                    var xform = giWorld[gb] * mmdWorld[mb].inverse;
                    vOut += xform.MultiplyPoint3x4(vWorld) * bw.weight;
                    nOut += xform.MultiplyVector(nWorld) * bw.weight;
                    topPairs.Add((gb, bw.weight));
                }
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
                        desc.Append($"[{mb.name}→{(gb != null ? gb.name : "无")} w={bw.weight:F2} B={bPos.y:F2} G={gPos.y:F2}] ");
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
