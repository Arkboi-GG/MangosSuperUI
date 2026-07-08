# PackedGuidReader

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# PackedGuidReader

**Purpose & Responsibilities**

`PackedGuidReader` is a lightweight, stateless helper struct defined in `ObjectGuid.h`. Its sole responsibility is to serve as a type-safe wrapper for an `ObjectGuid` reference during network packet deserialization.

In the World of Warcraft protocol, GUIDs (Globally Unique Identifiers) are often transmitted in a compressed ("packed") format to save bandwidth. The standard `ObjectGuid` class represents the full 64-bit identifier. However, the deserialization process requires reading bits from a `ByteBuffer` and reconstructing the GUID in place. `PackedGuidReader` allows the overloaded extraction operator (`operator>>`) for `ByteBuffer` to accept a specific type that signals "read a packed GUID into this target," distinguishing it from other potential read operations or ensuring the correct unpacking logic is invoked. It holds a pointer to the `ObjectGuid` instance that will receive the reconstructed value.

**Member-by-Member Behavior**

The unit contains only one member: the constructor.

*   **`PackedGuidReader(ObjectGuid& guid)`**: This constructor initializes the internal pointer `m_guidPtr` to point to the provided `ObjectGuid` reference. It is marked `explicit` to prevent accidental implicit conversions. The resulting object is typically created temporarily during a stream extraction operation (e.g., `buf >> reader`).

**Cross-Unit Boundaries**

*   **Called by**: The MAP indicates no external callers. In practice, instances of `PackedGuidReader` are usually created inline within expressions involving `ByteBuffer` extraction operators (defined in `ByteBuffer.cpp` or related I/O units), such as `buf >> obj.ReadAsPacked()`. The `ObjectGuid::ReadAsPacked()` method (in `ObjectGuid.h`) returns this struct, bridging the gap between the `ObjectGuid` interface and the `ByteBuffer` I/O system.
*   **Calls out**: None. The struct itself performs no logic beyond initialization. The actual unpacking logic resides in the `ByteBuffer` extraction operator (`operator>>`), which reads from the `PackedGuidReader`'s `m_guidPtr` to update the target `ObjectGuid`.

**Data Model**

This unit does not interact with any database tables. It operates entirely in memory on network packet data.

**Notable Implementation Details**

*   **Statelessness**: `PackedGuidReader` contains no logic for parsing or validation. It is purely a carrier for the destination address (`m_guidPtr`).
*   **Usage Pattern**: It is designed for use with the `>>` operator overload for `ByteBuffer`. The pattern `buffer >> guid.ReadAsPacked()` creates a temporary `PackedGuidReader`, passes it to the operator, which then uses the stored pointer to write the unpacked GUID back into the original `ObjectGuid` object.
*   **Client Build Awareness**: The `ObjectGuid` class provides two methods for creating this reader: `ReadAsPacked()` and `ReadAsPackedClientBuildAware()`. For client builds greater than 1.8.4, both return a `PackedGuidReader`. For older builds, `ReadAsPackedClientBuildAware()` returns a reference to the `ObjectGuid` itself, indicating that packing/unpacking logic differs by client version. `PackedGuidReader` is the mechanism used for the modern (post-1.8.4) packed format.

## Member Reference

**PackedGuidReader**
Constructor that takes a non-const reference to an `ObjectGuid` and stores its address in the `m_guidPtr` member variable. This enables the `ByteBuffer` extraction operator to identify the target location for the unpacked GUID data.

---

<!-- machine-true, projected from graph.json -->

## Map — PackedGuidReader

*Source:* ObjectGuid.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| PackedGuidReader | ctor | — | — | — |
