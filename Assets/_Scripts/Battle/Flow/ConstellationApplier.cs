using System.Collections.Generic;
using GIC.Data;
using GIC.Framework;
namespace GIC.Battle
{


    /// <summary>
    /// 命座被动应用器（B8 批 2026-09-30，docs/09：0命=固有被动、1-3命=重复出战升命的能力增强）。
    /// 命座=技能（Talent 型被动技能——#6 槽位技能资产），效果原子 trigger=OnDeploy 由本类消费，
    /// 不经 EffectCompiler 施放链（被动无可施放语义）。**幂等重算**：登场（UnitFactory 创建）与
    /// 升命（DeployUnitExecutor 同名重复出战）两触点都全量调 ApplyPassives——先全撤（修改器登记表
    /// RemoveModifier+溢出旗标清零+Energy 上限回配置基线）再按当前命座层重挂（层数单调只增，
    /// 重算=全撤后重挂天然幂等）。
    /// 消费方说明：StatBoost=UnitStats 修改器（标量属性）/SetStatRange 直扩上限（容量型属性 Energy
    /// ——快照与 ApplyEnergy 读原始结构体上限，修改器 MaxFlat 不可见故直扩）；EnergyOverflowTransfer
    /// =BattleSimState.ApplyEnergy 溢出转移被动旗标。
    /// </summary>
    public static class ConstellationApplier
    {
        /// <summary>命座上限（docs/09：共可提升 3 次）</summary>
        public const int MaxConstellation = 3;

        /// <summary>百分比面板属性集（Percent 基准=**+X 个百分点**〔BaseFlat〕——GI 命座口径：
        /// 「治疗加成/吸血提升X%」=绝对百分点加值，0 基值×相对提升恒 0 的坑见 docs/14 §105；
        /// 表外点数属性〔移速/攻速〕的 Percent=相对提升 ×(1+X%)。未来暴击/暴击伤害/充能效率类
        /// 百分比面板属性落地时入表，2026-10-01 拍板「应当全部统一」）</summary>
        private static readonly HashSet<StatType> PercentPanelStats = new()
        {
            StatType.HealEfficiency,
            StatType.LifeSteal,
        };

        /// <summary>
        /// 按当前命座层重算全部 OnDeploy 被动（幂等：全撤→重挂）。调用触点=单位创建尾（UnitFactory）
        /// 与升命后（DeployUnitExecutor）。**只处理属性/旗标段**（StatBoost/OverflowTransfer）；
        /// OnDeploy ApplyBuff（登场自动获得 Buff——芭芭拉 0命「获得歌声之环」）由 ApplyDeployBuffs
        /// 消费（需 sim 注册表与战场门面，RegisterUnit 尾部调用）。
        /// </summary>
        public static void ApplyPassives(Unit unit)
        {
            if (unit == null) return;
            var stats = unit.GetUnitComponent<UnitStats>();
            if (stats == null) return;

            // 全撤：命座修改器登记表 + 溢出旗标 + Energy 上限回配置基线（容量加成重算的回退基准）
            foreach (var mod in unit.ConstellationModifiers)
                stats.RemoveModifier(mod);
            unit.ConstellationModifiers.Clear();
            unit.EnergyOverflowPassive = false;
            if (unit.RawData != null)
                stats.SetStatRange(StatType.Energy, max: unit.RawData.GetEffectiveEnergy());

            int level = unit.ConstellationLevel;
            foreach (var skill in unit.Skills)
            {
                var data = skill?.RawData;
                if (data == null || data.skillType != SkillType.Talent) continue; // 命座=Talent 槽技能
                if (data.effects == null) continue; // 参数载体型命座（芭芭拉：参数由歌声之环 Buff 消费）合法
                foreach (var atom in data.effects)
                {
                    if (atom == null || atom.trigger != SkillEffectTrigger.OnDeploy) continue;
                    if (level < atom.minConstellation) continue; // 命座层未到，不生效
                    ApplyAtom(unit, stats, data, atom);
                }
            }
        }

