# Who

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Unit: `WorldPackets::Misc::Who`

**File:** `Misc.h`  
**Namespace:** `WorldPackets::Misc`

## Purpose & Responsibilities

The `Who` class represents the **`CMSG_WHO`** client packet. Its purpose is to deserialize the binary data sent by the game client when a player initiates a character search (typically via the "Who" list interface). The class acts as a structured data container, extracting search criteria such as level ranges, name fragments, guild names, race/class filters, and zone identifiers from the raw network stream. It does not perform the search logic or interact with the database; it solely prepares the parameters for downstream processing by the server's character lookup subsystem.

## Member-by-Member Behavior

The unit consists of a single constructor. The deserialization logic is implied by the class structure and the inheritance from `ClientPacket`, though the implementation of `ReadFromWorldPacket` is not part of this unit's definition.

### Construction
**`Who`**
The constructor initializes the `ClientPacket` base class with the opcode `CMSG_WHO`. It relies on in-class member initializers to set default values for all fields:
- `levelMin` and `levelMax` are set to `0`.
- `playerName` and `guildName` are initialized as empty `std::string` objects.
- `raceMask` and `classMask` are set to `0`.
- `zoneIds` and `searchTerms` are initialized as empty `std::vector` containers.

This ensures that the object is in a valid, predictable state immediately upon creation, preventing undefined behavior if the client sends a truncated or malformed packet.

## Cross-Unit Boundaries

*   **Calls Out:** None. The `Who` class does not invoke methods in other units.
*   **Called By:** The MAP indicates no external callers. In the broader system, instances of this class are created and populated by the packet dispatching infrastructure (e.g., `WorldSession`) when a `CMSG_WHO` opcode is received. The populated object is then passed to the character search handler.

## Data Model

This unit does not interact with any database tables. It operates exclusively on in-memory data received from the client.

## Notable Implementation Details

*   **Bitmask Filters:** The `raceMask` and `classMask` fields are `uint32` integers, indicating that the client transmits bitwise flags for race and class filtering. The consuming logic must interpret these bits to apply the correct filters.
*   **Variable-Length Lists:** The `zoneIds` and `searchTerms` fields are `std::vector` types, allowing the client to send multiple zone IDs or search keywords. This supports complex queries where a user might specify multiple regions or name fragments.
*   **Default Safety:** Explicit initialization of all members ensures that missing data in the packet stream results in safe defaults (zeros/empty collections) rather than garbage values.

## Member Reference

**Who**
Constructor for the `Who` packet. Initializes the base `ClientPacket` with opcode `CMSG_WHO` and sets all member variables to their default values (zeros, empty strings, empty vectors).

---

<!-- machine-true, projected from graph.json -->

## Map — Who

*Source:* Misc.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Who | ctor | — | — | — |
