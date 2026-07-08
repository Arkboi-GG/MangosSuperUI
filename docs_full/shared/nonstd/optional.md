<!-- provenance: failed-members -->
# optional

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# `nonstd::optional` Implementation (`optional.hpp`)

## Purpose & Responsibilities

This unit provides a complete, standalone implementation of `std::optional`, a wrapper type that manages the optional presence of a contained value of type `T`. It is designed for use in environments where C++17 (which introduced `std::optional`) is unavailable or unsupported, specifically targeting C++11 and C++14 compilers.

The implementation, originally authored by Sy Brand (TartanLlama) and distributed under the CC0 Public Domain Dedication, has been adapted for this codebase by renaming the namespace from `tl` to `nonstd`. It offers significant extensions beyond the C++17 standard library specification, including functional-style methods (`map`, `and_then`, `or_else`, `transform`) and specialized handling for reference types (`optional<T&>`).

Key responsibilities include:
1.  **Value Management:** Safely constructing, storing, and destructing a contained object `T` within a fixed-size storage buffer, tracking its lifetime via an engagement flag.
2.  **Compiler Compatibility:** Providing polyfills for C++14/17 utilities (such as `std::invoke`, `std::void_t`, `std::is_swappable`) and working around specific compiler bugs (notably in GCC 4.9–7 and MSVC 2015) regarding triviality traits and `noexcept` specifications.
3.  **Functional Composition:** Enabling chainable operations on optional values, allowing developers to write expressive, side-effect-free logic when dealing with potentially absent data.
4.  **Reference Semantics:** Implementing a partial specialization `optional<T&>` that behaves like a safe smart pointer, binding to lvalues without owning them.

## Member-by-Member Behavior

The implementation is structured using a hierarchy of base classes to manage the complexity of conditional triviality (ensuring `optional<int>` remains trivially copyable/movable/destructible while `optional<std::string>` does not).

### Core Storage and State

*   **`optional_storage_base`**: Manages the raw memory for the contained value. It uses a `union` containing a `dummy` type (to ensure non-empty union compliance in older standards) and the actual value `T`. It tracks engagement via `m_has_value`. Two specializations exist: one for trivially destructible types (no destructor needed) and one for non-trivially destructible types (explicit destructor calls `~T()` if engaged).
*   **`optional_operations_base`**: Provides low-level helper functions used by derived classes. `hard_reset` explicitly destroys the contained value and clears the flag. `construct` uses placement new to create the value. `assign` handles the logic of copying/moving values between optionals, respecting their engagement states. `has_value` and `get` provide access to the state and the raw reference.

### Conditional Triviality Hierarchy

To ensure `optional<T>` inherits the trivial properties of `T` where possible, the class derives from a sequence of bases that specialize based on type traits:

1.  **`optional_copy_base`**: Specializes based on `TL_OPTIONAL_IS_TRIVIALLY_COPY_CONSTRUCTIBLE(T)`. If false, it defines a custom copy constructor that checks `has_value()` and constructs the value manually.
2.  **`optional_move_base`**: Specializes based on `std::is_trivially_move_constructible(T)` (with GCC workarounds). If false, it defines a custom move constructor.
3.  **`optional_copy_assign_base`**: Specializes based on copy assignability and destructibility. If false, it defines a custom copy assignment operator calling `assign()`.
4.  **`optional_move_assign_base`**: Specializes based on move assignability and destructibility. If false, it defines a custom move assignment operator.

### Constructor/Assignment Deletion Control

*   **`optional_delete_ctor_base`** and **`optional_delete_assign_base`**: These templates conditionally delete copy/move constructors and assignment operators based on whether `T` itself supports these operations. For example, if `T` is not copy-constructible, `optional<T>`'s copy constructor is deleted. This ensures `optional` enforces the same constraints as the underlying type.

### Main Class Interface (`optional<T>`)

*   **Constructors**: Support default construction (empty), construction from `nullopt_t`, in-place construction via `in_place_t`, and converting construction from other `optional<U>` types or direct values `U`. Explicitness is controlled by `std::is_convertible`.
*   **Destruction**: Default destructor. Relies on `optional_storage_base` to handle conditional trivial destruction.
*   **Accessors**:
    *   `operator*` and `operator->`: Provide reference and pointer access to the contained value. Undefined behavior if empty.
    *   `value()`: Returns the value or throws `bad_optional_access` if empty.
    *   `value_or()`: Returns the value or a provided default.
    *   `has_value()`: Returns true if the optional contains a value.
