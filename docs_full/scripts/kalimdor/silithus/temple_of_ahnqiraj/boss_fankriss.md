# boss_fankriss

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_fankriss

**Purpose & Responsibilities**

`boss_fankriss.cpp` implements the artificial intelligence for the **Fankriss the Unyielding** encounter in the Temple of Ahn'Qiraj (AQ40) raid instance. This unit manages three distinct creature behaviors:
1.  **Boss Fankriss (`boss_fankrissAI`)**: The primary boss logic, handling complex mechanics including periodic summoning of "Spawn of Fankriss" (worms), casting entangling spells on players to teleport them to specific locations, and spawning "Vek'niss Hatchlings" at those locations. It also manages the boss's large aggro radius and evasion conditions.
2.  **Spawn of Fankriss (`creature_spawn_fankrissAI`)**: The AI for the summoned worm adds. These creatures have a simple enrage mechanic that triggers if they are not killed within a specific timeframe.
3.  **Vek'niss Hatchling (`creature_vekniss_hatchlingAI`)**: The AI for the hatchlings spawned by the boss's entangle mechanic. They have a delayed engagement timer, allowing players a brief window to kill them before they become aggressive.

The unit relies heavily on the `ScriptedAI` base class and interacts with the instance data system (`ScriptedInstance`) to track encounter progress. It does not interact with any database tables directly.

## Member-by-Member Behavior

### Boss Fankriss Logic (`boss_fankrissAI`)

This struct contains the core complexity of the encounter.

*   **State Management**:
    *   `Reset`: Initializes timers for Mortal Wound, evade checks, and web/entangle rotations. It resets the worm spawning state, clears existing hatchling batches, and shuffles the order of web locations using `std::shuffle` seeded with the current system clock.
    *   `Aggro`, `JustReachedHome`, `JustDied`: Standard instance data callbacks. `Aggro` sets the encounter state to `IN_PROGRESS`, `JustReachedHome` to `FAIL`, and `JustDied` to `DONE`.

*   **Summoning Mechanics**:
    *   `JustSummoned`: Handles logic immediately after a creature is summoned. If it's a hatchling, it increments the `aliveHatchlings` counter. If it's a Spawn of Fankriss, it forces the spawn into combat with the zone and targets a random player.
    *   `SummonedCreatureJustDied`: Decrements `aliveHatchlings` and removes the dead hatchling's GUID from the tracking vectors in `hatchlingVec`.
    *   `SummonWorm`: Summons a `NPC_SPAWN_FANKRISS` at a specific location. It dynamically casts the creature's AI to `creature_spawn_fankrissAI` to set the initial `enrageTimer`. If the cast fails, it logs an error.
    *   `SummonHatchling`: Summons a `NPC_VEKNISS_HATCHLING` at a given `SpawnLocation`. It uses `TEMPSUMMON_TIMED_DESPAWN_OUT_OF_COMBAT` with a 65-second duration. The hatchling's GUID is added to the provided batch list.

*   **Web/Entangle Mechanic**:
    *   `ReinitializeWebTimers`: Shuffles the `entangleSpells` vector (which pairs spell IDs with spawn locations) and sets randomized timers for the three web casts. The timers are staggered (approx. 2-18s, 15-28s, 25-45s) to prevent simultaneous casts. An optional `add` parameter allows delaying the first cycle.
    *   `GetHatchlingSpawnAmount`: Calculates how many hatchlings to spawn. It caps the total alive hatchlings at `MAX_HATCHLINGS` (20) and limits per-web spawns to `MAX_HATCHLINGS_PER_WEB` (4). It returns a random number between 2 and the calculated cap.
    *   `HandleHatchlings`: The main loop for the web mechanic. It iterates through the three entangle timers. If a timer expires, it selects a random player, casts the corresponding entangle spell, and marks the timer as cast. Once a web is cast, it spawns hatchlings. Depending on the `ALWAYS_HATCHLINGS_IN_3_LOCATIONS` macro, it either spawns hatchlings at all three locations or only the location associated with the cast web. After all three webs have been cast in a rotation, it waits for `entangleRotationTimer` (45s) before reinitializing the timers.

