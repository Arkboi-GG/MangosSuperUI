// collision-world.js — real building/tree/interior collision from VMaNGOS vmaps.
//
// ═══════════════════════════════════════════════════════════════════════════
// THE VMAP PORT (handoff §5.6 "Real collision — the vmap port")
// ═══════════════════════════════════════════════════════════════════════════
// The character used to raycast the RENDER meshes (WMO/doodad InstancedMeshes).
// A render mesh has no interior/doorway/step semantics, so that gives a crude
// outer shell and nothing to walk into — the "collision is false" bug.
//
// This fetches the server's OWN extracted collision geometry (the same meshes
// mangosd uses), served by GET /WorldEditor/Collision as a flat WoW-world
// triangle buffer, converts it into three.js scene space with the SAME centre-
// tile transform the .gps HUD uses (so it lines up with the rendered terrain),
// and builds three-mesh-bvh acceleration structures the character raycasts
// against — real walls with doorways, floors, stairs and interiors.
//
// WoW world (+X north, +Y west, +Z up) -> three.js scene (Y up), inverse of the
// verified .gps transform (handoff §3):
//     wowX = 32T - (meshZ/W + 0.5 + centerGridX) * T
//     wowY = 32T - (meshX/W + 0.5 + centerGridY) * T          meshY = wowZ
//   => sceneX = W * (31.5 - centerGridY - wowY/T)
//      sceneZ = W * (31.5 - centerGridX - wowX/T)
//      sceneY = wowZ
// with T = 533.33333 yd/tile and W = tileGrid.tileWidthMesh (scene units/tile).

import * as THREE from 'three';
import { getJSON } from './net.js';

const TILE_YARDS = 533.33333;

// Triangles per BVH mesh. Kept SMALL on purpose: each mesh's computeBoundsTree
// is a synchronous burst, and the build runs one mesh per event-loop turn (see
// _buildStep), so small chunks = short hitches instead of one multi-minute freeze
// on a city. A dense block (Stormwind) becomes many meshes built across frames.
const TRIS_PER_MESH = 64000;

export class CollisionWorld {
    constructor(editor) {
        this.editor = editor;
        this.meshes = [];            // invisible THREE.Mesh raycast proxies
        this.triangleCount = 0;
        this.stats = null;
        this._loadedKey = null;
        this._loading = false;

        // Incremental BVH build state (see _buildStep) — keeps a big city from
        // freezing the main thread.
        this._buildTimer = null;
        this._scene = null;
        this._builtTris = 0;

        // Invisible, DOUBLE-SIDED: vmap winding is not consistent, so a
        // single-sided target would make some walls and floors invisible to the
        // sweep depending on which way the extractor emitted them.
        this._material = new THREE.MeshBasicMaterial({ side: THREE.DoubleSide, visible: false });
    }

    /** Built and non-empty — the controller only switches to vmap collision then. */
    get ready() { return this.meshes.length > 0; }

    /**
     * Fetch and build the collision block for a preset. Safe to call again with
     * the same key (no-op) or a new one (rebuild). radius 1 = the 3×3 ADT block,
     * matching how terrain loads.
     */
    loadForPreset(presetKey, opts) {
        opts = opts || {};
        if (!presetKey) return Promise.resolve(false);

        // includeM2 defaults OFF: buildings/interiors (WMO vmaps) are the point of
        // the port, and a dense city's thousands of tree/lamp/fence M2s are what
        // blow the triangle count into the millions. Trees you can walk through is
        // an acceptable trade for not freezing on Stormwind; pass includeM2:true
        // for a forest zone where trunk collision matters.
        const includeM2 = opts.includeM2 === true;
        const radius = (opts.radius != null) ? opts.radius : 1;
        const key = presetKey + '|r' + radius + '|m2' + (includeM2 ? 1 : 0);

        if (this._loadedKey === key && this.ready) return Promise.resolve(true);
        if (this._loading) return Promise.resolve(false);
        this._loading = true;

        const url = '/WorldEditor/Collision?preset=' + encodeURIComponent(presetKey) +
                    '&radius=' + radius + '&includeM2=' + (includeM2 ? 'true' : 'false');

        return getJSON(url).then((r) => {
            this._loading = false;
            if (!r || !r.success) {
                console.warn('[collision] load failed:', r && r.error);
                this.clear();
                return false;
            }
            this._build(r);
            this._loadedKey = key;

            const s = r.stats || {};
            console.log('[collision] ' + this.triangleCount.toLocaleString() + ' triangles from ' +
                (s.TilesLoaded != null ? s.TilesLoaded : '?') + ' tile(s); ' +
                (s.SpawnsUsed != null ? s.SpawnsUsed : '?') + ' spawn(s), ' +
                (s.SpawnsDuplicate != null ? s.SpawnsDuplicate : '?') + ' cross-tile dup, ' +
                (s.SpawnsSkippedM2 != null ? s.SpawnsSkippedM2 : '?') + ' m2 skipped, ' +
                (s.SpawnsUnresolved != null ? s.SpawnsUnresolved : '?') + ' no .vmo. vmaps: ' + r.vmapDir);
            if (r.notes && r.notes.length) console.info('[collision]', r.notes.join(' | '));
            if (this.triangleCount === 0) {
                console.warn('[collision] 0 triangles — real building collision is INACTIVE, ' +
                    'the character falls back to render-mesh sweeping. Check the vmaps dir and the notes above.');
            }
            return true;
        }).catch((err) => {
            this._loading = false;
            console.warn('[collision] load error:', err && err.message);
            this.clear();
            return false;
        });
    }

