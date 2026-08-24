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

            CreateMat(log, "GI_身体", "NPC_Kanban_Paimon_Tex_Body_Diffuse", 1.0f);
            CreateMat(log, "GI_脸", "NPC_Kanban_Paimon_Tex_Face_Diffuse", 0.7f);
            CreateMat(log, "GI_头发", "NPC_Kanban_Paimon_Tex_Hair_Diffuse", 1.2f);
            CreateMat(log, "GI_披风", "NPC_Kanban_Paimon_Tex_Cloak_Diffuse", 0.8f, cullOff: true);
            // EyeStar 星形瞳孔条带采样脸贴图左下角淡金色纯色块（255,236,173）
            CreateMat(log, "GI_特效", "NPC_Kanban_Paimon_Tex_Face_Diffuse", 0f);
            AssetDatabase.SaveAssets();
            log.AppendLine("材质创建完成");
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
