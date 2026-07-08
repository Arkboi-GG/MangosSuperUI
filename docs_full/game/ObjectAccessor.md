# ObjectAccessor

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ObjectAccessor

**Purpose & Responsibilities**

`ObjectAccessor` is the global singleton responsible for managing the lifecycle and lookup of high-level world entities—specifically `Player`, `MasterPlayer`, `Corpse`, and `ShipTransport` objects—in the WoWVMaNGOS server. It acts as the central registry for these objects, providing thread-safe access methods to find them by GUID or name, and handling their insertion and removal from memory maps.

Key responsibilities include:
1.  **Global Lookup:** Providing static methods to retrieve `Player` and `MasterPlayer` instances by GUID or normalized name, distinguishing between players currently logged in (`IsInWorld`) and those merely loaded in memory.
2.  **Corpse Management:** Maintaining a dedicated map (`i_player2corpse`) linking player GUIDs to their active corpses. It handles the complex logic of adding corpses to grids, removing them upon expiration or conversion, and ensuring safe deletion relative to map grid states.
3.  **Thread Safety:** Utilizing `HashMapHolder<T>` templates with `std::shared_timed_mutex` for general object lookups and specific mutexes (`i_corpseGuard`) for corpse-specific operations to prevent race conditions during concurrent access.
4.  **Session Integration:** Coordinating with `WorldSession` to kick players and save player data to the database.

## Member-by-Member Behavior

### Initialization and Destruction

*   **`ObjectAccessor` (ctor)**: Initializes the singleton. No complex logic; relies on static member initialization for maps.
*   **`~ObjectAccessor` (dtor)**: Cleans up remaining corpses in `i_player2corpse`. It iterates through the map, skipping entries where the key is a corpse GUID (likely a safety check or legacy artifact), and calls `RemoveFromWorld()` and `delete` on the corpse objects. This ensures no memory leaks occur when the server shuts down.

### Player and MasterPlayer Lookup

The accessor distinguishes between `Player` (standard human/AI players) and `MasterPlayer` (likely a bot or special entity type, indicated by the `Chat/MasterPlayer.h` include).

*   **`FindPlayer(ObjectGuid)`**: Retrieves a `Player` by GUID. It first checks if the player exists in memory via `FindPlayerNotInWorld`. If found, it verifies the player is currently in the world (`IsInWorld`). Returns `nullptr` if the player is offline or not found.
*   **`FindPlayerNotInWorld(ObjectGuid)`**: Retrieves a `Player` by GUID regardless of online status. Uses `HashMapHolder<Player>::Find`.
*   **`FindPlayerByName(char const*)`**: Retrieves a `Player` by name. Normalizes the name, looks it up in `playerNameToPlayerPointer`, and verifies `IsInWorld`.
*   **`FindPlayerByNameNotInWorld(char const*)`**: Retrieves a `Player` by name regardless of online status. Normalizes the name and searches `playerNameToPlayerPointer`.
*   **`FindMasterPlayer(ObjectGuid)` / `FindMasterPlayer(char const*)`**: Analogous to the `Player` lookup methods but operate on `HashMapHolder<MasterPlayer>` and `playerNameToMasterPlayerPointer`.
*   **`FindPlayerPointer(ObjectGuid)` / `FindPlayerPointer(char const*)`**: Polymorphic lookup. Attempts to find a `Player` first. If not found, attempts to find a `MasterPlayer`. Wraps the result in a `PlayerPointer` (smart pointer wrapper) allowing callers to treat both types uniformly.

### Unit Retrieval

*   **`GetUnit(WorldObject const&, ObjectGuid)`**: A helper to retrieve a `Unit` (creature or player) from the world.
    *   If the GUID is invalid, returns `nullptr`.
    *   If the GUID is a Player, it delegates to `FindPlayer(guid)`.
    *   If the caller object (`u`) is not in the world, returns `nullptr`.
    *   Otherwise, it retrieves the map from the caller object and uses `Map::GetAnyTypeCreature(guid)` to find the creature/unit on that specific map. This ensures spatial locality for non-player units.

### Corpse Management

Corpse management is more complex due to grid dependencies and database synchronization.

