// render.js — rendering layer.
//
// Sections:
//   1. Constants and palette
//   2. Material factories (lit/flat aware, r162 ColorManagement)
//   3. Lighting rig (ambient/hemi/sun/fill + sky dome + ground plane)
//   4. CameraRig (camera + OrbitControls + walk-mode look state)
//   5. WalkMode helpers (terrain snap + forward collision + DUNGEON floor snap)
//   6. safeDispose (recursive geometry/material/texture cleanup)
//   7. Viewport (renderer, scene assembly, animate loop, resize, input dispatch)

import * as THREE from 'three';
import { applyWorldLighting, syncWorldLighting } from './world-light.js';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { EffectComposer } from 'three/addons/postprocessing/EffectComposer.js';
import { RenderPass } from 'three/addons/postprocessing/RenderPass.js';
import { OutlinePass } from 'three/addons/postprocessing/OutlinePass.js';
import { OutputPass } from 'three/addons/postprocessing/OutputPass.js';
import { DepthPrepass } from './collision.js';

// ─────────────────────────────────────────────────────────────────────────────
// 1. Palette constants
// ─────────────────────────────────────────────────────────────────────────────

export const SKY_TOP = 0x2a4f8a;
export const SKY_HORIZON = 0xe8a840;
export const FOG_COLOR = 0xc49a50;

// ─────────────────────────────────────────────────────────────────────────────
// 2. Material factories
// ─────────────────────────────────────────────────────────────────────────────
//
// r162 note: MeshStandardMaterial responds to physical-light intensities.
// Earlier values tuned for r128 legacy lights look dim, so lighting in
// LightingRig is re-tuned. Materials here unchanged in structure.

let litMode = true;
let wireframeOn = false;
let maxAnisotropyVal = 1;

export function setLitMode(v) { litMode = !!v; }
export function isLitMode() { return litMode; }
export function setWireframe(v) { wireframeOn = !!v; }
export function isWireframe() { return wireframeOn; }
export function setMaxAnisotropy(v) { maxAnisotropyVal = v; }
export function maxAnisotropy() { return maxAnisotropyVal; }

// ═══════════════════════════════════════════════════════════════════════════
// TERRAIN DETAIL OVERLAY  (M4.2c)
//
// The server bakes ALL texture layers into ONE composite per tile — MCLY layer
// order, MCAL alpha maps, the lot — and stretches it 0..1 over a 533-yard tile.
// At the default pixelsPerChunk of 128 that is 2048 texels across 533 yards, or
// about 0.26 yards per texel. Underfoot you are magnifying a single texel across
// a quarter of a yard, which is the smeared, out-of-focus ground you can see.
//
// For contrast, MSUIClient does what the real client does: keeps the 4 layers
// separate and repeats each one 8 times per 33.3-yard chunk. That is roughly
// 61 texels per yard — about 16x the effective detail, and it is why its terrain
// looks sharp and this does not.
//
// THE REAL FIX IS RUNTIME SPLATTING: ship MTEX names + MCLY + MCAL to the
// browser and blend four repeating layers in a shader, instead of baking. That
// is a server-and-shader change of real size and it belongs in its own pass.
//
// What this is: a high-frequency detail map multiplied over the composite at a
// steep repeat rate. It does not add correct detail — it cannot, the per-layer
// information is already gone by the time the browser sees the tile — but it
// restores the high-frequency variation the human eye reads as "in focus" and
// removes most of the smear. It is a mitigation with an honest name.
//
// Procedural, so there is no asset to ship or 404.
// ═══════════════════════════════════════════════════════════════════════════

let detailEnabled = true;
let detailStrength = 0.45;
const DETAIL_REPEATS_PER_YARD = 0.35;   // ~2.9 yards per tile of detail
let _detailTex = null;
const _detailMaterials = new Set();

export function setTerrainDetail(on, strength) {
    detailEnabled = !!on;
    if (strength !== undefined) detailStrength = strength;
    for (const m of _detailMaterials) {
        if (m.userData._detailUniforms) {
            m.userData._detailUniforms.uDetailStrength.value =
                detailEnabled ? detailStrength : 0.0;
        }
    }
}
export function isTerrainDetailOn() { return detailEnabled; }

