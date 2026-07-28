// world-light.js — make every lit surface use ONE lighting model.
//
// MSUIClient's rule, from Shaders/character.frag's own header: that shader is
// wmo.frag with a single change, and the rest is byte-identical on purpose
// because "a character that lights differently from the ground he stands on
// looks wrong in a way that is hard to name and easy to avoid". terrain.frag
// carries the same sun + ambient terms.
//
// The web editor broke that the moment terrain moved to a custom ShaderMaterial:
// the ground started using the simple model while characters, doodads and
// buildings stayed on three.js MeshStandardMaterial lit by a rig that sums well
// past 1.0. The character did not change — the world around it did — and it
// read as a pale cut-out.
//
// This applies the SAME formula to a MeshStandardMaterial without giving up
// skinning, shadows, or anything else three.js does for us: onBeforeCompile
// replaces the final lighting, and nothing else.
//
//     lit = albedo * (sunColor * max(dot(n, sunDir), 0) * sunIntensity
//                   + ambientColor * ambientIntensity * mix(0.62, 1.0, n.y*0.5+0.5))
//
// Uniform values come from terrain-splat.js's worldSunIntensity /
// worldAmbientIntensity, so there is exactly one place they are decided.

import * as THREE from 'three';
import {
    worldSunIntensity, worldAmbientIntensity, worldSunDirection
} from './terrain-splat.js';

const tracked = new Set();

/**
 * Override a MeshStandardMaterial's lighting with the world model.
 * Idempotent — safe to call again on the same material.
 */
