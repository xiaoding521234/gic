// ==================== BackpackScreen.Category.cs ====================
using System;
using System.Collections;
using System.Collections.Generic;
using GIC.Data.Event;
using UnityEngine;
using UnityEngine.Localization;
using GIC.Framework;
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

        /// <summary>滑线唯一写入口：X 用世界坐标对齐标签，Y 每帧锚回本地值。
        /// 毛玻璃入场/退场动画会移动父级 TopPanel（内容元素，从上偏移 100）——若钉死世界 Y，
        /// 滑线与动画重叠时线会被留在父级位移态的高度上，本地 Y 永久漂移（池化实例不重建，§39 同族）。</summary>
        private void SetLineWorldX(float worldX)
        {
            float localY = sharedSelectLine.anchoredPosition.y;
            sharedSelectLine.position = new Vector3(worldX, sharedSelectLine.position.y, sharedSelectLine.position.z);
            var ap = sharedSelectLine.anchoredPosition;
            sharedSelectLine.anchoredPosition = new Vector2(ap.x, localY);
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

            SetLineWorldX(target.GetComponent<RectTransform>().position.x);
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

            float elapsed = 0f;
            while (elapsed < lineSlideDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / lineSlideDuration;
                t = 1f - Mathf.Pow(1f - t, 3f); // ease out cubic

                SetLineWorldX(Mathf.Lerp(startX, targetX, t));
                yield return null;
            }

            SetLineWorldX(targetX);
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



