using UnityEngine;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// 元素组件 - 管理单位的元素属性
    /// </summary>
    public class UnitElement : MonoBehaviour, IUnitComponent
    {
        private Unit _owner;

        [Header("自身元素")]
        [SerializeField] private ElementType _selfElement = ElementType.Physical;

        [Header("染色元素")]
        [SerializeField] private ElementType _dyedElement = ElementType.Physical;

        [Header("染色层数（docs/06 §6.2 多层附着，决策七十四铺路：默认1；0=无附着）")]
        [SerializeField] private int _dyedLayers;

        // ==================== 属性 ====================
        // 修改器系统已随死路径删除（2026-09-29 批3⑤裁决：ElementModifier 全库零构造点零消费，
        // 活路径=SetSelfElement/Dye 直写 base——此前 getter 经修改器遍历恒空回退 base，删后语义等价）
        public ElementType SelfElement => _selfElement;
        public ElementType DyedElement => _dyedElement;

        /// <summary>染色层数（附着层数；Dyed=Physical 时无意义恒 0——层级随 Dye/ClearDye 同步维护）</summary>
        public int DyedLayers => _dyedLayers;

        // ==================== 初始化 ====================
        public void Init(Unit owner)
        {
            _owner = owner;
            _selfElement = owner.RawData.selfElement;
            // 登场无附着（2026-09-25 拍板 4A：对齐原神+docs/06——附着只来自元素伤害命中；
            // 首版 Dyed=SelfElement 会让元素角色登场常驻附着、第一击即触发反应）
            _dyedElement = ElementType.Physical;
            _dyedLayers = 0;
        }

        // ==================== 最终值计算 ====================

        // ==================== 便利方法 ====================


        /// <summary>
        /// 染色（默认 1 层——docs/06 §6.2「大部分附着为1层」）
        /// </summary>
        public void Dye(ElementType element)
        {
            Dye(element, 1);
        }

        /// <summary>
        /// 染色（带层数）：多层附着入口（docs/06 §6.3 多余的元素仍然附着——反应消耗后剩余层落此）；
        /// element=Physical 视为清附着（层数归 0）
        /// </summary>
        public void Dye(ElementType element, int layers)
        {
            _dyedElement = element;
            _dyedLayers = element == ElementType.Physical ? 0 : Mathf.Max(0, layers);
        }

        /// <summary>
        /// 清除染色
        /// </summary>
        public void ClearDye()
        {
            _dyedElement = ElementType.Physical;
            _dyedLayers = 0;
        }

        // ==================== 设置方法 ====================
        public void SetSelfElement(ElementType value) => _selfElement = value;
        public void SetDyedElement(ElementType value) => _dyedElement = value;
    }
}


