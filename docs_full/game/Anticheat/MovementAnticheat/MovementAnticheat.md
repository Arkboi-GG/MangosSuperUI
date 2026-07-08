# MovementAnticheat

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# MovementAnticheat

**Purpose & Responsibilities**

`MovementAnticheat` is the core engine for detecting and penalizing movement-based cheating in the WoWVMaNGOS server. It operates as a persistent state machine attached to each `Player`, analyzing incoming movement packets (`MovementInfo`) against expected physical behaviors, server-side state, and configurable thresholds.

Its primary responsibilities are:
1.  **Validation:** Checking every movement packet for anomalies such as impossible speeds, teleportation, wall-climbing, flying without flight abilities, and timing desynchronization.
2.  **Accumulation:** Tracking cheat occurrences over time (both per-update-tick and total session) to distinguish between transient network errors and sustained malicious behavior.
3.  **Enforcement:** Calculating penalties (kick, ban, IP ban) based on configured thresholds and executing immediate corrective actions (e.g., rejecting movement, resetting flags, forcing logout) when specific severe cheats are detected.
4.  **Logging:** Maintaining a packet log for flagged players to aid in post-mortem analysis and debugging.

The system is highly configurable via `World` configuration settings, allowing administrators to enable/disable specific checks, adjust sensitivity thresholds, and define penalty severities.

## Member-by-Member Behavior

### Initialization and Lifecycle Management

*   **`MovementAnticheat`**: The constructor initializes the object with a pointer to the `Player` (`me`) and retrieves the associated `WorldSession`. It establishes the link between the player entity and their network session for logging and security checks.
*   **`Init`**: Resets all internal counters (`m_cheatOccuranceTick`, `m_cheatOccuranceTotal`), desync metrics, jump counters, and timers. This prepares the anticheat state for a fresh evaluation cycle.
*   **`InitNewPlayer`**: Updates the internal `me` pointer to a new `Player` object. This is critical for handling mind-control scenarios where the `MovementAnticheat` instance remains bound to the original session/player context, but the controlled unit changes. It resets jump-related counters for the new target.
*   **`InitWallClimbLimits`**: A static method called during server startup (`World/LoadConfigSettings`). It calculates the tangent slopes (`m_wallSlope`, `m_wallSlopeHigh`) based on the configured wall climb angle, defining the geometric limits for valid vertical movement relative to horizontal displacement.
*   **`ResetJumpCounters`**: Clears jump tracking variables (`m_jumpCount`, `m_jumpFlagCount`, `m_jumpFlagTime`). Called when a jump sequence legitimately ends (e.g., landing, swimming) to prevent false positives in subsequent movements.

### Core Update and Penalty Logic

*   **`Update`**: Called periodically by `Player.Main/Update`. It acts as a timer gate; if the `m_updateCheckTimer` has not expired, it returns immediately. Otherwise, it triggers `Finalize` to evaluate accumulated cheats and determine penalties.
*   **`Finalize`**: The central aggregation point. It updates peak desync/distance metrics, resets the update timer, and calls `ComputeCheatAction` to determine the penalty bitmask. If logging is enabled, it writes summary data to the log. If a kick/ban penalty is triggered and packet logs exist, it appends a final message and dumps the `m_packetLog` deque to a file via `SniffFile`. Finally, it resets the tick-based cheat counters.
*   **`ComputeCheatAction`**: Iterates through all `CheatType` entries. For each, it checks if the check is enabled and if the occurrence count (either `m_cheatOccuranceTick` for immediate violations or `m_cheatOccuranceTotal` for cumulative ones) exceeds the configured threshold. If so, it applies the corresponding penalty bit (kick, ban, etc.) and appends the cheat name and count to the `reason` stream. It also handles special logic for botting detection, checking if enough data has been collected and applying the botting penalty if turn patterns are abnormal.
*   **`AddCheats`**: Takes a bitmask of detected cheats and a count. If notification or logging is enabled, it constructs a human-readable string of cheat names using `GetMovementCheatName` and sends a system message to the player or adds it to the packet log. It then delegates to `StoreCheat` for each individual cheat type.
*   **`StoreCheat`**: Increments both the tick-based (`m_cheatOccuranceTick`) and total (`m_cheatOccuranceTotal`) occurrence counters for a specific cheat type.

