# spell_item

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# spell_item

**Purpose & Responsibilities**

`spell_item.cpp` implements custom script hooks for a large set of item-based spells in the World of Warcraft server emulator. These scripts override default spell behavior to enforce specific game mechanics, such as random effect selection, conditional casting restrictions, quest-specific interactions, and complex summon behaviors. The unit does not define new data structures; instead, it provides `SpellScript` and `AuraScript` subclasses that are registered via factory functions (`GetScript_*`) and hooked into the core engine by `AddSC_item_spell_scripts`.

The unit handles three primary categories of logic:
1.  **Randomized Consumables:** Items like Deviate Fish, Noggenfogger Elixir, and Scorpid Surprise that trigger different secondary spells based on probability.
2.  **Conditional Casting Checks:** Spells that require specific states (e.g., mounted, holding a specific aura, targeting a specific NPC) to cast successfully, such as the Black Qiraji Battle Tank or Reindeer Transformation.
3.  **Complex Summon/Quest Interactions:** Spells that summon creatures with specific AI behaviors (following waypoints, whispering text) or modify reputation/faction states upon application (Ashbringer, Target Dummies).

**Data Model**

This unit does not interact with any database tables. All logic is driven by in-memory objects (`Spell`, `Aura`, `Unit`, `Player`, `Creature`) and hardcoded spell/NPC IDs.

## Member-by-Member Behavior

### Randomized Effect Execution
These methods handle the execution phase of spells where the outcome is determined by random number generation. They typically retrieve the caster as a `Player` and cast a secondary spell based on the result.

*   **OnEffectExecute#2** (`DeviateFishScript`): Triggers when a player consumes a Deviate Fish. It selects one of six possible spells (Sleepy, Invigorate, Shrink, Party Time, Healthy Spirit, Rejuvenation) uniformly at random using `PickRandomValue` (via `shared_Util/urand` implicitly in the macro) and casts it on the player.
*   **OnEffectExecute#3** (`CookedDeviateFishScript`): Handles Cooked Deviate Fish. The pool of effects depends on the client build. In older builds (≤ 1.5.1), it picks from 6 effects including gender-specific ones (Ninja/Pirate). In newer builds, it restricts the pool to only the first two gender-specific effects. It uses `shared_Util/urand` for selection and `Unit.Main/GetGender` to determine the correct spell ID.
*   **OnEffectExecute#11** (`NoggenfoggerElixirScript`): Implements the Noggenfogger Elixir logic. It uses a weighted random distribution: 60% chance for Skeleton, 20% for Miniature, and 20% for Slow Fall. It uses `shared_Util/urand(1, 10)` to determine the outcome and casts the corresponding spell on the player.
*   **OnEffectExecute#9** (`LinkensBoomerangScript`): Handles Linken's Boomerang procs. It checks specific effect indices. For index 1, it returns `false` (blocking the default effect) with a ~33% probability (`urand(0, 30)` check implies 1/31 chance to pass, effectively blocking most, but the comment says 10% stun/3% disarm; the code `if (urand(0, 30)) return false;` means it proceeds only if `urand` returns 0, which is 1/31 ≈ 3.2%. Wait, `urand(0, 30)` returns 0-30. If it returns non-zero, it returns false. So it proceeds only if 0. That is 1/31. The comment says "10% chance to proc stun". The code seems to implement a low-probability bypass. Actually, looking closely: `if (urand(0, 30)) return false;` means if the random number is *not* 0, it stops. So it continues only 1/31 of the time. This likely allows the base spell effect to proceed or triggers a hidden proc. For index 2, it uses `urand(0, 10)`, proceeding only if 0 (1/11 ≈ 9%).
*   **OnEffectExecute#12** (`ScorpidSurpriseScript`): Handles Scorpid Surprise. For effect index 1, it blocks the effect with a ~91% probability (`urand(0, 10)` returns 0-10; if non-zero, return false). This simulates the "poison sac" chance where the heal fails.
*   **OnEffectExecute#5** (`EverlookTransporterScript`): Implements the Dimensional Ripper. It generates a random number 0-119. If ≥ 70 (7/12 chance), it succeeds. Within success, if < 100 (4/12 total, or 4/7 of success), it casts the "Evil Twin" spell. Else (1/12 total), it casts the "Fire" spell. Uses `shared_Util/irand`.
*   **OnEffectExecute#7** (`GoblinJumperCablesScript`): Handles Goblin Jumper Cables. It uses `roll_chance_i(67)` to determine failure. If it rolls failure (67% chance), it casts the "Defibrillate" success spell (8338) and returns `false` to stop further processing. If it rolls success (33%), it returns `true`, allowing the base spell effect to proceed (which likely does nothing or heals normally). *Note: The logic here is inverted compared to typical "proc" scripts; it explicitly casts the success spell on failure of the roll.*
*   **OnEffectExecute#8** (`GoblinJumperCablesXLScript`): Similar to above, but for XL cables. Uses `roll_chance_i(50)` (50% fail rate). On fail, casts success spell 23055.
*   **OnEffectExecute#4** (`DiggingClawScript`): Handles Digging Claw. For effects 1 and 2, it checks if a specific creature (defined in spell misc value) exists within 10 yards using `WorldObject.Object/FindNearestCreature`. If found, it returns `false` (preventing the dig). If not found, it uses `roll_chance_i(20)` to determine if the dig succeeds (returns `true` if roll passes, `false` otherwise? No, `roll_chance_i(20)` returns true 20% of the time. If true, it returns `true`? Wait. `return roll_chance_i(20);` means if the roll is successful (20%), it returns `true` (allowing the effect). If the roll fails (80%), it returns `false` (blocking the effect). This simulates a 20% chance to find the egg.
*   **OnEffectExecute#13** (`TanarisFieldSamplingScript`): Handles field sampling items. For effect index 0, it returns `roll_chance_i(50)`, giving a 50% chance for the effect to proceed.

