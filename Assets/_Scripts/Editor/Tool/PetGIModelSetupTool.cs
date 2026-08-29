using UnityEditor;
using UnityEngine;

namespace GIC.Editor
{
    /// <summary>
    /// GI 官方模型落地工具（2026-08-24 纯原神资产路线）：
    /// 1) 给 Assets/Art/PaimonPet/GI/Paimon.fbx 的各网格建 PaimonToon 材质（v5=原神同款卡渲：Ramp 阴影
    ///    + 面部 SDF + 边缘光，虚拟光方向自包含、不依赖场景灯——"桌宠不需要光照"指不接场景光照系统）
    /// 2) 在 PaimonPet 场景把官方模型挂到 Paimon 根下（GI_Root），注册 GI 原始 .anim（Bip001 路径零重定向直读）
    /// 3) 视线/眨眼/情绪/手指控制器走骨名适配（GI 骨名与 MMD 不同：Bip001 Neck/Head、Eye_WinkA morph 等）
    /// 幂等：重复执行只补缺，不重复创建。
    ///
    /// 2026-08-25 材质槽修复：Body 网格有 2 个子网格/材质槽 = [身体 | 头发]——头发与虹膜几何都在槽1。
    /// 旧版用 sharedMaterial 单值赋值把两槽都压成身体材质，导致头发渲染成身体贴图、虹膜渲染成白布（"头发纹理
    /// 错误/眼睛只有眼白"两大目检 bug 的根因）。现按槽位逐一赋值。EyeStar 条带（双眼星形瞳孔）采样脸贴图
    /// 左下角的纯色色块（RGB 255,236,173 淡金色，全不透明），贴脸贴图取色。
    ///
    /// 2026-08-25 v5 卡渲重开：用户提供官方实际截图拍板"渲染和原神一样"。2026-08-22 的全关卡渲是 MMD 贴图
    /// 配官方 ramp 导致"被照成绿"时代的产物；官方模型落地后根因已消。ramp 行按 256x20 ramp 实测选干净行
    /// （见 UpgradeToonV5 注释），避开 body ramp 0.4 行黄绿亮端 (0.92,0.93,0.82)。
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

            // 贴图导入修正（幂等）：Ramp 是 1D 颜色查找表须关 mipmap（跨行/跨级混合出脏色）；SDF 是阈值数据须线性（sRGB off）
            FixImport("NPC_Kanban_Paimon_Tex_Body_Shadow_Ramp", srgb: true, mipmap: false);
            FixImport("NPC_Kanban_Paimon_Tex_Hair_Shadow_Ramp", srgb: true, mipmap: false);
            FixImport("NPC_Kanban_Paimon_Tex_Face_Lightmap_A_R_G", srgb: false, mipmap: false);

            // 卡渲风格 v4（2026-08-25 目检迭代）：v3 描边生效但眉毛/眼球也被描——反转壳原理上无法区分
            // 屏幕外轮廓与腔内浮层小几何，各自成环。修：①脸描边归零（原神脸部近乎无描边，头轮廓由头发描边承担）；
            // ②头发材质加 UV 裁剪盒掐掉虹膜区（虹膜采样头发贴图右下星空区，头发描边保留）。
            const float styleVer = 4f;
            var outlineBrown = new Color(0.14f, 0.09f, 0.06f, 1f);
            var noClip = new Vector4(0f, 0f, 0f, 0f);
            var irisClip = new Vector4(0.53f, 0.01f, 0.99f, 0.19f); // 虹膜 UV 盒（docs/19 实测）
            // 描边宽度：v4 首版 1.2/1.0/0.8 → 0.9/0.75/0.6 → 0.4/0.35/0.3 → v6 第三轮目检"再调细"
            // （2026-08-25）：0.2/0.18/0.15（脸恒 0）
            CreateMat(log, "GI_身体", "NPC_Kanban_Paimon_Tex_Body_Diffuse", 0.2f, styleVer, outlineCol: outlineBrown, clipUV: noClip);
            CreateMat(log, "GI_脸", "NPC_Kanban_Paimon_Tex_Face_Diffuse", 0f, styleVer, outlineCol: outlineFace_unused(), clipUV: noClip); // 描边宽=0：眉毛是脸网格浮层几何，按槽不可分
            CreateMat(log, "GI_头发", "NPC_Kanban_Paimon_Tex_Hair_Diffuse", 0.18f, styleVer, outlineCol: outlineBrown, clipUV: irisClip);
            // 披风用合成贴图（渐变+星光）；合成失败时回退纯 Diffuse
            var cloakTex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/NPC_Kanban_Paimon_Tex_Cloak_Composed.png") != null
                ? "NPC_Kanban_Paimon_Tex_Cloak_Composed" : "NPC_Kanban_Paimon_Tex_Cloak_Diffuse";
            CreateMat(log, "GI_披风", cloakTex, 0.15f, styleVer, outlineCol: outlineBrown, clipUV: noClip, cullOff: true);
            CreateMat(log, "GI_特效", "NPC_Kanban_Paimon_Tex_Face_Diffuse", 0f, 0f); // 隐藏层不参与风格

