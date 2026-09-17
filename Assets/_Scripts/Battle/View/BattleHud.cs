using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using GIC.Framework;
using GIC.Data;
using GIC.Tool;
using GIC.UI;

namespace GIC.Battle
{
    /// <summary>
    /// 正式战斗 HUD（B6 提前启动；docs/18 决策六 + docs/active/22 §13 + docs/designs/battle-hud-v1.html）。
    /// 布局 = MOBA 范式：移动左下（圆心距底 500）、爆发右下盘心 ×1.2、战技/延奏围绕；
    /// 单位选择交互（2026-09-18 拍板）：点立牌选中 → 技能盘现+手牌藏（选中态/手牌态互斥），
    /// 点空白取消选中；技能瞄准 = 可选格高亮 + 右上取消按钮。
    /// 交互状态机：Idle（手牌态）→ UnitSelected（行动态）→ Aiming（瞄准态）。
    /// 输入 = BattleCameraController.OnBoardTap（Drag 短点击复合发射，docs/24 §7.10 tap+pan 同体）。
    /// 拖动式瞄准/手牌卡列表/协议核心血条 = B4/B8 接线；技能图标为 4 角色临时硬表，B4 SkillConfig 落地后换数据驱动。
    /// </summary>
    public class BattleHud : MonoBehaviour
    {
        // ==================== 可调参数（编辑器直改） ====================

        [Header("配色")]
        [SerializeField] private Color 文字米白 = new Color(0.93f, 0.89f, 0.82f);
        [SerializeField] private Color 暖金 = new Color(0.83f, 0.74f, 0.56f);
        [SerializeField] private Color 玻璃底 = new Color(0.086f, 0.075f, 0.059f, 0.78f);
        [SerializeField] private Color 敌红 = new Color(0.78f, 0.36f, 0.31f);
        [SerializeField] private Color 高亮金 = new Color(0.83f, 0.74f, 0.56f, 0.55f);

        [Header("技能盘尺寸（2560×1440 基准）")]
        [SerializeField] private float 技能按钮直径 = 220f;
        [SerializeField] private float 爆发倍率 = 1.2f;
        [SerializeField] private float 围绕圆心距 = 300f;
        [SerializeField] private float 取消按钮直径 = 196f;

        [Header("文字")]
        [SerializeField] private float 顶栏字号 = 34f;
        [SerializeField] private float 按钮名字号 = 30f;

        // 布局常量（设计稿 v1 定稿；结构调整改代码，参数微调走 Inspector）
        private const float 移动按钮距左 = 64f;
        private const float 移动按钮圆心距底 = 500f;
        private const float 爆发圆心距右 = 300f;
        private const float 爆发圆心距底 = 300f;
        private const float 取消按钮距右 = 56f;
        private const float 取消按钮距顶 = 212f;
        private const int 移动最大步数 = 3;

        // ==================== 运行引用 ====================

        private BattleSession _session;
        private BattleBoard _board;
        private BattleCameraController _camera;
        private Action _onCloseBattle;
        private string _myPlayerId;

        private Canvas _canvas;

        // 顶栏
        private TextMeshProUGUI _turnText;
        private TextMeshProUGUI _clockText;
        private TextMeshProUGUI _queueText;
        private TextMeshProUGUI _tipText;
        private TextMeshProUGUI _resourceText;

        // 行动区
        private RectTransform _moveButton;
        private RectTransform _skillZone;
        private RectTransform _handZone;
        private RectTransform _cancelButton;

        // 技能详情（现有体系复用：Resources/Prefabs/UI/Skill/SkillDetailPanel.prefab）
        private SkillDetailView _skillDetailView;

        // 技能盘按钮（现成 Skill.prefab/SkillIconView；视觉填充走 InitWithData 现有链）
        private SkillIconView _skillView;
        private SkillIconView _burstView;
        private SkillIconView _ensoView;
        private TextCombiner _skillNameText;
        private TextCombiner _burstNameText;
        private TextCombiner _ensoNameText;

        // 高亮（世界层）
        private Transform _highlightRoot;
        private readonly List<GameObject> _highlightQuads = new List<GameObject>();

        // 状态机
        private enum HudState { Idle, UnitSelected, Aiming }
        private enum AimMode { Move, Skill }
        private HudState _state = HudState.Idle;
        private AimMode _aimMode;
        private string _selectedUnitId;
        private string _popupButtonKey;
        private string _aimButtonKey;
        private readonly HashSet<BattleCell> _aimCells = new HashSet<BattleCell>();

