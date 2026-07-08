# CreateThread

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`CreateThread` provides a thin abstraction layer over standard C++ threading primitives (`std::thread`) to ensure that every thread created within the MaNGOS server infrastructure carries a human-readable name. This naming capability is critical for debugging, profiling, and monitoring thread utilization in production environments, as standard `std::thread` objects do not expose names to debuggers or system monitors by default.

The unit exposes two factory functions for creating threads—one returning a raw `std::thread` and one returning a `std::unique_ptr<std::thread>`—and a utility function to rename the currently executing thread. Both factory functions automatically invoke the renaming utility immediately upon thread startup, ensuring that the name is set before the user-provided entry function begins execution. The implementation is strictly platform-specific, utilizing Windows structured exception handling for legacy debugger compatibility on Windows, and POSIX `pthread_setname_np` variants for Linux, BSD, and macOS.

## Member-by-Member Behavior

### Thread Creation Factories

**`CreateThread`** and **`CreateThreadPtr`** serve identical logical purposes: they instantiate a new OS-level thread and assign it a name. The distinction lies solely in the ownership semantics of the returned object.

1.  **Lambda Capture**: Both functions capture the provided `name` by value and the `entryFunction` by move semantics. This ensures the name string remains valid for the duration of the thread's setup, even if the caller destroys their local copy of the string immediately after the call.
2.  **Automatic Renaming**: Inside the lambda body, `IO::Multithreading::RenameCurrentThread(name)` is called *before* `entryFunction()` is invoked. This guarantees that the thread appears with its designated name in debuggers and system tools from the very first instruction of the user's logic.
3.  **Return Values**:
    *   `CreateThread` returns a `std::thread` by value. The caller is responsible for joining or detaching this thread. The `[[nodiscard]]` attribute warns the compiler if the return value is ignored, preventing accidental resource leaks or detached threads with no handle.
    *   `CreateThreadPtr` wraps the `std::thread` in a `std::unique_ptr`. This is useful for scenarios where the thread object needs to be stored in containers or managed via smart pointer lifecycles. Like its counterpart, it is marked `[[nodiscard]]`.

### Thread Naming Utility

**`RenameCurrentThread`** performs the low-level OS-specific work of assigning a name to the calling thread. It contains no synchronization primitives, as thread naming is inherently a per-thread operation.

*   **Windows Implementation**: On `WIN32`, the code uses a well-known hack involving `RaiseException` with the specific exception code `MS_VC_EXCEPTION` (0x406D1388). This exception is caught by Visual Studio debuggers (and compatible tools) to display the thread name in the Threads window. The code explicitly avoids `SetThreadDescription` (available in Windows 10+) in favor of this older mechanism, likely to maintain compatibility with older Windows versions or specific debugger behaviors expected by the project. The `__try`/`__except` block ensures the exception is handled locally and does not propagate up the stack, effectively making it a silent side-effect.
*   **POSIX Implementation**: On Linux, FreeBSD, NetBSD, OpenBSD, and macOS, the function calls `pthread_setname_np`. Note the argument order difference: Linux/BSD variants take `(pthread_t, const char*)`, while macOS takes `(const char*)`. The preprocessor directives correctly route to the appropriate signature.
*   **Unsupported Platforms**: If compiled on a platform not covered by these macros, a compile-time warning is issued, and the function becomes a no-op. The comment notes that failing to rename a thread is "not too serious," indicating this is a diagnostic convenience rather than a functional requirement.

## Cross-Unit Boundaries

This unit acts as a foundational utility, called by various high-level subsystems to initialize background tasks. It does not call out to other application units; its dependencies are limited to OS headers (`<Windows.h>`, `<pthread.h>`).

