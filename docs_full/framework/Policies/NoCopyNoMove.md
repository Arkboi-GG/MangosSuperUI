# NoCopyNoMove

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`NoCopyNoMove` is a C++ mixin class designed to enforce strict immovability and non-copyability on any class that inherits from it. It resides in the `MaNGOS::Policies` namespace within `ObjectConstructorTraits.h`.

Its primary responsibility is to prevent accidental or intentional duplication or transfer of ownership of objects that manage unique resources, singletons, or state that cannot be safely copied or moved. By deleting both the copy/move constructors and the copy/move assignment operators, it ensures that instances of derived classes can only be created in-place (e.g., via placement new or direct construction) and cannot be passed by value, returned by value, or assigned to other variables.

This is a foundational utility in the MaNGOS codebase for defining "non-movable" entities, often used for base classes of game objects, players, or world states where identity is tied to memory address or specific resource handles that do not support transfer semantics.

## Member-by-Member Behavior

The class consists entirely of special member functions declared as `delete` or `default`. There is no executable logic; all behavior is enforced at compile time by the compiler rejecting attempts to use these operations.

### Construction and Destruction

*   **`NoCopyNoMove()`**: The default constructor is defined as `default` and placed in the `protected` section. This allows derived classes to construct instances of `NoCopyNoMove` as part of their own initialization, but prevents external code from creating standalone instances of `NoCopyNoMove` directly.
*   **`~NoCopyNoMove()`**: The destructor is defined as `default` and also `protected`. This ensures that cleanup happens automatically when a derived object is destroyed, but again, prevents external deletion of raw pointers to `NoCopyNoMove` if such a scenario were possible (though typically managed by the derived class's visibility).

### Copy Semantics (Deleted)

*   **`NoCopyNoMove(const NoCopyNoMove&)`**: The copy constructor is explicitly deleted. Any attempt to copy an object inheriting from `NoCopyNoMove` will result in a compilation error.
*   **`operator=(const NoCopyNoMove&)`**: The copy assignment operator is explicitly deleted. Assigning one instance to another is forbidden.

### Move Semantics (Deleted)

*   **`NoCopyNoMove(NoCopyNoMove&&)`**: The move constructor is explicitly deleted. Unlike many modern C++ designs that favor move semantics for performance, this policy explicitly rejects moving. This is crucial for objects whose internal state relies on fixed memory addresses or non-transferable system resources.
*   **`operator=(NoCopyNoMove&&)`**: The move assignment operator is explicitly deleted. Moving an existing instance to another location is forbidden.

## Cross-Unit Boundaries

This unit has **no outgoing calls** to other units and is **not called by** other units in the traditional sense of function invocation. Its interaction with the rest of the codebase is purely through inheritance.

*   **Called by (Other Units)**: Various classes throughout the MaNGOS codebase (such as `Creature`, `Player`, `GameObject`, etc., though specific callers are not listed in the provided map) inherit from `NoCopyNoMove`. The "call" here is the compiler checking the accessibility and availability of these special member functions during the construction/destruction/assignment of those derived classes.
*   **Calls out (Other Units)**: None. The class is self-contained and relies solely on language features.

## Data Model

This unit interacts with **no database tables**. It is a pure C++ language-level policy mechanism.

## Notable Implementation Details

1.  **Protected Default Constructor/Destructor**: The default constructor and destructor are `protected`. This is a deliberate design choice to ensure `NoCopyNoMove` is used strictly as a non-public base class (typically via private or protected inheritance in the derived class, or simply as a mixin). It prevents users from accidentally creating a standalone `NoCopyNoMove` object, which would be useless since it contains no data.
2.  **Explicit Deletion vs. Implicit**: In C++11 and later, if a class defines a move constructor, the copy constructor is not implicitly generated. However, explicitly deleting them (`= delete`) provides clearer error messages and intent. It signals to maintainers that the lack of copy/move is a *policy decision*, not an oversight.
3.  **Contrast with `NoCopyButAllowMove`**: The header also defines `NoCopyButAllowMove` in the same namespace. `NoCopyNoMove` is the stricter variant. Engineers must choose between these two based on whether the derived object's resources can be transferred (moved) or must remain static. `NoCopyNoMove` is chosen when the object's identity is bound to its memory location or when the underlying resources (like certain OS handles or complex graph structures) do not support efficient or safe transfer.
4.  **Namespace**: It is nested within `MaNGOS::Policies`, indicating it is part of a set of reusable design patterns or constraints applied across the engine.

## Member Reference

*   **NoCopyNoMove**: Default constructor, defined as `default`, access level `protected`. Allows derived classes to initialize the base part of the object.
*   **~NoCopyNoMove**: Destructor, defined as `default`, access level `protected`. Handles cleanup for the base part of the object when a derived instance is destroyed.
*   **NoCopyNoMove#3**: Copy constructor, explicitly `delete`. Prevents copying of objects inheriting from this class.
*   **operator=#2**: Move assignment operator, explicitly `delete`. Prevents moving an existing object to another.
*   **NoCopyNoMove#2**: Move constructor, explicitly `delete`. Prevents moving construction of objects inheriting from this class.
*   **operator=**: Copy assignment operator, explicitly `delete`. Prevents assigning one object to another.

---

<!-- machine-true, projected from graph.json -->

## Map — NoCopyNoMove

*Source:* ObjectConstructorTraits.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| NoCopyNoMove | ctor | — | — | — |
| ~NoCopyNoMove | decl | — | — | — |
| NoCopyNoMove#3 | decl | — | — | — |
| operator=#2 | decl | — | — | — |
| NoCopyNoMove#2 | decl | — | — | — |
| operator= | decl | — | — | — |
