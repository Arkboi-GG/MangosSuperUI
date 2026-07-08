<!-- provenance: failed-members -->
# hash

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ObjectGuid Hash Specialization

## Purpose & Responsibilities

This unit provides the explicit template specialization of `std::hash` for the `ObjectGuid` class. Its sole responsibility is to enable `ObjectGuid` instances to be used as keys in standard unordered associative containers (such as `std::unordered_map` and `std::unordered_set`) by defining how an `ObjectGuid` is converted into a `std::size_t` hash value.

The implementation delegates the hashing logic entirely to the underlying `uint64` representation of the GUID. It retrieves the raw 64-bit integer value via `ObjectGuid::GetRawValue()` and passes it to the standard `std::hash<uint64>` functor. This ensures that the hash distribution properties are determined by the compiler's standard library implementation for 64-bit integers, while maintaining strict consistency with the equality semantics of `ObjectGuid` (which also compares based on the raw `uint64` value).

## Member-by-Member Behavior

### **operator()**

The `operator()` method is the core of the hash specialization. It accepts a constant reference to an `ObjectGuid` instance (`k`) and returns a `std::size_t`.

1.  **Delegation**: It calls `k.GetRawValue()` to obtain the internal `uint64` storage of the GUID.
2.  **Hashing**: It invokes `std::hash<uint64>()` on that raw value.
3.  **Return**: The resulting `std::size_t` is returned.

This design ensures that two `ObjectGuid` objects that compare equal (via `ObjectGuid::operator==`, which compares raw values) will always produce the same hash code, satisfying the requirements for use in unordered containers.

## Cross-Unit Boundaries

*   **Calls out**: None. The member does not call any other units listed in the map. It relies on `ObjectGuid::GetRawValue()` (defined in the same header, `ObjectGuid.h`) and `std::hash<uint64>` (standard library).
*   **Called by**: None. The map indicates no external units explicitly call this specific hash specialization member. However, implicitly, any code using `std::unordered_set<ObjectGuid>` or `std::unordered_map<ObjectGuid, T>` within the codebase will trigger this function through the standard library's container implementations.

## Data Model

This unit does not interact with any database tables. It operates purely on in-memory data structures.

## Notable Implementation Details

*   **Template Specialization**: The code uses explicit template specialization (`template <> struct std::hash<ObjectGuid>`) to inject custom behavior into the `std` namespace for a user-defined type. This is a standard C++ idiom for enabling hash-based containers.
*   **No Custom Logic**: There is no custom bit-mixing or cryptographic hashing involved. The unit trusts the standard library's `std::hash<uint64>` to provide sufficient distribution for the GUID values used in the application.
*   **Consistency with Equality**: Because `ObjectGuid::operator==` compares the raw `uint64` values, and this hash function hashes the raw `uint64` values, the invariant `a == b implies hash(a) == hash(b)` is strictly maintained.

## Member Reference

**operator()**
Computes the hash value for an `ObjectGuid` by delegating to `std::hash<uint64>` on the GUID's raw 64-bit integer representation.

---

<!-- machine-true, projected from graph.json -->

## Map — hash

*Source:* ObjectGuid.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| operator() | method | — | — | — |

---

<!-- verify: failed-members | invented: operator -->
