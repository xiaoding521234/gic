using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GIC.Framework;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// 技能工厂 - 自动扫描带有 SkillAttribute 的技能类
    /// </summary>
    public static class SkillFactory
    {
        private static Dictionary<SkillName, Func<BaseSkill>> _creators = new();
        private static bool _isInitialized = false;
        
        /// <summary>
        /// 初始化工厂，扫描所有技能类
        /// </summary>
        public static void Initialize()
        {
            if (_isInitialized) return;
            
            _creators.Clear();
            
            // 扫描所有程序集中的技能类
            var skillTypes = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(asm => asm.GetTypes())
                .Where(t => t.IsClass && !t.IsAbstract && typeof(BaseSkill).IsAssignableFrom(t))
                .Where(t => t.GetCustomAttribute<SkillAttribute>() != null);
            
            foreach (var type in skillTypes)
            {
                var attr = type.GetCustomAttribute<SkillAttribute>();
                var skillId = attr.SkillID;
                
                // 创建工厂委托
                _creators[skillId] = () => (BaseSkill)Activator.CreateInstance(type);
                
                GICLog.Info($"[SkillFactory] 注册技能: {skillId} -> {type.Name}");
            }
            
            _isInitialized = true;
            GICLog.Info($"[SkillFactory] 初始化完成，共注册 {_creators.Count} 个技能");
        }
        
        /// <summary>
        /// 创建技能实例。三层分流（2026-09-25 B-1，docs/18 决策九 D5）：
        /// ① 注册专属类优先（渐进双轨——旧技能类兜底）；
        /// ② 无注册类且 effects 非空 → ConfiguredSkill 数据驱动通用类（效果原子管线）；
        /// ③ 无注册类且 effects 空 → UnimplementedSkill 占位（保 unit.Skills 与 UnitConfig.skills
        /// **索引严格对齐**——ActionData.skillIndex 双端同源映射，占位不可施放）而非 null
        /// （null 会令 InitSkills 跳过 → 后续技能索引整体前移错位，2026-09-18 B4 实证防）。
        /// </summary>
        public static BaseSkill CreateWithData(SkillConfig.SkillData data)
        {
            if (!_isInitialized) Initialize();
            if (data == null) return null;

            BaseSkill skill;
            if (data.skillID != SkillName.None && _creators.TryGetValue(data.skillID, out var creator))
            {
                skill = creator();
            }
            else if (data.skillID != SkillName.None && data.HasEffects)
            {
                skill = new ConfiguredSkill(); // 数据驱动通用类（加技能=配 effects 零代码）
            }
            else
            {
                if (data.skillID != SkillName.None)
                    GICLog.Warn($"[SkillFactory] 未注册且无效果原子: {data.skillID} → 占位（不可施放）");
                skill = new UnimplementedSkill(); // None 配置条目=空槽，占位保索引对齐（不告警）
            }

            skill?.Init(data);
            return skill;
        }
        
        /// <summary>
        /// 手动注册技能（用于特殊情况）
        /// </summary>
        public static void Register(SkillName skillId, Func<BaseSkill> creator)
        {
            if (!_isInitialized) Initialize();
            _creators[skillId] = creator;
        }
        
        /// <summary>
        /// 检查技能是否已注册
        /// </summary>
        public static bool IsRegistered(SkillName skillId)
        {
            if (!_isInitialized) Initialize();
            return _creators.ContainsKey(skillId);
        }
        
        /// <summary>
        /// 获取所有已注册的技能ID
        /// </summary>
        public static IEnumerable<SkillName> GetAllRegisteredSkills()
        {
            if (!_isInitialized) Initialize();
            return _creators.Keys;
        }
    }
}


