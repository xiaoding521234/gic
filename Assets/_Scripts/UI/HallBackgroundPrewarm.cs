using UnityEngine;

namespace GIC.UI
{
    /// <summary>
    /// 大厅背景视频预热交接（跨场景静态持有，2026-10-11 手机端黑屏修复方案 A）。
    /// AssetCache.Preload 只把 VideoClip 资产读进内存、预热不了解码器——手机端 MediaCodec
    /// 首帧解码 0.3~1s，期间新建 RT 恒黑（桌面 WMF ~100-300ms 不可见，故仅手机暴露）。
    /// Splash 期预解码首帧渲入 RT 存此，MainHallScreen.EnsureVideoRT 认领即开播近零黑窗
    /// （对齐战斗立牌 PrewarmActionClips 先例，决策四十五同款根因）。
    /// 认领失败（预热超时/时段地址已变）当场销毁陈旧 RT，回落「现建 RT」旧行为，零回归。
    /// </summary>
    internal static class HallBackgroundPrewarm
    {
        private static RenderTexture _rt;
        private static string _address;

        /// <summary>Splash 预热完成侧移交（覆盖旧条目）</summary>
        internal static void Deposit(string address, RenderTexture rt)
        {
            if (_rt != null && _rt != rt)
            {
                _rt.Release();
                Object.Destroy(_rt);
            }
            _rt = rt;
            _address = address;
        }

        /// <summary>地址+尺寸全符 → 移交 RT（清槽）；空槽或不符（时段/位置已变）→ 销毁陈旧、返 null</summary>
        internal static RenderTexture TakeIfMatch(string address, int width, int height)
        {
            if (_rt == null) return null;

            if (_address != address || _rt.width != width || _rt.height != height)
            {
                _rt.Release();
                Object.Destroy(_rt);
                _rt = null;
                _address = null;
                return null;
            }

            var rt = _rt;
            _rt = null;
            _address = null;
            return rt;
        }
    }
}
