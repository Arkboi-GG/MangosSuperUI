# GameObjectDefines

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# GameObjectDefines

**Purpose & Responsibilities**

`GameObjectDefines.h` is a foundational definition header for the **Game Object (GO)** subsystem within the WoWVMaNGOS server. It does not contain executable logic beyond inline helper functions; instead, it provides the static data structures, enumerations, and constants that define what a Game Object is, how it behaves, and how its data is stored and interpreted.

Its primary responsibilities are:
1.  **Defining Game Object Types:** Enumerating every distinct category of interactive object in the world (doors, chests, quests, traps, transports, etc.).
2.  **Structuring Static Template Data:** Providing the `GameObjectInfo` struct, which maps directly to the `gameobject_template` database table. This struct uses a large `union` to hold type-specific configuration data (e.g., a door has `lockId` and `autoCloseTime`, while a chest has `lootId` and `consumable`).
3.  **Structuring Dynamic Instance Data:** Providing the `GameObjectData` struct, which maps to the `gameobject` database table, holding instance-specific state like position, rotation, spawn times, and current state (`GO_STATE_ACTIVE`, etc.).
4.  **Providing Helper Accessors:** Offering inline methods within `GameObjectInfo` to safely query properties across different GO types (e.g., `GetLockId()`, `IsUsableMounted()`), abstracting away the complexity of the underlying union.
5.  **Defining Flags and States:** Establishing the bitmasks for interaction flags (`GO_FLAG_*`) and dynamic flags (`GO_DYNFLAG_*`), as well as the lifecycle states of a GO (`GOState`, `LootState`).

This unit is purely declarative. It contains no database queries, no network packet handling, and no simulation logic. It serves as the contract between the database schema, the client protocol, and the server-side logic that manipulates Game Objects.

---

## Member-by-Member Behavior

The unit consists of two standalone functions and several complex data structures. The functions are simple string converters for debugging/logging purposes. The structures define the core data model.

### Conversion Functions

*   **`GameObjectFlagToString`**: Takes a `uint32` flag value from the `GameObjectFlags` enum and returns a human-readable `const char*`. It handles known flags (`IN_USE`, `LOCKED`, `TRANSPORT`, etc.) and returns `"UNKNOWN"` for unrecognized bits. This is used by other units to log or display the current status of a GO.
*   **`GameObjectDynamicFlagToString`**: Similar to the above, but converts `GameObjectDynamicLowFlags` (such as `ACTIVATE`, `ANIMATE`) into strings.

### Data Structures

#### `GameobjectTypes` Enum
Defines the integer IDs for every type of Game Object. These IDs correspond to the `type` column in `gameobject_template`. The enum includes conditional compilation blocks (`#if SUPPORTED_CLIENT_BUILD > ...`) to ensure compatibility with specific World of Warcraft client versions (e.g., `GAMEOBJECT_TYPE_FLAGSTAND` only exists for builds after 1.5.1).

#### `GameObjectFlags` Enum
Bitmask flags that control interaction and visual state.
*   `GO_FLAG_IN_USE`: Prevents interaction during animations.
*   `GO_FLAG_LOCKED`: Requires a key/spell to open.
*   `GO_FLAG_NO_INTERACT`: Completely disables player interaction.
*   `GO_FLAG_NODESPAWN`: Prevents the object from despawning after use (common for doors).
*   `GO_FLAG_TRIGGERED`: Indicates the GO was spawned by a spell/event rather than being a static world object.

#### `GameObjectDynamicLowFlags` Enum
Flags sent to the client to control visual feedback.
*   `GO_DYNFLAG_LO_ACTIVATE`: Highlights the GO to indicate it can be used.
*   `GO_DYNFLAG_LO_ANIMATE`: Triggers a specific animation state.

#### `GameObjectActions` Enum
Defines the actions that can be performed on a GO (e.g., `Open`, `Close`, `Destroy`, `Unlock`). These are likely used by script systems or event handlers to programmatically change a GO's state.

#### `GOState` Enum
Defines the visual state of a GO as seen by the client:
*   `GO_STATE_ACTIVE`: Used/Opened (e.g., an open door).
*   `GO_STATE_READY`: Unused/Closed (e.g., a closed door).
*   `GO_STATE_ACTIVE_ALTERNATIVE`: A variant active state (e.g., a door blown open by a cannon).

#### `LootState` Enum
Tracks the internal lifecycle of lootable objects (chests, bobbers):
*   `GO_NOT_READY`: Initial state.
*   `GO_READY`: Available for interaction.
*   `GO_ACTIVATED`: Currently being looted/opened.
*   `GO_JUST_DEACTIVATED`: Finished interaction, pending respawn or despawn.

