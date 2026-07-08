<!-- provenance: verbose -->
# AiBotDoctrineSolo

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AiBotDoctrineSolo

## Purpose & Responsibilities

`AiBotDoctrineSolo` implements the **Solo engagement doctrine** for the AI bot system, encapsulating behavior for bots operating alone or without an active team directive. It is a **stateless thin wrapper** that preserves existing, verified solo behavior by delegating all decisions to `AiBotAI` methods. Its primary responsibilities are routing target acquisition based on task type, enforcing density-based pull vetoes, and enabling shared combat mechanisms (stalemate breaking, overpull retreat, tap respect).

Defined in an anonymous namespace within `AiBotDoctrineSolo.cpp`, the class exposes only the `MakeSoloDoctrine` factory function. This design ensures the unit is purely additive: it compiles alongside the existing AI but alters no behavior until the central doctrine dispatch system activates.

## Member-by-Member Behavior

### Target Acquisition
*   **AcquireTarget**: Routes target selection based on `bot.m_currentTask.type`. If `TASK_MOVE_TO`, it calls `AiBotAI.Grind/ScanApproachTarget` to find valid quest mobs along the approach path. Otherwise, it calls `AiBotAI.Grind/SelectGrindTarget` for standard priority scanning.

### Pull Control
*   **HoldPull**: Delegates to `AiBotAI.Combat/OverpullGuard` to evaluate if a pull should be aborted due to enemy density. Since `OverpullGuard` returns `false` (allowing pull) when grouped, this effectively serves as the solo density cap.

### Combat Maintenance & Flags
*   **MaintainTarget**: Always returns `nullptr`, signaling the AI spine to defer target maintenance to legacy logic (kill credit, visibility checks, reselection). The solo doctrine does not implement independent target switching.
*   **UseStalemateBreaker**: Returns `true`, enabling `HandleCombatStalemate`.
*   **UseOverpullRetreat**: Returns `true`, enabling `HandleOverpullRetreat`.
*   **UseTapRespect**: Returns `true`, enabling logic to drop mobs tapped by other players.

### Identity & Factory
*   **Name**: Returns the string `"Solo"`.
*   **MakeSoloDoctrine**: Free function constructing and returning a `std::unique_ptr<IEngagementDoctrine>` containing a new `AiBotDoctrineSolo` instance. This is the sole external linkage symbol from this translation unit.

## Cross-Unit Boundaries

### Calls Out
*   **AiBotAI.Grind/ScanApproachTarget**: Called by `AcquireTarget` during `TASK_MOVE_TO` to retrieve targets suitable for approaching quest objectives.
*   **AiBotAI.Grind/SelectGrindTarget**: Called by `AcquireTarget` for standard grinding tasks to perform priority scanning.
*   **AiBotAI.Combat/OverpullGuard**: Called by `HoldPull` to check if the candidate target is surrounded by excessive enemies, making a pull unsafe.

### Called By
*   **AiBotDoctrine/MakeDoctrine**: The `MakeSoloDoctrine` function is intended to be called by the central doctrine factory (`MakeDoctrine`) in `AiBotDoctrine.cpp` once the dispatch system is activated. Currently, this connection is dormant as part of the incremental rollout strategy.

## Data Model

This unit does not interact with any database tables. All logic is driven by runtime state (`AiBotAI` members) and configuration flags.

## Notable Implementation Details

1.  **Anonymous Namespace Isolation**: `AiBotDoctrineSolo` is defined in an anonymous namespace, preventing direct instantiation from other translation units. Only `MakeSoloDoctrine` is exposed.
2.  **No Internal State**: The class holds no member variables, relying entirely on the passed `AiBotAI& bot` reference and static configuration flags. This contrasts with doctrines like `TeamAuto` which hold transient state.
3.  **Deferential Design**: `MaintainTarget`'s consistent return of `nullptr` ensures the solo doctrine does not interfere with complex legacy combat logic, maintaining backward compatibility.
4.  **Additive Commit Strategy**: Comments emphasize this file is "purely additive," designed to compile and link without affecting runtime behavior until the higher-level dispatch system invokes it.
5.  **Task-Based Routing**: `AcquireTarget` uses `bot.m_currentTask.type` to distinguish between approach and grind scans, leveraging existing task infrastructure.

## Member Reference

**AcquireTarget**: Routes target acquisition to `AiBotAI.Grind/ScanApproachTarget` if the current task is `TASK_MOVE_TO`, otherwise to `AiBotAI.Grind/SelectGrindTarget`.

**HoldPull**: Delegates to `AiBotAI.Combat/OverpullGuard` to determine if a pull should be held due to enemy density.

**MaintainTarget**: Always returns `nullptr`, deferring target maintenance to legacy AI logic.

**UseStalemateBreaker**: Returns `true` to enable stalemate breaking.

**UseOverpullRetreat**: Returns `true` to enable overpull retreat.

**UseTapRespect**: Returns `true` to enable tap respect (dropping foreign-tapped mobs).

**Name**: Returns the string literal `"Solo"`.

**MakeSoloDoctrine**: Factory function that creates and returns a `std::unique_ptr<IEngagementDoctrine>` wrapping a new `AiBotDoctrineSolo` instance.

---

<!-- machine-true, projected from graph.json -->

## Map — AiBotDoctrineSolo

*Source:* AiBotDoctrineSolo.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AcquireTarget | method | AiBotAI.Grind/ScanApproachTarget, AiBotAI.Grind/SelectGrindTarget | — | — |
| HoldPull | method | AiBotAI.Combat/OverpullGuard | — | — |
| MaintainTarget | method | — | — | — |
| UseStalemateBreaker | method | — | — | — |
| UseOverpullRetreat | method | — | — | — |
| UseTapRespect | method | — | — | — |
| Name | method | — | — | — |
| MakeSoloDoctrine | function | — | AiBotDoctrine/MakeDoctrine | — |