function detailTexture() {
    if (_detailTex) return _detailTex;
    const S = 128;
    const cv = document.createElement('canvas');
    cv.width = cv.height = S;
    const ctx = cv.getContext('2d');
    const img = ctx.createImageData(S, S);
    // Two octaves of value noise, wrapped so the tile edges meet.
    const grid = (n) => {
        const g = new Float32Array(n * n);
        for (let i = 0; i < n * n; i++) g[i] = Math.random();
        return g;
    };
    const g1 = grid(8), g2 = grid(32);
    const samp = (g, n, x, y) => {
        const fx = x * n, fy = y * n;
        const x0 = Math.floor(fx) % n, y0 = Math.floor(fy) % n;
        const x1 = (x0 + 1) % n, y1 = (y0 + 1) % n;
        const tx = fx - Math.floor(fx), ty = fy - Math.floor(fy);
        const sx = tx * tx * (3 - 2 * tx), sy = ty * ty * (3 - 2 * ty);
        const a = g[y0 * n + x0], b = g[y0 * n + x1];
        const c = g[y1 * n + x0], d = g[y1 * n + x1];
        return (a + (b - a) * sx) + ((c + (d - c) * sx) - (a + (b - a) * sx)) * sy;
    };
    for (let y = 0; y < S; y++) {
        for (let x = 0; x < S; x++) {
            const u = x / S, v = y / S;
            let n = samp(g1, 8, u, v) * 0.65 + samp(g2, 32, u, v) * 0.35;
            const c = Math.max(0, Math.min(255, Math.round(n * 255)));
            const i = (y * S + x) * 4;
            img.data[i] = img.data[i + 1] = img.data[i + 2] = c;
            img.data[i + 3] = 255;
        }
    }
    ctx.putImageData(img, 0, 0);
    _detailTex = new THREE.CanvasTexture(cv);
    _detailTex.wrapS = _detailTex.wrapT = THREE.RepeatWrapping;
    _detailTex.minFilter = THREE.LinearMipmapLinearFilter;
    _detailTex.magFilter = THREE.LinearFilter;
    _detailTex.generateMipmaps = true;
    _detailTex.anisotropy = maxAnisotropy();
    return _detailTex;
}

function applyDetailOverlay(mat) {
    mat.onBeforeCompile = (shader) => {
        shader.uniforms.uDetail = { value: detailTexture() };
        shader.uniforms.uDetailScale = { value: DETAIL_REPEATS_PER_YARD };
        shader.uniforms.uDetailStrength = {
            value: detailEnabled ? detailStrength : 0.0
        };
        mat.userData._detailUniforms = shader.uniforms;

        shader.vertexShader =
            'varying vec3 vDetailWorld;\n' +
            shader.vertexShader.replace(
                '#include <begin_vertex>',
                '#include <begin_vertex>\n' +
                '  vDetailWorld = (modelMatrix * vec4(transformed, 1.0)).xyz;');

        shader.fragmentShader =
            'uniform sampler2D uDetail;\n' +
            'uniform float uDetailScale;\n' +
            'uniform float uDetailStrength;\n' +
            'varying vec3 vDetailWorld;\n' +
            shader.fragmentShader.replace(
                '#include <map_fragment>',
                '#include <map_fragment>\n' +
                '  float dTex = texture2D(uDetail, vDetailWorld.xz * uDetailScale).r;\n' +
                '  diffuseColor.rgb *= mix(1.0, dTex * 1.6 + 0.2, uDetailStrength);');
    };
    // Materials with different onBeforeCompile output must not share a program.
    mat.customProgramCacheKey = () => 'terrain-detail-v1';
    _detailMaterials.add(mat);
}

export function makeTerrainMaterial(opts) {
    opts = opts || {};
    if (litMode) {
        const m = new THREE.MeshStandardMaterial({
            map: opts.map || null,
            color: opts.color || 0xffffff,
            side: THREE.FrontSide,
            roughness: 0.85,
            metalness: 0.0,
            fog: true,
            wireframe: wireframeOn
        });
        // Only worth doing on a textured tile — on the flat-colour fallback it
        // would just add noise to a solid green plane.
        if (opts.map) applyDetailOverlay(m);
        return m;
    }
    return new THREE.MeshBasicMaterial({
        map: opts.map || null,
        color: opts.color || 0xffffff,
        side: THREE.FrontSide,
        fog: true,
        wireframe: wireframeOn
    });
}

let _worldLightRig = null;
/** Set once by Viewport so material factories can reach the rig. */
export function setWorldLightRig(rig) { _worldLightRig = rig; }

export function makeDoodadMaterial(opts) {
    opts = opts || {};
    if (litMode) {
        return applyWorldLighting(new THREE.MeshStandardMaterial({
            map: opts.map || null,
            color: opts.color || 0x808080,
            side: opts.side || THREE.DoubleSide,
            alphaTest: opts.alphaTest || 0,
            transparent: opts.transparent || false,
            depthWrite: true,
            roughness: 0.7,
            metalness: 0.0,
            fog: true
        }), _worldLightRig);
    }
    return new THREE.MeshBasicMaterial({
        map: opts.map || null,
        color: opts.color || 0x808080,
        side: opts.side || THREE.DoubleSide,
        alphaTest: opts.alphaTest || 0,
        transparent: opts.transparent || false,
        depthWrite: true,
        fog: true
    });
}

export function makeWmoMaterial(opts) {
    opts = opts || {};
    if (litMode) {
        return applyWorldLighting(new THREE.MeshStandardMaterial({
            map: opts.map || null,
            color: opts.color || 0xaaaaaa,
            side: opts.side || THREE.FrontSide,
            alphaTest: opts.alphaTest || 0,
            transparent: opts.transparent || false,
            depthWrite: true,
            roughness: 0.5,
            metalness: 0.05,
            fog: true,
            wireframe: wireframeOn
        }), _worldLightRig);
    }
    return new THREE.MeshBasicMaterial({
        map: opts.map || null,
        color: opts.color || 0xaaaaaa,
        side: opts.side || THREE.FrontSide,
        alphaTest: opts.alphaTest || 0,
        transparent: opts.transparent || false,
        depthWrite: true,
        fog: true,
        wireframe: wireframeOn
    });
}

