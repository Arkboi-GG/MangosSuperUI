# GameObjectData

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# GameObjectData

**Purpose & Responsibilities**

`GameObjectData` is a lightweight aggregate data structure defined in `GameObjectDefines.h` that holds the persistent runtime state for a single `GameObject` instance in the world. It represents the "instance" layer of a game object, distinct from its template definition (`GameObjectInfo`). While `GameObjectInfo` defines *what* an object is (its type, display ID, static properties like lock IDs or loot tables), `GameObjectData` defines *where* it is, *when* it respawns, and *what state* it is currently in (open/closed, active/ready).

This unit is primarily responsible for:
1.  **Spatial State:** Storing the object's position (`WorldLocation`) and orientation (four quaternion components: `rotation0` through `rotation3`).
2.  **Lifecycle State:** Tracking the object's visual and logical state via `go_state` (Active, Ready, Alternative) and `animprogress`.
3.  **Respawn Logic:** Holding the minimum and maximum respawn times (`spawntimesecsmin`, `spawntimesecsmax`) and providing a utility method to calculate a random delay within that range.
4.  **Instance Context:** Storing the instance ID if the object exists within a dungeon or raid instance (`instanciatedContinentInstanceId`).

It contains no complex logic itself, serving instead as a POD-like container that is populated during loading from the database or creation, and updated during gameplay events.

## Member-by-Member Behavior

### **GetRandomRespawnTime**
This inline method calculates a random integer value between `spawntimesecsmin` and `spawntimesecsmax` (inclusive). It utilizes the global helper `urand` to generate this value. This value represents the time in seconds before the game object should respawn after being destroyed or consumed.

*   **Logic:** It casts the signed integers `spawntimesecsmin` and `spawntimesecsmax` to `uint32` before passing them to `urand`. This implies that negative values in the database would result in undefined or large unsigned values, though typical configuration ensures these are positive.
*   **Usage:** This is the standard mechanism for determining respawn delays for consumable objects (like chests or goobers) that are configured with a time-based respawn rather than a fixed one.

## Cross-Unit Boundaries

`GameObjectData` interacts with several core subsystems to manage the lifecycle and persistence of game objects.

### **Called By: Persistence and Initialization**
*   **`GameObject/LoadFromDB`**: When a game object is loaded from the `gameobject` database table, `LoadFromDB` populates a `GameObjectData` struct with the stored position, rotation, state, and respawn times. It then calls `GetRandomRespawnTime` to initialize the object's internal respawn timer if the object is currently despawned.
*   **`GameObject/Despawn`**: When an object is despawned (either manually or due to consumption), this method likely updates the `GameObjectData` state (setting `go_state` to `GO_STATE_READY` or similar) and may trigger a respawn timer calculation using `GetRandomRespawnTime`.

### **Called By: Spawning Systems**
*   **`PoolManager/Spawn1Object#2`**: The pool manager uses `GameObjectData` to track objects that are part of a spawn pool. It likely calls `GetRandomRespawnTime` to stagger the respawn of pooled objects to prevent them from all appearing simultaneously.
*   **`felwood/PlantQuestRewarded`**: Specific script logic in the Felwood zone uses this method to determine when a plant-related game object should reappear after a quest reward is granted.

### **Called By: AI and Event Scripts**
*   **`go_scripts/UpdateAI#5`**: Generic game object scripts may query the respawn time to synchronize animations or events with the object's lifecycle.
*   **`ThreatListCopier.battleground_alterac/go_av_landmineAI`**: In the Alterac Valley battleground, landmine AI logic uses this method to determine when a triggered mine should become active again or respawn, ensuring balanced gameplay dynamics.

## Data Model

`GameObjectData` corresponds directly to the `gameobject` table in the database. Although the schema is not explicitly provided in the prompt, the struct fields map to the following standard columns:

| Struct Field | Database Column (Implied) | Description |
| :--- | :--- | :--- |
| `id` | `guid` / `entry` | The unique identifier for the instance and the template entry. |
| `position` | `map`, `spawnPosX`, `spawnPosY`, `spawnPosZ` | The spatial coordinates of the object. |
| `rotation0` - `rotation3` | `orientation` (split into quaternion components) | The orientation of the object. Note: DB often stores a single float orientation, which is converted to quaternion components in memory. |
| `spawntimesecsmin` | `spawntime` | Minimum respawn time in seconds. |
| `spawntimesecsmax` | `spawntime` (or derived) | Maximum respawn time. Often the same as min in older DB schemas, but the struct supports a range. |
| `animprogress` | `animProgress` | Current animation progress (0-100). |
| `go_state` | `state` | Current state (0=Active, 1=Ready, 2=Alternative). |
| `spawn_flags` | `spawnMask` | Flags controlling visibility in different instances/difficulties. |
| `visibility_mod` | `visibilityDistanceType` | Modifier for visibility distance. |
| `instanciatedContinentInstanceId` | `InstanceId` | The ID of the instance map if applicable. |

## Notable Implementation Details

1.  **Quaternion Storage**: The struct stores rotation as four separate floats (`rotation0` to `rotation3`). This suggests an internal representation using quaternions for smooth interpolation and avoidance of gimbal lock, even if the database stores a single Euler angle. The `QuaternionData` struct defined nearby supports this interpretation.
2.  **Signed vs. Unsigned Respawn Times**: The fields `spawntimesecsmin` and `spawntimesecsmax` are `int32`, but `GetRandomRespawnTime` casts them to `uint32`. This is a potential edge case: if a database entry has a negative respawn time (invalid), the cast will result in a very large positive number, potentially causing the object to never respawn. Maintainers should ensure database integrity.
3.  **Inline Randomization**: `GetRandomRespawnTime` is an inline method that calls `urand`. This means the random seed and generation logic are handled globally. This ensures that respawn times are not deterministic across server restarts unless the global RNG seed is fixed.
4.  **No Virtual Functions**: As a simple struct, `GameObjectData` has no virtual functions, making it cheap to copy and pass around. This is appropriate for its role as a data holder rather than a behavioral entity.

## Member Reference

**GetRandomRespawnTime**
Returns a random integer between `spawntimesecsmin` and `spawntimesecsmax` (cast to `uint32`) using the `urand` helper. Used to determine the delay before a despawned game object respawns. Called by `GameObject/LoadFromDB`, `GameObject/Despawn`, `PoolManager/Spawn1Object#2`, `felwood/PlantQuestRewarded`, `go_scripts/UpdateAI#5`, and `ThreatListCopier.battleground_alterac/go_av_landmineAI`.

---

<!-- machine-true, projected from graph.json -->

## Map — GameObjectData

*Source:* GameObjectDefines.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GetRandomRespawnTime | method | — | felwood/PlantQuestRewarded, GameObject/Despawn, GameObject/LoadFromDB, go_scripts/UpdateAI#5, PoolManager/Spawn1Object#2, ThreatListCopier.battleground_alterac/go_av_landmineAI | — |
