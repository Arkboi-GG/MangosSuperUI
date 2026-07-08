<!-- provenance: verbose -->
# GameObjectAI

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`GameObjectAI` is the abstract base class defining the interface for Game Object Artificial Intelligence. It provides a polymorphic hook system allowing specific `GameObject` instances to exhibit scripted behaviors beyond static existence. While `GameObject` manages physical presence and state, `GameObjectAI` handles logic triggered by interaction, time, or external events. All virtual methods have empty default implementations, enabling derived classes to override only necessary behaviors.

## Member-by-Member Behavior

### Lifecycle & Updates

**`GameObjectAI`**
Constructor initializing the AI with a pointer to its parent `GameObject`, stored in the protected member `me`.

**`~GameObjectAI`**
Virtual destructor performing no cleanup.

**`OnRemoveFromWorld`**
Hook called by `GameObject::RemoveFromWorld` when the object is removed from the map. Allows derived classes to perform cleanup.

**`UpdateAI`**
Hook called periodically by `GameObject::Update`. Receives `uiDiff` (time elapsed in ms). Used for time-based logic like timers or state checks.

### Interactions

**`OnUse`**
Hook called by `GameObject::Use`, `GameObject::Update`, `Spell.Effects::EffectOpenLock`, and `WorldSession.LootHandler::DoLootRelease` when a unit interacts with the object. Returns `false` by default; derived classes return `true` if they handle the interaction.

**`OnActivateBySpell`**
Hook called by `Spell.Effects::EffectActivateObject` when a spell targets the object. Receives `SpellCaster`, `spellId`, and `action`. Returns `false` by default.

**`SetData`**
Generic interface for passing data to the AI via integer `id` and `value`. Used by external scripts to change internal state.

### Summoning

**`JustSummoned`**
Hook called by `WorldObject.Object::SummonCreature#2` when the `GameObject` successfully summons a `Creature`.

**`JustSummoned#2`**
Hook called by `Spell.Effects::EffectSummonObjectWild` and `WorldObject.Object::SummonGameObject` when the `GameObject` summons another `GameObject`.

**`SummonedCreatureJustDied`**
Hook called by `Unit.Main::Kill` when a creature summoned by this `GameObject` dies.

**`SummonedMovementInform`**
Hook called by `PointMovementGenerator::MovementInform#3`, `TargetedMovementGenerator::MovementInform`, and `TargetedMovementGenerator::MovementInform#2` when a summoned creature reaches a waypoint. Receives the `Creature`, `motion_type`, and `point_id`.

## Cross-Unit Boundaries

`GameObjectAI` is a passive interface called by the core engine and script modules.

*   **`GameObject`**: Calls `OnRemoveFromWorld`, `UpdateAI`, `OnUse`, and the `JustSummoned` overloads.
*   **`Spell.Effects`**: `EffectOpenLock` calls `OnUse`; `EffectActivateObject` calls `OnActivateBySpell`; `EffectSummonObjectWild` calls `JustSummoned(GameObject*)`.
*   **`Unit.Main`**: `Kill` calls `SummonedCreatureJustDied`.
*   **`WorldSession.LootHandler`**: `DoLootRelease` calls `OnUse`.
*   **`MovementGenerators`**: `PointMovementGenerator` and `TargetedMovementGenerator` call `SummonedMovementInform`.
*   **Derived Scripts**: Numerous zone-specific AIs (e.g., `go_arathi_cannon_fireAI`, `boss_herod/go_herod_leverAI`) inherit from `GameObjectAI` to implement specific behaviors.

## Data Model

This unit does not access any database tables. It operates entirely on in-memory objects and runtime state.

## Notable Implementation Details

1.  **Empty Defaults**: All virtual methods are empty, minimizing boilerplate for derived classes.
2.  **Protected `me`**: The `GameObject* me` pointer is protected, accessible only to derived classes.
3.  **No Ownership**: `GameObjectAI` does not own `me`; it assumes the `GameObject` outlives the AI.
4.  **Boolean Returns**: `OnUse` and `OnActivateBySpell` return `bool` to indicate if the interaction was handled, preventing further processing.
5.  **Overloaded Summons**: Two `JustSummoned` methods distinguish between summoned `Creature` and `GameObject` types.

## Member Reference

**`GameObjectAI`**
Constructor binding the AI to a `GameObject` instance, initializing the protected `me` pointer.

**`~GameObjectAI`**
Virtual destructor performing no cleanup.

