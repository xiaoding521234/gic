# 51-帧同步 F0~F2 交接与 Windows 验证清单

> **交接日期**：2026-11-05
> **分支**：`feature/frame-sync`（基于 master `9ea7475d`）
> **状态**：F0 完成、F1/F2 核心完成；**F3 本地预测/回滚未实现**
> **环境限制**：Mac 无 Tuanjie 编译环境，本批 **436+ 行改动全部未编译**；Windows 侧第一件事是编译排错。

---

## 1. 提交清单

| 提交 | 内容 |
|---|---|
| `43bbd302` | 设计稿：`docs/active/50-帧同步联机架构设计与实施计划.md` |
| `087b5f95` | 设计修订：网络窗口 4→2 tick，预测回滚提为首期必做 |
| `85c5e678` | **F0**：确定性 RNG / 遍历序 / 审计闸门 |
| `8c10a007` | **F0-5**：ReplayRunner 双跑确定性菜单 |
| `4bd6093e` | **F1+F2**：双端同构 + 本地 Sim 驱动 View |

---

## 2. 当前架构（改完后）

```
单机（IsNetworked=false）：输入即时结算（与改造前一致，零手感损失）

LAN Host（IsNetworked=true, IsHost=true）：
  本地输入 → EnqueueInput(targetTick=当前+2)
  AI 三脑   → EnqueueInput(targetTick=当前+1)
  每个 tick：ApplyDueInputs → 排序(playerId,seq) → 广播 InputBatch → 本地结算
  每秒：记录 hostHash，收客户端 HashReport 比对

LAN Client（IsNetworked=true, IsHost=false）：
  输入 → Flow.OnSubmitAction → 校验 → ClientSend(InputCommand)
  收到 BattleStart → 建 Sim（同 seed/map/roster）→ 建单位 → Flow.StartBattle()
  每个 tick：ApplyDueInputs（按 Host 批内序）→ 本地结算
  每秒：ClientSend(HashReport)
  View：本地 TurnResolver 产出的 Segment/Snapshot 直喂 BattlePlayer（F2）
```

**唯一允许的分叉**：AI 只在 Host 跑；客户端不跑脑、不定序。

---

## 3. Windows 第一优先级：编译排错

按以下顺序检查（本批最可能的编译/运行断点）：

### 3.1 类型/命名空间

| 文件 | 风险点 |
|---|---|
| `Data/Battle/Protocol/BattleMessages.cs` | `BattleStartMessage.roster` 类型 `List<GIC.Battle.BattlePlayerSetup>`（`GIC.Data` 引 `GIC.Battle`，同程序集跨命名空间）；新增 4 个消息类 |
| `Battle/Flow/BattleSession.cs` | 新增 `using System.Linq;`；`ClientLoopbackTransport` 使用 `BattleJsonSerializer.Instance`；`BuildClientSimUnits` 使用 `FirstOrDefault/AsCardId` |
| `Battle/Flow/RTSFlowController.cs` | `SortedDictionary<int, List<OrderedInput>>`、`InputCommandMessage/InputBatchMessage/HashReportMessage/DesyncAlertMessage`（均在 `GIC.Data`）；`ApplyDueInputs` 调 `ResolveAiActions(List<ActionData>)` |
| `UI/Screen/Battle/BattleScreen.cs` | 新增 `if (_session.IsHostSide) { ... }` 大块；`MirrorBattleTransport(isHost:...)` 构造；`isLanClient` 分支 |
| `Battle/Flow/BattleMetrics.cs` | 新增 `InputDelayTicks/MaxInputLatenessTicks/HashReportIntervalTicks` |
| `Battle/Flow/DeterministicRng.cs` | 新文件（含 `.meta`） |

### 3.2 运行断点

1. **客户端 `_session.Sim` 为 null**：`BattleScreen` 的 Host 装配块已用 `if (_session.IsHostSide)` 包住；确认块外没有残留 `_session.Sim.*`。
2. **客户端建单位 unitId 对齐**：`BuildClientSimUnits` 按 `msg.roster` 顺序建协议核心（`UnitFactory.CreateUnitWithData` + `RegisterUnit`），假设 Host 也是同序注册。若 hash 从第一秒就漂，优先查这里。
3. **`BattlePlayer.Bind(network, null)`**：客户端 map 随 `BattleStart` 到达；确认 `Bind` 不依赖 map。
4. **网络 Segment 与本地 Segment 双播**：`CreateClientSim` 的网络路由只注册 `BattleStart/InputBatch/DesyncAlert`；本地 Segment 走 `_localRouter`。确认 `BattleMessageRouter.Handle` 对未注册类型是静默忽略而不是抛异常。
5. **`MirrorBattleTransport` 构造时机**：`CreateHosted/CreateClientSim` 里 new 传输并注册 Mirror handler；确认房间/战斗切换时不会重复注册（`ReplaceHandler` 已是替换语义）。

---

## 4. 第二优先级：重录回放基线（必做）

`System.Random` → `DeterministicRng`（xorshift32）会改变暴击序列，**旧回放基线全部失效**：

