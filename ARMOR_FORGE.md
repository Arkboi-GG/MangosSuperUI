# ARMOR_FORGE.md — the Armor Forge as built (v2, TBC-import-driven)

**Audience:** the next agent (or developer) touching the Armor Forge.
**Status date:** 2026-08-22 (WotLK import lane added the same day, §8). v2 builds clean; the model re-emission chain is proven offline against
the local 1.12 + 2.4.3 clients (§3). In-client gates are listed in §7.

Sibling document: [WEAPON_FORGE.md](WEAPON_FORGE.md). The Armor Forge reuses the weapon machinery
wherever it can — read that first for id reservation, DBC writer, MPQ builder, donor fixture, and the
apply/deploy pattern; this document covers only what armor adds or does differently.

**History, so nobody repeats it:** v1 shipped three "generator" cards (AI-prompt painted pieces,
skin-a-stock-helm, cloak) plus a flat TBC list and a hand-assembled set editor. The owner rejected
it: not imports, unusable browse, confusing sets. v2 (this document) is what he actually asked for:

> Sets are a *visual grouping* signal — read TBC's set data so the pieces that belong together
> import together, as one unit; a single piece is the optional split. Bonuses are vanilla's business,
> never imported. Helm/shoulder/cloak must really import. Weapon Forge shows weapons, Armor Forge
> shows armor.

---

## 1. Why armor is three things

| Lane | Slots | How it renders | What the forge ships |
|---|---|---|---|
| **Painted** | chest, robe, legs, gloves, boots, bracers, belt, shirt, tabard | NO model — partial BLPs composited into the shared character **body atlas** | the TBC component BLPs, byte-for-byte, per gender suffix; `m_texture[14..21]` in the display row |
| **Modelled** | helm, shoulder | a real object-component **M2** on the head/shoulder bone | re-emitted native vanilla M2s: **16 helm race/gender variants**, or an **L/R shoulder pair**; one skin BLP; effect BLPs |
| **Cloak** | back | a single texture on the built-in **cape** geoset | the TBC `Cape\*.blp` + the row's cape geoset group |

The armor fields of ItemDisplayInfo (23 fields / 92 bytes, documented in
[`DbcService.LoadItemModelInfo`](MangosSuperUI/Services/DbcService.cs)): `[1-2]` model names,
`[3-4]` model textures, `[5]` icon, `[6-8]` geoset groups, `[11]` group sound, `[12-13]` helmet
hair/facial-hide masks, `[14-21]` the eight body-atlas component textures. The weapon row builder
zeros 6-8/12-13/14-21; `ArmorDisplayInfoRow` is where they get filled.

### Measured facts (local clients, scratchpad `ArmorProbe`) — do not re-derive
- **Helms are per race/gender files.** DBC says `Helm_X.mdx`; the client loads
  `Helm_X_{HuM,HuF,DwM,DwF,NiM,NiF,GnM,GnF,OrM,OrF,ScM,ScF,TaM,TaF,TrM,TrF}.m2` (TBC: same scheme +
  `Be*`/`Dr*`). A forged helm must ship all sixteen (`ArmorNaming.HelmVariantSuffixes`).
- **Shoulders are an L/R pair of distinct files**: `ModelName1=LShoulder_X.mdx`,
  `ModelName2=RShoulder_X.mdx`, `TextureName1==TextureName2`. Not a mirror.
