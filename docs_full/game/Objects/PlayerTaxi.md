<!-- provenance: verbose -->
# PlayerTaxi

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# PlayerTaxi

**Purpose & Responsibilities**

`PlayerTaxi` manages a player’s flight path (taxi) state, divided into two categories:
1.  **Known Nodes (`m_taximask`):** A persistent bitmask tracking which flight path nodes the player has unlocked.
2.  **Active Journey (`m_TaxiDestinations`, `m_taxiPath`):** Transient state for an ongoing taxi trip, including the queue of destination nodes, geometric path data, and cost discounts.

It provides serialization for database persistence and validation logic for route integrity, collaborating with `Player.Main` for lifecycle management, `WorldSession.TaxiHandler` for client communication, and `WaypointMovementGenerator` for movement execution.

## Member-by-Member Behavior

### Initialization and Mask Management

*   **`PlayerTaxi` (ctor)** Zero-initializes `m_taximask`. Called by `Player.Main/SaveNewPlayer`.
*   **`InitTaxiNodes`** Clears the mask and sets bits from the `startingTaxiMask` of the player’s race (`ChrRacesEntry`). Called by `Player.Main/SaveNewPlayer`.
*   **`~PlayerTaxi`** Empty destructor.
*   **`LoadTaxiMask`** Parses a space-separated string into `m_taximask` using `shared_Util/StrSplit`. It masks each value against `sTaxiNodesMask` to ensure only valid nodes are stored. Called by `Player.Main/LoadFromDB` and `ChatHandler.CharacterCommands/HandleCharacterFillFlysCommand`.
*   **`IsTaximaskNodeKnown`** Checks if a specific node bit is set in `m_taximask` (using 1-based indexing adjusted to 0-based). Called by `Player.Main/ActivateTaxiPathTo` and `WorldSession.TaxiHandler/SendTaxiStatus`.
*   **`SetTaximaskNode`** Sets a node bit in `m_taximask`; returns `true` if the bit was previously unset. Called by `AiBotAI.Bridge/BridgeHandleTakeFlight`, `ChatHandler.CharacterCommands/HandleLearnAllMyTaxisCommand`, `Player.Main/TaxiStepFinished`, and `WorldSession.TaxiHandler/SendLearnNewTaxiNode`.
*   **`AppendTaximaskTo`** Serializes `m_taximask` (or global `sTaxiNodesMask` if `all` is true) into a `ByteBuffer` using `ByteBuffer/operator<<#10`. Called by `WorldSession.TaxiHandler/SendTaxiMenu`.

### Active Journey State

*   **`ClearTaxiDestinations`** Clears `m_TaxiDestinations`, `m_taxiPath`, and resets `m_discount` to 1.0f. Called by `Player.Main/ActivateTaxiPathTo`, `Player.Main/LoadFromDB`, `Player.Main/SummonIfPossible`, `Player.Main/TaxiStepFinished`, `WaypointMovementGenerator/Finalize`, `WorldSession.MovementHandler/HandleMoveWorldportAck`, `WorldSession.BattleGroundHandler/HandleBattleFieldPortOpcode`, and various `ChatHandler.TeleportCommands`.
*   **`AddTaxiDestination`** Appends a node ID to `m_TaxiDestinations`. Called by `Player.Main/ActivateTaxiPathTo`.
*   **`SetDiscount`** Sets the `m_discount` multiplier. Called by `Player.Main/ActivateTaxiPathTo`.
*   **`GetTaxiSource`** Returns the first node in `m_TaxiDestinations` (0 if empty). Called by `Player.Main/ContinueTaxiFlight`, `Player.Main/LoadFromDB`, and `Player.Main/TaxiStepFinished`.
*   **`GetTaxiDestination`** Returns the second node in `m_TaxiDestinations` (0 if fewer than 2). Called by `Player.Main/TaxiStepFinished`.
*   **`NextTaxiDestination`** Pops the first node from `m_TaxiDestinations` and returns the new second node (advancing the journey). Called by `Player.Main/TaxiStepFinished` and `WaypointMovementGenerator/Update`.
*   **`GetTaxiPath`** Returns the `m_taxiPath` container. Called by `Creature.MotionMaster/MoveTaxiFlight` and `WorldSession.TaxiHandler/SendDoFlight`.
*   **`AddTaxiPathNode`** Adds a `TaxiPathNodeEntry` to `m_taxiPath`. Called by `Player.Main/ActivateTaxiPathTo`.
*   **`empty`** Returns `true` if `m_TaxiDestinations` is empty. Called by `AiBotAI.Main/UpdateAI`, `WaypointMovementGenerator/Finalize`, and `WorldSession.MovementHandler/HandleMoveWorldportAck`.

