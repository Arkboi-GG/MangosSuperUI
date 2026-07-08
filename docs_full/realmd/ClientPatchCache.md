<!-- provenance: verbose -->
# ClientPatchCache

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`ClientPatchCache` is a singleton service that caches MD5 hashes of game client patch files stored on the server’s filesystem. It eliminates redundant disk I/O and cryptographic computation by storing hashes alongside file metadata (size and last modification time). The cache detects file changes by comparing current metadata against cached entries; if a file has changed, it recomputes the hash. This component supports the authentication flow by providing integrity verification data for patches required by clients.

## Member-by-Member Behavior

### Initialization

**`ClientPatchCache`**
Constructs the singleton and immediately calls `LoadPatchesInfo` to populate the cache with all valid patch files found in the configured directory.

### Cache Population

**`LoadPatchesInfo`**
Scans the directory specified by the `PatchesDir` config key (defaulting to `"./patches"`) for patch files. For each file:
1.  Resolves the absolute path via `FileSystem/ToAbsolutePath`.
2.  Attempts to open a read-only handle via `FileSystem/TryOpenFileReadonly`.
3.  If successful, delegates to `CalculateAndCacheHash` to compute and store the hash.
4.  If opening fails, logs an error via `Log.Main/Out` but continues processing remaining files.
Uses `Config/GetStringDefault`, `FileSystem/GetAllFilesInFolder`, and `Log.Main/Out`.

### Hash Retrieval & Computation

**`GetOrCalculateHash`**
Retrieves the MD5 hash for a given `FileHandleReadonly`. It checks the internal `m_knownPatches` map for an entry matching the file’s path, size, and modification date.
*   **Cache Hit:** Returns the cached hash immediately.
*   **Cache Miss/Change:** Releases the lock, logs the change via `Log.Main/Out`, duplicates the file handle via `FileHandle/DuplicateFileHandle` (to preserve the original for the caller), and calls `CalculateAndCacheHash` to update the cache.
Called by `AuthSocket/_HandleLogonProof__PostRecv_HandleInvalidVersion`.

**`CalculateAndCacheHash`**
Computes the MD5 hash of a file by reading it in 1 MiB chunks using `FileHandle/ReadSync` and updating the digest via `Generator.MD5/UpdateData#4`. After reading, it constructs a `PatchCacheEntry` with the file’s metadata and final digest (`Generator.MD5/GetDigest`), asserts that the total bytes read match the file size, and stores the entry in `m_knownPatches` under mutex protection. Takes ownership of the provided file handle. May invoke `Errors/PrintStacktraceAndThrow` on critical failures.

## Cross-Unit Boundaries

*   **Config**: `LoadPatchesInfo` calls `Config/GetStringDefault` to retrieve the `PatchesDir` setting.
*   **FileSystem**: `LoadPatchesInfo` uses `FileSystem/ToAbsolutePath` and `FileSystem/GetAllFilesInFolder` for path resolution and enumeration. Both `LoadPatchesInfo` and `GetOrCalculateHash` use `FileSystem/TryOpenFileReadonly` to obtain file handles.
*   **FileHandle**: `GetOrCalculateHash` and `CalculateAndCacheHash` use `FileHandle/GetFilePath`, `FileHandle/GetLastModifyDate`, `FileHandle/GetTotalFileSize`, `FileHandle/ReadSync`, and `FileHandle/DuplicateFileHandle` to manage file metadata and content.
*   **Crypto/Hash/MD5**: `CalculateAndCacheHash` uses `Generator.MD5/Generator`, `Generator.MD5/UpdateData#4`, and `Generator.MD5/GetDigest` for hashing.
*   **Log.Main**: All public methods log status via `Log.Main/Out`.
*   **Errors**: `CalculateAndCacheHash` may trigger `Errors/PrintStacktraceAndThrow` on failure.
*   **AuthSocket**: `GetOrCalculateHash` is called by `AuthSocket/_HandleLogonProof__PostRecv_HandleInvalidVersion` during authentication to verify patch integrity.

## Data Model

