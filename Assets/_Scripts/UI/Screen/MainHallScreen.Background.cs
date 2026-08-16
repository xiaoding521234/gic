using UnityEngine;
using GIC.Framework;
using GIC.Data;
using GIC.Tool;

namespace GIC.UI
{
    /// <summary>
    /// 主厅 — 根据当前位置/时段加载大厅背景（Addressables via AssetCache）
    /// </summary>
    public partial class MainHallScreen
    {
        private string _lastBgAddress;
        private string _pendingBgAddress;

        private void UpdateBackground(PositionName position)
        {
            if (backgroundRenderer == null) return;

            var positionData = _positionManager.GetPositionData(position);
            if (positionData == null) return;

            RegionName region = positionData.region;

            string regionName = region.ToString();
            string positionName = position.ToString().ToSnakeCase();
            string timeSuffix = TimeUtility.GetTimeSuffix();
            string address = $"PositionBack/{regionName}/{positionName}_{timeSuffix}";

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

                // 加载期间地址可能已变（快速切换），丢弃过期结果
                if (_pendingBgAddress != address) return;

                if (sprite != null)
                {
                    backgroundRenderer.sprite = sprite;
                    _lastBgAddress = address;

                    // 新背景已上屏，释放旧背景
                    if (oldAddress != null && oldAddress != address)
                        _assetCache?.Release(oldAddress);
                }
                else
                {
                    GICLog.Warn($"未找到背景图片: {address}");
                }
            }, LoadPriority.High);
        }
    }
}
