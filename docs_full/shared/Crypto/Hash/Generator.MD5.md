<!-- provenance: verbose, boundary-bleed -->
# Generator.MD5

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Generator.MD5

## Purpose & Responsibilities

`Generator.MD5` provides the implementation for MD5 message-digest computation within the `wowvmangos` server. It acts as a thin wrapper around the OpenSSL `MD5_*` API, exposing two primary interfaces:
1.  **One-shot convenience functions:** Static `ComputeFrom` overloads that accept various input types (`std::vector`, `std::string`, `BigNumber`, raw pointers) and return a 16-byte `Digest`.
2.  **Incremental hashing:** The `Generator` class, which manages an OpenSSL `MD5_CTX` to allow data to be fed in chunks via `UpdateData`, with the final result retrieved via `GetDigest`.

This unit is a pure cryptographic utility. It contains no game logic, database interactions, or network handling.

## Member-by-Member Behavior

The unit consists of static helper functions and the `Generator` class implementation. Note that while the header `MD5.h` declares additional members (such as `CreateEmpty` and template overloads), this specific translation unit only implements the non-template static functions and the core `Generator` methods listed in the MAP.

### Static Convenience Functions
These functions create a temporary `Generator` instance, feed it the input data, and return the resulting digest. They are designed for simple, single-pass hashing operations.

*   **`ComputeFrom` (overloads):** Four overloads are implemented for `std::vector<uint8>`, `std::string`, `BigNumber`, and raw `uint8` pointers. Each follows the same pattern: instantiate a `Generator`, call the appropriate `UpdateData` method, and return the result of `GetDigest()`.
    *   The `std::vector` and `std::string` overloads delegate to the raw pointer version internally.
    *   The `BigNumber` overload converts the number to a byte array using `BigNumber::AsByteArray` before updating the context.
    *   The raw pointer overload (`uint8 const*, size_t`) is the foundational implementation that directly interacts with the OpenSSL context.

### Generator Class
The `Generator` class encapsulates the state required for incremental MD5 hashing.

*   **`Generator` / `~Generator`:** The constructor allocates an OpenSSL `MD5_CTX` on the heap and initializes it with `MD5_Init`. The destructor ensures proper cleanup by deleting the context. Heap allocation is used despite the small size of the context, likely for compatibility with older OpenSSL versions or legacy design choices.
*   **`UpdateData` (overloads):** These methods feed data into the hash context.
    *   The `std::vector` and `std::string` overloads cast their data to raw pointers and delegate to the core `UpdateData(uint8 const*, size_t)` method.
    *   The `BigNumber` overload converts the number to bytes via `BigNumber::AsByteArray` and then delegates to the vector overload.
    *   The core `UpdateData(uint8 const*, size_t)` method calls OpenSSL's `MD5_Update`.
*   **`GetDigest`:** Finalizes the hash computation by calling OpenSSL's `MD5_Final`. It returns a `Digest` object (a 16-byte array). Once called, the internal OpenSSL context is finalized; subsequent calls to `UpdateData` on the same `Generator` instance will yield undefined results. There is no `Reset` method; a new `Generator` must be created for further hashing.

## Cross-Unit Boundaries

*   **OpenSSL:** All cryptographic operations are delegated to `<openssl/md5.h>` (`MD5_Init`, `MD5_Update`, `MD5_Final`).
*   **`BigNumber` Unit:** The `Generator::UpdateData(BigNumber)` and `ComputeFrom(BigNumber)` members call `BigNumber::AsByteArray` (from the `BigNumber` unit) to convert large integers into byte sequences suitable for hashing.
*   **`WardenModule` Unit:** Calls `ComputeFrom#2` (the `std::vector` overload) for integrity checks.
*   **`WardenScan` Unit:** Calls `ComputeFrom` (the `std::string` overload) for scanning checksums.
*   **`WorldSession` Unit:** Calls `ComputeFrom` (the `std::string` overload) and `CreateEmpty` (declared in `MD5.h`, implemented elsewhere) in `SendAccountDataTimes`.
*   **`ClientPatchCache` Unit:** Uses the incremental `Generator` pattern: it constructs a `Generator`, calls `UpdateData#4` (raw pointer overload), and retrieves the result via `GetDigest` in `CalculateAndCacheHash`.

