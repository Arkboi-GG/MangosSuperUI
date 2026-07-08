# ScriptAction

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ScriptAction

**Purpose & Responsibilities**

`ScriptAction` is a lightweight data structure defined in `ScriptCommands.h` that represents a single, instantiated step within the execution of a scripted event in the world. It acts as the runtime context for a specific command (defined by `eScriptCommand`) that has been resolved from static configuration data (`ScriptInfo`).

Its primary responsibility is to hold the dynamic resolution of **who** is acting (`sourceGuid`) and **who** is being acted upon (`targetGuid`) for a specific script command, alongside a pointer to the static definition of that command (`script`). This separation allows the scripting system to load commands from the database once (into `ScriptInfo`) and then instantiate them multiple times with different sources and targets during runtime.

The unit contains exactly one member function, `IsSameScript`, which provides identity comparison logic for these actions. This is critical for the script engine to determine if a newly generated action is redundant or identical to one already processed or queued, preventing infinite loops or duplicate executions of the same logical step.

## Member-by-Member Behavior

### Identity Comparison

**`IsSameScript`**
This method determines whether the current `ScriptAction` instance is logically equivalent to another potential action described by the provided arguments (`id`, `sourceGuid`, `targetGuid`).

It performs a three-part check:
1.  **Script ID Match:** It verifies that the provided `id` matches the `id` stored in the static `ScriptInfo` pointed to by the current action's `script` member. This ensures we are comparing actions belonging to the same script definition.
2.  **Source GUID Match:** It checks if the provided `sourceGuid` matches the current action's `sourceGuid`. However, it includes a null-check optimization: if the provided `sourceGuid` is empty (null), this part of the check passes regardless of the current action's source. This allows callers to query for "any action with this ID and target" without specifying a source.
3.  **Target GUID Match:** Similarly, it checks if the provided `targetGuid` matches the current action's `targetGuid`, passing if the provided target is empty.

The method returns `true` only if all applicable conditions are met. This logic supports flexible querying where partial matches (e.g., matching by ID and Target only) are considered "the same script" for the purposes of termination or deduplication.

## Cross-Unit Boundaries

`ScriptAction` is a pure data structure with minimal behavior. Its interactions are strictly limited to identity verification requested by the core script execution engine.

*   **Called by `Map.Main` and `Map.TerminateScript`:**
    The `Map` unit (specifically its main execution loop and script termination logic) calls `IsSameScript` to manage the lifecycle of scripts.
    *   **Direction:** Data flows from `Map` into `ScriptAction`. `Map` provides the candidate `id`, `sourceGuid`, and `targetGuid` to compare against the current `ScriptAction`.
    *   **Why:** When a script terminates or when the engine needs to check if a specific script instance is already running or has been completed, it uses `IsSameScript` to identify the correct `ScriptAction` instances among potentially many concurrent scripts. This prevents the engine from terminating unrelated scripts or failing to terminate the intended one due to GUID mismatches.

## Data Model

This unit does not interact directly with any database tables. It operates entirely on in-memory objects (`ObjectGuid`, `ScriptInfo`). The `ScriptInfo` structure it references is populated from database tables (likely `script_texts`, `creature_ai_scripts`, or similar custom script tables depending on the specific WowVMaNGOS implementation), but `ScriptAction` itself performs no SQL queries or direct table access.

## Notable Implementation Details

1.  **Null-GUID Flexibility:** The logic `(sourceGuid == this->sourceGuid || !sourceGuid)` is a deliberate design choice. In C++, `ObjectGuid` typically evaluates to false if it is empty/null. This allows `Map.TerminateScript` to pass an empty GUID if it wants to match *all* instances of a script ID, or a specific GUID if it wants to target a specific actor. This flexibility is crucial for scripts that might spawn multiple copies of themselves or need to clean up all instances of a specific script type.
2.  **Const Correctness:** `IsSameScript` is marked `const`, indicating it does not modify the state of the `ScriptAction`. This is consistent with its role as a pure comparison function.
3.  **Dependency on `ScriptInfo`:** The method accesses `script->id`. It assumes `script` is a valid pointer. If `script` were null, this would cause a crash. The caller (`Map`) is responsible for ensuring `ScriptAction` instances have valid `script` pointers before calling this method.
4.  **No Side Effects:** The function has no side effects. It reads memory and returns a boolean. This makes it safe to call repeatedly during iteration over lists of active scripts.

## Member Reference

**IsSameScript**: Compares the current `ScriptAction` against a provided script ID, source GUID, and target GUID. Returns `true` if the script IDs match and the provided GUIDs match the action's GUIDs (or if the provided GUIDs are empty/null). Used by `Map.Main` and `Map.TerminateScript` to identify specific script instances for execution control or cleanup.

---

<!-- machine-true, projected from graph.json -->

## Map — ScriptAction

*Source:* ScriptCommands.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| IsSameScript | method | — | Map.Main/TerminateScript | — |
