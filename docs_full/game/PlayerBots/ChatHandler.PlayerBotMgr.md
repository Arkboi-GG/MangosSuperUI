<!-- provenance: failed-members, boundary-bleed -->
# ChatHandler.PlayerBotMgr

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# PlayerBotMgr: Architecture and Reference Documentation

## Purpose & Responsibilities

`PlayerBotMgr` is the central singleton manager for the server-side NPC bot system in wowvmangos. Its primary responsibility is to simulate human-like player characters ("bots") within the game world to populate zones, fill battleground queues, and assist players in groups (party bots).

The unit handles three distinct categories of bots:
1.  **Random Bots:** Persistent bots loaded from the `playerbot` database table. These are managed automatically by the `Update` loop to maintain a configured minimum and maximum population online, simulating a lively world.
2.  **Party Bots:** Temporary or persistent bots spawned via chat commands (implemented in `ChatHandler` methods such as `HandlePartyBotAddCommand`) to join a player's group. They follow complex AI behaviors defined in `PartyBotAI` (e.g., tanking, healing, DPS roles).
3.  **Battle Bots:** Bots spawned specifically to fill battleground queues (via `HandleBattleBotAddCommand` or auto-join logic) or automatically via `m_confBattleBotAutoJoin`. They use `BattleBotAI` to navigate predefined waypoints.

Additionally, this unit provides the **Chat Command Interface** implementations (methods prefixed with `Handle...` in `PlayerBotMgr.cpp`) allowing Game Masters and players to spawn, control, and debug these bots. It manages the lifecycle of bot accounts, sessions, and AI objects, ensuring they log in, load their AI, and interact with the world correctly.

## Data Model

`PlayerBotMgr` interacts with two database tables:

### `account`
*   **Usage:** Read-only during initialization to determine the highest existing account ID.
*   **Columns Accessed:** `id`
*   **Logic:** The manager queries `MAX(id)` from the `account` table to establish a safe starting point for generating unique account IDs for bots. It adds a buffer of 10,000 to this maximum to avoid collisions with real user accounts.

### `playerbot`
*   **Usage:** Source of truth for persistent random bots.
*   **Columns Accessed:** `char_guid`, `chance`, `ai`, `race`, `class`, `level`, `map`, `position_x`, `position_y`, `position_z`, `name`
*   **Logic:** During `Load()`, the manager iterates through all rows in this table. Each row defines a bot's identity, appearance, spawn location, and AI type. The `chance` column determines the probability weight for selecting this bot when `AddRandomBot()` is called.

## Cross-Unit Boundaries

`PlayerBotMgr` acts as a coordinator between the core engine systems and the bot-specific AI implementations.

### Core Engine Integration
*   **`World`**: Called by `LoadConfig` and `Update`. `PlayerBotMgr` relies on `World` for global configuration settings (`getConfig`) and time tracking (`GetGameTime`). `World::Update` calls `PlayerBotMgr::Update` periodically.
*   **`WorldSession`**: `PlayerBotMgr` creates `WorldSession` objects for each bot (`AddBot`). It sets the bot flag on the session (`SetBot`) and forces connections via `ForceAccountConnection`. Sessions are used to log bots in and out.
*   **`ObjectMgr`**: Used to resolve player names to GUIDs (`GetPlayerGuidByName`), generate new low GUIDs for bots (`GeneratePlayerLowGuid`), and retrieve player info for race/class validation (`GetPlayerInfo`).
*   **`AccountMgr`**: Used to verify security levels when creating bot sessions (`GetSecurity`).
*   **`BattleGroundMgr` / `BattleGround`**: `Update` checks battleground queues to auto-fill them with bots if `m_confBattleBotAutoJoin` is enabled. It retrieves queue information and minimum player requirements.
*   **`MapManager`**: Used to determine continent instance IDs for spawning battle bots on specific maps (e.g., GM Island).

