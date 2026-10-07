using UnityEngine;
namespace GIC.Data
{


    /// <summary>冻结配置（反应类硬控——无技能语境，本资产即真源；重复冰冻取时间更长者）</summary>
    [CreateAssetMenu(fileName = "Buff_Freeze", menuName = "Game/BuffConfig 冻结")]
    public class FreezeBuffConfig : BuffConfig
    {
        [Header("冰冻持续回合 × 级别")]
        public int 每级持续回合 = 2;
    }
}
