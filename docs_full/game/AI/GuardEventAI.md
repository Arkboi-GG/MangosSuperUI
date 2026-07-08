<!-- provenance: verbose -->
# GuardEventAI

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# GuardEventAI

`GuardEventAI` is a specialized AI controller for guard-type `Creature` entities, extending `CreatureEventAI`. It modifies standard engagement logic to ensure guards react aggressively to players attacking friendly NPCs, taxis, or those in PvP-contested states. It primarily overrides line-of-sight detection to expand attack ranges and adjust hostility thresholds for these specific scenarios.

## Member-by-Member Behavior

### Initialization and Selection

**GuardEventAI**
Constructs the AI instance, passing the associated `Creature` pointer to the base `CreatureEventAI` constructor.

**~GuardEventAI**
Empty destructor; resource management is delegated to the base class or owning `Creature`.

**Permissible**
Static method determining AI eligibility. Returns `PERMIT_BASE_SPECIAL` if the creature’s AI name is explicitly `"GuardEventAI"`, or if it is a guard (`IsGuard()`) with AI name `"EventAI"`. Otherwise, returns `PERMIT_BASE_NO`. This allows database-configured guards using the generic `EventAI` placeholder to automatically receive guard-specific behavior.

### Engagement Logic

**IsAttackingPlayerOrFriendly**
Private helper evaluating if a target unit (`pWho`) threatens friendly entities. Returns `true` if:
1. The target is in a PvP-contested state (`IsPvPContested()`).
2. The target has a victim that is friendly to the guard (`m_creature->IsFriendlyTo(pVictim)`) or is a taxi NPC (`pVictim->IsTaxi()`).

**MoveInLineOfSight**
Overrides base behavior to define engagement triggers when a unit enters line of sight:
1. **Early Exit**: Ignores the event if the guard already has a victim.
2. **Event Updates**: Calls `CreatureEventAI::UpdateEventsOn_MoveInLineOfSight` if pending events exist (`!m_bEmptyList`).
3. **Vertical Check**: For non-flying creatures, ignores targets with vertical distance exceeding `CREATURE_Z_ATTACK_RANGE`. Flying creatures bypass this.
4. **Radius Adjustment**: Calculates base attack distance. If the target is a non-friendly player attacking a friend/taxi/PvP zone, and the radius is < 30.0f, it forces the radius to 30.0f.
5. **Proximity**: Verifies the target is within the adjusted radius via `IsWithinDistInMap`.
6. **Attack Initiation**: Calls `AttackStart` if the guard can initiate attacks, the target is valid, accessible, in LOS, and meets hostility criteria: the target is hostile to players, hostile to the guard, or is attacking a friend (`isAttackingFriend`).

## Cross-Unit Boundaries

- **CreatureEventAI**: Inherits event-driven capabilities; calls `UpdateEventsOn_MoveInLineOfSight` during LOS checks.
- **Creature.Main**: Queries state via `GetAIName`, `IsGuard`, `CanFly`, `CanInitiateAttack`, `GetAttackDistance`, `GetVictim`, `IsFriendlyTo`, `IsHostileTo`, `IsInAccessablePlaceFor`, `IsValidAttackTarget`, `IsWithinDistInMap`, and `IsWithinLOSInMap`.
- **Unit.Main**: Evaluates target state via `GetVictim`, `IsFriendlyTo`, `IsPvPContested`, `IsTaxi`, `IsHostileToPlayers`, and `IsHostileTo`.
- **CreatureAI**: Calls `AttackStart` to begin combat.
- **Object**: Identifies target type via `IsCreature` and `IsPlayer`.
- **WorldObject.Object**: Handles spatial queries via `GetDistanceZ`, `IsWithinDistInMap`, and `IsWithinLOSInMap`.

## Data Model

`GuardEventAI` does not interact with database tables. All logic relies on in-memory `Creature` and `Unit` states.

## Notable Implementation Details

- **Expanded Aggro Range**: In `MoveInLineOfSight`, if a player is attacking a friendly entity, the attack radius is forcibly set to at least 30.0f, allowing guards to aggro from significantly further than their natural range.
- **PvP/Taxi Sensitivity**: Guards treat PvP-contested players and attackers of taxi NPCs as immediate threats, triggering aggro even if direct hostility isn't established otherwise.
- **Flying Exception**: Flying guards ignore vertical distance constraints, enabling them to engage targets on different elevation planes.

## Member Reference

**GuardEventAI**
Constructor initializing the AI by passing the `Creature` pointer to `CreatureEventAI`.

**Permissible**
Static method returning `PERMIT_BASE_SPECIAL` if the creature’s AI name is `"GuardEventAI"` or if it is a guard with AI name `"EventAI"`; otherwise `PERMIT_BASE_NO`.

**~GuardEventAI**
Empty destructor.

**IsAttackingPlayerOrFriendly**
Private helper returning `true` if the target is PvP-contested or attacking a friendly NPC or taxi.

**MoveInLineOfSight**
Overrides base LOS logic: skips if already attacking, updates events, checks vertical distance (unless flying), expands attack radius to 30.0f if target attacks friends, and initiates `AttackStart` if proximity, validity, accessibility, LOS, and hostility conditions are met.

---

<!-- machine-true, projected from graph.json -->

## Map — GuardEventAI

*Source:* GuardEventAI.cpp, GuardEventAI.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GuardEventAI | ctor | CreatureEventAI/CreatureEventAI | — | — |
| Permissible | method | Creature.Main/GetAIName, Creature.Main/IsGuard | — | — |
| ~GuardEventAI | dtor | — | — | — |
| IsAttackingPlayerOrFriendly | method | Unit.Main/GetVictim, Unit.Main/IsFriendlyTo, Unit.Main/IsPvPContested, Unit.Main/IsTaxi | — | — |
| MoveInLineOfSight | method | Creature.Main/CanFly, Creature.Main/CanInitiateAttack, Creature.Main/GetAttackDistance, CreatureAI/AttackStart, CreatureEventAI/UpdateEventsOn_MoveInLineOfSight, Object/IsCreature, Object/IsPlayer, Unit.Main/GetVictim, Unit.Main/IsFriendlyTo, Unit.Main/IsHostileTo, Unit.Main/IsHostileToPlayers, Unit.Main/IsInAccessablePlaceFor, WorldObject.Object/GetDistanceZ, WorldObject.Object/IsValidAttackTarget, WorldObject.Object/IsWithinDistInMap, WorldObject.Object/IsWithinLOSInMap | — | — |