### Bot AI Integration
*   **`PlayerBotAI`**: The base AI class. `PlayerBotMgr` instantiates this (or derived classes) and attaches it to the `Player` object via `SetAI`. It calls `OnBotEntryLoad` and `OnSessionLoaded` to initialize the AI.
*   **`PartyBotAI`**: Derived AI for group members. Chat commands in `PlayerBotMgr` cast `Player::AI()` to `PartyBotAI*` to invoke specific behaviors like `AttackStart`, `ResetSpellData`, or setting roles.
*   **`BattleBotAI`**: Derived AI for battlegrounds. Similar to PartyBotAI, it is instantiated with specific waypoint data and controlled via chat commands.
*   **`AiBotAI`**: A specific AI type mentioned in the DB load logic. If the `ai` column in `playerbot` is "AiBotAI", the bot is marked as `customBot`.

### Chat System Integration
*   **`ChatHandler`**: Many members in this unit are implementations of `ChatHandler` methods (prefixed with `Handle...`). They use `ChatHandler` utilities like `SendSysMessage`, `ExtractArg`, `GetSelectedPlayer`, and `SetSentErrorMessage` to provide feedback to the user issuing the command.

## Notable Implementation Details

### Account ID Generation Strategy
`PlayerBotMgr` does not create permanent records in the `account` table for most bots. Instead, it generates synthetic account IDs starting from `MAX(account.id) + 10000`. This avoids cluttering the main account table and allows bots to log in using these high-value IDs. The `GenBotAccountId()` method increments a counter (`m_maxAccountId`) to ensure uniqueness.

### State Management
Bots have three states defined in `PlayerBotState`:
1.  `PB_STATE_OFFLINE`: Not logged in.
2.  `PB_STATE_LOADING`: Session created, waiting for AI initialization.
3.  `PB_STATE_ONLINE`: Fully active in the world.

The `Update` loop transitions bots from `LOADING` to `ONLINE` once `PlayerBotAI::OnSessionLoaded` returns true. If loading fails, the bot is deleted.

### Temporary Bots
Some bots (like chat bots or short-lived test bots) are tracked in `m_tempBots`, a map of account ID to remaining lifetime in milliseconds. The `Update` loop decrements these timers. When a timer expires, the bot is removed from `m_bots` and its session is logged out. This prevents resource leaks from transient bots.

### Random Bot Selection Weighting
`AddRandomBot()` uses a weighted random selection algorithm. It sums the `chance` values of all offline bots into `m_totalChance`. It then picks a random number between 0 and `m_totalChance` and iterates through the bot list, subtracting each bot's chance until the random number falls within a bot's range. This allows administrators to control the frequency of specific bot types appearing in the world.

### Battle Bot Auto-Join Logic
If `m_confBattleBotAutoJoin` is enabled, `Update` scans all battleground queues every 10 seconds. For each bracket with real players queued, it calculates how many bots are needed to reach `BattleGround::GetMinPlayersPerTeam`. It then spawns `BattleBotAI` instances for both Alliance and Horde to fill the deficit. These bots are spawned on "GM Island" (coordinates hardcoded in `AddBattleBot`) before being moved into the battleground.

### Chat Command Safety Checks
The `PartyBotAddRequirementCheck` function enforces strict rules to prevent abuse or crashes:
*   Bots cannot be added while the player is flying, in a battleground, or dead/in combat (unless GM/skip-checks enabled).
*   Groups cannot exceed the configured max bot limit.
*   Instances cannot exceed their max player capacity.
*   Cloning enemies is prohibited.

## Member Reference

**PlayerBotMgr**
Constructor. Initializes member variables including configuration defaults (min/max bots, refresh intervals) and timers. Sets up the singleton instance.

**~PlayerBotMgr**
Destructor. Currently empty; cleanup is handled by `DeleteAll` or server shutdown.

**LoadConfig**
Reads configuration values from the server config file (e.g., `RandomBot.Enable`, `PlayerBot.Debug`). If `FORCE_LOGOUT_DELAY` is disabled, it clears temporary bots immediately. Called by `Load` and `World::LoadConfigSettings`.

**Load**
Initializes the bot system. It calls `DeleteAll` to clear existing state, loads config, queries the `account` table for the max ID, and then iterates through the `playerbot` table to create `PlayerBotEntry` objects for each persistent bot. It creates the appropriate AI object (`PlayerBotAI`, `AiBotAI`, etc.) for each entry. Finally, it adjusts min/max bot counts based on the total loaded bots and spawns initial random bots if enabled.

