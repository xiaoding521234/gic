// ==================== SettingsScreen.Navigation.cs（左侧导航面板切换 + 选中高亮） ====================
using System.Collections.Generic;
using UnityEngine;
using System.Collections;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.Battle;
using GIC.Tool;
namespace GIC.UI
{


    public partial class SettingsScreen
    {
        private static readonly Color SELECTED_COLOR = "FFD780".FromHex();
        private static readonly Color UNSELECTED_COLOR = "FFFFFF".FromHex();

        private int currentPanelIndex = -1;
        private Coroutine moveCoroutine;
        private const float MOVE_DURATION = 0.2f;

        private IEnumerator InitSelectPosition()
        {
            yield return null;

            if (select != null && navRects.Count > 0 && navRects[0] != null)
            {
                select.position = navRects[0].position;
                select.sizeDelta = navRects[0].sizeDelta;
            }

            currentPanelIndex = 0;
            for (int i = 0; i < navTexts.Count; i++)
            {
                if (navTexts[i] != null && navTexts[i].textComponent != null)
                {
                    navTexts[i].textComponent.color = (i == 0) ? SELECTED_COLOR : UNSELECTED_COLOR;
                }
            }
            for (int i = 0; i < settingPanels.Count; i++)
            {
                if (settingPanels[i] != null)
                {
                    settingPanels[i].SetActive(i == 0);
                }
            }
        }

        private void SwitchPanel(int index)
        {
            if (currentPanelIndex == index) return;

            for (int i = 0; i < navTexts.Count; i++)
            {
                if (navTexts[i] != null && navTexts[i].textComponent != null)
                {
                    navTexts[i].textComponent.color = (i == index) ? SELECTED_COLOR : UNSELECTED_COLOR;
                }
            }

            MoveSelectTo(index);

            for (int i = 0; i < settingPanels.Count; i++)
            {
                if (settingPanels[i] != null)
                {
                    settingPanels[i].SetActive(i == index);
                }
            }

            currentPanelIndex = index;
        }

        private void MoveSelectTo(int index)
        {
            if (select == null || index < 0 || index >= navRects.Count) return;
            if (navRects[index] == null) return;

            if (moveCoroutine != null)
            {
                StopCoroutine(moveCoroutine);
            }
            moveCoroutine = StartCoroutine(SmoothMoveSelect(navRects[index]));
        }

        private IEnumerator SmoothMoveSelect(RectTransform target)
        {
            Vector2 startPos = select.position;
            Vector2 targetPos = target.position;
            Vector2 startSize = select.sizeDelta;
            Vector2 targetSize = target.sizeDelta;

            float elapsed = 0f;
            while (elapsed < MOVE_DURATION)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / MOVE_DURATION;
                t = 1f - Mathf.Pow(1f - t, 3f);

                select.position = Vector2.Lerp(startPos, targetPos, t);
                select.sizeDelta = Vector2.Lerp(startSize, targetSize, t);
                yield return null;
            }

            select.position = targetPos;
            select.sizeDelta = targetSize;
        }
    }
}
