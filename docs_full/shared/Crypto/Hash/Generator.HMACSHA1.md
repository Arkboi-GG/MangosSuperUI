<!-- provenance: verbose, boundary-bleed -->
# Generator.HMACSHA1

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Generator.HMACSHA1

## Purpose & Responsibilities

`Generator.HMACSHA1` implements the streaming computation of HMAC-SHA1 digests, wrapping OpenSSL’s `HMAC_*` API. It manages the lifecycle of an `HMAC_CTX`, handling version-specific allocation and cleanup for OpenSSL 1.1.0+ and earlier versions. The unit is stateless beyond the active computation context and performs no I/O or database access.

It serves two primary subsystems:
1. **Authentication**: `AuthSocket/GenerateTotpPin` uses it to generate Time-based One-Time Password (TOTP) pins.
2. **Anti-Cheat (Warden)**: Various `WardenScan` and `WardenWin` modules use it to hash memory regions, drivers, and modules for integrity verification.

## Member-by-Member Behavior

### Construction & Destruction

**`Generator` (ctor, `std::vector<uint8>` key)**  
Delegates to the raw-pointer constructor. Accepts a key as a `std::vector<uint8>`, passing its underlying buffer and size. Used by `AuthSocket/GenerateTotpPin` when the TOTP secret is stored in a vector.

**`Generator#2` (ctor, `uint8 const*` key, `size_t` len)**  
Initializes the OpenSSL HMAC context with the provided key and SHA-1 algorithm. Handles OpenSSL version differences:
- OpenSSL ≥ 1.1.0: Uses `HMAC_CTX_new()` to allocate a heap-managed context.
- OpenSSL < 1.1.0: Allocates a `HMAC_CTX` via `new`, then initializes it with `HMAC_CTX_init()`.

Calls `HMAC_Init_ex()` to bind the key and specify `EVP_sha1()`. Used by all Warden scan modules (`WardenScan/WindowsCodeScan`, `WardenScan/WindowsDriverScan`, `WardenScan/WindowsModuleScan`, `WardenScan/WindowsModuleScan#2`) and `WardenWin/LoadScriptedScans` when keys are provided as raw pointers.

**`~Generator` (dtor)**  
Frees the OpenSSL HMAC context, respecting version differences:
- OpenSSL ≥ 1.1.0: Calls `HMAC_CTX_free()`.
- OpenSSL < 1.1.0: Calls `HMAC_CTX_cleanup()` followed by `delete`.

Ensures no resource leaks regardless of OpenSSL version.

### Data Updates

**`UpdateData#2` (method, `std::vector<uint8>`)**  
Overload that accepts a byte vector. Delegates to the raw-pointer version by passing `.data()` and `.size()`. Provides a clean interface for callers holding data in standard containers.

**`UpdateData` (method, `std::string`)**  
Overload that accepts a string. Casts the string’s character data to `uint8 const*` and passes the length. Enables hashing of text-based inputs without manual casting by the caller.

**`UpdateData#3` (method, `BigNumber`)**  
Overload that accepts a `BigNumber` object. Calls `BigNumber.AsByteArray()` (from the `BigNumber` unit) to obtain the byte representation, then delegates to the vector overload. Allows cryptographic big integers to be hashed directly. Used by `AuthSocket/GenerateTotpPin` to hash TOTP counters.

**`UpdateData#4` (method, `uint8 const*` data, `size_t` length)**  
Core update function. Calls OpenSSL’s `HMAC_Update()` to feed raw bytes into the HMAC context. This is the only overload that interacts directly with OpenSSL; all others delegate to it. Supports streaming: multiple calls accumulate data before finalization. Called by `WardenScan/WindowsDriverScan`, `WardenScan/WindowsModuleScan`, `WardenScan/WindowsModuleScan#2`, and `WardenWin/LoadScriptedScans`.

### Finalization

**`GetDigest` (method)**  
Finalizes the HMAC computation and returns the result. Allocates a local `Digest` (a 20-byte array, inherited from `SHA1::Digest`), calls `HMAC_Final()` to extract the hash into it, and returns the digest by value. The context remains valid after this call, though typical usage patterns create a new generator per operation. Used by `AuthSocket/GenerateTotpPin` and all Warden scan modules to retrieve the final hash value.

### Utility

