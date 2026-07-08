# DelIgnore

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# DelIgnore

**DelIgnore** is a client-to-server packet structure within the `WorldPackets::Misc` namespace, responsible for carrying the data associated with the `CMSG_DEL_IGNORE` opcode. Its sole responsibility is to define the payload layout for a request from a client to remove a specific entity (identified by an `ObjectGuid`) from the player's ignore list.

As a `ClientPacket`, it inherits the standard packet handling infrastructure but contains no custom logic beyond its constructor initialization. It does not perform validation, database interaction, or server-side processing; those responsibilities lie with the handler that instantiates and reads this packet.

## Member Reference

**DelIgnore**
The default constructor for the `DelIgnore` packet. It initializes the base `ClientPacket` with the opcode `CMSG_DEL_IGNORE`. The member variable `ignoreGuid` is implicitly default-initialized to an empty `ObjectGuid` by the compiler, as no initializer is provided in the constructor body or member initializer list. This member is populated later by the inherited `ReadFromWorldPacket` method when the packet is received from the network.

---

<!-- machine-true, projected from graph.json -->

## Map — DelIgnore

*Source:* Misc.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| DelIgnore | ctor | — | — | — |
