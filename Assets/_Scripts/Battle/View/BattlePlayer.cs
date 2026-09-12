using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.UI;
using GIC.Tool;
namespace GIC.Battle
{


    /// <summary>
    /// 最小战斗播放器（客户端侧；Host 显示侧同构复用）。
    /// Snapshot 建场 → Segment 驱动动画 → 片播放完成回 ack。
    /// 片开始时刻 =（全场最高攻速 − 本片攻速）÷ 10 秒（docs/22 §1）；
    /// 表现层时序与逻辑层解耦：逻辑即时结算，本类只管"何时播"。
    /// </summary>
    public class BattlePlayer : MonoBehaviour
    {
        [Header("棋盘引用")]
        [SerializeField] private BattleBoard _board;
        [Tooltip("立牌朝向相机（Additive 场景下 Camera.main 是 MainHall 相机，必须显式指定）")]
        [SerializeField] private Camera _viewCamera;

        [Header("播放参数")]
        [Tooltip("播放速率倍率（1=按公式实时；压缩片间等待用）")]
        [SerializeField] private float _playbackSpeed = 1f;

        [Header("立牌视觉")]
        [SerializeField] private Color _teamAColor = new Color(0.25f, 0.55f, 1f, 1f);
        [SerializeField] private Color _teamBColor = new Color(1f, 0.35f, 0.3f, 1f);

        /// <summary>最新快照（调试面板刷新用；客户端不持逻辑状态）</summary>
        public BattleSnapshot LatestSnapshot { get; private set; }
        public float PlaybackSpeed => _playbackSpeed;
        public BattleBoard Board => _board;

        /// <summary>快照更新通知（选择阶段头 + 开局）</summary>
        public event Action<BattleSnapshot> SnapshotUpdated;

        private IBattleTransport _transport;
        private Transform _viewRoot;
        private Quaternion _billboardRotation = Quaternion.identity;
        private readonly Dictionary<string, UnitView> _views = new Dictionary<string, UnitView>();
        private readonly Dictionary<UnitView, Vector3> _formationOffsets = new Dictionary<UnitView, Vector3>();
        private UnitConfig _unitConfig;

        private const float MoveStepSeconds = 0.18f;
        private const float CommandStaggerSeconds = 0.12f;

        public void Bind(IBattleTransport transport, BattleMapData map)
        {
            _transport = transport;
            if (_viewRoot == null)
            {
                var rootGo = new GameObject("UnitViews");
                rootGo.transform.SetParent(transform, false);
                _viewRoot = rootGo.transform;
            }
        }

        public void SetPlaybackSpeed(float speed)
        {
            _playbackSpeed = Mathf.Max(0.1f, speed);
        }

        private void Update()
        {
            // 立牌面向战场相机（固定斜俯视，逐帧保持即可；相机不动时开销可忽略）
            var cam = _viewCamera != null ? _viewCamera : Camera.main;
            if (cam != null)
            {
                var forward = cam.transform.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude > 0.001f)
                    _billboardRotation = Quaternion.LookRotation(forward, Vector3.up);
            }
        }

        // ==================== 消息处理（客户端侧） ====================

        public void OnBattleStart(BattleStartMessage message)
        {
            if (_board == null)
            {
                GICLog.Error("[BattlePlayer] 未绑定 BattleBoard，无法建盘");
                return;
            }

            _board.Build(message.map);
            ClearViews();

            foreach (var state in message.initialSnapshot.units)
                CreateView(state);

            RefreshAllFormations();
            LatestSnapshot = message.initialSnapshot;
            SnapshotUpdated?.Invoke(LatestSnapshot);
            GICLog.Info($"[BattlePlayer] 建盘完成：{_views.Count} 个单位立牌");
        }

        public void OnSnapshot(SnapshotMessage message)
        {
            LatestSnapshot = message.snapshot;

            // 尸体态可视同步（快照为准）
            foreach (var state in message.snapshot.units)
            {
                if (_views.TryGetValue(state.unitId, out var view))
                    view.SetCorpseVisual(state.isCorpse != 0);
            }

            SnapshotUpdated?.Invoke(message.snapshot);
        }

