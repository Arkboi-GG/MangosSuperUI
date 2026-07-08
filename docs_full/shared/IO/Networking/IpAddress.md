<!-- provenance: verbose -->
# IpAddress

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`IO::Networking::IpAddress` is a value-type wrapper for IP addresses within the MaNGOS network stack. It encapsulates either an IPv4 address (`uint32_t`) or an IPv6 address (`std::array<uint16_t, 8>`), providing parsing, serialization, and equality comparison.

Key characteristics:
- **IPv6 Parsing is Unimplemented:** `TryParseFromString` detects IPv6 syntax but returns `nullopt`. IPv6 addresses can only be constructed via direct memory manipulation or future implementation.
- **Cached Serialization:** `ToString()` returns a reference to a pre-computed string (`m_cachedToString`) to avoid repeated formatting costs during logging.
- **Strict IPv4 Parsing:** `TryParseFromString` manually validates IPv4 octets (0–255) and format, rejecting trailing characters or whitespace.

The unit also defines `IO::Networking::IpEndpoint`, an aggregate of `IpAddress` and `uint16_t` port, with its own equality operator.

## Member-by-Member Behavior

### Construction and Parsing

**`FromIpv4Uint32`**
Static factory creating an `IpAddress` from a raw `uint32_t` IPv4 value. Sets type to `IPv4`, stores the integer, and calls `UpdateCachedString`. Used by `MaNGOSsoap/SoapThreadBody` and `ProxyV2Reader/ReadProxyV2Handshake`.

**`TryParseFromString`**
Static factory attempting to parse a string into an `IpAddress`.
- **IPv4:** Manually parses four dot-separated decimal octets using `std::strtoll`. Validates range [0, 255] and strict formatting (no trailing chars). Constructs the `uint32_t` by shifting and OR-ing octets.
- **IPv6:** Detects `[` prefix but immediately returns `nullopt` (stubbed).
- Returns `nonstd::optional<IpAddress>`. Called by `AsyncSocketAcceptor_posix/CreateAndBindServer`, `DNS/ResolveDomainAll`, `Internal/inet_ntop`, `RealmList/UpdateRealms`, and `shared_Util/IsIPAddress`.

### Accessors and Utilities

**`GetType`**
Returns the `Type` enum (`IPv4` or `IPv6`). Called by `DNS/ResolveDomainAll`, `Internal/inet_pton`, `RealmList/GetAddressForClient`, and `shared_IO_Networking_Utils/IsInSameSubnet`.

**`_getInternalIPv4ReprAsUint32`**
Returns the raw `uint32_t` IPv4 value. Asserts (`MANGOS_ASSERT`) that the type is `IPv4`; calls `Errors/PrintStacktraceAndThrow` on failure. Called by `RealmList/UpdateRealms` and `shared_IO_Networking_Utils/IsInSameSubnet`.

**`ToString`**
Inline accessor returning a const reference to `m_cachedToString`. Called by `Internal/inet_pton`, `MaNGOSsoap/SoapThreadBody`, `realmd_Main/main`, `WorldSocket/GetServerAddresses`, and `WorldSocketMgr/OnNewClientConnected`.

**`UpdateCachedString`**
Private method regenerating `m_cachedToString`.
- **IPv4:** Converts integer to dotted decimal.
- **IPv6:** Implements RFC 5952-style compression: finds the longest zero-segment sequence, replaces it with `::` (or `:` at boundaries), and outputs uppercase hex segments. Called internally by `FromIpv4Uint32` and `TryParseFromString`.

### Equality Operators

**`operator==` (IpAddress)**
Free function comparing two `IpAddress` objects. Checks type match, then compares underlying data (`uint32_t` for IPv4, `std::array` for IPv6).

**`operator==#2` (IpEndpoint)**
Free function comparing two `IpEndpoint` objects. Returns true if both `ip` and `port` are equal.

## Cross-Unit Boundaries

### Outgoing Calls
- **`Errors/PrintStacktraceAndThrow`**: Called by `_getInternalIPv4ReprAsUint32` only if `MANGOS_ASSERT` fails (programming error: requesting IPv4 int from IPv6 address).

