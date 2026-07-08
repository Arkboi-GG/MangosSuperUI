# Messager

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Messager

**Purpose & Responsibilities**

`Messager<T>` is a thread-safe, generic utility class designed to decouple the production of work items from their execution. It implements a simple queue pattern where callers can asynchronously enqueue `std::function` callbacks (referred to as "messages") via `AddMessage`, and a separate consumer thread or context can process all pending messages atomically via `Execute`.

The class is templated on type `T`, allowing the stored functions to accept a pointer to an object of type `T` as an argument. This design is typical in server-side C++ applications like MaNGOS/WoWVMaNGOS for handling deferred updates, event dispatching, or batch processing of game objects, players, or world entities without holding locks during the potentially expensive execution phase.

**Member-by-Member Behavior**

### `AddMessage`
This function enqueues a new callback into the internal message queue.
*   **Thread Safety:** It acquires a `std::lock_guard` on `m_messageMutex` before modifying the vector, ensuring that concurrent calls to `AddMessage` from multiple threads are serialized and safe.
*   **Storage:** The provided `std::function<void(T*)>` is appended to `m_messageVector`.
*   **Usage Context:** Typically called from high-frequency or parallel contexts (e.g., multiple worker threads updating a shared entity) where immediate execution is undesirable or unsafe due to locking constraints.

### `Execute`
This function processes all currently queued messages.
*   **Atomic Swap Pattern:** To minimize lock contention, `Execute` does not iterate over the live vector while holding the lock. Instead:
    1.  It creates a local empty vector `messageVectorCopy`.
    2.  It acquires the lock and performs a `std::swap` between the internal `m_messageVector` and the local copy. This operation is O(1) and effectively clears the internal queue while transferring ownership of the current messages to the local scope.
    3.  The lock is released immediately after the swap.
*   **Execution Loop:** It iterates over the local `messageVectorCopy` and invokes each stored function, passing the provided `T* object` as the argument.
*   **Cleanup:** After iteration, the local vector is cleared (though this is technically redundant as the vector goes out of scope, it explicitly signals intent).
*   **Implication:** Because the swap happens atomically, any messages added via `AddMessage` *after* the swap begins will remain in the internal queue for the *next* call to `Execute`. Messages added *before* the swap are guaranteed to be executed in this call.

**Cross-Unit Boundaries**

According to the provided MAP, `Messager` has no explicit cross-unit dependencies listed in the "Calls out" or "Called by" columns. However, in practice:
*   **Called By:** Any unit requiring deferred execution of callbacks on a specific object type `T`. In the MaNGOS codebase, this pattern is often used by AI modules, movement handlers, or world update loops to batch changes.
*   **Calls Out:** None. The class is self-contained and relies only on standard library components (`<vector>`, `<mutex>`, `<functional>`).

**Data Model**

This unit does not interact with any database tables. It operates entirely in memory using standard C++ containers and synchronization primitives.

**Notable Implementation Details**

1.  **No Message Ordering Guarantee Across Swaps:** While messages within a single `Execute` call are processed in FIFO order (as they were pushed to the vector), the atomic swap means that the boundary between "current batch" and "next batch" is determined by the timing of the lock acquisition in `Execute`.
2.  **Exception Safety:** If a callback invoked in `Execute` throws an exception, the loop will terminate early, and subsequent messages in that batch will **not** be executed. The internal `m_messageVector` remains empty (due to the prior swap), so those unexecuted messages are lost unless the caller catches the exception and handles retry logic externally.
3.  **Template Flexibility:** The template parameter `T` allows reuse across different entity types (e.g., `Creature`, `Player`, `GameObject`) without code duplication.
4.  **Lock Granularity:** The lock is held only during the push (`AddMessage`) and the swap (`Execute`). The actual execution of callbacks occurs outside the critical section, preventing deadlocks or performance bottlenecks if callbacks are slow.

## Member Reference

**AddMessage**
Enqueues a `std::function<void(T*)>` callback into the internal thread-safe queue. Acquires `m_messageMutex` to ensure safe concurrent access from multiple producer threads.

**Execute**
Atomically swaps the internal message queue with a local copy, releases the lock, then iterates through the local copy invoking each callback with the provided `T* object`. This minimizes lock hold time and allows producers to continue adding messages while consumers execute previous batches.

---

<!-- machine-true, projected from graph.json -->

## Map — Messager

*Source:* Messager.cpp, Messager.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AddMessage | function | — | — | — |
| Execute | function | — | — | — |
