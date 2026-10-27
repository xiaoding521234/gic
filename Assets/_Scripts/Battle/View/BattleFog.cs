using System.Collections.Generic;
using UnityEngine;
using GIC.Data;
using GIC.Framework;

namespace GIC.Battle
{


    /// <summary>
    /// 战场迷雾控制器（2026-10-17 迷雾批，docs/active/44）——**纯表现层系统**：
    /// 客户端本地计算本方阵营探索集，Host 模拟/协议/快照零感知（LAN 各端独立计算=天然各自视角）。
    ///
    /// 探索集（单调有进无出，docs/03 §3.6「探索过永久可见」）：以本方阵营**存活单位**（含协议核心等
    /// 建筑；尸体不探索）的**当前视觉位置**为心、探索视野（UnitData.探索视野，切比雪夫，**回落 1**
    /// 〔2026-10-17 追拍默认 2→1；安柏/协议核心=2 走资产〕）逐帧
    /// 画圈标记——移动播放途中按视觉位置即时揭雾（走一路揭一路）；队友单位共享揭雾（TeamType 聚合）。
    ///
    /// 两种迷雾（BattleFogMode）：Skirmish=半透明雾面（地形完整可见）+敌方单位隐藏；
    /// Mission=不透明混沌盖（地形类型零泄露，虚空同罩）+敌方单位隐藏。渲染归 BattleBoard
    /// （RebuildSkirmishFog/RebuildMissionFog/RebuildGrassDecor——**两模式同过滤草簇**：草簇是
    /// 立牌式竖直面，穿出贴地雾面在未探索格漏亮，2026-10-17 追拍四修复），本控制器只管探索集、
    /// 脏检查与消费方 API。
    ///
    /// 单位隐藏：敌方单位所在格未探索=UnitView.SetFogHidden（渲染件意图态覆盖层）+头顶条同步自隐；
    /// 我方/队友恒可见。**单位类信息零泄露**（用户拍板 D4）：瞄准候选/预判描边/推荐色/执行预览行
    /// 等规划期消费方一律按 IsUnitVisible 过滤——地形类信息（移动路径绕行/部署合法性）允许泄露。
    /// **执行期反馈按归属方**（2026-10-17 追拍二）：敌方施放的世界特效（投射物/箭雨/落地箭——
    /// RegisterProjectileVisual 注册逐格门控，飞到已探索格才显示）与敌方自增益数字隐藏；
    /// 我方特效恒显（我方箭矢全程可见、**我方命中的伤害数字不隐藏**——即便目标在迷雾内）。
    /// 资源点（2026-10-28 返拍「摩拉堆在迷雾中应当隐藏」）：未探索格堆件整体隐藏
    /// （EvaluateResourceNodeVisibility——渲染层；采集角色的资源点透视=AI 感知层豁免，
    /// CompanionBrain 采集候选不查探索账本，核心透视同构，两层分口径勿混）。
    /// </summary>
    public class BattleFog : MonoBehaviour
    {
        private BattleFogMode _mode = BattleFogMode.None;
        private BattleBoard _board;
        private BattlePlayer _player;
        private System.Func<string> _localPlayerIdResolver;
        private System.Func<string, GIC.Data.TeamType> _teamOfResolver;

        private bool[] _explored;          // 探索集（行优先 width*height；单调有进无出）
        private bool _dirty;               // 探索集变化 → 重建雾面/混沌盖/草簇
        private float _dirtyAge;           // #1 审查批：脏龄（秒）——重建合并间隔节流用

        [Tooltip("#1 审查批：探索集变化的重建合并间隔（秒）——移动期多单位交错揭格原会逐事件全量重建雾面+草簇（多单位同拍移动≈每帧重建的卡顿源），按本间隔合并；揭格随移动渐进、100ms 粒度肉眼不可辨；0=立即（旧行为）")]
        [SerializeField] private float 迷雾重建合并间隔 = 0.1f;

        /// <summary>#1 审查批：未探索格列表缓冲复用（ApplyFogChanges 每次全图扫描产物）</summary>
        private readonly List<BattleCell> _unexploredBuffer = new List<BattleCell>();

        private string _myPlayerId;        // 本端玩家（SelfPlayerID→P1 回落，BattleHud 同口径）
        private GIC.Data.TeamType _myTeam = GIC.Data.TeamType.A;
        private bool _identityResolved;

