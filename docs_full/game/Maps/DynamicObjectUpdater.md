# DynamicObjectUpdater

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# DynamicObjectUpdater

**Purpose & Responsibilities**

`DynamicObjectUpdater` is a visitor struct within the `MaNGOS` namespace, defined in `GridNotifiers.h` and implemented in `GridNotifiersImpl.h`. Its sole responsibility is to process the effects of a **Dynamic Object** (a persistent area-of-effect spell effect, such as *Consecration*, *Flamestrike*, or *Freezing Trap*) on all valid `Unit` targets (Players and Creatures) currently visible in the grid cells surrounding the Dynamic Object.

When instantiated, it holds a reference to a specific `DynamicObject` and determines which units within that object's radius should receive the associated spell aura. It performs rigorous validation checks—including line-of-sight, immunity, PvP flags, and target validity—before applying or refreshing the `PersistentAreaAura` on the target. It also handles combat initiation (threat generation) for hostile effects.

This unit is part of the grid notification system, where visitors are passed to grid managers to iterate over objects in specific spatial partitions. `DynamicObjectUpdater` is specifically invoked by the `DynamicObject` class during its update cycle to ensure its effects remain synchronized with the current state of nearby entities.

**Member-by-Member Behavior**

### Construction and Initialization
*   **`DynamicObjectUpdater`**: The constructor accepts a reference to the `DynamicObject` being updated, a pointer to the `SpellCaster` who cast the spell, and a boolean indicating whether the spell is beneficial (`true`) or harmful (`false`).
    *   It initializes `i_check` with the provided caster.
    *   **Notable Logic**: If the caster is a `Unit` (e.g., a player or creature) and has an owner (e.g., a pet, totem, or charmed unit), `i_check` is reassigned to the **owner**. This ensures that threat and PvP checks are attributed to the primary controller rather than the indirect source, which is critical for correct gameplay mechanics (e.g., a hunter's trap threatening the hunter, not the trap itself).

### Visitor Methods
The struct implements the visitor pattern interface required by the grid system. It provides empty implementations for most object types and specialized implementations for `Player` and `Creature` maps.

*   **`Visit(GridRefManager<T>&)`**: A templated method that does nothing. This ensures that irrelevant object types (like `GameObject`, `Corpse`, etc.) are ignored during the grid traversal.
*   **`Visit(PlayerMapType&)`**: Iterates over all `Player` objects in the current grid cell. For each player, it calls `VisitHelper` to evaluate and apply the dynamic object's effect.
*   **`Visit(CreatureMapType&)`**: Iterates over all `Creature` objects in the current grid cell. For each creature, it calls `VisitHelper` to evaluate and apply the dynamic object's effect.

### Core Logic
*   **`VisitHelper`**: This is the central method containing all the business logic for applying the dynamic object's effect to a single `Unit` target. It performs the following steps in order:
    1.  **Visibility Check**: Ensures the target can see the caster (`i_check`). If not, the effect is skipped.
    2.  **Range Check**: Verifies the target is within the `DynamicObject`'s radius.
    3.  **Creature-Specific Filters**:
        *   Skips creatures immune to AoE.
        *   Skips creatures in evade mode (fleeing).
    4.  **Player-Specific Filters**:
        *   If the target is a Player and is not the caster, skips them if they are in Game Master mode or have GM invisibility enabled.
    5.  **Target Validity**:
        *   For harmful spells (`!i_positive`): Checks if the target is a valid attack target for the caster.
        *   For beneficial spells (`i_positive`): Checks if the target is a valid helpful target for the caster.
    6.  **Line-of-Sight (LoS)**: If the caster is a Player, verifies LoS between the Dynamic Object and the target. This prevents players from casting through walls by targeting the floor. Creatures bypass this check ("let creatures cheat").
    7.  **Refresh Check**: Calls `i_dynobject.NeedsRefresh(target)`. If the target already has the correct aura and doesn't need updating, the method returns early.
    8.  **PvP Flag Check (Patch 1.7.0+)**: For harmful spells, ensures that non-PvP-flagged players cannot damage PvP-flagged players (unless in a duel or FFA PvP zone).
    9.  **Combat Initiation**: For harmful spells, if the caster is a `Unit` and the spell does not have attributes suppressing threat, it triggers combat:
        *   Calls `AttackedBy` on the target's AI.
        *   Adds threat to the target's threat list.
        *   Sets both units into combat with each other.
    10. **Immunity Check**: Verifies the target is not immune to the spell or its specific effect index.
    11. **Aura Application**:
        *   Attempts to retrieve an existing `SpellAuraHolder` for this spell/caster combination on the target.
        *   **If Existing**:
            *   Marks the holder as in use.
            *   If the specific effect index is missing, creates a new `PersistentAreaAura`, adds it to the holder, and applies modifiers.
            *   If the effect exists and the spell is not channeled, updates the aura duration if the dynamic object's duration is longer.
        *   **If New**:
            *   Creates a new `SpellAuraHolder`.
            *   Creates a `PersistentAreaAura` and adds it to the holder.
            *   Attempts to add the holder to the target. If the target's debuff slots are full, the holder is discarded.
    12. **Channeling Sync**: If the aura is channeled, it synchronizes the aura's duration and timers with the caster's current channeled spell to ensure tick alignment.
    13. **Tracking**: Adds the target to the `DynamicObject`'s list of affected units via `i_dynobject.AddAffected(target)`.

**Cross-Unit Boundaries**

*   **Called By**:
    *   `DynamicObject/Update`: The `DynamicObject` class instantiates `DynamicObjectUpdater` and passes it to the grid manager to visit relevant cells. This is the primary entry point for the updater's lifecycle.
*   **Calls Out**:
    *   `Unit::CanSeeInWorld`: Checks visibility.
    *   `DynamicObject::IsWithinDistInMap`, `GetRadius`, `NeedsRefresh`, `AddAffected`, `GetCasterGuid`, `GetSpellId`, `GetEffIndex`, `GetDuration`, `IsChanneled`, `GetObjectGuid`, `GetUnitCaster`, `GetCaster`: Interacts with the owning `DynamicObject` to get state and track targets.
    *   `Creature::IsImmuneToAoe`, `IsInEvadeMode`: Filters invalid creature targets.
    *   `Player::IsGameMaster`, `GetVisibility`: Filters invalid player targets.
    *   `SpellCaster::IsValidAttackTarget`, `IsValidHelpfulTarget`, `ToUnit`, `GetCurrentSpell`: Validates targets and retrieves caster state.
    *   `Unit::AI`, `CreatureAI::AttackedBy`, `AddThreat`, `SetInCombatWithAggressor`, `SetInCombatWithVictim`: Initiates combat mechanics.
    *   `Unit::IsImmuneToSpell`, `IsImmuneToSpellEffect`: Checks immunities.
    *   `Unit::GetSpellAuraHolder`, `AddSpellAuraHolder`, `AddAuraToModList`: Manages aura holders on the target.
    *   `SpellMgr::GetSpellEntry`: Retrieves spell data.
    *   `PersistentAreaAura` constructor and methods: Creates and configures the specific aura type for dynamic objects.
    *   `SpellAuraHolder` methods: Manages the container for the aura.
    *   `Spell` methods: Synchronizes channeled spells.

**Data Model**

This unit does not interact directly with any database tables. It operates entirely on in-memory game objects (`DynamicObject`, `Unit`, `SpellAuraHolder`, etc.).

**Notable Implementation Details**

1.  **Owner Resolution**: The constructor's logic to reassign `i_check` to the owner of the caster is crucial. Without this, pets or totems would generate threat for themselves rather than their masters, breaking core WoW mechanics.
2.  **GM Immunity**: Players in GM mode or with GM invisibility are explicitly excluded from being affected by dynamic objects unless they are the caster. This prevents accidental self-harm or interference during testing/moderation.
3.  **Creature LoS Cheat**: The comment "Let creatures cheat" indicates that NPCs do not require Line-of-Sight to be affected by dynamic objects. This is likely an optimization or a design choice to simplify NPC behavior, ensuring they always take damage/healing from AoEs if in range, regardless of geometry.
4.  **Debuff Slot Handling**: If a target's debuff slots are full, the `SpellAuraHolder` is created but then discarded if `AddSpellAuraHolder` fails. This correctly simulates the client-side limitation where excess debuffs are dropped.
5.  **Channeling Synchronization**: The code carefully synchronizes the aura's internal timers with the caster's channeled spell duration. This prevents desync issues where the server thinks the aura is ticking at a different rate than the client expects, which could lead to premature expiration or extended duration.
6.  **PvP Flag Enforcement**: The check for PvP flags is conditional on the client build (> 1.6.1). This reflects a historical change in WoW where non-PvP players could no longer damage PvP players with AoEs like Consecration.

## Member Reference

**VisitHelper**
Private helper method that contains the core logic for applying the dynamic object's effect to a single `Unit` target. It performs visibility, range, immunity, PvP, and LoS checks before creating or refreshing the `PersistentAreaAura` on the target. It also initiates combat for harmful spells.

**DynamicObjectUpdater**
Constructor that initializes the updater with the `DynamicObject`, the `SpellCaster`, and a flag for positive/negative effects. It resolves the effective caster (`i_check`) to the owner if the caster is a controlled unit (pet/totem).

**Visit**
Templated visitor method for `GridRefManager<T>`. Provides an empty implementation to ignore irrelevant object types during grid traversal. Specialized versions for `PlayerMapType` and `CreatureMapType` iterate over the respective objects and call `VisitHelper` for each.

**Visit#2**
Refers to the specialized `Visit` methods for `PlayerMapType` and `CreatureMapType` defined in `GridNotifiersImpl.h`. These methods iterate over the units in the grid cell and delegate to `VisitHelper`.

---

<!-- machine-true, projected from graph.json -->

## Map — DynamicObjectUpdater

*Source:* GridNotifiers.h, GridNotifiersImpl.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| VisitHelper | method | — | — | — |
| DynamicObjectUpdater | ctor | — | DynamicObject/Update | — |
| Visit | method | — | — | — |
| Visit#2 | method | — | — | — |
