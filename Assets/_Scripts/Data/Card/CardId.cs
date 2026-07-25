using System;
using UnityEngine;

/// <summary>
/// 统一卡牌标识 — 将 CardType + 数值封为一个值类型。
/// </summary>
[Serializable]
public struct CardId : IEquatable<CardId>
{
    public CardType cardType;
    public int      value;

    // ── 构造 ──────────────────────────────────
    public CardId(CardType type, int v) { cardType = type; value = v; }
    public CardId(UnitName name)        { cardType = CardType.Unit; value = (int)name; }
    public CardId(ItemName name)        { cardType = CardType.Item; value = (int)name; }

    // ── 隐式转换，让旧调用 (UnitName → CardId) 自动通过 ─
    public static implicit operator CardId(UnitName name) => new CardId(name);
    public static implicit operator CardId(ItemName name) => new CardId(name);

    // ── 类型安全的取值 ─────────────────────────
    public UnitName AsUnitName() => (UnitName)value;
    public ItemName AsItemName() => (ItemName)value;

    // ── IEquatable ────────────────────────────
    public bool Equals(CardId other) => cardType == other.cardType && value == other.value;
    public override bool   Equals(object obj) => obj is CardId o && Equals(o);
    public override int    GetHashCode()      => ((int)cardType * 397) ^ value;
    public static bool     operator ==(CardId a, CardId b) => a.Equals(b);
    public static bool     operator !=(CardId a, CardId b) => !a.Equals(b);

    public override string ToString() => $"{cardType}:{value}";

    // 常用预置
    public static readonly CardId None = new CardId(CardType.Item, 0);
}

// ── CardType 枚举（从 Card.cs 移出，供全局使用） ──
public enum CardType
{
    Item = 0,
    Unit = 1,
}
