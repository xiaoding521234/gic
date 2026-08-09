# 项目知识库 — gic

> 用于快速让 AI 理解项目全貌。复制本文件内容到新对话开头即可。

## 基本信息

| 项 | 值 |
|---|---|
| 项目名 | gic |
| 公司 | HGAME |
| 引擎 | Unity Tuanjie（团结引擎），基于 Unity 2022 LTS |
| 类型 | 联机回合制战棋游戏 |
| 分辨率 | 2560×1440 |
| 脚本数 | ~156 个 C# 文件（`Assets/_Scripts/`） |
| 场景数 | 8 个（Boot, SplashScreen, MainHall, CoopScreen, BackpackScreen, MapScreen, SettingsScreen, WishScreen） |

## 依赖

| 包 | 版本 | 用途 |
|---|---|---|
| Mirror | 96+ | 网络同步（NetworkManager, NetworkBehaviour） |
| kcp2k | — | KCP 传输协议 |
| com.unity.localization | 1.5.3 | 本地化 |
| com.unity.textmeshpro | 3.0.9 | 文本渲染 |
| com.unity.nuget.newtonsoft-json | 3.2.2 | JSON 序列化 |
| com.unity.ugui | 2.0.0 | UI 系统 |
| com.unity.feature.2d | 2.0.1 | 2D 功能 |

## 场景结构

```
Boot 场景 (Single, scene 0)
  ├── GameScene         ← 场景管理器 + Unity 生命周期驱动 (DontDestroyOnLoad)
  ├── AudioManager      ← 音频管理 (DontDestroyOnLoad)
  ├── EventBusHub       ← 事件总线 (DontDestroyOnLoad)
  └── NetworkManager    ← 网络管理器 (DontDestroyOnLoad)
      └── NetworkEventBus (子GO, 持有 NetworkIdentity + NetworkEventBus)
        ↓ GameScene.Start() 加载
SplashScreen (Single) → 闪屏动画 → 加载 MainHall
        ↓
MainHall (Single) → 主菜单
        ↓ Additive 加载
CoopScreen / BackpackScreen / MapScreen / SettingsScreen / WishScreen
```

**关键**：`EditorAutoFullscreen.cs`（`[InitializeOnLoad]`）会在 Play 时强制打开 `Boot.unity` 作为起始场景。

## 代码结构 (`Assets/_Scripts/`)

