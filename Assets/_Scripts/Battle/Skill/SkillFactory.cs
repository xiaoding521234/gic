using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.UI;
using GIC.Tool;
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
                
                Debug.Log($"[SkillFactory] 注册技能: {skillId} -> {type.Name}");
            }
            
            _isInitialized = true;
            Debug.Log($"[SkillFactory] 初始化完成，共注册 {_creators.Count} 个技能");
        }
        
        /// <summary>
        /// 创建技能实例
        /// </summary>
        private static BaseSkill CreateWithID(SkillName skillID)
        {
            if (!_isInitialized) Initialize();
            
            if (skillID == SkillName.None) return null;
            
            if (_creators.TryGetValue(skillID, out var creator))
            {
                return creator();
            }
            
            Debug.LogWarning($"[SkillFactory] 未注册的技能: {skillID}");
            return null;
        }

        public static BaseSkill CreateWithData(SkillConfig.SkillData data)
        {
            if (!_isInitialized) Initialize();
            
            BaseSkill skill = CreateWithID(data.skillID);

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


