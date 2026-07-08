<!-- provenance: verbose -->
# Totem

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Totem

`Totem` extends `Creature` to represent a temporary, immobile entity summoned by a Shaman or scripted creature. It manages the totem’s lifecycle (creation, duration tracking, destruction), binds it to an owner, and enforces specific immunity and stat-update rules that distinguish totems from standard creatures.

## Purpose & Responsibilities

1.  **Lifecycle Management:** Handles instantiation via `Create` and `Summon`, and destruction via `UnSummon`. It tracks remaining duration and automatically despawns if the owner dies (unless the owner is a creature), goes out of visibility range, or expires.
2.  **Owner Binding:** Maintains a strict link to the summoning `Unit`. The totem copies the owner’s faction and level, and its existence is tied to the owner’s presence and visibility.
3.  **Spell Execution:** Passive totems cast their associated spell immediately upon summoning. Upon destruction, the totem removes its spell’s aura from itself, the owner, and the owner’s subgroup members.
4.  **Stat Immutability:** Overrides standard `Creature` stat-update methods to prevent dynamic recalculation of health, armor, or damage, keeping the totem’s properties static after creation.
5.  **Specialized Immunity:** Implements custom immunity logic to block healing, energizing, and most negative auras, while remaining vulnerable to specific Shaman totem-targeting spells.

## Member-by-Member Behavior

### Lifecycle and Positioning

**`Totem` (Constructor)**
Initializes the totem as a `CREATURE_SUBTYPE_TOTEM`, setting initial duration to 0 and type to `TOTEM_PASSIVE`.

**`Create`**
Prepares the totem for existence. It sets the map, creates the creature from its prototype, and adjusts the Z-coordinate to match the owner’s if the difference exceeds 5.0f (preventing visual glitches for swimming/flying casters). It relocates the totem, notifies instance data, loads addons, and forces walk mode. Called by `Spell.Effects/EffectSummonTotem`.

**`~Totem` (Destructor)**
Default destructor.

**`Summon`**
Activates the totem in the world. It initializes the AI movement manager, adds the creature to the map, and plays the spawn animation. If the owner is a creature with AI, it notifies the owner’s AI via `JustSummoned`. If the totem is `TOTEM_PASSIVE` and has an associated spell, it casts that spell on itself. Called by `Spell.Effects/EffectSummonTotem`.

**`UnSummon`**
Removes the totem from the world. It plays the despawn animation, stops combat, and removes the totem’s spell aura from itself. It then retrieves the owner and:
1.  Removes the totem reference from the owner (`_RemoveTotem`).
2.  Removes the totem’s spell aura from the owner.
3.  If the owner is a player in a group, it iterates through the group members in the same subgroup and removes the totem’s spell aura from them.
4.  Notifies the owner’s AI (if a creature) via `SummonedCreatureDespawn`.
5.  Sets the totem’s death state to `DEAD` for proper animation.
6.  Adds the totem to the object removal list.
Called by `ChatHandler.CreatureCommands/HandleNpcDeleteCommand`, `CreatureAI/operator()`, `Spell.Effects/EffectDestroyAllTotems`, `Spell.Effects/EffectSummonTotem`, and `Unit.Main/UnsummonAllTotems`.

### State and Properties

**`GetSpell`**
Returns the ID of the spell associated with this totem. Called by `TotemAI/TotemAI` and `Unit.Main/IsSecondaryThreatTarget`.

**`GetTotemDuration`**
Returns the remaining duration of the totem.

**`GetTotemType`**
Returns whether the totem is `TOTEM_PASSIVE` or `TOTEM_ACTIVE`. Called by `TotemAI/TotemAI`.

**`SetDuration`**
Sets the total lifetime of the totem in milliseconds. Called by `Spell.Effects/EffectSummonTotem`.

**`SetOwner`**
Binds the totem to a specific `Unit`. It copies the owner’s GUID, faction template ID, and level to the totem. Called by `Spell.Effects/EffectSummonTotem`.

**`GetOwner`**
Retrieves the `Unit` that owns this totem by looking up the stored owner GUID in the `ObjectAccessor`. Returns `nullptr` if the owner is not found.

**`SetTypeBySummonSpell`**
Determines the totem’s type based on the spell it casts. If the totem’s spell has a cast time, the totem is marked as `TOTEM_ACTIVE`; otherwise, it remains `TOTEM_PASSIVE`. Called by `Spell.Effects/EffectSummonTotem`.

### Updates and Maintenance

**`Update`**
The core update loop. It checks if the owner is missing, dead (and not a creature), the totem is dead, or the owner is out of visibility range; if so, it unsummons itself. It forces the motion master to `IDLE_MOTION_TYPE`. It calls `Creature::Update` to handle standard logic, then decrements the duration, unsummoning if expired. Called by `Creature.Main/Update`.

**`UpdateStats`, `UpdateResistances`, `UpdateArmor`, `UpdateMaxHealth`, `UpdateMaxPower`, `UpdateAttackPowerAndDamage`, `UpdateDamagePhysical`**
These methods override corresponding `Creature` methods as no-ops (returning `true` or doing nothing). This prevents dynamic stat recalculations, keeping the totem’s properties static.

