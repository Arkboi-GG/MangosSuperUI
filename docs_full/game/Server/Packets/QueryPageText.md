# QueryPageText

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# QueryPageText

## Purpose & Responsibilities

`QueryPageText` is a lightweight data structure within the `WorldPackets::Query` namespace, representing a client-to-server network packet (`CMSG_PAGE_TEXT_QUERY`). Its sole responsibility is to encapsulate the payload for a request initiated by a client to retrieve the text content associated with a specific in-game page (typically used for reading books, scrolls, or gossip menu descriptions).

As a `ClientPacket`, it serves as the input side of the communication channel. It holds a single field, `pageID`, which identifies the requested text resource. The class provides the necessary infrastructure to deserialize this ID from the raw binary data received over the network via the `ReadFromWorldPacket` method.

## Member-by-Member Behavior

### Construction and Initialization
The **`QueryPageText`** constructor initializes the object as a `ClientPacket` with the opcode `CMSG_PAGE_TEXT_QUERY`. This registration ensures that when the server receives a packet with this opcode, it can correctly instantiate this class to handle the incoming data. The constructor also initializes the `pageID` member to `0` by default, though this value is immediately overwritten during deserialization.

### Deserialization
The **`ReadFromWorldPacket`** method (inherited from `ClientPacket` but implemented in the corresponding `.cpp` file, not shown here but implied by the interface) is responsible for extracting the `pageID` from the `WorldPacket` buffer. Based on standard Mangos/WoW packet structures for this query type, this method reads a 32-bit unsigned integer (`uint32`) from the packet stream into the `pageID` member.

## Cross-Unit Boundaries

*   **Calls Out:** None. This unit is a pure data carrier and does not invoke logic in other units.
*   **Called By:** While the MAP indicates no specific callers, in the broader system context, instances of `QueryPageText` are typically constructed and populated by the network layer (e.g., `WorldSession` or a packet handler dispatcher) when a `CMSG_PAGE_TEXT_QUERY` is received. The handler then passes this object to the game logic (likely `ChatHandler` or `GossipHandler`) to resolve the page text and send the response (`SMSG_PAGE_TEXT`).

## Data Model

This unit does not interact directly with any database tables. It operates entirely on in-memory network packet data. The `pageID` it carries will eventually be used by other units to look up text data, potentially from tables like `page_text`, but `QueryPageText` itself performs no SQL operations.

## Notable Implementation Details

*   **Final Class:** The class is marked `final`, indicating it is not intended for inheritance. This is consistent with its role as a leaf node in the packet hierarchy.
*   **Namespace:** It resides in `WorldPackets::Query`, grouping it with other query-type packets (like `QueryPlayerName`, `QueryCreature`, etc.), which aids in modular organization of network message handlers.
*   **Default Value:** The `pageID` member is initialized to `0` in the declaration. While the constructor does not explicitly set it, the default initialization ensures a safe state before deserialization occurs.

## Member Reference

**QueryPageText**
Constructor for the `QueryPageText` packet. Initializes the base `ClientPacket` with the opcode `CMSG_PAGE_TEXT_QUERY` and sets the `pageID` member to its default value of `0`. This prepares the object to receive and store the page identifier from an incoming network packet.

---

<!-- machine-true, projected from graph.json -->

## Map — QueryPageText

*Source:* Query.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| QueryPageText | ctor | — | — | — |
