# Bot Grouping & Questing

<!-- aliases: bot grouping, bot questing, bots level up, bot party, bot behavior -->
<!-- documentation: model-written from source via the local LLM; review before trusting -->

Bot grouping and questing in VMaNGOS rely on the **SuperUiBots** subsystem, which splits decision-making between an external C# service (the "brain") and the in-process C++ server AI (`AiBotAI`). The C++ side acts as a high-fidelity executor: it maintains a persistent TCP bridge to the C# service, receives JSON commands for movement, combat, and social interactions, and pushes back real-time state snapshots. Grouping is handled by the C# service issuing `FORM_GROUP` directives, which the C++ bridge executes by creating a `Group` object and adding specified bot GUIDs. Questing flows similarly: the C# service plans the route and objectives, sending `QUEST_INTERACT` commands to accept or turn in quests, and `MOVE_TO` or `SET_TASK` commands to navigate to NPCs or grind areas. The C++ AI handles the granular execution—finding the specific NPC by entry, validating quest requirements, managing inventory for rewards, and executing the grind logic (target selection, patrol, and combat) locally to ensure responsiveness and adherence to server-side constraints like line-of-sight and threat mechanics.

The communication channel is established via **AiBotAI.Bridge/BridgeConnect**, which opens a non-blocking socket to the configured bridge host. Once connected, **AiBotAI.Bridge/BridgeSendHello** identifies the bot to the C# service. The C# service then drives the bot's behavior by sending JSON lines, which are parsed by **AiBotAI.Bridge/BridgeRecv** and dispatched by **AiBotAI.Bridge/BridgeProcessLine** to specific handlers. For grouping, **AiBotAI.Bridge/BridgeHandleFormGroup** parses a list of member GUIDs, creates a new group with the bot as leader, sets the loot method to `NEED_BEFORE_GREED`, and adds the members. For questing, **AiBotAI.Bridge/BridgeHandleQuestInteract** manages the lifecycle of a quest: it locates the quest-giver NPC within 15 yards, checks if the bot can accept or complete the quest using server-side validation (`CanTakeQuest`, `CanAddQuest`), and executes the transaction. During the quest, the bot may enter a grind phase initiated by **AiBotAI.Bridge/BridgeHandleSetTask**. The grind logic, implemented in `AiBotAIGrind.cpp`, uses **AiBotAI.Grind/SelectGrindTarget** to find valid enemies based on level bands and proximity, prioritizing isolated targets to avoid overpulls. If no targets are available, **AiBotAI.Grind/DoGrindPatrol** generates random movement within the grind radius. Throughout this process, **AiBotAI.Bridge/BridgeSendState** periodically pushes a comprehensive snapshot of the bot's health, position, inventory, and active quest log to the C# service, allowing the brain to make informed decisions about the next action.

## How to Modify

### Config
Three configuration keys directly influence bot behavior and performance:
*   **PartyBot.MaxBots** (default `0`): Controls the maximum number of bots allowed in a party. Setting this to `0` disables the feature or imposes no limit depending on implementation specifics, but typically `0` implies disabled or unrestricted in some contexts; verify with server logs. Non-zero values cap the party size.
*   **PartyBot.SkipChecks** (default `0`): When enabled (`1`), this likely bypasses certain validation checks for bot party formation, potentially allowing bots to join parties that would otherwise be restricted (e.g., level differences, faction restrictions). Use with caution as it may lead to unstable states.
*   **PlayerBot.UpdateMs** (default `1000`): Defines the interval in milliseconds at which the bot AI updates its state and processes commands. Lower values increase responsiveness but consume more CPU; higher values reduce server load but may make bots appear sluggish. The default `1000` ms provides a balance for most realms.

### Database
No specific database tables or columns are exposed in the provided schema for modifying bot grouping or questing logic directly. The bot's behavior is driven by the C# service's decision-making and the C++ AI's execution of standard server mechanics (quests, groups, combat). Any changes to quest availability, NPC locations, or group restrictions must be made through standard world database edits (e.g., `creature_template`, `quest_template`) and will affect bots as they do players.

