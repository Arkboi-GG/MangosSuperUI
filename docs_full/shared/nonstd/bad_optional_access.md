# bad_optional_access

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# `bad_optional_access`

**Purpose & Responsibilities**

`bad_optional_access` is a lightweight exception class defined within the `nonstd` namespace (a port of the `tl::optional` library). Its sole responsibility is to serve as the standard error signal when code attempts to retrieve the value from an `optional<T>` object that is currently empty (disengaged). It inherits from `std::exception`, allowing it to be caught by standard exception handling mechanisms.

This unit contains no database interactions, no cross-unit dependencies, and no complex logic. It is a minimal, self-contained component designed for integration with the broader `optional` template class defined in the same header (`optional.hpp`).

## Member-by-Member Behavior

The class exposes two members: a default constructor and a `what()` method.

### Construction
The **`bad_optional_access`** constructor is defaulted. It performs no initialization logic beyond invoking the default constructor of its base class, `std::exception`. This ensures that creating an instance of this exception is cheap and side-effect-free.

### Error Reporting
The **`what`** method overrides the virtual interface provided by `std::exception`. It returns a static string literal: `"Optional has no value"`. This message is constant, thread-safe, and requires no dynamic memory allocation. It provides a human-readable description of the failure mode (accessing an empty optional) for debugging or logging purposes.

## Cross-Unit Boundaries

According to the provided MAP, `bad_optional_access` has no outgoing calls to other units and is not listed as being called by other units in the cross-reference table. However, in practice, this class is instantiated and thrown by the `value()` methods of the `nonstd::optional<T>` template class (also defined in `optional.hpp`) when `has_value()` returns `false`. The boundary interaction is strictly internal to the `optional` library implementation:
*   **Direction:** Outbound from `optional<T>::value()` to `bad_optional_access`.
*   **Data:** An instance of `bad_optional_access` is thrown.
*   **Reason:** To enforce safe access semantics; users must check `has_value()` or use `value_or()`/`value()` with try-catch blocks.

## Data Model

This unit does not interact with any database tables. It is a pure C++ utility class.

## Notable Implementation Details

1.  **Minimal Footprint:** The class contains no member variables. It relies entirely on the base class `std::exception` for its exception-handling infrastructure.
2.  **Static Message:** The `what()` method returns a string literal rather than constructing a `std::string` or accessing a member variable. This makes the exception type trivially copyable and extremely fast to instantiate, which is desirable for error paths that should ideally not occur in performance-critical code.
3.  **Namespace Placement:** Defined in `nonstd`, aligning with the rest of the optional library implementation in this header.

## Member Reference

**bad_optional_access**
Default constructor. Initializes the exception object by delegating to the default constructor of `std::exception`. No custom initialization logic is performed.

**what**
Virtual method overriding `std::exception::what()`. Returns the static C-string `"Optional has no value"`. This provides a descriptive error message for catch blocks handling this specific exception type.

---

<!-- machine-true, projected from graph.json -->

## Map — bad_optional_access

*Source:* optional.hpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| bad_optional_access | ctor | — | — | — |
| what | method | — | — | — |
