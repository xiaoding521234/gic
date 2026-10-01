using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;
using TMPro;
using GIC.Data;
using GIC.Tool;
namespace GIC.Battle
{


    /// <summary>
    /// 单位立牌表现（双端同构表现层：Snapshot 建场、Segment 驱动动画，不持逻辑状态）。
    /// 风格参考饥荒：2D 立牌 + 底座投影；格子表现尺寸明显大于单位立牌。
    /// 立牌朝向 = 饥荒式"斜插卡片"：yaw 跟随相机（root，billboard），绕底边固定后倾（2026-09-18 拍板，
    /// 35°=90°−55°俯角，立牌面正对相机视线——完全垂直会被俯角透视压扁，与饥荒观感差异大的根因）。
    /// 头顶血条/元能条=屏幕空间层 BattleOverheadBars 显示（2026-09-24 拍板，原神式：不受遮挡+原神分隔线），
    /// 本组件只持数据（Hp/MaxHp/Energy/AttachedElement/OverheadBarAnchor）；
    /// 名字/Buff 徽章仍挂立牌倾斜组（世界空间 TMP + TextCombiner 本地化条目，docs/active/22 §13）。
    /// </summary>
    public class UnitView : MonoBehaviour
    {
        public string UnitId { get; private set; }
        public string DisplayName { get; private set; }
        public BattleCell Cell { get; set; }
        public bool IsCorpse { get; private set; }

        private SpriteRenderer _avatarRenderer;
        private Transform _baseDisc;
        private Color _baseColor = Color.white;

        /// <summary>受击圆柱直径（协议核心批 2026-09-29：per-unit 受击体——Create 传入，0=回落全局 0.42；
        /// 底座圆盘可视化同源=视觉即判定；OrbitBeamsWorld 选中弧光贴紧折算消费 BattleHud）</summary>
        private float _cylinderDiameter = BattleMetrics.UnitCylinderDiameter;
        public float CylinderDiameter => _cylinderDiameter;

        // 名字 + Buff 徽章行（血条/元能条视觉归 BattleOverheadBars 屏幕空间层，此处只存数据）
        private TextMeshPro _nameText;
        private TextCombiner _nameCombiner;
        private int _hp;
        private int _maxHp;

        /// <summary>队伍色（底座圆盘同源；2026-09-25 目检拍板：血条填充=玩家所选队伍色）</summary>
        public Color TeamColor => _baseColor;

        /// <summary>当前血量/上限（BattleOverheadBars 每帧拉取）</summary>
        public int Hp => _hp;
        public int MaxHp => _maxHp;

        /// <summary>头顶条锚点（世界坐标，含 55° 后仰偏移——与名字/Buff 徽章同一平面高度）</summary>
        public Vector3 OverheadBarAnchor =>
            _tiltGroup != null ? _tiltGroup.TransformPoint(new Vector3(0f, OverheadRowY(HpBarY), 0f)) : transform.position;

        // Buff 徽章（快照权威 + ApplyBuff/RemoveBuff 命令增量；图标=元素 Stroke 现成图）
        private readonly List<BuffState> _buffs = new List<BuffState>();
        private Transform _buffRow;

        // 元素附着（快照权威 + ElementAttach 命令增量，2026-09-22 接线）；视觉归 BattleOverheadBars（血条左缘，
        // 2026-09-24 随血条同改屏幕空间），此处只存状态
        private ElementType _attachedElement = ElementType.Physical;

        /// <summary>当前附着元素（Physical=无附着；BattleOverheadBars 每帧拉取）</summary>
        public ElementType AttachedElement => _attachedElement;

        /// <summary>立牌倾斜组（AvatarTilt：原点=格面底边，绕底边后仰）——血条/名字/Buff 行全部挂入，
        /// 与立牌同一平面同一旋转轴（2026-09-18 用户拍板：头顶信息一律随立牌倾斜）</summary>
        private Transform _tiltGroup;

        /// <summary>立牌后倾角缓存</summary>
        private float _tiltDegrees = 55f;

        /// <summary>立牌面内显示高度（Create 按 avatarScale 算出；头顶行 Y 以立牌顶为基准 + 原间隙，行尺寸不变）</summary>
        private float _avatarDisplayHeight = AvatarHeight;

        private const float AvatarHeight = 0.55f;

