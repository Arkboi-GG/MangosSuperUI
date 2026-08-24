// Character Viewer — Blend-mode suffix decoder.
//
// === Session M phase 2.5 ===
// The server-side GlbWriter encodes M2 render-flag blend modes into the
// material NAME with a `_blendN` suffix:
//
//   "mat_0_blend0"  → opaque                  (NoBlending)
//   "mat_0_blend1"  → alpha-key / cutout      (alphaTest only)
//   "mat_0_blend2"  → alpha-blend             (NormalBlending, transparent)
//   "mat_0_blend3"  → additive                (AdditiveBlending) ← Thunderfury blade
//   "mat_0_blend4"  → add-alpha               (AdditiveBlending, alpha=true)
//   "mat_0_blend5"  → modulate                (MultiplyBlending)
//   "mat_0_blend6"  → mod2x                   (CustomBlending, equation = ADD * 2x)
//
// glTF can only express opaque / alpha-mask / alpha-blend natively. The
// _blendN suffix smuggles the additional info through so three.js can
// reconstruct the WoW-native blend mode.
//
// Both the character loader AND the equip-time GLTFLoader (for weapon /
// helm / shoulder attachments) call applyBlendSuffix on their loaded
// scene. Doing it once per load is cheap (O(meshes)) and idempotent.
//
// === Why per-mesh instead of per-material ===
// GLTFLoader can deduplicate materials across primitives. We DON'T want
// to mutate a shared material — that would affect other meshes that
// reference it. So we clone the material the first time we touch it
// and assign the clone back. Subsequent meshes that referenced the
// pre-clone material still see the original (untouched) instance.

import * as THREE from 'three';

// Capture both the blend mode and an optional _aNN alpha suffix. Even
// though glTF carries the alpha factor in baseColorFactor.A (already
// applied to material.opacity by GLTFLoader), we extract it here so
// applyBlendMode can MAKE THE MATERIAL TRANSPARENT even for blend
// modes that would otherwise force transparent=false (notably blend
// mode 0 = opaque, where three.js ignores opacity entirely unless
// transparent is true).
const _BLEND_RE = /_blend(\d+)(?:_a(\d+))?$/;

/**
 * Walk a loaded glTF scene and reconfigure any materials whose name
 * carries a `_blendN` suffix to use the correct three.js blending.
 *
 * @param {THREE.Object3D} sceneRoot  The root returned by GLTFLoader
 *                                    (gltf.scene or any subtree).
 * @returns {number}  Number of materials reconfigured (for debugging).
 */
export function applyBlendSuffix(sceneRoot) {
    let touched = 0;
    sceneRoot.traverse(node => {
        if (!node.isMesh) return;
        const mats = Array.isArray(node.material) ? node.material : [node.material];
        for (let i = 0; i < mats.length; i++) {
            const mat = mats[i];
            if (!mat || !mat.name) continue;

            const m = _BLEND_RE.exec(mat.name);
            if (!m) continue;
            const blendMode = parseInt(m[1], 10);
            // Alpha suffix is in 1% steps (00..99). 100 (= fully opaque) is
            // omitted entirely so we default to 1.0 when absent. A material
            // that came back with _a19 means the source M2 transparency track
            // resolved to alpha=0.19 in the bind/idle pose, and we want the
            // material to render at that opacity regardless of blend mode.
            const alpha = (m[2] === undefined) ? 1.0 : parseInt(m[2], 10) / 100;

            // Clone before mutating — see "Why per-mesh" above.
            const cloned = mat.clone();
            cloned.name = mat.name; // preserve so a re-scan is idempotent

            applyBlendMode(cloned, blendMode, alpha);

            // Write back. SkinnedMesh / Mesh both expose material as
            // either a single value or an array; respect what was there.
            if (Array.isArray(node.material)) node.material[i] = cloned;
            else node.material = cloned;
            touched++;
        }
    });
    return touched;
}

/**
 * Apply a single M2 blend mode to a three.js material. Pure function
 * (no traversal) so callers that already have a material handle can
 * reconfigure it directly — used by the character-body atlas pipeline
 * which clones materials for its own reasons.
 *
 * @param {THREE.Material} mat
 * @param {number} blendMode  M2 blend mode 0-6.
 * @param {number} [alpha=1.0]  Static alpha from the M2 transparency
 *   track (Session N). Forces transparent=true when below 1.0, even
 *   for blend modes that would normally configure as opaque, because
 *   three.js ignores material.opacity unless transparent is set.
 *   Without this, mat_0_blend0_a19 materials (e.g. Thunderfury lightning
 *   geosets — opaque blend, partially transparent alpha) render at full
 *   opacity and we see the bug from the screenshot.
 */
