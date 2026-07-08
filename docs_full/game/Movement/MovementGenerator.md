<!-- provenance: degenerate, verbose -->
# MovementGenerator

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# MovementGenerator

**MovementGenerator** is the abstract base class defining the interface for all movement strategies in the server. It implements the Strategy pattern, allowing `Unit` objects to swap between behaviors (wandering, chasing, fleeing, etc.) at runtime via the `MotionMaster` stack.

This unit provides:
1.  **`MovementGenerator`**: The pure virtual interface with lifecycle callbacks (`Initialize`, `Finalize`, `Interrupt`, `Reset`, `Update`) and utility methods (`IsActive`, `IsReachable`).
2.  **`MovementGeneratorMedium<T, D>`**: A CRTP-style template helper that casts the generic `Unit` reference from `MotionMaster` into a specific type `T` (e.g., `Creature`) and delegates to the concrete implementation `D`.
3.  **Factory structures**: `SelectableMovement` and `MovementGeneratorFactory` for dynamic instantiation by type ID.

## Member-by-Member Behavior

### Lifecycle and State

*   **`~MovementGenerator`**: Virtual destructor. Empty.
*   **`IsActive`**: Concrete method checking if this instance is the active top of the `Unit`'s motion stack. It calls `Unit.Main/GetMotionMaster` to verify the stack is non-empty and `top() == this`. Essential for safety during asynchronous stack modifications.

### Virtual Interface (Callbacks)

Derived classes implement these pure virtual functions. They are invoked by `Creature.MotionMaster` or other callers listed in the MAP.

*   **`Initialize` / `Initialize#2` / `Initialize#3`**: Called before pushing onto the stack. Sets up initial state. `Initialize#3` (in `MovementGeneratorMedium`) casts `Unit` to `T` and delegates to `D::Initialize`.
*   **`Finalize` / `Finalize#2` / `Finalize#3`**: Called after removal from the stack. Cleans up resources. `Finalize#3` delegates to `D::Finalize`.
*   **`Interrupt` / `Interrupt#2` / `Interrupt#3`**: Called when a new generator is pushed above this one. Pauses/saves state. `Interrupt#3` delegates to `D::Interrupt`.
*   **`Reset` / `Reset#2` / `Reset#3`**: Called when this generator returns to the top. Restores state. `Reset#3` delegates to `D::Reset`.
*   **`Update` / `Update#2` / `Update#3`**: Core update loop. Advances movement logic based on `time_diff`. Returns `true` if movement is complete. `Update#3` delegates to `D::Update`.
*   **`UpdateAsync` / `UpdateAsync#2` / `UpdateAsync#3`**: Thread-safe update variant for pathfinding. Must not call AI or modify other units ("trade-safe"). `UpdateAsync#3` delegates to `D::UpdateAsync`.

### Utilities and Queries

*   **`GetMovementGeneratorType`**: Pure virtual. Returns the enum type of this generator.
*   **`UnitSpeedChanged`**: Virtual. Default empty. Called when unit speed changes.
*   **`UpdateFinalDistance`**: Virtual. Default empty. Informs generator of final distance to target.
*   **`IsReachable`**: Virtual. Default `true`. Returns `false` if the destination is unreachable (e.g., pathfinding failure).
*   **`GetResetPosition` / `GetResetPosition#2` / `GetResetPosition#3`**: Virtual. Default `false`. Provides a fallback position for evasion. `GetResetPosition#3` delegates to `D::GetResetPosition`.

### Factory Support

*   **`MovementGeneratorFactory<REAL_MOVEMENT>`**: Constructor for the factory struct. Inherits from `SelectableMovement`. Enables dynamic creation of `REAL_MOVEMENT` instances by type ID.

## Cross-Unit Boundaries

### Calls Out
*   **`Unit.Main/GetMotionMaster`**: Called by `IsActive` to inspect the motion stack.