### Packet Handling and Movement Validation

*   **`HandlePositionTests`**: The main entry point for position-based validation, called by `WorldSession.MovementHandler` opcodes. It performs a battery of checks:
    *   Validates client time (`ctime`) for nulls or reversals.
    *   Checks for jump speed changes mid-air.
    *   Detects overspeed jumps, multi-jumps, and swim/fly hacks.
    *   Calls specialized checkers: `CheckTimeDesync`, `CheckTeleport`, `CheckForbiddenArea`, `CheckFakeTransport`, `CheckTeleportToTransport`, `CheckWallClimb`, `CheckNoFallTime`, `CheckFallReset`, `CheckFallStop`, `CheckMoveStart`, and `CheckSpeedHack`.
    *   If `ShouldRejectMovement` returns true for the combined cheat flags, it executes corrective actions: resets movement flags, resolves pending changes, sends a heartbeat, and potentially forces a logout (for fake transport). It returns a timestamp indicating when the next movement packet should be accepted.
*   **`HandleFlagTests`**: Validates movement flags (root, levitate, water walk, hover, etc.). It detects illegal states such as self-rooting, moving while rooted, or claiming flight/water-walk status without corresponding server-side auras or permissions. If invalid flags are found, it removes them from the `movementInfo` and may reject the movement entirely if configured.
*   **`HandleSplineDone`**: Validates the completion of a server-initiated spline movement. It ensures the client hasn't sent duplicate spline IDs and that the final position reported by the client is within 10 yards of the server-calculated destination. If the player was not jumping/falling, it resets jump counters.
*   **`LogMovementPacket`**: Adds a `LoggedPacket` to the `m_packetLog` deque if the log size limit is configured. It uses a mutex to ensure thread safety.
*   **`IsLoggedOpcode`**: A static helper that returns `true` for opcodes relevant to movement cheating (teleports, speed changes, jumps, roots, etc.), determining which packets should be captured in the log.
*   **`AddMessageToPacketLog`**: Creates a dummy `WorldPacket` containing a text message and logs it via `LogMovementPacket`, allowing textual annotations (like "Detected cheats: ...") to be embedded in the binary packet log.

### Specialized Cheat Detection Methods

