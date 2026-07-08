<!-- provenance: verbose -->
# FileSystem

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# FileSystem

`FileSystem` provides thin POSIX wrappers for file I/O, path resolution, and directory enumeration within the `IO::Filesystem` namespace. It handles low-level system calls (`open`, `getcwd`, `opendir`) and translates errors into logs or exceptions, shielding callers from raw `errno` handling.

## Purpose & Responsibilities

1.  **File Opening**: `TryOpenFileReadonly` opens files in read-only mode, returning a managed handle or `nullptr` on failure.
2.  **Path Resolution**: `ToAbsolutePath` converts relative paths (specifically those prefixed with `./`) to absolute paths using the current working directory.
3.  **Directory Listing**: `GetAllFilesInFolder` enumerates regular files in a directory, supporting both filename-only and full-path outputs.

## Member-by-Member Behavior

### File Access

**TryOpenFileReadonly**
Opens `filePath` using `open(O_RDONLY)`. On success, it wraps the file descriptor in a `FileHandleReadonly` object (via `FileHandleReadonly/FileHandleReadonly`) and returns it in a `std::unique_ptr`. On failure, it logs the error via `Log.Main/Out` using the human-readable string from `SystemErrorToString/SystemErrorToString` and returns `nullptr`.
-   **Called by**: `AuthSocket/_HandleLogonProof__PostRecv_HandleInvalidVersion`, `ClientPatchCache/LoadPatchesInfo`.

### Path Manipulation

**ToAbsolutePath**
Resolves `partialPath` to an absolute path. If the path starts with `/`, it is returned unchanged. If it starts with `./`, the prefix is stripped, and the current working directory (from `getcwd`) is prepended. If `getcwd` fails, it throws `std::runtime_error` with details from `SystemErrorToString/SystemErrorToString`. It does not canonicalize internal `..` or `.` components.
-   **Called by**: `AuthSocket/_HandleLogonProof__PostRecv_HandleInvalidVersion`, `ClientPatchCache/LoadPatchesInfo`.

### Directory Operations

**GetAllFilesInFolder**
Lists regular files in `folderPath` non-recursively. It opens the directory with `opendir` and iterates entries via `readdir`. Each entry is validated as a regular file using `stat` and `S_ISREG`. Depending on `filePathOption`, it returns either just the filename (`OutputFilePath::JustFileName`) or the full path (`OutputFilePath::FullFilePath`). It manually closes the directory handle with `closedir`.
-   **Called by**: `ClientPatchCache/LoadPatchesInfo`, `WardenModuleMgr/GetModuleNames`.

## Cross-Unit Boundaries

### Outgoing Calls

| Target Unit | Member | Purpose |
|---|---|---|
| `FileHandleReadonly` | Constructor | Wraps native file descriptors into managed objects. |
| `Log.Main` | `Out` | Logs file open failures. |
| `SystemErrorToString` | `SystemErrorToString` | Converts `errno` to readable strings for logs/exceptions. |

### Incoming Calls

| Caller Unit | Member | Purpose |
|---|---|---|
| `AuthSocket` | `_HandleLogonProof__PostRecv_HandleInvalidVersion` | Validates client versions by reading specific files. |
| `ClientPatchCache` | `LoadPatchesInfo` | Loads patch data by resolving paths and reading files. |
| `WardenModuleMgr` | `GetModuleNames` | Discovers security modules by listing directory contents. |

## Data Model

This unit does not interact with any database tables.

## Notable Implementation Details

1.  **Incomplete Canonicalization**: `ToAbsolutePath` only strips leading `./` and prepends the CWD. It does not resolve `..` or `.` segments within the path, relying on the kernel to interpret them later.
2.  **Mixed Error Strategies**: `TryOpenFileReadonly` returns `nullptr` for expected failures (missing files), while `ToAbsolutePath` throws `std::runtime_error` for `getcwd` failures, treating unknown CWD as critical.
3.  **Manual Resource Cleanup**: `GetAllFilesInFolder` uses raw `DIR*` pointers and manual `closedir` calls instead of RAII wrappers, though the current linear flow prevents leaks.

## Member Reference

**TryOpenFileReadonly**
Opens a file in read-only mode. Returns `std::unique_ptr<FileHandleReadonly>` on success, `nullptr` on failure. Logs errors via `Log.Main/Out` and `SystemErrorToString/SystemErrorToString`. Called by `AuthSocket/_HandleLogonProof__PostRecv_HandleInvalidVersion` and `ClientPatchCache/LoadPatchesInfo`.

**ToAbsolutePath**
Converts relative paths (starting with `./`) to absolute paths by prepending the current working directory. Throws `std::runtime_error` if `getcwd` fails, using `SystemErrorToString/SystemErrorToString`. Does not canonicalize `..` or `.`. Called by `AuthSocket/_HandleLogonProof__PostRecv_HandleInvalidVersion` and `ClientPatchCache/LoadPatchesInfo`.

**GetAllFilesInFolder**
Enumerates regular files in a directory. Filters using `stat`/`S_ISREG`. Returns filenames or full paths based on `OutputFilePath`. Manually closes directory handle. Called by `ClientPatchCache/LoadPatchesInfo` and `WardenModuleMgr/GetModuleNames`.

---

<!-- machine-true, projected from graph.json -->

## Map — FileSystem

*Source:* FileSystem.cpp, FileSystem.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| TryOpenFileReadonly | function | FileHandleReadonly/FileHandleReadonly, Log.Main/Out, SystemErrorToString/SystemErrorToString | AuthSocket/_HandleLogonProof__PostRecv_HandleInvalidVersion, ClientPatchCache/LoadPatchesInfo | — |
| ToAbsolutePath | function | SystemErrorToString/SystemErrorToString | AuthSocket/_HandleLogonProof__PostRecv_HandleInvalidVersion, ClientPatchCache/LoadPatchesInfo | — |
| GetAllFilesInFolder | function | — | ClientPatchCache/LoadPatchesInfo, WardenModuleMgr/GetModuleNames | — |
