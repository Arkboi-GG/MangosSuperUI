# Register

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Register

**Purpose & Responsibilities**

`Register.cpp` provides the registration hook for zone-specific scripts related to outdoor player-versus-player (PvP) systems. Specifically, it conditionally registers the script modules for Eastern Plaguelands (`ep`) and Silverpine Forest (`si`). This unit acts as a gatekeeper, ensuring these scripts are only loaded if the server is configured to support them and if the client version being emulated is sufficiently modern (patch 1.12 or later). It does not contain the logic for the PvP systems themselves; it merely invokes their respective registration functions.

## Member-by-Member Behavior

### **RegisterZoneScripts**

This function is the entry point for registering outdoor PvP zone scripts. Its behavior is governed by two layers of conditions: compile-time macros and runtime configuration.

1.  **Compile-Time Check**: The entire body of the function is wrapped in `#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_11_2`. This means the code is only compiled if the server build supports client versions newer than 1.11.2. If the server is built strictly for older patches, this function effectively becomes empty.
2.  **Runtime Patch Check**: Inside the block, it checks `sWorld.GetWowPatch() >= WOW_PATCH_1_12`. This ensures that even if the server *can* run newer clients, the specific scripts are only registered if the current world instance is set to emulate patch 1.12 or higher. This is likely because the mechanics or zones associated with these scripts did not exist or were different in earlier patches.
3.  **Configuration Checks**:
    *   It checks `sWorld.getConfig(CONFIG_BOOL_OUTDOORPVP_EP_ENABLE)` to determine if the Eastern Plaguelands outdoor PvP system is enabled. If true, it calls `AddSC_outdoorpvp_ep()`.
    *   It checks `sWorld.getConfig(CONFIG_BOOL_OUTDOORPVP_SI_ENABLE)` to determine if the Silverpine Forest outdoor PvP system is enabled. If true, it calls `AddSC_outdoorpvp_si()`.

The function relies on forward declarations for `AddSC_outdoorpvp_ep()` and `AddSC_outdoorpvp_si()`, which are defined in other units (`OutdoorPvPEP` and `OutdoorPvPSI` respectively).

## Cross-Unit Boundaries

*   **Calls Out**:
    *   `OutdoorPvPEP/AddSC_outdoorpvp_ep`: Called if Eastern Plaguelands outdoor PvP is enabled. This function registers the specific scripts and handlers for the EP zone.
    *   `OutdoorPvPSI/AddSC_outdoorpvp_si`: Called if Silverpine Forest outdoor PvP is enabled. This function registers the specific scripts and handlers for the SI zone.
    *   `World/getConfig`: Called twice to retrieve boolean configuration flags (`CONFIG_BOOL_OUTDOORPVP_EP_ENABLE` and `CONFIG_BOOL_OUTDOORPVP_SI_ENABLE`) from the global `sWorld` singleton.
    *   `World/GetWowPatch`: Called to retrieve the current WoW patch version being emulated by the server.

*   **Called By**:
    *   `ZoneScriptMgr/InitZoneScripts`: This manager is responsible for initializing all zone-related scripts during server startup. It calls `RegisterZoneScripts` to ensure the outdoor PvP scripts are registered alongside other zone scripts.

## Data Model

This unit does not interact with any database tables. It operates entirely on runtime configuration and client version checks.

## Notable Implementation Details

*   **Conditional Compilation**: The use of `#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_11_2` indicates that these outdoor PvP features are considered incompatible with or unnecessary for client builds prior to 1.12. Maintainers should be aware that changing the supported client build range in the project settings will affect whether this code is included in the binary.
*   **Order of Checks**: The function checks the patch version first, then the configuration. This implies that even if a user enables the config option for an older patch, the scripts will not register because the patch check fails. This prevents potential crashes or undefined behavior if the scripts rely on mechanics introduced in 1.12.
*   **Forward Declarations**: The `AddSC_*` functions are declared but not defined here. This keeps the registration logic decoupled from the actual script implementations, promoting modularity.

## Member Reference

**RegisterZoneScripts**
A void function that conditionally registers outdoor PvP scripts for Eastern Plaguelands and Silverpine Forest. It first checks a compile-time macro (`SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_11_2`). If compiled, it checks at runtime if the server's emulated patch is 1.12 or higher (`sWorld.GetWowPatch()`). If so, it checks two configuration booleans (`CONFIG_BOOL_OUTDOORPVP_EP_ENABLE` and `CONFIG_BOOL_OUTDOORPVP_SI_ENABLE`) via `sWorld.getConfig()`. If a config is enabled, it calls the corresponding registration function (`AddSC_outdoorpvp_ep()` or `AddSC_outdoorpvp_si()`). This function is called by `ZoneScriptMgr::InitZoneScripts` during server initialization.

---

<!-- machine-true, projected from graph.json -->

## Map — Register

*Source:* Register.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| RegisterZoneScripts | function | OutdoorPvPEP/AddSC_outdoorpvp_ep, OutdoorPvPSI/AddSC_outdoorpvp_si, World/getConfig, World/GetWowPatch | ZoneScriptMgr/InitZoneScripts | — |
