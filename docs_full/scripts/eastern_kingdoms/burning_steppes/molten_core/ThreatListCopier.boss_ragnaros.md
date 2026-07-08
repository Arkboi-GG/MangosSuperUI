<!-- provenance: boundary-bleed -->
# ThreatListCopier.boss_ragnaros

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_ragnaros

**Purpose & Responsibilities**
This unit implements the artificial intelligence and encounter logic for **Ragnaros**, the final boss of the Molten Core raid instance. It manages a complex, multi-phase fight involving specific mechanics:
1.  **Phase 1 (Active Combat):** Ragnaros engages players with melee attacks, ranged "Magma Blast" spells when no melee targets are present, and periodic area-of-effect abilities like "Wrath of Ragnaros" (knockback/threat reset) and "Might of Ragnaros" (targeting mana users). He also spawns periodic "Lava Burst" game objects.
2.  **Phase 2 (Submerged/Banished):** After a set duration, Ragnaros submerges into the lava, becoming immune to player damage. During this phase, he summons "Sons of Flame" adds that inherit his threat list. He remains submerged until all adds are defeated or a timer expires, at which point he emerges to resume Phase 1.
3.  **Encounter Entry:** The script handles the transition from the previous boss (Majordomo Executus), waiting for Majordomo's death before initiating Ragnaros's entrance sequence.

The unit relies heavily on custom threat manipulation to ensure adds focus correctly and to force Ragnaros to switch targets during specific mechanics. It uses a helper class, `ThreatListCopier`, to replicate Ragnaros's threat state onto summoned minions.

## Member-by-Member Behavior

### Encounter Initialization and State Management

**`boss_ragnarosAI` (ctor)**
Initializes the AI object. It retrieves the instance data (`ScriptedInstance`) to track encounter progress, disables standard combat movement (likely to prevent pathing issues while submerged or during specific animations), and immediately calls `Reset()` to initialize timers and flags.

**`Reset`**
Resets all internal timers and boolean flags to their default states.
- Sets initial delays for abilities like Magma Blast (2s), Wrath of Ragnaros (25–30s), Might of Ragnaros (10–15s), and Lava Burst (10–15s).
- Sets the Phase 1 duration timer (`m_uiSubmergeTimer`) to 3 minutes.
- Sets the Phase 2 emergence delay (`m_uiAttackTimer`) to 1.5 minutes.
- Resets flags such as `HasYelledAggro`, `HasSubmergedOnce`, `IsBanished`, and `Explosion`.
- If the instance data is valid and Ragnaros is alive, it sets the encounter status to `NOT_STARTED`.

**`SpellHitTarget`**
Monitors spells hitting targets. Specifically checks if `SPELL_ELEMENTAL_FIRE_KILL` hits Majordomo Executus. If so, it starts a 7-second timer (`m_uiEnterCombatTimer`) to begin the entrance sequence. This decouples Ragnaros's activation from the immediate death event, allowing for cinematic timing.

### Phase Transitions and Mechanics

**`SummonSonsOfFlame`**
Summons up to 8 "Sons of Flame" (`NPC_SON_OF_FLAME`) at predefined positions (`PositionOfAdds`).
- For each son, it creates a `ThreatListCopier` object.
- Summons the creature with a timed despawn (10s out of combat).
- Iterates through Ragnaros's current threat list using the copier. The copier's `Process` method calls `AttackStart` on the son for each unit on Ragnaros's threat list, effectively copying the aggro table.
- Selects a random target from Ragnaros's threat list, gives the son 90% threat toward that target, initiates an attack, and moves the son to chase the target.
- Deletes the `ThreatListCopier` object after use.

**`UpdateLavaBurstAI`**
Manages the spawning of Lava Bursts in waves of three.
- Uses three timers: primary (`m_uiLavaBurstTimer`), secondary (`m_uiLavaBurstSecondaryTimer`), and tertiary (`m_uiLavaBurstTertiaryTimer`).
- When the primary timer expires, it spawns a burst and resets the primary timer (15–20s) and starts the secondary timer (2–4s).
- When the secondary timer expires, it spawns another burst and starts the tertiary timer (2–4s).
- When the tertiary timer expires, it spawns the final burst of the wave.
- This creates a staggered effect rather than spawning all three simultaneously.

