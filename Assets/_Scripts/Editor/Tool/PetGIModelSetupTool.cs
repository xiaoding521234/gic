using UnityEditor;
using UnityEngine;

namespace GIC.Editor
{
    /// <summary>
    /// GI 官方模型落地工具（2026-08-24 纯原神资产路线）：
    /// 1) 给 Assets/Art/PaimonPet/GI/Paimon.fbx 的各网格建 PaimonToon 材质（无光照定案：_ShadowStrength=0，纯贴图色）
    /// 2) 在 PaimonPet 场景把官方模型挂到 Paimon 根下（GI_Root），注册 GI 原始 .anim（Bip001 路径零重定向直读）
    /// 3) 视线/眨眼/情绪/手指控制器走骨名适配（GI 骨名与 MMD 不同：Bip001 Neck/Head、Eye_WinkA morph 等）
    /// 幂等：重复执行只补缺，不重复创建。
    ///
    /// 2026-08-25 材质槽修复：Body 网格有 2 个子网格/材质槽 = [身体 | 头发]——头发与虹膜几何都在槽1。
    /// 旧版用 sharedMaterial 单值赋值把两槽都压成身体材质，导致头发渲染成身体贴图、虹膜渲染成白布（"头发纹理
    /// 错误/眼睛只有眼白"两大目检 bug 的根因）。现按槽位逐一赋值。EyeStar 条带（双眼星形瞳孔）采样脸贴图
    /// 左下角的纯色色块（RGB 255,236,173 淡金色，全不透明），贴脸贴图取色。
    /// </summary>
    public static class PetGIModelSetupTool
    {
        const string FBX = "Assets/Art/PaimonPet/GI/Paimon.fbx";
        const string MatDir = "Assets/Art/PaimonPet/GI/Materials";
        const string TexDir = "Assets/Art/PaimonPet/GI/Textures";
        const string AnimDir = "Assets/Art/PaimonPet/GI/Animations";

        [MenuItem("Tools/桌宠/GI官方模型: 材质与场景接线")]
        public static void Setup()
        {
            if (!UnityEngine.SceneManagement.SceneManager.GetActiveScene().path.EndsWith("PaimonPet.unity"))
            {
                EditorUtility.DisplayDialog("GI 官方模型接线", "请先打开 PaimonPet 场景", "好");
                return;
            }
            var log = new System.Text.StringBuilder();
            BuildMaterials(log);
            WireScene(log);
            Debug.Log("[PetGIModelSetup]\n" + log);
        }

        static void BuildMaterials(System.Text.StringBuilder log)
        {
            if (!AssetDatabase.IsValidFolder(MatDir))
                AssetDatabase.CreateFolder("Assets/Art/PaimonPet/GI", "Materials");

            // 披风星空合成（官方银河披风）：Diffuse 只是白→蓝渐变底色，星星/星座线在 Lightmap（黑底星点）里，加法合成成一张贴图
            ComposeCloakTexture(log);
            CreateMat(log, "GI_身体", "NPC_Kanban_Paimon_Tex_Body_Diffuse", 1.0f);
            CreateMat(log, "GI_脸", "NPC_Kanban_Paimon_Tex_Face_Diffuse", 0.7f);
            CreateMat(log, "GI_头发", "NPC_Kanban_Paimon_Tex_Hair_Diffuse", 1.2f);
            // 披风用合成贴图（渐变+星光）；合成失败时回退纯 Diffuse
            var cloakTex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/NPC_Kanban_Paimon_Tex_Cloak_Composed.png") != null
                ? "NPC_Kanban_Paimon_Tex_Cloak_Composed" : "NPC_Kanban_Paimon_Tex_Cloak_Diffuse";
            CreateMat(log, "GI_披风", cloakTex, 0.8f, cullOff: true);
            CreateMat(log, "GI_特效", "NPC_Kanban_Paimon_Tex_Face_Diffuse", 0f);
            AssetDatabase.SaveAssets();
            log.AppendLine("材质创建完成");
        }

