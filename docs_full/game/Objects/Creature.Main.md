<!-- provenance: failed-members -->
# Creature.Main

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Creature

## Purpose & Responsibilities

The `Creature` class represents non-player characters (NPCs) in the game world, excluding players, game objects, and corpses (which inherit from or relate to this class). It serves as the central entity for all creature-specific logic, bridging the gap between the generic `Unit` base class and the specific behaviors required by NPCs, such as spawning, despawning, looting, vendor/trainer interactions, AI initialization, and combat mechanics unique to monsters.

Key responsibilities include:
1.  **Lifecycle Management:** Handling creation from database records (`creature` table), memory allocation, addition/removal from the world map, and deletion.
2.  **State Management:** Tracking health, mana, death states (alive, corpse, dead), respawn timers, and combat states.
3.  **AI Integration:** Initializing and managing the `CreatureAI` pointer, providing hooks for AI updates, and handling evade/combat transitions.
4.  **Interaction Logic:** Implementing vendor inventory management, trainer spell lists, gossip menus, and quest relations.
5.  **Loot & Rewards:** Managing loot generation, recipient determination (tapping), and experience/honor calculations.
6.  **Movement & Aggro:** Defining aggro ranges, leash distances, flee behaviors, and assisting mechanics (calling for help).
7.  **Data Persistence:** Saving and loading creature positions, phases, and respawn times to/from the database.

## Member-by-Member Behavior

### Lifecycle and Initialization
*   **`Creature` (ctor)**: Initializes the creature object, setting default values for health, mana, respawn timers, and state flags. It initializes the `loot` object and sets the `m_subtype`.
*   **`~Creature` (dtor)**: Cleans up resources, calling `Unit::CleanupsBeforeDelete()` and deleting the `m_AI` pointer.
*   **`Create`**: Allocates and initializes a creature in memory. It calls `CreateFromProto`, selects the final spawn position using `CreatureCreatePos`, sets corpse decay timers based on elite rank, and loads equipment and addons.
*   **`CreateFromProto`**: Sets the zone script, original entry, and creates the underlying object structure before calling `UpdateEntry`.
*   **`InitEntry`**: Loads creature template data (`CreatureInfo`), sets display ID, scale, class, race, and applies immunities based on creature type (e.g., Elementals immune to disease/poison). It also sets movement type and speed.
*   **`UpdateEntry`**: Updates the creature's entry ID, potentially changing its appearance, stats, and spells. It handles aura removal/addition when switching entries and reloads creature addons if necessary.
*   **`LoadFromDB`**: Loads a creature from the `creature` table. It validates spawn flags, determines the creature ID (handling random spawns), sets home position, and initializes the creature state (alive/dead) based on respawn timers.
*   **`SaveToDB`**: Persists the creature's current position, orientation, and spawn flags to the `creature` table.
*   **`SaveToDB#2`**: Persists the creature's current position, orientation, and spawn flags to the `creature` table, accepting an explicit map ID.
*   **`DeleteFromDB`**: Removes the creature record from the `creature`, `creature_addon`, `creature_movement`, `game_event_creature`, `game_event_creature_data`, and `creature_battleground` tables.
*   **`DeleteFromDB#2`**: Static method that removes the creature record from multiple tables using a low GUID and creature data.
*   **`AddToWorld`**: Inserts the creature into the map's object container, loads its creature group if applicable, and initializes the AI if not already done.
*   **`RemoveFromWorld`**: Removes the creature from the map's object container, notifies the AI and zone scripts, and handles cooldowns for summoned creatures.
*   **`RemoveCorpse`**: Handles the transition from corpse to dead state. It clears loot, stops group loot rolls, notifies the AI, and relocates the creature to its respawn coordinates.

### AI and Combat
*   **`AIM_Initialize`**: Creates and assigns a `CreatureAI` instance to the creature using `CreatureAISelector`. It initializes the motion master.
*   **`AI`**: Returns the current `CreatureAI` pointer.
*   **`AI#2`**: Returns the current `CreatureAI` pointer (const version).
*   **`SetAI`**: Manually sets the AI pointer (used by scripts).
*   **`Update`**: The main update loop for creatures. It handles:
    *   **Dead State:** Checks respawn timers, removes auras, picks new creature IDs if randomized, and calls `SetDeathState(JUST_ALIVED)` or `JUST_DIED`.
    *   **Corpse State:** Decays corpse timer, handles group loot timeouts, and calls `UpdateAI_corpse`.
    *   **Alive State:** Updates health/mana regeneration, checks for unreachable targets (evade logic), manages leash distances, calls `AI()->UpdateAI()`, and handles combat pulses for raid bosses.
*   **`OnEnterCombat`**: Triggered when the creature enters combat. It resets combat time, unmounts the creature, marks factions as "At War", summons guards if applicable, and calls `AI()->EnterCombat()`.
*   **`OnLeaveCombat`**: Triggered when combat ends. It updates combat state, notifies the creature group, and calls `AI()->EnterEvadeMode()`.
*   **`SetDeathState`**: Manages the transition between death states (ALIVE, JUST_ALIVED, DEAD, CORPSE, JUST_DIED). It calculates respawn delays, applies dynamic respawn modifiers, sets corpse decay timers, and handles falling animations for flying creatures.
*   **`Respawn`**: Forces a creature to respawn immediately by removing its corpse and resetting its visibility.
*   **`DespawnOrUnsummon`**: Despawns the creature or unsummons it if it's a temporary summon or pet.
*   **`ForcedDespawn`**: Immediately despawns the creature, optionally scheduling a respawn.
*   **`DisappearAndDie`**: Instantly kills and removes the creature from the world, often used for scripted events.

### Loot and Rewards
*   **`GenerateLootForBody`**: Generates loot for the creature's body based on its loot ID or AI-defined loot. It sets the team for loot distribution.
*   **`GeneratePlayerDependentLoot`**: Generates loot that depends on the looter's properties (e.g., reputation, class).
*   **`SetLootRecipient`**: Sets the player or group that has "tapped" the creature and is eligible for loot. It stores the recipient's GUID and group ID.
*   **`GetLootRecipient`**: Returns the player eligible to loot, prioritizing the original tapper's group if it still exists.
*   **`IsTappedBy`**: Checks if a specific player or their group has tapped the creature.
*   **`AllLootRemovedFromCorpse`**: Called when all loot is taken. It adjusts the corpse decay timer based on whether the corpse was skinned and the remaining respawn time.
*   **`StartGroupLoot` / `StopGroupLoot`**: Manages the group loot roll timer.

### Vendor and Trainer
*   **`GetVendorItems` / `GetVendorTemplateItems`**: Retrieves the list of items the creature sells.
*   **`GetVendorItemCurrentCount`**: Calculates the current stock of a vendor item, accounting for restock timers.
*   **`UpdateVendorItemCurrentCount`**: Updates the stock count after an item is purchased.
*   **`GetTrainerSpells` / `GetTrainerTemplateSpells`**: Retrieves the list of spells the creature teaches.
*   **`IsTrainerOf`**: Checks if the creature can train a specific player, verifying class, race, and reputation requirements.
*   **`CanInteractWithBattleMaster`**: Checks if a player can interact with the creature as a battleground master.

### Movement and Aggro
*   **`GetAttackDistance`**: Calculates the aggro range based on level difference, detection range, and auras.
*   **`DoFlee`**: Initiates fleeing behavior when health drops below a threshold.
*   **`DoFleeToGetAssistance`**: Flees towards a nearby ally to get assistance.
*   **`CallForHelp`**: Alerts nearby creatures to attack the current victim.
*   **`CallAssistance`**: Schedules a delayed call for assistance to prevent spamming.
*   **`CanInitiateAttack`**: Determines if the creature can start combat (checks react state, pacification, etc.).
*   **`SetInCombatWithZone`**: Puts all players in the instance into combat with the creature, used for raid bosses.

### Equipment and Virtual Items
Creatures do not hold physical items but display virtual items for visual representation.
*   **`SetVirtualItem`**: Assigns a virtual item to a weapon slot (main, off-hand, or ranged) by updating the creature's byte values for display ID, class, subclass, material, inventory type, and sheath state.
*   **`GetVirtualItemDisplayId`**: Returns the display ID of the virtual item in a specific slot.
*   **`GetVirtualItemClass`**: Returns the item class (e.g., Weapon, Armor) of the virtual item in a specific slot.
*   **`GetVirtualItemSubclass`**: Returns the item subclass (e.g., Sword, Shield) of the virtual item in a specific slot.
*   **`GetVirtualItemInventoryType`**: Returns the inventory type of the virtual item in a specific slot.
*   **`HasWeapon`**: Checks if the creature has a weapon equipped in the main hand by verifying if the virtual item class is `ITEM_CLASS_WEAPON`.
*   **`CanBeDisarmed`**: Checks if the creature can be disarmed by verifying if it can use its equipped weapon and if it is indeed a weapon.

### Creature Groups
Creatures can be grouped for coordinated movement and combat.
*   **`JoinCreatureGroup`**: Adds the creature to a leader's group, initializing the group if it doesn't exist. It sets the creature's group pointer and initializes motion if it's a formation.
*   **`LeaveCreatureGroup`**: Removes the creature from its group. If it is the leader, it disbands the group; otherwise, it removes itself as a member or temporary leader.

### Summoning and Cooldowns
*   **`StartCooldownForSummoner`**: If the creature was summoned by a spell with a cooldown-on-event attribute, it imposes that cooldown on the owner/summoner.
*   **`CancelSummonPossessedCharm`**: If the creature is possessed, it removes the possession aura from the charmer when the creature is removed from the world.

### Database Helpers
*   **`HasStaticDBSpawnData`**: Returns true if the creature has a corresponding record in the `creature` table (fixed GUID).
*   **`GetDBTableGUIDLow`**: Returns the low part of the GUID if the creature has static DB spawn data, otherwise 0.
*   **`SpawnInMaps`**: Static helper that spawns a creature with a specific DB GUID in all loaded map copies where the grid is loaded.

### Data Model

The `Creature` class interacts with the following database tables:

*   **`creature`**: Stores spawn data (`guid` PK, `id`, `id2`–`id5`, `map`, `position_x/y/z`, `orientation`, `spawntimesecsmin/max`, `wander_distance`, `health_percent`, `mana_percent`, `movement_type`, `spawn_flags`, `visibility_mod`, `patch_min/max`).
*   **`creature_addon`**: Stores additional data (`guid` PK, `patch` PK, `display_id`, `mount_display_id`, `equipment_id`, `stand_state`, `sheath_state`, `emote_state`, `auras`).
*   **`creature_movement`**: Stores waypoint paths (`id` PK, `point` PK, `position_x/y/z`, `orientation`, `waittime`, `wander_distance`, `script_id`, `path_id`).
*   **`game_event_creature`**: Links creatures to game events (`guid` PK, `event` PK).
*   **`game_event_creature_data`**: Stores event-specific overrides (`guid` PK, `patch` PK, `entry_id`, `display_id`, `equipment_id`, `spell_start/end`, `event` PK).
*   **`creature_battleground`**: Links creatures to battleground events (`guid` PK, `event1` PK, `event2` PK).
*   **`smartlog_creature`**: Logs creature deaths and long combats (`time`, `type`, `entry`, `guid`, `specifier`, `combatTime`, `content`).

## Notable Implementation Details

*   **Dynamic Respawn Delays:** The `ApplyDynamicRespawnDelay` method adjusts respawn times based on player population and level, reducing respawn times in high-population areas to maintain content availability.
*   **Leash Extension Sharing:** When creatures assist each other, they share a leash extension timer (`m_lastLeashExtensionTime`). Damaging one extends the leash for all, preventing premature evasion.
*   **AI Locking:** During `Update`, the AI is locked (`m_AI_locked`) to prevent concurrent modifications. If `AIM_Initialize` is called while locked, it defers initialization to the next update cycle.
*   **Corpse Decay Logic:** Corpse decay is complex, involving timers for looting, skinning, and respawn. `AllLootRemovedFromCorpse` adjusts the decay timer based on whether the corpse was skinned and the remaining respawn time.
*   **Random Spawns:** Creatures can have multiple entry IDs defined in the `creature` table. `LoadFromDB` and `Update` handle picking a random ID on spawn/respawn, ensuring the correct AI and stats are loaded.
*   **Zone Combat Pulse:** Raid bosses can trigger `SetInCombatWithZone`, forcing all players in the instance into combat with the boss, simulating a "zone-wide" aggro.

## Member Reference

**Find**: Finds a trainer spell by ID.
**RemoveItem**: Removes an item from the vendor list.
**FindItemSlot**: Finds the slot index of an item in the vendor list.
**FindItem**: Finds a vendor item by ID.
**GetCreatureGroup**: Returns the creature group the creature belongs to.
**SetCreatureGroup**: Sets the creature group.
**Execute**: Executes an assist delay event, attacking the victim if conditions are met.
**GetName**: Returns the creature's name from its template.
**GetSubName**: Returns the creature's subtitle.
**AssistDelayEvent**: Constructor for the assist delay event.
**SaveHomePosition**: Saves the current position as the home position.
**GetHomePosition#2**: Returns the home position.
**Execute#2**: Executes a forced despawn delay event.
**GetHomePositionO**: Returns the home orientation.
**AddCreatureState**: Adds a state flag.
**HasCreatureState**: Checks for a state flag.
**ClearCreatureState**: Clears a state flag.
**Execute#4**: Executes a targeted emote cleanup event.
**HasStaticFlag**: Checks for a static flag.
**HasStaticFlag#2**: Checks for a second static flag.
**HasExtraFlag**: Checks for an extra flag.
**HasImmunityFlag**: Checks for an immunity flag.
**GetSubtype**: Returns the creature subtype.
**IsPet**: Checks if the creature is a pet.
**IsTotem**: Checks if the creature is a totem.
**ToTotem#2**: Casts to const Totem.
**ToTotem**: Casts to Totem.
**IsTemporarySummon**: Checks if the creature is a temporary summon.
**IsCorpse**: Checks if the creature is a corpse.
**IsDespawned**: Checks if the creature is despawned (dead).
**SetCorpseDelay**: Sets the corpse decay delay.
**Execute#3**: Executes a targeted emote event.
**IsRacialLeader**: Checks if the creature is a racial leader.
**IsCivilian**: Checks if the creature is a civilian.
**IsTrigger**: Checks if the creature is a trigger.
**IsGuard**: Checks if the creature is a guard.
**IsImmuneToAoe**: Checks if the creature is immune to AoE.
**SelectFinalPoint**: Selects the final spawn point relative to an object.
**CanWalk**: Checks if the creature can walk.
**CanSwim**: Checks if the creature can swim.
**CanFly**: Checks if the creature can fly.
**SetCreatureReactState**: Sets the react state.
**GetCreatureReactState**: Gets the react state.
**HasCreatureReactState**: Checks the react state.
**Relocate**: Relocates the creature to new coordinates.
**IsPlusMob**: Checks if the creature is a plus mob.
**Creature**: Constructor.
**IsElite**: Checks if the creature is elite.
**IsWorldBoss**: Checks if the creature is a world boss.
**SetAI**: Sets the AI pointer.
**AI**: Returns the AI pointer.
**~Creature**: Destructor.
**AI#2**: Returns the AI pointer (const).
**SetAInitializeOnRespawn**: Sets flag to reinitialize AI on respawn.
**AddToWorld**: Adds the creature to the world.
**GetShieldBlockValue**: Calculates shield block value.
**GetMeleeDamageSchoolMask**: Returns melee damage school mask.
**SetMeleeDamageSchool**: Sets melee damage school.
**GetCurrentEquipmentId**: Returns current equipment ID.
**RemoveFromWorld**: Removes the creature from the world.
**GetCreatureInfo**: Returns creature template info.
**GetCreatureData**: Returns creature spawn data.
**GetCreatureAddon**: Returns creature addon data.
**RemoveCorpse**: Removes the corpse.
**GetLootRecipientGuid**: Returns loot recipient GUID.
**GetLootGroupRecipientId**: Returns loot group recipient ID.
**HasLootRecipient**: Checks if there is a loot recipient.
**IsGroupLootRecipient**: Checks if the recipient is a group.
**InitEntry**: Initializes creature entry data.
**IsSkinnableBy**: Checks if skinnable by a player.
**GetDetectionRange**: Returns detection range.
**SetNoCallAssistance**: Sets no call assistance flag.
**SetNoSearchAssistance**: Sets no search assistance flag.
**HasSearchedAssistance**: Checks if searched assistance.
**CanHaveTarget**: Checks if can have a target.
**GetDefaultMount**: Returns default mount ID.
**SetDefaultMount**: Sets default mount ID.
**GetDefaultMovementType**: Returns default movement type.
**SetDefaultMovementType**: Sets default movement type.
**IsDeadByDefault**: Checks if dead by default.
**GetRespawnTime**: Returns respawn time.
**SetRespawnTime**: Sets respawn time.
**GetRespawnDelay**: Returns respawn delay.
**SetRespawnDelay**: Sets respawn delay.
**GetWanderDistance**: Returns wander distance.
**SetWanderDistance**: Sets wander distance.
**UpdateCombatState**: Updates combat state.
**UpdateCombatWithZoneState**: Updates combat with zone state.
**SetCastingTarget**: Sets casting target.
**ClearCastingTarget**: Clears casting target.
**GetSpawnFlags**: Returns spawn flags.
**ToggleUnitFlagsFromStaticFlags**: Toggles unit flags based on static flags.
**SetDefaultGossipMenuId**: Sets default gossip menu ID.
**GetDefaultGossipMenuId**: Returns default gossip menu ID.
**SetDefaultValuesFromStaticFlags**: Sets default values from static flags.
**GetGridRef**: Returns grid reference.
**IsRegeneratingHealth**: Checks if regenerating health.
**IsRegeneratingMana**: Checks if regenerating mana.
**GetPetAutoSpellSize**: Returns pet auto spell size.
**GetPetAutoSpellOnPos**: Returns pet auto spell on position.
**SetCombatStartPosition**: Sets combat start position.
**GetCombatStartPosition**: Returns combat start position.
**UpdateEntry**: Updates creature entry.
**SetSummonPoint**: Sets summon point.
**GetSummonPoint**: Returns summon point.
**SetNoXP**: Sets no XP flag.
**EnableMoveInLosEvent**: Enables move in LOS event.
**GetTemporaryFactionFlags**: Returns temporary faction flags.
**GetReputationId**: Returns reputation ID.
**IsEvadeBecauseTargetNotReachable**: Checks if evading due to unreachable target.
**IsTempPacified**: Checks if temporarily pacified.
**SetTempPacified**: Sets temporary pacification.
**GetTempPacifiedTimer**: Returns temporary pacification timer.
**ResetDamageTakenOrigin**: Resets damage taken origin.
**CountDamageTaken**: Counts damage taken.
**IsLootAllowedDueToDamageOrigin**: Checks if loot is allowed due to damage origin.
**GetXPModifierDueToDamageOrigin**: Returns XP modifier due to damage origin.
**SetCallForHelpDist**: Sets call for help distance.
**SetLeashDistance**: Sets leash distance.
**SetDetectionDistance**: Sets detection distance.
**GetGroupLootTimer**: Returns group loot timer.
**SetEscortable**: Sets escortable flag.
**IsEscortable**: Checks if escortable.
**CanAssistPlayers**: Checks if can assist players.
**CanSummonGuards**: Checks if can summon guards.
**GetOriginalEntry**: Returns original entry.
**InitializeReactState**: Initializes react state.
**ChooseDisplayId**: Chooses display ID.
**ToCreature**: Casts to Creature.
**ToCreature#2**: Casts to const Creature.
**Update**: Main update loop.
**StartGroupLoot**: Starts group loot.
**StopGroupLoot**: Stops group loot.
**RegenerateAll**: Regenerates health and mana.
**RegenerateMana**: Regenerates mana.
**RegenerateHealth**: Regenerates health.
**DoFlee**: Initiates fleeing.
**DoFleeToGetAssistance**: Flees to get assistance.
**GetFleeingSpeed**: Returns fleeing speed.
**GetBaseWalkSpeedRate**: Returns base walk speed rate.
**GetBaseRunSpeedRate**: Returns base run speed rate.
**MoveAwayFromTarget**: Moves away from target.
**AIM_Initialize**: Initializes AI.
**Create**: Creates the creature.
**IsTrainerOf**: Checks if trainer of a player.
**CanInteractWithBattleMaster**: Checks if can interact with battle master.
**CanTrainAndResetTalentsOf**: Checks if can train and reset talents.
**GetOriginalLootRecipient**: Returns original loot recipient.
**GetGroupLootRecipient**: Returns group loot recipient.
**GetLootRecipient**: Returns loot recipient.
**SetLootRecipient**: Sets loot recipient.
**IsTappedBy**: Checks if tapped by a player.
**GenerateLootForBody**: Generates loot for body.
**GeneratePlayerDependentLoot**: Generates player dependent loot.
**SaveToDB**: Saves to database.
**SaveToDB#2**: Saves to database with map ID.
**GetClassLevelStats**: Returns class level stats.
**SetInitCreaturePowerType**: Sets initial creature power type.
**SelectLevel**: Selects level.
**InitStatsForLevel**: Initializes stats for level.
**_GetHealthMod**: Returns health modifier.
**_GetDamageMod**: Returns damage modifier.
**_GetSpellDamageMod**: Returns spell damage modifier.
**CreateFromProto**: Creates from prototype.
**LoadFromDB**: Loads from database.
**LoadEquipment**: Loads equipment.
**LoadDefaultEquipment**: Loads default equipment.
**HasQuest**: Checks if has quest.
**HasInvolvedQuest**: Checks if has involved quest.
**CreatureRespawnDeleteWorker**: Worker for deleting respawn data.
**operator()#2**: Operator for respawn delete worker.
**DeleteFromDB**: Deletes from database.
**DeleteFromDB#2**: Deletes from database with low GUID.
**GetAttackDistance**: Returns attack distance.
**SetDeathState**: Sets death state.
**FallGround**: Falls to ground.
**CastSpawnSpell**: Casts spawn spell.
**Respawn**: Respawns the creature.
**DespawnOrUnsummon**: Despawns or unsummons.
**ForcedDespawn**: Forces despawn.
**IsImmuneToSpell**: Checks immunity to spell.
**IsImmuneToDamage**: Checks immunity to damage.
**IsImmuneToSpellEffect**: Checks immunity to spell effect.
**IsVisibleInGridForPlayer**: Checks visibility in grid for player.
**SendAIReaction**: Sends AI reaction.
**CallAssistance**: Calls for assistance.
**CallForHelp**: Calls for help.
**CanAssistTo**: Checks if can assist to.
**CanBeTargetedByCallForHelp**: Checks if can be targeted by call for help.
**CanRespondToCallForHelpAgainst**: Checks if can respond to call for help against.
**CanFleeFromCallForHelpAgainst**: Checks if can flee from call for help against.
**CanInitiateAttack**: Checks if can initiate attack.
**DynamicRespawnRatesChecker**: Checker for dynamic respawn rates.
**operator()#3**: Operator for dynamic respawn checker.
**GetCount**: Returns count.
**HasNearbyEscort**: Checks if has nearby escort.
**ApplyDynamicRespawnDelay**: Applies dynamic respawn delay.
**SaveRespawnTime**: Saves respawn time.
**IsOutOfThreatArea**: Checks if out of threat area.
**GetLastLeashExtensionTimePtr**: Returns last leash extension time pointer.
**SetLastLeashExtensionTimePtr**: Sets last leash extension time pointer.
**ClearLastLeashExtensionTimePtr**: Clears last leash extension time pointer.
**GetLastLeashExtensionTime**: Returns last leash extension time.
**UpdateLeashExtensionTime**: Updates leash extension time.
**LoadDefaultAuras**: Loads default auras.
**LoadCreatureAddon**: Loads creature addon.
**SendZoneUnderAttackMessage**: Sends zone under attack message.
**SetInCombatWithZone**: Sets in combat with zone.
**MeetsSelectAttackingRequirement**: Checks if meets selecting attacking requirement.
**LogDeath**: Logs death.
**LogLongCombat**: Logs long combat.
**SelectAttackingTarget#2**: Selects attacking target with spell ID.
**SelectAttackingTarget**: Selects attacking target.
**IsInEvadeMode**: Checks if in evade mode.
**HasSpell**: Checks if has spell.
**LockOutSpells**: Locks out spells.
**AddCooldown**: Adds cooldown.
**GetRespawnTimeEx**: Returns extended respawn time.
**GetRespawnCoord**: Returns respawn coordinates.
**AllLootRemovedFromCorpse**: Handles all loot removed from corpse.
**GetAIName**: Returns AI name.
**GetScriptName**: Returns script name.
**GetScriptId**: Returns script ID.
**GetVendorItems**: Returns vendor items.
**GetVendorTemplateItems**: Returns vendor template items.
**GetVendorItemCurrentCount**: Returns vendor item current count.
**UpdateVendorItemCurrentCount**: Updates vendor item current count.
**Get

