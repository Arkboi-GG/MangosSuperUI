# BGQueueRemoveEvent

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# BGQueueRemoveEvent

**Purpose & Responsibilities**

`BGQueueRemoveEvent` is a transient event object within the `wowvmangos` server framework, designed to manage the lifecycle of a player's invitation to a BattleGround (BG). Specifically, it acts as a scheduled cleanup mechanism. When a player is invited to a BattleGround instance, the server schedules this event to fire after a specific delay (typically 1 minute and 20 seconds, as noted in the class comment). Its primary responsibility is to ensure that if a player remains in the queue beyond this timeout—either because they did not accept the invitation, disconnected, or were otherwise stuck—their invitation state is forcibly removed. This prevents stale invitations from blocking queue slots or confusing the client.

The class inherits from `BasicEvent`, integrating it into the server's global event scheduler. It stores the necessary context (player GUID, BG instance ID, BG type, and queue type) to identify and clean up the specific invitation record when executed.

**Member-by-Member Behavior**

The unit consists of two members: the constructor and the destructor. Both are minimal scaffolding required for the event system.

*   **Construction (`BGQueueRemoveEvent`)**: Initializes the event with the specific identifiers needed to locate and remove the player's queue entry later. It captures the `ObjectGuid` of the player, the `uint32` instance GUID of the BattleGround, the `BattleGroundTypeId`, the `BattleGroundQueueTypeId`, and the `removeTime` (likely the timestamp or duration until removal). These values are stored in private member variables.
*   **Destruction (`~BGQueueRemoveEvent`)**: A virtual destructor that performs no custom cleanup logic. It relies on the default behavior to destroy the object once the event system has finished processing it.

**Cross-Unit Boundaries**

This unit is tightly coupled with the `BattleGroundMgr` singleton, which acts as the central coordinator for all BattleGround operations.

*   **Called By**:
    *   `BattleGroundMgr::InviteGroupToBG`: When a group is successfully matched and invited to a BattleGround, this method likely creates and schedules a `BGQueueRemoveEvent` for each player in the group. This ensures that if the invitation expires or is ignored, the queue state is cleaned up.
    *   `BattleGroundMgr::PlayerLoggedIn`: When a player logs in, the manager checks for pending queue states. If a player had an active invitation before logging out (or if there's a race condition upon login), a `BGQueueRemoveEvent` might be scheduled or re-evaluated to ensure the player's queue status is consistent with their current online state.

*   **Calls Out**:
    *   The MAP indicates no direct calls out to other units from the constructor or destructor. However, the *execution* of this event (handled by the inherited `Execute` method, which is not part of this specific partial/unit definition but is implied by the class structure) would logically call back into `BattleGroundMgr` or `BattleGroundQueue` methods to perform the actual removal. Since `Execute` and `Abort` are declared in the header but not defined in this source snippet, and the MAP only lists the ctor/dtor for this unit, we strictly document the ctor/dtor behavior. The *scheduling* of this event is the key interaction point with `BattleGroundMgr`.

**Data Model**

This unit does not interact directly with any database tables. It operates entirely on in-memory state managed by the `BattleGroundMgr` and `BattleGroundQueue` classes. The `removeTime` parameter is an in-memory timestamp or duration value, not a persisted database column.

**Notable Implementation Details**

*   **Event Lifecycle**: As a subclass of `BasicEvent`, `BGQueueRemoveEvent` is designed to be created, scheduled, executed once, and destroyed. The lack of complex logic in the constructor/destructor emphasizes its role as a simple data carrier for the event scheduler.
*   **Timeout Logic**: The class comment explicitly states the purpose: "remove player from BG queue after 1 minute 20 seconds from first invitation." This specific duration is a game-design choice to balance giving players enough time to accept an invitation while preventing queue stagnation.
*   **State Preservation**: The event stores `m_bgQueueTypeId` because the `BattleGround` instance itself might be deleted or recycled by the time the event fires. Storing the queue type allows the system to identify the correct queue context even if the original `BattleGround` object is no longer valid or accessible via the instance ID alone.
*   **Re-invitation Handling**: The comment notes, "We must store removeInvite time in case player left queue and joined and is invited again." This suggests that the event system must handle overlapping or sequential invitations gracefully. The `removeTime` stored in the event likely serves as a unique identifier or a check to ensure the correct invitation is being cancelled, preventing accidental cancellation of a newer, valid invitation if the player rejoins the queue quickly.

## Member Reference

**BGQueueRemoveEvent** (ctor): Initializes the event object with the player's GUID, the BattleGround instance GUID, the BattleGround type ID, the BattleGround queue type ID, and the removal time. These values are stored in private member variables to be used when the event is eventually executed by the scheduler.

**~BGQueueRemoveEvent** (dtor): Virtual destructor that performs no custom cleanup. It allows the event system to safely delete the object after execution.

---

<!-- machine-true, projected from graph.json -->

## Map — BGQueueRemoveEvent

*Source:* BattleGroundMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| BGQueueRemoveEvent | ctor | — | BattleGroundMgr/InviteGroupToBG, BattleGroundMgr/PlayerLoggedIn | — |
| ~BGQueueRemoveEvent | dtor | — | — | — |
