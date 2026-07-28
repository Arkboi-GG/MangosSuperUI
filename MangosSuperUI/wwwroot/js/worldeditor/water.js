// water.js — animated, per-liquid-type water.
//
// ═══════════════════════════════════════════════════════════════════════════
// PORTED FROM MSUIClient
//   Shaders/water.vert   Gerstner surface displacement (small, wind-driven)
//   Shaders/water.frag   the PROCEDURAL fallback path — animated caustics,
//                        shimmer, wave-crest brightening, oscillating bands,
//                        fresnel sky-sheen, and the self-luminous magma/slime
//                        treatment. That path is fully self-contained GLSL, so
//                        it needs no BLP frame textures served from the MPQs.
//   World/LiquidRenderer.cs  liquid type codes, blend/depth state, the uTime
//                        seconds accumulator that drives the animation.
// ═══════════════════════════════════════════════════════════════════════════
//
// The editor drew EVERY liquid surface as one flat translucent plane — no
// motion, one colour. Vanilla water moves. This gives each liquid type its own
// animated material: the surface bobs on a stack of small waves and the colour
// shimmers, so a river reads as flowing water and a lava pool reads as lava.
//
// One ADT tile routinely carries more than one liquid (a river into the sea, a
// WMO lava pool). The type is carried PER VERTEX and the tile's water mesh is
// split into one draw group per type by partitionByType(); each group gets its
// own material with its type baked into a uniform.
//
// WHAT IS AUTHORED AND WHAT IS NOT (unchanged from before): ocean/river colours
// ARE authored in Light.dbc bands 13-16 and could later replace the palette
// here; slime and magma are ours. This file is the animation + look; wiring the
// authored colours through is a later pass (PLAN_12).
//
// COORDINATE NOTE: MSUIClient is Z-up; this is three.js Y-up. Vertical is +Y,
// the horizontal plane is XZ, and the surface has no per-vertex depth attribute
// (the /WorldEditor/Water mesh is surface positions only), so the shoreline
// depth fade from water.frag is dropped and the wave amplitude is kept small so
// nothing climbs a beach.

import * as THREE from 'three';

export const LIQUID_OCEAN = 1;
export const LIQUID_SLIME = 3;
export const LIQUID_RIVER = 4;
export const LIQUID_MAGMA = 6;

// Per-type look. `opacity` is the deep-water alpha; depth write is off so the
// bed shows through. magma/slime are self-luminous.
const TYPES = {
    [LIQUID_OCEAN]: { name: 'ocean', shallow: [0.06, 0.20, 0.28], deep: [0.02, 0.09, 0.16], opacity: 0.72 },
    [LIQUID_RIVER]: { name: 'river', shallow: [0.10, 0.26, 0.26], deep: [0.05, 0.15, 0.16], opacity: 0.60 },
    [LIQUID_SLIME]: { name: 'slime', shallow: [0.10, 0.26, 0.05], deep: [0.05, 0.15, 0.02], opacity: 0.92 },
    [LIQUID_MAGMA]: { name: 'magma', shallow: [1.00, 0.45, 0.05], deep: [0.15, 0.04, 0.01], opacity: 1.00 },
};

// Anything the data does not name reads as river — the commonest freshwater
// surface and the least wrong guess. Type 0 lands here too.
const FALLBACK_TYPE = LIQUID_RIVER;
const FALLBACK = TYPES[FALLBACK_TYPE];

export function waterTypeName(type) {
    const t = TYPES[type];
    return t ? t.name : (type ? 'type ' + type : 'unknown');
}

