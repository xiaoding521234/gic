using System;
using UnityEngine;
namespace GIC.Data
{


    /// <summary>
    /// 行动类型（选择阶段上交；B1 实现 Move/Skill/Pass，其余 B6 补全）
    /// </summary>
    public enum ActionType
    {
        [InspectorName("移动")]
        Move = 0,

        [InspectorName("战技")]
        Skill = 1,

        [InspectorName("爆发")]
        Burst = 2,

        [InspectorName("出战角色")]
        DeployUnit = 3,

        [InspectorName("出战建筑")]
        DeployBuilding = 4,

        [InspectorName("提升命座")]
        UpgradeConstellation = 5,

        [InspectorName("购买卡牌")]
        BuyCard = 6,

        [InspectorName("使用物品")]
        UseItem = 7,

        [InspectorName("装备物品")]
        EquipItem = 8,

        [InspectorName("势力特殊技能")]
        FactionSkill = 9,

        [InspectorName("开启核心宝箱")]
        OpenChest = 10,

        [InspectorName("空过")]
        Pass = 11,
    }

    /// <summary>
    /// 行动数据（即时制：提交即校验即结算；字段按类型部分有效）
    /// </summary>
    [Serializable]
    public class ActionData
    {
        [Header("归属")]
        public string playerId;
        public string unitId;
        public ActionType actionType;

        [Header("提交时刻（逻辑秒）")]
        public int turnNumber;

        [Header("移动参数")]
        public Direction2D direction = Direction2D.Right;
        public int moveMagnitude;

        [Header("技能参数")]
        public int skillIndex;
        public string targetUnitId;
        public BattleCell targetCell;

        [Header("部署参数（B6c：DeployUnit 有效）")]
        /// <summary>出战角色（UnitName 枚举值）</summary>
        public int deployUnitName;

        /// <summary>部署落点</summary>
        public BattleCell deployCell;

        [Header("使用物品参数（UseItem 有效，2026-10-16 首个可打出物品批）")]
        /// <summary>使用的物品（ItemName 枚举值；消耗手牌 1 张；目标饮用单位复用 targetUnitId——
        /// 玩家级行动 unitId 留空〔同 DeployUnit 口径〕）</summary>
        public int useItemId;

        [Header("购买参数（BuyCard 有效，决策八十六批E，docs/45 §7）")]
        /// <summary>购买的卡牌类型（CardType 枚举值：Unit=1/Item=0——单位卡/物品卡统一承载）</summary>
        public int buyCardType;

        /// <summary>购买的卡牌内容（UnitName 或 ItemName 枚举值）</summary>
        public int buyCardValue;

        [Header("装备物品参数（EquipItem 有效，2026-10-18 装备批）")]
        /// <summary>装备的物品（ItemName 枚举值——subType 须为 Weapon/Accessory；装备即消耗手牌 1 张
        /// docs/01 §卡牌性质；目标装备单位复用 targetUnitId——玩家级行动 unitId 留空〔同 UseItem 口径，
        /// 精确指定目标=打出食物同款交互〕）</summary>
        public int equipItemId;

        /// <summary>装备的手牌条目实例耐久（决策八十一「舍弃堆叠数量，统一采用耐久方案」：装备条目按
        /// 耐久分实例——客户端从拖拽的条目填充，Host 按此实例匹配消耗与安装/补充。新鲜卡=配置满值、
        /// 掠夺物=保留耐久。默认 0 序列化兼容。**已知边界（2026-10-26 复审批 T8 登记）**：旧回放/旧客户端
        /// 行动缺省 0——Host 按 (物品,0) 匹配可能错配到耐久 0 条目；现行「改后重录」回放纪律已覆盖，
        /// 跨版本联机（N1 前）无此场景，暂不加防线</summary>
        public int equipDurability;

