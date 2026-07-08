# expected

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# `expected.hpp` — `nonstd::expected` Implementation

## Purpose & Responsibilities

This unit implements `nonstd::expected<T, E>`, a header-only C++11-compatible library providing functionality equivalent to the C++23 `std::expected` proposal (originally P0323). It serves as a type-safe alternative to returning raw pointers, optional values, or using exceptions for error handling.

An `expected<T, E>` object holds either a value of type `T` (the "success" state) or an error of type `E` (the "failure" state). Unlike `std::optional`, which only indicates presence or absence, `expected` explicitly carries error information when a failure occurs. The implementation supports:
- **Value/Error Storage**: Using a discriminated union (`union`) to store either `T` or `unexpected<E>` without dynamic allocation.
- **Triviality Optimization**: Specialized base classes ensure that if `T` and `E` are trivially copyable/movable/destructible, the resulting `expected` type retains those properties for performance.
- **Functional Combinators**: Methods like `map`, `and_then`, `transform`, and `or_else` allow chaining operations on success values or error states without explicit branching.
- **Compiler Compatibility**: Extensive preprocessor macros and trait specializations handle deficiencies in older compilers (GCC 4.9–5.5, MSVC 2015) regarding `constexpr`, `noexcept`, and type traits.

The unit defines no database interactions; it is a pure utility library for control flow and data representation.

## Member-by-Member Behavior

The implementation is structured into several layers of inheritance to manage complexity and optimize for trivial types.

### 1. Core Types: `unexpected` and `bad_expected_access`

**`unexpected<E>`**
- **Purpose**: Wraps the error type `E`. It is used to construct an `expected` in the error state and to retrieve the error value.
- **Behavior**:
  - Constructors accept `E` by value, reference, or in-place arguments.
  - `value()` returns the stored error `E` with correct reference qualifiers (`&`, `&&`, `const &`, etc.).
  - Comparison operators (`==`, `!=`, `<`, etc.) delegate to the underlying `E`.
  - `make_unexpected()` is a free function helper to deduce the error type.

**`bad_expected_access<E>`**
- **Purpose**: Exception type thrown when accessing the value of an `expected` that is in the error state via `value()`.
- **Behavior**: Inherits from `std::exception`. Stores the error `E`. `what()` returns "Bad expected access". `error()` retrieves the stored error.

### 2. Storage Management: `expected_storage_base`

This family of structs manages the raw memory layout using a `union`. It specializes based on whether `T` and `E` are trivially destructible.

- **General Case (`expected_storage_base<T, E, false, false>`)**: Both `T` and `E` require manual destructor calls. The destructor checks `m_has_val` to call the correct destructor (`m_val.~T()` or `m_unexpect.~unexpected<E>()`).
- **Trivial Cases**: If `T` or `E` are trivially destructible, the corresponding destructor call is omitted or defaulted.
- **Void Case (`expected_storage_base<void, E, ...>`)**: When `T` is `void`, the union stores a dummy type instead of `T`. Construction/destruction of the value is a no-op.

Key members:
- **Constructors**: Initialize the union member and set `m_has_val`. Support in-place construction for both value and error.
- **Destructor**: Conditionally destroys the active union member.
- **`m_has_val`**: Boolean flag tracking whether the value or error is active.

### 3. Operations Base: `expected_operations_base`

Provides helper methods for constructing, destroying, and assigning the contained objects.

- **`construct(...)` / `construct_error(...)`**: Placement-new the value or error into the union and update `m_has_val`.
- **`assign(...)`**: Implements strong exception safety for assignment.
  - If copying/moving `T` can throw, it uses a temporary buffer to hold the old error state. If the new value construction fails, it restores the old error.
  - Specialized overloads exist for nothrow-copy/move scenarios to avoid unnecessary temporaries.
- **`get()` / `geterr()`**: Return references to the active union member.
- **`destroy_val()`**: Explicitly destroys the value `T`.

### 4. Triviality & Move/Copy Bases

These bases conditionally define or delete copy/move constructors and assignment operators based on type traits.