*   **`CheckTimeDesync`**: Compares client timestamps (`ctime`, `stime`) between consecutive packets. Detects time reversal (`CHEAT_TYPE_TIME_BACK`) and significant desynchronization (`CHEAT_TYPE_NUM_DESYNC`, `CHEAT_TYPE_TIME_DESYNC`).
*   **`CheckMultiJump`**: Tracks jump opcodes. If a jump occurs before the previous one has landed/swam, it flags `CHEAT_TYPE_MULTI_JUMP`.
*   **`CheckWallClimb`**: Calculates the slope of movement (delta Z / delta XY). If the slope exceeds `m_wallSlopeHigh`, it's an immediate fail. If it's between `m_wallSlope` and `m_wallSlopeHigh`, it compares VMap heights to see if the player is clipping through geometry. Flags `CHEAT_TYPE_WALL_CLIMB`.
*   **`CheckForbiddenArea`**: Hardcoded checks for specific Battlegrounds (Alterac Valley, Warsong Gulch, Arathi Basin). It prevents players from leaving their starting zones before the battle begins. Flags `CHEAT_TYPE_FORBIDDEN_AREA`.
*   **`CheckSpeedHack`**: Compares the distance traveled by the client against the expected distance based on speed and time. It uses server-side extrapolation (`Unit.Main/ExtrapolateMovement`) for accuracy. Accumulates `m_overspeedDistance` if the client moves faster than allowed. Also detects skipped heartbeats if the time gap between packets is too large while moving.
*   **`CheckFakeTransport`**: Verifies that if a player claims to be on a transport (`MOVEFLAG_ONTRANSPORT`), the referenced `GameObject` actually exists, is a transport, and is within 70 yards. Flags `CHEAT_TYPE_FAKE_TRANSPORT`.
*   **`CheckTeleportToTransport`**: Detects if a player suddenly appears on a transport after being far away (>100 yards) in the previous packet, without a legitimate teleport event. Flags `CHEAT_TYPE_TELEPORT_TRANSPORT`.
*   **`CheckNoFallTime`**: Monitors the duration of falling/jumping states. If a player stays in a jump/fall state for longer than `MIN_FALLING_TIME` without reporting fall time, it flags `CHEAT_TYPE_NO_FALL_TIME`.
*   **`CheckFallReset`**: Ensures that `CMSG_MOVE_FALL_RESET` is only sent when the player was actually jumping/falling. Flags `CHEAT_TYPE_BAD_FALL_RESET`.
*   **`CheckFallStop`**: Detects if a player stops falling/jumping abruptly without a valid landing opcode or root. Flags `CHEAT_TYPE_BAD_FALL_STOP`.
*   **`CheckMoveStart`**: Validates that movement start opcodes (forward, backward, pitch, strafe, turn) match the flags set in the `MovementInfo`. Flags `CHEAT_TYPE_BAD_MOVE_START`.
*   **`CheckBotting`**: Analyzes movement patterns to detect automated bots. It tracks turns (mouse vs. keyboard vs. abnormal) and resets stats if human-like behavior (strafer, jumping backward, interrupting casts) is observed. It increments the botting counter for consistent turning.
*   **`CheckTeleport`**: Detects sudden large displacements that don't match expected speed or teleport events. It excludes launched/taxi/teleporting states. Uses `IsTeleportAllowed3D` to check distance limits. Flags `CHEAT_TYPE_TELEPORT`.
*   **`IsTeleportAllowed3D`**: Checks if the distance moved is within the allowed teleport distance, accounting for speed rates and excluding known elevator areas (`IsInTransportArea`).
*   **`IsInTransportArea`**: Checks cached zone/area IDs for known elevator locations (Undercity Lift, Deeprun Tram, Thousand Needles Lift) to allow larger positional jumps.

### Event Handlers and Utilities

*   **`OnKnockBack`**: Sets `m_knockBack` to true and resets jump counters. This suppresses certain checks (like wall climb or speed) while the player is being knocked back, as their movement is server-controlled.
*   **`OnUnreachable`**: Called when a unit attacks an unreachable target. If the attacker is a player, not in knockback, and not on a transport, it flags `CHEAT_TYPE_PVE_FLYHACK`.
*   **`OnExplore`**: Flags `CHEAT_TYPE_EXPLORE` if exploration is enabled, and `CHEAT_TYPE_EXPLORE_HIGH_LEVEL` if the player explores an area significantly higher than their level.
*   **`OnWrongAckData`**: Flags `CHEAT_TYPE_WRONG_ACK_DATA` when the client sends incorrect acknowledgment data for movement changes.
*   **`OnFailedToAckChange`**: Flags `CHEAT_TYPE_PENDING_ACK_DELAY` when the client fails to acknowledge a pending movement change in time.
*   **`OnDeath`**: Records the death time (`m_deathTime`), likely used to reset or adjust cheat windows upon resurrection.
*   **`GetMoveTypeForMovementInfo`**: Determines the current movement type (Run, Walk, Swim, etc.) based on flags in `MovementInfo`.
*   **`ShouldRejectMovement`**: A static function that checks if any of the detected cheat flags correspond to a "reject" configuration option. If so, it returns `true`, triggering immediate movement correction in `HandlePositionTests`.
*   **`GetMovementCheatName`**: A free function that maps `CheatType` enums to human-readable strings for logging and notifications.
*   **`HandleCommand`**: Provides debug information via chat commands, displaying max desync values and cheat occurrence totals.
*   **`GetLastMovementInfo`**: Returns references to the player's stored `m_movementInfo`, used for comparing current packets against the previous state.

