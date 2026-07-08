# ReputationMgr

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ReputationMgr

## Purpose & Responsibilities

`ReputationMgr` is the subsystem responsible for managing a `Player`'s standing with various in-game factions. It acts as the authoritative source for reputation data, handling the calculation of current standing, the determination of reputation ranks (e.g., Hated, Neutral, Exalted), and the synchronization of this state with the client via network packets.

Key responsibilities include:
1.  **State Management:** Maintaining a map (`m_factions`) of all known factions, their current standing, and visibility/war flags for the owning player.
2.  **Calculation Logic:** Computing effective reputation by combining base reputation (derived from race/class templates) with earned standing. It also handles "spillover" mechanics, where gaining reputation with one faction automatically grants scaled reputation to allied factions.
3.  **Client Synchronization:** Sending specific opcodes (`SMSG_INITIALIZE_FACTIONS`, `SMSG_SET_FACTION_STANDING`, etc.) to ensure the client's reputation UI matches the server's state.
4.  **Persistence:** Loading and saving reputation states to the `character_reputation` database table.

## Member-by-Member Behavior

### Initialization and Lifecycle

*   **`ReputationMgr`**: The constructor initializes the manager with a pointer to the owning `Player`. It does not load data immediately; initialization is deferred until `LoadFromDB` or explicit calls.
*   **`~ReputationMgr`**: The destructor performs no special cleanup, relying on standard container destruction for `m_factions` and `m_forcedReactions`.
*   **`Initialize`**: Clears the existing faction map and populates it with entries from the global faction database (`sObjectMgr.GetFactionMap`). For every faction with a valid `reputationListID`, it creates a `FactionState` with zero standing and default flags derived from the player's race and class. This ensures the manager knows about all possible factions before applying saved data.

### Reputation Calculation and Access

*   **`ReputationToRank`**: A static utility that converts a raw integer standing value into a `ReputationRank` enum. It iterates backward through the `PointsInRank` array, subtracting thresholds until it finds the rank corresponding to the standing.
*   **`GetRepPointsToRank`**: Calculates the total cumulative points required to reach a specific rank from the bottom threshold (-42000). It uses `std::accumulate` on the `PointsInRank` array.
*   **`GetBaseReputation`**: Determines the starting reputation a player has with a faction based solely on their race and class. It queries the `FactionEntry` for an index matching the player's masks and returns the associated `BaseRepValue`.
*   **`GetReputation` (overloads)**:
    *   The `uint32` overload retrieves the `FactionEntry` and delegates to the pointer overload. If the faction is unknown, it logs an error and returns 0.
    *   The `FactionEntry*` overload calculates the total standing by adding the `GetBaseReputation` to the stored `Standing` in the `FactionState`. If no state exists, it returns 0.
*   **`GetRank`** / **`GetBaseRank`**: Retrieve the current or base reputation respectively, then pass the value to `ReputationToRank` to determine the rank.
*   **`GetState`** / **`GetStateList`**: Provide read-only access to the internal `FactionState` structures. `GetState` looks up a specific faction by entry or list ID, while `GetStateList` returns the entire map.

### Modifying Reputation

*   **`ModifyReputation`**: A public wrapper that calls the private `SetReputation` with `incremental=true`. This is the primary entry point for gaining or losing reputation (e.g., from quests or kills).
*   **`SetReputation` (public/private overloads)**:
    *   The core logic resides in the private overload accepting `incremental` and `noSpillover` flags.
    *   **Spillover Handling**: If `noSpillover` is false, it checks for a `RepSpilloverTemplate`. For each allied faction in the template, if the player's rank with that ally is below a threshold, it calculates a scaled amount of reputation (`standing * rate`) and applies it via `SetOneFactionReputation`.
    *   **Application**: After spillover, it updates the target faction's standing via `SetOneFactionReputation` and sends the updated state to the client via `SendState`.
*   **`SetOneFactionReputation`**: Updates the `Standing` field in the `FactionState`.
    *   If `incremental` is true, it adds the delta to the current standing plus base reputation.
    *   It clamps the resulting standing between `Reputation_Bottom` (-42000) and `Reputation_Cap` (42999).
    *   It stores the *difference* from the base reputation in `faction.Standing`.
    *   It marks the faction as needing save/send, sets it visible, and triggers `SetAtWar` if the rank drops to Hostile or lower.
    *   Finally, it notifies the `Player` via `ReputationChanged`.