        // ==================== 序列帧 idle（B-S3 立牌动作段·AI 直出循环帧试点） ====================
        // 帧数组=UnitConfig 立牌动画帧（sheet 网格切片，各帧 rect 同格恒定 → 换帧 bounds 不跳、
        // 角色在格内的位置差即动画起伏）；随机相位=多单位不同步扇翼；冻结/尸体停摆（保留当前帧）
        private Sprite[] _idleFrames;
        private float _idleFps = 12f;
        private int _idleIndex;
        private float _idleTimer;

        // ==================== 立牌循环动画视频（B-S3 视频路线：绿幕 mp4 → VideoPlayer→RT → 运行时 ChromaKey 抠色） ====================
        // 显存恒定（流式解码+RT，与帧数无关）；优先级高于序列帧；随机相位与序列帧同语义；
        // 冻结/尸体 Pause 停摆（保留当前帧）；解码失败（errorReceived）回落静态立牌 sprite
        private VideoPlayer _videoPlayer;
        private RenderTexture _videoRt;
        private Material _videoMaterial; // ChromaKey 材质（本组件持有，OnDestroy 释放——docs/14 §63①）
        private bool _videoFailed;
        private bool _videoHalted;
        private VideoClip _idleVideoClip; // 待机片=回切目标（B-S4a 移动态接线，2026-09-29）
        private VideoClip _moveVideoClip; // 移动态片（null=无移动态动画，移动期间照播待机）

        // ==================== 立牌朝向（B-S4a 方向镜像，2026-09-29 拍板③：素材统一朝右单份复用） ====================
        // 行动方向 ∈ {上,左上,左,左下} → 朝左（水平镜像），其余 → 朝右；行动后保持（待机延续朝向）；
        // 只翻立牌本体（sprite+视频 quad），名字/Buff 行/底座盘不翻；后续背后刺杀技能可读 FaceLeft
        private bool _faceLeft;
        private Transform _avatarSpriteFlip;      // Avatar sprite（静态/序列帧共用渲染器宿主）
        private Vector3 _avatarSpriteFlipBaseScale;
        private Transform _avatarVideoFlip;       // AvatarVideo quad
        private Vector3 _avatarVideoFlipBaseScale;

        private void Update()
        {
            if (_videoPlayer != null)
            {
                if (IsCorpse || IsFrozen)
                {
                    if (_videoPlayer.isPlaying) { _videoPlayer.Pause(); _videoHalted = true; }
                }
                else if (_videoHalted && !_videoFailed)
                {
                    _videoPlayer.Play(); // 解冻自动恢复（尸体永不复苏=永不恢复）
                    _videoHalted = false;
                }
                return;
            }
            if (_idleFrames == null || _idleFrames.Length < 2 || _avatarRenderer == null) return;
            if (IsCorpse || IsFrozen) return; // 尸体灰显/冻结冰色时停摆（保留当前帧）
            _idleTimer += Time.deltaTime;
            float interval = 1f / _idleFps;
            while (_idleTimer >= interval)
            {
                _idleTimer -= interval;
                _idleIndex = (_idleIndex + 1) % _idleFrames.Length;
                if (_idleIndex == 0) _idleTimer = 0f; // 回绕丢弃余量，防长跑计时漂移
                _avatarRenderer.sprite = _idleFrames[_idleIndex];
            }
        }

        /// <summary>立牌动画视频播放失败兜底（平台解码失败/文件缺失等）：停播 + 禁用 ChromaKey quad、
        /// 恢复静态立牌 sprite（尺寸/贴地基准与视频路径同源，无感切换）</summary>
        private void OnVideoError(VideoPlayer source, string message)
        {
            if (_videoFailed) return;
            _videoFailed = true;
            Debug.LogWarning($"[UnitView] 立牌动画视频播放失败，回落静态立牌：{message}");
            source.Stop();
            source.gameObject.SetActive(false);
            if (_avatarRenderer != null) _avatarRenderer.enabled = true;
        }

        // ==================== 移动态动画切换（B-S4a 移动态接线，2026-09-29 拍板「正式化安柏待机+移动动画」） ====================