## Cross-Unit Boundaries

*   **`Player.Main`**: The `MovementAnticheat` instance is tightly coupled with the `Player` object. It accesses `Player`'s movement info, speed, position, and state (teleporting, taxi flying, etc.). It also calls `Player` methods to send messages, resolve pending changes, and force logouts.
*   **`WorldSession.Main`**: Used to retrieve the player's username for logging, security level for bypassing checks, and latency for timing calculations.
*   **`World`**: Heavily relied upon for configuration settings (`getConfig`). Almost every check and penalty decision depends on values loaded from the server configuration.
*   **`WorldTimer`**: Used for high-resolution time measurements to calculate desync, elapsed time, and botting periods.
*   **`MovementPacketSender`**: Called to send corrective packets to the client (e.g., `SendSpeedChangeToAll`) when a cheat is rejected.
*   **`SniffFile`**: Used to write the packet log to disk when a ban/kick is issued.
*   **`ChatHandler`**: Used to display debug information via the `HandleCommand` method.
*   **`GridMap` / `Map.Main`**: Used in `CheckWallClimb` to query terrain heights and VMap data to validate vertical movement.
*   **`Unit.Main`**: Accessed for general unit state (rooted, stunned, charmed) and position data.
*   **`BattleGround`**: Checked in `CheckForbiddenArea` to enforce Battleground-specific movement rules.
*   **`GameObject`**: Checked in `CheckFakeTransport` to verify the existence and validity of transports.

## Data Model

This unit does not interact directly with database tables. All state is held in memory within the `MovementAnticheat` instance and the associated `Player`/`WorldSession` objects. Configuration is loaded from `vmangos.conf` (via `World/getConfig`). Packet logs are written to the filesystem as `.pkt` files, not to a database.

## Notable Implementation Details

*   **Mind Control Handling**: The `me` pointer in `MovementAnticheat` can change if the player controls another unit (e.g., via a spell). `InitNewPlayer` is called to update this pointer and reset counters, ensuring checks apply to the currently controlled entity while retaining the session context for logging/penalties.
*   **Tick vs. Total Counters**: Cheat occurrences are tracked in two arrays: `m_cheatOccuranceTick` (reset every `CHEATS_UPDATE_INTERVAL`) and `m_cheatOccuranceTotal` (reset only when a threshold is met). This allows for both immediate reactions to burst cheating and long-term accumulation of minor infractions.
*   **Packet Logging**: The `m_packetLog` is a `std::deque` protected by a mutex. It stores `LoggedPacket` objects, which contain the raw packet data. This log is only dumped to disk if a kick/ban penalty is triggered, optimizing I/O.
*   **Wall Climb Geometry**: The wall climb check uses a two-tier slope threshold. If the slope is moderately steep, it performs a more expensive VMap height comparison to distinguish between legitimate stair climbing and wall clipping.
*   **Botting Heuristics**: The botting detector distinguishes between mouse turns, keyboard turns, and abnormal turns. It resets its stats if it detects human-like behaviors (strafing, jumping backward, interrupting casts), making it harder for simple bots to evade detection.
*   **Transport Exceptions**: Several checks explicitly exclude players on transports or in known elevator areas to avoid false positives due to the complex movement mechanics of vehicles and lifts.
*   **Immediate Rejection**: Unlike some checks that only accumulate points, certain severe cheats (like fake transport or self-root) trigger immediate movement rejection and state correction via `ShouldRejectMovement` and the logic in `HandlePositionTests`/`HandleFlagTests`.