### Conditional Casting Checks
These methods intercept the cast attempt to validate preconditions.

*   **OnCheckCast#2** (`HeavyArmorKitScript`): Validates that the target item's level is at least 15. It retrieves the target item via `SpellCastTargetsInfo/getItemTarget` and checks `game_Objects_Item/GetProto`. Returns `SPELL_FAILED_LOWLEVEL` if invalid.
*   **OnCheckCast** (`BagOfGoldScript`): Validates that the caster has the "Narain's Turban" aura (ID 25688). It retrieves the effective caster via `Spell.Main/GetAffectiveCaster`, casts to `Player.Main/ToPlayer`, and checks `Unit.Main/HasAura#2`. Returns `SPELL_FAILED_TARGET_AURASTATE` if missing.
*   **OnCheckCast#3** (`KodoKombobulatorScript`): Prevents casting if the caster already has the quest credit aura (18172). Checks `Unit.Main/HasAura#2`. Returns `SPELL_FAILED_ITEM_NOT_READY` if present.
*   **OnCheckCast#4** (`MelodiusRaptureScript`): Validates that the target is a Deeprun Rat (NPC 13016). Retrieves target via `SpellCastTargetsInfo/getUnitTarget` and checks `Object/GetEntry`. Returns `SPELL_FAILED_BAD_TARGETS` if mismatch.
*   **OnCheckCast#5** (`PurifyAndPlaceFoodScript`): Prevents casting if the scripted map event 3938 is active. Retrieves the map via `WorldObject.Object/GetMap` and checks `Map.Main/GetScriptedMapEvent`. Returns `SPELL_FAILED_NOT_READY` if active.
*   **OnCheckCast#6** (`ReindeerTransformationScript`): Requires the caster to be mounted. Checks `Unit.Main/HasAuraType` for `SPELL_AURA_MOUNTED`. Returns `SPELL_FAILED_ONLY_MOUNTED` if not mounted.
*   **OnCheckCast#7** (`SummonBlackQirajiBattleTankScript`): Complex validation for summoning the battle tank.
    *   If mounted, removes mount aura and returns `SPELL_FAILED_DONT_REPORT`.
    *   If in water (and not a player in high liquid), returns `SPELL_FAILED_ONLY_ABOVEWATER`.
    *   If on a transport, returns `SPELL_FAILED_NO_MOUNTS_ALLOWED`.
    *   If not in Ahn'Qiraj Temple and the map doesn't allow mounts (checked via `MapEntry/IsMountAllowed`), and not triggered, returns `SPELL_FAILED_NO_MOUNTS_ALLOWED`.
    *   If in Area ID 35, returns `SPELL_FAILED_NO_MOUNTS_ALLOWED`.
    *   If in a disallowed mount form, returns `SPELL_FAILED_NOT_SHAPESHIFT`.
    *   Uses `Unit.Main/IsMounted`, `Unit.Main/IsInWater`, `Player.Main/IsInHighLiquid`, `WorldObject.Object/GetTransport`, `WorldObject.Object/GetMapId`, `WorldObject.Object/GetAreaId`, `Unit.Main/IsInDisallowedMountForm`, `Unit.Main/RemoveSpellsCausingAura`, `Spell.Main/IsTriggered`, `Object/IsPlayer`, `Object/ToPlayer`.

