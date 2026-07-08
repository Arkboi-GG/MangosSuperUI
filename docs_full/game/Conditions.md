# Conditions

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Conditions

The `Conditions` unit implements the server-side conditional logic engine for World of Warcraft emulation. It provides a unified interface for evaluating complex, composable boolean expressions that determine whether specific game actions are allowed, visible, or triggered. These conditions are attached to various game entities—such as loot templates, gossip menus, vendor items, scripted events, and quest requirements—and are evaluated at runtime against the current state of players, creatures, game objects, and the world environment.

The core abstraction is the `ConditionEntry` class, which represents a single atomic or composite condition. Conditions can be combined using logical operators (`AND`, `OR`, `NOT`) to form trees of logic. The system supports over 60 distinct condition types, ranging from simple checks (e.g., "is the player level 60?") to complex spatial and state queries (e.g., "is there a line of sight between the source and target?" or "has the player explored this specific area?").

This unit does not store persistent data itself; it relies on external managers (`ObjectMgr`, `GameEventMgr`, `SpellMgr`) and entity states (`Player`, `Creature`, `GameObject`) to retrieve the necessary information for evaluation. It is designed to be lightweight and fast, as conditions are frequently evaluated during high-frequency operations like loot generation, gossip menu updates, and AI decision-making.

## Member-by-Member Behavior

### Core Evaluation Logic

**`Meets`**
This is the primary entry point for evaluating a single `ConditionEntry`. It accepts a `target` object, a `map` context, a `source` object, and a `conditionSourceType` indicating where the check originates (e.g., loot, gossip, script).
1.  **Logging**: It logs the evaluation attempt for debugging purposes.
2.  **Target Swapping**: If the `CONDITION_FLAG_SWAP_TARGETS` flag is set, it swaps the `source` and `target` pointers. This allows conditions to be written generically and applied in reverse contexts without duplicating logic.
3.  **Parameter Validation**: It calls `CheckParamRequirements` to ensure the provided objects match the expected types for the condition type (e.g., a `CONDITION_ITEM` requires a Player target). If validation fails, it logs an error and returns `false`.
4.  **Evaluation**: It delegates to `Evaluate` to perform the actual logic.
5.  **Result Reversal**: If the `CONDITION_FLAG_REVERSE_RESULT` flag is set, it negates the result returned by `Evaluate`. This allows a single condition definition to be used for both positive and negative checks.

**`Evaluate`**
This method contains the switch-case logic for all supported `ConditionType` values. It interprets `m_value1` through `m_value4` based on the specific condition type. Key behaviors include:
*   **Logical Operators**: `CONDITION_NOT`, `CONDITION_OR`, and `CONDITION_AND` recursively call `Meets` on other `ConditionEntry` IDs stored in the values. This enables nested condition trees.
*   **Entity State Checks**: Conditions like `CONDITION_AURA`, `CONDITION_ITEM`, `CONDITION_SKILL`, and `LEVEL` cast the `target` to the appropriate type (`Unit`, `Player`) and query their state.
*   **Spatial Queries**: Conditions like `CONDITION_NEARBY_CREATURE`, `CONDITION_DISTANCE_TO_TARGET`, and `CONDITION_LINE_OF_SIGHT` use `WorldObject` methods to calculate distances or check visibility.
*   **Global State**: Conditions like `CONDITION_ACTIVE_GAME_EVENT` and `CONDITION_SAVED_VARIABLE` query global managers (`sGameEventMgr`, `sObjectMgr`).
*   **Instance/Script Data**: Conditions like `CONDITION_INSTANCE_DATA` and `CONDITION_MAP_EVENT_DATA` retrieve dynamic data from `InstanceData` or `ScriptedEvent` objects associated with the map.
*   **Edge Case Handling**: Some conditions, like `CONDITION_ESCORT`, handle null pointers gracefully, returning `true` if the source or target is dead or missing, depending on flags.

