using UnityEngine;
using UnityEngine.UI;
using GIC.Data;
namespace GIC.UI
{


    /// <summary>
    /// 小数据面板 — 单位详情卡内嵌的属性摘要（原神样式：行斑马纹 + 左标签右粗值）
    /// 位于技能区块之下、介绍区块之上；点击整块由 UnitDetailPanel 转发打开全部数据面板
    /// 背景 Image 已退役（2026-10-07 用户拍板「不需要大背景」，alpha=0 烘焙于 prefab）——仅保留组件作点击射线载体勿删
    /// </summary>
    public class UnitStatBriefPanel : MonoBehaviour
    {
        public Image 背景;
        public Button 点击按钮;
        public StatRowView[] 行列表; // 顺序：生命值/攻击力/防御力/元能上限/精通/幸运/理智

        // 行图标（原神官方属性图标，StatRowView.LoadStatIcon 加载）；null=槽位留空
        private static readonly string[] 行图标 = { "stat_hp", "stat_attack", "stat_defense", "stat_energy", "stat_mastery", "stat_luck", "stat_sanity" };

        [Header("行底斑马纹（元素主题色）")]
        [SerializeField, Range(0f, 1f)] private float 主题明条不透明度 = 0.55f;
        [SerializeField, Range(0f, 1f)] private float 主题暗条不透明度 = 0.25f;

        public void Init(UnitConfig.UnitData raw)
        {
            if (raw == null || 行列表 == null || 行列表.Length < 7) return;

            行列表[0].SetValue($"{raw.GetEffectiveHP():N0}");
            行列表[1].SetValue($"{raw.GetEffectiveAttack():N0}");
            行列表[2].SetValue($"{raw.GetEffectiveDefense():N0}");
            行列表[3].SetValue($"{raw.GetEffectiveEnergy():N0}"); // 元能上限（2026-10-07 用户拍板入基础段+小面板同展）
            行列表[4].SetValue($"{raw.GetEffectiveMastery():N0}");
            行列表[5].SetValue($"{raw.GetEffectiveLuck():N0}"); // 幸运/理智=平值（非百分比）
            行列表[6].SetValue($"{raw.GetEffectiveSanity():N0}");

            // 行图标=原神官方属性图标；行底=元素主题色明暗相间斑马纹（大背景退役后条底承主题色）
            var config = ElementFactionConfig.Instance;
            var elementColor = config != null
                ? config.GetElementColor(raw.selfElement)
                : new Color(0.31f, 0.63f, 0.94f);
            for (int i = 0; i < 行列表.Length; i++)
            {
                行列表[i].SetIcon(i < 行图标.Length ? StatRowView.LoadStatIcon(行图标[i]) : null);
                行列表[i].SetStripColor(elementColor, i % 2 == 0 ? 主题明条不透明度 : 主题暗条不透明度);
            }
        }
    }


}
