// terrain-splat.js — vanilla 4-layer terrain splatting in the browser.
//
// ═══════════════════════════════════════════════════════════════════════════
// PORTED FROM MSUIClient
//   Shaders/terrain.frag        the blend, the UV derivation, the lighting
//   World/TerrainTextures.cs    tileset array + 1024x1024 alpha atlas layout
//   World/TerrainTile.cs        TextureScale 8.0 repeats per chunk
// ═══════════════════════════════════════════════════════════════════════════
//
// WHY
//   The server used to bake MTEX + MCLY + MCAL into ONE composite stretched
//   across a 533-yard tile. At pixelsPerChunk 128 that is 2048 texels over 533
//   yards — 0.26 yd/texel — so underfoot you magnify a single texel across a
//   quarter of a yard. That is the blur.
//
//   Vanilla never resamples. It keeps up to 4 layers per chunk and REPEATS each
//   one 8 times per 33.3-yard chunk. A 256x256 tileset texture at 8 repeats is
//   ~61 texels per yard: about 16x the detail, from identical source data.
//
// HOW
//   - All of a tile's MTEX textures go into one DataArrayTexture, so the whole
//     tile draws in one call with a dynamic layer index.
//   - Every chunk's three alpha masks pack into one 1024x1024 texture
//     (16x16 chunks x 64x64 texels): R = layer 1, G = layer 2, B = layer 3.
//   - Which four array layers a chunk uses comes from a 16x16 lookup texture.
//
// ═══════════════════════════════════════════════════════════════════════════
// TWO DELIBERATE DIVERGENCES FROM MSUIClient, BOTH FORCED
// ═══════════════════════════════════════════════════════════════════════════
// 1. LAYER INDICES ARE A TEXTURE LOOKUP, NOT A VERTEX ATTRIBUTE.
//    MSUIClient builds each MCNK's 9x9+8x8 vertices separately, so every vertex
//    can carry its own chunk's four indices in a flat vec4 attribute. Our mesh
//    is one merged 129x129 V9 grid from VmangosMapParser, where chunk-boundary
//    vertices are SHARED. A flat attribute would make every boundary vertex pick
//    one neighbour arbitrarily and stripe the seams. Sampling a 16x16 lookup
//    texture by chunk coordinate is exact at every fragment and leaves the
//    geometry builder (and the sculpt tool's vertex indices) untouched.
//
// 2. GLSL3 ShaderMaterial, not onBeforeCompile on MeshStandardMaterial.
//    sampler2DArray does not exist in GLSL ES 1.00, which is what three.js emits
//    for its built-in materials. Packing the tileset into a single 2D atlas
//    instead would work in GLSL1 but bleeds neighbouring tiles into each other
//    at mip levels, which shows as colour fringing exactly at chunk transitions.
//    So this is a ShaderMaterial at GLSL3 with the lighting model from
//    terrain.frag, and its light uniforms are driven from the scene's own
//    LightingRig so it tracks whatever the world is lit by.
//
// If any of this looks wrong, Options -> Terrain Splat turns it off and the
// baked composite comes straight back.

import * as THREE from 'three';

/** Texture repeats per 33.3-yard chunk. MSUIClient TerrainTile; vanilla ~8. */
export const TEXTURE_SCALE = 8.0;
const CHUNKS_PER_SIDE = 16;

let splatEnabled = true;
export function setSplatEnabled(v) { splatEnabled = !!v; }
export function isSplatEnabled() { return splatEnabled; }

// ─────────────────────────────────────────────────────────────────────────────
// Decoding
// ─────────────────────────────────────────────────────────────────────────────

function decodeToRgba(base64, expectW, expectH) {
    return new Promise((resolve, reject) => {
        const img = new Image();
        img.onload = () => {
            try {
                const w = expectW || img.width, h = expectH || img.height;
                const cv = document.createElement('canvas');
                cv.width = w; cv.height = h;
                const ctx = cv.getContext('2d', { willReadFrequently: true });
                ctx.drawImage(img, 0, 0, w, h);
                resolve({ data: new Uint8Array(ctx.getImageData(0, 0, w, h).data.buffer), w, h });
            } catch (e) { reject(e); }
        };
        img.onerror = () => reject(new Error('image decode failed'));
        img.src = 'data:image/png;base64,' + base64;
    });
}

