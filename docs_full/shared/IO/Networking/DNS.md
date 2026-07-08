# DNS

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# DNS Resolution Utilities (`IO::Networking::DNS`)

## Purpose & Responsibilities

The `IO::Networking::DNS` unit provides platform-agnostic wrappers for hostname retrieval and domain name resolution. It abstracts OS-specific APIs (`WinSock2` on Windows, `netdb` on POSIX) into a consistent C++ interface returning `IpAddress` objects. It handles retrieving the local hostname, resolving domains (or validating IP strings) into address lists, and selecting a single IP from those lists.

## Member-by-Member Behavior

### Hostname Retrieval

**`GetOwnHostname`**
Retrieves the local hostname using `::gethostname` into a 1024-byte stack buffer. On failure, it logs the system error (via `SystemErrorToString/SystemErrorToString` from `SystemErrorToString`) through `Log.Main/Out` (from `Log`) and triggers a fatal assertion (`MANGOS_ASSERT`). Called by `WorldSocket/GetServerAddresses`.

### Domain Resolution

**`ResolveDomainAll`**
Resolves a `domainName` string into a `std::vector<IpAddress>`.
1.  **IP Shortcut:** Attempts to parse the input as an IP using `IpAddress/TryParseFromString` (from `IpAddress`). If successful, it validates the type via `IpAddress/GetType` and returns a single-element vector, bypassing DNS.
2.  **DNS Lookup:** If not an IP, it calls `::getaddrinfo` with hints for TCP streams and the requested address family. On failure, it logs the error (via `Log.Main/Out` and `SystemErrorToString/SystemErrorToString`) and returns an empty vector.
3.  **Result Processing:** Iterates the `addrinfo` linked list. For `AF_INET` entries, it converts the binary address to an `IpAddress` using `Internal/inet_ntop` (from `Internal`). For `AF_INET6` entries, it triggers a fatal assertion because IPv6 support is incomplete (conversion code is commented out). It frees the `addrinfo` memory before returning. Called by `WorldSocket/GetServerAddresses`.

**`ResolveDomainSingle`**
A convenience wrapper returning a single `nonstd::optional<IpAddress>`. It delegates to `ResolveDomainAll`. If the result list is empty, it returns `nullopt`. Otherwise, it selects an IP based on `SelectionStrategy`:
*   `First`: Returns the first element.
*   `Random`: Uses `shared_Util/urand` (from `shared_Util`) to pick a random index, providing basic load balancing.
Called by `RealmList/UpdateRealms`.

## Cross-Unit Boundaries

*   **`WorldSocket/GetServerAddresses`**: Calls `GetOwnHostname` and `ResolveDomainAll` to determine the server's identity and bind addresses during initialization.
*   **`RealmList/UpdateRealms`**: Calls `ResolveDomainSingle` to resolve realm hostnames to IPs.
*   **`IpAddress`**: Used for parsing input (`TryParseFromString`), validating types (`GetType`), and as the return type.
*   **`Internal`**: Provides `inet_ntop` to convert raw `sockaddr_in` structures into `IpAddress` objects.
*   **`Log`**: Receives error messages via `Main/Out` when DNS or hostname operations fail.
*   **`SystemErrorToString`**: Converts OS error codes (`errno`/`WSAGetLastError`) into readable strings for logging.
*   **`shared_Util`**: Provides `urand` for random IP selection in `ResolveDomainSingle`.
*   **`Errors`**: Implicitly involved via `MANGOS_ASSERT` macros which may trigger stack traces on failure.

## Data Model

This unit does not interact with any database tables.

## Notable Implementation Details

*   **IPv6 Unsupported**: Despite configuring `addrinfo` for IPv6, `ResolveDomainAll` asserts on `AF_INET6` results. The IPv6 conversion logic is commented out, making IPv6 resolution fatal in debug builds.
*   **IP String Optimization**: `ResolveDomainAll` avoids DNS overhead if the input is already a valid IP string.
*   **Platform Specifics**: Uses preprocessor checks to include correct headers and retrieve error codes for Windows vs. POSIX systems.
*   **Memory Safety**: Correctly calls `freeaddrinfo` to clean up `getaddrinfo` results.

## Member Reference

**GetOwnHostname**
Retrieves the local hostname via `::gethostname`. Logs errors via `Log.Main/Out` and `SystemErrorToString/SystemErrorToString` on failure, then asserts. Called by `WorldSocket/GetServerAddresses`.

**ResolveDomainAll**
Resolves a domain or IP string to a vector of `IpAddress`. Parses direct IPs via `IpAddress/TryParseFromString`; otherwise uses `::getaddrinfo`. Converts IPv4 results via `Internal/inet_ntop`. Asserts on IPv6 results. Logs errors via `Log.Main/Out` and `SystemErrorToString/SystemErrorToString`. Called by `WorldSocket/GetServerAddresses`.

**ResolveDomainSingle**
Wraps `ResolveDomainAll` to return a single `IpAddress`. Selects via `SelectionStrategy`: `First` takes the front element; `Random` uses `shared_Util/urand`. Returns `nullopt` if empty. Called by `RealmList/UpdateRealms`.

---

<!-- machine-true, projected from graph.json -->

## Map — DNS

*Source:* DNS.cpp, DNS.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GetOwnHostname | function | Errors/PrintStacktraceAndThrow, Log.Main/Out, SystemErrorToString/SystemErrorToString | WorldSocket/GetServerAddresses | — |
| ResolveDomainAll | function | Errors/PrintStacktraceAndThrow, Internal/inet_ntop, IpAddress/GetType, IpAddress/TryParseFromString, Log.Main/Out, SystemErrorToString/SystemErrorToString | WorldSocket/GetServerAddresses | — |
| ResolveDomainSingle | function | Errors/PrintStacktraceAndThrow, shared_Util/urand | RealmList/UpdateRealms | — |