This unit does not interact with any database tables. It operates entirely on the local filesystem and in-memory structures.

## Notable Implementation Details

1.  **Lock Granularity**: `GetOrCalculateHash` releases the mutex before calling `CalculateAndCacheHash` to avoid blocking other threads during slow I/O. This creates a race condition where multiple threads might recalculate the same hash simultaneously, but correctness is preserved as the result is identical.
2.  **Handle Duplication**: `GetOrCalculateHash` duplicates the file handle before passing it to `CalculateAndCacheHash`. This ensures the original handle remains valid for the caller (e.g., to stream data to a client), as `CalculateAndCacheHash` takes ownership and consumes the handle.
3.  **Change Detection**: Validity is determined by `fileSize` and `lastModifyDate`. Changes in these fields trigger recomputation. This relies on accurate filesystem timestamps; identical content with changed timestamps causes unnecessary recomputation, while preserved timestamps on modified content may serve stale hashes.
4.  **Chunked I/O**: Files are read in 1 MiB chunks to balance memory usage and I/O efficiency.
5.  **Singleton Access**: Accessed globally via `sRealmdPatchCache`.

## Member Reference

**ClientPatchCache**
Constructor for the singleton. Initializes the object and immediately calls `LoadPatchesInfo` to populate the cache with patch files from the configured directory.

**LoadPatchesInfo**
Private method that scans the `PatchesDir` for files. For each file, it attempts to open a read-only handle. If successful, it calls `CalculateAndCacheHash` to compute and store the MD5 hash. If opening fails, it logs an error. Uses `Config/GetStringDefault`, `FileSystem/ToAbsolutePath`, `FileSystem/GetAllFilesInFolder`, `FileSystem/TryOpenFileReadonly`, and `Log.Main/Out`.

**GetOrCalculateHash**
Public method to retrieve the MD5 hash for a given file handle. It checks the cache for an existing entry matching the file's path, size, and modification date. If a match is found, it returns the cached hash. If not, it logs the change, duplicates the file handle, and calls `CalculateAndCacheHash` to update the cache and return the new hash. Uses `FileHandle/GetFilePath`, `FileHandle/GetLastModifyDate`, `FileHandle/GetTotalFileSize`, `FileHandle/DuplicateFileHandle`, and `Log.Main/Out`. Called by `AuthSocket/_HandleLogonProof__PostRecv_HandleInvalidVersion`.

**CalculateAndCacheHash**
Private method that computes the MD5 hash of a file by reading it in 1 MiB chunks. It constructs a `PatchCacheEntry` with the file's metadata and hash, then stores it in the `m_knownPatches` map under a mutex lock. It takes ownership of the provided file handle. Uses `FileHandle/GetFilePath`, `FileHandle/GetLastModifyDate`, `FileHandle/GetTotalFileSize`, `FileHandle/ReadSync`, `Generator.MD5/Generator`, `Generator.MD5/GetDigest`, `Generator.MD5/UpdateData#4`, and potentially `Errors/PrintStacktraceAndThrow`.

---

<!-- machine-true, projected from graph.json -->

## Map — ClientPatchCache

*Source:* ClientPatchCache.cpp, ClientPatchCache.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ClientPatchCache | ctor | — | — | — |
| LoadPatchesInfo | method | Config/GetStringDefault, FileSystem/GetAllFilesInFolder, FileSystem/ToAbsolutePath, FileSystem/TryOpenFileReadonly, Log.Main/Out | — | — |
| GetOrCalculateHash | method | FileHandle/DuplicateFileHandle, FileHandle/GetFilePath, FileHandle/GetLastModifyDate, FileHandle/GetTotalFileSize, Log.Main/Out | AuthSocket/_HandleLogonProof__PostRecv_HandleInvalidVersion | — |
| CalculateAndCacheHash | method | Errors/PrintStacktraceAndThrow, FileHandle/GetFilePath, FileHandle/GetLastModifyDate, FileHandle/GetTotalFileSize, FileHandle/ReadSync, Generator.MD5/Generator, Generator.MD5/GetDigest, Generator.MD5/UpdateData#4 | — | — |
