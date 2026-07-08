# CombatDirective

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# CombatDirective

**Purpose & Responsibilities**

`CombatDirective` is a lightweight data structure within the `AiBotAI` system that represents the **group focus-fire state** for a single bot member. It acts as the "stamp" applied by an external coordinator (the C# `BotBrainService`) to synchronize combat behavior among grouped bots.

Its primary responsibility is to store two pieces of information:
1.  **Mode**: The type of group combat behavior requested (currently only `COMBAT_MODE_ASSIST`).
2.  **Anchor**: The low-part of the GUID of the "anchor" unit (typically the leader or primary tank) whose current target the bot should assist.

This structure is part of the **[TEAMPLAY]** subsystem. It is designed to be **stateless** from the perspective of the bot's internal logic; the bot does not decide *when* to update this directive. Instead, it receives updates via the TCP bridge (`BridgeHandleCombatDirective`) and exposes its state via `IsActive()` for consumption by the team-play resolution logic (`AiBotAITeamPlay.cpp`).

## Member-by-Member Behavior

### `IsActive`
*   **Kind**: Method (const)
*   **Behavior**: Returns `true` if the `mode` field is not `COMBAT_MODE_NONE`. This indicates that a valid group combat directive is currently active for this bot.
*   **Usage**: This is the primary guard clause used by other units to determine if the bot should defer to group logic or fall back to solo behavior.

### `Clear`
*   **Kind**: Method
*   **Behavior**: Resets the `CombatDirective` to its default state. It sets `mode` to `COMBAT_MODE_NONE` and `anchorGuidLow` to `0`.
*   **Usage**: Called when the coordinator sends a directive indicating no active group focus, or when the bot leaves a group context where such directives are irrelevant.

## Cross-Unit Boundaries

`CombatDirective` itself contains no external dependencies, but its members are heavily integrated with other parts of the `AiBotAI` system and the team-play subsystem.

### Calls Out
*   **None**. `CombatDirective` is a simple data holder with inline logic. It does not call functions in other units.

### Called By
The following units consume the state of `CombatDirective` to make decisions:

1.  **`AiBotAI.Main/RefreshDoctrine`** (`AiBotAIMain.cpp`)
    *   **Direction**: Read
    *   **Why**: During the doctrine refresh phase, the system checks if a combat directive is active. If `IsActive()` is true, it influences which `IEngagementDoctrine` strategy is selected (e.g., switching from `Solo` to `TeamAuto` or `Directed`).

2.  **`AiBotAI.Main/UpdateAI`** (`AiBotAIMain.cpp`)
    *   **Direction**: Read
    *   **Why**: The main AI loop checks the directive to enforce pull discipline. Specifically, it uses the directive to implement the "sticky-assist" and "pull-hold" mechanics, ensuring followers wait for the anchor to engage before pulling their own targets.

3.  **`AiBotDoctrine/ResolveDoctrine`** (`AiBotDoctrine.cpp`)
    *   **Direction**: Read
    *   **Why**: The doctrine resolution layer uses `IsActive()` to determine if the bot is operating under group constraints. This affects how targets are prioritized and whether the bot is allowed to initiate combat independently.

4.  **`AiBotDoctrineTeam/AcquireTarget`** (`AiBotDoctrineTeam.cpp`)
    *   **Direction**: Read
    *   **Why**: When acquiring a target in a group context, this method checks the directive to see if the bot should assist the anchor's current victim. If `IsActive()` is true and the mode is `ASSIST`, it attempts to lock onto the anchor's target.

5.  **`AiBotDoctrineTeam/ResolveFocus`** (`AiBotDoctrineTeam.cpp`)
    *   **Direction**: Read
    *   **Why**: This method resolves the final focus target for the bot. It uses the `anchorGuidLow` from the directive to identify the anchor unit and verify if the bot should be assisting that specific unit's combat actions.

## Data Model

`CombatDirective` does not interact with any database tables. It is a transient, in-memory structure populated exclusively via the TCP bridge from the C# coordinator.

## Notable Implementation Details

1.  **Minimalist Design**: The structure deliberately omits fields for future features (like `focusGuidLow` or `interruptGuidLow`) until those behaviors are implemented. This prevents "speculative dead fields" and keeps the struct clean. Comments in the source explicitly reserve space for these future additions.

2.  **Anchor Identification**: The `anchorGuidLow` stores only the low 32 bits of the GUID. This is sufficient because the high bit (player vs. creature) is typically known from context or can be inferred, and the full GUID can be reconstructed if necessary by searching the world object map. This reduces memory footprint and serialization size.

3.  **Thread Safety**: Since `CombatDirective` is accessed primarily from the main AI thread (via `UpdateAI` and doctrine resolvers) and updated from the bridge thread (via `BridgeHandleCombatDirective`), care must be taken to ensure atomicity or synchronization if accessed concurrently. However, in the current implementation, `BridgeHandleCombatDirective` is called from the main loop's bridge processing section, meaning updates happen sequentially with respect to the AI tick, avoiding race conditions.

4.  **Inactive Default**: The default state is `COMBAT_MODE_NONE`. This ensures that solo bots, or bots not yet assigned a group directive, behave normally without needing special null-checks for the directive object itself. The `IsActive()` method provides a clear boolean interface for this check.

5.  **Integration with Pull Discipline**: The directive is central to the "B3 Pull Discipline" mechanism. Followers with an active assist directive will hold their grind pulls for `AIBOT_ASSIST_PULL_HOLD_TICKS` (5 ticks) to allow the anchor to pull first. This prevents the "arrival fan-out" problem where every bot pulls a different mob simultaneously upon entering a camp.

## Member Reference

**IsActive**
Returns `true` if `mode` is not `COMBAT_MODE_NONE`. Used by `AiBotAI.Main/RefreshDoctrine`, `AiBotAI.Main/UpdateAI`, `AiBotDoctrine/ResolveDoctrine`, `AiBotDoctrineTeam/AcquireTarget`, and `AiBotDoctrineTeam/ResolveFocus` to determine if group combat logic applies.

**Clear**
Resets `mode` to `COMBAT_MODE_NONE` and `anchorGuidLow` to `0`. Called by `AiBotAI.Bridge/BridgeHandleCombatDirective` when the coordinator sends a "none" directive or when the bot leaves a group context.

---

<!-- machine-true, projected from graph.json -->

## Map — CombatDirective

*Source:* AiBotAIMain.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| IsActive | method | — | AiBotAI.Main/RefreshDoctrine, AiBotAI.Main/UpdateAI, AiBotDoctrine/ResolveDoctrine, AiBotDoctrineTeam/AcquireTarget, AiBotDoctrineTeam/ResolveFocus | — |
| Clear | method | — | AiBotAI.Bridge/BridgeHandleCombatDirective | — |
