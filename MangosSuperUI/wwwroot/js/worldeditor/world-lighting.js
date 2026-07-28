// world-lighting.js — vanilla's authored exterior lighting, resolved live.
//
// ═══════════════════════════════════════════════════════════════════════════
// PORTED FROM MSUIClient
//   World/ExteriorLighting.cs   the resolve: falloff blending across zones
//   World/WorldAtmosphere.cs    the evaluate: what the renderer consumes
//   World/SkyRenderer.cs        five authored bands, screen space, no dome
//   Shaders/sky.frag            the band blend by view elevation
//   Formats/DbcReader.cs        LightBand / LightColorBand sampling
//   SYSTEM_EXTERIOR_LIGHTING.md why each of those is what it is
// ═══════════════════════════════════════════════════════════════════════════
//
// THE BAR
//   Vanilla ships the real answers: Light.dbc says which lighting setup applies
//   where, LightIntBand says what colour every part of the sky is at every
//   minute of the day, LightFloatBand says how far fog reaches. The bar is to
//   follow that, and the reason it is worth the effort is that the yardstick is
//   A NUMBER IN A FILE. "Is the ambient right" stops being an opinion.
//
//   MSUIClient records what happens without one. A tuning pass rejected a
//   blue-biased ambient of (0.42, 0.50, 0.60) as "what made the world look
//   cool" and replaced it with a warm invention. The authored value at Azeroth
//   noon is (0.408, 0.510, 0.604). The tune was fighting the data almost
//   exactly, and had no way to know.
//
// WHAT IS DATA AND WHAT IS STILL OURS — stated, because the difference matters:
//
//   data   ambient colour (band 1), diffuse/sun colour (band 0), fog colour
//          (band 7), fog start and end (float bands 0 and 1), the five sky
//          colours (bands 2-6)
//   ours   the sky band HEIGHTS (LightIntBand gives five colours and never says
//          what elevation each sits at — 0.45/0.18/0.06 are MSUIClient's own
//          guess and are still unverified against a real-client capture)
//   ours   the sun DIRECTION (Light.dbc carries no sun position; six is
//          sunrise, twelve noon, eighteen sunset). Inventing this is honest
//          where inventing a colour was not.
//   ours   sun and ambient STRENGTH — multipliers on the authored values, where
//          1.0 means "use the data exactly". That is the correctness check.
//
// ONE LIGHTING MODEL, STILL
//   This does not add a second lighting path. It drives the SAME LightingRig
//   that terrain-splat.js, world-light.js and foliage.js already read through
//   worldSunIntensity / worldAmbientIntensity / worldSunDirection, so terrain,
//   buildings, doodads, the character and the grass all move together. Turning
//   it off restores the rig exactly as it was.

import * as THREE from 'three';
import { getJSON } from './net.js';

const GRID = 533.333;
const TILES = 32;

// Sky band stops as a view elevation, 0 horizon .. 1 zenith. OURS, not data.
export const SKY_STOPS = { middle: 0.45, band1: 0.18, band2: 0.06 };

// Band indices, so a call site reads as intent rather than as a magic number.
const B_DIFFUSE = 0, B_AMBIENT = 1;
const B_SKY_TOP = 2, B_SKY_MIDDLE = 3, B_SKY_BAND1 = 4, B_SKY_BAND2 = 5, B_SKY_SMOG = 6;
const B_FOG = 7;
const F_FOG_END = 0, F_FOG_START_MULT = 1;

// ─────────────────────────────────────────────────────────────────────────────
// Band sampling
// ─────────────────────────────────────────────────────────────────────────────
//
// Two samplers, never one. Sharing them is a real bug with a real symptom:
// interpolating a PACKED 0x00RRGGBB as a single number carries across the byte
// boundaries and lands on a colour belonging to neither key. MSUIClient's
// symptom at 11:11 was green ambient, cyan fog and a dark-purple sun while every
// scalar band in the same rows read perfectly — and that asymmetry was the
// diagnosis. Colours decode BOTH bracketing keys, then interpolate per channel.
//
// Both wrap: the segment from the last key to the first crosses midnight. A band
// that does not wrap snaps hard at 00:00 and reads as a rendering glitch.

