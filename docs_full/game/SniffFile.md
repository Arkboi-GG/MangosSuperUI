# SniffFile

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# SniffFile

**Purpose & Responsibilities**

`SniffFile` serializes network traffic logs into a binary "sniff" file format for debugging and replay. It manages a `FILE*` stream, writing a fixed header followed by packet records. Each record captures direction, timestamps, opcode, and payload, handling the protocol-specific difference between client (32-bit opcode) and server (16-bit opcode) packet structures.

## Member-by-Member Behavior

### Lifecycle
*   **`SniffFile(FILE* pFile)`**: Wraps an existing `FILE*`. Asserts the pointer is non-null.
*   **`SniffFile(char const* fileName)`**: Opens a new file in binary write mode (`"wb"`). Asserts the file opened successfully.
*   **`~SniffFile()`**: Closes the underlying `FILE*` if open, flushing buffers.

### Serialization
*   **`WriteHeader()`**: Writes the file signature: `"PKT"` (3 bytes), sniff version `0x201` (`uint16`), client build `SUPPORTED_CLIENT_BUILD` (`uint16`), and 40 bytes of zero padding.
*   **`WritePacket(WorldPacket const& packet, bool isClientPacket, time_t timestamp)`**: Writes a single packet record:
    1.  Direction: `0x00` (client) or `0xFF` (server).
    2.  Unix timestamp (`uint32`).
    3.  Millisecond timestamp from `packet.GetPacketTime()` (`uint32`).
    4.  Size and Opcode:
        *   **Client**: Size includes 4-byte opcode; opcode written as `uint32`.
        *   **Server**: Size includes 2-byte opcode; opcode written as `uint16`.
    5.  Payload: Raw bytes from `packet.contents()` if size > 0.
*   **`WritePacket(LoggedPacket const& packet)`**: Delegates to the above method, extracting `data`, `isClientPacket`, and `timestamp` from the `LoggedPacket` struct.
*   **`WriteToFile(Container<LoggedPacket, Args...> const& container)`**: Template method that writes the header, then iterates the container calling `WritePacket` for each entry.

## Cross-Unit Boundaries

*   **Calls `Errors/PrintStacktraceAndThrow`**: Both constructors use `MANGOS_ASSERT` to validate file handles, triggering error handling if initialization fails.
*   **Calls `ByteBuffer/contents`, `ByteBuffer/size`, `WorldPacket/GetOpcode`, `WorldPacket/GetPacketTime`**: Reads raw data, size, opcode, and timing metadata from the packet object.
*   **Called by `MovementAnticheat/Finalize`**: Used to dump movement packets for anti-cheat analysis.
*   **Called by `WorldSession.Main/QueueBinaryPacket`, `WorldSession.Main/SendPacketImpl`**: Integrates with the session layer to log network I/O.

## Data Model

This unit does not interact with any database tables.

## Notable Implementation Details

*   **Opcode Size Variance**: Client opcodes are `uint32`, server opcodes are `uint16`. Parsers must check the direction byte to interpret the opcode field correctly.
*   **Silent Write Failures**: `fwrite` return values are ignored. Disk full or I/O errors result in silent corruption of the sniff file.
*   **Timestamp Fallback**: The `LoggedPacket` struct (defined in the header) ensures `WorldPacket::FillPacketTime` is called if the packet lacks a millisecond timestamp, using `WorldTimer::getMSTime()`.

## Member Reference

*   **SniffFile**: Constructor taking `FILE*`; asserts non-null.
*   **SniffFile#2**: Constructor taking filename; opens file in `"wb"` mode and asserts success.
*   **~SniffFile**: Destructor; closes `FILE*` if valid.
*   **WriteHeader**: Writes magic "PKT", version `0x201`, build ID, and 40 bytes of zeros.
*   **WritePacket**: Overload for `LoggedPacket`; delegates to the core `WritePacket` method.
*   **WritePacket#2**: Core serialization; writes direction, timestamps, size, opcode (32-bit client / 16-bit server), and payload.

---

<!-- machine-true, projected from graph.json -->

## Map — SniffFile

*Source:* SniffFile.cpp, SniffFile.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| SniffFile | ctor | Errors/PrintStacktraceAndThrow | — | — |
| SniffFile#2 | ctor | Errors/PrintStacktraceAndThrow | MovementAnticheat/Finalize | — |
| ~SniffFile | dtor | — | — | — |
| WriteHeader | method | — | — | — |
| WritePacket | method | — | — | — |
| WritePacket#2 | method | ByteBuffer/contents, ByteBuffer/size, WorldPacket/GetOpcode, WorldPacket/GetPacketTime | WorldSession.Main/QueueBinaryPacket, WorldSession.Main/SendPacketImpl | — |
