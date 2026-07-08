<!-- provenance: failed-members -->
# naxxramas

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Unit Documentation: `naxxramas.h`

## Purpose & Responsibilities

The file `naxxramas.h` serves as the shared header for the Naxxramas raid instance scripts within the WoWVMaNGOS codebase. It defines the static data constants (enums for NPCs, GameObjects, Area Triggers, and text IDs) and declares the interface for the `instance_naxxramas` class. This class inherits from `ScriptedInstance` and acts as the central state manager for the raid, tracking boss encounters, door states, and specific encounter mechanics across different quarters (Arachnid, Plague, Military, Construct, and Frostwyrm Lair).

Because this is a header-only unit, it contains no executable logic. Its primary responsibilities are:
1.  **Constant Definitions:** Providing unique identifiers for all entities in the raid, organized by quarter and function.
2.  **Instance State Interface:** Declaring the `instance_naxxramas` class, which exposes methods for other scripts to query and modify instance-wide state (e.g., `SetData`, `GetData`).
3.  **Cross-Script Communication:** Defining the API that individual boss and event scripts use to interact with the instance manager, ensuring consistent behavior across the raid.
4.  **Helper Declarations:** Declaring utility functions for complex mechanics, such as calculating summon positions for Gothik's Death Knights or managing Kel'Thuzad's window portals.

## Member-by-Member Behavior

The members of `instance_naxxramas` are grouped by their functional role. Since this is a header-only unit, the descriptions reflect the declared interface and data structures.

### Instance Lifecycle and State
*   **`instance_naxxramas(Map* pMap)` / `~instance_naxxramas()`**: Constructor and destructor for the instance script object.
*   **`Initialize()`**: Resets the instance state to its initial condition when the instance is created or reset.
*   **`Update(uint32 diff)`**: A periodic tick handler that processes time-based events stored in `m_events`.

### Encounter State Management
*   **`SetData(uint32 uiType, uint32 uiData)`**: Updates instance state for a specific encounter type (e.g., marking a boss as `DONE`).
*   **`GetData(uint32 uiType)`**: Retrieves the current state of a specific encounter.
*   **`GetData64(uint32 uiData)`**: Retrieves a 64-bit GUID associated with a specific instance element.
*   **`IsEncounterInProgress()`**: Returns whether any encounter in the instance is currently active.
*   **`WingsAreCleared()`**: Checks if all four wing bosses are defeated.
*   **`GetNumEndbossDead()`**: Returns the number of wing bosses defeated.

### Object Tracking and Events
*   **`OnCreatureCreate(Creature* pCreature)`**: Called by the core when a creature spawns; used to track important NPCs.
*   **`OnObjectCreate(GameObject* pGo)`**: Called by the core when a GameObject spawns; used to track doors, portals, and chests.
*   **`OnCreatureRespawn(Creature * pCreature)`**: Handles logic when a creature respawns naturally.
*   **`OnCreatureEnterCombat(Creature * creature)`**: Triggered when a creature enters combat.
*   **`OnPlayerDeath(Player* p)` / `OnCreatureDeath(Creature* pCreature)`**: Hooks for death events.

### Door and Portal Management
*   **`UpdateAutomaticBossEntranceDoor(NaxxGOs which, uint32 uiData, int requiredPreBossData)` / `(GameObject* pGO, ...)`**: Automatically sets the state of an entrance door based on the encounter phase.
*   **`UpdateManualDoor(NaxxGOs which, uint32 uiData)` / `(GameObject* pGO, ...)`**: Manually sets a door's state.
*   **`UpdateBossGate(NaxxGOs which, uint32 uiData)` / `(GameObject* pGO, ...)`**: Handles "gate" objects, setting them to `ACTIVE` when the boss is `DONE`.
*   **`UpdateTeleporters(uint32 uiType, uint32 uiData)`**: Updates the visual and functional state of the teleporter eyes at the end of each wing.
*   **`SetTeleporterVisualState(GameObject* pGO, uint32 uiData)` / `SetTeleporterState(GameObject* pGO, uint32 uiData)`**: Helper functions to apply the correct visual/model state to teleporter GameObjects.
*   **`GetGOUuid(NaxxGOs which)`**: Retrieves the GUID of a specific GameObject by its enum ID.

