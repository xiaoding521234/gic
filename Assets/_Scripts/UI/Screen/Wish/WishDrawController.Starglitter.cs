using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace GIC.UI
{
    /// <summary>
    /// 星辉雨 + 进度条（纯表现层）
    /// </summary>
    public partial class WishDrawController
    {
        #region 星辉进度条

        /// <summary>
        /// 更新星辉进度条
        /// </summary>
        private void UpdateStarglitterProgressBar()
        {
            if (starglitterProgressFill == null || _wishManager == null) return;

            // 相遇之线射击中 或 有可用相遇之线：填满 + 持续抖动+流彩
            bool encounterActive = _flow != null && _flow.IsEncounterReady;
            if (encounterActive)
            {
                starglitterProgressFill.localScale = new Vector3(1f, 1f, 1f);
                if (_progressBarGlowCoroutine == null)
                    _progressBarGlowCoroutine = StartCoroutine(ProgressBarGlowCoroutine());
                return;
            }

            // 正常进度
            float progress = _wishManager.GetStarglitterProgress();
            starglitterProgressFill.localScale = new Vector3(progress, 1f, 1f);

            if (_progressBarGlowCoroutine != null)
            {
                StopCoroutine(_progressBarGlowCoroutine);
                _progressBarGlowCoroutine = null;
                starglitterProgressFill.anchoredPosition = Vector2.zero;
                starglitterProgressFill.GetComponent<Image>().color = new Color(0.9f, 0.8f, 0.2f, 0.8f);
            }
        }

        private IEnumerator ProgressBarGlowCoroutine()
        {
            Vector2 basePos = starglitterProgressFill.anchoredPosition;
            float elapsed = 0f;
            while (true)
            {
                elapsed += Time.deltaTime;
                float shake = 6f;
                starglitterProgressFill.anchoredPosition = basePos + new Vector2(
                    Random.Range(-shake, shake), Random.Range(-shake, shake));
                float hue = (elapsed * 2f) % 1f;
                starglitterProgressFill.GetComponent<Image>().color = Color.HSVToRGB(hue, 0.85f, 1f);
                yield return null;
            }
        }

        #endregion

        #region 星辉雨

        private bool _isRainActive;
        private int _rainSpawned;
        private int _rainTotal;

        /// <summary>
        /// 请求星辉雨——若已有雨在进行中，追加数量而非启动新协程
        /// （避免多个协程同时遍历 _activeDrops 导致重复更新、速度倍增）
        /// </summary>
        private void RequestStarglitterRain(int amount)
        {
            if (starglitterRainContainer == null || starglitterSprite == null) return;
            _rainTotal += amount;
            if (!_isRainActive)
                StartCoroutine(StarglitterRainCoroutine());
        }

        /// <summary>
        /// 星辉雨动画——单协程批量管理所有下落，分批错落生成
        /// </summary>
        private IEnumerator StarglitterRainCoroutine()
        {
            _isRainActive = true;
            _rainSpawned = 0;

            float parentWidth = ((RectTransform)starglitterRainContainer).rect.width;
            float parentHeight = ((RectTransform)starglitterRainContainer).rect.height;
            float startY = parentHeight * 0.5f + 50f;
            float fallHeight = startY + parentHeight * 0.5f + 100f;

            float nextSpawnTime = 0f;

            while (_rainSpawned < _rainTotal || _activeDrops.Count > 0)
            {
                if (_rainSpawned < _rainTotal && Time.time >= nextSpawnTime)
                {
                    int batchSize = Mathf.Clamp(_rainTotal / 4, 2, 5);
                    int batch = Mathf.Min(batchSize, _rainTotal - _rainSpawned);
                    for (int i = 0; i < batch; i++)
                    {
                        var go = GetPooledStarglitter();
                        var rt = go.GetComponent<RectTransform>();
                        var img = go.GetComponent<Image>();

                        float size = Random.Range(40f, 60f);
                        rt.sizeDelta = new Vector2(size, size);

                        float startX = Random.Range(-parentWidth * 0.4f, parentWidth * 0.4f);
                        rt.anchoredPosition = new Vector2(startX, startY);
                        img.color = Color.white;
                        go.SetActive(true);

                        _activeDrops.Add(new StarglitterDropData
                        {
                            rt = rt,
                            img = img,
                            startPos = new Vector2(startX, startY),
                            fallHeight = fallHeight,
                            startRot = Random.Range(0f, 360f),
                            endRot = 0f,
                            duration = Random.Range(0.6f, 1.0f),
                            elapsed = -Random.Range(0f, 0.15f),
                        });
                        int idx = _activeDrops.Count - 1;
                        var d = _activeDrops[idx];
                        d.endRot = d.startRot + Random.Range(180f, 540f);
                        _activeDrops[idx] = d;

                        _rainSpawned++;
                    }
                    nextSpawnTime = Time.time + Random.Range(0.15f, 0.35f);
                }

                for (int i = _activeDrops.Count - 1; i >= 0; i--)
                {
                    var drop = _activeDrops[i];
                    drop.elapsed += Time.deltaTime;

                    if (drop.elapsed < 0f)
                    {
                        _activeDrops[i] = drop;
                        continue;
                    }

                    float t = drop.elapsed / drop.duration;
                    if (t >= 1f)
                    {
                        drop.rt.gameObject.SetActive(false);
                        _activeDrops.RemoveAt(i);
                        continue;
                    }

                    drop.rt.anchoredPosition = new Vector2(drop.startPos.x, drop.startPos.y - drop.fallHeight * t);
                    drop.rt.localEulerAngles = new Vector3(0, 0, Mathf.Lerp(drop.startRot, drop.endRot, t));
                    _activeDrops[i] = drop;
                }

                yield return null;
            }

            _isRainActive = false;
            _rainTotal = 0;
        }

        private GameObject GetPooledStarglitter()
        {
            for (int i = 0; i < _starglitterPool.Count; i++)
            {
                if (!_starglitterPool[i].activeInHierarchy)
                    return _starglitterPool[i];
            }

            var obj = new GameObject("StarglitterDrop", typeof(RectTransform), typeof(Image));
            obj.transform.SetParent(starglitterRainContainer, false);
            obj.layer = starglitterRainContainer.gameObject.layer;

            var img = obj.GetComponent<Image>();
            img.sprite = starglitterSprite;
            img.preserveAspect = true;
            img.raycastTarget = false;

            var rt = obj.GetComponent<RectTransform>();
            rt.pivot = new Vector2(0.5f, 0.5f);

            obj.SetActive(false);
            _starglitterPool.Add(obj);
            return obj;
        }

        #endregion
    }
}
