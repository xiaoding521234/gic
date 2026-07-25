using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 移动组件 - 记录移动相关的配置数据和当前受到的力
/// </summary>
public class UnitMoveable : MonoBehaviour, IUnitComponent
{
    private Unit _owner;

    [Header("常态移动方式")]
    [SerializeField] private ForceType _normalMoveType = ForceType.Walk;

    [Header("碰撞规则")]
    [SerializeField] private bool _blockAllies = true;
    [SerializeField] private bool _blockEnemies = true;
    [SerializeField] private bool _blockedByEnemies = true;

    [Header("当前状态")]
    [SerializeField] private List<ForceData> _activeForces = new();

    private List<MoveableModifier> _modifiers = new();

    public ForceType NormalMoveType => GetFinalNormalMoveType();
    public bool BlockAllies => GetFinalBlockAllies();
    public bool BlockEnemies => GetFinalBlockEnemies();
    public bool BlockedByEnemies => GetFinalBlockedByEnemies();
    public IReadOnlyList<ForceData> ActiveForces => _activeForces;

    public void Init(Unit owner)
    {
        _owner = owner;
        _activeForces.Clear();

        _normalMoveType = owner.RawData.normalMoveType;
        _blockAllies = owner.RawData.blockAllies;
        _blockEnemies = owner.RawData.blockEnemies;
        _blockedByEnemies = owner.RawData.blockedByEnemies;
    }

    private ForceType GetFinalNormalMoveType()
    {
        foreach (var mod in _modifiers)
            if (mod.Type == MoveableModifierType.NormalMoveType) return mod.ForceTypeValue;
        return _normalMoveType;
    }

    private bool GetFinalBlockAllies() => GetFinalBool(MoveableModifierType.BlockAllies, _blockAllies);
    private bool GetFinalBlockEnemies() => GetFinalBool(MoveableModifierType.BlockEnemies, _blockEnemies);
    private bool GetFinalBlockedByEnemies() => GetFinalBool(MoveableModifierType.BlockedByEnemies, _blockedByEnemies);

    private bool GetFinalBool(MoveableModifierType type, bool defaultValue)
    {
        foreach (var mod in _modifiers)
            if (mod.Type == type) return mod.BoolValue;
        return defaultValue;
    }

    public void AddForce(ForceData force) => _activeForces.Add(force);
    public void RemoveForce(ForceData force) => _activeForces.Remove(force);
    public void ClearForces() => _activeForces.Clear();
    public bool HasForceOfType(ForceType type) => _activeForces.Exists(f => f.Type == type);

    public int GetTotalForceInDirection(Direction2D direction)
    {
        int total = 0;
        foreach (var force in _activeForces)
            if (force.Direction == direction) total += force.Magnitude;
        return total;
    }

    public void AddModifier(MoveableModifier modifier) => _modifiers.Add(modifier);
    public void RemoveModifier(MoveableModifier modifier) => _modifiers.Remove(modifier);
    public void RemoveModifiers(System.Predicate<MoveableModifier> match) => _modifiers.RemoveAll(match);
    public void ClearAllModifiers() => _modifiers.Clear();

    public void SetNormalMoveType(ForceType value) => _normalMoveType = value;
    public void SetBlockAllies(bool value) => _blockAllies = value;
    public void SetBlockEnemies(bool value) => _blockEnemies = value;
    public void SetBlockedByEnemies(bool value) => _blockedByEnemies = value;
}