### Specific Encounter Helpers
*   **Gothik's Death Knight Wing:**
    *   **`SetGothTriggers()`**: Initializes the trigger points for summoning Death Knights and Spectral Riders.
    *   **`GetClosestAnchorForGoth(Creature* pSource, bool bRightSide)`**: Calculates the nearest valid spawn point for a summoned unit on the specified side.
    *   **`GetGothSummonPointCreatures(std::list<Creature*> &lList, bool bRightSide)`**: Retrieves a list of creatures already spawned at specific anchor points.
    *   **`IsInRightSideGothArea(Unit const* pUnit)`**: Checks if a unit is on the right side of Gothik's arena.
*   **Kel'Thuzad:**
    *   **`OnKTAreaTrigger(AreaTriggerEntry const* pAT)`**: Handles area trigger events specific to Kel'Thuzad's lair.
    *   **`SetChamberCenterCoords(float fX, float fY, float fZ)` / `GetChamberCenterCoords(float &fX, float &fY, float &fZ)`**: Stores and retrieves the center coordinates of Kel'Thuzad's chamber.
    *   **`ToggleKelThuzadWindows(bool setOpen)`**: Opens or closes the four window portals in Kel'Thuzad's room.
*   **General:**
    *   **`onNaxxramasAreaTrigger(Player* pPlayer, AreaTriggerEntry const* pAt)`**: Handles general area triggers in the instance.
    *   **`HandleEvadeOutOfHome(Creature* pWho)`**: Logic to handle creatures evading combat if they move too far from their home position.

### Persistence
*   **`Save()`**: Serializes the current instance state into a string format for storage.
*   **`Load(char const* chrIn)`**: Deserializes the saved state string to restore the instance.

## Cross-Unit Boundaries

*   **Called By:** Individual boss scripts (e.g., `boss_anub_rekhan.cpp`, `boss_kelthuzad.cpp`) call `SetData`, `GetData`, and helper functions to update and query instance state. The core engine calls lifecycle methods like `Initialize`, `Update`, `OnCreatureCreate`, and `Save`.
*   **Calls Out:** Inherits from `ScriptedInstance`. Uses `EventMap` from `Utilities/EventMap.h`. Interacts with core game objects (`Creature`, `GameObject`, `Player`, `Map`).

## Data Model

This unit does not directly interact with database tables via SQL queries. It relies on `Save()` and `Load()` to serialize/deserialize instance state into a string, which is managed by the core engine.

## Notable Implementation Details

1.  **Enum-Based Encounters:** `NAXX_ENCOUNTERS_TYPES` defines indices for `m_auiEncounter`, which stores boss states.
2.  **Gothik's Spawning:** `eyeStalkPossitions` and `GothTrigger` struct provide data for dynamic spawn position calculation.
3.  **Kel'Thuzad Windows:** `GO_KT_WINDOW_*` enums are used in a loop in `ToggleKelThuzadWindows`; changing IDs requires updating that loop.
4.  **Door Types:** Distinction between "Automatic" and "Manual" doors allows flexible encounter design.

## Member Reference

