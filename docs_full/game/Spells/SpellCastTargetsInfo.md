<!-- provenance: verbose -->
# SpellCastTargetsInfo

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# SpellCastTargetsInfo

## Purpose & Responsibilities

`SpellCastTargets` is a data structure that encapsulates targeting information for spell casting in the MaNGOS server. It bridges high-level casting intent with low-level network packets and runtime object resolution. The class maintains two parallel representations of targets:
1.  **Raw Identifiers:** `ObjectGuid`s and coordinate floats (`m_srcX/Y/Z`, `m_destX/Y/Z`). These are stable, serializable, and used for network transmission and initial validation.
2.  **Resolved Pointers:** Direct pointers to `Unit*`, `GameObject*`, and `Item*`. These are volatile and must be refreshed via `Update` before use, as targets may move, die, or leave the world.

It supports various target types defined by `m_targetMask`, including units, game objects, corpses, items (including trade window slots), source/destination coordinates, and string targets. The class handles serialization (`read`/`write`) into/from `ByteBuffer`s, adhering to client-specific packing formats.

## Member-by-Member Behavior

### Initialization and State Management

**`SpellCastTargets` (ctor)**
Initializes the object to an empty state. Pointers (`m_unitTarget`, `m_itemTarget`, `m_GOTarget`) are set to `nullptr`. Coordinates are zeroed. `m_targetMask` is set to `0`.

**`operator=`**
Performs a member-wise copy of another `SpellCastTargets` instance, including pointers and GUIDs. Note that copying pointers does not increment reference counts; if the original target is deleted, the copied pointer becomes dangling. Used primarily during spell preparation.

**`IsEmpty`**
Returns `true` if no significant target is set. Checks if `m_GOTargetGUID`, `m_unitTargetGUID`, `m_itemTarget` (pointer), and `m_CorpseTargetGUID` are all null/zero. It does *not* check `m_itemTargetGUID` or coordinate targets. Used by `Spell.Main/CheckCast` to validate target presence.

### Target Setting and Resolution

**`PrepareForSpellSystem`**
Prepares target data for the spell system.
- If `m_targetMask` is `TARGET_FLAG_SELF`, it sets destination coordinates to the caster's position and `m_unitTarget` to the caster.
- If the mask includes `TARGET_FLAG_ITEM` or `TARGET_FLAG_TRADE_ITEM`, it asserts that the caster is a `Player`.
- Calls `Update(caster)` to resolve pending GUIDs into object pointers.

**`setUnitTarget`**
Sets a `Unit` as the primary target. Updates destination coordinates to the unit's position, stores the unit pointer and GUID, and sets `TARGET_FLAG_UNIT`. Returns immediately if target is null.

**`setDestination`**
Sets destination coordinates (`x, y, z`) for location-targeting spells. Updates `m_destX/Y/Z` and sets `TARGET_FLAG_DEST_LOCATION`.

**`setSource`**
Sets source coordinates (`x, y, z`) for spells originating from a specific point. Updates `m_srcX/Y/Z` and sets `TARGET_FLAG_SOURCE_LOCATION`.

**`setGOTarget`**
Sets a `GameObject` as the target. Stores the GO pointer and GUID. Notably, it does *not* set the `TARGET_FLAG_GAMEOBJECT` bit in the mask (the line is commented out), relying on the caller or packet reader to manage the mask.

**`setItemTarget`**
Sets an `Item` as the target. Stores the item pointer, GUID, and entry ID. Sets `TARGET_FLAG_ITEM`. Returns immediately if item is null.

**`setTradeItemTarget`**
Specialized setter for trade items. Sets `m_itemTargetGUID` to `TRADE_SLOT_NONTRADED` (a synthetic GUID representing a slot index), clears the entry, sets `TARGET_FLAG_TRADE_ITEM`, and calls `Update(caster)` to resolve the actual item from the trade window.

**`updateTradeSlotItem`**
Refreshes item target data if the target is a trade item. If `m_itemTarget` exists and the trade flag is set, it updates the GUID and Entry from the current `m_itemTarget` pointer.

**`setCorpseTarget`**
Sets a `Corpse` as the target. Stores only the corpse's GUID in `m_CorpseTargetGUID`. It does not store a pointer to the corpse object.

