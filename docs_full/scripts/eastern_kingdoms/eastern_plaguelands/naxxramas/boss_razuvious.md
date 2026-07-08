# boss_razuvious

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`boss_razuvious.cpp` implements the AI for **Razuvious** and his summoned **Death Knight Understudies** in the Naxxramas raid. It manages two distinct behaviors:
1.  **Boss Combat:** Razuvious’s spell rotation (`Unbalancing Strike`, `Disrupting Shout`) and random taunts.
2.  **Minion Coordination:** Summoning, tracking, and despawning four Understudies. One specific Understudy ("RP Buddy") participates in a pre-combat cinematic sequence involving synchronized emotes and facing directions.

The unit integrates with `instance_naxxramas` to report encounter state (Aggro, Fail, Done) and handles the transition between the RP sequence and combat.

## Member-by-Member Behavior

### Boss Razuvious AI (`boss_razuviousAI`)

**Lifecycle & State**
*   **`boss_razuviousAI` (ctor):** Retrieves `instance_naxxramas` data, calls `Reset()`, and immediately invokes `RespawnAdds()` to populate the arena.
*   **`Reset`:** Clears the combat `EventMap`. Note: It does *not* clear `rpEvents` or respawn adds; those are handled by `JustReachedHome` or the constructor.
*   **`JustReachedHome`:** Triggered on evade/death. Sets instance state to `FAIL` and calls `RespawnAdds()` to restore minions.
*   **`JustDied`:** Plays death sound, casts `SPELL_HOPELESS`, and sets instance state to `DONE`.
*   **`KilledUnit`:** 25% chance to play a random slay sound.

**Combat & Aggro**
*   **`Aggro`:** Plays random aggro sound, sets state to `IN_PROGRESS`, resets event maps, calls for help, and schedules:
    *   `EVENT_UNBALANCING_STRIKE` (30s)
    *   `EVENT_DISRUPTING_SHOUT` (15s)
    *   `EVENT_COMMAND` (40s)
*   **`UpdateAI`:**
    *   If not in combat, delegates to `UpdateRP`.
    *   If in combat, checks `HandleEvadeOutOfHome`.
    *   Executes `events`:
        *   `EVENT_UNBALANCING_STRIKE`: Casts on victim, repeats every 30s.
        *   `EVENT_DISRUPTING_SHOUT`: Casts on victim, shouts, repeats every 25s.
        *   `EVENT_COMMAND`: Random taunt, repeats every 30–60s.
    *   Performs melee attacks.
*   **`MoveInLineOfSight`:** Initiates combat if a hostile target is within 33 yards, has LOS, and is accessible. Adds threat to existing victims in dungeons.

**Minion & RP Management**
*   **`RespawnAdds`:** Unsummons any existing tracked Understudies, then summons four new ones at fixed coordinates. Tracks their GUIDs in `summonedAdds` and designates the second one (index 1) as `rpBuddy`.
*   **`MovementInform`:** If waypoint ID 6 is reached (returning home), resets `rpEvents` and schedules a precise cinematic sequence (turning, shouting, saluting).
*   **`getRPBuddy`:** Returns the `Creature` pointer for `rpBuddy` from the instance script.
*   **`UpdateRP`:** Drives the cinematic sequence via `rpEvents`:
    *   Coordinates facing and emotes between Razuvious and `rpBuddy`.
    *   Directly manipulates `mob_deathknightUnderstudyAI` members (`attackTimer`, `runAttack`) to pause/resume the buddy's idle animations during the scene.
    *   `EVENT_ADD_TURN_BACK`: Makes the buddy face a nearby NPC (entry 16211) if present.

### Death Knight Understudy AI (`mob_deathknightUnderstudyAI`)

