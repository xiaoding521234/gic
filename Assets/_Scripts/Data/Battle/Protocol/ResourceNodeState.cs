using System;
namespace GIC.Data
{


    /// <summary>
    /// 资源点运行时状态（docs/47）：开局态随 BattleMapData 一次下发（对齐 tiles/spawnCenters
    /// 协议模式）+秒拍头快照权威自愈（BattleSnapshot.resourceNodes）；采集余量变化走
    /// ResourceHarvest 命令增量（Host 权威，客户端零采集逻辑只镜像）。每格至多一个资源点
    /// （cell 可作唯一键；摩拉堆不占体积——与单位同格共存是常态而非异常）。
    /// </summary>
    [Serializable]
    public class ResourceNodeState
    {
        /// <summary>种类（ResourceNodeKind 的 int 值）</summary>
        public int kind;

        /// <summary>所在格（每格至多一个资源点）</summary>
        public BattleCell cell;

        /// <summary>剩余可采次数（charges 模型——SC2 矿脉同构：逐次扣减、归零即耗尽消失）</summary>
        public int remainingCharges;

        public ResourceNodeKind Kind => (ResourceNodeKind)kind;

        public ResourceNodeState Clone()
        {
            return new ResourceNodeState { kind = kind, cell = cell, remainingCharges = remainingCharges };
        }
    }
}
