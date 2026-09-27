using UnityEngine;
using UnityEngine.UI;
using GIC.Framework;

namespace GIC.Battle
{
    /// <summary>
    /// 选中提示特效：两束圆弧形光环绕飞行（2026-09-27 拍板「让两束光，环绕飞行选中的技能按钮」；
    /// 同日追加拍板：光束=圆弧形、贴图走 AI 生成——`Resources/UI/Battle/OrbitBeamArc.png`
    /// 为**图内两段对径对称的白光弧段**（拍板原话「应当是两个小段白光弧段，二者对称」；
    /// 同日目检后拍板换用 v2 彗尾版——头亮尾渐隐、尾迹拖旋转后方，同路径重生成替换），
    /// 运行时乘 tint 染元素色/队伍色，不再程序化生成）。
    /// 两版驱动同构：贴图弧段与画布同心 → 驱动=整图绕**环绕中心**旋转（两束随图对径同速飞行）。
    /// 技能按钮旧打钩图（SkillIconView.skillSelect）退役不再点亮。
    /// 光色由调用方定：技能按钮=选中单位元素色（ElementFactionConfig.GetElementColor）、
    /// 立牌底座圆盘=玩家队伍主色（BattlePalette 我方/敌方主色）。
    /// 运行时建件（AddComponent 脚本默认即生效，无 prefab 冻结；勿烘焙进 prefab——docs/14 §71）。
    /// </summary>

    /// <summary>UI 版：挂技能按钮（UGUI Image 光弧；Image 尺寸=2×环绕半径/光弧半径占比——
    /// 弧环落在目标环绕半径上，随按钮尺寸/布局缩放自适应）。消费方=BattleHud.SetAimSelectRing
    /// （瞄准态亮/灭，色=选中单位元素色）</summary>
    public class OrbitBeamsUi : MonoBehaviour
    {
        [Header("环绕光弧（技能按钮选中特效；比例×按钮短边，随布局缩放自适应）")]
        [Tooltip("环绕半径（按钮短边比例；弧带中心轨道半径）。0.575=可见弧带内缘贴按钮圈缘（按钮半径=短边之半 0.5；v2 彗尾贴图实测：环占比 0.692/内缘占比 0.602 折算；改大=离按钮远）")]
        [SerializeField] private float 环绕半径比例 = 0.575f;
        [Tooltip("环绕角速度（度/秒，两束随图对径同速）")]
        [SerializeField] private float 环绕角速度 = 240f;
        [Tooltip("光弧在贴图中的环半径占比（弧环圆心=贴图中心；实测=0.692（v2 彗尾，2026-09-27 重生成），换贴图按像素实测重算）")]
        [SerializeField] private float 光弧半径占比 = 0.692f;

        private RectTransform _beam;
        private float _angle;

        /// <summary>光弧贴图（Resources 共享资产；AI 生成两段对径对称白光弧段，运行时乘 tint 染色；两版组件共用）</summary>
        private static Sprite _arcSprite;
        internal static Sprite ArcSprite =>
            _arcSprite != null ? _arcSprite
            : (_arcSprite = Resources.Load<Sprite>("UI/Battle/OrbitBeamArc"));