// ─────────────────────────────────────────────────────────────────────────────
// Shared, once-per-frame uniforms (time, sun, fog). Every water material
// references THESE OBJECTS, so tickWater() updates them all in one place — the
// same "one lighting model" discipline terrain-splat.js/world-light.js keep.
// ─────────────────────────────────────────────────────────────────────────────
const SHARED = {
    uTime: { value: 0 },
    uWaveAmp: { value: 0.14 },     // small: no depth data to flatten the shore
    uWaveSpeed: { value: 1.0 },
    uSunDir: { value: new THREE.Vector3(-0.3, 0.95, 0.05).normalize() },
    uSunColor: { value: new THREE.Color(1.0, 0.9, 0.72) },
    uSunI: { value: 0.9 },
    uAmbColor: { value: new THREE.Color(0.5, 0.46, 0.38) },
    uAmbI: { value: 0.64 },
    uFogColor: { value: new THREE.Color(0.56, 0.71, 0.85) },
    uFogNear: { value: 250 },
    uFogFar: { value: 850 },
};

const VERT = /* glsl */`
uniform float uTime;
uniform float uWaveAmp;
uniform float uWaveSpeed;

varying vec3  vWorldPos;
varying vec2  vAbsXZ;
varying float vWave;

// Three small Gerstner-ish waves in spread directions. Vertical bob only enough
// to catch the light; the surface stays essentially flat so it never rises over
// a shoreline (there is no depth attribute to fade it out with).
float waveSum(vec2 p, float t) {
    float w = 0.0;
    w += sin(dot(p, vec2( 0.16,  0.11)) + t * 1.10) * 1.00;
    w += sin(dot(p, vec2(-0.13,  0.19)) + t * 1.37) * 0.55;
    w += sin(dot(p, vec2( 0.21, -0.08)) + t * 1.73) * 0.30;
    return w;
}

void main() {
    vec4 wp = modelMatrix * vec4(position, 1.0);
    vAbsXZ = wp.xz;

    float t = uTime * uWaveSpeed;
    float h = waveSum(wp.xz, t);
    vWave = h;
    wp.y += h * uWaveAmp;

    vWorldPos = wp.xyz;
    gl_Position = projectionMatrix * viewMatrix * wp;
}
`;

