# TrainerBuySpell

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# TrainerBuySpell

**TrainerBuySpell** is a client-side network packet structure within the `WorldPackets::Npc` namespace, defined in `Npc.h`. It represents the `CMSG_TRAINER_BUY_SPELL` message sent by the game client to the server when a player attempts to purchase a spell from an NPC trainer.

## Purpose & Responsibilities

The primary responsibility of `TrainerBuySpell` is to encapsulate the raw data required to identify a specific spell purchase request. It acts as a data carrier for two critical pieces of information:
1.  **Target Identification**: The `ObjectGuid` of the NPC trainer from whom the spell is being purchased.
2.  **Spell Identification**: The `uint32` ID of the specific spell the player wishes to learn.

As a subclass of `ClientPacket`, it inherits the mechanism for associating itself with the specific opcode `CMSG_TRAINER_BUY_SPELL` and provides the interface (`ReadFromWorldPacket`) necessary to deserialize binary network data into these structured fields. It contains no business logic; its sole role is data representation and deserialization preparation.

## Member-by-Member Behavior

### Constructor: `TrainerBuySpell()`
The default constructor initializes the packet instance. It performs two key actions:
1.  Calls the base class constructor `ClientPacket(CMSG_TRAINER_BUY_SPELL)`, registering this packet type with the correct network opcode.
2.  Initializes the member variables:
    *   `guid`: Default-constructed `ObjectGuid` (typically empty/null until populated).
    *   `spellId`: Initialized to `0`.

This ensures that every instance of `TrainerBuySpell` is correctly typed for the network layer before any data is read into it.

## Cross-Unit Boundaries

*   **Calls Out**: None. The MAP indicates no outgoing calls to other units. The class is a pure data structure with a deserialization interface.
*   **Called By**: None listed in the MAP. In practice, this packet is instantiated and filled by the network subsystem (likely `WorldSession` or a packet handler dispatcher) when a `CMSG_TRAINER_BUY_SPELL` opcode is detected on the wire. The caller then passes the populated `TrainerBuySpell` object to the game world logic (e.g., `Player::LearnSpell` or `Trainer` handlers) to execute the transaction.

## Data Model

This unit does not interact directly with any database tables. It operates entirely on transient network data. Any persistence related to the spell purchase (updating character data, deducting gold) occurs in downstream units after this packet has been processed.

## Notable Implementation Details

*   **Default Initialization**: The `spellId` is explicitly initialized to `0` in the class definition. This is a safety measure to ensure that if `ReadFromWorldPacket` fails or is not called, the spell ID does not contain garbage memory. However, valid spell IDs in WoW are typically non-zero, so a `0` value likely indicates an invalid or uninitialized packet state if encountered during processing.
*   **Namespace Isolation**: The class resides in `WorldPackets::Npc`, clearly segregating NPC-related network messages from other packet types (like combat or chat).
*   **Final Class**: The class is marked `final`, preventing inheritance. This enforces that `TrainerBuySpell` is a leaf node in the packet hierarchy, ensuring no derived classes alter its network structure or size.

## Member Reference

**TrainerBuySpell**
The default constructor for the `TrainerBuySpell` packet. It initializes the base `ClientPacket` with the opcode `CMSG_TRAINER_BUY_SPELL` and sets the `spellId` member to `0`. The `guid` member is default-constructed. This prepares the object to receive network data.

---

<!-- machine-true, projected from graph.json -->

## Map — TrainerBuySpell

*Source:* Npc.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| TrainerBuySpell | ctor | — | — | — |
