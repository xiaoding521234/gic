using System;
using System.Collections;
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

        /// <summary>立牌底世界高度（含悬浮离地——AvatarTilt 世界 y；无倾斜组回落根 y）：箭矢等贴牌
        /// 视觉件的起算基准——投射物箭高从立牌底起算=预览贴地坐标系校准值实战直接复用
        /// （2026-10-05 箭高校准悬浮锚定：UnitView 根只到格面，悬浮在 AvatarTilt 子物体上）</summary>
        public float AvatarBaseWorldY => _tiltGroup != null ? _tiltGroup.position.y : transform.position.y;

        /// <summary>投射物发射弓位锚点（2026-10-05 十轮定案「箭和立牌同一平面」）：把「箭高」（**面内语义**=
        /// tiltGroup 局部 y，时轮预览贴弓直读）沿面片取世界点=**箭矢飞行起点本体**；BattlePlayer 终点 z
        /// 同加面前伸量→两端同面同深=东西向屏幕水平直线且与立牌共面（六报「斜着飞」根因=当年终点落
        /// 格心、两端深度差所致；八轮「格心起飞+屏幕解算」案被「箭不在立牌平面上」报障推翻——教训=
        /// 两端必须同面同深勿混合）</summary>
        public Vector3 ProjectileOriginWorld(float facialHeight)
        {
            if (_tiltGroup != null)
                return _tiltGroup.TransformPoint(new Vector3(0f, facialHeight, 0f));
            return transform.position + new Vector3(0f, facialHeight, 0f);
        }

        /// <summary>立牌面内显示高度（Create 按 avatarScale 算出；头顶行 Y 以立牌顶为基准 + 原间隙，行尺寸不变）</summary>
        private float _avatarDisplayHeight = AvatarHeight;

        private const float AvatarHeight = 0.55f;

        // ==================== 立牌循环动画视频（B-S3 视频路线：绿幕 mp4 → VideoPlayer→RT → 运行时 ChromaKey 抠色） ====================
        // 显存恒定（流式解码+RT，与帧数无关）；随机相位（多枚同款单位错开扇翼）；
        // 冻结/尸体 Pause 停摆（保留当前帧）；解码失败（errorReceived）回落静态立牌 sprite。
        // 序列帧 idle 路线已退役（2026-10-05 拍板全库移除——视频路线前的旧尝试遗留，零单位在用）
        private VideoPlayer _videoPlayer;
        private RenderTexture _videoRt;
        private Material _videoMaterial; // ChromaKey 材质（本组件持有，OnDestroy 释放——docs/14 §63①）
        private bool _videoFailed;
        private bool _videoHalted;
        // 动作片 RT 池（2026-10-05 决策四十五：per-clip 池化+建场预热首帧——「首次施放/移动黑屏」根因=
        // 新分配 RT 恒黑 + VideoPlayer 首开解码 ~100-300ms 才落首帧；PrewarmActionClips 建场预解码首帧
        // 入池，PlayActionVideo 按片取 RT：预热命中=已带首帧零黑屏、未命中=现建回落旧行为兜底。
        // 池化顺带消除旧单槽 RT 的换片重建（move 768² 与战技 1344×768 交替播曾每次重建））
        private readonly Dictionary<VideoClip, RenderTexture> _actionRts = new Dictionary<VideoClip, RenderTexture>();
        private VideoClip _idleVideoClip; // 待机片=回切目标（B-S4a 移动态接线，2026-09-29）
        // 移动循环片（2026-10-05 决策四十四「尽可能统一」：per-skill——Move 型技能 SkillData.动作视频 经
        // SkillCast 登记（SetMoveVideo），Move 命令片起止 SetMoveAnimation 消费；走与一次性动作片同款
        // 双 RT+缩放补偿+位置偏移+校准速度机件，仅语义为循环态（随机相位、不自动回切）。
        // null=无移动片=移动期间照播待机（旧行为））
        private VideoClip _moveClip;
        private float _moveSpeed = 1f;     // 登记时已乘战斗回放速度（与一次性动作片口径一致）
        private float _moveScaleComp = 1f; // 缩放补偿（idle 主体高/移动片主体高）
        private Vector2 _moveOffset;       // 位置偏移
        private bool _moveLoopActive;      // 移动循环态在播（SetMoveAnimation(false) 精确归位，防无谓相位跳）

        // ==================== 立牌朝向（B-S4a 方向镜像，2026-09-29 拍板③：素材统一朝右单份复用） ====================
        // 行动方向 ∈ {上,左上,左,左下} → 朝左（水平镜像），其余 → 朝右；行动后保持（待机延续朝向）；
        // 只翻立牌本体（sprite+视频 quad），名字/Buff 行/底座盘不翻；后续背后刺杀技能可读 FaceLeft
        private bool _faceLeft;
        private Transform _avatarSpriteFlip;      // Avatar sprite（静态立牌渲染器宿主）
        private Vector3 _avatarSpriteFlipBaseScale;
        private Transform _avatarVideoFlip;       // AvatarVideo quad
        private Vector3 _avatarVideoFlipBaseScale;
        private Vector3 _avatarVideoFlipBasePosition; // 视频 quad 基准位（B-S4c 校准：动作片位置偏移的回退锚点）

        private void Update()
        {
            // 冻结中持续刷新霜化锚点：被击退/牵引等罕见位移也贴住（几 float 写入，代价可忽略）
            if (IsFrozen && _frostAmount > 0f) ApplyFrost(_frostAmount);

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
            // 无视频单位=静态立牌（序列帧 idle 路线已退役，2026-10-05 拍板全库移除）——Update 无逐帧事务
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

        /// <summary>动作片建场预热（2026-10-05 决策四十五，BattlePlayer.CreateView 建场后调）：为单位
        /// 全部技能动作视频（含 Move 移动循环片）各建 per-clip RT，并用临时 VideoPlayer 预解码首帧渲入——
        /// 首次施放/移动时素材面即有画（预热首帧），解码器随后接上，消除「首次使用黑屏」
        /// （根因=RT 新分配恒黑+VideoPlayer 首开解码 ~100-300ms 才落首帧；视频资产随 Config 常驻、
        /// 黑的只是解码器首开——AssetCache/Addressables 管不到解码器，预热走视频路径本机件）。
        /// 幂等（已预热片直通）；无视频路径/解码失败单位早退（动作片本就走主视频机）</summary>
        public void PrewarmActionClips(List<VideoClip> clips)
        {
            if (_videoPlayer == null || _videoFailed || clips == null) return;
            foreach (var clip in clips)
            {
                if (clip == null || clip.width <= 0 || clip.height <= 0) continue;
                if (_actionRts.ContainsKey(clip)) continue; // 已预热直通
                var rt = new RenderTexture((int)clip.width, (int)clip.height, 0,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                _actionRts[clip] = rt;
                StartCoroutine(PrewarmClipRoutine(clip, rt));
            }
        }

        /// <summary>单片预热协程：临时 VideoPlayer Prepare→首帧渲入 RT→拆临时播放器（RT 留存首帧）。
        /// 5s 超时兜底（挂死的准备不滞留协程与临时对象——预热失败仅回落「现建 RT 黑帧」旧行为不炸建场）</summary>
        private IEnumerator PrewarmClipRoutine(VideoClip clip, RenderTexture rt)
        {
            var host = new GameObject("ActionPrewarm");
            host.transform.SetParent(transform, false);
            var vp = host.AddComponent<VideoPlayer>();
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
                if (!vp.isPrepared) yield break; // 超时：RT 留黑=旧行为兜底
                vp.Play();
                yield return null;
                yield return null; // 两帧确保 WMF 完成一次渲染入 RT
                vp.Pause();
            }
            finally
            {
                if (vp != null) { vp.Stop(); vp.clip = null; }
                if (host != null) Destroy(host);
            }
        }

        /// <summary>登记移动循环片（per-skill，2026-10-05 决策四十四「尽可能统一」：Move 型技能的
        /// SkillData.动作视频=循环态移动片——BattlePlayer SkillCast 分支按 skillType==Move 调此登记，
        /// Move 命令片起止由 SetMoveAnimation 消费。三校准参数与一次性动作片同款（速度=战斗回放速度×
        /// 技能校准倍率、缩放补偿、位置偏移）；片规格与待机片不同也不变形（双 RT 路线同构）</summary>
        public void SetMoveVideo(VideoClip clip, float playbackSpeed, float scaleCompensation, Vector2 位置偏移)
        {
            _moveClip = clip;
            _moveSpeed = playbackSpeed;
            _moveScaleComp = scaleCompensation;
            _moveOffset = 位置偏移;
            // 正处于移动循环态时登记更新=下次起播生效（段间行走由片末统一回待机，无中途换片场景）
        }

        /// <summary>移动态切换（BattlePlayer.PlayMoveCoroutine 移动片起止驱动）：true=播登记的移动循环片
        /// （决策四十四：PlayActionVideo loop 通道=与一次性动作片同机件——双 RT+缩放补偿+位置偏移+校准
        /// 速度，循环+随机相位+播完不自动回切）；false=回待机循环（SwitchToIdleLoop）。无登记片/无视频
        /// 路径/解码失败=照播待机（旧行为）；_moveLoopActive 精确归位——未在移动态的 false 调用零操作；
        /// 移动打断一次性动作片=loop 通道整块接管 RT/clip/scale（无需先恢复）；冻结/尸体态 Pause 停摆</summary>
        public void SetMoveAnimation(bool moving)
        {
            var vp = _videoPlayer;
            if (vp == null || _videoFailed) return;
            if (moving)
            {
                if (_moveClip == null) return; // 无登记移动片=待机照播（旧行为）
                PlayActionVideo(_moveClip, _moveSpeed, _moveScaleComp, _moveOffset, loop: true);
                _moveLoopActive = true;
                return;
            }
            if (!_moveLoopActive) return;
            _moveLoopActive = false;
            SwitchToIdleLoop(vp);
        }

        // ==================== 技能动作片（B-S4c 战技/爆发动作轨：一次性动作视频，播完自动回待机循环） ====================

        /// <summary>播放技能动作片（BattlePlayer SkillCast 分支驱动，fire-and-forget）：从头播、
        /// 不循环、播完自动回 待机片（OnActionVideoFinished）。同片每次施放都重播（无幂等早退——连续两次
        /// 同技能应两次起手）。**双 RT 路线（B-S4c 宽幅动作片，2026-10-04）**：动作片与 idle 片原生尺寸不同
        /// （16:9 1344×768）时各用各的 RT——素材零裁剪零缩放=零画质损失（共用 RT 会拉伸变形）；quad scale×
        /// 缩放补偿（scaleCompensation=idle 主体高/动作片主体高，安柏宽幅构图实测 1.29）令主体视觉大小与
        /// idle 片恒等——绿幕区 ChromaKey 抠透明后仅透明域变宽，无观感影响。**loop 通道（2026-10-05 决策
        /// 四十四「尽可能统一」）**：loop=true=循环态移动片——同机件仅循环+随机相位+播完不自动回切
        /// （OnActionVideoFinished 的 isLooping 守卫天然放行）。冻结/尸体态 Pause 由 Update 停摆逻辑接管、
        /// 解冻续播到回切</summary>
        public void PlayActionVideo(VideoClip clip, float playbackSpeed, float scaleCompensation, Vector2 位置偏移, bool loop = false)
        {
            var vp = _videoPlayer;
            if (vp == null || _videoFailed || clip == null || clip.width <= 0 || clip.height <= 0) return;
            if (scaleCompensation <= 0f) scaleCompensation = 1f;
            // 动作片独立 RT 池（决策四十五：per-clip 取用——预热池命中=带首帧零黑屏；未命中=现建
            // 〔黑→首帧解码落地=旧行为兜底〕；共用主 RT 会拉伸变形，故各片各 RT 按原生尺寸）
            if (!_actionRts.TryGetValue(clip, out var actionRt))
            {
                actionRt = new RenderTexture((int)clip.width, (int)clip.height, 0,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                _actionRts[clip] = actionRt;
            }
            vp.isLooping = loop; // loop 通道（决策四十四）：一次性动作片 false（播完回待机）/移动循环片 true（不自动回切）
            vp.playbackSpeed = Mathf.Max(0.1f, playbackSpeed); // =战斗回放速度×技能校准倍率（移动循环片同口径——行走 tween 同按回放速度缩放，快进不脚滑）
            vp.clip = clip;
            vp.targetTexture = actionRt;
            _videoMaterial.mainTexture = actionRt;
            // quad 缩放补偿：动作片全幅按 comp×基准高显示（主体视觉高=idle 主体视觉高）；宽=片宽高比×同高；
            // x 分量乘朝向 sign（B-S4c 校准批次修复：旧版覆盖 localScale 丢 sign——向左施放时动作片恒朝右）
            if (_avatarVideoFlip != null)
            {
                float sign = _faceLeft ? -1f : 1f;
                float aspect = clip.width / (float)clip.height;
                _avatarVideoFlip.localScale = new Vector3(
                    sign * _avatarVideoFlipBaseScale.y * aspect * scaleCompensation,
                    _avatarVideoFlipBaseScale.y * scaleCompensation,
                    _avatarVideoFlipBaseScale.z);
                // 位置微调（B-S4c 校准）：播放期间偏移基准位，播完/被打断经 RestoreIdleVideoSurface 恢复
                _avatarVideoFlip.localPosition = new Vector3(
                    _avatarVideoFlipBasePosition.x + 位置偏移.x,
                    _avatarVideoFlipBasePosition.y + 位置偏移.y,
                    _avatarVideoFlipBasePosition.z);
            }
            vp.time = loop && clip.length > 0.0
                ? UnityEngine.Random.Range(0f, (float)clip.length) // 循环态=随机相位（多枚同款单位错开扇翼，B-S3 同语义）
                : 0.0; // 一次性动作片=从头播（起手动作从第一帧）
            if (IsCorpse || IsFrozen)
            {
                if (vp.isPlaying) { vp.Pause(); _videoHalted = true; }
            }
            else
            {
                vp.Play();
            }
        }

        /// <summary>视频路径回待机/移动常态：主 RT 回接+quad 恢复原比例与基准位（OnActionVideoFinished 回切与
        /// SetMoveAnimation 打断动作片两路共用；幂等——常态时零开销直通）。恢复的 scale 带朝向 sign
        /// （B-S4c 校准批次修复：旧版直接写回 baseScale 丢 sign——动作片播完后向左单位立牌翻回朝右）</summary>
        private void RestoreIdleVideoSurface()
        {
            var vp = _videoPlayer;
            if (vp != null && _videoRt != null && vp.targetTexture != _videoRt)
                vp.targetTexture = _videoRt;
            if (_videoMaterial != null && _videoMaterial.mainTexture != _videoRt)
                _videoMaterial.mainTexture = _videoRt;
            if (_avatarVideoFlip != null)
            {
                var facingScale = new Vector3(
                    (_faceLeft ? -1f : 1f) * _avatarVideoFlipBaseScale.x,
                    _avatarVideoFlipBaseScale.y,
                    _avatarVideoFlipBaseScale.z);
                if (_avatarVideoFlip.localScale != facingScale)
                    _avatarVideoFlip.localScale = facingScale;
                if (_avatarVideoFlip.localPosition != _avatarVideoFlipBasePosition)
                    _avatarVideoFlip.localPosition = _avatarVideoFlipBasePosition;
            }
        }

        /// <summary>一次性动作片播完回待机（loopPointReached 仅 isLooping=false 的动作片走到这里——
        /// isLooping=true 的循环片〔待机/移动 loop 态〕每次循环点也触发本事件，直接 return 无操作）；
        /// 回待机常态=SwitchToIdleLoop（与移动循环态结束两路共用）</summary>
        private void OnActionVideoFinished(VideoPlayer source)
        {
            if (source.isLooping) return;
            SwitchToIdleLoop(source);
        }

        /// <summary>回待机循环常态（2026-10-05 决策四十四抽提两路共用：一次性动作片播完回切 +
        /// 移动循环态结束 SetMoveAnimation(false)）：主 RT 回接+quad 原比例与基准位（RestoreIdleVideoSurface）+
        /// 回放速度归 1+待机片随机相位续播；冻结/尸体态 Pause 停摆</summary>
        private void SwitchToIdleLoop(VideoPlayer source)
        {
            var idle = _idleVideoClip;
            if (idle == null || _videoFailed) return;
            RestoreIdleVideoSurface();
            source.playbackSpeed = 1f;
            source.isLooping = true;
            source.clip = idle;
            if (idle.length > 0.0)
                source.time = UnityEngine.Random.Range(0f, (float)idle.length);
            if (IsCorpse || IsFrozen)
            {
                if (source.isPlaying) { source.Pause(); _videoHalted = true; }
            }
            else
            {
                source.Play();
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
            foreach (var rt in _actionRts.Values) // 动作片 RT 池随单位释放（B-S4c 双 RT；决策四十五池化）
                if (rt != null) { rt.Release(); Destroy(rt); }
            _actionRts.Clear();
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
        /// <param name="idleVideo">立牌循环动画视频（B-S3 视频路线：绿幕 mp4+运行时 ChromaKey 抠色；
        /// null=静态立牌兜底；ChromaKey shader 缺失时回落静态立牌。序列帧路线已退役〔2026-10-05 拍板全库移除〕；
        /// 移动循环片不再经此参数（决策四十四：per-skill 登记 SetMoveVideo）</param>
        /// <param name="hoverHeight">立牌离地高度（世界单位=格；UnitData.离地高度，2026-09-27 拍板新增）：
        /// 纸片人整体上浮——飞行/悬浮单位；底座圆盘留地面（受击圆柱可视化=视觉即判定不随浮空）；
        /// 血条/名字/Buff 行挂倾斜组随浮空同步抬高</param>
        public static UnitView Create(Transform parent, string unitId, string displayName, Sprite avatar, Color teamColor,
            Quaternion billboardRotation, float tiltDegrees = 55f, TextEntry nameEntry = null, int hp = 0, int maxHp = 0,
            float avatarScale = 1f, VideoClip idleVideo = null,
            float hoverHeight = 0f, float cylinderDiameter = 0f)
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
            // 基准 sprite=立牌图/头像（缩放/贴地/头顶行都按它算；格底贴地 → 飞行单位悬停属正确语义）
            var baseSprite = avatar;
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
                view._avatarVideoFlipBasePosition = videoGo.transform.localPosition; // 基准位（动作片位置偏移回退锚点）

                var vp = videoGo.AddComponent<VideoPlayer>();
                vp.playOnAwake = false;
                vp.clip = idleVideo;
                vp.renderMode = VideoRenderMode.RenderTexture;
                vp.targetTexture = view._videoRt;
                vp.isLooping = true;
                vp.audioOutputMode = VideoAudioOutputMode.None;
                // 随机相位（多枚同款单位错开扇翼）
                if (idleVideo.length > 0.0)
                    vp.time = UnityEngine.Random.Range(0f, (float)idleVideo.length);
                vp.errorReceived += view.OnVideoError;
                vp.loopPointReached += view.OnActionVideoFinished; // 一次性动作片播完回待机（B-S4c 动作轨）
                vp.Play();
                view._videoPlayer = vp;
                view._idleVideoClip = idleVideo; // 待机/回切目标片（B-S4a 移动态接线；移动片=per-skill SetMoveVideo 登记，决策四十四）
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

        // ==================== 冻结霜化（2026-10-02 二次拍板「像真的结冰=shader 技术」，原神级路线） ====================
        // 三层视觉（数学单源=FrozenFrost.cginc，网检一手信源拆解）：霜色重映射（保明度结构：暗部→深冰蓝、
        // 亮部→霜白）+边缘霜光（2D 菲涅尔，边缘实中间透）+晶体闪烁；冻结遮罩从脚往头噪声扰动蔓延、缓慢流动。
        // 首版「AI 冰壳贴图罩立牌」被否决已拆勿回退。双渲染路径同源：sprite 路径=冻结换装共享 FrozenSprite
        // 材质（tint=mesh 顶点色、进度/锚点=MPB）；视频路径=ChromaKeyVideo shader 内建霜化（材质实例直写）。

        [Header("冻结霜化（2026-10-02 拍板「像真的结冰」）")]
        [Tooltip("上冻结冰蔓延时长（秒，从脚往头）")]
        [SerializeField] private float 冻结蔓延时长 = 0.9f;

        [Tooltip("解冻退冰时长（秒）")]
        [SerializeField] private float 解冻时长 = 0.3f;

        private static readonly int FrostAmountId = Shader.PropertyToID("_FrozenAmount");
        private static readonly int FrostFootYId = Shader.PropertyToID("_FreezeFootY");
        private static readonly int FrostTopYId = Shader.PropertyToID("_FreezeTopY");
        private static Shader _frostSpriteShader;       // GIC/Battle/FrozenSprite（GraphicsSettings Always Included）
        private static Material _frostSpriteMaterial;  // 全单位共享单材质（tint 走顶点色、进度走 MPB——零实例化）

        private Material _avatarOriginalMaterial;      // 冻结换装前记录（还原用）
        private MaterialPropertyBlock _frostMpb;      // sprite 路径逐帧写进度/锚点
        private Coroutine _frostCo;
        private float _frostAmount;                     // 当前进度（快照同步重入时从中断处续播）

        /// <summary>冻结态（B4：水+冰反应）：霜化 shader 结冰（快照权威同步；2026-10-02 起立牌冻结配色由
        /// shader 接管——tint 置白，shader 缺失时回落冻结冰色 tint；底座盘不变色——2026-10-02 拍板）</summary>
        public bool IsFrozen { get; private set; }

        public void SetFrozenVisual(bool frozen)
        {
            IsFrozen = frozen;
            RefreshTint();
            if (_frostCo != null) { StopCoroutine(_frostCo); _frostCo = null; }
            _frostCo = StartCoroutine(FrostRoutine(frozen ? 1f : 0f));
        }

        /// <summary>霜化进度协程：目标 1=上冻（从脚往头蔓延）、0=解冻退冰；已到目标时单次落地即返回</summary>
        private IEnumerator FrostRoutine(float target)
        {
            float from = _frostAmount;
            if (Mathf.Approximately(from, target)) { ApplyFrost(target); yield break; }
            float duration = target > from ? 冻结蔓延时长 : 解冻时长;
            yield return BattleViewTween.Over(duration, t =>
            {
                _frostAmount = Mathf.Lerp(from, target, t);
                ApplyFrost(_frostAmount);
            });
            _frostAmount = target;
            ApplyFrost(target);
        }

        /// <summary>霜化落地单点：写双路径进度与脚/头顶世界锚点。sprite 路径——进度&gt;0 换装共享霜化材质、
        /// 回 0 还原默认材质（视频解码失败回退 sprite 路径也覆盖）；shader 缺失（构建剥离）时 null 守卫跳过
        /// =tint 兜底。锚点随每次写入刷新（冻结中被击退/牵引的罕见位移也贴住）</summary>
        private void ApplyFrost(float amount)
        {
            float footY = _tiltGroup != null ? _tiltGroup.position.y : transform.position.y;
            float topY = _tiltGroup != null
                ? _tiltGroup.TransformPoint(new Vector3(0f, _avatarDisplayHeight, 0f)).y
                : footY + _avatarDisplayHeight;

            if (_videoMaterial != null) // 视频路径：per-unit ChromaKey 材质实例直写（shader 内建霜化）
            {
                _videoMaterial.SetFloat(FrostAmountId, amount);
                _videoMaterial.SetFloat(FrostFootYId, footY);
                _videoMaterial.SetFloat(FrostTopYId, topY);
            }

            if (_avatarRenderer == null) return;
            if (amount > 0f)
            {
                var frostMat = FrostSpriteMaterial;
                if (frostMat != null)
                {
                    // 原材质只捕获一次；起步已挂霜化 shader 材质的（测试场景静态冻结/换装中断重入）不当
                    // 「原材质」——还原路径保持霜化材质、量归 0（amount=0 直通渲染与默认 sprite 材质恒等）
                    var current = _avatarRenderer.sharedMaterial;
                    if (_avatarOriginalMaterial == null && current != frostMat
                        && (current == null || current.shader != _frostSpriteShader))
                        _avatarOriginalMaterial = current;
                    _avatarRenderer.sharedMaterial = frostMat;
                    if (_frostMpb == null) _frostMpb = new MaterialPropertyBlock();
                    _avatarRenderer.GetPropertyBlock(_frostMpb);
                    _frostMpb.SetFloat(FrostAmountId, amount);
                    _frostMpb.SetFloat(FrostFootYId, footY);
                    _frostMpb.SetFloat(FrostTopYId, topY);
                    _avatarRenderer.SetPropertyBlock(_frostMpb);
                }
            }
            else
            {
                if (_avatarOriginalMaterial != null)
                    _avatarRenderer.sharedMaterial = _avatarOriginalMaterial; // 还原默认 sprite 材质
                if (_frostMpb != null)
                {
                    _avatarRenderer.GetPropertyBlock(_frostMpb);
                    _frostMpb.SetFloat(FrostAmountId, 0f); // 勿 Clear——保留块内 Unity 侧条目（_MainTex 等）
                    _avatarRenderer.SetPropertyBlock(_frostMpb);
                }
            }
        }

        /// <summary>共享霜化 sprite 材质（懒建单例；shader 缺失返回 null=ApplyFrost 守卫跳过）</summary>
        private static Material FrostSpriteMaterial
        {
            get
            {
                if (_frostSpriteMaterial != null) return _frostSpriteMaterial;
                if (_frostSpriteShader == null) _frostSpriteShader = Shader.Find("GIC/Battle/FrozenSprite");
                if (_frostSpriteShader == null) return null;
                _frostSpriteMaterial = new Material(_frostSpriteShader);
                return _frostSpriteMaterial;
            }
        }

        /// <summary>立牌着色统一收口：尸体灰 > 冻结（霜化 shader 接管配色 tint 置白；shader 缺失回落冻结
        /// 冰色）> 受击闪红 > 常态白</summary>
        private void RefreshTint()
        {
            if (_avatarRenderer == null) return;
            Color tint = IsCorpse ? Palette.立牌尸体灰
                : IsFrozen && FrostSpriteMaterial == null ? Palette.冻结冰色 // shader 缺失兜底=纯冰色 tint
                : Color.white;
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
