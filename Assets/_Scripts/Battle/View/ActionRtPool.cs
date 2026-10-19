using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;

namespace GIC.Battle
{
    /// <summary>
    /// 动作片 RT 全局池（2026-10-20 审查批#3）：同 clip 全场共享一份 RT+播放租约——
    /// 原 per-(单位,clip) RT 在镜像局同角色多单位下重复分配 identical 内容（RT 2~20MB/份，
    /// 40 份≈100-250MB；移动端 RT 还不计入 ResidentVideoBudget 解码器预算）。
    /// **并发同片施放（双方同角色同片同时播）=唯一冲突面**：租约被占时后到方懒建**私有 RT**
    /// （播放期独占、结束即释放）——常态（非并发）全员共享零重复。
    /// 预热期各单位解码器向共享 RT 写 identical 内容（同片同帧）=预热无冲突；
    /// 但真实施放租约被占期间，其它单位预热跳过首帧渲染（IsLeased 查询防写花在显画面）。
    /// </summary>
    public static class ActionRtPool
    {
        private static readonly Dictionary<VideoClip, RenderTexture> _shared =
            new Dictionary<VideoClip, RenderTexture>();
        private static readonly Dictionary<VideoClip, UnitView> _lease =
            new Dictionary<VideoClip, UnitView>();
        private static readonly Dictionary<(VideoClip, UnitView), RenderTexture> _private =
            new Dictionary<(VideoClip, UnitView), RenderTexture>();

        /// <summary>取动作 RT（施放入口）：租约空闲/本机持有=共享 RT（登记租约）；被他机播放
        /// 占用=懒建私有 RT（本机播放期独占，Release 时销毁）</summary>
        public static RenderTexture Acquire(VideoClip clip, UnitView requester)
        {
            if (clip == null || clip.width <= 0 || clip.height <= 0) return null;
            var rt = GetShared(clip);
            if (!_lease.TryGetValue(clip, out var holder) || holder == null)
            {
                _lease[clip] = requester; // 空闲（或前持者已亡佚）→ 本机持有
                return rt;
            }
            if (holder == requester) return rt; // 再入（换私有回共享等）
            return CreatePrivate(clip, requester); // 并发冲突：后到方私有
        }

        /// <summary>释放租约/私有 RT（回待机面/换片施放/单位清理；幂等——无租约无私有=零操作）</summary>
        public static void Release(VideoClip clip, UnitView requester)
        {
            if (clip == null) return;
            if (_lease.TryGetValue(clip, out var holder) && holder == requester)
                _lease.Remove(clip);
            var key = (clip, requester);
            if (_private.TryGetValue(key, out var priv))
            {
                _private.Remove(key);
                if (priv != null) { priv.Release(); Object.Destroy(priv); }
            }
        }

        /// <summary>租约查询（预热防冲突：被占时跳过首帧渲染写入，防写花在显画面）</summary>
        public static bool IsLeased(VideoClip clip)
            => clip != null && _lease.TryGetValue(clip, out var holder) && holder != null;

        /// <summary>共享 RT 直取（预热建件用——不占租约）</summary>
        public static RenderTexture GetShared(VideoClip clip)
        {
            if (clip == null || clip.width <= 0 || clip.height <= 0) return null;
            if (!_shared.TryGetValue(clip, out var rt) || rt == null)
            {
                rt = new RenderTexture((int)clip.width, (int)clip.height, 0,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                _shared[clip] = rt;
            }
            return rt;
        }

        private static RenderTexture CreatePrivate(VideoClip clip, UnitView requester)
        {
            var key = (clip, requester);
            if (_private.TryGetValue(key, out var priv) && priv != null) return priv;
            priv = new RenderTexture((int)clip.width, (int)clip.height, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            _private[key] = priv;
            return priv;
        }

        /// <summary>对局清场（BattlePlayer.ClearViews）：全部共享/私有 RT 释放（租约随清）</summary>
        public static void ClearAll()
        {
            foreach (var rt in _shared.Values)
                if (rt != null) { rt.Release(); Object.Destroy(rt); }
            _shared.Clear();
            _lease.Clear();
            foreach (var rt in _private.Values)
                if (rt != null) { rt.Release(); Object.Destroy(rt); }
            _private.Clear();
        }
    }
}
