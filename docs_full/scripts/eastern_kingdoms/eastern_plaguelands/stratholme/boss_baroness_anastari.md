<!-- provenance: verbose -->
# boss_baroness_anastari

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`boss_baroness_anastari.cpp` implements the AI for **Baroness Anastari**, a boss in the Stratholme dungeon. The core mechanic is **Possession**: the boss periodically possesses a random player, becoming invisible and invulnerable while controlling them. The AI tracks the possessed player’s health and the boss’s pre-possession position. When possession ends (player health < 25%, aura loss, or death), the boss returns to her original position, restores the player’s health to its pre-possession level (if alive), and resumes combat. Standard abilities include Banshee Wail, Banshee Curse, and Silence.

## Member-by-Member Behavior

### Initialization and Lifecycle

**`boss_baroness_anastariAI` (Constructor)**
Retrieves the `ScriptedInstance` pointer from the creature and calls `Reset()` to initialize timers and state.

**`Reset`**
Initializes ability timers (`BansheeWail`, `BansheeCurse`, `Silence`, `Possess`, `CheckPossess`). Clears possession state (`Possessed`, `PossessedPlayerGuid`) and resets `PlayerGuids`/`PlayerAggro` arrays (size 10, though only indices 0–4 are actively managed). Restores the creature to visible, selectable state, removes damage immunities, and records the creature’s current coordinates in `old_Position`.

**`JustDied`**
Signals the instance script (`m_pInstance->SetData`) that the boss is defeated. Ensures the creature is visible and selectable. Iterates through `PlayerGuids` (indices 0–4) to call `RestoreFaction` on any surviving players, clearing potential faction flags from the encounter.

**`DamageTaken`**
Sets incoming damage to 0 if `Possessed` is true, granting absolute immunity during possession.

### Combat Logic

**`UpdateAI`**
Executes the main AI loop, handling two states:

1.  **Possessed (`Possessed == true`):**
    *   Decrements `CheckPossess_Timer`. When expired, checks if the possessed player (`PossessedPlayerGuid`) has < 25% health or lost the `SPELL_POSSESS` aura.
    *   If possession ends:
        *   Removes the possess aura and restores the player’s faction.
        *   Teleports the boss back to `old_Position`.
        *   Restores the player’s health to `PlayerHealth` (memorized at possession start) if alive.
        *   Makes the boss visible, selectable, and vulnerable again.
        *   Reapplies saved threat values from `PlayerAggro` to players in `PlayerGuids` (indices 0–4) using `addThreatDirectly`.
        *   Attacks the former host and casts `SPELL_SILENCE`.
        *   Resets `Possess_Timer` (13–18s) and sets `Possessed = false`.

2.  **Normal Combat (`Possessed == false`):**
    *   Ensures visibility is `VISIBILITY_ON`.
    *   **Possession Attempt:** If `Possess_Timer` expires:
        *   Selects a random player target with > 30% health.
        *   Saves the threat of up to 5 players (indices 0–4) into `PlayerGuids` and `PlayerAggro`.
        *   If `Position_memorized` is false, records the boss’s current position and the target’s health percentage, then sets `Position_memorized = true`. *Note: This flag is never reset, so subsequent possessions use the position from the first possession.*
        *   Teleports to the target and casts `SPELL_POSSESS`.
        *   On success: makes the boss invisible, unselectable, and immune to damage. Reapplies saved threat to non-possessed players. Sets `Possessed = true` and starts `CheckPossess_Timer`.
    *   **Abilities:**
        *   `BansheeWail`: Cast on victim every 4s.
        *   `BansheeCurse`: Cast on victim every 18s if not present.
        *   `Silence`: Cast on self every 13–18s.
    *   Performs melee attacks if ready.

### Registration

**`GetAI_boss_baroness_anastari`**
Factory function allocating a new `boss_baroness_anastariAI` instance.

