<!-- provenance: boundary-bleed -->
# ChatHandler.DebugCommands

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ChatHandler.DebugCommands

## Purpose & Responsibilities

`ChatHandler.DebugCommands` is a partial implementation of the `ChatHandler` class in the WowVMaNGOS server emulator. It provides a comprehensive suite of administrative and debugging console/chat commands, typically prefixed with `.debug` or specific subsystem prefixes like `.mmap`.

Its primary responsibilities are:
1.  **Network Packet Injection:** Allowing administrators to manually construct and send raw or semi-raw network packets (`WorldPacket`) to clients to test UI responses, error messages, spell visuals, and chat behaviors without triggering the underlying game logic.
2.  **Data Inspection & Modification:** Providing tools to inspect and modify internal object fields (`UpdateFields`), spell coefficients, loot tables, and item states directly in memory.
3.  **Pathfinding & Navigation Debugging:** Offering detailed diagnostics for the MMAP (Movement Map) system, including tile loading, path calculation visualization, and off-mesh connection recording.
4.  **Collision & Line-of-Sight (LOS) Testing:** Using VMAP (Visual Map) data to debug static and dynamic object collisions and LOS breaks.
5.  **Simulation & Stress Testing:** Simulating loot drops, enchantment rolls, and movement splines to verify probabilities and AI behaviors.

This unit does not handle core gameplay logic but serves as a diagnostic interface for server developers and administrators.

## Member-by-Member Behavior

### Spell Diagnostics & Modification

*   **`HandleSpellIconFixCommand`**: Forces a specific spell to use Icon ID 1 by inserting/updating a row in the `spell_mod` database table via `Database/PExecute`. It then reloads spell modifications via `SpellModMgr/LoadSpellMods`. This is a quick fix for missing icons.
*   **`HandleSpellEffectsCommand`**: Displays detailed information about a spell's effects (indices 0-2). It iterates through `MAX_EFFECT_INDEX`, printing base points, mechanics, aura names, and trigger spells. For enchantment effects, it looks up `SpellItemEnchantmentEntry` to display aura IDs and slots. It uses `ChatHandler.LookupCommands/ShowSpellListHelper` to print the spell header.
*   **`HandleSpellInfosCommand`**: Prints high-level spell metadata including school, category, dispel type, attributes, interrupt flags, and specific flags like `IsBinary` and `IsPvEHeartBeat`. It relies on `SpellEntry` helper methods for these checks.
*   **`HandleSpellSearchCommand`**: Iterates through all known spell IDs (up to `SpellMgr/GetMaxSpellId`) to find spells matching a specific `SpellFamilyName` and `SpellFamilyFlags` bitmask. It prints matches using `ShowSpellListHelper`.
*   **`HandleDebugSpellCoefsCommand`**: Calculates and displays the default damage/healing coefficients for a spell. It distinguishes between direct damage/heal and periodic (DOT/HoT) effects by inspecting `SpellEntry` effect types. It outputs calculated coefficients and compares them with `EffectBonusCoefficient`.
*   **`HandleDebugSpellModsCommand`**: Sends a `SMSG_SET_FLAT_SPELL_MODIFIER` or `SMSG_SET_PCT_SPELL_MODIFIER` packet to a selected player. This modifies how the client calculates spell damage/healing for testing purposes. It enforces security checks via `ChatHandler.Chat/HasLowerSecurity`.
*   **`HandleDebugSpellCheckCommand`**: Triggers `SpellMgr/CheckUsedSpells` to validate spell properties against a database table (`spell_check`), logging results via `Log.Main/Out`.

### Network Packet Injection (UI & Feedback)

These commands construct `WorldPacket` objects manually and send them to the client to simulate various UI states or errors.

*   **`HandleDebugSendSpellFailCommand`**: Sends `SMSG_CAST_RESULT` with a failure code. Allows testing of spell cast failure animations/messages.
*   **`HandleDebugSendNextChannelSpellVisualCommand`**: Finds the next channeled spell ID greater than the argument and sends `MSG_CHANNEL_START` to the player, setting `UNIT_CHANNEL_SPELL` to visualize the channeling bar.
*   **`HandleSendSpellChannelVisualCommand`**: Similar to above, but uses the exact spell ID provided. Sends `MSG_CHANNEL_START` and updates the player's channel spell field.
*   **`HandleDebugSendPoiCommand`**: Sends a Point of Interest (POI) marker to the player at the location of the selected unit. Uses `GossipDef/SendPointOfInterest`.
*   **`HandleDebugSendEquipErrorCommand`**: Sends `SMSG_EQUIP_ERROR` with a specified error code to test equipment failure UI.
*   **`HandleDebugSendMailErrorCommand`**: Sends `SMSG_MAIL_RESULT` with specified ID, action, and error codes via `WorldSession.MailHandler/SendMailResult`.
*   **`HandleDebugSendSellErrorCommand`**: Sends `SMSG_SELL_ERROR` to test vendor sell failures.
*   **`HandleDebugSendBuyErrorCommand`**: Sends `SMSG_BUY_ERROR` to test vendor buy failures.
*   **`HandleDebugSendOpenBagCommand`**: Forces the client to open a bag window for the selected player by sending `SMSG_OPEN_CONTAINER`.
*   **`HandleDebugSendOpcodeCommand`**: Reads a custom packet definition from a local file `opcode.txt`. It parses types (`uint8`, `uint32`, `pguid`, etc.) and constructs a `WorldPacket` with the specified opcode and data, sending it to the client. This is a generic packet injector.
*   **`HandleDebugSendWorldStateCommand`**: Sends `SMSG_UPDATE_WORLD_STATE` to update world state fields (e.g., boss health bars, event timers) on the client.
*   **`HandleDebugPlayCinematicCommand`**: Sends `SMSG_CINEMATIC_START` to play a cinematic sequence identified by ID.
*   **`HandleDebugPlaySoundCommand`**: Plays a sound on a selected unit. Depending on whether a target is selected, it uses `PlayDistanceSound` or `PlayDirectSound`.
*   **`HandleDebugPlayMusicCommand`**: Sends `SMSG_PLAY_MUSIC` to a target player, playing background music.
*   **`HandleDebugPlayScriptText`**: Triggers a scripted text event via `ScriptMgr/DoScriptText` from a source unit to a target.
*   **`HandleDebugConditionCommand`**: Checks if a specific condition ID is satisfied for a target unit against a source unit using `Conditions/IsConditionSatisfied`.
*   **`HandleDebugSendChannelNotifyCommand`**: Sends `SMSG_CHANNEL_NOTIFY` to simulate channel join/leave/error notifications.
*   **`HandleDebugSendChatMsgCommand`**: Constructs a chat message packet using `ChatHandler.Chat/BuildChatPacket` and sends it directly, bypassing normal chat processing.
*   **`HandleDebugSendQuestPartyMsgCommand`**: Sends `SMSG_PUSH_TO_PARTY_RESPONSE` to simulate quest party push notifications.
*   **`HandleDebugSendQuestInvalidMsgCommand`**: Sends `SMSG_CAN_TAKE_QUEST_RESPONSE` to simulate quest acceptance failures.

