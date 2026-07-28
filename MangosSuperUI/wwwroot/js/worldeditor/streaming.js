// streaming.js — terrain + object streaming + per-model InstancedMesh pools.
//
// Sections:
//   1. Texture / model builders   — turn JSON responses into GPU resources
//   2. InstancePool                — per-model InstancedMesh manager
//   3. ObjectStream                — fetch queue + nearby-object pump
//   4. TileGrid                    — progressive ADT terrain + water

import * as THREE from 'three';
import {
    buildSplatResources, makeSplatMaterial, syncSplatUniforms,
    disposeSplatResources, isSplatEnabled
} from './terrain-splat.js';
import { getJSON } from './net.js';
import { partitionByType, waterTypeName } from './water.js';
import { tagEntity } from './core.js';
import {
    makeTerrainMaterial,
    makeDoodadMaterial,
    makeWmoMaterial,
    maxAnisotropy
} from './render.js';

// Phase 8: BVH for sub-millisecond terrain raycasting (sculpt brush)
import {
    computeBoundsTree,
    disposeBoundsTree,
    acceleratedRaycast
} from 'three-mesh-bvh';

// Phase 8: monkey-patch BVH onto Three.js prototypes
THREE.BufferGeometry.prototype.computeBoundsTree = computeBoundsTree;
THREE.BufferGeometry.prototype.disposeBoundsTree = disposeBoundsTree;
THREE.Mesh.prototype.raycast = acceleratedRaycast;

// ─────────────────────────────────────────────────────────────────────────────
// 1. Texture / model builders
// ─────────────────────────────────────────────────────────────────────────────

// ─────────────────────────────────────────────────────────────────────────────
// Authored terrain normals (MCNR)
// ─────────────────────────────────────────────────────────────────────────────
//
// computeVertexNormals() averages the faces around each vertex, which is right
// in the middle of a tile and wrong at its edge: the edge vertices have no
// neighbours across the seam, so their normals lean inward and every ADT
// boundary picks up a lighting crease that follows the tile grid. Holes do the
// same thing on a smaller scale, because the missing triangles pull the
// surrounding normals sideways.
//
// MCNR is the authored per-vertex answer, so there is nothing to average and
// neighbouring tiles agree by construction. The server converts it to scene axes
// and sends it as signed bytes (WorldEditorController.BuildTileNormalsBase64).
//
// It stays a TOGGLE rather than a replacement: if the axis conversion is wrong
// the whole world lights as though it were on its side, and turning this off has
// to restore the previous behaviour without a reload. So both sets are kept on
// the geometry and swapping is an array copy.

let authoredNormalsOn = true;
export function setAuthoredNormals(v) { authoredNormalsOn = !!v; }
export function isAuthoredNormals() { return authoredNormalsOn; }

function b64ToUint8(b64) {
    const bin = atob(b64);
    const out = new Uint8Array(bin.length);
    for (let i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i);
    return out;
}

function b64ToInt8(b64) {
    const bin = atob(b64);
    const out = new Int8Array(bin.length);
    for (let i = 0; i < bin.length; i++) out[i] = (bin.charCodeAt(i) << 24) >> 24;
    return out;
}

/** Copy whichever normal set is currently selected into the live attribute. */
export function applyNormalSource(geo) {
    if (!geo || !geo.userData) return;
    const src = (authoredNormalsOn && geo.userData.authoredNormals)
        ? geo.userData.authoredNormals
        : geo.userData.computedNormals;
    const attr = geo.attributes && geo.attributes.normal;
    if (!src || !attr || attr.array.length !== src.length) return;
    attr.array.set(src);
    attr.needsUpdate = true;
}

function makeTextureFromDataURI(dataURI, flipY = true) {
    const tex = new THREE.TextureLoader().load(dataURI);
    // flipY: WMO (MOTV) UVs render correct at true; M2 DOODAD geometry follows the
    // glTF/GLB convention (V=0 at TOP) and needs false — the same fix grass got in
    // foliage.js. With true, every doodad texture is V-flipped: invisible on solid
    // 3D shapes (bark/canopy tile), but flat/billboard doodads (bushes, saplings)
    // render literally upside down. So doodads pass false; WMOs keep the default.
    tex.flipY = flipY;
    tex.wrapS = THREE.RepeatWrapping;
    tex.wrapT = THREE.RepeatWrapping;
    tex.colorSpace = THREE.SRGBColorSpace;
    tex.anisotropy = maxAnisotropy();
    return tex;
}

export function buildModelParts(data) {
    const posAttr = new THREE.Float32BufferAttribute(data.positions, 3);
    const normAttr = new THREE.Float32BufferAttribute(data.normals, 3);
    const uvAttr = new THREE.Float32BufferAttribute(data.uvs, 2);
    const allIndices = data.indices;
    const subs = data.submeshes || [{ indexStart: 0, indexCount: allIndices.length, textureBase64: null }];

    const parts = [];
    for (let si = 0; si < subs.length; si++) {
        const sub = subs[si];
        if (!sub.indexCount) continue;

        const subIndices = allIndices.slice(sub.indexStart, sub.indexStart + sub.indexCount);
        const geo = new THREE.BufferGeometry();
        geo.setAttribute('position', posAttr);
        geo.setAttribute('normal', normAttr);
        geo.setAttribute('uv', uvAttr);
        geo.setIndex(new THREE.BufferAttribute(new Uint32Array(subIndices), 1));

        let material;
        if (sub.textureBase64) {
            // M2 blend mode decides how the submesh composites:
            //   0 opaque · 1 alpha-key (cutout) · 2 alpha · 3 no-alpha-add ·
            //   4 add · 5 mod · 6 mod2x
            // Additive (3/4) is the FIRE/GLOW path (the flame adds light); unlit
            // (candle flames, lantern glows) renders full-bright. Opaque/alpha-key
            // keep the cutout that already worked, so ordinary props are unchanged.
            const blend = sub.blendMode || 0;
            const additive = (blend === 3 || blend === 4);
            const softAlpha = (blend === 2 || blend === 5 || blend === 6);
            const o = {
                map: makeTextureFromDataURI(sub.textureBase64, false),  // M2 UVs: V=0 at top
                side: THREE.DoubleSide,
                unlit: !!sub.unlit,
            };
            if (additive) {
                o.transparent = true;
                o.blending = THREE.AdditiveBlending;   // glow adds to the scene
                o.depthWrite = false;                  // and never occludes
                o.alphaTest = 0;
                if (o.unlit) o.color = 0xffffff;        // full-bright flame
            } else if (softAlpha) {
                o.transparent = true;
                o.depthWrite = !sub.noZWrite;
                o.alphaTest = 0;
                if (o.unlit) o.color = 0xffffff;
            } else {
                // opaque (0) / alpha-key (1) — the existing cutout, unchanged.
                o.transparent = true;
                o.alphaTest = 0.5;
                o.depthWrite = !sub.noZWrite;
                if (o.unlit) o.color = 0xffffff;
            }
            material = makeDoodadMaterial(o);
        } else {
            material = makeDoodadMaterial({ color: 0x808080, side: THREE.DoubleSide });
        }
        parts.push({ geometry: geo, material: material });
    }
    return parts;
}

