using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.Battle;
using GIC.Tool;
namespace GIC.UI
{


    /// <summary>
    /// 标签芯片 — 单个标签的方形/圆角背景 + 文本显示
    /// </summary>
    public class TagChip : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI label;

        private LocalizedString _localizedString;

        /// <summary>
        /// 设置标签内容（支持本地化）
        /// </summary>
        public void SetEntry(TextEntry entry)
        {
            if (entry?.localizedString == null || entry.localizedString.IsEmpty) return;

            _localizedString = entry.localizedString;
            _localizedString.StringChanged += OnStringChanged;
            _localizedString.RefreshString();
        }

        private void OnStringChanged(string value)
        {
            if (label != null) label.text = value;
        }

        void OnDestroy()
        {
            if (_localizedString != null)
                _localizedString.StringChanged -= OnStringChanged;
        }
    }

}


