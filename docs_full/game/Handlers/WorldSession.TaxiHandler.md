<!-- provenance: boundary-bleed -->
# WorldSession.TaxiHandler

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# WorldSession.TaxiHandler

## Purpose & Responsibilities

The `WorldSession.TaxiHandler` partial implements the server-side network interface for the **Taxi (Flight Path)** system within the `wowvmangos` emulator. It handles incoming client packets related to flight masters, validates interactions, manages the discovery of new flight nodes, and triggers the visual and movement components of taxi flights.

Its primary responsibilities are:
1.  **Packet Handling:** Receiving and parsing client requests to query flight node status, open flight menus, and initiate flights.
2.  **Node Discovery:** Determining if a player is interacting with a valid flight master and automatically learning new nodes if they are unknown.
3.  **Menu Generation:** Constructing the list of available destinations and costs to send back to the client.
4.  **Flight Initiation:** Triggering the actual movement sequence, including mounting the player and handing off control to the motion master subsystem.

This unit does not store persistent data itself; it relies on the `Player` class (specifically its `PlayerTaxi` subsystem) for node knowledge and cost calculations, and on `ObjectMgr` for spatial lookups of taxi nodes. It contains no direct database queries.

## Member-by-Member Behavior

### Node Status & Discovery

**`HandleTaxiNodeStatusQueryOpcode`**
This is a thin wrapper that receives a `TaxiNodeStatusQuery` packet from the client. It immediately delegates to `SendTaxiStatus`, passing the creature GUID contained in the packet. This opcode is typically sent by the client when hovering over a potential flight master to check if it is a valid, known node.

**`SendTaxiStatus`**
Validates whether a specific creature represents a known taxi node for the player.
1.  Retrieves the `Creature` object from the map using the provided GUID via `WorldSession.Main/GetPlayer` and `Map.Main/GetCreature`. If the creature is not found or inaccessible, it logs a debug message via `Log.Main/Out` and returns.
2.  Uses `ObjectMgr/GetNearestTaxiNode` to find the taxi node ID corresponding to the creature's position (`WorldObject.Object/GetPositionX/Y/Z`) and map ID (`WorldObject.Object/GetMapId`), filtered by the player's team (`Player.Main/GetTeam`).
3.  If no node is found (`curloc == 0`), it returns silently.
4.  Constructs an `SMSG_TAXINODE_STATUS` packet. It sets the status byte to `1` if the player knows the node (`PlayerTaxi/IsTaximaskNodeKnown`), otherwise `0`.
5.  Sends the packet via `WorldSession.Main/SendPacket`.

**`HandleTaxiQueryAvailableNodes`**
Handles the initial interaction with a flight master NPC.
1.  Validates the target NPC using `Player.Main/GetNPCIfCanInteractWith`, ensuring it has the `UNIT_NPC_FLAG_FLIGHTMASTER` flag. If invalid, it logs and returns.
2.  Performs a cleanup step: if the player is feigning death (`Unit.Main/HasUnitState`), it removes the associated spells via `Unit.Main/RemoveSpellsCausingAura`.
3.  Attempts to learn the node via `SendLearnNewTaxiNode`. If this returns `true` (meaning the node was newly learned or the location was invalid), it stops processing.
4.  If the node was already known, it proceeds to `SendTaxiMenu` to display the flight options.

**`SendLearnNewTaxiNode`**
Determines if the player should learn the taxi node associated with the given creature.
1.  Finds the nearest taxi node ID using `ObjectMgr/GetNearestTaxiNode` based on the creature's coordinates and the player's team.
2.  If no node is found (`curloc == 0`), it returns `true`. This prevents `SendTaxiMenu` from being called with an invalid node ID, effectively treating invalid locations as "handled" to avoid errors downstream.
3.  Calls `PlayerTaxi/SetTaximaskNode` to mark the node as known.
    *   If the node was **not** previously known (function returns `true`):
        *   Sends `SMSG_NEW_TAXI_PATH` to trigger the "New Flight Path Discovered" cinematic/text.
        *   Sends `SMSG_TAXINODE_STATUS` with status `1` to update the client's hover state.
        *   Returns `true`.
    *   If the node was **already** known (function returns `false`):
        *   Returns `false`, signaling the caller (`HandleTaxiQueryAvailableNodes`) to proceed to the menu.

