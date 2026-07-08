# GuardAI

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# GuardAI

## Purpose & Responsibilities

`GuardAI` is a specialized `CreatureAI` for guard-type NPCs. It implements a "bystander intervention" threat model: guards aggro not only direct attackers but also players who are hostile to players, hostile to the guard, or attacking friendly NPCs (including taxis) or PvP-contested entities. It handles line-of-sight detection with specific vertical-distance rules for non-flying guards and manages the standard combat loop.

## Member-by-Member Behavior

### Eligibility & Initialization

**`Permissible`**
Static factory gatekeeper. Calls `Creature.Main/IsGuard`; returns `PERMIT_BASE_SPECIAL` for guards, `PERMIT_BASE_NO` otherwise.

**`GuardAI`**
Constructor initializing `CreatureAI` with the owning `Creature` pointer.

### Threat Assessment

**`IsAttackingPlayerOrFriendly`**
Private helper returning `true` if the unit is `Unit.Main/IsPvPContested` or its victim (`Unit.Main/GetVictim`) is friendly to the guard (`m_creature->IsFriendlyTo`) or a taxi (`pVictim->IsTaxi`).

### Combat Logic

**`MoveInLineOfSight`**
Handles aggro logic: ignores if in combat (`m_creature->GetVictim`); enforces Z-range for non-flyers (`Creature.Main/CanFly`, `WorldObject.Object/GetDistanceZ`); expands radius to 30.0f for players attacking friends (`IsAttackingPlayerOrFriendly`); triggers `CreatureAI/AttackStart` if target is within range/LOS (`WorldObject.Object/IsWithinDistInMap`, `WorldObject.Object/IsWithinLOSInMap`), valid (`WorldObject.Object/IsValidAttackTarget`), accessible (`Unit.Main/IsInAccessablePlaceFor`), and hostile (`Unit.Main/IsHostileToPlayers`, `m_creature->IsHostileTo`, or `isAttackingFriend`).

**`UpdateAI`**
Combat loop: exits if no target (`Unit.Main/SelectHostileTarget`, `m_creature->GetVictim`); updates spells (`CreatureAI/UpdateSpellsList`) if present; performs melee (`CreatureAI/DoMeleeAttackIfReady`).

## Cross-Unit Boundaries

*   **Creature.Main:** Queries state (`IsGuard`, `CanFly`, `CanInitiateAttack`, `GetVictim`, `IsFriendlyTo`, `IsHostileTo`, `GetAttackDistance`).
*   **Unit.Main:** Assesses target state (`GetVictim`, `IsPvPContested`, `IsTaxi`, `IsHostileToPlayers`, `IsInAccessablePlaceFor`, `SelectHostileTarget`).
*   **CreatureAI:** Executes actions (`AttackStart`, `DoMeleeAttackIfReady`, `UpdateSpellsList`).
*   **WorldObject.Object / Object:** Handles spatial checks (`GetDistanceZ`, `IsWithinDistInMap`, `IsWithinLOSInMap`, `IsValidAttackTarget`, `IsCreature`, `IsPlayer`).

## Data Model

`GuardAI` does not interact with any database tables.

## Notable Implementation Details

*   **Hardcoded Aggro Radius:** In `MoveInLineOfSight`, if a player is attacking a friend, the attack radius is clamped to a minimum of `30.0f`.
*   **Assignment in Condition:** `MoveInLineOfSight` uses `if (isAttackingFriend = IsAttackingPlayerOrFriendly(pWho))` to both evaluate and store the result.
*   **Taxi Protection:** Guards explicitly aggro players attacking taxi NPCs.

## Member Reference

**Permissible**: Static method checking `Creature.Main/IsGuard`; returns `PERMIT_BASE_SPECIAL` for guards, `PERMIT_BASE_NO` otherwise.

**GuardAI**: Constructor initializing `CreatureAI` with the owning `Creature` pointer.

**IsAttackingPlayerOrFriendly**: Private method returning `true` if the unit is `Unit.Main/IsPvPContested` or its victim (`Unit.Main/GetVictim`) is friendly to the guard (`m_creature->IsFriendlyTo`) or a taxi (`pVictim->IsTaxi`).

**MoveInLineOfSight**: Handles aggro logic: ignores if in combat; enforces Z-range for non-flyers (`Creature.Main/CanFly`, `WorldObject.Object/GetDistanceZ`); expands radius to 30.0f for players attacking friends (`IsAttackingPlayerOrFriendly`); triggers `CreatureAI/AttackStart` if target is within range/LOS (`WorldObject.Object/IsWithinDistInMap`, `WorldObject.Object/IsWithinLOSInMap`), valid (`WorldObject.Object/IsValidAttackTarget`), accessible (`Unit.Main/IsInAccessablePlaceFor`), and hostile (`Unit.Main/IsHostileToPlayers`, `m_creature->IsHostileTo`, or `isAttackingFriend`).

**UpdateAI**: Combat loop: exits if no target (`Unit.Main/SelectHostileTarget`, `m_creature->GetVictim`); updates spells (`CreatureAI/UpdateSpellsList`) if present; performs melee (`CreatureAI/DoMeleeAttackIfReady`).

---

<!-- machine-true, projected from graph.json -->

## Map — GuardAI

*Source:* GuardAI.cpp, GuardAI.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Permissible | method | Creature.Main/IsGuard | — | — |
| GuardAI | ctor | CreatureAI/CreatureAI | — | — |
| IsAttackingPlayerOrFriendly | method | Unit.Main/GetVictim, Unit.Main/IsFriendlyTo, Unit.Main/IsPvPContested, Unit.Main/IsTaxi | — | — |
| MoveInLineOfSight | method | Creature.Main/CanFly, Creature.Main/CanInitiateAttack, Creature.Main/GetAttackDistance, CreatureAI/AttackStart, Object/IsCreature, Object/IsPlayer, Unit.Main/GetVictim, Unit.Main/IsFriendlyTo, Unit.Main/IsHostileTo, Unit.Main/IsHostileToPlayers, Unit.Main/IsInAccessablePlaceFor, WorldObject.Object/GetDistanceZ, WorldObject.Object/IsValidAttackTarget, WorldObject.Object/IsWithinDistInMap, WorldObject.Object/IsWithinLOSInMap | — | — |
| UpdateAI | method | CreatureAI/DoMeleeAttackIfReady, CreatureAI/UpdateSpellsList, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
