<!-- provenance: boundary-bleed -->
# ChatHandler.UnitCommands

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ChatHandler.UnitCommands

## Purpose & Responsibilities

`ChatHandler.UnitCommands` (implemented in `UnitCommands.cpp`) provides the server-side implementation for a suite of administrative and debugging chat commands focused on inspecting and manipulating `Unit` objects (players and creatures). These commands allow Game Masters (GMs) and developers to:

1.  **Inspect State:** Retrieve detailed information about a unit’s position, stats, flags, movement, threat, auras, and AI.
2.  **Modify Attributes:** Directly alter core combat statistics (strength, agility, health, mana, resistances, attack power, speeds) and visual properties (morph, scale, faction).
3.  **Simulate Actions:** Force spells, apply/remove auras, deal damage, knock back, fear, or kill units.
4.  **Debug Movement & AI:** View and manipulate movement generators, hostile references, and threat lists.

This unit acts as a bridge between the chat interface and the game world entities (`Unit`, `Player`, `Creature`). It relies heavily on helper methods in the `ChatHandler` class (defined in `Chat.h` and implemented in other partials such as `Chat.cpp`) for argument parsing and output formatting, and on `Unit`, `Player`, and related classes for state mutation.

## Member-by-Member Behavior

### Inspection Commands

These commands retrieve and display current state information about a selected unit.

*   **HandleGUIDCommand**: Displays the GUID of the currently selected unit. It retrieves the selection via `Player.Main/GetSelectionGuid` and formats it using `ObjectGuid/GetString`.
*   **HandleGPSCommand**: Provides a comprehensive geographic report for a selected unit or a unit specified by a GUID link. It calculates grid/cell coordinates, retrieves zone/area names from DBC stores, checks for VMap availability (indoor/outdoor status), and reports terrain height and liquid status. It logs this data to the server log via `Log.Main/Out`.
*   **HandleGetDistanceCommand**: Calculates and displays the 3D distance, 2D distance, and raw Euclidean distance between the issuing player and the selected unit.
*   **HandleGetAngleCommand**: Calculates and displays the angle (in radians) between the issuing player and the selected unit.
*   **HandleUnitAIInfoCommand**: Dispatches to either `ChatHandler.CreatureCommands/HandleNpcAIInfoCommand` or `ChatHandler.CharacterCommands/HandleCharacterAIInfoCommand` depending on whether the selected unit is a creature or a player.
*   **HandleUnitInfoCommand**: Outputs a verbose dump of the unit’s core fields, including victim, charm, summon, target, faction, race, class, gender, flags, display IDs, and death state. It looks up faction and spell names from `ObjectMgr` and `SpellMgr`.
*   **HandleUnitMoveInfoCommand**: Displays internal movement data, including server/client timestamps, movement flags, position, transport info, jump parameters, and spline elevation.
*   **HandleUnitSpeedInfoCommand**: Lists all current speed modifiers (walk, run, swim, turn, etc.) for the unit.
*   **HandleUnitStatInfoCommand**: Provides an exhaustive dump of combat statistics, including health, power resources, base/total stats (STR, AGI, STA, INT, SPI), resistances, attack powers, crit/hit chances, and damage modifiers. For players, it additionally reports positive/negative stat buffs and resistance buff mods.
*   **HandleUnitUpdateFieldsInfoCommand**: Delegates to `ChatHandler.DebugCommands/ShowAllUpdateFieldsHelper` to print all raw update fields for the unit.
*   **HandleUnitFactionInfoCommand**: Displays detailed faction data, including the faction template ID, flags, hostile/friendly masks, and lists of enemy and friendly factions by name.
*   **HandleUnitShowRaceCommand**, **HandleUnitShowClassCommand**, **HandleUnitShowGenderCommand**, **HandleUnitShowPowerTypeCommand**, **HandleUnitShowFormCommand**: Specific inspectors that look up and display the human-readable name for the unit’s race, class, gender, power type, or shapeshift form from respective DBC stores.
*   **HandleUnitShowVisFlagsCommand**, **HandleUnitShowMiscFlagsCommand**, **HandleUnitShowUnitStateCommand**, **HandleUnitShowUnitFlagsCommand**, **HandleUnitShowNPCFlagsCommand**, **HandleUnitShowMoveFlagsCommand**: Display bitfield flags by converting them to string representations using `shared_Util/FlagsToString` and specific conversion arrays.
*   **HandleUnitShowEmoteStateCommand**, **HandleUnitShowStandStateCommand**, **HandleUnitShowSheathStateCommand**: Display the current emote, stand state, or sheath state.
*   **HandleUnitShowCreateSpellCommand**: Identifies and displays the spell ID and name that created the unit (e.g., a summoned pet).
*   **HandleUnitShowCombatTimerCommand**: Displays the remaining combat timer for the unit.
*   **HandleListAurasCommand**: Iterates through the unit’s `SpellAuraHolderMap`, displaying details for each active aura, including spell ID, effect index, duration, stacks, caster, and whether it is passive or a talent.
*   **HandleListMoveGensCommand**: Retrieves the list of used movement generators from the unit’s `MotionMaster` and prints their types.
*   **HandleListHostileRefsCommand**: Iterates through the unit’s hostile reference manager, listing the GUIDs of all units currently threatening or threatened by the target.
*   **HandleListThreatCommand**: Iterates through the unit’s threat manager, listing the threat value and GUID for each unit on the threat list.
*   **HandleMovegensCommand**: A more detailed movement generator inspector that prints the type of each generator in the motion master’s queue, including specific targets for chase/follow movements and destination coordinates for point/home movements.
*   **HandleCooldownListCommand**: Delegates to `SpellCaster/PrintCooldownList` to display active cooldowns.

### Spell Casting Commands

These commands force the casting of spells on targets, with various targeting options.

*   **HandleCastCommand**: Casts a specified spell on the selected unit. It validates the spell via `SpellMgr/IsSpellValid` and allows an optional "triggered" flag to bypass normal casting restrictions.
*   **HandleCastBackCommand**: Forces the selected unit to face the issuing player and cast a specified spell on the player.
*   **HandleCastDistCommand**: Casts a specified spell at a point in space located at a given distance from the issuing player. It calculates the target coordinates using `WorldObject.Object/GetClosePoint`.
*   **HandleCastSelfCommand**: Forces the selected unit to cast a specified spell on itself.
*   **HandleCastTargetCommand**: Forces the selected unit to face the issuing player and cast a specified spell on the unit's current combat victim.

