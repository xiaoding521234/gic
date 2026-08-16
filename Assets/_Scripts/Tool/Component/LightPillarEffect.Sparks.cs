using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace GIC.Tool
{
    /// <summary>
    /// 光柱特效 — 双向飞溅粒子（自行消亡，不进 _activeCoroutines 追踪）
    /// </summary>
    public partial class LightPillarEffect
    {
        #region 粒子

        private void SpawnSparks(Color color)
        {
            for (int i = 0; i < 粒子数量; i++)
            {
                var go = new GameObject("Spark");
                go.transform.SetParent(_rectTransform, false);

                var img = go.AddComponent<Image>();
                img.sprite = _sharedRadialSprite;
                img.raycastTarget = false;
                img.material = _additiveMat;

                RectTransform rect = go.transform as RectTransform;
                rect.sizeDelta = Vector2.one * 粒子大小 * Random.Range(0.6f, 1.4f);
                rect.anchoredPosition = Vector2.zero;
                rect.localScale = Vector3.zero;

                // 方向：上下双向飞溅，带少量水平扩散
                float angle = Random.Range(-粒子扩散, 粒子扩散) * Mathf.PI;
                float ySign = Random.value > 0.5f ? 1f : -1f;
                Vector2 dir = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle) * ySign).normalized;
                float speed = 粒子速度 * Random.Range(0.6f, 1.2f);
                float lifetime = 粒子时长 * Random.Range(0.7f, 1.3f);
                float delay = Random.Range(0f, 0.1f);

                img.color = new Color(color.r, color.g, color.b, 0f);
                // 粒子协程不追踪，由 ClearSpawned 直接销毁对象
                StartCoroutine(AnimateSpark(rect, img, dir, speed, lifetime, delay, color, 粒子不透明度 * 爆发强度));
                _spawnedObjects.Add(go);
            }
        }

        private IEnumerator AnimateSpark(RectTransform rect, Image img, Vector2 dir, float speed,
            float lifetime, float delay, Color color, float maxAlpha)
        {
            if (delay > 0f)
                yield return new WaitForSecondsRealtime(delay);

            float t = 0f;
            Vector2 pos = Vector2.zero;
            float initialSpeed = speed;
            float alpha = Mathf.Min(maxAlpha, 1f);

            while (t < lifetime && rect != null)
            {
                t += Time.unscaledDeltaTime;
                float p = t / lifetime;

                // 速度持续衰减但不停止（飞到终点不停留）
                float curSpeed = initialSpeed * (1f - p * 0.7f);
                pos += dir * curSpeed * Time.unscaledDeltaTime;
                rect.anchoredPosition = pos;

                // 前期快速放大，后期缓慢缩小
                float scaleP = p < 0.1f
                    ? Mathf.Lerp(0f, 1f, p / 0.1f)
                    : Mathf.Lerp(1f, 0.2f, (p - 0.1f) / 0.9f);
                rect.localScale = Vector3.one * scaleP;

                // 前 60% 保持高透明度，后 40% 平滑淡出
                float fade = p < 0.6f
                    ? alpha
                    : alpha * (1f - Mathf.Pow((p - 0.6f) / 0.4f, 2f));
                img.color = new Color(color.r, color.g, color.b, fade);
                yield return null;
            }

            if (rect != null)
                Destroy(rect.gameObject);
        }

        #endregion
    }
}
