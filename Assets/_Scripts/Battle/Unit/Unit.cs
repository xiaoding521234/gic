using System;
using System.Collections.Generic;
using UnityEngine;
using GIC.Data;
using GIC.Framework;
namespace GIC.Battle
{


    public class Unit : MonoBehaviour
    {
        private Dictionary<System.Type, IUnitComponent> _components = new();
        public UnitConfig.UnitData RawData { get; private set; }

        public List<BaseSkill> Skills = new();
        public List<BaseBuff> Buffs = new();

        // ==================== 命座（B8 批 2026-09-30，docs/09：0命=固有被动、1-3命=重复出战升命） ====================

        /// <summary>命座等级（0~3；3★+同名重复出战提升、封顶 MaxConstellation=3；尸体照升〔docs/05 §5.4〕
        /// ——ConstellationApplier 登场/升命时按层幂等重算被动；快照 BuildUnitState 携带）</summary>
        public int ConstellationLevel;

        /// <summary>命座被动属性修改器（ConstellationApplier 重算时全撤全挂的登记表——升命单调增，
        /// 重算=先 RemoveModifier 全撤再按层重挂；Energy 容量走 SetStatRange 不入此表）</summary>
        public readonly List<StatModifier> ConstellationModifiers = new();

        /// <summary>元能溢出转移被动（安柏1命 OnDeploy[EnergyOverflowTransfer] 注册——ApplyEnergy 消费：
        /// 获取元能溢出部分转移给切比雪夫最近未满我方〔同距 unitId 升序〕）</summary>
        public bool EnergyOverflowPassive;

        /// <summary>操控层级覆盖星（-1=无覆盖读 RawData.starLevel；试招沙盒置 5=魔神档玩家全手操——
        /// 仅改操控分档（TierOf 唯一消费方），数值回落公式仍读原星=数值原味；Unit 为运行时实例
        /// 零资产污染、随战斗销毁自然回收，无需还原。2026-10-05 时轮编辑器「开一把试招」）</summary>
        [NonSerialized] public int TierOverrideStars = -1;

        /// <summary>部署回合（决策八十六批D，docs/active/45 §6——RegisterUnit 写入〔真源=sim.CurrentTurn〕；
        /// 周期类建筑效果以「当前回合−部署回合」相对计数：骑士团部署即召 1 个+每过 效果周期 回合再召——
        /// 部署当回合末不再重复触发（相对差 0 跳过）。开局单位=0；随消散销毁无残留</summary>
        [NonSerialized] public int DeployedTurn;

        /// <summary>
        /// 单位体积（docs/05 §5.3：角色/造物 = 1，建筑 = 2；格子体积容量 3。
        /// 体积判定在阻挡规则之上，无视阻挡能力也不可绕过）——
        /// 真源=UnitData.GetVolume() 单出口（2026-09-27 复审收口：原「Building?2:1」在此与部署校验双源）
        /// </summary>
        public int Volume => RawData != null ? RawData.GetVolume() : 1;


        private void Awake()
        {
            EnsureComponentsCollected();
        }

        /// <summary>组件收集（幂等）。Play 域由 Awake 完成；**编辑器域（非 Play）Instantiate prefab
        /// 不执行 Awake**——回放回归等编辑器域工具经 InitWithData 时兜底收集（Replay harness 批
        /// 2026-10-20 实证：跳过则 _components 空→UnitStats/UnitIdentity 全缺失→攻速 0/查找失败/技能空转）</summary>
        public void EnsureComponentsCollected()
        {
            if (_components.Count > 0) return; // Play 域 Awake 已收集=幂等跳过
            foreach (var comp in GetComponents<IUnitComponent>())
            {
                _components[comp.GetType()] = comp;
            }
        }

        public void InitWithData(UnitConfig.UnitData data)
        {
            EnsureComponentsCollected(); // 编辑器域兜底（Awake 非运行态不执行）
            RawData = data;
            foreach (var comp in _components.Values)
            {
                comp.Init(this);
            }

            InitSkills(data);
        }

        private void InitSkills(UnitConfig.UnitData data)
        {
            var skills = data.skills;
            if (skills == null) return;
            for (int i = 0; i < skills.Count; i++)
            {
                var sd = skills[i]?.data;
                if (sd == null)
                {
                    // 空引用槽占位（2026-09-25 三轮审查 S6）：HUD 上交的 skillIndex 用 UnitConfig.skills
                    // 配置序、Host 消费用 unit.Skills 实例序——跳过会让后续技能索引整体前移、两序错位
                    // （点 A 技能放 B 技能且无告警）。占位件与 SkillFactory 的 UnimplementedSkill 哲学同款
                    // （不可施放、零产出），配置序=实例序恒成立；数据错误仍 Warn 暴露，请修 UnitConfig
                    GICLog.Warn($"[Unit] {data.unitName} 技能引用列表第 {i} 位为空槽——占位保 skillIndex 对齐，请修 UnitConfig");
                    AddSkill(new UnimplementedSkill());
                    continue;
                }
                var skill = SkillFactory.CreateWithData(sd);
                if (skill != null)
                {
                    AddSkill(skill);
                }
            }
        }

        /// <summary>
        /// 获取指定类型的 Unit 组件
        /// </summary>
        public T GetUnitComponent<T>() where T : class, IUnitComponent
        {
            _components.TryGetValue(typeof(T), out var comp);
            return comp as T;
        }

        public bool HasUnitComponent<T>() where T : IUnitComponent
        {
            return _components.ContainsKey(typeof(T));
        }

        public void AddSkill(BaseSkill skill)
        {
            Skills.Add(skill);
            skill.owner = this;
        }

        public void RemoveSkill(BaseSkill skill)
        {
            Skills.Remove(skill);
        }

        public void AddBuff(BaseBuff buff)
        {
            Buffs.Add(buff);
            buff.owner = this;
        }

        public void RemoveBuff(BaseBuff buff)
        {
            Buffs.Remove(buff);
        }


    }
}


