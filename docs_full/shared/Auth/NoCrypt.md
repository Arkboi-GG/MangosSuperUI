# NoCrypt

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

The `NoCrypt` class serves as a **null implementation** or **no-op adapter** for the authentication encryption interface defined by `AuthCrypt`. It exists within the `wowvmangos` codebase to allow the server to operate in a mode where network traffic is not encrypted during the authentication phase.

By providing an identical public interface to `AuthCrypt` but with empty method bodies, `NoCrypt` enables polymorphic usage. Code that expects an encryption object can instantiate `NoCrypt` instead of `AuthCrypt` when encryption is disabled or unnecessary (e.g., for local testing, debugging, or specific configuration modes). This avoids the need for conditional checks (`if (encryption_enabled)`) throughout the networking stack, adhering to the Null Object pattern.

## Member-by-Member Behavior

All members of `NoCrypt` are intentionally empty. They perform no computation, do not access memory beyond their arguments, and do not modify any internal state (as the class holds no private state).

*   **Construction**: The default constructor `NoCrypt()` initializes the object. Since there are no member variables, this is a trivial operation.
*   **Initialization**: `Init()` is a no-op. In `AuthCrypt`, this would likely set up internal counters or buffers; here, it does nothing.
*   **Key Management**: Both overloads of `SetKey` accept key material (either as a `std::vector<uint8>` or a raw pointer with length) but discard it immediately. No key is stored or processed.
*   **Data Transformation**: `DecryptRecv` and `EncryptSend` accept data buffers and lengths but perform no transformation. The input buffers are passed through unchanged. This ensures that if the rest of the system assumes these methods might modify the buffer, the data remains intact and readable as plaintext.

## Cross-Unit Boundaries

According to the provided MAP, `NoCrypt` has **no outgoing calls** to other units and is **not called by** any other units in the cross-reference data. However, its design implies it is a drop-in replacement for `AuthCrypt` in contexts where an encryption object is required by type signature.

*   **Interface Compatibility**: `NoCrypt` mirrors the public API of `AuthCrypt` (defined in the same header). Any unit that holds a pointer or reference to an encryption interface compatible with `AuthCrypt` can theoretically use `NoCrypt`.
*   **Isolation**: Because it performs no work and accesses no external resources, it introduces zero side effects on other parts of the system.

## Data Model

`NoCrypt` interacts with **no database tables**. It is a pure in-memory utility class with no persistence layer involvement.

## Notable Implementation Details

1.  **Null Object Pattern**: The class is a textbook example of the Null Object design pattern. It allows the caller to invoke encryption-related methods without checking if encryption is active. This simplifies control flow in the calling code.
2.  **Stateless Design**: Unlike `AuthCrypt`, which maintains `_send_i`, `_send_j`, `_recv_i`, `_recv_j`, and `_key` state, `NoCrypt` has no private members. This makes it extremely lightweight and thread-safe (since there is no shared mutable state to protect).
3.  **Buffer Integrity**: Although `DecryptRecv` and `EncryptSend` take pointers to `uint8` buffers, they do not read from or write to them. This guarantees that plaintext data remains plaintext after passing through these methods, which is critical for debugging tools that inspect raw packets.
4.  **Header-Only Definition**: All methods are defined inline in `AuthCrypt.h`. There is no corresponding `.cpp` file for `NoCrypt`. This reduces compilation overhead and linkage complexity.

## Member Reference

**NoCrypt**
Default constructor. Initializes the object. Trivial implementation with no body.

**Init**
Method. Empty body. Does not initialize any state.

**SetKey**
Method overload. Accepts `std::vector<uint8> const& key`. Empty body. Discards the key.

**SetKey#2**
Method overload. Accepts `uint8* key` and `size_t len`. Empty body. Discards the key and length.

**DecryptRecv**
Method. Accepts `uint8*` buffer and `size_t` length. Empty body. Does not modify the buffer.

**EncryptSend**
Method. Accepts `uint8*` buffer and `size_t` length. Empty body. Does not modify the buffer.

---

<!-- machine-true, projected from graph.json -->

## Map — NoCrypt

*Source:* AuthCrypt.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| NoCrypt | ctor | — | — | — |
| Init | method | — | — | — |
| SetKey | method | — | — | — |
| SetKey#2 | method | — | — | — |
| DecryptRecv | method | — | — | — |
| EncryptSend | method | — | — | — |
