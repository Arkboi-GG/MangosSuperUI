<!-- provenance: verbose -->
# ScriptedGossip

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ScriptedGossip

**ScriptedGossip** is a header-only utility module providing a standardized interface for NPC gossip interactions. It contains no executable logic or class definitions; instead, it exposes preprocessor macros, enumerations, and a single external function declaration to simplify scripted NPC behaviors. Its primary responsibility is to abstract low-level server-client communication mechanisms—such as sending gossip menus, vendor lists, or trainer data—into concise macros that script authors embed directly into NPC AI scripts.

The module acts as a bridge between high-level script logic and underlying `Player`, `Session`, and `GossipMenu` objects. By centralizing these definitions, it ensures consistency across NPC scripts regarding how gossip actions are triggered, menus are constructed, and specific game features (training, vending, taxi) are invoked.

## Purpose & Responsibilities

The core purpose of `ScriptedGossip.h` is to reduce boilerplate in NPC scripts. Interacting with a player via gossip typically involves retrieving the player's talk class, accessing the gossip menu object, adding items with specific icons/text, and sending the menu to the client. `ScriptedGossip` encapsulates these steps into single-line macros.

Key responsibilities include:
1.  **Standardizing Gossip Actions:** Defining integer constants for common NPC roles (e.g., `GOSSIP_ACTION_TRAIN`, `GOSSIP_ACTION_VENDOR`) for uniform action checking.
2.  **Abstracting Client Communication:** Providing macros like `SEND_GOSSIP_MENU` and `ADD_GOSSIP_ITEM` that hide the complexity of accessing `PlayerTalkClass` and `GossipMenu` objects.
3.  **Defining Skill Constants:** Establishing fixed mappings for trade skills (Alchemy, Blacksmithing, etc.) and proficiency levels (Apprentice, Journeyman, etc.) for trainer scripts.
4.  **Declaring External Utilities:** Exposing the `GetSkillLevel` function, implemented elsewhere, for querying player skill proficiency.

## Member-by-Member Behavior

### `GetSkillLevel`

**Kind:** Declaration (External Function)

**Behavior:**
This is a declaration for the external function `GetSkillLevel`. The implementation resides in another unit. It accepts two arguments:
1.  `Player* pPlayer`: Pointer to the player object.
2.  `uint32 skill`: Integer identifier for the trade skill (using `TRADESKILL_*` constants).

It returns a `uint32` representing the player's current level in that skill. Scripts use this to determine training eligibility or display appropriate gossip options.

**Cross-Unit Boundaries:**
*   **Called by:** Various NPC script units (external to this definition).
*   **Calls out:** None (declaration only). The implementation likely calls into `Player` methods.

## Data Model

This unit does not interact directly with any database tables. It operates entirely on in-memory objects (`Player`, `Session`, `GossipMenu`) and static constants. No SQL queries are present.

## Notable Implementation Details

### Macro-Based Abstraction
The utility relies on C preprocessor macros, allowing scripts to write direct-command-style code (e.g., `ADD_GOSSIP_ITEM(...)`). This introduces dependencies on specific scope variables:
*   `PlayerTalkClass`: Most macros assume this variable exists in the calling scope, typically set by the script framework.
*   `GetSession()`: Macros like `SEND_VENDORLIST` rely on `GetSession()` being available to return the current player's session.

### Enumerations for Game Logic
The header defines critical enumerations:
1.  **Trade Skills (`TRADESKILL_*`):** Maps skill IDs (1–13) to names (Alchemy, Blacksmithing, etc.).
2.  **Skill Levels (`TRADESKILL_LEVEL_*`):** Maps proficiency ranks (0–5) to names (None, Apprentice, Master).
3.  **Gossip Actions (`GOSSIP_ACTION_*`):** Standard action codes (e.g., `GOSSIP_ACTION_TRAIN` = 2, `GOSSIP_ACTION_VENDOR` = 1).
4.  **Gossip Senders (`GOSSIP_SENDER_*`):** Identifies NPC/menu contexts (e.g., `GOSSIP_SENDER_SEC_PROFTRAIN` = 4).

### Hardcoded Text IDs
Two gossip text IDs are defined:
*   `GOSSIP_TEXT_BROWSE_GOODS` (3370): Likely for vendor NPCs.
*   `GOSSIP_TEXT_TRAIN` (3266): Likely for trainer NPCs.
These reference game localization tables.

### Error Handling and Scope Assumptions
Macros expand directly into method calls on `PlayerTalkClass` and `GetSession()`. There is no internal error handling; if `PlayerTalkClass` is null or `GetSession()` fails, the result is undefined behavior or crashes. Validation is the responsibility of the script author or framework.

## Member Reference

**GetSkillLevel**
Declaration of an external function that retrieves a player's current level in a specified trade skill. Takes a `Player*` and a `uint32` skill ID, returning a `uint32` skill level. Implemented in another unit.

---

<!-- machine-true, projected from graph.json -->

## Map — ScriptedGossip

*Source:* ScriptedGossip.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GetSkillLevel | decl | — | — | — |
