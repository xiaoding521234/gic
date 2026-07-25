

## Codely Structured Memories

### User

### Feedback
- [2026-07-25 09:27:44] User prefers Chinese field names for Inspector-exposed serialized fields (e.g. 光柱颜色, 上升时间). When creating UI/effect components, use Chinese [SerializeField] names and [Header] labels instead of English. **Why:** User explicitly asked to change LightPillarEffect fields from English to Chinese for readability. **How to apply:** New MonoBehaviour components with Inspector-facing fields should use Chinese names by default.
### Project
- [2026-07-25 13:17:47] Card/backpack architecture refactored to data-driven design (2026-07-08): CardDetailView is now a container with UnitDetailPanel/ItemDetailPanel sub-panels, ICardConfig.GetBackpackTab() + PlayerSaveData.ownedCards replaced hard switch, IUsable interface for item use, CardPool for object pooling. **Why:** Original architecture was rigid — adding new item types required editing enums, switch statements, and UI headers across multiple files. **How to apply:** New card types only need a new ICardDetailPanel + CardViewStrategyFactory case; new item use functionality only needs IUsable implementation on ItemData. - [2026-07-25 11:23:00] Category enum deleted, replaced by BackpackTab (7 tabs: Character/Creation/Equipment/Consumable/Material/Currency/Quest) + ItemSubType (7 subtypes: Currency/Weapon/Artifact/Food/Drink/Material/Quest). ItemData.subType drives backpack tab via ItemSubTypeExtensions.ToBackpackTab(). UnitData.unitType drives Character/Creation tabs. **Why:** Old Category (Character/CommonItem/PreciousItem) mixed rarity with item type, didn't reflect actual item categories. **How to apply:** Adding new item types only needs a new ItemSubType value + ToBackpackTab mapping case; adding new backpack tabs only needs a BackpackTab value + TabOrder entry.

- [2026-07-25 13:06:15] ItemSubType.Armor renamed to ItemSubType.Artifact (圣遗物) on 2026-07-25. Enum value stays 2 (no asset migration needed). Localization Entry Key changed from "Armor" to "Artifact" — may need to update localization table if "Armor" entry existed.
- [2026-07-25 13:17:49] Card type cleanup completed (2026-07-25): (1) ItemTag enum deleted entirely — ItemSubType is now the sole item classification; ItemData.tags field removed, SortOrder uses (int)subType. (2) ownedValuableItems list deleted from PlayerSaveData — all items unified into ownedNormalItems. (3) Card.cardType and CardDetailView.cardType changed from mutable fields to read-only derived properties (=> saveCardData.id.cardType). **Why:** ItemTag and ItemSubType were redundant; ownedValuableItems was dead legacy that SyncMissingItems never populated; cardType was set in 3 places but should derive from data. **How to apply:** Don't re-introduce a separate tag enum for items; don't split item storage by rarity/value; cardType is always derived from CardId, never assign it.

### Reference

