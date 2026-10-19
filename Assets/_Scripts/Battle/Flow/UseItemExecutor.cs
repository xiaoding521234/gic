using System;
using System.Collections.Generic;
using GIC.Framework;
using GIC.Data;
namespace GIC.Battle
{
    /// <summary>
    /// 使用物品执行器（2026-10-16 首个可打出物品批，docs/05 §5.0——使用物品不消耗体力、占玩家
    /// 行动配额）：校验链=物品配置存在且配了效果（可打出判定单源=ItemData.HasEffects）→ 手牌持有
    ///（Host 权威——客户端可凭空上交，同 DeployUnit 手牌校验先例）→ 饮用目标为我方存活单位
    ///（targetUnitId；TeamType 口径同延奏——2v2 含队友单位；尸体不可饮用）→ 消耗 1 张
    ///（ItemConsumeEffect 指定物品模式——应用层手牌确定性扣减+ItemConsume 命令客户端镜像）→
    /// EffectCompiler.CompileItemUse 单出口编译物品效果（目标=饮用者；施法者=饮用者——治疗
    /// 基准/效率乘区按饮用者口径）。玩家级行动（unitId 留空——TurnResolver 先行摘出独立段结算，
    /// 不进攻速分桶）。首个消费方=蒲公英酒（SanityChange -10〔酒鬼 tag 条件〕+ Heal 40）。
    /// </summary>
    public static class UseItemExecutor
    {
        /// <summary>校验+消耗+效果编译。成功=true 且 effects 已并入（ItemConsume + 物品效果原子）；
        /// 失败=零效果零消耗（行动已使用，占用配额不退——与「门槛不足=落空不扣」并行口径的
        /// 差异：物品门槛在 Host 校验兜底，常规路径 HUD 只给可打出物品入口+目标域预判）。</summary>
        public static bool TryResolve(BattleSimState sim, ActionData action, BattleSnapshot sliceSnapshot,
            List<BattleEffect> effects)
        {
            var itemName = (ItemName)action.useItemId;
            var config = Wargame.Instance?.Context?.Get<ItemConfig>();
            var data = config?.GetItemData(itemName);
            if (data == null || !data.HasEffects)
            {
                GICLog.Warn($"[UseItem] {action.playerId} 上交 {itemName} 无配置或无使用效果（不可打出），使用落空");
                return false;
            }

            // 手牌持有校验（Host 权威，同 DeployUnit 先例——B7 LAN 防凭空上交）。
            // 2026-10-16 报障修复「打出后数量没扣减」：匹配口径与 LoseCard/FindHandEntry 完全同源
            //（cardType==Item + value）——旧校验只比 value 不比 cardType，手牌条目形态异常
            //（cardType 既非 Item 也非 Unit）时「校验通过而扣减失败」分裂=效果生效+数量不扣的
            // 半生效态（日志实锤：两次「未足额回滚零命令」）；同口径后校验直接拒绝+手牌 dump 定位
            var hand = sim.GetHand(action.playerId);
            bool inHand = false;
            if (hand != null)
            {
                foreach (var card in hand)
                {
                    if ((CardType)card.cardType != CardType.Item) continue;
                    if (card.value == (int)itemName && card.count >= 1)
                    {
                        inHand = true;
                        break;
                    }
                }
            }
            if (!inHand)
            {
                GICLog.Warn($"[UseItem] {action.playerId} 手牌无 {itemName}，使用拒绝（手牌形态：{DumpHand(sim, action.playerId)}）");
                return false;
            }

            // 饮用目标校验：我方存活单位（TeamType 口径同延奏；尸体不可饮用——治疗对尸体恒 0 无意义）
            var drinkerState = SkillHitResolver.FindUnitState(sliceSnapshot, action.targetUnitId);
            var casterTeam = sim.GetTeamOf(action.playerId);
            if (drinkerState == null || (TeamType)drinkerState.team != casterTeam || drinkerState.isCorpse != 0)
            {
                GICLog.Warn($"[UseItem] {action.playerId} 使用 {itemName} 目标无效（{action.targetUnitId}——须为我方存活单位），使用落空");
                return false;
            }
            var drinker = sim.GetUnit(action.targetUnitId);
            if (drinker == null)
            {
                GICLog.Warn($"[UseItem] {action.playerId} 使用 {itemName} 目标单位缺失（{action.targetUnitId}），使用落空");
                return false;
            }

            // 建筑不可饮用（2026-10-16 用户拍板「不可为建筑单位」）：给建筑喂物品无意义（治疗对
            // 协议核心吃治疗效率但物品定位是单位饮用）；客户端域已排除，Host 权威同口径兜底
            if (drinker.RawData != null && drinker.RawData.unitType == UnitType.Building)
            {
                GICLog.Warn($"[UseItem] {action.playerId} 使用 {itemName} 目标为建筑（{action.targetUnitId}），使用落空");
                return false;
            }

            // 消耗 1 张（指定物品模式 ItemConsumeEffect——Consumed 明细→ItemConsume 命令客户端镜像扣）
            effects.Add(new ItemConsumeEffect(action.playerId, itemName, 1));

            // 物品效果编译（单出口：目标=饮用者）
            EffectCompiler.CompileItemUse(sim, action.playerId, drinker, sliceSnapshot, data, effects);

            GICLog.Info($"[UseItem] {action.playerId} 使用 {itemName} → 饮用者 {action.targetUnitId}");
            return true;
        }

        /// <summary>手牌条目形态 dump（诊断日志用：cardType:value:count 逐条——扣减失败定位）</summary>
        private static string DumpHand(BattleSimState sim, string playerId)
        {
            var hand = sim.GetHand(playerId);
            if (hand == null) return "手牌未注册";
            var sb = new System.Text.StringBuilder();
            foreach (var card in hand)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append($"[{card.cardType}:{card.value}x{card.count}]");
            }
            return sb.Length == 0 ? "空手牌" : sb.ToString();
        }
    }
}
