using System;
using System.Collections.Generic;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// Host 侧战场状态访问门面（快照生成 / 效应应用 / unitId 注册 / 地形查询）。
    /// 逻辑 Unit = 现有 MonoBehaviour 组件容器（Host 场景实例化，docs/active/22 §4）。
    /// </summary>
    public class BattleSimState
    {
        public BattleMapData Map { get; }

        /// <summary>已注册玩家ID（**注册序保序**——部署核心位置代理按注册序映射 spawnCenters、回合提交完成度判定用。
        /// 2026-09-23 审查 Y5：原 HashSet 迭代序 .NET 无保证，B7 换注册实现会静默错位部署基准）</summary>
        private readonly List<string> _playerIds = new List<string>();

        /// <summary>玩家资源（B1 只携带初始值）</summary>
        private readonly Dictionary<string, PlayerResourceState> _resources = new Dictionary<string, PlayerResourceState>();

        private readonly Dictionary<string, Unit> _units = new Dictionary<string, Unit>();
        private int _nextUnitId = 1;

        /// <summary>协议核心登记（playerId → 核心 unitId；2026-09-29 拍板核心 Unit 化——
        /// RegisterUnit 时按 UnitName 识别登记，胜负判据/部署基准/片末检查全走此单一登记）</summary>
        private readonly Dictionary<string, string> _coreUnitIds = new Dictionary<string, string>();

        /// <summary>片边界 poll 的即时行动队列（连携/契约类；B1 调试用）</summary>
        private readonly List<ActionData> _instantActionQueue = new List<ActionData>();

        /// <summary>Buff 注册表（全局注册序：回合结束效果按注册序结算，docs/active/22 §2）</summary>
        private readonly List<BaseBuff> _activeBuffs = new List<BaseBuff>();
        private int _buffSequence;

        public IReadOnlyList<BaseBuff> ActiveBuffs => _activeBuffs;

        public IReadOnlyDictionary<string, Unit> Units => _units;
        public IReadOnlyList<ActionData> InstantActionQueue => _instantActionQueue;

        public BattleSimState(BattleMapData map)
        {
            Map = map;
        }

        /// <summary>逻辑单位隐藏根（装配时设置；部署等运行时生成单位挂此——纯逻辑容器勿落场景根，BattleSession._logicRoot 同源）</summary>
        public UnityEngine.Transform LogicRoot { get; set; }

        // ==================== 玩家注册 ====================

        public void RegisterPlayer(string playerId)
        {
            if (!_playerIds.Contains(playerId))
                _playerIds.Add(playerId); // 注册序即 spawnCenters 映射序（List 保序）
            if (!_resources.ContainsKey(playerId))
            {
                // 资源池 0 起步（2026-09-25 拍板「获得卡片=手牌构建唯一入口」）——开局初始 200/60
                // 不在此初始化，由装配期 GainCard 统一获得（编没编货币卡都送、落在牌上）；
                // BattleMetrics.InitialMora/InitialStamina 仍为口径常量（装配处引用）
                _resources[playerId] = new PlayerResourceState
                {
                    playerId = playerId,
                    mora = 0,
                    stamina = 0,
                    handCardCount = 0,
                    deckCardCount = 0,
                };
            }
        }

        public IReadOnlyList<string> PlayerIds => _playerIds;

        // ==================== 局内手牌与资源（2026-09-25 拍板：获得卡片=手牌构建唯一入口，完全统一） ====================

        private readonly Dictionary<string, List<HandCard>> _hands = new Dictionary<string, List<HandCard>>();

        /// <summary>注册玩家手牌（开局=空表起步，一切牌经 GainCard 获得）</summary>
        public void RegisterHand(string playerId, List<HandCard> handCards)
        {
            _hands[playerId] = handCards ?? new List<HandCard>();
            if (_resources.TryGetValue(playerId, out var res))
                res.handCardCount = _hands[playerId].Count;
        }

        public IReadOnlyList<HandCard> GetHand(string playerId)
        {
            return _hands.TryGetValue(playerId, out var hand) ? hand : null;
        }

        /// <summary>手牌条目列表（未注册手牌时建空表——GainCard 可先于 RegisterHand 安全调用）</summary>
        private List<HandCard> HandList(string playerId)
        {
            if (!_hands.TryGetValue(playerId, out var hand))
            {
                hand = new List<HandCard>();
                _hands[playerId] = hand;
            }
            return hand;
        }

        /// <summary>摩拉查询（无注册返回 0）</summary>
        public int GetMora(string playerId)
        {
            return _resources.TryGetValue(playerId, out var res) ? res.mora : 0;
        }

        /// <summary>体力查询（无注册返回 0）</summary>
        public int GetStamina(string playerId)
        {
            return _resources.TryGetValue(playerId, out var res) ? res.stamina : 0;
        }

        /// <summary>摩拉消耗（不足返回 false 且不改动；B6c 部署扣费。扣减走 ApplyMoraDelta——
        /// 池写自动同步摩拉牌条目存亡）</summary>
        public bool TrySpendMora(string playerId, int cost)
        {
            if (cost <= 0) return true;
            if (!_resources.TryGetValue(playerId, out var res) || res.mora < cost) return false;
            ApplyMoraDelta(playerId, -cost);
            return true;
        }

        /// <summary>体力是否够一次配额行动（B6d；消耗量收口 BattleMetrics.StaminaCostPerAction）</summary>
        public bool HasEnoughStamina(string playerId, int cost)
        {
            return cost <= 0 || GetStamina(playerId) >= cost;
        }

        /// <summary>摩拉增量（货币物品牌堆张数变化：回合结束发放/部署扣费/开局获得共用入口）。
        /// 池写后自动同步摩拉牌手牌条目存亡（SyncCurrencyEntry——获得与失去对称）</summary>
        public void ApplyMoraDelta(string playerId, int delta)
        {
            if (!_resources.TryGetValue(playerId, out var res)) return;
            res.mora = Math.Max(0, res.mora + delta);
            SyncCurrencyEntry(playerId, ItemName.Mora, res.mora);
        }

        /// <summary>体力增量（货币物品牌堆张数变化：回合结束发放/行动消耗/开局获得共用入口）。
        /// 池写后自动同步体力牌手牌条目存亡（SyncCurrencyEntry——获得与失去对称）</summary>
        public void ApplyStaminaDelta(string playerId, int delta)
        {
            if (!_resources.TryGetValue(playerId, out var res)) return;
            res.stamina = Math.Max(0, res.stamina + delta);
            SyncCurrencyEntry(playerId, ItemName.Stamina, res.stamina);
        }

        // ==================== 获得卡片 / 失去卡片（2026-09-25 拍板：手牌构建唯一入口，获得/失去对称） ====================

        /// <summary>
        /// 获得卡片（公用方法，用户拍板原语义：「如果手牌已经有该卡，则加数量，如果没有，
        /// 则加上这个卡」）。手牌构建/开局初始量/货币获得的统一入口——普通卡（角色/物品）数量=条目
        /// 真源；货币物品牌（摩拉/体力）数量走资源池（池即牌堆张数）。装配期调用（快照前）无需产
        /// 命令；局内获得（掠夺等）届时由调用方负责命令产出。
        /// </summary>
        public void GainCard(string playerId, CardId cardId, int count)
        {
            if (string.IsNullOrEmpty(playerId) || cardId.value <= 0 || count <= 0) return;

            var entry = FindHandEntry(playerId, cardId);
            if (entry != null && !IsCurrencyCard(cardId))
            {
                entry.count += count; // 普通卡：有则加数量
                return;
            }

            if (IsCurrencyCard(cardId))
            {
                // 货币：池写（池>0 时 SyncCurrencyEntry 自动加卡——有则不重复加，无则加上这个卡）
                if (cardId.AsItemName() == ItemName.Mora) ApplyMoraDelta(playerId, count);
                else ApplyStaminaDelta(playerId, count);
                return;
            }

            // 普通卡：没有则加上这个卡
            var hand = HandList(playerId);
            hand.Add(new HandCard(cardId, count));
            if (_resources.TryGetValue(playerId, out var res))
                res.handCardCount = hand.Count;
        }

        /// <summary>
        /// 失去卡片（获得的对偶，用户拍板「失去数量时，同理」）：有则减数量、
        /// 减至零移除卡。普通卡=条目真源；货币牌=池减（空堆时条目移除，再发放经池自动复活——
        /// 与获得完全对称）。数量不足返回 false 且不改动。使用/装备消耗与掠夺由后续批次接线。
        /// </summary>
        public bool LoseCard(string playerId, CardId cardId, int count)
        {
            if (count <= 0) return true;

            if (IsCurrencyCard(cardId))
            {
                int pool = cardId.AsItemName() == ItemName.Mora ? GetMora(playerId) : GetStamina(playerId);
                if (pool < count) return false;
                if (cardId.AsItemName() == ItemName.Mora) ApplyMoraDelta(playerId, -count);
                else ApplyStaminaDelta(playerId, -count);
                return true;
            }

            var entry = FindHandEntry(playerId, cardId);
            if (entry == null || entry.count < count) return false;
            entry.count -= count;
            if (entry.count <= 0)
            {
                var hand = HandList(playerId);
                hand.Remove(entry);
                if (_resources.TryGetValue(playerId, out var res))
                    res.handCardCount = hand.Count;
            }
            return true;
        }

        private static bool IsCurrencyCard(CardId cardId)
        {
            return cardId.cardType == CardType.Item
                && (cardId.AsItemName() == ItemName.Mora || cardId.AsItemName() == ItemName.Stamina);
        }

        /// <summary>货币牌手牌条目存亡同步（池写后调用）：堆里有牌（池>0）→手牌必有该卡；
        /// 空堆（池=0）→手牌移除该卡。发放/消耗/部署扣费/开局获得一切池写自动走此同步——
        /// 货币牌的"加上这个卡/移除这个卡"无特判，与普通卡获得/失去语义对称。</summary>
        private void SyncCurrencyEntry(string playerId, ItemName item, int pool)
        {
            if (item != ItemName.Mora && item != ItemName.Stamina) return;
            var hand = HandList(playerId);
            CardId id = new CardId(item);
            var entry = FindHandEntry(playerId, id);
            if (pool > 0)
            {
                if (entry == null)
                {
                    hand.Add(new HandCard(id, pool));
                    if (_resources.TryGetValue(playerId, out var res))
                        res.handCardCount = hand.Count;
                }
                else
                {
                    // 2026-09-27 复审修复：已有条目同步池值——快照序列化虽会重映射（IsCurrency 分支），
                    // 但 in-sim 消费方读条目 count 会拿到陈旧值（潜伏双源），此处与快照双保险
                    entry.count = pool;
                }
            }
            else if (entry != null)
            {
                hand.Remove(entry);
                if (_resources.TryGetValue(playerId, out var res))
                    res.handCardCount = hand.Count;
            }
        }

        private HandCard FindHandEntry(string playerId, CardId cardId)
        {
            if (!_hands.TryGetValue(playerId, out var hand)) return null;
            foreach (var entry in hand)
            {
                if ((CardType)entry.cardType == cardId.cardType && entry.value == cardId.value)
                    return entry;
            }
            return null;
        }

        /// <summary>玩家核心位置（B6c 部署落点判定基准）。协议核心 Unit 化（2026-09-29 拍板）：
        /// 真源=该玩家核心单位位置（核心免疫强制位移、永不移动，与出生区中心坐标恒等——换的是
        /// 数据源正确性，未来核心位类技能/多地图不再依赖注册序）；无核心（装配异常防御）回落
        /// 出生区中心代理（spawnCenters 与 PlayerIds 注册序同序）</summary>
        public BattleCell GetCorePosition(string playerId)
        {
            var core = GetCoreUnit(playerId);
            if (core != null) return GetPosition(core);
            int index = _playerIds.IndexOf(playerId);
            if (index >= 0 && Map.spawnCenters != null && index < Map.spawnCenters.Count)
                return Map.spawnCenters[index];
            return BattleCell.zero;
        }

        /// <summary>玩家队伍（部署新单位的阵营；装配时注册，P1=A/P2=B）</summary>
        private readonly Dictionary<string, TeamType> _playerTeams = new Dictionary<string, TeamType>();

        public void SetPlayerTeam(string playerId, TeamType team)
        {
            _playerTeams[playerId] = team;
        }

        public TeamType GetTeamOf(string playerId)
        {
            return _playerTeams.TryGetValue(playerId, out var team) ? team : TeamType.A;
        }

        // ==================== 单位注册 ====================

        /// <summary>
        /// 注册逻辑单位（分配 unitId 并写入身份/位置）
        /// </summary>
        public string RegisterUnit(Unit unit, string playerId, TeamType team, BattleCell position)
        {
            string unitId = $"U{_nextUnitId++}";
            unit.GetUnitComponent<UnitIdentity>()?.SetIdentity(unitId, playerId, team);
            unit.GetUnitComponent<UnitGridPosition>()?.SetPosition(BoardType.MainWorld, position.ToVector2Int());
            _units[unitId] = unit;
            // 协议核心登记（同玩家重复注册=后者覆盖——对局装配每玩家恰一枚，防御性取最新）
            if (unit.RawData != null && unit.RawData.unitName == UnitName.ProtocolCore)
                _coreUnitIds[playerId] = unitId;
            // 命座登场 Buff 授予（B8 批：0命固有被动的 Buff 形态——芭芭拉「获得歌声之环」，
            // OnDeploy[ApplyBuff] 原子；随后的 BuildUnitState 携带 buffs，客户端零额外命令）
            ConstellationApplier.ApplyDeployBuffs(this, unit);
            return unitId;
        }

        public Unit GetUnit(string unitId)
        {
            return unitId != null && _units.TryGetValue(unitId, out var unit) ? unit : null;
        }

        public bool TryGetUnitId(Unit unit, out string unitId)
        {
            unitId = unit?.GetUnitComponent<UnitIdentity>()?.UnitID;
            return unitId != null && _units.ContainsKey(unitId);
        }

        // ==================== 存活/状态判定 ====================

        public static bool IsDead(Unit unit)
        {
            return unit?.GetUnitComponent<UnitStatus>()?.IsDead ?? true;
        }

        public static bool CanAct(Unit unit)
        {
            var status = unit?.GetUnitComponent<UnitStatus>();
            return status != null && status.CanAct;
        }

        /// <summary>该队伍是否还有存活单位（旧全灭软停检测——核心判据落地后仅作装配异常防御回落，
        /// 正常对局胜负唯一判据=协议核心，TurnFlowController.CheckBattleOver 消费）</summary>
        public bool HasLivingUnits(TeamType team)
        {
            foreach (var kv in _units)
            {
                var id = kv.Value.GetUnitComponent<UnitIdentity>();
                if (id != null && id.Team == team && !IsDead(kv.Value))
                    return true;
            }
            return false;
        }

        // ==================== 协议核心（2026-09-29 拍板 Unit 化：HP/胜负判据/部署基准） ====================

        /// <summary>玩家的核心单位（未登记返回 null）</summary>
        public Unit GetCoreUnit(string playerId)
        {
            return _coreUnitIds.TryGetValue(playerId, out var unitId) ? GetUnit(unitId) : null;
        }

        /// <summary>该玩家的协议核心是否存活（未登记=视作存活，避免装配异常秒判负）</summary>
        public bool HasLivingCore(string playerId)
        {
            var core = GetCoreUnit(playerId);
            return core == null || !IsDead(core);
        }

        /// <summary>全对局是否有任一已登记核心（装配异常判定：无核心对局回落旧全灭口径）</summary>
        public bool HasAnyRegisteredCore() => _coreUnitIds.Count > 0;

        /// <summary>任一已登记核心已被摧毁（片末检查：核心死→中断剩余片结算，胜负广播由
        /// TurnFlowController 既有 CheckBattleOver 位置接住——docs/18 协议核心决策条）</summary>
        public bool AnyCoreDestroyed()
        {
            foreach (var kv in _coreUnitIds)
            {
                var core = GetUnit(kv.Value);
                if (core != null && IsDead(core)) return true;
            }
            return false;
        }

        /// <summary>队伍是否仍有存活的协议核心（多人：队内任一玩家的核心存活=该队未败；
        /// 队内无已登记核心=视作未败——防御口径）</summary>
        public bool TeamHasLivingCore(TeamType team)
        {
            bool anyRegistered = false;
            foreach (var kv in _coreUnitIds)
            {
                if (GetTeamOf(kv.Key) != team) continue;
                anyRegistered = true;
                var core = GetUnit(kv.Value);
                if (core != null && !IsDead(core)) return true;
            }
            return !anyRegistered;
        }

        // ==================== 位置查询 ====================

        public BattleCell GetPosition(Unit unit)
        {
            var pos = unit.GetUnitComponent<UnitGridPosition>();
            return pos != null ? new BattleCell(pos.Position) : BattleCell.zero;
        }

        public BattleCell GetPosition(string unitId)
        {
            var unit = GetUnit(unitId);
            return unit != null ? GetPosition(unit) : BattleCell.zero;
        }

        public void SetPosition(Unit unit, BattleCell cell)
        {
            unit.GetUnitComponent<UnitGridPosition>()?.SetPosition(BoardType.MainWorld, cell.ToVector2Int());
        }

        /// <summary>
        /// 获取某格上的全部单位（含尸体——尸体保留碰撞/体积，docs/05 §5.4）
        /// </summary>
        public List<Unit> GetUnitsAt(BattleCell cell)
        {
            var result = new List<Unit>();
            foreach (var kv in _units)
            {
                if (GetPosition(kv.Value).Equals(cell))
                    result.Add(kv.Value);
            }
            return result;
        }

        /// <summary>
        /// 某格现有单位体积之和（可排除指定单位）
        /// </summary>
        public int GetVolumeAt(BattleCell cell, Unit exclude = null)
        {
            int total = 0;
            foreach (var unit in GetUnitsAt(cell))
            {
                if (unit != exclude)
                    total += unit.Volume;
            }
            return total;
        }

        // ==================== 效应应用 ====================

        /// <summary>
        /// 应用伤害（HP 扣减，RangedInt 自动钳 0）
        /// </summary>
        public void ApplyDamage(Unit target, int amount)
        {
            var stats = target.GetUnitComponent<UnitStats>();
            if (stats == null) return;
            var hp = stats.GetStatStruct(StatType.HP);
            hp.Subtract(amount);
            stats.SetStatStruct(StatType.HP, hp);
        }

        /// <summary>
        /// 应用治疗
        /// </summary>
        public void ApplyHeal(Unit target, int amount)
        {
            var stats = target.GetUnitComponent<UnitStats>();
            if (stats == null) return;
            var hp = stats.GetStatStruct(StatType.HP);
            hp.Add(amount);
            stats.SetStatStruct(StatType.HP, hp);
        }

        /// <summary>
        /// 应用元能变化（B6a：正=获取——移动+10/战技至少1次命中+10；负=技能消耗。
        /// 上限=UnitConfig baseEnergy（GetEffectiveEnergy）+命座容量加成（SetStatRange 直扩）；消耗值=技能条目 EnergyCost。
        /// 溢出转移（B8 命座批，安柏1命）：delta>0 且目标带 EnergyOverflowPassive 且溢出——自身只留
        /// 装得下的部分，溢出转移给切比雪夫最近未满元能我方存活角色（同距 unitId 升序；全满则无效果）；
        /// 接收方经 out 回传给调用方（TurnResolver 转移效应命令），本层不做二次转移（单跳）。
        /// </summary>
        public void ApplyEnergy(Unit target, int delta)
        {
            ApplyEnergy(target, delta, out _, out _);
        }

        /// <summary>带溢出转移的元能应用（见上——overflowReceiverId/overflowAmount 回传转移事实）</summary>
        public void ApplyEnergy(Unit target, int delta, out string overflowReceiverId, out int overflowAmount)
        {
            overflowReceiverId = null;
            overflowAmount = 0;
            if (target == null) return;
            var stats = target.GetUnitComponent<UnitStats>();
            if (stats == null) return;
            var energy = stats.GetStatStruct(StatType.Energy);

            if (delta > 0 && target.EnergyOverflowPassive)
            {
                int room = energy.Max - energy.Value;
                if (delta > room) // 溢出：自身留满，余量转移（room≤0=已满全额转移）
                {
                    overflowAmount = delta - System.Math.Max(0, room);
                    if (room > 0)
                    {
                        energy.Add(room);
                        stats.SetStatStruct(StatType.Energy, energy);
                    }
                    var ally = FindNearestNonFullEnergyAlly(target);
                    if (ally != null && TryGetUnitId(ally, out var allyId))
                    {
                        overflowReceiverId = allyId;
                        var allyStats = ally.GetUnitComponent<UnitStats>();
                        var allyEnergy = allyStats.GetStatStruct(StatType.Energy);
                        allyEnergy.Add(overflowAmount); // 单跳：接收方不再转移
                        allyStats.SetStatStruct(StatType.Energy, allyEnergy);
                    }
                    else overflowAmount = 0; // 我方全满则无效果（安柏1命描述口径）
                    return;
                }
            }

            energy.Add(delta);
            stats.SetStatStruct(StatType.Energy, energy);
        }

        /// <summary>理智应用（2026-09-30 歌声之环批）：RangedInt Add 自动钳 -300~300（到上限自然停涨）；
        /// 玩法消费方随未来理智机制批接线，本批打通 配置→运行时→结算→命令→快照 全链</summary>
        public void ApplySanity(Unit target, int delta)
        {
            if (target == null || delta == 0) return;
            var stats = target.GetUnitComponent<UnitStats>();
            if (stats == null) return;
            var sanity = stats.GetStatStruct(StatType.Sanity);
            sanity.Add(delta);
            stats.SetStatStruct(StatType.Sanity, sanity);
        }

        /// <summary>切比雪夫距离最近、元能未满的我方存活角色（不含自身；同距 unitId 升序——
        /// 安柏1命溢出转移的接收方判定；尸体不算「角色」）</summary>
        private Unit FindNearestNonFullEnergyAlly(Unit self)
        {
            var selfPos = GetPosition(self);
            var selfIdentity = self.GetUnitComponent<UnitIdentity>();
            if (selfIdentity == null) return null;
            Unit best = null;
            int bestDist = int.MaxValue;
            string bestId = null;
            foreach (var kv in _units)
            {
                var candidate = kv.Value;
                if (candidate == self || BattleSimState.IsDead(candidate)) continue;
                var identity = candidate.GetUnitComponent<UnitIdentity>();
                if (identity == null || identity.Team != selfIdentity.Team) continue;
                var stats = candidate.GetUnitComponent<UnitStats>();
                if (stats == null) continue;
                var energy = stats.GetStatStruct(StatType.Energy);
                if (energy.Value >= energy.Max) continue; // 已满不接收
                var pos = GetPosition(candidate);
                int dist = System.Math.Max(System.Math.Abs(pos.x - selfPos.x), System.Math.Abs(pos.y - selfPos.y));
                if (dist < bestDist || (dist == bestDist && bestId != null
                    && string.CompareOrdinal(kv.Key, bestId) < 0))
                {
                    best = candidate;
                    bestDist = dist;
                    bestId = kv.Key;
                }
            }
            return best;
        }

        /// <summary>元能是否够施放（门槛=技能消耗值而非上限——延奏类不满即可放）。
        /// C-2 起消耗声明单源=SkillData.costs（ResourceGate 消费；本判定为元能条目底层原语）</summary>
        public static bool HasEnoughEnergy(Unit unit, int energyCost)
        {
            if (energyCost <= 0) return true;
            var stats = unit?.GetUnitComponent<UnitStats>();
            return stats != null && stats.Energy >= energyCost;
        }

        /// <summary>
        /// 死亡判定并转尸体态（HP≤0 → Dead；属性/碰撞/体积/势力全保留，docs/05 §5.4）
        /// </summary>
        public List<Unit> ResolveDeaths(IEnumerable<Unit> candidates)
        {
            var dead = new List<Unit>();
            foreach (var unit in candidates)
            {
                if (IsDead(unit)) continue;
                var stats = unit.GetUnitComponent<UnitStats>();
                if (stats != null && stats.HP <= 0)
                {
                    unit.GetUnitComponent<UnitStatus>()?.SetStatus(StatusType.Dead, true);
                    dead.Add(unit);
                }
            }
            return dead;
        }

        // ==================== Buff（B2） ====================

        /// <summary>
        /// 施加 Buff：同类已存在 → 合并（默认时长累加+级别取大，docs/06 燃烧延长同构）；
        /// 新施加 → 记入全局注册表（回合结束效果按注册序，docs/active/22 §2）→ OnApplied 生命周期回调
        /// </summary>
        public void ApplyBuff(Unit target, BaseBuff buff, Unit source = null)
        {
            if (target == null || buff == null) return;
            buff.source = source;
            buff.Sim = this; // 战场门面注入（回合结束效果需战场查询的 Buff 消费——如歌声之环半径枚举）

            var existing = target.Buffs.Find(b => b.Type == buff.Type);
            if (existing != null)
            {
                existing.Merge(buff);
                return;
            }

            buff.ApplicationIndex = _buffSequence++;
            target.AddBuff(buff); // Unit.AddBuff 置 owner
            _activeBuffs.Add(buff);
            buff.OnApplied(); // 如冻结写入 UnitStatus（B4）
        }

        /// <summary>移除 Buff（到期/驱散）：OnRemoved 生命周期回调 + 同步清注册表</summary>
        public void RemoveBuff(Unit target, BaseBuff buff)
        {
            buff.OnRemoved(); // 如冻结解除 UnitStatus（幂等：重复移除无害）
            target?.RemoveBuff(buff);
            _activeBuffs.Remove(buff);
        }

        /// <summary>元素附着（效应应用阶段执行；覆盖=消耗被反应附着，docs/06）</summary>
        public void AttachElement(Unit target, ElementType element)
        {
            target?.GetUnitComponent<UnitElement>()?.Dye(element);
        }

        /// <summary>清除附着（反应消耗的真实状态落点——决策二十五：反应 1:1 双方全消耗、等层
        /// 零残留；反应命中不再发 AttachElementEffect，清除由 ReactionEffect 应用分支执行）</summary>
        public void ClearDye(Unit target)
        {
            target?.GetUnitComponent<UnitElement>()?.ClearDye();
        }

        /// <summary>按注册序结算后的到期收集（RemainingTurns≤0；永久 Buff（RemainingTurns&lt;0）不计时
        /// 不收集——歌声之环/寒冰之棱类消失走 RemoveOnHolderDeath 倒下移除或碎裂即时移除
        /// （BattleSimState.RemoveBuff——寒冰之棱 TryShatter 直接注销）</summary>
        public List<BaseBuff> CollectExpiredBuffs()
        {
            var expired = new List<BaseBuff>();
            foreach (var buff in _activeBuffs)
                if (!buff.IsPermanent && buff.RemainingTurns <= 0) expired.Add(buff);
            return expired;
        }

        // ==================== 即时行动队列 ====================

        public void EnqueueInstantAction(ActionData action)
        {
            _instantActionQueue.Add(action);
        }

        public ActionData DequeueInstantAction()
        {
            if (_instantActionQueue.Count == 0) return null;
            var action = _instantActionQueue[0];
            _instantActionQueue.RemoveAt(0);
            return action;
        }

        // ==================== 快照 ====================

        /// <summary>UnitState 单一构造出口（2026-09-23 审查 Y3：快照与 Summon 命令共用——
        /// 此前 DeployUnitExecutor 手写 9 字段的部分状态，登场回合附着图标/元能/防御等客户端显示偏差，下回合快照才自愈）</summary>
        public UnitState BuildUnitState(string unitId, Unit unit)
        {
            var identity = unit.GetUnitComponent<UnitIdentity>();
            var stats = unit.GetUnitComponent<UnitStats>();
            var element = unit.GetUnitComponent<UnitElement>();
            var status = unit.GetUnitComponent<UnitStatus>();

            var state = new UnitState
            {
                unitId = unitId,
                unitName = identity?.UnitName.ToString() ?? "",
                playerId = identity?.OwnerPlayerID ?? "",
                team = (int)(identity?.Team ?? TeamType.A),
                position = GetPosition(unit),
                hp = stats?.HP ?? 0,
                maxHp = stats?.GetStatStruct(StatType.HP).Max ?? 0,
                attack = stats?.Attack ?? 0,
                defense = stats?.Defense ?? 0,
                attackSpeed = stats?.AttackSpeed ?? 0,
                moveSpeed = stats?.MoveSpeed ?? 0,
                dyedElement = (int)(element?.DyedElement ?? ElementType.Physical),
                isCorpse = status != null && status.IsDead ? 1 : 0,
                isFrozen = status != null && status.IsFrozen ? 1 : 0,
                volume = unit.Volume,
                energy = stats?.Energy ?? 0,
                maxEnergy = stats?.GetStatStruct(StatType.Energy).Max ?? 0,
                sanity = stats?.Sanity ?? 0,
                cylinderDiameter = unit.RawData?.受击圆柱直径 ?? 0f,
                constellation = unit.ConstellationLevel, // 命座（B8 批：展示/升命门控真源）
            };
            foreach (var buff in unit.Buffs)
                state.buffs.Add(new BuffState
                {
                    type = (int)buff.Type,
                    level = buff.Level,
                    remainingTurns = buff.RemainingTurns,
                });
            return state;
        }

        public BattleSnapshot TakeSnapshot(int turnNumber)
        {
            var snapshot = new BattleSnapshot { turnNumber = turnNumber };

            foreach (var kv in _units)
                snapshot.units.Add(BuildUnitState(kv.Key, kv.Value));

            foreach (var kv in _resources)
            {
                var res = kv.Value;
                var snapshotRes = new PlayerResourceState
                {
                    playerId = res.playerId,
                    mora = res.mora,
                    stamina = res.stamina,
                    handCardCount = res.handCardCount,
                    deckCardCount = res.deckCardCount,
                };
                if (_hands.TryGetValue(kv.Key, out var hand))
                {
                    // 货币条目 count=资源池镜像（真源=池）；普通条目 count=局内真源
                    foreach (var entry in hand)
                    {
                        var copy = new HandCard(entry.AsCardId(), entry.count);
                        if (copy.IsCurrency)
                            copy.count = copy.AsItemName() == ItemName.Mora ? res.mora : res.stamina;
                        snapshotRes.handCards.Add(copy);
                    }
                }
                snapshot.resources.Add(snapshotRes);
            }

            return snapshot;
        }

        // ==================== 片内附着编译视图（2026-10-01 双蒸发修复） ====================
        // 病灶：反应预览只读片前快照附着，而消耗/覆盖在效应统一应用才落状态——同片多次命中
        // （安柏一箭双丘丘）后续命中仍见旧附着，一层水双蒸发（docs/06 §6.3 1比1消耗被打破）。
        // 视图=编译期工作副本：初值回落快照，反应消耗/新附着随编译序即时推进；TurnResolver
        // 片/即时段编译期以 Begin/End 包裹（命中编译全部走这两处）。真实状态仍由
        // AttachElementEffect 统一应用写入——视图只服务反应预判，不落任何持久状态。

        /// <summary>片内附着编译视图（unitId → 当前附着；null=未开启，读取回落快照=旧行为）</summary>
        private Dictionary<string, ElementType> _compileDyeView;

        /// <summary>开启编译视图（片/即时段编译期入口调用，紧跟 TakeSnapshot 之后）</summary>
        public void BeginCompileDyeView() => _compileDyeView = new Dictionary<string, ElementType>();

        /// <summary>关闭编译视图（片内全部命中编译完成后调用）</summary>
        public void EndCompileDyeView() => _compileDyeView = null;

        /// <summary>编译期附着读取：视图优先，未命中回退片前快照态；无快照（回合结束段 tick——
        /// 2026-10-01 tick 统一拍板）回退**活态**（染色写入仍走效应统一应用，活态在该编译时点=段初真值）</summary>
        public ElementType GetCompileDye(string unitId, UnitState snapshotState)
        {
            if (_compileDyeView != null && _compileDyeView.TryGetValue(unitId, out var dye))
                return dye;
            if (snapshotState != null) return (ElementType)snapshotState.dyedElement;
            var unit = GetUnit(unitId);
            return unit != null
                ? (unit.GetUnitComponent<UnitElement>()?.DyedElement ?? ElementType.Physical)
                : ElementType.Physical;
        }

        /// <summary>编译期附着推进：反应消耗=Physical（docs/06 §6.3）；AttachElement 覆盖=来袭元素</summary>
        public void SetCompileDye(string unitId, ElementType element)
        {
            if (_compileDyeView != null) _compileDyeView[unitId] = element;
        }
    }
}
