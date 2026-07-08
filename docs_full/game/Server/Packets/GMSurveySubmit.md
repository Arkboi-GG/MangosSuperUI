# GMSurveySubmit

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# GMSurveySubmit

## Purpose & Responsibilities

`GMSurveySubmit` is a client-side packet structure within the `WorldPackets::GmTicket` namespace, responsible for deserializing the `CMSG_GMSURVEY_SUBMIT` network message. This packet represents a player submitting feedback via the in-game survey system, a feature introduced after client build 1.10.2.

The class encapsulates the raw data received from the client, including the ID of the main survey being answered, a collection of sub-survey responses (each containing a specific question ID, a ranked response value, and an optional comment), and a general comment field for the overall survey. It inherits from `ClientPacket`, integrating into the server's network layer to provide structured access to the incoming binary data.

## Member-by-Member Behavior

### Construction and Initialization

**`GMSurveySubmit`**
The constructor initializes the packet object. It sets the packet opcode to `CMSG_GMSURVEY_SUBMIT` by invoking the base `ClientPacket` constructor. It also initializes the member variables:
- `mainSurvey` is set to `0`.
- `subSurveys` is initialized as an empty `std::vector`.
- `comment` is initialized as an empty `std::string`.

This ensures that if the packet reading fails or is incomplete, the object remains in a valid, default state.

### Data Deserialization

Although not explicitly listed in the MAP as a separate member due to the MAP's focus on the class itself, the behavior of `ReadFromWorldPacket` is intrinsic to the class's function as defined in the header. This virtual method overrides the base class implementation to parse the binary `WorldPacket` into the structured fields (`mainSurvey`, `subSurveys`, `comment`). The logic for parsing the variable-length vector of `SubSurvey` objects and strings resides in the corresponding `.cpp` implementation (not provided here, but implied by the override declaration).

## Cross-Unit Boundaries

- **Calls Out:** None. The MAP indicates no outgoing calls to other units from this class. The deserialization logic likely interacts with `WorldPacket` methods (e.g., `ReadBits`, `ReadString`) which are part of the core networking infrastructure, but these are standard library/base class interactions rather than cross-unit business logic dependencies.
- **Called By:** None. The MAP indicates no other units explicitly call into this class. Typically, instances of `ClientPacket` subclasses are created and populated by the network handler during the packet dispatch phase. The handler creates the instance, calls `ReadFromWorldPacket`, and then passes the populated object to the appropriate command handler or game logic processor.

## Data Model

This unit does not interact directly with any database tables. It operates purely on network packet data. Any persistence of survey results would occur downstream in the game logic handlers that process this packet, potentially writing to tables such as `gm_survey` or similar (depending on the specific server implementation), but `GMSurveySubmit` itself is agnostic to storage.

## Notable Implementation Details

- **Conditional Compilation:** The entire class definition is wrapped in `#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_10_2`. This indicates that the survey submission feature is only supported in client versions newer than 1.10.2. Engineers maintaining backward compatibility must ensure that packets with this opcode are handled gracefully (e.g., ignored or rejected) for older clients, although the presence of the class itself is hidden from compilation for those builds.
- **Nested Structure:** The use of the nested `struct SubSurvey` allows for a clean representation of multiple questions within a single survey. Each sub-survey contains a `subSurveyId` (likely identifying the specific question), a `rank` (the user's selected answer, typically an integer scale), and a `comment` (free-text feedback).
- **Vector Usage:** The `subSurveys` member is a `std::vector`, implying that a single survey submission can contain zero or more sub-survey responses. The deserialization logic must correctly handle the count of sub-surveys sent by the client.

## Member Reference

**`GMSurveySubmit`**: Constructor that initializes the packet with the `CMSG_GMSURVEY_SUBMIT` opcode and resets all data members (`mainSurvey`, `subSurveys`, `comment`) to their default empty or zero states.

---

<!-- machine-true, projected from graph.json -->

## Map — GMSurveySubmit

*Source:* GmTicket.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GMSurveySubmit | ctor | — | — | — |
