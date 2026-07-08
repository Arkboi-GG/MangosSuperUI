# spell_special

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# spell_special

**Purpose & Responsibilities**

`spell_special.cpp` implements custom logic for a diverse set of spells and auras that require behavior beyond standard engine mechanics. It acts as a collection of specialized scripts handling:

1.  **Game Master (GM) Tools:** Scripts to toggle GM chat visibility, GM mode status, GM invisibility, and the "Beastmaster" cheat flag.
2.  **Darkmoon Faire Mechanics:** Logic for the Steam Tonk vehicle (control console interaction, cannon firing, mana consumption for turrets/flamethrowers) and cleanup when exiting the vehicle.
3.  **Racial Abilities:** Custom visual effects and dispel immunity for Night Elf Shadowmeld and Dwarf Stoneform.
4.  **Combat & Utility Spells:** Damage distribution for Meteor, cooldown clearing, Cannibalize animations, and Silithyst PvP buffs.
5.  **World Events:** Visual effects for opening Battleground banners.

This unit does not access any database tables directly; all logic is driven by in-memory game objects, spell entries, and player states.

## Member-by-Member Behavior

### GM Tool Scripts
These scripts allow Game Masters to toggle specific administrative flags via spells. They follow a consistent pattern: check if the caster is a `Player`, then call the corresponding setter on the `Player` object with `true` (enable) or `false` (disable).

*   **Showlabel Off/On (`OnEffectExecute#9`, `OnEffectExecute#10`)**: Toggle the GM chat label visibility. Calls `Player.Main/SetGMChat`.
*   **GM On/Off (`OnEffectExecute#5`, `OnEffectExecute#6`)**: Toggle the GM mode status. Calls `Player.Main/SetGameMaster`.
*   **Invis On/Off (`OnEffectExecute#7`, `OnEffectExecute#8`)**: Toggle GM invisibility. Calls `Player.Main/SetGMVisible`. Note that `InvisOff` sets visibility to `true` (visible), while `InvisOn` sets it to `false` (invisible).
*   **Beastmaster On/Off (`OnEffectExecute`, `OnEffectExecute#2`)**: Toggle the Beastmaster cheat flag. Calls `Player.Main/SetCheatBeastmaster`.

### Darkmoon Steam Tonk Mechanics
The Steam Tonk is a vehicle-like mechanic involving multiple spells and auras.

*   **Control Console (`OnInit`)**: When the player casts the control console spell, this script immediately unsummons any existing Hunter or Warlock pet via `Player.Main/UnsummonPetTemporaryIfAny`. This prevents the player from being stuck in place if they already have a pet active.
*   **Cannon (`OnEffectExecute#4`)**: When the tonk fires its cannon, this script identifies the unit target and casts spell ID 27766 (likely the projectile or damage spell) on that target via `SpellCaster/CastSpell#2`.
*   **Mana Consumption (`OnPeriodicTrigger`)**: Used by both the MG Turret and Flamethrower auras. Each tick, it checks if the target has at least 10 Mana. If yes, it deducts 10 Mana and sends a log update via `SpellCaster/SendEnergizeSpellLog`. If no, it removes the aura entirely and cancels the trigger spell by setting `spellInfo` to `nullptr`.
*   **Exiting the Tonk (`OnAfterApply#2`)**: Triggered when the "Controlling Steam Tonk" aura is removed. It performs cleanup:
    1.  Casts "Damaged Tonk" (27771) on the tonk itself.
    2.  Casts a 3-second stun (9179) on the player.
    3.  Removes the "Unroot" aura (24935) from the player.
    4.  Finds the nearest Control Console GameObject (180524) and resets its state to ready, loot state to ready, and removes the "in use" flag.

### Racial Abilities

*   **Shadowmeld (`OnAfterApply`, `OnBeforeApply`)**:
    *   **Apply**: When Shadowmeld is applied, it casts the visual effect spell "Elusiveness" (21009) on the player.
    *   **Remove**: When Shadowmeld is removed, it removes the "Elusiveness" aura via `Unit.Main/RemoveAurasDueToSpell`.
