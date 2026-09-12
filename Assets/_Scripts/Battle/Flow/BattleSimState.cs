using System;
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
    /// Host 侧战场状态访问门面（快照生成 / 效应应用 / unitId 注册 / 地形查询）。
    /// 逻辑 Unit = 现有 MonoBehaviour 组件容器（Host 场景实例化，docs/22 §4）。
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

        public IReadOnlyDictionary<string, Unit> Units => _units;
        public IReadOnlyList<ActionData> InstantActionQueue => _instantActionQueue;

        public BattleSimState(BattleMapData map)
        {
            Map = map;
        }

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
                    volume = unit.Volume,
                };
                foreach (var buff in buffs)
                    state.buffs.Add(buff.GetType().Name);
                snapshot.units.Add(state);
            }

            foreach (var kv in _resources)
            {
                var res = kv.Value;
                snapshot.resources.Add(new PlayerResourceState
                {
                    playerId = res.playerId,
                    mora = res.mora,
                    stamina = res.stamina,
                    handCardCount = res.handCardCount,
                    deckCardCount = res.deckCardCount,
                });
            }

            return snapshot;
        }
    }
}
