using UnityEngine;

namespace GIC.Data
{
    /// <summary>
    /// 星级视觉参数统一配置 — 颜色、时间、强度
    /// ScriptableObject，放在 Resources/Configs/StarVisualConfig.asset
    /// 所有静态方法/属性自动通过 Instance 访问，调用方无需改动
    /// </summary>
    [CreateAssetMenu(fileName = "StarVisualConfig", menuName = "GIC/Star Visual Config")]
    public class StarVisualConfig : ScriptableObject
    {
        [Header("星级颜色")]
        [SerializeField] private Color star1Color = new Color(0.298f, 0.686f, 0.314f, 1f); // 4CAF50
        [SerializeField] private Color star2Color = new Color(0.129f, 0.588f, 0.953f, 1f); // 2196F3
        [SerializeField] private Color star3Color = new Color(0.612f, 0.153f, 0.690f, 1f); // 9C27B0
        [SerializeField] private Color star4Color = new Color(1.000f, 0.596f, 0.000f, 1f); // FF9800
        [SerializeField] private Color star5Color = new Color(0.957f, 0.263f, 0.212f, 1f); // F44336

        [Header("祈愿停留时间")]
        [SerializeField] private float holdBaseTime = 0.3f;
        [SerializeField] private float holdTimePerStar = 0.175f;

        [Header("射击冷却")]
        [SerializeField] private float baseShotCooldown = 0.3f;
        [SerializeField] private float cooldownPerStar = 0.15f;

        [Header("相遇抖动")]
        [SerializeField] private float shakeBaseDuration = 0.8f;
        [SerializeField] private float shakeDurationPerStar = 0.3f;
        [SerializeField] private float shakeInterval = 0.06f;
        [SerializeField] private float shakeBaseIntensity = 10f;
        [SerializeField] private float shakeIntensityPerStar = 8f;

        [Header("发光叠加")]
        [SerializeField] private float glowBaseIntensity = 0.4f;
        [SerializeField] private float glowIntensityPerStar = 0.18f;
        [SerializeField] private Color encounterGlowColor = new Color(1f, 0.95f, 0.6f, 1f);

        [Header("边缘泛光")]
        [SerializeField] private float edgeGlowBaseAlpha = 0.3f;
        [SerializeField] private float edgeGlowAlphaPerStar = 0.18f;
        [SerializeField] private float edgeGlowMaxAlpha = 1.0f;
        [SerializeField] private float edgeGlowDuration = 0.8f;
        [SerializeField] private float edgeGlowBaseWidth = 0.08f;
        [SerializeField] private float edgeGlowWidthPerStar = 0.04f;

        // ── 单例（由 ConfigManager.Init [PostConstruct] 注入；未注入时按同路径 Resources 兜底，防异常时序/编辑器工具 NRE）──

        private static StarVisualConfig _instance;
        private static bool _instanceLoadAttempted;

        public static StarVisualConfig Instance
        {
            get
            {
                if (_instance == null && !_instanceLoadAttempted)
                {
                    _instanceLoadAttempted = true;
                    _instance = Resources.Load<StarVisualConfig>("Configs/StarVisualConfig");
                    if (_instance == null)
                        Debug.LogWarning("[StarVisualConfig] 未注入且 Resources 无此资产，星级视觉参数不可用");
                }
                return _instance;
            }
        }

        /// <summary>由 ConfigManager.Start() 调用注入</summary>
        public static void Initialize(StarVisualConfig config) => _instance = config;

        // ── 静态属性（保持调用方不变）──

        public static float BaseShotCooldown => Instance.baseShotCooldown;
        public static float ShakeBaseDuration => Instance.shakeBaseDuration;
        public static float ShakeInterval => Instance.shakeInterval;
        public static Color EncounterGlowColor => Instance.encounterGlowColor;
        public static float EdgeGlowMaxAlpha => Instance.edgeGlowMaxAlpha;
        public static float EdgeGlowDuration => Instance.edgeGlowDuration;

        // ── 静态方法 ──

        public static Color GetStarColor(int star)
        {
            var cfg = Instance;
            if (cfg == null) return Color.white;
            return star switch
            {
                1 => cfg.star1Color,
                2 => cfg.star2Color,
                3 => cfg.star3Color,
                4 => cfg.star4Color,
                5 => cfg.star5Color,
                _ => cfg.star1Color
            };
        }

        public static float GetHoldTime(int starLevel) => Instance.holdBaseTime + (starLevel - 1) * Instance.holdTimePerStar;

        public static float GetShotCooldown(int starLevel) => Instance.baseShotCooldown + starLevel * Instance.cooldownPerStar;

        public static float GetShakeDuration(int starLevel) => Instance.shakeBaseDuration * (1f + (starLevel - 1) * Instance.shakeDurationPerStar);

        public static float GetShakeIntensity(int starLevel) => Instance.shakeBaseIntensity + starLevel * Instance.shakeIntensityPerStar;

        public static float GetMaxGlow(int starLevel) => Instance.glowBaseIntensity + starLevel * Instance.glowIntensityPerStar;

        public static float GetEdgeGlowAlpha(int starLevel) => Instance.edgeGlowBaseAlpha + starLevel * Instance.edgeGlowAlphaPerStar;

        public static float GetEdgeGlowWidth(int starLevel) => Instance.edgeGlowBaseWidth + (starLevel - 1) * Instance.edgeGlowWidthPerStar;
    }
}
