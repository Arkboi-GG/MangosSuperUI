# boss_majordomo_executus

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_majordomo_executus

**Purpose & Responsibilities**

This unit implements the artificial intelligence and event logic for **Majordomo Executus**, a boss encounter in the Molten Core raid instance. The `boss_majordomoAI` class manages the entire lifecycle of the encounter, which consists of two distinct phases:

1.  **The Combat Phase:** Majordomo fights alongside eight summoned adds (Flamewaker Elites and Flamewaker Healers). During combat, Majordomo casts defensive buffs on himself, teleports players, and applies debuffs/buffs to his adds based on how many remain alive.
2.  **The Transition Phase:** Upon the death of all adds, Majordomo becomes friendly, initiates a scripted dialogue sequence, and then performs a cinematic movement to summon the final boss, Ragnaros.

The unit handles complex state management, including tracking summoned creature GUIDs, managing timers for spells and dialogue steps, and coordinating with the instance data system to mark the encounter as complete. It also contains specific logic to handle client patch differences regarding Ragnaros's despawn time.

**Member-by-Member Behavior**

### Initialization and State Management

**`boss_majordomoAI` (Constructor)**
Initializes the AI object. It retrieves the `ScriptedInstance` pointer from the creature's instance data, allowing the AI to update the raid's progress state. It immediately calls `Reset()` to initialize timers and spawn states.

**`Reset`**
Resets the AI state for a new encounter or upon respawn.
*   Sets the creature's movement type to IDLE.
*   Initializes `Reflection_Timer` (30s) and `TPDomo_Timer` (random 10-30s).
*   Resets `AddCount` to 8 and `AddSpawn` to false.
*   Iterates through the `m_addSpawns` array. If any GUIDs are valid, it attempts to find the corresponding creatures on the map and despawns them, clearing the GUIDs. This ensures no stale adds persist from a previous attempt.
*   Updates the instance data (`TYPE_MAJORDOMO`) to `NOT_STARTED` unless the event is already marked `DONE`. Note: The comment indicates Majordomo can respawn after 2 hours while keeping the event `DONE`, preventing re-triggering of the Ragnaros summon if the instance persists.
*   Resets all dialogue and Ragnaros event flags/timers to their initial states.

**`JustReachedHome`**
Triggered when the creature returns to its home position (e.g., after evading or despawning). It checks if the creature has the `UNIT_FLAG_PET_RENAME` flag (set during the defeat sequence) and if the defeat dialogue hasn't started yet. If so, it sets `DialogueDefeatStart0` to true, initiating the post-combat dialogue sequence in `UpdateAI`.

### Combat Logic

