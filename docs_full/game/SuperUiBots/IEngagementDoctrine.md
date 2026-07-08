# IEngagementDoctrine

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# IEngagementDoctrine

## Purpose & Responsibilities

`IEngagementDoctrine` is the abstract interface defining the **engagement decision layer** for `AiBotAI`. It isolates target acquisition, pull discipline, and in-combat maintenance logic from the AI spine, ensuring that distinct combat postures (**Solo**, **TeamAuto**, **Directed**) do not leak behavior into one another.

Concrete implementations reside in separate translation units (`AiBotDoctrineSolo.cpp`, etc.) and are instantiated via factory functions declared in this header. The interface owns transient engagement state (e.g., pull-hold counters), which resets automatically upon doctrine swap.

## Member-by-Member Behavior

### Target Acquisition & Discipline
*   **`AcquireTarget`**: Pure virtual. Determines the initial target for a non-engaged bot. Returns `Unit*` or `nullptr` (hold/patrol). Called by `AiBotAI.Main/UpdateAI`.
*   **`HoldPull`**: Pure virtual. Final veto before `AttackStart`. Returns `true` to delay the pull (e.g., waiting for an anchor). Called by `AiBotAI.Main/UpdateAI`.

### Combat Maintenance
*   **`MaintainTarget`**: Pure virtual. Dictates target persistence during combat. Returns a `Unit*` to force the spine to commit to that target (bypassing legacy reselection), or `nullptr` to defer to standard spine logic (kill-credit, visibility checks). Called by `AiBotAI.Main/UpdateAI`.

### Mechanism Opt-ins
*   **`UseStalemateBreaker`**, **`UseOverpullRetreat`**, **`UseTapRespect`**: Pure virtual. Boolean flags enabling shared spine mechanics. The spine checks these before executing respective handlers.

### State Signaling & Observability
*   **`HoldingForTeam`**: Virtual (default `false`). Signals if `AcquireTarget` returning `nullptr` is a deliberate tactical hold (preventing freeze/`GRIND_BLOCKED` bookkeeping) rather than a lack of targets. Only `TeamAuto` overrides this. Called by `AiBotAI.Main/UpdateAI`.
*   **`Name`**: Pure virtual. Returns a string identifier for logging/state echo. Called by `AiBotAI.Main/RefreshDoctrine` and `AiBotAI.Main/UpdateAI`.

## Cross-Unit Boundaries

*   **`AiBotAI.Main/UpdateAI`**: Primary consumer. Calls `AcquireTarget`, `HoldPull`, `MaintainTarget`, `HoldingForTeam`, and the `Use*` opt-ins to drive engagement logic.
*   **`AiBotAI.Main/RefreshDoctrine`**: Calls `Name` for observability.
*   **Factory Functions**: `MakeSoloDoctrine`, `MakeTeamDoctrine`, and `MakeDirectedDoctrine` are declared here but defined in their respective `.cpp` files. `MakeDoctrine` (in `AiBotDoctrine.cpp`) dispatches to them.

## Data Model

This unit does not interact with any database tables.

## Notable Implementation Details

1.  **Transient State Reset**: Transient state lives on the concrete doctrine instance. Swapping doctrines destroys the old instance and creates a new one, automatically resetting state like pull-hold counters.
2.  **Default `HoldingForTeam`**: `HoldingForTeam` has a default implementation returning `false`. `Solo` and `Directed` inherit this; only `TeamAuto` overrides it to signal deliberate waits.
3.  **Dead `Directed` Branch**: Until the M2 conduct substrate is complete, `Directed` is present-but-dead. `ResolveDoctrine` currently only selects between `Solo` and `TeamAuto`.

## Member Reference

*   **`~IEngagementDoctrine`**: Virtual destructor. Defaulted.
*   **`AcquireTarget`**: Pure virtual. Returns initial target (`Unit*`) or `nullptr`. Called by `AiBotAI.Main/UpdateAI`.
*   **`HoldPull`**: Pure virtual. Veto gate for attack initiation. Returns `bool`. Called by `AiBotAI.Main/UpdateAI`.
*   **`MaintainTarget`**: Pure virtual. Forces target commitment (`Unit*`) or defers to spine (`nullptr`). Called by `AiBotAI.Main/UpdateAI`.
*   **`UseStalemateBreaker`**: Pure virtual. Opt-in for stalemate breaking. Returns `bool`.
*   **`UseOverpullRetreat`**: Pure virtual. Opt-in for overpull retreat. Returns `bool`.
*   **`UseTapRespect`**: Pure virtual. Opt-in for tap respect. Returns `bool`.
*   **`HoldingForTeam`**: Virtual (default `false`). Signals deliberate team hold. Called by `AiBotAI.Main/UpdateAI`.
*   **`Name`**: Pure virtual. Returns doctrine identifier string. Called by `AiBotAI.Main/RefreshDoctrine` and `AiBotAI.Main/UpdateAI`.

---

<!-- machine-true, projected from graph.json -->

## Map — IEngagementDoctrine

*Source:* AiBotDoctrine.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ~IEngagementDoctrine | dtor | — | — | — |
| AcquireTarget | decl | — | AiBotAI.Main/UpdateAI | — |
| HoldPull | decl | — | AiBotAI.Main/UpdateAI | — |
| MaintainTarget | decl | — | AiBotAI.Main/UpdateAI | — |
| UseStalemateBreaker | decl | — | — | — |
| UseOverpullRetreat | decl | — | — | — |
| UseTapRespect | decl | — | — | — |
| HoldingForTeam | method | — | AiBotAI.Main/UpdateAI | — |
| Name | decl | — | AiBotAI.Main/RefreshDoctrine, AiBotAI.Main/UpdateAI | — |
