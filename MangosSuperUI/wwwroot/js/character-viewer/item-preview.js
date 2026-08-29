// item-preview.js — mount ONE item GLB with the complete WoW decode pipeline.
//
// WHY THIS EXISTS
// ───────────────
// The Items and GameObjects pages previewed models with Google's stock <model-viewer>. Everything
// our writers put in the file was thrown away at render time, because <model-viewer> is a plain
// glTF viewer:
//
//   • `mat_*_blend{N}` material names (GlbWriter) carry the WoW blend mode. Undecoded, an additive
//     glow shell composites as translucent paint instead of light.
//   • the `suiFx` extras manifest carries material animation (colour / opacity / UV-scroll) and
//     particle emitters. Undecoded, every animated track is frozen at whichever key got baked into
//     baseColorFactor, and no particle ever spawns.
//   • ItemVisual enchant effects are mounted by
//     GlbWriter.EmbedVisualEffects as manifest EMITTERS ONLY — no geometry is merged. So without a
//     suiFx decoder they render as literally nothing.
//     Thunderfury is different: display 30606 has ItemVisual 0 and its native M2 rig carries the
//     lightning, so preserving that rig and its global sequences is mandatory.
//
// This module runs the same four passes, in the same order, that the Weapon Forge viewer runs, and
// drives the fx handle every frame. It is deliberately generic — it takes a container and a URL and
// knows nothing about items — so the GameObjects page can adopt it unchanged.
//
// ORDER IS LOAD-BEARING (see blend-suffix.js):
//   1. applyBlendSuffix   — resolve WoW blend/alpha state from the material name
//   2. applyEnvMapping    — AFTER 1: it copies the blend state 1 resolved onto the matcap
//   3. installM2Fx        — the suiFx manifest: material animation + particle emitters
//   4. applyMultiTexture  — AFTER 3: installM2Fx CLONES the material, and Material.clone() drops
//                           onBeforeCompile, which is exactly what applyMultiTexture installs
//
// Passes 2 and 4 are no-ops on GLBs written by GlbWriter today (it emits no `_env` / `_mod` markers
// and a single UV set). They are here so this module is a faithful copy of the reference pipeline
// and lights up for free when the writer starts emitting them.

import * as THREE from 'three';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { applyBlendSuffix, applyEnvMapping, applyMultiTexture } from './blend-suffix.js';
import { installM2Fx } from './m2fx.js';

const ATTR = 'data-sui-glb';

// One live handle per container element. Re-mounting a container disposes the old one first, which
// is what keeps the edit form (whose DOM is wholesale-replaced on every icon pick) from leaking a
// WebGL context each time — browsers cap live contexts at roughly 8–16.
const _mounts = new Map();

const _loader = new GLTFLoader();

/**
 * Mount a GLB into `container` with the full decode pipeline.
 *
 * @param {HTMLElement} container
 * @param {string} glbUrl
 * @param {{autoRotate?: boolean, autoRotateSpeed?: number}} [opts]
 * @returns {{setUrl(url: string): void, dispose(): void} | null}
 */
