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
        public Image selectDisc;   // 选中态公共圆盘层（原神式：盘+glyph 分层染色）
        public Image selectGlyph;  // 选中态 glyph 层（与 NormalIcon 同 sprite，染深色）
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
            EventBusHub.Instance.Subscribe(_syncHandler, this);

            bool layered = selectDisc != null && selectGlyph != null;
            if (!layered && selectIcon != null)
            {
                // 兼容旧单层结构：未拆层时退化为单层动画
                selectIcon.transform.localScale = Vector3.zero;
                selectIcon.color = unselectedColor;
                selectIcon.gameObject.SetActive(true);
            }
            if (selectDisc != null)
            {
                selectDisc.transform.localScale = Vector3.zero;
                selectDisc.gameObject.SetActive(true);
            }
            if (selectGlyph != null)
            {
                selectGlyph.transform.localScale = Vector3.zero;
                selectGlyph.gameObject.SetActive(true);
            }
        }

        public void OnDestroy()
        {
            toggle.onValueChanged.RemoveListener(OnToggleValueChanged);

            EventBusHub.Instance?.UnsubscribeOwner(this);
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
            if (selectDisc != null && selectGlyph != null)
            {
                // 原神式两层：盘(米白) + glyph(深色) 同步 pop
                yield return PopLayers(isOn);
                yield break;
            }
            if (selectIcon != null)
            {
                yield return PopSingle(selectIcon, isOn);
                yield break;
            }
        }

        private IEnumerator PopLayers(bool isOn)
        {
            float elapsed = 0f;
            Vector3 startScaleDisc = selectDisc.transform.localScale;
            Vector3 startScaleGlyph = selectGlyph.transform.localScale;

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

                    selectDisc.transform.localScale = Vector3.one * scale;
                    selectGlyph.transform.localScale = Vector3.one * scale;
                    yield return null;
                }
                selectDisc.transform.localScale = Vector3.one;
                selectGlyph.transform.localScale = Vector3.one;
            }
            else
            {
                while (elapsed < popDuration)
                {
                    elapsed += Time.deltaTime;
                    float t = elapsed / popDuration;
                    float scale = Mathf.Lerp(startScaleDisc.x, 0f, t);
                    selectDisc.transform.localScale = Vector3.one * scale;
                    selectGlyph.transform.localScale = Vector3.one * scale;
                    yield return null;
                }
                selectDisc.transform.localScale = Vector3.zero;
                selectGlyph.transform.localScale = Vector3.zero;
            }
        }

        private IEnumerator PopSingle(Image img, bool isOn)
        {
            float elapsed = 0f;
            Vector3 startScale = img.transform.localScale;
            Color startColor = img.color;

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

                    img.transform.localScale = Vector3.one * scale;
                    img.color = Color.Lerp(unselectedColor, selectedColor, t);
                    yield return null;
                }
                img.transform.localScale = Vector3.one;
                img.color = selectedColor;
            }
            else
            {
                while (elapsed < popDuration)
                {
                    elapsed += Time.deltaTime;
                    float t = elapsed / popDuration;
                    img.transform.localScale = Vector3.Lerp(startScale, Vector3.zero, t);
                    img.color = Color.Lerp(startColor, unselectedColor, t);
                    yield return null;
                }
                img.transform.localScale = Vector3.zero;
                img.color = unselectedColor;
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