- **Cloaks are texture-only**: 0 M2s in `Cape\` in either client; row = empty model,
  `TextureName1` = cape stem, `geoset[0]` = cape shape variant.
- Stock helm/shoulder M2 = **1 bone, 4 views, 1 Type-2 texture, 0 attachments** — structurally the
  weapon scaffold, so the weapon variable-topology writer fits unchanged.
- **TBC ItemDisplayInfo**: the mounted 2.4.3 DBC measured **25 fields / 100 bytes** (vanilla layout
  with InventoryIcon[2] → everything from geoset groups on shifts +1, component base **15**, trailing
  particleColorID); one patch layer read as 24 / 96 (no second icon, base 14). `TbcArmorCatalog`
  detects by field count and shifts every field ≥ 6 accordingly — never hard-code the base.
  **TBC ItemSet.dbc** = 53 fields / 212 bytes, member item ids at `[18..34]` (vanilla: 45 / 180,
  `[10..26]`).
- **Set rows are template-authored**: a gauntlet row carries shoulder models + chest/pant/boot
  textures; the game reads only slot-relevant fields. See §6c — `PaintedSlots` is a filter.
- `tbc-item-catalog.json`: 14,591 class-4 rows, ~12 % GM/test junk, **no itemset column** (it was a
  one-off query on the remote tbcmangos DB; no generator is committed). Sets therefore come from the
  **TBC client's own ItemSet.dbc**, which also means any user gets set grouping with just their
  2.4.3 install — no TBC database needed.

---

## 2. Architecture

All armor code: `MangosSuperUI/Services/ArmorForge/`, `Controllers/ArmorForgeController.cs`,
`Views/ArmorForge/Index.cshtml`. Sidebar: **Game Development → Weapon Forge / Armor Forge** (the
v1 "Item Assets" hub was removed on request).

- **`TbcArmorCatalog`** — the browse: joins the shipped catalog (names) to the mounted TBC client.
  Junk-filtered (`JunkName` regex: `^\d`, `^Monster -`, `^OLD…`, `\bTEST\b`, `Deprecated`, `[PH]`,
  `\bPH\b`, `(DND)`, `DO NOT USE`, `Epic Warrior`, …). `^\d` is the big one — Blizzard's internal
  gear-up sets are named "&lt;ilvl&gt; &lt;quality&gt; &lt;theme&gt; &lt;slot&gt;" ("63 Green Frost
  Belt", "63 Blue Shadow Gloves", "90 Epic Rogue Cap", "5% Test Speed Boots") and **every** class-4
  name starting with a digit is one of them (measured: 1,217 of 14,591 TBC rows, 133 of 23,578 WotLK
  rows; no real armor name starts with a digit). Builds `Sets()` from TBC ItemSet.dbc (name `[1]`,
  items `[18..34]`), ranks each set by its best browsable member (`MaxQuality`, `MaxItemLevel`,
  `Featured` = epic at or above `FeaturedMinItemLevel`: **120** TBC, **200** WotLK) and tags every
  piece with its set. Exposes `GetDisplayRow()` (decoded TBC row). Shields are excluded — they are
  the Weapon Forge's.
- **`TbcArmorImporter`** — `Resolve(entry, displayIndex, diag) → ArmorImportSource`, one resolver
  per lane (§3). All member paths / internal names are final because the display id is reserved
  first.
- **`CustomArmorBuildService`** — the ONE packaging path. `ImportTbcAsync(entry, name?, setId,
  rebuild)`: reserve ids → resolve/emit → `WeaponItemTemplateSql` (donor-2131 clone with armor
  overrides: class 4, slot, material sound group, armor value, TBC quality/ilvl/req level, `set_id`)
  → persist → in-memory DBC registration → world INSERT + `.reload item_template` → (patch rebuild +
  deploy unless batched). `ImportTbcSetAsync(tbcSetId)`: allocate OUR set id (`armor_set` kind,
  ≥ 5000), create `custom_armor_set` (TBC name, **no bonuses**), import every member with `rebuild:
  false`, rebuild patch-6 once. Also `SaveSetAsync` (optional vanilla bonuses), `DeleteAsync`,
  `DeleteSetAsync`, `RebuildPatchAsync`, `ListSetsAsync`.
- **`ArmorPatchBuilder`** — `patch-6.MPQ` (mirrors `WeaponPatchBuilder`: pure, reopen-and-verify)
  with `ArmorDisplayInfoRow` rows, model + texture members, and `ItemSet.dbc` when sets exist.
- **`ArmorItemSetDbc`** — vanilla ItemSet.dbc writer (45 fields guarded at runtime).
- **Reused unchanged:** `WeaponIdReservationService` (kinds `item_entry`, `item_display`, new
  `armor_set`; display floor now unions `custom_armor_display`), `WeaponItemTemplateSql` +
  `DonorItemTemplateFixture`, `DbcWriterService`, `MpqArchiveWriter`, `M2Reader`,
  `TbcWeaponMeshExtractor`, `M2VariableTopologyBuilder`, `M2GeometryPatcher`, `M2BinaryValidator`,
  `CoordinateContract`, `WeaponAssetCompiler.ValidateBlp2Envelope` (made `internal`).

### DB (`DbInitializationService`, InnoDB, guarded migrations in `MigrateArmorForgeAsync`)
`custom_armor_display` (+`model_name2`), `custom_armor_component` (PK `display_id, slot,
gender_suffix`), `custom_armor_model` (emitted M2 + effect BLP members, PK `display_id, mpq_path`),
`custom_armor_set`.

### patch-6 layering
`patch-6 > patch-5 (weapons) > patch-4 (retextures) > base`. Its ItemDisplayInfo.dbc is built on the
mounted state **excluding patch-6** (`ResolveBaseDbc`), so it re-unions the lower rows.
`MpqReaderService.IsLivePatch` includes patch-6. `CustomWeaponBuildService` calls
`RebuildArmorPatchAsync` after every weapon build/rebuild (and the retexture hook reaches it
transitively), so patch-6 never masks newer weapon/retexture rows.

---

## 3. The import lanes (`TbcArmorImporter`)

**Painted** — for each non-empty `m_texture[slot]` partial on the TBC row, pull every gender variant
(`_M`, `_F`, `_U`, bare) from TBC `Item\TextureComponents\{subdir}\`; if
`ValidateBlp2Envelope` passes, carry the BLP **byte-for-byte** under our stem with the same suffix
(`Item\TextureComponents\{subdir}\SUI_A_####_s{slot}{suffix}.blp`); else decode → re-encode
uncompressed. The client composites them exactly like its own.

**Helm** — for each of the 16 vanilla suffixes: TBC `Head\{stem}_{sfx}.m2` (fallback: the HuM mesh)
→ `M2Reader.Parse` (v260 ok) → `TbcWeaponMeshExtractor.Extract` → `CoordinateContract.MeshToWoW`
(byte-space identity round trip; helms are placed by attachment, no reorientation) →
`M2VariableTopologyBuilder.Build(donor)` → `RewriteInternalName("SUI_A_####_{sfx}")` →
`M2BinaryValidator`. Donor = the vanilla file of the **same stem + suffix** if it exists, else the
pinned `Helm_Leather_D_01_{sfx}`. Output `Head\SUI_A_####_{sfx}.m2`; DBC `ModelName1 =
SUI_A_####.mdx` (the client appends the suffix). One skin BLP (`TextureName1 = SUI_A_####_V01`),
effect BLPs (`_E##`) shared across variants. Geoset groups / helmet-vis / sound / icon carried from
the TBC row.

**Shoulder** — same chain for `LShoulder_X` and `RShoulder_X` onto their own side's donor
(`LShoulder|RShoulder_Leather_A_01` fallback). DBC `ModelName1 = SUI_A_####_L.mdx`, `ModelName2 =
SUI_A_####_R.mdx`, `TextureName1 = TextureName2`.

