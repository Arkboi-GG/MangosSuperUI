// Character Viewer — M2 effects ("suiFx"): material animation and particles.
//
// === What this is for ===
//
// glTF can animate node transforms and skins. It cannot animate a material's
// colour, opacity or UV transform. So the server-side writers used to sample
// those M2 tracks once at rest and bake the result into a constant, and every
// pulsing glow, flickering flame, shimmering rune and scrolling energy band in
// the game's art arrived here as a still frame. Ashbringer's flame geosets, for
// instance, ship an alpha that steps 1→0→1 nine times a second and a texture
// weight that breathes 0.19↔1.0 on a 1.567 s loop; the previewer rendered
// whichever single value happened to be key 0.
//
// Particle emitters were missing for the same reason and one more: glTF has no
// concept of them at all. A forged weapon's flame was visible in the game client
// and absent from every preview surface.
//
// The server now ships both as JSON in the GLB's `extras`, under `suiFx` (see
// Services/M2Fx/M2FxManifest.cs for the wire format). This module drives the
// materials itself and hands the emitters to m2particles.js.
//
// === Contract ===
//
//   installM2Fx(gltf)                  → an fx handle, or null when the GLB
//                                        carries neither a manifest nor an
//                                        item bone rig
//   handle.update(ms, dtMs, camera)    → ms is absolute time, for the material
//                                        loops; dtMs and camera drive the item
//                                        mixer, the camera-facing bones and the
//                                        particle systems
//   handle.dispose()                   → restore the materials, stop the item
//                                        mixer and drop the particle meshes
//
// The handle is registered with the viewer's updater list (viewer.addFx) so the
// one render loop drives it. Attachments have no mixer of their own, which is
// why the MATERIAL half cannot ride on THREE.AnimationMixer.
//
// === The item bone rig (Thunderfury) ===
//
// A second class of motion is not material animation at all: it is skeleton
// behaviour. Thunderfury's lightning fins are weighted to bones whose transform
// rides an M2 global sequence, and its lightning orb hangs off bones flagged
// camera-facing. The server used to export item models rigid — no skin, no bone
// clips — so the fins pulsed their alpha while staying nailed in place and the
// orb sat there as a frozen card. GlbWriter now ships those models with an
// `ItemArmature`, `GlobalSequence_*` clips and identity `M2Billboard_*`
// correction nodes; this module owns the runtime half:
//
//   • one THREE.AnimationMixer per item scene, playing every global clip at
//     once (they are independent loops with independent periods, so exactly one
//     of them playing leaves the rest of the model frozen);
//   • the M2 camera-facing law, applied to the correction nodes AFTER the mixer
//     has sampled, so the two never fight over one quaternion channel.
//
// It is gated on the `ItemArmature` node name. The character body's armature is
// named `Armature`/`Armature_HairGeoset_N` and its global clips are already
// driven by animation-control.js — starting a second mixer on those would
// double-drive them.
//
// === Two things that will bite whoever edits this ===
//
// 1. GLTFLoader DEDUPES materials and textures across primitives. Mutating a
//    material in place would animate every other mesh that happens to share it,
//    and mutating `map.matrix` would animate every mesh sharing the IMAGE even
//    after the material was cloned — `Material.clone()` copies the texture
//    POINTER. So install clones both, per mesh, before touching anything.
//
// 2. Every track here is ABSOLUTE for its channel, not a multiplier over what
//    the exporter baked. The baked value IS one sample of the same curve, so
//    treating a track as modulation applies that sample twice — a pulse floor of
//    0.19 would render at 0.036. The manifest carries `baseAlpha`/`baseWeight`/
//    `baseRgb` for exactly the channels it does NOT animate, so the value is
//    always computed outright and `baseColorFactor` is ignored for these meshes.

import * as THREE from 'three';
import { installM2Particles } from './m2particles.js';

const MANIFEST_KEY = 'suiFx';
const SUPPORTED_VERSION = 1;

// Written by SkinnedGlbWriter.BuildItemRig. See its "Item rig" section.
const ITEM_ARMATURE_NAME = 'ItemArmature';
const BILLBOARD_NODE_RE = /^M2Billboard_(\d+)_f(\d+)/;
const GLOBAL_CLIP_RE = /^GlobalSequence_\d+$/;