### Menu & Flight Execution

**`SendTaxiMenu`**
Constructs and sends the flight path menu to the client.
1.  Identifies the current node ID using `ObjectMgr/GetNearestTaxiNode`. If none is found, it returns.
2.  Builds an `SMSG_SHOWTAXINODES` packet.
3.  Serializes the flight master's GUID, the current node ID, and a count of `1` (indicating the starting node).
4.  Appends the list of known nodes and their costs to the packet using `PlayerTaxi/AppendTaximaskTo`. This function internally handles the logic of determining which nodes are reachable and calculating costs, taking into account whether the player is flagged as a "taxi cheater" (`Player.Main/IsTaxiCheater`).
5.  Sends the packet.

**`SendDoFlight`**
Initiates the physical flight animation and movement.
1.  Cleans up feign death states if present.
2.  Ensures the player is not stuck in a previous flight state by looping while `Creature.MotionMaster/GetCurrentMovementGeneratorType` is `FLIGHT_MOTION_TYPE`, calling `MotionMaster/MovementExpired` to clear it.
3.  Applies the mount visual using `Player.Main/Mount` if a `mountDisplayId` is provided.
4.  Checks `PlayerTaxi/GetTaxiPath`.
    *   If the path is non-empty (multi-step flight), it calls `Creature.MotionMaster/MoveTaxiFlight()` (no arguments) to continue the existing path.
    *   Otherwise, it starts a new flight using `Creature.MotionMaster/MoveTaxiFlight(path, pathNode)` with the specified path ID and starting node.

### Activation Opcodes

**`HandleActivateTaxiOpcode`**
Handles the standard "Take Flight" button press for a single destination.
1.  Creates a vector of two nodes: `{packet.node1, packet.node2}`. In the context of standard taxi, this usually represents the start and end node.
2.  Validates the flight master NPC via `Player.Main/GetNPCIfCanInteractWith`.
3.  Delegates the actual path activation to `Player.Main/ActivateTaxiPathTo`, passing the node list and the NPC pointer.

**`HandleActivateTaxiExpressOpcode`**
Handles the "Express" flight option, allowing multi-stop flights selected via the UI. This handler is only compiled for client builds newer than 1.9.4.
1.  Validates the flight master NPC.
2.  Checks if the `packet.nodes` vector is empty; if so, returns.
3.  Delegates to `Player.Main/ActivateTaxiPathTo` with the full list of nodes and the NPC pointer.

## Cross-Unit Boundaries

### Incoming Calls (Called By)

*   **`Player.Main/OnGossipSelect`** (in `Player` unit): Calls `SendTaxiMenu`. This occurs when a player interacts with an NPC that has gossip options linked to flight paths, bypassing the standard taxi query flow.
*   **`Player.Main/PrepareGossipMenu`** (in `Player` unit): Calls `SendLearnNewTaxiNode`. This ensures that if a player talks to a flight master via gossip, they still discover the node if it's new.
*   **`Player.Main/ActivateTaxiPathTo`**, **`Player.Main/ContinueTaxiFlight`**, **`Player.Main/TaxiStepFinished`** (all in `Player` unit): Call `SendDoFlight`. These methods in the `Player` class manage the high-level state of the flight (cost deduction, path calculation, step completion) and invoke `SendDoFlight` to execute the low-level movement commands on the client/server sync layer.

### Outgoing Calls (Calls Into)

