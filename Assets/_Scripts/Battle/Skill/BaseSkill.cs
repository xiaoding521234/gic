using System;
using System.Collections.Generic;
using UnityEngine;
using GIC.Data;
using GIC.Tool;
namespace GIC.Battle
{


    /// <summary>
    /// 技能基类
    /// </summary>
    public abstract class BaseSkill
    {
        public Unit owner;
        public SkillConfig.SkillData RawData { get; private set; }

        private SkillType skillType = SkillType.Normal;

        // 消耗类数值不走独立字段——一律经 SkillParamKey（EnergyCost 等）customParams：
        // 取值入口=BattleSimState.GetEnergyCost(skill.RawData)，勿在此另设字段防双源（2026-09-23 审查 Y7 清理）
        public Sprite icon;

        /// <summary>时轮时间轴（B-S1；null=无时轮——技能走旧即时行为兜底）</summary>
        public SkillTimelineAsset Timeline { get; private set; }

        public List<SkillParam> SkillParams { get; private set; }

        public virtual void Init(SkillConfig.SkillData rawData)
        {
            RawData = rawData;

            Timeline = rawData.timeline; // 时轮（B-S1）：发射时刻/逐发间隔/投射物规格单一真源

            // 深拷贝参数数据
            SkillParams = new List<SkillParam>();
            if (rawData.customParams != null)
            {
                foreach (var p in rawData.customParams)
                {
                    SkillParams.Add(new SkillParam(p.key,p.value,p.baseType));
                }
            }


            skillType = rawData.skillType;

            if(rawData.icon != null)
            {
                icon = rawData.icon;
            }
            else
            {
                string path = $"UI/Skills/{rawData.skillID.ToString().ToSnakeCase()}";
                icon = Resources.Load<Sprite>(path);
            }
            
        }

        public SkillParam GetParam(SkillParamKey key)
        {
            foreach (var p in SkillParams)
            {
                if (p.key == key)
                {
                    return p;
                }
            }
            return null;
        }

        public abstract bool CanCast(Unit caster);

        /// <summary>
        /// Host 侧结算（B4）：读片前快照、产出效应列表（TurnResolver 统一应用）——纯结算不改状态，
        /// 附着/反应/冻结等状态修改一律以 BattleEffect 形态产出。默认空（即时交互类技能的承载 B6 另议）。
        /// </summary>
        /// <param name="sim">Host 战场门面（位置/注册查询，勿改状态）</param>
        /// <param name="action">行动数据（direction/skillIndex/targetUnitId）</param>
        /// <param name="sliceSnapshot">片前快照（瞬发结算唯一状态源）</param>
        public virtual List<BattleEffect> ResolveEffects(BattleSimState sim, ActionData action, BattleSnapshot sliceSnapshot)
        {
            return new List<BattleEffect>();
        }

        /// <summary>技能参数取值（int；BaseType=Fixed 输出数值本身，其余输出数值本身由技能逻辑按语境解释）</summary>
        public int GetParamValue(SkillParamKey key, int fallback = 0)
        {
            var p = GetParam(key);
            return p != null ? p.value : fallback;
        }

        /// <summary>
        /// 方向推荐预判（2026-09-23 拍板：瞄准高亮分色——可选且推荐=半透明白/可选但不推荐=半透明红）：
        /// 从 from 沿 direction（十字归一）直线施放能否命中至少一个敌方单位（含尸体——Host 判定同语义）。
        /// 客户端 HUD 进瞄准态时逐方向调用——静态快照格位预判（同片敌方移动不可知，属提示非校验；
        /// Host 结算仍是权威）。默认=整线逐格扫描（虚空截断、ProjectileRule.MaxRange 上限），
        /// 与整线迸发类（箭雨）判定同语义；投射物接触截停（安柏战技）/近战距离段（凯亚霜袭）等
        /// 特殊判定形态由子类覆写——覆写须与本技能 ResolveEffects 的判定形态保持同语义，防预判与结算漂移。
        /// </summary>
        public virtual bool WouldHitEnemyInDirection(BattleMapData map, BattleSnapshot snapshot,
            string casterPlayerId, BattleCell from, Direction2D direction)
        {
            var delta = SkillHitResolver.DirectionToDelta(direction);
            for (int step = 1; step <= ProjectileRule.MaxRange; step++)
            {
                var cell = new BattleCell(from.x + delta.x * step, from.y + delta.y * step);
                if (!map.HasTile(cell.x, cell.y)) break; // 虚空截断
                if (SkillHitResolver.FindEnemiesAt(snapshot, casterPlayerId, cell).Count > 0) return true;
            }
            return false;
        }
    }
}


