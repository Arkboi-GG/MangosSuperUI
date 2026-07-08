# ubrs_trash

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`ubrs_trash.cpp` implements the artificial intelligence for the **Blackhand Veteran** (`npc_blackhand_veteran`), a trash mob encountered in the Upper Blackrock Spire (UBRS) dungeon. This unit is part of the broader `instance_blackrock_spire` system but operates independently as a standard combatant. Its primary responsibility is to manage a specific rotation of offensive spells—Charge Bouclier, Coup Bouclier, and Frappe—while maintaining melee engagement. Additionally, it integrates with the dungeon's instance data system to record its death, likely for tracking room clearance or event progression via `TYPE_ROOM_EVENT`.

## Member-by-Member Behavior

The AI is structured around a timer-driven loop in `UpdateAI`, supported by helper methods for state management and initialization.

### Initialization and State Management

*   **`npc_blackhand_veteranAI` (Constructor)**: Initializes the AI object. It retrieves the `instance_blackrock_spire` data pointer from the creature's instance data, casting it to the specific instance type. It immediately calls `Reset()` to initialize timers and flags.
*   **`Reset`**: Resets the internal state of the AI. It sets `m_uiChargeBouclierTimer` to 0 (indicating it should trigger immediately or on the next tick depending on logic), `m_uiCoupBouclierTimer` to 2000ms, and `m_uiFrappeTimer` to 5000ms. Crucially, it sets `m_bFirstChargeDone` to `false`, ensuring the first Charge Bouclier targets the current victim rather than a random target.
*   **`ManageTimer`**: A utility method that decrements a given timer by the elapsed time (`diff`). It returns `true` if the timer has expired (i.e., the remaining time was less than `diff`), signaling that the associated action should be taken. This encapsulates the common pattern of checking and updating multiple independent timers.

### Combat Logic

*   **`UpdateAI`**: The core update loop, called periodically. It first checks if the creature has a valid hostile target; if not, it exits. It then processes three distinct abilities:
    1.  **Charge Bouclier**: If the timer expires, it selects a target. If `m_bFirstChargeDone` is false, it targets the current victim (`GetVictim`) and sets the flag to true. Subsequent charges target a random hostile unit (`SelectAttackingTarget`). Upon successful cast, the timer is reset to a random value between 8000ms and 14000ms.
    2.  **Coup Bouclier**: If the timer expires, it selects a random hostile target. It checks if the target is currently casting a non-melee spell (`IsNonMeleeSpellCasted`). If so, it applies a 25% chance check (`!urand(0, 3)`). This randomness prevents all veterans from interrupting simultaneously, which the code comments note would be "stupid." If the check passes and the cast succeeds, the timer resets to 10000ms.
    3.  **Frappe**: If the timer expires, it selects a random hostile target and casts Frappe. On success, the timer resets to 6000ms.
    Finally, it calls `DoMeleeAttackIfReady()` to handle standard physical attacks.

### Event Handling

*   **`JustDied`**: Triggered when the creature dies. It notifies the `instance_blackrock_spire` manager by calling `SetData64` with `TYPE_ROOM_EVENT` and the creature's GUID. This allows the instance script to track which specific veteran died, potentially for room-specific objectives or buffs.

### Script Registration

*   **`GetAI_npc_blackhand_veteran`**: Factory function that creates and returns a new instance of `npc_blackhand_veteranAI`.
*   **`AddSC_ubrs_trash`**: Registers the script with the engine. It creates a `Script` object, assigns the name `"npc_blackhand_veteran"`, links the `GetAI` factory function, and registers it with the `ScriptMgr`. This function is called by `ScriptLoader::AddScripts` during server startup.

## Cross-Unit Boundaries

*   **`instance_blackrock_spire`**: Called in `JustDied` via `SetData64`. The AI passes its GUID to the instance manager to log the death event under `TYPE_ROOM_EVENT`. This is a one-way communication from the mob to the instance controller.
*   **`ScriptedAI`**: The base class for `npc_blackhand_veteranAI`. Provides the framework for AI updates, target selection, and spell casting helpers like `DoCastSpellIfCan` and `DoMeleeAttackIfReady`.
*   **`Creature` / `Unit`**: Used extensively in `UpdateAI` for target selection (`SelectAttackingTarget`, `GetVictim`, `SelectHostileTarget`) and spell state checking (`IsNonMeleeSpellCasted`). These are standard engine interactions for combat logic.
*   **`ScriptMgr` / `Script`**: Used in `AddSC_ubrs_trash` to register the AI with the global script manager, making it available for creatures with the matching `script_name`.

## Data Model

This unit does not directly interact with any database tables. All configuration (spell IDs, timers) is hardcoded in the source. The only data persistence interaction is indirect through the `instance_blackrock_spire` module, which may store instance state in memory or temporary storage, but `ubrs_trash.cpp` itself performs no SQL operations.

## Notable Implementation Details

*   **Interrupt Randomization**: In `UpdateAI`, the `Coup Bouclier` ability includes a deliberate 25% failure chance (`!urand(0, 3)`) even when the target is casting. The comment explicitly states this is to prevent synchronized interrupts among multiple veterans, which would be unrealistic and overly powerful. This is a specific design choice to balance group encounters.
*   **First Charge Targeting**: The `m_bFirstChargeDone` flag ensures the first `Charge Bouclier` always hits the current victim, providing a predictable initial threat spike. Subsequent charges are random, adding unpredictability to the fight.
*   **Timer Management**: The use of `ManageTimer` abstracts the decrement-and-check logic, keeping `UpdateAI` cleaner. However, note that `m_uiChargeBouclierTimer` starts at 0 in `Reset`. Since `ManageTimer` returns `true` if `(*timer) < diff`, and `diff` is typically > 0, the charge will trigger on the very first `UpdateAI` call after reset, which is likely intended for immediate engagement.
*   **Hardcoded Spell IDs**: The spell IDs for Charge Bouclier (15749), Coup Bouclier (11972), and Frappe (14516) are hardcoded enums. Any changes to these spells in the game database would require a code change.

## Member Reference

*   **`npc_blackhand_veteranAI`**: Constructor that initializes the AI, retrieves the instance data pointer, and calls `Reset()`.
*   **`Reset`**: Method that resets all timers and the `m_bFirstChargeDone` flag to their initial values.
*   **`ManageTimer`**: Helper method that decrements a timer by `diff` and returns `true` if the timer has expired.
*   **`JustDied`**: Method that notifies the `instance_blackrock_spire` manager of the creature's death via `SetData64`.
*   **`UpdateAI`**: Main AI loop that manages the casting of Charge Bouclier, Coup Bouclier, and Frappe based on timers and target conditions, followed by melee attacks.
*   **`GetAI_npc_blackhand_veteran`**: Factory function that creates a new `npc_blackhand_veteranAI` instance.
*   **`AddSC_ubrs_trash`**: Function that registers the `npc_blackhand_veteran` script with the `ScriptMgr`.

---

<!-- machine-true, projected from graph.json -->

## Map — ubrs_trash

*Source:* ubrs_trash.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| npc_blackhand_veteranAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | — | — | — |
| ManageTimer | method | — | — | — |
| JustDied | method | instance_blackrock_spire/SetData64, Object/GetGUID | — | — |
| UpdateAI | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_npc_blackhand_veteran | function | — | — | — |
| AddSC_ubrs_trash | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
