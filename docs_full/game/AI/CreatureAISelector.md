<!-- provenance: verbose -->
# CreatureAISelector

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# CreatureAISelector

**Purpose & Responsibilities**

`CreatureAISelector` (in `CreatureAISelector.cpp`/`.h`) implements the factory logic for assigning an Artificial Intelligence (`CreatureAI`) and a movement strategy (`MovementGenerator`) to a `Creature` instance during initialization. It resolves conflicts between scripted overrides, hardcoded creature roles (pets, guards, totems), and dynamic permit-based selection, ensuring each creature receives appropriate behavior.

## Member-by-Member Behavior

### `selectAI`

This function implements a strict priority chain to assign an AI to a `Creature`:

1.  **Player Possession**: If the creature is not a pet, is charmed by a player (`GetCharmGuid().IsPlayer()`), and has `UNIT_FLAG_POSSESSED`, it returns a `NullCreatureAI`. This disables autonomous behavior for possessed bodies.
2.  **Script Override**: For non-controlled pets (guardians/mini-pets) and non-charmed creatures, it queries `ScriptMgr::GetCreatureAI`. If a script provides an AI, that instance is returned immediately, bypassing registry lookups.
3.  **Role-Based Registry Selection**:
    *   **Pets/Charmed**: If the creature is a controlled pet with a player owner, or is charmed, it selects `"PetAI"`.
    *   **Totems**: If `IsTotem()` is true, it selects `"TotemAI"`.
    *   **EventAI Corrections**: If the configured `AIName` is `"EventAI"` but the creature is a pet or guard, it overrides to `"PetEventAI"` or `"GuardEventAI"` respectively, preventing generic event logic from overriding specialized mechanics.
4.  **Named AI**: If no role-based AI was selected, it looks up the creature’s configured `AIName` in the `CreatureAIRegistry`.
5.  **Fallback Roles**: If still unresolved, it assigns `"GuardAI"` for guards (`IsGuard()`) or `"CritterAI"` for critters (`CREATURE_TYPE_CRITTER`).
6.  **Permit System**: As a final fallback, it iterates all registered AI factories, casting them to `SelectableAI` and calling `Permit(creature)`. It selects the AI with the highest permit value, allowing complex, context-sensitive selection logic defined in individual AI classes.
7.  **Default**: If no AI is selected, it defaults to `NullCreatureAI`. It logs the selected AI name if `LOG_FILTER_AI_AND_MOVEGENSS` is active.

### `selectMovementGenerator`

This function determines the initial `MovementGenerator` for a creature:

1.  **Owner Check**: If the creature’s owner is a player (`GetOwnerGuid().IsPlayer()`), the default type is `FOLLOW_MOTION_TYPE`. Otherwise, it uses `GetDefaultMovementType()`.
2.  **Formation Override**: If the creature is in a `CreatureGroup` that is in formation (`IsFormation()`), and the creature is **not** the leader or original leader, the type is overridden to `PATROL_MOTION_TYPE`. This ensures followers patrol in formation rather than following the leader blindly.
3.  **Instantiation**: It retrieves the `MovementGeneratorCreator` from `MovementGeneratorRegistry` for the determined type and creates the instance. Returns `nullptr` if no factory exists.

## Cross-Unit Boundaries

### `selectAI` Collaborations

*   **`Creature.Main`**: Calls `GetAIName`, `GetCreatureInfo`, `IsGuard`, `IsPet`, `IsTotem`, `GetCharmGuid`, `GetOwnerGuid`, `HasUnitState`, `IsCharmed`, `GetGUIDLow` to determine identity, role, and control status.
*   **`ObjectGuid`**: Calls `IsPlayer` on Charm/Owner GUIDs to distinguish player-controlled entities.
*   **`Pet.Main`**: Calls `IsControlled` to differentiate controlled pets from guardians.
*   **`ScriptMgr`**: Calls `GetCreatureAI` to allow script-based AI overrides.
*   **`NullCreatureAI`**: Calls constructor to instantiate the default empty AI.
*   **`Log.Main`**: Calls `HasLogFilter`, `HasLogLevelOrHigher`, `Out` for debug logging.
*   **`Errors`**: Calls `PrintStacktraceAndThrow` (via `MANGOS_ASSERT`) if a registry item fails `dynamic_cast<SelectableAI>`.