**`DoLavaBurst`**
Spawns a single Lava Burst game object (`GO_LAVA_BURST`) at a random location from the `PositionOfLavaBursts` array.
- Calculates a random rotation (`frand(0, M_PI_F)`).
- Calls `Use` on the game object, triggering its spell effects.

**`CheckForMelee`**
Determines if Ragnaros should perform melee attacks and selects the appropriate target. It follows a strict priority order:
1.  **Current Victim:** Checks if the current target is a player (not GM), in line-of-sight, and within melee range. If so, performs the melee attack and returns.
2.  **Top Aggro Player:** If the current victim is not reachable, searches for the highest-threat player in melee range and LOS. If found, switches target (modifying threat percentages to ensure the switch sticks), performs the attack, and returns.
3.  **Top Aggro Pet:** If no players are in melee range, searches for the highest-threat pet in melee range and LOS. Switches target, attacks, and returns.
4.  **Other Units:** As a fallback, searches for any non-player unit in melee range and LOS. Switches target, attacks, and logs the event (logging is commented out in the source).
- If no targets are found in melee range, sets `m_bInMelee` to `false`.

### Main AI Loop

**`UpdateAI`**
The core update loop, executed every tick. It handles timers, phase transitions, and ability casting.

1.  **Entrance Sequence (`m_uiEnterCombatTimer`):**
    - If active, waits for the timer to expire.
    - First expiration (7s): Plays roar emote, yells arrival line, marks as in combat, and sets a 3-second follow-up timer.
    - Second expiration (10s total): Removes immunity flags, despawns Majordomo's corpse, and clears the entrance timer.

2.  **Immunity Check:** Returns early if Ragnaros still has the `UNIT_FLAG_IMMUNE_TO_PLAYER` flag (during pre-combat or specific phases).

3.  **Emergence Animation (`m_uiEmergeStateTimer`):**
    - If active, waits for the animation to complete before proceeding.

4.  **Phase 2 (Banished/Submerged):**
    - If `IsBanished` is true:
        - Waits for `m_uiAttackTimer` to expire.
        - Checks if all "Sons of Flame" are dead or isolated. If all are gone, forces the timer to 0 to speed up emergence.
        - Maintains submerged visual auras.
        - Calls `UpdateLavaBurstAI` to continue spawning lava bursts.
        - When the timer expires, removes submerged auras, stands up, casts the emerge visual spell, sets `IsBanished` to false, resets relevant timers, and returns to Phase 1 logic.

5.  **Target Validation:** Returns early if no hostile target is selected.

6.  **Phase 1 Logic:**
    - Calls `UpdateLavaBurstAI` to manage lava burst spawning.
    - **Target Restoration (`m_uiRestoreTargetTimer`):** After casting "Might of Ragnaros," Ragnaros faces the target. This timer ensures he re-faces his original victim after a short delay.
    - **Phase Transition (`m_uiSubmergeTimer`):** If the timer expires and Ragnaros is not banished, he casts submerged visual/effect spells, plays a reinforcement line, summons Sons of Flame, sets `IsBanished` to true, and resets timers for the next cycle.
    - **Wrath of Ragnaros (`m_uiWrathOfRagnarosTimer`):** Casts a knockback spell, resets all threat (clearing aggro table), and plays a line.
    - **Might of Ragnaros (`m_uiMightOfRagnarosTimer`):**
        - Builds a list of alive, mana-using players who are not Game Masters.
        - If the list is not empty, picks a random target.
        - Casts "Might of Ragnaros" on the target, faces them, sets a restore-target timer, and possibly plays a line.
    - **Melee Check:** Calls `CheckForMelee()` to handle physical attacks.
    - **Magma Blast (`m_uiMagmaBlastTimer`):**
        - Only active if `m_bInMelee` is false (no one in melee range).
        - If the timer expires, plays a line (if not already played recently), selects a random player or pet target, and casts "Magma Blast."
        - Resets the timer to 2.5s if successful.

### Helper Classes and Registration

