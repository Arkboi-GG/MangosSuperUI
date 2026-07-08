# Health & Mana Regeneration

<!-- aliases: health regen, mana regen, regen rates, faster regen, mana regeneration too slow, spirit regen -->
<!-- documentation: model-written from source via the local LLM; review before trusting -->

Health and mana regeneration in VMaNGOS operates on a fixed 2-second tick driven by `Player::RegenerateAll`. This method acts as the gatekeeper: it skips processing if a cooldown timer (`m_regenTimer`) is active, but proceeds if the player is out of combat, possesses specific combat-regen auras, or is polymorphed. Once the gate passes, it invokes `Player::RegenerateHealth` and `Player::Regenerate` for specific power types (Rage, Energy, Mana).

**Health Regeneration**
`Player::RegenerateHealth` calculates the heal amount based on several states:
1.  **Polymorphed:** If `Unit::IsPolymorphed` returns true, the player regenerates a flat 10% of their maximum health per tick.
2.  **Normal/Combat Regen:** Otherwise, it calls `Unit::GetRegenHPPerSpirit`. This method uses a hardcoded formula based on the player's class and Spirit stat (e.g., Warriors use `Spirit * 1.26 - 22.6`, Druids use `Spirit * 0.11 + 1`).
3.  **Modifiers:** The base spirit value is multiplied by `Rate.Health` (from config). It is further modified by:
    *   **Sitting:** If `Unit::IsStandingUp` is false, the value is multiplied by 1.5.
    *   **Auras:** Percent-based health regen auras (`SPELL_AURA_MOD_HEALTH_REGEN_PERCENT`) are applied multiplicatively. Flat regen auras (`SPELL_AURA_MOD_REGEN`) are added additively. Combat-specific regen auras (`SPELL_AURA_MOD_REGEN_DURING_COMBAT`) apply a divisor of 5.0 to their modifier amount.
4.  **Carry-over:** Fractional health amounts are stored in `m_carryHealthRegen` and added to the next tick to prevent loss of precision.
5.  **Application:** The final integer value is applied via `Unit::ModifyHealth`, which caps the result at maximum health.

**Mana Regeneration**
`Player::Regenerate` handles mana via the `POWER_MANA` case:
1.  **Interrupt State:** It checks `IsUnderLastManaUseEffect`. If true (recently cast), it uses the interrupted regen rate (`m_modManaRegenInterrupt`); otherwise, it uses the normal rate (`m_modManaRegen`).
2.  **Calculation:** The rate is multiplied by `Rate.Mana` (from config) and by 2.0 (representing the 2-second tick interval).
3.  **Application:** The result is added to the current mana, capped at maximum, and applied via `Unit::SetPower`.

**Other Powers**
*   **Rage:** Decays by a fixed amount (20 units per tick, scaled by `Rate.Rage.Loss`) rather than regenerating.
*   **Energy:** Regenerates by a fixed amount (20 units per tick, scaled by `Rate.Energy`).
*   **Pets/Creatures:** Pets and creatures do not use the `Player::Regenerate` path. Their stats are initialized via `Pet::InitStatsForLevel` or `Creature::InitStatsForLevel`, but continuous regeneration logic for non-player units is handled elsewhere in the engine (not detailed in these slices), often relying on aura ticks or specific AI scripts (e.g., `silithus::UpdateAI#2` manually restores mana for specific bosses).

## How to Modify

### Config
The following keys in `mangosd.conf` directly scale the regeneration values calculated in the code. Changes take effect immediately upon config reload (`.reload config`) or server restart.

*   **`Rate.Health`** (default `1`): Multiplies the final health regeneration amount calculated in `Player::RegenerateHealth`. Setting this to `2` doubles health regen.
*   **`Rate.Mana`** (default `1`): Multiplies the mana regeneration amount calculated in `Player::Regenerate`. Setting this to `2` doubles mana regen.
*   **`Rate.Rage.Loss`** (default `1`): Multiplies the rage decay rate. Higher values cause rage to drop faster.
*   **`Rate.Rage.Income`** (default `1`): While listed in config, the provided source slices for `Player::Regenerate` only reference `Rate.Rage.Loss` for the decay calculation. Income is typically generated via damage dealt, not this regen tick.

### Database
There are no dedicated database tables or columns in the provided schema that tune the base regeneration formulas. However, regeneration is heavily influenced by character stats:
*   **Spirit:** The primary driver for health regen. Increasing a player's Spirit (via gear, enchants, or buffs) increases the output of `Unit::GetRegenHPPerSpirit`.
*   **Intellect:** Increases maximum mana and often provides mana regen bonuses via item stats or auras.
*   **Auras:** Spells that grant `SPELL_AURA_MOD_HEALTH_REGEN_PERCENT` or `SPELL_AURA_MOD_REGEN` will boost regen. These are defined in spell-related tables (columns not verified here).

