# ScriptedFollowerAI

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ScriptedFollowerAI

`ScriptedFollowerAI` is a base artificial intelligence class for `Creature` objects that perform escort or follow quests. It inherits from `ScriptedAI` and manages the lifecycle of a follower NPC: initiating the follow, maintaining proximity to the player leader, handling combat assistance, detecting failure conditions (such as the player moving too far away or the follower dying), and concluding the escort.

This unit implements the core logic for the "follow" mechanic. Specific NPCs (e.g., Kerlonian, Tooga, Ringo) inherit from `FollowerAI` and implement specific quest triggers, waypoints, and dialogue via script-specific methods. `ScriptedFollowerAI` handles the shared state machine, movement generation, and combat integration.

## Purpose & Responsibilities

The primary responsibility of `ScriptedFollowerAI` is to manage the `STATE_FOLLOW_INPROGRESS` lifecycle. Its key duties include:

1.  **Leader Tracking:** Identifying and tracking the current player leader (`m_uiLeaderGUID`). If the original leader dies or becomes invalid, it attempts to find a new leader within the player's group.
2.  **Movement Management:** Switching the creature's motion master between `MoveFollow`, `MoveIdle`, and `MoveTargetedHome` based on the current state (following, paused, returning, or evading).
3.  **Combat Assistance:** Automatically engaging enemies that attack the leader or nearby group members, provided the follower is within range and line of sight.
4.  **Failure Detection:** Monitoring the distance between the follower and the leader/group. If the player moves beyond `MAX_PLAYER_DISTANCE` (100.0f) or fails the associated quest, the escort is aborted, and the follower despawns.
5.  **State Machine:** Managing a bitmask-based state system (`m_uiFollowState`) to track whether the follower is following, paused, returning from combat, or completed.

## Member-by-Member Behavior

### Initialization and Lifecycle

**FollowerAI** initializes the AI state. It sets the update timer to 2500ms, clears the follow state, and nullifies the quest pointer. It calls the parent `ScriptedAI` constructor.

**~FollowerAI** is a trivial destructor.

**JustRespawned** resets the follower's state to `STATE_FOLLOW_NONE`. It ensures combat movement is enabled and restores the creature's faction template to its default value defined in `creature_template`. It then calls `ScriptedAI::Reset` to clear any lingering combat data.

**EnterEvadeMode** handles the creature leaving combat. It clears combo points, removes reset auras, deletes the threat list, stops combat, and clears the loot recipient. Crucially, if the follower is in `STATE_FOLLOW_INPROGRESS`, it checks the current motion type. If the creature was chasing, fleeing, or confused, it transitions to `STATE_FOLLOW_RETURNING` instead of returning to its home spawn point. This allows the follower to rejoin the leader after combat ends. If not following, it returns to its home position. It also resets the spell list to the default template.

### Follow Control

**StartFollow** initiates the escort. It validates that the creature is not in combat and not already following. It stores the leader's GUID, the follow distance, and the quest object. If a custom faction is provided, it updates the creature's faction. It clears any existing waypoint motion, sets the NPC flags to none, adds the `STATE_FOLLOW_INPROGRESS` state, and commands the motion master to `MoveFollow` the leader.

**SetFollowPaused** toggles the `STATE_FOLLOW_PAUSED` flag. If pausing, it stops movement and switches to idle. If resuming, it removes the pause state and commands the motion master to `MoveFollow` the current leader again. This is used by specific scripts to halt movement during cutscenes or specific events.

**SetFollowComplete** marks the escort as finished. It stops movement and switches to idle. If `bWithEndEvent` is true, it adds `STATE_FOLLOW_POSTEVENT`; otherwise, it removes it. It always adds `STATE_FOLLOW_COMPLETE`. This signals to `UpdateAI` that the follower should despawn once the post-event logic (if any) is resolved.

**OnEscortFailed** is a virtual hook called when the escort fails. The base implementation is empty. Derived classes override this to handle specific failure behaviors (e.g., playing a death animation or sending a message).

### State Management

**HasFollowState**, **AddFollowState**, and **RemoveFollowState** manage the internal bitmask `m_uiFollowState`. These are private helper methods used throughout the class to query and modify the follower's status.

### Combat and Aggro

**AssistPlayerInCombat** determines if the follower should attack a specific unit (`pWho`). It checks if `pWho` has a victim, if the follower can assist players, and if the follower is not stunned or feigning death. It verifies the victim is a player (or controlled by one) and that `pWho` is hostile. If the follower is within `MAX_PLAYER_DISTANCE` and has line of sight, it either starts combat with `pWho` (if not already in combat) or adds threat to `pWho` (if already in combat).

