# fireworks_show

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# fireworks_show

**Purpose & Responsibilities**

`fireworks_show` implements the scripted behavior for the `go_cheer_speaker` GameObject, which orchestrates periodic fireworks displays in major capital cities and hubs (Stormwind, Orgrimmar, Undercity, Ironforge, Thunder Bluff, Darnassus, and Booty Bay). The script activates only when the global game event `GAME_EVENT_FIREWORKS` (ID 6) is active.

The core responsibility is to trigger visual fireworks effects and accompanying crowd cheer sounds at specific times relative to the server's local clock. Specifically:
1.  **Standard Display:** Fires every minute on the hour (minute `0`, second `0`) through minute `9`.
2.  **Termination:** Stops firing at minute `10`, second `0`, playing a final cheer.
3.  **New Year Special:** If the display occurs at midnight (`hour 0`, minute `10`, second `30`), it triggers a "big" finale consisting of multiple large fireworks.

The unit manages its own timing via an internal `EventMap`, selecting random positions from hardcoded coordinate lists specific to the zone where the speaker resides, and choosing random visual effects from predefined sets of GameObject entries.

## Member-by-Member Behavior

### Initialization and Registration

*   **`AddSC_event_fireworks`**: This function registers the script with the engine. It creates a `Script` object named `"go_cheer_speaker"`, assigns `GetAI_go_cheer_speaker` as the factory function for creating the AI instance, and calls `RegisterSelf()` to make it available to the `ScriptMgr`. It is called by `ScriptLoader/AddScripts` during server startup.
*   **`GetAI_go_cheer_speaker`**: A factory function that instantiates and returns a new `go_cheer_speakerAI` object. It is invoked by the engine when a GameObject with the entry ID associated with this script spawns.
*   **`go_cheer_speakerAI` (ctor)**: Initializes the AI state. It sets `m_started` to `false` (indicating the current minute's display cycle hasn't begun) and `m_big` to `false` (indicating the special New Year finale has not occurred). It delegates initialization to the base `GameObjectAI` class.

### Selection Helpers

These methods determine which assets to use for the display. They do not perform actions themselves but return identifiers.

*   **`CheerPicker`**: Selects a random sound effect ID for crowd cheering. It uses `shared_Util/urand` to pick an integer between 0 and 3. It maps these to four specific sound IDs (`SOUND_CHEER_1` through `SOUND_CHEER_4`). Note that the switch statement handles cases 0, 1, and 2 explicitly, and defaults to `SOUND_CHEER_4` for case 3.
*   **`FireworksPicker`**: Returns a random entry ID from the `fireworkIds` array, which contains 23 different standard fireworks (various colors and sizes). It uses the helper `SelectRandomContainerElement`.
*   **`FireworksBIGOnlyPicker`**: Returns a random entry ID from the `fireworkBigIds` array, which contains 12 larger fireworks. Used exclusively for the New Year finale.

### Core Logic: `UpdateAI`

This is the primary loop, executed periodically by the engine. It manages the timing, condition checking, and execution of the fireworks display.

1.  **Event Processing**: It first updates the internal `EventMap` (`m_events`) with the elapsed time (`diff`).
2.  **Time Checking**: It retrieves the current server local time using `localtime`. The logic branches based on minutes and seconds:
    *   **Start Condition**: If the time is exactly `00:00` (minute 0, second 0), the display hasn't started yet (`!m_started`), and the global event is active, it schedules both a cheer and a firework event for 1 second from now and sets `m_started` to `true`.
    *   **Mid-Minute Continuation**: If the time is between minute 0, second 1 and minute 9, second 59, and the display hasn't started (e.g., the AI was updated late in the minute), it schedules a firework event for 1 second from now and sets `m_started` to `true`. This ensures the display doesn't miss a minute if the update tick was delayed.
    *   **Stop Condition**: If the time is exactly `10:00` (minute 10, second 0) and the display was running, it stops the cycle. It sets `m_started` to `false`, schedules a final cheer, and cancels any pending firework events.
    *   **New Year Finale**: If the time is `00:10:30` (midnight, minute 10, second 30), the global event is active, and the big finale hasn't happened yet (`!m_big`), it triggers the special sequence. It sets `m_big` to `true`, schedules a cheer, and schedules **11** consecutive firework events, all set to execute 1 second from now. This creates a rapid burst of large fireworks.
