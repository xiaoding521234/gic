# 39 - Buff 配置化（BuffConfig 资产）技术设计

> 立项：2026-10-07。用户拍板「遵循大厂的做法」——Buff 与技能/单位同等待遇，建独立配置资产。
> 状态：**已实施（K-1~K-3 落地，docs/18 决策五十四）**。实施差异：基类无「默认持续/默认叠层」通用字段（分家族字段精确承载，防死字段）；四行为族子类**必须各自同名文件**（Unity 脚本资产关联硬规则，docs/14 §131①）；实施坑=docs/14 §131（②桥脚本元组循环赋值不落盘→显式块规避、③Resources 清单延迟+注册表静态缓存残留）。

## 0. 业界信源与对照（网检一手信源，2026-10-07）

参照系 = **EGamePlay**（m969/EGamePlay，Unity 开源战斗框架，2380★，本项目技能系统的既有对照基准）。
全库 tree 经 api.github.com 拉取，关键文件经 jsdelivr 直读（存档 .codely-cli/tmp/egp_*）。

| EGamePlay 组件 | 形态 | GIC 对应组件 |
|---|---|---|
| `Buff_101.asset` 等（Resources/AbilityObjects/Buff/） | **每 Buff 一个 ScriptableObject 配置资产**（Id/ShowName/触发器/效果列表） | 新 `BuffConfig` 资产（本设计） |
| `AddStatusEffect : Effect`（技能侧效果原子） | `AddStatus: AbilityConfigObject` **资产引用** + `Duration` 覆盖 + `Params` 字典透传 | 既有 `ApplyBuffEffect`（paramKey/2/3 通道）——**已同构，维持** |
| `BuffComponent`（实体挂载） | List\<Ability\> + 按类型 id 分组 | 既有 `Unit.Buffs` + BuffType 分组——已同构 |
| Buff 行为 = 资产内 Effects/TriggerActions（运行时组件化） | 效果类本身仍是 C# | **GIC 分叉点**：行为留在 `BaseBuff` 子类（编译期展开定式，2026-09-25 审查已拍板更贴回合制）——配置只装「数据」，不装「过程」 |

对齐口径：**抽象对齐业界的组件划分与数据所有权，运行时形态沿用本项目既有定式**（与派蒙知识库批「Spring AI 组件对照」同款纪律）。

## 1. 现状与缺口

- Buff 类型：`BuffType` 枚举 7 类（Burn/Freeze/AttackUp/MoveSpeedUp/SongOfLife/Icicle/DefenseDown）；行为=每类一个 C# 子类 + `BuffFactory` 封闭 switch（2026-10-02 复审拍板维持）。
- StatBuff 族（加攻/加速/减防）数值：已数据驱动——施放技能资产参数表经 ApplyBuffEffect 三通道（paramKey=每层加成/paramKey2=叠层上限/paramKey3=时长，-1=永久）注入。
- **缺口 1**：行为族 Buff 基础数值写死在类里（`public const`）——Burn `DamagePerTurn=10`/`TurnsPerLevel=3`；歌声之环 `DamagePercentPerTurn=10`/`HealPercentPerTurn=5`；冰棱 `DamagePercentPerTurn=20`/`HealPercentPerShard=30`/`ShatterEnergyThresholdPercent=50`。调平衡必须改代码重编译。
- **缺口 2**：客户端专属图标映射 = `BattleOverheadBars.DedicatedBuffIcon` 硬编码 switch（"UI/Battle/Buff_SongOfLife" 路径字符串）——新 Buff 加图标要改代码。

## 2. 设计

### 2.1 BuffConfig 资产（新增）

- 数据类：`Assets/_Scripts/Data/Battle/Buff/BuffConfig.cs`，`ScriptableObject`。
- 资产目录：`Assets/Resources/Configs/Buffs/`（与 Skills/ 平级），**每 BuffType 一个资产**，共 7 个：
  `Buff_Burn / Buff_Freeze / Buff_AttackUp / Buff_MoveSpeedUp / Buff_SongOfLife / Buff_Icicle / Buff_DefenseDown.asset`
