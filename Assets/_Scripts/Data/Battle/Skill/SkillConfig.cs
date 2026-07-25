using UnityEngine;
using System;
using System.Collections.Generic;
using UnityEngine.Localization;

/// <summary>
/// 技能配置 - 支持多个技能数据
/// </summary>
[CreateAssetMenu(fileName = "SkillConfig", menuName = "Game/SkillConfig")]
public class SkillConfig : ScriptableObject
{
    [Serializable]
    public class SkillData
    {

        [Header("技能标识")]
        public SkillName skillID;

        [Header("基础信息")]
        public Sprite icon;

        [Header("技能类型")]
        public SkillType skillType = SkillType.Normal;

        [Header("自定义参数")]
        [SerializeField] public SkillParam[] customParams;

        public int GetInt(string key, int defaultValue = 0)
        {
            if (customParams != null)
            {
                foreach (var p in customParams)
                {
                    if (p.key == key) return p.value;
                }
            }
            return defaultValue;
        }

        public bool GetBool(string key, bool defaultValue = false)
        {
            if (customParams != null)
            {
                foreach (var p in customParams)
                {
                    if (p.key == key) return p.value != 0;
                }
            }
            return defaultValue;
        }

        #region 本地化 Entry 获取方法

        /// <summary>
        /// 获取技能名称的 Entry（用于 TextCombiner）
        /// </summary>
        public TextEntry GetNameEntry()
        {
            return skillID.GetEntry();
        }

        /// <summary>
        /// 获取技能类型的 Entry（用于 TextCombiner）
        /// </summary>
        public TextEntry GetTypeEntry()
        {
            return skillType.GetEntry();
        }

        /// <summary>
        /// 获取技能描述的 Entry（用于 TextCombiner）
        /// </summary>
        public TextEntry GetDescriptionEntry()
        {
            // 使用 SkillDescription 表，Key 为 skillID 的字符串
            var localizedString = new LocalizedString(TableName.SkillDescription.ToString(), skillID.ToString());
            return new TextEntry(localizedString, "");
        }

        #endregion
    }
}


/// <summary>
/// 技能参数键值对
/// </summary>
[Serializable]
public class SkillParam
{
    public string key;
    public int value;
    public SkillBaseType baseType;

    public SkillParam()
    { }

    public SkillParam(string key, int value, SkillBaseType baseType)
    {
        this.key = key;
        this.value = value;
        this.baseType = baseType;
    }

    private string GetDisplayValue()
    {
        // 固定值不需要加 %
        if (baseType == SkillBaseType.Fixed)
        {
            return value.ToString();
        }
        return value.ToString() + "%";
    }

    /// <summary>
    /// 获取技能参数展示值（右侧）：
    /// 固定值 → 直接显示数值 如 "3"
    /// 非固定值 → "100%攻击力" 格式
    /// </summary>
    public TextEntry GetValueEntry()
    {
        if (string.IsNullOrEmpty(key))
        {
            return null;
        }
        string displayValue = GetDisplayValue();
        if (baseType == SkillBaseType.Fixed)
        {
            // 固定值：直接返回纯文本，不需要加基于类型的后缀
            return new TextEntry(null, displayValue);
        }
        TextEntry baseEntry = GetBaseTypeEntry();
        baseEntry.leadingSeparator = displayValue;
        return baseEntry;
    }

    /// <summary>
    /// 获取技能参数名称（左）的本地化 Entry
    /// </summary>
    public TextEntry GetNameEntry()
    {
        if (string.IsNullOrEmpty(key))
        {
            return null;
        }
        var localizedString = new LocalizedString(TableName.SkillParamName.ToString(), key);
        return new TextEntry(localizedString, "");
    }

    /// <summary>
    /// 获取技能参数基础类型的本地化 Entry（用于 TextCombiner）
    /// </summary>
    private TextEntry GetBaseTypeEntry()
    {
        return baseType.GetEntry();
    }
}
/// <summary>
/// 数值基于类型枚举
/// </summary>
public enum SkillBaseType
{
    [InspectorName("固定值")]
    Fixed = 0,

    [InspectorName("攻击力")]
    BasedOnAttack = 1,

    [InspectorName("最大生命值")]
    BasedOnMaxHealth = 2,

    [InspectorName("当前生命值")]
    BasedOnCurrentHealth = 3,

    [InspectorName("已损生命值")]
    BasedOnLostHealth = 4,

    [InspectorName("防御力")]
    BasedOnDefense = 5,

    [InspectorName("目标最大生命值")]
    BasedOnTargetMaxHealth = 6,

    [InspectorName("目标当前生命值")]
    BasedOnTargetCurrentHealth = 7,

    [InspectorName("目标已损生命值")]
    BasedOnTargetLostHealth = 8,

    [InspectorName("移速")]
    BasedOnMoveSpeed = 9,
}

/// <summary>
/// BaseType 扩展方法
/// </summary>
public static class SkillBaseTypeExtensions
{
    /// <summary>
    /// 获取 BaseType 在本地化表中的 Entry Key
    /// </summary>
    private static string GetEntryKey(this SkillBaseType baseType)
    {
        return baseType.ToString();
    }

    /// <summary>
    /// 创建用于本地化系统的 LocalizedString 对象
    /// </summary>
    private static LocalizedString GetLocalizedString(this SkillBaseType baseType)
    {
        return new LocalizedString(TableName.SkillBaseType.ToString(), baseType.GetEntryKey());
    }

    /// <summary>
    /// 创建 Entry 对象（用于 TextCombiner）
    /// </summary>
    /// <param name="leadingSeparator">前置连接符</param>
    public static TextEntry GetEntry(this SkillBaseType baseType, string leadingSeparator = "")
    {
        LocalizedString localizedString = baseType.GetLocalizedString();
        return new TextEntry(localizedString, leadingSeparator);
    }
}