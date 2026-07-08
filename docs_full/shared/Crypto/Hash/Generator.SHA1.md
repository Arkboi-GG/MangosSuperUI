<!-- provenance: verbose, boundary-bleed -->
# Generator.SHA1

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Generator.SHA1

## Purpose & Responsibilities

`Generator.SHA1` provides a C++ interface to OpenSSL’s SHA-1 hashing algorithm. It supports two usage patterns:
1.  **One-shot computation:** Static `ComputeFrom` functions (defined in this unit) accept various input types (`std::string`, `std::vector<uint8>`, `BigNumber`, raw pointers) and return a 20-byte `Digest`.
2.  **Incremental streaming:** The `Generator` class (declared in this unit, implemented in `SHA1.cpp`) allows initializing a context, feeding data in chunks via `UpdateData`, and retrieving the final digest via `GetDigest`.

The unit also defines the `Digest` type (`std::array<uint8, 20>`) and a `CreateZero` utility. It contains no database interactions.

## Member-by-Member Behavior

### One-Shot Computation

These functions instantiate a temporary `Generator`, process the input, and return the digest.

*   **`ComputeFrom` (overloads)**: Accepts `std::vector<uint8>`, `std::string`, `BigNumber`, or raw `uint8*` buffers. Each creates a `Generator`, calls the corresponding `UpdateData` overload, and returns `GetDigest()`.
*   **`ComputeFrom#2`**: A header-only template accepting `std::array<uint8, N>`, forwarding to the raw pointer overload.

### Incremental Hashing (`Generator` Class)

*   **`Generator` / `~Generator`**: Manages the lifecycle of an OpenSSL `SHA_CTX`. The constructor allocates and initializes the context; the destructor frees it.
*   **`UpdateData` (overloads)**: Feeds data into the hash context.
    *   `std::vector<uint8>` and `std::string` overloads delegate to the raw pointer version.
    *   `BigNumber` overload converts the number to a byte array via `BigNumber::AsByteArray()` before updating.
    *   `uint8 const*, size_t` is the core implementation calling `SHA1_Update`.
*   **`GetDigest`**: Finalizes the hash via `SHA1_Final` and returns the 20-byte result.

### Utilities

*   **`CreateZero`**: Returns a `Digest` initialized to all zeros.

## Cross-Unit Boundaries

This unit is a foundational cryptographic primitive used by authentication, security scanning, and logging subsystems.

*   **Authentication (`AuthSocket`, `WorldSocket`)**:
    *   `AuthSocket/VerifyPinData`, `VerifyVersion`, `_HandleReconnectProof` and `WorldSocket/_HandleAuthSession` use `Generator` and `UpdateData` overloads to hash PINs, version strings, and session data.
    *   `WorldSocket/_HandleAuthSession` and `AuthSocket/_HandleReconnectProof` use `UpdateData(BigNumber)` for SRP6-related computations.
*   **SRP6 Protocol (`SRP6`)**:
    *   `SRP6/CalculateProof`, `CalculateSessionKey`, `CalculateVerifier#2`, and `Finalize` use `ComputeFrom` (including `BigNumber` overload) and `Generator` methods to derive keys and proofs.
    *   `SRP6/HashSessionKey` uses `ComputeFrom(uint8*, size_t)`.
*   **Account Management (`AccountMgr`)**:
    *   `AccountMgr/CalculateShaPassHash` uses `ComputeFrom` to hash passwords.
*   **Anti-Cheat & Logging (`WardenScanMgr`, `WardenScan`, `Log.Warden`)**:
    *   `WardenScanMgr/LoadFromDB` uses `CreateZero` for initialization.
    *   `WardenScan/GetChecker` uses `Generator` and `UpdateData` to compute checksums.
    *   `Log.Warden/BuildChecksum` uses `ComputeFrom(uint8*, size_t)`.

## Notable Implementation Details

*   **Heap Allocation**: `Generator` allocates `SHA_CTX` on the heap (`new SHA_CTX`) because it is an opaque struct. This prevents embedding issues but requires manual memory management.
*   **No Copy Semantics**: `Generator` lacks copy/move constructors. Copying would cause double-free errors. Users must rely on the one-shot `ComputeFrom` functions or ensure `Generator` instances are unique.
*   **BigNumber Integration**: `UpdateData(BigNumber)` depends on `BigNumber::AsByteArray()` for serialization. Consistency in byte order is critical for cryptographic correctness.
*   **Thread Safety**: Each `Generator` holds its own context, allowing concurrent use across threads.

## Member Reference

