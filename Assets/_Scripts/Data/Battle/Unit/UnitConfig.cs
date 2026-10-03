using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Video;
using GIC.Framework;
using GIC.Tool;
namespace GIC.Data
{


    [CreateAssetMenu(fileName = "UnitConfig", menuName = "Game/UnitConfig")]
    public class UnitConfig : ScriptableObject
    {
        /// <summary>
        /// 哨兵值：表示该属性未手动设置，应使用派生值
        /// </summary>
        public const int Unspecified = -64;

        [System.Serializable]
        public class UnitData
        {
            public UnitName unitName = UnitName.Amber;
            public UnitType unitType = UnitType.Character;
            public FactionType[] factions;

            public Sprite avatar;
            public List<Sprite> cards;
            public Sprite nameCard;

            /// <summary>战斗立牌图（纸片人全身立绘，gic-paperdoll 产物）；null 时立牌回落 avatar 头像</summary>
            public Sprite 立牌图;

            /// <summary>立牌循环动画帧（AI 直出序列帧 sheet 的网格切片，按序循环播放；null/空=静态立牌兜底）</summary>
            public Sprite[] 立牌动画帧;

            /// <summary>立牌动画播放帧率（fps，默认 12；序列帧循环速度）</summary>
            public float 立牌动画帧率 = 12f;

            /// <summary>立牌循环动画视频（B-S3 视频路线：绿幕 mp4 + VideoPlayer→RT + 运行时 ChromaKey 抠色；
            /// 显存恒定、与帧数无关；优先级高于 立牌动画帧；null=序列帧/静态兜底）</summary>
            public VideoClip 立牌动画视频;

            /// <summary>移动中循环动画视频（B-S4a 移动态接线，2026-09-29 拍板「正式化安柏待机+移动动画」）：
            /// 移动命令片内切此片、片末回 立牌动画视频（UnitView.SetMoveAnimation，驱动=BattlePlayer.PlayMoveCoroutine 起止）；
            /// null=无移动态动画（立牌动画视频 常驻=旧行为）</summary>
            public VideoClip 移动动画视频;

            /// <summary>立牌离地高度（格，1 格=1 世界单位；2026-09-27 拍板新增）：纸片人整体上浮——
            /// 飞行/悬浮单位调高（安柏=0.5）；**默认 0.05=AI 生成立牌底部渐隐行的陷地观感补偿基线**
            /// （2026-09-27 全员拍板统一 0.05，真实贴地需显式设 0）。底座圆盘不随浮空（受击圆柱
            /// 可视化=视觉即判定，判定恒在地面格）；血条/名字/Buff 行挂倾斜组随浮空同步抬高</summary>
            public float 离地高度 = 0.05f;

            /// <summary>立牌额外缩放（2026-09-27 拍板新增，乘在 BattlePlayer「全身立牌放大倍数」之上；
            /// 1=不缩放——当前全部单位统一 1.0，仅作 per-unit 微调闸门）</summary>
            public float 额外缩放 = 1f;

            public bool hideInBackpack = false;

            [Header("稀有度")]
            [Range(1, 5)]
            public int starLevel = 3;
            public int deployCost = Unspecified;

            [Header("武器")]
            public WeaponType weaponType = WeaponType.Claymore;

            [Header("基础属性")]
            public int baseHP = Unspecified;
            public int baseAttack = Unspecified;
            public int baseDefense = Unspecified;
            public int baseAttackSpeed = Unspecified;
            public int baseMoveSpeed = Unspecified;
            public int baseLuck = Unspecified;
            public int baseTenacity = Unspecified;
            public int baseMastery = Unspecified;
            public int baseSanity = Unspecified;

            [Header("战斗属性")]
            public int baseLifeSteal = Unspecified;
            public int baseHealEfficiency = Unspecified;
            public int baseEnergy = Unspecified;

            [Header("常态移动")]
            public ForceType normalMoveType = ForceType.Walk;

