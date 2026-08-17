#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using GIC.Framework;
using GIC.Data;

namespace GIC.Editor
{
    /// <summary>
    /// 地图坐标取点器 — 在 SceneView 中 Ctrl+左键点击地图，直接把世界坐标写入 MapConfig，
    /// 解决手动填 viewCenterWorld / anchor.world 时"不知道对不对"的问题。
    /// 所有已配置的区域视野中心与锚点以 Gizmo 常驻显示（黄=视野中心，青=锚点，红=当前编辑目标），
    /// 点击后 Gizmo 立即移动，所见即所得。
    /// 保存语义与项目规范一致：点击实时写入内存（可反复调整），「保存到磁盘」按钮落盘。
    /// </summary>
    public class MapPickPointTool : EditorWindow
    {
        private const string MapScreenScenePath = "Assets/Scenes/MapScreen.unity";

        private enum PickMode { 区域视野中心, 已有锚点, 新建锚点, 标定地图 }

        private PickMode _mode = PickMode.区域视野中心;
        private RegionName _region = RegionName.Mondstadt;
        private PositionName _newAnchorName = PositionName.StarsandShoal;
        private int _anchorIndex = -1;
        private string _lastPick = "尚未取点";

        // 标定地图模式：参照锚点 A/B（已知世界坐标）+ 在新图上点出两者当前位置
        private int _calibAnchorA = -1, _calibAnchorB = -1;
        private bool _hasPickA;
        private Vector2 _pickA;
        private bool _hasPickB;   // 解算完成后保留 B 点白圈（可视化"点了哪里"），重取/换参照时清除
        private Vector2 _pickB;

        /// <summary>方向校验容差（度）：图片轴与世界轴平行的模型下，两次点击连线方向必须与锚点已知位移方向一致。
        /// 超差=点错位置或图有旋转，拒绝解算（斜率是固定约束，不是自由量）</summary>
        private const float 标定方向容差 = 3f;

        /// <summary>
        /// 把点投影到「过点 P、方向 dir」的直线上（B 点硬约束：第二次点击只能落在此线）。
        /// 返回投影点与原点的距离（用于判断点击是否离谱地远）。
        /// </summary>
        private static Vector2 ProjectOntoLine(Vector2 point, Vector2 linePoint, Vector2 dir)
        {
            dir.Normalize();
            return linePoint + dir * Vector2.Dot(point - linePoint, dir);
        }

        // 锚点平铺索引 → (RegionData, AnchorData)
        private readonly List<(GIC.Data.MapConfig.RegionData region, GIC.Data.MapConfig.AnchorData anchor)> _allAnchors = new();

        private Label _coordLabel;
        private VisualElement _modeContainer;

