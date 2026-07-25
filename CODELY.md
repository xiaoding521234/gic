

## Codely Structured Memories

### User

### Feedback
- [2026-07-25 09:27:44] User prefers Chinese field names for Inspector-exposed serialized fields (e.g. 光柱颜色, 上升时间). When creating UI/effect components, use Chinese [SerializeField] names and [Header] labels instead of English. **Why:** User explicitly asked to change LightPillarEffect fields from English to Chinese for readability. **How to apply:** New MonoBehaviour components with Inspector-facing fields should use Chinese names by default.
### Project
- [2026-07-25 11:25:28] Card/backpack architecture refactored to data-driven design (2026-07-08): CardDetailView is now a container with UnitDetailPanel/ItemDetailPanel sub-panels, ICardConfig.GetBackpackTab() + PlayerSaveData.ownedCards replaced hard switch, IUsable interface for item use, CardPool for object pooling. **Why:** Original architecture was rigid — adding new item types required editing enums, switch statements, and UI headers across multiple files. **How to apply:** New card types only need a new ICardDetailPanel + CardViewStrategyFactory case; new item use functionality only needs IUsable implementation on ItemData. - [2026-07-25 11:23:00] Category enum deleted, replaced by BackpackTab (7 tabs: Character/Creation/Equipment/Consumable/Material/Currency/Quest) + ItemSubType (7 subtypes: Currency/Weapon/Armor/Food/Drink/Material/Quest). ItemData.subType drives backpack tab via ItemSubTypeExtensions.ToBackpackTab(). UnitData.unitType drives Character/Creation tabs. SaveManager no longer routes by PreciousItem — all items go to ownedNormalItems + RebuildOwnedCards(). **Why:** Old Category (Character/CommonItem/PreciousItem) mixed rarity with item type, didn't reflect actual item categories. **How to apply:** Adding new item types only needs a new ItemSubType value + ToBackpackTab mapping case; adding new backpack tabs only needs a BackpackTab value + TabOrder entry.


### Reference