- 字段（Inspector 中文序列化名，项目惯例）：

| 字段 | 类型 | 说明 |
|---|---|---|
| buffType | BuffType | 协议标识，与 `BuffState.type` 同值域；**一资产一类型，注册表按它索引** |
| 专属图标 | Sprite（可空） | 客户端图标解析第一优先；空=回落来源技能图标→元素图标（决策五十回落链不变） |
| 参数表 | List\<ParamEntry\>（复用技能 customParams 同构条目） | 行为族基础数值（Burn 10/3、歌声之环 10%/5%、冰棱 20%/30%/50%、光环半径等）；键空间复用 `SkillParamKey` 枚举语义 |
| 默认持续回合 | int（0=无默认） | 无技能语境时（反应类）的真源；技能实参优先于它 |
| 默认叠层上限 | int（0=无默认） | 同上 |

### 2.2 数值所有权（模板 + 覆盖，业界标准双源规则）

1. **技能语境 Buff**（AttackUp/MoveSpeedUp/DefenseDown/SongOfLife/Icicle）：
   技能资产 ApplyBuffEffect 实参 **> BuffConfig 默认值**——同一种 Buff 不同技能施放数值不同（原神口径，现状语义不变）。
2. **反应语境 Buff**（Burn/Freeze，无技能语境）：**BuffConfig 即真源**。
3. 命座成长（C 系参数）维持既有 `SourceConstellation` 技能参数通道——基础值=BuffConfig，命座增量=技能参数，两层正交不冲突。

### 2.3 运行时链（改动面）

- **注册表**：`BuffConfig` 静态查表（`Resources.LoadAll<...>("Configs/Buffs")` 惰性加载一次，域重载安全：静态字段+首次访问加载）。
- **BuffFactory**：`Create` 签名增加 config 注入（按 type 查注册表；资产缺失=Warn+null 防御，同既有缺 turns 防御）；`BaseBuff.Config` 字段持有引用。**类型→C# 类的 switch 保留**（行为组装不走资产——§0 分叉点）。
- **行为族类改造**：const → 读 `Config` 参数表（BurnBuff/SongOfLifeBuff/IcicleBuff/FreezeBuff；const 删除，单源归资产）。StatBuff 族注入通道零改动。
- **客户端图标**：`DedicatedBuffIcon` switch → 注册表读 `config.专属图标`；决策五十回落链保留。
- **协议/快照零变化**：`BuffState.type` 仍为 int——资产只是数据源不进协议，旧回放 JSON 兼容。

### 2.4 明确不做

- BuffConfig 不携带文案/名称（buff 展示描述走技能描述模板→本地化，决策五十体系维持）。
- 不在资产里配效果列表/触发器（行为=代码，§0 分叉点；EGamePlay 全量形态不搬）。
- 工厂不扩注册表式反射（2026-10-02 复审拍板维持封闭 switch）。

## 3. 实施批次

- **K-1 数据类+注册表+7 资产落库**（Burn 10/3、歌声之环 10/5、冰棱 20/30/50、专属图标两张接线从代码路径迁资产引用）。
- **K-2 行为族改造**：4 类 Buff const→config 读取 + BuffFactory 注入 + 调用点（TurnResolver/ConstellationApplier）适配。
- **K-3 客户端图标解析改造** + 编译验证 + 资产回读断言（参数逐位对账）+ docs/skill 同步。

## 4. 拍板项（2026-10-07）

1. 方向已拍（「遵循大厂的做法」=每类型独立 BuffConfig 资产 + 技能实参覆盖）。
2. 光环半径（歌声之环 Radius=1、冰棱半径）是否随批从类字段迁入 BuffConfig 参数表 → 推荐迁（AI 脑 AuraRadiusOfBuffType 同源受益）。
3. 客户端 DedicatedBuffIcon 硬编码迁移 → 随 K-3（缺口 2 顺手收口，无争议）。