*   **`ObjectMgr/GetNearestTaxiNode`** (in `ObjectMgr` unit): Critical dependency. Translates 3D coordinates into a logical Taxi Node ID. Without this, the system cannot map NPCs to flight paths.
*   **`PlayerTaxi` Subsystem** (methods `IsTaximaskNodeKnown`, `SetTaximaskNode`, `AppendTaximaskTo`, `GetTaxiPath` in `PlayerTaxi` unit): The core business logic for taxi resides here. `TaxiHandler` merely triggers these functions. `PlayerTaxi` manages the bitmask of known nodes, calculates costs, and stores the active flight path.
*   **`Creature.MotionMaster`** (methods `GetCurrentMovementGeneratorType`, `MoveTaxiFlight`, `MovementExpired` in `Creature.MotionMaster` unit): Handles the actual spline movement. `MoveTaxiFlight` instructs the creature (player) to move along the predefined taxi spline.
*   **`Log.Main/Out`** (in `Log.Main` unit): Used for debugging failed interactions (e.g., missing NPCs).
*   **`WorldSession.Main/GetPlayer`** and **`WorldSession.Main/SendPacket`** (in `WorldSession.Main` unit): Standard accessors to retrieve the `Player` object associated with the session and send network packets.

## Data Model

This unit performs **no direct database operations**. It does not query or modify any SQL tables. All persistence regarding known taxi nodes is handled by the `Player` class (via `PlayerTaxi`), which saves the taximask to the database during character save/load cycles. The spatial data for taxi nodes is loaded into memory by `ObjectMgr` at server startup.

## Notable Implementation Details

1.  **Feign Death Cleanup**: Both `HandleTaxiQueryAvailableNodes` and `SendDoFlight` explicitly check for and remove `SPELL_AURA_FEIGN_DEATH`. This is a robustness measure to prevent players from interacting with flight masters or flying while pretending to be dead, which could cause desyncs or exploits.
2.  **Invalid Node Handling in `SendLearnNewTaxiNode`**: If `GetNearestTaxiNode` returns `0` (no node found), `SendLearnNewTaxiNode` returns `true`. This is a subtle control-flow mechanism. By returning `true`, it signals to `HandleTaxiQueryAvailableNodes` that the "learning" phase is complete (even though nothing was learned), preventing `SendTaxiMenu` from executing with an invalid node ID, which would likely crash or corrupt the packet.
3.  **Multi-Path Support**: `SendDoFlight` distinguishes between starting a new flight and continuing a multi-step flight. It checks `GetTaxiPath().empty()`. If the path is already populated (from a previous step), it calls `MoveTaxiFlight()` without arguments to advance to the next segment. This allows seamless chaining of flight paths without re-sending the entire route from the client.
4.  **Client Version Gating**: `HandleActivateTaxiExpressOpcode` is wrapped in `#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_9_4`. This reflects that the express taxi UI (allowing multiple stops in one purchase) was introduced in later client versions. Older clients use `HandleActivateTaxiOpcode` which only supports simple point-to-point flights.
5.  **Cheater Flag**: `SendTaxiMenu` passes `GetPlayer()->IsTaxiCheater()` to `AppendTaximaskTo`. This suggests the server tracks players who exploit taxi costs (e.g., by manipulating position or flags) and may alter the cost calculation or menu presentation for them.

## Member Reference

**`HandleTaxiNodeStatusQueryOpcode`**
Receives the `TaxiNodeStatusQuery` packet and delegates to `SendTaxiStatus` with the creature GUID from the packet.

**`SendTaxiStatus`**
Validates the creature exists, finds the nearest taxi node ID via `ObjectMgr`, checks if the player knows it via `PlayerTaxi`, and sends `SMSG_TAXINODE_STATUS` to the client. Logs errors if the creature is missing.

**`HandleTaxiQueryAvailableNodes`**
Validates the flight master NPC, removes feign death effects, attempts to learn the node via `SendLearnNewTaxiNode`, and if already known, opens the menu via `SendTaxiMenu`.

**`SendTaxiMenu`**
Finds the current node ID, constructs `SMSG_SHOWTAXINODES`, appends known nodes/costs via `PlayerTaxi/AppendTaximaskTo` (respecting cheater flags), and sends the packet.

**`SendDoFlight`**
Removes feign death, clears existing flight motion generators, applies the mount visual, and initiates movement via `Creature.MotionMaster/MoveTaxiFlight`, handling both new paths and continuation of multi-step paths.