**Cloak** — TBC `Cape\{TextureName1}.blp` → `Cape\SUI_A_####_V01.blp`; row = `TextureName1` +
geoset group from TBC.

**Proven offline** (scratchpad `ArmorProbe/EmitTest`): 3 TBC-only helms × 16 variants all emit and
validate; 4 TBC-only shoulder L/R pairs all ok. **Do NOT** route armor through
`WeaponDonorResolver` or `RigidWeaponMeshValidator` — both encode the weapon envelope (X-extent
0.15–6, palm fraction, single-submesh/4-view donor checks) and would reject/pollute helms.

---

## 4. Sets

Grouping comes from TBC ItemSet.dbc (§1). Importing a set = one `custom_armor_set` row named after
the TBC set, every member imported and stamped (`custom_armor_display.set_id` and
`item_template.set_id`), one patch-6 rebuild. **No bonuses are imported.** The optional "Vanilla set
bonuses" card lets the operator add `threshold → spell` bonuses to any forged set; those write to
`ItemSet.dbc` (client tooltip from patch-6; the **server** reads its own `ItemSet.dbc` at startup —
deploy via `ArmorForge:ServerDbcPath` and restart for bonuses to apply).

---

## 5. Endpoints (`ArmorForgeController`)

`Index`, `Status` (fixture / TBC pieces+sets / patch), `TbcBrowse?search=&family=` (matching **sets
first** with all their armor members, then loose pieces), `TbcPreviewPainted?entry=` (slot → PNG
urls for the viewer, pre-import), `TbcImport` (entry, name?, setId), `TbcImportSet` (tbcSetId,
entries[]?), `ListArmor` (pieces + sets), `Delete`, `DeleteSet`, `RebuildPatch`, `DownloadPatch`,
`SaveSet`. No antiforgery (weapon-forge convention).

## 6. UI (`Views/ArmorForge/Index.cshtml`)

**Every card starts collapsed** (TBC import, WotLK import, Registry, bonuses) — click the heading to
open one. With two clients mounted the page otherwise opened into thousands of rows.

Inside a lane: one search box → **Tier & arena sets (N)** first (expand → pieces; **Import set** /
**Preview** / per-piece **Import piece**), then an **Other sets (N)** drawer, then loose pieces
(collapsed, click the header). The drawer holds everything that is not a current-expansion tier or
arena set — levelling greens, dungeon blues, crafted three-pieces, and the *earlier expansions*'
tiers, which the later client's ItemSet.dbc ships too. Measured on full mounts that split is **73
featured / 253 other** (2.4.3) and **98 / 369** (3.3.5a); before the split all 326 / 458 rendered
inline. Both lists travel in the same `ImportBrowse` payload (`sets` / `otherSets` +
`featuredSetCount` / `otherSetCount` / `featuredMinItemLevel`), so the toggle is a view flip with no
round-trip. Set headers show `ilvl N · K pieces`. Registry grouped by set (View / View set /
Delete / Delete set). Optional bonus editor. Right: the Items page's three.js character viewer
(default Apprentice's Robe so it isn't naked). **All previews go through `equipMultiple`** — the
same path the Items page uses (geosets, robe/kilt arbitration, paint order, cape) — via a new
`opts.fetchDressing` hook in `equip.js`: TBC pieces (pre-import) get a dressing payload from
`/ArmorForge/TbcDressing?entry=&race=&gender=` (same shape as `/Items/ItemDressing`, built from the
TBC row: inventoryType, geoset groups, gender-preferred slot PNGs, cape PNG, hidesHair); forged
pieces use `/Items/ItemDressing` (`BodyAtlasTextureService` reads `custom_armor_component`; helm/
shoulder GLBs come from the live patch-6 mount). Hover a piece = default outfit + piece; set
Preview / View set = the set's pieces together. The earlier direct-paint
(`equipBodyAtlasRetextureDirect`) path only overlaid textures and ignored geosets — that's why the
first preview looked wrong.

## 6b. Field lessons from the v2 adversarial review (why the code looks like this)

1. The armor base DBC honours **only** `ArmorForge:CleanDbcPath`. `WeaponForge:CleanDbcPath` is
   the state *without* patch-5; patch-6 built on it would shadow every forged weapon in-client.
2. Helm import is **two-pass** (pick the fallback mesh first, then emit all 16) and a single bad
   race/gender variant is a **warning** — only zero emitted fails the piece.
3. Effect textures are keyed by their **TBC source path**, not slot index — variants order their
   textures differently, so slot-keyed sharing bound the wrong bytes.
4. TBC-only bag icons (`Interface\Icons\*.blp`) are packaged when vanilla lacks them.
5. Any exception between id reservation and persistence **releases** the reserved ids.
6. `SaveSetAsync` also *unstamps* `item_template.set_id` for pieces removed from a set.
7. A 25+-field TBC ItemDisplayInfo shifts **every** field ≥ 6, not only the component base.

## 6c. Preview — what went wrong and what's true now (verified locally 2026-08-22)

Symptoms reported: "loads for a second, then drops off; only the gloves are right; no head".
Three distinct causes, all fixed and **verified in a local run against the Desktop 2.4.3 + 1.12
clients** (see §9 for the local-run recipe):

1. **Hover hijack.** Piece rows had a `mouseenter` preview; after clicking a set's Preview, moving
   the pointer down the list re-dressed the character with robe + ONE piece (the gloves). Hover
   previews are gone; a piece is previewed by clicking its name.
