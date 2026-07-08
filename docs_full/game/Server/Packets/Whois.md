# Whois

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Whois Packet Structure

## Purpose & Responsibilities

The `Whois` class, defined within the `WorldPackets::Query` namespace in `Query.h`, serves as a data structure representing a specific client-to-server network message: `CMSG_WHOIS`. Its primary responsibility is to encapsulate the payload of a "Whois" query sent by a client, which typically requests information about a specific character by name.

As a subclass of `ClientPacket`, `Whois` inherits the general mechanics of packet identification and serialization but specializes in holding the `charName` field. It acts as a passive data container; it does not contain logic for processing the query, validating the name, or generating responses. Those responsibilities lie elsewhere in the server architecture. This unit is strictly concerned with defining the shape of the incoming request data.

## Member-by-Member Behavior

### Construction and Initialization
**Whois**
The constructor initializes the packet object. It explicitly calls the base class `ClientPacket` constructor, passing the constant `CMSG_WHOIS`. This associates the packet instance with the correct opcode, ensuring that when the server receives raw bytes with this opcode, it can instantiate or cast the data to this specific type. The member variable `charName` is default-initialized as an empty `std::string` by the compiler, awaiting population via deserialization.

### Deserialization Interface
While declared in this header, the `ReadFromWorldPacket` method is not listed in the MAP for this unit and thus its behavior is not documented here as part of this specific unit's responsibilities. However, it is worth noting that `Whois` relies on this inherited interface to populate its `charName` member from the raw network buffer.

## Cross-Unit Boundaries

*   **Inheritance (`ClientPacket`):** `Whois` derives from `ClientPacket`. This establishes the contract that `Whois` is a packet originating from the client. It relies on `ClientPacket` for common functionality such as opcode management and potentially base-level validation or logging hooks.
*   **Deserialization Dependency (`WorldPacket`):** The `ReadFromWorldPacket` method (declared in this header, implemented elsewhere) accepts a `WorldPacket&` reference. This indicates that `Whois` depends on the `WorldPacket` class (likely defined in `Packet.h` or similar) to provide the low-level byte-stream reading capabilities. `Whois` does not parse bits directly; it delegates the extraction of strings and primitives to `WorldPacket` methods.
*   **Usage Context:** Although not shown in the "Called by" column of the map, `Whois` instances are typically created or populated by the network handler layer when a `CMSG_WHOIS` opcode is detected. They are then passed to the game logic layer (e.g., a session handler or command processor) which extracts `charName` to perform the actual lookup.

## Data Model

This unit does not interact directly with any database tables. It operates entirely in memory, handling transient network data. The `charName` field is a runtime value derived from client input, not a persistent database record.

## Notable Implementation Details

*   **Final Class:** The class is marked `final`, preventing further inheritance. This is a design choice indicating that the `Whois` packet structure is complete and should not be extended by subclasses, likely to enforce strict adherence to the protocol definition.
*   **Namespace Organization:** It resides in `WorldPackets::Query`, grouping it logically with other inquiry-type packets (like `QueryPlayerName`, `QueryCreature`, etc.). This suggests a modular approach to packet handling where different categories of queries are separated.
*   **String Handling:** The use of `std::string` for `charName` implies that the server expects the character name to be a standard C++ string. Care must be taken in the implementation of `ReadFromWorldPacket` (in the `.cpp` file) to handle string termination and encoding correctly, as network protocols often use specific delimiters or length prefixes for strings.

## Member Reference

**Whois**
Constructor for the `Whois` packet. Initializes the base `ClientPacket` with the `CMSG_WHOIS` opcode. Default-initializes the `charName` member.

---

<!-- machine-true, projected from graph.json -->

## Map — Whois

*Source:* Query.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Whois | ctor | — | — | — |