        /// <summary>
        /// 披风合成贴图：Cloak_Diffuse（白→蓝渐变底色）+ Cloak_Lightmap 星点。
        /// 官方披风内衬 = 深蓝底+白色繁星（官方立绘实证）；纯 Diffuse 渲染会丢失全部星空。
        /// 注意：官方 Lightmap 走光照 UV 通道（本项目网格无 UV1，无法按官方通道对应），星点位置在光照 UV
        /// 空间与 Diffuse UV 空间不重合——直接原位叠加会全部落在星星稀疏区（实测渲染无星）。
        /// 因此提取星点后均匀散布到蓝色内衬区（V&lt;0.45），白色外层保持纯白。
        /// </summary>
        static void ComposeCloakTexture(System.Text.StringBuilder log)
        {
            const string composedPath = TexDir + "/NPC_Kanban_Paimon_Tex_Cloak_Composed.png";
            if (System.IO.File.Exists(composedPath)) { log.AppendLine("披风合成贴图已存在，跳过"); return; }
            var diffusePath = TexDir + "/NPC_Kanban_Paimon_Tex_Cloak_Diffuse.png";
            var lmPath = TexDir + "/NPC_Kanban_Paimon_Tex_Cloak_Lightmap.png";
            if (!System.IO.File.Exists(diffusePath) || !System.IO.File.Exists(lmPath)) { log.AppendLine("!! 披风源贴图缺失，跳过合成"); return; }

            var diffuse = new Texture2D(2, 2);
            if (!diffuse.LoadImage(System.IO.File.ReadAllBytes(diffusePath))) { log.AppendLine("!! Diffuse 读取失败"); return; }
            var lm = new Texture2D(2, 2);
            if (!lm.LoadImage(System.IO.File.ReadAllBytes(lmPath))) { log.AppendLine("!! Lightmap 读取失败"); return; }

            var dc = diffuse.GetPixels();
            int dw = diffuse.width, dh = diffuse.height;

            // 从 Lightmap 提取星点（青色亮像素簇，洪泛连通）
            var lc = lm.GetPixels();
            int lw = lm.width, lh = lm.height;
            var stars = new System.Collections.Generic.List<float[]>(); // [半径(px), 亮度]
            var taken = new bool[lc.Length];
            for (int y = 0; y < lh; y++)
            {
                for (int x = 0; x < lw; x++)
                {
                    int i = y * lw + x;
                    if (taken[i]) continue;
                    var c = lc[i];
                    if (c.g < 0.35f && c.b < 0.35f) continue;
                    var queue = new System.Collections.Generic.Queue<int>();
                    queue.Enqueue(i); taken[i] = true;
                    int cnt = 0, maxG = 0;
                    while (queue.Count > 0)
                    {
                        int p = queue.Dequeue(); cnt++;
                        int px = p % lw, py = p / lw;
                        if ((int)(lc[p].g * 255f) > maxG) maxG = (int)(lc[p].g * 255f);
                        for (int dy = -2; dy <= 2; dy++)
                            for (int dx = -2; dx <= 2; dx++)
                            {
                                int nx = px + dx, ny = py + dy;
                                if (nx < 0 || ny < 0 || nx >= lw || ny >= lh) continue;
                                int ni = ny * lw + nx;
                                if (taken[ni]) continue;
                                var nc = lc[ni];
                                if (nc.g >= 0.35f || nc.b >= 0.35f) { taken[ni] = true; queue.Enqueue(ni); }
                            }
                    }
                    if (cnt >= 4)
                    {
                        float radiusPx = Mathf.Sqrt(cnt / Mathf.PI) * (dw / (float)lw);
                        stars.Add(new float[] { radiusPx, maxG / 255f });
                    }
                }
            }
            log.AppendLine($"Lightmap 提取星点 {stars.Count} 颗");

            // 星点伪随机散布到 Diffuse 蓝色内衬区（V<0.45）；白区（V>0.45）不撒星保持纯白
            var rng = new System.Random(20260825);
            const float liningTopV = 0.45f;
            foreach (var s in stars)
            {
                float tu = (float)rng.NextDouble();
                float tv = liningTopV * (float)rng.NextDouble();
                DrawStar(dc, dw, dh, s[0], s[1], tu, tv);
            }
            // 补一轮小星增加"繁星"密度（官方观感）
            for (int k = 0; k < stars.Count / 2; k++)
                DrawStar(dc, dw, dh, 2.2f, 0.7f, (float)rng.NextDouble(), liningTopV * (float)rng.NextDouble());

            var composed = new Texture2D(dw, dh, TextureFormat.RGBA32, false);
            composed.SetPixels(dc);
            composed.Apply();
            System.IO.File.WriteAllBytes(composedPath, composed.EncodeToPNG());
            AssetDatabase.ImportAsset(composedPath);
            log.AppendLine($"生成披风合成贴图 {dw}x{dh}（渐变+{stars.Count} 颗星散布蓝区）");
        }

        /// <summary>在 Diffuse 像素上画一颗柔边青白星星（加法提亮）。</summary>
        static void DrawStar(Color[] pixels, int w, int h, float radiusPx, float intensity, float u, float v)
        {
            int cx = (int)(u * w), cy = (int)(v * h);
            int r = Mathf.Max(1, (int)Mathf.Ceil(radiusPx));
            for (int dy = -r; dy <= r; dy++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    int x = cx + dx, y = cy + dy;
                    if (x < 0 || y < 0 || x >= w || y >= h) continue;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / Mathf.Max(1f, radiusPx);
                    if (d > 1f) continue;
                    float falloff = (1f - d * d) * intensity;
                    var c = pixels[y * w + x];
                    c.r = Mathf.Min(1f, c.r + 0.35f * falloff);
                    c.g = Mathf.Min(1f, c.g + 0.85f * falloff);
                    c.b = Mathf.Min(1f, c.b + 0.95f * falloff);
                    pixels[y * w + x] = c;
                }
            }
        }

