# mob_anubisath_sentinel

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# mob_anubisath_sentinel

## Purpose & Responsibilities

`mob_anubisath_sentinel` implements the artificial intelligence for the **Anubisath Sentinel** creature (entry `15264`) in the Temple of Ahn'Qiraj raid instance. This unit defines the cooperative behavior of these sentinels, which operate as a linked group rather than independent entities.

The core responsibilities of this AI are:
1.  **Group Coordination:** Upon aggro, a sentinel identifies nearby sentinels within a 90-yard radius and forms a "buddy list." It shares this list with all identified buddies, ensuring every sentinel in the cluster knows about every other sentinel.
2.  **Ability Distribution:** The group collectively selects a set of unique abilities (buffs/debuffs) from a pool of nine options. Each sentinel in the group is assigned one distinct ability to apply upon entering combat.
3.  **Assist Mechanics:** When one sentinel engages a player, it commands all other sentinels in its buddy list to attack the same target.
4.  **Death Transfer:** When a sentinel dies, it transfers its specific ability buff to a surviving buddy and heals that buddy, ensuring the group retains its full suite of abilities until the last sentinel falls.
5.  **Standard Combat:** Individual sentinels perform melee attacks, cast their assigned special ability (e.g., Knock Away, Mana Burn), and enrage below 30% health.

This unit does not interact with any database tables; all logic is driven by runtime state and hardcoded spell IDs.

## Member-by-Member Behavior

### Group Management & Initialization

*   **`aqsentinelAI`**: The constructor initializes the AI state. It clears the buddy list, resets the enraged flag, sets the initial ability ID to 0, marks the sentinel as not alone, and calls `Reset()` to initialize timers and flags.
*   **`ClearBuddyList`**: Empties the `nearby` set, which stores the GUIDs of allied sentinels.
*   **`AddBuddyToList`**: Adds a `Creature` pointer's GUID to the `nearby` set if it is not the sentinel itself and not already present.
*   **`GiveBuddyMyList`**: Takes a buddy's GUID, retrieves the creature, and casts its AI to `aqsentinelAI`. It then iterates through the current sentinel's `nearby` list, adding each known buddy to the target buddy's list. Finally, it adds the current sentinel to the target buddy's list. This ensures bidirectional awareness.
*   **`SendMyListToBuddies`**: Iterates through the current `nearby` list and calls `GiveBuddyMyList` for each entry, propagating the full group topology to all members.
*   **`AddSentinelsNear`**: Uses `GetCreatureListWithEntryInGrid` to find all creatures with entry `NPC_SENTINEL` (15264) within 90 yards of the source unit. It adds each found creature to the local buddy list via `AddBuddyToList`.
*   **`GetOtherSentinels`**: Orchestrates the pre-combat setup.
    1.  Initializes a vector to track chosen abilities.
    2.  Selects an ability for the current sentinel using `pickAbilityRandom`.
    3.  Clears the existing buddy list and repopulates it with nearby sentinels via `AddSentinelsNear`.
    4.  Iterates through the newly found buddies. For each buddy, it recursively calls `AddSentinelsNear` (expanding the search radius effectively), disables further gathering for that buddy (`gatherOthersWhenAggro = false`), and assigns it a unique random ability.
    5.  Calls `SendMyListToBuddies` to synchronize the group lists.
    6.  Calls `CallBuddiesToAttack` to initiate combat for the entire group.

### Combat & Ability Logic

*   **`selectAbility`**: Maps an integer index (0–8) to a specific spell ID representing a sentinel ability (e.g., Mending, Mana Burn, Reflect, Thunder, Storm, Knock Away).
*   **`pickAbilityRandom`**: Selects an unused ability index from the `chosenAbilities` vector. It attempts two passes: first a random start index, then a linear scan from 0. It marks the selected index as used and returns it.
*   **`GainSentinelAbility`**: Applies the aura corresponding to the provided spell ID to the sentinel.
*   **`Aggro`**: Triggered when the sentinel enters combat.
    1.  If `gatherOthersWhenAggro` is true, it calls `GetOtherSentinels` to form the group and assign abilities.
    2.  Applies the sentinel's assigned ability via `GainSentinelAbility`.
    3.  Marks the creature as in combat with the zone.
*   **`UpdateAI`**: The main update loop.
    1.  Checks for a valid hostile target.
    2.  If the sentinel has the **Knock Away** ability (`SPELL_KNOCK_BUFF`), it manages a 13-second timer. When expired, it casts `SPELL_KNOCK` on the victim and resets the timer.
    3.  If not yet enraged and health drops below 30%, it casts `SPELL_ENRAGE`, plays the emote, and sets the `m_bEnraged` flag.
    4.  Performs standard melee attacks if ready.
