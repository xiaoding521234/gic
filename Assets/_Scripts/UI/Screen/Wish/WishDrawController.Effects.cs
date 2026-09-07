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
    /// 视觉特效 — 光柱、命运之线、边缘泛光、倒计时、纠缠圆环风暴
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

        /// <summary>FateLine 材质懒加载（Additive 混合；线体/光晕线/圆环风暴共用）</summary>
        private bool EnsureFateLineMaterial()
        {
            if (_fateLineMaterial != null) return true;
            if (_fateLineShader == null)
                _fateLineShader = Shader.Find("UI/FateLine");
            if (_fateLineShader != null)
                _fateLineMaterial = new Material(_fateLineShader);
            return _fateLineMaterial != null;
        }

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
            bool isIntertwined = lineType == WishLineType.Intertwined;

            var lineObj = new GameObject(lineName, typeof(RectTransform), typeof(Image));
            lineObj.transform.SetParent(fateLineContainer, false);
            lineObj.layer = fateLineContainer.gameObject.layer;
            _activeFateLines.Add(lineObj);

            var lineImg = lineObj.GetComponent<Image>();
            lineImg.color = lineColor;
            lineImg.raycastTarget = false;
            if (EnsureFateLineMaterial())
                lineImg.material = _fateLineMaterial;

            var lineRect = lineObj.GetComponent<RectTransform>();

            float trackWidth = cardTrack.rect.width;
            // 纠缠之线：更粗更慢的"光矢"——主线加厚 + 宽幅低透明度光晕线随行（拖尾层次）
            float thickness = fateLineThickness * (isIntertwined ? Mathf.Max(intertwinedLineThicknessMultiplier, 1f) : 1f);
            float duration = isIntertwined ? Mathf.Max(intertwinedLineDuration, 0.05f) : 0.2f;

            lineRect.sizeDelta = new Vector2(trackWidth * 0.6f, thickness);
            lineRect.anchoredPosition = Vector2.zero;

            RectTransform haloRect = null;
            GameObject haloObj = null;
            if (isIntertwined)
            {
                haloObj = new GameObject("IntertwinedHalo", typeof(RectTransform), typeof(Image));
                haloObj.transform.SetParent(fateLineContainer, false);
                haloObj.layer = fateLineContainer.gameObject.layer;
                _activeFateLines.Add(haloObj);

                var haloImg = haloObj.GetComponent<Image>();
                haloImg.color = new Color(lineColor.r, lineColor.g, lineColor.b, 0.25f);
                haloImg.raycastTarget = false;
                if (_fateLineMaterial != null)
                    haloImg.material = _fateLineMaterial;

                haloRect = haloObj.GetComponent<RectTransform>();
                haloRect.sizeDelta = new Vector2(trackWidth * 0.6f, thickness * 4f);
            }

            Vector2 startPos = new Vector2(trackWidth * 0.5f + lineRect.rect.width * 0.5f, 0f);
            Vector2 endPos = new Vector2(-trackWidth * 0.5f - lineRect.rect.width * 0.5f, 0f);
            lineRect.anchoredPosition = startPos;
            if (haloRect != null) haloRect.anchoredPosition = startPos;

            float elapsed = 0f;
            bool hitPlayed = false;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                lineRect.anchoredPosition = Vector2.Lerp(startPos, endPos, t);
                if (haloRect != null)
                {
                    // 光晕线延迟 0.03s 起飞，形成拖尾层次
                    float haloT = Mathf.Clamp01((elapsed - 0.03f) / duration);
                    haloRect.anchoredPosition = Vector2.Lerp(startPos, endPos, haloT);
                }

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
            if (haloObj != null)
            {
                _activeFateLines.Remove(haloObj);
                Destroy(haloObj);
            }
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

        #region 纠缠圆环风暴（瞄准仪式）

        // 圆环素材：Resources/UI/Wish/intertwined_ring_N（AI 生成，参考抽卡背景图风格；黑底+Additive 材质自动隐形）
        private Sprite[] _ringSprites;
        private RectTransform _ringRoot;
        private Image _ceremonyVeil;
        private Coroutine _veilCoroutine;
        private Coroutine _ringStormCoroutine;
        private bool _ringSpawning;
        private float _ringStormElapsed;
        private float _ringSpawnCountdown;
        private readonly List<RingData> _activeRings = new();

        private struct RingData
        {
            public RectTransform rt;
            public Image img;
            public float elapsed;
            public float lifetime;
            public float startSize;
            public float endSize;
            public float spinSpeed;
            public float peakAlpha;
        }

        /// <summary>创建瞄准仪式两层 overlay：暗幕（紧贴 Background 之上）与圆环容器（暗幕之上、其余 UI 之下）</summary>
        private void CreateIntertwinedCeremonyOverlays()
        {
            if (drawRoot == null) return;

            var veilObj = new GameObject("IntertwinedVeil", typeof(RectTransform), typeof(Image));
            veilObj.transform.SetParent(drawRoot.transform, false);
            veilObj.layer = drawRoot.layer;
            var veilRect = veilObj.GetComponent<RectTransform>();
            veilRect.anchorMin = Vector2.zero;
            veilRect.anchorMax = Vector2.one;
            veilRect.offsetMin = Vector2.zero;
            veilRect.offsetMax = Vector2.zero;
            var veilImg = veilObj.GetComponent<Image>();
            veilImg.raycastTarget = false;
            veilImg.color = Color.clear;
            _ceremonyVeil = veilImg;
            veilRect.SetSiblingIndex(1);

            var ringRootObj = new GameObject("IntertwinedRingRoot", typeof(RectTransform));
            ringRootObj.transform.SetParent(drawRoot.transform, false);
            ringRootObj.layer = drawRoot.layer;
            var ringRect = ringRootObj.GetComponent<RectTransform>();
            ringRect.anchorMin = Vector2.zero;
            ringRect.anchorMax = Vector2.one;
            ringRect.offsetMin = Vector2.zero;
            ringRect.offsetMax = Vector2.zero;
            _ringRoot = ringRect;
            ringRect.SetSiblingIndex(2);
        }

        /// <summary>懒加载圆环素材（Resources/UI/Wish/intertwined_ring_N 按存在连续加载；一张都没有则风暴整体跳过）</summary>
        private bool EnsureRingSprites()
        {
            if (_ringSprites != null) return _ringSprites.Length > 0;

            var found = new List<Sprite>();
            for (int i = 0; i < 8; i++)
            {
                var s = Resources.Load<Sprite>($"UI/Wish/intertwined_ring_{i}");
                if (s == null) break;
                found.Add(s);
            }
            _ringSprites = found.ToArray();
            if (_ringSprites.Length == 0)
                Debug.LogWarning("[WishDrawController] 纠缠圆环素材缺失（Resources/UI/Wish/intertwined_ring_N），圆环风暴跳过");
            return _ringSprites.Length > 0;
        }

        /// <summary>纠缠瞄准期仪式开启：暗幕淡入 + 圆环风暴起势——生成频率在频率爬升秒数内从起始间隔爬到峰值间隔（拍板 3 秒最高），此后保持直到开火</summary>
        protected void StartIntertwinedCeremony()
        {
            if (!EnsureRingSprites() || !EnsureFateLineMaterial() || _ringRoot == null) return;

            PlayCeremonyVeil(show: true);

            _ringSpawning = true;
            _ringStormElapsed = 0f;
            _ringSpawnCountdown = 0f;
            if (_ringStormCoroutine == null)
                _ringStormCoroutine = StartCoroutine(RingStormCoroutine());
        }

        /// <summary>仪式收尾（开火时调用）：暗幕淡出 + 圆环停发；存活环由风暴协程自然播完淡出</summary>
        protected void StopIntertwinedCeremony()
        {
            PlayCeremonyVeil(show: false);
            _ringSpawning = false;
        }

        private void PlayCeremonyVeil(bool show)
        {
            if (_ceremonyVeil == null || intertwinedVeilAlpha <= 0f) return;
            if (_veilCoroutine != null) StopCoroutine(_veilCoroutine);
            _veilCoroutine = StartCoroutine(VeilCoroutine(show));
        }

        private IEnumerator VeilCoroutine(bool show)
        {
            float from = _ceremonyVeil.color.a;
            float to = show ? intertwinedVeilAlpha : 0f;
            float duration = show ? 0.4f : 0.25f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                _ceremonyVeil.color = new Color(0f, 0f, 0f, Mathf.Lerp(from, to, elapsed / duration));
                yield return null;
            }
            _ceremonyVeil.color = new Color(0f, 0f, 0f, to);
            _veilCoroutine = null;
        }

        /// <summary>风暴主循环：发射（频率随时间爬升）+ 批量更新存活环——单协程管理全部环（仿星辉雨模式，无每环协程）；停发后存活环播完即自灭退出</summary>
        private IEnumerator RingStormCoroutine()
        {
            while (_ringSpawning || _activeRings.Count > 0)
            {
                float dt = Time.deltaTime;
                _ringStormElapsed += dt;

                if (_ringSpawning)
                {
                    _ringSpawnCountdown -= dt;
                    if (_ringSpawnCountdown <= 0f)
                    {
                        float ramp = ringRampSeconds > 0f ? Mathf.Clamp01(_ringStormElapsed / ringRampSeconds) : 1f;
                        _ringSpawnCountdown = Mathf.Lerp(ringSpawnIntervalStart, ringSpawnIntervalPeak, ramp);
                        SpawnIntertwinedRing();
                    }
                }

                UpdateIntertwinedRings(dt);
                yield return null;
            }
            _ringStormCoroutine = null;
        }

        private void SpawnIntertwinedRing()
        {
            var go = new GameObject("IntertwinedRing", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_ringRoot, false);

            var img = go.GetComponent<Image>();
            img.sprite = _ringSprites[Random.Range(0, _ringSprites.Length)];
            img.material = _fateLineMaterial;   // Additive：素材黑底加色为 0 自动隐形，暗幕上呈发光
            img.raycastTarget = false;
            img.color = Color.clear;

            var rt = (RectTransform)go.transform;
            rt.anchoredPosition = Vector2.zero;                                // 画面中心出生
            rt.localEulerAngles = new Vector3(0f, 0f, Random.Range(0f, 360f)); // 随机初始角

            _activeRings.Add(new RingData
            {
                rt = rt,
                img = img,
                elapsed = 0f,
                lifetime = Mathf.Max(ringLifetime, 0.1f),
                startSize = ringStartSize,
                endSize = ringEndSize,
                spinSpeed = (Random.value < 0.5f ? -1f : 1f) * ringSpinSpeed * Random.Range(0.7f, 1.3f),
                peakAlpha = ringPeakAlpha,
            });
        }

        private void UpdateIntertwinedRings(float dt)
        {
            for (int i = _activeRings.Count - 1; i >= 0; i--)
            {
                RingData ring = _activeRings[i];
                ring.elapsed += dt;
                float t = ring.elapsed / ring.lifetime;
                if (t >= 1f)
                {
                    _activeRings.RemoveAt(i);
                    Destroy(ring.rt.gameObject);
                    continue;
                }

                // 迅速放大（easeOut：前快后慢更像能量扩散）+ 顺/逆时针快速自旋
                float size = Mathf.Lerp(ring.startSize, ring.endSize, 1f - (1f - t) * (1f - t));
                ring.rt.sizeDelta = new Vector2(size, size);
                Vector3 rot = ring.rt.localEulerAngles;
                rot.z += ring.spinSpeed * dt;
                ring.rt.localEulerAngles = rot;

                // 前 10% 淡入、后 30% 淡出
                float alpha = ring.peakAlpha * Mathf.Clamp01(t / 0.1f) * (t > 0.7f ? 1f - (t - 0.7f) / 0.3f : 1f);
                ring.img.color = new Color(ringTintColor.r, ringTintColor.g, ringTintColor.b, alpha);

                _activeRings[i] = ring;
            }
        }

        /// <summary>硬清理（OnDisable 兜底）：停协程、销毁全部环、清暗幕</summary>
        private void CleanupIntertwinedCeremony()
        {
            _ringSpawning = false;
            if (_ringStormCoroutine != null)
            {
                StopCoroutine(_ringStormCoroutine);
                _ringStormCoroutine = null;
            }
            if (_veilCoroutine != null)
            {
                StopCoroutine(_veilCoroutine);
                _veilCoroutine = null;
            }
            foreach (var ring in _activeRings)
            {
                if (ring.rt != null) Destroy(ring.rt.gameObject);
            }
            _activeRings.Clear();
            if (_ceremonyVeil != null)
                _ceremonyVeil.color = Color.clear;
        }

        #endregion

        #region 纠缠开火粉闪

        // 纠缠之线开火的全屏粉色闪光（drawRoot 子物体，alpha 尖峰快速衰减）
        private Image _flashImage;
        private Coroutine _flashCoroutine;

        private void CreateIntertwinedFlash()
        {
            if (drawRoot == null) return;

            var flashObj = new GameObject("IntertwinedFlash", typeof(RectTransform), typeof(Image));
            flashObj.transform.SetParent(drawRoot.transform, false);
            flashObj.layer = drawRoot.layer;

            var rect = flashObj.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            _flashImage = flashObj.GetComponent<Image>();
            _flashImage.raycastTarget = false;
            _flashImage.color = Color.clear;
        }

        /// <summary>纠缠之线开火：全屏粉色闪光（alpha 尖峰后快速衰减）</summary>
        private void PlayIntertwinedFlash()
        {
            if (_flashImage == null || intertwinedFlashAlpha <= 0f) return;
            if (_flashCoroutine != null) StopCoroutine(_flashCoroutine);
            _flashCoroutine = StartCoroutine(FlashCoroutine());
        }

        private IEnumerator FlashCoroutine()
        {
            Color c = intertwinedLineColor;
            const float duration = 0.25f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                _flashImage.color = new Color(c.r, c.g, c.b, intertwinedFlashAlpha * (1f - t));
                yield return null;
            }
            _flashImage.color = Color.clear;
            _flashCoroutine = null;
        }

        /// <summary>清理粉闪（OnDisable 兜底：协程随物体停用死亡时防止粉色滞留）</summary>
        private void CleanupIntertwinedFlash()
        {
            if (_flashCoroutine != null)
            {
                StopCoroutine(_flashCoroutine);
                _flashCoroutine = null;
            }
            if (_flashImage != null)
                _flashImage.color = Color.clear;
        }

        #endregion

        #region 倒计时

        private void UpdateCountdownBar(float ratio)
        {
            if (countdownBarFill == null) return;
            ratio = Mathf.Clamp01(ratio);
            countdownBarFill.localScale = new Vector3(ratio, 1f, 1f);
        }

        /// <summary>倒计时条按下一发线型调色：纠缠之线瞄准期染粉（仪式感），其余恢复原色（首次缓存原色）</summary>
        private void UpdateCountdownTint()
        {
            if (countdownBarFill == null) return;
            var img = countdownBarFill.GetComponent<Image>();
            if (img == null) return;

            if (!_hasOriginalCountdownColor)
            {
                _originalCountdownColor = img.color;
                _hasOriginalCountdownColor = true;
            }

            bool pink = _flow != null && _flow.UpcomingLineType == WishLineType.Intertwined;
            img.color = pink ? intertwinedLineColor : _originalCountdownColor;
        }

        #endregion
    }
}