## Member Reference

**GetMovementCheatName**: Free function mapping `CheatType` enum values to human-readable string literals for logging and notifications.

**IsInKnockBack**: Inline method returning the `m_knockBack` boolean flag, indicating if the player is currently under server-controlled knockback movement.

**MovementAnticheat**: Constructor initializing the `me` (Player) and `m_session` (WorldSession) pointers.

**GetLastMovementInfo**: Overloaded method returning a non-const reference to the `Player`'s `m_movementInfo`, used for comparing current packet data against the previous state.

**GetLastMovementInfo#2**: Overloaded method returning a const reference to the `Player`'s `m_movementInfo`, used for read-only comparisons of previous state.

**Update**: Timer-gated method called by `Player.Main/Update`. Triggers `Finalize` if the `m_updateCheckTimer` has expired.

**Finalize**: Aggregates peak desync/distance metrics, calls `ComputeCheatAction` to determine penalties, logs summary data, dumps packet logs to file if a kick/ban occurred, and resets tick-based counters.

**AddCheats**: Processes a bitmask of detected cheats, constructs human-readable names, notifies the player/logs if configured, and delegates to `StoreCheat` for each type.

**StoreCheat**: Increments both tick-based and total occurrence counters for a specific cheat type.

**ComputeCheatAction**: Iterates through all cheat types, compares occurrence counts against configured thresholds, applies penalty bits, and appends reasons to the output stream. Includes special logic for botting detection.

**AddMessageToPacketLog**: Creates a dummy `WorldPacket` with a text message and logs it via `LogMovementPacket` for annotation in the packet dump.

**IsLoggedOpcode**: Static method returning `true` for opcodes relevant to movement cheating, used to filter packets for logging.

**LogMovementPacket**: Thread-safe method adding a `LoggedPacket` to the `m_packetLog` deque if the size limit is configured.

**HandleCommand**: Debug method displaying max desync values and cheat occurrence totals via `ChatHandler`.

**Init**: Resets all internal counters, desync metrics, jump counters, and timers for a fresh evaluation cycle.

**InitNewPlayer**: Updates the `me` pointer to a new `Player` object (for mind control) and resets jump-related counters.

**ResetJumpCounters**: Clears jump tracking variables (`m_jumpCount`, `m_jumpFlagCount`, `m_jumpFlagTime`).

**InitWallClimbLimits**: Static method calculating tangent slopes for wall climb detection from configuration.

**OnKnockBack**: Sets `m_knockBack` to true and resets jump counters to suppress checks during server-controlled knockback.

**OnUnreachable**: Flags `CHEAT_TYPE_PVE_FLYHACK` if a player attacks an unreachable target without valid justification.

**OnExplore**: Flags `CHEAT_TYPE_EXPLORE` and `CHEAT_TYPE_EXPLORE_HIGH_LEVEL` based on area exploration and level disparity.

**OnWrongAckData**: Flags `CHEAT_TYPE_WRONG_ACK_DATA` when client acknowledgment data is incorrect.

**OnFailedToAckChange**: Flags `CHEAT_TYPE_PENDING_ACK_DELAY` when client fails to acknowledge movement changes in time.

**GetMoveTypeForMovementInfo**: Determines the current movement type (Run, Walk, Swim, etc.) based on flags in `MovementInfo`.

**ShouldRejectMovement**: Static function checking if any detected cheat flags correspond to a "reject" configuration option, triggering immediate movement correction.

**OnDeath**: Records the death time (`m_deathTime`).

**HandlePositionTests**: Main entry point for position-based validation. Performs a battery of checks (time, jump, teleport, wall climb, speed, etc.) and executes corrective actions if `ShouldRejectMovement` returns true.

**HandleFlagTests**: Validates movement flags (root, levitate, water walk, hover, etc.) and removes illegal flags or rejects movement if configured.

