# scholomance

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# scholomance.h

**Purpose & Responsibilities**  
This header defines the static identifiers and enumeration constants required for scripting the Scholomance dungeon instance. It contains no executable logic, classes, or methods. Its sole responsibility is to provide a centralized namespace for Game Object (GO) entries, Non-Player Character (NPC) entries, sound IDs, and instance data types associated with Scholomance bosses and mechanics. These constants are referenced by other script units to identify entities, set encounter states, and trigger events.

**Member-by-Member Behavior**  
As a header-only unit containing only `enum` blocks, there are no functions, methods, or variables to document. The content consists entirely of compile-time constants:

1.  **Game Object & NPC Identifiers**: The first `enum` block defines integer constants for specific in-game entities. These are used in corresponding `.cpp` files to cast objects to specific scripts or check entity types.
    *   `GO_*`: Constants for doors, braziers, and gates controlled by various bosses (Kirtonos, Gandling, Malicia, etc.).
    *   `SOUND_SCREECH`: A sound ID used for audio cues.
    *   `NPC_*`: Constants for boss NPCs (Kirtonos, Gandling, Vectus, Marduke) and a special NPC (`NPC_J_EEVEE`).

2.  **Instance Data Types**: The second `enum` block defines indices for the instance data storage system. In MaNGOS-based servers, instances store boss states using integer indices.
    *   `TYPE_*`: Indices for individual boss encounters (Gandling, Theolen, Malicia, Illucia/Barov, Alexei Barov, Polkelt, Ravenian, Kirtonos) and specific mechanics (Viewing Room Door, Dark Reaver).
    *   `INSTANCE_SCHOLOMANCE_MAX_ENCOUNTER`: Defines the upper bound for iteration over encounter states.
    *   `DATA_*`: Additional data slots for Vectus and Marduke, likely used for complex state tracking beyond simple boss kill status.

**Cross-Unit Boundaries**  
This unit has no outgoing calls. It is a passive definition module. It is included by other script units responsible for implementing the actual behavior of Scholomance bosses and game objects. Those units use these constants to:
*   Identify which NPC or GO triggered an event.
*   Set or query the completion state of a boss fight via the instance script interface.
*   Play specific sounds or open/close specific doors.

**Data Model**  
This unit does not interact with any database tables. It contains no SQL queries or data persistence logic.

**Notable Implementation Details**  
*   **Legacy Naming**: The copyright header references "ScriptDev2," indicating this code originated from the ScriptDev2 project, a popular third-party script library for MaNGOS. This suggests the constants may align with older database IDs or script conventions.
*   **Combined Boss State**: `TYPE_ILLUCIABAROV` combines Illucia and Barov into a single type index, suggesting their encounter logic might be handled together or share a state flag, despite being separate NPCs.
*   **No Logic**: There is no executable code here. Any bugs related to Scholomance logic would reside in the `.cpp` files that include this header, not here.

## Member Reference

The MAP for this unit lists no members. Consequently, this section is empty.

---

<!-- machine-true, projected from graph.json -->

## Map — scholomance

*Source:* scholomance.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
