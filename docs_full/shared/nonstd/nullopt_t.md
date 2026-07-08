# nullopt_t

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# `nullopt_t`

## Purpose & Responsibilities

`nullopt_t` is a lightweight tag type used within the `nonstd::optional` implementation (a C++11-compatible backport of `std::optional`) to explicitly represent the absence of a value. It serves as the canonical token for constructing an empty `optional`, assigning an `optional` to an empty state, and comparing an `optional` against emptiness.

The type is designed to be distinct from other types to prevent accidental implicit conversions. Its constructor is `explicit` and requires two dummy arguments of a private nested type (`do_not_use`), ensuring that instances of `nullopt_t` cannot be created inadvertently by user code. Instead, users interact with the pre-defined global constant `nonstd::nullopt`.

## Member-by-Member Behavior

### Construction

**`nullopt_t` (Constructor)**
The constructor is `constexpr` and `explicit`. It takes two arguments of type `nullopt_t::do_not_use`. This design prevents direct instantiation by users, as `do_not_use` is a private nested struct with no accessible constructors. The only instance of `nullopt_t` available to users is the static constant `nonstd::nullopt`, which is initialized with two temporary `do_not_use` objects.

## Cross-Unit Boundaries

`nullopt_t` itself has no outgoing calls and is not called by other units in the sense of function invocation. However, it is heavily integrated with the `nonstd::optional<T>` class template defined in the same header (`optional.hpp`).

*   **Called by `nonstd::optional<T>`:**
    *   **Construction:** `optional<T>` has a constructor taking `nullopt_t` to initialize an empty optional.
    *   **Assignment:** `optional<T>` has an assignment operator `operator=(nullopt_t)` to reset the optional to an empty state.
    *   **Comparison:** Free-standing comparison operators (`==`, `!=`, `<`, `<=`, `>`, `>=`) are overloaded to compare `optional<T>` instances directly with `nullopt_t`. These operators allow idiomatic checks like `if (opt == nullopt)`.

## Data Model

This unit does not interact with any database tables. It is a pure C++ utility type.

## Notable Implementation Details

1.  **Prevention of Accidental Construction:** The use of a private nested struct `do_not_use` as constructor arguments ensures that `nullopt_t` is a singleton-like tag in practice, despite being a regular class. Users cannot write `nullopt_t x;`; they must use `nonstd::nullopt`.
2.  **Constexpr Compatibility:** The constructor is marked `constexpr`, allowing `nullopt_t` instances (specifically the global `nullopt`) to be used in constant expressions, which is essential for compile-time evaluation in modern C++.
3.  **Namespace:** The type resides in the `nonstd` namespace, which is a custom namespace used in this codebase (renamed from the original `tl` namespace of the TartanLlama library) to avoid conflicts with standard library namespaces while providing a clear indication that it is a non-standard implementation.
4.  **No State:** The class contains no data members. It is an empty class, relying solely on its type identity for its functionality.

## Member Reference

**`nullopt_t`**
Constructor for the tag type representing an empty optional. It is `explicit` and `constexpr`, taking two dummy arguments of a private nested type `do_not_use` to prevent accidental instantiation. Users should always use the provided global constant `nonstd::nullopt` instead of attempting to construct this type directly.

---

<!-- machine-true, projected from graph.json -->

## Map — nullopt_t

*Source:* optional.hpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| nullopt_t | ctor | — | — | — |
