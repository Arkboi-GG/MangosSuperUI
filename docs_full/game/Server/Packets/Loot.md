# Loot

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Loot Packet Structures and Core Loot Data Model

## Purpose & Responsibilities

This unit defines the **packet structures** for client-to-server communication regarding loot interactions and the core **`Loot` data structure** that represents the contents of a lootable object (corpse, chest, fishing hole, etc.) within the server memory.

It serves two distinct architectural roles:
1.  **Network Layer (`WorldPackets::Loot`):** Defines the binary layout and deserialization logic for five specific client opcodes related to looting: `CMSG_AUTOSTORE_LOOT_ITEM`, `CMSG_LOOT`, `CMSG_LOOT_RELEASE`, `CMSG_LOOT_ROLL`, and `CMSG_LOOT_MASTER_GIVE`. These classes translate raw network bytes into strongly-typed C++ objects containing GUIDs, slot indices, and roll types.
2.  **Domain Model (`Loot` struct):** Acts as the central container for loot state. It holds the list of generated items (`LootItemList`), gold amount, quest-specific item mappings, and metadata about who is currently looting (`m_playersLooting`) and who is allowed to loot (`m_allowedLooters`). It manages the lifecycle of loot via `clear()` and tracks validity through `LootValidatorRef`.

The unit does **not** contain the logic for generating loot from database templates (that resides in `LootTemplate` and `LootStore`, declared in `LootMgr.h` but implemented elsewhere), nor does it contain the server-side response packets (those are likely in `WorldPackets::Loot` counterparts or `ChatHandler`/`Player` handlers). It strictly handles incoming client requests and the in-memory representation of the loot itself.

## Member-by-Member Behavior

### Network Packet Deserialization (`WorldPackets::Loot`)

These methods parse incoming `WorldPacket` data into the respective packet structs. They rely on `ByteBuffer` operators to extract primitive types and `ObjectGuid` extraction operators.

*   **`AutoStoreLootItem::ReadFromWorldPacket`**: Extracts a single `uint8` representing the `lootSlot` the client wishes to automatically store in their inventory.
*   **`LootUnit::ReadFromWorldPacket`**: Extracts the `ObjectGuid` of the unit/corpse the client is attempting to loot.
*   **`LootRelease::ReadFromWorldPacket`**: Extracts the `ObjectGuid` of the loot source the client is releasing. Note: The comment in `Loot.h` indicates the server ignores this GUID in favor of an internally stored one, suggesting this field is primarily for protocol compliance.
*   **`LootRoll::ReadFromWorldPacket`**: Extracts three fields:
    *   `lootedTarget`: The `ObjectGuid` of the loot source.
    *   `itemSlot`: The `uint32` index of the item being rolled on.
    *   `rollType`: The `uint8` type of roll (e.g., Need, Greed, Pass).
*   **`LootMasterGive::ReadFromWorldPacket`**: Extracts three fields for Master Looter distribution:
    *   `lootGuid`: The `ObjectGuid` of the loot source.
    *   `slotId`: The `uint8` index of the item.
    *   `playerGuid`: The `ObjectGuid` of the player receiving the item.

### Loot State Management (`Loot` Struct)

The `Loot` struct manages the state of a specific loot event.

#### Construction and Destruction
*   **`Loot::Loot` (Constructor)**: Initializes the loot state. It sets `m_personal` to `false`, `gold` to the provided `_gold` (default 0), `unlootedCount` to 0, and `loot_type` to `LOOT_CORPSE`. It stores the `lootTarget` pointer and initializes team flags to `TEAM_CROSSFACTION`. This constructor is called by various entities (`Creature`, `GameObject`, `Mail`, `Item`) when they generate loot.
*   **`~Loot` (Destructor)**: Calls `clear()` to ensure all dynamically allocated quest item lists and references are properly cleaned up.

