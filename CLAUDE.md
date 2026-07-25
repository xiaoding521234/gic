# CLAUDE.md — Project Instructions

## Project Overview

**gic** — 基于 Tuanjie 引擎的多人联机回合制战棋游戏。使用 Mirror 实现网络同步，Unity Localization 实现多语言。

## Engine & Key Packages

- **引擎**: Tuanjie (Unity 兼容)
- **网络**: Mirror
- **本地化**: com.unity.localization
- **JSON**: Newtonsoft (com.unity.nuget.newtonsoft-json)
- **UI**: uGUI (com.unity.ugui) + TextMesh Pro
- **2D**: com.unity.feature.2d (Tilemap 等)

## Code Conventions

- **语言**: C#，注释使用中文
- **命名**: PascalCase（类/方法/属性），私有字段使用 `_camelCase` 或 `camelCase`
- **XML 注释**: 使用 `/// <summary>` 中文注释描述公共 API
- **代码组织**: 使用 `#region` 分区组织代码块
- **场景文件扩展名**: `.unity`

## Architecture

### 核心框架 (`_Scripts/Framework/`)

- **`Wargame`**: 游戏总管理器，继承 `Singleton<T>`，持有所有子 Manager
- **`Singleton<T>`**: 泛型单例基类，通过 `Activator.CreateInstance` 创建实例（非 MonoBehaviour）
- **`IWargameManager`**: Manager 接口，定义 `Start()` 和 `Update(float deltaTime)`
- **`GameScene`**: 场景管理器（MonoBehaviour 单例，DontDestroyOnLoad），负责场景加载/卸载/历史栈
- **`EventBusHub`**: 事件总线中枢（MonoBehaviour 单例），自动路由本地/网络事件
  - `LocalEventBus`: 本地事件队列，按帧率处理
  - `NetworkEventBus`: 基于 Mirror 的网络事件同步
- **Manager 列表**: ConfigManager, SaveManager, InputManager, UIManager, CardManager, PositionManager, PlayerManager, SkillManager, UnitManager

### 事件系统 (`_Scripts/Data/Event/`)

- **`BaseEvent`**: 所有事件基类，包含 `SourcePlayerID`、`EventType`（Local/All/OnlyHost）、`Immediate` 等字段
- 事件通过 `EventBusHub.Instance.Send(event)` 发布
- 本地事件定义在 `LocalEvents.cs`，网络事件定义在 `PlayerNetworkEvents.cs`

### 场景系统 (`_Scripts/Data/SceneType.cs`)

- **`SceneType`**: 使用类替代枚举，携带 `SceneName` 和 `LoadSceneMode` 配置
- 静态只读实例定义所有场景（Boot, SplashScreen, MainHall, MapScreen 等）
- 通过 `SceneType.XXX.Load()` 加载场景

### 战斗系统 (`_Scripts/Battle/`)

- **`Unit`**: 战斗单位，使用组件模式（`IUnitComponent` 接口），通过 `GetUnitComponent<T>()` 获取组件
- **`Card`**: 卡牌，使用策略模式（`ICardViewStrategy`）区分角色卡/物品卡显示
- **`BaseSkill` / `BaseBuff`**: 技能和 Buff 基类，通过 Factory 创建
- **`Tile`**: 格子系统，使用 `ITileComponent` 组件模式

### 卡牌/背包架构 (`_Scripts/Battle/Card/` + `_Scripts/UI/Screen/Backpack/`)