### Incoming Calls (Consumers)
- **`MaNGOSsoap/SoapThreadBody`**: Uses `FromIpv4Uint32` and `ToString` for SOAP serialization.
- **`ProxyV2Reader/ReadProxyV2Handshake`**: Uses `FromIpv4Uint32` for proxy handshake data.
- **`AsyncSocketAcceptor_posix/CreateAndBindServer`**: Uses `TryParseFromString` to bind sockets.
- **`DNS/ResolveDomainAll`**: Uses `TryParseFromString` and `GetType` for DNS results.
- **`Internal/inet_ntop` / `Internal/inet_pton`**: Wrappers using `ToString` and `TryParseFromString`.
- **`RealmList/UpdateRealms` / `RealmList/GetAddressForClient`**: Uses `_getInternalIPv4ReprAsUint32` and `GetType` for realm routing.
- **`shared_Util/IsIPAddress` / `shared_IO_Networking_Utils/IsInSameSubnet`**: Uses `TryParseFromString` and `_getInternalIPv4ReprAsUint32` for validation/subnet math.
- **`WorldSocket/GetServerAddresses` / `WorldSocketMgr/OnNewClientConnected`**: Uses `ToString` for logging.
- **`realmd_Main/main`**: Uses `ToString` for config logging.

## Data Model

This unit does not interact with any database tables. All operations are in-memory.

## Notable Implementation Details

- **IPv6 Parsing Stub:** `TryParseFromString` returns `nullopt` for any IPv6 input. IPv6 support is limited to storage and serialization.
- **Manual IPv4 Parser:** Avoids `inet_pton`. Strictly rejects trailing whitespace/characters. Assumes big-endian byte order in the resulting `uint32_t` (first octet in MSB).
- **Cache Invalidation:** `m_cachedToString` is updated only in `FromIpv4Uint32`, `TryParseFromString`, and explicit `UpdateCachedString` calls. Direct modification of `m_address` (private) would stale the cache, but no public setters exist.
- **IPv6 Compression:** `UpdateCachedString` picks the *first* longest zero-sequence for `::` compression.

## Member Reference

**`FromIpv4Uint32`**
Static factory. Creates `IpAddress` from `uint32_t` IPv4. Sets type, stores value, updates cache. Called by `MaNGOSsoap/SoapThreadBody`, `ProxyV2Reader/ReadProxyV2Handshake`.

**`TryParseFromString`**
Static factory. Parses string to `IpAddress`. IPv4: manual octet validation (0-255), strict format. IPv6: returns `nullopt` (stub). Updates cache on success. Called by `AsyncSocketAcceptor_posix/CreateAndBindServer`, `DNS/ResolveDomainAll`, `Internal/inet_ntop`, `RealmList/UpdateRealms`, `shared_Util/IsIPAddress`.

**`ToString`**
Inline accessor. Returns const ref to cached string. Called by `Internal/inet_pton`, `MaNGOSsoap/SoapThreadBody`, `realmd_Main/main`, `WorldSocket/GetServerAddresses`, `WorldSocketMgr/OnNewClientConnected`.

**`GetType`**
Accessor. Returns `Type` enum. Called by `DNS/ResolveDomainAll`, `Internal/inet_pton`, `RealmList/GetAddressForClient`, `shared_IO_Networking_Utils/IsInSameSubnet`.

**`_getInternalIPv4ReprAsUint32`**
Accessor. Returns raw `uint32_t` IPv4. Asserts type is `IPv4`; calls `Errors/PrintStacktraceAndThrow` on failure. Called by `RealmList/UpdateRealms`, `shared_IO_Networking_Utils/IsInSameSubnet`.

**`UpdateCachedString`**
Private method. Regenerates `m_cachedToString`. IPv4: dotted decimal. IPv6: zero-compressed uppercase hex. Called by `FromIpv4Uint32`, `TryParseFromString`.

**`operator==`**
Free function. Compares two `IpAddress` objects (type match, then data).

**`operator==#2`**
Free function. Compares two `IpEndpoint` objects (`ip` and `port`).

---

<!-- machine-true, projected from graph.json -->

## Map — IpAddress

*Source:* IpAddress.cpp, IpAddress.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| FromIpv4Uint32 | method | — | MaNGOSsoap/SoapThreadBody, ProxyV2Reader/ReadProxyV2Handshake | — |
| TryParseFromString | method | — | AsyncSocketAcceptor_posix/CreateAndBindServer, DNS/ResolveDomainAll, Internal/inet_ntop, RealmList/UpdateRealms, shared_Util/IsIPAddress | — |
| ToString | method | — | Internal/inet_pton, MaNGOSsoap/SoapThreadBody, realmd_Main/main, WorldSocket/GetServerAddresses, WorldSocketMgr/OnNewClientConnected | — |
| GetType | method | — | DNS/ResolveDomainAll, Internal/inet_pton, RealmList/GetAddressForClient, shared_IO_Networking_Utils/IsInSameSubnet | — |
| _getInternalIPv4ReprAsUint32 | method | Errors/PrintStacktraceAndThrow | RealmList/UpdateRealms, shared_IO_Networking_Utils/IsInSameSubnet | — |
| UpdateCachedString | method | — | — | — |
| operator== | function | — | — | — |
| operator==#2 | function | — | — | — |