export function buildWmoParts(data, opts) {
    const forceDoubleSide = opts && opts.forceDoubleSide;
    const posAttr = new THREE.Float32BufferAttribute(data.positions, 3);
    const normAttr = new THREE.Float32BufferAttribute(data.normals, 3);
    const uvAttr = new THREE.Float32BufferAttribute(data.uvs, 2);

    // MOCV baked interior light, one normalized RGBA per vertex. Shared across
    // submeshes like the position buffer. When present, submeshes light through
    // the vanilla baked model (interior glow); when absent (old cache), the plain
    // world daylight model — so it degrades gracefully.
    let mocvAttr = null;
    if (data.colorsBase64) {
        const bin = atob(data.colorsBase64);
        const u8 = new Uint8Array(bin.length);
        for (let i = 0; i < bin.length; i++) u8[i] = bin.charCodeAt(i);
        if (u8.length === (data.positions.length / 3) * 4) {
            mocvAttr = new THREE.BufferAttribute(u8, 4, true);   // normalized -> 0..1 in shader
        }
    }

    const allIndices = data.indices;
    const subs = data.submeshes || [];

    const parts = [];
    for (let si = 0; si < subs.length; si++) {
        const sub = subs[si];
        if (!sub.indexCount) continue;

        const subIndices = allIndices.slice(sub.indexStart, sub.indexStart + sub.indexCount);
        const geo = new THREE.BufferGeometry();
        geo.setAttribute('position', posAttr);
        geo.setAttribute('normal', normAttr);
        geo.setAttribute('uv', uvAttr);
        if (mocvAttr) geo.setAttribute('mocv', mocvAttr);
        geo.setIndex(new THREE.BufferAttribute(new Uint32Array(subIndices), 1));

        // Instance interiors (AQ, Naxx) are viewed from inside — FrontSide
        // culling hides walls the camera looks at from the interior. Force
        // DoubleSide for all WMO materials when forceDoubleSide is set.
        const sideMode = forceDoubleSide ? THREE.DoubleSide
            : (sub.doubleSided ? THREE.DoubleSide : THREE.FrontSide);

        // batchType (1/2/3) only when MOCV is present, so the baked interior
        // model runs; without it makeWmoMaterial keeps the plain daylight model.
        const batchType = mocvAttr ? sub.batchType : undefined;

        let material;
        if (sub.textureBase64) {
            material = makeWmoMaterial({
                map: makeTextureFromDataURI(sub.textureBase64),
                side: sideMode,
                alphaTest: sub.transparent ? 0.5 : 0,
                transparent: !!sub.transparent,
                batchType
            });
        } else {
            material = makeWmoMaterial({
                color: 0xaaaaaa,
                side: sideMode,
                batchType
            });
        }
        parts.push({ geometry: geo, material: material });
    }
    return parts;
}

export function buildTerrainGeometry(tile) {
    const geo = new THREE.BufferGeometry();
    geo.setAttribute('position', new THREE.Float32BufferAttribute(tile.positions, 3));

    const uvs = new Float32Array(tile.positions.length / 3 * 2);
    for (let i = 0; i < tile.positions.length / 3; i++) {
        uvs[i * 2] = (i % tile.vertsWidth) / (tile.vertsWidth - 1);
        uvs[i * 2 + 1] = 1.0 - Math.floor(i / tile.vertsWidth) / (tile.vertsHeight - 1);
    }
    geo.setAttribute('uv', new THREE.Float32BufferAttribute(uvs, 2));

    // Build indices, skipping terrain holes.
    //
    // tile.holes is a flat int[256] from the server — 16×16 MCNK chunks, each
    // carrying the low-res hole bitmask from the ADT's MCNK header offset 0x3C.
    // A set bit means "this 2×2 cell block is a hole — don't render terrain here."
    //
    // Bit layout uses interleaved row/col masks (same as VMaNGOS GridMap::IsHole):
    //   HoletabH = [0x1111, 0x2222, 0x4444, 0x8888]  (column masks)
    //   HoletabV = [0x000F, 0x00F0, 0x0F00, 0xF000]  (row masks)
    //   isHole = (holeMask & HoletabH[holeCol] & HoletabV[holeRow]) !== 0
    const holes = tile.holes;
    const HOLE_TAB_H = [0x1111, 0x2222, 0x4444, 0x8888];
    const HOLE_TAB_V = [0x000F, 0x00F0, 0x0F00, 0xF000];
    const w = tile.vertsWidth;
    const h = tile.vertsHeight;
    const idxList = [];

    for (let y = 0; y < h - 1; y++) {
        for (let x = 0; x < w - 1; x++) {
            if (holes) {
                const chunkX = (x >> 3);
                const chunkY = (y >> 3);
                if (chunkX < 16 && chunkY < 16) {
                    const holeMask = holes[chunkY * 16 + chunkX];
                    if (holeMask) {
                        const cellInChunkX = x - (chunkX << 3);
                        const cellInChunkY = y - (chunkY << 3);
                        const holeCol = cellInChunkX >> 1;
                        const holeRow = cellInChunkY >> 1;
                        if (holeMask & HOLE_TAB_H[holeCol] & HOLE_TAB_V[holeRow]) {
                            continue;
                        }
                    }
                }
            }

            const tl = y * w + x;
            const tr = tl + 1;
            const bl = (y + 1) * w + x;
            const br = bl + 1;
            idxList.push(tl, bl, tr, tr, bl, br);
        }
    }

    geo.setIndex(new THREE.BufferAttribute(new Uint32Array(idxList), 1));

    // Compute first so the fallback set always exists, then overlay the
    // authored one. Both are kept so the toggle is instant.
    geo.computeVertexNormals();
    geo.userData.computedNormals = Float32Array.from(geo.attributes.normal.array);

    if (tile.normalsBase64) {
        const raw = b64ToInt8(tile.normalsBase64);
        if (raw.length === geo.attributes.normal.array.length) {
            const authored = new Float32Array(raw.length);
            for (let i = 0; i < raw.length; i++) authored[i] = raw[i] / 127;
            geo.userData.authoredNormals = authored;
        } else {
            console.warn('[terrain] MCNR normal count', raw.length, '!=', 
                geo.attributes.normal.array.length, '- keeping computed normals');
        }
    }
    applyNormalSource(geo);
    return geo;
}

// ─────────────────────────────────────────────────────────────────────────────
// 2. InstancePool — per-model InstancedMesh manager
// ─────────────────────────────────────────────────────────────────────────────
//
// Each loaded model has one InstanceSet:
//   { meshes: InstancedMesh[],  // one per submesh part
//     idToIndex, indexToId,     // bidirectional placement-id ↔ instance-index map
//     count, capacity, isWmo, parentGroup }
//
// Add: insert at next free index, set matrix, bump count.
// Remove: swap-with-last for O(1), shrink to 0 → dispose entire set.
// Grow: when count would exceed capacity, double capacity (rebuild meshes,
//       copy old matrices, swap children).

const INITIAL_CAPACITY = 512;

export class InstancePool {
    constructor() {
        // modelPath → { parts: [{geometry, material}] }
        this.modelRegistry = {};
        // modelPath → InstanceSet
        this.sets = {};

        // Parent groups with independent visibility toggles.
        this.doodadGroup = new THREE.Group();
        this.doodadGroup.name = 'allDoodads';
        this.doodadGroup.position.y = 0; // M1.1: 1:1 world height

        this.wmoGroup = new THREE.Group();
        this.wmoGroup.name = 'allWmos';
        this.wmoGroup.position.y = 0; // M1.1: 1:1 world height
    }

    attachTo(scene) {
        scene.add(this.doodadGroup);
        scene.add(this.wmoGroup);
    }

    registerModel(modelPath, parts) { this.modelRegistry[modelPath] = { parts }; }
    isModelLoaded(modelPath) { return !!this.modelRegistry[modelPath]; }

