<!-- provenance: boundary-bleed -->
# WorldSession.QuestHandler

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# WorldSession.QuestHandler

## Purpose & Responsibilities

The `WorldSession.QuestHandler` partial implements the server-side logic for processing client-to-server network packets related to quests. It resides within the `WorldSession` class, which represents a single player's connection to the game world. This unit is responsible for validating quest interactions, enforcing game rules (such as distance checks, level requirements, and party sharing constraints), and coordinating with other subsystems (`Player`, `ObjectMgr`, `ScriptMgr`) to update quest states, award rewards, and manage gossip interfaces.

It handles the entire lifecycle of a quest interaction from the client's perspective: querying quest availability, initiating dialogue with NPCs, accepting quests, tracking progress, completing objectives, and claiming rewards. It also manages specific mechanics like party quest sharing and localized quest text retrieval.

## Member-by-Member Behavior

### Quest Status and Dialogue Initialization

**`HandleQuestgiverStatusQueryOpcode`**
Determines the visual icon (exclamation mark, question mark, etc.) displayed above an NPC or GameObject. It first validates that the target object exists and is not hostile to the player. It delegates the initial status determination to `ScriptMgr.GetDialogStatus`. If the script manager returns a status code greater than 6 (indicating a need for deeper server-side evaluation), it calls `WorldSession.GetDialogStatus` to compute the final status based on quest relations and player state. Finally, it sends the resulting status back to the client via `GossipDef.SendQuestGiverStatus`.

**`HandleQuestgiverHelloOpcode`**
Initiates the gossip interface when a player interacts with an NPC. It performs several preparatory actions:
1.  Validates the NPC using `Player.GetNPCIfCanInteractWith`.
2.  Removes "fake death" states from the player if present.
3.  Pauses the NPC's out-of-combat movement unless it has the `CREATURE_FLAG_EXTRA_NO_MOVEMENT_PAUSE` flag.
4.  Interrupts any spells the player is channeling that are cancelled by interaction.
5.  Checks for custom gossip handling via `ScriptMgr.OnGossipHello`. If scripts handle the greeting, the method returns early.
6.  Otherwise, it prepares and sends the standard gossip menu using `Player.PrepareGossipMenu` and `Player.SendPreparedGossip`.

### Quest Acceptance and Sharing

**`HandleQuestgiverAcceptQuestOpcode`**
Processes the player's attempt to accept a quest. It validates the quest giver object and ensures the player can interact with them. It retrieves the quest template from `ObjectMgr` and checks if the player can take the quest.
If the player has active "quest share info" (meaning they are accepting a quest pushed by a party member), it verifies the distance between the sharer and the acceptor. If valid, it notifies the sharer and clears the share info.
If the quest has the `QUEST_FLAGS_PARTY_ACCEPT` flag, it iterates through the player's group, offering the quest to other eligible members who are in the same map. It sets up quest share info for these members and sends them a confirmation request.
Finally, it adds the quest to the player's log, completes it immediately if possible, closes the gossip window, and casts any source spell associated with the quest.

**`HandleQuestConfirmAccept`**
Handles the response from a party member who was offered a quest via `HandleQuestgiverAcceptQuestOpcode`. It verifies that the quest is party-acceptable, that the original sharer is still online, and that both players are in the same group or raid (depending on quest flags). It checks if the quest is sherable and if the original player currently has the quest. If all conditions are met and the recipient can take the quest, it adds the quest to their log and clears the share info.

**`HandlePushQuestToParty`**
Initiates the process of pushing a quest to party members. It iterates through the player's group, checking each member for eligibility:
1.  Must be within `QUEST_SHARE_DISTANCE`.
2.  Must not have already completed the quest.
3.  Must satisfy quest status prerequisites.
4.  Must be able to take the quest.
5.  Must have space in their quest log.
6.  Must not already be busy with another quest share.
If eligible, it sends the quest details to the member and sets up quest share info, prompting them to confirm acceptance.

**`HandleQuestPushResult`**
Processes the result of a quest push attempt. It retrieves the original sharer from the share info, constructs a response packet containing the result message, and sends it to the sharer. It then clears the share info from the responder's session.

### Quest Information and Details

**`HandleQuestgiverQueryQuestOpcode`**
Retrieves detailed information about a specific quest from a quest giver. It validates that the object is a valid quest giver or involved in the quest. If valid, it fetches the quest template and sends the detailed quest window to the player via `GossipDef.SendQuestGiverQuestDetails`.

