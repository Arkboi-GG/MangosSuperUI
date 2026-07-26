# MSUI Web Client — Handoff

**For: the next session. Read this first, then start work.**

Rev 2 — 2026-07-26, end of the session that ported foliage, Light.dbc exterior
lighting and MCNR terrain normals. Everything below is either committed to the
repo on `nicomain` or explicitly marked as not done.

---

## 0. The one-paragraph brief

Nico is building **two peer WoW 1.12 clients**: `MSUIClient` (native, C#/OpenGL,
Silk.NET — the more advanced one) and a **web client** grown out of
MangosSuperUI's three.js 3D World Editor. Both target 1:1 with the real 1.12
client. This session and the last were about porting MSUIClient's renderer into
the web one.

**The single most important instruction:** *the answers are in MSUIClient.*
Read its actual source files. Do not reconstruct behaviour from memory, notes,
or this document's summaries — those are pointers, not substitutes. Every bug
shipped in the first session came from reasoning about MSUIClient instead of
reading it.

**The second most important instruction:** *build a harness and then break it on
purpose.* §6 has the method. It has now caught two historical bugs before they
shipped, and both times the mutation test is what proved the check was real
rather than decorative.

---

## 1. Environment

| | |
|---|---|
| Web client repo | `C:\Users\nico\source\repos\MangosSuperUI` |
| Native client repo | `C:\Users\nico\source\repos\MSUIClient` |
| Both are connected folders | use `device_list_dir` / `device_stage_files` / `device_commit_files` |
| Server | VMaNGOS, systemd unit `mangossuperui.service`, runs at `192.168.0.2:5000` |
| Client data | `/home/wowvmangos/wowclient/Data` — all 15 MPQs, confirmed present |
| Deploy | Nico builds in VS and deploys; you commit source files to the repo |
| Line endings | **CRLF** on all C#/JS/Razor repo files, LF on docs. Verify before committing. |

**Architecture decision (settled):** server-mediated assets. MangosSuperUI reads
DBC/MPQ off the Linux box and serves parsed data to the browser, exactly as it
already does for items, icons, retexture and the character viewer. No WASM
readers, no File System Access. Private server, home use.

**Drift rule (settled):** where MSUIClient and MangosSuperUI disagree on a chunk
interpretation, **MSUIClient wins** and MangosSuperUI gets corrected.

**Where the split falls, now that three systems have been through it:** the
server owns the FILE FORMAT, the unit conversions and the settled coordinate
conventions. The browser owns anything that depends on where the camera is or
what time it is. Foliage and lighting both landed on that line independently.

---

## 2. What is built

### Session 1 (all deployed unless noted)

- **M1.1 true 1:1 world scale** ✅ deployed, working. Terrain Y is true world
  height. Includes a `coord_version` migration on `custom_wmo_placements.mesh_y`
  and `custom_terrain_sculpts.delta_y`. Kill switch:
  `"WorldEditor": { "SkipCoordMigration": true }`.
- **M1.2 deltaTime** ✅ deployed. `THREE.Clock`, clamped 100 ms. Movement is
  yards/second, ground snap `1 - exp(-21.4*dt)`, terrain probe a real 20 Hz.
- **M4.1/M4.2 playable character** ✅ deployed, mostly working.
  `character.js` (`PlayerCharacter`) + `character-control.js`
  (`CharacterController`), a faithful port of `Engine/Camera.cs`,
  `Engine/ClientWindow.cs`, `Program.cs` ~1262-1372, `Player/CharacterController.cs`.
  Last reported: turning works, terrain looks good, strafe fix unverified.
- **Terrain splatting** ✅ deployed, working — "terrain looks much better".
  `GET /WorldEditor/TerrainSplat` + `terrain-splat.js`. ~61 texels/yard, up from
  0.26 yd/texel.
- **One world lighting model** ✅ committed, unverified. `world-light.js`.
- **Animation** ✅ committed. `DefaultAnimationsToBake` `{0,4,5}` → `{0,4,5,13,37,38,39,40}`.
- **Closed eyes** ✅ committed, unverified. `CharacterSkinCompositor.PaintRegion`
  is now size-aware.