*   **Modifiers**:
    *   `reset()`: Destroys the contained value and makes the optional empty.
    *   `emplace()`: Destroys the current value (if any) and constructs a new one in place with provided arguments.
    *   `swap()`: Exchanges the contents with another optional. Handles mixed states (one empty, one full) by moving the value.
*   **Functional Extensions**:
    *   `map()` / `transform()`: Applies a function to the contained value if present, returning an `optional` of the result. If the function returns `void`, it returns an `optional<monostate>`.
    *   `and_then()`: Chains operations that return `optional`s. If the current optional is empty, it returns an empty optional of the target type.
    *   `or_else()`: Executes a fallback function if the optional is empty.
    *   `map_or()` / `map_or_else()`: Applies a function if present, otherwise returns a default value or executes a fallback function.
    *   `conjunction()`: Returns the argument wrapped in an optional if the current optional is engaged, else empty.
    *   `disjunction()`: Returns the current value if engaged, else the alternative optional.
    *   `take()`: Extracts the value, leaving the current optional empty.

### Reference Specialization (`optional<T&>`)

*   **Storage**: Holds a `T*` pointer instead of a union. It does not own the pointed-to object.
*   **Behavior**: Acts as a safe wrapper around a raw pointer. Construction binds to an lvalue. `reset()` sets the pointer to `nullptr`. `swap()` exchanges pointers.
*   **Limitations**: Does not support move semantics in the same way as value types (moving an `optional<T&>` just copies the pointer). It cannot bind to rvalues.

## Cross-Unit Boundaries

This unit is a self-contained header-only library. It does not call out to other units in the `wowvmangos` codebase, nor is it called by them in a way that creates complex dependency graphs visible in the MAP. It relies solely on standard library headers (`<exception>`, `<functional>`, `<new>`, `<type_traits>`, `<utility>`).

*   **Calls Out**: None. All dependencies are resolved via standard library primitives or internal `nonstd::detail` implementations.
*   **Called By**: None listed in the MAP. In practice, this header is included by various other parts of the codebase to provide `optional` functionality, but the MAP indicates no specific cross-file callers for this documentation scope.

## Data Model

This unit does not interact with any database tables. It is a pure C++ utility type.

## Notable Implementation Details

1.  **GCC Triviality Workarounds**:
    *   GCC versions < 8 have a bug in `std::is_trivially_copy_constructible` for `std::vector`. The code defines a custom trait `nonstd::detail::is_trivially_copy_constructible` that specializes for `std::vector` to inherit the triviality of its element type, bypassing the compiler bug.
    *   GCC < 5 lacks `std::is_trivially_move_constructible` and proper overload resolution for `const&&`. The code disables move optimization and `const&&` overloads via macros (`TL_OPTIONAL_GCC49`, `TL_OPTIONAL_NO_CONSTRR`) for these versions.

2.  **MSVC 2015 Limitations**:
    *   MSVC 2015 (`_MSC_VER == 1900`) lacks proper `noexcept` deduction for `std::mem_fn` and some type traits. The code disables `constexpr` implications and simplifies swappability checks to `std::true_type` for this compiler.

3.  **Libc++ `std::mem_fn` Bug**:
    *   On libc++ with C++11, `std::mem_fn` causes hard errors in `noexcept` expressions for non-const member functions. The code implements a workaround trait `is_pointer_to_non_const_member_func` and excludes such cases from the `noexcept` specification of `invoke`.

4.  **Union Empty Member**:
    *   To comply with pre-C++20 requirements that unions must have at least one non-static data member, `optional_storage_base` includes a `struct dummy {}` alongside `T m_value` in the union.

5.  **Reference Specialization Safety**:
    *   `optional<T&>` strictly requires lvalues during construction and assignment (`static_assert(std::is_lvalue_reference<U>::value)`). This prevents accidental binding to temporaries, ensuring the reference remains valid as long as the original object exists.

6.  **Functional Method Return Types**:
    *   Methods like `map` and `and_then` have multiple overloads for different value categories (`&`, `&&`, `const &`, `const &&`) to preserve perfect forwarding and const-correctness. The C++11/14 implementations use explicit return type declarations (`decltype(...)`) because deduced return types (`auto`) were not SFINAE-friendly in those standards.

