using UnityEngine;

namespace GIC.Battle
{
    /// <summary>
    /// 投射物面内自转（2026-11-09 用户拍板「芭芭拉的水弹能一直绕中心旋转」）：飞行中的专属弹射物
    /// 绕自身中心（sprite pivot=中心）在屏幕平面内持续旋转。世界旋转基座由创建方传入（相机朝向布告板），
    /// 本组件只在其上叠加 Z 轴角度——位置仍由飞行补间逐帧写，两者互不干扰。
    /// 池件复用：OnEnable 归零角度，回池 SetActive(false) 后重新出仓不复用上一条箭的相位。
    /// </summary>
    public sealed class ProjectileSpinner : MonoBehaviour
    {
        /// <summary>角速度（度/秒；正=逆时针，负=顺时针；0=不自转——创建方按配置禁用组件）</summary>
        private float _degreesPerSecond;
        private Quaternion _baseRotation = Quaternion.identity;
        private float _angle;

        /// <summary>配置自转：baseRotation=世界系布告板基座（通常取相机朝向），degreesPerSecond=角速度</summary>
        public void Configure(Quaternion baseRotation, float degreesPerSecond)
        {
            _baseRotation = baseRotation;
            _degreesPerSecond = degreesPerSecond;
            _angle = 0f;
            Apply();
        }

        private void OnEnable() => _angle = 0f;

        private void Update()
        {
            if (_degreesPerSecond == 0f) return;
            _angle = Mathf.Repeat(_angle + _degreesPerSecond * Time.deltaTime, 360f);
            Apply();
        }

        private void Apply() => transform.rotation = _baseRotation * Quaternion.Euler(0f, 0f, _angle);
    }
}
