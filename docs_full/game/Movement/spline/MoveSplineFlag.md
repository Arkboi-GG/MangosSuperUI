<!-- provenance: verbose -->
# MoveSplineFlag

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# MoveSplineFlag

`MoveSplineFlag` is a 32-bit packed structure representing movement state flags for the World of Warcraft client-server protocol. It bridges internal `MoveSpline` logic and `packet_builder` serialization by providing both bitwise and bitfield views of the same memory.

The class uses `#pragma pack(1)` to eliminate padding, allowing direct casting to `uint32` via `raw()` for network transmission. It defines an `eFlags` enumeration mapping specific bits to movement behaviors (e.g., `Flying`, `Runmode`, `Falling`) and masks (e.g., `Mask_Final_Facing`). Many bits are marked `Unknown` to preserve protocol compatibility.

Key responsibilities:
*   **Serialization:** Providing raw `uint32` access for packet construction.
*   **State Querying:** Checking specific modes like smooth interpolation (`isSmooth`) or facing requirements (`isFacing`).
*   **Facing Configuration:** Ensuring mutually exclusive facing modes (`Final_Point`, `Final_Target`, `Final_Angle`) via `EnableFacing...` methods, which clear the `Mask_Final_Facing` mask before setting the new mode.

### Cross-Unit Collaboration

*   **`packet_builder`**: Serializes flags into movement packets.
    *   `WriteCreate`, `WriteCommonMonsterMovePart`, and `WriteMonsterMove` call `raw()` or `operator&` to extract flags.
    *   The `Mask_No_Monster_Move` mask excludes facing and `Done` flags from certain packets to prevent client errors.
*   **`MoveSpline`**: Uses flags to drive movement logic.
    *   `init_spline` calls `isSmooth()` to select interpolation algorithms.
    *   `ComputePosition#2` calls `isFacing()` for orientation calculations.
    *   `_checkPathBounds` uses `operator&` for validation.
*   **`MoveSplineInit`**: Configures flags before movement.
    *   `Launch` invokes the `MoveSplineFlag#3` constructor to initialize from a `uint32`.
    *   `SetFacing#2` and `SetFacingGUID` call `EnableFacingAngle` and `EnableFacingTarget`.
*   **`game_Movement_spline_util`**: Calls `raw#2` (const `raw()`) for debugging output in `ToString`.

### Data Model

This unit does not interact with any database tables.

### Notable Implementation Details

*   **Packed Layout:** `#pragma pack(1)` is critical. Without it, compiler padding would break the `uint32` cast in `raw()`, corrupting network packets.
*   **Facing Mutual Exclusivity:** `EnableFacing...` methods clear `Mask_Final_Facing` before setting the target bit, preventing ambiguous client behavior.
*   **Unknown Flags:** Bits labeled `UnknownX` are preserved to maintain protocol synchronization. Altering them may desynchronize client movement.

## Member Reference

**raw**  
Returns a non-const reference to the internal `uint32`. Called by `packet_builder/WriteCreate`.

**raw#2**  
Returns a const reference to the internal `uint32`. Called by `game_Movement_spline_util/ToString`.

**MoveSplineFlag**  
Default constructor. Initializes flags to 0.

**MoveSplineFlag#3**  
Constructor taking a `uint32`. Called by `MoveSplineInit/Launch`.

**MoveSplineFlag#2**  
Copy constructor. Copies raw value from another instance. Called by `packet_builder/WriteCommonMonsterMovePart`, `WriteCreate`, and `WriteMonsterMove`.

**isSmooth**  
Checks if `Flying` (Catmull-Rom) flag is set. Called by `MoveSpline/init_spline`.

**isFacing**  
Checks if any `Mask_Final_Facing` bits are set. Called by `MoveSpline/ComputePosition#2`.

**hasAllFlags**  
Returns true if all bits in argument `f` are set.

**operator&**  
Bitwise AND with `uint32`. Called by `MoveSpline/_checkPathBounds` and `packet_builder` functions.

**operator|**  
Bitwise OR with `uint32`.

**operator&=**  
Bitwise AND assignment.

**operator|=**  
Bitwise OR assignment.

**EnableFacingPoint**  
Sets `Final_Point`, clears other facing flags.

**EnableFacingAngle**  
Sets `Final_Angle`, clears other facing flags. Called by `MoveSplineInit/SetFacing#2`.

**EnableFacingTarget**  
Sets `Final_Target`, clears other facing flags. Called by `MoveSplineInit/SetFacingGUID`.

---

<!-- machine-true, projected from graph.json -->

## Map — MoveSplineFlag

*Source:* MoveSplineFlag.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| raw | method | — | packet_builder/WriteCreate | — |
| raw#2 | method | — | game_Movement_spline_util/ToString | — |
| MoveSplineFlag | ctor | — | — | — |
| MoveSplineFlag#3 | ctor | — | MoveSplineInit/Launch | — |
| MoveSplineFlag#2 | ctor | — | packet_builder/WriteCommonMonsterMovePart, packet_builder/WriteCreate, packet_builder/WriteMonsterMove | — |
| isSmooth | method | — | MoveSpline/init_spline | — |
| isFacing | method | — | MoveSpline/ComputePosition#2 | — |
| hasAllFlags | method | — | — | — |
| operator& | method | — | MoveSpline/_checkPathBounds, packet_builder/WriteCommonMonsterMovePart, packet_builder/WriteMonsterMove | — |
| operator| | method | — | — | — |
| operator&= | method | — | — | — |
| operator|= | method | — | — | — |
| EnableFacingPoint | method | — | — | — |
| EnableFacingAngle | method | — | MoveSplineInit/SetFacing#2 | — |
| EnableFacingTarget | method | — | MoveSplineInit/SetFacingGUID | — |
