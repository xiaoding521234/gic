using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.UI;
using GIC.Tool;
namespace GIC.Battle
{


    /// <summary>
    /// 单位身份组件
    /// </summary>
    public class UnitIdentity : MonoBehaviour, IUnitComponent
    {
        private Unit _owner;
        
        [Header("基础信息")]
        [SerializeField] private UnitName _unitName;
        
        [Header("稀有度")]
        [SerializeField] private int _starLevel = 3;        // 星级 1-5
        
        // ==================== 动态生成的属性 ====================
        private string _unitID;
        private string _ownerPlayerID;
        
        // ==================== 可修改的属性 ====================

        private TeamType _team = TeamType.A;
        private WeaponType _weaponType = WeaponType.Gauntlet;
        private int _deployCost;
        private List<FactionType> _factions = new();
        
        // ==================== 修改器列表 ====================
        private List<IdentityModifier> _modifiers = new();
        
        // ==================== 属性 ====================
        public UnitName UnitName => _unitName;
        public string UnitID => _unitID;
        public int StarLevel => _starLevel;
        public string OwnerPlayerID => _ownerPlayerID;
        
        public TeamType Team => GetFinalTeam();
        public WeaponType WeaponType => GetFinalWeaponType();
        public int DeployCost => GetFinalDeployCost();
        public IReadOnlyList<FactionType> Factions => GetFinalFactions();
        
        // ==================== 初始化 ====================
        public void Init(Unit owner)
        {
            _owner = owner;
            _unitName = owner.RawData.unitName;
            _starLevel = owner.RawData.starLevel;
            _weaponType = owner.RawData.weaponType;
            _deployCost = owner.RawData.GetEffectiveDeployCost();
            _factions = owner.RawData.factions.ToList();   
        }

        public void SetIdentity(string unitID, string ownerPlayerID, TeamType teamType)
        {
           _unitID = unitID;
           _ownerPlayerID = ownerPlayerID;
           _team = teamType;
        }
        
        // ==================== 最终值计算 ====================
        
        private TeamType GetFinalTeam()
        {
            TeamType result = _team;
            
            foreach (var mod in _modifiers)
            {
                if (mod.Type == IdentityModifierType.Team)
                {
                    result = mod.TeamValue;
                }
            }
            
            return result;
        }
        
        private WeaponType GetFinalWeaponType()
        {
            WeaponType result = _weaponType;
            
            foreach (var mod in _modifiers)
            {
                if (mod.Type == IdentityModifierType.Weapon)
                {
                    result = mod.WeaponValue;
                }
            }
            
            return result;
        }
        
        private int GetFinalDeployCost()
        {
            int baseValue = _deployCost;
            float flatBonus = 0;
            float percentBonus = 0;
            
            foreach (var mod in _modifiers)
            {
                if (mod.Type == IdentityModifierType.DeployCostFlat)
                {
                    flatBonus += mod.IntValue;
                }
                else if (mod.Type == IdentityModifierType.DeployCostPercent)
                {
                    percentBonus += mod.IntValue;
                }
            }
            
            return Mathf.Max(0, Mathf.RoundToInt((baseValue + flatBonus) * (1 + percentBonus / 100f)));
        }
        
        private List<FactionType> GetFinalFactions()
        {
            List<FactionType> result = new(_factions);
            
            foreach (var mod in _modifiers)
            {
                switch (mod.Type)
                {
                    case IdentityModifierType.AddFaction:
                        if (!result.Contains(mod.FactionValue))
                            result.Add(mod.FactionValue);
                        break;
                    case IdentityModifierType.RemoveFaction:
                        result.Remove(mod.FactionValue);
                        break;
                    case IdentityModifierType.SetFactions:
                        result = new List<FactionType>(mod.FactionsValue);
                        break;
                }
            }
            
            return result;
        }
        
