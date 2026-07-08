# CreatureLinkingHolder

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# CreatureLinkingHolder

**Purpose & Responsibilities**

`CreatureLinkingHolder` manages the dynamic, runtime state of linked Non-Player Characters (NPCs) for a single game world `Map`. While the singleton `CreatureLinkingMgr` stores static configuration defining *which* NPCs are linked and *how*, `CreatureLinkingHolder` tracks the specific runtime instances (GUIDs) of those NPCs on a particular map.

Its responsibilities include:
1.  **Registry Maintenance:** Storing active "master" and "slave" creature instances identified by GUID.
2.  **Event Propagation:** Triggering actions on linked slaves (or masters) when a source creature undergoes lifecycle events (Aggro, Death, Evade, Respawn, Despawn), based on configured flags.
3.  **Conditional Spawning:** Determining if a creature can spawn based on the alive/dead state of its linked master.
4.  **Movement Synchronization:** Initiating follow behavior for slaves linked to masters.

An instance of this class exists for every `Map` object.

## Member-by-Member Behavior

The unit contains only one member, the constructor.

### Initialization

*   **`CreatureLinkingHolder`**: Default constructor. It initializes the object with empty internal storage maps (`m_holderMap`, `m_holderGuidMap`, `m_masterGuid`). It does not perform any loading or validation; data is populated dynamically as creatures are added via `AddSlaveToHolder` and `AddMasterToHolder`.

## Cross-Unit Boundaries

`CreatureLinkingHolder` acts as the runtime executor for linking rules defined elsewhere.

*   **Called by `Creature` / `Unit` (via Hooks)**:
    *   **Direction:** Inbound.
    *   **Why:** Hooks in `Creature.cpp` invoke `CreatureLinkingHolder` methods when a creature enters combat, dies, evades, respawns, or despawns. For example, a dying creature calls `DoCreatureLinkingEvent` to notify its links.
    *   **Data Crossing:** The `Creature` pointer acting as the source, and the `CreatureLinkingEvent` type.

*   **Calls `CreatureLinkingMgr`**:
    *   **Direction:** Outbound.
    *   **Why:** To retrieve static linking rules. Registration methods (`AddSlaveToHolder`, `AddMasterToHolder`) call `CreatureLinkingMgr::GetLinkedTriggerInformation` to determine linkage parameters (flags, search range, target master/slave).
    *   **Data Crossing:** `CreatureLinkingInfo` structures containing map IDs, master/slave IDs, flags, and search ranges.

*   **Calls `Creature` / `Unit` Methods**:
    *   **Direction:** Outbound.
    *   **Why:** To execute actions on linked entities. Helper methods invoke methods on `Creature` objects to change state (e.g., `Attack`, `Kill`, `Evade`, `Despawn`) or movement (`SetFollowing`).
    *   **Data Crossing:** Commands and target `Unit` pointers (e.g., the enemy to aggro).

*   **Calls `Map` Methods**:
    *   **Direction:** Outbound.
    *   **Why:** To query the state of other creatures on the map, particularly for spawn checks (`IsRespawnReady`, `CanSpawn`) which need to verify if a master creature is currently alive or dead.
    *   **Data Crossing:** Creature GUIDs and map context.

## Data Model

This unit does not directly interact with database tables. It consumes in-memory data (`CreatureLinkingInfo`) provided by `CreatureLinkingMgr`.

## Notable Implementation Details

1.  **Dual Storage Maps**: The class maintains `m_holderMap` (keyed by creature entry ID) and `m_holderGuidMap` (keyed by creature GUID). This supports both entry-based linking (all instances of type A link to all instances of type B) and GUID-based linking (specific individuals link to specific individuals).
2.  **Range-Based Activation**: Actions are often constrained by `searchRange`. Helpers like `IsSlaveInRangeOfMaster` ensure slaves only react to masters within a specified distance, preventing global map-wide reactions.
3.  **Flag Bitmask Interpretation**: The `CreatureLinkingFlags` enum defines complex behaviors (e.g., `FLAG_AGGRO_ON_AGGRO` vs `FLAG_TO_AGGRO_ON_AGGRO`). Processing logic must correctly distinguish between master-to-slave and slave-to-master propagation.
4.  **Stale GUID Risk**: The holder stores GUIDs but does not own the `Creature` objects. If a creature despawns without proper cleanup, the holder may retain stale GUIDs. Processing helpers mitigate this by verifying creature existence before acting.

## Member Reference

**CreatureLinkingHolder**
Default constructor. Initializes empty internal maps for storing master-slave relationships and GUID mappings. No database access or initialization logic occurs here.

---

<!-- machine-true, projected from graph.json -->

## Map — CreatureLinkingHolder

*Source:* CreatureLinkingMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| CreatureLinkingHolder | ctor | — | — | — |