To change base regen rates for specific classes or levels, you must modify the code, as the formulas in `Unit::GetRegenHPPerSpirit` are hardcoded.

### Code
If you need to change the fundamental formulas or add new tunables:

*   **Health Regen Formula:** Edit `Unit::GetRegenHPPerSpirit` in `Unit.cpp` (lines 6488-6527). The `switch` statement contains the class-specific linear equations (e.g., `CLASS_WARRIOR: regen = (Spirit * 1.26 - 22.6)`). Change the coefficients to alter how Spirit converts to health regen.
*   **Mana Regen Logic:** Edit `Player::Regenerate` in `Player.cpp` (lines 2338-2402). The `POWER_MANA` case calculates the tick value. You can add new multipliers or change the base calculation here.
*   **Regen Tick Interval:** The 2-second interval is hardcoded in `Player::RegenerateAll` (via `REGEN_TIME_PLAYER_FULL`) and referenced in `Player::Regenerate` (multiplier `2.0f`). Changing this requires editing both `Player.cpp` and ensuring consistency with any other systems relying on this tick rate.
*   **Polymorph Regen:** The flat 10% health regen for polymorphed targets is hardcoded in `Player::RegenerateHealth` (line 2422: `addValue = (float)GetMaxHealth() / 10;`). Change the divisor to adjust this rate.

## Path Reference

**Player.Main/RegenerateAll** (Player.cpp): The entry point for the 2-second regeneration tick. It checks combat status and aura conditions before delegating to health and power regeneration methods.

**Player.Main/Regenerate** (Player.cpp): Handles per-power regeneration (Mana, Rage, Energy). It applies config rates (`Rate.Mana`, `Rate.Rage.Loss`) and manages the interrupted mana regen state.

**Player.Main/RegenerateHealth** (Player.cpp): Calculates health restoration. It distinguishes between polymorphed (flat %) and normal (Spirit-based) regen, applies sitting bonuses, aura modifiers, and handles fractional carry-over.

**World/LoadConfigSettings** (World.cpp): Reads `Rate.Health`, `Rate.Mana`, and other rate keys from the configuration file into the server's memory, making them accessible to the regeneration logic.

**Unit.Main/GetTotalAuraModifier** (Unit.cpp): Sums the values of all active auras of a specific type. Used by `RegenerateHealth` to calculate the total bonus from combat-regen or percent-regen auras.

**Unit.Main/ModifyHealth** (Unit.cpp): Safely adds health to a unit, ensuring the value does not exceed maximum health and handling death if health drops to zero. Called by `RegenerateHealth` to apply the calculated heal.

**Unit.Main/GetRegenHPPerSpirit** (Unit.cpp): Contains the hardcoded class-specific formulas that convert Spirit stat into base health regeneration points.

**Unit.Main/SetPower** (Unit.cpp): Updates a unit's power resource (Mana, Rage, etc.) and triggers necessary client updates. Called by `Regenerate` to apply mana changes.

**Unit.Main/IsStandingUp** (Unit.cpp): Checks if the unit is standing. Used by `RegenerateHealth` to apply the 1.5x sitting bonus.

**Unit.Main/IsPolymorphed** (Unit.cpp): Checks if the unit is currently polymorphed. Used by `RegenerateHealth` to switch to the flat 10% max health regen formula.

**boss_moam/Aggro** (boss_moam.cpp): Example of a boss script that manually sets mana to 0 on aggro, overriding any regeneration state.

**ChatHandler.CharacterCommands/HandleModifyEnergyCommand** (CharacterCommands.cpp): GM command to manually set a player's energy, bypassing regeneration.

**ChatHandler.CharacterCommands/HandleModifyRageCommand** (CharacterCommands.cpp): GM command to manually set a player's rage, bypassing regeneration/decay.

**ChatHandler.CharacterCommands/HandleGroupReplenishCommand** (CharacterCommands.cpp): GM command to instantly set all group members' health and mana to maximum, bypassing regeneration.

**ChatHandler.UnitCommands/HandleModifyManaCommand** (UnitCommands.cpp): GM command to manually set a unit's mana, bypassing regeneration.

**ChatHandler.UnitCommands/HandleDeplenishCommand** (UnitCommands.cpp): GM command to set health to 1 and power to 0, effectively disabling regeneration until the unit recovers.

**ChatHandler.UnitCommands/HandleReplenishCommand** (UnitCommands.cpp): GM command to instantly set health and mana to maximum, bypassing regeneration.

