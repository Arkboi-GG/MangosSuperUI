# boss_patchwerk

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_patchwerk

**Purpose & Responsibilities**  
`boss_patchwerk.cpp` implements the AI for **Patchwerk**, a Naxxramas raid boss. It manages melee combat, periodic abilities (**Hateful Strike**, **Slimebolt**), and enrage timers (**Soft Enrage** at 5% HP, **Hard Berserk** at 7 minutes). The script tracks instance progress (`IN_PROGRESS`, `DONE`, `FAIL`) via `instance_naxxramas` and uses `EventMap` for timed events. It inherits from `ScriptedAI` and overrides core hooks to customize target selection and ability execution.

## Member-by-Member Behavior

### Initialization & State
- **`boss_patchwerkAI` (ctor)**: Retrieves `instance_naxxramas` data and calls `Reset()`.
- **`Reset`**: Clears `m_events`, resets enrage flags (`m_bEnraged`, `m_bBerserk`), `m_failedStrikes`, and `m_previousTarget`.

### Combat Lifecycle
- **`KilledUnit`**: Plays `SAY_SLAY` with a 20% chance (`urand(0, 4)` returns 0 20% of the time).
- **`JustDied`**: Plays `SAY_DEATH` and sets instance data to `DONE`.
- **`JustReachedHome`**: Sets instance data to `FAIL`.
- **`Aggro`**: Plays random aggro quote, sets instance data to `IN_PROGRESS`, and schedules `EVENT_BERSERK` (7 min), `EVENT_HATEFULSTRIKE` (1.2s), and `EVENT_SLIMEBOLT` (7m30s, if client ≥ 1.12.1).

### Abilities & Targeting
- **`DoHatefulStrike`**: Targets the highest-HP player in melee range among the top 4 threat list entries, excluding the main tank unless no other target exists. If the cast fails due to range, it increments `m_failedStrikes`; after 3 failures, it summons the target via `SPELL_SUMMON_PLAYER` (if not teleporting). Resets `m_failedStrikes` on success.
- **`CustomGetTarget`**: Overrides default targeting. Returns `false` if dead. Selects the top threat target if available, checking for CC states (stun, fear, etc.) before attacking. If no target but combat persists (taunt/charm), it prevents evasion. If attackers exist but aren’t in the threat list (e.g., pets), it prevents evasion. Otherwise, calls `OnLeaveCombat()`.
- **`UpdateAI`**: Calls `CustomGetTarget()`. Triggers **Soft Enrage** (`SPELL_ENRAGE`) if HP < 5%. Processes events:
  - `EVENT_BERSERK`: Casts `SPELL_BERSERK` (hard enrage).
  - `EVENT_HATEFULSTRIKE`: Calls `DoHatefulStrike()`.
  - `EVENT_SLIMEBOLT`: Casts `SPELL_SLIMEBOLT` on victim (anti-kite).
  Retries failed casts in 100ms. Ends with `DoMeleeAttackIfReady()`.

### Registration
- **`GetAI_boss_patchwerk`**: Factory function returning a new `boss_patchwerkAI`.
- **`AddSC_boss_patchwerk`**: Registers the script with `ScriptMgr`.

## Cross-Unit Boundaries

| Member | Direction | Collaborating Unit | Interaction |
|--------|-----------|---------------------|-------------|
| `boss_patchwerkAI` | Calls | `ScriptedAI` | Base AI inheritance. |
| `boss_patchwerkAI` | Calls | `WorldObject::GetInstanceData` | Gets `instance_naxxramas` data. |
| `Reset` | Calls | `EventMap::Reset` | Clears timers. |
| `KilledUnit` | Calls | `ScriptMgr::DoScriptText` | Plays kill quote. |
| `KilledUnit` | Calls | `shared_Util::urand` | Randomizes quote. |
| `JustDied` | Calls | `instance_naxxramas::SetData` | Reports `DONE`. |
| `JustDied` | Calls | `ScriptMgr::DoScriptText` | Plays death quote. |
| `JustReachedHome` | Calls | `instance_naxxramas::SetData` | Reports `FAIL`. |
| `Aggro` | Calls | `EventMap::ScheduleEvent` | Schedules abilities. |
| `Aggro` | Calls | `instance_naxxramas::SetData` | Reports `IN_PROGRESS`. |
| `Aggro` | Calls | `ScriptMgr::DoScriptText` | Plays aggro quote. |
| `Aggro` | Calls | `shared_Util::urand` | Randomizes aggro quote. |
| `DoHatefulStrike` | Calls | `HostileReference::getUnitGuid` | Gets threat list GUIDs. |
| `DoHatefulStrike` | Calls | `Log::Out` | Logs missing spell error. |
| `DoHatefulStrike` | Calls | `Object::GetObjectGuid`, `ToPlayer` | Validates targets. |
| `DoHatefulStrike` | Calls | `Player::IsBeingTeleported` | Checks teleport status. |
| `DoHatefulStrike` | Calls | `SpellCaster::CastSpell` | Casts Hateful Strike/Summon. |
| `DoHatefulStrike` | Calls | `SpellMgr::GetSpellEntry` | Fetches spell data. |
| `DoHatefulStrike` | Calls | `ThreatManager::getThreatList` | Iterates threat list. |
| `DoHatefulStrike` | Calls | `Unit::GetHealth`, `GetVictim`, `IsImmuneToSpell`, `SetInFront`, `SetTargetGuid` | Manages targeting/casts. |
| `DoHatefulStrike` | Calls | `WorldObject::CanReachWithMeleeSpellAttack`, `IsInMap` | Checks range/map. |
| `CustomGetTarget` | Calls | `Creature::OnLeaveCombat` | Evades combat. |
| `CustomGetTarget` | Calls | `Creature::MotionMaster::GetCurrentMovementGeneratorType` | Checks movement type. |
| `CustomGetTarget` | Calls | `CreatureAI::AttackStart` | Starts attack. |
| `CustomGetTarget` | Calls | `ThreatManager::getHostileTarget`, `isThreatListEmpty` | Gets target. |
| `CustomGetTarget` | Calls | `Unit::CanReachWithMeleeAutoAttack`, `GetAttackers`, `GetCharmerGuid`, `GetMotionMaster`, `GetThreatManager`, `HasAuraType`, `HasUnitState`, `IsAlive`, `IsAttackReady`, `IsInCombat`, `IsTargetableBy`, `SetInFront`, `SetTargetGuid` | Manages state/targeting. |
| `CustomGetTarget` | Calls | `WorldObject::IsInMap` | Checks attacker map presence. |
| `UpdateAI` | Calls | `CreatureAI::DoCastSpellIfCan`, `DoMeleeAttackIfReady` | Executes spells/melee. |
| `UpdateAI` | Calls | `EventMap::ExecuteEvent`, `Repeat`, `Update` | Processes events. |
| `UpdateAI` | Calls | `ScriptMgr::DoScriptText` | Plays enrage emotes. |
| `UpdateAI` | Calls | `Unit::GetHealthPercent`, `GetVictim` | Checks HP/victim. |
| `AddSC_boss_patchwerk` | Calls | `Script::Script`, `ScriptMgr::RegisterSelf` | Registers script. |
| `AddSC_boss_patchwerk` | Called By | `ScriptLoader::AddScripts` | Loads script. |

