# boss_skeram

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_skeram

**Purpose & Responsibilities**  
This unit implements the artificial intelligence for **Prophet Skeram**, a boss encounter in the *Temple of Ahn'Qiraj* instance. It handles Skeram’s combat mechanics, including spell casting, target selection, illusion summoning, teleportation ("blink") sequences, and interaction with the instance script (`ScriptedInstance`) to track encounter state. The AI also manages the behavior of Skeram’s summoned illusions (images), ensuring they mimic Skeram’s actions during blink phases and despawn appropriately upon death or encounter failure.

---

## Member-by-Member Behavior

### Initialization & State Management
- **`boss_skeramAI` (constructor)**: Initializes the AI by retrieving the instance data pointer, setting `IsImage` to `false`, and calling `Reset()` to initialize timers and state variables.
- **`Reset`**: Resets all spell timers to randomized intervals, sets the next split threshold to 75% health, ensures visibility is on, clears image pointers and controlled player GUID, and configures melee Z-reach to handle pathing limitations around Skeram’s platforms.
- **`JustReachedHome`**: Handles retreat/failure scenarios. Cancels any active fulfillment effects, kills the creature if it’s an image, and marks the encounter as failed in the instance data.

### Combat & Spell Mechanics
- **`UpdateAI`**: The core update loop. Manages timers for:
  - **Arcane Explosion**: Casts if more than `m_maxMeleeAllowed` players are in melee range.
  - **Earth Shock**: Spams on the current target if out of melee range.
  - **True Fulfillment**: Mind-controls the nearest hostile player within 40 yards, applying haste, healing modification, and immunity buffs.
  - **Blink**: Triggers teleportation sequence every 10–18 seconds.
  - **Image Summoning**: Summons two illusions when Skeram’s health drops below the next split threshold (75%, 50%, 25%).
- **`Aggro`**: Plays a random aggro sound, marks the encounter as in-progress, and calculates `m_maxMeleeAllowed` based on raid size (patch 1.12+) or a fixed value (pre-1.12).
- **`KilledUnit`**: Plays a random slay sound when a player is killed.
- **`JustDied`**: If an image, cancels fulfillment and despawns. If the main Skeram, plays death sound, sets respawn delay, binds the killer’s group to the instance, and marks the encounter as done.

### Targeting & Line-of-Sight
- **`MoveInLineOfSight`**: Extends aggro radius to 28 yards for players not feigning death, triggering `AttackStart` if conditions are met.

### Illusion & Blink Mechanics
- **`JustSummoned`**: Configures summoned images by setting their max health based on Skeram’s current health phase, matching Skeram’s health percentage, hiding them initially, and assigning them to `ImageA` or `ImageB`. Triggers `UnisonBlink` when both images are present.
- **`UnisonBlink`**: Prepares Skeram and both images for teleportation by removing auras, clearing target icons, interrupting spells, and removing attackers. Then calls `CastBlink` for each entity.
- **`CastBlink` (single parameter)**: A wrapper that invokes the two-parameter `CastBlink#2` with a default mask allowing any of the three platforms.
- **`CastBlink#2` (two parameters)**: The primary teleportation logic. It selects a random platform (0, 1, or 2) using a bitmask to avoid duplicates among Skeram and his images. It casts the corresponding blink spell, resets threat, resets the Earth Shock timer, and makes the caster visible.
- **`CancelFulfillment`**: Removes True Fulfillment and associated buffs from the previously controlled player.

### Registration
- **`GetAI_boss_skeram`**: Factory function returning a new `boss_skeramAI` instance.
- **`AddSC_boss_skeram`**: Registers the script with the engine, linking the name `"boss_skeram"` to the AI factory.

---

## Cross-Unit Boundaries

