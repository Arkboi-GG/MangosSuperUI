<!-- provenance: boundary-bleed -->
# ChatHandler.MiscCommands

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ChatHandler.MiscCommands

## Purpose & Responsibilities

`ChatHandler.MiscCommands` implements a collection of miscellaneous administrative, debugging, and utility commands for the MaNGOS server. These commands are handled by the `ChatHandler` class and are primarily intended for Game Masters (GMs), administrators, and developers.

The unit covers several distinct subsystems:
1.  **GM Status & Visibility:** Commands to toggle GM mode, GM chat, and GM invisibility levels.
2.  **Auction House & Services:** Commands to force-open auction houses (Alliance, Horde, Goblin) or service windows (Bank, Stable) regardless of NPC proximity.
3.  **Guild Management:** Full lifecycle management of guilds (create, invite, uninvite, rank, delete, rename, view logs).
4.  **Instance & Dungeon Control:** Commands to manage instance bindings, switch instances, view instance performance/data, and reset instance states.
5.  **Mail System:** Utilities to send mail, items, and money to specific players or mass-mail entire races.
6.  **BattleGround (BG) Administration:** Commands to view BG status, force-start/stop BGs, and register players to specific BG queues.
7.  **World Data Inspection:** Commands to inspect area triggers, cinematic waypoints, spawn pools, and graveyard links.
8.  **Help & Navigation:** Basic help display and camera/view manipulation.

This unit does not handle core gameplay logic but provides the interface for operators to manipulate game state, debug world data, and assist players.

## Member-by-Member Behavior

### GM Status & Visibility

*   **HandleGMCommand**: Toggles the invoking player's GM mode. If no argument is provided, it reports the current status. If `on` or `off` is provided, it calls `Player.Main/SetGameMaster`. It uses `ChatHandler.Chat/ExtractOnOff` to parse the boolean argument.
*   **HandleGMChatCommand**: Similar to `HandleGMCommand`, but toggles the GM chat badge visibility using `Player.Main/SetGMChat`.
*   **HandleGMVisibleCommand**: Manages GM invisibility. It supports two modes: simple on/off via `ExtractOnOff`, or setting a specific invisibility level via `ExtractUInt32`. The level must be less than or equal to the GM's access level (`ChatHandler.Chat/GetAccessLevel`). It updates `Player.Main/SetGMVisible` and `Player.Main/SetGMInvisibilityLevel`.

### Auction House & Service Windows

These commands bypass normal NPC interaction requirements by directly sending packets to the client and setting internal player flags.

*   **HandleAuctionAllianceCommand**: Sets the player's auction access mode to Alliance (0) if they are Alliance, otherwise -1 (error/none). Sends `WorldSession.AuctionHouseHandler/SendAuctionHello`.
*   **HandleAuctionHordeCommand**: Sets the player's auction access mode to Horde (0) if they are Horde, otherwise -1. Sends `WorldSession.AuctionHouseHandler/SendAuctionHello`.
*   **HandleAuctionGoblinCommand**: Sets the player's auction access mode to 1 (Goblin/Neutral). Sends `WorldSession.AuctionHouseHandler/SendAuctionHello`.
*   **HandleAuctionCommand**: Sets the player's auction access mode to 0 (Standard). Sends `WorldSession.AuctionHouseHandler/SendAuctionHello`.
*   **HandleBankCommand**: Sends the bank window packet directly to the player using `WorldSession.NPCHandler/SendShowBank` with the player's GUID.
*   **HandleStableCommand**: Sends the stable master packet directly to the player using `WorldSession.NPCHandler/SendStablePet` with the player's GUID.

### Help & View

*   **HandleHelpCommand**: Displays help text. If no arguments, it shows general help. If arguments are provided, it looks up the command in `ChatHandler.Chat/getCommandTable` and displays specific help via `ChatHandler.Chat/ShowHelpForCommand`.
*   **HandleCommandsCommand**: Displays the full list of available commands by calling `ChatHandler.Chat/ShowHelpForCommand` with an empty string.
*   **HandleSetViewCommand**: Changes the GM's camera view to follow a selected unit. It retrieves the selected unit via `ChatHandler.Chat/GetSelectedUnit` and calls `Camera/SetView` on the player's camera (`Player.Main/GetCamera`).

### Guild Management

These commands allow GMs to manipulate guild structures directly.

*   **HandleGuildCreateCommand**: Creates a new guild. It extracts the leader's name and the guild name. It checks if the leader is already in a guild (`Player.Main/GetGuildId`). If valid, it creates a `game_Guild_Guild` object, calls `Guild.Create`, and registers it with `GuildMgr/AddGuild`.
*   **HandleGuildInviteCommand**: Invites a player to a guild. It resolves the target player and guild name. It retrieves the guild via `GuildMgr/GetGuildByName` and calls `game_Guild_Guild/AddMember` with the lowest rank.
*   **HandleGuildUninviteCommand**: Removes a player from a guild. It resolves the target player and their guild ID. It retrieves the guild via `GuildMgr/GetGuildById` and calls `game_Guild_Guild/DelMember`. If the guild becomes empty, it calls `game_Guild_Guild/Disband` and deletes the guild object.
*   **HandleGuildRankCommand**: Changes a member's rank. It resolves the target player and guild, then calls `game_Guild_Guild/ChangeRank` on the member slot.
*   **HandleGuildDeleteCommand**: Disbands a guild by name. It retrieves the guild via `GuildMgr/GetGuildByName`, calls `game_Guild_Guild/Disband`, and deletes the object.
*   **HandleGuildRenameCommand**: Renames a guild. It checks for name collisions via `GuildMgr/GetGuildByName` and calls `game_Guild_Guild/Rename`.
*   **HandleGuildShowLogCommand**: Displays the guild event log. It retrieves the guild and iterates over `Guild/GetGuildEventLog`, formatting entries with timestamps converted via `shared_Util/secsToTimeString`.

