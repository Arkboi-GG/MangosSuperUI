# AiBotTaskData

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AiBotTaskData

**AiBotTaskData** is a plain-old-data (POD) structure defined in `AiBotAIMain.h` that encapsulates the payload of a single high-level task assigned to an autonomous AI bot. It serves as the primary state container for the bot's current objective, bridging the gap between the external C# coordinator (via the TCP bridge) and the internal C++ state machine.

The structure supports various task types defined by the `AiBotTask` enumeration, including idle wandering, movement, questing, grinding, and taxiing. It holds specific data fields relevant to these tasks, such as destination coordinates, quest identifiers, NPC GUIDs, and, critically for the `TASK_GRIND` mode, creature entries and kill counts.

This unit contains two member functions: **MatchesObjectiveEntry**, which validates if a specific creature entry satisfies the current grind objective (including alternate entries for item-drop ties), and **Clear**, which resets the entire structure to its default idle state. It does not manage database tables directly; its data is populated via network commands or initialized locally.

## Member-by-Member Behavior

### Objective Validation

**MatchesObjectiveEntry**
This method determines whether a given creature entry ID (`entry`) counts as a valid target for the bot's current objective. It is primarily used during the grinding phase (`TASK_GRIND`) to decide if a scanned creature should be engaged or credited upon death.

*   **Logic**:
    1.  If `entry` is 0, it returns `false` immediately (0 indicates no entry or invalid creature).
    2.  It checks if `entry` matches the primary `creatureEntry` field. If so, it returns `true`.
    3.  It iterates through the `altCreatureEntries` array (up to `MAX_ALT_ENTRIES`, which is 3). If `entry` matches any non-zero alternate entry, it returns `true`.
    4.  If no matches are found, it returns `false`.
*   **Context**: This logic allows the C# coordinator to specify "tie-breaker" creatures for item-drop objectives. For example, if both "Young Wolf" and "Timber Wolf" drop the required item with equal probability, the coordinator can list both. The bot will accept kills from either. However, this widening is explicitly *not* used for kill-count quests, where the server strictly requires a specific creature entry.

### State Management

**Clear**
This method resets all fields of the `AiBotTaskData` structure to their default, idle values. It effectively cancels the current task and prepares the structure for a new assignment.

*   **Actions**:
    *   Sets `type` to `TASK_IDLE`.
    *   Resets `questId`, `npcGuid`, `creatureEntry`, `killGoal`, and `killCount` to 0.
    *   Resets spatial coordinates (`x`, `y`, `z`, `radius`) to 0.0f.
    *   Clears the `altCreatureEntries` array by setting all slots to 0.
    *   Resets `taxiSourceNode` and `taxiDestNode` to 0.
*   **Usage**: Called whenever the bot receives a command that supersedes the current task (e.g., a new `SET_TASK`, `TAKE_FLIGHT`, or `TELEPORT` command) or when the bot completes its current objective and needs to revert to a neutral state.

## Cross-Unit Boundaries

**AiBotTaskData** is a passive data holder; it does not initiate calls to other units. Its methods are invoked by various components of the `AiBotAI` class to query or reset task state.

### Called By

*   **AiBotAI.Grind/SelectGrindTarget**:
    *   Calls **MatchesObjectiveEntry**.
    *   *Purpose*: During the grind patrol, the bot scans nearby creatures. `SelectGrindTarget` uses this method to filter the scan results, ensuring only creatures that match the primary or alternate objective entries are considered as valid attack targets.

*   **AiBotAI.Main/UpdateAI**:
    *   Calls **MatchesObjectiveEntry**.
    *   *Purpose*: In the main update loop, the bot checks if a killed creature contributes to the current grind objective. This ensures that `killCount` is only incremented for valid targets, preventing false progress on quests with specific entry requirements.

*   **AiBotAI.Bridge/BridgeHandleSetTask**:
    *   Calls **Clear**.
    *   *Purpose*: When the C# coordinator sends a new task definition, the current task data is wiped clean before populating the new fields. This prevents residual data from the previous task (e.g., old coordinates or creature entries) from interfering with the new one.