### Modification Commands

These commands alter the state or attributes of a selected unit.

*   **HandlePvPCommand**: Toggles the PvP flag on the selected unit. For players, it calls `Player.Main/UpdatePvP`; for creatures, it calls `Unit.Main/SetPvP`.
*   **HandleFreezeCommand**: Applies a freeze effect by casting spell ID 9454 on the target.
*   **HandleUnfreezeCommand**: Removes the freeze effect by removing auras due to spell ID 9454.
*   **HandlePossessCommand**: Allows the issuing player to possess the selected unit by casting a custom spell (ID 530) with a specific value.
*   **HandleModifyStrengthCommand**, **HandleModifyAgilityCommand**, **HandleModifyStaminaCommand**, **HandleModifyIntellectCommand**, **HandleModifySpiritCommand**: Set the base value for the specified stat using `Unit.Main/SetModifierValue` and trigger a full stat recalculation via `Unit.Main/UpdateAllStats`. If the target is a player, it notifies them of the change. Note: `HandleModifyStaminaCommand` also resets the unit’s health to maximum if alive, reflecting the immediate impact of stamina on health pools.
*   **HandleModifyArmorCommand**, **HandleModifyHolyCommand**, **HandleModifyFireCommand**, **HandleModifyNatureCommand**, **HandleModifyFrostCommand**, **HandleModifyShadowCommand**, **HandleModifyArcaneCommand**: Directly set the resistance values in the unit’s object fields using `WorldObject.Object/SetInt32Value`. These bypass normal stat calculation systems.
*   **HandleModifyMeleeApCommand**, **HandleModifyRangedApCommand**: Set attack power values and trigger physical damage updates via `Unit.Main/UpdateDamagePhysical`.
*   **HandleModifySpellPowerCommand**: Applies spell power by casting a custom spell (ID 18058) with the specified amount as a parameter, effectively adding an aura-based modifier.
*   **HandleModifyMainSpeedCommand**, **HandleModifyOffSpeedCommand**, **HandleModifyRangedSpeedCommand**: Set the base attack times (speed) for main hand, off hand, and ranged weapons using `WorldObject.Object/SetFloatValue`.
*   **HandleModifyCastSpeedCommand**: Sets the cast speed modifier. The implementation differs based on client build: it uses `SetFloatValue` for newer clients (>1.11.2) and `SetInt32Value` for older ones.
*   **HandleModifyCrCommand**, **HandleModifyBrCommand**: Set the combat reach and bounding radius floats directly.
*   **HandleDeMorphCommand**, **HandleModifyMorphCommand**: Remove or apply a morph (display ID) to the unit. Security checks prevent lower-level admins from morphing higher-level players.
*   **HandleModifyEmoteStateCommand**: Forces the unit to play a specific emote animation.
*   **HandleModifyFactionCommand**: Changes the unit’s faction template ID and optionally its unit, NPC, and dynamic flags. It validates the faction ID against `ObjectMgr`.
*   **HandleModifyASpeedCommand**: Adjusts walk, run, and swim speeds. It caps the speed at 4.0 for non-basic admins and prevents modification if the unit is taxi flying.
*   **HandleModifyScaleCommand**: Changes the unit’s model scale and updates model data. Includes security checks for player targets.
*   **HandleModifyHPCommand**, **HandleModifyManaCommand**: Set current and maximum health or mana. They ensure the maximum is not less than the current value and include security checks for player targets.
*   **HandleDeplenishCommand**, **HandleReplenishCommand**: Instantly set health/power to minimum (1 HP, 0 power) or maximum, respectively.
*   **HandleDamageCommand**: Deals direct damage to the selected unit. It supports optional school type arguments to apply armor reduction and resistance calculations. It manually triggers attack state updates to the client.
*   **HandleAoEDamageCommand**: Deals damage to all units within a specified range of the issuing player. It uses `GridNotifiers` to find targets and iterates through them to deal damage.
*   **HandleChargeCommand**: Forces the issuing player to charge towards the selected unit using `Creature.MotionMaster/MoveCharge`.
*   **HandleKnockBackCommand**: Knocks the selected unit away from the issuing player with specified horizontal and vertical speeds.
*   **HandleFearCommand**: Finds a suitable fear spell by iterating through spell entries and applies it via `HandleAuraHelper`.

### Aura Management

*   **HandleAuraCommand**, **HandleNameAuraCommand**: Apply a specific spell aura to a unit. `HandleNameAuraCommand` allows targeting a player by name. Both delegate to `HandleAuraHelper`.
*   **HandleAuraHelper**: Creates a `SpellAuraHolder` for the given spell ID and duration, creates individual `Aura` objects for each valid effect, and adds them to the target unit. It validates that the spell actually applies auras.
*   **HandleUnAuraCommand**: Removes all auras from a unit, or only those from a specific spell ID.
*   **HandleCooldownClearCommand**, **HandleCooldownClearClientSideCommand**: Clears all cooldowns or a specific spell’s cooldown from a unit. The client-side variant specifically clears client-side cooldowns for a player.

### Death & Control

*   **HandleDieCommand**, **HandleNameDieCommand**: Kill the selected unit or a player by name. Both delegate to `HandleDieHelper`.
*   **HandleDieHelper**: Checks security permissions. If configured, it deals lethal damage to credit the killer; otherwise, it sets the loot recipient to null (for creatures) and deals lethal damage to the self to avoid loot issues. It also disables god mode if the player was invincible.

## Cross-Unit Boundaries

*   **ChatHandler.Chat**: Almost every member calls `ChatHandler.Chat` methods (implemented in `Chat.cpp` or other partials) for:
    *   **Output**: `PSendSysMessage`, `SendSysMessage`, `SetSentErrorMessage` to communicate results to the user.
    *   **Input Parsing**: `ExtractGuidFromLink`, `ExtractInt32`, `ExtractFloat`, `ExtractSpellIdFromLink`, `ExtractPlayerTarget`, `ExtractOnOff`, `ExtractLiteralArg` to parse command arguments.
    *   **Target Resolution**: `GetSelectedUnit`, `GetSelectedPlayer` to identify the target entity.
    *   **Localization**: `GetMangosString`, `GetSessionDbLocaleIndex` for localized messages.
