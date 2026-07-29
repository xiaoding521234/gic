# 角色配置数据

> 在此文件中修改或新增角色数据。AI 会根据此文件内容同步更新 UnitConfig.asset、本地化表、枚举等。
> 字段留空或写 `auto` 表示使用默认派生值（按星级/武器类型自动计算）。

## 字段说明

| 字段 | 类型 | 说明 |
|------|------|------|
| starLevel | 1-5 | 星级，决定出战摩拉(1→10/2→20/3→50/4→100/5→300)和基础HP(1→150/2→200/3→200/4→250/5→300) |
| weaponType | 枚举 | Claymore/Sword/Polearm/Gauntlet/Catalyst/Bow/Gun，决定基础攻击力和攻速 |
| selfElement | 枚举 | Physical/Pyro/Hydro/Anemo/Electro/Cryo/Dendro/Geo/Light |
| normalMoveType | 枚举 | Walk/Fly/Amphibious，飞行不受步行地形限制 |
| baseHP | int/auto | 留空则按星级派生 |
| baseAttack | int/auto | 留空则按武器类型派生 |
| baseAttackSpeed | int/auto | 留空则按武器类型派生 |
| baseMoveSpeed | int/auto | 默认3 |
| visionRange | int/auto | 默认1 |
| deployCost | int/auto | 留空则按星级派生 |
| factions | 枚举列表 | Mondstadt/Liyue/Inazuma/Sumeru/Fontaine/Natlan/Nodkrai/Celestia/Snezhnaya/Khaenriah/Special |
| tags | 枚举列表 | Gathering/BurstDamage/StableDamage/AttackUp/DefenseUp/Vision/Agile/Heavy/Healing/Control/Push/Dash/CoordinatedAttack/Resource/Revive/FastEnso 等 |

### 武器默认值

| 武器 | 攻击力 | 攻速 |
|------|:------:|:----:|
| Claymore | 60 | 20 |
| Sword | 40 | 40 |
| Polearm | 40 | 50 |
| Gauntlet | 40 | 60 |
| Catalyst | 60 | 10 |
| Bow | 50 | 30 |
| Gun | 50 | 70 |

### 技能类型

| 类型 | 说明 | 消耗体力 |
|------|------|:--------:|
| Move | 移动 | ✅ |
| Normal | 战技 | ✅ |
| Burst | 爆发 | ✅ |
| Talent | 天赋（命座） | ❌ |
| Enso | 延奏（蒙德） | ❌ |
| Henka | 变奏（蒙德） | ❌ |
| Contract | 契约（璃月） | ❌ |
| Renkei | 连携（稻妻） | ❌ |
| Wisdom | 智慧（须弥） | ❌ |

### 参数基准类型（SkillBaseType）

| 类型 | 说明 | 展示格式 |
|------|------|---------|
| Fixed | 固定值 | `4` |
| BasedOnAttack | 攻击力 | `40%攻击力` |
| BasedOnMaxHealth | 最大生命值 | `20%最大生命值` |
| BasedOnMoveSpeed | 移速 | `100%移速` |
| BasedOnDefense | 防御力 | `30%防御力` |

---

## 安柏 (Amber)

- UnitName: `Amber` (枚举值 3001)
- 势力: Mondstadt
- 星级: 3
- 武器: Bow
- 元素: Pyro
- 移动方式: Fly
- 出战摩拉: auto (→50)
- 生命值: auto (→200)
- 攻击力: auto (→50)
- 攻速: auto (→30)
- 防御力: 0
- 移速: 5
- 视野: 1
- 碰撞: 阻挡友方/敌方/被敌方阻挡 = true/true/true
- 标签: Gathering, BurstDamage, AttackUp, Vision

### 技能

#### 1. 飞行冠军 (Amber_FlyingChampion)

- 类型: Move
- 图标: fly

| 参数 | 枚举 | 值 | 基准 |
|------|------|:--:|------|
| 移动距离 | MoveDistance | 100 | BasedOnMoveSpeed |
| 采集次数 | CollectCount | 2 | Fixed |

**描述模板 (zh-Hans):**
```
·选择十字方向其一飞行
·采集经过的资源点{CollectCount}次
```

---