- **`expected_copy_base`**: Defines copy constructors if `T` and `E` are copyable. Uses `no_init` tag to bypass default initialization in derived classes.
- **`expected_move_base`**: Defines move constructors if `T` and `E` are movable.
- **`expected_copy_assign_base` / `expected_move_assign_base`**: Define assignment operators. They rely on `expected_operations_base::assign` for the heavy lifting.
- **`expected_delete_ctor_base` / `expected_delete_assign_base`**: Delete constructors/operators if `T` or `E` lack the necessary capabilities (e.g., deleting copy constructor if `T` is not copyable).
- **`expected_default_ctor_base`**: Deletes the default constructor if `T` is not default-constructible (unless `T` is `void`).

### 5. Main Class: `expected<T, E>`

The primary interface. It inherits from the various bases above.

#### Construction
- **Default Constructor**: Defaulted if `T` is default-constructible. Initializes to a valid `T{}`.
- **In-Place Construction**: `expected(in_place_t, Args...)` constructs `T` in place.
- **Error Construction**: `expected(unexpect_t, Args...)` or `expected(unexpected<E>)` initializes the error state.
- **Forwarding Constructor**: `expected(U&& v)` forwards `v` to construct `T` in place, unless `U` is already an `expected` or `unexpected`.

#### Accessors
- **`has_value()`**: Returns `true` if holding a value.
- **`operator*()` / `operator->()`**: Dereference the value. Asserts `has_value()` is true.
- **`value()`**: Returns the value. Throws `bad_expected_access<E>` if in error state.
- **`error()`**: Returns the error. Asserts `has_value()` is false.
- **`value_or(U&& v)`**: Returns the value if present, otherwise returns `v` converted to `T`.

#### Modification
- **`emplace(...)`**: Destroys the current content and constructs a new `T` in place. Handles exception safety by saving/restoring the error state if construction throws.
- **`operator=(U&& v)`**: Assigns a new value. If currently holding an error, it destroys the error and constructs the value. Strong exception safety guaranteed.
- **`operator=(unexpected<G>&&)`**: Assigns a new error. Destroys the current value if present.

#### Functional Combinators (Free Functions in `detail`)
These are exposed as member methods via delegation.

- **`map(F&& f)` / `transform(F&& f)`**: If `has_value()`, applies `f` to the value and wraps the result in a new `expected`. If error, propagates the error.
- **`and_then(F&& f)`**: If `has_value()`, applies `f` (which must return an `expected`) and returns that `expected`. If error, propagates the error. Allows chaining fallible operations.
- **`map_error(F&& f)` / `transform_error(F&& f)`**: If error, applies `f` to the error and wraps the result in a new `expected` with the transformed error. If value, propagates the value.
- **`or_else(F&& f)`**: If error, applies `f` (which must return an `expected`) and returns that `expected`. If value, propagates the value. Allows recovery from errors.

#### Swap
- **`swap(expected& rhs)`**: Exchanges contents with another `expected`.
  - **Both have value**: Swaps values.
  - **Both have error**: Swaps errors.
  - **Mixed**: Moves the value/error into the other's slot, carefully managing destructors and constructors to maintain strong exception safety. Specialized implementations exist for nothrow-move scenarios.

### 6. Free Functions
- **Comparison Operators**: `==`, `!=`, `<`, etc., compare two `expected`s or an `expected` with a value/error. Two `expected`s are equal if both have values and the values are equal, or both have errors and the errors are equal.
- **`swap(expected&, expected&)`**: ADL-enabled swap function.

## Cross-Unit Boundaries

This unit is self-contained within the `nonstd` namespace. It does not call into other units in the `wowvmangos` codebase. It relies solely on standard library headers (`<exception>`, `<functional>`, `<type_traits>`, `<utility>`, `<cassert>`).

## Data Model

This unit interacts with no database tables. It is a pure C++ utility library.

## Notable Implementation Details

1. **Exception Safety in Assignment/Emplace**:
   - In `expected_operations_base::assign` and `expected::emplace`, if the current state is an error and the new value construction might throw, the implementation saves the current error into a temporary variable (`tmp`). It then attempts to construct the new value. If construction throws, it restores the error from `tmp` before rethrowing. This ensures the `expected` remains in a valid state (either the original error or the new value) even if an exception occurs.

2. **Triviality Propagation**:
   - The hierarchy of base classes (`expected_copy_base`, `expected_move_base`, etc.) is designed to allow the compiler to generate trivial copy/move/assignment operators when possible. This is critical for performance in containers like `std::vector<expected<T, E>>`.

