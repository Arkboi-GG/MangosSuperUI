# AiBotDoctrineDirected

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`AiBotDoctrineDirected` implements the **Directed** engagement doctrine for AI bots, representing a "companion" or "puppet" posture where the bot acts strictly under external authority (a real player or RTS user). Its primary responsibility is to **suppress all autonomous decision-making** regarding combat targets and self-preservation.

In this mode, the bot does not select its own enemies, does not evaluate whether to hold or release a pull, and does not attempt to flee or break stalemates. Instead, it relies entirely on direct bridge commands (such as `ATTACK_TARGET`, `MOVE_TO`) executed by the bot's spine (`AiBotAIMain`). The doctrine ensures that the bot remains passive in its tactical choices, allowing the external controller to dictate behavior without interference from the bot's internal autonomous logic.

This unit is part of a three-state split in the bot's conduct system. While the bot defaults to `AUTONOMOUS` conduct initially, the `Directed` doctrine is constructed via the factory `MakeDirectedDoctrine()` to ensure the factory is total (handling all `DoctrineKind` variants), even if the `COMPANION` or `PUPPET` conduct states required to activate it are not yet fully routed in the current version (v1).

## Member-by-Member Behavior

The members of `AiBotDoctrineDirected` are grouped by their role in enforcing passivity.

### Target Acquisition and Maintenance
These methods ensure the bot never independently chooses or adjusts its combat focus.

*   **AcquireTarget**: Returns `nullptr`. This explicitly prevents the bot from autonomously selecting a new target. Any target assignment must come from an external bridge command (e.g., `ATTACK_TARGET`), which sets the victim directly in the spine, bypassing this doctrine's acquisition logic.
*   **MaintainTarget**: Returns `nullptr`. This defers target maintenance to the spine's existing victim handling. In v1, the bot simply continues attacking whatever victim was previously pinned by an order. Future versions (M7) may implement "assist the player's target" logic here, but currently, it performs no autonomous retargeting or group-state analysis.

### Pull Control
*   **HoldPull**: Returns `false`. This indicates that the bot will never veto a pull. Since the bot does not initiate pulls autonomously, there is no "unordered pull" to veto. Ordered pulls (from external commands) are never vetoed by this doctrine.

### Self-Preservation and Safety
These methods disable all autonomous survival mechanisms, ensuring the bot does not act against the controller's wishes (e.g., fleeing when the controller wants to tank).

*   **UseStalemateBreaker**: Returns `false`. Disables automatic teleportation or other actions designed to break combat stalemates.
*   **UseOverpullRetreat**: Returns `false`. Disables automatic retreat when the bot detects it is overwhelmed ("overpulled").
*   **UseTapRespect**: Returns `false`. Disables logic that would respect loot or damage taps from other players. In the `PUPPET` context, this is intentionally off.

### Identity and Construction
*   **Name**: Returns the string `"Directed"`, identifying the doctrine type for logging or debugging purposes.
*   **MakeDirectedDoctrine**: A free function in the anonymous namespace that constructs and returns a `std::unique_ptr<IEngagementDoctrine>` containing a new `AiBotDoctrineDirected` instance. This serves as the factory hook for the doctrine resolution system.

## Cross-Unit Boundaries

*   **Called by `AiBotDoctrine/MakeDoctrine`**: The function `MakeDirectedDoctrine` is invoked by the `MakeDoctrine` function in `AiBotDoctrine.cpp`. This integration allows the central doctrine factory to instantiate the `Directed` doctrine when the bot's conduct posture requires it (specifically `COMPANION` or `PUPPET`).
*   **Calls out**: None. This unit is purely passive and does not invoke methods in other units. It relies on the caller (the spine/AI main loop) to handle the actual execution of commands like setting victims or moving.

## Data Model

This unit does not interact with any database tables. It operates entirely in memory using the bot's current state and configuration.

## Notable Implementation Details

*   **Passive Design**: The entire class is implemented within an anonymous namespace, exposing only the factory function `MakeDirectedDoctrine`. This encapsulates the implementation details while providing a clean interface for the factory.
*   **V1 Skeleton**: The code comments explicitly state that this is a v1 skeleton. Key features like "assist the player's target" in `MaintainTarget` are deferred to future milestones (M7). Currently, `MaintainTarget` does nothing more than return `nullptr`, relying on the spine's persistent victim pointer.
*   **No Autonomous Pull Path**: The design ensures that no autonomous pull path exists. The bot only attacks what is explicitly ordered. This is critical for the `PUPPET` mode, where the bot must not interfere with the player's intended actions.
*   **Factory Completeness**: The existence of this unit ensures that the `MakeDoctrine` factory is "total," meaning it can handle any `DoctrineKind` enum value, even if some kinds (like `Directed`) are not yet actively routed by the conduct resolver in the current build.

## Member Reference

*   **AcquireTarget**: Method that returns `nullptr`, preventing autonomous target selection.
*   **HoldPull**: Method that returns `false`, indicating no veto on pulls (as none are autonomous).
*   **MaintainTarget**: Method that returns `nullptr`, deferring target maintenance to the spine's victim handling.
*   **UseStalemateBreaker**: Method that returns `false`, disabling autonomous stalemate-breaking actions.
*   **UseOverpullRetreat**: Method that returns `false`, disabling autonomous retreat when overwhelmed.
*   **UseTapRespect**: Method that returns `false`, disabling tap-respect logic.
*   **Name**: Method that returns the string `"Directed"`.
*   **MakeDirectedDoctrine**: Function that creates and returns a `std::unique_ptr<IEngagementDoctrine>` for the `AiBotDoctrineDirected` class, serving as the factory hook.

---

<!-- machine-true, projected from graph.json -->

## Map — AiBotDoctrineDirected

*Source:* AiBotDoctrineDirected.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AcquireTarget | method | — | — | — |
| HoldPull | method | — | — | — |
| MaintainTarget | method | — | — | — |
| UseStalemateBreaker | method | — | — | — |
| UseOverpullRetreat | method | — | — | — |
| UseTapRespect | method | — | — | — |
| Name | method | — | — | — |
| MakeDirectedDoctrine | function | — | AiBotDoctrine/MakeDoctrine | — |
