using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Serialization;
using UnityEngine.UI;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.Battle;
using GIC.Tool;
namespace GIC.UI
{


    public class PopupDialog : MonoBehaviour
    {
        [FormerlySerializedAs("messageText")]
        [SerializeField] private TextMeshProUGUI messageTextObj;
        public Button backPanel;
        public CanvasGroup canvasGroup;

        [Header("动画")]
        [SerializeField] private float fadeInDuration = 0.08f;
        [SerializeField] private float displayDuration = 1.5f;
        [SerializeField] private float fadeOutDuration = 0.08f;

        private TextCombiner _messageText;
        private Coroutine currentCoroutine;

        private void EnsureTextCombiner()
        {
            if (_messageText == null && messageTextObj != null)
            {
                _messageText = messageTextObj.GetComponent<TextCombiner>();
                if (_messageText == null)
                    _messageText = messageTextObj.gameObject.AddComponent<TextCombiner>();
            }
        }

        public void Init(string message)
        {
            EnsureTextCombiner();
            _messageText.SetSingleEntry(message);
            Show();
        }

        public void Init(LocalizedString localizedString)
        {
            EnsureTextCombiner();
            _messageText.SetSingleEntry(localizedString);
            Show();
        }

        private void Show()
        {
            backPanel.onClick.AddListener(Close);
            gameObject.SetActive(true);
            currentCoroutine = StartCoroutine(ShowCoroutine());
        }

        private IEnumerator ShowCoroutine()
        {
            canvasGroup.alpha = 0f;

            float elapsed = 0f;
            while (elapsed < fadeInDuration)
            {
                elapsed += Time.deltaTime;
                canvasGroup.alpha = elapsed / fadeInDuration;
                yield return null;
            }
            canvasGroup.alpha = 1f;

            yield return new WaitForSeconds(displayDuration);

            elapsed = 0f;
            while (elapsed < fadeOutDuration)
            {
                elapsed += Time.deltaTime;
                canvasGroup.alpha = 1f - (elapsed / fadeOutDuration);
                yield return null;
            }

            Destroy(gameObject);
        }

        private void Close()
        {
            if (currentCoroutine != null)
            {
                StopCoroutine(currentCoroutine);
            }
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            backPanel.onClick.RemoveListener(Close);
        }
    }

}
