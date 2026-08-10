using System.Collections.Generic;
using GIC.Data;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace GIC.UI
{
    /// <summary>
    /// 祈愿立绘预加载器 — 在 MainHall 启动时预加载首个角色（哥伦比娅）的 4K 立绘，
    /// 常驻内存永不释放，保证进入祈愿场景第一眼看见的不是黑屏。
    /// </summary>
    public static class WishArtPreloader
    {
        private static readonly Dictionary<UnitName, AsyncOperationHandle<Sprite>> _cache = new();

        /// <summary>
        /// 需要常驻内存的角色立绘列表
        /// </summary>
        private static readonly UnitName[] PersistentUnits = { UnitName.Columbina };

        /// <summary>
        /// 在 MainHall 启动时调用，预加载所有常驻立绘
        /// </summary>
        public static void PreloadPersistent()
        {
            foreach (var unit in PersistentUnits)
            {
                if (_cache.ContainsKey(unit)) continue;

                string address = $"WishArt/{unit.ToString().ToLower()}";
                var handle = Addressables.LoadAssetAsync<Sprite>(address);
                _cache[unit] = handle;
            }
        }

        /// <summary>
        /// 查询某个角色的立绘是否已缓存（正在加载或已完成）
        /// </summary>
        public static bool TryGetHandle(UnitName unit, out AsyncOperationHandle<Sprite> handle)
        {
            return _cache.TryGetValue(unit, out handle);
        }
    }
}