**`SendLearnNewTaxiNode`**
Finds the node ID for the creature. If valid, marks it as known via `PlayerTaxi/SetTaximaskNode`. If newly learned, sends discovery packets (`SMSG_NEW_TAXI_PATH`, `SMSG_TAXINODE_STATUS`). Returns `true` if the node was learned OR if no node was found (to suppress menu generation).

**`HandleActivateTaxiExpressOpcode`**
(Only for clients > 1.9.4) Validates the flight master, checks for non-empty node list, and calls `Player.Main/ActivateTaxiPathTo` to start a multi-stop flight.

**`HandleActivateTaxiOpcode`**
Validates the flight master, creates a two-node vector from the packet, and calls `Player.Main/ActivateTaxiPathTo` to start a standard flight.

---

<!-- machine-true, projected from graph.json -->

## Map — WorldSession.TaxiHandler

*Source:* TaxiHandler.cpp, WorldSession.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| HandleTaxiNodeStatusQueryOpcode | method | — | — | — |
| SendTaxiStatus | method | ByteBuffer/operator<<#7, Log.Main/Out, Map.Main/GetCreature, ObjectGuid/GetString, ObjectGuid/operator<<, ObjectMgr/GetNearestTaxiNode, Player.Main/GetTeam, PlayerTaxi/IsTaximaskNodeKnown, WorldObject.Object/GetMap, WorldObject.Object/GetMapId, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldPacket/WorldPacket#4, WorldSession.Main/GetPlayer, WorldSession.Main/SendPacket | — | — |
| HandleTaxiQueryAvailableNodes | method | Log.Main/Out, ObjectGuid/GetString, Player.Main/GetNPCIfCanInteractWith, Unit.Main/HasUnitState, Unit.Main/RemoveSpellsCausingAura, WorldSession.Main/GetPlayer | — | — |
| SendTaxiMenu | method | ByteBuffer/operator<<#10, Object/GetObjectGuid, ObjectGuid/operator<<, ObjectMgr/GetNearestTaxiNode, Player.Main/GetTeam, Player.Main/IsTaxiCheater, PlayerTaxi/AppendTaximaskTo, WorldObject.Object/GetMapId, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldPacket/WorldPacket#4, WorldSession.Main/GetPlayer, WorldSession.Main/SendPacket | Player.Main/OnGossipSelect | — |
| SendDoFlight | method | Creature.MotionMaster/GetCurrentMovementGeneratorType, Creature.MotionMaster/MoveTaxiFlight, Creature.MotionMaster/MoveTaxiFlight#2, MotionMaster/MovementExpired, Player.Main/Mount, PlayerTaxi/GetTaxiPath, Unit.Main/GetMotionMaster, Unit.Main/HasUnitState, Unit.Main/RemoveSpellsCausingAura, WorldSession.Main/GetPlayer | Player.Main/ActivateTaxiPathTo, Player.Main/ContinueTaxiFlight, Player.Main/TaxiStepFinished | — |
| SendLearnNewTaxiNode | method | ByteBuffer/operator<<#7, Object/GetObjectGuid, ObjectGuid/operator<<, ObjectMgr/GetNearestTaxiNode, Player.Main/GetTeam, PlayerTaxi/SetTaximaskNode, WorldObject.Object/GetMapId, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldPacket/WorldPacket#4, WorldSession.Main/GetPlayer, WorldSession.Main/SendPacket | Player.Main/PrepareGossipMenu | — |
| HandleActivateTaxiExpressOpcode | method | Log.Main/Out, ObjectGuid/GetString, Player.Main/ActivateTaxiPathTo, Player.Main/GetNPCIfCanInteractWith, WorldSession.Main/GetPlayer | — | — |
| HandleActivateTaxiOpcode | method | Log.Main/Out, ObjectGuid/GetString, Player.Main/ActivateTaxiPathTo, Player.Main/GetNPCIfCanInteractWith, WorldSession.Main/GetPlayer | — | — |

---

<!-- verify: boundary-bleed | foreign: update, WorldSession -->
