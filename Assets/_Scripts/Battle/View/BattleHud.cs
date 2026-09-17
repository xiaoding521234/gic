using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using GIC.Framework;
using GIC.Data;
using GIC.Tool;

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
        private RectTransform _skillPopup;
        private TextMeshProUGUI _popupText;

        // 高亮（世界层）
        private Transform _highlightRoot;
        private readonly List<GameObject> _highlightQuads = new List<GameObject>();

        // 状态机
        private enum HudState { Idle, UnitSelected, Aiming }
        private enum AimMode { Move, Skill }
        private HudState _state = HudState.Idle;
        private AimMode _aimMode;
        private string _selectedUnitId;
        private readonly HashSet<BattleCell> _aimCells = new HashSet<BattleCell>();
        private bool _popupOpen;
        private string _popupButtonKey;

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
                    if (_popupOpen) { ClosePopup(); return; }        // 情况②：面板开着点外部=收面板（选中保持）
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
                RefreshSkillIcons(unit.unitName);
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
            _state = HudState.Aiming;
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
            _aimCells.Clear();
            ClearHighlights();
            _cancelButton.gameObject.SetActive(false);
            _tipText.text = "已选中单位 —— 选择行动（移动 / 技能）";
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
                skillIndex = 0,
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
            if (_popupOpen)
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
            bool burst = buttonKey == "burst";
            var name = burst ? "元素爆发" : "元素战技";
            _popupText.text =
                $"<size=40>{name}</size>\n" +
                "<size=28>伤害 —（技能数据 B4 接入）</size>\n" +
                "<size=28>目标：单个敌方单位</size>\n" +
                "<size=28>消耗：体力 10</size>\n\n" +
                "<size=26>再点本按钮 → 进入瞄准 · 点棋盘空白 → 收起</size>";
            _skillPopup.gameObject.SetActive(true);
            _popupOpen = true;
        }

        private void ClosePopup()
        {
            if (!_popupOpen) return;
            _popupOpen = false;
            if (_skillPopup != null)
                _skillPopup.gameObject.SetActive(false);
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

            // 爆发（盘心，×1.2）
            var burst = MakeActionButton("BurstButton", null, "爆发", 技能按钮直径 * 爆发倍率);
            PlaceInZone(burst, _skillZone, burstCenter);
            burst.GetComponent<Button>().onClick.AddListener(() => OnSkillButtonClicked("burst"));
            _burstIcon = burst.GetComponent<Image>();

            // 战技（左弧位）
            var skill = MakeActionButton("SkillButton", null, "战技", 技能按钮直径);
            PlaceInZone(skill, _skillZone, burstCenter + new Vector2(-围绕圆心距, 0f));
            skill.GetComponent<Button>().onClick.AddListener(() => OnSkillButtonClicked("skill"));
            _skillIcon = skill.GetComponent<Image>();

            // 延奏（上弧位；B4 接入前禁用）
            var encore = MakeActionButton("EncoreButton", "UI/Other/Faction/mondstadt_windmill", "延奏", 技能按钮直径);
            PlaceInZone(encore, _skillZone, burstCenter + new Vector2(0f, 围绕圆心距));
            encore.GetComponent<Button>().interactable = false;
        }

        private static void PlaceInZone(RectTransform rect, Transform zone, Vector2 inZonePos)
        {
            rect.SetParent(zone, false);
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = inZonePos;
        }

        private Image _burstIcon;
        private Image _skillIcon;

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
            _skillPopup = MakeRect("SkillPopup", _canvas.transform);
            _skillPopup.anchorMin = _skillPopup.anchorMax = new Vector2(1f, 0f);
            _skillPopup.pivot = new Vector2(1f, 0.5f);
            _skillPopup.anchoredPosition = new Vector2(-70f, 640f);
            _skillPopup.sizeDelta = new Vector2(430f, 360f);

            var bg = _skillPopup.gameObject.AddComponent<Image>();
            bg.color = 玻璃底;

            _popupText = MakeText("PopupText", _skillPopup, 顶栏字号 * 0.5f, 文字米白);
            SetRect(_popupText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 0f), new Vector2(24f, -20f), new Vector2(-48f, 40f));
            _popupText.alignment = TextAlignmentOptions.TopLeft;

            _skillPopup.gameObject.SetActive(false);
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

            if (iconPath != null)
            {
                var iconGo = new GameObject("Icon");
                var iconRect = iconGo.AddComponent<RectTransform>();
                iconRect.SetParent(rect, false);
                iconRect.anchorMin = iconRect.anchorMax = new Vector2(0.5f, 0.5f);
                iconRect.anchoredPosition = Vector2.zero;
                iconRect.sizeDelta = new Vector2(diameter * 0.56f, diameter * 0.56f);
                var icon = iconGo.AddComponent<Image>();
                icon.sprite = Resources.Load<Sprite>(iconPath);
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

        // ==================== 技能图标（4 角色临时硬表；B4 SkillConfig 落地后换数据驱动） ====================

        private static readonly Dictionary<UnitName, (string skill, string burst)> SkillIconTable =
            new Dictionary<UnitName, (string skill, string burst)>
            {
                { UnitName.Amber, ("UI/Skills/amber_double_shot", "UI/Skills/arrow_rain") },
                { UnitName.Kaeya, ("UI/Skills/kaeya_frostgnaw", "UI/Skills/kaeya_glacial_waltz") },
                { UnitName.Lisa, ("UI/Skills/lisa_violet_arc", "UI/Skills/lisa_lightning_rose") },
                { UnitName.Barbara, ("UI/Skills/barbara_water_serenade", "UI/Skills/barbara_shining_miracle") },
            };

        private static readonly (string skill, string burst) FallbackIcons = ("UI/Skills/sword_skill", "UI/Skills/badge");

        /// <summary>选中单位时刷新技能盘图标</summary>
        private void RefreshSkillIcons(string unitName)
        {
            var icons = FallbackIcons;
            if (Enum.TryParse(unitName, out UnitName name) && SkillIconTable.TryGetValue(name, out var mapped))
                icons = mapped;
            _skillIcon.sprite = Resources.Load<Sprite>(icons.skill);
            _burstIcon.sprite = Resources.Load<Sprite>(icons.burst);
        }
    }
}
