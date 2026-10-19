using System;
using System.Collections.Generic;
using UnityEngine;
using GIC.Data;
namespace GIC.Battle
{
    /// <summary>
    /// 伤害效应（同片多来源伤害按 (攻击者,目标) 合并）
    /// </summary>
    public class DamageEffect : BattleEffect
    {
        public string AttackerUnitId;
        public int Amount;
        public int Element;

        /// <summary>投放形态（B4）：0=瞬发直击 / 1=直线飞行投射物（客户端播箭矢）；docs/18 决策二"投放形态由技能数据驱动"</summary>
        public int Delivery;

        /// <summary>投射物发射格（Delivery=1 时有效）</summary>
        public BattleCell FromCell;

        /// <summary>命中点连续格心坐标（Delivery=1 投射物有效；格心坐标系：格 c 的心=c+0.5）。
        /// Host 接触判定得出、命令下发时千分定点化（hitX/hitY）——勿由双端各自推算（docs/22 §11）</summary>
        public float HitPointX;
        public float HitPointY;

        /// <summary>本次命中触发的元素反应子类型（0=无；2026-09-22——Damage 命令带反应标记，
        /// 客户端伤害数字带反应名，如"蒸发 40"）</summary>
        public int ReactionType;

        /// <summary>发射时刻（毫秒，相对片播放起点；时轮 B-S1——投射物前摇/瞬发段时刻，
        /// 客户端据此延迟箭矢起飞与伤害数字节拍；命令合并键含此值=逐发不并）</summary>
        public int LaunchMs;

        /// <summary>命中时刻（秒，相对片播放起点；投射物=ProjectileResolver 接触判定 hitT，瞬发/整线
        /// 迸发=段时刻（与 LaunchMs 同值），tick=0 而节拍随 LaunchMs 错峰）。吸血治疗继承此值作
        /// +N 应用时刻（2026-10-01 WYSIWYG 修复——此前吸血绿字片头瞬弹、早于箭矢落地）；
        /// 命令层不携带（客户端伤害弹出时刻自推导，此字段仅供同片效应派生）</summary>
        public float HitSeconds;

        /// <summary>本次命中是否暴击（2026-10-02 幸运暴击批：roll 于编译出口 CompileElementalDamage，
        /// 理智乘区已并入 Amount——本字段仅供命令层表现〔客户端数字放大〕非结算量）；
        /// 同 (来源,目标,时刻) 合并时取「任一段暴击即暴」（MergeDamageEffects）</summary>
        public bool IsCrit;

        /// <summary>箭矢视觉飞行高度（格=世界单位；0=BattleMetrics.ArrowFlightHeight 默认——纯视觉；
        /// 投射物路径=ProjectileResolver 从发射声明 ProjectileEffect.Height 透传，随 Damage 命令
        /// 千分下发 arrowHeightY，客户端箭矢起飞/落点同高；2026-10-05 对齐动画松弦位）</summary>
        public float ProjectileHeightY;

        public DamageEffect(string attackerUnitId, string targetUnitId, int amount, int element = 0,
            int delivery = 0, BattleCell fromCell = default, float hitPointX = 0f, float hitPointY = 0f,
            int reactionType = 0, int launchMs = 0)
        {
            AttackerUnitId = attackerUnitId;
            TargetUnitId = targetUnitId;
            Amount = amount;
            Element = element;
            Delivery = delivery;
            FromCell = fromCell;
            HitPointX = hitPointX;
            HitPointY = hitPointY;
            ReactionType = reactionType;
            LaunchMs = launchMs;
        }

        /// <summary>装备格挡标记（装备批 2026-10-18：兔兔伯爵——受到 ≥BlockThreshold 的伤害被完全
        /// 抵挡）：ApplyEffects 消费 TryConsumeBlock 置位——Amount 保留原值（命令层尺寸/对账用），
        /// **应用侧跳过 ApplyDamage 与吸血**；命令层 Damage 命令带 blocked=1（客户端弹「格挡」不弹数字
        /// 不掉血）。0=未格挡</summary>
        public int BlockedByItem;
    }
}
