namespace GIC.Battle
{
    /// <summary>
    /// 确定性随机数（F0-4，docs/active/50 §7.3）：xorshift32，状态可序列化。
    /// **替换 `System.Random` 的原因**：其内部状态不可导出——帧同步无法在 hash 中比对、
    /// 回滚/重连无法精确恢复；xorshift32 只有 32 位 state，跨端/回放/检查点完全可复现。
    /// 铁律：模拟层唯一随机源，仍只经 `BattleSimState.RollChance` 单出口消费。
    /// </summary>
    public sealed class DeterministicRng
    {
        private const uint DefaultSeed = 0x9E3779B9u; // 黄金比例常数（0 种子兜底）
        private uint _state;

        public DeterministicRng(int seed)
        {
            _state = seed == 0 ? DefaultSeed : unchecked((uint)seed);
        }

        /// <summary>当前状态（hash / 检查点用）</summary>
        public uint State => _state;

        /// <summary>恢复状态（回滚/重连检查点用；0=非法值回落默认种子）</summary>
        public void Restore(uint state) => _state = state == 0 ? DefaultSeed : state;

        /// <summary>xorshift32：周期 2^32-1，零状态不可达</summary>
        public uint NextUInt()
        {
            uint x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x == 0 ? DefaultSeed : x;
            return _state;
        }

        /// <summary>[0,1) 双精度（24 位有效精度，与 System.Random.NextDouble 同量级）</summary>
        public double NextDouble() => (NextUInt() >> 8) * (1.0 / 16777216.0);

        /// <summary>[0,1) 单精度（表现层/评分用；模拟判定仍走 NextDouble）</summary>
        public float NextFloat01() => (NextUInt() >> 8) * (1f / 16777216f);

        /// <summary>[0,maxExclusive) 整数（maxExclusive<=0 返回 0）</summary>
        public int Next(int maxExclusive)
            => maxExclusive <= 0 ? 0 : (int)(NextUInt() % (uint)maxExclusive);
    }
}