*   **Worm Spawning Logic**:
    *   Inside `UpdateAI`, the boss tracks three `Worm` structs. It spawns worms sequentially based on `spawnTimer`. Once all scheduled worms for a wave are spawned, it calculates the next wave's parameters:
        *   Randomizes the spawn order (`vIndex`).
        *   Determines the number of worms in the next wave (1-3).
        *   Sets `spawnTimer` for the next wave based on the previous wave's size (more worms = longer delay).
        *   Sets `enrageTimer` for the worms, increasing with each worm in the wave (15s, 20s, 25s).

*   **Combat & Movement**:
    *   `MoveInLineOfSight`: Implements a large aggro radius (100 yards). It ignores players in Feign Death.
    *   `UpdateAI`:
        *   **Pre-combat Aggro**: If not in combat, it iterates through all players on the map. It checks if a player is within `PULL_DISTANCE` (80 yards) of `pullCenter`, below a certain Z-coordinate (-70.0f), and has Line of Sight. If so, it initiates combat.
        *   **Mortal Wound**: Casts `SPELL_MORTAL_WOUND` on the victim every 4-8 seconds.
        *   **Evade Check**: Every 2.5 seconds, it checks if the boss's Y position exceeds 1400. If so, it evades (likely to prevent the boss from running out of bounds during zone-in glitches).

### Spawn of Fankriss Logic (`creature_spawn_fankrissAI`)

*   `Reset`: Sets the `enrageTimer` to 10 seconds.
*   `UpdateAI`: Decrements the timer. If the timer expires and the server patch is 1.10 or higher, it casts `SPELL_SPAWN_ENRAGE` on itself. It then performs melee attacks.

### Vek'niss Hatchling Logic (`creature_vekniss_hatchlingAI`)

*   `Reset`: Sets `engageTimer` to `HATCHLINGS_ATTACK_DELAY` (2.5s) and resets engagement flags.
*   `AttackedBy`: If attacked before engaging, it sets `wasAttacked = true` and immediately engages (`engageTimer = 0`).
*   `AttackStart`, `EnterCombat`, `MoveInLineOfSight`, `Aggro`: All these methods are guarded by `hasEngaged`. If the hatchling hasn't engaged yet, it ignores these events.
*   `UpdateAI`:
    *   If `engageTimer` expires and `hasEngaged` is false, it sets `hasEngaged = true`.
    *   If it wasn't manually attacked (`!wasAttacked`), it selects the nearest target. If the target is within 200 yards, it adds threat and starts attacking.
    *   Once engaged, it behaves like a standard melee attacker.

## Cross-Unit Boundaries

*   **`ScriptedAI`**: All three AI structs inherit from `ScriptedAI`. They call base methods like `DoMeleeAttackIfReady`, `DoCastSpellIfCan`, `EnterEvadeMode`, and `Aggro`.
*   **`ScriptedInstance`**: `boss_fankrissAI` retrieves the instance data via `GetInstanceData()` and calls `SetData` to update the encounter state (`TYPE_FANKRISS`) in `Aggro`, `JustReachedHome`, and `JustDied`.
*   **`Creature` / `Unit`**: The AIs extensively use `Creature` and `Unit` methods for targeting (`SelectHostileTarget`, `SelectAttackingTarget`), combat state (`IsInCombat`, `SetInCombatWithZone`), and positioning (`GetDistance`, `IsWithinLOSInMap`).
*   **`World`**: `creature_spawn_fankrissAI::UpdateAI` calls `sWorld.GetWowPatch()` to determine if the enrage spell should be cast (patch 1.10+).
*   **`ScriptMgr`**: `AddSC_boss_fankriss` registers the scripts with the script manager.

## Data Model

This unit does not interact with any database tables. All state is managed in memory via the AI structs and the instance data system.

## Notable Implementation Details