export function applyBlendMode(mat, blendMode, alpha = 1.0) {
    // Apply alpha to the material's opacity. GLTFLoader has already done
    // this via baseColorFactor.A, but we set it again explicitly in case
    // a future writer change loses it, and to ensure clone() copies it.
    mat.opacity = alpha;

    // When alpha < 1.0 we need transparent=true regardless of what the
    // blend mode wants, otherwise three.js drops the opacity entirely.
    const needsAlpha = alpha < 0.999;

    switch (blendMode) {
        case 0: // opaque
            mat.transparent = needsAlpha;
            mat.blending = THREE.NormalBlending;
            mat.depthWrite = !needsAlpha;
            mat.alphaTest = 0;
            break;

        case 1: // alpha-key / cutout
            mat.transparent = needsAlpha;
            mat.blending = THREE.NormalBlending;
            mat.depthWrite = true;
            mat.alphaTest = 0.5;
            break;

        case 2: // alpha-blend
            mat.transparent = true;
            mat.blending = THREE.NormalBlending;
            mat.depthWrite = false;
            mat.alphaTest = 0;
            break;

        case 3: // additive (the Thunderfury blade case)
            mat.transparent = true;
            mat.blending = THREE.AdditiveBlending;
            mat.depthWrite = false;
            mat.alphaTest = 0;
            // For additive materials we want the texture's RGB to drive
            // intensity directly with the texture's alpha as a mask.
            // three.js's AdditiveBlending already does srcAlpha-modulated
            // add by default — no extra config needed here.
            break;

        case 4: // add-alpha (additive with alpha modulation)
            mat.transparent = true;
            mat.blending = THREE.AdditiveBlending;
            mat.depthWrite = false;
            mat.alphaTest = 0;
            break;

        case 5: // modulate
            mat.transparent = true;
            mat.blending = THREE.MultiplyBlending;
            mat.depthWrite = false;
            mat.alphaTest = 0;
            break;

        case 6: // mod2x — three.js doesn't have a native preset for this.
            // Rare on weapons. Approximate with custom blend equation.
            mat.transparent = true;
            mat.blending = THREE.CustomBlending;
            mat.blendEquation = THREE.AddEquation;
            mat.blendSrc = THREE.DstColorFactor;
            mat.blendDst = THREE.SrcColorFactor;
            mat.depthWrite = false;
            break;

        default:
            // Unknown — leave as default
            break;
    }
    mat.needsUpdate = true;
}

// Matches the `_env` marker WeaponPreviewService adds to an environment-mapped pass's
// material name (e.g. "pass1_tex0_env_blend6_a100"). `_env` sits before the blend suffix so
// applyBlendSuffix's end-anchored regex still resolves the blend mode.
const _ENV_RE = /_env(?:_|$)/;

/**
 * Render WoW ENVIRONMENT-MAPPED passes as a matcap.
 *
 * WoW's EnvMap texture-coordinate mode samples the texture by the surface's view-space normal
 * (a sphere map), NOT by the mesh UVs — which is what makes a weapon's reflective energy flow
 * across the blade as it turns. glTF cannot express that, so the server flattens those passes to
 * static UV0 and the effect FREEZES (the Warglaive's dead green blades). three.js's
 * MeshMatcapMaterial does exactly the same view-normal sphere sampling, so swapping an `_env`
 * pass to a matcap of its own texture restores the moving reflection.
 *
 * Run AFTER applyBlendSuffix so the blend/transparency state it resolved can be copied onto the
 * matcap verbatim — only the sampling changes, not the compositing.
 *
 * @param {THREE.Object3D} sceneRoot
 * @returns {number} number of materials converted (logged for verification).
 */