3. **GCC/MSVC Workarounds**:
   - Macros like `TL_EXPECTED_GCC49`, `TL_EXPECTED_MSVC2015_CONSTEXPR`, and `TL_EXPECTED_NO_CONSTRR` disable features unsupported by older compilers (e.g., `constexpr` in certain contexts, `const&&` overloads).
   - Custom trait `is_trivially_copy_constructible` is provided for GCC < 8 to work around bugs in `std::vector` with non-copyable types.

4. **Void Support**:
   - When `T` is `void`, the `expected` represents a fallible operation with no return value. The storage uses a dummy type, and accessors like `operator*()` are disabled. `map` and `and_then` adapt to this by ignoring the value parameter.

5. **Strong Exception Guarantee for Swap**:
   - `swap_where_only_one_has_value_and_t_is_not_void` uses temporary moves and placement new to exchange contents. If an exception occurs during the move/construction, it restores the original state using the saved temporary.

## Member Reference

**unexpected<E>#3**  
Declaration of the `unexpected` class template.

**unexpected<E>#2**  
Constructor taking `E&&`. Moves the error value into storage.

**unexpected<E>**  
Constructor taking `const E&`. Copies the error value into storage.

**value#3**  
Returns `const E&&` reference to the stored error.

**value**  
Returns `const E&` reference to the stored error.

**value#2**  
Returns `E&` reference to the stored error.

**value#4**  
Returns `E&&` reference to the stored error.

**expected_storage_base<T, E, , >**  
Base storage class for non-trivially destructible `T` and `E`. Manages union and `m_has_val`.

**expected_storage_base<T, E, , >#2**  
Constructor initializing with `no_init_t`. Sets `m_has_val` to false.

**~expected_storage_base<T, E, , >**  
Destructor. Calls destructor of active union member based on `m_has_val`.

**expected_storage_base<type-parameter-0-0, type-parameter-0-1, true, true>**  
Specialization for trivially destructible `T` and `E`. Destructor is defaulted.

**expected_storage_base<type-parameter-0-0, type-parameter-0-1, true, true>#2**  
Constructor initializing with `no_init_t`.

**~expected_storage_base<type-parameter-0-0, type-parameter-0-1, true, true>**  
Defaulted destructor.

**expected_storage_base<type-parameter-0-0, type-parameter-0-1, true, false>**  
Specialization for trivially destructible `T`, non-trivial `E`. Destructor only destroys `E` if active.

**expected_storage_base<type-parameter-0-0, type-parameter-0-1, true, false>#2**  
Constructor initializing with `no_init_t`.

**~expected_storage_base<type-parameter-0-0, type-parameter-0-1, true, false>**  
Destructor. Destroys `E` if `!m_has_val`.

**expected_storage_base<type-parameter-0-0, type-parameter-0-1, false, true>**  
Specialization for non-trivial `T`, trivially destructible `E`. Destructor only destroys `T` if active.

**expected_storage_base<type-parameter-0-0, type-parameter-0-1, false, true>#2**  
Constructor initializing with `no_init_t`.

**~expected_storage_base<type-parameter-0-0, type-parameter-0-1, false, true>**  
Destructor. Destroys `T` if `m_has_val`.

**expected_storage_base<void, type-parameter-0-0, false, true>**  
Specialization for `T=void`, trivially destructible `E`. Uses dummy type for value slot.

**expected_storage_base<void, type-parameter-0-0, false, true>#2**  
Constructor initializing with `no_init_t`.

**expected_storage_base<void, type-parameter-0-0, false, true>#3**  
Constructor initializing with `in_place_t`.

**~expected_storage_base<void, type-parameter-0-0, false, true>**  
Defaulted destructor.

**expected_storage_base<void, type-parameter-0-0, false, false>**  
Specialization for `T=void`, non-trivially destructible `E`.

**expected_storage_base<void, type-parameter-0-0, false, false>#2**  
Constructor initializing with `no_init_t`.

**expected_storage_base<void, type-parameter-0-0, false, false>#3**  
Constructor initializing with `in_place_t`.

**~expected_storage_base<void, type-parameter-0-0, false, false>**  
Destructor. Destroys `E` if `!m_has_val`.