            // v5→v6 卡渲试验→v7 定妆（2026-08-25 三轮目检收敛）：v5/v6 开 ramp 调子+边缘光+SDF，
            // 目检实证桌宠场景（小窗、无环境光）下 ramp 调子读作"脏色/发绿"（头发 ramp 过渡带绿占优段+
            // 白衣薰衣草紫影整体读脏）。用户拍板"不需要阴影"——v7 全关，输出=纯官方贴图×细描边。
            // ramp/SDF 贴图与行选档仍接线（身体0.25/脸0.75/头发Hair0.40/披风0.925），Inspector 随时调回。
            UpgradeToonV7(log, "GI_身体", "NPC_Kanban_Paimon_Tex_Body_Shadow_Ramp", 0.25f);
            UpgradeToonV7(log, "GI_脸", "NPC_Kanban_Paimon_Tex_Body_Shadow_Ramp", 0.75f, true);
            UpgradeToonV7(log, "GI_头发", "NPC_Kanban_Paimon_Tex_Hair_Shadow_Ramp", 0.40f);
            UpgradeToonV7(log, "GI_披风", "NPC_Kanban_Paimon_Tex_Body_Shadow_Ramp", 0.925f);

            AssetDatabase.SaveAssets();
            log.AppendLine("材质创建完成");
        }

        /// <summary>贴图导入设置修正（Ramp/SDF 类）。幂等：已是目标设置则跳过。</summary>
        static void FixImport(string texName, bool srgb, bool mipmap)
        {
            var path = $"{TexDir}/{texName}.png";
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) return;
            if (imp.sRGBTexture == srgb && imp.mipmapEnabled == mipmap) return;
            imp.sRGBTexture = srgb;
            imp.mipmapEnabled = mipmap;
            imp.SaveAndReimport();
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

        static Color outlineFace_unused() => new Color(0.22f, 0.14f, 0.10f, 1f);

        /// <summary>创建/升级材质。风格参数（v4=纯贴图+描边色+UV 裁剪盒）只在 styleVer 提升（_StyleVer &lt; styleVer）时写一次，
        /// 用户 Inspector 调参不被重跑覆盖；贴图引用与描边宽度/剔除始终对齐（非风格项）。</summary>
        static void CreateMat(System.Text.StringBuilder log, string name, string diffuseTex, float outline,
            float styleVer = 0f, Color outlineCol = default(Color), Vector4 clipUV = default(Vector4), bool cullOff = false)
        {
            var path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("GIC/PaimonToon"));
                AssetDatabase.CreateAsset(m, path);
                m.SetFloat("_StyleVer", 0f); // 新材质从 v0 起步，走下方风格升级路径
                log.AppendLine($"创建 {name}");
            }

            // 非风格项（接线性质，幂等对齐）
            m.SetFloat("_OutlineWidth", outline);
            m.SetFloat("_Cull", cullOff ? 0f : 2f);
            // 描边颜色模式（2026-08-26 原神式升级，幂等对齐）：1=基色暗化（描边=主贴图像素×_OutlineTint），
            // 0=固定色。默认走 1——原神描边随固有色变化（白衣冷灰/肤色暖棕/深发深发色），固定暖棕是旧定妆。
            m.SetFloat("_OutlineColorMode", 1f);
            if (diffuseTex != null)
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/{diffuseTex}.png");
                if (tex != null) m.SetTexture("_BaseMap", tex);
                else log.AppendLine($"!! 贴图缺失 {diffuseTex}");
            }

            // 风格项（版本门控，写一次）：v4=无光照（阴影/边缘光/SDF 全零）+ 原神式暖棕描边色 + 描边 UV 裁剪盒
            if (styleVer > 0f && m.GetFloat("_StyleVer") < styleVer)
            {
                m.SetFloat("_StyleVer", styleVer);
                m.SetFloat("_ShadowStrength", 0f);
                m.SetFloat("_RimStrength", 0f);
                // 基色暗化描边系数（2026-08-26）：白衣描边≈0.5 灰、肤色≈0.45 暖棕、深发≈0.4 深发色——
                // 逐通道系数让暖色暗得暖、冷色暗得冷，比统一系数更贴原神观感
                m.SetColor("_OutlineTint", new Color(0.5f, 0.45f, 0.4f, 1f));
                EnableKeyword(m, "_FACESHADOW", false);
                if (outlineCol.a > 0f) m.SetColor("_OutlineColor", outlineCol); // default(Color) 全零=不覆盖
                if (clipUV.z > clipUV.x) m.SetVector("_OutlineClipUV", clipUV);
                log.AppendLine($"风格升级 {name} → v{styleVer}（纯贴图+基色暗化描边，UV裁剪={(clipUV.z > clipUV.x ? clipUV.ToString("F2") : "none")}）");
            }
            EditorUtility.SetDirty(m);
        }

        /// <summary>v5→v6 卡渲试验 → v7 定妆（2026-08-25 三轮目检收敛）。
        /// v5 开 ramp 调子（soft 0.15）→ 头发 ramp 0.40 行过渡带 u∈[0.82,0.92] 绿占优段被大面积采样=头发发绿；
        /// v6 收窄过渡（soft 0.03）后用户仍报"全身发绿"并拍板"不需要阴影"——桌宠小窗无环境光，
        /// ramp 调子整体读作脏色（白衣薰衣草紫影+皮肤紫灰影小尺寸下不清新），阴影是负资产。
        /// v7：_ShadowStrength=0 + _RimStrength=0 + SDF 关——输出=纯官方贴图×已验证细描边（0.2/0.18/0.15）。
        /// ramp/SDF 贴图与行选档仍接线保留（Inspector 调 _ShadowStrength/_RimStrength/_FACESHADOW 随时回来）。
        /// 版本门控同 CreateMat（_StyleVer &lt; 7 才写，用户 Inspector 调参不被重跑覆盖）。
        /// ramp 行选档（ramp 256x20 行带制，见 docs/19）：
        ///   身体 0.25=布料带（暗端薰衣草紫 0.84,0.73,0.94 × 亮端暖白）；
        ///   脸 0.75=皮肤带（暗端紫灰 × 亮端桃暖）；
        ///   头发 Hair 0.40=冷紫灰暗端 × 近白亮端（避开 body ramp 0.4 行的黄绿亮端）；
        ///   披风 0.925=冷灰暗端（深蓝披风压暗一档，星点区不被吞）。</summary>
        static void UpgradeToonV7(System.Text.StringBuilder log, string name, string rampTex, float rampV, bool faceSdf = false)
        {
            var m = LoadMat(name);
            if (m == null) { log.AppendLine($"!! v7 跳过：{name} 不存在"); return; }
            if (m.GetFloat("_StyleVer") >= 7f) { log.AppendLine($"v7 已应用 {name}，跳过"); return; }

            var ramp = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/{rampTex}.png");
            if (ramp != null) m.SetTexture("_Ramp", ramp);
            else log.AppendLine($"!! Ramp 贴图缺失 {rampTex}");
            m.SetFloat("_RampV", rampV);
            m.SetFloat("_ShadowThreshold", 0.55f);
            m.SetFloat("_ShadowSoftness", 0.03f);
            m.SetFloat("_ShadowStrength", 0f); // v7 无阴影定妆：输出=纯官方贴图
            m.SetFloat("_RimPower", 3.5f);
            m.SetFloat("_RimStrength", 0f);
            m.SetVector("_LightDir", new Vector4(0.4f, 0.65f, 0.65f, 0f)); // 虚拟光留接线，Inspector 调回即用

            if (faceSdf)
            {
                // SDF 贴图/参数留接线（keyword 关闭：shadow=0 时无输出，关掉省一个变体）
                var sdf = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/NPC_Kanban_Paimon_Tex_Face_Lightmap_A_R_G.png");
                if (sdf != null) m.SetTexture("_FaceSDFMap", sdf);
                else log.AppendLine("!! SDF 贴图缺失");
                m.SetFloat("_FaceShadowRange", 0.9f);
                m.SetFloat("_FaceShadowSoftness", 0.04f);
            }
            EnableKeyword(m, "_FACESHADOW", false);
            m.SetFloat("_StyleVer", 7f);
            EditorUtility.SetDirty(m);
            log.AppendLine($"v7 定妆 {name}: 无阴影纯贴图（ramp 行 {rampV} 留接线可调回）");
        }

        static void EnableKeyword(Material m, string kw, bool on)
        {
            // 数组法：Material.DisableKeyword 对 shader_feature 局部关键字偶发不落盘（2026-08-25 GI_脸实证），
            // 直接改写 shaderKeywords 数组最稳；_FACESHADOW 这类双状态（开=变体生效）没有 _ON 浮点镜像位
            if (on)
            {
                if (System.Array.IndexOf(m.shaderKeywords, kw) < 0)
                {
                    var arr = new string[m.shaderKeywords.Length + 1];
                    m.shaderKeywords.CopyTo(arr, 0);
                    arr[arr.Length - 1] = kw;
                    m.shaderKeywords = arr;
                }
            }
            else
            {
                // 2026-08-25 实证：FindAll 移除法对 shader_feature 局部关键字也无效（Tuanjie 只增不删），
                // 唯一可靠移除=清空整个局部关键字数组——PaimonToon 材质唯一用到的局部关键字就是 _FACESHADOW，
                // 清空即安全；将来加新局部关键字需改回逐个保留
                m.shaderKeywords = new string[0];
            }
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

            // 5) GI 影子壳（2026-08-25）：剪影→高斯→合成管线复用（PaimonDropShadowController），
            //    壳本体换 GI 网格——Body（含头发槽）+Cloak 两 SMR 场景副本共享骨骼，姿势逐帧同步。
            BuildGIDropShadow(paimon, giRoot, log);

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(paimon.scene);
        }

        /// <summary>GI 影子壳：旧 MMD 壳改名留兜底；新建 _DropShadow 节点（镜像 GI_Root 世界姿态），
        /// 内放 Body/Cloak 的 SMR 副本（Object.Instantiate 场景实例——网格/骨骼/蒙皮数据全引用共享），
        /// 全槽 PaimonShadowSilhouette 材质、进 PaimonShadow 层（主相机剔除，只被影子相机渲染）。
        /// 幂等：_DropShadow 已含 GI 副本则跳过。</summary>
        static void BuildGIDropShadow(GameObject paimon, Transform giRoot, System.Text.StringBuilder log)
        {
            var silhouette = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/PaimonPet/Materials/PaimonShadowSilhouette.mat");
            if (silhouette == null) { log.AppendLine("!! PaimonShadowSilhouette.mat 未找到，影子壳跳过"); return; }
            var shadowLayer = LayerMask.NameToLayer("PaimonShadow");
            if (shadowLayer < 0) { log.AppendLine("!! PaimonShadow 层不存在，影子壳跳过"); return; }

            // 旧 MMD 壳让位（改名保留兜底：恢复 MMD 时改回 _DropShadow 即可）
            var oldShell = paimon.transform.Find("_DropShadow");
            if (oldShell != null && oldShell.name == "_DropShadow")
            {
                var oldSmr = oldShell.GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (oldSmr != null && oldSmr.sharedMesh != null && oldSmr.sharedMesh.name.Contains("Paimon_mesh"))
                {
                    oldShell.name = "MMD_DropShadow";
                    log.AppendLine("旧 MMD 影子壳改名 MMD_DropShadow（禁用兜底保留）");
                }
            }

            var shell = paimon.transform.Find("_DropShadow");
            if (shell != null && shell.childCount > 0 && shell.GetComponentInChildren<SkinnedMeshRenderer>(true) != null
                && shell.GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMesh.name.Contains("Body"))
            {
                log.AppendLine("GI 影子壳已存在，跳过");
            }
            else
            {
                if (shell == null)
                {
                    shell = new GameObject("_DropShadow").transform;
                    shell.SetParent(paimon.transform, false);
                }
                // 壳节点镜像 GI_Root 世界姿态，副本保持原局部值 → 蒙皮跟随骨骼（副本自身 transform 不参与蒙皮矩阵）
                shell.SetPositionAndRotation(giRoot.position, giRoot.rotation);
                shell.localScale = giRoot.lossyScale;

                foreach (var nodeName in new[] { "Body", "Cloak" })
                {
                    var src = giRoot.Find(nodeName);
                    if (src == null) { log.AppendLine($"!! GI_Root 下无 {nodeName}，影子壳缺该件"); continue; }
                    var copy = Object.Instantiate(src.gameObject, shell, false);
                    copy.name = nodeName + "_ShadowShell";
                    copy.layer = shadowLayer;
                    var smr = copy.GetComponent<SkinnedMeshRenderer>();
                    if (smr != null)
                    {
                        var mats = new Material[smr.sharedMaterials.Length];
                        for (int i = 0; i < mats.Length; i++) mats[i] = silhouette;
                        smr.sharedMaterials = mats;
                        smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        smr.receiveShadows = false;
                        smr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                        smr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                    }
                    foreach (var extra in copy.GetComponentsInChildren<UnityEngine.Behaviour>(true))
                        if (extra != null && extra != smr) extra.enabled = false;
                }
                log.AppendLine("GI 影子壳创建完成（Body+Cloak 剪影副本，共享骨骼）");
            }

            // 改绑控制器序列化引用（旧引用随改名指向 MMD 壳）+ 恢复启用
            // （启用开关在 MMD→GI 切换期被关——影子壳缺位时代的"影子暂禁用"，Awake 照常跑但 LateUpdate 直接 return，
            //  表现为"初始化成功但永远看不见影子"，2026-08-25 Player.log 实证：有 init 日志无 RT 日志）
            var ctrl = paimon.GetComponent<GIC.Pet.PaimonDropShadowController>();
            if (ctrl != null)
            {
                var so = new SerializedObject(ctrl);
                var prop = so.FindProperty("影子渲染器");
                var firstSmr = shell.GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (prop != null && firstSmr != null && prop.objectReferenceValue != firstSmr)
                {
                    prop.objectReferenceValue = firstSmr;
                    log.AppendLine("阴影控制器引用已改绑 GI 壳");
                }
                var en = so.FindProperty("启用");
                if (en != null && !en.boolValue)
                {
                    en.boolValue = true;
                    ctrl.enabled = true;
                    log.AppendLine("阴影控制器 启用 开关已恢复（MMD→GI 切换期被关闭）");
                }
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }
}
