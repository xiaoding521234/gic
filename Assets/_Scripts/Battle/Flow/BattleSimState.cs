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

        /// <summary>已注册玩家ID（回合提交完成度判定）</summary>
        private readonly HashSet<string> _playerIds = new HashSet<string>();

        /// <summary>玩家资源（B1 只携带初始值）</summary>
        private readonly Dictionary<string, PlayerResourceState> _resources = new Dictionary<string, PlayerResourceState>();

        private readonly Dictionary<string, Unit> _units = new Dictionary<string, Unit>();
        private int _nextUnitId = 1;

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

        public void RegisterPlayer(string playerId, int initialMora = 200, int initialStamina = 60)
        {
            _playerIds.Add(playerId);
            if (!_resources.ContainsKey(playerId))
            {
                _resources[playerId] = new PlayerResourceState
                {
                    playerId = playerId,
                    mora = initialMora,
                    stamina = initialStamina,
                    handCardCount = 0,
                    deckCardCount = 0,
                };
            }
        }

        public IReadOnlyCollection<string> PlayerIds => _playerIds;

        // ==================== 局内手牌与资源（B6c：手牌=玩家当前卡组投影，卡不消耗可重复出战） ====================

        private readonly Dictionary<string, List<int>> _hands = new Dictionary<string, List<int>>();

        /// <summary>注册玩家手牌（开局从存档当前卡组构建；UnitName 枚举值列表）</summary>
        public void RegisterHand(string playerId, List<int> handUnitNames)
        {
            _hands[playerId] = handUnitNames ?? new List<int>();
            if (_resources.TryGetValue(playerId, out var res))
                res.handCardCount = _hands[playerId].Count;
        }

        public IReadOnlyList<int> GetHand(string playerId)
        {
            return _hands.TryGetValue(playerId, out var hand) ? hand : null;
        }

        /// <summary>摩拉查询（无注册返回 0）</summary>
        public int GetMora(string playerId)
        {
            return _resources.TryGetValue(playerId, out var res) ? res.mora : 0;
        }

        /// <summary>摩拉消耗（不足返回 false 且不改动；B6c 部署扣费）</summary>
        public bool TrySpendMora(string playerId, int cost)
        {
            if (!_resources.TryGetValue(playerId, out var res) || res.mora < cost) return false;
            res.mora -= cost;
            return true;
        }

        /// <summary>玩家核心位置代理（B6c 部署落点判定基准；协议核心 B8 Unit 化后换真核心——
        /// 数据源收口此处一处：v1=出生区中心，spawnCenters 与 PlayerIds 同序）</summary>
        public BattleCell GetCorePosition(string playerId)
        {
            int index = 0;
            foreach (var id in _playerIds)
            {
                if (id == playerId) break;
                index++;
            }
            if (index < _playerIds.Count && Map.spawnCenters != null && index < Map.spawnCenters.Count)
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
        /// 上限=UnitConfig baseEnergy（GetEffectiveEnergy）；消耗值=技能条目 EnergyCost
        /// （三角色爆发 30/40/100 恰与上限相等=攒满才放；安柏延奏 20<上限 30=不必攒满，两者独立）
        /// </summary>
        public void ApplyEnergy(Unit target, int delta)
        {
            var stats = target.GetUnitComponent<UnitStats>();
            if (stats == null) return;
            var energy = stats.GetStatStruct(StatType.Energy);
            energy.Add(delta);
            stats.SetStatStruct(StatType.Energy, energy);
        }

        /// <summary>技能的元能消耗（技能条目 EnergyCost 参数；0=无消耗——战技/移动不耗能）</summary>
        public static int GetEnergyCost(SkillConfig.SkillData skillData)
        {
            return skillData?.GetInt(SkillParamKey.EnergyCost, 0) ?? 0;
        }

        /// <summary>元能是否够施放（门槛=技能消耗值而非上限——延奏类不满即可放）</summary>
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

        /// <summary>按注册序结算后的到期收集（RemainingTurns≤0）</summary>
        public List<BaseBuff> CollectExpiredBuffs()
        {
            var expired = new List<BaseBuff>();
            foreach (var buff in _activeBuffs)
                if (buff.RemainingTurns <= 0) expired.Add(buff);
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

        public BattleSnapshot TakeSnapshot(int turnNumber)
        {
            var snapshot = new BattleSnapshot { turnNumber = turnNumber };

            foreach (var kv in _units)
            {
                var unit = kv.Value;
                var identity = unit.GetUnitComponent<UnitIdentity>();
                var stats = unit.GetUnitComponent<UnitStats>();
                var element = unit.GetUnitComponent<UnitElement>();
                var status = unit.GetUnitComponent<UnitStatus>();
                var buffs = unit.Buffs;

                var state = new UnitState
                {
                    unitId = kv.Key,
                    unitName = identity?.UnitName.ToString() ?? "",
                    playerId = identity?.OwnerPlayerID ?? "",
                    team = (int)(identity?.Team ?? TeamType.A),
                    position = GetPosition(unit),
                    hp = stats?.HP ?? 0,
                    maxHp = stats?.GetStatStruct(StatType.HP).Max ?? 0,
                    attack = stats?.Attack ?? 0,
                    defense = stats?.Defense ?? 0,
                    attackSpeed = stats?.AttackSpeed ?? 0,
                    dyedElement = (int)(element?.DyedElement ?? ElementType.Physical),
                    isCorpse = status != null && status.IsDead ? 1 : 0,
                    isFrozen = status != null && status.IsFrozen ? 1 : 0,
                    volume = unit.Volume,
                    energy = stats?.Energy ?? 0,
                    maxEnergy = stats?.GetStatStruct(StatType.Energy).Max ?? 0,
                };
                foreach (var buff in buffs)
                    state.buffs.Add(new BuffState
                    {
                        type = (int)buff.Type,
                        level = buff.Level,
                        remainingTurns = buff.RemainingTurns,
                    });
                snapshot.units.Add(state);
            }

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
                    snapshotRes.handUnits = new List<int>(hand);
                snapshot.resources.Add(snapshotRes);
            }

            return snapshot;
        }
    }
}
