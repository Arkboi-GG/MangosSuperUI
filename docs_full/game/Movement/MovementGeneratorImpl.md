# MovementGeneratorImpl

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# MovementGeneratorImpl

**Purpose & Responsibilities**

`MovementGeneratorImpl` is a minimal header-only unit that provides the concrete implementation for the `MovementGeneratorFactory` template class. Its sole responsibility is to define the `Create` method, which acts as a factory function for instantiating specific movement generator objects.

In the context of the MaNGOS/WoWVMaNGOS server architecture, creatures (NPCs, players, etc.) require different types of movement behaviors (e.g., wandering, following, attacking). The `MovementGeneratorFactory` allows the system to request a new movement generator of a specific type (`MOVEMENT_GEN`) without hardcoding the instantiation logic in multiple places. This unit ensures that when a factory is asked to create a generator, it correctly casts the provided raw pointer data into a `Creature` object and passes it to the constructor of the target movement generator class.

**Member-by-Member Behavior**

The unit contains only one member, `Create`, which is defined inline within the header.

*   **`Create`**: This function is a template member of `MovementGeneratorFactory`. It accepts a `void*` parameter named `data`. Inside the function, it performs a `reinterpret_cast` to convert this void pointer into a `Creature*`. It then allocates a new instance of the templated class `MOVEMENT_GEN`, passing the casted `creature` pointer to its constructor. Finally, it returns a pointer to the base class `MovementGenerator`. This design enables polymorphic creation of movement generators while ensuring the specific derived class receives the necessary creature context during initialization.

**Cross-Unit Boundaries**

*   **Calls Out**: None. The `Create` function does not call any functions in other units. It relies solely on standard library allocation (`new`) and the constructor of the templated `MOVEMENT_GEN` class.
*   **Called By**: None listed in the map. However, logically, this function is called by any part of the system that utilizes a `MovementGeneratorFactory` instance to assign a new movement behavior to a creature. The caller provides the raw `Creature` pointer as `void*` data.

**Data Model**

This unit does not interact with any database tables. It operates entirely in memory, managing object instantiation and pointer casting.

**Notable Implementation Details**

*   **Template Specialization**: The implementation is tied to the `MovementGeneratorFactory<MOVEMENT_GEN>` template. This means the code is generated at compile time for each specific movement generator type used in the server.
*   **Raw Pointer Casting**: The use of `reinterpret_cast<Creature*>(data)` assumes that the caller always passes a valid `Creature` pointer wrapped as `void*`. There is no runtime type checking or validation in this unit. If a non-Creature pointer were passed, undefined behavior would occur.
*   **Memory Management**: The function uses `new` to allocate the movement generator on the heap. The responsibility for deleting this object lies with the caller or the owning entity (likely the `Creature` or its movement manager), not with this factory function.
*   **Inline Definition**: The entire logic is defined inline in the header file, which is necessary because it is a template function. This avoids separate compilation issues but increases compile-time dependencies for any file including this header.

## Member Reference

**Create**
A template function within `MovementGeneratorFactory` that instantiates a specific movement generator. It takes a `void*` data pointer, casts it to `Creature*`, and returns a newly allocated `MOVEMENT_GEN` object initialized with that creature.

---

<!-- machine-true, projected from graph.json -->

## Map — MovementGeneratorImpl

*Source:* MovementGeneratorImpl.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Create | function | — | — | — |
