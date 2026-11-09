using System;
using System.Collections.Generic;
using UnityEngine;
using GIC.Data;
namespace GIC.Battle
{
    /// <summary>
    /// 元素附着效应（B4）：反应消耗语义已由 ElementReactionResolver 在结算时定夺，
    /// 本效应=效应应用阶段直接 Dye 目标为 incoming 元素（覆盖旧附着=消耗）。
    /// Layers=附着层数（docs/06 §6.2 铺路，决策七十四：默认 1；编译期多层消耗后的
    /// 「多余层仍附着」落此——如 2 层火 vs 1 层冰融化后剩 1 层火）
    /// </summary>
    public class AttachElementEffect : BattleEffect
    {
        public string SourceUnitId;
        public int Element;

        /// <summary>附着层数（默认 1——docs/06 §6.2；0=编译层已保证不产本效应）</summary>
        public int Layers = 1;

        /// <summary>命中时刻（秒，相对片播放起点；0=立即）——WYSIWYG（决策七十六复测返修：
        /// 箭矢飞行中目标不提前挂附着图标；OnCast 施放/效果拍末泡湿类=0 立即）</summary>
        public float HitSeconds;

        public AttachElementEffect(string sourceUnitId, string targetUnitId, int element, int layers = 1)
        {
            SourceUnitId = sourceUnitId;
            TargetUnitId = targetUnitId;
            Element = element;
            Layers = layers;
        }
    }
}