        public void OnSegment(SegmentMessage message)
        {
            StartCoroutine(PlaySegmentCoroutine(message.segment));
        }

        public void OnTurnEnd(TurnEndMessage message)
        {
            GICLog.Info($"[BattlePlayer] 回合 {message.turnNumber} 结束");
        }

        // ==================== 片播放 ====================

        private IEnumerator PlaySegmentCoroutine(Segment segment)
        {
            // 片开始时刻 =（全场最高攻速 − 自身攻速）÷ 10 秒（连续换算）
            float delay = segment.insertedInstantAction != 0
                ? 0.15f
                : Mathf.Max(0f, (segment.turnMaxAttackSpeed - segment.sliceAttackSpeed) / 10f);
            if (delay > 0f)
                yield return new WaitForSeconds(delay / _playbackSpeed);

            var playbacks = new List<Coroutine>();
            float stagger = 0f;
            foreach (var command in segment.commands)
            {
                switch (command.type)
                {
                    case BattleCommandType.Move:
                        if (_views.TryGetValue(command.actorUnitId, out var mover))
                        {
                            mover.Cell = command.path.Count > 0 ? command.path[command.path.Count - 1] : mover.Cell;
                            playbacks.Add(StartCoroutine(PlayMoveCoroutine(mover, command.path)));
                        }
                        break;

                    case BattleCommandType.Damage:
                        if (_views.TryGetValue(command.targetUnitId, out var target))
                            playbacks.Add(StartCoroutine(PlayDamageCoroutine(target, -command.value, stagger, false)));
                        break;

                    case BattleCommandType.Heal:
                        if (_views.TryGetValue(command.targetUnitId, out var healed))
                            playbacks.Add(StartCoroutine(PlayDamageCoroutine(healed, command.value, stagger, true)));
                        break;

                    case BattleCommandType.Death:
                        if (_views.TryGetValue(command.actorUnitId, out var dying))
                            playbacks.Add(StartCoroutine(PlayDeathCoroutine(dying, stagger)));
                        break;

                    default:
                        // B2+ 接线：ApplyBuff/RemoveBuff/ElementAttach/Reaction 等
                        break;
                }
                stagger += CommandStaggerSeconds / _playbackSpeed;
            }

            foreach (var playback in playbacks)
                yield return playback;

            RefreshAllFormations();

            // 片播放完成确认（Host 等全体 ack 才结算下一片）
            _transport.ClientSend(BattleMessageType.SegmentAck, new SegmentAck
            {
                turnNumber = segment.turnNumber,
                sliceIndex = segment.sliceIndex,
            });
        }