/**
 * Build the three GPU resources for one tile.
 * Returns null when the tile has no usable tileset, so the caller can fall back
 * to the composite rather than rendering an untextured plane.
 */
export async function buildSplatResources(data, maxAniso) {
    if (!data || !data.success) return null;
    if (!data.texturesBase64 || data.texturesBase64.length === 0) return null;
    if (!data.alphaAtlasBase64) return null;

    const w = data.textureWidth, h = data.textureHeight;
    const count = data.texturesBase64.length;

    // ── Tileset -> DataArrayTexture ─────────────────────────────────────────
    const layers = await Promise.all(
        data.texturesBase64.map((b64) => decodeToRgba(b64, w, h)));

    const packed = new Uint8Array(w * h * 4 * count);
    for (let i = 0; i < count; i++) packed.set(layers[i].data, i * w * h * 4);

    const tileset = new THREE.DataArrayTexture(packed, w, h, count);
    tileset.format = THREE.RGBAFormat;
    tileset.type = THREE.UnsignedByteType;
    tileset.colorSpace = THREE.SRGBColorSpace;
    tileset.wrapS = tileset.wrapT = THREE.RepeatWrapping;
    tileset.magFilter = THREE.LinearFilter;
    tileset.minFilter = THREE.LinearMipmapLinearFilter;
    tileset.generateMipmaps = true;
    tileset.anisotropy = maxAniso || 1;
    tileset.needsUpdate = true;

    // ── Alpha atlas ─────────────────────────────────────────────────────────
    // flipY = false so texel row 0 is chunk IndexY 0, matching how the server
    // packs it (ay = cy * 64 + py) and how BuildCompositeTexture lays out its
    // pixels (pixOffY = cy * pixelsPerChunk). The shader flips V once, in one
    // place, rather than every texture guessing.
    const atlasPix = await decodeToRgba(data.alphaAtlasBase64,
        data.alphaAtlasSize, data.alphaAtlasSize);
    const alphaAtlas = new THREE.DataTexture(
        atlasPix.data, atlasPix.w, atlasPix.h, THREE.RGBAFormat, THREE.UnsignedByteType);
    alphaAtlas.colorSpace = THREE.NoColorSpace;   // this is DATA, not colour
    alphaAtlas.wrapS = alphaAtlas.wrapT = THREE.ClampToEdgeWrapping;
    alphaAtlas.magFilter = THREE.LinearFilter;
    alphaAtlas.minFilter = THREE.LinearFilter;    // no mips: masks must not bleed
    alphaAtlas.generateMipmaps = false;
    alphaAtlas.needsUpdate = true;

    // ── Per-chunk layer indices -> 16x16 lookup ─────────────────────────────
    // One texel per chunk, RGBA = the four array-layer indices. 255 = unused,
    // which the shader treats as "no layer" exactly like MSUIClient's -1.
    const lut = new Uint8Array(CHUNKS_PER_SIDE * CHUNKS_PER_SIDE * 4);
    lut.fill(255);
    const cl = data.chunkLayers || [];
    for (let i = 0; i < CHUNKS_PER_SIDE * CHUNKS_PER_SIDE; i++) {
        for (let li = 0; li < 4; li++) {
            const v = cl[i * 4 + li];
            lut[i * 4 + li] = (v === undefined || v === null || v < 0) ? 255 : (v & 0xff);
        }
    }
    const chunkLut = new THREE.DataTexture(
        lut, CHUNKS_PER_SIDE, CHUNKS_PER_SIDE, THREE.RGBAFormat, THREE.UnsignedByteType);
    chunkLut.colorSpace = THREE.NoColorSpace;
    chunkLut.magFilter = THREE.NearestFilter;     // indices must never interpolate
    chunkLut.minFilter = THREE.NearestFilter;
    chunkLut.generateMipmaps = false;
    chunkLut.needsUpdate = true;

    return { tileset, alphaAtlas, chunkLut, textureCount: count };
}

// ─────────────────────────────────────────────────────────────────────────────
// Material
// ─────────────────────────────────────────────────────────────────────────────

const VERT = /* glsl */`
out vec3 vWorldPos;
out vec3 vNormal;
out vec2 vTileUV;

void main() {
    vec4 wp = modelMatrix * vec4(position, 1.0);
    vWorldPos = wp.xyz;
    vNormal = normalize(mat3(modelMatrix) * normal);
    vTileUV = uv;
    gl_Position = projectionMatrix * viewMatrix * wp;
}
`;