*   **Stoneform (`OnAfterApply#5`)**: Grants immunity to Disease and Poison dispels. It calls `Unit.Main/ApplySpellDispelImmunity` for both dispel types. The effect index checked depends on the client build: Index 2 for builds ≤ 1.6.1, Index 0 for newer builds.

### Combat & Utility Spells

*   **Meteor (`OnEffectExecute#9`)**: Modifies the total damage of the spell by dividing it by the number of unique targets hit. This ensures the total damage output remains constant regardless of how many targets are struck, distributing the damage evenly among them.
*   **Clear All Cooldowns (`OnEffectExecute#3`)**: Calls `SpellCaster/RemoveAllCooldowns` on the caster unit to reset all spell cooldowns.
*   **Cannibalize (`OnSuccessfulFinish`)**: After the Cannibalize spell finishes casting, it applies the "Cannibalize Aura" (20578) to the caster.
*   **Cannibalize Aura (`OnAfterApply`, `OnPeriodicTickEnd`)**:
    *   **Apply/Remove**: Handles the emote state. When the aura is removed, it stops the cannibalize animation (`EMOTE_STATE_NONE`).
    *   **Tick**: During periodic ticks, if the target is alive and their current power type matches the aura's misc value, it triggers the cannibalize animation (`EMOTE_STATE_CANNIBALIZE`).
*   **Silithyst (`OnAfterApply#4`)**: Handles the Silithyst PvP buff in Silithus (Zone 1377).
    *   **Apply**: Casts the team-specific buff (Alliance: 29894, Horde: 29895).
    *   **Remove**: Removes the team-specific buff. If the aura was removed by cancel, death, or dispel, it notifies the zone script via `ZoneScript/HandleDropFlag#3` to handle outdoor PvP flag dropping.

### World Events

*   **Opening Battleground Banner (`OnSuccessfulStart`)**: When a player successfully opens a battleground banner (GameObject), this script checks if the lock type is `LOCKTYPE_SLOW_OPEN`. If so, it creates and prepares a visual spell (24390) on the caster to show the opening animation.

## Cross-Unit Boundaries

This unit interacts primarily with `Player`, `Unit`, `Spell`, `Aura`, `GameObject`, and `ZoneScript` classes.

*   **Player.Main**: Called extensively by GM tool scripts to modify player flags (`SetGMChat`, `SetGameMaster`, `SetGMVisible`, `SetCheatBeastmaster`) and by the Tonk script to unsummon pets (`UnsummonPetTemporaryIfAny`).
*   **SpellCaster**: Used to cast secondary spells (e.g., Tonk cannon shot, Cannibalize aura, Shadowmeld visuals) via `CastSpell#2` and to send power change logs (`SendEnergizeSpellLog`).
*   **Unit.Main**: Used to remove auras (`RemoveAurasDueToSpell`), apply dispel immunity (`ApplySpellDispelImmunity`), handle emotes (`HandleEmoteCommand`), and manage power (`GetPower`, `ModifyPower`).
*   **GameObject**: Used to find and reset the Tonk Control Console (`FindNearestGameObject`, `SetGoState`, `SetLootState`, `RemoveFlag`) and to get lock information for the Banner script (`GetGOInfo`, `GetLockId`).
*   **ZoneScript**: Called by the Silithyst script to handle flag dropping in outdoor PvP zones (`HandleDropFlag#3`).
*   **SpellMgr/LockStore**: Used to look up spell entries and lock information for validation and visual effects.

## Data Model

This unit does not interact with any database tables. All data is derived from in-game spell entries, object definitions, and runtime state.

## Notable Implementation Details

