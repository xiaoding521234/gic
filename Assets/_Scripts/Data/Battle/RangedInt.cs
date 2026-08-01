using System;
using UnityEngine;
using GIC.Framework;
using GIC.Data.Event;
using GIC.UI;
using GIC.Battle;
using GIC.Tool;
namespace GIC.Data
{


    /// <summary>
    /// 带上下限的整数属性
    /// </summary>
    [Serializable]
    public struct RangedInt
    {
        [SerializeField] private int _min;
        [SerializeField] private int _max;
        [SerializeField] private int _value;

        public int Min => _min;
        public int Max => _max;

        public int Value
        {
            get => _value;
            set => _value = Mathf.Clamp(value, _min, _max);
        }

        public RangedInt(int min, int max, int initialValue)
        {
            _min = min;
            _max = max;
            _value = Mathf.Clamp(initialValue, min, max);
        }

        public RangedInt(int min, int max) : this(min, max, 0) { }

        public static implicit operator int(RangedInt r) => r._value;

        public void Add(int amount) => Value = _value + amount;
        public void Subtract(int amount) => Value = _value - amount;
        
        // 修改范围
        public void SetMin(int min)
        {
            _min = min;
            Value = _value; // 重新钳制
        }
        
        public void SetMax(int max)
        {
            _max = max;
            Value = _value; // 重新钳制
        }

        public override string ToString() => _value.ToString();
    }
}

