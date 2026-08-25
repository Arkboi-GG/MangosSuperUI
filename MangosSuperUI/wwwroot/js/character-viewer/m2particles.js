// Character Viewer — M2 particle emitters.
//
// === What this is ===
//
// A re-simulation of an M2's particle emitters, driven by the descriptors the
// server ships in the GLB's `extras.suiFx.emitters` (see
// Services/M2Fx/M2FxManifest.cs). Nothing in the browser drew particles before
// this: a forged weapon's flame was visible in the game client and simply absent
// from every preview surface.
//
// === Why re-simulate rather than bake ===
//
// A particle effect is a RATE, not a picture. The forge learned that expensively
// on Worldbreaker (ARMOR_FORGE.md §8c): the rebuild carried position, colour and
// size and STILL read as a strobe, because the two numbers that decide whether
// particles overlap — emission rate and lifespan — had been dropped along the
// way. A baked snapshot of a flame is a decal. So the client gets the same
// numbers the client-side game engine gets and runs the same loop.
//
// === Model ===
//
// One THREE.Mesh per emitter, holding a pool of camera-facing quads in ONE
// buffer geometry — four vertices and two triangles per particle, rewritten each
// frame. That is the shape spells/visuallab.js already proved out on the spell
// previewer; this is an ES-module rewrite of it with the M2 semantics corrected
// (see "What is different" below).
//
// Simulation happens in the emitter's LOCAL space and the mesh is parented to the
// model root, so a weapon's flame rides the hand bone for free. Real WoW
// simulates in world space, which matters for a sprinting character trailing
// smoke and does not for an object being turned on a preview turntable.
//
// === What is different from the spell-lab version, and why ===
//
//  * Emission direction. WoW spreads particles around the emitter's OWN up axis
//    (WoW +Z, which M2Reader's conversion makes +Y here): `verticalRange` is the
//    cone half-angle away from that axis and `horizontalRange` is the azimuth
//    sweep around it. The old code built the direction around +Z with the
//    vertical term as a plain sine, so a flame with verticalRange 0 came out
//    drifting sideways instead of rising.
//  * Flipbook cells. A particle walks `head` cells over its head phase and
//    `tail` cells over its decay phase, both shipped per emitter. The old code
//    picked one cell at random per particle and held it, which reads as static
//    noise rather than an animating flame.
//  * Ramps honour the emitter's own `midpoint` instead of assuming 0.5, and
//    alpha is a curve of its own rather than a channel of the colour.

import * as THREE from 'three';

/** Hard ceiling per emitter. Vanilla item emitters sit well under this; the cap
 *  exists so a malformed rate cannot allocate a hundred megabytes of buffers. */
const MAX_PARTICLES = 2000;

/**
 * Build particle systems for every emitter in the manifest and parent them under
 * the model.
 *
 * @param {object} gltf      GLTFLoader result (used for texture resolution).
 * @param {object} manifest  The parsed `suiFx` object.
 * @param {THREE.Object3D} root  Node the systems are parented to.
 * @returns {Promise<{update:(ms:number,dtMs:number,camera:THREE.Camera)=>void,
 *                    dispose:()=>void, emitterCount:number}|null>}
 */
export async function installM2Particles(gltf, manifest, root) {
    const defs = manifest?.emitters;
    if (!Array.isArray(defs) || defs.length === 0 || !root) return null;

    const systems = [];
    for (const def of defs) {
        let texture = null;
        try {
            texture = await gltf.parser.getDependency('texture', def.tex);
        } catch (err) {
            console.warn('[m2particles] emitter texture', def.tex, 'unavailable', err);
        }
        if (!texture) continue;

        const sys = createEmitterSystem(def, texture);
        if (sys) { root.add(sys.mesh); systems.push(sys); }
    }

    if (systems.length === 0) return null;

    return {
        emitterCount: systems.length,
        update(ms, dtMs, camera) {
            if (!camera) return;
            // Clamp the step so a backgrounded tab does not come back and emit a
            // minute's worth of particles into one frame.
            const dt = Math.min(Math.max(dtMs, 0) / 1000, 0.1);
            for (const sys of systems) sys.update(dt, camera);
        },
        dispose() {
            for (const sys of systems) sys.dispose();
            systems.length = 0;
        },
    };
}