**`OnRemoveFromWorld`**
Hook called when the `GameObject` is removed from the world. Default implementation is empty.

**`UpdateAI`**
Hook called periodically during the game loop. Receives the time difference in milliseconds. Default implementation is empty.

**`SetData`**
Generic interface for setting internal state via integer ID and value. Default implementation is empty.

**`OnUse`**
Hook called when a unit interacts with the `GameObject`. Returns `false` by default.

**`OnActivateBySpell`**
Hook called when a spell activates the `GameObject`. Returns `false` by default.

**`JustSummoned`**
Hook called when a `Creature` is successfully summoned by the `GameObject`. Default implementation is empty.

**`JustSummoned#2`**
Hook called when a `GameObject` is successfully summoned by the `GameObject`. Default implementation is empty.

**`SummonedCreatureJustDied`**
Hook called when a summoned `Creature` dies. Default implementation is empty.

**`SummonedMovementInform`**
Hook called when a summoned `Creature` reaches a movement point. Default implementation is empty.

---

<!-- machine-true, projected from graph.json -->

## Map — GameObjectAI

*Source:* GameObjectAI.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GameObjectAI | ctor | — | arathi_highlands/go_arathi_cannon_fireAI, ashenvale/go_foulweald_totem_moundAI, azshara/go_bay_of_stormsAI, blackrock_depths/go_cell_doorAI, boss_anubrekhan/anub_doorAI, boss_dragon_of_nightmare/go_putrid_shroomAI, boss_herod/go_herod_leverAI, boss_ossirian/ossirian_crystalAI, boss_ouro/go_sandworm_baseAI, boss_sapphiron/sapphiron_birthAI, boss_urok/go_urok_challengeAI, deadmines/go_defias_gunpowderAI, desolace/go_ghost_magnetAI, dreadsteed_ritual/go_pedestal_of_immol_tharAI, dreadsteed_ritual/go_ritual_nodeAI, dustwallow_marsh/go_forged_sealAI, dustwallow_marsh/go_unforged_sealAI, eastern_plaguelands/go_darrowshire_triggerAI, elemental_invasions/elemental_invasion_riftAI, felwood/go_corrupted_plantAI, fireworks_show/go_cheer_speakerAI, go_scripts/go_bells, go_scripts/go_containment_coffer, go_scripts/go_darkmoon_faire_music, go_scripts/go_firework_rocket, go_scripts/go_lunar_festival_firecracker, hillsbrad_foothills/go_dusty_rugAI, hillsbrad_foothills/go_helcular_s_graveAI, hinterlands/go_lards_picnic_basketAI, instance_blackfathom_deeps/go_fire_of_akumaiAI, instance_blackrock_spire/go_father_flameAI, instance_blackwing_lair/go_egg_razAI, instance_blackwing_lair/go_engin_suppressionAI, instance_dire_maul/go_fixed_trap, instance_dire_maul/go_warpwood_pod, instance_scholomance/go_viewing_room_door, scourge_invasion/GoCircle, scourge_invasion/GoNecropolis, silithus/go_wind_stoneAI, silithus/scarab_gongAI, stranglethorn_vale/go_transpolyporterAI, stratholme/go_supply_crateAI, tanaris/go_inconspicuous_landmarkAI, ThreatListCopier.battleground_alterac/AV_BeaconInvocationObjectAI, ThreatListCopier.battleground_alterac/go_av_landmineAI | — |
| ~GameObjectAI | dtor | — | — | — |
| OnRemoveFromWorld | method | — | GameObject/RemoveFromWorld | — |
| UpdateAI | method | — | GameObject/Update | — |
| SetData | method | — | — | — |
| OnUse | method | — | GameObject/Update, GameObject/Use, Spell.Effects/EffectOpenLock, WorldSession.LootHandler/DoLootRelease | — |
| OnActivateBySpell | method | — | Spell.Effects/EffectActivateObject | — |
| JustSummoned | method | — | WorldObject.Object/SummonCreature#2 | — |
| JustSummoned#2 | method | — | Spell.Effects/EffectSummonObjectWild, WorldObject.Object/SummonGameObject | — |
| SummonedCreatureJustDied | method | — | Unit.Main/Kill | — |
| SummonedMovementInform | method | — | PointMovementGenerator/MovementInform#3, TargetedMovementGenerator/MovementInform, TargetedMovementGenerator/MovementInform#2 | — |