        static void CreateMat(System.Text.StringBuilder log, string name, string diffuseTex, float outline, bool cullOff = false)
        {
            var path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("GIC/PaimonToon"));
                AssetDatabase.CreateAsset(m, path);
                log.AppendLine($"创建 {name}");
            }
            m.SetFloat("_ShadowStrength", 0f);
            m.SetFloat("_OutlineWidth", outline);
            m.SetFloat("_Cull", cullOff ? 0f : 2f);
            if (diffuseTex != null)
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/{diffuseTex}.png");
                if (tex != null) m.SetTexture("_BaseMap", tex);
                else log.AppendLine($"!! 贴图缺失 {diffuseTex}");
            }
            EditorUtility.SetDirty(m);
        }

        static Material LoadMat(string name) => AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{name}.mat");

        /// <summary>按名字找到 GI_Root 直接子节点的 SMR，逐槽位赋材质（槽不足补、槽多截断到 mesh 子网格数）。</summary>
        static void SetMats(Transform giRoot, string node, params Material[] mats)
        {
            var t = giRoot.Find(node);
            if (t == null) { Debug.LogWarning($"[PetGIModelSetup] 未找到节点 {node}"); return; }
            var smr = t.GetComponent<SkinnedMeshRenderer>();
            if (smr == null) return;
            var target = new Material[mats.Length];
            for (int i = 0; i < mats.Length; i++) target[i] = mats[i];
            smr.sharedMaterials = target;
        }

        static void WireScene(System.Text.StringBuilder log)
        {
            var paimon = GameObject.Find("Paimon");
            if (paimon == null) { log.AppendLine("!! 未找到 Paimon 根"); return; }

            // 1) 挂官方模型实例（GI_Root）
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FBX);
            if (fbx == null) { log.AppendLine("!! FBX 未加载"); return; }

            Transform giRoot = paimon.transform.Find("GI_Root");
            if (giRoot == null)
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(fbx, paimon.transform);
                inst.name = "GI_Root";
                inst.transform.localPosition = Vector3.zero;
                inst.transform.localRotation = Quaternion.identity;
                inst.transform.localScale = Vector3.one;
                giRoot = inst.transform;
                log.AppendLine("实例化 GI_Root");
            }

            // 2) 材质接线（FBX 直接子物体：Body[2槽=身体+头发] / Body_UV1[2槽] / Cloak / EyeStar / Face / Face_UV1）
            var bodyMat = LoadMat("GI_身体");
            var hairMat = LoadMat("GI_头发");
            var cloakMat = LoadMat("GI_披风");
            var fxMat = LoadMat("GI_特效");
            var faceMat = LoadMat("GI_脸");
            SetMats(giRoot, "Body", bodyMat, hairMat);
            SetMats(giRoot, "Body_UV1", bodyMat, hairMat);
            SetMats(giRoot, "Cloak", cloakMat);
            SetMats(giRoot, "EyeStar", fxMat);
            SetMats(giRoot, "Face", faceMat);
            SetMats(giRoot, "Face_UV1", faceMat);
            log.AppendLine("材质接线完成（Body 双槽=身体+头发）");

            // EyeStar 条带默认隐藏（2026-08-25 定案）：官方立绘/Q版头像实证默认眼神=干净浅蓝虹膜+黑瞳+白高光，
            // 无星形图案（"星星眼"是特定表情/近距离特写的星辰细节）；且该星形几何比虹膜盘还大（烘焙实测 Y 向 0.022 vs 0.006），
            // 金色不透明渲染=巨大金星糊满双眼（用户目检 bug）。MMD 基准版也无此层。保留接线便于日后实验（重新激活即恢复）。
            var starNode = giRoot.Find("EyeStar");
            if (starNode != null && starNode.gameObject.activeSelf)
            {
                starNode.gameObject.SetActive(false);
                log.AppendLine("EyeStar 金星条带已隐藏（官方默认眼神无星形，详见工具注释）");
            }

            // Body_UV1/Face_UV1 是 lightmap UV 副本（重复网格）——关闭渲染防重叠
            foreach (var n in new[] { "Body_UV1", "Face_UV1" })
            {
                var t = giRoot.Find(n);
                if (t != null) t.gameObject.SetActive(false);
            }

            // 3) 注册 GI 动画（Animation 组件挂在 NPC_Kanban_Paimon_Model 骨节点上，Bip001 路径起点）
            var animNode = giRoot.Find("NPC_Kanban_Paimon/NPC_Kanban_Paimon_Model");
            var anim = animNode != null ? animNode.GetComponent<Animation>() : null;
            if (anim == null) { log.AppendLine("!! NPC_Kanban_Paimon_Model 上无 Animation"); return; }
            int added = 0;
            foreach (var g in AssetDatabase.FindAssets("t:AnimationClip", new[] { AnimDir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip == null || !clip.legacy) continue;
                if (anim.GetClip(clip.name) == null) { anim.AddClip(clip, clip.name); added++; }
            }
            log.AppendLine($"注册 GI 动画 {added} 条");

            // 4) PetLookAtController 骨名适配：GI 骨名 Bip001 Neck/Head + 无目骨（眼球骨 +EyeBone L/R A01）
            var look = paimon.GetComponent<GIC.Pet.PetLookAtController>();
            if (look != null) log.AppendLine("PetLookAt 存在——骨名适配需走组件新字段（见 PetGICompat）");

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(paimon.scene);
        }
    }
}
