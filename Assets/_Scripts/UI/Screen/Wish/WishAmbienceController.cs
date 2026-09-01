using System.Collections;
using UnityEngine;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.Battle;
using GIC.Tool;

namespace GIC.UI
{
    /// <summary>
    /// 祈愿界面氛围特效：烟雾层 + 元素粒子，由 ParticleSystem 驱动
    /// 粒子层级在 Ambience 子 Canvas (sortingOrder=75) 下渲染，
    /// 位于 BackgroundLayer(50) 之上、主 UI Canvas(100) 之下
    /// </summary>
    public class WishAmbienceController : MonoBehaviour
    {
        [Header("粒子系统")]
        [SerializeField] private ParticleSystem smokeParticles;
        [SerializeField] private ParticleSystem elementParticles;

        private ParticleSystem.Particle[] _particleBuffer = new ParticleSystem.Particle[128];
        private Coroutine _speedBoostCoroutine;

        // 缓存原始速度（只在首次加速时记录，避免连续切换累积）
        private float _origVelXMin, _origVelXMax, _origVelYMin, _origVelYMax;
        private float _origEmissionRate;
        private bool _hasOrigVel;

        [Header("粒子循环（飞出左边重生在右边）")]
        [SerializeField] private float wrapLeftX = -1100f;
        [SerializeField] private float wrapRightX = 1100f;

        private ParticleSystem.Particle[] _wrapBuffer = new ParticleSystem.Particle[256];

        private void OnEnable()
        {
            StartAmbience();
        }

        private void OnDisable()
        {
            StopAmbience();
        }

        public void StartAmbience()
        {
            StartWithPrewarm(smokeParticles);
            StartWithPrewarm(elementParticles);
        }

        /// <summary>
        /// 手动预热：Simulate 模拟一个完整周期，使粒子预填充整个屏幕，
        /// 而不是全部从生成点（右侧）开始飞入。prewarm 标志只对 playOnAwake 生效，手动 Play 不会触发。
        /// </summary>
        private void StartWithPrewarm(ParticleSystem ps)
        {
            if (ps == null) return;
            ps.Clear(true);
            ps.Simulate(ps.main.duration, true, true);
            ps.Play(true);
        }

        public void StopAmbience()
        {
            if (smokeParticles != null) smokeParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (elementParticles != null) elementParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        /// <summary>
        /// 每帧将飞出左边的粒子瞬移到右边，保持屏幕粒子密度恒定。
        /// 解决反复切换卡池（加速）导致粒子飞散后密度下降的问题。
        /// </summary>
        private void LateUpdate()
        {
            WrapParticles(elementParticles);
            WrapParticles(smokeParticles);
        }

        private void WrapParticles(ParticleSystem ps)
        {
            if (ps == null) return;

            int count = ps.GetParticles(_wrapBuffer);
            if (count == 0) return;

            bool changed = false;
            for (int i = 0; i < count; i++)
            {
                if (_wrapBuffer[i].position.x < wrapLeftX)
                {
                    var pos = _wrapBuffer[i].position;
                    pos.x = wrapRightX;
                    _wrapBuffer[i].position = pos;
                    changed = true;
                }
            }
            if (changed)
                ps.SetParticles(_wrapBuffer, count);
        }

        /// <summary>
        /// 切换卡池时立即触发粒子加速。
        /// </summary>
        public void OnPoolSwitching()
        {
            BoostElementSpeed(8f, 0.6f);
        }

        /// <summary>
        /// 设置粒子颜色（传入角色元素的对应颜色）。
        /// 同时更新 startColor（影响新粒子）和已存活粒子的颜色。
        /// </summary>
        public void SetElementColor(Color color)
        {
            if (elementParticles != null)
            {
                var main = elementParticles.main;
                main.startColor = color;

                int count = elementParticles.GetParticles(_particleBuffer);
                for (int i = 0; i < count; i++)
                    _particleBuffer[i].startColor = color;
                elementParticles.SetParticles(_particleBuffer, count);
            }
        }

        /// <summary>
        /// 临时加快元素粒子速度，加速后平滑减速回原速。
        /// 连续切换时基于缓存的原速计算，不累积加速。
        /// </summary>
        private void BoostElementSpeed(float multiplier, float duration)
        {
            if (elementParticles == null) return;

            var vel = elementParticles.velocityOverLifetime;
            var emission = elementParticles.emission;

            // 首次记录原始速度和发射率
            if (!_hasOrigVel)
            {
                _origVelXMin = vel.x.constantMin;
                _origVelXMax = vel.x.constantMax;
                _origVelYMin = vel.y.constantMin;
                _origVelYMax = vel.y.constantMax;
                _origEmissionRate = emission.rateOverTime.constant;
                _hasOrigVel = true;
            }

            if (_speedBoostCoroutine != null)
                StopCoroutine(_speedBoostCoroutine);
            _speedBoostCoroutine = StartCoroutine(SpeedBoostRoutine(multiplier, duration));
        }

        private IEnumerator SpeedBoostRoutine(float multiplier, float duration)
        {
            var vel = elementParticles.velocityOverLifetime;
            var emission = elementParticles.emission;

            // 瞬间加速 + 同步提高发射率（维持屏幕密度）
            vel.x = new ParticleSystem.MinMaxCurve(_origVelXMin * multiplier, _origVelXMax * multiplier);
            vel.y = new ParticleSystem.MinMaxCurve(_origVelYMin * multiplier, _origVelYMax * multiplier);
            emission.rateOverTime = _origEmissionRate * multiplier;

            // 加速持续 duration 秒
            yield return Wait.Seconds(duration);

            // 0.5 秒平滑减速回原速 + 同步降低发射率
            float decelTime = 0.5f;
            float elapsed = 0f;
            while (elapsed < decelTime)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / decelTime;
                vel.x = new ParticleSystem.MinMaxCurve(
                    Mathf.Lerp(_origVelXMin * multiplier, _origVelXMin, t),
                    Mathf.Lerp(_origVelXMax * multiplier, _origVelXMax, t));
                vel.y = new ParticleSystem.MinMaxCurve(
                    Mathf.Lerp(_origVelYMin * multiplier, _origVelYMin, t),
                    Mathf.Lerp(_origVelYMax * multiplier, _origVelYMax, t));
                emission.rateOverTime = Mathf.Lerp(_origEmissionRate * multiplier, _origEmissionRate, t);
                yield return null;
            }

            // 确保精确恢复
            vel.x = new ParticleSystem.MinMaxCurve(_origVelXMin, _origVelXMax);
            vel.y = new ParticleSystem.MinMaxCurve(_origVelYMin, _origVelYMax);
            emission.rateOverTime = _origEmissionRate;
        }
    }
}