### Instance & Dungeon Control

*   **HandleInstanceBindingMode**: Toggles "smart rebinding" for the player via `Player.Main/SetSmartInstanceBindingMode`.
*   **HandleInstanceSwitchCommand**: Forces a player to switch to a specific instance ID. It calls `Player.Main/SwitchInstance` and disables auto-switching via `Player.Main/SetAutoInstanceSwitch`.
*   **HandleInstanceContinentsCommand**: Lists players on continent maps (IDs 0 and 1). It iterates through maps using `MapManager/FindMap` and reports player counts and visibility distances.
*   **HandleInstanceGetDataCommand**: Reads a specific data index from the current instance's `InstanceData` object.
*   **HandleInstanceSetDataCommand**: Writes a value to a specific data index in the current instance's `InstanceData` object.
*   **HandleInstancePerfInfosCommand**: Prints performance statistics for the current map, including counts of visible players, game objects, units, and corpses.
*   **HandleInstanceListBindsCommand**: Lists all instance bindings for a player and their group. It iterates over `Player.Main/GetBoundInstances` and `Group/GetBoundInstances`, displaying reset times and permissions.
*   **HandleInstanceUnbindHelper**: Internal helper that iterates through a player's bound instances and calls `Player.Main/UnbindInstance` for those matching the criteria (specific map or all).
*   **HandleInstanceUnbindCommand**: Entry point for unbinding. It parses arguments to determine if a specific map ID is targeted or if all instances should be unbound. It delegates to `HandleInstanceUnbindHelper`.
*   **HandleInstanceGroupUnbindCommand**: Unbinds all members of a group from instances. It iterates through group members, calls `HandleInstanceUnbindHelper` for each, and then disbands the group via `game_Group_Group/Disband`.
*   **HandleInstanceStatsCommand**: Reports global instance statistics (loaded instances, players in instances, saves, bounds) from `MapManager` and `MapPersistentStateMgr`.
*   **HandleInstanceSaveDataCommand**: Forces the current instance's `InstanceData` to save to the database via `InstanceData/SaveToDB`.

### Mail System

*   **HandleSendMailHelper**: Helper that parses subject and body text from arguments and sets them on a `MailDraft` object.
*   **HandleSendMassMailCommand**: Sends mail to all players of a specific race mask. It creates a `MailDraft`, populates it via `HandleSendMailHelper`, and adds a task to `MassMailMgr/AddMassMailTask`.
*   **HandleSendItemsHelper**: Helper that parses subject, body, and a list of item IDs/counts. It creates `Item` objects, saves them to DB (`game_Objects_Item/SaveToDB`), and adds them to the `MailDraft`.
*   **HandleSendItemsCommand**: Sends items to a specific player. It uses `HandleSendItemsHelper` to populate the draft and sends it via `game_Mail_Mail/SendMailTo`.
*   **HandleSendMassItemsCommand**: Sends items to all players of a specific race mask. Uses `HandleSendItemsHelper` and `MassMailMgr/AddMassMailTask`.
*   **HandleSendMoneyHelper**: Helper that parses subject, body, and amount, setting them on the `MailDraft`.
*   **HandleSendMoneyCommand**: Sends money to a specific player. Uses `HandleSendMoneyHelper` and `game_Mail_Mail/SendMailTo`.
*   **HandleSendMassMoneyCommand**: Sends money to all players of a specific race mask. Uses `HandleSendMoneyHelper` and `MassMailMgr/AddMassMailTask`.
*   **HandleSendMailCommand**: Sends a simple mail to a specific player. Uses `HandleSendMailHelper` and `game_Mail_Mail/SendMailTo`.
*   **HandleSendMessageCommand**: Sends a direct chat message to a player. It checks if the player is logging out (`WorldSession.Main/IsLogingOut`) and uses `WorldSession.Main/SendAreaTriggerMessage` for delivery.

### BattleGround (BG) Administration

*   **RegisterPlayerToBG**: Static helper function that registers a player to a specific BattleGround queue. It checks level access (`Player.Main/GetBGAccessByLevel`), ensures they aren't already in a BG, sets their entry point, and sends the BG list packet.
*   **HandleGoWarsongCommand**, **HandleGoArathiCommand**, **HandleGoAlteracCommand**: Convenience wrappers that call `RegisterPlayerToBG` with the respective BG type constants. Note: These are called by `BattleBotAI.Main/UpdateAI` according to the map, suggesting they may be used by automated bots to join BGs.
*   **HandleBGStatusCommand**: Displays detailed information about all active BattleGrounds and queues. It iterates through `BattleGroundMgr/GetBattleGroundsBegin` and `BattleGroundMgr/GetBattleGroundsEnd`, reporting player counts, status, and start times.
*   **HandleBGStartCommand**: Forces the current BattleGround to start immediately by setting the delay to 0 via `BattleGround/SetStartDelayTime`.
*   **HandleBGStopCommand**: Stops the current BattleGround by calling `game_Battlegrounds_BattleGround/StopBattleGround`.
*   **HandleBGCustomCommand**: Passes raw arguments to the BattleGround's custom command handler via `game_Battlegrounds_BattleGround/HandleCommand`.

### World Data Inspection (Triggers, Cinematics, Pools, Graves)

