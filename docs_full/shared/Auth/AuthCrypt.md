# AuthCrypt

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AuthCrypt

## Purpose & Responsibilities

`AuthCrypt` implements a lightweight, stateful stream cipher used to encrypt and decrypt the initial bytes of network packets during the World of Warcraft authentication phase. It operates on fixed-length prefixes: the first 4 bytes of outgoing data (`CRYPTED_SEND_LEN`) and the first 6 bytes of incoming data (`CRYPTED_RECV_LEN`). The algorithm uses a session-specific key and maintains internal state counters (`_i` and `_j`) to ensure continuity across sequential packet operations.

A companion class, `NoCrypt`, is defined in the header with an identical interface but empty implementations. This allows the networking stack (`WorldSocket`) to remain agnostic to whether encryption is active, enabling runtime switching between encrypted and unencrypted modes without code changes.

## Member-by-Member Behavior

### Initialization and State

*   **`AuthCrypt` (ctor)**: Sets the `_initialized` flag to `false`. Internal state variables are default-initialized.
*   **`Init`**: Resets the send and receive state counters (`_send_i`, `_send_j`, `_recv_i`, `_recv_j`) to zero and sets `_initialized` to `true`. This prepares the cipher for a new session. Called by `WorldSocket::_HandleAuthSession`.
*   **`IsInitialized`**: Returns the boolean `_initialized` status. Allows callers to verify setup before cryptographic operations.
*   **`~AuthCrypt` (dtor)**: Default destructor. No resource cleanup is required.

### Key Management

*   **`SetKey` (vector overload)**: Assigns the provided `std::vector<uint8>` to the internal `_key`. If the input key is empty, it resizes `_key` to 1 element to prevent division-by-zero errors in the cipher loop. Called by `WorldSocket::_HandleAuthSession`.
*   **`SetKey#2` (pointer overload)**: Copies `len` bytes from the raw `key` pointer into `_key`. Like the vector overload, it ensures `_key` is non-empty by resizing to 1 if the resulting vector is empty. This overload is available for direct memory handling, though `WorldSocket` primarily uses the vector version.

### Cryptographic Operations

*   **`EncryptSend`**: Encrypts the first 4 bytes of the input buffer in-place. For each byte, it XORs the data with the key at index `_send_i`, adds `_send_j`, stores the result, and updates `_send_j` to the new result. `_send_i` increments and wraps around the key size. Returns immediately if not initialized or if the buffer is too short. Called by `WorldSocket::HandleResultOfAsyncWrite`.
*   **`DecryptRecv`**: Decrypts the first 6 bytes of the input buffer in-place. For each byte, it subtracts `_recv_j` from the data, XORs with the key at index `_recv_i`, stores the result, and updates `_recv_j` to the original encrypted byte. `_recv_i` increments and wraps around the key size. Returns immediately if not initialized or if the buffer is too short. Called by `WorldSocket::DoRecvIncomingData`.

## Cross-Unit Boundaries

`AuthCrypt` has no outbound dependencies. It is exclusively driven by `WorldSocket`:

1.  **`WorldSocket::_HandleAuthSession`**: Calls `AuthCrypt::Init()` to reset state and `AuthCrypt::SetKey()` to load the session key.
2.  **`WorldSocket::DoRecvIncomingData`**: Calls `AuthCrypt::DecryptRecv()` on incoming buffers to reveal plaintext headers.
3.  **`WorldSocket::HandleResultOfAsyncWrite`**: Calls `AuthCrypt::EncryptSend()` on outgoing buffers before transmission.

## Data Model

This unit does not interact with any database tables. All state is held in memory within the `AuthCrypt` instance.

## Notable Implementation Details

*   **Fixed-Length Prefixes**: Only the first 4 bytes (send) and 6 bytes (recv) are processed. The remainder of the packet is untouched.
*   **In-Place Modification**: Both `EncryptSend` and `DecryptRecv` modify the input buffer directly. Callers must ensure the buffer is mutable and sufficiently sized.
*   **State Persistence**: The `_i` and `_j` counters persist across calls. `AuthCrypt` must be used sequentially for a given stream; resetting requires `Init()`.
*   **Empty Key Safeguard**: `SetKey` methods resize the key to 1 if empty, preventing undefined behavior in modulo operations. This is a defensive measure against invalid inputs.
*   **Algorithm Symmetry**: Encryption uses `(data ^ key[i]) + j`; decryption uses `(data - j) ^ key[i]`. These are inverse operations for `uint8` arithmetic.

## Member Reference

**AuthCrypt**: Constructor; initializes `_initialized` to `false`.
**Init**: Resets send/recv counters to 0 and sets `_initialized` to `true`.
**DecryptRecv**: Decrypts the first 6 bytes of the input buffer in-place using the current key and state; returns early if uninitialized or buffer too short.
**IsInitialized**: Returns the `_initialized` boolean flag.
**EncryptSend**: Encrypts the first 4 bytes of the input buffer in-place using the current key and state; returns early if uninitialized or buffer too short.
**SetKey**: Overload accepting `std::vector<uint8>`; assigns key and ensures non-empty size.
**SetKey#2**: Overload accepting raw pointer and length; copies data into key and ensures non-empty size.
**~AuthCrypt**: Destructor; no cleanup required.

---

<!-- machine-true, projected from graph.json -->

## Map — AuthCrypt

*Source:* AuthCrypt.cpp, AuthCrypt.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AuthCrypt | ctor | — | — | — |
| Init | method | — | WorldSocket/_HandleAuthSession | — |
| DecryptRecv | method | — | WorldSocket/DoRecvIncomingData | — |
| IsInitialized | method | — | — | — |
| EncryptSend | method | — | WorldSocket/HandleResultOfAsyncWrite | — |
| SetKey | method | — | WorldSocket/_HandleAuthSession | — |
| SetKey#2 | method | — | — | — |
| ~AuthCrypt | dtor | — | — | — |