*   **`mob_deathknightUnderstudyAI` (ctor):** Initializes instance data and calls `Reset()`.
*   **`Reset`:** Sets emote to `READY1H`, initializes a random attack timer (5–10s), and enables `runAttack`.
*   **`Aggro`:** Disables `runAttack` (standard combat takes over) and calls for help.
*   **`UpdateAI`:**
    *   If `runAttack` is true (idle/RP state), plays an `ATTACK1H` emote periodically.
    *   Otherwise, performs standard melee combat.

### Registration

*   **`GetAI_boss_razuvious` / `GetAI_mob_deathknightUnderstudy`:** Factory functions for the AI classes.
*   **`AddSC_boss_razuvious`:** Registers scripts `boss_razuvious` and `deathknight_understudy_ai`.

## Cross-Unit Boundaries

*   **`instance_naxxramas`**:
    *   **Called by:** `boss_razuviousAI` (ctor, `RespawnAdds`, `JustReachedHome`, `JustDied`, `Aggro`, `UpdateAI`, `getRPBuddy`).
    *   **Collaboration:** Manages encounter state (`SetData`/`GetData`), provides creature pointers for summons (`GetCreature`), and handles evasion logic (`HandleEvadeOutOfHome`).
*   **`mob_deathknightUnderstudyAI`**:
    *   **Called by:** `boss_razuviousAI::UpdateRP`.
    *   **Collaboration:** Razuvious’s AI directly casts the Understudy’s AI pointer to modify `attackTimer` and `runAttack`, synchronizing the RP sequence.
*   **`ScriptedAI` / `CreatureAI`**:
    *   **Calls out:** Base methods (`DoMeleeAttackIfReady`, `DoCastSpellIfCan`, etc.) for standard combat actions.
*   **`ScriptMgr`**:
    *   **Called by:** Both AI classes via `DoScriptText` for sounds/emotes.

## Data Model

No database tables are accessed. State is managed in-memory via:
*   `instance_naxxramas`: Raid instance state.
*   `summonedAdds` (`std::vector<ObjectGuid>`): Tracks summoned Understudies.
*   `rpBuddy` (`ObjectGuid`): Identifies the Understudy in the RP sequence.
*   `events` / `rpEvents` (`EventMap`): Timed triggers for combat and cinematics.

## Notable Implementation Details

1.  **Direct AI Manipulation:** `UpdateRP` casts `b->AI()` to `mob_deathknightUnderstudyAI*` to modify private members. This tight coupling ensures precise RP synchronization but breaks encapsulation.
2.  **Hardcoded Positions:** Understudy spawn points are fixed in `addPositions`. Map changes require manual coordinate updates.
3.  **RP Timing:** `MovementInform` uses mixed `Seconds()`/`Milliseconds()` for precise cinematic choreography. `EVENT_ADD_TURN_BACK` depends on NPC 16211 being within 5 yards.
4.  **Fail State:** `JustReachedHome` sets state to `FAIL`, indicating an incomplete encounter.
5.  **Add Cleanup:** `RespawnAdds` unsummons tracked adds before spawning new ones, preventing duplicates. It safely handles missing creatures by checking `GetCreature` results.

## Member Reference

**mob_deathknightUnderstudyAI** (ctor): Initializes instance data and calls `Reset()`.

**Reset#2**: Sets emote to `READY1H`, initializes random attack timer (5–10s), enables `runAttack`.

**Aggro#2**: Disables `runAttack`, calls for help.

**UpdateAI#2**: Plays idle attack emote if `runAttack` is true; otherwise performs melee combat.

**boss_razuviousAI** (ctor): Initializes instance data, calls `Reset()` and `RespawnAdds()`.

**Reset**: Clears combat `EventMap`.

**MoveInLineOfSight**: Initiates combat if hostile target is within 33 yards, has LOS, and is accessible.

**RespawnAdds**: Unsummons tracked Understudies, summons four new ones at fixed positions, tracks GUIDs, designates `rpBuddy`.

**JustReachedHome**: Sets instance state to `FAIL`, calls `RespawnAdds()`.