**MoveInLineOfSight** is called when a unit enters the creature's line of sight. If the follower is actively following (`STATE_FOLLOW_INPROGRESS`), it first attempts `AssistPlayerInCombat`. If that doesn't trigger combat, it checks if the unit is hostile and within attack range. If so, it enters combat with that target.

**UpdateFollowerAI** is the core combat loop. It selects a hostile target if none exists. If spells are configured, it updates them. Finally, it performs a melee attack if ready. This method is called by `UpdateAI` and can be overridden by derived classes to add custom abilities.

**JustDied** handles the follower's death. If the escort is in progress, it identifies the leader. If the leader is in a group, it iterates through all group members and fails the associated quest for anyone who has it incomplete. If the leader is solo, it fails the quest for the leader. It then calls `OnEscortFailed(true)` to notify the derived script.

### Movement and Updates

**MovementInform** is called when the creature reaches a movement point. It primarily handles the `POINT_COMBAT_START` point ID. If the follower reaches this point and has a leader, it sets the state to `STATE_FOLLOW_RETURNING`. If it has no leader, it executes a "ugly fix": it deals self-damage to kill itself, teleports to its home position, and forces a despawn. This handles cases where the follower spawns incorrectly or loses its leader entirely.

**UpdateAI** is the main tick function. If the follower is in progress and not in combat, it checks a timer (`m_uiUpdateFollowTimer`).
1.  If `STATE_FOLLOW_COMPLETE` is set and no post-event is pending, the creature despawns.
2.  If `STATE_FOLLOW_RETURNING` is set, it removes that state and resumes following the leader.
3.  It checks if the leader (or any group member) is within `MAX_PLAYER_DISTANCE`. If everyone is too far, or if the quest status is no longer incomplete (quest failed/abandoned), it sets `bShouldAbort` to true.
4.  If aborting, it pauses the follow, calls `OnEscortFailed(false)`, and despawns.
5.  If valid, it resets the timer and calls `UpdateFollowerAI` to handle combat/spells.

**GetLeaderForFollower** retrieves the current leader object. It first tries to get the player associated with `m_uiLeaderGUID`. If that player is dead, it iterates through the player's group to find an alive member within `MAX_PLAYER_DISTANCE`. If found, it updates `m_uiLeaderGUID` to this new leader. If no valid leader is found, it returns null.

**GetAIInformation** outputs debug information about the follower's state, leader GUID, follow distance, and state bitmask to the chat handler.

## Cross-Unit Boundaries

`ScriptedFollowerAI` interacts heavily with the core engine modules for movement, combat, and player management.

*   **Creature / Unit:** It relies on `Creature` and `Unit` methods for combat initiation (`EnterCombatWithTarget`, `AttackStart`), threat management (`AddThreat`, `DeleteThreatList`), and state queries (`IsHostileTo`, `IsFriendlyTo`, `GetVictim`). It uses `Creature::GetMotionMaster` to control movement.
*   **Player / Group:** It uses `Player` methods to retrieve group information (`GetGroup`), quest status (`GetQuestStatus`), and to fail quests (`FailQuest`). It iterates through `GroupReference` lists to check distances for all group members, ensuring the follower doesn't abandon the quest if one group member is close enough.
*   **ScriptedAI:** As a subclass, it calls `ScriptedAI::Reset` and `ScriptedAI::GetAIInformation`. It overrides virtual methods like `UpdateAI`, `MoveInLineOfSight`, and `EnterEvadeMode`.
*   **Derived Scripts:** Many specific NPC scripts (e.g., `darkshore/npc_kerlonianAI`, `tanaris/npc_toogaAI`) call `StartFollow`, `SetFollowPaused`, `SetFollowComplete`, and `HasFollowState` to control the escort flow. They also override `OnEscortFailed` and `UpdateFollowerAI` for custom behavior.

## Data Model

This unit does not directly access database tables. It operates entirely on in-memory objects (`Creature`, `Player`, `Group`, `Quest`). The quest data referenced (`m_pQuestForFollow`) is passed in from the calling script, which likely loaded it from the database earlier. No SQL queries are executed within `ScriptedFollowerAI`.

## Notable Implementation Details