### Summon and Aura Application Logic
These methods handle side-effects when a spell summons a creature or applies an aura.

*   **OnSummon** (`ChainedEssenceOfEranikusScript`): When the poison cloud is summoned, it whispers a random text ID (4438-4445) to the caster using `WorldObject.Object/MonsterWhisper#2`.
*   **OnSummon#2** (`ReleaseUmisYetiScript`): Handles the "Release Umi's Yeti" quest.
    *   Emotes and speaks text via `WorldObject.Object/MonsterTextEmote#2` and `WorldObject.Object/MonsterSay#2`.
    *   Determines behavior based on `WorldObject.Object/GetAreaId`:
        *   Un'Goro (541): Finds NPC Quixxil (10977) via `WorldObject.Object/FindNearestCreature`. Makes the yeti follow (`Creature.MotionMaster/MoveFollow`) and sets the NPC to walk (`Unit.Main/SetWalk`) and move on waypoint (`Creature.MotionMaster/MoveWaypoint`).
        *   Tanaris (976): Finds NPC Sprinkle (7583). Same follow/waypoint logic.
        *   Winterspring (2255): Finds NPC Legacki (10978). Same follow/waypoint logic.
*   **OnSummon#3** (`TargetDummyScript`): Configures the summoned dummy. Sets its faction to match the caster temporarily (`Creature.Main/SetFactionTemporary` using `WorldObject.Object/GetFactionTemplateId`) and sets the `UNIT_FLAG_PLAYER_CONTROLLED` flag via `WorldObject.Object/SetUInt32Value`.
*   **OnSummon#4** (`VanquishedTentacleofCthunScript`): Configures the summoned tentacle. Retrieves `Unit.Main/GetCharmInfo` and sets it to stay (`CharmInfo/SetCommandState`, `CharmInfo/SetIsAtStay`, `CharmInfo/SetIsCommandFollow`) and saves the position (`Unit.Main/SaveStayPosition`).
*   **OnAfterApply** (`AshbringerAuraScript`): Handles the Ashbringer weapon aura.
    *   Applies a forced Friendly reputation with Scarlet Crusade (Faction 56) using `Player.Main/GetReputationMgr`, `ReputationMgr/ApplyForceReaction`, and `ReputationMgr/SendForceReactions`.
    *   Stops attacking the Scarlet Crusade faction if the forced rank is Friendly or if the real rank becomes Friendly upon removal, using `Player.Main/StopAttackFaction` and `Player.Main/GetReputationRank`.
    *   Checks `Object/GetTypeId` and `Object/ToPlayer` to ensure the target is a player.
