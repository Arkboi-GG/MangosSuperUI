# UpdatePacket

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# UpdatePacket

## Purpose & Responsibilities

`UpdatePacket` is a lightweight aggregate struct defined in `UpdateData.h` that represents a single contiguous block of serialized update data intended for transmission to a client. It serves as the fundamental payload unit within the broader `UpdateData` system, which manages the aggregation, compression, and sending of object state changes (position, health, flags, etc.) in the WoWVMaNGOS server.

The class itself contains no logic; it is purely a data carrier consisting of:
1.  A `ByteBuffer` (`data`) holding the raw binary update information.
2.  A `uint32` counter (`blockCount`) tracking the number of individual update blocks contained within that buffer.

Its primary responsibility is to hold the result of serializing multiple object updates into a single buffer so that the `UpdateData` class can manage a list of these packets (`std::list<UpdatePacket> m_datas`) for efficient network transmission. The constructor initializes `blockCount` to zero, ensuring a clean state for accumulation.

## Member-by-Member Behavior

### **UpdatePacket** (Constructor)
*   **Kind:** Constructor
*   **Behavior:** Initializes the `blockCount` member variable to `0`. The `data` member (`ByteBuffer`) is default-initialized by the compiler (which typically results in an empty buffer).
*   **Context:** This constructor is called implicitly whenever an `UpdatePacket` instance is created, such as when `UpdateData.AddUpdateBlockAndGetBuffer()` creates a new packet entry in its internal list.

## Cross-Unit Boundaries

According to the provided MAP, `UpdatePacket` has **no outgoing calls** to other units and is **not called by** any other units in the cross-file/cross-class boundary sense defined by the map. 

However, in the broader context of the source file `UpdateData.h`:
*   It is a member type of `UpdateData` (`std::list<UpdatePacket> m_datas`).
*   It is used by `UpdateData.BuildPacket()` methods (defined in `UpdateData.cpp`, not shown here but referenced in the header) to construct the final `WorldPacket` sent to clients.
*   The `ByteBuffer` member relies on the `ByteBuffer` class (included via `"ByteBuffer.h"`).

Since the MAP explicitly lists no cross-unit interactions for `UpdatePacket` itself, we treat it as an isolated data structure in this documentation scope. Its interaction with `UpdateData` is internal to the `UpdateData` class's implementation, not a direct call from/to `UpdatePacket`'s own members.

## Data Model

`UpdatePacket` does not interact with any database tables. It operates entirely in memory, handling transient network serialization data.

## Notable Implementation Details

1.  **Aggregate Structure:** `UpdatePacket` is a simple aggregate with public members. This design allows `UpdateData` to directly access and manipulate the `data` buffer and `blockCount` without needing getter/setter methods, optimizing performance in a hot path (network updates are frequent).
2.  **No Copy/Move Semantics Defined:** The class relies on compiler-generated copy/move constructors and assignment operators. Given that `ByteBuffer` likely manages dynamic memory, copying an `UpdatePacket` involves deep-copying the buffer contents. In high-frequency update scenarios, this could be a performance consideration, though `UpdateData` typically moves or references these packets rather than copying them unnecessarily.
3.  **Initialization Safety:** The constructor explicitly sets `blockCount = 0`. This is critical because `blockCount` is used by the client-side protocol to parse the update stream. An uninitialized count would lead to deserialization errors or crashes on the client side.

## Member Reference

**UpdatePacket**  
Constructor that initializes the `blockCount` member to `0`. The `data` member (`ByteBuffer`) is default-initialized. This prepares the packet to accumulate serialized update blocks.

---

<!-- machine-true, projected from graph.json -->

## Map — UpdatePacket

*Source:* UpdateData.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| UpdatePacket | ctor | — | — | — |