*   **Unit.Main / WorldObject.Object**: The primary data source and sink. Members call getters (`GetHealth`, `GetSpeed`, `GetFactionTemplateId`, etc.) to inspect state and setters (`SetHealth`, `SetModifierValue`, `SetDisplayId`, etc.) to modify state.
*   **Player.Main**: Used when the target is a player, for specific player-related operations like `UpdatePvP`, `GetSelectionGuid`, `GetObjectByTypeMask`, and stat-specific getters (`GetPosStat`, `GetNegStat`).
*   **SpellMgr / ObjectMgr**: Used to resolve IDs to human-readable names or validation data (e.g., `GetSpellEntry`, `GetFactionEntry`, `GetAreaLocaleString`).
*   **SpellCaster**: Used for casting spells (`CastSpell`, `CastCustomSpell`) and managing cooldowns (`PrintCooldownList`, `RemoveSpellCooldown`).
*   **Creature.MotionMaster / Unit.Main**: Used for movement manipulation (`MoveCharge`, `GetMotionMaster`, `GetUsedMovementGeneratorsList`).
*   **Unit.SpellAuras**: Used for detailed aura inspection (`GetSpellAuraHolderMap`, `GetAuraByEffectIndex`) and creation (`CreateAura`, `AddSpellAuraHolder`).
*   **ThreatManager / HostileRefManager**: Used for inspecting combat relationships (`getThreatList`, `getFirst`).
*   **GridMap / GridDefines / Cell**: Used in `HandleGPSCommand` for spatial calculations and VMap/Map existence checks.
*   **Log.Main**: Used in `HandleGPSCommand` to debug-log location data.
*   **ChatHandler.CreatureCommands / ChatHandler.CharacterCommands**: `HandleUnitAIInfoCommand` delegates to these units for specialized AI information.
*   **ChatHandler.DebugCommands**: `HandleUnitUpdateFieldsInfoCommand` delegates to `ShowAllUpdateFieldsHelper` in this unit.

## Data Model

This unit does not interact directly with database tables. All data is retrieved from in-memory structures (DBC stores, object managers, unit fields) or passed via command-line arguments.

## Notable Implementation Details

*   **Stat Modification Inconsistency**: The `HandleModify[Stat]Command` methods (Strength, Agility, etc.) use `SetModifierValue` and `UpdateAllStats`, which is the proper way to modify derived stats. However, `HandleModifyArmorCommand` and resistance commands use `SetInt32Value` directly on object fields. This bypasses the stat system and may lead to inconsistencies if other parts of the engine expect stats to be calculated from modifiers.
*   **Spell Power Hack**: `HandleModifySpellPowerCommand` does not set a stat directly. Instead, it casts a specific spell (ID 18058) with the desired value as a parameter. This implies that spell 18058 is configured to grant spell power based on its value, acting as a workaround for the lack of a direct spell power stat setter.
*   **Client Build Conditionals**: Several commands (`HandleUnitStatInfoCommand`, `HandleModifyCastSpeedCommand`) contain `#if SUPPORTED_CLIENT_BUILD` directives to handle differences in object field types (Int32 vs Float) between different WoW client versions.
*   **Security Checks**: Many modification commands (`HandleModifyMorphCommand`, `HandleModifyScaleCommand`, `HandleModifyHPCommand`, etc.) perform security checks using `HasLowerSecurity` to prevent lower-level administrators from modifying higher-level players.
*   **Die Command Logic**: `HandleDieHelper` has special logic to handle loot recipients. If the `CONFIG_BOOL_DIE_COMMAND_CREDIT` config is enabled, it deals damage to credit the issuer. Otherwise, it clears the loot recipient for creatures to prevent loot from appearing, then kills the unit.
*   **GPS Command Complexity**: `HandleGPSCommand` is notably complex, performing multiple lookups (Map, Zone, Area, VMap, Liquid) and calculating grid/cell coordinates. It also handles transport-relative positioning.
*   **Fear Spell Search**: `HandleFearCommand` iterates through *all* spell IDs up to `GetMaxSpellId()` to find a suitable fear spell. This is inefficient and could be optimized by caching or using a predefined list of fear spells.
*   **AOE Damage Range**: `HandleAoEDamageCommand` uses a fixed `max_range` variable initialized to 10, which can be overridden by the second argument. It uses `Cell::VisitAllObjects` to find targets, ensuring efficient spatial queries.

## Member Reference

**HandleGUIDCommand**: Displays the GUID of the currently selected unit.

**HandleGPSCommand**: Provides a comprehensive geographic report for a selected unit, including coordinates, zone/area info, VMap status, and terrain data.

**HandleGetDistanceCommand**: Calculates and displays the 3D, 2D, and Euclidean distances between the issuing player and the selected unit.

**HandleGetAngleCommand**: Calculates and displays the angle between the issuing player and the selected unit.

**HandleUnitAIInfoCommand**: Dispatches to `ChatHandler.CreatureCommands/HandleNpcAIInfoCommand` or `ChatHandler.CharacterCommands/HandleCharacterAIInfoCommand` based on unit type.

**HandleUnitInfoCommand**: Outputs a verbose dump of the unit’s core fields, including victim, charm, faction, race, class, flags, and display IDs.

**HandleUnitMoveInfoCommand**: Displays internal movement data, including timestamps, flags, position, and jump parameters.

**HandleUnitSpeedInfoCommand**: Lists all current speed modifiers (walk, run, swim, etc.) for the unit.

**HandleUnitStatInfoCommand**: Provides an exhaustive dump of combat statistics, including health, power, base/total stats, resistances, and damage modifiers.

**HandleUnitUpdateFieldsInfoCommand**: Delegates to `ChatHandler.DebugCommands/ShowAllUpdateFieldsHelper` to print all raw update fields.

**HandleUnitFactionInfoCommand**: Displays detailed faction data, including template ID, flags, masks, and enemy/friendly faction lists.

**HandleUnitShowRaceCommand**: Displays the human-readable name for the unit’s race.

**HandleUnitShowClassCommand**: Displays the human-readable name for the unit’s class.