### Code
For changes not covered by config or database:
*   **Grouping Logic**: Edit **AiBotAI.Bridge/BridgeHandleFormGroup** in `AiBotAIBridge.cpp` to modify how groups are created, such as changing the default loot method or adding pre-conditions for joining.
*   **Quest Interaction**: Edit **AiBotAI.Bridge/BridgeHandleQuestInteract** in `AiBotAIBridge.cpp` to alter quest acceptance/completion logic, such as adding custom checks or modifying reward handling.
*   **Grind Behavior**: Edit **AiBotAI.Grind/SelectGrindTarget** in `AiBotAIGrind.cpp` to change target selection priorities, level bands, or aggression thresholds. Edit **AiBotAI.Grind/DoGrindPatrol** to modify idle movement patterns.
*   **Bridge Communication**: Edit **AiBotAI.Bridge/BridgeSendState** in `AiBotAIBridge.cpp` to add or remove data fields sent to the C# service, influencing the brain's decision-making capabilities.

## Path Reference

**AiBotAI.Bridge/BridgeConnect** (AiBotAIBridge.cpp): Establishes the TCP connection to the C# brain service, setting up non-blocking I/O and handling reconnection logic with exponential backoff.

**AiBotAI.Bridge/BridgeDisconnect** (AiBotAIBridge.cpp): Closes the socket connection, resets bridge state flags, and clears send/receive buffers to prepare for a future reconnection.

**AiBotAI.Bridge/BridgeSend** (AiBotAIBridge.cpp): Queues a JSON command string to the send buffer, enforcing a size limit by dropping oldest data if the buffer overflows, and triggers a flush.

**AiBotAI.Bridge/BridgeFlush** (AiBotAIBridge.cpp): Drains the send buffer to the socket, handling partial writes and non-blocking errors (EWOULDBLOCK) to ensure reliable transmission.

**AiBotAI.Bridge/BridgeSendHello** (AiBotAIBridge.cpp): Sends an initial identification message to the C# service containing the bot's GUID, name, race, class, level, and position.

**AiBotAI.Bridge/BridgeSendState** (AiBotAIBridge.cpp): Periodically broadcasts a comprehensive snapshot of the bot's state (health, mana, position, inventory, quest log, durability) to the C# service for decision-making.

**AiBotAI.Bridge/BridgeSendEvent** (AiBotAIBridge.cpp): Sends discrete event notifications (e.g., quest completed, item sold) to the C# service, allowing the brain to react to specific outcomes.

**AiBotAI.Bridge/BridgeRecv** (AiBotAIBridge.cpp): Reads incoming data from the socket, parses complete JSON lines, and passes them to the processor while handling partial reads and connection closures.

**AiBotAI.Bridge/JsonExtractFloat** (AiBotAIBridge.cpp): Utility function to parse a floating-point value from a JSON string by key name.

**AiBotAI.Bridge/JsonExtractInt** (AiBotAIBridge.cpp): Utility function to parse an integer value from a JSON string by key name.

**AiBotAI.Bridge/JsonExtractString** (AiBotAIBridge.cpp): Utility function to parse a string value from a JSON string by key name.

**AiBotAI.Bridge/BridgeProcessLine** (AiBotAIBridge.cpp): Dispatches incoming JSON commands to the appropriate handler method based on the "type" field (e.g., "FORM_GROUP", "QUEST_INTERACT").

**AiBotAI.Bridge/BridgeHandleTeleport** (AiBotAIBridge.cpp): Executes a same-map teleport command, validating safety caps and grounding the Z coordinate before moving the bot.

**AiBotAI.Bridge/BridgeHandleMoveTo** (AiBotAIBridge.cpp): Directs the bot to move to specified coordinates, applying arrival jitter to avoid navigation mesh issues and supporting objective-enriched movements.

