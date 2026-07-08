# Generator — Class Overview

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Generator

`Generator` is the server’s cryptographic hashing interface, providing C++ wrappers around OpenSSL implementations for SHA-1, MD5, and HMAC-SHA1. It serves as the foundational primitive for authentication (password hashing, SRP6 proof generation, TOTP PINs), anti-cheat integrity checks (Warden scans), and data verification (patch caching, account data timestamps). The class abstracts away OpenSSL context management and version-specific API differences, offering both one-shot static functions for simple inputs and streaming `Generator` objects for incremental data processing.

## How the class is split

The `Generator` functionality is distributed across three partials, each dedicated to a specific hash algorithm:

*   **`Generator.SHA1`**: Implements SHA-1 hashing. It provides static `ComputeFrom` functions for one-shot hashing of strings, vectors, `BigNumber`s, and raw buffers, as well as a `Generator` class for incremental streaming. It is heavily used by the authentication subsystem (`AuthSocket`, `WorldSocket`) and the SRP6 protocol for secure login handshakes.
*   **`Generator.MD5`**: Implements MD5 hashing. Similar to SHA-1, it offers static `ComputeFrom` utilities and a streaming `Generator` class. It is primarily used for integrity checks in the Warden anti-cheat system, patch cache validation (`ClientPatchCache`), and generating timestamps for account data synchronization (`WorldSession`).
*   **`Generator.HMACSHA1`**: Implements HMAC-SHA1 (Hash-based Message Authentication Code). It wraps the OpenSSL `HMAC_*` API to compute keyed hashes. This partial is critical for generating Time-based One-Time Password (TOTP) pins during authentication and for verifying the integrity of memory regions, drivers, and modules in the Warden anti-cheat scans.

## How the partials collaborate

The three partials operate independently, each managing its own OpenSSL context (`SHA_CTX`, `MD5_CTX`, or `HMAC_CTX`). They do not call each other; instead, they are selected by the calling subsystem based on the required cryptographic strength and protocol specification.

*   **Authentication Flow**: `AuthSocket` and `WorldSocket` primarily use `Generator.SHA1` for password hashing and SRP6 calculations. `Generator.HMACSHA1` is invoked specifically when generating or verifying TOTP PINs.
*   **Anti-Cheat (Warden) Flow**: The Warden subsystem uses `Generator.MD5` for general checksums and `Generator.HMACSHA1` for more rigorous integrity verification of client memory and modules. `Generator.SHA1` is also used for building checksums in Warden logs.
*   **Shared Dependencies**: All three partials depend on the `BigNumber` unit for converting large integers into byte arrays via `BigNumber::AsByteArray()` when hashing numeric data. They also share a common design pattern: static `ComputeFrom` functions for simplicity and a `Generator` class for streaming large or chunked data.

## Data model

The `Generator` class performs no database operations. It does not read from or write to any tables in the `mangos`, `characters`, `realmd`, or `logs` databases. All data processed by `Generator` is transient, residing in memory buffers, network packets, or file streams during the hashing operation.

## Where to go deeper

*   **`Generator.SHA1`**: Open this doc to understand how passwords are hashed, how SRP6 proofs are calculated, and how SHA-1 digests are generated for Warden logs and account data.
*   **`Generator.MD5`**: Open this doc to see how MD5 is used for patch cache validation, Warden integrity checks, and account data timestamp generation.
*   **`Generator.HMACSHA1`**: Open this doc to learn about TOTP PIN generation and the detailed HMAC workflows used by Warden to scan client memory, drivers, and modules.

---

<!-- machine-true, projected from graph.json -->

## Map — Generator

*Source:* HMACSHA1.cpp, HMACSHA1.h, SHA1.h, MD5.cpp, MD5.h, SHA1.cpp

