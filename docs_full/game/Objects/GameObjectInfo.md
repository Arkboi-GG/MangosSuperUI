# GameObjectInfo

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# GameObjectInfo

**Purpose & Responsibilities**

`GameObjectInfo` is a static data structure representing the template definition for a non-living object in the game world (a "GameObject"). It resides in `GameObjectDefines.h` and serves as the canonical source of truth for the properties of a specific GameObject entry ID. Unlike the dynamic `GameObject` class instance, which tracks runtime state (position, health, current owner), `GameObjectInfo` contains immutable configuration data loaded from the database table `gameobject_template`.

Its primary responsibility is to provide type-safe accessors for fields that vary significantly depending on the GameObject's functional type (e.g., a Door behaves differently than a Chest or a Quest Giver). Because the underlying database stores these varied properties in a single set of generic columns (`data0` through `data23`), `GameObjectInfo` uses a C++ `union` to overlay strongly-typed structs onto this raw data. The methods in this unit act as a facade, switching on the `type` field to return the correct value from the appropriate sub-struct within the union.

This unit is heavily utilized during the initialization of `GameObject` instances (`GameObject/LoadFromDB`, `GameObject/Create`) and during runtime checks for interaction validity (`GameObject/Use`, `GameObject/Update`). It does not manage state; it only describes *what* the object is and *how* it is configured to behave.

## Member-by-Member Behavior

The members of `GameObjectInfo` are organized by the logical aspect of the GameObject they expose. Most are simple getters that inspect the `type` enum and return a value from the corresponding union member.

### Interaction and Usage Constraints

These methods determine whether a player can interact with the object and under what conditions.

*   **`IsUsableMounted`**: Returns `true` if the GameObject can be used while the player is mounted. This is hardcoded `true` for Mailboxes. For Quest Givers, Text objects, Goobers, and Spell Casters, it depends on the specific `allowMounted` flag in their respective union structs. Other types return `false`.
*   **`CannotBeUsedUnderImmunity`**: Determines if the object ignores immunity effects (like Divine Shield). If this returns `true`, a player under immunity *cannot* use the object. Notably, **all Chests** return `true` here regardless of their specific config, reflecting a hard-coded rule that chests cannot be looted while immune. Other types (Doors, Buttons, Quest Givers, Goobers, Flag Stands, Flag Drops) depend on their `noDamageImmune` field.
*   **`GetInteractionDistance`**: Returns the maximum distance (in yards) a player must be to interact with the object. Most types fall back to the global `INTERACTION_DISTANCE` constant. However, specific types have overrides:
    *   Quest Givers, Text, Flag Stands, Flag Drops, and Mini Games: 5.55556 yards.
    *   Binders and Chairs/Fishing Nodes: 100.0 yards (though comments note chairs are effectively 3 yards for sitting, the code returns 100.0 for the general interaction check).
    *   Area Damage: 0.0 yards.

### Locking and Security

*   **`GetLockId`**: Retrieves the `lockId` associated with the GameObject. This ID references the `Lock.dbc` file to determine what keys or spells are required to open the object. This applies to Doors, Buttons, Quest Givers, Chests, Traps, Goobers, Area Damage, Cameras, Flag Stands, Fishing Holes, and Flag Drops. If the type doesn't support locking, it returns 0.

### Lifecycle and State Management

These methods control how the object persists, despawns, or resets.

*   **`IsDespawnAtAction`**: Returns `true` if the object should despawn immediately after being used/looted. This is determined by the `consumable` flag for Chests and Goobers. All other types return `false`.
*   **`GetDespawnPossibility`**: A somewhat ambiguously named method (commented as "despawn at targeting of cast?") that actually checks the `noDamageImmune` flag for Doors, Buttons, Quest Givers, Goobers, Flag Stands, and Flag Drops. For all other types, it defaults to `true`. This is likely used to determine if the object can be targeted or affected by certain area-of-effect spells or mechanics that might cause despawning.
*   **`GetCharges`**: Returns the number of uses remaining before the object becomes inert or despawns. This is relevant for Traps, Guard Posts, and Spell Casters. Other types return 0.
*   **`GetCooldown`**: Returns the cooldown period in seconds before the object can be triggered or used again. Relevant for Traps and Goobers. Other types return 0.
*   **`GetAutoCloseTime`**: Calculates the time in seconds after which an opened object (like a door or button) automatically closes. The raw value stored in the database is divided by `0x10000` (65536) to get the final second count. Applies to Doors, Buttons, Traps, Goobers, Transports, and Area Damage.
*   **`IsInfiniteGameObject`**: Returns `true` for Doors, Flag Stands, and Flag Drops. This likely indicates that these objects do not have a finite "use" count that depletes them permanently; they toggle states instead.
*   **`IsLargeGameObject`**: Returns `true` if the object is considered "large" for rendering or selection purposes. This depends on the `large` flag in the union for Buttons, Quest Givers, Generics, Traps, Spell Focuses, Goobers, Spell Casters, and Capture Points.