**has_value#2**  
Returns `m_has_val` from `expected_operations_base`.

**get**  
Returns `T&` reference to value.

**get#3**  
Returns `const T&&` reference to value.

**get#2**  
Returns `const T&` reference to value.

**get#4**  
Returns `T&&` reference to value.

**geterr#5**  
Returns `unexpected<E>&` reference to error.

**geterr#7**  
Returns `const unexpected<E>&` reference to error.

**geterr#6**  
Returns `unexpected<E>&` reference to error (rvalue context).

**geterr#8**  
Returns `const unexpected<E>&` reference to error (rvalue context).

**destroy_val#2**  
Destroys the value `T` in `expected_operations_base`.

**has_value**  
Returns `m_has_val` from `expected_operations_base` (void specialization).

**geterr**  
Returns `unexpected<E>&` reference to error (void specialization).

**geterr#3**  
Returns `const unexpected<E>&` reference to error (void specialization).

**geterr#2**  
Returns `unexpected<E>&` reference to error (void specialization, rvalue).

**geterr#4**  
Returns `const unexpected<E>&` reference to error (void specialization, rvalue).

**destroy_val**  
No-op destroy for void specialization.

**expected_copy_base<type-parameter-0-0, type-parameter-0-1, false>#2**  
Declaration of copy base for non-trivially copyable types.

**expected_copy_base<type-parameter-0-0, type-parameter-0-1, false>**  
Constructor initializing with `no_init`.

**expected_copy_base<type-parameter-0-0, type-parameter-0-1, false>#3**  
Declaration of copy base for non-trivially copyable types.

**operator=#5**  
Deleted copy assignment operator for non-copyable types.

**operator=#4**  
Deleted move assignment operator for non-movable types.

**expected_move_base<type-parameter-0-0, type-parameter-0-1, false>#2**  
Declaration of move base for non-trivially movable types.

**expected_move_base<type-parameter-0-0, type-parameter-0-1, false>#3**  
Declaration of move base for non-trivially movable types.

**expected_move_base<type-parameter-0-0, type-parameter-0-1, false>**  
Constructor initializing with `no_init`.

**operator=#22**  
Deleted copy assignment operator for non-copyable types.

**operator=#21**  
Deleted move assignment operator for non-movable types.

**expected_copy_assign_base<type-parameter-0-0, type-parameter-0-1, false>**  
Declaration of copy assign base for non-trivially copy assignable types.

**expected_copy_assign_base<type-parameter-0-0, type-parameter-0-1, false>#3**  
Declaration of copy assign base.

**expected_copy_assign_base<type-parameter-0-0, type-parameter-0-1, false>#2**  
Declaration of copy assign base.

**operator=**  
Copy assignment operator. Delegates to `assign`.

**operator=#3**  
Deleted copy assignment operator for non-copyable types.

**expected_move_assign_base<type-parameter-0-0, type-parameter-0-1, false>**  
Declaration of move assign base for non-trivially move assignable types.

**expected_move_assign_base<type-parameter-0-0, type-parameter-0-1, false>#3**  
Declaration of move assign base.

**expected_move_assign_base<type-parameter-0-0, type-parameter-0-1, false>#2**  
Declaration of move assign base.

**operator=#20**  
Deleted copy assignment operator for non-copyable types.

**operator=#2**  
Move assignment operator. Delegates to `assign`.

**expected_delete_ctor_base<T, E, EnableCopy, EnableMove>**  
Base class to conditionally delete constructors.

**expected_delete_ctor_base<T, E, EnableCopy, EnableMove>#3**  
Declaration of delete ctor base.

**expected_delete_ctor_base<T, E, EnableCopy, EnableMove>#2**  
Declaration of delete ctor base.

**operator=#28**  
Deleted copy assignment operator.

**operator=#27**  
Deleted move assignment operator.

**expected_delete_ctor_base<type-parameter-0-0, type-parameter-0-1, true, false>**  
Specialization: Copy enabled, Move disabled.

**expected_delete_ctor_base<type-parameter-0-0, type-parameter-0-1, true, false>#3**  
Declaration.

**expected_delete_ctor_base<type-parameter-0-0, type-parameter-0-1, true, false>#2**  
Declaration.

**operator=#19**  
Deleted copy assignment operator.

