# WEAPON_GEN — Generating Valid Custom Weapons

Map of how MangosSuperUI (web app), SuperUI-Core (server), and MSUIClient (client) combine to
generate brand-new, valid weapon models — mesh + texture — and get them rendering in-game.
Armor comes later (see §9); weapons first because they are the simplest renderable item:
one rigid base-pass model, one DBC-controlled texture, no body compositing, and no generated
animation. The chosen stock sword scaffold still carries eight bones and five effect
attachments even though all vertices are rigidly weighted to its root.

Status: **authoritative implementation and handoff plan** (new-geometry generation is not
implemented yet). The source references, stock-asset census, current capability inventory,
and operating boundaries below were verified on 2026-08-17.

Source provenance matters:

- MangosSuperUI web code is `{SUPERUI_ROOT}`, the workspace containing this file.
- MSUIClient is `{MSUI_ROOT}`.
- Paths written as `Services/...`, `Controllers/...`, or `wwwroot/...` are relative to
  `{SUPERUI_APP}`; paths such as `Formats/...` and `World/...` are relative to `{MSUI_APP}`.
- `{MSUI_ROOT}\.reference-vmangos-core` is a stock/reference checkout, **not** the authoritative
  SuperUI-Core fork. Its recoverable identity is `github.com/vmangos/core.git`, branch
  `development`, commit `91c920d402ff1d4f99cb8292e5a9ed86715309f9`; use it only for
  comparison/navigation.
- The authoritative fork is `{CORE_ROOT}` (`/home/wowvmangos/vmangos` on the homeserver); it
  is not present in this workspace. Read-only verification on 2026-08-17 pinned commit
  **`dc7ebbbe7f06d934860f8e67e595c31dfe34a2bf`** on branch `development`. The working tree also
  contained unrelated pre-existing changes in `src/game/SuperUiBots/AiBotAIMain.cpp`,
  `AiBotAIMain.h`, `AiBotAIMovement.cpp`, plus untracked sibling files
  `AiBotAIMain.cpp.prefinding018`, `AiBotAIMain.h.prefinding018`, and
  `AiBotAIMovement.cpp.prefinding018`; none belongs to this weapon plan. There were no staged
  core changes. The local generated source-graph snapshot is older (2026-04-10).
- Read-only travel-key command used to identify the authoritative checkout:

  ```powershell
  ssh -i "{VMANGOS_SSH_KEY}" `
    -o BatchMode=yes -o ConnectTimeout=10 wowvmangos@192.168.0.2 `
    'git -C {CORE_ROOT} rev-parse --show-toplevel HEAD'
  ```

Current-PC recovery roots (replace through configuration on the next PC, never hard-code in
compiler records):

| logical root | current location |
|---|---|
| `{SUPERUI_ROOT}` | `C:\Users\nico\source\repos\MangosSuperUI` |
| `{SUPERUI_APP}` | `{SUPERUI_ROOT}\MangosSuperUI` |
| `{MSUI_ROOT}` | `C:\Users\nico\source\repos\MSUIClient` |
| `{MSUI_APP}` | `{MSUI_ROOT}\MSUIClient` |
| `{MSUI_DATA_ROOT}` | `{MSUI_ROOT}\GameData\Data` |
| `{ULTUM_ROOT}` | `C:\Users\nico\Desktop\CRPG-Ultum` |
| `{CORE_ROOT}` | `/home/wowvmangos/vmangos` on `wowvmangos@192.168.0.2` |
| `{VMANGOS_SSH_KEY}` | current filename `id_ed25519_msui_vmangos_travel_20260731`; provision securely outside Git |
| `{ARTIFACT_ROOT}` | **not implemented/configured yet**; Phase 0 must choose a controlled/content-addressed store before any Forge state exists |

Phase 0 records the then-current authoritative commit/branch/dirty list in every corpus and
validation manifest and revalidates the symbols if the commit differs from the pinned value.
Never infer fork behavior solely from the stock reference checkout.

Terminology used throughout:

- **Verified fact** means confirmed in the current source or measured from the installed
  vanilla asset corpus.
- **Design decision** means the required v1 contract even when vanilla permits more.
- **Open question** means work that must be resolved before the dependent phase starts.
- **Build/staging** means work Codex may implement and test without changing live state.
- **Owner acceptance** means Nico applies database changes, installs artifacts, controls
  server/client runtime state, and performs live commands. Those actions are never part of
  an agent-run build or test.

---

## 1. Executive decision

A weapon the client can render is a join across five artifacts:

```
item_template row          (server)        entry ≥ 900000, display_id ───────────┐
ItemDisplayInfo.dbc row    (client patch)  display_id → model + texture + icon   │
weapon M2 file             (client patch)  Item\ObjectComponents\Weapon\       │
                                           SUI_W_####.m2                        │
BLP2 texture               (client patch)  UV-sampled pixel payload             │
patch MPQ                  (artifact)      carries DBC + M2 + BLP               │
                                                                                ↓
                  player item-query path OR NPC virtual-weapon path → rendering
```

The architecture is settled:

- **MangosSuperUI web app is the forge and artifact compiler.** It owns deterministic mesh
  generation, UVs, texture generation, M2/BLP/DBC serialization, preview, validation, and
  immutable MPQ bundle creation.
- **SuperUI-Core remains asset-blind.** It owns gameplay fields and sends the item entry or
  display metadata. It does not and should not parse M2, BLP, UVs, or MPQs.
- **MSUIClient is the fast independent consumer and diagnostic surface.** The real Blizzard
  1.12.1 client is the final compatibility judge because MSUI intentionally simplifies some
  M2 rendering behavior.

The existing stack proves **existing-model retexture**, DBC serialization, BLP conversion,
MPQ building, and basic preview. It does **not** prove the custom-model link. The material
missing work is larger than three wiring changes:

1. Reconcile the two M2 parsers and build a lossless/raw vanilla-M2 structural inspector.
2. Prove a custom model path with an offset-preserving donor-based Frankenweapon.
3. Build a donor-scaffold static-weapon writer with four inline views and strict validation.
4. Add direct generated-byte preview, custom-M2 persistence/packaging, atomic shared ID
   reservation, and a pure snapshot-based patch builder.
5. Build the deterministic parametric mesh/UV generator. ULTUM's Blender recipes are the
   starting geometry reference; a later TRELLIS.2 route handles hero weapons.

The first valid target is deliberately narrow: a static one-handed sword, one indexed
triangle-list primitive, one material, one Type-2 texture, opaque 128×64 DXT1 BLP, four
inline views, vanilla `MD20` v256 output, and `ItemVisual=0`.

---

## 2. The five artifacts, precisely

### 2.1 `item_template` — the only persisted server-side asset-routing record

Verified core behavior:

- The authoritative `{CORE_ROOT}` at pinned commit `dc7ebbbe…` does **not** load
  `ItemDisplayInfo.dbc`; loading is explicitly commented out in
  `src/game/Database/DBCStores.cpp:71,231`. `display_id` is loaded as item metadata and sent to
  clients. The core cannot validate that a DBC row, M2, BLP, UV layout, or MPQ member exists.
  The stock `.reference-vmangos-core` copy is a convenient local line reference, not evidence
  for fork behavior.
- "Any uint32" is too broad. The core performs no DBC referential validation, but the live
  `item_template.entry` and `display_id` columns are `MEDIUMINT UNSIGNED`, giving the practical
  range `0..16,777,215`. The loader also contains three legacy display-ID substitutions.
- Player and NPC visuals travel through different paths:

  ```text
  Player equip:
    PLAYER_VISIBLE_ITEM publishes item ENTRY (+ enchants)
      → client requests item data
      → SMSG_ITEM_QUERY_SINGLE_RESPONSE supplies DisplayInfoID and metadata

  NPC virtual weapon:
    Creature::SetVirtualItem publishes DisplayInfoID + class/subclass/material/
    inventory/sheath directly
  ```

  Both paths are required acceptance tests. Character selection is a third display-direct
  presentation path but is not needed for the first weapon gate.
- Visual-routing fields that matter are `class=2`, a compatible `subclass`, `inventory_type`,
  `material`, `sheath`, and `display_id`. Weapon-compatible inventory types include at least
  `13` weapon, `15` ranged, `17` two-hand, `21` main-hand, `22` off-hand, `25` thrown, and
  `26` ranged-right. V1 supports a one-handed sword only; other combinations must clone a
  known-good donor and fail closed when subclass/inventory/sheath disagree.
- `item_template.Material` is gameplay/on-wire metadata. It is **not** an M2 render material,
  a BLP format, or the texture wrapper.
- Existing item allocation uses `GET /Items/NextCustomId` and `MAX(entry)+1` above
  `CUSTOM_RANGE_START = 900000`. That read-then-write scheme is not atomic and must be
  replaced or guarded by a transaction/unique reservation before concurrent Forge commits.
- DBC display row 679 is only the **visual donor**; a read-only authoritative DB query found no
  `item_template` row using display 679. The pinned gameplay donor is entry **2131,
  `Shortsword`**: class 2, subclass 7, display 22075, inventory type 13, allowable class/race
  -1/-1, required level 1, required skill/rank 0/0, material 1, sheath 3, patch 0, quality 1,
  item level 3, delay 2600, damage 2..4, max durability 20, bonding 0. Inventory type 13 is
  preferred because it permits main-hand and dual-wield-capable offhand acceptance tests.
  Phase 0 records its complete explicit schema/row snapshot, not only this summary. Main-hand-
  only fallbacks are entry 25 `Worn Shortsword` (display 1542) and entry 1161 `Militia
  Shortsword` (display 1544), both inventory 21, material 1, sheath 3, patch 0.
- The generic `/Items/Save` endpoint defaults omitted fields to zero and is unsafe as a
  weapon-row generator.
- `item_template.sql` must clone that full pinned gameplay row using an explicit column list,
  then change only deliberate fields such as reserved entry, names, display ID, quality/stats,
  requirements, damage, and prices. Preserve a known-valid class=2/subclass=7/inventory/
  material/sheath/allowable-mask/delay/damage tuple until each change is validated. If the
  reserved target entry already exists or any donor column is missing, fail closed. An
  "idempotent" handoff means a guarded insert whose already-correct hash can be recognized;
  it never means `ON DUPLICATE KEY UPDATE` over an unrelated live entry.
- A new row with only `patch=0` is visible on every normal content patch: the loader selects
  the greatest row patch `<=` the active WoW patch. A later higher-patch row for the same
  entry supersedes it. The former `patch=0` open question is resolved.
- The anti-datamining gate only blocks querying an undiscovered template before acquisition.
  `Item::Create` and normal acquisition paths mark the item discovered; an owner-run
  `.additem` is sufficient for the first GM test. Do not globally disable the setting or
  mutate mailbox/worldstate merely to satisfy the development path.
- `.reload item_template` exists and calls `LoadItemPrototypes()`. It is an **owner acceptance
  action**, not an automatic Forge step. The build path never sends RA commands, restarts a
  process, or changes live runtime state.

### 2.2 `ItemDisplayInfo.dbc` (client) — the binding record

Corrected vanilla 1.12.1 layout: 23 fields, 92 bytes per row. Both comments currently present
in `Services/DbcService.cs` are stale/contradictory around fields 9/10; the raw installed-DBC
evidence recorded below is authoritative and the source comments must be corrected.

| field | meaning | weapon-gen use |
|---|---|---|
| 0 | ID | minted display id (≥ 60000) |
| 1 | ModelName1 | **`SUI_W_####.mdx`**; DBC logical name, including extension |
| 2 | ModelName2 | empty for an ordinary weapon; MSUI uses it for second shoulder only |
| 3 | TextureName1 | `SUI_W_####_V01`; bare stem, no directory or extension |
| 4 | TextureName2 | empty for v1 |
| 5 | InventoryIcon | reuse a fitting stock icon initially |
| 6–8 | GeosetGroup[0..2] | zero for v1 weapon |
| 9 | flags/sparse presentation field | zero for v1 |
| 10 | SpellVisualID | zero for v1 |
| 11 | GroupSoundIndex | preserve the selected simple-sword donor value initially |
| 12–13 | HelmetGeosetVis[0..1] | zero |
| 14–21 | body texture components | empty; these belong to worn body-atlas armor |
| 22 | ItemVisual | zero until enchant effects are explicitly implemented |

