// ==================== SceneFadeOverlay.cs（根场景转场加载页门面） ====================
// 联机开局→战斗场景加载等根转场的"盖场"原语：把同步尖峰（StopHost/场景激活帧）藏进
// 加载页里，新场景就绪后由调用方 Reveal 揭幕。
// 原神式加载页 prefab（用户拍板参考原神截图）——白底+势力徽标缓转（Cover 按所选地图
// 换标）+词条文案（多条随机，点击换条）+元素图标点亮，布局见 Resources/Prefabs/LoadingOverlay.prefab。
// 淡入淡出时长统一 0.2s（2026-09-13 用户拍板）。
using System.Collections;
using UnityEngine;
using GIC.Framework;
using GIC.Data;

namespace GIC.UI
{
    public static class SceneFadeOverlay
    {
        private const string PrefabPath = "Prefabs/LoadingOverlay";
        private const float SafetyRevealSeconds = 15f; // 揭幕方失约（加载失败等）时的自动揭幕兜底

        private static LoadingOverlayDriver _driver;
        private static Coroutine _safetyRoutine;

        /// <summary>懒创建加载页实例（UIRoot 挂 GameScene=DontDestroyOnLoad，跨根场景存活）</summary>
        private static LoadingOverlayDriver EnsureCreated()
        {
            if (_driver != null) return _driver;
            var uiRoot = GameObject.Find("UIRoot");
            if (uiRoot == null) return null;

            var prefab = Resources.Load<GameObject>(PrefabPath);
            if (prefab == null)
            {
                GICLog.Error($"[SceneFadeOverlay] 加载页 prefab 缺失: Resources/{PrefabPath}（转场无盖场，动画直接裸奔）");
                return null;
            }

            var go = Object.Instantiate(prefab, uiRoot.transform, false);
            go.name = "LoadingOverlay";
            _driver = go.GetComponent<LoadingOverlayDriver>();
            if (_driver == null)
            {
                GICLog.Error("[SceneFadeOverlay] prefab 缺少 LoadingOverlayDriver 组件");
                Object.Destroy(go);
                return null;
            }
            go.SetActive(false);
            return _driver;
        }

        /// <summary>当前加载页不透明度（0=透明未覆盖；冒烟/断言用）</summary>
        public static float CurrentAlpha => _driver != null ? _driver.当前透明度 : 0f;

        /// <summary>
        /// 加载页淡入（盖住后续同步尖峰与场景加载），并按所选地图势力换中央徽标。
        /// 安全兜底：15s 内无人 Reveal 则自动揭幕，防"加载失败卡死加载页"。
        /// </summary>
        public static void Cover(BattleMapConfig map, float duration = 0.2f)
        {
            var d = EnsureCreated();
            if (d == null) return;
            if (map != null) d.SetFaction(map.faction);
            d.Cover(duration);
            StartSafety(d);
        }

        /// <summary>揭幕（新场景就绪时调用）。从未覆盖时纯 no-op（调试直开零副作用）。</summary>
        public static void Reveal(float duration = 0.2f)
        {
            if (_driver == null) return; // 未 Cover 过 → 不创建不留痕
            StopSafety();
            _driver.Reveal(duration);
        }

        /// <summary>
        /// 预热加载页：提前实例化 prefab 并激活两帧再隐藏——把首次 Cover 的 ~0.5s 同步开销
        /// （prefab 实例化+TMP 字体初始化等，2026-09-13 帧实测）挪到无感时机（建房等待期），
        /// 转场淡入帧不再尖峰。同时预热战斗工厂（UnitFactory/SkillFactory：Resources.Load
        /// prefab+反射注册），开局装配帧的同步成本进一步缩水。幂等（工厂自带 _isInitialized 守卫）。
        /// </summary>
        public static void PreWarm()
        {
            var d = EnsureCreated();
            if (d == null) return;
            d.gameObject.SetActive(true); // 先激活（Awake/TMP 初始化同步发生；inactive 对象无法 StartCoroutine）
            d.StartCoroutine(PreWarmRoutine(d));

            // 战斗工厂预热：装配帧不再付 Resources.Load+反射扫描+首枚单位实例化
            // 的首次成本（UnitFactory.PreWarm 实例化一枚立牌再销毁，依赖资产进缓存）
            GIC.Battle.UnitFactory.PreWarm();
            GIC.Battle.SkillFactory.Initialize();
        }

        private static IEnumerator PreWarmRoutine(LoadingOverlayDriver d)
        {
            yield return null;
            yield return null; // 渲染两帧：字体图集/材质完成首用烘焙
            d.gameObject.SetActive(false);
        }

        // ── 15s 安全兜底（跑在加载页实例上，随销毁自灭） ──

        private static void StartSafety(LoadingOverlayDriver d)
        {
            StopSafety();
            _safetyRoutine = d.StartCoroutine(SafetyRoutine(d));
        }

        private static void StopSafety()
        {
            if (_safetyRoutine != null && _driver != null)
                _driver.StopCoroutine(_safetyRoutine);
            _safetyRoutine = null;
        }

        private static IEnumerator SafetyRoutine(LoadingOverlayDriver d)
        {
            yield return new WaitForSecondsRealtime(SafetyRevealSeconds);
            _safetyRoutine = null;
            GICLog.Warn("[SceneFadeOverlay] 揭幕超时（场景加载失败？），自动揭幕兜底");
            Reveal(0.5f);
        }
    }
}