- **`.mdl` → `.m2`** ✅ deployed.

### Session 2 — all committed, **none verified in a browser**

#### Foliage / ground clutter (was §5.1)

New: `Services/GroundEffectTables.cs`, `wwwroot/js/worldeditor/foliage.js`.
Changed: `AdtTerrainReader.cs`, `WorldEditorController.cs`, `index.js`, `ui.js`,
`streaming.js`.

- `AdtTerrainReader` now parses MCNK header **0x40** (2 bits per cell, the
  authored ground-effect layer index) and **0x50** (1 bit per cell, "place
  nothing here"), with MSUIClient's accessors ported verbatim.
- `GET /WorldEditor/Foliage?preset=&tileGridX=&tileGridY=` returns, per tile:
  `recipes` (density + doodads with resolved MPQ paths, weights and kinds),
  `cellRecipeBase64` (Uint16 × 16384, the authored answer), `cellRecipeAlphaBase64`
  (the alpha-sampling guess, for A/B), `cellFlagsBase64` (bit 0 masked, bit 1 hole).
  Process-lifetime cached.
- The browser does the scatter, because it is camera-dependent. Options →
  **Foliage** (off by default), **Density**, **Draw Radius**.
- Fade window is linked to Radius by default (`FadeStartFraction` 0.66). Radius
  alone does nothing past the fade window — that was a real bug in MSUIClient.

#### Light.dbc exterior lighting + day/night (was §5.2)

New: `Services/WowDbcFile.cs`, `Services/LightTables.cs`,
`wwwroot/js/worldeditor/world-lighting.js`.
Changed: `GroundEffectTables.cs` (refactored onto the shared reader),
`WorldEditorController.cs`, `index.js`, `ui.js`.

- `GET /WorldEditor/Lighting?preset=` returns the whole map's chain: zones with
  the coordinate convention already applied, plus the curves each references.
  Colours stay **packed**; the browser decodes both bracketing keys and
  interpolates per channel.
- `world-lighting.js` does the resolve (falloff blending, farthest-first over the
  map default), the evaluate, a **screen-space five-band sky**, and `describe()`.
- It drives the **existing** `LightingRig`, so terrain, WMOs, doodads, the
  character and the grass all move together. `sun.intensity = 3.5 * sunStrength`
  and `ambient.intensity = hemi.intensity = ambientStrength` make **1.0 mean "use
  the data exactly"** — that is the correctness check.
- Options → **Light.dbc**, **Authored Sky**, **Time of Day**, **Day Cycle**.
  Off by default. Switching off restores the rig bit-for-bit from a snapshot.

#### Water split by liquid type (part of §5.3)

New: `wwwroot/js/worldeditor/water.js`.
Changed: `WorldEditorController.cs`, `streaming.js`.

- `GET /WorldEditor/Water` now emits `liquidTypesBase64` — one legacy type code
  **per vertex** (1 ocean, 3 slime, 4 river, 6 magma), from `MclqLayer.LiquidType`
  on the ADT path and from `tflag & 0x07` on the WMO MLIQ path. The tile-wide
  `liquidType` is still sent for back-compat.
- The client partitions the index buffer by type into draw groups on one mesh,
  each with its own material. No shader. Lava is no longer blue and Undercity's
  slime is no longer a canal.
- **Ocean and river colours are still ours, and they should not be.** Bands 13-16
  and the `LightParams` alphas are already on the wire from `/WorldEditor/Lighting`
  — see §5.3. Slime and magma genuinely are not in the data.
- `_unloadTile` now disposes a material **array**; missing that leaks one
  material per tile per type.

#### MCNR authored terrain normals (was §5.5)

Changed: `WorldEditorController.cs`, `streaming.js`, `ui.js`.

- Both heightmap endpoints emit `normalsBase64` — 129×129×3 signed bytes, axis
  converted server-side once.
- `buildTerrainGeometry` keeps **both** the computed and authored sets on
  `geo.userData`; Options → **MCNR Normals** swaps them with no reload.
- Why: `computeVertexNormals()` per tile has no neighbours across the seam, so
  every ADT boundary picked up a lighting crease that followed the tile grid.

---

## 3. Ground truth extracted (do not re-derive)

### Coordinates

- **Heading:** `mesh.rotation.y = yaw - PI/2`. `+PI/2` faces the camera.
- **Movement basis (three.js):** `forward = (sin yaw, cos yaw)`,
  `right = (-cos yaw, sin yaw)`.
- MSUIClient's "+90 degrees" is in **WoW space** and does not transfer.

### The cell / vertex grid mapping — load-bearing, used by three systems

```
gridCol = chunk.IndexX * 8 + cx      -> three.js +X
gridRow = chunk.IndexY * 8 + cy      -> three.js +Z
foliage cell index   = gridRow * 128 + gridCol
V9 vertex index      = gridRow * 129 + gridCol
MCVT / MCNR entry    = row * 17 + col      (9 outer + 8 inner per row)
```

The **IndexX/IndexY swap looks like a typo. It is not.** It was derived from
MSUIClient's `chunkX = originX - IndexY*8*cell` / `chunkY = originY - IndexX*8*cell`
and the ADT-space mapping, and then found to be **independently confirmed** by
`PatchMcvtDeltas`, which already writes sculpted heights back with
`v9Index = (IndexY*8+row)*129 + (IndexX*8+col)` and round-trips through the real
client. Two derivations, one answer.

### Mesh → WoW world (the HUD `.gps` transform, verified)

```
modfPosX = (meshX / tileWidthMesh + 0.5 + centerGridY) * 533.333
modfPosZ = (meshZ / tileWidthMesh + 0.5 + centerGridX) * 533.333
wowX = 32*533.333 - modfPosZ        wowY = 32*533.333 - modfPosX
```

Consequences: `sceneX = -wowY`, `sceneZ = -wowX`, `sceneY = wowZ`. Any WoW-space
direction (sun, normals) converts as `(dx,dy,dz) -> (-dy, dz, -dx)`.

> `readUrlTeleport()` in `index.js` appears to use `gridX` where this formula
> uses `gridY`. It was not touched this session. If World Map teleports land on
> the wrong axis, that is the first place to look.

### Terrain splat orientation

`transposeAlpha = false`. Mesh UV `u = col/(w-1)`, `v = 1 - row/(h-1)`; the splat
shader flips V **once**; atlas and LUT are both `flipY = false`.
`TEXTURE_SCALE = 8.0`.

### Movement / camera / input

```
RUN 7.0   WALK 2.5   BACKWARD 4.5 (a distinct SPEED, not a scale)
turn 2.8 rad/s   tilt = turn * 0.6   radius 0.4  height 2.1  maxSlope 55
gravity 19.29110527  jumpVelocity 7.9558  terminal 60.148
pitch 0.35  distance 9 (1.5..40)  EyeHeight 2.2  FOV 70  PitchLimit 1.45
mouseSensitivity 0.004  clearance 0.35  restoreSpeed 8.0
EaseOrbitBehind: EXPONENTIAL, 0.15s time constant.  Zoom: ADDITIVE.
FoldOrbitIntoFacing: on the right-button DOWN TRANSITION only.
turn = Axis(Left,Right) + (if !mouseSteering) Axis(A,D)
strafe = Axis(E,Q)     + (if  mouseSteering) Axis(D,A)
Letter bindings resolve by produced CHARACTER; arrows/Page/Space/Shift by code.
```

Vanilla **runs by default**; Shift walks.

### Foliage

```
Radius 45   DensityScale 0.5   MaxPerCell 6   MaxInstances 24000
RescatterDistance 8   Scale 1.0 +/-0.25   AlphaCutoff 0.4
Wind 0.06 / 1.4   LinkFadeToRadius on, FadeStartFraction 0.66
Chain: MCLY.EffectId -> GroundEffectTexture.dbc -> GroundEffectDoodad.dbc
GroundEffectTexture is 7 FIELDS in 1.12: density at field 5, NO weights.
Models are bare names; they live under World\NoDXT\Detail\ (mostly) as .m2.
```

**The rule that matters most:** every random value for a tuft is drawn *before*
any rejection test, and **no `continue` may appear between the first and last
`rng` call**. A camera-dependent test that skips draws makes the stream position
depend on where you are standing, and the whole cell reshuffles as you walk.

### Exterior lighting

```
Light.dbc -> LightParams.dbc -> LightIntBand.dbc (18 colours) + LightFloatBand.dbc (6 scalars)
Band rows for LightParams P: int P*18-17..P*18, float P*6-5..P*6. BY ID, not row.
7668 = 426 x 18 and 2556 = 426 x 6 exactly — check that arithmetic first.
Positions / falloff radii / fog end are stored YARDS x 36.
Band times are HALF-MINUTES from midnight, 0..2880. Curves WRAP across midnight.
Fog start is NOT a distance: float band 1 is a 0..0.999 multiplier, start = end * mult.
Azeroth noon: fog 125..500, ambient (0.408, 0.510, 0.604).
Colour packing 0x00RRGGBB. Convention: X = 17066.666 - dbc.Z, Y = 17066.666 - dbc.X, Z = dbc.Y.
Sky band stops 0.45 / 0.18 / 0.06 — MSUIClient's OWN GUESS, still unverified.
Sun direction is ours: six sunrise, twelve noon, eighteen sunset.
```

Elwynn Forest has **no** dedicated `Light.dbc` row. At Northshire only the map
default applies, and that is correct, not a bug.

### MCNR

Stored `(x, z, y)` with 127 = 1.0, in WoW space. Flat ground is `(0, 127, 0)`.
Converted server-side to `scene = (-raw[2], raw[1], -raw[0])`.

**MCCV does not exist in vanilla 1.12 ADTs** — it is a WotLK-era chunk. The
earlier plan's "MCNR/MCCV" should read MCNR only.

### Animation

`M2Sequence.IsLooping` reads flag `0x20` and **that bit is not a loop flag**.
One-shots are exactly `{37, 39}`. Key selection uses the sequence's absolute
timestamp window, not `Ranges[seqIdx]`. **No strafe clips on land.**

---

## 4. Incident log — what broke and why

| Bug | Cause | Lesson |
|---|---|---|
| **Server SIGABRT, reproducible** | `MigrateLegacyCoords` called itself. Three call-site injections used a bare `str.replace()` instead of the count-asserted helper; the anchor also appeared *inside* the method, and the 12-space anchor is a substring of the 16-space one. | **Never bypass the asserted-count replace helper.** |
| **Camera flew backwards** | Third-person offset applied as a **delta every frame**, never undone. | One authority per transform. |
| **Character rendered black** | `color.multiplyScalar(0.62)` in a `traverse()` — all ~14 geosets **share one material**. 0.62¹⁴ ≈ 0.0012. | `traverse()` walks meshes, not materials. |
| **Character faced the camera** | `yaw + PI/2`. MSUIClient's "+90" was read as corroboration; it is a different basis. | A matching number is not a matching derivation. |
| **Blank face, washed-out limbs** | `baseSkin` passed to `unequipAll` but not `equipMultiple`. | Always pass `baseSkin` to every `equip.js` call outside the items page. |
| **Strafe dead, 3 rounds** | `e.code` is physical-position on a **US** layout; then matching both code and character let one Dvorak keypress satisfy two bindings. | Letters by character, everything else by code. |

Two historical MSUIClient bugs that this session's harnesses were built to catch,
and did:

| Bug | Symptom | Check that catches it |
|---|---|---|
| Grass re-scatters as you walk | Continuous churn, not an occasional glitch — every cell takes its turn at the radius edge | `harness/run.mjs` "tufts do not reshuffle when the camera moves" |
| Colour bands share the scalar sampler | Green ambient, cyan fog, dark-purple sun at 11:11 — while every scalar band in the same rows reads perfectly | `harness2/run.mjs` "colour bands interpolate PER CHANNEL", which reproduces the exact (0.498, 0.502, 0.502) |

---

## 5. What is NOT done

### 5.1 Verify session 2 in a browser — **next**

Nothing from session 2 has been seen running. In rough order of what a first look
should check:

1. **Foliage on, walk to the Northshire road.** Grass must not creep onto the
   cobblestone. That single test exercises the 0x40 layer map, the 0x50 mask and
   the whole DBC chain. If grass reshuffles as you walk, the harness rule in §3
   was broken somewhere.
2. **Light.dbc on at Northshire.** The console prints the convention self-test —
   *Light 77 → (-8801, 579), N yd from Stormwind*. If it says FAILED, stop:
   every zone light is in the wrong place. Then `we.lighting.print()` and compare
   fog against **125 .. 500** and ambient against **(0.408, 0.510, 0.604)**.
3. **MCNR Normals on/off.** If the world lights as though it were on its side,
   the axis conversion in `BuildTileNormalsBase64` is wrong; the toggle is there
   so that is a click, not a rebuild.
4. **Authored Sky.** The band *heights* are guesses (§3). Colours coming out
   plausibly with a wrong-looking gradient means the stops need tuning, not the
   data.

### 5.2 Per-group WMO transport, then portals

- `GET /WorldEditor/WmoModel` **merges all groups into one buffer** — portal
  culling is impossible until it doesn't. Spec in the plan doc §4 (M1.4).
- Also send raw `MOMT.blendMode` (currently pre-digested to booleans), MOCV,
  MOGN/MOGI names, and reject antiportals (`GroupFlags & 0x04000000`).
- `ALWAYS_DRAW (0x10000)` + low vertex count = authored distance impostors
  (Stormwind's skyline). Exclude by flag **first**.
- Then `MSUIClient/PLAN_10_WMO_PORTALS.md` — D1 (containing-group readout)
  alone, verified, before any traversal.

### 5.3 Water — authored colours (the type split is done)

Liquid TYPE now reaches the client and each type draws in its own colour. What is
left is the **authored** part: `/WorldEditor/Lighting` already ships **LightIntBand
13-16** (ocean close/far, river close/far) and the **LightParams water alphas** on
every params entry, resolved and consumed by nothing. `water.js`'s ocean and river
values are placeholders and say so.

Wire them through `world-lighting.js` (they ride the same `enabled` switch — one
decision about whether the client believes the data, and water must not be able to
disagree with the sky about it). Source: `MSUIClient/World/LiquidRenderer.cs`,
`SYSTEM_WATER.md`, `PLAN_12_WATER_COLOURS.md`.

**Guard the alphas.** A deep alpha of 0 makes every lake invisible, and an
unauthored `LightParams` row reads as 0 rather than as absent. MSUIClient rejects
the whole alpha pair when the deep alpha is <= 0.01 and keeps the shader's own
constants — same shape as the fog-end guard, and for the same reason: data may
change the look, it may not delete the world.

Still not done here either: close/far distance blending, shore fade, and any
animation at all (vanilla scrolls a texture; this is a flat colour).

### 5.4 Streaming / residency

`MSUIClient/SYSTEM_STREAMING.md` (47 KB). Web-specific: no Web Workers anywhere,
all geometry is JSON number arrays, all textures base64 PNG. KTX2 + workers are
the levers.

**Concretely measurable now:** the heightmap endpoints send positions as plain
JSON float arrays — 16641 × 3 numbers ≈ half a megabyte per tile, nine of them on
a preset load. The normals added this session are base64 signed bytes (49 KB) for
exactly that reason. Positions are the obvious next thing to pack.

### 5.5 Smaller, known

- **Colour-space inconsistency in walk mode.** `render.js` bypasses
  `EffectComposer` — and therefore `OutputPass` — when `rig.walk.mode` is on.
  Built-in materials get three.js's output conversion on a direct render; custom
  `ShaderMaterial`s (terrain splat, foliage, sky) do not. Character mode disables
  walk mode so it takes the composer path, but **walk mode will look different**.
  Needs a screenshot comparison, then either drop the bypass or add the encode.
- **Strafe torso split** — blocked: needs key-bone metadata in the GLB.
  MangosSuperUI's M2 reader doesn't parse `KeyBoneId`/`KeyBoneLookup` at all.
  Twist bone is key bone 5 (Waist), fallback 4 (SpineLow); `TorsoFollow 0.66`;
  `phi = atan2(-sideness, forwardness)`.
- **M2 blend modes / two-pass** — server sends no render flags; every doodad gets
  a blanket `DoubleSide + alphaTest 0.5 + transparent`.
- **`WowSocketBridge.cs`** is on a "dead code" list. It is **not** dead — a web
  client needs a WebSocket↔TCP relay. Do not delete.
- **`NearbyObjects: DEDUP skipped`** repeats every pump for the same WMOs —
  placements are rebuilt rather than cached. Real wasted work.
- **`VmapFormat.cs`** carries the LIQU 4-byte-short bug MSUIClient fixed.
- ~~**`BlpDecoder`** 1-bit alpha~~ — **already correct** in MangosSuperUI
  (`DecodePalettized`, `alphaDepth` case 1, returns 0/255). Stale drift entry.
- **Foliage: no per-zone override table.** The per-kind toggles are a blunt
  global stand-in for retail's hand curation.
- **Lighting: no skybox models, no clouds, no weather.** `LightSkybox`, bands
  9-12 and every params set other than `ParamsClear` are parsed or reachable and
  unused.

---

## 6. Method — this is what worked

1. **Read the MSUIClient source.** Stage the file and read it. Not the handbook,
   not memory, not this doc.
2. **Edit with asserted-count replacements.** A helper that throws unless the
   anchor matches exactly N times. The one time it was bypassed, the server
   crashed. `/home/claude/work/edit.py` is the helper; each patch is a small
   script beside it.
3. **Build a Node ESM harness that imports the ACTUAL shipped module**, with
   `three` and `net.js` stubbed. Then **mutation-test the harness**: reintroduce
   the historical bug in a scratch copy and confirm the check fails. A check that
   has never failed is not yet a check.
   - `/home/claude/harness` — foliage, 22 checks.
   - `/home/claude/harness2` — lighting, 31 checks.
   - `/home/claude/harness3` — water type partition, 14 checks.
4. **Verify structure before committing** — brace/paren balance against the
   original baseline, ES-module parse of every file, cross-module import
   resolution, CRLF.
5. **No .NET SDK in the sandbox** (`dot.net` is blocked). C# is committed
   uncompiled — say so, and keep changes structurally conservative.

---

## 7. Suggested opening prompt for the next session

> Read `MSUI_WEB_CLIENT_HANDOFF.md` and `/areas/msui-world-editor.md` in memory.
> Continue the MSUIClient → web client port. Start with §5.1 (verifying foliage,
> Light.dbc lighting and MCNR in a browser), then water (§5.3), then per-group
> WMO transport (§5.2). Read the MSUIClient source files directly — do not work
> from summaries. Build a harness and mutation-test it. Work continuously without
> stopping for approval; commit to the repo as you go. When you run low on
> context, update this handoff.

---

## 8. Open questions for Nico

1. **Terrain source** — heights still come from the VMaNGOS `.map`, not ADT
   MCVT. Now that MCNR normals come from the ADT, the two sources are mixed.
   Switch wholesale and retire the `.map` path?
2. **Vertical exaggeration** — was the old 3.5× stretch load-bearing for
   sculpting, or incidental?
3. **Tone mapping** — the rig sums past 1.0 with `NoToneMapping`. ACES filmic is
   the systemic fix for over-exposure, but it changes how the whole world looks.
   Do it? (Related: the walk-mode composer bypass in §5.5.)
4. **Characters** — avatar only, or the full unit renderer (NPCs, creatures)?
5. **Networking (M9)** — start it in parallel?
6. **Foliage default** — it is off by default because a world editor is not
   always a place you want grass in the way. Should the player-facing mode
   default it on?