// M2 bone flags. Same masks MSUIClient's AttachedItemBillboardLaw uses.
const IGNORE_PARENT_ROTATION = 0x04;
const BILLBOARD_MASK = 0x78;

// Three.js world up. The camera basis is locked to it exactly as the client
// locks its own to WoW's +Z, so a rolled camera cannot spin the billboards.
const WORLD_UP = new THREE.Vector3(0, 1, 0);

// The M2 axes the billboard law names, expressed in the glTF frame M2Reader
// converts models into. That conversion is the rotation (x, y, z) → (x, z, -y),
// so WoW's +Y arrives as (0, 0, -1), its +X is unchanged, and its -Z is (0, -1, 0).
const M2_AXIS_Y = new THREE.Vector3(0, 0, -1);
const M2_AXIS_X = new THREE.Vector3(1, 0, 0);
const M2_AXIS_NEG_Z = new THREE.Vector3(0, -1, 0);

// Scratch. One set, reused every frame — this runs per correction node per frame
// on up to four viewers at once and must not allocate.
const _W = new THREE.Matrix4();
const _Winv = new THREE.Matrix4();
const _boneModel = new THREE.Matrix4();
const _desired = new THREE.Matrix4();
const _local = new THREE.Matrix4();
const _basis = new THREE.Matrix4();
const _pos = new THREE.Vector3();
const _scale = new THREE.Vector3();
const _kept = new THREE.Quaternion();
const _facingQ = new THREE.Quaternion();
const _fwd = new THREE.Vector3();
const _right = new THREE.Vector3();
const _up = new THREE.Vector3();
const _bx = new THREE.Vector3();
const _by = new THREE.Vector3();
const _bz = new THREE.Vector3();
const _axisY = new THREE.Vector3();
const _axisZ = new THREE.Vector3();
const _fallback = new THREE.Vector3();

function normalizeOr(v, fallback) {
    if (v.lengthSq() > 1e-8) v.normalize();
    else v.copy(fallback);
    return v;
}

/**
 * Bind an item GLB's bone rig: play its global-sequence clips and rewrite its
 * camera-facing bones every frame.
 *
 * Returns null for any GLB without an `ItemArmature` — which is every character
 * body, every rigid item, and every GLB written before this existed.
 *
 * @param {object} gltf
 * @param {THREE.Object3D} sceneRoot  The item root as mounted in the scene.
 */