        /// <summary>unitId → 探索视野缓存（快照 unitName→UnitConfig 解析，缺配置回落 1）</summary>
        private readonly Dictionary<string, int> _visionByUnit = new Dictionary<string, int>();

        /// <summary>视野缓存对应的快照代（换代即失效——见 Update；追拍六：升命 1命 C1Vision 中途
        /// 生效/新召唤单位次快照补齐均自愈）</summary>
        private BattleSnapshot _lastVisionSnapshot;

        /// <summary>敌方特效门控注册表（2026-10-17 追拍二「敌方安柏射出的箭矢应当只有飞到已探索格
        /// 才显示」）：CreateProjectileVisual 全箭矢工厂单点注册；对象销毁按 Unity null 探测自动剪除
        /// （无需显式反注册）</summary>
        private readonly List<ProjectileFogGate> _projectiles = new List<ProjectileFogGate>();

        private class ProjectileFogGate
        {
            public GameObject Go;
            public Renderer[] Renderers;   // 注册时缓存（箭矢=单 MeshRenderer；缓存防逐帧 GetComponents）
            public bool EnemyOwned;
            public bool Hidden;
        }

        [Autowired] private UnitConfig _unitConfig;

        /// <summary>迷雾激活（None=直通：全图可见、零隐藏）</summary>
        public bool Active => _mode != BattleFogMode.None && _explored != null;

        public BattleFogMode Mode => _mode;

        /// <summary>初始化（BattlePlayer.EnsureFog 建件后由 ConfigureFog 注入；board/player 引用随建盘就位）。
        /// 身份解析器惰性求值（ConfigureFog 早于 session 创建——lambda 捕获字段延后取值）</summary>
        public void Init(BattleFogMode mode, BattleBoard board, BattlePlayer player,
            System.Func<string> localPlayerIdResolver, System.Func<string, GIC.Data.TeamType> teamOfResolver)
        {
            _mode = mode;
            _board = board;
            _player = player;
            _localPlayerIdResolver = localPlayerIdResolver;
            _teamOfResolver = teamOfResolver;
            Wargame.Instance?.Context?.Inject(this); // 运行时建件不走场景注入扫描（docs/14 §78）
            _identityResolved = false;
        }

        /// <summary>建盘钩子（BattlePlayer.OnBattleStart）：新战斗探索集归零+立即评估——
        /// 初始态同步落地（核心单位周界揭雾），避免首帧全雾闪烁</summary>
        public void OnBattleBuilt()
        {
            if (_board == null || _board.Map == null) return;
            _identityResolved = false;
            _visionByUnit.Clear();
            _lastVisionSnapshot = _player != null ? _player.LatestSnapshot : null;
            _explored = new bool[_board.Map.width * _board.Map.height];
            _dirty = true; // 空探索集也重建（全图雾）→ 随后 EvaluateReveal 即时揭初始区
            ResolveIdentity();
            if (!_identityResolved) return; // 身份未就绪——Update 循环解析成功后补齐首评估
            EvaluateReveal();
            LogResolvedVisions(); // 揭雾视野诊断（开局一次——「配置值≠实际揭雾半径」分歧排查锚点，追拍六）
            ApplyFogChanges();
            EvaluateUnitHiding();
            EvaluateResourceNodeVisibility();
        }

        /// <summary>清场钩子（BattlePlayer.ClearViews）：探索集/雾面/隐藏态/特效注册表全清</summary>
        public void Clear()
        {
            _explored = null;
            _visionByUnit.Clear();
            _projectiles.Clear();
            _dirty = false;
            if (_board != null) _board.ClearFogOverlay();
        }

        private void Update()
        {
            if (_mode == BattleFogMode.None || _board == null || _board.Map == null || _player == null) return;
            if (_explored == null) return;

            ResolveIdentity();
            if (!_identityResolved) return; // 身份未就绪勿用默认队 A 评估（LAN 客户端短暂误判防线）
            // 快照换代=视野缓存失效（追拍六：升命 1命 C1Vision 当回合快照起生效/新召唤单位次快照补齐）
            if (!ReferenceEquals(_player.LatestSnapshot, _lastVisionSnapshot))
            {
                _lastVisionSnapshot = _player.LatestSnapshot;
                _visionByUnit.Clear();
            }
            EvaluateReveal();
            // #1 审查批节流：探索集变化按「迷雾重建合并间隔」合并重建（移动期多单位交错揭格
            // 原逐事件触发雾面+草簇全量重建；节流后重建频率上限 1/间隔，配合 mesh 复用消卡顿）
            if (_dirty)
            {
                _dirtyAge += Time.deltaTime;
                if (_dirtyAge >= 迷雾重建合并间隔)
                    ApplyFogChanges();
            }
            else _dirtyAge = 0f;
            EvaluateUnitHiding();
            EvaluateResourceNodeVisibility();
            EvaluateProjectileFog();
        }