*   **`AddCorpse(Corpse*)`**: Adds a corpse to the `i_player2corpse` map keyed by owner GUID. It asserts that the corpse is not already present and is not of type `CORPSE_BONES`. It calculates the grid cell ID from the corpse's position and registers the corpse with `ObjectMgr::AddCorpseCellData` for grid-based queries.
*   **`RemoveCorpse(Corpse*)`**: Removes a corpse from `i_player2corpse`. It calculates the cell ID, calls `ObjectMgr::DeleteCorpseCellData` to unregister it from grid queries, and calls `Corpse::RemoveFromWorld()`. Finally, it erases the entry from the map.
*   **`GetCorpseForPlayerGUID(ObjectGuid)`**: Retrieves the active corpse for a specific player GUID from `i_player2corpse`. Asserts the GUID is a player and the corpse type is not `CORPSE_BONES`.
*   **`GetCorpseInMap(ObjectGuid, uint32)`**: Finds a corpse by GUID using `HashMapHolder<Corpse>::Find` and verifies it resides on the specified `mapid`.
*   **`AddCorpsesToGrid(GridPair, GridType&, Map const*)`**: Iterates through `i_player2corpse` to add relevant corpses to a specific grid. It filters by grid pair and, if the map is instanceable, verifies the corpse's instance ID matches the map's instance ID. This ensures corpses are only added to the correct grid context.
*   **`ConvertCorpseForPlayer(ObjectGuid, Player const*)`**: Handles the transition of a corpse when a player dies or is resurrected.
    *   Retrieves the corpse via `GetCorpseForPlayerGUID`.
    *   Calls `RemoveCorpse` to clean up the map and grid data.
    *   Finds the map where the corpse resides.
    *   **Critical Logic:** If the map is loaded, it adds the corpse to the map's removal list (`Map::AddCorpseToRemove`) for delayed processing. This prevents crashes caused by destroying grid references while the map is performing visibility updates. If the map is not loaded, it deletes the corpse from the database (`Corpse::DeleteFromDB`) and frees memory immediately.
*   **`RemoveOldCorpses()`**: Iterates through `i_player2corpse` and calls `ConvertCorpseForPlayer` for any corpse that has expired (`Corpse::IsExpired`). This is typically called periodically by the world update loop.

### Object Lifecycle Registration

These methods manage the insertion and removal of objects from the global hash maps.

*   **`AddObject(Player*)`**: Inserts the player into `HashMapHolder<Player>` and adds an entry to `playerNameToPlayerPointer` keyed by the player's name.
*   **`RemoveObject(Player*)`**: Removes the player from `HashMapHolder<Player>` and erases the name entry from `playerNameToPlayerPointer`.
*   **`AddObject(MasterPlayer*)` / `RemoveObject(MasterPlayer*)`**: Analogous to Player registration, operating on `HashMapHolder<MasterPlayer>` and `playerNameToMasterPlayerPointer`.
*   **`AddObject(Corpse*)` / `RemoveObject(Corpse*)`**: Delegates to `HashMapHolder<Corpse>::Insert/Remove`.
*   **`AddObject(ShipTransport*)` / `RemoveObject(ShipTransport*)`**: Delegates to `HashMapHolder<ShipTransport>::Insert/Remove`.

### Administrative Operations

*   **`SaveAllPlayers()`**: Acquires a read lock on the player map, iterates through all players, and calls `Player::SaveToDB()` on each. Used for server shutdown or manual save commands.
*   **`KickPlayer(ObjectGuid)`**: Finds the player by GUID. If found, retrieves their `WorldSession` and calls `KickPlayer()` and `LogoutPlayer(false)` to forcibly disconnect them.

## Cross-Unit Boundaries

*   **`Map`**: `GetUnit` calls `Map::GetAnyTypeCreature` to resolve non-player units. `ConvertCorpseForPlayer` calls `Map::AddCorpseToRemove` to defer corpse deletion. `AddCorpsesToGrid` interacts with `Map` to verify instance IDs.
*   **`ObjectMgr`**: `AddCorpse` and `RemoveCorpse` call `ObjectMgr::AddCorpseCellData` and `ObjectMgr::DeleteCorpseCellData` respectively to maintain spatial indexing for corpses. `FindPlayerByNameNotInWorld` calls `ObjectMgr::normalizePlayerName`.
*   **`Player`**: `SaveAllPlayers` calls `Player::SaveToDB`. `AddObject`/`RemoveObject` call `Player::GetName`. `KickPlayer` calls `Player::GetSession`.
*   **`Corpse`**: `AddCorpse`, `RemoveCorpse`, `ConvertCorpseForPlayer`, and `RemoveOldCorpses` interact extensively with `Corpse` methods like `GetType`, `GetOwnerGuid`, `GetPositionX/Y`, `GetMapId`, `GetInstanceId`, `GetGrid`, `RemoveFromWorld`, `DeleteFromDB`, and `IsExpired`.
*   **`WorldSession`**: `KickPlayer` calls `WorldSession::KickPlayer` and `WorldSession::LogoutPlayer`.
*   **`MapManager`**: `ConvertCorpseForPlayer` calls `MapManager::FindMap` to locate the map containing a corpse.
*   **`GridDefines`**: `AddCorpse` and `RemoveCorpse` use `MaNGOS::ComputeCellPair` to translate coordinates into grid cell IDs.

