using System;
namespace GIC.Data
{
    /// <summary>单位普通采集读条状态。真源在 Host，快照与增量命令只负责镜像。</summary>
    [Serializable]
    public class HarvestChannelState
    {
        public string unitId;
        public BattleCell nodeCell;
        public int startTick;
        public int finishTick;

        public HarvestChannelState Clone()
        {
            return new HarvestChannelState
            {
                unitId = unitId,
                nodeCell = nodeCell,
                startTick = startTick,
                finishTick = finishTick,
            };
        }
    }

    /// <summary>安柏 3B「循迹觅宝」的每单位下次发现 tick；0=尚未初始化。</summary>
    [Serializable]
    public class ResourceDiscoveryState
    {
        public string unitId;
        public int nextTick;

        public ResourceDiscoveryState Clone()
        {
            return new ResourceDiscoveryState { unitId = unitId, nextTick = nextTick };
        }
    }

    /// <summary>安柏移动快速采集的每单位、每资源点独立冷却。</summary>
    [Serializable]
    public class QuickHarvestCooldownState
    {
        public string unitId;
        public BattleCell nodeCell;
        public int readyTick;

        public QuickHarvestCooldownState Clone()
        {
            return new QuickHarvestCooldownState
            {
                unitId = unitId,
                nodeCell = nodeCell,
                readyTick = readyTick,
            };
        }
    }
}
