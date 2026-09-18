using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
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
    /// 片开始时刻 =（全场最高攻速 − 本片攻速）÷ 10 秒（docs/active/22 §1）；
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
        [Tooltip("立牌后仰角（饥荒式斜插卡片：倾角=俯角 55° 时立牌面正对视线完全消压扁；0=完全垂直；2026-09-18 两轮目检修正：方向=顶部远离相机后仰）")]
        [SerializeField, Range(0f, 80f)] private float 立牌后倾角 = 55f;

        // 配色统一走 BattlePalette 配置资产（2026-09-18 统一化批次；原 _teamAColor/_teamBColor 场景序列化值
        // 与代码默认一致，迁移零损失——队伍色与 HUD 队列框/accent 同源对齐）
        private static BattlePalette Palette => BattlePalette.Instance;

        /// <summary>最新快照（HUD/血条刷新用；客户端不持逻辑状态）</summary>
        public BattleSnapshot LatestSnapshot { get; private set; }
        public float PlaybackSpeed => _playbackSpeed;
        public BattleBoard Board => _board;

        /// <summary>快照更新通知（选择阶段头 + 开局）</summary>
        public event Action<BattleSnapshot> SnapshotUpdated;

        /// <summary>片开始播放（参数=本片攻速；HUD 高亮当前执行者用）</summary>
        public event Action<int> OnSegmentPlaying;

        private IBattleTransport _transport;
        private Transform _viewRoot;
        private Quaternion _billboardRotation = Quaternion.identity;
        private readonly Dictionary<string, UnitView> _views = new Dictionary<string, UnitView>();
        private readonly Dictionary<UnitView, Vector3> _formationOffsets = new Dictionary<UnitView, Vector3>();
        private UnitConfig _unitConfig;
        private TurnFlowController _flow;

        /// <summary>当前立牌布局态：true=散开（选择阶段展开布局）/ false=收拢（执行阶段重叠格心）——两态模型（docs/active/22 §11）</summary>
        private bool _spreadFormations;

        private const float MoveStepSeconds = 0.18f;
        private const float CommandStaggerSeconds = 0.12f;
        private const float TurnEndDelaySeconds = 0.25f; // 回合结束段固定节拍（不按攻速排程）

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

        /// <summary>绑定回合状态机：两态模型由阶段切换驱动（选择阶段散开 / 执行阶段收拢，docs/active/22 §11）</summary>
        public void BindFlow(TurnFlowController flow)
        {
            if (_flow != null) _flow.OnPhaseChanged -= OnPhaseChangedForFormations;
            _flow = flow;
            if (_flow != null) _flow.OnPhaseChanged += OnPhaseChangedForFormations;
        }

        private void OnDestroy()
        {
            if (_flow != null) _flow.OnPhaseChanged -= OnPhaseChangedForFormations;
        }

        private void OnPhaseChangedForFormations(BattlePhase phase, int turn)
        {
            // 两态模型（docs/18 决策二 2026-09-17 修订）：执行阶段收拢重叠格心（特效锚=锚全部单位）；
            // 选择阶段自动散开展示（复用旧队形偏移为展开布局），无需悬停/选中触发
            _spreadFormations = phase == BattlePhase.Selecting;
            RefreshAllFormations(_spreadFormations);
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

            _spreadFormations = true; // 开局即散开（StartBattle 随即进选择阶段，事件驱动的散开为幂等重排）
            RefreshAllFormations(_spreadFormations);
            LatestSnapshot = message.initialSnapshot;
            SnapshotUpdated?.Invoke(LatestSnapshot);
            GICLog.Info($"[BattlePlayer] 建盘完成：{_views.Count} 个单位立牌");
        }

        public void OnSnapshot(SnapshotMessage message)
        {
            LatestSnapshot = message.snapshot;

            // 尸体态 + 头顶血量 + 冻结可视同步（快照为准）
            foreach (var state in message.snapshot.units)
            {
                if (_views.TryGetValue(state.unitId, out var view))
                {
                    view.SetCorpseVisual(state.isCorpse != 0);
                    view.SetFrozenVisual(state.isFrozen != 0);
                    view.SetHp(state.hp, state.maxHp);
                    view.SetBuffs(state.buffs); // 头顶 Buff 行（权威态）
                }
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

        /// <summary>投射物飞行速度（世界单位/秒；1 格=1 世界单位）</summary>
        private const float ProjectileSpeed = 8f;

        /// <summary>投射物光条 sprite（程序化 1×1 白图缓存；B5 表现批次换正式箭矢素材）</summary>
        private static Sprite _projectileSprite;
        private static Sprite ProjectileSprite
        {
            get
            {
                if (_projectileSprite != null) return _projectileSprite;
                var tex = new Texture2D(1, 1);
                tex.SetPixel(0, 0, Color.white);
                tex.Apply();
                _projectileSprite = Sprite.Create(tex, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
                return _projectileSprite;
            }
        }

        /// <summary>
        /// 直线投射物：箭矢（billboard 白色光条）从发射格飞至目标位置，到达后接伤害表现
        /// （docs/18 决策二：投射物真实飞行、命中=接触立牌圆柱之时——B4 格级近似，B5 表现批次美化与连续化）
        /// </summary>
        private IEnumerator PlayProjectileThenDamageCoroutine(UnitView target, BattleCommand command, float stagger)
        {
            if (stagger > 0f)
                yield return new WaitForSeconds(stagger);

            Vector3 from = _board.CellToWorld(command.cell) + new Vector3(0f, 0.45f, 0f);
            Vector3 to = target.transform.position + new Vector3(0f, 0.45f, 0f);

            var arrowGo = new GameObject("Projectile");
            arrowGo.transform.SetParent(_viewRoot, false);
            arrowGo.transform.position = from;
            arrowGo.transform.rotation = _billboardRotation;
            arrowGo.transform.localScale = new Vector3(0.05f, 0.30f, 1f);
            var renderer = arrowGo.AddComponent<SpriteRenderer>();
            renderer.sprite = ProjectileSprite;
            renderer.color = new Color(0.98f, 0.93f, 0.80f);
            renderer.sortingOrder = 12;

            float distance = Vector3.Distance(from, to);
            float duration = distance > 0f ? distance / (ProjectileSpeed * _playbackSpeed) : 0f;
            yield return BattleViewTween.Over(duration, t => arrowGo.transform.position = Vector3.Lerp(from, to, t));

            Destroy(arrowGo);
            yield return PlayDamageCoroutine(target, -command.value, 0f, false);
        }

        private IEnumerator PlaySegmentCoroutine(Segment segment)
        {
            // 片开始时刻 =（全场最高攻速 − 自身攻速）÷ 10 秒（连续换算）；
            // 即时行动块 = 短节拍；回合结束段 = 固定节拍（不按攻速排程）
            float delay;
            if (segment.turnEnd != 0)
                delay = TurnEndDelaySeconds;
            else if (segment.insertedInstantAction != 0)
                delay = 0.15f;
            else
                delay = Mathf.Max(0f, (segment.turnMaxAttackSpeed - segment.sliceAttackSpeed) / 10f);
            if (delay > 0f)
                yield return new WaitForSeconds(delay / _playbackSpeed);

            if (segment.turnEnd == 0)
                OnSegmentPlaying?.Invoke(segment.sliceAttackSpeed); // 回合结束段无"当前执行者"，不高亮

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
                        {
                            if (command.direction == 1) // 直线投射物（B4）：先飞后中（delivery 元数据）
                                playbacks.Add(StartCoroutine(PlayProjectileThenDamageCoroutine(target, command, stagger)));
                            else
                                playbacks.Add(StartCoroutine(PlayDamageCoroutine(target, -command.value, stagger, false)));
                        }
                        break;

                    case BattleCommandType.Heal:
                        if (_views.TryGetValue(command.targetUnitId, out var healed))
                            playbacks.Add(StartCoroutine(PlayDamageCoroutine(healed, command.value, stagger, true)));
                        break;

                    case BattleCommandType.Death:
                        if (_views.TryGetValue(command.actorUnitId, out var dying))
                            playbacks.Add(StartCoroutine(PlayDeathCoroutine(dying, stagger)));
                        break;

                    case BattleCommandType.ApplyBuff:
                        if (_views.TryGetValue(command.targetUnitId, out var buffed))
                            buffed.ApplyBuffBadge(command.buffType, command.buffTurns);
                        break;

                    case BattleCommandType.RemoveBuff:
                        if (_views.TryGetValue(command.targetUnitId, out var unbuffed))
                            unbuffed.RemoveBuffBadge(command.buffType);
                        break;

                    default:
                        // B4+ 接线：ElementAttach/Reaction/StatChange/Summon 等
                        break;
                }
                stagger += CommandStaggerSeconds / _playbackSpeed;
            }

            foreach (var playback in playbacks)
                yield return playback;

            // 按当前布局态收拢/散开重排（执行阶段=收拢重叠格心；阶段切换事件负责切态）
            RefreshAllFormations(_spreadFormations);

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
                yield return BattleViewTween.Over(stepSeconds, t => view.ApplyPosition(Vector3.Lerp(from, to, t)));
            }
            if (path.Count > 0)
                view.ApplyPosition(_board.CellToWorld(path[path.Count - 1]));
        }

        private IEnumerator PlayDamageCoroutine(UnitView view, int displayValue, float delay, bool isHeal)
        {
            if (delay > 0f)
                yield return new WaitForSeconds(delay);

            view.FlashHit();
            view.ApplyHpDelta(displayValue); // 头顶血条即时反馈（快照权威，下个选择阶段头校正）

            // 伤害/治疗数字（B5 正式化：随机偏移防同点多数字重叠 + 起手弹跳放大；
            // 2026-09-18 统一化：TextMesh→世界 TMP，fontSize 64×scale 0.06 ≈0.384 世界高与原 characterSize 等高）
            var numberGo = new GameObject("DamageNumber");
            numberGo.transform.SetParent(_viewRoot, false);
            var randomOffset = new Vector3(
                UnityEngine.Random.Range(-0.18f, 0.18f), 0.35f, UnityEngine.Random.Range(-0.04f, 0.04f));
            Vector3 start = view.transform.position + randomOffset;
            numberGo.transform.position = start;
            numberGo.transform.rotation = _billboardRotation;
            numberGo.transform.localScale = Vector3.one * 0.06f;

            var text = numberGo.AddComponent<TextMeshPro>();
            text.font = BattleViewFactory.WorldTextFont;
            text.fontSize = 64;
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = false;
            ((RectTransform)numberGo.transform).sizeDelta = new Vector2(40f, 14f);
            text.text = displayValue > 0 ? $"+{displayValue}" : displayValue.ToString();
            var baseColor = isHeal ? Palette.治疗绿 : Palette.伤害红;
            text.color = baseColor;

            Vector3 end = start + new Vector3(0f, 0.55f, 0f);
            float duration = 0.8f / _playbackSpeed;
            float popDuration = 0.15f / _playbackSpeed;
            Vector3 baseScale = numberGo.transform.localScale;
            yield return BattleViewTween.Over(duration, t =>
            {
                numberGo.transform.position = Vector3.Lerp(start, end, t);
                text.color = new Color(baseColor.r, baseColor.g, baseColor.b, 1f - t);
                // 弹跳：前 15% 从 0.6 放大到 1.15，回落到 1.0 后保持
                float elapsed = t * duration;
                float pop = elapsed < popDuration
                    ? Mathf.Lerp(0.6f, 1.15f, elapsed / popDuration)
                    : Mathf.Lerp(1.15f, 1f, Mathf.Clamp01((elapsed - popDuration) / (popDuration * 2f)));
                numberGo.transform.localScale = baseScale * pop;
            });

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
            TextEntry nameEntry = null;
            if (_unitConfig != null && Enum.TryParse<UnitName>(state.unitName, out var unitName) &&
                _unitConfig.TryGetUnitData(unitName, out var unitData))
            {
                avatar = unitData.avatar;
                displayName = unitName.ToString();
                nameEntry = unitName.GetEntry(); // 单位名本地化条目（UnitName 表）
            }

            var team = (TeamType)state.team;
            var teamColor = team == TeamType.B ? Palette.敌方主色 : Palette.我方主色;
            // 血条敌我染色：本地 1v1 惯例 A=先手（真人）→ 绿；B7 联机时应按 viewer 归属重定
            bool allyHpBar = team != TeamType.B;
            var view = UnitView.Create(_viewRoot, state.unitId, displayName, avatar, teamColor,
                _billboardRotation, 立牌后倾角, nameEntry, state.hp, state.maxHp, allyHpBar);
            view.Cell = state.position;
            view.SetCorpseVisual(state.isCorpse != 0);
            view.SetFrozenVisual(state.isFrozen != 0);
            view.SetBuffs(state.buffs);
            view.ApplyPosition(_board.CellToWorld(state.position));
            _views[state.unitId] = view;
        }

        /// <summary>
        /// 同格立牌布局（两态模型，docs/active/22 §11）：spread=true 散开展示（1 居中/2 并排/3 三角展开布局，
        /// 选择阶段）/ false 收拢重叠格心（执行阶段，特效锚格心=锚全部单位）；占据数变化实时重排
        /// </summary>
        private void RefreshAllFormations(bool spread)
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
                    Vector3 offset = spread ? GetFormationOffset(occupants.Count, i) : Vector3.zero;
                    _formationOffsets[occupants[i]] = offset;
                    occupants[i].ApplyPosition(_board.CellToWorld(kv.Key) + offset);
                }
            }
        }

        /// <summary>散开态的同格偏移（原队形布局复用为展开形态，docs/18 决策二）</summary>
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
