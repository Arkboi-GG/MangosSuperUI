# CreatureCreatePos

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# CreatureCreatePos

**CreatureCreatePos** is a lightweight helper struct defined in `Creature.h` within the MaNGOS/WowVMaNGOS server codebase. Its sole responsibility is to encapsulate the logic required to determine the final spawn coordinates (`Position`) for a `Creature` instance before it is added to the world. It supports two distinct initialization modes: absolute positioning (explicit X, Y, Z, Orientation) and relative positioning (offset from an existing `WorldObject`).

This unit does not manage state over time; it is constructed, configured, and consumed during the creature creation process. The actual calculation of the final coordinates occurs lazily via `SelectFinalPoint`, allowing the caller to defer the computation until the target `Creature` instance exists and can provide context (such as collision checks or specific offset adjustments).

## Purpose & Responsibilities

The primary purpose of **CreatureCreatePos** is to abstract the complexity of calculating spawn positions away from the callers (spells, commands, summoning logic). It handles:
1.  **Storage**: Holding either absolute coordinates or a reference to a nearby object and offset parameters.
2.  **Map Resolution**: Determining the correct `Map` instance for the spawn, either directly provided or derived from the reference object.
3.  **Coordinate Calculation**: Converting relative offsets (distance and angle from a reference object) into absolute world coordinates, respecting the orientation of the reference object.
4.  **Validation**: Providing a mechanism (`Relocate`) to verify if the calculated position is valid for the specific `Creature` being spawned.

## Member-by-Member Behavior

### Construction Modes

The struct provides two constructors, representing the two supported spawn strategies.

#### **CreatureCreatePos** (Absolute Coordinates)
*   **Signature**: `CreatureCreatePos(Map* map, float x, float y, float z, float o)`
*   **Behavior**: Initializes the struct with explicit world coordinates.
    *   Stores the provided `Map*` in `m_map`.
    *   Sets `m_closeObject` to `nullptr`, indicating no reference object is involved.
    *   Sets `m_angle` and `m_dist` to `0.0f`, as these are irrelevant for absolute positioning.
    *   Directly assigns `x`, `y`, `z`, and `o` to the public `m_pos` member.
*   **Usage Context**: Used when the spawn location is known precisely, such as loading a creature from the database (`Creature.Main/LoadFromDB`) or summoning a creature at a specific waypoint (`ChatHandler.CreatureCommands/HandleEscortShowWpCommand`).

#### **CreatureCreatePos#2** (Relative Coordinates)
*   **Signature**: `CreatureCreatePos(WorldObject* closeObject, float ori, float dist = 0.0f, float angle = 0.0f)`
*   **Behavior**: Initializes the struct to spawn a creature near an existing `WorldObject`.
    *   Derives the `Map` pointer from `closeObject->GetMap()` and stores it in `m_map`.
    *   Stores the `closeObject` pointer in `m_closeObject`.
    *   Stores the desired orientation (`ori`) in `m_pos.o`. Note that the X, Y, Z components of `m_pos` are **not** initialized here; they remain uninitialized until `SelectFinalPoint` is called.
    *   Stores `angle` in `m_angle` and `dist` in `m_dist`.
*   **Logic Nuance**: The comment in the source indicates that if `dist` is `0.0f`, the creature spawns exactly at the reference object's coordinates. Otherwise, it spawns at a point `dist` units away, rotated by `angle` relative to the reference object's orientation.
*   **Usage Context**: Used extensively by spell effects (`Spell.Effects/EffectSummon`, `Spell.Effects/EffectSummonGuardian`, etc.) and pet systems (`Pet.Main/CreateBaseAtCreature`) where the spawn location is relative to the caster or target.

### Coordinate Resolution

#### **SelectFinalPoint**
*   **Signature**: `void SelectFinalPoint(Creature* cr)`
*   **Behavior**: This is the core computational method. It calculates the final absolute coordinates and stores them in `m_pos`.
    *   **If Absolute Mode** (`m_closeObject` is `nullptr`): The coordinates are already set in the constructor. This method likely performs validation or minor adjustments (e.g., ensuring the creature is on solid ground) using the provided `Creature* cr` context, though the specific implementation details are in the corresponding `.cpp` file.
    *   **If Relative Mode** (`m_closeObject` is not `nullptr`):
        1.  Retrieves the current position of `m_closeObject`.
        2.  Calculates the new position based on `m_dist` and `m_angle`. The angle is typically applied relative to the reference object's orientation.
        3.  Updates `m_pos.x`, `m_pos.y`, and `m_pos.z` with the calculated values.
        4.  Uses `cr` to potentially adjust the position (e.g., collision detection, finding a valid ground height).
*   **Constraint**: The source comment explicitly states: *"read only after SelectFinalPoint"*. Accessing `m_pos` before calling this method in relative mode results in undefined behavior for X/Y/Z.

#### **Relocate**
*   **Signature**: `bool Relocate(Creature* cr) const`
*   **Behavior**: Validates whether the calculated position in `m_pos` is suitable for the given `Creature* cr`.
    *   Returns `true` if the position is valid (e.g., within map bounds, on valid terrain).
    *   Returns `false` if the position is invalid.
*   **Usage**: Callers use this to decide whether to proceed with adding the creature to the world or to abort/fallback.