2. **Template rows.** TBC/WotLK ItemDisplayInfo rows for sets are authored from a shared template:
   the *gauntlets* row carries `LShoulder_Plate_A_01.mdx` models and chest/pant/boot component
   textures; the *helm* row carries sleeve/chest/pant textures (raw-dumped: rows 45659/49684/45658…
   for Onslaught). The game reads only the slot-relevant fields; the preview AND the importer were
   painting every listed slot, so each piece overpainted the others. `ArmorTypeProfile.PaintedSlots`
   is now a FILTER applied in both (chest 0,1,3,4 · robe +5,6 · legs 5,6 · gloves 1,2 · boots 6,7 ·
   bracers 1 · belt 4,5 · tabard 3,4 · shirt 0,1,3,4; helm/shoulder/cloak none).
3. **No head pre-import.** Dressing payloads now carry `attachments` built straight from the source
   client's M2 (`lane.Catalog.LoadM2` — TBC v260, WotLK v264+.skin) + skin BLP through the same
   `GlbWriter` the Items page uses, cached under `wwwroot/armor_forge_cache/{lane}/{entry}/`
   (`helm_{RaGe}.glb`, `lshoulder.glb`, `rshoulder.glb`). Verified: Onslaught Battlegear mounts the
   helm on Attachment_11 and shoulders on Attachment_5/6, atlas composited, glove/boot geosets set.

Also measured: this 2.4.3 client's mounted ItemDisplayInfo.dbc is **25 fields** (InventoryIcon[2]);
`TbcArmorCatalog` detects by field count and shifts every field ≥ 6 (an earlier patch layer read as
24 — the detection handles both).

## 6d. Preview — the fabricated DBC row (2026-08-23, measured)

Owner: *"After committing the gear in armor forge, some of it goes wonky in the previewer — fine in
game, but wonky."* Screenshot: set 5002 Worldbreaker, spaulders rendering as scattered shards, both
sides wrong and differently wrong, everything else correct.

The forged model was fine. **The previewer and the client were reading different DBC rows.**

- The client reads patch-6, written by `ArmorDisplayInfoRow.BuildAndAdd`, which sets
  `TextureName2 == TextureName1` whenever there is a second model. Correct.
- The previewer reads `DbcService.ItemModelInfos`, an in-memory row that
  `CustomArmorBuildService.RegisterDisplayWithDbc` produced by **cloning the first stock row whose
  ModelName1 starts with `LShoulder`** and overriding only ModelName1/ModelName2/TextureName1.
  Measured against the mounted 1.12 `ItemDisplayInfo.dbc`: that row is display **1057**,
  `LShoulder_Leather_A_01`, whose `TextureName2` is `Shoulder_Leather_A_01Brown` — and 1,752 of the
  1,826 `LShoulder*` rows carry a non-empty TextureName2, so this was never going to be empty.

`ItemTextureService.EnsureShoulderGlb` builds the LEFT pad from ModelName1 + TextureName1 and the
RIGHT from ModelName2 + **TextureName2** — so the forged right spaulder was skinned with a stock brown
leather shoulder BLP, sampled through the forged material's alpha key, which punches the pad into
disconnected islands. Correct in game, exploded in the previewer, asymmetric by construction.
`GeosetGroup`, `HelmetGeosetVis1/2`, `BodyTextures` and `ItemVisualId` were inherited from the same
arbitrary row.

Fixed at the source rather than at the symptom:

- `DbcService.RegisterCustomDisplayEntry` gained an overload that takes a **full `ItemModelDbc`**, and
  `RegisterDisplayWithDbc` now states every field with the same values the patch row gets. The two
  cannot drift again. The clone-based overload stays for `ItemRetextureService`, where the donor row
  genuinely IS the item being retextured.
- The clone overload also stopped inheriting `TextureName2` whenever the caller states the model pair
  explicitly — that is a Forge writing its own row, and the donor's second texture is never right
  there. This covers the Weapon Forge's mirrored-ModelName2 (thrown) case too.
- Helm/shoulder GLBs now have the same cache discipline weapons already had: live-patch refresh, a
  SHA-256 `.source` sidecar over (model name, texture name, raw M2, every bound texture), and the
  fingerprint on the returned URL. They previously returned on a bare `File.Exists` whose only version
  component was the assembly MVID, so a delete-and-re-import inside one running process kept serving
  the old GLB and any fix read as a no-op.
- `ArmorForgeController.BuildSourceAttachmentGlb`'s pre-import cache had no version component at all;
  it is now stamped with `RigidGlbVersion`.

## 7. Proven vs pending

**Proven offline:** helm 16-variant + shoulder L/R re-emission (§3); TBC DBC layouts; set grouping;
cloak row shape. **Builds clean.**

**Owner in-client gates (same as the weapon families had):** (1) a forged TBC helm/shoulder renders
on the bone (writer + DBC binding are weapon-proven; armor attachment untested in-game); (2) a painted
set composites; (3) a cloak. **Deferred:** pre-import 3D preview of helm/shoulder meshes (after
import works via View); catalog JSON extension (set data is client-derived, so not needed for sets;
names for TBC-only entries outside the catalog would need it).

## 8. WotLK import lane (2026-08-22, offline-proven, NOT yet client-verified)

The TBC lane is now one of two **expansion-keyed lanes** (`tbc` / `wotlk`); everything past
resolution — ids, donor SQL, persistence, patch-6, sets, registry, bonuses — is lane-agnostic.

