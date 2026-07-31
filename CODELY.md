

## Codely Structured Memories

### User
- [2026-07-27 14:07:54] User is experienced with Java backend application development (Spring Boot, etc.). When explaining Agent/AI concepts, use Java backend analogies (Controller/Service/Mapper, Spring patterns, etc.) instead of Python/AI-native analogies.

### Feedback
- [2026-07-25 09:27:44] User prefers Chinese field names for Inspector-exposed serialized fields (e.g. 光柱颜色, 上升时间). When creating UI/effect components, use Chinese [SerializeField] names and [Header] labels instead of English. **Why:** User explicitly asked to change LightPillarEffect fields from English to Chinese for readability. **How to apply:** New MonoBehaviour components with Inspector-facing fields should use Chinese names by default.
### Project





- [2026-07-27 20:02:35] Game design documents split into docs/ directory (was GameDesign.md, split 2026-07-27). "协议核心" is a simultaneous-turn card-based tactical wargame (原神IP, up to 6 players, LAN multiplayer via Mirror). Core loop: 祈愿解锁→局前选8种卡→同时选1行动→攻速排序执行→摧毁核心掠夺→原石结算. Key design: (1) 纠缠 merged into 摩拉 as sole deployment currency, 5/turn auto; (2) 6 factions mixable (蒙德延奏/璃月契约/稻妻连携/挪德双祝福命座/纳塔夜魂/须弥智慧数据库); (3) 命座0-3, 重复出战升命; (4) 战败=世界树+协议核心全毁→放逐+观战; (5) 建筑=科技树节点, 买卡费用=出战费一半. **Why:** Tracks evolving game design for implementation planning. **How to apply:** Reference when implementing battle system, faction mechanics, or economy; design is actively iterating.
- [2026-07-28 09:14:44] GIC (协议核心 game project) targets全平台互通 (cross-platform: PC + mobile + console). Project name "GIC" derived from directory D:\Tuanjie_editor\gic.
- [2026-07-28 09:16:21] GIC project: solo developer (1人), personal Demo/portfolio goal, NOT commercial launch. Target audience: both Genshin players and strategy/tactical gamers. **Why:** Solo dev with limited resources — scope must be drastically cut from GDD ambition. **How to apply:** Recommend 2-player 1v1 over 6-player, 2-3 factions over 7, hotseat/local before Mirror networking, vertical slice over breadth.


- [2026-07-29 14:26:28] Unit data file at docs/unit-data.md (created 2026-07-29): Markdown-based character config spec. AI reads this file to sync UnitConfig.asset / SkillName enum / SkillParamKey enum / localization tables. Contains field reference tables (weapon defaults, skill types, param base types), Amber as complete template (6 skills incl. DoubleShot), and HTML-comment template block for new characters. **Why:** eliminates manual multi-file editing when adding/modifying characters — edit one MD, AI applies changes everywhere. **How to apply:** user edits unit-data.md, asks AI to apply; AI updates enum + UnitConfig.asset + localization tables per gic-localization skill.
- [2026-07-29 22:56:01] GIC battle system architecture decision (2026-07-29): Will use StS-style "Action Queue (coroutine, serial) + DamagePipeline (sync, Phase hooks)" pattern. EventBusHub/LocalEventBus retained for UI/network layer only — battle logic does NOT go through EventBus. Key mappings: AbstractGameAction→BattleAction(IEnumerator Execute()), addToBot→ActionQueue.Enqueue(), DamageInfo.applyPowers→DamagePipeline.Process(), AbstractPower hooks→IDamageHook+DamagePhase enum (PreDamage/Calculate/PostDamage/OnDeath), isDone→yield return. No R/D two-phase needed (sync pipeline makes it unnecessary vs Java mod's frame-driven queue). **Why:** User confirmed turn-based = strict serial, no concurrency. StS validates this exact pattern. **How to apply:** When implementing battle system, create ActionQueue + DamagePipeline as separate layer from existing EventBus.










### Reference
- [2026-07-28 16:17:53] Localization CSV tool at Tools/Localization/CSV 导出导入 (created 2026-07-28). Exports/imports all 21 TableName localization tables to/from CSV ({TableName}.csv). CSV format: Key,Id,zh-Hans,zh-TW,en,ja,ru. RFC 4180 compliant (supports commas/quotes/newlines in values). Smart matching: Id first, then Key, then create new. **Why:** Unity Localization YAML is unicode-escaped and hard to batch-edit. **How to apply:** use this tool instead of Unity Localization Window for bulk edits; CSV folder defaults to Export/Localization.
- [2026-07-29 17:22:57] MC mod gichess (old project, Java/NeoForge): Java source at D:\Game\mod\wg-template-1.21.4\src\main\java\com\wg\gichess\ (308 files). Extracted jar at D:\Picture\gichess\my\wg-0.2.d. Architecture: EventBus (frame-driven queue, owner-order + priority sorting, LOCK animation blocks queue), SkillExecutionE.R/D two-phase events, DamageS.DEFFAULT (synchronous damage pipeline: element attach → reaction loop → formula → shield → HP → DamageA animation), AnimationManager (dual-channel: concurrent + serial-locked), ChessBoard.post() sets event order = firstRoles+secondRoles+board. ~20+ characters (Amber/Barbara/Kaeya/Lisa/Klee/Jean/Citlali/HaborymBoss etc), 7 element types + 18 reactions, Mondstadt延奏/Natlan夜魂 implemented. **Why:** validates GIC core gameplay and provides battle system architecture reference for Unity port. **How to apply:** reference for Unity系分 — map EventBus→EventBusHub(with owner ordering), LOCK animation→coroutine yield, DamageS→DamageCalculator(synchronous), AnimationManager→Unity Animator+VFX, SkillExecutionE.R/D→ActionRequest/ActionResult events.
- [2026-07-30 09:26:28] GIC MCP Server created (2026-07-29): Located at .codely-cli/mcp/gic-mcp-server/src/index.js. Provides 6 tools: query_units, query_skills, query_items, query_localization, query_unit_data, query_docs. Parses Unity YAML .asset files (UnitConfig.asset, ItemConfig.asset) and docs/ markdown. Configured in .codely-cli/settings.json under mcpServers.gic. Enum mappings fixed: WeaponType starts at 0 (Claymore), FactionType uses large values (Celestia=1001, Nodkrai=2001, Mondstadt=3001, Liyue=4001...). Extensions (codely-unity-lsp-server + TJGenerators) moved from project-level to global (~/.codely-cli/extensions/) to avoid duplication across gic and umc workspaces.
