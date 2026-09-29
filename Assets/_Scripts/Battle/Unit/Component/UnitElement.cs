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

        // ==================== 属性 ====================
        // 修改器系统已随死路径删除（2026-09-29 批3⑤裁决：ElementModifier 全库零构造点零消费，
        // 活路径=SetSelfElement/Dye 直写 base——此前 getter 经修改器遍历恒空回退 base，删后语义等价）
        public ElementType SelfElement => _selfElement;
        public ElementType DyedElement => _dyedElement;

        // ==================== 初始化 ====================
        public void Init(Unit owner)
        {
            _owner = owner;
            _selfElement = owner.RawData.selfElement;
            // 登场无附着（2026-09-25 拍板 4A：对齐原神+docs/06——附着只来自元素伤害命中；
            // 首版 Dyed=SelfElement 会让元素角色登场常驻附着、第一击即触发反应）
            _dyedElement = ElementType.Physical;
        }

        // ==================== 最终值计算 ====================

        // ==================== 便利方法 ====================


        /// <summary>
        /// 染色
        /// </summary>
        public void Dye(ElementType element)
        {
            _dyedElement = element;
        }

        /// <summary>
        /// 清除染色
        /// </summary>
        public void ClearDye()
        {
            _dyedElement = ElementType.Physical;
        }

        // ==================== 设置方法 ====================
        public void SetSelfElement(ElementType value) => _selfElement = value;
        public void SetDyedElement(ElementType value) => _dyedElement = value;
    }
}


