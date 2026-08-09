using System.Collections.Generic;
using UnityEngine;
using GIC.Framework;
using GIC.Data.Event;
using GIC.UI;
using GIC.Battle;
using GIC.Tool;
namespace GIC.Data
{


    /// <summary>
    /// 卡牌配置统一入口 — UnitData 和 ItemData 通过适配器实现此接口，
    /// 让展示层不再需要判断 CardType。
    /// </summary>
    public interface ICardConfig
    {
        int                  StarLevel         { get; }
        int                  SortOrder         { get; }   // 主排序键
        int                  ConfigIndex       { get; }   // 配置文件中的顺序（同主键同星级的次级排序）
        BackpackTab          GetBackpackTab();           // 背包分页
        TextEntry            GetNameEntry();
        TextEntry            GetDescriptionEntry();
        IReadOnlyList<Sprite> GetSprites();
        Sprite               GetSprite(int index);
        int                  GetTotalSkins();
        int                  GetTagCount();
        TextEntry            GetTagEntry(int index);
        Color                GetStarColor();
    }

}



