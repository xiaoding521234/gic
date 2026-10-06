using UnityEngine;
using UnityEngine.UI;
using GIC.Data;
namespace GIC.UI
{


    /// <summary>
    /// 小数据面板 — 单位详情卡内嵌的属性摘要（原神样式：元素主题深色底 + 左标签右粗值）
    /// 位于技能区块之下、介绍区块之上；点击整块由 UnitDetailPanel 转发打开全部数据面板
    /// </summary>
    public class UnitStatBriefPanel : MonoBehaviour
    {
        public Image 背景;
        public Button 点击按钮;
        public StatRowView[] 行列表; // 顺序：生命值/攻击力/防御力/精通/幸运/理智

        [Header("元素主题")]
        [SerializeField, Range(0.05f, 1f)] private float 底色明度系数 = 0.35f;
        [SerializeField, Range(0f, 1f)] private float 底色不透明度 = 0.95f;

        public void Init(UnitConfig.UnitData raw)
        {
            if (raw == null || 行列表 == null || 行列表.Length < 6) return;

            // 元素主题底色：单位元素色加深（对标原神属性小面板）
            var config = ElementFactionConfig.Instance;
            var elementColor = config != null
                ? config.GetElementColor(raw.selfElement)
                : new Color(0.31f, 0.63f, 0.94f);
            if (背景 != null)
            {
                背景.color = new Color(
                    elementColor.r * 底色明度系数,
                    elementColor.g * 底色明度系数,
                    elementColor.b * 底色明度系数,
                    底色不透明度);
            }

            行列表[0].SetValue($"{raw.GetEffectiveHP():N0}");
            行列表[1].SetValue($"{raw.GetEffectiveAttack():N0}");
            行列表[2].SetValue($"{raw.GetEffectiveDefense():N0}");
            行列表[3].SetValue($"{raw.GetEffectiveMastery():N0}");
            行列表[4].SetValue($"{raw.GetEffectiveLuck():N0}");
            行列表[5].SetValue($"{raw.GetEffectiveSanity():N0}");

            // 生命值行图标 = 单位元素图标（原神小面板同款）；其余行图标为预留槽位
            for (int i = 0; i < 行列表.Length; i++)
                行列表[i].SetIcon(null);
            if (config != null)
                行列表[0].SetIcon(config.GetElementIconStroke(raw.selfElement));
        }
    }


}
