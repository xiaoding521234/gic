using UnityEngine;
using GIC.UI;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.Battle;
namespace GIC.Tool
{


    public static class GameObjectExtensions
    {
        /// <summary>
        /// 重新激活GameObject
        /// </summary>
        public static void Reactivate(this GameObject obj)
        {
            if (obj == null) return;
            obj.SetActive(false);
            obj.SetActive(true);
        }

        /// <summary>
        /// 重新激活组件所在的GameObject
        /// </summary>
        public static void Reactivate(this Component component)
        {
            if (component == null) return;
            component.gameObject.Reactivate();
        }
    }
}