            [Header("碰撞规则")]
            public bool blockAllies = true;
            public bool blockEnemies = true;
            public bool blockedByEnemies = true;
            /// <summary>与友方互不阻挡（2026-09-26 拍板「只要这个单位 互不阻挡 字段为 true，无论是他穿
            /// 其它友军，还是友军穿他，都不阻挡」——双向豁免单字段：本单位不阻挡友方进入其格 +
            /// 本单位移动/部署也不被友方阻挡。配置驱动单源：MovementResolver/DeployUnitExecutor/
            /// 两客户端预览四处同读，改配置即全链路响应）</summary>
            public bool 与友方互不阻挡 = false;

            /// <summary>受击圆柱直径（格=世界单位；2026-09-29 协议核心批新增）：连续命中判定的
            /// per-unit 受击体直径（docs/05 §5.3「受击体=立牌真实大小的圆柱」），底座圆盘同源可视化
            /// （视觉即判定）、选中弧光贴紧值按它折算。0=未指定回落全局 BattleMetrics.UnitCylinderDiameter
            /// （0.42——消费侧经 BattleMetrics.CylinderDiameterOf 单出口解析，Data 层勿反向引用 Battle 常量）；
            /// 协议核心=0.8（近乎占满格，大目标易命中=攻城手感）</summary>
            public float 受击圆柱直径 = 0f;

            [Header("元素")]
            public ElementType selfElement = ElementType.Physical;

            [Header("视野（2026-10-03 拍板拆分：攻击视野=AI 追击感知半径 / 迷雾视野=破雾半径）")]
            /// <summary>攻击视野（2026-10-03 拍板「眷属总是向协议核心进攻，除非攻击视野内有其它敌人」）：
            /// 眷属 AI 追击**非核心**敌人的感知半径（切比雪夫，格）——视野外的敌不关心（皇室战争式）；
            /// 协议核心无视野门槛恒为目标，非核心敌还须严格近于核心（同距核心优先）。
            /// Unspecified=回落 5；安柏=24（对齐全图狙击射程）。攻击档自身射程即感知边界（射程≤视野
            /// 对现役单位恒成立）。未来 Buff 要改它时再入 StatType（预留勿提前造）。</summary>
            public int 攻击视野 = Unspecified;

            /// <summary>迷雾视野（战争迷雾系统预留，2026-10-03 拍板）：可自动破除迷雾的范围（切比雪夫，格）。
            /// Unspecified=回落 2；现行消费方=StatType.VisionRange 基值（安柏 1命「视野提升」休眠数据位
            /// 挂此管线——迷雾批落地时接线）。</summary>
            public int 迷雾视野 = Unspecified;

            [Header("标签")]
            public UnitTag[] tags;

            [Header("技能（2026-09-23 独立化：SkillConfig 资产引用列表；顺序=skillIndex 语义勿重排；通用技能共享资产）")]
            public List<SkillConfig> skills = new List<SkillConfig>();

            [Header("语音")]
            public UnitVoiceData voices;

            [Header("伙伴行为档案（操控分层 D 批次，docs/active/32 §6.1——评分骨架权重；null=中性档案）")]
            /// <summary>个体行为档案：一套评分骨架+每单位一份档案——行为差异全在档案，加新伙伴=配档案零代码
            ///（costs/时轮同哲学）。仅伙伴（3-4★）消费；眷属不配（启发式维持=层级特色+算量安全阀）、
            /// 魔神无脑（玩家全手操）不消费。</summary>
            public CompanionProfile 行为档案;

            private bool IsSpecified(int value) => value != Unspecified;

            public int GetDeployCost()
            {
                if (IsSpecified(deployCost)) return deployCost;
                return starLevel switch { 1 => 10, 2 => 20, 3 => 50, 4 => 100, 5 => 300, _ => 50 };
            }

            public int GetHPByStarLevel()
            {
                // 2026-09-26 拍板：1★=100、2★=150（低星更脆）；3~5★ 维持原值
                return starLevel switch { 1 => 100, 2 => 150, 3 => 200, 4 => 300, 5 => 600, _ => 200 };
            }

