using System;
using System.Collections.Generic;
using UnityEngine;
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

        // 名字/Buff 行布局常量（**面内高度**：沿倾斜组 local Y，随立牌后仰；立牌本体 0~_avatarDisplayHeight，
        // 全身放大时行 Y 随立牌顶同步抬高、行自身尺寸不变；HpBarY 仅存为头顶条锚点高度——条状视觉已上移屏幕空间层）
        private const float HpBarY = 0.66f;
        private const float NameY = 0.84f;
        private const float BuffRowY = 1.08f;
        private const float BuffBadgeSize = 0.22f;
        private const float BuffBadgeGap = 0.28f;
        private const float NameFontSize = 36f;   // 世界高度 ≈ 3.43 × scale
        private const float NameScale = 0.038f;   // → 约 0.13 世界高

        /// <summary>头顶行面内 Y：立牌顶 + 与原 0.55 高版本相同的间隙（全身放大仅抬高位置）</summary>
        private float OverheadRowY(float baseY) => _avatarDisplayHeight + (baseY - AvatarHeight);

        // 配色统一走 BattlePalette 配置资产（2026-09-18 统一化批次；原 static 字面量收口，同语义不同值已对齐）
        private static BattlePalette Palette => BattlePalette.Instance;

        // 世界层运行时材质（工厂出品由本组件持有，OnDestroy 释放——Destroy 物体不销材质，docs/14 §63①）
        private Material _baseDiscMaterial;

        private void OnDestroy()
        {
            if (_baseDiscMaterial != null) Destroy(_baseDiscMaterial);
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
        public static UnitView Create(Transform parent, string unitId, string displayName, Sprite avatar, Color teamColor,
            Quaternion billboardRotation, float tiltDegrees = 55f, TextEntry nameEntry = null, int hp = 0, int maxHp = 0,
            float avatarScale = 1f)
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
            // 血条/名字/Buff 行同挂此倾斜组（用户拍板：头顶信息随立牌同平面后仰）
            var avatarGo = new GameObject("AvatarTilt");
            avatarGo.transform.SetParent(root.transform, false);
            avatarGo.transform.localPosition = Vector3.zero;
            avatarGo.transform.localRotation = Quaternion.Euler(tiltDegrees, 0f, 0f);
            view._tiltGroup = avatarGo.transform;

            var spriteGo = new GameObject("Avatar");
            spriteGo.transform.SetParent(avatarGo.transform, false);
            view._avatarRenderer = spriteGo.AddComponent<SpriteRenderer>();
            view._avatarRenderer.sprite = avatar;
            view._avatarRenderer.sortingOrder = 10;

            if (avatar != null)
            {
                float displayHeight = AvatarHeight * avatarScale;
                float worldHeight = avatar.bounds.size.y;
                float scale = worldHeight > 0f ? displayHeight / worldHeight : 1f;
                spriteGo.transform.localScale = Vector3.one * scale;
                // sprite 中心置于半高处（外层原点=底边 → 底边贴地、立牌居中于半高）
                spriteGo.transform.localPosition = new Vector3(0f, displayHeight * 0.5f, 0f);
                view._avatarDisplayHeight = displayHeight;
            }

            // 阵营色底座圆盘（B5 连续判定：受击圆柱的可视化——直径=BattleMetrics.UnitCylinderDiameter，
            // 视觉即判定，docs/18 决策二）
            view._baseDiscMaterial = BattleViewFactory.CreateUnlitMaterial(teamColor);
            var baseGo = BattleViewFactory.CreateDisc(root.transform, "BaseDisc", view._baseDiscMaterial);
            baseGo.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            baseGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            baseGo.transform.localScale = new Vector3(BattleMetrics.UnitCylinderDiameter, BattleMetrics.UnitCylinderDiameter, 1f);
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
                    ? new Vector3(BattleMetrics.UnitCylinderDiameter, 0.28f, 1f) // 尸体底座压扁（压扁值沿用旧观感）
                    : new Vector3(BattleMetrics.UnitCylinderDiameter, BattleMetrics.UnitCylinderDiameter, 1f);
        }

        /// <summary>冻结态（B4：水+冰反应）：立牌冰色 tint + 底座冰色（快照权威同步）</summary>
        public bool IsFrozen { get; private set; }

        public void SetFrozenVisual(bool frozen)
        {
            IsFrozen = frozen;
            RefreshTint();
            if (_baseDisc != null && _baseDisc.GetComponent<MeshRenderer>() != null)
                _baseDisc.GetComponent<MeshRenderer>().sharedMaterial.color = frozen
                    ? Palette.冻结冰色
                    : _baseColor;
        }

        /// <summary>立牌着色统一收口：尸体灰 > 冻结冰色 > 受击闪红 > 常态白</summary>
        private void RefreshTint()
        {
            if (_avatarRenderer == null) return;
            if (IsCorpse)
                _avatarRenderer.color = Palette.立牌尸体灰;
            else if (IsFrozen)
                _avatarRenderer.color = Palette.冻结冰色;
            else
                _avatarRenderer.color = Color.white;
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
                renderer.sortingOrder = 11;
                float worldHeight = icon.bounds.size.y;
                if (worldHeight > 0f)
                    iconGo.transform.localScale = Vector3.one * (BuffBadgeSize / worldHeight);

                // 剩余回合角标（右下小数字；世界 TMP=工厂字体链，fontSize×scale×0.1≈原 TextMesh characterSize 同高）
                var turnsGo = new GameObject("Turns");
                turnsGo.transform.SetParent(iconGo.transform, false);
                turnsGo.transform.localPosition = new Vector3(0.14f, -0.14f, -0.01f);
                turnsGo.transform.localScale = Vector3.one * 0.05f;
                var turnsText = turnsGo.AddComponent<TextMeshPro>();
                turnsText.font = BattleViewFactory.WorldTextFont;
                turnsText.fontSize = 32;
                turnsText.alignment = TextAlignmentOptions.Center;
                turnsText.enableWordWrapping = false;
                ((RectTransform)turnsGo.transform).sizeDelta = new Vector2(20f, 5f);
                turnsText.text = buff.remainingTurns.ToString();
                turnsText.color = Palette.文字米白;
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
                default: return null;
            }
        }
    }
}
