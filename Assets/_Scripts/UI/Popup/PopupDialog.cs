using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PopupDialog : MonoBehaviour
{
    public TextMeshProUGUI messageText;
    public Button backPanel;
    public CanvasGroup canvasGroup;

    [Header("动画")]
    [SerializeField] private float fadeInDuration = 0.08f;
    [SerializeField] private float displayDuration = 1.5f;
    [SerializeField] private float fadeOutDuration = 0.08f;

    private Coroutine currentCoroutine;

    public void Init(string message)
    {
        messageText.text = message;
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