export function applyEnvMapping(sceneRoot) {
    let converted = 0;
    sceneRoot.traverse(node => {
        if (!node.isMesh) return;
        const mats = Array.isArray(node.material) ? node.material : [node.material];
        for (let i = 0; i < mats.length; i++) {
            const mat = mats[i];
            if (!mat || !mat.name || !_ENV_RE.test(mat.name)) continue;

            const env = new THREE.MeshMatcapMaterial({ matcap: mat.map || null });
            env.name = mat.name;
            // Carry over the exact blend/depth state applyBlendSuffix resolved — additive, mod2x,
            // etc. The matcap only changes how the texture is SAMPLED (view normal, not UVs).
            env.transparent = mat.transparent;
            env.opacity = mat.opacity;
            env.depthWrite = mat.depthWrite;
            env.depthTest = mat.depthTest;
            env.side = mat.side;
            env.alphaTest = mat.alphaTest;
            env.blending = mat.blending;
            env.blendEquation = mat.blendEquation;
            env.blendSrc = mat.blendSrc;
            env.blendDst = mat.blendDst;
            if (mat.color && env.color) env.color.copy(mat.color);
            env.needsUpdate = true;

            if (Array.isArray(node.material)) node.material[i] = env;
            else node.material = env;
            converted++;
        }
    });
    if (converted > 0)
        console.log(`[env-map] ${converted} environment-mapped pass(es) → matcap (WoW EnvMap reflection restored)`);
    else
        console.log('[env-map] no _env passes on this model — nothing to convert');
    return converted;
}

// `_mod` marker WeaponPreviewService adds to a multi-texture MODULATE pass (e.g.
// "pass3_tex1_mod_blend4_a100"). Sits before the blend suffix so applyBlendSuffix still parses it.
const _MOD_RE = /_mod(?:_|$)/;

/**
 * Reconstruct a WoW multi-texture MODULATE pass — the "wave" combine.
 *
 * WoW combines a 2-texture batch by MULTIPLYING a static copy of the energy by a scrolling copy;
 * the interference is a band that grows, shifts and shrinks along the blade over the loop. glTF/three
 * can't express a two-sample multiply in one additive pass, so the exporter would otherwise split it
 * into two additive layers — which can only ADD, lighting the whole area (the "full glow" bug).
 *
 * Here the pass carries TWO UV sets: TEXCOORD_0 (the scroll mapping, already animated by m2fx via
 * map.matrix → vMapUv) and TEXCOORD_1 = 'uv1' (the static mapping). We sample the SAME map a second
 * time at the static UV and multiply it into the base colour. Product = scrolled × static = the wave.
 *
 * Run AFTER installM2Fx so this lands on the material m2fx actually animates (installM2Fx clones and
 * reassigns; Material.clone does not carry onBeforeCompile, so it must be set last).
 *
 * @param {THREE.Object3D} sceneRoot
 * @returns {number} number of materials converted.
 */
export function applyMultiTexture(sceneRoot) {
    let converted = 0;
    sceneRoot.traverse(node => {
        if (!node.isMesh) return;
        const mats = Array.isArray(node.material) ? node.material : [node.material];
        for (let i = 0; i < mats.length; i++) {
            const mat = mats[i];
            if (!mat || !mat.name || !_MOD_RE.test(mat.name) || !mat.map) continue;

            // Route the SECOND (static) sample of the energy texture through the aoMap slot, which is the
            // supported way to get a UV1-mapped varying (vAoMapUv) in this three.js — a bare
            // `attribute vec2 uv1` is not bound unless a channel-1 texture activates it. aoMap is a
            // static clone of the same texture on channel 1; aoMapIntensity 0 keeps its own AO term
            // inert. mat.map stays on channel 0 and keeps the m2fx scroll (map.matrix).
            const modTex = mat.map.clone();
            modTex.matrixAutoUpdate = true;   // identity — the static copy must NOT scroll
            modTex.channel = 1;
            modTex.needsUpdate = true;
            mat.aoMap = modTex;
            mat.aoMapIntensity = 0;

            mat.onBeforeCompile = (shader) => {
                // map_fragment has just set diffuseColor from the SCROLLED sample (vMapUv). Multiply the
                // STATIC sample (aoMap at vAoMapUv = UV1) to get the modulated, moving wave — the product
                // WoW's fixed-function multi-texture stage produces and two additive passes cannot.
                shader.fragmentShader = shader.fragmentShader.replace(
                    '#include <map_fragment>',
                    '#include <map_fragment>\n\tdiffuseColor *= texture2D( aoMap, vAoMapUv );');
            };
            // Keep this program out of the shared cache so a plain additive material never reuses it.
            mat.customProgramCacheKey = () => 'suiModCombine';
            mat.needsUpdate = true;
            converted++;
        }
    });
    if (converted > 0)
        console.log(`[multi-tex] ${converted} MODULATE pass(es) → two-sample multiply (wave restored)`);
    else
        console.log('[multi-tex] no _mod passes on this model');
    return converted;
}