*   **HandleTriggerCommand**: Inspects a specific area trigger by ID or finds the nearest one. It displays trigger details, teleport destinations, and associated quests.
*   **HandleTriggerActiveCommand**: Lists all area triggers currently active at the player's location using `ObjectMgr/IsPointInAreaTriggerZone`.
*   **HandleTriggerNearCommand**: Lists area triggers within a specified distance of the player.
*   **ShowTriggerListHelper**, **ShowTriggerTargetListHelper**: Internal helpers that format and print trigger information.
*   **HandleCinematicAddWpCommand**: Adds a new waypoint to the `cinematic_waypoints` table in the database. It inserts the current player's position and a timer/comment. It then reloads cinematics via `ObjectMgr/LoadCinematicsWaypoints`.
*   **HandleCinematicGoTimeCommand**: Teleports the player to a specific cinematic waypoint position at a given time using `ObjectMgr/GetCinematicPosition` and `Player.Main/TeleportTo`.
*   **HandleCinematicListWpCommand**: Placeholder stub (returns true, does nothing).
*   **HandlePoolUpdateCommand**: Updates a specific spawn pool, forcing it to respawn creatures/game objects up to its limit.
*   **HandlePoolSpawnsCommand**: Lists all spawned creatures and game objects in the current map's pools.
*   **HandlePoolInfoCommand**: Displays detailed information about a specific pool, including its members, chances, and spawned status.
*   **HandleLinkGraveCommand**: Links a graveyard to the current zone for a specific team. It validates the zone and graveyard ID, then calls `ObjectMgr/AddGraveYardLink`.
*   **HandleNearGraveCommand**: Finds the nearest graveyard to the player's location for a specific team and displays its details.

## Cross-Unit Boundaries

*   **ChatHandler.Chat**: Heavily utilized for parsing arguments (`ExtractOnOff`, `ExtractUInt32`, etc.), sending messages (`SendSysMessage`, `PSendSysMessage`), and retrieving command tables/help.
*   **Player.Main**: Accessed for player state (`GetTeam`, `IsGameMaster`, `GetGuildId`, `GetBoundInstances`, etc.) and actions (`SetAuctionAccessMode`, `TeleportTo`, `UnbindInstance`).
*   **WorldSession.Main**: Used to retrieve the player object (`GetPlayer`) and send network packets (`SendAuctionHello`, `SendShowBank`, `SendAreaTriggerMessage`).
*   **GuildMgr / game_Guild_Guild**: Core guild operations are delegated here. `ChatHandler` acts as the interface, passing validated data to these units for persistence and state changes.
*   **MassMailMgr**: Used for bulk mail operations. `ChatHandler` prepares the `MailDraft` and delegates the actual distribution to `MassMailMgr`.
*   **ObjectMgr**: Used for static data lookups (triggers, cinematics, graveyards, pool templates).
*   **MapManager / Map.Main**: Used for instance and map-level operations (finding maps, getting instance data, printing infos).
*   **BattleGroundMgr / BattleGround**: Used for querying and manipulating BattleGround state.
*   **Database**: Direct SQL execution is used in `HandleGMListFullCommand` (querying `account`) and `HandleCinematicAddWpCommand` (inserting into `cinematic_waypoints`).

## Data Model

*   **`account`**: Queried in `HandleGMListFullCommand` to list usernames and GM levels. Columns used: `username`, `gmlevel` (from joined `account_access` table, though only `account` is listed in schema, the query implies a join).
*   **`cinematic_waypoints`**: Inserted into in `HandleCinematicAddWpCommand`. Columns used: `cinematic`, `timer`, `position_x`, `position_y`, `position_z`, `comment`.

## Notable Implementation Details

*   **Memory Management in Guild Commands**: `HandleGuildCreateCommand` manually allocates a `Guild` object with `new`. If creation fails, it explicitly `delete`s it. Successful guilds are registered with `GuildMgr`. Similarly, `HandleGuildUninviteCommand` and `HandleGuildDeleteCommand` `delete` the guild object after disbanding. This suggests `GuildMgr` does not automatically clean up memory upon disbanding in this codebase version, or these commands bypass standard cleanup paths.
*   **Mass Mail Dynamic Allocation**: In `HandleSendMassMailCommand`, `HandleSendMassItemsCommand`, and `HandleSendMassMoneyCommand`, the `MailDraft` is allocated with `new` because it is transferred to `MassMailMgr`. The caller (`ChatHandler`) relinquishes ownership, so it does not delete it. If helper functions fail, the draft is manually deleted to prevent leaks.
*   **Hardcoded Continent IDs**: `HandleInstanceContinentsCommand` hardcodes map IDs 0 and 1 as continents. This may not reflect all world maps if the game expands or if other maps are considered continental.
*   **BG Bot Integration**: The `HandleGo...Command` functions are marked as called by `BattleBotAI.Main/UpdateAI`. This indicates these commands are not just for human GMs but are part of an automated bot system that joins battlegrounds.
*   **Cinematic List Stub**: `HandleCinematicListWpCommand` is a stub that returns `true` without implementing functionality.
*   **Direct DB Insert**: `HandleCinematicAddWpCommand` performs a direct `INSERT` into `cinematic_waypoints` and then reloads the data. This is a runtime modification of persistent world data.
*   **Instance Unbind Logic**: `HandleInstanceUnbindHelper` skips unbinding the instance the player is currently in (`itr->first != player->GetMapId()`). This prevents kicking the player out of their current dungeon.

## Member Reference

