using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Localization;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.Battle;
using GIC.Tool;
using UnityEngine.Serialization;
namespace GIC.UI
{


    public class PopupManager : MonoBehaviour
    {
        /// <summary>静态访问入口（场景唯一实例，Boot 场景建立）</summary>
        public static PopupManager Instance { get; private set; }

        public GameObject popupPrefab;

        [Header("轻提示")]
        [SerializeField] private GameObject toastPrefab;
        [InspectorName("轻提示间距")]
        [SerializeField] private float toastGap = 10f;
        [InspectorName("轻提示位置比例")]
        [SerializeField] private float toastPosRatio = 0.05f;

        private readonly List<PopupDialog> _activeToasts = new();
        private float _canvasHeight;
        private float _toastHeight;

        private void Awake()
        {
            if (Instance == null)
                Instance = this;
            else if (Instance != this)
                Destroy(gameObject);
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

            Vector2 pos = ComputeToastPosition(_activeToasts.Count);
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

            Vector2 pos = ComputeToastPosition(_activeToasts.Count);
            dialog.InitToast(localizedString, pos, OnToastComplete);
            _activeToasts.Add(dialog);
        }

        private PopupDialog CreateToast()
        {
            var prefab = toastPrefab != null ? toastPrefab : popupPrefab;
            if (prefab == null)
            {
                GICLog.Error("PopupManager: popupPrefab 未设置");
                return null;
            }

            var instance = Instantiate(prefab, transform);
            var dialog = instance.GetComponent<PopupDialog>();
            if (dialog == null)
            {
                GICLog.Error("PopupManager: 预制体上未找到 PopupDialog 组件");
                Destroy(instance);
                return null;
            }
            return dialog;
        }

        private Vector2 ComputeToastPosition(int index)
        {
            float height = GetCanvasHeight();
            float toastH = GetToastHeight();
            // 子节点锚定在屏幕中心 (0.5, 0.5)，Y=0 是中心，正值向上
            // 目标位置：距顶部 20%（从顶部向下 0.2*height）
            float y = height * 0.5f - height * toastPosRatio - index * (toastH + toastGap);
            return new Vector2(0f, y);
        }

        private float GetCanvasHeight()
        {
            if (_canvasHeight <= 0f)
            {
                var canvas = GetComponentInParent<Canvas>();
                _canvasHeight = canvas != null ? canvas.GetComponent<RectTransform>().rect.height : 1080f;
            }
            return _canvasHeight;
        }

        private float GetToastHeight()
        {
            if (_toastHeight <= 0f)
            {
                var prefab = toastPrefab != null ? toastPrefab : popupPrefab;
                // 根节点 rect 高度为 0，实际内容在子节点 Image 上
                for (int i = 0; i < prefab.transform.childCount; i++)
                {
                    var childRect = prefab.transform.GetChild(i).GetComponent<RectTransform>();
                    if (childRect != null && childRect.rect.height > 0f)
                    {
                        _toastHeight = childRect.rect.height;
                        break;
                    }
                }
                if (_toastHeight <= 0f) _toastHeight = 60f;
            }
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
                    _activeToasts[i].SetToastPosition(ComputeToastPosition(i));
            }
        }
    }


}