1. 游戏内 `replay on` 打一局（完整终局）。
2. `Tools/TG/回放回归（最新录制）` → 会报 hash 漂移（预期）。
3. 以新录制为新基线；旧基线文件保留作历史对照。
4. `Tools/TG/回放回归（双跑确定性）` → 应 PASS（摘要逐字一致）。

---

## 5. 第三优先级：LAN 双机 hash 验证

1. 两台机同局域网，Host 建房 + 客户端加入。
2. 打满 10 分钟（或至终局）。
3. 观察 Console：
   - 正常：无 `[Desync]` 日志。
   - 异常：`[Desync] tick=... host=... client=...` → 记录首个漂移 tick，按 tick 回溯该秒的输入批/单位状态。
4. 若从 tick 20（第一秒）就漂：优先查「客户端建单位顺序/初始手牌/seed/迷雾模式」。
5. 若中途漂：优先查「AI 批是否只在 Host 生成、InputBatch 是否同 tick 应用、字典遍历是否还有遗漏」。

---

## 6. 第四优先级：观察局 AI 回归

- 单机观察局两局（None 雾 / Skirmish 雾），确认 AI 行为、终局与改造前一致。
- 重点：AI 节拍从「即时结算」改为「targetTick=当前+1」后，AI 反应是否仍符合预期（应无可感知差异）。

---

## 7. F3 剩余工作（本地预测 + 回滚）

**未实现**。建议在 F1/F2 编译通过、双机 hash 全一致后再做。

### 7.1 预测范围（首期）

| 输入 | 预测 | 说明 |
|---|---|---|
| 移动 | ✅ | 延迟最敏感 |
| 战技/爆发 | ✅ | 本地预检 CD/体力/元能/射程 |
| 瞄准指示器 | ✅（纯表现） | 无需回滚 |
| 部署/购买/升命/装备/物品 | ❌ | 经济副作用大，等权威 |

### 7.2 回滚机制（建议实现顺序）

1. **检查点**：`BattleSimState` 新增 `CaptureCheckpoint()` / `RestoreCheckpoint()`；每 20 tick 存一份（或每 tick 轻量 ring buffer，实测选型）。
2. **输入环形缓冲**：保留最近 40 tick 的已确认 InputBatch + 本地预测输入。
3. **预测应用**：客户端本地输入立即应用到 Sim 并驱动 View，同时缓存预测标记。
4. **权威校正**：`InputBatch(tick)` 到达后比对：
   - 一致 → 确认；
   - 不一致/被拒 → 回滚到 `tick` 前检查点 → 重放权威输入 → 追到当前 tick → 表现层校正。
5. **预算熔断**：≤4 tick 重放 ≤2ms；超预算降级「快照拉回 + 表现淡入」。
6. **表现层**：本地预测态/远端确认态分流；误差 <15cm 平滑、>15cm 拉回；被拒技能 VFX 淡出 + CD/资源回滚。

### 7.3 为什么没有盲写

- `Unit` 是 MonoBehaviour 组件图（UnitStats/UnitMoveable/Buff/装备/地脉维度），深拷贝/恢复不是加个方法就能可靠完成的。
- 回滚预算、误差阈值、表现层分流必须实机调参。
- F1 本身 436 行未编译，叠加 F3 会变成两层未验证代码。

---

## 8. 已知风险 / TODO

| # | 项 | 说明 |
|---|---|---|
| 1 | `BattlePlayer.Bind(network, null)` | 客户端 map 为 null，确认 `Bind` 不依赖 |
| 2 | 客户端 unitId 对齐 | 名册序假设；若首秒漂移优先查 |
| 3 | `BattleStartMessage.roster` 序列化 | `BattlePlayerSetup` 含枚举字段，确认 `BattleJsonSerializer` 能处理 |
| 4 | `ApplyDueInputs` 的 AI/玩家混合排序 | 当前按 `(playerId, seq)` 排序；AI 与玩家交错时按批内序结算，需实机确认与旧行为一致 |
| 5 | 单机性能 | `SortedUnits()` 每次分配列表，`GetUnitsAt` 等热点后续可加每 tick 缓存 |
| 6 | 浮点跨平台 | ProjectileResolver/EffectCompiler 的 `Mathf.Sqrt` 同平台确定，跨平台定点化=F4 |
| 7 | 客户端 `BattleStart` 前 `Flow.Update` | `_sim=null`、`Phase=Idle`，`Update` 直接 return；确认无 NRE |

---

## 9. 设计文档索引

- `docs/active/50-帧同步联机架构设计与实施计划.md`（协议/时序/确定性纪律/分期）
- `docs/active/49-即时制残留语义审计.md`（P6 收尾背景）
- `docs/active/43-联机架构对齐大厂优化技术设计.md`（N1~N6 路线图）
- `docs/active/48-即时制改造技术设计与实施计划.md`（tick/确定性底座）

---

## 10. 快速命令

```bash
# 切分支
git fetch origin && git checkout feature/frame-sync

# 确定性审计（Mac/Linux；Windows 可用 Git Bash）
./tools/determinism-audit.sh

# 严格模式（硬违规即失败）
./tools/determinism-audit.sh --strict
```

Unity 菜单：
- `Tools/TG/回放回归（最新录制）`
- `Tools/TG/回放回归（双跑确定性）`
- `Tools/TG/回放回归（自测·合成局闭环）`