function wrapHours(h) {
    h %= 24;
    return h < 0 ? h + 24 : h;
}

/** Bracketing keys and the blend factor between them, wrapping across midnight. */
function bracket(times, hours) {
    const n = times.length;
    if (hours < times[0] || hours >= times[n - 1]) {
        const from = times[n - 1];
        const to = times[0] + 24;
        const at = hours < times[0] ? hours + 24 : hours;
        const span = to - from;
        return { i0: n - 1, i1: 0, t: span <= 0 ? 0 : (at - from) / span };
    }
    let i0 = 0;
    for (let i = 0; i < n - 1; i++) {
        if (hours >= times[i] && hours < times[i + 1]) { i0 = i; break; }
        i0 = i + 1;
    }
    const i1 = Math.min(i0 + 1, n - 1);
    const span = times[i1] - times[i0];
    return { i0, i1, t: span <= 0 ? 0 : (hours - times[i0]) / span };
}

function sampleScalar(band, hours) {
    if (!band || !band.t || band.t.length === 0) return 0;
    if (band.t.length === 1) return band.v[0];
    const b = bracket(band.t, wrapHours(hours));
    return band.v[b.i0] + (band.v[b.i1] - band.v[b.i0]) * Math.min(1, Math.max(0, b.t));
}

/** Packed 0x00RRGGBB to 0..1 RGB, written into out. */
function decode(packed, out) {
    out[0] = ((packed >>> 16) & 0xff) / 255;
    out[1] = ((packed >>> 8) & 0xff) / 255;
    out[2] = (packed & 0xff) / 255;
    return out;
}

const _ca = [0, 0, 0], _cb = [0, 0, 0];

function sampleColor(band, hours, out) {
    out[0] = out[1] = out[2] = 0;
    if (!band || !band.t || band.t.length === 0) return out;
    if (band.t.length === 1) return decode(band.v[0], out);

    const b = bracket(band.t, wrapHours(hours));
    decode(band.v[b.i0], _ca);
    decode(band.v[b.i1], _cb);
    const t = Math.min(1, Math.max(0, b.t));
    out[0] = _ca[0] + (_cb[0] - _ca[0]) * t;
    out[1] = _ca[1] + (_cb[1] - _ca[1]) * t;
    out[2] = _ca[2] + (_cb[2] - _ca[2]) * t;
    return out;
}

/**
 * 1 inside the inner radius, 0 outside the outer, linear between.
 *
 * Never nearest-wins. Snapping to the nearest zone pops at every zone edge —
 * the same class of defect as rebuilding placements at a tile boundary. And a
 * zone with falloffEnd 0 that is NOT the map default has no reach at all;
 * treating it as infinite would let one stray row repaint the whole map.
 */
function falloffWeight(distance, start, end) {
    if (end <= 0) return 0;
    if (distance <= start) return 1;
    if (distance >= end) return 0;
    const span = end - start;
    return span <= 0 ? 1 : 1 - (distance - start) / span;
}

// ─────────────────────────────────────────────────────────────────────────────
// The sky — five authored bands, screen space
// ─────────────────────────────────────────────────────────────────────────────
//
// No dome. The sky is a function of view DIRECTION, not of position, so a
// screen-space pass is exact at any FOV and any orientation, with no geometry to
// build, cull or get wrong at the poles — and, unlike the 1400-yard hemisphere
// the editor shipped with, nothing to walk out of or clip against the far plane.
//
// The scene keeps its fog-coloured background underneath. That is deliberate:
// switching the sky off restores exactly the old flat behaviour rather than
// exposing a hard far-clip edge.