**operator=#18**  
Deleted move assignment operator.

**expected_delete_ctor_base<type-parameter-0-0, type-parameter-0-1, false, true>**  
Specialization: Copy disabled, Move enabled.

**expected_delete_ctor_base<type-parameter-0-0, type-parameter-0-1, false, true>#3**  
Declaration.

**expected_delete_ctor_base<type-parameter-0-0, type-parameter-0-1, false, true>#2**  
Declaration.

**operator=#17**  
Deleted copy assignment operator.

**operator=#16**  
Deleted move assignment operator.

**expected_delete_ctor_base<type-parameter-0-0, type-parameter-0-1, false, false>**  
Specialization: Copy and Move disabled.

**expected_delete_ctor_base<type-parameter-0-0, type-parameter-0-1, false, false>#3**  
Declaration.

**expected_delete_ctor_base<type-parameter-0-0, type-parameter-0-1, false, false>#2**  
Declaration.

**operator=#15**  
Deleted copy assignment operator.

**operator=#14**  
Deleted move assignment operator.

**expected_delete_assign_base<T, E, EnableCopy, EnableMove>**  
Base class to conditionally delete assignment operators.

**expected_delete_assign_base<T, E, EnableCopy, EnableMove>#3**  
Declaration.

**expected_delete_assign_base<T, E, EnableCopy, EnableMove>#2**  
Declaration.

**operator=#26**  
Deleted copy assignment operator.

**operator=#25**  
Deleted move assignment operator.

**expected_delete_assign_base<type-parameter-0-0, type-parameter-0-1, true, false>**  
Specialization: Copy enabled, Move disabled.

**expected_delete_assign_base<type-parameter-0-0, type-parameter-0-1, true, false>#3**  
Declaration.

**expected_delete_assign_base<type-parameter-0-0, type-parameter-0-1, true, false>#2**  
Declaration.

**operator=#13**  
Deleted copy assignment operator.

**operator=#12**  
Deleted move assignment operator.

**expected_delete_assign_base<type-parameter-0-0, type-parameter-0-1, false, true>**  
Specialization: Copy disabled, Move enabled.

**expected_delete_assign_base<type-parameter-0-0, type-parameter-0-1, false, true>#3**  
Declaration.

**expected_delete_assign_base<type-parameter-0-0, type-parameter-0-1, false, true>#2**  
Declaration.

**operator=#11**  
Deleted copy assignment operator.

**operator=#10**  
Deleted move assignment operator.

**expected_delete_assign_base<type-parameter-0-0, type-parameter-0-1, false, false>**  
Specialization: Copy and Move disabled.

**expected_delete_assign_base<type-parameter-0-0, type-parameter-0-1, false, false>#3**  
Declaration.

**expected_delete_assign_base<type-parameter-0-0, type-parameter-0-1, false, false>#2**  
Declaration.

**operator=#9**  
Deleted copy assignment operator.

**operator=#8**  
Deleted move assignment operator.

**expected_default_ctor_base<T, E, Enable>#2**  
Declaration of default ctor base.

**expected_default_ctor_base<T, E, Enable>#4**  
Declaration of default ctor base.

**expected_default_ctor_base<T, E, Enable>#3**  
Declaration of default ctor base.

**operator=#24**  
Deleted copy assignment operator.

**operator=#23**  
Deleted move assignment operator.

**expected_default_ctor_base<T, E, Enable>**  
Constructor for default-constructible `T`.

**expected_default_ctor_base<type-parameter-0-0, type-parameter-0-1, false>#2**  
Declaration of default ctor base for non-default-constructible `T`.

**expected_default_ctor_base<type-parameter-0-0, type-parameter-0-1, false>#4**  
Declaration of default ctor base.

**expected_default_ctor_base<type-parameter-0-0, type-parameter-0-1, false>#3**  
Declaration of default ctor base.

**operator=#7**  
Deleted copy assignment operator.

**operator=#6**  
Deleted move assignment operator.

**expected_default_ctor_base<type-parameter-0-0, type-parameter-0-1, false>**  
Deleted default constructor for non-default-constructible `T`.

**bad_expected_access<E>**  
Constructor storing error `E`.

**what**  
Returns "Bad expected access".

**error#3**  
Returns `const E&&` reference to stored error.