export function mountItemPreview(container, glbUrl, opts = {}) {
    if (!container || !glbUrl) return null;

    // Re-mounting the same container replaces the previous scene rather than stacking one.
    const existing = _mounts.get(container);
    if (existing) { try { existing.dispose(); } catch (e) { /* keep going */ } }

    const renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true });
    renderer.setPixelRatio(window.devicePixelRatio || 1);
    // Deliberately NOT setting toneMapping / outputColorSpace. Every writer authors materials with
    // WithUnlitShader(), and the Forge viewer relies on the three.js defaults. An ACES curve (or
    // model-viewer's old exposure="1.2") crushes exactly the additive passes this exists to show.
    renderer.domElement.style.cssText = 'width:100%;height:100%;display:block;';
    container.appendChild(renderer.domElement);

    const scene = new THREE.Scene();
    const camera = new THREE.PerspectiveCamera(45, 1, 0.01, 100);
    camera.position.set(1.4, 0.9, 1.4);

    const controls = new OrbitControls(camera, renderer.domElement);
    controls.enableDamping = true;
    controls.autoRotate = opts.autoRotate !== false;
    controls.autoRotateSpeed = opts.autoRotateSpeed ?? 1.5;

    // No lights and no grid on purpose: unlit materials ignore lights, and a grid is an authoring
    // aid that does not belong in an item card.

    let model = null;
    let fx = null;
    let disposed = false;
    let rafId = 0;
    const clock = new THREE.Clock();
    let elapsedMs = 0;

    function resize() {
        const w = container.clientWidth, h = container.clientHeight;
        if (w === 0 || h === 0) return;          // hidden panel — ResizeObserver re-fires on show
        renderer.setSize(w, h, false);
        camera.aspect = w / h;
        camera.updateProjectionMatrix();
    }

    // A ResizeObserver, not a window listener: the edit form renders while `#colEdit` is still
    // display:none, so the first layout is 0x0 and must self-correct once the panel is shown.
    const ro = typeof ResizeObserver !== 'undefined' ? new ResizeObserver(resize) : null;
    ro?.observe(container);

    function frameObject(obj) {
        const box = new THREE.Box3().setFromObject(obj);
        const size = box.getSize(new THREE.Vector3());
        const center = box.getCenter(new THREE.Vector3());
        const maxDim = Math.max(size.x, size.y, size.z) || 1;
        controls.target.copy(center);
        camera.position.copy(center).add(new THREE.Vector3(maxDim * 1.6, maxDim * 1.1, maxDim * 1.6));
        camera.near = maxDim / 100;
        camera.far = maxDim * 100;
        camera.updateProjectionMatrix();
        controls.update();
    }

    function disposeModel() {
        if (fx) { try { fx.dispose?.(); } catch (e) { /* ignore */ } fx = null; }
        if (!model) return;
        scene.remove(model);
        model.traverse(node => {
            if (!node.isMesh) return;
            node.geometry?.dispose?.();
            const mats = Array.isArray(node.material) ? node.material : [node.material];
            for (const m of mats) {
                if (!m) continue;
                m.map?.dispose?.();
                m.matcap?.dispose?.();
                m.aoMap?.dispose?.();
                m.dispose?.();
            }
        });
        model = null;
    }

    function load(url) {
        _loader.load(url, gltf => {
            if (disposed) return;
            disposeModel();
            const root = gltf.scene;

            applyBlendSuffix(root);                                  // 1
            applyEnvMapping(root);                                   // 2 — after 1
            try { fx = installM2Fx(gltf); }                          // 3
            catch (err) { console.warn('[item-preview] m2fx install failed', err); fx = null; }
            applyMultiTexture(root);                                 // 4 — after 3

            model = root;
            scene.add(root);
            resize();
            frameObject(root);
        }, undefined, err => console.warn('[item-preview] load failed', url, err));
    }

    function tick() {
        if (disposed) return;
        rafId = requestAnimationFrame(tick);
        const dt = clock.getDelta();
        if (fx) {
            const dtMs = dt * 1000;
            elapsedMs += dtMs;
            // The camera argument is required — m2particles.js billboards its sprites against it.
            try { fx.update(elapsedMs, dtMs, camera); }
            catch (err) { console.warn('[item-preview] m2fx failed; dropping it', err); fx = null; }
        }
        controls.update();
        renderer.render(scene, camera);
    }

    const handle = {
        setUrl(url) { if (!disposed && url) load(url); },
        dispose() {
            if (disposed) return;
            disposed = true;
            if (rafId) cancelAnimationFrame(rafId);
            ro?.disconnect();
            disposeModel();
            controls.dispose?.();
            renderer.dispose();
            // Free the GL context eagerly rather than waiting for GC — these mounts churn.
            try { renderer.forceContextLoss?.(); } catch (e) { /* not fatal */ }
            renderer.domElement.remove();
            if (_mounts.get(container) === handle) _mounts.delete(container);
        },
    };

    _mounts.set(container, handle);
    resize();
    load(glbUrl);
    tick();
    return handle;
}

/**
 * Mount every not-yet-mounted `[data-sui-glb]` container at or under `root`.
 * Call this right after injecting markup, and after the panel is made visible.
 * @param {HTMLElement|Document} [root]
 */
export function mountPending(root = document) {
    if (!root) return;
    const scope = root.nodeType === 1 && root.matches?.(`[${ATTR}]`) ? [root] : [];
    const found = root.querySelectorAll?.(`[${ATTR}]`) ?? [];
    for (const el of [...scope, ...found]) {
        const url = el.getAttribute(ATTR);
        if (url && !_mounts.has(el)) mountItemPreview(el, url);
    }
}

/**
 * Dispose any mounts at or under `node`. Call BEFORE replacing the node's markup, or the orphaned
 * canvas keeps its WebGL context and its rAF loop alive forever.
 * @param {HTMLElement|Document} node
 */
export function unmountItemPreview(node) {
    if (!node) return;
    for (const [el, handle] of [..._mounts]) {
        if (el === node || node.contains?.(el)) {
            try { handle.dispose(); } catch (e) { _mounts.delete(el); }
        }
    }
}
