using UnityEngine;
using UnityEngine.EventSystems;

namespace GIC.UI
{
    /// <summary>
    /// 卡组行的拖拽把手（挂在行内把手 GO 上）：
    /// · 把手拖动 = 拖拽排序（转发 DeckRowView → DeckSwitchPanel）；
    ///   事件系统把拖拽链锁定在本对象，ScrollView 不会同时滚动——把手拖与列表滚天然互斥；
    /// · 把手点击被本类消耗（空实现），不会冒泡触发行选中。
    /// </summary>
    public class DeckRowDragHandle : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
    {
        private DeckRowView _row;

        public void Init(DeckRowView row) => _row = row;

        public void OnBeginDrag(PointerEventData eventData)
            => _row?.Panel?.OnRowBeginDrag(_row, eventData.position);

        public void OnDrag(PointerEventData eventData)
            => _row?.Panel?.OnRowDrag(_row, eventData.position);

        public void OnEndDrag(PointerEventData eventData)
            => _row?.Panel?.OnRowEndDrag(_row);

        public void OnPointerClick(PointerEventData eventData)
        {
            // 消耗点击：把手区域不触发行选中
        }
    }
}
