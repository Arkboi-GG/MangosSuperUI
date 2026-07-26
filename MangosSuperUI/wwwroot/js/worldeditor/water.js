// water.js — per-liquid-type water appearance.
//
// The editor drew EVERY liquid surface as one translucent blue plane, so
// Ironforge's lava, Undercity's slime and the Stormwind canals were all the same
// colour. The type was already being parsed — `Water` returned a single
// `liquidType` for the whole tile and the client discarded it.
//
// Vanilla's legacy type codes, from the repo's own MLIQ reader:
//
//     1 = ocean    3 = slime    4 = river    6 = magma
//
// One ADT tile routinely carries more than one: a river running into the sea, a
// WMO's lava pool inside a mountain. So the type is now carried PER VERTEX and
// the tile's water mesh is split into one draw group per type. No shader — the
// split is an index partition and each group gets an ordinary material, which
// keeps three.js' fog and transparency handling doing what it already does.
//
// WHAT IS AUTHORED AND WHAT IS NOT, stated because the difference will matter
// when PLAN_12 lands:
//   OCEAN and RIVER colours ARE authored — LightIntBand bands 13-16 (ocean
//   close/far, river close/far) and the LightParams shallow/deep alphas, both of
//   which `/WorldEditor/Lighting` already ships and nothing yet consumes.
//   SLIME and MAGMA are NOT in that data at all; the values below are ours and
//   are marked as such rather than pretending otherwise.

import * as THREE from 'three';

export const LIQUID_OCEAN = 1;
export const LIQUID_SLIME = 3;
export const LIQUID_RIVER = 4;
export const LIQUID_MAGMA = 6;

/**
 * Per-type appearance. `lit: false` means the surface ignores scene lighting,
 * which is the whole point for magma: lava that dims at night is lava rendered
 * as though it were water.
 */
const TYPES = {
    [LIQUID_OCEAN]: { name: 'ocean', color: 0x1b4b78, opacity: 0.55, lit: false },
    [LIQUID_SLIME]: { name: 'slime', color: 0x59801c, opacity: 0.90, lit: false },
    [LIQUID_RIVER]: { name: 'river', color: 0x2266aa, opacity: 0.45, lit: false },
    [LIQUID_MAGMA]: { name: 'magma', color: 0xff5a12, opacity: 1.00, lit: false },
};

// Anything the data does not name reads as river — it is the most common
// freshwater surface and the least wrong guess. Type 0 lands here too.
const FALLBACK = TYPES[LIQUID_RIVER];

export function waterTypeName(type) {
    const t = TYPES[type];
    return t ? t.name : (type ? 'type ' + type : 'unknown');
}

export function makeWaterMaterial(type) {
    const t = TYPES[type] || FALLBACK;
    const mat = new THREE.MeshBasicMaterial({
        color: t.color,
        transparent: t.opacity < 1,
        opacity: t.opacity,
        side: THREE.DoubleSide,
        // Water must not write depth or it occludes the riverbed under it.
        // Magma is opaque but keeps the same treatment so a lava surface seen
        // through a doorway sorts the same way every other liquid does.
        depthWrite: false,
        fog: true,
    });
    mat.userData.liquidType = type;
    return mat;
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
