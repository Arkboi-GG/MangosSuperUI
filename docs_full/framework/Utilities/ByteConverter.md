<!-- provenance: verbose -->
# ByteConverter

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`ByteConverter` is a header-only utility namespace that provides compile-time and runtime mechanisms for byte-swapping (endianness conversion) of primitive data types. It abstracts differences between little-endian and big-endian architectures, ensuring multi-byte integers and floats are interpreted correctly regardless of the host machine's native byte order.

Behavior is determined by the `MANGOS_ENDIAN` macro:
*   **Little-Endian Targets**: `EndianConvert` performs the swap; `EndianConvertReverse` is a no-op.
*   **Big-Endian Targets**: `EndianConvert` is a no-op; `EndianConvertReverse` performs the swap.

This allows the codebase to call these functions unconditionally, relying on the preprocessor to select the correct implementation.

## Member-by-Member Behavior

### Internal Byte Swapping Logic

*   **`convert` (Recursive Template)**: The core engine for reversing byte order. It takes a `char*` pointer and a template parameter `T` (byte count). It swaps the first and last bytes of the current range, then recursively calls itself with size `T-2` and an advanced pointer.
*   **`convert#2` (Base Cases)**: Explicit template specializations for `convert<0>` and `convert<1>` that do nothing, terminating the recursion. The `convert<1>` case handles the central byte of odd-sized types.

### Public API Functions

*   **`EndianConvert`**:
    *   On **Little-Endian** builds: Calls `ByteConverter::apply` to swap bytes.
    *   On **Big-Endian** builds: No-op.
    *   **Specializations**: Explicitly defined as no-ops for `uint8` and `int8`.
    *   **Pointer Overload**: Declared but undefined (`template<typename T> void EndianConvert(T*)`). Passing a pointer causes a linker error, enforcing reference usage.

*   **`EndianConvertReverse`**:
    *   On **Big-Endian** builds: Calls `ByteConverter::apply` to swap bytes.
    *   On **Little-Endian** builds: No-op.
    *   **Specializations**: Explicitly defined as no-ops for `uint8` and `int8`.
    *   **Pointer Overload**: Declared but undefined, serving the same safety purpose as `EndianConvert`.

*   **`apply`**: Internal helper casting a typed pointer `T*` to `char*` and initiating `convert<sizeof(T)>`.

## Cross-Unit Boundaries

According to the MAP, `ByteConverter` has **no external dependencies** and is **not called by** any other specific tracked units. It is a self-contained utility, likely included by network serialization or packet parsing classes elsewhere in the codebase.

## Data Model

This unit does not interact with any database tables. It operates purely on in-memory binary representations.

## Notable Implementation Details

1.  **Linker Error Safety**: The undefined pointer overloads for `EndianConvert` and `EndianConvertReverse` intentionally cause linker errors if a pointer is passed instead of a reference, preventing accidental misuse.
2.  **Single-Byte Optimization**: Explicit no-op specializations for `uint8` and `int8` avoid unnecessary overhead for single-byte types.
3.  **Recursive Templates**: The `convert` function uses compile-time recursion. While effective, it generates multiple function calls at runtime unless optimized away by the compiler.
4.  **Preprocessor Dependency**: Correct behavior relies entirely on `MANGOS_ENDIAN` being correctly defined for the target architecture.

## Member Reference

**convert**
Recursive template function swapping bytes in a `char*` buffer by exchanging the first and last bytes and recursing inward until the size is 0 or 1.

**convert#2**
Explicit template specializations for `convert<0>` and `convert<1>`. Both are empty functions serving as base cases to terminate recursion.

**EndianConvert**
On Little-Endian systems, swaps the byte order of the referenced value `T&`. On Big-Endian systems, it is a no-op. Specialized as no-ops for `uint8` and `int8`. Pointer versions are declared but undefined to cause linker errors on misuse.

**EndianConvert#2**
Refers to the specialized no-op implementations of `EndianConvert` for `uint8` and `int8` types, ensuring single-byte values are processed efficiently without function overhead.

**EndianConvertReverse**
On Big-Endian systems, swaps the byte order of the referenced value `T&`. On Little-Endian systems, it is a no-op. Specialized as no-ops for `uint8` and `int8`. Pointer versions are declared but undefined to cause linker errors on misuse.

**EndianConvertReverse#2**
Refers to the specialized no-op implementations of `EndianConvertReverse` for `uint8` and `int8` types, ensuring single-byte values are processed efficiently without function overhead.

---

<!-- machine-true, projected from graph.json -->

## Map — ByteConverter

*Source:* ByteConverter.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| convert | function | — | — | — |
| convert#2 | function | — | — | — |
| EndianConvert | function | — | — | — |
| EndianConvert#2 | function | — | — | — |
| EndianConvertReverse | function | — | — | — |
| EndianConvertReverse#2 | function | — | — | — |
