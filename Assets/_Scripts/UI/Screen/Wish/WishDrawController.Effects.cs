using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using GIC.Framework;
using GIC.Data;
using GIC.Tool;

namespace GIC.UI
{
    /// <summary>
    /// 视觉特效 — 光柱、命运之线、边缘泛光、倒计时
    /// </summary>
    public partial class WishDrawController
    {
        #region edgeBloom

        /// <summary>
        /// 创建屏幕边缘泛光 Image（drawRoot 的子物体，随 drawRoot 显隐）
        /// </summary>
        private void CreateEdgeGlow()
        {
            if (drawRoot == null) return;

            var glowObj = new GameObject("ScreenEdgeGlow", typeof(RectTransform), typeof(Image));
            glowObj.transform.SetParent(drawRoot.transform, false);
            glowObj.layer = drawRoot.layer;

            var rect = glowObj.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            _edgeGlow = glowObj.AddComponent<ScreenEdgeGlow>();
        }

        #endregion

        #region pillarBurst

        /// <summary>
        /// 光柱爆发特效——在卡道中心播放，颜色和强度取决于星级
        /// </summary>
        private IEnumerator GlowBurstCoroutine(int starLevel)
        {
            Color starColor = StarVisualConfig.GetStarColor(starLevel);

            var pillarObj = new GameObject("GlowBurst", typeof(RectTransform));
            pillarObj.layer = cardTrack.gameObject.layer;
            pillarObj.transform.SetParent(cardTrack.parent, false);
            int trackIndex = cardTrack.GetSiblingIndex();
            pillarObj.transform.SetSiblingIndex(trackIndex);

            var pillarRect = pillarObj.GetComponent<RectTransform>();
            pillarRect.anchorMin = new Vector2(0.5f, 0.5f);
            pillarRect.anchorMax = new Vector2(0.5f, 0.5f);
            pillarRect.pivot = new Vector2(0.5f, 0.5f);
            pillarRect.anchoredPosition = Vector2.zero;
            pillarRect.sizeDelta = Vector2.zero;

            var pillar = pillarObj.AddComponent<LightPillarEffect>();
            pillar.Play(starColor);

            yield return Wait.Seconds(0.5f);

            pillar.Stop();

            yield return Wait.Seconds(0.6f);
            Destroy(pillarObj);
        }

        #endregion

        #region fateLine

        // 材质实例缓存（每次射击复用，协程中断时线体由 CleanupFateLines 兜底清理）
        private Material _fateLineMaterial;
        private readonly List<GameObject> _activeFateLines = new();

        private IEnumerator FateLineCoroutine(WishLineType lineType = WishLineType.Normal)
        {
            // 线色随本发消耗的命运之缘变化：命运之线（白）/相遇之线（金）/纠缠之线（粉）
            Color lineColor = lineType switch
            {
                WishLineType.Encounter => encounterLineColor,
                WishLineType.Intertwined => intertwinedLineColor,
                _ => fateLineColor,
            };
            string lineName = lineType switch
            {
                WishLineType.Encounter => "EncounterLine",
                WishLineType.Intertwined => "IntertwinedLine",
                _ => "FateLine",
            };
            var lineObj = new GameObject(lineName, typeof(RectTransform), typeof(Image));
            lineObj.transform.SetParent(fateLineContainer, false);
            lineObj.layer = fateLineContainer.gameObject.layer;
            _activeFateLines.Add(lineObj);

            var lineImg = lineObj.GetComponent<Image>();
            lineImg.color = lineColor;
            lineImg.raycastTarget = false;

            if (_fateLineShader == null)
                _fateLineShader = Shader.Find("UI/FateLine");
            if (_fateLineMaterial == null && _fateLineShader != null)
                _fateLineMaterial = new Material(_fateLineShader);
            if (_fateLineMaterial != null)
                lineImg.material = _fateLineMaterial;

            var lineRect = lineObj.GetComponent<RectTransform>();

            float trackWidth = cardTrack.rect.width;
            lineRect.sizeDelta = new Vector2(trackWidth * 0.6f, fateLineThickness);
            lineRect.anchoredPosition = Vector2.zero;

            Vector2 startPos = new Vector2(trackWidth * 0.5f + lineRect.rect.width * 0.5f, 0f);
            Vector2 endPos = new Vector2(-trackWidth * 0.5f - lineRect.rect.width * 0.5f, 0f);
            lineRect.anchoredPosition = startPos;

            float duration = 0.2f;
            float elapsed = 0f;
            bool hitPlayed = false;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                lineRect.anchoredPosition = Vector2.Lerp(startPos, endPos, t);

                if (!hitPlayed && t >= 0.5f)
                {
                    hitPlayed = true;
                    if (cardHitSFX != null)
                        AudioManager.Instance?.PlaySFX(cardHitSFX, sfxVolume);
                }

                yield return null;
            }

            _activeFateLines.Remove(lineObj);
            Destroy(lineObj);
        }

        /// <summary>清理残留线体（协程被打断时兜底；正常结束由协程自身移除后销毁）</summary>
        private void CleanupFateLines()
        {
            foreach (var line in _activeFateLines)
            {
                if (line != null) Destroy(line);
            }
            _activeFateLines.Clear();
        }

        #endregion

        #region 倒计时

        private void UpdateCountdownBar(float ratio)
        {
            if (countdownBarFill == null) return;
            ratio = Mathf.Clamp01(ratio);
            countdownBarFill.localScale = new Vector3(ratio, 1f, 1f);
        }

        #endregion
    }
}