        /// <summary>在技能按钮下建特效件（全拉伸壳；Image.raycastTarget 关勿拦点击/拖拽）</summary>
        public static OrbitBeamsUi Create(RectTransform button)
        {
            var go = new GameObject("OrbitBeams", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(button, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var fx = go.AddComponent<OrbitBeamsUi>();
            fx.Build();
            go.SetActive(false);
            return fx;
        }

        private void Build()
        {
            if (ArcSprite == null)
            {
                GICLog.Warn("[OrbitBeams] 光弧贴图未找到（Resources/UI/Battle/OrbitBeamArc），选中特效不显示");
                return;
            }
            var go = new GameObject("BeamArc", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(transform, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); // 中心锚：弧图整体绕按钮中心旋转
            var img = go.GetComponent<Image>();
            img.sprite = ArcSprite;
            img.raycastTarget = false;
            _beam = rt;
            Place(); // 首帧前的初始姿态
        }

        /// <summary>光色（元素色由调用方传入；两束同图同色）</summary>
        public void SetColor(Color color)
        {
            if (_beam != null) _beam.GetComponent<Image>().color = color;
        }

        private void Update()
        {
            if (_beam == null) return;
            _angle = Mathf.Repeat(_angle + 环绕角速度 * Time.deltaTime, 360f);
            Place();
        }

        private void Place()
        {
            var rect = (RectTransform)transform;
            float baseSize = Mathf.Min(rect.rect.width, rect.rect.height);
            // 弧图尺寸：贴图内弧环半径=占比×半图 → 半图=环绕半径/占比 → 全图=2×环绕半径/占比
            float size = Mathf.Max(1f, 2f * baseSize * 环绕半径比例 / Mathf.Max(0.01f, 光弧半径占比));
            _beam.anchoredPosition = Vector2.zero;   // 弧图居按钮中心
            _beam.localEulerAngles = new Vector3(0f, 0f, _angle); // 整图旋转=弧段绕心飞行（弧与贴图同心，几何天然保圆）
            _beam.sizeDelta = new Vector2(size, size);
        }
    }

    /// <summary>世界版：环绕立牌底座圆盘的两束圆弧光（平铺地面 quad，弧图绕盘心旋转）。
    /// 消费方=BattleHud 选中单位（色=队伍主色；贴片高度由 BattleHud 传 GetDecalHeight）</summary>
    public class OrbitBeamsWorld : MonoBehaviour
    {
        [Header("环绕光弧（底座圆盘选中特效；世界单位）")]
        [Tooltip("环绕半径（弧带中心轨道半径，世界单位）。0.242=可见弧带内缘贴底座圆盘缘（盘半径=UnitCylinderDiameter/2=0.21；v2 彗尾贴图实测同 UI 版口径折算——2026-09-27 追拍「紧紧贴着圆盘」；改大=离盘远）")]
        [SerializeField] private float 环绕半径 = 0.242f;
        [Tooltip("环绕角速度（度/秒，两束随图对径同速）。负值=修正向：Unity 左手系正 yaw 与 2D 正 z 旋向相反，同贴图取正会头尾倒置（尾在前），2026-09-27 目检实证后取负——头前尾后")]
        [SerializeField] private float 环绕角速度 = -150f;
        [Tooltip("光弧在贴图中的环半径占比（弧环圆心=贴图中心；实测=0.692（v2 彗尾，2026-09-27 重生成），换贴图按像素实测重算）")]
        [SerializeField] private float 光弧半径占比 = 0.692f;

        private Transform _beam;
        private Material _material; // 本组件持有，OnDestroy 释放——Destroy 物体不销材质（docs/14 §63①）
        private float _angle;

        public static OrbitBeamsWorld Create(Transform parent)
        {
            var go = new GameObject("DiscOrbitBeams");
            go.transform.SetParent(parent, false);
            var fx = go.AddComponent<OrbitBeamsWorld>();
            fx.Build();
            return fx;
        }

        private void Build()
        {
            var sprite = OrbitBeamsUi.ArcSprite; // 与 UI 版同贴图（两段对称白光弧，材质 tint 染色）
            if (sprite == null)
            {
                GICLog.Warn("[OrbitBeams] 光弧贴图未找到（Resources/UI/Battle/OrbitBeamArc），底座选中特效不显示");
                return;
            }
            _material = BattleViewFactory.CreateTransparentUnlitMaterial(Color.white);
            _material.mainTexture = sprite.texture;
            _beam = BattleViewFactory.CreateQuad(transform, "BeamArc", _material).transform;
            _beam.GetComponent<MeshRenderer>().sortingOrder = 1; // 瞄准贴片(0)之上、立牌(10)之下（金盘 2026-09-27 已退役）
            Place();
        }

        /// <summary>落位+光色（center=底座圆盘中心世界坐标；色=玩家队伍主色）</summary>
        public void Setup(Vector3 center, Color color)
        {
            transform.position = center;
            if (_material != null) _material.color = color;
        }

        private void Update()
        {
            if (_beam == null) return;
            _angle = Mathf.Repeat(_angle + 环绕角速度 * Time.deltaTime, 360f);
            Place();
        }

        private void Place()
        {
            float size = Mathf.Max(0.01f, 2f * 环绕半径 / Mathf.Max(0.01f, 光弧半径占比));
            // 平铺地面绕盘心飞行：quad 居盘心、整图绕竖轴旋转（Euler(90,yaw,0)=Ry(yaw)·Rx(90)——
            // Rx 平铺面朝上、Ry 绕盘心竖轴旋弧）、scale=弧图边长
            _beam.localPosition = Vector3.zero;
            _beam.localRotation = Quaternion.Euler(90f, _angle, 0f);
            _beam.localScale = new Vector3(size, size, 1f);
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }
    }
}
