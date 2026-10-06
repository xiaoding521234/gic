using UnityEngine;
using UnityEngine.UI;
using GIC.Data;
namespace GIC.UI
{


    /// <summary>
    /// 全部数据面板 — 全屏暗遮罩 + 分区斑马纹行列表（原神样式；无问号按钮、无加成拆分值）
    /// 由 UnitDetailPanel 打开，× 关闭
    /// </summary>
    public class UnitStatsPanel : MonoBehaviour
    {
        public Button 关闭按钮;
        public ScrollRect 滚动区;
        public StatRowView[] 行列表; // 15 行：基础（生命/攻击/防御/精通/幸运/理智）+ 进阶（攻速/移速/韧性/治疗效率/吸血）+ 战斗（元能上限/部署消耗/攻击视野/迷雾视野）

        private UnitConfig.UnitData _raw;

        private void Awake()
        {
            if (关闭按钮 != null)
                关闭按钮.onClick.AddListener(Close);
        }

        public void Open(UnitConfig.UnitData raw)
        {
            _raw = raw;
            RefreshRows();
            if (!gameObject.activeSelf)
                gameObject.SetActive(true);
            if (滚动区 != null)
                滚动区.verticalNormalizedPosition = 1f;
        }

        public void Close()
        {
            gameObject.SetActive(false);
        }

        private void RefreshRows()
        {
            if (_raw == null || 行列表 == null || 行列表.Length < 15) return;

            行列表[0].SetValue($"{_raw.GetEffectiveHP():N0}");
            行列表[1].SetValue($"{_raw.GetEffectiveAttack():N0}");
            行列表[2].SetValue($"{_raw.GetEffectiveDefense():N0}");
            行列表[3].SetValue($"{_raw.GetEffectiveMastery():N0}");
            行列表[4].SetValue($"{_raw.GetEffectiveLuck():N0}");
            行列表[5].SetValue($"{_raw.GetEffectiveSanity():N0}");
            行列表[6].SetValue($"{_raw.GetEffectiveAttackSpeed():N0}");
            行列表[7].SetValue($"{_raw.GetEffectiveMoveSpeed():N0}");
            行列表[8].SetValue($"{_raw.GetEffectiveTenacity():N0}");
            行列表[9].SetValue($"{_raw.GetEffectiveHealEfficiency():N0}%");
            行列表[10].SetValue($"{_raw.GetEffectiveLifeSteal():N0}%");
            行列表[11].SetValue($"{_raw.GetEffectiveEnergy():N0}");
            行列表[12].SetValue($"{_raw.GetEffectiveDeployCost():N0}");
            行列表[13].SetValue($"{_raw.GetEffectiveAttackVision():N0}");
            行列表[14].SetValue($"{_raw.GetEffectiveFogVision():N0}");

            // 生命值行图标 = 单位元素图标；其余行图标为预留槽位
            var config = ElementFactionConfig.Instance;
            for (int i = 0; i < 行列表.Length; i++)
                行列表[i].SetIcon(null);
            if (config != null)
                行列表[0].SetIcon(config.GetElementIconStroke(_raw.selfElement));
        }
    }


}