### Visibility, War, and Inactive States

*   **`SetVisible`**: Marks a faction as visible in the client UI. It checks for flags that force invisibility (`FACTION_FLAG_INVISIBLE_FORCED`, `FACTION_FLAG_HIDDEN`). If allowed, it sets `FACTION_FLAG_VISIBLE`, marks for save/send, and sends `SMSG_SET_FACTION_VISIBLE` to the client.
*   **`SetAtWar`**: Toggles the `FACTION_FLAG_AT_WAR` flag.
    *   It prevents declaring war if the faction has `FACTION_FLAG_PEACE_FORCED` and the player's rank is higher than Hated.
    *   It prevents changing war status for hidden/forced-invisible factions.
*   **`SetInactive`**: Toggles the `FACTION_FLAG_INACTIVE` flag, which hides the faction from the client UI temporarily. It requires the faction to be visible first.
*   **`ApplyForceReaction`**: Manages temporary forced reactions (e.g., from spells). It adds or removes entries from the `m_forcedReactions` map.
*   **`GetForcedRankIfAny`**: Checks if a faction has a forced reaction active and returns the rank if so.

### Client Communication

*   **`SendInitialReputations`**: Sends `SMSG_INITIALIZE_FACTIONS`. It constructs a fixed-size packet containing up to 64 faction slots. It fills slots for known factions with their flags and standing, and pads empty slots with zeros. This is typically sent once during login.
*   **`SendState`**: Sends `SMSG_SET_FACTION_STANDING`. It sends the standing for the specified faction and any other factions marked with `needSend`. It resets the `needSend` flag for sent factions.
*   **`SendForceReactions`**: Sends `SMSG_SET_FORCED_REACTIONS`, transmitting the current map of forced reaction ranks to the client.
*   **`SendVisible`**: Sends `SMSG_SET_FACTION_VISIBLE` for a single faction. It skips sending if the player is still loading (checked via `PlayerLoading()`).

### Persistence

*   **`LoadFromDB`**:
    1.  Calls `Initialize` to build the full faction map.
    2.  Iterates through the provided `QueryResult` from `character_reputation`.
    3.  For each row, it updates the corresponding `FactionState`'s standing and flags.
    4.  It re-applies visibility, inactive, and war states using the setter methods to ensure consistency with current rules (e.g., checking peace-forced flags).
    5.  It forces `SetAtWar` if the calculated rank is Hostile or lower.
    6.  If the loaded flags match the current flags, it clears the `needSend` and `needSave` flags to avoid redundant network traffic or DB writes.
*   **`SaveToDB`**:
    1.  Prepares prepared statements for deletion and insertion into `character_reputation`.
    2.  Iterates through `m_factions`.
    3.  For any faction marked `needSave`, it deletes the old record (if any) and inserts the new one with the current standing and flags.
    4.  Clears the `needSave` flag.

## Cross-Unit Boundaries

*   **Player.Main**:
    *   `ReputationMgr` holds a raw pointer to `Player` (`m_player`).
    *   It calls `Player::GetName` for logging errors in `GetReputation`.
    *   It calls `Player::GetRaceMask` and `Player::GetClassMask` in `GetBaseReputation` and `GetDefaultStateFlags` to determine base standings.
    *   It calls `Player::SendDirectMessage` in all `Send*` methods to transmit packets.
    *   It calls `Player::ReputationChanged` in `SetOneFactionReputation` to notify the player object of changes.
    *   It calls `Player::GetGUIDLow` in `SaveToDB` and `LoadFromDB` for persistence.
    *   It calls `Player::GetSession()->PlayerLoading()` in `SendVisible` to suppress packets during login.
*   **ObjectMgr**:
    *   Calls `GetFactionEntry` frequently to resolve faction IDs to `FactionEntry` structs.
    *   Calls `GetFactionMap` in `Initialize` to populate the initial state.
    *   Calls `GetRepSpilloverTemplate` in `SetReputation` to handle alliance reputation gains.
