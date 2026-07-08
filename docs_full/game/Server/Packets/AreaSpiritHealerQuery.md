# AreaSpiritHealerQuery

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AreaSpiritHealerQuery

`AreaSpiritHealerQuery` is a client-side network packet class within the `WorldPackets::Battleground` namespace. It represents the `CMSG_AREA_SPIRIT_HEALER_QUERY` message sent by the game client to the server. This message is triggered when a player interacts with an Area Spirit Healer NPC, typically to request information about resurrection services or to initiate the resurrection process while dead.

The class inherits from `ClientPacket`, establishing it as an inbound message from the client. Its primary responsibility is to deserialize the raw binary data received over the network into structured fields, specifically extracting the `ObjectGuid` of the target NPC.

## Member-by-Member Behavior

### Construction
**`AreaSpiritHealerQuery`**
The constructor initializes the packet object. It explicitly calls the base class constructor `ClientPacket(CMSG_AREA_SPIRIT_HEALER_QUERY)`, registering this packet type with the network handler under the specific opcode `CMSG_AREA_SPIRIT_HEALER_QUERY`. No additional initialization is performed on member variables during construction.

### Deserialization
Although not listed in the MAP as a separate entry due to being a virtual override defined elsewhere (likely in a corresponding `.cpp` file not provided in the source snippet, or implied by the interface), the class declares `void ReadFromWorldPacket(WorldPacket& recv_data) override`. This method is responsible for reading the `guid` field from the incoming `WorldPacket`. Based on standard patterns in this codebase, it would extract the `ObjectGuid` from the packet stream. The MAP indicates no external calls are made from this unit, implying the deserialization logic is self-contained or relies solely on the `WorldPacket` API.

## Cross-Unit Boundaries

*   **Inheritance:** Inherits from `ClientPacket` (defined in `Packet.h`). This establishes the contract for handling inbound network messages.
*   **Dependency:** Uses `ObjectGuid` (defined in `ObjectGuid.h`) to store the identifier of the spirit healer NPC.
*   **No Outbound Calls:** The MAP confirms this unit makes no calls to other units. It is a pure data structure with deserialization logic.
*   **Called By:** The MAP shows no external callers. In practice, this class is instantiated by the network layer when the opcode `CMSG_AREA_SPIRIT_HEALER_QUERY` is detected. The instantiation and subsequent processing (calling `ReadFromWorldPacket` and then routing to a handler) occur in the network dispatch system, which is outside the scope of this specific translation unit.

## Data Model

This unit does not interact directly with any database tables. It operates entirely in memory, handling transient network data.

## Notable Implementation Details

*   **Minimal State:** The class contains only a single member variable: `ObjectGuid guid`. This reflects the simplicity of the query: the client identifies *which* spirit healer it is interacting with.
*   **Opcode Specificity:** The constructor hardcodes the opcode `CMSG_AREA_SPIRIT_HEALER_QUERY`. This ensures strict coupling between this class and the specific network message type.
*   **Namespace Organization:** Located in `WorldPackets::Battleground`, indicating its logical grouping with other battleground-related network messages, even though spirit healers exist in non-battleground zones. This suggests a broader categorization of "combat-related" or "PvP-adjacent" interactions in the packet namespace design.

## Member Reference

**AreaSpiritHealerQuery**
Constructor for the `AreaSpiritHealerQuery` packet. Initializes the base `ClientPacket` with the opcode `CMSG_AREA_SPIRIT_HEALER_QUERY`. Does not perform any additional setup or call other units.

---

<!-- machine-true, projected from graph.json -->

## Map — AreaSpiritHealerQuery

*Source:* Battleground.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AreaSpiritHealerQuery | ctor | — | — | — |