**DeleteAll**
Logs out all online bots, clears the `m_bots` and `m_tempBots` containers, and resets statistics. Called during server shutdown (`World::Shutdown`).

**OnBotLogin**
Updates a bot's state to `PB_STATE_ONLINE` and logs the event if debug mode is enabled. Called by `Update` when a bot successfully loads.

**OnBotLogout**
Updates a bot's state to `PB_STATE_OFFLINE` and logs the event. Called by `DeleteAll` and `DeleteBot`.

**OnPlayerInWorld**
Called when a player enters the world. If the player is a bot (checked via `WorldSession::GetBot`), it attaches the pre-loaded `PlayerBotAI` to the player object and triggers `OnPlayerLogin` on the AI.

**Update**
The main tick function called by `World::Update`. It performs three key tasks:
1.  **Temp Bot Cleanup:** Decrements timers for temporary bots and removes expired ones.
2.  **Bot Lifecycle Management:** Processes bots in `LOADING` state. If their session is ready, it moves them to `ONLINE`. If a bot requests removal (`requestRemoval`), it removes it from groups and logs it out.
3.  **Auto-Botting:** If `m_confBattleBotAutoJoin` is on, it fills battleground queues with battle bots. If `m_confEnableRandomBots` is on, it calls `AddOrRemoveBot` to maintain the desired population count.

**AddOrRemoveBot**
Decides whether to add or remove a random bot based on the current online count versus a randomly selected target within the min/max range. Calls `AddRandomBot` or `DeleteRandomBot`.

**AddBot**
Overload accepting a `PlayerBotAI*`. Creates a new `PlayerBotEntry` for a custom AI bot, assigns it a generated account ID and GUID, marks it as `customBot`, and inserts it into `m_bots`. Then calls the overloaded `AddBot(uint32...)` to start the login process.

**AddBot#2**
Overload accepting a `uint32 playerGUID`. Starts the login process for a bot. It finds or creates the `PlayerBotEntry`, creates a new `WorldSession` for the bot's account ID, sets the bot flag on the session, and adds the session to the world. It marks the bot as `LOADING`.

**AddRandomBot**
Selects an offline bot from `m_bots` using weighted random selection based on the `chance` field and calls `AddBot` to log it in.

**AddTempBot**
Adds an account ID to the `m_tempBots` map with a specified lifetime in milliseconds. Used for chat bots or short-lived entities.

**RefreshTempBot**
Extends the lifetime of a temporary bot if it exists in `m_tempBots`, ensuring it doesn't drop below 1 second.

**DeleteBot#2**
Overload accepting an iterator. Handles the removal of a bot from the system. It updates statistics, calls `OnBotLogout`, and returns true. Note: It does *not* erase the entry from `m_bots` itself; the caller (usually `Update`) is responsible for erasing the iterator if necessary, except in specific error paths within `Update`. Called by `PlayerBotAI::UpdateAI`.

**DeleteBot**
Overload accepting a `uint32 playerGUID`. Finds the bot by GUID and calls the iterator-based `DeleteBot`.

**DeleteRandomBot**
Selects a random online bot (excluding custom and chat bots) and logs it out by calling `OnBotLogout` and updating stats.

**SelectRandomRaceForClass**
Static helper function. Given a class and team (Alliance/Horde), it returns a valid race ID that can play that class on that team. Used by `AddBattleBot` and party bot commands.

**AddBattleBot**
Spawns a battle bot for a specific battleground queue. It selects a random class/race, creates a `BattleBotAI` with hardcoded spawn coordinates (GM Island), and calls `AddBot`. It sends world text notifications to players in the queue.

**DeleteBattleBots**
Iterates through all bots and marks any with `BattleBotAI` for removal (`requestRemoval = true`). Disables auto-join.

**ForceAccountConnection**
Checks if a `WorldSession` corresponds to a bot. If so, it allows the connection if the bot is not offline. Also checks if the account is in `m_tempBots`. Called by `WorldSession::Update` to bypass normal login restrictions for bots.