*   **`JustDied`**: Handles the death event.
    1.  Assumes the sentinel is alone (`m_bAlone = true`).
    2.  Iterates through the `nearby` buddy list. For each living buddy:
        *   Sets `m_bAlone = false`.
        *   Casts `SPELL_TRANSFER` and `SPELL_HEAL_BRETHREN` on the buddy.
        *   If the buddy has `aqsentinelAI`, it grants the dead sentinel's ability to the buddy via `GainSentinelAbility`.
    3.  If at least one buddy survived, plays the transfer emote.
*   **`SpellHitTarget`**: Reduces threat by 20% on the current victim if the sentinel successfully hits a player with `SPELL_KNOCK` (Knock Away). This mitigates aggro loss from the knockback effect.

### Perception & Cleanup

*   **`MoveInLineOfSight`**: Overrides the default perception. If a player is within 45 yards, in line of sight, not feigning death, and the sentinel is not already in combat, it initiates an attack. This extends the standard aggro range.
*   **`Reset`**: Called when the creature respawns or resets.
    1.  If the sentinel is not dead, it checks its `nearby` list and respawns any dead buddies.
    2.  Clears the buddy list.
    3.  Resets `gatherOthersWhenAggro` to true, `m_bEnraged` to false, and the knock timer to 13 seconds.
*   **`CallBuddiesToAttack`**: Iterates through the `nearby` list. For each buddy not already in combat, it disables assistance calls (`SetNoCallAssistance`) and forces an attack start on the specified target.

### Script Registration

*   **`GetAI_mob_anubisath_sentinelAI`**: Factory function returning a new `aqsentinelAI` instance.
*   **`AddSC_mob_anubisath_sentinel`**: Registers the script with the engine, linking the name "mob_anubisath_sentinel" to the factory function.

## Cross-Unit Boundaries

*   **`ScriptedAI`**: `aqsentinelAI` inherits from `ScriptedAI` (`ScriptedAI/ScriptedAI`). It overrides `MoveInLineOfSight`, `Reset`, `SpellHitTarget`, `Aggro`, `JustDied`, and `UpdateAI`. It uses helper methods like `DoCastSpellIfCan` and `DoMeleeAttackIfReady` provided by the base class.
*   **`BasicAI` / `CreatureAI`**: `MoveInLineOfSight` calls `BasicAI::MoveInLineOfSight` and `CreatureAI::AttackStart`. `CallBuddiesToAttack` and `GetOtherSentinels` use `CreatureAI::AttackStart` to force aggro on buddies.
*   **`Unit` / `WorldObject`**: Extensive use of `Unit` methods for state checks (`IsInCombat`, `HasAuraType`, `IsDead`, `GetHealthPercent`, `GetVictim`, `SelectHostileTarget`, `AddAura`, `GetThreatManager`) and spatial queries (`IsWithinDistInMap`, `IsWithinLOSInMap`). `WorldObject` methods are used for map context (`GetMap`) and type identification (`GetTypeId`).
*   **`Map`**: Used to retrieve `Creature` pointers from GUIDs via `Map::GetCreature` in `GiveBuddyMyList`, `CallBuddiesToAttack`, `GetOtherSentinels`, `Reset`, and `JustDied`.
*   **`GridSearchers`**: `AddSentinelsNear` uses `GetCreatureListWithEntryInGrid` to find nearby sentinels.
*   **`ThreatManager`**: `SpellHitTarget` accesses the threat manager to modify threat percentages.
*   **`ScriptMgr`**: `JustDied` and `UpdateAI` use `DoScriptText` to play emotes. `AddSC_mob_anubisath_sentinel` registers the script via `ScriptMgr::RegisterSelf`.
*   **`ScriptLoader`**: `AddSC_mob_anubisath_sentinel` is called by `ScriptLoader::AddScripts` during server startup.

## Data Model

This unit does not access any database tables. All configuration (spell IDs, creature entries, ranges) is hardcoded in the source.

## Notable Implementation Details

*   **Recursive Buddy Discovery**: In `GetOtherSentinels`, after finding nearby sentinels, the code iterates through them and calls `AddSentinelsNear` on *each* buddy. This effectively expands the search radius beyond the initial 90 yards centered on the original sentinel, creating a larger cohesive group. However, `gatherOthersWhenAggro` is set to `false` for these secondary searches to prevent infinite loops or redundant processing.
*   **Ability Uniqueness**: The `pickAbilityRandom` function ensures that no two sentinels in the same group start with the same ability. It uses a boolean vector to track used indices.
*   **Threat Mitigation for Knock Away**: The `SpellHitTarget` override specifically reduces threat by 20% when `SPELL_KNOCK` lands on a player. This is a critical balance mechanic to prevent tanks from losing aggro due to the high damage/interrupt nature of the knockback.
*   **Death Chain Reaction**: The `JustDied` logic ensures that abilities are passed to *any* living buddy. If multiple buddies are alive, the ability is passed to the first one encountered in the `nearby` set iteration. The healing spell `SPELL_HEAL_BRETHREN` is also cast on the recipient.
*   **Respawn Logic**: The `Reset` method attempts to respawn dead buddies if the sentinel itself is not dead. This is unusual for a reset handler, which typically runs on spawn; however, it may serve to clean up stuck corpses if the reset is triggered manually or under specific conditions.
*   **Hardcoded Ranges**: The aggro range is extended to 45 yards in `MoveInLineOfSight`, while the buddy detection range is fixed at 90 yards in `AddSentinelsNear`.