*   **OnAfterApply#2** (`DiscombobulateAuraScript`): When the Discombobulate aura is applied, it removes any mount auras from the target using `Unit.Main/RemoveSpellsCausingAura`.

### Periodic and Channeling Logic
*   **OnEffectExecute#10** (`BrittleArmorDummyScript`): When the dummy spell hits, it casts spell 24575 on the unit target via `SpellCaster/CastSpell#2`.
*   **OnEffectExecute** (`MercurialShieldDummyScript`): When the dummy spell hits, it casts spell 26464 on the unit target via `SpellCaster/CastSpell#2`.
*   **OnEffectExecute#6** (`GDRChannelScript`): When the GDR Channel spell executes effect 1, it casts the periodic damage spell (13493) on the caster via `SpellCaster/CastSpell#2`.
*   **OnAuraValueCalculate** (`GDRPeriodicDamageScript`): Calculates the damage amount for the periodic tick. Returns a random value between 100 and 500 using `shared_Util/urand`.
*   **OnPeriodicCalculateAmount** (`GDRPeriodicDamageScript`): Accumulates damage. It checks if the target is currently channeling the GDR Channel spell (13278) via `Aura/GetTarget` and `SpellCaster/GetCurrentSpell`. If not channeling, it zeroes the amount. Otherwise, it adds the amount to a member variable `dmg`.
*   **OnAfterApply#3** (`GDRPeriodicDamageScript`): Triggered when the aura expires. If removed by expiration and `dmg` > 0, it retrieves the target's current target GUID via `Unit.Main/GetTargetGuid`, finds that unit on the map via `Map.Main/GetUnit` and `WorldObject.Object/GetMap`, and casts the final damage hit spell (13279) with the accumulated `dmg` value using `SpellCaster/CastCustomSpell#2`.

### Other Spell Hooks
*   **OnSuccessfulStart** (`InstantCastScript`): Forces the spell cast time to 0 and resets the timer, effectively making the spell instant. Uses `Spell.Main/SetCastTime` and `Spell.Main/ReSetTimer`.
*   **OnCast** (`ElunesCandleScript`): Determines which omen spell to cast based on the target.
    *   Retrieves target via `SpellCastTargetsInfo/getUnitTarget`.
    *   If target is NPC 15467, picks a random omen spell from a list using `shared_Util/urand`.
    *   If target is NPC 15466, uses a fixed spell ID.
    *   Otherwise, uses a default spell ID.
    *   Casts the selected spell on the target via `SpellCaster/CastSpell#2`.
*   **OnAfterHit** (`FirstAidScript`): After a First Aid spell hits, it casts the "Recently Bandaged" spell (11196) on the target via `SpellCaster/CastSpell#2`.
*   **OnSuccessfulFinish** (`WolfsheadHelmScript`): After the helm spell finishes, it casts the energy spell (29940) on the caster via `SpellCaster/CastSpell#2`.

### Registration
*   **AddSC_item_spell_scripts**: Registers all the above scripts with the engine. It creates `Script` objects, assigns names and factory functions (`GetScript_*`), and calls `ScriptMgr/RegisterSelf`. It is called by `ScriptLoader/AddScripts`.

## Cross-Unit Boundaries

