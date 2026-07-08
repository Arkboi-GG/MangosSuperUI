# stratholme

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# stratholme

**Purpose & Responsibilities**

The `stratholme` translation unit implements scripted behaviors for specific non-boss entities within the Stratholme instance. It handles:
1.  **Event Triggers:** Logic for starting the Baron Run event (`go_gauntlet_gate`) and the Postmaster quest chain (`go_stratholme_postbox`).
2.  **Quest-Specific Mobs:** AI for `mob_freed_soul` (ambient dialogue), `mob_restless_soul` (quest credit tracking and self-destruct), and `mobs_spectral_ghostly_citizen` (combat, haunting phantoms, and emote interactions).
3.  **Environmental Mechanics:** The `mobs_cristal_zuggurat` AI monitors nearby acolytes to determine when crystals should self-destruct, triggering zone-wide announcements.
4.  **Ambient Creatures:** `mobs_rat_pestifere` provides complex, pre-scripted pathing for rats during specific events, and `go_supply_crate` spawns random plagued pests upon interaction.
5.  **Spell Scripts:** Custom logic for the "Haunting Phantoms" aura (periodic phantom spawning) and the "Eye of Naxxramas" gargoyle summoning spell (auto-attacking the caster's target).

This unit does not contain database queries or touch any SQL tables. All state management relies on the `ScriptedInstance` interface provided by the core engine.

## Member-by-Member Behavior

### Event Triggers

**GOHello_go_gauntlet_gate**
This function acts as the trigger for the first gate of the Gauntlet event. It retrieves the instance data via `WorldObject.Object/GetInstanceData`. If the instance exists and the `TYPE_BARON_RUN` encounter state is not already started, it sets the state to `IN_PROGRESS` using `InstanceData/SetData`. It returns `false` to prevent the default game object interaction behavior.

**GOOpen_go_stratholme_postbox**
Handles interaction with the postboxes in Stratholme. It checks the `TYPE_POSTMASTER` state via `InstanceData/GetData`.
- If the state is `SPECIAL`, it casts `SPELL_SUMMON_POSTMASTER` on the player and sets the state to `DONE`.
- Otherwise, it sets the state to `IN_PROGRESS`.
- Regardless of the previous state, it summons three `NPC_UNDEAD_POSTMAN` creatures near the player. It calculates random spawn points within 6.0 units of the player using `WorldObject.Object/GetRandomPoint` and `WorldObject.Object/SummonCreature#2`.

### Ambient & Quest Mobs

**mob_freed_soulAI**
A simple AI for freed souls.
- **ctor**: Initializes the parent `ScriptedAI`.
- **Reset#2**: Upon reset or spawn, it selects a random text ID from a predefined list (6451–6455) and plays it using `ScriptMgr/DoScriptText`.
- **GetAI_mob_freed_soul**: Factory function returning a new instance of this AI.

**mob_restless_soulAI**
Implements logic for the "Restless Soul" quest (Quest 5282).
- **ctor**: Initializes `ScriptedAI` and calls `Reset`.
- **Reset#3**: Resets internal timers (`Die_Timer` to 5000ms) and flags (`Tagged` to false).
- **SpellHit**: Checks if the creature is hit by `SPELL_EGAN_BLASTER` (17368) from a player who has the quest incomplete. If so, it marks the creature as `Tagged` and stores the player's GUID in `Tagger`.
- **JustSummoned**: When a soul is freed (summoned), it casts `SPELL_SOUL_FREED` on itself. If the original `Tagger` player is still on the map, the summoned soul follows them using `Creature.MotionMaster/MoveFollow`.
- **JustDied**: If the restless soul dies while `Tagged`, it summons a `mob_freed_soul` (Entry 11136) at its location.
- **UpdateAI#2**: If `Tagged`, it counts down `Die_Timer`. When the timer expires, it grants quest credit to the `Tagger` player via `Player.Main/KilledMonsterCredit` and instantly kills itself using `Unit.Main/DealDamage`.
- **GetAI_mob_restless_soul**: Factory function.

**mobs_spectral_ghostly_citizenAI**
Handles the spectral citizens found in Stratholme.
- **ctor**: Initializes `ScriptedAI`, calls `Reset`, and initializes `hasEvadedOnce`.
- **Reset#5**: Sets `Die_Timer` to 5000ms, `cast_Haunting` to 20000ms, and `Tagged` to false.
- **SpellHit#2**: Marks the creature as `Tagged` if hit by `SPELL_EGAN_BLASTER`.
- **JustDied#3**: If `Tagged` upon death, it attempts to spawn up to four `mob_restless_soul` creatures nearby. Each subsequent spawn has a decreasing probability (100%, 50%, 33%, 25%) determined by `shared_Util/urand`.
- **UpdateAI#4**:
    - If `Tagged`, it counts down `Die_Timer` and self-destructs when expired.
    - In combat, it casts `SPELL_HAUNTING_PHANTOM` on its victim every 20 seconds.
    - Performs standard melee attacks.
- **ReceiveEmote#2**: Handles player emotes:
    - **Dance**: If in combat and not yet evaded, it enters evade mode (despawns/flees). Otherwise, it dances.
    - **Rude**: Slaps the player if in melee range, otherwise performs a rude gesture.
    - **Wave/Bow/Kiss**: Performs corresponding emotes.
- **GetAI_mobs_spectral_ghostly_citizen**: Factory function.

### Environmental Mechanics

**mobs_cristal_zugguratAI**
Manages the Zuggurat crystals.
- **ctor**: Retrieves instance data, sets update timer, and calls `Reset`.
- **Reset#4**: No-op.
- **JustDied#2**: Summons a temporary creature (Entry 10399) to yell `SAY_CRYSTAL_DESTROYED`. It updates the instance data with `TYPE_CRISTAL_DIE`. If all crystals are dead (`TYPE_CRISTAL_ALL_DIE` is `DONE`), it triggers a second yell (`SAY_ALL_CRYSTALS_DESTROYED`).
- **UpdateAI#3**: Every 2 seconds, it scans for nearby creatures with Entry 10399 (acolytes) within 50 yards. If any acolyte is alive, it returns early. If no acolytes are found (or all are dead), it instantly kills the crystal using `Unit.Main/DealDamage`.
- **GetAI_mobs_cristal_zuggurat**: Factory function.

### Ambient Pathing

**AI_mobs_rat_pestifere**
Implements complex, pre-scripted movement paths for rats.
- **ctor**: Randomly assigns a display ID (1418) if the random roll is 2. Calls `Reset`.
- **Reset**: Resets movement state variables.
- **ReceiveEmote**: Interprets emote IDs >= 1000 as movement commands. It subtracts 999 to get a rat ID (1–10) and teleports the rat to a specific starting coordinate using `Map.Main/CreatureRelocation`.
- **Deplacement**: A helper method that calculates movement time based on distance and speed, then initiates movement using `Unit.Main/MonsterMove`. It updates internal destination coordinates.
- **UpdateAI**: Executes the movement sequence based on `m_idRat` and `m_mvt_id`. Each rat ID corresponds to a specific sequence of waypoints defined in the switch statements. When the sequence completes, `m_idRat` is set to 0, stopping movement.
- **GetAI_mobs_rat_pestifere**: Factory function. Note: This script is commented out in `AddSC_stratholme`, meaning it is not registered by default.

### Game Objects & Spells

**go_supply_crateAI**
- **ctor**: Inherits from `GameObjectAI`.
- **OnUse**: When interacted with, it randomly chooses between Plagued Rats, Insects, or Maggots. It spawns 1–4 of the chosen creature near the user. Finally, it sets the game object's loot state to `GO_JUST_DEACTIVATED`.
- **GetAIgo_supply_crate**: Factory function.

**HauntingPhantomsScript**
- **OnBeforeApply**: Sets the periodic timer for the aura to 5 seconds.
- **OnPeriodicDummy**: Every tick, there is a 5% chance to spawn either a "Spiteful Phantom" (Spell 16334) or a "Wrath Phantom" (Spell 16335) on the target.
- **GetScript_HauntingPhantoms**: Factory function.

**EyeOfNaxxramasSummonRockwingGargoylesScript**
- **OnSummon**: When a gargoyle is summoned, it identifies the caster's current attacker/helper target and commands the gargoyle to attack that target immediately.
- **GetScript_EyeOfNaxxramasSummonRockwingGargoyles**: Factory function.

**AddSC_stratholme**
Registers all the above scripts with the `ScriptMgr`. Note that `mobs_rat_pestifere` is commented out in this registration block.

## Cross-Unit Boundaries

- **InstanceData**: `GOHello_go_gauntlet_gate`, `GOOpen_go_stratholme_postbox`, and `mobs_cristal_zugguratAI` rely heavily on `InstanceData/GetData` and `InstanceData/SetData` to synchronize event states (Baron Run, Postmaster, Crystal Deaths) with the rest of the instance logic.
- **WorldObject/Object**: Used extensively for spatial calculations (`GetPositionX/Y/Z`, `GetRandomPoint`) and entity manipulation (`SummonCreature`, `GetInstanceData`).
- **Unit/Main**: Used for combat actions (`DealDamage`, `GetHealth`, `SelectHostileTarget`, `AttackStart`) and quest logic (`KilledMonsterCredit`).
- **ScriptedAI**: Base class for all creature AIs, providing common functionality like `DoScriptText` and `EnterEvadeMode`.
- **Map/Main**: Used for retrieving entities (`GetUnit`, `GetPlayer`, `GetCreature`) and relocating creatures (`CreatureRelocation`).
- **SpellCaster**: Used to cast spells (`CastSpell`) from creatures or players.
- **Aura/SpellScript**: Custom spell behaviors hook into the core spell system via `AuraScript` and `SpellScript` interfaces.

## Data Model

This unit does not access any database tables. All state is managed in-memory via the `ScriptedInstance` interface and local member variables.

## Notable Implementation Details

- **Rat Pathing Complexity**: The `AI_mobs_rat_pestifere` uses hardcoded coordinates and a state machine (`m_mvt_id`) to simulate complex pathing. This is brittle and relies on specific emote triggers to initiate. The script is currently disabled in `AddSC_stratholme`.
- **Crystal Death Logic**: `mobs_cristal_zugguratAI` does not die from damage. Instead, it self-destructs when its associated acolytes (Entry 10399) are all dead. This requires periodic scanning of the grid.
- **Quest Credit Timing**: `mob_restless_soulAI` grants quest credit only after a 5-second delay following being "tagged" by the correct spell. This prevents accidental credit if the player switches targets quickly.
- **Emote Interactions**: `mobs_spectral_ghostly_citizenAI` has unique behavior for dance emotes, allowing players to force the mob to evade combat once per encounter.
- **Disabled Script**: The `mobs_rat_pestifere` script is fully implemented but commented out in the registration function `AddSC_stratholme`. Maintainers should be aware that enabling it requires uncommenting the relevant block.

## Member Reference

**GOHello_go_gauntlet_gate**: Function that triggers the Baron Run event by setting `TYPE_BARON_RUN` to `IN_PROGRESS` in the instance data.

**GOOpen_go_stratholme_postbox**: Function that handles postbox interaction, summoning postmen and potentially the Postmaster NPC based on instance state.

**mob_freed_soulAI**: Constructor for the freed soul AI, initializing the parent `ScriptedAI`.

**Reset#2**: Method in `mob_freed_soulAI` that plays a random ambient dialogue line.

**GetAI_mob_freed_soul**: Factory function returning a new `mob_freed_soulAI` instance.

**mob_restless_soulAI**: Constructor for the restless soul AI, initializing the parent `ScriptedAI`.

**Reset#3**: Method in `mob_restless_soulAI` that resets internal timers and tags.

**SpellHit**: Method in `mob_restless_soulAI` that tags the creature if hit by `SPELL_EGAN_BLASTER` from a player with the active quest.

**JustSummoned**: Method in `mob_restless_soulAI` that casts a freeing spell and makes the summoned soul follow the tagging player.

**JustDied**: Method in `mob_restless_soulAI` that summons a freed soul if the restless soul was tagged.

**UpdateAI#2**: Method in `mob_restless_soulAI` that handles the self-destruct timer and grants quest credit upon expiration.

**GetAI_mob_restless_soul**: Factory function returning a new `mob_restless_soulAI` instance.

**mobs_spectral_ghostly_citizenAI**: Constructor for the spectral citizen AI, initializing the parent `ScriptedAI`.

**Reset#5**: Method in `mobs_spectral_ghostly_citizenAI` that resets combat timers and tags.

**SpellHit#2**: Method in `mobs_spectral_ghostly_citizenAI` that tags the creature if hit by `SPELL_EGAN_BLASTER`.

**JustDied#3**: Method in `mobs_spectral_ghostly_citizenAI` that spawns restless souls with decreasing probability if the citizen was tagged.

**UpdateAI#4**: Method in `mobs_spectral_ghostly_citizenAI` that handles self-destruct, casting haunting phantoms, and melee attacks.

**ReceiveEmote#2**: Method in `mobs_spectral_ghostly_citizenAI` that handles player emotes, including forcing evasion on dance.

**GetAI_mobs_spectral_ghostly_citizen**: Factory function returning a new `mobs_spectral_ghostly_citizenAI` instance.

**mobs_cristal_zugguratAI**: Constructor for the crystal AI, initializing instance data and timers.

**Reset#4**: Method in `mobs_cristal_zugguratAI` that is currently a no-op.

**JustDied#2**: Method in `mobs_cristal_zugguratAI` that summons a yelling creature and updates instance data regarding crystal deaths.

**UpdateAI#3**: Method in `mobs_cristal_zugguratAI` that checks for living acolytes and self-destructs the crystal if none are found.

**GetAI_mobs_cristal_zuggurat**: Factory function returning a new `mobs_cristal_zugguratAI` instance.

**AI_mobs_rat_pestifere**: Constructor for the rat AI, assigning a random display ID and resetting state.

**Reset**: Method in `AI_mobs_rat_pestifere` that resets movement variables.

**ReceiveEmote**: Method in `AI_mobs_rat_pestifere` that interprets emotes as movement commands and teleports the rat to a start position.

**Deplacement**: Helper method in `AI_mobs_rat_pestifere` that calculates movement time and initiates movement to a new coordinate.

**UpdateAI**: Method in `AI_mobs_rat_pestifere` that executes pre-scripted waypoint sequences based on the rat's ID.

**GetAI_mobs_rat_pestifere**: Factory function returning a new `AI_mobs_rat_pestifere` instance.

**go_supply_crateAI**: Constructor for the supply crate AI, inheriting from `GameObjectAI`.

**OnUse**: Method in `go_supply_crateAI` that spawns random plagued pests and deactivates the crate.

**GetAIgo_supply_crate**: Factory function returning a new `go_supply_crateAI` instance.

**OnBeforeApply**: Method in `HauntingPhantomsScript` that sets the aura's periodic timer.

**OnPeriodicDummy**: Method in `HauntingPhantomsScript` that periodically spawns phantoms with a 5% chance.

**GetScript_HauntingPhantoms**: Factory function returning a new `HauntingPhantomsScript` instance.

**OnSummon**: Method in `EyeOfNaxxramasSummonRockwingGargoylesScript` that makes summoned gargoyles attack the caster's target.

**GetScript_EyeOfNaxxramasSummonRockwingGargoyles**: Factory function returning a new `EyeOfNaxxramasSummonRockwingGargoylesScript` instance.

**AddSC_stratholme**: Function that registers all Stratholme-related scripts with the script manager.

---

<!-- machine-true, projected from graph.json -->

## Map — stratholme

*Source:* stratholme.cpp, stratholme.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GOHello_go_gauntlet_gate | function | InstanceData/GetData, InstanceData/SetData, WorldObject.Object/GetInstanceData | — | — |
| GOOpen_go_stratholme_postbox | function | InstanceData/GetData, InstanceData/SetData, SpellCaster/CastSpell#2, WorldObject.Object/GetInstanceData, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/GetRandomPoint, WorldObject.Object/SummonCreature#2 | — | — |
| mob_freed_soulAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#2 | method | ScriptMgr/DoScriptText | — | — |
| GetAI_mob_freed_soul | function | — | — | — |
| mob_restless_soulAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#3 | method | — | — | — |
| SpellHit | method | Object/GetGUID, Object/GetTypeId, Player.Main/GetQuestStatus | — | — |
| JustSummoned | method | Creature.MotionMaster/MoveFollow, Map.Main/GetUnit, ObjectGuid/ObjectGuid#5, SpellCaster/CastSpell#2, Unit.Main/GetMotionMaster, WorldObject.Object/GetMap | — | — |
| JustDied | method | WorldObject.Object/SummonCreature#2 | — | — |
| UpdateAI#2 | method | Map.Main/GetPlayer, Object/GetEntry, Object/GetGUID, ObjectGuid/ObjectGuid#5, Player.Main/KilledMonsterCredit, Unit.Main/DealDamage, Unit.Main/GetHealth, WorldObject.Object/GetMap | — | — |
| GetAI_mob_restless_soul | function | — | — | — |
| mobs_spectral_ghostly_citizenAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#5 | method | — | — | — |
| SpellHit#2 | method | — | — | — |
| JustDied#3 | method | shared_Util/urand, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/GetRandomPoint, WorldObject.Object/SummonCreature#2 | — | — |
| UpdateAI#4 | method | CreatureAI/DoMeleeAttackIfReady, SpellCaster/CastSpell#2, Unit.Main/DealDamage, Unit.Main/GetHealth, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| ReceiveEmote#2 | method | ScriptedAI/EnterEvadeMode, SpellCaster/CastSpell#2, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/HandleEmoteCommand, Unit.Main/IsInCombat | — | — |
| GetAI_mobs_spectral_ghostly_citizen | function | — | — | — |
| mobs_cristal_zugguratAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset#4 | method | — | — | — |
| JustDied#2 | method | InstanceData/GetData, InstanceData/SetData, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/MonsterYellToZone, WorldObject.Object/SummonCreature#2 | — | — |
| UpdateAI#3 | method | GridSearchers/GetCreatureListWithEntryInGrid#2, Map.Main/GetCreature, Object/GetObjectGuid, Unit.Main/DealDamage, Unit.Main/GetHealth, Unit.Main/IsAlive | — | — |
| GetAI_mobs_cristal_zuggurat | function | — | — | — |
| AI_mobs_rat_pestifere | ctor | ScriptedAI/ScriptedAI, shared_Util/urand, Unit.Main/SetDisplayId | — | — |
| Reset | method | — | — | — |
| ReceiveEmote | method | Map.Main/CreatureRelocation, WorldObject.Object/GetMap | — | — |
| Deplacement | method | Map.Main/CreatureRelocation, Unit.Main/GetSpeed, Unit.Main/MonsterMove, Unit.Main/SetWalk, WorldObject.Object/GetMap, WorldObject.Object/IsWalking | — | — |
| UpdateAI | method | — | — | — |
| GetAI_mobs_rat_pestifere | function | — | — | — |
| go_supply_crateAI | ctor | GameObjectAI/GameObjectAI | — | — |
| OnUse | method | GameObject/SetLootState, shared_Util/urand, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SummonCreature#2 | — | — |
| GetAIgo_supply_crate | function | — | — | — |
| OnBeforeApply | method | Aura/GetEffIndex, Aura/SetPeriodicTimer | — | — |
| OnPeriodicDummy | method | Aura/GetTarget, shared_Util/roll_chance_i, shared_Util/urand, SpellCaster/CastSpell#2 | — | — |
| GetScript_HauntingPhantoms | function | — | — | — |
| OnSummon | method | Creature.Main/AI, CreatureAI/AttackStart, Unit.Main/GetAttackerForHelper | — | — |
| GetScript_EyeOfNaxxramasSummonRockwingGargoyles | function | — | — | — |
| AddSC_stratholme | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
