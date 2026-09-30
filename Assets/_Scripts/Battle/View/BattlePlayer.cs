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

        [Tooltip("全身立牌（UnitData.立牌图）放大倍数：全身立绘人物在图中占比小，放大对齐头像版人物观感（2026-09-21 先试 2.5；2026-09-27 拍板 2.5→2：站立全身图 tight bounds 撑满 1.375 格超格子、且比安柏视频立牌（主体仅占帧 69~87%）高 15~45%，降至 2 后显示高=1.1 格）；血条/名字/Buff 行尺寸不变、随立牌顶抬高；头像版（无立牌图回落 avatar）恒为原尺寸")]
        [SerializeField, Min(0.1f)] private float 全身立牌放大倍数 = 2f;

        [Header("投射物箭矢")]
        [Tooltip("箭矢屏幕长度（格）——正式素材长轴按 bounds 归一到该长度，高度随素材纵横比；十字四向全长（2026-09-28 拍板方案 A：屏幕平行布告板+屏幕平面内旋转，上下射=屏幕竖直箭；0.7→0.49=同日目检拍板调小 30%）")]
        [SerializeField, Min(0.1f)] private float 箭矢长度 = 0.49f;

        [Header("箭雨天降（arrow_rain cue；时轮特效轨→整线迸发技能视觉）")]
        [Tooltip("每格每段落箭数（「大量箭矢」密度轴）")]
        [SerializeField, Min(1)] private int 箭雨每格每段箭数 = 2;
        [Tooltip("落箭箭矢缩放（相对「箭矢长度」基准；1=与飞行箭同大）")]
        [SerializeField, Range(0.3f, 2f)] private float 箭雨箭矢缩放 = 1f;
        [Tooltip("落箭起点离落点高度（格；读作从天而降）")]
        [SerializeField, Min(0.5f)] private float 箭雨起始高度 = 2.2f;
        [Tooltip("落下倾斜角（度；0=竖直落下；正=沿安柏射向斜落（起点向安柏一侧侧移——东射自西向东落、北射自南向北落，读作安柏射出的箭坠入格内）、负=逆射向；2026-09-28 拍板「斜着落下」+追拍板「方向应当根据安柏的位置来」；侧移=tan(角)×起始高度（世界倾角），落点/落地时刻/节拍不变）")]
        [SerializeField, Range(-60f, 60f)] private float 箭雨落下倾斜角 = 30f;
        [Tooltip("单箭坠落时长（秒；起飞时刻=段时刻-坠落时长，落地与伤害数字同拍）")]
        [SerializeField, Min(0.02f)] private float 箭雨坠落时长 = 0.14f;
        [Tooltip("落点在格内的随机散布半径（格）")]
        [SerializeField, Range(0f, 0.45f)] private float 箭雨格内散布 = 0.3f;
        [Tooltip("段内每支箭的随机起飞延迟上限（秒；散开成阵雨而非整排齐落）")]
        [SerializeField, Range(0f, 0.2f)] private float 箭雨逐箭散布延迟 = 0.06f;
        [Tooltip("落地后沿箭轴向落点内压入的深度（格；尖入土、尾翘起=插在表面观感，入土段被地形深度裁掉；0=不压入）")]
        [SerializeField, Min(0f)] private float 箭雨入土深度 = 0.15f;
        [Tooltip("落地压入时长（秒；0=瞬间到位）")]
        [SerializeField, Min(0f)] private float 箭雨入土时长 = 0.05f;
        [Tooltip("落地插稳后原地滞留时长（秒；2026-09-28 拍板「箭落地后应当插在表面 3 秒」默认 3）")]
        [SerializeField, Min(0f)] private float 箭雨落地滞留 = 3f;
        [Tooltip("滞留后的淡出时长（秒）")]
        [SerializeField, Range(0f, 1f)] private float 箭雨落地淡出 = 0.18f;

        // 配色统一走 BattlePalette 配置资产（2026-09-18 统一化批次；原 _teamAColor/_teamBColor 场景序列化值
        // 与代码默认一致，迁移零损失——队伍色与 HUD 队列框/accent 同源对齐）
        private static BattlePalette Palette => BattlePalette.Instance;

        /// <summary>最新快照（HUD/血条刷新用；客户端不持逻辑状态）</summary>
        public BattleSnapshot LatestSnapshot { get; private set; }
        public float PlaybackSpeed => _playbackSpeed;
        public BattleBoard Board => _board;

        /// <summary>快照更新通知（选择阶段头 + 开局）</summary>
        public event Action<BattleSnapshot> SnapshotUpdated;

        /// <summary>片开始播放（参数=本片攻速；执行预览推进行用）。2026-09-29 语义收紧：只对
        /// **攻速行动片**发（部署段/回合结束段/即时行动块不发——它们不按攻速排程，行进推进语义
        /// 只属攻速片；旧消费方=攻速队列高亮已随队列退役）</summary>
        public event Action<int> OnSegmentPlaying;

        /// <summary>行动预告到达（2026-09-29 执行预览：Host 分桶后、首片推送前下发——
        /// 订阅方=执行预览建行；载荷自含攻速/归属/技能索引。命名同 SnapshotUpdated 模式）</summary>
        public event Action<TurnPlanMessage> TurnPlanUpdated;

        /// <summary>玩家资源增量（B6d 经济闭环：摩拉/体力 StatChange 命令消费点——
        /// 参数=玩家ID / 属性子类型（StatKindMora/StatKindStamina）/ 变化量；HUD 订阅即时刷新，
        /// 快照权威兜底）</summary>
        public event Action<string, int, int> OnResourceDelta;

        /// <summary>手牌物品牌消耗（统一消耗模型 C-1，docs/active/30）：技能吃物品（如酒/苹果）的
        /// ItemConsume 命令消费点——参数=玩家ID / 物品名（ItemName）/ 消耗量。本类先扣本地快照
        /// handCards 镜像（减尽移除条目），HUD 订阅刷新对应卡角标；快照权威兜底</summary>
        public event Action<string, int, int> OnItemConsumed;

        /// <summary>战斗结束事件（2026-09-25 三轮审查 S10 轻量全灭软停；订阅方=HUD 胜负提示，
        /// 结算画面=B8）</summary>
        public event Action<BattleOverMessage> BattleOver;

        private IBattleTransport _transport;
        private Transform _viewRoot;
        private BattleDamageNumbers _damageNumbers;
        private BattleOverheadBars _overheadBars;
        private Quaternion _billboardRotation = Quaternion.identity;
        private readonly Dictionary<string, UnitView> _views = new Dictionary<string, UnitView>();

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
            _board.BuildGrassDecor(_viewCamera); // 草簇装饰（2026-09-27 地形批次：立牌式草簇随机铺满草地格）
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

        /// <summary>行动预告到达（2026-09-29 执行预览）：转发事件——订阅方（执行预览）在执行相位
        /// 侧消费建行</summary>
        public void OnTurnPlan(TurnPlanMessage message)
        {
            TurnPlanUpdated?.Invoke(message);
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

        /// <summary>在途有意义行动计数（2026-09-30 拍板「跳过空等：只要这期间已经没有任何正在进行的
        /// 行动，包括箭矢飞行等，必须已经没有任何意义了才可跳过空等」——攻速片片头空等跳过
        /// WaitSliceDelaySkippingDeadAir 的探测源）：计数=行动类演出段未结算数量——投射物飞行
        /// （命中/消散）、箭雨单箭下落（含散布延迟，fire-and-forget=**唯一跨片在途源**——片 ack
        /// 不等它）、移动行走；结算反馈类（伤害数字飘字/元能跳变/闪色恢复/落箭插土滞留淡出）不计数
        /// （已结算信息属装饰尾巴）。片 ack 门控保证 gate 型演出在下一片到达前必然演完，故计数实际
        /// 只拦 fire-and-forget 段——gate 型行动段同样计数属防线冗余（防未来 fire-and-forget 化回归
        /// 静默破约）；PlaySkillCastVfxCoroutine 后续新增 fire-and-forget 行动类 cue 必须同步计数。</summary>
        private int _meaningfulActionInFlight;

        /// <summary>
        /// 直线投射物：箭矢从发射格飞至**命中点**（Host 接触判定千分定点下发，docs/active/22 §11——
        /// 移动中目标命中点=中途接触位置，所见即所得），到达后接伤害表现。
        /// B4 的"飞向目标当前位置"近似已废弃；无命中点数据时兜底旧行为。
        /// </summary>
        private IEnumerator PlayProjectileThenDamageCoroutine(UnitView target, BattleCommand command, float stagger)
        {
            _meaningfulActionInFlight++; // 在途有意义行动：投射物飞行（发射延迟+飞行+落地弹字）——空等跳过探测源
            try
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

                // 元素色动态染色（2026-09-28 拍板）：命中箭取 Damage 命令自带伤害元素（metadata）——与结算同源
                var arrowGo = CreateProjectileVisual(from, to,
                    ElementFactionConfig.Instance.GetElementColor((ElementType)command.metadata));

                float distance = Vector3.Distance(from, to);
                float duration = distance > 0f ? distance / (ProjectileSpeed * _playbackSpeed) : 0f;
                yield return BattleViewTween.Over(duration, t => arrowGo.transform.position = Vector3.Lerp(from, to, t));

                Destroy(arrowGo);
                yield return PlayDamageCoroutine(target, -command.value, 0f, false, command.reactionKind);
            }
            finally
            {
                _meaningfulActionInFlight--;
            }
        }

        /// <summary>
        /// 投射物消散（Effect 命令·EffectKindProjectileVanish）：无接触命中（虚空截断或 24 格上限，
        /// docs/05 §5.3）——从发射格飞至消散点（千分定点）后消失；无定点时按方向飞 value 格兜底
        /// </summary>
        private IEnumerator PlayProjectileVanishCoroutine(BattleCommand command, float stagger)
        {
            _meaningfulActionInFlight++; // 在途有意义行动：投射物飞行（发射延迟+飞行至消散）——空等跳过探测源
            try
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

                // 元素色=消散命令随带投射物元素（reactionKind，Host 与命中 Damage.metadata 同口径回填——
                // 丘丘人借凯亚霜袭时消散箭同为冰色，非施法者物理灰）
                var arrowGo = CreateProjectileVisual(from, to,
                    ElementFactionConfig.Instance.GetElementColor((ElementType)command.reactionKind));

                float distance = Vector3.Distance(from, to);
                float duration = distance > 0f ? distance / (ProjectileSpeed * _playbackSpeed) : 0f;
                yield return BattleViewTween.Over(duration, t => arrowGo.transform.position = Vector3.Lerp(from, to, t));

                Destroy(arrowGo);
            }
            finally
            {
                _meaningfulActionInFlight--;
            }
        }

        /// <summary>正式箭矢 sprite（Resources/UI/Battle/ArrowBolt——AI 生成白色箭矢，运行时元素色染色；
        /// B-S3 箭矢正式素材 2026-09-28 拍板落地。加载失败回退程序化白色光条占位）</summary>
        private static Sprite _arrowSprite;
        private static bool _arrowSpriteLoaded;
        private static Sprite ArrowSprite
        {
            get
            {
                if (!_arrowSpriteLoaded)
                {
                    _arrowSpriteLoaded = true;
                    _arrowSprite = Resources.Load<Sprite>("UI/Battle/ArrowBolt");
                    if (_arrowSprite == null)
                        GICLog.Warn("[BattlePlayer] 箭矢素材 UI/Battle/ArrowBolt 加载失败，回退白色光条占位");
                }
                return _arrowSprite;
            }
        }

        /// <summary>投射物光条 sprite（程序化 1×1 白图缓存；箭矢素材缺失时的占位兜底）</summary>
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

        /// <summary>箭矢视觉（2026-09-28 拍板方案 A：屏幕平行布告板 + 屏幕平面内旋转对齐飞行方向——
        /// 固定视角十字四向只有 0/90/180/270 四角，上下射=屏幕竖直箭、四向全长；元素色动态染色。
        /// 创建即就位，飞行由调用方 tween；占位兜底配色=BattlePalette「箭矢占位色」活色勿写字面量）</summary>
        private GameObject CreateProjectileVisual(Vector3 from, Vector3 to, Color tint)
        {
            var arrowGo = new GameObject("Projectile");
            arrowGo.transform.SetParent(_viewRoot, false);
            arrowGo.transform.position = from;

            // 朝向：先取屏幕平行布告板（面正对视线，与立牌后仰同族），再按飞行方向的屏幕投影
            // 绕视线轴旋转到四向其一（投影走 WorldToScreenPoint，不假设画布/世界轴向映射）
            var cam = _viewCamera != null ? _viewCamera : Camera.main;
            if (cam != null && (to - from).sqrMagnitude > 1e-8f)
            {
                var screenFrom = cam.WorldToScreenPoint(from);
                var screenTo = cam.WorldToScreenPoint(to);
                var angle = Mathf.Atan2(screenTo.y - screenFrom.y, screenTo.x - screenFrom.x) * Mathf.Rad2Deg;
                arrowGo.transform.rotation = cam.transform.rotation * Quaternion.Euler(0f, 0f, angle);
            }
            else
            {
                arrowGo.transform.rotation = _billboardRotation; // 无相机/零程兜底：竖立布告板
            }

            var renderer = arrowGo.AddComponent<SpriteRenderer>();
            renderer.sortingOrder = BattleMetrics.ProjectileSortingOrder;
            var sprite = ArrowSprite;
            if (sprite != null)
            {
                var scale = 箭矢长度 / sprite.bounds.size.x; // 箭矢长度=屏幕长轴全长，高度随素材纵横比
                arrowGo.transform.localScale = new Vector3(scale, scale, 1f);
                renderer.sprite = sprite;
                renderer.color = tint;
            }
            else
            {
                arrowGo.transform.localScale = new Vector3(0.05f, 0.30f, 1f); // 占位兜底：白色光条
                renderer.sprite = ProjectileSprite;
                renderer.color = Palette.箭矢占位色;
            }
            return arrowGo;
        }

        // ==================== 时轮表现轨（SkillCast 消费，B-S3 ②首个接线=2026-09-28 箭雨） ====================

        private static readonly Dictionary<int, SkillConfig> _skillConfigCache = new Dictionary<int, SkillConfig>();

        /// <summary>按 skillID 约定路径加载技能配置（Resources/Configs/Skills/{SkillName}——技能独立化约定；
        /// 缺失 Warn 一次；缓存防逐段重复加载）</summary>
        private static SkillConfig.SkillData LoadSkillData(int skillId)
        {
            if (_skillConfigCache.TryGetValue(skillId, out var config)) return config?.data;
            config = Resources.Load<SkillConfig>($"Configs/Skills/{(SkillName)skillId}");
            if (config == null)
                GICLog.Warn($"[BattlePlayer] 技能配置缺失：Configs/Skills/{(SkillName)skillId}（表现轨无源可播）");
            _skillConfigCache[skillId] = config;
            return config?.data;
        }

        /// <summary>时轮特效轨播放入口：SkillCast 命令→技能配置→时轮资产，cueName→视觉开关台
        ///（首个 cue=arrow_rain 箭雨天降；动作/音效轨素材落地后同入口扩展）。
        /// fire-and-forget 不 gate 片 ack（片节拍由 Damage 数字 launchMs 承载，同 S5 装饰尾巴口径）</summary>
        private IEnumerator PlaySkillCastVfxCoroutine(BattleCommand command)
        {
            var timeline = LoadSkillData(command.value)?.timeline;
            if (timeline == null) yield break; // 无时轮技能（移动等）——无表现轨可播，静默
            foreach (var clip in SkillTimelineQuery.ClipsOf(timeline, SkillTrackType.Vfx))
            {
                switch (clip.cueName)
                {
                    case "arrow_rain":
                        yield return PlayArrowRainCoroutine(timeline, command);
                        break;
                    // 后续特效 cue 随 B-S3 素材落地扩（音效轨 cueName=arrow_rain_release 同期接播放）
                }
            }
        }

        /// <summary>箭雨天降（arrow_rain）：判定轨 LineBurst 为节拍/射程单源——段时刻=startTime+hitInterval×段
        ///（与 CompileLineBurstSegment 同源）、整线格集=step≥1 虚空截断同口径、段数=参数表 DamageCount
        ///（数值归参数表铁律）；每段每格 N 支技能元素色箭自高处**斜落**（复用箭矢素材，起点沿安柏射向
        /// 反方向侧移=落向随安柏射向「箭雨落下倾斜角」——2026-09-28 拍板「斜着落下」+追拍板「方向根据
        /// 安柏的位置」）。
        /// 落地时刻=段时刻（与 Damage 数字同拍）——起飞提前一个坠落时长</summary>
        private IEnumerator PlayArrowRainCoroutine(SkillTimelineAsset timeline, BattleCommand command)
        {
            var burstClips = SkillTimelineQuery.JudgmentClips(timeline, SkillJudgmentKind.LineBurst);
            if (burstClips.Count == 0) yield break;
            var clip = burstClips[0];
            var skillData = LoadSkillData(command.value);
            int waves = skillData != null ? skillData.GetInt(SkillParamKey.DamageCount, 1) : 1;
            int maxRange = clip.maxRange > 0 ? clip.maxRange : ProjectileRule.MaxRange;

            var cells = new List<BattleCell>(); // 整线格集（Host CompileLineBurstSegment 同口径）
            var delta = SkillHitResolver.DirectionToDelta((Direction2D)command.direction);
            var shotDir = new Vector3(delta.x, 0f, delta.y); // 射向世界向量（落箭斜落沿它、起点向安柏一侧回撤）
            for (int step = 1; step <= maxRange; step++)
            {
                var cell = new BattleCell(command.cell.x + delta.x * step, command.cell.y + delta.y * step);
                if (_board == null || _board.Map == null || !_board.Map.HasTile(cell.x, cell.y)) break;
                cells.Add(cell);
            }
            if (cells.Count == 0) yield break;

            var tint = ElementFactionConfig.Instance.GetElementColor((ElementType)command.reactionKind);
            float prevSpawn = 0f;
            for (int wave = 0; wave < waves; wave++)
            {
                float spawnAt = Mathf.Max(0f, clip.startTime + clip.hitInterval * wave - 箭雨坠落时长);
                if (spawnAt > prevSpawn)
                {
                    yield return new WaitForSeconds((spawnAt - prevSpawn) / _playbackSpeed);
                    prevSpawn = spawnAt;
                }
                foreach (var cell in cells)
                {
                    for (int i = 0; i < 箭雨每格每段箭数; i++)
                        StartCoroutine(PlayRainArrowCoroutine(cell, shotDir, tint,
                            UnityEngine.Random.Range(0f, 箭雨逐箭散布延迟))); // 单箭 fire-and-forget（不 gate ack）
                }
            }
        }

        /// <summary>单支落箭：延迟起飞→斜落（落向沿安柏射向「箭雨落下倾斜角」）→落地沿箭轴压入插土
        ///（入土段被地形深度裁掉=尖插表面、尾翘起；水面格落点=波浪表面之上）→原地滞留（2026-09-28
        /// 拍板「插在表面 3 秒」）→淡出销毁</summary>
        private IEnumerator PlayRainArrowCoroutine(BattleCell cell, Vector3 shotDir, Color tint, float delaySeconds)
        {
            // 在途有意义行动计数（fire-and-forget 单箭=唯一跨片在途源——片 ack 不等它，下一片片头
            // 空等跳过据此探测，2026-09-30 拍板）：散布延迟+斜落段=箭矢飞行；落地即结算，
            // 插土/滞留/淡出=已结算装饰尾巴不计数
            _meaningfulActionInFlight++;
            GameObject arrowGo = null;
            Vector3 to = Vector3.zero;
            try
            {
                if (delaySeconds > 0f)
                    yield return new WaitForSeconds(delaySeconds / _playbackSpeed);

                var jitter = new Vector3(UnityEngine.Random.Range(-箭雨格内散布, 箭雨格内散布), 0f,
                    UnityEngine.Random.Range(-箭雨格内散布, 箭雨格内散布));
                var basePos = _board.CellToWorld(cell) + jitter;
                var from = basePos + new Vector3(0f, 箭雨起始高度, 0f);
                to = new Vector3(basePos.x, _board.GetVisualSurfaceHeight(cell) + 0.02f, basePos.z);
                // 斜落方向=沿安柏射向（2026-09-28 追拍板「斜落的方向应当根据安柏的位置来」）：起点沿射向
                // 反方向侧移——落箭读作安柏射出的箭越过格心继续飞行坠入该格（东射=自西侧高处向东落、
                // 北射=自南侧高处向北落，起点天然在安柏一侧）；侧移量=tan(倾斜角)×起始高度（世界倾角）；
                // 落点/落地时刻/节拍全不变（纯视觉，判定无涉）
                if (shotDir.sqrMagnitude > 1e-6f && 箭雨落下倾斜角 != 0f)
                {
                    from -= shotDir * (Mathf.Tan(箭雨落下倾斜角 * Mathf.Deg2Rad) * 箭雨起始高度);
                }
                arrowGo = CreateProjectileVisual(from, to, tint);
                arrowGo.transform.localScale *= 箭雨箭矢缩放;

                yield return BattleViewTween.Over(箭雨坠落时长 / _playbackSpeed,
                    t => { if (arrowGo != null) arrowGo.transform.position = Vector3.Lerp(from, to, t); });
            }
            finally
            {
                _meaningfulActionInFlight--; // 落地/中断都结算——插土滞留淡出段不阻下一片空等跳过
            }

            // 落地插土：沿箭轴向落点内压入「箭雨入土深度」——屏幕平面布告板，入土段被地形深度裁掉
            //（尖插表面、尾翘起；水面格插在波浪表面，透明水体不裁=透水可见属预期）
            if (arrowGo != null && 箭雨入土深度 > 0f)
            {
                var stuckTo = to + arrowGo.transform.right * 箭雨入土深度; // transform.right=屏面内飞行方向（尖朝前）
                yield return BattleViewTween.Over(箭雨入土时长 / _playbackSpeed,
                    t => { if (arrowGo != null) arrowGo.transform.position = Vector3.Lerp(to, stuckTo, t); });
            }

            if (箭雨落地滞留 > 0f)
                yield return new WaitForSeconds(箭雨落地滞留 / _playbackSpeed);

            var renderer = arrowGo != null ? arrowGo.GetComponent<SpriteRenderer>() : null;
            if (renderer != null && 箭雨落地淡出 > 0f)
            {
                var color = renderer.color;
                yield return BattleViewTween.Over(箭雨落地淡出 / _playbackSpeed, t =>
                {
                    if (renderer != null)
                    {
                        var c = color;
                        c.a = color.a * (1f - t);
                        renderer.color = c;
                    }
                });
            }
            if (arrowGo != null) Destroy(arrowGo);
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
            {
                // 空等跳过（2026-09-30 拍板「凯亚行动完后芭芭拉空等 3 秒，应当跳过空等时间——只要
                // 这期间已经没有任何正在进行的行动（含箭矢飞行等）才可跳过」——docs/18 L44 既有
                ///「衍生事件提早完成即跳过剩余等待」语义的落地）：攻速片片头等待逐帧探测在途有意义
                // 行动，无在途即提前进片；部署/回合结束/即时块固定短拍不参与跳过
                if (segment.turnEnd == 0 && segment.deploy == 0 && segment.insertedInstantAction == 0)
                    yield return WaitSliceDelaySkippingDeadAir(delay / _playbackSpeed);
                else
                    yield return new WaitForSeconds(delay / _playbackSpeed);
            }

            // 只对攻速行动片发（2026-09-29 语义收紧：部署/回合结束/即时行动块不按攻速排程——
            // 执行预览的行进推进只属攻速片；旧消费方攻速队列已随队列退役）
            if (segment.turnEnd == 0 && segment.deploy == 0 && segment.insertedInstantAction == 0)
                OnSegmentPlaying?.Invoke(segment.sliceAttackSpeed);

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
                            {
                                // 直击数字节拍（2026-09-28 对齐 Heal 分支口径）：launchMs>0=时轮段时刻到点再弹
                                //（箭雨 4 段 0.15s 间隔与落箭同拍连续弹）；0=立即维持命令 stagger 旧节拍
                                //（霜袭等 startTime=0 技能与无时轮兜底路径不变）
                                float dmgDelay = command.launchMs > 0
                                    ? command.launchMs / 1000f / _playbackSpeed
                                    : stagger;
                                playbacks.Add(StartCoroutine(PlayDamageCoroutine(target, -command.value, dmgDelay, false,
                                    command.reactionKind)));
                            }
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

                    case BattleCommandType.Revive:
                        // 复苏（B-3 ②，芭芭拉闪耀奇迹——docs/05 §5.4「血量永远0不复苏」唯一例外）：
                        // 解灰+底座还原+视频恢复（SetCorpseVisual 原路反转）+ 复活血量与 +N 治疗数字
                        // 复用 Heal 链（PlayDamageCoroutine isHeal——含 ApplyHpDelta 即时反馈）
                        if (_views.TryGetValue(command.targetUnitId, out var revived))
                        {
                            revived.SetCorpseVisual(false);
                            playbacks.Add(StartCoroutine(PlayDamageCoroutine(revived, command.value, stagger, true)));
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

                    case BattleCommandType.ItemConsume:
                        // 物品消耗（统一消耗模型 C-1）：先扣本地 handCards 镜像（减尽移除条目），再广播
                        // HUD 刷新角标；下回合快照权威兜底（镜像偏离自愈）
                        ApplyItemConsumeToLocalHand(command.targetUnitId, (ItemName)command.metadata, command.value);
                        OnItemConsumed?.Invoke(command.targetUnitId, command.metadata, command.value);
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
                        // 时轮演出起点事件（B-S1）：按 skillID 加载时轮资产播动作/音效/特效轨
                        // ——特效轨首个消费方=arrow_rain 箭雨天降（2026-09-28），动作/音效轨待素材同入口扩展；
                        // fire-and-forget 不进 playbacks（不 gate 片 ack，节拍由 Damage launchMs 承载）；
                        // 前摇期投射物视觉由投射物命令的 launchMs 延迟起飞承载（施放者动作/音效待素材批次）
                        GICLog.Info($"[BattlePlayer] 技能施放：{command.actorUnitId} → {(SkillName)command.value}" +
                                    $" 方向 {(Direction2D)command.direction}");
                        if (_views.TryGetValue(command.actorUnitId, out var caster))
                            caster.SetFacing(IsLeftFacing(command.direction)); // 立牌朝向随施放方向（拍板③：向左射箭→转向左）
                        StartCoroutine(PlaySkillCastVfxCoroutine(command));
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

        /// <summary>攻速片片头等待——空等跳过（2026-09-30 拍板，docs/18「回合流程重排+攻速延迟执行模型」
        /// 既有「行动及其衍生事件提早完成时，后续行动跳过剩余等待立即执行」语义的落地）：期间逐帧探测
        /// 在途有意义行动计数（_meaningfulActionInFlight——箭矢飞行/箭雨下落/移动行走未结算）——
        /// &gt;0 则继续等（等到其在途结算即止，**剩余空等不再补满**——空等只承担攻速节奏提示）；
        /// ==0 立即进片。片 ack 门控保证 gate 型演出在下一片到达前演完，实际拦的只有 fire-and-forget
        /// 行动段（箭雨单箭）；已结算装饰尾巴（飘字/插箭滞留淡出/闪色恢复）不计数不阻跳过。</summary>
        private IEnumerator WaitSliceDelaySkippingDeadAir(float scaledDelay)
        {
            float elapsed = 0f;
            while (elapsed < scaledDelay && _meaningfulActionInFlight > 0)
            {
                yield return null;
                elapsed += Time.deltaTime;
            }
        }

        /// <summary>行动方向→立牌朝向（2026-09-29 拍板③：方向 ∈ {上,左上,左,左下} 朝左水平镜像，其余朝右——
        /// 素材统一朝右单份复用；朝向行动后保持、待机延续；后续背后刺杀类技能可读 UnitView.FaceLeft）</summary>
        private static bool IsLeftFacing(int direction)
        {
            return direction == (int)Direction2D.Up || direction == (int)Direction2D.UpLeft
                || direction == (int)Direction2D.Left || direction == (int)Direction2D.DownLeft;
        }

        private IEnumerator PlayMoveCoroutine(UnitView view, BattleCommand command)
        {
            // 移动态动画（B-S4a，2026-09-29 拍板「正式化安柏待机+移动动画」）：移动片内切 移动动画视频、
            // 片末回 待机（含被挡弹回段——弹回也是移动表现）；try/finally 保异常不滞留移动态
            view.SetFacing(IsLeftFacing(command.direction)); // 立牌朝向随移动方向（拍板③）；行动后保持=待机延续
            view.SetMoveAnimation(true);
            _meaningfulActionInFlight++; // 在途有意义行动：移动行走（含被挡弹回段）——空等跳过探测源
            try
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
            finally
            {
                view.SetMoveAnimation(false);
                _meaningfulActionInFlight--; // 移动结算（正常/中断同收）
            }
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

        /// <summary>物品消耗命令扣本地手牌镜像（统一消耗模型 C-1）：LatestSnapshot.resources 内该玩家
        /// handCards 条目数量减/减尽移除——片内即时反映（HUD 角标刷新走 OnItemConsumed 事件）；
        /// 非我方玩家命令仅镜像维护（HUD 只刷我方手牌），下回合快照权威兜底</summary>
        private void ApplyItemConsumeToLocalHand(string playerId, ItemName item, int amount)
        {
            var snapshot = LatestSnapshot;
            if (snapshot == null) return;
            PlayerResourceState res = null;
            foreach (var r in snapshot.resources)
            {
                if (r.playerId == playerId) { res = r; break; }
            }
            if (res == null) return;
            HandCard target = null;
            foreach (var entry in res.handCards)
            {
                if ((CardType)entry.cardType == CardType.Item && entry.value == (int)item) { target = entry; break; }
            }
            if (target == null)
            {
                GICLog.Warn($"[BattlePlayer] ItemConsume 镜像无条目（{playerId} {item}×{amount}）——快照权威自愈兜底");
                return;
            }
            target.count -= amount;
            if (target.count <= 0)
                res.handCards.Remove(target);
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
            VideoClip moveVideo = null; // 移动态动画（B-S4a，2026-09-29 拍板「正式化安柏待机+移动动画」）
            float unitScale = 1f;   // UnitData.额外缩放（2026-09-27 拍板：乘在全身立牌放大倍数之上，1=不缩放）
            float hoverHeight = 0f; // UnitData.离地高度（2026-09-27 拍板：飞行/悬浮单位纸片人整体上浮）
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
                moveVideo = unitData.移动动画视频; // 移动态片（null=移动期间照播待机=旧行为）
                if (idleVideo == null && unitData.立牌动画帧 != null && unitData.立牌动画帧.Length > 1)
                {
                    idleFrames = unitData.立牌动画帧;
                    idleFps = unitData.立牌动画帧率;
                }
                unitScale = unitData.额外缩放;
                hoverHeight = unitData.离地高度;
            }

            var team = (TeamType)state.team;
            var teamColor = team == TeamType.B ? Palette.敌方主色 : Palette.我方主色;
            // 血条填充=队伍色（与底座同色，2026-09-25 拍板——BattleOverheadBars 消费 TeamColor；
            // B7 联机按 viewer 归属重定时属屏幕空间层议题，Palette.血条我方绿/敌方红 字段保留备用）
            var view = UnitView.Create(_viewRoot, state.unitId, displayName, avatar, teamColor,
                _billboardRotation, 立牌后倾角, nameEntry, state.hp, state.maxHp,
                (useFullBody ? 全身立牌放大倍数 : 1f) * unitScale, idleFrames, idleFps, idleVideo, moveVideo, hoverHeight,
                state.cylinderDiameter); // per-unit 受击圆柱直径（协议核心批：快照真源与 Host 判定同源，0=回落全局 0.42）
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
