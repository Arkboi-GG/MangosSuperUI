# IpEndpoint

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# IpEndpoint

`IpEndpoint` is a lightweight aggregate in `IO::Networking` holding an `IO::Networking::IpAddress` and a `uint16_t` port. It represents a network socket address, delegating IP formatting to `IpAddress` and providing simple serialization.

## Purpose & Responsibilities

1.  **Aggregation**: Bundles an IP address and port into a single passable unit.
2.  **Serialization**: Converts the endpoint to a string (`IP:PORT` or `[IPv6]:PORT`) via `toString()`.

It performs no validation, I/O, or parsing.

## Member-by-Member Behavior

### Construction

*   **`IpEndpoint()`**: Default constructor. Initializes `ip` to its default state and `port` to `0`. Creates a null-like endpoint for stack allocation or placeholder use.
*   **`IpEndpoint(IO::Networking::IpAddress ip, uint16_t port)`**: Parameterized constructor. Moves the `IpAddress` into `ip` and copies the port. Used to create valid endpoints from parsed or configured data.

### Formatting

*   **`toString()`**: Returns a `std::string` concatenating `ip.ToString()` (delegating to `IpAddress`), a colon, and the decimal port. Relies on `IpAddress` to bracket IPv6 addresses correctly.

## Cross-Unit Boundaries

`IpEndpoint` makes no outgoing calls. It is constructed or serialized by:

1.  **`AsyncSocketAcceptor_posix/OnNewClientToAcceptAvailable`**: Constructs `IpEndpoint` to capture a new client’s remote address upon TCP acceptance.
2.  **`RealmList/UpdateRealm`**: Constructs `IpEndpoint` to represent game server locations during realm configuration updates.
3.  **`AuthSocket/LoadRealmlistAndWriteIntoBuffer`**: Calls `toString()` to serialize realm addresses into the realmlist packet for clients.

## Data Model

`IpEndpoint` interacts with no database tables. It is a transient in-memory structure.

## Notable Implementation Details

*   **Delegation**: Assumes `IpAddress::ToString()` handles IPv6 bracketing. Failure there yields ambiguous strings (e.g., `::1:3724`).
*   **Move Semantics**: The parameterized constructor uses `std::move` for `IpAddress`, optimizing potential copies despite `IpAddress`’s small size.
*   **No Validation**: Constructors accept any `uint16_t` port and any `IpAddress`; callers must ensure validity.
*   **Equality**: A `friend` `operator==` is declared for comparison, implemented externally.

## Member Reference

**IpEndpoint**
Default constructor. Initializes `ip` to its default state and `port` to `0`.

**IpEndpoint#2**
Parameterized constructor. Moves an `IO::Networking::IpAddress` and copies a `uint16_t` port. Called by `AsyncSocketAcceptor_posix/OnNewClientToAcceptAvailable` and `RealmList/UpdateRealm`.

**toString**
Returns a `std::string` in `IP:PORT` format by calling `ip.ToString()` and appending the port. Called by `AuthSocket/LoadRealmlistAndWriteIntoBuffer`.

---

<!-- machine-true, projected from graph.json -->

## Map — IpEndpoint

*Source:* IpAddress.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| IpEndpoint | ctor | — | — | — |
| IpEndpoint#2 | ctor | — | AsyncSocketAcceptor_posix/OnNewClientToAcceptAvailable, RealmList/UpdateRealm | — |
| toString | method | — | AuthSocket/LoadRealmlistAndWriteIntoBuffer | — |