**error**  
Returns `const E&` reference to stored error.

**error#4**  
Returns `E&&` reference to stored error.

**error#2**  
Returns `E&` reference to stored error.

**valptr**  
Returns pointer to value storage.

**valptr#2**  
Returns const pointer to value storage.

**errptr**  
Returns pointer to error storage.

**errptr#2**  
Returns const pointer to error storage.

**err**  
Returns reference to error storage.

**err#2**  
Returns const reference to error storage.

**expected<T, E>**  
Declaration of main `expected` class.

**expected<T, E>#3**  
Declaration of main `expected` class.

**expected<T, E>#2**  
Declaration of main `expected` class.

**operator=#30**  
Deleted copy assignment operator.

**operator=#29**  
Deleted move assignment operator.

**swap_where_both_have_value#2**  
Swaps values when both `expected`s have values (void case).

**swap_where_both_have_value**  
Swaps values when both `expected`s have values (non-void case).

**swap_where_only_one_has_value#2**  
Swaps contents when only one has value (void case).

**swap_where_only_one_has_value**  
Swaps contents when only one has value (non-void case).

**swap_where_only_one_has_value_and_t_is_not_void#3**  
Swaps contents with exception safety (T throws, E nothrow).

**swap_where_only_one_has_value_and_t_is_not_void#2**  
Swaps contents with exception safety (T nothrow, E throws).

**swap_where_only_one_has_value_and_t_is_not_void**  
Swaps contents with exception safety (both nothrow).

**operator->#2**  
Dereferences value (const).

**operator->**  
Dereferences value (non-const).

**has_value#3**  
Returns `m_has_val` from `expected` class.

**error#7**  
Returns `const E&&` reference to error.

**error#5**  
Returns `const E&` reference to error.

**error#8**  
Returns `E&&` reference to error.

**error#6**  
Returns `E&` reference to error.

---

<!-- machine-true, projected from graph.json -->

## Map — expected

