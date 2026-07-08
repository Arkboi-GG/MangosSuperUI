# ProxyV2Reader

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`ProxyV2Reader` parses the HAProxy PROXY Protocol Version 2 binary handshake to extract the original client’s IPv4 address from connections terminated by a reverse proxy. It is invoked immediately upon socket acceptance, before any application data is read, to ensure accurate client identification for logging and session management. The unit strictly validates the protocol signature, version, command, and address family, rejecting unsupported formats (e.g., IPv6, Unix sockets) with specific errors.

## Member-by-Member Behavior

### **ReadProxyV2Handshake**

This function initiates an asynchronous two-stage read on the provided `AsyncSocket` to consume the PROXY v2 header and payload.

1.  **Header Validation**:
    *   Allocates a `proxy_hdr_v2` structure and reads 16 bytes.
    *   Validates the 12-byte magic signature (`0D 0A 0D 0A 00 0D 0A 51 55 49 54 0A`).
    *   Checks `ver_cmd`: high nibble must be `2` (version), low nibble must be `1` (`PROXY` command).
    *   Checks `fam`: must be `0x11` (`TCP_OVER_IPV4`). IPv6 and Unix sockets are rejected.
    *   Checks `len`: converted via `ntohs`, must equal `sizeof(proxy_addr::ipv4_addr)` (12 bytes).
    *   Any mismatch logs an error via `Log.Main/Out` and returns `IO::NetworkError::InvalidProtocolBehavior` via the callback.

2.  **Payload Extraction**:
    *   If valid, allocates a `proxy_addr` union and reads the payload length.
    *   Extracts `src_addr` from the IPv4 block, converts from network to host byte order (`ntohl`), and constructs an `IO::Networking::IpAddress` via `IpAddress::FromIpv4Uint32`.
    *   Invokes the callback with the resulting `IpAddress` or an error if the read fails.

## Cross-Unit Boundaries

*   **Called by `realmd_Main/main` and `WorldSocketMgr/OnNewClientConnected`**:
    Entry points for new connections in the Realm and World servers. They call this function to determine if a connection is proxied. Success yields the client IP; failure implies a direct connection or protocol error, handled by the caller.

*   **Calls into `AsyncSocket._posix/Read`**:
    Performs the non-blocking I/O operations. The `AsyncSocket` unit manages the event loop and triggers the lambda callbacks when data arrives.

*   **Calls into `IpAddress/FromIpv4Uint32`**:
    Converts the raw 32-bit IPv4 integer into a standardized `IpAddress` object for consistent usage across the codebase.

*   **Calls into `Log.Main/Out`**:
    Logs errors to `LOG_NETWORK` at `LOG_LVL_ERROR` for any validation failure (signature, version, command, family, or length mismatch).

*   **Calls into `NetworkError/NetworkError`**:
    Constructs error objects for the callback, primarily using `IO::NetworkError::ErrorType::InvalidProtocolBehavior` for protocol violations.

## Data Model

This unit does not interact with any database tables. It operates entirely on in-memory network buffers.

## Notable Implementation Details

*   **IPv4-Only Constraint**: The code explicitly rejects non-IPv4 families (`fam != 0x11`). Infrastructure requiring IPv6 proxy support must update this unit to parse `ipv6_addr`.
*   **Memory Safety**: Uses `std::shared_ptr` with raw `new` for `proxy_hdr_v2` and `proxy_addr` to ensure buffer validity across asynchronous callback boundaries.
*   **Struct Packing**: `#pragma pack(1)` ensures no padding in `proxy_hdr_v2` and `proxy_addr`, matching the binary protocol specification.
*   **Byte Order**: Correctly applies `ntohs` for length and `ntohl` for the IPv4 address, ensuring correctness on little-endian systems.

## Member Reference

**ReadProxyV2Handshake**
Asynchronously reads and validates the PROXY Protocol v2 binary header from an `AsyncSocket`. It performs a two-step read: first for the 16-byte fixed header, then for the variable-length address payload. It validates the magic signature, protocol version (must be 2), command (must be PROXY), and address family (must be TCPv4). If valid, it extracts the source IPv4 address, converts it to host byte order, and returns it via the callback as an `IpAddress`. Any deviation from the expected protocol format results in an `InvalidProtocolBehavior` error logged and returned via the callback. This function must be called before any other data is read from the socket.

---

<!-- machine-true, projected from graph.json -->

## Map — ProxyV2Reader

*Source:* ProxyV2Reader.cpp, ProxyV2Reader.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ReadProxyV2Handshake | function | AsyncSocket._posix/Read, IpAddress/FromIpv4Uint32, Log.Main/Out, NetworkError/NetworkError | realmd_Main/main, WorldSocketMgr/OnNewClientConnected | — |
