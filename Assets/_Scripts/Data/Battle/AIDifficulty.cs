using UnityEngine;
namespace GIC.Data
{


    /// <summary>
    /// AI 玩家难度档（docs/active/42 §9.2「难度＝决策质量阶梯，零资源作弊」；AI-4 批落地）。
    /// 档位只开/关既有决策件——任何档不注入资源、不给信息特权、不改规则。
    /// 放 GIC.Data（PlayerInfo/房间名册序列化消费——SpawnPositionType 同款先例；
    /// 消费侧=Battle.AIPlayerConfig 门控/配额脑）。
    /// 默认档=棋圣（现行全件开启——AI-2 验证基线零回归；房间 AI 行可选，2026-10-12 选择器批）。
    /// </summary>
    public enum AIDifficulty
    {
        [InspectorName("学徒")]
        Apprentice = 0,

        [InspectorName("劲敌")]
        Rival = 1,

        [InspectorName("棋圣")]
        Grandmaster = 2,

        [InspectorName("天理")]
        Providence = 3,
    }
}
