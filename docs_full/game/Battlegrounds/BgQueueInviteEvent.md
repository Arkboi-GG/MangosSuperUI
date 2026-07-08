# BgQueueInviteEvent

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# BgQueueInviteEvent

`BgQueueInviteEvent` is a transient event object within the `wowvmangos` codebase, defined in `BattleGroundMgr.h`. It inherits from `BasicEvent` and serves as a scheduled callback mechanism to re-invite a player to a specific Battleground instance after a delay. Its primary role is to handle the edge case where a player’s initial invitation to a Battleground might fail or be ignored by the client, ensuring the player is eventually placed into the correct instance context.

The class is instantiated by `BattleGroundMgr::InviteGroupToBG` (in the `BattleGroundMgr` unit) when a group is matched and invited to a Battleground. It stores the necessary identifiers—player GUID, Battleground instance GUID, Battleground type ID, and a removal timestamp—to perform its action independently of the original invocation context. This decoupling allows the system to manage invitations asynchronously via the server’s event scheduler.

## Member-by-Member Behavior

### **BgQueueInviteEvent** (Constructor)
The constructor initializes the event with four parameters:
- `playerGuid`: The unique identifier of the player being invited.
- `bgInstanceGuid`: The unique identifier of the Battleground instance they are being invited to.
- `bgTypeId`: The type of Battleground (e.g., Warsong Gulch, Arathi Basin).
- `removeTime`: A timestamp indicating when the invitation should be considered invalid or removed.

These values are stored in private member variables (`m_playerGuid`, `m_bgInstanceGuid`, `m_bgTypeId`, `m_removeTime`) for use during event execution. The constructor does not perform any validation or side effects; it simply prepares the event for scheduling.

### **~BgQueueInviteEvent** (Destructor)
The destructor is virtual and empty. It exists solely to satisfy the requirements of polymorphic deletion through the `BasicEvent` base class interface. No cleanup of resources is performed, as the class holds only primitive types and an `ObjectGuid`.

## Cross-Unit Boundaries

- **Called by `BattleGroundMgr::InviteGroupToBG`**: The `BattleGroundMgr` unit creates instances of `BgQueueInviteEvent` when inviting groups to Battlegrounds. This indicates that the event is part of the broader Battleground queue management and invitation workflow. The `BattleGroundMgr` passes the relevant context (player, instance, type, timing) to the event, which then schedules itself for future execution.
- **No outgoing calls**: The `BgQueueInviteEvent` class does not call into any other units. Its `Execute` and `Abort` methods (declared in the header but defined elsewhere, likely in `BattleGroundMgr.cpp`) are responsible for interacting with the rest of the system, but those interactions are not part of this unit’s direct responsibilities.

## Data Model

This unit does not interact with any database tables. It operates entirely in memory, using identifiers passed at construction time.

## Notable Implementation Details

- **Inheritance from `BasicEvent`**: As a subclass of `BasicEvent`, `BgQueueInviteEvent` integrates with the server’s global event scheduler. This allows the invitation logic to be deferred until a specific time, avoiding blocking operations and enabling asynchronous processing.
- **Statelessness beyond stored IDs**: The class does not maintain references to `Player` or `BattleGround` objects. Instead, it stores only their GUIDs and type IDs. This design choice prevents dangling pointers if the `Player` or `BattleGround` objects are destroyed before the event executes. The actual resolution of these IDs to objects occurs in the `Execute` method (defined outside this unit).
- **Virtual Destructor**: The presence of a virtual destructor ensures safe deletion when the event is managed through a `BasicEvent*` pointer, adhering to standard C++ practices for polymorphic classes.

## Member Reference

**BgQueueInviteEvent**  
Constructor that initializes the event with the player GUID, Battleground instance GUID, Battleground type ID, and removal timestamp. These values are stored in private member variables for use during event execution.

**~BgQueueInviteEvent**  
Virtual destructor that performs no cleanup. Exists to support polymorphic deletion through the `BasicEvent` base class.

---

<!-- machine-true, projected from graph.json -->

## Map — BgQueueInviteEvent

*Source:* BattleGroundMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| BgQueueInviteEvent | ctor | — | BattleGroundMgr/InviteGroupToBG | — |
| ~BgQueueInviteEvent | dtor | — | — | — |
