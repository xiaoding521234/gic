using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Localization;
using GIC.Framework;
using GIC.Data;
using GIC.Tool;

namespace GIC.Battle
{
    /// <summary>
    /// BattleHud 布局分件（2026-09-21 王者荣耀式自定义布局系统，C 案；2026-09-22 prefab 化收编）：
    /// - 槽寻址：布局槽=BattleHud.prefab 内画布下 Slots/{key}（槽内首子级=控件；加可拖件=prefab 加槽节点+AllLayoutKeys 加一行）
    /// - LayoutSlot：归一锚点 (u,v)、anchoredPosition=0、pivot 中心——拖拽=改锚、缩放=slot.localScale；
    ///   归一锚点跨视口比例不漂（比例锚点方案，docs/17 §7）；默认布局=槽上编辑器摆的锚点（解析时捕获）
    /// - 编辑模式：_layoutEditing 门控全交互；倒计时冻结（TurnFlowController.SelectTimerPaused）；
    ///   拖拽/点选/缩放滑条 + 底部方案工具栏（恢复默认/方案一~三 保存+应用/完成；prefab 烘焙默认隐藏）
    /// - 持久化：SaveSettings.hudLayoutPresets（主存档 settings 分区，纯新增字段不升版）；
    ///   战斗启动按 activeHudLayout 应用激活方案
    /// 方案数据类=HudLayoutPreset/HudLayoutEntry（PlayerSaveData.cs）。
    /// </summary>
    public partial class BattleHud
    {
        // ==================== 布局注册表 ====================

        /// <summary>可拖件定义（注册表行；默认布局与方案条目同构）</summary>
        private class LayoutWidgetDef
        {
            public string key;
            public RectTransform slot;     // 画布级包裹器（归一锚点承载位置）
            public RectTransform content; // 原 HUD 件（slot 内全拉伸）
            public GameObject plate;      // 拖拽板（透明 Image 承射线；无图件也可拖；仅编辑态激活）
            public GameObject frame;      // 选中金框（4 边条）
            public Vector2 defaultUV;     // 默认布局归一锚点（2560×1440 参考系换算）
        }

        private readonly List<LayoutWidgetDef> _layoutWidgets = new List<LayoutWidgetDef>();
        private readonly Dictionary<string, LayoutWidgetDef> _layoutByKey = new Dictionary<string, LayoutWidgetDef>();
        private RectTransform _slotsRoot; // 画布下 Slots/ 容器（prefab 结构契约：槽节点名=key、槽内首子级=控件）

        /// <summary>技能盘三键（显隐成组控制的 key 集）</summary>
        private static readonly string[] SkillDiscKeys = { "burst", "skill", "enso" };

        private Vector2 _refSize = new Vector2(2560f, 1440f); // 参考分辨率（BuildUi 从 CanvasScaler 读）

        // 编辑模式
        private bool _layoutEditing;
        private LayoutWidgetDef _selectedWidget;
        private RectTransform _editToolbar;
        private Slider _scaleSlider;
        private readonly List<TMP_Text> _slotNameTexts = new List<TMP_Text>(); // 3 槽名（激活态高亮刷新）

        [Autowired] private SaveManager _saveManager;

        private const float LayoutUVMargin = 0.02f;    // 拖拽夹边（归一坐标安全边距）
        private const float LayoutScaleMin = 0.6f;
        private const float LayoutScaleMax = 1.6f;
        private const int LayoutSlotCount = 3;          // 固定三方案槽（拍板：命名后置）

        internal bool IsLayoutEditing => _layoutEditing;

        /// <summary>布局槽全键（prefab 结构契约；加可拖件=BattleHud.prefab 加槽节点+此处登记一行）</summary>
        private static readonly string[] AllLayoutKeys =
        {
            "burst", "skill", "enso", "move", "cancel", "settings",
            "turn", "countdown", "clock", "queue", "myinfo", "enemyinfo", "hand", "tip",
        };