```
_Scripts/
├── Battle/               # 战斗实体（纯运行时，不含管理器）
│   ├── Unit/             # 单位（组件容器模式）
│   │   ├── Unit.cs       # ← Dictionary<Type, IUnitComponent> 容器
│   │   ├── Unitfactory.cs
│   │   └── Component/    # UnitStats, UnitMoveable, UnitIdentity, UnitInventory, UnitStatus, UnitUI, UnitGridPosition, UnitElement, ForceData
│   ├── Card/             # 卡牌（策略模式）
│   │   ├── Card.cs
│   │   └── ViewStrategy/ # ICardViewStrategy → UnitCardViewStrategy / ItemCardViewStrategy
│   ├── Skill/            # 技能（工厂 + 属性反射注册）
│   │   ├── BaseSkill.cs
│   │   ├── SkillFactory.cs
│   │   └── SkillAttribute.cs  # [SkillAttribute(SkillName.XXX)] 自动注册
│   ├── Buff/
│   │   └── BaseBuff.cs
│   └── TIle/             # 棋盘格子（同 Unit 的组件容器模式）
│       ├── Tile.cs
│       └── Component/    # ITileComponent, TileGridPosition, TileIdentity
│
├── Data/                 # 数据定义（ScriptableObject 配置 + 枚举 + 事件）
│   ├── Battle/
│   │   ├── Unit/         # UnitConfig(SOA), UnitName(enum~40角色), UnitType, FactionType, StatType, StatusType, WeaponType, UnitTag
│   │   ├── Skill/        # SkillConfig(SOA), SkillName, SkillType, SkillContext
│   │   ├── Item/         # ItemConfig(SOA), ItemName, ItemTag
│   │   ├── Element/      # Element, ElementType
│   │   ├── Board/        # BoardType, TileType
│   │   └── RangedInt.cs, StatModifierType.cs
│   ├── Card/             # ICardConfig(接口), CardConfigAdapter, CardConfigResolver, CardId
│   ├── Event/            # BaseEvent, LocalEvents, NetworkEventMessage, PlayerNetworkEvents
│   ├── SceneType.cs      # 场景类型（sealed class，非 enum）
│   ├── PlayerID.cs       # Offline="Offline", Unknown="99", Host="0"
│   ├── PlayerInfo.cs     # 玩家信息结构
│   ├── PlayerColor.cs    # 玩家颜色枚举（10色）
│   └── ...
│
├── Framework/            # 核心框架（持久化管理器）
│   ├── Wargame.cs        # ← 顶层管理器（Singleton<Wargame>，纯 C#）
│   ├── GameScene.cs      # ← 场景管理器（MonoBehaviour, [DefaultExecutionOrder(-100)]）
│   ├── ConfigManager.cs  # 加载 SO 配置 (Resources/Configs/)
│   ├── UIManager.cs     # 占位
│   ├── InputManager.cs  # 占位
│   ├── PositionManager.cs # 地图位置 + 背景音乐
│   ├── Interfaces/      # Singleton<T>, IWargameManager
│   ├── EventBus/        # 事件总线系统（见下文）
│   ├── Network/         # 网络系统（见下文）
│   ├── Audio/            # AudioManager (partial class: .cs/.SFX.cs/.Music.cs/.Voice.cs/.Volume.cs)
│   ├── Battle/          # CardManager(7副牌组), CardDeck(懒重建), UnitManager, ViewManager(实为 SkillManager)
│   └── Save/            # SaveManager(JSON存档), PlayerSaveData
│
├── UI/
│   ├── Screen/
│   │   ├── MainHallScreen.cs     # 主菜单
│   │   ├── Coop/                 # CoopScreen + CoopNetworkController + PlayerRowView
│   │   ├── Backpack/             # BackpackScreen(.cs/.Animation/.Category/.Deck/.Display/.Events) + CardDetailView
│   │   ├── Map/                  # MapScreen, MapAnchor, MapZoomAndDrag
│   │   ├── Settings/            # SettingsScreen + 各种 SettingItem
│   │   ├── Wish/                 # WishScreen, CharacterPanelController
│   │   └── SplashScreen.cs
│   ├── Popup/           # PopupDialog, PopupManager
│   ├── Skill/           # SkillDetailView, SkillIconView, ParamView
│   ├── Common/          # ElementColor, StarColor
│   └── ViewType.cs      # enum: Display/Actual/OnlyDisplay
│
├── Editor/              # 编辑器扩展
│   ├── Tool/            # EditorAutoFullscreen, ElementFactionIconAutoLoader, ItemConfigImageTool, UnitConfigImageTool, SerializedPropertyContextMenu
│   ├── FindMissingScripts.cs
│   ├── ItemConfigEditor.cs
│   ├── PositionConfigEditor.cs
│   ├── TMPFontReplacer.cs
│   └── UnitConfigEditor.cs
│
└── Tool/                # 工具类
    ├── CardSortUtility.cs       # 卡牌排序
    ├── EnumExtensions.cs        # [InspectorName] 反射
    ├── GameObjectExtensions.cs  # Reactivate()
    ├── StringExtensions.cs      # ToSnakeCase()
    ├── TimeUtility.cs           # 白天/黑夜判断
    └── Component/              # TextCombiner, ArcLayoutGroup, LocalizedDropdown
```

## 核心架构

### 1. 管理器层级

```
GameScene (MonoBehaviour, DontDestroyOnLoad)
  ├── Awake() → Wargame.Instance.Init()
  ├── Start() → Wargame.Instance.Start() → 加载 SplashScreen
  └── Update() → Wargame.Instance.Update(deltaTime)

Wargame (Singleton<Wargame>, 纯 C#)
  ├── ConfigManager    ← 加载 Resources/Configs/ 下的 ScriptableObject
  ├── SaveManager      ← JSON 存档 (persistentDataPath/gic_save.json)
  ├── InputManager     ← 占位
  ├── UIManager        ← 占位
  ├── CardManager      ← 7 副牌组管理（懒重建）
  ├── PositionManager  ← 地图位置 + 背景音乐
  ├── PlayerManager    ← 玩家管理（网络事件驱动）
  ├── SkillManager     ← 占位（ViewManager.cs 中）
  └── UnitManager      ← 缓存 UnitConfig
```

### 2. 事件总线系统

```
EventBusHub (MonoBehaviour, DontDestroyOnLoad)
  ├── LocalEventBus (纯 C#)     ← 本地事件队列（固定帧率处理，锁机制）
  └── NetworkEventBus (NetworkBehaviour) ← 网络事件（Mirror）
      └── NetworkEventRegistry   ← FNV-1a 哈希注册，ushort ID + JSON 序列化

事件路由：
  Local     → LocalEventBus.Send (排队)
  All       → NetworkEventBus.SendToAll (广播所有客户端 + 服务器)
  OnlyHost  → NetworkEventBus.SendToHost (仅服务器)
  None      → NetworkEventBus.SendToPlayer (定向发送)

事件生命周期：Queued → Processing → Completed/Cancelled
锁状态：None / Animation / Highest（Animation 锁时可跳过非紧急事件）
```

### 3. 网络系统

