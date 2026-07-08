# spell_warlock

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# spell_warlock

**Purpose & Responsibilities**

`spell_warlock.cpp` implements custom logic for specific Warlock spells in the WoWVMaNGOS server. Because the base spell system handles generic effects (damage, healing, movement), this unit provides script hooks (`SpellScript` and `AuraScript`) to enforce class-specific mechanics, validate casting conditions, trigger secondary spells, and manage item creation. It covers nine distinct Warlock abilities: Demonic Sacrifice, Conflagrate, Life Tap, Curse of Agony (legacy), Devour Magic, Inferno, Create Healthstone, Ritual of Summoning, and Curse of Idiocy.

The unit does not interact with any database tables; all logic is driven by in-memory object states, spell IDs, and creature entries.

## Member-by-Member Behavior

### Demonic Sacrifice
*Spells: 18788*

This ability allows a Warlock to sacrifice a summoned demon to gain a buff. The logic maps specific demon creature entries to corresponding buff spell IDs.

*   **OnEffectExecute#4**: Triggered when the spell effect executes. It checks if the target is a valid unit. It reads the target's creature entry (`GetEntry`) and uses a `switch` statement to determine the correct buff spell ID:
    *   Entry 416 (Imp) → Spell 18789
    *   Entry 417 (Fellhunter) → Spell 18792
    *   Entry 1860 (Voidwalker) → Spell 18790
    *   Entry 1863 (Succubus) → Spell 18791
    *   Any other entry logs an error via `Log.Main/Out` and returns early.
    *   Finally, it casts the determined buff spell on the caster using `SpellCaster/CastSpell#2`.

*   **GetScript_WarlockDemonicSacrifice**: Factory function returning a new instance of `WarlockDemonicSacrificeScript`.

### Conflagrate
*Spells: 17962, 18930, 18931, 18932*

Conflagrate consumes an existing Immolate aura on the target to deal immediate damage. This script ensures the target has a valid Immolate aura before allowing the cast and removes that aura upon execution.

*   **OnCheckCast**: Validates the cast target. It retrieves the target's periodic damage auras (`Unit.Main/GetAurasByType`). It iterates through them looking for an aura that fits the Warlock Immolate family (`CF_WARLOCK_IMMOLATE`) and was applied by the current caster (`Aura/GetCasterGuid` matches `spell->m_caster`). If no such aura is found, it returns `SPELL_FAILED_TARGET_AURASTATE`.

*   **OnEffectExecute**: Executes the consumption logic. Similar to `OnCheckCast`, it finds the caster's Immolate aura on the target. Once found, it removes that specific aura using `Unit.Main/RemoveAurasByCasterSpell`.

*   **GetScript_WarlockConflagrate**: Factory function returning a new instance of `WarlockConflagrateScript`.

### Life Tap
*Spells: 1454, 1455, 1456, 11687, 11688, 11689*

Life Tap converts health into mana. This script handles the complex calculation of damage (health loss), applies modifiers, checks for sufficient health, and accounts for the "Improved Life Tap" talent which increases mana gain.

*   **OnCheckCast#3**: Pre-cast validation. It calculates the potential damage (health cost) using `SpellDamageBonusDone` and `SpellDamageBonusTaken` on the caster. It applies spell modifiers (`Unit.Main/GetSpellModOwner`). Crucially, it checks if the caster's current health (`Unit.Main/GetHealth`) is greater than the calculated damage (using `std::ceil` to handle floating point rounding). If health is insufficient, it returns `SPELL_FAILED_FIZZLE`.

*   **OnEffectExecute#5**: Post-cast execution. It recalculates the damage value (`SpellCaster/CalculateSpellEffectValue`) and applies modifiers again. It converts the float damage to an integer using `shared_Util/dither`.
    *   If the caster has enough health, it reduces their health (`Unit.Main/ModifyHealth`).
    *   It then checks for "Improved Life Tap" talents by iterating over `SPELL_AURA_DUMMY` auras on the caster. If an aura with `SpellIconID == 208` is found, it increases the mana gain by the modifier amount (`(modifier + 100) * mana / 100`).
    *   Finally, it casts a custom spell (ID 31818) on the caster to grant the calculated mana.
    *   If health is insufficient (edge case), it sends a failure result.