## Data Model

This unit does not directly execute SQL queries against database tables. It interacts with the database indirectly through:
1.  **`Player::SaveToDB`**: Called by `SaveAllPlayers`.
2.  **`Corpse::DeleteFromDB`**: Called by `ConvertCorpseForPlayer`.
3.  **`ObjectMgr::AddCorpseCellData` / `DeleteCorpseCellData`**: These likely interact with a temporary or persistent structure for grid data, but the SQL specifics are encapsulated within `ObjectMgr`.

Therefore, no direct table schemas are managed by `ObjectAccessor`.

## Notable Implementation Details

1.  **Deferred Corpse Deletion**: In `ConvertCorpseForPlayer`, the code explicitly checks if the map is loaded. If it is, the corpse is added to a removal list (`Map::AddCorpseToRemove`) rather than being deleted immediately. The comment explains this is to avoid crashing due to destroyed grid references during visibility updates. This is a critical concurrency/safety mechanism.
2.  **Name Normalization**: Player lookups by name always normalize the input string using `ObjectMgr::normalizePlayerName` before searching the `playerNameToPlayerPointer` map. This ensures case-insensitive and accent-insensitive matching (depending on the normalization implementation).
3.  **Thread Safety Granularity**:
    *   `HashMapHolder<T>` uses `std::shared_timed_mutex` allowing multiple readers but exclusive writers.
    *   `i_player2corpse` is protected by `i_corpseGuard` (a `std::mutex` wrapped in `MaNGOS::GeneralLock`).
    *   `SaveAllPlayers` acquires a read lock on the player map before iterating, preventing modification during the save operation.
4.  **Polymorphic Player Pointer**: `FindPlayerPointer` returns a `PlayerPointer` which wraps either a `Player` or `MasterPlayer`. This allows systems like chat or social features to handle both types uniformly without knowing the specific subclass.
5.  **Singleton Pattern**: `ObjectAccessor` is instantiated as a singleton using `INSTANTIATE_SINGLETON_2` with a class-level lockable policy, ensuring global unique access.

## Member Reference