**`CheckParamRequirements`**
This helper method validates that the `target`, `source`, and `map` arguments passed to `Meets` are suitable for the current condition type. It uses the `ConditionTargets` array (derived from `ConditionTargetsInternal`) to look up the required parameter type (e.g., `CONDITION_REQ_TARGET_PLAYER`). If the required object is null or of the wrong type (e.g., passing a `Creature` when a `Player` is required), it returns `false`. This prevents crashes from invalid casts in `Evaluate`.

**`IsValid`**
This method performs static validation of the condition's configuration data, typically called during loading from the database. It checks:
*   **Referential Integrity**: Ensures that IDs referenced in values (e.g., spell IDs, item IDs, quest IDs, faction IDs) exist in their respective databases.
*   **Range Checks**: Validates that numeric values (e.g., skill levels, reputation ranks, percentages) are within acceptable bounds.
*   **Dependency Existence**: For logical conditions (`AND`, `OR`, `NOT`), it verifies that the referenced child condition IDs exist and are lower than the current entry ID (to prevent circular dependencies or forward references that might not be loaded yet).
*   **Disabling Invalid Conditions**: If a referenced entity (like a spell or item) is marked as "existing" in the database but not currently loaded (perhaps due to a patch difference), it calls `DisableCondition` to mark the condition as `CONDITION_NONE` (always true) rather than failing entirely. This provides resilience against missing data.

**`CanBeUsedWithoutPlayer`**
A static utility method that determines if a condition (identified by `entry`) can be evaluated without a `Player` object as the target. It recursively checks logical conditions and consults the `ConditionTargets` array. This is used by systems like `LootMgr` to optimize checks or determine if a condition is relevant for non-player entities.

**`IsConditionSatisfied`**
A free function that serves as a convenient wrapper for evaluating a condition by ID. It looks up the `ConditionEntry` in `sConditionStorage` and calls `Meets`. If the condition ID is invalid, it returns `false`. This is the standard interface used by most other units to check conditions.

### Supporting Methods

**`ConditionEntry` (Constructors)**
Two constructors are provided: a default one for storage initialization and a parameterized one for creating entries from database records. They initialize the entry ID, condition type, four value fields, and flags.

**`GetTeam`**
Returns the team (Alliance/Horde) specified by the condition if it is a `CONDITION_TEAM`, otherwise returns `TEAM_CROSSFACTION`. This is used by `LootMgr` to quickly filter loot eligibility based on faction.

**`DisableCondition`**
A private method that marks the condition as `CONDITION_NONE` and toggles the reverse result flag. This effectively neutralizes the condition, making it always evaluate to `true` (or `false` if reversed), allowing the server to continue operating despite invalid configuration data.

## Cross-Unit Boundaries

The `Conditions` unit acts as a central hub, querying many other subsystems to gather the data needed for evaluation.