- **Calls into `ScriptedAI`**: Inherits base AI functionality (e.g., `DoCastSpellIfCan`, `DoMeleeAttackIfReady`, `GetPlayersWithinRange`, `DoResetThreat`, `DoStopAttack`).
- **Calls into `WorldObject.Object`**: Uses `GetInstanceData` to retrieve instance-specific data and `GetMap` to access map-level operations.
- **Calls into `shared_Util`**: Uses `urand` for randomizing timers and spell choices.
- **Calls into `Unit.Main`**: Interacts with health, visibility, auras, targeting, and combat states (e.g., `SetMeleeZReach`, `RemoveAurasDueToSpellByCancel`, `GetHealthPercent`, `ClearComboPointHolders`, `InterruptSpellsCastedOnMe`, `RemoveAllAttackers`, `RemoveAllAuras`, `SetVisibility`, `CanReachWithMeleeAutoAttack`, `DoKillUnit`, `GetVictim`, `SelectHostileTarget`, `FindNearestHostilePlayer`).
- **Calls into `Creature.Main`**: Manages creature lifecycle (e.g., `ForcedDespawn`, `SetRespawnDelay`, `GetRespawnTimeEx`, `AI`, `SetInCombatWithZone`).
- **Calls into `InstanceData`**: Updates encounter state (`TYPE_SKERAM`) via `SetData` and `GetData`.
- **Calls into `Map.Main`**: Retrieves player counts (`GetPlayersCountExceptGMs`) and binds groups to instances (`BindToInstanceOrRaid`).
- **Calls into `ScriptMgr`**: Plays scripted sounds via `DoScriptText`.
- **Calls into `BasicAI`**: Calls `MoveInLineOfSight` for standard behavior fallback.
- **Calls into `CreatureAI`**: Calls `AttackStart` to initiate combat.
- **Calls into `Object`**: Checks type IDs and retrieves object GUIDs.
- **Calls into `SpellCaster`**: Directly casts spells via `CastSpell`.
- **Calls into `World`**: Checks the game patch version via `GetWowPatch`.
- **Calls into `ZoneScript`**: Accesses map data via `GetMap`.
- **Called by `ScriptLoader.AddScripts`**: The registration function `AddSC_boss_skeram` is invoked by the script loader to register this AI.

---

## Data Model

This unit does **not** interact with any database tables. All state is managed in-memory via timers, instance data, and creature properties.

---

## Notable Implementation Details

1. **Illusion Health Scaling**:  
   Illusions’ max health is dynamically scaled based on Skeram’s current health phase:
   - Below 25%: 50% of original max health.
   - 25–50%: 20% of original max health.
   - Above 50%: 10% of original max health.  
   Their current health percentage matches Skeram’s at the time of summoning.

2. **Blink Position Selection**:  
   Uses a bitmask (`0x7`) to ensure Skeram and both images teleport to **distinct** platforms. The `Bogo select` loop in `CastBlink#2` retries until an unused position is chosen, then removes it from the mask.

3. **True Fulfillment Targeting**:  
   Always targets the **nearest** hostile player within 40 yards, regardless of tank status. Previous targets lose their buffs via `CancelFulfillment`.

4. **Arcane Explosion Threshold**:  
   The number of melee players required to trigger Arcane Explosion scales with raid size in patch 1.12+ (`players / 10`), but defaults to 4 in earlier patches.

5. **Pathing Workaround**:  
   Skeram’s platforms have complex geometry. The AI sets `SetMeleeZReach(74.0f)` to allow partial paths near ledges, acknowledging that full pathing onto raised areas is unsupported.

6. **Image Despawn Logic**:  
   Images despawn instantly if Skeram dies (`JustDied` checks `IsImage`). They also kill themselves if the encounter fails (`JustReachedHome`).

7. **Earth Shock Delay After Blink**:  
   The Earth Shock timer is reset to 2000ms after each blink in `CastBlink#2`, introducing a brief pause before spamming resumes.

---

## Member Reference