*   **`shared_Util/urand` / `irand` / `roll_chance_i`**: Used extensively by randomized effect scripts (`OnEffectExecute#2`, `#3`, `#11`, `#9`, `#12`, `#5`, `#7`, `#8`, `#4`, `#13`, `OnAuraValueCalculate`) to determine outcomes.
*   **`SpellCaster/CastSpell#2` / `CastCustomSpell#2`**: Called by almost all execution and summon scripts to trigger secondary spells or effects.
*   **`Object/ToPlayer` / `Player.Main/ToPlayer`**: Used to safely cast generic objects to `Player` pointers for accessing player-specific methods (reputation, auras, transport status).
*   **`Unit.Main/HasAura#2` / `HasAuraType`**: Used in check-cast scripts (`OnCheckCast`, `#3`, `#6`) to validate caster state.
*   **`WorldObject.Object/FindNearestCreature`**: Used by `OnSummon#2` (Yeti) and `OnEffectExecute#4` (Digging Claw) to locate NPCs or eggs.
*   **`Creature.MotionMaster/MoveFollow` / `MoveWaypoint`**: Used by `OnSummon#2` to control summoned creature movement.
*   **`Player.Main/GetReputationMgr` / `ReputationMgr/ApplyForceReaction`**: Used by `OnAfterApply` (Ashbringer) to modify faction standing.
*   **`Map.Main/GetScriptedMapEvent`**: Used by `OnCheckCast#5` to prevent casting during specific events.
*   **`SpellCastTargetsInfo/getItemTarget` / `getUnitTarget`**: Used by `OnCheckCast#2` and `OnCheckCast#4` / `OnCast` to identify targets.

## Notable Implementation Details

*   **Client Build Dependency:** `CookedDeviateFishScript` uses `#if SUPPORTED_CLIENT_BUILD <= CLIENT_BUILD_1_5_1` to change the pool of random effects. This ensures compatibility with older client expectations where more effects were available.
*   **Inverted Probability Logic:** In `GoblinJumperCablesScript` and `GoblinJumperCablesXLScript`, the code uses `roll_chance_i` to determine *failure*. If the roll indicates failure (high probability), it explicitly casts the "success" spell and returns `false`. This suggests the base spell effect might be a heal or nothing, and the script overrides it with the specific "defibrillate" visual/effect spell when the "failure" condition is met.
*   **Accumulated Damage:** `GDRPeriodicDamageScript` accumulates damage in a member variable `dmg` across multiple ticks. This total is only applied as a single hit (`SPELL_GDR_DAMAGE_HIT`) when the aura expires naturally (`AURA_REMOVE_BY_EXPIRE`). If the aura is removed by other means, the accumulated damage is lost.
*   **Hardcoded IDs:** Many spell and NPC IDs are hardcoded (e.g., `NPC_DEEPRUN_RAT = 13016`, `SPELL_RECENTLY_BANDAGED = 11196`). Changes to these IDs in the database would break the script behavior.
*   **Transport Check:** `SummonBlackQirajiBattleTankScript` explicitly checks `pPlayer->GetTransport()` to prevent casting while on a vehicle/transport, returning `SPELL_FAILED_NO_MOUNTS_ALLOWED`.

## Member Reference

**OnCheckCast#2**
Validates target item level for Heavy Armor Kit.

**GetScript_HeavyArmorKit**
Factory function for HeavyArmorKitScript.

**OnEffectExecute#3**
Executes Cooked Deviate Fish random effects, respecting client build.

**GetScript_DeviateFish**
Factory function for DeviateFishScript.

**OnEffectExecute#2**
Executes Deviate Fish random effects.

**GetScript_CookedDeviateFish**
Factory function for CookedDeviateFishScript.

**OnEffectExecute#11**
Executes Noggenfogger Elixir weighted random effects.

**GetScript_NoggenfoggerElixir**
Factory function for NoggenfoggerElixirScript.

**OnEffectExecute#9**
Handles Linken's Boomerang proc chances.

**GetScript_LinkensBoomerang**
Factory function for LinkensBoomerangScript.

**OnEffectExecute#12**
Handles Scorpid Surprise poison sac chance.

**GetScript_ScorpidSurprise**
Factory function for ScorpidSurpriseScript.

**OnEffectExecute**
Casts secondary spell for Brittle Armor Dummy.

**GetScript_BrittleArmorDummy**
Factory function for BrittleArmorDummyScript.