// Port of MSUIClient Shaders/terrain.frag. The blend order, the UV derivation
// and the ambient hemisphere term are that file's; the fog is three.js linear
// fog so terrain fades into the same horizon as everything else.
const FRAG = /* glsl */`
precision highp float;
precision highp sampler2DArray;

in vec3 vWorldPos;
in vec3 vNormal;
in vec2 vTileUV;

uniform sampler2DArray uTileset;
uniform sampler2D uAlphaAtlas;
uniform sampler2D uChunkLut;

uniform vec3  uSunDirection;
uniform vec3  uSunColor;
uniform float uSunIntensity;
uniform vec3  uAmbientColor;
uniform float uAmbientIntensity;
uniform vec3  uFogColor;
uniform float uFogNear;
uniform float uFogFar;
uniform float uTextureScale;
uniform float uChunksPerSide;
uniform int   uDebugMode;   // 0 textured, 1 normals, 2 chunk UVs, 3 splat mask

out vec4 outColor;

const vec3 UP = vec3(0.0, 1.0, 0.0);   // three.js is Y-up; MSUIClient is Z-up

vec3 sampleLayer(float idx, vec2 uv) {
    if (idx > 254.5) return vec3(0.0);
    return texture(uTileset, vec3(uv, idx)).rgb;
}

void main() {
    vec3 n = normalize(vNormal);

    // The mesh UV runs v = 1 - row/(h-1); the atlas and the LUT are packed with
    // row 0 = chunk IndexY 0. Flip once, here, and every sampler below agrees.
    vec2 auv = vec2(vTileUV.x, 1.0 - vTileUV.y);

    vec2 chunkUV = auv * uChunksPerSide;
    vec2 texUV   = fract(chunkUV) * uTextureScale;

    // Nearest-sampled at the centre of the chunk's texel: no interpolation
    // between neighbouring chunks' layer indices.
    vec2 lutUV = (floor(chunkUV) + 0.5) / uChunksPerSide;
    vec4 L = texture(uChunkLut, lutUV) * 255.0;

    vec3 splat = texture(uAlphaAtlas, auv).rgb;

    if (uDebugMode == 1) { outColor = vec4(n * 0.5 + 0.5, 1.0); return; }
    if (uDebugMode == 2) { outColor = vec4(fract(chunkUV), 0.0, 1.0); return; }
    if (uDebugMode == 3) { outColor = vec4(splat, 1.0); return; }

    vec3 albedo;
    if (L.x > 254.5) {
        // No base layer for this chunk. Neutral rather than black, so a data
        // gap reads as "missing texture" and not as a hole in the world.
        albedo = vec3(0.34, 0.40, 0.24);
    } else {
        // Base, then overlays in order. Each covers what is under it by its own
        // alpha — the same paint-on-top order the client uses.
        albedo = sampleLayer(L.x, texUV);
        albedo = mix(albedo, sampleLayer(L.y, texUV), L.y > 254.5 ? 0.0 : splat.r);
        albedo = mix(albedo, sampleLayer(L.z, texUV), L.z > 254.5 ? 0.0 : splat.g);
        albedo = mix(albedo, sampleLayer(L.w, texUV), L.w > 254.5 ? 0.0 : splat.b);
    }

    float ndl = max(dot(n, normalize(uSunDirection)), 0.0);
    vec3 sun = uSunColor * ndl * uSunIntensity;
    vec3 ambient = uAmbientColor * uAmbientIntensity
        * mix(0.62, 1.0, dot(n, UP) * 0.5 + 0.5);

    vec3 color = albedo * (sun + ambient);

    float dist = length(vWorldPos - cameraPosition);
    float fog = clamp((dist - uFogNear) / max(uFogFar - uFogNear, 1.0), 0.0, 1.0);
    color = mix(color, uFogColor, fog);

    outColor = vec4(color, 1.0);
}
`;