**IsPermanentBot**
Returns true if the given GUID exists in `m_bots`.

**IsChatBot**
Returns true if the given GUID exists in `m_bots` and is flagged as `isChatBot`. Called by `WorldSession::CanProcessPackets` to potentially restrict chat bot actions.

**AddAllBots**
Logs in all offline bots that are not chat bots.

**HandleBotReloadCommand**
Chat command handler. Calls `PlayerBotMgr::Load()` to reload bots from the database.

**HandleBotAddAiCommand**
Chat command handler. Spawns a specific AI bot (e.g., warrior, mage) at Northshire Abbey using `AiBotAI`.

**HandleBotAddRandomCommand**
Chat command handler. Adds a specified number of random bots from the database.

**HandleBotStopCommand**
Chat command handler. Calls `DeleteAll()` to unload all bots.

**HandleBotAddAllCommand**
Chat command handler. Calls `AddAllBots()` to log in all configured bots.

**HandleBotAddCommand**
Chat command handler. Loads a specific player character as a bot by name.

**HandleBotDeleteCommand**
Chat command handler. Removes a specific bot by name.

**HandleBotInfoCommand**
Chat command handler. Displays statistics about the bot system (online count, loading count, etc.).

**HandleBotStartCommand**
Chat command handler. Enables the random bot system by calling `Start()`.

**PartyBotAddRequirementCheck**
Helper function for party bot commands. Validates that the player can add bots (not flying, not in BG, group not full, etc.).

**HandlePartyBotAddCommand**
Chat command handler. Spawns a new party bot with a specified role/class near the player. Uses `PartyBotAI`.

**HandlePartyBotCloneCommand**
Chat command handler. Spawns a party bot that clones the selected player's race/class.

**HandlePartyBotLoadCommand**
Chat command handler. Loads an existing offline character as a party bot.

**HandlePartyBotSetRoleCommand**
Chat command handler. Changes the role (Tank, DPS, Healer) of a selected party bot and refreshes its spell data.

**HandlePartyBotAttackStartCommand**
Chat command handler. Orders all party bots in the group to attack a selected target.

**StopPartyBotAttackHelper**
Helper function. Stops a bot's attack, interrupts spells, stops movement, and clears chase motion.

**HandlePartyBotAttackStopCommand**
Chat command handler. Orders party bots to stop attacking a specific target.

**HandlePartyBotAoECommand**
Chat command handler. Forces party bots to cast an Area of Effect spell on a target.

**HandlePartyBotStartCastingCommand**
Chat command handler. Allows party bots to cast spells again.

**HandlePartyBotStopCastingCommand**
Chat command handler. Prevents party bots from casting spells.

**HandlePartyBotToggleCastingCommand**
Internal handler for start/stop casting commands. Updates the `m_preventCasting` flag on `PartyBotAI`.

**HandlePartyBotControlMarkCommand**
Chat command handler. Assigns a raid target icon (mark) for party bots to crowd control.

**HandlePartyBotFocusMarkCommand**
Chat command handler. Assigns a raid target icon for party bots to focus fire.

**HandlePartyBotClearMarksCommand**
Chat command handler. Clears all CC and focus marks for party bots.

**HandlePartyBotComeToMeHelper**
Helper function. Moves a bot to the player's position, interrupting current actions.

**HandlePartyBotComeToMeCommand**
Chat command handler. Orders party bots to move to the player's location.

**HandlePartyBotUseGObjectHelper**
Helper function. Makes a bot use a nearby GameObject.

**HandlePartyBotUseGObjectCommand**
Chat command handler. Orders party bots to use a selected GameObject.

**HandlePartyBotPauseApplyHelper**
Helper function. Pauses a bot's AI update timer and stops its movement.

**HandlePartyBotPauseHelper**
Internal handler for pause/unpause commands. Parses arguments and applies pause state to bots.

**HandlePartyBotPauseCommand**
Chat command handler. Pauses party bots for a specified duration.

**HandlePartyBotUnpauseCommand**
Chat command handler. Unpauses party bots.