**HandleSplineDone**: Validates the completion of a server-initiated spline movement, ensuring no duplicate IDs and proximity to the destination.

**CheckNoFallTime**: Monitors the duration of falling/jumping states, flagging `CHEAT_TYPE_NO_FALL_TIME` if fall time is not reported for too long.

**CheckFallReset**: Ensures `CMSG_MOVE_FALL_RESET` is only sent when the player was actually jumping/falling.

**CheckFallStop**: Detects abrupt stops in falling/jumping without valid landing opcodes.

**CheckMoveStart**: Validates that movement start opcodes match the flags set in `MovementInfo`.

**CheckTimeDesync**: Compares client timestamps between consecutive packets to detect time reversal and desynchronization.

**CheckMultiJump**: Tracks jump opcodes, flagging `CHEAT_TYPE_MULTI_JUMP` if a jump occurs before the previous one lands.

**CheckWallClimb**: Calculates movement slope and compares it against configured limits and VMap heights to detect wall climbing.

**CheckForbiddenArea**: Hardcoded checks for specific Battlegrounds to prevent leaving starting zones early.

**CheckSpeedHack**: Compares client-traveled distance against expected distance based on speed and time, accumulating overspeed metrics.

**CheckFakeTransport**: Verifies the existence and validity of transports claimed by the player.

**CheckTeleportToTransport**: Detects sudden appearances on transports after being far away.

**HasEnoughBottingData**: Checks if sufficient movement packets have been received and if turn patterns are abnormal enough to warrant botting detection.

**ResetBottingStats**: Resets botting detection counters and timers.

**CheckBotting**: Analyzes movement patterns (turns, strafing, jumping) to detect automated bots.

**CheckTeleport**: Detects sudden large displacements that don't match expected speed or teleport events.

**IsInTransportArea**: Checks cached zone/area IDs for known elevator locations to allow larger positional jumps.

**IsTeleportAllowed3D**: Checks if the distance moved is within the allowed teleport distance, accounting for speed rates and excluding known elevator areas.

---

<!-- machine-true, projected from graph.json -->

## Map — MovementAnticheat

