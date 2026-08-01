using System.Collections;
using GIC.Data.Event;
using UnityEngine;
using UnityEngine.UI;
using GIC.Framework;
using GIC.Data;
using GIC.Battle;
using GIC.Tool;
namespace GIC.UI
{


    public class ItemCategoryView : MonoBehaviour
    {
        public Toggle toggle;
        public Image selectIcon;
        public BackpackTab tab = BackpackTab.Character;

        [Header("动画参数")]
        [SerializeField] private float popDuration = 0.2f;
        [SerializeField] private Color selectedColor = Color.white;
        [SerializeField] private Color unselectedColor = new(0.5f, 0.5f, 0.5f, 1f);

        private CategorySyncHandler _syncHandler;
        private Coroutine _popCoroutine;
        private bool _isSelected;

        public void Awake()
        {
            toggle.onValueChanged.AddListener(OnToggleValueChanged);

            _syncHandler = new CategorySyncHandler(this);
            EventBusHub.Instance.Subscribe(_syncHandler);

            if (selectIcon != null)
            {
                selectIcon.transform.localScale = Vector3.zero;
                selectIcon.color = unselectedColor;
                selectIcon.gameObject.SetActive(true);
            }
        }

        public void OnDestroy()
        {
            toggle.onValueChanged.RemoveListener(OnToggleValueChanged);

            if (EventBusHub.Instance != null && _syncHandler != null)
                EventBusHub.Instance.Unsubscribe(_syncHandler);
        }

        private void OnToggleValueChanged(bool isOn)
        {
            PlayPop(isOn);

            if (isOn)
            {
                EventBusHub.Instance.SendImmediate(new OnBackpackCategoryChangedEvent
                {
                    Tab = tab
                });
            }
        }

        private void SetVisual(bool isOn)
        {
            toggle.SetIsOnWithoutNotify(isOn);
            PlayPop(isOn);
        }

        private void PlayPop(bool isOn)
        {
            if (_popCoroutine != null) StopCoroutine(_popCoroutine);
            _popCoroutine = StartCoroutine(PopAnimation(isOn));
        }

        private IEnumerator PopAnimation(bool isOn)
        {
            if (selectIcon == null) yield break;

            float elapsed = 0f;
            Vector3 startScale = selectIcon.transform.localScale;
            Color startColor = selectIcon.color;

            if (isOn)
            {
                while (elapsed < popDuration)
                {
                    elapsed += Time.deltaTime;
                    float t = elapsed / popDuration;

                    float scale;
                    if (t < 0.5f)
                        scale = Mathf.Lerp(0f, 1.1f, t * 2f);
                    else
                        scale = Mathf.Lerp(1.1f, 1f, (t - 0.5f) * 2f);

                    selectIcon.transform.localScale = Vector3.one * scale;
                    selectIcon.color = Color.Lerp(unselectedColor, selectedColor, t);
                    yield return null;
                }
                selectIcon.transform.localScale = Vector3.one;
                selectIcon.color = selectedColor;
            }
            else
            {
                while (elapsed < popDuration)
                {
                    elapsed += Time.deltaTime;
                    float t = elapsed / popDuration;
                    selectIcon.transform.localScale = Vector3.Lerp(startScale, Vector3.zero, t);
                    selectIcon.color = Color.Lerp(startColor, unselectedColor, t);
                    yield return null;
                }
                selectIcon.transform.localScale = Vector3.zero;
                selectIcon.color = unselectedColor;
            }
        }

        private class CategorySyncHandler : IEventHandler<OnBackpackCategorySyncEvent>
        {
            private readonly ItemCategoryView _view;
            public CategorySyncHandler(ItemCategoryView view) => _view = view;

            public bool CanHandle(OnBackpackCategorySyncEvent evt)
                => _view != null;

            public void Handle(OnBackpackCategorySyncEvent evt)
                => _view.SetVisual(_view.tab == evt.Tab);
        }
    }

}


