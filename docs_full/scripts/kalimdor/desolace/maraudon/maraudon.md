<!-- provenance: failed-members -->
# maraudon

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Maraudon Encounter Enums (`maraudon.h`)

## Purpose & Responsibilities

`maraudon.h` is a lightweight header file that defines a set of integer constants used to identify specific encounter states and entities within the **Maraudon** dungeon instance. It serves as a central registry for IDs related to two specific mechanics: the **Larva Spewer** encounter and the **Celebras** encounter.

The file does not contain executable logic, classes, or functions. Its sole responsibility is to provide symbolic names for:
1.  **Encounter Types:** Integer indices used likely by the instance script to track boss kill status or phase progression.
2.  **NPC IDs:** Identifiers for specific creature templates involved in these encounters.
3.  **Game Object (GO) IDs:** Identifiers for static or dynamic objects (vines, spewers) used in the visual and mechanical representation of these encounters.

This header is included by other scripts (likely `instance_maraudon.cpp` or specific boss AI files) to avoid hardcoding numeric IDs throughout the codebase, improving maintainability and readability.

## Data Model

This unit interacts with no database tables. It contains no SQL queries or data persistence logic.

## Notable Implementation Details

*   **Enum Structure:** The file uses an unnamed `enum` block. This places the constants directly into the global namespace (or the namespace of the including file, depending on context, though typically global in older Mangos-style scripts). This allows direct usage of identifiers like `TYPE_LARVA_SPEWER` without prefixing.
*   **Encounter Indexing:** The constants `TYPE_LARVA_SPEWER` (0) and `TYPE_CELEBRAS` (1) suggest an array-based approach to managing encounter data in the instance script, where `MARAUDON_MAX_ENCOUNTER` (2) defines the size of that array.
*   **Specific Entity IDs:**
    *   `NPC_SPEWED_LARVA` (13533) and `NPC_CELEBRAS_REDEEMED` (13716) are specific creature entries. The latter implies a transformation mechanic where Celebras changes form upon being "redeemed."
    *   `GO_HEALED_CELEBRIAN_VINE` (178904) and `GO_VYLESTEM_VINE` (178905) are game objects associated with the Celebras fight, likely representing environmental hazards or objectives.
    *   `GO_LARVA_SPEWER` (178559) is the game object representing the Larva Spewer boss itself.

## Cross-Unit Boundaries

As a pure header file containing only constants, `maraudon.h` has no runtime calls. However, it is **called by** (included by) other units in the Maraudon instance script suite. These units use the defined constants to:
*   Set encounter states in the instance data store.
*   Summon or despawn specific NPCs and Game Objects using the provided IDs.
*   Identify entities during combat logic.

No specific calling units are listed in the provided MAP, but standard World of Warcraft server architecture dictates that `instance_maraudon.cpp` and boss-specific AI files (e.g., `boss_celebras.cpp`, `boss_larva_spewer.cpp`) would include this header.

## Member Reference

**TYPE_LARVA_SPEWER**
An integer constant with value `0`. Used as an index to represent the Larva Spewer encounter in the instance's encounter tracking system.

**TYPE_CELEBRAS**
An integer constant with value `1`. Used as an index to represent the Celebras encounter in the instance's encounter tracking system.

**MARAUDON_MAX_ENCOUNTER**
An integer constant with value `2`. Defines the total number of tracked encounters in this enum scope, likely used for array sizing or loop bounds in the instance script.

**NPC_SPEWED_LARVA**
An integer constant with value `13533`. Represents the Creature Entry ID for the larvae spawned by the Larva Spewer.

**NPC_CELEBRAS_REDEEMED**
An integer constant with value `13716`. Represents the Creature Entry ID for Celebras after he has been redeemed (transformed).

**GO_HEALED_CELEBRIAN_VINE**
An integer constant with value `178904`. Represents the Game Object Entry ID for vines associated with the healed/redeemed state of the Celebras encounter.

**GO_VYLESTEM_VINE**
An integer constant with value `178905`. Represents the Game Object Entry ID for Vylestem vines, likely part of the environment or mechanics in the Celebras area.

**GO_LARVA_SPEWER**
An integer constant with value `178559`. Represents the Game Object Entry ID for the Larva Spewer boss entity.

---

<!-- machine-true, projected from graph.json -->

## Map — maraudon

*Source:* maraudon.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|

---

<!-- verify: failed-members | invented: GO_HEALED_CELEBRIAN_VINE, GO_LARVA_SPEWER, GO_VYLESTEM_VINE, MARAUDON_MAX_ENCOUNTER, NPC_CELEBRAS_REDEEMED, NPC_SPEWED_LARVA, TYPE_CELEBRAS, TYPE_LARVA_SPEWER -->