**HandlePartyBotPullCommand**
Chat command handler. Orders tanks to pull a target while pausing DPS bots.

**HandlePartyBotUnequipCommand**
Chat command handler. Removes an item from a party bot's inventory.

**HandlePartyBotRemoveCommand**
Chat command handler. Marks a party bot for removal.

**HandleBattleBotAddAlteracCommand**
Chat command handler. Adds a battle bot to the Alterac Valley queue.

**HandleBattleBotAddArathiCommand**
Chat command handler. Adds a battle bot to the Arathi Basin queue.

**HandleBattleBotAddWarsongCommand**
Chat command handler. Adds a battle bot to the Warsong Gulch queue.

**HandleBattleBotAddCommand**
Internal handler for adding battle bots. Parses faction, level, and temporariness, then calls `AddBattleBot`.

**HandleBattleBotRemoveCommand**
Chat command handler. Marks a selected battle bot for removal.

**HandleBattleBotRemoveAllCommand**
Chat command handler. Calls `DeleteBattleBots()` to remove all battle bots.

**ShowBattleBotPathHelper**
Helper function. Summons visual waypoint creatures to display a battle bot's path.

**HandleBattleBotShowPathCommand**
Chat command handler. Visualizes the path of a selected battle bot.

**HandleBattleBotShowAllPathsCommand**
Chat command handler. Visualizes all predefined paths for the current battleground.

---

<!-- machine-true, projected from graph.json -->

## Map — ChatHandler.PlayerBotMgr