**AiBotAI.Bridge/BridgeHandleSayText** (AiBotAIBridge.cpp): Handles chat output commands (Say, Yell, Whisper, Channel), constructing and sending the appropriate chat packets.

**AiBotAI.Bridge/BridgeHandleQuestInteract** (AiBotAIBridge.cpp): Manages quest acceptance and completion, locating the NPC, validating requirements, and processing rewards.

**AiBotAI.Bridge/BridgeHandleAbandonQuest** (AiBotAIBridge.cpp): Removes a specified quest from the bot's quest log and updates the tracked quest ID.

**AiBotAI.Bridge/BridgeHandleLearnSpell** (AiBotAIBridge.cpp): Forces the bot to learn a specific spell ID if it is not already known.

**AiBotAI.Bridge/BridgeHandleTrain** (AiBotAIBridge.cpp): Automates spell training at an NPC, learning all affordable green spells and deducting the cost.

**AiBotAI.Bridge/BridgeHandleQueryQuestStatus** (AiBotAIBridge.cpp): Responds to a query from the C# service with a full snapshot of the bot's active quest log.

**AiBotAI.Bridge/BridgeHandleAttackTarget** (AiBotAIBridge.cpp): Initiates combat with a specific creature GUID, validating that the target is hostile and valid.

**AiBotAI.Bridge/BridgeHandleInteractNpc** (AiBotAIBridge.cpp): Triggers interaction with an NPC, moving the bot closer if necessary and facing the NPC.

**AiBotAI.Bridge/BridgeHandleSetTask** (AiBotAIBridge.cpp): Sets the bot's internal task state to GRIND or IDLE, configuring grind parameters like center coordinates and radius.

**AiBotAI.Bridge/BridgeHandleCombatDirective** (AiBotAIBridge.cpp): Applies group combat directives, such as assisting a specific anchor target for coordinated focus-fire.

**AiBotAI.Bridge/BridgeHandleTakeFlight** (AiBotAIBridge.cpp): Activates a flight path between taxi nodes, validating cost and path existence before initiating travel.

**AiBotAI.Bridge/BridgeHandleSellItems** (AiBotAIBridge.cpp): Vends unwanted items to a vendor, protecting quest items, high-quality gear, and consumables based on configurable thresholds.

**AiBotAI.Bridge/BridgeHandleRepairItems** (AiBotAIBridge.cpp): Repairs all equipped gear at an NPC, checking for damage and deducting the repair cost.

**AiBotAI.Bridge/BridgeHandleUseGameObject** (AiBotAIBridge.cpp): Interacts with a Game Object, looting items and gold, auto-equipping loot, and despawning the object.

**AiBotAI.Bridge/BridgeHandleFormGroup** (AiBotAIBridge.cpp): Creates a new group with the bot as leader, adds specified member GUIDs, and sets the loot method to NEED_BEFORE_GREED.

**AiBotAI.Bridge/BridgeHandleDisbandGroup** (AiBotAIBridge.cpp): Disbands the current group if the bot is a member.

**AiBotAI.Bridge/BridgeHandleResurrect** (AiBotAIBridge.cpp): Handles resurrection, teleporting the ghost to a safe graveyard location if stuck in a death loop, then reviving the bot.

**AiBotAI.Bridge/SendChatRecvEvent** (AiBotAIBridge.cpp): Emits a CHAT_RECV event to the C# service when the bot receives a chat message, including sender, message, and chat type.

**AiBotAI.Grind/CountNearbyHostiles** (AiBotAIGrind.cpp): Counts alive, hostile, untapped creatures within a radius of a candidate to prevent overpulls during target selection.

**AiBotAI.Grind/AiBotGrayLevel** (AiBotAIGrind.cpp): Calculates the minimum creature level that yields XP for a given player level, ensuring bots do not waste time on grey mobs.

**AiBotAI.Grind/SelectGrindTarget** (AiBotAIGrind.cpp): Primary target selector for grind tasks, prioritizing aggroed targets, then objective-specific mobs, and finally indefinite XP mobs within preferred level bands.