// ─────────────────────────────────────────────────────────────────────────────
// 3. Lighting rig + sky dome + ground plane
// ─────────────────────────────────────────────────────────────────────────────
//
// r162 physical lights are roughly 10× brighter for the same intensity number
// as r128 legacy lights — but only for MeshStandardMaterial. With our scene
// using MeshStandardMaterial (litMode) the original intensities looked dim.
// New values calibrated to preserve the warm-afternoon feel.

export class LightingRig {
    constructor(scene) {
        this.scene = scene;
        this.ambient = new THREE.AmbientLight(0xffe8c8, 0.9);
        scene.add(this.ambient);

        this.hemi = new THREE.HemisphereLight(0xeebb66, 0x4a5530, 0.8);
        scene.add(this.hemi);

        this.sun = new THREE.DirectionalLight(0xffbb55, 3.5);
        this.sun.position.set(-100, 28, 50);
        scene.add(this.sun);

        this.fill = new THREE.DirectionalLight(0x99bbdd, 0.4);
        this.fill.position.set(60, 60, -40);
        scene.add(this.fill);

        this._lit = true;
    }

    setLit(v) {
        this._lit = !!v;
        this.ambient.intensity = this._lit ? 0.9 : 1.4;
        this.hemi.visible = this._lit;
        this.sun.intensity = this._lit ? 3.5 : 1.8;
        this.fill.visible = this._lit;
    }

    isLit() { return this._lit; }
}

export function addSkyDome(scene) {
    const skyGeo = new THREE.SphereGeometry(1400, 32, 16, 0, Math.PI * 2, 0, Math.PI * 0.5);
    const skyMat = new THREE.ShaderMaterial({
        side: THREE.BackSide, depthWrite: false,
        uniforms: {
            topColor: { value: new THREE.Color(SKY_TOP) },
            horizonColor: { value: new THREE.Color(SKY_HORIZON) },
            offset: { value: 10 },
            exponent: { value: 0.4 }
        },
        vertexShader: `
            varying vec3 vWorldPosition;
            void main() {
                vec4 wp = modelMatrix * vec4(position, 1.0);
                vWorldPosition = wp.xyz;
                gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0);
            }`,
        fragmentShader: `
            uniform vec3 topColor;
            uniform vec3 horizonColor;
            uniform float offset;
            uniform float exponent;
            varying vec3 vWorldPosition;
            void main() {
                float h = normalize(vWorldPosition + offset).y;
                gl_FragColor = vec4(mix(horizonColor, topColor, max(pow(max(h, 0.0), exponent), 0.0)), 1.0);
            }`
    });
    const sky = new THREE.Mesh(skyGeo, skyMat);
    sky.renderOrder = -1;
    sky.name = 'sky';
    scene.add(sky);
    return sky;
}

export function addGroundPlane(scene) {
    const geo = new THREE.PlaneGeometry(8000, 8000);
    const mat = new THREE.MeshBasicMaterial({ color: FOG_COLOR, transparent: true, opacity: 0.5 });
    const ground = new THREE.Mesh(geo, mat);
    ground.rotation.x = -Math.PI / 2;
    ground.position.y = -5;
    ground.renderOrder = -0.5;
    ground.name = 'ground';
    scene.add(ground);
    return ground;
}

// ─────────────────────────────────────────────────────────────────────────────
// 4. CameraRig — camera + OrbitControls + walk-mode look state
// ─────────────────────────────────────────────────────────────────────────────
//
// Walk mode bypasses OrbitControls' rotation; instead we manage yaw/pitch
// directly via mouse delta. The state lives here so input handlers can
// mutate it without exporting globals.

// M1.1 — ground-probe reach, in yards. Scene Y is true world height, so the
// probe is anchored to the camera rather than to a fixed altitude.
const PROBE_ABOVE = 300;
const PROBE_BELOW = 700;

// Real 1.12 eye height. Was 2 in the old squashed space where it meant
// nothing in particular; it is now 2.1 actual yards (MSUIClient MovementConfig).
const EYE_HEIGHT = 2.1;

// M1.2 — ground-snap smoothing, expressed as a RATE (1/s) instead of a
// per-frame lerp factor. alpha = 1 - exp(-rate * dt) is the frame-rate
// independent form of the old `* 0.3` at 60fps:
//     0.3 = 1 - exp(-rate/60)  →  rate = -60 * ln(0.7) ≈ 21.4
const SNAP_RATE = 21.4;

// Terrain probe cadence. Was "every 3rd frame", which meant 20Hz at 60fps but
// 48Hz on a 144Hz display and 10Hz on a 30fps one — the probe got cheaper
// exactly when the machine was struggling. Now a real 20Hz.
const SNAP_HZ = 20;