7.  **Exception Safety**:
    *   The `swap` member function is `noexcept` only if `T` is nothrow move-constructible and swappable. The free-standing `swap` function delegates to the member.
    *   `value()` throws `bad_optional_access` if the optional is empty.

8.  **Namespace Aliasing**:
    *   The entire library resides in `namespace nonstd`. The `std::hash` specialization for `nonstd::optional<T>` is injected into `namespace std` to allow use in unordered containers.

## Member Reference

**optional_storage_base<T, >**
Constructor for the storage base when `T` is not trivially destructible. Initializes the dummy member and sets `m_has_value` to false.

**~optional_storage_base<T, >**
Destructor for the storage base when `T` is not trivially destructible. If `m_has_value` is true, explicitly calls the destructor of `T` and sets the flag to false.

**optional_storage_base<type-parameter-0-0, true>**
Constructor for the storage base when `T` is trivially destructible. Initializes the dummy member and sets `m_has_value` to false. No destructor is generated, preserving trivial destructibility.

**hard_reset**
Explicitly destroys the contained value by calling its destructor and sets `m_has_value` to false. Used internally during assignment operations.

**has_value**
Returns the internal `m_has_value` flag, indicating whether the optional contains a value.

**get**
Returns a reference to the contained value `T`. Overloaded for lvalue, const lvalue, rvalue, and const rvalue contexts.

**get#3**
Overload of `get` for rvalue context, returning `T&&`.

**get#2**
Overload of `get` for const lvalue context, returning `const T&`.

**get#4**
Overload of `get` for const rvalue context (if supported), returning `const T&&`.

**optional_copy_base<type-parameter-0-0, false>#2**
Declaration placeholder for the specialization of `optional_copy_base` when `T` is not trivially copy-constructible.

**optional_copy_base<type-parameter-0-0, false>**
Constructor for the specialization of `optional_copy_base` when `T` is not trivially copy-constructible. Default constructor.

**optional_copy_base<type-parameter-0-0, false>#3**
Declaration placeholder for the specialization of `optional_copy_base` when `T` is not trivially copy-constructible.

**operator=#7**
Declaration placeholder for copy assignment operator in `optional_copy_base` specialization.

**operator=#6**
Declaration placeholder for move assignment operator in `optional_copy_base` specialization.

**optional_move_base<type-parameter-0-0, false>#2**
Declaration placeholder for the specialization of `optional_move_base` when `T` is not trivially move-constructible.

**optional_move_base<type-parameter-0-0, false>#3**
Declaration placeholder for the specialization of `optional_move_base` when `T` is not trivially move-constructible.

**optional_move_base<type-parameter-0-0, false>**
Constructor for the specialization of `optional_move_base` when `T` is not trivially move-constructible. Default constructor.

**operator=#22**
Declaration placeholder for copy assignment operator in `optional_move_base` specialization.

**operator=#21**
Declaration placeholder for move assignment operator in `optional_move_base` specialization.

**optional_copy_assign_base<type-parameter-0-0, false>**
Declaration placeholder for the specialization of `optional_copy_assign_base` when triviality conditions fail.

**optional_copy_assign_base<type-parameter-0-0, false>#3**
Declaration placeholder for the specialization of `optional_copy_assign_base`.

**optional_copy_assign_base<type-parameter-0-0, false>#2**
Declaration placeholder for the specialization of `optional_copy_assign_base`.

**operator=**
Copy assignment operator for `optional_copy_assign_base` specialization. Calls `assign(rhs)` to handle value transfer.

**operator=#5**
Declaration placeholder for move assignment operator in `optional_copy_assign_base` specialization.

**optional_move_assign_base<type-parameter-0-0, false>**
Declaration placeholder for the specialization of `optional_move_assign_base` when triviality conditions fail.

**optional_move_assign_base<type-parameter-0-0, false>#3**
Declaration placeholder for the specialization of `optional_move_assign_base`.

**optional_move_assign_base<type-parameter-0-0, false>#2**
Declaration placeholder for the specialization of `optional_move_assign_base`.

**operator=#20**
Declaration placeholder for copy assignment operator in `optional_move_assign_base` specialization.

**operator=#2**
Move assignment operator for `optional_move_assign_base` specialization. Calls `assign(std::move(rhs))` to handle value transfer.

**optional_delete_ctor_base<T, EnableCopy, EnableMove>**
Declaration placeholder for the base that conditionally deletes constructors.

**optional_delete_ctor_base<T, EnableCopy, EnableMove>#3**
Declaration placeholder for the base that conditionally deletes constructors.

