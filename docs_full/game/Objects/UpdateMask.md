<!-- provenance: verbose -->
# UpdateMask

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# UpdateMask

## Purpose & Responsibilities

`UpdateMask` is a dynamic bitfield class that tracks which fields of a `WorldObject` have changed and require synchronization with clients. To minimize network traffic, the server sends only "dirty" fields; `UpdateMask` manages the underlying memory for this bitmask, providing methods to set, clear, and query individual bits, as well as logical operators to combine or filter masks. It maintains a `mHasData` flag to allow callers to quickly determine if any updates exist.

## Member-by-Member Behavior

### Construction and Destruction

*   **`UpdateMask()`**: Default constructor. Initializes `mHasData` to `false`, `mCount` and `mBlocks` to `0`, and `mUpdateMask` to `nullptr`. No memory is allocated.
*   **`UpdateMask(const UpdateMask&)`**: Copy constructor. Initializes `mUpdateMask` to `0` and delegates to `operator=` to perform a deep copy.
*   **`~UpdateMask()`**: Destructor. Frees the dynamically allocated `mUpdateMask` array using `delete[]`.

### Bit Manipulation

*   **`SetBit(uint32 index)`**: Sets the bit at `index` to 1. Calculates the byte offset (`index >> 3`) and bit shift (`index & 0x7`) within the `uint32` array. Sets `mHasData` to `true`.
*   **`UnsetBit(uint32 index)`**: Clears the bit at `index` to 0. Does **not** update `mHasData`, even if this was the last set bit.
*   **`GetBit(uint32 index)`**: Returns `true` if the bit at `index` is set.

### State Querying

*   **`GetBlockCount()`**: Returns `mBlocks`, the number of 32-bit integers in the mask.
*   **`GetLength()`**: Returns the mask size in bytes (`mBlocks << 2`).
*   **`GetCount()`**: Returns `mCount`, the total number of fields the mask tracks.
*   **`GetMask()`**: Returns a raw `uint8*` pointer to the underlying data, used for serialization.
*   **`HasData()`**: Returns `mHasData`, indicating if any bits are set.

### Memory Management

*   **`SetCount(uint32 valuesCount)`**: Resizes the mask to track `valuesCount` fields. Deletes the old buffer, calculates new block count (`(valuesCount + 31) / 32`), allocates new memory, and zeroes it. Does **not** reset `mHasData`.
*   **`Clear()`**: Zeros the existing buffer and sets `mHasData` to `false`. Reuses allocated memory.

### Logical Operations

*   **`operator=(const UpdateMask&)`**: Deep copies the source mask. Resizes target via `SetCount` and copies data with `memcpy`.
*   **`operator&=(const UpdateMask&)`**: Bitwise AND assignment. Iterates up to `mBlocks`. **Risk**: Accesses `mask.mUpdateMask[i]` without checking if `mask.mBlocks < mBlocks`, potentially causing out-of-bounds reads if the source mask is smaller.
*   **`operator|=(const UpdateMask&)`**: Bitwise OR assignment. Same out-of-bounds risk as `&=`.
*   **`operator&(const UpdateMask&)`**: Returns a new mask resulting from bitwise AND.
*   **`operator|(const UpdateMask&)`**: Returns a new mask resulting from bitwise OR.

## Cross-Unit Boundaries

`UpdateMask` is a passive data structure driven by callers:

1.  **`WorldObject.Object`**: Primary consumer. Uses `SetBit` to mark changed fields (e.g., health, position) and `GetMask`/`GetBlockCount` to serialize updates in `BuildValuesUpdate` and `DirectSendPublicValueUpdate`. `Clear` resets the mask after sending.
2.  **`Player.Main`**: Uses `UpdateMask` in `SetGameMaster` to mark GM-related field changes.
3.  **`game_Group_Group`**: Uses `UpdateMask` in `AddMember` to track group composition changes.

## Data Model

`UpdateMask` operates entirely in memory. It does not interact with any database tables.

## Notable Implementation Details

