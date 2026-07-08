# razorfen_downs

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Razorfen Downs Scripts (`razorfen_downs`)

This unit implements scripted behaviors for two specific entities within the **Razorfen Downs** instance: the escort NPC **Bel'nistrasz** and the interactive **Gong** game object. It handles the complex "Extinguishing the Idol" questline, including a multi-phase ritual combat encounter, and provides basic interaction tracking for the gong mechanism.

## Purpose & Responsibilities

The primary responsibility of `razorfen_downs` is to manage the **Bel'nistrasz** escort and ritual sequence. Bel'nistrasz is a dragon who escorts players through the instance to a specific location where he performs a ritual to extinguish fires in an idol room. This involves:
1.  **Escort Logic:** Moving Bel'nistrasz along a predefined path until he reaches the ritual site.
2.  **Ritual Combat:** Once paused at the ritual site, Bel'nistrasz initiates a timed sequence where waves of enemies are summoned. Players must defeat these waves while Bel'nistrasz casts spells and delivers dialogue.
3.  **Quest Completion:** Upon successful completion of the ritual, the associated quest is awarded, and environmental game objects (fires) are deactivated.

Secondarily, the unit handles the **Gong** (`go_gong`), a game object that increments a counter in the instance data when interacted with, likely contributing to a larger puzzle or event mechanic managed by the instance script.

## Member-by-Member Behavior

### Bel'nistrasz AI (`npc_belnistraszAI`)

The `npc_belnistraszAI` class inherits from `npc_escortAI` (from `ScriptedEscortAI`) and manages the state machine for Bel'nistrasz's behavior.

#### Initialization and State Management
*   **`npc_belnistraszAI` (Constructor):** Initializes the AI, retrieves the instance data pointer (`pInstance`), resets ritual phase/timers, and calls `Reset`.
*   **`Reset`:** Resets combat timers (`m_uiFireballTimer`, `m_uiFrostNovaTimer`) to their initial values. This is called during construction and presumably when the creature despawns/resets.
*   **`JustDied`:** Resets all ritual state variables (`m_uiRitualPhase`, `m_uiRitualTimer`, `m_bAggro`) to their default values and calls the base `JustDied` handler. This ensures that if Bel'nistrasz dies during the ritual, the state is cleared for a potential retry.

#### Combat and Aggro Handling
*   **`AttackedBy`:** Handles incoming attacks.
    *   If the escort is **paused** (during the ritual) and Bel'nistrasz hasn't already aggroed (`!m_bAggro`), he plays a random aggro line (`SAY_BELNISTRASZ_AGGRO_1` or `SAY_BELNISTRASZ_AGGRO_2`) via `ScriptMgr/DoScriptText` and sets `m_bAggro` to true. He then returns early, preventing standard aggro propagation.
    *   If not paused, it delegates to the base `CreatureAI/AttackedBy`.
*   **`AttackStart`:** Prevents Bel'nistrasz from initiating combat if the escort is paused and the ritual has started (`m_uiRitualPhase > 0`). Otherwise, it delegates to the base `npc_escortAI::AttackStart`.

#### Summoning Mechanics
*   **`SpawnerSummon`:** Called when a spawner creature is summoned.
    *   If the ritual phase is greater than 7, it summons **Plaguemaw the Rotting** (`NPC_PLAGUEMAW_THE_ROTTING`) at the spawner's location.
    *   Otherwise, it summons four minions in a circle around the spawner using `WorldObject.Object/GetClosePoint` with a random angle. The minions are:
        *   Two **Withered Battle Boars** (`NPC_WITHERED_BATTLE_BOAR`).
        *   One **Withered Quilguard** (`NPC_WITHERED_QUILGUARD`).
        *   One **Deaths Head Geomancer** (`NPC_DEATHS_HEAD_GEOMANCER`).
*   **`JustSummoned`:** Overrides the base handler to immediately call `SpawnerSummon` on the newly summoned creature. This creates a chain reaction where summoning a spawner triggers the summoning of its minions.
*   **`DoSummonRandom`:** Selects one of three predefined coordinates (`m_fSpawnerCoord`) randomly using `shared_Util/urand` and summons an **Idol Room Spawner** (`NPC_IDOL_ROOM_SPAWNER`) at that location. This spawner will then trigger `JustSummoned` -> `SpawnerSummon` to create the actual enemy wave.

#### Ritual Progression
*   **`WaypointReached`:** Triggered when Bel'nistrasz reaches a waypoint.
    *   At waypoint **24**, he says `SAY_BELNISTRASZ_START_RIT` and pauses the escort (`SetEscortPaused(true)`), initiating the ritual sequence.