Raw installed-DBC evidence resolves the former field-9/10 disagreement: field 9 is nonzero in
only 11 armor/cape/tabard-like rows with values 1/2, consistent with flags; field 10 is nonzero
in 1,008 rows and contains characteristic ranged values (bows 5, firearms 224, thrown 98,
wands 225/226), proving it is `SpellVisualID`. MSUI's field-10 parsing is correct. Fix the
stale/conflicting web comments and any web consumer that treats field 9 as SpellVisual. V1
writes both fields zero.

`DbcWriterService` already provides `CloneRow`, `PatchRow`, `AddString`, sorting, and WDBC
serialization. However, "patch ModelName1" alone is not sufficient: a cloned donor row can
silently inherit ModelName2, TextureName2, body/geoset fields, spell/item visuals, and other
state. Weapon generation must either construct a purpose-built 23-field row or clone display
row **679** and explicitly set every field listed above. Never rely on unspecified donor
values. `Write()` sorts by ID internally; the final validator still confirms order and string
offset validity.

Naming is deliberately asymmetric:

```text
DBC ModelName1:  SUI_W_0001.mdx
MPQ model member: Item\ObjectComponents\Weapon\SUI_W_0001.m2
DBC TextureName1: SUI_W_0001_V01
MPQ BLP member:   Item\ObjectComponents\Weapon\SUI_W_0001_V01.blp
```

The existing display allocator uses `MAX+1`, checks only the two retexture tables, and is not
transactional. The weapon path must replace it with one shared reservation mechanism covering
the stock DBC maximum, retextures, body-atlas entries, weapon displays, and future model
variants. Enforce uniqueness in the database and serialize/reserve IDs atomically. The
`CUSTOM_DISPLAY_BASE = 60000` convention remains valid; 60000+ ItemDisplayInfo IDs are already
proven in-client.

### 2.3 The M2 mesh — the artifact we cannot produce yet

Vanilla item M2s are single-file, little-endian, offset-table binaries with embedded view/skin
data. The v1 writer emits magic `MD20`, version 256, and no external `.skin` files. The current
web reader rejects version `>=264`; the measured stock corpus contains 569 v256 weapons and
two v257 Stratholme maces, so "all stock is exactly 256" is false while **v256 remains the
canonical output decision**.

#### The current readers are not yet a write specification

`Services/M2Handlers/M2Reader.cs` is a useful render/preview reader, but it is intentionally
lossy. Its `M2Model` omits or collapses:

- UV1/the second 48-byte-vertex UV pair;
- all inline views except view 0;
- most of each 32-byte submesh record, including submesh bounds;
- many sequence fields and attachment animation data;
- full transparency tracks;
- model bounds, collision arrays, events, and multiple header/lookup arrays.

It therefore cannot be inverted for stock struct-for-struct round trips as written. A
parse→emit→parse test through the same partial reader is circular and can pass while losing
data required by the reference client.

There is also a blocking parser disagreement in the 24-byte batch record. The web reader
interprets the tail around `+18..+22` differently from the corrected MSUI reader; web currently
places a transform index at `+18` and omits `+22`, while MSUI reads texture-coordinate at
`+18`, weight at `+20`, and transform at `+22`. Reconcile this from measured bytes and one
authoritative layout before building a serializer.

Required foundation:

1. A raw structural inspector that enumerates every v256 header array, preserves unmodeled
   bytes, reads all four views, and reports exact offsets/ranges without coordinate conversion.
2. A writer-owned `RigidWeaponMesh` DTO for positions, normals, UV0, vertex IDs, indices, and
   the single material contract. Do not overload the GLB-preview `M2Model` as the file AST.
3. A donor-scaffold writer that preserves or deliberately emits all required lookup/event/
   attachment structures and recomputes offsets, padding, counts, bounds, and radii.
4. Independent validation through the corrected web parser, MSUI parser, raw inspector, and
   finally the real 1.12.1 client.

#### Measured stock-weapon corpus (Phase 0 substantially complete)

The installed archive census covered 571 `Item\ObjectComponents\Weapon\*.m2` models:

| measurement | result |
|---|---|
| inline views | **all 571 have four**; views are real per-LOD structures, not assumed duplicates |
| version | 569 v256; 2 v257 |
| triangle range | 10–1,002; median 136 |
| vertex range | 8–1,038; median 130; p95 482 |
| sword subset | 102 swords; 42–616 triangles; median 102.5; p95 362 |
| sequences | 501/571 have one; every sampled sword starts with animation ID 0/Stand |
| collision | 569/571 empty; only two Horde 2H axes carry collision arrays |
| sword orientation | all 102 use raw WoW +X as their longest/blade axis |
| sword weights | rigid `(255,0,0,0)` to bone 0 |
| sword UV1 | all 102 sampled swords store `(0,0)` for every second-UV value |
| bones/attachments | most swords contain 8 bones and 5 enchant attachments, IDs 0–4 |
| textures | only 158/571 use one texture; 308 use two; one Type-2-only slot is a proven simple pattern |

This replaces the former `nViews=1 vs 4`, general triangle-budget, sword-axis, collision, and
basic BLP-envelope open questions. It does **not** prove that a one-view synthetic file is
accepted, and there is no reason to take that risk: output four views.

The golden donor's four views are confirmed non-identical. Their final view-header field values
are `256, 75, 53, 21`; view 0's vertex lookup/triangle map differs from views 1–3, and each
32-byte submesh record differs, while the measured bone-property and batch bytes match.
Fixed-topology phases preserve all four view arrays byte-for-byte **only while edits remain
inside every preserved global and per-view submesh center/bounds contract**. If they do not,
Phase 2 may patch the proven fixed-width center/bounds fields in all four submesh records without
moving offsets; it must never leave stale culling metadata. Variable-topology generation must
explicitly create and validate four view-local vertex lookup, triangle, submesh, and batch
structures; copying only view 0 is invalid. A later simplification may deliberately emit four
equivalent generated views only after the reference client proves that policy.

#### Canonical v1 donor and scaffold

Use `ITEM\ObjectComponents\WEAPON\Sword_1H_Short_A_01.m2`, DBC display row **679**, and
`Sword_1H_Short_A_01Blue.blp` as the golden fixture:

- v256, 7,056 bytes;
- 34 vertices, 48 triangles;
- one submesh and one batch;
- four inline views;
- one Stand sequence;
- eight bones, with every vertex rigidly weighted to root bone 0;
- one Type-2-only texture slot;
- five enchant attachments, IDs 0–4, distributed along +X;
- no collision;
- raw WoW vertex bounds approximately `min(-0.206,-0.061,-0.124)` and
  `max(0.889,0.066,0.139)`; the blade runs +X and the hilt/pommel straddles X=0;
- attachment positions approximately X=`0.17, 0.32, 0.47, 0.61, 0.78`;
- UV0 range approximately U=`0.0078..0.9922`, V=`0.0156..0.9688`, demonstrating deliberate
  edge padding rather than sampling exactly on texture borders;
- a nonzero sequence lookup, 203-entry playable-animation lookup, key-bone lookup,
  texture-replace table, bone lookup, texture-unit lookup, UV lookup, five attachment lookup
  entries, and two events;
- 128×64 opaque DXT1 texture.

This donor is safer than Quel'Serrar, which uses three submeshes, four batches, DXT3 alpha,
a Type-2 texture plus embedded reflect texture, and an effect pass that MSUI suppresses.

The first writer is a **donor-scaffold static-sword writer**, not a general M2 editor. Its
output contract is:

| section | v1 decision |
|---|---|
| header/name | `MD20`, v256, canonical custom model name |
| sequences/lookups | preserve the donor's known-good Stand and lookup anatomy |
| bones | preserve donor's eight-bone scaffold; generated vertices weight 255 to bone 0 |
| vertices | 48 bytes: raw WoW position, weights, bone indices, normal, UV0, UV1; UV1 is `(0,0)` for v1 |
| views | four inline views; fixed-topology phases preserve the donor's distinct view-local structures |
| submesh/batch | exactly one of each; opaque base material |
| texture | one 16-byte Type-2 entry with empty embedded filename plus valid lookup chains |
| attachments/events | preserve five donor attachment IDs/lookup and event scaffold; `ItemVisual=0` means effects remain unused |
| collision | zero for the simple-sword scaffold |
| bounds | recompute vertex AABB, center, and appropriate authored radius |
| optional render systems | colors, particles, ribbons, lights, cameras, UV animations remain absent unless donor structure requires a preserved lookup |

Do not validate radius as `radius >= max(|vertex| from origin)`: that rejects 502/571 stock
models. Radius normally encloses geometry relative to the model bounds center, and animated
models can carry a deliberately larger authored value. The raw inspector and donor comparison
must define the exact v1 rule.

#### Coordinates, pivot, and attachments

WoW model space is Z-up. The existing preview conversion is:

```text
WoW → glTF/MSUI preview: (x, y, z) → (x, z, -y)
glTF → WoW writer:       (x, y, z) → (x, -z, y)
```

Canonical v1 coordinate/sampling contract:

- `RigidWeaponMesh` is right-handed, glTF-like **Y-up** authoring space. +X runs from grip toward
  the sword tip, the grip is at the origin, and one DTO unit equals one WoW model-space unit;
  the compiler never guesses or silently rescales. Import normalization must fit the measured
  donor envelope explicitly and record its scale/translation.
- WoW M2 space is right-handed Z-up. The two matrices above are inverse orthonormal rotations
  with determinant +1, not reflections. Apply the same rotation to normals and renormalize;
  preserve triangle index order across this final conversion.
- Before that conversion, a GLB importer bakes every node transform into positions. Normals use
  the inverse-transpose of the node's linear 3×3 transform and are renormalized. Reject singular
  transforms. If a baked node transform has negative determinant, reverse each triangle once;
  do not reverse again for the determinant-+1 glTF↔WoW rotation.
- `RigidWeaponMesh` UV0 uses the pipeline's top-left image convention: `(0,0)` is the top-left
  texel corner, U increases right, and V increases down. Copy UV0 unchanged into M2 and preview
  glTF. PNG→BLP encoding preserves top-to-bottom pixel-row order; neither writer flips V or
  image rows. Any importer with a bottom-left convention must canonicalize once at ingest and
  record that operation. A donor checkerboard fixture proves the rule before Phase 1.
- UV1 is exactly `(0,0)` for every v1 vertex. Tangents are not serialized in this scaffold;
  normals and winding determine front-face/culling behavior.

For swords, the grip/crossguard sits near local X=0, the pommel extends slightly into -X, and
the blade extends along +X. The weapon renderer mounts the model root directly to the hand;
it applies no weapon-specific corrective translation/rotation. M2 attachment 0 is an
enchant/effect mount, **not** the grip point. Scale and pivot are part of the authored mesh.

MSUI attachment behavior used by validation:

- main/default weapon → right-hand attachment ID 1;
- offhand weapon → left-hand attachment ID 2;
- shield → left-wrist attachment ID 0;
- equipment slots 15/16/17 drive main/off/ranged sheath handling;
- the core's authoritative `SheathTypes` values are `0=None`, `1=Mainhand`, `2=Offhand`,
  `3=LargeWeaponLeft`, `4=LargeWeaponRight`, `5=HipLeft`, `6=HipRight`, `7=Shield`;
- MSUI currently implements a different partial 1–4 placement mapping and hides/omits core
  values 5–7. Treat MSUI sheath rendering as partial until all eight values are reconciled
  against donor metadata and the reference client;
- missing race/sex character attachment IDs silently skip rendering, so validate on actual
  target character models rather than the weapon alone.

### 2.4 The texture wrapper — how the skin binds to the mesh

"Wrapper" is shorthand for three separate artifacts and must not be modeled as one opaque
skin file:

1. **UV0 lives in the mesh.** Each 48-byte M2 vertex stores the coordinates that map image
   pixels onto triangles. The UV wireframe/template PNG is an authoring guide only; it is not
   a runtime artifact.
2. **The M2 texture slot is Type 2.** Its embedded filename is empty. ItemDisplayInfo
   `TextureName1` supplies the bare BLP stem, resolved in the model's weapon directory. This
   is the required generated-content convention and gives one M2 many display/texture
   variants without duplicating geometry.
