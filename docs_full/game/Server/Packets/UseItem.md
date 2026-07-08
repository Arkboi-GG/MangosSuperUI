# UseItem

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# UseItem (WorldPackets::Spell)

## Purpose & Responsibilities

`UseItem` is a client-side packet structure within the `WorldPackets::Spell` namespace, designed to represent the `CMSG_USE_ITEM` message sent from a game client to the server. Its primary responsibility is to define the data layout for requests to use an item equipped with a spell effect (such as a wand, trinket, or consumable that triggers a specific spell slot). It inherits from `ClientPacket`, establishing it as an inbound message handler component in the network layer.

The class encapsulates four key pieces of information required by the server to process the item usage:
1.  **Bag Index**: The container holding the item.
2.  **Slot**: The specific position of the item within that bag.
3.  **Spell Slot**: The index of the spell ID within the item's template definition (items can have multiple spell effects).
4.  **Targets**: The casting targets for the spell triggered by the item.

As a `final` class, it cannot be subclassed, ensuring a fixed contract for this specific packet type.

## Member-by-Member Behavior

### Constructor (`UseItem`)
The constructor initializes the packet instance. It performs two critical setup tasks:
1.  **Base Initialization**: It calls the `ClientPacket` constructor with the constant `CMSG_USE_ITEM`. This registers the packet type with the network dispatcher, allowing the server to route incoming binary data to this specific class for parsing.
2.  **Member Initialization**: It explicitly initializes the data members `bagIndex`, `slot`, and `spellSlot` to `0`. This ensures that if the packet parsing fails or fields are missing, these values default to a safe zero state rather than containing garbage memory. The `targets` member relies on its default constructor.

## Cross-Unit Boundaries

*   **Calls Out**: None. The constructor contains no logic that invokes other units.
*   **Called By**: None listed in the map. In practice, this constructor is invoked by the network deserialization framework when a `CMSG_USE_ITEM` opcode is detected on the wire. The framework instantiates this object and then calls `ReadFromWorldPacket` (declared in the header but not part of this specific unit's map) to populate the fields.

## Data Model

This unit does not interact directly with any database tables. It operates purely on runtime network data structures.

## Notable Implementation Details

*   **Final Class**: The class is marked `final`, preventing inheritance. This is consistent with packet classes which typically have a fixed binary format defined by the game protocol.
*   **Explicit Default Values**: Unlike some other packet classes in the same header (e.g., `CastSpell` uses in-class initializers `= 0`), `UseItem` uses the member initializer list in the constructor. This is functionally equivalent but stylistically distinct within the file.
*   **Spell Slot Semantics**: The presence of `spellSlot` distinguishes `UseItem` from generic item usage. It implies the item has multiple potential spell effects (defined in the item template), and the client specifies *which* one to activate. This is common for items like wands (where the spell might change based on ammo) or multi-effect trinkets.
*   **Namespace Structure**: It resides in `WorldPackets::Spell`, grouping it logically with other spell-related network messages, even though it originates from an item interaction.

## Member Reference

**UseItem**
Constructor for the `UseItem` packet. Initializes the base `ClientPacket` with the opcode `CMSG_USE_ITEM` and sets `bagIndex`, `slot`, and `spellSlot` to `0`.

---

<!-- machine-true, projected from graph.json -->

## Map — UseItem

*Source:* Spell.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| UseItem | ctor | — | — | — |
