<!-- provenance: verbose -->
# GridSearchers

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# GridSearchers

**GridSearchers** provides utility functions for querying the spatial grid to locate `GameObject` and `Creature` instances near a source object. It abstracts the core MaNGOS grid traversal (`Cell::VisitGridObjects`) into simple APIs for finding the nearest single object or collecting lists of objects within a radius. The unit supports searches by a single entry ID or a vector of multiple entry IDs.

## Member-by-Member Behavior

### Nearest Object Retrieval

**GetClosestGameObjectWithEntry**
Finds the nearest `GameObject` with entry `uiEntry` within `fMaxSearchRange` of `pSource`. It constructs a `MaNGOS::NearestGameObjectEntryInObjectRangeCheck` and a `MaNGOS::GameObjectLastSearcher`, then invokes `Cell::VisitGridObjects`. Returns `nullptr` if no match is found.

**GetClosestCreatureWithEntry**
Finds the nearest **alive** `Creature` with entry `uiEntry` within `fMaxSearchRange` of `pSource`. It uses `MaNGOS::NearestCreatureEntryWithLiveStateInObjectRangeCheck` with the `true` flag to enforce the alive state, paired with a `MaNGOS::CreatureLastSearcher`. Returns `nullptr` if no match is found.

### List Population

**GetGameObjectListWithEntryInGrid#2**
Populates `lList` with all `GameObject`s matching a single entry `uiEntry` within range. It uses `MaNGOS::AllGameObjectsWithEntryInRange` and `MaNGOS::GameObjectListSearcher`.

**GetGameObjectListWithEntryInGrid**
Populates `lList` with all `GameObject`s matching any entry in the `entries` vector within range. It uses `MaNGOS::AllGameObjectsMatchingOneEntryInRange` and `MaNGOS::GameObjectListSearcher`.

**GetCreatureListWithEntryInGrid#2**
Populates `lList` with all `Creature`s matching a single entry `uiEntry` within range. It uses `MaNGOS::AllCreaturesOfEntryInRange` and `MaNGOS::CreatureListSearcher`. Unlike the "Closest" variant, this does not explicitly filter for alive creatures in the checker name.

**GetCreatureListWithEntryInGrid**
Populates `lList` with all `Creature`s matching any entry in the `entries` vector within range. It uses `MaNGOS::AllCreaturesMatchingOneEntryInRange` and `MaNGOS::CreatureListSearcher`.

## Cross-Unit Boundaries

### Outgoing Calls
*   **Errors/PrintStacktraceAndThrow**: Referenced in the MAP; likely triggered by assertion failures or core errors during grid traversal.
*   **MaNGOS Core Checkers**: `NearestGameObjectEntryInObjectRangeCheck`, `NearestCreatureEntryWithLiveStateInObjectRangeCheck`, `AllGameObjectsWithEntryInRange`, `AllGameObjectsMatchingOneEntryInRange`, `AllCreaturesOfEntryInRange`, `AllCreaturesMatchingOneEntryInRange`.
*   **MaNGOS Core Searchers**: `GameObjectLastSearcher`, `CreatureLastSearcher`, `GameObjectListSearcher`, `CreatureListSearcher`.
*   **Cell::VisitGridObjects**: The core engine function that iterates over spatial grid cells.

### Incoming Calls
Called by numerous boss scripts (e.g., `boss_kurinnaxx`, `boss_sapphiron`, `boss_cthun`), instance scripts (e.g., `instance_naxxramas.Main`, `instance_molten_core`), world events (e.g., `scourge_invasion`, `silithus`), and battlegrounds (`ThreatListCopier.battleground_alterac`).

## Data Model

This unit performs runtime spatial queries on in-memory game objects. It does not interact with any database tables.

## Notable Implementation Details

*   **Alive State Filtering**: `GetClosestCreatureWithEntry` explicitly filters for alive creatures. The list variants (`GetCreatureListWithEntryInGrid`) do not explicitly filter for alive state in their checker names, meaning they may return dead or inactive creatures. Scripts requiring alive targets from lists must verify state manually.
*   **LastSearcher Semantics**: The "Closest" functions use `...LastSearcher` templates. The grid visitor processes objects, and the searcher retains the final valid object. Combined with `Nearest...Check` logic, this yields the closest object.
*   **No Sorting for Lists**: List functions do not sort results by distance. Clients must use `ObjectDistanceOrder` (defined in `GridSearchers.h`) to sort if needed.
*   **Assertions**: All functions assert `pSource` is non-null.