*   **HandleHelpCommand**: Displays help for a specific command or general help if no arguments are provided. Uses `ChatHandler.Chat/getCommandTable` and `ChatHandler.Chat/ShowHelpForCommand`.
*   **HandleCommandsCommand**: Displays the full list of available commands by calling `ChatHandler.Chat/ShowHelpForCommand` with an empty string.
*   **HandleAuctionAllianceCommand**: Opens the Alliance auction house for the player by setting access mode and sending `WorldSession.AuctionHouseHandler/SendAuctionHello`.
*   **HandleAuctionHordeCommand**: Opens the Horde auction house for the player by setting access mode and sending `WorldSession.AuctionHouseHandler/SendAuctionHello`.
*   **HandleAuctionGoblinCommand**: Opens the Goblin (neutral) auction house for the player by setting access mode to 1 and sending `WorldSession.AuctionHouseHandler/SendAuctionHello`.
*   **HandleAuctionCommand**: Opens the standard auction house for the player by setting access mode to 0 and sending `WorldSession.AuctionHouseHandler/SendAuctionHello`.
*   **HandleBankCommand**: Opens the bank window for the player by sending `WorldSession.NPCHandler/SendShowBank` with the player's GUID.
*   **HandleStableCommand**: Opens the stable master window for the player by sending `WorldSession.NPCHandler/SendStablePet` with the player's GUID.
*   **HandleGMCommand**: Toggles the player's GM mode on/off based on arguments, updating `Player.Main/SetGameMaster`.
*   **HandleGMChatCommand**: Toggles the player's GM chat badge visibility on/off, updating `Player.Main/SetGMChat`.
*   **HandleGMVisibleCommand**: Sets the player's GM visibility and invisibility level, validating against the GM's access level.
*   **HandleSetViewCommand**: Changes the player's camera view to follow a selected unit using `Camera/SetView`.
*   **HandleGMListFullCommand**: Queries the `account` table to list all accounts with GM level > 0, displaying username and GM level.
*   **HandleGMListIngameCommand**: Iterates through online players to list those with GM status, showing their name and whisper acceptance status.
*   **RegisterPlayerToBG**: Helper function that registers a player to a specific BattleGround queue, checking level access and setting entry points.
*   **HandleGoWarsongCommand**: Wrapper that calls `RegisterPlayerToBG` for Warsong Gulch. Called by `BattleBotAI.Main/UpdateAI`.
*   **HandleGoArathiCommand**: Wrapper that calls `RegisterPlayerToBG` for Arathi Basin. Called by `BattleBotAI.Main/UpdateAI`.
*   **HandleGoAlteracCommand**: Wrapper that calls `RegisterPlayerToBG` for Alterac Valley. Called by `BattleBotAI.Main/UpdateAI`.
*   **HandleGuildCreateCommand**: Creates a new guild with a specified leader and name, registering it with `GuildMgr`.
*   **HandleGuildInviteCommand**: Invites a player to a specified guild, adding them with the lowest rank.
*   **HandleGuildUninviteCommand**: Removes a player from their guild, disbanning the guild if it becomes empty.
*   **HandleGuildRankCommand**: Changes a guild member's rank to a specified value.
*   **HandleGuildDeleteCommand**: Disbands and deletes a guild by name.
*   **HandleGuildRenameCommand**: Renames a guild, checking for name collisions.
*   **HandleGuildShowLogCommand**: Displays the event log for a specified guild.
*   **HandleInstanceBindingMode**: Toggles smart instance rebinding for the player.
*   **HandleInstanceSwitchCommand**: Forces a player to switch to a specific instance ID.
*   **HandleInstanceContinentsCommand**: Lists players on continent maps (IDs 0 and 1) and their visibility distances.
*   **HandleInstanceGetDataCommand**: Reads a specific data index from the current instance's `InstanceData`.
*   **HandleInstanceSetDataCommand**: Writes a value to a specific data index in the current instance's `InstanceData`.
*   **HandleInstancePerfInfosCommand**: Prints performance statistics for the current map.
*   **HandleInstanceListBindsCommand**: Lists all instance bindings for a player and their group.
*   **HandleInstanceUnbindHelper**: Internal helper that unbinds a player from specific or all instances.
*   **HandleInstanceUnbindCommand**: Entry point for unbinding a player from instances, delegating to `HandleInstanceUnbindHelper`.
*   **HandleInstanceGroupUnbindCommand**: Unbinds all group members from instances and disbands the group.
*   **HandleInstanceStatsCommand**: Reports global instance statistics.
*   **HandleInstanceSaveDataCommand**: Forces the current instance's data to save to the database.
*   **HandleSendMailHelper**: Helper that parses subject and body text for a mail draft.
*   **HandleSendMassMailCommand**: Sends mail to all players of a specific race mask using `MassMailMgr`.
*   **HandleSendItemsHelper**: Helper that parses subject, body, and item lists for a mail draft, creating and saving items.
*   **HandleSendItemsCommand**: Sends items to a specific player via mail.
*   **HandleSendMassItemsCommand**: Sends items to all players of a specific race mask using `MassMailMgr`.
*   **HandleSendMoneyHelper**: Helper that parses subject, body, and amount for a mail draft.
*   **HandleSendMoneyCommand**: Sends money to a specific player via mail.
*   **HandleSendMassMoneyCommand**: Sends money to all players of a specific race mask using `MassMailMgr`.
*   **HandleSendMailCommand**: Sends a simple mail to a specific player.
*   **HandleSendMessageCommand**: Sends a direct chat message to a player, checking if they are logging out.
*   **HandlePoolUpdateCommand**: Updates a specific spawn pool, forcing respawns.
*   **HandlePoolSpawnsCommand**: Lists all spawned creatures and game objects in the current map's pools.
*   **HandlePoolInfoCommand**: Displays detailed information about a specific pool.
*   **ShowTriggerTargetListHelper**: Internal helper that formats and prints area trigger teleport destination information.
*   **ShowTriggerListHelper**: Internal helper that formats and prints area trigger information.
*   **HandleTriggerCommand**: Inspects a specific area trigger by ID or finds the nearest one, displaying details and associated quests.
*   **HandleTriggerActiveCommand**: Lists all area triggers currently active at the player's location.
*   **HandleTriggerNearCommand**: Lists area triggers within a specified distance of the player.
*   **HandleCinematicAddWpCommand**: Adds a new waypoint to the `cinematic_waypoints` table in the database and reloads cinematics.
*   **HandleCinematicGoTimeCommand**: Teleports the player to a specific cinematic waypoint position at a given time.
*   **HandleCinematicListWpCommand**: Placeholder stub that returns true without implementing functionality.
*   **HandleBGStatusCommand**: Displays detailed information about all active BattleGrounds and queues.
*   **HandleBGStartCommand**: Forces the current BattleGround to start immediately.
*   **HandleBGStopCommand**: Stops the current BattleGround.
*   **HandleBGCustomCommand**: Passes raw arguments to the BattleGround's custom command handler.
*   **HandleLinkGraveCommand**: Links a graveyard to the current zone for a specific team.
*   **HandleNearGraveCommand**: Finds the nearest graveyard to the player's location for a specific team and displays its details.

