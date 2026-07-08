# NoCopyButAllowMove

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`NoCopyButAllowMove` is a C++ mixin class designed to enforce specific object lifetime semantics through inheritance. Its sole responsibility is to disable copying while explicitly enabling moving for any class that inherits from it.

By inheriting from `MaNGOS::Policies::NoCopyButAllowMove`, a derived class automatically:
1.  **Prevents Copying:** The compiler will reject attempts to copy-construct or copy-assign instances of the derived class. This is useful for classes managing unique resources (e.g., raw pointers, file handles, network sockets) where duplication would lead to double-free errors or logical inconsistencies.
2.  **Enables Moving:** The compiler generates default move constructor and move assignment operator. This allows efficient transfer of ownership of resources from one instance to another, which is critical for performance in containers (like `std::vector`) or when returning objects from functions.

This pattern is part of the `MaNGOS::Policies` namespace, indicating it is a reusable utility within the MaNGOS codebase for enforcing common design constraints without repeating boilerplate `delete` and `default` specifiers in every class definition.

## Member-by-Member Behavior

The class consists entirely of special member functions (constructors, destructor, assignment operators). There are no data members.

### Lifecycle Control

*   **`NoCopyButAllowMove()` (Default Constructor):** Declared `protected` and `= default`. It allows derived classes to construct instances but prevents direct instantiation of `NoCopyButAllowMove` itself (since it is intended as a base class).
*   **`~NoCopyButAllowMove()` (Destructor):** Declared `protected` and `= default`. Ensures proper cleanup order during destruction of derived classes. Like the constructor, it being protected prevents direct use of the base class.

### Copy Semantics (Disabled)

*   **`NoCopyButAllowMove(const NoCopyButAllowMove&)` (Copy Constructor):** Explicitly `= delete`. Any attempt to copy an object inheriting from this trait will result in a compile-time error.
*   **`operator=(const NoCopyButAllowMove&)` (Copy Assignment Operator):** Explicitly `= delete`. Prevents assigning one instance to another via copy semantics.

### Move Semantics (Enabled)

*   **`NoCopyButAllowMove(NoCopyButAllowMove&&)` (Move Constructor):** Explicitly `= default`. Allows the compiler to generate a standard move constructor. Since the class has no data members, this effectively becomes a no-op in terms of data transfer, but it satisfies the type system requirements for movable types.
*   **`operator=(NoCopyButAllowMove&&)` (Move Assignment Operator):** Explicitly `= default`. Allows the compiler to generate a standard move assignment operator.

## Cross-Unit Boundaries

This unit is a pure utility header. It does not call into other units, nor is it called by other units in the sense of runtime execution flow. Its "collaboration" is purely at the **compile time** level:

*   **Called By (Inheritance):** Other classes in the MaNGOS codebase inherit from `NoCopyButAllowMove` to adopt its copy/move restrictions. The MAP shows no specific callers because these relationships are structural (inheritance) rather than procedural (function calls).
*   **Calls Out:** None. The class contains no logic that invokes other functions.

## Data Model

This unit does not interact with any database tables. It is a compile-time type trait.

## Notable Implementation Details

1.  **Protected Base Constructor/Destructor:** The default constructor and destructor are `protected`. This is a deliberate design choice to prevent users from accidentally creating standalone instances of `NoCopyButAllowMove`. It enforces that the class is used strictly as a mixin/base class. If they were `public`, one could write `NoCopyButAllowMove obj;`, which is semantically meaningless since the class holds no state.
2.  **Explicit `= default` for Move Operations:** In C++11 and later, if you delete the copy operations, the compiler will *not* automatically generate move operations unless you explicitly request them (or if the class has no user-declared copy/move/destructor). By explicitly marking the move operations as `= default`, the author ensures that even if future changes add data members or other constructors, the move semantics remain enabled unless explicitly removed. This makes the intent robust against accidental compiler-generated defaults.
3.  **Namespace Organization:** Located in `MaNGOS::Policies`, this suggests a broader framework of such traits. Indeed, the same header defines `NoCopyNoMove`, offering a parallel option for classes that should be neither copied nor moved (e.g., singleton-like or context-bound objects).

## Member Reference

*   **NoCopyButAllowMove**: Default constructor, declared `protected` and `= default`. Allows derived classes to initialize the base subobject.
*   **~NoCopyButAllowMove**: Destructor, declared `protected` and `= default`. Cleans up the base subobject during derived class destruction.
*   **NoCopyButAllowMove#3**: Move constructor, declared `public` and `= default`. Enables moving ownership of derived class resources.
*   **operator=#2**: Move assignment operator, declared `public` and `= default`. Enables moving ownership from one derived instance to another.
*   **NoCopyButAllowMove#2**: Copy constructor, declared `public` and `= delete`. Prevents copying of derived class instances.
*   **operator=**: Copy assignment operator, declared `public` and `= delete`. Prevents copy assignment of derived class instances.

---

<!-- machine-true, projected from graph.json -->

## Map — NoCopyButAllowMove

*Source:* ObjectConstructorTraits.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| NoCopyButAllowMove | ctor | — | — | — |
| ~NoCopyButAllowMove | decl | — | — | — |
| NoCopyButAllowMove#3 | decl | — | — | — |
| operator=#2 | decl | — | — | — |
| NoCopyButAllowMove#2 | decl | — | — | — |
| operator= | decl | — | — | — |