## Member Reference

**GetClosestGameObjectWithEntry**
Returns the nearest `GameObject` with the specified entry within range. Uses `NearestGameObjectEntryInObjectRangeCheck` and `GameObjectLastSearcher`.

**GetClosestCreatureWithEntry**
Returns the nearest alive `Creature` with the specified entry within range. Uses `NearestCreatureEntryWithLiveStateInObjectRangeCheck` (alive=true) and `CreatureLastSearcher`.

**GetGameObjectListWithEntryInGrid#2**
Populates a list with all `GameObject`s matching a single entry within range. Uses `AllGameObjectsWithEntryInRange`.

**GetGameObjectListWithEntryInGrid**
Populates a list with all `GameObject`s matching any entry in a vector within range. Uses `AllGameObjectsMatchingOneEntryInRange`.

**GetCreatureListWithEntryInGrid#2**
Populates a list with all `Creature`s matching a single entry within range. Uses `AllCreaturesOfEntryInRange`. Does not explicitly filter for alive state.

**GetCreatureListWithEntryInGrid**
Populates a list with all `Creature`s matching any entry in a vector within range. Uses `AllCreaturesMatchingOneEntryInRange`. Does not explicitly filter for alive state.

---

<!-- machine-true, projected from graph.json -->

## Map — GridSearchers