**Creature.Main/SetInitCreaturePowerType** (Creature.cpp): Initializes the power type (Mana, Energy, Rage) for a creature based on its class, determining which regeneration/decay logic applies.

**Creature.Main/InitStatsForLevel** (Creature.cpp): Sets base health and mana pools for creatures. While it doesn't handle tick-based regen, it establishes the maximum values that regen caps against.

**Creature.Main/LoadFromDB** (Creature.cpp): Loads creature data, including initial health/mana percentages, which can affect the starting point for any regeneration.

**Object/ToPet** (Pet.h): Helper to cast an Object to a Pet. Used to distinguish pet regeneration logic from player logic.

**PartyBotAI/UpdateAI** (PartyBotAI.cpp): Bot AI logic that can manually set health/mana to 100% (`SetHealthPercent`, `SetPowerPercent`) during initialization or specific states, bypassing natural regeneration.

**Pet.Main/LoadPetFromDB** (Pet.cpp): Loads pet data from the database, restoring saved health and mana values.

**Pet.Main/CreateBaseAtCreature** (Pet.cpp): Creates a pet instance, setting initial power types and happiness, which can influence damage and potentially regeneration via auras.

**Pet.Main/InitStatsForLevel** (Pet.cpp): Calculates base stats for pets, including health and mana pools, based on level and owner.

**Player.Main/Create** (Player.cpp): Initializes a new player, setting initial health and mana to maximum.

**Player.Main/ProcessDelayedOperations** (Player.cpp): Handles post-resurrection operations, including setting health/mana to specific values, which overrides regeneration.

**Player.Main/GiveLevel** (Player.cpp): On level up, resets health and mana to maximum, effectively skipping regeneration needs.

**Player.Main/InitStatsForLevel** (Player.cpp): Recalculates base stats and resets health/mana to maximum during stat resets or login.

**Player.Main/ResurrectPlayer** (Player.cpp): Restores health and mana to a percentage of maximum upon resurrection, bypassing regeneration.

**Player.Main/LoadFromDB** (Player.cpp): Loads player state from the database, restoring saved health and mana values.

**Player.Main/ResurrectUsingRequestData** (Player.cpp): Handles player-requested resurrection, setting health/mana to specific values from the request data.

**Player.StatSystem/UpdateDamagePhysical#2** (StatSystem.cpp): Updates pet damage based on happiness. While not direct regen, it shows how pet stats are dynamically modified.

**ruins_of_ahnqiraj/Reset#3** (ruins_of_ahnqiraj.cpp): Boss script that sets mana to 0 on reset, preventing regeneration from having an effect until combat starts.

**ruins_of_ahnqiraj/Aggro#2** (ruins_of_ahnqiraj.cpp): Boss script that sets mana to 0 on aggro.

**silithus/Reset#2** (silithus.cpp): Boss script that disables mana regeneration (`ClearCreatureState(CSTATE_REGEN_MANA)`) and sets mana to 0.

**silithus/UpdateAI#2** (silithus.cpp): Boss script that manually regenerates mana (+2% per dead player) instead of using the standard regen system.

**Spell.Effects/EffectSummon** (SpellEffects.cpp): Summons a pet, initializing its health and mana to maximum, bypassing regeneration.

**Spell.Effects/EffectSelfResurrect** (SpellEffects.cpp): Self-resurrection spell that sets health and mana to specific values, bypassing regeneration.

**WorldObject.Object/SetStatInt32Value** (Object.cpp): Low-level setter for unit fields, used by `SetPower` to update the mana/health values in the object's data structure.

---

<!-- machine-true, projected from graph.json -->

## Map — Health & Mana Regeneration

*Source:* Player.cpp, World.cpp, Unit.cpp, boss_moam.cpp, CharacterCommands.cpp, UnitCommands.cpp, Creature.cpp, Pet.h, PartyBotAI.cpp, Pet.cpp, StatSystem.cpp, ruins_of_ahnqiraj.cpp, silithus.cpp, SpellEffects.cpp, Object.cpp
*Config keys:* Rate.Health (default 1), Rate.Mana (default 1), Rate.Rage.Income (default 1), Rate.Rage.Loss (default 1)
*Tables:* —