3. **BLP2 stores the sampled pixels.** `BlpWriterService` emits DXT1, DXT3, and palettized
   RAW1 with mip chains; the decoder also reads DXT5. Dimensions must be powers of two.
   Current public conversion accepts dimensions from 4 through 4096; JPEG-encoded BLP2 is
   not supported and is never emitted by this pipeline.

Full weapon-folder census: 3,730 readable BLPs, of which 2,421 are 128×64, 3,511 are DXT1,
175 are DXT3, 40 are palettized, and four additional DXT files use 1-bit alpha. Dimensions
range from 32 through 512 per axis, including a rare 256×512 texture; the corpus is
overwhelmingly 128×64 DXT1. Therefore the v1 output matches its donor exactly: **128×64 DXT1,
no alpha, full mip chain**. DXT3/1-bit alpha are valid stock behavior but deferred until the
opaque path passes. Generated runtime textures may later use 256×128 when detail justifies it;
preserve a higher-resolution source master.

Route A uses a fixed 2:1 per-archetype atlas: blade faces/edge/fuller, guard, grip, and pommel
islands, with optional mirrored blade halves. Generate region-mask PNGs and a visible UV
wireframe alongside the source texture. Pack islands with explicit gutters and mip-safe edge
dilation; otherwise low mip levels will bleed neighboring material colors across seams.

Never publish generated content by shadowing a stock Type-0 embedded path. Every generated
model and texture gets a unique `SUI_W_*` name. Type-0/layered reflect textures and multiple
passes are outside v1.

Field 5 references an `Interface\Icons` BLP stem. Reuse a fitting stock icon initially; a
ComfyUI-generated icon is a later presentation upgrade, not a validity dependency.

### 2.5 The patch MPQ + delivery — two clients, one archive

`MpqBuilderService`/`MpqArchiveWriter` already provide managed MPQ v1 output, temporary-file
construction, archive reopening, byte-for-byte member verification, and replacement. Reuse
that implementation after separating it from live deployment side effects.

Design decisions:

- Build one authoritative `ItemDisplayInfo.dbc` snapshot from the clean DBC plus the union of
  retexture, body-atlas, and weapon-display rows. Two independently built high-priority DBCs
  will shadow one another. The normal output remains `patch-4.MPQ`; `patch-5` is a deliberate
  escape hatch only if it contains the same complete authoritative DBC snapshot.
- Build from a snapshot under a process/build lock. Add each shared custom M2 once, each BLP,
  and exactly one sorted DBC. Reopen the finished MPQ and validate the extracted bytes.
- Make builds immutable and content-addressed internally. `GET /Items/DownloadPatch` serves
  an existing build; a download must never trigger regeneration or deployment as a side
  effect.
- The artifact path is outside every live client Data directory. A successful build ends at
  a downloadable ZIP/MPQ and owner checklist.

Current implementation hazards that must be fixed before reuse:

1. `RebuildPatchMAsync` returns early when `custom_item_retexture` is empty, before considering
   body-atlas or future weapon rows. Query all source sets first, then test whether the union
   is empty.
2. Existing schemas reserve `custom_m2` fields but current commits store NULL/empty paths and
   the rebuild only adds BLP bytes. Weapon integration must explicitly persist and package M2.
3. The builder currently copies `patch-4.MPQ` into a configured Linux client directory after
   building. Commit/delete/download paths can therefore deploy indirectly. Refactor a pure
   artifact builder before the Forge calls any of those paths.
4. Archive-priority logic is inconsistent. MSUI's `MpqMount` uses correct descending numeric
   patch precedence. SuperUI's held-archive reader and `tools/mpqpeek` use reverse alphabetic
   ordering, which can put `patch.MPQ` ahead of `patch-2.MPQ`. The diagnostic named here is
   `{MSUI_ROOT}\tools\mpqpeek`, not the intentionally ignored MangosSuperUI root `tools/` tree.
   Share/fix one numeric comparator before using provenance results as authoritative.
5. Startup cache registration, build-status/download, delete/rebuild, and in-memory
   `RegisterCustomDisplayEntry` flows are retexture-centric. Extend them to model/display
   records and pass explicit custom model, texture, and `ItemVisual=0`; do not make preview
   depend on a previously installed MPQ.

Required handoff bundle:

```text
weapon-build-<build-id>.zip
  patch-4.MPQ
  item_template.sql              idempotent/explicit owner-applied statement
  manifest.json                  IDs, paths, versions, SHA-256, member list
  validation-report.json
  validation-report.md
  source/                        params + source GLB/PNG or source master
  compiled/SUI_W_####.m2
  compiled/SUI_W_####_V01.blp
  preview/SUI_W_####.glb
  authoring/uv-guide.png
  authoring/region-*.png
  OWNER_CHECKLIST.md
```

Nico alone applies the SQL, copies the patch into client Data directories, runs
`.reload item_template`, restarts/remounts clients, invokes `.additem`, or changes live
database/runtime state. A future owner-only UI may expose those actions separately and be
disabled by default; none is part of Forge build success or agent-run acceptance.

The real Blizzard 1.12.1 client is the strict judge. MSUIClient is the fast diagnostic
surface, not a substitute: it ignores item-model skinning, suppresses overlapping effect
passes, and does not consume `ItemVisual` effects.

---

## 3. Capability inventory

State labels: **solid** is reusable as-is for this scope; **partial** requires material work or
has a narrower proof; **absent** does not exist.

| capability | state | authoritative note |
|---|---|---|
| Web M2 render read | partial | good for view-0 geometry/preview; lossy and not invertible |
| MSUI M2 read | partial | independently implemented and stricter in places; still a runtime renderer, not a full binary spec |
| raw all-array/all-view M2 inspector | **absent** | Phase 0 blocker |
| M2 particle/emitter parse and byte patch | partial | useful later; not a geometry writer |
| offset-preserving vertex patch | **absent, small** | first Frankenweapon implementation |
| new-geometry M2 writer | **absent** | donor-scaffold writer before any general writer |
| M2 → GLB rigid preview | partial | position/normal/UV0; loses retail-only structural/material behavior |
| direct generated M2+BLP preview | **absent** | must bypass display-ID/retexture resolution and key by content hash |
| ItemDisplayInfo read | solid with comment/consumer fixes | MSUI runtime field-10 SpellVisual read is correct; stale web comments/consumers and MSUI `DbcReader.cs:131` comment must be corrected |
| DBC clone/patch/add-string/sort/write | solid | reuse `DbcWriterService` |
| explicit weapon DBC row creation | **absent** | field 1 plus full deliberate 23-field state |
| BLP DXT1/DXT3/palettized encode + mips | solid | v1 uses 128×64 opaque DXT1 |
| BLP vanilla decode | solid | includes DXT5 read support |
| tier palette variants | solid for whole-image recolor | region-aware variants are later |
| MPQ v1 build and member read-back | solid | separate from live auto-copy side effect |
| authoritative snapshot patch builder | **absent** | must union all row types, package M2, lock, hash, and never deploy |
| custom-M2 persistence/package | **absent** | existing columns are NULL/unused |
| custom entry convention ≥900000 | partial | allocation exists but `MAX+1` is non-atomic |
| custom display convention ≥60000 | partial | proven in-client but allocator excludes weapons and is non-atomic |
| fixed-layout UV guide/masks | **absent** | Phase 4 generator deliverable |
| parametric mesh/UV generator | **absent** | ULTUM recipes are source reference, not integrated code |
| GLB upload/import | **absent** | no bounded multipart endpoint or weapon importer |
| ComfyUI img2img texture path | solid as a later input | not yet atlas/region conditioned |
| web held weapon preview | partial | hand/wrist mounting exists; sheathed attachments 26/27 are not implemented |
| MSUI base weapon renderer | partial | rigid hand mounting works; the entire sheath mapping is unreconciled with core/retail, and item effects/layered fidelity do not |
| MSUI automated weapon provenance/specimens | **absent** | current headless item batch enumerates helms/capes only |
| MSUI live remount | **absent and deferred** | MPQ misses, DBC, model, and texture caches all need invalidation; restart first |
| core gameplay/display pass-through | solid | no C++ geometry work required |
| live DB apply/reload/deploy | owner-only | never part of agent-run build or acceptance |

The project is therefore not merely "one writer, one generator, three small deltas." The
critical path is binary truth → donor proof → writer/compiler → immutable artifact build →
parametric generation, with independent validation throughout.

---

## 4. Pipeline design

```
                          BUILD / STAGING BOUNDARY
┌────────────────────────────────────────────────────────────────────────────┐
│ Weapon Forge                                                               │
│                                                                            │
│ parameters or source GLB + source texture                                  │
│   → RigidWeaponMesh normalization/validation                               │
│   → UV guide + masks                                                       │
│   → donor-scaffold M2 writer                                               │
│   → BLP writer                                                             │
│   → direct content-hashed GLB/mannequin preview                            │
│   → explicit ItemDisplayInfo row + reserved IDs                            │
│   → snapshot patch builder                                                 │
│   → reopen MPQ and validate exact packaged DBC/M2/BLP                      │
│   → immutable owner handoff ZIP                                            │
└────────────────────────────────────────────────────────────────────────────┘
                                  │
                                  │ files/checklist only
                                  ▼
                         OWNER ACCEPTANCE BOUNDARY
            Nico applies SQL / copies patch / reloads core /
             restarts clients / executes player and NPC tests
```

### 4.1 Pure compiler boundary

Implement a pure `WeaponAssetCompiler` with no live DB, RA, client-path, or runtime dependency:

```text
RigidWeaponMesh + WeaponTexture + WeaponCompileOptions
  → M2 bytes
  → BLP bytes
  → preview GLB bytes
  → UV guide (always) + semantic region masks (when region metadata exists)
  → structured diagnostics
```

The compiler accepts exactly one rigid triangle primitive/material for v1. Through Phase 4 it
also accepts **only the golden donor's fixed topology**: 34 stable vertex IDs, 48 triangles, and
the donor's four proven view-local structures. It rejects arbitrary topology until Phase 5 adds
and reference-client-proves variable-topology four-view generation. Independently, it rejects
skins, animations, morph targets, non-triangle primitives, unsupported compression, multiple
materials, non-finite data, invalid indices, and missing UV0. It applies all node transforms
before normalization, preserves stable vertex IDs where applicable, and applies the coordinate,
normal, winding, and UV contract in §2.3 exactly once.

`RigidWeaponMesh` includes optional per-triangle or per-UV-island `regionId` metadata. Route A
supplies semantic labels such as blade/edge/guard/grip/pommel and therefore emits masks. Route
B normally lacks semantic labels and emits only the unconditional UV wireframe unless a later
classifier/operator supplies regions. The compiler never fabricates semantic masks from UVs.

Preview must use the generated byte pair directly. Existing `EnsureGlb` is retexture-specific:
it resolves a custom display back to its original vanilla M2 and injects a replacement BLP;
it ignores custom-M2 bytes. New drafts therefore use a content-hash preview endpoint that
parses `generated.m2 + generated.blp` without requiring installation or display-ID lookup.
The final preview is regenerated from bytes extracted from the finished MPQ.

### 4.2 Source-of-truth model

Do not duplicate an M2 for every texture variant. Use two conceptual records (exact schema is
an implementation choice, but the separation is required):

```text
custom_weapon_model
  model_id, model_mpq_path, compiled_m2, m2_sha256
  source_kind (donor_patch | parametric | glb_trellis)
  source_blob/artifact_store_key, source_sha256
  generator_params_json, seed
  generator_version, writer_version, coordinate_contract_version
  validation_state, validation_report, created_at

custom_weapon_display
  display_id, model_id
  texture_mpq_path, compiled_blp, blp_sha256
  source_texture/master texture, texture params/version
  icon_stem, item_visual, donor_display_id
  explicit 23-field DBC state, validation_state, created_at

custom_weapon_item_manifest (required for publishable owner handoff)
  requested item entry and gameplay fields
  display_id, idempotent SQL text/hash, build_id
```

Store compiled bytes for deterministic rebuilds **and** enough source material to edit or
recompile later. Parameters alone are not reproducible across algorithm, dependency, model,
or weight changes. Route B must retain the source GLB and high-resolution texture; Route A
retains parameters, seed, region masks, and a generator version.