## Data Model

No database tables are accessed.

## Notable Implementation Details

1. **Hateful Strike Targeting**: Prioritizes highest-HP melee player in top 4 threat, skipping main tank if others exist. Summons target after 3 failed range checks.
2. **Enrage Mechanics**: Soft enrage (`SPELL_ENRAGE`) at <5% HP; Hard Berserk (`SPELL_BERSERK`) at 7 minutes.
3. **Slimebolt**: Anti-kite spell starting 30s after berserk, repeating every 5s (client ≥ 1.12.1 only).
4. **Custom Targeting**: `CustomGetTarget` prevents evasion if attackers exist but aren’t in threat list (e.g., pets) or if taunted/charmed.
5. **Event Retries**: Failed casts retry in 100ms.

## Member Reference

- **`boss_patchwerkAI`**: Constructor initializing AI, instance data, and state.
- **`Reset`**: Clears events, enrage flags, failed strikes, and previous target.
- **`KilledUnit`**: Plays kill quote with 20% chance.
- **`JustDied`**: Plays death quote and reports `DONE`.
- **`JustReachedHome`**: Reports `FAIL`.
- **`Aggro`**: Plays aggro quote, reports `IN_PROGRESS`, schedules abilities.
- **`DoHatefulStrike`**: Targets high-HP melee players, casts Hateful Strike, summons after 3 failures.
- **`CustomGetTarget`**: Overrides targeting for CC, taunts, and edge cases.
- **`UpdateAI`**: Main loop for events, enrage checks, and melee.
- **`GetAI_boss_patchwerk`**: Factory function for AI instance.
- **`AddSC_boss_patchwerk`**: Registers script with engine.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_patchwerk

*Source:* boss_patchwerk.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_patchwerkAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | EventMap/Reset, ObjectGuid/Clear | — | — |
| KilledUnit | method | ScriptMgr/DoScriptText, shared_Util/urand | — | — |
| JustDied | method | instance_naxxramas.Main/SetData, ScriptMgr/DoScriptText | — | — |
| JustReachedHome | method | instance_naxxramas.Main/SetData | — | — |
| Aggro | method | EventMap/ScheduleEvent#3, instance_naxxramas.Main/SetData, ScriptMgr/DoScriptText, shared_Util/urand | — | — |
| DoHatefulStrike | method | HostileReference/getUnitGuid, Log.Main/Out, Object/GetObjectGuid, Object/ToPlayer, ObjectGuid/operator!=, Player.Main/IsBeingTeleported, SpellCaster/CastSpell, SpellCaster/CastSpell#2, SpellMgr/GetSpellEntry, SpellMgr/Instance, ThreatManager/getThreatList, Unit.Main/GetHealth, Unit.Main/GetThreatManager, Unit.Main/GetVictim, Unit.Main/IsImmuneToSpell, Unit.Main/SetInFront, Unit.Main/SetTargetGuid, WorldObject.Object/CanReachWithMeleeSpellAttack, WorldObject.Object/IsInMap | — | — |
| CustomGetTarget | method | Creature.Main/OnLeaveCombat, Creature.MotionMaster/GetCurrentMovementGeneratorType, CreatureAI/AttackStart, Object/GetObjectGuid, ObjectGuid/operator!=, ThreatManager/getHostileTarget, ThreatManager/isThreatListEmpty, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/GetAttackers, Unit.Main/GetCharmerGuid, Unit.Main/GetMotionMaster, Unit.Main/GetThreatManager, Unit.Main/HasAuraType, Unit.Main/HasUnitState, Unit.Main/IsAlive, Unit.Main/IsAttackReady, Unit.Main/IsInCombat, Unit.Main/IsTargetableBy, Unit.Main/SetInFront, Unit.Main/SetTargetGuid, WorldObject.Object/IsInMap | — | — |
| UpdateAI | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, EventMap/ExecuteEvent, EventMap/Repeat#3, EventMap/Update, ScriptMgr/DoScriptText, Unit.Main/GetHealthPercent, Unit.Main/GetVictim | — | — |
| GetAI_boss_patchwerk | function | — | — | — |
| AddSC_boss_patchwerk | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