function installItemRig(gltf, sceneRoot) {
    let armature = null;
    const corrections = [];

    sceneRoot.traverse(node => {
        if (!node.name) return;
        if (node.name === ITEM_ARMATURE_NAME) { armature = node; return; }
        const m = BILLBOARD_NODE_RE.exec(node.name);
        if (!m) return;
        const flags = Number.parseInt(m[2], 10);
        if (Number.isFinite(flags)) corrections.push({ node, flags, depth: 0 });
    });

    if (!armature) return null;

    // Parent-before-child. A correction node under another correction node has
    // to read a parent world matrix that already carries the rewrite, which is
    // how the client's palette propagates a rewritten bone to its descendants.
    for (const c of corrections) {
        let depth = 0;
        for (let n = c.node; n && n !== sceneRoot; n = n.parent) depth++;
        c.depth = depth;
        // We own this matrix from here on; leaving matrixAutoUpdate set would
        // let updateMatrix() recompose it from the node's (identity) TRS and
        // wipe the correction on the very next frame.
        c.node.matrixAutoUpdate = false;
        c.node.matrix.identity();
    }
    corrections.sort((a, b) => a.depth - b.depth);

    const clips = Array.isArray(gltf?.animations)
        ? gltf.animations.filter(c => c && GLOBAL_CLIP_RE.test(c.name || ''))
        : [];

    let mixer = null;
    if (clips.length > 0) {
        mixer = new THREE.AnimationMixer(sceneRoot);
        for (const clip of clips) {
            const action = mixer.clipAction(clip);
            action.setLoop(THREE.LoopRepeat, Infinity);
            action.play();
        }
    }

    if (!mixer && corrections.length === 0) return null;

    /** The M2 camera-facing basis, ported from SpellMeshSkinningLaw.ApplyBillboardBones. */
    function facingQuaternion(billboard, kept, out) {
        if (billboard & 0x08) {
            // Full facing: the bone's local +X looks straight down the barrel of
            // the camera and its local YZ plane spans the screen.
            _bx.copy(_fwd).negate();
            _by.copy(_right);
            _bz.copy(_up);
        } else if (billboard & 0x40) {
            // Keep the bone's animated Y axis; solve the rest against the view.
            normalizeOr(_bz.copy(M2_AXIS_Y).applyQuaternion(kept), M2_AXIS_Y);
            normalizeOr(_by.copy(_fwd).cross(_bz), _right);
            normalizeOr(_bx.copy(_by).cross(_bz), _fallback.copy(_fwd).negate());
        } else if (billboard & 0x10) {
            // Keep the bone's animated X axis.
            normalizeOr(_bx.copy(M2_AXIS_X).applyQuaternion(kept), _fallback.copy(_fwd).negate());
            normalizeOr(_bz.copy(_fwd).cross(_bx), _up);
            normalizeOr(_by.copy(_bz).cross(_bx), _right);
        } else {
            // Remaining mode (normally 0x20): keep the authored -Z axis.
            normalizeOr(_by.copy(M2_AXIS_NEG_Z).applyQuaternion(kept), _right);
            normalizeOr(_bx.copy(_fwd).cross(_by), _fallback.copy(_fwd).negate());
            normalizeOr(_bz.copy(_bx).cross(_by), _up);
        }

        // MSUIClient builds a row-vector matrix whose rows are (bx, bz, -by), i.e.
        // column-major axes (bx, bz, -by) in WoW space. Carrying that through the
        // Z-up→Y-up basis change (R_gltf = T·R_wow·T⁻¹, and T maps e2→e3, e3→-e2)
        // permutes the last two columns into (Bx, -By, -Bz), where B = T·b. Every
        // b above is already computed in the glTF frame, so this IS that result.
        _axisY.copy(_by).negate();
        _axisZ.copy(_bz).negate();
        _basis.makeBasis(_bx, _axisY, _axisZ);
        return out.setFromRotationMatrix(_basis);
    }

    function applyBillboards(camera) {
        if (!camera || corrections.length === 0) return;

        // Nothing has folded this frame's animation into a world matrix yet — the
        // renderer does that at draw time — and for an equipped weapon the
        // character's own mixer just moved the hand this hangs off. Pull the
        // ancestors and the whole item subtree forward before reading either.
        sceneRoot.updateWorldMatrix(true, true);
        camera.updateWorldMatrix(true, false);

        _W.copy(sceneRoot.matrixWorld);
        _Winv.copy(_W).invert();

        camera.getWorldDirection(_fwd);
        _right.copy(_fwd).cross(WORLD_UP);
        // Camera looking straight up or down: the cross degenerates, so fall back
        // to the camera's own X axis rather than to a zero vector.
        if (_right.lengthSq() < 1e-8) _right.setFromMatrixColumn(camera.matrixWorld, 0);
        _right.normalize();
        _up.copy(_right).cross(_fwd).normalize();

        // Into the item's OWN model space. Everything below then matches the
        // client, which does the same algebra in model space — and it is what
        // lets an equipped weapon inherit an arbitrary hand rotation and scale
        // without the orb picking up the character's facing.
        _fwd.transformDirection(_Winv);
        _right.transformDirection(_Winv);
        _up.transformDirection(_Winv);

        for (const c of corrections) {
            const bone = c.node.parent;
            if (!bone) continue;

            _boneModel.multiplyMatrices(_Winv, bone.matrixWorld);
            _boneModel.decompose(_pos, _kept, _scale);

            const billboard = c.flags & BILLBOARD_MASK;
            if (c.flags & IGNORE_PARENT_ROTATION) _facingQ.identity();
            else if (billboard !== 0) facingQuaternion(billboard, _kept, _facingQ);
            else continue;

            // Position and scale survive; only the orientation is replaced. Then
            // back out the local matrix that puts this node there:
            //   parentWorld · local = W · desired  ⟹  local = boneModel⁻¹ · desired
            _desired.compose(_pos, _facingQ, _scale);
            _local.copy(_boneModel).invert().multiply(_desired);
            c.node.matrix.copy(_local);
            c.node.updateWorldMatrix(false, true);
        }
    }

    return {
        clipCount: clips.length,
        billboardCount: corrections.length,
        update(dtMs, camera) {
            if (mixer) mixer.update(Math.max(0, dtMs || 0) / 1000);
            applyBillboards(camera);
        },
        dispose() {
            if (mixer) {
                mixer.stopAllAction();
                mixer.uncacheRoot(sceneRoot);
                mixer = null;
            }
            for (const c of corrections) {
                c.node.matrix.identity();
                c.node.matrixAutoUpdate = true;
                c.node.updateWorldMatrix(false, true);
            }
            corrections.length = 0;
        },
    };
}