### Object Field Inspection & Modification

These commands allow reading and writing to the `UpdateFields` of `Object`s, `Unit`s, and `Item`s. They rely heavily on helper methods defined in this unit (`HandleSetValueHelper`, `HandleGetValueHelper`, `HandlerDebugModValueHelper`).

*   **`HandleDebugGetLootRecipientCommand`**: Inspects a creature's loot recipient status, printing the GUID of the player/group receiving loot.
*   **`HandleDebugGetItemStateCommand`**: A complex diagnostic tool. It lists items in a player's inventory filtered by state (`ITEM_UNCHANGED`, `ITEM_CHANGED`, etc.), lists items in the update queue, or performs an integrity check ("all") verifying that item slots, owners, containers, and queue positions are consistent.
*   **`HandleDebugSetItemValueCommand`**: Sets a specific field index on an item held by the player. Uses `HandleSetValueHelper`.
*   **`HandleDebugSetValueByIndexCommand`**: Sets a field index on a selected unit. Uses `HandleSetValueHelper`.
*   **`HandleDebugSetValueByNameCommand`**: Sets a field by its symbolic name (e.g., "UNIT_FIELD_HEALTH"). It resolves the name to an offset via `UpdateFields/GetUpdateFieldDataByName` and handles different types (INT, FLOAT, BYTES).
*   **`HandleDebugGetItemValueCommand`**: Gets a field value from an item. Uses `HandleGetValueHelper`.
*   **`HandleDebugGetValueByIndexCommand`**: Gets a field value from a selected unit by index. Uses `HandleGetValueHelper`.
*   **`HandleDebugGetValueByNameCommand`**: Gets a field value from a selected unit by name. Uses `ShowUpdateFieldHelper`.
*   **`HandleDebugModItemValueCommand`**: Modifies an item field by adding/subtracting or bitwise operations. Uses `HandlerDebugModValueHelper`.
*   **`HandleDebugModValueCommand`**: Modifies a unit field by adding/subtracting or bitwise operations. Uses `HandlerDebugModValueHelper`.
*   **`HandleDebugUnitBytes1Command`** & **`HandleDebugUnitBytes2Command`**: Specifically set byte values in `UNIT_FIELD_BYTES_1` and `UNIT_FIELD_BYTES_2`, which control flags like sheathed state, power type, and stand state.
*   **`HandleDebugForceUpdateCommand`**: Forces the server to send an update for a specific field index to the client, useful for debugging sync issues.

#### Helper Methods for Fields
*   **`HandleSetValueHelper`**: Parses type strings (`int`, `hex`, `bit`, `float`) and sets the corresponding value on an `Object`. Validates field bounds.
*   **`HandleGetValueHelper`**: Retrieves a value from an `Object` and formats it based on the requested type (binary string for bits, hex for hex, etc.).
*   **`HandlerDebugModValueHelper`**: Performs arithmetic or bitwise modifications on existing field values. Supports `+=`, `|=`, `&=`, `&=~` for integers and `+=` for floats.
*   **`ShowAllUpdateFieldsHelper`**: Iterates through all non-zero fields of an object and prints them. Called by `ChatHandler.ObjectCommands` and `ChatHandler.UnitCommands`.
*   **`ShowUpdateFieldHelper`**: Formats and prints a single field's value based on its type definition from `UpdateFields`.

### Movement, Pathfinding & MMAP

*   **`HandleDebugMoveCommand`**: Changes the motion master state of a creature (Idle, Random, Confused, Fleeing, Feared).
*   **`HandleDebugMoveToCommand`**: Moves the player to the location of a selected unit using `MovePoint`.
*   **`HandleDebugMoveDistanceCommand`**: Moves a target unit a specified distance away from the player.
*   **`HandleDebugFaceMeCommand`**: Rotates a target unit to face the player.
*   **`HandleDebugMoveFlagsCommand`**: Gets or sets the raw movement flags of a unit.
*   **`HandleDebugMoveSplineCommand`**: Prints the current spline path details (origin, points, finalized status) for a unit.
*   **`HandleVideoTurn`**: Creates a spiral path around a selected unit and moves the player along it using `MoveSplineInit`. Useful for camera testing.
*   **`HandleDebugExp`**: An experimental command that finds nearby creatures and makes them perform a semi-circular movement pattern.
*   **`HandleMmap`**: Enables/disables MMAP globally via `World/setConfig`.
*   **`HandleMmapConnection`**: Records two points (start and end) to create an off-mesh connection. Writes the data to `offmesh_connections.txt` for later processing by the map generator.
*   **`HandleMmapTestArea`**: Tests pathfinding performance by calculating paths from the player to all creatures within a radius. Reports success/failure and timing.
*   **`HandleMmapPathCommand`**: Calculates a path from a target unit to the player. Visualizes the path by summoning temporary waypoint creatures at each path point. Supports straight vs. smooth paths.
*   **`HandleMmapLocCommand`**: Displays detailed navigation mesh information for the selected unit's location, including tile coordinates, polygon references, and closest walkable points.
*   **`HandleMmapLoadedTilesCommand`**: Lists all loaded MMAP tiles for the current map.
*   **`HandleMmapStatsCommand`**: Prints statistics about the loaded navigation meshes (tile count, polygon count, memory usage).
*   **`HandleMmapUnload`** & **`HandleMmapLoad`**: Manually unload or load MMAP data for the current map/tile.

### Collision & Line of Sight (VMAP)