        // ==================== 消费方 API ====================

        /// <summary>该格是否已探索（未激活/越界=true 直通——越界格不参与雾语义）</summary>
        public bool IsExplored(int x, int y)
        {
            if (!Active || _board == null || _board.Map == null) return true;
            if (!_board.Map.InBounds(x, y)) return true;
            return _explored[y * _board.Map.width + x];
        }

        public bool IsExplored(BattleCell cell) => IsExplored(cell.x, cell.y);

        /// <summary>该单位对本端是否可见（迷雾过滤单出口）：我方/队友恒可见；敌方=所在格已探索；
        /// 视野暴露的敌方恒可见（追拍七「无法隐身」+追拍八「所有人可见」——瞄准/预判等规划期消费方
        /// 同享现身）。未激活=true 全可见。消费方=瞄准候选/预判描边/执行预览行（规划期单位类零泄露口径）</summary>
        public bool IsUnitVisible(UnitState unit)
        {
            if (unit == null || !Active) return true;
            if ((GIC.Data.TeamType)unit.team == _myTeam) return true;
            if (_player != null && _player.Views.TryGetValue(unit.unitId, out var view)
                && IsExposed(view)) return true;
            return IsExplored(unit.position.x, unit.position.y);
        }

        /// <summary>该 unitId 是否敌方（本端视角；视图查无=按非敌方处理=不隐藏——防误吞我方反馈）。
        /// 消费方=伤害数字归属方判定（追拍二：敌方施放且目标隐藏才隐藏数字，我方命中恒显）</summary>
        public bool IsEnemyActor(string unitId)
        {
            if (!Active || _player == null || string.IsNullOrEmpty(unitId)) return false;
            foreach (var v in _player.Views.Values)
                if (v != null && v.UnitId == unitId)
                    return v.OwnerTeam != _myTeam;
            return false;
        }

        /// <summary>敌方特效门控注册（CreateProjectileVisual 全箭矢工厂单点调用）：ownerTeam=施法者
        /// 阵营——我方/队友=恒显零门控；敌方=逐帧按当前位置所在格探索态显隐（飞到已探索格才显示，
        /// 追拍二）。对象销毁由 EvaluateProjectileFog 的 null 探测自动剪除</summary>
        public void RegisterProjectileVisual(GameObject go, GIC.Data.TeamType ownerTeam)
        {
            if (!Active || go == null) return;
            if (ownerTeam == _myTeam) return; // 我方特效恒显（拍板「我方安柏的箭矢不用隐藏」）
            _projectiles.Add(new ProjectileFogGate
            {
                Go = go,
                Renderers = go.GetComponentsInChildren<Renderer>(),
                EnemyOwned = true,
                Hidden = false,
            });
        }

        /// <summary>移除箭矢的迷雾门控（2026-10-20 审查批#15 箭矢池化）：回池箭复用时会重新注册，
        /// 陈旧门控须随回池注销——null 探测只清已销毁对象、不清失活池件（渲染器复位由
        /// CreateProjectileVisual 池件出仓时重置 enabled）</summary>
        public void UnregisterProjectileVisual(GameObject go)
        {
            if (go == null) return;
            for (int i = _projectiles.Count - 1; i >= 0; i--)
                if (_projectiles[i].Go == go)
                    _projectiles.RemoveAt(i);
        }

        /// <summary>外部批量揭雾（安柏 2命「爆发破雾」客户端表现，docs/active/44 §9-②——与 Host
        /// CompileBurstFogBreak 同口径线域）：标记探索集+置脏（下一帧重建雾面/混沌盖/草簇）。
        /// 未激活/越界幂等直通；单调有进无出与常规揭雾同账本</summary>
        public void RevealCells(IEnumerable<BattleCell> cells)
        {
            if (!Active || _board == null || _board.Map == null || cells == null) return;
            var map = _board.Map;
            foreach (var c in cells)
            {
                if (!map.InBounds(c.x, c.y)) continue;
                int idx = c.y * map.width + c.x;
                if (!_explored[idx]) { _explored[idx] = true; _dirty = true; }
            }
        }

