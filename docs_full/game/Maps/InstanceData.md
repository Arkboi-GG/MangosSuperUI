<!-- provenance: verbose -->
# InstanceData

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# InstanceData

**InstanceData** is the abstract base class for instance-specific state management scripts. Inheriting from `ZoneScript`, it defines the interface for serializing instance progress (boss kills, event states) to the database and exposing that state to creature AI, game objects, and area triggers via standardized getters and setters. Most members are virtual stubs intended for override by derived classes (e.g., `instance_blackrock_depths`). The base class implements only the database persistence logic (`SaveToDB`) and a fallback logger for unimplemented condition checks.

## Member-by-Member Behavior

### Persistence
*   **SaveToDB**: The sole concrete persistence method. It skips Battle Grounds (`Map.Main/IsBattleGround`). It calls the virtual `Save()` twice: first to check if the returned pointer is null (skipping the save if so), then to retrieve the string for escaping and insertion. It determines the target table using `Map.Main/Instanceable`:
    *   If true, it updates the `instance` table using `Map.Main/GetInstanceId`.
    *   If false, it updates the `world` table using `Map.Main/GetId`.
    *   It uses `Database/escape_string` to sanitize the data and `Database/PExecute` to run the `UPDATE` query.
*   **Save**: Virtual method returning a `char const*` representing serialized state. Defaults to `""` (empty string). Called by `SaveToDB` and `MapPersistentStateMgr/SaveToDB`.

### Lifecycle Hooks
*   **Initialize**, **Load**, **Create**: Virtual stubs called by `Map.Main/CreateInstanceData` during instance setup. `Initialize` runs on creation, `Load` on restoration from DB, and `Create` on new instance generation. All are empty in the base class.
*   **Update**: Virtual stub called periodically by `Map.Main/Update#3` and `ScriptedInstance/Update`. Empty in the base class.

### Data Access API
Derived classes override these to store instance state. The base implementations return 0 or do nothing.
*   **GetData** / **SetData**: 32-bit integer storage. Used extensively by boss scripts, GOs, and area triggers to track phases, deaths, and flags.
*   **GetData64** / **SetData64**: 64-bit integer storage. Often used for `ObjectGuid`s.
*   **GetGuid** / **SetGuid**: Non-virtual wrappers around `GetData64`/`SetData64`. `GetGuid` casts the 64-bit value to `ObjectGuid`; `SetGuid` extracts the raw value from an `ObjectGuid` and calls `SetData64`.

### Game Logic Hooks
*   **IsEncounterInProgress**: Returns `false` by default. Overridden to return `true` during boss fights. Called by `Map.Main/CanEnter#2` to block entry.
*   **CustomSpellCasted**: Empty virtual hook called by `Spell.Effects/EffectDummy` to react to specific spells.
*   **CheckConditionCriteriaMeet**: Called by `Conditions/Evaluate` for complex instance conditions. The base implementation logs an error via `Log.Main/Out` (indicating a missing implementation in the derived class) and returns `false`.

## Cross-Unit Boundaries

*   **Calls Out**:
    *   **Map.Main**: Uses `IsBattleGround`, `Instanceable`, `GetInstanceId`, and `GetId` in `SaveToDB` to determine persistence strategy. Uses `GetId` in `CheckConditionCriteriaMeet` for logging.
    *   **Database**: Uses `escape_string` and `PExecute` in `SaveToDB` for safe SQL execution.
    *   **Log.Main**: Uses `Out` in `CheckConditionCriteriaMeet` to report missing condition implementations.
*   **Called By**:
    *   **Instance Scripts**: Hundreds of derived scripts (bosses, GOs, escorts) call `GetData`, `SetData`, `GetData64`, and `SetData64` to synchronize state.
    *   **Map.Main**: Calls lifecycle methods (`Initialize`, `Load`, `Create`), `Update`, `IsEncounterInProgress`, and `SaveToDB` (on crash unload).
    *   **ChatHandler**: Calls `SaveToDB`, `GetData`, and `SetData` for admin commands.
    *   **Spell.Effects**: Calls `CustomSpellCasted` and `SetData`.
    *   **Conditions**: Calls `CheckConditionCriteriaMeet`.