*   **Log.Main**:
    *   Calls `Out` in `GetReputation` to log errors when an invalid faction ID is queried.
*   **ChatHandler / Commands**:
    *   Various command handlers call `GetReputation`, `GetState`, `GetRank`, `SetReputation`, and `GetStateList` to allow GMs to inspect or modify player reputation.
*   **Spell Effects / Items**:
    *   `Spell.Effects/EffectReputation` calls `ModifyReputation` to apply reputation changes from spells.
    *   `spell_item/OnAfterApply` and `Unit.SpellAuras/HandleForceReaction` call `ApplyForceReaction` and `SendForceReactions` to manage temporary reputation buffs/debuffs.
*   **Gameplay Systems**:
    *   `Player.Main/CanCompleteQuest`, `FullQuestComplete`, etc., call `GetReputation` to check quest requirements.
    *   `WorldObject.Object/IsValidAttackTarget` and related methods call `GetState` and `GetForcedRankIfAny` to determine if combat is allowed based on faction relations.
    *   `game_Battlegrounds_BattleGround/RewardReputationToTeam` and `instance_*` scripts call `ModifyReputation` to reward players for dungeon/battleground participation.

## Data Model

The `ReputationMgr` interacts with one database table:

*   **`character_reputation`**: Stores persistent reputation data for players.
    *   **Columns**:
        *   `guid` (int, PK): The player's GUID.
        *   `faction` (int, PK): The faction ID.
        *   `standing` (int): The current standing value (relative to base).
        *   `flags` (int): Bitmask of `FactionFlags` (Visible, AtWar, Hidden, etc.).
    *   **Usage**:
        *   `LoadFromDB` reads all rows for the player's GUID to restore state.
        *   `SaveToDB` deletes existing rows for modified factions and inserts new ones. Only factions marked `needSave` are written.

## Notable Implementation Details

