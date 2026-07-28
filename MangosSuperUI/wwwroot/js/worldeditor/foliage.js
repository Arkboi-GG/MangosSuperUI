// foliage.js — vanilla ground-effect clutter (grass, ferns, flowers, pebbles).
//
// ═══════════════════════════════════════════════════════════════════════════
// PORTED FROM MSUIClient
//   World/FoliageRenderer.cs   the scatter, the rejection order, every knob
//   Shaders/grass.vert         wind sway (bend grows with blade height squared)
//   Shaders/grass.frag         distance fade folded into the alpha cutout
//   SYSTEM_FOLIAGE.md          why each of those is what it is
// ═══════════════════════════════════════════════════════════════════════════
//
// THE BAR (SYSTEM_FOLIAGE §0)
//   Foliage is not decoration scattered by taste. Vanilla has a complete
//   authored data chain saying which clutter appears on which square of ground:
//
//     MCLY.EffectId -> GroundEffectTexture.dbc -> GroundEffectDoodad.dbc
//
//   The single clearest test of whether it is right is that GRASS MUST NOT
//   CREEP ONTO THE NORTHSHIRE COBBLESTONE. Every wrong version of this system
//   fails that test, and the two mechanisms that pass it — the MCNK 0x40 cell
//   layer map and the 0x50 no-doodad mask — are hand-authored data, not
//   anything derivable from the alpha maps. Both arrive from the server already
//   resolved (see WorldEditorController.Foliage).
//
// WHAT RUNS WHERE
//   Server: the authored answer per cell, the DBC chain, model path resolution.
//   Here:   the scatter (camera-dependent, re-runs as you walk), the instancing
//           and the shader.
//
// THE ONE RULE THAT IS EASY TO BREAK (SYSTEM_FOLIAGE §1.1a)
//   Every random value for a tuft is drawn BEFORE any rejection test, and no
//   `continue` may appear between the first and last rng call of the placement
//   loop. The seed is per cell and deterministic, so the tufts are supposed to
//   be stable — but if a camera-dependent test (the radius check) can skip
//   draws, the stream position at tuft i+1 depends on where you are standing,
//   and every remaining tuft in that cell gets a new position, model, rotation
//   and size on every re-scatter. Cells fully inside the radius never show it;
//   cells straddling the edge reshuffle constantly, and as you walk EVERY cell
//   takes its turn at the edge. That reads as continuous churn, and it cost
//   MSUIClient a full debugging round because the seed looked correct — it was.
//   The CONSUMPTION was not.
//
// ONE DELIBERATE DIVERGENCE FROM grass.frag
//   grass.frag's ambient term is flat: `uAmbientColor * uAmbientIntensity`,
//   with no hemisphere factor, while terrain.frag / wmo.frag / character.frag
//   all use `mix(0.62, 1.0, n.up * 0.5 + 0.5)`. Its own header states the
//   intent — "same lighting/fog model as the doodad and terrain shaders so
//   grass sits into the ground it grows from" — so the flat ambient is an
//   omission against that intent rather than a decision. This file uses the
//   shared world model (same source of truth as terrain-splat.js and
//   world-light.js), because grass that lights differently from the ground it
//   grows out of is the exact failure character.frag's header describes.

import * as THREE from 'three';
import { getJSON } from './net.js';
import {
    worldSunIntensity, worldAmbientIntensity, worldSunDirection
} from './terrain-splat.js';
import { applyWorldLighting, forgetWorldLighting } from './world-light.js';

// ─────────────────────────────────────────────────────────────────────────────
// Knobs — MSUIClient FoliageRenderer defaults, verbatim
// ─────────────────────────────────────────────────────────────────────────────

export const FOLIAGE_DEFAULTS = {
    radius: 45,             // scatter/draw radius, yards
    densityScale: 0.5,      // multiplies the DBC density
    maxPerCell: 6,          // cap doodads per ~4.17yd cell
    maxInstances: 24000,    // hard ceiling
    rescatterDistance: 8,   // rescatter after moving this far
    scale: 1.0,
    scaleJitter: 0.25,
    windStrength: 0.06,
    windSpeed: 1.4,
    linkFadeToRadius: true,
    fadeStartFraction: 0.66,
    fadeStart: 30,
    fadeEnd: 45,
    alphaCutoff: 0.4,
    brightness: 1.0,
    useCellLayerMap: true,
    useNoDoodadMask: true,
    skipHoles: true,
};