            public int GetAttackByWeaponType()
            {
                return weaponType switch
                {
                    WeaponType.Claymore => 60, WeaponType.Sword => 40,
                    WeaponType.Polearm => 40, WeaponType.Gauntlet => 40,
                    WeaponType.Catalyst => 60, WeaponType.Bow => 50,
                    WeaponType.Gun => 50, _ => 40
                };
            }

            public int GetAttackSpeedByWeaponType()
            {
                return weaponType switch
                {
                    WeaponType.Claymore => 20, WeaponType.Sword => 40,
                    WeaponType.Polearm => 50, WeaponType.Gauntlet => 60,
                    WeaponType.Catalyst => 10, WeaponType.Bow => 30,
                    WeaponType.Gun => 70, _ => 40
                };
            }

            public int GetEffectiveAttack() => IsSpecified(baseAttack) ? baseAttack : GetAttackByWeaponType();
            public int GetEffectiveAttackSpeed() => IsSpecified(baseAttackSpeed) ? baseAttackSpeed : GetAttackSpeedByWeaponType();
            public int GetEffectiveDefense() => IsSpecified(baseDefense) ? baseDefense : 0;
            public int GetEffectiveMoveSpeed() => IsSpecified(baseMoveSpeed) ? baseMoveSpeed : 30;
            public int GetEffectiveLuck() => IsSpecified(baseLuck) ? baseLuck : 0;
            public int GetEffectiveTenacity() => IsSpecified(baseTenacity) ? baseTenacity : 0;
            public int GetEffectiveMastery() => IsSpecified(baseMastery) ? baseMastery : 0;
            public int GetEffectiveSanity() => IsSpecified(baseSanity) ? baseSanity : 50;
            public int GetEffectiveHP() => IsSpecified(baseHP) ? baseHP : GetHPByStarLevel();
            public int GetEffectiveAttackVision() => IsSpecified(攻击视野) ? 攻击视野 : 5;
            public int GetEffectiveFogVision() => IsSpecified(迷雾视野) ? 迷雾视野 : 2;
            public int GetEffectiveLifeSteal() => IsSpecified(baseLifeSteal) ? baseLifeSteal : 0;
            public int GetEffectiveHealEfficiency() => IsSpecified(baseHealEfficiency) ? baseHealEfficiency : 100;
            public int GetEffectiveEnergy() => IsSpecified(baseEnergy) ? baseEnergy : 100;
            public int GetEffectiveDeployCost() => IsSpecified(deployCost) ? deployCost : GetDeployCost();

            /// <summary>单位体积单出口（2026-09-27 复审收口：原「Building?2:1」在 Unit.Volume 与部署校验各写一份=双源，
            /// 违反 docs/20 §1.6 判定规则字段单源）。docs/05 §5.3：角色/造物=1、建筑=2，格子体积容量 3——
            /// 体积判定在阻挡规则之上，无视阻挡配置不可绕过。消费方=Unit.Volume（移动/占据）与
            /// DeployUnitExecutor.IsDeployCellValid（部署预判，实例化前）两链路同读</summary>
            public int GetVolume() => unitType == UnitType.Building ? 2 : 1;

            /// <summary>
            /// 获取指定技能的语音组
            /// </summary>
            public AudioClipRandom GetSkillVoices(SkillName skill)
            {
                return voices?.GetSkillVoices(skill);
            }

            /// <summary>
            /// 是否为角色单位
            /// </summary>
            public bool IsCharacter => unitType == UnitType.Character;

            /// <summary>
            /// 是否为造物单位
            /// </summary>
            public bool IsCreation => unitType == UnitType.Creation;

            /// <summary>
            /// 是否为造物（原 Interactive 已合并到 Creation）
            /// </summary>
            public bool IsInteractive => unitType == UnitType.Creation;

            public Sprite GetCard(int skin)
            {
                // 添加边界检查
                if (cards == null || cards.Count == 0)
                    return null;

                // 确保索引在有效范围内
                if (skin < 0 || skin >= cards.Count)
                {
                    GICLog.Warn($"GetCard: 皮肤索引 {skin} 超出范围 (0-{cards.Count - 1})，返回默认卡片");
                    return cards[0]; // 返回第一张卡片作为默认
                }

                return cards[skin];
            }

