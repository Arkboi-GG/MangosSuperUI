<!-- provenance: verbose -->
# DialogueHelper

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`DialogueHelper` is a utility class that manages sequential, timed dialogue sequences for scripted encounters. It acts as a state machine iterating through static arrays of `SIDialogueEntry` (single-sided) or `SIDialogueEntryTwoSide` (branching) structures. Its primary responsibilities are enforcing delays between dialogue lines and dispatching text broadcasts via specific NPC speakers.

The class supports two operational modes:
1.  **Instance-Integrated:** When initialized with a `ScriptedInstance`, it leverages the instance’s NPC storage to resolve speakers and optionally simulates text if the speaker is not physically present.
2.  **Standalone:** When used without an instance, subclasses must override `GetSpeakerByEntry` to provide speaker resolution.

## Member-by-Member Behavior

### Initialization and Configuration

**`InitializeDialogueHelper`**
Binds the helper to a `ScriptedInstance` object. It stores the instance pointer (`m_pInstance`) and sets the `m_bCanSimulate` flag. If simulation is enabled, the helper can broadcast text even if the speaker NPC is missing from the instance’s storage maps, ensuring dialogue continuity during complex phases.

**`SetDialogueSide`**
Configures the dialogue variant for two-sided sequences (`SIDialogueEntryTwoSide`). Passing `true` selects the primary text/speaker pairs; `false` selects the alternate pairs. This allows a single dialogue definition to support branching narratives (e.g., faction-specific lines).

### Hooks and Resolution

**`JustDidDialogueStep`**
A protected virtual hook invoked after a dialogue line is processed. The base implementation is empty. Subclasses or instance scripts override this to perform side effects, such as updating encounter states or triggering events, based on the text entry ID of the line just spoken.

**`GetSpeakerByEntry`**
A protected virtual method intended to resolve an NPC entry ID to a live `Creature` pointer. The base implementation returns `nullptr`. In instance-integrated mode, this method is typically bypassed by internal logic that queries the `ScriptedInstance`’s storage directly. It remains available for standalone usage where the caller must implement custom speaker resolution.

## Cross-Unit Boundaries

### Collaboration with `ScriptedInstance`

*   **Called by:** `ScriptedInstance/DoNextDialogueStep` calls `JustDidDialogueStep` and `GetSpeakerByEntry`.
    *   **Context:** Although `DoNextDialogueStep` is a private member of `DialogueHelper`, the MAP identifies `ScriptedInstance/DoNextDialogueStep` as the caller. This reflects the tight coupling where the instance script drives the dialogue flow. The interaction allows the instance to trigger hooks (`JustDidDialogueStep`) and resolve speakers (`GetSpeakerByEntry`) as part of the dialogue progression logic.
    *   **Data Flow:** The `DialogueHelper` uses the `ScriptedInstance` pointer (set via `InitializeDialogueHelper`) to access NPC storage and simulation capabilities.

### Collaboration with `instance_temple_of_ahnqiraj`

*   **Called by:** `instance_temple_of_ahnqiraj/Initialize` calls `InitializeDialogueHelper`.
    *   **Purpose:** Binds the dialogue helper to the Temple of Ahn'Qiraj instance data, enabling it to use the instance’s specific NPC storage and text simulation features.

## Data Model

`DialogueHelper` does not interact with any database tables. It operates entirely in memory using static arrays defined in source code and runtime instance data.

## Notable Implementation Details

1.  **Sentinel-Terminated Arrays:** Constructors require input arrays to be terminated by a sentinel value (`{0,0,0}` for single-side, `{0,0,0,0,0}` for two-side). Missing terminators cause undefined behavior.
2.  **Simulation Fallback:** The `m_bCanSimulate` flag allows text broadcasting even if `GetSingleCreatureFromStorage` fails, critical for NPCs that despawn or are not loaded into standard storage.
3.  **Virtual Method Bypass:** When `InitializeDialogueHelper` is used, `GetSpeakerByEntry` is often bypassed by internal instance-aware logic, making the virtual hook primarily useful for non-instance contexts.

## Member Reference

**InitializeDialogueHelper**
Initializes the helper with a `ScriptedInstance` pointer and a simulation flag, binding it to the instance’s NPC storage and text broadcasting capabilities.

**SetDialogueSide**
Selects the dialogue variant for two-sided sequences: `true` for primary pairs, `false` for alternate pairs.

**JustDidDialogueStep**
Protected virtual hook called after each dialogue line is processed; defaults to no-op, intended for subclass overrides to trigger side effects.

**GetSpeakerByEntry**
Protected virtual method to resolve an NPC entry to a `Creature` pointer; returns `nullptr` by default, overridden or bypassed in instance-integrated mode.

---

<!-- machine-true, projected from graph.json -->

## Map — DialogueHelper

*Source:* ScriptedInstance.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| InitializeDialogueHelper | method | — | instance_temple_of_ahnqiraj/Initialize | — |
| SetDialogueSide | method | — | — | — |
| JustDidDialogueStep | method | — | ScriptedInstance/DoNextDialogueStep | — |
| GetSpeakerByEntry | method | — | ScriptedInstance/DoNextDialogueStep | — |