#### 2. 一箭双丘丘 (Amber_DoubleShot)

- 类型: Normal (战技)
- 图标: bow

| 参数 | 枚举 | 值 | 基准 |
|------|------|:--:|------|
| 伤害 | Damage | 40 | BasedOnAttack |
| 伤害次数 | DamageCount | 2 | Fixed |

**描述模板 (zh-Hans):**
```
·选择十字方向其一，射出{DamageCount}发箭矢
·每发造成{Damage}火伤
```

---

#### 3. 箭雨 (Amber_ArrowRain)

- 类型: Burst
- 图标: arrow_rain

| 参数 | 枚举 | 值 | 基准 |
|------|------|:--:|------|
| 伤害 | Damage | 40 | BasedOnAttack |
| 伤害次数 | DamageCount | 4 | Fixed |
| 元能消耗 | EnergyCost | 3 | Fixed |

**描述模板 (zh-Hans):**
```
·选择十字方向其一射出大量箭矢
·造成{DamageCount}次{Damage}火伤
```

---

#### 4. 百发百中 (Amber_Sharpshooter)

- 类型: Enso (蒙德延奏)
- 图标: sharpshooter

| 参数 | 枚举 | 值 | 基准 |
|------|------|:--:|------|
| 攻击提升 | ATKBonus | 10 | Fixed |
| 元能消耗 | EnergyCost | 2 | Fixed |

**描述模板 (zh-Hans):**
```
·被协者提升{ATKBonus}攻击
```

---

#### 5. 全面侦查 (Amber_Scouting)

- 类型: Henka (蒙德变奏)
- 图标: reveal

| 参数 | 枚举 | 值 | 基准 |
|------|------|:--:|------|
| 生效半径 | EffectRadius | 3 | Fixed |

**描述模板 (zh-Hans):**
```
·破除{EffectRadius}格范围迷雾并解除敌人隐身
```

---

#### 6. 安柏命座 (Amber_Constellation)

- 类型: Talent
- 图标: constellation

| 参数     | 枚举          |  值  | 基准    |
| ------ | ----------- | :-: | ----- |
| 1命移速提升 | C1MoveSpeed |  1  | Fixed |
| 1命视野提升 | C1Vision    |  1  | Fixed |
| 2命移速提升 | C2MoveSpeed |  2  | Fixed |

**描述模板 (zh-Hans):**
```
·【1命】移速提升{C1MoveSpeed}，视野提升{C1Vision}
·【2命】移速提升{C2MoveSpeed}，爆发可以破除迷雾
·【3命】获得<color=#FFD700><u><link="Unit:Amber">兔兔伯爵</link></u></color>
```

> 注：3命目前关联安柏角色卡用于测试，后续改为关联兔兔伯爵物品卡

---

<!--
## 模板：复制此块新增角色

## 角色名 (EnumName)

- UnitName: `EnumName` (枚举值 XXXX)
- 势力: FactionName
- 星级: X
- 武器: WeaponType
- 元素: ElementType
- 移动方式: Walk/Fly/Amphibious
- 出战摩拉: auto 或具体值
- 生命值: auto 或具体值
- 攻击力: auto 或具体值
- 攻速: auto 或具体值
- 防御力: X
- 移速: X
- 视野: X
- 碰撞: 阻挡友方/敌方/被敌方阻挡 = true/true/true
- 标签: Tag1, Tag2

### 技能

#### 1. 技能名 (EnumName_Skill1)

- 类型: Move/Normal/Burst/Talent/Enso/Henka
- 图标: icon_name

| 参数 | 枚举 | 值 | 基准 |
|------|------|:--:|------|
| 参数名 | ParamKey | X | BaseType |

**描述模板 (zh-Hans):**
```
·描述内容，含{ParamKey}占位符
```

#### 2. 技能名 (EnumName_Skill2)
...

#### 3. 命座 (EnumName_Constellation)

- 类型: Talent

| 参数 | 枚举 | 值 | 基准 |
|------|------|:--:|------|
| C1效果 | C1Xxx | X | Fixed |

**描述模板 (zh-Hans):**
```
·【1命】效果描述{C1Xxx}
·【2命】效果描述
·【3命】效果描述
```

-->
