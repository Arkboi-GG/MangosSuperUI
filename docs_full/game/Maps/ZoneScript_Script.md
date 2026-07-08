# ZoneScript_Script

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ZoneScript_Script

**ZoneScript_Script** is an abstract base class that defines the interface for registering zone-specific scripts within the `wowvmangos` server. It serves as a factory-like registration point, allowing specific zone implementations (such as Eastern Plaguelands or Silithus) to provide a `ZoneScript` instance and identify the map ID they govern. This class does not contain logic itself; it enforces a contract that derived classes must implement `GetZoneScript()` and `GetMapId()`.

## Purpose & Responsibilities

The primary responsibility of `ZoneScript_Script` is to act as a standardized wrapper for zone scripts. The server uses this interface to dynamically load and manage scripts associated with specific maps. By defining a common interface, the `ZoneScriptMgr` can iterate over a collection of these script objects to initialize zone behaviors without needing hard-coded dependencies on specific zone implementations.

It is strictly an abstract interface:
1.  **Identity**: It identifies which map (`uint32`) the script applies to via `GetMapId()`.
2.  **Factory**: It provides access to the actual logic handler (`ZoneScript*`) via `GetZoneScript()`.

## Member-by-Member Behavior

### Construction and Destruction
*   **`ZoneScript_Script()`**: The default constructor is empty. It performs no initialization.
*   **`~ZoneScript_Script()`**: The destructor is virtual and empty. It ensures proper cleanup when deleting derived classes through a base pointer, though no resources are managed by this base class.

### Interface Methods
*   **`GetZoneScript()`**: A pure virtual function that returns a pointer to a `ZoneScript` object. Derived classes must implement this to return the specific script instance responsible for handling events in their designated zone.
*   **`GetMapId()`**: A pure virtual function that returns a `uint32` representing the Map ID. This allows the system to associate the script with a specific game world map.

## Cross-Unit Boundaries

### Called By
*   **`OutdoorPvPEP/OutdoorPvP_eastern_plaguelands`**: The Eastern Plaguelands Outdoor PvP implementation derives from `ZoneScript_Script`. It implements the interface to register its specific PvP logic for the Eastern Plaguelands map.
*   **`OutdoorPvPSI/OutdoorPvP_silithus`**: The Silithus Outdoor PvP implementation derives from `ZoneScript_Script`. It implements the interface to register its specific PvP logic for the Silithus map.

These units rely on `ZoneScript_Script` to integrate their specialized logic into the general zone management framework. They do not call methods *on* `ZoneScript_Script` (since it is their base), but they *are* instances of it, fulfilling the contract defined here.

### Calls Out
This unit does not call any other units. It is a passive interface definition.

## Data Model

This unit does not interact with any database tables. It operates entirely in memory, managing pointers to script objects and map IDs.

## Notable Implementation Details

*   **Abstract Nature**: The class contains no data members. It exists solely to define the `GetZoneScript` and `GetMapId` interface. Any attempt to instantiate `ZoneScript_Script` directly will result in a compilation error.
*   **Virtual Destructor**: The destructor is marked `virtual`, which is critical for polymorphic deletion. Since `ZoneScriptMgr` stores pointers to `ZoneScript_Script` in its internal collections (`m_ZoneScripts_Scripts`), deleting these pointers requires the virtual destructor to correctly invoke the destructors of derived classes like `OutdoorPvPEP`.
*   **Const Correctness**: Both `GetZoneScript()` and `GetMapId()` are declared `const`, indicating that querying the script instance or map ID does not modify the state of the `ZoneScript_Script` object.

## Member Reference

**ZoneScript_Script**
Default constructor. Performs no initialization.

**~ZoneScript_Script**
Virtual destructor. Performs no cleanup but ensures proper polymorphic deletion of derived classes.

**GetZoneScript**
Pure virtual function. Returns a pointer to the `ZoneScript` instance associated with this registration. Must be implemented by derived classes.

**GetMapId**
Pure virtual function. Returns the `uint32` Map ID that this script applies to. Must be implemented by derived classes.

---

<!-- machine-true, projected from graph.json -->

## Map — ZoneScript_Script

*Source:* ZoneScriptMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ZoneScript_Script | ctor | — | OutdoorPvPEP/OutdoorPvP_eastern_plaguelands, OutdoorPvPSI/OutdoorPvP_silithus | — |
| ~ZoneScript_Script | dtor | — | — | — |
| GetZoneScript | decl | — | ZoneScriptMgr/InitMapZoneScripts | — |
| GetMapId | decl | — | ZoneScriptMgr/InitMapZoneScripts | — |