#### `GameObjectInfo` Struct
The most critical structure in this file. It represents the **template** data for a GO, loaded from `gameobject_template`.
*   **Header Fields:** `id`, `type`, `displayId`, `name`, `icon`, `faction`, `flags`, `size`.
*   **Union `data`:** Contains nested structs for each `GameobjectType`. Only the struct matching the `type` field is valid. For example, if `type` is `GAMEOBJECT_TYPE_DOOR`, only the `door` struct fields (`startOpen`, `lockId`, `autoCloseTime`, etc.) are meaningful.
*   **Footer Fields:** `MinMoneyLoot`, `MaxMoneyLoot`, `ScriptId`.
*   **Helper Methods:** Inline functions that switch on `type` to provide uniform access to heterogeneous data.
    *   `IsDespawnAtAction()`: Returns true if the GO should disappear after use (e.g., consumable chests).
    *   `IsUsableMounted()`: Checks if the GO can be interacted with while the player is mounted.
    *   `GetLockId()`: Retrieves the lock ID for any lockable GO type.
    *   `CannotBeUsedUnderImmunity()`: Determines if immunity effects (like Divine Shield) prevent interaction. Note that `GAMEOBJECT_TYPE_CHEST` always returns `true` here, reflecting a specific game rule for version 3.3.5a.
    *   `GetAutoCloseTime()`: Calculates the auto-close delay by dividing the stored value by `0x10000`.
    *   `GetInteractionDistance()`: Returns the maximum distance a player can stand to interact with the GO. Most types use the global `INTERACTION_DISTANCE`, but specific types like `CHAIR` (100.0f) or `QUESTGIVER` (5.55556f) have overrides.

#### `GameObjectData` Struct
Represents the **instance** data for a GO, loaded from the `gameobject` table.
*   **Position/Orientation:** `position` (WorldLocation), `rotation0`–`rotation3` (quaternion components).
*   **Spawn Logic:** `spawntimesecsmin`, `spawntimesecsmax` define the respawn window.
*   **State:** `animprogress`, `go_state`, `spawn_flags`, `visibility_mod`.
*   **Instance Context:** `instanciatedContinentInstanceId` links the GO to a specific dungeon/raid instance.
*   **Helper Methods:**
    *   `ComputeRespawnDelay(baseDelay)`: Likely applies modifiers to the base delay (implementation not shown in this header, but declared).
    *   `GetRandomRespawnTime()`: Returns a random integer between `spawntimesecsmin` and `spawntimesecsmax`.

#### `GameObjectDisplayInfoAddon` Struct
Contains bounding box data (`min_x`, `max_z`, etc.) for a GO's display model, likely used for collision detection or visual culling.

#### `QuaternionData` Struct
A utility structure for handling 3D rotations.
*   Stores `x, y, z, w` components.
*   Provides constructors and methods to convert between quaternions and Euler angles (`toEulerAnglesZYX`, `fromEulerAnglesZYX`).
*   `isUnit()`: Checks if the quaternion is normalized.

#### `GameObjectLocale` Struct
Holds localized names for a GO, stored as a vector of strings indexed by locale ID.

---

## Cross-Unit Boundaries

As a definition header, `GameObjectDefines.h` has no outgoing calls to other units. It is included by virtually every unit that deals with Game Objects.

*   **Called By:**
    *   **GameObject.cpp / GameObject.h:** Uses `GameObjectInfo` and `GameObjectData` to manage the lifetime and state of GO instances.
    *   **DatabaseLoaders:** Units responsible for loading `gameobject_template` and `gameobject` tables will populate these structs.
    *   **Script Systems:** Scripts interacting with GOs will use `GameObjectActions` and `GameObjectFlags`.
    *   **Packet Handlers:** Network code will use `GOState` and `GameObjectFlags` to serialize/deserialize GO updates to clients.
    *   **Debug/Logging Tools:** Use `GameObjectFlagToString` and `GameObjectDynamicFlagToString` to print readable states.

*   **Collaboration:**
    *   The `GameObjectInfo` struct acts as the bridge between the database layer (which reads raw rows) and the logic layer (which needs typed accessors like `GetLockId()`).
    *   The `QuaternionData` struct is likely used by the movement or positioning systems to handle GO orientation, collaborating with math utilities in `Common.h` or `Math.h`.

---

## Data Model

This unit defines the C++ structures that mirror two specific database tables. No SQL is executed here, but the struct layouts dictate how data is interpreted.