#### State Querying
*   **`empty`**: Returns `true` if there are no items, no quest items, and no gold. Used by `Player.Main/SendLoot` and `Unit.Main/Kill` to determine if a loot window should even be opened.
*   **`isLooted`**: Returns `true` if `gold` is 0 AND `unlootedCount` is 0. This indicates the loot is fully consumed. Used by `Player.Main/IsAllowedToLoot` and `Spell.Main/CheckCast` to prevent interaction with empty corpses.
*   **`HasFFAQuestItems`**: Returns the boolean flag `m_hasFFAQuestItems`. Used by `WorldSession.LootHandler/DoLootRelease` to handle cleanup of Free-For-All quest items.
*   **`GetTeam`**: Returns the `m_groupTeam` value. Used by `LootMgr/AllowedForTeam` and `LootMgr/Roll#2` to enforce team-based loot restrictions (e.g., in battlegrounds).
*   **`HasPlayersLooting`**: Returns `true` if the `m_playersLooting` set is not empty. Used by `Spell.Main/CheckCast` to prevent spells that require targeting a corpse if someone is already looting it.
*   **`IsOriginalLooter`**: Delegates to `IsAllowedLooter(guid, false)`. Used by `Player.Main/SendLoot` to determine UI permissions.

#### State Mutation and Tracking
*   **`clear`**: Resets the entire loot state. It deletes dynamically allocated `QuestItemList` pointers in `m_playerQuestItems`, `m_playerFFAItems`, and `m_playerNonQuestNonFFAConditionalItems`. It clears all item lists, resets gold/counters, and clears the `m_LootValidatorRefManager`. If `clearQuestItems` is false, it preserves quest item maps (used by `leaveOnlyQuestItems`). Called extensively by `Creature.Main`, `GameObject`, and `WorldSession.LootHandler` when loot is released or despawned.
*   **`leaveOnlyQuestItems`**: Calls `clear(false)`. Used by `Player.Main/SendLoot` to strip non-quest items, likely for UI filtering or specific loot rules.
*   **`SetTeam`**: Sets `m_groupTeam`. Called by `Creature.Main/GenerateLootForBody` and `Player.Main/SendLoot` to establish team context for loot eligibility.
*   **`AddLooter`**: Inserts a player's `ObjectGuid` into `m_playersLooting`. Called by `Player.Main/SendLoot` when a player begins looting.
*   **`RemoveLooter`**: Erases a player's `ObjectGuid` from `m_playersLooting`. Called by `WorldSession.LootHandler/DoLootRelease` when a player stops looting.
*   **`addLootValidatorRef`**: Inserts a `LootValidatorRef` into `m_LootValidatorRefManager`. This allows external objects (like `game_Group_Group`) to register for notification when the loot becomes invalid (e.g., cleared). Called by `game_Group_Group/targetObjectBuildLink`.

#### Item Accessors
*   **`GetPlayerQuestItems`**: Returns the `m_playerQuestItems` map. Called by `LootMgr/hasItemFor` and `WorldSession.LootHandler/HandleAutostoreLootItemOpcode` to check if a player has specific quest items available.
*   **`GetPlayerFFAItems`**: Returns the `m_playerFFAItems` map. Called by `LootMgr/hasItemFor` to check Free-For-All quest items.
*   **`GetPlayerNonQuestNonFFAConditionalItems`**: Returns the `m_playerNonQuestNonFFAConditionalItems` map. Called by `LootMgr/hasItemFor` to check conditional items that are neither quest nor FFA.

## Cross-Unit Boundaries

### Incoming Calls (Dependencies)
The packet reading methods depend on:
*   **`ByteBuffer/operator>>#6`**: Used by `AutoStoreLootItem`, `LootRoll`, and `LootMasterGive` to extract `uint8` and `uint32` primitives.
*   **`ByteBuffer/operator>>#9`**: Used by `LootRoll` (likely for `uint32` or similar, depending on overload resolution).
*   **`ObjectGuid/operator>>`**: Used by `LootUnit`, `LootRelease`, `LootRoll`, and `LootMasterGive` to deserialize GUIDs.

### Outgoing Calls (Collaborators)
The `Loot` struct is heavily integrated with the game world and session management:

