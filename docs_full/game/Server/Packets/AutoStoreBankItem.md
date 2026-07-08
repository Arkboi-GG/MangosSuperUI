# AutoStoreBankItem

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AutoStoreBankItem

**AutoStoreBankItem** is a client-to-server packet structure within the `WorldPackets::Item` namespace, defined in `Item.h`. It represents the `CMSG_AUTOSTORE_BANK_ITEM` message, which a client sends to request that a specific item from its inventory be automatically stored into the player's bank.

The class encapsulates two fields identifying the source location of the item:
*   `srcbag`: The bag index containing the item.
*   `srcslot`: The slot index within that bag.

Like other packet classes in this header, `AutoStoreBankItem` inherits from `ClientPacket` and provides a default constructor that initializes the packet type and zero-initializes the data members. It declares a pure virtual interface requirement (`ReadFromWorldPacket`) for deserializing the binary data from the network stream, though the implementation of that deserialization is not part of this unit.

This unit has no dependencies on other code units, performs no database operations, and contains no executable logic beyond its declaration. It serves purely as a data contract for the network layer.

## Member Reference

**AutoStoreBankItem**
Constructor for the `AutoStoreBankItem` packet. Initializes the base `ClientPacket` with the opcode `CMSG_AUTOSTORE_BANK_ITEM` and sets both `srcbag` and `srcslot` to `0`.

---

<!-- machine-true, projected from graph.json -->

## Map — AutoStoreBankItem

*Source:* Item.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AutoStoreBankItem | ctor | — | — | — |
