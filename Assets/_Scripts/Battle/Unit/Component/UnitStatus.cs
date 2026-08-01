using UnityEngine;
using System.Collections.Generic;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.UI;
using GIC.Tool;
namespace GIC.Battle
{


    /// <summary>
    /// 状态修改器
    /// </summary>
    public class StatusModifier
    {
        public StatusType StatusType;
        public bool Value;


        public StatusModifier(StatusType statusType, bool value)
        {
            StatusType = statusType;
            Value = value;
        }
    }

    /// <summary>
    /// 单位状态组件 - 管理单位的状态效果
    /// </summary>
    public class UnitStatus : MonoBehaviour, IUnitComponent
    {
        private Unit _owner;

        // ==================== 状态模板 ====================
        private static readonly Dictionary<StatusType, bool> _templateStatus = new()
        {
            { StatusType.Invisible, false },
            { StatusType.SuperArmor, false },
            { StatusType.Stunned, false },
            { StatusType.Silenced, false },
            { StatusType.Frozen, false },
            { StatusType.Petrified, false },
        };

        // ==================== 当前状态实例 ====================
        private Dictionary<StatusType, bool> _status = new();

        // ==================== 修改器列表 ====================
        private List<StatusModifier> _modifiers = new();

        // ==================== 属性 ====================
        public bool IsInvisible => GetFinalStatus(StatusType.Invisible);
        public bool IsSuperArmor => GetFinalStatus(StatusType.SuperArmor);
        public bool IsStunned => GetFinalStatus(StatusType.Stunned);
        public bool IsSilenced => GetFinalStatus(StatusType.Silenced);
        public bool IsFrozen => GetFinalStatus(StatusType.Frozen);
        public bool IsPetrified => GetFinalStatus(StatusType.Petrified);

        /// <summary>
        /// 是否被硬控（晕眩/冰冻/石化）
        /// </summary>
        public bool IsHardControlled => IsStunned || IsFrozen || IsPetrified;

        /// <summary>
        /// 是否可以行动
        /// </summary>
        public bool CanAct => !IsHardControlled;

        /// <summary>
        /// 是否可以被敌方选中
        /// </summary>
        public bool CanBeTargeted => !IsInvisible;

        // ==================== 初始化 ====================
        public void Init(Unit owner)
        {
            _owner = owner;

            // 从模板复制初始值
            _status.Clear();
            foreach (var kv in _templateStatus)
            {
                _status[kv.Key] = kv.Value;
            }
        }

        // ==================== 最终状态计算 ====================
        public bool GetFinalStatus(StatusType statusType)
        {
            if (!_status.TryGetValue(statusType, out bool baseValue))
                return false;

            bool result = baseValue;

            foreach (var mod in _modifiers)
            {
                if (mod.StatusType == statusType)
                {
                    result = mod.Value;
                }
            }

            return result;
        }

        // ==================== 基础值管理 ====================
        public void SetBaseValue(StatusType statusType, bool value)
        {
            if (_status.ContainsKey(statusType))
            {
                _status[statusType] = value;
            }
        }

        public bool GetBaseValue(StatusType statusType)
        {
            return _status.TryGetValue(statusType, out bool value) ? value : false;
        }

        // ==================== 修改器管理 ====================
        public void AddModifier(StatusModifier modifier)
        {
            _modifiers.Add(modifier);
        }

        public bool RemoveModifier(StatusModifier modifier)
        {
            return _modifiers.Remove(modifier);
        }

        public void RemoveModifiers(System.Predicate<StatusModifier> match)
        {
            _modifiers.RemoveAll(match);
        }

        public void ClearAllModifiers()
        {
            _modifiers.Clear();
        }

        // ==================== 便利方法 ====================
        public void SetStatus(StatusType statusType, bool value)
        {
            SetBaseValue(statusType, value);
        }

        public bool IsVisibleToPlayer(string playerID)
        {
            var identity = _owner.GetUnitComponent<UnitIdentity>();
            if (identity != null && identity.OwnerPlayerID == playerID)
                return true;

            return !IsInvisible;
        }

        public Dictionary<StatusType, bool> GetAllStatus()
        {
            return new Dictionary<StatusType, bool>(_status);
        }

        public void ResetToTemplate()
        {
            _status.Clear();
            foreach (var kv in _templateStatus)
            {
                _status[kv.Key] = kv.Value;
            }
        }
    }
}