        /// <summary>
        /// 布局槽寻址（2026-09-22 prefab 化：槽=BattleHud.prefab 内画布下 Slots/{key}，控件=槽内首子级）。
        /// 默认布局=槽上编辑器摆的归一锚点（解析时捕获为 defaultUV）；拖拽板/金框已烘焙（缺=Warn）；
        /// 拖拽处理器每实例必挂（迁移剥离过场景引用，UnityEvent/委托不序列化无残留）。
        /// </summary>
        private void ResolveLayoutSlots()
        {
            _slotsRoot = _canvas.transform.Find("Slots") as RectTransform;
            if (_slotsRoot == null)
            {
                GICLog.Error("[BattleHud] prefab 缺 Slots 容器，布局系统不可用");
                return;
            }

            foreach (var key in AllLayoutKeys)
            {
                var slot = _slotsRoot.Find(key) as RectTransform;
                if (slot == null)
                {
                    GICLog.Warn($"[BattleHud] 布局槽缺失：{key}");
                    continue;
                }
                if (slot.childCount < 1)
                {
                    GICLog.Warn($"[BattleHud] 布局槽 {key} 无控件（首子级应为控件本体）");
                    continue;
                }

                var def = new LayoutWidgetDef
                {
                    key = key,
                    slot = slot,
                    content = slot.GetChild(0) as RectTransform,
                    plate = slot.Find("DragPlate")?.gameObject,
                    frame = slot.Find("SelectFrame")?.gameObject,
                    defaultUV = slot.anchorMin, // 编辑器默认位（prefab 摆放即默认布局）
                };
                if (def.plate == null) GICLog.Warn($"[BattleHud] 槽 {key} 缺拖拽板（无图件将不可拖）");
                var handler = slot.gameObject.AddComponent<LayoutDragHandler>();
                handler.Init(this, def);

                _layoutWidgets.Add(def);
                _layoutByKey[key] = def;
            }
        }

        // ==================== 布局应用/采集 ====================

        /// <summary>恢复内置默认布局（所有件回 defaultUV、scale=1）</summary>
        private void ApplyDefaultLayout()
        {
            foreach (var def in _layoutWidgets)
            {
                def.slot.anchorMin = def.slot.anchorMax = def.defaultUV;
                def.slot.anchoredPosition = Vector2.zero;
                def.slot.localScale = Vector3.one;
            }
        }

        /// <summary>应用方案条目（先回默认再覆盖——条目缺 key 保持默认；uv/scale 越界夹取）</summary>
        private void ApplyEntries(List<HudLayoutEntry> entries)
        {
            ApplyDefaultLayout();
            if (entries == null) return;
            foreach (var entry in entries)
            {
                if (entry == null || !_layoutByKey.TryGetValue(entry.key, out var def)) continue;
                def.slot.anchorMin = def.slot.anchorMax = new Vector2(
                    Mathf.Clamp(entry.u, LayoutUVMargin, 1f - LayoutUVMargin),
                    Mathf.Clamp(entry.v, LayoutUVMargin, 1f - LayoutUVMargin));
                def.slot.anchoredPosition = Vector2.zero;
                def.slot.localScale = Vector3.one * Mathf.Clamp(entry.scale, LayoutScaleMin, LayoutScaleMax);
            }
        }

        /// <summary>采集当前布局为方案条目（全注册件快照）</summary>
        private List<HudLayoutEntry> CaptureLayout()
        {
            var entries = new List<HudLayoutEntry>();
            foreach (var def in _layoutWidgets)
                entries.Add(new HudLayoutEntry
                {
                    key = def.key,
                    u = def.slot.anchorMin.x,
                    v = def.slot.anchorMin.y,
                    scale = def.slot.localScale.x,
                });
            return entries;
        }

        /// <summary>战斗启动按存档激活方案应用（-1/空槽=默认）</summary>
        private void ApplySavedLayoutOnStart()
        {
            var settings = _saveManager != null ? _saveManager.CurrentSave.settings : null;
            if (settings == null) return;
            int idx = settings.activeHudLayout;
            var preset = idx >= 0 && idx < settings.hudLayoutPresets.Count ? settings.hudLayoutPresets[idx] : null;
            if (preset != null && preset.entries.Count > 0)
                ApplyEntries(preset.entries);
            else
                ApplyDefaultLayout();
        }

        // ==================== 显隐（编辑态感知） ====================

        /// <summary>件显隐统一口（编辑态强制全显——任意件可拖）</summary>
        private void SetLayoutWidgetActive(string key, bool visible)
        {
            if (!_layoutByKey.TryGetValue(key, out var def)) return;
            if (_layoutEditing) visible = true;
            def.content.gameObject.SetActive(visible);
        }