### Immunity and Interaction

**`IsImmuneToSpellEffect`**
Determines if the totem is immune to a specific spell effect. Totems are *not* immune to specific Shaman spells (Mana Spring, Healing Stream, Mana Tide) identified by family masks, nor to spells cast on themselves. They are immune to healing, energizing, and negative auras. For other cases, it defers to `Creature::IsImmuneToSpellEffect`.

## Cross-Unit Boundaries

*   **`Creature.Main`**: `Totem` inherits from `Creature`, relying on it for positioning, creation, and updates. It overrides many methods to customize totem behavior.
*   **`Spell.Effects/EffectSummonTotem`**: Orchestrates the totem’s lifecycle, calling `Create`, `Summon`, `SetDuration`, `SetOwner`, and `SetTypeBySummonSpell`.
*   **`TotemAI`**: Calls `GetSpell` and `GetTotemType` to determine totem behavior.
*   **`Unit.Main`**: `Totem` interacts with `Unit` for owner management (`SetOwnerGuid`, `GetOwnerGuid`, `_RemoveTotem`), faction/level copying, and aura removal. `IsSecondaryThreatTarget` calls `GetSpell`.
*   **`CreatureAI`**: `Summon` and `UnSummon` notify the owner’s AI if the owner is a creature.
*   **`Group` / `Player.Main`**: `UnSummon` iterates through the owner’s group to remove totem auras from subgroup members.
*   **`Map.Main` / `WorldObject.Object`**: Used for map context, visibility checks, and spawn/despawn animations.
*   **`ZoneScript`**: `Create` notifies zone scripts via `OnCreatureCreate`.
*   **`ChatHandler.CreatureCommands`**, **`Spell.Effects/EffectDestroyAllTotems`**, **`Unit.Main/UnsummonAllTotems`**: Trigger `UnSummon` for cleanup.

## Data Model

This unit does not interact directly with any database tables. All totem data is transient and managed in memory.

## Notable Implementation Details

*   **Z-Coordinate Adjustment:** In `Create`, the totem’s Z-coordinate is forced to match the owner’s if the difference exceeds 5.0f, preventing visual glitches for swimming or flying casters.
*   **Owner Death Persistence for Creatures:** In `Update`, if the owner is a `Creature`, the totem does *not* unsummon when the owner dies, allowing scripted encounters where totems persist after the summoner’s defeat.
*   **Group Aura Cleanup:** `UnSummon` removes auras from the owner’s entire subgroup, ensuring group-wide buffs are properly cleaned up.
*   **Static Stats:** Overriding `UpdateStats` and related methods as no-ops ensures totems do not scale dynamically with the owner’s stats after creation.
*   **Immunity Specifics:** `IsImmuneToSpellEffect` has hardcoded exceptions for specific Shaman totem spells, allowing them to affect totems while blocking general healing and negative auras.

## Member Reference

**`Totem`**
Constructor. Initializes the totem as a `CREATURE_SUBTYPE_TOTEM`, sets duration to 0, and type to `TOTEM_PASSIVE`.

**`Create`**
Method. Prepares the totem for existence. Sets map, creates from proto, adjusts Z-coordinate to match owner (if diff > 5.0f), relocates, notifies instance data, loads addons, and sets walk mode. Called by `Spell.Effects/EffectSummonTotem`.

**`~Totem`**
Destructor. Default destructor.

**`GetSpell`**
Method. Returns the ID of the spell associated with this totem. Called by `TotemAI/TotemAI` and `Unit.Main/IsSecondaryThreatTarget`.

**`GetTotemDuration`**
Method. Returns the remaining duration of the totem.

**`GetTotemType`**
Method. Returns the totem's type (`TOTEM_PASSIVE` or `TOTEM_ACTIVE`). Called by `TotemAI/TotemAI`.

**`SetDuration`**
Method. Sets the total lifetime of the totem. Called by `Spell.Effects/EffectSummonTotem`.

**`UpdateStats`**
Method. Override. Returns `true` without performing any stat updates. Prevents dynamic stat changes.

**`UpdateResistances`**
Method. Override. No-op. Prevents resistance updates.

**`UpdateArmor`**
Method. Override. No-op. Prevents armor updates.

**`UpdateMaxHealth`**
Method. Override. No-op. Prevents max health updates.

**`UpdateMaxPower`**
Method. Override. No-op. Prevents max power updates.

**`UpdateAttackPowerAndDamage`**
Method. Override. No-op. Prevents attack power/damage updates.

**`UpdateDamagePhysical`**
Method. Override. No-op. Prevents physical damage updates.

**`Update`**
Method. Core update loop. Checks owner validity, visibility, and totem life. Forces idle movement. Calls base `Creature::Update`. Decrements duration and unsummons if expired. Called by `Creature.Main/Update`.