## Data Model

*   **`instance`**: Stores state for instanceable maps. Keyed by `id` (unsigned int PK). The `data` column (longtext) holds the serialized string.
*   **`world`**: Stores state for non-instanceable maps. Keyed by `map` (unsigned int PK). The `data` column (longtext) holds the serialized string.

## Notable Implementation Details

*   **Double Call to Save**: `SaveToDB` calls `Save()` twice: once to check for a null pointer, and again to retrieve the string. Since the default implementation returns `""` (non-null), the check passes, and the empty string is escaped and saved to the database. This results in unnecessary DB writes for instances that do not override `Save()` or return `nullptr`.
*   **Silent Failures in GUID Wrappers**: `SetGuid` relies on `SetData64`. If a derived class fails to implement `SetData64`, `SetGuid` will silently discard the GUID without error.
*   **Condition Logging**: `CheckConditionCriteriaMeet` does not fail silently; it logs an error to help developers identify missing condition logic in derived classes.

## Member Reference

**SaveToDB**: Persists instance state to the `instance` or `world` table. Skips Battle Grounds. Calls `Save()` twice (null check then retrieval), escapes the string, and executes an `UPDATE` query. Called by `ChatHandler.MiscCommands/HandleInstanceSaveDataCommand`, various instance scripts, and `Map.Main/CrashUnload`.

**InstanceData**: Constructor. Initializes the `instance` pointer and calls `SetMap`.

**~InstanceData**: Destructor. Empty.

**CheckConditionCriteriaMeet**: Logs an error if called (indicating missing implementation) and returns `false`. Called by `Conditions/Evaluate`.

**Initialize**: Virtual lifecycle hook. Empty. Called by `Map.Main/CreateInstanceData`.

**Load**: Virtual lifecycle hook. Empty. Called by `Map.Main/CreateInstanceData`.

**Create**: Virtual lifecycle hook. Empty. Called by `Map.Main/CreateInstanceData`.

**Save**: Virtual method returning serialized state string. Defaults to `""`. Called by `SaveToDB` and `MapPersistentStateMgr/SaveToDB`.

**Update**: Virtual periodic update hook. Empty. Called by `Map.Main/Update#3` and `ScriptedInstance/Update`.

**IsEncounterInProgress**: Returns `false` by default. Used to block entry during encounters. Called by `Map.Main/CanEnter#2`.

**CustomSpellCasted**: Virtual hook for spell effects. Empty. Called by `Spell.Effects/EffectDummy`.

**GetData64**: Retrieves 64-bit state. Defaults to 0. Called by many boss and GO scripts.

**SetData64**: Sets 64-bit state. Does nothing by default. Called by boss scripts and `Map.ScriptCommands/ScriptCommand_SetData64`.

**GetGuid**: Wrapper retrieving `ObjectGuid` from 64-bit storage. Called by scripts accessing stored GUIDs.

**SetGuid**: Wrapper storing `ObjectGuid` via 64-bit storage. Called by scripts storing GUIDs.

**GetData**: Retrieves 32-bit state. Defaults to 0. Called by many boss, GO, and area trigger scripts.

**SetData**: Sets 32-bit state. Does nothing by default. Called by many boss, GO, area trigger, and chat handler scripts.

---

<!-- machine-true, projected from graph.json -->

## Map — InstanceData

