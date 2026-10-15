// ResidentVideoBudget.cs - 常驻视频解码器全局预算（2026-10-16 施放延迟根治批，安卓预算制）
// 每常驻 MediaCodec/WMF 会话持解码缓冲（4K 级片 ~22-56MB/个）且安卓硬解实例有设备上限——
// 全量常驻在低端机会爆。预算=全局上限+LRU 淘汰：被淘汰片回落现行主播放器换片路径
// （有首开延迟但池 RT 首帧仍在=不黑屏）。桌面/编辑器全量常驻（isMobilePlatform 门控）。
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;

namespace GIC.Battle
{
    /// <summary>常驻视频播放器预算管理器：移动端全局上限+LRU 淘汰；宿主 OnDestroy 自动注销</summary>
    public static class ResidentVideoBudget
    {
        /// <summary>移动端常驻解码器上限（Inspector 不可达的静态默认；BattlePlayer 建场可用
        /// 预算字段覆盖——2026-10-16 设计值 10：~3 单位×3 片，被淘汰片下次施放走换片路径）</summary>
        public static int MobileLimit = 10;

        // LRU 注册表：最近使用时间 → 淘汰最旧
        private static readonly List<(UnitView owner, VideoPlayer player, float lastUse)> _entries =
            new List<(UnitView, VideoPlayer, float)>();

        public static void Register(UnitView owner, VideoPlayer player)
        {
            if (owner == null || player == null) return;
            if (!Application.isMobilePlatform) return; // 桌面/编辑器全量常驻（WMF 会话成本低无实质上限）
            _entries.Add((owner, player, Time.unscaledTime));
            owner.Destroyed += UnregisterOwner; // 宿主销毁自动清
            while (_entries.Count > MobileLimit)
                EvictOldest();
        }

        /// <summary>施放时刷新使用时间（防在播/常用片被 LRU 误淘）</summary>
        public static void Touch(VideoPlayer player)
        {
            for (int i = 0; i < _entries.Count; i++)
                if (_entries[i].player == player)
                {
                    _entries[i] = (_entries[i].owner, _entries[i].player, Time.unscaledTime);
                    return;
                }
        }

        private static void EvictOldest()
        {
            if (_entries.Count == 0) return;
            int oldest = 0;
            for (int i = 1; i < _entries.Count; i++)
                if (_entries[i].lastUse < _entries[oldest].lastUse) oldest = i;
            var e = _entries[oldest];
            _entries.RemoveAt(oldest);
            if (e.owner != null && e.player != null && e.player.clip != null)
                e.owner.EvictResidentPlayer(e.player.clip); // 拆播放器；池 RT 首帧留存=不黑屏
        }

        private static void UnregisterOwner(UnitView owner)
        {
            _entries.RemoveAll(e => e.owner == owner);
        }

        /// <summary>对局清场调用（BattlePlayer.ClearViews）：全注册表清空</summary>
        public static void ClearAll()
        {
            _entries.Clear();
        }
    }
}
