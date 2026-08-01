using UnityEngine;
using UnityEngine.UI;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.Battle;
using GIC.Tool;
namespace GIC.UI
{


    public abstract class SettingItem : MonoBehaviour
    {
        [Header("通用UI组件")]
        [SerializeField] protected Image backgroundImage;
        [SerializeField] protected TextCombiner labelText;
        
        public abstract void Initialize();
        public abstract void ApplyValue();
        public abstract void ResetToDefault();
    }
}

