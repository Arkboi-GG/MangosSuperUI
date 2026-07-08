# RC4

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

The `RC4` class provides a thin, object-oriented wrapper around the OpenSSL `EVP` interface for the RC4 stream cipher. Its primary responsibility is to manage the lifecycle of an `EVP_CIPHER_CTX` context, handling initialization with variable-length keys and performing in-place encryption or decryption of byte buffers. Because RC4 is a symmetric stream cipher, the operations for encryption and decryption are identical; the class exposes a single `UpdateData` method that applies the keystream to the input buffer regardless of direction.

This unit is exclusively used by the **Log.Warden** subsystem (`Log.Warden/Warden` and `Log.Warden/HandleChallengeResponse`) to secure communication channels, likely for challenge-response authentication or encrypted data transmission between the server and clients. It abstracts away OpenSSL version-specific quirks, particularly the requirement to explicitly load the "legacy" provider in OpenSSL 3.0+, ensuring the deprecated RC4 algorithm remains accessible despite modern security defaults.

## Member-by-Member Behavior

### Construction and Lifecycle

*   **`RC4(uint8 len)`**: This constructor initializes an RC4 context without setting a key immediately. It allocates a new `EVP_CIPHER_CTX`, configures it for the RC4 cipher, and sets the expected key length to `len`. Crucially, if compiled against OpenSSL 3.0 or newer, it loads the `legacy` provider to enable access to RC4. The context remains uninitialized until `Init` or the second constructor is used.
*   **`RC4(uint8* seed, uint8 len)`**: This constructor performs full initialization. It allocates the context, sets the key length, and immediately applies the `seed` as the encryption key using `EVP_EncryptInit_ex`. Like the first constructor, it handles the OpenSSL 3.0 legacy provider loading. This allows for immediate use of the cipher after construction.
*   **`~RC4()`**: The destructor frees the allocated `EVP_CIPHER_CTX` using `EVP_CIPHER_CTX_free`, preventing memory leaks. It does not explicitly unload the legacy provider, relying on the process lifetime or OpenSSL's internal reference counting for provider cleanup.

### Cipher Operations

*   **`Init(const uint8* seed)`**: Re-initializes an existing RC4 context with a new key (`seed`). This is useful if the same `RC4` object instance needs to be reused with different keys. It calls `EVP_EncryptInit_ex` with the new key material, resetting the internal state of the cipher.
*   **`UpdateData(uint8* data, size_t len)`**: Performs the actual cryptographic operation. It takes a mutable buffer `data` of size `len` and processes it in-place. The method uses `EVP_EncryptUpdate` to process the bulk of the data and `EVP_EncryptFinal_ex` to finalize the operation. Since RC4 is a stream cipher with no block padding requirements, `EVP_EncryptFinal_ex` typically produces no additional output, but it is called to ensure the context is properly finalized according to the EVP API contract. The output length is tracked via `outlen`, though the return value is ignored, implying the caller assumes the output size equals the input size (which is true for RC4).

## Cross-Unit Boundaries

The `RC4` class acts as a pure utility component with no dependencies on other application logic units. It relies solely on the system-level OpenSSL library.

*   **Called by `Log.Warden/Warden`**: The Warden module uses `RC4` to handle encrypted communications. It likely constructs `RC4` instances to decrypt incoming packets from clients and encrypt outgoing responses. The specific usage pattern suggests that `Warden` manages the lifecycle of these cipher objects, initializing them with session-specific keys derived during the handshake.
*   **Called by `Log.Warden/HandleChallengeResponse`**: This function uses `RC4` to process the challenge-response phase of the Warden protocol. It likely initializes an `RC4` instance with a shared secret or derived key to decrypt the client's challenge response or to encrypt the server's challenge data.
*   **Called by `Log.Warden/DecryptData` and `Log.Warden/EncryptData`**: These helper functions in the Warden subsystem delegate the actual byte manipulation to `RC4::UpdateData`. They pass the raw byte buffers to be transformed, relying on `RC4` to perform the XOR-based stream cipher operation.

## Data Model

This unit does not interact with any database tables. It operates entirely in memory on byte buffers provided by the calling Warden subsystem.

## Notable Implementation Details

1.  **OpenSSL 3.0 Compatibility**: The code explicitly checks for `OPENSSL_VERSION_MAJOR >= 3` and loads the `legacy` provider. This is a critical maintenance detail because RC4 is considered cryptographically broken and is disabled by default in modern OpenSSL versions. Without this explicit loading, the `EVP_rc4()` call would fail, causing the Warden subsystem to break on servers running OpenSSL 3.0+.
2.  **In-Place Processing**: `UpdateData` modifies the `data` buffer in place. The pointer passed to `EVP_EncryptUpdate` for both input and output is the same (`data`). This is efficient for memory usage but requires the caller to ensure the buffer is writable and contains the correct data before the call.
3.  **Key Length Flexibility**: The constructors accept a `uint8 len` for the key length. RC4 supports variable key lengths (typically 1–256 bytes). The code correctly passes this length to `EVP_CIPHER_CTX_set_key_length`, allowing the Warden subsystem to use whatever key size the protocol dictates.
4.  **No Error Handling**: The `RC4` methods do not check return values from OpenSSL functions (e.g., `EVP_EncryptInit_ex`, `EVP_EncryptUpdate`). If an OpenSSL call fails (e.g., due to invalid key length or provider issues), the failure is silent. The caller (`Log.Warden`) must rely on subsequent validation or assume success. This is a potential fragility point if the environment changes (e.g., missing legacy provider permissions).
5.  **Context Reuse**: The `Init` method allows re-keying an existing context. However, note that `EVP_EncryptInit_ex` resets the internal state. If the caller expects to continue an existing stream, they must ensure they are not inadvertently resetting the keystream mid-stream. Given the usage in challenge-response, this is likely intended behavior (fresh key = fresh stream).

## Member Reference

*   **RC4#2**: Constructor that initializes an RC4 context with a specified key length but no key material. Loads the OpenSSL legacy provider if required.
*   **RC4**: Constructor that initializes an RC4 context with a specified key length and immediately sets the key using the provided `seed`. Loads the OpenSSL legacy provider if required.
*   **~RC4**: Destructor that frees the underlying OpenSSL `EVP_CIPHER_CTX` resource.
*   **Init**: Method that re-initializes the RC4 context with a new key (`seed`), resetting the cipher state.
*   **UpdateData**: Method that performs in-place encryption/decryption of the provided `data` buffer using the current RC4 context state.

---

<!-- machine-true, projected from graph.json -->

## Map — RC4

*Source:* RC4.cpp, RC4.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| RC4#2 | ctor | — | Log.Warden/Warden | — |
| RC4 | ctor | — | — | — |
| ~RC4 | dtor | — | — | — |
| Init | method | — | Log.Warden/HandleChallengeResponse, Log.Warden/Warden | — |
| UpdateData | method | — | Log.Warden/DecryptData, Log.Warden/EncryptData | — |
