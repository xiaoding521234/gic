using System;
using System.Collections.Generic;
using UnityEngine;
using GIC.Data;
namespace GIC.Battle
{
    /// <summary>
    /// 复苏效应（B-3 ②，芭芭拉闪耀奇迹——docs/05 §5.4「血量永远 0 不复苏」的唯一例外通道）：
    /// 应用=清除目标尸体态+治疗（一次性=isCorpse→存活 与 HP+HealAmount 单效应原子化，
    /// 客户端单命令同步解灰+弹 +N，无中间态）。HealAmount=编译期 ResolveHealAmount 换算后的
    /// 终值（BasedOnMaxHealth=施法者最大生命——2026-09-30 拍板翻转，受疗者治疗效率单源）；
    /// 目标非尸体=应用层防御性 no-op（编译层 condition=TargetIsCorpse 已保证，零命令）。
    /// </summary>
    public class ReviveEffect : BattleEffect
    {
        public string SourceUnitId;

        /// <summary>复活血量（治疗量终值——编译期换算，应用层直加）</summary>
        public int HealAmount;

        /// <summary>应用回填（目标为尸体才应用；false=活体防御性 no-op——命令发射/对账豁免零命令）</summary>
        public bool Applied;

        /// <summary>灵魂归体消散回填（决策九十一，2026-10-25）：应用层移除尸体所链灵魂时写入其
        /// unitId（无链接=null 零动作）；命令层随 Revive 命令补发 Death(dissolve)=灵魂归体淡出
        /// ——「同一实体的两半」系统级联动，非维度间影响（尸体在主世界被复苏，灵魂无需跨界）</summary>
        public string RemovedSoulId;

        public ReviveEffect(string sourceUnitId, string targetUnitId, int healAmount)
        {
            SourceUnitId = sourceUnitId;
            TargetUnitId = targetUnitId;
            HealAmount = healAmount;
        }
    }
}
