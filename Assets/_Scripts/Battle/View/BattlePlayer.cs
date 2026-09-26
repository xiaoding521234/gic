using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Video;
using GIC.Framework;
using GIC.Data;
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

        [Tooltip("被挡撞墙探出幅度（格距比例；移动被挡=向被挡方向探出再弹回，逻辑位置不变，2026-09-21）")]
        [SerializeField, Range(0.05f, 0.9f)] private float 被挡撞墙探出幅度 = 0.45f;

        [Header("立牌视觉")]
        [Tooltip("立牌后仰角（饥荒式斜插卡片：倾角=俯角 55° 时立牌面正对视线完全消压扁；0=完全垂直；2026-09-18 两轮目检修正：方向=顶部远离相机后仰）")]
        [SerializeField, Range(0f, 80f)] private float 立牌后倾角 = 55f;

        [Tooltip("全身立牌（UnitData.立牌图）放大倍数：全身立绘人物在图中占比小，放大对齐头像版人物观感（2026-09-21 先试 2.5）；血条/名字/Buff 行尺寸不变、随立牌顶抬高；头像版（无立牌图回落 avatar）恒为原尺寸")]
        [SerializeField, Min(0.1f)] private float 全身立牌放大倍数 = 2.5f;

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

        /// <summary>玩家资源增量（B6d 经济闭环：摩拉/体力 StatChange 命令消费点——
        /// 参数=玩家ID / 属性子类型（StatKindMora/StatKindStamina）/ 变化量；HUD 订阅即时刷新，
        /// 快照权威兜底）</summary>
        public event Action<string, int, int> OnResourceDelta;

        /// <summary>战斗结束事件（2026-09-25 三轮审查 S10 轻量全灭软停；订阅方=HUD 胜负提示，
        /// 结算画面=B8）</summary>
        public event Action<BattleOverMessage> BattleOver;

        private IBattleTransport _transport;
        private Transform _viewRoot;
        private BattleDamageNumbers _damageNumbers;
        private BattleOverheadBars _overheadBars;
        private Quaternion _billboardRotation = Quaternion.identity;
        private readonly Dictionary<string, UnitView> _views = new Dictionary<string, UnitView>();
        private readonly Dictionary<UnitView, Vector3> _formationOffsets = new Dictionary<UnitView, Vector3>();

        /// <summary>单位配置（DI 容器 [Bean] 缓存——ConfigManager 产出；2026-09-23 审查 Y10 收口，Bind 时注入）</summary>
        [Autowired] private UnitConfig _unitConfig;
        private TurnFlowController _flow;

        /// <summary>当前立牌布局态：true=散开（选择阶段展开布局）/ false=收拢（执行阶段重叠格心）——两态模型（docs/active/22 §11）</summary>
        private bool _spreadFormations;

        private const float MoveStepSeconds = BattleMetrics.MoveStepSeconds;
        private const float CommandStaggerSeconds = 0.12f;
        private const float TurnEndDelaySeconds = 0.25f; // 回合结束段固定节拍（不按攻速排程）
        private const float BlockedBumpOutSecondsFactor = 0.75f;  // 撞墙探出时长 = 步进节奏 × 0.75（缓出）
        private const float BlockedBumpBackSecondsFactor = 0.55f; // 撞墙弹回时长 = 步进节奏 × 0.55（快出缓停）

        public void Bind(IBattleTransport transport, BattleMapData map)
        {
            Wargame.Instance?.Context?.Inject(this); // [Autowired] UnitConfig（Y10）
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
                    view.SetAttachedElement((ElementType)state.dyedElement); // 附着元素（权威态）
                    view.SetHp(state.hp, state.maxHp);
                    view.SetEnergy(state.energy, state.maxEnergy); // 元能（权威态；B6a）
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

        /// <summary>战斗结束（S10 轻量全灭软停）：一方全灭——HUD 胜负提示消费；结算画面=B8</summary>
        public void OnBattleOver(BattleOverMessage message)
        {
            GICLog.Info($"[BattlePlayer] 战斗结束：胜方队伍 {(TeamType)message.winnerTeam}");
            BattleOver?.Invoke(message);
        }

        // ==================== 片播放 ====================

        // 投射物/移动速度常量统一走 BattleMetrics（B5 连续判定：Host 判定与客户端播放同源，
        // "所见即所得"=双方按同一速度常量推进时间轴）
        private const float ProjectileSpeed = BattleMetrics.ProjectileSpeed;

        /// <summary>
        /// 直线投射物：箭矢从发射格飞至**命中点**（Host 接触判定千分定点下发，docs/active/22 §11——
        /// 移动中目标命中点=中途接触位置，所见即所得），到达后接伤害表现。
        /// B4 的"飞向目标当前位置"近似已废弃；无命中点数据时兜底旧行为。
        /// </summary>
        private IEnumerator PlayProjectileThenDamageCoroutine(UnitView target, BattleCommand command, float stagger)
        {
            if (stagger > 0f)
                yield return new WaitForSeconds(stagger);

            // 时轮（B-S1）：发射时刻延迟（Host 时轮资产下发，勿推算——前摇=箭矢延迟起飞）
            if (command.launchMs > 0)
                yield return new WaitForSeconds(command.launchMs / 1000f / _playbackSpeed);

            Vector3 from = _board.CellToWorld(command.cell) + new Vector3(0f, 0.45f, 0f);
            Vector3 to = command.hitX != 0 || command.hitY != 0
                ? _board.ContinuousCellToWorld(command.hitX / 1000f, command.hitY / 1000f) + new Vector3(0f, 0.45f, 0f)
                : target.transform.position + new Vector3(0f, 0.45f, 0f); // 兜底：无定点数据时飞向目标

            var arrowGo = CreateProjectileVisual(from);

            float distance = Vector3.Distance(from, to);
            float duration = distance > 0f ? distance / (ProjectileSpeed * _playbackSpeed) : 0f;
            yield return BattleViewTween.Over(duration, t => arrowGo.transform.position = Vector3.Lerp(from, to, t));

            Destroy(arrowGo);
            yield return PlayDamageCoroutine(target, -command.value, 0f, false, command.reactionKind);
        }

        /// <summary>
        /// 投射物消散（Effect 命令·EffectKindProjectileVanish）：无接触命中（虚空截断或 24 格上限，
        /// docs/05 §5.3）——从发射格飞至消散点（千分定点）后消失；无定点时按方向飞 value 格兜底
        /// </summary>
        private IEnumerator PlayProjectileVanishCoroutine(BattleCommand command, float stagger)
        {
            if (stagger > 0f)
                yield return new WaitForSeconds(stagger);

            // 时轮（B-S1）：发射时刻延迟（与命中侧投射物同源对齐）
            if (command.launchMs > 0)
                yield return new WaitForSeconds(command.launchMs / 1000f / _playbackSpeed);

            Vector3 from = _board.CellToWorld(command.cell) + new Vector3(0f, 0.45f, 0f);
            Vector3 to;
            if (command.hitX != 0 || command.hitY != 0)
            {
                to = _board.ContinuousCellToWorld(command.hitX / 1000f, command.hitY / 1000f) + new Vector3(0f, 0.45f, 0f);
            }
            else
            {
                var delta = SkillHitResolver.DirectionToDelta((Direction2D)command.direction);
                to = _board.CellToWorld(new BattleCell(command.cell.x + delta.x * command.value,
                    command.cell.y + delta.y * command.value)) + new Vector3(0f, 0.45f, 0f);
            }

            var arrowGo = CreateProjectileVisual(from);

            float distance = Vector3.Distance(from, to);
            float duration = distance > 0f ? distance / (ProjectileSpeed * _playbackSpeed) : 0f;
            yield return BattleViewTween.Over(duration, t => arrowGo.transform.position = Vector3.Lerp(from, to, t));

            Destroy(arrowGo);
        }

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

        /// <summary>箭矢视觉（billboard 白色光条；B5 换正式素材——创建即就位，飞行由调用方 tween。
        /// 配色=BattlePalette「箭矢占位色」活色（2026-09-23 审查 Y9 收口，勿写字面量）</summary>
        private GameObject CreateProjectileVisual(Vector3 from)
        {
            var arrowGo = new GameObject("Projectile");
            arrowGo.transform.SetParent(_viewRoot, false);
            arrowGo.transform.position = from;
            arrowGo.transform.rotation = _billboardRotation;
            arrowGo.transform.localScale = new Vector3(0.05f, 0.30f, 1f);
            var renderer = arrowGo.AddComponent<SpriteRenderer>();
            renderer.sprite = ProjectileSprite;
            renderer.color = Palette.箭矢占位色;
            renderer.sortingOrder = 12;
            return arrowGo;
        }

        private IEnumerator PlaySegmentCoroutine(Segment segment)
        {
            // 片开始时刻 =（全场最高攻速 − 自身攻速）÷ 10 秒（连续换算）；
            // 即时行动块/部署段 = 短节拍；回合结束段 = 固定节拍（不按攻速排程）
            float delay;
            if (segment.turnEnd != 0)
                delay = TurnEndDelaySeconds;
            else if (segment.insertedInstantAction != 0 || segment.deploy != 0)
                delay = 0.15f;
            else
                delay = Mathf.Max(0f, (segment.turnMaxAttackSpeed - segment.sliceAttackSpeed) / 10f);
            if (delay > 0f)
                yield return new WaitForSeconds(delay / _playbackSpeed);

            if (segment.turnEnd == 0 && segment.deploy == 0)
                OnSegmentPlaying?.Invoke(segment.sliceAttackSpeed); // 回合结束/部署段无"当前执行者"，不高亮

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
                            playbacks.Add(StartCoroutine(PlayMoveCoroutine(mover, command)));
                        }
                        break;

                    case BattleCommandType.Damage:
                        if (_views.TryGetValue(command.targetUnitId, out var target))
                        {
                            if (command.direction == 1)
                                // 直线投射物（B5）：与移动同 t=0 起跑（时间轴对齐——命中时刻=接触时刻），
                                // 不吃命令 stagger；命中点由命令定点下发
                                playbacks.Add(StartCoroutine(PlayProjectileThenDamageCoroutine(target, command, 0f)));
                            else
                                playbacks.Add(StartCoroutine(PlayDamageCoroutine(target, -command.value, stagger, false,
                                    command.reactionKind)));
                        }
                        break;

                    case BattleCommandType.Heal:
                        if (_views.TryGetValue(command.targetUnitId, out var healed))
                        {
                            // 命中时刻（2026-09-25「命中时才给」）：OnHit 治疗（水之浅唱）到命中毫秒再弹 +N
                            //（与投射物落地同时刻）；0=立即（OnCast 治疗/回合结束段，保持命令 stagger 节拍）
                            float healDelay = command.launchMs > 0
                                ? command.launchMs / 1000f / _playbackSpeed
                                : stagger;
                            playbacks.Add(StartCoroutine(PlayDamageCoroutine(healed, command.value, healDelay, true)));
                        }
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
                        {
                            unbuffed.RemoveBuffBadge(command.buffType);
                            // 冻结到期即时退冰色（与 Reaction(Freeze) 即时上色对称；其它视觉仍随快照）
                            if (command.buffType == (int)BuffType.Freeze)
                                unbuffed.SetFrozenVisual(false);
                        }
                        break;

                    case BattleCommandType.ElementAttach:
                        // 附着即时刷新（2026-09-22 接线：此前靠下回合快照自愈，反应当片不可见）
                        if (_views.TryGetValue(command.targetUnitId, out var attached))
                            attached.SetAttachedElement((ElementType)command.metadata);
                        break;

                    case BattleCommandType.Reaction:
                        // 反应发生事件：冻结=立牌冰色即时同步（此前冰色只随快照来，反应当回合不显）；
                        // 融化=反应爆发特效属 B5/B6 画面批次，此处仅留挂点
                        if (_views.TryGetValue(command.targetUnitId, out var reacted))
                        {
                            if (command.metadata == BattleCommand.ReactionKindFreeze)
                                reacted.SetFrozenVisual(true);
                        }
                        break;

                    case BattleCommandType.StatChange:
                        // 属性变化增量：元能（B6a）→单位缓存；摩拉/体力（B6d）→玩家资源事件——
                        // 玩家资源命令的 targetUnitId=玩家 ID 不在 _views，先分流否则被静默丢弃。
                        // 摩拉掠夺命令带命中时刻（B-3，同元能「命中时才给」）：launchMs>0 到点再应用
                        if (command.metadata == BattleCommand.StatKindMora
                            || command.metadata == BattleCommand.StatKindStamina)
                        {
                            if (command.metadata == BattleCommand.StatKindMora && command.launchMs > 0)
                                playbacks.Add(StartCoroutine(PlayResourceDeltaCoroutine(command)));
                            else
                                OnResourceDelta?.Invoke(command.targetUnitId, command.metadata, command.value);
                            break;
                        }
                        if (_views.TryGetValue(command.targetUnitId, out var statChanged))
                        {
                            if (command.metadata == BattleCommand.StatKindEnergy)
                            {
                                // 命中时刻（2026-09-25 拍板「命中时才给」）：战技获能命令带命中毫秒——
                                // 到点再跳元能（与投射物命中表现同时刻）；0=立即（移动获能/协奏/消耗/回合发放）
                                if (command.launchMs > 0)
                                    playbacks.Add(StartCoroutine(PlayEnergyDeltaCoroutine(statChanged, command)));
                                else
                                    statChanged.ApplyEnergyDelta(command.value);
                            }
                        }
                        break;

                    case BattleCommandType.Summon:
                        // 部署登场（B6c）：按命令携带的全量状态即时建 view（快照权威自愈兜底）
                        if (command.summonUnit != null && !_views.ContainsKey(command.summonUnit.unitId))
                        {
                            CreateView(command.summonUnit);
                            // 新登场立即按当前布局态入位（两态模型：执行阶段=收拢重叠格心）
                            if (_views.TryGetValue(command.summonUnit.unitId, out var summoned))
                                summoned.ApplyPosition(_board.CellToWorld(command.summonUnit.position));
                            RefreshAllFormations(_spreadFormations);
                        }
                        break;

                    case BattleCommandType.SkillCast:
                        // 时轮演出起点事件（B-S1 协议占位）：按 skillID 加载时轮资产播动作/音效/特效轨
                        // =B-S3 素材落地后接线；当前无表现资产，前摇期视觉由投射物命令的 launchMs
                        // 延迟起飞承载（施放者动作/音效待素材批次）
                        GICLog.Info($"[BattlePlayer] 技能施放：{command.actorUnitId} → {(SkillName)command.value}" +
                                    $" 方向 {(Direction2D)command.direction}");
                        break;

                    case BattleCommandType.Effect:
                        // 特效命令（B5 首个接线=投射物消散，与投射物/移动同 t=0 起跑）
                        if (command.metadata == BattleCommand.EffectKindProjectileVanish)
                            playbacks.Add(StartCoroutine(PlayProjectileVanishCoroutine(command, 0f)));
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

        private IEnumerator PlayMoveCoroutine(UnitView view, BattleCommand command)
        {
            var path = command.path;
            float stepSeconds = MoveStepSeconds / _playbackSpeed;
            for (int i = 1; i < path.Count; i++)
            {
                Vector3 from = _board.CellToWorld(path[i - 1]);
                Vector3 to = _board.CellToWorld(path[i]);
                yield return BattleViewTween.Over(stepSeconds, t => view.ApplyPosition(Vector3.Lerp(from, to, t)));
            }
            if (path.Count > 0)
                view.ApplyPosition(_board.CellToWorld(path[path.Count - 1]));

            // 被挡撞墙弹回：Host 已停在被挡格前（全挡=原格 / 部分挡=被挡格前一格），表现层探出再弹回
            if (command.metadata == BattleCommand.MoveBlocked)
                yield return PlayBlockedBumpCoroutine(view, path.Count > 0 ? path[path.Count - 1] : view.Cell,
                    command.direction, stepSeconds);
        }

        /// <summary>
        /// 被挡撞墙弹回（2026-09-21）：向被挡方向探出后弹回原位——纯表现层反馈，逻辑位置不变。
        /// 探出缓出挤向被挡格、弹回快出缓停，时长跟随步进节奏（0.18s/格）；
        /// 方向换算用 MovementResolver.StepVector（8 向与 Host 步进同源，斜向=朝下一格格心等比例 45%）
        /// </summary>
        private IEnumerator PlayBlockedBumpCoroutine(UnitView view, BattleCell homeCell, int blockedDirection, float stepSeconds)
        {
            // 勿用 SkillHitResolver.DirectionToDelta——十字归一映射会把斜向弹回归一到主轴（首版教训）
            var step = MovementResolver.StepVector((Direction2D)blockedDirection);
            var dir = new Vector3(step.x, 0f, step.y);
            if (dir.sqrMagnitude < 0.001f) yield break;

            Vector3 home = _board.CellToWorld(homeCell);
            Vector3 peak = home + dir * 被挡撞墙探出幅度;
            // 探出：缓出减速（挤向被挡格的"尝试"感）
            yield return BattleViewTween.Over(stepSeconds * BlockedBumpOutSecondsFactor, t =>
                view.ApplyPosition(Vector3.Lerp(home, peak, 1f - (1f - t) * (1f - t))));
            // 弹回：快速离开、临原位缓停（末帧保证 t=1 精确归位）
            yield return BattleViewTween.Over(stepSeconds * BlockedBumpBackSecondsFactor, t =>
                view.ApplyPosition(Vector3.Lerp(peak, home, 1f - (1f - t) * (1f - t))));
        }

        /// <summary>反应子类型 → 本地化反应名（短命数字对象直接求值当前语言，不挂 TextCombiner）。
        /// 仅增伤反应（融化/蒸发）有名——冻结是控制反应、无伤害加成，数字不带名</summary>
        private static string ReactionNameOf(int reactionKind)
        {
            string key = null;
            if (reactionKind == BattleCommand.ReactionKindMelt) key = "Battle_ReactionMelt";
            else if (reactionKind == BattleCommand.ReactionKindVaporize) key = "Battle_ReactionVaporize";
            if (key == null) return null;
            return new LocalizedString("UIText", key).GetLocalizedString();
        }

        private IEnumerator PlayDamageCoroutine(UnitView view, int displayValue, float delay, bool isHeal,
            int reactionKind = 0)
        {
            if (delay > 0f)
                yield return new WaitForSeconds(delay);

            // 治疗不闪受击红（2026-09-25 三轮审查 S5 批修正：原实现无条件 FlashHit，
            // 治疗 +N 跳血时立牌闪红观感错误）；受击闪色维持原数字存活节律再恢复——
            // 拆独立协程不 gate ack（S5：纯装饰尾巴曾挂在 playbacks 里把每片拉长 0.8s）
            if (!isHeal)
            {
                view.FlashHit();
                StartCoroutine(RestoreColorAfterFlashRoutine(view));
            }
            view.ApplyHpDelta(displayValue); // 头顶血条即时反馈（快照权威，下个选择阶段头校正）

            // 伤害/治疗数字=原神式屏幕空间层（2026-09-24 拍板「按照原神的做法」：Overlay 画布永不遮挡、
            // 首帧爆裂收缩、尺寸随伤害对数放大；反应名前缀为 GIC 特色保留、伤害不带负号、治疗带 +；
            // 随机偏移防同点多数字重叠）
            var reactionName = !isHeal ? ReactionNameOf(reactionKind) : null;
            int value = Mathf.Abs(displayValue);
            string text = isHeal
                ? $"+{value}"
                : reactionName != null ? $"{reactionName} {value}" : value.ToString();
            // 随机偏移防同点多数字重叠（2026-09-24 目检后用户拍板加大散布：XZ 加宽、高度带随机）
            var randomOffset = new Vector3(
                UnityEngine.Random.Range(-0.42f, 0.42f), UnityEngine.Random.Range(0.3f, 0.65f),
                UnityEngine.Random.Range(-0.15f, 0.15f));
            EnsureDamageNumbers().Spawn(view.transform.position + randomOffset, text,
                isHeal ? Palette.治疗绿 : Palette.伤害红, value, _playbackSpeed);
        }

        /// <summary>受击闪色恢复尾巴（S5：不进 playbacks=不 gate ack——Host 片节拍只等位移/伤害主体）</summary>
        private IEnumerator RestoreColorAfterFlashRoutine(UnitView view)
        {
            yield return new WaitForSeconds(0.8f / _playbackSpeed);
            if (view != null) view.RestoreColor();
        }

        /// <summary>元能命中时刻应用（2026-09-25 拍板「命中时才给」）：战技获能命令带命中毫秒——
        /// 到点再跳元能（与投射物命中表现同时刻）；0=立即不走本协程</summary>
        private IEnumerator PlayEnergyDeltaCoroutine(UnitView view, BattleCommand command)
        {
            yield return new WaitForSeconds(command.launchMs / 1000f / _playbackSpeed);
            view.ApplyEnergyDelta(command.value);
        }

        /// <summary>玩家资源命令命中时刻应用（B-3 摩拉掠夺，同元能「命中时才给」）：到点再发资源事件；
        /// 0=立即不走本协程（部署扣费/回合发放/体力恒立即）</summary>
        private IEnumerator PlayResourceDeltaCoroutine(BattleCommand command)
        {
            yield return new WaitForSeconds(command.launchMs / 1000f / _playbackSpeed);
            OnResourceDelta?.Invoke(command.targetUnitId, command.metadata, command.value);
        }

        /// <summary>原神式伤害数字层懒建（随 BattleScreen 场景卸载消亡）</summary>
        private BattleDamageNumbers EnsureDamageNumbers()
        {
            if (_damageNumbers == null)
            {
                var go = new GameObject("DamageNumbers");
                go.transform.SetParent(transform, false);
                _damageNumbers = go.AddComponent<BattleDamageNumbers>();
                _damageNumbers.Init(_viewCamera != null ? _viewCamera : Camera.main);
            }
            return _damageNumbers;
        }

        /// <summary>原神式头顶条层懒建（血条+元能条，2026-09-24；随 BattleScreen 场景卸载消亡）</summary>
        private BattleOverheadBars EnsureOverheadBars()
        {
            if (_overheadBars == null)
            {
                var go = new GameObject("OverheadBars");
                go.transform.SetParent(transform, false);
                _overheadBars = go.AddComponent<BattleOverheadBars>();
                _overheadBars.Init(_viewCamera != null ? _viewCamera : Camera.main);
            }
            return _overheadBars;
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
            _overheadBars?.ClearAll(); // 头顶条随单位同清（2026-09-24 屏幕空间层）
        }

        private void CreateView(UnitState state)
        {
            if (_views.ContainsKey(state.unitId)) return;

            // 客户端只依快照与共享配置建场（不触 Host 逻辑对象）；配置经 [Autowired] 注入（Y10）

            Sprite avatar = null;
            bool useFullBody = false;
            string displayName = state.unitId;
            TextEntry nameEntry = null;
            Sprite[] idleFrames = null;
            float idleFps = 12f;
            VideoClip idleVideo = null;
            if (_unitConfig != null && Enum.TryParse<UnitName>(state.unitName, out var unitName) &&
                _unitConfig.TryGetUnitData(unitName, out var unitData))
            {
                // 全身立牌（立牌图）人物占比小，按 Inspector 倍数整体放大；缺立牌图的单位回落头像原尺寸
                useFullBody = unitData.立牌图 != null;
                avatar = useFullBody ? unitData.立牌图 : unitData.avatar;
                displayName = unitName.ToString();
                nameEntry = unitName.GetEntry(); // 单位名本地化条目（UnitName 表）
                // 立牌循环动画（B-S3 视频路线拍板）：视频（绿幕+运行时 ChromaKey 抠色）优先于序列帧；都缺=静态兜底
                idleVideo = unitData.立牌动画视频;
                if (idleVideo == null && unitData.立牌动画帧 != null && unitData.立牌动画帧.Length > 1)
                {
                    idleFrames = unitData.立牌动画帧;
                    idleFps = unitData.立牌动画帧率;
                }
            }

            var team = (TeamType)state.team;
            var teamColor = team == TeamType.B ? Palette.敌方主色 : Palette.我方主色;
            // 血条填充=队伍色（与底座同色，2026-09-25 拍板——BattleOverheadBars 消费 TeamColor；
            // B7 联机按 viewer 归属重定时属屏幕空间层议题，Palette.血条我方绿/敌方红 字段保留备用）
            var view = UnitView.Create(_viewRoot, state.unitId, displayName, avatar, teamColor,
                _billboardRotation, 立牌后倾角, nameEntry, state.hp, state.maxHp,
                useFullBody ? 全身立牌放大倍数 : 1f, idleFrames, idleFps, idleVideo);
            view.Cell = state.position;
            view.SetCorpseVisual(state.isCorpse != 0);
            view.SetFrozenVisual(state.isFrozen != 0);
            view.SetAttachedElement((ElementType)state.dyedElement); // 附着元素（建场权威态）
            view.SetEnergy(state.energy, state.maxEnergy); // 元能（建场权威态；B6a）
            view.SetBuffs(state.buffs);
            view.ApplyPosition(_board.CellToWorld(state.position));
            _views[state.unitId] = view;
            EnsureOverheadBars().Register(view); // 原神式头顶条（血条+元能条+附着图标，2026-09-24）
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