*Source:* MovementAnticheat.cpp, MovementAnticheat.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GetMovementCheatName | function | — | — | — |
| IsInKnockBack | method | — | Creature.Main/Update | — |
| MovementAnticheat | ctor | Player.Main/GetSession | Anticheat/CreateAnticheatFor | — |
| GetLastMovementInfo | method | — | — | — |
| GetLastMovementInfo#2 | method | — | — | — |
| Update | method | World/getConfig | Player.Main/Update | — |
| Finalize | method | Object/IsInWorld, Player.Main/Player#2, SniffFile/SniffFile#2, World/getConfig, WorldSession.Main/GetUsername | Player.Main/OnDisconnected | — |
| AddCheats | method | Player.Main/PSendSysMessage, World/getConfig, World/getConfig#4, WorldSession.Main/GetPlayer | — | — |
| StoreCheat | method | — | — | — |
| ComputeCheatAction | method | Errors/PrintStacktraceAndThrow, World/getConfig, World/getConfig#4, WorldTimer/getMSTimeDiffToNow | — | — |
| AddMessageToPacketLog | method | ByteBuffer/operator<<, WorldPacket/WorldPacket#4 | — | — |
| IsLoggedOpcode | method | — | WorldSession.Main/QueueBinaryPacket | — |
| LogMovementPacket | method | LoggedPacket/LoggedPacket, World/getConfig#4 | MovementPacketSender/SendKnockBackToController, MovementPacketSender/SendMovementFlagChangeToController, MovementPacketSender/SendSpeedChangeToController, MovementPacketSender/SendTeleportToController, Player.Main/ExecuteTeleportFar, Player.Main/SetClientControl, WorldObject.Object/SendMovementMessageToSet, WorldSession.Main/QueueBinaryPacket | — |
| HandleCommand | method | ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage | ChatHandler.AccountCommands/HandleAnticheatCommand | — |
| Init | method | — | Anticheat/CreateAnticheatFor | — |
| InitNewPlayer | method | — | WorldSession.Main/InitCheatData | — |
| ResetJumpCounters | method | — | MoveSplineInit/Launch | — |
| InitWallClimbLimits | method | World/getConfig#2 | World/LoadConfigSettings | — |
| OnKnockBack | method | — | Unit.Main/KnockBack | — |
| OnUnreachable | method | GridMap/GetWaterLevel, Object/GetObjectGuid, ObjectGuid/operator==, Unit.Main/GetCharmerOrOwnerGuid, World/getConfig, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/GetTerrain, WorldObject.Object/GetTransport, WorldSession.Main/GetSecurity | TargetedMovementGenerator/Update, TargetedMovementGenerator/_setTargetLocation | — |
| OnExplore | method | Unit.Main/GetLevel, World/getConfig, WorldSession.Main/GetSecurity | Player.Main/CheckAreaExploreAndOutdoor | — |
| OnWrongAckData | method | World/getConfig, WorldSession.Main/GetSecurity | WorldSession.MovementHandler/HandleForceSpeedChangeAckOpcodes, WorldSession.MovementHandler/HandleMoveKnockBackAck, WorldSession.MovementHandler/HandleMovementFlagChangeToggleAck, WorldSession.MovementHandler/HandleMoveRootAck | — |
| OnFailedToAckChange | method | World/getConfig, WorldSession.Main/GetSecurity | Unit.Main/CheckPendingMovementChanges | — |
| GetMoveTypeForMovementInfo | method | MovementInfo/HasMovementFlag | — | — |
| ShouldRejectMovement | function | World/getConfig | — | — |
| OnDeath | method | shared_Util/getMSTime | Player.Main/KillPlayer | — |
| HandlePositionTests | method | MovementInfo/HasMovementFlag, MovementPacketSender/SendSpeedChangeToAll, MoveSpline/Finalized, Opcodes/IsFallEndOpcode, Opcodes/IsFlagAckOpcode, shared_Util/getMSTime, Unit.Main/GetSpeed, Unit.Main/ResolvePendingMovementChanges, Unit.Main/SendHeartBeat, World/getConfig, World/GetCurrentDiff, WorldObject.Object/CorrectData, WorldObject.Object/RemoveUnitMovementFlag, WorldSession.Main/GetLatency, WorldSession.Main/GetSecurity, WorldSession.Main/LogoutRequest | WorldSession.MovementHandler/HandleForceSpeedChangeAckOpcodes, WorldSession.MovementHandler/HandleMoveKnockBackAck, WorldSession.MovementHandler/HandleMovementFlagChangeToggleAck, WorldSession.MovementHandler/HandleMovementOpcodes, WorldSession.MovementHandler/HandleMoveNotActiveMoverOpcode, WorldSession.MovementHandler/HandleMoveRootAck | — |
| HandleFlagTests | method | MovementInfo/RemoveMovementFlag, MoveSpline/Finalized, Player.Main/HasCheatOption, Player.Main/IsBeingTeleported, shared_Util/getMSTime, Unit.Main/HasAuraType, Unit.Main/HasPendingMovementChange, Unit.Main/HasUnitState, Unit.Main/IsTaxiFlying, Unit.Main/ResolvePendingMovementChanges, Unit.Main/SendHeartBeat, World/getConfig, World/GetCurrentDiff, WorldObject.Object/RemoveUnitMovementFlag, WorldSession.Main/GetLatency, WorldSession.Main/GetSecurity | WorldSession.MovementHandler/HandleForceSpeedChangeAckOpcodes, WorldSession.MovementHandler/HandleMoveKnockBackAck, WorldSession.MovementHandler/HandleMovementFlagChangeToggleAck, WorldSession.MovementHandler/HandleMovementOpcodes, WorldSession.MovementHandler/HandleMoveNotActiveMoverOpcode, WorldSession.MovementHandler/HandleMoveRootAck, WorldSession.MovementHandler/HandleMoveSplineDoneOpcode | — |
| HandleSplineDone | method | MovementInfo/GetPos, MovementInfo/GetTransportGuid, MovementInfo/GetTransportPos#2, MovementInfo/HasMovementFlag, MoveSpline/FinalDestination, Player.Main/Player#2 | WorldSession.MovementHandler/HandleMoveSplineDoneOpcode | — |
| CheckNoFallTime | method | MovementInfo/HasMovementFlag, MovementInfo/WasSentBySession, World/getConfig, WorldSession.Main/GetGUID | — | — |
| CheckFallReset | method | MovementInfo/HasMovementFlag, MovementInfo/WasSentBySession, World/getConfig, WorldSession.Main/GetGUID | — | — |
| CheckFallStop | method | MovementInfo/HasMovementFlag, MovementInfo/WasSentBySession, Opcodes/IsFallEndOpcode, World/getConfig, WorldSession.Main/GetGUID | — | — |
| CheckMoveStart | method | MovementInfo/HasMovementFlag, MovementInfo/WasSentBySession, Opcodes/IsAnyMoveAckOpcode, Opcodes/IsFallEndOpcode, Player.Main/HasCheatOption, World/getConfig, WorldSession.Main/GetGUID | — | — |
| CheckTimeDesync | method | MovementInfo/WasSentBySession, WorldSession.Main/GetGUID, WorldTimer/getMSTimeDiff | — | — |
| CheckMultiJump | method | World/getConfig | — | — |
| CheckWallClimb | method | GridMap/GetHeightStatic, Map.Main/GetDynamicTreeHeight, Map.Main/GetTerrain, Object/HasFlag, Unit.Main/IsTaxiFlying, World/getConfig, WorldObject.Object/GetMap | — | — |
| CheckForbiddenArea | method | BattleGround/GetStatus, Player.Main/GetBattleGround, Player.Main/GetTeam, World/getConfig, WorldObject.Object/GetMapId | — | — |
| CheckSpeedHack | method | ObjectGuid/IsEmpty, Player.Main/IsBeingTeleported, Unit.Main/ExtrapolateMovement, Unit.Main/GetSpeedForMovementInfo, Unit.Main/IsTaxiFlying, World/getConfig, World/getConfig#3 | — | — |
| CheckFakeTransport | method | GameObject/IsTransport, Map.Main/GetGameObject, MovementInfo/HasMovementFlag, World/getConfig, WorldObject.Object/GetMap, WorldObject.Object/IsWithinDist | — | — |
| CheckTeleportToTransport | method | MovementInfo/HasMovementFlag, World/getConfig | — | — |
| HasEnoughBottingData | method | World/getConfig#4, WorldTimer/getMSTimeDiffToNow | — | — |
| ResetBottingStats | method | shared_Util/getMSTime | — | — |
| CheckBotting | method | MovementInfo/HasMovementFlag, Opcodes/IsAnyMoveAckOpcode, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/GetVictim, Unit.Main/IsInCombat, World/getConfig, WorldObject.Object/HasInArc, WorldObject.Object/IsMoving | — | — |
| CheckTeleport | method | MovementInfo/HasMovementFlag, Player.Main/IsBeingTeleported, Player.Main/IsLaunched, Unit.Main/IsTaxiFlying, World/getConfig | — | — |
| IsInTransportArea | method | Player.Main/GetCachedAreaId, Player.Main/GetCachedZoneId | — | — |
| IsTeleportAllowed3D | method | Player.Main/HasMovementFlag, Unit.Main/GetSpeedRate, World/getConfig#2, WorldObject.Object/GetPosition#3 | — | — |
