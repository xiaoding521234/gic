using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.UI;
using GIC.Tool;
namespace GIC.Battle
{


    /// <summary>
    /// 战斗调试面板（B1 灰盒；程序化 UGUI + legacy Text——调试工具不入包，B5 换正式战斗 HUD）。
    /// 本地双开控制调试模式：P1/P2 两侧选择共用本面板，各自上交一个行动。
    /// </summary>
    public class BattleDebugPanel : MonoBehaviour
    {
        private BattleSession _session;
        private Action _onCloseBattle;
        private List<string> _manualPlayerIds = new List<string>();

        private Text _phaseText;
        private Text _statusText;

        // 每玩家选择状态
        private readonly Dictionary<string, PlayerSelection> _selections = new Dictionary<string, PlayerSelection>();

        private readonly string[] _directionLabels = { "右", "左", "上", "下", "右上", "左上", "右下", "左下" };
        private readonly Direction2D[] _directionValues =
        {
            Direction2D.Right, Direction2D.Left, Direction2D.Up, Direction2D.Down,
            Direction2D.UpRight, Direction2D.UpLeft, Direction2D.DownRight, Direction2D.DownLeft,
        };
        private readonly float[] _speedValues = { 1f, 2f, 4f };

        // ==================== 绑定与构建 ====================

        public void Bind(BattleSession session, Action onCloseBattle, List<string> manualPlayerIds = null)
        {
            _session = session;
            _onCloseBattle = onCloseBattle;

            // 手动操控的玩家（联机入口=真人列表；调试兜底=双开 P1+P2）
            _manualPlayerIds = manualPlayerIds != null && manualPlayerIds.Count > 0
                ? new List<string>(manualPlayerIds)
                : new List<string> { BattleDebugPlayerIds.P1, BattleDebugPlayerIds.P2 };

            _selections.Clear();
            foreach (var pid in _manualPlayerIds)
                _selections[pid] = new PlayerSelection();

            _session.Player.SnapshotUpdated += OnSnapshotUpdated;
            _session.Flow.OnPhaseChanged += OnPhaseChanged;

            BuildUi();
            RefreshFromSnapshot(_session.Player.LatestSnapshot);
        }

        private void OnDestroy()
        {
            if (_session != null)
            {
                if (_session.Player != null) _session.Player.SnapshotUpdated -= OnSnapshotUpdated;
                if (_session.Flow != null) _session.Flow.OnPhaseChanged -= OnPhaseChanged;
            }
        }

        private void OnSnapshotUpdated(BattleSnapshot snapshot) => RefreshFromSnapshot(snapshot);

        private void OnPhaseChanged(BattlePhase phase, int turn) => RefreshPhaseText();

        // ==================== UI 构建（程序化） ====================

        private const float PanelWidth = 500f;
        private const float RowHeight = 44f;

        private float _cursorY;

