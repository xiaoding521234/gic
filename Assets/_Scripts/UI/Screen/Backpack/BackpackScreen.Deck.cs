// ==================== BackpackScreen.Deck.cs ====================
using System.Collections;
using LocalEvents;
using UnityEngine;

public partial class BackpackScreen
{
    private const int MAX_DECK_SIZE = 8;

    // 编辑面板动画
    private RectTransform editDetailsPanelRect;
    private Vector2 editDetailsPanelTargetPos;
    private Coroutine editPanelAnimCoroutine;

    private void SwitchDeck(int newDeckId)
    {
        if (currentDeckId == newDeckId) return;
        if (newDeckId < 0 || newDeckId >= cardManager.decks.Length) return;

        currentDeckId = newDeckId;
        saveManager.CurrentSave.currentDeck = newDeckId;
        saveManager.SaveGame();
        RefreshCardList();
    }

    private void OnToggleEditMode()
    {
        if (isEditMode)
            ExitEditMode();
        else
            EnterEditMode();
    }

    private void EnterEditMode()
    {
        isEditMode = true;

        if (returnButton != null)
            returnButton.onClick.AddListener(OnToggleEditMode);

        UpdateDeckCountText();

        if (editDetailsPanel != null)
        {
            editDetailsPanel.SetActive(true);
            editDetailsPanelRect.anchoredPosition = editDetailsPanelTargetPos + Vector2.down * panelSlideOffset;
            if (editPanelAnimCoroutine != null) StopCoroutine(editPanelAnimCoroutine);
            editPanelAnimCoroutine = StartCoroutine(SlideEditPanel(true));
        }

        foreach (var card in spawnedCards)
        {
            if (card != null) card.EnterEditMode();
        }
    }

    private void ExitEditMode()
    {
        isEditMode = false;

        if (returnButton != null)
            returnButton.onClick.RemoveListener(OnToggleEditMode);

        if (editDetailsPanel != null)
        {
            if (editPanelAnimCoroutine != null) StopCoroutine(editPanelAnimCoroutine);
            editPanelAnimCoroutine = StartCoroutine(SlideEditPanel(false));
        }

        foreach (var card in spawnedCards)
        {
            if (card != null) card.ExitEditDeck();
        }
    }

    private IEnumerator SlideEditPanel(bool slideIn)
    {
        if (editDetailsPanelRect == null) yield break;

        float elapsed = 0f;
        Vector2 startPos = slideIn
            ? editDetailsPanelTargetPos + Vector2.down * panelSlideOffset
            : editDetailsPanelTargetPos;
        Vector2 endPos = slideIn
            ? editDetailsPanelTargetPos
            : editDetailsPanelTargetPos + Vector2.down * panelSlideOffset;

        while (elapsed < panelSlideDuration)
        {
            elapsed += Time.deltaTime;
            float t = slideCurve.Evaluate(elapsed / panelSlideDuration);
            editDetailsPanelRect.anchoredPosition = Vector2.Lerp(startPos, endPos, t);
            yield return null;
        }

        editDetailsPanelRect.anchoredPosition = endPos;

        if (!slideIn)
            editDetailsPanel.SetActive(false);
    }

    private void UpdateDeckCountText()
    {
        if (countText != null)
        {
            int count = GetCurrentDeckCount();
            countText.text = $"{count}/{MAX_DECK_SIZE}";
        }
    }

    private void OnEditCardClicked(SaveCardData cardData, bool isInDeck)
    {
        if (cardData == null) return;

        Card card = FindCardByData(cardData);
        if (card != null && card.overlay != null && card.overlay.gameObject.activeSelf)
            return;

        if (isInDeck)
        {
            cardData.RemoveFromDeck(currentDeckId);
            saveManager.SaveGame();
            cardManager.RebuildDeck(currentDeckId);
            RefreshCurrentDeckCache();
            UpdateCardDeckVisual(cardData, false);
        }
        else
        {
            if (GetCurrentDeckCount() < MAX_DECK_SIZE)
            {
                cardData.AddToDeck(currentDeckId);
                saveManager.SaveGame();
                cardManager.RebuildDeck(currentDeckId);
                RefreshCurrentDeckCache();
                UpdateCardDeckVisual(cardData, true);
            }
            else
            {
                GameScene.Instance.ShowLocalizedPopup("Deck_Full");
            }
        }

        UpdateDeckCountText();
    }

    private Card FindCardByData(SaveCardData cardData)
    {
        foreach (var card in spawnedCards)
        {
            if (card != null && card.saveCardData == cardData)
                return card;
        }
        return null;
    }

    private void UpdateCardDeckVisual(SaveCardData cardData, bool inDeck)
    {
        Card card = FindCardByData(cardData);
        if (card != null)
            card.onDeck?.gameObject.SetActive(inDeck);
    }

    private int GetCurrentDeckCount()
    {
        if (currentDeckId >= 0 && currentDeckId < cardManager.decks.Length)
            return cardManager.decks[currentDeckId].Count;
        return 0;
    }
}  