**AiBotAI.Grind/DoGrindPatrol** (AiBotAIGrind.cpp): Generates random idle movement within the grind radius when no valid targets are available, snapping to terrain.

**AiBotAI.Grind/ScanApproachTarget** (AiBotAIGrind.cpp): Scans for valid quest-related targets while moving to a location, building a union of valid entries from the current task and incomplete quests.

**AiBotAI.Grind/ConvertMoveToGrindInPlace** (AiBotAIGrind.cpp): Transitions the bot from movement to grind mode by re-centering the task area on the bot's current position.

**ChatHandler.PlayerBotMgr/LoadConfig** (PlayerBotMgr.cpp): Reads configuration values related to bot management, including `PlayerBot.UpdateMs`, from the server config file.

**World/LoadConfigSettings** (World.cpp): Loads and validates server configuration settings, including `PartyBot.MaxBots`, from the config file during startup or reload.

---

<!-- machine-true, projected from graph.json -->

## Map — Bot Grouping & Questing

*Source:* AiBotAIBridge.cpp, AiBotAIGrind.cpp, PlayerBotMgr.cpp, World.cpp
*Config keys:* PartyBot.MaxBots (default 0), PartyBot.SkipChecks (default 0), PlayerBot.UpdateMs (default 1000)
*Tables:* —