- **Mount + catalog**: `WotlkMpqSource` (`WeaponForge:WotlkDataPath`, set on Settings next to the TBC
  path) + `WotlkItemCatalog` (`wwwroot/data/wotlk-item-catalog.json`, 23,578 class-4 rows from the
  open-source azerothcore-wotlk `item_template.sql`). See WEAPON_FORGE.md §2 for the shared
  `LegacyMpqSource` / `LegacyItemCatalog` / `M2WotlkReader` / `MpqPatchOrder` details.
- **`TbcArmorCatalog`** is unsealed and built over the base types; **`WotlkArmorCatalog`** derives from
  it (WotLK mount + catalog, `Key`/`Label`, `LoadM2` passthrough). The 3.3.5a ItemDisplayInfo is
  **25 fields / 100 B** (second inventory icon at [6] → component base **15**, geosets 7-9, sound 12,
  helmet-vis 13-14, textures 15-22) — the existing width-detected shift handles it; ItemSet.dbc is the
  same 53-field layout as 2.4.3 (name [1], items [18..34]). Measured: 16,919 browsable WotLK armor
  pieces (12,005 painted / 3,957 modelled / 957 cloaks) in 439 sets (junk filter unchanged).
- **`TbcArmorImporter`** is unsealed; `ParseTbc` now calls `catalog.LoadM2` (version-aware); messages
  use the lane label. **`WotlkArmorImporter`** derives from it. Helm/shoulder re-emission is the same
  chain: 4 WotLK-only helms × 16 vanilla variants all emit + validate (WotLK ships 20 per helm — the
  4 Be*/Dr* files are simply not requested); shoulder pairs ok; two 3.3.5a IDI rows
  (`LShoulder_Robe_MageDungeon_A_01`, `LShoulder_Cloth_AhnQiraj_A_01`) reference files absent from
  the client and fail with a clear diagnostic, which is the intended behaviour.
- **`ArmorImportSources`** (`Services/ArmorForge/ArmorImportSources.cs`, DI singleton) pairs catalog +
  importer per lane; `CustomArmorBuildService.ImportAsync(lane|"tbc"|"wotlk", …)` /
  `ImportSetAsync(…)` are the implementations, `ImportTbcAsync`/`ImportTbcSetAsync` and the new
  `ImportWotlkAsync`/`ImportWotlkSetAsync` are wrappers. `CustomArmorBuildResult.SourceExpansion`,
  gameplay_json `sourceExpansion`, audit actions `import_wotlk_*`.
- **Controller**: `Status` returns `tbc` and `wotlk` blocks; `TbcBrowse/WotlkBrowse/ImportBrowse?
  expansion=`, `TbcDressing/WotlkDressing/ImportDressing`, `TbcImport/WotlkImport/Import`,
  `TbcImportSet/WotlkImportSet/ImportSet(expansion, sourceSetId)`. Piece/set DTOs carry `expansion`;
  the preview cache is `armor_forge_cache/{tbc|wotlk}/{entry}`.
- **UI**: a second **"Import WotLK armor & sets"** card (`wotlkSearch/Family/Results`, `pillWotlk`);
  the search/render/import JS is one `laneSearch(lane, q)` over `LANES.tbc|wotlk`; imports post to the
  lane-keyed `Import`/`ImportSet`; pre-import dressing routes by `e.expansion` to
  `{Tbc|Wotlk}Dressing`. Registry, View, set editor unchanged (forged pieces are vanilla rows).
- **Proven offline** (scratchpad `WotlkProbe/ProdTest`, the production classes wired by hand against
  the local 3.3.5a client): Scourgeborne / Deathbringer / Frostfire sets resolve every member
  (painted components, 16–18 helm files incl. effect BLPs, L/R shoulders), loose cloak/helm/shoulder
  samples resolve; famous weapons (Shadowmourne 3,017 tris / 3 glow passes, Glorenzelg, Betrayer of
  Humanity, Oathbinder) load and extract. **Owner in-client gates are the same three as §7** for the
  WotLK pieces.

## 7b. Deleting a set — ordering and batching (fixed 2026-08-23)

Owner: *"delete set now deletes the 1st item and breaks up the set."* Two defects, both in
`CustomArmorBuildService.DeleteSetAsync`:

1. **The set row was deleted FIRST**, before any piece. The registry groups pieces by joining them to
   `custom_armor_set` (anything whose set is missing falls into "Single pieces"), so the moment that
   row went, every remaining piece visibly fell out of the set — even though the pieces still existed.
   That is the "breaks up the set" half, and it happened *before* a single piece was touched.
2. **A full patch rebuild + world reload PER PIECE.** `DeleteAsync` repackaged all of patch-6,
   redeployed it, rewrote `ItemSet.dbc` and issued `.reload item_template` over RA — once for every
   piece. An 8-piece set meant 8 rebuilds and 8 RA round-trips, slow enough to blow the request budget
   partway through. That is the "only the 1st item got deleted" half. The import side already had the
   answer: `ImportAsync(..., bool rebuild = true)` with `ImportSetAsync` passing `rebuild: false` per
   piece and rebuilding once at the end. Delete never got the same contract.

