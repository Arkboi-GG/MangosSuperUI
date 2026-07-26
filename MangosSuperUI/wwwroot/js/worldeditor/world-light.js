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
export function applyWorldLighting(material, rig) {
    if (!material || material.userData._worldLit) return material;
    material.userData._worldLit = true;

    material.onBeforeCompile = (shader) => {
        shader.uniforms.uWSunDir = { value: worldSunDirection(rig) };
        shader.uniforms.uWSunColor = {
            value: new THREE.Color(rig && rig.sun ? rig.sun.color : 0xffbb55) };
        shader.uniforms.uWSunI = { value: worldSunIntensity(rig) };
        shader.uniforms.uWAmbColor = {
            value: new THREE.Color(rig && rig.ambient ? rig.ambient.color : 0xffe8c8) };
        shader.uniforms.uWAmbI = { value: worldAmbientIntensity(rig) };
        material.userData._wlUniforms = shader.uniforms;

        shader.vertexShader =
            'varying vec3 vWLNormal;\n' +
            shader.vertexShader.replace(
                '#include <defaultnormal_vertex>',
                '#include <defaultnormal_vertex>\n' +
                '  vWLNormal = normalize(mat3(modelMatrix) * objectNormal);');

        // Replace the assembled lighting with the world model. diffuseColor
        // already holds albedo (map + colour + vertex colours) at this point.
        shader.fragmentShader =
            'uniform vec3 uWSunDir;\nuniform vec3 uWSunColor;\nuniform float uWSunI;\n' +
            'uniform vec3 uWAmbColor;\nuniform float uWAmbI;\nvarying vec3 vWLNormal;\n' +
            shader.fragmentShader.replace(
                '#include <output_fragment>',
                '  {\n' +
                '    vec3 wn = normalize(vWLNormal);\n' +
                '    if (!gl_FrontFacing) wn = -wn;\n' +
                '    float ndl = max(dot(wn, normalize(uWSunDir)), 0.0);\n' +
                '    vec3 wsun = uWSunColor * ndl * uWSunI;\n' +
                '    vec3 wamb = uWAmbColor * uWAmbI * mix(0.62, 1.0, wn.y * 0.5 + 0.5);\n' +
                '    gl_FragColor = vec4(diffuseColor.rgb * (wsun + wamb), diffuseColor.a);\n' +
                '  }');
    };
    material.customProgramCacheKey = () => 'world-lit-v1';
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