*   **`ObjectAccessor`**: Constructor for the singleton.
*   **`~ObjectAccessor`**: Destructor; cleans up remaining corpses in `i_player2corpse` by calling `RemoveFromWorld` and deleting them.
*   **`GetUnit`**: Retrieves a `Unit` by GUID. If player, uses `FindPlayer`; otherwise, uses `Map::GetAnyTypeCreature` on the caller's map.
*   **`Insert`**: Static method in `HashMapHolder<T>`; inserts an object into the map with write lock.
*   **`Remove`**: Static method in `HashMapHolder<T>`; removes an object from the map with write lock.
*   **`Find`**: Static method in `HashMapHolder<T>`; finds an object by GUID with read lock.
*   **`GetCorpseInMap`**: Finds a corpse by GUID using `HashMapHolder<Corpse>::Find` and verifies it matches the given `mapid`.
*   **`GetContainer`**: Static method in `HashMapHolder<T>`; returns the underlying map container.
*   **`GetLock`**: Static method in `HashMapHolder<T>`; returns the mutex lock.
*   **`FindPlayerNotInWorld`**: Finds a `Player` by GUID using `HashMapHolder<Player>::Find`, ignoring online status.
*   **`HashMapHolder<T>`**: Template class providing thread-safe static map operations for objects.
*   **`FindPlayer`**: Finds a `Player` by GUID, verifying they are `IsInWorld`.
*   **`ObjectAccessor#2`**: Declaration of the singleton instance.
*   **`FindPlayerByNameNotInWorld`**: Finds a `Player` by normalized name using `playerNameToPlayerPointer`, ignoring online status.
*   **`operator=`**: Deleted assignment operator for the singleton.
*   **`FindPlayerByName`**: Finds a `Player` by normalized name, verifying they are `IsInWorld`.
*   **`FindMasterPlayer#2`**: Finds a `MasterPlayer` by normalized name using `playerNameToMasterPlayerPointer`.
*   **`GetPlayers`**: Returns the underlying `HashMapHolder<Player>::MapType`.
*   **`FindMasterPlayer`**: Finds a `MasterPlayer` by GUID using `HashMapHolder<MasterPlayer>::Find`.
*   **`GetMasterPlayers`**: Returns the underlying `HashMapHolder<MasterPlayer>::MapType`.
*   **`FindPlayerPointer`**: Polymorphic lookup returning a `PlayerPointer` wrapping either a `Player` or `MasterPlayer` by GUID.
*   **`AddObject`**: Overloads for `Player`, `MasterPlayer`, `Corpse`, and `ShipTransport`. For Players/MasterPlayers, updates both the hash map and the name-to-pointer map. For Corpses/Transports, delegates to `HashMapHolder`.
*   **`AddObject#4`**: Alias for `AddObject(ShipTransport*)`.
*   **`FindPlayerPointer#2`**: Polymorphic lookup returning a `PlayerPointer` by name.
*   **`RemoveObject`**: Overloads for `Player`, `MasterPlayer`, `Corpse`, and `ShipTransport`. For Players/MasterPlayers, removes from both hash map and name-to-pointer map. For Corpses/Transports, delegates to `HashMapHolder`.
*   **`RemoveObject#4`**: Alias for `RemoveObject(ShipTransport*)`.
*   **`SaveAllPlayers`**: Iterates all players and calls `Player::SaveToDB` under a read lock.
*   **`KickPlayer`**: Finds player by GUID, retrieves session, and calls `KickPlayer` and `LogoutPlayer`.
*   **`GetCorpseForPlayerGUID`**: Retrieves corpse from `i_player2corpse` by player GUID, asserting validity.
*   **`RemoveCorpse`**: Removes corpse from `i_player2corpse`, updates grid data via `ObjectMgr`, and calls `RemoveFromWorld`.
*   **`AddCorpse`**: Adds corpse to `i_player2corpse`, updates grid data via `ObjectMgr`.
*   **`AddCorpsesToGrid`**: Adds relevant corpses from `i_player2corpse` to a specific grid, checking instance IDs if applicable.
*   **`ConvertCorpseForPlayer`**: Converts/removes a corpse for a player. Defers deletion to `Map::AddCorpseToRemove` if map is loaded to prevent grid crashes, otherwise deletes immediately.
*   **`RemoveOldCorpses`**: Iterates `i_player2corpse` and converts expired corpses.
*   **`AddObject#3`**: Alias for `AddObject(Player*)`.
*   **`RemoveObject#3`**: Alias for `RemoveObject(Player*)`.
*   **`AddObject#2`**: Alias for `AddObject(MasterPlayer*)`.
*   **`RemoveObject#2`**: Alias for `RemoveObject(MasterPlayer*)`.

---

<!-- machine-true, projected from graph.json -->

## Map — ObjectAccessor

