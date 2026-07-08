<!-- provenance: verbose -->
# GmTicket

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# GmTicket

**Purpose & Responsibilities**

`GmTicket` defines three client-to-server network packet structures within `WorldPackets::GmTicket`. It handles deserialization of GM ticket creation, text updates, and player survey submissions. This unit is a pure data transport layer: it extracts raw bytes from `WorldPacket` buffers into strongly-typed C++ fields without performing validation or business logic.

## Member-by-Member Behavior

Each class inherits from `ClientPacket` and implements `ReadFromWorldPacket`, invoked by the network layer upon receiving the corresponding opcode.

#### `GmTicketCreate::ReadFromWorldPacket`
Parses a new GM ticket submission. Extracts from `recv_data`:
1.  **`ticketType`**: `uint8` cast to `TicketType`.
2.  **`mapId`**: Map ID (`uint32`).
3.  **`x`, `y`, `z`**: Player coordinates (`float`).
4.  **`ticketText`**: Issue description (`std::string`).
5.  **`reservedForFutureUse`**: Compatibility padding (`std::string`).

The constructor initializes defaults (zeroed coordinates, empty strings) to ensure a known state.

#### `GmTicketUpdateText::ReadFromWorldPacket`
Handles ticket text updates. Extracts:
1.  **`type`**: Update context (`uint8`).
2.  **`ticketText`**: New text content (`std::string`).

#### `GMSurveySubmit::ReadFromWorldPacket`
Parses survey responses (clients > 1.10.2). Extracts:
1.  **`mainSurvey`**: Main survey ID (`uint32`).
2.  **`subSurveys`**: Variable-length list of `SubSurvey` structs. Loops up to 10 times:
    *   Reads `subSurveyId`. If `0`, breaks immediately (terminator).
    *   Reads `rank` (`uint8`) and `comment` (`std::string`).
    *   Moves the populated struct into `subSurveys`.
3.  **`comment`**: General survey comment (`std::string`).

## Cross-Unit Boundaries

These packets are leaf nodes for outgoing calls; they do not invoke other business logic units. They rely on:
*   **`ByteBuffer` operators**: `operator>>` variants for extracting primitives and strings from `WorldPacket`.
*   **`ClientPacket`**: Base class providing opcode definitions and infrastructure.

They are called by the network handler when the server receives opcodes `CMSG_GMTICKET_CREATE`, `CMSG_GMTICKET_UPDATETEXT`, or `CMSG_GMSURVEY_SUBMIT`.

## Data Model

This unit does not interact directly with any database tables. It operates entirely on in-memory network buffers. Persistence occurs in downstream handlers consuming these parsed objects.

## Notable Implementation Details

1.  **Survey Terminator**: `GMSurveySubmit::ReadFromWorldPacket` uses `subSurveyId == 0` to break the sub-survey loop. Valid IDs must be non-zero; a zero ID stops parsing immediately.
2.  **Version Gate**: `GMSurveySubmit` is wrapped in `#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_10_2`. Changes to survey logic must respect this gate.
3.  **No Validation**: No checks for coordinate ranges, string lengths, or ticket types. Invalid data is stored as-is; validation must occur in downstream handlers.
4.  **Reserved Field**: `GmTicketCreate` includes `reservedForFutureUse`, consuming bandwidth/memory for potential future protocol changes.

## Member Reference

**ReadFromWorldPacket#2** (`GmTicketUpdateText::ReadFromWorldPacket`): Parses `type` and `ticketText` from the incoming packet buffer using `ByteBuffer` extraction operators.

**ReadFromWorldPacket#3** (`GMSurveySubmit::ReadFromWorldPacket`): Parses `mainSurvey`, a variable list of `SubSurvey` structs (terminated by `subSurveyId == 0`), and a final `comment`. Conditionally compiled for clients > 1.10.2.

**GmTicketCreate** (`GmTicketCreate` constructor): Initializes the packet object with opcode `CMSG_GMTICKET_CREATE` and default values for all fields (zeroed coordinates, empty strings, default ticket type).

**ReadFromWorldPacket** (`GmTicketCreate::ReadFromWorldPacket`): Parses `ticketType`, `mapId`, `x`, `y`, `z`, `ticketText`, and `reservedForFutureUse` from the incoming packet buffer. Casts the first byte to `TicketType`.

---

<!-- machine-true, projected from graph.json -->

## Map — GmTicket

*Source:* GmTicket.cpp, GmTicket.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ReadFromWorldPacket#2 | method | ByteBuffer/operator>>, ByteBuffer/operator>>#6, ByteBuffer/operator>>#8, ByteBuffer/operator>>#9 | — | — |
| ReadFromWorldPacket#3 | method | ByteBuffer/operator>>, ByteBuffer/operator>>#6 | — | — |
| GmTicketCreate | ctor | — | — | — |
| ReadFromWorldPacket | method | ByteBuffer/operator>>, ByteBuffer/operator>>#6, ByteBuffer/operator>>#9 | — | — |
