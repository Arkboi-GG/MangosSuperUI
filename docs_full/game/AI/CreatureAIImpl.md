# CreatureAIImpl

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# CreatureAIImpl

**CreatureAIImpl.h** contains the implementation for the `CreatureAIFactory` template class and a collection of utility template functions for random selection. It serves two primary purposes: providing a standardized way to instantiate specific creature AI subclasses from generic data, and offering convenient helpers to select a random value from a small, fixed set of options.

## Purpose & Responsibilities

1.  **AI Instantiation Factory**: The `Create` function implements the core logic for `CreatureAIFactory`. It allows the server to create a specific `CreatureAI` derived class (`REAL_AI`) by passing a raw pointer to the owning `Creature`. This decouples the AI creation process from the specific AI type, enabling the engine to assign AI behaviors dynamically based on configuration or creature type.
2.  **Random Selection Utilities**: The file defines multiple overloads of the `RAND` template function. These utilities allow developers to easily pick one value uniformly at random from a list of 2 to 16 arguments. This is commonly used in AI scripts to randomize behavior, such as choosing between different attack patterns, dialogue lines, or movement targets, without manually implementing random index logic.

## Member-by-Member Behavior

### AI Factory

**Create**
This function implements the `Create` method for the `CreatureAIFactory<REAL_AI>` template class.
*   **Input**: A `void*` pointer containing the data required for construction.
*   **Logic**:
    1.  Reinterprets the `void*` as a `Creature*`.
    2.  Allocates a new instance of the template parameter `REAL_AI`, passing the casted `Creature*` to its constructor.
    3.  Returns the new instance as a `CreatureAI*` base pointer.
*   **Responsibility**: It acts as a bridge between generic object creation interfaces (which often use `void*` for flexibility) and strongly-typed C++ constructors. The caller is responsible for managing the lifetime of the returned pointer.

### Random Selection Utilities

The unit provides ten overloads of the `RAND` template function, supporting 2 through 16 arguments. Each overload follows an identical pattern:
*   **Template Parameter**: `T`, the type of the values being selected.
*   **Mechanism**: Calls `urand` (from `Util.h`) to generate a random integer index within the range `[0, N-1]`, where `N` is the number of arguments.
*   **Selection**: Uses a `switch` statement to return a `const&` to the argument corresponding to the generated index.
*   **Uniformity**: Since `urand` produces uniformly distributed integers, each argument has an equal probability of being selected.

## Cross-Unit Boundaries

*   **Calls Out**:
    *   **`urand` (from `Util.h`)**: Used by all `RAND` overloads to generate the random index.
    *   **`REAL_AI` Constructor**: The `Create` function calls the constructor of the specific AI class specified by the template parameter `REAL_AI`. This dependency is resolved at compile time.
*   **Called By**:
    *   **`Create`**: Called by the AI management system (typically within `CreatureAI.h` or related manager classes) when initializing a creature's AI.
    *   **`RAND`**: Called by various AI scripts, spell handlers, and combat logic routines throughout the codebase to introduce randomness.

## Data Model

This unit does not interact with any database tables. It operates entirely in memory using C++ objects and primitive types.

## Notable Implementation Details

1.  **Unsafe Casting in `Create`**: The `Create` function uses `reinterpret_cast<Creature*>` on the input `void*`. It does not perform null checks or type validation. If an invalid pointer is passed, the subsequent construction of `REAL_AI` will likely fail or cause undefined behavior. Callers must guarantee the validity of the data pointer.
2.  **Raw Pointer Ownership**: `Create` returns a raw pointer allocated with `new`. There is no smart pointer usage or automatic cleanup. The caller must explicitly delete the returned `CreatureAI*` to avoid memory leaks.
3.  **Reference Lifetime in `RAND`**: All `RAND` overloads return `T const&`. This avoids copying potentially large objects but requires that the arguments passed to `RAND` remain valid for the duration of the returned reference's use. In typical usage (e.g., `DoAction(RAND(a, b))`), this is safe because the reference is consumed immediately. However, storing the returned reference for later use is unsafe if the original arguments go out of scope.
4.  **Defensive Switch Defaults**: Each `RAND` switch statement includes a `default:` case that falls through to `case 0:`. This ensures that if `urand` ever returns a value outside the expected range (which should not happen given its contract), the function still returns a valid result (the first argument) rather than exhibiting undefined behavior due to a missing return path.
5.  **Code Duplication**: The `RAND` functions are manually duplicated for each arity (2–16 arguments). This approach avoids the complexity of variadic templates (not standard in older C++ versions) and runtime overhead of arrays or vectors. While verbose, it provides optimal performance and compile-time type safety.

## Member Reference

**Create**
Implements `CreatureAIFactory<REAL_AI>::Create`. Casts a `void*` to `Creature*`, constructs a new `REAL_AI` instance with that creature, and returns it as a `CreatureAI*`. Caller-managed memory.

---

<!-- machine-true, projected from graph.json -->

## Map — CreatureAIImpl

*Source:* CreatureAIImpl.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Create | function | — | — | — |
