using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GIC.Editor
{
    /// <summary>
    /// PaimonPet 场景动作同步（2026-08-24 建；2026-08-25 切 GI 时代）：把 GI 原始 .anim
    /// （Assets/Art/PaimonPet/GI/Animations/，199 条零重定向直读）一键注册进 PaimonPet 的
    /// Animation 组件（PetAnimSwapper.目标动画 所指），并重建动作测试 UI（AnimUICanvas，编辑器 Play 专用）。
    /// 单场景方案（2026-08-24）：原 PaimonRetargetTest 独立测试场景已废弃并入本场景——
    /// 测试 UI 也由此重建。幂等：清空重注册/删旧重建；默认 clip 按名字保持原引用，丢失则回退 Standby。
    /// MMD 重定向产物（Animations/MMD/）已随官方模型落地退出使用，仅兜底留存不再同步。
    /// </summary>
    public static class PetSceneSyncTool
    {
        private const string ScenePath = "Assets/Scenes/PaimonPet.unity";
        private const string ClipDir = "Assets/Art/PaimonPet/GI/Animations";

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
            // 目标 Animation = PetAnimSwapper.目标动画（唯一权威来源，勿按 FindObjectsOfType 顺序取——场景曾残留空 Animation 组件）
            var swapper = Object.FindObjectsOfType<GIC.Pet.PetAnimSwapper>(true).FirstOrDefault();
            if (swapper == null)
            {
                Debug.LogError("[PetSceneSync] 场景里没找到 PetAnimSwapper");
                return;
            }
            var anim = swapper.TargetAnimation;
            if (anim == null)
            {
                Debug.LogError("[PetSceneSync] PetAnimSwapper.TargetAnimation 未指定");
                return;
            }

            var clips = AssetDatabase.FindAssets("t:AnimationClip", new[] { ClipDir })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".anim"))
                .Select(p => AssetDatabase.LoadAssetAtPath<AnimationClip>(p))
                .Where(c => c != null && c.legacy)
                .OrderBy(c => c.name)
                .ToList();
            if (clips.Count == 0)
            {
                Debug.LogError($"[PetSceneSync] {ClipDir} 下没有 clip");
                return;
            }
            // 排除 GI 程序化姿势层（Left/Right Arm/Hand/Forearm/Finger ~100 条 0.017s 单帧姿势——非完整动作，
            // 列进测试面板只会刷屏；HeadControlRotation 同理）
            clips = clips.Where(c => !c.name.Contains("LeftArm") && !c.name.Contains("RightArm")
                                     && !c.name.Contains("LeftHand") && !c.name.Contains("RightHand")
                                     && !c.name.Contains("LeftForearm") && !c.name.Contains("RightForearm")
                                     && !c.name.Contains("LeftFinger") && !c.name.Contains("RightFinger")
                                     && !c.name.Contains("HeadControlRotation"))
                         .ToList();

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
                        ?? clips.FirstOrDefault(c => c.name.EndsWith("_Standby"))
                        ?? clips.FirstOrDefault(c => c.name.EndsWith("_Standby_MMD"))
                        ?? clips[0];

            EditorUtility.SetDirty(anim);

            WireInertializer(anim, clips, swapper);

            RebuildAnimTestUI(clips);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            Debug.Log($"[PetSceneSync] 注册 {clips.Count} 个 clip 到 {anim.name}（旧 {oldNames.Count} 个已清）+ 测试 UI 已重建，默认={anim.clip.name}，场景已保存");
        }

        /// <summary>惯性化器接线（2026-08-27）：确保 swapper 物体上有 PetInertializer，
        /// 骨列表填为全部 clip 曲线绑定路径的并集（父->子排序）。非动画骨（眼球本体等叠加层
        /// 地盘）因无曲线天然排除。曲线路径经 SerializedObject 直读 m_*Curves——
        /// GetCurveBindings 对 legacy clip 不可信（2026-08-25 实证会被 m_EditorCurves 劫持/返回空）。
        /// 幂等：组件已存在只刷新骨列表；swapper 引用缺失则补。</summary>
        private static void WireInertializer(Animation anim, List<AnimationClip> clips, GIC.Pet.PetAnimSwapper swapper)
        {
            var inert = swapper.GetComponent<GIC.Pet.PetInertializer>();
            if (inert == null) inert = swapper.gameObject.AddComponent<GIC.Pet.PetInertializer>();

            var paths = new HashSet<string>();
            foreach (var c in clips)
            {
                var so = new SerializedObject(c);
                foreach (var prop in new[] { "m_RotationCurves", "m_PositionCurves", "m_ScaleCurves", "m_FloatCurves" })
                {
                    var arr = so.FindProperty(prop);
                    if (arr == null || !arr.isArray) continue;
                    for (int i = 0; i < arr.arraySize; i++)
                    {
                        var p = arr.GetArrayElementAtIndex(i).FindPropertyRelative("path");
                        if (p != null && !string.IsNullOrEmpty(p.stringValue)) paths.Add(p.stringValue);
                    }
                }
            }

            var bones = new List<(int depth, Transform t)>();
            int unparsed = 0;
            foreach (var path in paths)
            {
                var t = anim.transform.Find(path);
                if (t == null) { unparsed++; continue; } // 哈希路径骨不在 FBX 骨架（哈希修复工具已知遗留），预期
                bones.Add((path.Split('/').Length, t));
            }
            var sorted = bones.OrderBy(b => b.depth).Select(b => b.t).ToList();

            var soI = new SerializedObject(inert);
            var arrI = soI.FindProperty("骨列表");
            arrI.arraySize = sorted.Count;
            for (int i = 0; i < sorted.Count; i++) arrI.GetArrayElementAtIndex(i).objectReferenceValue = sorted[i];
            soI.ApplyModifiedPropertiesWithoutUndo();

            var soS = new SerializedObject(swapper);
            var pRef = soS.FindProperty("惯性化器");
            if (pRef.objectReferenceValue == null)
            {
                pRef.objectReferenceValue = inert;
                soS.ApplyModifiedPropertiesWithoutUndo();
            }
            Debug.Log($"[PetSceneSync] 惯性化器接线：{sorted.Count} 骨入列表（未解析路径 {unparsed} 个=哈希遗留骨，预期）");
        }

        /// <summary>重建动作测试 UI（AnimUICanvas + EventSystem，编辑器 Play 测动作用；
        /// 构建版由 PetEditorOnly 在 Awake 自禁用——桌宠进程不出现测试面板）。
        /// 按钮持久绑定场景内 PetAnimSwapper.Play(clip名)，运行时零查找。</summary>
        private static void RebuildAnimTestUI(List<AnimationClip> clips)
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
            var cnName = new Dictionary<string, string>
            {
                ["Standby"] = "待机", ["Greet"] = "打招呼", ["Anger"] = "生气", ["Sneer01"] = "坏笑",
                ["Clap01"] = "鼓掌", ["Nod01"] = "点头", ["ShakeHead01"] = "摇头", ["Refuse01"] = "拒绝",
                ["Run"] = "跑动", ["SitLoop"] = "坐姿", ["Sleep01"] = "睡觉", ["Turnback"] = "转身",
                ["Drag01"] = "拎起",
                ["Domagic"] = "施法",
                ["Shy01AS"] = "害羞·入场", ["Shy01BS"] = "害羞·退场", ["Shy01Loop"] = "害羞·循环",
                ["Confuse01AS"] = "困惑·入场", ["Confuse01BS"] = "困惑·退场", ["Confuse01Loop"] = "困惑·循环",
                ["Think01AS"] = "思考·入场", ["Think01BS"] = "思考·退场", ["Think01Loop"] = "思考·循环",
                ["Like01AS"] = "点赞·入场", ["Like01BS"] = "点赞·退场", ["Like01Loop"] = "点赞·循环",
                ["Appear"] = "出现", ["Disappear"] = "消失",
                ["Show_1"] = "展示1", ["Show_2"] = "展示2", ["Show_3"] = "展示3", ["Show_4"] = "展示4",
                ["Hope"] = "期待", ["Guide"] = "引导",
                ["Chat01AS"] = "聊天01·入场", ["Chat01BS"] = "聊天01·退场", ["Chat01Loop"] = "聊天01·循环",
                ["Chat02AS"] = "聊天02·入场", ["Chat02BS"] = "聊天02·退场", ["Chat02Loop"] = "聊天02·循环",
                ["LookAround01AS"] = "张望·入场", ["LookAround01BS"] = "张望·退场", ["LookAround01Loop"] = "张望·循环",
                ["Whisper01AS"] = "悄悄话·入场", ["Whisper01BS"] = "悄悄话·退场", ["Whisper01Loop"] = "悄悄话·循环",
                ["Shrug01AS"] = "耸肩·入场", ["Shrug01BS"] = "耸肩·退场", ["Shrug01Loop"] = "耸肩·循环",
                ["Akimbo01AS"] = "叉腰·入场", ["Akimbo01BS"] = "叉腰·退场", ["Akimbo01Loop"] = "叉腰·循环",
                ["HoldArm01AS"] = "抱臂·入场", ["HoldArm01BS"] = "抱臂·退场", ["HoldArm01Loop"] = "抱臂·循环",
                ["CoverEyes01AS"] = "捂眼·入场", ["CoverEyes01BS"] = "捂眼·退场", ["CoverEyes01Loop"] = "捂眼·循环",
                ["PutHand01AS"] = "摊手·入场", ["PutHand01BS"] = "摊手·退场", ["PutHand01Loop"] = "摊手·循环",
                ["StrikeChest01AS"] = "捶胸·入场", ["StrikeChest01BS"] = "捶胸·退场", ["StrikeChest01Loop"] = "捶胸·循环",
                ["Forerake01AS"] = "前挠·入场", ["Forerake01BS"] = "前挠·退场", ["Forerake01Loop"] = "前挠·循环",
                ["Backrake01AS"] = "后挠·入场", ["Backrake01BS"] = "后挠·退场", ["Backrake01Loop"] = "后挠·循环",
                ["PointL01AS"] = "指左·入场", ["PointL01BS"] = "指左·退场", ["PointL01Loop"] = "指左·循环",
                ["PointR01AS"] = "指右·入场", ["PointR01BS"] = "指右·退场", ["PointR01Loop"] = "指右·循环",
                ["Nod02"] = "点头02", ["ShakeHead02"] = "摇头02",
                ["FlyMove"] = "飞行移动", ["NormalMove"] = "普通移动",
                ["Turn_90L_AS"] = "左转90·入场", ["Turn_90L_BS"] = "左转90·退场",
                ["Turn_90R_AS"] = "右转90·入场", ["Turn_90R_BS"] = "右转90·退场",
                ["DiveRunAS"] = "俯冲跑·入场", ["DiveRunBS"] = "俯冲跑·退场", ["DiveRunLoop"] = "俯冲跑·循环",
                ["DiveRunDownAS"] = "俯冲下潜·入场", ["DiveRunDownBS"] = "俯冲下潜·退场", ["DiveRunDownLoop"] = "俯冲下潜·循环",
                ["DiveRunUpAS"] = "俯冲上浮·入场", ["DiveRunUpBS"] = "俯冲上浮·退场", ["DiveRunUpLoop"] = "俯冲上浮·循环",
                ["DiveWalkAS"] = "俯冲走·入场", ["DiveWalkBS"] = "俯冲走·退场", ["DiveWalkLoop"] = "俯冲走·循环",
                ["DiveTurn90L"] = "俯冲左转90", ["DiveTurn90R"] = "俯冲右转90",
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
                txt.text = cnName.TryGetValue(en, out var zh) ? zh : en;
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