**`ThreatListCopier` (ctor)**
Initializes the copier with a destination unit (the Son of Flame).

**`Process`**
Implements the `ThreatListProcesser` interface. For each unit on the source threat list, it calls `AttackStart` on the destination unit, forcing the add to aggro that player. Returns `false` to continue iterating.

**`GetAI_boss_ragnaros`**
Factory function that returns a new `boss_ragnarosAI` instance for a given creature.

**`AddSC_boss_ragnaros`**
Registers the script with the engine. Creates a `Script` object, sets its name to "boss_ragnaros", assigns the `GetAI_boss_ragnaros` factory function, and registers it with the `ScriptMgr`. Called by `ScriptLoader/AddScripts`.

## Cross-Unit Boundaries

- **`InstanceData` (`m_pInstance`):** Used in `Reset`, `Aggro`, and `JustDied` to update the encounter state (`TYPE_RAGNAROS`) to `NOT_STARTED`, `IN_PROGRESS`, or `DONE`. This allows other scripts (like doors or other bosses) to react to Ragnaros's status.
- **`CreatureAI` / `ScriptedAI`:** Inherits from `ScriptedAI`. Uses methods like `DoCastSpellIfCan`, `DoScriptText`, `DoResetThreat`, and `SetCombatMovement`.
- **`Unit.Main`:** Extensively used for state management: `IsAlive`, `GetVictim`, `SetTargetGuid`, `SetFacingToObject`, `SetInFront`, `SetStandState`, `AddAura`, `RemoveAurasDueToSpell`, `HasAura`, `GetPowerType`, `GetThreatManager`, `SelectHostileTarget`, `SelectAttackingTarget`, `AttackerStateUpdate`, `ResetAttackTimer`, `IsAttackReady`, `CanReachWithMeleeAutoAttack`, `IsWithinLOSInMap`, `HandleEmote`, `SetInCombatWithZone`.
- **`Creature.Main`:** Used for `SummonCreature`, `FindNearestCreature`, `ForcedDespawn`, `ProcessThreatList`, `GetMotionMaster`.
- **`ThreatManager`:** Accessed via `GetThreatManager()` to call `modifyThreatPercent` (for adding/removing threat) and `getThreatList` (for iterating over aggroed players).
- **`WorldObject.Object`:** Used for `GetInstanceData`, `SummonGameObject`, `GetObjectGuid`, `GetEntry`, `GetTypeId`, `RemoveFlag`, `HasFlag`, `ToPlayer`.
- **`GameObject`:** Used in `DoLavaBurst` to `Use` the spawned lava burst object.
- **`ScriptMgr`:** Used in `KilledUnit` to play text lines (`DoScriptText`) and in `AddSC_boss_ragnaros` to register the script (`RegisterSelf`).
- **`shared_Util`:** Uses `urand` for random number generation and `frand` for floating-point randomness.
- **`GridSearchers`:** Uses `GetCreatureListWithEntryInGrid` in `UpdateAI` to find all Sons of Flame nearby during the submerged phase.
- **`Log.Main`:** Uses `Out` to log errors if spells fail to cast or if no target is found for Magma Blast.
- **`Player.Main`:** Uses `IsGameMaster` to exclude GMs from certain mechanics (Might of Ragnaros, melee targeting).
- **`SpellCaster`:** Uses `IsNonMeleeSpellCasted` in `CheckForMelee` to ensure melee attacks don't interrupt spell casting.

## Data Model

This unit does not directly query or modify database tables. It interacts with the `ScriptedInstance` system, which likely reads/writes to a save system or memory-based instance data structure, but no SQL queries or table references are present in the code.

## Notable Implementation Details

