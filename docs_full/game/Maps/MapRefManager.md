<!-- provenance: verbose -->
# MapRefManager

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`MapRefManager` is a thin wrapper around `RefManager<Map, Player>` that provides strongly-typed access to the doubly-linked list of `Player` objects on a `Map`. It exposes `MapReference` pointers via `getFirst`/`getLast` and STL-compatible iterators (`begin`/`end`/`rbegin`/`rend`) for traversing the player list. It contains no independent logic or database interactions.

## Member-by-Member Behavior

All members delegate to the parent `RefManager` or construct iterators from its results.

### Direct Accessors
*   **`getFirst` / `getFirst#2`**: Return mutable/const `MapReference*` to the list head.
*   **`getLast` / `getLast#2`**: Return mutable/const `MapReference*` to the list tail.

### Iterators
*   **`begin` / `end`**: Mutable iterators for forward traversal. `end` uses `nullptr` as the sentinel.
*   **`begin#2` / `end#2`**: Const iterators for read-only forward traversal.
*   **`rbegin` / `rend`**: Reverse iterators. `rbegin` starts at the last element; `rend` uses `nullptr` as the sentinel.

## Cross-Unit Boundaries

*   **`Map.Main`**: Calls `begin`/`end` for core updates (`Update#3`, `UpdatePlayers`, etc.), packet processing, and cleanup (`CrashUnload`). Calls `getFirst` for `TeleportAllPlayersTo`.
*   **`instance_molten_core/Update`** and **`Player.Main/GiveLevel`**: Call `getFirst#2` to inspect the first player.
*   **`ChatHandler.MiscCommands/HandleInstanceContinentsCommand`** and **`ThreatListCopier.battleground_alterac/UpdateAI#8`**: Call `begin#2`/`end#2` for administrative or battleground-specific player scans.
*   **`getLast`**, **`getLast#2`**, **`rbegin`**, **`rend`**: Currently uncalled by other units.

## Data Model

This unit does not interact with any database tables.

## Notable Implementation Details

1.  **Explicit Casting**: `getFirst`/`getLast` cast the base class return type to `MapReference*`.
2.  **Null Sentinel**: `end` and `rend` initialize iterators with `nullptr`, which `LinkedListHead::Iterator` treats as the termination condition.
3.  **No Ownership**: Player addition/removal is handled by `RefManager` or `Map`; this unit only exposes traversal.

## Member Reference

**getFirst**  
Returns mutable `MapReference*` to the first player. Called by `Map.Main/TeleportAllPlayersTo`.

**getFirst#2**  
Returns const `MapReference*` to the first player. Called by `instance_molten_core/Update` and `Player.Main/GiveLevel`.

**getLast**  
Returns mutable `MapReference*` to the last player. Uncalled.

**getLast#2**  
Returns const `MapReference*` to the last player. Uncalled.

**begin**  
Returns mutable `iterator` starting at the first element. Called by `Map.Main` for updates, packets, and cleanup.

**end**  
Returns mutable `iterator` representing the end of the list (`nullptr`). Paired with `begin`.

**rbegin**  
Returns mutable reverse `iterator` starting at the last element. Uncalled.

**rend**  
Returns mutable reverse `iterator` representing the end of the reverse list (`nullptr`). Uncalled.

**begin#2**  
Returns const `const_iterator` starting at the first element. Called by `ChatHandler.MiscCommands/HandleInstanceContinentsCommand` and `ThreatListCopier.battleground_alterac/UpdateAI#8`.

**end#2**  
Returns const `const_iterator` representing the end of the list (`nullptr`). Paired with `begin#2`.

---

<!-- machine-true, projected from graph.json -->

## Map — MapRefManager

*Source:* MapRefManager.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| getFirst | method | — | Map.Main/TeleportAllPlayersTo | — |
| getFirst#2 | method | — | instance_molten_core/Update, Player.Main/GiveLevel | — |
| getLast | method | — | — | — |
| getLast#2 | method | — | — | — |
| begin | method | — | Map.Main/CrashUnload, Map.Main/ProcessSessionPackets, Map.Main/Update#3, Map.Main/UpdateActiveCellsAsynch, Map.Main/UpdateActiveCellsSynch, Map.Main/UpdatePlayers | — |
| end | method | — | Map.Main/CrashUnload, Map.Main/ProcessSessionPackets, Map.Main/Update#3, Map.Main/UpdateActiveCellsAsynch, Map.Main/UpdateActiveCellsSynch, Map.Main/UpdatePlayers | — |
| rbegin | method | — | — | — |
| rend | method | — | — | — |
| begin#2 | method | — | ChatHandler.MiscCommands/HandleInstanceContinentsCommand, ThreatListCopier.battleground_alterac/UpdateAI#8 | — |
| end#2 | method | — | ChatHandler.MiscCommands/HandleInstanceContinentsCommand, ThreatListCopier.battleground_alterac/UpdateAI#8 | — |