    setDoodadsVisible(v) { this.doodadGroup.visible = !!v; }
    setWmosVisible(v) { this.wmoGroup.visible = !!v; }

    buildPlacementMatrix(placement) {
        const matrix = new THREE.Matrix4();
        const pos = new THREE.Vector3(placement.x, placement.y, placement.z);
        const scl = new THREE.Vector3(1, 1, 1);

        if (placement.kind === 'w') {
            // WMO: MODF has no scale field — always 1.
            scl.set(1, 1, 1);
            const rot = new THREE.Euler(0, 0, 0, 'YXZ');
            if (placement.rotY) rot.y = (placement.rotY || 0) * Math.PI / 180;
            matrix.compose(pos, new THREE.Quaternion().setFromEuler(rot), scl);
            return matrix;
        }

        if (placement.kind === 'wd') {
            // WMO-embedded doodad: full quaternion, already in Y-up world space.
            // Server pre-composed (WMO_world_rot) · (MODD_local_rot) and did
            // the Z-up→Y-up basis change on both. We just pose it.
            const s = placement.scale || 1.0;
            scl.set(s, s, s);
            const quat = new THREE.Quaternion(
                placement.qx || 0,
                placement.qy || 0,
                placement.qz || 0,
                placement.qw == null ? 1 : placement.qw
            );
            matrix.compose(pos, quat, scl);
            return matrix;
        }

        // Default: ADT MDDF M2 doodad — Euler rotY in degrees with the
        // historical -90° offset to align model-forward with WoW's convention.
        const s = placement.scale || 1.0;
        scl.set(s, s, s);
        const rotM = new THREE.Euler(0, 0, 0, 'YXZ');
        rotM.y = ((placement.rotY || 0) - 90) * Math.PI / 180;
        matrix.compose(pos, new THREE.Quaternion().setFromEuler(rotM), scl);
        return matrix;
    }

    _getOrCreate(modelPath, isWmo) {
        if (this.sets[modelPath]) return this.sets[modelPath];

        const reg = this.modelRegistry[modelPath];
        if (!reg) return null;

        const parent = isWmo ? this.wmoGroup : this.doodadGroup;
        const meshes = [];
        for (let pi = 0; pi < reg.parts.length; pi++) {
            const part = reg.parts[pi];
            const im = new THREE.InstancedMesh(part.geometry, part.material, INITIAL_CAPACITY);
            im.count = 0;
            // PERF: frustumCulled = true — Three.js will skip drawing this
            // entire InstancedMesh when its bounding sphere is outside the
            // camera frustum. We recompute the bounding sphere in
            // flushBounds() after all instances for a pump cycle are placed.
            im.frustumCulled = true;
            tagEntity(im, {
                type: isWmo ? 'wmo' : 'm2',
                id: 'instanced:' + modelPath,
                selectable: false,
                transformable: false,
                persistable: false,
                source: 'vanilla'
            });
            parent.add(im);
            meshes.push(im);
        }
        const set = {
            meshes, idToIndex: {}, indexToId: {},
            count: 0, capacity: INITIAL_CAPACITY,
            isWmo, parentGroup: parent,
            boundsDirty: false
        };
        this.sets[modelPath] = set;
        return set;
    }

    addInstance(modelPath, placementId, placement) {
        const isWmo = placement.kind === 'w';
        const set = this._getOrCreate(modelPath, isWmo);
        if (!set) return;

        if (set.count >= set.capacity) this._grow(modelPath, set);

        const idx = set.count;
        set.idToIndex[placementId] = idx;
        set.indexToId[idx] = placementId;
        set.count++;

        const matrix = this.buildPlacementMatrix(placement);
        for (let mi = 0; mi < set.meshes.length; mi++) {
            set.meshes[mi].setMatrixAt(idx, matrix);
            set.meshes[mi].count = set.count;
            set.meshes[mi].instanceMatrix.needsUpdate = true;
        }
        set.boundsDirty = true;
    }

    removeInstance(modelPath, placementId) {
        const set = this.sets[modelPath];
        if (!set) return;
        const idx = set.idToIndex[placementId];
        if (idx === undefined) return;

        const lastIdx = set.count - 1;
        if (idx !== lastIdx) {
            const lastId = set.indexToId[lastIdx];
            const tmp = new THREE.Matrix4();
            for (let mi = 0; mi < set.meshes.length; mi++) {
                set.meshes[mi].getMatrixAt(lastIdx, tmp);
                set.meshes[mi].setMatrixAt(idx, tmp);
                set.meshes[mi].instanceMatrix.needsUpdate = true;
            }
            set.idToIndex[lastId] = idx;
            set.indexToId[idx] = lastId;
        }
        delete set.idToIndex[placementId];
        delete set.indexToId[lastIdx];
        set.count--;
        for (let mi = 0; mi < set.meshes.length; mi++) set.meshes[mi].count = set.count;
        if (set.count === 0) this.disposeSet(modelPath);
        else set.boundsDirty = true;
    }

    /** Recompute bounding spheres for all InstancedMeshes that changed.
     *  Call once per pump cycle (after addInstance/removeInstance batch). */
    flushBounds() {
        for (const mp in this.sets) {
            const set = this.sets[mp];
            if (!set.boundsDirty) continue;
            set.boundsDirty = false;
            for (let mi = 0; mi < set.meshes.length; mi++) {
                set.meshes[mi].computeBoundingSphere();
            }
        }
    }

    _grow(modelPath, set) {
        const newCap = set.capacity * 2;
        const reg = this.modelRegistry[modelPath];
        if (!reg) return;

        const newMeshes = [];
        const tmp = new THREE.Matrix4();
        for (let pi = 0; pi < reg.parts.length; pi++) {
            const part = reg.parts[pi];
            const newIm = new THREE.InstancedMesh(part.geometry, part.material, newCap);
            newIm.count = set.count;
            newIm.frustumCulled = true;

            const oldIm = set.meshes[pi];
            for (let i = 0; i < set.count; i++) {
                oldIm.getMatrixAt(i, tmp);
                newIm.setMatrixAt(i, tmp);
            }
            newIm.instanceMatrix.needsUpdate = true;

            set.parentGroup.remove(oldIm);
            oldIm.dispose();
            set.parentGroup.add(newIm);
            newMeshes.push(newIm);
        }
        set.meshes = newMeshes;
        set.capacity = newCap;
        set.boundsDirty = true;
    }

    disposeSet(modelPath) {
        const set = this.sets[modelPath];
        if (!set) return;
        for (let mi = 0; mi < set.meshes.length; mi++) {
            set.parentGroup.remove(set.meshes[mi]);
            set.meshes[mi].dispose();
        }
        delete this.sets[modelPath];
    }

    disposeAll() {
        for (const mp of Object.keys(this.sets)) this.disposeSet(mp);
        for (const mp of Object.keys(this.modelRegistry)) {
            const reg = this.modelRegistry[mp];
            if (reg && reg.parts) {
                for (const p of reg.parts) {
                    if (p.geometry) p.geometry.dispose();
                    if (p.material) {
                        if (p.material.map) p.material.map.dispose();
                        p.material.dispose();
                    }
                }
            }
        }
        this.modelRegistry = {};
    }

