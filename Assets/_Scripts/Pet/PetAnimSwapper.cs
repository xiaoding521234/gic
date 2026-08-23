using UnityEngine;

namespace GIC.Pet
{
    /// <summary>
    /// 方案B测试场景动作切换（运行时只管播放）：UI 按钮由管线在编辑期预置于场景
    /// （PaimonRetargetTest.unity 的 AnimUICanvas），onClick 持久绑定 Play(clip名)。
    /// 表情走运行时情绪层（PetEmotionController，docs/19 §2.6 三层架构）：
    /// Play 按动作名映射下发情绪指令，映射表 Inspector 中文可配。
    /// </summary>
    public class PetAnimSwapper : MonoBehaviour
    {
        [System.Serializable]
        public class 动作情绪映射
        {
            [Tooltip("动作名片段（clip 名包含即命中，如 Anger）")] public string 动作名片段;
            [Tooltip("情绪名（PetEmotionController 情绪表中的名，空=不表情绪）")] public string 情绪名;
        }

        [SerializeField] private Animation targetAnimation; // Paimon_MMD 根上的 Animation 组件

        [Header("动作→情绪映射（表情由情绪层驱动，不烘焙进 clip）")]
        [SerializeField] private 动作情绪映射[] 情绪映射 = new[]
        {
            new 动作情绪映射 { 动作名片段 = "Anger", 情绪名 = "Anger" },
        };

        [SerializeField] private PetEmotionController emotionController; // 情绪层（可空=无表情）

        public void Play(string clipName)
        {
            if (targetAnimation == null) return;
            var state = targetAnimation[clipName];
            if (state == null || state.clip == null) return;
            state.wrapMode = WrapMode.Loop;
            targetAnimation.Stop();
            targetAnimation.clip = state.clip;
            targetAnimation.Play(clipName);

            // 情绪下发：无映射的情绪动作 → 下发空名清回默认脸
            if (emotionController != null)
            {
                var emo = "";
                foreach (var m in 情绪映射)
                    if (!string.IsNullOrEmpty(m.动作名片段) && clipName.Contains(m.动作名片段)) { emo = m.情绪名; break; }
                emotionController.SetEmotion(emo);
            }
        }
    }
}