        // ==================== 设置方法（修改基础值） ====================
        public void SetUnitID(string id) => _unitID = id;
        public void SetOwnerPlayer(string playerID) => _ownerPlayerID = playerID;
        public void SetTeam(TeamType team) => _team = team;
        public void SetWeaponType(WeaponType weaponType) => _weaponType = weaponType;
        public void SetDeployCost(int cost) => _deployCost = cost;
        public void SetFactions(List<FactionType> factions) => _factions = factions;
        public void AddFaction(FactionType faction)
        {
            if (!_factions.Contains(faction))
                _factions.Add(faction);
        }
        public void RemoveFaction(FactionType faction) => _factions.Remove(faction);

        
        // ==================== 修改器管理 ====================
        
        public void AddModifier(IdentityModifier modifier)
        {
            _modifiers.Add(modifier);
        }
        
        public void RemoveModifier(IdentityModifier modifier)
        {
            _modifiers.Remove(modifier);
        }
        
        public void RemoveModifiers(System.Predicate<IdentityModifier> match)
        {
            _modifiers.RemoveAll(match);
        }
        
        public void ClearAllModifiers()
        {
            _modifiers.Clear();
        }
        
        // ==================== 判断方法 ====================
        
        public bool IsControllableBy(string playerID) => _ownerPlayerID == playerID;
        
        public bool IsSameTeam(UnitIdentity other)
        {
            if (other == null) return false;
            return Team == other.Team;
        }
        
        public bool IsEnemy(UnitIdentity other)
        {
            if (other == null) return true;
            return Team != other.Team;
        }
        
        public bool IsAlly(UnitIdentity other) => !IsEnemy(other);
        
        /// <summary>
        /// 获取主要势力
        /// </summary>
        public FactionType GetPrimaryFaction()
        {
            var factions = Factions;
            return factions.Count > 0 ? factions[0] : FactionType.Special;
        }
        
        /// <summary>
        /// 是否拥有指定势力
        /// </summary>
        public bool HasFaction(FactionType faction)
        {
            return Factions.Contains(faction);
        }
        
        // ==================== 私有方法 ====================
        
    }

    /// <summary>
    /// 身份修改器
    /// </summary>
    public class IdentityModifier
    {
        public IdentityModifierType Type;
        public int IntValue;
        public TeamType TeamValue;
        public WeaponType WeaponValue;
        public FactionType FactionValue;
        public List<FactionType> FactionsValue;
        
        public IdentityModifier()
        {
            
        }
        // 数值修改器
        public IdentityModifier(IdentityModifierType type, int value)
        {
            Type = type;
            IntValue = value;
        }
        
        // 队伍修改器
        public IdentityModifier(TeamType team)
        {
            Type = IdentityModifierType.Team;
            TeamValue = team;
        }
        
        // 武器修改器
        public IdentityModifier(WeaponType weapon)
        {
            Type = IdentityModifierType.Weapon;
            WeaponValue = weapon;
        }
        
        // 添加势力修改器
        public static IdentityModifier AddFaction(FactionType faction)
        {
            return new IdentityModifier
            {
                Type = IdentityModifierType.AddFaction,
                FactionValue = faction
            };
        }
        
        // 移除势力修改器
        public static IdentityModifier RemoveFaction(FactionType faction)
        {
            return new IdentityModifier
            {
                Type = IdentityModifierType.RemoveFaction,
                FactionValue = faction
            };
        }
        
        // 设置势力列表修改器
        public static IdentityModifier SetFactions(List<FactionType> factions)
        {
            return new IdentityModifier
            {
                Type = IdentityModifierType.SetFactions,
                FactionsValue = factions
            };
        }
    }

    /// <summary>
    /// 身份修改器类型
    /// </summary>
    public enum IdentityModifierType
    {
        Team,               // 队伍
        Weapon,             // 武器
        DeployCostFlat,     // 出战花费固定值
        DeployCostPercent,  // 出战花费百分比
        AddFaction,         // 添加势力
        RemoveFaction,      // 移除势力
        SetFactions,        // 设置势力列表
    }

    /// <summary>
    /// 队伍类型
    /// </summary>
    public enum TeamType
    {
        A = 0,
        B = 1,
        C = 2,
        D = 3,
        E = 4,
        F = 5,
        G = 6,
        H = 7,
        I = 8,
    }
}


