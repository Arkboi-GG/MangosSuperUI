<!-- provenance: verbose -->
# Digest

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Crypto Hash Utilities (MD5 and SHA1)

## Purpose & Responsibilities

The `Crypto::Hash::MD5` and `Crypto::Hash::SHA1` namespaces provide thin C++ wrappers around OpenSSL’s MD5 and SHA1 cryptographic hash functions. They serve two primary roles in the WoWVMaNGOS server:

1.  **Authentication:** Generating and verifying hashes for the Secure Remote Password (SRP) protocol, account login, and session reconnection proofs.
2.  **Integrity Checking:** Computing checksums for the Warden anti-cheat system to detect client-side modifications.

These units perform pure in-memory computation. They expose static `ComputeFrom` functions for one-shot hashing and `Generator` classes for incremental hashing of fragmented data. They do not perform I/O or database access.

## Member-by-Member Behavior

### MD5 Namespace (`Crypto::Hash::MD5`)

*   **`Digest`**: A `std::array<uint8, 16>` representing the 128-bit MD5 output. Includes a static `size()` method returning 16.
*   **`CreateEmpty()`**: Returns a zero-initialized `Digest`.
*   **`ComputeFrom(...)`**: Static functions computing the MD5 hash of input data (`std::array`, `std::vector`, `std::string`, `BigNumber`, or raw bytes) in one step.
*   **`Generator`**: A class for incremental MD5 hashing.
    *   **Constructor/Destructor**: Manages the underlying OpenSSL `MD5_CTX`.
    *   **`UpdateData(...)`**: Feeds data chunks into the hash state.
    *   **`GetDigest()`**: Finalizes and returns the `Digest`. The generator cannot be reused after this call.

### SHA1 Namespace (`Crypto::Hash::SHA1`)

*   **`Digest`**: A `std::array<uint8, 20>` representing the 160-bit SHA1 output. Includes a static `size()` method returning 20.
*   **`CreateZero()`**: Returns a zero-initialized `Digest`.
*   **`ComputeFrom(...)`**: Static functions for one-step SHA1 hashing, mirroring the MD5 interface.
*   **`Generator`**: A class for incremental SHA1 hashing, identical in interface to MD5’s `Generator` but operating on `SHA_CTX`.

## Cross-Unit Boundaries

### Authentication Subsystem

*   **`AuthSocket`**: Calls `MD5::Digest::size()` in `_HandleLogonProof__PostRecv_HandleInvalidVersion` for buffer sizing. Calls `SHA1::Digest::size()` in `VerifyPinData`, `VerifyVersion`, and `_HandleReconnectProof` for PIN and session proof validation.
*   **`WorldSocket`**: Calls `SHA1::Digest::size()` in `_HandleAuthSession` during the initial handshake.
*   **`AccountMgr`**: Calls `SHA1::Digest::size()` in `CalculateShaPassHash` for password hashing preparation.
*   **`SRP6`**: Calls `SHA1::Digest::size()` in `CalculateProof` and `CalculateVerifier#2` for SRP6 verifier and proof generation.

### Warden Anti-Cheat System

*   **`WardenScan`**: Calls `MD5::Digest::size()` and `SHA1::Digest::size()` in `GetChecker`, `MacStringHashScan`, and `WindowsStringHashScan` to configure hash checkers. Calls `SHA1::Digest::size()` in `WindowsCodeScan`, `WindowsDriverScan`, `WindowsFileHashScan`, `WindowsHookScan`, `WindowsModuleScan`, and `WindowsModuleScan#2` for memory, driver, file, and module integrity checks.
*   **`WardenWin`**: Calls `SHA1::Digest::size()` in `LoadScriptedScans` for custom scan script loading.
*   **`WardenScanMgr`**: Calls `SHA1::Digest::size()` in `LoadFromDB` when loading scan configurations.

### Other

*   **`WorldSession.Main`**: Calls `MD5::Digest::size()` in `SendAccountDataTimes` for account data synchronization.
*   **`Log.Warden`**: Calls `MD5::Digest::size()` in `SendModuleUse` for logging module usage.

## Data Model

This unit performs pure computational operations on in-memory data. It does not directly access any database tables.

## Notable Implementation Details

*   **OpenSSL Dependency**: `MD5_CTX` and `SHA_CTX` are typedefs for OpenSSL structures. `Generator` classes manage these contexts via raw pointers (`m_ctx`).
*   **No State Reset**: `Generator` classes lack a `Reset()` method. Once `GetDigest()` is called, the context is finalized; a new instance is required for subsequent hashing.
*   **BigNumber Support**: Overloads for `BigNumber` allow direct hashing of large integers used in SRP6.
*   **Constexpr Initialization**: `CreateEmpty()` and `CreateZero()` are `constexpr`, enabling compile-time buffer initialization.

## Member Reference

**size** (MD5): Static method returning the constant size of an MD5 digest (16 bytes). Called by `AuthSocket/_HandleLogonProof__PostRecv_HandleInvalidVersion`, `Log.Warden/SendModuleUse`, `WardenScan/GetChecker`, `WardenScan/MacStringHashScan`, `WardenScan/WindowsStringHashScan`, and `WorldSession.Main/SendAccountDataTimes`.

**size#2** (SHA1): Static method returning the constant size of a SHA1 digest (20 bytes). Called by `AccountMgr/CalculateShaPassHash`, `AuthSocket/VerifyPinData`, `AuthSocket/VerifyVersion`, `AuthSocket/_HandleReconnectProof`, `SRP6/CalculateProof`, `SRP6/CalculateVerifier#2`, `WardenScan/GetChecker`, `WardenScan/MacStringHashScan`, `WardenScan/WindowsCodeScan`, `WardenScan/WindowsDriverScan`, `WardenScan/WindowsFileHashScan`, `WardenScan/WindowsHookScan`, `WardenScan/WindowsModuleScan`, `WardenScan/WindowsModuleScan#2`, `WardenScan/WindowsStringHashScan`, `WardenScanMgr/LoadFromDB`, `WardenWin/LoadScriptedScans`, and `WorldSocket/_HandleAuthSession`.

---

<!-- machine-true, projected from graph.json -->

## Map — Digest

*Source:* MD5.h, SHA1.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| size | method | — | AuthSocket/_HandleLogonProof__PostRecv_HandleInvalidVersion, Log.Warden/SendModuleUse, WardenScan/GetChecker, WardenScan/MacStringHashScan, WardenScan/WindowsStringHashScan, WorldSession.Main/SendAccountDataTimes | — |
| size#2 | method | — | AccountMgr/CalculateShaPassHash, AuthSocket/VerifyPinData, AuthSocket/VerifyVersion, AuthSocket/_HandleReconnectProof, SRP6/CalculateProof, SRP6/CalculateVerifier#2, WardenScan/GetChecker, WardenScan/MacStringHashScan, WardenScan/WindowsCodeScan, WardenScan/WindowsDriverScan, WardenScan/WindowsFileHashScan, WardenScan/WindowsHookScan, WardenScan/WindowsModuleScan, WardenScan/WindowsModuleScan#2, WardenScan/WindowsStringHashScan, WardenScanMgr/LoadFromDB, WardenWin/LoadScriptedScans, WorldSocket/_HandleAuthSession | — |