/**
 * Read the animation manifest out of a loaded glTF and bind it to the scene's
 * materials.
 *
 * @param {object} gltf  The object GLTFLoader resolved with.
 * @param {THREE.Object3D} [root]  Subtree to bind (defaults to gltf.scene).
 * @returns {{update: (ms:number, dtMs:number, camera:object)=>void,
 *            dispose: ()=>void, meshCount: number}|null}
 */
export function installM2Fx(gltf, root = null) {
    const sceneRoot = root || gltf?.scene;
    if (!sceneRoot) return null;

    // A manifest is no longer the only reason to hold a handle: an item whose
    // motion lives entirely in its skeleton (Thunderfury's fins) can carry an
    // empty `meshes` dictionary and still need a per-frame tick.
    const manifest = readManifest(gltf);
    const rig = installItemRig(gltf, sceneRoot);
    if (!manifest && !rig) return null;

    const bindings = [];

    if (manifest) sceneRoot.traverse(node => {
        if (!node.isMesh) return;

        const mats = Array.isArray(node.material) ? node.material : [node.material];
        for (let i = 0; i < mats.length; i++) {
            const mat = mats[i];
            if (!mat) continue;

            // Keyed by mesh name where the writer gives each submesh its own mesh (GlbWriter), and by
            // MATERIAL name where it does not (WeaponPreviewService puts every pass into one mesh, so
            // the only per-pass identity in the glTF is the material). Mesh name wins when both match.
            const entry = manifest.meshes[node.name] || manifest.meshes[mat.name];
            if (!entry) continue;

            // Clone per mesh — see note 1 above. blend-suffix.js already clones
            // for its own reasons, but it only touches materials whose name
            // carries a _blendN suffix and it does not clone the texture, so we
            // cannot rely on having a private instance.
            const owned = mat.clone();
            owned.name = mat.name;
            if (owned.map) {
                owned.map = owned.map.clone();
                owned.map.needsUpdate = true;
            }
            if (Array.isArray(node.material)) node.material[i] = owned;
            else node.material = owned;

            bindings.push(bind(owned, entry));
        }
    });

    // Particle emitters. Their texture sheets ride in the GLB's binary chunk and
    // are resolved through the loader, which is async — but every caller here
    // (loader.js, equip.js, the forge preview pages) installs synchronously while
    // mounting geometry. So the handle is returned immediately and the particle
    // systems attach to it when they land, usually within the same frame.
    // `disposed` guards the case where the object is swapped out first.
    let particles = null;
    let disposed = false;
    if (manifest && Array.isArray(manifest.emitters) && manifest.emitters.length > 0) {
        installM2Particles(gltf, manifest, sceneRoot)
            .then(handle => {
                if (disposed) handle?.dispose();
                else particles = handle;
            })
            .catch(err => console.warn('[m2fx] particle install failed', err));
    }

    if (bindings.length === 0 && !rig && !(manifest && Array.isArray(manifest.emitters))) {
        return null;
    }

    return {
        meshCount: bindings.length,
        get emitterCount() { return particles ? particles.emitterCount : 0; },
        get boneClipCount() { return rig ? rig.clipCount : 0; },
        get billboardBoneCount() { return rig ? rig.billboardCount : 0; },
        // Order is load-bearing: the billboard pass inside rig.update reads the
        // bone world matrices the mixer just wrote, and the particle systems
        // billboard against a camera that must already be up to date.
        update(ms, dtMs, camera) {
            for (const b of bindings) b.update(ms);
            rig?.update(dtMs, camera);
            if (particles) particles.update(ms, dtMs, camera);
        },
        dispose() {
            disposed = true;
            for (const b of bindings) b.dispose();
            bindings.length = 0;
            rig?.dispose();
            particles?.dispose();
            particles = null;
        },
    };
}