*   **`HandleDebugLoSCommand`**: Checks for static and dynamic object collisions between the player and a selected unit using VMAP. Reports the name and position of colliding models.
*   **`HandleDebugLoSAllowCommand`**: Toggles the `MOD_NO_BREAK_LOS` flag on a colliding static model. This allows or prevents the model from breaking line-of-sight. Changes are logged to `los_mods` file.

### Miscellaneous Debugging

*   **`HandleDebugBattlegroundCommand`**: Toggles battleground testing mode via `BattleGroundMgr/ToggleTesting`.
*   **`HandleDebugAnimCommand`**: Plays an emote on the player.
*   **`HandleDebugSetAuraStateCommand`**: Manually sets or clears aura state flags on a unit.
*   **`HandleDebugAssertFalseCommand`**: Intentionally triggers an assertion failure (`ASSERT(false)`), causing a crash/stack trace. Used for testing crash handlers.
*   **`HandleDebugChatFreezeCommand`**: Whispers a freeze message to a player, likely used to test anti-cheat or freeze mechanisms.
*   **`HandleDebugOverflowCommand`**: Tests string normalization with a specific Unicode overflow string.
*   **`HandleDebugLootTableCommand`**: Simulates loot generation for a specified loot table ID (creature, item, fishing, etc.) multiple times. Calculates drop rates for items. Can focus on a specific item ID.
*   **`HandleDebugItemEnchantCommand`**: Simulates random enchantment rolls for an item with `RandomProperty`. Calculates drop rates for enchantments.
*   **`IsSimilarItem`**: A static helper function comparing two `ItemPrototype` structs for similarity in stats, quality, class, subclass, inventory type, armor, and allowable classes. Used by `HandleFactionChangeItemsCommand`.
*   **`HandleFactionChangeItemsCommand`**: Iterates through all items to identify those that might need faction-specific counterparts (e.g., Alliance-only items that lack a Horde equivalent). It uses `IsSimilarItem` to find potential matches.
*   **`HandleDebugPvPCreditCommand`**: Sends `SMSG_PVP_CREDIT` to simulate honor gain or dishonorable kills.
*   **`HandleDebugMonsterChatCommand`**: Forces a unit to send a chat message of a specified type. Handles different packet structures for different client builds (1.11.2 vs 1.12.1+).
*   **`HandleDebugTimeCommand`**: Sets the global time rate via `World/SetTimeRate`.
*   **`HandleUnitStatCommand`**: Gets or sets the unit state flags of a unit.
*   **`HandleDebugControlCommand`**: Enables or disables client control for a player (likely for botting or remote control testing).
*   **`HandleDebugGetPrevPlayTimeCommand`** & **`HandleDebugSetPrevPlayTimeCommand`**: Reads or writes the previous play time stored in the `WorldSession` for a player's account.

## Cross-Unit Boundaries

*   **ChatHandler.Chat**: Extensively used for argument parsing (`ExtractUInt32`, `ExtractSpellIdFromLink`, etc.), sending system messages (`PSendSysMessage`, `SendSysMessage`), and retrieving session/player context (`GetSession`, `GetSelectedUnit`, `GetPlayer`). Note: `ChatHandler` itself is the class being partially implemented here; these calls are to other members of `ChatHandler` defined in other partials (e.g., `ChatHandler.Chat`).
*   **SpellMgr/SpellEntry**: Used to retrieve spell data (`GetSpellEntry`, `GetMaxSpellId`) and calculate values (`CalculateSimpleValue`, `CalculateDefaultCoefficient`).
*   **WorldSession/Main**: Used to send packets (`SendPacket`), get the player object (`GetPlayer`), and manage session-specific data (`GetUsername`, `SetPreviousPlayedTime`).
*   **Player/Main**: Used to manipulate player state (`SendEquipError`, `SendChannelUpdate`, `SetClientControl`, `GetItemByGuid`) and send specific messages (`SendDirectMessage`, `SendCinematicStart`).
*   **WorldObject/Object**: Used for basic object manipulation (`SetUInt32Value`, `GetPosition`, `GetName`, `GetGUID`).
*   **WorldPacket**: Used to construct raw network packets for injection.
*   **Database/PExecute**: Used by `HandleSpellIconFixCommand` to write to the database.
*   **SpellModMgr**: Used to reload spell modifications after database changes.
*   **Log.Main**: Used for logging debug output (`Out`).
*   **Conditions**: Used to evaluate condition satisfaction.
*   **ScriptMgr**: Used to trigger script events.
*   **ObjectMgr**: Used to retrieve sound entries and item prototypes.
*   **BattleGroundMgr**: Used to toggle testing modes.
*   **MoveMap/MMapManager**: Central to all MMAP commands, providing access to navigation meshes, queries, and loading/unloading functionality.
*   **PathFinder**: Used for path calculation and visualization.
*   **VMAP/ModelInstance**: Used for collision detection and LOS checks.
*   **Loot/LootMgr**: Used for simulating loot generation.
*   **ItemEnchantmentMgr**: Used for retrieving enchantment modifiers.

## Data Model

This unit interacts with the following database tables indirectly or directly:

*   **`spell_mod`**: Written to by `HandleSpellIconFixCommand` to force icon changes. Columns involved: `Id`, `SpellIconID`, `Comment`.
*   **`spell_check`**: Referenced by `HandleDebugSpellCheckCommand` to validate spell properties. The structure is not defined in the schema provided, but it is used as a lookup table for validation rules.

Most other data interactions are with in-memory structures (DBC stores, Loot Templates, MMAP data) rather than direct SQL queries.

## Notable Implementation Details

*   **Hardcoded File Paths**: `HandleDebugSendOpcodeCommand` reads from `opcode.txt` in the working directory. `HandleDebugLoSAllowCommand` writes to `los_mods`. `HandleMmapConnection` writes to `offmesh_connections.txt`. These are relative paths and depend on the server's execution context.
*   **Client Build Conditionals**: `HandleDebugMonsterChatCommand` contains `#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_11_2` blocks to handle differences in chat packet structures between WoW versions.
*   **Raw Memory Manipulation**: Commands like `HandleDebugSetValueByNameCommand` and `HandleDebugModValueCommand` allow direct modification of object fields. Incorrect usage can corrupt object state or cause crashes. Bounds checking is performed (`field >= target->GetValuesCount()`), but semantic validity is not guaranteed.
*   **Performance Impact**: `HandleDebugLootTableCommand` and `HandleDebugItemEnchantCommand` can run thousands of simulations. They include a timeout mechanism (`MAX_TIME = 30` seconds) to prevent hanging the server thread.
*   **Static State**: `HandleMmapConnection` uses static variables (`hasStartPoint`, `startX`, etc.) to maintain state between two invocations of the command. This means only one connection can be recorded at a time per server instance.
*   **French Language Strings**: Some error messages in `HandleSpellEffectsCommand` and `HandleSpellInfosCommand` are hardcoded in French (e.g., "Sort %u inexistant dans les DBCs."). This suggests legacy code or incomplete localization handling in debug commands.
*   **Assertion Crash**: `HandleDebugAssertFalseCommand` intentionally crashes the server. This is dangerous in production and should only be used in development environments.

