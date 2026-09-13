// ==================== BackpackScreen.Category.cs ====================
using System;
using System.Collections;
using System.Collections.Generic;
using GIC.Data.Event;
using UnityEngine;
using UnityEngine.Localization;
using GIC.Framework;
using GIC.Battle;
using GIC.Data;
using GIC.Tool;
namespace GIC.UI
{


    public partial class BackpackScreen
    {
        private static readonly BackpackTab[] TabOrder =
        {
            BackpackTab.Character,
            BackpackTab.Creation,
            BackpackTab.Building,
            BackpackTab.Equipment,
            BackpackTab.Consumable,
            BackpackTab.Material,
            BackpackTab.Currency,
            BackpackTab.Quest,
        };

        private void OnPreviousCategory()
        {
            int idx = Array.IndexOf(TabOrder, currentTab);
            SetCategory(TabOrder[(idx - 1 + TabOrder.Length) % TabOrder.Length], isInit: false);
        }

        private void OnNextCategory()
        {
            int idx = Array.IndexOf(TabOrder, currentTab);
            SetCategory(TabOrder[(idx + 1) % TabOrder.Length], isInit: false);
        }

        private void CacheTabViews()
        {
            _tabViews.Clear();
            if (tabContainer == null) return;
            for (int i = 0; i < tabContainer.childCount; i++)
            {
                var view = tabContainer.GetChild(i).GetComponent<ItemCategoryView>();
                if (view != null) _tabViews.Add(view);
            }
        }

        private void SetCategory(BackpackTab newTab, bool isInit)
        {
            if (currentTab == newTab && !isInit) return;
            currentTab = newTab;

            if (categoryText != null)
            {
                categoryText.ClearAllEntries();
                categoryText.AddEntry(new LocalizedString(TableName.UIText.ToString(), newTab.ToString()));
            }

            EventBusHub.Instance.SendImmediate(new OnBackpackCategorySyncEvent { Tab = newTab });

            if (!isInit)
                SlideSelectLineTo(newTab);

            if (!isInit) RefreshCardList();
        }

        private void SnapSelectLineTo(BackpackTab tab)
        {
            if (sharedSelectLine == null) return;

            ItemCategoryView target = null;
            foreach (var view in _tabViews)
            {
                if (view != null && view.tab == tab) { target = view; break; }
            }
            if (target == null) return;

            var targetRT = target.GetComponent<RectTransform>();
            sharedSelectLine.position = new Vector2(targetRT.position.x, sharedSelectLine.position.y);
        }

        private void SlideSelectLineTo(BackpackTab tab)
        {
            if (sharedSelectLine == null) return;

            ItemCategoryView target = null;
            foreach (var view in _tabViews)
            {
                if (view != null && view.tab == tab)
                {
                    target = view;
                    break;
                }
            }

            if (target == null) return;

            // 用世界坐标 position（和 SettingsScreen 一样的做法）
            var targetRT = target.GetComponent<RectTransform>();
            if (_lineCoroutine != null) StopCoroutine(_lineCoroutine);
            _lineCoroutine = StartCoroutine(SlideLineCoroutine(targetRT));
        }

        private IEnumerator SlideLineCoroutine(RectTransform targetRT)
        {
            float startX = sharedSelectLine.position.x;
            float targetX = targetRT.position.x;
            float y = sharedSelectLine.position.y; // 固定 Y

            float elapsed = 0f;
            while (elapsed < lineSlideDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / lineSlideDuration;
                t = 1f - Mathf.Pow(1f - t, 3f); // ease out cubic

                float x = Mathf.Lerp(startX, targetX, t);
                sharedSelectLine.position = new Vector2(x, y);
                yield return null;
            }

            sharedSelectLine.position = new Vector2(targetX, y);
        }

        private void OnClose()
        {
            if (isClosing) return;
            if (isEditMode)
            {
                ExitEditMode();
            }
            // 标准关闭模板：防重入 + Closing 锁 + 音乐恢复 + 公共组件退场动画 + GoBack
            CloseScreen(() => 毛玻璃动画器.ExitRoutine());
        }
    }

}



