using UnityEngine;

/// <summary>
/// 移动/作用力类型枚举
/// </summary>
public enum ForceType
{
    [InspectorName("步行")] Walk = 1,
    [InspectorName("飞行")] Fly = 2,
    [InspectorName("两栖")] Amphibious = 3,
    [InspectorName("击退")] Knockback = 4,
    [InspectorName("牵引")] Pull = 5,
    [InspectorName("跳跃")] Jump = 6,
    [InspectorName("瞬移")] Teleport = 7
}