### Serialization and Validation

*   **`LoadTaxiDestinationsFromString`** Parses a space-separated string of node IDs into `m_TaxiDestinations` using `shared_Util/StrSplit`. It validates that at least 2 nodes exist, that a valid path exists between consecutive nodes via `ObjectMgr/GetTaxiPath`, and that a mount exists for the source node via `ObjectMgr/GetTaxiMountDisplayId`. Returns `false` if validation fails. Called by `Player.Main/LoadFromDB`.
*   **`SaveTaxiDestinationsToString`** Serializes only the first two nodes of `m_TaxiDestinations` to a space-separated string. Returns empty string if fewer than 2 nodes. Called by `Player.Main/SaveNewPlayer` and `Player.Main/SaveToDB`.
*   **`GetCurrentTaxiPath`** Retrieves the path ID for the current leg (first two destinations) via `ObjectMgr/GetTaxiPath`. Called by `Player.Main/ContinueTaxiFlight`.
*   **`GetCurrentTaxiCost`** Calculates the cost for the current leg via `ObjectMgr/GetTaxiPath`, multiplied by `m_discount` and rounded to the nearest integer. Called by `WaypointMovementGenerator/Update`.
*   **`operator<<`** Serializes `m_taximask` to an `std::ostringstream`. Called by `Player.Main/SaveNewPlayer` and `Player.Main/SaveToDB`.

## Cross-Unit Boundaries

*   **`Player.Main`**: Primary owner. Calls `PlayerTaxi` for initialization, loading/saving state, and managing journey steps (`ActivateTaxiPathTo`, `TaxiStepFinished`, `LoadFromDB`, `SaveToDB`).
*   **`WorldSession.TaxiHandler`**: Consumes mask data for menus (`SendTaxiMenu`), status (`SendTaxiStatus`), and flight instructions (`SendDoFlight`, `SendLearnNewTaxiNode`).
*   **`ObjectMgr`**: Provides static data validation (`GetTaxiPath`, `GetTaxiMountDisplayId`) during journey loading and cost calculation.
*   **`WaypointMovementGenerator`**: Drives the physical movement, querying path data (`GetTaxiPath`), advancing steps (`NextTaxiDestination`), and checking completion (`empty`).
*   **`ChatHandler`**: Manipulates state for GM/debug commands (e.g., `HandleCharacterFillFlysCommand`, `HandleLearnAllMyTaxisCommand`).
*   **`shared_Util`**: Provides `StrSplit` for parsing database strings.
*   **`ByteBuffer`**: Used for network serialization of the taxi mask.

## Data Model

`PlayerTaxi` does not query tables directly. It processes string data passed by `Player.Main` from the `characters` table:
*   **`taximask`**: Space-separated integers representing the bitmask.
*   **`taxipath`**: Space-separated node IDs representing the active journey.

## Notable Implementation Details

1.  **Limited Persistence:** `SaveTaxiDestinationsToString` saves only the first two nodes. Multi-leg journeys lose subsequent stops upon server restart or relog.
2.  **Mask Validation:** `LoadTaxiMask` ANDs input with `sTaxiNodesMask` to prevent invalid DB entries from corrupting the bitmask.
3.  **Mount Requirement:** `LoadTaxiDestinationsFromString` fails if no mount is found for the source node, potentially rejecting valid quest-based taxi paths that don't use standard mounts.
4.  **Indexing:** Bitmask operations use `(nodeidx - 1)`, reflecting 1-based node IDs in game logic mapped to 0-based bit positions.