**optional_delete_ctor_base<T, EnableCopy, EnableMove>#2**
Declaration placeholder for the base that conditionally deletes constructors.

**operator=#26**
Declaration placeholder for copy assignment in `optional_delete_ctor_base`.

**operator=#25**
Declaration placeholder for move assignment in `optional_delete_ctor_base`.

**optional_delete_ctor_base<type-parameter-0-0, true, false>**
Specialization where `T` is copy-constructible but not move-constructible. Deletes move constructor.

**optional_delete_ctor_base<type-parameter-0-0, true, false>#3**
Declaration placeholder for this specialization.

**optional_delete_ctor_base<type-parameter-0-0, true, false>#2**
Declaration placeholder for this specialization.

**operator=#19**
Declaration placeholder for copy assignment in this specialization.

**operator=#18**
Declaration placeholder for move assignment in this specialization.

**optional_delete_ctor_base<type-parameter-0-0, false, true>**
Specialization where `T` is move-constructible but not copy-constructible. Deletes copy constructor.

**optional_delete_ctor_base<type-parameter-0-0, false, true>#3**
Declaration placeholder for this specialization.

**optional_delete_ctor_base<type-parameter-0-0, false, true>#2**
Declaration placeholder for this specialization.

**operator=#17**
Declaration placeholder for copy assignment in this specialization.

**operator=#16**
Declaration placeholder for move assignment in this specialization.

**optional_delete_ctor_base<type-parameter-0-0, false, false>**
Specialization where `T` is neither copy nor move constructible. Deletes both constructors.

**optional_delete_ctor_base<type-parameter-0-0, false, false>#3**
Declaration placeholder for this specialization.

**optional_delete_ctor_base<type-parameter-0-0, false, false>#2**
Declaration placeholder for this specialization.

**operator=#15**
Declaration placeholder for copy assignment in this specialization.

**operator=#14**
Declaration placeholder for move assignment in this specialization.

**optional_delete_assign_base<T, EnableCopy, EnableMove>**
Declaration placeholder for the base that conditionally deletes assignment operators.

**optional_delete_assign_base<T, EnableCopy, EnableMove>#3**
Declaration placeholder for the base that conditionally deletes assignment operators.

**optional_delete_assign_base<T, EnableCopy, EnableMove>#2**
Declaration placeholder for the base that conditionally deletes assignment operators.

**operator=#24**
Declaration placeholder for copy assignment in `optional_delete_assign_base`.

**operator=#23**
Declaration placeholder for move assignment in `optional_delete_assign_base`.

**optional_delete_assign_base<type-parameter-0-0, true, false>**
Specialization where `T` is copy-assignable but not move-assignable. Deletes move assignment.

**optional_delete_assign_base<type-parameter-0-0, true, false>#3**
Declaration placeholder for this specialization.

**optional_delete_assign_base<type-parameter-0-0, true, false>#2**
Declaration placeholder for this specialization.

**operator=#13**
Declaration placeholder for copy assignment in this specialization.

**operator=#12**
Declaration placeholder for move assignment in this specialization.

**optional_delete_assign_base<type-parameter-0-0, false, true>**
Specialization where `T` is move-assignable but not copy-assignable. Deletes copy assignment.

**optional_delete_assign_base<type-parameter-0-0, false, true>#3**
Declaration placeholder for this specialization.

**optional_delete_assign_base<type-parameter-0-0, false, true>#2**
Declaration placeholder for this specialization.

**operator=#11**
Declaration placeholder for copy assignment in this specialization.

**operator=#10**
Declaration placeholder for move assignment in this specialization.

**optional_delete_assign_base<type-parameter-0-0, false, false>**
Specialization where `T` is neither copy nor move assignable. Deletes both assignments.

**optional_delete_assign_base<type-parameter-0-0, false, false>#3**
Declaration placeholder for this specialization.

**optional_delete_assign_base<type-parameter-0-0, false, false>#2**
Declaration placeholder for this specialization.

**operator=#9**
Declaration placeholder for copy assignment in this specialization.

**operator=#8**
Declaration placeholder for move assignment in this specialization.

**disjunction#13**
Overload of `disjunction` for `optional<T&>` taking `const optional&` and returning `optional`.

**disjunction#15**
Overload of `disjunction` for `optional<T&>` taking `const optional&` (const ref) and returning `optional`.

