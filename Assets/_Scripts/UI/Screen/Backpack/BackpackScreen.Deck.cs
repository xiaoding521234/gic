// ==================== BackpackScreen.Deck.cs ====================
using System.Collections;
using System.Collections.Generic;
using GIC.Data.Event;
using UnityEngine;
using UnityEngine.UI;
using GIC.Framework;
using GIC.Battle;
using GIC.Data;
using GIC.Tool;
namespace GIC.UI
{


    public partial class BackpackScreen
    {
        [Header("编辑面板动画")]
        [SerializeField] private float 编辑面板滑入偏移 = 100f;
        [SerializeField] private float 编辑面板滑动时长 = 0.2f;
        [SerializeField]
        private AnimationCurve 编辑面板滑动曲线 = new AnimationCurve(
            new Keyframe(0, 0, 2f, 2f),
            new Keyframe(1, 1, 0f, 0f)
        );

        // 编辑面板动画
        private RectTransform editDetailsPanelRect;
        private Vector2 editDetailsPanelTargetPos;
        private Coroutine editPanelAnimCoroutine;
        private TextCombiner _deckBarNumberCombiner;
        private TextCombiner _deckBarNameCombiner;

        /// <summary>当前卡组 Id（卡组管理面板删除卡组后的联动判断用）</summary>
        public int CurrentDeckId => currentDeckId;

        /// <summary>切换当前卡组（卡组管理面板点击行时调用）</summary>
        public void SwitchDeck(int newDeckId)
        {
            if (currentDeckId == newDeckId) return;
            if (newDeckId < 0 || newDeckId >= cardManager.decks.Length) return;

            currentDeckId = newDeckId;
            // 统一变更入口（2026-09-05 Modify 迁移）：变更+自动标脏一步完成
            saveManager.Modify(s => s.progress.currentDeck = newDeckId);
            RefreshCardList();
            UpdateDeckBar();

            if (isEditMode)
                RefreshDeckPanel();
        }

        /// <summary>打开卡组管理面板（长条卡组按钮入口）</summary>
        public void OpenDeckPanel()
        {
            if (deckSwitchPanel != null)
                deckSwitchPanel.Open();
        }

        /// <summary>刷新长条卡组按钮的编号与名称（当前卡组变化/改名/排序后调用）</summary>
        public void UpdateDeckBar()
        {
            if (deckBarNumberText == null || deckBarNameText == null) return;

            var order = saveManager.CurrentSave.progress.deckOrder;
            int pos = order.IndexOf(currentDeckId);
            int number = (pos < 0 ? currentDeckId : pos) + 1;

            _deckBarNumberCombiner ??= deckBarNumberText.GetComponent<TextCombiner>() ?? deckBarNumberText.gameObject.AddComponent<TextCombiner>();
            _deckBarNameCombiner ??= deckBarNameText.GetComponent<TextCombiner>() ?? deckBarNameText.gameObject.AddComponent<TextCombiner>();

            _deckBarNumberCombiner.SetSingleEntry(number.ToString());

            string customName = cardManager.GetDeckName(currentDeckId);
            if (!string.IsNullOrEmpty(customName))
                _deckBarNameCombiner.SetSingleEntry(customName);
            else
                _deckBarNameCombiner.SetSingleEntry(DeckSwitchPanel.DefaultDeckName(number));
        }

        /// <summary>卡组内容被外部操作整体替换（面板粘贴/导入密语）后的联动刷新</summary>
        public void OnDeckContentChanged(int deckId)
        {
            if (deckId == currentDeckId)
            {
                RefreshCurrentDeckCache();
                UpdateDeckCountText();
                if (isEditMode)
                    RefreshDeckPanel();
                RefreshCardList();
            }
            UpdateDeckBar();
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
                editDetailsPanelRect.anchoredPosition = editDetailsPanelTargetPos + Vector2.down * 编辑面板滑入偏移;
                if (editPanelAnimCoroutine != null) StopCoroutine(editPanelAnimCoroutine);
                editPanelAnimCoroutine = StartCoroutine(SlideEditPanel(true));
            }