| Member | Kind | Source | Role |
|---|---|---|---|
| Player.Main/RegenerateAll | method | Player.cpp:2318-2336 | seed — Player.*/Regenerate* |
| Player.Main/Regenerate | method | Player.cpp:2338-2402 | seed — Player.*/Regenerate* |
| Player.Main/RegenerateHealth | method | Player.cpp:2404-2455 | seed — Player.*/Regenerate* |
| World/LoadConfigSettings | method | World.cpp:440-1245 | seed — reads config Rate.Health |
| Unit.Main/GetTotalAuraModifier | method | Unit.cpp:3232-3252 | related — 1 hop from a seed |
| Unit.Main/ModifyHealth | method | Unit.cpp:6426-6455 | related — 1 hop from a seed |
| Unit.Main/GetRegenHPPerSpirit | method | Unit.cpp:6488-6527 | related — 1 hop from a seed |
| Unit.Main/SetPower | method | Unit.cpp:8353-8383 | related — 1 hop from a seed |
| Unit.Main/IsStandingUp | method | Unit.cpp:9497-9501 | related — 1 hop from a seed |
| Unit.Main/IsPolymorphed | method | Unit.cpp:9522-9525 | related — 1 hop from a seed |
| boss_moam/Aggro | method | boss_moam.cpp:77-89 | related — 2 hops from a seed |
| ChatHandler.CharacterCommands/HandleModifyEnergyCommand | method | CharacterCommands.cpp:4733-4770 | related — 2 hops from a seed |
| ChatHandler.CharacterCommands/HandleModifyRageCommand | method | CharacterCommands.cpp:4772-4812 | related — 2 hops from a seed |
| ChatHandler.CharacterCommands/HandleGroupReplenishCommand | method | CharacterCommands.cpp:5766-5795 | related — 2 hops from a seed |
| ChatHandler.UnitCommands/HandleModifyManaCommand | method | UnitCommands.cpp:2326-2367 | related — 2 hops from a seed |
| ChatHandler.UnitCommands/HandleDeplenishCommand | method | UnitCommands.cpp:2369-2383 | related — 2 hops from a seed |
| ChatHandler.UnitCommands/HandleReplenishCommand | method | UnitCommands.cpp:2385-2401 | related — 2 hops from a seed |
| Creature.Main/SetInitCreaturePowerType | method | Creature.cpp:1682-1707 | related — 2 hops from a seed |
| Creature.Main/InitStatsForLevel | method | Creature.cpp:1722-1783 | related — 2 hops from a seed |
| Creature.Main/LoadFromDB | method | Creature.cpp:1852-1966 | related — 2 hops from a seed |
| Object/ToPet | method | Pet.h:299-302 | related — 2 hops from a seed |
| PartyBotAI/UpdateAI | method | PartyBotAI.cpp:580-888 | related — 2 hops from a seed |
| Pet.Main/LoadPetFromDB | method | Pet.cpp:118-425 | related — 2 hops from a seed |
| Pet.Main/CreateBaseAtCreature | method | Pet.cpp:1212-1271 | related — 2 hops from a seed |
| Pet.Main/InitStatsForLevel | method | Pet.cpp:1273-1487 | related — 2 hops from a seed |
| Player.Main/Create | method | Player.cpp:401-529 | related — 2 hops from a seed |
| Player.Main/ProcessDelayedOperations | method | Player.cpp:2166-2202 | related — 2 hops from a seed |
| Player.Main/GiveLevel | method | Player.cpp:3114-3275 | related — 2 hops from a seed |
| Player.Main/InitStatsForLevel | method | Player.cpp:3314-3470 | related — 2 hops from a seed |
| Player.Main/ResurrectPlayer | method | Player.cpp:4714-4773 | related — 2 hops from a seed |
| Player.Main/LoadFromDB | method | Player.cpp:14692-15283 | related — 2 hops from a seed |
| Player.Main/ResurrectUsingRequestData | method | Player.cpp:20110-20166 | related — 2 hops from a seed |
| Player.StatSystem/UpdateDamagePhysical#2 | method | StatSystem.cpp:1045-1102 | related — 2 hops from a seed |
| ruins_of_ahnqiraj/Reset#3 | method | ruins_of_ahnqiraj.cpp:401-407 | related — 2 hops from a seed |
| ruins_of_ahnqiraj/Aggro#2 | method | ruins_of_ahnqiraj.cpp:409-417 | related — 2 hops from a seed |
| silithus/Reset#2 | method | silithus.cpp:935-947 | related — 2 hops from a seed |
| silithus/UpdateAI#2 | method | silithus.cpp:982-1059 | related — 2 hops from a seed |
| Spell.Effects/EffectSummon | method | SpellEffects.cpp:2281-2380 | related — 2 hops from a seed |
| Spell.Effects/EffectSelfResurrect | method | SpellEffects.cpp:5264-5299 | related — 2 hops from a seed |
| WorldObject.Object/SetStatInt32Value | method | Object.cpp:1255-1261 | related — 2 hops from a seed |