export class CameraRig {
    constructor(canvas) {
        this.camera = new THREE.PerspectiveCamera(60, 1, 0.1, 2000);
        this.camera.position.set(0, 30, 80);

        this.controls = new OrbitControls(this.camera, canvas);
        this.controls.enableDamping = true;
        this.controls.dampingFactor = 0.1;
        this.controls.maxPolarAngle = Math.PI - 0.1;
        this.controls.minPolarAngle = 0.1;
        this.controls.minDistance = 1;
        this.controls.maxDistance = 5000;
        this.controls.enableZoom = false;
        // Free up right-click for walk-mode look — move pan to middle mouse.
        this.controls.mouseButtons = {
            LEFT: THREE.MOUSE.ROTATE,
            MIDDLE: THREE.MOUSE.PAN,
            RIGHT: null
        };

        this.walk = {
            mode: false,
            eyeHeight: EYE_HEIGHT,
            yaw: 0,
            pitch: 0,
            inited: false,
            rightMouseDown: false,
            lastMouseX: 0,
            lastMouseY: 0
        };
    }

    /**
     * M1.1 — place the camera against TRUE world height.
     *
     * Before 1:1 scale the scene was centred on Y=0 by construction, so a
     * hard-coded (0, 30, 80) always looked at something. Now terrain sits at
     * its real altitude (Elwynn ~50-200, Un'Goro ~-150, Blackrock Spire past
     * +400), and a fixed Y is underground or in orbit depending on the zone.
     *
     * groundY: measured terrain height under the target, when known.
     * fallbackY: the block's height range midpoint, used until a probe lands.
     */
    frameTerrain(groundY, opts) {
        opts = opts || {};
        const x = (opts.x !== undefined) ? opts.x : 0;
        const z = (opts.z !== undefined) ? opts.z : 0;
        const y = (groundY !== undefined && groundY !== null && isFinite(groundY))
            ? groundY : 0;

        if (this.walk.mode) {
            this.camera.position.set(x, y + this.walk.eyeHeight, z);
            this.applyWalkLook();
        } else {
            // Orbit: sit back and above, looking at the ground point.
            const back = (opts.distance !== undefined) ? opts.distance : 80;
            const up = (opts.elevation !== undefined) ? opts.elevation : 30;
            this.camera.position.set(x, y + up, z + back);
            this.controls.target.set(x, y, z);
            this.controls.update();
        }
    }

    /**
     * Measure terrain height at an XZ position by probing straight down.
     * Returns null when nothing is under the point (tile not streamed yet).
     */
    probeGroundY(meshes, x, z, fromY) {
        if (!meshes || meshes.length === 0) return null;
        const start = (fromY !== undefined && isFinite(fromY))
            ? fromY + PROBE_ABOVE
            : PROBE_ABOVE + PROBE_BELOW;
        const ray = new THREE.Raycaster(
            new THREE.Vector3(x, start, z), new THREE.Vector3(0, -1, 0));
        ray.far = PROBE_ABOVE + PROBE_BELOW * 2;
        const hits = ray.intersectObjects(meshes, false);
        return hits.length > 0 ? hits[0].point.y : null;
    }

    enterWalkMode() {
        this.walk.mode = true;
        this.walk.eyeHeight = EYE_HEIGHT;
        this.controls.enableRotate = false;
        this.controls.enablePan = false;
        const dir = new THREE.Vector3();
        this.camera.getWorldDirection(dir);
        this.walk.yaw = Math.atan2(dir.x, dir.z);
        this.walk.pitch = 0;
        this.walk.inited = true;
        this.applyWalkLook();
    }

    leaveWalkMode() {
        this.walk.mode = false;
        this.controls.enableRotate = true;
        this.controls.enablePan = true;
        this.walk.inited = false;
    }