*   **Leader Handoff:** `GetLeaderForFollower` automatically switches the leader to another group member if the original leader dies. This prevents the escort from failing immediately upon the leader's death, allowing the group to continue the quest.
*   **Return from Combat:** In `EnterEvadeMode`, if the follower is in `STATE_FOLLOW_INPROGRESS`, it does *not* return to its home spawn point. Instead, it sets `STATE_FOLLOW_RETURNING`. In `UpdateAI`, this state causes the follower to resume following the leader. This is critical for escort quests where the follower must stay with the player after combat.
*   **Ugly Fix in MovementInform:** The `MovementInform` method contains a hardcoded check for `POINT_COMBAT_START`. If the follower reaches this point but has no leader, it kills itself and despawns. This is a workaround for potential desync issues where the follower might spawn in an invalid state or lose its leader reference entirely.
*   **Distance Check:** The `MAX_PLAYER_DISTANCE` constant is 100.0f. The `UpdateAI` method checks if *any* group member is within this distance. If all are outside, the escort fails. This allows groups to spread out slightly without failing the quest, as long as at least one person stays close.
*   **Quest Failure Logic:** `JustDied` and `UpdateAI` both fail the quest for the player (and group members) if the escort fails. This ensures the quest cannot be completed if the follower dies or abandons the player.
*   **State Bitmask:** The follow state is managed via a bitmask (`m_uiFollowState`). This allows multiple states to be active simultaneously (e.g., `INPROGRESS` and `PAUSED`). Care must be taken when adding/removing states to ensure logical consistency.

## Member Reference

**FollowerAI**: Constructor that initializes the follower AI state, setting the update timer to 2500ms, clearing the follow state, and nullifying the quest pointer. Calls the parent `ScriptedAI` constructor.

**~FollowerAI**: Trivial destructor.

**AssistPlayerInCombat**: Determines if the follower should attack a specific unit (`pWho`). Checks if `pWho` has a victim, if the follower can assist players, and if the follower is not stunned or feigning death. Verifies the victim is a player and that `pWho` is hostile. If within range and line of sight, starts combat or adds threat.

**OnEscortFailed**: Virtual hook called when the escort fails. Base implementation is empty. Derived classes override this to handle specific failure behaviors.

**HasFollowState**: Private helper method that checks if a specific bit is set in the `m_uiFollowState` bitmask.

**AddFollowState**: Private helper method that sets a specific bit in the `m_uiFollowState` bitmask.

**RemoveFollowState**: Private helper method that clears a specific bit in the `m_uiFollowState` bitmask.

**MoveInLineOfSight**: Called when a unit enters the creature's line of sight. If following, attempts `AssistPlayerInCombat`. If that fails, checks if the unit is hostile and within attack range, entering combat if so.

**JustDied**: Handles the follower's death. If the escort is in progress, it fails the associated quest for the leader and all group members who have it incomplete. Calls `OnEscortFailed(true)`.

**JustRespawned**: Resets the follower's state to `STATE_FOLLOW_NONE`, enables combat movement, restores the default faction template, and calls `ScriptedAI::Reset`.

**EnterEvadeMode**: Handles the creature leaving combat. Clears combat data and auras. If following, sets `STATE_FOLLOW_RETURNING` to rejoin the leader instead of returning home. Resets the spell list.

**UpdateAI**: Main tick function. Checks for completion/despawn, handles returning from combat, and verifies the leader/group is within `MAX_PLAYER_DISTANCE`. If the player is too far or the quest is failed, it aborts the escort and despawns. Otherwise, it calls `UpdateFollowerAI`.

**UpdateFollowerAI**: Core combat loop. Selects a hostile target, updates spells, and performs melee attacks. Can be overridden by derived classes.

**MovementInform**: Called when the creature reaches a movement point. Handles `POINT_COMBAT_START` by setting `STATE_FOLLOW_RETURNING` if a leader exists, or killing/despawning the creature if no leader is found (workaround for desync).

**StartFollow**: Initiates the escort. Validates state, stores leader GUID, follow distance, and quest. Sets faction if provided. Clears waypoint motion, sets NPC flags to none, adds `STATE_FOLLOW_INPROGRESS`, and starts following the leader.

**GetLeaderForFollower**: Retrieves the current leader object. If the original leader is dead, it searches the group for an alive member within `MAX_PLAYER_DISTANCE` and updates the leader GUID. Returns null if no valid leader is found.

