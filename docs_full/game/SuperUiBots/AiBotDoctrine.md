<!-- provenance: verbose -->
# AiBotDoctrine

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AiBotDoctrine

## Purpose & Responsibilities

`AiBotDoctrine` implements the **engagement doctrine resolution and factory** layer for AI bots. It determines which of three high-level combat strategies (`Solo`, `TeamAuto`, or `Directed`) a bot follows during a behavior tick and instantiates the corresponding `IEngagementDoctrine` object.

The unit enforces strict separation of concerns:
1.  **Resolution**: `ResolveDoctrine` selects the active doctrine based on posture, group status, and combat directives.
2.  **Factory**: `MakeDoctrine` creates the concrete implementation via per-TU hooks, keeping concrete classes file-local.
3.  **Interface**: Defines `IEngagementDoctrine`, standardizing engagement decisions (target acquisition, pull discipline, maintenance) while isolating group-specific logic from solo paths.

Swapping the `std::unique_ptr<IEngagementDoctrine>` on doctrine change automatically resets transient state (e.g., pull-hold counters).

## Member-by-Member Behavior

### Resolution and Factory

**`ResolveDoctrine`**
Selects a `DoctrineKind` using frozen priority logic:
1.  **Posture**: If `ConductPosture` is `Companion` or `Puppet`, returns `Directed`. Currently hardcoded to `Autonomous` (M2 pending), making this branch inactive.
2.  **Group & Directive**: If the bot is grouped (`GetGroup() != nullptr`) and `m_combatDirective.IsActive()` is true, returns `TeamAuto`.
3.  **Default**: Otherwise, returns `Solo`.

**`MakeDoctrine`**
Factory function returning a `std::unique_ptr<IEngagementDoctrine>` for the given `DoctrineKind`. Delegates to `MakeSoloDoctrine`, `MakeTeamDoctrine`, or `MakeDirectedDoctrine`. Defaults to `Solo` for compiler satisfaction on unreachable paths.

### Interface & Enums

**`DoctrineKind`**
Enum defining strategies: `Solo` (ungrouped/no directive), `TeamAuto` (grouped + active directive), `Directed` (companion/puppet posture).

**`ConductPosture`**
Enum for operational stance: `Autonomous` (default), `Companion`, `Puppet`.

**`IEngagementDoctrine`**
Abstract interface for engagement decisions:
-   `AcquireTarget`: Selects a pull target; `nullptr` implies hold/patrol.
-   `HoldPull`: Veto before `AttackStart`.
-   `MaintainTarget`: Insists on a target or defers to spine legacy logic.
-   `UseStalemateBreaker`, `UseOverpullRetreat`, `UseTapRespect`: Opt-in flags for shared machinery.
-   `HoldingForTeam`: Returns `true` if `nullptr` from `AcquireTarget` is a deliberate wait (only `TeamAuto` overrides; defaults to `false`).
-   `Name`: String identifier for logging.

## Cross-Unit Boundaries

### Calls Out
-   **`AiBotAI.Main/GetBotPlayer`**: Called by `ResolveDoctrine` to retrieve the `Player*` for group checks.
-   **`CombatDirective/IsActive`**: Called by `ResolveDoctrine` to check for active assist/tank directives.
-   **`Player.Main/GetGroup`**: Called by `ResolveDoctrine` to verify group membership.
-   **`AiBotDoctrineDirected/MakeDirectedDoctrine`**, **`AiBotDoctrineSolo/MakeSoloDoctrine`**, **`AiBotDoctrineTeam/MakeTeamDoctrine`**: Called by `MakeDoctrine` to instantiate concrete doctrines.

### Called By
-   **`AiBotAI.Main/RefreshDoctrine`**: Calls `ResolveDoctrine` and `MakeDoctrine` every tick to update the active doctrine, swapping the `unique_ptr` if the kind changes.

## Data Model

No database tables are accessed.

## Notable Implementation Details

-   **Hardcoded Posture**: `ResolveDoctrine` hardcodes `ConductPosture::Autonomous`, rendering `Directed` logic dead code until M2 integration.
-   **State Reset**: Doctrine swaps destroy the old `unique_ptr`, resetting transient state (e.g., `TeamAuto`'s pull-hold counter) by construction.
-   **Const-Correctness**: `ResolveDoctrine` takes `const AiBotAI&` but calls `GetBotPlayer()` which returns a non-const `Player*` to inspect mutable group state.
-   **Default Factory Return**: `MakeDoctrine` defaults to `Solo` to satisfy compiler requirements for exhaustive switches.

## Member Reference

**ResolveDoctrine**
Evaluates posture, group status, and combat directives to return a `DoctrineKind`. Hardcoded to `Autonomous` posture, so it currently returns `TeamAuto` if grouped and directive active, else `Solo`.

**MakeDoctrine**
Factory function creating a `std::unique_ptr<IEngagementDoctrine>` for the given `DoctrineKind` by delegating to per-TU hooks (`MakeSoloDoctrine`, `MakeTeamDoctrine`, `MakeDirectedDoctrine`).

---

<!-- machine-true, projected from graph.json -->

## Map — AiBotDoctrine

*Source:* AiBotDoctrine.cpp, AiBotDoctrine.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ResolveDoctrine | function | AiBotAI.Main/GetBotPlayer, CombatDirective/IsActive, Player.Main/GetGroup | AiBotAI.Main/RefreshDoctrine | — |
| MakeDoctrine | function | AiBotDoctrineDirected/MakeDirectedDoctrine, AiBotDoctrineSolo/MakeSoloDoctrine, AiBotDoctrineTeam/MakeTeamDoctrine | AiBotAI.Main/RefreshDoctrine | — |