const FRAG = /* glsl */`
precision highp float;

uniform float uType;          // 1 ocean, 3 slime, 4 river, 6 magma
uniform vec3  uShallow;
uniform vec3  uDeep;
uniform float uOpacity;
uniform float uLit;           // 1 = lit water, 0 = self-luminous magma/slime

uniform float uTime;
uniform vec3  uSunDir;
uniform vec3  uSunColor;
uniform float uSunI;
uniform vec3  uAmbColor;
uniform float uAmbI;
uniform vec3  uFogColor;
uniform float uFogNear;
uniform float uFogFar;

varying vec3  vWorldPos;
varying vec2  vAbsXZ;
varying float vWave;

float hash21(vec2 p){ return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float vnoise(vec2 p){
    vec2 i = floor(p), f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = hash21(i), b = hash21(i + vec2(1,0));
    float c = hash21(i + vec2(0,1)), d = hash21(i + vec2(1,1));
    return mix(mix(a,b,f.x), mix(c,d,f.x), f.y);
}
float fbm(vec2 p, float t){
    float v = 0.0;
    v += vnoise(p * 3.0 + t * 0.3) * 0.5;
    v += vnoise(p * 6.0 - t * 0.5) * 0.25;
    v += vnoise(p * 12.0 + t * 0.7) * 0.125;
    return v;
}

void main() {
    vec3  V    = normalize(cameraPosition - vWorldPos);   // toward camera
    float dist = length(cameraPosition - vWorldPos);
    float fog  = clamp((dist - uFogNear) / max(uFogFar - uFogNear, 1.0), 0.0, 1.0);

    bool magma = (uType > 5.5);
    bool slime = (uType > 2.5 && uType < 3.5);

    if (magma || slime) {
        // Self-luminous, viscous flow. Colour crawls with time; a slow pulse and
        // a hot core give it life. No sky lighting — lava that dims at night is
        // lava rendered as though it were water.
        vec2 uv = vAbsXZ;
        float n1 = fbm(uv * 0.06 + vec2(uTime * 0.02, uTime * 0.03), uTime * 0.4);
        float n2 = fbm(uv * 0.12 + vec2(-uTime * 0.015, uTime * 0.025), uTime * 0.3);
        float flow = n1 * 0.6 + n2 * 0.4;

        vec3 crust = uDeep;
        vec3 hot   = uShallow;
        vec3 core  = magma ? vec3(1.0, 0.85, 0.30) : vec3(0.50, 1.0, 0.30);
        float crustMask = smoothstep(0.25, 0.50, flow);
        float coreMask  = smoothstep(0.60, 0.80, flow);
        vec3 col = mix(crust, hot, crustMask);
        col = mix(col, core, coreMask);
        col *= 1.0 + 0.15 * sin(uTime * 1.5 + flow * 6.0);
        col *= 1.0 + coreMask * 0.6;
        col = mix(uFogColor, col, 1.0 - fog);
        gl_FragColor = vec4(col, mix(0.97, 1.0, coreMask));
        return;
    }

    // ── Ordinary water (ocean/river) ──────────────────────────────────────────
    bool ocean = (uType > 0.5 && uType < 1.5);

    // A slow noise stands in for depth variation, so the body is not one flat
    // tone: darker in the "deep" troughs, lighter on the "shallow" crests.
    float depth = 0.5 + 0.5 * fbm(vAbsXZ * 0.03, uTime * 0.05);
    vec3 body = mix(uShallow, uDeep, depth);

    // Flat lighting (the ripples are drawn, not relit) so the surface sits into
    // the world the same way terrain/wmo/character do.
    float ndl = max(uSunDir.y, 0.0);
    vec3 light = uAmbColor * uAmbI + uSunColor * uSunI * ndl * 0.35;
    vec3 col = body * light;

    // Animated caustics / shimmer — the drifting light on moving water.
    float g1 = fbm(vAbsXZ * 0.35 + vec2( uTime * 0.90, uTime * 0.20), uTime);
    float g2 = fbm(vAbsXZ * 0.75 + vec2(-uTime * 0.50, uTime * 0.55), uTime * 1.3);
    float caustic = g1 * 0.6 + g2 * 0.4;
    col += (ocean ? vec3(0.10, 0.14, 0.18) : vec3(0.11, 0.17, 0.15))
         * smoothstep(0.50, 0.90, caustic);
    col += vec3(0.20, 0.24, 0.22) * smoothstep(0.82, 0.99, caustic);

    // Slow light/dark bands drifting sideways — the "oscillating colours".
    float osc = 0.5 + 0.5 * sin(dot(vAbsXZ, vec2(0.12, 0.07)) - uTime * 1.4);
    col *= 0.92 + 0.16 * osc;

    // Wave-crest brightening from the vertex wave height.
    col += vec3(smoothstep(0.6, 1.4, vWave) * 0.05);

    // Grazing-angle sky sheen (fresnel on the flat surface).
    float fres = clamp(pow(1.0 - max(V.y, 0.0), 5.0), 0.0, 1.0);
    col = mix(col, uFogColor, fres * (ocean ? 0.40 : 0.28));

    // Sun sparkle on the crests.
    float sparkle = pow(max(fbm(vAbsXZ * 4.0 + uTime * 0.5, uTime * 1.5) - 0.55, 0.0) / 0.45, 3.0);
    col += uSunColor * uSunI * sparkle * 0.12;

    col = mix(col, uFogColor, fog);

    float alpha = mix(uOpacity, min(1.0, uOpacity * 1.3), fres);
    gl_FragColor = vec4(col, alpha);
}
`;

// Live materials, so tickWater() can advance them without walking the scene.
const LIVE = new Set();