**HandleUnitShowGenderCommand**: Displays the human-readable name for the unit’s gender.

**HandleUnitShowPowerTypeCommand**: Displays the human-readable name for the unit’s power type.

**HandleUnitShowFormCommand**: Displays the human-readable name for the unit’s shapeshift form.

**HandleUnitShowVisFlagsCommand**: Displays the unit’s visibility flags as a string.

**HandleUnitShowMiscFlagsCommand**: Displays the unit’s miscellaneous flags as a string.

**HandleUnitShowEmoteStateCommand**: Displays the current emote state of the unit.

**HandleUnitShowStandStateCommand**: Displays the current stand state of the unit.

**HandleUnitShowSheathStateCommand**: Displays the current sheath state of the unit.

**HandleUnitShowUnitStateCommand**: Displays the unit’s state flags as a string.

**HandleUnitShowUnitFlagsCommand**: Displays the unit’s flags as a string.

**HandleUnitShowNPCFlagsCommand**: Displays the unit’s NPC flags as a string.

**HandleUnitShowMoveFlagsCommand**: Displays the unit’s movement flags as a string.

**HandleUnitShowCreateSpellCommand**: Displays the spell ID and name that created the unit.

**HandleUnitShowCombatTimerCommand**: Displays the remaining combat timer for the unit.

**HandlePvPCommand**: Toggles the PvP flag on the selected unit.

**HandleFreezeCommand**: Applies a freeze effect by casting spell ID 9454.

**HandleUnfreezeCommand**: Removes the freeze effect by removing auras due to spell ID 9454.

**HandlePossessCommand**: Allows the issuing player to possess the selected unit.

**HandleNameAuraCommand**: Applies a specific spell aura to a player targeted by name.

**HandleAuraHelper**: Creates and applies a `SpellAuraHolder` for a given spell ID and duration to a unit.

**HandleAuraCommand**: Applies a specific spell aura to the selected unit.

**HandleUnAuraCommand**: Removes all auras or auras from a specific spell ID from the selected unit.

**HandleListAurasCommand**: Lists all active auras on the selected unit with details.

**HandleListMoveGensCommand**: Lists the used movement generators for the selected unit.

**HandleListHostileRefsCommand**: Lists the hostile references for the selected unit.

**HandleListThreatCommand**: Lists the threat values for units on the selected unit's threat list.

**HandleChargeCommand**: Forces the issuing player to charge towards the selected unit.

**HandleCastCommand**: Casts a specified spell on the selected unit.

**HandleCastBackCommand**: Forces the selected unit to face the issuing player and cast a spell on them.

**HandleCastDistCommand**: Casts a specified spell at a point in space at a given distance from the issuing player.

**HandleCastTargetCommand**: Forces the selected unit to cast a spell on its current combat victim.

**HandleCastSelfCommand**: Forces the selected unit to cast a spell on itself.

**HandleModifyStrengthCommand**: Sets the base strength stat and recalculates all stats.

**HandleModifyAgilityCommand**: Sets the base agility stat and recalculates all stats.

**HandleModifyStaminaCommand**: Sets the base stamina stat, recalculates stats, and resets health to max if alive.

**HandleModifyIntellectCommand**: Sets the base intellect stat and recalculates all stats.

**HandleModifySpiritCommand**: Sets the base spirit stat and recalculates all stats.

**HandleModifyArmorCommand**: Directly sets the armor value in object fields.

**HandleModifyHolyCommand**: Directly sets the holy resistance value in object fields.

**HandleModifyFireCommand**: Directly sets the fire resistance value in object fields.

**HandleModifyNatureCommand**: Directly sets the nature resistance value in object fields.

**HandleModifyFrostCommand**: Directly sets the frost resistance value in object fields.

**HandleModifyShadowCommand**: Directly sets the shadow resistance value in object fields.

**HandleModifyArcaneCommand**: Directly sets the arcane resistance value in object fields.

**HandleModifyMeleeApCommand**: Sets melee attack power and updates physical damage.

**HandleModifyRangedApCommand**: Sets ranged attack power and updates physical damage.

**HandleModifySpellPowerCommand**: Applies spell power by casting a custom spell (ID 18058).

**HandleModifyMainSpeedCommand**: Sets the main hand attack speed.

**HandleModifyOffSpeedCommand**: Sets the off hand attack speed.

**HandleModifyRangedSpeedCommand**: Sets the ranged attack speed.

**HandleModifyCastSpeedCommand**: Sets the cast speed modifier.

**HandleModifyCrCommand**: Sets the combat reach float.

**HandleModifyBrCommand**: Sets the bounding radius float.

**HandleDeMorphCommand**: Removes the morph from the selected unit.

**HandleModifyMorphCommand**: Applies a morph (display ID) to the selected unit.

**HandleModifyEmoteStateCommand**: Forces the unit to play a specific emote animation.

**HandleModifyFactionCommand**: Changes the unit’s faction template ID and flags.

**HandleModifyASpeedCommand**: Adjusts walk, run, and swim speeds.

**HandleModifyScaleCommand**: Changes the unit’s model scale.

**HandleModifyHPCommand**: Sets current and maximum health.

**HandleModifyManaCommand**: Sets current and maximum mana.

**HandleDeplenishCommand**: Sets health to 1 and power to 0.

**HandleReplenishCommand**: Sets health and power to maximum.

**HandleDamageCommand**: Deals direct damage to the selected unit, with optional school type.

**HandleAoEDamageCommand**: Deals damage to all units within a specified range of the issuing player.

**HandleMovegensCommand**: Detailed inspector for movement generators in the motion master’s queue.

**HandleCooldownListCommand**: Displays active cooldowns for the selected unit.

**HandleCooldownClearCommand**: Clears all or specific spell cooldowns for the selected unit.

**HandleCooldownClearClientSideCommand**: Clears client-side cooldowns for the selected player.

**HandleNameDieCommand**: Kills a player targeted by name.

**HandleDieCommand**: Kills the selected unit.

**HandleDieHelper**: Helper function to kill a unit, handling loot recipients and god mode.

**HandleFearCommand**: Finds a suitable fear spell and applies it to the selected unit.

**HandleKnockBackCommand**: Knocks the selected unit away from the issuing player.