Now: `DeleteAsync(displayId, bool rebuild)` mirrors the import signature; `DeleteSetAsync` deletes
every piece with `rebuild: false`, contains per-piece failures instead of aborting the loop, then does
**one** reload + **one** rebuild, and only then deletes the set row and releases its id. A partial
failure therefore leaves an intact, still-grouped, still-retryable set rather than debris.
`ArmorDeleteResult.NotFound` distinguishes "already gone" from "failed" so one stale member cannot make
a set permanently undeletable. The UI reports the real outcome (`ok:false` renders as "Delete set
incomplete" with the reasons) instead of always saying "Deleted set" and dumping raw JSON.

## 8a. Lane naming — `Legacy*` vs `Tbc*`/`Wotlk*` (2026-08-23)

Owner: *"Why do we only C# files named TBC importers? We also do Wrath."* Correct — the TBC lane was
built first and WotLK was bolted on, so `TbcArmorCatalog`/`TbcArmorImporter` were doing double duty as
both the shared base AND the TBC registration, and `WotlkArmorCatalog : TbcArmorCatalog` literally
made WotLK a subclass of TBC. Now:

- `LegacyArmorCatalog` / `LegacyArmorImporter` are **abstract** bases holding all the shared logic
  (files `LegacyArmorCatalog.cs`, `LegacyArmorImporter.cs`).
- `TbcArmorCatalog`/`TbcArmorImporter` and `WotlkArmorCatalog`/`WotlkArmorImporter` are **sealed
  siblings**, each supplying only its mount, its catalog and its endgame item level (120 / 200 —
  `FeaturedMinItemLevel` is now `abstract`, so neither lane can silently inherit the other's).
- Shared records lost their TBC names: `TbcArmorEntry`→`LegacyArmorEntry`, `TbcSetInfo`→`LegacySetInfo`,
  `TbcDisplayRow`→`LegacyDisplayRow`; the shared extractor is `LegacyWeaponMeshExtractor` (it serves
  weapons AND armor, so it was doubly misnamed). `ParseTbc`→`ParseSourceModel`, `_tbc`→`_mpq`.
- DI registrations are unchanged (`TbcArmorCatalog`, `WotlkArmorCatalog`, …) — the concrete names
  were already right.

See WEAPON_FORGE.md §2b for the full convention table and the diagnostic-key rename.

## 8b. Motion — flames that move (2026-08-23, offline-proven, NOT client-verified)

Owner on Worldbreaker: *"in the real Wrath these have nice flames coming through the open parts of
the shoulders + some flame animation on the center headpiece. Right now it's just a static fire-esque
image."* Baked static sprites are replaced by **real 1.12 particle emitters transplanted out of stock
models**, plus a **global-sequence alpha pulse** on additive glow passes. Full mechanism, measurements
and the 320-model regression are in WEAPON_FORGE.md; the armor-specific parts:

- Armor is where frozen effects hurt most — a helm's eye flames and a shoulder's braziers ARE the
  piece. Vanilla itself puts particle emitters on **40 of its 225 shoulder models**
  (`LShoulder_Robe_Raid_A_01`), so there is both native precedent and a donor to lift from.
- `TbcArmorImporter.Emit` plans motion **before** extracting and passes `bakeEmitters: !plan.Any` —
  a rebuilt emitter must not ALSO be baked into a sprite, or the effect draws twice, once alive and
  once as a decal. The graft runs after `RewriteInternalName`, before validation; the pulse right
  after it. Both are wrapped: a failure is a warning and the piece still ships.
- Positions come straight from source space through `CoordinateContract.MeshToWoW` — armor is emitted
  without a placement transform, so an emitter sits exactly where the later client had it.
- Measured on the owner's own gear: **Valorous Worldbreaker Headpiece** → 1 flame emitter (2 zero-size
  source emitters correctly skipped, which would otherwise have inherited the donor's 0.25 scale and
  painted blobs the original never had); **Spaulders** → 3 flame emitters; source orange
  (255,121,23) carried through; re-parse ok, validator clean.
- Intent for these comes from **colour, not name**: `SHOULDER_MAIL_RAIDHUNTER_G_01_PARTICLE` says
  nothing about what it is, and reading the name alone produced a glow ball instead of fire.

## 8c. Motion, part two — the flame was moving, just wrong (2026-08-23, measured)

Owner on the first in-client round of §8b: *"The flame is a nice smooth effect in wrath, and we have a
VERY fast on/off flame thing."* The transplant worked; the **timing** did not travel with it.

Measured end-to-end against the real clients (`M2WotlkReader.Parse` on
`LShoulder_Mail_RaidShaman_G_01.m2`, then `EffectMotionPlanner.Build` + `M2EmitterTransplanter.Apply`
against a stock 1.12 scaffold, dumping the shipping bytes):

| | source (WotLK Worldbreaker) | donor as-shipped (FLAMELICKSMALL) |
|---|---|---|
| lifespan | **2.30 s** | 0.75 s |
| emission rate | 8 /s | 7 /s |
| particles alive | **~18** | ~5 |
| texture sheet | 1×1 (still) | **4×4 (16-cell flipbook)** |

Same colour, same size, same place — and five sparse sprites appearing and vanishing seven times a
second while each flicks through sixteen flipbook cells in 0.75 s **is** the reported on/off strobe.
`M2ParticleEmitterInfo` carried position/texture/colour/scale and nothing that decides whether
particles OVERLAP, which is the whole difference between fire and a stutter of separate sprites.

Four changes, all in the shared Weapon Forge machinery:

1. **`M2EmitterMotion`** — both readers now capture the ten float tracks (speed, variation, vertical
   and horizontal range, gravity, lifespan, rate, emission area, zSource). The v264 layout differs
   twice from ≤ v263 and both bite: its `M2Track` values are `M2Array<M2Array<T>>` (one sub-array per
   sequence — reading the outer array as floats returns the inner count/offset words, i.e. every
   track came back 0.000), and bare `lifespanVary` / `emissionRateVary` floats are inserted after two
   of the tracks, so a constant 0x14 stride silently reads the wrong field from emission rate on.
2. **`M2EmitterTransplanter` writes them over the donor's**, in place, in the track value arrays —
   same count, same offset, same interpolation, so the offset-preserving surgery still holds.
   Clamped to what 1.12 ships, and the steady-state particle count is capped at 150.
3. **Donor selection now matches tile geometry** (`VanillaEmitterDonors.Best`). The graft keeps the
   donor's texture and therefore its cell grid, so the grid can only be selected for, not retargeted.
   The Flame family also gained an explicit representative (FIRE1 — a plain upward lick authored on a
   shoulder pad) rather than "whichever Flame is first in catalog order", which was the 4×4 molotov.
4. **The colour ramp survives.** The source ramps (255,219,143) → (255,121,23) → (58,26,2) — a
   white-hot core cooling to ember — and the forge used to paint the mid key on all three keyframes.

Result for Worldbreaker's spaulders: lifespan 2.30 s, rate 8/s, ~18 overlapping particles, 1×1 donor,
full colour ramp, validator clean.

Also closed while in here: `M2BinaryValidator` never looked at particle-emitter tracks, though it
checks exactly the same invariants for transparency / colour / UV tracks. Emitters reach a forged
model only by transplant, and two things do not travel with a shifted offset — the `int16
globalSequence` index (which now points into the TARGET's global-loop array, and every scaffold has
zero entries) and the per-sequence `ranges` array. Neither fires on today's donors (all measure
`gs = -1`), but the failure is silent, indistinguishable from the timing bug it sits next to, and one
WotLK donor away. The validator now warns on both, and `Apply` resets a dangling index to −1 rather
than leaving the client to modulo against a duration that is not there.