*Source:* ObjectAccessor.cpp, ObjectAccessor.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ObjectAccessor | ctor | — | — | — |
| ~ObjectAccessor | dtor | Corpse/RemoveFromWorld, ObjectGuid/IsCorpse | — | — |
| GetUnit | method | Map.Main/GetAnyTypeCreature, Object/IsInWorld, ObjectGuid/IsPlayer, ObjectGuid/operator!, WorldObject.Object/GetMap | ChatHandler.Chat/GetSelectedUnit, ChatHandler.ObjectCommands/HandleGameObjectDeleteCommand, DynamicObject/GetCaster, FearMovementGenerator/Update#2, FearMovementGenerator/_getPoint, FleeingMovementGenerator/_getPoint, GameObject/GetOwner, GameObject/RemoveFromWorld, Player.Main/AddComboPoints, Player.Main/ClearComboPoints, Player.Main/SetComboPoints, Spell.Main/AddUnitTarget, Spell.Main/cancel, Spell.Main/CheckAtDelay, Spell.Main/DelayedChannel, Spell.Main/DoAllEffectOnTarget#3, Spell.Main/HandleAddTargetTriggerAuras, Spell.Main/HandleDelayedSpellLaunch, Spell.Main/HandleThreatSpells, Spell.Main/HasValidUnitPresentInTargetList, Spell.Main/SendChannelStart, Spell.Main/SendChannelUpdate, Spell.Main/update, Spell.Main/UpdateOriginalCasterPointer, SpellCastTargetsInfo/Update, TemporarySummon/GetSummoner, ThreatManager/updateOnlineStatus, Totem/GetOwner, Unit.Main/GetCharm, Unit.Main/GetCharmer, Unit.Main/GetOwner, Unit.SpellAuras/GetCaster, Unit.SpellAuras/GetRealCaster, Unit.SpellAuras/GetTriggerTarget, Unit.SpellAuras/HandleAuraRetainComboPoints, WorldSession.MiscHandler/HandleSetSelectionOpcode | — |
| Insert | function | — | — | — |
| Remove | function | — | — | — |
| Find | function | — | — | — |
| GetCorpseInMap | method | WorldObject.Object/GetMapId | Map.Main/GetCorpse | — |
| GetContainer | function | — | — | — |
| GetLock | function | — | — | — |
| FindPlayerNotInWorld | method | ObjectGuid/operator! | AsyncCommandHandlers/ShowPlayerListHelper, BattleGroundMgr/BuildPvpLogDataPacket, BattleGroundMgr/Execute, BattleGroundMgr/Execute#2, BattleGroundMgr/InviteGroupToBG, BattleGroundMgr/RemoveOfflinePlayer, ChatHandler.PlayerBotMgr/HandlePartyBotLoadCommand, game_Chat_Channel/GetPlayer, game_Guild_Guild/AddMember, ObjectMgr/Callback, ObjectMgr/Callback#2, PartyBotAI/GetPartyLeader, WorldSession.CharacterHandler/HandleCharDeleteOpcode | — |
| HashMapHolder<T> | ctor | — | — | — |
| FindPlayer | method | Object/IsInWorld | ChatHandler.PlayerBotMgr/Update, Creature.Main/GetOriginalLootRecipient, game_Group_Group/UpdateLooterGuid, game_Guild_Guild/BroadcastPacket, game_Guild_Guild/BroadcastPacketToRank, game_Guild_Guild/Roster, GMTicketMgr/GetAssignedPlayer, GMTicketMgr/GetPlayer, GridNotifiers/Notify, LootMgr/GetLootTarget, LootMgr/NotifyItemRemoved, LootMgr/NotifyMoneyRemoved, LootMgr/NotifyQuestItemRemoved, Map.Main/GetPlayer, PartyBotAI/AddToPlayerGroup, PartyBotAI/OnSessionLoaded, PartyBotAI/UpdateAI, Player.Main/GetObjectByTypeMask, Spell.Effects/EffectSkinPlayerCorpse, Spell.Main/SetTargetMap, Unit.Main/GetCharmerOrOwnerPlayer, Unit.Main/GetCharmerOrOwnerPlayerOrPlayerItself, Unit.Main/GetOwnerPlayer, Unit.Main/GetOwnerPlayerOrPlayerItself, Unit.Main/GetPossessor, Unit.Main/Kill, WorldSession.CharacterHandler/HandleCharDeleteOpcode, WorldSession.CharacterHandler/HandlePlayerLogin, WorldSession.GuildHandler/HandleGuildDeclineOpcode, WorldSession.LootHandler/HandleLootMasterGiveOpcode, WorldSession.PetitionsHandler/HandleOfferPetitionOpcode, WorldSession.QuestHandler/HandleQuestConfirmAccept, WorldSession.QuestHandler/HandleQuestPushResult | — |
| ObjectAccessor#2 | decl | — | — | — |
| FindPlayerByNameNotInWorld | method | ObjectMgr/normalizePlayerName | game_Chat_Channel/GetPlayer#2 | — |
| operator= | decl | — | — | — |
| FindPlayerByName | method | Object/IsInWorld | ChatHandler.AccountCommands/HandleSpamerMute, ChatHandler.AccountCommands/HandleSpamerUnmute, ChatHandler.TicketCommands/ViewTicketByIdOrName, WorldSession.GuildHandler/HandleGuildInviteOpcode | — |
| FindMasterPlayer#2 | method | ObjectMgr/normalizePlayerName | game_Chat_Channel/GetPlayer#2, WorldSession.ChatHandler/HandleChatMessageOpcode | — |
| GetPlayers | method | — | ChatHandler.CharacterCommands/HandleResetAllCommand, ChatHandler.HardcodedEvents/UpdateWorldState, ChatHandler.LookupCommands/HandleListClickToMoveCommand, ChatHandler.MiscCommands/HandleGMListIngameCommand, WorldSession.MiscHandler/operator() | — |
| FindMasterPlayer | method | ObjectGuid/operator! | game_Chat_Channel/GetPlayer, game_Chat_Channel/List, game_Guild_Guild/BroadcastToGuild, game_Guild_Guild/BroadcastToOfficers, game_Mail_Mail/SendMailTo, SocialMgr/BroadcastToFriendListers, SocialMgr/GetFriendInfo, WorldSession.CharacterHandler/HandlePlayerLogin, WorldSession.MiscHandler/HandleAddFriendOpcode | — |
| GetMasterPlayers | method | — | — | — |
| FindPlayerPointer | method | — | — | — |
| AddObject | method | — | Corpse/AddToWorld | — |
| AddObject#4 | method | — | Transport/Create#2 | — |
| FindPlayerPointer#2 | method | — | — | — |
| RemoveObject | method | — | Corpse/RemoveFromWorld | — |
| RemoveObject#4 | method | — | — | — |
| SaveAllPlayers | method | Player.Main/SaveToDB | ChatHandler.ServerCommands/HandleSaveAllCommand, Master/_OnSignal | — |
| KickPlayer | method | Player.Main/GetSession, WorldSession.Main/KickPlayer, WorldSession.Main/LogoutPlayer | AccountMgr/DeleteAccount | — |
| GetCorpseForPlayerGUID | method | Corpse/GetType, Errors/PrintStacktraceAndThrow, ObjectGuid/IsPlayer | ChatHandler.TeleportCommands/HandleGocorpseCommand, ObjectGridLoader/LoadHelper, Player.Main/GetCorpse | — |
| RemoveCorpse | method | Corpse/GetOwnerGuid, Corpse/GetType, Corpse/RemoveFromWorld, Errors/PrintStacktraceAndThrow, GridDefines/ComputeCellPair, ObjectGuid/GetCounter, ObjectMgr/DeleteCorpseCellData, WorldObject.Object/GetMapId, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY | — | — |
| AddCorpse | method | Corpse/GetOwnerGuid, Corpse/GetType, Errors/PrintStacktraceAndThrow, GridDefines/ComputeCellPair, ObjectGuid/GetCounter, ObjectMgr/AddCorpseCellData, WorldObject.Object/GetInstanceId, WorldObject.Object/GetMapId, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY | ObjectMgr/LoadCorpses, Player.Main/CreateCorpse | — |
| AddCorpsesToGrid | method | Corpse/GetGrid, Map.Main/GetInstanceId, Map.Main/Instanceable, ObjectGuid/IsPlayer, WorldObject.Object/GetInstanceId | Map.Main/EnsureGridLoaded | — |
| ConvertCorpseForPlayer | method | Corpse/DeleteFromDB, Errors/PrintStacktraceAndThrow, Map.Main/AddCorpseToRemove, MapManager/FindMap, Object/GetObjectGuid, ObjectGuid/IsPlayer, ObjectGuid/ObjectGuid, WorldObject.Object/GetInstanceId, WorldObject.Object/GetMapId | ChatHandler.CharacterCommands/HandleReviveCommand, Player.Main/DeleteFromDB, Player.Main/LoadCorpse, Player.Main/RemovedInsignia, Player.Main/SpawnCorpseBones, Spell.Effects/EffectSkinPlayerCorpse | — |
| RemoveOldCorpses | method | Corpse/IsExpired | ChatHandler.ServerCommands/HandleServerCorpsesCommand, World/Update | — |
| AddObject#3 | method | Player.Main/GetName | PlayerBotAI/SpawnNewPlayer, WorldSession.CharacterHandler/HandlePlayerLogin | — |
| RemoveObject#3 | method | Player.Main/GetName | Map.Main/CrashUnload, Map.Main/DeleteFromWorld | — |
| AddObject#2 | method | MasterPlayer.Main/GetName | WorldSession.CharacterHandler/HandlePlayerLogin | — |
| RemoveObject#2 | method | MasterPlayer.Main/GetName | MasterPlayer.Main/~MasterPlayer | — |