## Member Reference

**PlayerTaxi** Zero-initializes `m_taximask`. Called by `Player.Main/SaveNewPlayer`.

**InitTaxiNodes** Sets `m_taximask` from race-specific `startingTaxiMask`. Called by `Player.Main/SaveNewPlayer`.

**~PlayerTaxi** Empty destructor.

**LoadTaxiMask** Parses space-separated string into `m_taximask`, masking against `sTaxiNodesMask`. Uses `shared_Util/StrSplit`. Called by `ChatHandler.CharacterCommands/HandleCharacterFillFlysCommand`, `Player.Main/LoadFromDB`.

**IsTaximaskNodeKnown** Checks if a node bit is set in `m_taximask`. Called by `Player.Main/ActivateTaxiPathTo`, `WorldSession.TaxiHandler/SendTaxiStatus`.

**SetTaximaskNode** Sets a node bit in `m_taximask`; returns `true` if newly set. Called by `AiBotAI.Bridge/BridgeHandleTakeFlight`, `ChatHandler.CharacterCommands/HandleLearnAllMyTaxisCommand`, `Player.Main/TaxiStepFinished`, `WorldSession.TaxiHandler/SendLearnNewTaxiNode`.

**AppendTaximaskTo** Serializes `m_taximask` or global mask to `ByteBuffer`. Uses `ByteBuffer/operator<<#10`. Called by `WorldSession.TaxiHandler/SendTaxiMenu`.

**ClearTaxiDestinations** Clears destinations, path, and resets discount. Called by `ChatHandler.TeleportCommands/HandleGoHelper`, `ChatHandler.TeleportCommands/HandleGonameCommand`, `ChatHandler.TeleportCommands/HandleGroupgoCommand`, `ChatHandler.TeleportCommands/HandleNamegoCommand`, `ChatHandler.TeleportCommands/HandleTeleGroupCommand`, `Player.Main/ActivateTaxiPathTo`, `Player.Main/LoadFromDB`, `Player.Main/SummonIfPossible`, `Player.Main/TaxiStepFinished`, `WaypointMovementGenerator/Finalize`, `WorldSession.BattleGroundHandler/HandleBattleFieldPortOpcode`, `WorldSession.MovementHandler/HandleMoveWorldportAck`.

**LoadTaxiDestinationsFromString** Parses node IDs, validates path connectivity via `ObjectMgr/GetTaxiPath` and mount availability via `ObjectMgr/GetTaxiMountDisplayId`. Uses `shared_Util/StrSplit`. Called by `Player.Main/LoadFromDB`.

**AddTaxiDestination** Appends node ID to `m_TaxiDestinations`. Called by `Player.Main/ActivateTaxiPathTo`.

**SetDiscount** Sets `m_discount` multiplier. Called by `Player.Main/ActivateTaxiPathTo`.

**GetTaxiSource** Returns first node in `m_TaxiDestinations`. Called by `Player.Main/ContinueTaxiFlight`, `Player.Main/LoadFromDB`, `Player.Main/TaxiStepFinished`.

**GetTaxiDestination** Returns second node in `m_TaxiDestinations`. Called by `Player.Main/TaxiStepFinished`.

**NextTaxiDestination** Pops first node, returns new second node. Called by `Player.Main/TaxiStepFinished`, `WaypointMovementGenerator/Update`.

**GetTaxiPath** Returns `m_taxiPath` container. Called by `Creature.MotionMaster/MoveTaxiFlight`, `WorldSession.TaxiHandler/SendDoFlight`.

**AddTaxiPathNode** Adds entry to `m_taxiPath`. Called by `Player.Main/ActivateTaxiPathTo`.

**empty** Returns `true` if `m_TaxiDestinations` is empty. Called by `AiBotAI.Main/UpdateAI`, `WaypointMovementGenerator/Finalize`, `WorldSession.MovementHandler/HandleMoveWorldportAck`.

**SaveTaxiDestinationsToString** Serializes only first two nodes to string. Called by `Player.Main/SaveNewPlayer`, `Player.Main/SaveToDB`.