*Source:* GridSearchers.cpp, GridSearchers.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GetClosestGameObjectWithEntry | function | Errors/PrintStacktraceAndThrow, NearestGameObjectEntryInObjectRangeCheck/NearestGameObjectEntryInObjectRangeCheck | boss_kurinnaxx/UpdateAI, boss_vectus/UpdateAI, darkshore/BeginEvent, desolace/JustStartedEscort#2, felwood/QuestAccept_npc_captured_arkonarin, felwood/WaypointReached#2, feralas/BeginEvent, hinterlands/QuestAccept_npc_rinji, instance_maraudon/Update, instance_sunken_temple/ProcessStatueUsed, npcs_special/UpdateAI#10, razorfen_downs/UpdateEscortAI, silithus/BeginAQOpeningEvent, silithus/SetupAQGate, swamp_of_sorrows/WaypointStart, western_plaguelands/MoveInLineOfSight | — |
| GetClosestCreatureWithEntry | function | Errors/PrintStacktraceAndThrow, NearestCreatureEntryWithLiveStateInObjectRangeCheck/NearestCreatureEntryWithLiveStateInObjectRangeCheck | arathi_highlands/WaypointReached, blackrock_depths/OnUse, boss_four_horsemen/Reset, boss_ossirian/OnUse, darkshore/at_murloc_camp, dustwallow_marsh/AreaTrigger_at_sentry_point, felwood/AreaTrigger_at_irontree_wood, feralas/Aggro, feralas/Aggro#2, feralas/Aggro#3, feralas/JustDied#2, instance_scarlet_monastery/Update, instance_wailing_caverns/SetData, loch_modan/AreaTrigger_at_huldar_miran, moonglade/EnterEvadeMode, quest_stormwind_rendezvous/AreaTrigger_at_stormwind_gates, searing_gorge/GossipHello_npc_dying_archaeologist, searing_gorge/QuestAccept_npc_dying_archaeologist, silithus/BeginAQOpeningEvent, silithus/JustSummoned, silithus/QuestAcceptGO_crystalline_tear, silithus/UpdateAI, silithus/UpdateAI#7, sunken_temple/AreaTrigger_at_shade_of_eranikus, the_barrens/AreaTrigger_at_twiggy_flathead, ungoro_crater/AreaTrigger_at_scent_larkorwi, ungoro_crater/Reset#6, ungoro_crater/Transform, wetlands/GossipHello_npc_mikhail, wetlands/JustRespawned, wetlands/QuestAccept_npc_mikhail, world_event_wareffort/FollowSaurfang, world_event_wareffort/SetRespawnNearSaurfang | — |
| GetGameObjectListWithEntryInGrid#2 | function | AllGameObjectsWithEntryInRange/AllGameObjectsWithEntryInRange, Errors/PrintStacktraceAndThrow | boss_celebras_the_cursed/WaypointReached, boss_marli/Reset, boss_marli/SelectNextEgg, boss_sapphiron/DeleteAndDispellIceBlocks, razorfen_kraul/DoFindNewTuber, scourge_invasion/SummonCultists, ThreatListCopier.battleground_alterac/AV_NpcEventWorldBoss_A_AI, ThreatListCopier.battleground_alterac/AV_NpcEventWorldBoss_H_AI | — |
| GetGameObjectListWithEntryInGrid | function | AllGameObjectsMatchingOneEntryInRange/AllGameObjectsMatchingOneEntryInRange, Errors/PrintStacktraceAndThrow | scourge_invasion/DespawnEventDoodads, scourge_invasion/DespawnNecropolis | — |
| GetCreatureListWithEntryInGrid#2 | function | AllCreaturesOfEntryInRange/AllCreaturesOfEntryInRange, Errors/PrintStacktraceAndThrow | blackrock_depths/Aggro#4, boss_anubrekhan/Reset, boss_ayamiss/Reset, boss_ayamiss/UpdateAI, boss_broodlord_lashlayer/SetMobsDesactivated, boss_cannon_master_willey/EnterEvadeMode, boss_celebras_the_cursed/GOHello_go_book_celebras, boss_garr/Aggro, boss_gluth/DespawnAllZombiess, boss_gluth/DoSearchZombieChow, boss_golemagg/Aggro, boss_golemagg/Reset, boss_maexxna/JustReachedHome, boss_marli/Reset, boss_nefarian/SetAura, boss_onyxia/Aggro#2, boss_onyxia/EnterEvadeMode, boss_razuvious/UpdateRP, boss_sartura/DamageTaken, boss_tendris_warpwood/Aggro, boss_thermaplugg/UpdateAI, boss_twinemperors/Aggro, boss_vaelastrasz/SetAuraFlames, boss_venoxis/EnterEvadeMode, boss_venoxis/JustReachedHome, dustwallow_marsh/UpdateAI#3, feralas/BeginEvent, instance_blackrock_depths/SetData, instance_deadmines/Update, instance_dire_maul/UpdateFormationSpeed, instance_molten_core/OnCreatureCreate, instance_molten_core/OnCreatureEnterCombat, instance_naxxramas.boss_kelthuzad/SpellHit#2, instance_naxxramas.Main/OnCreatureCreate, instance_naxxramas.Main/OnCreatureEnterCombat, instance_ruins_of_ahnqiraj/GiveRepAfterRajaxxDeath, instance_scarlet_monastery/SetData, instance_shadowfang_keep/Update, instance_sunken_temple/SetData, Map.ScriptCommands/ScriptCommand_SummonCreature, mob_anubisath_sentinel/AddSentinelsNear, molten_core/UpdateAI#3, quest_stormwind_rendezvous/UpdateAI, scourge_invasion/DespawnCultists, scourge_invasion/DespawnEventDoodads, scourge_invasion/DespawnShadowsOfDoom, scourge_invasion/GetFindersAmount, scourge_invasion/UpdateAI#7, stratholme/UpdateAI#3, ThreatListCopier.battleground_alterac/AggroLinkedMobsIfNeeded, ThreatListCopier.battleground_alterac/AV_NpcEventWorldBoss_A_AI, ThreatListCopier.battleground_alterac/AV_NpcEventWorldBoss_H_AI, ThreatListCopier.battleground_alterac/JustDied, ThreatListCopier.battleground_alterac/JustDied#2, ThreatListCopier.battleground_alterac/JustRespawned, ThreatListCopier.battleground_alterac/Reset#17, ThreatListCopier.battleground_alterac/UpdateEscortAI, ThreatListCopier.battleground_alterac/UpdateEscortAI#2, ThreatListCopier.battleground_alterac/UpdateEscortAI#5, ThreatListCopier.battleground_alterac/WaypointReached, ThreatListCopier.boss_ragnaros/UpdateAI, uldaman/UpdateAI | — |
| GetCreatureListWithEntryInGrid | function | AllCreaturesMatchingOneEntryInRange/AllCreaturesMatchingOneEntryInRange, Errors/PrintStacktraceAndThrow | boss_cthun/DespawnAllTentacles, boss_gothik/OpenTheGate, boss_gothik/Reset, boss_noth/JustReachedHome, boss_razorgore/EvadeTroops, boss_razorgore/PhaseSwitch, boss_razorgore/PopAdd, boss_razorgore/SituationInitiale, boss_razorgore/UpdateAI#2, boss_twinemperors/HandleBugSpell, eastern_plaguelands/DespawnAll#2, eastern_plaguelands/SetAttackOnPeasantOrPlayer, instance_naxxramas.Main/SetData, scourge_invasion/HasMinion, scourge_invasion/NecroticShard, scourge_invasion/UncommonMinionspawner | — |