**SetFollowComplete**: Marks the escort as finished. Stops movement, switches to idle, and adds `STATE_FOLLOW_COMPLETE`. Optionally adds `STATE_FOLLOW_POSTEVENT`.

**SetFollowPaused**: Toggles the `STATE_FOLLOW_PAUSED` flag. Pauses movement by switching to idle, or resumes following the leader.

**GetAIInformation**: Outputs debug information about the follower's state, leader GUID, follow distance, and state bitmask to the chat handler.

---

<!-- machine-true, projected from graph.json -->

## Map — ScriptedFollowerAI

*Source:* ScriptedFollowerAI.cpp, ScriptedFollowerAI.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| FollowerAI | ctor | ScriptedAI/ScriptedAI | darkshore/npc_kerlonianAI, darkshore/npc_rabid_thistle_bearAI, darkshore/npc_threshwackonatorAI, feralas/npc_kindal_moonweaverAI, feralas/npc_shay_leafrunnerAI, gnomeregan/npc_kernobeeAI, razorfen_kraul/npc_snufflenose_gopherAI, tanaris/npc_toogaAI, teldrassil/npc_mistAI, ungoro_crater/npc_ringoAI | — |
| ~FollowerAI | dtor | — | — | — |
| AssistPlayerInCombat | method | Creature.Main/CanAssistPlayers, CreatureAI/AttackStart, Unit.Main/AddThreat, Unit.Main/GetCharmerOrOwnerPlayerOrPlayerItself, Unit.Main/GetVictim, Unit.Main/HasUnitState, Unit.Main/IsFriendlyTo, Unit.Main/SetInCombatWith, WorldObject.Object/IsWithinDistInMap, WorldObject.Object/IsWithinLOSInMap | — | — |
| OnEscortFailed | method | — | — | — |
| HasFollowState | method | — | darkshore/MoveInLineOfSight, darkshore/MoveInLineOfSight#2, darkshore/SpellHit, darkshore/UpdateFollowerAI, feralas/EndEvent, feralas/MoveInLineOfSight, feralas/Reset#5, gnomeregan/UpdateFollowerAI, razorfen_kraul/EffectDummyCreature_npc_snufflenose_gopher, razorfen_kraul/MovementInform, tanaris/MoveInLineOfSight, tanaris/UpdateFollowerAI, teldrassil/MoveInLineOfSight, ungoro_crater/ClearFaint, ungoro_crater/MoveInLineOfSight, ungoro_crater/SetFaint, ungoro_crater/SpellHit, ungoro_crater/UpdateFollowerAI | — |
| AddFollowState | method | — | — | — |
| RemoveFollowState | method | — | — | — |
| MoveInLineOfSight | method | Creature.Main/CanInitiateAttack, Creature.Main/EnterCombatWithTarget, Creature.Main/GetAttackDistance, Unit.Main/IsHostileTo, Unit.Main/IsInAccessablePlaceFor, Unit.Main/IsTargetableBy, WorldObject.Object/IsWithinDistInMap, WorldObject.Object/IsWithinLOSInMap | darkshore/MoveInLineOfSight, darkshore/MoveInLineOfSight#2, feralas/MoveInLineOfSight, tanaris/MoveInLineOfSight, teldrassil/MoveInLineOfSight, ungoro_crater/MoveInLineOfSight | — |
| JustDied | method | Group/GetFirstMember, GroupReference/next, Player.Main/FailQuest, Player.Main/GetGroup, Player.Main/GetQuestStatus, QuestDef/GetQuestId | feralas/JustDied#4, feralas/JustDied#5, gnomeregan/JustDied#2 | — |
| JustRespawned | method | Creature.Main/GetCreatureInfo, CreatureAI/IsCombatMovementEnabled, CreatureAI/SetCombatMovement, ScriptedAI/Reset, Unit.Main/GetFactionTemplateId, Unit.Main/SetFactionTemplateId | darkshore/JustRespawned, darkshore/JustRespawned#3, feralas/JustRespawned, feralas/JustRespawned#2, gnomeregan/JustRespawned, teldrassil/JustRespawned, ungoro_crater/JustRespawned#2 | — |
| EnterEvadeMode | method | Creature.Main/GetCreatureInfo, Creature.Main/RemoveAurasAtReset, Creature.Main/SetLootRecipient, Creature.MotionMaster/GetCurrentMovementGeneratorType, Creature.MotionMaster/MoveTargetedHome, CreatureAI/SetSpellsList#2, ScriptedAI/Reset, Unit.Main/ClearComboPointHolders, Unit.Main/CombatStop, Unit.Main/DeleteThreatList, Unit.Main/GetMotionMaster | darkshore/EffectDummyCreature_npc_rabid_thistle_bear | — |
| UpdateAI | method | Creature.Main/DisappearAndDie, Creature.MotionMaster/MoveFollow, Group/GetFirstMember, GroupReference/next, Log.Main/Out, Player.Main/GetGroup, Player.Main/GetQuestStatus, QuestDef/GetQuestId, Unit.Main/GetMotionMaster, Unit.Main/GetVictim, WorldObject.Object/IsWithinDistInMap | razorfen_kraul/UpdateAI | — |
| UpdateFollowerAI | method | CreatureAI/DoMeleeAttackIfReady, CreatureAI/UpdateSpellsList, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | gnomeregan/UpdateFollowerAI | — |
| MovementInform | method | Creature.Main/ForcedDespawn, Creature.Main/GetHomePosition#2, Unit.Main/DealDamage, Unit.Main/GetMaxHealth | tanaris/MovementInform | — |
| StartFollow | method | Creature.MotionMaster/GetCurrentMovementGeneratorType, Creature.MotionMaster/MoveFollow, Creature.MotionMaster/MoveIdle, Log.Main/Out, MotionMaster/Clear, Object/GetGUID, Player.Main/GetName, Unit.Main/GetMotionMaster, Unit.Main/GetVictim, Unit.Main/SetFactionTemplateId, WorldObject.Object/SetUInt32Value | darkshore/GossipSelect_npc_threshwackonator, darkshore/QuestAccept_npc_kerlonian, darkshore/StartFollowing, feralas/BeforeStartFollow, feralas/QuestAccept_npc_kindal_moonweaver, gnomeregan/StartQuest, razorfen_kraul/npc_snufflenose_gopherAI, tanaris/QuestAccept_npc_tooga, teldrassil/QuestAccept_npc_mist, ungoro_crater/QuestAccept_npc_ringo | — |
| GetLeaderForFollower | method | Group/GetFirstMember, GroupReference/next, Log.Main/Out, Map.Main/GetPlayer, Object/GetGUID, ObjectGuid/ObjectGuid#5, Player.Main/GetGroup, Unit.Main/IsAlive, WorldObject.Object/GetMap, WorldObject.Object/IsWithinDistInMap | darkshore/DoAtEnd, darkshore/MoveInLineOfSight, feralas/MoveInLineOfSight, feralas/SpriteDied, feralas/SpriteSaved, gnomeregan/UpdateFollowerAI, tanaris/MoveInLineOfSight, tanaris/UpdateFollowerAI, teldrassil/DoComplete, ungoro_crater/MoveInLineOfSight | — |
| SetFollowComplete | method | Creature.MotionMaster/GetCurrentMovementGeneratorType, Creature.MotionMaster/MoveIdle, MotionMaster/Clear, Unit.Main/GetMotionMaster, Unit.Main/StopMoving | darkshore/DoAtEnd, darkshore/MoveInLineOfSight, feralas/EndEvent, feralas/MoveInLineOfSight, feralas/SpriteDied, feralas/SpriteSaved, gnomeregan/UpdateFollowerAI, tanaris/MoveInLineOfSight, tanaris/MovementInform, tanaris/UpdateFollowerAI, teldrassil/DoComplete, ungoro_crater/MoveInLineOfSight, ungoro_crater/UpdateFollowerAI | — |
| SetFollowPaused | method | Creature.MotionMaster/GetCurrentMovementGeneratorType, Creature.MotionMaster/MoveFollow, Creature.MotionMaster/MoveIdle, MotionMaster/Clear, Unit.Main/GetMotionMaster, Unit.Main/StopMoving | darkshore/ClearSleeping, darkshore/SetSleeping, feralas/QuestAccept_npc_kindal_moonweaver, feralas/ResumeFollowing, feralas/UpdateFollowerAI, gnomeregan/StartQuest, gnomeregan/UpdateFollowerAI, razorfen_kraul/DoFindNewTuber, razorfen_kraul/EffectDummyCreature_npc_snufflenose_gopher, razorfen_kraul/npc_snufflenose_gopherAI, razorfen_kraul/UpdateAI, ungoro_crater/ClearFaint, ungoro_crater/SetFaint | — |
| GetAIInformation | method | ChatHandler.Chat/PSendSysMessage, CreatureAI/GetAIInformation | — | — |