        /// <summary>详情面板当前是否开着（以现有面板 activeSelf 为准——其自带 Update 也关面板，勿另存布尔失同步）</summary>
        private bool PopupOpen => _skillDetailView != null && _skillDetailView.skillDetailPanel != null
            && _skillDetailView.skillDetailPanel.activeSelf;

        // ==================== 绑定 ====================

        public void Bind(BattleSession session, BattleBoard board, BattleCameraController camera, Action onCloseBattle)
        {
            _session = session;
            _board = board;
            _camera = camera;
            _onCloseBattle = onCloseBattle;
            _myPlayerId = BattleDebugPlayerIds.P1; // 本地双开：真人 P1（B7 联机换本机玩家 id）

            BuildUi();

            _session.Player.SnapshotUpdated += OnSnapshotUpdated;
            _session.Flow.OnPhaseChanged += OnPhaseChanged;
            if (_camera != null)
                _camera.OnBoardTap += OnBoardTap;

            RefreshFromSnapshot(_session.Player.LatestSnapshot);
        }

        private void OnDestroy()
        {
            if (_session != null)
            {
                if (_session.Player != null) _session.Player.SnapshotUpdated -= OnSnapshotUpdated;
                if (_session.Flow != null) _session.Flow.OnPhaseChanged -= OnPhaseChanged;
            }
            if (_camera != null)
                _camera.OnBoardTap -= OnBoardTap;
        }

        // ==================== 数据回调 ====================

        private void OnSnapshotUpdated(BattleSnapshot snapshot) => RefreshFromSnapshot(snapshot);

        private void OnPhaseChanged(BattlePhase phase, int turn)
        {
            if (phase != BattlePhase.Selecting)
            {
                // 执行阶段：清瞄准/清选中回手牌态（执行阶段 HUD 变化 B6 表现侧再拍）
                ExitAiming();
                DeselectUnit();
            }
            RefreshFromSnapshot(_session.Player.LatestSnapshot);
        }

        private void RefreshFromSnapshot(BattleSnapshot snapshot)
        {
            if (snapshot == null || _turnText == null) return;

            // 顶栏：回合 + 阶段
            string phaseLabel = _session.Flow.Phase switch
            {
                BattlePhase.Selecting => "选择阶段",
                BattlePhase.Resolving => "执行阶段",
                BattlePhase.Finished => "战斗结束",
                _ => "待机",
            };
            _turnText.text = $"回合 {snapshot.turnNumber} · {phaseLabel}";

            // 战斗时钟（独立时钟，TurnFlowController）
            int minutes = _session.Flow.BattleTimeMinutes;
            _clockText.text = $"{minutes / 60:D2}:{minutes % 60:D2}";

            // 我方资源
            var res = snapshot.resources.FirstOrDefault(r => r.playerId == _myPlayerId);
            if (res != null)
                _resourceText.text = $"体力 {res.stamina}    摩拉 {res.mora}    手牌 {res.handCardCount}";

            // 执行预览（全部存活单位按攻速降序；含双方——攻速时间窗跨单位一致生效）
            var order = snapshot.units
                .Where(u => u.isCorpse == 0)
                .OrderByDescending(u => u.attackSpeed)
                .Take(6);
            _queueText.text = "执行预览  " + string.Join("  →  ", order.Select(u => $"{u.unitName} {u.attackSpeed}"));

            // 选中单位若已死亡（对局中不可能复苏），清选中
            if (!string.IsNullOrEmpty(_selectedUnitId))
            {
                var sel = snapshot.units.FirstOrDefault(u => u.unitId == _selectedUnitId);
                if (sel == null || sel.isCorpse != 0)
                {
                    ExitAiming();
                    DeselectUnit();
                }
            }
        }

        // ==================== 棋盘点击（拾取/瞄准） ====================

        private void OnBoardTap(Vector2 screenPos)
        {
            if (_session == null || _session.Flow.Phase != BattlePhase.Selecting) return;
            if (_camera == null || _board == null || _board.Map == null) return;
            if (!_camera.TryGetBoardPoint(screenPos, out Vector3 world)) return;

            var cell = _board.WorldToCell(world);
            bool inBounds = _board.Map.HasTile(cell.x, cell.y);
            var snapshot = _session.Player.LatestSnapshot;
            if (snapshot == null) return;

            var myUnit = FindUnitAt(snapshot, cell);
            var enemyUnit = FindUnitAt(snapshot, cell, enemiesOnly: true);

            switch (_state)
            {
                case HudState.Aiming:
                    if (inBounds && _aimCells.Contains(cell)) { SubmitAim(cell, enemyUnit); return; }
                    // 瞄准态点非可选格 = 退回选中态（不算"点空白取消选中"）
                    ExitAiming();
                    return;

                case HudState.UnitSelected:
                    if (PopupOpen) { ClosePopup(); return; }          // 情况②：面板开着点外部=收面板（选中保持）
                    if (myUnit != null) { SelectUnit(myUnit.unitId); return; } // 换选中
                    if (enemyUnit != null) return;                    // 点敌方：无操作
                    DeselectUnit();                                   // 点空白=取消选中
                    return;

                case HudState.Idle:
                    if (myUnit != null) SelectUnit(myUnit.unitId);
                    return;
            }
        }