*   **Macro-Driven Behavior**: The `ALWAYS_HATCHLINGS_IN_3_LOCATIONS` macro significantly changes the difficulty. If defined, every time a player is webbed, hatchlings spawn at *all three* web locations. If undefined, they only spawn at the location of the webbed player. The default in this code is defined.
*   **Randomization**: The unit uses `std::shuffle` with `std::chrono::system_clock` for shuffling web locations and worm spawn orders. This provides better randomness than the older `urand`-based approaches often seen in legacy scripts.
*   **Hatchling Cap**: The `MAX_HATCHLINGS` constant (20) prevents the encounter from becoming unmanageable due to excessive spawns. `GetHatchlingSpawnAmount` ensures this cap is respected.
*   **Worm Enrage Scaling**: The enrage timer for worms increases with each worm in a wave (15s, 20s, 25s), encouraging players to prioritize earlier spawns.
*   **Evade Condition**: The check `if (m_creature->GetPositionY() > 1400)` in `UpdateAI` is a safety measure to prevent the boss from running out of the instance geometry, which can happen if players are pulled too far back or during zone-in transitions.
*   **Pre-Combat Pull Check**: The boss actively scans for players in a specific area (`pullCenter`, Z < -70.0f) even before combat starts. This ensures the boss engages if players approach from the correct entrance, rather than relying solely on standard aggro radii.

## Member Reference

**`creature_spawn_fankrissAI`** (ctor): Constructs the AI for Spawn of Fankriss, retrieving instance data and calling `Reset`.

**`Reset#2`** (method): Resets the `enrageTimer` to 10,000 ms for the Spawn of Fankriss.

**`UpdateAI#2`** (method): Updates the Spawn of Fankriss AI. Checks for enrage (casting `SPELL_SPAWN_ENRAGE` if timer expires and patch >= 1.10) and performs melee attacks.

**`creature_vekniss_hatchlingAI`** (ctor): Constructs the AI for Vek'niss Hatchling, retrieving instance data and calling `Reset`.

**`Reset#3`** (method): Resets the hatchling's `engageTimer` to `HATCHLINGS_ATTACK_DELAY` and clears engagement flags.

**`AttackedBy`** (method): If the hatchling is attacked before engaging, it sets `wasAttacked` and immediately engages.

**`AttackStart`** (method): Only initiates attack if `hasEngaged` is true.

**`EnterCombat`** (method): Only enters combat if `hasEngaged` is true.

**`MoveInLineOfSight#2`** (method): Only processes LoS events if `hasEngaged` is true.

**`Aggro#2`** (method): Only processes aggro events if `hasEngaged` is true.

**`UpdateAI#3`** (method): Updates the hatchling AI. If `engageTimer` expires, it engages (selecting nearest target if not already attacked). Once engaged, it performs melee attacks.

**`boss_fankrissAI`** (ctor): Constructs the boss AI, initializing worm vectors and calling `Reset`.

**`HatchlingBatch`** (ctor): Constructs a batch of hatchlings, storing their GUIDs.

**`Reset`** (method): Resets all boss timers, worm states, and hatchling counts. Shuffles web locations.

**`MoveInLineOfSight`** (method): Checks for players within 100 yards to initiate combat, ignoring Feign Death.

**`Aggro`** (method): Sets instance data to `IN_PROGRESS`.

**`JustReachedHome`** (method): Sets instance data to `FAIL`.

**`JustDied`** (method): Sets instance data to `DONE`.

**`JustSummoned`** (method): Tracks alive hatchlings and forces Spawn of Fankriss into combat with a random player.

**`SummonedCreatureJustDied`** (method): Removes dead hatchlings from tracking lists and decrements the alive count.

**`ReinitializeWebTimers`** (method): Shuffles web locations and sets randomized timers for the next cycle of entangle casts.

**`GetHatchlingSpawnAmount`** (method): Calculates the number of hatchlings to spawn, respecting global and per-web caps.

**`SummonHatchling`** (method): Summons a hatchling at a specific location and adds its GUID to a batch.