## 8d. The flames are visible in the previewer too (2026-08-23)

Grafted emitters used to be a client-only result: correct in game, absent from the Armor Forge
preview, which made every iteration on §8b/§8c a deploy-and-log-in round trip. The previewer now
re-simulates them — see WEAPON_FORGE.md §3c for the mechanism and the measured semantics.

For armor specifically: the emitter meshes parent under the attachment node, so a spaulder brazier
rides `Attachment_5`/`_6` and a helm flame rides `Attachment_11` without any extra plumbing. The
POST-commit preview path (forged v256 model out of patch-6 → `GlbWriter`) carries them; the
PRE-import path renders the v264 source model directly and does not, because `M2FxReader` decodes
the ≤ v263 record layout only. So a set previews its flames after import, not before.

## 9. Running the site locally (no DB) to test the forge

The preview/browse path needs no database. Recipe (scratchpad `localrun/`): extract
`DBFilesClient\*.dbc` from the vanilla MPQs into a folder (probe project with `MpqArchive`), create a
run dir with `appsettings.json` (copied) + a `server-config.json` holding `Vmangos:ClientDataPath`
(vanilla Data), `Vmangos:DbcPath` (the extracted folder), `WeaponForge:TbcDataPath`,
`WeaponForge:WotlkDataPath`, a `wwwroot` junction to the project's wwwroot, then run the built DLL
with that dir as cwd (content root = cwd, so that `server-config.json` wins). Startup DB
registrations are now fail-soft (`Program.cs`), so the app boots with the DB down; DB-backed calls
(`/Items/ItemDressing`, import) 500 locally and are skipped by the viewer.


## 9. First in-client round (2026-08-22) — what broke and what changed

- **Painted hi-res components**: WotLK late sets ship 256×128 / 256×64 components; vanilla regions are
  128×64 / 128×32 and the client blits at region size → quartered, streaky textures (Worldbreaker
  chest/gloves/kilt). `TbcArmorImporter.PackComponentBlp(blp, slot, …)` downscales to the region
  (`ComponentRegion(slot)`), Mitchell resample, uncompressed re-encode, diag
  `tbc.component.reencode: … downscaled to the vanilla 128×64 atlas region`. Onslaught (128×64) was
  untouched and rendered correctly.
- **Eye glows / shoulder flames** are particle emitters; they are now baked as static additive
  glow sprites by the shared extractor (see WEAPON_FORGE.md §2 "Emitter baking") — helm variants
  share the emitter textures as `SUI_A_####_E##.blp` members. Owner gate: Onslaught eyes glow,
  Worldbreaker shoulders flame.
- **Deploy staleness**: `Status.deployedPatch` + the red "patch-6 STALE" pill when the client's
  patch-6 is not the last build (client was running during deploy).

## 10. Class restriction + n-piece bonuses "not sticking" (2026-08-24, measured, code fixed — NOT yet client-verified)

Owner: *"Armor effect colour recolor works. However the class restriction AND the n pieces = effect aren't
sticking."* Walked A→Z with live measurements. **The table registrations were correct** — the DB rows,
`set_id` stamping and `.reload` were all fine. Three other things were wrong.

### The root cause of the bonus half: the server never got ItemSet.dbc
`ArmorForge:ServerDbcPath` was unset in the deployed `server-config.json` — and it was the *only* path
property in the forge with no `??` fallback, **and** unreachable from the Settings page
(`SettingsController.ServerConfig` has no ArmorForge member). So `DeployItemSetToServer` no-opped on every
build since the feature shipped: the server's `ItemSet.dbc` was still the Apr-3 stock file (172 records,
max id 551) while patch-6 carried a correct 175-record one.

The old comment here claimed the server copy was *"only needed if vanilla bonuses are defined"*. **That is
false, and it is why the key was never set.** `ObjectMgr::LoadItemPrototypes` (`ObjectMgr.cpp:4127-4130`)
validates `item_template.set_id` against `sItemSetStore` **unconditionally** and zeroes the column in
memory when the id is missing. Measured consequence: 15 × `Item (Entry: 11011xx) has wrong ItemSet
(500x)` in DBErrors.log, one per forged piece — so the set lost its **bonuses, its tooltip set block and
its membership**, bonuses or not.

