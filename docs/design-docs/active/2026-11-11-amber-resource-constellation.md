# 安柏 3B「循迹觅宝」资源命座设计

> 状态：方案 A 与书面规格均已由用户确认（2026-11-11），实现已落地；静态验证通过，Tuanjie 实机待验。
> 工作分支：master。
> 范围：仅替换安柏 3B；其余命座、战技、爆发、移动与既有资源点规则不改。

## 1. 目标与命名

- 3B 名称：**循迹觅宝**。
- Tag：**资源**，不再使用“冷却”。
- 内部语义名调整为 `Amber_C3B_TreasureHunt`，保留数值 ID `3001012` 与资产 GUID，避免既有分支选择 ID、快照和协议引用失效。
- 玩家描述模板：
  `·每{C3DiscoveryInterval}秒在自身{C3DiscoveryRadius}厘米内发现一处隐藏摩拉堆，每处可采集{C3DiscoveryCharges}次`

设计目的：让 3B 与 1A、2B 形成资源流协同，并延长安柏在地图原生资源耗尽后的玩法生命周期。

## 2. 方案选择与边界

采用**运行时生成并公开**：触发前节点不存在，触发时在安柏附近空格生成摩拉堆。

不采用地图预置隐藏点：该方案需要独立休眠节点池、地图隐藏配置和额外揭示状态，且数量受地图限制。

不采用直接发放摩拉：直接发放绕过移动、读条、快速采集、装备恢复和双方争抢，不符合“发现资源点”的玩法目标。

## 3. 周期规则

- 选择 3B 后不立即生成；首次触发为选择后 9 秒，之后每 9 秒一次。
- 每个安柏单位独立记录 `nextDiscoveryTick`，按稳定单位顺序结算。
- 到点时先推进下一周期，再判断存活与候选格，防止死亡或失败积累补发。
- 安柏死亡时跳过当次；复活后按下一既定窗口触发，不补发死亡期间节点。
- 多个安柏互不计时共享；生成的节点本身为中立资源，不设归属。
- 发现逻辑嵌入现有 `BattleResolver.ResolveHarvestTick`，Live、客户端同构 Sim 与 ReplayRunner 共用同一逐 tick 入口。

## 4. 位置选择

- 以安柏连续 cm 坐标为圆心，候选格心欧氏距离 `<=300cm`。
- 候选格必须满足：
  - `Map.HasTile` 且 Fly 可通行，保证安柏能够接近采集；
  - 当前没有存活资源点，不覆盖地图原堆或此前发现的堆；
  - 允许与单位同格，因为资源点不占体积、不参与阻挡。
- 候选格按 `(distanceSquared, x, y)` 全序排序后，由战斗种子 RNG 抽取，禁止 `UnityEngine.Random`、字典序随机或墙钟随机。
- 没有候选格时跳过本轮，下一周期重试；不推迟到同帧反复搜索，不覆盖旧节点。
- 未采完的节点持续存在；本需求不增加数量上限或生命周期。地图无候选格是自然停止条件。

## 5. 资源点规则

- 新节点类型沿用 `ResourceNodeKind.MoraPile`。
- `remainingCharges=3`，普通读条时间使用资源点默认值 3 秒。
- 每次采集仍获得 `ResourceNodeMetrics.MoraPerHarvest=5` 摩拉，余量归零后按既有规则消散。
- 普通读条、安柏移动快速采集、1A 双倍采集、采集额外体力/元能、兔兔伯爵耐久恢复均复用现有链路。
- 发现后为中立资源，双方均可采集；发现者不享有独占权。
- 发现不等于破雾：节点是否渲染仍由既有迷雾规则决定，AI 采集感知规则不变。

## 6. 配置、文本与旧 3B 清理

新增技能参数，全部数据驱动：

| 参数 | 值 | 含义 |
|---|---:|---|
| `C3DiscoveryInterval` | 9 | 发现周期（秒） |
| `C3DiscoveryRadius` | 300 | 发现半径（cm） |
| `C3DiscoveryCharges` | 3 | 每处可采次数 |

