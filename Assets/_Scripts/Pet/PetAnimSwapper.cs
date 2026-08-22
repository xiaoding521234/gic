using UnityEngine;

namespace GIC.Pet
{
    /// <summary>
    /// 方案B测试场景动作切换（运行时只管播放）：UI 按钮由管线在编辑期预置于场景
    /// （PaimonRetargetTest.unity 的 AnimUICanvas），onClick 持久绑定 Play(clip名)。
    /// </summary>
    public class PetAnimSwapper : MonoBehaviour
    {
        [SerializeField] private Animation targetAnimation; // Paimon_MMD 根上的 Animation 组件

        public void Play(string clipName)
        {
            if (targetAnimation == null) return;
            var state = targetAnimation[clipName];
            if (state == null || state.clip == null) return;
            state.wrapMode = WrapMode.Loop;
            targetAnimation.Stop();
            targetAnimation.clip = state.clip;
            targetAnimation.Play(clipName);
        }
    }
}