**`CreateEmpty` (function)**  
A constexpr utility defined in the header that returns a zero-initialized `Digest`. Useful for initializing digest variables or comparing against empty states. Does not interact with the `Generator` class.

## Cross-Unit Boundaries

### Called By: Authentication Subsystem

**`AuthSocket/GenerateTotpPin`**  
Uses `Generator.HMACSHA1` to compute the HMAC-SHA1 of a TOTP counter value combined with a secret key. The flow:
1. Creates a `Generator` with the user’s TOTP secret key (via `Generator` ctor).
2. Calls `UpdateData#3` with the counter (as a `BigNumber`).
3. Calls `GetDigest` to obtain the hash.
4. Truncates and offsets the digest to produce the final PIN.

This integration relies on `BigNumber.AsByteArray()` to convert the counter into bytes before hashing.

### Called By: Anti-Cheat (Warden) Subsystem

Multiple Warden scan modules use `Generator.HMACSHA1` to verify client integrity:

**`WardenScan/WindowsCodeScan`**  
Hashes executable code regions. Creates a generator with a scan-specific key (via `Generator#2`), updates it with memory dumps (via `UpdateData#4`), and retrieves the digest (via `GetDigest`) for comparison against known-good values.

**`WardenScan/WindowsDriverScan`**  
Hashes loaded drivers. Similar pattern: key initialization (via `Generator#2`), memory region updates (via `UpdateData#4`), digest retrieval (via `GetDigest`).

**`WardenScan/WindowsModuleScan` & `WindowsModuleScan#2`**  
Hashes loaded modules (DLLs, EXEs). Two variants likely handle different module types or scanning strategies. Both use the same HMAC workflow: key initialization (via `Generator#2`), data updates (via `UpdateData#4`), and digest retrieval (via `GetDigest`).

**`WardenWin/LoadScriptedScans`**  
Loads and executes scripted scan definitions. Uses HMAC to verify script integrity or hash scanned data. Calls `Generator#2` for initialization, `UpdateData#4` for data ingestion, and `GetDigest` for finalization.

All Warden integrations follow the same pattern: initialize with a key, stream data via `UpdateData#4`, finalize with `GetDigest`. The key varies per scan type, ensuring different hashes for different checks.

### Calls Out To

**`BigNumber/AsByteArray`**  
Called by the `UpdateData#3` overload. Converts a `BigNumber` into a byte vector for hashing. This is the only outbound dependency from this unit.

## Data Model

This unit performs no database operations. It contains no SQL queries, table references, or ORM interactions. All data flows through in-memory buffers and OpenSSL contexts.

## Notable Implementation Details

### OpenSSL Version Compatibility

The unit explicitly handles two major OpenSSL API shifts:
- **Pre-1.1.0**: `HMAC_CTX` is allocated on the heap via `new` and initialized with `HMAC_CTX_init()`. Cleanup requires `HMAC_CTX_cleanup()` + `delete`.
- **Post-1.1.0**: `HMAC_CTX` is managed via `HMAC_CTX_new()`/`HMAC_CTX_free()`, which handle internal allocation.

This dual-path approach ensures compatibility across server environments with different OpenSSL versions. The version check uses `OPENSSL_VERSION_NUMBER >= 0x10100000L`, which corresponds to OpenSSL 1.1.0.

**Gotcha**: If compiled against OpenSSL 1.1.0+ but linked against an older runtime (unlikely but possible in mixed-environment deployments), the `HMAC_CTX_new()` call would fail at link time. Conversely, compiling against older OpenSSL but running with newer libraries would cause undefined behavior. The build system must ensure consistent OpenSSL versions across compile and runtime.

### Context Reuse

`GetDigest()` does not reset or invalidate the context. After calling `GetDigest()`, the generator can continue accepting `UpdateData` calls and produce a new digest. However, typical usage patterns (especially in Warden and AuthSocket) create a fresh `Generator` per operation, so this reuse capability is unused in practice. Maintainers should note that reusing a context without clearing it between operations would produce incorrect results unless the intent is to hash concatenated streams.

### No Error Handling