*   **`ObjectMgr`**: Heavily relied upon for static data lookups. `IsValid` calls `GetFactionEntry`, `GetQuestTemplate`, `GetItemPrototype`, `GetCreatureTemplate`, `GetGameObjectTemplate`, and various `IsExisting...` methods to verify that the IDs configured in conditions correspond to valid game entities. `Evaluate` uses `GetSavedVariable` and `GetGOData`.
*   **`Player`**: `Evaluate` casts the `target` to `Player` for numerous conditions, calling methods like `HasItemCount`, `HasSkill`, `GetReputationMgr`, `CanTakeQuest`, `IsCurrentQuest`, `GetGroup`, `GetHonorMgr`, and `GetSpellAuraHolderMap`. This allows conditions to check player-specific states like inventory, skills, reputation, quests, and group membership.
*   **`Unit` / `Creature` / `GameObject`**: `Evaluate` casts `target` or `source` to these types to check states like `HasAura`, `IsAlive`, `IsInCombat`, `GetHealthPercent`, `GetLevel`, `GetMotionMaster` (for waypoints), `isSpawned`, `getLootState`, and `GetGoState`. `Creature` specific methods like `GetCreatureGroup` are used for group-related conditions.
*   **`Map`**: Used to retrieve instance-specific data. `Evaluate` calls `GetInstanceData` to access `InstanceData` methods like `CheckConditionCriteriaMeet` and `GetData`. It also calls `GetScriptedMapEvent` to access `ScriptedEvent` data. `GetId` is used to check the current map ID.
*   **`GameEventMgr`**: `Evaluate` calls `IsActiveEvent` and `IsActiveHoliday` to check if global events or holidays are running. `IsValid` calls `IsValidEvent`.
*   **`SpellMgr`**: `IsValid` calls `GetSpellEntry` and `IsExistingSpellId` to verify spell IDs. `Evaluate` accesses `SpellAuraHolder` via `Player::GetSpellAuraHolderMap`.
*   **`World`**: `IsValid` calls `getConfig` and `GetConfigMaxSkillValue` to validate skill levels against server configuration. `Evaluate` calls `GetWowPatch` to check the current content patch.
*   **`Log`**: Both `Meets` and `IsValid` call `sLog.Out` to log debug information and errors.
*   **`LootMgr`**: `Meets` and `GetTeam` are called by `LootMgr::AllowedForTeam` and `LootMgr::AllowedForPlayer` to determine if a player can receive specific loot items. `CanBeUsedWithoutPlayer` is called by `LootMgr::LoadLootTable`.
*   **`ChatHandler`**: `IsConditionSatisfied` is called by `HandleDebugConditionCommand` to allow administrators to test conditions manually.
*   **`CreatureEventAI`**: `IsConditionSatisfied` is called by `ProcessEvent` to determine if AI events should trigger.
*   **`Map` (Scripting)**: `IsConditionSatisfied` is called by various map scripting functions (`ScriptCommandStartDirect`, `ScriptsProcess`, `StartAreaTriggerScript`, `UpdateEvent`) to control the flow of scripted events.
*   **`Player` (Interactions)**: `IsConditionSatisfied` is called by `BuyItemFromVendor`, `GetGossipTextId`, `PrepareGossipMenu`, `SatisfyQuestCondition`, `SendListInventory`, and `HandleAreaTriggerOpcode` to control vendor availability, gossip options, quest rewards, and area triggers.

## Data Model

The `Conditions` unit does not directly interact with database tables in its source code. However, it is designed to work with data loaded from a `conditions` table (not shown in the schema but implied by the `ConditionEntry` structure and `ObjectMgr::LoadConditions` caller). The `ConditionEntry` class mirrors the columns of this table:
*   `m_entry`: Unique identifier for the condition.
*   `m_condition`: Type of condition (maps to `ConditionType` enum).
*   `m_value1` - `m_value4`: Parameters for the condition, meaning varies by type.
*   `m_flags`: Bitmask for modifiers like `REVERSE_RESULT` and `SWAP_TARGETS`.

The unit relies on other tables indirectly via `ObjectMgr` and other managers:
*   `creature_template`, `gameobject_template`, `item_template`, `quest_template`, `spell_template`, `faction_template`: Referenced by IDs in condition values.
*   `game_event`, `holiday`: Referenced by IDs in event/holiday conditions.
*   `saved_variables`: Referenced by index in saved variable conditions.

## Notable Implementation Details

*   **Recursive Evaluation**: Logical conditions (`AND`, `OR`, `NOT`) recursively call `Meets` on other conditions. This allows for complex, nested logic trees. The `IsValid` method ensures these trees do not contain cycles by requiring child condition IDs to be lower than the parent's ID.
*   **Target Swapping**: The `CONDITION_FLAG_SWAP_TARGETS` flag allows a condition to be evaluated with the source and target roles reversed. This is useful for reusing conditions in different contexts (e.g., checking if a player is near a creature vs. if a creature is near a player).
*   **Resilience to Missing Data**: The `IsValid` method distinguishes between "non-existent" and "not loaded" entities. If an entity is marked as existing in the database but not currently loaded (e.g., due to a patch difference), the condition is disabled (`DisableCondition`) rather than causing a hard failure. This allows the server to start even with incomplete data.
*   **Performance Optimization**: `CanBeUsedWithoutPlayer` allows systems like `LootMgr` to skip evaluating conditions that require a player target when the context doesn't involve a player.
*   **Static Arrays for Requirements**: The `ConditionTargetsInternal` array maps each condition type to its parameter requirements. This allows `CheckParamRequirements` to quickly validate inputs without complex logic.
*   **Local Time Calculation**: `CONDITION_LOCAL_TIME` uses `localtime` to check the server's local time. This is useful for time-based events or spawns.
*   **Area Exploration Check**: `CONDITION_AREA_EXPLORED` manually calculates bit offsets to check the `PLAYER_EXPLORED_ZONES` fields in the player's data. This is a low-level check that bypasses higher-level APIs.
*   **Escort Logic**: `CONDITION_ESCORT` has specific flags (`CF_ESCORT_SOURCE_DEAD`, `CF_ESCORT_TARGET_DEAD`) to handle the unique requirements of escort quests, where the death of the escortee or the player might satisfy or fail the condition depending on the context.