// ═══════════════════════════════════════════════════════════════════════════
// ONE LIGHTING MODEL FOR THE WHOLE WORLD
// ═══════════════════════════════════════════════════════════════════════════
// MSUIClient states the rule in Shaders/character.frag: that file is wmo.frag
// with one change, and the header explains why the rest is byte-identical —
// "a character that lights differently from the ground he stands on looks wrong
// in a way that is hard to name and easy to avoid". terrain.frag uses the same
// sun + ambient terms as both.
//
// Giving terrain its own ShaderMaterial broke that here: the ground moved to
// this model while the character stayed on three.js's MeshStandardMaterial with
// a rig that sums past 1.0. The character went pale against a correctly-lit
// world — not because the character changed, but because the ground did.
//
// So these two functions are the single source of truth, exported and consumed
// by BOTH the splat shader and the standard-material override in world-light.js.
// The first version of this file scaled the rig by 0.35 and 0.85 to make the
// splat look right on its own. Those numbers were invented and they are what
// put the character out of step; they are gone.
export function worldSunIntensity(rig) {
    // The rig's DirectionalLight is 3.5 in three.js' physically-corrected units
    // (useLegacyLights = false). The simple lambert term here is not physically
    // corrected, so it consumes the same light at unit scale.
    return rig && rig.sun ? Math.min(rig.sun.intensity / 3.5, 1.0) : 1.0;
}

export function worldAmbientIntensity(rig) {
    // Ambient plus the hemisphere fill, which the simple model folds into one
    // term via the n.y hemisphere factor in the shader.
    const a = rig && rig.ambient ? rig.ambient.intensity : 0.9;
    const h = rig && rig.hemi ? rig.hemi.intensity : 0.8;
    return (a + h) * 0.5;
}

export function worldSunDirection(rig, out) {
    const v = out || new THREE.Vector3();
    if (rig && rig.sun && rig.sun.position) v.copy(rig.sun.position).normalize();
    else v.set(-100, 28, 50).normalize();
    return v;
}

/**
 * @param {object} res    buildSplatResources() output
 * @param {object} rig    LightingRig, for sun/ambient
 * @param {THREE.Fog} fog scene fog
 */
export function makeSplatMaterial(res, rig, fog) {
    const sun = rig && rig.sun;
    const amb = rig && rig.ambient;

    const sunDir = new THREE.Vector3(-100, 28, 50).normalize();
    if (sun && sun.position) sunDir.copy(sun.position).normalize();

    const mat = new THREE.ShaderMaterial({
        glslVersion: THREE.GLSL3,
        vertexShader: VERT,
        fragmentShader: FRAG,
        side: THREE.FrontSide,
        uniforms: {
            uTileset: { value: res.tileset },
            uAlphaAtlas: { value: res.alphaAtlas },
            uChunkLut: { value: res.chunkLut },
            uSunDirection: { value: sunDir },
            uSunColor: { value: new THREE.Color(sun ? sun.color : 0xffbb55) },
            uSunIntensity: { value: worldSunIntensity(rig) },
            uAmbientColor: { value: new THREE.Color(amb ? amb.color : 0xffe8c8) },
            uAmbientIntensity: { value: worldAmbientIntensity(rig) },
            uFogColor: { value: new THREE.Color(fog ? fog.color : 0xc49a50) },
            uFogNear: { value: fog ? fog.near : 200 },
            uFogFar: { value: fog ? fog.far : 900 },
            uTextureScale: { value: TEXTURE_SCALE },
            uChunksPerSide: { value: CHUNKS_PER_SIDE },
            uDebugMode: { value: 0 },
        },
    });
    mat.userData.isSplat = true;
    return mat;
}

/** Keep every live splat material tracking the scene's fog and lights. */
export function syncSplatUniforms(materials, rig, fog) {
    for (const m of materials) {
        if (!m || !m.uniforms) continue;
        if (fog) {
            m.uniforms.uFogColor.value.copy(fog.color);
            m.uniforms.uFogNear.value = fog.near;
            m.uniforms.uFogFar.value = fog.far;
        }
        if (rig && rig.sun) {
            m.uniforms.uSunDirection.value.copy(rig.sun.position).normalize();
            m.uniforms.uSunColor.value.copy(rig.sun.color);
            m.uniforms.uSunIntensity.value = worldSunIntensity(rig);
        }
        if (rig && rig.ambient) {
            m.uniforms.uAmbientColor.value.copy(rig.ambient.color);
            m.uniforms.uAmbientIntensity.value = worldAmbientIntensity(rig);
        }
    }
}

export function disposeSplatResources(res) {
    if (!res) return;
    if (res.tileset) res.tileset.dispose();
    if (res.alphaAtlas) res.alphaAtlas.dispose();
    if (res.chunkLut) res.chunkLut.dispose();
}
