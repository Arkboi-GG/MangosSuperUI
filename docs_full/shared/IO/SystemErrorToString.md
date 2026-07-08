# SystemErrorToString

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# SystemErrorToString

**Purpose & Responsibilities**

`SystemErrorToString` provides a cross-platform utility for converting native operating system error codes (e.g., `errno` on POSIX, `GetLastError()` on Windows) into human-readable strings. It exposes two interfaces: `SystemErrorToString`, which returns a safe `std::string` suitable for logging, and `SystemErrorToCString`, an internal helper returning a `const char*` backed by thread-local storage to avoid heap allocations.

## Member-by-Member Behavior

**`SystemErrorToString`**
The primary public entry point. It accepts an `int nativeSystemErrorCode` and returns a `std::string` formatted as `(code) message`. It delegates the textual lookup to `SystemErrorToCString` and prepends the numeric code in parentheses. This ensures the specific error identifier is preserved even if the textual description is generic or unavailable.

**`SystemErrorToCString`**
An internal function that resolves the error code to a C-string using a `thread_local` buffer (`g_threadLocalStorage`, 256 bytes). Behavior varies by platform:
*   **Windows**: Calls `FormatMessageA` with `FORMAT_MESSAGE_FROM_SYSTEM`. Returns `"<Unable to generate error text>"` on failure.
*   **Linux**: Calls the GNU variant of `strerror_r`, which returns a `char*`. The function returns this pointer directly (which may point to the local buffer or a static internal buffer).
*   **Other POSIX (macOS, BSD)**: Calls the POSIX variant of `strerror_r`, which returns an `int`. Returns `"<Unable to generate error text>"` if the return value is non-zero; otherwise, returns the local buffer.
*   **Unsupported**: Triggers a compile-time `#error`.

## Cross-Unit Boundaries

This unit has no outgoing dependencies on other custom units. It is widely consumed by the I/O subsystem to translate low-level system failures into readable logs. Callers include:
*   **AsyncSocket._posix**: `InitializeAndFixateMemoryLocation`, `OnIoEvent`, `PerformNonBlockingRead`, `PerformNonBlockingWrite`.
*   **AsyncSocketAcceptor_posix**: `CreateAndBindServer`, `OnNewClientToAcceptAvailable`.
*   **DNS**: `GetOwnHostname`, `ResolveDomainAll`.
*   **FileHandle**: `DuplicateFileHandle`, `GetLastModifyDate`, `GetTotalFileSize`, `ReadSync`.
*   **FileSystem**: `ToAbsolutePath`, `TryOpenFileReadonly`.
*   **IoContext_linux**: `CreateIoContext`, `RunUntilShutdown`.
*   **NetworkError**: `ToString`.

## Data Model

This unit does not interact with any database tables.

## Notable Implementation Details

1.  **Thread-Local Buffer**: `SystemErrorToCString` uses `thread_local char g_threadLocalStorage[256]`. It is not reentrant within a single thread; storing the returned pointer and calling the function again before copying the data results in undefined behavior. `SystemErrorToString` mitigates this by immediately converting the result to a `std::string`.
2.  **`strerror_r` Incompatibility**: The code explicitly branches for Linux vs. other POSIX systems due to differing `strerror_r` signatures (GNU returns `char*`, POSIX returns `int`). The Linux branch assumes success unless the pointer is null (though no explicit null check is performed in the source).
3.  **Fixed Buffer Size**: The 255-character limit truncates excessively long error messages, though this is rare for standard system errors.

## Member Reference

**SystemErrorToCString**
Internal helper converting a native error code to a `const char*` via thread-local storage. Handles platform-specific APIs (`FormatMessageA` on Windows, `strerror_r` on POSIX) and returns a fallback string on failure for Windows and non-Linux POSIX.

**SystemErrorToString**
Public function converting a native error code to a `std::string` formatted as `(code) message`. Calls `SystemErrorToCString` for the text portion. Used by I/O and network units for error reporting.

---

<!-- machine-true, projected from graph.json -->

## Map — SystemErrorToString

*Source:* SystemErrorToString.cpp, SystemErrorToString.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| SystemErrorToCString | function | — | — | — |
| SystemErrorToString | function | — | AsyncSocket._posix/InitializeAndFixateMemoryLocation, AsyncSocket._posix/OnIoEvent, AsyncSocket._posix/PerformNonBlockingRead, AsyncSocket._posix/PerformNonBlockingWrite, AsyncSocketAcceptor_posix/CreateAndBindServer, AsyncSocketAcceptor_posix/OnNewClientToAcceptAvailable, DNS/GetOwnHostname, DNS/ResolveDomainAll, FileHandle/DuplicateFileHandle, FileHandle/GetLastModifyDate, FileHandle/GetTotalFileSize, FileHandle/ReadSync, FileSystem/ToAbsolutePath, FileSystem/TryOpenFileReadonly, IoContext_linux/CreateIoContext, IoContext_linux/RunUntilShutdown, NetworkError/ToString | — |
