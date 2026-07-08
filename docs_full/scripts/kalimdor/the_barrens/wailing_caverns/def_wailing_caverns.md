# def_wailing_caverns

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# def_wailing_caverns

**Purpose & Responsibilities**

`def_wailing_caverns` is a header-only definition unit that provides shared constants for the "Wailing Caverns" dungeon instance scripts within the WoWVMaNGOS server. It does not contain executable logic, classes, or functions. Its sole responsibility is to define an enumeration block containing identifiers used by other script units to manage encounter states, game objects, gossip options, and quest data.

By centralizing these magic numbers into a single header, the unit ensures consistency across multiple script files that handle different bosses or events within the same dungeon. It acts as a contract for the numeric values representing specific game entities and states.

**Data Model**

This unit interacts with no database tables. It contains no SQL queries or data access logic.

**Notable Implementation Details**

*   **Header-Only Design:** The file uses include guards (`#ifndef DEF_WAILING_CAVERNS_H`) but defines no classes or functions. It is purely a namespace for integer constants via an anonymous `enum`.
*   **Encounter Indexing:** The first six entries (`TYPE_ANACONDRA` through `TYPE_MUTANUS`) correspond to the six main bosses of the Wailing Caverns dungeon. They are indexed sequentially from 0 to 5. `WAILING_CAVERNS_MAX_ENCOUNTER` is set to 6, likely used as a bound check or array size for encounter save data.
*   **Non-Boss Data:** `DATA_NARALEX` (value 6) represents Naralex, who is typically a quest giver or minor NPC rather than a primary raid boss encounter, yet is tracked in the same enum space, possibly for encounter credit or phase tracking purposes.
*   **Game Object ID:** `GO_DMF_CHEST` (180055) identifies a specific chest game object, likely associated with the "Disciple of the Sea" or "Mutanus the Devourer" encounters.
*   **Gossip and Quest IDs:** The unit defines specific gossip option IDs (`GOSSIP_DISCIPLE_SPECIAL`) and quest IDs (`QUEST_FORTUNE_AWAITS`) used in interaction scripts.
*   **Sound/Yell IDs:** `YELL_AFTER_GOSSIP` and `SERPENTIS_YELL` are integer IDs corresponding to sound entries in the game's database, used to trigger specific audio cues during scripted events.

**Cross-Unit Boundaries**

As a header-only definition file, `def_wailing_caverns` does not call into other units. It is included by other script units (such as boss AI implementations or instance scripts) which then use these constants. The MAP indicates no outgoing or incoming calls because there is no executable code to call.

## Member Reference

The MAP for this unit lists no members. Consequently, this section is empty.

---

<!-- machine-true, projected from graph.json -->

## Map — def_wailing_caverns

*Source:* def_wailing_caverns.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