export function makeWaterMaterial(type) {
    const t = TYPES[type] || FALLBACK;
    const magmaOrSlime = (type === LIQUID_MAGMA || type === LIQUID_SLIME);
    const mat = new THREE.ShaderMaterial({
        vertexShader: VERT,
        fragmentShader: FRAG,
        transparent: t.opacity < 1 || !magmaOrSlime,
        // Water must not write depth or it occludes the bed under it. Magma keeps
        // the same treatment so a lava surface seen through a doorway sorts the
        // way every other liquid does.
        depthWrite: false,
        depthTest: true,
        side: THREE.DoubleSide,
        fog: false,   // this shader does its own fog against the same horizon
        uniforms: {
            uType: { value: type || FALLBACK_TYPE },
            uShallow: { value: new THREE.Color().fromArray(t.shallow) },
            uDeep: { value: new THREE.Color().fromArray(t.deep) },
            uOpacity: { value: t.opacity },
            uLit: { value: magmaOrSlime ? 0 : 1 },
            // Shared, updated once per frame in tickWater.
            uTime: SHARED.uTime,
            uWaveAmp: SHARED.uWaveAmp,
            uWaveSpeed: SHARED.uWaveSpeed,
            uSunDir: SHARED.uSunDir,
            uSunColor: SHARED.uSunColor,
            uSunI: SHARED.uSunI,
            uAmbColor: SHARED.uAmbColor,
            uAmbI: SHARED.uAmbI,
            uFogColor: SHARED.uFogColor,
            uFogNear: SHARED.uFogNear,
            uFogFar: SHARED.uFogFar,
        },
    });
    mat.userData.liquidType = type;
    mat.userData.isWater = true;
    LIVE.add(mat);
    const dispose0 = mat.dispose.bind(mat);
    mat.dispose = function () { LIVE.delete(mat); dispose0(); };
    return mat;
}

/**
 * Advance the water animation and keep it lit by the same rig as the world.
 * Registered as a viewport ticker (called fn(viewport, dt) every frame).
 */
export function tickWater(viewport, dt) {
    if (!(dt > 0)) return;
    if (LIVE.size === 0) return;
    SHARED.uTime.value += dt;

    // Track the scene's fog and lighting rig so water fades into the same
    // horizon and dims with the day cycle, exactly like terrain and grass.
    const fog = viewport.scene && viewport.scene.fog;
    if (fog) {
        SHARED.uFogColor.value.copy(fog.color);
        SHARED.uFogNear.value = fog.near;
        SHARED.uFogFar.value = fog.far;
    }
    const rig = viewport.lighting;
    if (rig) {
        if (rig.sun) {
            if (rig.sun.position) SHARED.uSunDir.value.copy(rig.sun.position).normalize();
            SHARED.uSunColor.value.copy(rig.sun.color);
            SHARED.uSunI.value = Math.min(rig.sun.intensity / 3.5, 1.0);
        }
        if (rig.ambient) {
            SHARED.uAmbColor.value.copy(rig.ambient.color);
            const h = rig.hemi ? rig.hemi.intensity : rig.ambient.intensity;
            SHARED.uAmbI.value = (rig.ambient.intensity + h) * 0.5;
        }
    }
}

/**
 * Split an indexed water mesh into one draw group per liquid type.
 *
 * `types` is one byte per VERTEX; every quad's four vertices share a value, so
 * reading the type off a triangle's first index is exact rather than a guess.
 * Returns { index, groups, materials } or null when there is nothing to draw.
 */
export function partitionByType(indices, types, fallbackType) {
    if (!indices || indices.length === 0) return null;

    const buckets = new Map();
    for (let i = 0; i + 2 < indices.length; i += 3) {
        const t = types ? types[indices[i]] : (fallbackType | 0);
        let b = buckets.get(t);
        if (!b) { b = []; buckets.set(t, b); }
        b.push(indices[i], indices[i + 1], indices[i + 2]);
    }

    const ordered = new Uint32Array(indices.length);
    const groups = [];
    const materials = [];
    let at = 0;
    for (const [type, list] of buckets) {
        groups.push({ start: at, count: list.length, materialIndex: materials.length });
        materials.push(makeWaterMaterial(type));
        for (let i = 0; i < list.length; i++) ordered[at++] = list[i];
    }

    return { index: ordered, groups, materials };
}