        private IEnumerator PlayMoveCoroutine(UnitView view, List<BattleCell> path)
        {
            float stepSeconds = MoveStepSeconds / _playbackSpeed;
            for (int i = 1; i < path.Count; i++)
            {
                Vector3 from = _board.CellToWorld(path[i - 1]);
                Vector3 to = _board.CellToWorld(path[i]);
                float elapsed = 0f;
                while (elapsed < stepSeconds)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / stepSeconds);
                    view.ApplyPosition(Vector3.Lerp(from, to, t));
                    yield return null;
                }
                view.ApplyPosition(to);
            }
            if (path.Count > 0)
                view.ApplyPosition(_board.CellToWorld(path[path.Count - 1]));
        }

        private IEnumerator PlayDamageCoroutine(UnitView view, int displayValue, float delay, bool isHeal)
        {
            if (delay > 0f)
                yield return new WaitForSeconds(delay);

            view.FlashHit();

            // 扣血数字（TextMesh 调试级；B5 换正式伤害数字表现）
            var numberGo = new GameObject("DamageNumber");
            numberGo.transform.SetParent(_viewRoot, false);
            numberGo.transform.position = view.transform.position + new Vector3(0f, 0.35f, 0f);
            numberGo.transform.rotation = _billboardRotation;

            var textMesh = numberGo.AddComponent<TextMesh>();
            textMesh.text = displayValue > 0 ? $"+{displayValue}" : displayValue.ToString();
            textMesh.fontSize = 64;
            textMesh.characterSize = 0.06f;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.color = isHeal ? new Color(0.3f, 0.95f, 0.45f) : new Color(1f, 0.3f, 0.25f);

            Vector3 start = numberGo.transform.position;
            Vector3 end = start + new Vector3(0f, 0.55f, 0f);
            float duration = 0.8f / _playbackSpeed;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                numberGo.transform.position = Vector3.Lerp(start, end, t);
                textMesh.color = new Color(textMesh.color.r, textMesh.color.g, textMesh.color.b, 1f - t);
                yield return null;
            }

            view.RestoreColor();
            UnityEngine.Object.Destroy(numberGo);
        }

        private IEnumerator PlayDeathCoroutine(UnitView view, float delay)
        {
            if (delay > 0f)
                yield return new WaitForSeconds(delay);
            view.SetCorpseVisual(true);
        }

        // ==================== 立牌与队形 ====================

        private void ClearViews()
        {
            foreach (var kv in _views)
                if (kv.Value != null)
                    Destroy(kv.Value.gameObject);
            _views.Clear();
            _formationOffsets.Clear();
        }

        private void CreateView(UnitState state)
        {
            if (_views.ContainsKey(state.unitId)) return;

            // 客户端只依快照与共享配置建场（不触 Host 逻辑对象）
            if (_unitConfig == null)
                _unitConfig = Resources.Load<UnitConfig>("Configs/UnitConfig");

            Sprite avatar = null;
            string displayName = state.unitId;
            if (_unitConfig != null && Enum.TryParse<UnitName>(state.unitName, out var unitName) &&
                _unitConfig.TryGetUnitData(unitName, out var unitData))
            {
                avatar = unitData.avatar;
                displayName = unitName.ToString();
            }

            var teamColor = (TeamType)state.team == TeamType.B ? _teamBColor : _teamAColor;
            var view = UnitView.Create(_viewRoot, state.unitId, displayName, avatar, teamColor, _billboardRotation);
            view.Cell = state.position;
            view.SetCorpseVisual(state.isCorpse != 0);
            view.ApplyPosition(_board.CellToWorld(state.position));
            _views[state.unitId] = view;
        }

        /// <summary>
        /// 同格队形布局（docs/18 决策二）：1 居中 / 2 并排 / 3 三角（2 前 1 后）；占据数变化实时重排
        /// </summary>
        private void RefreshAllFormations()
        {
            var byCell = new Dictionary<BattleCell, List<UnitView>>();
            foreach (var view in _views.Values)
            {
                if (!byCell.TryGetValue(view.Cell, out var list))
                {
                    list = new List<UnitView>();
                    byCell[view.Cell] = list;
                }
                list.Add(view);
            }

            foreach (var kv in byCell)
            {
                var occupants = kv.Value;
                occupants.Sort((a, b) => string.CompareOrdinal(a.UnitId, b.UnitId));

                for (int i = 0; i < occupants.Count; i++)
                {
                    Vector3 offset = GetFormationOffset(occupants.Count, i);
                    _formationOffsets[occupants[i]] = offset;
                    occupants[i].ApplyPosition(_board.CellToWorld(kv.Key) + offset);
                }
            }
        }

        private static Vector3 GetFormationOffset(int count, int index)
        {
            switch (count)
            {
                case 1: return Vector3.zero;
                case 2: return index == 0 ? new Vector3(-0.2f, 0f, 0.1f) : new Vector3(0.2f, 0f, 0.1f);
                default:
                    // 3 单位：2 前 1 后三角（多余单位叠加保持三角外扩）
                    return index switch
                    {
                        0 => new Vector3(-0.2f, 0f, 0.12f),
                        1 => new Vector3(0.2f, 0f, 0.12f),
                        2 => new Vector3(0f, 0f, -0.14f),
                        3 => new Vector3(-0.4f, 0f, -0.14f),
                        _ => new Vector3(0.4f, 0f, -0.14f),
                    };
            }
        }
    }
}
