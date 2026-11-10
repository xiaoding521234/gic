using System.Collections.Generic;
using UnityEngine;
using GIC.Framework;
using GIC.Data;
namespace GIC.Battle
{
    /// <summary>
    /// Buff 工厂（单一 switch 分发，2026-10-02 执行阶段复审收口：原双 Create 重载合一——
    /// 封闭枚举 switch 即定式，不再挂「扩为注册表」的旧承诺）。
    /// value 通道（B-S1b）：技能参数经 ApplyBuffEffect.BuffValue 单源传入（寒冰之棱的初始层数），
    /// 与技能配置同源、勿在各 Buff 内硬编码默认值以外的取值来源。
    /// 参数型 Buff（StatBuff 族，2026-10-07 返修三 决策五十六「技能资产剥离 buff 配置」）：
    /// 数值单源=StatBuffConfig 资产（每层加成/叠层上限/持续秒数）——零注入（延奏 ApplyBuff
    /// paramKey 已剥离；冰棱减防 2026-10-08 同步剥离）合法走资产默认；注入通道非零仍覆写
    /// （「技能实参＞模板」保留）；注入与资产双零才 Warn+null。
    /// 2026-10-08 决策五十七「资产即身份」：Create 优先按 buffKey（BuffConfig 资产名）查具名资产
    /// 构造——同资产叠层合并、**异资产同族共存**（安柏加攻「百发百中」+班尼特加攻可同时存在，
    /// 用户拍板「不同名即可叠加」）；buffKey 空/未命中=旧路径按 BuffType 查同族首资产（旧回放兼容）。
    /// switch 分发键=资产的 buffType（族），同族具名资产共用构造分支——新增同类 buff 零工厂代码。
    /// </summary>
    public static class BuffFactory
    {
        public static BaseBuff Create(BuffType type, int level, Unit source, int value = 0,
            int stackLimit = 0, int turns = 0, string buffKey = null)
        {
            // 具名身份优先：buffKey 命中资产=按资产的族分发（族与 key 声明不符也以资产为准——资产即真源）；
            // 空/未命中回落族首资产（旧回放/存量效应兼容）
            var named = BuffConfig.ByKey(buffKey);
            if (named != null) type = named.buffType;
            var cfg = named != null ? named : BuffConfig.OfType(type);
            if (cfg == null)
            {
                GICLog.Warn($"[BuffFactory] BuffType.{type} 缺配置资产" +
                            $"（Assets/Resources/Configs/Buffs/Buff_{type}.asset，docs/39）");
                return null;
            }
            switch (type)
            {
                case BuffType.Burn:
                    if (cfg is not BurnBuffConfig burnCfg) return FamilyMismatch(type, cfg);
                    return new BurnBuff(burnCfg, level) { source = source };
                case BuffType.Freeze:
                    if (cfg is not FreezeBuffConfig freezeCfg) return FamilyMismatch(type, cfg);
                    return new FreezeBuff(freezeCfg, level) { source = source };
                case BuffType.SongOfLife:
                    // 歌声之环（B-3 ②）：永久 1 层光环——参数通道不适用（恒 1 层/不计时）
                    if (cfg is not SongOfLifeBuffConfig songCfg) return FamilyMismatch(type, cfg);
                    return new SongOfLifeBuff(songCfg) { source = source };
                case BuffType.Icicle:
                    // 寒冰之棱（凛冽轮舞批）：value 通道=初始层数（ShardCount 经 paramKey 注入——
                    // 工厂按 Buff 类型解释 value 的既有先例同款，AttackUp=每层加成/寒冰之棱=初始层数）
                    // + 施加者命座召唤增补（2026-10-08 拍板 2命/3命各+1——source 赋值后调用：
                    // 首次注册与 Merge 的 newer 两路都已带上增补，叠层按次入账）
                    if (cfg is not IcicleBuffConfig icicleCfg) return FamilyMismatch(type, cfg);
                    var shardBuff = new IcicleBuff(icicleCfg, Mathf.Max(1, value)) { source = source };
                    shardBuff.ApplyConstellationSummonBonus();
                    return shardBuff;
                case BuffType.VisionExposed:
                    // 视野暴露（安柏变奏「全面侦查」，追拍七）：标记型——持续=资产单源
                    //（value/stackLimit 参数通道不适用）；0=配置错误 Warn+null
                    if (cfg is not VisionExposureBuffConfig exposureCfg) return FamilyMismatch(type, cfg);
                    if (exposureCfg.持续秒数 == 0)
                    {
                        GICLog.Warn($"[BuffFactory] 视野暴露 {cfg.name} 持续秒数未配置（0）");
                        return null;
                    }
                    return new VisionExposureBuff(exposureCfg, turns) { source = source };
                case BuffType.AttackUp:
                case BuffType.MoveSpeedUp:
                case BuffType.DefenseDown:
                    // StatBuff 族数值单源=StatBuffConfig 资产（每层加成/叠层上限/持续秒数）——
                    // 延奏与冰棱减防（2026-10-08 C2 注入退役）全零注入走资产默认；注入通道非零
                    // 仍覆写优先（「技能实参＞模板」决策五十四语义保留——将来技能需覆盖时用）；
                    // -1=永久声明（决策五十），注入与资产双零才是配置错误。
                    // 具名共存（决策五十七）：同族异资产经 buffKey 命中各自 cfg——此处构造分支共用
                    if (cfg is not StatBuffConfig statCfg) return FamilyMismatch(type, cfg);
                    int bonusPerStack = value != 0 ? value : statCfg.每层加成;
                    int stackLimitResolved = stackLimit != 0 ? stackLimit : statCfg.叠层上限;
                    int turnsResolved = turns != 0 ? turns : statCfg.持续秒数;
                    if (turnsResolved == 0)
                    {
                        GICLog.Warn($"[BuffFactory] 参数型 Buff {type} 持续秒数未配置" +
                                    "（注入通道与 StatBuffConfig 持续秒数均为 0）");
                        return null;
                    }
                    if (type == BuffType.AttackUp)
                        return new AttackUpBuff(level, bonusPerStack, stackLimitResolved, turnsResolved) { source = source, Config = cfg };
                    if (type == BuffType.MoveSpeedUp)
                        return new MoveSpeedBuff(level, bonusPerStack, stackLimitResolved, turnsResolved) { source = source, Config = cfg };
                    return new DefenseDownBuff(level, bonusPerStack, stackLimitResolved, turnsResolved) { source = source, Config = cfg };
                default:
                    GICLog.Warn($"[BuffFactory] 未实现的 Buff 类型 {type}");
                    return null;
            }
        }

        /// <summary>配置资产家族与 BuffType 不符（如 Burn 类型挂了歌声之环的资产子类）——防御出口</summary>
        private static BaseBuff FamilyMismatch(BuffType type, BuffConfig cfg)
        {
            GICLog.Warn($"[BuffFactory] BuffType.{type} 配置资产家族不符（{cfg.name} 实为 {cfg.GetType().Name}）");
            return null;
        }
    }
}
