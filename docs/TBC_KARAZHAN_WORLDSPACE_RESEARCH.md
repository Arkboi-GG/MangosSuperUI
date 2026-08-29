# TBC Worldspace Import Research: Karazhan Pilot

**Date:** 2026-08-28  
**Status:** Research and implementation plan; no server artifacts, databases, or runtime state were changed  
**Target:** SuperUI-Core / Vanilla 1.12.1 client  
**Source reference:** CMaNGOS TBC and TBC-DB for client 2.4.3 build 8606

## Decision

Karazhan is the right first TBC worldspace pilot, with one important correction:
its entrances are on Azeroth map 0, but the raid interior is a separate worldspace,
map 532. The pilot therefore does not avoid map import. It avoids the much larger
Outland continent problem and gives us a bounded way to prove:

- a TBC WDT/ADT/WMO world can load in the Vanilla client;
- client DBC rows and asset dependencies can be translated and merged safely;
- SuperUI-Core can load compatible map, collision, and navigation products;
- cMaNGOS spawns, movement, doors, and instance data can be translated to the
  target database schema; and
- one encounter can be ported from cMaNGOS APIs to SuperUI-Core APIs.

Karazhan is WMO-dominant but is represented by nine active world tiles in the
installed extraction. It is therefore easier than Outland, but it still exercises
the WDT/ADT tile path rather than being only a single model dropped onto map 0.

The first deliverable should be an **empty, walkable map 532**, followed by a
pathing pilot, static population, and finally Attumen/Midnight as the first combat
vertical slice. Attempting the complete raid in one step would combine client
format conversion, collision, navigation, database translation, spell translation,
and AI porting into one debugging problem.

## Evidence inspected

This study used:

- the current MangosSuperUI project and its existing TBC item-import, MPQ,
  world-editing, DBC, and server-data code;
- read-only inspection of the Linux cMaNGOS TBC, TBC-DB, extracted data, and
  SuperUI-Core trees;
