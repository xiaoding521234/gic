using UnityEngine;
using UnityEngine.Video;
using GIC.Framework;
using GIC.Data;
using GIC.Tool;

namespace GIC.UI
{
    /// <summary>
    /// 主厅 — 根据当前位置/时段加载大厅背景（Addressables via AssetCache）。
    /// 数据驱动：PositionConfig 配置每个时段用图片(Sprite)还是视频(VideoClip)，有视频时优先使用视频。
    /// VideoPlayer 和 Quad 在场景中预先配置（Background3D/BackgroundVideo），此处只管播放控制。
    /// </summary>
    public partial class MainHallScreen
    {
        // ── 图片背景状态 ──
        private string _lastBgAddress;
        private string _pendingBgAddress;

        // ── 视频背景状态 ──
        private RenderTexture _bgVideoRT;
        private string _lastVideoAddress;
        private string _pendingVideoAddress;

        private void UpdateBackground(PositionName position)
        {
            if (backgroundRenderer == null) return;

            var positionData = _positionManager.GetPositionData(position);
            if (positionData == null) return;

            var timePeriod = TimeUtility.GetCurrentTimePeriod();
            var mediaType = positionData.GetMediaType(timePeriod);
            string address = positionData.GetBackgroundAddress(timePeriod);

            if (mediaType == PositionMediaType.Video)
                UpdateVideoBackground(address);
            else
                UpdateImageBackground(address);
        }

        // ── 图片背景 ──

        private void UpdateImageBackground(string address)
        {
            // 如果视频正在播放，切换回图片模式
            if (backgroundVideoPlayer != null && backgroundVideoPlayer.gameObject.activeSelf)
                StopVideoBackground();

            // 地址相同且精灵已加载 → 跳过重载
            if (address == _lastBgAddress && backgroundRenderer.sprite != null)
                return;

            // 相同地址已在加载中 → 不重复请求
            if (address == _pendingBgAddress)
                return;

            _pendingBgAddress = address;
            string oldAddress = _lastBgAddress;

            _assetCache?.LoadAsync<Sprite>(address, sprite =>
            {
                if (this == null || backgroundRenderer == null) return;
                if (_pendingBgAddress != address) return;

                if (sprite != null)
                {
                    backgroundRenderer.sprite = sprite;
                    _lastBgAddress = address;
                    if (oldAddress != null && oldAddress != address)
                        _assetCache?.Release(oldAddress);
                }
                else
                {
                    GICLog.Warn($"未找到背景图片: {address}");
                }
            }, LoadPriority.High);
        }

        // ── 视频背景 ──

        private void UpdateVideoBackground(string address)
        {
            if (backgroundVideoPlayer == null)
            {
                GICLog.Warn("[BackgroundVideo] 场景未配置 backgroundVideoPlayer，跳过视频背景");
                return;
            }

            // 隐藏图片渲染器，显示视频
            if (backgroundRenderer.enabled)
                backgroundRenderer.enabled = false;
            if (!backgroundVideoPlayer.gameObject.activeSelf)
                backgroundVideoPlayer.gameObject.SetActive(true);

            // 地址相同且视频已在播放 → 跳过
            if (address == _lastVideoAddress && backgroundVideoPlayer.isPlaying)
                return;

            // 相同地址已在加载中 → 不重复请求
            if (address == _pendingVideoAddress)
                return;

            _pendingVideoAddress = address;
            string oldAddress = _lastVideoAddress;

            if (backgroundVideoPlayer.isPlaying)
                backgroundVideoPlayer.Stop();

            _assetCache?.LoadAsync<VideoClip>(address, clip =>
            {
                if (this == null || backgroundVideoPlayer == null) return;
                if (_pendingVideoAddress != address) return;

                if (clip != null)
                {
                    backgroundVideoPlayer.clip = clip;
                    _lastVideoAddress = address;

                    // 按视频原始分辨率创建/重建 RenderTexture，保持原画质
                    EnsureVideoRT((int)clip.width, (int)clip.height);

                    if (oldAddress != null && oldAddress != address)
                        _assetCache?.Release(oldAddress);

                    backgroundVideoPlayer.isLooping = true;
                    backgroundVideoPlayer.Play();
                    GICLog.Info($"[BackgroundVideo] 播放视频: {address} ({clip.width}x{clip.height})");
                }
                else
                {
                    GICLog.Warn($"未找到背景视频: {address}，回退到图片模式");
                    backgroundRenderer.enabled = true;
                    if (backgroundVideoPlayer.gameObject.activeSelf)
                        backgroundVideoPlayer.gameObject.SetActive(false);
                }
            }, LoadPriority.High);
        }

        private void StopVideoBackground()
        {
            if (backgroundVideoPlayer != null && backgroundVideoPlayer.isPlaying)
                backgroundVideoPlayer.Stop();

            if (backgroundVideoPlayer != null && backgroundVideoPlayer.gameObject.activeSelf)
                backgroundVideoPlayer.gameObject.SetActive(false);

            if (!backgroundRenderer.enabled)
                backgroundRenderer.enabled = true;

            if (_lastVideoAddress != null)
            {
                _assetCache?.Release(_lastVideoAddress);
                _lastVideoAddress = null;
            }
            if (_pendingVideoAddress != null && _pendingVideoAddress != _lastVideoAddress)
            {
                _assetCache?.Release(_pendingVideoAddress);
                _pendingVideoAddress = null;
            }
        }

        /// <summary>
        /// 按视频原始分辨率创建RenderTexture并绑定到VideoPlayer和材质。
        /// 分辨率变化时（切换不同分辨率的视频）重建RT。
        /// </summary>
        private void EnsureVideoRT(int width, int height)
        {
            if (_bgVideoRT != null && _bgVideoRT.width == width && _bgVideoRT.height == height) return;

            if (_bgVideoRT != null)
                _bgVideoRT.Release();

            _bgVideoRT = new RenderTexture(width, height, 0);
            backgroundVideoPlayer.targetTexture = _bgVideoRT;

            // 材质：URP 项目用 URP/Unlit，纹理属性是 _BaseMap（非 _MainTex）
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Texture");
            var mat = new Material(shader);
            // URP/Unlit 用 _BaseMap，内置 Unlit/Texture 用 _MainTex，两者都设
            mat.SetTexture("_BaseMap", _bgVideoRT);
            mat.SetTexture("_MainTex", _bgVideoRT);

            var mr = backgroundVideoPlayer.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
        }

        private void OnDestroyBackground()
        {
            if (backgroundVideoPlayer != null && backgroundVideoPlayer.isPlaying)
                backgroundVideoPlayer.Stop();
            if (_bgVideoRT != null)
            {
                _bgVideoRT.Release();
                _bgVideoRT = null;
            }
            if (_lastVideoAddress != null)
                _assetCache?.Release(_lastVideoAddress);
            if (_pendingVideoAddress != null && _pendingVideoAddress != _lastVideoAddress)
                _assetCache?.Release(_pendingVideoAddress);
        }
    }
}
