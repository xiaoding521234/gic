using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using GIC.Data;
using GIC.Framework;

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
            window.minSize = new Vector2(980f, 660f);
            // 打开时若当前选中就是时轮资产则直接载入
            if (Selection.activeObject is SkillTimelineAsset selected)
                window._asset = selected;
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

            // 时间轴区（内容多时滚动）
            var scroll = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
            scroll.style.flexGrow = 1f;
            _timelineArea = new VisualElement();
            scroll.Add(_timelineArea);
            root.Add(scroll);

            // 选中 clip 属性面板
            root.Add(ConfigEditorUITK.CreateSectionHeader("选中 clip"));
            var panelScroll = new ScrollView(ScrollViewMode.Vertical);
            panelScroll.style.maxHeight = 190f;
            _clipPanel = new VisualElement();
            panelScroll.Add(_clipPanel);
            root.Add(_clipPanel);

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
                {
                    _asset = current;
                    _selected = null;
                    RefreshAll();
                }
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
                    clip.startTime = SnapTo(Mathf.Max(0f, drag.startValue + delta));
                    clip.endTime = Mathf.Max(clip.startTime + Snap, SnapTo(drag.startValue + (clip.endTime - drag.startValue) + delta));
                }
                else
                {
                    clip.endTime = Mathf.Max(clip.startTime + Snap, SnapTo(drag.startValue + delta));
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
            public DragMode mode;
            public bool undoRecorded;
        }

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
                endTime = Mathf.Min(0.3f, Mathf.Max(0.1f, _asset.totalTime)),
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

            // 第一行：轨道 + kind + 删除
            var row1 = new VisualElement();
            row1.style.flexDirection = FlexDirection.Row;
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

            // 第二行：时刻字段
            var row2 = new VisualElement();
            row2.style.flexDirection = FlexDirection.Row;
            row2.style.marginTop = 4;
            row2.Add(FloatFieldOf("起点", clip.startTime, v =>
            {
                clip.startTime = Mathf.Max(0f, v);
                AfterTimeEdit(clip);
            }));
            row2.Add(FloatFieldOf("终点", clip.endTime, v =>
            {
                clip.endTime = Mathf.Max(clip.startTime + Snap, v);
                AfterTimeEdit(clip);
            }));
            row2.Add(FloatFieldOf("连发间隔", clip.hitInterval, v =>
            {
                clip.hitInterval = Mathf.Max(0f, v);
                MarkDirtyLight();
            }));
            panel.Add(row2);

            // 判定轨载荷（投射物规格）
            if (track == SkillTrackType.Judgment)
            {
                var row3 = new VisualElement();
                row3.style.flexDirection = FlexDirection.Row;
                row3.style.marginTop = 4;
                row3.Add(FloatFieldOf("弹速", clip.projectileSpeed, v => { clip.projectileSpeed = Mathf.Max(0f, v); MarkDirtyLight(); }));
                row3.Add(FloatFieldOf("判定直径", clip.hitDiameter, v => { clip.hitDiameter = Mathf.Max(0f, v); MarkDirtyLight(); }));
                var rangeField = new IntegerField("射程") { value = clip.maxRange };
                rangeField.style.width = 130;
                rangeField.style.marginLeft = 8;
                rangeField.RegisterValueChangedCallback(evt =>
                {
                    RecordUndo("改射程");
                    clip.maxRange = evt.newValue;
                    MarkDirtyLight();
                });
                row3.Add(rangeField);
                var hint = new Label("0=用 BattleMetrics 默认（速度8格/s·直径0.42·射程24）");
                hint.style.fontSize = 10;
                hint.style.color = new Color(0.55f, 0.58f, 0.6f);
                hint.style.alignSelf = Align.Center;
                hint.style.marginLeft = 8;
                row3.Add(hint);
                panel.Add(row3);
            }

            // 表现轨 cueName
            if (track == SkillTrackType.Action || track == SkillTrackType.Vfx || track == SkillTrackType.Sfx)
            {
                var row4 = new VisualElement();
                row4.style.flexDirection = FlexDirection.Row;
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
                row4.Add(cueHint);
                panel.Add(row4);
            }
        }

        private FloatField FloatFieldOf(string label, float value, Action<float> apply)
        {
            var field = new FloatField(label) { value = value };
            field.style.width = 130;
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

        // ==================== 总刷新 ====================

        private void RefreshAll()
        {
            RefreshHeader();
            RebuildTimeline();
            RefreshClipPanel();
            RefreshValidation();
            if (_statusLabel != null)
                _statusLabel.text = StatusText();
        }
    }
}
