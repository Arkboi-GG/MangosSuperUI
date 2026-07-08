# LinkedListHead

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`LinkedListHead` is a lightweight, intrusive doubly-linked list container implemented in `LinkedList.h`. It serves as the foundational data structure for managing ordered collections of objects throughout the WowVMaNGOS codebase, particularly for entity management (creatures, players, transports) and script execution queues.

Unlike standard library containers (`std::list`), `LinkedListHead` is **intrusive**: it does not allocate nodes separately. Instead, the elements being stored must inherit from or contain a `LinkedListElement`. This design avoids heap allocations for the list nodes themselves, reducing memory fragmentation and overhead—a critical optimization for high-frequency operations like combat updates, threat management, and zone transitions.

The class maintains:
1.  **Sentinel Nodes:** Two internal `LinkedListElement` instances (`iFirst` and `iLast`) that act as permanent head and tail markers. This simplifies insertion/deletion logic by eliminating special cases for empty lists or single-element lists.
2.  **Size Tracking:** A `uint32` counter (`iSize`) that tracks the number of elements. This allows O(1) size checks, though the implementation includes a fallback recalculation mechanism if the counter becomes inconsistent.

## Member-by-Member Behavior

### Initialization and State Inspection

*   **`LinkedListHead` (Constructor):** Initializes the list as empty. It links `iFirst` to `iLast` and vice versa, creating a circular-like sentinel structure where `iFirst->next` is `iLast` and `iLast->prev` is `iFirst`. Sets `iSize` to 0.
*   **`isEmpty`:** Returns `true` if the list contains no user elements. It checks if `iFirst.iNext` is effectively the sentinel `iLast` (by verifying `!iFirst.iNext->isInList()`). This method is heavily used across the codebase to check for active entities, such as alive bosses in `boss_cthun`, valid targets in `CreatureAI`, or players in instances via `MapManager`.
*   **`getSize`:** Returns the number of elements. If `iSize` is non-zero, it returns the cached value. If `iSize` is zero, it iterates through the entire list to count elements, returning the computed result. This defensive behavior suggests that `iSize` might occasionally become desynchronized with the actual list contents, requiring a repair pass. It is called by `ChatHandler.PlayerBotMgr` for party bot requirements, `HostileRefManager` for threat assistance calculations, and `MapManager` to count players in instances.

### Accessors

*   **`getFirst` / `getLast`:** Return pointers to the first or last user element in the list. If the list is empty, they return `nullptr`. These are const-correct, providing both mutable and immutable access. Note that the MAP lists `getFirst#2` and `getLast#2`; these correspond to the `const` overloads of these methods.
*   **`insertFirst` / `insertLast`:** Insert a `LinkedListElement` at the beginning or end of the list. `insertFirst` delegates to `iFirst.insertAfter()`, and `insertLast` delegates to `iLast.insertBefore()`. These methods do **not** update the `iSize` counter; the caller is responsible for calling `incSize()` or `decSize()` appropriately. `insertFirst` is called by `MapReference::targetObjectBuildLink` when linking objects into a map's spatial or logical structures.

### Size Management

*   **`incSize` / `decSize`:** Increment or decrement the internal `iSize` counter. These are simple arithmetic operations. They are called by `MapReference` during link building (`targetObjectBuildLink`) and destruction (`sourceObjectDestroyLink`, `targetObjectDestroyLink`). The separation of insertion logic from size tracking places the burden of consistency on the caller, which is a common pattern in low-level C++ containers to allow batch operations or conditional sizing.

### Iterator Support

The class defines an inner `Iterator` template class that provides STL-compatible bidirectional iteration over the list. This allows algorithms expecting iterators to work with `LinkedListHead`. The iterator wraps a `LinkedListElement*` and implements standard operators (`++`, `--`, `*`, `->`, `==`, `!=`). It uses the `next()` and `prev()` methods of `LinkedListElement` to traverse the list.

## Cross-Unit Boundaries

`LinkedListHead` is a passive data structure; it does not initiate calls to other units. However, it is extensively consumed by various subsystems:

1.  **Boss Encounters (`boss_cthun`, `boss_nefarian`):**
    *   `boss_cthun::SelectRandomAliveNotStomach` and `boss_nefarian::HandleClassCall` use `isEmpty` to check if there are valid targets or phases remaining.
    *   `boss_nefarian::OnPeriodicTickEnd` likely uses `isEmpty` to determine if periodic actions should continue.

2.  **Creature AI & Combat (`CreatureAI`, `HostileRefManager`):**
    *   `CreatureAI::ClearTargetIcon` uses `isEmpty` to manage target icons.
    *   `HostileRefManager::threatAssist` uses `getSize` to calculate threat distribution among party members.

3.  **Map & Instance Management (`Map`, `ScriptedInstance`, `instance_*`):**
    *   `Map::Reset` and `Map::Remove` use `isEmpty` and `getSize` to clean up or verify map states.
    *   Various instance scripts (`instance_blackrock_depths`, `instance_naxxramas`, `instance_stratholme`, `instance_temple_of_ahnqiraj`) use `isEmpty` to check for specific conditions (e.g., princess replacement, whisper updates, escort AI states).
    *   `Transport` uses `isEmpty` to manage player updates when entering/exiting range.