- 新 3B 资产不再携带 AttackSpeed 原子，移除旧 `C3ReleaseSpeed/C3CooldownReduction` 参数引用。
- 删除 `BattleMetrics.SkillCooldownTicks` 的 3B 冷却特判，恢复通用战技/爆发 CD；HUD 继续调用统一入口。
- 旧枚举参数值与“冷却”Tag 可保留供其他角色使用，但安柏新 3B 不再消费。
- 复用现有 `SkillTag.Resource=5004`（施工核查发现既有定义，取消原“新增 5010”计划），同步 Shared Data 与 zh-Hans、zh-TW、en、ja、ru 五语言。
- 更新 SkillName、SkillDescription、SkillParamName、SkillTag、角色文档和资源总文档；卡片墙与升命弹窗自动读取新资产，不新增专用 UI。

## 7. Host、协议与表现

- `BattleSimState` 增加运行时新增节点 API：校验空格、复制节点、禁止覆盖；采集仍由原 `TryHarvestResourceNode` 扣减。
- 增加每单位发现计时状态，按 `unitId` 排序，纳入快照与状态 hash。
- 新增 `ResourceNodeDiscovered` 命令，携带完整节点数据（kind/cell/remainingCharges/harvestSeconds），用于发现当拍即时表现。
- `BattleBoard` 增加单节点 Upsert/重建入口；采集命令继续处理后续减件与消散，每秒快照继续全量自愈。
- 节点 kind、采集秒数、余量和发现计时均进入确定性校验，避免只有格子和余量一致而规则状态已分叉。
- ReplayRunner 已在分离后、AI 前调用 `ResolveHarvestTick`，本次不另建第二条发现调度链。

## 8. 验收清单

1. 选择 3B 当拍不生成；第 9 秒首次发现，之后每 9 秒一处。
2. 节点只落在 300cm 内合法空格，不覆盖旧堆；无候选时跳过且不补发。
3. 每处恰可采 3 次，每次 5 摩拉，采空消失；普通采集与快速采集均可消费。
4. 死亡跳过、复活不补发；多个安柏各自计时；节点可被双方采集。
5. 1A 对新节点可双倍采集，兔兔伯爵正常恢复耐久；发现本身不破雾。
6. 旧 3B 不再减少 CD、不再增加释放速度，战技/爆发其他数值不变。
7. Host、客户端、快照、命令流、逐秒 hash 与 ReplayRunner 一致；同一 seed 重放选点一致。
8. 五语言名称、Tag、参数和描述完整，卡片墙与升命弹窗显示“循迹觅宝/资源”。

## 9. 验证边界

Mac 侧执行静态结构、配置引用、五语言、确定性与 `git diff --check`。Tuanjie 编译、PlayMode 周期实测和新基线回放仍需在可用引擎环境完成，不以静态检查替代实机结论。

## 10. 施工记录（2026-11-11）

- 3B 内部语义名与资产改为 `Amber_C3B_TreasureHunt`，保留技能 ID `3001012` 和原 GUID `5c585437e7264bc0bb3f2e0b0ebcc733`；图标从旧双箭图标改为既有摩拉物品图标。
- 资产使用既有 `SkillTag.Resource=5004`，配置 `C3DiscoveryInterval=9`、`C3DiscoveryRadius=300`、`C3DiscoveryCharges=3`，效果原子为空；五语言名称、描述和参数名已同步。
- `BattleResolver.ResolveHarvestTick` 在普通/快速采集前执行每单位发现；候选格确定性排序后由 `BattleSimState` 的 xorshift32 RNG 抽取。
- `BattleSimState` 支持运行时新增节点与发现计时，快照按稳定顺序输出节点/计时；hash 新增节点 kind、harvestSeconds、发现 tick，RNG state 沿用既有覆盖。
- 新增 `BattleCommandType.ResourceNodeDiscovered=26` 和完整节点载荷；`BattlePlayer/BattleBoard` 即时 Upsert 表现并刷新迷雾显隐，快照继续全量自愈。
- 旧 3B AttackSpeed 原子和冷却特判已删除，Host/HUD 使用通用 `SkillCooldownTicks(SkillType)`。
- 角色文档、命座总表、资源总文档已更新；旧即时制设计中的 3B 结论已标记推翻并指向本文。
- 后续调参（2026-11-11 用户拍板）：`C3DiscoveryInterval` 由 6 秒改为 **9 秒**；全角色 `BurstCooldownSeconds` 由 5 秒改为 **6 秒**，对应 `BurstCooldownTicks=120`。移动快速采集 6 秒与 2A 暴露 6 秒不变。
