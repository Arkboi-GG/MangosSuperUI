<!-- provenance: verbose -->
# durotar

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Durotar: Lazy Peon Quest Support (`durotar.cpp`)

**Purpose & Responsibilities**
This unit implements the AI and spell handling for the "Lazy Peons" quest (ID 5441) in Durotar. It defines `LazyPeonAI` for creature entry 10556, managing a cycle of sleeping, awakening via spell, moving to a lumber pile (entry 175784), working, and returning to sleep. It also provides `peon_wake_up`, a spell effect handler that grants quest credit to the player upon successful awakening. No database tables are accessed; all logic relies on in-memory game objects and hardcoded constants.

## Member-by-Member Behavior

### AI Lifecycle and State Management
The `LazyPeonAI` class inherits from `ScriptedAI` and uses a finite state machine (`States` enum) to manage behavior.

*   **`LazyPeonAI` (Constructor):** Initializes timers (`timer_before_working` = 3000ms, `timer_before_moving_to_lumberpile` = 2000ms), sets initial state to `STATE_SLEEPING`, clears `playerGuid`, and calls `Reset()`.
*   **`OnScriptEventHappened`:** Updates the internal `state` to the provided `uiEvent`, allowing external scripts to force state changes.
*   **`Reset`:** Empty override; performs no specific cleanup.

### Spell Interaction and Awakening
*   **`SpellHit`:** Triggered when the creature is hit by a spell. If the spell is `SPELL_AWAKEN_PEON` (19938), the creature is entry 10556, and it has the `SPELL_BUFF_SLEEP` (17743) aura, it records the caster’s GUID in `playerGuid` and transitions to `STATE_WAKEUP`, resetting the movement timer to 0.

### Main Update Loop (`UpdateAI`)
Drives the FSM based on elapsed time (`diff`):

*   **`STATE_SLEEPING`:** Casts `SPELL_BUFF_SLEEP` on itself if the aura is missing.
*   **`STATE_WORKING`:** Counts down `timer_before_sleep`. On expiry, stops the working emote, retrieves home position via `GetHomePosition`, moves back via `MovePoint`, and transitions to `STATE_MOVING_BACK`.
*   **`STATE_WAKEUP`:** Counts down `timer_before_working` (3000ms). On expiry, removes the sleep aura, transitions to `STATE_START_MOVING_TO_LUMBERPILE`, and resets timers.
*   **`STATE_START_MOVING_TO_LUMBERPILE`:** Counts down `timer_before_moving_to_lumberpile` (2000ms). On expiry:
    1.  Finds the nearest `GO_LUMBERPILE` (175784) within 20.0 units.
    2.  If found, enables walking, calculates a contact point near the pile using `GetContactPoint` (offset `CONTACT_DISTANCE + 0.2f`), and issues a pathfinding move command.
    3.  Retrieves the player from `playerGuid` and triggers `DoScriptText` with `SAY_SPELL_HIT` (5774).
    4.  Transitions to `STATE_MOVING_TO_LUMBERPILE`.

### Movement Completion (`MovementInform`)
Handles completion of movement actions:

*   **`MovementInform`:** If the completed movement is `POINT_MOTION_TYPE` ID 1:
    *   In `STATE_MOVING_TO_LUMBERPILE`: Transitions to `STATE_WORKING`, faces the nearest lumber pile, starts the working emote (`EMOTE_WORKING`), and sets `timer_before_sleep` to `WORKING_DURATION` (120,000ms).
    *   In `STATE_MOVING_BACK`: Transitions to `STATE_SLEEPING` and re-applies the sleep buff.

### External Spell Effect Handler
*   **`peon_wake_up`:** Validates that the spell is `SPELL_AWAKEN_PEON`, the target is entry 10556, and the target has the sleep aura. If valid, it casts the caster as a `Player` and calls `KilledMonsterCredit` to award quest progress.

### Registration
*   **`GetAI_LazyPeon`:** Factory function returning a new `LazyPeonAI` instance.
*   **`AddSC_durotar`:** Registers the "LazyPeons" script, linking `peon_wake_up` to `pEffectDummyCreature` and `GetAI_LazyPeon` to `GetAI`, then registers with `ScriptMgr`.

## Cross-Unit Boundaries

*   **`LazyPeonAI` calls `ObjectGuid/Clear`, `ScriptedAI/ScriptedAI`:** Initializes base AI and GUID tracking.
*   **`SpellHit` calls `Object/GetEntry`, `Object/GetObjectGuid`, `Unit.Main/HasAura#2`:** Validates creature type, identifies caster, and checks for sleep aura.
*   **`UpdateAI` calls:**
    *   `Creature.Main/GetHomePosition`: Gets spawn coordinates for return trip.
    *   `Creature.MotionMaster/MovePoint`: Commands movement to coordinates.
    *   `CreatureAI/DoCastSpellIfCan`: Casts sleep buff if allowed.
    *   `Map.Main/GetPlayer`: Resolves `playerGuid` to a live `Player` for feedback.
    *   `ScriptMgr/DoScriptText`: Triggers chat/emote messages.
    *   `Unit.Main/GetMotionMaster`, `HandleEmoteState`, `HasAura#2`, `RemoveAurasDueToSpell`, `SetWalk`: Controls movement, visuals, auras, and walk mode.
    *   `WorldObject.Object/FindNearestGameObject`, `GetContactPoint`, `GetMap`: Locates lumber pile and calculates approach vector.