**`HandleQuestQueryOpcode`**
Responds to a direct query for quest data, often used for localization or initial loading. It retrieves the quest template and, if a locale index is available, fetches localized strings (title, details, objectives, end text) from `ObjectMgr.GetQuestLocale`. It manually constructs a `WorldPacket` containing all quest fields, including IDs, levels, rewards, requirements, and text strings, handling variable-length string encoding and fixed-size field packing. It supports conditional fields based on client build version (e.g., `RewMoneyMaxLevel`).

### Quest Completion and Rewards

**`HandleQuestgiverChooseRewardOpcode`**
Handles the player selecting a specific reward option for a completed quest. It validates the reward index to prevent hacking. It checks if the player is alive; if dead, it verifies if the NPC is visible to dead players. It calls `Player.CanRewardQuest` to validate eligibility. If valid, it awards the reward via `Player.RewardQuest` and automatically queries the next quest in the chain if one exists. If the reward cannot be given, it re-sends the offer reward window.

**`HandleQuestgiverRequestRewardOpcode`**
Handles the initial request to turn in a completed quest. It validates the quest giver and checks player visibility/alive status similar to the choose reward handler. It attempts to complete the quest if not already done. If the quest status is now complete, it sends the reward offer window to the player.

**`HandleQuestgiverCompleteQuest`**
Handles the client signal that a quest is ready to be turned in. It checks the current quest status. If incomplete, it determines if the quest is repeatable and sends the appropriate "request items" window, indicating whether the player can complete or reward the quest. If already complete, it sends the reward window directly.

### Quest Log Management

**`HandleQuestLogSwapQuest`**
Allows the player to swap two quests in their quest log. It validates the slot indices and delegates the swap to `Player.SwapQuestSlot`.

**`HandleQuestLogRemoveQuest`**
Allows the player to abandon a quest. It delegates the removal to `Player.RemoveQuestAtSlot`.

**`HandleQuestgiverCancel`**
Closes the current gossip window if open.

**`HandleQuestgiverQuestAutoLaunch`**
Currently a no-op placeholder.

### Internal Helper

**`GetDialogStatus`**
Calculates the appropriate dialog status icon for a quest giver. It distinguishes between creatures and game objects to retrieve the correct quest relation maps from `ObjectMgr`.
1.  **Involved Relations (Quest Finishers):** Iterates through quests the NPC finishes. If the player has completed the quest but not rewarded it, or if it's an auto-complete repeatable quest, it sets the status to "Reward". If incomplete, it sets it to "Incomplete".
2.  **Quest Relations (Quest Givers):** Iterates through quests the NPC offers. If the player doesn't have the quest, it checks visibility, level requirements, and configuration settings for hiding low-level quests. It sets statuses like "Available", "Chat" (level gap), or "Unavailable".
It returns the highest priority status found.

## Cross-Unit Boundaries

*   **`Player.Main`**: Heavily utilized for state validation and modification. `WorldSession.QuestHandler` relies on `Player` methods to check if a player can take, add, complete, or reward a quest (`CanTakeQuest`, `AddQuest`, `CompleteQuest`, `RewardQuest`). It also uses `Player` to manage gossip menus, quest logs, and party sharing info.
*   **`ObjectMgr`**: Used to retrieve static quest data (`GetQuestTemplate`, `GetQuestLocale`) and dynamic relation maps (`GetCreatureQuestRelationsMapBounds`, etc.). This is the primary source of truth for quest definitions.
*   **`GossipDef`**: Used to send UI updates to the client, such as opening gossip windows, sending quest details, offering rewards, and closing gossip.
*   **`ScriptMgr`**: Provides hooks for custom quest logic. `GetDialogStatus` and `OnGossipHello` allow scripts to override default behavior.
*   **`Group` / `GroupReference`**: Used to iterate over party members when sharing quests or offering party-accept quests.
*   **`ObjectAccessor`**: Used to find other players by GUID during quest sharing confirmation.
*   **`Log.Main`**: Used for debugging and error reporting when invalid objects or actions are detected.
*   **`Unit.Main` / `Creature.Main`**: Used for basic entity checks like hostility, alive status, visibility, and movement pausing.
*   **`WorldPacket` / `ByteBuffer`**: Used for constructing raw network packets, particularly in `HandleQuestQueryOpcode` where manual packet construction is required for efficiency and compatibility.