## Data Model

This unit does not interact with any database tables.

## Notable Implementation Details

*   **Heap Allocation:** The `MD5_CTX` is heap-allocated in the `Generator` constructor. While `MD5_CTX` is typically small enough for stack allocation, this unit uses `new`/`delete`. The destructor correctly pairs these operations.
*   **Non-Reusable State:** `GetDigest` finalizes the OpenSSL context. The `Generator` cannot be reused after `GetDigest` is called. Users must instantiate a new `Generator` for each new hash operation.
*   **String Hashing:** The `std::string` overload hashes `data.size()` bytes from `c_str()`. This excludes the implicit null terminator unless it is explicitly part of the string data.
*   **BigNumber Dependency:** Hash consistency for `BigNumber` inputs depends entirely on the byte representation returned by `BigNumber::AsByteArray` in the `BigNumber` unit.

## Member Reference

*   **`ComputeFrom#2`**: Static function. Computes MD5 digest from a `std::vector<uint8>`. Creates a temporary `Generator`, updates it with the vector data, and returns the digest. Called by `WardenModule/WardenModule#2`.
*   **`ComputeFrom`**: Static function. Computes MD5 digest from a `std::string`. Creates a temporary `Generator`, updates it with the string data, and returns the digest. Called by `WardenScan/GetChecker` and `WorldSession.Main/SendAccountDataTimes`.
*   **`ComputeFrom#3`**: Static function. Computes MD5 digest from a `BigNumber`. Converts the number to bytes via `BigNumber::AsByteArray`, creates a temporary `Generator`, updates it, and returns the digest.
*   **`CreateEmpty`**: Static constexpr function. Returns a zero-initialized `Digest` (16 bytes of zeros). Called by `WorldSession.Main/SendAccountDataTimes`.
*   **`ComputeFrom#4`**: Static function. Computes MD5 digest from a raw `uint8` pointer and length. Creates a temporary `Generator`, updates it with the raw data, and returns the digest.
*   **`Generator`**: Constructor. Allocates and initializes an OpenSSL `MD5_CTX` on the heap. Called by `ClientPatchCache/CalculateAndCacheHash`.
*   **`~Generator`**: Destructor. Frees the heap-allocated `MD5_CTX`.
*   **`UpdateData#2`**: Method. Updates the hash context with data from a `std::vector<uint8>`. Delegates to the raw pointer version.
*   **`UpdateData`**: Method. Updates the hash context with data from a `std::string`. Casts the string buffer to `uint8*` and delegates to the raw pointer version.
*   **`UpdateData#3`**: Method. Updates the hash context with data from a `BigNumber`. Calls `BigNumber::AsByteArray` to get bytes, then updates the context.
*   **`UpdateData#4`**: Method. Core method. Updates the hash context with raw `uint8` data and length using OpenSSL's `MD5_Update`. Called by `ClientPatchCache/CalculateAndCacheHash`.
*   **`GetDigest`**: Method. Finalizes the hash computation using OpenSSL's `MD5_Final` and returns the 16-byte `Digest`. Called by `ClientPatchCache/CalculateAndCacheHash`.

---

<!-- machine-true, projected from graph.json -->

## Map — Generator.MD5

*Source:* MD5.cpp, MD5.h, SHA1.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ComputeFrom#2 | function | — | WardenModule/WardenModule#2 | — |
| ComputeFrom | function | — | WardenScan/GetChecker, WorldSession.Main/SendAccountDataTimes | — |
| ComputeFrom#3 | function | — | — | — |
| CreateEmpty | function | — | WorldSession.Main/SendAccountDataTimes | — |
| ComputeFrom#4 | function | — | — | — |
| Generator | ctor | — | ClientPatchCache/CalculateAndCacheHash | — |
| ~Generator | dtor | — | — | — |
| UpdateData#2 | method | — | — | — |
| UpdateData | method | — | — | — |
| UpdateData#3 | method | BigNumber/AsByteArray | — | — |
| UpdateData#4 | method | — | ClientPatchCache/CalculateAndCacheHash | — |
| GetDigest | method | — | ClientPatchCache/CalculateAndCacheHash | — |

---

<!-- verify: boundary-bleed | foreign: ComputeFrom, CreateEmpty, Generator, GetDigest, UpdateData -->