**`Summon`**
Method. Activates the totem. Initializes AI, adds to map, plays spawn animation, notifies owner AI, and casts the totem's spell if passive. Called by `Spell.Effects/EffectSummonTotem`.

**`UnSummon`**
Method. Removes the totem. Plays despawn animation, stops combat, removes auras from self, owner, and owner's subgroup members. Notifies owner AI. Sets death state and adds to removal list. Called by `ChatHandler.CreatureCommands/HandleNpcDeleteCommand`, `CreatureAI/operator()`, `Spell.Effects/EffectDestroyAllTotems`, `Spell.Effects/EffectSummonTotem`, and `Unit.Main/UnsummonAllTotems`.

**`SetOwner`**
Method. Binds the totem to a `Unit`, copying GUID, faction, and level. Called by `Spell.Effects/EffectSummonTotem`.

**`GetOwner`**
Method. Retrieves the owning `Unit` by GUID lookup.

**`SetTypeBySummonSpell`**
Method. Determines totem type based on the cast time of its associated spell. Called by `Spell.Effects/EffectSummonTotem`.

**`IsImmuneToSpellEffect`**
Method. Implements specialized immunity rules for totems, allowing specific Shaman spells while blocking healing, energizing, and negative auras.

---

<!-- machine-true, projected from graph.json -->

## Map — Totem

*Source:* Totem.cpp, Totem.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Totem | ctor | Creature.Main/Creature | Spell.Effects/EffectSummonTotem | — |
| Create | method | Creature.Main/CreateFromProto, Creature.Main/LoadCreatureAddon, Creature.Main/Relocate, Creature.Main/SelectFinalPoint, CreatureCreatePos/GetMap, Map.Main/GetInstanceData, Unit.Main/SetWalk, WorldObject.Object/GetMap, WorldObject.Object/GetPositionZ, WorldObject.Object/SetMap, ZoneScript/OnCreatureCreate | Spell.Effects/EffectSummonTotem | — |
| ~Totem | dtor | — | — | — |
| GetSpell | method | — | TotemAI/TotemAI, Unit.Main/IsSecondaryThreatTarget | — |
| GetTotemDuration | method | — | — | — |
| GetTotemType | method | — | TotemAI/TotemAI | — |
| SetDuration | method | — | Spell.Effects/EffectSummonTotem | — |
| UpdateStats | method | — | — | — |
| UpdateResistances | method | — | — | — |
| UpdateArmor | method | — | — | — |
| UpdateMaxHealth | method | — | — | — |
| UpdateMaxPower | method | — | — | — |
| UpdateAttackPowerAndDamage | method | — | — | — |
| UpdateDamagePhysical | method | — | — | — |
| Update | method | Creature.Main/Update, Creature.MotionMaster/GetCurrentMovementGeneratorType, Creature.MotionMaster/MoveIdle, Object/GetTypeId, Unit.Main/GetMotionMaster, Unit.Main/IsAlive, WorldObject.Object/IsWithinVisibilityDistanceOf | — | — |
| Summon | method | Creature.Main/AI, Creature.Main/AIM_Initialize, CreatureAI/JustSummoned, Object/GetTypeId, SpellCaster/CastSpell#2, WorldObject.Object/GetMap, WorldObject.Object/SendObjectSpawnAnim | Spell.Effects/EffectSummonTotem | — |
| UnSummon | method | Creature.Main/AI, Creature.Main/SetDeathState, CreatureAI/SummonedCreatureDespawn, game_Group_Group/SameSubGroup, Group/GetFirstMember, GroupReference/next, Object/GetTypeId, Player.Main/GetGroup, Unit.Main/CombatStop, Unit.Main/IsAlive, Unit.Main/RemoveAurasDueToSpell, Unit.Main/_RemoveTotem, WorldObject.Object/AddObjectToRemoveList, WorldObject.Object/SendObjectDeSpawnAnim | ChatHandler.CreatureCommands/HandleNpcDeleteCommand, CreatureAI/operator(), Spell.Effects/EffectDestroyAllTotems, Spell.Effects/EffectSummonTotem, Unit.Main/UnsummonAllTotems | — |
| SetOwner | method | Object/GetObjectGuid, Unit.Main/GetFactionTemplateId, Unit.Main/GetLevel, Unit.Main/SetCreatorGuid, Unit.Main/SetFactionTemplateId, Unit.Main/SetLevel, Unit.Main/SetOwnerGuid | Spell.Effects/EffectSummonTotem | — |
| GetOwner | method | ObjectAccessor/GetUnit, Unit.Main/GetOwnerGuid | — | — |
| SetTypeBySummonSpell | method | SpellEntry/GetCastTime, SpellMgr/GetSpellEntry, SpellMgr/Instance | Spell.Effects/EffectSummonTotem | — |
| IsImmuneToSpellEffect | method | Creature.Main/IsImmuneToSpellEffect, SpellEntry/IsFitToFamilyMask, SpellEntry/IsPeriodicRegenerateEffect, SpellEntry/IsPositiveSpell#4, SpellEntry/IsSpellAppliesAura#2 | — | — |