**OnEffectExecute#10**
Casts secondary spell for Mercurial Shield Dummy.

**GetScript_MercurialShieldDummy**
Factory function for MercurialShieldDummyScript.

**OnEffectExecute#5**
Handles Everlook Transporter random outcomes.

**GetScript_EverlookTransporter**
Factory function for EverlookTransporterScript.

**OnEffectExecute#6**
Triggers GDR Periodic Damage spell.

**GetScript_GDRChannel**
Factory function for GDRChannelScript.

**OnAuraValueCalculate**
Calculates random damage for GDR Periodic Damage.

**OnPeriodicCalculateAmount**
Accumulates GDR damage if channeling.

**OnAfterApply#3**
Applies accumulated GDR damage on aura expire.

**GetScript_GDRPeriodicDamage**
Factory function for GDRPeriodicDamageScript.

**OnSummon#3**
Configures Target Dummy faction and flags.

**GetScript_TargetDummy**
Factory function for TargetDummyScript.

**OnSummon**
Whispers text for Chained Essence of Eranikus.

**GetScript_ChainedEssenceOfEranikus**
Factory function for ChainedEssenceOfEranikusScript.

**OnSummon#2**
Handles Release Umi's Yeti quest logic (follow/waypoint).

**GetScript_ReleaseUmisYeti**
Factory function for ReleaseUmisYetiScript.

**OnSummon#4**
Configures Vanquished Tentacle to stay.

**GetScript_VanquishedTentacleofCthun**
Factory function for VanquishedTentacleofCthunScript.

**OnEffectExecute#7**
Handles Goblin Jumper Cables defibrillate chance.

**GetScript_GoblinJumperCables**
Factory function for GoblinJumperCablesScript.

**OnEffectExecute#8**
Handles Goblin Jumper Cables XL defibrillate chance.

**GetScript_GoblinJumperCablesXL**
Factory function for GoblinJumperCablesXLScript.

**OnCheckCast#6**
Checks if caster is mounted for Reindeer Transformation.

**GetScript_ReindeerTransformation**
Factory function for ReindeerTransformationScript.

**OnCheckCast**
Checks for Narain's Turban aura for Bag of Gold.

**GetScript_BagOfGold**
Factory function for BagOfGoldScript.

**OnSuccessfulStart**
Sets cast time to 0 for Instant Cast spells.

**GetScript_InstantCast**
Factory function for InstantCastScript.

**OnEffectExecute#4**
Handles Digging Claw egg finding chance.

**GetScript_DiggingClaw**
Factory function for DiggingClawScript.

**OnEffectExecute#13**
Handles Tanaris Field Sampling chance.

**GetScript_TanarisFieldSampling**
Factory function for TanarisFieldSamplingScript.

**OnCast**
Determines and casts Elune's Candle omen spell.

**GetScript_ElunesCandle**
Factory function for ElunesCandleScript.

**OnAfterHit**
Casts Recently Bandaged spell after First Aid.

**GetScript_FirstAid**
Factory function for FirstAidScript.

**OnSuccessfulFinish**
Casts Wolfshead Helm energy spell.

**GetScript_WolfsheadHelm**
Factory function for WolfsheadHelmScript.

**OnCheckCast#3**
Prevents Kodo Kombobulator cast if quest credit aura exists.

**GetScript_KodoKombobulator**
Factory function for KodoKombobulatorScript.

**OnCheckCast#4**
Validates target is Deeprun Rat for Melodious Rapture.

**GetScript_MelodiusRapture**
Factory function for MelodiusRaptureScript.

**OnCheckCast#5**
Prevents Purify and Place Food if map event is active.

**GetScript_PurifyAndPlaceFood**
Factory function for PurifyAndPlaceFoodScript.

**OnCheckCast#7**
Validates conditions for Summon Black Qiraji Battle Tank.