4.  **Unit Updates (`Unit`):**
    *   `Unit::Update` uses `isEmpty` to skip processing if certain linked lists (e.g., aura lists, target lists) are empty.

5.  **MapReference:**
    *   `MapReference::targetObjectBuildLink` calls `insertFirst` and `incSize` to add an object to a list.
    *   `MapReference::sourceObjectDestroyLink` and `targetObjectDestroyLink` call `decSize` to remove an object.

## Data Model

This unit does not interact with any database tables. It is a pure in-memory data structure.

## Notable Implementation Details

1.  **Sentinel-Based Design:** The use of `iFirst` and `iLast` as permanent sentinels means that `getFirst()` always returns `iFirst.iNext` (unless empty), and `getLast()` returns `iLast.iPrev`. This avoids null-pointer checks during traversal, as the sentinels are always valid objects.
2.  **Size Inconsistency Handling:** The `getSize()` method's fallback to iterating the list if `iSize` is zero is a notable defensive measure. It implies that `iSize` can become stale or incorrect, possibly due to bugs in callers that fail to call `incSize`/`decSize` symmetrically. This makes `getSize()` potentially O(N) in the worst case, which is unusual for a linked list size query.
3.  **Manual Memory Management:** The `LinkedListElement` destructor calls `delink()`, which removes the element from the list if it is still linked. This prevents dangling pointers in the list when an element is destroyed. However, the list itself does not delete the elements; the caller must manage the lifetime of the objects containing the `LinkedListElement`.
4.  **No Exception Safety:** The insertion and deletion methods do not provide strong exception safety guarantees. If an exception occurs during insertion (e.g., in a custom comparator or callback), the list state may be corrupted. Given the low-level nature of the code, this is likely accepted as a trade-off for performance.
5.  **Iterator Validity:** Iterators are invalidated by any modification to the list (insertion or deletion) that affects the position of the iterated element. This is standard for linked lists but worth noting for users relying on iterator stability.

## Member Reference

*   **`LinkedListHead`**: Constructor that initializes the sentinel nodes (`iFirst`, `iLast`) and sets `iSize` to 0.
*   **`isEmpty`**: Returns `true` if the list has no user elements. Used extensively by boss AI, creature AI, map managers, and instance scripts to check for active entities or conditions.
*   **`getFirst`**: Returns a pointer to the first user element, or `nullptr` if empty.
*   **`getFirst#2`**: Const overload of `getFirst`.
*   **`getLast`**: Returns a pointer to the last user element, or `nullptr` if empty.
*   **`getLast#2`**: Const overload of `getLast`.
*   **`insertFirst`**: Inserts an element at the beginning of the list. Called by `MapReference::targetObjectBuildLink`. Does not update `iSize`.
*   **`insertLast`**: Inserts an element at the end of the list. Does not update `iSize`.
*   **`getSize`**: Returns the number of elements. Uses cached `iSize` if non-zero; otherwise, iterates the list to count. Called by `ChatHandler.PlayerBotMgr`, `HostileRefManager`, `Map`, and `MapManager`.
*   **`incSize`**: Increments the internal size counter. Called by `MapReference::targetObjectBuildLink`.
*   **`decSize`**: Decrements the internal size counter. Called by `MapReference::sourceObjectDestroyLink` and `MapReference::targetObjectDestroyLink`.

---

<!-- machine-true, projected from graph.json -->

## Map — LinkedListHead

*Source:* LinkedList.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| LinkedListHead | ctor | — | — | — |
| isEmpty | method | — | boss_cthun/SelectRandomAliveNotStomach, boss_nefarian/HandleClassCall, boss_nefarian/OnPeriodicTickEnd, Creature.Main/SetInCombatWithZone, CreatureAI/ClearTargetIcon, eastern_plaguelands/Reset, instance_blackrock_depths/ReplacePrincessIfPossible, instance_naxxramas.Main/SetData, instance_stratholme/SetData, instance_stratholme/Update, instance_temple_of_ahnqiraj/UpdateCThunWhisper, Map.Main/Reset, ScriptedInstance/DoUpdateWorldState, Transport/SendCreateUpdateToMap, Transport/SendOutOfRangeUpdateToMap, Unit.Main/Update, wailing_caverns/UpdateEscortAI | — |
| getFirst | method | — | — | — |
| getFirst#2 | method | — | — | — |
| getLast | method | — | — | — |
| getLast#2 | method | — | — | — |
| insertFirst | method | — | MapReference/targetObjectBuildLink | — |
| insertLast | method | — | — | — |
| getSize | method | — | ChatHandler.PlayerBotMgr/PartyBotAddRequirementCheck, HostileRefManager/threatAssist, Map.Main/Remove#2, MapManager/GetNumPlayersInInstances | — |
| incSize | method | — | MapReference/targetObjectBuildLink | — |
| decSize | method | — | MapReference/sourceObjectDestroyLink, MapReference/targetObjectDestroyLink | — |