1.  **`Player.Main`**:
    *   `SendLoot`: Calls `empty`, `leaveOnlyQuestItems`, `AddLooter`, `IsOriginalLooter`, `SetTeam`, and `GetPlayerQuestItems`. This is the primary interface for presenting loot to the client.
    *   `IsAllowedToLoot`: Calls `isLooted` to verify loot availability.
    *   `RemoveCorpse` / `GenerateLootForBody`: Call `clear` and `SetTeam` during corpse lifecycle management.

2.  **`WorldSession.LootHandler`**:
    *   `DoLootRelease`: Calls `clear`, `RemoveLooter`, `isLooted`, and `HasFFAQuestItems`. Handles the client request to release loot.
    *   `HandleAutostoreLootItemOpcode`: Calls `GetPlayerQuestItems` to process auto-store requests.

3.  **`Creature.Main`**:
    *   `GenerateLootForBody` / `GeneratePlayerDependentLoot`: Call `SetTeam` and construct `Loot` objects.
    *   `RemoveCorpse`: Calls `clear`.

4.  **`GameObject`**:
    *   `Update` / `getFishLoot`: Call `clear` to manage loot state for chests and fishing holes.

5.  **`Spell.Main`**:
    *   `CheckCast`: Calls `isLooted` and `HasPlayersLooting` to validate spell targets (e.g., preventing resurrection or loot-related spells on occupied/empty corpses).

6.  **`game_Group_Group`**:
    *   `targetObjectBuildLink`: Calls `addLootValidatorRef` to link group loot rolls to the loot object's lifecycle.

7.  **`LootMgr`**:
    *   `hasItemFor` / `operator<<#2`: Call the `GetPlayer...Items` methods to serialize loot data for clients or check eligibility.
    *   `AllowedForTeam` / `Roll#2`: Call `GetTeam` to enforce group/team rules.

8.  **`ChatHandler.DebugCommands`**:
    *   `HandleDebugLootTableCommand`: Calls `SetTeam` and constructs `Loot` for debugging purposes.

## Data Model

This unit does not directly interact with database tables. It operates entirely on in-memory structures. The `LootStore` and `LootTemplate` classes (declared in `LootMgr.h`) are responsible for loading data from tables like `creature_loot_template`, `gameobject_loot_template`, etc., but the `Loot` struct itself only holds the resulting runtime state (`LootItem` vectors, gold counts, and player GUIDs).

## Notable Implementation Details

1.  **Memory Management of Quest Items**: The `Loot` struct uses `std::map<uint32, QuestItemList*>` for quest items. The `clear()` method manually iterates these maps and `delete`s the `QuestItemList` pointers before clearing the map. This indicates that `QuestItemList` objects are heap-allocated, likely because they are created dynamically per-player or per-loot-event and need to persist independently of the `Loot` struct's lifetime in some contexts, or simply due to legacy design choices. Failure to call `clear()` (or the destructor) would result in memory leaks.

2.  **Loot Validator References**: The `LootValidatorRefManager` allows external objects (specifically `game_Group_Group`) to register a reference to the `Loot` object. When `clear()` is called, `m_LootValidatorRefManager.clearReferences()` is invoked. This mechanism ensures that if a loot object is destroyed or cleared (e.g., corpse despawns), any pending group rolls associated with it are notified and can clean themselves up, preventing dangling pointers or invalid roll states.

