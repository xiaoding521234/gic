using UnityEngine;
using System.Collections.Generic;
using System;

/// <summary>
/// 元素组件 - 管理单位的元素属性
/// </summary>
public class UnitElement : MonoBehaviour, IUnitComponent
{
    private Unit _owner;

    [Header("自身元素")]
    [SerializeField] private ElementType _selfElement = ElementType.Physical;

    [Header("染色元素")]
    [SerializeField] private ElementType _dyedElement = ElementType.Physical;

    // ==================== 修改器列表 ====================
    private List<ElementModifier> _modifiers = new();

    // ==================== 属性 ====================
    public ElementType SelfElement => GetFinalSelfElement();
    public ElementType DyedElement => GetFinalDyedElement();

    // ==================== 初始化 ====================
    public void Init(Unit owner)
    {
        _owner = owner;
        _selfElement = owner.RawData.selfElement;
        _dyedElement = _selfElement;
    }

    // ==================== 最终值计算 ====================

    private ElementType GetFinalSelfElement()
    {
        ElementType result = _selfElement;

        foreach (var mod in _modifiers)
        {
            if (mod.Type == ElementModifierType.SelfElement)
            {
                result = mod.ElementValue;
            }
        }

        return result;
    }

    private ElementType GetFinalDyedElement()
    {
        ElementType result = _dyedElement;

        foreach (var mod in _modifiers)
        {
            if (mod.Type == ElementModifierType.DyedElement)
            {
                result = mod.ElementValue;
            }
        }

        return result;
    }

    // ==================== 便利方法 ====================


    /// <summary>
    /// 染色
    /// </summary>
    public void Dye(ElementType element)
    {
        _dyedElement = element;
    }

    /// <summary>
    /// 清除染色
    /// </summary>
    public void ClearDye()
    {
        _dyedElement = ElementType.Physical;
    }

    // ==================== 修改器管理 ====================

    public void AddModifier(ElementModifier modifier)
    {
        _modifiers.Add(modifier);
    }

    public void RemoveModifier(ElementModifier modifier)
    {
        _modifiers.Remove(modifier);
    }

    public void RemoveModifiers(System.Predicate<ElementModifier> match)
    {
        _modifiers.RemoveAll(match);
    }

    public void ClearAllModifiers()
    {
        _modifiers.Clear();
    }

    // ==================== 设置方法 ====================
    public void SetSelfElement(ElementType value) => _selfElement = value;
    public void SetDyedElement(ElementType value) => _dyedElement = value;
}

/// <summary>
/// 元素修改器
/// </summary>
public class ElementModifier
{
    public ElementModifierType Type;
    public ElementType ElementValue;

    public ElementModifier(ElementModifierType type, ElementType value)
    {
        Type = type;
        ElementValue = value;
    }
}

/// <summary>
/// 元素修改器类型
/// </summary>
public enum ElementModifierType
{
    SelfElement,    // 自身元素
    DyedElement,    // 染色元素
}

