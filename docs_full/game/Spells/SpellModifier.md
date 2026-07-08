# SpellModifier

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# SpellModifier

## Purpose & Responsibilities

`SpellModifier` is a value object representing a single rule that modifies the behavior of other spells. It encapsulates the modification parameters (operation, type, value, charges) and the scope criteria (a bitmask and spell family constraint) that determine which spells are affected. It does not manage collections or apply modifications; those responsibilities belong to `Unit.SpellAuras`.

## Member-by-Member Behavior

### Construction

Four constructors initialize the struct from different sources:

*   **Default (`SpellModifier`)**: Resets all fields to neutral defaults (`MAX_SPELLMOD`, `SPELLMOD_TYPE_NONE`, zero values, null pointer).
*   **Raw Data (`SpellModifier#3`)**: Accepts pre-calculated `spellId` and `mask`. Used when scope data is already known, avoiding lookups.
*   **Spell Entry (`SpellModifier#2`)**: Takes a `SpellEntry` and effect index. It extracts the `spellId` and computes the `mask` by calling `SpellMgr/GetSpellAffectMask`. Called by `Unit.SpellAuras/HandleAddModifier`.
*   **Aura (`SpellModifier#4`)**: Takes an `Aura` pointer. It retrieves the `spellId` and effect index from the aura, computes the `mask` via `SpellMgr/GetSpellAffectMask`, and stores the `aura` pointer in `ownerAura` to link the modifier to its source. Called by `Unit.SpellAuras/HandleAuraDummy`.

### Evaluation

*   **`IsAffectedOnSpell`**: Determines if a target `SpellEntry` falls within this modifier’s scope. It first resolves the modifier’s source spell via `SpellMgr/GetSpellEntry`. If the source is missing or its `SpellFamilyName` differs from the target’s, it returns `false`. Otherwise, it delegates to `SpellEntry/IsFitToFamilyMask` to check if the target’s family bits intersect with the stored `mask`. Called by `Player.Main/HasInstantCastingSpellMod`, `Player.Main/IsAffectedBySpellmod`, and `Unit.SpellAuras/IsAffectedOnSpell`.

## Cross-Unit Boundaries

*   **`SpellMgr`**: Constructors `#2` and `#4` call `SpellMgr/GetSpellAffectMask` to compute the scope bitmask. `IsAffectedOnSpell` calls `SpellMgr/GetSpellEntry` to resolve the source spell for family comparison.
*   **`Unit.SpellAuras`**: Creates modifiers via constructors `#2` and `#4` when processing auras. Queries `IsAffectedOnSpell` to evaluate spell interactions.
*   **`Player.Main`**: Queries `IsAffectedOnSpell` to determine player-specific spell modifications (e.g., instant casting).
*   **`Aura`**: Constructor `#4` reads `GetId` and `GetEffIndex` from the owning aura.

## Data Model

This unit interacts with no database tables. It operates entirely on in-memory `SpellEntry` and `Aura` objects.

## Notable Implementation Details

*   **Family Name Gate**: `IsAffectedOnSpell` requires `SpellFamilyName` to match between the source and target spells before checking the bitmask. This prevents cross-family interference even if masks overlap.
*   **Owner Tracking**: Only the Aura-based constructor sets `ownerAura`. This allows `Unit.SpellAuras` to trace modifiers back to their source auras for cleanup or updates.
*   **Null Safety**: `IsAffectedOnSpell` safely handles missing source spells by returning `false` if `SpellMgr/GetSpellEntry` returns `nullptr`.

## Member Reference

**SpellModifier** (default ctor): Initializes all members to default/zero values. No external calls.

**SpellModifier#3** (ctor): Constructs a modifier from raw data (`_op`, `_type`, `_value`, `_spellId`, `_mask`). Sets `ownerAura` to `nullptr`. No external calls.

**SpellModifier#2** (ctor): Constructs a modifier from a `SpellEntry` and effect index. Extracts `spellId` from `spellEntry`. Calls `SpellMgr/Instance` and `SpellMgr/GetSpellAffectMask` to compute the `mask`. Sets `ownerAura` to `nullptr`. Called by `Unit.SpellAuras/HandleAddModifier`.

**SpellModifier#4** (ctor): Constructs a modifier from an `Aura`. Extracts `spellId` via `Aura/GetId` and effect index via `Aura/GetEffIndex`. Calls `SpellMgr/Instance` and `SpellMgr/GetSpellAffectMask` to compute the `mask`. Stores the `aura` pointer in `ownerAura`. Called by `Unit.SpellAuras/HandleAuraDummy`.

**IsAffectedOnSpell**: Determines if a target `SpellEntry` is affected by this modifier. Retrieves the source spell entry via `SpellMgr/GetSpellEntry`. Returns `false` if the source spell is null or if `SpellFamilyName` differs between source and target. Otherwise, returns the result of `SpellEntry/IsFitToFamilyMask` on the target spell using the stored `mask`. Called by `Player.Main/HasInstantCastingSpellMod`, `Player.Main/IsAffectedBySpellmod`, and `Unit.SpellAuras/IsAffectedOnSpell`.

---

<!-- machine-true, projected from graph.json -->

## Map — SpellModifier

*Source:* SpellModifier.cpp, SpellModifier.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| SpellModifier#3 | ctor | SpellMgr/GetSpellAffectMask, SpellMgr/Instance | — | — |
| SpellModifier#2 | ctor | Aura/GetEffIndex, Aura/GetId, SpellMgr/GetSpellAffectMask, SpellMgr/Instance | Unit.SpellAuras/HandleAddModifier | — |
| SpellModifier | ctor | — | — | — |
| SpellModifier#4 | ctor | — | Unit.SpellAuras/HandleAuraDummy | — |
| IsAffectedOnSpell | method | SpellEntry/IsFitToFamilyMask, SpellMgr/GetSpellEntry, SpellMgr/Instance | Player.Main/HasInstantCastingSpellMod, Player.Main/IsAffectedBySpellmod, Unit.SpellAuras/IsAffectedOnSpell | — |