export const FOLIAGE_KINDS =
    ['Grass', 'Flower', 'Bush', 'Rock', 'Plant', 'Mushroom', 'Other'];

const CELLS_PER_SIDE = 128;
const CHUNK_GUARD = 24;     // reject a whole chunk beyond Radius + this

// ─────────────────────────────────────────────────────────────────────────────
// Deterministic PRNG
// ─────────────────────────────────────────────────────────────────────────────
//
// MSUIClient seeds System.Random with HashCode.Combine(col,row,IndexX,IndexY,
// cx,cy). The exact generator does not matter — what matters is that the seed
// depends only on the cell, and that the stream position depends only on
// (cell, i). Grid col/row already fold IndexX/cx and IndexY/cy together, so the
// seed here is (tileGridX, tileGridY, gridCol, gridRow).

function seedFor(gx, gy, col, row) {
    let h = 2166136261 >>> 0;
    h = Math.imul(h ^ (gx & 0xffff), 16777619) >>> 0;
    h = Math.imul(h ^ (gy & 0xffff), 16777619) >>> 0;
    h = Math.imul(h ^ (col & 0xffff), 16777619) >>> 0;
    h = Math.imul(h ^ (row & 0xffff), 16777619) >>> 0;
    return h >>> 0;
}

/** mulberry32 — small, fast, and its state is one uint32. */
function makeRng(seed) {
    let a = seed >>> 0;
    return function next() {
        a = (a + 0x6D2B79F5) >>> 0;
        let t = a;
        t = Math.imul(t ^ (t >>> 15), 1 | t);
        t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
        return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
}

// ─────────────────────────────────────────────────────────────────────────────
// Grass material
// ─────────────────────────────────────────────────────────────────────────────
//
// The old CUSTOM ShaderMaterial rendered every grass card as an OPAQUE square —
// the alpha cutout never took, even though the server's PNG carries a correct
// alpha channel (BlpDecoder decodes DXT1 punch-through to alpha 0). Rather than
// keep debugging a bespoke shader, grass now uses the SAME material the tree
// doodads use and that already works: a MeshStandardMaterial with three.js'
// built-in `alphaTest` cutout and the world lighting model (applyWorldLighting).
// The wind sway is appended to that material's onBeforeCompile, so grass keeps
// its motion AND finally lights like the ground it grows out of.
//
// One small regression vs the old shader: the per-blade distance FADE is gone
// (three.js alphaTest is a hard cut). The scatter radius already bounds where
// grass appears, so it pops in/out at the radius edge instead of dissolving —
// acceptable, and re-addable later as an alpha ramp if it reads badly.

function makeGrassMaterial(map, rig) {
    const mat = new THREE.MeshStandardMaterial({
        map,
        alphaTest: 0.5,          // hard cutout, exactly like the tree doodads
        transparent: true,
        side: THREE.DoubleSide,  // grass is two-sided cards
        metalness: 0.0,
        roughness: 0.9,
        fog: true,
    });

    // Same sun + hemispheric-ambient model as terrain / doodads / character,
    // but with skyNormal: grass billboards are vertical, so their true normals
    // are horizontal and an overhead sun would miss them entirely. skyNormal
    // lights each blade with a world-up normal so the field reads as sunlit.
    applyWorldLighting(mat, rig, { skyNormal: true });

    // Wind sway appended to the world-lighting onBeforeCompile. Bend grows with
    // blade-height^2 so the base stays planted; the phase uses ABSOLUTE world XZ
    // so the field does not pulse as the camera moves. Applied to `transformed`
    // (model-local, M2 Y-up) before the instance/model transform.
    const base = mat.onBeforeCompile;
    const windU = {
        uTime: { value: 0 },
        uWindStrength: { value: FOLIAGE_DEFAULTS.windStrength },
        uWindSpeed: { value: FOLIAGE_DEFAULTS.windSpeed },
    };
    mat.userData._windU = windU;
    mat.onBeforeCompile = (shader) => {
        if (base) base(shader);
        shader.uniforms.uTime = windU.uTime;
        shader.uniforms.uWindStrength = windU.uWindStrength;
        shader.uniforms.uWindSpeed = windU.uWindSpeed;
        shader.vertexShader =
            'uniform float uTime;\nuniform float uWindStrength;\nuniform float uWindSpeed;\n' +
            shader.vertexShader.replace(
                '#include <begin_vertex>',
                '#include <begin_vertex>\n' +
                '  {\n' +
                '    vec4 _gw =\n' +
                '      #ifdef USE_INSTANCING\n' +
                '        instanceMatrix *\n' +
                '      #endif\n' +
                '        vec4(transformed, 1.0);\n' +
                '    _gw = modelMatrix * _gw;\n' +
                '    float _bh = max(transformed.y, 0.0);\n' +
                '    float _ph = _gw.x * 0.15 + _gw.z * 0.11 + uTime * uWindSpeed;\n' +
                '    float _amt = uWindStrength * _bh * _bh;\n' +
                '    transformed.x += sin(_ph) * _amt;\n' +
                '    transformed.z += cos(_ph * 0.83) * _amt;\n' +
                '  }');
    };
    mat.customProgramCacheKey = () => 'grass-windlit-v1';
    mat.needsUpdate = true;
    return mat;
}

function textureFromDataURI(uri) {
    const tex = new THREE.TextureLoader().load(uri);
    // flipY MUST be false: the foliage geometry is M2 doodad geometry served as
    // GLB, so its UVs follow the glTF convention (V=0 at the TOP of the image).
    // A flipY=true texture renders vertically mirrored on that geometry — which
    // is exactly the "grass is upside down" bug (blades hang down, and the atlas
    // slices sample the wrong band, leaving the pale square above each tuft).
    tex.flipY = false;
    tex.wrapS = THREE.RepeatWrapping;
    tex.wrapT = THREE.RepeatWrapping;
    // r162 decodes sRGB via the texture's internal format (SRGB8_ALPHA8), not a
    // shader chunk, so a raw texture2D() in a custom shader gets linear values.
    // Same treatment terrain-splat.js gives the tileset — they must match.
    tex.colorSpace = THREE.SRGBColorSpace;
    return tex;
}

// ─────────────────────────────────────────────────────────────────────────────
// FoliageField
// ─────────────────────────────────────────────────────────────────────────────

export class FoliageField {
    constructor(editor) {
        this.editor = editor;

        this.enabled = false;
        Object.assign(this, FOLIAGE_DEFAULTS);

        this.group = new THREE.Group();
        this.group.name = 'foliage';
        this.group.visible = false;
        editor.viewport.scene.add(this.group);

        // tileKey -> { state, recipes, cellRecipe, cellRecipeAlpha, cellFlags }
        this._tiles = {};
        // modelPath -> { state, parts: [{geometry, material, mesh}] }
        this._models = {};
        this._modelQueue = [];
        this._modelsInFlight = 0;

        // modelPath -> Array of {x,y,z,yaw,scale}, rebuilt every scatter
        this._batches = new Map();

        this._lastScatterX = 0;
        this._lastScatterZ = 0;
        this._hasScattered = false;
        this._retryTimer = 0;
        this._time = 0;

        // Per-kind curation: one visibility toggle and one keep-probability per
        // clutter kind. This is where retail's hand curation gets reproduced —
        // the raw DBCs do not encode it.
        this.kindEnabled = {};
        this.kindDensity = {};
        for (const k of FOLIAGE_KINDS) { this.kindEnabled[k] = true; this.kindDensity[k] = 1; }

        // Readouts. Scatter and draw are DIFFERENT SCHEDULES and must never be
        // averaged into one number — a scatter runs about once a second and
        // rebuilds everything, the draw runs every frame and is small.
        this.instanceCount = 0;
        this.scatterCells = 0;
        this.scatterCandidates = 0;
        this.maskedCells = 0;
        this.holeCells = 0;
        this.deferredTiles = 0;
        this.deferredModels = 0;
        this.scatterCount = 0;
        this.scatterMs = 0;
        this.kindInstances = {};

        this._m4 = new THREE.Matrix4();
        this._pos = new THREE.Vector3();
        this._quat = new THREE.Quaternion();
        this._scl = new THREE.Vector3();
        this._eul = new THREE.Euler(0, 0, 0, 'YXZ');

        editor.signals.presetClearing.add(() => this.clearAll());
    }

    get effectiveFadeEnd() {
        return this.linkFadeToRadius ? this.radius : this.fadeEnd;
    }

    get effectiveFadeStart() {
        return this.linkFadeToRadius
            ? this.radius * Math.max(0, Math.min(1, this.fadeStartFraction))
            : this.fadeStart;
    }

    setEnabled(v) {
        this.enabled = !!v;
        this.group.visible = this.enabled;
        if (this.enabled) this.forceRescatter();
    }

    /** Any coverage change takes effect on the next frame, not after 8 yards. */
    forceRescatter() { this._hasScattered = false; this._retryTimer = 0; }

    /**
     * Live density multiplier — the same knob as Options → Density, clamped to
     * 0..2×. Used by the in-world foliage control next to the character.
     */
    setDensityScale(v) {
        v = Math.max(0, Math.min(2, v));
        if (this.densityScale === v) return;
        this.densityScale = v;
        this.forceRescatter();
    }

    setKindEnabled(kind, on) {
        if (this.kindEnabled[kind] === !!on) return;
        this.kindEnabled[kind] = !!on;
        this.forceRescatter();
    }

    setKindDensity(kind, keep) {
        keep = Math.max(0, Math.min(1, keep));
        if (this.kindDensity[kind] === keep) return;
        this.kindDensity[kind] = keep;
        this.forceRescatter();
    }

    // ── frame ────────────────────────────────────────────────────────────────

    tick(viewport, dt) {
        if (!this.enabled) return;
        this._time += dt;

        const cam = viewport.rig.camera;
        this._drainModelQueue();
        this._maybeScatter(cam.position.x, cam.position.z, dt);
        this._syncUniforms(viewport);
    }

    _syncUniforms(viewport) {
        // Lighting + fog now ride the shared world-lighting model (synced globally
        // by render.js syncWorldLighting) and the material's own fog. Grass only
        // needs its wind clock advanced here.
        for (const path in this._models) {
            const m = this._models[path];
            if (!m.parts) continue;
            for (const part of m.parts) {
                const wu = part.material.userData && part.material.userData._windU;
                if (!wu) continue;
                wu.uTime.value = this._time;
                wu.uWindStrength.value = this.windStrength;
                wu.uWindSpeed.value = this.windSpeed;
            }
        }
    }

    _maybeScatter(camX, camZ, dt) {
        if (this._hasScattered) {
            const dx = camX - this._lastScatterX, dz = camZ - this._lastScatterZ;
            if (dx * dx + dz * dz < this.rescatterDistance * this.rescatterDistance) {
                // Something the last scatter needed was still loading. Retry,
                // but on a timer rather than every frame: MSUIClient's deferred
                // tiles resolve in microseconds off an in-process cache, ours
                // resolve over HTTP, and a per-frame retry would rebuild the
                // whole resident set 60 times a second while a model downloads.
                if (this.deferredTiles === 0 && this.deferredModels === 0) return;
                this._retryTimer -= dt;
                if (this._retryTimer > 0) return;
            }
        }
        this._retryTimer = 0.25;
        this._lastScatterX = camX;
        this._lastScatterZ = camZ;
        this._hasScattered = true;
        this._scatter(camX, camZ);
    }

    // ── the scatter ──────────────────────────────────────────────────────────

    _scatter(camX, camZ) {
        const t0 = (typeof performance !== 'undefined') ? performance.now() : 0;

        this._batches.clear();
        this.scatterCells = 0;
        this.scatterCandidates = 0;
        this.maskedCells = 0;
        this.holeCells = 0;
        this.deferredTiles = 0;
        this.deferredModels = 0;
        for (const k of FOLIAGE_KINDS) this.kindInstances[k] = 0;

        const tg = this.editor.tileGrid;
        let total = 0;

        if (tg && !tg.isDungeon) {
            const radiusSq = this.radius * this.radius;
            const guard = this.radius + CHUNK_GUARD;
            const guardSq = guard * guard;

            outer:
            for (const key in tg.tiles) {
                const entry = tg.tiles[key];
                if (!entry || !entry.mesh || !entry.geo) continue;

                const frame = this._tileFrame(entry);
                if (!frame) continue;

                // Coarse tile reject before paying for the foliage fetch.
                const tileHalf = frame.cell * CELLS_PER_SIDE * 0.5;
                const tcx = frame.x0 + tileHalf, tcz = frame.z0 + tileHalf;
                if (Math.abs(tcx - camX) > guard + tileHalf) continue;
                if (Math.abs(tcz - camZ) > guard + tileHalf) continue;

                const data = this._tileData(entry.gridX, entry.gridY);
                if (!data) { this.deferredTiles++; continue; }
                if (data.state === 'loading') { this.deferredTiles++; continue; }
                if (data.state !== 'ready') continue;   // 'none' = an answer, not a miss

                const recipeMap = this.useCellLayerMap ? data.cellRecipe : data.cellRecipeAlpha;

                for (let chunkRow = 0; chunkRow < 16; chunkRow++) {
                    for (let chunkCol = 0; chunkCol < 16; chunkCol++) {
                        const ccx = frame.x0 + (chunkCol * 8 + 4) * frame.cell;
                        const ccz = frame.z0 + (chunkRow * 8 + 4) * frame.cell;
                        const ddx = ccx - camX, ddz = ccz - camZ;
                        if (ddx * ddx + ddz * ddz > guardSq) continue;

                        for (let cy = 0; cy < 8; cy++) {
                            for (let cx = 0; cx < 8; cx++) {
                                const gridCol = chunkCol * 8 + cx;
                                const gridRow = chunkRow * 8 + cy;
                                const idx = gridRow * CELLS_PER_SIDE + gridCol;

                                const flags = data.cellFlags[idx];
                                // Vanilla decides clutter per cell and reads both
                                // answers out of the MCNK header rather than
                                // deriving them. These two toggles are
                                // DIAGNOSTICS, not settings.
                                if (this.useNoDoodadMask && (flags & 1)) { this.maskedCells++; continue; }
                                // A holed cell has no terrain at all — it is the
                                // cut the artists made for a dungeon entrance.
                                // Scattering there is what puts shrubs growing
                                // through a mine's wooden beams.
                                if (this.skipHoles && (flags & 2)) { this.holeCells++; continue; }

                                const slot = recipeMap[idx];
                                if (slot === 0xFFFF) continue;
                                const recipe = data.recipes[slot];
                                if (!recipe || !recipe.doodads.length) continue;

                                const perCell = Math.max(0, Math.min(this.maxPerCell,
                                    Math.round(recipe.density * this.densityScale)));
                                if (perCell <= 0) continue;

                                this.scatterCells++;
                                this.scatterCandidates += perCell;

                                const rng = makeRng(
                                    seedFor(entry.gridX, entry.gridY, gridCol, gridRow));

                                for (let i = 0; i < perCell; i++) {
                                    // ── Draw EVERY random value for this tuft FIRST ──
                                    //
                                    // No `continue` may appear in this block. See
                                    // the file header: a camera-dependent test
                                    // that skips draws makes the stream position
                                    // depend on where you are standing, and the
                                    // whole cell reshuffles as you walk.
                                    //
                                    // Draw order mirrors MSUIClient's px (WoW X,
                                    // which is our grid ROW) then py (WoW Y, our
                                    // grid COL).
                                    const rRow = rng();
                                    const rCol = rng();
                                    const model = pickWeighted(recipe.doodads, rng);
                                    const keepRoll = rng();
                                    const yaw = rng() * Math.PI * 2;
                                    const jitter = rng();

                                    // ── Rejections below consume nothing ────────
                                    // Ordered cheapest-first, which is also a real
                                    // saving: the height lookup used to run before
                                    // the per-kind filter, so every in-radius
                                    // candidate paid for it even when its kind was
                                    // switched off and it was about to be dropped.
                                    const fCol = gridCol + rCol;
                                    const fRow = gridRow + rRow;
                                    const px = frame.x0 + fCol * frame.cell;
                                    const pz = frame.z0 + fRow * frame.cell;

                                    const dxp = px - camX, dzp = pz - camZ;
                                    if (dxp * dxp + dzp * dzp > radiusSq) continue;

                                    if (!this.kindEnabled[model.kind]) continue;
                                    const keep = this.kindDensity[model.kind];
                                    if (keep <= 0 || (keep < 1 && keepRoll > keep)) continue;

                                    // A model that FAILED must not count as
                                    // deferred, or the retry timer keeps
                                    // rebuilding the whole resident set forever
                                    // waiting for something that will never
                                    // arrive. Same shape as TryPeek returning
                                    // true with a null adt: "known to have
                                    // none" is an answer, not a miss.
                                    const ready = this._ensureModel(model.path);
                                    if (ready !== 'ready') {
                                        if (ready !== 'failed') this.deferredModels++;
                                        continue;
                                    }

                                    const h = this._sampleHeight(frame, fCol, fRow);
                                    if (h === null) continue;

                                    const s = this.scale *
                                        (1 - this.scaleJitter + jitter * this.scaleJitter * 2);

                                    let list = this._batches.get(model.path);
                                    if (!list) { list = []; this._batches.set(model.path, list); }
                                    list.push(px, h, pz, yaw, s);

                                    this.kindInstances[model.kind]++;
                                    if (++total >= this.maxInstances) break outer;
                                }
                            }
                        }
                    }
                }
            }
        }

        // Drop foliage data for tiles the grid has evicted. Walking across a
        // continent otherwise accumulates one 64KB payload per tile visited,
        // forever, for tiles that are no longer in the scene.
        if (tg && tg.tiles) {
            for (const k in this._tiles) if (!tg.tiles[k]) delete this._tiles[k];
        }

        this.instanceCount = total;
        this.scatterCount++;
        this._uploadBatches();

        this.scatterMs = ((typeof performance !== 'undefined') ? performance.now() : 0) - t0;
    }

    /**
     * Where a tile's mesh grid sits in world space, derived from the geometry
     * itself rather than from tileWidthMesh.
     *
     * This matters: the multi-tile Heightmap endpoint BAKES the tile offset into
     * the vertex positions and leaves mesh.position at the origin, while
     * SingleTileHeightmap emits centred positions and offsets via mesh.position.
     * Reading column 0 and column 1 straight out of the position attribute is
     * correct under both, and stays correct if either changes.
     */
    _tileFrame(entry) {
        if (entry._foliageFrame) {
            const f = entry._foliageFrame;
            f.x0 = f.baseX + entry.mesh.position.x;
            f.z0 = f.baseZ + entry.mesh.position.z;
            return f;
        }
        const attr = entry.geo && entry.geo.attributes && entry.geo.attributes.position;
        if (!attr) return null;
        const pos = attr.array;
        const n = pos.length / 3;
        const w = entry.vertsWidth || Math.round(Math.sqrt(n));
        if (!w || w * w !== n || w < 2) return null;

        const baseX = pos[0], baseZ = pos[2];
        const cell = pos[3] - pos[0];                 // col 1 minus col 0 -> +X
        const cellZ = pos[w * 3 + 2] - pos[2];        // row 1 minus row 0 -> +Z
        if (!(cell > 0) || !(cellZ > 0)) return null;

        const f = { baseX, baseZ, cell, w, pos, x0: 0, z0: 0 };
        f.x0 = baseX + entry.mesh.position.x;
        f.z0 = baseZ + entry.mesh.position.z;
        entry._foliageFrame = f;
        return f;
    }

    /** Bilinear height off the terrain grid. No raycast — 24,000 of those is a stall. */
    _sampleHeight(frame, fCol, fRow) {
        const w = frame.w, pos = frame.pos;
        let c0 = Math.floor(fCol), r0 = Math.floor(fRow);
        if (c0 < 0 || r0 < 0 || c0 > w - 2 || r0 > w - 2) return null;
        const fx = fCol - c0, fz = fRow - r0;

        const i00 = (r0 * w + c0) * 3 + 1;
        const i10 = i00 + 3;
        const i01 = ((r0 + 1) * w + c0) * 3 + 1;
        const i11 = i01 + 3;

        const a = pos[i00] + (pos[i10] - pos[i00]) * fx;
        const b = pos[i01] + (pos[i11] - pos[i01]) * fx;
        const h = a + (b - a) * fz;
        return Number.isFinite(h) ? h : null;
    }

    // ── per-tile foliage data ────────────────────────────────────────────────

    _tileData(gx, gy) {
        const key = gx + ',' + gy;
        let d = this._tiles[key];
        if (d) return d;

        const preset = this.editor.currentPreset;
        if (!preset) return null;

        d = { state: 'loading' };
        this._tiles[key] = d;

        getJSON('/WorldEditor/Foliage?preset=' + encodeURIComponent(preset) +
                '&tileGridX=' + gx + '&tileGridY=' + gy)
            .then((r) => {
                if (!r || !r.success) {
                    d.state = 'none';
                    if (r && r.notes && r.notes.length) console.info('[foliage]', r.notes.join(' | '));
                    return;
                }
                d.recipes = r.recipes || [];
                d.cellRecipe = new Uint16Array(b64ToBytes(r.cellRecipeBase64).buffer);
                d.cellRecipeAlpha = new Uint16Array(b64ToBytes(r.cellRecipeAlphaBase64).buffer);
                d.cellFlags = b64ToBytes(r.cellFlagsBase64);
                d.state = 'ready';
                if (r.notes && r.notes.length) console.info('[foliage]', r.notes.join(' | '));
                this.forceRescatter();
            })
            .catch((err) => {
                console.warn('[foliage] tile', gx, gy, 'failed:', err && err.message);
                d.state = 'none';
            });

        return d;
    }

    // ── models ───────────────────────────────────────────────────────────────

    /**
     * Model state, enqueueing a fetch on first sight.
     * 'ready' | 'queued' | 'loading' | 'failed'.
     */
    _ensureModel(path) {
        const m = this._models[path];
        if (m) return m.state;

        this._models[path] = { state: 'queued', parts: null };
        this._modelQueue.push(path);
        return 'queued';
    }

    _drainModelQueue() {
        const MAX_IN_FLIGHT = 4;
        while (this._modelQueue.length > 0 && this._modelsInFlight < MAX_IN_FLIGHT) {
            const path = this._modelQueue.shift();
            const rec = this._models[path];
            if (!rec || rec.state !== 'queued') continue;
            rec.state = 'loading';
            this._modelsInFlight++;

            getJSON('/WorldEditor/DoodadModel?path=' + encodeURIComponent(path))
                .then((data) => {
                    this._modelsInFlight--;
                    if (!data || !data.success || !data.positions) { rec.state = 'failed'; return; }
                    rec.parts = this._buildParts(data);
                    rec.state = rec.parts.length ? 'ready' : 'failed';
                    if (rec.state === 'ready') this.forceRescatter();
                })
                .catch((err) => {
                    this._modelsInFlight--;
                    rec.state = 'failed';
                    console.warn('[foliage] model', path, 'failed:', err && err.message);
                });
        }
    }

    _buildParts(data) {
        const posAttr = new THREE.Float32BufferAttribute(data.positions, 3);
        const normAttr = new THREE.Float32BufferAttribute(data.normals, 3);
        const uvAttr = new THREE.Float32BufferAttribute(data.uvs, 2);
        const allIndices = data.indices;
        const subs = data.submeshes ||
            [{ indexStart: 0, indexCount: allIndices.length, textureBase64: null }];

        const parts = [];
        for (const sub of subs) {
            if (!sub.indexCount) continue;
            // Grass is an alpha-cutout card. A submesh with no texture has no
            // cutout, so it would draw as an opaque quad — MSUIClient drops
            // those too ("no texture -> nothing to draw for grass").
            if (!sub.textureBase64) continue;

            const geo = new THREE.BufferGeometry();
            geo.setAttribute('position', posAttr);
            geo.setAttribute('normal', normAttr);
            geo.setAttribute('uv', uvAttr);
            geo.setIndex(new THREE.BufferAttribute(
                new Uint32Array(allIndices.slice(sub.indexStart, sub.indexStart + sub.indexCount)), 1));

            const material = makeGrassMaterial(
                textureFromDataURI(sub.textureBase64), this.editor.viewport.lighting);
            const mesh = new THREE.InstancedMesh(geo, material, 1);
            mesh.count = 0;
            mesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
            // Every instance is inside Radius of the camera by construction, so
            // per-mesh frustum culling can only ever be wrong here (the bounding
            // volume would have to be recomputed on every scatter to be right).
            mesh.frustumCulled = false;
            mesh.matrixAutoUpdate = false;
            this.group.add(mesh);

            parts.push({ geometry: geo, material, mesh, capacity: 1 });
        }
        return parts;
    }

    _uploadBatches() {
        for (const path in this._models) {
            const rec = this._models[path];
            if (rec.state !== 'ready' || !rec.parts) continue;

            const flat = this._batches.get(path);
            const count = flat ? flat.length / 5 : 0;

            for (const part of rec.parts) {
                if (count > part.capacity) {
                    // Grow to the next power of two and rebuild the attribute.
                    let cap = Math.max(64, part.capacity);
                    while (cap < count) cap *= 2;
                    this.group.remove(part.mesh);
                    part.mesh.dispose();
                    const mesh = new THREE.InstancedMesh(part.geometry, part.material, cap);
                    mesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
                    mesh.frustumCulled = false;
                    mesh.matrixAutoUpdate = false;
                    this.group.add(mesh);
                    part.mesh = mesh;
                    part.capacity = cap;
                }

                const mesh = part.mesh;
                for (let i = 0; i < count; i++) {
                    const o = i * 5;
                    this._pos.set(flat[o], flat[o + 1], flat[o + 2]);
                    this._eul.set(0, flat[o + 3], 0);
                    this._quat.setFromEuler(this._eul);
                    const s = flat[o + 4];
                    this._scl.set(s, s, s);
                    this._m4.compose(this._pos, this._quat, this._scl);
                    mesh.setMatrixAt(i, this._m4);
                }
                mesh.count = count;
                mesh.instanceMatrix.needsUpdate = true;
            }
        }
    }

    // ── teardown ─────────────────────────────────────────────────────────────

    /** Drop a tile's foliage data when the tile itself is evicted. */
    forgetTile(gx, gy) { delete this._tiles[gx + ',' + gy]; }

    clearAll() {
        this._tiles = {};
        this._batches.clear();
        for (const path in this._models) {
            const rec = this._models[path];
            if (!rec.parts) continue;
            for (const part of rec.parts) {
                this.group.remove(part.mesh);
                part.mesh.dispose();
                part.geometry.dispose();
                if (part.material.map) part.material.map.dispose();
                forgetWorldLighting(part.material);
                part.material.dispose();
            }
        }
        this._models = {};
        this._modelQueue = [];
        this._modelsInFlight = 0;
        this.instanceCount = 0;
        this._hasScattered = false;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// helpers
// ─────────────────────────────────────────────────────────────────────────────

function pickWeighted(doodads, rng) {
    let total = 0;
    for (const d of doodads) total += d.weight;
    if (total <= 0) return doodads[0];
    let pick = Math.floor(rng() * total);
    for (const d of doodads) {
        if (pick < d.weight) return d;
        pick -= d.weight;
    }
    return doodads[doodads.length - 1];
}

function b64ToBytes(b64) {
    const bin = atob(b64);
    const out = new Uint8Array(bin.length);
    for (let i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i);
    return out;
}