1.  **Standing Storage**: The `FactionState::Standing` field does **not** store the absolute reputation value. It stores the difference between the current total reputation and the `BaseReputation`. This allows the base reputation (which might change if race/class logic were dynamic, though it isn't currently) to be factored out. Calculations in `GetReputation` add them back together.
2.  **Spillover Logic**: Spillover is applied recursively in `SetReputation`. However, it only applies if the player's rank with the *target* spillover faction is below a certain threshold defined in the template. This prevents infinite loops or excessive gains at high ranks.
3.  **Fixed Packet Size**: `SendInitialReputations` assumes a maximum of 64 factions. It pads the packet with zeros if fewer factions are present. This is a legacy constraint from the client protocol.
4.  **War State Enforcement**: `SetAtWar` enforces rules that prevent players from declaring war on factions marked `PEACE_FORCED` unless they are already Hated or lower. This is crucial for preventing players from attacking their own faction's allies.
5.  **Lazy Saving**: The `needSave` flag ensures that only changed reputation states are written to the database. `LoadFromDB` resets these flags if the loaded data matches the initialized state, preventing unnecessary writes on login.
6.  **Error Logging**: `GetReputation` logs an error if a faction entry is not found. This helps identify bugs where quests or items reference non-existent faction IDs.

## Member Reference

*   **`ReputationToRank`**: Static method converting raw standing integer to `ReputationRank` enum by iterating through `PointsInRank` thresholds.
*   **`GetRepPointsToRank`**: Static method calculating cumulative points needed to reach a specific rank from the bottom threshold.
*   **`GetReputation#2`**: Overload taking `uint32` faction ID; retrieves `FactionEntry` and delegates to pointer overload. Logs error if faction is unknown.
*   **`GetBaseReputation`**: Calculates starting reputation based on player's race/class masks and `FactionEntry` data.
*   **`ReputationMgr`**: Constructor initializing the manager with a `Player` pointer.
*   **`~ReputationMgr`**: Destructor performing standard cleanup.
*   **`GetReputation`**: Overload taking `FactionEntry*`; returns sum of base reputation and stored standing.
*   **`GetStateList`**: Returns the internal map of all faction states.
*   **`GetState`**: Retrieves `FactionState` for a given `FactionEntry` or `RepListID`.
*   **`GetState#2`**: Overload retrieving `FactionState` by `RepListID`.
*   **`GetRank`**: Returns the `ReputationRank` for a faction based on its current total standing.
*   **`GetBaseRank`**: Returns the `ReputationRank` for a faction based on its base standing only.
*   **`GetForcedRankIfAny`**: Checks if a faction has an active forced reaction and returns the rank.
*   **`ApplyForceReaction`**: Adds or removes a forced reaction rank for a faction.
*   **`SetReputation`**: Public wrapper setting absolute standing; private overload handles incremental updates and spillover logic.
*   **`GetDefaultStateFlags`**: Retrieves default visibility/war flags for a faction based on player race/class.
*   **`ModifyReputation`**: Public wrapper calling `SetReputation` with `incremental=true`.
*   **`SendForceReactions`**: Sends `SMSG_SET_FORCED_REACTIONS` packet with current forced reaction map.
*   **`SendState`**: Sends `SMSG_SET_FACTION_STANDING` packet for specified faction and any others marked `needSend`.
*   **`SendInitialReputations`**: Sends `SMSG_INITIALIZE_FACTIONS` packet with up to 64 faction slots.
*   **`SendVisible`**: Sends `SMSG_SET_FACTION_VISIBLE` packet for a single faction, skipping if player is loading.
*   **`Initialize`**: Populates `m_factions` with all known factions from `ObjectMgr`, setting zero standing and default flags.
*   **`SetReputation#2`**: Private overload handling spillover calculations and delegating to `SetOneFactionReputation`.
*   **`SetOneFactionReputation`**: Updates standing in `FactionState`, clamps values, marks for save/send, and notifies player.
*   **`SetVisible#3`**: Overload taking `FactionTemplateEntry`; resolves to `FactionEntry` and delegates.
*   **`SetVisible#2`**: Overload taking `FactionEntry`; delegates to `FactionState*` overload.
*   **`SetVisible`**: Sets `FACTION_FLAG_VISIBLE` on `FactionState` if not forced hidden, and sends packet.
*   **`SetAtWar#2`**: Overload taking `RepListID`; delegates to `FactionState*` overload.
*   **`SetAtWar`**: Toggles `FACTION_FLAG_AT_WAR` with checks for peace-forced flags and visibility.
*   **`SetInactive#2`**: Overload taking `RepListID`; delegates to `FactionState*` overload.
*   **`SetInactive`**: Toggles `FACTION_FLAG_INACTIVE` if faction is visible.
*   **`LoadFromDB`**: Initializes factions, then iterates query results to restore standing and flags, re-applying rules.
*   **`SaveToDB`**: Deletes and inserts records in `character_reputation` for all factions marked `needSave`.

---

<!-- machine-true, projected from graph.json -->

## Map — ReputationMgr

*Source:* ReputationMgr.cpp, ReputationMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ReputationToRank | method | — | — | — |
| GetRepPointsToRank | method | — | Player.Main/SatisfyItemRequirements | — |
| GetReputation#2 | method | Log.Main/Out, ObjectMgr/GetFactionEntry, Player.Main/GetName | Player.Main/CanCompleteQuest, Player.Main/FullQuestComplete, Player.Main/SatisfyQuestReputation | — |
| GetBaseReputation | method | FactionEntry/GetIndexFitTo, Unit.Main/GetClassMask, Unit.Main/GetRaceMask | Player.Main/ChangeReputationsForRace | — |
| ReputationMgr | ctor | — | Player.Main/Player#5 | — |
| ~ReputationMgr | dtor | — | — | — |
| GetReputation | method | — | ChatHandler.CharacterCommands/HandleModifyRepCommand, ChatHandler.LookupCommands/ShowFactionListHelper, Player.Main/ReputationChanged | — |
| GetStateList | method | — | ChatHandler.CharacterCommands/HandleCharacterReputationCommand, Player.Main/ChangeReputationsForRace | — |
| GetState | method | — | ChatHandler.LookupCommands/HandleLookupFactionCommand, Player.Main/ChangeReputationsForRace, WorldObject.Object/GetFactionReactionTo, WorldObject.Object/GetReactionTo, WorldObject.Object/IsValidAttackTarget | — |
| GetState#2 | method | — | Player.Main/ChangeReputationsForRace | — |
| GetRank | method | — | ChatHandler.LookupCommands/ShowFactionListHelper, Conditions/Evaluate, GameObject/IsFriendlyTo, GameObject/IsHostileTo, instance_ruins_of_ahnqiraj/GiveRepAfterRajaxxDeath, Player.Main/CanInteractWithNPC, Player.Main/ClearTemporaryWarWithFactions, Player.Main/GetReputationRank, Player.Main/RewardReputation#2, Player.Main/SatisfyItemRequirements, WorldObject.Object/GetFactionReactionTo | — |
| GetBaseRank | method | — | — | — |
| GetForcedRankIfAny | method | — | GameObject/IsFriendlyTo, GameObject/IsHostileTo, WorldObject.Object/GetFactionReactionTo, WorldObject.Object/GetReactionTo, WorldObject.Object/IsValidAttackTarget | — |
| ApplyForceReaction | method | — | spell_item/OnAfterApply, Unit.SpellAuras/HandleForceReaction | — |
| SetReputation | method | — | ChatHandler.CharacterCommands/HandleModifyRepCommand, Player.Main/ChangeReputationsForRace, Player.Main/FullQuestComplete, Player.Main/SatisfyItemRequirements | — |
| GetDefaultStateFlags | method | FactionEntry/GetIndexFitTo, Unit.Main/GetClassMask, Unit.Main/GetRaceMask | — | — |
| ModifyReputation | method | — | game_Battlegrounds_BattleGround/RewardReputationToTeam, game_Group_Group/RewardGroupAtKill_helper, instance_naxxramas.Main/SetData, instance_ruins_of_ahnqiraj/GiveRepAfterRajaxxDeath, Player.Main/RewardReputation, Player.Main/RewardReputation#2, Spell.Effects/EffectReputation | — |
| SendForceReactions | method | ByteBuffer/operator<<#10, Player.Main/SendDirectMessage, WorldPacket/Initialize, WorldPacket/WorldPacket | spell_item/OnAfterApply, Unit.SpellAuras/HandleForceReaction | — |
| SendState | method | ByteBuffer/operator<<#10, ByteBuffer/wpos, Player.Main/SendDirectMessage, WorldPacket/WorldPacket#4 | Player.Main/ChangeReputationsForRace | — |
| SendInitialReputations | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#7, Player.Main/SendDirectMessage, WorldPacket/WorldPacket#4 | Player.Main/SendInitialPacketsBeforeAddToMap | — |
| SendVisible | method | ByteBuffer/operator<<#10, Player.Main/GetSession, Player.Main/SendDirectMessage, WorldPacket/WorldPacket#4, WorldSession.Main/PlayerLoading | — | — |
| Initialize | method | ObjectMgr/GetFactionMap | — | — |
| SetReputation#2 | method | ObjectMgr/GetFactionEntry, ObjectMgr/GetRepSpilloverTemplate, Player.Main/GetReputationRank | — | — |
| SetOneFactionReputation | method | Player.Main/ReputationChanged | — | — |
| SetVisible#3 | method | ObjectMgr/GetFactionEntry | WorldSession.MiscHandler/HandleSetSelectionOpcode | — |
| SetVisible#2 | method | — | Player.Main/AddQuest | — |
| SetVisible | method | — | — | — |
| SetAtWar#2 | method | — | Creature.Main/OnEnterCombat, Player.Main/ClearTemporaryWarWithFactions, WorldSession.CharacterHandler/HandleSetFactionAtWarOpcode | — |
| SetAtWar | method | — | — | — |
| SetInactive#2 | method | — | WorldSession.CharacterHandler/HandleSetFactionInactiveOpcode | — |
| SetInactive | method | — | — | — |
| LoadFromDB | method | Field/GetUInt32, ObjectMgr/GetFactionEntry, QueryResult/Fetch, QueryResult/NextRow | Player.Main/Create, Player.Main/LoadFromDB | — |
| SaveToDB | method | Database/CreateStatement, Object/GetGUIDLow, SqlStatementID/SqlStatementID | Player.Main/SaveToDB | character_reputation |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `character_reputation`: guid int(11) unsigned PK, faction int(11) unsigned PK, standing int(11), flags int(11)

*`?` = nullable, `PK` = primary key column.*