**disjunction#14**
Overload of `disjunction` for `optional<T&>` taking `const optional&` (rvalue) and returning `optional`.

**disjunction#16**
Overload of `disjunction` for `optional<T&>` taking `const optional&` (const rvalue) and returning `optional`.

**disjunction#9**
Overload of `disjunction` for `optional<T&>` taking `optional&&` (lvalue) and returning `optional`.

**disjunction#11**
Overload of `disjunction` for `optional<T&>` taking `optional&&` (const ref) and returning `optional`.

**disjunction#10**
Overload of `disjunction` for `optional<T&>` taking `optional&&` (rvalue) and returning `optional`.

**disjunction#12**
Overload of `disjunction` for `optional<T&>` taking `optional&&` (const rvalue) and returning `optional`.

**take#2**
Extracts the value from `optional<T&>`, leaving it empty. Returns the extracted optional.

**optional<T>#2**
Declaration placeholder for the main `optional` template class.

**optional<T>**
Default constructor for `optional<T>`. Constructs an empty optional.

**optional<T>#4**
Declaration placeholder for the main `optional` template class.

**optional<T>#3**
Declaration placeholder for the main `optional` template class.

**~optional<T>**
Destructor for `optional<T>`. Defaulted, relying on base class for cleanup.

**operator=#4**
Assignment operator for `optional<T>` taking `nullopt_t`. Resets the optional.

**operator=#29**
Declaration placeholder for copy assignment operator in `optional<T>`.

**operator=#28**
Declaration placeholder for move assignment operator in `optional<T>`.

**swap#2**
Swaps the contents of two `optional<T&>` objects by swapping their internal pointers.

**operator->#4**
Returns a pointer to the contained value for `optional<T&>` in const context.

**operator->#3**
Returns a pointer to the contained value for `optional<T&>` in non-const context.

**operator*#3**
Returns a reference to the contained value for `optional<T&>` in non-const context.

**operator*#5**
Returns a reference to the contained value for `optional<T&>` in const context.

**operator*#4**
Returns a reference to the contained value for `optional<T&>` in const lvalue context.

**operator*#6**
Returns a reference to the contained value for `optional<T&>` in const rvalue context.

**has_value#3**
Checks if `optional<T&>` contains a value by testing if the internal pointer is non-null.

**value#3**
Returns the referenced value for `optional<T&>` or throws `bad_optional_access`.

**value#5**
Returns the referenced value for `optional<T&>` in const context or throws `bad_optional_access`.

**value#4**
Returns the referenced value for `optional<T&>` in const lvalue context or throws `bad_optional_access`.

**value#6**
Returns the referenced value for `optional<T&>` in const rvalue context or throws `bad_optional_access`.

**reset#2**
Resets `optional<T&>` by setting the internal pointer to `nullptr`.

**disjunction#5**
Overload of `disjunction` for `optional<T>` taking `const optional&` and returning `optional`.

**disjunction#7**
Overload of `disjunction` for `optional<T>` taking `const optional&` (const ref) and returning `optional`.

**disjunction#6**
Overload of `disjunction` for `optional<T>` taking `const optional&` (rvalue) and returning `optional`.

**disjunction#8**
Overload of `disjunction` for `optional<T>` taking `const optional&` (const rvalue) and returning `optional`.

**disjunction**
Overload of `disjunction` for `optional<T>` taking `optional&&` (lvalue) and returning `optional`.

**disjunction#3**
Overload of `disjunction` for `optional<T>` taking `optional&&` (const ref) and returning `optional`.

**disjunction#2**
Overload of `disjunction` for `optional<T>` taking `optional&&` (rvalue) and returning `optional`.

**disjunction#4**
Overload of `disjunction` for `optional<T>` taking `optional&&` (const rvalue) and returning `optional`.

**take**
Extracts the value from `optional<T>`, leaving it empty. Returns the extracted optional.

**optional<type-parameter-0-0 &>**
Default constructor for `optional<T&>`. Initializes the internal pointer to `nullptr`.

**optional<type-parameter-0-0 &>#2**
Constructor for `optional<T&>` from `nullopt_t`. Initializes the internal pointer to `nullptr`.

**optional<type-parameter-0-0 &>#4**
Declaration placeholder for `optional<T&>`.

**optional<type-parameter-0-0 &>#3**
Declaration placeholder for `optional<T&>`.

**~optional<type-parameter-0-0 &>**
Destructor for `optional<T&>`. Defaulted, no cleanup needed.