3.  **Event Execution**: It processes any events that have become due via `m_events.ExecuteEvent()`:
    *   **`EVENT_CHEER`**: Plays a random cheer sound using `WorldObject.Object/PlayDistanceSound` with the ID returned by `CheerPicker()`.
    *   **`EVENT_FIRE`**:
        *   Looks up the current zone ID using `WorldObject.Object/GetZoneId()`.
        *   Finds the corresponding position vector in the `pos` map. If the zone is not in the map (i.e., not a supported city), it does nothing.
        *   Selects a random position from the zone's list.
        *   Generates two random rotation values (`rndrot`, `rndrot2`) using `shared_Util/frand`.
        *   **Spawning**:
            *   If `m_big` is true (New Year finale), it summons a large firework using `FireworksBIGOnlyPicker()`.
            *   Otherwise, it summons a standard firework using `FireworksPicker()`.
            *   The firework is summoned at the selected position with the calculated rotations.
            *   Crucially, it immediately calls `GameObject/SetRespawnTime(0)` and `GameObject/Delete()` on the spawned firework. This indicates the firework is a transient visual effect that should disappear instantly after spawning (likely relying on the GameObject's spell/animation to play out before deletion, or the deletion triggers the visual effect).
        *   **Rescheduling**: If the display is still active (`m_started` is true), it schedules the next firework event to occur between 1 and 2 seconds from now (`Seconds(1), Seconds(2)`). This creates a rhythmic, slightly randomized interval for the fireworks within the active minute.

## Cross-Unit Boundaries

*   **`EventMap`**: Used extensively in `UpdateAI` to schedule, cancel, and execute timed events (`EVENT_CHEER`, `EVENT_FIRE`). This allows the AI to manage its own timing independently of the main game loop ticks.
*   **`GameEventMgr.Main/IsActiveEvent`**: Called in `UpdateAI` to check if `GAME_EVENT_FIREWORKS` is active. This gates the entire functionality; if the event is off, no fireworks occur.
*   **`WorldObject.Object`**:
    *   `GetZoneId`: Used in `UpdateAI` to determine which city the speaker is in, allowing selection of the correct spawn coordinates.
    *   `PlayDistanceSound`: Used in `UpdateAI` to play the cheer sounds.
    *   `SummonGameObject`: Used in `UpdateAI` to create the visual firework objects.
*   **`GameObject`**:
    *   `SetRespawnTime` and `Delete`: Called in `UpdateAI` on the newly spawned firework objects. This immediate deletion pattern suggests the fireworks are purely visual triggers that don't need to persist as interactive entities.
*   **`shared_Util`**:
    *   `urand`: Used in `CheerPicker` for integer randomization.
    *   `frand`: Used in `UpdateAI` to generate random rotation angles for the fireworks.
*   **`Script` / `ScriptMgr`**: `AddSC_event_fireworks` interacts with these to register the script. `ScriptLoader/AddScripts` calls this registration function.

## Data Model

This unit does not interact with any database tables. All configuration data (firework IDs, sound IDs, zone IDs, and spawn coordinates) is hardcoded in static arrays and maps within the source file.

## Notable Implementation Details

*   **Hardcoded Coordinates**: The spawn locations for fireworks are hardcoded in `std::vector<Position>` arrays for each city. These are mapped to zone IDs in the `pos` unordered_map. Zones like `DUROTAR` share coordinates with `ORGRIMMAR`, and `TIRISFAL_GLADES` shares with `UNDERCITY`, reflecting that the speakers are likely placed in the city zones even if the broader area has a different zone ID.
*   **Immediate Deletion**: The firework GameObjects are deleted immediately after summoning (`SetRespawnTime(0); Delete();`). This is unusual for persistent objects but common for transient visual effects where the "spawn" action itself triggers the animation/spell, and the object is then discarded to save memory.
*   **Time-Based Logic**: The AI relies heavily on `localtime` from the server's OS. This means the fireworks are synchronized to the server's local clock, not game time. The logic checks for specific minute/second combinations to start, continue, and stop the display.
*   **New Year Special Case**: The "big" finale is triggered only once per hour at `00:10:30` if the hour is 0. The `m_big` flag prevents re-triggering within the same update cycle, but since `m_started` resets at `10:00`, the `m_big` flag effectively persists until the next midnight window if the server stays up. However, the condition `localTm->tm_hour == 0` ensures it only happens at midnight.
*   **Randomized Intervals**: After the initial firework, subsequent fireworks in the same minute are scheduled with a random delay between 1 and 2 seconds (`Seconds(1), Seconds(2)`), creating a more natural, less robotic rhythm.
*   **Switch Fallthrough in CheerPicker**: The `switch` statement in `CheerPicker` does not have `break` statements. Cases 0, 1, and 2 fall through to the default return of `SOUND_CHEER_4`? No, wait. Looking closely:
    ```cpp
    switch (urand(0, 3))
    {
        case 0:
            return SOUND_CHEER_1;
        case 1:
            return SOUND_CHEER_2;
        case 2:
            return SOUND_CHEER_3;
    }
    return SOUND_CHEER_4;
    ```
    Actually, each case has a `return`. So it correctly returns one of the four sounds. The lack of `break` is irrelevant because `return` exits the function.

## Member Reference

**go_cheer_speakerAI** (ctor): Initializes the AI instance, setting `m_started` and `m_big` flags to false, and calling the base `GameObjectAI` constructor.

**CheerPicker**: Selects a random cheer sound ID (1-4) using `shared_Util/urand` and returns it.

**FireworksPicker**: Returns a random standard firework entry ID from the `fireworkIds` array.

**FireworksBIGOnlyPicker**: Returns a random large firework entry ID from the `fireworkBigIds` array.

**UpdateAI**: The main logic loop. Checks server local time to start/stop the display, handles the New Year special case, executes scheduled events (playing sounds or summoning/deleting fireworks), and reschedules future firework events with randomized intervals.

**GetAI_go_cheer_speaker**: Factory function that creates and returns a new `go_cheer_speakerAI` instance for a given GameObject.

**AddSC_event_fireworks**: Registers the `go_cheer_speaker` script with the engine by creating a `Script` object and calling `RegisterSelf()`. Called by `ScriptLoader/AddScripts`.

---

<!-- machine-true, projected from graph.json -->

## Map — fireworks_show

*Source:* fireworks_show.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| go_cheer_speakerAI | ctor | GameObjectAI/GameObjectAI | — | — |
| CheerPicker | method | shared_Util/urand | — | — |
| FireworksPicker | method | — | — | — |
| FireworksBIGOnlyPicker | method | — | — | — |
| UpdateAI | method | EventMap/CancelEvent, EventMap/ExecuteEvent, EventMap/ScheduleEvent, EventMap/ScheduleEvent#2, EventMap/Update, GameEventMgr.Main/IsActiveEvent, GameObject/Delete, GameObject/SetRespawnTime, shared_Util/frand, WorldObject.Object/GetZoneId, WorldObject.Object/PlayDistanceSound, WorldObject.Object/SummonGameObject | — | — |
| GetAI_go_cheer_speaker | function | — | — | — |
| AddSC_event_fireworks | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
