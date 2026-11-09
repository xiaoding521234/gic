using System.Collections.Generic;
using UnityEngine;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// AI 玩家注册表（AI-4 批，docs/active/42 §9.2/§11）：per AI 玩家的难度档存取——
    /// 配额脑（Bind 时登记）与伙伴脑（DecideAll 按单位归属查询）的消费单源。
    /// 真人玩家不注册 → 门控恒全开（其自主军团=全件质量，难度只属 AI 玩家）。
    /// 生命周期：static+按玩家 ID 键控（Bind 每局重写=跨局自愈；未参战玩家残留条目无人查询=无害）。
    /// AIDifficulty 枚举=GIC.Data（房间名册/PlayerInfo 序列化层消费——SpawnPositionType 同款先例，
    /// 2026-10-12 选择器批上移）。
    /// </summary>
    public static class AIPlayerConfig
    {
        /// <summary>决策质量门（伙伴脑骨架按单位归属消费；全 true=全件——真人/默认）</summary>
        public struct BrainGates
        {
            /// <summary>威胁图喂伙伴脑（走位避险+反威胁——§9.2 棋圣档开启件；劲敌的威胁图仅配额脑消费）</summary>
            public bool ThreatFeed;
            /// <summary>E-3 反应预期分（增伤反应预判——§9.2 学徒关闭件）</summary>
            public bool ReactionPreview;
            /// <summary>E-2 意图板去重/集火（伤害溢出/治疗/集火跟随——§9.2 学徒关闭件）</summary>
            public bool IntentBoard;
        }

        private static readonly Dictionary<string, AIDifficulty> _difficultyByPlayer = new();

        /// <summary>登记/改设 AI 玩家难度（配额脑 Bind 默认登记；房间 UI/观察 harness 可随时改——下拍生效）</summary>
        public static void SetDifficulty(string playerId, AIDifficulty difficulty)
        {
            if (string.IsNullOrEmpty(playerId)) return;
            _difficultyByPlayer[playerId] = difficulty;
        }

        /// <summary>玩家难度查询（未注册=真人/默认 → 棋圣全件）</summary>
        public static AIDifficulty DifficultyOf(string playerId)
            => !string.IsNullOrEmpty(playerId) && _difficultyByPlayer.TryGetValue(playerId, out var d)
                ? d
                : AIDifficulty.Grandmaster;

        /// <summary>伙伴脑决策质量门（按单位归属；未注册=真人 → 全开）</summary>
        public static BrainGates GatesFor(string playerId)
        {
            switch (DifficultyOf(playerId))
            {
                case AIDifficulty.Apprentice:
                    // 学徒：关对手预测器/反应预期分/意图板去重（失误注入在配额脑侧执行）
                    return new BrainGates { ThreatFeed = false, ReactionPreview = false, IntentBoard = false };
                case AIDifficulty.Rival:
                    // 劲敌：威胁图+对手预测=配额脑消费；伙伴脑走位避险属棋圣档
                    return new BrainGates { ThreatFeed = false, ReactionPreview = true, IntentBoard = true };
                default:
                    // 棋圣/天理：全件（天理增强件=二阶先验/信息探针等随 AI-5/AI-6 实装接线）
                    return new BrainGates { ThreatFeed = true, ReactionPreview = true, IntentBoard = true };
            }
        }

        /// <summary>配额脑威胁图门（学徒=对手预测器关闭件；劲敌及以上开）</summary>
        public static bool QuotaThreatEnabled(AIDifficulty difficulty)
            => difficulty != AIDifficulty.Apprentice;

        /// <summary>失误注入轮转法门（§9.2 学徒专属——评分榜取第 (turn mod N)+1 名=确定性次优）</summary>
        public static bool MistakeInjectionEnabled(AIDifficulty difficulty)
            => difficulty == AIDifficulty.Apprentice;

        /// <summary>劲敌次优注入门（凌晨批议题⑥）：中档难度=「分差内取次名」——GameAIPro ch04
        /// hustling 三招之一「最优与次优分差&lt;20% 才选次优」的确定性实现（零 roll；分差大仍取
        /// 最优=只在接近时犯错的拟人形态；学徒轮转法独立不变）</summary>
        public static bool SecondBestInjectionEnabled(AIDifficulty difficulty)
            => difficulty == AIDifficulty.Rival;

        /// <summary>摩拉收入百分比（凌晨批议题⑥：天理=经济作弊型难度——SC2 proto CheatMoney/
        /// petra 经济倍率表先例，决策逻辑不动、收入乘倍率；其余档恒 100）。消费点=BattleResolver
        /// 秒拍末发放+建筑摩拉收入（玩家资源发放两单源，掠夺/消耗不复用）；数值真源=AITuning.
        /// 天理摩拉收入百分比。**LAN 边界**：难度注册在配额脑 Bind（本地进程）——远端 AI 玩家
        /// 的天理档对 Host 结算不可见（回落 100%），天理作弊单机/Host 本地 AI 生效</summary>
        public static int MoraIncomePercentOf(string playerId)
            => DifficultyOf(playerId) == AIDifficulty.Providence
                ? AITuningConfig.Instance.天理摩拉收入百分比
                : 100;

        /// <summary>战略层门（§9.2 学徒开启件「战略层仅发育态」——AIStrategist 态势机不走、
        /// 恒发育乘子；三账本红线/体力保底=硬保底语义非决策质量件、全档生效）</summary>
        public static bool StrategistDevelopOnly(AIDifficulty difficulty)
            => difficulty == AIDifficulty.Apprentice;
    }
}