## Data Model

This unit does not directly access database tables. All data access is mediated through `ObjectMgr` (which caches data from tables like `quest_template`, `quest_locale`, `creature_questrelation`, etc.) and `Player` (which persists quest progress to the character database). No SQL queries are executed directly in this source file.

## Notable Implementation Details

*   **Manual Packet Construction:** `HandleQuestQueryOpcode` manually constructs the `SMSG_QUEST_QUERY_RESPONSE` packet. This involves calculating the exact buffer size needed for variable-length strings and fixed-size fields, then serializing each field individually. This approach avoids overhead but requires careful maintenance if packet structures change.
*   **Quest Sharing Logic:** The quest sharing mechanism (`HandleQuestgiverAcceptQuestOpcode`, `HandlePushQuestToParty`, `HandleQuestConfirmAccept`) involves a multi-step handshake. The initiator pushes the quest, setting up temporary state (`SetQuestShareInfo`). The recipient must confirm, triggering `HandleQuestConfirmAccept`, which validates the relationship again before adding the quest. This prevents race conditions and ensures both players are aware of the share.
*   **Dead Player Interactions:** Several handlers (`HandleQuestgiverChooseRewardOpcode`, `HandleQuestgiverRequestRewardOpcode`) include special logic to allow dead players to interact with certain NPCs if those NPCs are flagged as visible to dead players (`IsVisibleForDead`). This supports quests that can be turned in at graveyards or spirit healers.
*   **Script Integration:** The handler checks `ScriptMgr` early in the process (e.g., `OnGossipHello`). If a script returns true, the default behavior is skipped. This allows for extensive customization of quest interactions without modifying core code.
*   **Level Hiding Configuration:** `GetDialogStatus` respects the `CONFIG_INT32_QUEST_LOW_LEVEL_HIDE_DIFF` configuration. If a player is significantly higher level than the quest, the quest may be hidden or marked differently, depending on the config value.
*   **Party Accept Flag:** Quests with `QUEST_FLAGS_PARTY_ACCEPT` trigger automatic distribution to group members upon acceptance by one member. This logic iterates through the group and sets up share info for eligible members, streamlining party questing.

## Member Reference

**`HandleQuestgiverStatusQueryOpcode`**: Determines the dialog icon status for a quest giver by checking hostility, delegating to `ScriptMgr`, and falling back to `GetDialogStatus` if needed, then sending the result to the client.

**`HandleQuestgiverHelloOpcode`**: Initiates gossip interaction by validating the NPC, removing fake death, pausing NPC movement, interrupting spells, checking scripts, and preparing/sending the gossip menu.

**`HandleQuestgiverAcceptQuestOpcode`**: Processes quest acceptance, validating the giver, handling quest sharing from parties, distributing party-accept quests to group members, adding the quest, completing it if possible, and casting source spells.

**`HandleQuestgiverQueryQuestOpcode`**: Retrieves and sends detailed quest information to the player after validating the quest giver's involvement with the quest.

**`HandleQuestQueryOpcode`**: Manually constructs and sends a raw packet containing comprehensive quest data, including localized text if available, supporting various client builds.

**`HandleQuestgiverChooseRewardOpcode`**: Handles reward selection, validating the choice, checking player visibility/alive status, awarding the reward, and querying the next quest in the chain.

**`HandleQuestgiverRequestRewardOpcode`**: Handles the initial request to turn in a quest, completing it if necessary, and sending the reward offer window if the quest is complete.

**`HandleQuestgiverCancel`**: Closes the current gossip window.

**`HandleQuestLogSwapQuest`**: Swaps two quests in the player's log after validating slot indices.

**`HandleQuestLogRemoveQuest`**: Abandons a quest from the player's log at the specified slot.

**`HandleQuestConfirmAccept`**: Confirms a quest share from a party member, validating group membership, quest flags, and eligibility before adding the quest to the recipient's log.

**`HandleQuestgiverCompleteQuest`**: Signals quest completion readiness, sending the appropriate "request items" or reward window based on quest status and repeatability.

**`HandleQuestgiverQuestAutoLaunch`**: Placeholder method with no current implementation.