**GetScript_SummonBlackQirajiBattleTank**
Factory function for SummonBlackQirajiBattleTankScript.

**OnAfterApply#2**
Removes mount auras on Discombobulate apply.

**GetScript_Discombobulate**
Factory function for DiscombobulateAuraScript.

**OnAfterApply**
Applies Ashbringer reputation and stops attacks.

**GetScript_Ashbringer**
Factory function for AshbringerAuraScript.

**AddSC_item_spell_scripts**
Registers all item spell scripts with the engine.

---

<!-- machine-true, projected from graph.json -->

## Map — spell_item

*Source:* spell_item.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| OnCheckCast#2 | method | game_Objects_Item/GetProto, SpellCastTargetsInfo/getItemTarget | — | — |
| GetScript_HeavyArmorKit | function | — | — | — |
| OnEffectExecute#3 | method | Object/ToPlayer, Spell.Main/GetCaster, SpellCaster/CastSpell#2 | — | — |
| GetScript_DeviateFish | function | — | — | — |
| OnEffectExecute#2 | method | Object/ToPlayer, shared_Util/urand, Spell.Main/GetCaster, SpellCaster/CastSpell#2, Unit.Main/GetGender | — | — |
| GetScript_CookedDeviateFish | function | — | — | — |
| OnEffectExecute#11 | method | Object/ToPlayer, shared_Util/urand, Spell.Main/GetCaster, SpellCaster/CastSpell#2 | — | — |
| GetScript_NoggenfoggerElixir | function | — | — | — |
| OnEffectExecute#9 | method | shared_Util/urand | — | — |
| GetScript_LinkensBoomerang | function | — | — | — |
| OnEffectExecute#12 | method | shared_Util/urand | — | — |
| GetScript_ScorpidSurprise | function | — | — | — |
| OnEffectExecute | method | Spell.Main/GetUnitTarget, SpellCaster/CastSpell#2 | — | — |
| GetScript_BrittleArmorDummy | function | — | — | — |
| OnEffectExecute#10 | method | Spell.Main/GetUnitTarget, SpellCaster/CastSpell#2 | — | — |
| GetScript_MercurialShieldDummy | function | — | — | — |
| OnEffectExecute#5 | method | shared_Util/irand, SpellCaster/CastSpell#2 | — | — |
| GetScript_EverlookTransporter | function | — | — | — |
| OnEffectExecute#6 | method | Spell.Main/GetCaster, SpellCaster/CastSpell#2 | — | — |
| GetScript_GDRChannel | function | — | — | — |
| OnAuraValueCalculate | method | shared_Util/urand | — | — |
| OnPeriodicCalculateAmount | method | Aura/GetTarget, SpellCaster/GetCurrentSpell | — | — |
| OnAfterApply#3 | method | Aura/GetRemoveMode, Aura/GetTarget, Map.Main/GetUnit, SpellCaster/CastCustomSpell#2, Unit.Main/GetTargetGuid, WorldObject.Object/GetMap | — | — |
| GetScript_GDRPeriodicDamage | function | — | — | — |
| OnSummon#3 | method | Creature.Main/SetFactionTemporary, WorldObject.Object/GetFactionTemplateId, WorldObject.Object/SetUInt32Value | — | — |
| GetScript_TargetDummy | function | — | — | — |
| OnSummon | method | WorldObject.Object/MonsterWhisper#2 | — | — |
| GetScript_ChainedEssenceOfEranikus | function | — | — | — |
| OnSummon#2 | method | Creature.MotionMaster/MoveFollow, Creature.MotionMaster/MoveWaypoint, Unit.Main/GetMotionMaster, Unit.Main/SetWalk, WorldObject.Object/FindNearestCreature, WorldObject.Object/GetAreaId, WorldObject.Object/MonsterSay#2, WorldObject.Object/MonsterTextEmote#2 | — | — |
| GetScript_ReleaseUmisYeti | function | — | — | — |
| OnSummon#4 | method | CharmInfo/SetCommandState, Unit.Main/GetCharmInfo, Unit.Main/SaveStayPosition, Unit.Main/SetIsAtStay, Unit.Main/SetIsCommandFollow | — | — |
| GetScript_VanquishedTentacleofCthun | function | — | — | — |
| OnEffectExecute#7 | method | shared_Util/roll_chance_i, SpellCaster/CastSpell#2 | — | — |
| GetScript_GoblinJumperCables | function | — | — | — |
| OnEffectExecute#8 | method | shared_Util/roll_chance_i, SpellCaster/CastSpell#2 | — | — |
| GetScript_GoblinJumperCablesXL | function | — | — | — |
| OnCheckCast#6 | method | Unit.Main/HasAuraType | — | — |
| GetScript_ReindeerTransformation | function | — | — | — |
| OnCheckCast | method | Player.Main/ToPlayer, Spell.Main/GetAffectiveCaster, Unit.Main/HasAura#2 | — | — |
| GetScript_BagOfGold | function | — | — | — |
| OnSuccessfulStart | method | Spell.Main/ReSetTimer, Spell.Main/SetCastTime | — | — |
| GetScript_InstantCast | function | — | — | — |
| OnEffectExecute#4 | method | shared_Util/roll_chance_i, WorldObject.Object/FindNearestCreature | — | — |
| GetScript_DiggingClaw | function | — | — | — |
| OnEffectExecute#13 | method | shared_Util/roll_chance_i | — | — |
| GetScript_TanarisFieldSampling | function | — | — | — |
| OnCast | method | Object/GetEntry, Object/ToUnit, shared_Util/urand, SpellCaster/CastSpell#2, SpellCastTargetsInfo/getUnitTarget | — | — |
| GetScript_ElunesCandle | function | — | — | — |
| OnAfterHit | method | Spell.Main/GetUnitTarget, SpellCaster/CastSpell#2 | — | — |
| GetScript_FirstAid | function | — | — | — |
| OnSuccessfulFinish | method | SpellCaster/CastSpell#2 | — | — |
| GetScript_WolfsheadHelm | function | — | — | — |
| OnCheckCast#3 | method | Unit.Main/HasAura#2 | — | — |
| GetScript_KodoKombobulator | function | — | — | — |
| OnCheckCast#4 | method | Object/GetEntry, SpellCastTargetsInfo/getUnitTarget | — | — |
| GetScript_MelodiusRapture | function | — | — | — |
| OnCheckCast#5 | method | Map.Main/GetScriptedMapEvent, WorldObject.Object/GetMap | — | — |
| GetScript_PurifyAndPlaceFood | function | — | — | — |
| OnCheckCast#7 | method | MapEntry/IsMountAllowed, Object/IsPlayer, Object/ToPlayer, Player.Main/IsInHighLiquid, Spell.Main/IsTriggered, Unit.Main/IsInDisallowedMountForm, Unit.Main/IsInWater, Unit.Main/IsMounted, Unit.Main/RemoveSpellsCausingAura, WorldObject.Object/GetAreaId, WorldObject.Object/GetMapId, WorldObject.Object/GetTransport | — | — |
| GetScript_SummonBlackQirajiBattleTank | function | — | — | — |
| OnAfterApply#2 | method | Aura/GetEffIndex, Aura/GetTarget, Unit.Main/RemoveSpellsCausingAura | — | — |
| GetScript_Discombobulate | function | — | — | — |
| OnAfterApply | method | Aura/GetEffIndex, Aura/GetTarget, Object/GetTypeId, Object/ToPlayer, Player.Main/GetReputationMgr, Player.Main/GetReputationRank, ReputationMgr/ApplyForceReaction, ReputationMgr/SendForceReactions, Unit.Main/StopAttackFaction | — | — |
| GetScript_Ashbringer | function | — | — | — |
| AddSC_item_spell_scripts | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
