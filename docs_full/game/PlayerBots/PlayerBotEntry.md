# PlayerBotEntry

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# PlayerBotEntry

**Purpose & Responsibilities**  
`PlayerBotEntry` is a lightweight data structure representing a single managed bot character within the `PlayerBotMgr` system. It holds the persistent identity (GUID, account ID, name), operational state (online/offline/loading), configuration flags (chat bot, custom bot, removal request), and a pointer to the active AI controller (`PlayerBotAI`). It does not perform logic itself; it is a passive record owned by `PlayerBotMgr` and manipulated by the manager’s methods.

**Member-by-Member Behavior**  
The struct contains two constructors:
- **`PlayerBotEntry(uint64 guid, uint32 account, uint32 chance_)`**: Initializes a bot entry with specific GUID, account ID, and spawn chance. Sets default state to offline, disables chat/custom flags, clears removal request, and nullifies the AI pointer.
- **`PlayerBotEntry()`**: Default constructor initializing all fields to zero/false/null, with `chance` set to 100.0f (note: stored as `uint32` but initialized with float literal, implying implicit conversion).

**Cross-Unit Boundaries**  
This unit has no outgoing calls to other units and is not called by any other unit outside its own definition scope. It is instantiated and managed exclusively by `PlayerBotMgr` (in `PlayerBotMgr.cpp`, not shown here but implied by the MAP’s absence of cross-references). The `ai` member points to `PlayerBotAI`, but ownership and lifecycle management reside in `PlayerBotMgr`.

**Data Model**  
No database tables are touched by this unit. All data is held in memory.

**Notable Implementation Details**  
- The `chance` field is declared as `uint32` but initialized with `100.0f` in the default constructor. This suggests the value is treated as a percentage or weight, but the float-to-int conversion truncates decimals. Maintainers should ensure callers pass integer values or adjust the type if fractional chances are needed.
- The `state` field uses `uint8` but maps to the `PlayerBotState` enum (`PB_STATE_OFFLINE`, etc.). No validation is performed on assignment; external code must ensure valid enum values.
- The `ai` member is a `std::unique_ptr<PlayerBotAI>`, indicating exclusive ownership. However, the constructors initialize it to `nullptr`, meaning the AI is attached later by `PlayerBotMgr`.

## Member Reference

**PlayerBotEntry#2**  
Default constructor initializing all fields to zero/false/null, with `chance` set to 100.0f (implicitly converted to uint32).

**PlayerBotEntry**  
Parameterized constructor accepting `guid`, `account`, and `chance_`, setting remaining fields to defaults (offline state, no flags, null AI).

---

<!-- machine-true, projected from graph.json -->

## Map — PlayerBotEntry

*Source:* PlayerBotMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| PlayerBotEntry#2 | ctor | — | — | — |
| PlayerBotEntry | ctor | — | — | — |