*   **`UpdateEscortAI`:** The main update loop.
    *   **Paused State (Ritual):** If the escort is paused, it checks `m_uiRitualTimer`. When the timer expires, it advances `m_uiRitualPhase` and executes phase-specific actions:
        *   **Phase 0:** Disables combat movement and casts `SPELL_IDOL_SHUTDOWN` on himself.
        *   **Phases 1, 2, 4, 5, 6, 8:** Calls `DoSummonRandom()` to spawn a wave of enemies. Phases 3, 5, 7, and 9 also include dialogue lines (`SAY_BELNISTRASZ_3_MIN`, etc.).
        *   **Phase 9:** Says `SAY_BELNISTRASZ_FINISH`.
        *   **Phase 10:** Completes the ritual.
            *   Awards the quest `QUEST_EXTINGUISHING_THE_IDOL` to the player via `Player.Main/GroupEventHappens`.
            *   Removes the `SPELL_IDOL_SHUTDOWN` aura.
            *   Summons a brazier game object (`GO_BELNISTRASZ_BRAZIER`).
            *   Deactivates two fire game objects (`GO_IDOL_OVEN_FIRE` and `GO_IDOL_MOUTH_FIRE`) by setting their loot state to `GO_JUST_DEACTIVATED`.
            *   Updates the instance data (`pInstance->SetData(EXTINGUISH_FIRES, 0)`).
            *   Resumes the escort (`SetEscortPaused(false)`).
    *   **Combat State:** If not paused and in combat, it manages spell casting:
        *   Casts `SPELL_FIREBALL` on the victim every 2–3 seconds.
        *   Casts `SPELL_FROST_NOVA` on the victim every 10–15 seconds.
        *   Performs melee attacks if ready.

### Helper Functions

*   **`GetAI_npc_belnistrasz`:** Factory function that creates and returns a new `npc_belnistraszAI` instance for a given creature.
*   **`QuestAccept_npc_belnistrasz`:** Handles quest acceptance for `QUEST_EXTINGUISHING_THE_IDOL`.
    *   Retrieves the AI pointer via `dynamic_cast`.
    *   Starts the escort sequence (`pEscortAI->Start`).
    *   Plays the ready dialogue (`SAY_BELNISTRASZ_READY`).
    *   Sets the creature's faction to neutral active (`FACTION_ESCORT_N_NEUTRAL_ACTIVE`).
*   **`GOHello_go_gong`:** Handles interaction with the Gong game object.
    *   Retrieves the instance data.
    *   Increments the `DATA_GONG_WAVES` counter in the instance data.
    *   Returns `true` to indicate successful interaction.
*   **`AddSC_razorfen_downs`:** Registers the scripts with the script manager.
    *   Registers `npc_belnistrasz` with its AI getter and quest accept handler.
    *   Registers `go_gong` with its hello handler.

## Cross-Unit Boundaries

*   **`ScriptedEscortAI` / `npc_escortAI`:** `npc_belnistraszAI` inherits from this to gain escort functionality (pathing, pausing, player tracking). It calls `HasEscortState`, `SetEscortPaused`, `GetPlayerForEscort`, and `Start`.
*   **`CreatureAI`:** Base AI class providing combat utilities like `AttackedBy`, `AttackStart`, `DoCastSpellIfCan`, `DoMeleeAttackIfReady`, and `SetCombatMovement`.
*   **`ScriptMgr`:** Used to play dialogue lines via `DoScriptText`.
*   **`WorldObject.Object`:** Used for spatial calculations (`GetClosePoint`, `GetOrientation`, `GetPositionX/Y/Z`) and summoning entities (`SummonCreature`, `SummonGameObject`).
*   **`InstanceData` / `ScriptedInstance`:** Used to communicate with the instance script. `pInstance->SetData` updates the instance state (e.g., `EXTINGUISH_FIRES`, `DATA_GONG_WAVES`). `GetInstanceData` retrieves the instance pointer.
*   **`Player.Main`:** `GroupEventHappens` is used to award the quest to the group.
*   **`Unit.Main`:** `GetVictim`, `SelectHostileTarget`, and `RemoveAurasDueToSpell` are used for combat management.
*   **`shared_Util`:** `urand` and `rand_norm_f` provide random number generation for summoning positions and spell cooldowns.
*   **`GridSearchers`:** `GetClosestGameObjectWithEntry` finds the fire game objects to deactivate them.

## Data Model

This unit does not directly access database tables. It interacts with runtime instance data structures (`ScriptedInstance`) and uses hardcoded entity IDs (creature entries, game object entries, spell IDs, quest IDs) defined in enums.

## Notable Implementation Details

