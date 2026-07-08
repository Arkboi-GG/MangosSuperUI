# Internal

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# IO::Networking::Internal

## Purpose & Responsibilities

`IO::Networking::Internal` provides platform-agnostic wrappers for low-level networking primitives: converting between `IO::Networking::IpAddress` and native `in_addr` structures, and closing native socket handles. It abstracts OS-specific API differences (Windows vs. POSIX/BSD) and compatibility constraints (Windows XP, BSD macro conflicts). The unit contains no state and interacts with no database tables.

## Member-by-Member Behavior

### IP Address Conversion

**inet_ntop** converts a native `in_addr` to an `IO::Networking::IpAddress`.
*   **Windows:** Manually formats the IPv4 string via `snprintf` (interpreting bytes as octets) because `inet_ntop` is unavailable on XP. Parses the string via `IO::Networking::IpAddress::TryParseFromString`.
*   **Linux/macOS:** Uses `::inet_ntop` to convert binary to string, then parses via `TryParseFromString`.
*   **BSD:** Undefines conflicting macros, calls `__inet_ntop`, then parses via `TryParseFromString`.
*   **Validation:** Asserts the resulting `IpAddress` is valid.

**inet_pton** converts an `IO::Networking::IpAddress` to a native `in_addr`.
*   **Precondition:** Asserts the input is IPv4.
*   **Windows:** Directly assigns the internal 32-bit representation (via `_getInternalIPv4ReprAsUint32()`) to `in_addr.s_addr` after applying `::htonl` for network byte order.
*   **Linux/macOS/BSD:** Converts the `IpAddress` to a string via `ToString()`, then uses `::inet_pton` (or `__inet_pton` on BSD) to populate the `in_addr` struct. Asserts the system call returns 1.

### Socket Management

**CloseSocket** closes a native socket handle.
*   **Windows:** Calls `::closesocket()`.
*   **POSIX:** Calls `::close()`.

## Cross-Unit Boundaries

### Calls Out
*   **`inet_ntop` → `IO::Networking::IpAddress::TryParseFromString`:** Parses the converted IP string into an `IpAddress` object.
*   **`inet_ntop` → `Errors::PrintStacktraceAndThrow`:** Triggered if the `MANGOS_ASSERT` on parsing fails.
*   **`inet_pton` → `IO::Networking::IpAddress::GetType` / `ToString`:** Validates IP version and retrieves the string representation for POSIX/BSD conversion.
*   **`inet_pton` → `Errors::PrintStacktraceAndThrow`:** Triggered if the `MANGOS_ASSERT` on the system `inet_pton` return value fails.

### Called By
*   **`inet_ntop` ← `AsyncSocketAcceptor_posix::OnNewClientToAcceptAvailable` / `DNS::ResolveDomainAll`:** Converts raw `in_addr` from `accept()` or DNS resolution into `IpAddress` objects.
*   **`CloseSocket` ← `SocketConnector::ConnectBlocking` / `SocketDescriptor::CloseSocket`:** Releases OS socket resources.
*   **`inet_pton` ← `AsyncSocketAcceptor_posix::CreateAndBindServer` / `SocketConnector::ConnectBlocking`:** Converts `IpAddress` objects into native `in_addr` for `bind()` or `connect()`.

## Data Model

This unit does not interact with any database tables.

## Notable Implementation Details

1.  **Windows XP Compatibility:** Avoids `inet_pton`/`inet_ntop` on Windows, implementing manual formatting/parsing instead.
2.  **BSD Macro Conflicts:** Undefines `inet_ntop`/`inet_pton` on FreeBSD/NetBSD/OpenBSD to prevent ambiguity with macros, ensuring correct calls to `__inet_ntop`/`__inet_pton`.
3.  **Assertion on Parsing:** Uses `MANGOS_ASSERT` to verify success in both conversion directions. Assumes input data is strictly valid IPv4; garbage data causes aborts in debug builds.
4.  **Byte Order Handling:** On Windows, `inet_pton` manually applies `::htonl` to ensure `in_addr.s_addr` is in network byte order. POSIX implementations rely on `inet_pton` to handle this after string conversion.

## Member Reference

**inet_ntop**: Converts a native `in_addr` pointer to an `IO::Networking::IpAddress` object. Uses manual string formatting on Windows for XP compatibility, `::inet_ntop` on Linux/macOS, and `__inet_ntop` on BSD variants. Parses the resulting string via `IO::Networking::IpAddress::TryParseFromString`. Asserts validity of the result.

**CloseSocket**: Closes a native socket handle. Calls `::closesocket()` on Windows and `::close()` on POSIX systems.

**inet_pton**: Converts an `IO::Networking::IpAddress` object to a native `in_addr` structure. Asserts the IP is IPv4. On Windows, directly assigns the 32-bit internal representation (converted to network byte order via `::htonl`). On Linux/macOS/BSD, converts the IP to a string via `ToString()` and uses `::inet_pton` or `__inet_pton` to populate the `in_addr` struct, asserting success.

---

<!-- machine-true, projected from graph.json -->

## Map — Internal

*Source:* Internal.cpp, Internal.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| inet_ntop | function | Errors/PrintStacktraceAndThrow, IpAddress/TryParseFromString | AsyncSocketAcceptor_posix/OnNewClientToAcceptAvailable, DNS/ResolveDomainAll | — |
| CloseSocket | function | — | SocketConnector/ConnectBlocking, SocketDescriptor/CloseSocket | — |
| inet_pton | function | Errors/PrintStacktraceAndThrow, IpAddress/GetType, IpAddress/ToString | AsyncSocketAcceptor_posix/CreateAndBindServer, SocketConnector/ConnectBlocking | — |
