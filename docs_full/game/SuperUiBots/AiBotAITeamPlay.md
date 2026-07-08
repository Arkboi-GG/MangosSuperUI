# AiBotAITeamPlay

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AiBotAITeamPlay

**Purpose & Responsibilities**

`AiBotAITeamPlay` is a placeholder translation unit designed to establish a stable architectural boundary for future group-combat positioning logic within the `wowvmangos` bot AI system. Currently, it contains no active behavior. Its sole responsibility is to declare and implement a stub function, `ResolveCombatMove`, which serves as a "seam" for future development. This allows the broader AI system to compile against a defined interface for team-based movement decisions before the actual logic (referred to in comments as the "movement weave") is implemented.

The unit explicitly documents that previous group-combat responsibilities—specifically target selection (`ResolveCombatTarget`) and sticky-assist memory management—have been retired from this namespace and migrated to the `TeamAuto` engagement doctrine in `AiBotDoctrineTeam.cpp`. Consequently, `AiBotAITeamPlay` currently defers all positioning decisions to the bot's default solo behavior.

## Member-by-Member Behavior

### **ResolveCombatMove**

This function is intended to determine optimal combat positioning for a bot participating in a group fight. It accepts a constant reference to the `AiBotAI` instance and output references for X, Y, and Z coordinates.

*   **Current Implementation:** The function is a **v1 STUB**. It immediately discards all input arguments using `(void)` casts to suppress compiler warnings. It returns `false`, indicating it has no opinion on where the bot should move.
*   **Effect:** Because the function returns `false` and leaves the output coordinates unchanged, the calling logic (presumably in the movement module) will fall back to the bot's standard solo positioning algorithm. The bot behaves as if it is fighting alone, ignoring any potential team-based formation requirements.
*   **Future Intent:** As noted in the header and source comments, this function is declared now to ensure the ownership boundary is correct. Future updates will replace the stub with logic for "role positioning" or "move-to-ally" behaviors, allowing bots to maintain specific formations relative to allies or targets during group combat.

## Cross-Unit Boundaries

*   **Calls Out:** None. The current implementation does not invoke any functions from other units.
*   **Called By:** None. According to the provided MAP, no other units currently call `ResolveCombatMove`. This suggests the integration point for this function has not yet been wired into the main AI loop, or the calling logic is conditional on a feature flag not yet enabled.
*   **Dependencies:**
    *   **`AiBotAI`**: The function signature requires `AiBotAI const&`. The `.cpp` file includes `AiBotAI.h` to access this type. However, the current stub does not inspect any members of the `AiBotAI` object.
    *   **`AiBotDoctrineTeam`**: While not called by this unit, the comments extensively reference `AiBotDoctrineTeam.cpp` (and the `TeamAuto` doctrine) as the new owner of group-fight decision-making. This highlights a deliberate separation of concerns: target selection and engagement logic reside in `AiBotDoctrineTeam`, while positioning logic is reserved for `AiBotAITeamPlay` (currently empty).

## Data Model

This unit interacts with **no database tables**. It performs no SQL queries and accesses no persistent storage. All logic is transient and contained within the runtime execution of the AI tick.

## Notable Implementation Details

1.  **Stub Suppression**: The use of `(void)bot; (void)outX; ...` is a deliberate pattern to silence unused parameter warnings in strict compilation environments. This confirms the function is intentionally inert.
2.  **Architectural Stability**: The primary value of this unit is not its code, but its existence. By declaring `ResolveCombatMove` now, the developers avoid breaking changes when the actual positioning logic is added later. Callers can be written against this interface immediately, even if the implementation is a no-op.
3.  **Retired Logic**: The comments clarify that `ResolveCombatTarget` and related sticky-assist mechanics were previously part of this namespace but have been moved. Engineers looking for group-targeting logic should look in `AiBotDoctrineTeam.cpp`, not here.

## Member Reference

**ResolveCombatMove**
A stub function in the `TeamPlay` namespace that currently returns `false` and ignores all inputs. It is designed to eventually provide team-based combat positioning coordinates (X, Y, Z) for an `AiBotAI` instance, but currently defers to solo positioning behavior. No database tables are accessed.

---

<!-- machine-true, projected from graph.json -->

## Map — AiBotAITeamPlay

*Source:* AiBotAITeamPlay.cpp, AiBotAITeamPlay.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ResolveCombatMove | function | — | — | — |