The unit assumes OpenSSL functions succeed. `HMAC_Init_ex()`, `HMAC_Update()`, and `HMAC_Final()` do not return error codes in normal usage, and the unit does not check for failures. If OpenSSL encounters an internal error (e.g., invalid key, corrupted context), the behavior is undefined. In practice, this is acceptable because:
- Keys are controlled by the application (not user input).
- Contexts are short-lived and freshly allocated.
- OpenSSL’s HMAC API is robust for valid inputs.

However, if future modifications introduce dynamic or untrusted keys, error checking should be added.

### Digest Type Alias

`Digest` is aliased to `SHA1::Digest`, which is a `std::array<uint8, 20>`. This means HMAC-SHA1 produces the same 20-byte output as plain SHA-1, which is correct per the HMAC specification. The aliasing simplifies code by reusing the SHA1 digest structure.

### Template Overload for Arrays

The `UpdateData` template for `std::array<uint8, N>` is defined inline in the header. This enables zero-copy hashing of fixed-size buffers. The template parameter `N` is deduced automatically, so callers pass arrays naturally without specifying size. Note that this template overload is not listed in the MAP as it is a header-only implementation detail not explicitly tracked in the cross-unit call graph.

## Member Reference

**Generator**  
Constructs an HMAC-SHA1 generator from a key stored in a `std::vector<uint8>`. Delegates to the raw-pointer constructor. Used by `AuthSocket/GenerateTotpPin` when the TOTP secret is held in a vector.

**Generator#2**  
Constructs an HMAC-SHA1 generator from a raw key buffer. Initializes the OpenSSL context with version-aware allocation and binds the key with SHA-1. Used by all Warden scan modules and `WardenWin/LoadScriptedScans` when keys are provided as raw pointers.

**CreateEmpty**  
Returns a zero-initialized 20-byte `Digest`. Constexpr utility for initializing digest variables. Not called by any other unit in the current codebase.

**~Generator**  
Destroys the HMAC context, freeing resources with version-aware cleanup. Ensures no memory leaks across OpenSSL versions.

**UpdateData#2**  
Updates the HMAC state with data from a `std::vector<uint8>`. Delegates to the raw-pointer overload. Provides a convenient interface for vector-held data.

**UpdateData**  
Updates the HMAC state with string data. Casts characters to `uint8` and passes the length. Enables hashing of text inputs like usernames or tokens.

**UpdateData#3**  
Updates the HMAC state with a `BigNumber`. Calls `BigNumber.AsByteArray()` to convert the number to bytes, then delegates to the vector overload. Used by `AuthSocket/GenerateTotpPin` to hash TOTP counters.

**UpdateData#4**  
Core update function. Calls OpenSSL’s `HMAC_Update()` to feed raw bytes into the context. Supports streaming updates. Called by Warden modules when hashing memory regions or module contents.

**GetDigest**  
Finalizes the HMAC computation and returns the 20-byte digest. Calls `HMAC_Final()` to extract the hash. Used by all callers (AuthSocket and Warden modules) to retrieve the final hash value.

---

<!-- machine-true, projected from graph.json -->

## Map — Generator.HMACSHA1

*Source:* HMACSHA1.cpp, HMACSHA1.h, SHA1.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Generator | ctor | — | AuthSocket/GenerateTotpPin | — |
| Generator#2 | ctor | — | WardenScan/WindowsCodeScan, WardenScan/WindowsDriverScan, WardenScan/WindowsModuleScan, WardenScan/WindowsModuleScan#2, WardenWin/LoadScriptedScans | — |
| CreateEmpty | function | — | — | — |
| ~Generator | dtor | — | — | — |
| UpdateData#2 | method | — | — | — |
| UpdateData | method | — | WardenScan/WindowsDriverScan, WardenScan/WindowsModuleScan, WardenScan/WindowsModuleScan#2, WardenWin/LoadScriptedScans | — |
| UpdateData#3 | method | BigNumber/AsByteArray | — | — |
| UpdateData#4 | method | — | AuthSocket/GenerateTotpPin, WardenScan/WindowsCodeScan, WardenWin/LoadScriptedScans | — |
| GetDigest | method | — | AuthSocket/GenerateTotpPin, WardenScan/WindowsCodeScan, WardenScan/WindowsDriverScan, WardenScan/WindowsModuleScan, WardenScan/WindowsModuleScan#2, WardenWin/LoadScriptedScans | — |

---

<!-- verify: boundary-bleed | foreign: CreateEmpty, Generator, GetDigest, UpdateData -->
