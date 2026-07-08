# AsyncSocketAcceptor

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AsyncSocketAcceptor

## Purpose & Responsibilities

`AsyncSocketAcceptor` is a header-only declaration for a class that binds to a TCP address and asynchronously accepts incoming connections. It abstracts platform-specific asynchronous I/O mechanisms: on Windows, it uses IOCP; on Unix-like systems, it inherits from `IO::SystemIoEventReceiver` to integrate with the system event loop. The class enforces strict lifecycle management by throwing in its destructor if `ClosePortAndStopAcceptingNewConnections` is not called first.

## Member-by-Member Behavior

The provided MAP contains no members for this unit. Consequently, no member behavior is documented here. The source code declares the following members, but they are excluded from the Member Reference because they do not appear in the MAP:

- `~AsyncSocketAcceptor`
- `CreateAndBindServer`
- `ClosePortAndStopAcceptingNewConnections`
- `AutoAcceptSocketsUntilClose`
- `OnIoEvent`
- `AsyncSocketAcceptor` (constructor)
- `AcceptOne`
- `OnNewClientToAcceptAvailable`
- `m_acceptorNativeSocket`
- `m_ctx`
- `m_wasClosed`
- `m_currentAcceptTask`
- `m_onNewSocketCallback`

## Cross-Unit Boundaries

As the MAP lists no cross-unit calls, no collaborations are documented. The source code indicates dependencies on `IO::IoContext`, `IO::Networking::SocketDescriptor`, `IO::NetworkError`, and `IO::SystemIoEventReceiver`, but these are not reflected in the provided MAP.

## Data Model

This unit does not interact with any database tables.

## Notable Implementation Details

- **Header-Only Declaration**: The provided source is only the header file. No implementation details (logic, error handling, or platform-specific branching) are visible in the `.cpp` files because none were provided.
- **Empty MAP**: The MAP provided for this unit is empty. All documentation regarding specific members, their signatures, and their interactions is therefore omitted to adhere strictly to the rule of grounding statements in the provided MAP.

## Member Reference

The MAP contains no members.

---

<!-- machine-true, projected from graph.json -->

## Map — AsyncSocketAcceptor

*Source:* AsyncSocketAcceptor.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