*   **Called by `MaNGOSsoap/StartSoapThread`**: Uses `CreateThreadPtr` to launch the SOAP interface thread. The use of `unique_ptr` suggests the SOAP subsystem manages the thread's lifecycle dynamically.
*   **Called by `Master/Run`**: The main server process uses both `CreateThread` and `RenameCurrentThread`. It likely renames the main thread itself for visibility and spawns worker threads using `CreateThread`.
*   **Called by `World/SetInitialWorldSettings`**: Uses `CreateThreadPtr` during world initialization, possibly for asynchronous configuration loading or network binding tasks.
*   **Called by `Anticheat/StartWardenUpdateThread`**: Uses `CreateThread` to start the Warden anticheat update loop.
*   **Called by `AsyncSystemTimer/AsyncSystemTimer`**: Uses `CreateThread` to initialize the timer subsystem's background thread.
*   **Called by `Database/InitDelayThread`**: Uses `CreateThread` to handle delayed database initialization tasks.
*   **Called by `realmd_Main/main`**: The realm daemon uses both `CreateThread` and `RenameCurrentThread` to set up its main event loop and worker threads.
*   **Called by `ThreadPool/worker`**: Uses `CreateThread` to spawn individual workers in the general-purpose thread pool.

## Data Model

This unit does not interact with any database tables. It operates entirely in memory and relies on OS-level APIs for thread management.

## Notable Implementation Details

1.  **Windows Exception Hack**: The Windows implementation of `RenameCurrentThread` relies on raising a software exception (`RaiseException`). While this is a standard technique for Visual Studio integration, it is technically unsafe in strict exception-safe contexts because it bypasses C++ exception handling mechanisms. However, since it is wrapped in a `__try`/`__except` block that swallows the exception, it does not affect C++ stack unwinding. Maintainers should be aware that this code will not work correctly if compiled with `/EHsc` (C++ exceptions only) without the specific SEH support enabled, though the `__try` syntax implies SEH is intended.
2.  **Platform-Specific Argument Order**: The code correctly handles the differing signatures of `pthread_setname_np` between Linux/BSD and macOS. A common bug in cross-platform code is swapping these arguments, but this implementation is correct.
3.  **No Error Handling for Naming**: The function ignores return values from `pthread_setname_np`. On some systems, this call can fail (e.g., if the name is too long or permissions are insufficient). The design choice here is to treat naming as a best-effort diagnostic feature; failure to name a thread does not prevent the thread from running.
4.  **Move Semantics for Entry Function**: The `entryFunction` is moved into the lambda capture. This allows callers to pass expensive-to-copy function objects or lambdas with captured resources efficiently. However, it means the caller cannot reuse the `entryFunction` variable after passing it to `CreateThread`.

## Member Reference

**CreateThreadPtr**
Creates a new `std::thread` wrapped in a `std::unique_ptr`. Captures the thread name and entry function, renames the thread immediately upon start, then executes the entry function. Returns the unique pointer to the thread object. Marked `[[nodiscard]]` to enforce proper lifecycle management.

**CreateThread**
Creates a new `std::thread` by value. Captures the thread name and entry function, renames the thread immediately upon start, then executes the entry function. Returns the thread object directly. Marked `[[nodiscard]]` to enforce proper lifecycle management.

**RenameCurrentThread**
Assigns a human-readable name to the currently executing thread. On Windows, it raises a structured exception (`MS_VC_EXCEPTION`) to signal the debugger. On POSIX systems (Linux, BSD, macOS), it calls `pthread_setname_np` with platform-appropriate arguments. On unsupported platforms, it issues a compile-time warning and does nothing. This function is called internally by the `CreateThread` factories but is also exposed for direct use by units like `Master` and `realmd_Main` to rename existing threads (such as the main thread).

---

<!-- machine-true, projected from graph.json -->

## Map — CreateThread

*Source:* CreateThread.cpp, CreateThread.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| CreateThreadPtr | function | — | MaNGOSsoap/StartSoapThread, Master/Run, World/SetInitialWorldSettings | — |
| CreateThread | function | — | Anticheat/StartWardenUpdateThread, AsyncSystemTimer/AsyncSystemTimer, Database/InitDelayThread, Master/Run, realmd_Main/main, ThreadPool/worker | — |
| RenameCurrentThread | function | — | Master/Run, realmd_Main/main | — |
