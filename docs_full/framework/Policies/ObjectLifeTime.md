# ObjectLifeTime

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ObjectLifeTime

## Purpose & Responsibilities

`ObjectLifeTime` is a minimal utility module within the `MaNGOS` namespace designed to manage process-level cleanup routines and provide a placeholder mechanism for handling dangling references to objects. It serves two distinct, unrelated purposes:

1.  **Process Exit Registration:** It provides a C++-friendly interface (`at_exit`) to register functions to be executed when the server process terminates, wrapping the standard C library `std::atexit`.
2.  **Dead Reference Handling:** It defines a template class `ObjectLifeTime<T>` that offers a static method `OnDeadReference`. Currently, this method acts as a fatal error handler, throwing a `std::runtime_error` if invoked. The code comments explicitly state that dead references are not actively handled beyond this exception, suggesting this is a stub for future safety mechanisms or a safeguard against accessing destroyed objects.

The module contains no database interactions, no cross-unit dependencies, and no complex state management. It is a self-contained utility for lifecycle hooks.

## Member-by-Member Behavior

### Process Exit Management

**`external_wrapper`**
This is an `extern "C"` function defined in `ObjectLifeTime.cpp`. Its sole purpose is to bridge C++ function pointers to the C-style signature required by `std::atexit`. It accepts a `void*` pointer, casts it implicitly to a `void (*)()` function pointer, and registers it with `std::atexit`. This wrapper exists because `std::atexit` expects a specific C linkage signature, and direct passing of C++ function pointers can sometimes lead to linkage issues or require explicit casting that this wrapper encapsulates cleanly.

**`at_exit`**
Defined in `ObjectLifeTime.cpp` within the `MaNGOS` namespace, this function takes a C++ function pointer `void (*func)()`. It casts this pointer to `void*` and passes it to `external_wrapper`. This provides a clean, namespace-scoped API for registering cleanup functions without exposing the internal casting mechanics to callers.

### Object Lifecycle Safety

**`ScheduleCall`**
A static member function of the template class `ObjectLifeTime<T>`, defined in `ObjectLifeTime.h`. It accepts a function pointer `void (*destroyer)()` and immediately forwards it to the global `MaNGOS::at_exit` function. This allows classes using `ObjectLifeTime` to register their own cleanup destructors or finalization routines to run at process exit. It essentially binds the concept of object destruction scheduling to the process lifetime.

**`OnDeadReference`**
A static member function of `ObjectLifeTime<T>`, declared in `ObjectLifeTime.h` and defined in the same file. It is marked with `DECLSPEC_NORETURN` and `ATTR_NORETURN`, indicating it does not return control to the caller. The implementation throws a `std::runtime_error` with the message "Dead Reference". The comment `// We don't handle Dead Reference for now` indicates this is a defensive stub. If any part of the system attempts to access an object whose lifetime has ended (and presumably triggers this callback), the server will crash with this exception. This suggests that the intended design was to have a more graceful handling of stale pointers, but currently, it treats such events as unrecoverable errors.

## Cross-Unit Boundaries

The `ObjectLifeTime` unit is entirely isolated. According to the MAP, it has no outgoing calls to other units and is not called by any other units listed in the cross-reference data. While `std::atexit` is a standard library function, it is not considered an external unit in the context of this codebase's architectural map. Therefore, `ObjectLifeTime` operates as a standalone utility with no integration points into the rest of the MaNGOS engine's core systems (such as World, Map, or Creature modules).

## Data Model

This unit does not interact with any database tables. It performs no SQL queries, inserts, updates, or deletes. All operations are memory-based and related to process lifecycle management.

## Notable Implementation Details

1.  **Casting Safety in `external_wrapper`:** The function `external_wrapper` casts a `void*` back to a function pointer `void (*)()`. While this is common in C/C++ interop, it relies on the assumption that the `void*` passed from `at_exit` is indeed a valid function pointer. There is no validation performed. If garbage data were passed, the behavior would be undefined, though `at_exit` itself ensures only registered functions are called.
2.  **Noreturn Attributes:** The `OnDeadReference` function uses both `DECLSPEC_NORETURN` (likely a compiler-specific macro for declaration) and `ATTR_NORETURN` (likely a GCC/Clang attribute). This dual marking ensures that compilers optimize around the fact that the function never returns, preventing warnings about unreachable code after the call.
3.  **Stub Implementation:** The comment in `OnDeadReference` is critical for maintainers. It signals that the current behavior (throwing an exception) is temporary or incomplete. Any future work on object lifetime tracking should replace this throw with proper logging, cleanup, or safe-null handling.
4.  **Template Instantiation:** `ObjectLifeTime` is a template class, but it has no member variables. It only provides static methods. This means `ObjectLifeTime<T>` and `ObjectLifeTime<U>` are distinct types, but they share the same implementation logic. The type parameter `T` is unused in the current implementation, suggesting the class was designed to be associated with a specific object type for potential future specialization, but currently, it treats all types identically.

## Member Reference

**external_wrapper**
An `extern "C"` function that wraps `std::atexit`. It accepts a `void*` pointer, interprets it as a `void (*)()` function pointer, and registers it for execution at program termination. This facilitates C++ function registration via C linkage.

**at_exit**
A function in the `MaNGOS` namespace that accepts a C++ function pointer `void (*func)()`. It casts the pointer to `void*` and delegates to `external_wrapper`, providing a clean API for registering exit handlers.

**ScheduleCall**
A static template member function of `ObjectLifeTime<T>`. It accepts a destroyer function pointer and registers it for execution at process exit by calling `MaNGOS::at_exit`. It allows object-specific cleanup routines to be scheduled globally.

**OnDeadReference**
A static template member function of `ObjectLifeTime<T>`, marked as noreturn. It throws a `std::runtime_error` with the message "Dead Reference". It serves as a stub for handling accesses to destroyed objects, currently treating such events as fatal errors.

---

<!-- machine-true, projected from graph.json -->

## Map — ObjectLifeTime

*Source:* ObjectLifeTime.cpp, ObjectLifeTime.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| external_wrapper | function | — | — | — |
| at_exit | function | — | — | — |
| ScheduleCall | function | — | — | — |
| OnDeadReference | function | — | — | — |