    _build(payload) {
        this.clear();
        this.stats = payload.stats || null;

        const bytes = b64ToBytes(payload.positionsBase64 || '');
        const floatLen = Math.floor(bytes.byteLength / 4);
        const wow = new Float32Array(bytes.buffer, bytes.byteOffset, floatLen);
        const nVerts = Math.floor(wow.length / 3);
        this.triangleCount = Math.floor(nVerts / 3);
        if (this.triangleCount === 0) return;

        const tg = this.editor.tileGrid;
        const T = TILE_YARDS;
        const W = (tg && tg.tileWidthMesh) ? tg.tileWidthMesh : T;
        const cgx = tg ? tg.centerGridX : 0;
        const cgy = tg ? tg.centerGridY : 0;

        // WoW world -> scene, per the inverse .gps transform above.
        const scene = new Float32Array(nVerts * 3);
        for (let i = 0; i < nVerts; i++) {
            const wx = wow[i * 3], wy = wow[i * 3 + 1], wz = wow[i * 3 + 2];
            scene[i * 3]     = W * (31.5 - cgy - wy / T);
            scene[i * 3 + 1] = wz;
            scene[i * 3 + 2] = W * (31.5 - cgx - wx / T);
        }

        // Build the BVH meshes INCREMENTALLY — one chunk per event-loop turn —
        // so a huge city (Stormwind's vmap is the whole city in one .vmo, ~900k
        // triangles) does not freeze the browser. computeBoundsTree is the
        // expensive synchronous step; yielding between chunks keeps input and
        // rendering alive. Collision comes online progressively: `ready` flips
        // true after the first chunk and the character falls back to the
        // render-mesh sweep until then.
        this._scene = scene;
        this._builtTris = 0;
        this._scheduleBuild();
    }

    _scheduleBuild() {
        if (this._buildTimer != null) return;
        this._buildTimer = setTimeout(() => this._buildStep(), 0);
    }

    _buildStep() {
        this._buildTimer = null;
        const scene = this._scene;
        if (!scene) return;

        const start = this._builtTris;
        const count = Math.min(TRIS_PER_MESH, this.triangleCount - start);
        if (count <= 0) { this._scene = null; return; }

        const geo = new THREE.BufferGeometry();
        geo.setAttribute('position',
            new THREE.BufferAttribute(new Float32Array(scene.subarray(start * 9, (start + count) * 9)), 3));
        // three-mesh-bvh wants an index; the trivial sequential one makes the
        // soup indexed without changing a single triangle.
        const idx = new Uint32Array(count * 3);
        for (let k = 0; k < idx.length; k++) idx[k] = k;
        geo.setIndex(new THREE.BufferAttribute(idx, 1));
        try { geo.computeBoundsTree(); } catch (e) { /* falls back to brute force */ }

        const mesh = new THREE.Mesh(geo, this._material);
        mesh.name = 'collision-proxy';
        mesh.visible = false;          // never rendered — a raycast target only
        mesh.matrixAutoUpdate = false;
        mesh.updateMatrixWorld(true);  // identity: geometry is already scene-space
        this.meshes.push(mesh);

        this._builtTris = start + count;
        if (this._builtTris < this.triangleCount) {
            this._buildTimer = setTimeout(() => this._buildStep(), 0);   // yield, then next chunk
        } else {
            this._scene = null;
        }
    }

    clear() {
        if (this._buildTimer != null) { clearTimeout(this._buildTimer); this._buildTimer = null; }
        this._scene = null;
        this._builtTris = 0;
        for (const m of this.meshes) {
            if (m.geometry) {
                if (m.geometry.boundsTree && m.geometry.disposeBoundsTree) m.geometry.disposeBoundsTree();
                m.geometry.dispose();
            }
        }
        this.meshes = [];
        this.triangleCount = 0;
        this._loadedKey = null;
    }
}

function b64ToBytes(b64) {
    const bin = atob(b64);
    const out = new Uint8Array(bin.length);
    for (let i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i);
    return out;
}