---

<!-- machine-true, projected from graph.json -->

## Map — ChatHandler.UnitCommands

*Source:* UnitCommands.cpp, Chat.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| HandleGUIDCommand | method | ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, ObjectGuid/GetString, ObjectGuid/operator!, Player.Main/GetSelectionGuid, WorldSession.Main/GetPlayer | — | — |
| HandleGPSCommand | method | AreaEntry/GetById, Cell/Cell#2, Cell/CellX, Cell/CellY, ChatHandler.Chat/ExtractGuidFromLink, ChatHandler.Chat/GetMangosString, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/GetSessionDbLocaleIndex, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, DBCStores/Map2ZoneCoordinates, GridDefines/ComputeCellPair, GridDefines/ComputeGridPair, GridMap/ExistMap, GridMap/ExistVMap, GridMap/getLiquidStatus#2, GridMap/IsOutdoors, Log.Main/Out, Map.Main/GetHeight, Object/GetEntry, Object/GetGUIDLow, Object/GetTypeId, ObjectMgr/GetAreaLocaleString, Player.Main/GetObjectByTypeMask, Position/Position, World/GetDefaultDbcLocale, WorldObject.Object/GetInstanceId, WorldObject.Object/GetMap, WorldObject.Object/GetMapId, WorldObject.Object/GetName, WorldObject.Object/GetOrientation, WorldObject.Object/GetPosition#2, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/GetTerrain, WorldObject.Object/GetTransport, WorldObject.Object/GetZoneAndAreaId, WorldSession.Main/GetPlayer | — | — |
| HandleGetDistanceCommand | method | ChatHandler.Chat/ExtractGuidFromLink, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/GetObjectByTypeMask, WorldObject.Object/GetDistance#3, WorldObject.Object/GetDistance2d#3, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldSession.Main/GetPlayer | — | — |
| HandleGetAngleCommand | method | ChatHandler.Chat/ExtractGuidFromLink, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/GetObjectByTypeMask, WorldObject.Object/GetAngle, WorldObject.Object/GetName, WorldSession.Main/GetPlayer | — | — |
| HandleUnitAIInfoCommand | method | ChatHandler.CharacterCommands/HandleCharacterAIInfoCommand, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, ChatHandler.CreatureCommands/HandleNpcAIInfoCommand, Object/IsCreature | — | — |
| HandleUnitInfoCommand | method | ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Object/GetByteValue, Object/GetFloatValue, Object/GetGuidValue, Object/GetObjectGuid, Object/GetUInt32Value, ObjectGuid/GetString, ObjectGuid/ObjectGuid, ObjectMgr/GetFactionEntry, SpellMgr/GetSpellEntry, SpellMgr/Instance, Unit.Main/GetChannelObjectGuid, Unit.Main/GetCharmerGuid, Unit.Main/GetCharmGuid, Unit.Main/GetClass, Unit.Main/GetCombatReach, Unit.Main/GetCreatureType, Unit.Main/GetDeathState, Unit.Main/GetDisplayId, Unit.Main/GetFactionTemplateId, Unit.Main/GetGender, Unit.Main/GetLevel, Unit.Main/GetMountID, Unit.Main/GetNativeDisplayId, Unit.Main/GetObjectBoundingRadius, Unit.Main/GetRace, Unit.Main/GetShapeshiftForm, Unit.Main/GetSheath, Unit.Main/GetStandState, Unit.Main/GetTargetGuid, Unit.Main/GetUnitState, Unit.Main/GetVictim, WorldObject.Object/GetFactionTemplateEntry | — | — |
| HandleUnitMoveInfoCommand | method | ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, MovementInfo/HasMovementFlag, Object/GetObjectGuid, ObjectGuid/GetString, ObjectGuid/IsEmpty, Position/IsEmpty, shared_Util/FlagsToString | — | — |
| HandleUnitSpeedInfoCommand | method | ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Object/GetObjectGuid, ObjectGuid/GetString, Unit.Main/GetSpeed | — | — |
| HandleUnitStatInfoCommand | method | ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Object/GetFloatValue, Object/GetInt16Value, Object/GetInt32Value, Object/GetObjectGuid, Object/GetUInt32Value, Object/ToPlayer, ObjectGuid/GetString, Player.Main/GetNegStat, Player.Main/GetPosStat, Player.Main/GetResistanceBuffMods, Player.StatSystem/GetWeaponBasedAuraModifier#2, Unit.Main/GetCreateResistance, Unit.Main/GetCreateStat, Unit.Main/GetHealth, Unit.Main/GetMaxHealth, Unit.Main/GetMaxPower, Unit.Main/GetPower, Unit.Main/GetPowerType, Unit.Main/GetResistance, Unit.Main/GetSpellCritPercent, Unit.Main/GetStat | — | — |
| HandleUnitUpdateFieldsInfoCommand | method | ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, ChatHandler.DebugCommands/ShowAllUpdateFieldsHelper | — | — |
| HandleUnitFactionInfoCommand | method | ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Object/GetObjectGuid, ObjectGuid/GetString, ObjectMgr/GetFactionEntry, shared_Util/FlagsToString, Unit.Main/GetFactionTemplateId, WorldObject.Object/GetFactionTemplateEntry, WorldObject.Object/GetName | — | — |
| HandleUnitShowRaceCommand | method | ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Object/GetObjectGuid, ObjectGuid/GetString, Unit.Main/GetRace | — | — |
| HandleUnitShowClassCommand | method | ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Object/GetObjectGuid, ObjectGuid/GetString, Unit.Main/GetClass | — | — |
| HandleUnitShowGenderCommand | method | ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Object/GetObjectGuid, ObjectGuid/GetString, SharedDefines/GenderToString, Unit.Main/GetGender | — | — |
| HandleUnitShowPowerTypeCommand | method | ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Object/GetObjectGuid, ObjectGuid/GetString, SharedDefines/PowerToString, Unit.Main/GetPowerType | — | — |
| HandleUnitShowFormCommand | method | ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Object/GetObjectGuid, ObjectGuid/GetString, Unit.Main/GetShapeshiftForm | — | — |
| HandleUnitShowVisFlagsCommand | method | ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Object/GetByteValue, Object/GetObjectGuid, ObjectGuid/GetString, shared_Util/FlagsToString | — | — |
| HandleUnitShowMiscFlagsCommand | method | ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Object/GetByteValue, Object/GetObjectGuid, ObjectGuid/GetString, shared_Util/FlagsToString | — | — |
| HandleUnitShowEmoteStateCommand | method | ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Object/GetObjectGuid, Object/GetUInt32Value, ObjectGuid/GetString | — | — |
| HandleUnitShowStandStateCommand | method | ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Object/GetObjectGuid, ObjectGuid/GetString, Unit.Main/GetStandState, UnitDefines/UnitStandStateToString | — | — |
| HandleUnitShowSheathStateCommand | method | ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Object/GetObjectGuid, ObjectGuid/GetString, Unit.Main/GetSheath, UnitDefines/SheathStateToString | — | — |
| HandleUnitShowUnitStateCommand | method | ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Object/GetObjectGuid, ObjectGuid/GetString, shared_Util/FlagsToString, Unit.Main/GetUnitState | — | — |
| HandleUnitShowUnitFlagsCommand | method | ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Object/GetObjectGuid, Object/GetUInt32Value, ObjectGuid/GetString, shared_Util/FlagsToString | — | — |
| HandleUnitShowNPCFlagsCommand | method | ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Object/GetObjectGuid, Object/GetUInt32Value, ObjectGuid/GetString, shared_Util/FlagsToString | — | — |
| HandleUnitShowMoveFlagsCommand | method | ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Object/GetObjectGuid, ObjectGuid/GetString, shared_Util/FlagsToString, WorldObject.Object/GetUnitMovementFlags | — | — |
| HandleUnitShowCreateSpellCommand | method | ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Object/GetObjectGuid, Object/GetUInt32Value, ObjectGuid/GetString, SpellMgr/GetSpellEntry, SpellMgr/Instance | — | — |
| HandleUnitShowCombatTimerCommand | method | ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Object/GetObjectGuid, ObjectGuid/GetString, Unit.Main/GetCombatTimer | — | — |
| HandlePvPCommand | method | ChatHandler.Chat/ExtractOnOff, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Object/ToPlayer, Player.Main/UpdatePvP, Unit.Main/SetPvP | — | — |
| HandleFreezeCommand | method | ChatHandler.Chat/GetSelectedUnit, SpellCaster/CastSpell#2 | — | — |
| HandleUnfreezeCommand | method | ChatHandler.Chat/GetSelectedUnit, Unit.Main/RemoveAurasDueToSpell | — | — |
| HandlePossessCommand | method | ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/SendSysMessage#2, SpellCaster/CastCustomSpell#2, WorldSession.Main/GetPlayer | — | — |
| HandleNameAuraCommand | method | ChatHandler.Chat/ExtractArg, ChatHandler.Chat/ExtractInt32, ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/ExtractSpellIdFromLink, ObjectGuid/ObjectGuid | — | — |
| HandleAuraHelper | method | ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, SpellAuraHolder/SetAuraDuration, SpellEntry/HasEffect, SpellEntry/IsAreaAuraEffect, SpellEntry/IsSpellAppliesAura#2, SpellMgr/GetSpellEntry, SpellMgr/Instance, Unit.Main/AddSpellAuraHolder, Unit.SpellAuras/AddAura, Unit.SpellAuras/CreateAura, Unit.SpellAuras/CreateSpellAuraHolder, WorldSession.Main/GetPlayer | ChatHandler.CreatureCommands/HandleNpcSpawnSetAurasCommand | — |
| HandleAuraCommand | method | ChatHandler.Chat/ExtractInt32, ChatHandler.Chat/ExtractSpellIdFromLink, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage | — | — |
| HandleUnAuraCommand | method | ChatHandler.Chat/ExtractSpellIdFromLink, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Unit.Main/RemoveAllAuras, Unit.Main/RemoveAurasDueToSpell | — | — |
| HandleListAurasCommand | method | Aura/GetAuraDuration, Aura/GetAuraMaxDuration, Aura/GetAuraPeriodicTimer, Aura/GetEffIndex, Aura/GetModifier, Aura/GetStackAmount, ChatHandler.Chat/GetMangosString, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/GetSessionDbcLocale, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, DBCStores/GetTalentSpellCost#2, ObjectGuid/GetString, SpellAuraHolder/GetAuraByEffectIndex, SpellAuraHolder/GetCasterGuid, SpellAuraHolder/GetSpellProto, SpellAuraHolder/IsPassive, Unit.Main/GetSpellAuraHolderMap, Unit.SpellAuras/GetId | — | — |
| HandleListMoveGensCommand | method | ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Creature.MotionMaster/GetMovementGeneratorTypeName, Creature.MotionMaster/GetUsedMovementGeneratorsList, Object/GetObjectGuid, ObjectGuid/GetString, Unit.Main/GetMotionMaster | — | — |
| HandleListHostileRefsCommand | method | ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, HostileReference/next, HostileRefManager/getFirst, Object/GetGuidStr, Object/GetObjectGuid, ObjectGuid/GetString, ThreatManager/getSourceUnit, Unit.Main/GetHostileRefManager | — | — |
| HandleListThreatCommand | method | ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, HostileReference/getThreat, HostileReference/getUnitGuid, Object/GetObjectGuid, ObjectGuid/GetString, ThreatManager/getThreatList, Unit.Main/GetThreatManager | — | — |
| HandleChargeCommand | method | ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Creature.MotionMaster/MoveCharge, Unit.Main/GetMotionMaster, WorldSession.Main/GetPlayer | — | — |
| HandleCastCommand | method | ChatHandler.Chat/ExtractLiteralArg, ChatHandler.Chat/ExtractSpellIdFromLink, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, SpellCaster/CastSpell#2, SpellMgr/GetSpellEntry, SpellMgr/Instance, SpellMgr/IsSpellValid, WorldSession.Main/GetPlayer | — | — |
| HandleCastBackCommand | method | ChatHandler.Chat/ExtractLiteralArg, ChatHandler.Chat/ExtractSpellIdFromLink, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, SpellCaster/CastSpell#2, SpellMgr/GetSpellEntry, SpellMgr/Instance, Unit.Main/SetFacingToObject, WorldSession.Main/GetPlayer | — | — |
| HandleCastDistCommand | method | ChatHandler.Chat/ExtractFloat, ChatHandler.Chat/ExtractLiteralArg, ChatHandler.Chat/ExtractSpellIdFromLink, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, SpellCaster/CastSpell#4, SpellMgr/GetSpellEntry, SpellMgr/Instance, SpellMgr/IsSpellValid, WorldObject.Object/GetClosePoint, WorldSession.Main/GetPlayer | — | — |
| HandleCastTargetCommand | method | ChatHandler.Chat/ExtractLiteralArg, ChatHandler.Chat/ExtractSpellIdFromLink, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, SpellCaster/CastSpell#2, SpellMgr/GetSpellEntry, SpellMgr/Instance, Unit.Main/GetVictim, Unit.Main/SetFacingToObject, WorldSession.Main/GetPlayer | — | — |
| HandleCastSelfCommand | method | ChatHandler.Chat/ExtractLiteralArg, ChatHandler.Chat/ExtractSpellIdFromLink, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, SpellCaster/CastSpell#2, SpellMgr/GetSpellEntry, SpellMgr/Instance, SpellMgr/IsSpellValid, WorldSession.Main/GetPlayer | — | — |
| HandleModifyStrengthCommand | method | ChatHandler.Chat/ExtractInt32, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Object/ToPlayer, Player.Main/PSendSysMessage#2, Unit.Main/SetModifierValue, Unit.Main/UpdateAllStats, WorldObject.Object/GetName | — | — |
| HandleModifyAgilityCommand | method | ChatHandler.Chat/ExtractInt32, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Object/ToPlayer, Player.Main/PSendSysMessage#2, Unit.Main/SetModifierValue, Unit.Main/UpdateAllStats, WorldObject.Object/GetName | — | — |
| HandleModifyStaminaCommand | method | ChatHandler.Chat/ExtractInt32, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Object/ToPlayer, Player.Main/PSendSysMessage#2, Unit.Main/GetMaxHealth, Unit.Main/IsAlive, Unit.Main/SetHealth, Unit.Main/SetModifierValue, Unit.Main/UpdateAllStats, WorldObject.Object/GetName | — | — |
| HandleModifyIntellectCommand | method | ChatHandler.Chat/ExtractInt32, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Object/ToPlayer, Player.Main/PSendSysMessage#2, Unit.Main/SetModifierValue, Unit.Main/UpdateAllStats, WorldObject.Object/GetName | — | — |
| HandleModifySpiritCommand | method | ChatHandler.Chat/ExtractInt32, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Object/ToPlayer, Player.Main/PSendSysMessage#2, Unit.Main/SetModifierValue, Unit.Main/UpdateAllStats, WorldObject.Object/GetName | — | — |
| HandleModifyArmorCommand | method | ChatHandler.Chat/ExtractInt32, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Object/ToPlayer, Player.Main/PSendSysMessage#2, WorldObject.Object/GetName, WorldObject.Object/SetInt32Value | — | — |
| HandleModifyHolyCommand | method | ChatHandler.Chat/ExtractInt32, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Object/ToPlayer, Player.Main/PSendSysMessage#2, WorldObject.Object/GetName, WorldObject.Object/SetInt32Value | — | — |
| HandleModifyFireCommand | method | ChatHandler.Chat/ExtractInt32, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Object/ToPlayer, Player.Main/PSendSysMessage#2, WorldObject.Object/GetName, WorldObject.Object/SetInt32Value | — | — |
| HandleModifyNatureCommand | method | ChatHandler.Chat/ExtractInt32, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Object/ToPlayer, Player.Main/PSendSysMessage#2, WorldObject.Object/GetName, WorldObject.Object/SetInt32Value | — | — |
| HandleModifyFrostCommand | method | ChatHandler.Chat/ExtractInt32, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Object/ToPlayer, Player.Main/PSendSysMessage#2, WorldObject.Object/GetName, WorldObject.Object/SetInt32Value | — | — |
| HandleModifyShadowCommand | method | ChatHandler.Chat/ExtractInt32, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Object/ToPlayer, Player.Main/PSendSysMessage#2, WorldObject.Object/GetName, WorldObject.Object/SetInt32Value | — | — |
| HandleModifyArcaneCommand | method | ChatHandler.Chat/ExtractInt32, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Object/ToPlayer, Player.Main/PSendSysMessage#2, WorldObject.Object/GetName, WorldObject.Object/SetInt32Value | — | — |
| HandleModifyMeleeApCommand | method | ChatHandler.Chat/ExtractInt32, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Object/ToPlayer, Player.Main/PSendSysMessage#2, Unit.Main/UpdateDamagePhysical, WorldObject.Object/GetName, WorldObject.Object/SetInt32Value | — | — |
| HandleModifyRangedApCommand | method | ChatHandler.Chat/ExtractInt32, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Object/ToPlayer, Player.Main/PSendSysMessage#2, Unit.Main/UpdateDamagePhysical, WorldObject.Object/GetName, WorldObject.Object/SetInt32Value | — | — |
| HandleModifySpellPowerCommand | method | ChatHandler.Chat/ExtractInt32, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Object/ToPlayer, Player.Main/PSendSysMessage#2, SpellCaster/CastCustomSpell#2, Unit.Main/RemoveAurasDueToSpell, WorldObject.Object/GetName | — | — |
| HandleModifyMainSpeedCommand | method | ChatHandler.Chat/ExtractInt32, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Object/ToPlayer, Player.Main/PSendSysMessage#2, WorldObject.Object/GetName, WorldObject.Object/SetFloatValue | — | — |
| HandleModifyOffSpeedCommand | method | ChatHandler.Chat/ExtractInt32, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Object/ToPlayer, Player.Main/PSendSysMessage#2, WorldObject.Object/GetName, WorldObject.Object/SetFloatValue | — | — |
| HandleModifyRangedSpeedCommand | method | ChatHandler.Chat/ExtractInt32, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Object/ToPlayer, Player.Main/PSendSysMessage#2, WorldObject.Object/GetName, WorldObject.Object/SetFloatValue | — | — |
| HandleModifyCastSpeedCommand | method | ChatHandler.Chat/ExtractFloat, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Object/ToPlayer, Player.Main/PSendSysMessage#2, WorldObject.Object/GetName, WorldObject.Object/SetFloatValue | — | — |
| HandleModifyCrCommand | method | ChatHandler.Chat/ExtractFloat, ChatHandler.Chat/GetSelectedUnit, WorldObject.Object/SetFloatValue | — | — |
| HandleModifyBrCommand | method | ChatHandler.Chat/ExtractFloat, ChatHandler.Chat/GetSelectedUnit, WorldObject.Object/SetFloatValue | — | — |
| HandleDeMorphCommand | method | ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/HasLowerSecurity, Object/GetTypeId, Unit.Main/DeMorph, WorldSession.Main/GetPlayer | — | — |
| HandleModifyMorphCommand | method | ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/HasLowerSecurity, ChatHandler.Chat/PSendSysMessage, Object/GetTypeId, Unit.Main/SetDisplayId, WorldSession.Main/GetPlayer | — | — |
| HandleModifyEmoteStateCommand | method | ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Unit.Main/HandleEmoteState | — | — |
| HandleModifyFactionCommand | method | ChatHandler.Chat/ExtractOptUInt32, ChatHandler.Chat/ExtractUint32KeyFromLink, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Object/GetGUIDLow, Object/GetUInt32Value, ObjectMgr/GetFactionTemplateEntry, Unit.Main/GetFactionTemplateId, Unit.Main/SetFactionTemplateId, WorldObject.Object/SetUInt32Value | — | — |
| HandleModifyASpeedCommand | method | ChatHandler.Chat/GetAccessLevel, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Object/GetTypeId, Player.Main/GetName, Unit.Main/IsTaxiFlying, Unit.Main/UpdateSpeed | — | — |
| HandleModifyScaleCommand | method | ChatHandler.Chat/GetNameLink, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/HasLowerSecurity, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Object/GetTypeId, Player.Main/PSendSysMessage#2, Unit.Main/UpdateModelData, WorldObject.Object/SetObjectScale | — | — |
| HandleModifyHPCommand | method | ChatHandler.Chat/ExtractInt32, ChatHandler.Chat/GetNameLink, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/HasLowerSecurity, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Object/GetTypeId, Object/ToPlayer, Player.Main/PSendSysMessage#2, Unit.Main/SetHealth, Unit.Main/SetMaxHealth | — | — |
| HandleModifyManaCommand | method | ChatHandler.Chat/ExtractInt32, ChatHandler.Chat/GetNameLink, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/HasLowerSecurity, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Object/GetTypeId, Object/ToPlayer, Player.Main/PSendSysMessage#2, Unit.Main/SetMaxPower, Unit.Main/SetPower | — | — |
| HandleDeplenishCommand | method | ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Unit.Main/GetPowerType, Unit.Main/IsAlive, Unit.Main/SetHealth, Unit.Main/SetPower | — | — |
| HandleReplenishCommand | method | ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Unit.Main/GetMaxHealth, Unit.Main/GetMaxPower, Unit.Main/GetPowerType, Unit.Main/IsAlive, Unit.Main/SetHealth, Unit.Main/SetPower | — | — |
| HandleDamageCommand | method | ChatHandler.Chat/ExtractInt32, ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, ObjectGuid/operator!, Player.Main/GetSelectionGuid, shared_Util/ditheru, SpellCaster/CalcArmorReducedDamage, SpellCaster/DealDamageMods, SpellDefines/GetSchoolMask, Unit.Main/CalculateDamageAbsorbAndResist, Unit.Main/DealDamage, Unit.Main/IsAlive, Unit.Main/SendAttackStateUpdate#2, WorldSession.Main/GetPlayer | — | — |
| HandleAoEDamageCommand | method | AnyAoETargetUnitInObjectRangeCheck/AnyAoETargetUnitInObjectRangeCheck, ChatHandler.Chat/ExtractInt32, Unit.Main/DealDamage, Unit.Main/SendAttackStateUpdate#2, WorldSession.Main/GetPlayer | — | — |
| HandleMovegensCommand | method | ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Creature.MotionMaster/GetDestination, MotionMaster/begin, MotionMaster/end, MovementGenerator/GetMovementGeneratorType, Object/GetGUIDLow, Object/GetTypeId, Unit.Main/GetMotionMaster, WorldObject.Object/GetName | — | — |
| HandleCooldownListCommand | method | ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, SpellCaster/PrintCooldownList | — | — |
| HandleCooldownClearCommand | method | ChatHandler.Chat/ExtractSpellIdFromLink, ChatHandler.Chat/GetMangosString, ChatHandler.Chat/GetNameLink, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Object/GetTypeId, SpellCaster/RemoveAllCooldowns, SpellCaster/RemoveSpellCooldown, SpellMgr/GetSpellEntry, SpellMgr/Instance, WorldObject.Object/GetName, WorldSession.Main/GetPlayer | — | — |
| HandleCooldownClearClientSideCommand | method | ChatHandler.Chat/GetNameLink, ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/RemoveAllCooldowns | — | — |
| HandleNameDieCommand | method | ChatHandler.Chat/ExtractArg, ChatHandler.Chat/ExtractPlayerTarget, ObjectGuid/ObjectGuid | — | — |
| HandleDieCommand | method | ChatHandler.Chat/GetSelectedUnit | — | — |
| HandleDieHelper | method | ChatHandler.Chat/HasLowerSecurity, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Creature.Main/SetLootRecipient, Object/ToCreature, Object/ToPlayer, ObjectGuid/ObjectGuid, Player.Main/SetCheatGod, Unit.Main/DealDamage, Unit.Main/GetHealth, Unit.Main/GetInvincibilityHpThreshold, Unit.Main/IsAlive, World/getConfig, WorldSession.Main/GetPlayer | — | — |
| HandleFearCommand | method | ChatHandler.Chat/ExtractInt32, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, SpellEntry/HasAttribute, SpellMgr/GetMaxSpellId, SpellMgr/GetSpellEntry, SpellMgr/Instance | — | — |
| HandleKnockBackCommand | method | ChatHandler.Chat/ExtractFloat, ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/GetSession, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Unit.Main/KnockBackFrom, WorldSession.Main/GetPlayer | — | — |

---

<!-- verify: boundary-bleed | foreign: ChatHandler, update -->
