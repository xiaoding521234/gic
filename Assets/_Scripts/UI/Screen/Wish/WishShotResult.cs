using System.Collections.Generic;
using GIC.Framework;
using GIC.Battle;
using GIC.Data;

namespace GIC.UI
{
    /// <summary>
    /// 一次射击的完整预计算结果 — 业务逻辑全部执行完毕后的动画步骤清单。
    /// 表现层按 steps 顺序播放，不调用任何 WishManager 方法。
    /// </summary>
    public class WishShotResult
    {
        /// <summary>第几发（0-based）</summary>
        public int shotIndex;

        /// <summary>最终星级</summary>
        public int finalStarLevel;

        /// <summary>是否为相遇之线射击</summary>
        public bool isEncounter;

        /// <summary>有序动画步骤 — 表现层按序播放</summary>
        public List<WishRevealStep> steps = new();
    }

    /// <summary>
    /// 单个动画步骤 — 表现层播放器根据 type 执行对应动画
    /// </summary>
    public class WishRevealStep
    {
        public enum StepType
        {
            /// <summary>播放五星过渡视频（必须在一切之前）</summary>
            Star5Video,

            /// <summary>光柱+光带+边缘泛光</summary>
            CardEffects,

            /// <summary>星辉雨</summary>
            StarglitterRain,

            /// <summary>开始抖动+发光（相遇之线专用）</summary>
            EncounterShakeStart,

            /// <summary>升级卡片显示+特效+星辉雨（相遇之线专用）</summary>
            EncounterUpgrade,

            /// <summary>停止抖动+清理发光（相遇之线专用）</summary>
            EncounterShakeEnd,

            /// <summary>停留+飞行到左侧</summary>
            HoldThenFly,
        }

        public StepType type;

        /// <summary>星级（CardEffects / EncounterUpgrade / HoldThenFly 使用）</summary>
        public int starLevel;

        /// <summary>星辉雨数量（StarglitterRain / EncounterUpgrade 使用，0=无雨）</summary>
        public int starglitterAmount;

        /// <summary>新卡片数据（EncounterUpgrade 使用，null=不换卡）</summary>
        public SaveCardData cardData;

        /// <summary>升级前等待时长（EncounterUpgrade 使用，抖动持续时间）</summary>
        public float shakeDelay;

        /// <summary>相遇之线升级总次数（EncounterShakeStart 使用）</summary>
        public int upgradeCount;

        /// <summary>起始星级（EncounterShakeStart 使用）</summary>
        public int baseStarLevel;
    }
}