1.  **`gameobject_template`**
    *   Mapped to **`GameObjectInfo`**.
    *   Columns such as `entry`, `type`, `displayId`, `name`, `flags`, and the type-specific `data0`–`data23` fields are mapped to the struct members.
    *   The `union` in `GameObjectInfo` reflects the fact that `gameobject_template` stores type-specific data in a generic set of columns (`data0` through `data23`), which must be interpreted differently depending on the `type`.

2.  **`gameobject`**
    *   Mapped to **`GameObjectData`**.
    *   Columns such as `guid`, `id`, `map`, `phaseMask`, `spawnMask`, `position_x/y/z`, `orientation`, `rotation0-3`, `spawntimesecs`, `animprogress`, `state`, and `instanceId` are mapped to this struct.
    *   Note: The struct splits `spawntimesecs` into `spawntimesecsmin` and `spawntimesecsmax`, suggesting the database might store a single value that is expanded, or the struct supports a range not directly visible in a simple column dump. However, `GetRandomRespawnTime()` implies a range is supported.

3.  **`gameobject_locale`**
    *   Mapped to **`GameObjectLocale`**.
    *   Stores localized names for each GO entry.

---

## Notable Implementation Details

1.  **Union-Based Type Safety:**
    The `GameObjectInfo` struct uses a `union` to store type-specific data. This is a memory-efficient design but requires strict discipline: the `type` field *must* match the active struct within the union. The helper methods (e.g., `GetLockId()`) enforce this by switching on `type`. If a developer accesses `chest.lockId` when `type` is `GAMEOBJECT_TYPE_DOOR`, the behavior is undefined (reading garbage memory). The helper methods mitigate this risk.

2.  **Client Build Compatibility:**
    The header extensively uses `#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_X_Y_Z` to include/exclude GO types and fields. This allows the server to compile for multiple WoW versions (e.g., Classic, TBC, WotLK) from the same codebase. Developers must ensure that the `type` enum values remain consistent or properly offset across builds to avoid deserialization errors.

3.  **Auto-Close Time Calculation:**
    In `GetAutoCloseTime()`, the raw value is divided by `0x10000` (65536). This suggests the database stores the time in a fixed-point format or milliseconds scaled by a factor. The comment `secs till autoclose = autoCloseTime / 0x10000` confirms this. This is a non-obvious detail that could lead to bugs if developers assume the stored value is seconds.

4.  **Immunity Handling:**
    The `CannotBeUsedUnderImmunity()` method has a hardcoded rule: `GAMEOBJECT_TYPE_CHEST` always returns `true`. This means chests cannot be looted while immune, regardless of the `noDamageImmune` flag. This is a specific game mechanic for the supported client version (3.3.5a) and differs from other types like doors or buttons, which check the `noDamageImmune` flag.

5.  **Interaction Distance Overrides:**
    Most GOs use the global `INTERACTION_DISTANCE`. However, `GetInteractionDistance()` hardcodes specific distances for certain types:
    *   `QUESTGIVER`, `TEXT`, `FLAGSTAND`, `FLAGDROP`, `MINI_GAME`: 5.55556f
    *   `BINDER`: 10.0f
    *   `CHAIR`, `FISHINGNODE`: 100.0f (likely to allow sitting/fishing from afar)
    *   `AREADAMAGE`: 0.0f (cannot be interacted with directly)
    These overrides are critical for gameplay feel and must be maintained if new GO types are added.

6.  **Quaternion Utilities:**
    The `QuaternionData` struct provides basic conversion between quaternions and Euler angles (ZYX order). This is essential for converting database-stored rotations (often Euler) into engine-friendly quaternions for interpolation and collision checks. The `isUnit()` method allows validation of rotation data integrity.

7.  **Pack Alignment:**
    The `GameObjectInfo` struct is wrapped in `#pragma pack(1)`. This ensures that the struct's memory layout matches the exact byte sequence expected from the database or client packets, preventing padding issues that could cause misinterpretation of data. This is crucial for binary compatibility.

---

## Member Reference

**`GameObjectFlagToString`**
Inline function that converts a `GameObjectFlags` bitmask value into a human-readable string (e.g., `"Locked"`, `"In Use"`). Returns `"UNKNOWN"` for unrecognized flags. Used for logging and debugging.

**`GameObjectDynamicFlagToString`**
Inline function that converts a `GameObjectDynamicLowFlags` bitmask value into a human-readable string (e.g., `"Activate"`, `"Animate"`). Returns `"UNKNOWN"` for unrecognized flags. Used for logging and debugging.

---

<!-- machine-true, projected from graph.json -->

## Map — GameObjectDefines

*Source:* GameObjectDefines.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GameObjectFlagToString | function | — | — | — |
| GameObjectDynamicFlagToString | function | — | — | — |
