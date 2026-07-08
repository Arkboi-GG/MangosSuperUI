<!-- provenance: verbose -->
# TotemAI

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`TotemAI` provides the artificial intelligence for totem creatures in the WoW server emulation. It distinguishes between **passive** totems (which apply an aura immediately upon summoning) and **active** totems (which periodically cast spells on nearby targets). The AI enforces stationarity by suppressing movement and standard aggro responses, ensuring totems remain rooted and focus solely on their designated spell effects.

## Member-by-Member Behavior

### Initialization and Classification

**`TotemAI` (Constructor)**
Initializes the totem’s state and determines its behavior type.
1.  **State Flagging:** Adds `UNIT_STATE_NO_SEARCH_FOR_OTHERS` to the creature to prevent independent target searching.
2.  **Data Retrieval:** Attempts to cast the `Creature` to a `Totem` object.
    *   **Success:** Retrieves `m_spellId` and `m_totemType` directly from the `Totem` instance.
    *   **Failure (Fallback):** Uses `CreatureInfo`’s first spell ID. Queries `SpellMgr`:
        *   If the spell has a cast time, classifies as `TOTEM_ACTIVE`.
        *   Otherwise, classifies as `TOTEM_PASSIVE`. If the passive spell applies an aura, it is cast immediately on the totem itself.

### AI Loop and Targeting

**`UpdateAI`**
Executes the periodic logic for active totems.
1.  **Root Enforcement:** Ensures `UNIT_STATE_ROOT` is set to maintain stationarity.
2.  **Early Exits:** Returns immediately if the totem is passive, dead, or currently casting a non-melee spell.
3.  **Range Calculation:** Retrieves the spell entry for `m_spellId` and determines `max_range`.
4.  **Target Resolution:**
    *   Retrieves the previously stored victim (`m_victimGuid`) from the map.
    *   If no victim exists, queries the totem’s owner (Shaman) for their current attacker via `owner->GetAttackerForHelper()`.
    *   If the current victim is invalid (out of range, not a valid target, or not visible), it clears the victim and performs a spatial search (`Cell::VisitAllObjects`) for the nearest attackable unit within `max_range`.
5.  **Action Execution:**
    *   **Target Found:** Updates `m_victimGuid`, orients the totem towards the target (`SetInFront`), and casts the spell.
    *   **No Target:** Clears `m_victimGuid`.

### Interface Overrides

**`MoveInLineOfSight`** and **`AttackStart`**
Empty overrides that disable standard creature reactions to line-of-sight events or aggro generation. Totems rely exclusively on the `UpdateAI` loop for behavior.

**`Permissible`**
Static method that returns `PERMIT_BASE_SPECIAL` if the creature is a totem (`creature->IsTotem()`), otherwise `PERMIT_BASE_NO`. This restricts the AI assignment to totems only.

## Cross-Unit Boundaries

*   **`Creature.Main` / `Totem`:** The constructor uses `Creature` for state flags and template data, and casts to `Totem` for dynamic spell information.
*   **`SpellMgr` / `SpellEntry`:** Resolves spell IDs to properties (cast time, aura, range) via `SpellMgr::Instance`.
*   **`Unit.Main`:** Manages state (`AddUnitState`, `HasUnitState`), ownership (`GetCharmerOrOwner`), and combat helpers (`GetAttackerForHelper`). The latter links the totem's targeting to the Shaman's combat state.
*   **`Map.Main` / `CellImpl` / `GridNotifiers`:** `UpdateAI` uses `Map::GetUnit` for entity retrieval and `Cell::VisitAllObjects` with `NearestAttackableUnitInObjectRangeCheck` for spatial target searches.
*   **`totems/TotemGlebeAI`:** Calls the `TotemAI` constructor, indicating instantiation by a specific totem subtype.

## Data Model

`TotemAI` does not interact directly with any database tables. All data is loaded into memory via DBC stores and managed through `SpellMgr` and `Creature` objects.

## Notable Implementation Details

1.  **Fallback Logic:** The constructor handles cases where `ToTotem()` fails by falling back to `CreatureInfo` template data, ensuring functionality even for dynamically spawned or desynced totems.
2.  **Immediate Passive Cast:** Passive totems applying auras cast their spell immediately in the constructor, ensuring buffs are active instantly upon summoning.
3.  **Owner-Assisted Targeting:** Prioritizing the Shaman's current attacker (`GetAttackerForHelper`) makes the totem act cohesively with the player's rotation.
4.  **Root State Enforcement:** `UpdateAI` re-applies `UNIT_STATE_ROOT` every tick to prevent accidental movement from other scripts.
5.  **Empty Overrides:** `MoveInLineOfSight` and `AttackStart` are intentionally empty to suppress standard aggro/movement behaviors.

## Member Reference

**`Permissible`**: Static method checking if a creature is a totem (`creature->IsTotem()`). Returns `PERMIT_BASE_SPECIAL` if true, `PERMIT_BASE_NO` otherwise. Restricts AI assignment to totems.

**`MoveInLineOfSight`**: Empty override disabling standard line-of-sight reaction logic for totems.

**`AttackStart`**: Empty override disabling standard attack initiation logic for totems.

**`TotemAI`**: Constructor. Sets `UNIT_STATE_NO_SEARCH_FOR_OTHERS`. Casts creature to `Totem` to get spell/type; falls back to `CreatureInfo` if cast fails. Classifies as `TOTEM_ACTIVE` (has cast time) or `TOTEM_PASSIVE`. If passive and applies aura, casts spell immediately on self.

**`UpdateAI`**: Main AI loop. Ensures `UNIT_STATE_ROOT`. Returns early if passive. Checks alive/casting status. Gets spell range. Retrieves previous victim or owner's attacker. Validates victim (range, visibility, validity). If invalid, searches for nearest attackable unit in range. If target found, sets orientation and casts spell. If no target, clears victim GUID.

---

<!-- machine-true, projected from graph.json -->

## Map — TotemAI

*Source:* TotemAI.cpp, TotemAI.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Permissible | method | Creature.Main/IsTotem | — | — |
| MoveInLineOfSight | method | — | — | — |
| AttackStart | method | — | — | — |
| TotemAI | ctor | Creature.Main/GetCreatureInfo, Creature.Main/ToTotem, CreatureAI/CreatureAI, SpellCaster/CastSpell, SpellEntry/GetCastTime, SpellEntry/IsSpellAppliesAura, SpellMgr/GetSpellEntry, SpellMgr/Instance, Totem/GetSpell, Totem/GetTotemType, Unit.Main/AddUnitState | totems/TotemGlebeAI | — |
| UpdateAI | method | Map.Main/GetUnit, NearestAttackableUnitInObjectRangeCheck/NearestAttackableUnitInObjectRangeCheck, Object/GetObjectGuid, ObjectGuid/Clear, SpellCaster/CastSpell#2, SpellCaster/IsNonMeleeSpellCasted, SpellEntry/GetSpellMaxRange, SpellMgr/GetSpellEntry, SpellMgr/Instance, Unit.Main/AddUnitState, Unit.Main/GetAttackerForHelper, Unit.Main/GetCharmerOrOwner, Unit.Main/HasUnitState, Unit.Main/IsAlive, Unit.Main/IsVisibleForOrDetect, Unit.Main/SetInFront, WorldObject.Object/GetMap, WorldObject.Object/IsValidAttackTarget, WorldObject.Object/IsWithinDistInMap | — | — |