## Member Reference

**HandleSpellIconFixCommand**: Fixes a spell's icon by updating the `spell_mod` table and reloading mods.
**HandleSpellEffectsCommand**: Displays detailed effect data for a spell, including enchantments and triggers.
**HandleSpellInfosCommand**: Displays high-level spell metadata and flags.
**HandleSpellSearchCommand**: Searches for spells by family name and flags.
**HandleDebugSendSpellFailCommand**: Injects a spell cast failure packet.
**HandleDebugSendNextChannelSpellVisualCommand**: Finds and plays the next channeled spell visual.
**HandleSendSpellChannelVisualCommand**: Plays a specific channeled spell visual.
**HandleDebugSendPoiCommand**: Sends a Point of Interest marker to the client.
**HandleDebugSendEquipErrorCommand**: Injects an equipment error packet.
**HandleDebugSendMailErrorCommand**: Injects a mail result error packet.
**HandleDebugSendSellErrorCommand**: Injects a sell error packet.
**HandleDebugSendBuyErrorCommand**: Injects a buy error packet.
**HandleDebugSendOpenBagCommand**: Forces the client to open a bag window.
**HandleDebugSendOpcodeCommand**: Injects a custom packet defined in `opcode.txt`.
**HandleDebugSendWorldStateCommand**: Updates world state fields on the client.
**HandleDebugPlayCinematicCommand**: Plays a cinematic sequence.
**HandleDebugPlaySoundCommand**: Plays a sound on a unit.
**HandleDebugPlayMusicCommand**: Plays background music for a player.
**HandleDebugPlayScriptText**: Triggers a scripted text event.
**HandleDebugConditionCommand**: Checks if a condition is satisfied.
**HandleDebugSendChannelNotifyCommand**: Injects a channel notification packet.
**HandleDebugSendChatMsgCommand**: Injects a raw chat message packet.
**HandleDebugSendQuestPartyMsgCommand**: Injects a quest party push response.
**HandleDebugGetLootRecipientCommand**: Displays the loot recipient for a creature.
**HandleDebugSendQuestInvalidMsgCommand**: Injects a quest invalid response.
**HandleDebugGetItemStateCommand**: Diagnoses item states and update queue integrity.
**HandleDebugBattlegroundCommand**: Toggles battleground testing mode.
**HandleDebugSpellCheckCommand**: Validates spells against the `spell_check` table.
**HandleDebugAnimCommand**: Plays an emote on the player.
**HandleDebugSetAuraStateCommand**: Sets aura state flags on a unit.
**HandleSetValueHelper**: Helper to set object field values by index/type.
**HandleDebugSetItemValueCommand**: Sets an item field value.
**HandleDebugSetValueByIndexCommand**: Sets a unit field value by index.
**HandleDebugSetValueByNameCommand**: Sets a unit field value by name.
**HandleGetValueHelper**: Helper to get object field values by index/type.
**HandleDebugGetItemValueCommand**: Gets an item field value.
**HandleDebugGetValueByIndexCommand**: Gets a unit field value by index.
**HandleDebugGetValueByNameCommand**: Gets a unit field value by name.
**ShowAllUpdateFieldsHelper**: Prints all non-zero fields of an object.
**ShowUpdateFieldHelper**: Prints a single field's value formatted by type.
**HandlerDebugModValueHelper**: Helper to modify object field values arithmetically/bitwise.
**HandleDebugModItemValueCommand**: Modifies an item field value.
**HandleDebugModValueCommand**: Modifies a unit field value.
**HandleDebugSpellCoefsCommand**: Displays spell damage/healing coefficients.
**HandleDebugSpellModsCommand**: Sends a spell modifier packet to a player.
**HandleDebugLoSCommand**: Checks for VMAP collisions between player and target.
**HandleDebugLoSAllowCommand**: Toggles LOS-breaking flag on a VMAP model.
**HandleSendSpellVisualCommand**: Plays a spell visual on a target.
**HandleSendSpellImpactCommand**: Plays a spell impact visual on a target.
**HandleDebugAssertFalseCommand**: Triggers an assertion failure/crash.
**HandleDebugChatFreezeCommand**: Whispers a freeze message to a player.
**HandleDebugOverflowCommand**: Tests string normalization with overflow data.
**HandleDebugLootTableCommand**: Simulates loot drops to calculate rates.
**HandleDebugItemEnchantCommand**: Simulates enchantment rolls to calculate rates.
**IsSimilarItem**: Compares two item prototypes for similarity.
**HandleFactionChangeItemsCommand**: Identifies items needing faction counterparts.
**HandleVideoTurn**: Moves the player in a spiral around a unit.
**HandleDebugExp**: Experimental movement command for nearby creatures.
**HandleDebugMoveCommand**: Changes a creature's motion master state.
**HandleDebugControlCommand**: Enables/disables client control for a player.
**HandleDebugMonsterChatCommand**: Forces a unit to send a chat message.
**HandleDebugTimeCommand**: Sets the global time rate.
**HandleDebugMoveFlagsCommand**: Gets/sets unit movement flags.
**HandleDebugMoveSplineCommand**: Displays current spline path details.
**HandleUnitStatCommand**: Gets/sets unit state flags.
**HandleDebugPvPCreditCommand**: Simulates honor gain/dishonorable kill.
**HandleDebugMoveToCommand**: Moves the player to a unit's location.
**HandleDebugMoveDistanceCommand**: Moves a unit away from the player.
**HandleDebugFaceMeCommand**: Rotates a unit to face the player.
**HandleDebugForceUpdateCommand**: Forces a field update to the client.
**HandleMmap**: Enables/disables MMAP globally.
**HandleMmapConnection**: Records off-mesh connections to a file.
**HandleMmapTestArea**: Tests pathfinding performance in an area.
**HandleMmapPathCommand**: Calculates and visualizes a path.
**HandleMmapLocCommand**: Displays navmesh location details.
**HandleMmapLoadedTilesCommand**: Lists loaded MMAP tiles.
**HandleMmapStatsCommand**: Displays MMAP statistics.
**HandleMmapUnload**: Unloads MMAP data for the current map.
**HandleMmapLoad**: Loads MMAP data for the current map/tile.
**HandleDebugUnitBytes1Command**: Sets bytes in UNIT_FIELD_BYTES_1.
**HandleDebugUnitBytes2Command**: Sets bytes in UNIT_FIELD_BYTES_2.
**HandleDebugGetPrevPlayTimeCommand**: Displays previous play time for an account.
**HandleDebugSetPrevPlayTimeCommand**: Sets previous play time for an account.

