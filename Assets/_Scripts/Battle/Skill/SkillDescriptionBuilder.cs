using System.Text;
using System.Collections.Generic;
using GIC.Data;
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
        /// 关联面板描述构建（2026-10-07 决策五十六）：Buff/Concept 类 link 的 RelatedDescription 模板替换——
        /// 先走技能参数通道（Build 同链：{SkillParamKey} 占位+永久声明渲染），残余 {Key} 再按
        /// BuffConfig.关联名 反查资产解析——延奏类与行为族 buff 数值均=BuffConfig 单源
        /// （2026-10-07 返修三：延奏技能资产已剥离 buff 参数，{BonusPerStack}/{StackLimit}/{DurationTurns}
        /// 由 StatBuffConfig 供值），数值统一金色高亮，与技能描述同观感。
        /// 2026-10-07 返修四：占位符与技能参数**同构渲染**（value+baseType 二元组）——%与基底名随数值
        /// **整体金色**（如「100%攻击力」全金），模板不再自带 %/基底名措辞；baseType 由家族覆写
        /// RelatedPlaceholderBaseType 提供（null=纯数字、Percent=N%、BasedOnX=N%基底名——与
        /// GetColoredValue 同口径，基底名按当前语言本地化）。
        /// </summary>
        public static string BuildRelated(string template, SkillParam[] skillParams, GIC.Data.BuffConfig buffConfig)
        {
            var text = Build(template, skillParams);
            if (string.IsNullOrEmpty(text) || buffConfig == null) return text;

            var sb = new StringBuilder(text);
            foreach (var key in GIC.Data.BuffConfig.RelatedPlaceholderKeys)
            {
                var placeholder = "{" + key + "}";
                if (!text.Contains(placeholder)) continue;
                var value = buffConfig.ResolveRelatedPlaceholder(key);
                if (!value.HasValue) continue;

                string rendered;
                if (key == GIC.Data.BuffConfig.KeyStatDurationTurns && value.Value < 0)
                {
                    // 永久声明（持续回合<0 与技能参数 Duration<0 同口径：本地化「永久」金色单源）
                    rendered = GIC.Data.SkillParam.LocalizedPermanentDisplay();
                }
                else
                {
                    var baseType = buffConfig.RelatedPlaceholderBaseType(key);
                    if (baseType == SkillBaseType.Percent)
                        rendered = value.Value + "%";
                    else if (baseType.HasValue)
                        rendered = value.Value + "%" + GetLocalizedBaseName(baseType.Value);
                    else
                        rendered = value.Value.ToString();
                }
                sb.Replace(placeholder, $"<color={ValueColor}>{rendered}</color>");
            }
            return sb.ToString();
        }

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
            // 永久声明（Duration<0，2026-10-07 拍板「持续时间改为无限」）：占位符渲染本地化「永久」
            // （金色）而非 -1——与参数表右侧展示同口径单源（SkillParam.LocalizedPermanentDisplay）
            if (param.key == SkillParamKey.Duration && param.baseType == SkillBaseType.Fixed && param.value < 0)
                return $"<color={ValueColor}>{SkillParam.LocalizedPermanentDisplay()}</color>";

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