**operator=#3**
Assignment operator for `optional<T&>` taking `nullopt_t`. Sets internal pointer to `nullptr`.

**operator=#27**
Declaration placeholder for copy assignment operator in `optional<T&>`.

**swap**
Swaps the contents of two `optional<T>` objects. Handles cases where one or both are empty.

**operator->#2**
Returns a pointer to the contained value for `optional<T>` in const context.

**operator->**
Returns a pointer to the contained value for `optional<T>` in non-const context.

**operator***
Returns a reference to the contained value for `optional<T>` in non-const lvalue context.

**operator*#2**
Returns a reference to the contained value for `optional<T>` in const lvalue context.

**has_value#2**
Checks if `optional<T>` contains a value by returning the `m_has_value` flag.

**value**
Returns the contained value for `optional<T>` or throws `bad_optional_access`.

**value#2**
Returns the contained value for `optional<T>` in const context or throws `bad_optional_access`.

**reset**
Destroys the contained value in `optional<T>` and sets `m_has_value` to false.

**operator()**
Explicit conversion operator to `bool` for `optional<T&>`, returning true if the internal pointer is non-null.

---

<!-- machine-true, projected from graph.json -->

## Map — optional

*Source:* optional.hpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| optional_storage_base<T, > | ctor | — | — | — |
| ~optional_storage_base<T, > | dtor | — | — | — |
| optional_storage_base<type-parameter-0-0, true> | ctor | — | — | — |
| hard_reset | function | — | — | — |
| has_value | function | — | — | — |
| get | function | — | — | — |
| get#3 | function | — | — | — |
| get#2 | function | — | — | — |
| get#4 | function | — | — | — |
| optional_copy_base<type-parameter-0-0, false>#2 | decl | — | — | — |
| optional_copy_base<type-parameter-0-0, false> | ctor | — | — | — |
| optional_copy_base<type-parameter-0-0, false>#3 | decl | — | — | — |
| operator=#7 | decl | — | — | — |
| operator=#6 | decl | — | — | — |
| optional_move_base<type-parameter-0-0, false>#2 | decl | — | — | — |
| optional_move_base<type-parameter-0-0, false>#3 | decl | — | — | — |
| optional_move_base<type-parameter-0-0, false> | ctor | — | — | — |
| operator=#22 | decl | — | — | — |
| operator=#21 | decl | — | — | — |
| optional_copy_assign_base<type-parameter-0-0, false> | decl | — | — | — |
| optional_copy_assign_base<type-parameter-0-0, false>#3 | decl | — | — | — |
| optional_copy_assign_base<type-parameter-0-0, false>#2 | decl | — | — | — |
| operator= | function | — | — | — |
| operator=#5 | decl | — | — | — |
| optional_move_assign_base<type-parameter-0-0, false> | decl | — | — | — |
| optional_move_assign_base<type-parameter-0-0, false>#3 | decl | — | — | — |
| optional_move_assign_base<type-parameter-0-0, false>#2 | decl | — | — | — |
| operator=#20 | decl | — | — | — |
| operator=#2 | function | — | — | — |
| optional_delete_ctor_base<T, EnableCopy, EnableMove> | decl | — | — | — |
| optional_delete_ctor_base<T, EnableCopy, EnableMove>#3 | decl | — | — | — |
| optional_delete_ctor_base<T, EnableCopy, EnableMove>#2 | decl | — | — | — |
| operator=#26 | decl | — | — | — |
| operator=#25 | decl | — | — | — |
| optional_delete_ctor_base<type-parameter-0-0, true, false> | decl | — | — | — |
| optional_delete_ctor_base<type-parameter-0-0, true, false>#3 | decl | — | — | — |
| optional_delete_ctor_base<type-parameter-0-0, true, false>#2 | decl | — | — | — |
| operator=#19 | decl | — | — | — |
| operator=#18 | decl | — | — | — |
| optional_delete_ctor_base<type-parameter-0-0, false, true> | decl | — | — | — |
| optional_delete_ctor_base<type-parameter-0-0, false, true>#3 | decl | — | — | — |
| optional_delete_ctor_base<type-parameter-0-0, false, true>#2 | decl | — | — | — |
| operator=#17 | decl | — | — | — |
| operator=#16 | decl | — | — | — |
| optional_delete_ctor_base<type-parameter-0-0, false, false> | decl | — | — | — |
| optional_delete_ctor_base<type-parameter-0-0, false, false>#3 | decl | — | — | — |
| optional_delete_ctor_base<type-parameter-0-0, false, false>#2 | decl | — | — | — |
| operator=#15 | decl | — | — | — |
| operator=#14 | decl | — | — | — |
| optional_delete_assign_base<T, EnableCopy, EnableMove> | decl | — | — | — |
| optional_delete_assign_base<T, EnableCopy, EnableMove>#3 | decl | — | — | — |
| optional_delete_assign_base<T, EnableCopy, EnableMove>#2 | decl | — | — | — |
| operator=#24 | decl | — | — | — |
| operator=#23 | decl | — | — | — |
| optional_delete_assign_base<type-parameter-0-0, true, false> | decl | — | — | — |
| optional_delete_assign_base<type-parameter-0-0, true, false>#3 | decl | — | — | — |
| optional_delete_assign_base<type-parameter-0-0, true, false>#2 | decl | — | — | — |
| operator=#13 | decl | — | — | — |
| operator=#12 | decl | — | — | — |
| optional_delete_assign_base<type-parameter-0-0, false, true> | decl | — | — | — |
| optional_delete_assign_base<type-parameter-0-0, false, true>#3 | decl | — | — | — |
| optional_delete_assign_base<type-parameter-0-0, false, true>#2 | decl | — | — | — |
| operator=#11 | decl | — | — | — |
| operator=#10 | decl | — | — | — |
| optional_delete_assign_base<type-parameter-0-0, false, false> | decl | — | — | — |
| optional_delete_assign_base<type-parameter-0-0, false, false>#3 | decl | — | — | — |
| optional_delete_assign_base<type-parameter-0-0, false, false>#2 | decl | — | — | — |
| operator=#9 | decl | — | — | — |
| operator=#8 | decl | — | — | — |
| disjunction#13 | function | — | — | — |
| disjunction#15 | function | — | — | — |
| disjunction#14 | function | — | — | — |
| disjunction#16 | function | — | — | — |
| disjunction#9 | function | — | — | — |
| disjunction#11 | function | — | — | — |
| disjunction#10 | function | — | — | — |
| disjunction#12 | function | — | — | — |
| take#2 | function | — | — | — |
| optional<T>#2 | decl | — | — | — |
| optional<T> | ctor | — | — | — |
| optional<T>#4 | decl | — | — | — |
| optional<T>#3 | decl | — | — | — |
| ~optional<T> | decl | — | — | — |
| operator=#4 | function | — | — | — |
| operator=#29 | decl | — | — | — |
| operator=#28 | decl | — | — | — |
| swap#2 | function | — | — | — |
| operator->#4 | function | — | — | — |
| operator->#3 | function | — | — | — |
| operator*#3 | function | — | — | — |
| operator*#5 | function | — | — | — |
| operator*#4 | function | — | — | — |
| operator*#6 | function | — | — | — |
| has_value#3 | function | — | — | — |
| value#3 | function | — | — | — |
| value#5 | function | — | — | — |
| value#4 | function | — | — | — |
| value#6 | function | — | — | — |
| reset#2 | function | — | — | — |
| disjunction#5 | function | — | — | — |
| disjunction#7 | function | — | — | — |
| disjunction#6 | function | — | — | — |
| disjunction#8 | function | — | — | — |
| disjunction | function | — | — | — |
| disjunction#3 | function | — | — | — |
| disjunction#2 | function | — | — | — |
| disjunction#4 | function | — | — | — |
| take | function | — | — | — |
| optional<type-parameter-0-0 &> | ctor | — | — | — |
| optional<type-parameter-0-0 &>#2 | ctor | — | — | — |
| optional<type-parameter-0-0 &>#4 | decl | — | — | — |
| optional<type-parameter-0-0 &>#3 | decl | — | — | — |
| ~optional<type-parameter-0-0 &> | decl | — | — | — |
| operator=#3 | function | — | — | — |
| operator=#27 | decl | — | — | — |
| swap | function | — | — | — |
| operator->#2 | function | — | — | — |
| operator-> | function | — | — | — |
| operator* | function | — | — | — |
| operator*#2 | function | — | — | — |
| has_value#2 | function | — | — | — |
| value | function | — | — | — |
| value#2 | function | — | — | — |
| reset | function | — | — | — |
| operator() | function | — | — | — |

---

<!-- verify: failed-members | invented: operator -->
