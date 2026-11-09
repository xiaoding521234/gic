using UnityEngine;
using UnityEngine.Serialization;
namespace GIC.Data
{

    /// <summary>
    /// 视野暴露 Buff 配置（安柏变奏「全面侦查」，2026-10-17 迷雾批追拍七）：标记型状态——数值位仅
    /// 持续秒数（关联描述 {DurationSeconds} 金色渲染单源；-1=永久）。行为全在迷雾消费方
    /// （Host 账本/客户端 BattleFog），本资产不装过程。同名文件硬规则（docs/14 §131①）。
    /// </summary>
    [CreateAssetMenu(fileName = "BuffConfig", menuName = "Game/BuffConfig 视野暴露（变奏全面侦查）")]
    public class VisionExposureBuffConfig : BuffConfig
    {
        [Header("持续秒数（安柏变奏视野暴露；-1=永久——关联描述 {DurationSeconds} 渲染本地化「永久」）")]
        [FormerlySerializedAs("持续回合")]
        public int 持续秒数 = 3;

        /// <summary>关联描述占位符（{DurationSeconds}——本资产单源）</summary>
        public override int? ResolveRelatedPlaceholder(string key)
            => key == KeyStatDurationSeconds ? 持续秒数 : null;
    }
}
