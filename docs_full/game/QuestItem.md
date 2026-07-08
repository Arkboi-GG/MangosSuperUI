# QuestItem

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# QuestItem

**Purpose & Responsibilities**  
`QuestItem` is a lightweight data structure within `LootMgr.h` that tracks the state of a specific quest-related item slot within a loot table. It records the item’s positional index in the client-facing quest item list and whether that item has been claimed (looted). It contains no logic beyond construction and state storage; all behavioral decisions regarding quest items are handled by other units such as `Loot`, `LootTemplate`, and `Player`.

**Member-by-Member Behavior**  
The unit defines two constructors:
- **`QuestItem()`**: Default constructor. Initializes `index` to `0` and `is_looted` to `false`. Used when constructing empty or placeholder entries.
- **`QuestItem(uint8 _index, bool _islooted)`**: Parameterized constructor. Sets `index` to `_index` and `is_looted` to `_islooted` (defaulting to `false`). Used when populating quest item lists with known positions and states.

Both constructors are trivial initializers with no side effects, no cross-unit calls, and no database interaction.

**Cross-Unit Boundaries**  
This unit has no outgoing calls to other units and is not called by any other unit according to the MAP. However, in practice, instances of `QuestItem` are stored in `QuestItemList` vectors and `QuestItemMap` maps within the `Loot` struct (defined in the same header). The `Loot` unit manages the lifecycle of these objects, including creation, deletion, and state updates. No other units directly instantiate or manipulate `QuestItem` outside of `Loot`’s internal logic.

**Data Model**  
This unit does not interact with any database tables. It operates entirely in memory as part of the loot generation and distribution system.

**Notable Implementation Details**  
- The `index` field corresponds to the position in the client’s quest item UI list, which has a maximum capacity of 32 items (`MAX_NR_QUEST_ITEMS`). This limit is enforced elsewhere in the codebase, not within `QuestItem`.
- The `is_looted` flag is critical for preventing duplicate claims of quest items. Once set to `true`, the item should remain in that state for the lifetime of the loot object.
- `QuestItem` is intentionally minimal to avoid overhead in loot processing, which can involve many items and frequent state checks.

## Member Reference

**QuestItem**  
Default constructor. Initializes `index` to `0` and `is_looted` to `false`.

**QuestItem#2**  
Parameterized constructor. Accepts `uint8 _index` and optional `bool _islooted` (defaults to `false`). Sets member variables accordingly.

---

<!-- machine-true, projected from graph.json -->

## Map — QuestItem

*Source:* LootMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| QuestItem | ctor | — | — | — |
| QuestItem#2 | ctor | — | — | — |