            foreach (var card in spawnedCards)
            {
                if (card != null) card.EnterEditMode();
            }

            RefreshDeckPanel();
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

            ClearDeckPanel();
        }

        private IEnumerator SlideEditPanel(bool slideIn)
        {
            if (editDetailsPanelRect == null) yield break;

            float elapsed = 0f;
            Vector2 startPos = slideIn
                ? editDetailsPanelTargetPos + Vector2.down * 编辑面板滑入偏移
                : editDetailsPanelTargetPos;
            Vector2 endPos = slideIn
                ? editDetailsPanelTargetPos
                : editDetailsPanelTargetPos + Vector2.down * 编辑面板滑入偏移;

            while (elapsed < 编辑面板滑动时长)
            {
                elapsed += Time.deltaTime;
                float t = 编辑面板滑动曲线.Evaluate(elapsed / 编辑面板滑动时长);
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
                countText.text = $"{count}/{CardManager.MaxDeckSize}";
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
                saveManager.Modify(_ => cardData.RemoveFromDeck(currentDeckId));
                cardManager.RebuildDeck(currentDeckId);
                RefreshCurrentDeckCache();
                UpdateCardDeckVisual(cardData, false);

                if (card != null)
                    card.PlayLightBandReverse();
            }
            else
            {
                if (GetCurrentDeckCount() < CardManager.MaxDeckSize)
                {
                    saveManager.Modify(_ => cardData.AddToDeck(currentDeckId));
                    cardManager.RebuildDeck(currentDeckId);
                    RefreshCurrentDeckCache();
                    UpdateCardDeckVisual(cardData, true);
                    PlayAddToDeckVoice(cardData);

                    if (card != null)
                        card.PlayLightBand();
                }
                else
                {
                    PopupManager.Instance.ShowToast(new UnityEngine.Localization.LocalizedString(TableName.PopupText.ToString(), "Deck_Full"));
                }
            }

            UpdateDeckCountText();
            RefreshDeckPanel();
        }

        private void PlayAddToDeckVoice(SaveCardData cardData)
        {
            if (cardData.id.cardType != Data.CardType.Unit) return;

            UnitName unitName = cardData.id.AsUnitName();
            var unitData = unitConfig.GetUnitData(unitName);
            if (unitData?.voices?.onGoWar == null || unitData.voices.onGoWar.Count == 0) return;

            var clip = unitData.voices.onGoWar.GetRandomClip();
            if (clip != null)
                AudioManager.Instance?.PlayVoiceExclusive(clip);
        }

        private void RefreshDeckPanel()
        {
            if (_deckCardPool == null || deckContent == null) return;

            ClearDeckPanel();

            if (currentDeckId < 0 || currentDeckId >= cardManager.decks.Length) return;

            // 用 deck.Cards（已排序）而非 currentDeckCards（HashSet 无序）
            foreach (var cardData in cardManager.decks[currentDeckId].Cards)
            {
                Card card = _deckCardPool.Get(cardData, cardDetailView);
                if (card == null) continue;

                card.isDeckPanelMode = true;
                card.SetViewType(ViewType.Display);
                card.transform.localScale = Vector3.one * deckCardScale;

                if (card.toggle != null) card.toggle.group = null;
                deckSpawnedCards.Add(card);
            }
        }

        private void ClearDeckPanel()
        {
            foreach (var c in deckSpawnedCards)
            {
                if (c != null)
                {
                    c.transform.localScale = Vector3.one;
                    c.isDeckPanelMode = false;
                    c.ExitEditDeck(); // 重置 overlay 和 isEditMode
                    c.transform.localScale = Vector3.one; // 重置缩放
                    _deckCardPool.Release(c);
                }
            }
            deckSpawnedCards.Clear();
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

}


