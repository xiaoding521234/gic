using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace GIC.UI
{
    /// <summary>
    /// 五星过渡视频 — 全屏黑屏过渡动画，抽到五星时播放
    /// </summary>
    public partial class WishDrawController
    {
        private VideoPlayer _star5VideoPlayer;
        private RenderTexture _star5VideoRT;
        private RawImage _star5VideoRawImage;

        /// <summary>五星过渡视频是否正在播放（冻结冷却倒计时）</summary>
        protected bool _isVideoPlaying;

        /// <summary>
        /// 创建全屏视频覆盖层（类似 ScreenEdgeGlow，在 Awake 中调用）
        /// GameObject 保持激活，通过 RawImage alpha 控制可见性，使 VideoPlayer.Prepare 可用
        /// </summary>
        private void CreateStar5VideoOverlay()
        {
            var videoObj = new GameObject("Star5VideoOverlay", typeof(RectTransform), typeof(RawImage), typeof(VideoPlayer));
            videoObj.transform.SetParent(transform, false);
            videoObj.layer = gameObject.layer;
            videoObj.transform.SetAsLastSibling();

            var rect = videoObj.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            _star5VideoRawImage = videoObj.GetComponent<RawImage>();
            _star5VideoRawImage.color = new Color(1f, 1f, 1f, 0f); // 不可见
            _star5VideoRawImage.raycastTarget = false;              // 不拦截输入

            _star5VideoRT = new RenderTexture(1920, 1080, 0);
            _star5VideoRawImage.texture = _star5VideoRT;

            _star5VideoPlayer = videoObj.GetComponent<VideoPlayer>();
            _star5VideoPlayer.renderMode = VideoRenderMode.RenderTexture;
            _star5VideoPlayer.targetTexture = _star5VideoRT;
            _star5VideoPlayer.playOnAwake = false;
            _star5VideoPlayer.isLooping = false;
            _star5VideoPlayer.audioOutputMode = VideoAudioOutputMode.Direct;
            _star5VideoPlayer.url = System.IO.Path.Combine(Application.streamingAssetsPath, "mc.mp4");
        }

        /// <summary>
        /// 预加载五星过渡视频（点击祈愿按钮时调用，非阻塞）
        /// </summary>
        private void PreloadStar5Video()
        {
            if (_star5VideoPlayer != null && !_star5VideoPlayer.isPrepared)
                _star5VideoPlayer.Prepare();
        }

        /// <summary>
        /// 播放五星过渡视频协程
        /// 视频加载失败时静默跳过
        /// </summary>
        private IEnumerator PlayStar5TransitionCoroutine()
        {
            if (_star5VideoPlayer == null) yield break;

            _isVideoPlaying = true;

            if (!_star5VideoPlayer.isPrepared)
                _star5VideoPlayer.Prepare();

            float prepareTimeout = 3f;
            float elapsed = 0f;
            while (!_star5VideoPlayer.isPrepared && elapsed < prepareTimeout)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            if (!_star5VideoPlayer.isPrepared)
            {
                _isVideoPlaying = false;
                yield break;
            }

            _star5VideoPlayer.Play();

            _star5VideoRawImage.color = Color.white;
            _star5VideoRawImage.raycastTarget = true;

            // 视频播放期间禁用边缘泛光，防止穿透视频覆盖层
            if (_edgeGlow != null) _edgeGlow.enabled = false;

            while (_star5VideoPlayer.isPlaying)
                yield return null;

            if (_edgeGlow != null) _edgeGlow.enabled = true;

            _star5VideoPlayer.Stop();

            // 隐藏视频覆盖层
            _star5VideoRawImage.color = new Color(1f, 1f, 1f, 0f);
            _star5VideoRawImage.raycastTarget = false;
            _isVideoPlaying = false;
        }

        private void CleanupStar5Video()
        {
            if (_star5VideoPlayer != null)
            {
                _star5VideoPlayer.Stop();
            }
            if (_star5VideoRawImage != null)
            {
                _star5VideoRawImage.color = new Color(1f, 1f, 1f, 0f);
                _star5VideoRawImage.raycastTarget = false;
            }
            if (_edgeGlow != null) _edgeGlow.enabled = true;
            _isVideoPlaying = false;
        }

        private void OnDestroyStar5Video()
        {
            if (_star5VideoRT != null)
            {
                _star5VideoRT.Release();
                _star5VideoRT = null;
            }
        }
    }
}