        /// <summary>按状态机重排件显隐（取代旧 zone 级 SetActive 散写；SelectUnit/DeselectUnit/瞄准进出共用）</summary>
        private void ApplyStateVisibility()
        {
            bool acting = _state == HudState.UnitSelected || _state == HudState.Aiming;
            foreach (var key in SkillDiscKeys)
                SetLayoutWidgetActive(key, acting);
            SetLayoutWidgetActive("move", acting);
            SetLayoutWidgetActive("hand", !acting);
            SetLayoutWidgetActive("cancel", _state == HudState.Aiming);
        }

        // ==================== 编辑模式 ====================

        private void ToggleLayoutEdit()
        {
            if (_layoutEditing) ExitLayoutEdit();
            else EnterLayoutEdit();
        }

        private void EnterLayoutEdit()
        {
            if (_layoutEditing) return;
            _layoutEditing = true; // 先立旗：随后的显隐调用经编辑态强制全显
            ExitAiming();
            DeselectUnit(); // → Idle（编辑前清瞄准/选中）
            ClosePopup();
            if (_session != null && _session.Flow != null)
                _session.Flow.SelectTimerPaused = true; // 编辑期冻结选择倒计时（拍板 D1）

            foreach (var def in _layoutWidgets)
                def.plate.SetActive(true);
            if (_editToolbar != null) _editToolbar.gameObject.SetActive(true); // prefab 烘焙工具栏，编辑态激活
            SelectWidget(null);
            SetTip("Battle_LayoutHint");
        }

        private void ExitLayoutEdit()
        {
            if (!_layoutEditing) return;
            _layoutEditing = false;
            if (_session != null && _session.Flow != null)
                _session.Flow.SelectTimerPaused = false;
            if (_editToolbar != null) _editToolbar.gameObject.SetActive(false); // 隐藏回烘焙态（引用保留，重进即用）
            foreach (var def in _layoutWidgets)
            {
                def.plate.SetActive(false);
                def.frame.SetActive(false);
            }
            SelectWidget(null);
            ApplyStateVisibility();
            SetTip("Battle_TipSelect");
        }

        /// <summary>编辑态点选件（选中→金框+滑条绑定；点选经由拖拽板/控件冒泡到 slot 处理器）</summary>
        private void SelectWidget(LayoutWidgetDef def)
        {
            if (_selectedWidget != null && _selectedWidget.frame != null)
                _selectedWidget.frame.SetActive(false);
            _selectedWidget = def;
            if (def != null)
            {
                if (def.frame != null) def.frame.SetActive(true);
                if (_scaleSlider != null)
                {
                    _scaleSlider.interactable = true;
                    _scaleSlider.SetValueWithoutNotify(def.slot.localScale.x);
                }
            }
            else if (_scaleSlider != null)
            {
                _scaleSlider.interactable = false;
            }
        }

        /// <summary>拖拽中：屏幕像素增量 → 画布单位偏移（overlay 画布除 scaleFactor），实时夹边</summary>
        private void OnLayoutWidgetDrag(LayoutWidgetDef def, Vector2 screenDelta)
        {
            float sf = _canvas != null ? _canvas.scaleFactor : 1f;
            if (sf <= 0f) return;
            def.slot.anchoredPosition += screenDelta / sf;
            ClampSlotToCanvas(def);
        }

        /// <summary>拖拽结束：偏移折算进归一锚点（anchoredPosition 归零）</summary>
        private void OnLayoutWidgetDragEnd(LayoutWidgetDef def)
        {
            var canvasRect = _canvas.GetComponent<RectTransform>().rect;
            float w = Mathf.Max(1f, canvasRect.width);
            float h = Mathf.Max(1f, canvasRect.height);
            Vector2 uv = def.slot.anchorMin;
            Vector2 ap = def.slot.anchoredPosition;
            float u = Mathf.Clamp(uv.x + ap.x / w, LayoutUVMargin, 1f - LayoutUVMargin);
            float v = Mathf.Clamp(uv.y + ap.y / h, LayoutUVMargin, 1f - LayoutUVMargin);
            def.slot.anchorMin = def.slot.anchorMax = new Vector2(u, v);
            def.slot.anchoredPosition = Vector2.zero;
        }