        /// <summary>移动态动画切换（BattlePlayer.PlayMoveCoroutine 移动片起止驱动）：true=播 移动动画视频、
        /// false=回 立牌动画视频。幂等（同片/无视频路径/解码失败早退）；换片随机相位（多枚同款单位不同步）；
        /// 冻结/尸体态不 Play（Update 停摆逻辑每帧接管，解冻自然恢复）</summary>
        public void SetMoveAnimation(bool moving)
        {
            var vp = _videoPlayer;
            if (vp == null || _videoFailed) return;
            var clip = moving ? _moveVideoClip : _idleVideoClip;
            if (clip == null || vp.clip == clip) return;
            vp.clip = clip;
            if (clip.length > 0.0)
                vp.time = UnityEngine.Random.Range(0f, (float)clip.length);
            if (IsCorpse || IsFrozen)
            {
                if (vp.isPlaying) { vp.Pause(); _videoHalted = true; }
            }
            else
            {
                vp.Play();
            }
        }

        // ==================== 立牌朝向（拍板③：方向镜像） ====================

        /// <summary>当前立牌朝向（朝左=素材水平镜像态；后续背后刺杀等方向判定技能读此值，权威态届时随 B7 进快照）</summary>
        public bool FaceLeft => _faceLeft;

        /// <summary>设置立牌朝向（2026-09-29 拍板③）：faceLeft=true 立牌本体水平镜像（素材统一朝右，反方向不生成第二份）；
        /// 幂等；只翻 sprite 与视频 quad（负 localScale.x——视频 shader 已 Cull Off 防翻面剔除），
        /// 名字/Buff 行/底座盘不动；行动后保持=待机延续朝向</summary>
        public void SetFacing(bool faceLeft)
        {
            if (_faceLeft == faceLeft) return;
            _faceLeft = faceLeft;
            ApplyFacing();
        }

        private void ApplyFacing()
        {
            float sign = _faceLeft ? -1f : 1f;
            if (_avatarSpriteFlip != null)
                _avatarSpriteFlip.localScale = new Vector3(
                    sign * _avatarSpriteFlipBaseScale.x, _avatarSpriteFlipBaseScale.y, _avatarSpriteFlipBaseScale.z);
            if (_avatarVideoFlip != null)
                _avatarVideoFlip.localScale = new Vector3(
                    sign * _avatarVideoFlipBaseScale.x, _avatarVideoFlipBaseScale.y, _avatarVideoFlipBaseScale.z);
        }

        // 名字/Buff 行布局常量（**面内高度**：沿倾斜组 local Y，随立牌后仰；立牌本体 0~_avatarDisplayHeight，
        // 全身放大时行 Y 随立牌顶同步抬高、行自身尺寸不变；HpBarY 仅存为头顶条锚点高度——条状视觉已上移屏幕空间层）
        private const float HpBarY = 0.66f;
        private const float NameY = 0.84f;
        private const float BuffRowY = 1.08f;
        private const float BuffBadgeSize = 0.22f;
        private const float BuffBadgeGap = 0.28f;
        private const float NameFontSize = 36f;   // 世界高度 ≈ 3.43 × scale
        private const float NameScale = 0.038f;   // → 约 0.13 世界高

        /// <summary>底座盘不透明度（2026-09-27 拍板半透明投影感：实体色板读作「坑/板」，脚站盘心显陷地
        /// ——主因归圆盘（用户目检）；受击圆柱判定语义不变仅观感透明化。调 0=隐藏盘）</summary>
        private const float 底座盘不透明度 = 0.7f;

        /// <summary>底座盘颜色写入单出口（alpha 恒=底座盘不透明度——冻结/常态回写防满 alpha 复辟实体观感）</summary>
        private static Color WithDiscAlpha(Color c) => new Color(c.r, c.g, c.b, 底座盘不透明度);

        /// <summary>头顶行面内 Y：立牌顶 + 与原 0.55 高版本相同的间隙（全身放大仅抬高位置）</summary>
        private float OverheadRowY(float baseY) => _avatarDisplayHeight + (baseY - AvatarHeight);

        // 配色统一走 BattlePalette 配置资产（2026-09-18 统一化批次；原 static 字面量收口，同语义不同值已对齐）
        private static BattlePalette Palette => BattlePalette.Instance;

        // 世界层运行时材质（工厂出品由本组件持有，OnDestroy 释放——Destroy 物体不销材质，docs/14 §63①）
        private Material _baseDiscMaterial;

        private void OnDestroy()
        {
            if (_baseDiscMaterial != null) Destroy(_baseDiscMaterial);
            if (_videoMaterial != null) Destroy(_videoMaterial); // ChromaKey 材质（调用方持有纪律，docs/14 §63①）
            if (_videoRt != null) { _videoRt.Release(); Destroy(_videoRt); } // RT 随单位释放（视频路线显存恒定的收口）
        }

