# ICallback

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ICallback

`ICallback` is a minimal abstract base class defining the interface for asynchronous or deferred execution units within the MaNGOS database subsystem. It provides a uniform `Execute()` method that callers can invoke without knowing the specific payload or target object of the callback. The class itself contains no state; all behavior is implemented by derived template classes (`_ICallback`, `Callback`, `QueryCallback`, etc.) defined in the same header.

This unit does not interact with any database tables directly. It serves purely as a polymorphic handle for the database query execution engine to dispatch work back to game-world objects or static functions after a query completes.

## Member Reference

**Execute**
A pure virtual function declaration (`virtual void Execute() = 0`). This defines the contract for all callbacks: when the database system is ready to process the result of an operation, it calls this method. Concrete implementations in derived classes (e.g., `_ICallback`) forward this call to the underlying stored method pointer.

**~ICallback**
A virtual destructor. It is necessary because `ICallback` is designed to be deleted through base-class pointers (e.g., `delete callback;`). Without a virtual destructor, deleting a derived object via an `ICallback*` would result in undefined behavior. The implementation is empty, as all cleanup is handled by the derived template classes.

---

<!-- machine-true, projected from graph.json -->

## Map — ICallback

*Source:* DatabaseCallback.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Execute | decl | — | — | — |
| ~ICallback | dtor | — | — | — |
