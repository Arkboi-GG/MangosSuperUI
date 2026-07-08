# ReadableBuffer

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`ReadableBuffer` (defined in `ReadableBuffer.h`) is a type-erased wrapper that holds a reference to contiguous byte data for asynchronous network transmission. It serves as the payload container for the `AsyncSocket` subsystem, allowing sockets to queue outgoing data without depending on the specific underlying storage type (`ByteBuffer`, `std::vector`, or raw pointer).

Key characteristics:
1.  **Read-Only Interface:** Exposes only `GetSize()` and `GetPtr()`, providing a uniform `uint8 const*` view.
2.  **Shared Ownership:** Stores `std::shared_ptr` references to underlying containers, ensuring data validity during async writes even if the creator goes out of scope.
3.  **Manual Memory Management:** Uses a C-style union (`BufferUnion`) to store different `shared_ptr` types. Because unions cannot automatically manage non-trivial destructors, `ReadableBuffer` uses placement `new` and explicit `Destruct()` calls.
4.  **Cached Metadata:** Pointer and size are cached at construction. The underlying buffer must not be modified while held, as the cache would become invalid.

This class mimics `std::variant` functionality for pre-C++17 or performance-critical contexts.

## Member-by-Member Behavior

### Construction
Constructors initialize cached `m_ptr` and `m_size`, then use placement `new` to construct the appropriate `std::shared_ptr` in `m_buffer` based on `BufferType`.

*   **Default/Null:** `ReadableBuffer()` and `ReadableBuffer(std::nullptr_t)` initialize to empty (`Unset`). Called by `AsyncSocket.Main/AsyncSocket` (`ReadableBuffer#2`).
*   **ByteBuffer:** Accepts `shared_ptr<ByteBuffer const>` (lvalue/rvalue). Sets `BufferType::ByteBuffer`. Called by `WorldSocket/HandleResultOfAsyncWrite` (`ReadableBuffer#15`).
*   **Vectors:** Accepts `shared_ptr` to `vector<uint8>`, `vector<int8>`, or `vector<char>`. For `int8`/`char`, pointers are `reinterpret_cast` to `uint8 const*`. Heavily used by `AuthSocket` handlers (`_HandleLogonChallenge`, `_HandleRealmList`, etc.) via `ReadableBuffer#6` and `RASocket/DoRecvIncomingData` via `ReadableBuffer#8`.
*   **Raw Pointer:** Accepts `shared_ptr<uint8 const>` and `size_t`. Sets `BufferType::PtrU8`. Used by `AuthSocket/RepeatInternalXferLoop`, `RASocket/SendAndDisconnect`, and `RASocket/SendAndRecvNextInput` (`ReadableBuffer#16`).
*   **Move-from-Value:** Constructors taking `ByteBuffer&&` or `vector<T>&&` wrap the value in a `shared_ptr` before delegating.

### Assignment and Destruction
*   **`operator=` (Copy/Move):** Updates `m_ptr`, `m_size`, and `m_type`, then uses placement `new` to overwrite the union. **Gotcha:** If the destination holds a different `BufferType` than the source, the old `shared_ptr` destructor is **not called**, causing a memory leak and potential undefined behavior. Callers must ensure type compatibility or call `Destruct()` first. `operator=#2` (copy) is called by `AsyncSocket._posix/Write`; `operator=#3` (move) by `AsyncSocket._posix/PerformNonBlockingWrite` and `StopPendingTransactionsAndForceClose`.
*   **`operator=(std::nullptr_t)`:** Resets state to `Unset` without destructing old content.
*   **`Destruct()`:** Explicitly calls the destructor of the active `shared_ptr` member and resets `m_type` to `Unset`.
*   **`~ReadableBuffer`:** Delegates to `Destruct()`.

### Accessors
*   **`GetSize()` / `GetPtr()`:** Return cached metadata. Called by `AsyncSocket._posix/PerformNonBlockingWrite` and `Write` for `send()` syscalls.

## Cross-Unit Boundaries

1.  **Protocol Handlers → ReadableBuffer:** `AuthSocket`, `WorldSocket`, and `RASocket` construct `ReadableBuffer` instances to package data for transmission, isolating the socket layer from storage specifics.
2.  **ReadableBuffer → AsyncSocket:** `AsyncSocket._posix` methods call `GetSize()` and `GetPtr()` to retrieve data for network transmission. `operator=` variants are called during write completion or error handling to reset/update buffer states.

## Data Model

This unit does not interact with any database tables.

## Notable Implementation Details

1.  **Assignment Leak Risk:** As noted, `operator=` does not destroy the old union member if types differ. This is a critical maintenance hazard.
2.  **Shallow Copy:** Copies share the underlying `shared_ptr`. Modifying data via another reference affects the buffer. The contract forbids modification.
3.  **Manual Union Lifecycle:** `BufferUnion` has trivial constructors/destructors; `ReadableBuffer` manages the actual object lifetimes via `Destruct()` and placement `new`.

## Member Reference

