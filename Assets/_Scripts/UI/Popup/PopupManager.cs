using UnityEngine;
using UnityEngine.Localization;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.Battle;
using GIC.Tool;
namespace GIC.UI
{


    public class PopupManager : MonoBehaviour
    {
        public GameObject popupPrefab;


        private void Awake()
        {
            
        }


        public void ShowPopup(string message)
        {
            if (popupPrefab == null)
            {
                Debug.LogError("PopupManager: popupPrefab 未设置");
                return;
            }

            var instance = Instantiate(popupPrefab, transform);
            var dialog = instance.GetComponent<PopupDialog>();
            if (dialog != null)
            {
                dialog.Init(message);
            }
            else
            {
                Debug.LogError("PopupManager: popupPrefab 上未找到 PopupDialog 组件");
            }
        }

        public void ShowPopup(LocalizedString localizedString)
        {
            if (popupPrefab == null)
            {
                Debug.LogError("PopupManager: popupPrefab 未设置");
                return;
            }

            var instance = Instantiate(popupPrefab, transform);
            var dialog = instance.GetComponent<PopupDialog>();
            if (dialog != null)
            {
                dialog.Init(localizedString);
            }
            else
            {
                Debug.LogError("PopupManager: popupPrefab 上未找到 PopupDialog 组件");
            }
        }
    }


}