        /// <summary>拖拽实时夹边（件中心保持在画布 2%~98% 内）</summary>
        private void ClampSlotToCanvas(LayoutWidgetDef def)
        {
            var canvasRect = _canvas.GetComponent<RectTransform>().rect;
            float w = Mathf.Max(1f, canvasRect.width);
            float h = Mathf.Max(1f, canvasRect.height);
            Vector2 uv = def.slot.anchorMin;
            Vector2 ap = def.slot.anchoredPosition;
            Vector2 center = new Vector2((uv.x - 0.5f) * w + ap.x, (uv.y - 0.5f) * h + ap.y);
            float mx = w * (0.5f - LayoutUVMargin);
            float my = h * (0.5f - LayoutUVMargin);
            def.slot.anchoredPosition = new Vector2(
                ap.x + Mathf.Clamp(center.x, -mx, mx) - center.x,
                ap.y + Mathf.Clamp(center.y, -my, my) - center.y);
        }

        // ==================== 方案存取（主存档 settings 分区） ====================

        /// <summary>保存当前布局到方案槽（同时激活该槽；Modify 延迟合并落盘）</summary>
        private void SaveLayoutToSlot(int slotIndex)
        {
            var entries = CaptureLayout();
            _saveManager?.Modify(s =>
            {
                var presets = s.settings.hudLayoutPresets;
                while (presets.Count <= slotIndex)
                    presets.Add(new HudLayoutPreset());
                presets[slotIndex].entries = entries;
                s.settings.activeHudLayout = slotIndex;
            });
            RefreshSlotNameColors();
        }

        /// <summary>应用方案槽（空槽=恢复默认并回落 -1）</summary>
        private void ApplyLayoutSlot(int slotIndex)
        {
            var settings = _saveManager != null ? _saveManager.CurrentSave.settings : null;
            var preset = settings != null && slotIndex < settings.hudLayoutPresets.Count
                ? settings.hudLayoutPresets[slotIndex] : null;
            if (preset != null && preset.entries.Count > 0)
            {
                ApplyEntries(preset.entries);
                _saveManager?.Modify(s => s.settings.activeHudLayout = slotIndex);
            }
            else
            {
                ApplyDefaultLayout();
                _saveManager?.Modify(s => s.settings.activeHudLayout = -1);
            }
            SelectWidget(null);
            RefreshSlotNameColors();
        }

        /// <summary>恢复默认布局（激活方案回落 -1）</summary>
        private void ResetLayoutDefault()
        {
            ApplyDefaultLayout();
            SelectWidget(null);
            _saveManager?.Modify(s => s.settings.activeHudLayout = -1);
            RefreshSlotNameColors();
        }

        /// <summary>方案名高亮：激活槽金色 / 其余米白</summary>
        private void RefreshSlotNameColors()
        {
            var settings = _saveManager != null ? _saveManager.CurrentSave.settings : null;
            int active = settings != null ? settings.activeHudLayout : -1;
            for (int i = 0; i < _slotNameTexts.Count; i++)
                _slotNameTexts[i].color = i == active ? Palette.高亮金 : Palette.文字米白;
        }

        // ==================== 编辑工具栏与入口钮（prefab 烘焙，运行时寻址接线） ====================