- the pinned [CMaNGOS TBC source](https://github.com/cmangos/mangos-tbc/tree/4f2ce1815277cb1cadd0f980b249f0d105e732c9),
  [TBC-DB](https://github.com/cmangos/tbc-db/tree/c972214b37980388ad602700e76b4136fa9ae940),
  the dedicated
  [Karazhan database update](https://github.com/cmangos/tbc-db/blob/c972214b37980388ad602700e76b4136fa9ae940/Updates/Instances/532_karazhan.sql),
  and the upstream
  [Karazhan ScriptDevAI sources](https://github.com/cmangos/mangos-tbc/tree/4f2ce1815277cb1cadd0f980b249f0d105e732c9/src/game/AI/ScriptDevAI/scripts/eastern_kingdoms/karazhan);
- the upstream
  [extraction workflow](https://github.com/cmangos/mangos-tbc/blob/4f2ce1815277cb1cadd0f980b249f0d105e732c9/contrib/extractor_scripts/ExtractResources.sh),
  which explicitly performs DBC/map extraction, vmap extraction and assembly,
  and then movement-map generation; and
- the public [WoW client listfile](https://github.com/wowdev/wow-listfile) to
  cross-check archive paths. The TBC client MPQs remain the authoritative source
  for the actual dependency closure.

Pinned Linux reference revisions observed during the read-only inspection:

| Component | Path | Revision |
|---|---|---|
| cMaNGOS TBC | `/home/wowvmangos/cmangos/source` | `4f2ce1815277cb1cadd0f980b249f0d105e732c9` |
| TBC-DB | `/home/wowvmangos/cmangos/database` | `c972214b37980388ad602700e76b4136fa9ae940` |
| SuperUI-Core target | `/home/wowvmangos/vmangos` | `8d3d7969b76a16814b07f47d9e2659443462aafa` |

The revision hashes, source client build, and every generated artifact hash should
be recorded in the eventual content manifest. “Current master” is not a
reproducible source definition.

## What exists for map 532 on the Linux box

The installed cMaNGOS extraction provides a useful completeness oracle:

| Source artifact | Observed footprint |
|---|---:|
| `.map` terrain products | 9 tiles, 615,346 bytes total |
| vmap products | `532.vmtree` plus 9 `.vmtile` files, 3,218,315 bytes total |
| referenced collision models | 369 `.vmo` files, 10,155,692 bytes total; none missing |
| movement maps | `532.mmap` plus 9 `.mmtile` files, 24,746,804 bytes total |

These are **reference fixtures, not files we can copy into SuperUI**. They tell us
the expected tile count, dependency count, approximate footprint, and source
coordinates. They can also be used for geometry/pathing comparison.

The source `Map.dbc` row identifies:

- map ID `532`;
- internal directory `Karazahn` (retain this exact historical spelling);
- raid/instance type `2`;
- linked area `3457`;
- loading-screen ID `200`;
- corpse return map `0`;
- seven-day reset (`604800` seconds); and
- expansion value `1`.

Map ID 532 is part of the cross-layer contract and should not be remapped. Other
DBC and database IDs need collision checks because this is already a customized
Vanilla installation.

## The required artifact set

The feature is not one folder of map files. It is five coordinated products:

| Product | What it contains | Where it belongs |
|---|---|---|
| Core source | Instance and encounter C++, narrow compatibility helpers, static registration, manifest | `SuperUI-Core/src/game/TBCcontent/` |
| Database migration | Map metadata, translated templates/spawns/movement/scripts/conditions/loot | SuperUI-Core's normal SQL migration tree |
| Client patch | Vanilla-layout DBCs plus translated WDT/ADT/WMO/M2/BLP and optional UI/audio | Deterministic MangosSuperUI build output |
| Server data | Target-format `dbc`, `maps`, `vmaps`, `mmaps`, and model lookup products | Staging artifact generated by the exact SuperUI-Core toolchain |
| Provenance manifest | Source revisions, IDs, dependency graph, hashes, transformations, required migrations | Checked in under `TBCcontent/Karazhan/` and mirrored in build output |

Conceptually:

```text
TBC 2.4.3 MPQs (read-only)       pinned cMaNGOS DB/scripts (read-only)
              \                              /
               dependency + ID translation registry
                    /          |          \
        Vanilla client     SuperUI SQL     SuperUI C++
            patch          migrations      scripts
                    \          |          /
             exact SuperUI extractor toolchain
                         |
                maps / vmaps / mmaps
                         |
                  owner-operated staging
```

This separation lets `TBCcontent/` be the requested sibling of `SuperUiContent`
without committing raw Blizzard client assets or treating generated binaries as
source code.

## Client worldspace import

### World files and dependencies

The root map path is:

```text
World\Maps\Karazahn\Karazahn.wdt
```

The actual 2.4.3 archives confirm this is a normal tiled WDT, not a global-WMO
WDT. The effective WDT comes from `expansion.MPQ`, is 32,836 bytes, has
`MVER=18`, an all-zero `MPHD` (the global-WMO bit is not set), an empty `MWMO`,
and no WDT-level `MODF`. Its SHA-256 is
`7d2d11f496618f3ffa7e48cb32891e52049dd08bb7c112dece85e2e223018b31`.

Exactly these `MAIN` entries are active:

```text
Karazahn_34_51.adt  Karazahn_35_51.adt  Karazahn_36_51.adt
Karazahn_34_52.adt  Karazahn_35_52.adt  Karazahn_36_52.adt
Karazahn_34_53.adt  Karazahn_35_53.adt  Karazahn_36_53.adt
```

The effective ADTs are `patch.MPQ` overrides of the originals in
`expansion.MPQ`. Each repeats the same `MODF` placement for the instance WMO:
unique ID `622229`, rotation `(0, 308.5, 0)`, doodad set `0`, name set `0`.
That repetition is intentional for a WMO spanning tile boundaries and must be
preserved by the conversion.

The archive closure includes the nine active Karazhan ADTs, their WMO placements,
the instance WMO, WMO group files, every referenced texture, every doodad-set M2,
and every dependency of those M2s. The principal instance root found in the
actual patch MPQ, also cross-checked against the public listfile, is:

```text
World\WMO\Dungeon\AZ_Karazahn\Kharazan_instance.wmo
```

with group files `Kharazan_instance_000.WMO` through
`Kharazan_instance_070.WMO`. Older/exterior `Kharazan.wmo` files also exist and
must not be included merely because the names are similar. The WDT/ADT placement
records decide which roots are actually used.

The effective root from `patch.MPQ` is 326,156 bytes (SHA-256
`1c1454b5a07c6b7791846a4bc64f30fedaf0dec911e26474e6f3a833c3e168cc`),
WMO version 17, root ID 4449. All 71 version-17 group files are present. The root
contains 135 materials, 93 portals, 17 lights, 114 usable material-texture paths,
and one doodad set. Its `MOHD` header claims 7,060 doodads, while the actual
`MODS` bounds and `MODD` data resolve to 6,801 placements referencing 335
unique model paths. The importer must validate chunk/set bounds rather than trust
the summary count.

Across the nine ADTs there are five terrain textures, 394 unique case-normalized
M2/MDX paths, 28 placed WMO roots, and 71 `MODF` records representing 60 unique
path/UID placements. The union of direct ADT and root-WMO model dependencies is
672 unique models:

| Direct M2 version | Count |
|---|---:|
| 260 | 604 |
| 261 | 5 |
| 262 | 4 |
| 263 | 59 |

There are no direct Vanilla-v256 models in that source union. Generic paths must
first be resolved against the Vanilla archives, where a native v256 counterpart
may already exist; every remaining TBC-specific model needs conversion or an
explicit substitution. Raw copying these M2 files is not a viable phase-1 plan.

Karazhan-specific doodads occur under paths such as
`World\Azeroth\Karazahn\...`, but filename-prefix searching is not a safe
dependency strategy. Shared chandeliers, doors, particles, sounds, and textures
may have generic paths. The importer must parse references:

1. `Map.dbc` -> internal map directory.
2. WDT -> active tile set and any global placements.
3. ADTs -> WMO/M2 name tables and placement records.
4. WMO root/groups -> texture tables and doodad sets.
5. M2 -> skins, textures, animations, particles, ribbons, and sounds.
6. Repeat until the dependency graph reaches closure.

Minimap, loading-screen, map-overlay, and music assets are useful but not phase-1
requirements. Geometry, textures, and any assets needed to enter without a client
crash are requirements.

### Client DBCs

At minimum, the merged Vanilla client patch will need translated rows from:

- `Map.dbc` for map 532;
- `AreaTable.dbc` for Karazhan subareas;
- `AreaTrigger.dbc` for entrances/exits once normal zoning is enabled;
- `CreatureDisplayInfo.dbc`, `CreatureDisplayInfoExtra.dbc`, and
  `CreatureModelData.dbc` for imported creatures;
- `GameObjectDisplayInfo.dbc` for doors and props; and
- for combat, `Spell.dbc` plus the referenced visual, timing, duration, radius,
  range, icon, and sound records/assets.

Optional presentation adds `WorldMapArea`, `WorldMapOverlay`, loading screens,
minimaps, and music after the geometry gate passes.

TBC DBC record layouts are not interchangeable with Vanilla layouts. The import
operation must translate selected rows field by field and merge them into the
existing Vanilla DBC. It must never replace the whole Vanilla DBC with a TBC file.
This is particularly important now that weapons, armor, and world content can all
modify shared files: MangosSuperUI needs one final whole-file patch compiler.

The inspected files make this concrete: TBC `Map.dbc` has 125 fields and
500-byte records, while the target Vanilla `Map.dbc` has 42 fields and 168-byte
records. `AreaTrigger.dbc` happens to retain a 10-field/40-byte layout in both
snapshots, but its records still need ID and semantic validation before merging.
Likewise, TBC/Vanilla `AreaTable.dbc` use 35/25 fields and
`WMOAreaTable.dbc` use 28/20. Field-by-field projection is required.

The source has two map-532 area rows: 3457 (`Karazhan`, exploration flag 1085)
and 3477 (`Karazhan *UNUSED*`). Only 3457 is required initially. Root WMO 4449
has 188 source `WMOAreaTable` rows spanning alternate/obsolete name sets; matching
name set 0 to the 71 live groups reduces the required closure to 72 rows (one
fallback plus one per live group). Allocate collision-safe Vanilla row IDs.

Loading-screen row 200 does not exist in the target client. Phase 1 can clone
nearby Vanilla raid Map 533 as the Map-row template and reuse its loading screen
197, postponing a new loading-screen dependency without changing map 532's
geometry or server identity.

### Binary compatibility

- TBC M2s commonly use versions 260-263, while the Vanilla client expects version
  256. Existing item conversion proves that MangosSuperUI can produce some
  Vanilla-safe model output, but an animated world doodad or door is a harder case
  than a static item model.
- WMO and ADT structures are close across these client versions, but “close” is
  not a compatibility guarantee. Unsupported chunks, flags, doodad references,
  liquid, batches, and material fields must be validated against the Vanilla
  client and the target extractors.
- BLP textures are generally the least risky assets, but every referenced file
  still belongs in the manifest.

For the initial geometry shell, the lowest-risk strategy is to suppress the root
WMO doodad set and omit TBC M2s. That proves the WDT/ADT/WMO, textures, client DBC,
collision, and navigation before model animation becomes part of the problem.
The next pass can resolve generic paths to Vanilla assets and convert only the
missing models.

The conversion gate is therefore: no TBC-layout DBC rows, no unsupported M2
versions, no unresolved model or texture reference, and no client crash on enter,
relog, or tile transition. An upstream report of a stock 2.4.3 client crashing on
Karazhan entry is a useful reminder that zoning stability must be tested explicitly,
not assumed from a successful server load. See
[CMaNGOS issue 2223](https://github.com/cmangos/issues/issues/2223).

### Existing reader issue found during the study

`Services/WmoReader.cs::ParseGroup` currently reads v17 `MOGP` group flags from
offset `+0` and bounding boxes from `+4/+16`. The header begins with two name
offsets; flags are at `+8` and bounding boxes at `+12/+24`. The existing
batch-count, liquid, and subchunk offsets are already absolute/correct, so this is
a targeted header-field defect rather than a reason to replace the parser. It can
make rendering appear functional while exterior/interior flags and group bounds
are wrong. Fix and regression-test this before using the reader to generate the
72 live `WMOAreaTable` rows or validate collision bounds.

## Server geometry, collision, and pathing

The official cMaNGOS flow is:

```text
client archives -> DBC/maps
client archives -> vmap extractor -> Buildings -> vmap assembler -> vmaps
maps + vmaps -> MoveMapGen -> mmaps
```

SuperUI must follow the same stages, but use its own exact toolchain for final
output. Direct copies fail the compatibility contract:

| Format | cMaNGOS TBC reference | SuperUI-Core target | Result |
|---|---|---|---|
| `.map` version magic | `s1.4` | `z1.4` | regenerate |
| `.mmap/.mmtile` version | `8` | `6` | regenerate |
| final vmap magic | `VMAP_7.0` | `VMAP_7.0` | magic matches, but implementations differ; regenerate and validate |

For the source reference, the nine map/mmap tile stems use server `y,x` order:
`5325134` through `5325136`, `5325234` through `5325236`, and `5325334`
through `5325336`. The vmap tile names use `x,y` with separators:
`532_34_51.vmtile` through `532_36_53.vmtile`. Code should derive these names
from the target toolchain rather than hand-constructing them from one convention.

The source `532.vmtree` marks itself tiled (`isTiled=1`) and all nine vmap tiles
reference `Kharazan_Instance.wmo`. The `.mmap` file itself is the bare 28-byte
Detour navigation-parameter structure; the version check is on each `.mmtile`
header, where the source version 8 and target version 6 differ.

The safest pipeline is to first create a Vanilla-compatible client patch, mount
that patch over a staged Vanilla client data set, and run the extractors built from
the exact target SuperUI-Core revision. If the target extractor cannot discover
or parse the added assets, the alternative is a narrowly scoped TBC archive/input
front end that feeds the target raw-vmap format; the target assembler and target
MoveMapGen must still produce the final files.

The existing MangosSuperUI “surgical” vmap regeneration is not enough for this
first import. It intentionally reuses collision geometry already present in the
Vanilla `Buildings` output. Karazhan introduces new WMO/M2 geometry, so it needs a
full extraction path at least once. Later edits can reuse the surgical path after
the new geometry has become part of the staged model set.

Pathing has three separate inputs:

- the static WMO/terrain navmesh generated into `.mmap/.mmtile`;
- database waypoint and formation data; and
- encounter scripts for special movement, doors, teleports, flying, and evade.

Dynamic doors are not solved solely by baking a static navmesh. Their display/model
data, collision model, gameobject state, and instance-script transitions must agree.
Nightbane's flying phases and Chess movement should be late milestones; they are
poor first tests of the basic navmesh.

The pinned cMaNGOS extractor config has a Karazhan override for central tile
`3552`: simplification error `1.0`, detail sample distance/error `0.5/0.5`,
walkable height `5`, and climb `4`. Preserve these as reference values and map
them deliberately into the target generator's option schema; do not assume the
two generators interpret an identically named setting the same way.

## Database translation

The dedicated TBC-DB `532_karazhan.sql` is 3,981 lines and substantial but not
self-contained. Its own metadata calls the instance roughly 90% complete and
notes missing role-play behavior, so it is a strong baseline rather than a gold
master.
It contains local spawns, paths, groups, formations, DB scripts, conditions, and
worldstate metadata. The read-only inventory found:

| Data | Count |
|---|---:|
| Creature spawns | 984 |
| Distinct creature entries | 107 |
| Gameobject spawns | 417 |
| Distinct gameobject entries | 276 |
| Creature movement rows | 782 |
| Template movement rows | 121 |
| Spawn groups / memberships | 178 / 757 |
| Group formations | 16 |
| Waypoint paths / points | 10 / 148 |
| Worldstate conditions | 9 |

Following dynamically summoned and script-referenced entries expands the closure
to 148 creature templates, 176 model rows, 40 equipment rows, 123 static or 155
full-closure EventAI events, 662 creature-loot rows, 37 pickpocket rows, 12
skinning rows, 121 immunity rows, 70 reputation-on-kill rows, and ten quest
relations. These counts are useful regression oracles for a closure exporter.

The file assumes that the full TBC database already supplies creature and
gameobject templates, models, equipment, factions, spells, loot, gossip, texts,
and other global rows. TBC-DB describes itself as a content database specifically
for `mangos-tbc` and client 2.4.3, so importing this one SQL file verbatim is not a
valid shortcut.

The target schema also differs materially. The inspected SuperUI database has no
direct equivalents for several modern cMaNGOS tables, including
`instance_template`, `spawn_group`, and `waypoint_path`; it uses legacy structures
such as `map_template`, `creature_groups`, legacy movement tables, and different
DB-script tables. Translation has to preserve behavior, not table names.

Specific mappings and losses to handle explicitly:

- source `instance_template` becomes target `map_template` with raid type 2,
  player limit 10, linked zone 3457, seven-day reset, corpse map/coordinates, and
  the `instance_karazhan` script binding;
- source `spawnMask=1` is normal difficulty and may be dropped after validation,
  but source spawn-group weighted choices, maximum counts, worldstate gating,
  respawn overrides, and formations cannot be represented by target
  `creature_groups` alone;
- `waypoint_path` rows must be folded into target creature movement tables with a
  stable `path_id` mapping;
- relay/random DB scripts need semantic conversion to target generic/movement
  script tables or C++; numeric command values are not portable;
- EventAI must be projected into the target's split event/action tables; and
- encounter-credit/worldstate-name tables missing from the target become explicit
  instance-script state and manifest metadata.

A dependency-closure exporter should start from map 532 and recursively collect:

- creature/gameobject templates and addons;
- display/model, equipment, faction, and movement references;
- spawn groups, formations, linking, respawn, and path data;
- loot and quest/gossip dependencies selected for the milestone;
- script names, event IDs, text/broadcast rows, conditions, and worldstates; and
- every seeded spell plus recursively triggered spells.

Each row then passes through an explicit source-to-target schema mapper and an ID
collision ledger. Original IDs should be preserved when they are free because the
scripts embed many of them. A deterministic remap must update every inbound and
outbound reference when an ID collides. The manifest should make silent partial
imports impossible.

Do not retain the source SQL's fixed `5320000` GUID base without a collision
scan. Movement, groups, formations, linking, and DB scripts all refer back to
those GUIDs and must be rewritten from one stable mapping.

Useful source entrance records are:

| Trigger | Meaning | Destination |
|---:|---|---|
| 4131 | Main entrance | map 532, `-11101.8, -1998.31, 49.8927, 0.007069` |
| 4436 | Main exit | map 0, `-11112.9, -2005.89, 49.3307, 4.02516` |
| 4135 | Service entrance | map 532, `-11040.1, -1996.85, 94.6837, 2.20224` |
| 4520 | Service exit | map 0, `-11034.8, -2003.8, 92.98, 0` |

Phase 1 does not need trigger support: Nico can use an owner-operated GM teleport
on the development runtime. Normal entrance/exit behavior belongs in a later gate.
The inspected current 2.4.3 access data has no key/quest condition; its main and
service entrances require levels 70 and 68. Launch-era Master's Key attunement
would be an intentional policy addition, not something supplied by this snapshot.
No transport rows are required.

Before importing combat values, one design policy must be explicit: preserve
original TBC tuning/level assumptions, scale the raid for the SuperUI progression,
or support both as data profiles. Geometry and pathing should remain independent
of that choice.

## Script port

The current cMaNGOS Karazhan implementation is a real subsystem, not a handful of
boss timers. It comprises 14 files and 7,872 lines across the instance controller,
bosses, Opera, Chess, support NPCs, and shared definitions. Chess alone is 1,793
lines and Opera 1,055. The closure has 86 referenced NPC IDs, 23 GO IDs, 234
directly referenced spells (254 after following triggered spells), 42 creature
spell lists, 25 named C++ spell handlers bound to 26 spell IDs, two scripted
events, 73 negative script-text IDs, and 64 broadcast-text IDs.

Shared script dependencies include `npc_aoe_damage_trigger` for NPC 16697/aura
28874, `go_aura_generator` for chessboard 185324, and `go_bells` for bell
182064/sound 9154. These belong in the closure even though their implementations
are outside the Karazhan directory.

cMaNGOS uses APIs such as `CombatAI`, `DoBroadcastText`, modern spawn-manager
operations, and templated spell-script registration. SuperUI-Core uses an older
ScriptedAI/ScriptDev-style surface and static script registration. The source can
be used as the behavioral specification, but it is not a copy/paste port.

Recommended rules:

- Put adapted encounter source under `TBCcontent/Karazhan/`.
- Expose one `AddTBCcontentScripts()` entry point from the new subtree, then
  register its children explicitly.
- Keep a very small `TBCcontent/Compatibility/` layer for repeated mechanical API
  mappings. Do not import cMaNGOS's whole AI framework into the Vanilla core.
- Rewrite encounter state machines in native SuperUI idioms and bind every script
  name through the target database.
- Treat spells as a separate closure: translate only the encounter spells needed
  by the current milestone, recursively include triggered spells, add Vanilla
  client `Spell.dbc` rows and visuals, and emulate/remap TBC-only effects or auras.

SuperUI-Core supports `ScriptedInstance`, `SpellScript`, `AuraScript`, and the
aura-generator gameobject type, but its factory/binding APIs differ. In particular,
target `spell_scripts` is a DB-command table, not cMaNGOS's named C++ registration
mechanism. Named spell factories bind through `spell_template.script_name`.
The target currently selects the newest `spell_template.build` no greater than
supported client build 5875, so rows stamped as TBC build 8606 will be ignored.
Every imported spell needs the project's Vanilla/custom-spell conversion and
build policy, not merely copied SQL.

Attumen/Midnight is the best first combat slice. It exercises two linked AIs,
mount/phase transition, instance state, door or spawn state, and a small spell set.
Maiden is smaller in source lines, but Attumen is the more useful end-to-end entry
encounter. Opera, Chess, and Nightbane should be last because they multiply event,
movement, vehicle-like, flying, and persistence requirements.

## Proposed `TBCcontent/` source layout

```text
SuperUI-Core/src/game/
├── SuperUiContent/
└── TBCcontent/
    ├── README.md
    ├── Loader/
    │   ├── TbcContentLoader.h
    │   └── TbcContentLoader.cpp
    ├── Compatibility/
    ├── Shared/
    │   ├── TbcContentIds.h
    │   └── ContentManifest.schema.json
    └── Karazhan/
        ├── README.md
        ├── content-manifest.json
        ├── karazhan.h
        ├── instance_karazhan.cpp
        ├── boss_midnight.cpp
        └── ...
```

SuperUI-Core uses explicit CMake source lists; the directory will not compile just
because it exists. The implementation must update the relevant source list,
include path/source group, and static script loader, then reconfigure before a
build. Database migrations remain in the existing SQL migration tree, referenced
from `content-manifest.json`.

Suggested MangosSuperUI components for the later implementation:

- `TbcWorldSource`: read-only, patch-aware TBC archive mount;
- `WorldDependencyScanner`: WDT/ADT/WMO/M2/BLP transitive closure;
- `VanillaWorldTranscoder`: selected DBC rows and compatible world assets;
- `TbcContentRegistry`: durable IDs, source hashes, and transformation records;
- `TbcDatabaseTranslator`: read-only cMaNGOS closure export to target migrations;
- `ServerArtifactBuilder`: exact-core extraction into staging only; and
- `WorldPatchLane`: contribution to the unified whole-file MPQ compiler.

## Phased pilot and acceptance gates

### Phase 0 — immutable inventory

- Pin the TBC client build, cMaNGOS/TBC-DB revisions, and target core revision.
- Parse map 532's nine-tile world graph and produce a complete file manifest.
- Record DBC/database ID collisions and target schema mappings.
- Correct and regression-test the WMO group-header flag/bounds offsets.
- Add offline validators for record widths, model versions, missing files, and
  final server format versions.

**Gate:** the closure is complete and reproducible from read-only sources; no
unexplained files or IDs remain.

### Phase 1 — empty Karazhan

- Build a client patch containing Vanilla-format Map 532 and Area 3457 rows,
  reuse loading screen 197, and include the original WDT, all nine effective
  patch-level ADTs, the root WMO, all 71 groups, and direct WMO/terrain textures.
- Suppress the WMO doodad set and omit all raw TBC M2s for this first shell.
- Resolve any generic ADT M2 paths to native Vanilla assets; sanitize remaining
  `MDDF/MCRF` and secondary `MODF/MCRF` references for the strict shell rather
  than leaving dangling TBC-model references.
- Prepare a minimal `map_template` migration, without population or encounters.
- Generate target-format map/vmap/mmap products with the exact SuperUI toolchain.
- Produce manifests and hashes; do not install them automatically.

**Owner-operated test:** Nico stages the prepared artifacts and database change,
then enters using GM teleport. Verify load, rendering, coordinates, floors/heights,
collision, line of sight, disconnect/relog, and exit without a client crash.

### Phase 2 — pathing laboratory

- Add one controlled test NPC or the first translated trash patrol.
- Translate a short waypoint path through the entrance and stairs.
- Validate chase, evade, height changes, corners/line of sight, and a door state.

**Gate:** no falling through floors, wall traversal, stuck chase, or invalid tile
load; the nine expected navigation tiles load with target version 6.

### Phase 3 — static population

- Translate creature/gameobject templates, display assets, spawns, equipment,
  formations, groups, patrols, and the required non-combat dependencies.
- Keep boss scripts disabled while validating placement and movement.

**Gate:** closure report has zero unresolved references and populated rooms remain
stable through unload/reload.

### Phase 4 — Attumen/Midnight vertical slice

- Translate the exact spell, text, state, model, loot, and script dependencies.
- Port the encounter natively under `TBCcontent/Karazhan/`.
- Validate wipe/reset, respawn, phase transition, kill, persistence, and re-entry.

**Gate:** repeatable encounter behavior and clean target-core build. Installation,
database application, and runtime control remain Nico's steps.

### Phase 5 — remaining raid

Port one encounter family at a time. Leave Opera, Chess, and Nightbane until the
instance framework, spell translator, pathing, and persistence are already proven.

## What this pilot will not prove

Karazhan does not validate a complete Outland import. A continent adds broad
outdoor terrain ADTs, liquids, lighting, weather, zone/area coverage, streaming
across many tiles, flight/taxi data, transports, world-map UI, and much larger
dependency and navigation graphs. The reusable tooling from this pilot is the
right foundation, but Outland should be a separate milestone after map 532 works.

## Principal risks

1. **Client format compatibility:** a syntactically valid MPQ can still crash the
   Vanilla client because of one TBC-only model, WMO chunk, DBC field, or dangling
   dependency.
2. **False binary compatibility:** matching vmap magic does not make independently
   evolved structs safe to copy.
3. **Database closure:** the dedicated map SQL references global TBC data outside
   that file, and source/target schemas encode grouping and scripts differently.
4. **Spell semantics:** many encounter mechanics depend on TBC spell effects,
   attributes, targets, and auras that do not map one-to-one to Vanilla.
5. **ID collision:** previously imported/custom content may already occupy display,
   spell, area, text, event, or template IDs.
6. **Licensing/provenance:** preserve cMaNGOS/TBC-DB notices and review adapted SQL
   and code licensing separately; do not commit raw client assets.

## Bottom line

The correct first objective is not “port Karazhan.” It is:

> Reproducibly build an empty, Vanilla-client-compatible map 532 and regenerate
> SuperUI-native collision and navigation for it, with a complete manifest and no
> unresolved dependencies.

Once that gate passes, database population, pathing, and Attumen can be added in
separate, observable layers. The requested `TBCcontent/` sibling is the right home
for owned core source and the content manifest, but not for opaque copied cMaNGOS
server binaries or raw TBC client files.
