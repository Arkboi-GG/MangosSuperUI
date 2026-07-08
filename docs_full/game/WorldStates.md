<!-- provenance: verbose -->
# WorldStates

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# WorldStates

## Purpose & Responsibilities

`WorldStates` is a header-only utility defining the contract for serializing **World State** updates to game clients. World States are global or zone-specific variables broadcast to players to update UI elements, map icons, and quest progress indicators.

This unit provides two responsibilities:
1.  **Enum Definition:** The `WorldStates` enumeration maps human-readable identifiers (e.g., `WS_WE_ALLIANCE_COPPERBAR_CURRENT`) to numeric IDs required by the client protocol.
2.  **Serialization Helpers:** Two inline functions, `WriteInitialWorldStatePair` and `WriteUpdateWorldStatePair`, serialize a state ID and value into a `ByteBuffer`. These functions adapt their serialization format based on `SUPPORTED_CLIENT_BUILD`, ensuring compatibility between Vanilla (1.x) and TBC+ (2.0+) client protocols.

The unit does not manage state storage or logic; it purely facilitates transmission of state data from server memory to the network buffer.

## Member-by-Member Behavior

### Serialization Functions

#### `WriteInitialWorldStatePair`
Serializes a world state pair for **initialization** packets, used when a player logs in or enters a zone to sync the client's UI.

*   **Behavior:** Appends `state` ID and `value` to the `ByteBuffer`.
*   **Version Adaptation:**
    *   `SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_10_2`: Serializes `state` as `uint32` and `value` as `int32`.
    *   Otherwise: Serializes `state` as `uint16` and `value` as `int16`.
*   **Callers:** `OutdoorPvPEP::FillInitialWorldStates` (variants #1-5), `OutdoorPvPSI::FillInitialWorldStates`, `Player.Main::SendInitWorldStates`, `world_event_wareffort::BuildWarEffortWorldStates`.

#### `WriteUpdateWorldStatePair`
Serializes a world state pair for **dynamic update** packets, used when a state changes (e.g., tower ownership, resource counts) and needs pushing to clients.

*   **Behavior:** Appends `state` ID and `value` to the `ByteBuffer`.
*   **Version Adaptation:**
    *   `SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_8_4`: Serializes `state` as `uint32` and `value` as `int32`.
    *   Otherwise: Serializes `state` as `uint16` and `value` as `int16`.
*   **Callers:** `BattleGroundMgr::BuildUpdateWorldStatePacket`, `Player.Main::SendUpdateWorldState`.

### Enumerations (`WorldStates`)

The `WorldStates` enum defines numeric IDs for game features, grouped logically in source:

1.  **Ahn'Qiraj War Effort:** IDs for resource contributions (copper bars, herbs, leather, etc.) for Alliance/Horde, including shared requirements and faction-specific totals. Includes `WS_WE_TRANSITION_DAYS_REMAINING`.
2.  **Scourge Invasion:** IDs for invasion status in zones (Winterspring, Azshara, etc.), battles won, and remaining necropolises per zone.
3.  **Silithus Outdoor PvP:** IDs for gathered resources and Silithyst levels.
4.  **Eastern Plaguelands Outdoor PvP:** IDs for tower statuses (Eastwall, Northpass, Plaguewood, Crown Guard) for Alliance/Horde. Includes states for "Controlled," "Contested," "Neutral," and "Progressing," plus UI slider positions and tower counts.

## Cross-Unit Boundaries

This unit is passive; its members are exclusively **called by** other units constructing network packets.

*   **OutdoorPvP Modules (`OutdoorPvPEP`, `OutdoorPvPSI`):** Call `WriteInitialWorldStatePair` to pack calculated event states (e.g., tower ownership) into buffers for players entering the zone.
*   **Player Initialization (`Player.Main`):** Calls `WriteInitialWorldStatePair` during login/zone change for baseline sync, and `WriteUpdateWorldStatePair` for specific global state deltas.
*   **Battle Ground Manager (`BattleGroundMgr`):** Calls `WriteUpdateWorldStatePair` to append changed states to update packets.
*   **War Effort Event (`world_event_wareffort`):** Calls `WriteInitialWorldStatePair` to ensure clients display correct war effort progress.

## Data Model

This unit does not interact with any database tables. It operates entirely on in-memory data passed by callers. Persistence is handled by calling units.

## Notable Implementation Details

1.  **Client Build Versioning:** Conditional compilation (`#if SUPPORTED_CLIENT_BUILD > ...`) changes serialized integer sizes.
    *   **Vanilla (1.x):** `uint16` state ID, `int16` value. Limits values to ±32,767.
    *   **TBC+ (2.0+):** `uint32` state ID, `int32` value.
    *   **Risk:** Callers must ensure values fit `int16` for Vanilla clients. Resource counts exceeding 32,767 will overflow/truncate. Enum IDs are all within `uint16` range.
2.  **Inline Functions:** Both helpers are `inline` to optimize frequent packet construction calls.
3.  **Known Issues:** Source comments note `WS_PLAGUEWOOD_TOWER_HORDE_CONTESTED` (ID 2367) does not work client-side.
4.  **No Error Handling:** Functions assume valid `ByteBuffer` capacity and valid inputs; no overflow checks are performed.

## Member Reference

**WriteInitialWorldStatePair**
Serializes a world state ID and value into a `ByteBuffer` for initial client synchronization. Adapts integer sizes (`uint16/int16` vs `uint32/int32`) based on `SUPPORTED_CLIENT_BUILD`. Called by `OutdoorPvPEP`, `OutdoorPvPSI`, `Player.Main`, and `world_event_wareffort`.

**WriteUpdateWorldStatePair**
Serializes a world state ID and value into a `ByteBuffer` for dynamic client updates. Adapts integer sizes (`uint16/int16` vs `uint32/int32`) based on `SUPPORTED_CLIENT_BUILD`. Called by `BattleGroundMgr` and `Player.Main`.

---

<!-- machine-true, projected from graph.json -->

## Map — WorldStates

*Source:* WorldStates.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| WriteInitialWorldStatePair | function | — | OutdoorPvPEP/FillInitialWorldStates, OutdoorPvPEP/FillInitialWorldStates#2, OutdoorPvPEP/FillInitialWorldStates#3, OutdoorPvPEP/FillInitialWorldStates#4, OutdoorPvPEP/FillInitialWorldStates#5, OutdoorPvPSI/FillInitialWorldStates, Player.Main/SendInitWorldStates, world_event_wareffort/BuildWarEffortWorldStates | — |
| WriteUpdateWorldStatePair | function | — | BattleGroundMgr/BuildUpdateWorldStatePacket, Player.Main/SendUpdateWorldState | — |