## Member Reference

**`Meets`**: Evaluates the condition against the provided target, map, and source. Handles target swapping, parameter validation, and result reversal. Logs debug info.

**`Evaluate`**: Contains the switch-case logic for all condition types. Performs the actual checks by casting objects and querying their state or global managers. Recursively evaluates logical conditions.

**`ConditionEntry`**: Default constructor for storage initialization. Initializes all fields to zero/default values.

**`ConditionEntry#2`**: Parameterized constructor for creating entries from database records. Sets entry ID, condition type, values, and flags.

**`GetTeam`**: Returns the team specified by the condition if it is a `CONDITION_TEAM`, otherwise `TEAM_CROSSFACTION`. Used for quick faction filtering.

**`DisableCondition`**: Private method that marks the condition as `CONDITION_NONE` and toggles the reverse result flag, effectively neutralizing it.

**`CheckParamRequirements`**: Validates that the provided target, source, and map match the requirements for the current condition type. Prevents invalid casts in `Evaluate`.

**`IsValid`**: Performs static validation of the condition's configuration data. Checks referential integrity, range limits, and dependency existence. Disables invalid conditions gracefully.

**`CanBeUsedWithoutPlayer`**: Static method that determines if a condition can be evaluated without a Player target. Used for optimization in non-player contexts.

**`IsConditionSatisfied`**: Free function wrapper that looks up a condition by ID and calls `Meets`. Standard interface for condition evaluation.

---

<!-- machine-true, projected from graph.json -->

## Map — Conditions