        /// <summary>该施法者的爆发是否应在本端揭雾（2命破雾客户端门）：迷雾激活+**我方/队友**施放
        /// （敌方爆发破的是敌方的雾——LAN 各端各自视角）+快照命座≥2。能量结算=Host 权威（元能效应
        /// 随命令到点跳变），本端只管表现层；Talent 参数门在 Host 编译侧（数据驱动），客户端按命座
        /// 近似（当前配此参数族的唯安柏——零误报面）</summary>
        public bool ShouldRevealBurstFog(string actorUnitId)
        {
            if (!Active || IsEnemyActor(actorUnitId)) return false;
            var snapshot = _player != null ? _player.LatestSnapshot : null;
            if (snapshot?.units == null) return false;
            foreach (var u in snapshot.units)
            {
                if (u == null || u.unitId != actorUnitId) continue;
                return u.constellation >= 2;
            }
            return false;
        }

        /// <summary>召唤登场钩（BattlePlayer Summon 命令位调用，追拍七）：①视野缓存预热——召唤单位
        /// 不在当前快照（次快照才收录），直接吃命令携带的 exploreVision（BuildUnitState 已填），
        /// 防「部署先揭半径 1（回落值）次回合才扩到半径 2」；②敌方立即隐藏——不等下一帧
        /// EvaluateUnitHiding（建件到逐帧评估间的渲染帧=「闪一下才隐藏」报障根因）</summary>
        public void OnUnitSummoned(UnitState state)
        {
            if (!Active || state == null) return;
            if (state.isSoul != 0) return; // 灵魂在地脉维度（决策九十一）：不入雾域——灵界视角恒显、不揭雾不被雾隐藏
            if (state.exploreVision > 0)
                _visionByUnit[state.unitId] = state.exploreVision;
            else if (_unitConfig != null && System.Enum.TryParse<UnitName>(state.unitName, out var name)
                && _unitConfig.TryGetUnitData(name, out var data))
                _visionByUnit[state.unitId] = data.GetEffectiveExploreVision();
            if (_player != null && _player.Views.TryGetValue(state.unitId, out var view) && view != null)
                view.SetFogHidden(view.OwnerTeam != _myTeam
                    && !IsExposed(view)
                    && !IsExplored(_board.WorldToCell(view.transform.position)));
        }

        /// <summary>该敌方单位是否处于视野暴露（安柏变奏「全面侦查」，追拍七；追拍八拍板「解除隐身
        /// 应当是清除掉这个buff，因此是所有人可见」）：**持有即对所有人可见**（不按施加者阵营过滤——
        /// 敌方给自己人上暴露，本端同样看得见）。按视图徽章判（UnitView.Buffs=快照权威+ApplyBuffBadge
        /// 命令流增量，施加即生效）</summary>
        private bool IsExposed(UnitView view)
        {
            if (view == null || view.Buffs == null) return false;
            foreach (var b in view.Buffs)
                if (b != null && b.type == (int)BuffType.VisionExposed) return true;
            return false;
        }

        /// <summary>该敌方单位的视野暴露是否由本方阵营施加（**仅「所在格随行揭雾」trail 消费**——
        /// 格子探索情报=施加者阵营独享非全队公开，与单位现身的「所有人可见」分口径，追拍八）</summary>
        private bool IsExposedToMyTeam(UnitView view)
        {
            if (view == null || view.Buffs == null) return false;
            foreach (var b in view.Buffs)
            {
                if (b == null || b.type != (int)BuffType.VisionExposed) continue;
                if (string.IsNullOrEmpty(b.sourceUnitId)) continue;
                if (_player != null && _player.Views.TryGetValue(b.sourceUnitId, out var src)
                    && src != null && src.OwnerTeam == _myTeam)
                    return true;
            }
            return false;
        }

        // ==================== 内部：身份/揭雾/重建/隐藏 ====================

        private void ResolveIdentity()
        {
            if (_identityResolved) return;
            var id = _localPlayerIdResolver != null ? _localPlayerIdResolver() : null;
            if (string.IsNullOrEmpty(id)) return; // PlayerManager 未就绪——下帧再试
            _myPlayerId = id;
            _myTeam = _teamOfResolver != null ? _teamOfResolver(id) : GIC.Data.TeamType.A;
            _identityResolved = true;
        }