*Source:* expected.hpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| unexpected<E>#3 | decl | — | — | — |
| unexpected<E>#2 | ctor | — | — | — |
| unexpected<E> | ctor | — | — | — |
| value#3 | function | — | — | — |
| value | function | — | — | — |
| value#2 | function | — | — | — |
| value#4 | function | — | — | — |
| expected_storage_base<T, E, , > | ctor | — | — | — |
| expected_storage_base<T, E, , >#2 | ctor | — | — | — |
| ~expected_storage_base<T, E, , > | dtor | — | — | — |
| expected_storage_base<type-parameter-0-0, type-parameter-0-1, true, true> | ctor | — | — | — |
| expected_storage_base<type-parameter-0-0, type-parameter-0-1, true, true>#2 | ctor | — | — | — |
| ~expected_storage_base<type-parameter-0-0, type-parameter-0-1, true, true> | decl | — | — | — |
| expected_storage_base<type-parameter-0-0, type-parameter-0-1, true, false> | ctor | — | — | — |
| expected_storage_base<type-parameter-0-0, type-parameter-0-1, true, false>#2 | ctor | — | — | — |
| ~expected_storage_base<type-parameter-0-0, type-parameter-0-1, true, false> | dtor | — | — | — |
| expected_storage_base<type-parameter-0-0, type-parameter-0-1, false, true> | ctor | — | — | — |
| expected_storage_base<type-parameter-0-0, type-parameter-0-1, false, true>#2 | ctor | — | — | — |
| ~expected_storage_base<type-parameter-0-0, type-parameter-0-1, false, true> | dtor | — | — | — |
| expected_storage_base<void, type-parameter-0-0, false, true> | ctor | — | — | — |
| expected_storage_base<void, type-parameter-0-0, false, true>#2 | ctor | — | — | — |
| expected_storage_base<void, type-parameter-0-0, false, true>#3 | ctor | — | — | — |
| ~expected_storage_base<void, type-parameter-0-0, false, true> | decl | — | — | — |
| expected_storage_base<void, type-parameter-0-0, false, false> | ctor | — | — | — |
| expected_storage_base<void, type-parameter-0-0, false, false>#2 | ctor | — | — | — |
| expected_storage_base<void, type-parameter-0-0, false, false>#3 | ctor | — | — | — |
| ~expected_storage_base<void, type-parameter-0-0, false, false> | dtor | — | — | — |
| has_value#2 | function | — | — | — |
| get | function | — | — | — |
| get#3 | function | — | — | — |
| get#2 | function | — | — | — |
| get#4 | function | — | — | — |
| geterr#5 | function | — | — | — |
| geterr#7 | function | — | — | — |
| geterr#6 | function | — | — | — |
| geterr#8 | function | — | — | — |
| destroy_val#2 | function | — | — | — |
| has_value | function | — | — | — |
| geterr | function | — | — | — |
| geterr#3 | function | — | — | — |
| geterr#2 | function | — | — | — |
| geterr#4 | function | — | — | — |
| destroy_val | function | — | — | — |
| expected_copy_base<type-parameter-0-0, type-parameter-0-1, false>#2 | decl | — | — | — |
| expected_copy_base<type-parameter-0-0, type-parameter-0-1, false> | ctor | — | — | — |
| expected_copy_base<type-parameter-0-0, type-parameter-0-1, false>#3 | decl | — | — | — |
| operator=#5 | decl | — | — | — |
| operator=#4 | decl | — | — | — |
| expected_move_base<type-parameter-0-0, type-parameter-0-1, false>#2 | decl | — | — | — |
| expected_move_base<type-parameter-0-0, type-parameter-0-1, false>#3 | decl | — | — | — |
| expected_move_base<type-parameter-0-0, type-parameter-0-1, false> | ctor | — | — | — |
| operator=#22 | decl | — | — | — |
| operator=#21 | decl | — | — | — |
| expected_copy_assign_base<type-parameter-0-0, type-parameter-0-1, false> | decl | — | — | — |
| expected_copy_assign_base<type-parameter-0-0, type-parameter-0-1, false>#3 | decl | — | — | — |
| expected_copy_assign_base<type-parameter-0-0, type-parameter-0-1, false>#2 | decl | — | — | — |
| operator= | function | — | — | — |
| operator=#3 | decl | — | — | — |
| expected_move_assign_base<type-parameter-0-0, type-parameter-0-1, false> | decl | — | — | — |
| expected_move_assign_base<type-parameter-0-0, type-parameter-0-1, false>#3 | decl | — | — | — |
| expected_move_assign_base<type-parameter-0-0, type-parameter-0-1, false>#2 | decl | — | — | — |
| operator=#20 | decl | — | — | — |
| operator=#2 | function | — | — | — |
| expected_delete_ctor_base<T, E, EnableCopy, EnableMove> | decl | — | — | — |
| expected_delete_ctor_base<T, E, EnableCopy, EnableMove>#3 | decl | — | — | — |
| expected_delete_ctor_base<T, E, EnableCopy, EnableMove>#2 | decl | — | — | — |
| operator=#28 | decl | — | — | — |
| operator=#27 | decl | — | — | — |
| expected_delete_ctor_base<type-parameter-0-0, type-parameter-0-1, true, false> | decl | — | — | — |
| expected_delete_ctor_base<type-parameter-0-0, type-parameter-0-1, true, false>#3 | decl | — | — | — |
| expected_delete_ctor_base<type-parameter-0-0, type-parameter-0-1, true, false>#2 | decl | — | — | — |
| operator=#19 | decl | — | — | — |
| operator=#18 | decl | — | — | — |
| expected_delete_ctor_base<type-parameter-0-0, type-parameter-0-1, false, true> | decl | — | — | — |
| expected_delete_ctor_base<type-parameter-0-0, type-parameter-0-1, false, true>#3 | decl | — | — | — |
| expected_delete_ctor_base<type-parameter-0-0, type-parameter-0-1, false, true>#2 | decl | — | — | — |
| operator=#17 | decl | — | — | — |
| operator=#16 | decl | — | — | — |
| expected_delete_ctor_base<type-parameter-0-0, type-parameter-0-1, false, false> | decl | — | — | — |
| expected_delete_ctor_base<type-parameter-0-0, type-parameter-0-1, false, false>#3 | decl | — | — | — |
| expected_delete_ctor_base<type-parameter-0-0, type-parameter-0-1, false, false>#2 | decl | — | — | — |
| operator=#15 | decl | — | — | — |
| operator=#14 | decl | — | — | — |
| expected_delete_assign_base<T, E, EnableCopy, EnableMove> | decl | — | — | — |
| expected_delete_assign_base<T, E, EnableCopy, EnableMove>#3 | decl | — | — | — |
| expected_delete_assign_base<T, E, EnableCopy, EnableMove>#2 | decl | — | — | — |
| operator=#26 | decl | — | — | — |
| operator=#25 | decl | — | — | — |
| expected_delete_assign_base<type-parameter-0-0, type-parameter-0-1, true, false> | decl | — | — | — |
| expected_delete_assign_base<type-parameter-0-0, type-parameter-0-1, true, false>#3 | decl | — | — | — |
| expected_delete_assign_base<type-parameter-0-0, type-parameter-0-1, true, false>#2 | decl | — | — | — |
| operator=#13 | decl | — | — | — |
| operator=#12 | decl | — | — | — |
| expected_delete_assign_base<type-parameter-0-0, type-parameter-0-1, false, true> | decl | — | — | — |
| expected_delete_assign_base<type-parameter-0-0, type-parameter-0-1, false, true>#3 | decl | — | — | — |
| expected_delete_assign_base<type-parameter-0-0, type-parameter-0-1, false, true>#2 | decl | — | — | — |
| operator=#11 | decl | — | — | — |
| operator=#10 | decl | — | — | — |
| expected_delete_assign_base<type-parameter-0-0, type-parameter-0-1, false, false> | decl | — | — | — |
| expected_delete_assign_base<type-parameter-0-0, type-parameter-0-1, false, false>#3 | decl | — | — | — |
| expected_delete_assign_base<type-parameter-0-0, type-parameter-0-1, false, false>#2 | decl | — | — | — |
| operator=#9 | decl | — | — | — |
| operator=#8 | decl | — | — | — |
| expected_default_ctor_base<T, E, Enable>#2 | decl | — | — | — |
| expected_default_ctor_base<T, E, Enable>#4 | decl | — | — | — |
| expected_default_ctor_base<T, E, Enable>#3 | decl | — | — | — |
| operator=#24 | decl | — | — | — |
| operator=#23 | decl | — | — | — |
| expected_default_ctor_base<T, E, Enable> | ctor | — | — | — |
| expected_default_ctor_base<type-parameter-0-0, type-parameter-0-1, false>#2 | decl | — | — | — |
| expected_default_ctor_base<type-parameter-0-0, type-parameter-0-1, false>#4 | decl | — | — | — |
| expected_default_ctor_base<type-parameter-0-0, type-parameter-0-1, false>#3 | decl | — | — | — |
| operator=#7 | decl | — | — | — |
| operator=#6 | decl | — | — | — |
| expected_default_ctor_base<type-parameter-0-0, type-parameter-0-1, false> | ctor | — | — | — |
| bad_expected_access<E> | ctor | — | — | — |
| what | function | — | — | — |
| error#3 | function | — | — | — |
| error | function | — | — | — |
| error#4 | function | — | — | — |
| error#2 | function | — | — | — |
| valptr | function | — | — | — |
| valptr#2 | function | — | — | — |
| errptr | function | — | — | — |
| errptr#2 | function | — | — | — |
| err | function | — | — | — |
| err#2 | function | — | — | — |
| expected<T, E> | decl | — | — | — |
| expected<T, E>#3 | decl | — | — | — |
| expected<T, E>#2 | decl | — | — | — |
| operator=#30 | decl | — | — | — |
| operator=#29 | decl | — | — | — |
| swap_where_both_have_value#2 | function | — | — | — |
| swap_where_both_have_value | function | — | — | — |
| swap_where_only_one_has_value#2 | function | — | — | — |
| swap_where_only_one_has_value | function | — | — | — |
| swap_where_only_one_has_value_and_t_is_not_void#3 | function | — | — | — |
| swap_where_only_one_has_value_and_t_is_not_void#2 | function | — | — | — |
| swap_where_only_one_has_value_and_t_is_not_void | function | — | — | — |
| operator->#2 | function | — | — | — |
| operator-> | function | — | — | — |
| has_value#3 | function | — | — | — |
| error#7 | function | — | — | — |
| error#5 | function | — | — | — |
| error#8 | function | — | — | — |
| error#6 | function | — | — | — |
