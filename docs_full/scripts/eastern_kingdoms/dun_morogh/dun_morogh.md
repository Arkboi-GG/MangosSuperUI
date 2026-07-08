# dun_morogh

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# dun_morogh

**Purpose & Responsibilities**

The `dun_morogh` translation unit implements the scripted behavior for a specific Non-Player Character (NPC) located in the zone Dun Morogh: **Narm Faulk**. This NPC serves as a quest objective or interactive element that begins in a dead state and must be healed by a player using a specific spell to become active. Once healed, the NPC remains active for a limited duration before resetting or evading. The unit provides the AI logic (`npc_narm_faulkAI`) and the registration mechanism (`AddSC_dun_morogh`) required to integrate this behavior into the server's script system.

**Member-by-Member Behavior**

The unit is organized around the `npc_narm_faulkAI` class, which inherits from `ScriptedAI`. It manages the lifecycle of the Narm Faulk creature through several key methods:

*   **Initialization and State Management**: The constructor initializes the AI and immediately calls `Reset` to establish the initial "dead" state. The `Reset` method configures the creature to appear dead by setting dynamic flags and stand state, initializes a timer (`lifeTimer`) to 120 seconds (120,000 ms), and resets the `spellHit` flag.
*   **Interaction Handling**: The `SpellHit` method is the core interaction point. It checks if the incoming spell matches ID 8593 (likely a healing spell) and if the NPC hasn't already been healed (`!spellHit`). If conditions are met, it changes the creature's state to standing, removes the "dead" dynamic flag, triggers a dialogue line (`SAY_HEAL`), and sets the `spellHit` flag to prevent re-triggering.
*   **Lifecycle Updates**: The `UpdateAI` method runs periodically. It only performs actions if the creature is currently standing (i.e., has been healed). It decrements the `lifeTimer` by the time difference (`diff`). If the timer expires, it triggers `EnterEvadeMode`, effectively ending the interaction or despawning the active state.
*   **Registration**: The global functions `GetAI_npc_narm_faulk` and `AddSC_dun_morogh` handle the integration with the server's script manager. `AddSC_dun_morogh` creates a `Script` object, assigns the name "npc_narm_faulk", links the AI getter, and registers it. This function is called by the central script loader.

**Cross-Unit Boundaries**

*   **`npc_narm_faulkAI` (ctor)**: Calls into `ScriptedAI` (from `ScriptedAI.h/cpp`) to initialize the base AI class.
*   **`Reset`**: Calls `Unit::SetStandState` and `WorldObject::SetUInt32Value` (from `Unit.h/cpp` and `WorldObject.h/cpp`) to visually and logically set the creature to a dead state.
*   **`UpdateAI`**: Calls `Creature::AI()` (from `Creature.h/cpp`) to access the AI interface, `CreatureAI::EnterEvadeMode` (from `CreatureAI.h/cpp`) to trigger evasion logic when the timer expires, and `Unit::IsStandingUp` (from `Unit.h/cpp`) to check the current posture.
*   **`SpellHit`**: Calls `Object::ToUnit` (from `Object.h/cpp`) to cast the hitter to a Unit pointer, `ScriptMgr::DoScriptText` (from `ScriptMgr.h/cpp`) to broadcast the dialogue, `Unit::SetStandState` and `WorldObject::SetUInt32Value` to update the visual state to standing.
*   **`AddSC_dun_morogh`**: Calls `Script::RegisterSelf` (from `Script.h/cpp`) and is called by `ScriptLoader::AddScripts` (from `ScriptLoader.cpp`) to ensure this script is loaded at server startup.

**Data Model**

This unit does not interact with any database tables. All state is managed in-memory via the AI class members (`lifeTimer`, `spellHit`) and the creature's runtime properties.

**Notable Implementation Details**

*   **Hardcoded Spell ID**: The healing interaction is tied strictly to Spell ID 8593. Any change to this spell in the game data would require a code change.
*   **Timer Logic**: The `lifeTimer` starts at 120,000 milliseconds (2 minutes). The check `if (lifeTimer < diff)` is slightly unusual; typically, timers are checked with `<=` or subtracted first then checked for `< 0`. However, since `diff` is usually small (e.g., 1000ms), this effectively means the timer expires after roughly 120 seconds. If `diff` is larger than the remaining timer, it triggers evade.
*   **Commented Code**: There is a commented-out line `//m_creature->RemoveAllAuras();` in `SpellHit`. This suggests that originally, all auras were removed upon healing, but this was disabled, possibly because it interfered with the healing spell's effects or other buffs.
*   **Empty Override**: `MoveInLineOfSight` is overridden but empty. This prevents any default behavior associated with entering line of sight, ensuring the NPC remains passive until healed.

## Member Reference

**npc_narm_faulkAI** (ctor): Initializes the AI instance by calling the base `ScriptedAI` constructor and immediately invoking `Reset` to set the initial dead state.

**Reset**: Sets `lifeTimer` to 120,000 ms, marks the creature as dead via `UNIT_DYNAMIC_FLAGS` and `UNIT_STAND_STATE_DEAD`, and resets `spellHit` to false.

**MoveInLineOfSight**: An empty override that suppresses any default line-of-sight behavior for this creature.

**UpdateAI**: Checks if the creature is standing. If so, it decrements `lifeTimer` by `diff`. If `lifeTimer` drops below `diff`, it calls `EnterEvadeMode` to end the active state.

**SpellHit**: Triggers when the creature is hit by a spell. If the spell ID is 8593 and `spellHit` is false, it sets the creature to standing, clears dead flags, plays dialogue `SAY_HEAL`, and sets `spellHit` to true.

**GetAI_npc_narm_faulk**: A factory function that returns a new `npc_narm_faulkAI` instance for the given `Creature`.

**AddSC_dun_morogh**: Registers the "npc_narm_faulk" script with the `ScriptMgr` by creating a `Script` object, assigning the AI getter, and calling `RegisterSelf`.

---

<!-- machine-true, projected from graph.json -->

## Map — dun_morogh

*Source:* dun_morogh.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| npc_narm_faulkAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | method | Unit.Main/SetStandState, WorldObject.Object/SetUInt32Value | — | — |
| MoveInLineOfSight | method | — | — | — |
| UpdateAI | method | Creature.Main/AI, CreatureAI/EnterEvadeMode, Unit.Main/IsStandingUp | — | — |
| SpellHit | method | Object/ToUnit, ScriptMgr/DoScriptText, Unit.Main/SetStandState, WorldObject.Object/SetUInt32Value | — | — |
| GetAI_npc_narm_faulk | function | — | — | — |
| AddSC_dun_morogh | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