const SKY_VERT = /* glsl */`
varying vec2 vNdc;
void main() {
    vNdc = position.xy;
    gl_Position = vec4(position.xy, 1.0, 1.0);
}
`;

const SKY_FRAG = /* glsl */`
varying vec2 vNdc;

uniform vec3  uForward;
uniform vec3  uRight;
uniform vec3  uUp;
uniform float uTanHalfFov;
uniform float uAspect;

uniform vec3 uSkyTop;
uniform vec3 uSkyMiddle;
uniform vec3 uSkyBand1;
uniform vec3 uSkyBand2;
uniform vec3 uSkySmog;

uniform float uStopMiddle;
uniform float uStopBand1;
uniform float uStopBand2;

// Guards a divide when two stops are dragged onto each other.
float safeSpan(float a, float b) { return max(a - b, 1e-4); }

void main() {
    vec3 dir = normalize(
        uForward
      + uRight * (vNdc.x * uTanHalfFov * uAspect)
      + uUp    * (vNdc.y * uTanHalfFov));

    // MSUIClient is Z-up and reads dir.z here; three.js is Y-up. That is the
    // ONLY difference between this file and Shaders/sky.frag.
    float e = clamp(dir.y, -1.0, 1.0);

    vec3 c;
    if (e >= uStopMiddle)
        c = mix(uSkyMiddle, uSkyTop, (e - uStopMiddle) / safeSpan(1.0, uStopMiddle));
    else if (e >= uStopBand1)
        c = mix(uSkyBand1, uSkyMiddle, (e - uStopBand1) / safeSpan(uStopMiddle, uStopBand1));
    else if (e >= uStopBand2)
        c = mix(uSkyBand2, uSkyBand1, (e - uStopBand2) / safeSpan(uStopBand1, uStopBand2));
    else if (e >= 0.0)
        c = mix(uSkySmog, uSkyBand2, e / max(uStopBand2, 1e-4));
    else
        // Below the horizon the smog colour continues. Terrain covers this
        // almost everywhere; it shows through on a cliff edge and must not be
        // black, or the world ends in a hard line — the exact failure the flat
        // clear colour was originally chosen to avoid.
        c = uSkySmog;

    gl_FragColor = vec4(c, 1.0);
}
`;

class WorldSky {
    constructor(scene) {
        const geo = new THREE.BufferGeometry();
        // One oversized triangle covering the clip cube. Cheaper than a quad and
        // free of the seam a two-triangle quad puts down the diagonal.
        geo.setAttribute('position', new THREE.Float32BufferAttribute(
            [-1, -1, 0, 3, -1, 0, -1, 3, 0], 3));

        this.material = new THREE.ShaderMaterial({
            vertexShader: SKY_VERT,
            fragmentShader: SKY_FRAG,
            depthTest: false,
            depthWrite: false,
            fog: false,
            side: THREE.DoubleSide,
            uniforms: {
                uForward: { value: new THREE.Vector3(0, 0, -1) },
                uRight: { value: new THREE.Vector3(1, 0, 0) },
                uUp: { value: new THREE.Vector3(0, 1, 0) },
                uTanHalfFov: { value: Math.tan(30 * Math.PI / 180) },
                uAspect: { value: 1 },
                uSkyTop: { value: new THREE.Color(0x2a4f8a) },
                uSkyMiddle: { value: new THREE.Color(0x2a4f8a) },
                uSkyBand1: { value: new THREE.Color(0xe8a840) },
                uSkyBand2: { value: new THREE.Color(0xe8a840) },
                uSkySmog: { value: new THREE.Color(0xc49a50) },
                uStopMiddle: { value: SKY_STOPS.middle },
                uStopBand1: { value: SKY_STOPS.band1 },
                uStopBand2: { value: SKY_STOPS.band2 },
            },
        });

        this.mesh = new THREE.Mesh(geo, this.material);
        this.mesh.name = 'authoredSky';
        this.mesh.frustumCulled = false;
        this.mesh.matrixAutoUpdate = false;
        this.mesh.renderOrder = -10000;   // before everything, writes no depth
        this.mesh.visible = false;
        scene.add(this.mesh);
    }