- **数据层**: `CardId`（struct，统一标识角色/物品）→ `ICardConfig`（接口，统一配置查询）→ `CardConfigAdapter`（适配器，委托到 `UnitConfig`/`ItemConfig`）→ `CardConfigResolver`（静态注册表，按 `CardId` 返回适配器）
- **存档层**: `SaveCardData`（class，持有 `CardId` + `count` + `skin` + `inDecks`）→ `PlayerSaveData.ownedCards`（运行时缓存，合并 `ownedUnits` + `ownedNormalItems` 两个序列化列表）
- **卡牌显示**: `Card`（MonoBehaviour 容器，`cardType` 为只读派生属性 `=> saveCardData.id.cardType`）→ `ICardViewStrategy`（策略接口，仅处理 Card 本身显示）→ `CardViewStrategyFactory`（按 `CardType` 返回单例策略）
- **详情面板**: `CardDetailView`（容器，管理公用字段 + 皮肤切换，`cardType` 为只读派生属性 `=> card.cardType`）→ `ICardDetailPanel`（接口）→ `UnitDetailPanel` / `ItemDetailPanel`（子面板，处理类型专属字段）
  - 角色标签：`TagChip`（方形背景芯片，`TagChip.prefab`），通过 `WrapLayoutGroup`（自动换行布局）排列在 `TagContainer` 中；`UnitDetailPanel.RefreshTagChips()` 按数量动态生成
  - 物品标签：仅 `ItemDetailPanel.mainTag` 显示 `subType`，描述区域不显示标签
- **TextCombiner**: 本地化文本组合器（`_Scripts/Tool/Component/`），通过 `LocalizedString.ChangeHandler` 委托注册回调。`_activeHandlers` 列表存储委托引用，`ClearAllEntries()`/`RemoveEntry()`/`OnDestroy()` 通过引用正确 `-=` 取消注册（旧代码用 `-= null` 导致回调泄漏）
- **分类系统**: `BackpackTab`（7 个背包分页）由 `ICardConfig.GetBackpackTab()` 返回，驱动 `BackpackScreen.BuildDisplayList()` 筛选。角色通过 `UnitType`→`BackpackTab` 映射，物品通过 `ItemSubType`→`BackpackTab` 映射（`ItemSubTypeExtensions.ToBackpackTab()`）。`ItemSubType` 是物品唯一分类枚举（已删除 `ItemTag`），同时也是物品排序键（`SortOrder => (int)subType`）
- **物品使用**: `IUsable` 接口，`ItemConfig.ItemData` 可选择实现，`ItemDetailPanel` 按需显示使用按钮
- **对象池**: `CardPool`（`_Scripts/Framework/Pool/`），`BackpackScreen` 使用池获取/归还卡牌，消除全量 destroy/respawn

### 数据层 (`_Scripts/Data/`)

- Config 类（`UnitConfig`, `ItemConfig`, `SkillConfig`）定义数据结构
- 枚举类（`UnitType`, `SkillType`, `CardType` 等）定义类型

### UI 层 (`_Scripts/UI/`)

- Screen 类管理各界面（Backpack, Map, Settings, Wish, Coop 等）
- 大型 Screen 使用 partial class 拆分（如 `BackpackScreen.Animation.cs`, `.Category.cs`, `.Events.cs`）

### 目录结构

```
Assets/_Scripts/
├── Battle/      — 战斗系统（Unit, Card, Skill, Buff, Tile）
├── Data/        — 数据定义、事件、枚举
├── Editor/      — 编辑器工具
├── Framework/   — 核心框架（Wargame, Manager, EventBus, Network, Save, Audio）
├── Test/        — 测试脚本
├── Tool/        — 工具类、扩展方法
└── UI/          — UI 界面与组件
```

## Design Patterns

- **单例**: `Singleton<T>`（纯 C#），MonoBehaviour 单例使用 `Instance` + `DontDestroyOnLoad`
- **组件模式**: `IUnitComponent`, `ITileComponent`
- **策略模式**: `ICardViewStrategy` + `CardViewStrategyFactory`
- **工厂模式**: `SkillFactory`, `Unitfactory`
- **事件驱动**: `EventBusHub` 统一路由本地/网络事件

## Unity Tool Usage

- 修改 C# 脚本后必须编译验证：`unity_workflow.compile_and_validate`
- 场景操作前确保场景已保存：`unity_scene.ensure_scene_saved`
- 写操作前检查 Play Mode 状态，Play Mode 下避免写入
- 使用 `unity_console` + `since_token` 追踪编译错误