### Linking and Events

*   **`GetLinkedGameObjectEntry`**: Returns the Entry ID of another GameObject that is linked to this one. This is commonly used for buttons that trigger traps, or chests that trigger traps upon opening. Applies to Buttons, Chests, Spell Focuses, and Goobers.
*   **`GetEventScriptId`**: Returns the script ID associated with the object's events. This is used to trigger scripted behaviors when the object is used or activated. Applies to Goobers, Chests, and Cameras.
*   **`GetLootId`**: Returns the loot template ID for the object. This determines what items drop when the object is looted. Applies to Chests and Fishing Holes.
*   **`GetGossipMenuId`**: Returns the gossip menu ID displayed when interacting with the object. Applies to Quest Givers and Goobers.

### Rendering and Visibility

*   **`IsServerOnly`**: Returns `true` if the object is invisible to clients and exists only for server-side logic. Applies to Generic objects, Traps, Spell Focuses, and Aura Generators (if the `serverOnly` flag is set).
*   **`CanAlwaysBreakLoS`**: Returns `true` if the object can always break Line of Sight, regardless of its state. This is hardcoded for Doors and Generic objects. This is used during model initialization (`GameObjectModel/initialize`).
*   **`IsTransport`**: Returns `true` if the object is a Transport (boat/elevator) or MO_Transport (moving object). This is a simple type check.

## Cross-Unit Boundaries

`GameObjectInfo` is a passive data provider. It does not initiate actions but is queried extensively by other units to make decisions.

*   **Called by `GameObject`**:
    *   `LoadFromDB`: Uses `IsDespawnAtAction` and `GetDespawnPossibility` to initialize the object's persistence settings.
    *   `Use`: Checks `IsUsableMounted`, `CannotBeUsedUnderImmunity`, `GetCharges`, `GetCooldown`, and `GetAutoCloseTime` to validate and process player interaction.
    *   `Update`: Queries `IsDespawnAtAction`, `GetCharges`, and `GetAutoCloseTime` to handle periodic state changes (e.g., auto-closing doors, checking charge depletion).
    *   `Create`: Uses `IsLargeGameObject` and `IsInfiniteGameObject` to set initial flags.
    *   `IsTransport`: Delegates directly to `GameObjectInfo::IsTransport`.
    *   `IsAtInteractDistance#2`: Uses `GetInteractionDistance` to calculate valid interaction range.
    *   `RemoveFromWorld`, `RespawnLinkedGameObject`, `SummonLinkedTrapIfAny`, `TriggerLinkedGameObject`: Use `GetLinkedGameObjectEntry` to find and manipulate associated objects.
    *   `UseDoorOrButton`: Uses `GetAutoCloseTime`.
    *   `CanAggroWhenOpening`, `GetSpellForLock`, `PlayerCanUse`: Use `GetLockId` to determine security requirements.
    *   `ActivateToQuest`: Uses `GetLootId` to potentially award quest items.
    *   `IsVisibleForInState`: Uses `IsServerOnly` to determine visibility rules.

*   **Called by `Spell` related units**:
    *   `Spell.Effects/EffectOpenLock`, `Spell.Main/CheckCast`, `spell_special/OnSuccessfulStart`: Use `GetLockId` and `CannotBeUsedUnderImmunity` to verify if a spell can successfully unlock or interact with the target GameObject.

*   **Called by `ObjectMgr`**:
    *   `LoadGameobjects`: Uses `IsDespawnAtAction` during bulk loading.
    *   `LoadGossipMenu`, `LoadGossipMenuItems`: Use `GetGossipMenuId` to pre-load gossip data.
    *   `LoadGameObjectForQuests`: Uses `GetLootId` to link objects to quests.

*   **Called by `LootMgr`**:
    *   `LoadLootTemplates_Gameobject`: Uses `GetLootId` to build loot tables.