### Getter Methods

**`getUnitTargetGuid`**, **`getUnitTarget`**, **`getGOTargetGuid`**, **`getGOTarget`**, **`getCorpseTargetGuid`**, **`getItemTargetGuid`**, **`getItemTarget`**, **`getItemTargetEntry`**
Simple accessors returning stored GUIDs, pointers, or entry IDs.

**`getDestination`**, **`getSource`**
Output-parameter style getters that populate `float&` references with stored source or destination coordinates.

### Network Serialization

**`read`**
Deserializes target data from a `ByteBuffer`.
1. Reads `m_targetMask`.
2. If `TARGET_FLAG_SELF`, returns early (resolution deferred to `PrepareForSpellSystem`).
3. Based on mask bits, reads corresponding GUIDs using `ReadAsPackedClientBuildAware()` for Units, GOs, Corpses, and Items.
4. If `TARGET_FLAG_SOURCE_LOCATION` or `TARGET_FLAG_DEST_LOCATION` is set, reads three floats each. Validates coordinates using `MaNGOS::IsValidMapCoord`; throws `ByteBufferException` if invalid.
5. If `TARGET_FLAG_STRING` is set, reads the string target into `m_strTarget`.

**`write`**
Serializes target data to a `ByteBuffer`.
1. Writes `m_targetMask`.
2. Handles the "object GUID" block: If Unit, GO, or Corpse flags are set, writes one GUID field.
   - If `TARGET_FLAG_UNIT`: Writes packed GUID of `m_unitTarget` (or 0 if null).
   - Else if `TARGET_FLAG_GAMEOBJECT`: Writes packed GUID of `m_GOTarget` (or 0 if null).
   - Else if Corpse flags: Writes `m_CorpseTargetGUID` packed.
   - Else: Writes 0.
   - Packing format depends on `SUPPORTED_CLIENT_BUILD`.
3. Handles the "item GUID" block: If Item or Trade Item flags are set, writes packed GUID of `m_itemTarget` (or 0 if null).
4. Writes source/destination coordinates if their respective flags are set.
5. Writes the string target if the string flag is set.

**`operator<<`**, **`operator>>`**
Inline helpers delegating to `write` and `read`, allowing `SpellCastTargets` to be streamed directly into/from `ByteBuffer`s.

### Pointer Resolution

**`Update`**
Resolves raw GUIDs into object pointers using the provided `SpellCaster`. Critical because pointers can become stale.
1. **GameObjects:** Looks up `m_GOTargetGUID` in the caster's map via `Map.Main/GetGameObject`. Sets `m_GOTarget` to result or `nullptr`.
2. **Units:** If `m_unitTargetGUID` is set:
   - If GUID matches caster, casts caster to `Unit*`.
   - Otherwise, uses `ObjectAccessor::GetUnit` to find the unit globally.
   - Sets `m_unitTarget` to result or `nullptr`.
3. **Items:** Resets `m_itemTarget` to `nullptr`. If caster is a `Player`:
   - If `TARGET_FLAG_ITEM` is set, looks up item by GUID in player's inventory via `Player.Main/GetItemByGuid`.
   - If `TARGET_FLAG_TRADE_ITEM` is set, retrieves trade data via `Player.Main/GetTradeData`, checks if GUID represents a valid trade slot index, and retrieves item from trader's side via `TradeData/GetItem`.
   - If item found, updates `m_itemTargetEntry`.

## Cross-Unit Boundaries

`SpellCastTargets` is a passive data holder that relies on other systems for validation and resolution.