        private void BuildUi()
        {
            var canvasGo = new GameObject("BattleDebugCanvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            canvasGo.AddComponent<GraphicRaycaster>();
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(2560f, 1440f);
            scaler.matchWidthOrHeight = 0.5f;

            // 全屏输入拦截层（完全透明只吃射线）：防止点击穿透到底层界面（如 MainHall 按钮）
            var blockerGo = new GameObject("InputBlocker");
            var blockerRect = blockerGo.AddComponent<RectTransform>();
            blockerRect.SetParent(canvasGo.transform, false);
            blockerRect.anchorMin = Vector2.zero;
            blockerRect.anchorMax = Vector2.one;
            blockerRect.offsetMin = blockerRect.offsetMax = Vector2.zero;
            var blocker = blockerGo.AddComponent<Image>();
            blocker.color = new Color(0f, 0f, 0f, 0f);
            blocker.raycastTarget = true;

            var panelGo = new GameObject("DebugPanel");
            var panelRect = panelGo.AddComponent<RectTransform>();
            panelRect.SetParent(canvasGo.transform, false);
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.anchoredPosition = new Vector2(16f, -16f);
            panelRect.sizeDelta = new Vector2(PanelWidth, 1300f);
            var panelImage = panelGo.AddComponent<Image>();
            panelImage.color = new Color(0.08f, 0.09f, 0.12f, 0.82f);

            _cursorY = -12f;
            CreateLabel(panelRect, "战斗调试 B1（灰盒）", 30, new Color(1f, 0.85f, 0.4f));

            _phaseText = CreateLabel(panelRect, "回合 -", 24, Color.white);
            _statusText = CreateLabel(panelRect, "", 20, new Color(0.85f, 0.92f, 1f));
            ReserveSpace(150f);

            foreach (var playerId in _manualPlayerIds)
                BuildPlayerBlock(panelRect, playerId, "玩家 " + playerId);

            // 底部工具行
            NextRow(panelRect, out var toolsRow);
            CreateCycleButton(toolsRow, "速率", new[] { "×1", "×2", "×4" }, 0, index =>
            {
                _session.Player.SetPlaybackSpeed(_speedValues[index]);
            });
            CreateButton(toolsRow, "插入即时行动(战技)", 200f, () =>
            {
                if (_manualPlayerIds.Count == 0) return;
                var playerId = _manualPlayerIds[0];
                var selection = _selections[playerId];
                var action = BuildAction(playerId, selection);
                action.actionType = ActionType.Skill;
                _session.SubmitInstantAction(action);
                LogLine($"[调试] {playerId} 插入即时行动：{selection.UnitId} → {selection.TargetUnitId}");
            });
            CreateButton(toolsRow, "结束战斗", 120f, () => _onCloseBattle?.Invoke());
        }

        private void BuildPlayerBlock(RectTransform panelRect, string playerId, string title)
        {
            var selection = _selections[playerId];
            CreateLabel(panelRect, title, 26, new Color(0.75f, 0.85f, 1f));

            // 行 1：单位 / 行动
            NextRow(panelRect, out var row1);
            selection.UnitCycle = CreateCycleButton(row1, "单位", Array.Empty<string>(), -1, _ => RefreshUnitDependentButtons(playerId));
            selection.ActionCycle = CreateCycleButton(row1, "行动", new[] { "移动", "战技", "空过" }, 0, _ => RefreshUnitDependentButtons(playerId));

            // 行 2：方向 / 力度 / 目标
            NextRow(panelRect, out var row2);
            selection.DirectionCycle = CreateCycleButton(row2, "方向", _directionLabels, 0, null);
            selection.MagnitudeCycle = CreateCycleButton(row2, "力度", new[] { "1", "2", "3" }, 0, null);
            selection.TargetCycle = CreateCycleButton(row2, "目标", Array.Empty<string>(), -1, null);

            // 行 3：提交
            NextRow(panelRect, out var row3);
            CreateButton(row3, $"提交 {playerId}", 200f, () =>
            {
                var action = BuildAction(playerId, selection);
                _session.SubmitAction(action);
                LogLine($"[调试] {playerId} 上交：{action.actionType} by {action.unitId}");
            });

            ReserveSpace(12f);
        }

        private ActionData BuildAction(string playerId, PlayerSelection selection)
        {
            var action = new ActionData
            {
                playerId = playerId,
                unitId = selection.UnitCycle?.Current ?? "",
                actionType = (ActionType)Mathf.Max(0, selection.ActionCycle?.CurrentIndex ?? 0),
                direction = _directionValues[Mathf.Clamp(selection.DirectionCycle?.CurrentIndex ?? 0, 0, _directionValues.Length - 1)],
                moveMagnitude = Mathf.Max(0, selection.MagnitudeCycle?.CurrentIndex ?? 0) + 1,
                skillIndex = 0,
                targetUnitId = selection.TargetCycle?.Current ?? "",
            };
            return action;
        }

        // ==================== 刷新 ====================

        private void RefreshPhaseText()
        {
            if (_phaseText == null || _session == null) return;
            var phase = _session.Flow.Phase;
            string phaseLabel = phase switch
            {
                BattlePhase.Selecting => "选择阶段（收行动）",
                BattlePhase.Resolving => "执行阶段（片演算）",
                BattlePhase.Finished => "战斗结束",
                _ => "待机",
            };
            _phaseText.text = $"回合 {_session.Flow.TurnNumber} · {phaseLabel}";
        }

        private void RefreshFromSnapshot(BattleSnapshot snapshot)
        {
            if (snapshot == null || _phaseText == null) return;

            RefreshPhaseText();

            // 单位与目标下拉重建（保留原选择）
            foreach (var kv in _selections)
            {
                var playerId = kv.Key;
                var selection = kv.Value;

                var ownUnits = new List<string>();
                var enemyUnits = new List<string>();
                var displayNames = new Dictionary<string, string>();

                foreach (var state in snapshot.units)
                {
                    displayNames[state.unitId] = $"{state.unitName}({state.unitId})";
                    if (state.playerId == playerId)
                    {
                        if (state.isCorpse == 0)
                            ownUnits.Add(state.unitId);
                    }
                    else
                    {
                        enemyUnits.Add(state.unitId);
                    }
                }

                selection.OwnUnitIds = ownUnits;
                selection.EnemyUnitIds = enemyUnits;
                selection.DisplayNames = displayNames;

                var ownLabels = ownUnits.ConvertAll(id => displayNames[id]);
                selection.UnitCycle.SetOptions(ownLabels, ownUnits, selection.UnitCycle.Current);

                var enemyLabels = enemyUnits.ConvertAll(id => displayNames[id]);
                selection.TargetCycle.SetOptions(enemyLabels, enemyUnits, selection.TargetCycle.Current);

                RefreshUnitDependentButtons(playerId);
            }

            // HP 状态清单
            var sb = new System.Text.StringBuilder();
            foreach (var state in snapshot.units)
            {
                sb.AppendLine($"{state.unitName}({state.unitId}) {((TeamType)state.team == TeamType.A ? "P1" : "P2")}" +
                              $" HP {state.hp}/{state.maxHp} 速{state.attackSpeed} @{state.position}" +
                              (state.isCorpse != 0 ? " [尸体]" : ""));
            }
            _statusText.text = sb.ToString();
        }

        private void RefreshUnitDependentButtons(string playerId)
        {
            var selection = _selections[playerId];
            bool hasUnit = !string.IsNullOrEmpty(selection.UnitCycle?.Current);
            selection.DirectionCycle.gameObject.SetActive(hasUnit && CurrentActionIsMove(selection));
            selection.MagnitudeCycle.gameObject.SetActive(hasUnit && CurrentActionIsMove(selection));
            selection.TargetCycle.gameObject.SetActive(hasUnit && CurrentActionIsSkill(selection));
        }

        private static bool CurrentActionIsMove(PlayerSelection selection)
        {
            return (ActionType)Mathf.Max(0, selection.ActionCycle?.CurrentIndex ?? 0) == ActionType.Move;
        }

        private static bool CurrentActionIsSkill(PlayerSelection selection)
        {
            return (ActionType)Mathf.Max(0, selection.ActionCycle?.CurrentIndex ?? 0) == ActionType.Skill;
        }

        private void LogLine(string message)
        {
            GICLog.Info(message);
        }

        // ==================== UI 基础件 ====================

        private void NextRow(RectTransform parent, out RectTransform row)
        {
            _nextX = 0f;
            var rowGo = new GameObject($"Row_{_cursorY:0}");
            row = rowGo.AddComponent<RectTransform>();
            row.SetParent(parent, false);
            row.anchorMin = row.anchorMax = new Vector2(0f, 1f);
            row.pivot = new Vector2(0f, 1f);
            row.anchoredPosition = new Vector2(12f, _cursorY);
            row.sizeDelta = new Vector2(PanelWidth - 24f, RowHeight);
            _cursorY -= RowHeight + 6f;
        }

        private Text CreateLabel(RectTransform parent, string text, int fontSize, Color color)
        {
            var go = new GameObject($"Label_{text}");
            var rect = go.AddComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(12f, _cursorY);
            rect.sizeDelta = new Vector2(PanelWidth - 24f, fontSize + 8f);
            _cursorY -= fontSize + 10f;

            var label = go.AddComponent<Text>();
            label.text = text;
            label.fontSize = fontSize;
            label.color = color;
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (label.font == null) label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return label;
        }

        private void ReserveSpace(float height) => _cursorY -= height;

        private Button CreateButton(RectTransform parent, string text, float width, Action onClick)
        {
            var go = new GameObject($"Btn_{text}");
            var rect = go.AddComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(_nextX, 0f);
            rect.sizeDelta = new Vector2(width, RowHeight);
            _nextX += width + 10f;

            var image = go.AddComponent<Image>();
            image.color = new Color(0.25f, 0.32f, 0.45f, 1f);

            var button = go.AddComponent<Button>();
            var colors = button.colors;
            colors.highlightedColor = new Color(0.35f, 0.45f, 0.65f, 1f);
            colors.pressedColor = new Color(0.2f, 0.25f, 0.35f, 1f);
            button.colors = colors;
            button.onClick.AddListener(() => onClick?.Invoke());

            var labelGo = new GameObject("Text");
            var labelRect = labelGo.AddComponent<RectTransform>();
            labelRect.SetParent(go.transform, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            var label = labelGo.AddComponent<Text>();
            label.text = text;
            label.alignment = TextAnchor.MiddleCenter;
            label.fontSize = 20;
            label.color = Color.white;
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (label.font == null) label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            label.raycastTarget = false;

            return button;
        }

        private float _nextX;

        private CycleButton CreateCycleButton(RectTransform row, string prefix, string[] labels, int initialIndex, Action<int> onChanged)
        {
            var button = CreateButton(row, $"{prefix}:-", 150f, null);
            var cycle = new CycleButton(prefix, button, onChanged);
            cycle.SetOptions(new List<string>(labels), null, initialIndex >= 0 && labels.Length > 0 ? labels[Mathf.Clamp(initialIndex, 0, labels.Length - 1)] : null);
            return cycle;
        }

        // ==================== 内部类型 ====================

        private class PlayerSelection
        {
            public CycleButton UnitCycle;
            public CycleButton ActionCycle;
            public CycleButton DirectionCycle;
            public CycleButton MagnitudeCycle;
            public CycleButton TargetCycle;

            public List<string> OwnUnitIds = new List<string>();
            public List<string> EnemyUnitIds = new List<string>();
            public Dictionary<string, string> DisplayNames = new Dictionary<string, string>();

            public string UnitId => UnitCycle?.Current;
            public string TargetUnitId => TargetCycle?.Current;
        }

        /// <summary>
        /// 循环选择按钮（点击轮换值；调试级控件）
        /// </summary>
        private class CycleButton
        {
            private readonly string _prefix;
            private readonly Button _button;
            private readonly Action<int> _onChanged;
            private List<string> _labels = new List<string>();
            private List<string> _values = new List<string>();
            private int _index;

            public CycleButton(string prefix, Button button, Action<int> onChanged)
            {
                _prefix = prefix;
                _button = button;
                _onChanged = onChanged;
                _button.onClick.AddListener(CycleNext);
            }

            public string Current => _index >= 0 && _index < _values.Count ? _values[_index] : null;

            public int CurrentIndex => _index;

            public GameObject gameObject => _button != null ? _button.gameObject : null;

            public void SetOptions(List<string> labels, List<string> values, string keepValue)
            {
                _labels = labels ?? new List<string>();
                _values = values ?? _labels;

                int keepIndex = -1;
                if (keepValue != null)
                {
                    for (int i = 0; i < _values.Count; i++)
                        if (_values[i] == keepValue) { keepIndex = i; break; }
                }
                _index = keepIndex >= 0 ? keepIndex : (_labels.Count > 0 ? 0 : -1);
                RefreshLabel();
            }

            private void CycleNext()
            {
                if (_labels.Count == 0) return;
                _index = (_index + 1) % _labels.Count;
                RefreshLabel();
                _onChanged?.Invoke(_index);
            }

            private void RefreshLabel()
            {
                if (_button == null) return;
                var label = _button.GetComponentInChildren<Text>();
                if (label != null)
                    label.text = _index >= 0 ? $"{_prefix}:{_labels[_index]}" : $"{_prefix}:-";
            }
        }
    }
}