### Accessors

#### **GetMap**
*   **Signature**: `Map* GetMap() const`
*   **Behavior**: Returns the `Map*` stored in `m_map`.
*   **Usage**: Used by `Creature.Main/Create`, `Pet.Main/Create`, and `Totem/Create` to ensure the creature is instantiated on the correct map instance.

## Cross-Unit Boundaries

**CreatureCreatePos** acts as a data carrier and calculator between high-level spawning logic and the low-level `Creature` instantiation.

### Incoming Calls (Consumers)
Members of **CreatureCreatePos** are called by various units to prepare spawn data:
*   **ChatHandler.CreatureCommands**: Commands like `HandleEscortShowWpCommand` and `Helper_CreateWaypointFor` construct `CreatureCreatePos` instances to place creatures at specific waypoints or locations requested by GMs.
*   **Creature.Main**: `LoadFromDB` constructs a `CreatureCreatePos` with absolute coordinates from the database to spawn persistent creatures.
*   **Player.Main**: `SummonPossessedMinion` uses relative positioning to spawn minions near the player.
*   **Spell.Effects**: Multiple effect handlers (`EffectSummon`, `EffectSummonGuardian`, `EffectSummonCritter`, `EffectSummonPet#2`, `EffectSummonTotem`) use the relative constructor to spawn summoned entities near the caster or target.
*   **WorldObject.Object**: `SummonCreature` and `SummonCreature#2` are generic summoning utilities that accept `CreatureCreatePos` to handle the placement logic.
*   **Pet.Main**: `CreateBaseAtCreature` and `LoadPetFromDB` use it to position pets.

### Outgoing Calls (Dependencies)
*   **None**: The MAP indicates no outgoing calls to other units. However, the source code shows internal dependencies:
    *   `WorldObject`: Accessed via `m_closeObject->GetMap()` in the relative constructor.
    *   `Creature`: Passed to `SelectFinalPoint` and `Relocate` for context-aware calculations.

## Data Model

**CreatureCreatePos** does not interact directly with any database tables. It operates entirely in memory. The data it processes (coordinates) may originate from the `creature` table (via `Creature.Main/LoadFromDB`), but the struct itself performs no SQL operations.

## Notable Implementation Details

1.  **Lazy Evaluation**: In relative mode, `m_pos.x/y/z` are not calculated in the constructor. They are uninitialized until `SelectFinalPoint` is called. This design allows the `Creature` instance to be created first (with minimal overhead) and then positioned, or allows the positioning logic to depend on properties of the `Creature` instance itself (like size or collision box).
2.  **Map Ownership**: The struct holds a raw pointer to `Map`. It does not take ownership. The caller must ensure the `Map` remains valid while the `CreatureCreatePos` is in use.
3.  **Angle Reference**: In relative mode, the `angle` parameter is interpreted relative to the reference object's orientation. This is crucial for spells that summon guardians facing a specific direction relative to the caster.
4.  **Const Correctness**: `Relocate` and `GetMap` are `const`, allowing them to be called on const instances. `SelectFinalPoint` is non-const because it modifies `m_pos`.
5.  **No Validation in Constructor**: The constructors do not validate if the `Map` is loaded or if the `WorldObject` is valid. Invalid pointers passed to the constructor will lead to crashes when `GetMap()` or `SelectFinalPoint()` is called. Callers are responsible for ensuring validity.

## Member Reference

**CreatureCreatePos**  
Constructor for absolute positioning. Takes a `Map*` and explicit X, Y, Z, O coordinates. Initializes `m_pos` directly and sets `m_closeObject` to `nullptr`.

**CreatureCreatePos#2**  
Constructor for relative positioning. Takes a `WorldObject*`, orientation, distance, and angle. Derives `Map` from the object. Leaves `m_pos.x/y/z` uninitialized until `SelectFinalPoint` is called.

**GetMap**  
Accessor returning the `Map*` associated with the spawn position. Used by `Creature.Main/Create`, `Pet.Main/Create`, and `Totem/Create` to instantiate the creature on the correct map.

---

<!-- machine-true, projected from graph.json -->

## Map — CreatureCreatePos

*Source:* Creature.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| CreatureCreatePos | ctor | — | ChatHandler.CreatureCommands/HandleEscortShowWpCommand, ChatHandler.CreatureCommands/Helper_CreateWaypointFor, Creature.Main/LoadFromDB, Player.Main/SummonPossessedMinion, Spell.Effects/EffectSummon, Spell.Effects/EffectSummonGuardian, WorldObject.Object/SummonCreature, WorldObject.Object/SummonCreature#2 | — |
| CreatureCreatePos#2 | ctor | — | ChatHandler.CreatureCommands/HandleNpcAddCommand, Pet.Main/CreateBaseAtCreature, Pet.Main/LoadPetFromDB, Player.Main/SummonPossessedMinion, Spell.Effects/EffectSummon, Spell.Effects/EffectSummonCritter, Spell.Effects/EffectSummonGuardian, Spell.Effects/EffectSummonPet#2, Spell.Effects/EffectSummonTotem, WorldObject.Object/SummonCreature#2 | — |
| GetMap | method | — | Creature.Main/Create, Pet.Main/Create, Totem/Create | — |