---

<!-- machine-true, projected from graph.json -->

## Map — ChatHandler.DebugCommands

*Source:* DebugCommands.cpp, Chat.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| HandleSpellIconFixCommand | method | ChatHandler.Chat/ExtractSpellIdFromLink, Database/PExecute#2, SpellModMgr/LoadSpellMods | — | — |
| HandleSpellEffectsCommand | method | ChatHandler.Chat/ExtractSpellIdFromLink, ChatHandler.Chat/GetSessionDbcLocale, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SetSentErrorMessage, ChatHandler.LookupCommands/ShowSpellListHelper, SpellEntry/CalculateSimpleValue, SpellMgr/GetSpellEntry, SpellMgr/Instance | — | — |
| HandleSpellInfosCommand | method | ChatHandler.Chat/ExtractSpellIdFromLink, ChatHandler.Chat/GetSessionDbcLocale, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SetSentErrorMessage, ChatHandler.LookupCommands/ShowSpellListHelper, SpellEntry/GetSpellSpecific, SpellEntry/IsBinary, SpellEntry/IsPositiveSpell#4, SpellEntry/IsPvEHeartBeat, SpellMgr/GetSpellEntry, SpellMgr/Instance | — | — |
| HandleSpellSearchCommand | method | ChatHandler.Chat/GetSessionDbcLocale, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, ChatHandler.LookupCommands/ShowSpellListHelper, SpellMgr/GetMaxSpellId, SpellMgr/GetSpellEntry, SpellMgr/Instance | — | — |
| HandleDebugSendSpellFailCommand | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#7, ChatHandler.Chat/ExtractOptUInt32, ChatHandler.Chat/ExtractUInt32, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| HandleDebugSendNextChannelSpellVisualCommand | method | ByteBuffer/operator<<#10, ChatHandler.Chat/PSendSysMessage, Player.Main/SendChannelUpdate, Player.Main/SendDirectMessage, SpellEntry/IsChanneledSpell, SpellMgr/GetMaxSpellId, SpellMgr/GetSpellEntry, SpellMgr/Instance, WorldObject.Object/SetUInt32Value, WorldPacket/WorldPacket#4, WorldSession.Main/GetPlayer | — | — |
| HandleSendSpellChannelVisualCommand | method | ByteBuffer/operator<<#10, ChatHandler.Chat/PSendSysMessage, Player.Main/SendChannelUpdate, Player.Main/SendDirectMessage, SpellMgr/GetMaxSpellId, SpellMgr/GetSpellEntry, SpellMgr/Instance, WorldObject.Object/SetUInt32Value, WorldPacket/WorldPacket#4, WorldSession.Main/GetPlayer | — | — |
| HandleDebugSendPoiCommand | method | ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/SendSysMessage#2, GossipDef/SendPointOfInterest, Log.Main/Out, Object/GetGUIDLow, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldSession.Main/GetPlayer | — | — |
| HandleDebugSendEquipErrorCommand | method | Player.Main/SendEquipError, WorldSession.Main/GetPlayer | — | — |
| HandleDebugSendMailErrorCommand | method | ChatHandler.Chat/ExtractUInt32, WorldSession.MailHandler/SendMailResult | — | — |
| HandleDebugSendSellErrorCommand | method | ObjectGuid/ObjectGuid, Player.Main/SendSellError, WorldSession.Main/GetPlayer | — | — |
| HandleDebugSendBuyErrorCommand | method | Player.Main/SendBuyError, WorldSession.Main/GetPlayer | — | — |
| HandleDebugSendOpenBagCommand | method | ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Object/GetObjectGuid, Player.Main/SendOpenContainer | — | — |
| HandleDebugSendOpcodeCommand | method | ByteBuffer/hexlike, ByteBuffer/operator<<, ByteBuffer/operator<<#10, ByteBuffer/operator<<#11, ByteBuffer/operator<<#13, ByteBuffer/operator<<#7, ByteBuffer/operator<<#9, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/PSendSysMessage#2, Log.Main/Out, Object/GetPackGUID, Object/GetTypeId, ObjectGuid/operator<<#2, WorldObject.Object/GetName, WorldPacket/GetOpcode, WorldPacket/WorldPacket#4, WorldSession.Main/GetPlayer, WorldSession.Main/SendPacket | — | — |
| HandleDebugSendWorldStateCommand | method | ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/PSendSysMessage, Player.Main/SendUpdateWorldState, WorldSession.Main/GetPlayer | — | — |
| HandleDebugPlayCinematicCommand | method | ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/SendCinematicStart, WorldSession.Main/GetPlayer | — | — |
| HandleDebugPlaySoundCommand | method | ChatHandler.Chat/ExtractUint32KeyFromLink, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, ObjectMgr/GetSoundEntry, Player.Main/GetSelectionGuid, WorldObject.Object/PlayDirectSound, WorldObject.Object/PlayDistanceSound, WorldSession.Main/GetPlayer | — | — |
| HandleDebugPlayMusicCommand | method | ByteBuffer/operator<<#4, ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/ExtractUint32KeyFromLink, ChatHandler.Chat/GetSession, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, ObjectMgr/GetSoundEntry, Player.Main/GetSession, Player.Main/SendDirectMessage, WorldPacket/WorldPacket#4, WorldSession.Main/GetSecurity | — | — |
| HandleDebugPlayScriptText | method | ChatHandler.Chat/ExtractInt32, ChatHandler.Chat/GetSelectedUnit, ScriptMgr/DoScriptText, WorldSession.Main/GetPlayer | — | — |
| HandleDebugConditionCommand | method | ChatHandler.Chat/ExtractInt32, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/SendSysMessage, Conditions/IsConditionSatisfied, WorldObject.Object/GetMap, WorldSession.Main/GetPlayer | — | — |
| HandleDebugSendChannelNotifyCommand | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#3, ByteBuffer/operator<<#7, ChatHandler.Chat/ExtractUInt32, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| HandleDebugSendChatMsgCommand | method | ChatHandler.Chat/BuildChatPacket, ChatHandler.Chat/ExtractUInt32, Object/GetObjectGuid, WorldPacket/WorldPacket, WorldSession.Main/GetPlayer, WorldSession.Main/GetPlayerName, WorldSession.Main/SendPacket | — | — |
| HandleDebugSendQuestPartyMsgCommand | method | ChatHandler.Chat/ExtractUInt32, Player.Main/SendPushToPartyResponse, WorldSession.Main/GetPlayer | — | — |
| HandleDebugGetLootRecipientCommand | method | ChatHandler.Chat/GetSelectedCreature, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, Creature.Main/GetLootGroupRecipientId, Creature.Main/GetLootRecipient, Creature.Main/GetLootRecipientGuid, Creature.Main/HasLootRecipient, Object/GetGuidStr, ObjectGuid/GetString | — | — |
| HandleDebugSendQuestInvalidMsgCommand | method | Player.Main/SendCanTakeQuestResponse, WorldSession.Main/GetPlayer | — | — |
| HandleDebugGetItemStateCommand | method | Bag/GetBagSize, Bag/GetItemByPos, ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, game_Objects_Item/GetBagSlot, game_Objects_Item/GetContainer, game_Objects_Item/GetOwnerGuid, game_Objects_Item/GetQueuePos, game_Objects_Item/GetSlot, game_Objects_Item/GetState, game_Objects_Item/IsBag, game_Objects_Item/IsInUpdateQueue, Object/GetGuidStr, Object/GetObjectGuid, ObjectGuid/GetString, ObjectGuid/operator!=, Player.Main/GetItemByPos, Player.Main/GetItemUpdateQueue, WorldSession.Main/GetPlayer | — | — |
| HandleDebugBattlegroundCommand | method | BattleGroundMgr/ToggleTesting | — | — |
| HandleDebugSpellCheckCommand | method | Log.Main/Out, SpellMgr/CheckUsedSpells, SpellMgr/Instance | — | — |
| HandleDebugAnimCommand | method | ChatHandler.Chat/ExtractUInt32, Unit.Main/HandleEmoteCommand, WorldSession.Main/GetPlayer | — | — |
| HandleDebugSetAuraStateCommand | method | ChatHandler.Chat/ExtractInt32, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Unit.Main/ModifyAuraState | — | — |
| HandleSetValueHelper | method | ChatHandler.Chat/ExtractFloat, ChatHandler.Chat/ExtractUInt32Base, ChatHandler.Chat/GetMangosString, ChatHandler.Chat/PSendSysMessage#2, Log.Main/Out, Object/GetObjectGuid, Object/GetValuesCount, ObjectGuid/GetString, WorldObject.Object/SetFloatValue, WorldObject.Object/SetUInt32Value | — | — |
| HandleDebugSetItemValueCommand | method | ChatHandler.Chat/ExtractLiteralArg, ChatHandler.Chat/ExtractOptNotLastArg, ChatHandler.Chat/ExtractUInt32, ObjectGuid/ObjectGuid#2, Player.Main/GetItemByGuid, WorldSession.Main/GetPlayer | — | — |
| HandleDebugSetValueByIndexCommand | method | ChatHandler.Chat/ExtractLiteralArg, ChatHandler.Chat/ExtractOptNotLastArg, ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage | — | — |
| HandleDebugSetValueByNameCommand | method | ChatHandler.Chat/ExtractFloat, ChatHandler.Chat/ExtractQuotedOrLiteralArg, ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Object/GetTypeMask, UpdateFields/GetUpdateFieldDataByName, WorldObject.Object/GetName, WorldObject.Object/SetByteValue, WorldObject.Object/SetFloatValue, WorldObject.Object/SetUInt16Value, WorldObject.Object/SetUInt32Value | — | — |
| HandleGetValueHelper | method | ChatHandler.Chat/GetMangosString, ChatHandler.Chat/PSendSysMessage#2, Log.Main/Out, Object/GetFloatValue, Object/GetObjectGuid, Object/GetUInt32Value, Object/GetValuesCount, ObjectGuid/GetString | — | — |
| HandleDebugGetItemValueCommand | method | ChatHandler.Chat/ExtractLiteralArg, ChatHandler.Chat/ExtractUInt32, ObjectGuid/ObjectGuid#2, Player.Main/GetItemByGuid, WorldSession.Main/GetPlayer | — | — |
| HandleDebugGetValueByIndexCommand | method | ChatHandler.Chat/ExtractLiteralArg, ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage | — | — |
| HandleDebugGetValueByNameCommand | method | ChatHandler.Chat/ExtractQuotedOrLiteralArg, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Object/GetGuidStr, Object/GetTypeMask, UpdateFields/GetUpdateFieldDataByName | — | — |
| ShowAllUpdateFieldsHelper | method | ChatHandler.Chat/PSendSysMessage, Object/GetGuidStr, Object/GetUInt32Value, Object/GetValuesCount | ChatHandler.ObjectCommands/HandleGameObjectUpdateFieldsInfoCommand, ChatHandler.UnitCommands/HandleUnitUpdateFieldsInfoCommand | — |
| ShowUpdateFieldHelper | method | ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, Object/GetByteValue, Object/GetFloatValue, Object/GetGuidValue, Object/GetTypeMask, Object/GetUInt16Value, Object/GetUInt32Value, ObjectGuid/GetString, UpdateFields/GetUpdateFieldDataByTypeMaskAndOffset | — | — |
| HandlerDebugModValueHelper | method | ChatHandler.Chat/ExtractFloat, ChatHandler.Chat/ExtractUInt32Base, ChatHandler.Chat/GetMangosString, ChatHandler.Chat/PSendSysMessage#2, Log.Main/Out, Object/GetFloatValue, Object/GetObjectGuid, Object/GetUInt32Value, Object/GetValuesCount, ObjectGuid/GetString, WorldObject.Object/SetFloatValue, WorldObject.Object/SetUInt32Value | — | — |
| HandleDebugModItemValueCommand | method | ChatHandler.Chat/ExtractLiteralArg, ChatHandler.Chat/ExtractUInt32, ObjectGuid/ObjectGuid#2, Player.Main/GetItemByGuid, WorldSession.Main/GetPlayer | — | — |
| HandleDebugModValueCommand | method | ChatHandler.Chat/ExtractLiteralArg, ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage | — | — |
| HandleDebugSpellCoefsCommand | method | ChatHandler.Chat/ExtractSpellIdFromLink, ChatHandler.Chat/GetMangosString, ChatHandler.Chat/PSendSysMessage#2, SpellEntry/CalculateDefaultCoefficient, SpellMgr/GetSpellEntry, SpellMgr/Instance | — | — |
| HandleDebugSpellModsCommand | method | ByteBuffer/operator<<#4, ByteBuffer/operator<<#7, ChatHandler.Chat/ExtractInt32, ChatHandler.Chat/ExtractLiteralArg, ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/GetNameLink, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/HasLowerSecurity, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/GetSession, Player.Main/PSendSysMessage#2, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| HandleDebugLoSCommand | method | ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SendSysMessage#2, GameObjectModel/getPosition, Map.Main/FindCollisionModel, Map.Main/FindDynamicObjectCollisionModel, WorldObject.Object/GetDistance#3, WorldObject.Object/GetMap, WorldObject.Object/GetPosition#2, WorldSession.Main/GetPlayer | — | — |
| HandleDebugLoSAllowCommand | method | ChatHandler.Chat/ExtractOnOff, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, Log.Main/Out, Map.Main/FindCollisionModel, Object/GetGUIDLow, Player.Main/GetName, WorldObject.Object/GetMap, WorldObject.Object/GetPosition#2, WorldSession.Main/GetPlayer | — | — |
| HandleSendSpellVisualCommand | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#11, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Object/GetGUID, Object/GetObjectGuid, SpellEntry/IsChanneledSpell, SpellMgr/GetSpellEntry, SpellMgr/Instance, Unit.Main/SendSpellGo, Unit.Main/SetChannelObjectGuid, WorldObject.Object/GetName, WorldObject.Object/SendMessageToSet, WorldObject.Object/SetUInt32Value, WorldPacket/WorldPacket#4, WorldSession.Main/GetPlayer | — | — |
| HandleSendSpellImpactCommand | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#11, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Object/GetGUID, WorldObject.Object/GetName, WorldObject.Object/SendMessageToSet, WorldPacket/WorldPacket#4 | — | — |
| HandleDebugAssertFalseCommand | method | Errors/PrintStacktraceAndThrow | — | — |
| HandleDebugChatFreezeCommand | method | ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/GetSession, MasterPlayer.Chat/Whisper, Player.Main/GetSession, WorldSession.Main/GetMasterPlayer, WorldSession.Main/GetSecurity | — | — |
| HandleDebugOverflowCommand | method | ObjectMgr/normalizePlayerName | — | — |
| HandleDebugLootTableCommand | method | ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Loot/Loot, Loot/SetTeam, LootMgr/FillNotNormalLootFor, LootMgr/GetLootFor, LootMgr/Process, LootStore/IsRatesAllowed, Object/GetGUIDLow, ObjectMgr/GetItemPrototype, Player.Main/GetTeam | — | — |
| HandleDebugItemEnchantCommand | method | ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/PSendSysMessage#2, ItemEnchantmentMgr/GetItemEnchantMod, ObjectMgr/GetItemPrototype | — | — |
| IsSimilarItem | function | — | — | — |
| HandleFactionChangeItemsCommand | method | ChatHandler.Chat/PSendSysMessage, ObjectMgr/GetItemPrototypeMap, ObjectMgr/GetMountDataByEntry | — | — |
| HandleVideoTurn | method | ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/SendSysMessage#2, Log.Main/Out, MoveSplineInit/Launch, MoveSplineInit/MovebyPath, MoveSplineInit/MoveSplineInit, MoveSplineInit/SetFly, MoveSplineInit/SetVelocity, WorldObject.Object/GetPosition#2, WorldSession.Main/GetPlayer | — | — |
| HandleDebugExp | method | AnyUnitInObjectRangeCheck/AnyUnitInObjectRangeCheck, Cell/Cell#2, Cell/SetNoCreate, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/SendSysMessage#2, GridDefines/ComputeCellPair, MoveSplineInit/Launch, MoveSplineInit/MovebyPath, MoveSplineInit/MoveSplineInit, MoveSplineInit/SetVelocity, MoveSplineInit/SetWalk, WorldObject.Object/GetMap, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/UpdateGroundPositionZ | — | — |
| HandleDebugMoveCommand | method | ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/SendSysMessage, Creature.MotionMaster/MoveConfused, Creature.MotionMaster/MoveFeared, Creature.MotionMaster/MoveFleeing, Creature.MotionMaster/MoveIdle, Creature.MotionMaster/MoveRandom, MotionMaster/Clear, Unit.Main/GetMotionMaster, WorldSession.Main/GetPlayer | — | — |
| HandleDebugControlCommand | method | ChatHandler.Chat/ExtractOnOff, ChatHandler.Chat/GetSelectedPlayer, Player.Main/SetClientControl | — | — |
| HandleDebugMonsterChatCommand | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#3, ByteBuffer/operator<<#7, ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/GetSelectedUnit, Object/GetObjectGuid, ObjectGuid/operator<<, Player.Main/GetName, WorldObject.Object/SendMessageToSet, WorldPacket/WorldPacket#4, WorldSession.Main/GetPlayer | — | — |
| HandleDebugTimeCommand | method | ChatHandler.Chat/ExtractFloat, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, World/SetTimeRate | — | — |
| HandleDebugMoveFlagsCommand | method | ChatHandler.Chat/ExtractUInt32Base, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/PSendSysMessage, Unit.Main/SendHeartBeat, WorldObject.Object/GetUnitMovementFlags | — | — |
| HandleDebugMoveSplineCommand | method | ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/PSendSysMessage, MoveSpline/Finalized, MoveSpline/GetMovementOrigin, MoveSpline/getPath, Object/GetGuidStr | — | — |
| HandleUnitStatCommand | method | ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/PSendSysMessage, Unit.Main/AddUnitState, Unit.Main/ClearUnitState, Unit.Main/HasUnitState | — | — |
| HandleDebugPvPCreditCommand | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#11, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/PSendSysMessage, Object/GetGUID, Object/GetTypeId, shared_Util/urand, WorldPacket/WorldPacket#4, WorldSession.Main/GetPlayer, WorldSession.Main/SendPacket | — | — |
| HandleDebugMoveToCommand | method | ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/PSendSysMessage, Creature.MotionMaster/MovePoint, Unit.Main/GetMotionMaster, WorldObject.Object/GetName, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldSession.Main/GetPlayer | — | — |
| HandleDebugMoveDistanceCommand | method | ChatHandler.Chat/ExtractFloat, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage#2, Creature.MotionMaster/MoveDistance, Unit.Main/GetMotionMaster, WorldObject.Object/GetName, WorldSession.Main/GetPlayer | — | — |
| HandleDebugFaceMeCommand | method | ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/PSendSysMessage, Unit.Main/SetFacingTo, WorldObject.Object/GetAngle, WorldSession.Main/GetPlayer | — | — |
| HandleDebugForceUpdateCommand | method | ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, WorldObject.Object/ForceValuesUpdateAtIndex | — | — |
| HandleMmap | method | ChatHandler.Chat/ExtractOnOff, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, World/getConfig, World/setConfig#2 | — | — |
| HandleMmapConnection | method | ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, Log.Main/Out, WorldObject.Object/GetMapId, WorldObject.Object/GetPosition#2, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldSession.Main/GetPlayer | — | — |
| HandleMmapTestArea | method | AnyUnitInObjectRangeCheck/AnyUnitInObjectRangeCheck, Cell/Cell#2, Cell/SetNoCreate, ChatHandler.Chat/ExtractFloat, ChatHandler.Chat/PSendSysMessage, Creature.Main/GetDBTableGUIDLow, Creature.Main/IsTrigger, GridDefines/ComputeCellPair, Object/GetEntry, Object/GetTypeId, Object/ToCreature, PathInfo/getPathType, shared_Util/getMSTime, WorldObject.Object/GetMap, WorldObject.Object/GetPosition#2, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.PathFinder/calculate#2, WorldObject.PathFinder/PathInfo, WorldSession.Main/GetPlayer, WorldTimer/getMSTimeDiff | — | — |
| HandleMmapPathCommand | method | ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/PSendSysMessage, GameObject/GetDisplayId, GenericTransport/CalculatePassengerPosition, MoveMap/createOrGetMMapManager, MoveMap/GetGONavMesh, MoveMap/GetNavMesh, MoveSplineInit/Launch, MoveSplineInit/MoveSplineInit, MoveSplineInit/SetFacing#2, MoveSplineInit/SetTransport, Object/GetGUIDLow, PathInfo/getFullPath, PathInfo/getPathType, PathInfo/SetTransport, Player.Main/GetName, Transport/AddPassenger, Unit.Main/SendHeartBeat, Unit.Main/SetFly, Unit.Main/SetLevel, WorldObject.Object/GetMapId, WorldObject.Object/GetName, WorldObject.Object/GetOrientation, WorldObject.Object/GetPosition#2, WorldObject.Object/GetTransport, WorldObject.Object/SummonCreature#2, WorldObject.PathFinder/calculate#2, WorldObject.PathFinder/Length, WorldObject.PathFinder/PathInfo, WorldSession.Main/GetPlayer | — | — |
| HandleMmapLocCommand | method | ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, GameObject/GetDisplayId, GenericTransport/CalculatePassengerOffset, GenericTransport/CalculatePassengerPosition, MoveMap/createOrGetMMapManager, MoveMap/GetModelNavMeshQuery, MoveMap/GetNavMesh, MoveMap/GetNavMeshQuery, Transport/AddPassenger, Unit.Main/SendHeartBeat, Unit.Main/SetFly, Unit.Main/StopMoving, WorldObject.Object/GetMapId, WorldObject.Object/GetPosition#2, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetTransport, WorldObject.Object/SummonCreature#2, WorldObject.PathFinder/FindWalkPoly | — | — |
| HandleMmapLoadedTilesCommand | method | ChatHandler.Chat/PSendSysMessage, MoveMap/createOrGetMMapManager, MoveMap/GetNavMesh, MoveMap/GetNavMeshQuery, WorldObject.Object/GetMapId, WorldSession.Main/GetPlayer | — | — |
| HandleMmapStatsCommand | method | ChatHandler.Chat/PSendSysMessage, GameObject/GetDisplayId, MMapManager/getLoadedMapsCount, MMapManager/getLoadedTilesCount, MoveMap/createOrGetMMapManager, MoveMap/GetModelNavMeshQuery, MoveMap/GetNavMesh, World/getConfig, WorldObject.Object/GetMapId, WorldObject.Object/GetTransport, WorldSession.Main/GetPlayer | — | — |
| HandleMmapUnload | method | ChatHandler.Chat/PSendSysMessage, MoveMap/createOrGetMMapManager, MoveMap/unloadMap, WorldObject.Object/GetMapId, WorldSession.Main/GetPlayer | — | — |
| HandleMmapLoad | method | ChatHandler.Chat/PSendSysMessage, MoveMap/createOrGetMMapManager, MoveMap/loadMap, WorldObject.Object/GetMapId, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldSession.Main/GetPlayer | — | — |
| HandleDebugUnitBytes1Command | method | ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, WorldObject.Object/SetByteValue | — | — |
| HandleDebugUnitBytes2Command | method | ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, WorldObject.Object/SetByteValue | — | — |
| HandleDebugGetPrevPlayTimeCommand | method | ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/GetSession, shared_Util/secsToTimeString, WorldSession.Main/GetPreviousPlayedTime, WorldSession.Main/GetUsername | — | — |
| HandleDebugSetPrevPlayTimeCommand | method | ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/GetSession, shared_Util/secsToTimeString, WorldSession.Main/GetUsername, WorldSession.Main/SetPreviousPlayedTime | — | — |

---

<!-- verify: boundary-bleed | foreign: ChatHandler, load, update -->