*Source:* InstanceData.cpp, InstanceData.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| SaveToDB | method | Database/escape_string, Database/PExecute#2, Map.Main/GetId, Map.Main/GetInstanceId, Map.Main/Instanceable, Map.Main/IsBattleGround | ChatHandler.MiscCommands/HandleInstanceSaveDataCommand, instance_blackfathom_deeps/SetData, instance_blackrock_depths/SetData, instance_blackrock_spire/SetData, instance_blackwing_lair/SetData, instance_dire_maul/SetData, instance_gnomeregan/SetData, instance_maraudon/SetData, instance_molten_core/SetData, instance_naxxramas.Main/SetData, instance_razorfen_downs/SetData, instance_razorfen_kraul/SetData, instance_ruins_of_ahnqiraj/SetData, instance_scarlet_monastery/SetData, instance_scholomance/SetData, instance_shadowfang_keep/SetData, instance_stratholme/SetData, instance_sunken_temple/SetData, instance_temple_of_ahnqiraj/SetData, instance_uldaman/SetData, instance_wailing_caverns/SetData, instance_zulfarrak/SetData, instance_zulgurub/Create, instance_zulgurub/SetData, Map.Main/CrashUnload | instance, world |
| InstanceData | ctor | — | — | — |
| ~InstanceData | dtor | — | — | — |
| CheckConditionCriteriaMeet | method | Log.Main/Out, Map.Main/GetId | Conditions/Evaluate | — |
| Initialize | method | — | Map.Main/CreateInstanceData | — |
| Load | method | — | Map.Main/CreateInstanceData | — |
| Create | method | — | Map.Main/CreateInstanceData | — |
| Save | method | — | MapPersistentStateMgr/SaveToDB | — |
| Update | method | — | Map.Main/Update#3, ScriptedInstance/Update | — |
| IsEncounterInProgress | method | — | Map.Main/CanEnter#2 | — |
| CustomSpellCasted | method | — | Spell.Effects/EffectDummy | — |
| GetData64 | method | — | blackrock_depths/AreaTrigger_at_ring_of_law, blackrock_depths/DoGate, blackrock_depths/DoPotionOfLoveIfCan, blackrock_depths/GOUse_go_bar_ale_mug, blackrock_depths/JustDied, blackrock_depths/Reset#9, blackrock_depths/SummonRingBoss, blackrock_depths/UpdateEscortAI#2, blackrock_depths/UpdateEscortAI#4, blackrock_depths/WaypointReached#4, blackrock_depths/WaypointReached#5, boss_archaedas/EnterEvadeMode#2, boss_archaedas/UpdateAI, boss_archaedas/UpdateAI#2, boss_buru/DamageTaken, boss_chromaggus/boss_chromaggusAI, boss_chromaggus/Reset, boss_chromaggus/UpdateAI, boss_emperor_dagran_thaurissan/JustDied, boss_emperor_dagran_thaurissan/UpdateAI#2, boss_golemagg/UpdateEvents#2, boss_interrogator_vishas/JustDied, boss_ironaya/UpdateAI, boss_jindo/UpdateAI#2, boss_mandokir/KilledUnit, boss_nefarian/EnterEvadeMode, boss_razorgore/EnterCombat, boss_razorgore/JustDied, boss_razorgore/PhaseSwitch, boss_razorgore/PopAdd, boss_razorgore/SituationInitiale, boss_razorgore/UpdateAI, boss_razorgore/UpdateAI#2, boss_tomb_of_seven/GetDwarfForPhase, boss_vectus/JustDied, boss_victor_nefarius/boss_victor_nefariusAI, boss_victor_nefarius/LoadScepterRun, boss_victor_nefarius/SummonedCreatureJustDied, deadmines/GOHello_go_door_lever_dm, instance_blackrock_spire/OnUse, instance_blackwing_lair/GOHello_go_orb_of_domination, instance_blackwing_lair/OnUse, instance_scarlet_monastery/AreaTrigger_at_cathedral_entrance, instance_zulgurub/ProcessEventId_event_summon_gahzranka, ScriptMgr/GetTargetByType, wailing_caverns/MovementInform, wailing_caverns/UpdateEscortAI, wailing_caverns/WaypointReached, zulfarrak/initBlyCrewMember, zulfarrak/MovementInform, zulfarrak/switchFactionIfAlive, zulfarrak/UpdateAI, zulfarrak/UpdateAI#2 | — |
| SetData64 | method | — | boss_archaedas/JoinCombat, Map.ScriptCommands/ScriptCommand_SetData64, uldaman/GOHello_go_keystone_chamber | — |
| GetGuid | method | — | — | — |
| SetGuid | method | — | — | — |
| GetData | method | — | blackrock_depths/Activate, blackrock_depths/AreaTrigger_at_ring_of_law, blackrock_depths/AreaTrigger_at_shadowforge_bridge, blackrock_depths/GOHello_go_dark_keeper_portrait, blackrock_depths/GOHello_go_relic_coffer_door, blackrock_depths/GOHello_go_shadowforge_brazier, blackrock_depths/GOHello_go_thunderbrew_laguer_keg, blackrock_depths/GOUse_go_bar_ale_mug, blackrock_depths/QuestAccept_npc_marshal_windsor, blackrock_depths/QuestRewarded_npc_rocknot, blackrock_depths/SummonRingBoss, blackrock_depths/UpdateAI#2, blackrock_depths/UpdateEscortAI, blackrock_depths/UpdateEscortAI#2, blackrock_depths/UpdateEscortAI#3, blackrock_depths/UpdateEscortAI#4, blackrock_depths/UpdateEscortAI#5, blackrock_depths/WaypointReached, blackrock_depths/WaypointReached#2, blackrock_depths/WaypointReached#3, blackrock_depths/WaypointReached#4, blackrock_depths/WaypointReached#5, blackrock_depths/WaypointReached#6, boss_archaedas/UpdateAI, boss_archaedas/UpdateAI#2, boss_arlokk/GOHello_go_gong_of_bethekk, boss_arlokk/JustReachedHome, boss_bug_trio/JustDied, boss_gahzranka/CheckSpawnStatus, boss_gahzranka/Reset, boss_garr/Aggro, boss_garr/Reset, boss_golemagg/DamageTaken#2, boss_hakkar/UpdateAI, boss_jeklik/UpdateAI#2, boss_majordomo_executus/Aggro, boss_majordomo_executus/Reset, boss_majordomo_executus/SummonedCreatureJustDied, boss_mandokir/CheckRaptor, boss_marli/Aggro, boss_marli/Reset, boss_order_of_silver_hand/JustDied, boss_razorgore/SituationInitiale, boss_razorgore/UpdateAI#2, boss_skeram/Aggro, boss_skeram/UpdateAI, boss_tomb_of_seven/UpdateAI, boss_vaelastrasz/BeginSpeech, boss_vaelastrasz/boss_vaelAI, boss_vaelastrasz/GossipHello_boss_vael, boss_vaelastrasz/QuestAccept_vaelastrasz, boss_vaelastrasz/UpdateAI, boss_victor_nefarius/LoadScepterRun, boss_victor_nefarius/UpdateAI, ChatHandler.MiscCommands/HandleInstanceGetDataCommand, ChatHandler.MiscCommands/HandleInstanceSetDataCommand, Conditions/Evaluate, deadmines/GOHello_go_defias_cannon, deadmines/GOHello_go_defias_gunpowder, instance_blackfathom_deeps/OnUse, instance_blackrock_spire/OnUse, instance_blackwing_lair/AreaTrigger_at_enter_vael_room, instance_blackwing_lair/RestoreGo, instance_dire_maul/UpdateAI#2, instance_molten_core/GOHello_go_rune_MC, instance_molten_core/UpdateRune, instance_scarlet_monastery/AreaTrigger_at_cathedral_entrance, instance_scholomance/GOOpen_brazier_herald, instance_zulgurub/OnGossipHello_go_table_madness, instance_zulgurub/ProcessEventId_event_summon_gahzranka, Map.ScriptCommands/ScriptCommand_SetData, molten_core/JustDied, molten_core/JustDied#2, razorfen_downs/GOHello_go_gong, ruins_of_ahnqiraj/UpdateAI#5, stratholme/GOHello_go_gauntlet_gate, stratholme/GOOpen_go_stratholme_postbox, stratholme/JustDied#2, wailing_caverns/OnScriptEventHappened, wailing_caverns/UpdateEscortAI, zulfarrak/MovementInform, zulfarrak/OnGossipHello_npc_sergeant_bly, zulfarrak/OnGossipHello_npc_weegli_blastfuse, zulfarrak/OnTrigger_at_antusul, zulfarrak/UpdateAI#2 | — |
| SetData | method | — | blackrock_depths/Activate, blackrock_depths/Aggro#2, blackrock_depths/AreaTrigger_at_ring_of_law, blackrock_depths/AreaTrigger_at_shadowforge_bridge, blackrock_depths/CheckForWipe, blackrock_depths/GOHello_go_dark_keeper_portrait, blackrock_depths/GOHello_go_relic_coffer_door, blackrock_depths/GOHello_go_shadowforge_brazier, blackrock_depths/GOHello_go_thunderbrew_laguer_keg, blackrock_depths/GOUse_go_bar_ale_mug, blackrock_depths/JustDied, blackrock_depths/JustDied#2, blackrock_depths/JustDied#3, blackrock_depths/JustDied#4, blackrock_depths/JustDied#5, blackrock_depths/OnUse, blackrock_depths/QuestAccept_npc_marshal_windsor, blackrock_depths/QuestRewarded_npc_rocknot, blackrock_depths/UpdateAI#2, blackrock_depths/UpdateAI#5, blackrock_depths/UpdateEscortAI#2, blackrock_depths/UpdateEscortAI#3, blackrock_depths/UpdateEscortAI#4, blackrock_depths/WaypointReached, blackrock_depths/WaypointReached#2, blackrock_depths/WaypointReached#3, blackrock_depths/WaypointReached#4, blackrock_depths/WaypointReached#6, boss_archaedas/EnterEvadeMode, boss_archaedas/JustDied, boss_archaedas/JustReachedHome, boss_archaedas/ProcessEventId_event_awaken_archaedas, boss_archaedas/UpdateAI, boss_arlokk/GOHello_go_gong_of_bethekk, boss_arlokk/JustDied, boss_arlokk/JustReachedHome, boss_ayamiss/Aggro, boss_ayamiss/JustDied, boss_ayamiss/Reset, boss_baroness_anastari/JustDied, boss_baron_geddon/Aggro, boss_baron_geddon/JustDied, boss_baron_geddon/Reset, boss_broodlord_lashlayer/Aggro, boss_broodlord_lashlayer/JustDied, boss_broodlord_lashlayer/JustReachedHome, boss_bug_trio/Aggro, boss_bug_trio/JustDied, boss_bug_trio/JustReachedHome, boss_buru/EnterCombat, boss_buru/JustDied, boss_buru/Reset, boss_celebras_the_cursed/JustDied, boss_chromaggus/Aggro, boss_chromaggus/JustDied, boss_chromaggus/JustReachedHome, boss_doctor_theolen_krastinov/JustDied, boss_ebonroc/Aggro, boss_ebonroc/JustDied, boss_ebonroc/JustReachedHome, boss_fankriss/Aggro, boss_fankriss/JustDied, boss_fankriss/JustReachedHome, boss_firemaw/Aggro, boss_firemaw/JustDied, boss_firemaw/JustReachedHome, boss_flamegor/Aggro, boss_flamegor/JustDied, boss_flamegor/JustReachedHome, boss_gahzranka/Aggro, boss_gahzranka/JustDied, boss_gahzranka/Reset, boss_garr/Aggro, boss_garr/JustDied, boss_garr/Reset, boss_gehennas/Aggro, boss_gehennas/JustDied, boss_gehennas/Reset, boss_golemagg/Aggro, boss_golemagg/JustDied, boss_golemagg/Reset, boss_hakkar/Aggro, boss_hakkar/JustDied, boss_hakkar/Reset, boss_huhuran/Aggro, boss_huhuran/JustDied, boss_huhuran/JustReachedHome, boss_illucia_barov/JustDied, boss_instructor_malicia/JustDied, boss_jeklik/Aggro, boss_jeklik/JustDied, boss_jeklik/Reset, boss_jindo/Aggro, boss_jindo/JustDied, boss_jindo/Reset, boss_kurinnaxx/Aggro, boss_kurinnaxx/JustDied, boss_kurinnaxx/JustRespawned, boss_lord_alexei_barov/JustDied, boss_lorekeeper_polkelt/JustDied, boss_lucifron/Aggro, boss_lucifron/JustDied, boss_lucifron/Reset, boss_magmus/Aggro, boss_magmus/JustDied, boss_magmus/Reset, boss_majordomo_executus/Aggro, boss_majordomo_executus/Reset, boss_majordomo_executus/SummonedCreatureJustDied, boss_maleki_the_pallid/JustDied, boss_mandokir/JustDied, boss_mandokir/Reset, boss_marli/Aggro, boss_marli/JustDied, boss_marli/Reset, boss_moam/Aggro, boss_moam/JustDied, boss_moam/Reset, boss_nefarian/EnterEvadeMode, boss_nefarian/JustDied, boss_nerubenkan/JustDied, boss_onyxia/Aggro#2, boss_onyxia/JustDied, boss_onyxia/Reset#2, boss_order_of_silver_hand/JustDied, boss_order_of_silver_hand/Reset, boss_ouro/Aggro, boss_ouro/JustDied, boss_ouro/JustReachedHome, boss_ramstein_the_gorger/Aggro, boss_ramstein_the_gorger/JustDied, boss_ramstein_the_gorger/Reset, boss_razorgore/EnterCombat, boss_razorgore/JustDied, boss_razorgore/JustReachedHome, boss_razorgore/MortPhaseUn, boss_shazzrah/Aggro, boss_shazzrah/JustDied, boss_shazzrah/Reset, boss_skeram/Aggro, boss_skeram/JustDied, boss_skeram/JustReachedHome, boss_sulfuron_harbinger/Aggro, boss_sulfuron_harbinger/JustDied, boss_sulfuron_harbinger/Reset, boss_the_ravenian/JustDied, boss_tomb_of_seven/JustDied, boss_tomb_of_seven/JustReachedHome, boss_tomb_of_seven/UpdateAI, boss_vaelastrasz/Aggro, boss_vaelastrasz/BeginSpeech, boss_vaelastrasz/JustDied, boss_vaelastrasz/JustReachedHome, boss_vaelastrasz/QuestAccept_vaelastrasz, boss_vaelastrasz/UpdateAI, boss_venoxis/Aggro, boss_venoxis/JustDied, boss_venoxis/JustReachedHome, boss_victor_nefarius/Aggro, boss_victor_nefarius/FailScepterRun, boss_victor_nefarius/HandleScepterRun, boss_victor_nefarius/JustReachedHome, boss_victor_nefarius/StartScepterRun, boss_victor_nefarius/SummonedCreatureJustDied, boss_viscidus/Aggro, boss_viscidus/JustDied, boss_viscidus/JustReachedHome, ChatHandler.MiscCommands/HandleInstanceSetDataCommand, deadmines/GOHello_go_defias_cannon, deadmines/GOHello_go_defias_gunpowder, instance_blackfathom_deeps/OnUse, instance_blackrock_spire/OnUse, instance_blackwing_lair/AreaTrigger_at_enter_vael_room, instance_blackwing_lair/OnUse, instance_dire_maul/UpdateAI#2, instance_molten_core/GOHello_go_rune_MC, instance_molten_core/UpdateRune, instance_scarlet_monastery/AreaTrigger_at_cathedral_entrance, instance_scholomance/GOOpen_brazier_herald, instance_scholomance/OnUse, instance_zulgurub/ProcessEventId_event_summon_gahzranka, Map.ScriptCommands/ScriptCommand_SetData, razorfen_downs/GOHello_go_gong, razorfen_downs/UpdateEscortAI, ruins_of_ahnqiraj/Aggro#4, ruins_of_ahnqiraj/JustDied#2, ruins_of_ahnqiraj/Reset#5, Spell.Effects/EffectDummy, Spell.Effects/EffectScriptEffect, Spell.Effects/SendLoot, stratholme/GOHello_go_gauntlet_gate, stratholme/GOOpen_go_stratholme_postbox, stratholme/JustDied#2, ThreatListCopier.boss_ragnaros/Aggro, ThreatListCopier.boss_ragnaros/JustDied, ThreatListCopier.boss_ragnaros/Reset, uldaman/EnterEvadeMode, uldaman/GOHello_go_keystone_chamber, uldaman/JustDied, uldaman/ProcessEventId_event_awaken_stone_keeper, wailing_caverns/UpdateEscortAI, wailing_caverns/WaypointReached, zulfarrak/MovementInform, zulfarrak/OnGossipHello_go_troll_cage, zulfarrak/OnTrigger_at_antusul, zulfarrak/OnTrigger_at_zumrah | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `instance`: id int(11) unsigned PK, map int(11) unsigned, reset_time bigint(40), data longtext?
- `world`: map int(11) unsigned PK, data longtext?

*`?` = nullable, `PK` = primary key column.*