**`instance_naxxramas(Map* pMap)`**: Constructor for the instance script.
**`~instance_naxxramas()`**: Destructor for the instance script.
**`Initialize()`**: Resets instance state to initial conditions.
**`IsEncounterInProgress()`**: Returns true if any encounter is active.
**`OnCreatureCreate(Creature* pCreature)`**: Tracks spawned creatures.
**`OnObjectCreate(GameObject* pGo)`**: Tracks spawned GameObjects.
**`OnCreatureRespawn(Creature * pCreature)`**: Handles creature respawn logic.
**`SetData(uint32 uiType, uint32 uiData)`**: Updates instance state for a specific encounter type.
**`GetData(uint32 uiType)`**: Retrieves instance state for a specific encounter type.
**`GetData64(uint32 uiData)`**: Retrieves a 64-bit GUID for a specific instance element.
**`GetGOUuid(NaxxGOs which)`**: Retrieves the GUID of a specific GameObject by its enum ID.
**`Save()`**: Serializes instance state to a string.
**`Load(char const* chrIn)`**: Deserializes instance state from a string.
**`SetGothTriggers()`**: Initializes Gothik's summon trigger points.
**`GetClosestAnchorForGoth(Creature* pSource, bool bRightSide)`**: Finds the nearest spawn anchor for Gothik's summons.
**`GetGothSummonPointCreatures(std::list<Creature*> &lList, bool bRightSide)`**: Gets creatures at Gothik's summon points.
**`IsInRightSideGothArea(Unit const* pUnit)`**: Checks if a unit is on the right side of Gothik's arena.
**`OnKTAreaTrigger(AreaTriggerEntry const* pAT)`**: Handles Kel'Thuzad-specific area triggers.
**`SetChamberCenterCoords(float fX, float fY, float fZ)`**: Sets the center coordinates of Kel'Thuzad's chamber.
**`GetChamberCenterCoords(float &fX, float &fY, float &fZ)`**: Gets the center coordinates of Kel'Thuzad's chamber.
**`ToggleKelThuzadWindows(bool setOpen)`**: Opens or closes Kel'Thuzad's window portals.
**`OnPlayerDeath(Player* p)`**: Handles player death events.
**`OnCreatureDeath(Creature* pCreature)`**: Handles creature death events.
**`onNaxxramasAreaTrigger(Player* pPlayer, AreaTriggerEntry const* pAt)`**: Handles general area triggers.
**`UpdateAutomaticBossEntranceDoor(NaxxGOs which, uint32 uiData, int requiredPreBossData)`**: Updates entrance door state automatically.
**`UpdateAutomaticBossEntranceDoor(GameObject* pGO, uint32 uiData, int requiredPreBossData)`**: Updates entrance door state automatically.
**`UpdateManualDoor(NaxxGOs which, uint32 uiData)`**: Updates manual door state.
**`UpdateManualDoor(GameObject* pGO, uint32 uiData)`**: Updates manual door state.
**`UpdateBossGate(NaxxGOs which, uint32 uiData)`**: Updates boss gate state.
**`UpdateBossGate(GameObject* pGO, uint32 uiData)`**: Updates boss gate state.
**`UpdateTeleporters(uint32 uiType, uint32 uiData)`**: Updates teleporter states for a wing.
**`SetTeleporterVisualState(GameObject* pGO, uint32 uiData)`**: Sets visual state of a teleporter.
**`SetTeleporterState(GameObject* pGO, uint32 uiData)`**: Sets functional state of a teleporter.
**`GetNumEndbossDead()`**: Returns the number of wing bosses defeated.
**`m_alHeiganTrapGuids[4]`**: Stores GUIDs of Heigan's traps.
**`HandleEvadeOutOfHome(Creature* pWho)`**: Handles creature evasion.
**`OnCreatureEnterCombat(Creature * creature)`**: Handles creature combat entry.
**`WingsAreCleared()`**: Checks if all wing bosses are defeated.
**`m_faerlinaHaveGreeted`**: Flag indicating if Faerlina has greeted players.
**`m_thaddiusHaveGreeted`**: Flag indicating if Thaddius has greeted players.
**`m_haveDoneDKWingIntro`**: Flag indicating if the Death Knight wing intro has played.
**`m_horsemenDeathCounter`**: Counter for Four Horsemen deaths.
**`m_uiHorsemenChestGUID`**: GUID of the Four Horsemen chest.
**`m_auiEncounter[MAX_ENCOUNTER]`**: Array storing encounter states.
**`strInstData`**: String for saving/loading instance data.
**`m_lGothTriggerList`**: List of Gothik's trigger GUIDs.
**`m_mGothTriggerMap`**: Map of Gothik's trigger states.
**`m_fChamberCenterX`**: X coordinate of Kel'Thuzad's chamber center.
**`m_fChamberCenterY`**: Y coordinate of Kel'Thuzad's chamber center.
**`m_fChamberCenterZ`**: Z coordinate of Kel'Thuzad's chamber center.
**`m_events`**: Event map for timed events.
**`Update(uint32 diff)`**: Periodic update handler.

---

<!-- machine-true, projected from graph.json -->

## Map — naxxramas

*Source:* naxxramas.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|

---

<!-- verify: failed-members | invented: GetChamberCenterCoords, GetData, GetData64, GetGOUuid, GetNumEndbossDead, Initialize, IsEncounterInProgress, m_alHeiganTrapGuids[4], m_auiEncounter[MAX_ENCOUNTER], m_events, m_faerlinaHaveGreeted, m_fChamberCenterX, m_fChamberCenterY, m_fChamberCenterZ, m_haveDoneDKWingIntro, m_horsemenDeathCounter, m_lGothTriggerList, m_mGothTriggerMap, m_thaddiusHaveGreeted, m_uiHorsemenChestGUID, Save, SetChamberCenterCoords, SetData, SetGothTriggers, strInstData, ToggleKelThuzadWindows, Update, UpdateBossGate, UpdateManualDoor, UpdateTeleporters, WingsAreCleared, ~instance_naxxramas -->