/**
 * A keyed set of fx handles that behaves like one handle.
 *
 * The character owns one of these. Attachments (helm, spaulders, weapons) are
 * separate GLBs loaded and swapped by equip.js, which has no reference to the
 * viewer and should not grow one — so it puts its handles in here under the
 * attachment id, and the page's boot code registers the whole registry with
 * `viewer.addFx` exactly once. Re-mounting an attachment replaces its entry and
 * disposes the old one, which is what stops a swapped-out weapon's material
 * bindings from ticking forever.
 *
 * @returns {{set:(key:any,handle:object|null)=>void, remove:(key:any)=>void,
 *            update:(ms:number,dtMs:number,camera:object)=>void,
 *            dispose:()=>void, size:()=>number}}
 */
export function createFxRegistry() {
    const handles = new Map();

    function remove(key) {
        const existing = handles.get(key);
        if (!existing) return;
        handles.delete(key);
        try { existing.dispose?.(); } catch { /* already gone */ }
    }

    return {
        set(key, handle) {
            remove(key);
            if (handle) handles.set(key, handle);
        },
        remove,
        update(ms, dtMs, camera) {
            for (const handle of handles.values()) handle.update(ms, dtMs, camera);
        },
        dispose() {
            for (const key of Array.from(handles.keys())) remove(key);
        },
        size() { return handles.size; },
    };
}

/** Pull `extras.suiFx` off the glTF root. Returns null when absent or too new. */
function readManifest(gltf) {
    // GLTFLoader does not surface root-level extras on the result object, but it
    // keeps the parsed JSON on the parser. userData is checked first so a future
    // loader version that does surface them keeps working.
    const extras = gltf?.userData?.[MANIFEST_KEY]
        ? gltf.userData
        : gltf?.parser?.json?.extras;
    const manifest = extras?.[MANIFEST_KEY];
    if (!manifest || !manifest.meshes) return null;

    if (typeof manifest.v === 'number' && manifest.v > SUPPORTED_VERSION) {
        console.warn(`[m2fx] manifest version ${manifest.v} is newer than this client (${SUPPORTED_VERSION}); ignoring`);
        return null;
    }
    return manifest;
}

/**
 * Bind one material to one manifest entry.
 *
 * Captures the material's pre-install state so dispose() can put it back — the
 * Armor Forge tears the viewer down and rebuilds it on every race swap, and a
 * half-restored material would leak a wrong colour into the next mount.
 */