*   **Summon Chain Reaction:** The summoning logic relies on a chain: `DoSummonRandom` summons a spawner -> `JustSummoned` calls `SpawnerSummon` -> `SpawnerSummon` summons minions. This allows for flexible wave composition without hardcoding minion positions relative to Bel'nistrasz.
*   **Ritual Phase Timer:** The ritual progresses through phases based on a timer (`m_uiRitualTimer`) that decrements in `UpdateEscortAI`. Each phase has a different duration, creating a varied pace for the encounter.
*   **Aggro Suppression During Ritual:** During the ritual (`STATE_ESCORT_PAUSED`), Bel'nistrasz does not propagate aggro normally. Instead, he plays a specific aggro line once and stops further aggro handling, ensuring he remains focused on the ritual unless directly attacked again.
*   **Hardcoded Coordinates:** The spawner positions are hardcoded in `m_fSpawnerCoord`. This assumes the idol room layout is static.
*   **Gong Counter:** The gong interaction simply increments a counter in the instance data. The actual effect of this counter is handled elsewhere (likely in the instance script), as noted by the comment "basic support, not blizzlike data is missing...".
*   **Spell Usage:** `SPELL_ARCANE_INTELLECT` is defined but commented out as unused. `SPELL_IDOL_SHUTDOWN` is cast on Bel'nistrasz himself during the ritual, possibly to suppress his abilities or change his appearance.

## Member Reference

**npc_belnistraszAI** (ctor): Initializes the AI, retrieves instance data, resets state variables, and calls `Reset`.

**Reset**: Resets fireball and frost nova timers to initial values.

**JustDied**: Resets ritual phase, timer, and aggro flag; calls base `JustDied`.

**AttackedBy**: If paused and not yet aggroed, plays aggro dialogue and sets aggro flag; otherwise delegates to base.

**AttackStart**: Prevents attack initiation if paused and ritual started; otherwise delegates to base.

**SpawnerSummon**: Summons Plaguemaw if phase > 7, else summons four minions in a circle around the spawner.

**JustSummoned**: Calls `SpawnerSummon` on the summoned creature.

**DoSummonRandom**: Randomly selects a coordinate and summons an Idol Room Spawner.

**WaypointReached**: At waypoint 24, plays start ritual dialogue and pauses escort.

**UpdateEscortAI**: Manages ritual phases (summoning, dialogue, quest completion) if paused; manages spell casting and melee if in combat.

**GetAI_npc_belnistrasz**: Factory function returning a new `npc_belnistraszAI` instance.

**QuestAccept_npc_belnistrasz**: Starts escort, plays dialogue, and sets faction for quest acceptance.

**GOHello_go_gong**: Increments gong wave counter in instance data.

**AddSC_razorfen_downs**: Registers Bel'nistrasz and Gong scripts with the script manager.

---

<!-- machine-true, projected from graph.json -->

## Map — razorfen_downs

*Source:* razorfen_downs.cpp, razorfen_downs.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| npc_belnistraszAI | ctor | ScriptedEscortAI/npc_escortAI, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | — | — | — |
| JustDied | method | ScriptedEscortAI/JustDied | — | — |
| AttackedBy | method | CreatureAI/AttackedBy, ScriptedEscortAI/HasEscortState, ScriptMgr/DoScriptText, shared_Util/urand | — | — |
| AttackStart | method | CreatureAI/AttackStart, ScriptedEscortAI/HasEscortState | — | — |
| SpawnerSummon | method | shared_Util/rand_norm_f, WorldObject.Object/GetClosePoint, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SummonCreature#2 | — | — |
| JustSummoned | method | — | — | — |
| DoSummonRandom | method | shared_Util/urand, WorldObject.Object/SummonCreature#2 | — | — |
| WaypointReached | method | ScriptedEscortAI/SetEscortPaused, ScriptMgr/DoScriptText | — | — |
| UpdateEscortAI | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, CreatureAI/SetCombatMovement, GameObject/SetLootState, GridSearchers/GetClosestGameObjectWithEntry, InstanceData/SetData, Player.Main/GroupEventHappens, ScriptedEscortAI/GetPlayerForEscort, ScriptedEscortAI/HasEscortState, ScriptedEscortAI/SetEscortPaused, ScriptMgr/DoScriptText, shared_Util/urand, Unit.Main/GetVictim, Unit.Main/RemoveAurasDueToSpell, Unit.Main/SelectHostileTarget, WorldObject.Object/SummonGameObject | — | — |
| GetAI_npc_belnistrasz | function | — | — | — |
| QuestAccept_npc_belnistrasz | function | Creature.Main/AI, Object/GetGUID, QuestDef/GetQuestId, ScriptedEscortAI/Start, ScriptMgr/DoScriptText, Unit.Main/SetFactionTemplateId | — | — |
| GOHello_go_gong | function | InstanceData/GetData, InstanceData/SetData, WorldObject.Object/GetInstanceData | — | — |
| AddSC_razorfen_downs | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
