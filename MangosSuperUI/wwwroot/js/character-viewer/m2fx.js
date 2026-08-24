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
//                                        carries no manifest
//   handle.update(ms, dtMs, camera)    → ms is absolute time, for the material
//                                        loops; dtMs and camera are for the
//                                        particle systems, which integrate
//                                        motion and billboard against the view
//   handle.dispose()                   → restore the materials and drop the
//                                        particle meshes
//
// The handle is registered with the viewer's updater list (viewer.addFx) so the
// one render loop drives it. Attachments have no mixer of their own, which is
// why this cannot ride on THREE.AnimationMixer.
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
    const manifest = readManifest(gltf);
    if (!manifest) return null;

    const sceneRoot = root || gltf?.scene;
    if (!sceneRoot) return null;

    const bindings = [];

    sceneRoot.traverse(node => {
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
    if (Array.isArray(manifest.emitters) && manifest.emitters.length > 0) {
        installM2Particles(gltf, manifest, sceneRoot)
            .then(handle => {
                if (disposed) handle?.dispose();
                else particles = handle;
            })
            .catch(err => console.warn('[m2fx] particle install failed', err));
    }

    if (bindings.length === 0 && !Array.isArray(manifest.emitters)) return null;

    return {
        meshCount: bindings.length,
        get emitterCount() { return particles ? particles.emitterCount : 0; },
        update(ms, dtMs, camera) {
            for (const b of bindings) b.update(ms);
            if (particles) particles.update(ms, dtMs, camera);
        },
        dispose() {
            disposed = true;
            for (const b of bindings) b.dispose();
            bindings.length = 0;
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