*Source:* Conditions.cpp, Conditions.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Meets | method | Log.Main/Out, Map.Main/GetId, Object/GetGuidStr | LootMgr/AllowedForTeam | — |
| Evaluate | method | AreaEntry/GetFlagById, Creature.Main/GetCreatureGroup, Creature.Main/GetDBTableGUIDLow, Creature.Main/ToCreature#2, Creature.MotionMaster/getLastReachedWaypoint, CreatureGroups/GetLeaderGuid, CreatureGroups/GetMembers, CreatureGroups/GetOriginalLeaderGuid, GameEventMgr.Main/IsActiveEvent, GameEventMgr.Main/IsActiveHoliday, GameObject/GetDBTableGUIDLow, GameObject/GetGoState, GameObject/getLootState, GameObject/isSpawned, HonorMgr/GetRank, InstanceData/CheckConditionCriteriaMeet, InstanceData/GetData, Log.Main/Out, Map.Main/GetCreature, Map.Main/GetGameObject, Map.Main/GetId, Map.Main/GetInstanceData#2, Map.Main/GetScriptedMapEvent, Map.Main/GetScriptedMapEvent#2, Map.Main/GetWorldObject, Object/GetEntry, Object/GetObjectGuid, Object/GetTypeId, Object/GetUInt32Value, Object/GetValuesCount, Object/HasFlag, Object/IsInWorld, Object/IsPlayer, Object/IsUnit, Object/ToCreature#2, Object/ToGameObject#2, Object/ToPlayer#2, Object/ToUnit#2, ObjectGuid/GetCounter, ObjectGuid/ObjectGuid#3, ObjectGuid/operator!=, ObjectMgr/GetFactionEntry, ObjectMgr/GetGOData, ObjectMgr/GetQuestTemplate, ObjectMgr/GetSavedVariable, Player.Main/CanTakeQuest, Player.Main/GetGroup#2, Player.Main/GetHonorMgr#2, Player.Main/GetQuestRewardStatus, Player.Main/GetReputationMgr#2, Player.Main/GetSkillValueBase, Player.Main/GetTeam, Player.Main/HasItemCount, Player.Main/HasItemWithIdEquipped, Player.Main/HasSkill, Player.Main/HasSpell, Player.Main/IsCurrentQuest, Player.Main/ToPlayer#2, ReputationMgr/GetRank, ScriptedEvent/GetData, SpellAuraHolder/GetSpellProto, Unit.Main/CantPathToVictim, Unit.Main/GetClassMask, Unit.Main/GetHealthPercent, Unit.Main/GetLevel, Unit.Main/GetMotionMaster, Unit.Main/GetPet, Unit.Main/GetPowerPercent, Unit.Main/GetRaceMask, Unit.Main/GetSpellAuraHolderMap#2, Unit.Main/HasAura, Unit.Main/HasAura#2, Unit.Main/IsAlive, Unit.Main/IsCharmerOrOwnerPlayerOrPlayerItself, Unit.Main/IsDead, Unit.Main/IsInCombat, World/GetWowPatch, WorldObject.Object/FindNearestCreature, WorldObject.Object/FindNearestFriendlyPlayer, WorldObject.Object/FindNearestGameObject, WorldObject.Object/FindNearestHostilePlayer, WorldObject.Object/FindNearestPlayer, WorldObject.Object/GetDistance#3, WorldObject.Object/GetDistance3dToCenter#4, WorldObject.Object/GetGender, WorldObject.Object/GetMap, WorldObject.Object/GetMapId, WorldObject.Object/GetReactionTo, WorldObject.Object/GetZoneAndAreaId, WorldObject.Object/IsMoving, WorldObject.Object/IsWithinDistInMap, WorldObject.Object/IsWithinLOSInMap | — | — |
| ConditionEntry | ctor | — | — | — |
| ConditionEntry#2 | ctor | — | — | — |
| GetTeam | method | — | LootMgr/AllowedForTeam | — |
| DisableCondition | method | — | — | — |
| CheckParamRequirements | method | Object/IsCreature, Object/IsGameObject, Object/IsPlayer, Object/IsUnit | — | — |
| IsValid | method | AreaEntry/GetById, GameEventMgr.Main/IsValidEvent, GridDefines/IsValidMapCoord#3, Log.Main/Out, ObjectMgr/GetCreatureTemplate, ObjectMgr/GetFactionEntry, ObjectMgr/GetGameObjectTemplate, ObjectMgr/GetItemPrototype, ObjectMgr/GetQuestTemplate, ObjectMgr/IsExistingCreatureGuid, ObjectMgr/IsExistingCreatureId, ObjectMgr/IsExistingGameObjectGuid, ObjectMgr/IsExistingGameObjectId, ObjectMgr/IsExistingItemId, ObjectMgr/IsExistingQuestId, SpellMgr/GetSpellEntry, SpellMgr/Instance, SpellMgr/IsExistingSpellId, UpdateFields/GetIndexOfUpdateFieldForCurrentBuild, World/getConfig#4, World/GetConfigMaxSkillValue | ObjectMgr/LoadConditions | — |
| CanBeUsedWithoutPlayer | method | — | LootMgr/AllowedForTeam, LootMgr/LoadLootTable | — |
| IsConditionSatisfied | function | — | ChatHandler.DebugCommands/HandleDebugConditionCommand, CreatureEventAI/ProcessEvent, LootMgr/AllowedForPlayer, Map.Main/ScriptCommandStartDirect, Map.Main/ScriptsProcess, Map.Main/StartAreaTriggerScript, Map.Main/UpdateEvent, Map.ScriptCommands/ScriptCommand_RemoveMapEventTarget, Map.ScriptCommands/ScriptCommand_TerminateCondition, Player.Main/BuyItemFromVendor, Player.Main/GetGossipTextId#2, Player.Main/PrepareGossipMenu, Player.Main/SatisfyQuestCondition, WorldSession.ItemHandler/SendListInventory, WorldSession.MiscHandler/HandleAreaTriggerOpcode | — |