- **Threat Copying Mechanism:** The `ThreatListCopier` class is a custom solution to replicate Ragnaros's threat list onto his Sons of Flame. This ensures that adds focus the same players Ragnaros is fighting, preventing them from randomly aggroing low-threat targets. The copier iterates through the threat list and calls `AttackStart` on each unit, which is a heavy operation but necessary for accurate threat replication.
- **Phase 2 Emergence Logic:** Ragnaros remains submerged until either all Sons of Flame are dead/isolated OR the `m_uiAttackTimer` (1.5 minutes) expires. The code checks for alive Sons of Flame every tick during the submerged phase. If all are gone, it forces the timer to 0, allowing immediate emergence. This prevents the boss from being stuck submerged if the raid wipes the adds quickly.
- **Melee Target Priority:** `CheckForMelee` has a complex fallback chain. It prioritizes the current victim, then top-agro players, then pets, then other units. This ensures Ragnaros always tries to hit a player if possible, but won't stand idle if only pets or NPCs are in range. The threat modification (`modifyThreatPercent`) is used aggressively to force target switches, ensuring the AI doesn't get stuck on an unreachable target.
- **Might of Ragnaros Targeting:** This ability specifically targets mana users. The code builds a vector of eligible players (alive, mana power type, not GM) and picks one randomly. This is a classic mechanic to pressure healers/mages.
- **Lava Burst Wave Spawning:** Instead of spawning all three lava bursts at once, `UpdateLavaBurstAI` staggers them with 2–4 second delays between each. This creates a more dynamic visual and mechanical challenge, requiring players to move continuously rather than just avoiding a single cluster.
- **Entrance Timing:** The script waits for Majordomo's death via `SpellHitTarget` and then uses a 7-second timer before starting the entrance sequence. This allows for a cinematic pause where Majordomo dies, and Ragnaros slowly rises, enhancing the dramatic effect.
- **Hardcoded Positions:** Lava Burst locations and Son of Flame spawn points are hardcoded in arrays (`PositionOfLavaBursts`, `PositionOfAdds`). This limits flexibility but ensures consistent placement relative to the boss arena.
- **Error Logging:** The script logs errors if key spells fail to cast (Submerge, Emerge, Magma Blast). This aids in debugging issues related to spell IDs or casting conditions.

## Member Reference

**ThreatListCopier** (ctor)
Initializes the `ThreatListCopier` helper object with a pointer to the destination `Unit` (typically a Son of Flame) whose threat list is to be populated.

**Process**
Method of `ThreatListCopier`. Called by the threat list iteration mechanism. It invokes `AttackStart` on the destination unit for the current unit in the list, thereby copying the aggro state. Returns `false` to allow iteration to continue.

**boss_ragnarosAI** (ctor)
Constructs the AI instance for Ragnaros. It fetches the `ScriptedInstance` data, disables combat movement, and calls `Reset()` to initialize all timers and state flags.

**Reset**
Resets all internal timers (e.g., `m_uiSubmergeTimer`, `m_uiWrathOfRagnarosTimer`) and boolean flags (e.g., `IsBanished`, `HasYelledAggro`) to their default values. Updates the instance data to `NOT_STARTED` if Ragnaros is alive.

**Aggro**
Triggered when Ragnaros enters combat. It ignores aggro from Majordomo Executus, updates the instance state to `IN_PROGRESS`, removes player immunity flags, and applies triggered auras for `Melt Weapon` and `Elemental Fire`.

**SpellHitTarget**
Checks if the spell `SPELL_ELEMENTAL_FIRE_KILL` hits Majordomo Executus. If so, it starts the `m_uiEnterCombatTimer` to initiate the entrance sequence after a delay.

**JustDied**
Called upon Ragnaros's death. Updates the instance data to `DONE`.

**KilledUnit**
Triggered when Ragnaros kills a unit. Ignores specific entries (e.g., 12018) and plays a random kill quote via `DoScriptText`.

**SummonSonsOfFlame**
Summons 8 Sons of Flame at predefined positions. For each son, it uses a `ThreatListCopier` to copy Ragnaros's threat list, selects a random target, modifies threat to ensure focus, and initiates chase movement.

**UpdateLavaBurstAI**
Manages the staggered spawning of Lava Bursts using three sequential timers (primary, secondary, tertiary). Calls `DoLavaBurst()` when each timer expires.

**DoLavaBurst**
Spawns a `GO_LAVA_BURST` game object at a random location from the `PositionOfLavaBursts` array and triggers its use.