**ReadableBuffer** (ctor): Default constructor. Initializes to empty/unset state.
**ReadableBuffer#15** (ctor): Constructs from `shared_ptr<ByteBuffer const>`. Called by `WorldSocket/HandleResultOfAsyncWrite`.
**ReadableBuffer#6** (ctor): Constructs from `shared_ptr<vector<uint8> const>`. Called by `AuthSocket` handlers (`_HandleLogonChallenge`, `_HandleLogonProof__PostRecv`, `_HandleLogonProof__PostRecv_HandleInvalidVersion`, `_HandleRealmList`, `_HandleReconnectChallenge`, `_HandleReconnectProof`).
**ReadableBuffer#13** (ctor): Constructs from `shared_ptr<vector<int8> const>`.
**ReadableBuffer#4** (ctor): Constructs from `shared_ptr<vector<char> const>`.
**ReadableBuffer#14** (ctor): Constructs from `shared_ptr<uint8 const>` with size.
**ReadableBuffer#5** (ctor): Constructs from `shared_ptr<vector<uint8>>` (rvalue).
**ReadableBuffer#12** (ctor): Constructs from `shared_ptr<vector<int8>>` (rvalue).
**ReadableBuffer#3** (ctor): Constructs from `shared_ptr<vector<char>>` (rvalue).
**ReadableBuffer#16** (ctor): Constructs from `shared_ptr<uint8 const>` with size. Called by `AuthSocket/RepeatInternalXferLoop`, `RASocket/SendAndDisconnect`, `RASocket/SendAndRecvNextInput`.
**ReadableBuffer#10** (ctor): Constructs from `ByteBuffer&&` (moves into shared_ptr).
**ReadableBuffer#8** (ctor): Constructs from `vector<uint8>&&` (moves into shared_ptr). Called by `RASocket/DoRecvIncomingData`.
**ReadableBuffer#9** (ctor): Constructs from `vector<int8>&&` (moves into shared_ptr).
**ReadableBuffer#7** (ctor): Constructs from `vector<char>&&` (moves into shared_ptr).
**ReadableBuffer#17** (ctor): Constructs from `nullptr_t`.
**operator=#3** (method): Move assignment operator. Called by `AsyncSocket._posix/PerformNonBlockingWrite`, `AsyncSocket._posix/StopPendingTransactionsAndForceClose`.
**Destruct** (method): Manually destroys active shared_ptr in union and resets type to Unset.
**~ReadableBuffer** (dtor): Calls `Destruct()`.
**ReadableBuffer#11** (ctor): Copy constructor.
**operator=#2** (method): Copy assignment operator. Called by `AsyncSocket._posix/Write`.
**ReadableBuffer#2** (ctor): Default constructor. Called by `AsyncSocket.Main/AsyncSocket`.
**operator=** (method): Assignment from `nullptr_t`. Resets to unset.
**GetSize** (method): Returns cached `m_size`. Called by `AsyncSocket._posix/PerformNonBlockingWrite`, `AsyncSocket._posix/Write`.
**GetPtr** (method): Returns cached `m_ptr`. Called by `AsyncSocket._posix/PerformNonBlockingWrite`, `AsyncSocket._posix/Write`.
**BufferUnion** (ctor): Default constructor for union member.
**~BufferUnion** (dtor): Default destructor for union member.

---

<!-- machine-true, projected from graph.json -->

## Map — ReadableBuffer

*Source:* ReadableBuffer.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ReadableBuffer | ctor | — | — | — |
| ReadableBuffer#15 | ctor | — | WorldSocket/HandleResultOfAsyncWrite | — |
| ReadableBuffer#6 | ctor | — | AuthSocket/_HandleLogonChallenge, AuthSocket/_HandleLogonProof__PostRecv, AuthSocket/_HandleLogonProof__PostRecv_HandleInvalidVersion, AuthSocket/_HandleRealmList, AuthSocket/_HandleReconnectChallenge, AuthSocket/_HandleReconnectProof | — |
| ReadableBuffer#13 | ctor | — | — | — |
| ReadableBuffer#4 | ctor | — | — | — |
| ReadableBuffer#14 | ctor | — | — | — |
| ReadableBuffer#5 | ctor | — | — | — |
| ReadableBuffer#12 | ctor | — | — | — |
| ReadableBuffer#3 | ctor | — | — | — |
| ReadableBuffer#16 | ctor | — | AuthSocket/RepeatInternalXferLoop, RASocket/SendAndDisconnect, RASocket/SendAndRecvNextInput | — |
| ReadableBuffer#10 | ctor | — | — | — |
| ReadableBuffer#8 | ctor | — | RASocket/DoRecvIncomingData | — |
| ReadableBuffer#9 | ctor | — | — | — |
| ReadableBuffer#7 | ctor | — | — | — |
| ReadableBuffer#17 | ctor | — | — | — |
| operator=#3 | method | — | AsyncSocket._posix/PerformNonBlockingWrite, AsyncSocket._posix/StopPendingTransactionsAndForceClose | — |
| Destruct | method | — | — | — |
| ~ReadableBuffer | dtor | — | — | — |
| ReadableBuffer#11 | ctor | — | — | — |
| operator=#2 | method | — | AsyncSocket._posix/Write | — |
| ReadableBuffer#2 | ctor | — | AsyncSocket.Main/AsyncSocket | — |
| operator= | method | — | — | — |
| GetSize | method | — | AsyncSocket._posix/PerformNonBlockingWrite, AsyncSocket._posix/Write | — |
| GetPtr | method | — | AsyncSocket._posix/PerformNonBlockingWrite, AsyncSocket._posix/Write | — |
| BufferUnion | ctor | — | — | — |
| ~BufferUnion | dtor | — | — | — |
