# FileHandleReadonly

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`FileHandleReadonly` is a non-copyable, non-movable RAII wrapper around a native operating system file handle, specifically designed for **read-only** access. It inherits from the abstract-like base class `FileHandle` (which provides common functionality like seeking and metadata retrieval) and adds the capability to synchronously read bytes from the file into a user-provided buffer.

This class is part of the `IO::Filesystem` namespace within the Mangos core I/O subsystem. Its primary responsibility is to provide a safe, high-level interface for reading file contents while ensuring the underlying native file descriptor is properly managed via the base class destructor. It explicitly forbids copying or moving instances, enforcing a strict ownership model where each `FileHandleReadonly` instance corresponds to exactly one open file handle.

## Member-by-Member Behavior

### Construction and Lifecycle
*   **`FileHandleReadonly`**: The constructor initializes the object by forwarding the file path and native handle to the `FileHandle` base class constructor. It establishes the RAII guard for the file resource. The copy and move constructors, as well as the assignment operators, are explicitly deleted (`= delete`) to prevent accidental duplication of the underlying OS handle, which would lead to double-closure errors or undefined behavior.

### Reading Data
*   **`ReadSync`**: This is the core functional method of the class. It attempts to read up to `amountToRead` bytes from the current file position into the memory buffer pointed to by `dest`.
    *   It returns the actual number of bytes read.
    *   If the return value is less than `amountToRead`, it indicates that the end of the file (EOF) was reached during the operation.
    *   An inline overload exists for `int8_t*` buffers, which simply casts the pointer to `uint8_t*` and delegates to the primary `uint8_t*` version. This allows callers to pass signed char buffers without manual casting.

### Handle Duplication
*   **`DuplicateFileHandle`**: Declared in the header but not defined in this unit (implementation likely resides in a corresponding `.cpp` file or another partial). It returns a `std::unique_ptr<FileHandleReadonly>` containing a new, independent file handle pointing to the same file. This is useful for scenarios where multiple readers need to access the same file concurrently or independently without sharing the same file position state.

## Cross-Unit Boundaries

*   **Called by `FileSystem/TryOpenFileReadonly`**: The `FileHandleReadonly` constructor is invoked by the `FileSystem` unit (specifically the `TryOpenFileReadonly` function/method). This indicates that `FileSystem` acts as the factory or coordinator for opening files. It handles the low-level OS calls to open the file, obtains the native handle, and then constructs a `FileHandleReadonly` object to hand back to the caller. This separation ensures that error handling for file opening is centralized in `FileSystem`, while `FileHandleReadonly` focuses purely on the semantics of reading an already-opened file.

*   **Calls into Base Class `FileHandle`**: Although not listed as "calls out" to a *different* unit in the map (since it's inheritance), `FileHandleReadonly` relies on `FileHandle` for:
    *   Storage of `m_filePath` and `m_nativeFileHandle`.
    *   Destruction of the native handle (via `~FileHandle()`).
    *   Seeking capabilities (`Seek`), though `FileHandleReadonly` itself does not expose or override these, they remain accessible to derived classes or internal use if needed (though typically, a readonly handle might just rely on sequential reads or explicit seeks via the base).

## Data Model

This unit does not interact with any database tables. It operates entirely on the local filesystem using native OS file handles.

## Notable Implementation Details

1.  **Strict Ownership Semantics**: The deletion of copy/move constructors and assignment operators is critical. Since the base class `FileHandle` likely closes the native handle in its destructor, allowing copies would result in multiple objects trying to close the same OS handle, causing crashes or data corruption.
2.  **Type Erasure for Buffers**: The inline overload of `ReadSync` for `int8_t*` demonstrates a design choice to accommodate both signed and unsigned byte buffers commonly used in C/C++ for binary data, avoiding the need for explicit `reinterpret_cast` at call sites.
3.  **EOF Detection Logic**: The documentation comment for `ReadSync` explicitly states that a return value smaller than `amountToRead` signifies EOF. Callers must check the return value to determine if the entire requested block was read or if the file ended prematurely. This is standard POSIX `read()` behavior, but it is crucial for correct parsing logic in higher-level layers.
4.  **No Internal Buffering**: Unlike `std::ifstream`, `FileHandleReadonly` appears to perform direct synchronous reads from the OS handle. There is no indication of internal buffering in the header. This means frequent small reads may incur significant syscall overhead, suggesting that callers should request larger chunks of data when possible.

## Member Reference

**FileHandleReadonly**  
Constructor that initializes the `FileHandle` base class with the provided file path and native OS file handle. Establishes RAII ownership of the file resource. Copy and move operations are deleted.

**FileHandleReadonly#3**  
Declaration of the deleted copy constructor `FileHandleReadonly(const FileHandleReadonly&)`. Prevents copying of file handles.

**operator=#2**  
Declaration of the deleted copy assignment operator `FileHandleReadonly& operator=(const FileHandleReadonly&)`. Prevents copying of file handles.

**FileHandleReadonly#2**  
Declaration of the deleted move constructor `FileHandleReadonly(FileHandleReadonly&&)`. Prevents moving of file handles.

**operator=**  
Declaration of the deleted move assignment operator `FileHandleReadonly& operator=(FileHandleReadonly&&)`. Prevents moving of file handles.

**ReadSync**  
Synchronously reads up to `amountToRead` bytes from the current file position into the `dest` buffer. Returns the number of bytes actually read. A return value less than `amountToRead` indicates EOF. Includes an inline overload for `int8_t*` buffers.

---

<!-- machine-true, projected from graph.json -->

## Map — FileHandleReadonly

*Source:* FileHandle.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| FileHandleReadonly | ctor | — | FileSystem/TryOpenFileReadonly | — |
| FileHandleReadonly#3 | decl | — | — | — |
| operator=#2 | decl | — | — | — |
| FileHandleReadonly#2 | decl | — | — | — |
| operator= | decl | — | — | — |
| ReadSync | method | — | — | — |
