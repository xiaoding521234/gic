using System.Text;
using System.Collections.Generic;

/// <summary>
/// 技能描述动态生成器
/// 将描述模板中的 {ParamKey} 占位符替换为带颜色的参数值
/// 模板格式: "造成{Count}次{Damage}火伤" → "造成<color=#FFD700>4</color>次<color=#FFD700>40%攻击力</color>火伤"
/// </summary>
public static class SkillDescriptionBuilder
{
    /// <summary>
    /// 数值着色（金色）
    /// </summary>
    private const string ValueColor = "#FFD700";

    /// <summary>
    /// 根据模板和参数列表构建最终描述文本
    /// </summary>
    /// <param name="template">描述模板（来自本地化表），含 {ParamKey} 占位符</param>
    /// <param name="parameters">技能参数列表</param>
    /// <returns>替换占位符后的带颜色 TMP 富文本</returns>
    public static string Build(string template, SkillParam[] parameters)
    {
        if (string.IsNullOrEmpty(template)) return template;
        if (parameters == null || parameters.Length == 0) return template;

        // 构建 key → colored value 的映射
        var paramMap = new Dictionary<string, string>();
        foreach (var param in parameters)
        {
            if (param.key == SkillParamKey.None) continue;

            string placeholder = "{" + param.key.ToString() + "}";
            if (template.Contains(placeholder))
            {
                string coloredValue = GetColoredValue(param);
                if (!string.IsNullOrEmpty(coloredValue))
                    paramMap[placeholder] = coloredValue;
            }
        }

        if (paramMap.Count == 0) return template;

        // 逐个替换
        StringBuilder sb = new StringBuilder(template);
        foreach (var kvp in paramMap)
        {
            sb.Replace(kvp.Key, kvp.Value);
        }

        return sb.ToString();
    }

    /// <summary>
    /// 获取单个参数的带颜色展示值
    /// 固定值 → "4" → "<color=#FFD700>4</color>"
    /// 非固定值 → "40%攻击力" → "<color=#FFD700>40%攻击力</color>"
    /// </summary>
    private static string GetColoredValue(SkillParam param)
    {
        if (param.baseType == SkillBaseType.Fixed)
        {
            return $"<color={ValueColor}>{param.value}</color>";
        }

        // 非固定值: "40%攻击力"
        // 类型名通过本地化获取，但描述中用简写
        string baseName = GetBaseTypeShortName(param.baseType);
        return $"<color={ValueColor}>{param.value}%{baseName}</color>";
    }

    /// <summary>
    /// 获取基础类型的简写名（用于描述文本内嵌）
    /// </summary>
    private static string GetBaseTypeShortName(SkillBaseType baseType)
    {
        return baseType switch
        {
            SkillBaseType.BasedOnAttack => "攻击力",
            SkillBaseType.BasedOnMaxHealth => "最大生命值",
            SkillBaseType.BasedOnCurrentHealth => "当前生命值",
            SkillBaseType.BasedOnLostHealth => "已损生命值",
            SkillBaseType.BasedOnDefense => "防御力",
            SkillBaseType.BasedOnTargetMaxHealth => "目标最大生命值",
            SkillBaseType.BasedOnTargetCurrentHealth => "目标当前生命值",
            SkillBaseType.BasedOnTargetLostHealth => "目标已损生命值",
            SkillBaseType.BasedOnMoveSpeed => "移速",
            _ => "",
        };
    }
}
