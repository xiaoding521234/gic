using GIC.Data;
namespace GIC.Battle
{
    /// <summary>
    /// 资源点采集事实效应（docs/47，2026-10-27）：余量在采集判定点已扣（sim.TryHarvestResourceNode
    /// ——同建筑元能引擎「能量落地同拍判定」模式），本效应只承载事实供命令发射（ResourceHarvest
    /// type=23）——客户端 BattleBoard 余量刷新/耗尽移除+采集飘字；**资源点余量变化不进本效应**
    /// （sim 状态真值在判定点已改，命令仅镜像）。TargetUnitId=采集者单位 id。
    /// 读条采集与移动快速采集均在实际成功 tick 立即产出；HitSeconds 恒 0。
    /// 旧“移动结束后按路径格补算、按历史补间时刻延迟入账”语义已退役。
    /// </summary>
    public class ResourceHarvestEffect : BattleEffect
    {
        /// <summary>资源点格（客户端定位表现件）</summary>
        public BattleCell NodeCell;

        /// <summary>本次获得摩拉（资源点定义收益——与 MoraGainEffect 同值成对产出）</summary>
        public int MoraGain;

        /// <summary>采集后剩余次数（0=已耗尽——客户端移除表现件）</summary>
        public int Remaining;

        /// <summary>命中时刻（秒，相对片播放起点；0=立即——站立采集秒拍末）</summary>
        public float HitSeconds;

        public ResourceHarvestEffect(string collectorUnitId, BattleCell nodeCell, int moraGain, int remaining,
            float hitSeconds = 0f)
        {
            TargetUnitId = collectorUnitId;
            NodeCell = nodeCell;
            MoraGain = moraGain;
            Remaining = remaining;
            HitSeconds = hitSeconds;
        }
    }
}