---

<!-- machine-true, projected from graph.json -->

## Map — Creature.Main

*Source:* Creature.cpp, Creature.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Find | method | — | WorldSession.NPCHandler/HandleTrainerBuySpellOpcode | — |
| RemoveItem | method | — | ObjectMgr/RemoveVendorItem | — |
| FindItemSlot | method | — | Player.Main/BuyItemFromVendor | — |
| FindItem | method | — | ObjectMgr/IsVendorItemValid, ObjectMgr/RemoveVendorItem | — |
| GetCreatureGroup | method | — | boss_vectus/JustDied, boss_vectus/UpdateAI, ChatHandler.CreatureCommands/HandleNpcGroupAddCommand, ChatHandler.CreatureCommands/HandleNpcGroupAddRelCommand, ChatHandler.CreatureCommands/HandleNpcGroupDelCommand, Conditions/Evaluate, CreatureAISelector/selectMovementGenerator, eastern_plaguelands/EnableCombat, eastern_plaguelands/EnableCombat#2, eastern_plaguelands/JustDied#2, eastern_plaguelands/JustReachedHome, instance_ruins_of_ahnqiraj/OnCreatureEnterCombat, Map.Main/LoadCreatureSpawnWithGroup, Map.ScriptCommands/ScriptCommand_StartScriptOnGroup, ruins_of_ahnqiraj/UpdateAI#7, ruins_of_ahnqiraj/UpdateAI#9, Unit.Main/Kill, Unit.SpellAuras/Update, WaypointMovementGenerator/InitPatrol, WaypointMovementGenerator/OnArrived | — |
| SetCreatureGroup | method | — | boss_vectus/JustDied, boss_vectus/UpdateAI, ChatHandler.CreatureCommands/HandleNpcGroupAddCommand, ChatHandler.CreatureCommands/HandleNpcGroupAddRelCommand, CreatureGroups/DisbandGroup | — |
| Execute | method | CreatureAI/AttackStart, Map.Main/GetAnyTypeCreature, Map.Main/GetUnit, Unit.Main/GetVictim, WorldObject.Object/GetMap | — | — |
| GetName | method | — | AiBotAI.Bridge/BridgeHandleAttackTarget, AiBotAI.Bridge/BridgeHandleInteractNpc, AiBotAI.Bridge/BridgeHandleQuestInteract, AiBotAI.Bridge/BridgeHandleRepairItems, AiBotAI.Bridge/BridgeHandleSellItems, AiBotAI.Bridge/BridgeHandleTrain, AiBotAI.Loot/DoAutoLoot, AiBotAI.Main/UpdateAI, BattleGroundAV/HandleCommand, ChatHandler.CharacterCommands/HandleLevelUpCommand, ChatHandler.CreatureCommands/HandleNpcAllowAttackCommand, ChatHandler.CreatureCommands/HandleNpcAllowMovementCommand, ChatHandler.CreatureCommands/HandleNpcFollowCommand, ChatHandler.CreatureCommands/HandleNpcGroupDelCommand, ChatHandler.CreatureCommands/HandleNpcSetReactStateCommand, ChatHandler.CreatureCommands/HandleNpcUnFollowCommand, ChatHandler.CreatureCommands/HandleWpShowCommand, game_Battlegrounds_BattleGround/operator()#2, game_Battlegrounds_BattleGround/operator()#5, ScriptedEscortAI/MovementInform, WorldSession.PetHandler/SendPetNameQuery | — |
| GetSubName | method | — | — | — |
| AssistDelayEvent | ctor | EventProcessor/BasicEvent, Object/GetObjectGuid | — | — |
| SaveHomePosition | method | — | Map.ScriptCommands/ScriptCommand_SetHomePosition | — |
| GetHomePosition#2 | method | — | boss_gluth/UpdateAI, boss_gothik/EnterEvadeMode, boss_jeklik/EnterEvadeMode, instance_naxxramas.Main/HandleEvadeOutOfHome, moonglade/UpdateAI#2, moonglade/UpdateEscortAI, ScriptedFollowerAI/MovementInform | — |
| Execute#2 | method | — | — | — |
| GetHomePositionO | method | — | IdleMovementGenerator/Finalize#2, world_event_wareffort/MoveToWaveBattlePosition#2 | — |
| AddCreatureState | method | — | Map.ScriptCommands/ScriptCommand_Emote, silithus/Aggro | — |
| HasCreatureState | method | — | Map.ScriptCommands/ScriptCommand_Emote, Unit.SpellAuras/HandleAuraDummy | — |
| ClearCreatureState | method | — | silithus/Reset#2 | — |
| Execute#4 | method | Map.Main/GetUnit, Unit.Main/HandleEmote, Unit.Main/IsInCombat, Unit.Main/SetFacingToObject, WorldObject.Object/GetMap, WorldObject.Object/IsMoving | — | — |
| HasStaticFlag | method | — | BasicAI/IsProximityAggroAllowedFor, Creature.MotionMaster/MoveTargetedHome, CreatureAI/CreatureAI, CreatureAI/JustRespawned, CreatureAI/OnCombatStop, HomeMovementGenerator/_setTargetLocation, PetAI/MoveInLineOfSight, Player.Main/CanInteractWithNPC, Spell.Effects/EffectSummonWild, SpellCaster/GetDefenseSkillValue, SpellCaster/MagicSpellHitResult, Unit.Main/DealMeleeDamage, Unit.Main/IsVisibleForDead, Unit.Main/Kill, Unit.SpellAuras/HandleModCharm, Unit.SpellAuras/ModPossess, WorldObject.Object/IsWithinLootXPDist | — |
| HasStaticFlag#2 | method | — | CreatureLinkingMgr/ProcessSlave, Unit.Main/AttackedBy, Unit.Main/Kill, Unit.Main/RollMeleeOutcomeAgainst#2, Unit.Main/SetInCombatState, Unit.Main/SetInCombatWithAggressor, Unit.Main/SetInCombatWithVictim, Unit.Main/UpdateSpeed | — |
| HasExtraFlag | method | — | BasicAI/IsProximityAggroAllowedFor, CyclicMovementGenerator/_setTargetLocation, RandomMovementGenerator/_setRandomLocation, SpellCaster/MeleeSpellHitResult, TargetedMovementGenerator/Update, Unit.Main/CanHaveThreatList, Unit.Main/IsSpellPartiallyBlocked, Unit.Main/IsVisibleForOrDetect, Unit.Main/RollMeleeOutcomeAgainst#2, Unit.Main/SelectHostileTarget, Unit.Main/Update, Unit.Main/UsesPvPCombatTimer, WorldSession.BattleGroundHandler/HandleBattlemasterHelloOpcode, WorldSession.ItemHandler/SendListInventory, WorldSession.NPCHandler/HandleGossipHelloOpcode, WorldSession.NPCHandler/HandleGossipSelectOptionOpcode, WorldSession.QuestHandler/HandleQuestgiverHelloOpcode | — |
| HasImmunityFlag | method | — | — | — |
| GetSubtype | method | — | ChatHandler.CreatureCommands/HandleNpcDeleteCommand, ChatHandler.CreatureCommands/HandleWpAddCommand, ChatHandler.CreatureCommands/HandleWpExportCommand, ChatHandler.CreatureCommands/HandleWpModifyCommand, ChatHandler.CreatureCommands/HandleWpShowCommand, ChatHandler.CreatureCommands/UnsummonVisualWaypoints | — |
| IsPet | method | — | ChatHandler.CharacterCommands/HandleLevelUpCommand, ChatHandler.CreatureCommands/HandleNpcSetDisplayIdCommand, ChatHandler.CreatureCommands/HandleNpcSetEntryCommand, ChatHandler.CreatureCommands/HandleNpcSetLevelCommand, ChatHandler.CreatureCommands/HandleNpcSpawnSetAurasCommand, ChatHandler.CreatureCommands/HandleNpcSpawnSetDisplayIdCommand, ChatHandler.CreatureCommands/HandleNpcSpawnSetEmoteStateCommand, ChatHandler.CreatureCommands/HandleNpcSpawnSetEntryCommand, ChatHandler.CreatureCommands/HandleNpcSpawnSetSheathStateCommand, ChatHandler.CreatureCommands/HandleNpcSpawnSetStandStateCommand, ChatHandler.CreatureCommands/HandleNpcTameCommand, CreatureAISelector/selectAI, CreatureLinkingMgr/AddMasterToHolder, CreatureLinkingMgr/ProcessSlaveGuidList, Map.Main/AddToActive, Map.Main/AddToGrid#2, Map.Main/RemoveFromActive, Map.Main/RemoveFromGrid#2, ObjectGridLoader/Visit#5, PetAI/AttackedBy, PetAI/CanAttack, PetAI/HandleReturnMovement, PetAI/OwnerAttacked, PetAI/OwnerAttackedBy, PetAI/Permissible, PetAI/SelectNextTarget, PetAI/UpdateAI, PetAI/_needToStop, PetEventAI/AttackStart, PetEventAI/MoveInLineOfSight, PetEventAI/Permissible, PetEventAI/UpdateAI, Player.Main/IsHonorOrXPTarget, Player.StatSystem/UpdateMaxHealth, Player.StatSystem/UpdateMaxPower, Spell.Effects/EffectDummy, Spell.Main/CheckPetCast, Spell.Main/CheckTamingSpell, Spell.Main/DoAllEffectOnTarget#3, Spell.Main/SetTargetMap, SpellCaster/MeleeDamageBonusDone, SpellCaster/SpellDamageBonusDone, TargetedMovementGenerator/Update, Unit.Main/CanHaveThreatList, Unit.Main/DealDamage, Unit.Main/GetSpellModOwner, Unit.Main/HandlePetCommand, Unit.Main/Kill, Unit.Main/RollMeleeOutcomeAgainst#2, Unit.Main/SetInCombatState, Unit.Main/UpdateSpeed, WorldSession.PetHandler/HandlePetAbandon, WorldSession.PetHandler/HandlePetAction, WorldSession.PetHandler/HandlePetCastSpellOpcode, WorldSession.PetHandler/HandlePetSetAction | — |
| IsTotem | method | — | CreatureAI/operator(), CreatureAISelector/selectAI, GameObject/operator()#3, Player.Main/IsHonorOrXPTarget, SpellCaster/DealHeal, SpellCaster/SpellDamageBonusDone, SpellCaster/SpellHealingBonusDone, TotemAI/Permissible, Unit.Main/CanHaveThreatList, Unit.Main/GetSpellModOwner, Unit.Main/GetTotem, Unit.Main/GetUnitBlockChance, Unit.Main/GetUnitDodgeChance, Unit.Main/ModConfuseSpell, Unit.Main/RollMeleeOutcomeAgainst#2, Unit.SpellAuras/AreaAura, Unit.SpellAuras/IsNeedVisibleSlot | — |
| ToTotem#2 | method | — | Unit.Main/IsSecondaryThreatTarget | — |
| ToTotem | method | — | TotemAI/TotemAI | — |
| IsTemporarySummon | method | — | boss_marli/Reset, ChatHandler.CreatureCommands/HandleNpcAIInfoCommand, instance_blackrock_spire/OnCreatureDeath, instance_blackrock_spire/OnCreatureEvade, instance_blackwing_lair/OnCreatureCreate, instance_blackwing_lair/OnCreatureRespawn, instance_dire_maul/Reset#8, Map.ScriptCommands/ScriptCommand_SummonCreature, npcs_special/UpdateAI#10, npc_j_eevee/npc_j_eevee_scholomanceAI, PointMovementGenerator/MovementInform#3, TargetedMovementGenerator/MovementInform, TargetedMovementGenerator/MovementInform#2, Unit.Main/Kill, ZoneScript/DelCreature | — |
| IsCorpse | method | — | Spell.Main/CheckScriptTargeting, Spell.Main/SetTargetMap, TemporarySummon/Update | — |
| IsDespawned | method | — | CreatureLinkingMgr/ProcessSlave, game_Battlegrounds_BattleGround/SpawnBGCreature, instance_uldaman/SetData, TemporarySummon/Update | — |
| SetCorpseDelay | method | — | boss_gothik/SummonAdd, scourge_invasion/OnScriptEventHappened#3, scourge_invasion/PallidHorrorAI | — |
| Execute#3 | method | Unit.Main/HandleEmoteState, Unit.Main/IsInCombat, Unit.Main/SetFacingTo, WorldObject.Object/IsMoving | — | — |
| IsRacialLeader | method | — | HonorMgr/SendPVPCredit, Player.Main/RewardHonor | — |
| IsCivilian | method | — | CreatureAI/CanTriggerAlert, PetAI/MoveInLineOfSight, PetEventAI/MoveInLineOfSight, Player.Main/RewardHonor | — |
| IsTrigger | method | — | ChatHandler.DebugCommands/HandleMmapTestArea | — |
| IsGuard | method | — | BasicAI/SummonedCreatureDespawn, CreatureAISelector/selectAI, GuardAI/Permissible, GuardEventAI/Permissible, Unit.Main/Kill | — |
| IsImmuneToAoe | method | — | Spell.Main/CheckTarget | — |
| SelectFinalPoint | method | Unit.Main/GetObjectBoundingRadius, WorldObject.Object/GetClosePoint, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | Pet.Main/Create, Totem/Create | — |
| CanWalk | method | — | Unit.Main/IsInAccessablePlaceFor | — |
| CanSwim | method | — | Unit.Main/IsInAccessablePlaceFor, WorldObject.Object/UpdateAllowedPositionZ | — |
| CanFly | method | — | BasicAI/MoveInLineOfSight, CyclicMovementGenerator/_setTargetLocation, GuardAI/MoveInLineOfSight, GuardEventAI/MoveInLineOfSight, RandomMovementGenerator/_setRandomLocation, ScriptedPetAI/MoveInLineOfSight, Unit.Main/IsInAccessablePlaceFor, WaypointMovementGenerator/StartMove#2, WorldObject.Object/UpdateAllowedPositionZ, WorldObject.PathFinder/BuildPolyPath | — |
| SetCreatureReactState | method | — | Map.ScriptCommands/ScriptCommand_SetReactState, Unit.Main/SetReactState | — |
| GetCreatureReactState | method | — | ChatHandler.CreatureCommands/HandleNpcAIInfoCommand, Unit.Main/GetReactState | — |
| HasCreatureReactState | method | — | Unit.Main/HasReactState | — |
| Relocate | method | Log.Main/Out, Object/GetGuidStr, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/IsPositionValid, WorldObject.Object/Relocate#2 | Pet.Main/Create, Totem/Create | — |
| IsPlusMob | method | — | — | — |
| Creature | ctor | Loot/Loot, Unit.Main/Unit | ChatHandler.CreatureCommands/HandleEscortShowWpCommand, ChatHandler.CreatureCommands/HandleNpcAddCommand, Map.Main/LoadCreatureSpawn, ObjectMgr/AddCreData, ObjectMgr/MoveCreData, Pet.Main/Pet, PoolManager/Spawn1Object, TemporarySummon/TemporarySummon, Totem/Totem | — |
| IsElite | method | — | Spell.Effects/EffectSkinning | — |
| IsWorldBoss | method | — | Map.Main/LoadCreatureSpawn, PoolManager/Spawn1Object, ScriptedAI/EnterEvadeMode, ScriptedInstance/OnCreatureEnterCombat, SpellCaster/GetLevelForTarget, Unit.Main/CanDetectInvisibilityOf, Unit.Main/UpdateSpeed | — |
| SetAI | method | — | boss_vaelastrasz/UpdateAI, CreatureEventAI/CreatureEventAI, Map.ScriptCommands/ScriptCommand_SummonCreature | — |
| AI | method | — | AiBotAI.Combat/UpdateOutOfCombatAI_Hunter, AiBotAI.Combat/UpdateOutOfCombatAI_Warlock, arathi_highlands/JustSummoned, arathi_highlands/QuestAccept_npc_kinelory, arathi_highlands/QuestAccept_npc_professor_phizzlethorpe, arathi_highlands/QuestAccept_npc_shakes_o_breen, ashenvale/DefineFoulwealdMound, ashenvale/JustSummoned, ashenvale/JustSummoned#2, ashenvale/JustSummoned#3, ashenvale/QuestAccept_npc_feero_ironhand, ashenvale/QuestAccept_npc_ruul_snowhoof, ashenvale/QuestAccept_npc_torek, BattleBotAI.Main/UpdateOutOfCombatAI_Hunter, BattleBotAI.Main/UpdateOutOfCombatAI_Warlock, blackrock_depths/Aggro#4, blackrock_depths/AreaTrigger_at_ring_of_law, blackrock_depths/GOHello_go_relic_coffer_door, blackrock_depths/GossipSelect_npc_mistress_nagmara, blackrock_depths/GOUse_go_bar_ale_mug, blackrock_depths/QuestAccept_npc_marshal_windsor, blackrock_depths/QuestRewarded_npc_mistress_nagmara, blackrock_depths/QuestRewarded_npc_rocknot, blackrock_depths/UpdateEscortAI#2, blackrock_depths/WaypointReached#4, blackrock_depths/WaypointReached#5, boss_anubrekhan/Aggro, boss_anubrekhan/Aggro#2, boss_anubrekhan/ExplodeOneDeadCryptGuard, boss_anubrekhan/UpdateAI, boss_arlokk/DoSummonSinglePhanter, boss_arlokk/GetArlokkAI, boss_arlokk/JustSummoned, boss_arlokk/UpdateAI#2, boss_ayamiss/UpdateAI#2, boss_bug_trio/JustDied, boss_bug_trio/LeashEncounter, boss_celebras_the_cursed/GOHello_go_book_celebras, boss_celebras_the_cursed/QuestAccept_celebras_spirit, boss_cthun/Aggro#2, boss_cthun/AttackStart, boss_cthun/CheckRespawnEye, boss_cthun/DespawnAllTentacles, boss_cthun/SpawnTentacleIfReady, boss_cthun/Update#2, boss_emperor_dagran_thaurissan/JustDied, boss_faerlina/UpdateAI, boss_fankriss/JustSummoned, boss_fankriss/SummonWorm, boss_four_horsemen/Aggro, boss_four_horsemen/Reset#2, boss_four_horsemen/Reset#3, boss_four_horsemen/Reset#4, boss_four_horsemen/Reset#5, boss_garr/JustDied#2, boss_gluth/SummonAdd, boss_golemagg/UpdateEvents#2, boss_gordok_king/UpdateAI, boss_gordok_king/UpdateAI#2, boss_gothik/EffectDummyCreature_spell_anchor, boss_gothik/SummonAdd, boss_jandice_barov/SummonIllusions, boss_jeklik/UpdateAI, boss_jindo/DoSummonSkeleton, boss_jindo/JustSummoned, boss_jindo/UpdateAI#2, boss_lethon/SpellHitTarget, boss_loatheb/WhackAStalk, boss_maexxna/JustSummoned, boss_maexxna/UpdateWraps, boss_mandokir/JustSummoned, boss_mandokir/KilledUnit#2, boss_mandokir/UpdateAI, boss_marli/JustSummoned, boss_moam/JustSummoned, boss_moam/UpdateAI, boss_nefarian/EnterEvadeMode, boss_nerubenkan/RaiseUndeadScarab, boss_noxxion/SummonAdds, boss_onyxia/CheckForTargetsInAggroRadius, boss_onyxia/JustSummoned, boss_ossirian/Aggro, boss_overlord_wyrmthalak/JustSummoned, boss_razorgore/EvadeTroops, boss_razorgore/PhaseSwitch, boss_razorgore/PopAdd, boss_razorgore/SituationInitiale, boss_razorgore/UpdateAI#2, boss_razuvious/UpdateRP, boss_sartura/DamageTaken, boss_sartura/LeashEncounter, boss_sartura/LeashEncounter#2, boss_skeram/CastBlink#2, boss_skeram/JustSummoned, boss_thaddius/Aggro#4, boss_thaddius/CheckSpawnAdds, boss_thaddius/DamageTaken, boss_thaddius/HandleCheckSpawnAdd, boss_thaddius/HandleMagneticPull, boss_tomb_of_seven/JustSummoned, boss_twinemperors/Aggro, boss_twinemperors/HandleBugSpell, boss_twinemperors/UpdateAI, boss_twinemperors/UpdateTeleportToMyBrother#2, boss_urok/DefineGoChallenge, boss_urok/ProcessEventId_event_banner_destroyed, boss_urok/SpawnAtRune, boss_vaelastrasz/GossipSelect_boss_vael, boss_vectus/UpdateAI, boss_victor_nefarius/JustSummoned, boss_victor_nefarius/NefariusGossipOptionClicked, boss_viscidus/EffectAuraDummy_spell_aura_dummy_viscidus_freeze, boss_ysondre/JustSummoned, burning_steppes/DemonDespawn, burning_steppes/EffectDummyCreature_spell_capture_grark, burning_steppes/GossipSelect_npc_klinfran, burning_steppes/JustSummoned, burning_steppes/QuestAccept_npc_grark_lorkrub, burning_steppes/WaypointReached, ChatHandler.CreatureCommands/HandleNpcAIInfoCommand, ChatHandler.CreatureCommands/HandleNpcAllowAttackCommand, ChatHandler.CreatureCommands/HandleNpcAllowMovementCommand, ChatHandler.CreatureCommands/HandleNpcEvadeCommand, ChatHandler.HardcodedEvents/HandleActiveZone, ChatHandler.HardcodedEvents/SummonMouth, CreatureGroups/MemberAssist, CreatureGroups/OnLeaveCombat, CreatureGroups/OnMemberDied, CreatureLinkingMgr/ProcessSlave, darkshore/at_murloc_camp, darkshore/DoAtEnd, darkshore/EffectDummyCreature_npc_rabid_thistle_bear, darkshore/GossipSelect_npc_threshwackonator, darkshore/JustSummoned, darkshore/JustSummoned#2, darkshore/JustSummoned#3, darkshore/QuestAccept_npc_kerlonian, darkshore/QuestAccept_npc_prospector_remtravel, darkshore/QuestAccept_npc_therylune, darkshore/QuestAccept_npc_volcor, desolace/DefineMagramiMagnet, desolace/GOHello_go_hand_of_iruxos_crystal, desolace/JustSummoned, desolace/QuestAccept_npc_cork_gizelton, desolace/QuestAccept_npc_dalinda_malem, desolace/QuestAccept_npc_melizza_brimbuzzle, desolace/QuestAccept_npc_rigger_gizelton, dreadsteed_ritual/EventStart, dun_morogh/UpdateAI, duskwood/JustDied#2, duskwood/JustSummoned, duskwood/LaunchStitches, duskwood/QuestRewarded_npc_sirra_vonindi, duskwood/WaypointReached, dustwallow_marsh/AreaTrigger_at_sentry_point, dustwallow_marsh/QuestAccept_npc_stinky_ignatz, dustwallow_marsh/QuestRewarded_npc_archmage_tervosh, eastern_plaguelands/DoRessurectUnit, eastern_plaguelands/EnableCombat, eastern_plaguelands/EnableCombat#2, eastern_plaguelands/GossipHello_npc_joseph_redpath, eastern_plaguelands/QuestAccept_npc_eris_havenfire, eastern_plaguelands/SpellHit, eastern_plaguelands/UpdateAI, elwynn_forest/UpdateAI, FearMovementGenerator/Finalize#3, felwood/JustSummoned, felwood/JustSummoned#2, felwood/QuestAccept_npc_arei, felwood/QuestAccept_npc_captured_arkonarin, feralas/BeginEvent, feralas/EffectDummyCreature_npc_shay_leafrunner, feralas/JustDied#3, feralas/QuestAccept_npc_kindal_moonweaver, feralas/QuestAccept_npc_shay_leafrunner, feralas/UpdateAI#4, FleeingMovementGenerator/Finalize#3, gnomeregan/GossipSelect_npc_blastmaster_emi_shortfuse, gnomeregan/QuestAccept_npc_kernobee, GuardMgr/SummonGuard, hinterlands/QuestAccept_npc_rinji, HomeMovementGenerator/Finalize, IdleMovementGenerator/Finalize, instance_blackrock_depths/DoSummonCreatureAndAttack, instance_blackrock_depths/SetData, instance_blackrock_spire/AreaTrigger_at_ubrs_the_beast, instance_dire_maul/EnterEvadeMode, instance_dire_maul/GossipSelect_boss_kromcrush, instance_dire_maul/JustSummoned, instance_dire_maul/MoveInLineOfSight, instance_dire_maul/SetData, instance_dire_maul/SummonAdds, instance_dire_maul/UpdateAI#2, instance_naxxramas.boss_kelthuzad/EvadeAllGuardians, instance_naxxramas.boss_kelthuzad/OnKTAreaTrigger, instance_naxxramas.boss_kelthuzad/SpawnAndSendP1Creature, instance_naxxramas.boss_kelthuzad/UpdateP2P3, instance_naxxramas.Main/HandleEvadeOutOfHome, instance_naxxramas.Main/OnPlayerDeath, instance_ruins_of_ahnqiraj/Update, instance_scarlet_monastery/SetData, instance_sunken_temple/OnCreatureEnterCombat, instance_uldaman/SetData, instance_zulgurub/SetData, loch_modan/AreaTrigger_at_huldar_miran, loch_modan/JustSummoned, loch_modan/QuestAccept_npc_miran, Map.Main/CreatureRespawnRelocation, Map.Main/SendEventToAdditionalTargets, Map.Main/SendEventToMainTargets, Map.ScriptCommands/ScriptCommand_AttackStart, Map.ScriptCommands/ScriptCommand_CreatureSpells, Map.ScriptCommands/ScriptCommand_Evade, Map.ScriptCommands/ScriptCommand_SendScriptEvent, Map.ScriptCommands/ScriptCommand_SetCombatMovement, Map.ScriptCommands/ScriptCommand_SetMeleeAttack, Map.ScriptCommands/ScriptCommand_SetPhase, Map.ScriptCommands/ScriptCommand_SetPhaseRandom, Map.ScriptCommands/ScriptCommand_SetPhaseRange, Map.ScriptCommands/ScriptCommand_SummonCreature, mob_anubisath_sentinel/CallBuddiesToAttack, mob_anubisath_sentinel/GetOtherSentinels, mob_anubisath_sentinel/GiveBuddyMyList, mob_anubisath_sentinel/JustDied, molten_core/JustSummoned, moonglade/EnterEvadeMode, moonglade/JustDied, moonglade/JustSummoned#2, moonglade/QuestAccept_npc_keeper_remulos, moonglade/SummonedMovementInform, moonglade/UpdateAI, moonglade/UpdateEscortAI, npcs_special/GossipHello_npc_kwee_peddlefeet, npcs_special/QuestAccept_npc_doctor, npcs_special/SpellHit, npcs_special/UpdateAI#3, npcs_special/UpdateAI#8, ObjectGridLoader/Visit#6, PartyBotAI/UpdateOutOfCombatAI_Hunter, PartyBotAI/UpdateOutOfCombatAI_Warlock, Pet.Main/Unsummon, Player.Main/SendLoot, PointMovementGenerator/Finalize#2, PointMovementGenerator/MovementInform, PointMovementGenerator/MovementInform#3, quest_stormwind_rendezvous/AreaTrigger_at_stormwind_gates, quest_stormwind_rendezvous/GossipHello_npc_reginald_windsor, quest_stormwind_rendezvous/GossipHello_npc_squire_rowe, quest_stormwind_rendezvous/GossipSelect_npc_reginald_windsor, quest_stormwind_rendezvous/GossipSelect_npc_squire_rowe, quest_stormwind_rendezvous/PokeRowe, quest_stormwind_rendezvous/QuestAccept_npc_reginald_windsor, quest_stormwind_rendezvous/UpdateAI#2, razorfen_downs/QuestAccept_npc_belnistrasz, razorfen_kraul/EffectDummyCreature_npc_snufflenose_gopher, razorfen_kraul/JustSummoned, razorfen_kraul/QuestAccept_npc_willix_the_importer, redridge_mountains/QuestAccept_npc_corporal_keeshan, ruins_of_ahnqiraj/GetTuubidAI, ruins_of_ahnqiraj/GetTuubidAI#2, ruins_of_ahnqiraj/JustSummoned, scourge_invasion/OnScriptEventHappened#3, scourge_invasion/SummonCultists, scourge_invasion/UpdateAI#7, scourge_invasion/UpdateAI#8, ScriptedAI/Ambush, searing_gorge/QuestAccept_npc_dying_archaeologist, searing_gorge/UpdateAI, silithus/BeginAQOpeningEvent, silithus/DemonDespawn, silithus/DoTimeStopArmy, silithus/GossipHello_npc_Krug_SkullSplit, silithus/GossipSelect_npc_Krug_SkullSplit, silithus/JustSummoned, silithus/JustSummoned#2, silithus/OnActivateBySpell, silithus/QuestAcceptGO_crystalline_tear, silithus/QuestComplete_npc_Geologist_Larksbane, silverpine_forest/QuestAccept_npc_deathstalker_erland, Spell.Effects/EffectDummy, Spell.Effects/EffectSummon, Spell.Effects/EffectSummonCritter, Spell.Effects/EffectSummonGuardian, Spell.Effects/EffectSummonObject, Spell.Effects/EffectSummonObjectWild, Spell.Effects/EffectTransmitted, Spell.Main/DoAllEffectOnTarget#3, Spell.Main/finish, SpellCaster/DealDamageMods, stonetalon_mountains/QuestAccept_npc_piznik, stormwind_city/QuestAccept_npc_bartleby, stormwind_city/QuestAccept_npc_dashel_stonefist, stranglethorn_vale/JustSummoned, stranglethorn_vale/QuestRewarded_npc_witch_doctor_unbagwa, stratholme/OnSummon, swamp_of_sorrows/QuestAccept_npc_galen_goodward, tanaris/GOHello_go_inconspicuous_landmark, tanaris/QuestAccept_npc_tooga, tanaris/QuestRewarded_npc_yehkinya, TargetedMovementGenerator/MovementInform, TargetedMovementGenerator/MovementInform#2, teldrassil/QuestAccept_npc_mist, TemporarySummon/InformSummonerOfDespawn, the_barrens/AreaTrigger_at_twiggy_flathead, the_barrens/JustSummoned#2, the_barrens/ProcessEventId_event_the_principle_source, the_barrens/QuestAccept_npc_gilthares, the_barrens/QuestAccept_npc_wizzlecranks_shredder, thousand_needles/go_panther_cage, thousand_needles/JustSummoned, thousand_needles/ProcessEventId_event_test_of_endurance, thousand_needles/QuestAccept_npc_lakota_windsong, thousand_needles/QuestAccept_npc_paoka_swiftmountain, thousand_needles/UpdateAI, ThreatListCopier.battleground_alterac/GossipHello_npc_AVBlood_collector, ThreatListCopier.battleground_alterac/GossipSelect_npc_AVBlood_collector, ThreatListCopier.battleground_alterac/JustDied, ThreatListCopier.battleground_alterac/JustDied#2, ThreatListCopier.battleground_alterac/QuestComplete_AV_npc_troops_chief, ThreatListCopier.battleground_alterac/QuestComplete_npc_AVBlood_collector, ThreatListCopier.battleground_alterac/Reset#5, ThreatListCopier.boss_ragnaros/SummonSonsOfFlame, Totem/Summon, Totem/UnSummon, ungoro_crater/Aggro#2, ungoro_crater/Aggro#3, ungoro_crater/DemonDespawn, ungoro_crater/GossipSelect_npc_simone_the_inconspicuous, ungoro_crater/JustReachedHome, ungoro_crater/QuestAccept_npc_ame01, ungoro_crater/QuestAccept_npc_ringo, ungoro_crater/Transform, ungoro_crater/UpdateAI#2, ungoro_crater/UpdateAI#3, Unit.Main/AI, Unit.Main/Attack, Unit.Main/AttackedBy, Unit.Main/HandlePetCommand, Unit.Main/Kill, Unit.Main/operator()#2, Unit.Main/SelectHostileTarget, Unit.Main/SetInCombatWithVictim, Unit.Main/SummonCreatureAndAttack, Unit.Main/TauntApply, Unit.SpellAuras/HandleModCharm, Unit.SpellAuras/ModPossess, wailing_caverns/UpdateEscortAI, WaypointMovementGenerator/OnArrived, westfall/JustSummoned, westfall/QuestAccept_npc_daphne_stilwell, wetlands/GossipHello_npc_mikhail, wetlands/QuestAccept_npc_mikhail, winterspring/DemonDespawn, WorldObject.Object/SummonCreature#2, WorldObject.Object/SummonGameObject, WorldSession.ChatHandler/HandleTextEmoteOpcode, world_event_wareffort/GossipHello_npc_AQwar_collector, world_event_wareffort/QuestComplete_npc_AQwar_collector, world_event_wareffort/UpdateAI#2, zulfarrak/OnGossipSelect_npc_sergeant_bly, zulfarrak/OnGossipSelect_npc_weegli_blastfuse, zulfarrak/UpdateAI, zulfarrak/UpdateAI#2 | — |
| ~Creature | dtor | Unit.Main/CleanupsBeforeDelete | — | — |
| AI#2 | method | — | Player.Main/IsAllowedToLoot | — |
| SetAInitializeOnRespawn | method | — | boss_dragon_of_nightmare/boss_dragon_of_nightmareAI | — |
| AddToWorld | method | CreatureGroups/IsFormation, CreatureGroups/LoadCreatureGroup, CreatureGroups/OnRespawn, CreatureGroupsManager/instance, Object/GetObjectGuid, Object/IsInWorld, ObjectGuid/GetHigh, Unit.Main/AddToWorld, Unit.Main/GetDeathState, WorldObject.Object/GetMap, WorldObject.Object/SetActiveObjectState, ZoneScript/OnCreatureCreate | — | — |
| GetShieldBlockValue | method | — | — | — |
| GetMeleeDamageSchoolMask | method | — | ThreatManager/selectNextVictim | — |
| SetMeleeDamageSchool | method | — | Pet.Main/InitStatsForLevel | — |
| GetCurrentEquipmentId | method | — | ChatHandler.CreatureCommands/HandleNpcInfoCommand, instance_dire_maul/GordokBruteAI | — |
| RemoveFromWorld | method | CreatureAI/OnRemoveFromWorld, Object/GetObjectGuid, Object/GetUInt32Value, Object/IsInWorld, ObjectGuid/GetHigh, Unit.Main/RemoveFromWorld, WorldObject.Object/GetMap, ZoneScript/OnCreatureRemove | ChatHandler.HardcodedEvents/Disable#5, ChatHandler.HardcodedEvents/SummonMouth, ChatHandler.HardcodedEvents/SummonPallid, npcs_special/UpdateAI#7, scholo_trash/SpellHit, scourge_invasion/DespawnEventDoodads, scourge_invasion/JustDied#2, scourge_invasion/NecroticShard, scourge_invasion/OnScriptEventHappened, scourge_invasion/SpellHitTarget, scourge_invasion/SpellHitTarget#2, scourge_invasion/SpellHitTarget#3, scourge_invasion/SpellHitTarget#4, ungoro_crater/DemonDespawn | — |
| GetCreatureInfo | method | — | AiBotAI.Grind/SelectGrindTarget, boss_celebras_the_cursed/UpdateEscortAI, boss_mr_smite/Reset, boss_noxxion/JustDied, ChatHandler.CreatureCommands/HandleEscortShowWpCommand, ChatHandler.CreatureCommands/HandleNpcInfoCommand, CreatureAI/CreatureAI, CreatureAI/JustRespawned, CreatureAI/OnCombatStop, CreatureAISelector/selectAI, CreatureAISelector/selectMovementGenerator, CritterAI/Permissible, game_Group_Group/RewardGroupAtKill_helper, Map.ScriptCommands/ScriptCommand_SetEquipment, Pet.Main/CreateBaseAtCreature, Pet.Main/GetSkillIdForPetTraining, Pet.Main/HaveInDiet, Pet.Main/InitializeDefaultName, Pet.Main/InitStatsForLevel, Pet.Main/IsPermanentPetFor, Pet.Main/LearnPetPassives, Pet.Main/LoadPetFromDB, Player.Main/CharmSpellInitialize, Player.Main/IsHonorOrXPTarget, Player.Main/PrepareGossipMenu, Player.Main/RewardSinglePlayerAtKill, Player.Main/SendLoot, ScriptedAI/EnterEvadeMode, ScriptedAI/SetEquipmentSlots, ScriptedEscortAI/EnterEvadeMode, ScriptedEscortAI/JustRespawned, ScriptedFollowerAI/EnterEvadeMode, ScriptedFollowerAI/JustRespawned, scripts_battlegrounds_battleground/npc_etendardAI, Spell.Effects/EffectSummonCritter, Spell.Effects/EffectSummonGuardian, Spell.Main/CheckCast, Spell.Main/CheckTamingSpell, SpellCaster/MeleeDamageBonusDone, SpellCaster/SpellDamageBonusDone, TotemAI/TotemAI, Unit.Main/GetCreatureType, Unit.Main/Kill, Unit.Main/RestoreFaction, Unit.SpellAuras/HandleAuraTransform, Unit.SpellAuras/HandleModCharm, Unit.SpellAuras/HandlePeriodicTriggerSpell, WorldObject.Object/IsWithinLootXPDist, WorldSession.NPCHandler/SendTrainerList | — |
| GetCreatureData | method | — | ChatHandler.CreatureCommands/HandleNpcAddEntryCommand, ChatHandler.CreatureCommands/HandleNpcSpawnInfoCommand, ChatHandler.CreatureCommands/HandleNpcSpawnSetAurasCommand, ChatHandler.CreatureCommands/HandleNpcSpawnSetDisplayIdCommand, ChatHandler.CreatureCommands/HandleNpcSpawnSetEmoteStateCommand, ChatHandler.CreatureCommands/HandleNpcSpawnSetEntryCommand, ChatHandler.CreatureCommands/HandleNpcSpawnSetMoveTypeCommand, ChatHandler.CreatureCommands/HandleNpcSpawnSetRespawnTimeCommand, ChatHandler.CreatureCommands/HandleNpcSpawnSetSheathStateCommand, ChatHandler.CreatureCommands/HandleNpcSpawnSetStandStateCommand, ChatHandler.CreatureCommands/HandleNpcSpawnWanderDistCommand, ScriptedAI/ScriptedAI | — |
| GetCreatureAddon | method | — | ChatHandler.CreatureCommands/HandleNpcSpawnSetAurasCommand, ChatHandler.CreatureCommands/HandleNpcSpawnSetDisplayIdCommand, ChatHandler.CreatureCommands/HandleNpcSpawnSetEmoteStateCommand, ChatHandler.CreatureCommands/HandleNpcSpawnSetSheathStateCommand, ChatHandler.CreatureCommands/HandleNpcSpawnSetStandStateCommand | — |
| RemoveCorpse | method | CreatureAI/CorpseRemoved, CreatureLinkingMgr/DoCreatureLinkingEvent, Loot/clear, Map.Main/CreatureRelocation, Map.Main/GetCreatureLinkingHolder, Unit.Main/GetDeathState, WorldObject.Object/GetMap, WorldObject.Object/UpdateObjectVisibility | darkshore/JustSummoned, darkshore/UpdateAI, game_Battlegrounds_BattleGround/SpawnBGCreature, instance_uldaman/DespawnMinion, instance_uldaman/RespawnMinion, instance_uldaman/SetData, the_barrens/UpdateAI#2, ThreatListCopier.battleground_alterac/JustRespawned | — |
| GetLootRecipientGuid | method | — | ChatHandler.DebugCommands/HandleDebugGetLootRecipientCommand, Spell.Main/CheckCast, Unit.Main/DealDamage, Unit.Main/Kill | — |
| GetLootGroupRecipientId | method | — | ChatHandler.DebugCommands/HandleDebugGetLootRecipientCommand, Spell.Main/CheckCast | — |
| HasLootRecipient | method | — | ChatHandler.DebugCommands/HandleDebugGetLootRecipientCommand, Unit.Main/DealDamage, WorldObject.Object/BuildValuesUpdate | — |
| IsGroupLootRecipient | method | — | — | — |
| InitEntry | method | Log.Main/Out, Object/SetEntry, ObjectMgr/GetCreatureDisplayInfoRandomGender, ObjectMgr/GetCreatureTemplate, Unit.Main/ApplySpellImmune, Unit.Main/SetDisplayId, Unit.Main/SetFly, Unit.Main/SetNativeDisplayId, Unit.Main/UpdateSpeed, WorldObject.Object/SetByteValue, WorldObject.Object/SetFloatValue, WorldObject.Object/SetObjectScale | Pet.Main/Create | — |
| IsSkinnableBy | method | — | Spell.Main/CheckCast | — |
| GetDetectionRange | method | — | BasicAI/MoveInLineOfSight | — |
| SetNoCallAssistance | method | — | boss_loatheb/Aggro#2, boss_loatheb/mob_eyeStalkAI, boss_loatheb/mob_rottingMaggotAI, boss_loatheb/MoveInLineOfSight, boss_loatheb/MoveInLineOfSight#2, boss_loatheb/Reset#2, boss_loatheb/UpdateAI#2, CreatureGroups/MemberAssist, instance_blackrock_spire/JustDidDialogueStep, instance_dire_maul/Reset#3, mob_anubisath_sentinel/CallBuddiesToAttack, molten_core/Reset#2, PointMovementGenerator/Finalize, Unit.Main/AttackStop | — |
| SetNoSearchAssistance | method | — | instance_naxxramas.boss_kelthuzad/kt_p1AddAI, Unit.Main/AttackStop | — |
| HasSearchedAssistance | method | — | Unit.Main/AttackStop | — |
| CanHaveTarget | method | — | Spell.Main/prepare#2 | — |
| GetDefaultMount | method | — | — | — |
| SetDefaultMount | method | — | Map.ScriptCommands/ScriptCommand_Mount | — |
| GetDefaultMovementType | method | — | Creature.MotionMaster/InitializeNewDefault, CreatureAISelector/selectMovementGenerator, CreatureGroups/OnMemberDied, instance_naxxramas.Main/EnterStoneform, instance_naxxramas.Main/mob_naxxramasGarboyleAI, Unit.Main/RestoreMovement | — |
| SetDefaultMovementType | method | — | boss_gothik/Reset#2, boss_majordomo_executus/Reset, boss_omen/OnFireworkLaunch, boss_thaddius/Reset#3, boss_vectus/MoveInLineOfSight, burning_steppes/Reset#2, burning_steppes/Transform, ChatHandler.CreatureCommands/HandleNpcSetMoveTypeCommand, ChatHandler.CreatureCommands/HandleNpcSetWanderDistCommand, ChatHandler.CreatureCommands/HandleNpcSpawnSetMoveTypeCommand, ChatHandler.CreatureCommands/HandleWpModifyCommand, elemental_invasions/DoSpawn, instance_blackrock_depths/HandleBarPatrons, instance_razorfen_kraul/SetData, Map.ScriptCommands/ScriptCommand_SetDefaultMovement, ruins_of_ahnqiraj/OssirianTornadoAI, scripts_battlegrounds_battleground/UpdateAI, silithus/Reset#10, silithus/Transform, ThreatListCopier.battleground_alterac/UpdateEscortAI#3, ThreatListCopier.battleground_alterac/UpdateEscortAI#4, winterspring/Reset, winterspring/Transform, zulfarrak/Reset#3, zulfarrak/UpdateAI#3 | — |
| IsDeadByDefault | method | — | — | — |
| GetRespawnTime | method | — | CreatureGroups/Respawn, CreatureLinkingMgr/ProcessSlave, game_Battlegrounds_BattleGround/SpawnBGCreature, Spell.Effects/EffectDummy | — |
| SetRespawnTime | method | — | boss_gahzranka/CheckSpawnStatus, boss_timmy_the_cruel/JustDied, burning_steppes/DemonDespawn, burning_steppes/JustDied, burning_steppes/Reset#2, feralas/EndEvent, feralas/JustDied#4, game_Battlegrounds_BattleGround/SpawnBGCreature, instance_ruins_of_ahnqiraj/GiveRepAfterRajaxxDeath, instance_ruins_of_ahnqiraj/OnCreatureDeath, instance_ruins_of_ahnqiraj/SetAndorovSquadRespawnTime, instance_ruins_of_ahnqiraj/SetData, instance_zulgurub/OnCreatureDeath, Map.Main/LoadCreatureSpawn, molten_core/JustDied, molten_core/JustDied#2, PoolManager/Spawn1Object, quest_stormwind_rendezvous/UpdateAI, silithus/DemonDespawn, silithus/JustDied#4, silithus/Reset#10, ThreatListCopier.battleground_alterac/UpdateAI#17, ungoro_crater/DemonDespawn, ungoro_crater/JustDied, ungoro_crater/Reset#6, wailing_caverns/MovementInform, winterspring/DemonDespawn, winterspring/JustDied, winterspring/Reset | — |
| GetRespawnDelay | method | — | ChatHandler.CreatureCommands/HandleNpcInfoCommand, instance_deadmines/OnCreatureCreate, instance_deadmines/Update, instance_shadowfang_keep/Update, Map.Main/LoadCreatureSpawn, PoolManager/Spawn1Object, ThreatListCopier.battleground_alterac/Reset#5, western_plaguelands/DoDie, wetlands/npc_tapoke_slim_jahnAI | — |
| SetRespawnDelay | method | — | ashenvale/EnragedFoulwealdJustDied, ashenvale/EventStart, ashenvale/UpdateAI, boss_skeram/JustDied, boss_urok/SpawnAtRune, boss_vaelastrasz/Aggro, boss_vaelastrasz/JustDied, boss_victor_nefarius/JustReachedHome, boss_victor_nefarius/JustSummoned, boss_victor_nefarius/SummonedCreatureJustDied, burning_steppes/DemonDespawn, burning_steppes/JustDied, burning_steppes/Reset#2, ChatHandler.CreatureCommands/HandleNpcSetRespawnTimeCommand, ChatHandler.CreatureCommands/HandleNpcSpawnSetRespawnTimeCommand, deadmines/GOHello_go_defias_gunpowder, game_Battlegrounds_BattleGround/SpawnBGCreature, instance_blackrock_depths/SetData, moonglade/JustSummoned#2, quest_stormwind_rendezvous/UpdateAI, scourge_invasion/UpdateAI#7, silithus/DemonDespawn, silithus/JustDied#4, silithus/JustSummoned, silithus/Reset#10, silithus/Reset#7, silithus/StartEvent, ThreatListCopier.battleground_alterac/JustDied, ThreatListCopier.battleground_alterac/JustRespawned, ungoro_crater/DemonDespawn, ungoro_crater/JustDied, ungoro_crater/Reset#6, western_plaguelands/DoDie, wetlands/JustRespawned, wetlands/UpdateEscortAI, winterspring/DemonDespawn, winterspring/JustDied, winterspring/Reset | — |
| GetWanderDistance | method | — | — | — |
| SetWanderDistance | method | — | boss_four_horsemen/UpdateAI#3, boss_gothik/Reset#2, boss_omen/OnFireworkLaunch, boss_sapphiron/npc_sapphiron_blizzardAI, boss_thaddius/Reset#3, ChatHandler.CreatureCommands/HandleNpcSetWanderDistCommand, ChatHandler.CreatureCommands/HandleNpcSpawnWanderDistCommand, elemental_invasions/DoSpawn, instance_blackrock_depths/HandleBarPatrons, instance_naxxramas.boss_kelthuzad/StartEncounter, instance_naxxramas.Main/OnCreatureCreate, Map.ScriptCommands/ScriptCommand_SetDefaultMovement, ruins_of_ahnqiraj/OssirianTornadoAI, scourge_invasion/UpdateAI, ThreatListCopier.battleground_alterac/AV_WarRiderAI, ThreatListCopier.battleground_alterac/UpdateEscortAI#3, ThreatListCopier.battleground_alterac/UpdateEscortAI#4 | — |
| UpdateCombatState | method | — | boss_sapphiron/setHover, Unit.Main/CombatStop, Unit.Main/Kill | — |
| UpdateCombatWithZoneState | method | — | Unit.Main/CombatStop, Unit.Main/Kill | — |
| SetCastingTarget | method | — | Spell.Main/prepare#2 | — |
| ClearCastingTarget | method | — | Spell.Main/finish | — |
| GetSpawnFlags | method | — | — | — |
| ToggleUnitFlagsFromStaticFlags | method | WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag | Pet.Main/InitStatsForLevel | — |
| SetDefaultGossipMenuId | method | — | eastern_plaguelands/JustReachedHome, eastern_plaguelands/JustRespawned, instance_ruins_of_ahnqiraj/SetData, instance_ruins_of_ahnqiraj/Update, instance_wailing_caverns/SetData, Map.ScriptCommands/ScriptCommand_SetGossipMenu, Pet.Main/Create, Unit.SpellAuras/HandlePeriodicTriggerSpell | — |
| GetDefaultGossipMenuId | method | — | Player.Main/SendPreparedQuest, WorldSession.NPCHandler/HandleGossipHelloOpcode, WorldSession.QuestHandler/HandleQuestgiverHelloOpcode | — |
| SetDefaultValuesFromStaticFlags | method | — | — | — |
| GetGridRef | method | — | — | — |
| IsRegeneratingHealth | method | — | — | — |
| IsRegeneratingMana | method | — | — | — |
| GetPetAutoSpellSize | method | — | PetAI/UpdateAI | — |
| GetPetAutoSpellOnPos | method | — | PetAI/UpdateAI | — |
| SetCombatStartPosition | method | — | duskwood/JustSummoned, duskwood/WaypointReached, instance_zulfarrak/MoveNPCIfAlive, Unit.Main/SetInCombatState, zulfarrak/DestroyDoor, zulfarrak/initBlyCrewMember, zulfarrak/MovementInform, zulfarrak/RunAfterExplosion1, zulfarrak/RunAfterExplosion2 | — |
| GetCombatStartPosition | method | — | ThreatListCopier.battleground_alterac/Reset#10, ThreatListCopier.battleground_alterac/Reset#6, ThreatListCopier.battleground_alterac/Reset#7 | — |
| UpdateEntry | method | CreatureAI/SetSpellsList#2, Object/IsInWorld, ObjectMgr/GetFactionEntry, ObjectMgr/GetFactionTemplateEntry, Player.StatSystem/UpdateAllStats, Unit.Main/GetHealthPercent, Unit.Main/GetPowerPercent, Unit.Main/HasAura#2, Unit.Main/RemoveAurasDueToSpellByCancel, Unit.Main/SetAttackTime, Unit.Main/SetCanModifyStats, Unit.Main/SetCreateResistance, Unit.Main/SetFactionTemplateId, Unit.Main/SetFly, Unit.Main/SetPvP, Unit.Main/SetSheath, World/getConfig, WorldObject.Object/AddUnitMovementFlag, WorldObject.Object/RemoveFlag, WorldObject.Object/SetActiveObjectState, WorldObject.Object/SetByteValue, WorldObject.Object/SetFlag, WorldObject.Object/SetUInt32Value, WorldObject.Object/SetVisibilityModifier | boss_dathrohan_balnazzar/Reset, boss_dathrohan_balnazzar/UpdateAI, boss_dragon_of_nightmare/GetAI_boss_dragon_of_nightmare, burning_steppes/Transform, ChatHandler.CreatureCommands/HandleNpcSetEntryCommand, ChatHandler.CreatureCommands/HandleNpcSpawnSetEntryCommand, darkshore/EffectDummyCreature_npc_rabid_thistle_bear, felwood/WaypointReached#2, GameEventMgr.Main/operator(), instance_blackrock_depths/ReplacePrincessIfPossible, instance_blackrock_spire/OnCreatureCreate, instance_blackwing_lair/OnCreatureCreate, instance_blackwing_lair/OnCreatureRespawn, instance_molten_core/OnCreatureCreate, instance_molten_core/OnCreatureRespawn, instance_naxxramas.Main/ChangeColor, instance_temple_of_ahnqiraj/OnCreatureCreate, Map.ScriptCommands/ScriptCommand_UpdateEntry, quest_stormwind_rendezvous/UpdateAI, quest_stormwind_rendezvous/UpdateAI_corpse, searing_gorge/UpdateAI, silithus/Transform, Spell.Effects/EffectDummy, ThreatListCopier.battleground_alterac/JustRespawned#2, ThreatListCopier.battleground_alterac/UpdateAI#5, winterspring/Transform | — |
| SetSummonPoint | method | — | ChatHandler.CreatureCommands/Helper_CreateWaypointFor, Player.Main/SummonPossessedMinion, Spell.Effects/EffectSummon, Spell.Effects/EffectSummonCritter, Spell.Effects/EffectSummonGuardian, Spell.Effects/EffectSummonPet#2, Spell.Effects/EffectSummonTotem, WorldObject.Object/SummonCreature, WorldObject.Object/SummonCreature#2 | — |
| GetSummonPoint | method | — | — | — |
| SetNoXP | method | — | arathi_highlands/JustSummoned#2, boss_herod/JustSummoned | — |
| EnableMoveInLosEvent | method | — | boss_archaedas/Reset#2, boss_ouro/Reset#3, boss_ouro/Reset#4, boss_vectus/Reset, CreatureEventAI/CreatureEventAI, darkshore/ClearSleeping, darkshore/Reset, darkshore/Reset#6, darkshore/Reset#7, eastern_plaguelands/Reset#4, gnomeregan/Reset, npcs_special/Reset#10, npcs_special/Reset#21, quest_stormwind_rendezvous/ResetCreature, tanaris/Reset, ThreatListCopier.battleground_alterac/Reset#21, ungoro_crater/Reset#4, western_plaguelands/Reset, western_plaguelands/Reset#3 | — |
| GetTemporaryFactionFlags | method | — | boss_sapphiron/setHover, HomeMovementGenerator/Finalize, Unit.Main/CombatStop | — |
| GetReputationId | method | — | — | — |
| IsEvadeBecauseTargetNotReachable | method | — | — | — |
| IsTempPacified | method | — | Unit.Main/SelectHostileTarget | — |
| SetTempPacified | method | — | boss_gothik/UpdateAI, WorldObject.Object/SummonCreature#2 | — |
| GetTempPacifiedTimer | method | — | — | — |
| ResetDamageTakenOrigin | method | — | Unit.Main/AttackStop | — |
| CountDamageTaken | method | — | boss_twinemperors/DamageTaken, Unit.Main/DealDamage | — |
| IsLootAllowedDueToDamageOrigin | method | — | Unit.Main/Kill | — |
| GetXPModifierDueToDamageOrigin | method | — | — | — |
| SetCallForHelpDist | method | — | — | — |
| SetLeashDistance | method | — | — | — |
| SetDetectionDistance | method | — | instance_blackrock_spire/JustDidDialogueStep, scourge_invasion/UpdateAI#9 | — |
| GetGroupLootTimer | method | — | game_Group_Group/SendLootStartRollsForPlayer | — |
| SetEscortable | method | — | ScriptedEscortAI/npc_escortAI | — |
| IsEscortable | method | — | — | — |
| CanAssistPlayers | method | — | ScriptedEscortAI/AssistPlayerInCombat, ScriptedFollowerAI/AssistPlayerInCombat | — |
| CanSummonGuards | method | — | BasicAI/BasicAI, BasicAI/JustRespawned, BasicAI/SummonedCreatureDespawn, scourge_invasion/SelectRandomFlameshockerSpawnTarget | — |
| GetOriginalEntry | method | — | GameEventMgr.Main/operator() | — |
| InitializeReactState | method | — | — | — |
| ChooseDisplayId | method | Log.Main/Out, shared_Util/urand, Unit.Main/GetScaleForDisplayId | Map.ScriptCommands/ScriptCommand_Morph, Map.ScriptCommands/ScriptCommand_Mount, ObjectMgr/GetTaxiMountDisplayId, Unit.SpellAuras/HandleAuraMounted, Unit.SpellAuras/HandleAuraTransform | — |
| ToCreature | function | — | Map.Main/SendEventToAdditionalTargets, Map.Main/SendEventToMainTargets, Map.ScriptCommands/ScriptCommand_AddThreat, Map.ScriptCommands/ScriptCommand_AssistUnit, Map.ScriptCommands/ScriptCommand_AttackStart, Map.ScriptCommands/ScriptCommand_CallForHelp, Map.ScriptCommands/ScriptCommand_CreatureSpells, Map.ScriptCommands/ScriptCommand_DespawnCreature, Map.ScriptCommands/ScriptCommand_Evade, Map.ScriptCommands/ScriptCommand_Flee, Map.ScriptCommands/ScriptCommand_Invincibility, Map.ScriptCommands/ScriptCommand_JoinCreatureGroup, Map.ScriptCommands/ScriptCommand_LeaveCreatureGroup, Map.ScriptCommands/ScriptCommand_ModifyThreat, Map.ScriptCommands/ScriptCommand_Mount, Map.ScriptCommands/ScriptCommand_MoveTo, Map.ScriptCommands/ScriptCommand_RespawnCreature, Map.ScriptCommands/ScriptCommand_SendScriptEvent, Map.ScriptCommands/ScriptCommand_SetActiveObject, Map.ScriptCommands/ScriptCommand_SetCombatMovement, Map.ScriptCommands/ScriptCommand_SetCommandState, Map.ScriptCommands/ScriptCommand_SetDefaultMovement, Map.ScriptCommands/ScriptCommand_SetEquipment, Map.ScriptCommands/ScriptCommand_SetFaction, Map.ScriptCommands/ScriptCommand_SetGossipMenu, Map.ScriptCommands/ScriptCommand_SetHomePosition, Map.ScriptCommands/ScriptCommand_SetMeleeAttack, Map.ScriptCommands/ScriptCommand_SetMovementType, Map.ScriptCommands/ScriptCommand_SetPhase, Map.ScriptCommands/ScriptCommand_SetPhaseRandom, Map.ScriptCommands/ScriptCommand_SetPhaseRange, Map.ScriptCommands/ScriptCommand_SetReactState, Map.ScriptCommands/ScriptCommand_SetRun, Map.ScriptCommands/ScriptCommand_StartWaypoints, Map.ScriptCommands/ScriptCommand_UpdateEntry, Map.ScriptCommands/ScriptCommand_ZoneCombatPulse, PetAI/DoAttack, Player.Main/LeaveCombatWithFarAwayCreatures, ScriptMgr/GetTargetByType, Spell.Effects/EffectDummy, Spell.Main/CheckCast, Spell.Main/CheckTamingSpell, Spell.Main/SetTargetMap, Unit.Main/AttackedBy, Unit.Main/Kill, Unit.SpellAuras/HandleAuraDummy, Unit.SpellAuras/PeriodicDummyTick | — |
| ToCreature#2 | function | — | Conditions/Evaluate | — |
| Update | method | CreatureAI/EnterEvadeMode, CreatureAI/JustRespawned, CreatureAI/UpdateAI, CreatureAI/UpdateAI_corpse, CreatureData/ChooseCreatureId, CreatureGroups/ChooseCreatureId, CreatureLinkingMgr/CanSpawn, CreatureLinkingMgr/DoCreatureLinkingEvent, GameEventMgr.Main/GetCreatureUpdateDataForActiveEvent, Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out, Map.Main/GetCreatureLinkingHolder, Map.Main/GetPersistentState, MotionMaster/Clear, MotionMaster/GetCurrent, MovementAnticheat/IsInKnockBack, MovementGenerator/GetMovementGeneratorType, MovementGenerator/IsReachable, Object/GetEntry, Object/GetGUIDLow, Object/GetObjectGuid, Object/IsInWorld, Object/IsPlayer, ObjectGuid/IsPlayer, Player.Main/GetCheatData, Player.StatSystem/UpdateAllStats, PoolManager/IsPartOfAPool, shared_Util/tickTime, ThreatManager/modifyThreatPercent#2, Unit.Main/CanHaveThreatList, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/ClearUnitState, Unit.Main/GetCharmerOrOwnerGuid, Unit.Main/GetHealthPercent, Unit.Main/GetThreatManager, Unit.Main/GetVictim, Unit.Main/HasDistanceCasterMovement, Unit.Main/HasUnitState, Unit.Main/IsAlive, Unit.Main/ModifyAuraState, Unit.Main/RemoveAllAuras, Unit.Main/SetHealth, Unit.Main/Update, World/getConfig#4, World/GetTimeRate, WorldObject.Object/GetDistance#4, WorldObject.Object/GetMap, WorldObject.Object/GetMapId, WorldObject.Object/IsLikePlayer, WorldObject.Object/IsWithinDist3d, WorldObject.Object/IsWithinLOSInMap, WorldObject.Object/SetUInt32Value, ZoneScript/OnCreatureRespawn | eastern_plaguelands/DoRessurectUnit, Pet.Main/Update, TemporarySummon/Update, Totem/Update | — |
| StartGroupLoot | method | Group/GetId | game_Group_Group/StartLootRoll | — |
| StopGroupLoot | method | game_Group_Group/EndRoll, ObjectMgr/GetGroupById | — | — |
| RegenerateAll | method | Unit.Main/IsInCombat, Unit.Main/IsPolymorphed | — | — |
| RegenerateMana | method | ObjectGuid/IsPlayer, shared_Util/round_float_chance, Unit.Main/GetCharmerOrOwnerGuid, Unit.Main/GetMaxPower, Unit.Main/GetPower, Unit.Main/IsInCombat, Unit.Main/IsUnderLastManaUseEffect, Unit.Main/ModifyPower | Pet.Main/RegenerateAll | — |
| RegenerateHealth | method | ObjectGuid/IsPlayer, Unit.Main/GetCharmerOrOwnerGuid, Unit.Main/GetHealth, Unit.Main/GetMaxHealth, Unit.Main/GetRegenHPPerSpirit, Unit.Main/IsPolymorphed, Unit.Main/ModifyHealth, World/getConfig#2 | Pet.Main/RegenerateAll | — |
| DoFlee | method | Object/GetObjectGuid, SpellCaster/InterruptSpellsWithInterruptFlags, Unit.Main/GetHealthPercent, Unit.Main/GetVictim, Unit.Main/HasAuraType, Unit.Main/HasUnitState, Unit.Main/ModifyAuraState, Unit.Main/SetFleeing, Unit.Main/UpdateSpeed, World/getConfig#4, WorldObject.Object/MonsterTextEmote#2 | boss_herod/UpdateAI#2, boss_timmy_the_cruel/UpdateAI#2, darkshore/UpdateAI, Map.ScriptCommands/ScriptCommand_Flee, thousand_needles/UpdateAI | — |
| DoFleeToGetAssistance | method | Creature.MotionMaster/MoveSeekAssistance, NearestAssistCreatureInCreatureRangeCheck/NearestAssistCreatureInCreatureRangeCheck, Object/GetObjectGuid, ObjectGuid/ObjectGuid, SpellCaster/InterruptSpellsWithInterruptFlags, Unit.Main/GetMotionMaster, Unit.Main/GetVictim, Unit.Main/HasAuraType, Unit.Main/HasUnitState, Unit.Main/SetFleeing, Unit.Main/SetTargetGuid, Unit.Main/UpdateSpeed, World/getConfig#2, World/getConfig#4, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/MonsterTextEmote#2 | Map.ScriptCommands/ScriptCommand_Flee | — |
| GetFleeingSpeed | method | Unit.Main/GetSpeed | FearMovementGenerator/Initialize, PointMovementGenerator/Initialize | — |
| GetBaseWalkSpeedRate | method | Unit.Main/GetDisplayId, Unit.Main/GetMountID, Unit.Main/GetNativeDisplayId | Unit.Main/UpdateSpeed | — |
| GetBaseRunSpeedRate | method | Unit.Main/GetDisplayId, Unit.Main/GetMountID, Unit.Main/GetNativeDisplayId | Unit.Main/UpdateSpeed | — |
| MoveAwayFromTarget | method | Creature.MotionMaster/MoveDistance, SpellCaster/InterruptSpellsWithInterruptFlags, Unit.Main/GetMotionMaster, Unit.Main/HasUnitState | Map.ScriptCommands/ScriptCommand_SetMovementType | — |
| AIM_Initialize | method | Creature.MotionMaster/Initialize, CreatureAISelector/selectAI, Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out | boss_vectus/UpdateAI, instance_molten_core/OnCreatureCreate, instance_molten_core/OnCreatureRespawn, instance_temple_of_ahnqiraj/OnCreatureCreate, Pet.Main/LoadPetFromDB, quest_stormwind_rendezvous/UpdateAI, Spell.Effects/EffectResurrectNew, Spell.Effects/EffectSummon, Spell.Effects/EffectSummonCritter, Spell.Effects/EffectSummonDeadPet, Spell.Effects/EffectSummonGuardian, Spell.Effects/EffectSummonPet#2, Spell.Effects/EffectTameCreature, TemporarySummon/Summon, Totem/Summon, Unit.SpellAuras/HandleModCharm, Unit.SpellAuras/ModPossess | — |
| Create | method | CreatureCreatePos/GetMap, CreatureLinkingMgr/AddMasterToHolder, CreatureLinkingMgr/AddSlaveToHolder, CreatureLinkingMgr/GetLinkedTriggerInformation, CreatureLinkingMgr/IsLinkedEventTrigger, Map.Main/GetCreatureLinkingHolder, Unit.Main/SetWalk, World/getConfig#4, WorldObject.Object/SetMap | ChatHandler.CreatureCommands/HandleEscortShowWpCommand, ChatHandler.CreatureCommands/HandleNpcAddCommand, ChatHandler.CreatureCommands/Helper_CreateWaypointFor, Player.Main/SummonPossessedMinion, WorldObject.Object/SummonCreature, WorldObject.Object/SummonCreature#2 | — |
| IsTrainerOf | method | GossipDef/ClearMenus, GossipDef/SendGossipMenu, Log.Main/Out, Object/GetEntry, Object/GetGUIDLow, Object/GetObjectGuid, Player.Main/GetReputationRank, Player.Main/HasSpell, Unit.Main/GetClass, Unit.Main/GetRace, Unit.Main/IsTrainer, WorldObject.Object/GetFactionTemplateEntry | AiBotAI.Bridge/BridgeHandleTrain, Player.Main/PrepareGossipMenu, WorldObject.Object/BuildValuesUpdate, WorldSession.NPCHandler/HandleTrainerBuySpellOpcode, WorldSession.NPCHandler/SendTrainerList | — |
| CanInteractWithBattleMaster | method | BattleGroundMgr/GetBattleMasterBG, GossipDef/ClearMenus, GossipDef/SendGossipMenu, Object/GetEntry, Object/GetObjectGuid, Player.Main/GetBGAccessByLevel, Unit.Main/IsBattleMaster | Player.Main/PrepareGossipMenu | — |
| CanTrainAndResetTalentsOf | method | Unit.Main/GetClass, Unit.Main/GetLevel | Player.Main/PrepareGossipMenu | — |
| GetOriginalLootRecipient | method | ObjectAccessor/FindPlayer | AiBotAI.Loot/DoAutoLoot, Player.Main/IsAllowedToLoot, Unit.Main/Kill | — |
| GetGroupLootRecipient | method | ObjectMgr/GetGroupById | AiBotAI.Loot/DoAutoLoot, Player.Main/IsAllowedToLoot, Player.Main/SendLoot, Unit.Main/DealDamage, Unit.Main/Kill | — |
| GetLootRecipient | method | Group/GetFirstMember, GroupReference/next, Player.Main/GetGroup | AiBotAI.Main/UpdateAI, ChatHandler.DebugCommands/HandleDebugGetLootRecipientCommand, instance_ruins_of_ahnqiraj/GiveRepAfterRajaxxDeath, Player.Main/SendLoot | — |
| SetLootRecipient | method | Group/GetId, Object/GetObjectGuid, Object/IsPet, ObjectGuid/Clear, ObjectGuid/operator==, Player.Main/GetGroup, Unit.Main/GetCharmerOrOwnerPlayerOrPlayerItself, Unit.Main/GetPetGuid, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag | AiBotAI.Loot/DoAutoLoot, azshara/Reset, boss_bug_trio/JustDied, boss_vectus/EnterEvadeMode, ChatHandler.UnitCommands/HandleDieHelper, CreatureAI/EnterEvadeMode, duskwood/JustDied, instance_dire_maul/JustDied, instance_dire_maul/JustDied#2, moonglade/EnterEvadeMode, ScriptedAI/EnterEvadeMode, ScriptedEscortAI/EnterEvadeMode, ScriptedFollowerAI/EnterEvadeMode, silithus/OnActivateBySpell, Spell.Effects/EffectSummonWild, ThreatListCopier.battleground_alterac/EnterEvadeMode, Unit.Main/DealDamage, Unit.Main/Kill, world_event_wareffort/EnterEvadeMode#2 | — |
| IsTappedBy | method | Errors/PrintStacktraceAndThrow, Group/isBGGroup, Object/GetObjectGuid, ObjectGuid/operator==, Player.Main/GetGroup#2, Unit.Main/GetPetGuid | AiBotAI.Grind/CountNearbyHostiles, AiBotAI.Grind/ScanApproachTarget, AiBotAI.Grind/SelectGrindTarget, AiBotAI.Main/UpdateAI, Spell.Main/CheckCast, Unit.SpellAuras/HandleChannelDeathItem, WorldObject.Object/BuildValuesUpdate | — |
| GenerateLootForBody | method | CreatureAI/FillLoot, Group/GetTeam, Loot/clear, Loot/SetTeam, LootMgr/FillLoot, LootMgr/GenerateMoneyLoot, Player.Main/GetTeam | AiBotAI.Loot/DoAutoLoot, Unit.Main/Kill | — |
| GeneratePlayerDependentLoot | method | Group/GetTeam, Loot/SetTeam, LootMgr/FillPlayerDependentLoot, Player.Main/GetTeam | Unit.Main/Kill | — |
| SaveToDB | method | Log.Main/Out, WorldObject.Object/GetMapId | ChatHandler.CreatureCommands/HandleNpcSpawnSetDeathStateCommand, ChatHandler.CreatureCommands/HandleWpModifyCommand | — |
| SaveToDB#2 | method | Database/BeginTransaction, Database/CommitTransaction, Database/PExecuteLog, Object/GetEntry, Object/GetGUIDLow, ObjectMgr/GetCreatureDisplayInfoAddon, ObjectMgr/NewOrExistCreatureData, Unit.Main/GetNativeDisplayId, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | ChatHandler.CreatureCommands/HandleEscortShowWpCommand, ChatHandler.CreatureCommands/HandleNpcAddCommand | creature |
| GetClassLevelStats | method | ObjectMgr/GetCreatureClassLevelStats, Unit.Main/GetClass, Unit.Main/GetLevel | Pet.Main/InitStatsForLevel, Player.StatSystem/UpdateAttackPowerAndDamage, Player.StatSystem/UpdateDamagePhysical | — |
| SetInitCreaturePowerType | method | Object/ToPet, Pet.Main/GetPetType, Unit.Main/GetClass, Unit.Main/GetCreatePowers, Unit.Main/SetMaxPower, Unit.Main/SetPower, WorldObject.Object/SetByteValue | Pet.Main/InitStatsForLevel | — |
| SelectLevel | method | shared_Util/urand, Unit.Main/SetLevel | Spell.Effects/EffectSummonCritter | — |
| InitStatsForLevel | method | Unit.Main/SetBaseWeaponDamage, Unit.Main/SetCreateHealth, Unit.Main/SetCreateMana, Unit.Main/SetCreateResistance, Unit.Main/SetCreateStat, Unit.Main/SetHealth, Unit.Main/SetHealthPercent, Unit.Main/SetMaxHealth, Unit.Main/SetMaxPower, Unit.Main/SetModifierValue, Unit.Main/SetPower, Unit.Main/SetPowerPercent | ChatHandler.CharacterCommands/HandleLevelUpCommand | — |
| _GetHealthMod | method | World/getConfig#2 | Pet.Main/InitStatsForLevel | — |
| _GetDamageMod | method | World/getConfig#2 | Pet.Main/InitStatsForLevel | — |
| _GetSpellDamageMod | method | World/getConfig#2 | SpellCaster/MeleeDamageBonusDone, SpellCaster/SpellDamageBonusDone | — |
| CreateFromProto | method | CreatureInfo/GetHighGuid, WorldObject.Object/SetZoneScript, WorldObject.Object/_Create | Totem/Create | — |
| LoadFromDB | method | CreatureCreatePos/CreatureCreatePos, CreatureData/ChooseCreatureId, CreatureData/GetCreatureIdCount, CreatureData/GetRandomRespawnTime, CreatureGroups/ChooseCreatureId, CreatureGroups/LoadCreatureGroup, CreatureGroupsManager/instance, CreatureLinkingMgr/CanSpawn, CreatureLinkingMgr/DoCreatureLinkingEvent, CreatureLinkingMgr/IsSpawnedByLinkedMob, GameEventMgr.Main/GetCreatureUpdateDataForActiveEvent, GridMap/GetHeightStatic, Log.Main/Out, Map.Main/GetCreature, Map.Main/GetCreatureLinkingHolder, Map.Main/GetHeight, Map.Main/GetPersistentState, MapPersistentStateMgr/GetCreatureRespawnTime, MapPersistentStateMgr/SaveCreatureRespawnTime, Object/GetGUIDLow, ObjectGuid/ObjectGuid#3, ObjectMgr/GetCreatureAddon, ObjectMgr/GetCreatureData, ObjectMgr/GetCreatureTemplate, Unit.Main/IsAlive, Unit.Main/SetHealth, Unit.Main/SetHealthPercent, Unit.Main/SetPower, Unit.Main/SetPowerPercent, Unit.Main/SetVisibility, WorldObject.Object/GetMap, WorldObject.Object/GetTerrain, WorldObject.Object/Relocate | ChatHandler.CreatureCommands/HandleEscortShowWpCommand, ChatHandler.CreatureCommands/HandleNpcAddCommand, Map.Main/LoadCreatureSpawn, ObjectMgr/AddCreData, ObjectMgr/MoveCreData, PoolManager/Spawn1Object | — |
| LoadEquipment | method | EquipmentTemplate/ChooseEquipmentEntry, ObjectMgr/GetEquipmentTemplate | boss_gordok_king/Reset, boss_mr_smite/Reset, boss_mr_smite/SplineFinished, instance_dire_maul/Reset, instance_dire_maul/UpdateAI, Map.ScriptCommands/ScriptCommand_SetEquipment, ScriptedAI/SetEquipmentSlots, tanaris/Reset#2, tanaris/WaypointReached, Unit.SpellAuras/HandleAuraTransform | — |
| LoadDefaultEquipment | method | ItemPrototype/IsRangedWeapon, ObjectMgr/GetItemPrototype | — | — |
| HasQuest | method | Object/GetEntry, ObjectMgr/GetCreatureQuestRelationsMapBounds | — | — |
| HasInvolvedQuest | method | Object/GetEntry, ObjectMgr/GetCreatureQuestInvolvedRelationsMapBounds | — | — |
| CreatureRespawnDeleteWorker | ctor | — | — | — |
| operator()#2 | method | MapPersistentStateMgr/SaveCreatureRespawnTime | — | — |
| DeleteFromDB | method | Log.Main/Out, Object/GetGUIDLow | ChatHandler.CreatureCommands/HandleEscortHideWpCommand | — |
| DeleteFromDB#2 | method | Database/BeginTransaction, Database/CommitTransaction, Database/PExecuteLog, MapManager/GetContinentInstanceId, ObjectMgr/DeleteCreatureData | ChatHandler.CreatureCommands/HandleNpcDeleteCommand | creature, creature_addon, creature_battleground, creature_movement, game_event_creature, game_event_creature_data |
| GetAttackDistance | method | Aura/GetModifier, Aura/GetSpellProto, Object/IsPet, Pet.Main/IsEnabled, SpellCaster/GetLevelForTarget, Unit.Main/GetAurasByType, Unit.Main/GetCharmerOrOwnerPlayer, Unit.Main/GetLevel, Unit.Main/GetTotalAuraModifier, World/getConfig#2 | BasicAI/MoveInLineOfSight, boss_twinemperors/MoveInLineOfSight, eastern_plaguelands/AttackStart, eastern_plaguelands/AttackStart#2, GuardAI/MoveInLineOfSight, GuardEventAI/MoveInLineOfSight, PetAI/MoveInLineOfSight, PetEventAI/MoveInLineOfSight, ScriptedFollowerAI/MoveInLineOfSight, ScriptedPetAI/MoveInLineOfSight, Unit.SpellAuras/HandleFeignDeath | — |
| SetDeathState | method | Creature.MotionMaster/Initialize, LootStore/HaveLootFor, Object/GetUInt32Value, ObjectGuid/ObjectGuid, shared_Util/urand, Unit.Main/ClearUnitState, Unit.Main/GetHealth, Unit.Main/GetMaxHealth, Unit.Main/SetDeathState, Unit.Main/SetHealth, Unit.Main/SetTargetGuid, Unit.Main/SetWalk, Unit.Main/UpdateSpeed, World/GetActiveSessionCount, World/getConfig, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag, WorldObject.Object/SetUInt32Value | ChatHandler.CreatureCommands/HandleNpcMoveHelperCommand, ChatHandler.CreatureCommands/HandleWpModifyCommand, game_Battlegrounds_BattleGround/SpawnBGCreature, instance_scarlet_monastery/SetData, instance_uldaman/DespawnMinion, instance_uldaman/RespawnMinion, instance_uldaman/SetData, Map.ScriptCommands/ScriptCommand_RespawnCreature, npcs_special/UpdateAI#8, Pet.Main/SetDeathState, Spell.Effects/EffectDummy, ThreatListCopier.battleground_alterac/JustRespawned, Totem/UnSummon | — |
| FallGround | method | Log.Main/Out, Map.Main/GetHeight, Map.Main/GetId, MoveSplineInit/Launch, MoveSplineInit/MoveSplineInit, MoveSplineInit/MoveTo#2, MoveSplineInit/SetFall, Object/GetEntry, Unit.Main/GetDeathState, Unit.Main/SetDeathState, WorldObject.Object/GetMap, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | — | — |
| CastSpawnSpell | method | Log.Main/Out, Object/GetGuidStr, SpellCaster/CastSpell#2, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag | — | — |
| Respawn | method | Map.Main/GetPersistentState, MapPersistentStateMgr/SaveCreatureRespawnTime, Object/GetGUIDLow, Unit.Main/GetVisibility, Unit.Main/SetVisibility, WorldObject.Object/GetMap, WorldObject.Object/SetUnitMovementFlags | boss_bug_trio/LeashEncounter, boss_buru/UpdateAI, boss_cthun/CheckRespawnEye, boss_golemagg/KillAdds, boss_golemagg/Reset, boss_gothik/EnterEvadeMode, boss_jeklik/EnterEvadeMode, boss_onyxia/Aggro#2, boss_razorgore/MortPhaseUn, boss_razorgore/SituationInitiale, boss_sartura/LeashEncounter, boss_sartura/LeashEncounter#2, boss_tomb_of_seven/CallToFight, boss_twinemperors/Reset, boss_twinemperors/Reset#2, boss_vectus/SpellHit, boss_venoxis/JustReachedHome, ChatHandler.CreatureCommands/HandleNpcMoveHelperCommand, ChatHandler.CreatureCommands/HandleNpcSpawnSetDeathStateCommand, ChatHandler.CreatureCommands/HandleRespawnCommand, ChatHandler.CreatureCommands/HandleWpModifyCommand, CreatureGroups/Respawn, CreatureLinkingMgr/DoCreatureLinkingEvent, CreatureLinkingMgr/ProcessSlave, eastern_plaguelands/DoRessurectUnit, GridNotifiers/operator()#2, instance_blackrock_spire/AreaTrigger_at_blackrock_spire, instance_maraudon/Update, instance_naxxramas.Main/OnCreatureCreate, instance_naxxramas.Main/SetData, instance_ruins_of_ahnqiraj/OnCreatureEnterCombat, instance_scarlet_monastery/SetData, instance_temple_of_ahnqiraj/RestoreOuroSpawnTrigger, instance_temple_of_ahnqiraj/SetData, instance_uldaman/RespawnMinion, instance_uldaman/SetData, instance_zulgurub/ProcessEventId_event_summon_gahzranka, Map.ScriptCommands/ScriptCommand_RespawnCreature, mob_anubisath_sentinel/Reset, moonglade/UpdateAI#2, quest_stormwind_rendezvous/UpdateAI, ScriptedEscortAI/ResetEscort, ScriptedEscortAI/UpdateAI, ThreatListCopier.battleground_alterac/JustRespawned, ThreatListCopier.battleground_alterac/Reset#17, WorldObject.Object/RespawnNearCreaturesByEntry | — |
| DespawnOrUnsummon | method | Pet.Main/DelayedUnsummon, TemporarySummon/UnSummon | ashenvale/JustDied, boss_loatheb/EnterEvadeMode, boss_maexxna/JustDied#2, boss_majordomo_executus/Reset, boss_majordomo_executus/UpdateAI, boss_omen/JustDied, ChatHandler.CreatureCommands/HandleNpcDespawnCommand, desolace/DespawnCaravan, eastern_plaguelands/JustDied#2, eastern_plaguelands/JustReachedHome, instance_blackrock_depths/SetData, instance_blackrock_spire/DespawnStadiumSpectators, instance_blackrock_spire/OnCreatureEvade, instance_blackrock_spire/SetData, instance_dire_maul/SummonedCreatureJustDied, instance_ruins_of_ahnqiraj/GiveRepAfterRajaxxDeath, instance_scarlet_monastery/SetData, Map.ScriptCommands/ScriptCommand_DespawnCreature, npcs_special/EndEvent, quest_stormwind_rendezvous/JustDied, quest_stormwind_rendezvous/UpdateAI, scourge_invasion/SpellHit#6, Spell.Effects/EffectDummy, stormwind_city/ResetThug, ungoro_crater/UpdateAI, Unit.AuraProcHandler/HandleDummyAuraProc, Unit.SpellAuras/HandleAuraDummy, Unit.SpellAuras/PeriodicDummyTick | — |
| ForcedDespawn | method | EventProcessor/AddEvent, EventProcessor/CalculateTime, ForcedDespawnDelayEvent/ForcedDespawnDelayEvent, Unit.Main/IsAlive, Unit.Main/SetHealth | arena_challenge_ai/EnterEvadeMode, blackrock_depths/CheckForWipe, boss_archaedas/UpdateAI, boss_arlokk/JustReachedHome, boss_bug_trio/JustDied, boss_cannon_master_willey/EnterEvadeMode, boss_cthun/CheckRespawnEye, boss_herod/DespawnMyrmidons, boss_lethon/SpellHitTarget#2, boss_lethon/UpdateAI, boss_noth/SummonedCreatureJustDied, boss_onyxia/EnterEvadeMode, boss_ossirian/JustDied, boss_ouro/DespawnCreatures, boss_ouro/JustReachedHome, boss_ouro/JustSummoned#2, boss_ouro/UpdateAI#2, boss_ouro/UpdateAI#3, boss_skeram/JustDied, boss_thermaplugg/JustReachedHome, boss_vaelastrasz/Aggro, boss_vectus/JustDied, boss_vectus/SpellHit, boss_venoxis/EnterEvadeMode, boss_victor_nefarius/SummonedCreatureJustDied, boss_viscidus/SummonedMovementInform, burning_steppes/DemonDespawn, CreatureLinkingMgr/ProcessSlave, darkshore/JustSummoned, darkshore/UpdateAI, darkshore/UpdateAI#2, desolace/DespawnCaravan, eastern_plaguelands/DespawnAll, eastern_plaguelands/DespawnAll#2, eastern_plaguelands/DespawnGuid, eastern_plaguelands/JustSummoned, eastern_plaguelands/MovementInform, eastern_plaguelands/SummonedMovementInform#2, eastern_plaguelands/UpdateAI, eastern_plaguelands/UpdateAI#5, felwood/SpellHit, felwood/SpellHit#2, feralas/JustDied#3, feralas/MoveInLineOfSight, feralas/UpdateAI#4, gnomeregan/JustDied, hinterlands/UpdateEscortAI, instance_blackrock_depths/HandleBarPatrons, instance_blackrock_spire/JustDidDialogueStep, instance_blackwing_lair/OnCreatureRespawn, instance_dire_maul/MovementInform#3, instance_molten_core/OnCreatureCreate, instance_naxxramas.boss_kelthuzad/UpdateAI#4, instance_naxxramas.Main/OnCreatureCreate, instance_naxxramas.Main/OnCreatureDeath, instance_ruins_of_ahnqiraj/OnCreatureDeath, instance_temple_of_ahnqiraj/JustDidDialogueStep, instance_temple_of_ahnqiraj/Start, instance_wailing_caverns/SetData, instance_zulgurub/OnCreatureDeath, molten_core/Kill_Self, moonglade/DoDespawnSummoned, moonglade/EnterEvadeMode, moonglade/UpdateAI, moonglade/UpdateAI#2, moonglade/UpdateEscortAI, npcs_special/EnterEvadeMode#3, npcs_special/MoveInLineOfSight#4, npcs_special/SpellHit#3, npcs_special/UpdateAI#16, npcs_special/UpdateAI#18, quest_stormwind_rendezvous/UpdateAI, scourge_invasion/DespawnCultists, scourge_invasion/DespawnShadowsOfDoom, ScriptedFollowerAI/MovementInform, silithus/AbortScene, silithus/DemonDespawn, silithus/ResetEvent, silithus/SummonedMovementInform, silithus/UpdateAI#4, silithus/UpdateAI#7, Spell.Effects/EffectDummy, Spell.Effects/EffectTameCreature, Spell.Main/SendChannelUpdate, ThreatListCopier.boss_ragnaros/UpdateAI, ungoro_crater/DemonDespawn, ungoro_crater/EnterEvadeMode, ungoro_crater/JustReachedHome, ungoro_crater/Transform, wailing_caverns/JustDied, wailing_caverns/MovementInform, wetlands/UpdateEscortAI, winterspring/DemonDespawn, winterspring/SpellHit#2, zulfarrak/MovementInform | — |
| IsImmuneToSpell | method | SpellEntry/HasAttribute, SpellEntry/IsIgnoringCasterAndTargetRestrictions, SpellEntry/IsPositiveSpell#4, Unit.Main/IsImmuneToSpell | — | — |
| IsImmuneToDamage | method | SpellEntry/HasAttribute, SpellEntry/IsIgnoringCasterAndTargetRestrictions, Unit.Main/IsImmuneToDamage | — | — |
| IsImmuneToSpellEffect | method | SpellEntry/IsIgnoringCasterAndTargetRestrictions, Unit.Main/IsImmuneToSpellEffect | Totem/IsImmuneToSpellEffect | — |
| IsVisibleInGridForPlayer | method | Player.Main/GetCorpse, Player.Main/GetDeathTimer, Player.Main/IsGameMaster, Unit.Main/IsAlive, World/getConfig#2, WorldObject.Object/IsWithinDistInMap | — | — |
| SendAIReaction | method | ByteBuffer/operator<<#10, Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out, Object/GetObjectGuid, ObjectGuid/operator<<, WorldObject.Object/SendObjectMessageToSet, WorldPacket/WorldPacket#4 | CreatureAI/TriggerAlertDirect, Unit.Main/Attack | — |
| CallAssistance | method | AnyAssistCreatureInRangeCheck/AnyAssistCreatureInRangeCheck, EventProcessor/AddEvent, EventProcessor/CalculateTime, Object/GetObjectGuid, Unit.Main/GetVictim, Unit.Main/IsCharmed, World/getConfig#2, World/getConfig#4 | PointMovementGenerator/Finalize, Unit.Main/Attack | — |
| CallForHelp | method | CallOfHelpCreatureInRangeDo/CallOfHelpCreatureInRangeDo, Unit.Main/GetVictim, Unit.Main/IsCharmed | boss_emperor_dagran_thaurissan/Aggro, boss_emperor_dagran_thaurissan/UpdateAI, boss_razuvious/Aggro, boss_razuvious/Aggro#2, instance_blackwing_lair/Aggro#2, instance_dire_maul/UpdateAI#2, instance_dire_maul/UpdateAI#4, instance_naxxramas.Main/Aggro#2, Map.ScriptCommands/ScriptCommand_CallForHelp | — |
| CanAssistTo | method | — | — | — |
| CanBeTargetedByCallForHelp | method | Object/HasFlag, Unit.Main/GetCharmerOrOwnerGuid, Unit.Main/GetFactionTemplateId, Unit.Main/HasUnitState, Unit.Main/IsAlive, Unit.Main/IsFriendlyTo, Unit.Main/IsInCombat, Unit.Main/IsTargetableBy, WorldObject.Object/HasFactionTemplateFlag | — | — |
| CanRespondToCallForHelpAgainst | method | Map.Main/IsDungeon, Unit.Main/HasReactState, Unit.Main/IsCharmerOrOwnerPlayerOrPlayerItself, WorldObject.Object/GetMap, WorldObject.Object/HasFactionTemplateFlag, WorldObject.Object/IsWithinVisibilityDistanceOf | — | — |
| CanFleeFromCallForHelpAgainst | method | Creature.MotionMaster/GetCurrentMovementGeneratorType, Unit.Main/GetMotionMaster#2, Unit.Main/IsRooted, WorldObject.Object/GetDistance#3, WorldObject.Object/HasFactionTemplateFlag | — | — |
| CanInitiateAttack | method | Object/HasFlag, Unit.Main/HasReactState, Unit.Main/HasUnitState, Unit.Main/IsAlive, Unit.Main/IsNeutralToAll | BasicAI/MoveInLineOfSight, boss_four_horsemen/AggroRadius, boss_four_horsemen/MoveInLineOfSight, boss_heigan/MoveInLineOfSight, boss_loatheb/MoveInLineOfSight, boss_loatheb/MoveInLineOfSight#2, boss_maexxna/MoveInLineOfSight, boss_razuvious/MoveInLineOfSight, GuardAI/MoveInLineOfSight, GuardEventAI/MoveInLineOfSight, PetAI/MoveInLineOfSight, PetEventAI/MoveInLineOfSight, ScriptedFollowerAI/MoveInLineOfSight, ScriptedPetAI/MoveInLineOfSight, world_event_wareffort/MoveInLineOfSight, world_event_wareffort/MoveInLineOfSight#2 | — |
| DynamicRespawnRatesChecker | ctor | Unit.Main/GetLevel, World/getConfig#4 | — | — |
| operator()#3 | method | Player.Main/GetEscortingGuid, Unit.Main/GetLevel | — | — |
| GetCount | method | — | — | — |
| HasNearbyEscort | method | — | — | — |
| ApplyDynamicRespawnDelay | method | GridMap/IsOutdoors, Object/IsInWorld, Unit.Main/GetLevel, World/getConfig#2, World/getConfig#4, WorldObject.Object/GetMapId, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/GetTerrain | — | — |
| SaveRespawnTime | method | Map.Main/GetPersistentState, MapPersistentStateMgr/SaveCreatureRespawnTime, Object/GetGUIDLow, WorldObject.Object/GetMap | burning_steppes/DemonDespawn, burning_steppes/JustDied, Map.Main/LoadCreatureSpawn, molten_core/JustDied, molten_core/JustDied#2, PoolManager/Spawn1Object, ScriptedInstance/Update, silithus/DemonDespawn, silithus/JustDied#4, ungoro_crater/DemonDespawn, ungoro_crater/JustDied, winterspring/DemonDespawn, winterspring/JustDied | — |
| IsOutOfThreatArea | method | Map.Main/IsDungeon, World/getConfig#2, WorldObject.Object/GetMap, WorldObject.Object/IsInMap, WorldObject.Object/IsWithinDist3d | PetAI/_needToStop, ThreatManager/selectNextVictim | — |
| GetLastLeashExtensionTimePtr | method | — | CreatureGroups/MemberAssist | — |
| SetLastLeashExtensionTimePtr | method | — | CreatureGroups/MemberAssist | — |
| ClearLastLeashExtensionTimePtr | method | — | Unit.Main/CombatStop | — |
| GetLastLeashExtensionTime | method | — | — | — |
| UpdateLeashExtensionTime | method | — | boss_taerar/UpdateDragonAI, TargetedMovementGenerator/Update, ungoro_crater/DamageTaken, ungoro_crater/DamageTaken#2, Unit.Main/SetInCombatWithAggressor | — |
| LoadDefaultAuras | method | Log.Main/Out, Object/GetEntry, Object/GetGUIDLow, SpellCaster/CastSpell, SpellMgr/GetSpellEntry, SpellMgr/Instance, Unit.Main/HasAura#2 | Pet.Main/Create | — |
| LoadCreatureAddon | method | Unit.Main/Mount, Unit.Main/SetSheath, Unit.Main/SetStandState, WorldObject.Object/SetUInt32Value | boss_lord_alexei_barov/Reset, boss_vectus/EnterEvadeMode, HomeMovementGenerator/Finalize, moonglade/EnterEvadeMode, ScriptedAI/EnterEvadeMode, Spell.Effects/EffectSummonGuardian, Totem/Create, world_event_wareffort/EnterEvadeMode#2 | — |
| SendZoneUnderAttackMessage | method | ByteBuffer/operator<<#10, Map.Main/SendToPlayers, Player.Main/GetTeam, WorldObject.Object/GetAreaId, WorldObject.Object/GetMap, WorldPacket/WorldPacket#4 | Unit.Main/Kill | — |
| SetInCombatWithZone | method | CreatureAI/AttackStart, LinkedListHead/isEmpty, Log.Main/Out, Map.Main/GetId, Map.Main/GetPlayers, Map.Main/IsDungeon, Object/GetEntry, Unit.Main/CanHaveThreatList, Unit.Main/GetPet, Unit.Main/GetVictim, Unit.Main/IsInCombat, WorldObject.Object/FindNearestHostilePlayer, WorldObject.Object/GetMap, WorldObject.Object/IsValidAttackTarget | blackrock_depths/SummonRingBoss, blackrock_depths/SummonRingMob, boss_anubrekhan/Aggro, boss_anubrekhan/ExplodeOneDeadCryptGuard, boss_anubrekhan/UpdateAI, boss_arlokk/Aggro, boss_ayamiss/JustSummoned, boss_baron_geddon/Aggro, boss_broodlord_lashlayer/Aggro, boss_broodlord_lashlayer/MoveInLineOfSight, boss_broodlord_lashlayer/UpdateAI, boss_bug_trio/JustSummoned, boss_buru/DamageTaken, boss_buru/EnterCombat, boss_buru/JustDied#2, boss_buru/UpdateAI, boss_cannon_master_willey/JustSummoned, boss_chromaggus/Aggro, boss_cthun/Aggro, boss_cthun/cthunPortalTentacle, boss_cthun/Reset#7, boss_cthun/SpawnEyeTentacles, boss_cthun/UpdateAI#2, boss_cthun/UpdateTransitionPhase, boss_ebonroc/Aggro, boss_fankriss/JustSummoned, boss_fankriss/UpdateAI#3, boss_firemaw/Aggro, boss_flamegor/Aggro, boss_four_horsemen/Reset, boss_garr/Aggro, boss_garr/Aggro#2, boss_gehennas/Aggro, boss_gluth/SummonAdd, boss_gothik/Aggro, boss_gothik/OpenTheGate, boss_gothik/SummonAdd, boss_grobbulus/SpellHitTarget, boss_halycon/JustDied, boss_heigan/Aggro, boss_ironaya/Aggro, boss_jindo/UpdateAI#2, boss_kurinnaxx/Aggro, boss_lucifron/Aggro, boss_mandokir/Aggro, boss_moam/Aggro, boss_nefarian/Aggro, boss_nefarian/JustSummoned, boss_nefarian/UpdateAI, boss_noth/Aggro, boss_noth/JustSummoned, boss_onyxia/Aggro, boss_onyxia/Aggro#2, boss_onyxia/CheckForTargetsInAggroRadius, boss_ossirian/Aggro, boss_ouro/JustSummoned#2, boss_razorgore/EnterCombat, boss_razorgore/PhaseSwitch, boss_razorgore/PopAdd, boss_razorgore/UpdateAI#2, boss_sartura/Aggro, boss_sartura/Aggro#2, boss_skeram/JustSummoned, boss_sulfuron_harbinger/Aggro, boss_tendris_warpwood/Aggro, boss_thaddius/Aggro#4, boss_thaddius/TransitionToPhase, boss_thaddius/UpdateAI#3, boss_tomb_of_seven/CallToFight, boss_twinemperors/Aggro, boss_twinemperors/GoBeBadBug, boss_twinemperors/UpdateAI, boss_vaelastrasz/Aggro, boss_victor_nefarius/Aggro, boss_victor_nefarius/JustSummoned, boss_victor_nefarius/UpdateAI, CreatureLinkingMgr/ProcessSlave, instance_blackfathom_deeps/DoSpawnMobs, instance_blackrock_depths/DoSummonCreatureAndAttack, instance_blackwing_lair/OnCreatureEnterCombat, instance_naxxramas.boss_kelthuzad/Aggro, instance_naxxramas.boss_kelthuzad/SpawnAndSendP1Creature, instance_naxxramas.boss_kelthuzad/UpdateP1, instance_naxxramas.boss_kelthuzad/UpdateP2P3, instance_naxxramas.Main/OnPlayerDeath, instance_scarlet_monastery/SetData, instance_sunken_temple/SetData, instance_temple_of_ahnqiraj/DoHandleTempleAreaTrigger, Map.ScriptCommands/ScriptCommand_ZoneCombatPulse, mob_anubisath_sentinel/Aggro, molten_core/Aggro, molten_core/Aggro#2, ruins_of_ahnqiraj/Aggro, ruins_of_ahnqiraj/Aggro#2, ruins_of_ahnqiraj/Aggro#3, ruins_of_ahnqiraj/Aggro#4, ruins_of_ahnqiraj/Aggro#5, ruins_of_ahnqiraj/Aggro#6, ruins_of_ahnqiraj/Aggro#7, ruins_of_ahnqiraj/Aggro#9, ruins_of_ahnqiraj/UpdateAI#8, ThreatListCopier.battleground_alterac/Aggro#9, ThreatListCopier.boss_ragnaros/Aggro, ThreatListCopier.boss_ragnaros/UpdateAI, Unit.Main/SetInCombatState, Unit.SpellAuras/TriggerSpell, zulfarrak/OnTrigger_at_antusul, zulgurub_trash/Aggro, zulgurub_trash/Aggro#2, zulgurub_trash/UpdateAI#6 | — |
| MeetsSelectAttackingRequirement | method | Object/GetTypeId, Object/HasFlag, Object/ToCreature#2, Object/ToPet#2, Object/ToPlayer#2, Player.Main/IsGameMaster, SpellEntry/IsTargetInRange, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/GetPowerType, Unit.Main/IsDead, Unit.Main/IsFriendlyTo, WorldObject.Object/IsWithinLOSInMap | — | — |
| LogDeath | method | Database/CreateStatement, Log.Main/IsSmartLog, Map.Main/Instanceable, Object/GetEntry, Object/GetGUIDLow, Object/ToCreature, Player.Main/GetName, SqlPreparedStatement/Execute#2, SqlStatement/addInt32, SqlStatement/addString#2, SqlStatement/addString#3, SqlStatementID/SqlStatementID, Unit.Main/GetCharmerOrOwnerPlayerOrPlayerItself, World/getConfig, WorldObject.Object/GetInstanceId, WorldObject.Object/GetMap, WorldObject.Object/GetMapId | Unit.Main/Kill | smartlog_creature |
| LogLongCombat | method | Database/CreateStatement, Object/GetEntry, Object/GetGUIDLow, SqlPreparedStatement/Execute#2, SqlStatement/addInt32, SqlStatement/addString#2, SqlStatement/addString#3, SqlStatementID/SqlStatementID, World/getConfig, WorldObject.Object/GetMapId | — | smartlog_creature |
| SelectAttackingTarget#2 | method | SpellMgr/GetSpellEntry, SpellMgr/Instance | blackrock_depths/UpdateAI, boss_bug_trio/UpdateBugAI#3, boss_four_horsemen/UpdateAI#3, boss_four_horsemen/UpdateAI#4, boss_four_horsemen/UpdateAI#5, boss_gothik/UpdateAI, boss_jindo/UpdateAI, boss_ouro/UpdateAI, instance_dire_maul/UpdateAI#4, instance_naxxramas.boss_kelthuzad/UpdateP2P3, instance_temple_of_ahnqiraj/UpdateAI, molten_core/UpdateAI#5 | — |
| SelectAttackingTarget | method | shared_Util/urand, ThreatManager/getThreatList, Unit.Main/CanHaveThreatList, Unit.Main/GetThreatManager#2, WorldObject.Object/GetCombatDistance, WorldObject.Object/GetDistance3dToCenter#3 | arena_challenge_ai/UpdateAI, arena_challenge_ai/UpdateAI#3, arena_challenge_ai/UpdateAI#4, arena_challenge_ai/UpdateAI#7, blackrock_depths/UpdateAI, boss_anubrekhan/ExplodeOneDeadCryptGuard, boss_anubrekhan/UpdateAI, boss_anubshiah/UpdateAI, boss_arcanist_doan/UpdateAI, boss_arlokk/JustSummoned, boss_arlokk/UpdateAI, boss_ayamiss/UpdateAI, boss_baroness_anastari/UpdateAI, boss_baron_geddon/UpdateAI, boss_celebras_the_cursed/UpdateAI, boss_chromaggus/UpdateAI, boss_cthun/EnterDarkGlarePhase, boss_cthun/SelectHostileTargetMelee, boss_dathrohan_balnazzar/UpdateAI, boss_dragon_of_nightmare/GetNextTarget, boss_emeriss/UpdateDragonAI, boss_emperor_dagran_thaurissan/UpdateAI, boss_faerlina/UpdateAI, boss_fankriss/HandleHatchlings, boss_fankriss/JustSummoned, boss_fankriss/UpdateAI#3, boss_gahzranka/UpdateAI, boss_gehennas/UpdateAI, boss_gluth/SummonAdd, boss_golemagg/UpdateEvents, boss_gordok_king/UpdateAI#2, boss_gothik/ResetThreatAndAttackNearestTarget, boss_gothik/SummonAdd, boss_hakkar/UpdateAI, boss_high_inquisitor_fairbanks/UpdateAI, boss_high_interrogator_gerstahn/UpdateAI, boss_huhuran/UpdateAI, boss_illucia_barov/UpdateAI, boss_immol_thar/UpdateAI, boss_instructor_malicia/UpdateAI, boss_ironaya/UpdateAI, boss_jandice_barov/UpdateAI, boss_jeklik/DoAttack, boss_jeklik/UpdateAI, boss_jindo/UpdateAI, boss_jindo/UpdateAI#2, boss_kurinnaxx/UpdateAI, boss_loatheb/UpdateAI, boss_lord_alexei_barov/UpdateAI, boss_lucifron/UpdateAI, boss_maexxna/JustSummoned, boss_majordomo_executus/UpdateAI, boss_mandokir/UpdateAI, boss_marli/JustSummoned, boss_marli/UpdateAI, boss_moam/JustSummoned, boss_moam/UpdateAI, boss_mr_smite/PhaseEquipEnd, boss_nerubenkan/UpdateAI, boss_noth/BlinkAndRepeatEvent, boss_noth/OnRemoveVulnerability, boss_onyxia/JustSummoned, boss_ouro/JustSummoned, boss_ouro/UpdateAI, boss_overlord_wyrmthalak/JustSummoned, boss_ras_frostwhisper/UpdateAI, boss_renataki/UpdateAI, boss_sapphiron/UpdateAI, boss_sartura/AssignRandomThreat, boss_sartura/AssignRandomThreat#2, boss_shadow_hunter_voshgajin/UpdateAI, boss_shazzrah/UpdateAI, boss_sulfuron_harbinger/UpdateAI, boss_taerar/UpdateDragonAI, boss_thaddius/DoSpellChain, boss_thaddius/HandleReviveEvent, boss_thaddius/TransitionToPhase, boss_thaddius/UpdateAI#3, boss_the_beast/UpdateAI, boss_tomb_of_seven/JustSummoned, boss_tomb_of_seven/UpdateAI, boss_vaelastrasz/UpdateAI#2, boss_venoxis/UpdateAI, boss_victor_nefarius/JustSummoned, boss_victor_nefarius/UpdateAI, boss_viscidus/UpdateAI, boss_ysondre/JustSummoned, boss_ysondre/UpdateAI, boss_ysondre/UpdateDragonAI, boss_zevrim/UpdateAI, dreadsteed_ritual/UpdateAI, dreadsteed_ritual/UpdateAI#2, duskwood/UpdateAI#3, eastern_plaguelands/UpdateAI#2, feralas/UpdateAI, feralas/UpdateAI#2, feralas/UpdateAI#3, instance_blackwing_lair/UpdateAI#4, instance_dire_maul/JustSummoned, instance_dire_maul/UpdateAI#2, instance_dire_maul/UpdateAI#6, instance_dire_maul/UpdateAI#7, instance_naxxramas.boss_kelthuzad/SpawnAndSendP1Creature, instance_naxxramas.boss_kelthuzad/UpdateAI#5, instance_naxxramas.boss_kelthuzad/UpdateAI#6, instance_naxxramas.boss_kelthuzad/UpdateP1, instance_naxxramas.boss_kelthuzad/UpdateP2P3, instance_naxxramas.Main/OnPlayerDeath, molten_core/JustSummoned, molten_core/UpdateAI, molten_core/UpdateAI#4, moonglade/UpdateEscortAI, npc_sandstalker/Aggro, npc_sandstalker/UpdateAI, PlayerAI/PlayerControlledAI, PlayerAI/UpdateAI#2, ruins_of_ahnqiraj/UpdateAI, ruins_of_ahnqiraj/UpdateAI#10, ruins_of_ahnqiraj/UpdateAI#11, ruins_of_ahnqiraj/UpdateAI#12, ruins_of_ahnqiraj/UpdateAI#2, ScriptMgr/GetTargetByType, silithus/UpdateAI#2, Spell.Effects/EffectDummy, ThreatListCopier.battleground_alterac/UpdateAI#12, ThreatListCopier.battleground_alterac/UpdateAI#8, ThreatListCopier.boss_ragnaros/CheckForMelee, ThreatListCopier.boss_ragnaros/SummonSonsOfFlame, ThreatListCopier.boss_ragnaros/UpdateAI, ubrs_trash/UpdateAI, uldaman/UpdateAI#2, undercity/UpdateAI, wailing_caverns/UpdateEscortAI, world_event_wareffort/UpdateAI#5 | — |
| IsInEvadeMode | method | Creature.MotionMaster/GetCurrentMovementGeneratorType, CreatureGroups/GetLeaderGuid, CreatureGroups/IsFormation, Map.Main/GetCreature, Object/GetObjectGuid, ObjectGuid/operator!=, Unit.Main/GetMotionMaster#2, Unit.Main/GetOwnerCreature, Unit.Main/IsInCombat, WorldObject.Object/GetMap | boss_onyxia/CheckForTargetsInAggroRadius, boss_ouro/UpdateAI, boss_sartura/UpdateAI#3, CreatureEventAI/ProcessEvent, instance_blackwing_lair/GOHello_go_orb_of_domination, instance_naxxramas.Main/HandleEvadeOutOfHome, PetAI/SelectNextTarget, PetAI/_needToStop, ScriptedAI/EnterEvadeIfOutOfCombatArea, Spell.Main/CheckAtDelay, SpellCaster/DealDamageMods, SpellCaster/DealSpellDamage, SpellCaster/SpellHitResult, ThreatListCopier.battleground_alterac/GetAIInformation, ThreatListCopier.battleground_alterac/UpdateAI#10, Unit.Main/Attack, Unit.Main/DealMeleeDamage, Unit.Main/RollMeleeOutcomeAgainst#2 | — |
| HasSpell | method | — | WorldSession.PetHandler/HandlePetCastSpellOpcode, WorldSession.PetHandler/HandlePetSetAction, WorldSession.PetHandler/HandlePetSpellAutocastOpcode | — |
| LockOutSpells | method | SpellCaster/LockOutSpells | — | — |
| AddCooldown | method | CooldownContainer/AddCooldown, Object/GetObjectGuid, ObjectGuid/IsPlayer, Player.Main/SendSpellCooldown, Player.Main/ToPlayer, SpellEntry/GetCastTime, Unit.Main/GetCharmer, Unit.Main/GetCharmerGuid, Unit.Main/GetSpellModOwner, World/GetCurrentClockTime | — | — |
| GetRespawnTimeEx | method | — | boss_skeram/JustDied, boss_vaelastrasz/QuestAccept_vaelastrasz, ChatHandler.CreatureCommands/HandleNpcInfoCommand, Player.Main/SendLoot, Unit.Main/Kill | — |
| GetRespawnCoord | method | Errors/PrintStacktraceAndThrow, GridDefines/IsValidMapCoord#3, WorldObject.Object/PrintCoordinatesError | boss_archaedas/boss_archaedasAI, boss_razorgore/SituationInitiale, CreatureGroups/Respawn, CreatureLinkingMgr/CanSpawn, CreatureLinkingMgr/IsSlaveInRangeOfMaster, CreatureLinkingMgr/IsSlaveInRangeOfMaster#2, CreatureLinkingMgr/SetFollowing, HomeMovementGenerator/_setTargetLocation, instance_blackfathom_deeps/DoSpawnMobs, Map.Main/AddToActive, Map.Main/CreatureRespawnRelocation, Map.Main/RemoveFromActive, ObjectGridLoader/Visit#5, quest_stormwind_rendezvous/EndScene, quest_stormwind_rendezvous/UpdateAI#2, quest_stormwind_rendezvous/UpdateAI_corpse, ScriptedEscortAI/UpdateAI, Transport/TeleportTransport | — |
| AllLootRemovedFromCorpse | method | Object/HasFlag, World/getConfig#2 | AiBotAI.Loot/DoAutoLoot, WorldSession.LootHandler/DoLootRelease | — |
| GetAIName | method | — | ChatHandler.CreatureCommands/HandleNpcAIInfoCommand, ChatHandler.CreatureCommands/HandleNpcInfoCommand, CreatureAISelector/selectAI, CreatureEventAI/Permissible, GuardEventAI/Permissible, PetEventAI/Permissible | — |
| GetScriptName | method | ScriptMgr/GetScriptName#2 | ChatHandler.CreatureCommands/HandleNpcAIInfoCommand, ChatHandler.CreatureCommands/HandleNpcInfoCommand, ChatHandler.CreatureCommands/HandleWpShowCommand | — |
| GetScriptId | method | — | ScriptMgr/GetCreatureAI, ScriptMgr/GetDialogStatus, ScriptMgr/OnAuraDummy, ScriptMgr/OnEffectDummy, ScriptMgr/OnGossipHello, ScriptMgr/OnGossipSelect, ScriptMgr/OnQuestAccept, ScriptMgr/OnQuestRewarded | — |
| GetVendorItems | method | Object/GetEntry, ObjectMgr/GetNpcVendorItemList | Player.Main/BuyItemFromVendor, Player.Main/PrepareGossipMenu, WorldSession.ItemHandler/SendListInventory | — |
| GetVendorTemplateItems | method | ObjectMgr/GetNpcVendorTemplateItemList | Player.Main/BuyItemFromVendor, Player.Main/PrepareGossipMenu, WorldSession.ItemHandler/SendListInventory | — |
| GetVendorItemCurrentCount | method | ObjectMgr/GetItemPrototype | Player.Main/BuyItemFromVendor, WorldSession.ItemHandler/SendListInventory | — |
| UpdateVendorItemCurrentCount | method | ObjectMgr/GetItemPrototype, shared_Util/urand, VendorItemCount/VendorItemCount, World/GetActiveSessionCount | Player.Main/BuyItemFromVendor | — |
| GetTrainerTemplateSpells | method | ObjectMgr/GetNpcTrainerTemplateSpells | AiBotAI.Bridge/BridgeHandleTrain, WorldSession.NPCHandler/HandleTrainerBuySpellOpcode, WorldSession.NPCHandler/SendTrainerList | — |
| GetTrainerSpells | method | Object/GetEntry, ObjectMgr/GetNpcTrainerSpells | AiBotAI.Bridge/BridgeHandleTrain, WorldSession.NPCHandler/HandleTrainerBuySpellOpcode, WorldSession.NPCHandler/SendTrainerList | — |
| GetNameForLocaleIdx | method | Object/GetEntry, ObjectMgr/GetCreatureLocale | Pet.Main/GetNameForLocaleIdx, Pet.Main/InitializeDefaultName | — |
| SetFactionTemporary | method | Unit.Main/SetFactionTemplateId | ashenvale/QuestAccept_npc_torek, azshara/MovementInform, blackrock_depths/AttackThief, blackrock_depths/UpdateAI, boss_archaedas/UpdateAI, boss_cthun/Pull, burning_steppes/EffectDummyCreature_spell_capture_grark, ChatHandler.CreatureCommands/HandleNpcSetFactionIdCommand, darkshore/QuestAccept_npc_therylune, darkshore/StartEscort, desolace/CaravanFaction, desolace/QuestAccept_npc_dalinda_malem, desolace/WaypointReached#3, felwood/QuestAccept_npc_arei, felwood/WaypointReached#2, feralas/UpdateAI#4, gnomeregan/UpdateEscortAI, hinterlands/QuestAccept_npc_rinji, instance_blackrock_depths/HandleBarPatrons, instance_blackrock_depths/OnCreatureCreate, instance_blackrock_depths/SetData, instance_dire_maul/OnCreatureDeath, instance_dire_maul/SetData, instance_scarlet_monastery/Update, instance_uldaman/SetData, loch_modan/AreaTrigger_at_huldar_miran, Map.ScriptCommands/ScriptCommand_SetFaction, Player.Main/SummonPossessedMinion, quest_stormwind_rendezvous/UpdateAI, razorfen_kraul/QuestAccept_npc_willix_the_importer, redridge_mountains/QuestAccept_npc_corporal_keeshan, silverpine_forest/QuestAccept_npc_deathstalker_erland, spell_item/OnSummon#3, stonetalon_mountains/StartEvent, stranglethorn_vale/StartEvent, swamp_of_sorrows/QuestAccept_npc_galen_goodward, teldrassil/QuestAccept_npc_mist, thousand_needles/QuestAccept_npc_lakota_windsong, ungoro_crater/QuestAccept_npc_ame01 | — |
| ClearTemporaryFaction | method | Unit.Main/IsCharmed, Unit.Main/SetFactionTemplateId | boss_sapphiron/setHover, desolace/CaravanFaction, desolace/Dialogue, HomeMovementGenerator/Finalize, Map.ScriptCommands/ScriptCommand_SetFaction, quest_stormwind_rendezvous/UpdateAI, stranglethorn_vale/ResetCreature, Unit.Main/CombatStop | — |
| SendAreaSpiritHealerQueryOpcode | method | ByteBuffer/operator<<#10, Object/GetObjectGuid, ObjectGuid/operator<<, Player.Main/SendDirectMessage, Spell.Main/GetCastedTime, SpellCaster/GetCurrentSpell, WorldPacket/WorldPacket#4 | WorldSession.BattleGroundHandler/HandleAreaSpiritHealerQueryOpcode, WorldSession.NPCHandler/HandleGossipHelloOpcode | — |
| DisappearAndDie | method | Log.Main/Out, Object/IsCreature, Object/IsInWorld, Object/ToCreature, Pet.Main/Unsummon, Unit.Main/IsAlive, WorldObject.Object/DestroyForNearbyPlayers | arathi_highlands/FinishEvent, ashenvale/JustDied#2, ashenvale/WaypointReached#3, boss_gahzranka/CheckSpawnStatus, boss_golemagg/KillAdds, boss_jindo/DespawnAllSummons, boss_jindo/UpdateAI#2, boss_victor_nefarius/JustReachedHome, burning_steppes/UpdateEscortAI, darkshore/MovementInform, darkshore/UpdateFollowerAI#2, dreadsteed_ritual/EventEndedFail, dreadsteed_ritual/PhaseTwoEndedSuccess, duskwood/DespawnWatcher, eastern_plaguelands/FailEvent, feralas/UpdateFollowerAI, gnomeregan/JustDied#2, gnomeregan/UpdateFollowerAI, instance_maraudon/OnCreatureCreate, instance_maraudon/OnCreatureRespawn, instance_maraudon/SetData, instance_wailing_caverns/SetData, instance_zulfarrak/OnCreatureCreate, npc_j_eevee/UpdateAI, npc_j_eevee/UpdateAI#2, scourge_invasion/UpdateAI#7, ScriptedEscortAI/ResetEscort, ScriptedEscortAI/UpdateAI, ScriptedFollowerAI/UpdateAI, silithus/DoUnsummonArmy, ThreatListCopier.battleground_alterac/JustRespawned, ThreatListCopier.battleground_alterac/UpdateAI#17, ThreatListCopier.battleground_alterac/UpdateEscortAI, ThreatListCopier.battleground_alterac/UpdateEscortAI#2, WorldObject.Object/DespawnNearCreaturesByEntry, zulgurub_trash/UpdateAI#5 | — |
| GetHomePosition | method | — | durotar/UpdateAI, world_event_wareffort/CalculateRotatedPositionAboutLeader, world_event_wareffort/FollowSaurfang, world_event_wareffort/SetRespawnNearSaurfang | — |
| SetHomePosition | method | — | ashenvale/EnragedFoulwealdJustDied, ashenvale/EventStart, ashenvale/UpdateAI, blackrock_depths/Activate, blackrock_depths/SummonRingBoss, blackrock_depths/SummonRingMob, boss_celebras_the_cursed/WaypointReached, boss_chromaggus/UpdateAI, boss_gahzranka/CheckSpawnStatus, boss_halycon/JustDied, boss_mandokir/CheckVilebranchState, boss_mandokir/SpellHitTarget#2, boss_omen/OnFireworkLaunch, boss_ouro/UpdateAI, boss_razorgore/PhaseSwitch, burning_steppes/DemonDespawn, burning_steppes/JustDied, burning_steppes/Reset#2, burning_steppes/Transform, ChatHandler.CreatureCommands/HandleNpcMoveHelperCommand, darkshore/JustRespawned#5, deadmines/SummonedMovementInform, desolace/SetMagnetGuid, dreadsteed_ritual/SummonGuard, dreadsteed_ritual/SummonImp, dreadsteed_ritual/WaveSpawn, duskwood/JustSummoned, duskwood/WaypointReached, eastern_plaguelands/JustSummoned, eastern_plaguelands/UpdateAI, elemental_invasions/DoSpawn, instance_blackfathom_deeps/DoSpawnMobs, instance_blackrock_spire/JustDidDialogueStep, instance_dire_maul/npc_mizzle_the_craftyAI, instance_naxxramas.boss_kelthuzad/StartEncounter, instance_stratholme/SetData, instance_stratholme/SummonRamstein, instance_stratholme/Update, instance_temple_of_ahnqiraj/RestoreOuroSpawnTrigger, instance_zulfarrak/MoveNPCIfAlive, Map.ScriptCommands/ScriptCommand_SetHomePosition, silithus/DemonDespawn, silithus/JustDied#4, silithus/Reset#10, silithus/Transform, silithus/UpdateAI, stonetalon_mountains/JustSummoned, ThreatListCopier.battleground_alterac/JustRespawned, ThreatListCopier.battleground_alterac/UpdateAI#4, ThreatListCopier.battleground_alterac/UpdateEscortAI#3, ThreatListCopier.battleground_alterac/UpdateEscortAI#4, ThreatListCopier.battleground_alterac/WaypointReached, ThreatListCopier.battleground_alterac/WaypointReached#4, ThreatListCopier.battleground_alterac/WaypointReached#5, WaypointMovementGenerator/StartMove#2, winterspring/DemonDespawn, winterspring/JustDied, winterspring/Reset, winterspring/Transform, world_event_wareffort/MovementInform, world_event_wareffort/MoveToWaveBattlePosition, world_event_wareffort/MoveToWaveBattlePosition#2, world_event_wareffort/MoveToWaveBattlePosition#3, world_event_wareffort/SetRespawnNearSaurfang, world_event_wareffort/UpdateAI#2, world_event_wareffort/UpdateAI#3, zulfarrak/initBlyCrewMember, zulfarrak/MovementInform | — |
| ResetHomePosition | method | — | boss_mandokir/CheckVilebranchState, Map.ScriptCommands/ScriptCommand_SetHomePosition, the_barrens/UpdateEscortAI | — |
| RemoveAurasAtReset | method | ObjectGuid/IsPlayer, SpellAuraHolder/GetCasterGuid, SpellAuraHolder/GetSpellProto, SpellAuraHolder/IsPermanent, SpellEntry/IsAuraRemovedOnEvade, Unit.Main/RemoveAllNegativeAuras, Unit.Main/RemoveSpellAuraHolder, Unit.SpellAuras/IsPositive | CreatureAI/EnterEvadeMode, ScriptedAI/EnterEvadeMode, ScriptedEscortAI/EnterEvadeMode, ScriptedFollowerAI/EnterEvadeMode, world_event_wareffort/EnterEvadeMode#2 | — |
| OnLeaveCombat | method | CreatureAI/EnterEvadeMode, CreatureGroups/OnLeaveCombat, ZoneScript/OnCreatureEvade | boss_cthun/CheckIfAllDead, boss_patchwerk/CustomGetTarget, boss_thaddius/TransitionToPhase, PetEventAI/UpdateAI, Transport/TeleportTransport | — |
| OnEnterCombat | method | ByteBuffer/operator<<#9, CreatureAI/AttackedBy, CreatureAI/EnterCombat, CreatureGroups/OnMemberAttackStart, GuardMgr/SummonGuard, Object/GetObjectGuid, Object/ToPlayer, ObjectGuid/operator<<, Player.Main/GetReputationMgr, Player.Main/SendDirectMessage, Player.Main/SendFactionAtWar, Player.Main/SetTemporaryAtWarWithFaction, ReputationMgr/SetAtWar#2, Unit.Main/GetOwnerCreature, Unit.Main/GetOwnerPlayer, Unit.Main/HasUnitState, Unit.Main/IsMounted, Unit.Main/SetStandState, Unit.Main/Unmount, WorldObject.Object/GetFactionId, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldPacket/WorldPacket#4, ZoneScript/OnCreatureEnterCombat | — | — |
| ResetStats | method | Player.StatSystem/UpdateDamagePhysical, Unit.Main/RemoveAllAuras, Unit.Main/SetBaseWeaponDamage | boss_arlokk/Reset, boss_mandokir/Reset, boss_marli/Reset, boss_venoxis/Reset | — |
| GetDefaultDamageRange | method | — | boss_arlokk/UpdateAI, boss_marli/UpdateAI | — |
| GetDefaultArmor | method | — | boss_moam/Reset | — |
| GetNearestVictimInRange | method | ThreatManager/getThreatList, Unit.Main/CanHaveThreatList, Unit.Main/GetThreatManager, WorldObject.Object/GetDistance#3 | boss_twinemperors/UpdateAI | — |
| GetFarthestVictimInRange | method | ThreatManager/getThreatList, Unit.Main/CanHaveThreatList, Unit.Main/GetThreatManager, WorldObject.Object/GetDistance#3 | boss_mandokir/UpdateAI, zulgurub_trash/UpdateAI#5 | — |
| GetVictimInRange | method | ThreatManager/getThreatList, Unit.Main/CanHaveThreatList, Unit.Main/GetThreatManager, WorldObject.Object/IsInRange | ThreatListCopier.battleground_alterac/UpdateAI#16 | — |
| GetHostileCasterInRange | method | ThreatManager/getThreatList, Unit.Main/CanHaveThreatList, Unit.Main/GetThreatManager, Unit.Main/IsCaster, WorldObject.Object/IsInRange | — | — |
| GetHostileCaster | method | ThreatManager/getThreatList, Unit.Main/CanHaveThreatList, Unit.Main/GetThreatManager, Unit.Main/IsCaster | — | — |
| ProcessThreatList | method | ThreatListProcesser/Process, ThreatManager/getThreatList, Unit.Main/CanHaveThreatList, Unit.Main/GetThreatManager | ThreatListCopier.battleground_alterac/AggroLinkedMobsIfNeeded, ThreatListCopier.boss_ragnaros/SummonSonsOfFlame | — |
| CastSpellOnFarthestVictim | method | SpellCaster/CastSpell#2 | — | — |
| CastSpellOnNearestVictim | method | SpellCaster/CastSpell#2 | boss_jeklik/UpdateAI | — |
| CastSpellOnHostileCasterInRange | method | SpellCaster/CastSpell#2 | — | — |
| AddThreatsOf | method | ThreatManager/getThreatList, Unit.Main/AddThreat, Unit.Main/GetThreatManager#2, Unit.Main/IsAlive, Unit.Main/IsFriendlyTo, Unit.Main/SetInCombatWith | boss_jindo/JustSummoned, instance_ruins_of_ahnqiraj/OnCreatureEvade | — |
| SelectNearestHostileUnitInAggroRange | method | Cell/Cell#2, Cell/SetNoCreate, GridDefines/ComputeCellPair, NearestHostileUnitInAggroRangeCheck/NearestHostileUnitInAggroRangeCheck, Object/ToCreature#2, WorldObject.Object/GetMap, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY | boss_archaedas/EnterEvadeMode, boss_archaedas/EnterEvadeMode#2, ScriptedEscortAI/UpdateEscortAI, uldaman/EnterEvadeMode | — |
| FindNearestFriendlyGuard | method | NearestFriendlyGuardInRangeCheck/NearestFriendlyGuardInRangeCheck | — | — |
| CallNearestGuard | method | CreatureAI/AttackStart, WorldObject.Object/IsValidAttackTarget | GuardMgr/SummonGuard | — |
| TryToCast#2 | method | Log.Main/Out, Object/GetEntry, SpellCaster/IsNonMeleeSpellCasted, SpellMgr/GetSpellEntry, SpellMgr/Instance | boss_razorgore/UpdateAI, CreatureAI/DoCastSpellIfCan, Map.ScriptCommands/ScriptCommand_CastSpell | — |
| TryToCast | method | Creature.MotionMaster/GetCurrentMovementGeneratorType, MotionMaster/GetCurrent, MovementGenerator/IsReachable, Spell.Main/GetCastingObject, Spell.Main/prepare, Spell.Main/SetCastItem, Spell.Main/Spell#2, SpellCaster/InterruptNonMeleeSpells, SpellCaster/IsNonMeleeSpellCasted, SpellCastTargetsInfo/setDestination, SpellCastTargetsInfo/setSource, SpellCastTargetsInfo/setUnitTarget, SpellCastTargetsInfo/SpellCastTargets, SpellEntry/GetSpellSchoolMask, SpellEntry/IsAreaOfEffectSpell, SpellEntry/IsCharmSpell, SpellEntry/IsDismountSpell, SpellEntry/IsPositiveSpell#4, SpellEntry/IsTargetPowerTypeValid, ThreatManager/getThreatList, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/GetGuardianCountWithEntry, Unit.Main/GetMotionMaster, Unit.Main/GetPowerType, Unit.Main/GetThreatManager, Unit.Main/HasAura#2, Unit.Main/HasUnitState, Unit.Main/IsImmuneToDamage, Unit.Main/IsMounted, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/HasInArc | CreatureAI/DoSpellsListCasts | — |
| GetCombatTime | method | World/getConfig#4 | instance_naxxramas.Main/SetData | — |
| ResetCombatTime | method | — | — | — |
| EnterCombatWithTarget | method | CreatureAI/AttackStart, Unit.Main/AddThreat, Unit.Main/GetVictim, Unit.Main/SetInCombatWith | BasicAI/MoveInLineOfSight, CreatureLinkingMgr/DoCreatureLinkingEvent, GameObject/DoAggroWhenOpening, Map.ScriptCommands/ScriptCommand_AssistUnit, PetAI/DoAttack, ScriptedFollowerAI/MoveInLineOfSight, world_event_wareffort/MoveInLineOfSight | — |
| ApplyGameEventSpells | method | SpellCaster/CastSpell#2, SpellEntry/IsSpellAppliesAura, SpellMgr/GetSpellEntry, SpellMgr/Instance, Unit.Main/RemoveAurasDueToSpell | GameEventMgr.Main/operator() | — |
| FillGuidsListFromThreatList | method | HostileReference/getUnitGuid, ThreatManager/getThreatList, Unit.Main/CanHaveThreatList, Unit.Main/GetThreatManager | — | — |
| AddCreatureToRemoveListInMapsWorker | ctor | — | — | — |
| operator() | method | Map.Main/GetCreature, WorldObject.Object/AddObjectToRemoveList | — | — |
| AddToRemoveListInMaps | method | CreatureData/GetObjectGuid | ChatHandler.CreatureCommands/HandleNpcDeleteCommand, GameEventMgr.Main/GameEventUnspawn | — |
| SpawnCreatureInMapsWorker | ctor | — | — | — |
| operator()#4 | method | Map.Main/IsLoaded | — | — |
| SpawnInMaps | method | — | GameEventMgr.Main/GameEventSpawn | — |
| HasStaticDBSpawnData | method | — | ChatHandler.CreatureCommands/HandleNpcGroupAddCommand, ChatHandler.CreatureCommands/HandleNpcGroupAddRelCommand, ChatHandler.CreatureCommands/HandleNpcGroupDelCommand, ChatHandler.CreatureCommands/HandleNpcSpawnSetDeathStateCommand, ChatHandler.CreatureCommands/HandleWpAddCommand, CreatureGroups/DisbandGroup, Map.Main/AddToActive, Map.Main/RemoveFromActive | — |
| GetDBTableGUIDLow | method | Object/GetGUIDLow | boss_timmy_the_cruel/npc_crimson_guardsmanAI, ChatHandler.CreatureCommands/HandleNpcDeleteCommand, ChatHandler.CreatureCommands/HandleNpcSpawnSetAurasCommand, ChatHandler.CreatureCommands/HandleNpcSpawnSetDisplayIdCommand, ChatHandler.CreatureCommands/HandleNpcSpawnSetEmoteStateCommand, ChatHandler.CreatureCommands/HandleNpcSpawnSetEntryCommand, ChatHandler.CreatureCommands/HandleNpcSpawnSetMoveTypeCommand, ChatHandler.CreatureCommands/HandleNpcSpawnSetRespawnTimeCommand, ChatHandler.CreatureCommands/HandleNpcSpawnSetSheathStateCommand, ChatHandler.CreatureCommands/HandleNpcSpawnSetStandStateCommand, ChatHandler.CreatureCommands/HandleNpcSpawnWanderDistCommand, ChatHandler.DebugCommands/HandleMmapTestArea, Conditions/Evaluate, instance_naxxramas.Main/OnCreatureRespawn, instance_naxxramas.Main/UpdateAI#2, instance_sunken_temple/SetData | — |
| SetVirtualItem | method | Log.Main/Out, Object/GetGuidStr, ObjectMgr/GetItemPrototype, WorldObject.Object/SetByteValue, WorldObject.Object/SetUInt32Value | boss_mr_smite/PhaseEquipProcess, boss_warmaster_voone/SpellHitTarget, ChatHandler.CreatureCommands/HandleNpcAddWeaponCommand, instance_scarlet_monastery/Update, Map.ScriptCommands/ScriptCommand_SetEquipment, silithus/UpdateAI#7, westfall/WaypointReached | — |
| GetVirtualItemDisplayId | method | Object/GetUInt32Value | Player.StatSystem/GetWeaponBasedAuraModifier | — |
| GetVirtualItemClass | method | Object/GetByteValue | Player.StatSystem/GetWeaponBasedAuraModifier | — |
| GetVirtualItemSubclass | method | Object/GetByteValue | Player.StatSystem/GetWeaponBasedAuraModifier | — |
| GetVirtualItemInventoryType | method | Object/GetByteValue | Player.StatSystem/GetWeaponBasedAuraModifier | — |
| JoinCreatureGroup | method | Creature.MotionMaster/Initialize, CreatureGroups/AddMember, CreatureGroups/CreatureGroup, CreatureGroups/IsFormation, Log.Main/Out, Object/GetGuidStr, Object/GetObjectGuid, Unit.Main/GetMotionMaster | blackrock_depths/GOHello_go_thunderbrew_laguer_keg, desolace/AddToFormation, duskwood/AddToFormation, eastern_plaguelands/MovementInform, gnomeregan/JustSummoned, instance_blackrock_spire/DoSendNextStadiumWave, Map.ScriptCommands/ScriptCommand_JoinCreatureGroup, OutdoorPvPEP/SummonSquadAtEastWallTower, scourge_invasion/PallidHorrorAI, ThreatListCopier.battleground_alterac/UpdateEscortAI, ThreatListCopier.battleground_alterac/UpdateEscortAI#5 | — |
| LeaveCreatureGroup | method | CreatureGroups/DisbandGroup, CreatureGroups/GetLeaderGuid, CreatureGroups/GetOriginalLeaderGuid, CreatureGroups/RemoveMember, CreatureGroups/RemoveTemporaryLeader, Object/GetObjectGuid, ObjectGuid/operator== | ChatHandler.CreatureCommands/HandleNpcGroupDelCommand, Map.ScriptCommands/ScriptCommand_LeaveCreatureGroup | — |
| HasWeapon | method | — | Player.StatSystem/UpdateDamagePhysical, Spell.Effects/EffectWeaponDmg, Spell.Main/CheckItems | — |
| CanBeDisarmed | method | Unit.Main/CanUseEquippedWeapon | — | — |
| StartCooldownForSummoner | method | Object/GetUInt32Value, SpellCaster/AddCooldown, SpellEntry/HasAttribute, SpellMgr/GetSpellEntry, SpellMgr/Instance, Unit.Main/GetOwner | — | — |
| CancelSummonPossessedCharm | method | Object/GetUInt32Value, SpellEntry/HasEffect, SpellMgr/GetSpellEntry, SpellMgr/Instance, Unit.Main/GetCharmer, Unit.Main/HasUnitState, Unit.Main/RemoveAurasDueToSpell | — | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `creature`: guid int(10) unsigned PK, id mediumint(8) unsigned, id2 mediumint(8) unsigned, id3 mediumint(8) unsigned, id4 mediumint(8) unsigned, id5 mediumint(8) unsigned, map smallint(5) unsigned, position_x float, position_y float, position_z float, orientation float, spawntimesecsmin int(10) unsigned, spawntimesecsmax int(10) unsigned, wander_distance float, health_percent float, mana_percent float unsigned, movement_type tinyint(3) unsigned, spawn_flags int(10) unsigned, visibility_mod float?, patch_min tinyint(3) unsigned, patch_max tinyint(3) unsigned
- `creature_addon`: guid int(10) unsigned PK, patch tinyint(3) unsigned PK, display_id smallint(5) unsigned, mount_display_id smallint(6), equipment_id int(11), stand_state tinyint(3) unsigned, sheath_state tinyint(3) unsigned, emote_state smallint(5) unsigned, auras text?
- `creature_battleground`: guid int(10) unsigned PK, event1 tinyint(3) unsigned PK, event2 tinyint(3) unsigned
- `creature_movement`: id int(10) unsigned PK, point mediumint(8) unsigned PK, position_x float, position_y float, position_z float, orientation float, waittime int(10) unsigned, wander_distance float unsigned, script_id mediumint(8) unsigned, path_id int(10) unsigned
- `game_event_creature`: guid int(10) unsigned PK, event smallint(6) PK
- `game_event_creature_data`: guid int(10) unsigned PK, patch tinyint(3) unsigned PK, entry_id mediumint(8) unsigned, display_id mediumint(8) unsigned, equipment_id mediumint(8) unsigned, spell_start smallint(5) unsigned, spell_end smallint(5) unsigned, event smallint(5) unsigned PK
- `smartlog_creature`: time timestamp, type enum('Death','LongCombat','ScriptInfo',''), entry int(11), guid int(11), specifier varchar(255), combatTime int(11), content varchar(255)

*`?` = nullable, `PK` = primary key column.*


---

<!-- verify: failed-members | missing: AddCreatureToRemoveListInMapsWorker, AddThreatsOf, AddToRemoveListInMaps, ApplyGameEventSpells, CallNearestGuard, CastSpellOnFarthestVictim, CastSpellOnHostileCasterInRange, CastSpellOnNearestVictim, ClearTemporaryFaction, EnterCombatWithTarget, FillGuidsListFromThreatList, FindNearestFriendlyGuard, GetCombatTime, GetDefaultArmor, GetDefaultDamageRange, GetFarthestVictimInRange, GetHostileCaster, GetHostileCasterInRange, GetNameForLocaleIdx, GetNearestVictimInRange, GetVictimInRange, operator()#4, ProcessThreatList, RemoveAurasAtReset, ResetCombatTime, ResetHomePosition, ResetStats, SelectNearestHostileUnitInAggroRange, SendAreaSpiritHealerQueryOpcode, SetFactionTemporary, SetHomePosition, SpawnCreatureInMapsWorker, TryToCast, TryToCast#2 | invented: operator -->