### Called By
*   **`Creature.MotionMaster`**: Primary consumer. Invokes lifecycle methods (`Initialize`, `Finalize`, `Interrupt`, `Reset`, `Update`) and queries (`GetMovementGeneratorType`, `IsReachable`, `UnitSpeedChanged`, `UpdateFinalDistance`).
*   **`ScriptedEscortAI/UpdateAI`**: Calls `Initialize#2` for escort setup.
*   **`Unit.Main/NearTeleportTo`, `Unit.Main/TeleportPositionRelocation`**: Call `Interrupt#2` and `Reset#2` during teleportation.
*   **`ChatHandler.UnitCommands/HandleMovegensCommand`**: Calls `GetMovementGeneratorType` for admin commands.
*   **`Creature.Main/Update`, `AiBotAI.Combat/CheckForUnreachableTarget`, `BattleBotAI.Main/CheckForUnreachableTarget`, `boss_sapphiron/UpdateReachable`, `Unit.Main/CantPathToVictim`**: Call `IsReachable` to validate pathing.
*   **`HomeMovementGenerator/_setTargetLocation`**: Calls `GetResetPosition` for home coordinates.

## Data Model

No database tables are accessed by this unit.

## Notable Implementation Details

1.  **Unsafe Casting in `MovementGeneratorMedium`**: The template uses `(T*)&u` (reinterpret cast) to convert `Unit` to `T`. This assumes `u` is actually of type `T`. The commented-out `u.AssertIsType<T>()` indicates prior runtime checks were removed for performance, relying on correct usage by `MotionMaster`.
2.  **Thread Safety**: `UpdateAsync` is marked "trade-safe" to allow execution in contexts where AI or unit modification is prohibited (e.g., separate pathfinding threads).
3.  **Stack Validity**: `IsActive` checks `top() == this` to handle cases where the generator is delayed-erased from the stack during updates.

## Member Reference

**~MovementGenerator**: Virtual destructor. Empty.

**IsActive**: Checks if this generator is the top of the `Unit`'s motion stack. Calls `Unit.Main/GetMotionMaster`.

**Initialize#2**: Pure virtual declaration. Called before adding to motion stack.

**Finalize#2**: Pure virtual declaration. Called after removal from motion stack.

**Interrupt#2**: Pure virtual declaration. Called when losing top position.

**Reset#2**: Pure virtual declaration. Called when returning to top position.

**Update#2**: Pure virtual declaration. Main update loop.

**UpdateAsync**: Virtual method. Default empty. Thread-safe pathfinding update.

**GetMovementGeneratorType**: Pure virtual declaration. Returns movement type enum.

**UnitSpeedChanged**: Virtual method. Default empty. Called on speed change.

**UpdateFinalDistance**: Virtual method. Default empty. Called with final distance.

**IsReachable**: Virtual method. Default `true`. Checks pathfindability.

**GetResetPosition**: Virtual method. Default `false`. Provides evasion reset coords.

**Initialize**: Pure virtual function declaration (base class).

**Finalize**: Pure virtual function declaration (base class).

**Interrupt**: Pure virtual function declaration (base class).

**Reset**: Pure virtual function declaration (base class).

**Update**: Pure virtual function declaration (base class).

**UpdateAsync#2**: Virtual function declaration (base class).

**GetResetPosition#2**: Virtual function declaration (base class).

**Initialize#3**: Declaration in `MovementGeneratorMedium`. Casts `Unit` to `T`, delegates to `D::Initialize`.

**Finalize#3**: Declaration in `MovementGeneratorMedium`. Casts `Unit` to `T`, delegates to `D::Finalize`.

**Interrupt#3**: Declaration in `MovementGeneratorMedium`. Casts `Unit` to `T`, delegates to `D::Interrupt`.

**Reset#3**: Declaration in `MovementGeneratorMedium`. Casts `Unit` to `T`, delegates to `D::Reset`.

**Update#3**: Declaration in `MovementGeneratorMedium`. Casts `Unit` to `T`, delegates to `D::Update`.