| Member | Kind | Source | Role |
|---|---|---|---|
| AiBotAI.Bridge/BridgeConnect | method | AiBotAIBridge.cpp:51-98 | seed — AiBotAI.Bridge/* |
| AiBotAI.Bridge/BridgeDisconnect | method | AiBotAIBridge.cpp:101-112 | seed — AiBotAI.Bridge/* |
| AiBotAI.Bridge/BridgeSend | method | AiBotAIBridge.cpp:114-143 | seed — AiBotAI.Bridge/* |
| AiBotAI.Bridge/BridgeFlush | method | AiBotAIBridge.cpp:145-186 | seed — AiBotAI.Bridge/* |
| AiBotAI.Bridge/BridgeSendHello | method | AiBotAIBridge.cpp:188-222 | seed — AiBotAI.Bridge/* |
| AiBotAI.Bridge/BridgeSendState | method | AiBotAIBridge.cpp:251-408 | seed — AiBotAI.Bridge/* |
| AiBotAI.Bridge/BridgeSendEvent | method | AiBotAIBridge.cpp:410-422 | seed — AiBotAI.Bridge/* |
| AiBotAI.Bridge/BridgeRecv | method | AiBotAIBridge.cpp:424-482 | seed — AiBotAI.Bridge/* |
| AiBotAI.Bridge/JsonExtractFloat | function | AiBotAIBridge.cpp:486-497 | seed — AiBotAI.Bridge/* |
| AiBotAI.Bridge/JsonExtractInt | function | AiBotAIBridge.cpp:499-509 | seed — AiBotAI.Bridge/* |
| AiBotAI.Bridge/JsonExtractString | function | AiBotAIBridge.cpp:511-524 | seed — AiBotAI.Bridge/* |
| AiBotAI.Bridge/BridgeProcessLine | method | AiBotAIBridge.cpp:526-578 | seed — AiBotAI.Bridge/* |
| AiBotAI.Bridge/BridgeHandleTeleport | method | AiBotAIBridge.cpp:602-705 | seed — AiBotAI.Bridge/* |
| AiBotAI.Bridge/BridgeHandleMoveTo | method | AiBotAIBridge.cpp:735-844 | seed — AiBotAI.Bridge/* |
| AiBotAI.Bridge/BridgeHandleSayText | method | AiBotAIBridge.cpp:847-922 | seed — AiBotAI.Bridge/* |
| AiBotAI.Bridge/BridgeHandleQuestInteract | method | AiBotAIBridge.cpp:928-1163 | seed — AiBotAI.Bridge/* |
| AiBotAI.Bridge/BridgeHandleAbandonQuest | method | AiBotAIBridge.cpp:1165-1191 | seed — AiBotAI.Bridge/* |
| AiBotAI.Bridge/BridgeHandleLearnSpell | method | AiBotAIBridge.cpp:1193-1214 | seed — AiBotAI.Bridge/* |
| AiBotAI.Bridge/BridgeHandleTrain | method | AiBotAIBridge.cpp:1216-1392 | seed — AiBotAI.Bridge/* |
| AiBotAI.Bridge/BridgeHandleQueryQuestStatus | method | AiBotAIBridge.cpp:1408-1458 | seed — AiBotAI.Bridge/* |
| AiBotAI.Bridge/BridgeHandleAttackTarget | method | AiBotAIBridge.cpp:1461-1495 | seed — AiBotAI.Bridge/* |
| AiBotAI.Bridge/BridgeHandleInteractNpc | method | AiBotAIBridge.cpp:1497-1537 | seed — AiBotAI.Bridge/* |
| AiBotAI.Bridge/BridgeHandleSetTask | method | AiBotAIBridge.cpp:1539-1593 | seed — AiBotAI.Bridge/* |
| AiBotAI.Bridge/BridgeHandleCombatDirective | method | AiBotAIBridge.cpp:1613-1640 | seed — AiBotAI.Bridge/* |
| AiBotAI.Bridge/BridgeHandleTakeFlight | method | AiBotAIBridge.cpp:1642-1774 | seed — AiBotAI.Bridge/* |
| AiBotAI.Bridge/BridgeHandleSellItems | method | AiBotAIBridge.cpp:1790-2026 | seed — AiBotAI.Bridge/* |
| AiBotAI.Bridge/BridgeHandleRepairItems | method | AiBotAIBridge.cpp:2044-2140 | seed — AiBotAI.Bridge/* |
| AiBotAI.Bridge/BridgeHandleUseGameObject | method | AiBotAIBridge.cpp:2143-2260 | seed — AiBotAI.Bridge/* |
| AiBotAI.Bridge/BridgeHandleFormGroup | method | AiBotAIBridge.cpp:2270-2392 | seed — AiBotAI.Bridge/* |
| AiBotAI.Bridge/BridgeHandleDisbandGroup | method | AiBotAIBridge.cpp:2400-2416 | seed — AiBotAI.Bridge/* |
| AiBotAI.Bridge/BridgeHandleResurrect | method | AiBotAIBridge.cpp:2418-2564 | seed — AiBotAI.Bridge/* |
| AiBotAI.Bridge/SendChatRecvEvent | method | AiBotAIBridge.cpp:2612-2635 | seed — AiBotAI.Bridge/* |
| AiBotAI.Grind/CountNearbyHostiles | method | AiBotAIGrind.cpp:58-95 | seed — AiBotAI.Grind/* |
| AiBotAI.Grind/AiBotGrayLevel | function | AiBotAIGrind.cpp:120-126 | seed — AiBotAI.Grind/* |
| AiBotAI.Grind/SelectGrindTarget | method | AiBotAIGrind.cpp:128-321 | seed — AiBotAI.Grind/* |
| AiBotAI.Grind/DoGrindPatrol | method | AiBotAIGrind.cpp:324-344 | seed — AiBotAI.Grind/* |
| AiBotAI.Grind/ScanApproachTarget | method | AiBotAIGrind.cpp:385-454 | seed — AiBotAI.Grind/* |
| AiBotAI.Grind/ConvertMoveToGrindInPlace | method | AiBotAIGrind.cpp:467-481 | seed — AiBotAI.Grind/* |
| ChatHandler.PlayerBotMgr/LoadConfig | method | PlayerBotMgr.cpp:51-64 | seed — reads config PlayerBot.UpdateMs |
| World/LoadConfigSettings | method | World.cpp:440-1245 | seed — reads config PartyBot.MaxBots |