/** The M2 blend modes an emitter actually uses, mapped to three.js. */
function blendingFor(mode) {
    switch (mode) {
        case 3:
        case 4: return THREE.AdditiveBlending;   // additive / add-alpha — most flames and glows
        case 5: return THREE.MultiplyBlending;   // modulate
        default: return THREE.NormalBlending;    // opaque / alpha-key / alpha-blend
    }
}

function createEmitterSystem(def, texture) {
    const rate = def.rate > 0 ? def.rate : 0;
    const life = def.life > 0 ? def.life : 0;
    if (rate <= 0 || life <= 0) return null;

    // Pool for the steady state plus headroom for the variance in spawn timing.
    const capacity = Math.max(4, Math.min(Math.ceil(rate * life * 1.5) + 4, MAX_PARTICLES));

    const rows = Math.max(1, def.rows | 0);
    const cols = Math.max(1, def.cols | 0);
    const cellCount = rows * cols;
    const cellW = 1 / cols;
    const cellH = 1 / rows;

    const positions = new Float32Array(capacity * 4 * 3);
    const uvs = new Float32Array(capacity * 4 * 2);
    const colors = new Float32Array(capacity * 4 * 4);
    const indices = new Uint32Array(capacity * 6);
    for (let i = 0; i < capacity; i++) {
        const v = i * 4, o = i * 6;
        indices[o] = v; indices[o + 1] = v + 1; indices[o + 2] = v + 2;
        indices[o + 3] = v; indices[o + 4] = v + 2; indices[o + 5] = v + 3;
    }

    const geometry = new THREE.BufferGeometry();
    geometry.setAttribute('position', new THREE.BufferAttribute(positions, 3));
    geometry.setAttribute('uv', new THREE.BufferAttribute(uvs, 2));
    geometry.setAttribute('color', new THREE.BufferAttribute(colors, 4));
    geometry.setIndex(new THREE.BufferAttribute(indices, 1));

    // The sheet is shared with whatever else the GLB bound it to, and a flipbook
    // walks sub-rectangles of it, so give this system its own THREE.Texture view.
    const map = texture.clone();
    map.needsUpdate = true;

    const material = new THREE.MeshBasicMaterial({
        map,
        vertexColors: true,
        transparent: true,
        blending: blendingFor(def.blend),
        depthWrite: false,
        side: THREE.DoubleSide,
        // Particle sheets are alpha-masked art; without this the transparent
        // border of every quad still writes a depth-sorted fragment and the
        // sprites cut visible squares out of each other.
        alphaTest: def.blend === 3 || def.blend === 4 ? 0 : 0.05,
    });

    const mesh = new THREE.Mesh(geometry, material);
    mesh.name = 'M2Emitter';
    // Particles live in the emitter's local space and routinely leave the model's
    // bounds; culling against the mesh's own (permanently stale) bounding sphere
    // makes the whole system blink out at glancing angles.
    mesh.frustumCulled = false;
    mesh.renderOrder = 10;

    const pool = new Array(capacity);
    for (let i = 0; i < capacity; i++) {
        pool[i] = { alive: false, age: 0, x: 0, y: 0, z: 0, vx: 0, vy: 0, vz: 0 };
    }

    const origin = Array.isArray(def.pos) ? def.pos : [0, 0, 0];
    const mid = def.mid > 0 && def.mid < 1 ? def.mid : 0.5;
    const scaleRamp = def.scale || [1, 1, 1];
    const colorRamp = def.color || [[1, 1, 1], [1, 1, 1], [1, 1, 1]];
    const alphaRamp = def.alpha || [1, 1, 1];
    const head = def.head || [0, 0, 1];
    const tail = def.tail || [0, 0, 1];

    let spawnAccumulator = 0;
    let nextSlot = 0;

    const camRight = new THREE.Vector3();
    const camUp = new THREE.Vector3();
    const camFwd = new THREE.Vector3();

    function spawn() {
        // Round-robin rather than first-free: with a full pool that keeps the
        // oldest particle being the one recycled, so the effect thins evenly
        // instead of stuttering at one end of the buffer.
        const p = pool[nextSlot];
        nextSlot = (nextSlot + 1) % capacity;

        p.alive = true;
        p.age = 0;

        // Spawn area. Type 1 is a plane (area L x W in the emitter's own plane),
        // type 2 a sphere of that radius; anything else is a point source.
        let ox = 0, oy = 0, oz = 0;
        const areaL = def.areaL || 0, areaW = def.areaW || 0;
        if (def.type === 2) {
            const radius = Math.max(areaL, areaW);
            const theta = Math.random() * Math.PI * 2;
            const phi = Math.acos(2 * Math.random() - 1);
            ox = radius * Math.sin(phi) * Math.cos(theta);
            oz = radius * Math.sin(phi) * Math.sin(theta);
            oy = radius * Math.cos(phi);
        } else if (areaL > 0 || areaW > 0) {
            ox = (Math.random() - 0.5) * areaL;
            oz = (Math.random() - 0.5) * areaW;
        }
        p.x = origin[0] + ox;
        p.y = origin[1] + oy;
        p.z = origin[2] + oz;

        // Direction: tilt away from the emitter's up axis (+Y here) by up to
        // verticalRange, then sweep around it by horizontalRange. With
        // verticalRange 0 that is straight up, which is what a brazier does.
        const speed = (def.speed || 0) * (1 + (Math.random() * 2 - 1) * (def.speedVar || 0));
        const polar = (def.vRange || 0) * Math.random();
        const azimuth = (def.hRange || 0) * (Math.random() - 0.5);
        const sinP = Math.sin(polar), cosP = Math.cos(polar);
        p.vx = speed * sinP * Math.cos(azimuth);
        p.vy = speed * cosP;
        p.vz = speed * sinP * Math.sin(azimuth);
    }

    function update(dt, camera) {
        // Emit. The accumulator carries the fractional remainder so a 6/second
        // emitter does not round to zero at 60fps and never fire.
        spawnAccumulator += rate * dt;
        let budget = capacity;
        while (spawnAccumulator >= 1 && budget-- > 0) {
            spawnAccumulator -= 1;
            spawn();
        }

        camera.matrixWorld.extractBasis(camRight, camUp, camFwd);

        const pos = geometry.attributes.position.array;
        const uv = geometry.attributes.uv.array;
        const col = geometry.attributes.color.array;
        const gravity = def.gravity || 0;
        let live = 0;

        for (let i = 0; i < capacity; i++) {
            const p = pool[i];
            const vi = i * 12, ti = i * 8, ci = i * 16;

            if (p.alive) {
                p.age += dt;
                if (p.age >= life) p.alive = false;
            }
            if (!p.alive) {
                // Collapse the quad to a point AND zero its alpha: either alone
                // still costs a degenerate draw, both together cost nothing
                // visible.
                for (let z = 0; z < 12; z++) pos[vi + z] = 0;
                for (let z = 0; z < 16; z++) col[ci + z] = 0;
                continue;
            }
            live++;

            // WoW gravity pulls along the emitter's down axis.
            p.vy -= gravity * dt;
            p.x += p.vx * dt;
            p.y += p.vy * dt;
            p.z += p.vz * dt;

            const t = p.age / life;
            let f, scale, r, g, b, a, cells, cellIndex;
            if (t < mid) {
                f = mid > 0 ? t / mid : 0;
                scale = lerp(scaleRamp[0], scaleRamp[1], f);
                r = lerp(colorRamp[0][0], colorRamp[1][0], f);
                g = lerp(colorRamp[0][1], colorRamp[1][1], f);
                b = lerp(colorRamp[0][2], colorRamp[1][2], f);
                a = lerp(alphaRamp[0], alphaRamp[1], f);
                cells = head;
            } else {
                f = mid < 1 ? (t - mid) / (1 - mid) : 1;
                scale = lerp(scaleRamp[1], scaleRamp[2], f);
                r = lerp(colorRamp[1][0], colorRamp[2][0], f);
                g = lerp(colorRamp[1][1], colorRamp[2][1], f);
                b = lerp(colorRamp[1][2], colorRamp[2][2], f);
                a = lerp(alphaRamp[1], alphaRamp[2], f);
                cells = tail;
            }

            // Live glow override — the same transform the forge BAKES: a tint replaces the
            // colour ramp (the bake writes one flat colour over the emitter's colour keys),
            // intensity scales the colour (additive particles render colour as brightness),
            // and a dim also shrinks the particle. Applied here, at ramp-evaluation time, so
            // the preview matches the committed result instead of multiplying on top of it.
            const ov = material.userData && material.userData.suiGlowOverride;
            if (ov) {
                if (ov.tint) { r = ov.tint[0]; g = ov.tint[1]; b = ov.tint[2]; }
                if (ov.intensity != null) { r *= ov.intensity; g *= ov.intensity; b *= ov.intensity; }
                if (ov.sizeMul != null) scale *= ov.sizeMul;
            }

            // Billboard: a camera-facing quad of side `scale`, centred on the
            // particle. Half-extents along the camera's right and up axes.
            const half = scale * 0.5;
            const rx = camRight.x * half, ry = camRight.y * half, rz = camRight.z * half;
            const ux = camUp.x * half, uy = camUp.y * half, uz = camUp.z * half;

            pos[vi + 0] = p.x - rx - ux; pos[vi + 1] = p.y - ry - uy; pos[vi + 2] = p.z - rz - uz;
            pos[vi + 3] = p.x + rx - ux; pos[vi + 4] = p.y + ry - uy; pos[vi + 5] = p.z + rz - uz;
            pos[vi + 6] = p.x + rx + ux; pos[vi + 7] = p.y + ry + uy; pos[vi + 8] = p.z + rz + uz;
            pos[vi + 9] = p.x - rx + ux; pos[vi + 10] = p.y - ry + uy; pos[vi + 11] = p.z - rz + uz;

            if (cellCount > 1) {
                // Walk the phase's cell range as the particle ages, wrapping for
                // the repeat count. This is the flipbook actually playing.
                const span = cells[1] - cells[0] + 1;
                const step = Math.floor(f * span * (cells[2] || 1));
                cellIndex = cells[0] + (span > 0 ? step % span : 0);
                const cx = cellIndex % cols, cy = Math.floor(cellIndex / cols);
                const u0 = cx * cellW, u1 = u0 + cellW;
                // Sheets are authored top-left origin; glTF/three UVs are
                // bottom-left, so the row index counts down from the top.
                const v1 = 1 - cy * cellH, v0 = v1 - cellH;
                uv[ti + 0] = u0; uv[ti + 1] = v0;
                uv[ti + 2] = u1; uv[ti + 3] = v0;
                uv[ti + 4] = u1; uv[ti + 5] = v1;
                uv[ti + 6] = u0; uv[ti + 7] = v1;
            } else {
                uv[ti + 0] = 0; uv[ti + 1] = 0;
                uv[ti + 2] = 1; uv[ti + 3] = 0;
                uv[ti + 4] = 1; uv[ti + 5] = 1;
                uv[ti + 6] = 0; uv[ti + 7] = 1;
            }

            for (let v = 0; v < 4; v++) {
                col[ci + v * 4 + 0] = r;
                col[ci + v * 4 + 1] = g;
                col[ci + v * 4 + 2] = b;
                col[ci + v * 4 + 3] = a;
            }
        }

        geometry.attributes.position.needsUpdate = true;
        geometry.attributes.uv.needsUpdate = true;
        geometry.attributes.color.needsUpdate = true;
        mesh.visible = live > 0;
    }

    return {
        mesh,
        update,
        dispose() {
            mesh.removeFromParent();
            geometry.dispose();
            material.dispose();
            map.dispose();
        },
    };
}

function lerp(a, b, f) {
    return a + (b - a) * (f < 0 ? 0 : f > 1 ? 1 : f);
}