**UpdateAsync#3**: Declaration in `MovementGeneratorMedium`. Casts `Unit` to `T`, delegates to `D::UpdateAsync`.

**GetResetPosition#3**: Declaration in `MovementGeneratorMedium`. Casts `Unit` to `T`, delegates to `D::GetResetPosition`.

**MovementGeneratorFactory<REAL_MOVEMENT>**: Constructor for factory struct. Inherits from `SelectableMovement`.

---

<!-- machine-true, projected from graph.json -->

## Map — MovementGenerator

*Source:* MovementGenerator.cpp, MovementGenerator.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ~MovementGenerator | dtor | — | — | — |
| IsActive | method | Unit.Main/GetMotionMaster | — | — |
| Initialize#2 | decl | — | Creature.MotionMaster/Initialize, Creature.MotionMaster/InitializeNewDefault, Creature.MotionMaster/Mutate, ScriptedEscortAI/UpdateAI | — |
| Finalize#2 | decl | — | Creature.MotionMaster/ClearType, Creature.MotionMaster/DelayedClean, Creature.MotionMaster/DelayedExpire, Creature.MotionMaster/DirectClean, Creature.MotionMaster/DirectExpire, Creature.MotionMaster/InitializeNewDefault | — |
| Interrupt#2 | decl | — | Creature.MotionMaster/Mutate, Unit.Main/NearTeleportTo, Unit.Main/TeleportPositionRelocation | — |
| Reset#2 | decl | — | Creature.MotionMaster/DirectClean, Creature.MotionMaster/DirectExpire, Creature.MotionMaster/UpdateMotion, Unit.Main/NearTeleportTo, Unit.Main/TeleportPositionRelocation | — |
| Update#2 | decl | — | Creature.MotionMaster/UpdateMotion | — |
| UpdateAsync | method | — | Creature.MotionMaster/UpdateMotionAsync | — |
| GetMovementGeneratorType | decl | — | ChatHandler.UnitCommands/HandleMovegensCommand, Creature.Main/Update, Creature.MotionMaster/ClearType, Creature.MotionMaster/DelayedExpire, Creature.MotionMaster/DirectExpire, Creature.MotionMaster/GetCurrentMovementGeneratorType, Creature.MotionMaster/getLastReachedWaypoint, Creature.MotionMaster/GetUsedMovementGeneratorsList, Creature.MotionMaster/GetWaypointPathInformation, Creature.MotionMaster/Initialize, Creature.MotionMaster/InitializeNewDefault, Creature.MotionMaster/Mutate, Creature.MotionMaster/ReInitializePatrolMovement, Creature.MotionMaster/SetNextWaypoint | — |
| UnitSpeedChanged | method | — | Creature.MotionMaster/PropagateSpeedChange | — |
| UpdateFinalDistance | method | — | Creature.MotionMaster/UpdateFinalDistanceToTarget | — |
| IsReachable | method | — | AiBotAI.Combat/CheckForUnreachableTarget, BattleBotAI.Main/CheckForUnreachableTarget, boss_sapphiron/UpdateReachable, Creature.Main/TryToCast, Creature.Main/Update, Unit.Main/CantPathToVictim | — |
| GetResetPosition | method | — | HomeMovementGenerator/_setTargetLocation | — |
| Initialize | function | — | — | — |
| Finalize | function | — | — | — |
| Interrupt | function | — | — | — |
| Reset | function | — | — | — |
| Update | function | — | — | — |
| UpdateAsync#2 | function | — | — | — |
| GetResetPosition#2 | function | — | — | — |
| Initialize#3 | decl | — | — | — |
| Finalize#3 | decl | — | — | — |
| Interrupt#3 | decl | — | — | — |
| Reset#3 | decl | — | — | — |
| Update#3 | decl | — | — | — |
| UpdateAsync#3 | function | — | — | — |
| GetResetPosition#3 | function | — | — | — |
| MovementGeneratorFactory<REAL_MOVEMENT> | ctor | — | — | — |