*   **`ComputeFrom#2`**: Template `ComputeFrom(std::array<uint8, N> const&)` in `SHA1.h`. Forwards to raw pointer overload.
*   **`ComputeFrom`**: Function `ComputeFrom(std::vector<uint8> const&)` in `SHA1.cpp`. Called by `AccountMgr/CalculateShaPassHash`, `SRP6/CalculateProof`.
*   **`ComputeFrom#3`**: Function `ComputeFrom(BigNumber const&)` in `SHA1.cpp`. Called by `SRP6/CalculateProof`.
*   **`CreateZero`**: Function `CreateZero()` in `SHA1.h`. Returns zeroed digest. Called by `WardenScanMgr/LoadFromDB`.
*   **`ComputeFrom#4`**: Function `ComputeFrom(uint8 const*, size_t)` in `SHA1.cpp`. Called by `Log.Warden/BuildChecksum`, `SRP6/HashSessionKey`.
*   **`Generator`**: Constructor `Generator()` in `SHA1.cpp`. Allocates/initializes `SHA_CTX`. Called by `AuthSocket/VerifyPinData`, `AuthSocket/VerifyVersion`, `AuthSocket/_HandleReconnectProof`, `SRP6/CalculateProof`, `SRP6/CalculateSessionKey`, `SRP6/CalculateVerifier#2`, `SRP6/Finalize`, `WardenScan/GetChecker`, `WorldSocket/_HandleAuthSession`.
*   **`~Generator`**: Destructor `~Generator()` in `SHA1.cpp`. Frees `SHA_CTX`.
*   **`UpdateData#2`**: Method `UpdateData(std::vector<uint8> const&)` in `SHA1.cpp`. Delegates to raw pointer version. Called by `AuthSocket/VerifyPinData`.
*   **`UpdateData`**: Method `UpdateData(std::string const&)` in `SHA1.cpp`. Delegates to raw pointer version. Called by `AuthSocket/_HandleReconnectProof`, `WardenScan/GetChecker`, `WorldSocket/_HandleAuthSession`.
*   **`UpdateData#3`**: Method `UpdateData(BigNumber const&)` in `SHA1.cpp`. Calls `BigNumber::AsByteArray()`. Called by `AuthSocket/VerifyPinData`, `AuthSocket/_HandleReconnectProof`, `SRP6/CalculateProof`, `SRP6/CalculateSessionKey`, `SRP6/CalculateVerifier#2`, `SRP6/Finalize`, `WorldSocket/_HandleAuthSession`.
*   **`UpdateData#4`**: Method `UpdateData(uint8 const*, size_t)` in `SHA1.cpp`. Core `SHA1_Update` call. Called by `AuthSocket/VerifyPinData`, `AuthSocket/VerifyVersion`, `SRP6/CalculateVerifier#2`, `WardenScan/GetChecker`, `WorldSocket/_HandleAuthSession`.
*   **`GetDigest`**: Method `GetDigest()` in `SHA1.cpp`. Finalizes hash. Called by `AuthSocket/VerifyPinData`, `AuthSocket/VerifyVersion`, `AuthSocket/_HandleReconnectProof`, `SRP6/CalculateProof`, `SRP6/CalculateSessionKey`, `SRP6/CalculateVerifier#2`, `SRP6/Finalize`, `WardenScan/GetChecker`, `WorldSocket/_HandleAuthSession`.

---

<!-- machine-true, projected from graph.json -->

## Map — Generator.SHA1

*Source:* SHA1.cpp, SHA1.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ComputeFrom#2 | function | — | — | — |
| ComputeFrom | function | — | AccountMgr/CalculateShaPassHash, SRP6/CalculateProof | — |
| ComputeFrom#3 | function | — | SRP6/CalculateProof | — |
| CreateZero | function | — | WardenScanMgr/LoadFromDB | — |
| ComputeFrom#4 | function | — | Log.Warden/BuildChecksum, SRP6/HashSessionKey | — |
| Generator | ctor | — | AuthSocket/VerifyPinData, AuthSocket/VerifyVersion, AuthSocket/_HandleReconnectProof, SRP6/CalculateProof, SRP6/CalculateSessionKey, SRP6/CalculateVerifier#2, SRP6/Finalize, WardenScan/GetChecker, WorldSocket/_HandleAuthSession | — |
| ~Generator | dtor | — | — | — |
| UpdateData#2 | method | — | AuthSocket/VerifyPinData | — |
| UpdateData | method | — | AuthSocket/_HandleReconnectProof, WardenScan/GetChecker, WorldSocket/_HandleAuthSession | — |
| UpdateData#3 | method | BigNumber/AsByteArray | AuthSocket/VerifyPinData, AuthSocket/_HandleReconnectProof, SRP6/CalculateProof, SRP6/CalculateSessionKey, SRP6/CalculateVerifier#2, SRP6/Finalize, WorldSocket/_HandleAuthSession | — |
| UpdateData#4 | method | — | AuthSocket/VerifyPinData, AuthSocket/VerifyVersion, SRP6/CalculateVerifier#2, WardenScan/GetChecker, WorldSocket/_HandleAuthSession | — |
| GetDigest | method | — | AuthSocket/VerifyPinData, AuthSocket/VerifyVersion, AuthSocket/_HandleReconnectProof, SRP6/CalculateProof, SRP6/CalculateSessionKey, SRP6/CalculateVerifier#2, SRP6/Finalize, WardenScan/GetChecker, WorldSocket/_HandleAuthSession | — |

---

<!-- verify: boundary-bleed | foreign: ComputeFrom, Generator, GetDigest, UpdateData -->