        /// <summary>
        /// 创建立牌（头像 SpriteRenderer + 阵营色底座 + 头顶血条/单位名）
        /// </summary>
        /// <param name="tiltDegrees">立牌后仰角（饥荒式"斜插卡片"：相机固定俯角 55°，立牌向后仰倾角=俯角时
        /// 立牌面恰好正对视线（完全消俯视压扁），与地面夹角=90°−倾角；2026-09-18 两轮目检修正：方向=顶部
        /// 远离相机后仰，正对值=55°）</param>
        /// <param name="nameEntry">单位名本地化条目（UnitName.GetEntry()；null 时回退 displayName 静态文本）</param>
        /// <param name="hp">初始血量</param>
        /// <param name="maxHp">最大血量</param>
        /// <param name="avatarScale">立牌整体放大倍数（1=头像版原尺寸）：全身立绘人物在图中占比小，放大对齐
        /// 头像版人物观感——底边原点贴地不漂移；血条/名字/Buff 行尺寸不变、随立牌顶同步抬高；
        /// B5 判定圆柱与底座不受视觉放大影响</param>
        /// <param name="idleFrames">立牌循环动画帧（B-S3 立牌动作段：AI 直出 sheet 网格切片，按序循环；
        /// null/空=静态立牌兜底。各帧 rect 同格恒定 → 缩放/贴地基准取帧 0，换帧不跳 bounds）</param>
        /// <param name="idleFps">动画播放帧率（fps）</param>
        /// <param name="idleVideo">立牌循环动画视频（B-S3 视频路线：绿幕 mp4+运行时 ChromaKey 抠色；
        /// 优先级高于 idleFrames——配了视频的单位不再消费序列帧；ChromaKey shader 缺失时回落静态立牌）</param>
        /// <param name="moveVideo">移动中循环动画视频（B-S4a 移动态接线：移动命令片内播放、片末回 idleVideo；
        /// null=无移动态动画，待机片常驻=旧行为）</param>
        /// <param name="hoverHeight">立牌离地高度（世界单位=格；UnitData.离地高度，2026-09-27 拍板新增）：
        /// 纸片人整体上浮——飞行/悬浮单位；底座圆盘留地面（受击圆柱可视化=视觉即判定不随浮空）；
        /// 血条/名字/Buff 行挂倾斜组随浮空同步抬高</param>
        public static UnitView Create(Transform parent, string unitId, string displayName, Sprite avatar, Color teamColor,
            Quaternion billboardRotation, float tiltDegrees = 55f, TextEntry nameEntry = null, int hp = 0, int maxHp = 0,
            float avatarScale = 1f, Sprite[] idleFrames = null, float idleFps = 12f, VideoClip idleVideo = null,
            VideoClip moveVideo = null, float hoverHeight = 0f, float cylinderDiameter = 0f)
        {
            var root = new GameObject($"UnitView_{unitId}");
            root.transform.SetParent(parent, false);
            root.transform.rotation = billboardRotation;

            var view = root.AddComponent<UnitView>();
            view.UnitId = unitId;
            view.DisplayName = displayName;
            view._tiltDegrees = tiltDegrees;

            // 头像立牌（SpriteRenderer 自动处理图集 UV）：
            // 外层 AvatarTilt 原点=格面底边（旋转轴=底边），内层挂 sprite 居于半高处——
            // 后仰时立牌绕底边倒（底边保持贴地），非绕中心转（那会让底边翘起/插地）。
            // 血条/名字/Buff 行同挂此倾斜组（用户拍板：头顶信息随立牌同平面后仰）。
            // 离地高度（2026-09-27 拍板）：倾斜组整体上浮 hoverHeight——飞行/悬浮单位（安柏=0.5），
            // 底座圆盘是 root 子级不随浮空（受击圆柱可视化=视觉即判定，判定恒在地面格）
            var avatarGo = new GameObject("AvatarTilt");
            avatarGo.transform.SetParent(root.transform, false);
            avatarGo.transform.localPosition = new Vector3(0f, hoverHeight, 0f);
            avatarGo.transform.localRotation = Quaternion.Euler(tiltDegrees, 0f, 0f);
            view._tiltGroup = avatarGo.transform;

            var spriteGo = new GameObject("Avatar");
            spriteGo.transform.SetParent(avatarGo.transform, false);
            view._avatarRenderer = spriteGo.AddComponent<SpriteRenderer>();
            // 有帧数组时以帧 0 为基准 sprite（同格恒定 bounds）：缩放/贴地/头顶行都按它算，
            // 角色在格内的位置差即飞行动画的自然起伏（格底贴地 → 飞行单位悬停属正确语义）
            bool hasIdle = idleFrames != null && idleFrames.Length > 1;
            var baseSprite = hasIdle ? idleFrames[0] : avatar;
            view._avatarRenderer.sprite = baseSprite;
            view._avatarRenderer.sortingOrder = BattleMetrics.AvatarSortingOrder;

            if (baseSprite != null)
            {
                float displayHeight = AvatarHeight * avatarScale;
                float worldHeight = baseSprite.bounds.size.y;
                float scale = worldHeight > 0f ? displayHeight / worldHeight : 1f;
                spriteGo.transform.localScale = Vector3.one * scale;
                // sprite 中心置于半高处（外层原点=底边 → 底边贴地、立牌居中于半高）
                spriteGo.transform.localPosition = new Vector3(0f, displayHeight * 0.5f, 0f);
                view._avatarDisplayHeight = displayHeight;
                // 朝向镜像锚点（拍板③）：记 base scale 供 SetFacing 翻 x（默认朝右=base 不动）
                view._avatarSpriteFlip = spriteGo.transform;
                view._avatarSpriteFlipBaseScale = spriteGo.transform.localScale;
            }

            if (hasIdle)
            {
                view._idleFrames = idleFrames;
                view._idleFps = Mathf.Max(1f, idleFps);
                // 随机相位：多枚同款单位不同步扇翼（两枚安柏测试军互错开即目检点）
                view._idleIndex = UnityEngine.Random.Range(0, idleFrames.Length);
                view._avatarRenderer.sprite = idleFrames[view._idleIndex];
            }

            // 立牌循环动画视频（B-S3 视频路线）：绿幕 mp4 → VideoPlayer→RT → ChromaKey quad 运行时抠色。
            // 静态 sprite（上方已按同尺寸建好）保留作兜底但禁用——解码失败时 OnVideoError 恢复；
            // shader 缺失（构建剥离防线）时整块跳过=自然回落静态立牌
            if (idleVideo != null && BattleViewFactory.ChromaKeyShader != null)
            {
                view._avatarRenderer.enabled = false;
                float videoDisplayHeight = AvatarHeight * avatarScale;
                float videoAspect = idleVideo.height > 0 ? idleVideo.width / (float)idleVideo.height : 1f;
                view._videoRt = new RenderTexture((int)idleVideo.width, (int)idleVideo.height, 0,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                view._videoMaterial = BattleViewFactory.CreateChromaKeyMaterial();
                view._videoMaterial.mainTexture = view._videoRt;

                var videoGo = BattleViewFactory.CreateQuad(view._tiltGroup, "AvatarVideo", view._videoMaterial);
                // 与 sprite 路径同基准：全画布高=displayHeight、中心悬于半高处（底边贴地）
                videoGo.transform.localPosition = new Vector3(0f, videoDisplayHeight * 0.5f, 0f);
                videoGo.transform.localScale = new Vector3(videoDisplayHeight * videoAspect, videoDisplayHeight, 1f);
                videoGo.GetComponent<MeshRenderer>().sortingOrder = BattleMetrics.AvatarSortingOrder; // 与立牌 sprite 同序（跨单位立牌遮挡排序语义一致）
                // 朝向镜像锚点（拍板③）：记 base scale 供 SetFacing 翻 x（视频 quad 依赖 shader Cull Off）
                view._avatarVideoFlip = videoGo.transform;
                view._avatarVideoFlipBaseScale = videoGo.transform.localScale;

                var vp = videoGo.AddComponent<VideoPlayer>();
                vp.playOnAwake = false;
                vp.clip = idleVideo;
                vp.renderMode = VideoRenderMode.RenderTexture;
                vp.targetTexture = view._videoRt;
                vp.isLooping = true;
                vp.audioOutputMode = VideoAudioOutputMode.None;
                // 随机相位：与序列帧路径同语义（多枚同款单位错开扇翼）
                if (idleVideo.length > 0.0)
                    vp.time = UnityEngine.Random.Range(0f, (float)idleVideo.length);
                vp.errorReceived += view.OnVideoError;
                vp.Play();
                view._videoPlayer = vp;
                view._idleVideoClip = idleVideo; // 待机/回切目标片（B-S4a 移动态接线）
                view._moveVideoClip = moveVideo;
            }

            // 阵营色底座圆盘（B5 连续判定：受击圆柱的可视化——直径=该单位受击圆柱直径
            // （协议核心批 per-unit：默认 0.42/协议核心 0.8），视觉即判定，docs/18 决策二）。
            // 半透明投影感（2026-09-27 拍板：实体色板读作「坑/板」，
            // 脚站盘心显陷地——主因归圆盘，用户目检归因）；sortingOrder=-1 恒先画于一切 3000 透明件
            // （瞄准贴片 0/立牌 10/箭矢 12）之下、水面（2999）之上
            view._cylinderDiameter = cylinderDiameter > 0f ? cylinderDiameter : BattleMetrics.UnitCylinderDiameter;
            view._baseDiscMaterial = BattleViewFactory.CreateTransparentUnlitMaterial(WithDiscAlpha(teamColor));
            var baseGo = BattleViewFactory.CreateDisc(root.transform, "BaseDisc", view._baseDiscMaterial);
            baseGo.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            baseGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            baseGo.transform.localScale = new Vector3(view._cylinderDiameter, view._cylinderDiameter, 1f);
            baseGo.GetComponent<MeshRenderer>().sortingOrder = BattleMetrics.BaseDiscSortingOrder;
            view._baseDisc = baseGo.transform;
            view._baseColor = teamColor;

            view.BuildName(nameEntry);
            view.BuildBuffRow();
            view.SetHp(hp, maxHp);

            return view;
        }

        /// <summary>附着元素同步（快照权威 + ElementAttach 命令增量；覆盖语义后到者胜）。
        /// 视觉归 BattleOverheadBars（血条左缘图标，2026-09-24 随血条同改屏幕空间），此处只存状态</summary>
        public void SetAttachedElement(ElementType element)
        {
            _attachedElement = element;
        }

        /// <summary>单位名：世界空间 TextMeshPro + TextCombiner 同物体（语言切换自动刷新）。
        /// 挂立牌倾斜组，与立牌同平面（用户拍板 2026-09-18：头顶信息一律随立牌倾斜）</summary>
        private void BuildName(TextEntry nameEntry)
        {
            var nameGo = new GameObject("NameText");
            nameGo.transform.SetParent(_tiltGroup, false);
            nameGo.transform.localPosition = new Vector3(0f, OverheadRowY(NameY), 0f);
            nameGo.transform.localScale = Vector3.one * NameScale;

            _nameText = nameGo.AddComponent<TextMeshPro>();
            _nameText.font = BattleViewFactory.WorldTextFont;
            _nameText.fontSize = NameFontSize;
            _nameText.alignment = TextAlignmentOptions.Center;
            _nameText.enableWordWrapping = false;
            _nameText.color = Palette.文字米白;
            var rect = (RectTransform)nameGo.transform;
            rect.sizeDelta = new Vector2(40f, 14f);

            _nameCombiner = nameGo.AddComponent<TextCombiner>();
            if (nameEntry != null)
                _nameCombiner.AddEntry(nameEntry);
            else
                _nameCombiner.AddStaticEntry(DisplayName);
        }

        /// <summary>
        /// 应用格位 + 队形偏移（世界坐标由 BattleBoard 换算）
        /// </summary>
        public void ApplyPosition(Vector3 worldPosition)
        {
            transform.position = worldPosition;
        }

        /// <summary>
        /// 尸体灰显（docs/05 §5.4：血量 0 永不复苏，立牌变灰）
        /// </summary>
        public void SetCorpseVisual(bool corpse)
        {
            IsCorpse = corpse;
            RefreshTint();
            if (_baseDisc != null)
                _baseDisc.localScale = corpse
                    ? new Vector3(_cylinderDiameter, 0.28f, 1f) // 尸体底座压扁（压扁值沿用旧观感）
                    : new Vector3(_cylinderDiameter, _cylinderDiameter, 1f);
        }

        /// <summary>冻结态（B4：水+冰反应）：立牌冰色 tint + 底座冰色（快照权威同步）</summary>
        public bool IsFrozen { get; private set; }

        public void SetFrozenVisual(bool frozen)
        {
            IsFrozen = frozen;
            RefreshTint();
            if (_baseDisc != null && _baseDisc.GetComponent<MeshRenderer>() != null)
                _baseDisc.GetComponent<MeshRenderer>().sharedMaterial.color =
                    WithDiscAlpha(frozen ? Palette.冻结冰色 : _baseColor); // 保盘半透明（2026-09-27 拍板，回写防满 alpha）
        }

        /// <summary>立牌着色统一收口：尸体灰 > 冻结冰色 > 受击闪红 > 常态白</summary>
        private void RefreshTint()
        {
            if (_avatarRenderer == null) return;
            Color tint = IsCorpse ? Palette.立牌尸体灰 : IsFrozen ? Palette.冻结冰色 : Color.white;
            _avatarRenderer.color = tint;
            if (_videoMaterial != null)
                _videoMaterial.color = tint; // 视频路径同 tint（ChromaKey _Color，同 SpriteRenderer.color 语义）
            if (_nameText != null)
                _nameText.color = IsCorpse ? Palette.名字尸体灰 : Palette.文字米白;
        }

        /// <summary>
        /// 受击闪红
        /// </summary>
        public void FlashHit()
        {
            if (_avatarRenderer != null && !IsCorpse)
                _avatarRenderer.color = Palette.受击闪红;
            if (_videoMaterial != null && !IsCorpse)
                _videoMaterial.color = Palette.受击闪红; // 视频路径同步闪红
        }

        public void RestoreColor()
        {
            RefreshTint();
        }

        // ==================== 头顶血量（快照权威 + 伤害/治疗命令增量驱动） ====================

        /// <summary>快照同步血量（权威值，选择阶段头/开局调用；视觉=BattleOverheadBars 每帧拉取 Hp/MaxHp）</summary>
        public void SetHp(int hp, int maxHp)
        {
            _hp = hp;
            _maxHp = maxHp;
        }

        /// <summary>命令流增量血量（Damage/Heal 播放期间即时反馈；下个快照自然校正）</summary>
        public void ApplyHpDelta(int delta)
        {
            _hp = Mathf.Clamp(_hp + delta, 0, Mathf.Max(1, _maxHp));
        }

        // ==================== 元能（B6a：快照权威 + StatChange 命令增量；视觉=BattleOverheadBars 元能条，2026-09-24） ====================

        public int EnergyCurrent { get; private set; }
        public int EnergyMax { get; private set; }

        /// <summary>快照权威同步元能（选择阶段头/开局；HUD 爆发键门控读此缓存）</summary>
        public void SetEnergy(int current, int max)
        {
            EnergyCurrent = current;
            EnergyMax = max;
        }

        /// <summary>命令流增量元能（StatChange·StatKindEnergy；下个快照自然校正）</summary>
        public void ApplyEnergyDelta(int delta)
        {
            EnergyCurrent = Mathf.Clamp(EnergyCurrent + delta, 0, Mathf.Max(0, EnergyMax));
        }

        // ==================== 理智（2026-09-30 歌声之环批：快照权威 + StatChange(Sanity) 命令增量；
        // 视觉消费方随未来理智机制批，本批打通数据链——B7 联机随快照/命令自动同步） ====================

        public int SanityCurrent { get; private set; }

        /// <summary>快照权威同步理智（选择阶段头/开局）</summary>
        public void SetSanity(int current)
        {
            SanityCurrent = current;
        }

        /// <summary>命令流增量理智（StatChange·StatKindSanity；下个快照自然校正；钳 -300~300 同 UnitStats）</summary>
        public void ApplySanityDelta(int delta)
        {
            SanityCurrent = Mathf.Clamp(SanityCurrent + delta, -300, 300);
        }

        // ==================== 头顶 Buff 徽章（B2；快照权威 + 命令流增量） ====================

        /// <summary>Buff 徽章行容器：挂立牌倾斜组，与立牌同平面同一旋转轴（2026-09-18 用户目检：
        /// 火图标应和立牌一样倾斜，勿正对相机平放；拍板"都应当斜"→ 血条/名字/Buff 行全挂倾斜组）</summary>
        private void BuildBuffRow()
        {
            var rowGo = new GameObject("BuffRow");
            rowGo.transform.SetParent(_tiltGroup, false);
            rowGo.transform.localPosition = new Vector3(0f, OverheadRowY(BuffRowY), 0f);
            _buffRow = rowGo.transform;
        }

        /// <summary>快照权威同步（选择阶段头/开局）</summary>
        public void SetBuffs(List<BuffState> buffs)
        {
            _buffs.Clear();
            if (buffs != null)
                foreach (var b in buffs)
                    _buffs.Add(new BuffState { type = b.type, level = b.level, remainingTurns = b.remainingTurns });
            RebuildBuffBadges();
        }

        /// <summary>命令流增量：施加/刷新（同类已存在=更新回合数）</summary>
        public void ApplyBuffBadge(int type, int turns)
        {
            var existing = _buffs.Find(b => b.type == type);
            if (existing != null)
            {
                existing.remainingTurns = turns;
            }
            else
            {
                _buffs.Add(new BuffState { type = type, level = 1, remainingTurns = turns });
            }
            RebuildBuffBadges();
        }

        /// <summary>命令流增量：移除（到期/驱散）</summary>
        public void RemoveBuffBadge(int type)
        {
            _buffs.RemoveAll(b => b.type == type);
            RebuildBuffBadges();
        }

        private void RebuildBuffBadges()
        {
            if (_buffRow == null) return;
            foreach (Transform child in _buffRow)
                Destroy(child.gameObject);

            int count = _buffs.Count;
            for (int i = 0; i < count; i++)
            {
                var buff = _buffs[i];
                var icon = BuffIconOf(buff.type);
                if (icon == null) continue;

                float x = (i - (count - 1) * 0.5f) * BuffBadgeGap;

                var iconGo = new GameObject($"Buff_{buff.type}");
                iconGo.transform.SetParent(_buffRow, false);
                iconGo.transform.localPosition = new Vector3(x, 0f, 0f);
                var renderer = iconGo.AddComponent<SpriteRenderer>();
                renderer.sprite = icon;
                renderer.sortingOrder = BattleMetrics.BuffBadgeSortingOrder;
                float worldHeight = icon.bounds.size.y;
                if (worldHeight > 0f)
                    iconGo.transform.localScale = Vector3.one * (BuffBadgeSize / worldHeight);

                // 剩余回合角标（右下小数字；世界 TMP=工厂字体链，fontSize×scale×0.1≈原 TextMesh characterSize 同高）；
                // 永久 Buff（remainingTurns<0，如歌声之环）不计时——无角标
                if (buff.remainingTurns > 0)
                {
                    var turnsGo = new GameObject("Turns");
                    turnsGo.transform.SetParent(iconGo.transform, false);
                    turnsGo.transform.localPosition = new Vector3(0.14f, -0.14f, -0.01f);
                    turnsGo.transform.localScale = Vector3.one * 0.05f;
                    var turnsText = turnsGo.AddComponent<TextMeshPro>();
                    turnsText.font = BattleViewFactory.WorldTextFont;
                    turnsText.fontSize = 32;
                    turnsText.alignment = TextAlignmentOptions.Center;
                    turnsText.enableWordWrapping = false;
                    ((RectTransform)turnsGo.transform).sizeDelta = new Vector3(20f, 5f);
                    turnsText.text = buff.remainingTurns.ToString();
                    turnsText.color = Palette.文字米白;
                }
            }
        }

        /// <summary>Buff 类型 → 图标（元素 Stroke 现成图；B4 反应批次按类型扩充映射）</summary>
        private static Sprite BuffIconOf(int buffType)
        {
            var config = ElementFactionConfig.Instance;
            if (config == null) return null;
            switch ((BuffType)buffType)
            {
                case BuffType.Burn: return config.GetElementIconStroke(ElementType.Pyro);
                case BuffType.Freeze: return config.GetElementIconStroke(ElementType.Cryo);
                case BuffType.AttackUp: return config.GetElementIconStroke(ElementType.Anemo); // 占位：延奏=蒙德协奏（风）；正式图标待拍板（docs/11）
                case BuffType.MoveSpeedUp: return config.GetElementIconStroke(ElementType.Anemo); // 占位：移速提升（风系语义）；正式图标待拍板（docs/11）
                case BuffType.SongOfLife: return config.GetElementIconStroke(ElementType.Hydro); // 占位：歌声之环=水光环（B-3 ②）；正式图标待拍板（docs/11）
                case BuffType.Icicle: return config.GetElementIconStroke(ElementType.Cryo); // 占位：寒冰之棱=冰（凛冽轮舞批）；正式图标待拍板（docs/11）
                case BuffType.DefenseDown: return config.GetElementIconStroke(ElementType.Cryo); // 占位：防御减少（寒冰之棱2命）；正式图标待拍板（docs/11）
                default: return null;
            }
        }
    }
}
