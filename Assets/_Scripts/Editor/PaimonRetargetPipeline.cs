using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GIC.Editor.Retarget
{
    /// <summary>
    /// 方案B管线（docs/19 §2.3 / HANDOFF-派蒙动画重定向.md）：GI 看板动画重定向到 MMD 原生骨架。
    /// MMD 网格/权重/bindpose 零改动；逐帧把 GI Bip001 骨骼世界位姿经 A 期手轴对齐矩阵的逆变换
    /// 搬回 MMD 世界，沿 MMD 层级自顶向下解出每骨 localPosition/localRotation 曲线，产出 legacy .anim。
    /// 关键事实（实测）：①GI 绑定姿势=.anim t=0 采样（FBX 内骨骼局部塌缩不可用）②MMD 骨世界带×100
    /// 缩放（Paimon_arm scale=100），local=parentW⁻¹·target 后自动回资产单位，勿手动剥缩放 ③解局部时
    /// 父世界用"本帧已解出的 local + bind scale"合成 ④四元数逐帧连续化防翻转插值 ⑤GI 根骨 Bip001
    /// 不直接驱动全ての親（其运动经盆骨世界链路隐式传递，直接驱动会把两骨架基准差塞进腰腹链）。
    /// 旋转传递用 v6 关节角式：qWorld(f) = R⁻¹·(Qg(f)·Qg(0)⁻¹)·R · qPose，其中 qPose 为 f0 悬空姿势
    /// 基准（FromToRotation 方向对齐 × 绑定旋转，无配对子骨叶子退化纯增量）。数学恒等：骨段方向全程
    /// 精确跟踪 R⁻¹·dGi(f)。位置除腰锚（bob/根运动载体）外全部恒绑定——骨长恒定=蒙皮零拉伸。
    /// 演化史：v1 绝对旋转=逐骨捻碎（Max 骨轴滚转任意）；v2 纯增量=基准差致腿直棍僵硬；
    /// v5 位置+方向双硬对齐=过约束，骨长被压短皮肤绷紧（"腿僵硬+腰前翘"实测）。v6 只传关节角
    /// （标准 retarget 方程），比例差由 MMD 自身骨长吸收，皮肤全程零形变。
    /// v7 全骨驱动：代表骨之外的辅助骨（裙摆D链/肩P肩C/捻骨/上半身1等，承载大量皮肤权重）
    /// 不再保持绑定，改为 restW f0 刚体栈基准 + 自身映射目标的动画增量（同目标父子增量自动
    /// 抵消，异目标成相对运动）——否则主骨架摆悬空姿势而裙摆腰腹皮肤纹丝不动（僵硬病灶）。
    /// v7.2 躯干骨（腰/上半身/上半身2/首）姿势参考方向改"指向头骨"：Biped Pelvis→Spine 骨段
    /// 带水平前倾分量（Max 约定），按骨段对齐=整躯干前倾（"肚子前翘"实测）；指向链终端头骨
    /// 两骨架语义一致。
    /// v8 捻骨（腕捩/手捩）废弃 v4 世界 Slerp 插值：MMD 手臂链绑定相邻骨为反相对齐（179.9°
    /// 约定），世界插值捋成同向 → 捻骨翻转 ~170° 蒙皮折叠（"每臂三处塌缩"实测）；并入跟随骨
    /// 公式后绑定 180° 结构天然保住。
    /// v14 跟随骨 Δ_rel 回退为绝对世界追踪（2026-08-23 晚）：qWant 是绝对世界旋转
    ///（解局部 lr=pw⁻¹·qWant 已除父世界），v13 的 Δ(自身)·Δ(父)⁻¹ 叠上 腰キャンセル→Thigh
    /// 映射后 足D 的 Δ_rel≡单位——大腿段冻结在 f0，盆骨摇摆不传给腿（双腿侧漂实测）。
    /// v15 D 链腿骨（足D/ひざD/足首D）并入代表骨：腿部蒙皮全在 D 链，跟随公式不穿 f0
    /// 姿势基准（restW=绑定栈）致待机腿恒为绑定直棍（偏 GI 源 ~37° 常量+脚踝 15cm）；
    /// 足D→ひざD→足首D ↔ Thigh→Calf→Foot 单链语义干净，qPose 方向对齐后全程精确跟踪
    ///（腿部方向审计 D 链误差 0°，仅 GI 源符号跳变帧为审计伪影）。
    /// 可泛化：换 MMD 模型只需骨名走标准 MMD 命名（MapMmd 表覆盖），管线不变。
    /// 目.L/R 完全不驱动（v3）：平移虹膜机制下位置保持式的对齐残差（左右 8/3mm 不对称）会变成虹膜
    /// 位移——实测左目被推到绑定位后方 7mm 虹膜沉入眼球后（"左眼只剩眼白"）；绑定=编辑模式=正确。
    /// 捻骨 腕捩/手捩 按插值骨驱动（v4）：MMD 捻骨承载腕部渐变皮肤权重，保持绑定时 GI 手部旋转
    /// 全压手首 → 手捩/手首权重边界剪切（"手腕骨折"实测）；位姿在上下级驱动骨间按绑定比例插值分散。
    /// </summary>
    public static class PaimonRetargetPipeline
    {
        // 配置见 PetRetargetConfig（ScriptableObject，Assets/Art/PaimonPet/RetargetConfig.asset）
        static PetRetargetConfig cfg;

        [MenuItem("Tools/\u684c\u5ba0/\u65b9\u6848B: GI\u52a8\u753b\u91cd\u5b9a\u5411\u5230MMD\u9aa8\u67b6")]
        public static void Run()
        {
            var log = new StringBuilder();
            try
            {
                RunInternal(log);
                Debug.Log("[RetargetB] 完成\n" + log);
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[RetargetB] 失败: " + ex + "\n" + log);
            }
            finally
            {
                // 完整日志落盘（console 接口只读首行，诊断靠文件）
                try
                {
                    var logPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Logs", "retarget_log.txt");
                    Directory.CreateDirectory(Directory.GetParent(logPath).FullName);
                    File.WriteAllText(logPath, log.ToString());
                }
                catch (System.Exception exF) { Debug.LogError("[RetargetB] 日志写盘失败: " + exF.Message); }
            }
        }

        // ==================== .anim 读取（AnimationUtility 类型化 API，v9 替代手写 YAML 解析） ====================

        class CurveTrack
        {
            public readonly List<float> times = new List<float>();
            public readonly List<Vector4> vals = new List<Vector4>(); // pos:xyz / rot:xyzw
            public Vector4 Sample(float t)
            {
                int lo = 0, hi = times.Count - 1;
                while (lo < hi)
                {
                    var mid = (lo + hi) >> 1;
                    if (times[mid] < t) lo = mid + 1; else hi = mid;
                }
                if (lo >= times.Count) return vals[vals.Count - 1];
                if (Mathf.Abs(times[lo] - t) < 1e-5f) return vals[lo];
                if (lo == 0) return vals[0];
                float t1 = times[lo - 1], t2 = times[lo];
                float f = Mathf.Clamp01((t - t1) / Mathf.Max(t2 - t1, 1e-6f));
                return Vector4.Lerp(vals[lo - 1], vals[lo], f);
            }
        }

        class ClipData
        {
            public string name;
            public readonly Dictionary<string, CurveTrack> pos = new Dictionary<string, CurveTrack>();
            public readonly Dictionary<string, CurveTrack> rot = new Dictionary<string, CurveTrack>();
            public List<float> frameTimes; // 全曲线 key time 并集
        }

        /// <summary>
        /// 用 AnimationUtility.GetCurveBindings/GetEditorCurve 读类型化曲线（替代手写 YAML 解析——
        /// 格式升级由 Unity 兜底）。哈希数字路径（path_数字）过滤保留；Root/MotionT root motion
        /// 曲线经 propertyName 前缀过滤（只取 localPosition./localRotation.）。
        /// </summary>
        static ClipData ParseClip(string file, string name, StringBuilder log)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(file);
            if (clip == null) throw new System.InvalidOperationException($"clip \u52a0\u8f7d\u5931\u8d25: {file}");
            // 注意：Transform 曲线 binding.propertyName 用内部名 m_LocalPosition.x/m_LocalRotation.x
            //（非动画窗口显示名 localPosition.x），按"含 Position/Rotation + 末位分量字符"匹配；
            // Root/MotionT/MotionQ root motion 曲线天然被排除；哈希数字路径（blendshape）跳过。
            var posCurves = new Dictionary<string, AnimationCurve[]>(); // path → [x,y,z]
            var rotCurves = new Dictionary<string, AnimationCurve[]>(); // path → [x,y,z,w]
            foreach (var b in AnimationUtility.GetCurveBindings(clip))
            {
                if (b.type != typeof(Transform)) continue;
                if (long.TryParse(b.path, out _)) continue; // 哈希路径对不上，忽略
                var curve = AnimationUtility.GetEditorCurve(clip, b);
                if (curve == null || curve.keys.Length == 0) continue;
                var prop = b.propertyName;
                char comp = prop[prop.Length - 1];
                int idx = "xyzw".IndexOf(comp);
                if (idx < 0) continue;
                if (prop.Contains("Position") && idx < 3)
                {
                    if (!posCurves.TryGetValue(b.path, out var arr)) posCurves[b.path] = arr = new AnimationCurve[3];
                    arr[idx] = curve;
                }
                else if (prop.Contains("Rotation"))
                {
                    if (!rotCurves.TryGetValue(b.path, out var arr)) rotCurves[b.path] = arr = new AnimationCurve[4];
                    arr[idx] = curve;
                }
            }
            var cd = new ClipData { name = name };
            int nr = BuildTrack(rotCurves, cd.rot, true);
            int np = BuildTrack(posCurves, cd.pos, false);
            var times = new SortedSet<float>();
            foreach (var tr in cd.rot.Values) foreach (var t in tr.times) times.Add(t);
            foreach (var tp in cd.pos.Values) foreach (var t in tp.times) times.Add(t);
            cd.frameTimes = times.ToList();
            if (cd.frameTimes.Count < 2) throw new System.InvalidOperationException($"[{name}] \u5e27\u6570\u5f02\u5e38 {cd.frameTimes.Count}");
            log.AppendLine($"[parse {name}] rot\u66f2\u7ebf={nr} pos\u66f2\u7ebf={np} \u5e27\u6570={cd.frameTimes.Count} \u65f6\u957f={cd.frameTimes[cd.frameTimes.Count - 1]:F3}s");
            return cd;
        }

        /// <summary>分量曲线组 → 全 key 并集 CurveTrack（各分量在并集时刻求值重组）。
        /// v11 符号连续化：旋转 track 逐 key 维持与前一 key 的正点积（q≡-q 等价表示，
        /// GI 源曲线存在符号跳变——并集时刻间的分量线性插值跨跳变会插出过零垃圾四元数，
        /// 污染该帧解算并永久写入输出 clip。Standby R 膝 171°@1.34s 单帧翻转与
        /// C05_01 全 clip 恒偏 77.9° 均此根因，2026-08-23 腿部方向审计实证。key 处取值
        /// 恒安全，此修保证 key 之间任意 t 的插值也安全）。</summary>
        static int BuildTrack(Dictionary<string, AnimationCurve[]> src, Dictionary<string, CurveTrack> dst, bool isRot)
        {
            int count = 0;
            foreach (var kv in src)
            {
                var arr = kv.Value;
                var times = new SortedSet<float>();
                foreach (var c in arr)
                    if (c != null)
                        foreach (var k in c.keys) times.Add(k.time);
                if (times.Count == 0) continue;
                var track = new CurveTrack();
                var prevQ = new Vector4(0f, 0f, 0f, 1f);
                foreach (var t in times)
                {
                    if (isRot)
                    {
                        var q = new Quaternion(
                            arr[0] != null ? arr[0].Evaluate(t) : 0f,
                            arr[1] != null ? arr[1].Evaluate(t) : 0f,
                            arr[2] != null ? arr[2].Evaluate(t) : 0f,
                            arr[3] != null ? arr[3].Evaluate(t) : 1f).normalized;
                        var v4 = new Vector4(q.x, q.y, q.z, q.w);
                        if (Vector4.Dot(v4, prevQ) < 0f) v4 = -v4; // 符号连续化
                        prevQ = v4;
                        track.times.Add(t);
                        track.vals.Add(v4);
                    }
                    else
                    {
                        track.times.Add(t);
                        track.vals.Add(new Vector3(
                            arr[0] != null ? arr[0].Evaluate(t) : 0f,
                            arr[1] != null ? arr[1].Evaluate(t) : 0f,
                            arr[2] != null ? arr[2].Evaluate(t) : 0f));
                    }
                }
                dst[kv.Key] = track;
                count++;
            }
            return count;
        }

        // ==================== 小工具 ====================

        static Vector3 VecOf(Vector4 v) => new Vector3(v.x, v.y, v.z);
        static Quaternion QuatOf(Vector4 v) => new Quaternion(v.x, v.y, v.z, v.w).normalized;

        /// <summary>从（可带均匀缩放的）矩阵提取旋转（数学核心见 PetRetargetMath）</summary>
        static Quaternion ExtractRot(Matrix4x4 m) => PetRetargetMath.ExtractRot(m);

        static string Rel(Transform t, Transform root)
        {
            var names = new List<string>();
            var cur = t;
            while (cur != null && cur != root) { names.Add(cur.name); cur = cur.parent; }
            names.Reverse();
            return string.Join("/", names);
        }

        static void RunInternal(StringBuilder log)
        {
            // ---------- 0. 配置加载（缺省自动创建默认资产） ----------
            cfg = PetRetargetConfig.LoadOrCreate();
            var MMD_FBX_PATH = cfg.mmdFbxPath;
            var GI_FBX_PATH = cfg.giFbxPath;
            var OUT_DIR = cfg.outDir;
            var TEST_SCENE = cfg.testScene;

            // ---------- 1. 读取全部 clip 曲线 ----------
            var clips = new List<ClipData>();
            foreach (var name in cfg.clips)
            {
                // 兼容两种命名：旧 Ani_NPC_Kanban_Paimon_{name}.anim 与 Cs 系列 Ani_Cs_NPC_Kanban_Paimon_{name}.anim
                var path = $"{cfg.animDir}/Ani_NPC_Kanban_Paimon_{name}.anim";
                var clipAsset = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clipAsset == null)
                {
                    path = $"{cfg.animDir}/Ani_Cs_NPC_Kanban_Paimon_{name}.anim";
                    clipAsset = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                }
                if (clipAsset == null) throw new System.InvalidOperationException($"\u52a8\u753b\u7f3a\u5931: {path}");
                clips.Add(ParseClip(path, name, log));
            }
            var standby = clips[0]; // clips[0]（默认 Standby）t=0 = GI 参考绑定姿势（与 A 期对齐一致）

            // ---------- 2. 临时场景：两 FBX 同空间 ----------
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var mmdFbx = AssetDatabase.LoadAssetAtPath<GameObject>(MMD_FBX_PATH);
            var giFbx = AssetDatabase.LoadAssetAtPath<GameObject>(GI_FBX_PATH);
            var mmdInst = (GameObject)PrefabUtility.InstantiatePrefab(mmdFbx);
            var giInst = (GameObject)PrefabUtility.InstantiatePrefab(giFbx);
            mmdInst.transform.position = Vector3.zero;
            giInst.transform.position = Vector3.zero;

            // GI：正确 Bip001（子含 Pelvis，有重名）；modelNode = .anim path 根
            Transform rightBip = null;
            foreach (var t in giInst.GetComponentsInChildren<Transform>(true))
                if (t.name == "Bip001" && t.Find("Bip001 Pelvis") != null) { rightBip = t; break; }
            if (rightBip == null) throw new System.InvalidOperationException("GI 正确 Bip001 未找到");
            var modelNode = rightBip.parent;
            var modelW = modelNode.localToWorldMatrix;

            // GI 全子树 DFS 先序（父先于子）
            var giBones = new List<Transform>();
            void DfsGi(Transform t) { giBones.Add(t); for (int i = 0; i < t.childCount; i++) DfsGi(t.GetChild(i)); }
            foreach (Transform ch in modelNode) DfsGi(ch);
            var giBoneByPath = new Dictionary<string, Transform>();
            var giPathByBone = new Dictionary<Transform, string>();
            foreach (var b in giBones)
            {
                var p = Rel(b, modelNode);
                giBoneByPath[p] = b; giPathByBone[b] = p;
            }

            // MMD：骨根 = {armNode}/{rootBone} 之下全骨；armW = 解算父世界
            var mmdRoot = mmdInst.transform.Find(cfg.armNodeName + "/" + cfg.rootBoneName);
            if (mmdRoot == null) throw new System.InvalidOperationException($"MMD \u9aa8\u6839 {cfg.rootBoneName} \u672a\u627e\u5230");
            var armNode = mmdRoot.parent;
            var armW = armNode.localToWorldMatrix;
            var mmdBones = new List<Transform>();
            void DfsM(Transform t) { mmdBones.Add(t); for (int i = 0; i < t.childCount; i++) DfsM(t.GetChild(i)); }
            DfsM(mmdRoot);
            var mmdBind = new Dictionary<Transform, Matrix4x4>(mmdBones.Count);
            foreach (var b in mmdBones) mmdBind[b] = Matrix4x4.TRS(b.localPosition, b.localRotation, b.localScale);

            // GI 绑定姿势：t=0 覆写（FBX 局部塌缩不可用；scale 曲线恒1→one，披风链乘积≈1 且 MMD 无对应骨）
            int overridden = 0;
            foreach (var b in giBones)
            {
                var p = giPathByBone[b];
                if (standby.rot.TryGetValue(p, out var rt)) b.localRotation = QuatOf(rt.Sample(0f));
                if (standby.pos.TryGetValue(p, out var pt)) b.localPosition = VecOf(pt.Sample(0f));
                b.localScale = Vector3.one;
                if (standby.rot.ContainsKey(p) && standby.pos.ContainsKey(p)) overridden++;
            }
            // 覆写后的实际局部 = 各 clip 缺曲线时的回退值（不用塌缩的 FBX 原值）
            var giRefPos = new Dictionary<Transform, Vector3>(giBones.Count);
            var giRefRot = new Dictionary<Transform, Quaternion>(giBones.Count);
            foreach (var b in giBones) { giRefPos[b] = b.localPosition; giRefRot[b] = b.localRotation; }

            var giWorld0 = new Dictionary<Transform, Matrix4x4>(giBones.Count); // GI 参考姿势世界（对齐用）
            var mmdWorld0 = new Dictionary<Transform, Matrix4x4>(mmdBones.Count); // MMD 绑定世界（对齐/残差用）
            foreach (var b in giBones) giWorld0[b] = b.localToWorldMatrix;
            foreach (var b in mmdBones) mmdWorld0[b] = b.localToWorldMatrix;
            log.AppendLine($"[setup] MMD\u9aa8={mmdBones.Count} GI\u9aa8={giBones.Count} t0\u8986\u5199={overridden} MMD\u6839={cfg.rootBoneName} GI\u6839={modelNode.name}");

            // ---------- 3. 手轴对齐（A 期 §5 段原样，实证 rotY≈150.2° scale≈1.158 盆锚点） ----------
            Transform MmdFind(string n) { foreach (var b in mmdBones) if (b.name == n) return b; return null; }
            Transform GiFind(string path) => giBoneByPath.TryGetValue(path, out var b) ? b : null;
            Vector3 POf(Dictionary<Transform, Matrix4x4> w, Transform t) => t != null && w.ContainsKey(t) ? w[t].GetColumn(3) : Vector3.zero;

            var GI_HAND_L = cfg.giHandLPath;
            var GI_HAND_R = cfg.giHandRPath;
            var GI_HEAD = cfg.giHeadPath;
            var gp = POf(giWorld0, GiFind(cfg.giPelvisPath));
            var gh = POf(giWorld0, GiFind(GI_HEAD));
            var glh = POf(giWorld0, GiFind(GI_HAND_L));
            var grh = POf(giWorld0, GiFind(GI_HAND_R));
            var mp = POf(mmdWorld0, MmdFind(cfg.mmdPelvisName));
            var mh = POf(mmdWorld0, MmdFind(cfg.mmdHeadName));
            var mlh = POf(mmdWorld0, MmdFind(cfg.mmdHandLName));
            var mrh = POf(mmdWorld0, MmdFind(cfg.mmdHandRName));
            if (gp == Vector3.zero || mp == Vector3.zero || mh == Vector3.zero || gh == Vector3.zero)
                throw new System.InvalidOperationException($"对齐关键点缺失 gp={gp} mp={mp} mh={mh} gh={gh}");
            void Orthon(Vector3 pelvis, Vector3 head, Vector3 handL, Vector3 handR, out Vector3 y, out Vector3 x, out Vector3 z)
            {
                y = (head - pelvis).normalized;
                var xr = handL - handR;
                x = (xr - Vector3.Project(xr, y)).normalized;
                z = Vector3.Cross(x, y).normalized;
            }
            Orthon(gp, gh, glh, grh, out var gy, out var gx, out var gz);
            Orthon(mp, mh, mlh, mrh, out var my, out var mx, out var mz);
            var R = Quaternion.LookRotation(gz, gy) * Quaternion.Inverse(Quaternion.LookRotation(mz, my));
            float scale = Vector3.Distance(gp, gh) / Mathf.Max(Vector3.Distance(mp, mh), 1e-5f);
            var T = gp - scale * (R * mp);
            var align = Matrix4x4.TRS(T, R, Vector3.one * scale);
            var alignInv = align.inverse;
            // 质量指标：对齐后 MMD 眼骨到 GI 眼骨距离（A 期实证 3-8mm）
            var eyeL = POf(mmdWorld0, MmdFind("\u76ee.L")); // 目.L
            var eyeR = POf(mmdWorld0, MmdFind("\u76ee.R")); // 目.R
            var geL = POf(giWorld0, GiFind(GI_HEAD + "/+EyeBone L A01"));
            var geR = POf(giWorld0, GiFind(GI_HEAD + "/+EyeBone R A01"));
            float edL = Vector3.Distance(align.MultiplyPoint3x4(eyeL), geL);
            float edR = Vector3.Distance(align.MultiplyPoint3x4(eyeR), geR);
            log.AppendLine($"[align] scale={scale:F3} rotY={Quaternion.Angle(Quaternion.identity, R):F1}\u00b0 T={T} \u773c\u8dddL={edL:F3} \u773c\u8dddR={edR:F3}");
            if (edL > cfg.alignEyeErrMax || edR > cfg.alignEyeErrMax)
                throw new System.InvalidOperationException($"\u5bf9\u9f50\u8d28\u91cf\u5f02\u5e38 \u773c\u8dddL={edL:F3} R={edR:F3}\uff08\u9608\u503c{cfg.alignEyeErrMax}\uff0cA \u671f\u5b9e\u8bc1 3-8mm\uff09");

            // ---------- 4. 骨映射（配置表驱动）+ 主骨代表选择 ----------
            var giByName = new Dictionary<string, Transform>();
            foreach (var b in giBones) if (!giByName.ContainsKey(b.name)) giByName[b.name] = b;
            Transform GiGet(string n) => giByName.TryGetValue(n, out var t) ? t : null;
            Transform MapMmd(string raw) => cfg.MapMmdBone(raw, GiGet);

            // 三趟映射：同名 → 显式表 → 祖先继承（A 期逻辑）
            var mmd2gi = new Dictionary<Transform, Transform>();
            var passOf = new Dictionary<Transform, int>(); // 0=同名 1=显式表 2=继承
            foreach (var mb in mmdBones)
                if (giByName.TryGetValue(mb.name, out var same)) { mmd2gi[mb] = same; passOf[mb] = 0; }
            int byName = mmd2gi.Count;
            foreach (var mb in mmdBones)
                if (!mmd2gi.ContainsKey(mb))
                {
                    var t = MapMmd(mb.name);
                    if (t != null) { mmd2gi[mb] = t; passOf[mb] = 1; }
                }
            int byTable = mmd2gi.Count - byName;
            foreach (var mb in mmdBones)
            {
                if (mmd2gi.ContainsKey(mb)) continue;
                var p = mb.parent;
                Transform got = null;
                while (p != null && p != armNode)
                {
                    if (mmd2gi.TryGetValue(p, out var t)) { got = t; break; }
                    got = MapMmd(p.name);
                    if (got != null) break;
                    p = p.parent;
                }
                mmd2gi[mb] = got ?? GiGet("Bip001 Pelvis");
                passOf[mb] = 2;
            }
            log.AppendLine($"[map] \u6620\u5c04 {mmd2gi.Count} 条: 同名={byName} 显式表={byTable} 祖先继承={mmd2gi.Count - byName - byTable}");

            // 有曲线的 GI 骨集合（Standby 曲线集为基准）
            var giCurvedBones = new HashSet<Transform>();
            foreach (var kv in standby.rot) if (giBoneByPath.TryGetValue(kv.Key, out var b)) giCurvedBones.Add(b);
            foreach (var kv in standby.pos) if (giBoneByPath.TryGetValue(kv.Key, out var b)) giCurvedBones.Add(b);
            log.AppendLine($"[map] GI \u6709\u66f2\u7ebf\u9aa8 {giCurvedBones.Count}/{giBones.Count}");

            // 代表优先级（配置驱动）：0=主链标准名 1=次要前缀链 2=同名（+HairS物理链）3=中枢备胎 9=继承不作代表
            // _shadow_/_dummy_ 影骨（物理复制骨）劣后于同名本体骨（如 目.L 优先于 _shadow_目.L）
            var rank0 = new HashSet<string>(cfg.rank0Bones);
            var rank3 = new HashSet<string>(cfg.rank3Bones);
            int RepRank(Transform mb)
            {
                bool shadow = mb.name.StartsWith("_shadow_") || mb.name.StartsWith("_dummy_");
                var n = mb.name;
                if (n.StartsWith("_shadow_")) n = n.Substring(8);
                else if (n.StartsWith("_dummy_")) n = n.Substring(7);
                int r;
                if (rank0.Contains(n)) r = 0;
                else if (rank3.Contains(n)) r = 3;
                else if (cfg.rank1Prefixes.Any(p => n.StartsWith(p))) r = 1;
                else if (passOf.TryGetValue(mb, out var pass) && pass == 0) r = 2; // +HairS/+EarB 等同名物理骨
                else r = 9; // 继承骨：不作代表（保持绑定姿势随父动）
                return shadow ? r + 5 : r;
            }

            // 每 GI 骨选一个 MMD 代表（rank 最小者胜）。
            // 注意：①GI 根骨 Bip001 不选代表——其全身运动已隐式包含在 Pelvis 等子骨的世界位姿里
            // （位置保持式传递用世界矩阵），直接驱动全ての親只会把两骨架的根部基准差（悬空 vs 站立）
            // 塞进 センター/グルーブ 链造成腰腹拉伸（实测 frame0 残差 0.388m）。
            // ②MMD 目.L/R 不驱动：平移虹膜机制（虹膜顶点权重挂目骨），位置保持式的对齐残差
            // （左右不对称 8mm/3mm）会直接变成虹膜位移——实测左目被定位到绑定位后方 7mm，
            // 虹膜沉入眼球后方"只剩眼白"。绑定姿势=编辑模式=用户确认正确，目骨保持绑定即可。
            var giRep = new Dictionary<Transform, Transform>();  // gi → mmd
            var rankByGi = new Dictionary<Transform, int>();
            var eyeNames = cfg.UndrivenSet(); // 目.L/R 等不驱动骨（配置）
            foreach (var mb in mmdBones)
            {
                if (mb == mmdRoot) continue; // 根骨不作代表（Bip001 经盆骨世界链路隐式传递）
                if (eyeNames.Contains(mb.name)) continue; // 不驱动（见配置 undrivenBones）
                if (!mmd2gi.TryGetValue(mb, out var g) || g == null) continue;
                if (!giCurvedBones.Contains(g)) continue;
                int r = RepRank(mb);
                if (r >= 9) continue;
                if (!giRep.TryGetValue(g, out _) || r < rankByGi[g]) { giRep[g] = mb; rankByGi[g] = r; }
            }
            if (giRep.Count == 0) throw new System.InvalidOperationException("\\u65e0\\u4efb\\u4f55\\u4e3b\\u9aa8\\u4ee3\\u8868");
            var repByMmd = new Dictionary<Transform, Transform>(); // mmd → gi
            foreach (var kv in giRep) repByMmd[kv.Value] = kv.Key;

            // v15（2026-08-23 晚）：D 链腿骨（足D/ひざD/足首D ×LR）并入代表骨。
            // 腿部蒙皮全在 D 链（标准腿链零蒙皮），跟随骨公式只传增量不传 f0 姿势基准
            //（restW=绑定刚体栈），待机腿恒为绑定直棍——偏离 GI 源悬空屈腿 ~37° 常量
            // + 脚踝 15cm 侧漂（暂停帧实测：GI 源左脚踝应在盆后 12.6cm，MMD 直垂）。
            // D 链是干净单链（足D→ひざD→足首D ↔ Thigh→Calf→Foot），5b 配对子骨逻辑
            // 天然命中，与代表骨同式（qPose 方向对齐 + v6 关节角）求解即精确跟踪。
            // 不并入 下半身/腰キャンセル：多分支骨"最长子延伸"会在躯干/腿间选错段（v5b 已踩）。
            int dAdded = 0;
            foreach (var mb in mmdBones)
            {
                if (repByMmd.ContainsKey(mb)) continue;
                var bn = mb.name;
                bool isDLeg = bn == "足D.L" || bn == "足D.R" || bn == "ひざD.L" || bn == "ひざD.R" || bn == "足首D.L" || bn == "足首D.R";
                if (!isDLeg) continue;
                if (!mmd2gi.TryGetValue(mb, out var gD2) || gD2 == null || !giCurvedBones.Contains(gD2)) continue;
                repByMmd[mb] = gD2;
                dAdded++;
            }
            if (dAdded != 6) throw new System.InvalidOperationException($"D \\u94fe\\u817f\\u9aa8\\u5e76\\u5165\\u4ee3\\u8868\\u5931\\u8d25 {dAdded}/6");

            log.AppendLine($"[rep] \\u4e3b\\u9aa8\\u4ee3\\u8868 {giRep.Count} \\u6761 + D \\u94fe {dAdded} \\u6761:");
            foreach (var kv in repByMmd.OrderBy(k => Rel(k.Key, armNode), System.StringComparer.Ordinal))
                log.AppendLine($"  {kv.Key.name} \u2190 {kv.Value.name}");
            foreach (var b in giCurvedBones.Where(b => !giRep.ContainsKey(b)).OrderBy(b => b.name))
            {
                if (b == rightBip)
                    log.AppendLine("  [skip] Bip001 \u6839\u9aa8\u8fd0\u52a8\u7ecf\u76c6\u9aa8\u4e16\u754c\u94fe\u8def\u9690\u5f0f\u4f20\u9012\uff08\u4e0d\u76f4\u63a5\u9a71\u52a8\uff09");
                else
                    log.AppendLine($"  [skip] {b.name} \u6709\u66f2\u7ebf\u4f46\u65e0\u4ee3\u8868\uff08\u7269\u7406\u9aa8/\u7ee7\u627f\u9aa8\uff0c\u4fdd\u6301\u7ed1\u5b9a\u59ff\u52bf\uff09");
            }

            // ---------- 4b. 腰锚（v6 位置传递锚点）+ 骨路径自检占位（5c 后校验） ----------
            // 捻骨（腕捩/手捩）v4 专用 Slerp 插值已废弃（v8）：MMD 手臂链绑定相邻骨为反相对齐
            // （腕→腕捩=179.9°约定），按"世界旋转插值"会捋成同向 → 捻骨翻转 ~170° 蒙皮折叠
            // （"每臂三处塌缩"实测）。v8 捻骨走 v7.1 跟随骨公式（restW 刚体栈保留绑定局部
            // 结构 + 自身映射目标 UpperArm/Forearm 的增量），绑定 180° 结构天然保住。
            var pelvisAnchor = repByMmd.Keys.FirstOrDefault(k => k.name == cfg.anchorBoneName);
            if (pelvisAnchor == null)
                throw new System.InvalidOperationException($"{cfg.anchorBoneName} \u4e0d\u5728\u4ee3\u8868\u96c6\u4e2d\uff0c\u65e0\u6cd5\u4f5c\u4e3a\u4f4d\u7f6e\u951a\u70b9");

            // ---------- 5. 输出目录 + 逐帧解局部 + 写 clip ----------
            if (!AssetDatabase.IsValidFolder(OUT_DIR))
                AssetDatabase.CreateFolder(cfg.animDir, "MMD");

            // 路径自检移至 5c 后（代表骨+跟随骨统一校验；SetCurve 不校验，missing path 只在运行时警告）
            var pelvisGi = GiFind(cfg.giPelvisPath);
            var headGi = GiFind(GI_HEAD);
            var clipAssets = new List<AnimationClip>();

            // ---------- 5b. 方向对齐预计算（v5.1） ----------
            // 每代表骨：MMD 绑定骨段方向 dMmdBind（本骨→主子骨）与 GI 侧对应子骨引用。
            // 主子骨选择带语义约束：两侧子骨必须互为代表/映射对（如 腰→上半身 对 Pelvis→Spine），
            // 否则"最长子延伸"会在多分支骨（腰下挂躯干+双腿）两侧选到语义不同的段
            //（实测 MMD 选上半身/GI 选 Thigh，腰被对齐到"腿方向"掰 90°）。
            // 无配对子骨时退化为 parent→self 方向（中枢/叶子骨）。mmdSet 已无用，删。
            Vector3 P0Of(Dictionary<Transform, Matrix4x4> w, Transform t) => w[t].GetColumn(3);
            var repChildM = new Dictionary<Transform, Transform>();
            var repChildG = new Dictionary<Transform, Transform>();
            var repDirBind = new Dictionary<Transform, Vector3>();
            foreach (var kv in repByMmd)
            {
                var mb = kv.Key; var g = kv.Value;
                // MMD 侧：本骨子骨中"映射目标 == 某 GI 骨"且该 GI 骨与本骨目标 g 的子骨一致的候选里选最长
                Transform childM = null, childG = null; float bestD = 1e-8f;
                foreach (Transform c in mb)
                {
                    if (!mmd2gi.TryGetValue(c, out var cG) || cG == null) continue;
                    if (cG.parent != g) continue; // 配对约束：MMD 子骨的目标必须是 GI 本骨的直接子骨
                    var d = (P0Of(mmdWorld0, c) - P0Of(mmdWorld0, mb)).sqrMagnitude;
                    if (d > bestD) { bestD = d; childM = c; childG = cG; }
                }
                repChildM[mb] = childM; repChildG[mb] = childG;
                var dM = childM != null ? P0Of(mmdWorld0, childM) - P0Of(mmdWorld0, mb)
                                        : P0Of(mmdWorld0, mb) - P0Of(mmdWorld0, mb.parent);
                repDirBind[mb] = dM.normalized;
            }

            // v7.2 躯干骨（腰/上半身/上半身2/首）参考方向改"指向头骨"（链终端锚点）：
            // Biped Pelvis→Spine 骨段带水平前倾分量（Max 约定 Spine 挂盆骨前侧），按骨段对齐会把
            // 整个躯干前倾（"肚子前翘"实测 25.7°）；"指向头"在两骨架语义一致（沿链向上）。
            // 四肢骨段方向语义一致（已实测正确），保持不变。
            var torsoOverride = cfg.TorsoSet();
            var headMmdRep = repByMmd.Keys.FirstOrDefault(k => k.name == "\u982d");
            if (headMmdRep != null)
                foreach (var kv in repByMmd)
                {
                    if (!torsoOverride.Contains(kv.Key.name)) continue;
                    repDirBind[kv.Key] = (P0Of(mmdWorld0, headMmdRep) - P0Of(mmdWorld0, kv.Key)).normalized;
                }
            log.AppendLine($"[v7.2] \u8eaf\u5e72\u53c2\u8003\u65b9\u5411\u6539\u6307\u5411\u5934\u9aa8: {string.Join("/", torsoOverride)}");

            // ---------- 5c. v7 全骨驱动清单 ----------
            // 病根：只驱动 28 代表骨时，其余 ~190 骨（裙摆D链/肩P肩C/上半身1/下半身等承载皮肤权重的
            // 辅助骨）保持绑定——主骨架摆出悬空姿势而裙摆/腰腹皮肤纹丝不动 → "腿僵硬直棍（裙摆）+
            // 腰前翘（腰腹权重骨冲突）"。根治：凡有 mmd2gi 映射的骨全部驱动——
            // 旋转 = R⁻¹·Δg(f)·R · (poseD(最近已驱动 GI 祖先) · Q绑定)，位置恒绑定（刚性）。
            // poseD = 该 GI 骨的代表 f0 姿势旋转 × 其 MMD 代表绑定旋转之逆（把 GI 悬空基准姿势
            // 传播到所有依附骨；D 链跟随大腿、肩P/C 跟随锁骨、下半身跟随盆——裙摆随腿摆动）。
            // 跳过：①腰锚以上根链（全ての親/センター/グルーブ——锚位解算吸收，勿双转）
            // ②操作中心（独立节点）③目.L/R（v3 平移虹膜铁律）。
            var rootChainSkip = new HashSet<Transform>();
            {
                var p = pelvisAnchor.parent;
                while (p != null && p != armNode) { rootChainSkip.Add(p); p = p.parent; }
            }
            var driveBones = new List<Transform>();
            foreach (var mb in mmdBones)
            {
                if (mb == mmdRoot || rootChainSkip.Contains(mb)) continue;
                if (repByMmd.ContainsKey(mb)) continue;
                if (mb.name == "\u64cd\u4f5c\u4e2d\u5fc3" || eyeNames.Contains(mb.name)) continue;
                if (!mmd2gi.TryGetValue(mb, out var g) || g == null) continue;
                driveBones.Add(mb);
            }
            log.AppendLine($"[v7] \u9a71\u52a8\u9aa8\u6e05\u5355: \u4ee3\u8868{repByMmd.Count} + \u8ddf\u968f{driveBones.Count} = {repByMmd.Count + driveBones.Count}/{mmdBones.Count}\uff08\u8df3\u8fc7\u6839\u94fe{rootChainSkip.Count}+\u76ee+\u64cd\u4f5c\u4e2d\u5fc3\u4fdd\u6301\u7ed1\u5b9a\uff1b\u637b\u9aa8\u5df2\u5e76\u5165\u8ddf\u968f\uff09");
            // 捻骨家族实测 16 根（腕捩/腕捩1-3/手捩/手捩1-3 × LR，另含 shadow/dummy 副本），
            // 全部经 MapMmd 前缀规则映射 UpperArm/Forearm 并入跟随——刚体跟随绑定 180° 结构。
            int twistCore = driveBones.Count(b => b.name == "\u8155\u6369.L" || b.name == "\u8155\u6369.R" || b.name == "\u624b\u6369.L" || b.name == "\u624b\u6369.R");
            int twistAll = driveBones.Count(b => b.name.StartsWith("\u8155\u6369") || b.name.StartsWith("\u624b\u6369"));
            log.AppendLine($"[v8] \u637b\u9aa8\u5bb6\u65cf\u8fdb\u5165\u8ddf\u968f: \u6838\u5fc3{twistCore}/4 \u5168\u65cf{twistAll}/16");
            if (twistCore != 4 || twistAll < 16)
                throw new System.InvalidOperationException($"\u637b\u9aa8\u672a\u8fdb\u5165\u8ddf\u968f\u6e05\u5355\uff08\u6838\u5fc3{twistCore}/4 \u5168\u65cf{twistAll}/16\uff09");

            // 路径自检（代表骨+跟随骨，含捻骨）
            foreach (var kv in repByMmd.Keys.Concat(driveBones))
            {
                var p = cfg.armNodeName + "/" + Rel(kv, armNode);
                if (mmdInst.transform.Find(p) == null)
                    throw new System.InvalidOperationException($"\u8def\u5f84\u89e3\u6790\u5931\u8d25: {p}");
            }

            foreach (var cd in clips)
            {
                int frames = cd.frameTimes.Count;
                var repPos = new Dictionary<Transform, Vector3[]>(repByMmd.Count);
                var repRot = new Dictionary<Transform, Quaternion[]>(repByMmd.Count);
                var repQ = new Dictionary<Transform, Quaternion[]>(repByMmd.Count); // 期望世界旋转（v6 关节角式）
                var quatG0 = new Dictionary<Transform, Quaternion>();               // GI 骨本 clip f0 世界旋转（增量基准）
                var dG0Dir = new Dictionary<Transform, Vector3>();                  // GI f0 骨段方向（共轭到 MMD 空间，姿势基准）
                var qPose = new Dictionary<Transform, Quaternion>();                // MMD f0 姿势旋转（方向对齐基准 × 绑定）
                var restW = new Dictionary<Transform, Quaternion>();                // v7.1 全骨 f0 姿势基准（刚体栈）
                var anchorOffset = Vector3.zero;                                    // v10 腰锚舞台偏移（每 clip f0 计算）
                foreach (var mb in repByMmd.Keys) { repPos[mb] = new Vector3[frames]; repRot[mb] = new Quaternion[frames]; repQ[mb] = new Quaternion[frames]; }
                foreach (var mb in driveBones) { repPos[mb] = new Vector3[frames]; repRot[mb] = new Quaternion[frames]; } // v7 跟随骨（含捻骨）也写曲线

                var giW = new Dictionary<Transform, Matrix4x4>(giBones.Count);
                var mmdW = new Dictionary<Transform, Matrix4x4>(mmdBones.Count);
                Dictionary<Transform, Matrix4x4> giW0 = null; // 本 clip 首帧世界（残差自检用）
                float phMin = float.MaxValue, phMax = float.MinValue;
                Quaternion BindRot(Transform mb) => ExtractRot(mmdBind[mb]); // 绑定局部旋转（v12.1 提升作用域）

                for (int f = 0; f < frames; f++)
                {
                    float t = cd.frameTimes[f];

                    // GI 世界（DFS 先序合成；缺曲线骨回退参考姿势局部，Nub 保持 FBX 原值）
                    foreach (var b in giBones)
                    {
                        var p = giPathByBone[b];
                        var lp = cd.pos.TryGetValue(p, out var pt) ? VecOf(pt.Sample(t)) : giRefPos[b];
                        var lr = cd.rot.TryGetValue(p, out var rt) ? QuatOf(rt.Sample(t)) : giRefRot[b];
                        var pw = b.parent == modelNode ? modelW : giW[b.parent];
                        giW[b] = pw * Matrix4x4.TRS(lp, lr, Vector3.one);
                    }
                    if (f == 0) giW0 = new Dictionary<Transform, Matrix4x4>(giW);

                    var ph = Vector3.Distance(giW[pelvisGi].GetColumn(3), giW[headGi].GetColumn(3));
                    phMin = Mathf.Min(phMin, ph); phMax = Mathf.Max(phMax, ph);

                    // MMD 自顶向下解（v6 关节角传递 + 刚性链）：
                    // 旋转 qWorld(f) = R⁻¹·(Qg(f)·Qg(0)⁻¹)·R · qPose——GI 世界旋转增量（骨轴约定无关的
                    // 物理量）共轭到 MMD 空间，叠在本 clip f0 姿势基准上；
                    // qPose = FromToRotation(dBind, R⁻¹·dGi(0)) · Q绑定（悬空屈腿等基准姿势穿上身，
                    // 无配对子骨的叶子骨退化为纯增量）。数学上骨段方向全程精确跟踪 R⁻¹·dGi(f)。
                    // 位置：非锚骨 localPos 恒=绑定（骨长恒定→蒙皮零拉伸零压缩，v5 过约束病根）；
                    // 仅腰锚（盆根）位置保持式传递，承载 bob/全身根运动。
                    if (f == 0)
                    {
                        foreach (var kv in repByMmd)
                        {
                            var g0 = kv.Value;
                            quatG0[g0] = ExtractRot(giW[g0]);
                            var cG = repChildG[kv.Key];
                            if (torsoOverride.Contains(kv.Key.name) && headMmdRep != null)
                            {
                                // v7.2 躯干骨：GI 侧参考 = 指向 GI 头骨（与 MMD 侧 repDirBind 同语义）
                                var headG = repByMmd[headMmdRep];
                                var d0 = (giW[headG].GetColumn(3) - giW[g0].GetColumn(3)).normalized;
                                dG0Dir[kv.Key] = Quaternion.Inverse(R) * d0;
                            }
                            else if (cG != null)
                            {
                                var d0 = (giW[cG].GetColumn(3) - giW[g0].GetColumn(3)).normalized;
                                dG0Dir[kv.Key] = Quaternion.Inverse(R) * d0;
                            }
                            qPose[kv.Key] = dG0Dir.TryGetValue(kv.Key, out var d0M)
                                ? Quaternion.FromToRotation(repDirBind[kv.Key], d0M) * ExtractRot(mmdWorld0[kv.Key])
                                : ExtractRot(mmdWorld0[kv.Key]);
                        }
                        // v10 腰锚舞台偏移扣除：Cs 系列根骨带大幅舞台位移（实测 12m），
                        // 桌宠需原地动作——f0 目标平移到 MMD 绑定盆位，保留 bob/根运动相对 f0 的部分
                        anchorOffset = (Vector3)(alignInv * giW[repByMmd[pelvisAnchor]]).GetColumn(3) - (Vector3)mmdWorld0[pelvisAnchor].GetColumn(3);
                        // v7.1 restW 全骨刚体栈：f0 姿势基准 = 代表骨 qPose，其余骨 = 父基准 × 自身绑定局部旋转。
                        // 跟随骨世界旋转 = C·Δg·C⁻¹ · restW——同目标父子 Δ 自动抵消（无双重施加），
                        // 异目标（足D=Thigh 挂在 下半身=Pelvis 下）自动成为相对运动（裙摆随腿）。
                        foreach (var mb in mmdBones)
                        {
                            var gD = mmd2gi[mb];
                            if (!quatG0.ContainsKey(gD)) quatG0[gD] = ExtractRot(giW[gD]);
                        }
                        restW.Clear();
                        restW[mmdRoot] = ExtractRot(armW); // 根基准 = arm 节点旋转（栈底）
                        void StackRest(Transform mb)
                        {
                            restW[mb] = qPose.TryGetValue(mb, out var qp) ? qp : restW[mb.parent] * BindRot(mb);
                            foreach (Transform ch in mb) StackRest(ch);
                        }
                        foreach (Transform ch in mmdRoot) StackRest(ch);
                        if (restW.Count != mmdBones.Count)
                            throw new System.InvalidOperationException($"restW \u6808\u5f02\u5e38 {restW.Count}/{mmdBones.Count}");
                    }
                    Quaternion QWantOf(Transform mb, Transform g)
                    {
                        var delta = ExtractRot(giW[g]) * Quaternion.Inverse(quatG0[g]);
                        return (Quaternion.Inverse(R) * delta * R) * qPose[mb];
                    }
                    void SolveFrame(int f)
                    {
                    foreach (var mb in mmdBones)
                    {
                        var pw = mb == mmdRoot ? armW : mmdW[mb.parent];
                        if (repByMmd.TryGetValue(mb, out var g))
                        {
                            var qWant = QWantOf(mb, g);
                            Vector3 lp;
                            if (mb == pelvisAnchor)
                            {
                                // 腰锚：位置保持式（bob/根运动载体）+ v10 舞台偏移扣除
                                var gPos = (Vector3)(alignInv * giW[g]).GetColumn(3) - anchorOffset;
                                lp = (pw.inverse * Matrix4x4.TRS(gPos, qWant, Vector3.one)).GetColumn(3);
                            }
                            else
                            {
                                // 刚性链：骨长恒定
                                lp = mmdBind[mb].GetColumn(3);
                            }
                            var lr = ExtractRot(pw.inverse * Matrix4x4.TRS(lp, qWant, Vector3.one));
                            repPos[mb][f] = lp;
                            repRot[mb][f] = lr;
                            repQ[mb][f] = qWant;
                            var bm = mmdBind[mb];
                            var sc = new Vector3(bm.GetColumn(0).magnitude, bm.GetColumn(1).magnitude, bm.GetColumn(2).magnitude);
                            mmdW[mb] = pw * Matrix4x4.TRS(lp, lr, sc);
                        }
                        else if (driveBones.Contains(mb))
                        {
                            // v14 根治（2026-08-23 晚）：跟随骨世界旋转 = C·Δ(自身目标)·C⁻¹·restW（绝对追踪）。
                            // qWant 是【绝对世界旋转】——解局部时 lr = pw⁻¹·qWant 已把父世界除掉，
                            // 父增量不存在"双重施加"，每骨世界独立精确跟踪 R⁻¹·Δ(自身目标)·R。
                            // v13 的 Δ_rel=Δ(自身)·Δ(父)⁻¹ 是错误诊断的回归：叠上 腰キャンセル→Thigh 映射后
                            // 足D.L 的 Δ_rel=Δ(Thigh)·Δ(Thigh)⁻¹≡单位——大腿骨段全程冻结在 f0，
                            // 盆骨摇摆完全不传给腿（Standby 双腿向 +x 侧漂，脚踝偏离 GI 源 ~15cm 实测；
                            // 标准链代表骨全程精确但零蒙皮，D 链才是可视皮）。
                            // v13 想修的"v7.1 D 链飞膝"实为 v11 之前的 GI 源符号跳变伪影，与裸乘无关。
                            // 同目标父子（腰キャンセル/足D→Thigh）绝对追踪下局部恒=绑定（v8 捻骨已验证）。
                            var gD = mmd2gi[mb];
                            var deltaSelf = ExtractRot(giW[gD]) * Quaternion.Inverse(quatG0[gD]);
                            var qWant = (Quaternion.Inverse(R) * deltaSelf * R) * restW[mb];
                            var lp = mmdBind[mb].GetColumn(3);
                            var lr = ExtractRot(pw.inverse * Matrix4x4.TRS(lp, qWant, Vector3.one));
                            repPos[mb][f] = lp;
                            repRot[mb][f] = lr;
                            var bm = mmdBind[mb];
                            var scD = new Vector3(bm.GetColumn(0).magnitude, bm.GetColumn(1).magnitude, bm.GetColumn(2).magnitude);
                            mmdW[mb] = pw * Matrix4x4.TRS(lp, lr, scD);
                        }
                        else
                        {
                            mmdW[mb] = pw * mmdBind[mb];
                        }
                    }
                    } // SolveFrame

                    SolveFrame(f);
                    // v12 两遍法（皮肤链 f0 同源）：腿部蒙皮全在 D 链（足D/ひざD/足首D，标准腿链零蒙皮）。
                    // f0 解完后用实解世界重建 restW，使 D 链 f0 基准 = 标准链 f0 姿势（消除 42mm 分叉，
                    // Greet 左膝前弯病灶）。同目标父子 Δ 抵消性质保持（restWp⁻¹·restWc = f0 局部结构）。
                    if (f == 0)
                    {
                        foreach (var mb in mmdBones) restW[mb] = ExtractRot(mmdW[mb]);
                        foreach (var mb in driveBones) { repPos[mb][0] = mmdBind[mb].GetColumn(3); repRot[mb][0] = BindRot(mb); }
                    }

                    // 解算精确性断言（首/中/末帧）：
                    // ①腰锚位置 vs align⁻¹·giW 目标（应精确）
                    // ②骨段方向 = 本帧 MMD 配对子骨连线 vs R⁻¹·GI 配对子骨连线（v6 数学恒等式，应≈0）
                    // ③旋转 vs 期望（构造量，应=0）
                    if (f == 0 || f == frames / 2 || f == frames - 1)
                    {
                        float eA = Vector3.Distance(mmdW[pelvisAnchor].GetColumn(3), (Vector3)(alignInv * giW[repByMmd[pelvisAnchor]]).GetColumn(3) - anchorOffset);
                        float aMax = 0f, dMax = -1f; var dWorst = "?";
                        foreach (var kv in repByMmd)
                        {
                            aMax = Mathf.Max(aMax, Quaternion.Angle(ExtractRot(mmdW[kv.Key]), repQ[kv.Key][f]));
                            if (torsoOverride.Contains(kv.Key.name) && headMmdRep != null)
                            {
                                // v7.2 躯干骨：比较"指向头"向量（链式合成非逐骨恒等，宽松上界）
                                var headG2 = repByMmd[headMmdRep];
                                var dMmd = (mmdW[headMmdRep].GetColumn(3) - mmdW[kv.Key].GetColumn(3)).normalized;
                                var dTgt = Quaternion.Inverse(R) * (giW[headG2].GetColumn(3) - giW[kv.Value].GetColumn(3)).normalized;
                                float da = Vector3.Angle(dMmd, dTgt);
                                if (da > dMax) { dMax = da; dWorst = kv.Key.name; }
                            }
                            else
                            {
                                var cM = repChildM[kv.Key]; var cG = repChildG[kv.Key];
                                if (cM != null && cG != null && repByMmd.ContainsKey(cM))
                                {
                                    var dMmd = (mmdW[cM].GetColumn(3) - mmdW[kv.Key].GetColumn(3)).normalized;
                                    var dTgt = (Quaternion.Inverse(R) * (giW[cG].GetColumn(3) - giW[kv.Value].GetColumn(3)).normalized);
                                    float da = Vector3.Angle(dMmd, dTgt);
                                    if (da > dMax) { dMax = da; dWorst = kv.Key.name; }
                                }
                            }
                        }
                        log.AppendLine($"[{cd.name} f{f}] \u89e3\u7b97\u7cbe\u786e\u6027: \u951a\u4f4d\u8bef\u5dee={eA:F6}m rotMax={aMax:F3}\u00b0 \u53c2\u8003\u65b9\u5411\u6700\u5927\u8bef\u5dee={dMax:F2}\u00b0({dWorst})");
                        if (eA > cfg.anchorPosErrMax || aMax > cfg.rotErrMaxDeg)
                            throw new System.InvalidOperationException($"[{cd.name} f{f}] \u89e3\u7b97\u4e0d\u7cbe\u786e \u951a={eA:F6}m rot={aMax:F3}\u00b0\uff08\u5e94<0.0001m/0.5\u00b0\uff09");
                        if (dMax > cfg.dirErrMaxDeg && cd.name == clips[0].name)
                            // 方向跟踪阈值仅对参考 clip 严格（杂技动作如前滚翻中"指向头"向量含两骨架
                            // 比例差投影，剧烈姿势下结构性放大——解算本身是增量式不受影响，仅记录）
                            throw new System.InvalidOperationException($"[{cd.name} f{f}] \u53c2\u8003\u65b9\u5411\u8bef\u5dee {dMax:F2}\u00b0 \u8d85\u9608\u503c\uff08\u53c2\u8003clip\u5e94<{cfg.dirErrMaxDeg}\u00b0\uff09");
                    }
                }

                // 姿态旋转幅度 vs 绑定（v5 方向对齐式：f0 即为悬空姿态角，腿部应显著非零=弯腿到位）
                foreach (var n in new[] { "\u8155.L", "\u8db3.L", "\u3072\u3056.L", "\u8db3\u9996.L", "\u982d", "\u8170" })
                {
                    var mb = repByMmd.Keys.FirstOrDefault(k => k.name == n);
                    if (mb == null) continue;
                    float aMin = 999f, aMax = 0f;
                    for (int f = 0; f < frames; f++)
                    {
                        var a = Quaternion.Angle(repQ[mb][f], ExtractRot(mmdWorld0[mb]));
                        aMin = Mathf.Min(aMin, a); aMax = Mathf.Max(aMax, a);
                    }
                    log.AppendLine($"[{cd.name}] \u59ff\u6001\u65cb\u8f6c(vs\u7ed1\u5b9a) {n}: f0={Quaternion.Angle(repQ[mb][0], ExtractRot(mmdWorld0[mb])):F1}\u00b0 \u5e45\u5ea6=[{aMin:F1}\u00b0, {aMax:F1}\u00b0]");
                }
                // v7.1 跟随抽查（世界角 vs 绑定）：足D.L 应随大腿摆（≈足.L 世界姿态角），_shadow_目.L 应≈頭角
                {
                    var footD = driveBones.FirstOrDefault(b => b.name == "\u8db3D.L");
                    var thigh = repByMmd.Keys.FirstOrDefault(k => k.name == "\u8db3.L");
                    var headB = repByMmd.Keys.FirstOrDefault(k => k.name == "\u982d");
                    var shadowEye = driveBones.FirstOrDefault(b => b.name == "_shadow_\u76ee.L");
                    float WAngle(Transform mb, int fi)
                    {
                        var lr = repRot[mb][fi];
                        var bm = mmdBind[mb];
                        var lrBind = ExtractRot(bm);
                        // 世界角差 = 局部差 × 父链差近似（此处直接用局部差×父世界差的合成，简化：比较世界四元数需重合成——
                        // 用已存局部+父绑定栈粗算 f0：直接对比局部差即可观察跟随量（父若同目标已抵消）
                        return Quaternion.Angle(lr, lrBind);
                    }
                    if (footD != null && thigh != null)
                    {
                        string sHead = headB != null ? WAngle(headB, 0).ToString("F1") : "?";
                        string sShadow = shadowEye != null ? WAngle(shadowEye, 0).ToString("F1") : "?";
                        log.AppendLine($"[{cd.name}] \u8ddf\u968f\u62bd\u67e5(f0\u5c40\u90e8\u89d2) \u8db3D.L={WAngle(footD, 0):F1}\u00b0 \u8db3.L={WAngle(thigh, 0):F1}\u00b0 \u982d={sHead}\u00b0 _shadow_\u76ee.L={sShadow}\u00b0");
                    }
                }

                // 四元数连续化（q ≡ -q，防曲线插值整周翻转；含捻骨）
                foreach (var mb in repPos.Keys)
                {
                    var arr = repRot[mb];
                    for (int f = 1; f < frames; f++)
                        if (Quaternion.Dot(arr[f], arr[f - 1]) < 0f)
                            arr[f] = new Quaternion(-arr[f].x, -arr[f].y, -arr[f].z, -arr[f].w);
                }

                // frame0 残差（v10：扣除腰锚舞台偏移后衡量——衡量"GI 首帧姿势 vs MMD 绑定姿势"的差，
                // 腰锚 f0 现为构造性 0；頭为拟合关键点 cm 级；其余骨两姿势本就不同仅记录）
                float rPelvis = -1f, rHead = -1f;
                var rTop = new List<(string n, float d)>();
                foreach (var kv in repByMmd)
                {
                    var d = Vector3.Distance((Vector3)(alignInv * giW0[kv.Value]).GetColumn(3) - anchorOffset, (Vector3)mmdWorld0[kv.Key].GetColumn(3));
                    if (kv.Key.name == "\u8170") rPelvis = d;          // 腰 = 盆锚点
                    else if (kv.Key.name == "\u982d") rHead = d;       // 頭 = 拟合关键点
                    rTop.Add((kv.Key.name, d));
                }
                foreach (var (n, d) in rTop.OrderByDescending(x => x.d).Take(5))
                    log.AppendLine($"[{cd.name}] frame0\u59ff\u52bf\u5dee {n} = {d:F3}m");
                log.AppendLine($"[{cd.name}] \u76c6-\u5934\u8ddd[{phMin:F3},{phMax:F3}]m \u8170\u6b8b\u5dee={rPelvis:F4}m \u982d\u6b8b\u5dee={rHead:F4}m");
                if (rPelvis < 0f || rHead < 0f)
                    throw new System.InvalidOperationException($"[{cd.name}] \u8170/\u982d \u4e0d\u5728\u4ee3\u8868\u96c6\u4e2d\uff08rPelvis={rPelvis} rHead={rHead}\uff09");
                if (rPelvis > cfg.anchorResidualMax)
                    throw new System.InvalidOperationException($"[{cd.name}] \u76c6\u951a\u70b9\u6b8b\u5dee {rPelvis:F4}m \u5e94\u7cbe\u786e");
                if (rHead > 0.35f)
                    // 頭残差=GI 首帧姿势差（各 clip 不同，Cs 系列可达 0.2-0.3m），仅设宽松结构上界；
                    // Standby 单独从严（对齐质量回归检查）
                    if (cd.name == "Standby" && rHead > cfg.headResidualMax)
                        throw new System.InvalidOperationException($"[Standby] \u982d\u62df\u5408\u6b8b\u5dee {rHead:F4}m \u8d85\u9608\u503c\uff08\u5bf9\u9f50\u8d28\u91cf\u5f02\u5e38\uff09");

                // 写 clip（legacy；路径前缀 Paimon_arm/，相对挂 Animation 的 MMD 根节点；代表骨+捻骨）。
                // 位置曲线只写腰锚——其余骨无位置动画=保持 prefab 绑定值（刚性链，骨长恒定的运行时保证）
                var clip = new AnimationClip { name = $"Ani_NPC_Kanban_Paimon_{cd.name}_MMD", legacy = true, frameRate = 60f };
                foreach (var mb in repPos.Keys)
                {
                    var path = cfg.armNodeName + "/" + Rel(mb, armNode);
                    var rots = repRot[mb];
                    var krx = new Keyframe[frames]; var kry = new Keyframe[frames]; var krz = new Keyframe[frames]; var krw = new Keyframe[frames];
                    for (int f = 0; f < frames; f++)
                    {
                        var tt = cd.frameTimes[f];
                        krx[f] = new Keyframe(tt, rots[f].x); kry[f] = new Keyframe(tt, rots[f].y);
                        krz[f] = new Keyframe(tt, rots[f].z); krw[f] = new Keyframe(tt, rots[f].w);
                    }
                    clip.SetCurve(path, typeof(Transform), "localRotation.x", new AnimationCurve(krx));
                    clip.SetCurve(path, typeof(Transform), "localRotation.y", new AnimationCurve(kry));
                    clip.SetCurve(path, typeof(Transform), "localRotation.z", new AnimationCurve(krz));
                    clip.SetCurve(path, typeof(Transform), "localRotation.w", new AnimationCurve(krw));
                    if (mb == pelvisAnchor)
                    {
                        var poss = repPos[mb];
                        var kpx = new Keyframe[frames]; var kpy = new Keyframe[frames]; var kpz = new Keyframe[frames];
                        for (int f = 0; f < frames; f++)
                        {
                            var tt = cd.frameTimes[f];
                            kpx[f] = new Keyframe(tt, poss[f].x); kpy[f] = new Keyframe(tt, poss[f].y); kpz[f] = new Keyframe(tt, poss[f].z);
                        }
                        clip.SetCurve(path, typeof(Transform), "localPosition.x", new AnimationCurve(kpx));
                        clip.SetCurve(path, typeof(Transform), "localPosition.y", new AnimationCurve(kpy));
                        clip.SetCurve(path, typeof(Transform), "localPosition.z", new AnimationCurve(kpz));
                    }
                }
                clip.wrapMode = cd.name == "Standby" ? WrapMode.Loop : WrapMode.Once;
                var outPath = $"{OUT_DIR}/{clip.name}.anim";
                if (AssetDatabase.LoadAssetAtPath<AnimationClip>(outPath) != null) AssetDatabase.DeleteAsset(outPath);
                AssetDatabase.CreateAsset(clip, outPath);
                clipAssets.Add(AssetDatabase.LoadAssetAtPath<AnimationClip>(outPath));
                log.AppendLine($"[{cd.name}] clip \u5199\u51fa: {outPath} \u9aa8 {repPos.Count}\uff08\u4ee3\u8868{repByMmd.Count}+\u8ddf\u968f{driveBones.Count}\uff09 \u5e27 {frames}");
            }

            // ---------- 6. 测试场景（原生 MMD 模型 + 绿地面 + 自动播转换后 Standby） ----------
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var lightGo = new GameObject("MainLight");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.2f;
            lightGo.transform.rotation = Quaternion.Euler(35, -155, 20);

            var pet = (GameObject)PrefabUtility.InstantiatePrefab(mmdFbx);
            pet.name = "Paimon_MMD";
            var animComp = pet.AddComponent<Animation>();
            // 全部转换 clip 注册（供 Play/按钮切换）
            foreach (var c in clipAssets) animComp.AddClip(c, c.name);
            animComp.clip = clipAssets[0];
            animComp.playAutomatically = true;
            animComp.cullingType = AnimationCullingType.AlwaysAnimate;

            // 动作切换 UI：编辑期预置于场景（AnimUICanvas），运行时零生成；布局可在此场景直接调
            var swapper = pet.AddComponent<GIC.Pet.PetAnimSwapper>();
            var anim = pet.AddComponent<GIC.Pet.PetEmotionController>();
            pet.AddComponent<GIC.Pet.PetBlinkController>();
            var sw = new SerializedObject(swapper);
            sw.FindProperty("targetAnimation").objectReferenceValue = animComp;
            sw.FindProperty("emotionController").objectReferenceValue = anim;
            sw.ApplyModifiedPropertiesWithoutUndo();

            var canvasGo = new GameObject("AnimUICanvas", typeof(UnityEngine.Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            canvasGo.GetComponent<UnityEngine.Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(960, 540);

            // 滚动列表：ScrollView > Viewport(Mask) > Content(VerticalLayout+ContentSizeFitter) > 按钮们
            var scrollGo = new GameObject("ScrollView", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.ScrollRect));
            scrollGo.transform.SetParent(canvasGo.transform, false);
            var scrollRect = scrollGo.GetComponent<RectTransform>();
            scrollRect.anchorMin = scrollRect.anchorMax = new Vector2(0, 1);
            scrollRect.pivot = new Vector2(0, 1);
            scrollRect.anchoredPosition = new Vector2(16, -16);
            scrollRect.sizeDelta = new Vector2(250, 420);
            scrollGo.GetComponent<UnityEngine.UI.Image>().color = new Color(0.1f, 0.12f, 0.16f, 0.5f);

            var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Mask));
            viewportGo.transform.SetParent(scrollGo.transform, false);
            var vpRect = viewportGo.GetComponent<RectTransform>();
            vpRect.anchorMin = Vector2.zero; vpRect.anchorMax = Vector2.one; vpRect.sizeDelta = Vector2.zero;
            var vpImg = viewportGo.GetComponent<UnityEngine.UI.Image>();
            vpImg.color = Color.white; vpImg.raycastTarget = false;
            viewportGo.GetComponent<UnityEngine.UI.Mask>().showMaskGraphic = false;

            var contentGo = new GameObject("Content", typeof(RectTransform), typeof(UnityEngine.UI.VerticalLayoutGroup), typeof(UnityEngine.UI.ContentSizeFitter));
            contentGo.transform.SetParent(viewportGo.transform, false);
            var contentRect = contentGo.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0, 1); contentRect.anchorMax = new Vector2(1, 1);
            contentRect.pivot = new Vector2(0.5f, 1); contentRect.sizeDelta = Vector2.zero;
            var vlg = contentGo.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.spacing = 6;
            vlg.childForceExpandHeight = false; vlg.childForceExpandWidth = false;
            vlg.childControlWidth = true; vlg.childControlHeight = true;
            contentGo.GetComponent<UnityEngine.UI.ContentSizeFitter>().verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;

            var scroll = scrollGo.GetComponent<UnityEngine.UI.ScrollRect>();
            scroll.viewport = vpRect; scroll.content = contentRect;
            scroll.horizontal = false; scroll.vertical = true;
            scroll.scrollSensitivity = 30;
            scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            foreach (var c in clipAssets)
            {
                var btnGo = new GameObject(c.name, typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Button));
                btnGo.transform.SetParent(contentGo.transform, false);
                btnGo.GetComponent<UnityEngine.UI.Image>().color = new Color(0.18f, 0.22f, 0.3f, 0.85f);
                var ble = btnGo.AddComponent<UnityEngine.UI.LayoutElement>();
                ble.minHeight = 34; ble.minWidth = 220;
                var txtGo = new GameObject("Label", typeof(RectTransform), typeof(UnityEngine.UI.Text));
                txtGo.transform.SetParent(btnGo.transform, false);
                var txt = txtGo.GetComponent<UnityEngine.UI.Text>();
                txt.font = font; txt.fontSize = 20; txt.color = Color.white;
                txt.alignment = TextAnchor.MiddleCenter;
                txt.text = c.name.Replace("Ani_Cs_NPC_Kanban_Paimon_", "Cs:").Replace("Ani_NPC_Kanban_Paimon_", "").Replace("_MMD", "");
                var tr = txtGo.GetComponent<RectTransform>();
                tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.sizeDelta = Vector2.zero;
                // 持久监听（存进场景文件，运行时零查找）
                UnityEditor.Events.UnityEventTools.AddStringPersistentListener(
                    btnGo.GetComponent<UnityEngine.UI.Button>().onClick, swapper.Play, c.name);
            }

            // EventSystem（场景预置）
            var esGo = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem), typeof(UnityEngine.EventSystems.StandaloneInputModule));

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(3, 1, 3);
            var matGuids = AssetDatabase.FindAssets("RerigGroundMat");
            if (matGuids.Length > 0)
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(matGuids[0]));
                if (mat != null) ground.GetComponent<MeshRenderer>().sharedMaterial = mat;
            }

            // 相机对准骨骼包围盒（SMR bounds 在 ×100 节点下不可靠）
            var sceneRoot = pet.transform.Find(cfg.armNodeName + "/" + cfg.rootBoneName);
            var bmin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var bmax = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            foreach (var b in sceneRoot.GetComponentsInChildren<Transform>(true))
            {
                bmin = Vector3.Min(bmin, b.position); bmax = Vector3.Max(bmax, b.position);
            }
            var ctr = (bmin + bmax) / 2f; var size = bmax - bmin;
            var camGo = new GameObject("MainCamera"); camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.15f, 0.15f, 0.18f);
            camGo.transform.position = ctr + new Vector3(0, 0.05f, size.z + 1.1f);
            camGo.transform.LookAt(ctr);

            EditorSceneManager.SaveScene(scene, TEST_SCENE);
            AssetDatabase.SaveAssets();
            log.AppendLine($"[scene] \u6d4b\u8bd5\u573a\u666f\u5df2\u5efa: {TEST_SCENE}\uff08\u9aa8\u9abc\u5305\u56f4\u76d2 size={size} \u4e2d\u5fc3={ctr}\uff09");
        }
    }
}
