using Unity.VisualScripting;
using UnityEngine;
using GIC.Framework;
using GIC.Data.Event;
using GIC.UI;
using GIC.Battle;
using GIC.Tool;
namespace GIC.Data
{


    public enum TableName
    {
        [InspectorName("UI资源表")]
        UIAssets = 0,

        [InspectorName("UI文本表")]
        UIText = 1,

        [InspectorName("地点名称表")]
        PositionName = 2,

        [InspectorName("物品名称表")]
        ItemName = 3,

        [InspectorName("物品标签表")]
        ItemTag = 4,

        [InspectorName("技能参数基于名称表")]
        SkillBaseType = 5,

        [InspectorName("技能参数名称表")]
        SkillParamName = 6,

        [InspectorName("技能名称表")]
        SkillName = 7,

        [InspectorName("技能类型表")]
        SkillType = 8,

        [InspectorName("技能介绍表")]
        SkillDescription = 9,

        [InspectorName("单位名称表")]
        UnitName = 10,

        [InspectorName("单位势力表")]
        FactionType = 11,

        [InspectorName("单位标签表")]
        UnitTag = 12,

        [InspectorName("单位类型表")]
        UnitType = 13,

        [InspectorName("武器类型表")]
        WeaponType = 14,

        [InspectorName("单位称号表")]
        UnitTitle = 15,

        [InspectorName("单位介绍表")]
        UnitDescription = 16,

        [InspectorName("单位获取描述表")]
        UnitObtainDescription = 17,

        [InspectorName("物品描述表")]
        ItemDescription = 18,

        [InspectorName("弹窗文本表")]
        PopupText = 19,

        [InspectorName("关联名称表")]
        RelatedName = 20,

        [InspectorName("关联描述表")]
        RelatedDescription = 21,

    }
}