*   **`MovementInform` calls:**
    *   `CreatureAI/DoCastSpellIfCan`: Re-applies sleep buff on return.
    *   `Unit.Main/HandleEmoteState`, `SetFacingToObject`: Controls visuals and orientation.
    *   `WorldObject.Object/FindNearestGameObject`: Locates lumber pile to face during work.
*   **`peon_wake_up` calls:**
    *   `Object/GetEntry`, `GetObjectGuid`, `ToPlayer`: Type checks and casting.
    *   `Player.Main/KilledMonsterCredit`: Awards quest credit.
    *   `Unit.Main/HasAura#2`: Verifies target is asleep.
*   **`AddSC_durotar` calls:**
    *   `Script/Script`, `ScriptMgr/RegisterSelf`: Engine registration.
*   **Called By:**
    *   `ScriptLoader/AddScripts` calls `AddSC_durotar` at startup.

## Data Model

This unit accesses no database tables. All configuration is hardcoded in the `LazyPeon` enum. Quest credit is handled via in-memory player state updates (`KilledMonsterCredit`).

## Notable Implementation Details

1.  **Two-Step Movement:** Moving to the lumber pile involves `STATE_START_MOVING_TO_LUMBERPILE` (calculates destination, issues move) and `STATE_MOVING_TO_LUMBERPILE` (waits for `MovementInform`). This ensures the destination is calculated once before movement begins.
2.  **Pathfinding Offset:** Uses `GetContactPoint` with `CONTACT_DISTANCE + 0.2f` to prevent clipping into the GameObject.
3.  **Delayed Feedback:** Player feedback (`DoScriptText`) occurs in `STATE_START_MOVING_TO_LUMBERPILE`, introducing a ~2-second delay after spell cast due to `timer_before_moving_to_lumberpile`.
4.  **Dual Credit/Awakening Paths:** `peon_wake_up` awards credit via spell effect, while `SpellHit` triggers AI state change. Both check identical preconditions (Spell ID, Entry, Aura), ensuring consistency.
5.  **No Fallback for Missing Lumber Pile:** If `FindNearestGameObject` fails in `STATE_START_MOVING_TO_LUMBERPILE`, the AI remains in that state indefinitely, retrying each tick without error handling or fallback.

## Member Reference

**LazyPeonAI** (ctor): Initializes AI state, timers, and GUIDs; calls `Reset()`.

**OnScriptEventHappened**: Updates internal `state` from `uiEvent`.

**Reset**: Empty override; no specific cleanup.

**SpellHit**: Checks for `SPELL_AWAKEN_PEON` on asleep peon; records caster GUID and transitions to `STATE_WAKEUP`.

**UpdateAI**: Drives FSM: applies sleep buff, counts timers, calculates movement to lumber pile, triggers player feedback, and transitions between states.

**MovementInform**: Handles movement completion: starts working emote/timer at lumber pile, or resumes sleeping on return.

**peon_wake_up**: Validates spell/target conditions and awards quest credit via `KilledMonsterCredit`.

**GetAI_LazyPeon**: Factory function returning new `LazyPeonAI` instance.

**AddSC_durotar**: Registers "LazyPeons" script with engine, linking AI and spell handler.

---

<!-- machine-true, projected from graph.json -->

## Map — durotar

*Source:* durotar.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| LazyPeonAI | ctor | ObjectGuid/Clear, ScriptedAI/ScriptedAI | — | — |
| OnScriptEventHappened | method | — | — | — |
| Reset | method | — | — | — |
| SpellHit | method | Object/GetEntry, Object/GetObjectGuid, Unit.Main/HasAura#2 | — | — |
| UpdateAI | method | Creature.Main/GetHomePosition, Creature.MotionMaster/MovePoint, CreatureAI/DoCastSpellIfCan, Map.Main/GetPlayer, ScriptMgr/DoScriptText, Unit.Main/GetMotionMaster, Unit.Main/HandleEmoteState, Unit.Main/HasAura#2, Unit.Main/RemoveAurasDueToSpell, Unit.Main/SetWalk, WorldObject.Object/FindNearestGameObject, WorldObject.Object/GetContactPoint, WorldObject.Object/GetMap | — | — |
| MovementInform | method | CreatureAI/DoCastSpellIfCan, Unit.Main/HandleEmoteState, Unit.Main/SetFacingToObject, WorldObject.Object/FindNearestGameObject | — | — |
| peon_wake_up | function | Object/GetEntry, Object/GetObjectGuid, Object/ToPlayer, Player.Main/KilledMonsterCredit, Unit.Main/HasAura#2 | — | — |
| GetAI_LazyPeon | function | — | — | — |
| AddSC_durotar | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