        /// <summary>玩家直接操控标记（2026-10-12 拍板：伙伴技能解锁——玩家通过配额直接操控伙伴
        /// 行动时标记 true（真人/AI 提交路径同标，AI-1 批起含势力技能号令），ResourceGate 体力换算
        /// 据此减免 5（可减免为负数=获得体力）；SkillExecutor 对伙伴施法者的延奏/契约号令发津贴 +5
        /// （拍板口径「延奏号令 0→−5」）；AI 伙伴自主行动=false 不减免。默认 false 序列化兼容旧存档</summary>
        public bool isPlayerDirectControl;

        /// <summary>行动产生时刻（tick 数，决策九十五即时制 docs/active/48 §8——RTSFlowController
        /// 提交时写入；段命令流对齐用；0=旧回放/旧客户端兼容缺省）</summary>
        public int submitTick;

        /// <summary>真摇杆持续态标记（决策九十五 P6，蓝图 §3.3 移动命令两形态之二「方向持续：
        /// 按住期间朝向移动，松手停」）：拖拽移动按住期间客户端周期重提交远距目标点维持行进
        /// （普通移动命令 mover 替换链）；**aimX/aimZ 双零=停止信号**——Host 移除该单位在途 mover
        /// 并发位置校正命令（空 path+cm 停点，P4-E 被挡校正同协议形态、无在途 mover=幂等零命令）。
        /// 默认 false=旧「目标点移动」语义零回归（旧回放/旧客户端缺省兼容）</summary>
        public bool moveContinuous;

        /// <summary>移动目标点 X（厘米，决策九十五蓝图 §8 Move 双轨）：&gt;0 时走即时制 tick 移动
        /// （tile A* 路径+steering 每步推进+圆碰撞，RTSMovementSystem）；=0 时回落旧
        /// direction+magnitude 格移动（Host 侧桥为目标点 cm 走 RTSMovementSystem——旧格轨
        /// MovementResolver 已随 2026-11-09 残留清理批删除）</summary>
        public int targetX;

        /// <summary>移动目标点 Z（厘米，同上）</summary>
        public int targetZ;

        /// <summary>部署自由落点 X（厘米，蓝图 §3.4 DeployUnit 双轨）：&gt;0 时自由落点校验
        /// （欧氏半径+可走 tile+无碰撞圆重叠）；=0 时回落旧 deployCell 格部署（HUD 自由落点
        /// 瞄准随 P4 批接线）</summary>
        public int deployX;

        /// <summary>部署自由落点 Z（厘米，同上）</summary>
        public int deployZ;

        // ==================== 技能瞄准（决策九十五 P3，蓝图 §4.1/§8——自由角瞄准） ====================
        // 旧 direction（Direction2D 十字）随 P3 起只作输入兜底：AI 三脑（P5 移植前）与旧 HUD
        // 链上交的十字方向由 AimVectorOf 单源桥为轴对齐向量（十字=向量子集，判定数学天然兼容零回归）；
        // P4 HUD 批接线后由瞄准层直接填任意角千分向量（拖拽轮盘落点/点击落点→施法者→落点方向）。

        /// <summary>瞄准向量 X（千分定点：轴向单位向量×1000，如 (1000,0)=东、(707,707)=东北 45°；
        /// =0 时由 AimVectorOf 按 direction/targetCell/targetUnitId 桥推导）</summary>
        public int aimX;

        /// <summary>瞄准向量 Z（千分定点，同上；X/Z 双 0=未声明走桥）</summary>
        public int aimZ;

        public override string ToString()
        {
            return $"[ActionData {playerId}/{unitId} {actionType} turn={turnNumber}" +
                   (actionType == ActionType.Move ? $" dir={direction} mag={moveMagnitude}" : "") +
                   (actionType == ActionType.Skill ? $" skill={skillIndex} target={targetUnitId}" : "") +
                   (actionType == ActionType.DeployUnit ? $" unit={(UnitName)deployUnitName} at={deployCell}" : "") + "]";
        }
    }
}
