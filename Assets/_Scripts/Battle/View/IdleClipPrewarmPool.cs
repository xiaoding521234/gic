// IdleClipPrewarmPool.cs - 待机片卡组预热池（2026-10-16 部署黑窗根治批）
// 问题：部署单位=中途建 UnitView→待机片现建 RT（新分配恒黑）+VideoPlayer 首开解码
// （WMF 0.1~0.3s/安卓 MediaCodec 0.3~1s）→期间 ChromaKey quad 显示纯黑不透明块
// （黑像素无绿不被抠）——「部署黑块随后正常」。
// 方案（用户拍板「按本局卡组并集」）：开局把本局可能部署角色的待机片各预解码首帧存入静态
// 模板 RT；UnitView.Create 命中→Graphics.Blit 模板首帧到单位自己的 RT=零黑窗（解码器随后
// 无缝接管同 RT）；未命中（LAN 客户端看不见的敌方人类卡组角色）→静态立牌过渡兜底（quad
// 藏、sprite 显示，首帧落地再切换）。池=「首帧模板库」借还语义最简（多枚同型部署各自 RT 照旧，
// 池模板无限次服务零所有权管理）；对局结束 Clear。
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;

namespace GIC.Battle
{
    /// <summary>待机片首帧模板池：VideoClip → 带首帧的模板 RT。Build 场景喂入、UnitView.Create 消费</summary>
    public static class IdleClipPrewarmPool
    {
        private static readonly Dictionary<VideoClip, RenderTexture> _frames = new Dictionary<VideoClip, RenderTexture>();
        private static Coroutine _running;

        /// <summary>预热一批待机片（幂等——已在池/进行中直通）：临时 VideoPlayer Prepare→Play 等首帧渲入模板 RT→拆临时件。
        /// 5s/片超时兜底（挂死不滞留协程，模板留黑=未预热态自然回落兜底）</summary>
        public static void Prewarm(MonoBehaviour host, IEnumerable<VideoClip> clips)
        {
            if (host == null || clips == null) return;
            var todo = new List<VideoClip>();
            foreach (var clip in clips)
                if (clip != null && clip.width > 0 && clip.height > 0 && !_frames.ContainsKey(clip))
                    todo.Add(clip);
            if (todo.Count == 0) return;
            if (_running != null) host.StopCoroutine(_running);
            _running = host.StartCoroutine(PrewarmRoutine(todo));
        }

        private static IEnumerator PrewarmRoutine(List<VideoClip> clips)
        {
            var poolGo = new GameObject("IdleClipPrewarmPool") { hideFlags = HideFlags.HideAndDontSave };
            foreach (var clip in clips)
            {
                if (clip == null || _frames.ContainsKey(clip)) continue;
                var rt = new RenderTexture((int)clip.width, (int)clip.height, 0,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                var vp = poolGo.AddComponent<VideoPlayer>();
                try
                {
                    vp.playOnAwake = false;
                    vp.clip = clip;
                    vp.renderMode = VideoRenderMode.RenderTexture;
                    vp.targetTexture = rt;
                    vp.audioOutputMode = VideoAudioOutputMode.None;
                    vp.Prepare();
                    float deadline = Time.unscaledTime + 5f;
                    while (!vp.isPrepared && Time.unscaledTime < deadline) yield return null;
                    if (!vp.isPrepared) { rt.Release(); Object.Destroy(rt); continue; } // 超时：不留黑模板（未预热态自然回落兜底）
                    vp.Play();
                    deadline = Time.unscaledTime + 5f;
                    while (vp.frame < 1 && Time.unscaledTime < deadline) yield return null;
                    if (vp.frame < 1) { rt.Release(); Object.Destroy(rt); continue; }
                    vp.Pause();
                    _frames[clip] = rt; // 模板 RT 留首帧（之后临时播放器拆走）
                }
                finally
                {
                    vp.Stop();
                    vp.clip = null;
                    Object.DestroyImmediate(vp);
                }
                yield return null; // 分片逐个预热，不占满一帧
            }
            Object.Destroy(poolGo);
            _running = null;
        }

        /// <summary>Create 消费：命中→把模板首帧 Blit 进单位自己的 RT（零黑窗，主播放器解码后无缝接管）；未命中=false（调用方走静态立牌过渡兜底）</summary>
        public static bool TryCopyFirstFrame(VideoClip clip, RenderTexture targetRt)
        {
            if (clip == null || targetRt == null) return false;
            if (!_frames.TryGetValue(clip, out var frame) || frame == null) return false;
            var prev = RenderTexture.active;
            RenderTexture.active = targetRt;
            Graphics.Blit(frame, targetRt);
            RenderTexture.active = prev;
            return true;
        }

        /// <summary>清池（对局结束/切场景调用——模板 RT 释放；借出的首帧已拷进单位 RT 不受影响）</summary>
        public static void Clear()
        {
            foreach (var rt in _frames.Values)
                if (rt != null) { rt.Release(); Object.Destroy(rt); }
            _frames.Clear();
        }
    }
}