        [MenuItem("Tools/地图/坐标取点器", priority = -58)]
        public static void Open()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != MapScreenScenePath)
                EditorSceneManager.OpenScene(MapScreenScenePath, OpenSceneMode.Single);
            GetWindow<MapPickPointTool>("地图坐标取点器");
            FrameMap();
        }

        /// <summary>把 SceneView 相机对准地图中心（垂直画布正视，整图可见）。地图不可见时先点这个。</summary>
        private static void FrameMap()
        {
            var sv = SceneView.lastActiveSceneView;
            if (sv == null) { GICLog.Warn("[MapPickPointTool] 无活动 SceneView"); return; }
            var cfg = LoadConfig();

            // 中心与视野范围：优先按标定参数计算整图范围；无图时退化为全部标记点的包围盒中心
            Vector2 center;
            float viewSize = 80f;
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/Map/Textures/all_map.jpg");
            if (cfg != null && sprite != null)
            {
                float w = sprite.rect.width * cfg.WorldUnitsPerPixel;
                float d = sprite.rect.height * cfg.WorldUnitsPerPixel;
                center = new Vector2(cfg.MapOrigin.x + w * 0.5f, cfg.MapOrigin.y - d * 0.5f);
                viewSize = d * 0.55f;
            }
            else if (cfg != null)
            {
                var all = new List<Vector2>();
                foreach (var r in cfg.AllRegions)
                {
                    all.Add(r.viewCenterWorld);
                    foreach (var a in r.anchors) all.Add(a.world);
                }
                if (all.Count > 0)
                {
                    center = all.Aggregate(Vector2.zero, (s, p) => s + p) / all.Count;
                    viewSize = all.Max(p => Mathf.Abs(p.x - center.x)) * 1.2f;
                }
                else center = Vector2.zero;
            }
            else center = Vector2.zero;

            // 退出 2D 模式：2D 模式强制相机沿 +Z 水平看，地图侧对相机呈一条线（什么都看不见）
            sv.in2DMode = false;
            sv.orthographic = true;
            sv.LookAtDirect(new Vector3(center.x, center.y, 0f), Quaternion.identity, viewSize);
            SceneView.RepaintAll();
        }

        // ==================== 视角对准 ====================

        private const float 目标对准视野尺寸 = 12f; // 单点目标的对准视野（正交半高，看得清标记又不失上下文）

        /// <summary>通用对准：正交注视目标点（退 2D 模式），失败提示原因</summary>
        private static bool LookAtPoint(Vector2 worldXY, float viewSize, string what)
        {
            var sv = SceneView.lastActiveSceneView;
            if (sv == null) { GICLog.Warn("[MapPickPointTool] 无活动 SceneView"); return false; }
            sv.in2DMode = false;
            sv.orthographic = true;
            sv.LookAtDirect(new Vector3(worldXY.x, worldXY.y, 0f), Quaternion.identity, viewSize);
            SceneView.RepaintAll();
            GICLog.Info($"[MapPickPointTool] 视角已对准{what} ({worldXY.x:F2}, {worldXY.y:F2})");
            return true;
        }

        /// <summary>对准当前编辑目标（随模式联动）</summary>
        private void FrameEditingTarget()
        {
            var cfg = LoadConfig();
            if (cfg == null) return;
            switch (_mode)
            {
                case PickMode.区域视野中心:
                {
                    var r = cfg.GetRegion(_region);
                    if (r == null) { GICLog.Warn($"[MapPickPointTool] 区域 {_region} 无 RegionData"); return; }
                    LookAtPoint(r.viewCenterWorld, 目标对准视野尺寸, $"区域 {_region} 视野中心");
                    return;
                }
                case PickMode.已有锚点:
                case PickMode.新建锚点:
                {
                    // 已有锚点模式用下拉选中项；新建模式对准已选区域视野中心（新锚点尚不存在）
                    if (_mode == PickMode.已有锚点 && _anchorIndex >= 0 && _anchorIndex < _allAnchors.Count)
                    {
                        var a = _allAnchors[_anchorIndex].anchor;
                        LookAtPoint(a.world, 目标对准视野尺寸, $"锚点 {a.positionName}");
                        return;
                    }
                    var r2 = cfg.GetRegion(_region);
                    if (r2 == null) { GICLog.Warn($"[MapPickPointTool] 区域 {_region} 无 RegionData"); return; }
                    LookAtPoint(r2.viewCenterWorld, 目标对准视野尺寸, $"区域 {_region} 视野中心");
                    return;
                }
                case PickMode.标定地图:
                {
                    // 对准 A/B 两参照点的中点，视野取两点距离的 1.4 倍保证都在画面内
                    if (_calibAnchorA < 0 || _calibAnchorB < 0 ||
                        _calibAnchorA >= _allAnchors.Count || _calibAnchorB >= _allAnchors.Count)
                    {
                        GICLog.Warn("[MapPickPointTool] 请先选择参照锚点 A/B");
                        return;
                    }
                    var a = _allAnchors[_calibAnchorA].anchor.world;
                    var b = _allAnchors[_calibAnchorB].anchor.world;
                    var mid = (a + b) * 0.5f;
                    LookAtPoint(mid, Mathf.Max(目标对准视野尺寸, Vector2.Distance(a, b) * 0.7f), "标定参照 A/B 中点");
                    return;
                }
            }
        }

        /// <summary>对准当前选中区域（区域视野中心）</summary>
        private void FrameCurrentRegion()
        {
            var cfg = LoadConfig();
            var r = cfg?.GetRegion(_region);
            if (r == null) { GICLog.Warn($"[MapPickPointTool] 区域 {_region} 无 RegionData"); return; }
            LookAtPoint(r.viewCenterWorld, 目标对准视野尺寸, $"区域 {_region} 视野中心");
        }

        /// <summary>对准下拉选中的锚点（已有锚点模式的选择）</summary>
        private void FrameSelectedAnchor()
        {
            if (_anchorIndex < 0 || _anchorIndex >= _allAnchors.Count)
            {
                GICLog.Warn("[MapPickPointTool] 当前无可选锚点（切到「已有锚点」模式并选择）");
                return;
            }
            var a = _allAnchors[_anchorIndex].anchor;
            LookAtPoint(a.world, 目标对准视野尺寸, $"锚点 {a.positionName}");
        }

        /// <summary>对准坐标原点（mapOrigin，图片左上角在世界中的位置）</summary>
        private void FrameOrigin()
        {
            var cfg = LoadConfig();
            if (cfg == null) return;
            LookAtPoint(cfg.MapOrigin, 目标对准视野尺寸, "坐标原点");
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            Undo.undoRedoPerformed += OnUndoRedo;
        }
        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            Undo.undoRedoPerformed -= OnUndoRedo;
        }

        /// <summary>Undo/Redo 后同步：锚点索引重建 + 面板刷新（撤销改的是 MapConfig 对象，窗口与 Gizmo 需跟随）</summary>
        private void OnUndoRedo()
        {
            RebuildAnchorIndex();
            if (rootVisualElement.childCount > 0) BuildUI();
            SceneView.RepaintAll();
        }

        private void CreateGUI()
        {
            // GetWindow 复用已打开窗口时 CreateGUI 不再触发，由 BuildUI 自行重建
            minSize = new Vector2(460f, 620f); // 标定模式内容多，保证最小可用尺寸（超出部分 ScrollView 滚动）
            BuildUI();
        }

        private void BuildUI()
        {
            var root = rootVisualElement;
            root.Clear(); // 幂等重建：本方法会被 CreateGUI/保存/新建锚点多处调用，先清空防重影
            root.style.paddingTop = 10;
            root.style.paddingBottom = 10;
            root.style.paddingLeft = 12;
            root.style.paddingRight = 12;
            ConfigEditorUITK.ApplyGameFont(root); // 游戏字体（SDF 版，不再触发 TextCore 异常）

            // 整体可滚动：标定模式控件多，窗口放不下时滚动而非挤压重叠
            var scroll = new ScrollView();
            root.Add(scroll);
            var content = scroll;

            RebuildAnchorIndex();

            content.Add(ConfigEditorUITK.CreateSectionHeader("取点模式"));
            var modeField = new EnumField("目标", _mode);
            modeField.SetEnabled(false); // 仅展示当前模式，切换由下方按钮完成
            content.Add(modeField);

            var modeRow = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            foreach (PickMode m in System.Enum.GetValues(typeof(PickMode)))
            {
                var local = m;
                var btn = ConfigEditorUITK.CreateToolButton(m.ToString(), () => { _mode = local; RebuildModeControls(); modeField.SetValueWithoutNotify(_mode); });
                btn.style.flexGrow = 1;
                modeRow.Add(btn);
            }
            content.Add(modeRow);

            _modeContainer = new VisualElement();
            content.Add(_modeContainer);
            RebuildModeControls();

            content.Add(ConfigEditorUITK.CreateSectionHeader("取点结果"));
            _coordLabel = new Label(_lastPick) { style = { whiteSpace = WhiteSpace.Normal } };
            content.Add(_coordLabel);

            // ── 视角对准：整图 / 各类目标点（按当前模式智能选择默认目标）──
            content.Add(ConfigEditorUITK.CreateSectionHeader("视角对准"));
            var frameRow = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            var frameMapBtn = ConfigEditorUITK.CreateToolButton("整张地图", FrameMap);
            frameMapBtn.style.flexGrow = 1;
            frameRow.Add(frameMapBtn);

            var frameTargetBtn = ConfigEditorUITK.CreateToolButton("当前编辑目标", FrameEditingTarget);
            frameTargetBtn.style.flexGrow = 1;
            frameRow.Add(frameTargetBtn);
            content.Add(frameRow);

            var frameRow2 = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            var frameRegionBtn = ConfigEditorUITK.CreateToolButton("区域视野中心", FrameCurrentRegion);
            frameRegionBtn.style.flexGrow = 1;
            frameRow2.Add(frameRegionBtn);

            var frameAnchorBtn = ConfigEditorUITK.CreateToolButton("选中锚点", FrameSelectedAnchor);
            frameAnchorBtn.style.flexGrow = 1;
            frameRow2.Add(frameAnchorBtn);

            var frameOriginBtn = ConfigEditorUITK.CreateToolButton("坐标原点", FrameOrigin);
            frameOriginBtn.style.flexGrow = 1;
            frameRow2.Add(frameOriginBtn);
            content.Add(frameRow2);
            content.Add(new Label("「当前编辑目标」随模式联动：区域→该区域视野中心；锚点→选中锚点；标定→A/B 两参照点。") { style = { whiteSpace = WhiteSpace.Normal, opacity = 0.8f, marginBottom = 4 } });

            var saveBtn = ConfigEditorUITK.CreatePrimaryButton("保存到磁盘 (SetDirty + SaveAssets)", SaveToDisk);
            content.Add(saveBtn);

            var help = new Label(
                "用法：SceneView 中 Ctrl+左键点击地图位置取点（普通点击仍是选择物体）。\n" +
                "黄色圆 = 区域视野中心（附区域名）；青色 = 锚点（附地名）；红色 = 当前编辑目标。\n" +
                "点击后立即生效（Gizmo 移动），可反复点击调整，最后点「保存到磁盘」落盘。\n" +
                "Ctrl+Z 可逐步撤销每次取点/标定（域重载后撤销栈清空）。") { style = { whiteSpace = WhiteSpace.Normal, marginTop = 8, opacity = 0.8f } };
            content.Add(help);
        }

        private void RebuildModeControls()
        {
            _modeContainer.Clear();
            switch (_mode)
            {
                case PickMode.区域视野中心:
                {
                    var f = new EnumField("区域", _region);
                    f.RegisterValueChangedCallback(e => _region = (RegionName)e.newValue);
                    _modeContainer.Add(f);
                    var cfg = LoadConfig();
                    var has = cfg != null && cfg.GetRegion(_region) != null;
                    _modeContainer.Add(new Label(has
                        ? $"当前配置：{(cfg.GetRegion(_region).viewCenterWorld)}"
                        : "该区域暂无 RegionData，取点时将自动创建（可用于新增至冬/坎瑞亚）")
                        { style = { whiteSpace = WhiteSpace.Normal } });
                    break;
                }
                case PickMode.已有锚点:
                {
                    var choices = _allAnchors.Select(t => $"{t.region.region} · {t.anchor.positionName}").ToList();
                    if (choices.Count == 0)
                    {
                        _modeContainer.Add(new Label("MapConfig 中暂无锚点"));
                        break;
                    }
                    if (_anchorIndex < 0 || _anchorIndex >= choices.Count) _anchorIndex = 0;
                    var dd = new DropdownField("锚点", choices, _anchorIndex);
                    dd.RegisterValueChangedCallback(_ => _anchorIndex = dd.index);
                    _modeContainer.Add(dd);
                    break;
                }
                case PickMode.新建锚点:
                {
                    var rf = new EnumField("所属区域", _region);
                    rf.RegisterValueChangedCallback(e => _region = (RegionName)e.newValue);
                    var nf = new EnumField("地点", _newAnchorName);
                    nf.RegisterValueChangedCallback(e => _newAnchorName = (PositionName)e.newValue);
                    _modeContainer.Add(rf);
                    _modeContainer.Add(nf);
                    _modeContainer.Add(new Label("取点后自动创建锚点并切换到「已有锚点」模式") { style = { whiteSpace = WhiteSpace.Normal } });
                    break;
                }
                case PickMode.标定地图:
                {
                    var cfg = LoadConfig();
                    var names = _allAnchors.Select(t => t.anchor.positionName.ToString()).ToList();
                    if (names.Count < 2)
                    {
                        _modeContainer.Add(new Label("标定需要至少 2 个已有锚点作参照（当前 " + names.Count + " 个）。请先用其他模式配置锚点。") { style = { whiteSpace = WhiteSpace.Normal } });
                        break;
                    }
                    if (_calibAnchorA < 0 || _calibAnchorA >= names.Count) _calibAnchorA = 0;
                    if (_calibAnchorB < 0 || _calibAnchorB >= names.Count || _calibAnchorB == _calibAnchorA)
                        _calibAnchorB = (_calibAnchorA + 1) % names.Count;

                    _modeContainer.Add(new Label(
                        "原理：锚点世界坐标固定不动；标定调整图片位置/缩放，\n" +
                        "使你点击的图上内容移动到锚点正下方（图向锚点靠拢，锚点不移动）。\n" +
                        "① 选两个相距较远的参照锚点 A、B\n" +
                        "② Ctrl+左键点 A 在图上的实际位置（绿圈处）\n" +
                        "③ 再点 B 的实际位置（自动吸附到过 A 的约束线，只需点对远近）\n" +
                        "✎ 自检：未换图时点在 A/B 绿圈正中心 → 解出的标定应与当前一致（图不动）") { style = { whiteSpace = WhiteSpace.Normal, marginBottom = 4 } });

                    // 标签用短名（A/B）：中文长标签在 SDF 字体度量下横向溢出会与字段值重叠
                    var ddA = new DropdownField("A", names, _calibAnchorA);
                    ddA.style.marginBottom = 2;
                    ddA.RegisterValueChangedCallback(_ => { _calibAnchorA = ddA.index; _hasPickA = false; _hasPickB = false; });
                    _modeContainer.Add(ddA);
                    var ddB = new DropdownField("B", names, _calibAnchorB);
                    ddB.style.marginBottom = 4;
                    ddB.RegisterValueChangedCallback(_ => { _calibAnchorB = ddB.index; _hasPickA = false; _hasPickB = false; });
                    _modeContainer.Add(ddB);

                    _modeContainer.Add(new Label(CalibrationStepHint()) { style = { whiteSpace = WhiteSpace.Normal, marginBottom = 2 } });

                    var cfgLine = new Label($"当前标定：原点 {(cfg ? cfg.MapOrigin.ToString("F2") : "-")}，单位 {(cfg ? cfg.WorldUnitsPerPixel.ToString("F6") : "-")}") { style = { whiteSpace = WhiteSpace.Normal, opacity = 0.8f, marginBottom = 4 } };
                    _modeContainer.Add(cfgLine);

                    var resetBtn = ConfigEditorUITK.CreateToolButton("重取（清除已点击的点）", () =>
                    {
                        _hasPickA = false;
                        _hasPickB = false;
                        RebuildModeControls();
                        SceneView.RepaintAll();
                    });
                    _modeContainer.Add(resetBtn);
                    break;
                }
            }
        }

        private string CalibrationStepHint()
        {
            if (_calibAnchorA < 0 || _calibAnchorB < 0 || _calibAnchorA >= _allAnchors.Count || _calibAnchorB >= _allAnchors.Count)
                return "▶ 请先选择参照锚点 A、B";
            if (_hasPickB)
                return "✓ 标定完成（两白圈=你的点击点）。下一步：「保存到磁盘」+ 菜单 Tools/地图/应用地图标定";
            var a = _allAnchors[_calibAnchorA].anchor;
            var b = _allAnchors[_calibAnchorB].anchor;
            float knownDist = Vector2.Distance(a.world, b.world);
            string known = $"（已知世界距离 {knownDist:F2}）";
            if (!_hasPickA)
                return $"▶ 下一步：点击 {a.positionName} 在图上的实际位置 {known}";
            return $"▶ 下一步：点击 {_allAnchors[_calibAnchorB].anchor.positionName} 的实际位置 {known}";
        }

        private static GIC.Data.MapConfig LoadConfig()
            => AssetDatabase.LoadAssetAtPath<GIC.Data.MapConfig>("Assets/Resources/Configs/MapConfig.asset");

        private void RebuildAnchorIndex()
        {
            _allAnchors.Clear();
            var cfg = LoadConfig();
            if (cfg == null) return;
            foreach (var r in cfg.AllRegions)
                foreach (var a in r.anchors)
                    _allAnchors.Add((r, a));
        }

        // ==================== SceneView 交互与 Gizmo ====================

        private void OnSceneGUI(SceneView sv)
        {
            DrawGizmos();

            var e = Event.current;
            if (e.type == UnityEngine.EventType.MouseDown && e.button == 0 && e.control && !e.alt)
            {
                var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                if (Mathf.Abs(ray.direction.z) > 0.0001f)
                {
                    float t = -ray.origin.z / ray.direction.z;
                    if (t > 0f)
                    {
                        var p = ray.origin + ray.direction * t;
                        ApplyPick(new Vector2(p.x, p.y));
                        e.Use();
                    }
                }
            }
        }

        private void DrawGizmos()
        {
            var cfg = LoadConfig();
            if (cfg == null) return;

            foreach (var r in cfg.AllRegions)
            {
                var pos = new Vector3(r.viewCenterWorld.x, r.viewCenterWorld.y, 0f);
                DrawMarker(pos, 1.1f, new Color(1f, 0.85f, 0.2f, 0.35f));
                Handles.Label(pos + Vector3.up * 1.5f + Vector3.back * 2f, $"◆ {r.region} 视野中心");
            }

            foreach (var (region, anchor) in _allAnchors)
            {
                var pos = new Vector3(anchor.world.x, anchor.world.y, 0f);
                DrawMarker(pos, 0.7f, new Color(0.2f, 0.9f, 0.9f, 0.35f));
                Handles.Label(pos + Vector3.up * 1f + Vector3.back * 1.2f, anchor.positionName.ToString());
            }

            // 当前编辑目标红色高亮
            Vector3 target = default;
            bool has = false;
            switch (_mode)
            {
                case PickMode.区域视野中心:
                {
                    var r = cfg.GetRegion(_region);
                    if (r != null) { target = new Vector3(r.viewCenterWorld.x, r.viewCenterWorld.y, 0f); has = true; }
                    break;                }
                case PickMode.已有锚点 when _anchorIndex >= 0 && _anchorIndex < _allAnchors.Count:
                {
                    var a = _allAnchors[_anchorIndex].anchor;
                    target = new Vector3(a.world.x, a.world.y, 0f);
                    has = true;
                    break;
                }
                case PickMode.标定地图 when _calibAnchorA >= 0 && _calibAnchorB >= 0 &&
                    _calibAnchorA < _allAnchors.Count && _calibAnchorB < _allAnchors.Count:
                {
                    // 参照锚点 A/B 绿色标记其"应在"位置；已点击的位置画白标记（解算后保留两个）
                    var wa = _allAnchors[_calibAnchorA].anchor.world;
                    var wb = _allAnchors[_calibAnchorB].anchor.world;
                    DrawMarker(new Vector3(wa.x, wa.y, 0f), 1.3f, new Color(0.3f, 1f, 0.4f, 0.35f));
                    DrawMarker(new Vector3(wb.x, wb.y, 0f), 1.3f, new Color(0.3f, 1f, 0.4f, 0.35f));
                    Handles.Label(new Vector3(wa.x, wa.y, 0f) + Vector3.up * 1.5f + Vector3.back * 2f, $"A {_allAnchors[_calibAnchorA].anchor.positionName}（应在此）");
                    Handles.Label(new Vector3(wb.x, wb.y, 0f) + Vector3.up * 1.5f + Vector3.back * 2f, $"B {_allAnchors[_calibAnchorB].anchor.positionName}（应在此）");

                    // B 点约束线：过已点击的 A 点、方向=A→B 已知位移。第二击只能落在此线上（自动投影）
                    if (_hasPickA)
                    {
                        Vector2 dir = (wb - wa).normalized;
                        var p0 = new Vector3(_pickA.x, _pickA.y, -0.1f);
                        var p1 = p0 + new Vector3(dir.x, dir.y, 0f) * 200f;
                        var p2 = p0 - new Vector3(dir.x, dir.y, 0f) * 200f;
                        Handles.color = new Color(1f, 1f, 1f, 0.35f);
                        Handles.DrawLine(p2, p1);
                        Handles.Label(p0 + Vector3.up * 1f + Vector3.back * 1.5f, "B 须落在此线上");
                    }

                    if (_hasPickA) DrawMarker(new Vector3(_pickA.x, _pickA.y, 0f), 1.0f, new Color(1f, 1f, 1f, 0.35f));
                    if (_hasPickB) DrawMarker(new Vector3(_pickB.x, _pickB.y, 0f), 1.0f, new Color(1f, 1f, 1f, 0.35f));
                    break;
                }
            }
            if (has) DrawMarker(target, 1.6f, new Color(1f, 0.25f, 0.2f, 0.35f));
        }

        /// <summary>标记 = 1 个圆环 + 中心半透明小实心圆（半径=圆环 20%，半透明可见底下地图）</summary>
        private static void DrawMarker(Vector3 pos, float size, Color fill)
        {
            Handles.color = fill;
            Handles.DrawSolidDisc(pos, Vector3.back, size * 0.2f);
            Handles.color = new Color(fill.r, fill.g, fill.b, 1f); // 圆环不透明，轮廓清晰
            Handles.DrawWireDisc(pos, Vector3.back, size);
        }

        // ==================== 取点写回 ====================

        /// <summary>写入标定参数（mapOrigin / worldUnitsPerPixel 为私有序列化字段，走 SerializedObject；可撤销）</summary>
        private static void SetCalibration(GIC.Data.MapConfig cfg, Vector2 origin, float unit)
        {
            var so = new SerializedObject(cfg);
            so.FindProperty("mapOrigin").vector2Value = origin;
            so.FindProperty("worldUnitsPerPixel").floatValue = unit;
            so.ApplyModifiedProperties();
        }

        private void ApplyPick(Vector2 world)
        {
            var cfg = LoadConfig();
            if (cfg == null) { GICLog.Error("[MapPickPointTool] MapConfig.asset 未找到"); return; }

            // 撤销注册：每次取点为一个 Undo 单元（Ctrl+Z 逐步回退；域重载后撤销栈清空属 Unity 行为）
            Undo.RecordObject(cfg, "地图取点");

            switch (_mode)
            {
                case PickMode.区域视野中心:
                {
                    var r = cfg.GetRegion(_region);
                    if (r != null)
                    {
                        r.viewCenterWorld = world;
                    }
                    else
                    {
                        // 区域无 RegionData（如至冬/坎瑞亚）：自动创建（可撤销）
                        var so = new SerializedObject(cfg);
                        var arr = so.FindProperty("regions");
                        arr.arraySize++;
                        var elem = arr.GetArrayElementAtIndex(arr.arraySize - 1);
                        elem.FindPropertyRelative("region").intValue = (int)_region;
                        elem.FindPropertyRelative("viewCenterWorld").vector2Value = world;
                        so.ApplyModifiedProperties();
                        cfg.BuildCache();
                        GICLog.Info($"[MapPickPointTool] 已创建区域 {_region} 的 RegionData");
                    }
                    break;
                }
                case PickMode.已有锚点:
                {
                    if (_anchorIndex < 0 || _anchorIndex >= _allAnchors.Count) { GICLog.Warn("[MapPickPointTool] 未选择锚点"); return; }
                    _allAnchors[_anchorIndex].anchor.world = world;
                    break;
                }
                case PickMode.新建锚点:
                {
                    var r = cfg.GetRegion(_region);
                    if (r == null) { GICLog.Warn($"[MapPickPointTool] 区域 {_region} 无 RegionData，请先用「区域视野中心」模式创建"); return; }
                    r.anchors.Add(new GIC.Data.MapConfig.AnchorData(_newAnchorName, world));
                    _mode = PickMode.已有锚点;
                    RebuildAnchorIndex();
                    _anchorIndex = _allAnchors.Count - 1;
                    if (rootVisualElement.childCount > 0) BuildUI();
                    break;
                }
                case PickMode.标定地图:
                {
                    if (_calibAnchorA < 0 || _calibAnchorB < 0 ||
                        _calibAnchorA >= _allAnchors.Count || _calibAnchorB >= _allAnchors.Count ||
                        _calibAnchorA == _calibAnchorB)
                    {
                        GICLog.Warn("[MapPickPointTool] 请先在面板中选择两个不同的参照锚点 A/B");
                        return;
                    }

                    var anchorA = _allAnchors[_calibAnchorA].anchor;
                    var anchorB = _allAnchors[_calibAnchorB].anchor;

                    if (!_hasPickA)
                    {
                        _pickA = world;
                        _hasPickA = true;
                        _hasPickB = false;
                        _lastPick = $"① 已点击 {anchorA.positionName} 的实际位置 ({world.x:F2}, {world.y:F2})\n请继续点击 {anchorB.positionName}";
                    }
                    else
                    {
                        // B 点硬约束：第二次点击只能落在「过 A 点、方向=已知位移向量」的直线上（斜率固定）。
                        // 点击位置投影到该线——用户横着点歪了，落点自动滑到线上正确的纵向位置
                        Vector2 knownDir = (anchorB.world - anchorA.world).normalized;
                        Vector2 pickB = ProjectOntoLine(world, _pickA, knownDir);

                        // 点击的世界坐标 → 新图上的像素坐标（按当前标定换算）
                        var pxA = cfg.WorldToPixel(_pickA);
                        var pxB = cfg.WorldToPixel(pickB);
                        float pxDistance = Vector2.Distance(pxA, pxB);
                        float known = Vector2.Distance(anchorA.world, anchorB.world);

                        if (pxDistance < 1f || known < 0.01f)
                        {
                            _lastPick = "两次点击位置过近（像素距离 < 1），请重取（选相距更远的两个锚点）";
                            _hasPickA = false;
                            _hasPickB = false;
                        }
                        else
                        {
                            // 投影点落在 A 的反方向（点反了）：提示但不硬拒（用户可有意为之的场景不存在，直接提示重取）
                            float sign = Vector2.Dot(world - _pickA, knownDir);
                            if (sign < 0f)
                            {
                                _lastPick = $"点击位置在参照线反方向（B 应在 A 的 {knownDir} 方向侧），请重新点击 B";
                                _hasPickB = false;
                                // 保留 _hasPickA，继续等第二次点击
                            }
                            else
                            {
                            // 两点解标定：新单位 = 已知世界距离 ÷ 新图像素距离；
                            // 新原点使 pxA 映射回 anchorA.world（Y 翻转：图片 y 向下、世界 z 向上）
                            float newUnit = known / pxDistance;
                            var newOrigin = new Vector2(
                                anchorA.world.x - pxA.x * newUnit,
                                anchorA.world.y + pxA.y * newUnit);
                            SetCalibration(cfg, newOrigin, newUnit);

                            // 保留两个点击点白圈（可视化），重取/换参照时清除
                            _pickB = pickB;
                            _hasPickA = true;
                            _hasPickB = true;
                            _lastPick =
                                $"✓ 标定完成：原点 ({newOrigin.x:F2}, {newOrigin.y:F2})，单位长度 {newUnit:F6}\n" +
                                $"（世界 {known:F2} ÷ 像素 {pxDistance:F0}）\n" +
                                "锚点/区域数据未动。请点「保存到磁盘」，再运行菜单\n" +
                                "Tools/地图/应用地图标定到 MapScreen 场景（场景内图片即时重摆，目测对齐）";
                            GICLog.Info($"[MapPickPointTool] 标定完成 origin=({newOrigin.x:F2},{newOrigin.y:F2}) unit={newUnit:F6}");
                            }
                        }
                    }

                    EditorUtility.SetDirty(cfg);
                    if (_coordLabel != null) _coordLabel.text = _lastPick;
                    RebuildModeControls();
                    SceneView.RepaintAll();
                    return; // 标定模式不走下方锚点像素回显
                }
            }

            EditorUtility.SetDirty(cfg);
            var px = cfg.WorldToPixel(world);
            _lastPick = $"世界坐标: ({world.x:F2}, {world.y:F2})\n图片像素: ({px.x:F0}, {px.y:F0})（左上起，可对照 Photoshop 量点）";
            if (_coordLabel != null) _coordLabel.text = _lastPick;
            SceneView.RepaintAll();
            GICLog.Info($"[MapPickPointTool] 取点 world=({world.x:F2},{world.y:F2}) pixel=({px.x:F0},{px.y:F0})");
        }

        private void SaveToDisk()
        {
            var cfg = LoadConfig();
            if (cfg == null) return;
            EditorUtility.SetDirty(cfg);
            AssetDatabase.SaveAssets();
            GICLog.Info("[MapPickPointTool] MapConfig 已保存到磁盘");
            RebuildAnchorIndex();
            if (rootVisualElement.childCount > 0) BuildUI();
        }
    }
}
#endif
