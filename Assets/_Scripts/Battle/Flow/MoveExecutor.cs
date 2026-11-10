using System;
using System.Collections.Generic;
using GIC.Framework;
using GIC.Data;
using Vector2Int = UnityEngine.Vector2Int;
namespace GIC.Battle
{
    /// <summary>
    /// 移动行动执行器：移动距离上限/消耗声明/途经采集参数的数据驱动单源。
    /// 移动是特殊的技能（2026-09-23 B-S1b 拍板）：步数上限=移动技能条目 MoveDistance 参数
    /// （skills[0]·Move 型，数据驱动与 HUD 瞄准同源）；无移动技能/无参数回落 3（旧默认）。
    /// （旧格轨 BuildMover→MovementResolver 同片展开链已随回合制移动执行退役删除——2026-11-09
    /// 残留清理批；移动结算现走 RTSMovementSystem tick 新轨）
    /// </summary>
    public static class MoveExecutor
    {
        /// <summary>
        /// 移动步数上限（Host 权威收口）：移动技能 MoveDistance 按基准换算（2026-09-23 用户拍板
        /// 「拼接后为10%移速」——BasedOnMoveSpeed=百分比×当前移速，SkillData.ResolveMoveDistance；
        /// 2026-11-07 移速基线改 50 后：安柏 60→6 格、其余 50→5 格）；移速读 UnitStats 当前值
        /// （含 Buff——减速缩短移动距离）。
        /// </summary>
        public static int MaxMoveDistance(Unit unit)
        {
            if (unit?.Skills != null)
            {
                foreach (var skill in unit.Skills)
                {
                    if (skill?.RawData?.skillType != SkillType.Move) continue;
                    int moveSpeed = unit.GetUnitComponent<UnitStats>()?.MoveSpeed ?? BattleMetrics.DefaultMoveSpeedCm;
                    return skill.RawData.ResolveMoveDistance(moveSpeed);
                }
            }
            return 3; // 无移动技能条目=旧默认回落
        }

        /// <summary>移动消耗声明（统一消耗模型 C-2，docs/30）：移动技能条目的 costs（C-2 起全移动
        /// 技能资产已配 Stamina 条目）——**无移动技能条目单位走常量兜底**（配额行动 10 体力，规则真源
        /// =BattleMetrics.StaminaCostPerAction；2026-09-28 盘点 21/34 单位无 Move 条目=兜底是主路径非边角）。
        /// 返回共享只读实例（HasAll/ChargeAll 只读消费，调用方勿改）。
        /// 有 Move 条目但未声明消耗=免费移动（costs 空=无消耗语义，数据即事实）</summary>
        public static List<SkillCostEntry> GetMoveCosts(Unit unit)
        {
            if (unit?.Skills != null)
            {
                foreach (var skill in unit.Skills)
                {
                    if (skill?.RawData?.skillType != SkillType.Move) continue;
                    return skill.RawData.HasCosts ? skill.RawData.costs : null;
                }
            }
            return FallbackMoveCosts;
        }

        private static readonly List<SkillCostEntry> FallbackMoveCosts = new()
        {
            new SkillCostEntry { kind = CostKind.Stamina, amount = BattleMetrics.StaminaCostPerAction }
        };

        // ==================== 移动快速采集（即时制逐 tick 真圆判定） ====================

        /// <summary>移动快速采集参数单源：次数/半径/冷却/体力/元能均读移动技能资产；
        /// 1A 仅用自身 CollectCount 覆盖次数。Host 逐 tick 结算与 AI 路径估值共用本对象。</summary>
        public readonly struct CollectParams
        {
            public readonly int count;             // 每点每次触发采集次数（CollectCount）
            public readonly int energyGain;        // 每次采集元能（CollectEnergyGain）
            public readonly int staminaGain;       // 每次采集体力（CollectStaminaGain）
            public readonly int radiusCm;          // 触发半径（CollectRadius，cm）
            public readonly int cooldownSeconds;   // 每单位每资源点独立冷却（CollectCooldown，秒）

            public CollectParams(int count, int energyGain, int staminaGain, int radiusCm, int cooldownSeconds)
            {
                this.count = count;
                this.energyGain = energyGain;
                this.staminaGain = staminaGain;
                this.radiusCm = radiusCm;
                this.cooldownSeconds = cooldownSeconds;
            }
        }

        /// <summary>读移动者移动技能条目的采集参数（与 MaxMoveDistance/GetMoveCosts 同源条目遍历；
        /// 无 Move 条目/无 CollectCount 参数=零采集）</summary>
        public static CollectParams CollectParamsOf(Unit unit)
        {
            if (unit?.Skills != null)
            {
                foreach (var skill in unit.Skills)
                {
                    var data = skill?.RawData;
                    if (data == null || data.skillType != SkillType.Move) continue;
                    int count = data.GetInt(SkillParamKey.CollectCount, 0);
                    if (unit.HasConstellationChoice(SkillName.Amber_C1A_DoubleHarvest))
                    {
                        var option = unit.RawData?.GetConstellationOption(1,
                            (int)SkillName.Amber_C1A_DoubleHarvest);
                        count = option?.skill?.data != null
                            ? option.skill.data.GetInt(SkillParamKey.CollectCount, count) : count;
                    }
                    if (count <= 0) return default;
                    return new CollectParams(count,
                        data.GetInt(SkillParamKey.CollectEnergyGain, 0),
                        data.GetInt(SkillParamKey.CollectStaminaGain, 0),
                        data.GetInt(SkillParamKey.CollectRadius, ResourceNodeMetrics.HarvestRadiusCm),
                        data.GetInt(SkillParamKey.CollectCooldown,
                            ResourceNodeMetrics.DefaultQuickHarvestCooldownSeconds));
                }
            }
            return default;
        }

        /// <summary>AI 路径估值与 Host 半径参数同源：任一连续路径线段进入资源点真圆即计一次快速采集。</summary>
        public static bool PathPassesResourceNode(Vector2Int fromCm, List<Vector2IntCm> waypoints,
            BattleCell nodeCell, int radiusCm)
        {
            if (waypoints == null || radiusCm < 0) return false;
            long radiusSq = (long)radiusCm * radiusCm;
            int ax = fromCm.x, az = fromCm.y;
            foreach (var waypoint in waypoints)
            {
                if (SegmentWithinRadius(ax, az, waypoint.x, waypoint.z,
                        nodeCell.CenterXCm, nodeCell.CenterYCm, radiusSq)) return true;
                ax = waypoint.x;
                az = waypoint.z;
            }
            return false;
        }

        private static bool SegmentWithinRadius(int ax, int az, int bx, int bz,
            int px, int pz, long radiusSq)
        {
            long abx = bx - ax, abz = bz - az;
            long apx = px - ax, apz = pz - az;
            long abSq = abx * abx + abz * abz;
            long dot = apx * abx + apz * abz;
            if (dot <= 0) return apx * apx + apz * apz <= radiusSq;
            if (dot >= abSq)
            {
                long bpx = px - bx, bpz = pz - bz;
                return bpx * bpx + bpz * bpz <= radiusSq;
            }
            long cross = abx * apz - abz * apx;
            return cross * cross <= radiusSq * abSq;
        }
    }
}