## Member Reference

*   **`selectAbility`**: Maps an integer index (0–8) to a specific sentinel ability spell ID.
*   **`aqsentinelAI`**: Constructor that initializes state variables, clears the buddy list, and calls `Reset`.
*   **`MoveInLineOfSight`**: Extends aggro range to 45 yards for players in line of sight who are not feigning death.
*   **`ClearBuddyList`**: Empties the set of nearby sentinel GUIDs.
*   **`AddBuddyToList`**: Adds a creature's GUID to the nearby set if not already present.
*   **`GiveBuddyMyList`**: Propagates the current sentinel's buddy list to another sentinel, ensuring mutual awareness.
*   **`SendMyListToBuddies`**: Calls `GiveBuddyMyList` for all entries in the nearby list.
*   **`CallBuddiesToAttack`**: Forces all non-combatting buddies to attack a specified target.
*   **`AddSentinelsNear`**: Finds all Anubisath Sentinels within 90 yards and adds them to the buddy list.
*   **`pickAbilityRandom`**: Selects an unused ability index from a pool of 9, marking it as used.
*   **`GetOtherSentinels`**: Orchestrates group formation, ability assignment, and initial aggro for all nearby sentinels.
*   **`Reset`**: Respawns dead buddies, clears the buddy list, and resets combat flags and timers.
*   **`GainSentinelAbility`**: Applies the aura for the sentinel's assigned ability.
*   **`SpellHitTarget`**: Reduces threat by 20% on the victim if the Knock Away spell hits a player.
*   **`Aggro`**: Triggers group formation (if needed), applies the assigned ability, and marks combat status.
*   **`JustDied`**: Transfers the sentinel's ability and heals a surviving buddy; plays an emote if a buddy survives.
*   **`UpdateAI`**: Manages the Knock Away timer, enrage check at 30% health, and melee attacks.
*   **`GetAI_mob_anubisath_sentinelAI`**: Factory function to create the AI instance.
*   **`AddSC_mob_anubisath_sentinel`**: Registers the script with the engine.

---

<!-- machine-true, projected from graph.json -->

## Map — mob_anubisath_sentinel

*Source:* mob_anubisath_sentinel.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| selectAbility | method | — | — | — |
| aqsentinelAI | ctor | ScriptedAI/ScriptedAI | — | — |
| MoveInLineOfSight | method | BasicAI/MoveInLineOfSight, CreatureAI/AttackStart, Object/GetTypeId, Unit.Main/HasAuraType, Unit.Main/IsInCombat, WorldObject.Object/IsWithinDistInMap, WorldObject.Object/IsWithinLOSInMap | — | — |
| ClearBuddyList | method | — | — | — |
| AddBuddyToList | method | Object/GetObjectGuid | — | — |
| GiveBuddyMyList | method | Creature.Main/AI, Map.Main/GetCreature, WorldObject.Object/GetMap | — | — |
| SendMyListToBuddies | method | — | — | — |
| CallBuddiesToAttack | method | Creature.Main/AI, Creature.Main/SetNoCallAssistance, CreatureAI/AttackStart, Map.Main/GetCreature, Unit.Main/IsInCombat, WorldObject.Object/GetMap | — | — |
| AddSentinelsNear | method | GridSearchers/GetCreatureListWithEntryInGrid#2 | — | — |
| pickAbilityRandom | method | — | — | — |
| GetOtherSentinels | method | Creature.Main/AI, Map.Main/GetCreature, WorldObject.Object/GetMap | — | — |
| Reset | method | Creature.Main/Respawn, Map.Main/GetCreature, Unit.Main/IsDead, WorldObject.Object/GetMap | — | — |
| GainSentinelAbility | method | Unit.Main/AddAura | — | — |
| SpellHitTarget | method | Object/GetTypeId, ThreatManager/getThreat, ThreatManager/modifyThreatPercent#2, Unit.Main/GetThreatManager, Unit.Main/GetVictim | — | — |
| Aggro | method | Creature.Main/SetInCombatWithZone | — | — |
| JustDied | method | Creature.Main/AI, CreatureAI/DoCastSpellIfCan, Map.Main/GetCreature, ScriptMgr/DoScriptText, Unit.Main/IsDead, WorldObject.Object/GetMap | — | — |
| UpdateAI | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, ScriptMgr/DoScriptText, Unit.Main/GetHealthPercent, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_mob_anubisath_sentinelAI | function | — | — | — |
| AddSC_mob_anubisath_sentinel | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
