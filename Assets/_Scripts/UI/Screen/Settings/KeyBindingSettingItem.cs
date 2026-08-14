using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Localization;
using UnityEngine.UI;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.Battle;
using GIC.Tool;
namespace GIC.UI
{
    /// <summary>
    /// 按键绑定设置项 — 每个实例管理一个 slot（主键或副键）。
    /// 点击后监听下一次按键用于重绑，所有按键均可绑定。
    /// 再次点击同一按钮可取消监听。
    /// </summary>
    public class KeyBindingSettingItem : SettingItem
    {
        [Header("按键绑定组件")]
        [SerializeField] private Button button;
        [SerializeField] private TextCombiner valueText;

        private KeyAction _action;
        private int _slot;
        private bool _isListening;
        // 点击按钮启动监听后跳过一帧，避免捕获到启动点击本身
        private bool _skipFrame;

        // 全部键位缓存 — 避免监听期间每帧 Enum.GetValues 分配新数组
        private static readonly KeyCode[] AllKeyCodes = (KeyCode[])Enum.GetValues(typeof(KeyCode));

        [Autowired] private InputManager _inputManager;

        /// <summary>容器已就绪（Boot 链路），懒获取注入（prefab 实例化由 SettingsScreen Setup 触发）</summary>
        private InputManager IM
        {
            get
            {
                if (_inputManager == null)
                    Wargame.Instance?.Context?.Inject(this);
                return _inputManager;
            }
        }

        /// <summary>
        /// 设置动作和槽位。标签显示为「{动作名} - {主键/副键}」。
        /// </summary>
        public void Setup(KeyAction action, int slot, string actionLabelKey)
        {
            _action = action;
            _slot = slot;

            string slotKey = slot == 0 ? "PrimaryKey" : "SecondaryKey";
            labelText.ClearAllEntries();
            labelText.AddEntry(new LocalizedString("UIText", actionLabelKey));
            labelText.AddStaticEntry(" - ");
            labelText.AddEntry(new LocalizedString("UIText", slotKey));

            button.onClick.AddListener(OnButtonClicked);

            var im = IM;
            if (im != null)
                im.OnRebindCancelled += OnRebindCancelled;
        }

        public override void Initialize()
        {
            UpdateDisplayText();
        }

        private void OnButtonClicked()
        {
            if (_isListening)
            {
                CancelRebind();
                return;
            }

            _isListening = true;
            _skipFrame = true;
            IM?.BeginRebind();
            valueText.SetSingleEntry(new LocalizedString("UIText", "PressKey"));

            // 取消按钮选中状态，防止 Space/Enter 被 EventSystem 当作 Submit 触发 onClick
            EventSystem.current?.SetSelectedGameObject(null);
        }

        private void Update()
        {
            if (!_isListening) return;

            // 跳过启动点击所在帧
            if (_skipFrame)
            {
                _skipFrame = false;
                return;
            }

            // 前置门：无任何按键按下时跳过全键位轮询
            if (!Input.anyKeyDown) return;

            foreach (var key in AllKeyCodes)
            {
                if (key == KeyCode.None) continue;
                if (Input.GetKeyDown(key))
                {
                    ConfirmRebind(key);
                    return;
                }
            }
        }

        private void OnRebindCancelled()
        {
            if (!_isListening) return;
            _isListening = false;
            UpdateDisplayText();
        }

        private void ConfirmRebind(KeyCode key)
        {
            _isListening = false;
            var im = IM;
            if (im != null)
            {
                var conflict = im.FindKeyConflict(key, _action);
                if (conflict.HasValue)
                {
                    GameScene.Instance?.ShowToast(new LocalizedString("UIText", "KeyConflict"));
                    im.EndRebind();
                    UpdateDisplayText();
                    return;
                }

                im.RebindKey(_action, _slot, key);
                im.EndRebind();
            }
            UpdateDisplayText();
        }

        private void CancelRebind()
        {
            _isListening = false;
            IM?.EndRebind();
            UpdateDisplayText();
        }

        private void UpdateDisplayText()
        {
            var im = IM;
            KeyCode key = im != null ? im.GetKey(_action, _slot) : KeyCode.None;

            if (key == KeyCode.None)
            {
                valueText.SetSingleEntry("—");
                return;
            }

            string tableKey = GetKeyNameTableKey(key);
            if (tableKey != null)
                valueText.SetSingleEntry(new LocalizedString("UIText", tableKey));
            else
                valueText.SetSingleEntry(key.ToString()); // 字母/F1/Alpha1 等枚举名本身通用
        }

        /// <summary>
        /// 特殊命名的键 → UIText 表 key（本地化显示）；
        /// 返回 null 表示直接用枚举名显示。
        /// </summary>
        private static string GetKeyNameTableKey(KeyCode key)
        {
            switch (key)
            {
                case KeyCode.Mouse0: return "KeyName_Mouse0";
                case KeyCode.Mouse1: return "KeyName_Mouse1";
                case KeyCode.Mouse2: return "KeyName_Mouse2";
                case KeyCode.Space: return "KeyName_Space";
                case KeyCode.Escape: return "KeyName_Escape";
                case KeyCode.Return: return "KeyName_Return";
                case KeyCode.Tab: return "KeyName_Tab";
                case KeyCode.Backspace: return "KeyName_Backspace";
                case KeyCode.Delete: return "KeyName_Delete";
                case KeyCode.LeftArrow: return "KeyName_LeftArrow";
                case KeyCode.RightArrow: return "KeyName_RightArrow";
                case KeyCode.UpArrow: return "KeyName_UpArrow";
                case KeyCode.DownArrow: return "KeyName_DownArrow";
                case KeyCode.LeftShift: return "KeyName_LeftShift";
                case KeyCode.RightShift: return "KeyName_RightShift";
                case KeyCode.LeftControl: return "KeyName_LeftControl";
                case KeyCode.RightControl: return "KeyName_RightControl";
                case KeyCode.LeftAlt: return "KeyName_LeftAlt";
                case KeyCode.RightAlt: return "KeyName_RightAlt";
                default: return null;
            }
        }

        public override void ApplyValue() { }

        public override void ResetToDefault()
        {
            IM?.ResetAction(_action);
            UpdateDisplayText();
        }

        private void OnDestroy()
        {
            var im = _inputManager;
            if (im != null)
            {
                im.OnRebindCancelled -= OnRebindCancelled;
                if (_isListening)
                    im.EndRebind();
            }
        }
    }
}
