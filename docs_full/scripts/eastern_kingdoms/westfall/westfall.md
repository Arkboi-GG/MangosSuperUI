<!-- provenance: verbose -->
# westfall

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# westfall.cpp

## Purpose & Responsibilities

`westfall.cpp` implements the scripted behavior for **Daphne Stilwell** (NPC) in the Westfall zone, specifically supporting the escort quest **"The Tome of Valor"** (Quest ID 1651).

The unit provides:
1.  **AI Logic (`npc_daphne_stilwellAI`)**: Inherits from `npc_escortAI` to manage pathfinding, waypoint progression, and combat. It orchestrates a sequence where Daphne equips a rifle, summons hostile Defias Raiders at specific waypoints to simulate combat encounters, and delivers dialogue.
2.  **Quest Hook (`QuestAccept_npc_daphne_stilwell`)**: Intercepts the quest acceptance event to initialize the escort state and trigger introductory dialogue.
3.  **Registration (`AddSC_westfall`)**: Registers the AI and quest hook with the server's script manager.

This unit does not interact with any database tables directly; all state is managed in-memory via the AI class members and the core engine's quest/escort systems.

## Member-by-Member Behavior

### Escort Initialization and State Management

**`npc_daphne_stilwellAI` (Constructor)**
Initializes the AI instance for a `Creature`. It sets the internal waypoint holder (`m_uiWPHolder`) to 0 and immediately calls `Reset()` to ensure the AI starts in a clean state. It inherits from `npc_escortAI`, gaining access to escort-specific utilities like `HasEscortState` and `Start`.

**`Reset`**
Called when the escort is reset (e.g., player dies, escort fails, or manually reset).
*   If the escort is currently active (`STATE_ESCORT_ESCORTING`), it checks `m_uiWPHolder` to determine if the reset occurred during a specific combat phase (waypoints 7, 8, or 9). If so, it plays specific "down" dialogue lines (`SAY_DS_DOWN_1`, etc.) via `ScriptMgr::DoScriptText`.
*   If the escort is not active, it resets `m_uiWPHolder` to 0.
*   It always resets `m_uiShootTimer` to 0, stopping any pending ranged attacks.

### Waypoint Progression and Event Triggering

**`WaypointReached`**
The core logic driver, called by the base `npc_escortAI` when Daphne arrives at a predefined path point. It updates `m_uiWPHolder` and executes actions based on the waypoint ID:
*   **Waypoint 4**: Equips Daphne with a virtual item (ID 6946, likely a rifle) in the main hand, sets her sheath state to ranged, and triggers a standing use emote.
*   **Waypoints 7, 8, 9**: These represent combat phases. At each, Daphne summons multiple **Defias Raiders** (NPC ID 6180) at hardcoded coordinates near her position. The number of raiders increases with each waypoint (3 at WP7, 4 at WP8, 5 at WP9). They are summoned as temporary creatures that despawn after 30 seconds or when out of combat.
*   **Waypoint 10**: Calls `SetRun(false)`, causing Daphne to stop running and walk.
*   **Waypoint 11**: Plays the prologue dialogue (`SAY_DS_PROLOGUE`).
*   **Waypoint 13**: Resets equipment slots, sets sheath to unarmed, and triggers a standing use emote (likely indicating she put away her weapon).
*   **Waypoint 17**: The final waypoint. Retrieves the player associated with the escort via `GetPlayerForEscort` and triggers the quest completion event `GroupEventHappens` for `QUEST_TOME_VALOR`.

### Combat Behavior

**`AttackStart`**
Initiates combat when a hostile unit (`pWho`) is detected.
*   Attempts to attack the target.
*   If successful, adds threat, sets both entities as being in combat with each other, and commands the motion master to chase the target with a 30.0f distance offset.

**`JustSummoned`**
Called by the engine when a creature summoned by Daphne (via `WaypointReached`) spawns.
*   Immediately forces the summoned creature's AI to attack Daphne (`m_creature`), ensuring the Defias Raiders engage her immediately upon spawning.

**`UpdateEscortAI`**
The periodic update loop (called every tick/delta time).
*   Checks for a valid hostile target. If none, returns early.
*   **Ranged Attack Logic**: Maintains a `m_uiShootTimer`. If the timer expires:
    *   Resets the timer to 1000ms.
    *   Checks if the victim is out of melee range (`!CanReachWithMeleeAutoAttack`).
    *   If out of range, casts `SPELL_SHOOT` (ID 6660) on the victim.
*   Decrements the timer if not expired.
*   Calls `DoMeleeAttackIfReady()` to handle standard melee swings.

### Quest Integration and Registration

**`QuestAccept_npc_daphne_stilwell`**
A global function hooked to the quest accept event.
*   Checks if the accepted quest is `QUEST_TOME_VALOR` (1651).
*   If so, plays the start dialogue (`SAY_DS_START`).
*   Casts the creature's AI to `npc_daphne_stilwellAI` and calls `Start()` to begin the escort sequence, passing the player's GUID and quest data.

**`GetAI_npc_daphne_stilwell`**
Factory function that creates and returns a new instance of `npc_daphne_stilwellAI` for the given creature.

**`AddSC_westfall`**
Registration function called by `ScriptLoader::AddScripts`.
*   Creates a `Script` object.
*   Sets the script name to `"npc_daphne_stilwell"`.
*   Assigns `GetAI_npc_daphne_stilwell` as the AI getter.
*   Assigns `QuestAccept_npc_daphne_stilwell` as the quest accept handler.
*   Registers the script with the `ScriptMgr`.

## Cross-Unit Boundaries