**KilledUnit**: 25% chance to play random slay sound.

**JustDied**: Plays death sound, casts `SPELL_HOPELESS`, sets instance state to `DONE`.

**Aggro**: Plays aggro sound, sets state to `IN_PROGRESS`, resets event maps, calls for help, schedules combat abilities.

**MovementInform**: Schedules RP sequence if waypoint ID 6 is reached.

**getRPBuddy**: Returns `Creature` pointer for `rpBuddy` from instance script.

**UpdateRP**: Executes RP sequence, coordinates emotes/facing, directly manipulates `mob_deathknightUnderstudyAI` state.

**UpdateAI**: Delegates to `UpdateRP` if not in combat; otherwise executes combat events and melee attacks.

**GetAI_boss_razuvious**: Factory function for `boss_razuviousAI`.

**GetAI_mob_deathknightUnderstudy**: Factory function for `mob_deathknightUnderstudyAI`.

**AddSC_boss_razuvious**: Registers `boss_razuvious` and `deathknight_understudy_ai` scripts.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_razuvious

*Source:* boss_razuvious.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| mob_deathknightUnderstudyAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset#2 | method | shared_Util/urand, Unit.Main/HandleEmote | — | — |
| Aggro#2 | method | Creature.Main/CallForHelp | — | — |
| UpdateAI#2 | method | CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, Unit.Main/GetVictim, Unit.Main/HandleEmote, Unit.Main/SelectHostileTarget | — | — |
| boss_razuviousAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | EventMap/Reset | — | — |
| MoveInLineOfSight | method | Creature.Main/CanInitiateAttack, CreatureAI/AttackStart, Map.Main/IsDungeon, Unit.Main/AddThreat, Unit.Main/GetVictim, Unit.Main/IsHostileTo, Unit.Main/IsInAccessablePlaceFor, Unit.Main/IsTargetableBy, Unit.Main/SetInCombatWith, WorldObject.Object/GetMap, WorldObject.Object/IsWithinDistInMap, WorldObject.Object/IsWithinLOSInMap | — | — |
| RespawnAdds | method | instance_naxxramas.Main/GetData, Object/GetObjectGuid, TemporarySummon/UnSummon, WorldObject.Object/SetUInt32Value, WorldObject.Object/SummonCreature#2, ZoneScript/GetCreature | — | — |
| JustReachedHome | method | instance_naxxramas.Main/SetData | — | — |
| KilledUnit | method | ScriptMgr/DoScriptText, shared_Util/urand | — | — |
| JustDied | method | CreatureAI/DoCastSpellIfCan, instance_naxxramas.Main/SetData, ScriptMgr/DoScriptText | — | — |
| Aggro | method | Creature.Main/CallForHelp, EventMap/Reset, EventMap/ScheduleEvent#2, instance_naxxramas.Main/SetData, ScriptMgr/DoScriptText, shared_Util/urand | — | — |
| MovementInform | method | EventMap/Reset, EventMap/ScheduleEvent#2 | — | — |
| getRPBuddy | method | ZoneScript/GetCreature | — | — |
| UpdateRP | method | Creature.Main/AI, EventMap/ExecuteEvent, EventMap/Update, GridSearchers/GetCreatureListWithEntryInGrid#2, Unit.Main/HandleEmote, Unit.Main/SetFacingToObject | — | — |
| UpdateAI | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, EventMap/ExecuteEvent, EventMap/Repeat, EventMap/Update, instance_naxxramas.Main/HandleEvadeOutOfHome, ScriptMgr/DoScriptText, shared_Util/urand, Unit.Main/GetVictim, Unit.Main/IsInCombat, Unit.Main/SelectHostileTarget | — | — |
| GetAI_boss_razuvious | function | — | — | — |
| GetAI_mob_deathknightUnderstudy | function | — | — | — |
| AddSC_boss_razuvious | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