`artifact_store_key` is an opaque controlled-storage identifier or bundle-relative path, never
an uploaded/client-supplied host filesystem path. Normalize and validate it inside the artifact
store; do not persist arbitrary absolute paths.

`custom_weapon_item_manifest` may be omitted only for an intermediate display-only/NPC proof
that produces no `item_template.sql`. Every publishable five-artifact weapon and every owner
handoff ZIP containing SQL requires it. Compile SQL from the captured literal donor-2131
schema/row fixture and its recorded hash; never use a live `INSERT … SELECT` whose donor can
drift between build and owner application.

Use one concrete reservation authority for both namespaces:

```text
custom_id_allocator
  kind PRIMARY KEY             # item_entry | item_display
  next_id

custom_id_reservation
  kind, id PRIMARY KEY
  build_id, slot, state, reserved_at, committed_at
  UNIQUE (build_id, kind, slot)
```

The staging coordinator opens a transaction, locks the allocator row for each kind, advances
`next_id`, inserts reservations, and commits both reservations before compilation. Database
constraints enforce uniqueness. The pure compiler receives already-reserved IDs and never
allocates them. A retry presents the same stable `(build_id, kind, slot)` key and receives the
same reserved ID; it never advances the allocator merely because transport or compilation was
retried. Reservation states are append-auditable/terminal: `reserved` may become `committed`,
`failed`, or `handed_off`, but rows and IDs are never deleted or recycled. Agent tests use an
isolated repository/snapshot, never the live allocator.

### 4.3 Snapshot artifact builder

- Start from a known clean `ItemDisplayInfo.dbc`.
- Snapshot all retexture, atlas, weapon-model, and weapon-display records under one build lock.
- Reserve IDs atomically before serialization.
- Add every unique model once and every display texture once.
- Emit one deliberately populated DBC row per display and one authoritative sorted DBC.
- Build MPQ, reopen it, validate path provenance and exact member bytes, and compute SHA-256.
- Publish an immutable build directory/ZIP. Never build on a GET request, deploy, copy to a
  Data directory, write `item_template`, or issue RA commands.

Determinism requirements:

- use a coordinator-wide keyed/file/database build lock; the current MPQ builder's
  instance-local lock is insufficient across service instances;
- normalize MPQ paths to canonical backslashes and canonical casing, compare paths
  case-insensitively, and fail on duplicates instead of silently overwriting;
- sort source records by stable IDs before string insertion, sort DBC rows, and insert MPQ
  members by canonical path;
- emit canonical UTF-8 manifest JSON with stable property/member ordering and no host-specific
  absolute paths;
- fix or ignore ZIP timestamps and other nondeterministic container metadata when comparing
  content builds;
- record the clean base `ItemDisplayInfo.dbc` SHA-256, client build, compiler/writer/generator
  versions, and canonical path policy in the manifest.

DB-backed source-of-truth storage is compatible with this boundary only when operating on a
development/isolated store during agent testing. Applying records to the live server database
is a distinct Nico-operated action.

---

## 5. Mesh generation: proof route plus two production routes

The ULTUM project (`{ULTUM_ROOT}`, see `docs/HANDOFF.md`) already
built and paid for most of the mesh-generation lessons — on the same hardware this pipeline
will use. Its deterministic recipes and TRELLIS lessons materially change the production
plan, but neither is allowed to hide uncertainty in the M2 writer. All routes terminate in
the same pure `WeaponAssetCompiler`.

### Route 0 — donor clone and fixed-topology Frankenweapon (proof path)

Before a reconstructed-offset writer exists, clone `Sword_1H_Short_A_01.m2` to a unique MPQ
path and patch only known fixed-width vertex fields in its existing 34-vertex block:

- first proof: identical geometry, new model path, new DBC row, and new BLP;
- second proof: modify positions/normals/UV0 while preserving vertex count, view topology,
  offsets, bones, attachments, events, and lookups;
- preserve all four view records byte-for-byte only when deformation remains inside every
  recorded global and per-view submesh center/bounds contract; otherwise, after the record
  layout is proven, patch those fixed-width fields in all four views and validate them without
  moving any section offset;
- if edited through GLB, carry a stable custom vertex-ID attribute/sidecar and fail if any ID
  is lost, duplicated, or reordered.

This route proves custom `ModelName1`, physical `.m2` packaging, Type-2 texture binding,
pivot/orientation, hand/sheath mounting, and both server wire paths without trusting a new
offset serializer. It is a disposable integration proof, not the final generator.

Because Phases 1–2 preserve offsets, their internal M2 name string intentionally remains the
donor name while DBC/MPQ identity changes to `SUI_W_0001`. The validator reports and explicitly
permits that single mismatch for both alias/Frankenweapon proofs. Phase 3 rewrites the internal
name canonically while rebuilding offsets; changing it earlier would invalidate the isolation
test unless a separately proven fixed-width name slot were introduced.

### Route A — deterministic parametric generator (primary path)

- A weapon archetype is a small parameter schema. Sword: blade length / width-curve /
  cross-section (diamond, lens, flat-bevel) / tip (point, clip, round) / fuller on-off;
  guard style + width; grip length/radius; pommel style. Axes/maces/staves/daggers are
  sibling schemas sharing the extrude/lathe primitives.
- **Working geometry recipes already exist**: ULTUM's `pipeline/blender/build_m3_prop_kit.py`
  authors weapons from beveled primitives (`box`/`cylinder`/`prism`/`brace`) —
  `build_sword()` 332 tris, `build_sword(great=True)` 332, `build_axe()` 740,
  `build_staff()` 756, `build_shield()` 332. The sword is squarely in the measured vanilla
  range; the larger recipes remain below the observed global maximum but must be compared
  with their own subclass census. Port the construction math to C#, replace embedded
  material colors with a fixed UV atlas + painted BLP, and test the port against the Blender
  reference output. In-process C# keeps the Forge preview loop fast and dependency-free, but
  bevel, winding, triangulation, normal, and UV behavior require explicit unit tests rather
  than assuming the port is trivial.
- Construction: blade = extruded 2D silhouette (mirrored halves), guard/grip/pommel = lathed
  profiles. Normals from construction, **UVs assigned deterministically to a fixed
  per-archetype atlas**.
- Why this is primary: geometry and UVs are deterministic and constrained by construction,
  the parameter space is batchable, and the fixed atlas turns texturing into a controlled
  region-fill problem instead of an uncontrolled AI one. "Valid by construction" only means
  the generator satisfies the `RigidWeaponMesh` contract; the compiled M2 still passes the
  full validation ladder.
- One ULTUM art-direction lesson applies directly: bare beveled primitives with flat
  materials read as toy-like ("inflatable asset pack" — the rejected M3 world kit). The
  painted texture carries the style; vanilla WoW is exactly low-poly-plus-hand-painted, so
  Route A geometry + a good wrapper is on-style where ULTUM's untextured kit wasn't.

### Route B — TRELLIS.2 image→3D on the A6000 (hero / unique weapons)

The worker at `homeai@192.168.0.201` (key-only SSH alias `homeai-a6000`) runs the proven
ULTUM reconstruction stack:

| what | where |
|---|---|
| TRELLIS.2 — `microsoft/TRELLIS.2-4B` (MIT) — preferred | `/home/homeai/ai-tools/TRELLIS.2`, env `/home/homeai/ai-tools/envs/trellis2` |
| Hunyuan3D-2 — installed, weights license-restricted (non-commercial pending review) — avoid for now | `/home/homeai/ai-tools/Hunyuan3D-2` |
| ComfyUI/FLUX — already wired into `ComfyUIDispatcher` | `http://192.168.0.201:8188` |
| rembg background removal (BRIA RMBG-2.0 disabled) | `CRPG-Ultum/pipeline/homeai/remove_background.py` |
| generation wrapper to adapt for weapons | `CRPG-Ultum/pipeline/homeai/generate_trellis2_characters.py` |
| shared job area | `/home/homeai/ai-work/` (`ultum/` exists; add `wowgen/`) |

Flow: FLUX weapon concept (front-facing, flat background) → rembg → real-alpha RGBA →
TRELLIS.2 at weapon-scale export parameters → GLB with xatlas UVs + baked color texture →
bounded Forge upload → strict GLB ingest/normalization → `WeaponAssetCompiler`.

ULTUM lessons this route must obey (all already paid for):