**`HandlePushQuestToParty`**: Pushes a quest to eligible party members by checking distance, completion status, prerequisites, and log space, then setting up share info for confirmation.

**`HandleQuestPushResult`**: Sends the result of a quest push attempt back to the original sharer and clears the share info.

**`GetDialogStatus`**: Calculates the dialog status icon by iterating through quest relations and involved relations, checking player status, level requirements, and configuration settings to determine the highest priority status.

---

<!-- machine-true, projected from graph.json -->

## Map — WorldSession.QuestHandler

*Source:* QuestHandler.cpp, WorldSession.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| HandleQuestgiverStatusQueryOpcode | method | GossipDef/SendQuestGiverStatus, Log.Main/Out, Object/GetTypeId, ObjectGuid/GetString, Player.Main/GetObjectByTypeMask, ScriptMgr/GetDialogStatus, ScriptMgr/GetDialogStatus#2, Unit.Main/IsHostileTo | — | — |
| HandleQuestgiverHelloOpcode | method | Creature.Main/GetDefaultGossipMenuId, Creature.Main/HasExtraFlag, Creature.MotionMaster/PauseOutOfCombatMovement, Log.Main/Out, ObjectGuid/GetString, Player.Main/GetNPCIfCanInteractWith, Player.Main/PrepareGossipMenu, Player.Main/SendPreparedGossip, ScriptMgr/OnGossipHello, SpellCaster/InterruptSpellsWithChannelFlags, Unit.Main/HasUnitState, Unit.Main/RemoveAurasWithInterruptFlags, Unit.Main/RemoveSpellsCausingAura, WorldSession.Main/GetPlayer | — | — |
| HandleQuestgiverAcceptQuestOpcode | method | GossipDef/CloseGossip, Group/GetFirstMember, GroupReference/next, Map.Main/GetPlayer, Object/GetObjectGuid, Object/GetTypeId, Object/HasQuest, ObjectMgr/GetQuestTemplate, Player.Main/AddQuest, Player.Main/CanAddQuest, Player.Main/CanCompleteQuest, Player.Main/CanInteractWithQuestGiver, Player.Main/CanShareQuest, Player.Main/CanTakeQuest, Player.Main/ClearQuestShareInfo, Player.Main/CompleteQuest, Player.Main/GetGroup, Player.Main/GetObjectByTypeMask, Player.Main/GetQuestShareInfo, Player.Main/SendPushToPartyResponse, Player.Main/SendQuestConfirmAccept, Player.Main/SetQuestShareInfo, QuestDef/GetQuestId, QuestDef/GetSrcSpell, QuestDef/HasQuestFlag, SpellCaster/CastSpell#2, WorldObject.Object/GetMap, WorldObject.Object/IsInMap, WorldObject.Object/IsWithinDist, WorldSession.Main/GetPlayer | — | — |
| HandleQuestgiverQueryQuestOpcode | method | GossipDef/CloseGossip, GossipDef/SendQuestGiverQuestDetails, Object/GetObjectGuid, Object/HasInvolvedQuest, Object/HasQuest, ObjectMgr/GetQuestTemplate, Player.Main/GetObjectByTypeMask | — | — |
| HandleQuestQueryOpcode | method | ByteBuffer/append#4, ByteBuffer/operator<<#10, ByteBuffer/operator<<#4, ByteBuffer/operator<<#9, ObjectMgr/GetQuestLocale, ObjectMgr/GetQuestTemplate, QuestDef/GetDetails, QuestDef/GetEndText, QuestDef/GetNextQuestInChain, QuestDef/GetObjectives, QuestDef/GetPointMapId, QuestDef/GetPointOpt, QuestDef/GetPointX, QuestDef/GetPointY, QuestDef/GetQuestFlags, QuestDef/GetQuestId, QuestDef/GetQuestLevel, QuestDef/GetQuestMethod, QuestDef/GetRepObjectiveFaction, QuestDef/GetRepObjectiveValue, QuestDef/GetRewMoneyMaxLevel, QuestDef/GetRewOrReqMoney, QuestDef/GetRewSpell, QuestDef/GetSrcItemId, QuestDef/GetTitle, QuestDef/GetType, QuestDef/GetZoneOrSort, QuestDef/HasQuestFlag, WorldPacket/WorldPacket#4, WorldSession.Main/GetSessionDbLocaleIndex, WorldSession.Main/SendPacket | — | — |
| HandleQuestgiverChooseRewardOpcode | method | GossipDef/SendQuestGiverOfferReward, GossipDef/SendQuestGiverQuestDetails, Log.Main/Out, Object/GetGUIDLow, Object/HasInvolvedQuest, Object/ToCreature, ObjectMgr/GetQuestTemplate, Player.Main/CanRewardQuest#2, Player.Main/GetName, Player.Main/GetNextQuest, Player.Main/GetObjectByTypeMask, Player.Main/RewardQuest, Unit.Main/IsAlive, Unit.Main/IsVisibleForDead, WorldSession.Main/GetPlayer | — | — |
| HandleQuestgiverRequestRewardOpcode | method | GossipDef/SendQuestGiverOfferReward, Object/HasInvolvedQuest, Object/ToCreature, ObjectMgr/GetQuestTemplate, Player.Main/CanCompleteQuest, Player.Main/CompleteQuest, Player.Main/GetObjectByTypeMask, Player.Main/GetQuestStatus, Unit.Main/IsAlive, Unit.Main/IsInvisibleForAlive, WorldSession.Main/GetPlayer | — | — |
| HandleQuestgiverCancel | method | GossipDef/CloseGossip | — | — |
| HandleQuestLogSwapQuest | method | Player.Main/SwapQuestSlot, WorldSession.Main/GetPlayer | — | — |
| HandleQuestLogRemoveQuest | method | Player.Main/RemoveQuestAtSlot | — | — |
| HandleQuestConfirmAccept | method | ObjectAccessor/FindPlayer, ObjectMgr/GetQuestTemplate, Player.Main/AddQuest, Player.Main/CanAddQuest, Player.Main/CanTakeQuest, Player.Main/ClearQuestShareInfo, Player.Main/GetQuestShareInfo, Player.Main/IsCurrentQuest, Player.Main/IsInSameGroupWith, Player.Main/IsInSameRaidWith, QuestDef/HasQuestFlag, QuestDef/IsAllowedInRaid | — | — |
| HandleQuestgiverCompleteQuest | method | GossipDef/SendQuestGiverRequestItems, ObjectMgr/GetQuestTemplate, Player.Main/CanCompleteRepeatableQuest, Player.Main/CanRewardQuest, Player.Main/GetQuestStatus, QuestDef/IsRepeatable | — | — |
| HandleQuestgiverQuestAutoLaunch | method | — | — | — |
| HandlePushQuestToParty | method | GossipDef/SendQuestGiverQuestDetails, Group/GetFirstMember, GroupReference/next, Object/GetObjectGuid, ObjectMgr/GetQuestTemplate, Player.Main/CanTakeQuest, Player.Main/GetGroup, Player.Main/GetQuestShareInfo, Player.Main/GetQuestStatus, Player.Main/SatisfyQuestLog, Player.Main/SatisfyQuestStatus, Player.Main/SendPushToPartyResponse, Player.Main/SetQuestShareInfo, QuestDef/GetQuestId, WorldObject.Object/IsWithinDist | — | — |
| HandleQuestPushResult | method | ByteBuffer/operator<<#7, Object/GetObjectGuid, ObjectAccessor/FindPlayer, ObjectGuid/operator<<, Player.Main/ClearQuestShareInfo, Player.Main/GetQuestShareInfo, Player.Main/GetSession, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| GetDialogStatus | method | Log.Main/Out, Object/GetEntry, Object/GetTypeId, ObjectMgr/GetCreatureQuestInvolvedRelationsMapBounds, ObjectMgr/GetCreatureQuestRelationsMapBounds, ObjectMgr/GetGOQuestInvolvedRelationsMapBounds, ObjectMgr/GetGOQuestRelationsMapBounds, ObjectMgr/GetQuestTemplate, Player.Main/CanSeeStartQuest, Player.Main/CanTakeQuest, Player.Main/GetQuestLevelForPlayer, Player.Main/GetQuestRewardStatus, Player.Main/GetQuestStatus, Player.Main/GetQuestStatusMap, Player.Main/SatisfyQuestLevel, QuestDef/HasQuestFlag, QuestDef/IsActive, QuestDef/IsAutoComplete, QuestDef/IsRepeatable, Unit.Main/GetLevel, World/getConfig#3 | — | — |

---

<!-- verify: boundary-bleed | foreign: process, update, WorldSession -->