*   **GetScript_WarlockLifeTap**: Factory function returning a new instance of `WarlockLifeTapScript`.

### Curse of Agony (Legacy)
*Spell: 18280*

This script contains legacy logic for older client builds (≤ 1.10.2) regarding the "Curse of Agony Dummy" effect.

*   **OnEffectExecute#3**: Wrapped in `#if SUPPORTED_CLIENT_BUILD <= CLIENT_BUILD_1_10_2`. It handles a triggered spell effect. It identifies the original caster of the aura, calculates damage bonuses (`SpellCaster/SpellDamageBonusDone`), and casts a custom spell (18277) on the target. For modern clients, this function does nothing.

*   **GetScript_WarlockCurseOfAgonyDummy**: Factory function returning a new instance of `WarlockCurseOfAgonyDummyScript`.

### Devour Magic
*Spells: 19505, 19731, 19734, 19736*

Devour Magic dispels magic effects and heals the Warlock. This script triggers the heal component when a dispel succeeds.

*   **OnSuccessfulDispel**: Triggered when the dispel effect successfully removes a buff/debuff. It maps the base Devour Magic spell ID to a specific heal spell ID:
    *   19505 → 19658
    *   19731 → 19732
    *   19734 → 19733
    *   19736 → 19735
    *   If the ID is unknown, it logs a debug message. Otherwise, it casts the heal spell on the caster using `SpellCaster/CastSpell#2`.

*   **GetScript_WarlockDevourMagic**: Factory function returning a new instance of `WarlockDevourMagicScript`.

### Inferno
*Spells: 1122, 24670*

Inferno summons an Infernal. This script applies additional effects to the summoned creature immediately upon spawn.

*   **OnSummon**: Triggered when the summon is created. It casts three spells on the summoned creature (`summon`):
    1.  Spell 20882: Enslave demon effect (no mana/cooldown).
    2.  Spell 22707: Short root spell (from sniffs).
    3.  Spell 22703: Inferno effect.
    All are cast using `SpellCaster/CastSpell#2`.

*   **GetScript_WarlockInferno**: Factory function returning a new instance of `WarlockInfernoScript`.

### Create Healthstone
*Spells: 5699, 6201, 6202, 11729, 11730*

Creates a healthstone item. The specific item ID depends on the spell rank and whether the Warlock has the "Improved Healthstone" talent.

*   **GetItemId**: Helper method. It checks the caster's `SPELL_AURA_DUMMY` auras for Improved Healthstone ranks (18692 for Rank 1, 18693 for Rank 2). It uses a static lookup table `items[5][3]` to select the correct item ID based on the spell ID (row) and talent rank (column). If the spell ID is unrecognized, it logs an error and returns 0.

*   **OnCheckCast#2**: Pre-cast validation. It converts the caster to a `Player`. If valid, it calls `GetItemId` to determine the item. It checks if the player has inventory space for the item using `Player.Main/CanStoreNewItem`. If not, it sends an equip error (`Player.Main/SendEquipError`) and fails the cast.

*   **OnEffectExecute#2**: Execution. It determines the item ID via `GetItemId` and creates the item in the target's inventory using `Spell.Effects/DoCreateItem`.

*   **GetScript_WarlockCreateHealthstone**: Factory function returning a new instance of `WarlockCreateHealthstoneScript`.

### Ritual of Summoning
*Spell: 698*

Allows a Warlock to summon another player. This script enforces strict targeting rules.

