using UnityEngine;
using System;
using UnityEngine.Localization;
using UnityEngine.Video;
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

            [Header("动作视频（B-S4c 战技/爆发动作轨，2026-10-04；2026-10-05 决策四十四扩到 Move 型=循环态移动片）：绿幕 mp4 动作片")]
            [Header("（战技/爆发=一次性：时长≈timeline.totalTime——拉弓/释放/收势整段，发射时刻由裁剪对齐时轮 startTime；")]
            [Header("SkillCast 片头从头播、播完自动回待机循环。Move 型=循环态：SkillCast 登记+Move 命令片起止，")]
            [Header("行走期间循环、片末回待机、速度乘回放速度（快进不脚滑）。null=无动作视频=待机照播）")]
            public VideoClip 动作视频;

            [Tooltip("动作片缩放补偿（1=不补偿；=idle 片主体高/动作片主体高——宽幅 16:9 动作片构图主体小，播放放大回 idle 主体视觉大小，UnitView.PlayActionVideo 内乘 quad scale；安柏宽幅构图实测=1.29（idle 主体 554px/动作片主体 430px））")]
            public float 动作片缩放补偿 = 1f;

            [Tooltip("动作片播放速度倍率（1=原速；0/负=按 1 处理。运行时实际速度=战斗回放速度×本倍率——校准动作内容节拍与时轮判定时刻对齐（安柏二连射：第二箭松弦对齐第二发 0.30+0.39s）或片长铺满 totalTime；时轮编辑器「动作片校准」区实时预览所见即所得）")]
            public float 动作片播放速度 = 1f;

            [Tooltip("动作片位置微调（世界单位=格；X=水平、Y=垂直，相对待机 quad 基准位）：校准宽幅动作片构图主体与待机片主体视觉重合（观感统一）；播放期间偏移、播完/被打断随 RestoreIdleVideoSurface 恢复基准位。时轮编辑器「动作片校准」区实时预览所见即所得")]
            public Vector2 动作片位置偏移;

            [Header("效果原子列表（B-1，docs/active/29：空=走旧技能类兜底；非空=数据驱动管线——")]
            [Header("加/改效果=编辑此列表零代码；无注册类且非空→ConfiguredSkill 通用类，docs/18 决策九 D5）")]
            public System.Collections.Generic.List<SkillEffectConfig> effects;

            [Header("消耗声明（统一消耗模型，docs/active/30：技能消耗=数据驱动 (资源,数量) 列表——")]
            [Header("C-2 存量迁移完成：消耗运行时唯一真源=本列表，**空=免费技能**；EnergyCost 参数仅描述渲染")]
            public System.Collections.Generic.List<SkillCostEntry> costs;

            /// <summary>是否声明了消耗条目（非空=走 ResourceGate 统一管道）</summary>
            public bool HasCosts => costs != null && costs.Count > 0;

            /// <summary>是否有数据驱动效果（非空=走 EffectCompiler 新管线）</summary>
            public bool HasEffects => effects != null && effects.Count > 0;

            /// <summary>是否单位指向型技能（延奏/契约=类型固有；爆发=时轮 aimMode 声明——
            /// B-3 ② 芭芭拉闪耀奇迹首个消费者，docs/11「方向模式数据驱动化」在爆发档的落地）：
            /// 三消费方=Host EffectCompiler.CompileSkill（目标校验+OnCast，无判定轨）、
            /// HUD（IsLineSkill 判定与瞄准格域）、AI 脑（无方向域的目标估值档）。</summary>
            public bool IsUnitTargeted()
            {
                if (skillType == SkillType.Enso || skillType == SkillType.Contract) return true;
                return skillType == SkillType.Burst
                    && timeline != null
                    && timeline.aimMode == SkillAimMode.TargetUnit;
            }

            /// <summary>是否无目标自施放爆发（时轮 aimMode=None 声明——首个=凯亚凛冽轮舞 buff 型爆发，
            /// 2026-10-01 拍板「凯亚爆发实际并不是召唤，与歌声之环类似，都是buff」）：无判定轨、
            /// 无方向/目标域，OnCast 效果（filter=Caster=自身）直接产出。
            /// 三消费方=Host EffectCompiler.CompileSkill（跳过判定编译）、HUD（IsLineSkill 排除+
            /// 瞄准域=自身格）、AI 脑（自身增益估值档）。</summary>
            public bool IsSelfCast()
            {
                return skillType == SkillType.Burst
                    && timeline != null
                    && timeline.aimMode == SkillAimMode.None;
            }

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

        /// <summary>永久声明展示文本（2026-10-07 拍板「加攻/减防持续时间改为无限」：Duration&lt;0 =
        /// 永久——展示=本地化「永久」而非负数；SkillParamName 表 Permanent 键，表未加载/缺条目回落
        /// 中文）。GetDisplayValueText 与 SkillDescriptionBuilder 占位符渲染共用单源</summary>
        public static string LocalizedPermanentDisplay()
        {
            var table = UnityEngine.Localization.Settings.LocalizationSettings.StringDatabase
                .GetTable(TableName.SkillParamName.ToString());
            return table?.GetEntry("Permanent")?.Value ?? "永久";
        }

        /// <summary>
        /// 获取展示值文本（不含颜色）：
        /// 固定值 → "3"
        /// 非固定值 → "100%"（基底名由 GetValueEntry 的 baseEntry 拼接）
        /// </summary>
        public string GetDisplayValueText()
        {
            if (key == SkillParamKey.None) return null;

            // 永久声明（Duration<0）：参数表右侧展示本地化「永久」——勿把 -1 直显给玩家
            if (key == SkillParamKey.Duration && baseType == SkillBaseType.Fixed && value < 0)
                return LocalizedPermanentDisplay();

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