**UpdateAI**
The main AI loop. Handles the entrance sequence, phase transitions (submerge/emerge), ability casting (Wrath, Might, Magma Blast), and calls `CheckForMelee` and `UpdateLavaBurstAI`. Manages timers and state flags for all mechanics.

**CheckForMelee**
Determines if Ragnaros should perform melee attacks. Prioritizes the current victim, then top-agro players, then pets, then other units. Modifies threat to force target switches if necessary.

**GetAI_boss_ragnaros**
Factory function that returns a new `boss_ragnarosAI` instance for a given `Creature`.

**AddSC_boss_ragnaros**
Registers the "boss_ragnaros" script with the `ScriptMgr`, linking it to the `GetAI_boss_ragnaros` factory function.

---

<!-- machine-true, projected from graph.json -->

## Map — ThreatListCopier.boss_ragnaros

*Source:* boss_ragnaros.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ThreatListCopier | ctor | — | — | — |
| Process | method | CreatureAI/AttackStart, Unit.Main/AI | — | — |
| boss_ragnarosAI | ctor | CreatureAI/SetCombatMovement, ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | InstanceData/SetData, shared_Util/urand, Unit.Main/IsAlive | — | — |
| Aggro | method | Creature.Main/SetInCombatWithZone, CreatureAI/DoCastSpellIfCan, InstanceData/SetData, Object/GetEntry, Object/GetTypeId, WorldObject.Object/RemoveFlag | — | — |
| SpellHitTarget | method | Object/GetEntry, Object/GetTypeId | — | — |
| JustDied | method | InstanceData/SetData | — | — |
| KilledUnit | method | Object/GetEntry, ScriptMgr/DoScriptText | — | — |
| SummonSonsOfFlame | method | Creature.Main/AI, Creature.Main/ProcessThreatList, Creature.Main/SelectAttackingTarget, Creature.MotionMaster/MoveChase, CreatureAI/AttackStart, ThreatManager/modifyThreatPercent#2, Unit.Main/GetMotionMaster, Unit.Main/GetThreatManager, WorldObject.Object/SummonCreature#2 | — | — |
| UpdateLavaBurstAI | method | shared_Util/urand | — | — |
| DoLavaBurst | method | GameObject/Use, shared_Util/frand, shared_Util/urand, WorldObject.Object/SummonGameObject | — | — |
| UpdateAI | method | Creature.Main/ForcedDespawn, Creature.Main/SelectAttackingTarget, Creature.Main/SetInCombatWithZone, CreatureAI/DoCastSpellIfCan, GridSearchers/GetCreatureListWithEntryInGrid#2, Log.Main/Out, Object/GetObjectGuid, Object/HasFlag, Object/ToPlayer, Player.Main/IsGameMaster, ScriptedAI/DoResetThreat, ScriptMgr/DoScriptText, shared_Util/urand, ThreatManager/getThreatList, Unit.Main/AddAura, Unit.Main/GetPowerType, Unit.Main/GetThreatManager, Unit.Main/GetVictim, Unit.Main/HandleEmote, Unit.Main/HasAura#2, Unit.Main/HasUnitState, Unit.Main/IsAlive, Unit.Main/RemoveAurasDueToSpell, Unit.Main/SelectHostileTarget, Unit.Main/SetFacingToObject, Unit.Main/SetInFront, Unit.Main/SetStandState, Unit.Main/SetTargetGuid, WorldObject.Object/FindNearestCreature, WorldObject.Object/RemoveFlag | — | — |
| CheckForMelee | method | Creature.Main/SelectAttackingTarget, Object/IsPlayer, Object/ToPlayer, Player.Main/IsGameMaster, SpellCaster/IsNonMeleeSpellCasted, ThreatManager/modifyThreatPercent#2, Unit.Main/AttackerStateUpdate, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/GetThreatManager, Unit.Main/GetVictim, Unit.Main/IsAttackReady, Unit.Main/ResetAttackTimer, WorldObject.Object/IsWithinLOSInMap | — | — |
| GetAI_boss_ragnaros | function | — | — | — |
| AddSC_boss_ragnaros | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |

---

<!-- verify: boundary-bleed | foreign: aggro, JustDied, KilledUnit, Process, reset, ThreatListCopier, UpdateAI -->
