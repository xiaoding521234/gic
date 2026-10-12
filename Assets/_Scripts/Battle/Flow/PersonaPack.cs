using UnityEngine;
namespace GIC.Battle
{


    /// <summary>
    /// AI 玩家人格原型包（AI-4 批，docs/active/42 §9.1——**与势力/卡组解耦**，2026-10-12 用户拍板：
    /// 未来 AI 自由混搭构筑卡组，人格绑势力不成立——人格=纯评分权重原型，卡组决定可用机制、
    /// 人格决定用得多狠）。纯数据资产：同一评分骨架换参数即换人格，加新人格零代码。
    /// 同一评分骨架换参数即换人格；加新人格零代码。
    /// v1 消费面（配额脑评分乘子）：四桶基线权重=配额候选族乘子（军备桶=操魔神档/激进度=直接操控
    /// 边际/契约桶=号令延奏档/升命桶=升命投资档；科技桶候选未实装=dormant）；交战距离偏好/
    /// 背叛阈值/出价风格=AI-5 军团倾向与 AI-3 外交层消费位（预留）；消息风格=表达层预留。
    /// 验收锚（§9.1）：盲测回放仅凭行为统计可反推人格原型——权重只改倾向不改规则。
    /// </summary>
    [CreateAssetMenu(fileName = "Persona_", menuName = "GIC/AI 人格原型包")]
    public class PersonaPack : ScriptableObject
    {
        [Header("四桶基线权重（配额候选族乘子，1=中性）")]
        [Tooltip("军备桶：操魔神/战力投放倾向")]
        public float 军备桶权重 = 1f;

        [Tooltip("科技桶：建筑/科技投资（建筑购买未实装=dormant）")]
        public float 科技桶权重 = 1f;

        [Tooltip("契约桶：号令/延奏/外交倾向")]
        public float 契约桶权重 = 1f;

        [Tooltip("升命桶：命座投资倾向")]
        public float 升命桶权重 = 1f;

        [Header("战略倾向（AI-3/AI-5 消费位）")]
        [Tooltip("激进度：直接操控攻击边际乘子——狂战士高/守城者低")]
        public float 激进度 = 1f;

        [Tooltip("交战距离偏好（0=自动；正值倾向远距——AI-5 军团倾向消费，dormant）")]
        public float 交战距离偏好 = 0f;

        [Tooltip("背叛阈值（AI-3 态度账消费，dormant）")]
        public float 背叛阈值 = 1f;

        [Tooltip("出价风格（AI-3 契约档位决策消费，dormant）")]
        public float 出价风格 = 1f;

        [Tooltip("消息风格（谈判期快捷消息表达层预留，dormant）")]
        public string 消息风格 = "平静";
    }
}