        private UnitState FindUnitAt(BattleSnapshot snapshot, BattleCell cell, bool enemiesOnly = false)
        {
            foreach (var u in snapshot.units)
            {
                if (u.isCorpse != 0) continue;
                if (u.position.x != cell.x || u.position.y != cell.y) continue;
                bool mine = u.playerId == _myPlayerId;
                if (enemiesOnly && mine) continue;
                if (!enemiesOnly && !mine) continue;
                return u;
            }
            return null;
        }

        // ==================== 状态机 ====================

        private void SelectUnit(string unitId)
        {
            ExitAiming();
            _selectedUnitId = unitId;
            _state = HudState.UnitSelected;
            var snapshot = _session.Player.LatestSnapshot;
            var unit = snapshot?.units.FirstOrDefault(u => u.unitId == unitId);
            if (unit != null)
                RefreshSkillButtons(unit.unitName);
            _handZone.gameObject.SetActive(false);
            _moveButton.gameObject.SetActive(true);
            _skillZone.gameObject.SetActive(true);
            _tipText.text = "已选中单位 —— 选择行动（移动 / 技能）";
        }

        private void DeselectUnit()
        {
            _selectedUnitId = null;
            _state = HudState.Idle;
            ClosePopup();
            _moveButton.gameObject.SetActive(false);
            _skillZone.gameObject.SetActive(false);
            _handZone.gameObject.SetActive(true);
            _tipText.text = "点击我方立牌选中单位";
        }

        private void EnterAiming(AimMode mode, string skillButtonKey)
        {
            _aimMode = mode;
            _aimButtonKey = skillButtonKey;
            _state = HudState.Aiming;
            SetAimSelectRing(skillButtonKey, true);
            ClosePopup();
            ComputeAimCells();
            ShowAimHighlights();
            _cancelButton.gameObject.SetActive(true);
            _tipText.text = _aimMode == AimMode.Move
                ? "选择目标格（8 方向直线，至多 3 步）——点右上取消"
                : "选择目标单位所在格——点右上取消";
        }

        private void ExitAiming()
        {
            if (_state != HudState.Aiming) return;
            _state = HudState.UnitSelected;
            SetAimSelectRing(_aimButtonKey, false);
            _aimCells.Clear();
            ClearHighlights();
            _cancelButton.gameObject.SetActive(false);
            _tipText.text = "已选中单位 —— 选择行动（移动 / 技能）";
        }

        /// <summary>瞄准态视觉反馈：亮/灭对应技能按钮的选中环（prefab 自带 skillSelect）</summary>
        private void SetAimSelectRing(string buttonKey, bool on)
        {
            var view = buttonKey == "burst" ? _burstView
                : buttonKey == "enso" ? _ensoView
                : _skillView;
            if (view != null && view.skillSelect != null)
                view.skillSelect.gameObject.SetActive(on);
        }

        /// <summary>瞄准可选格：移动 = 8 方向直线 1..3 步（有地块格）；战技 = 敌方存活单位所在格</summary>
        private void ComputeAimCells()
        {
            _aimCells.Clear();
            var snapshot = _session.Player.LatestSnapshot;
            var sel = snapshot?.units.FirstOrDefault(u => u.unitId == _selectedUnitId);
            if (sel == null) return;

            if (_aimMode == AimMode.Skill)
            {
                foreach (var u in snapshot.units)
                {
                    if (u.isCorpse != 0 || u.playerId == _myPlayerId) continue;
                    if (_board.Map.HasTile(u.position.x, u.position.y))
                        _aimCells.Add(new BattleCell(u.position.x, u.position.y));
                }
                return;
            }

            // 移动：8 方向 × 1..3 步（客户端只做地块粗筛；体积/阻挡由 Host 结算兜底）
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                for (int step = 1; step <= 移动最大步数; step++)
                {
                    var c = new BattleCell(sel.position.x + dx * step, sel.position.y + dy * step);
                    if (!_board.Map.HasTile(c.x, c.y)) break;
                    _aimCells.Add(c);
                }
            }
        }

