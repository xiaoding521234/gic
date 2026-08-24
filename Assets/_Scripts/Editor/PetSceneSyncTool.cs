using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GIC.Editor
{
    /// <summary>
    /// PaimonPet 场景动作同步（2026-08-24）：把管线产物（Animations/MMD/ 全部 _MMD.anim）
    /// 一键注册进 PaimonPet 的 Animation 组件，并重建动作测试 UI（AnimUICanvas，编辑器 Play 专用）。
    /// 单场景方案（2026-08-24）：原 PaimonRetargetTest 独立测试场景已废弃并入本场景——
    /// 管线对输出 clip 是"删旧建新"（GUID 会换），场景引用靠本工具对齐；测试 UI 也由此重建，
    /// 不再有第二个场景需要维护。幂等：清空重注册/删旧重建；默认 clip 按名字保持原引用，丢失则回退 Standby。
    /// </summary>
    public static class PetSceneSyncTool
    {
        private const string ScenePath = "Assets/Scenes/PaimonPet.unity";
        private const string ClipDir = "Assets/Art/PaimonPet/Animations/MMD";

        [MenuItem("Tools/桌宠/同步 PaimonPet 动作列表")]
        public static void Sync()
        {
            var cur = EditorSceneManager.GetActiveScene();
            if (cur.path != ScenePath)
            {
                if (cur.isDirty)
                {
                    Debug.LogError("[PetSceneSync] 当前场景有未保存修改，请先保存再同步（避免静默丢弃）");
                    return;
                }
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
            SyncInternal();
        }

        /// <summary>管线（PaimonRetargetPipeline 步骤 6）重定向完成后调用——此时 PaimonPet 已 Single 打开。</summary>
        public static void SyncInternal()
        {
            var animComps = Object.FindObjectsOfType<Animation>(true);
            if (animComps.Length == 0)
            {
                Debug.LogError("[PetSceneSync] PaimonPet 里没找到 Animation 组件");
                return;
            }
            if (animComps.Length > 1)
                Debug.LogWarning($"[PetSceneSync] 场景里有 {animComps.Length} 个 Animation，取第一个（{animComps[0].name}）");
            var anim = animComps[0];

            var clips = AssetDatabase.FindAssets("t:AnimationClip", new[] { ClipDir })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".anim"))
                .Select(p => AssetDatabase.LoadAssetAtPath<AnimationClip>(p))
                .Where(c => c != null)
                .OrderBy(c => c.name)
                .ToList();
            if (clips.Count == 0)
            {
                Debug.LogError($"[PetSceneSync] {ClipDir} 下没有 clip，请先重跑重定向管线");
                return;
            }

            // 默认 clip 按名字保留（RemoveClip 后引用会失效，先记名再找回同名新资产）
            var keepDefaultName = anim.clip != null ? anim.clip.name : null;

            // 读当前注册名（Animation 无公开枚举 API，走 SerializedObject）
            var oldNames = new List<string>();
            var so = new SerializedObject(anim);
            var arr = so.FindProperty("m_Animations");
            for (int i = 0; i < arr.arraySize; i++)
            {
                var c = arr.GetArrayElementAtIndex(i).objectReferenceValue as AnimationClip;
                if (c != null) oldNames.Add(c.name);
            }
            foreach (var n in oldNames) anim.RemoveClip(n);
            foreach (var c in clips) anim.AddClip(c, c.name);

            var keepDefault = keepDefaultName != null ? clips.FirstOrDefault(c => c.name == keepDefaultName) : null;
            anim.clip = keepDefault
                        ?? clips.FirstOrDefault(c => c.name.EndsWith("Standby_MMD"))
                        ?? clips[0];

            EditorUtility.SetDirty(anim);

            重建动作测试UI(clips);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            Debug.Log($"[PetSceneSync] 注册 {clips.Count} 个 clip 到 {anim.name}（旧 {oldNames.Count} 个已清）+ 测试 UI 已重建，默认={anim.clip.name}，场景已保存");
        }

        /// <summary>重建动作测试 UI（AnimUICanvas + EventSystem，编辑器 Play 测动作用；
        /// 构建版由 PetEditorOnly 在 Awake 自禁用——桌宠进程不出现测试面板）。
        /// 按钮持久绑定场景内 PetAnimSwapper.Play(clip名)，运行时零查找。</summary>
        private static void 重建动作测试UI(List<AnimationClip> clips)
        {
            var swapper = Object.FindObjectsOfType<GIC.Pet.PetAnimSwapper>(true).FirstOrDefault();
            if (swapper == null)
            {
                Debug.LogError("[PetSceneSync] 场景里没找到 PetAnimSwapper，测试 UI 未重建");
                return;
            }

            // 删旧重建（幂等；含上一次生成的面板/事件系统）
            foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root.name == "AnimUICanvas" || root.name == "EventSystem") Object.DestroyImmediate(root);
            }

            var canvasGo = new GameObject("AnimUICanvas", typeof(UnityEngine.Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster), typeof(GIC.Pet.PetEditorOnly));
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

            // 中文按钮名映射（2026-08-23）：clip 英文名 → 按钮显示中文；未列名按原名兜底
            var 中文名 = new Dictionary<string, string>
            {
                ["Standby"] = "待机", ["Greet"] = "打招呼", ["Anger"] = "生气", ["Sneer01"] = "坏笑",
                ["Clap01"] = "鼓掌", ["Nod01"] = "点头", ["ShakeHead01"] = "摇头", ["Refuse01"] = "拒绝",
                ["Run"] = "跑动", ["SitLoop"] = "坐姿", ["Sleep01"] = "睡觉", ["Turnback"] = "转身",
                ["Domagic"] = "施法",
                ["Shy01AS"] = "害羞·入场", ["Shy01BS"] = "害羞·退场", ["Shy01Loop"] = "害羞·循环",
                ["Confuse01AS"] = "困惑·入场", ["Confuse01BS"] = "困惑·退场", ["Confuse01Loop"] = "困惑·循环",
                ["Think01AS"] = "思考·入场", ["Think01BS"] = "思考·退场", ["Think01Loop"] = "思考·循环",
                ["Like01AS"] = "点赞·入场", ["Like01BS"] = "点赞·退场", ["Like01Loop"] = "点赞·循环",
            };
            // LegacyRuntime.ttf 无中文字形，按钮名换中文必须换字体（沿用项目 zh-cn.ttf，TMP 目录同款）
            var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/TextMesh Pro/Resources/Fonts & Materials/zh-cn.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            foreach (var c in clips)
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
                var en = c.name.Replace("Ani_Cs_NPC_Kanban_Paimon_", "").Replace("Ani_NPC_Kanban_Paimon_", "").Replace("_MMD", "");
                txt.text = 中文名.TryGetValue(en, out var zh) ? zh : en;
                var tr = txtGo.GetComponent<RectTransform>();
                tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.sizeDelta = Vector2.zero;
                // 持久监听（存进场景文件，运行时零查找）
                UnityEditor.Events.UnityEventTools.AddStringPersistentListener(
                    btnGo.GetComponent<UnityEngine.UI.Button>().onClick, swapper.Play, c.name);
            }

            // EventSystem（场景预置，同样构建版自禁用）
            var esGo = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem), typeof(UnityEngine.EventSystems.StandaloneInputModule), typeof(GIC.Pet.PetEditorOnly));
        }
    }
}
