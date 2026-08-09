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
  - `LocalEventBus`: 本地事件队列，FixedUpdate 驱动（默认50fps），每 tick 处理一个 handler
  - `Send()`: 入队事件，按优先级处理
  - `SendImmediate()`: 立即执行事件，不入队（用于 UI 同步等场景）
  - `NetworkEventBus`: 基于 Mirror 的网络事件同步
- **Manager 列表**: ConfigManager, SaveManager, InputManager, UIManager, CardManager, PositionManager, PlayerManager, SkillManager, UnitManager

### 事件系统 (`_Scripts/Data/Event/`)

- **`BaseEvent`**: 所有事件基类，包含 `SourcePlayerID`、`EventType`（Local/All/OnlyHost）、`Immediate` 等字段
- 事件通过 `EventBusHub.Instance.Send(event)` 发布（入队）或 `SendImmediate(event)` 发布（立即执行）
- 事件按 handler 优先级处理（高→低），每 FixedUpdate tick 处理一个 handler
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
- **详情面板**: `CardDetailView`（容器，管理公用字段 + 皮肤切换，`cardType` 为只读派生属性 `=> saveCardData?.cardType ?? card?.cardType`）→ `ICardDetailPanel`（接口，含 `Init(Card)` 和 `Init(SaveCardData, isReadOnly)` 两个重载）→ `UnitDetailPanel` / `ItemDetailPanel`（子面板，处理类型专属字段）
  - `CardDetailView.Init(SaveCardData)`: 不依赖 Card 组件的初始化（用于关联面板），设置 `IsReadOnly=true`，隐藏皮肤切换按钮和使用按钮
  - 角色标签：`TagChip`（方形背景芯片，`TagChip.prefab`），通过 `WrapLayoutGroup`（自动换行布局）排列在 `TagContainer` 中；`UnitDetailPanel.RefreshTagChips()` 按数量动态生成
  - 物品标签：仅 `ItemDetailPanel.mainTag` 显示 `subType`，描述区域不显示标签
- **技能描述系统** (`_Scripts/Battle/Skill/`):
  - `SkillParamKey`: 枚举，替代原来的裸字符串 key，编译期安全。新增参数只需加枚举值 + SkillParamName 表条目
  - `SkillParam`: `key` 为 `SkillParamKey` 枚举，`value` 为 `int`（永远不使用小数），`baseType` 为 `SkillBaseType`（固定值/攻击力/生命值等）
  - `SkillDescriptionBuilder`: 动态描述生成器，将本地化模板中的 `{ParamKey}` 占位符替换为带 TMP 颜色标签的参数值（如 `{Damage}` → `<color=#FFD700>40%攻击力</color>`）
  - `SkillDetailView`: 技能详情面板，通过 `TextCombiner.textProcessor` 注入 `SkillDescriptionBuilder`。`textProcessor` 必须在 `AddEntry()` 之前设置（AddEntry 会同步触发首次渲染）
- **关联面板** (`SkillDetailView`):
  - 技能描述中的 `<link="Type:Id">` 富文本可点击，通过 `LinkParser.Parse()` 解析 `"Type:Id"` 格式
  - 路由：`Unit:Amber` → CardMode（实例化 CardDetailView 副本显示卡片详情），`Concept:Tenacity` → RuleMode（纯文本名称+描述，支持嵌套 link）
  - 始终只有 1 个 RelatedPanel，CardMode 时从 `cardDetailViewTemplate`（场景中背包的 CardDetailView）实例化副本，关闭时 Destroy
  - CardMode 内点击技能图标 → 关闭 RelatedPanel + SkillDetailView 更新为新技能（副本的 `UnitDetailPanel.skillDetailView` 指向 Layer 2 的 SkillDetailView）
- **TextCombiner**: 本地化文本组合器（`_Scripts/Tool/Component/`），通过 `LocalizedString.ChangeHandler` 委托注册回调。`_activeHandlers` 列表存储委托引用，`ClearAllEntries()`/`RemoveEntry()`/`OnDestroy()` 通过引用正确 `-=` 取消注册。新增 `textProcessor`（`Func<string, string>`）回调，最终文本经过处理后显示（用于动态描述注入）
- **分类系统**: `BackpackTab`（7 个背包分页）由 `ICardConfig.GetBackpackTab()` 返回，驱动 `BackpackScreen.BuildDisplayList()` 筛选。角色通过 `UnitType`→`BackpackTab` 映射，物品通过 `ItemSubType`→`BackpackTab` 映射（`ItemSubTypeExtensions.ToBackpackTab()`）。`ItemSubType` 是物品唯一分类枚举（已删除 `ItemTag`），同时也是物品排序键（`SortOrder => (int)subType`）。排序三级键：SortOrder 升序 → StarLevel 降序 → ConfigIndex（配置文件列表顺序）升序。`ICardConfig.ConfigIndex` 由 `UnitConfig.GetUnitIndex()` / `ItemConfig.GetItemIndex()` 提供。`SaveManager.SyncMissingUnits/SyncMissingItems` 按配置文件 `unitDataList`/`itemDataList` 顺序重建存档列表（已有保留，缺失补 count=0），重建后统一排序写回
- **物品使用**: `IUsable` 接口，`ItemConfig.ItemData` 可选择实现，`ItemDetailPanel` 按需显示使用按钮
- **对象池**: `CardPool`（`_Scripts/Framework/Pool/`），`BackpackScreen` 使用池获取/归还卡牌，消除全量 destroy/respawn

### 数据层 (`_Scripts/Data/`)

- Config 类（`UnitConfig`, `ItemConfig`, `SkillConfig`）定义数据结构
- 枚举类（`UnitType`, `SkillType`, `CardType` 等）定义类型

### 编辑器工具 (`_Scripts/Editor/`)

- `LocalizationCsvTool`: 本地化 CSV 导出导入工具（Menu: Tools/Localization/CSV 导出导入），将 21 张 Localization 表导出为 CSV（Excel 可编辑），编辑后导回。CSV 格式: `Key,Id,zh-Hans,zh-TW,en,ja,ru`，RFC 4180 兼容
- `UnitConfigEditor` / `ItemConfigEditor`: Inspector 自定义，技能参数编辑器使用 `SkillParamKey` 枚举 Popup

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