*   **OnCheckCast#4**: Validates the target.
    1.  Ensures the caster is a Player.
    2.  Ensures the caster has a selection (`Player.Main/GetSelectionGuid`).
    3.  Retrieves the target player via `ObjectMgr/GetPlayer`.
    4.  Validates that the target exists, is not the caster, and is in the same raid group (`Player.Main/IsInSameRaidWith`).
    5.  Checks if the target is in combat (`Unit.Main/IsInCombat`); if so, fails.
    6.  Checks map context:
        *   If the caster is in a dungeon (`MapEntry/IsDungeon`), the target must be in the same instance (`WorldObject.Object/GetMap`).
        *   If the caster is in a Battle Ground (`Player.Main/InBattleGround`), the cast fails entirely.

*   **GetScript_WarlockRitualOfSummoning**: Factory function returning a new instance of `WarlockRitualOfSummoningScript`.

### Curse of Idiocy
*Spell: 1010*

An aura that reduces intellect and spirit. This script prevents the curse from stacking indefinitely by stopping further applications once a threshold of stat loss is reached.

*   **OnPeriodicTrigger**: Triggered periodically by the aura.
    1.  Prevents self-triggering by checking if caster and target GUIDs match.
    2.  Iterates through the target's `SPELL_AURA_MOD_STAT` auras.
    3.  Sums the negative amounts for Intellect and Spirit from existing Curse of Idiocy auras.
    4.  If both Intellect loss ≤ -90 AND Spirit loss ≤ -90, it sets `spellInfo` to `nullptr`, effectively preventing the periodic tick from applying further effects or triggering secondary spells.

*   **GetScript_WarlockCurseOfIdiocy**: Factory function returning a new instance of `WarlockCurseOfIdiocyAuraScript`.

### Registration

*   **AddSC_warlock_spell_scripts**: Entry point called by `ScriptLoader/AddScripts`. It creates `Script` objects for each of the nine abilities defined above, assigns their respective factory functions (`GetScript_*`), and registers them with the script manager (`ScriptMgr/RegisterSelf`).

## Cross-Unit Boundaries

*   **Logging**: `OnEffectExecute#4` (Demonic Sacrifice) and `GetItemId` (Healthstone) call `Log.Main/Out` to report errors for unhandled creature entries or unknown spell IDs.
*   **Spell Casting**: Multiple members (`OnEffectExecute#4`, `OnSuccessfulDispel`, `OnSummon`) call `SpellCaster/CastSpell#2` to trigger secondary spells (buffs, heals, enslavement). `OnEffectExecute#5` (Life Tap) calls `SpellCaster/CastCustomSpell#2` to apply mana.
*   **Unit/Aura Management**:
    *   `OnCheckCast` and `OnEffectExecute` (Conflagrate) call `Unit.Main/GetAurasByType` and `Unit.Main/RemoveAurasByCasterSpell` to manage Immolate auras.
    *   `OnEffectExecute#5` (Life Tap) calls `Unit.Main/GetAurasByType` to find talent auras and `Unit.Main/ModifyHealth` to reduce HP.
    *   `OnPeriodicTrigger` (Curse of Idiocy) calls `Unit.Main/GetAurasByType` to sum stat reductions.
    *   `GetItemId` (Healthstone) calls `Unit.Main/GetAurasByType` to check for talent auras.
*   **Player/Inventory**: `OnCheckCast#2` (Healthstone) calls `Player.Main/CanStoreNewItem` and `Player.Main/SendEquipError` to manage inventory constraints.
*   **Target Validation**: `OnCheckCast#4` (Ritual of Summoning) calls `ObjectMgr/GetPlayer`, `Player.Main/IsInSameRaidWith`, `Unit.Main/IsInCombat`, `MapEntry/IsDungeon`, and `Player.Main/InBattleGround` to enforce summoning rules.
*   **Damage Calculation**: `OnCheckCast#3` and `OnEffectExecute#5` (Life Tap) call `SpellCaster/SpellDamageBonusDone`, `SpellCaster/SpellDamageBonusTaken`, `Unit.Main/GetSpellModOwner`, and `SpellCaster/CalculateSpellEffectValue` to compute health costs. `OnEffectExecute#3` (Curse of Agony) also uses `SpellCaster/SpellDamageBonusDone`.