Measured server facts (do not re-derive):
- The core reads `DataDir` + `SUPPORTED_CLIENT_BUILD` + `/dbc/` → `run/data/5875/dbc`. The 5875 is a
  compile-time `#define` (`Progression.h`), not configurable. `Vmangos:DbcPath` already points exactly there.
- **A restart is mandatory.** `LoadDBCStores` has ONE call site (`World.cpp:1401`, inside
  `World::SetInitialWorldSettings`); no entry in the 116-command `reloadCommandTable` touches a DBC store.
  `.reload item_template` re-runs the validation against the *stale* in-memory store, so it re-zeroes.
- **Converse, and useful:** `.reload item_template` alone IS enough for `allowable_class` — the core reads
  it signed-into-uint32 (`ObjectMgr.cpp:3829`), never clamps it, and `Player::CanUseItem`
  (`Player.cpp:10208`) ANDs the live prototype. No restart needed for the class half.
- The dbc dir is already app-writable by design (`PatchBuilderService` overwrites six DBCs there on every
  Spell Creator build) and the service user owns it. Permissions were never the blocker.
- Set bonus spells resolve from the world-DB `spell_template`, **not** Spell.dbc; an all-zero-spell set is
  safe (`Item.cpp:59-60` skips empty slots silently).

Fixed: `ResolveServerDbcDir()` chains `ArmorForge:ServerDbcPath` → `Vmangos:DbcPath` →
`Vmangos:ServerDataPath/5875/dbc` and reports what it tried. `DeployItemSetToServer` is now tri-state
(`ItemSetDeployState`) so "nothing to deploy" stops reporting the same success as a real deploy, takes a
first-write-wins `.vanilla` sidecar (only from a file that still *looks* stock — max id < 5000, so a
custom copy can never be enshrined as the vanilla reference), and reads the file back byte-for-byte.
`ArmorPatchResult.ItemSetOmitted` makes "sets exist but no ItemSet.dbc was built" a **failure** instead of
an indistinguishable success. Every result payload now carries the server-set outcome.

### The class-restriction half: the modal had no class control
`allowable_class` existed only as a **hidden** `cfg.allowableClass` variable, set as a side effect of
clicking *Generate stats* with a class picked. Picking a class and importing without generating did
nothing. Worse: re-opening a restricted piece showed Class reset to "— none / generic —", so one click on
Generate inside that edit silently stripped the restriction.

Live A/B proof from the owner's own two imports four minutes apart: set **5003** (00:12, generated) →
`allowable_class = 1`; the re-import as set **5004** (00:16) → `gameplay: null` on all five pieces →
`allowable_class = -1`.

Fixed: **Class restriction is now a real, visible, always-collected control** in both the piece modal
(`#cfgClassRestrict`) and the set modal (`#setClassRestrict`, applied to every piece — a tier set is
class-locked as a unit). It round-trips through `fillModalFromConfig`, `prefillFromSource` and
`cfgGenerate`; a multi-bit mask that no single option can represent is preserved as its own option rather
than flattened. `collectItemConfig` always emits the field (`-1` for "Any class") so clearing a
restriction actually clears the column. `prefillFromSource` now carries the source's own mask —
`CloneVanillaAsync` copies the whole `item_template` row, so without that a clone of a class-only item
would have been silently unlocked.

### Silent-drop guards (the "5/5 pieces imported" lie)
`openSetConfigure` reset `configs: {}` on every open, so cancel-then-reopen destroyed every per-piece
edit and bonus row; `setGenerate` *replaced* rather than merged, discarding name/armor/resistances/
durability/bonding/effects. Both fixed. `submitSetConfigure` now auto-generates unconfigured pieces once
(guarded against re-entry), then `confirm()`s rather than importing donor defaults silently. All three
live sets had `bonuses_json = '[]'`: a bonus row whose effect was typed but never clicked collapsed to
nothing, so `collectBonuses` now reports `attempted` and the submit path confirms the drop. **The
standalone "Vanilla set bonuses" card is a separate control with its own collector** (`addBonusRow`, raw
numeric spell id) and needed the same guard written twice.

### Restart signal (F4)
New `ServerItemSetStatus()` + `serverItemSet` in `Status`, a **sets** pill, and a *Restart mangosd* button
(hidden until owed) that reuses `POST /Home/ProcessAction`. `WriteCanonicalPatch` also drops
`ArtifactRoot/ItemSet.dbc` so the status compare does not have to repack the MPQ.

### Still owed
1. **Adversarial review did not run** (API overload) — this change is unreviewed.
2. **Live data repair is NOT done and must be in-place.** 10 of the 15 forged pieces are **equipped** on
   two level-60 warriors, with 27 `item_instance` rows and 13 mail attachments referencing entries
   1101101-1101115 — a delete-and-re-forge would destroy owned items. Check `item_instance`, not
   `character_inventory` (mail bypasses inventory). Sets 5002/5003/5004 are also three duplicates of the
   same set with identical names.
3. Deploy sequence, all four steps: publish the binary → restart `mangossuperui` (config re-read) →
   trigger a patch-6 rebuild (the deploy only runs inside a rebuild) → restart `mangosd`. Verify by
   DBErrors.log going quiet.
4. Out of scope but noted: `item_template.item_level` is `TINYINT UNSIGNED` (255 ceiling) and the WotLK
   raw ilvl 226 is carried through — a source above 255 would wrap.
