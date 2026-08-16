using System.Collections.Generic;
using UnityEngine;

namespace GIC.Framework
{
    /// <summary>
    /// WaitForSeconds 缓存 — UI 动画协程高频延迟复用，避免每次 yield 重复分配
    /// 按毫秒精度取整作 key：相同延迟复用同一实例，微小浮点差异不会无限膨胀字典
    /// 用法：yield return Wait.Seconds(0.5f);
    /// </summary>
    public static class Wait
    {
        private const float Precision = 0.001f;
        private static readonly Dictionary<int, WaitForSeconds> Cache = new();

        public static WaitForSeconds Seconds(float seconds)
        {
            int key = Mathf.RoundToInt(seconds / Precision);
            if (!Cache.TryGetValue(key, out var wait))
            {
                wait = new WaitForSeconds(key * Precision);
                Cache[key] = wait;
            }
            return wait;
        }
    }
}
