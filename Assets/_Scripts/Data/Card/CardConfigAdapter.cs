using System.Collections.Generic;
using UnityEngine;
using GIC.Framework;
using GIC.Data.Event;
using GIC.UI;
using GIC.Battle;
using GIC.Tool;
namespace GIC.Data
{


    // ─────────────────────────────────────────────
    // 两个轻量适配器 — 不改 UnitData / ItemData 的序列化结构，
    // 只提供 ICardConfig 接口的委托实现。
    // ─────────────────────────────────────────────

    public readonly struct UnitConfigAdapter : ICardConfig
    {
        private readonly UnitConfig.UnitData _d;
        public UnitConfigAdapter(UnitConfig.UnitData data) => _d = data;

        public int StarLevel => _d?.starLevel ?? 0;

        public int SortOrder =>
            _d?.factions != null && _d.factions.Length > 0
                ? (int)_d.factions[0]
                : int.MaxValue;

        public int ConfigIndex
        {
            get
            {
                if (_d == null || CardConfigResolver.UnitConfig == null) return int.MaxValue;
                int idx = CardConfigResolver.UnitConfig.GetUnitIndex(_d.unitName);
                return idx >= 0 ? idx : int.MaxValue;
            }
        }

        public BackpackTab GetBackpackTab() => _d?.unitType switch
        {
            UnitType.Creation => BackpackTab.Creation,
            _ => BackpackTab.Character,
        };

        public TextEntry GetNameEntry()        => _d?.GetNameEntry();
        public TextEntry GetDescriptionEntry() => _d?.GetDescriptionEntry();

        public IReadOnlyList<Sprite> GetSprites() => _d?.cards;
        public Sprite GetSprite(int index)        => _d?.GetCard(index);
        public int    GetTotalSkins()              => _d?.cards?.Count ?? 0;

        public int      GetTagCount()                        => _d?.tags?.Length ?? 0;
        public TextEntry GetTagEntry(int index)              => _d?.tags?[index].GetEntry();
        public Color    GetStarColor()                       => StarColor.GetStarColor(_d?.starLevel ?? 0);
    }

    public readonly struct ItemConfigAdapter : ICardConfig
    {
        private readonly ItemConfig.ItemData _d;
        public ItemConfigAdapter(ItemConfig.ItemData data) => _d = data;

        public int StarLevel => _d?.starLevel ?? 0;

        public int SortOrder =>
            _d != null
                ? (int)_d.subType
                : int.MaxValue;

        public int ConfigIndex
        {
            get
            {
                if (_d == null || CardConfigResolver.ItemConfig == null) return int.MaxValue;
                int idx = CardConfigResolver.ItemConfig.GetItemIndex(_d.itemID);
                return idx >= 0 ? idx : int.MaxValue;
            }
        }

        public BackpackTab GetBackpackTab() => _d?.subType.ToBackpackTab() ?? BackpackTab.Material;

        public TextEntry GetNameEntry()        => _d?.itemID.GetEntry();
        public TextEntry GetDescriptionEntry() => _d?.GetDescriptionEntry();

        public IReadOnlyList<Sprite> GetSprites() => _d?.icon;
        public Sprite GetSprite(int index)        => _d?.GetIcon(index);
        public int    GetTotalSkins()              => _d?.icon?.Count ?? 0;

        public int      GetTagCount()           => 1;
        public TextEntry GetTagEntry(int index) => _d?.subType.GetEntry();
        public Color    GetStarColor()          => StarColor.GetStarColor(_d?.starLevel ?? 0);
    }

}



