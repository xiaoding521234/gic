using System;
using System.Collections.Generic;
using UnityEngine;
using GIC.Data;
namespace GIC.Battle
{
    /// <summary>
    /// 回合末光环命中待结算事件（决策二十四终版「同时进行」=逐拍交错，2026-10-01）：buff 的
    /// OnTurnEnd 只**声明**命中（谁/打谁/元素/伤害请求/第几拍），TurnResolver 收齐全部 buff 的事件后
    /// **按 (拍时刻, 施法者 unitId, 目标 unitId) 排序**、共享编译视图逐个结算（反应预判+1:1 消耗+
    /// **每层附着**=每层命中都是一次元素应用）——双异元素光环同拍交错、每拍可触发反应（对齐原神：
    /// 双光环同时活跃、反应在应用事件序列里自然发生）。Host 进程内引用不序列化；结算后被替换为
    /// Damage/Reaction/Attach 全套产物，不进效应应用阶段。
    /// </summary>
    public class PendingAuraHit : BattleEffect
    {
        public string AttackerUnitId;

        /// <summary>施法者（Host 进程内引用，不序列化）</summary>
        public Unit Attacker;

        /// <summary>目标（Host 进程内引用，不序列化）</summary>
        public Unit Target;

        /// <summary>来袭元素</summary>
        public ElementType Element;

        /// <summary>伤害请求（null=纯附着事件——环的附着我方分支）</summary>
        public DamageRequest Request;

        /// <summary>拍时刻（秒=layer×BuffLayerStaggerSeconds；排序主键）</summary>
        public float BeatSeconds;

        /// <summary>该层命中后附着来袭元素（默认 true；纯附着事件也走此位）</summary>
        public bool Attach = true;

        /// <summary>来袭元素层数（决策七十四 docs/06 §6.2：默认 1=常规附着反应；0=纯伤害不反应
        /// 不附着——燃烧 DoT 回归 0 层终点语义〔修订决策二十三〕，反应级别= min(双方层数)）</summary>
        public int Layers = 1;

        public PendingAuraHit(string attackerUnitId, string targetUnitId, Unit attacker, Unit target,
            ElementType element, DamageRequest request, float beatSeconds, bool attach = true, int layers = 1)
        {
            AttackerUnitId = attackerUnitId;
            TargetUnitId = targetUnitId;
            Attacker = attacker;
            Target = target;
            Element = element;
            Request = request;
            BeatSeconds = beatSeconds;
            Attach = attach;
            Layers = layers;
        }
    }
}
