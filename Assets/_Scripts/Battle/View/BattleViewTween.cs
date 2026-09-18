using System;
using System.Collections;
using UnityEngine;

namespace GIC.Battle
{
    /// <summary>
    /// 表现层补间统一驱动（2026-09-18 统一化批次：伤害数字/投射物飞行/移动插值的 elapsed-while 手写循环收口于此，
    /// docs/14 §63）。时长口径不变：调用方传 duration 时自行除以 _playbackSpeed（与既有行为一致）。
    /// </summary>
    public static class BattleViewTween
    {
        /// <summary>归一化补间：t∈[0,1] 逐帧驱动，末帧保证 t=1（收尾精确落点，防末帧插值残差）；duration≤0 直落终值</summary>
        public static IEnumerator Over(float duration, Action<float> update)
        {
            if (duration <= 0f)
            {
                update(1f);
                yield break;
            }
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                update(Mathf.Clamp01(elapsed / duration));
                yield return null;
            }
            update(1f);
        }
    }
}
