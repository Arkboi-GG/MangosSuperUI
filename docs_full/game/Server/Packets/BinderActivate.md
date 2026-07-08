# BinderActivate

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# BinderActivate

**BinderActivate** is a client-side network packet class within the `WorldPackets::Npc` namespace, responsible for representing the `CMSG_BINDER_ACTIVATE` message sent by the game client to the server. Its sole responsibility is to encapsulate the data payload associated with a player interacting with a "binder" NPC (typically used for binding a respawn point or hearthstone location). The class inherits from `ClientPacket`, indicating it originates from the client and requires deserialization from the raw network buffer before its fields can be accessed by server-side logic.

This unit contains only the declaration of the class structure and its constructor. It does not contain implementation logic for reading the packet data; that behavior is defined in the corresponding `.cpp` file (not provided here, but implied by the `override` specifier on `ReadFromWorldPacket`). The class holds a single public member, `npcGuid`, which stores the unique identifier of the NPC entity being activated.

## Member Reference

**BinderActivate**
The default constructor for the `BinderActivate` packet. It explicitly initializes the base class `ClientPacket` with the opcode `CMSG_BINDER_ACTIVATE`. This registration ensures that when the server receives a packet with this specific opcode, it is correctly routed to instances of this class for processing. The constructor does not initialize the `npcGuid` member, leaving it to be populated later by the `ReadFromWorldPacket` method during the deserialization phase.

---

<!-- machine-true, projected from graph.json -->

## Map — BinderActivate

*Source:* Npc.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| BinderActivate | ctor | — | — | — |