*   **Damage Distribution in Meteor**: The `MeteorScript` manually divides the total damage by the number of targets. This is a specific design choice to prevent damage scaling with target count, ensuring balance.
*   **Client Build Compatibility**: The `Stoneform` script uses preprocessor directives (`#if SUPPORTED_CLIENT_BUILD`) to check different effect indices for dispel immunity depending on the client version. This highlights the need to maintain backward compatibility with older client protocols.
*   **Tonk Cleanup Complexity**: Exiting the Steam Tonk involves multiple steps: damaging the vehicle, stunning the player, removing movement modifiers, and resetting the interactive GameObject. Failure to reset the GameObject would leave it unusable for other players.
*   **Silithyst PvP Integration**: The Silithyst script tightly couples with the zone script system. It doesn't just manage buffs; it triggers world-state changes (flag dropping) when the buff is lost under specific conditions, integrating spell logic with zone-level PvP mechanics.
*   **Mana Check Before Tick**: The Darkmoon Faire turret/flamethrower script checks mana availability *before* applying the effect. If insufficient mana, it removes the aura and cancels the spell trigger. This prevents negative mana states and ensures the ability stops working correctly when resources are depleted.

## Member Reference

*   **OnEffectExecute#9**: Divides Meteor spell damage by the number of unique targets hit to distribute damage evenly.
*   **GetScript_Meteor**: Factory function returning a new `MeteorScript` instance.
*   **OnInit**: Unsummons any existing pet when the Darkmoon Steam Tonk Control Console is used to prevent movement locks.
*   **GetScript_DarkmoonSteamTonkControlConsole**: Factory function returning a new `DarkmoonSteamTonkControlConsoleScript` instance.
*   **OnEffectExecute#4**: Casts the cannon projectile spell (27766) on the targeted unit when the Tonk fires.
*   **GetScript_DarkmoonSteamTonkCannon**: Factory function returning a new `DarkmoonSteamTonkCannonScript` instance.
*   **OnEffectExecute#10**: Disables GM chat label visibility for the caster player.
*   **GetScript_ShowlabelOff**: Factory function returning a new `ShowlabelOffScript` instance.
*   **OnEffectExecute#11**: Enables GM chat label visibility for the caster player.
*   **GetScript_ShowlabelOn**: Factory function returning a new `ShowlabelOnScript` instance.
*   **OnEffectExecute#5**: Disables GM mode for the caster player.
*   **GetScript_GMOff**: Factory function returning a new `GMOffScript` instance.
*   **OnEffectExecute#6**: Enables GM mode for the caster player.
*   **GetScript_GMOn**: Factory function returning a new `GMOnScript` instance.
*   **OnEffectExecute#7**: Makes the caster player visible (disables GM invisibility).
*   **GetScript_InvisOff**: Factory function returning a new `InvisOffScript` instance.
*   **OnEffectExecute#8**: Makes the caster player invisible (enables GM invisibility).
*   **GetScript_InvisOn**: Factory function returning a new `InvisOnScript` instance.
*   **OnEffectExecute**: Disables the Beastmaster cheat flag for the caster player.
*   **GetScript_BMOff**: Factory function returning a new `BMOffScript` instance.
*   **OnEffectExecute#2**: Enables the Beastmaster cheat flag for the caster player.
*   **GetScript_BMOn**: Factory function returning a new `BMOnScript` instance.
*   **OnEffectExecute#3**: Removes all cooldowns from the caster unit.
*   **GetScript_ClearAllCooldowns**: Factory function returning a new `ClearAllCooldownsScript` instance.
*   **OnSuccessfulStart**: Checks if a battleground banner is being opened with a slow lock type and casts a visual spell (24390) if so.
*   **GetScript_OpeningBattlegroundBanner**: Factory function returning a new `OpeningBattlegroundBannerScript` instance.
*   **OnSuccessfulFinish**: Applies the Cannibalize Aura (20578) to the caster after the Cannibalize spell finishes.
*   **GetScript_Cannibalize**: Factory function returning a new `CannibalizeSpellScript` instance.
*   **OnAfterApply**: Stops the cannibalize emote when the Cannibalize Aura is removed.
*   **OnPeriodicTickEnd**: Triggers the cannibalize emote during periodic ticks if the target is alive and consuming the correct power type.
*   **GetScript_CannibalizeAura**: Factory function returning a new `CannibalizeAuraScript` instance.
*   **OnAfterApply#4**: Applies or removes team-specific Silithyst buffs in Silithus and handles flag dropping on removal.
*   **GetScript_Silithyst**: Factory function returning a new `SilithystAuraScript` instance.
*   **OnPeriodicTrigger**: Deducts 10 Mana per tick for MG Turret/Flamethrower; removes aura if insufficient mana.
*   **GetScript_ActivateMGTurret**: Factory function returning a new `DarkmoonFaireManaConsumptionAuraScript` instance.
*   **GetScript_Flamethrower**: Factory function returning a new `DarkmoonFaireManaConsumptionAuraScript` instance.
*   **OnAfterApply#2**: Cleans up after exiting the Steam Tonk: damages tonk, stuns player, removes roots, and resets the control console GameObject.
*   **GetScript_ControllingSteamTonk**: Factory function returning a new `ControllingSteamTonkAuraScript` instance.
*   **OnAfterApply#3**: (Note: This entry in the MAP seems to correspond to the `OnAfterApply` in `ShadowmeldAuraScript` or similar, but based on source, `Shadowmeld` has `OnAfterApply` and `OnBeforeApply`. The MAP lists `OnAfterApply#3` calling `CastSpell#2`. Looking at source, `ShadowmeldAuraScript::OnAfterApply` casts `SPELL_ELUSIVENESS`. Let's verify MAP vs Source.
    *   MAP: `OnAfterApply#3` -> `SpellCaster/CastSpell#2`.
    *   Source: `ShadowmeldAuraScript::OnAfterApply` calls `target->CastSpell(...)`.
    *   Source: `ControllingSteamTonkAuraScript::OnAfterApply` calls `target->CastSpell(...)` and `player->CastSpell(...)`.
    *   Source: `SilithystAuraScript::OnAfterApply` calls `player->CastSpell(...)`.
    *   Source: `CannibalizeAuraScript::OnAfterApply` does NOT call CastSpell.
    *   Source: `StoneformAuraScript::OnAfterApply` does NOT call CastSpell.
    *   The MAP has `OnAfterApply#2` for `ControllingSteamTonk` and `OnAfterApply#4` for `Silithyst`.
    *   The MAP has `OnAfterApply#3` calling `CastSpell#2`. In the source, `ShadowmeldAuraScript::OnAfterApply` calls `CastSpell`. So `OnAfterApply#3` is likely `ShadowmeldAuraScript::OnAfterApply`.
    *   Wait, the MAP also lists `OnBeforeApply` for `Shadowmeld`.
    *   Let's check the MAP again.
    *   `OnAfterApply#3`: Calls `SpellCaster/CastSpell#2`.
    *   `OnBeforeApply`: Calls `Unit.Main/RemoveAurasDueToSpell`.
    *   This matches `ShadowmeldAuraScript`.