- **Called by `Spell.Main`**: The core spell system uses `SpellCastTargets` extensively. `Spell.Main/SetTargetMap` populates targets; `Spell.Main/UpdatePointers` calls `Update` to refresh pointers before effects execute. Getters like `getUnitTarget` are used in checks (`CheckCast`, `CheckRange`).
- **Called by `WorldSession` handlers**: Handlers like `HandleCastSpellOpcode` and `HandlePetCastSpellOpcode` parse incoming packets, instantiate `SpellCastTargets`, call `read` to deserialize client intent, and pass the object to casting logic.
- **Calls into `Object`/`WorldObject`**: Uses `GetPositionX/Y/Z` and `GetObjectGuid` on `Unit`, `GameObject`, and `Item` to resolve positions and IDs.
- **Calls into `ObjectAccessor`**: `Update` uses `ObjectAccessor::GetUnit` to resolve unit GUIDs to pointers.
- **Calls into `Player`/`TradeData`**: `Update` and `setTradeItemTarget` interact with `Player` to access inventory and trade windows, retrieving items by GUID or slot index.
- **Calls into `Map`**: `Update` uses `Map.Main/GetGameObject` to resolve GO GUIDs within the caster's map.
- **Calls into `Errors`**: `read` throws `ByteBufferException` if coordinates are invalid.

## Data Model

This unit does not interact directly with database tables. It operates entirely on in-memory objects and network packet data.

## Notable Implementation Details

1.  **Pointer Volatility**: Pointers (`m_unitTarget`, etc.) are *not* updated automatically when a target moves or dies. Callers *must* call `Update` before dereferencing pointers. Failure results in undefined behavior.
2.  **Trade Item Specialization**: Trade items use a synthetic GUID (`TRADE_SLOT_NONTRADED`) in packets. `Update` interprets the raw value as a slot index within the active trade window.
3.  **Mask Inconsistency in `setGOTarget`**: `setGOTarget` does *not* set `TARGET_FLAG_GAMEOBJECT` in `m_targetMask`. This relies on the mask being set by the caller or packet reader. Programmatic setting via `setGOTarget` may fail serialization if the mask isn't manually adjusted.
4.  **Coordinate Validation**: `read` strictly validates coordinates against `MaNGOS::IsValidMapCoord`. Invalid coordinates throw a hard exception, dropping the connection.
5.  **Client Build Compatibility**: `write` uses preprocessor checks (`SUPPORTED_CLIENT_BUILD`) to switch between packed and legacy GUID formats.
6.  **Self-Target Optimization**: When `TARGET_FLAG_SELF` is set, `read` returns immediately. Resolution is deferred to `PrepareForSpellSystem`, saving bandwidth.

## Member Reference

**SpellCastTargets** (ctor): Initializes all members to null/zero/default values. Sets `m_targetMask` to 0.

**operator=**: Copies all members from another `SpellCastTargets` instance, including pointers and GUIDs.

**PrepareForSpellSystem**: Resolves self-targets to caster coordinates/unit. Asserts caster is a Player if item targets are present. Calls `Update` to resolve pointers.

**setUnitTarget**: Sets `m_unitTarget`, `m_unitTargetGUID`, and destination coordinates. Sets `TARGET_FLAG_UNIT`.

**getUnitTargetGuid**: Returns `m_unitTargetGUID`.

**getUnitTarget**: Returns `m_unitTarget` pointer.

**getDestination**: Outputs `m_destX`, `m_destY`, `m_destZ` to reference parameters.

**getSource**: Outputs `m_srcX`, `m_srcY`, `m_srcZ` to reference parameters.

**setDestination**: Sets `m_destX/Y/Z` and sets `TARGET_FLAG_DEST_LOCATION`.

**getGOTargetGuid**: Returns `m_GOTargetGUID`.

**getGOTarget**: Returns `m_GOTarget` pointer.

**getCorpseTargetGuid**: Returns `m_CorpseTargetGUID`.

**getItemTargetGuid**: Returns `m_itemTargetGUID`.

**setSource**: Sets `m_srcX/Y/Z` and sets `TARGET_FLAG_SOURCE_LOCATION`.

**getItemTarget**: Returns `m_itemTarget` pointer.

**getItemTargetEntry**: Returns `m_itemTargetEntry`.

**IsEmpty**: Returns true if no Unit, GO, Item (pointer), or Corpse GUID is set.

**setGOTarget**: Sets `m_GOTarget` and `m_GOTargetGUID`. Does *not* set `TARGET_FLAG_GAMEOBJECT` in mask.

**setItemTarget**: Sets `m_itemTarget`, `m_itemTargetGUID`, `m_itemTargetEntry`. Sets `TARGET_FLAG_ITEM`.