*   **Called by `Player.Main`**:
    *   `SendLoot`: Uses `GetLootId` to send loot data to the client.

*   **Called by `AiBotAI.Bridge`**:
    *   `BridgeHandleUseGameObject`: Uses `GetLootId` to simulate bot looting behavior.

*   **Called by `GameObjectModel`**:
    *   `construct`: Uses `IsServerOnly` to decide whether to create a visual model.
    *   `initialize`: Uses `CanAlwaysBreakLoS` to configure collision/LOS properties.

## Data Model

`GameObjectInfo` maps directly to the `gameobject_template` database table. While the schema is not provided in the input, the code reveals the following mapping strategy:

*   **Primary Key**: `id` (uint32)
*   **Type Discriminator**: `type` (uint32) determines which subset of `data0`-`data23` columns are interpreted.
*   **Generic Fields**: `displayId`, `name`, `icon`, `faction`, `flags`, `size`, `MinMoneyLoot`, `MaxMoneyLoot`, `ScriptId` are stored directly in the struct.
*   **Union Data**: The columns `data0` through `data23` in the database are overlaid by the `union` in the struct. For example, if `type` is `GAMEOBJECT_TYPE_DOOR`, `data0` becomes `door.startOpen`, `data1` becomes `door.lockId`, etc.
*   **Localization**: The `name` field is supplemented by `GameObjectLocale`, which stores localized names in a separate vector, likely sourced from `gameobject_locale`.

## Notable Implementation Details

1.  **Union Overlay**: The core complexity of `GameObjectInfo` is the `union` containing 31 different structs (one for each `GameobjectTypes` enum value). This allows the database to use a flat schema (`data0`-`data23`) while providing type-safe access in C++. Maintainers must ensure that new fields added to any sub-struct do not exceed the 24-element limit of the `raw.data` array, or the union size will mismatch the database column count.
2.  **Hardcoded Immunity Rule for Chests**: In `CannotBeUsedUnderImmunity`, `GAMEOBJECT_TYPE_CHEST` unconditionally returns `true`. This means *no* chest can be looted while a player is immune, regardless of the `noDamageImmune` flag in the database. This is a significant gameplay constraint embedded in the code.
3.  **Auto-Close Time Calculation**: `GetAutoCloseTime` divides the raw database value by `0x10000`. This implies that database values for auto-close times are stored as fixed-point numbers or scaled integers. For example, a value of `65536` results in a 1-second auto-close time.
4.  **Client Build Conditionals**: Several members (`GetLockId`, `IsUsableMounted`, `IsLargeGameObject`, etc.) contain `#if SUPPORTED_CLIENT_BUILD > ...` blocks. This ensures compatibility with older WoW client versions that did not support newer GameObject types (like Flag Stands or Fishing Holes). Accessing these fields on unsupported clients would return default values or skip the logic.
5.  **Default Interaction Distance**: The fallback `INTERACTION_DISTANCE` constant is used for most types. However, `GetInteractionDistance` explicitly overrides this for specific types. The comment for `GAMEOBJECT_TYPE_CHAIR` notes "for sitting its 3 yards" but the code returns `100.0f`. This suggests that the 100.0f value is a broad interaction radius for *selecting* the chair, while the actual sitting mechanic might enforce a stricter 3-yard check elsewhere.
6.  **Server-Only Objects**: `IsServerOnly` checks specific flags for Generic, Trap, Spell Focus, and Aura Generator types. These objects are not rendered to the client (`GameObjectModel/construct` skips model creation if this returns true), but they still exist in the world for server-side logic (e.g., triggering spells or events).

## Member Reference

**IsDespawnAtAction**: Returns `true` if the GameObject is consumable (Chests/Goobers with `consumable` flag set), indicating it should despawn after use.

**IsUsableMounted**: Returns `true` if the object can be used while mounted. Hardcoded `true` for Mailboxes; otherwise depends on `allowMounted` flag for Quest Givers, Text, Goobers, and Spell Casters.

**GetLockId**: Returns the `lockId` from the appropriate union struct for types that support locking (Doors, Buttons, Chests, etc.). Returns 0 for types that don't.

**GetDespawnPossibility**: Returns the `noDamageImmune` flag for Doors, Buttons, Quest Givers, Goobers, Flag Stands, and Flag Drops. Defaults to `true` for other types. Likely used to determine if the object can be targeted by spells that cause despawning.

