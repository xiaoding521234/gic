using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Video;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using GIC.Data;
using GIC.Framework;
using GIC.Battle;

namespace GIC.Editor
{


    /// <summary>
    /// 时轮编辑器（B-S2，docs/active/28 §7）：多轨道技能时间轴可视化编辑窗。
    /// 入口 Tools/TG/时轮编辑器；选中 SkillTimelineAsset 打开自动载入（也可顶部 ObjectField 切换）。
    ///
    /// 结构：资产栏（选择/新建/接线 UnitConfig/保存）→ 头部（totalTime/aimMode）→
    /// 时间标尺 + 七轨行（clip 块绝对定位、点击选中、水平拖拽改时刻、右缘拖拽调时长）→
    /// 选中 clip 属性面板（按轨道显示载荷字段）→ 校验提示行。
    ///
    /// 编辑语义（gic-editor-tool 规范）：修改直写 C# 对象 + Undo.RecordObject（Ctrl+Z 可撤销），
    /// 「保存到磁盘」=SetDirty+SaveAssets；时刻吸附 0.05s；BuildUI 幂等（重入先 Clear）。
    /// 分工铁律提示（docs/active/28 §3）：时间与规格归时轮，数值归 SkillParamKey——
    /// 判定轨不存伤害数值（伤害%/发数/消耗在技能参数表），编辑器只管何时发生与投射物规格。
    /// </summary>
    public class SkillTimelineEditor : EditorWindow
    {
        [MenuItem("Tools/TG/时轮编辑器")]
        public static void Open()
        {
            var window = GetWindow<SkillTimelineEditor>();
            window.titleContent = new GUIContent("时轮编辑器");
            window.minSize = new Vector2(1040f, 720f); // 两栏布局（左时间轴+右校准列 540）——易用性重排 2026-10-05
            // 窗口残留旧窄尺寸时拉宽到两栏舒适宽（画布 514+时间轴 430+边距，防校准栏被压缩画布溢出）
            if (window.position.width < 1040f)
                window.position = new Rect(window.position.x, window.position.y, 1240f, Mathf.Max(window.position.height, 780f));
            // 打开时若当前选中就是时轮资产则直接载入
            // （GetWindow 触发的 CreateGUI 先于本行执行——赋值后必须刷新，否则窗口空载：
            //   既有缺陷随校准节修复一并收口，2026-10-05）
            if (Selection.activeObject is SkillTimelineAsset selected)
                window.LoadAsset(selected);
        }

        /// <summary>载入时轮资产并全量刷新（打开时选中资产/外部切换共用入口）</summary>
        private void LoadAsset(SkillTimelineAsset asset)
        {
            _asset = asset;
            _selected = null;
            RefreshAll();
        }

        // ==================== 开一把试招（战斗沙盒入口，2026-10-05） ====================
        // 时轮校准→实战验证闭环：编辑时轮/校准动作片后一键进战斗手操放技能，所见即所得对齐判定线。
        // 沙盒语义（BattleLaunchConfig.BuildSandbox）：A=编辑单位（层级覆盖 5★魔神档=全手操）、
        // B=温迪空座木桩（不挂配额脑=站桩；回合经 Sim.SandboxAutoPassPlayerId 即时 Pass）。

        private const string SandboxAllyKey = "TimelineSandboxAllyUnit";
        private const string SandboxDummyKey = "TimelineSandboxDummyUnit";
        private const string BootScenePath = "Assets/Scenes/Boot.unity";

        /// <summary>域重载安全的 Play 进入钩子（[InitializeOnLoadMethod] 每次重载后重跑注册一次——
        /// 勿挂窗口实例：EnterPlaymode 触发域重载，窗口 OnEnable 与 EnteredPlayMode 事件顺序无保证）</summary>
        [InitializeOnLoadMethod]
        private static void RegisterSandboxPlayModeHook()
        {
            EditorApplication.playModeStateChanged += OnSandboxPlayModeChanged;
        }