            #region localization Entry 获取方法

            /// <summary>
            /// 获取单位名称的 Entry（用于 TextCombiner）
            /// </summary>
            public TextEntry GetNameEntry()
            {
                return unitName.GetEntry();
            }

            /// <summary>
            /// 获取单位称号的 Entry（用于 TextCombiner）
            /// </summary>
            public TextEntry GetTitleEntry()
            {
                var localizedString = new LocalizedString(TableName.UnitTitle.ToString(), unitName.ToString());
                return new TextEntry(localizedString, "");
            }

            /// <summary>
            /// 获取单位介绍的 Entry（用于 TextCombiner）
            /// </summary>
            public TextEntry GetDescriptionEntry()
            {
                var localizedString = new LocalizedString(TableName.UnitDescription.ToString(), unitName.ToString());
                return new TextEntry(localizedString, "");
            }

            /// <summary>
            /// 获取单位获取描述的 Entry（用于 TextCombiner）
            /// </summary>
            public TextEntry GetObtainDescriptionEntry()
            {
                var localizedString = new LocalizedString(TableName.UnitObtainDescription.ToString(), unitName.ToString());
                return new TextEntry(localizedString, "");
            }

            #endregion
        }

        [System.Serializable]
        public class SkillVoiceEntry
        {
            public SkillName skillName;
            public AudioClipRandom voices;
        }

        /// <summary>支援/输出倾向（行为档案「候选类别」）——支援型骨架多一个候选类别
        /// （估「奶谁/去哪护」而非只「打谁」），非新脑（docs/active/32 §6.1）</summary>
        public enum CompanionRole
        {
            [InspectorName("输出型")] DamageDealer = 1,
            [InspectorName("支援型")] Support = 2,
        }

        /// <summary>行为档案「目标偏好」：输出型=攻击目标锚；支援型=移动/保护锚（docs/active/32 §6.1）</summary>
        public enum CompanionTargetPreference
        {
            [InspectorName("最近敌人")] NearestEnemy = 1,
            [InspectorName("最缺血我方")] MostWoundedAlly = 2,
        }

        /// <summary>伙伴个体行为档案（docs/active/32 §6.1：评分骨架共享维度框架——每维度乘档案权重，
        /// 差异全在这）。v1 五维=偏好交战距离/技能优先权重/激进度/目标偏好/候选类别；
        /// 权重 1=中性（=评分制 v2 原口径）。</summary>
        [System.Serializable]
        public class CompanionProfile
        {
            [Header("交战距离")]
            [InspectorName("偏好交战距离（格；0=自动=攻击射程单源〔CR-Move 拍板，皇室战争式「进射程即停」〕；>0=手动覆写微调手感）")]
            public int 偏好交战距离 = 0;

            [Header("技能优先权重（同分冲突倾向；1=中性）")]
            [InspectorName("战技优先权重")]
            public float 战技优先权重 = 1f;

            [InspectorName("爆发优先权重")]
            public float 爆发优先权重 = 1f;

            [InspectorName("移动优先权重")]
            public float 移动优先权重 = 1f;

            [Header("激进度（追击/斩杀倾向乘数；1=中性，<1 保守、>1 激进）")]
            [InspectorName("激进度")]
            public float 激进度 = 1f;

            [Header("集火权重（E-2 协调层：0=游走型不跟随集火；1~10=对已被我方声明攻击的未死目标每档加 3 分——集中优势兵力 vs 分散火力的取舍）")]
            [InspectorName("集火权重")]
            public int 集火权重 = 0;

            [Header("支援行为（F 批次支援型架构重构，docs/active/34 §5.4）")]
            [InspectorName("支援贴近距离（0=自动=技能集最大治疗原子半径——伤员锚驻位语义；>0=手动覆写微调手感）")]
            public int 支援贴近距离 = 0;

            [InspectorName("自保权重（0=不自保；1=标准——落点距最近敌 < 危险半径扣 15 分/档，奶妈不站敌人刀口）")]
            public float 自保权重 = 0f;

