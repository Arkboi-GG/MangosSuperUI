# boss_general_angerforge

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`boss_general_angerforge.cpp` implements the AI for **General Angerforge**, a boss in the **Blackrock Depths** dungeon. The unit defines `boss_general_angerforgeAI`, which handles standard melee combat, periodic casting of **Sunder Armor**, and a single-phase transition at 30% health. At this threshold, the boss emits an alarm emote and summons ten reinforcements (Anvilrage Reservists and Medics) at hardcoded coordinates. These summons follow the boss closely. The unit also provides the factory and registration functions required by the server’s script system. No database tables are accessed.

## Member-by-Member Behavior

### Initialization & State
*   **`boss_general_angerforgeAI` (ctor)**: Calls the base `ScriptedAI` constructor and immediately invokes `Reset()` to initialize timers.
*   **`Reset`**: Sets `m_uiSunderArmorTimer` to a random interval between 5–10 seconds and `m_uiAlarmTimer` to 0.

### Combat Loop
*   **`UpdateAI`**: Executed each tick. It returns early if no valid target exists. It then:
    1.  Manages `m_uiSunderArmorTimer`: casts `SPELL_SUNDER_ARMOR` on the victim when expired, resetting the timer to 5–15 seconds.
    2.  Checks health: if below 30%, it triggers the alarm sequence if `m_uiAlarmTimer` is expired. This plays emote `EMOTE_ALARM` (5286), summons all creatures from `m_aAddspawnLocs` (despawning after 30 minutes or death), and sets `m_uiAlarmTimer` to 3 minutes to prevent re-triggering.
    3.  Calls `DoMeleeAttackIfReady()` for physical attacks.

### Summons
*   **`JustSummoned`**: Called when a summoned add enters the world. It commands the add to `MoveFollow` the boss with zero distance/offset, keeping them clustered.

### Registration
*   **`GetAI_boss_general_angerforge`**: Factory function returning a new `boss_general_angerforgeAI` instance.
*   **`AddSC_boss_general_angerforge`**: Creates a `Script` object, assigns the name `"boss_general_angerforge"` and the `GetAI` factory, and registers it via `ScriptMgr::RegisterSelf`. Called by `ScriptLoader::AddScripts`.

## Cross-Unit Boundaries

*   **`ScriptedAI`**: Base class providing AI framework.
*   **`shared_Util`**: `urand` used for randomizing spell timers.
*   **`CreatureAI`**: `DoCastSpellIfCan` and `DoMeleeAttackIfReady` used in `UpdateAI`.
*   **`Unit.Main`**: `GetHealthPercent`, `GetVictim`, `SelectHostileTarget`, and `GetMotionMaster` used for state checks and movement setup.
*   **`Creature.MotionMaster`**: `MoveFollow` used in `JustSummoned`.
*   **`ScriptMgr`**: `DoScriptText` for emotes; `RegisterSelf` for script loading.
*   **`WorldObject.Object`**: `SummonCreature` used to spawn adds.
*   **`ScriptLoader`**: `AddScripts` calls `AddSC_boss_general_angerforge`.

## Data Model

No database tables are accessed. All data (spell IDs, NPC entries, coordinates) is hardcoded in enums and static arrays.

## Notable Implementation Details

*   **Fixed Spawn Points**: Reinforcements spawn at absolute coordinates defined in `m_aAddspawnLocs`, regardless of the boss's current position.
*   **One-Time Wave**: The 3-minute `m_uiAlarmTimer` prevents the reinforcement wave from repeating during a single engagement.
*   **Immediate Trigger**: Since `m_uiAlarmTimer` starts at 0, the wave spawns instantly when health drops below 30%.
*   **No Explicit Despawn on Boss Death**: Adds persist until death or the 30-minute timeout; they do not despawn automatically if the boss dies.

## Member Reference

**boss_general_angerforgeAI** (ctor): Initializes the AI object and calls `Reset()`.

**Reset**: Sets `m_uiSunderArmorTimer` to 5–10 seconds and `m_uiAlarmTimer` to 0.

**JustSummoned**: Commands the summoned creature to follow the boss with zero distance/offset.

**UpdateAI**: Handles target validation, `Sunder Armor` casting, the <30% health alarm/summon trigger, and melee attacks.

**GetAI_boss_general_angerforge**: Factory function creating a new `boss_general_angerforgeAI` instance.

**AddSC_boss_general_angerforge**: Registers the script with `ScriptMgr` via `RegisterSelf`.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_general_angerforge

*Source:* boss_general_angerforge.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_general_angerforgeAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | method | shared_Util/urand | — | — |
| JustSummoned | method | Creature.MotionMaster/MoveFollow, Unit.Main/GetMotionMaster | — | — |
| UpdateAI | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, ScriptMgr/DoScriptText, shared_Util/urand, Unit.Main/GetHealthPercent, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, WorldObject.Object/SummonCreature#2 | — | — |
| GetAI_boss_general_angerforge | function | — | — | — |
| AddSC_boss_general_angerforge | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
