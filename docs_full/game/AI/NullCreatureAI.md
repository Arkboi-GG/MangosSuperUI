<!-- provenance: verbose -->
# NullCreatureAI

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`NullCreatureAI` is a minimal, passive AI implementation for `Creature` entities. It ensures assigned creatures remain non-aggressive and inactive in combat by disabling melee attacks, combat movement, and target searching. Crucially, it maintains proper combat state management to allow creatures to correctly evade combat if forced into it (e.g., by player aggression), preventing them from getting stuck in a hostile state.

## Member-by-Member Behavior

### Initialization

**`NullCreatureAI` (Constructor)**
Initializes the AI by calling the base `CreatureAI` constructor. It configures the associated `Creature` (`c`) to be passive:
1.  Calls `c->AddUnitState(UNIT_STATE_NO_SEARCH_FOR_OTHERS)` (via `Unit.Main`) to prevent the creature from actively seeking new hostile targets.
2.  Sets `m_bMeleeAttack` and `m_bCombatMovement` to `false`, disabling offensive actions and movement toward targets.

### Passive Event Handling

The following methods override base class hooks with empty bodies to suppress all reactive combat behaviors:

*   **`MoveInLineOfSight`**: Ignores units entering line of sight; prevents aggro from visual detection.
*   **`AttackStart`**: Prevents the creature from initiating attacks.
*   **`AttackedBy`**: Prevents retaliation or automatic combat entry when damaged. The creature takes damage but does not fight back.

### Update Loop

**`UpdateAI`**
Called periodically by the AI manager. It invokes `m_creature->SelectHostileTarget()` (delegating to `Unit.Main`). This call is essential for state cleanup: it allows the core system to evaluate the threat list and trigger "evade mode," ensuring the creature leaves combat if it was forced into it. Without this, the creature might remain stuck in a combat state indefinitely.

### AI Selection

**`Permissible`**
A static method that returns `PERMIT_BASE_IDLE`. It signals to the AI selection system that this AI is suitable for idle or passive creatures.

## Cross-Unit Boundaries

### Outgoing Calls

*   **`CreatureAI`**: Base class constructor called during initialization.
*   **`Unit.Main`**:
    *   `AddUnitState`: Called in the constructor to set the `UNIT_STATE_NO_SEARCH_FOR_OTHERS` flag.
    *   `SelectHostileTarget`: Called in `UpdateAI` to manage combat evasion and state cleanup.

### Incoming Calls

*   **`boss_vaelastrasz::UpdateAI`**, **`boss_victor_nefarius::GetAI_boss_victor_nefarius`**: Instantiate `NullCreatureAI` for specific boss-related NPCs.
*   **`CreatureAISelector::selectAI`**: Calls `Permissible` to validate AI suitability.
*   **`Map.ScriptCommands::ScriptCommand_SummonCreature`**: Assigns this AI to summoned passive creatures.
*   **`scripts_battlegrounds_battleground::npc_etendardAI`**: Uses this AI for passive battleground banners.

## Data Model

`NullCreatureAI` does not interact with any database tables.

## Notable Implementation Details

1.  **No Retaliation**: The empty `AttackedBy` implementation means creatures with this AI will stand still and take damage without reacting. This is intentional for passive NPCs but implies they cannot flee or defend themselves.
2.  **Evade Dependency**: The `SelectHostileTarget()` call in `UpdateAI` is critical for preventing combat-state leaks. If a passive creature is attacked, this call ensures it can eventually exit combat mode; omitting it would cause persistent combat flags.
3.  **Static Suitability**: `Permissible` is static and always returns `PERMIT_BASE_IDLE`, providing a simple, non-dynamic check for AI assignment.

## Member Reference

**NullCreatureAI** (ctor): Initializes base class, adds `UNIT_STATE_NO_SEARCH_FOR_OTHERS` to the creature via `Unit.Main`, and disables melee/combat movement flags.

**~NullCreatureAI** (dtor): Empty destructor; no cleanup performed.

**MoveInLineOfSight** (method): Empty override; ignores line-of-sight events to prevent aggro.

**AttackStart** (method): Empty override; prevents attack initiation.

**AttackedBy** (method): Empty override; prevents retaliation when attacked.

**UpdateAI** (method): Calls `Unit.Main/SelectHostileTarget` to enable proper combat evasion and state cleanup.

**Permissible** (method): Static method returning `PERMIT_BASE_IDLE` to indicate suitability for idle creatures.

---

<!-- machine-true, projected from graph.json -->

## Map — NullCreatureAI

*Source:* NullCreatureAI.cpp, NullCreatureAI.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| NullCreatureAI | ctor | CreatureAI/CreatureAI, Unit.Main/AddUnitState | boss_vaelastrasz/UpdateAI, boss_victor_nefarius/GetAI_boss_victor_nefarius, CreatureAISelector/selectAI, Map.ScriptCommands/ScriptCommand_SummonCreature, scripts_battlegrounds_battleground/npc_etendardAI | — |
| ~NullCreatureAI | dtor | — | — | — |
| MoveInLineOfSight | method | — | — | — |
| AttackStart | method | — | — | — |
| AttackedBy | method | — | — | — |
| UpdateAI | method | Unit.Main/SelectHostileTarget | — | — |
| Permissible | method | — | — | — |