**`HandleHatchlings`** (method): Manages the entangle/web mechanic. Casts entangle spells on random players and spawns hatchlings at web locations.

**`SummonWorm`** (method): Summons a Spawn of Fankriss and sets its enrage timer.

**`UpdateAI`** (method): Main boss update loop. Handles pre-combat aggro checks, Mortal Wound casting, worm spawning logic, hatchling management, and evade conditions.

**`GetAI_boss_fankriss`** (function): Factory function returning a new `boss_fankrissAI` instance.

**`GetAI_creature_spawn_fankriss`** (function): Factory function returning a new `creature_spawn_fankrissAI` instance.

**`GetAI_creature_vekniss_hatchling`** (function): Factory function returning a new `creature_vekniss_hatchlingAI` instance.

**`AddSC_boss_fankriss`** (function): Registers the three scripts with the script manager.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_fankriss

*Source:* boss_fankriss.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| creature_spawn_fankrissAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset#2 | method | — | — | — |
| UpdateAI#2 | method | CreatureAI/DoMeleeAttackIfReady, SpellCaster/CastSpell#2, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, World/GetWowPatch | — | — |
| creature_vekniss_hatchlingAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset#3 | method | — | — | — |
| AttackedBy | method | CreatureAI/AttackedBy | — | — |
| AttackStart | method | CreatureAI/AttackStart | — | — |
| EnterCombat | method | ScriptedAI/EnterCombat | — | — |
| MoveInLineOfSight#2 | method | BasicAI/MoveInLineOfSight | — | — |
| Aggro#2 | method | ScriptedAI/Aggro | — | — |
| UpdateAI#3 | method | Creature.Main/SelectAttackingTarget, Creature.Main/SetInCombatWithZone, CreatureAI/DoMeleeAttackIfReady, ThreatManager/addThreat#3, Unit.Main/GetThreatManager, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, WorldObject.Object/GetDistance#3 | — | — |
| boss_fankrissAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| HatchlingBatch | ctor | — | — | — |
| Reset | method | shared_Util/urand | — | — |
| MoveInLineOfSight | method | BasicAI/MoveInLineOfSight, CreatureAI/AttackStart, Object/GetTypeId, Unit.Main/HasAuraType, Unit.Main/IsInCombat, WorldObject.Object/IsWithinDistInMap | — | — |
| Aggro | method | InstanceData/SetData | — | — |
| JustReachedHome | method | InstanceData/SetData | — | — |
| JustDied | method | InstanceData/SetData | — | — |
| JustSummoned | method | Creature.Main/AI, Creature.Main/SelectAttackingTarget, Creature.Main/SetInCombatWithZone, CreatureAI/AttackStart, Object/GetEntry | — | — |
| SummonedCreatureJustDied | method | Object/GetEntry, Object/GetObjectGuid | — | — |
| ReinitializeWebTimers | method | shared_Util/urand | — | — |
| GetHatchlingSpawnAmount | method | shared_Util/urand | — | — |
| SummonHatchling | method | Object/GetObjectGuid, WorldObject.Object/SummonCreature#2 | — | — |
| HandleHatchlings | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan | — | — |
| SummonWorm | method | Creature.Main/AI, Log.Main/Out, WorldObject.Object/SummonCreature#2 | — | — |
| UpdateAI | method | CreatureAI/AttackStart, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Map.Main/GetPlayers, Player.Main/IsGameMaster, ScriptedAI/EnterEvadeMode, shared_Util/urand, Unit.Main/GetVictim, Unit.Main/IsAlive, Unit.Main/IsInCombat, Unit.Main/SelectHostileTarget, WorldObject.Object/GetDistance#4, WorldObject.Object/GetMap, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/IsWithinLOSInMap | — | — |
| GetAI_boss_fankriss | function | — | — | — |
| GetAI_creature_spawn_fankriss | function | — | — | — |
| GetAI_creature_vekniss_hatchling | function | — | — | — |
| AddSC_boss_fankriss | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