    wmoMeshList() {
        const out = [];
        this.wmoGroup.traverse((c) => {
            if (c.isInstancedMesh || c.isMesh) out.push(c);
        });
        return out;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 3. ObjectStream — nearby-object streaming pump
// ─────────────────────────────────────────────────────────────────────────────
//
// Responsibilities:
//  - Fetch queue: limit concurrent /WorldEditor/{Wmo,Doodad}Model fetches
//  - On 600ms tick: ask server for nearby objects, unload distant ones
//  - Maintain InstancePool: dedup against placement-store's customPlacementKeys
//  - Provide WMO mesh list for walk-mode collision

const MAX_CONCURRENT_FETCHES = 4;

// ── Residency caps (why Stormwind used to crash the tab) ─────────────────────
// Object eviction was radius-only and had NO count cap: a dense city returns
// thousands of placements plus hundreds of UNIQUE models, each decoding its own
// GPU textures, all instantiated in one frame. That is the OOM / stall. These
// bound the working set regardless of how much the server hands back.
const MAX_RESIDENT_PLACEMENTS = 1400;  // hard ceiling; farthest evicted over it
const ADD_BUDGET_PER_PUMP = 140;       // new instances created per pump (nearest first)
const MAX_LOAD_RADIUS = 320;           // yd; clamps the working set at the source

export class ObjectStream {
    constructor(editor) {
        this.editor = editor;
        this.pool = new InstancePool();

        this.activePlacements = {};

        this._fetchQueue = [];
        this._fetchInFlight = 0;
        this._fetching = {};
        this._failedModels = {};     // negative cache for 404'd models
        this._streamingInFlight = false;

        this.LOAD_RADIUS = 250;
        this.UNLOAD_RADIUS = 350;
        this._addBudget = 0;   // per-pump instantiation budget (set in pump)

        this._diagNextNearby = true;

        // Generation counter — incremented on clearAll(). Pump responses
        // that arrive after a clearAll are stale and must be discarded.
        this._generation = 0;
    }

    attachTo(scene) { this.pool.attachTo(scene); }
    wmoMeshList() { return this.pool.wmoMeshList(); }
    setLoadRadii(load, unload) { this.LOAD_RADIUS = load; this.UNLOAD_RADIUS = unload; }
    setDoodadsVisible(v) { this.pool.setDoodadsVisible(v); }
    setWmosVisible(v) { this.pool.setWmosVisible(v); }

    _enqueueFetch(modelPath, priority) {
        if (this._fetching[modelPath]) return;
        if (this._failedModels[modelPath]) return;
        if (this.pool.isModelLoaded(modelPath)) return;
        this._fetching[modelPath] = true;
        this._fetchQueue.push({ path: modelPath, priority: priority || 0 });
        this._drain();
    }

    _drain() {
        while (this._fetchInFlight < MAX_CONCURRENT_FETCHES && this._fetchQueue.length > 0) {
            this._fetchQueue.sort((a, b) => a.priority - b.priority);
            const item = this._fetchQueue.shift();
            this._fetchInFlight++;

            const modelPath = item.path;
            const isWmo = modelPath.toLowerCase().indexOf('.wmo') !== -1;
            const url = isWmo ? '/WorldEditor/WmoModel' : '/WorldEditor/DoodadModel';
            const gen = this._generation;

            getJSON(url + '?path=' + encodeURIComponent(modelPath))
                .then((mdata) => {
                    this._fetchInFlight--;
                    if (gen !== this._generation) { delete this._fetching[modelPath]; this._drain(); return; }
                    if (mdata.success && mdata.positions && mdata.positions.length > 0) {
                        // Force DoubleSide for WMOs in instance/raid maps (mapId > 1)
                        // so interior faces are visible when the camera is inside.
                        const instanceMap = this.editor.tileGrid && this.editor.tileGrid.mapId > 1;
                        const parts = isWmo
                            ? buildWmoParts(mdata, { forceDoubleSide: instanceMap })
                            : buildModelParts(mdata);
                        this.pool.registerModel(modelPath, parts);
                        this._instantiatePending(modelPath);
                        // PERF: recompute bounding spheres after new instances placed
                        this.pool.flushBounds();
                    } else {
                        this._failedModels[modelPath] = true;
                    }
                    delete this._fetching[modelPath];
                    this._drain();
                })
                .catch(() => {
                    this._fetchInFlight--;
                    this._failedModels[modelPath] = true;
                    delete this._fetching[modelPath];
                    this._drain();
                });
        }
    }

    _instantiatePending(modelPath) {
        for (const id in this.activePlacements) {
            const p = this.activePlacements[id];
            if (p.model === modelPath && !p.instanced) {
                this.pool.addInstance(modelPath, id, p);
                p.instanced = true;
            }
        }
    }

    pump(camX, camZ, globalMidHeight, globalHeightScale) {
        // DUNGEON: skip streaming entirely for dungeon maps
        if (this.editor.tileGrid && this.editor.tileGrid.isDungeon) return;

        // Eviction runs FIRST, before the in-flight guard — otherwise a slow
        // NearbyObjects fetch (exactly what happens under city load) starves the
        // unload and the resident set only ever grows. Distance eviction + the
        // hard count cap keep memory bounded even mid-fetch.
        this._evictDistant(camX, camZ);
        this._enforceResidentCap(camX, camZ);

        if (!this.editor.currentPreset || this._streamingInFlight) return;

        // Server fetch
        this._streamingInFlight = true;
        const gen = this._generation;
        let url = '/WorldEditor/NearbyObjects' +
            '?preset=' + encodeURIComponent(this.editor.currentPreset) +
            '&camX=' + camX.toFixed(1) +
            '&camZ=' + camZ.toFixed(1) +
            '&loadRadius=' + this.LOAD_RADIUS.toFixed(0) +
            '&globalMidHeight=' + globalMidHeight +
            '&globalHeightScale=' + globalHeightScale;

        if (this._diagNextNearby) {
            url += '&wmoDoodadDiag=true';
            this._diagNextNearby = false;
            console.log('[ObjectStream] firing WMO doodad diagnostic on this NearbyObjects request');
        }

        getJSON(url)
            .then((resp) => {
                this._streamingInFlight = false;
                if (gen !== this._generation) return;
                if (!resp.success) return;
                const adds = resp.add || {};
                // Nearest-first, budgeted: never instantiate a whole city in one
                // frame. Doodads and WMOs share one budget; the leftovers are
                // picked up on later pumps as nearer ones evict.
                this._addBudget = ADD_BUDGET_PER_PUMP;
                this._addWmos((adds.wmos || []), camX, camZ);       // buildings first
                this._addDoodads(adds.doodads || [], camX, camZ);
                // A large response can still overshoot the cap; trim the farthest.
                this._enforceResidentCap(camX, camZ);
                // PERF: recompute bounding spheres after this batch
                this.pool.flushBounds();
            })
            .catch(() => { this._streamingInFlight = false; });
    }

    /** Remove placements past UNLOAD_RADIUS (was inline in pump). */
    _evictDistant(camX, camZ) {
        const toRemove = [];
        const r2 = this.UNLOAD_RADIUS * this.UNLOAD_RADIUS;
        for (const id in this.activePlacements) {
            const p = this.activePlacements[id];
            const dx = p.x - camX, dz = p.z - camZ;
            if (dx * dx + dz * dz > r2) toRemove.push(id);
        }
        for (const id of toRemove) {
            const p = this.activePlacements[id];
            if (p.instanced) this.pool.removeInstance(p.model, id);
            delete this.activePlacements[id];
        }
        if (toRemove.length > 0) this.pool.flushBounds();
    }

    /**
     * Hard ceiling on resident placements. Over the cap, evict the FARTHEST —
     * the single change that stops a dense city from exhausting GPU memory,
     * because unique-model textures scale with the resident set. Radius alone
     * never bounded the count.
     */
    _enforceResidentCap(camX, camZ) {
        const ids = Object.keys(this.activePlacements);
        if (ids.length <= MAX_RESIDENT_PLACEMENTS) return;
        ids.sort((a, b) => {
            const pa = this.activePlacements[a], pb = this.activePlacements[b];
            const da = (pa.x - camX) * (pa.x - camX) + (pa.z - camZ) * (pa.z - camZ);
            const db = (pb.x - camX) * (pb.x - camX) + (pb.z - camZ) * (pb.z - camZ);
            return db - da;   // farthest first
        });
        const cut = ids.length - MAX_RESIDENT_PLACEMENTS;
        let flushed = false;
        for (let i = 0; i < cut; i++) {
            const id = ids[i];
            const p = this.activePlacements[id];
            if (p.instanced) { this.pool.removeInstance(p.model, id); flushed = true; }
            delete this.activePlacements[id];
        }
        if (flushed) this.pool.flushBounds();
    }

    _addDoodads(arr, camX, camZ) {
        arr = this._nearestFirst(arr, camX, camZ);
        for (const d of arr) {
            if (this._addBudget <= 0) break;              // budget: rest retry next pump
            if (this.activePlacements[d.id]) continue;

            // Two flavors of doodad share this array:
            //   kind 'd'  = ADT MDDF placement, oriented by Euler rotY
            //   kind 'wd' = WMO-embedded MODD, oriented by a full quaternion
            //               already in Y-up world space (server pre-composes
            //               WMO_world_rot · MODD_local_rot for us)
            const placementKind = d.kind || 'd';
            const rec = {
                model: d.model, x: d.x, y: d.y, z: d.z,
                scale: d.scale, type: d.type,
                kind: placementKind, instanced: false
            };
            if (placementKind === 'wd') {
                rec.qx = d.qx; rec.qy = d.qy; rec.qz = d.qz; rec.qw = d.qw;
            } else {
                rec.rotY = d.rotY;
            }
            this.activePlacements[d.id] = rec;
            this._addBudget--;

            if (this.pool.isModelLoaded(d.model)) {
                this.pool.addInstance(d.model, d.id, this.activePlacements[d.id]);
                this.activePlacements[d.id].instanced = true;
            } else {
                const dx = d.x - camX, dz = d.z - camZ;
                this._enqueueFetch(d.model, dx * dx + dz * dz);
            }
        }
    }

    /** Sort a placement array nearest-first to the camera (budget spends on the near ones). */
    _nearestFirst(arr, camX, camZ) {
        if (!arr || arr.length < 2) return arr || [];
        return arr.slice().sort((a, b) => {
            const da = (a.x - camX) * (a.x - camX) + (a.z - camZ) * (a.z - camZ);
            const db = (b.x - camX) * (b.x - camX) + (b.z - camZ) * (b.z - camZ);
            return da - db;
        });
    }

    _addWmos(arr, camX, camZ) {
        arr = this._nearestFirst(arr, camX, camZ);
        const customKeys = this.editor.placementStore
            ? this.editor.placementStore.customPlacementKeys
            : {};
        for (const w of arr) {
            if (this._addBudget <= 0) break;
            if (this.activePlacements[w.id]) continue;
            // Skip if a custom placement is at this position (avoids
            // double-rendering placed WMOs that also exist in the ADT).
            const sp = Math.round(w.x) + '|' + Math.round(w.z);
            if (customKeys[sp]) continue;

            this.activePlacements[w.id] = {
                model: w.model, x: w.x, y: w.y, z: w.z,
                rotX: w.rotX, rotY: w.rotY, rotZ: w.rotZ,
                kind: 'w', instanced: false
            };
            this._addBudget--;
            if (this.pool.isModelLoaded(w.model)) {
                this.pool.addInstance(w.model, w.id, this.activePlacements[w.id]);
                this.activePlacements[w.id].instanced = true;
            } else {
                const dx = w.x - camX, dz = w.z - camZ;
                this._enqueueFetch(w.model, dx * dx + dz * dz);
            }
        }
    }

    purgeStreamedNear(x, z, r) {
        const r2 = r * r;
        const purge = [];
        for (const sid in this.activePlacements) {
            const sp = this.activePlacements[sid];
            if (sp.kind !== 'w') continue;
            const dx = sp.x - x, dz = sp.z - z;
            if (dx * dx + dz * dz < r2) purge.push(sid);
        }
        for (const pid of purge) {
            const pp = this.activePlacements[pid];
            if (pp.instanced) this.pool.removeInstance(pp.model, pid);
            delete this.activePlacements[pid];
        }
    }

    clearAll() {
        this._generation++;
        this.pool.disposeAll();
        this.activePlacements = {};
        this._fetchQueue = [];
        this._fetchInFlight = 0;
        this._fetching = {};
        this._failedModels = {};
        this._streamingInFlight = false;
        this._diagNextNearby = true;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 4. TileGrid — progressive ADT terrain + water
// ─────────────────────────────────────────────────────────────────────────────
//
// Owns the `tiles[]` map, initial 3x3 heightmap load, single-tile loads on
// camera movement, water mesh per tile, and unloading distant tiles.
// Exposes `terrainMeshes()` for the placement raycaster + walk-mode snap.

export class TileGrid {
    constructor(editor) {
        this.editor = editor;
        this.tiles = {};
        this.loading = {};
        this.tileWidthMesh = 0;
        // M1.1: the height transform is the identity. These are kept so the
        // coord readout and the sculpt round-trip still read as formulas.
        this.globalMidHeight = 0;
        this.globalHeightScale = 1.0;
        // M1.1: true world-height range of the loaded block (yards).
        this.worldMinHeight = 0;
        this.worldMaxHeight = 0;
        // Live splat materials, so fog and light changes can be pushed to them.
        this._splatMaterials = new Set();
        this.centerGridX = 0;
        this.centerGridY = 0;
        this.mapId = 0;
        this.TILE_RADIUS = 1;
        this.UNLOAD_RADIUS = 3;
        this.textureRes = 128;
        this.fogNear = 180;
        this.fogFar = 550;

        // DUNGEON: dungeon-mode state
        this.isDungeon = false;
        this.dungeonGroup = null;
        this.dungeonMeshes = [];
        this.dungeonMapId = 0;
        this.dungeonInfo = null;
    }

    terrainMeshes() {
        if (this.isDungeon) return this.dungeonMeshes;
        const out = [];
        for (const k in this.tiles) {
            const t = this.tiles[k];
            if (t.mesh) out.push(t.mesh);
        }
        return out;
    }

    dungeonWmoMeshes() { return this.dungeonMeshes; }

    setTileRadius(r) {
        this.TILE_RADIUS = Math.max(1, r | 0);
        this.UNLOAD_RADIUS = this.TILE_RADIUS + 2;
    }

    setTextureRes(r) { this.textureRes = r; }

    /** Push the authored/computed normal choice into every loaded tile. */
    applyNormalSource() {
        for (const k in this.tiles) {
            const t = this.tiles[k];
            if (t && t.geo) applyNormalSource(t.geo);
        }
    }

    cameraToGrid(controlsTarget) {
        const dx = Math.round(controlsTarget.x / this.tileWidthMesh);
        const dy = Math.round(controlsTarget.z / this.tileWidthMesh);
        return { gridX: this.centerGridX + dy, gridY: this.centerGridY + dx };
    }

    /** Keep splat materials tracking the scene's fog and lighting. */
    syncSplat() {
        if (this._splatMaterials.size === 0) return;
        const vp = this.editor.viewport;
        syncSplatUniforms(this._splatMaterials, vp.lighting, vp.scene.fog);
    }

    updateFogForRadius(scene, camera, r) {
        const range = Math.max(0.3, r + 0.5) * (this.tileWidthMesh || 400);

        // Instance/raid maps (mapId > 1) are floating structures — push fog
        // way back and increase far clip so the full architecture is visible.
        // Outdoor zones keep tight fog for atmosphere + perf.
        const isInstance = this.mapId > 1;
        this.fogNear = isInstance ? range * 1.5 : range * 0.3;
        this.fogFar = isInstance ? range * 4.0 : range * 0.9;
        if (scene.fog) {
            scene.fog.near = this.fogNear;
            scene.fog.far = this.fogFar;
        }
        camera.far = isInstance ? range * 5.0 : range * 1.5;
        camera.updateProjectionMatrix();
    }

    /**
     * M1.1 — put the camera on the ground at the centre of the loaded block.
     *
     * Probes the terrain under (0,0) — the centre tile's origin — and hands the
     * measured height to the rig. Falls back to the block's height-range midpoint
     * if the probe misses (hole, water-only tile, geometry not ready).
     */
    frameCameraOnCentre(x, z) {
        const rig = this.editor.viewport && this.editor.viewport.rig;
        if (!rig || !rig.frameTerrain) return;

        const px = (x !== undefined) ? x : 0;
        const pz = (z !== undefined) ? z : 0;

        const meshes = this.terrainMeshes();
        const mid = (isFinite(this.worldMinHeight) && isFinite(this.worldMaxHeight))
            ? (this.worldMinHeight + this.worldMaxHeight) * 0.5
            : 0;

        let groundY = rig.probeGroundY(meshes, px, pz, this.worldMaxHeight);
        if (groundY === null || !isFinite(groundY)) groundY = mid;

        rig.frameTerrain(groundY, { x: px, z: pz });

        // M1.1: the backdrop ground plane was pinned at y=-5, which only read as
        // "below the world" when the scene was centred on zero. Drop it under the
        // lowest loaded terrain instead.
        const scene = this.editor.viewport && this.editor.viewport.scene;
        const ground = scene && scene.getObjectByName('ground');
        if (ground) {
            const floor = isFinite(this.worldMinHeight) ? this.worldMinHeight : groundY;
            ground.position.y = floor - 5;
        }

        return groundY;
    }

    objectRadiiForCurrent() {
        const base = this.tileWidthMesh || 400;
        // Clamped to MAX_LOAD_RADIUS: with a real 533-yd tile this used to reach
        // ~480 yd, so a whole city loaded at once. Capping the request shrinks
        // the working set at the source; the resident-count cap backs it up.
        const load = Math.min(MAX_LOAD_RADIUS, Math.max(150, (this.TILE_RADIUS + 0.5) * base * 0.6));
        const unload = load * 1.4;
        return { load, unload };
    }

    loadPreset(presetKey, statusCallback) {
        const editor = this.editor;
        editor.currentPreset = presetKey;

        this._clearDungeon();
        Object.keys(this.tiles).forEach((k) => this._unloadTile(k));
        this.tiles = {};
        this.loading = {};

        this.tileWidthMesh = 0;
        // M1.1: the height transform is the identity. These are kept so the
        // coord readout and the sculpt round-trip still read as formulas.
        this.globalMidHeight = 0;
        this.globalHeightScale = 1.0;
        this.worldMinHeight = 0;
        this.worldMaxHeight = 0;
        this.isDungeon = false;

        if (presetKey.startsWith('dungeon:')) {
            const mapId = parseInt(presetKey.substring(8));
            if (!isNaN(mapId)) return this._loadDungeon(mapId, statusCallback);
        }

        return getJSON('/WorldEditor/Heightmap?preset=' + encodeURIComponent(presetKey) + '&tileRadius=1')
            .then((hm) => {
                if (!hm.success) {
                    statusCallback && statusCallback('Heightmap failed: ' + hm.error);
                    return null;
                }
                this.tileWidthMesh = hm.tileWidthMesh;
                this.globalMidHeight = (hm.midHeight !== undefined) ? hm.midHeight : 0;
                this.globalHeightScale = (hm.heightScale > 0) ? hm.heightScale : 1;
                this.mapId = hm.mapId || 0;
                // M1.1: true world-height range of the loaded block, so the
                // camera and ground plane can be placed without guessing.
                this.worldMinHeight = (hm.minHeight !== undefined) ? hm.minHeight : 0;
                this.worldMaxHeight = (hm.maxHeight !== undefined) ? hm.maxHeight : 0;

                const center = hm.tiles.find((t) => t.dx === 0 && t.dy === 0);
                if (center) {
                    this.centerGridX = center.gridX;
                    this.centerGridY = center.gridY;
                }

                const radii = this.objectRadiiForCurrent();
                if (editor.objectStream) editor.objectStream.setLoadRadii(radii.load, radii.unload);

                const toLoad = [];
                hm.tiles.forEach((tile) => {
                    const key = this._key(tile.gridX, tile.gridY);
                    const entry = {
                        mesh: null, gridX: tile.gridX, gridY: tile.gridY,
                        dx: tile.dx, dy: tile.dy,
                        geo: buildTerrainGeometry(tile),
                        // Grid dimensions, so consumers that index the vertex
                        // grid (foliage height sampling) do not have to infer
                        // them from the attribute length.
                        vertsWidth: tile.vertsWidth,
                        vertsHeight: tile.vertsHeight,
                        loading: false
                    };
                    this.tiles[key] = entry;
                    toLoad.push(entry);
                });

                let texLoaded = 0;
                return new Promise((resolve) => {
                    toLoad.forEach((entry) => {
                        this._loadTexture(entry, () => {
                            texLoaded++;
                            statusCallback && statusCallback('Textures: ' + texLoaded + '/' + toLoad.length);
                            if (texLoaded >= toLoad.length) {
                                this.updateFogForRadius(editor.viewport.scene, editor.viewport.rig.camera, this.TILE_RADIUS);
                                statusCallback && statusCallback(hm.label || presetKey);
                                // Skip water for instance/raid maps — their ADTs contain
                                // overworld water (rivers, lakes) from the terrain below
                                // the floating instance, which renders as erratic blue
                                // planes cutting through the architecture.
                                if (this.mapId <= 1) {
                                    toLoad.forEach((e) => this._loadWater(e));
                                }
                                // M1.1: terrain is now at true world height, so a
                                // fixed camera Y is underground or in orbit depending
                                // on the zone. Frame against the real surface.
                                this.frameCameraOnCentre();
                                editor.signals.presetLoaded.dispatch(presetKey);
                                resolve(hm);
                            }
                        });
                    });
                });
            });
    }

    // ── DUNGEON loading pipeline ──────────────────────────────────────────

    _loadDungeon(mapId, statusCallback) {
        const editor = this.editor;
        this.isDungeon = true;
        this.dungeonMapId = mapId;

        statusCallback && statusCallback('Loading dungeon info...');

        return getJSON('/WorldEditor/DungeonInfo?mapId=' + mapId)
            .then((info) => {
                if (!info.success) {
                    statusCallback && statusCallback('Dungeon info failed: ' + info.error);
                    return null;
                }
                this.dungeonInfo = info;
                statusCallback && statusCallback('Loading ' + info.name + ' geometry...');
                return getJSON('/WorldEditor/WmoModel?path=' + encodeURIComponent(info.wmoPath));
            })
            .then((wmoData) => {
                if (!wmoData || !wmoData.success) {
                    statusCallback && statusCallback('Dungeon WMO load failed');
                    return null;
                }
                statusCallback && statusCallback('Building dungeon meshes...');
                const parts = buildWmoParts(wmoData);
                if (parts.length === 0) {
                    statusCallback && statusCallback('Dungeon WMO has no geometry');
                    return null;
                }
                const group = new THREE.Group();
                group.name = 'dungeonWmo';

                for (let i = 0; i < parts.length; i++) {
                    const mesh = new THREE.Mesh(parts[i].geometry, parts[i].material);
                    mesh.frustumCulled = false;
                    mesh.geometry.computeBoundsTree();
                    group.add(mesh);
                    this.dungeonMeshes.push(mesh);
                }

                if (this.dungeonInfo.modf) group.position.set(0, 0, 0);

                this.dungeonGroup = group;
                editor.viewport.scene.add(group);

                const bbox = new THREE.Box3();
                group.traverse((c) => {
                    if (c.isMesh && c.geometry) {
                        c.geometry.computeBoundingBox();
                        const meshBbox = c.geometry.boundingBox.clone();
                        meshBbox.applyMatrix4(c.matrixWorld);
                        bbox.union(meshBbox);
                    }
                });

                const center = new THREE.Vector3();
                const size = new THREE.Vector3();
                bbox.getCenter(center);
                bbox.getSize(size);
                const maxDim = Math.max(size.x, size.y, size.z);

                this.tileWidthMesh = maxDim;
                // M1.1: dungeon WMO geometry is already true world height —
                // folding the bbox centre in here double-counted it in the
                // .gps readout. Identity, same as the terrain path.
                this.globalMidHeight = 0;
                this.globalHeightScale = 1.0;
                this.worldMinHeight = bbox.min.y;
                this.worldMaxHeight = bbox.max.y;

                const scene = editor.viewport.scene;
                const camera = editor.viewport.rig.camera;
                scene.fog.near = maxDim * 0.4;
                scene.fog.far = maxDim * 1.2;
                camera.far = maxDim * 2.0;
                camera.near = 0.1;
                camera.updateProjectionMatrix();

                scene.background = new THREE.Color(0x111118);
                scene.fog.color = new THREE.Color(0x111118);

                const sky = scene.getObjectByName('sky');
                const ground = scene.getObjectByName('ground');
                if (sky) sky.visible = false;
                if (ground) ground.visible = false;

                const rig = editor.viewport.rig;
                camera.position.set(center.x, center.y + 2, center.z);
                rig.controls.target.set(center.x, center.y, center.z + 10);

                if (!rig.walk.mode) {
                    rig.enterWalkMode();
                    editor.signals.walkModeChanged.dispatch(true);
                }

                statusCallback && statusCallback(this.dungeonInfo.name);
                editor.signals.presetLoaded.dispatch(editor.currentPreset);

                return {
                    success: true, label: this.dungeonInfo.name, isDungeon: true,
                    tileWidthMesh: this.tileWidthMesh,
                    midHeight: this.globalMidHeight,
                    heightScale: this.globalHeightScale
                };
            })
            .catch((err) => {
                console.error('Dungeon load failed:', err);
                statusCallback && statusCallback('Dungeon load failed: ' + err.message);
                return null;
            });
    }

    _clearDungeon() {
        if (this.dungeonGroup) {
            const scene = this.editor.viewport.scene;
            scene.remove(this.dungeonGroup);

            this.dungeonGroup.traverse((c) => {
                if (c.isMesh) {
                    if (c.geometry) {
                        if (c.geometry.boundsTree) c.geometry.disposeBoundsTree();
                        c.geometry.dispose();
                    }
                    if (c.material) {
                        if (c.material.map) c.material.map.dispose();
                        c.material.dispose();
                    }
                }
            });

            this.dungeonGroup = null;
            this.dungeonMeshes = [];
            this.dungeonInfo = null;
            this.isDungeon = false;

            // MSUIClient DayFog blue (matches render.js FOG_COLOR). world-lighting
            // re-applies the authored fog each frame when it is on; this is the
            // fallback for the moment after leaving a dungeon.
            const FOG_COLOR = 0x8fb5d9;
            scene.background = new THREE.Color(FOG_COLOR);
            if (scene.fog) scene.fog.color = new THREE.Color(FOG_COLOR);

            const sky = scene.getObjectByName('sky');
            const ground = scene.getObjectByName('ground');
            if (sky) sky.visible = true;
            if (ground) ground.visible = true;
        }
    }

    // ── Progressive terrain loading ───────────────────────────────────────

    checkProgressive(controlsTarget) {
        if (this.isDungeon) return;
        if (!this.editor.currentPreset || this.tileWidthMesh === 0) return;
        const cam = this.cameraToGrid(controlsTarget);
        for (let dy = -this.TILE_RADIUS; dy <= this.TILE_RADIUS; dy++) {
            for (let dx = -this.TILE_RADIUS; dx <= this.TILE_RADIUS; dx++) {
                const gx = cam.gridX + dy;
                const gy = cam.gridY + dx;
                const key = this._key(gx, gy);
                if (gx < 0 || gx > 63 || gy < 0 || gy > 63) continue;
                if (this.tiles[key] || this.loading[key]) continue;
                this.loading[key] = true;
                this._loadSingleTile(gx, gy);
            }
        }
        Object.keys(this.tiles).forEach((key) => {
            const t = this.tiles[key];
            const dgx = t.gridX - cam.gridX;
            const dgy = t.gridY - cam.gridY;
            if (Math.abs(dgx) > this.UNLOAD_RADIUS || Math.abs(dgy) > this.UNLOAD_RADIUS) {
                this._unloadTile(key);
            }
        });
    }

    _key(gx, gy) { return gx + ',' + gy; }

    _loadSingleTile(gx, gy) {
        const key = this._key(gx, gy);
        const url = '/WorldEditor/SingleTileHeightmap' +
            '?preset=' + encodeURIComponent(this.editor.currentPreset) +
            '&tileGridX=' + gx + '&tileGridY=' + gy +
            '&globalMidHeight=' + this.globalMidHeight +
            '&globalHeightScale=' + this.globalHeightScale;

        getJSON(url).then((hm) => {
            if (!hm.success) { delete this.loading[key]; return; }
            const geo = buildTerrainGeometry(hm);
            const dx = gy - this.centerGridY;
            const dy = gx - this.centerGridX;
            const entry = {
                mesh: null, gridX: gx, gridY: gy, dx, dy, geo,
                vertsWidth: hm.vertsWidth, vertsHeight: hm.vertsHeight,
                loading: true
            };
            this.tiles[key] = entry;
            this._loadTexture(entry, () => {
                if (entry.mesh) {
                    entry.mesh.position.x = dx * this.tileWidthMesh;
                    entry.mesh.position.z = dy * this.tileWidthMesh;
                    entry.mesh.position.y = 0; // M1.1
                }
                delete this.loading[key];
                if (this.mapId <= 1) this._loadWater(entry);
            });
        }).catch(() => { delete this.loading[key]; });
    }

    /**
     * Load a tile's ground texturing.
     *
     * Prefers real 4-layer splatting (terrain-splat.js): the tileset, the
     * per-chunk layer indices and the packed alpha atlas, blended on the GPU at
     * ~8 texture repeats per chunk. Falls back to the server-baked composite if
     * splat is switched off, if the tile has no usable tileset, or if anything
     * in the splat path throws — a blurry tile beats a missing one.
     */
    _loadTexture(entry, callback) {
        if (isSplatEnabled()) {
            const splatUrl = '/WorldEditor/TerrainSplat?preset=' +
                encodeURIComponent(this.editor.currentPreset) +
                '&tileGridX=' + entry.gridX + '&tileGridY=' + entry.gridY;
            getJSON(splatUrl)
                .then((d) => buildSplatResources(d, maxAnisotropy()))
                .then((res) => {
                    if (!res) throw new Error('no splat data');
                    const vp = this.editor.viewport;
                    const mat = makeSplatMaterial(res, vp.lighting, vp.scene.fog);
                    entry.splat = res;
                    this._splatMaterials.add(mat);
                    this._finishTile(entry, mat);
                    if (callback) callback();
                })
                .catch((err) => {
                    console.warn('[terrain] splat failed for tile',
                        entry.gridX, entry.gridY, '- falling back to composite:',
                        err && err.message);
                    this._loadComposite(entry, callback);
                });
            return;
        }
        this._loadComposite(entry, callback);
    }

    _loadComposite(entry, callback) {
        const url = '/WorldEditor/Textures?preset=' + encodeURIComponent(this.editor.currentPreset) +
            '&tileGridX=' + entry.gridX + '&tileGridY=' + entry.gridY +
            '&pixelsPerChunk=' + this.textureRes;

        getJSON(url).then((tex) => {
            if (tex.success && tex.compositeBase64) {
                const img = new Image();
                img.onload = () => {
                    const t = new THREE.Texture(img);
                    t.colorSpace = THREE.SRGBColorSpace;
                    t.needsUpdate = true;
                    t.wrapS = THREE.ClampToEdgeWrapping;
                    t.wrapT = THREE.ClampToEdgeWrapping;
                    t.minFilter = THREE.LinearMipmapLinearFilter;
                    t.magFilter = THREE.LinearFilter;
                    t.anisotropy = maxAnisotropy();
                    t.generateMipmaps = true;
                    this._finishTile(entry, makeTerrainMaterial({ map: t }));
                    if (callback) callback();
                };
                img.src = 'data:image/png;base64,' + tex.compositeBase64;
                return;
            }
            this._finishTile(entry, makeTerrainMaterial({ color: 0x3a5a2a }));
            if (callback) callback();
        }).catch(() => {
            this._finishTile(entry, makeTerrainMaterial({ color: 0x3a5a2a }));
            if (callback) callback();
        });
    }

    _finishTile(entry, mat) {
        entry.mesh = new THREE.Mesh(entry.geo, mat);
        entry.mesh.position.y = 0; // M1.1

        // Phase 8: tag terrain mesh with tile identity for sculpt tool
        entry.mesh.userData.tileKey = this._key(entry.gridX, entry.gridY);
        entry.mesh.userData.tileGridX = entry.gridX;
        entry.mesh.userData.tileGridY = entry.gridY;

        // Phase 8: BVH for fast brush raycasting (also benefits walk-mode
        // terrain snap and placement ghost terrain raycast)
        entry.mesh.geometry.computeBoundsTree();

        this.editor.viewport.scene.add(entry.mesh);
        entry.loading = false;
    }

    _unloadTile(key) {
        const t = this.tiles[key];
        if (!t) return;
        const scene = this.editor.viewport.scene;
        if (t.mesh) {
            scene.remove(t.mesh);
            // Phase 8: dispose BVH before geometry
            if (t.mesh.geometry && t.mesh.geometry.boundsTree) {
                t.mesh.geometry.disposeBoundsTree();
            }
            if (t.mesh.geometry) t.mesh.geometry.dispose();
            if (t.mesh.material) {
                if (t.mesh.material.map) t.mesh.material.map.dispose();
                this._splatMaterials.delete(t.mesh.material);
                t.mesh.material.dispose();
            }
        }
        // The tileset array, alpha atlas and chunk LUT are per-tile GPU
        // allocations — the biggest ones the editor makes. Streaming leaks them
        // in minutes if eviction forgets.
        if (t.splat) { disposeSplatResources(t.splat); t.splat = null; }
        if (t.waterMesh) {
            scene.remove(t.waterMesh);
            if (t.waterMesh.geometry) t.waterMesh.geometry.dispose();
            // Water is now one material PER LIQUID TYPE on the same mesh, so this
            // is an array. Missing that leaks one material per tile per type.
            const wm = t.waterMesh.material;
            if (Array.isArray(wm)) wm.forEach((m) => m && m.dispose());
            else if (wm) wm.dispose();
        }
        delete this.tiles[key];
    }

    _loadWater(entry) {
        const url = '/WorldEditor/Water?preset=' + encodeURIComponent(this.editor.currentPreset) +
            '&tileGridX=' + entry.gridX + '&tileGridY=' + entry.gridY +
            '&globalMidHeight=' + this.globalMidHeight +
            '&globalHeightScale=' + this.globalHeightScale;

        getJSON(url).then((w) => {
            if (!w.success || !w.hasWater) return;
            const tileEntry = this.tiles[this._key(entry.gridX, entry.gridY)];
            if (!tileEntry) return;

            // Server returns flat positions[] + indices[] already in tile-local
            // mesh coordinates (tile centered at origin in its own frame). Per
            // VMaNGOS .map cell-vertex grid resolution — one quad per water cell
            // with 4 real heights from the vertex grid.
            if (!w.positions || !w.indices || w.positions.length === 0) return;

            const geo = new THREE.BufferGeometry();
            geo.setAttribute('position', new THREE.Float32BufferAttribute(new Float32Array(w.positions), 3));
            geo.computeVertexNormals();

            // Split by liquid type so lava is not blue. The server now sends one
            // legacy type code per vertex; without it (older server) everything
            // falls back to the tile-wide code, which is what used to happen.
            const types = w.liquidTypesBase64 ? b64ToUint8(w.liquidTypesBase64) : null;
            const split = partitionByType(w.indices, types, w.liquidType);
            if (!split) return;

            geo.setIndex(new THREE.BufferAttribute(split.index, 1));
            geo.clearGroups();
            for (const g of split.groups) geo.addGroup(g.start, g.count, g.materialIndex);

            if (split.materials.length > 1) {
                console.info('[water] tile', entry.gridX, entry.gridY, 'has',
                    split.materials.map((m) => waterTypeName(m.userData.liquidType)).join(' + '));
            }

            const waterMesh = new THREE.Mesh(geo, split.materials);
            const dx = entry.gridY - this.centerGridY;
            const dy = entry.gridX - this.centerGridX;
            waterMesh.position.x = dx * this.tileWidthMesh;
            waterMesh.position.z = dy * this.tileWidthMesh;
            waterMesh.position.y = 0;
            waterMesh.renderOrder = 1;
            this.editor.viewport.scene.add(waterMesh);
            tileEntry.waterMesh = waterMesh;
        });
    }

    applyWireframe(on) {
        if (this.isDungeon && this.dungeonGroup) {
            this.dungeonGroup.traverse((c) => {
                if (c.isMesh && c.material) c.material.wireframe = on;
            });
            return;
        }
        for (const k in this.tiles) {
            const t = this.tiles[k];
            if (t.mesh && t.mesh.material) t.mesh.material.wireframe = on;
        }
    }

    unloadTile(key) { this._unloadTile(key); }

    clearAll() {
        this._clearDungeon();
        Object.keys(this.tiles).forEach((k) => this._unloadTile(k));
        this.tiles = {};
        this.loading = {};
    }
}