**`Aggro`**
Triggered when Majordomo enters combat.
*   Checks if the faction is not friendly (ensuring this doesn't trigger during the post-defeat phase).
*   Iterates through all valid add GUIDs. For each alive add, it casts `SPELL_SEPARATION_ANXIETY` (aura that punishes adds for moving too far).
*   Casts `SPELL_AEGIS_OF_RAGNAROS` on itself (full heal).
*   Plays the aggro sound (`SAY_AGGRO`).
*   Updates instance data to `IN_PROGRESS`.

**`KilledUnit`**
Triggered when Majordomo kills a player.
*   Checks if the faction is not friendly.
*   Plays the slay sound (`SAY_SLAY`) via `ScriptMgr::DoScriptText`.

**`SummonedCreatureJustDied`**
Triggered when one of the eight adds dies.
*   Decrements `AddCount`.
*   **Encouragement:** If `AddCount > 0`, it casts `SPELL_ENCOURAGEMENT` on all remaining alive adds.
*   **Immunity:** If `AddCount <= 4` (half or fewer remains), it casts `SPELL_IMMUNITY` on all remaining alive adds.
*   **Champion:** If `AddCount == 1` (last add standing), it plays `SAY_LAST_ADD` and casts `SPELL_CHAMPION` on the last add.
*   **Defeat:** If `AddCount == 0`:
    *   Marks the instance data as `DONE` (if not already).
    *   Calls `EnterEvadeMode()` to stop combat.
    *   Sets unit flags to `PET_RENAME | IMMUNE_TO_PLAYER | IN_COMBAT`. This makes Majordomo untargetable and immune to damage, transitioning him into the dialogue phase.

**`UpdateAI`**
The main update loop, handling timers, spawning, dialogue, and combat abilities.

1.  **Add Spawning:** If the creature is not immune, not friendly, and adds haven't been spawned yet (`!AddSpawn`), it iterates through the static `m_aBosspawnLocs` array. It despawns any existing adds at those slots and summons new ones using `SummonCreature`. It stores the new GUIDs in `m_addSpawns` and sets `AddSpawn = true`.
2.  **Defeat Dialogue Sequence:**
    *   **Step 0 (`DialogueDefeatStart0`):** Waits 2.4s, then sets faction to `FACTION_FRIENDLY`, sets immunity flags, and plays `SAY_DEFEAT1`. Transitions to Step 1.
    *   **Step 1 (`DialogueDefeatStart1`):** Waits 3.6s, removes `IN_COMBAT` flag (keeping `IMMUNE_TO_PLAYER`). Waits until 7.7s total, plays `SAY_DEFEAT2`, transitions to Step 2.
    *   **Step 2 (`DialogueDefeatStart2`):** Waits 8.6s, plays `SAY_DEFEAT3`, transitions to Step 3.
    *   **Step 3 (`DialogueDefeatStart3`):** Checks if Majordomo is near coordinates `(758.089, -1176.71)` and is friendly. If so, waits 17.6s, then casts `SPELL_VISUAL_TELEPORT` and transitions to `DialogueTeleportStart`.
3.  **Teleport Sequence:**
    *   **Start (`DialogueTeleportStart`):** Waits 1.51s, casts `SPELL_MAJORDOMO_TELEPORT`, transitions to `DialogueTeleportFinished`.
    *   **Finish (`DialogueTeleportFinished`):** Waits 0.1s, sets `UNIT_NPC_FLAG_GOSSIP`. This allows players to interact with Majordomo via gossip to trigger the Ragnaros summon.
4.  **Ragnaros Summon Event:**
    *   If `RagnarosEventStart` is true and faction is friendly, it increments `DialogueRagnaros_M` every second and calls `DomoEvent()`.
5.  **Combat Abilities (if not friendly and has targets):**
    *   **Aegis:** If health < 50%, casts `SPELL_AEGIS_OF_RAGNAROS`.
    *   **Reflection/Damage Shield:** Every 30s, randomly casts `SPELL_MAGIC_REFLECTION` or `SPELL_DAMAGE_SHIELD` on all alive adds.
    *   **Teleport:** Every 20-30s, randomly selects a player target (current victim or random attacker) and casts either `SPELL_TELEPORT_TARGET` or `SPELL_TELEPORT_RANDOM`. Resets threat on successful cast.
    *   **Melee:** Calls `DoMeleeAttackIfReady`.

**`OnScriptEventHappened`**
Triggered by external scripts (likely the gossip menu interaction).
*   Checks if the invoker is a player.
*   Sets `RagnarosEventStart = true`.
*   Clears `UNIT_NPC_FLAGS` (removing gossip option).
*   This triggers the `DomoEvent` sequence in `UpdateAI`.

### Ragnaros Summoning Sequence

**`DomoEvent`**
Executes the cinematic steps for summoning Ragnaros, driven by the `DialogueRagnaros_M` counter incremented in `UpdateAI`.
*   **Case 6:** Moves Majordomo to `POINT_SUMMON1`. Summons visual GameObjects (`OBJECT_LAVA_STEAM`, `OBJECT_LAVA_SPLASH`). Casts `SPELL_SUMMON_RAGNAROS`. Plays `SAY_MAJ`.
*   **Case 15:** Sets orientation to face the summoning spot.
*   **Case 21:** Plays `SAY_SUMMON_MAJ`.
*   **Case 28:** Summons Ragnaros (`NPC_RAGNAROS`) at specific coordinates. Calculates despawn time based on `sWorld.GetWowPatch()`: 2 hours for patch 1.4+, 1 hour otherwise. Sets Majordomo to face Ragnaros.
*   **Case 36:** Finds Ragnaros, plays `SAY_ARRIVAL1_RAG`, and triggers a roar emote.
*   **Case 50:** Plays `SAY_ARRIVAL2_MAJ`.
*   **Case 60:** Finds Ragnaros, sets Ragnaros's target to Majordomo, plays `SAY_ARRIVAL3_RAG`, and triggers another roar.
*   **Case 76:** Removes invincibility threshold from Majordomo. Finds Ragnaros and casts `SPELL_ELEMENTAL_FIRE` on Majordomo, killing him. Control passes to Ragnaros's script.

**`MovementInform`**
Handles pathfinding completion events.
*   **`POINT_RESPAWN`:** Sets orientation to `POINT_RESPAWN_O`.
*   **`POINT_SUMMON1`:** Clears motion master and moves to `POINT_SUMMON2`.
*   **`POINT_SUMMON2`:** Clears motion master and moves to `POINT_SUMMON3` (slightly offset from SUMMON2 to ensure movement trigger).
*   **`POINT_SUMMON3`:** Clears motion master and sets to IDLE.

### Registration

**`GetAI_boss_majordomo`**
Factory function that creates and returns a new `boss_majordomoAI` instance for a given creature.

**`AddSC_boss_majordomo`**
Registers the script with the engine. Creates a `Script` object, assigns the name `"boss_majordomo"` and the `GetAI` function, and registers it with `ScriptMgr`. Called by `ScriptLoader::AddScripts`.

**Cross-Unit Boundaries**

*   **`ScriptedAI` / `ScriptedInstance`:** Inherits from `ScriptedAI` for base AI functionality (timers, casting helpers). Uses `ScriptedInstance` to manage raid-wide state (`TYPE_MAJORDOMO`).
*   **`Creature` / `WorldObject` / `Unit`:** Interacts heavily with the creature object (`m_creature`) to modify flags, faction, health, position, and summon/despawn entities. Uses `Unit` methods for targeting and threat management.
*   **`Map`:** Used to retrieve creature pointers from GUIDs (`GetCreature`).
*   **`ScriptMgr`:** Used to play sounds (`DoScriptText`).
*   **`World`:** Used to check the client patch version (`GetWowPatch`) to determine Ragnaros's despawn duration.
*   **`shared_Util`:** Uses `urand` for random number generation in combat abilities.

**Data Model**

This unit does not directly access database tables. It relies on in-memory instance data (`ScriptedInstance`) and static configuration arrays (`m_aBosspawnLocs`) defined within the source file.

**Notable Implementation Details**

*   **Patch-Specific Logic:** The `DomoEvent` method (case 28) explicitly checks `sWorld.GetWowPatch() >= WOW_PATCH_104` to determine whether Ragnaros should despawn after 1 or 2 hours. This reflects a historical change in the game client.
*   **State Persistence:** The `Reset` method carefully preserves the `DONE` state in instance data if the event has already completed, allowing Majordomo to respawn without resetting the raid's progress toward Ragnaros.
*   **Add Tracking:** The AI maintains an array of 8 `ObjectGuid`s (`m_addSpawns`) to track summoned adds. It uses these GUIDs to apply spells and check status, rather than relying on proximity or entry IDs alone. This ensures precise control over the specific adds spawned for this encounter.
*   **Dialogue Timing:** The post-defeat dialogue is implemented as a state machine in `UpdateAI` using boolean flags (`DialogueDefeatStart0-3`) and a shared timer (`DialogueDefeatTimer`). Each step advances only when the timer exceeds a specific threshold, ensuring strict sequencing of sounds and flag changes.
*   **Movement Chaining:** The `MovementInform` handler chains movements for the Ragnaros summon sequence. Since setting orientation or other properties might interrupt movement, the AI explicitly clears the motion master and issues new `MovePoint` commands to ensure smooth progression through the cinematic path.

## Member Reference

**`boss_majordomoAI`** (ctor): Initializes the AI, retrieves the instance data pointer, and calls `Reset()`.

**`Reset`**: Resets all timers, flags, and add counts. Despawn any lingering adds. Updates instance data to `NOT_STARTED` unless already `DONE`.

**`SummonedCreatureJustDied`**: Handles add deaths. Decrements count. Applies `ENCOURAGEMENT`, `IMMUNITY`, or `CHAMPION` spells based on remaining count. Triggers defeat sequence if count reaches 0.

**`JustReachedHome`**: Checks for `PET_RENAME` flag and starts the defeat dialogue sequence if conditions are met.

**`KilledUnit`**: Plays slay sound if not in friendly phase.

**`Aggro`**: Applies `SEPARATION_ANXIETY` to adds, casts `AEGIS_OF_RAGNAROS` on self, plays aggro sound, and updates instance data to `IN_PROGRESS`.

**`MovementInform`**: Handles pathfinding completion. Chains movements for the Ragnaros summon cinematic and sets orientation.

**`DomoEvent`**: Executes the Ragnaros summon cinematic steps based on a counter. Includes movement, visual effects, summoning Ragnaros, dialogue, and finally killing Majordomo.

**`UpdateAI`**: Main loop. Spawns adds if needed. Manages defeat dialogue states. Handles Ragnaros summon event trigger. Executes combat abilities (Aegis, Reflection/Shield, Teleport) and melee attacks.

**`OnScriptEventHappened`**: Triggered by gossip interaction. Starts the Ragnaros summon event sequence.

**`GetAI_boss_majordomo`**: Factory function to create the AI instance.

**`AddSC_boss_majordomo`**: Registers the script with the engine.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_majordomo_executus

*Source:* boss_majordomo_executus.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_majordomoAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | Creature.Main/DespawnOrUnsummon, Creature.Main/SetDefaultMovementType, InstanceData/GetData, InstanceData/SetData, Map.Main/GetCreature, ObjectGuid/IsEmpty, ObjectGuid/ObjectGuid, WorldObject.Object/GetMap | — | — |
| SummonedCreatureJustDied | method | CreatureAI/DoCastSpellIfCan, InstanceData/GetData, InstanceData/SetData, Map.Main/GetCreature, ObjectGuid/IsEmpty, ScriptedAI/EnterEvadeMode, Unit.Main/IsAlive, WorldObject.Object/GetMap, WorldObject.Object/MonsterYell#2, WorldObject.Object/SetUInt32Value | — | — |
| JustReachedHome | method | Object/HasFlag | — | — |
| KilledUnit | method | ScriptMgr/DoScriptText, Unit.Main/GetFactionTemplateId | — | — |
| Aggro | method | CreatureAI/DoCastSpellIfCan, InstanceData/GetData, InstanceData/SetData, Map.Main/GetCreature, ObjectGuid/IsEmpty, ScriptMgr/DoScriptText, Unit.Main/GetFactionTemplateId, Unit.Main/IsAlive, WorldObject.Object/GetMap | — | — |
| MovementInform | method | Creature.MotionMaster/MoveIdle, Creature.MotionMaster/MovePoint, MotionMaster/Clear, Unit.Main/GetMotionMaster, WorldObject.Object/SetOrientation | — | — |
| DomoEvent | method | Creature.MotionMaster/MovePoint, Object/GetGUID, ObjectGuid/ObjectGuid#5, ScriptMgr/DoScriptText, SpellCaster/CastSpell#2, Unit.Main/GetMotionMaster, Unit.Main/HandleEmote, Unit.Main/SetFacingToObject, Unit.Main/SetInvincibilityHpThreshold, Unit.Main/SetTargetGuid, World/GetWowPatch, WorldObject.Object/FindNearestCreature, WorldObject.Object/SetOrientation, WorldObject.Object/SummonCreature#2, WorldObject.Object/SummonGameObject | — | — |
| UpdateAI | method | Creature.Main/DespawnOrUnsummon, Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Map.Main/GetCreature, Object/GetObjectGuid, Object/HasFlag, Object/IsPlayer, ObjectGuid/IsEmpty, ObjectGuid/ObjectGuid, ScriptedAI/DoResetThreat, ScriptMgr/DoScriptText, shared_Util/urand, Unit.Main/GetFactionTemplateId, Unit.Main/GetHealthPercent, Unit.Main/GetVictim, Unit.Main/IsAlive, Unit.Main/SelectHostileTarget, Unit.Main/SetFactionTemplateId, WorldObject.Object/GetDistance2d#4, WorldObject.Object/GetMap, WorldObject.Object/SetUInt32Value, WorldObject.Object/SummonCreature#2 | — | — |
| OnScriptEventHappened | method | Object/IsPlayer, WorldObject.Object/SetUInt32Value | — | — |
| GetAI_boss_majordomo | function | — | — | — |
| AddSC_boss_majordomo | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