        private void SubmitAim(BattleCell cell, UnitState enemyAtCell)
        {
            var snapshot = _session.Player.LatestSnapshot;
            var sel = snapshot?.units.FirstOrDefault(u => u.unitId == _selectedUnitId);
            if (sel == null) return;

            var action = new ActionData
            {
                playerId = _myPlayerId,
                unitId = _selectedUnitId,
                actionType = _aimMode == AimMode.Move ? ActionType.Move : ActionType.Skill,
                skillIndex = _aimMode == AimMode.Move ? 0 : GetSelectedSkillIndex(_aimButtonKey),
                targetUnitId = enemyAtCell != null ? enemyAtCell.unitId : "",
                moveMagnitude = 1,
                direction = Direction2D.Up,
            };

            if (_aimMode == AimMode.Move)
            {
                int dx = cell.x - sel.position.x;
                int dy = cell.y - sel.position.y;
                action.direction = DeltaToDirection(dx, dy);
                action.moveMagnitude = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
            }

            _session.SubmitAction(action);
            GICLog.Info($"[BattleHud] {_myPlayerId} 上交：{action.actionType} by {sel.unitName}" +
                        (_aimMode == AimMode.Skill ? $" → {action.targetUnitId}" : $" {action.direction} ×{action.moveMagnitude}"));

            ExitAiming();
            DeselectUnit();
            _tipText.text = "已上交行动 —— 等待执行";
        }

        private static Direction2D DeltaToDirection(int dx, int dy)
        {
            if (dx > 0 && dy == 0) return Direction2D.Right;
            if (dx < 0 && dy == 0) return Direction2D.Left;
            if (dx == 0 && dy > 0) return Direction2D.Up;
            if (dx == 0 && dy < 0) return Direction2D.Down;
            if (dx > 0 && dy > 0) return Direction2D.UpRight;
            if (dx < 0 && dy > 0) return Direction2D.UpLeft;
            if (dx > 0 && dy < 0) return Direction2D.DownRight;
            return Direction2D.DownLeft;
        }

        // ==================== 技能按钮（点击式） ====================

        private void OnMoveButtonClicked()
        {
            if (_state == HudState.Aiming && _aimMode == AimMode.Move) { ExitAiming(); return; }
            if (_state != HudState.UnitSelected) return;
            EnterAiming(AimMode.Move, null);
        }

        /// <summary>点击式三情况：①面板开着再点=进瞄准 ②面板没开第一次点=开面板 ③拖动=B4 接线</summary>
        private void OnSkillButtonClicked(string buttonKey)
        {
            if (_state != HudState.UnitSelected) return;
            if (PopupOpen)
            {
                if (_popupButtonKey == buttonKey)
                    EnterAiming(AimMode.Skill, buttonKey);
                else
                {
                    ClosePopup();
                    ShowSkillPopup(buttonKey);
                }
                return;
            }
            ShowSkillPopup(buttonKey);
        }

        private void ShowSkillPopup(string buttonKey)
        {
            _popupButtonKey = buttonKey;
            if (_skillDetailView == null) return;

            // 走现有技能详情体系：UnitData.skills 的 SkillData → SkillDetailView（图标/类型/名称/描述/参数全本地化）
            var skillData = GetSelectedSkillData(buttonKey);
            var unitData = GetSelectedUnitData();
            if (skillData == null || unitData == null) return;

            _skillDetailView.skillDetailPanel.SetActive(true);
            _skillDetailView.InitWithData(skillData, unitData, null);
            _skillDetailView.OpenPanel();
        }

        private void ClosePopup()
        {
            if (_skillDetailView != null)
                _skillDetailView.ClosePanel();
        }

        private void OnCancelButtonClicked()
        {
            if (_state == HudState.Aiming) ExitAiming();
        }

        // ==================== 可选格高亮（世界层） ====================