        /// <summary>编辑工具栏接线（烘焙默认隐藏；按钮/滑条/槽名按契约名解析 + Palette 活色）。
        /// 单行 13 件=宽度表驱动的旧构建法迁移烘焙（杜绝重叠），改布局在 prefab 编辑器里做。</summary>
        private void ResolveLayoutToolbar()
        {
            _editToolbar = _canvas.transform.Find("LayoutEditToolbar") as RectTransform;
            if (_editToolbar == null)
            {
                GICLog.Warn("[BattleHud] prefab 缺编辑工具栏（LayoutEditToolbar）");
                return;
            }
            _editToolbar.gameObject.SetActive(false); // 烘焙隐藏态；EnterLayoutEdit 激活

            // Palette 活色（烘焙色仅兜底，改资产随下局生效）
            var bg = _editToolbar.GetComponent<Image>();
            if (bg != null) bg.color = Palette.按钮底盘;
            var line = _editToolbar.Find("GoldLine")?.GetComponent<Image>();
            if (line != null) line.color = Palette.暖金;
            RecolorText(_editToolbar.Find("ScaleLabel/ScaleLabelText")?.GetComponent<TextCombiner>(), Palette.文字米白);

            WireToolbarButton("ResetButton", ResetLayoutDefault);
            WireToolbarButton("DoneButton", ExitLayoutEdit);

            _slotNameTexts.Clear();
            for (int i = 0; i < LayoutSlotCount; i++)
            {
                int slot = i; // 闭包捕获
                WireToolbarButton($"Save{i}", () => SaveLayoutToSlot(slot));
                WireToolbarButton($"Apply{i}", () => ApplyLayoutSlot(slot));
                var nameText = _editToolbar.Find($"SlotName{i}/SlotName{i}Text")?.GetComponent<TMP_Text>();
                if (nameText != null) _slotNameTexts.Add(nameText);
            }

            _scaleSlider = _editToolbar.Find("ScaleSlider")?.GetComponent<Slider>();
            if (_scaleSlider != null)
            {
                _scaleSlider.interactable = false; // 未选件时不可交互（选中后 SelectWidget 打开）
                _scaleSlider.onValueChanged.AddListener(value =>
                {
                    if (_selectedWidget != null)
                        _selectedWidget.slot.localScale = Vector3.one * value;
                });
            }
            RefreshSlotNameColors();
        }

        /// <summary>工具栏按钮接线（按钮底盘活色 + Label 文本活色 + onClick）</summary>
        private void WireToolbarButton(string name, Action onClick)
        {
            var rect = _editToolbar.Find(name);
            if (rect == null) { GICLog.Warn($"[BattleHud] 工具栏缺按钮：{name}"); return; }
            var image = rect.GetComponent<Image>();
            if (image != null) image.color = Palette.按钮底盘;
            RecolorText(rect.Find(name + "Label")?.GetComponent<TextCombiner>(), Palette.文字米白);
            var button = rect.GetComponent<Button>();
            if (button != null) button.onClick.AddListener(() => onClick?.Invoke());
        }

        // ==================== 编辑入口钮 ====================

        /// <summary>布局编辑入口钮接线（HUD 常驻，设置钮左侧；拍板 C1——开一局即可直接调布局）</summary>
        private void ResolveLayoutEntryButton()
        {
            var rect = _canvas.transform.Find("LayoutEditButton");
            if (rect == null) { GICLog.Warn("[BattleHud] prefab 缺布局编辑入口钮"); return; }
            var image = rect.GetComponent<Image>();
            if (image != null) image.color = Palette.按钮底盘;
            RecolorText(rect.Find("Label")?.GetComponent<TextCombiner>(), Palette.文字米白);
            rect.GetComponent<Button>()?.onClick.AddListener(ToggleLayoutEdit);
        }

        /// <summary>拖拽/点选处理器（挂 slot；事件从控件/拖拽板冒泡；非编辑态全静默）</summary>
        private class LayoutDragHandler : MonoBehaviour,
            UnityEngine.EventSystems.IBeginDragHandler,
            UnityEngine.EventSystems.IDragHandler,
            UnityEngine.EventSystems.IEndDragHandler,
            UnityEngine.EventSystems.IPointerClickHandler
        {
            private BattleHud _owner;
            private LayoutWidgetDef _def;

            public void Init(BattleHud owner, LayoutWidgetDef def)
            {
                _owner = owner;
                _def = def;
            }

            public void OnBeginDrag(UnityEngine.EventSystems.PointerEventData eventData)
            {
                if (_owner == null || !_owner._layoutEditing) return;
                // 起点锚定：拖拽全程在 anchoredPosition 上累加，结束折算回锚点
            }

            public void OnDrag(UnityEngine.EventSystems.PointerEventData eventData)
            {
                if (_owner == null || !_owner._layoutEditing) return;
                _owner.OnLayoutWidgetDrag(_def, eventData.delta);
            }

            public void OnEndDrag(UnityEngine.EventSystems.PointerEventData eventData)
            {
                if (_owner == null || !_owner._layoutEditing) return;
                _owner.OnLayoutWidgetDragEnd(_def);
            }

            public void OnPointerClick(UnityEngine.EventSystems.PointerEventData eventData)
            {
                if (_owner == null || !_owner._layoutEditing) return;
                _owner.SelectWidget(_def);
            }
        }
    }
}
