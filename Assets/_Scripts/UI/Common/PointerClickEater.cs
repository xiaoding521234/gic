using UnityEngine;
using UnityEngine.EventSystems;
namespace GIC.UI
{


    /// <summary>
    /// 点击挡板 — IPointerClickHandler 空体：挂到某节点后，命中其子级的点击在该节点被消费，
    /// 不再向父级冒泡。用于「点数据块之外关闭」类场景：内容区（ScrollView）挂本组件吃掉区内点击，
    /// 遮罩根上的关闭 Button 只接住块外点击（UGUI 点击沿 transform 向上冒泡到首个处理器——
    /// 2026-10-07 大面板实测：根 Button 会接住行内点击，勿按"射线被行拦住=不冒泡"想当然）。
    /// </summary>
    public class PointerClickEater : MonoBehaviour, IPointerClickHandler
    {
        public void OnPointerClick(PointerEventData eventData) { }
    }


}
