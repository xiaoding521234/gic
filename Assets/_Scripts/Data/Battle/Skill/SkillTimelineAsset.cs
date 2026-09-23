using System;
using System.Collections.Generic;
using UnityEngine;
namespace GIC.Data
{


    /// <summary>
    /// 时轮轨道类型（B-S1；模型对标 EGamePlay ExecuteClipType 的 GIC 化）——
    /// clip 平铺存储，编辑器（B-S2）与消费方按轨道分组解释载荷。
    /// </summary>
    public enum SkillTrackType
    {
        [InspectorName("目标声明")] Targeting = 0,
        [InspectorName("动作")] Action = 1,
        [InspectorName("判定")] Judgment = 2,
        [InspectorName("特效")] Vfx = 3,
        [InspectorName("音效")] Sfx = 4,
        [InspectorName("资源")] Resource = 5,
        [InspectorName("位移")] Movement = 6,
    }

    /// <summary>
    /// 目标声明（瞄准模式）——HUD 瞄准框架数据驱动化的声明位（B-S2+ 消费，当前只存不接线）
    /// </summary>
    public enum SkillAimMode
    {
        [InspectorName("十字方向")] CrossDirection = 0,
        [InspectorName("坐标格")] Cell = 1,
        [InspectorName("单位指向")] TargetUnit = 2,
        [InspectorName("无目标")] None = 3,
    }

    /// <summary>
    /// 判定轨 clip 子类型（trackType=Judgment 时按 kind 解释）
    /// </summary>
    public enum SkillJudgmentKind
    {
        [InspectorName("直线投射物")] LineProjectile = 1,
        [InspectorName("整线迸发")] LineBurst = 2,
        [InspectorName("指向单位")] TargetedUnit = 3,  // 延奏/契约类（单位指向，docs/18 决策二）——后续批次接线
        [InspectorName("范围迸发")] AreaBurst = 4,     // 以格为中心 AoE——占位（后续技能用）
    }

    /// <summary>
    /// 时轮 clip（平铺 union 载荷，风格同 BattleCommand；按 trackType 部分字段有效）。
    ///
    /// 分工铁律（防双源漂移，2026-09-23 审查 Y7 教训）：**时间与规格归时轮**——何时发射（startTime）、
    /// 连发间隔（hitInterval）、投射物速度/体积/射程；**数值归 SkillParamKey**——每发伤害百分比（Damage）、
    /// 发数（DamageCount）、消耗（EnergyCost）。clip 不重复存伤害数值；发数默认读参数表。
    /// </summary>
    [Serializable]
    public class SkillTimelineClip
    {
        [Header("轨道与时刻")]
        public SkillTrackType trackType = SkillTrackType.Judgment;

        /// <summary>起始时刻（秒；片播放起点=0——前摇时长即发射事件时刻）</summary>
        public float startTime;

        /// <summary>结束时刻（秒；持续型 clip 有效，瞬时事件=startTime）</summary>
        public float endTime;

        /// <summary>轨道内子类型（Judgment=SkillJudgmentKind；表现轨 B-S2/B-S3 定义各自枚举）</summary>
        public int kind;

        [Header("判定轨载荷（trackType=Judgment 有效）")]

        /// <summary>连发间隔（秒；发数 &gt;1 时的逐发间隔——两发箭矢 0.05s 即此处）</summary>
        public float hitInterval;

        /// <summary>投射物速度（格/s；0=用 BattleMetrics.ProjectileSpeed 默认）</summary>
        public float projectileSpeed;

        /// <summary>判定圆柱直径（0=用 BattleMetrics.UnitCylinderDiameter 默认）</summary>
        public float hitDiameter;

        /// <summary>射程上限（格；0=用 ProjectileRule.MaxRange 默认）</summary>
        public int maxRange;

        [Header("表现轨载荷（动作/音效/特效轨；B-S3 接素材）")]

        /// <summary>表现资源名（音效名/动作段名/特效名——B-S3 素材落地后接线消费）</summary>
        public string cueName;
    }

    /// <summary>
    /// 时轮（SkillTimeline）资产——技能的演出与判定时间轴单一真源（B-S1 落地判定侧）。
    /// 对标 EGamePlay ExecutionObject（TotalTime/TargetInputType/ExecuteClips 三件）。
    ///
    /// 挂接：SkillConfig.SkillData.timeline（null=无时轮兜底=旧即时行为）；
    /// 双消费：Host 侧=判定事件时刻表（BaseSkill.ResolveEffects 编译为发射声明/逐发判定）；
    /// 客户端=表现视图（SkillCast 命令触发，按 skillID 加载资产播动作/音效/特效轨——B-S3 素材落地后接线）。
    /// 资产路径约定：Assets/Resources/Configs/SkillTimelines/{SkillName}.asset。
    /// </summary>
    [CreateAssetMenu(fileName = "SkillTimeline", menuName = "Game/SkillTimeline")]
    public class SkillTimelineAsset : ScriptableObject
    {
        [Header("总时长（秒；含后摇——演出收束/取消窗口消费，B-S3）")]
        public float totalTime;

        [Header("目标声明（瞄准模式；HUD 瞄准数据驱动化 B-S2+ 消费，当前只存不接线）")]
        public SkillAimMode aimMode = SkillAimMode.CrossDirection;

        [Header("时间轴 clip 列表（平铺；编辑器按轨道分组可视化）")]
        public List<SkillTimelineClip> clips = new List<SkillTimelineClip>();
    }

    /// <summary>时轮资产的读取助手（Host/客户端共用）</summary>
    public static class SkillTimelineQuery
    {
        /// <summary>
        /// 取指定轨道的全部 clip（startTime 升序副本——编辑数据的列表顺序不可当逻辑依据）
        /// </summary>
        public static List<SkillTimelineClip> ClipsOf(SkillTimelineAsset timeline, SkillTrackType track)
        {
            var result = new List<SkillTimelineClip>();
            if (timeline == null) return result;
            foreach (var clip in timeline.clips)
                if (clip.trackType == track) result.Add(clip);
            result.Sort((a, b) => a.startTime.CompareTo(b.startTime));
            return result;
        }

        /// <summary>判定轨中指定子类型的 clip（升序）；无时轮返回空表（调用方走兜底）</summary>
        public static List<SkillTimelineClip> JudgmentClips(SkillTimelineAsset timeline, SkillJudgmentKind kind)
        {
            var result = new List<SkillTimelineClip>();
            if (timeline == null) return result;
            foreach (var clip in timeline.clips)
                if (clip.trackType == SkillTrackType.Judgment && clip.kind == (int)kind) result.Add(clip);
            result.Sort((a, b) => a.startTime.CompareTo(b.startTime));
            return result;
        }
    }
}
