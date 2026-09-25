using UnityEngine;
using System;
using UnityEngine.Localization;
using GIC.Tool;
namespace GIC.Data
{


    /// <summary>
    /// 技能配置资产（2026-09-23 起独立化：每技能一个 SO，不再内嵌 UnitConfig）。
    /// 资产路径约定：Assets/Resources/Configs/Skills/{SkillName 枚举名}.asset；
    /// UnitConfig.unitDataList[].skills = List&lt;SkillConfig&gt; 引用列表（顺序=skillIndex 语义，勿重排）；
    /// 通用技能（Common_Walk 等）多角色共享同一资产——一处改全处生效。
    /// 技能本体字段在 data（SkillData）；时轮时间轴=data.timeline（SkillTimelineAsset，可空）。
    /// </summary>
    [CreateAssetMenu(fileName = "SkillConfig", menuName = "Game/SkillConfig")]
    public class SkillConfig : ScriptableObject
    {
        [Header("技能数据（旧内嵌结构原样迁移；时轮编辑器/UnitData 编辑窗按此编辑）")]
        public SkillData data = new SkillData();

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

            [Header("时轮时间轴（B-S1；null=无时轮兜底=旧即时行为）")]
            public SkillTimelineAsset timeline;

            [Header("效果原子列表（B-1，docs/active/29：空=走旧技能类兜底；非空=数据驱动管线——")]
            [Header("加/改效果=编辑此列表零代码；无注册类且非空→ConfiguredSkill 通用类，docs/18 决策九 D5）")]
            public System.Collections.Generic.List<SkillEffectConfig> effects;

            /// <summary>是否有数据驱动效果（非空=走 EffectCompiler 新管线）</summary>
            public bool HasEffects => effects != null && effects.Count > 0;

            /// <summary>
            /// 移动距离换算（2026-09-23 用户拍板：「配置文件里的10，还要看基于类型。拼接后为10%移速」）——
            /// MoveDistance 参数按 baseType 解释：BasedOnMoveSpeed=百分比×移速（10%×50=5 格）、
            /// Fixed=直读格数；缺参数默认按 10%移速换算（与全员配置一致）。
            /// 消费端：Host=MoveExecutor.MaxMoveDistance（结算权威）、HUD=移动瞄准步数上限（显示同源）。
            /// </summary>
            public int ResolveMoveDistance(int moveSpeed)
            {
                if (customParams != null)
                {
                    foreach (var p in customParams)
                    {
                        if (p.key != SkillParamKey.MoveDistance) continue;
                        if (p.baseType == SkillBaseType.Fixed) return p.value;
                        return moveSpeed * p.value / 100; // BasedOnMoveSpeed 等百分比型基准
                    }
                }
                return moveSpeed * 10 / 100; // 缺参数=默认 10% 移速（30 移速=3 格，与旧回落一致）
            }

            public int GetInt(SkillParamKey key, int defaultValue = 0)
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

            public bool GetBool(SkillParamKey key, bool defaultValue = false)
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

            #region localization Entry 获取方法

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
        public SkillParamKey key;
        public int value;
        public SkillBaseType baseType;

        public SkillParam()
        { }

        public SkillParam(SkillParamKey key, int value, SkillBaseType baseType)
        {
            this.key = key;
            this.value = value;
            this.baseType = baseType;
        }

        /// <summary>
        /// 获取展示值文本（不含颜色）：
        /// 固定值 → "3"
        /// 非固定值 → "100%"（基底名由 GetValueEntry 的 baseEntry 拼接）
        /// </summary>
        public string GetDisplayValueText()
        {
            if (key == SkillParamKey.None) return null;

            if (baseType == SkillBaseType.Fixed)
            {
                return value.ToString();
            }
            return value.ToString() + "%";
        }

        /// <summary>
        /// 获取技能参数展示值（右侧）的 Entry：
        /// 固定值 → "3"
        /// 上下文百分比 → "50%"
        /// 非固定值 → "100%攻击力"（类型名通过本地化）
        /// </summary>
        public TextEntry GetValueEntry()
        {
            if (key == SkillParamKey.None)
            {
                return null;
            }
            string displayValue = GetDisplayValueText();
            // 固定值与上下文百分比：右侧只显示数值本身（百分比自带 %，无基底名）
            if (baseType == SkillBaseType.Fixed || baseType == SkillBaseType.Percent)
            {
                return new TextEntry(null, displayValue);
            }
            TextEntry baseEntry = baseType.GetEntry();
            baseEntry.leadingSeparator = displayValue;
            return baseEntry;
        }

        /// <summary>
        /// 获取技能参数名称（左）的本地化 Entry
        /// </summary>
        public TextEntry GetNameEntry()
        {
            if (key == SkillParamKey.None)
            {
                return null;
            }
            return key.GetEntry();
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

        [InspectorName("理智")]
        BasedOnSanity = 10,

        [InspectorName("上下文百分比")]
        Percent = 11,
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
}