**CannotBeUsedUnderImmunity**: Returns `true` if the object cannot be used by immune players. Hardcoded `true` for all Chests. For other types, depends on the `noDamageImmune` flag.

**GetCharges**: Returns the `charges` field for Traps, Guard Posts, and Spell Casters. Returns 0 for other types.

**GetCooldown**: Returns the `cooldown` field for Traps and Goobers. Returns 0 for other types.

**GetLinkedGameObjectEntry**: Returns the `linkedTrapId` for Buttons, Chests, Spell Focuses, and Goobers. Used to identify associated objects (e.g., a trap triggered by a button).

**GetAutoCloseTime**: Returns the `autoCloseTime` field divided by `0x10000` for Doors, Buttons, Traps, Goobers, Transports, and Area Damage. Represents seconds until auto-close.

**GetLootId**: Returns the `lootId` for Chests and Fishing Holes. Used to determine loot tables.

**GetGossipMenuId**: Returns the `gossipID` for Quest Givers and Goobers.

**IsLargeGameObject**: Returns `true` if the `large` flag is set for Buttons, Quest Givers, Generics, Traps, Spell Focuses, Goobers, Spell Casters, and Capture Points.

**IsInfiniteGameObject**: Returns `true` for Doors, Flag Stands, and Flag Drops. Indicates the object does not deplete with use.

**IsServerOnly**: Returns `true` if the `serverOnly` flag is set for Generic, Trap, Spell Focus, and Aura Generator types. These objects are invisible to clients.

**IsTransport**: Returns `true` if the type is `TRANSPORT` or `MO_TRANSPORT`.

**CanAlwaysBreakLoS**: Returns `true` for Doors and Generic objects, indicating they always block Line of Sight.

**GetInteractionDistance**: Returns specific interaction distances for Quest Givers (5.55556), Binders/Chairs/Fishing Nodes (100.0), and Area Damage (0.0). Falls back to `INTERACTION_DISTANCE` for others.

**GetEventScriptId**: Returns the `eventId` for Goobers, Chests, and Cameras. Used to trigger scripted events.

---

<!-- machine-true, projected from graph.json -->

## Map — GameObjectInfo

*Source:* GameObjectDefines.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| IsDespawnAtAction | method | — | GameObject/LoadFromDB, GameObject/Update, ObjectMgr/LoadGameobjects | — |
| IsUsableMounted | method | — | GameObject/Use | — |
| GetLockId | method | — | GameObject/CanAggroWhenOpening, GameObject/GetSpellForLock, GameObject/PlayerCanUse, GameObject/Update, Spell.Effects/EffectOpenLock, Spell.Main/CheckCast, spell_special/OnSuccessfulStart | — |
| GetDespawnPossibility | method | — | GameObject/LoadFromDB | — |
| CannotBeUsedUnderImmunity | method | — | GameObject/Use, Spell.Effects/EffectOpenLock, Spell.Main/CheckCast | — |
| GetCharges | method | — | GameObject/Update, GameObject/Use | — |
| GetCooldown | method | — | GameObject/Use | — |
| GetLinkedGameObjectEntry | method | — | GameObject/RemoveFromWorld, GameObject/RespawnLinkedGameObject, GameObject/SummonLinkedTrapIfAny, GameObject/TriggerLinkedGameObject | — |
| GetAutoCloseTime | method | — | GameObject/Update, GameObject/Use, GameObject/UseDoorOrButton | — |
| GetLootId | method | — | AiBotAI.Bridge/BridgeHandleUseGameObject, GameObject/ActivateToQuest, LootMgr/LoadLootTemplates_Gameobject, ObjectMgr/LoadGameObjectForQuests, Player.Main/SendLoot | — |
| GetGossipMenuId | method | — | ObjectMgr/LoadGossipMenu, ObjectMgr/LoadGossipMenuItems | — |
| IsLargeGameObject | method | — | GameObject/Create | — |
| IsInfiniteGameObject | method | — | GameObject/Create | — |
| IsServerOnly | method | — | GameObject/IsVisibleForInState, GameObjectModel/construct | — |
| IsTransport | method | — | GameObject/IsTransport | — |
| CanAlwaysBreakLoS | method | — | GameObjectModel/initialize | — |
| GetInteractionDistance | method | — | GameObject/IsAtInteractDistance#2 | — |
| GetEventScriptId | method | — | — | — |
