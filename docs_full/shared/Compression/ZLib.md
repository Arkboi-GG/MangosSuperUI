<!-- provenance: verbose -->
# ZLib

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

The `Compression::ZLib` unit provides a thin, safe wrapper around the standard zlib library functions (`compress` and `uncompress`). It manages buffer allocation and error checking, exposing an interface that returns `nonstd::optional<std::vector<uint8>>`. This ensures callers receive either a valid result or a clear failure signal (`nullopt`), eliminating manual memory management in higher-level code.

It is exclusively used by `WorldSession` for account data synchronization: decompressing incoming updates and compressing outgoing responses. It does not interact with any database tables.

## Member-by-Member Behavior

### Decompression

**`Decompress`** restores compressed data given an input buffer, the expected uncompressed size, and a checksum validation option.

1.  **Buffer Allocation**: Allocates an output buffer of `decompressedSize + 1024` bytes. The padding prevents `Z_BUF_ERROR` if zlib requires slightly more space than the declared size.
2.  **Uncompression**: Invokes zlib's `uncompress`.
3.  **Error Handling**:
    *   If the result is not `Z_OK`, it checks for `Z_DATA_ERROR`.
    *   If `Z_DATA_ERROR` occurs and `option` is `ChecksumOption::ValidateChecksum`, it returns `nullopt`.
    *   If `option` is `ChecksumOption::IgnoreChecksum`, `Z_DATA_ERROR` is ignored, allowing the process to continue to the size check.
    *   Any other error results in `nullopt`.
4.  **Size Verification**: Verifies that `actualSize` (bytes written by zlib) exactly equals `decompressedSize`. If they differ, it returns `nullopt`.
5.  **Result**: Resizes the output vector to `actualSize` and returns it.

### Compression

**`Compress`** exists in two overloads.

1.  **Raw Pointer Overload (`Compress(uint8 const*, size_t)`)**:
    *   Calculates maximum compressed size using `compressBound`.
    *   Allocates an output buffer of that size.
    *   Invokes zlib's `compress`.
    *   Returns `nullopt` if the result is not `Z_OK`.
    *   On success, resizes the output vector to the actual compressed size and returns it.

2.  **Vector Overload (`Compress(std::vector<uint8> const&)`)**:
    *   Convenience wrapper forwarding `data.data()` and `data.size()` to the raw pointer overload.

## Cross-Unit Boundaries

*   **Called by `WorldSession.MiscHandler/HandleUpdateAccountData`**:
    *   **Direction**: `WorldSession` calls `ZLib::Decompress`.
    *   **Context**: Handles incoming compressed account data updates from the client. `WorldSession` passes the compressed payload and expected size. If `ZLib` returns `nullopt`, the update is rejected.

*   **Called by `WorldSession.MiscHandler/HandleRequestAccountData`**:
    *   **Direction**: `WorldSession` calls `ZLib::Compress`.
    *   **Context**: Handles outgoing account data responses. `WorldSession` serializes data into a vector and passes it to `Compress`. The resulting compressed vector is sent to the client.

## Data Model

This unit does not interact with any database tables. It operates entirely on in-memory `std::vector<uint8>` buffers.

## Notable Implementation Details

*   **Padding Strategy**: `Decompress` adds 1024 bytes to the output buffer to avoid `Z_BUF_ERROR` from zlib's internal requirements, even when `decompressedSize` is known.
*   **Checksum Flexibility**: `ChecksumOption::IgnoreChecksum` allows `Z_DATA_ERROR` to be bypassed. This prioritizes availability over integrity, relying on the strict size check (`actualSize == decompressedSize`) to catch major discrepancies. Corrupted data with matching sizes may pass through.
*   **Default Compression**: `Compress` uses zlib's default compression level, offering no tuning for speed vs. size.

## Member Reference

**`Decompress`**: Takes a compressed `std::vector<uint8>`, expected uncompressed size, and checksum option. Allocates a padded buffer, calls zlib `uncompress`, validates against `Z_OK` (ignoring `Z_DATA_ERROR` if checksums are ignored), verifies final size matches expectation, and returns the resized vector or `nullopt`.

**`Compress#2`**: Convenience overload accepting a `std::vector<uint8> const&`. Delegates to the raw pointer `Compress` overload.

**`Compress`**: Raw pointer overload calculating max compressed size via `compressBound`, allocating a buffer, calling zlib `compress`, and returning the resized vector or `nullopt` on failure.

---

<!-- machine-true, projected from graph.json -->

## Map — ZLib

*Source:* ZLib.cpp, ZLib.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Decompress | function | — | WorldSession.MiscHandler/HandleUpdateAccountData | — |
| Compress#2 | function | — | WorldSession.MiscHandler/HandleRequestAccountData | — |
| Compress | function | — | — | — |
