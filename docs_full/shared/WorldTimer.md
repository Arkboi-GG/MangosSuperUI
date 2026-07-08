<!-- provenance: verbose -->
# WorldTimer

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# WorldTimer

`WorldTimer` is a static utility class providing millisecond-precision timing services for the game server loop. It acts as the canonical source of "server time" for logic requiring monotonic progression (e.g., cooldowns, movement extrapolation, anti-cheat desync detection). The class prevents instantiation via private constructors and exposes only static methods.

## Member-by-Member Behavior

### Time Difference Calculation

**`getMSTimeDiff`**
Calculates the elapsed time in milliseconds between `oldMSTime` and `newMSTime`.
*   **Normal Case:** If `oldMSTime <= newMSTime`, returns `newMSTime - oldMSTime`.
*   **Wrap-Around Case:** If `oldMSTime > newMSTime`, the 32-bit unsigned counter likely overflowed. It computes two distances:
    1.  `diff_1`: Forward distance through wrap: `(0xFFFFFFFF - oldMSTime) + newMSTime`.
    2.  `diff_2`: Direct backward distance: `oldMSTime - newMSTime`.
    It returns `std::min(diff_1, diff_2)`. This heuristic assumes valid ticks are close in time; thus, the wrap distance is small, while the backward distance is huge (~4 billion ms). If no wrap occurred but `oldMSTime` is erroneously larger, the small backward distance is preferred.

**`getMSTimeDiffToNow`**
Convenience wrapper calling `getMSTimeDiff(t, getMSTime())`. Calculates time elapsed since timestamp `t` until the current server tick.

### State Accessors (Declared in Header, Defined Elsewhere)

*   **`getMSTime`**: Returns current server tick time (`m_iTime`).
*   **`tickTime`**: Returns current tick time.
*   **`tickPrevTime`**: Returns previous tick time (`m_iPrevTime`).
*   **`tick`**: Advances the timer, updating `m_iPrevTime` and `m_iTime`.

### Construction

*   **`WorldTimer`**: Private constructor preventing instantiation.
*   **`WorldTimer#2`**: Private copy constructor preventing copying.

## Cross-Unit Boundaries

`WorldTimer` has no outgoing calls. It is a leaf dependency called by numerous units for time deltas:

*   **Core Loop**: `Map.Main` (updates, visibility), `World`/`WorldRunnable` (global ticks), `Unit.Main` (movement, diminishing returns).
*   **Anti-Cheat/Movement**: `MovementAnticheat` (desync checks, botting data accumulation), `MovementBroadcaster` (pacing).
*   **Database/Sessions**: `DatabaseMysql` (query timing), `WorldSession.Main` (packet processing, timeouts).
*   **Game Features**: `BattleGroundMgr` (wait times), `SpellMgr`/`GMTicketMgr` (load timing), `Transport` (age), `WardenWin` (heartbeats), `CreatureAI` (alert throttling).

## Data Model

`WorldTimer` does not interact with any database tables.

## Notable Implementation Details

*   **Wrap-Around Heuristic**: `getMSTimeDiff` relies on the assumption that consecutive ticks are milliseconds apart. If the server pauses for >49 days (uint32 ms limit), logic fails.
*   **Static State**: `m_iTime` and `m_iPrevTime` are static, ensuring a single global monotonic clock for all server logic.
*   **Inline Efficiency**: Methods are inline to minimize overhead in high-frequency loops.

## Member Reference

**`getMSTimeDiff`**
Static method. Calculates millisecond difference between `oldMSTime` and `newMSTime`, handling 32-bit wrap-around by returning `std::min` of forward wrap distance and direct backward distance. Called by `BattleGroundMgr/PlayerInvitedToBgUpdateAverageWaitTime`, `BattleGroundMgr/PlayerLoggedIn`, `ChatHandler.DebugCommands/HandleMmapTestArea`, `DatabaseMysql/Execute`, `DatabaseMysql/_Query`, `Map.Main/DoUpdate`, `Map.Main/ShouldUpdateMap`, `Map.Main/UpdateCells`, `Map.Main/UpdatePlayers`, `Map.Main/UpdateSessionsMovementAndSpellsIfNeeded`, `Master/freezeDetector`, `MovementAnticheat/CheckTimeDesync`, `shared_Util/tick`, `Unit.Main/ExtrapolateMovement`, `Unit.Main/GetDiminishing`, `World/SetInitialWorldSettings`, `WorldRunnable/operator()`, `WorldSession.BattleGroundHandler/HandleBattlefieldStatusOpcode`.

**`getMSTimeDiffToNow`**
Static method. Wrapper for `getMSTimeDiff(t, getMSTime())`. Calculates time elapsed since timestamp `t`. Called by `BattleGroundMgr/RemoveOfflinePlayer`, `CreatureAI/CanTriggerAlert`, `GMTicketMgr/LoadSurveys`, `GMTicketMgr/LoadTickets`, `Map.Main/SendObjectUpdates`, `Map.Main/Update#3`, `Map.Main/UpdateVisibilityForRelocations`, `MovementAnticheat/ComputeCheatAction`, `MovementAnticheat/HasEnoughBottingData`, `MovementBroadcaster/UpdateConfiguration`, `MovementBroadcaster/Work`, `SpellMgr/LoadSpells`, `SqlOperations/Update`, `Transport/GetTimeSinceCreation`, `WardenWin/GetPlayerInfo`, `World/Update`, `WorldRunnable/operator()`, `WorldSession.Main/ProcessPackets`, `WorldSession.Main/Update`.

**`WorldTimer`**
Private constructor. Prevents instantiation.

**`WorldTimer#2`**
Private copy constructor. Prevents copying.

---

<!-- machine-true, projected from graph.json -->

## Map — WorldTimer

*Source:* Timer.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| getMSTimeDiff | method | — | BattleGroundMgr/PlayerInvitedToBgUpdateAverageWaitTime, BattleGroundMgr/PlayerLoggedIn, ChatHandler.DebugCommands/HandleMmapTestArea, DatabaseMysql/Execute, DatabaseMysql/_Query, Map.Main/DoUpdate, Map.Main/ShouldUpdateMap, Map.Main/UpdateCells, Map.Main/UpdatePlayers, Map.Main/UpdateSessionsMovementAndSpellsIfNeeded, Master/freezeDetector, MovementAnticheat/CheckTimeDesync, shared_Util/tick, Unit.Main/ExtrapolateMovement, Unit.Main/GetDiminishing, World/SetInitialWorldSettings, WorldRunnable/operator(), WorldSession.BattleGroundHandler/HandleBattlefieldStatusOpcode | — |
| getMSTimeDiffToNow | method | — | BattleGroundMgr/RemoveOfflinePlayer, CreatureAI/CanTriggerAlert, GMTicketMgr/LoadSurveys, GMTicketMgr/LoadTickets, Map.Main/SendObjectUpdates, Map.Main/Update#3, Map.Main/UpdateVisibilityForRelocations, MovementAnticheat/ComputeCheatAction, MovementAnticheat/HasEnoughBottingData, MovementBroadcaster/UpdateConfiguration, MovementBroadcaster/Work, SpellMgr/LoadSpells, SqlOperations/Update, Transport/GetTimeSinceCreation, WardenWin/GetPlayerInfo, World/Update, WorldRunnable/operator(), WorldSession.Main/ProcessPackets, WorldSession.Main/Update | — |
| WorldTimer | decl | — | — | — |
| WorldTimer#2 | decl | — | — | — |