        /// <summary>揭雾评估（每帧）：本方存活单位（含建筑；尸体不探索）按**视觉位置**画圈标记探索集。
        /// 移动播放途中即时揭雾；标记单调有进无出</summary>
        private void EvaluateReveal()
        {
            var views = _player.Views;
            if (views == null || views.Count == 0) return;
            var map = _board.Map;
            foreach (var view in views.Values)
            {
                if (view == null || view.IsCorpse) continue;
                if (view.IsSoulView) continue; // 灵魂不揭雾（决策九十一：地脉驻留体无探索语义——灵界透视）
                if (view.OwnerTeam != _myTeam) continue; // 敌方不为本端揭雾

                int vision = ResolveVision(view.UnitId);
                if (vision <= 0) continue;
                var cell = _board.WorldToCell(view.transform.position); // 视觉位置（移动途中随行揭雾）
                int r = vision;
                for (int dy = -r; dy <= r; dy++)
                {
                    int y = cell.y + dy;
                    if (y < 0 || y >= map.height) continue;
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int x = cell.x + dx;
                        if (x < 0 || x >= map.width) continue;
                        int idx = y * map.width + x;
                        if (!_explored[idx]) { _explored[idx] = true; _dirty = true; }
                    }
                }
            }

            // 视野暴露 trail（追拍七，安柏变奏「全面侦查」）：被**本方施加**视野暴露的敌方——所在格
            // 随行揭雾（**格子探索情报=施加者阵营独享**非全队公开——与单位现身的「所有人可见」分口径，
            // 追拍八；徽章数据=快照权威+ApplyBuffBadge 命令流增量，施加即生效）
            foreach (var view in views.Values)
            {
                if (view == null || view.IsCorpse || view.OwnerTeam == _myTeam) continue;
                if (!IsExposedToMyTeam(view)) continue;
                var cell = _board.WorldToCell(view.transform.position);
                if (!map.InBounds(cell.x, cell.y)) continue;
                int idx = cell.y * map.width + cell.x;
                if (!_explored[idx]) { _explored[idx] = true; _dirty = true; }
            }
        }

        /// <summary>单位探索视野解析（缓存，追拍六「1命视野真源化」）：**首选快照 exploreVision**
        /// （Host 从 UnitStats.VisionRange 终值填充=config 基值+1命 StatBoost——升命中途生效随快照
        /// 到达，docs/active/44 §9-③）；0=未填（旧端/手工构造态）回落旧链（快照 unitName→UnitConfig
        /// 基值，回落 1）。缓存随快照换代整体失效（Update 口）——新召唤单位/升命视野变化自愈</summary>
        private int ResolveVision(string unitId)
        {
            if (_visionByUnit.TryGetValue(unitId, out int cached)) return cached;
            int vision = 1;
            var snapshot = _player != null ? _player.LatestSnapshot : null;
            if (snapshot?.units != null)
            {
                foreach (var u in snapshot.units)
                {
                    if (u == null || u.unitId != unitId) continue;
                    if (u.exploreVision > 0)
                        vision = u.exploreVision;
                    else if (_unitConfig != null
                        && System.Enum.TryParse<UnitName>(u.unitName, out var name)
                        && _unitConfig.TryGetUnitData(name, out var data))
                        vision = data.GetEffectiveExploreVision();
                    break;
                }
            }
            _visionByUnit[unitId] = vision;
            return vision;
        }

        /// <summary>开局诊断（追拍六加）：本方各单位揭雾视野解析值一行入控制台——「配置值≠实际揭雾
        /// 半径」类分歧直接对答案（如 [迷雾] 揭雾视野: Amber=3 ProtocolCore=2）；每场战斗一次</summary>
        private void LogResolvedVisions()
        {
            var snapshot = _player != null ? _player.LatestSnapshot : null;
            if (snapshot?.units == null) return;
            var sb = new System.Text.StringBuilder();
            foreach (var u in snapshot.MainWorldUnits()) // 主世界域单源（②④迁移：灵魂不揭雾不入诊断）
            {
                if (u == null || (TeamType)u.team != _myTeam || u.isCorpse != 0) continue;
                sb.Append(' ').Append(u.unitName).Append('=').Append(ResolveVision(u.unitId));
            }
            if (sb.Length > 0) GICLog.Info("[迷雾] 揭雾视野:" + sb);
        }

