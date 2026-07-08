# custom

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# custom

## Purpose & Responsibilities

The `custom` translation unit serves as the registration entry point for the "ScriptDevZero" custom script suite within the WoWVMaNGOS server framework. Its sole responsibility is to expose a single function, `AddSC_zero_scripts`, which aggregates the registration of specific custom creature scripts. This unit acts as a bridge between the core server's script loading mechanism and the specialized creature behaviors defined in other modules. It does not contain game logic, AI implementations, or data processing; it strictly manages the lifecycle initialization of the custom script subsystem.

## Member-by-Member Behavior

### `AddSC_zero_scripts`

This function is the primary public interface of the unit. When invoked, it triggers the registration of custom creature scripts by calling `AddSC_custom_creatures()` (defined in the `custom_creatures` unit). This design allows the core server to register all "zero" scripts (a legacy naming convention from ScriptDevZero) by invoking a single known symbol, rather than requiring knowledge of every individual script module.

## Cross-Unit Boundaries

*   **Called by `ScriptLoader`**: The core server component `ScriptLoader` invokes `AddSC_zero_scripts` during the server startup sequence. This call signals the server to load all scripts associated with the "zero" namespace. The direction of control flow is from the core loader into this unit.
*   **Calls into `custom_creatures`**: Inside `AddSC_zero_scripts`, the unit calls `AddSC_custom_creatures`. This delegates the actual registration of creature-specific scripts to the `custom_creatures` unit. This separation ensures that creature logic is modularized, allowing `custom_creatures` to be updated or replaced without modifying the central registration hook in `custom`.

## Data Model

This unit does not interact with any database tables. It performs no SQL queries, inserts, updates, or deletes. Its operation is entirely in-memory and occurs during the server initialization phase.

## Notable Implementation Details

*   **Legacy Naming Convention**: The function name `AddSC_zero_scripts` and the header comment referencing "ScriptDevZero" indicate that this code originates from the ScriptDevZero project, a popular open-source scripting framework for MaNGOS/WowServer. The "zero" suffix distinguishes these scripts from other potential script sets (e.g., "custom", "elite", etc.) that might exist in a larger codebase.
*   **Minimalist Design**: The implementation contains no error handling, logging, or conditional checks. It assumes that `AddSC_custom_creatures` is always available and safe to call. This reflects the typical pattern in MaNGOS-derived servers where script registration functions are expected to be side-effect-free and idempotent during the loading phase.
*   **Header Declaration**: The header `custom.h` declares both `AddSC_custom_creatures` and `AddSC_zero_scripts`. While `AddSC_zero_scripts` is implemented in `custom.cpp`, `AddSC_custom_creatures` is declared here likely for convenience or historical reasons, though its definition resides elsewhere. This unit relies on the linker to resolve the `AddSC_custom_creatures` symbol at link time.

## Member Reference

**AddSC_zero_scripts**
A void function that serves as the aggregate registration hook for the ScriptDevZero custom scripts. It calls `AddSC_custom_creatures()` from the `custom_creatures` unit to ensure all custom creature scripts are registered with the server's script system. It is invoked by the `ScriptLoader` unit during server startup.

---

<!-- machine-true, projected from graph.json -->

## Map — custom

*Source:* custom.cpp, custom.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AddSC_zero_scripts | function | custom_creatures/AddSC_custom_creatures | ScriptLoader/AddScripts | — |