    applyWalkLook() {
        const cy = Math.cos(this.walk.pitch);
        const lookDir = new THREE.Vector3(
            Math.sin(this.walk.yaw) * cy,
            Math.sin(this.walk.pitch),
            Math.cos(this.walk.yaw) * cy
        );
        this.controls.target.copy(this.camera.position).addScaledVector(lookDir, 10);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 5. WalkMode helpers
// ─────────────────────────────────────────────────────────────────────────────
//
// updateSnap(): cast ray downward, lift camera to eyeHeight above terrain.
//   DUNGEON MODE: raycast dungeonMeshes directly, multi-floor Y-snap.
// updateCollision(): cast forward from camera, push back if a WMO is closer
//   than COLLISION_DISTANCE. (Currently disabled — blocks cave entrances.)

const COLLISION_DISTANCE = 3;

export class WalkMode {
    constructor(editor) {
        this.editor = editor;
        this._down = new THREE.Vector3(0, -1, 0);
        this._rayDown = new THREE.Raycaster();
        this._rayFwd = new THREE.Raycaster();

        // ── PERF: throttle + cache ──────────────────────────────────────
        // WMO raycasts are expensive (InstancedMesh without per-instance BVH).
        // In dense areas like Stormwind, raycasting every WMO mesh every frame
        // tanks FPS. We throttle the full snap to ~20Hz and cache the WMO hit
        // result between frames.
        this._snapFrame = 0;
        this._cachedBestY = null;        // last computed best Y
        this._cachedTargetY = null;      // smoothed target Y for interpolation
        this._wmoMeshCache = null;       // cached wmoMeshList array
        this._wmoMeshCacheFrame = -999;  // frame when cache was built
    }

    updateSnap(dt) {
        if (!(dt > 0)) dt = 1 / 60;
        // Frame-rate independent approach to the target height.
        const snapAlpha = 1 - Math.exp(-SNAP_RATE * dt);
        const rig = this.editor.viewport.rig;
        if (!rig.walk.mode) return;
        if (!this.editor.tileGrid) return;

        this._snapFrame++;
        this._snapAccum = (this._snapAccum || 0) + dt;

        // ── DUNGEON MODE ─────────────────────────────────────────────────
        // Dungeon meshes have BVH → fast raycast. Run every frame, no throttle needed.
        if (this.editor.tileGrid.isDungeon) {
            const dungeonMeshes = this.editor.tileGrid.dungeonMeshes;
            if (!dungeonMeshes || dungeonMeshes.length === 0) return;

            // M1.1: scene Y is true world height (can exceed 500), so the probe
            // starts relative to the camera instead of at a fixed altitude.
            const origin = new THREE.Vector3(
                rig.camera.position.x, rig.camera.position.y + PROBE_ABOVE, rig.camera.position.z);
            this._rayDown.set(origin, this._down);
            this._rayDown.far = PROBE_ABOVE + PROBE_BELOW;

            const hits = this._rayDown.intersectObjects(dungeonMeshes, false);
            if (hits.length > 0) {
                let bestY = null;
                for (let i = 0; i < hits.length; i++) {
                    const hy = hits[i].point.y;
                    if (hy < rig.camera.position.y + 1) {
                        if (bestY === null || hy > bestY) bestY = hy;
                    }
                }
                if (bestY === null) bestY = hits[hits.length - 1].point.y;

                const targetY = bestY + rig.walk.eyeHeight;
                const dy = (targetY - rig.camera.position.y) * snapAlpha;
                rig.camera.position.y += dy;
                rig.controls.target.y += dy;
            }
            return;
        }

        // ── NORMAL TERRAIN MODE (with throttled WMO raycast) ─────────────

        // PERF: full raycast at a real SNAP_HZ regardless of frame rate.
        // Between samples, keep smoothing toward the last known target Y.
        const doFullRaycast = (this._snapAccum >= 1 / SNAP_HZ);
        if (doFullRaycast) this._snapAccum = 0;

        if (doFullRaycast) {
            const origin = new THREE.Vector3(
                rig.camera.position.x, rig.camera.position.y + PROBE_ABOVE, rig.camera.position.z);
            this._rayDown.set(origin, this._down);
            this._rayDown.far = PROBE_ABOVE + PROBE_BELOW;

            // Terrain raycast — BVH-accelerated, always fast
            const terrainMeshes = this.editor.tileGrid.terrainMeshes();
            let terrainY = null;
            if (terrainMeshes.length > 0) {
                const terrainHits = this._rayDown.intersectObjects(terrainMeshes);
                if (terrainHits.length > 0) terrainY = terrainHits[0].point.y;
            }

            // WMO raycast — expensive, use cached mesh list + spatial filter
            let wmoY = null;
            const stream = this.editor.objectStream;
            if (stream) {
                const wmoMeshes = this._getWmoMeshesNearby(stream, rig.camera.position);
                if (wmoMeshes.length > 0) {
                    const wmoHits = this._rayDown.intersectObjects(wmoMeshes, false);
                    if (wmoHits.length > 0) wmoY = wmoHits[0].point.y;
                }
            }

            // Decision logic (unchanged)
            let bestY;
            if (terrainY === null && wmoY !== null) {
                bestY = wmoY;
                this._inCave = true;
            } else if (terrainY !== null && this._inCave && wmoY !== null) {
                bestY = wmoY;
            } else if (terrainY !== null) {
                bestY = terrainY;
                this._inCave = false;
            } else {
                bestY = null;
            }

            this._cachedBestY = bestY;
        }

        // Apply smoothed Y snap (runs every frame for smooth movement)
        if (this._cachedBestY !== null) {
            const targetY = this._cachedBestY + rig.walk.eyeHeight;
            const dy = (targetY - rig.camera.position.y) * snapAlpha;
            rig.camera.position.y += dy;
            rig.controls.target.y += dy;
        }
    }

    /// PERF: return WMO meshes near the camera, using a cached list that
    /// refreshes every ~30 frames (~0.5s). Between refreshes, the same
    /// array is reused (no traverse/alloc). Also filters to meshes whose
    /// bounding sphere is within 80 units of the camera XZ.
    _getWmoMeshesNearby(stream, camPos) {
        // Rebuild full mesh list every ~30 frames
        if (!this._wmoMeshCache || (this._snapFrame - this._wmoMeshCacheFrame) > 30) {
            this._wmoMeshCache = stream.wmoMeshList();
            this._wmoMeshCacheFrame = this._snapFrame;
        }

        // Spatial filter: only test meshes near the camera
        const RADIUS_SQ = 80 * 80;
        const cx = camPos.x, cz = camPos.z;
        const nearby = [];
        for (let i = 0; i < this._wmoMeshCache.length; i++) {
            const m = this._wmoMeshCache[i];
            // InstancedMesh doesn't have a meaningful single position,
            // but its parent Group (wmoGroup) is at y=-0.5 with no XZ offset.
            // For instanced meshes, skip the spatial filter (they contain
            // instances scattered across the map — the GPU handles culling).
            // For regular Meshes (placement-store custom WMOs), check position.
            if (m.isInstancedMesh) {
                nearby.push(m);
            } else {
                const wp = m.getWorldPosition(new THREE.Vector3());
                const dx = wp.x - cx, dz = wp.z - cz;
                if (dx * dx + dz * dz < RADIUS_SQ) nearby.push(m);
            }
        }
        return nearby;
    }

    updateCollision() {
        // WMO forward collision disabled — it blocks cave/dungeon entrances.
        // TODO: re-enable with portal-aware logic that distinguishes between
        // solid exterior walls (push back) and enterable doorways (pass through).
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 6. safeDispose — recursive geometry/material/texture cleanup
// ─────────────────────────────────────────────────────────────────────────────
//
// Three.js does not auto-dispose geometry, materials, or textures when an
// object is removed from the scene. Call this whenever you remove() something
// you built yourself (i.e. not shared from a registry).

function disposeMaterial(mat) {
    if (!mat) return;
    if (mat.map) mat.map.dispose();
    if (mat.normalMap) mat.normalMap.dispose();
    if (mat.roughnessMap) mat.roughnessMap.dispose();
    if (mat.metalnessMap) mat.metalnessMap.dispose();
    if (mat.alphaMap) mat.alphaMap.dispose();
    mat.dispose();
}

export function safeDispose(root) {
    if (!root) return;
    root.traverse(function (c) {
        if (c.isMesh || c.isLine || c.isLineSegments || c.isPoints) {
            if (c.geometry) c.geometry.dispose();
            const mat = c.material;
            if (mat) {
                if (Array.isArray(mat)) mat.forEach(disposeMaterial);
                else disposeMaterial(mat);
            }
        }
        if (c.isInstancedMesh) {
            if (c.geometry) c.geometry.dispose();
            if (c.material) {
                if (Array.isArray(c.material)) c.material.forEach(disposeMaterial);
                else disposeMaterial(c.material);
            }
            if (typeof c.dispose === 'function') c.dispose();
        }
    });
}

// ─────────────────────────────────────────────────────────────────────────────
// 7. Viewport — renderer, scene assembly, animate loop, resize, input dispatch
// ─────────────────────────────────────────────────────────────────────────────
//
// Scene composition: sky dome → lighting rig → ground plane → tile grid
// (added later as it loads) → InstancePool's wmoGroup + doodadGroup
// (already added by ObjectStream.attachTo).

export class Viewport {
    constructor(editor, canvas) {
        this.editor = editor;
        this.canvas = canvas;
        editor.viewport = this;

        // Renderer
        this.renderer = new THREE.WebGLRenderer({
            canvas: canvas, antialias: true, alpha: true, powerPreference: 'high-performance'
        });
        this.renderer.setPixelRatio(window.devicePixelRatio);
        this.renderer.outputColorSpace = THREE.SRGBColorSpace;
        this.renderer.toneMapping = THREE.NoToneMapping;
        if ('useLegacyLights' in this.renderer) this.renderer.useLegacyLights = false;

        setMaxAnisotropy(this.renderer.capabilities.getMaxAnisotropy());

        // Scene + fog
        this.scene = editor.scene;
        this.scene.background = new THREE.Color(FOG_COLOR);
        this.scene.fog = new THREE.Fog(FOG_COLOR, 180, 550);

        // Lighting + sky + ground
        this.lighting = new LightingRig(this.scene);
        // Material factories are module-level functions with no viewport
        // reference; hand them the rig once so every lit material can join the
        // world lighting model.
        setWorldLightRig(this.lighting);
        addSkyDome(this.scene);
        this.ground = addGroundPlane(this.scene);

        // Camera + controls
        this.rig = new CameraRig(canvas);

        // ── Postprocessing (Phase 4) ────────────────────────────────────────
        // EffectComposer with a RenderPass → OutlinePass → OutputPass chain.
        // OutlinePass takes a Vector2 (size); renderer hasn't been sized yet
        // so we pass a placeholder and update via resize() below.
        this.composer = new EffectComposer(this.renderer);
        this.composer.addPass(new RenderPass(this.scene, this.rig.camera));

        const initSize = new THREE.Vector2(canvas.clientWidth || 1, canvas.clientHeight || 1);
        this.outlinePass = new OutlinePass(initSize, this.scene, this.rig.camera);
        this.outlinePass.edgeStrength = 4.0;
        this.outlinePass.edgeThickness = 1.5;
        this.outlinePass.edgeGlow = 0.4;
        this.outlinePass.visibleEdgeColor.set(0xffaa00);
        this.outlinePass.hiddenEdgeColor.set(0x553300);
        this.composer.addPass(this.outlinePass);

        this.composer.addPass(new OutputPass());

        // Resize
        this._preFsHeight = 0;
        window.addEventListener('resize', () => this.resize());
        this.resize();
        this._preFsHeight = this.canvas.clientHeight || (window.innerHeight - 130);

        // FPS counter
        this._fpsCounter = 0;
        this.currentFps = 0;
        setInterval(() => { this.currentFps = this._fpsCounter * 2; this._fpsCounter = 0; }, 500);

        // Input dispatch — every event passes through ToolManager.
        this._bindInputDispatch();

        // Periodic-task counters
        this._progressiveTimer = 0;
        this._streamTimer = 0;
        this._helperTimer = 0;

        // External per-frame callbacks (registered by index.js)
        this._tickers = [];

        // M1.2 — frame clock. Everything that moves is now expressed per
        // SECOND, not per frame; a 144Hz machine used to travel 2.4x further
        // per keypress than a 60Hz one.
        this._clock = new THREE.Clock();
        this.deltaTime = 1 / 60;

        // Phase 6: depth prepass for collision-aware ghost rendering. Must
        // be constructed AFTER this.ground exists (added to exclusions in
        // its ctor). Idle when no consumers registered, so zero cost
        // outside placement mode.
        this.depthPrepass = new DepthPrepass(this.editor);
        // Sync to current CSS-pixel size (matches OutlinePass convention).
        // Viewport.resize() already ran above, sizing the renderer; the
        // initial DepthPrepass placeholder size needs the same update.
        this.depthPrepass.setSize(this.canvas.clientWidth || 1, this.canvas.clientHeight || 1);

        // Kick off the loop.
        this._animate = this._animate.bind(this);
        this._animate();
    }

    addTicker(fn) { this._tickers.push(fn); }

    resize() {
        const parent = this.canvas.parentElement;
        let w, h;
        if (document.fullscreenElement) {
            w = window.innerWidth;
            h = window.innerHeight;
        } else {
            this.canvas.style.width = '';
            this.canvas.style.height = '';
            w = parent.clientWidth;
            h = this._preFsHeight > 0
                ? this._preFsHeight
                : Math.max(400, window.innerHeight - 130);
            if (h > window.innerHeight - 60) h = window.innerHeight - 60;
        }
        this.rig.camera.aspect = w / h;
        this.rig.camera.updateProjectionMatrix();
        this.renderer.setSize(w, h);
        if (this.composer) this.composer.setSize(w, h);
        if (this.outlinePass) this.outlinePass.setSize(w, h);
        if (this.depthPrepass) this.depthPrepass.setSize(w, h);
        if (this._placementCtx) this._placementCtx.setSize(w, h);
    }

    rememberPreFullscreen() {
        this._preFsHeight = this.canvas.clientHeight;
    }

    _bindInputDispatch() {
        const tools = this.editor.tools;

        this.canvas.addEventListener('pointerdown', (ev) => {
            if (tools.active && typeof tools.active.onPointerDown === 'function') {
                try {
                    const handled = tools.active.onPointerDown(ev, this._ctx());
                    if (handled) { ev.stopImmediatePropagation(); }
                } catch (err) { console.error('onPointerDown', err); }
            }
        }, true);

        this.canvas.addEventListener('pointermove', (ev) => {
            if (tools.active && typeof tools.active.onPointerMove === 'function') {
                try { tools.active.onPointerMove(ev, this._ctx()); }
                catch (err) { console.error('onPointerMove', err); }
            }
        });

        this.canvas.addEventListener('pointerup', (ev) => {
            if (tools.active && typeof tools.active.onPointerUp === 'function') {
                try { tools.active.onPointerUp(ev, this._ctx()); }
                catch (err) { console.error('onPointerUp', err); }
            }
        });

        this.canvas.addEventListener('contextmenu', (ev) => {
            const rig = this.rig;
            if (rig.walk.mode) { ev.preventDefault(); return; }
            if (tools.active && typeof tools.active.onContextMenu === 'function') {
                try { if (tools.active.onContextMenu(ev)) ev.preventDefault(); }
                catch (err) { console.error('onContextMenu', err); }
            }
        });

        this.canvas.addEventListener('wheel', (ev) => {
            if (tools.active && typeof tools.active.onWheel === 'function') {
                try { tools.active.onWheel(ev, this._ctx()); }
                catch (err) { console.error('onWheel', err); }
            }
        }, { capture: true, passive: false });
    }

    _ctx() {
        return {
            camera: this.rig.camera,
            controls: this.rig.controls,
            scene: this.scene
        };
    }

    _animate() {
        // M1.2 — clamp dt so a backgrounded tab (rAF stops, then fires with a
        // multi-second delta) cannot teleport the camera across the map on
        // return. 100ms = 10fps floor; below that, motion just slows down.
        const dt = Math.min(this._clock.getDelta(), 0.1);
        this.deltaTime = dt;

        requestAnimationFrame(this._animate);
        this._fpsCounter++;

        // In walk mode, the controls.target is being driven manually via
        // yaw/pitch — bypass OrbitControls' damping/rotation entirely.
        if (this.rig.walk.mode) {
            this.rig.camera.lookAt(this.rig.controls.target);
        } else {
            this.rig.controls.update();
        }

        // Walk-mode hooks (terrain snap + WMO collision)
        if (this.editor.walkModeImpl) {
            this.editor.walkModeImpl.updateSnap(dt);
            this.editor.walkModeImpl.updateCollision();
        }

        // External tickers (movement, compass, hud, etc.)
        for (let i = 0; i < this._tickers.length; i++) {
            try { this._tickers[i](this, dt); } catch (err) { console.error('ticker', err); }
        }

        // Progressive terrain check (~500ms)
        this._progressiveTimer++;
        if (this._progressiveTimer >= 30 && this.editor.tileGrid && this.editor.currentPreset) {
            this._progressiveTimer = 0;
            this.editor.tileGrid.checkProgressive(this.rig.controls.target);
            // Splat materials are ShaderMaterials, so three.js does not update
            // their fog/light uniforms for us.
            if (this.editor.tileGrid.syncSplat) this.editor.tileGrid.syncSplat();
            syncWorldLighting(this.lighting);
            // Slide ground plane to follow the camera so its corners stay
            // hidden behind the fog horizon.
            if (this.ground) {
                this.ground.position.x = this.rig.controls.target.x;
                this.ground.position.z = this.rig.controls.target.z;
            }
        }

        // Object streaming check (~600ms, offset from terrain to spread load)
        this._streamTimer++;
        if (this._streamTimer >= 36 && this.editor.objectStream && this.editor.currentPreset && this.editor.tileGrid
            && this.editor.tileGrid.tileWidthMesh > 0) {
            this._streamTimer = 0;
            this.editor.objectStream.pump(
                this.rig.camera.position.x,
                this.rig.camera.position.z,
                this.editor.tileGrid.globalMidHeight,
                this.editor.tileGrid.globalHeightScale
            );
        }

        // Tool helper update (~10Hz, cheap DOM positioning)
        this._helperTimer++;
        if (this._helperTimer >= 6) {
            this._helperTimer = 0;
            const active = this.editor.tools.active;
            if (active && typeof active.updateHelpers === 'function') {
                try { active.updateHelpers(); } catch (err) { console.error('updateHelpers', err); }
            }
        }

        // M4.2 — character mode. The controller owns the character's position
        // AND the camera, so it must run after the walk-mode snap (which it
        // disables anyway) and before the render.
        const cc = this.editor.characterController;
        if (cc && cc.enabled) {
            try { cc.update(dt); } catch (err) { console.error('character', err); }
        }

        // Phase 6: depth prepass for ghost collision viz. No-op when no
        // consumers are registered (i.e. outside placement mode).
        if (this.depthPrepass) this.depthPrepass.runIfNeeded();

        // Placement context: ghost depth pass + collision overlay. No-op
        // when no ghost is active. Must run AFTER the scene depth prepass
        // (so tSceneDepth is fresh) and BEFORE composer.render (so the
        // overlay pass has valid ghost depth to sample).
        if (this._placementCtx) this._placementCtx.runIfNeeded();

        // ── PERF: bypass EffectComposer in walk mode ────────────────────
        const needOutline = this.outlinePass &&
            this.outlinePass.selectedObjects &&
            this.outlinePass.selectedObjects.length > 0;
        const hasPlacementCtx = this._placementCtx && this._placementCtx._passInserted;

        // Reset render info BEFORE rendering so we capture accurate stats
        this.renderer.info.reset();

        if (!needOutline && !hasPlacementCtx && this.rig.walk.mode) {
            this.renderer.render(this.scene, this.rig.camera);
        } else {
            // When using composer, capture scene render stats by doing a manual
            // info.reset before the composer runs. The RenderPass (first pass)
            // does the real scene render; subsequent passes add their own calls.
            this.composer.render();
        }

        // ── PERF: diagnostic log (every 5s) ─────────────────────────
        if (this._fpsCounter % 300 === 1) {
            const info = this.renderer.info;
            const walk = this.rig.walk.mode ? 'WALK' : 'orbit';
            const bypass = (!needOutline && !hasPlacementCtx && this.rig.walk.mode) ? 'BYPASS' : 'COMPOSER';
            let sceneChildCount = 0;
            this.scene.traverse(() => { sceneChildCount++; });
            const tg = this.editor.tileGrid;
            const tileCount = tg ? Object.keys(tg.tiles).length : 0;
            const os = this.editor.objectStream;
            const activeP = os ? Object.keys(os.activePlacements).length : 0;
            const imSets = os ? Object.keys(os.pool.sets).length : 0;
            console.log(`[render] ${walk} ${bypass} calls=${info.render.calls} tris=${info.render.triangles} | scene=${sceneChildCount} tiles=${tileCount} placements=${activeP} imSets=${imSets} | tex=${info.memory.textures} geo=${info.memory.geometries} | fps=${this.currentFps}`);
        }
    }
}