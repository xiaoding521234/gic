using System.Collections;
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

        private IEnumerator FateLineCoroutine(bool isEncounter = false)
        {
            Color lineColor = isEncounter ? encounterLineColor : fateLineColor;
            var lineObj = new GameObject(isEncounter ? "EncounterLine" : "FateLine", typeof(RectTransform), typeof(Image));
            lineObj.transform.SetParent(fateLineContainer, false);
            lineObj.layer = fateLineContainer.gameObject.layer;

            var lineImg = lineObj.GetComponent<Image>();
            lineImg.color = lineColor;
            lineImg.raycastTarget = false;

            if (_fateLineShader == null)
                _fateLineShader = Shader.Find("UI/FateLine");
            if (_fateLineShader != null)
                lineImg.material = new Material(_fateLineShader);

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

            if (lineImg.material != null) Destroy(lineImg.material);
            Destroy(lineObj);
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