*   **GetScript_Shadowmeld**: Factory function returning a new `ShadowmeldAuraScript` instance.
*   **OnAfterApply#5**: Applies disease and poison dispel immunity for Stoneform, checking effect index based on client build.
*   **GetScript_Stoneform**: Factory function returning a new `StoneformAuraScript` instance.
*   **AddSC_special_spell_scripts**: Registers all special spell and aura scripts with the script manager.

---

<!-- machine-true, projected from graph.json -->

## Map — spell_special

*Source:* spell_special.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| OnEffectExecute#9 | method | — | — | — |
| GetScript_Meteor | function | — | — | — |
| OnInit | method | Object/ToPlayer, Player.Main/UnsummonPetTemporaryIfAny, Spell.Main/GetCaster | — | — |
| GetScript_DarkmoonSteamTonkControlConsole | function | — | — | — |
| OnEffectExecute#4 | method | Spell.Main/GetUnitTarget, SpellCaster/CastSpell#2 | — | — |
| GetScript_DarkmoonSteamTonkCannon | function | — | — | — |
| OnEffectExecute#10 | method | Object/ToPlayer, Player.Main/SetGMChat | — | — |
| GetScript_ShowlabelOff | function | — | — | — |
| OnEffectExecute#11 | method | Object/ToPlayer, Player.Main/SetGMChat | — | — |
| GetScript_ShowlabelOn | function | — | — | — |
| OnEffectExecute#5 | method | Object/ToPlayer, Player.Main/SetGameMaster | — | — |
| GetScript_GMOff | function | — | — | — |
| OnEffectExecute#6 | method | Object/ToPlayer, Player.Main/SetGameMaster | — | — |
| GetScript_GMOn | function | — | — | — |
| OnEffectExecute#7 | method | Object/ToPlayer, Player.Main/SetGMVisible | — | — |
| GetScript_InvisOff | function | — | — | — |
| OnEffectExecute#8 | method | Object/ToPlayer, Player.Main/SetGMVisible | — | — |
| GetScript_InvisOn | function | — | — | — |
| OnEffectExecute | method | Object/ToPlayer, Player.Main/SetCheatBeastmaster | — | — |
| GetScript_BMOff | function | — | — | — |
| OnEffectExecute#2 | method | Object/ToPlayer, Player.Main/SetCheatBeastmaster | — | — |
| GetScript_BMOn | function | — | — | — |
| OnEffectExecute#3 | method | SpellCaster/RemoveAllCooldowns | — | — |
| GetScript_ClearAllCooldowns | function | — | — | — |
| OnSuccessfulStart | method | GameObject/GetGOInfo, GameObjectInfo/GetLockId, Spell.Main/prepare#2, Spell.Main/Spell#2, SpellCastTargetsInfo/getGOTarget, SpellMgr/GetSpellEntry, SpellMgr/Instance | — | — |
| GetScript_OpeningBattlegroundBanner | function | — | — | — |
| OnSuccessfulFinish | method | Spell.Main/GetCorpseTarget, Spell.Main/GetUnitTarget, SpellCaster/CastSpell#2 | — | — |
| GetScript_Cannibalize | function | — | — | — |
| OnAfterApply | method | Aura/GetEffIndex, Aura/GetTarget, Unit.Main/HandleEmoteCommand | — | — |
| OnPeriodicTickEnd | method | Aura/GetTarget, Unit.Main/GetPowerType, Unit.Main/HandleEmoteCommand, Unit.Main/IsAlive, Unit.SpellAuras/GetMiscValue | — | — |
| GetScript_CannibalizeAura | function | — | — | — |
| OnAfterApply#4 | method | Aura/GetEffIndex, Aura/GetHolder, Aura/GetId, Aura/GetTarget, Object/GetTypeId, Object/ToPlayer, Player.Main/GetTeam, Player.Main/GetZoneScript, SpellAuraHolder/GetRemoveMode, SpellCaster/CastSpell#2, Unit.Main/RemoveAurasDueToSpell, WorldObject.Object/GetZoneId, ZoneScript/HandleDropFlag#3 | — | — |
| GetScript_Silithyst | function | — | — | — |
| OnPeriodicTrigger | method | Aura/GetId, SpellCaster/SendEnergizeSpellLog, Unit.Main/GetPower, Unit.Main/ModifyPower, Unit.Main/RemoveAurasDueToSpell | — | — |
| GetScript_ActivateMGTurret | function | — | — | — |
| GetScript_Flamethrower | function | — | — | — |
| OnAfterApply#2 | method | Aura/GetCaster, Aura/GetEffIndex, Aura/GetTarget, GameObject/SetGoState, GameObject/SetLootState, Object/GetTypeId, Object/ToPlayer, SpellCaster/CastSpell#2, Unit.Main/RemoveAurasDueToSpell, WorldObject.Object/FindNearestGameObject, WorldObject.Object/RemoveFlag | — | — |
| GetScript_ControllingSteamTonk | function | — | — | — |
| OnAfterApply#3 | method | Aura/GetEffIndex, Aura/GetTarget, Object/GetTypeId, SpellCaster/CastSpell#2 | — | — |
| OnBeforeApply | method | Aura/GetEffIndex, Aura/GetTarget, Object/GetTypeId, Unit.Main/RemoveAurasDueToSpell | — | — |
| GetScript_Shadowmeld | function | — | — | — |
| OnAfterApply#5 | method | Aura/GetEffIndex, Aura/GetSpellProto, Aura/GetTarget, Unit.Main/ApplySpellDispelImmunity | — | — |
| GetScript_Stoneform | function | — | — | — |
| AddSC_special_spell_scripts | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