**GetCurrentTaxiPath** Retrieves path ID for current leg via `ObjectMgr/GetTaxiPath`. Called by `Player.Main/ContinueTaxiFlight`.

**GetCurrentTaxiCost** Calculates discounted cost for current leg via `ObjectMgr/GetTaxiPath`. Called by `WaypointMovementGenerator/Update`.

**operator<<** Serializes `m_taximask` to `std::ostringstream`. Called by `Player.Main/SaveNewPlayer`, `Player.Main/SaveToDB`.

---

<!-- machine-true, projected from graph.json -->

## Map — PlayerTaxi

*Source:* PlayerTaxi.cpp, PlayerTaxi.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| PlayerTaxi | ctor | — | Player.Main/SaveNewPlayer | — |
| InitTaxiNodes | method | — | Player.Main/SaveNewPlayer | — |
| ~PlayerTaxi | dtor | — | — | — |
| LoadTaxiMask | method | shared_Util/StrSplit | ChatHandler.CharacterCommands/HandleCharacterFillFlysCommand, Player.Main/LoadFromDB | — |
| IsTaximaskNodeKnown | method | — | Player.Main/ActivateTaxiPathTo, WorldSession.TaxiHandler/SendTaxiStatus | — |
| SetTaximaskNode | method | — | AiBotAI.Bridge/BridgeHandleTakeFlight, ChatHandler.CharacterCommands/HandleLearnAllMyTaxisCommand, Player.Main/TaxiStepFinished, WorldSession.TaxiHandler/SendLearnNewTaxiNode | — |
| AppendTaximaskTo | method | ByteBuffer/operator<<#10 | WorldSession.TaxiHandler/SendTaxiMenu | — |
| ClearTaxiDestinations | method | — | ChatHandler.TeleportCommands/HandleGoHelper, ChatHandler.TeleportCommands/HandleGonameCommand, ChatHandler.TeleportCommands/HandleGroupgoCommand, ChatHandler.TeleportCommands/HandleNamegoCommand, ChatHandler.TeleportCommands/HandleTeleGroupCommand, Player.Main/ActivateTaxiPathTo, Player.Main/LoadFromDB, Player.Main/SummonIfPossible, Player.Main/TaxiStepFinished, WaypointMovementGenerator/Finalize, WorldSession.BattleGroundHandler/HandleBattleFieldPortOpcode, WorldSession.MovementHandler/HandleMoveWorldportAck | — |
| LoadTaxiDestinationsFromString | method | ObjectMgr/GetTaxiMountDisplayId, ObjectMgr/GetTaxiPath, shared_Util/StrSplit | Player.Main/LoadFromDB | — |
| AddTaxiDestination | method | — | Player.Main/ActivateTaxiPathTo | — |
| SetDiscount | method | — | Player.Main/ActivateTaxiPathTo | — |
| GetTaxiSource | method | — | Player.Main/ContinueTaxiFlight, Player.Main/LoadFromDB, Player.Main/TaxiStepFinished | — |
| GetTaxiDestination | method | — | Player.Main/TaxiStepFinished | — |
| NextTaxiDestination | method | — | Player.Main/TaxiStepFinished, WaypointMovementGenerator/Update | — |
| GetTaxiPath | method | — | Creature.MotionMaster/MoveTaxiFlight, WorldSession.TaxiHandler/SendDoFlight | — |
| AddTaxiPathNode | method | — | Player.Main/ActivateTaxiPathTo | — |
| empty | method | — | AiBotAI.Main/UpdateAI, WaypointMovementGenerator/Finalize, WorldSession.MovementHandler/HandleMoveWorldportAck | — |
| SaveTaxiDestinationsToString | method | — | Player.Main/SaveNewPlayer, Player.Main/SaveToDB | — |
| GetCurrentTaxiPath | method | ObjectMgr/GetTaxiPath | Player.Main/ContinueTaxiFlight | — |
| GetCurrentTaxiCost | method | ObjectMgr/GetTaxiPath | WaypointMovementGenerator/Update | — |
| operator<< | function | — | Player.Main/SaveNewPlayer, Player.Main/SaveToDB | — |
