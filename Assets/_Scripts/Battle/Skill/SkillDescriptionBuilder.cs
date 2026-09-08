using System.Text;
using System.Collections.Generic;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.UI;
using GIC.Tool;
namespace GIC.Battle
{


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
        /// <param name="template">描述模板（来自本地化表，任意语言），含 {ParamKey} 占位符</param>
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
        /// 上下文百分比 → "50%" → "<color=#FFD700>50%</color>"
        /// 非固定值 → "40%攻击力" → "<color=#FFD700>40%攻击力</color>"
        /// 基底名从 SkillBaseType 本地化表按当前语言读取（勿硬编码中文——描述模板是五语言的）
        /// </summary>
        private static string GetColoredValue(SkillParam param)
        {
            if (param.baseType == SkillBaseType.Fixed)
            {
                return $"<color={ValueColor}>{param.value}</color>";
            }
            if (param.baseType == SkillBaseType.Percent)
            {
                // 上下文百分比：基底由技能语境提供，展示值只含数字+%，结构（"基于失去护盾的…"）在描述模板里
                return $"<color={ValueColor}>{param.value}%</color>";
            }

            // 非固定值: "40%攻击力"——基底名走本地化表（param.GetValueEntry 的 TextEntry 已按当前语言解析，
            // 此处取其静态前缀拼 %，保持与参数列表右侧一致的展示来源）
            string baseName = GetLocalizedBaseName(param.baseType);
            return $"<color={ValueColor}>{param.value}%{baseName}</color>";
        }

        /// <summary>
        /// 获取基础类型的本地化名（跟随当前语言，用于描述文本内嵌）
        /// 同步读 SkillBaseType 表当前语言值；表未加载/缺条目时退回简写兜底
        /// </summary>
        private static string GetLocalizedBaseName(SkillBaseType baseType)
        {
            var table = UnityEngine.Localization.Settings.LocalizationSettings.StringDatabase
                .GetTable(TableName.SkillBaseType.ToString());
            var entry = table?.GetEntry(baseType.ToString());
            return entry?.Value ?? GetBaseTypeShortNameFallback(baseType);
        }

        /// <summary>
        /// 兜底简写名（本地化表缺失时使用，避免描述出现空基底）
        /// </summary>
        private static string GetBaseTypeShortNameFallback(SkillBaseType baseType)
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
                SkillBaseType.BasedOnSanity => "理智",
                _ => "",
            };
        }
    }

}