**`AddSC_boss_baroness_anastari`**
Registers the script with `ScriptMgr` via `ScriptLoader::AddScripts`.

## Cross-Unit Boundaries

*   **`ScriptedAI` / `ScriptedInstance`**: Inherits from `ScriptedAI` for helper methods (`DoCastSpellIfCan`, etc.). Uses `ScriptedInstance` (via `m_pInstance`) to update dungeon state on death.
*   **`Unit` / `Creature` / `WorldObject`**: Manipulates creature state (visibility, flags, position, health, faction) and queries the map for units.
*   **`ThreatManager`**: Reads/writes threat values to preserve aggro during possession transitions.
*   **`ScriptMgr` / `Script`**: Integrates the boss script into the server’s global registry.

## Data Model

This unit does not interact with any database tables. All state is held in memory.

## Notable Implementation Details

*   **Threat Preservation**: The AI manually saves and restores threat for up to 5 players (`PlayerGuids`/`PlayerAggro`) to prevent aggro loss during the invisible possession phase.
*   **Health Restoration**: The possessed player’s health is restored to its pre-possession level (`PlayerHealth`) if they survive, preventing punishment for damage taken while controlled.
*   **Position Bug**: `Position_memorized` is set to `true` after the first possession and never reset. Subsequent possessions will teleport the boss back to the position recorded during the *first* possession, not the most recent one.
*   **Array Bounds**: Arrays are size 10, but loops only iterate 0–4. Only the top 5 threats are preserved.
*   **Invulnerability**: `DamageTaken` zeroes damage during possession, ensuring the boss cannot die while controlling a player.

## Member Reference

**`boss_baroness_anastariAI`**
Constructor initializing instance data and calling `Reset`.

**`Reset`**
Resets timers, clears possession state, restores visibility/selectability, removes immunities, and records initial position.

**`JustDied`**
Updates instance state, restores visibility/selectability, and restores faction for tracked players.

**`DamageTaken`**
Nullifies damage if `Possessed` is true.

**`UpdateAI`**
Manages possession state transitions, threat preservation, health restoration, and ability casting (Banshee Wail, Curse, Silence).

**`GetAI_boss_baroness_anastari`**
Factory function creating the AI instance.

**`AddSC_boss_baroness_anastari`**
Registers the script with the server.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_baroness_anastari

*Source:* boss_baroness_anastari.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_baroness_anastariAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | Unit.Main/ApplySpellImmune, Unit.Main/SetVisibility, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/RemoveFlag | — | — |
| JustDied | method | InstanceData/SetData, Map.Main/GetUnit, ObjectGuid/ObjectGuid#5, Unit.Main/RestoreFaction, Unit.Main/SetVisibility, WorldObject.Object/GetMap, WorldObject.Object/RemoveFlag | — | — |
| DamageTaken | method | — | — | — |
| UpdateAI | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, HostileReference/getThreat, HostileReference/getUnitGuid, Map.Main/GetUnit, Object/GetGUID, Object/IsPlayer, ObjectGuid/IsPlayer, ObjectGuid/ObjectGuid#5, shared_Util/urand, ThreatManager/addThreatDirectly, ThreatManager/getThreatList, Unit.Main/ApplySpellImmune, Unit.Main/Attack, Unit.Main/GetHealthPercent, Unit.Main/GetThreatManager, Unit.Main/GetVictim, Unit.Main/GetVisibility, Unit.Main/HasAura#2, Unit.Main/IsAlive, Unit.Main/NearTeleportTo, Unit.Main/RemoveAurasDueToSpell, Unit.Main/RestoreFaction, Unit.Main/SelectHostileTarget, Unit.Main/SetHealthPercent, Unit.Main/SetInCombatWith, Unit.Main/SetVisibility, WorldObject.Object/GetMap, WorldObject.Object/GetPosition#3, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag | — | — |
| GetAI_boss_baroness_anastari | function | — | — | — |
| AddSC_boss_baroness_anastari | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