### `selectMovementGenerator` Collaborations

*   **`Creature.Main`**: Calls `GetCreatureGroup`, `GetCreatureInfo`, `GetDefaultMovementType`, `GetOwnerGuid` for group and ownership data.
*   **`CreatureGroups`**: Calls `IsFormation`, `GetLeaderGuid`, `GetOriginalLeaderGuid` to determine formation follower status.
*   **`Object`**: Calls `GetObjectGuid` to compare against leader GUIDs.
*   **`ObjectGuid`**: Calls `IsPlayer` and `operator!=` for owner checks and GUID comparisons.
*   **`Errors`**: Calls `PrintStacktraceAndThrow` (via `MANGOS_ASSERT`) if `CreatureInfo` is null.

## Data Model

This unit does not interact directly with database tables. All decision data comes from in-memory objects (`Creature`, `CreatureInfo`, `CreatureGroup`) loaded by other subsystems.

## Notable Implementation Details

1.  **Possession Edge Case**: The first check in `selectAI` explicitly handles `UNIT_FLAG_POSSESSED` with a player charm, returning `NullCreatureAI` to prevent autonomous actions during possession.
2.  **Script Priority**: Scripted AIs take precedence over registry-based AIs for non-controlled pets and non-charmed creatures, allowing admin overrides via scripts.
3.  **EventAI Overrides**: Explicit checks for `"EventAI"` on pets/guards override to `"PetEventAI"`/`"GuardEventAI"`, preserving specialized mechanics.
4.  **Permit System**: The fallback loop allows dynamic AI selection based on `SelectableAI::Permit`, enabling complex logic like health-based or stealth-based AI switching.
5.  **Formation Logic**: `selectMovementGenerator` ensures only non-leaders in formations use `PATROL_MOTION_TYPE`, maintaining formation structure.
6.  **Singletons**: The file instantiates `CreatureAIRegistry` and `MovementGeneratorRegistry` singletons via `INSTANTIATE_SINGLETON_1`.

## Member Reference

**selectAI**
Determines the `CreatureAI` instance for a `Creature` by checking possession status, script overrides, role-based registry entries (Pet, Totem, Guard, Critter), named AI configurations, and finally permit-based selection. Falls back to `NullCreatureAI` if no match is found. Logs the selection if debug logging is enabled.

**selectMovementGenerator**
Determines the `MovementGenerator` for a `Creature` by checking if it has a player owner (follow motion) or is a follower in a formation (patrol motion). Uses the creature's default movement type otherwise. Instantiates the generator from the `MovementGeneratorRegistry`.

---

<!-- machine-true, projected from graph.json -->

## Map — CreatureAISelector

*Source:* CreatureAISelector.cpp, CreatureAISelector.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| selectAI | function | Creature.Main/GetAIName, Creature.Main/GetCreatureInfo, Creature.Main/IsGuard, Creature.Main/IsPet, Creature.Main/IsTotem, Errors/PrintStacktraceAndThrow, Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out, NullCreatureAI/NullCreatureAI, Object/GetGUIDLow, ObjectGuid/IsPlayer, Pet.Main/IsControlled, ScriptMgr/GetCreatureAI, Unit.Main/GetCharmGuid, Unit.Main/GetOwnerGuid, Unit.Main/HasUnitState, Unit.Main/IsCharmed | Creature.Main/AIM_Initialize | — |
| selectMovementGenerator | function | Creature.Main/GetCreatureGroup, Creature.Main/GetCreatureInfo, Creature.Main/GetDefaultMovementType, CreatureGroups/GetLeaderGuid, CreatureGroups/GetOriginalLeaderGuid, CreatureGroups/IsFormation, Errors/PrintStacktraceAndThrow, Object/GetObjectGuid, ObjectGuid/IsPlayer, ObjectGuid/operator!=, Unit.Main/GetOwnerGuid | Creature.MotionMaster/Initialize, Creature.MotionMaster/InitializeNewDefault | — |