function bind(mat, entry) {
    const rgb = track(entry.rgb);
    const alpha = track(entry.alpha);
    const weight = track(entry.weight);
    const uv = entry.uv || null;
    const uvTranslate = track(uv?.translate);
    const uvRotate = track(uv?.rotate);
    const uvScale = track(uv?.scale);
    const uvBase = Array.isArray(uv?.base) && uv.base.length === 5 ? uv.base : null;

    const baseRgb = Array.isArray(entry.baseRgb) && entry.baseRgb.length === 3 ? entry.baseRgb : null;
    const baseAlpha = typeof entry.baseAlpha === 'number' ? entry.baseAlpha : 1;
    const baseWeight = typeof entry.baseWeight === 'number' ? entry.baseWeight : 1;

    const animatesUv = !!(uvTranslate || uvRotate || uvScale || uvBase);
    const animatesOpacity = !!(alpha || weight);

    const restore = {
        color: mat.color ? mat.color.clone() : null,
        opacity: mat.opacity,
        transparent: mat.transparent,
        depthWrite: mat.depthWrite,
        wrapS: mat.map ? mat.map.wrapS : null,
        wrapT: mat.map ? mat.map.wrapT : null,
        matrixAutoUpdate: mat.map ? mat.map.matrixAutoUpdate : null,
    };

    if (animatesUv && mat.map) {
        // A UV scroll past 1.0 must tile, not smear. GLTFLoader leaves textures
        // at ClampToEdgeWrapping unless the glTF sampler says otherwise, and the
        // writers emit no sampler, so this has to be forced here — a scrolling
        // shield texture would otherwise stretch its last pixel column across
        // the whole face.
        mat.map.wrapS = THREE.RepeatWrapping;
        mat.map.wrapT = THREE.RepeatWrapping;
        mat.map.matrixAutoUpdate = false;
    }

    if (animatesOpacity) {
        // An opacity that moves has to be composited, whatever the blend mode
        // decided. Additive passes (blend 3/4) are already transparent and
        // depth-write-free, but an opaque or alpha-key pass with an animated
        // weight would otherwise have its opacity silently ignored — three.js
        // drops material.opacity entirely unless transparent is set — and would
        // punch a depth hole through whatever it fades into.
        mat.transparent = true;
        mat.depthWrite = false;
    }

    return {
        update(ms) {
            if (rgb && mat.color) {
                const v = rgb.sample(ms);
                mat.color.setRGB(v[0], v[1], v[2]);
            } else if (baseRgb && mat.color) {
                mat.color.setRGB(baseRgb[0], baseRgb[1], baseRgb[2]);
            }

            if (animatesOpacity) {
                const a = alpha ? alpha.sample(ms)[0] : baseAlpha;
                const w = weight ? weight.sample(ms)[0] : baseWeight;
                mat.opacity = clamp01(a * w);
            }

            if (animatesUv && mat.map) {
                const t = uvTranslate ? uvTranslate.sample(ms) : null;
                const r = uvRotate ? uvRotate.sample(ms) : null;
                const s = uvScale ? uvScale.sample(ms) : null;
                const tx = t ? t[0] : (uvBase ? uvBase[0] : 0);
                const ty = t ? t[1] : (uvBase ? uvBase[1] : 0);
                const rot = r ? r[0] : (uvBase ? uvBase[2] : 0);
                const sx = s ? s[0] : (uvBase ? uvBase[3] : 1);
                const sy = s ? s[1] : (uvBase ? uvBase[4] : 1);
                // Around (0.5, 0.5) — the client rotates and scales texture space
                // about its centre, not its corner.
                mat.map.matrix.setUvTransform(tx, ty, sx, sy, rot, 0.5, 0.5);
            }
        },
        dispose() {
            if (restore.color && mat.color) mat.color.copy(restore.color);
            mat.opacity = restore.opacity;
            mat.transparent = restore.transparent;
            mat.depthWrite = restore.depthWrite;
            if (mat.map && restore.wrapS !== null) {
                mat.map.wrapS = restore.wrapS;
                mat.map.wrapT = restore.wrapT;
                mat.map.matrixAutoUpdate = restore.matrixAutoUpdate;
            }
            mat.needsUpdate = true;
        },
    };
}

/**
 * Compile one manifest track into a sampler.
 *
 * `step` tracks hold a key until the next one; linear tracks blend. Getting that
 * backwards turns Ashbringer's flame flicker into a smooth fade and a smooth
 * fade into a strobe, so the interpolation kind travels in the manifest rather
 * than being guessed from the key spacing.
 */
function track(def) {
    if (!def || !Array.isArray(def.t) || !Array.isArray(def.v)) return null;
    const times = def.t;
    const keys = def.v;
    const n = Math.min(times.length, keys.length);
    if (n < 2) return null;

    const duration = def.dur > 0 ? def.dur : times[n - 1];
    if (!(duration > 0)) return null;

    const step = def.step === true;
    const scalar = typeof keys[0] === 'number';
    const width = scalar ? 1 : keys[0].length;
    const out = new Array(width).fill(0);

    const at = (i, c) => (scalar ? keys[i] : keys[i][c]);

    return {
        sample(ms) {
            let t = ms % duration;
            if (t < 0) t += duration;

            // Linear scan. Item material tracks measure 2–10 keys; a binary
            // search would be more code than the loop it replaces.
            let i = 0;
            while (i < n - 1 && times[i + 1] <= t) i++;

            if (step || i >= n - 1) {
                for (let c = 0; c < width; c++) out[c] = at(i, c);
                return out;
            }

            const span = times[i + 1] - times[i];
            const f = span > 0 ? (t - times[i]) / span : 0;
            for (let c = 0; c < width; c++) {
                const a = at(i, c), b = at(i + 1, c);
                out[c] = a + (b - a) * f;
            }
            return out;
        },
    };
}

function clamp01(v) {
    return v < 0 ? 0 : v > 1 ? 1 : (Number.isFinite(v) ? v : 1);
}
