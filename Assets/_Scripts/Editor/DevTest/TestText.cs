using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.UI;
using GIC.UI;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.Battle;
using GIC.Tool;
namespace GIC.Editor
{


    public class TestText : MonoBehaviour
    {
        public TextCombiner textCombiner;

        void Awake()
        {
            // 自动从当前物体查找 TextCombiner 组件
            if (textCombiner == null)
            {
                textCombiner = GetComponent<TextCombiner>();
                
                if (textCombiner == null)
                {
                    GICLog.Warn($"TestText: 在 {gameObject.name} 上未找到 TextCombiner 组件", this);
                }
            }
        }

        void Start()
        {
            if (textCombiner != null)
            {
                var entry = RegionName.Mondstadt.GetEntry();
                var entry2 = RegionName.Nodkrai.GetEntry();
                textCombiner.AddEntry(entry);
                textCombiner.AddEntry(entry2);
            }
        }
    }
}