        /// <summary>
        /// 登场 Buff 授予段（0命固有被动的 Buff 形态——芭芭拉「获得歌声之环」：**RegisterUnit 尾部调用**，
        /// 需 sim 注册表与门面注入；升命不重跑——环随持有者倒下消失后复苏不自动回环，重挂走技能施放）。
        /// 消费 OnDeploy+ApplyBuff+minConstellation≤当前层：施加者=自身（环数值随自身命座/属性成长）。
        /// 随后的 BuildUnitState（开局装配/Summon 命令同构）携带 buffs——客户端建 view 即见徽章，零额外命令。
        /// </summary>
        public static void ApplyDeployBuffs(BattleSimState sim, Unit unit)
        {
            if (sim == null || unit == null) return;
            int level = unit.ConstellationLevel;
            foreach (var skill in unit.Skills)
            {
                var data = skill?.RawData;
                if (data == null || data.skillType != SkillType.Talent) continue;
                if (data.effects == null) continue;
                foreach (var atom in data.effects)
                {
                    if (atom == null || atom.trigger != SkillEffectTrigger.OnDeploy) continue;
                    if (atom.kind != SkillEffectKind.ApplyBuff) continue;
                    if (level < atom.minConstellation) continue;
                    // 同类已存在不重挂（幂等防御口径）
                    bool exists = false;
                    foreach (var b in unit.Buffs)
                        if (b != null && b.Type == atom.buffType) { exists = true; break; }
                    if (exists) continue;
                    var buff = atom.paramKey3 != SkillParamKey.None
                        ? BuffFactory.Create(atom.buffType, 1, unit,
                            data.GetInt(atom.paramKey), data.GetInt(atom.paramKey2), data.GetInt(atom.paramKey3))
                        : BuffFactory.Create(atom.buffType, 1, unit, data.GetInt(atom.paramKey));
                    if (buff == null) continue;
                    sim.ApplyBuff(unit, buff, unit);
                }
            }
        }

        private static void ApplyAtom(Unit unit, UnitStats stats, SkillConfig.SkillData data, SkillEffectConfig atom)
        {
            switch (atom.kind)
            {
                case SkillEffectKind.StatBoost:
                {
                    int value = atom.paramKey != SkillParamKey.None ? data.GetInt(atom.paramKey) : atom.value;
                    if (value == 0) break;

                    if (atom.statType == StatType.Energy)
                    {
                        // 容量型属性：直扩上限（快照 maxEnergy/ApplyEnergy 钳位读原始结构体 Max——
                        // 修改器 MaxFlat 对这两处不可见，故 SetStatRange 直扩；Percent 基准不支持容量型）
                        var param = FindParam(data, atom.paramKey);
                        if (param != null && param.baseType == SkillBaseType.Percent)
                        {
                            GICLog.Warn($"[Constellation] StatBoost 容量型属性（{atom.statType}）不支持 Percent 基准（{data.skillID}），忽略");
                            break;
                        }
                        var energy = stats.GetStatStruct(atom.statType);
                        stats.SetStatRange(atom.statType, max: energy.Max + value);
                    }
                    else
                    {
                        // 标量属性：BaseFlat/Percent 修改器（docs/20 §5.1 基准纪律——baseType 二元组生效）。
                        // **Percent 双语义统一（2026-10-01 用户拍板「为什么不用 Percent？应当全部统一」——
                        // 对齐 GI 命座口径）**：百分比面板属性（治疗效率/吸血等基值 0~100 效率刻度）的
                        // 「提升X%」=**+X 个百分点**（BaseFlat——0 基值×相对提升恒 0=旧版吸血失效根因
                        // docs/14 §105）；点数属性（移速/攻速等）的「提升X%」=相对提升（BasePercent
                        // ×(1+X%)）。属性族清单收口 PercentPanelStats——未来暴击/暴伤/充能类入表
                        var param = FindParam(data, atom.paramKey);
                        bool percent = param != null && param.baseType == SkillBaseType.Percent;
                        var type = percent && !PercentPanelStats.Contains(atom.statType)
                            ? StatModifierType.BasePercent
                            : StatModifierType.BaseFlat;
                        var mod = new StatModifier(atom.statType, value, type);
                        stats.AddModifier(mod);
                        unit.ConstellationModifiers.Add(mod);
                    }
                    break;
                }

                case SkillEffectKind.EnergyOverflowTransfer:
                    unit.EnergyOverflowPassive = true; // ApplyEnergy 消费（溢出→最近未满我方）
                    break;

                case SkillEffectKind.ApplyBuff:
                    // 登场 Buff 授予（芭芭拉 0命「获得歌声之环」）——本段（无 sim）不处理，
                    // 由 ApplyDeployBuffs 在 RegisterUnit 尾部消费
                    break;

                default:
                    // OnDeploy 语义只承载被动原子；其它 kind 配进 OnDeploy 属配置错误——Warn 暴露
                    GICLog.Warn($"[Constellation] OnDeploy 触发器不支持 kind={atom.kind}（{data.skillID}）——" +
                                "命座被动只消费 StatBoost/EnergyOverflowTransfer/ApplyBuff，请修技能配置");
                    break;
            }
        }

        private static SkillParam FindParam(SkillConfig.SkillData data, SkillParamKey key)
        {
            if (data.customParams == null || key == SkillParamKey.None) return null;
            foreach (var p in data.customParams)
                if (p.key == key) return p;
            return null;
        }
    }
}