```
MyNetworkManager (Mirror NetworkManager)
  ├── KcpTransport (端口 7777)
  ├── MyNetworkDiscovery (LAN 发现，单播扫描模式)
  ├── maxPlayers = 6
  ├── StartHostMode() → 自动端口探测
  └── JoinRoom(ip, port)

PlayerManager (IWargameManager, 纯 C#)
  ├── _allPlayers: Dictionary<string, PlayerInfo>
  ├── SelfPlayerID = connectionId.ToString() ("0" = Host)
  ├── 事件驱动：OnServerConnect → 注册玩家 → 广播 AddPlayerEvent
  └── 10 个事件 Handler（SetTeam/SetColor/SetSpawn/ToggleReady/Kick/Add/Remove/UpdateInfo/Kicked/SetSelfPlayer）

NetworkEventBus (NetworkBehaviour, 子 GO of NetworkManager)
  ├── OnStartServer() → 注册 Server + Client handler
  ├── OnStartClient() → 注册 Client handler
  └── 消息类型：NetworkEventMessage (EventId, SenderID, TargetID, Json)
```

### 4. 战斗实体

```
Unit (MonoBehaviour)
  ├── Dictionary<Type, IUnitComponent> _components
  ├── InitWithData(UnitConfig.UnitData) → 初始化所有组件 + 创建技能
  ├── Skills: List<BaseSkill>
  └── Buffs: List<BaseBuff>

BaseSkill (abstract)
  ├── [SkillAttribute(SkillName.XXX)] → SkillFactory 反射注册
  ├── SkillFactory.CreateWithData(SkillConfig.SkillData) → 实例化 + Init
  └── SkillParam: key-value 自定义参数

Card (MonoBehaviour, UI)
  ├── ICardViewStrategy (策略模式: Unit / Item)
  ├── CardViewStrategyFactory.Get(CardType) → 返回策略实例
  └── SaveCardData → CardConfigResolver.Resolve(CardId) → ICardConfig
```

### 5. 数据 / 配置系统

```
ScriptableObject 配置（Resources/Configs/）：
  UnitConfig     ← 角色数据（属性、星级、势力、技能、精灵）
  ItemConfig     ← 物品数据（标签、元素、类别）
  SkillConfig    ← 技能数据（类型、参数、图标）
  PositionConfig ← 地图位置数据
  ElementFactionIconConfig ← 元素/势力图标

CardConfigResolver.Initialize(unitConfig, itemConfig)
  → Resolve(CardId) → ICardConfig (UnitConfigAdapter / ItemConfigAdapter)

SaveCardData (存档中的卡牌实例)
  → CardConfigResolver.Resolve(id) → 获取名称/星级/图标等
```

### 6. 存档系统

```
SaveManager (IWargameManager)
  ├── 路径: Application.persistentDataPath/gic_save.json
  ├── IS_DELETION_TEST_MODE = true  ← ⚠️ 发布前改为 false
  ├── PlayerSaveData: 玩家名, 拥有角色/物品, 当前牌组, 音量, 显示设置, 当前位置
  └── SyncMissingUnits/SyncMissingItems: 按配置文件顺序重建列表，已有数据保留，缺失的补 count=0
```

## 关键设计模式

| 模式 | 应用 |
|---|---|
| Singleton<T> | Wargame 及所有 IWargameManager（纯 C# Activator.CreateInstance） |
| 组件容器 | Unit/Tile 持有 Dictionary<Type, IComponent> |
| 策略模式 | Card → ICardViewStrategy (Unit/Item) |
| 工厂模式 | SkillFactory（反射 + Attribute 注册）、UnitFactory |
| 适配器模式 | UnitConfigAdapter/ItemConfigAdapter → ICardConfig |
| 事件总线 | LocalEventBus（本地队列）+ NetworkEventBus（Mirror 网络同步） |
| 场景管理 | GameScene（历史栈 + Single/Additive 加载 + 预加载） |

## 注意事项

- **Boot 场景**：所有持久化管理器在 Boot 场景，`EditorAutoFullscreen` 会在 Play 时强制切到 Boot
- **NetworkEventBus**：挂在 NetworkManager 的子 GO 上（分离 NetworkIdentity），避免 StopHost 时禁用 NetworkManager
- **SaveManager.IS_DELETION_TEST_MODE**：当前为 true，每次启动删除存档；发布前必须改为 false
- **LocalEventBus**：固定帧率处理事件（每帧处理一个 handler），有锁机制（Animation/Highest）
- **Localization**：全面使用 Unity Localization 包，TextCombiner 组件统一管理多段本地化文本
- **PlayerID**：使用 connectionId（"0"=Host, "99"=Unknown, "Offline"=未连接）
- **SortOrder**：卡牌排序按 SortOrder（势力/物品子类型）升序 → StarLevel 降序 → ConfigIndex（配置文件顺序）升序