        private static void OnSandboxPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingPlayMode)
            {
                // 防御：未消费的等待钩子随退出清理；prefs 一并清（用户主动退 Play=不想开了，防幽灵启动）
                UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoadedForSandbox;
                EditorApplication.update -= WaitForLobbyReadyThenLaunch;
                _sandboxWaitingLobby = false;
                EditorPrefs.DeleteKey(SandboxAllyKey);
                EditorPrefs.DeleteKey(SandboxDummyKey);
                return;
            }
            if (change != PlayModeStateChange.EnteredPlayMode) return;
            if (!EditorPrefs.HasKey(SandboxAllyKey)) return;
            // 进 Play 初期=Boot 期框架初始化/Splash 流程在跑——过早期 Launch 会切场景失败/与开机流程
            // 的 Splash→大厅跳转竞速（2026-10-05 实测：直接 Launch 停在大厅）。改等 MainHall 场景
            // 加载完成（框架全就绪）再切战斗=与大厅「开战」按钮完全同路径时点。
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoadedForSandbox;
        }

        private static void OnSceneLoadedForSandbox(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            if (scene.name != "MainHall") return; // 等大厅（Boot/Splash 过场不算）
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoadedForSandbox;
            // 大厅加载完≠转场完：Boot→大厅的 SceneTransition 输入锁还压着（揭幕动画期）——此时
            // Launch 会被 GameScene.LoadSceneWithConfig 的锁分支拦截静默丢弃（2026-10-05 二连实测
            // 停大厅根因：Warn「场景正在切换中，请稍后再试」实锤）。转轮询等锁释放再 Launch；
            // 30s 超时兜底防锁滞留死等。
            _sandboxWaitingLobby = true;
            _sandboxWaitStart = EditorApplication.timeSinceStartup;
            EditorApplication.update += WaitForLobbyReadyThenLaunch;
        }

        private static bool _sandboxWaitingLobby;
        private static double _sandboxWaitStart;

        private static void WaitForLobbyReadyThenLaunch()
        {
            if (!_sandboxWaitingLobby) return;
            double waited = EditorApplication.timeSinceStartup - _sandboxWaitStart;
            if (!Application.isPlaying || waited > 30.0)
            {
                // 退 Play=用户放弃 / 超时=转场锁滞留异常——本轮放弃并清 prefs 防幽灵启动
                _sandboxWaitingLobby = false;
                EditorApplication.update -= WaitForLobbyReadyThenLaunch;
                EditorPrefs.DeleteKey(SandboxAllyKey);
                EditorPrefs.DeleteKey(SandboxDummyKey);
                if (waited > 30.0)
                    GICLog.Warn("[时轮编辑器] 试招沙盒等待大厅转场超时（30s）——已放弃，可重新点「开一把试招」");
                return;
            }
            if (GIC.Framework.InputLocks.HasLock(GIC.Framework.InputLockReason.SceneTransition)) return; // 转场揭幕中，继续等
            _sandboxWaitingLobby = false;
            EditorApplication.update -= WaitForLobbyReadyThenLaunch;
            LaunchSandboxFromPending();
        }

        private static void LaunchSandboxFromPending()
        {
            var ally = (UnitName)EditorPrefs.GetInt(SandboxAllyKey);
            var dummy = (UnitName)EditorPrefs.GetInt(SandboxDummyKey, (int)UnitName.Venti);
            EditorPrefs.DeleteKey(SandboxAllyKey);
            EditorPrefs.DeleteKey(SandboxDummyKey);
            if (!Application.isPlaying) return; // 防御：delayCall 前已退出 Play
            BattleLaunchConfig.Launch(BattleLaunchConfig.BuildSandbox(ally, dummy));
            GICLog.Info($"[时轮编辑器] 试招沙盒启动（大厅就绪后）：{ally} vs 温迪木桩");
        }

        /// <summary>「开一把试招」入口：反查持有单位→沙盒配置→Launch。Play 中直接开；
        /// Edit 中先写 EditorPrefs 惨透（域重载安全）再切 Boot 场景进 Play（战斗装配依赖
        /// Boot 期 DI 容器/存档初始化——勿从其它场景直开）</summary>
        private void LaunchSandboxFromEditor()
        {
            FindPreviewSkill(out var unitData);
            if (unitData == null)
            {
                EditorUtility.DisplayDialog("开一把试招",
                    "未找到持有此时轮的技能——先「接线到 UnitConfig」（或确认资产名=SkillName 枚举名）", "知道了");
                return;
            }
            var ally = unitData.unitName;
            if (Application.isPlaying)
            {
                BattleLaunchConfig.Launch(BattleLaunchConfig.BuildSandbox(ally, UnitName.Venti));
                GICLog.Info($"[时轮编辑器] 试招沙盒启动（Play 中直开）：{ally} vs 温迪木桩");
                return;
            }
            EditorPrefs.SetInt(SandboxAllyKey, (int)ally);
            EditorPrefs.SetInt(SandboxDummyKey, (int)UnitName.Venti);
            var activePath = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().path;
            if (!string.Equals(activePath, BootScenePath, System.StringComparison.OrdinalIgnoreCase))
            {
                // 未保存改动先问询（OpenScene 不保存=会丢）；用户取消=中止试招
                if (!UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    return;
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(BootScenePath);
            }
            EditorApplication.EnterPlaymode();
            GICLog.Info($"[时轮编辑器] 试招沙盒启动（进 Play）：{ally} vs 温迪木桩");
        }

        // ==================== 状态 ====================

        private SkillTimelineAsset _asset;
        private SkillTimelineClip _selected;

        /// <summary>像素/秒（缩放滑条 60~240）</summary>
        private float _pps = 120f;
        private const float Snap = 0.05f;
        private const float TrackHeight = 24f;
        private const float TrackLabelWidth = 84f;
        private const float ClipMinWidth = 14f;

        // UI 引用（增量刷新用）
        private Label _statusLabel;
        private Label _validationLabel;
        private VisualElement _headerFields;
        private VisualElement _timelineArea;   // 标尺+七轨容器（重建）
        private VisualElement _clipPanel;      // 选中 clip 属性面板（重建）
        private FloatField _totalTimeField;
        private EnumField _aimModeField;
        private Slider _zoomSlider;

        // ==================== 动作片校准预览（B-S4c 校准批次，2026-10-05） ====================
        // 时轮编辑器内嵌校准工位：反查持有本时轮的技能（timeline 引用→skillID 名兜底）→ 其 SkillData
        // 动作视频/缩放补偿/播放速度/位置偏移 所见即所得调参——预览画布=待机片基准（幽灵底图）叠加
        // 动作片当前帧（ChromaKey 抠色后按 comp/offset 同构摆放，与战斗 UnitView quad 数学一致），
        // 底部标尺画判定轨事件时刻竖线（首发 startTime+连发 hitInterval×发数），播放头可拖/逐帧——
        // 用于校准「动画节拍与代码判定对齐」（安柏二连射第二箭松弦对齐第二发）与「观感统一」（主体重合）。

        private VisualElement _calibSection;     // 校准右栏（无动作片技能时隐藏）
        private VisualElement _calibPlayRow;     // 播放控制整行（预览关/技能缺失时随隐藏）
        private Label _calibInfoLabel;            // 信息卡（富文本：技能/对齐状态/判定时刻）
        private IMGUIContainer _calibCanvasHost;  // 预览画布（含标尺，IMGUI 混排——skill 规范先例）
        private VisualElement _calibControls;     // 播放控制按钮池（重建）
        private VisualElement _calibParams;       // 参数滑条区（重建）
        private Label _calibTimeLabel;             // 当前播放时刻（OnPreviewUpdate 实时刷新）
        private Button _calibPlayBtn;             // 播放/暂停单键（步进/拖动/播完自然停时同步文本）
        private bool _calibOpen = true;           // 预览开关（关=收起右栏+拆引擎省资源）

        /// <summary>校准画布固定尺寸（易用性重排：右栏 540 内画布最大化）</summary>
        private const float CalibCanvasW = 508f;
        private const float CalibCanvasH = 290f;

        // 预览引擎（编辑器侧独立于战斗运行时；窗口销毁/收起必须释放——OnDisable teardown）
        private GameObject _previewRoot;
        private VideoPlayer _actionPlayer;         // 动作片（isLooping=false 一次性）
        private VideoPlayer _idlePlayer;          // 待机基准片（isLooping=true 循环播=真实待机观感；null=单位无待机片）
        private RenderTexture _actionRawRt, _actionKeyedRt;   // 原始帧→ChromaKey 抠色帧
        private RenderTexture _idleRawRt, _idleKeyedRt;
        private Material _previewChromaMat;       // GIC/Battle/ChromaKeyVideo（编辑器 Shader.Find 可得）
        private SkillConfig _previewSkillConfig;  // 校准参数写回目标（Undo/SetDirty 宿主资产）
        private SkillConfig.SkillData _previewSkillData;
        private UnitConfig.UnitData _previewUnitData;
        private VideoClip _previewActionClip, _previewIdleClip;
        private bool _previewPlaying;
        private bool _actionBlitDirty;             // 暂停态 seek/逐帧后置位——update 里补一次 Blit
        private bool _scrubbing;                  // 标尺拖拽中

        /// <summary>轨道固定显示序（目标声明→表现→判定→资源→位移）</summary>
        private static readonly SkillTrackType[] TrackOrder =
        {
            SkillTrackType.Targeting, SkillTrackType.Action, SkillTrackType.Judgment,
            SkillTrackType.Vfx, SkillTrackType.Sfx, SkillTrackType.Resource, SkillTrackType.Movement,
        };

        private static readonly Dictionary<SkillTrackType, Color> TrackColors = new()
        {
            { SkillTrackType.Targeting, new Color(0.45f, 0.45f, 0.5f) },
            { SkillTrackType.Action, new Color(0.25f, 0.5f, 0.85f) },
            { SkillTrackType.Judgment, new Color(0.8f, 0.3f, 0.28f) },
            { SkillTrackType.Vfx, new Color(0.6f, 0.35f, 0.75f) },
            { SkillTrackType.Sfx, new Color(0.8f, 0.65f, 0.2f) },
            { SkillTrackType.Resource, new Color(0.3f, 0.65f, 0.35f) },
            { SkillTrackType.Movement, new Color(0.25f, 0.6f, 0.6f) },
        };

        // ==================== 骨架 ====================

        private void OnEnable()
        {
            EditorApplication.update += OnPreviewUpdate; // 校准预览帧驱动（blit+重绘）
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnPreviewUpdate;
            TeardownPreviewResources(); // VideoPlayer/RT/材质随窗口关闭与域重载释放（防泄漏）
        }

        private void CreateGUI()
        {
            // GetWindow 复用已开窗口时 CreateGUI 不再触发——幂等重建
            rootVisualElement.Clear();
            BuildUI();
        }

        private void BuildUI()
        {
            var root = rootVisualElement;
            root.Clear();
            root.style.paddingLeft = 10;
            root.style.paddingRight = 10;
            root.style.paddingTop = 8;
            root.style.paddingBottom = 6;
            ConfigEditorUITK.ApplyGameFont(root);

            root.Add(BuildAssetBar());

            _headerFields = new VisualElement();
            root.Add(_headerFields);

            _statusLabel = new Label("未载入资产——顶部选择 SkillTimelineAsset 或「新建」");
            _statusLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
            _statusLabel.style.fontSize = 11;
            _statusLabel.style.marginBottom = 4;
            root.Add(_statusLabel);

            // 主工作区两栏（2026-10-05 易用性重排）：左=时间轴+选中 clip 面板（编辑动线就近），
            // 右=动作片校准列（固定宽，预览+调参一屏并排——视线不再上下跳）；校准未启用时右栏收起、
            // 左栏自动占满
            var mainRow = new VisualElement();
            mainRow.style.flexDirection = FlexDirection.Row;
            mainRow.style.flexGrow = 1f;
            mainRow.style.marginTop = 2;

            var leftColumn = new VisualElement();
            leftColumn.style.flexGrow = 1f;
            leftColumn.style.minWidth = 430;

            var scroll = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
            scroll.style.flexGrow = 1f;
            _timelineArea = new VisualElement();
            scroll.Add(_timelineArea);
            leftColumn.Add(scroll);

            // 选中 clip 属性面板（时间轴正下方——拖完 clip 就近改属性）；flexWrap 字段行换行后总高变高，
            // maxHeight 放宽 240 防内部滚动吞字段（窄栏适配 2026-10-05）
            leftColumn.Add(ConfigEditorUITK.CreateSectionHeader("选中 clip"));
            var panelScroll = new ScrollView(ScrollViewMode.Vertical);
            panelScroll.style.maxHeight = 240f;
            _clipPanel = new VisualElement();
            panelScroll.Add(_clipPanel);
            leftColumn.Add(panelScroll);

            mainRow.Add(leftColumn);

            // 右=动作片校准预览列（B-S4c 校准批次，2026-10-05）
            _calibSection = BuildCalibrationSection();
            mainRow.Add(_calibSection);

            root.Add(mainRow);

            _validationLabel = new Label();
            _validationLabel.style.color = new Color(0.85f, 0.7f, 0.25f);
            _validationLabel.style.fontSize = 11;
            _validationLabel.style.whiteSpace = WhiteSpace.Normal;
            _validationLabel.style.marginTop = 4;
            root.Add(_validationLabel);

            root.Add(ConfigEditorUITK.CreateFooterBar(
                "编辑即写入内存（Ctrl+Z 可撤销）；时间/规格归时轮、数值归 SkillParamKey（docs/active/28 §3）",
                SaveToDisk));

            RefreshAll();
        }

        // ==================== 资产栏 ====================

        private VisualElement BuildAssetBar()
        {
            var bar = new VisualElement();
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.alignItems = Align.Center;
            bar.style.marginBottom = 8;

            // 对象选择走 IMGUI（Tuanjie UITK ObjectField 点名称=Ping 不开选择器）
            var objectFieldHolder = new IMGUIContainer(() =>
            {
                var current = EditorGUILayout.ObjectField("时轮资产", _asset, typeof(SkillTimelineAsset), false,
                    GUILayout.Width(300)) as SkillTimelineAsset;
                if (current != _asset)
                    LoadAsset(current);
            });
            objectFieldHolder.style.width = 310;
            objectFieldHolder.style.marginRight = 8;
            bar.Add(objectFieldHolder);

            var newBtn = ConfigEditorUITK.CreateToolButton("新建", ShowCreateDialog);
            newBtn.style.width = 80;
            newBtn.style.marginTop = 0;
            bar.Add(newBtn);

            var wireBtn = ConfigEditorUITK.CreateToolButton("接线到 UnitConfig", WireToUnitConfig);
            wireBtn.style.width = 140;
            wireBtn.style.marginTop = 0;
            wireBtn.style.marginLeft = 6;
            bar.Add(wireBtn);

            // 开一把试招（2026-10-05 用户需求）：以正在编辑时轮的持有单位进战斗沙盒——
            // 单位层级覆盖 5★魔神档=玩家全手操全部技能；对面温迪空座木桩站桩挨打
            var tryBtn = ConfigEditorUITK.CreateToolButton("开一把试招", LaunchSandboxFromEditor);
            tryBtn.style.width = 110;
            tryBtn.style.marginTop = 0;
            tryBtn.style.marginLeft = 6;
            bar.Add(tryBtn);

            // 缩放滑条（右对齐）
            var zoomLabel = new Label("缩放");
            zoomLabel.style.marginLeft = 12;
            bar.Add(zoomLabel);
            _zoomSlider = new Slider(60f, 240f) { value = _pps, showInputField = false };
            _zoomSlider.style.width = 120;
            _zoomSlider.RegisterValueChangedCallback(evt =>
            {
                _pps = evt.newValue;
                RebuildTimeline();
            });
            bar.Add(_zoomSlider);

            return bar;
        }

        private void ShowCreateDialog()
        {
            // 轻量创建：输入 SkillName 枚举名（AI/开发者知道枚举名；搜索下拉留 B-S2 增强）
            var path = EditorUtility.SaveFilePanel("新建时轮资产",
                "Assets/Resources/Configs/SkillTimelines", "", "asset");
            if (string.IsNullOrEmpty(path)) return;
            if (!path.StartsWith(Application.dataPath))
            {
                EditorUtility.DisplayDialog("时轮编辑器", "资产必须创建在 Assets/Resources/Configs/SkillTimelines/ 下（客户端按此路径加载）", "知道了");
                return;
            }
            var assetPath = "Assets" + path.Substring(Application.dataPath.Length);
            var fileName = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            if (!Enum.TryParse<SkillName>(fileName, out _))
            {
                if (!EditorUtility.DisplayDialog("时轮编辑器",
                    $"文件名 {fileName} 不是 SkillName 枚举名（客户端按 skillID 名加载资产会落空）。\n仍要创建吗？",
                    "仍然创建", "取消"))
                    return;
            }
            var asset = CreateInstance<SkillTimelineAsset>();
            asset.totalTime = 1f;
            AssetDatabase.CreateAsset(asset, assetPath);
            AssetDatabase.SaveAssets();
            _asset = asset;
            _selected = null;
            RefreshAll();
            GICLog.Info($"[时轮编辑器] 新建资产 {assetPath}（记得「接线到 UnitConfig」）");
        }

        private void WireToUnitConfig()
        {
            if (_asset == null) return;
            var config = AssetDatabase.LoadAssetAtPath<UnitConfig>("Assets/Resources/Configs/UnitConfig.asset");
            if (config == null)
            {
                EditorUtility.DisplayDialog("时轮编辑器", "UnitConfig.asset 未找到", "知道了");
                return;
            }
            // 按文件名匹配 SkillName（资产命名约定=枚举名）
            var fileName = _asset.name;
            int wired = 0;
            foreach (var unitData in config.unitDataList)
            {
                if (unitData?.skills == null) continue;
                foreach (var skill in unitData.skills)
                {
                    if (skill?.data == null || skill.data.skillID.ToString() != fileName) continue;
                    // 被改对象=SkillConfig 资产（技能独立化后 timeline 字段在资产内）——标脏/撤销必须落在
                    // 被改资产上：SetDirty UnitConfig 不写盘，SaveAssets 只保存已标脏资产（2026-09-25 审查 S1）
                    Undo.RecordObject(skill, "时轮编辑器：接线到 UnitConfig");
                    skill.data.timeline = _asset;
                    EditorUtility.SetDirty(skill);
                    wired++;
                }
            }
            if (wired > 0)
            {
                AssetDatabase.SaveAssets();
                GICLog.Info($"[时轮编辑器] {_asset.name} 已接线 UnitConfig（{wired} 处 skillID 匹配，已写盘）");
                _statusLabel.text = StatusText();
            }
            else
            {
                EditorUtility.DisplayDialog("时轮编辑器",
                    $"UnitConfig 中没有 skillID={fileName} 的技能条目——检查资产名与 SkillName 枚举是否一致", "知道了");
            }
        }

        // ==================== 头部 ====================

        private void RefreshHeader()
        {
            _headerFields.Clear();
            if (_asset == null) return;

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 6;

            _totalTimeField = new FloatField("总时长") { value = _asset.totalTime };
            _totalTimeField.style.width = 150;
            _totalTimeField.RegisterValueChangedCallback(evt =>
            {
                RecordUndo("改总时长");
                _asset.totalTime = Mathf.Max(0f, evt.newValue);
                RebuildTimeline();
            });
            row.Add(_totalTimeField);

            _aimModeField = new EnumField("瞄准", _asset.aimMode);
            _aimModeField.style.width = 190;
            _aimModeField.style.marginLeft = 10;
            _aimModeField.RegisterValueChangedCallback(evt =>
            {
                RecordUndo("改瞄准模式");
                _asset.aimMode = (SkillAimMode)(evt.newValue ?? SkillAimMode.CrossDirection);
            });
            row.Add(_aimModeField);

            var clipCount = new Label($"clip ×{_asset.clips.Count}");
            clipCount.style.marginLeft = 14;
            clipCount.style.color = new Color(0.7f, 0.72f, 0.75f);
            row.Add(clipCount);

            _headerFields.Add(row);
        }

        // ==================== 时间轴 ====================

        private void RebuildTimeline()
        {
            _timelineArea.Clear();
            if (_asset == null) return;

            float axisWidth = Mathf.Max(600f, (_asset.totalTime + 0.6f) * _pps);
            _timelineArea.Add(BuildRuler(axisWidth));

            foreach (var track in TrackOrder)
                _timelineArea.Add(BuildTrackRow(track, axisWidth));
        }

        /// <summary>时间标尺：0.1s 小刻度 + 0.5s 大刻度（数字）</summary>
        private VisualElement BuildRuler(float axisWidth)
        {
            var ruler = new VisualElement();
            ruler.style.flexDirection = FlexDirection.Row;
            ruler.style.marginBottom = 2;

            var spacer = new VisualElement();
            spacer.style.width = TrackLabelWidth;
            ruler.Add(spacer);

            var axis = new VisualElement();
            axis.style.width = axisWidth;
            axis.style.height = 18;
            ruler.Add(axis);

            for (int i = 0; i * 0.1f <= _asset.totalTime + 0.001f; i++)
            {
                float t = i * 0.1f;
                bool major = i % 5 == 0; // 0.5s 大刻度（整数循环避浮点累加误差）
                var tick = new VisualElement();
                tick.style.position = Position.Absolute;
                tick.style.left = t * _pps;
                tick.style.width = major ? 2 : 1;
                tick.style.height = major ? 14 : 7;
                tick.style.bottom = 0;
                tick.style.backgroundColor = new Color(0.6f, 0.6f, 0.6f, major ? 0.9f : 0.45f);
                axis.Add(tick);
                if (major && t > 0f)
                {
                    var num = new Label($"{t:0.0}s");
                    num.style.position = Position.Absolute;
                    num.style.left = t * _pps + 4;
                    num.style.fontSize = 10;
                    num.style.color = new Color(0.65f, 0.68f, 0.7f);
                    axis.Add(num);
                }
            }
            return ruler;
        }

        private VisualElement BuildTrackRow(SkillTrackType track, float axisWidth)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = 3;

            // 轨道标签（含「+」加 clip）
            var labelBox = new VisualElement();
            labelBox.style.width = TrackLabelWidth;
            labelBox.style.flexDirection = FlexDirection.Row;
            labelBox.style.alignItems = Align.Center;
            var label = new Label(TrackLabelOf(track));
            label.style.fontSize = 11;
            label.style.color = TrackColors[track] * 1.3f;
            label.style.flexGrow = 1f;
            labelBox.Add(label);
            var addBtn = new Button(() => AddClip(track)) { text = "+" };
            addBtn.style.width = 20;
            addBtn.style.height = 18;
            addBtn.style.marginLeft = 2;
            addBtn.style.fontSize = 11;
            labelBox.Add(addBtn);
            row.Add(labelBox);

            // 轨道底板
            var lane = new VisualElement();
            lane.style.width = axisWidth;
            lane.style.height = TrackHeight;
            lane.style.backgroundColor = new Color(0.16f, 0.17f, 0.19f);
            ConfigEditorUITK.SetBorderRadius(lane, 4);
            row.Add(lane);

            foreach (var clip in _asset.clips.Where(c => c.trackType == track).OrderBy(c => c.startTime))
                lane.Add(BuildClipBlock(clip, track));

            return row;
        }

        /// <summary>clip 块：绝对定位、点击选中、水平拖拽平移、右缘拖拽调时长</summary>
        private VisualElement BuildClipBlock(SkillTimelineClip clip, SkillTrackType track)
        {
            var color = TrackColors[track];
            float left = clip.startTime * _pps;
            float width = Mathf.Max(ClipMinWidth, (clip.endTime - clip.startTime) * _pps);

            var block = new VisualElement();
            block.style.position = Position.Absolute;
            block.style.left = left;
            block.style.top = 2;
            block.style.width = width;
            block.style.height = TrackHeight - 4;
            block.style.backgroundColor = color;
            ConfigEditorUITK.SetBorderRadius(block, 3);
            if (clip == _selected)
                ConfigEditorUITK.SetBorder(block, 2f, new Color(1f, 0.82f, 0.25f));

            var text = new Label(ClipLabelOf(clip));
            text.style.fontSize = 10;
            text.style.color = Color.white;
            text.style.unityTextAlign = TextAnchor.MiddleCenter;
            text.style.overflow = Overflow.Hidden;
            text.pickingMode = PickingMode.Ignore; // 命中归块本体
            block.Add(text);

            // 右缘调长手柄（8px 命中区）
            var resize = new VisualElement();
            resize.style.position = Position.Absolute;
            resize.style.right = 0;
            resize.style.top = 0;
            resize.style.width = 8;
            resize.style.height = TrackHeight - 4;
            resize.style.backgroundColor = new Color(1f, 1f, 1f, 0.18f);
            resize.style.borderTopRightRadius = 3;
            resize.style.borderBottomRightRadius = 3;
            block.Add(resize);

            // 拖拽状态（闭包共享）
            var drag = new DragState();

            block.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                drag.active = true;
                drag.startPointerX = evt.position.x;
                drag.mode = IsNearRightEdge(block, evt.position) ? DragMode.Resize : DragMode.Move;
                drag.startValue = drag.mode == DragMode.Resize ? clip.endTime : clip.startTime;
                drag.startWidth = clip.endTime - clip.startTime;
                drag.undoRecorded = false;
                block.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });

            block.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!drag.active) return;
                if (!drag.undoRecorded)
                {
                    RecordUndo(drag.mode == DragMode.Move ? "拖拽 clip" : "调 clip 时长");
                    drag.undoRecorded = true;
                }
                float delta = (evt.position.x - drag.startPointerX) / _pps;
                if (drag.mode == DragMode.Move)
                {
                    // 整体平移=宽度保持；起点钳 [0, 总时长-宽度]（2026-10-05 拍板「优化」：
                    // 防拖拽把 clip 拉到 24.05 类远超总时长的无意义值——拖出窗口坐标仍持续产生大 delta）
                    float width = drag.startWidth;
                    float ceiling = ClipTimeCeiling;
                    float target = SnapTo(drag.startValue + delta);
                    target = Mathf.Clamp(target, 0f, Mathf.Max(0f, ceiling - width));
                    clip.startTime = target;
                    clip.endTime = target + width;
                }
                else
                {
                    clip.endTime = Mathf.Max(clip.startTime + Snap, Mathf.Min(SnapTo(drag.startValue + delta), ClipTimeCeiling));
                }
                ApplyClipGeometry(block, clip);
                MarkDirtyLight();
            });

            block.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!drag.active) return;
                drag.active = false;
                block.ReleasePointer(evt.pointerId);
                // 点击（未产生位移）= 选中；拖拽=重绘同步选中态与面板字段
                if (Mathf.Abs(evt.position.x - drag.startPointerX) < 3f)
                    _selected = clip;
                RebuildTimeline();
                RefreshClipPanel();
                RefreshValidation();
            });

            return block;
        }

        private enum DragMode { Move, Resize }
        private class DragState
        {
            public bool active;
            public float startPointerX;
            public float startValue;
            public float startWidth; // PointerDown 时的 clip 宽度（Move 平移=宽度保持）
            public DragMode mode;
            public bool undoRecorded;
        }

        /// <summary>clip 时刻上限=总时长（总时长≤0=移动类动态时长约定=不设限）</summary>
        private float ClipTimeCeiling => _asset != null && _asset.totalTime > 0f ? _asset.totalTime : float.MaxValue;

        private static bool IsNearRightEdge(VisualElement block, Vector2 pointer)
        {
            var local = block.WorldToLocal(pointer);
            return local.x >= block.resolvedStyle.width - 10f;
        }

        private void ApplyClipGeometry(VisualElement block, SkillTimelineClip clip)
        {
            block.style.left = clip.startTime * _pps;
            block.style.width = Mathf.Max(ClipMinWidth, (clip.endTime - clip.startTime) * _pps);
        }

        private static float SnapTo(float value) => Mathf.Round(value / Snap) * Snap;

        // ==================== clip 增删与面板 ====================

        private void AddClip(SkillTrackType track)
        {
            if (_asset == null) return;
            RecordUndo("加 clip");
            var clip = new SkillTimelineClip
            {
                trackType = track,
                startTime = 0f,
                endTime = Mathf.Clamp(Mathf.Min(0.3f, Mathf.Max(0.1f, _asset.totalTime)), 0.1f, ClipTimeCeiling),
                kind = track == SkillTrackType.Judgment ? (int)SkillJudgmentKind.LineProjectile : 0,
                cueName = "",
            };
            _asset.clips.Add(clip);
            _selected = clip;
            MarkDirtyLight();
            RebuildTimeline();
            RefreshClipPanel();
            RefreshValidation();
        }

        private void DeleteSelected()
        {
            if (_asset == null || _selected == null) return;
            RecordUndo("删 clip");
            _asset.clips.Remove(_selected);
            _selected = null;
            MarkDirtyLight();
            RebuildTimeline();
            RefreshClipPanel();
            RefreshValidation();
        }

        /// <summary>重建选中 clip 属性面板（按轨道显示有效载荷）</summary>
        private void RefreshClipPanel()
        {
            _clipPanel.Clear();
            if (_asset == null || _selected == null)
            {
                var hint = new Label("未选中 clip——点击时间轴上的 clip 块");
                hint.style.color = new Color(0.55f, 0.55f, 0.55f);
                hint.style.fontSize = 11;
                _clipPanel.Add(hint);
                return;
            }
            BuildClipPanelInto(_clipPanel, _selected);
        }

        private void BuildClipPanelInto(VisualElement panel, SkillTimelineClip clip)
        {
            var track = clip.trackType;

            // 第一行：轨道 + kind + 删除（flexWrap：左栏窄容器下行宽超限自动换行——2026-10-05 报障「拥挤看不到数据」）
            var row1 = new VisualElement();
            row1.style.flexDirection = FlexDirection.Row;
            row1.style.flexWrap = UnityEngine.UIElements.Wrap.Wrap;
            row1.style.alignItems = Align.Center;
            var trackLabel = new Label($"轨道：{TrackLabelOf(track)}");
            trackLabel.style.width = 130;
            trackLabel.style.color = TrackColors[track] * 1.3f;
            row1.Add(trackLabel);

            if (track == SkillTrackType.Judgment)
            {
                var kindField = new EnumField("类型", (SkillJudgmentKind)clip.kind);
                kindField.style.width = 220;
                kindField.RegisterValueChangedCallback(evt =>
                {
                    RecordUndo("改判定类型");
                    clip.kind = Convert.ToInt32(evt.newValue ?? SkillJudgmentKind.LineProjectile);
                    MarkDirtyLight();
                    RefreshStatusAndClipLabels();
                });
                row1.Add(kindField);
            }
            row1.Add(ConfigEditorUITK.CreateToolButton("删除 clip", DeleteSelected, danger: true));
            panel.Add(row1);

            // 第二行：时刻字段（输入钳 [0, 总时长]——与拖拽同钳制口径，2026-10-05「优化」拍板）
            var row2 = new VisualElement();
            row2.style.flexDirection = FlexDirection.Row;
            row2.style.flexWrap = UnityEngine.UIElements.Wrap.Wrap;
            row2.style.marginTop = 4;
            row2.Add(FloatFieldOf("起点", clip.startTime, v =>
            {
                clip.startTime = Mathf.Clamp(v, 0f, Mathf.Min(ClipTimeCeiling, Mathf.Max(0f, clip.endTime - Snap)));
                AfterTimeEdit(clip);
            }));
            row2.Add(FloatFieldOf("终点", clip.endTime, v =>
            {
                clip.endTime = Mathf.Clamp(v, clip.startTime + Snap, ClipTimeCeiling);
                AfterTimeEdit(clip);
            }));
            panel.Add(row2);

            var row2b = new VisualElement();
            row2b.style.flexDirection = FlexDirection.Row;
            row2b.style.flexWrap = UnityEngine.UIElements.Wrap.Wrap;
            row2b.style.marginTop = 2;
            row2b.Add(FloatFieldOf("连发间隔", clip.hitInterval, v =>
            {
                clip.hitInterval = Mathf.Max(0f, v);
                MarkDirtyLight();
            }));
            panel.Add(row2b);

            // 判定轨载荷（投射物规格）——同样一行 2 个
            if (track == SkillTrackType.Judgment)
            {
                var row3 = new VisualElement();
                row3.style.flexDirection = FlexDirection.Row;
                row3.style.flexWrap = UnityEngine.UIElements.Wrap.Wrap;
                row3.style.marginTop = 4;
                row3.Add(FloatFieldOf("弹速", clip.projectileSpeed, v => { clip.projectileSpeed = Mathf.Max(0f, v); MarkDirtyLight(); }));
                row3.Add(FloatFieldOf("判定直径", clip.hitDiameter, v => { clip.hitDiameter = Mathf.Max(0f, v); MarkDirtyLight(); }));
                panel.Add(row3);

                var row3b = new VisualElement();
                row3b.style.flexDirection = FlexDirection.Row;
                row3b.style.flexWrap = UnityEngine.UIElements.Wrap.Wrap;
                row3b.style.marginTop = 2;
                var rangeField = new IntegerField("射程") { value = clip.maxRange };
                rangeField.style.width = 170;
                rangeField.style.marginLeft = 8;
                rangeField.style.flexShrink = 0; // 防压缩（输入框被压成「(」的教训——shrink 只换行不压缩）
                rangeField.RegisterValueChangedCallback(evt =>
                {
                    RecordUndo("改射程");
                    clip.maxRange = evt.newValue;
                    MarkDirtyLight();
                });
                row3b.Add(rangeField);
                var hint = new Label("0=用 BattleMetrics 默认（速度8格/s·直径0.42·射程24）");
                hint.style.fontSize = 10;
                hint.style.color = new Color(0.55f, 0.58f, 0.6f);
                hint.style.alignSelf = Align.Center;
                hint.style.marginLeft = 8;
                hint.style.whiteSpace = WhiteSpace.Normal; // 窄栏下折行不溢出
                row3b.Add(hint);
                panel.Add(row3b);
            }

            // 表现轨 cueName
            if (track == SkillTrackType.Action || track == SkillTrackType.Vfx || track == SkillTrackType.Sfx)
            {
                var row4 = new VisualElement();
                row4.style.flexDirection = FlexDirection.Row;
                row4.style.flexWrap = UnityEngine.UIElements.Wrap.Wrap;
                row4.style.marginTop = 4;
                var cueField = new TextField("cue 名") { value = clip.cueName ?? "" };
                cueField.style.width = 300;
                cueField.RegisterValueChangedCallback(evt =>
                {
                    RecordUndo("改 cue 名");
                    clip.cueName = evt.newValue;
                    MarkDirtyLight();
                    RefreshStatusAndClipLabels();
                });
                row4.Add(cueField);
                var cueHint = new Label("音效名=Resources/Audios/Battle/{cue}（B-S3 素材接线）");
                cueHint.style.fontSize = 10;
                cueHint.style.color = new Color(0.55f, 0.58f, 0.6f);
                cueHint.style.alignSelf = Align.Center;
                cueHint.style.marginLeft = 8;
                cueHint.style.whiteSpace = WhiteSpace.Normal; // 窄栏下折行不溢出
                row4.Add(cueHint);
                panel.Add(row4);
            }
        }

        private FloatField FloatFieldOf(string label, float value, Action<float> apply)
        {
            var field = new FloatField(label) { value = value };
            // 170=标签+输入框都舒展；flexShrink=0 防压缩——此前 130 一行 3 个时输入框被 shrink 压成「(」
            // （2026-10-05 用户截图实锤）；窄容器放不下时走行 flexWrap 换行而非压缩
            field.style.width = 170;
            field.style.flexShrink = 0;
            field.style.marginLeft = 8;
            field.style.marginRight = 0;
            field.RegisterValueChangedCallback(evt =>
            {
                RecordUndo($"改{label}");
                apply(evt.newValue);
            });
            return field;
        }

        private void AfterTimeEdit(SkillTimelineClip clip)
        {
            MarkDirtyLight();
            RebuildTimeline();
            RefreshValidation();
        }

        private void RefreshStatusAndClipLabels()
        {
            RebuildTimeline(); // clip 标签文本（cueName/kind 名）变化需重绘块
        }

        // ==================== 校验与保存 ====================

        private void RefreshValidation()
        {
            if (_asset == null)
            {
                _validationLabel.text = "";
                return;
            }
            var issues = new List<string>();
            foreach (var clip in _asset.clips)
            {
                if (clip.endTime > _asset.totalTime + 0.001f)
                    issues.Add($"clip「{ClipLabelOf(clip)}」终点 {clip.endTime:0.00}s 超出总时长");
                if (clip.endTime < clip.startTime)
                    issues.Add($"clip「{ClipLabelOf(clip)}」终点早于起点");
            }
            if (_asset.clips.Count > 0 && !_asset.clips.Any(c => c.trackType == SkillTrackType.Judgment))
                issues.Add("无判定轨 clip——该技能将无任何判定产出（纯演出技能才允许）");
            // 同 kind 判定 clip 多于一处（2026-09-25 审查 S2）：技能消费模型=单 clip 承载整轮连发
            //（hitInterval×DamageCount），多 clip 会被技能侧逐 clip × 发数全量放大（语义未定义）
            foreach (var group in _asset.clips.Where(c => c.trackType == SkillTrackType.Judgment)
                .GroupBy(c => c.kind).Where(g => g.Count() > 1))
                issues.Add($"判定轨「{(SkillJudgmentKind)group.Key}」有 {group.Count()} 个 clip——技能侧会逐 clip × 发数放大发射量（消费模型=单 clip 承载连发），多 clip 语义未定义");
            if (_asset.totalTime <= 0f)
                issues.Add("总时长为 0（移动类动态时长约定除外，docs/active/28 §10）");
            _validationLabel.text = issues.Count > 0 ? "⚠ " + string.Join("；", issues) : "✓ 校验通过";
        }

        private void RecordUndo(string op)
        {
            if (_asset != null) Undo.RecordObject(_asset, $"时轮编辑器：{op}");
        }

        private void MarkDirtyLight()
        {
            if (_asset != null) EditorUtility.SetDirty(_asset);
        }

        private void SaveToDisk()
        {
            if (_asset == null) return;
            EditorUtility.SetDirty(_asset);
            AssetDatabase.SaveAssets();
            GICLog.Info($"[时轮编辑器] 已保存 {_asset.name}");
            _statusLabel.text = StatusText();
        }

        // ==================== 文本助手 ====================

        private string StatusText()
        {
            if (_asset == null) return "未载入资产";
            var path = AssetDatabase.GetAssetPath(_asset);
            bool wired = IsWiredToUnitConfig();
            return $"{_asset.name} · {path}{(wired ? " · 已接线 UnitConfig ✓" : " · 未接线 UnitConfig")}";
        }

        private bool IsWiredToUnitConfig()
        {
            var config = AssetDatabase.LoadAssetAtPath<UnitConfig>("Assets/Resources/Configs/UnitConfig.asset");
            if (config == null) return false;
            foreach (var unitData in config.unitDataList)
            {
                if (unitData?.skills == null) continue;
                foreach (var skill in unitData.skills)
                    if (skill?.data != null && skill.data.timeline == _asset) return true;
            }
            return false;
        }

        private static string TrackLabelOf(SkillTrackType track) => track switch
        {
            SkillTrackType.Targeting => "目标声明",
            SkillTrackType.Action => "动作",
            SkillTrackType.Judgment => "判定",
            SkillTrackType.Vfx => "特效",
            SkillTrackType.Sfx => "音效",
            SkillTrackType.Resource => "资源",
            SkillTrackType.Movement => "位移",
            _ => track.ToString(),
        };

        private static string ClipLabelOf(SkillTimelineClip clip) => clip.trackType switch
        {
            SkillTrackType.Judgment => ((SkillJudgmentKind)clip.kind).ToString(),
            SkillTrackType.Action or SkillTrackType.Vfx or SkillTrackType.Sfx =>
                string.IsNullOrEmpty(clip.cueName) ? "（未命名）" : clip.cueName,
            _ => TrackLabelOf(clip.trackType),
        };

        // ==================== 动作片校准预览（B-S4c 校准批次，2026-10-05；易用性重排同日二批） ====================

        /// <summary>校准右栏骨架（竖排）：标题行（预览开关）→ 信息卡（富文本状态）→ 大画布 → 播放控制行
        /// （含时刻显示）→ 参数滑条区（标签+Slider+数值框三段式）→ 按钮行——预览与调参一屏内完成，
        /// 画布加宽至 508、滑条拖动连续反馈（所见即所得校准主工作流）</summary>
        private VisualElement BuildCalibrationSection()
        {
            var section = new VisualElement();
            section.style.width = 540;
            section.style.flexShrink = 0; // 窗口窄时不压缩校准栏（时间轴侧本有横向滚动让位；防画布溢出盖时间轴）
            section.style.marginLeft = 10;
            section.style.marginTop = 2;

            var headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.alignItems = Align.Center;
            headerRow.style.marginBottom = 4;
            var title = new Label("动作片校准");
            title.style.fontSize = 13;
            title.style.color = new Color(0.85f, 0.87f, 0.9f);
            title.style.flexGrow = 1f;
            headerRow.Add(title);

            var openToggle = new Toggle("启用预览") { value = _calibOpen };
            openToggle.RegisterValueChangedCallback(evt =>
            {
                _calibOpen = evt.newValue;
                RefreshCalibration(); // 开=反查+重建引擎；关=拆资源省解码（勿直调 Ensure——Teardown 已清 clip 引用）
            });
            headerRow.Add(openToggle);
            section.Add(headerRow);

            _calibInfoLabel = new Label();
            _calibInfoLabel.enableRichText = true;
            _calibInfoLabel.style.fontSize = 11;
            _calibInfoLabel.style.whiteSpace = WhiteSpace.Normal;
            _calibInfoLabel.style.marginTop = 2;
            _calibInfoLabel.style.marginBottom = 4;
            section.Add(_calibInfoLabel);

            _calibCanvasHost = new IMGUIContainer(DrawCalibCanvas);
            _calibCanvasHost.style.width = CalibCanvasW + 6;
            _calibCanvasHost.style.height = CalibCanvasH + 6;
            section.Add(_calibCanvasHost);

            // 播放控制行：按钮统一尺寸横向排 + 当前时刻实时显示（OnPreviewUpdate 刷新）
            _calibPlayRow = new VisualElement();
            _calibPlayRow.style.flexDirection = FlexDirection.Row;
            _calibPlayRow.style.alignItems = Align.Center;
            _calibPlayRow.style.marginTop = 4;
            _calibControls = new VisualElement(); // 控件池由 RebuildCalibControls 填充（横排按钮）
            _calibControls.style.flexDirection = FlexDirection.Row;
            _calibPlayRow.Add(_calibControls);
            _calibTimeLabel = new Label("t=0.00s");
            _calibTimeLabel.style.fontSize = 11;
            _calibTimeLabel.style.color = new Color(0.7f, 0.72f, 0.75f);
            _calibTimeLabel.style.marginLeft = 10;
            _calibPlayRow.Add(_calibTimeLabel);
            section.Add(_calibPlayRow);

            // 参数滑条区（重建挂点——含全部校准参数行与按钮行）
            _calibParams = new VisualElement();
            _calibParams.style.marginTop = 4;
            section.Add(_calibParams);

            return section;
        }

        /// <summary>校准节刷新入口：反查技能→预览资源重建→控件/信息重建（RefreshAll 与资产切换时调）</summary>
        private void RefreshCalibration()
        {
            if (_calibSection == null) return; // BuildUI 未建（无资产窗口态）

            var skillConfig = FindPreviewSkill(out var unitData);
            _previewSkillConfig = skillConfig;
            _previewSkillData = skillConfig != null ? skillConfig.data : null;
            _previewUnitData = unitData;
            _previewActionClip = _previewSkillData?.动作视频;
            _previewIdleClip = _previewUnitData?.立牌动画视频;

            bool hasData = _previewSkillData != null;
            bool full = hasData && _previewActionClip != null;
            bool showPanel = full && _calibOpen;
            _calibSection.style.display = _asset != null ? DisplayStyle.Flex : DisplayStyle.None;
            // 预览关=收窄条 230（标题+「启用预览」开关必须可见可重开）。勿用 width=0：display=Flex 下
            // 标题/开关会溢出挂到窗口右缘成竖条（2026-10-05 用户报障「全部塌缩到右边」取证实锤）
            _calibSection.style.width = _calibOpen ? 540 : 230;
            _calibCanvasHost.style.display = showPanel ? DisplayStyle.Flex : DisplayStyle.None;
            _calibPlayRow.style.display = showPanel ? DisplayStyle.Flex : DisplayStyle.None;
            _calibControls.style.display = showPanel ? DisplayStyle.Flex : DisplayStyle.None;
            // 信息卡与参数区：开关开即显示（技能未配动作视频时 params 显示提示行、info 显示状态——
            // 画布/播放行才是需要 full 的）；开关关=全隐只剩标题条
            _calibInfoLabel.style.display = _calibOpen ? DisplayStyle.Flex : DisplayStyle.None;
            _calibParams.style.display = (_calibOpen && hasData) ? DisplayStyle.Flex : DisplayStyle.None;

            if (!full || !_calibOpen)
            {
                TeardownPreviewResources();
            }
            else
            {
                EnsurePreviewResources();
                ApplyCalibSpeedToPlayer();
            }
            RebuildCalibControls();
            RefreshCalibInfo();
        }

        /// <summary>右列参数区重建（技能/参数外部变化时同步控件值）</summary>
        private void RebuildCalibControls()
        {
            if (_calibControls == null || _calibParams == null) return;
            _calibControls.Clear();
            _calibParams.Clear();
            var data = _previewSkillData;
            if (data == null)
            {
                var missHint = new Label("——");
                missHint.style.fontSize = 11;
                missHint.style.color = new Color(0.55f, 0.55f, 0.55f);
                _calibParams.Add(missHint);
                return;
            }
            if (_previewActionClip == null)
            {
                var hint = new Label("技能未配 动作视频——B-S4c 素材落地后在 SkillConfig 资产接线（null=待机照播）");
                hint.style.fontSize = 11;
                hint.style.color = new Color(0.55f, 0.55f, 0.55f);
                hint.style.whiteSpace = WhiteSpace.Normal;
                _calibParams.Add(hint);
                return;
            }

            // 播放控制（播放/暂停单键切换）
            Button playBtn = null;
            playBtn = ConfigEditorUITK.CreateToolButton("播放", () =>
            {
                if (_actionPlayer == null) return;
                if (_previewPlaying) { _actionPlayer.Pause(); _previewPlaying = false; }
                else
                {
                    // 停在末帧再点播放=从头播（2026-10-05 报障「第二次播放无反应」：播完态 Play 从当前位
                    // 续播——1.5s 片只续 0.04s 观感无反应；末帧判据与自然停分流同口径）
                    if (_actionPlayer.clip != null && _actionPlayer.frame >= (long)_actionPlayer.frameCount - 1)
                        _actionPlayer.frame = 0;
                    _actionPlayer.Play();
                    _previewPlaying = true;
                }
                SyncCalibPlayBtn();
            });
            _calibPlayBtn = playBtn;
            playBtn.style.width = 72;
            playBtn.style.marginLeft = 0;
            _calibControls.Add(playBtn);
            AddSmallButton(_calibControls, "重播", () =>
            {
                if (_actionPlayer == null) return;
                _actionPlayer.frame = 0; // paused 态赋值=渲染首帧
                _actionPlayer.Play();
                _previewPlaying = true;
                _actionBlitDirty = true;
                SyncCalibPlayBtn();
            });
            AddSmallButton(_calibControls, "◀1帧", () => StepFrame(-1));
            AddSmallButton(_calibControls, "1帧▶", () => StepFrame(1));

            // 参数滑条区：标签+Slider+数值框三段式（拖动连续生效——所见即所得校准主工作流；
            // Undo 语义：数值框输入走 RecordUndo（Ctrl+Z），Slider 拖动不记（连续 tick 会灌爆撤销栈，
            // 拖丢可重拖回）——CalibSliderRow 内分流）
            _calibParams.Add(CalibSliderRow("播放速度", data.动作片播放速度, 0.1f, 4f, v =>
            {
                data.动作片播放速度 = Mathf.Max(0.01f, v);
                ApplyCalibSpeedToPlayer();
            }));
            _calibParams.Add(CalibSliderRow("缩放补偿", data.动作片缩放补偿, 0.5f, 3f, v =>
            {
                data.动作片缩放补偿 = Mathf.Max(0.01f, v);
            }));
            _calibParams.Add(CalibSliderRow("偏移X", data.动作片位置偏移.x, -1f, 1f, v =>
            {
                data.动作片位置偏移 = new Vector2(v, data.动作片位置偏移.y);
            }));
            _calibParams.Add(CalibSliderRow("偏移Y", data.动作片位置偏移.y, -1f, 1f, v =>
            {
                data.动作片位置偏移 = new Vector2(data.动作片位置偏移.x, v);
            }));

            // 按钮行：自动对齐 + 保存（宽度统一）
            var rowActions = new VisualElement();
            rowActions.style.flexDirection = FlexDirection.Row;
            rowActions.style.marginTop = 6;
            var alignBtn = ConfigEditorUITK.CreateToolButton("自动对齐总时长", () =>
            {
                if (_asset == null || _asset.totalTime <= 0f || _previewActionClip == null || _previewActionClip.length <= 0.0) return;
                RecordUndoCalib("自动对齐播放速度");
                _previewSkillData.动作片播放速度 = (float)(_previewActionClip.length / (double)_asset.totalTime);
                EditorUtility.SetDirty(_previewSkillConfig);
                ApplyCalibSpeedToPlayer();
                RebuildCalibControls();
                RefreshCalibInfo();
            });
            alignBtn.style.width = 160;
            alignBtn.style.marginLeft = 0;
            rowActions.Add(alignBtn);
            var alignHint = new Label("片长÷总时长");
            alignHint.style.fontSize = 10;
            alignHint.style.color = new Color(0.55f, 0.58f, 0.6f);
            alignHint.style.alignSelf = Align.Center;
            alignHint.style.marginLeft = 6;
            rowActions.Add(alignHint);
            _calibParams.Add(rowActions);

            var saveBtn = ConfigEditorUITK.CreateToolButton("保存校准参数到磁盘", SaveCalibToDisk);
            saveBtn.style.width = 160;
            saveBtn.style.marginLeft = 0;
            saveBtn.style.marginTop = 4;
            _calibParams.Add(saveBtn);
        }

        /// <summary>参数行三段式：标签 + Slider（拖动连续生效，不记 Undo）+ 数值框（输入记 Undo）——
        /// 两条路写回同一数据并互相同步显示，公共尾部=SetDirty+信息刷新+画布重绘</summary>
        private VisualElement CalibSliderRow(string label, float value, float min, float max, Action<float> apply)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 3;

            var lab = new Label(label);
            lab.style.width = 64;
            row.Add(lab);

            var slider = new Slider(min, max) { value = Mathf.Clamp(value, min, max), showInputField = false };
            slider.style.flexGrow = 1f;
            slider.style.marginRight = 6;
            var field = new FloatField { value = value };
            field.style.width = 62;

            void AfterChanged()
            {
                EditorUtility.SetDirty(_previewSkillConfig);
                RefreshCalibInfo();
                Repaint();
            }
            slider.RegisterValueChangedCallback(evt =>
            {
                apply(evt.newValue);
                field.SetValueWithoutNotify(evt.newValue);
                AfterChanged();
            });
            field.RegisterValueChangedCallback(evt =>
            {
                RecordUndoCalib($"改{label}");
                var v = Mathf.Clamp(evt.newValue, min, max);
                apply(v);
                slider.SetValueWithoutNotify(v);
                field.SetValueWithoutNotify(v); // 越界输入回钳显示
                AfterChanged();
            });
            row.Add(slider);
            row.Add(field);
            return row;
        }

        private void AddSmallButton(VisualElement row, string text, Action action, float width = 64f)
        {
            var btn = ConfigEditorUITK.CreateToolButton(text, action);
            btn.style.width = width;
            btn.style.marginLeft = 4;
            btn.style.marginTop = 0;
            row.Add(btn);
        }

        /// <summary>播放/暂停单键文本同步（步进/拖动/播完自然停等外部改播放态时调）</summary>
        private void SyncCalibPlayBtn()
        {
            if (_calibPlayBtn != null)
                _calibPlayBtn.text = _previewPlaying ? "暂停" : "播放";
        }

        private void RecordUndoCalib(string op)
        {
            if (_previewSkillConfig != null) Undo.RecordObject(_previewSkillConfig, $"时轮编辑器校准：{op}");
        }

        private void SaveCalibToDisk()
        {
            if (_previewSkillConfig == null) return;
            EditorUtility.SetDirty(_previewSkillConfig);
            AssetDatabase.SaveAssets();
            GICLog.Info($"[时轮编辑器] 已保存 {_previewSkillConfig.name} 校准参数（播放速度/缩放补偿/位置偏移）");
        }

        /// <summary>逐帧步进（暂停态 seek 渲染）：±1 帧——精细核对松弦瞬间是否踩判定线</summary>
        private void StepFrame(int delta)
        {
            if (_actionPlayer == null || _actionPlayer.clip == null) return;
            _previewPlaying = false;
            if (_actionPlayer.isPlaying) _actionPlayer.Pause();
            long frameCount = (long)_actionPlayer.frameCount;
            long target = Mathf.Clamp((int)_actionPlayer.frame + delta, 0, (int)Mathf.Max(0, frameCount - 1));
            _actionPlayer.frame = target; // paused 态赋值=seek 并渲染该帧
            _actionBlitDirty = true;
            SyncCalibPlayBtn();
            Repaint(); // EditorWindow 级重绘（Tuanjie IMGUIContainer 无 Repaint——窗口重绘连带画布刷新）
        }

        private void RefreshCalibInfo()
        {
            if (_calibInfoLabel == null) return;
            var data = _previewSkillData;
            if (_asset == null || data == null)
            {
                _calibInfoLabel.text = "<color=#d6a25f>未找到持有此时轮的技能——先「接线到 UnitConfig」</color>";
                return;
            }
            float speed = PreviewSpeed();
            float clipLen = _previewActionClip != null ? (float)_previewActionClip.length : 0f;
            float span = clipLen > 0f ? clipLen / speed : 0f; // 校准速度下的播放长（战斗 1x 回放视角）
            float total = Mathf.Max(0f, _asset.totalTime);
            float diff = span - total;

            // 富文本状态色：绿=对齐、黄=轻微偏差、红=明显偏差——一眼可读（易用性重排）
            var sb = new System.Text.StringBuilder();
            sb.Append($"<b>{data.skillID}</b>（{_previewUnitData?.unitName.ToString() ?? "？"}）");
            sb.Append($" · 待机基准：{(_previewIdleClip != null ? "<color=#7fd67f>✓</color>" : "<color=#d6a25f>无</color>")}");
            sb.Append($"\n片长 {clipLen:0.00}s × 速度 {speed:0.00} = <color=#b8c4d4>{span:0.00}s</color> vs 总时长 {total:0.00}s ");
            if (clipLen > 0f && total > 0f)
            {
                if (diff > 0.02f)
                    sb.Append($"<color=#d67f7f>超出 {diff:0.00}s</color>");
                else if (diff < -0.02f)
                    sb.Append($"<color=#d6c27f>短 {Mathf.Abs(diff):0.00}s</color>");
                else
                    sb.Append("<color=#7fd67f>✓铺满</color>");
            }
            var shots = Mathf.Max(1, data.GetInt(SkillParamKey.DamageCount, 1));
            var judgments = SkillTimelineQuery.ClipsOf(_asset, SkillTrackType.Judgment);
            var times = new List<string>();
            foreach (var clip in judgments)
            {
                int n = clip.hitInterval > 0f ? shots : 1;
                for (int i = 0; i < n; i++)
                {
                    float t = clip.startTime + clip.hitInterval * i;
                    if (t > total) break;
                    times.Add($"{t:0.00}s");
                }
            }
            if (times.Count > 0)
                sb.Append($"\n<color=#d6836a>判定时刻：{string.Join("、", times)}</color>——播放头踩线核对动作节拍");
            else
                sb.Append("\n无判定轨 clip（纯演出）——只校准观感（缩放/位置）");
            _calibInfoLabel.text = sb.ToString();
        }

        // ==================== 校准引擎（编辑器侧资源） ====================

        /// <summary>反查持有本时轮的技能：强匹配 timeline 引用，兜底 skillID 枚举名==资产名；取首个持有者</summary>
        private SkillConfig FindPreviewSkill(out UnitConfig.UnitData unitData)
        {
            unitData = null;
            if (_asset == null) return null;
            var unitConfig = AssetDatabase.LoadAssetAtPath<UnitConfig>("Assets/Resources/Configs/UnitConfig.asset");
            if (unitConfig == null) return null;
            SkillConfig byTimeline = null, byName = null;
            UnitConfig.UnitData unitByTimeline = null, unitByName = null;
            foreach (var data in unitConfig.unitDataList)
            {
                if (data?.skills == null) continue;
                foreach (var skill in data.skills)
                {
                    if (skill?.data == null) continue;
                    if (skill.data.timeline == _asset)
                    {
                        if (byTimeline == null) { byTimeline = skill; unitByTimeline = data; }
                    }
                    if (skill.data.skillID.ToString() == _asset.name)
                    {
                        if (byName == null) { byName = skill; unitByName = data; }
                    }
                }
            }
            if (byTimeline != null) { unitData = unitByTimeline; return byTimeline; }
            if (byName != null) { unitData = unitByName; return byName; }
            return null;
        }

        /// <summary>校准速度单源：0/负=按 1（与 BattlePlayer 消费口径一致）</summary>
        private float PreviewSpeed()
        {
            var d = _previewSkillData;
            return d != null && d.动作片播放速度 > 0f ? d.动作片播放速度 : 1f;
        }

        private void ApplyCalibSpeedToPlayer()
        {
            if (_actionPlayer != null) _actionPlayer.playbackSpeed = PreviewSpeed();
        }

        /// <summary>建/换预览资源（幂等：clip 无变化直通；换片全拆重建）——仅 _calibOpen 时持有资源</summary>
        private void EnsurePreviewResources()
        {
            var actionClip = _previewActionClip;
            var idleClip = _previewIdleClip;
            if (actionClip == null) { TeardownPreviewResources(); return; }
            if (_actionPlayer != null && _previewRoot != null && _previewActionClip == actionClip
                && _previewIdleClip == idleClip && (idleClip == null || _idlePlayer != null))
                return;

            TeardownPreviewResources();

            _previewRoot = new GameObject("TimelineCalibPreview") { hideFlags = HideFlags.HideAndDontSave };
            var chromaShader = Shader.Find("GIC/Battle/ChromaKeyVideo");
            if (chromaShader != null) _previewChromaMat = new Material(chromaShader);

            // 动作片：一次性（isLooping=false），起始帧先渲一帧作静态预览
            _actionRawRt = MakeRt(actionClip.width, actionClip.height);
            _actionKeyedRt = MakeRt(actionClip.width, actionClip.height);
            _actionPlayer = _previewRoot.AddComponent<VideoPlayer>();
            _actionPlayer.playOnAwake = false;
            _actionPlayer.clip = actionClip;
            _actionPlayer.renderMode = VideoRenderMode.RenderTexture;
            _actionPlayer.targetTexture = _actionRawRt;
            _actionPlayer.isLooping = false;
            _actionPlayer.audioOutputMode = VideoAudioOutputMode.None;
            _actionPlayer.playbackSpeed = PreviewSpeed();
            _actionPlayer.Play(); // 开播首帧（paused 态从未 Play 的 VideoPlayer 可能不渲染 RT——起播即渲染）
            _previewPlaying = true;
            _actionBlitDirty = true;
            _previewActionClip = actionClip;

            // 待机基准片：循环播（真实待机观感——翼扇动），随机相位
            if (idleClip != null)
            {
                _idleRawRt = MakeRt(idleClip.width, idleClip.height);
                _idleKeyedRt = MakeRt(idleClip.width, idleClip.height);
                _idlePlayer = _previewRoot.AddComponent<VideoPlayer>();
                _idlePlayer.playOnAwake = false;
                _idlePlayer.clip = idleClip;
                _idlePlayer.renderMode = VideoRenderMode.RenderTexture;
                _idlePlayer.targetTexture = _idleRawRt;
                _idlePlayer.isLooping = true;
                _idlePlayer.audioOutputMode = VideoAudioOutputMode.None;
                if (idleClip.length > 0.0)
                    _idlePlayer.time = UnityEngine.Random.Range(0f, (float)idleClip.length);
                _idlePlayer.Play();
                _previewIdleClip = idleClip;
            }
        }

        private static RenderTexture MakeRt(uint w, uint h)
        {
            return new RenderTexture(Mathf.Max(2, (int)w), Mathf.Max(2, (int)h), 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        }

        private void TeardownPreviewResources()
        {
            if (_previewRoot != null) { DestroyImmediate(_previewRoot); _previewRoot = null; }
            _actionPlayer = null;
            _idlePlayer = null;
            ReleaseRt(ref _actionRawRt);
            ReleaseRt(ref _actionKeyedRt);
            ReleaseRt(ref _idleRawRt);
            ReleaseRt(ref _idleKeyedRt);
            if (_previewChromaMat != null) { DestroyImmediate(_previewChromaMat); _previewChromaMat = null; }
            _previewPlaying = false;
            _actionBlitDirty = false;
            _previewActionClip = null;
            _previewIdleClip = null;
        }

        private static void ReleaseRt(ref RenderTexture rt)
        {
            if (rt == null) return;
            rt.Release();
            DestroyImmediate(rt);
            rt = null;
        }

        /// <summary>帧驱动：按视频帧号变化 blit 抠色并重绘画布（播放中逐帧；seek/参数变更走 dirty 标记）</summary>
        private long _lastActionFrame = -1;
        private long _lastIdleFrame = -1;

        private void OnPreviewUpdate()
        {
            if (!_calibOpen || _previewRoot == null) return;
            bool needRepaint = false;
            // 起播自愈：EnsurePreviewResources 在窗口构建上下文（CreateGUI/RefreshAll）中调 Play() 请求
            // 可能被引擎丢弃（实证 frame 恒 -1 不起播）——update 正常上下文重试；isPlaying=false 按 frame 分流：
            // frame<0=从未起播（重试 Play）；frame>=0=播完自然停（勿当准备期误吃 _previewPlaying 标志）
            if (_idlePlayer != null && !_idlePlayer.isPlaying && _idlePlayer.frame < 0)
                _idlePlayer.Play();
            if (_idlePlayer != null && _idlePlayer.isPlaying)
            {
                if (_idlePlayer.frame != _lastIdleFrame)
                {
                    BlitPreview(_idleRawRt, _idleKeyedRt);
                    _lastIdleFrame = _idlePlayer.frame;
                    needRepaint = true;
                }
            }
            if (_actionPlayer != null)
            {
                // 播完判据双通道：!isPlaying（正常翻转型）或 frame 达末帧（Tuanjie WMF 怪态：isLooping=false
                // 播完后 isPlaying 可恒 True 卡末帧不翻转——frame>=frameCount-1 才是可靠终点）
                bool reachedEnd = _actionPlayer.clip != null
                    && _actionPlayer.frame >= (long)_actionPlayer.frameCount - 1;
                if (_previewPlaying && (!_actionPlayer.isPlaying || reachedEnd))
                {
                    if (_actionPlayer.frame < 0)
                        _actionPlayer.Play(); // Play 请求被窗口构建上下文丢弃——update 上下文重试
                    else
                    {
                        // isLooping=false 播完自然停：显式 Pause（Tuanjie WMF 怪态 isPlaying 可恒 True——
                        // 不 Pause 的话下次 Play 是 no-op=第二次播放无反应）+标记停止+末帧补一次+按钮回「播放」
                        _previewPlaying = false;
                        if (_actionPlayer.isPlaying) _actionPlayer.Pause();
                        BlitPreview(_actionRawRt, _actionKeyedRt);
                        SyncCalibPlayBtn();
                    }
                }
                if (_previewPlaying || _actionBlitDirty)
                {
                    if (_actionPlayer.frame != _lastActionFrame || _actionBlitDirty)
                    {
                        BlitPreview(_actionRawRt, _actionKeyedRt);
                        _lastActionFrame = _actionPlayer.frame;
                        _actionBlitDirty = false;
                        needRepaint = true;
                    }
                }
                if (_previewPlaying && _actionPlayer.isPlaying)
                    needRepaint = true; // 播放头推进（同一视频帧内也要挪播放头位置）
            }
            // 播放时刻实时显示（拖动/步进/播放全覆盖——数据变化就刷文本，代价可忽略）
            if (_calibTimeLabel != null && _actionPlayer != null && _asset != null)
            {
                float speedCache = PreviewSpeed();
                float headT = Mathf.Clamp((float)_actionPlayer.time / speedCache, 0f, _asset.totalTime);
                var text = $"t={headT:0.00}s / {_asset.totalTime:0.00}s";
                if (_calibTimeLabel.text != text) _calibTimeLabel.text = text;
            }
            if (needRepaint && _calibCanvasHost != null)
                Repaint(); // EditorWindow 级重绘（Tuanjie IMGUIContainer 无 Repaint——窗口重绘连带画布刷新）
        }

        private void BlitPreview(RenderTexture src, RenderTexture dst)
        {
            if (src == null || dst == null) return;
            // ChromaKey 材质带 Blend SrcAlpha OneMinusSrcAlpha（透明队列 shader）——直接 Blit 时源帧按
            // alpha 混合叠加到目标 RT 已有内容上，RT 不清零则逐帧累积叠影（2026-10-05 用户报障
            // 「同位置叠加完全不透明，像没删上一帧」；战斗无此问题：该材质在战斗中只挂 quad 混到
            // 每帧清屏的屏幕、VideoPlayer→RT 是覆盖式渲染）。先 GL.Clear 清零目标=恢复单帧语义。
            var prev = RenderTexture.active;
            RenderTexture.active = dst;
            GL.Clear(false, true, Color.clear);
            RenderTexture.active = prev;
            if (_previewChromaMat != null) Graphics.Blit(src, dst, _previewChromaMat);
            else Graphics.Blit(src, dst); // shader 缺失兜底：绿幕直显（聊胜于无）
        }

        // ==================== 校准画布（IMGUI：待机基准叠加+判定线+标尺+播放头） ====================

        private GUIStyle _rulerTickStyle;

        private void DrawCalibCanvas()
        {
            // 固定坐标画布（不走 GUILayout）：IMGUIContainer 内 GUILayoutUtility.GetRect 在视频播放的
            // 高频重绘下实测 rect 逐次下移——每帧多画一份安柏逐层叠排（2026-10-05 用户报障）；固定 rect 免疫
            var canvas = new Rect(0f, 0f, CalibCanvasW, CalibCanvasH);
            var asset = _asset;
            var data = _previewSkillData;
            if (asset == null || data == null) return;

            if (_rulerTickStyle == null)
                _rulerTickStyle = new GUIStyle(EditorStyles.miniLabel);

            float rulerH = 26f;
            EditorGUI.DrawRect(canvas, new Color(0.12f, 0.13f, 0.15f));
            var ruler = new Rect(canvas.x, canvas.yMax - rulerH, canvas.width, rulerH);
            EditorGUI.DrawRect(ruler, new Color(0.08f, 0.09f, 0.10f));

            float groundY = ruler.y - 4f;
            EditorGUI.DrawRect(new Rect(canvas.x, groundY, canvas.width, 1.5f), new Color(0.38f, 0.40f, 0.43f)); // 贴地线

            float speed = PreviewSpeed();
            float comp = data.动作片缩放补偿 > 0f ? data.动作片缩放补偿 : 1f;
            var offset = data.动作片位置偏移;
            float totalTime = Mathf.Max(0.05f, asset.totalTime);

            // 比例尺：1 立牌高（AvatarHeight=0.55 世界单位）= idleH 像素——位置偏移的世界单位→像素换算
            float idleH = (groundY - canvas.y - 6f) * 0.86f;
            float pxPerWorld = idleH / 0.55f;

            // 待机基准（幽灵底图）：DrawTexture 后盖黑纱——可见形状但不抢戏
            if (_idleKeyedRt != null)
            {
                float idleAspect = _idleKeyedRt.width / (float)_idleKeyedRt.height;
                var idleRect = new Rect(canvas.center.x - idleH * idleAspect * 0.5f, groundY - idleH, idleH * idleAspect, idleH);
                GUI.DrawTexture(idleRect, _idleKeyedRt);
                EditorGUI.DrawRect(idleRect, new Color(0f, 0f, 0f, 0.5f));
            }

            // 动作片当前帧：comp/offset 同构摆放（与战斗 UnitView quad 数学一致：Y正上浮、X正右移）
            if (_actionKeyedRt != null)
            {
                float actionAspect = _actionKeyedRt.width / (float)_actionKeyedRt.height;
                float actionH = idleH * comp;
                float actionW = actionH * actionAspect;
                float bottom = groundY - offset.y * pxPerWorld;
                float cx = canvas.center.x + offset.x * pxPerWorld;
                GUI.DrawTexture(new Rect(cx - actionW * 0.5f, bottom - actionH, actionW, actionH), _actionKeyedRt);
            }

            float XOf(float t) => canvas.x + Mathf.Clamp01(t / totalTime) * canvas.width;

            // 判定事件竖线（画面区淡线：首发红、连发橙）——所见即所得对齐辅助
            var judgments = SkillTimelineQuery.ClipsOf(asset, SkillTrackType.Judgment);
            var shots = Mathf.Max(1, data.GetInt(SkillParamKey.DamageCount, 1));
            foreach (var clip in judgments)
            {
                int n = clip.hitInterval > 0f ? shots : 1;
                for (int i = 0; i < n; i++)
                {
                    float t = clip.startTime + clip.hitInterval * i;
                    if (t > totalTime) break;
                    var c = i == 0 ? new Color(1f, 0.4f, 0.35f, 0.30f) : new Color(1f, 0.8f, 0.35f, 0.26f);
                    EditorGUI.DrawRect(new Rect(XOf(t), canvas.y, 1f, ruler.y - canvas.y), c);
                }
            }

            // 标尺：刻度+数字
            for (int i = 0; i * 0.1f <= totalTime + 0.001f; i++)
            {
                float t = i * 0.1f;
                float x = XOf(t);
                bool major = i % 5 == 0;
                EditorGUI.DrawRect(new Rect(x, ruler.y, 1f, major ? 8f : 4f),
                    new Color(0.55f, 0.58f, 0.6f, major ? 0.9f : 0.5f));
                if (major)
                    GUI.Label(new Rect(x + 3f, ruler.yMax - 14f, 40f, 14f), $"{t:0.0}s", _rulerTickStyle);
            }

            // 动作片覆盖区条（0~片长/速度）：片长 vs 总时长直观对比
            float clipLen = (float)(_actionPlayer != null && _actionPlayer.clip != null ? _actionPlayer.clip.length : 0.0);
            float actionSpan = clipLen > 0f ? Mathf.Min(totalTime, clipLen / speed) : 0f;
            if (actionSpan > 0f)
                EditorGUI.DrawRect(new Rect(ruler.x, ruler.y + 1f, XOf(actionSpan) - ruler.x, 3f),
                    new Color(0.35f, 0.6f, 0.9f, 0.85f));

            // 判定线（标尺亮色版）
            foreach (var clip in judgments)
            {
                int n = clip.hitInterval > 0f ? shots : 1;
                for (int i = 0; i < n; i++)
                {
                    float t = clip.startTime + clip.hitInterval * i;
                    if (t > totalTime) break;
                    EditorGUI.DrawRect(new Rect(XOf(t), ruler.y, 1.5f, ruler.height - 6f),
                        i == 0 ? new Color(1f, 0.4f, 0.35f) : new Color(1f, 0.8f, 0.35f));
                }
            }

            // 播放头（白线+时刻）
            if (_actionPlayer != null)
            {
                float headT = (float)_actionPlayer.time / speed;
                float hx = XOf(Mathf.Clamp(headT, 0f, totalTime));
                EditorGUI.DrawRect(new Rect(hx, canvas.y, 1.5f, ruler.y - canvas.y), Color.white);
                GUI.Label(new Rect(hx + 4f, canvas.y + 2f, 90f, 16f), $"t={headT:0.00}s", _rulerTickStyle);
            }

            // 标尺拖拽 seek（拖动=暂停+定位；校准工作流=拖到判定线附近逐帧核对）
            var evt = Event.current;
            if (evt.type == UnityEngine.EventType.MouseDown && ruler.Contains(evt.mousePosition))
            {
                _scrubbing = true;
                _previewPlaying = false;
                if (_actionPlayer != null && _actionPlayer.isPlaying) _actionPlayer.Pause();
                SyncCalibPlayBtn();
                SeekToMouse(ruler, evt.mousePosition.x, totalTime, speed);
                evt.Use();
            }
            else if (evt.type == UnityEngine.EventType.MouseDrag && _scrubbing)
            {
                SeekToMouse(ruler, evt.mousePosition.x, totalTime, speed);
                evt.Use();
            }
            else if (evt.type == UnityEngine.EventType.MouseUp && _scrubbing)
            {
                _scrubbing = false;
                evt.Use();
            }
        }

        private void SeekToMouse(Rect ruler, float mouseX, float totalTime, float speed)
        {
            if (_actionPlayer == null || _actionPlayer.clip == null) return;
            float t = Mathf.Clamp01((mouseX - ruler.x) / ruler.width) * totalTime;
            float clipLen = (float)_actionPlayer.clip.length;
            t = Mathf.Min(t, clipLen / speed); // 片长外=末帧
            _actionPlayer.time = t * speed;
            _actionBlitDirty = true;
            Repaint(); // EditorWindow 级重绘（Tuanjie IMGUIContainer 无 Repaint——窗口重绘连带画布刷新）
        }

        // ==================== 总刷新 ====================

        private void RefreshAll()
        {
            RefreshHeader();
            RebuildTimeline();
            RefreshCalibration(); // 动作片校准节（资产/技能变化时反查与预览重建）
            RefreshClipPanel();
            RefreshValidation();
            if (_statusLabel != null)
                _statusLabel.text = StatusText();
        }
    }
}
