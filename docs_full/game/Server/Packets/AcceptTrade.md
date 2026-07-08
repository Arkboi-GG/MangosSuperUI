# AcceptTrade

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AcceptTrade (`WorldPackets::Trade::AcceptTrade`)

**Purpose & Responsibilities**

`AcceptTrade` is a lightweight data structure within the `WorldPackets::Trade` namespace, designed to represent the `CMSG_ACCEPT_TRADE` client-to-server network message. Its sole responsibility is to encapsulate the raw bytes of a trade acceptance request received from a client and provide the mechanism to parse them into a usable object state. As a `ClientPacket`, it serves as the input contract for the server-side trade system, signaling that a player has confirmed their intent to finalize a trade session.

The class contains no payload fields (such as item lists or gold amounts) because the specific contents of the trade are managed by the server state during the preceding negotiation phase. The packet itself carries only the command identifier, implying that the server must look up the active trade session associated with the sending player to process the acceptance.

## Member-by-Member Behavior

### Construction and Initialization
The **`AcceptTrade`** constructor initializes the packet object. It explicitly invokes the base class `ClientPacket` constructor, passing the constant `CMSG_ACCEPT_TRADE`. This registers the packet type with the network layer, ensuring that incoming data streams identified by this opcode are routed to this specific handler. No additional initialization is required for payload data, as the class holds no member variables beyond those inherited from `ClientPacket`.

### Packet Parsing
Although not listed as a separate member in the MAP due to its inheritance, the class overrides `ReadFromWorldPacket` (declared in the header). This method is responsible for deserializing the binary data from the `WorldPacket` buffer. Given that `AcceptTrade` has no public data members, this implementation likely performs minimal validation or simply consumes the packet header, relying on the server logic to verify the trade state externally.

## Cross-Unit Boundaries

*   **Calls Out:** None. The `AcceptTrade` class does not invoke methods in other units. It is a passive data carrier.
*   **Called By:** None listed in the MAP. In practice, this class is instantiated by the network dispatcher when a `CMSG_ACCEPT_TRADE` opcode is detected. The dispatcher then passes this object to the trade handling subsystem (likely within `Player` or a dedicated `TradeHandler` unit, though these are outside the scope of this unit's definition).

## Data Model

This unit does not interact directly with any database tables. It operates entirely in memory as part of the network I/O layer. Any persistence related to trades (e.g., logging completed trades) would occur in downstream handlers after the packet has been processed, not within this class.

## Notable Implementation Details

*   **Final Class:** The class is marked `final`, preventing inheritance. This enforces a strict, closed design for this specific packet type, ensuring no derived classes can alter its behavior or add unexpected payload fields.
*   **No Payload State:** Unlike `SetTradeItem` or `SetTradeGold` (also in `Trade.h`), `AcceptTrade` does not store trade specifics. This indicates that the trade state is maintained server-side (likely in a `Trade` object linked to the players involved), and the packet is merely a trigger signal.
*   **Namespace Isolation:** Defined within `WorldPackets::Trade`, it is strictly scoped to world-server network traffic, separating it from authentication or realm-server packets.

## Member Reference

**AcceptTrade**  
Constructor for the `AcceptTrade` packet. Initializes the base `ClientPacket` with the opcode `CMSG_ACCEPT_TRADE`. Does not initialize any local member variables as the class holds no payload data.

---

<!-- machine-true, projected from graph.json -->

## Map — AcceptTrade

*Source:* Trade.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AcceptTrade | ctor | — | — | — |