| Member | Partial | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|---|
| Generator | Generator.HMACSHA1 | ctor | — | AuthSocket/GenerateTotpPin | — |
| Generator#2 | Generator.HMACSHA1 | ctor | — | WardenScan/WindowsCodeScan, WardenScan/WindowsDriverScan, WardenScan/WindowsModuleScan, WardenScan/WindowsModuleScan#2, WardenWin/LoadScriptedScans | — |
| CreateEmpty | Generator.HMACSHA1 | function | — | — | — |
| ~Generator | Generator.HMACSHA1 | dtor | — | — | — |
| UpdateData#2 | Generator.HMACSHA1 | method | — | — | — |
| UpdateData | Generator.HMACSHA1 | method | — | WardenScan/WindowsDriverScan, WardenScan/WindowsModuleScan, WardenScan/WindowsModuleScan#2, WardenWin/LoadScriptedScans | — |
| UpdateData#3 | Generator.HMACSHA1 | method | BigNumber/AsByteArray | — | — |
| UpdateData#4 | Generator.HMACSHA1 | method | — | AuthSocket/GenerateTotpPin, WardenScan/WindowsCodeScan, WardenWin/LoadScriptedScans | — |
| GetDigest | Generator.HMACSHA1 | method | — | AuthSocket/GenerateTotpPin, WardenScan/WindowsCodeScan, WardenScan/WindowsDriverScan, WardenScan/WindowsModuleScan, WardenScan/WindowsModuleScan#2, WardenWin/LoadScriptedScans | — |
| ComputeFrom#2 | Generator.MD5 | function | — | WardenModule/WardenModule#2 | — |
| ComputeFrom | Generator.MD5 | function | — | WardenScan/GetChecker, WorldSession.Main/SendAccountDataTimes | — |
| ComputeFrom#3 | Generator.MD5 | function | — | — | — |
| CreateEmpty | Generator.MD5 | function | — | WorldSession.Main/SendAccountDataTimes | — |
| ComputeFrom#4 | Generator.MD5 | function | — | — | — |
| Generator | Generator.MD5 | ctor | — | ClientPatchCache/CalculateAndCacheHash | — |
| ~Generator | Generator.MD5 | dtor | — | — | — |
| UpdateData#2 | Generator.MD5 | method | — | — | — |
| UpdateData | Generator.MD5 | method | — | — | — |
| UpdateData#3 | Generator.MD5 | method | BigNumber/AsByteArray | — | — |
| UpdateData#4 | Generator.MD5 | method | — | ClientPatchCache/CalculateAndCacheHash | — |
| GetDigest | Generator.MD5 | method | — | ClientPatchCache/CalculateAndCacheHash | — |
| ComputeFrom#2 | Generator.SHA1 | function | — | — | — |
| ComputeFrom | Generator.SHA1 | function | — | AccountMgr/CalculateShaPassHash, SRP6/CalculateProof | — |
| ComputeFrom#3 | Generator.SHA1 | function | — | SRP6/CalculateProof | — |
| CreateZero | Generator.SHA1 | function | — | WardenScanMgr/LoadFromDB | — |
| ComputeFrom#4 | Generator.SHA1 | function | — | Log.Warden/BuildChecksum, SRP6/HashSessionKey | — |
| Generator | Generator.SHA1 | ctor | — | AuthSocket/VerifyPinData, AuthSocket/VerifyVersion, AuthSocket/_HandleReconnectProof, SRP6/CalculateProof, SRP6/CalculateSessionKey, SRP6/CalculateVerifier#2, SRP6/Finalize, WardenScan/GetChecker, WorldSocket/_HandleAuthSession | — |
| ~Generator | Generator.SHA1 | dtor | — | — | — |
| UpdateData#2 | Generator.SHA1 | method | — | AuthSocket/VerifyPinData | — |
| UpdateData | Generator.SHA1 | method | — | AuthSocket/_HandleReconnectProof, WardenScan/GetChecker, WorldSocket/_HandleAuthSession | — |
| UpdateData#3 | Generator.SHA1 | method | BigNumber/AsByteArray | AuthSocket/VerifyPinData, AuthSocket/_HandleReconnectProof, SRP6/CalculateProof, SRP6/CalculateSessionKey, SRP6/CalculateVerifier#2, SRP6/Finalize, WorldSocket/_HandleAuthSession | — |
| UpdateData#4 | Generator.SHA1 | method | — | AuthSocket/VerifyPinData, AuthSocket/VerifyVersion, SRP6/CalculateVerifier#2, WardenScan/GetChecker, WorldSocket/_HandleAuthSession | — |
| GetDigest | Generator.SHA1 | method | — | AuthSocket/VerifyPinData, AuthSocket/VerifyVersion, AuthSocket/_HandleReconnectProof, SRP6/CalculateProof, SRP6/CalculateSessionKey, SRP6/CalculateVerifier#2, SRP6/Finalize, WardenScan/GetChecker, WorldSocket/_HandleAuthSession | — |
