using GIC.Data;
namespace GIC.Battle
{
    /// <summary>
    /// 资源点采集事实效应（docs/47，2026-10-27）：余量在采集判定点已扣（sim.TryHarvestResourceNode
    /// ——同建筑元能引擎「能量落地同拍判定」模式），本效应只承载事实供命令发射（ResourceHarvest
    /// type=23）——客户端 BattleBoard 余量刷新/耗尽移除+采集飘字；**资源点余量变化不进本效应**
    /// （sim 状态真值在判定点已改，命令仅镜像）。TargetUnitId=采集者单位 id。
    /// 命中时刻（2026-10-28 返修「表现对齐动画」）：站立采集=回合末收尾类 HitSeconds 恒 0 立即；
    /// 途经采集=**行走到达资源点格时刻**（i×MoveStepSeconds+k×CommandStaggerSeconds——移动补间
    /// 与 PlayMoveCoroutine 同源），客户端到点再减件/弹字（勿在段首即减）。
    /// </summary>
    public class ResourceHarvestEffect : BattleEffect
    {
        /// <summary>资源点格（客户端定位表现件）</summary>
        public BattleCell NodeCell;

        /// <summary>本次获得摩拉（资源点定义收益——与 MoraGainEffect 同值成对产出）</summary>
        public int MoraGain;

        /// <summary>采集后剩余次数（0=已耗尽——客户端移除表现件）</summary>
        public int Remaining;

        /// <summary>命中时刻（秒，相对片播放起点；0=立即——站立采集回合末）</summary>
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