    setVisible(v) { this.mesh.visible = !!v; }

    /**
     * Camera basis, built here rather than inverting a matrix in the shader.
     * matrixWorld's columns are the camera's right / up / back axes, so forward
     * is the negated third column — one less place for a transpose convention
     * to go wrong.
     */
    updateCamera(camera) {
        const e = camera.matrixWorld.elements;
        const u = this.material.uniforms;
        u.uRight.value.set(e[0], e[1], e[2]).normalize();
        u.uUp.value.set(e[4], e[5], e[6]).normalize();
        u.uForward.value.set(-e[8], -e[9], -e[10]).normalize();
        u.uTanHalfFov.value = Math.tan(camera.fov * Math.PI / 360);
        u.uAspect.value = camera.aspect;
    }

    setStops(middle, band1, band2) {
        // Kept ordered, so dragging one stop past another cannot invert a band
        // and produce a stripe nobody can explain.
        const b2 = Math.min(0.9, Math.max(0.001, band2));
        const b1 = Math.min(0.95, Math.max(b2 + 0.001, band1));
        const m = Math.min(0.99, Math.max(b1 + 0.001, middle));
        this.material.uniforms.uStopBand2.value = b2;
        this.material.uniforms.uStopBand1.value = b1;
        this.material.uniforms.uStopMiddle.value = m;
    }

