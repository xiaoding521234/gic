namespace GIC.Battle
{
    /// <summary>
    /// 摩拉获取效应（docs/47 资源点批，2026-10-27）——MoraSpendEffect 获取侧对偶：玩家级池写
    /// （ApplyMoraDelta 含手牌货币条目同步）。采集（站立/途经两路径）的共用产出原子；
    /// 未来资源产出族（宝箱/任务奖励类）同用。TargetUnitId=玩家 ID。
    /// </summary>
    public class MoraGainEffect : BattleEffect
    {
        public int Amount;

        /// <summary>命中时刻（秒，相对片播放起点；0=立即——回合末站立采集/未来产出路径）。
        /// 途经采集=行走到达资源点格时刻（2026-10-28 返修：客户端到点再入账，与堆减件/采集
        /// 飘字/体力元能同拍——WYSIWYG 三件套效应侧载体）</summary>
        public float HitSeconds;

        public MoraGainEffect(string playerId, int amount, float hitSeconds = 0f)
        {
            TargetUnitId = playerId;
            Amount = amount;
            HitSeconds = hitSeconds;
        }
    }
}