*   **AiBotAI.Bridge/BridgeHandleTakeFlight**:
    *   Calls **Clear**.
    *   *Purpose*: Initiating a flight path is a distinct mode that suspends normal grinding or questing. The task data is cleared to reflect this transition, though specific taxi nodes are set separately in the handling logic.

*   **AiBotAI.Bridge/BridgeHandleTeleport**:
    *   Calls **Clear**.
    *   *Purpose*: Similar to flight, a direct teleport command interrupts the current flow. Clearing the task ensures the bot does not attempt to resume a grind or quest immediately upon arrival unless explicitly instructed.

*   **AiBotAI.Main/MovementInform**:
    *   Calls **Clear**.
    *   *Purpose*: Triggered when the bot arrives at a destination. Depending on the task type, this might signal completion. For some tasks, clearing the data here signifies that the movement objective is fulfilled and the bot is ready for the next instruction.

*   **AiBotAI.Movement/MoveToDestination**:
    *   Calls **Clear**.
    *   *Purpose*: When initiating a new movement sequence, especially if it involves complex pathing or chunked navigation, the task data might be cleared to ensure a clean slate for the movement state, although typically `Clear` is called by the bridge handlers before `MoveToDestination` is invoked.

## Data Model

**AiBotTaskData** does not interact directly with any database tables. It is an in-memory structure populated by:
1.  **Network Commands**: JSON payloads received via the TCP bridge from the C# `BotBrainService`.
2.  **Local Initialization**: Default values set upon construction or reset.

There are no SQL queries, inserts, updates, or deletes associated with this unit.

## Notable Implementation Details

*   **Alternate Entries Limitation**: The `altCreatureEntries` array is fixed at size 3 (`MAX_ALT_ENTRIES`). This is a deliberate design choice to handle rare cases where multiple creature species share identical drop rates for a required item. It is *not* intended for general-purpose multi-target grinding. The code comments explicitly warn against using this for kill-count objectives, as the game server only credits kills for the exact entry specified in the quest template.
*   **Zero-Entry Guard**: `MatchesObjectiveEntry` explicitly returns `false` if the input `entry` is 0. This prevents accidental matches against uninitialized or invalid creature data, which is crucial because 0 is often used as a sentinel value for "no target" or "invalid entry" throughout the codebase.
*   **Structural Simplicity**: As a POD struct, `AiBotTaskData` has no constructors or destructors. This makes it lightweight and easy to copy or assign, which is beneficial for passing task data between different parts of the AI system without overhead.
*   **Clear vs. Default Initialization**: While the struct members have default initializers (e.g., `type = TASK_IDLE`), the `Clear()` method provides an explicit way to reset the state. This is important because the struct might be reused across multiple task lifecycles, and relying solely on default initialization could lead to stale data if the struct is not re-created.

## Member Reference

**MatchesObjectiveEntry**
Validates if a creature entry matches the current grind objective, including primary and up to three alternate entries. Returns `false` for entry 0. Used by `AiBotAI.Grind/SelectGrindTarget` and `AiBotAI.Main/UpdateAI`.

**Clear**
Resets all task fields to default idle values. Used by `AiBotAI.Bridge/BridgeHandleSetTask`, `BridgeHandleTakeFlight`, `BridgeHandleTeleport`, `AiBotAI.Main/MovementInform`, `AiBotAI.Main/UpdateAI`, and `AiBotAI.Movement/MoveToDestination`.

---

<!-- machine-true, projected from graph.json -->

## Map — AiBotTaskData

*Source:* AiBotAIMain.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| MatchesObjectiveEntry | method | — | AiBotAI.Grind/SelectGrindTarget, AiBotAI.Main/UpdateAI | — |
| Clear | method | — | AiBotAI.Bridge/BridgeHandleSetTask, AiBotAI.Bridge/BridgeHandleTakeFlight, AiBotAI.Bridge/BridgeHandleTeleport, AiBotAI.Main/MovementInform, AiBotAI.Main/UpdateAI, AiBotAI.Movement/MoveToDestination | — |