**setTradeItemTarget**: Sets synthetic trade GUID, clears entry, sets `TARGET_FLAG_TRADE_ITEM`, calls `Update`.

**operator<<**: Delegates to `write`.

**updateTradeSlotItem**: Updates `m_itemTargetGUID` and `m_itemTargetEntry` from `m_itemTarget` if trade flag is set.

**operator>>**: Delegates to `read`.

**setCorpseTarget**: Sets `m_CorpseTargetGUID` from corpse.

**Update**: Resolves `m_GOTarget`, `m_unitTarget`, and `m_itemTarget` pointers from their GUIDs using the caster's map, accessor, and inventory/trade data.

**read**: Deserializes mask, GUIDs, coordinates, and strings from `ByteBuffer`. Validates coordinates. Throws exception on invalid coords.

**write**: Serializes mask, GUIDs (packed/unpacked based on client build), coordinates, and strings to `ByteBuffer`.

---

<!-- machine-true, projected from graph.json -->

## Map — SpellCastTargetsInfo

*Source:* SpellCastTargetsInfo.cpp, SpellCastTargetsInfo.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| SpellCastTargets | ctor | — | CombatBotBaseAI/CastWeaponBuff, CombatBotBaseAI/EquipOrUseNewItem, Creature.Main/TryToCast, GameObject/AddUniqueUse, GameObject/Use, PetAI/UpdateAI, SpellCaster/CastCustomSpell, SpellCaster/CastSpell, SpellCaster/CastSpell#3, Unit.Main/SendSpellGo, WorldSession.NPCHandler/HandleTrainerBuySpellOpcode, WorldSession.TradeHandler/HandleAcceptTradeOpcode | — |
| operator= | method | — | Spell.Main/prepare, WorldSession.PetHandler/HandlePetCastSpellOpcode, WorldSession.TradeHandler/HandleAcceptTradeOpcode | — |
| PrepareForSpellSystem | method | Errors/PrintStacktraceAndThrow, Object/GetObjectGuid, Object/IsPlayer, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | WorldSession.PetHandler/HandlePetCastSpellOpcode, WorldSession.SpellHandler/HandleCastSpellOpcode, WorldSession.SpellHandler/HandleUseItemOpcode | — |
| setUnitTarget | method | Object/GetObjectGuid, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | CombatBotBaseAI/EquipOrUseNewItem, Creature.Main/TryToCast, GameObject/Use, PetAI/UpdateAI, Spell.Main/CheckCast, Spell.Main/CheckPetCast, Spell.Main/CheckScriptTargeting, Spell.Main/SetTargetMap, SpellCaster/CastCustomSpell, SpellCaster/CastSpell, Unit.Main/AttackerStateUpdate, Unit.Main/SendSpellGo, WorldSession.MiscHandler/HandleSetSelectionOpcode, WorldSession.NPCHandler/HandleTrainerBuySpellOpcode | — |
| getUnitTargetGuid | method | — | Spell.Main/cast, Spell.Main/FillTargetMap, Spell.Main/update, spell_hunter/OnCheckCast, spell_hunter/OnCheckCast#2, Unit.Main/InterruptSpellsCastedOnMe | — |
| getUnitTarget | method | — | PartyBotAI/UpdateAI, Spell.Main/cast, Spell.Main/CheckCast, Spell.Main/CheckItems, Spell.Main/CheckPetCast, Spell.Main/CheckPower, Spell.Main/CheckRange, Spell.Main/CheckScriptTargeting, Spell.Main/CheckTamingSpell, Spell.Main/CheckTarget, Spell.Main/DoSpellHitOnUnit, Spell.Main/FillTargetMap, Spell.Main/finish, Spell.Main/OnSpellLaunch, Spell.Main/prepare#2, Spell.Main/SetTargetMap, Spell.Main/SpellNotifierCreatureAndPlayer, Spell.Main/update, spell_druid/OnCheckCast, spell_item/OnCast, spell_item/OnCheckCast#4, spell_paladin/OnCheckCast, spell_warlock/OnCheckCast, spell_warrior/OnCheckTarget, Unit.AuraProcHandler/HandleProcTriggerSpellAuraProc, WorldSession.SpellHandler/HandleCastSpellOpcode, WorldSession.SpellHandler/HandleUseItemOpcode | — |
| getDestination | method | — | Spell.Effects/EffectLeapForward, Spell.Effects/EffectTeleUnitsFaceCaster, Spell.Main/SetTargetMap | — |
| getSource | method | — | Spell.Main/SetTargetMap | — |
| setDestination | method | — | Creature.Main/TryToCast, Spell.Main/CheckScriptTargeting, Spell.Main/FillTargetMap, Spell.Main/SetTargetMap, SpellCaster/CastSpell, SpellCaster/CastSpell#3 | — |
| getGOTargetGuid | method | — | Spell.Main/cancel, Spell.Main/SetTargetMap | — |
| getGOTarget | method | — | Spell.Effects/EffectSendEvent, Spell.Effects/EffectSummonDemon, Spell.Effects/EffectSummonPlayer, Spell.Main/cancel, Spell.Main/CheckCast, Spell.Main/CheckRange, Spell.Main/finish, Spell.Main/OnSpellLaunch, Spell.Main/prepare#2, Spell.Main/SetTargetMap, Spell.Main/update, spell_special/OnSuccessfulStart | — |
| getCorpseTargetGuid | method | — | Spell.Main/CheckCast, Spell.Main/handle_immediate, Spell.Main/SetTargetMap | — |
| getItemTargetGuid | method | — | Spell.Main/CheckCast, Spell.Main/CheckItems | — |
| setSource | method | — | Creature.Main/TryToCast, Spell.Main/SetTargetMap, SpellCaster/CastSpell | — |
| getItemTarget | method | — | Spell.Main/CheckCast, Spell.Main/CheckItems, Spell.Main/ClearCastItem, Spell.Main/IgnoreItemRequirements, Spell.Main/SetTargetMap, SpellEntry/GetCastTime, spell_item/OnCheckCast#2 | — |
| getItemTargetEntry | method | — | Spell.Main/TakeReagents | — |
| IsEmpty | method | — | Spell.Main/CheckCast | — |
| setGOTarget | method | Object/GetObjectGuid | GameObject/AddUniqueUse, GameObject/Use, Spell.Effects/EffectTransmitted, SpellCaster/CastSpell | — |
| setItemTarget | method | Object/GetEntry, Object/GetObjectGuid | CombatBotBaseAI/CastWeaponBuff, Spell.Main/ClearCastItem, Spell.Main/TakeReagents | — |
| setTradeItemTarget | method | ObjectGuid/ObjectGuid#5 | WorldSession.TradeHandler/HandleAcceptTradeOpcode | — |
| operator<< | function | — | Spell.Main/SendSpellGo, Spell.Main/SendSpellStart, Unit.Main/SendSpellGo | — |
| updateTradeSlotItem | method | Object/GetEntry, Object/GetObjectGuid | Spell.Main/cast | — |
| operator>> | function | — | Pet/ReadFromWorldPacket#4, Spell/ReadFromWorldPacket#4, Spell/ReadFromWorldPacket#6 | — |
| setCorpseTarget | method | Object/GetObjectGuid | Spell.Main/SetTargetMap | — |
| Update | method | Map.Main/GetGameObject, Object/GetEntry, Object/GetObjectGuid, Object/ToPlayer, Object/ToUnit, ObjectAccessor/GetUnit, ObjectGuid/GetRawValue, ObjectGuid/operator==, Player.Main/GetItemByGuid, Player.Main/GetTradeData, TradeData/GetItem, TradeData/GetTraderData, WorldObject.Object/GetMap | Spell.Main/UpdatePointers | — |
| read | method | ByteBuffer/ByteBufferException, ByteBuffer/operator>>, ByteBuffer/operator>>#12, ByteBuffer/operator>>#8, ByteBuffer/rpos, ByteBuffer/size, GridDefines/IsValidMapCoord#3, ObjectGuid/operator>>#2, ObjectGuid/ReadAsPackedClientBuildAware | — | — |
| write | method | ByteBuffer/operator<<, ByteBuffer/operator<<#13, ByteBuffer/operator<<#7, ByteBuffer/operator<<#9, Object/GetPackGUID, ObjectGuid/operator<<#2, ObjectGuid/WriteAsPacked | — | — |