1.  **Out-of-Bounds Risk in Logical Operators**: `operator&=` and `operator|=` loop up to `mBlocks` (the target's size) but access `mask.mUpdateMask[i]` (the source). If the source mask has fewer blocks than the target, this reads uninitialized or invalid memory. The commented `MANGOS_ASSERT` suggests this invariant was intended but is not enforced. Callers must ensure source masks are at least as large as targets.
2.  **`mHasData` Inconsistency**: `UnsetBit` does not reset `mHasData` to `false` when clearing the last bit. `SetCount` also preserves `mHasData` across resizes. Callers must explicitly call `Clear()` or manage the flag if accurate emptiness checks are required after unsetting bits or resizing.
3.  **Manual Memory Management**: Uses raw `new[]`/`delete[]`. `SetCount` correctly deletes the old buffer before allocating. The copy constructor initializes `mUpdateMask` to `0` before `operator=` overwrites it, which is slightly redundant but safe.

## Member Reference

**UpdateMask**
Default constructor. Initializes mask to empty state with no memory allocated.

**UpdateMask#2**
Copy constructor. Delegates to `operator=` for deep copy.

**~UpdateMask**
Destructor. Frees `mUpdateMask` array.

**SetBit**
Sets bit at `index`. Updates `mHasData` to true.

**UnsetBit**
Clears bit at `index`. Does not update `mHasData`.

**GetBit**
Returns true if bit at `index` is set.

**GetBlockCount**
Returns number of 32-bit blocks (`mBlocks`).

**GetLength**
Returns mask size in bytes.

**GetCount**
Returns total field count (`mCount`).

**GetMask**
Returns raw `uint8*` pointer to mask data.

**HasData**
Returns `mHasData` flag.

**SetCount**
Resizes mask for `valuesCount` fields. Allocates and zeroes memory. Does not reset `mHasData`.

**Clear**
Zeros buffer and sets `mHasData` to false.

**operator=**
Deep copies source mask.

**operator&=**
Bitwise AND assignment. Risk of out-of-bounds read if source is smaller than target.

**operator|=**
Bitwise OR assignment. Risk of out-of-bounds read if source is smaller than target.

**operator&**
Returns new mask with bitwise AND result.

**operator|**
Returns new mask with bitwise OR result.

---

<!-- machine-true, projected from graph.json -->

## Map — UpdateMask

*Source:* UpdateMask.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| UpdateMask | ctor | — | game_Group_Group/AddMember, Player.Main/SetGameMaster, WorldObject.Object/BuildCreateUpdateBlockForPlayer, WorldObject.Object/BuildValuesUpdateBlockForPlayer#2, WorldObject.Object/BuildValuesUpdateBlockForPlayerWithFlags, WorldObject.Object/DirectSendPublicValueUpdate, WorldObject.Object/DirectSendPublicValueUpdate#3 | — |
| UpdateMask#2 | ctor | — | — | — |
| ~UpdateMask | dtor | — | — | — |
| SetBit | method | — | Player.Main/SetGameMaster, WorldObject.Object/BuildValuesUpdate, WorldObject.Object/DirectSendPublicValueUpdate, WorldObject.Object/DirectSendPublicValueUpdate#3, WorldObject.Object/MarkUpdateFieldsWithFlagForUpdate, WorldObject.Object/_SetCreateBits, WorldObject.Object/_SetUpdateBits | — |
| UnsetBit | method | — | — | — |
| GetBit | method | — | WorldObject.Object/BuildValuesUpdate, WorldObject.Object/DirectSendPublicValueUpdate#2 | — |
| GetBlockCount | method | — | WorldObject.Object/BuildValuesUpdate, WorldObject.Object/DirectSendPublicValueUpdate#2 | — |
| GetLength | method | — | WorldObject.Object/BuildValuesUpdate, WorldObject.Object/DirectSendPublicValueUpdate#2 | — |
| GetCount | method | — | WorldObject.Object/BuildValuesUpdate | — |
| GetMask | method | — | WorldObject.Object/BuildValuesUpdate, WorldObject.Object/DirectSendPublicValueUpdate#2 | — |
| HasData | method | — | game_Group_Group/AddMember, WorldObject.Object/BuildValuesUpdateBlockForPlayer#2, WorldObject.Object/BuildValuesUpdateBlockForPlayerWithFlags | — |
| SetCount | method | — | game_Group_Group/AddMember, Player.Main/SetGameMaster, WorldObject.Object/BuildCreateUpdateBlockForPlayer, WorldObject.Object/BuildValuesUpdateBlockForPlayer#2, WorldObject.Object/BuildValuesUpdateBlockForPlayerWithFlags, WorldObject.Object/DirectSendPublicValueUpdate, WorldObject.Object/DirectSendPublicValueUpdate#3 | — |
| Clear | method | — | — | — |
| operator= | method | — | — | — |
| operator&= | method | — | — | — |
| operator|= | method | — | — | — |
| operator& | method | — | — | — |
| operator| | method | — | — | — |