## Data Model

This unit does not access any database tables. All data is derived from in-game objects, spell definitions, and creature entries.

## Notable Implementation Details

*   **Hardcoded Creature Entries**: `OnEffectExecute#4` (Demonic Sacrifice) relies on hardcoded creature entries (416, 417, 1860, 1863). If the database adds new demon types or changes these entries, this logic will fail silently (logging an error) unless updated.
*   **Legacy Code**: `OnEffectExecute#3` (Curse of Agony) is wrapped in a preprocessor directive for client build 1.10.2 or lower. It is dead code for modern builds.
*   **Floating Point Rounding**: `OnCheckCast#3` (Life Tap) uses `std::ceil` to compare health against damage, while `OnEffectExecute#5` uses `shared_Util/dither` to convert the final damage to an integer. This discrepancy could theoretically allow a cast to pass validation but fail execution if the dithered value exceeds remaining health, though the execution block re-checks health before modifying it.
*   **Talent Detection by Icon**: `OnEffectExecute#5` (Life Tap) detects the "Improved Life Tap" talent by checking for `SpellIconID == 208` on dummy auras. This is fragile; if the icon ID changes in a future patch or database update, the talent bonus will break.
*   **Static Lookup Table**: `GetItemId` (Healthstone) uses a static array `items[5][3]`. The indices are tightly coupled to specific spell IDs and talent ranks. Adding a new healthstone rank requires updating this table and the switch statement.
*   **Silent Failures**: In `OnCheckCast#2` (Healthstone), if `pCaster` is null (not a player), it returns `SPELL_CAST_OK`. This allows non-player casters (e.g., NPCs) to potentially create healthstones if the spell is cast on them, bypassing inventory checks.

## Member Reference

**OnEffectExecute#4**: Maps demon creature entries to buff spell IDs and casts the appropriate buff on the caster; logs errors for unhandled entries.
**GetScript_WarlockDemonicSacrifice**: Factory function for `WarlockDemonicSacrificeScript`.
**OnCheckCast**: Validates that the target has an Immolate aura applied by the caster; fails if not found.
**OnEffectExecute**: Removes the caster's Immolate aura from the target upon Conflagrate execution.
**GetScript_WarlockConflagrate**: Factory function for `WarlockConflagrateScript`.
**OnCheckCast#3**: Calculates Life Tap health cost, applies modifiers, and validates sufficient health; fails if health is too low.
**OnEffectExecute#5**: Executes Life Tap: reduces health, calculates mana gain (including Improved Life Tap talent bonus), and grants mana via custom spell.
**GetScript_WarlockLifeTap**: Factory function for `WarlockLifeTapScript`.
**OnEffectExecute#3**: Legacy logic for Curse of Agony dummy effect (client build ≤ 1.10.2); calculates damage and casts custom spell.
**GetScript_WarlockCurseOfAgonyDummy**: Factory function for `WarlockCurseOfAgonyDummyScript`.
**OnSuccessfulDispel**: Triggers a heal spell on the caster when Devour Magic successfully dispels a magic effect.
**GetScript_WarlockDevourMagic**: Factory function for `WarlockDevourMagicScript`.
**OnSummon**: Applies enslavement, root, and Inferno effect spells to the summoned Infernal.
**GetScript_WarlockInferno**: Factory function for `WarlockInfernoScript`.
**GetItemId**: Determines the correct Healthstone item ID based on spell rank and Improved Healthstone talent aura.
**OnCheckCast#2**: Validates inventory space for the Healthstone item; sends error if full.
**OnEffectExecute#2**: Creates the Healthstone item in the target's inventory.
**GetScript_WarlockCreateHealthstone**: Factory function for `WarlockCreateHealthstoneScript`.
**OnCheckCast#4**: Validates Ritual of Summoning target: must be in same raid, not in combat, and in same instance (if dungeon) or not in battleground.
**GetScript_WarlockRitualOfSummoning**: Factory function for `WarlockRitualOfSummoningScript`.
**OnPeriodicTrigger**: Prevents Curse of Idiocy from stacking beyond -90 Intellect and -90 Spirit by nullifying spell info.
**GetScript_WarlockCurseOfIdiocy**: Factory function for `WarlockCurseOfIdiocyAuraScript`.
**AddSC_warlock_spell_scripts**: Registers all Warlock spell scripts with the script manager.