    dispose() {
        if (this.mesh.parent) this.mesh.parent.remove(this.mesh);
        this.mesh.geometry.dispose();
        this.material.dispose();
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// WorldLighting
// ─────────────────────────────────────────────────────────────────────────────

export class WorldLighting {
    constructor(editor) {
        this.editor = editor;

        /**
         * The A/B switch for this whole system — MSUIClient calls it
         * WorldAtmosphere.UseAuthoredData. Off restores the rig exactly.
         *
         * MSUIClient shipped a version where this was accidentally the DevTools
         * flag, so in a normal build the resolve never ran and every colour fell
         * back to the invented constants the system exists to replace. It is
         * recorded there as a seam violation, inverted: core decides, the dev
         * layer observes. Keep this a user setting and nothing else.
         */
        this.enabled = false;

        this.timeHours = 12;
        /** Game-hours per real second. 0 = frozen. Vanilla runs at real time. */
        this.cycleSpeed = 0;

        /**
         * 1.0 means "use the data exactly" — the correctness check. The authored
         * Azeroth noon reads dark on a modern display (NoToneMapping, exposure 1),
         * so midday looked underlit. These lift it to a bright noon while keeping
         * the authored HUE. Tunable live via the Light.dbc panel sliders or
         * `window.we.lighting.sunStrength` / `.ambientStrength` — the ambient
         * carries more of the lift because the shadowed/ambient-lit areas are what
         * read as "not bright enough"; the sun is nudged less to avoid blowout.
         */
        this.sunStrength = 1.3;
        this.ambientStrength = 1.55;

        this.skyEnabled = true;
        this.stops = Object.assign({}, SKY_STOPS);

        this.data = null;          // the map payload
        this.state = 'idle';       // idle | loading | ready | none
        this.mapId = null;
        this.notes = [];

        this.sample = {
            colors: new Array(18),
            floats: new Array(6),
            contributors: [],
            hasData: false,
        };
        for (let i = 0; i < 18; i++) this.sample.colors[i] = [0, 0, 0];
        for (let i = 0; i < 6; i++) this.sample.floats[i] = 0;
        this._scratchColors = new Array(18);
        for (let i = 0; i < 18; i++) this._scratchColors[i] = [0, 0, 0];
        this._scratchFloats = new Array(6).fill(0);

        this._sky = new WorldSky(editor.viewport.scene);
        this._saved = null;
        this._loggedOnce = false;
        this._sunDir = new THREE.Vector3(0, 1, 0);

        editor.signals.presetClearing.add(() => {
            this.data = null; this.state = 'idle'; this.mapId = null;
        });
    }

    // ── the switch ───────────────────────────────────────────────────────────

    setEnabled(v) {
        v = !!v;
        if (v === this.enabled) return;
        this.enabled = v;
        if (v) this._save();
        else { this._restore(); this._sky.setVisible(false); }
    }

    setSkyEnabled(v) {
        this.skyEnabled = !!v;
        this._sky.setVisible(this.enabled && this.skyEnabled && this.sample.hasData);
    }

    setStops(middle, band1, band2) {
        this.stops.middle = middle;
        this.stops.band1 = band1;
        this.stops.band2 = band2;
    }

    /** Snapshot the rig so switching off is exact rather than approximate. */
    _save() {
        if (this._saved) return;
        const vp = this.editor.viewport;
        const rig = vp.lighting, fog = vp.scene.fog;
        this._saved = {
            sunColor: rig.sun.color.clone(),
            sunIntensity: rig.sun.intensity,
            sunPos: rig.sun.position.clone(),
            ambColor: rig.ambient.color.clone(),
            ambIntensity: rig.ambient.intensity,
            hemiColor: rig.hemi.color.clone(),
            hemiIntensity: rig.hemi.intensity,
            fogColor: fog ? fog.color.clone() : null,
            fogNear: fog ? fog.near : 0,
            fogFar: fog ? fog.far : 0,
            background: vp.scene.background ? vp.scene.background.clone() : null,
            domeVisible: this._dome() ? this._dome().visible : true,
        };
    }

    _restore() {
        const s = this._saved;
        if (!s) return;
        const vp = this.editor.viewport;
        const rig = vp.lighting, fog = vp.scene.fog;
        rig.sun.color.copy(s.sunColor);
        rig.sun.intensity = s.sunIntensity;
        rig.sun.position.copy(s.sunPos);
        rig.ambient.color.copy(s.ambColor);
        rig.ambient.intensity = s.ambIntensity;
        rig.hemi.color.copy(s.hemiColor);
        rig.hemi.intensity = s.hemiIntensity;
        if (fog && s.fogColor) {
            fog.color.copy(s.fogColor);
            fog.near = s.fogNear;
            fog.far = s.fogFar;
        }
        if (vp.scene.background && s.background) vp.scene.background.copy(s.background);
        const dome = this._dome();
        if (dome) dome.visible = s.domeVisible;
        this._saved = null;
    }

    _dome() { return this.editor.viewport.scene.getObjectByName('sky'); }

    // ── frame ────────────────────────────────────────────────────────────────

    tick(viewport, dt) {
        if (this.cycleSpeed !== 0) this.timeHours = wrapHours(this.timeHours + dt * this.cycleSpeed);
        if (!this.enabled) return;

        const tg = this.editor.tileGrid;
        if (!tg || tg.isDungeon || !tg.tileWidthMesh) return;

        if (this.state === 'idle' || this.mapId !== tg.mapId) this._load(tg.mapId);
        if (this.state !== 'ready') return;

        const pos = viewport.rig.camera.position;
        const w = this.worldFromMesh(tg, pos);
        this.resolve(w.x, w.y, w.z, this.timeHours);
        this.apply(viewport);
    }

    /**
     * Three.js scene coords -> WoW world coords, the same transform the HUD's
     * .gps readout uses (ui.js Compass.tick). Written out rather than imported
     * so the two can be diffed by eye:
     *
     *   modfPosX = (meshX / tileWidth + 0.5 + centerGridY) * 533.333
     *   modfPosZ = (meshZ / tileWidth + 0.5 + centerGridX) * 533.333
     *   wowX = 32*533.333 - modfPosZ      (axis swap)
     *   wowY = 32*533.333 - modfPosX      (axis swap)
     */
    worldFromMesh(tg, p) {
        const modfPosX = (p.x / tg.tileWidthMesh + 0.5 + tg.centerGridY) * GRID;
        const modfPosZ = (p.z / tg.tileWidthMesh + 0.5 + tg.centerGridX) * GRID;
        return { x: TILES * GRID - modfPosZ, y: TILES * GRID - modfPosX, z: p.y };
    }

    _load(mapId) {
        const preset = this.editor.currentPreset;
        if (!preset) return;
        this.state = 'loading';
        this.mapId = mapId;

        getJSON('/WorldEditor/Lighting?preset=' + encodeURIComponent(preset))
            .then((r) => {
                this.notes = (r && r.notes) || [];
                if (!r || !r.success) {
                    this.state = 'none';
                    console.info('[light] unavailable:', (r && r.error) || 'no response',
                        this.notes.join(' | '));
                    return;
                }
                this.data = r;
                this.state = 'ready';
                // The convention self-test is the one note that must never be
                // scrolled past — a wrong convention puts every zone light in
                // the wrong place and nothing else will say so.
                for (const n of this.notes) {
                    if (/FAILED/i.test(n)) console.error('[light]', n);
                    else console.info('[light]', n);
                }
            })
            .catch((err) => {
                this.state = 'none';
                console.warn('[light] fetch failed:', err && err.message);
            });
    }

    // ── resolve ──────────────────────────────────────────────────────────────

    /**
     * Blend the authored lighting at a world position and time.
     *
     * The map-wide default is the base; each zone is lerped on top of it by its
     * own falloff weight, FARTHEST FIRST so the nearest zone lands last and
     * dominates. Zone lights are small — measured reaches near Northshire are
     * 495, 250, 90, 85 and 76 yards — so in open country the honest answer is
     * "only the map default applies", and that is correct rather than a miss.
     */
    resolve(wx, wy, wz, hours) {
        const s = this.sample;
        s.hasData = false;
        s.contributors.length = 0;
        if (!this.data) return s;

        const zones = this.data.zones || [];
        const params = this.data.lightParams || {};

        let base = null;
        for (const z of zones) if (z.isDefault) { base = z; break; }

        if (base) {
            this._readInto(s.colors, s.floats, params[base.paramsId], hours);
            s.hasData = true;
            s.contributors.push({ id: base.id, paramsId: base.paramsId, isDefault: true, distance: 0, weight: 1 });
        } else {
            for (let i = 0; i < 18; i++) { s.colors[i][0] = s.colors[i][1] = s.colors[i][2] = 0; }
            for (let i = 0; i < 6; i++) s.floats[i] = 0;
        }

        const scored = [];
        for (const z of zones) {
            if (z.isDefault) continue;
            const dx = wx - z.x, dy = wy - z.y, dz = wz - z.z;
            const d = Math.sqrt(dx * dx + dy * dy + dz * dz);
            const w = falloffWeight(d, z.start, z.end);
            if (w <= 0) continue;
            scored.push({ z, d, w });
        }
        scored.sort((a, b) => b.d - a.d);

        for (const { z, d, w } of scored) {
            this._readInto(this._scratchColors, this._scratchFloats, params[z.paramsId], hours);
            for (let i = 0; i < 18; i++) {
                const a = s.colors[i], b = this._scratchColors[i];
                a[0] += (b[0] - a[0]) * w;
                a[1] += (b[1] - a[1]) * w;
                a[2] += (b[2] - a[2]) * w;
            }
            for (let i = 0; i < 6; i++) s.floats[i] += (this._scratchFloats[i] - s.floats[i]) * w;
            s.hasData = true;
            s.contributors.push({ id: z.id, paramsId: z.paramsId, isDefault: false, distance: d, weight: w });
        }
        return s;
    }

    _readInto(colors, floats, entry, hours) {
        if (!entry) {
            for (let i = 0; i < 18; i++) { colors[i][0] = colors[i][1] = colors[i][2] = 0; }
            for (let i = 0; i < 6; i++) floats[i] = 0;
            return;
        }
        for (let i = 0; i < 18; i++) sampleColor(entry.colours[i], hours, colors[i]);
        for (let i = 0; i < 6; i++) floats[i] = sampleScalar(entry.floats[i], hours);
    }

    /** Yards. Float band 0, already un-scaled from the stored x36 by the server. */
    get fogEnd() { return this.sample.floats[F_FOG_END]; }

    /**
     * Yards, DERIVED. The data stores a 0..0.999 MULTIPLIER rather than a second
     * distance, so the authored relationship between the two is kept rather than
     * flattened into two independent knobs. Azeroth at noon: 500 x 0.25 = 125.
     */
    get fogStart() { return this.fogEnd * this.sample.floats[F_FOG_START_MULT]; }

    /**
     * Six is sunrise, twelve solar noon, eighteen sunset — the one thing here
     * still ours, because Light.dbc carries no sun position.
     *
     * MSUIClient computes it in WoW space (X north, Y west, Z up):
     *   (cos(phase)*0.72, sin(phase)*0.42, sin(phase))
     * Our scene X is -wowY and our scene Z is -wowX, with scene Y = wowZ, so the
     * same vector becomes (-wowY, wowZ, -wowX).
     */
    sunDirection(out) {
        const phase = (wrapHours(this.timeHours) - 6) / 24 * Math.PI * 2;
        const v = out || this._sunDir;
        return v.set(
            -Math.sin(phase) * 0.42,
            Math.sin(phase),
            -Math.cos(phase) * 0.72
        ).normalize();
    }

    // ── apply ────────────────────────────────────────────────────────────────

    apply(viewport) {
        const s = this.sample;
        if (!s.hasData) { this._sky.setVisible(false); return; }

        const rig = viewport.lighting;
        const scene = viewport.scene;
        const camera = viewport.rig.camera;

        const d = s.colors[B_DIFFUSE], a = s.colors[B_AMBIENT], f = s.colors[B_FOG];

        // The DBC stores sRGB byte triplets. `new THREE.Color(0xffbb55)` — what
        // the rig was built with — converts sRGB to the working space, so these
        // must be told they are sRGB too or the whole world shifts.
        rig.sun.color.setRGB(d[0], d[1], d[2], THREE.SRGBColorSpace);
        rig.ambient.color.setRGB(a[0], a[1], a[2], THREE.SRGBColorSpace);
        rig.hemi.color.copy(rig.ambient.color);

        // Intensities are set so that terrain-splat.js's worldSunIntensity and
        // worldAmbientIntensity — the ONE place the whole scene's light level is
        // decided — come out as exactly the strength multipliers. At 1.0 the
        // data is used exactly, and that is what makes the check exact:
        //     worldSunIntensity     = sun.intensity / 3.5
        //     worldAmbientIntensity = (ambient.intensity + hemi.intensity) / 2
        rig.sun.intensity = 3.5 * this.sunStrength;
        rig.ambient.intensity = this.ambientStrength;
        rig.hemi.intensity = this.ambientStrength;

        this.sunDirection(this._sunDir);
        rig.sun.position.copy(this._sunDir).multiplyScalar(100);

        // Fog. Guard rather than trust: a zero fog end would collapse the world
        // to a point, and an unauthored band reads as zero rather than as
        // absent. Data may change the look; it may not delete the world.
        const end = this.fogEnd;
        if (scene.fog && end > 10) {
            const start = Math.min(Math.max(this.fogStart, 0), end - 1);
            scene.fog.color.setRGB(f[0], f[1], f[2], THREE.SRGBColorSpace);
            scene.fog.near = start;
            scene.fog.far = end;
            if (scene.background && scene.background.isColor) scene.background.copy(scene.fog.color);
            // Only ever GROWN. Geometry that vanishes before it has fogged out
            // is worse than geometry drawn past the fog.
            if (camera.far < end * 1.2) { camera.far = end * 1.2; camera.updateProjectionMatrix(); }
        }

        // Sky. The authored bands replace the editor's invented blue-to-orange
        // dome, which is hidden rather than removed so switching off restores it.
        const dome = this._dome();
        if (this.skyEnabled) {
            if (dome) dome.visible = false;
            const u = this._sky.material.uniforms;
            u.uSkyTop.value.setRGB(s.colors[B_SKY_TOP][0], s.colors[B_SKY_TOP][1], s.colors[B_SKY_TOP][2], THREE.SRGBColorSpace);
            u.uSkyMiddle.value.setRGB(s.colors[B_SKY_MIDDLE][0], s.colors[B_SKY_MIDDLE][1], s.colors[B_SKY_MIDDLE][2], THREE.SRGBColorSpace);
            u.uSkyBand1.value.setRGB(s.colors[B_SKY_BAND1][0], s.colors[B_SKY_BAND1][1], s.colors[B_SKY_BAND1][2], THREE.SRGBColorSpace);
            u.uSkyBand2.value.setRGB(s.colors[B_SKY_BAND2][0], s.colors[B_SKY_BAND2][1], s.colors[B_SKY_BAND2][2], THREE.SRGBColorSpace);
            u.uSkySmog.value.setRGB(s.colors[B_SKY_SMOG][0], s.colors[B_SKY_SMOG][1], s.colors[B_SKY_SMOG][2], THREE.SRGBColorSpace);
            this._sky.setStops(this.stops.middle, this.stops.band1, this.stops.band2);
            this._sky.updateCamera(camera);
            this._sky.setVisible(true);
        } else {
            if (dome) dome.visible = true;
            this._sky.setVisible(false);
        }

        if (!this._loggedOnce) { this._loggedOnce = true; console.info(this.describe()); }
    }

    // ── the instrument ───────────────────────────────────────────────────────

    /**
     * What the data says here and now, in one block.
     *
     * MSUIClient's argument for building the probe BEFORE touching a colour is
     * the whole reason this system reads correctly, so the web client gets the
     * same thing in the cheapest form that still works: printable, exact, and
     * showing the contributors so "only the map default applies" can be told
     * apart from "our position is in the wrong coordinate space".
     */
    describe() {
        if (!this.data) return '[light] no data loaded';
        const s = this.sample;
        const names = this.data.bandNames || [];
        const fnames = this.data.floatBandNames || [];
        const c = (i) => s.colors[i].map((v) => v.toFixed(3)).join(' ');

        const lines = [];
        lines.push(`[light] map ${this.data.mapId}  t=${this.timeHours.toFixed(2)}h  ` +
                   `sunStrength=${this.sunStrength} ambientStrength=${this.ambientStrength}`);
        if (s.contributors.length === 0) lines.push('  (no contributing zones — nothing resolved)');
        for (const k of s.contributors) {
            lines.push(`  zone ${k.id}${k.isDefault ? ' (map default)' : ''}` +
                       ` params ${k.paramsId} dist ${k.distance.toFixed(0)}yd weight ${k.weight.toFixed(3)}`);
        }
        for (let i = 0; i < 18; i++) lines.push(`  ${String(i).padStart(2)} ${(names[i] || '').padEnd(18)} ${c(i)}`);
        for (let i = 0; i < 6; i++) lines.push(`  f${i} ${(fnames[i] || '').padEnd(21)} ${s.floats[i].toFixed(3)}`);
        lines.push(`  fog ${this.fogStart.toFixed(0)} .. ${this.fogEnd.toFixed(0)} yd`);
        return lines.join('\n');
    }

    print() { console.log(this.describe()); return this.describe(); }
}
