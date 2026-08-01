using TMPro;
using UnityEngine;
using UnityEngine.UI;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.Battle;
using GIC.Tool;
namespace GIC.UI
{


    public class ParamView : MonoBehaviour
    {
        public TextCombiner paramName;
        public TextCombiner paramValue;

        public void InitWithParam(TextEntry paramNameEntry, string paramValueText)
        {
            paramName.ClearAllEntries();
            paramName.AddEntry(paramNameEntry);
            
            paramValue.ClearAllEntries();
            paramValue.AddStaticEntry(paramValueText);
        }
        
        public void InitWithParam(TextEntry paramNameEntry, TextEntry paramValueEntry)
        {
            if (paramNameEntry == null || paramValueEntry == null)
            {
                Debug.LogWarning("Param-View InitWithParam: paramNameEntry or paramValueEntry is null, skipping");
                return;
            }

            paramName.ClearAllEntries();
            paramName.AddEntry(paramNameEntry);

            paramValue.ClearAllEntries();
            // 将 leadingSeparator（数值部分）作为静态文本先加入，再加入本地化条目（类型名称）
            if (!string.IsNullOrEmpty(paramValueEntry.leadingSeparator))
            {
                paramValue.AddStaticEntry(paramValueEntry.leadingSeparator);
            }
            if (paramValueEntry.localizedString != null && !paramValueEntry.localizedString.IsEmpty)
            {
                paramValue.AddEntry(paramValueEntry.localizedString, "");
            }
        }
        
        public void InitWithParam(string paramNameText, string paramValueText)
        {
            paramName.ClearAllEntries();
            paramName.AddStaticEntry(paramNameText);
            
            paramValue.ClearAllEntries();
            paramValue.AddStaticEntry(paramValueText);
        }
    }
}