1. **Never externally collapse-decimate a TRELLIS output.** 1M→23k destroyed surfaces and
   UVs; even 95k–474k targets speckled, holed, and black-patched (HANDOFF "critical failed
   experiment"). **Set `--decimation-target` at the TRELLIS export stage instead** — the
   wrapper passes it into `o_voxel.postprocess.to_glb`, where reduction happens *before*
   xatlas UV parameterization. That path produced clean runtime trees. The former 800–2,000
   weapon target is too high for the measured sword envelope. Start around **200–400
   triangles**, allow about **600** for a hero sword, and initially reject outputs above
   approximately **1,000**. Validate the post-seam vertex count as well as triangle count.
   Start texture export at 256–512 for the source master, then compile to the chosen runtime
   envelope.
2. Inputs must be genuinely transparent front-facing RGBA — the wrapper hard-fails otherwise.
3. Baseline settings: `--seed 42`, `--pipeline-type 1024_cascade`.
4. Nico may run remote jobs under `systemd-run --user` so an SSH drop cannot kill a
   generation. Agents never start that remote process or control its runtime.
5. Wrapper outputs are normalized to a `[-0.5, 0.5]³` AABB. Existing output uses embedded
   WebP (`extension_webp=True`); disable that extension for the first importer and emit PNG,
   unless explicit SharpGLTF/WebP support is proven with fixtures.

The GLB importer is real work, not an existing capability. It must:

- enforce upload size/count/time limits and reject archive/path tricks;
- apply scene/node transforms and choose exactly one visible triangle primitive;
- reject skins, animation, morph targets, lines/points, unsupported Draco/mesh compression,
  missing normals/UV0, and multiple materials unless a deliberate bake/merge exists;
- apply the §2.3 determinant-aware node-transform/winding rule, remove/reject degenerate
  triangles, normalize finite inverse-transpose-transformed normals, and enforce UInt16-safe
  lookup/vertex counts;
- extract the embedded texture, explicitly support or reject WebP, and retain the source;
- orient/scale the mesh to the measured sword convention and place the grip at the origin;
- produce diagnostics rather than silently repairing ambiguous grip/orientation cases.

Weapons avoid character rigging and animation, but "no manifold requirement" is not permission
to accept broken geometry. The renderer may not require a closed collision manifold, while
the compiler still rejects non-finite data, degenerates, invalid winding/normals, unusable
thin surfaces, and malformed UVs. The thin-blade reconstruction risk remains experimentally
unproven.

Dispatch remains owner/operator-driven initially: Nico generates on the A6000 and uploads the
result to the Forge. Only add a bounded authenticated job API beside ComfyUI if repeated hero
generation justifies the extra service. It is not part of the validity arc.

Kitbash-from-stock (segmenting donor blades/hilts) stays parked — more parsing archaeology
than either route for less control.

## 6. Texture generation strategy (the wrapper, layered)

**Route A** owns its UV atlas, so each mesh ships with **region masks** (blade face,
edge, fuller, guard, grip, pommel — rendered as mask PNGs alongside the mesh):

1. **Procedural fill**: per-region material ramps — blade metal gradient along the
   strip, edge highlight, wrapped-leather grip, metal guard/pommel — deterministic, ugly-proof,
   zero AI. Generate at a source-master resolution, dilate island edges into gutters, then
   downsample and encode at the donor-matched runtime envelope.
2. **Tier variants**: `PaletteSwapService` currently accepts a source PNG and emits PNG, not
   BLP directly. Recolor the retained source/master PNG, then run each output through
   `BlpWriterService`; alternatively add an explicit BLP-decode adapter. Type-2 texturing means
   each encoded variant is one more DBC/display row on the same M2.
3. **AI pass**: existing `BuildImg2ImgWorkflow` can be seeded with the procedural fill rather
   than a vanilla texture (initial denoise experiment ≈0.3–0.5). A seed does not guarantee
   atlas compliance. Validate protected gutters/background, compare per-region masks, and
   reject outputs that paint across islands. Add stronger conditioning only if low-denoise
   img2img cannot reliably preserve the layout.

**Route B** weapons arrive **pre-textured**: TRELLIS bakes color onto its own xatlas layout.
Downscale the bake to the stock envelope, optionally img2img-stylize toward hand-painted
vanilla at low denoise, dilate island gutters, then encode BLP. Preserve the original GLB and
high-resolution bake. No semantic region masks exist on this route unless derived later, but
whole-image palette variants remain available.

## 7. Validation ladder — what "valid" means, checkably

Validation is deliberately independent and staged. A same-reader round trip is useful but
never sufficient.

### 7.1 Input `RigidWeaponMesh` validation

- exactly one rigid triangle primitive and one material;
- finite positions, normals, and UV0; normalized nonzero normals;
- triangle index count is a multiple of three;
- no out-of-range or degenerate triangles;
- no skin, animation, morph targets, unsupported compression, lines, or points;
- UV0 in the agreed `[0,1]` policy, with no NaN/Inf; warn/reject unintended overlaps and
  insufficient island guttering;
- canonical right-handed Y-up DTO coordinates, explicit recorded scale/normalization, and the
  determinant-aware normal/winding transform in §2.3; no implicit axis or unit guesses;
- top-left UV/image convention with no writer-side V/row flip, proven against the donor
  checkerboard fixture before accepting generated textures;
- grip/pivot at the measured origin convention, sword long axis +X after normalization;
- through Phase 4, exact golden fixed-topology vertex IDs/counts and donor four-view structures;
  arbitrary topology is a hard rejection until the Phase-5 view generator is proven;
- v1 geometry target 200–400 triangles, hero warning above 600, initial hard policy ceiling
  about 1,000; independently enforce UInt16-safe global and view-local counts.

### 7.2 M2 binary validation

- exact `MD20` magic and v256 output;
- every count/offset/range in bounds, correctly aligned/padded according to the proven donor
  layout, with no overlaps except explicitly shared data;
- exactly four inline views, each structurally complete and geometry-count consistent;
- view triangle indices reference the **view vertex-lookup array**, and every lookup entry
  references a valid global vertex; do not merely compare triangle indices to global nVerts;
- every submesh range is valid and every batch points to valid submesh, render flag, texture,
  texture-unit/coordinate, weight/transparency, and transform lookup entries;
- every vertex is 48 bytes, includes both UV pairs, has finite raw WoW coordinates, has UV1
  exactly `(0,0)` for v1, and has weights summing to 255 with valid bone indices;
- donor-scaffold sequence, bone, lookup, attachment, and event counts match the chosen v1
  contract; attachment IDs 0–4 and their lookup entries resolve;
- Phases 1–2 retain the exact donor internal-name bytes and report the one deliberate mismatch
  with DBC/MPQ identity; Phase 3+ requires a canonical internal name matching the packaged
  `SUI_W_####` model identity;
- one Type-2 texture with empty filename and an opaque base render flag;
- collision arrays empty for the simple-sword scaffold;
- recomputed AABB/center/radius are finite and consistent with donor semantics—never apply the
  disproven `radius >= max distance from world origin` rule;
- parser disagreement in batch layout is resolved before this validator can pass.

The first writer proof is not "current M2Reader structs round-trip." It is:

1. raw donor inspector captures every array and all four views;
2. lossless/raw-preserving donor parse→emit is byte-identical where promised, or differences
   are explicitly enumerated and semantically justified;
3. web and MSUI parsers independently accept the result and agree on key decoded records;
4. donor and re-emitted model render side-by-side;
5. reference client accepts the re-emitted donor before generated topology is attempted.

### 7.3 BLP and DBC validation

- BLP2 header, 128×64 v1 dimensions, DXT1/no-alpha mode, full valid mip chain, and successful
  decode of every packaged mip required by the decoder;
- 92-byte/23-field DBC schema, sorted IDs, string offsets in the string block, unique display
  ID, deliberate values in all fields, `ModelName1` present, `ModelName2` empty, field 9/10
  zero, `ItemVisual=0`;
- DBC model `.mdx` logical name resolves to the packaged `.m2` member and TextureName1 resolves
  to the packaged `.blp` member under the weapon directory;
- SQL/manifest `display_id` exactly matches the DBC row.

### 7.4 Packaged-artifact validation

- reopen the finished MPQ with the same numeric patch-order semantics as the clients;
- verify member provenance, names, sizes, SHA-256, and byte identity against the manifest;
- extract the DBC/M2/BLP, resolve the display join, rerun all semantic validators, and create
  the final GLB preview from these extracted bytes—not from pre-package inputs;
- no build or download operation writes a client Data directory or live database.

### 7.5 Visual/client validation

1. Web: direct generated-byte standalone preview plus mannequin main-hand/off-hand held
   placement. This proves base geometry/UV/pivot only. Web sheathed attachments 26/27 are not
   implemented; add them in the pure-Forge phase or leave sheath proof to MSUI/reference.
2. MSUIClient: implement/build a headless harness with real weapon displays, equipment slots 15/16/17,
   main/off/ranged categories, all core sheath values 0–7, and archive provenance through the
   real numeric `MpqMount` resolver. Reconcile its current partial sheath mapping against the
   reference client. Current helm/cape-only coverage is not sufficient. Nico runs the client
   harness; agent acceptance stops at build plus pure offline validators.
3. Nico-operated fresh MSUI process: remount alone is insufficient because negative MPQ lookups, DBC rows,
   model objects, and textures are all cached. Restart is the reliable initial gate.
4. Owner-run Blizzard client: install patch, apply SQL, reload item templates, restart client,
   `.additem` on a GM, and check dressing room, held, offhand, and sheathed states on at least
   one male and one female character model.
5. Owner-run server-path check: validate both player equip/query and an NPC virtual weapon.

Only after step 5 passes is the weapon marked **reference-client valid**. MSUI success alone
cannot certify bones, enchant attachments, layered retail shaders, or ItemVisual effects.

## 8. Phased plan

Every phase has a build/staging acceptance and, where relevant, a separate Nico-operated live
acceptance. Agent work never crosses the latter boundary.

| phase | build/staging deliverable | acceptance |
|---|---|---|
| **0 — Lock binary/server truth** | Pin authoritative core branch/commit/dirty state; check in non-proprietary census reports/manifests while retaining client payloads only by controlled artifact key/hash; lock/revalidate DBC row 679 and the embedded full donor-2131 schema/row snapshot; fix numeric MPQ ordering; correct stale web and MSUI DBC field-9/10 comments; reconcile M2 batch layout and all sheath values; build raw all-array/all-view inspector; define/prove the coordinate/UV checkerboard contract; bootstrap the isolated transactional reservation registry and an offline direct-M2+BLP preview harness | Core symbols are revalidated at the pinned commit; both M2 parsers/inspector agree on the donor; embedded donor fixture hashes and all four distinct views/scaffold arrays are verified; collision-checked golden item/display IDs are reserved and permanently tombstoned; remaining unknowns are explicit |
| **1 — Golden custom-path proof** | Using those reserved IDs, clone donor bytes unchanged to `SUI_W_0001.m2`; deliberately retain/report the original internal M2 name while changing only DBC/MPQ identity; create explicit DBC row, donor-derived full item SQL, 128×64 DXT1 BLP, manifest, MPQ, and offline packaged-byte preview | Artifact validators pass, owner preflight reconfirms the IDs are unoccupied, then **required Nico-operated gate:** custom ModelName1/Type-2 binding renders on both clients before Phase 2 |
| **2 — Fixed-layout Frankenweapon** | Offset-preserving edits to the donor's 34 positions/normals/UV0 with stable vertex IDs; preserve UV1=`(0,0)` and all topology/offsets; remain within all preserved global/per-view bounds or patch every proven fixed-width bounds field | Visibly novel but deliberately ugly sword passes packaged validators; Nico checks grip, main/offhand, sheath, player query, and NPC virtual path |
| **3 — Donor-scaffold writer** | Lossless/raw-preserving document model where required; canonical custom internal name; four-view static-sword writer; recomputed offsets/bounds; strict structured diagnostics | Re-emitted donor passes independent parsers, then Nico proves it in the reference client; generated fixed-topology equivalent passes the same ladder |
| **4 — Pure fixed-topology Forge/compiler** | Fixed-topology `RigidWeaponMesh` compiler for the golden 34-vertex/48-triangle donor structure only; promote the Phase-0 preview harness into a direct content-hash web endpoint; optional web sheath preview; model/display records; production coordinator integration for the existing reservation registry; deterministic snapshot builder; immutable handoff ZIP; no live side effects | Arbitrary topology is rejected; rebuild is deterministic by hash; weapon-only artifact builds; download is side-effect free; exact MPQ bytes drive final preview |
| **5 — Parametric sword generator** | Port/test ULTUM sword construction in C#; parameter schema; fixed UV atlas; region masks; procedural fill; generate and validate four view-local lookup/index/submesh/batch structures for variable topology | Parameter changes produce 200–400-triangle swords that pass compile/package validation; Nico accepts at least three silhouettes in reference client |
| **6 — Texture/display variants** | One shared M2 with multiple `custom_weapon_display` rows; palette tiers; protected-gutter img2img experiment; optional icon generation | Variant family proves one-model/many-Type-2-textures without path collisions or duplicated M2 bytes |
| **7 — TRELLIS hero pilot** | Bounded GLB upload/import; PNG texture path; export-stage decimation around 200–400 (≤600 hero, ~1000 ceiling); normalization diagnostics; retained source GLB/master | One AI-concepted thin-blade weapon passes the exact same compiler and owner acceptance ladder; no special-case M2 path |
| **8 — Breadth and polish** | Axe/mace/dagger/staff, subclass-specific donors/budgets, shield folder variant, sheath tuning; later enchant attachments/ItemVisual | Each family receives its own measured contract and reference-client specimen; no generalization from sword without evidence |

Why Frankenweapon precedes the writer: it proves model naming, M2 packaging, DBC binding,
texture wrapping, pivot, attachment placement, patch precedence, and both core packet paths
without reconstructing a single offset. If it fails, the fault is in the surrounding join;
if a later writer fails, the fault is isolated to binary serialization.

## 9. Later: armor, glow, and friends

- **Armor is different, not globally easier.** Chest/legs/hands/feet/waist are usually body-
  atlas components (fields 14–21) composited onto race/sex character textures, and the
  existing `BodyAtlasTextureService`/palettized encoder provide substantial groundwork. Full
  armor validity still involves geosets, robe rules, race/sex variation, slot composition,
  and texture seams. Helms and shoulders are attached M2s but have race/sex/model-pair and
  hiding rules that prevent a blind reuse of the sword scaffold.
- **Shields** are rigid attached models under `Item\ObjectComponents\Shield` and are the
  closest follow-on to weapons; they reuse the compiler with a different folder, donor,
  bounds/pivot, wrist/back attachments, and gameplay inventory/sheath contract.
- **Enchant glow / ItemVisual (field 22)** needs correctly placed weapon attachment points,
  ItemVisuals/ItemVisualEffects wiring, and reference-client shader/effect validation. MSUI
  currently parses ItemVisual but does not render it. Preserve donor attachment anatomy now;
  enable the field only in a later explicit effect phase.
- **Ranged weapon quirks** (bows draw/flex — animated) and **fist weapons** (paired models):
  park until the static-mesh families work.

## 10. Remaining open questions and blockers

Resolved and no longer open: weapons use four inline views in the measured corpus; swords run
along raw +X with grip near X=0; the broad geometry/BLP envelopes are measured; simple swords
use no collision; `patch=0` rows are visible at all normal active patches; the physical model
member is `.m2` while the DBC logical name is `.mdx`.

Phase 0 blockers:

1. **Authoritative-input refresh:** commit `dc7ebbbe…` and gameplay donor 2131 are pinned here,
   with the complete literal donor schema/row streams embedded in §13.3. At Phase 0, reproduce
   their hashes against the current authoritative source and record/resolve any changed core
   commit, dirty state, schema, or row before proceeding; do not silently replace the fixture.
2. **Batch record truth:** resolve the web/MSUI disagreement at batch offsets `+18..+22`
   against raw donor bytes and one external/spec reference if necessary. No writer before this.
3. **MPQ/sheath parity:** consolidate web, MSUI, and diagnostic-tool numeric archive ordering,
   and reconcile MSUI's partial sheath mapping with core values 0–7 and retail behavior,
   before accepting provenance or sheath diagnostics.

Phase 2/3 blockers:

4. **Lossless document scope:** decide which arrays the donor-scaffold writer deliberately
   recreates and which raw donor structures it preserves. Enumerate every difference from the
   golden donor; no silent drops.
5. **Bounds/radius rule:** derive the correct static-sword center/radius calculation from donor
   values and confirm it on multiple simple swords. Resolve before Phase 2 if deformation can
   leave any global or per-view submesh center/bounds contract; otherwise constrain Phase 2
   inside every preserved bound and resolve before the Phase 3 writer. Do not use origin-based
   max distance.
6. **Event/attachment semantics:** identify the two golden-donor events and confirm whether a
   new static sword may preserve them unchanged. Keep ItemVisual zero during this work.

Phase 5 variable-topology blocker:

7. **Variable-topology views:** the golden views are confirmed distinct. Determine the
   canonical view-generation/LOD policy for arbitrary parametric topology and prove it in the
   reference client; fixed-topology phases preserve donor views unchanged.

Later Route B questions:

8. Minimum TRELLIS export-stage decimation target that preserves a thin blade without holes,
   thickness inflation, broken normals, or unusable UVs.
9. Whether a high-resolution TRELLIS bake remains readable after donor-envelope downsampling,
   and whether stylization is consistently necessary.
10. Whether PCA plus grip-end heuristics can orient/scale hero weapons reliably; ambiguous cases
   must fall back to an explicit owner-set grip marker/axis rather than guessing.
11. Whether SharpGLTF reliably exposes the chosen custom vertex attributes and embedded PNG;
    keep WebP disabled until fixtures prove the extension path.

Non-blocking policy questions such as exact hero triangle allowance must be decided from
reference-client specimens, not used to delay the donor validity arc.

## 11. Owner acceptance checklist template

The Forge includes this checklist in every handoff bundle. Codex prepares it but does not run
the live steps.

1. Verify build ID, manifest SHA-256, MPQ member list, SQL target entry/display IDs, and the
   validation report status.
2. Back up/retain the currently installed patch and relevant owner-managed database state
   according to Nico's normal procedure.
3. Apply the supplied `item_template.sql` or use the explicit owner UI; confirm affected row
   and `display_id`.
4. Before copying, run the read-only client-archive preflight with the shared numeric comparator. Inventory
   every installed archive that provides `DBFilesClient\ItemDisplayInfo.dbc`, identify the
   winning provider, and resolve any higher-priority archive that would shadow this build.
5. Copy the supplied `patch-4.MPQ` to each intended client Data directory.
6. Before starting/restarting a client, run the read-only MSUI `MpqMount` resolver against the
   installed directory. Require the DBC, packaged M2, and packaged BLP to resolve from the
   intended archive and exactly match all three manifest SHA-256 values. Stop on any shadowing
   or byte mismatch.
7. Run `.reload item_template` or perform Nico's chosen server lifecycle step.
8. Fully restart the Blizzard client and MSUIClient so MPQ, DBC, model, texture, and negative
   lookup caches are cold.
9. On a GM, run `.additem <entry>` and verify query/name/icon data.
10. Check main hand, offhand where semantically allowed, held and sheathed states, and dressing
   view on at least one male and one female character.
11. Assign the same display/metadata through an NPC virtual weapon and verify that path.
12. Record screenshots/logs, client build, build ID, pass/fail reason, and any culling/pivot/
     texture/effect discrepancy back into the weapon model's validation record.

## 12. Key implementation touchpoints

Web app (`MangosSuperUI`):

- `Services/M2Handlers/M2Reader.cs` — partial v256 reader; expand raw inspection separately.
- `Services/GlbWriter.cs` — rigid position/normal/UV0 preview only.
- `Services/BlpWriterService.cs` and `Services/SuperUiMPQ/BlpDecoder.cs` — runtime texture codec.
- `Services/DbcService.cs:461-475,742-765` — conflicting ItemDisplayInfo comments; correct to
  raw-proven field 9 flags / field 10 SpellVisual before relying on either.
- `Services/DbcWriterService.cs` — clone/patch/string append/sort/serialize.
- `Services/ItemServices/ItemTextureService.cs` — existing extraction/retexture preview; do not
  route brand-new custom M2 drafts through its original-display fallback.
- `Services/ItemServices/ItemRetextureService.cs:1061-1240` — current DBC/MPQ rebuild, M2 omission,
  early-return, and live auto-copy hazards to separate.
- `Services/SuperUiMPQ/MpqBuilderService.cs` — reusable verified MPQ writer.
- `Controllers/ItemsController.cs` — item allocation/save and download paths; current MAX+1
  allocation and build-on-download behavior require redesign.
- `wwwroot/js/character-viewer/equip.js` — direct hand mounting and mannequin preview.
- `wwwroot/lib/three/examples/jsm/utils/UVsDebug.js` — available UV-guide rendering helper.

MSUIClient:

- `Formats/M2Reader.cs` — independent M2 interpretation and corrected batch-tail candidate.
- `Formats/DbcReader.cs` — 23-field DBC reader; its runtime field-10 SpellVisual mapping is
  correct, but the field-9/10 comment at line 131 is stale and must be corrected in Phase 0.
- `Formats/MpqMount.cs` — runtime numeric archive precedence, supplier provenance, and caches.
- `World/Units/AttachedItemRenderer.cs` — model/texture resolution, rigid hand/sheath mounting,
  and known single-base-pass limitations.
- `GameLoop/Dev/GameLoop.VariantBatch.cs` — extend helm/cape-only headless coverage with weapon
  slots, states, characters, and packaged display IDs.

Core paths (verify in the pinned authoritative homeserver fork; local `.reference-vmangos-core`
is stock reference only):

- `src/game/ObjectMgr.cpp` — item-template patch selection and metadata load.
- `src/game/Handlers/ItemHandler.cpp` — player item-query response.
- `src/game/Objects/Player.cpp` — visible-item/player path.
- `src/game/Objects/Creature.cpp` — NPC virtual-weapon display-direct path.
- `src/game/Database/DBCStores.cpp` — ItemDisplayInfo load intentionally disabled.

The C++ core needs no geometry feature for v1. Any future manifest/hash handshake is an
optional diagnostics feature after the normal client-patch path is proven, not a dependency.

## 13. PC-swap and reproducibility handoff

### 13.1 Immediate portability warning

The initial audit found `WEAPON_GEN.md` untracked. The requested handoff commit must track it;
verify with `git ls-files --error-unmatch WEAPON_GEN.md` and `git log -1 -- WEAPON_GEN.md`
before abandoning this checkout. The root `tools/` tree and `WORLD_STATE.md` are intentionally
owner-local, ignored, and removed from the public repository tip; they require a separate
private transfer if Nico wants them on the next PC.

Pinned working-copy state before the handoff commit on 2026-08-17:

| component | sanitized origin | branch / HEAD | relevant dirty or untracked state |
|---|---|---|---|
| MangosSuperUI | `github.com/Yafrovon/MangosSuperUI.git` | `main` / baseline `7cc213466f9e4b88f55bb164e6e5b6771d0789af` | initial state: untracked `WEAPON_GEN.md`, `CMANGOS_PORT.md`, and `tools/port/`; handoff commit tracks both plans, while root `tools/` and `WORLD_STATE.md` remain owner-local via `.gitignore` |
| MSUIClient | `github.com/Yafrovon/MSUIClient` | `main` / `f8b1d19f036d7fec4772c4d72021b0f9bc4661a4` | 0 staged; modified `vantages.json`; preserve as unrelated owner work |
| ULTUM | `github.com/Yafrovon/CRPG-Ultum.git` | `main` / `13df086f05077ef597d3772836081675343e51c8` | heavily dirty: 19 tracked changes and 54 untracked status entries; required `docs/HANDOFF.md` and `pipeline/blender/build_m3_prop_kit.py` are untracked |
| SuperUI-Core | `github.com/Yafrovon/SuperUI-Core.git` | `development` / `dc7ebbbe7f06d934860f8e67e595c31dfe34a2bf` | unrelated SuperUiBots changes and `.prefinding018` listed in source-provenance section |

Required action before switching PCs:

1. Verify the handoff commit contains `WEAPON_GEN.md`, `CMANGOS_PORT.md`, and the owner-local
   ignore/removal policy without publishing `tools/` or `WORLD_STATE.md`.
2. Commit or separately export ULTUM's `docs/HANDOFF.md` and
   `pipeline/blender/build_m3_prop_kit.py`; Git will not carry them in their current state.
3. Record sanitized clone origins and verify each transfer by commit/file SHA-256.
4. Transfer owner worktrees with dirty-state manifests or patches; do not discard unrelated
   changes and do not use destructive reset/checkout commands.
5. Provision SSH private keys and credentials separately through Nico's secure mechanism.
   Never add keys, passwords, connection strings, or RA credentials to Git or the handoff ZIP.

Required ULTUM reference fingerprints on the current PC:

| file | size | SHA-256 |
|---|---:|---|
| `{ULTUM_ROOT}\docs\HANDOFF.md` | 25,925 bytes | `832a674305d3af2fe33d66778fa833619b8e44fa7f5255af2d8e543d06d5258f` |
| `{ULTUM_ROOT}\pipeline\blender\build_m3_prop_kit.py` | 15,228 bytes | `ab303c8e0aeced6017dbfeeafd579101016291a99163d8e18479e1684e16fce5` |

### 13.2 Configuration to recreate on the next PC

Record names and sanitized endpoints in the transfer manifest; provision secret values outside
the repository:

- `{SUPERUI_ROOT}`, `{MSUI_ROOT}`, `{MSUI_DATA_ROOT}`, `{ULTUM_ROOT}`, `{ARTIFACT_ROOT}`;
- MSUI checkout and `GameData\Data` roots;
- `Vmangos:DbcPath`, `Vmangos:ClientDataPath`, patch output/source-MPQ paths;
- Admin/Mangos/Characters/Realmd/Logs DB connection-string settings;
- `RemoteAccess:*`/RA endpoint settings;
- `SpellCreator:ComfyUI:*` and any ComfyUI dispatcher endpoint names;
- authoritative-core SSH host alias/user/root and separately provisioned travel key;
- ULTUM/TRELLIS worker alias, controlled job-area key, and model/environment identifiers;
- artifact-store implementation/root, path-canonicalization version, and retention policy.

No compiled record may contain a machine-specific absolute path. Store opaque artifact keys,
configured logical roots, and relative bundle paths.

### 13.3 Phase-0 evidence that prose alone cannot replace

Before leaving the current PC, export or regenerate and verify the following **non-proprietary
reports/fixtures**. Proprietary client binaries stay in Nico's controlled artifact store and
are referenced only by hash/key plus an extraction recipe:

- client build/version and source MPQ archive inventory with archive SHA-256;
- clean base `ItemDisplayInfo.dbc` provider, row/field/record counts, SHA-256, and extraction
  command;
- golden M2/BLP provider archives, canonical MPQ paths, sizes, SHA-256, and extraction command;
- full all-four-view/raw-array golden-donor report and the 571-model/3,730-BLP census output;
- complete `item_template` schema plus literal donor-2131 patch-0 row fixture and fixture hash;
- parser/comparator/census tool versions and commands used to produce those reports;
- sanitized authoritative-core symbol/line report at the pinned commit.

Check non-copyright reports, schemas, and extraction scripts into the project. Store M2, BLP,
DBC, MPQ, source masters, and other proprietary payloads only in the controlled artifact store
or owner transfer bundle, with hashes recorded here/manifests.

Current reproducibility gap: the 2026-08-17/18 corpus numbers were produced by ephemeral
read-only probes plus the compiled MSUI `MpqMount`; there is **no tracked donor/census command
or non-proprietary raw-array report in either repository yet**. The fingerprints below preserve
what was measured, but prose and hashes alone do not satisfy Phase 0. Before Phase 1, add the
tracked audit project at `{MSUI_APP}\DevTools\WeaponGenAudit\WeaponGenAudit.csproj` and commit its
non-proprietary output beneath `{SUPERUI_ROOT}\docs_full\weapon-gen\reports\<audit-id>\`. The
required stable command contract is:

```powershell
dotnet run --project "{MSUI_APP}\DevTools\WeaponGenAudit\WeaponGenAudit.csproj" -- `
  corpus --data "{MSUI_DATA_ROOT}" `
  --report "{SUPERUI_ROOT}\docs_full\weapon-gen\reports\<audit-id>" `
  --no-extract-client-payloads

dotnet run --project "{MSUI_APP}\DevTools\WeaponGenAudit\WeaponGenAudit.csproj" -- `
  donor --data "{MSUI_DATA_ROOT}" `
  --dbc "DBFilesClient\ItemDisplayInfo.dbc" `
  --model "ITEM\ObjectComponents\WEAPON\Sword_1H_Short_A_01.m2" `
  --texture "ITEM\ObjectComponents\WEAPON\Sword_1H_Short_A_01Blue.blp" `
  --artifact-store "{ARTIFACT_ROOT}" `
  --report "{SUPERUI_ROOT}\docs_full\weapon-gen\reports\<audit-id>"
```

Those commands are a required implementation interface, not a claim that the project exists
today. Each report manifest records sanitized logical-root arguments, audit-tool source commit,
built executable hash, MSUI source commit, numeric comparator version, exact provider archive
manifest, full command/exit status, client-build claim and its verification status, and every
report/payload hash or controlled artifact key. The corpus command emits no client payloads;
the donor command may copy proprietary bytes only into `{ARTIFACT_ROOT}`, never the tracked
report directory. Until this tool and report set exist, mark the Phase-0 portability gate
incomplete.

Authoritative donor-2131 snapshot already captured read-only at `2026-08-18T02:16:39Z`:

| property | value |
|---|---|
| database/server | `mangos`; MariaDB `10.11.14` |
| selector | `entry=2131 AND patch=0`; exactly one matching row |
| schema width | 130 columns |
| `SHOW CREATE TABLE` stdout | 7,097 bytes / 134 LF-terminated lines; SHA-256 `30b346649bbf62bd464155f6d3b13ac710a7dc66a62aaa04c00e1fa8c7679e8e` |
| ordered header + complete row TSV stdout | 1,925 bytes / 2 LF-terminated lines; SHA-256 `dfd89aacfc4704a05a58ccd5b570df76ad5f23171864f3c2275f243c4cc2477e` |

The exact literal fixture streams are embedded below. Reconstruct them as UTF-8 without BOM,
with LF newlines: the bytes between each opening and closing fence, including the final LF
before the closing fence, are the authoritative payload.

`SHOW CREATE TABLE item_template` stdout — SHA-256
`30b346649bbf62bd464155f6d3b13ac710a7dc66a62aaa04c00e1fa8c7679e8e`:

```text
item_template	CREATE TABLE `item_template` (
  `entry` mediumint(8) unsigned NOT NULL DEFAULT 0,
  `patch` tinyint(3) unsigned NOT NULL DEFAULT 0 COMMENT 'Content patch in which this exact version of the entry was added',
  `class` tinyint(3) unsigned NOT NULL DEFAULT 0,
  `subclass` tinyint(3) unsigned NOT NULL DEFAULT 0,
  `name` varchar(255) NOT NULL DEFAULT '',
  `description` varchar(255) NOT NULL DEFAULT '',
  `display_id` mediumint(8) unsigned NOT NULL DEFAULT 0,
  `quality` tinyint(3) unsigned NOT NULL DEFAULT 0,
  `flags` int(10) unsigned NOT NULL DEFAULT 0,
  `buy_count` tinyint(3) unsigned NOT NULL DEFAULT 1,
  `buy_price` int(10) unsigned NOT NULL DEFAULT 0,
  `sell_price` int(10) unsigned NOT NULL DEFAULT 0,
  `inventory_type` tinyint(3) unsigned NOT NULL DEFAULT 0,
  `allowable_class` mediumint(9) NOT NULL DEFAULT -1,
  `allowable_race` mediumint(9) NOT NULL DEFAULT -1,
  `item_level` tinyint(3) unsigned NOT NULL DEFAULT 0,
  `required_level` tinyint(3) unsigned NOT NULL DEFAULT 0,
  `required_skill` smallint(5) unsigned NOT NULL DEFAULT 0,
  `required_skill_rank` smallint(5) unsigned NOT NULL DEFAULT 0,
  `required_spell` smallint(5) unsigned NOT NULL DEFAULT 0,
  `required_honor_rank` mediumint(8) unsigned NOT NULL DEFAULT 0,
  `required_city_rank` mediumint(8) unsigned NOT NULL DEFAULT 0,
  `required_reputation_faction` smallint(5) unsigned NOT NULL DEFAULT 0,
  `required_reputation_rank` smallint(5) unsigned NOT NULL DEFAULT 0,
  `max_count` smallint(5) unsigned NOT NULL DEFAULT 0,
  `stackable` smallint(5) unsigned NOT NULL DEFAULT 1,
  `container_slots` tinyint(3) unsigned NOT NULL DEFAULT 0,
  `stat_type1` tinyint(3) unsigned NOT NULL DEFAULT 0,
  `stat_value1` smallint(6) NOT NULL DEFAULT 0,
  `stat_type2` tinyint(3) unsigned NOT NULL DEFAULT 0,
  `stat_value2` smallint(6) NOT NULL DEFAULT 0,
  `stat_type3` tinyint(3) unsigned NOT NULL DEFAULT 0,
  `stat_value3` smallint(6) NOT NULL DEFAULT 0,
  `stat_type4` tinyint(3) unsigned NOT NULL DEFAULT 0,
  `stat_value4` smallint(6) NOT NULL DEFAULT 0,
  `stat_type5` tinyint(3) unsigned NOT NULL DEFAULT 0,
  `stat_value5` smallint(6) NOT NULL DEFAULT 0,
  `stat_type6` tinyint(3) unsigned NOT NULL DEFAULT 0,
  `stat_value6` smallint(6) NOT NULL DEFAULT 0,
  `stat_type7` tinyint(3) unsigned NOT NULL DEFAULT 0,
  `stat_value7` smallint(6) NOT NULL DEFAULT 0,
  `stat_type8` tinyint(3) unsigned NOT NULL DEFAULT 0,
  `stat_value8` smallint(6) NOT NULL DEFAULT 0,
  `stat_type9` tinyint(3) unsigned NOT NULL DEFAULT 0,
  `stat_value9` smallint(6) NOT NULL DEFAULT 0,
  `stat_type10` tinyint(3) unsigned NOT NULL DEFAULT 0,
  `stat_value10` smallint(6) NOT NULL DEFAULT 0,
  `delay` smallint(5) unsigned NOT NULL DEFAULT 1000,
  `range_mod` float NOT NULL DEFAULT 0,
  `ammo_type` tinyint(3) unsigned NOT NULL DEFAULT 0,
  `dmg_min1` float NOT NULL DEFAULT 0,
  `dmg_max1` float NOT NULL DEFAULT 0,
  `dmg_type1` tinyint(3) unsigned NOT NULL DEFAULT 0,
  `dmg_min2` float NOT NULL DEFAULT 0,
  `dmg_max2` float NOT NULL DEFAULT 0,
  `dmg_type2` tinyint(3) unsigned NOT NULL DEFAULT 0,
  `dmg_min3` float NOT NULL DEFAULT 0,
  `dmg_max3` float NOT NULL DEFAULT 0,
  `dmg_type3` tinyint(3) unsigned NOT NULL DEFAULT 0,
  `dmg_min4` float NOT NULL DEFAULT 0,
  `dmg_max4` float NOT NULL DEFAULT 0,
  `dmg_type4` tinyint(3) unsigned NOT NULL DEFAULT 0,
  `dmg_min5` float NOT NULL DEFAULT 0,
  `dmg_max5` float NOT NULL DEFAULT 0,
  `dmg_type5` tinyint(3) unsigned NOT NULL DEFAULT 0,
  `block` mediumint(8) unsigned NOT NULL DEFAULT 0,
  `armor` smallint(5) NOT NULL DEFAULT 0,
  `holy_res` smallint(5) NOT NULL DEFAULT 0,
  `fire_res` smallint(5) NOT NULL DEFAULT 0,
  `nature_res` smallint(5) NOT NULL DEFAULT 0,
  `frost_res` smallint(5) NOT NULL DEFAULT 0,
  `shadow_res` smallint(5) NOT NULL DEFAULT 0,
  `arcane_res` smallint(5) NOT NULL DEFAULT 0,
  `spellid_1` smallint(5) unsigned NOT NULL DEFAULT 0,
  `spelltrigger_1` tinyint(3) unsigned NOT NULL DEFAULT 0,
  `spellcharges_1` tinyint(4) NOT NULL DEFAULT 0,
  `spellppmrate_1` float NOT NULL DEFAULT 0,
  `spellcooldown_1` int(11) NOT NULL DEFAULT -1,
  `spellcategory_1` smallint(5) unsigned NOT NULL DEFAULT 0,
  `spellcategorycooldown_1` int(11) NOT NULL DEFAULT -1,
  `spellid_2` smallint(5) unsigned NOT NULL DEFAULT 0,
  `spelltrigger_2` tinyint(3) unsigned NOT NULL DEFAULT 0,
  `spellcharges_2` tinyint(4) NOT NULL DEFAULT 0,
  `spellppmrate_2` float NOT NULL DEFAULT 0,
  `spellcooldown_2` int(11) NOT NULL DEFAULT -1,
  `spellcategory_2` smallint(5) unsigned NOT NULL DEFAULT 0,
  `spellcategorycooldown_2` int(11) NOT NULL DEFAULT -1,
  `spellid_3` smallint(5) unsigned NOT NULL DEFAULT 0,
  `spelltrigger_3` tinyint(3) unsigned NOT NULL DEFAULT 0,
  `spellcharges_3` tinyint(4) NOT NULL DEFAULT 0,
  `spellppmrate_3` float NOT NULL DEFAULT 0,
  `spellcooldown_3` int(11) NOT NULL DEFAULT -1,
  `spellcategory_3` smallint(5) unsigned NOT NULL DEFAULT 0,
  `spellcategorycooldown_3` int(11) NOT NULL DEFAULT -1,
  `spellid_4` smallint(5) unsigned NOT NULL DEFAULT 0,
  `spelltrigger_4` tinyint(3) unsigned NOT NULL DEFAULT 0,
  `spellcharges_4` tinyint(4) NOT NULL DEFAULT 0,
  `spellppmrate_4` float NOT NULL DEFAULT 0,
  `spellcooldown_4` int(11) NOT NULL DEFAULT -1,
  `spellcategory_4` smallint(5) unsigned NOT NULL DEFAULT 0,
  `spellcategorycooldown_4` int(11) NOT NULL DEFAULT -1,
  `spellid_5` smallint(5) unsigned NOT NULL DEFAULT 0,
  `spelltrigger_5` tinyint(3) unsigned NOT NULL DEFAULT 0,
  `spellcharges_5` tinyint(4) NOT NULL DEFAULT 0,
  `spellppmrate_5` float NOT NULL DEFAULT 0,
  `spellcooldown_5` int(11) NOT NULL DEFAULT -1,
  `spellcategory_5` smallint(5) unsigned NOT NULL DEFAULT 0,
  `spellcategorycooldown_5` int(11) NOT NULL DEFAULT -1,
  `bonding` tinyint(3) unsigned NOT NULL DEFAULT 0,
  `page_text` mediumint(8) unsigned NOT NULL DEFAULT 0,
  `page_language` tinyint(3) unsigned NOT NULL DEFAULT 0,
  `page_material` tinyint(3) unsigned NOT NULL DEFAULT 0,
  `start_quest` mediumint(8) unsigned NOT NULL DEFAULT 0,
  `lock_id` mediumint(8) unsigned NOT NULL DEFAULT 0,
  `material` tinyint(4) NOT NULL DEFAULT 0,
  `sheath` tinyint(3) unsigned NOT NULL DEFAULT 0,
  `random_property` mediumint(8) unsigned NOT NULL DEFAULT 0,
  `set_id` mediumint(8) unsigned NOT NULL DEFAULT 0,
  `max_durability` smallint(5) unsigned NOT NULL DEFAULT 0,
  `area_bound` mediumint(8) unsigned NOT NULL DEFAULT 0,
  `map_bound` smallint(6) NOT NULL DEFAULT 0,
  `duration` int(11) unsigned NOT NULL DEFAULT 0,
  `bag_family` mediumint(9) NOT NULL DEFAULT 0,
  `disenchant_id` mediumint(8) unsigned NOT NULL DEFAULT 0,
  `food_type` tinyint(3) unsigned NOT NULL DEFAULT 0,
  `min_money_loot` int(10) unsigned NOT NULL DEFAULT 0,
  `max_money_loot` int(10) unsigned NOT NULL DEFAULT 0,
  `wrapped_gift` mediumint(8) unsigned NOT NULL DEFAULT 0,
  `extra_flags` tinyint(1) unsigned NOT NULL DEFAULT 0,
  `other_team_entry` int(11) unsigned DEFAULT 1,
  PRIMARY KEY (`entry`,`patch`),
  KEY `items_index` (`class`)
) ENGINE=MyISAM DEFAULT CHARSET=utf8mb3 COLLATE=utf8mb3_general_ci ROW_FORMAT=FIXED COMMENT='Item System'
```

Ordered header + complete donor-row TSV stdout — SHA-256
`dfd89aacfc4704a05a58ccd5b570df76ad5f23171864f3c2275f243c4cc2477e`:

```tsv
entry	patch	class	subclass	name	description	display_id	quality	flags	buy_count	buy_price	sell_price	inventory_type	allowable_class	allowable_race	item_level	required_level	required_skill	required_skill_rank	required_spell	required_honor_rank	required_city_rank	required_reputation_faction	required_reputation_rank	max_count	stackable	container_slots	stat_type1	stat_value1	stat_type2	stat_value2	stat_type3	stat_value3	stat_type4	stat_value4	stat_type5	stat_value5	stat_type6	stat_value6	stat_type7	stat_value7	stat_type8	stat_value8	stat_type9	stat_value9	stat_type10	stat_value10	delay	range_mod	ammo_type	dmg_min1	dmg_max1	dmg_type1	dmg_min2	dmg_max2	dmg_type2	dmg_min3	dmg_max3	dmg_type3	dmg_min4	dmg_max4	dmg_type4	dmg_min5	dmg_max5	dmg_type5	block	armor	holy_res	fire_res	nature_res	frost_res	shadow_res	arcane_res	spellid_1	spelltrigger_1	spellcharges_1	spellppmrate_1	spellcooldown_1	spellcategory_1	spellcategorycooldown_1	spellid_2	spelltrigger_2	spellcharges_2	spellppmrate_2	spellcooldown_2	spellcategory_2	spellcategorycooldown_2	spellid_3	spelltrigger_3	spellcharges_3	spellppmrate_3	spellcooldown_3	spellcategory_3	spellcategorycooldown_3	spellid_4	spelltrigger_4	spellcharges_4	spellppmrate_4	spellcooldown_4	spellcategory_4	spellcategorycooldown_4	spellid_5	spelltrigger_5	spellcharges_5	spellppmrate_5	spellcooldown_5	spellcategory_5	spellcategorycooldown_5	bonding	page_text	page_language	page_material	start_quest	lock_id	material	sheath	random_property	set_id	max_durability	area_bound	map_bound	duration	bag_family	disenchant_id	food_type	min_money_loot	max_money_loot	wrapped_gift	extra_flags	other_team_entry
2131	0	2	7	Shortsword		22075	1	0	1	54	10	13	-1	-1	3	1	0	0	0	0	0	0	0	0	1	0	0	0	0	0	0	0	0	0	0	0	0	0	0	0	0	0	0	0	0	0	2600	0	0	2	4	0	0	0	0	0	0	0	0	0	0	0	0	0	0	0	0	0	0	0	0	0	0	0	0	0	-1	0	-1	0	0	0	0	-1	0	-1	0	0	0	0	-1	0	-1	0	0	0	0	-1	0	-1	0	0	0	0	0	0	0	0	0	0	0	0	0	1	3	0	0	20	0	0	0	0	0	0	0	0	0	0	1
```

The row hash covers all 130 ordered column names and all 130 values. Reproduce it read-only on
the homeserver without printing credentials:

```bash
wg_cfg=/home/wowvmangos/vmangos/run/etc/mangosd.conf
wg_info=$(grep -m1 "^[[:space:]]*WorldDatabase.Info" "$wg_cfg")
wg_info=${wg_info#*\"}
wg_info=${wg_info%%\"*}

wg_host=${wg_info%%;*}
wg_rest=${wg_info#*;}
wg_port=${wg_rest%%;*}
wg_rest=${wg_rest#*;}
wg_user=${wg_rest%%;*}
wg_rest=${wg_rest#*;}
wg_pass=${wg_rest%%;*}
wg_db=${wg_rest#*;}

export MYSQL_PWD="$wg_pass"

mysql --protocol=tcp -h "$wg_host" -P "$wg_port" -u "$wg_user" -D "$wg_db" \
  -N -B -r -e "SHOW CREATE TABLE item_template;"

mysql --protocol=tcp -h "$wg_host" -P "$wg_port" -u "$wg_user" -D "$wg_db" \
  -B -r --column-names \
  -e "SELECT * FROM item_template WHERE entry=2131 AND patch=0 ORDER BY patch;"

unset MYSQL_PWD wg_pass wg_info wg_rest wg_host wg_port wg_user wg_db wg_cfg
```

Pipe each exact MySQL command's LF-normalized stdout to `sha256sum` to compare with the hashes
above. Do not store the extracted password or raw connection string in a report.

Client corpus fingerprints, verified read-only through the compiled MSUI `MpqMount` numeric
priority resolver and in-memory SHA-256:

| asset/provider | bytes / structure | SHA-256 |
|---|---|---|
| clean final-1.12.1 `DBFilesClient\ItemDisplayInfo.dbc` from `patch.MPQ` | 3,059,840; WDBC 29,602 rows × 23 fields × 92 bytes; 336,436-byte string block | `181f775e4d1cfd20b4db91de531b1dce02b03bba498efcd5d6ea79c9cb1dd244` |
| lower/base `ItemDisplayInfo.dbc` from `dbc.MPQ` — **not** the final clean baseline | 2,141,933; 20,753 rows | `ce317f7101d0a1bcffbc65688114cd8d441e76ef06d870bb55135dcff0d6808f` |
| numbered `ItemDisplayInfo.dbc` from `patch-2.MPQ` | 3,060,024; 29,604 rows | `6f37ca862dc632a11aaf86e0ba7b183aa7abaebb052549e6120aa9fa69dd9dee` |
| currently installed effective `ItemDisplayInfo.dbc` from custom `patch-4.MPQ` | 4,403,757; 38,481 rows | `0d29f0d23fbee83c5ec616a0a8665e69a4aad737f9f1ee9e1cd9865f3f1e093a` |
| golden `ITEM\ObjectComponents\WEAPON\Sword_1H_Short_A_01.m2` from winning final-stock `patch.MPQ` | 7,056 | `4632779297c8202d5915ff420f63ed2097853680a7ceaa4476bf10fabccdc392` |
| lower differing copy of the same M2 from `model.MPQ` — do not use | 7,056 | `12c6673c2956a3eb6c2b2c215b37669af01a99bc63f596d763fbaaf28ac360f2` |
| golden `ITEM\ObjectComponents\WEAPON\Sword_1H_Short_A_01Blue.blp` from only/winning `texture.MPQ` | 6,652 | `9ba36fe87aaab99f5ae15a597a2fc59bd85c7c69a994feed718ccdb990085219` |

Whole provider-archive fingerprints:

| archive | SHA-256 |
|---|---|
| `patch.MPQ` | `977b63af02fe5d9f64453e18f3cfb7ac077769068d29125e475be8357654aeb4` |
| `model.MPQ` | `5dfddc2bdd255f5c7ea97de55d4a8e894383c3da43f0f247e29fa37f554b4a5d` |
| `texture.MPQ` | `9af80fc30a91672f27583cdf9d25cd88571bda209f68916d34775402a98571f4` |
| `patch-2.MPQ` | `6de457521b5ce390ce448e6aa03de410f99dc982536d4a79dff015b1667ff158` |
| `patch-4.MPQ` | `e9adeb1d59e4d598916e3ecfacd51d879396b50c23ddbf077e9b7d468e81e1ca` |
| `dbc.MPQ` | `5caa94db4ef3b2841e8bb23a29ae08b29738464af7ee4c471969c4f1cfea2be5` |

The full `GameData\Data` corpus contains 15 MPQs totaling 5,507,361,412 bytes. Its canonical
archive-manifest SHA-256 is
`3f8f7214803e561e9432feba66c70b79513d6a3e8341d492d2133aae440f75ca`, produced from records
sorted by case-insensitive archive name in the form
`name<TAB>decimal-bytes<TAB>lowercase-sha256`, LF-separated UTF-8 without BOM or trailing LF.

Target client identity is documented as WoW 1.12.1 build 5875, but this `GameData` directory
contains no `WoW.exe`/build marker for independent executable verification. Record that as a
documented target rather than a hash-proven executable fact. The current MSUI checkout is
`f8b1d19…`; its existing Release binary identifies older commit `a82db16f94eac47047de46a7303254093c318ce4`,
with executable SHA-256 `dd2c60673e90062cc5ed6b23ccbf4a59168e30f2364859a855e8d216e7f5b28f`
and DLL SHA-256 `708dbf5b4013db4791061fbade0e0ffc622c883f1700c96fcd3e36c9462e0667`.
Rebuild MSUI on the new PC before treating binary diagnostics as source-current; Nico controls
actual process execution.

### 13.4 Forge state and ID continuity

Current state is explicitly **none**: `custom_weapon_model`, `custom_weapon_display`,
`custom_weapon_item_manifest`, `custom_id_allocator`, `custom_id_reservation`, and the
content-addressed `{ARTIFACT_ROOT}` do not exist in the current implementation, so this PC has
no weapon-gen migrations, reservations, tombstones, compiled members, or immutable weapon ZIPs
to export. The first Phase-0 implementation on the next PC bootstraps them from the pinned
read-only baselines and records that initialization manifest.

After Phase 0 creates any such state, every later PC move must restore, not reinitialize blindly:

- migrations for `custom_weapon_model`, `custom_weapon_display`, required item manifests,
  `custom_id_allocator`, and `custom_id_reservation`;
- isolated Forge model/display/item records;
- allocator rows, every reservation, failed-build tombstone, and externally handed-off ID;
- content-addressed source masters, compiled members, manifests, reports, and immutable ZIPs;
- build-lock/canonicalization/compiler/writer/generator version metadata.

Never reuse an item entry or display ID from a failed, deleted, or externally distributed
build. When bootstrapping an allocator from recovered state, choose:

```text
next item entry = max(configured floor,
                      read-only authoritative item_template max + 1,
                      all historical item-entry reservations + 1)

next display id = max(configured floor,
                       clean base DBC max + 1,
                       every retained/installed/distributed custom DBC max + 1,
                       all retexture/atlas/weapon display IDs + 1,
                       all historical display-ID reservations + 1)
```

Reserve transactionally from those values and retain tombstones permanently enough to prevent
collision with any patch that may still exist outside the Forge database. Fail closed if either
namespace would allocate above the live `MEDIUMINT UNSIGNED` ceiling of `16,777,215`; never wrap,
truncate, or silently choose a lower reusable ID.

### 13.5 PC-swap completion gate

The handoff is portable only when all of these are true:

- `WEAPON_GEN.md` is tracked in the handoff revision; root `tools/` and `WORLD_STATE.md` are
  absent from the public tree and, if still needed, separately transferred through a private
  owner-controlled channel;
- both required ULTUM files are committed/exported and hash-verified;
- all four repo commits/dirty manifests and sanitized origins are recorded;
- settings names are recreated and secrets/keys are separately provisioned;
- Phase-0 report/fixture hashes and controlled artifact keys are available;
- the embedded literal `item_template` DDL and donor-2131 TSV fixtures are present and reproduce their recorded SHA-256 hashes exactly;
- current preimplementation state is recorded as `none`; after Phase 0 creates weapon-gen state,
  allocator/reservation/tombstone records and the content-addressed Forge store are exported;
- a clean new-PC checkout can read this document and locate every non-Git dependency without
  relying on conversation history.
