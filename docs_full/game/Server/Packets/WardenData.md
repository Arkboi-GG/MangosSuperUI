# WardenData

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# WardenData

**Purpose & Responsibilities**

`WardenData` is a client-to-server packet structure within the `WorldPackets::Misc` namespace, responsible for transporting raw anti-cheat data from the game client to the server. It is part of the "Warden" system, a proprietary integrity-checking mechanism used in World of Warcraft to detect modified client files or unauthorized third-party software.

This specific unit handles the **reception** side of the protocol: it defines the memory layout for the incoming `CMSG_WARDEN_DATA` packet and provides the constructor necessary to instantiate the packet handler. The actual parsing of the binary payload (`ReadFromWorldPacket`) and the subsequent processing of the anti-cheat checks are implemented in other units (specifically, the corresponding `.cpp` implementation file and the server-side Warden handler logic, which are not part of this header-only definition).

The class is conditionally compiled only for client builds newer than `CLIENT_BUILD_1_5_1`, indicating that the Warden anti-cheat protocol was introduced or significantly altered after that version.

## Member-by-Member Behavior

### Construction
*   **`WardenData()`**: The default constructor initializes the packet object. It sets the opcode to `CMSG_WARDEN_DATA` via the base `ClientPacket` constructor. It does not initialize the `data` vector, leaving it empty until populated by the deserialization logic in `ReadFromWorldPacket`.

## Cross-Unit Boundaries

*   **Base Class (`ClientPacket`)**: `WardenData` inherits from `ClientPacket` (defined in `Packet.h`). This inheritance provides the fundamental packet infrastructure, including the opcode management and the virtual interface for reading data from the network stream.
*   **Implementation Unit (`Misc.cpp` or similar)**: While declared here, the `ReadFromWorldPacket` method is implemented elsewhere. That implementation will populate the `data` member with the raw bytes received from the client.
*   **Server Logic (Warden Handler)**: Once the packet is fully read, the server's Warden subsystem (likely in a unit such as `WardenHandler.cpp` or `Player.cpp`) will consume the `data` vector to perform integrity checks. This unit merely transports the data; it does not interpret it.

## Data Model

This unit does not interact directly with any database tables. It operates entirely in memory, handling transient network packet data. The `data` vector holds binary blobs that may eventually trigger updates to Warden-related database records (e.g., logging violations or updating check states), but those interactions occur in downstream processing units, not here.

## Notable Implementation Details

*   **Conditional Compilation**: The entire class is wrapped in `#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_5_1`. This ensures that the packet structure is only available for clients that support the Warden protocol. Attempting to use this class with older clients would result in compilation errors or undefined behavior if not guarded.
*   **Raw Data Transport**: The `data` member is a `std::vector<uint8>`. This indicates that the Warden protocol sends a variable-length binary blob. The structure does not parse this blob into specific fields (like check IDs or results) at the packet level; that parsing is deferred to the server-side handler. This design keeps the packet layer lightweight and decoupled from the complex logic of interpreting Warden responses.
*   **No Validation in Header**: As is standard for packet headers, there is no validation logic here. The validity of the data (length, format) is assumed to be handled during the `ReadFromWorldPacket` phase or by the consumer of the packet.

## Member Reference

**WardenData**
Constructor for the `WardenData` packet. Initializes the base `ClientPacket` with the opcode `CMSG_WARDEN_DATA`. The `data` member remains empty until populated by the deserialization routine. Available only for client builds greater than `1.5.1`.

---

<!-- machine-true, projected from graph.json -->

## Map — WardenData

*Source:* Misc.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| WardenData | ctor | — | — | — |