3.  **Ignored GUID in LootRelease**: The `LootRelease` packet reads a `guid` from the client, but the comment in `Loot.h` explicitly states: `// not used by server (uses internally stored guid instead)`. This suggests the server tracks the active loot session internally (likely via the player's current target or a session variable) and ignores the client-provided GUID for security or consistency reasons.

4.  **Team Context**: The `m_groupTeam` field is initialized to `TEAM_CROSSFACTION` but is often set via `SetTeam` during loot generation. This is critical for battlegrounds or arenas where loot rules might differ based on team affiliation. The `GetTeam` method exposes this for `LootMgr` to enforce restrictions.

5.  **FFA and Conditional Items**: The separation of `m_playerQuestItems`, `m_playerFFAItems`, and `m_playerNonQuestNonFFAConditionalItems` reflects complex loot rules. FFA (Free-For-All) items are typically quest items that can be looted by anyone, while conditional items might require specific quests or conditions. The `GetPlayer...Items` methods provide access to these segregated lists for serialization and validation.

## Member Reference

**ReadFromWorldPacket** (in `AutoStoreLootItem`): Reads `lootSlot` (`uint8`) from the packet using `ByteBuffer/operator>>#6`.

**ReadFromWorldPacket#5** (in `LootUnit`): Reads `guid` (`ObjectGuid`) from the packet using `ObjectGuid/operator>>`.

**ReadFromWorldPacket#3** (in `LootRelease`): Reads `guid` (`ObjectGuid`) from the packet using `ObjectGuid/operator>>`.

**AutoStoreLootItem** (ctor): Initializes `lootSlot` to 0 and sets the opcode to `CMSG_AUTOSTORE_LOOT_ITEM`.

**ReadFromWorldPacket#4** (in `LootRoll`): Reads `lootedTarget` (`ObjectGuid`), `itemSlot` (`uint32`), and `rollType` (`uint8`) using `ObjectGuid/operator>>` and `ByteBuffer/operator>>#6`/`#9`.

**ReadFromWorldPacket#2** (in `LootMasterGive`): Reads `lootGuid` (`ObjectGuid`), `slotId` (`uint8`), and `playerGuid` (`ObjectGuid`) using `ObjectGuid/operator>>` and `ByteBuffer/operator>>#6`.

**GetPlayerQuestItems**: Returns the `m_playerQuestItems` map. Called by `LootMgr/hasItemFor`, `LootMgr/operator<<#2`, and `WorldSession.LootHandler/HandleAutostoreLootItemOpcode`.

**GetPlayerFFAItems**: Returns the `m_playerFFAItems` map. Called by `LootMgr/hasItemFor` and `LootMgr/operator<<#2`.

**GetPlayerNonQuestNonFFAConditionalItems**: Returns the `m_playerNonQuestNonFFAConditionalItems` map. Called by `LootMgr/hasItemFor` and `LootMgr/operator<<#2`.

**Loot** (ctor): Initializes loot state with `lootTarget`, `gold`, and default flags. Called by `ChatHandler.DebugCommands/HandleDebugLootTableCommand`, `Corpse/Corpse`, `Creature.Main/Creature`, `GameObject/GameObject`, `game_Mail_Mail/prepareItems`, `game_Mail_Mail/prepareTemplateItems`, and `game_Objects_Item/Item`.

**~Loot** (dtor): Calls `clear()` to clean up resources.

**addLootValidatorRef**: Registers a `LootValidatorRef` in `m_LootValidatorRefManager`. Called by `game_Group_Group/targetObjectBuildLink`.

**clear**: Resets all loot data, deleting dynamic quest item lists and clearing references. Called by `AiBotAI.Bridge/BridgeHandleUseGameObject`, `AiBotAI.Loot/DoAutoLoot`, `arathi_highlands/SummonedCreatureJustDied`, `Creature.Main/GenerateLootForBody`, `Creature.Main/RemoveCorpse`, `GameObject/getFishLoot`, `GameObject/Update`, `Player.Main/SendLoot`, `thousand_needles/SummonedCreatureJustDied`, and `WorldSession.LootHandler/DoLootRelease`.

**leaveOnlyQuestItems**: Calls `clear(false)` to preserve quest items. Called by `Player.Main/SendLoot`.

**empty**: Returns `true` if no items, quest items, or gold exist. Called by `instance_wailing_caverns/SetData`, `Player.Main/SendLoot`, and `Unit.Main/Kill`.

**isLooted**: Returns `true` if gold and unlooted count are zero. Called by `Player.Main/IsAllowedToLoot`, `Spell.Main/CheckCast`, `WorldObject.Object/BuildValuesUpdate`, and `WorldSession.LootHandler/DoLootRelease`.

**HasFFAQuestItems**: Returns `m_hasFFAQuestItems`. Called by `WorldSession.LootHandler/DoLootRelease`.

**AddLooter**: Adds a player GUID to `m_playersLooting`. Called by `Player.Main/SendLoot`.

**RemoveLooter**: Removes a player GUID from `m_playersLooting`. Called by `WorldSession.LootHandler/DoLootRelease`.

**HasPlayersLooting**: Returns `true` if `m_playersLooting` is not empty. Called by `Spell.Main/CheckCast`.

**IsOriginalLooter**: Delegates to `IsAllowedLooter(guid, false)`. Called by `Player.Main/SendLoot`.

**GetTeam**: Returns `m_groupTeam`. Called by `LootMgr/AllowedForTeam` and `LootMgr/Roll#2`.

**SetTeam**: Sets `m_groupTeam`. Called by `ChatHandler.DebugCommands/HandleDebugLootTableCommand`, `Creature.Main/GenerateLootForBody`, `Creature.Main/GeneratePlayerDependentLoot`, and `Player.Main/SendLoot`.

---

<!-- machine-true, projected from graph.json -->

## Map — Loot

*Source:* Loot.cpp, Loot.h, LootMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ReadFromWorldPacket | method | ByteBuffer/operator>>#6 | — | — |
| ReadFromWorldPacket#5 | method | ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#3 | method | ObjectGuid/operator>> | — | — |
| AutoStoreLootItem | ctor | — | — | — |
| ReadFromWorldPacket#4 | method | ByteBuffer/operator>>#6, ByteBuffer/operator>>#9, ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#2 | method | ByteBuffer/operator>>#6, ObjectGuid/operator>> | — | — |
| GetPlayerQuestItems | method | — | LootMgr/hasItemFor, LootMgr/operator<<#2, WorldSession.LootHandler/HandleAutostoreLootItemOpcode | — |
| GetPlayerFFAItems | method | — | LootMgr/hasItemFor, LootMgr/operator<<#2 | — |
| GetPlayerNonQuestNonFFAConditionalItems | method | — | LootMgr/hasItemFor, LootMgr/operator<<#2 | — |
| Loot | ctor | — | ChatHandler.DebugCommands/HandleDebugLootTableCommand, Corpse/Corpse, Creature.Main/Creature, GameObject/GameObject, game_Mail_Mail/prepareItems, game_Mail_Mail/prepareTemplateItems, game_Objects_Item/Item | — |
| ~Loot | dtor | — | — | — |
| addLootValidatorRef | method | — | game_Group_Group/targetObjectBuildLink | — |
| clear | method | — | AiBotAI.Bridge/BridgeHandleUseGameObject, AiBotAI.Loot/DoAutoLoot, arathi_highlands/SummonedCreatureJustDied, Creature.Main/GenerateLootForBody, Creature.Main/RemoveCorpse, GameObject/getFishLoot, GameObject/Update, Player.Main/SendLoot, thousand_needles/SummonedCreatureJustDied, WorldSession.LootHandler/DoLootRelease | — |
| leaveOnlyQuestItems | method | — | Player.Main/SendLoot | — |
| empty | method | — | instance_wailing_caverns/SetData, Player.Main/SendLoot, Unit.Main/Kill | — |
| isLooted | method | — | Player.Main/IsAllowedToLoot, Spell.Main/CheckCast, WorldObject.Object/BuildValuesUpdate, WorldSession.LootHandler/DoLootRelease | — |
| HasFFAQuestItems | method | — | WorldSession.LootHandler/DoLootRelease | — |
| AddLooter | method | — | Player.Main/SendLoot | — |
| RemoveLooter | method | — | WorldSession.LootHandler/DoLootRelease | — |
| HasPlayersLooting | method | — | Spell.Main/CheckCast | — |
| IsOriginalLooter | method | — | Player.Main/SendLoot | — |
| GetTeam | method | — | LootMgr/AllowedForTeam, LootMgr/Roll#2 | — |
| SetTeam | method | — | ChatHandler.DebugCommands/HandleDebugLootTableCommand, Creature.Main/GenerateLootForBody, Creature.Main/GeneratePlayerDependentLoot, Player.Main/SendLoot | — |