*Source:* PlayerBotMgr.cpp, PlayerBotMgr.h, Chat.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| PlayerBotMgr | ctor | — | — | — |
| ~PlayerBotMgr | dtor | — | — | — |
| LoadConfig | method | Config/GetBoolDefault, Config/GetIntDefault, World/getConfig | World/LoadConfigSettings | — |
| Load | method | Database/PQuery, Field/GetCppString, Field/GetFloat, Field/GetUInt32, Field/GetUInt8, Log.Main/Out, ObjectGuid/ObjectGuid#5, ObjectMgr/GetPlayerNameByGUID, PlayerBotAI/CreatePlayerBotAI, PlayerBotAI/OnBotEntryLoad, PlayerBotMgr/GenBotAccountId, QueryResult/Fetch, QueryResult/NextRow | World/SetInitialWorldSettings | account, playerbot |
| DeleteAll | method | Log.Main/Out | World/Shutdown | — |
| OnBotLogin | method | Log.Main/Out | — | — |
| OnBotLogout | method | Log.Main/Out | — | — |
| OnPlayerInWorld | method | Player.Main/GetSession, Player.Main/SetAI, PlayerAI/SetPlayer, PlayerBotAI/OnPlayerLogin, WorldSession.Main/GetBot | Player.Main/AddToWorld | — |
| Update | method | BattleGround/GetMinLevel, BattleGround/GetMinPlayersPerTeam, BattleGroundMgr/BgTemplateId, BattleGroundMgr/GetBattleGroundTemplate, Errors/PrintStacktraceAndThrow, Log.Main/Out, ObjectAccessor/FindPlayer, Player.Main/GetBattleGroundBracketIdFromLevel, Player.Main/IsBot, Player.Main/RemoveFromGroup, PlayerBotAI/OnSessionLoaded#2, shared_Util/urand, World/FindSession, World/GetGameTime, WorldSession.Main/LogoutPlayer | World/Update | — |
| AddOrRemoveBot | method | shared_Util/urand | — | — |
| AddBot | method | ObjectMgr/GeneratePlayerLowGuid, PlayerBotMgr/GenBotAccountId | — | — |
| AddBot#2 | method | AccountMgr/GetSecurity, Log.Main/Out, ObjectGuid/ObjectGuid#5, ObjectMgr/GetPlayerAccountIdByGUID, PlayerBotAI/PlayerBotAI, World/AddSession, World/FindSession, WorldSession.Main/SetBot, WorldSession.Main/WorldSession | — | — |
| AddRandomBot | method | shared_Util/urand | — | — |
| AddTempBot | method | — | — | — |
| RefreshTempBot | method | — | — | — |
| DeleteBot#2 | method | — | PlayerBotAI/UpdateAI | — |
| DeleteBot | method | — | — | — |
| DeleteRandomBot | method | shared_Util/urand | — | — |
| SelectRandomRaceForClass | function | ObjectMgr/GetPlayerInfo | — | — |
| AddBattleBot | method | BattleBotAI.Main/BattleBotAI, Log.Main/Out, MapManager/GetContinentInstanceId, World/SendWorldTextToBGAndQueue | — | — |
| DeleteBattleBots | method | — | — | — |
| ForceAccountConnection | method | WorldSession.Main/GetAccountId, WorldSession.Main/GetBot | WorldSession.Main/Update | — |
| IsPermanentBot | method | — | — | — |
| IsChatBot | method | — | WorldSession.Main/CanProcessPackets | — |
| AddAllBots | method | — | — | — |
| HandleBotReloadCommand | method | ChatHandler.Chat/SendSysMessage | — | — |
| HandleBotAddAiCommand | method | AiBotAI.Main/AiBotAI, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SetSentErrorMessage, MapManager/GetContinentInstanceId | — | — |
| HandleBotAddRandomCommand | method | ChatHandler.Chat/PSendSysMessage | — | — |
| HandleBotStopCommand | method | ChatHandler.Chat/SendSysMessage | — | — |
| HandleBotAddAllCommand | method | ChatHandler.Chat/SendSysMessage | — | — |
| HandleBotAddCommand | method | ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SetSentErrorMessage, ObjectGuid/GetCounter, ObjectMgr/GetPlayerGuidByName | — | — |
| HandleBotDeleteCommand | method | ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SetSentErrorMessage, ObjectGuid/GetCounter, ObjectMgr/GetPlayerGuidByName | — | — |
| HandleBotInfoCommand | method | ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, PlayerBotMgr/GetStats, World/GetActiveSessionCount | — | — |
| HandleBotStartCommand | method | PlayerBotMgr/Start | — | — |
| PartyBotAddRequirementCheck | method | ChatHandler.Chat/GetSession, ChatHandler.Chat/SendSysMessage, Group/GetMembersCount, Group/IsFull, LinkedListHead/getSize, Map.Main/GetMapEntry, Map.Main/GetPlayers, Map.Main/IsDungeon, Player.Main/GetGroup#2, Player.Main/GetTeam, Player.Main/InBattleGround, Unit.Main/GetLevel, Unit.Main/IsDead, Unit.Main/IsInCombat, Unit.Main/IsTaxiFlying, World/getConfig, World/getConfig#4, WorldObject.Object/GetMap, WorldSession.Main/GetSecurity | — | — |
| HandlePartyBotAddCommand | method | ChatHandler.Chat/ExtractArg, ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/GetSession, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SetSentErrorMessage, CombatBotBaseAI/IsMeleeDamageClass, Map.Main/GetInstanceId, PartyBotAI/PartyBotAI, Player.Main/GetTeam, shared_Util/frand, Unit.Main/GetLevel, World/getConfig, WorldObject.Object/GetMap, WorldObject.Object/GetMapId, WorldObject.Object/GetNearPoint, WorldObject.Object/GetOrientation, WorldSession.Main/GetPlayer, WorldSession.Main/GetSecurity | — | — |
| HandlePartyBotCloneCommand | method | ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Map.Main/GetInstanceId, PartyBotAI/PartyBotAI, shared_Util/frand, Unit.Main/GetClass, Unit.Main/GetLevel, Unit.Main/GetRace, WorldObject.Object/GetMap, WorldObject.Object/GetMapId, WorldObject.Object/GetNearPoint, WorldObject.Object/GetOrientation, WorldSession.Main/GetPlayer | — | — |
| HandlePartyBotLoadCommand | method | ChatHandler.Chat/ExtractPlayerNameFromLink, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Map.Main/GetInstanceId, ObjectAccessor/FindPlayerNotInWorld, ObjectGuid/GetCounter, ObjectGuid/ObjectGuid#5, ObjectGuid/operator!, ObjectMgr/GetPlayerGuidByName, PartyBotAI/PartyBotAI#2, shared_Util/frand, WorldObject.Object/GetMap, WorldObject.Object/GetMapId, WorldObject.Object/GetNearPoint, WorldObject.Object/GetOrientation, WorldSession.Main/GetPlayer | — | — |
| HandlePartyBotSetRoleCommand | method | ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, CombatBotBaseAI/IsMeleeDamageClass, CombatBotBaseAI/PopulateSpellData, CombatBotBaseAI/ResetSpellData, Player.Main/AI, Player.Main/GetName, Unit.Main/GetClass | — | — |
| HandlePartyBotAttackStartCommand | method | ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/GetSession, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Group/GetFirstMember, GroupReference/next, PartyBotAI/AttackStart, Player.Main/AI, Player.Main/GetGroup, WorldObject.Object/GetName, WorldObject.Object/IsValidAttackTarget, WorldSession.Main/GetPlayer | — | — |
| StopPartyBotAttackHelper | function | Creature.MotionMaster/GetCurrentMovementGeneratorType, MotionMaster/Clear, ShortTimeTracker/GetExpiry, ShortTimeTracker/Reset, SpellCaster/InterruptNonMeleeSpells, Unit.Main/AttackStop, Unit.Main/GetMotionMaster, Unit.Main/IsStopped, Unit.Main/StopMoving | — | — |
| HandlePartyBotAttackStopCommand | method | ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/GetSession, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Group/GetFirstMember, GroupReference/next, Player.Main/AI, Player.Main/GetGroup, Unit.Main/GetVictim, WorldObject.Object/GetName, WorldSession.Main/GetPlayer | — | — |
| HandlePartyBotAoECommand | method | ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/GetSession, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Group/GetFirstMember, GroupReference/next, Player.Main/AI, Player.Main/GetGroup, SpellCaster/CastSpell, SpellCaster/GetCurrentSpell, SpellCaster/InterruptSpell, SpellEntry/IsAreaOfEffectSpell, SpellEntry/IsPositiveSpell#4, SpellEntry/IsTargetInRange, WorldObject.Object/GetName, WorldObject.Object/IsValidAttackTarget, WorldSession.Main/GetPlayer | — | — |
| HandlePartyBotStartCastingCommand | method | — | — | — |
| HandlePartyBotStopCastingCommand | method | — | — | — |
| HandlePartyBotToggleCastingCommand | method | ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/GetSession, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SetSentErrorMessage, Group/GetFirstMember, GroupReference/next, Player.Main/AI, Player.Main/GetGroup, Player.Main/GetName, SpellCaster/InterruptNonMeleeSpells, WorldSession.Main/GetPlayer | — | — |
| HandlePartyBotControlMarkCommand | method | ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/GetSession, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SetSentErrorMessage, Group/GetFirstMember, GroupReference/next, Player.Main/AI, Player.Main/GetGroup, Player.Main/GetName, WorldSession.Main/GetPlayer | — | — |
| HandlePartyBotFocusMarkCommand | method | ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/GetSession, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SetSentErrorMessage, Group/GetFirstMember, GroupReference/next, Player.Main/AI, Player.Main/GetGroup, Player.Main/GetName, WorldSession.Main/GetPlayer | — | — |
| HandlePartyBotClearMarksCommand | method | ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/GetSession, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SetSentErrorMessage, Group/GetFirstMember, GroupReference/next, Player.Main/AI, Player.Main/GetGroup, Player.Main/GetName, WorldSession.Main/GetPlayer | — | — |
| HandlePartyBotComeToMeHelper | function | Player.Main/AI, SpellCaster/InterruptSpellsWithInterruptFlags, Unit.Main/GetStandState, Unit.Main/GetVictim, Unit.Main/HasUnitState, Unit.Main/IsAlive, Unit.Main/MonsterMove, Unit.Main/SetStandState, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/IsInMap | — | — |
| HandlePartyBotComeToMeCommand | method | ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/GetSession, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SetSentErrorMessage, Group/GetFirstMember, GroupReference/next, Player.Main/GetGroup, Player.Main/GetName, WorldSession.Main/GetPlayer | — | — |
| HandlePartyBotUseGObjectHelper | function | GameObject/Use, Player.Main/AI, WorldObject.Object/IsWithinDist | — | — |
| HandlePartyBotUseGObjectCommand | method | ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/GetSession, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, ChatHandler.ObjectCommands/getSelectedGameObject, Group/GetFirstMember, GroupReference/next, Player.Main/GetGroup, Player.Main/GetName, WorldSession.Main/GetPlayer | — | — |
| HandlePartyBotPauseApplyHelper | function | Creature.MotionMaster/MoveIdle, Player.Main/AI, ShortTimeTracker/Reset, Unit.Main/GetMotionMaster, Unit.Main/StopMoving | — | — |
| HandlePartyBotPauseHelper | method | ChatHandler.Chat/ExtractArg, ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/GetSession, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Group/GetFirstMember, GroupReference/next, Player.Main/GetGroup, Player.Main/GetName, WorldSession.Main/GetPlayer | — | — |
| HandlePartyBotPauseCommand | method | — | — | — |
| HandlePartyBotUnpauseCommand | method | — | — | — |
| HandlePartyBotPullCommand | method | ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/GetSession, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Group/GetFirstMember, GroupReference/next, PartyBotAI/AttackStart, Player.Main/AI, Player.Main/GetGroup, WorldObject.Object/GetName, WorldObject.Object/IsValidAttackTarget, WorldSession.Main/GetPlayer | — | — |
| HandlePartyBotUnequipCommand | method | ChatHandler.Chat/ExtractKeyFromLink#2, ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/AI, Player.Main/DestroyItemCount#2, Player.Main/GetItemCount | — | — |
| HandlePartyBotRemoveCommand | method | ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/AI | — | — |
| HandleBattleBotAddAlteracCommand | method | — | — | — |
| HandleBattleBotAddArathiCommand | method | — | — | — |
| HandleBattleBotAddWarsongCommand | method | — | — | — |
| HandleBattleBotAddCommand | method | ChatHandler.Chat/ExtractArg, ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SetSentErrorMessage, World/getConfig#4 | — | — |
| HandleBattleBotRemoveCommand | method | ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/AI | — | — |
| HandleBattleBotRemoveAllCommand | method | ChatHandler.Chat/SendSysMessage | — | — |
| ShowBattleBotPathHelper | function | SpellCaster/CastSpell#2, WorldObject.Object/SetUInt32Value, WorldObject.Object/SummonCreature | — | — |
| HandleBattleBotShowPathCommand | method | ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/AI, WorldObject.Object/GetMap | — | — |
| HandleBattleBotShowAllPathsCommand | method | BattleGround/GetTypeID, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, Player.Main/GetBattleGround, WorldObject.Object/GetMap, WorldSession.Main/GetPlayer | — | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `account`: id int(11) unsigned PK, username varchar(32), gmlevel tinyint(3) unsigned, sessionkey longtext?, v longtext?, s longtext?, token_key varchar(100), email text?, joindate timestamp, last_ip varchar(30), failed_logins int(11) unsigned, locked tinyint(3) unsigned, lock_country varchar(2), last_login timestamp, online tinyint(4), expansion tinyint(3) unsigned, mutetime bigint(40), locale tinyint(3) unsigned, os varchar(4), platform varchar(4), current_realm tinyint(3) unsigned, flags int(10) unsigned, security varchar(255)?, email_verif tinyint(1), geolock_pin int(11)?
- `playerbot`: char_guid bigint(20) unsigned PK, chance int(10) unsigned, comment varchar(255)?, ai varchar(50)?, name varchar(12), race tinyint(3) unsigned, class tinyint(3) unsigned, level tinyint(3) unsigned, map smallint(5) unsigned, position_x float, position_y float, position_z float

*`?` = nullable, `PK` = primary key column.*


---

<!-- verify: failed-members | invented: Auto-Botting:, Bot Lifecycle Management:, Temp Bot Cleanup: -->

---

<!-- verify: boundary-bleed | foreign: ChatHandler, Enable, load -->
