using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Localization;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
namespace GIC.UI
{


    public class PopupManager : MonoBehaviour
    {
        /// <summary>静态访问入口（场景唯一实例，Boot 场景建立）</summary>
        public static PopupManager Instance { get; private set; }

        [InspectorName("模态弹窗预制体")]
        public GameObject popupPrefab;

        [Header("轻提示")]
        [SerializeField] private GameObject toastPrefab;
        [InspectorName("轻提示间距")]
        [SerializeField] private float toastGap = 10f;
        [InspectorName("轻提示位置比例")]
        [SerializeField] private float toastPosRatio = 0.05f;

        private readonly List<PopupDialog> _activeToasts = new();
        private float _toastHeight;

        // 存档写盘失败 → toast（2026-09-05）：SaveManager 只发事件不依赖 UI，本管理器是 toast 设施所以订阅方落在此处
        private SaveFailedToastHandler _saveFailedHandler;

        private void Awake()
        {
            if (Instance == null)
                Instance = this;
            else if (Instance != this)
                Destroy(gameObject);
        }

        private void Start()
        {
            // Start 时机订阅（EventBusHub.Instance 已在 Awake 建立——Start 晚于同场景全部 Awake，订阅不落空）
            _saveFailedHandler = new SaveFailedToastHandler(this);
            EventBusHub.Instance.Subscribe(_saveFailedHandler, this);
        }

        private void OnDestroy()
        {
            EventBusHub.Instance?.UnsubscribeOwner(this);   // owner 登记式退订兜底（gic-eventbus 规范）
            if (Instance == this) Instance = null;
        }

        public void ShowModalPopup(string message)
        {
            if (popupPrefab == null)
            {
                GICLog.Error("PopupManager: popupPrefab 未设置");
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
                GICLog.Error("PopupManager: popupPrefab 上未找到 PopupDialog 组件");
            }
        }

        public void ShowModalPopup(LocalizedString localizedString)
        {
            if (popupPrefab == null)
            {
                GICLog.Error("PopupManager: popupPrefab 未设置");
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
                GICLog.Error("PopupManager: popupPrefab 上未找到 PopupDialog 组件");
            }
        }

        /// <summary>
        /// 显示轻提示（纯文本）— 从顶部滑入，不阻断点击，可堆叠
        /// </summary>
        public void ShowToast(string message)
        {
            // 去重：相同内容的 toast 仍在显示时，刷新而非新建（移出中的不刷新——让它走完，新建替代）
            foreach (var existing in _activeToasts)
            {
                if (existing != null && !existing.IsDismissing && existing.GetToastMessage() == message)
                {
                    existing.RefreshToast();
                    return;
                }
            }

            var dialog = CreateToast();
            if (dialog == null) return;

            Vector2 pos = ComputeToastPosition(_activeToasts.Count, dialog);
            dialog.InitToast(message, pos, OnToastComplete);
            _activeToasts.Add(dialog);
        }

        /// <summary>
        /// 显示轻提示（本地化文本）
        /// </summary>
        public void ShowToast(LocalizedString localizedString)
        {
            string key = localizedString.TableEntryReference.Key;

            foreach (var existing in _activeToasts)
            {
                if (existing != null && !existing.IsDismissing && existing.GetToastMessage() == key)
                {
                    existing.RefreshToast();
                    return;
                }
            }

            var dialog = CreateToast();
            if (dialog == null) return;

            Vector2 pos = ComputeToastPosition(_activeToasts.Count, dialog);
            dialog.InitToast(localizedString, pos, OnToastComplete);
            _activeToasts.Add(dialog);
        }

        private PopupDialog CreateToast()
        {
            // 拆分定案后 toast 专用 prefab 独立配置——缺失即配置错误，不兜底模态 prefab（会静默呈现错误外观）
            if (toastPrefab == null)
            {
                GICLog.Error("PopupManager: toastPrefab 未设置");
                return null;
            }

            var instance = Instantiate(toastPrefab, transform);
            var dialog = instance.GetComponent<PopupDialog>();
            if (dialog == null)
            {
                GICLog.Error("PopupManager: toastPrefab 上未找到 PopupDialog 组件");
                Destroy(instance);
                return null;
            }
            return dialog;
        }

        private Vector2 ComputeToastPosition(int index, PopupDialog dialog)
        {
            float height = GetToastCanvasHeight(dialog);
            float toastH = GetToastHeight();
            // 子节点锚定在屏幕中心 (0.5, 0.5)，Y=0 是中心，正值向上
            // 目标位置：距顶部 toastPosRatio 比例高度（默认 5%）
            float y = height * 0.5f - height * toastPosRatio - index * (toastH + toastGap);
            return new Vector2(0f, y);
        }

        /// <summary>toast 坐标系 = toast 实例自带根 Canvas（与 PopupDialog.SetupToastLayout 同源，
        /// 避免管理器父 Canvas 与 toast Canvas 双源不一致）</summary>
        private float GetToastCanvasHeight(PopupDialog dialog)
        {
            if (dialog == null) return 1080f;
            var canvas = dialog.GetComponent<Canvas>();
            return canvas != null ? canvas.GetComponent<RectTransform>().rect.height : 1080f;
        }

        private float GetToastHeight()
        {
            if (_toastHeight <= 0f && toastPrefab != null)
            {
                // 根节点 rect 高度为 0，实际内容在子节点 Image 上
                for (int i = 0; i < toastPrefab.transform.childCount; i++)
                {
                    var childRect = toastPrefab.transform.GetChild(i).GetComponent<RectTransform>();
                    if (childRect != null && childRect.rect.height > 0f)
                    {
                        _toastHeight = childRect.rect.height;
                        break;
                    }
                }
            }
            if (_toastHeight <= 0f) _toastHeight = 60f;
            return _toastHeight;
        }

        private void OnToastComplete(PopupDialog dialog)
        {
            int index = _activeToasts.IndexOf(dialog);
            if (index < 0) return;

            _activeToasts.RemoveAt(index);

            // 重排剩余提示
            for (int i = index; i < _activeToasts.Count; i++)
            {
                if (_activeToasts[i] != null)
                    _activeToasts[i].SetToastPosition(ComputeToastPosition(i, _activeToasts[i]));
            }
        }

        /// <summary>存档写盘失败事件处理器（gic-eventbus 标准写法：CanHandle 判 activeInHierarchy 防已销毁回调）</summary>
        private class SaveFailedToastHandler : IEventHandler<OnSaveFailedEvent>
        {
            private readonly PopupManager _manager;
            public SaveFailedToastHandler(PopupManager manager) => _manager = manager;
            public bool CanHandle(OnSaveFailedEvent evt) => _manager != null && _manager.gameObject.activeInHierarchy;
            public void Handle(OnSaveFailedEvent evt) =>
                _manager.ShowToast(new LocalizedString(TableName.PopupText.ToString(), "Save_WriteFailed"));
        }
    }


}
