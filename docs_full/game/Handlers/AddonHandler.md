# AddonHandler

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AddonHandler

**Purpose & Responsibilities**

`AddonHandler` is a singleton utility responsible for processing client-side addon information during the authentication session. Specifically, it handles the decompression and parsing of compressed addon data sent by the client, and constructs the server's response packet (`SMSG_ADDON_INFO`). Its primary role is to determine the visibility status of each addon reported by the client—marking official Blizzard addons as hidden (to prevent clients from detecting server-side modifications via missing official files) and allowing custom addons to remain visible. It acts as a bridge between the raw network packet received during login and the structured response required by the game client protocol.

**Member-by-Member Behavior**

The unit contains minimal lifecycle management and one core operational method.

*   **Lifecycle**: The constructor `AddonHandler()` and destructor `~AddonHandler()` are empty stubs. The class is instantiated as a singleton via the `INSTANTIATE_SINGLETON_1` macro in the source file, accessible globally via the `sAddOnHandler` macro defined in the header.
*   **BuildAddonPacket**: This is the sole functional method. It takes a source `WorldPacket` containing compressed addon data from the client and a target `WorldPacket` to populate with the server's response.
    1.  **Validation**: It first checks if the source packet has enough data to contain a size header. It reads the expected uncompressed size (`tempValue`). If the size is zero or exceeds a safety limit of `0xFFFFF` (approx 1MB), it returns `false`, logging an error for oversized packets.
    2.  **Decompression**: It allocates a `ByteBuffer` of the specified size and uses the `zlib` library (`uncompress`) to decompress the payload from the source packet into this buffer. If decompression fails, it logs an error and returns `false`.
    3.  **Parsing & Response Construction**:
        *   For client builds newer than 1.6.1, it initializes the target packet with opcode `SMSG_ADDON_INFO`.
        *   It iterates through the decompressed buffer, reading individual addon records consisting of a name, flags, modulus CRC, and URL CRC.
        *   **Official Addons**: If the addon name contains "Blizzard", it marks the addon as `ADDON_STATUS_HIDDEN`. It then checks the addon's modulus CRC against a hardcoded constant (`correctModulusCRC = 0x4C1C776D`). If the CRC does not match, it includes a 256-byte cryptographic key (`tdata`) in the response. This mechanism likely serves to verify the integrity of the client's official addon files or to provide a decryption key for specific official content.
        *   **Custom Addons**: Any addon not containing "Blizzard" in its name is marked as `ADDON_STATUS_VISIBLE` with no additional info or URL provided.
    4.  **Legacy Support**: For client builds 1.10.2 and older, it performs minor adjustments to the packet format, including reading two unknown fields from the decompressed data and appending a zero byte to the target packet.

**Cross-Unit Boundaries**

*   **Called by `WorldSocket._HandleAuthSession`**: The `WorldSocket` unit invokes `BuildAddonPacket` during the authentication handshake. This indicates that addon verification is part of the initial login sequence, ensuring the client's addon configuration is validated before full session establishment.
*   **Calls into `ByteBuffer`**: Extensively uses `ByteBuffer` methods (`resize`, `contents`, `rpos`, `size`, `operator>>`, `operator<<`) to manage memory allocation for decompression and to parse/write binary data structures.
*   **Calls into `WorldPacket`**: Uses `WorldPacket::Initialize` to set the response opcode and `operator<<` to serialize the final response data.
*   **Calls into `Log.Main`**: Uses `sLog.Out` to report errors for malformed packets, oversized data, or decompression failures.
*   **External Dependency**: Relies on the `zlib` library (`uncompress`) for data decompression, though this is a system-level library rather than another codebase unit.

**Data Model**

This unit does not interact with any database tables. All data processing occurs in-memory using network packets and temporary buffers.

**Notable Implementation Details**

*   **Hardcoded Cryptographic Key**: The method contains a static array `tdata` (256 bytes) and a specific CRC check (`0x4C1C776D`). This suggests a tight coupling with a specific version of the official Blizzard addons. If the official addons change their checksums, this logic may fail to recognize them as "official," potentially causing them to be treated as custom addons or failing the integrity check.
*   **Client Build Conditional Logic**: The behavior changes significantly based on `SUPPORTED_CLIENT_BUILD`.
    *   For builds > 1.7.1, `ADDON_STATUS_BANNED` is 0 and `VISIBLE` is 1.
    *   For builds <= 1.7.1, these values are swapped.
    *   For builds <= 1.10.2, extra parsing steps are taken.
    *   For builds <= 1.6.1, the entire response construction block is skipped, implying older clients might not expect this specific response format or handle addons differently.
*   **String Matching Heuristic**: Official addons are identified solely by checking if the name contains the substring "Blizzard". This is a fragile heuristic; any custom addon naming itself with "Blizzard" would be incorrectly classified as official and hidden.
*   **Memory Safety**: The code casts away `const` from `decompressedPacket.contents()` and `sourcePacket.contents()` when passing pointers to `uncompress`. This is necessary because `zlib`'s `uncompress` signature expects non-const pointers for input/output buffers, despite the logical intent being read-only for the source. This is a common pattern in legacy C++ code interfacing with C libraries but requires careful handling to avoid accidental modification of the source packet data.

## Member Reference

**AddonHandler**
Constructor for the singleton instance. Empty body.

**~AddonHandler**
Destructor for the singleton instance. Empty body.

**BuildAddonPacket**
Core method that decompresses client addon data, parses individual addon entries, and constructs an `SMSG_ADDON_INFO` response. It hides official "Blizzard" addons, verifies their integrity via CRC, and exposes custom addons. Returns `true` on success, `false` on validation or decompression failure.

---

<!-- machine-true, projected from graph.json -->

## Map — AddonHandler

*Source:* AddonHandler.cpp, AddonHandler.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AddonHandler | ctor | — | — | — |
| ~AddonHandler | dtor | — | — | — |
| BuildAddonPacket | method | ByteBuffer/append#5, ByteBuffer/ByteBuffer, ByteBuffer/contents, ByteBuffer/operator<<#10, ByteBuffer/operator<<#7, ByteBuffer/operator>>, ByteBuffer/operator>>#6, ByteBuffer/operator>>#9, ByteBuffer/resize, ByteBuffer/rpos, ByteBuffer/size, Log.Main/Out, WorldPacket/Initialize | WorldSocket/_HandleAuthSession | — |