        /// <summary>探索集变化落地：重建雾面（Skirmish）/混沌盖（Mission）+草簇探索过滤（两模式同过滤
        /// ——追拍四：草簇是立牌式竖直面，穿出贴地雾面在未探索格漏亮）</summary>
        private void ApplyFogChanges()
        {
            _dirty = false;
            _dirtyAge = 0f; // #1 审查批：节流脏龄归零
            var map = _board.Map;
            var unexplored = _unexploredBuffer; // 缓冲复用（#1 审查批：原每次 new List）
            unexplored.Clear();
            for (int y = 0; y < map.height; y++)
                for (int x = 0; x < map.width; x++)
                    if (!_explored[y * map.width + x])
                        unexplored.Add(new BattleCell(x, y));

            if (_mode == BattleFogMode.Skirmish)
            {
                // 对战迷雾：雾面只盖实格（虚空豁口正常可见——拍板 D6）
                _board.RebuildSkirmishFog(unexplored);
            }
            else
            {
                // 任务迷雾：混沌盖含虚空格（未知区域浑然一体不泄露地图轮廓）
                _board.RebuildMissionFog(unexplored);
            }
            // 草簇探索过滤（两模式同款）：未探索格不生成草簇——竖直草尖穿出贴地雾面=雾里漏亮（追拍四）
            bool[] explored = _explored;
            _board.RebuildGrassDecor((x, y) => explored[y * map.width + x], _player.ViewCamera);
        }

        /// <summary>单位隐藏评估（每帧）：敌方单位所在格（视觉位置）未探索=隐藏。走进已探索区现身、
        /// 走进迷雾消失；我方/队友恒可见（SetFogHidden 幂等直通）。视野暴露的敌方不隐藏（追拍七
        /// 「解除隐身并无法隐身」+追拍八「所有人可见」）</summary>
        private void EvaluateUnitHiding()
        {
            var views = _player.Views;
            if (views == null) return;
            foreach (var view in views.Values)
            {
                if (view == null) continue;
                if (view.IsSoulView) { view.SetFogHidden(false); continue; } // 灵魂永不雾隐（决策九十一：地脉视角可见性与雾无关；防御复位）
                bool hidden = view.OwnerTeam != _myTeam
                    && !IsExposed(view) // 视野暴露（追拍八）：解除隐身=所有人可见
                    && !IsExplored(_board.WorldToCell(view.transform.position));
                view.SetFogHidden(hidden);
            }
        }

        /// <summary>资源点隐藏评估（每帧，2026-10-28 返修拍板「摩拉堆在迷雾中应当隐藏」）：未探索格
        /// 堆件整体隐藏——探索过永久可见（探索集单调，与单位口径一致）；采集角色透视=AI 感知层
        /// 豁免（CompanionBrain 采集候选不查探索账本——核心透视 IsUnitVisibleTo 同构），客户端
        /// 渲染无透视（透视≠揭雾：堆可见不揭格）。幂等直通（BattleBoard 侧隐藏态缓存零开销）</summary>
        private void EvaluateResourceNodeVisibility()
        {
            if (_board == null) return;
            _board.ApplyResourceNodeFogVisibility(cell => IsExplored(cell));
        }

        /// <summary>敌方特效门控评估（每帧，追拍二）：注册表内敌方特效按**当前位置**所在格探索态显隐
        /// ——箭矢飞进已探索格即显示、飞回迷雾即隐藏（逐格平滑跟随）；销毁对象 null 探测剪除。
        /// 我方特效不注册（恒显）</summary>
        private void EvaluateProjectileFog()
        {
            if (_projectiles.Count == 0) return;
            for (int i = _projectiles.Count - 1; i >= 0; i--)
            {
                var p = _projectiles[i];
                if (p.Go == null) { _projectiles.RemoveAt(i); continue; } // 已销毁（Destroy 后 Unity null）
                if (p.Renderers == null) continue;
                bool hidden = !IsExplored(_board.WorldToCell(p.Go.transform.position));
                if (hidden == p.Hidden) continue; // 幂等（状态未变零开销）
                p.Hidden = hidden;
                foreach (var r in p.Renderers)
                    if (r != null) r.enabled = !hidden;
            }
        }
    }
}
