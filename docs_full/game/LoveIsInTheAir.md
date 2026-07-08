# LoveIsInTheAir

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# LoveIsInTheAir

## Purpose & Responsibilities

`LoveIsInTheAir` is a header-only utility unit that provides a single lookup function, `GetLoveIsInTheAirGossipForCreature`. Its sole responsibility is to determine the correct gossip menu ID for non-player characters (NPCs) participating in the "Love is in the Air" seasonal event.

The function acts as a static mapping layer between game entity identifiers (`creatureId`) and content identifiers (gossip menu IDs). It handles two distinct mapping strategies:
1.  **Direct Mapping:** Most NPCs have a fixed gossip menu ID regardless of their specific instance properties.
2.  **Gender-Based Mapping:** A subset of NPCs (specifically guards and certain faction representatives like the Bluffwatcher and Orgrimmar Grunt) have different gossip menus depending on whether the NPC instance is male or female. This allows the event to provide gender-specific dialogue or interactions.

This unit contains no state, no database access, and no complex logic beyond a large `switch` statement. It is designed to be included directly where the gossip menu resolution is needed during spell aura processing.

## Member-by-Member Behavior

### `GetLoveIsInTheAirGossipForCreature`

This inline function takes two parameters:
*   `creatureId` (`uint32`): The unique database identifier of the NPC.
*   `gender` (`uint32`): The gender of the specific NPC instance (typically `GENDER_MALE` or `GENDER_FEMALE`).

It returns a `uint32` representing the gossip menu ID.

**Logic Flow:**
1.  The function enters a `switch` statement on `creatureId`.
2.  **Direct Returns:** For the majority of cases, it immediately returns a hardcoded gossip ID (ranging from 6954 to 7081). These IDs correspond to specific dialogue trees defined in the game's data files for the Valentine's Day event.
3.  **Conditional Returns:** For specific `creatureId`s (68, 1976, 3084, 3296), it checks the `gender` parameter:
    *   If `GENDER_MALE`, it returns one gossip ID.
    *   Otherwise, it returns a different gossip ID.
4.  **Fallback/Error Handling:** If the `creatureId` does not match any known case, the function logs an error via `sLog.Out` indicating an unexpected creature ID attempted to access the gossip menu. It then returns `0`, which typically signifies "no gossip menu" or "invalid menu" in the context of the caller.

**Notable Implementation Details:**
*   **Inline Definition:** The function is defined as `inline` within the header, ensuring zero overhead for inclusion in other translation units.
*   **Hardcoded Data:** All mappings are hardcoded. There is no dynamic loading or configuration. This implies that any changes to which NPCs participate in the event or which menus they use require a code change and recompile.
*   **Error Logging:** The fallback case explicitly logs an error. This is a debugging aid to catch cases where an NPC is incorrectly configured to trigger this spell aura but lacks a corresponding entry in this switch statement.

## Cross-Unit Boundaries

### Called By: `Unit.SpellAuras/HandlePeriodicTriggerSpell`

*   **Direction:** Inbound (Other unit calls this unit).
*   **Collaboration:** The `Unit.SpellAuras` module, specifically within the `HandlePeriodicTriggerSpell` method, invokes `GetLoveIsInTheAirGossipForCreature`.
*   **Context:** This suggests that the "Love is in the Air" event is triggered by a periodic spell aura applied to NPCs. When the aura ticks or triggers, the system needs to determine what gossip menu to present to players interacting with that NPC. The `Unit.SpellAuras` unit likely passes the NPC's `creatureId` and `gender` to this function to resolve the appropriate menu ID before displaying it to the player.
*   **Data Crossing:**
    *   **Input:** `creatureId` and `gender` from the NPC instance handling the spell aura.
    *   **Output:** The resolved gossip menu ID, which is then used by the `Unit.SpellAuras` unit to populate the gossip interface.

### Calls Out: None

This unit does not call any other units. It relies solely on local constants and the logging facility (`sLog`), which is a global singleton typically considered part of the core infrastructure rather than a functional dependency for business logic.

## Data Model

This unit does not interact with any database tables. All data (creature IDs, genders, and gossip menu IDs) is hardcoded within the source code. Therefore, there is no SQL schema or table interaction to document.

## Notable Implementation Details

1.  **Gender Sensitivity:** The function distinguishes between male and female NPCs for four specific creature IDs:
    *   `68` (Stormwind City Guard) / `1976` (Stormwind City Patroller)
    *   `3084` (Bluffwatcher)
    *   `3296` (Orgrimmar Grunt)
    This indicates that the event designers wanted these generic guard-type NPCs to have gender-specific greetings or responses, likely to enhance immersion or provide varied dialogue options.

2.  **Comprehensive Coverage:** The switch statement covers a wide variety of factions (Stormwind, Ironforge, Darnassus, Thunder Bluff, Undercity, Orgrimmar, Gnomeregan Exiles, etc.) and roles (trainers, vendors, guards, quest givers). This suggests the event was designed to be pervasive across major Alliance and Horde hubs.

3.  **Error Detection:** The explicit error log for unknown `creatureId`s is crucial for maintenance. If a new NPC is added to the event via spell aura assignment but forgotten in this switch statement, the server logs will immediately flag the discrepancy, preventing silent failures where an NPC appears to have no gossip menu.

4.  **Return Value Semantics:** Returning `0` for unknown IDs is a standard convention in many game engines to indicate "null" or "none." The caller (`Unit.SpellAuras`) must handle this `0` appropriately, likely by not opening a gossip window or falling back to default behavior.

## Member Reference

**GetLoveIsInTheAirGossipForCreature**: An inline function that maps a `creatureId` and `gender` to a specific gossip menu ID for the "Love is in the Air" event. It uses a large `switch` statement to return hardcoded IDs. For most creatures, the ID is fixed. For guards and specific faction representatives (IDs 68, 1976, 3084, 3296), the ID depends on the NPC's gender. If the `creatureId` is unrecognized, it logs an error and returns `0`. This function is called by `Unit.SpellAuras/HandlePeriodicTriggerSpell` to resolve gossip menus during periodic spell aura triggers.

---

<!-- machine-true, projected from graph.json -->

## Map — LoveIsInTheAir

*Source:* LoveIsInTheAir.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GetLoveIsInTheAirGossipForCreature | function | — | Unit.SpellAuras/HandlePeriodicTriggerSpell | — |