            [Header("目标与角色")]
            [InspectorName("目标偏好")]
            public CompanionTargetPreference 目标偏好 = CompanionTargetPreference.NearestEnemy;

            [InspectorName("候选类别")]
            public CompanionRole 候选类别 = CompanionRole.DamageDealer;
        }

        [System.Serializable]
        public class UnitVoiceData
        {
            [Header("出战")]
            public AudioClipRandom onGoWar;

            [Header("选择(高血量)")]
            public AudioClipRandom onChooseHighHP;

            [Header("选择(低血量)")]
            public AudioClipRandom onChooseLowHP;

            [Header("受轻击")]
            public AudioClipRandom onHitLight;

            [Header("受重击")]
            public AudioClipRandom onHitHeavy;

            [Header("倒下")]
            public AudioClipRandom onDie;

            [Header("技能语音")]
            public SkillVoiceEntry[] skillVoices;

            public AudioClipRandom GetSkillVoices(SkillName skill)
            {
                if (skillVoices == null) return null;
                foreach (var entry in skillVoices)
                {
                    if (entry.skillName == skill)
                        return entry.voices;
                }
                return null;
            }
        }

        public List<UnitData> unitDataList = new();
        private Dictionary<UnitName, UnitData> dataCache;

        public void BuildCache()
        {
            dataCache = new Dictionary<UnitName, UnitData>();
            foreach (var data in unitDataList)
            {
                if (data != null && !dataCache.ContainsKey(data.unitName))
                {
                    dataCache.Add(data.unitName, data);
                }
            }
        }

        public UnitData GetUnitData(UnitName unitName)
        {
            if (dataCache == null) BuildCache();
            dataCache.TryGetValue(unitName, out var data);
            return data;
        }

        public bool TryGetUnitData(UnitName unitName, out UnitData data)
        {
            if (dataCache == null) BuildCache();
            return dataCache.TryGetValue(unitName, out data);
        }

        public List<UnitData> GetAllUnits()
        {
            if (dataCache == null) BuildCache();
            return new List<UnitData>(dataCache.Values);
        }

        public List<UnitData> GetUnitsByType(UnitType unitType)
        {
            if (dataCache == null) BuildCache();

            var result = new List<UnitData>();
            foreach (var data in dataCache.Values)
            {
                if (data.unitType == unitType)
                    result.Add(data);
            }
            return result;
        }

        public List<UnitData> GetUnitsByFaction(FactionType faction)
        {
            if (dataCache == null) BuildCache();

            var result = new List<UnitData>();
            foreach (var data in dataCache.Values)
            {
                if (data.factions != null)
                {
                    foreach (var f in data.factions)
                    {
                        if (f == faction)
                        {
                            result.Add(data);
                            break;
                        }
                    }
                }
            }
            return result;
        }

        public List<UnitData> GetUnitsByStarLevel(int starLevel)
        {
            if (dataCache == null) BuildCache();

            var result = new List<UnitData>();
            foreach (var data in dataCache.Values)
            {
                if (data.starLevel == starLevel)
                    result.Add(data);
            }
            return result;
        }

        public List<UnitData> GetUnitsByWeaponType(WeaponType weaponType)
        {
            if (dataCache == null) BuildCache();

            var result = new List<UnitData>();
            foreach (var data in dataCache.Values)
            {
                if (data.weaponType == weaponType)
                    result.Add(data);
            }
            return result;
        }

        public bool HasUnit(UnitName unitName)
        {
            if (dataCache == null) BuildCache();
            return dataCache.ContainsKey(unitName);
        }

        /// <summary>
        /// 获取角色在 unitDataList 中的索引（配置文件顺序），不存在返回 -1
        /// </summary>
        public int GetUnitIndex(UnitName unitName)
        {
            for (int i = 0; i < unitDataList.Count; i++)
            {
                if (unitDataList[i] != null && unitDataList[i].unitName == unitName)
                    return i;
            }
            return -1;
        }

        public int GetUnitCount()
        {
            if (dataCache == null) BuildCache();
            return dataCache.Count;
        }
    }
}