---

<!-- machine-true, projected from graph.json -->

## Map — ChatHandler.MiscCommands

*Source:* MiscCommands.cpp, Chat.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| HandleHelpCommand | method | ChatHandler.Chat/getCommandTable, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/ShowHelpForCommand | — | — |
| HandleCommandsCommand | method | ChatHandler.Chat/getCommandTable, ChatHandler.Chat/ShowHelpForCommand | — | — |
| HandleAuctionAllianceCommand | method | Player.Main/GetTeam, Player.Main/SetAuctionAccessMode, WorldSession.AuctionHouseHandler/SendAuctionHello, WorldSession.Main/GetPlayer | — | — |
| HandleAuctionHordeCommand | method | Player.Main/GetTeam, Player.Main/SetAuctionAccessMode, WorldSession.AuctionHouseHandler/SendAuctionHello, WorldSession.Main/GetPlayer | — | — |
| HandleAuctionGoblinCommand | method | Player.Main/SetAuctionAccessMode, WorldSession.AuctionHouseHandler/SendAuctionHello, WorldSession.Main/GetPlayer | — | — |
| HandleAuctionCommand | method | Player.Main/SetAuctionAccessMode, WorldSession.AuctionHouseHandler/SendAuctionHello, WorldSession.Main/GetPlayer | — | — |
| HandleBankCommand | method | Object/GetObjectGuid, WorldSession.Main/GetPlayer, WorldSession.NPCHandler/SendShowBank | — | — |
| HandleStableCommand | method | Object/GetObjectGuid, WorldSession.Main/GetPlayer, WorldSession.NPCHandler/SendStablePet | — | — |
| HandleGMCommand | method | ChatHandler.Chat/ExtractOnOff, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/IsGameMaster, Player.Main/SetGameMaster, WorldSession.Main/GetPlayer, WorldSession.Main/SendNotification#2 | — | — |
| HandleGMChatCommand | method | ChatHandler.Chat/ExtractOnOff, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/IsGMChat, Player.Main/SetGMChat, WorldSession.Main/GetPlayer, WorldSession.Main/SendNotification#2 | — | — |
| HandleGMVisibleCommand | method | ChatHandler.Chat/ExtractOnOff, ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/GetAccessLevel, ChatHandler.Chat/GetMangosString, ChatHandler.Chat/GetSession, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/GetGMInvisibilityLevel, Player.Main/IsGMVisible, Player.Main/SetGMInvisibilityLevel, Player.Main/SetGMVisible, WorldSession.Main/GetPlayer | — | — |
| HandleSetViewCommand | method | Camera/SetView, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/GetCamera, WorldSession.Main/GetPlayer | — | — |
| HandleGMListFullCommand | method | ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SendSysMessage#2, Database/PQuery, Field/GetString, QueryResult/Fetch, QueryResult/NextRow | — | account |
| HandleGMListIngameCommand | method | ChatHandler.Chat/GetMangosString, ChatHandler.Chat/GetNameLink, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage#2, ObjectAccessor/GetPlayers, Player.Main/GetSession, Player.Main/IsAcceptWhispers, Player.Main/IsGameMaster, Player.Main/IsVisibleGloballyFor, World/getConfig#4, WorldSession.Main/GetPlayer, WorldSession.Main/GetSecurity | — | — |
| RegisterPlayerToBG | function | Object/GetObjectGuid, Player.Main/GetBGAccessByLevel, Player.Main/InBattleGround, Player.Main/SetBattleGroundEntryPoint#2, WorldObject.Object/GetMapId, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldSession.BattleGroundHandler/SendBattleGroundList, WorldSession.Main/GetPlayer | — | — |
| HandleGoWarsongCommand | method | — | BattleBotAI.Main/UpdateAI | — |
| HandleGoArathiCommand | method | — | BattleBotAI.Main/UpdateAI | — |
| HandleGoAlteracCommand | method | — | BattleBotAI.Main/UpdateAI | — |
| HandleGuildCreateCommand | method | ChatHandler.Chat/ExtractOptNotLastArg, ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/ExtractQuotedArg, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, game_Guild_Guild/Create#2, game_Guild_Guild/Guild, GuildMgr/AddGuild, Player.Main/GetGuildId | — | — |
| HandleGuildInviteCommand | method | ChatHandler.Chat/ExtractOptNotLastArg, ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/ExtractQuotedArg, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, game_Guild_Guild/AddMember, Guild/GetLowestRank, GuildMgr/GetGuildByName, ObjectGuid/ObjectGuid | — | — |
| HandleGuildUninviteCommand | method | ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, game_Guild_Guild/DelMember, game_Guild_Guild/Disband, GuildMgr/GetGuildById, ObjectGuid/ObjectGuid, Player.Main/GetGuildId, Player.Main/GetGuildIdFromDB | — | — |
| HandleGuildRankCommand | method | ChatHandler.Chat/ExtractOptNotLastArg, ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, game_Guild_Guild/ChangeRank, Guild/GetLowestRank, Guild/GetMemberSlot, GuildMgr/GetGuildById, ObjectGuid/ObjectGuid, Player.Main/GetGuildId, Player.Main/GetGuildIdFromDB | — | — |
| HandleGuildDeleteCommand | method | ChatHandler.Chat/ExtractQuotedArg, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, game_Guild_Guild/Disband, GuildMgr/GetGuildByName | — | — |
| HandleGuildRenameCommand | method | ChatHandler.Chat/ExtractQuotedArg, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, game_Guild_Guild/Rename, GuildMgr/GetGuildByName | — | — |
| HandleGuildShowLogCommand | method | ChatHandler.Chat/ExtractQuotedArg, ChatHandler.Chat/GetNameLink#3, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, game_Guild_Guild/GuildEventLogTypeToString, Guild/GetGuildEventLog, GuildMgr/GetGuildByName, shared_Util/secsToTimeString | — | — |
| HandleInstanceBindingMode | method | ChatHandler.Chat/GetSession, ChatHandler.Chat/PSendSysMessage, Player.Main/SetSmartInstanceBindingMode, WorldSession.Main/GetPlayer | — | — |
| HandleInstanceSwitchCommand | method | ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/PSendSysMessage, Player.Main/SetAutoInstanceSwitch, Player.Main/SwitchInstance, WorldObject.Object/GetInstanceId | — | — |
| HandleInstanceContinentsCommand | method | ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/PSendSysMessage, Map.Main/GetGridActivationDistance, Map.Main/GetPlayers, Map.Main/GetVisibilityDistance, MapManager/FindMap, MapRefManager/begin#2, MapRefManager/end#2, Player.Main/GetName, WorldObject.Object/GetInstanceId, WorldObject.Object/GetMapId | — | — |
| HandleInstanceGetDataCommand | method | ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/GetSession, ChatHandler.Chat/PSendSysMessage, InstanceData/GetData, Map.Main/GetInstanceData, WorldObject.Object/FindMap, WorldSession.Main/GetPlayer | — | — |
| HandleInstanceSetDataCommand | method | ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/GetSession, ChatHandler.Chat/PSendSysMessage, InstanceData/GetData, InstanceData/SetData, Map.Main/GetInstanceData, WorldObject.Object/FindMap, WorldSession.Main/GetPlayer | — | — |
| HandleInstancePerfInfosCommand | method | ChatHandler.Chat/GetSession, ChatHandler.Chat/PSendSysMessage, Map.Main/PrintInfos, ObjectGuid/GetHigh, WorldObject.Object/FindMap, WorldSession.Main/GetPlayer | — | — |
| HandleInstanceListBindsCommand | method | ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/PSendSysMessage, DungeonPersistentState/CanReset, DungeonPersistentState/GetResetTime, DungeonResetScheduler/GetResetTimeFor, Group/GetBoundInstances, MapPersistentStateManager/GetScheduler, MapPersistentStateMgr/GetInstanceId, Player.Main/GetBoundInstances, Player.Main/GetGroup, shared_Util/secsToTimeString, WorldSession.Main/GetPlayer | — | — |
| HandleInstanceUnbindHelper | method | DungeonPersistentState/CanReset, DungeonPersistentState/GetResetTime, MapPersistentStateMgr/GetInstanceId, Object/IsInWorld, Player.Main/GetBoundInstances, Player.Main/PSendSysMessage, Player.Main/UnbindInstance, shared_Util/secsToTimeString, WorldObject.Object/GetMapId | — | — |
| HandleInstanceUnbindCommand | method | ChatHandler.Chat/GetAccessLevel, ChatHandler.Chat/GetSelectedPlayer, shared_Util/isNumeric#4, WorldSession.Main/GetPlayer | — | — |
| HandleInstanceGroupUnbindCommand | method | ChatHandler.Chat/GetNameLink, ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SetSentErrorMessage, game_Group_Group/Disband, Group/GetFirstMember, GroupReference/next, Object/IsInWorld, Player.Main/GetGroup, Player.Main/InBattleGround, shared_Util/isNumeric#4 | — | — |
| HandleInstanceStatsCommand | method | ChatHandler.Chat/PSendSysMessage, MapManager/GetNumInstances, MapManager/GetNumPlayersInInstances, MapPersistentStateMgr/GetStatistics | — | — |
| HandleInstanceSaveDataCommand | method | ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SetSentErrorMessage, InstanceData/SaveToDB, Map.Main/GetInstanceData, WorldObject.Object/GetMap, WorldSession.Main/GetPlayer | — | — |
| HandleSendMailHelper | method | ChatHandler.Chat/ExtractQuotedArg, game_Mail_Mail/SetSubjectAndBody | — | — |
| HandleSendMassMailCommand | method | ChatHandler.Chat/ExtractRaceMask, ChatHandler.Chat/PSendSysMessage#2, game_Mail_Mail/MailSender#2, MailDraft/MailDraft, MassMailMgr/AddMassMailTask#3, Object/GetObjectGuid, ObjectGuid/GetCounter, WorldSession.Main/GetPlayer | — | — |
| HandleSendItemsHelper | method | ChatHandler.Chat/ExtractArg, ChatHandler.Chat/ExtractQuotedArg, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, game_Mail_Mail/AddItem, game_Mail_Mail/SetSubjectAndBody, game_Objects_Item/CreateItem, game_Objects_Item/SaveToDB, ItemPrototype/GetMaxStackSize, Object/GetObjectGuid, ObjectGuid/ObjectGuid, ObjectMgr/GetItemPrototype, WorldSession.Main/GetPlayer | — | — |
| HandleSendItemsCommand | method | ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/playerLink, ChatHandler.Chat/PSendSysMessage#2, game_Mail_Mail/MailReceiver#2, game_Mail_Mail/MailSender#2, game_Mail_Mail/SendMailTo, MailDraft/MailDraft, Object/GetObjectGuid, ObjectGuid/GetCounter, ObjectGuid/ObjectGuid, WorldSession.Main/GetPlayer | — | — |
| HandleSendMassItemsCommand | method | ChatHandler.Chat/ExtractRaceMask, ChatHandler.Chat/PSendSysMessage#2, game_Mail_Mail/MailSender#2, MailDraft/MailDraft, MassMailMgr/AddMassMailTask#3, Object/GetObjectGuid, ObjectGuid/GetCounter, WorldSession.Main/GetPlayer | — | — |
| HandleSendMoneyHelper | method | ChatHandler.Chat/ExtractQuotedArg, ChatHandler.Chat/ExtractUInt32, game_Mail_Mail/SetSubjectAndBody, MailDraft/SetMoney | — | — |
| HandleSendMoneyCommand | method | ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/playerLink, ChatHandler.Chat/PSendSysMessage#2, game_Mail_Mail/MailReceiver#2, game_Mail_Mail/MailSender#2, game_Mail_Mail/SendMailTo, MailDraft/MailDraft, Object/GetObjectGuid, ObjectGuid/GetCounter, ObjectGuid/ObjectGuid, WorldSession.Main/GetPlayer | — | — |
| HandleSendMassMoneyCommand | method | ChatHandler.Chat/ExtractRaceMask, ChatHandler.Chat/PSendSysMessage#2, game_Mail_Mail/MailSender#2, MailDraft/MailDraft, MassMailMgr/AddMassMailTask#3, Object/GetObjectGuid, ObjectGuid/GetCounter, WorldSession.Main/GetPlayer | — | — |
| HandleSendMailCommand | method | ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/playerLink, ChatHandler.Chat/PSendSysMessage#2, game_Mail_Mail/MailReceiver#2, game_Mail_Mail/MailSender#2, game_Mail_Mail/SendMailTo, MailDraft/MailDraft, Object/GetObjectGuid, ObjectGuid/GetCounter, ObjectGuid/ObjectGuid, WorldSession.Main/GetPlayer | — | — |
| HandleSendMessageCommand | method | ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/GetNameLink, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/GetSession, WorldSession.Main/IsLogingOut, WorldSession.Main/SendAreaTriggerMessage | — | — |
| HandlePoolUpdateCommand | method | ChatHandler.Chat/ExtractUint32KeyFromLink, ChatHandler.Chat/PSendSysMessage, Map.Main/GetPersistentState, MapPersistentStateMgr/GetSpawnedPoolData, PoolManager/GetPoolTemplate, PoolManager/GetSpawnedObjects, WorldObject.Object/GetMap, WorldSession.Main/GetPlayer | — | — |
| HandlePoolSpawnsCommand | method | ChatHandler.Chat/ExtractUint32KeyFromLink, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Map.Main/GetPersistentState, MapEntry/Instanceable, MapPersistentStateMgr/GetMapEntry, MapPersistentStateMgr/GetMapId, MapPersistentStateMgr/GetSpawnedPoolData, ObjectMgr/GetCreatureData, ObjectMgr/GetCreatureTemplate, ObjectMgr/GetGameObjectTemplate, ObjectMgr/GetGOData, PoolManager/IsPartOfAPool, PoolManager/IsPartOfAPool#2, SpawnedPoolData/GetSpawnedCreatures, SpawnedPoolData/GetSpawnedGameobjects, WorldObject.Object/GetMap, WorldSession.Main/GetPlayer | — | — |
| HandlePoolInfoCommand | method | ChatHandler.Chat/ExtractUint32KeyFromLink, ChatHandler.Chat/GetMangosString, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, Map.Main/GetPersistentState, MapPersistentStateMgr/GetSpawnedPoolData, ObjectMgr/GetCreatureData, ObjectMgr/GetCreatureTemplate, ObjectMgr/GetGameObjectTemplate, ObjectMgr/GetGOData, PoolManager/GetPoolCreatures, PoolManager/GetPoolGameObjects, PoolManager/GetPoolPools, PoolManager/GetPoolTemplate, PoolManager/IsPartOfAPool#3, PoolTemplateData/IsAutoSpawn, SpawnedPoolData/GetSpawnedCreatures, SpawnedPoolData/GetSpawnedGameobjects, SpawnedPoolData/GetSpawnedPools, WorldObject.Object/GetMap, WorldSession.Main/GetPlayer | — | — |
| ShowTriggerTargetListHelper | method | ChatHandler.Chat/GetMangosString, ChatHandler.Chat/PSendSysMessage#2, WorldObject.Object/GetDistance2d#2, WorldSession.Main/GetPlayer | — | — |
| ShowTriggerListHelper | method | ChatHandler.Chat/GetMangosString, ChatHandler.Chat/PSendSysMessage#2, ObjectMgr/GetAreaTriggerTeleport, ObjectMgr/GetQuestForAreaTrigger, ObjectMgr/IsTavernAreaTrigger, WorldObject.Object/GetDistance2d#4, WorldSession.Main/GetPlayer | — | — |
| HandleTriggerCommand | method | ChatHandler.Chat/ExtractUint32KeyFromLink, ChatHandler.Chat/GetSessionDbLocaleIndex, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, ChatHandler.LookupCommands/ShowQuestListHelper, ObjectMgr/GetAreaTrigger, ObjectMgr/GetAreaTriggersMap, ObjectMgr/GetAreaTriggerTeleport, ObjectMgr/GetQuestForAreaTrigger, WorldObject.Object/GetMapId, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldSession.Main/GetPlayer | — | — |
| HandleTriggerActiveCommand | method | ChatHandler.Chat/SendSysMessage#2, ObjectMgr/GetAreaTriggersMap, ObjectMgr/IsPointInAreaTriggerZone, WorldObject.Object/GetMapId, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldSession.Main/GetPlayer | — | — |
| HandleTriggerNearCommand | method | ChatHandler.Chat/SendSysMessage#2, ObjectMgr/GetAreaTriggersMap, ObjectMgr/GetAreaTriggerTeleport, WorldObject.Object/GetMapId, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldSession.Main/GetPlayer | — | — |
| HandleCinematicAddWpCommand | method | ChatHandler.Chat/PSendSysMessage, Database/PExecute#2, ObjectMgr/LoadCinematicsWaypoints, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldSession.Main/GetPlayer | — | cinematic_waypoints |
| HandleCinematicGoTimeCommand | method | ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SetSentErrorMessage, ObjectMgr/GetCinematicPosition, Player.Main/TeleportTo, WorldObject.Object/GetMapId, WorldSession.Main/GetPlayer | — | — |
| HandleCinematicListWpCommand | method | — | — | — |
| HandleBGStatusCommand | method | BattleGround/GetMaxLevel, BattleGround/GetMinLevel, BattleGround/GetName, BattleGround/GetPlayers, BattleGround/GetStartTime, BattleGround/GetStatus, BattleGroundMgr/BgQueueTypeId, BattleGroundMgr/GetBattleGroundsBegin, BattleGroundMgr/GetBattleGroundsEnd, BattleGroundMgr/GetBattleGroundTemplate, ChatHandler.Chat/playerLink, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, Errors/PrintStacktraceAndThrow, ObjectMgr/GetPlayerNameByGUID, shared_Util/secsToTimeString, WorldSession.Main/GetPlayer | — | — |
| HandleBGStartCommand | method | BattleGround/GetInstanceID, BattleGround/GetName, BattleGround/SetStartDelayTime, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SetSentErrorMessage, Errors/PrintStacktraceAndThrow, Player.Main/GetBattleGround, WorldSession.Main/GetPlayer | — | — |
| HandleBGStopCommand | method | BattleGround/GetInstanceID, BattleGround/GetName, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SetSentErrorMessage, Errors/PrintStacktraceAndThrow, game_Battlegrounds_BattleGround/StopBattleGround, Player.Main/GetBattleGround, WorldSession.Main/GetPlayer | — | — |
| HandleBGCustomCommand | method | ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SetSentErrorMessage, Errors/PrintStacktraceAndThrow, game_Battlegrounds_BattleGround/HandleCommand, Player.Main/GetBattleGround, WorldSession.Main/GetPlayer | — | — |
| HandleLinkGraveCommand | method | AreaEntry/GetById, AreaEntry/IsZone, ChatHandler.Chat/ExtractLiteralArg, ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, ObjectMgr/AddGraveYardLink, WorldObject.Object/GetZoneId, WorldSession.Main/GetPlayer | — | — |
| HandleNearGraveCommand | method | ChatHandler.Chat/GetMangosString, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, ObjectMgr/FindGraveYardData, ObjectMgr/GetClosestGraveYard, WorldObject.Object/GetMapId, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/GetZoneId, WorldSession.Main/GetPlayer | — | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `account`: id int(11) unsigned PK, username varchar(32), gmlevel tinyint(3) unsigned, sessionkey longtext?, v longtext?, s longtext?, token_key varchar(100), email text?, joindate timestamp, last_ip varchar(30), failed_logins int(11) unsigned, locked tinyint(3) unsigned, lock_country varchar(2), last_login timestamp, online tinyint(4), expansion tinyint(3) unsigned, mutetime bigint(40), locale tinyint(3) unsigned, os varchar(4), platform varchar(4), current_realm tinyint(3) unsigned, flags int(10) unsigned, security varchar(255)?, email_verif tinyint(1), geolock_pin int(11)?
- `cinematic_waypoints`: cinematic int(11) unsigned?, timer int(11) unsigned?, position_x float?, position_y float?, position_z float?, comment varchar(255)?

*`?` = nullable, `PK` = primary key column.*


---

<!-- verify: boundary-bleed | foreign: ChatHandler -->