---

<!-- machine-true, projected from graph.json -->

## Map — spell_warlock

*Source:* spell_warlock.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| OnEffectExecute#4 | method | Log.Main/Out, Object/GetEntry, Spell.Main/GetUnitTarget, SpellCaster/CastSpell#2 | — | — |
| GetScript_WarlockDemonicSacrifice | function | — | — | — |
| OnCheckCast | method | Aura/GetCasterGuid, Aura/GetSpellProto, Object/GetObjectGuid, ObjectGuid/operator==, SpellCastTargetsInfo/getUnitTarget, Unit.Main/GetAurasByType | — | — |
| OnEffectExecute | method | Aura/GetCasterGuid, Aura/GetId, Aura/GetSpellProto, Object/GetObjectGuid, ObjectGuid/operator==, Spell.Main/GetUnitTarget, Unit.Main/GetAurasByType, Unit.Main/RemoveAurasByCasterSpell | — | — |
| GetScript_WarlockConflagrate | function | — | — | — |
| OnCheckCast#3 | method | SpellCaster/SpellDamageBonusDone, Unit.Main/GetHealth, Unit.Main/GetSpellModOwner, Unit.Main/SpellDamageBonusTaken | — | — |
| OnEffectExecute#5 | method | Aura/GetModifier, Aura/GetSpellProto, shared_Util/dither, Spell.Main/SendCastResult, SpellCaster/CalculateSpellEffectValue, SpellCaster/CastCustomSpell#2, SpellCaster/SpellDamageBonusDone, Unit.Main/GetAurasByType, Unit.Main/GetHealth, Unit.Main/GetSpellModOwner, Unit.Main/ModifyHealth, Unit.Main/SpellDamageBonusTaken | — | — |
| GetScript_WarlockLifeTap | function | — | — | — |
| OnEffectExecute#3 | method | — | — | — |
| GetScript_WarlockCurseOfAgonyDummy | function | — | — | — |
| OnSuccessfulDispel | method | Log.Main/Out, SpellCaster/CastSpell#2 | — | — |
| GetScript_WarlockDevourMagic | function | — | — | — |
| OnSummon | method | SpellCaster/CastSpell#2 | — | — |
| GetScript_WarlockInferno | function | — | — | — |
| GetItemId | method | Aura/GetId, Log.Main/Out, Unit.Main/GetAurasByType | — | — |
| OnCheckCast#2 | method | Object/ToPlayer, Player.Main/CanStoreNewItem, Player.Main/SendEquipError | — | — |
| OnEffectExecute#2 | method | Spell.Effects/DoCreateItem, Spell.Main/GetUnitTarget | — | — |
| GetScript_WarlockCreateHealthstone | function | — | — | — |
| OnCheckCast#4 | method | MapEntry/IsDungeon, Object/ToPlayer, ObjectGuid/operator!, ObjectMgr/GetPlayer, Player.Main/GetSelectionGuid, Player.Main/InBattleGround, Player.Main/IsInSameRaidWith, Unit.Main/IsInCombat, WorldObject.Object/GetMap, WorldObject.Object/GetMapId | — | — |
| GetScript_WarlockRitualOfSummoning | function | — | — | — |
| OnPeriodicTrigger | method | Aura/GetId, Aura/GetModifier, Object/GetObjectGuid, ObjectGuid/operator==, Unit.Main/GetAurasByType | — | — |
| GetScript_WarlockCurseOfIdiocy | function | — | — | — |
| AddSC_warlock_spell_scripts | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
