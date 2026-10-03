using System.Collections.Generic;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// 眷属决策器（B6b 起，docs/04 §4.1 五阶段第一阶段；D 批次 2026-09-29 术语迁移改名：
    /// 低级单位→眷属、LowUnitBrain→FamiliarBrain，docs/active/32 §6）：1~2 星眷属单位无论归属
    /// 都自主决定本回合行动（表现类似 MOBA 小兵），决策先于玩家选择完成、不感知玩家本回合选择；
    /// 本阶段只决策不结算。
    /// v2（2026-09-25 方向纪律收口）：战技预判/移动方向一律十字四向（docs/18 决策八「十字方向其一」
    /// ——八向上交在技能侧被归一主轴斜向必空放，docs/11 方向纪律①④）；预判走技能实例
    /// WouldHitEnemyInDirection（与 HUD 瞄准推荐/结算形态同源）+ 存活目标校验（纯尸体线不浪费行动）。
    /// v3（2026-09-26 报障「不会绕路卡湖边」）：移动档改 BFS 最短路首步（FindApproachFirstStep）——
    /// 直行被湖/虚空挡时拐弯绕行，每回合重算走一步沿最短路逼近；小兵蠕动 1 步/回合语义不变。
    /// v4（2026-09-26 拍板「当自己的任何攻击都无法打到时，换目标巡逻」）：攻击档打不了时按距离
    /// 升序逐敌试逼近步（FindEnemiesByDistance）——贴身已到/不可达的目标自动跳过换下一个，
    /// 不再对着打不了的目标站桩；全部敌都无逼近步才缺席。
    /// v5（2026-10-03 拍板「扩眷属脑会放爆发」）：新增爆发档——满槽即放、优先于战技（元能门槛经
    /// ResourceGate 元能照查；方向攻击型爆发与战技走同一十字预判口 WouldHitEnemyInDirection；
    /// 单位指向/无目标自施放型无方向域不消费——眷属现无此类，出现时再扩）。
    /// v6（2026-10-03 报障「移速 20 根本没发挥出来」）：移动步数退役「小兵蠕动 1 步/回合」硬编码
    /// ——改读 MoveExecutor.MaxMoveDistance（MoveDistance×移速单源换算=10%×移速，含 Buff 减速，
    /// 与玩家移动/HUD 同口径）；遇阻由 MovementResolver 结算截停。
    /// v6.1（2026-10-03 同日报障「来回左右移动持续多回合」）：步进改走 BFS 最短路「直线前缀」
    /// （FindApproachStraightSteps——只踏距离严格递减格、踏上攻击位即停）；「首步方向×N 直线飞」
    /// 越过拐点/攻击位=振荡根源，每回合距离单调递减后数学上不可能振荡。
    /// v7（2026-10-03 拍板「眷属总是向协议核心进攻，除非攻击视野内有其它敌人…同距离时优先协议
    /// 核心，不在攻击视野则不关心——就像皇室战争的单位一样」）：移动档目标序改**核心锚定**——
    /// 核心无视野门槛恒为目标；非核心敌须「攻击视野内（Unspecified 回落 5）且严格近于核心」才追
    /// （UnitData.攻击视野 字段）；核心已破=退化为视野内纯距离序。攻击档（战技/爆发）射程即感知
    /// 边界，不另设视野门。
    /// 纯读 BattleSimState 由 Host 在选择阶段头生成（docs/18 决策一），行动与玩家行动合并进执行阶段。
    /// 决策档：爆发可命中（满槽）→ 爆发朝敌 → 战技可命中→打 → 否则按「核心锚定视野序」逐敌
    /// 沿最短路按移速步进 → 全部敌打不了也走不近=缺席。
    /// 眷属不配行为档案（启发式维持=层级特色，也是数量安全阀——场上眷属多，评分制算量敏感，
    /// docs/active/32 §6.1）。
    /// </summary>
    public static class FamiliarBrain
    {
        /// <summary>
        /// 全部眷属单位的本回合行动（存活的；不可动者缺席=无行动——Pass 无结算意义不出命令）
        /// </summary>
        public static List<ActionData> DecideAll(BattleSimState sim, int turnNumber)
        {
            // 决策快照（纯读，与选择阶段广播同源状态）——技能预判的唯一状态源
            var snapshot = sim.TakeSnapshot(turnNumber);

            var actions = new List<ActionData>();
            foreach (var kv in sim.Units)
            {
                var unit = kv.Value;
                if (BattleHeuristics.IsBuilding(unit)) continue; // 建筑不参与任何行动（含未来低星建筑）
                if (!BattleHeuristics.IsFamiliar(unit)) continue;
                if (BattleSimState.IsDead(unit) || !BattleSimState.CanAct(unit)) continue;

                var action = DecideOne(sim, snapshot, kv.Key, unit, turnNumber);
                if (action != null)
                    actions.Add(action);
            }
            return actions;
        }

        /// <summary>
        /// 单个眷属单位决策：①爆发可命中且消耗门槛过（满槽即放，v5 2026-10-03 拍板）→ 爆发朝敌；
        /// ②战技可命中（十字向预判有存活敌）→ 战技朝敌；
        /// ③否则按距离升序逐敌试 BFS 最短路首步、按移速步进（v3 绕行+v4 换目标巡逻+v6 移速步数
        /// 单源——某敌贴身已到/不可达即换下一个目标，被挡由 MovementResolver 结算截回——被挡也算
        /// 已使用，眷属无体力配额=层级表 0 档）；
        /// ④无敌人/全场敌都无逼近步 → 缺席。
        /// E-1 对手建模复用（docs/active/33 §2.1）：public 即预测器入口——启发式纯函数（同快照
        /// 恒同输出），配额脑对敌方眷属的预测=本方法原样直跑（「行为可被玩家轻易预测」决策三十一
        /// 特性的 AI 侧对偶——AI 玩家同样按可预测模型推演敌方眷属）
        /// </summary>
        public static ActionData DecideOne(BattleSimState sim, BattleSnapshot snapshot,
            string unitId, Unit unit, int turnNumber)
        {
            var identity = unit.GetUnitComponent<UnitIdentity>();
            if (identity == null) return null;

            // 爆发档（v5，2026-10-03 拍板「扩眷属脑会放爆发」）：满槽即放、优先于战技——单次伤害
            // 更高+倾倒元能（攒满即放勿囤积，同凛冽轮舞 AI 口径）；方向攻击型爆发与战技走同一
            // 十字预判口（单位指向/无目标自施放型无方向域不消费——眷属现无此类）
            var burstAction = TryCastAttack(sim, snapshot, unit, identity, unitId, turnNumber, SkillType.Burst);
            if (burstAction != null) return burstAction;

            // 战技（无该型技能=自动跳过此档；占位技能不可施放同理）
            var skillAction = TryCastAttack(sim, snapshot, unit, identity, unitId, turnNumber, SkillType.Normal);
            if (skillAction != null) return skillAction;

            // 朝敌按移速步进——v6（2026-10-03 报障「移速 20 根本没发挥出来」）：步数上限退役
            // 「小兵蠕动 1 步/回合」硬编码，改读 MoveExecutor.MaxMoveDistance（移动技能 MoveDistance×
            // 移速单源换算=10%×移速，含 Buff 减速——与玩家移动/HUD 同口径）。
            // v6.1（2026-10-03 同日报障「来回左右移动持续多回合」）：步进改走 BFS 最短路「直线前缀」
            // （FindApproachStraightSteps——只踏到攻击位距离严格递减的最短路格、踏上攻击位即停）；
            // 旧写法「首步方向×N 直线飞」会越过路径拐点与攻击位，下回合 BFS 指回程=左右振荡
            // 永不收敛（L 形路径+对角目标场景），每回合距离单调递减后数学上不可能振荡。v4 换目标
            // 巡逻不变（攻击档打不了时按距离升序逐敌试逼近步）；遇阻/拐点由原语就地截停（被挡也算
            // 已使用）；移速被压到 0 步（重减速）=缺席不空耗。
            int moveSteps = MoveExecutor.MaxMoveDistance(unit);
            if (moveSteps > 0)
            {
                // v7（2026-10-03 拍板「眷属总是向协议核心进攻，除非攻击视野内有其它敌人，才会去追
                // （需要比协议核心更近才行，同距离时优先协议核心），不在攻击视野则不关心——就像
                // 皇室战争的单位一样」）：目标序从纯距离升序改**核心锚定**——
                // ①敌方协议核心（建筑）=永恒目标：无视野门槛；
                // ②非核心敌须同时满足：在攻击视野内（RawData.GetEffectiveAttackVision 切比雪夫半径，
                //   Unspecified 回落 5）**且**严格近于核心（同距核心优先）才可追；视野外不关心。
                // 核心已破（不在场）=无锚，退化为「视野内最近敌」纯距离序兜底。逐敌距离升序+等距
                // unitId 升序铁律不变（确定性）；v6.1 直线前缀步进不变。
                var enemies = BattleHeuristics.FindEnemiesByDistance(sim, unit);
                Unit core = null;
                foreach (var e in enemies)
                    if (BattleHeuristics.IsBuilding(e)) { core = e; break; } // 现役唯一建筑=双方协议核心
                var selfPos = sim.GetPosition(unit);
                int coreDist = core != null ? selfPos.ChebyshevTo(sim.GetPosition(core)) : int.MaxValue;
                int attackVision = unit.RawData != null ? unit.RawData.GetEffectiveAttackVision() : 5;

                foreach (var target in enemies)
                {
                    if (target != core)
                    {
                        int targetDist = selfPos.ChebyshevTo(sim.GetPosition(target));
                        if (targetDist > attackVision) continue; // 攻击视野外：不关心（CR 式感知）
                        if (targetDist >= coreDist) continue;   // 不严格近于核心：不追（同距核心优先）
                    }

                    var steps = BattleHeuristics.FindApproachStraightSteps(sim, unit, sim.GetPosition(target), moveSteps, out var moveDirection);
                    if (steps <= 0) continue; // 该敌打不了也走不近：换下一个目标

                    return new ActionData
                    {
                        playerId = identity.OwnerPlayerID,
                        unitId = unitId,
                        actionType = ActionType.Move,
                        direction = moveDirection,
                        moveMagnitude = steps,
                        turnNumber = turnNumber,
                    };
                }
            }
            return null; // 全场敌都无可逼近步（或移速被压到 0）：缺席
        }

        /// <summary>
        /// 攻击技能施放尝试（战技/爆发共用，v5 爆发档抽提）：类型槽定位（FindSkillIndex——与
        /// SkillExecutor.GetSkill 同源索引）+ CanCast（占位技能不可施放）+ 消耗门槛（统一消耗模型
        /// C-2：costs 单源镜像 ResourceGate.HasAll——眷属豁免玩家资源、元能照查，与 Host 同口径）
        /// + 十字向首可命中方向（FindAttackDirection：WouldHitEnemyInDirection+存活目标校验）
        /// </summary>
        private static ActionData TryCastAttack(BattleSimState sim, BattleSnapshot snapshot,
            Unit unit, UnitIdentity identity, string unitId, int turnNumber, SkillType skillType)
        {
            int skillIndex = BattleHeuristics.FindSkillIndex(unit, skillType);
            if (skillIndex < 0) return null;
            var skill = unit.Skills[skillIndex];
            if (skill == null || !skill.CanCast(unit)) return null;
            if (!ResourceGate.HasAll(sim, unit, identity.OwnerPlayerID, skill.RawData?.costs, out _)) return null;
            var direction = BattleHeuristics.FindAttackDirection(sim, snapshot, unit, skill);
            if (direction == 0) return null;
            return new ActionData
            {
                playerId = identity.OwnerPlayerID,
                unitId = unitId,
                actionType = ActionType.Skill,
                skillIndex = skillIndex,
                direction = direction,
                turnNumber = turnNumber,
            };
        }
    }
}