        private void ShowAimHighlights()
        {
            ClearHighlights();
            if (_highlightRoot == null) return;
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");

            foreach (var cell in _aimCells)
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = $"AimHighlight_{cell.x}_{cell.y}";
                UnityEngine.Object.Destroy(quad.GetComponent<Collider>());
                quad.transform.SetParent(_highlightRoot, false);
                quad.transform.position = new Vector3(
                    _board.CellToWorld(cell).x,
                    _board.GetSurfaceHeight(cell) + 0.03f,
                    _board.CellToWorld(cell).z);
                quad.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                quad.transform.localScale = new Vector3(0.92f, 0.92f, 1f);
                var renderer = quad.GetComponent<MeshRenderer>();
                var material = new Material(shader);
                material.color = 高亮金;
                renderer.sharedMaterial = material;
                _highlightQuads.Add(quad);
            }
        }

        private void ClearHighlights()
        {
            foreach (var quad in _highlightQuads)
                if (quad != null) Destroy(quad);
            _highlightQuads.Clear();
        }

        // ==================== UI 构建 ====================

        private void BuildUi()
        {
            var canvasGo = new GameObject("BattleHudCanvas");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 40;
            canvasGo.AddComponent<GraphicRaycaster>();
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(2560f, 1440f);
            scaler.matchWidthOrHeight = 0.5f;

            // 高亮根（世界层，非 Canvas 子级）
            var highlightGo = new GameObject("AimHighlightRoot");
            highlightGo.transform.SetParent(transform, false);
            _highlightRoot = highlightGo.transform;

            BuildTopBar();
            BuildMoveButton();
            BuildSkillZone();
            BuildHandZone();
            BuildTipBar();
            BuildCancelButton();
            BuildSkillPopup();

            // 初始 = 手牌态
            _moveButton.gameObject.SetActive(false);
            _skillZone.gameObject.SetActive(false);
            _cancelButton.gameObject.SetActive(false);
            _tipText.text = "点击我方立牌选中单位";
        }

        private void BuildTopBar()
        {
            var bar = MakeRect("TopBar", _canvas.transform);
            bar.anchorMin = bar.anchorMax = new Vector2(0.5f, 1f);
            bar.pivot = new Vector2(0.5f, 1f);
            bar.anchoredPosition = Vector2.zero;
            bar.sizeDelta = new Vector2(1600f, 240f);

            _turnText = MakeText("TurnText", bar, 顶栏字号, 文字米白);
            SetRect(_turnText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(900f, 60f));

            _clockText = MakeText("ClockText", bar, 顶栏字号 * 0.6f, 暖金);
            SetRect(_clockText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -84f), new Vector2(900f, 46f));

            _queueText = MakeText("QueueText", bar, 顶栏字号 * 0.5f, 文字米白);
            SetRect(_queueText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -136f), new Vector2(1400f, 42f));

            _resourceText = MakeText("ResourceText", bar, 顶栏字号 * 0.55f, 文字米白);
            SetRect(_resourceText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(64f, -30f), new Vector2(600f, 50f));
            _resourceText.alignment = TextAlignmentOptions.Left;

            // 设置/退出（右上角，走 BattleExitConfirmDialog 确认）
            var settings = MakeIconButton("SettingsButton", bar, "UI/Other/Faction/mondstadt_frame",
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-56f, -56f), new Vector2(84f, 84f));
            settings.onClick.AddListener(() => _onCloseBattle?.Invoke());
        }

        private void BuildMoveButton()
        {
            float d = 技能按钮直径;
            _moveButton = MakeActionButton("MoveButton", "UI/Skills/walk", "移动", d);
            // 底部锚：距左 64，圆心距底 500（设计稿 v1 定稿）
            _moveButton.anchorMin = _moveButton.anchorMax = new Vector2(0f, 0f);
            _moveButton.pivot = new Vector2(0.5f, 0.5f);
            _moveButton.anchoredPosition = new Vector2(移动按钮距左 + d * 0.5f, 移动按钮圆心距底);
            _moveButton.GetComponent<Button>().onClick.AddListener(OnMoveButtonClicked);
        }

        private void BuildSkillZone()
        {
            // 盘心（爆发圆心）屏幕坐标：距右 300 / 距底 300
            _skillZone = MakeRect("SkillZone", _canvas.transform);
            _skillZone.anchorMin = _skillZone.anchorMax = new Vector2(1f, 0f);
            _skillZone.pivot = new Vector2(1f, 0f);
            _skillZone.anchoredPosition = Vector2.zero;
            _skillZone.sizeDelta = new Vector2(爆发圆心距右 + 400f, 900f);

            // 爆发圆心 in-zone 坐标（zone 右下角为原点，pivot 右下）
            var burstCenter = new Vector2(爆发圆心距右, 爆发圆心距底);

            // 技能位全用现成 Skill.prefab（SkillIconView：图标+元素色环+选中环+Toggle；子件锚点全拉伸，
            // sizeDelta 直接等比缩放整件；2026-09-18 用户拍板"skill 也有现成预制体"）
            var burst = MakeSkillIconButton("BurstSkillIcon", 技能按钮直径 * 爆发倍率);
            PlaceInZone(burst, _skillZone, burstCenter);
            WireSkillIconButton(burst, "burst", ref _burstView, ref _burstNameText);

            var skill = MakeSkillIconButton("NormalSkillIcon", 技能按钮直径);
            PlaceInZone(skill, _skillZone, burstCenter + new Vector2(-围绕圆心距, 0f));
            WireSkillIconButton(skill, "skill", ref _skillView, ref _skillNameText);

            var encore = MakeSkillIconButton("EnsoSkillIcon", 技能按钮直径);
            PlaceInZone(encore, _skillZone, burstCenter + new Vector2(0f, 围绕圆心距));
            WireSkillIconButton(encore, "enso", ref _ensoView, ref _ensoNameText);
            _ensoRect = encore;
        }

        private RectTransform _ensoRect;

        /// <summary>实例化现成 Skill.prefab（根 100×100）并等比缩放到目标直径（子件锚点全拉伸随动）</summary>
        private RectTransform MakeSkillIconButton(string name, float diameter)
        {
            var prefab = Resources.Load<GameObject>("Prefabs/UI/Skill/Skill");
            if (prefab == null)
            {
                GICLog.Error("[BattleHud] Skill.prefab 未找到，技能盘不可用");
                return null;
            }
            var instance = Instantiate(prefab, _canvas.transform, false);
            instance.name = name;
            var rect = instance.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(diameter, diameter);
            return rect;
        }

        /// <summary>接线：SkillIconView 视觉填充（InitWithData 现有链）+ Button 驱动点击式状态机 + 底部名称</summary>
        private void WireSkillIconButton(RectTransform buttonRect, string buttonKey,
            ref SkillIconView viewRef, ref TextCombiner nameRef)
        {
            var view = buttonRect.GetComponent<SkillIconView>();
            viewRef = view;

            // Toggle 自身交互停用（ViewType.OnlyDisplay 也屏蔽其详情跳转）——改由 Button 驱动本 HUD 状态机；
            // 选中环 skillSelect 由瞄准态显隐管理（瞄准中亮环=视觉反馈）
            view.toggle.enabled = false;
            var button = buttonRect.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.ColorTint;
            button.onClick.AddListener(() => OnSkillButtonClicked(buttonKey));

            // 底部名称（prefab 无名字文本；技能名本地化条目由 RefreshSkillButtons 填）
            var labelGo = new GameObject("Name");
            var labelRect = labelGo.AddComponent<RectTransform>();
            labelRect.SetParent(buttonRect, false);
            labelRect.anchorMin = labelRect.anchorMax = new Vector2(0.5f, 0f);
            labelRect.pivot = new Vector2(0.5f, 1f);
            labelRect.anchoredPosition = new Vector2(0f, -8f);
            labelRect.sizeDelta = new Vector2(260f, 按钮名字号 + 8f);
            var labelText = labelGo.AddComponent<TextMeshProUGUI>();
            labelText.text = buttonKey == "burst" ? "爆发" : buttonKey == "enso" ? "延奏" : "战技";
            labelText.fontSize = 按钮名字号;
            labelText.color = 文字米白;
            labelText.alignment = TextAlignmentOptions.Center;
            labelText.raycastTarget = false;
            nameRef = labelGo.AddComponent<TextCombiner>();
        }

        private static void PlaceInZone(RectTransform rect, Transform zone, Vector2 inZonePos)
        {
            rect.SetParent(zone, false);
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = inZonePos;
        }

        private void BuildHandZone()
        {
            _handZone = MakeRect("HandZone", _canvas.transform);
            _handZone.anchorMin = _handZone.anchorMax = new Vector2(0.5f, 0f);
            _handZone.pivot = new Vector2(0.5f, 0f);
            _handZone.anchoredPosition = new Vector2(0f, 30f);
            _handZone.sizeDelta = new Vector2(900f, 120f);

            var handText = MakeText("HandText", _handZone, 顶栏字号 * 0.45f, 文字米白);
            SetRect(handText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(880f, 44f));
            handText.text = "手牌（卡列表 B8 接入 · 数量见左上资源区）";
        }

        /// <summary>全局提示条（独立于手牌区——选中态手牌隐藏时提示仍可见）</summary>
        private void BuildTipBar()
        {
            var rect = MakeRect("TipBar", _canvas.transform);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 168f);
            rect.sizeDelta = new Vector2(1000f, 52f);

            _tipText = MakeText("TipText", rect, 顶栏字号 * 0.5f, 暖金);
            SetRect(_tipText.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        }

        private void BuildCancelButton()
        {
            float d = 取消按钮直径;
            var cancel = MakeRect("CancelButton", _canvas.transform);
            cancel.anchorMin = cancel.anchorMax = new Vector2(1f, 1f);
            cancel.pivot = new Vector2(0.5f, 0.5f);
            cancel.anchoredPosition = new Vector2(-取消按钮距右 - d * 0.5f, -取消按钮距顶 - d * 0.5f);
            cancel.sizeDelta = new Vector2(d, d);

            var image = cancel.gameObject.AddComponent<Image>();
            image.sprite = Resources.Load<Sprite>("UI/Skills/circle");
            image.color = new Color(敌红.r, 敌红.g, 敌红.b, 0.35f);

            var label = MakeText("CancelText", cancel, 取消按钮直径 * 0.16f, 文字米白);
            SetRect(label.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            label.text = "取消";

            var button = cancel.gameObject.AddComponent<Button>();
            button.onClick.AddListener(OnCancelButtonClicked);
            _cancelButton = cancel;
        }

        private void BuildSkillPopup()
        {
            // 复用现有技能详情面板（Resources/Prefabs/UI/Skill/SkillDetailPanel.prefab——背包/卡牌详情同款，
            // 图标/类型/名称/描述/参数行全本地化，自带滑入动画与点外关闭）
            var prefab = Resources.Load<GameObject>("Prefabs/UI/Skill/SkillDetailPanel");
            if (prefab == null)
            {
                GICLog.Warn("[BattleHud] SkillDetailPanel.prefab 未找到，技能详情不可用");
                return;
            }
            var popup = Instantiate(prefab, _canvas.transform, false);
            popup.name = "BattleSkillDetail";
            _skillDetailView = popup.GetComponent<SkillDetailView>();
            if (_skillDetailView == null)
            {
                GICLog.Warn("[BattleHud] SkillDetailPanel.prefab 根缺 SkillDetailView 组件");
                return;
            }

            // 面板初始隐藏，摆到技能盘左侧（prefab 原位是背包场景接线值，须重摆）
            _skillDetailView.skillDetailPanel.SetActive(false);
            if (_skillDetailView.relatedPanel != null)
                _skillDetailView.relatedPanel.SetActive(false);
            var rect = _skillDetailView.skillDetailPanel.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            var pos = new Vector2(-爆发圆心距右 - 400f, 0f);
            rect.anchoredPosition = pos;
            _skillDetailView.RepositionPanel(pos);

            if (_skillDetailView.relatedPanel != null)
            {
                var relatedRect = _skillDetailView.relatedPanel.GetComponent<RectTransform>();
                if (relatedRect != null)
                {
                    relatedRect.anchorMin = relatedRect.anchorMax = new Vector2(1f, 0.5f);
                    relatedRect.pivot = new Vector2(1f, 0.5f);
                }
            }
        }

        // ==================== UI 基础件 ====================

        private static RectTransform MakeRect(string name, Transform parent)
        {
            var go = new GameObject(name);
            var rect = go.AddComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private TextMeshProUGUI MakeText(string name, Transform parent, float fontSize, Color color)
        {
            var go = new GameObject(name);
            var rect = go.AddComponent<RectTransform>();
            rect.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>圆形行动按钮（底盘 circle + 图标 + 底部名称）</summary>
        private RectTransform MakeActionButton(string name, string iconPath, string label, float diameter)
        {
            var rect = MakeRect(name, _canvas.transform);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(diameter, diameter);

            var image = rect.gameObject.AddComponent<Image>();
            var circle = Resources.Load<Sprite>("UI/Skills/circle");
            image.sprite = circle;
            image.color = new Color(0.10f, 0.09f, 0.07f, 0.92f);
            image.raycastTarget = true;

            var button = rect.gameObject.AddComponent<Button>();
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.06f, 1.03f, 0.92f);
            colors.pressedColor = new Color(0.85f, 0.83f, 0.75f);
            button.colors = colors;

            // 图标位（iconPath null 时也建空位——RefreshSkillButtons 按选中单位的 SkillData.icon 填）
            {
                var iconGo = new GameObject("Icon");
                var iconRect = iconGo.AddComponent<RectTransform>();
                iconRect.SetParent(rect, false);
                iconRect.anchorMin = iconRect.anchorMax = new Vector2(0.5f, 0.5f);
                iconRect.anchoredPosition = Vector2.zero;
                iconRect.sizeDelta = new Vector2(diameter * 0.56f, diameter * 0.56f);
                var icon = iconGo.AddComponent<Image>();
                if (iconPath != null) icon.sprite = Resources.Load<Sprite>(iconPath);
                icon.raycastTarget = false;
            }

            var labelGo = new GameObject("Label");
            var labelRect = labelGo.AddComponent<RectTransform>();
            labelRect.SetParent(rect, false);
            labelRect.anchorMin = labelRect.anchorMax = new Vector2(0.5f, 0f);
            labelRect.anchoredPosition = new Vector2(0f, -10f);
            labelRect.sizeDelta = new Vector2(240f, 按钮名字号 + 8f);
            var labelText = labelGo.AddComponent<TextMeshProUGUI>();
            labelText.text = label;
            labelText.fontSize = 按钮名字号;
            labelText.color = 文字米白;
            labelText.alignment = TextAlignmentOptions.Center;
            labelText.raycastTarget = false;
            labelGo.AddComponent<TextCombiner>(); // 名称在 RefreshSkillButtons 换为技能名本地化条目

            return rect;
        }

        private Button MakeIconButton(string name, Transform parent, string iconPath,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size)
        {
            var rect = MakeRect(name, parent);
            SetRect(rect, anchorMin, anchorMax, position, size);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = Resources.Load<Sprite>(iconPath);
            var button = rect.gameObject.AddComponent<Button>();
            return button;
        }

        // ==================== 技能数据链（现有体系：UnitConfig.skills → SkillData；2026-09-18 复用拍板） ====================

        private UnitConfig _unitConfig;

        /// <summary>选中角色的配置数据（头像/技能表/元素全在；BattlePlayer 同款加载）</summary>
        private UnitConfig.UnitData GetSelectedUnitData()
        {
            if (_unitConfig == null)
                _unitConfig = Resources.Load<UnitConfig>("Configs/UnitConfig");
            var snapshot = _session.Player.LatestSnapshot;
            var unit = snapshot?.units.FirstOrDefault(u => u.unitId == _selectedUnitId);
            if (unit == null) return null;
            return Enum.TryParse(unit.unitName, out UnitName name) && _unitConfig != null && _unitConfig.TryGetUnitData(name, out var data)
                ? data : null;
        }

        /// <summary>按钮键 → 该技能在 UnitData.skills 数组的索引（ActionData.skillIndex 的 Host 侧语义）</summary>
        private int GetSelectedSkillIndex(string buttonKey)
        {
            var unitData = GetSelectedUnitData();
            if (unitData?.skills == null) return 0;
            SkillType want = buttonKey switch
            {
                "burst" => SkillType.Burst,
                "enso" => SkillType.Enso,
                _ => SkillType.Normal,
            };
            for (int i = 0; i < unitData.skills.Length; i++)
                if (unitData.skills[i].skillType == want) return i;
            return 0;
        }

        /// <summary>按钮键（skill/burst/enso）→ 该角色的 SkillData（UnitData.skills 按 SkillType 分拣）</summary>
        private SkillConfig.SkillData GetSelectedSkillData(string buttonKey)
        {
            var unitData = GetSelectedUnitData();
            if (unitData?.skills == null) return null;
            SkillType want = buttonKey switch
            {
                "burst" => SkillType.Burst,
                "enso" => SkillType.Enso,
                _ => SkillType.Normal,
            };
            return unitData.skills.FirstOrDefault(s => s.skillType == want) ?? unitData.skills.FirstOrDefault();
        }

        /// <summary>选中单位时刷新技能盘：图标/元素色环/主动被动色全走 SkillIconView.InitWithData 现有链</summary>
        private void RefreshSkillButtons(string unitName)
        {
            var unitData = GetSelectedUnitData();
            if (unitData == null) return;

            var normal = GetSelectedSkillData("skill");
            var burst = GetSelectedSkillData("burst");
            var enso = GetSelectedSkillData("enso");

            ApplySkillButton(_skillView, normal, _skillNameText, unitData);
            ApplySkillButton(_burstView, burst, _burstNameText, unitData);
            ApplySkillButton(_ensoView, enso, _ensoNameText, unitData);
            if (_ensoRect != null)
                _ensoRect.GetComponent<Button>().interactable = enso != null; // 无延奏配置的角色置灰
        }

        private void ApplySkillButton(SkillIconView view, SkillConfig.SkillData data, TextCombiner label, UnitConfig.UnitData unitData)
        {
            if (view == null) return;
            view.gameObject.SetActive(data != null);
            if (data == null) return;

            // 现有链：图标白底不染 + 底图染亮元素色 + 主动/被动色环（2026-09-10 拍板规则全在 SkillIconView 内）
            view.InitWithData(data, unitData, ViewType.OnlyDisplay, _skillDetailView);

            if (label != null)
            {
                label.ClearAllEntries();
                label.AddEntry(data.skillID.GetEntry());
            }
        }
    }
}
