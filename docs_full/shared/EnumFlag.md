<!-- provenance: verbose -->
# EnumFlag

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`EnumFlag` provides a type-safe interface for manipulating C++ enumerations as bitmasks. It offers two distinct mechanisms:
1.  **Free-function operators** for raw enum types registered via the `DEFINE_ENUM_FLAG` macro, enabled via SFINAE.
2.  **The `EnumFlag<T>` class template**, which wraps a flag enum `T` to provide methods for inspection (`HasFlag`, `HasAllFlags`) and modification (`RemoveFlag`), alongside implicit conversion to/from the raw enum type.

This unit prevents accidental mixing of incompatible enum types and eliminates manual casting to underlying integer types for bitwise operations.

## Member-by-Member Behavior

### Registration and Traits
The unit uses the `DEFINE_ENUM_FLAG(enumType)` macro to specialize `IsEnumFlag` for a given type. The `EnumTraits::IsFlag<T>` trait checks this specialization. Free-function operators use `std::enable_if` to constrain themselves to types where `IsFlag<T>::value` is true. The `EnumFlag<T>` class enforces this constraint via `static_assert`.

### Raw Enum Operators
These free functions operate on raw enum values `T` marked with `DEFINE_ENUM_FLAG`. They cast operands to `std::underlying_type_t<T>`, perform the bitwise operation, and cast the result back to `T`.
*   **`operator&`**, **`operator|`**, **`operator~`**: Return a new enum value resulting from bitwise AND, OR, or NOT.
*   **`operator&=`**, **`operator|=`**: Modify the left operand in place and return a reference to it.

### `EnumFlag<T>` Wrapper
The `EnumFlag<T>` class stores a private `_value` of type `T`.

*   **Construction & Conversion**: The constructor is implicit and `constexpr`. An implicit conversion operator `operator T()` allows seamless interchange with raw enums.
*   **Bitwise Operators**: `operator&=`, `operator|=`, and `operator~` mirror the raw enum operators but operate on `EnumFlag` instances. Binary operators `operator&` and `operator|` are friend functions that delegate to the compound assignment operators.
*   **Inspection**: `HasFlag(T)` checks if a single bit is set. `HasAllFlags(T)` checks if all bits in the mask are set. Both are `constexpr`.
*   **Modification**: `RemoveFlag(EnumFlag)` clears the bits specified by the argument.
*   **Extraction**: `AsUnderlyingType()` returns the raw integer value.

## Cross-Unit Boundaries

This unit is self-contained. It defines no dependencies on other units and is not called by any other units in the provided MAP. It serves as a foundational utility included by headers that define flag enums.

## Data Model

This unit does not interact with any database tables.

## Notable Implementation Details

*   **Compile-Time Safety**: The `static_assert` in `EnumFlag<T>` ensures that only enums explicitly marked with `DEFINE_ENUM_FLAG` can be wrapped. Attempting to wrap an unmarked enum results in a clear compile error.
*   **Implicit Conversions**: Both the constructor and `operator T()` are implicit. This allows `EnumFlag<T>` to be used transparently in contexts expecting `T`, and vice versa, but requires care to avoid unintended conversions.
*   **Constexpr Compatibility**: Most members are `constexpr`, allowing flag operations to be resolved at compile time.
*   **Underlying Type Casting**: All bitwise logic relies on casting to `std::underlying_type_t<T>` because C++ does not support native bitwise operations on `enum class` types.

## Member Reference

**`EnumFlag<T>`**: Constructor initializing `_value` from a raw enum `T`. Implicit, `constexpr`, and guarded by `static_assert` requiring `T` to be a registered flag type.

**`operator&=`**: Compound assignment for `EnumFlag<T>`. Bitwise ANDs `_value` with the argument's `_value` and returns `*this`.

**`operator&`**: Friend function returning a new `EnumFlag<T>` resulting from bitwise AND of two instances. Delegates to `operator&=`.

**`operator|=`**: Compound assignment for `EnumFlag<T>`. Bitwise ORs `_value` with the argument's `_value` and returns `*this`.

**`operator|`**: Friend function returning a new `EnumFlag<T>` resulting from bitwise OR of two instances. Delegates to `operator|=`.

**`operator~`**: Unary operator returning a new `EnumFlag<T>` with the bitwise NOT of `_value`.

**`RemoveFlag`**: Clears the bits specified by the argument `EnumFlag` from `_value` using `_value &= ~flag._value`.

**`HasFlag`**: Returns `true` if the single bit specified by the raw enum `flag` is set in `_value`. Uses underlying type casting for the check.

**`HasAllFlags`**: Returns `true` if all bits specified by the raw enum `flags` are set in `_value`. Checks `(_value & flags) == flags`.

**`AsUnderlyingType`**: Returns `_value` cast to `std::underlying_type_t<T>`.

---

<!-- machine-true, projected from graph.json -->

## Map — EnumFlag

*Source:* EnumFlag.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| EnumFlag<T> | ctor | — | — | — |
| operator&= | function | — | — | — |
| operator& | function | — | — | — |
| operator|= | function | — | — | — |
| operator| | function | — | — | — |
| operator~ | function | — | — | — |
| RemoveFlag | function | — | — | — |
| HasFlag | function | — | — | — |
| HasAllFlags | function | — | — | — |
| AsUnderlyingType | function | — | — | — |