*   **`npc_escortAI` (Base Class)**: `npc_daphne_stilwellAI` inherits heavily from this. It relies on `npc_escortAI` for pathfinding, waypoint management, and state tracking (`HasEscortState`, `GetPlayerForEscort`, `Start`).
*   **`ScriptMgr`**: Used by `Reset`, `WaypointReached`, and `QuestAccept_npc_daphne_stilwell` to play dialogue text (`DoScriptText`).
*   **`Creature` / `Unit`**: The AI interacts with the core entity classes to manipulate visual state (`SetVirtualItem`, `SetSheath`, `HandleEmoteCommand`), combat state (`Attack`, `AddThreat`, `SetInCombatWith`), and movement (`GetMotionMaster`).
*   **`WorldObject`**: Used in `WaypointReached` to summon Defias Raiders (`SummonCreature`).
*   **`Player`**: Used in `WaypointReached` to trigger quest completion (`GroupEventHappens`) and in `QuestAccept_npc_daphne_stilwell` to retrieve the player's GUID.
*   **`ScriptLoader`**: Calls `AddSC_westfall` during server startup to register the scripts.

## Data Model

This unit does not query or modify any database tables directly. All quest data (IDs, text entries, spell IDs) is referenced via constants defined in the `DaphneStilwellData` enum or hardcoded values. The quest logic relies on the core engine's in-memory quest definitions.

## Notable Implementation Details

*   **Hardcoded Summon Coordinates**: The Defias Raiders are summoned at fixed world coordinates in `WaypointReached`. This assumes Daphne is always at a predictable location relative to these points when reaching waypoints 7, 8, and 9. If the path changes or Daphne is moved, the raiders may spawn far from her.
*   **Aggressive Timer Reset**: In `UpdateEscortAI`, `m_uiShootTimer` is reset to 1000ms *before* checking if the spell can be cast. This means the check happens roughly every second, regardless of whether the spell was successfully cast or failed.
*   **Melee Range Check**: The ranged spell `SPELL_SHOOT` is only cast if the victim is *not* within melee auto-attack range. This prevents Daphne from shooting while standing next to an enemy, adhering to typical ranged combat logic.
*   **Dialogue on Reset**: The `Reset` method plays specific dialogue if the escort is interrupted during combat phases (waypoints 7-9). This provides feedback to the player if the escort fails mid-fight.
*   **Immediate Aggro on Summons**: `JustSummoned` forces the summoned Defias Raiders to attack Daphne immediately. This ensures the combat encounter starts instantly without waiting for the raiders to detect her.

## Member Reference

**`npc_daphne_stilwellAI`** (ctor): Initializes the AI, sets `m_uiWPHolder` to 0, and calls `Reset()`. Inherits from `npc_escortAI`.

**`Reset`**: Handles escort reset. Plays specific dialogue if reset occurs during combat waypoints (7-9) via `ScriptMgr::DoScriptText`. Resets `m_uiWPHolder` and `m_uiShootTimer`.

**`WaypointReached`**: Core logic for escort progression. Equips rifle at WP4. Summons Defias Raiders at WPs 7, 8, 9 using `WorldObject::SummonCreature`. Stops running at WP10. Plays prologue at WP11. Unequips at WP13. Triggers quest completion at WP17 via `Player::GroupEventHappens`.

**`AttackStart`**: Initiates combat. Attacks target, adds threat, sets combat state, and chases target via `Unit::GetMotionMaster`.

**`JustSummoned`**: Forces summoned creatures to attack Daphne immediately via `CreatureAI::AttackStart`.

**`UpdateEscortAI`**: Periodic update. Manages `m_uiShootTimer`. Casts `SPELL_SHOOT` if out of melee range. Handles melee attacks via `CreatureAI::DoMeleeAttackIfReady`.

**`QuestAccept_npc_daphne_stilwell`**: Global quest hook. Checks for `QUEST_TOME_VALOR`. Plays start dialogue and starts the escort via `ScriptedEscortAI::Start`.

**`GetAI_npc_daphne_stilwell`**: Factory function returning a new `npc_daphne_stilwellAI` instance.

**`AddSC_westfall`**: Registers the script with `ScriptMgr` via `Script::RegisterSelf`, linking the AI and quest accept functions. Called by `ScriptLoader::AddScripts`.

---

<!-- machine-true, projected from graph.json -->

## Map — westfall

*Source:* westfall.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| npc_daphne_stilwellAI | ctor | ScriptedEscortAI/npc_escortAI | — | — |
| Reset | method | ScriptedEscortAI/HasEscortState, ScriptMgr/DoScriptText | — | — |
| WaypointReached | method | Creature.Main/SetVirtualItem, Player.Main/GroupEventHappens, ScriptedAI/SetEquipmentSlots, ScriptedEscortAI/GetPlayerForEscort, ScriptedEscortAI/SetRun, ScriptMgr/DoScriptText, Unit.Main/HandleEmoteCommand, Unit.Main/SetSheath, WorldObject.Object/SummonCreature#2 | — | — |
| AttackStart | method | Creature.MotionMaster/MoveChase, Unit.Main/AddThreat, Unit.Main/Attack, Unit.Main/GetMotionMaster, Unit.Main/SetInCombatWith | — | — |
| JustSummoned | method | Creature.Main/AI, CreatureAI/AttackStart | — | — |
| UpdateEscortAI | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| QuestAccept_npc_daphne_stilwell | function | Creature.Main/AI, Object/GetGUID, QuestDef/GetQuestId, ScriptedEscortAI/Start, ScriptMgr/DoScriptText | — | — |
| GetAI_npc_daphne_stilwell | function | — | — | — |
| AddSC_westfall | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
