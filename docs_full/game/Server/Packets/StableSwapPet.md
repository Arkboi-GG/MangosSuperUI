# StableSwapPet

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# StableSwapPet

**StableSwapPet** is a client-to-server network packet structure within the `WorldPackets::Npc` namespace. It represents the `CMSG_STABLE_SWAP_PET` message sent by the World of Warcraft client when a player interacts with a stable master NPC to swap a currently active pet with one stored in their stable slots.

This unit defines the data contract for this specific interaction. It inherits from `ClientPacket`, indicating it originates from the client and is processed by the server. The structure carries two critical pieces of information required to execute the swap: the identifier of the Non-Player Character (NPC) initiating the transaction (`npcGuid`) and the index number of the pet within the player's stable that should be swapped out or brought in (`petNumber`).

As a packet definition, **StableSwapPet** contains no business logic, database queries, or cross-unit calls. Its sole responsibility is to define the memory layout and serialization interface for this specific game message. The actual processing of the swap—validating ownership, checking stable slot availability, updating the player's active pet, and persisting changes—is handled by other units that receive an instance of this packet after it has been deserialized.

## Member Reference

**StableSwapPet**
Constructor for the `StableSwapPet` packet. It initializes the base `ClientPacket` with the opcode `CMSG_STABLE_SWAP_PET`. It sets the default value of `petNumber` to `0` and leaves `npcGuid` in its default constructed state (empty/null GUID). This constructor is called during the packet deserialization process when the server receives the corresponding network message.

---

<!-- machine-true, projected from graph.json -->

## Map — StableSwapPet

*Source:* Npc.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| StableSwapPet | ctor | — | — | — |