- **`boss_skeramAI`**: Constructor initializing AI state, instance data, and calling `Reset`.
- **`Reset`**: Resets timers, health thresholds, visibility, and pathing settings.
- **`CancelFulfillment`**: Removes True Fulfillment and related buffs from the controlled player.
- **`MoveInLineOfSight`**: Extends aggro radius to 28 yards for non-feigning players.
- **`KilledUnit`**: Plays random slay sounds.
- **`JustDied`**: Handles death logic for main Skeram and images, updating instance state.
- **`Aggro`**: Sets encounter state, plays aggro sounds, and calculates melee threshold.
- **`JustReachedHome`**: Handles retreat/failure, canceling effects and marking encounter as failed.
- **`UpdateAI`**: Core loop managing spells, targeting, image summoning, and blink triggers.
- **`JustSummoned`**: Configures illusions’ health, visibility, and assigns them to `ImageA`/`ImageB`.
- **`UnisonBlink`**: Prepares Skeram and images for synchronized teleportation.
- **`CastBlink`**: Wrapper method that invokes `CastBlink#2` with a default mask.
- **`CastBlink#2`**: Primary teleportation logic selecting distinct platforms via bitmask, casting spells, and resetting timers.
- **`GetAI_boss_skeram`**: Factory function for creating the AI instance.
- **`AddSC_boss_skeram`**: Registers the script with the engine.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_skeram

*Source:* boss_skeram.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_skeramAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | ObjectGuid/Clear, shared_Util/urand, Unit.Main/SetMeleeZReach, Unit.Main/SetVisibility | — | — |
| CancelFulfillment | method | Map.Main/GetPlayer, Unit.Main/RemoveAurasDueToSpellByCancel, WorldObject.Object/GetMap | — | — |
| MoveInLineOfSight | method | BasicAI/MoveInLineOfSight, CreatureAI/AttackStart, Object/GetTypeId, Unit.Main/HasAuraType, Unit.Main/IsInCombat, WorldObject.Object/IsWithinDistInMap | — | — |
| KilledUnit | method | ScriptMgr/DoScriptText, shared_Util/urand | — | — |
| JustDied | method | Creature.Main/ForcedDespawn, Creature.Main/GetRespawnTimeEx, Creature.Main/SetRespawnDelay, InstanceData/SetData, Map.Main/BindToInstanceOrRaid, ScriptMgr/DoScriptText, Unit.Main/GetCharmerOrOwnerPlayerOrPlayerItself, WorldObject.Object/GetMap | — | — |
| Aggro | method | InstanceData/GetData, InstanceData/SetData, Map.Main/GetPlayersCountExceptGMs, ScriptMgr/DoScriptText, World/GetWowPatch, ZoneScript/GetMap#2 | — | — |
| JustReachedHome | method | InstanceData/SetData, Unit.Main/DoKillUnit | — | — |
| UpdateAI | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, InstanceData/GetData, Object/GetObjectGuid, ScriptedAI/GetPlayersWithinRange, ScriptMgr/DoScriptText, shared_Util/urand, SpellCaster/CastSpell#2, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/DoKillUnit, Unit.Main/GetHealthPercent, Unit.Main/GetMeleeReach, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, WorldObject.Object/FindNearestHostilePlayer | — | — |
| JustSummoned | method | Creature.Main/AI, Creature.Main/SetInCombatWithZone, Object/GetEntry, Unit.Main/GetHealthPercent, Unit.Main/GetMaxHealth, Unit.Main/SetHealthPercent, Unit.Main/SetMaxHealth, Unit.Main/SetVisibility | — | — |
| UnisonBlink | method | CreatureAI/ClearTargetIcon, Unit.Main/ClearComboPointHolders, Unit.Main/InterruptSpellsCastedOnMe, Unit.Main/RemoveAllAttackers, Unit.Main/RemoveAllAuras, Unit.Main/SetVisibility | — | — |
| CastBlink | method | — | — | — |
| CastBlink#2 | method | Creature.Main/AI, ScriptedAI/DoResetThreat, ScriptedAI/DoStopAttack, shared_Util/urand, SpellCaster/CastSpell#2, Unit.Main/SetVisibility | — | — |
| GetAI_boss_skeram | function | — | — | — |
| AddSC_boss_skeram | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