export function applyWorldLighting(material, rig, opts) {
    if (!material || material.userData._worldLit) return material;
    material.userData._worldLit = true;

    // Grass/foliage cards are crossed VERTICAL billboards, so their real normals
    // are horizontal (y≈0). At noon the sun is overhead, so dot(horizontal, sun)
    // ≈ 0 and grass receives NO sun — it reads as flat, dark, blue-ambient slabs
    // (the "squares"). Games light grass with a fixed UP normal so a field lit by
    // an overhead sun looks lit. skyNormal switches this material to that trick.
    const skyNormal = !!(opts && opts.skyNormal);

    material.onBeforeCompile = (shader) => {
        shader.uniforms.uWSunDir = { value: worldSunDirection(rig) };
        shader.uniforms.uWSunColor = {
            value: new THREE.Color(rig && rig.sun ? rig.sun.color : 0xffbb55) };
        shader.uniforms.uWSunI = { value: worldSunIntensity(rig) };
        shader.uniforms.uWAmbColor = {
            value: new THREE.Color(rig && rig.ambient ? rig.ambient.color : 0xffe8c8) };
        shader.uniforms.uWAmbI = { value: worldAmbientIntensity(rig) };
        material.userData._wlUniforms = shader.uniforms;

        // For grass (skyNormal) light with a fixed world-up normal so the
        // overhead noon sun reaches it. For everything else derive the world
        // normal, including the per-instance rotation on InstancedMesh (doodads,
        // WMOs) — without instanceMatrix every instance is lit as if unrotated,
        // so rotated trees/buildings read flat and faceted (the "low-poly" look).
        const normalInject = skyNormal
            ? '  vWLNormal = vec3(0.0, 1.0, 0.0);'
            : '  vec3 wlN = objectNormal;\n' +
              '  #ifdef USE_INSTANCING\n' +
              '    wlN = mat3(instanceMatrix) * wlN;\n' +
              '  #endif\n' +
              '  vWLNormal = normalize(mat3(modelMatrix) * wlN);';

        shader.vertexShader =
            'varying vec3 vWLNormal;\n' +
            shader.vertexShader.replace(
                '#include <defaultnormal_vertex>',
                '#include <defaultnormal_vertex>\n' + normalInject);

        // Replace the assembled lighting with the world model. diffuseColor
        // already holds albedo (map + colour + vertex colours) at this point.
        //
        // THE CHUNK NAME MATTERS: three.js renamed <output_fragment> to
        // <opaque_fragment> in r150. The World Editor runs r162, so the old name
        // does NOT exist and replacing it was a SILENT NO-OP — which is exactly
        // why the character (and doodads/WMOs) never actually became world-lit
        // and read as a pale, flat cut-out. Target whichever the running three.js
        // ships so this works on r128 (viewer) and r162 (editor) alike.
        const worldLit =
            '  {\n' +
            '    vec3 wn = normalize(vWLNormal);\n' +
            '    if (!gl_FrontFacing) wn = -wn;\n' +
            '    float ndl = max(dot(wn, normalize(uWSunDir)), 0.0);\n' +
            '    vec3 wsun = uWSunColor * ndl * uWSunI;\n' +
            '    vec3 wamb = uWAmbColor * uWAmbI * mix(0.62, 1.0, wn.y * 0.5 + 0.5);\n' +
            '    gl_FragColor = vec4(diffuseColor.rgb * (wsun + wamb), diffuseColor.a);\n' +
            '  }';
        let frag = shader.fragmentShader;
        if (frag.indexOf('#include <opaque_fragment>') !== -1) {
            frag = frag.replace('#include <opaque_fragment>', worldLit);
        } else if (frag.indexOf('#include <output_fragment>') !== -1) {
            frag = frag.replace('#include <output_fragment>', worldLit);
        } else {
            // Last resort: overwrite the final gl_FragColor assignment.
            frag = frag.replace(/gl_FragColor\s*=\s*vec4\([^;]*;/, worldLit);
        }
        shader.fragmentShader =
            'uniform vec3 uWSunDir;\nuniform vec3 uWSunColor;\nuniform float uWSunI;\n' +
            'uniform vec3 uWAmbColor;\nuniform float uWAmbI;\nvarying vec3 vWLNormal;\n' +
            frag;
    };
    material.customProgramCacheKey = () => skyNormal ? 'world-lit-sky-v1' : 'world-lit-v1';
    material.needsUpdate = true;
    tracked.add(material);
    return material;
}

// ─────────────────────────────────────────────────────────────────────────────
// WMO interior lighting — the baked-MOCV branch (SYSTEM_WMO_INTERIOR_LIGHTING.md)
// ─────────────────────────────────────────────────────────────────────────────
//
// Vanilla does NOT light building interiors at runtime; the artist baked a colour
// per vertex (MOCV) — warm tavern light, the dark back of a mine — and the client
// only modulates the texture by it. The web lit every WMO face with the outdoor
// sun, so interiors read as flat, dull, brightly-lit boxes. This applies
// MSUIClient wmo.frag's three-way branch on top of the SAME daylight model the
// rest of the world uses, so exterior faces are byte-identical to before and only
// interior/transitional batches change.
//
//   light  = the world daylight model (identical to applyWorldLighting)
//   baked  = mocv.rgb * 2.0            (Blizzard halves at load, doubles at draw)
//   type 1 (transparent): mix(baked, light,          mocv.a)   // portal fade
//   type 2 (interior):    mix(baked, light + baked,  mocv.a)
//   type 3 (exterior):    light                                // unchanged
//
// It registers into the SAME tracked set as applyWorldLighting, so
// syncWorldLighting drives its sun/ambient uniforms with no extra wiring. The
// geometry must carry a normalized vec4 `mocv` attribute (streaming.js sets it).
export function applyWmoLighting(material, rig, batchType) {
    if (!material || material.userData._worldLit) return material;
    material.userData._worldLit = true;
    const bt = (batchType === 1 || batchType === 2) ? batchType : 3;

    material.onBeforeCompile = (shader) => {
        shader.uniforms.uWSunDir = { value: worldSunDirection(rig) };
        shader.uniforms.uWSunColor = {
            value: new THREE.Color(rig && rig.sun ? rig.sun.color : 0xffbb55) };
        shader.uniforms.uWSunI = { value: worldSunIntensity(rig) };
        shader.uniforms.uWAmbColor = {
            value: new THREE.Color(rig && rig.ambient ? rig.ambient.color : 0xffe8c8) };
        shader.uniforms.uWAmbI = { value: worldAmbientIntensity(rig) };
        material.userData._wlUniforms = shader.uniforms;   // syncWorldLighting updates these

        shader.vertexShader =
            'attribute vec4 mocv;\nvarying vec4 vMocv;\nvarying vec3 vWLNormal;\n' +
            shader.vertexShader.replace(
                '#include <defaultnormal_vertex>',
                '#include <defaultnormal_vertex>\n' +
                '  {\n' +
                '    vec3 wlN = objectNormal;\n' +
                '    #ifdef USE_INSTANCING\n' +
                '      wlN = mat3(instanceMatrix) * wlN;\n' +
                '    #endif\n' +
                '    vWLNormal = normalize(mat3(modelMatrix) * wlN);\n' +
                '    vMocv = mocv;\n' +
                '  }');

        const worldLit =
            '  {\n' +
            '    vec3 wn = normalize(vWLNormal);\n' +
            '    if (!gl_FrontFacing) wn = -wn;\n' +
            '    float ndl = max(dot(wn, normalize(uWSunDir)), 0.0);\n' +
            '    vec3 wsun = uWSunColor * ndl * uWSunI;\n' +
            '    vec3 wamb = uWAmbColor * uWAmbI * mix(0.62, 1.0, wn.y * 0.5 + 0.5);\n' +
            '    vec3 light = wsun + wamb;\n' +
            '    vec3 baked = vMocv.rgb * 2.0;\n' +   // VertexColorScale = 2.0 (vanilla)
            '    vec3 lighting;\n' +
            (bt === 1
                ? '    lighting = mix(baked, light, vMocv.a);\n'
                : bt === 2
                    ? '    lighting = mix(baked, light + baked, vMocv.a);\n'
                    : '    lighting = light;\n') +
            '    gl_FragColor = vec4(diffuseColor.rgb * lighting, diffuseColor.a);\n' +
            '  }';

        let frag = shader.fragmentShader;
        if (frag.indexOf('#include <opaque_fragment>') !== -1) {
            frag = frag.replace('#include <opaque_fragment>', worldLit);
        } else if (frag.indexOf('#include <output_fragment>') !== -1) {
            frag = frag.replace('#include <output_fragment>', worldLit);
        } else {
            frag = frag.replace(/gl_FragColor\s*=\s*vec4\([^;]*;/, worldLit);
        }
        shader.fragmentShader =
            'uniform vec3 uWSunDir;\nuniform vec3 uWSunColor;\nuniform float uWSunI;\n' +
            'uniform vec3 uWAmbColor;\nuniform float uWAmbI;\n' +
            'varying vec3 vWLNormal;\nvarying vec4 vMocv;\n' +
            frag;
    };
    material.customProgramCacheKey = () => 'wmo-lit-v1-' + bt;
    material.needsUpdate = true;
    tracked.add(material);
    return material;
}

/** Push rig changes into every world-lit material. */
export function syncWorldLighting(rig) {
    for (const m of tracked) {
        const u = m.userData && m.userData._wlUniforms;
        if (!u) continue;
        worldSunDirection(rig, u.uWSunDir.value);
        if (rig && rig.sun) u.uWSunColor.value.copy(rig.sun.color);
        if (rig && rig.ambient) u.uWAmbColor.value.copy(rig.ambient.color);
        u.uWSunI.value = worldSunIntensity(rig);
        u.uWAmbI.value = worldAmbientIntensity(rig);
    }
}

export function forgetWorldLighting(material) { tracked.delete(material); }
