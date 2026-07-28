// character.js — the player character you walk around with.  (M4.1)
//
// This module deliberately owns almost no new engineering. MangosSuperUI
// already ships a working skinned-character pipeline in
// wwwroot/js/character-viewer/:
//
//   loader.js   loadCharacterGlb(url) → { root, bones, geosets, animations, ... }
//               with geoset metadata parsed off the mesh names
//               ("Geoset_<id>_c<cat>_v<var>_s<sub>") into userData.
//   dresser.js  showDefaultGeosets(character) → the naked baseline.
//   equip.js    unequipAll(character, { baseSkin }) → paints the body atlas.
//
// Those are reused verbatim. What is NEW here is everything the *viewer*
// never needed, because a viewer's character stands on a turntable:
//
//   - putting the character in the world at true world height (M1.1)
//   - a locomotion state machine driven by MEASURED DISPLACEMENT
//   - the heading convention that makes it face where it is going
//
// ─────────────────────────────────────────────────────────────────────────
// HEADING — why +90 degrees
// ─────────────────────────────────────────────────────────────────────────
// M2Reader converts M2 render vertices to glTF Y-up at parse:
//     (x, y, z)_m2  ->  (x, z, -y)_gltf
// In M2/WoW space the model's forward is +X, so in the GLB the model's
// forward is also +X.
//
// A three.js Object3D with rotation.y = phi maps its local +X to world
//     (cos phi, 0, -sin phi)
// and we want that to equal the desired forward (fx, 0, fz) where
// fx = sin(yaw), fz = cos(yaw). Solving:
//     cos phi = sin(yaw)  and  sin phi = -cos(yaw)   ->   phi = yaw - PI/2
//
// CORRECTION (2026-07-26): the first version of this used yaw + PI/2 and the
// character stood facing the camera — exactly 180 degrees wrong, because
// +PI/2 and -PI/2 differ by a half turn.
//
// The error was not just algebra. MSUIClient records heading = "Yaw + 90
// degrees", confirmed on screen against the real client, and seeing my sign
// slip produce the same number 90 read as corroboration. It was not: MSUIClient
// works in WoW space (X north, Y west, Z up) and that +90 does not transfer to
// a Y-up glTF basis unexamined. A matching magnitude is not a matching
// derivation.
//
// ─────────────────────────────────────────────────────────────────────────
// SPEED — measured, not requested
// ─────────────────────────────────────────────────────────────────────────
// Ground speed comes from how far the character ACTUALLY moved this frame,
// never from the input vector. Two things fall out for free:
//   - walking into a wall slows the animation instead of running on the spot
//   - strafing needs no special case; the angle comes from the displacement
// (M4.2 uses that second property for the torso split.)

import * as THREE from 'three';
import { loadCharacterGlb } from '../character-viewer/loader.js';
import { showDefaultGeosets } from '../character-viewer/dresser.js';
import { unequipAll, equipMultiple, clearSkinCache } from '../character-viewer/equip.js';
import { applyWorldLighting } from './world-light.js';

// ─────────────────────────────────────────────────────────────────────────
// Constants
// ─────────────────────────────────────────────────────────────────────────

// Authored clip speeds, yards/second (MSUIClient MovementConfig). The Walk and
// Run clips were animated for these; playback rate is scaled off them so the
// feet track the ground instead of skating.
const WALK_SPEED = 2.5;
const RUN_SPEED = 7.0;

// Below this the character is Standing. NO GRACE TIMER — MSUIClient tried
// WoWee's kGraceSec hold-open and rejected it outright ("that delay you added
// is awful. The sharper stop was infinitely better"). Do not reintroduce one.
const MOVE_THRESHOLD = 0.15;   // yd/s

// Walk below this, Run above. Midway between the two authored speeds.
const WALK_RUN_SPLIT = (WALK_SPEED + RUN_SPEED) * 0.5;

// Asymmetric smoothing: accelerating is eased, stopping is instant.
const SPEED_SMOOTH_RATE = 12.0;  // 1/s

// Clip crossfade. Short enough that Stand->Run reads as a start, long enough
// that it isn't a pop.
const CROSSFADE = 0.15;         // s

// Playback-rate clamp. A judgement call, not ground truth: without a ceiling a
// downhill sprint turns the run cycle into a blur, and without a floor a slow
// creep freezes it.
const RATE_MIN = 0.5;
const RATE_MAX = 2.2;


// Animation ids we care about here (AnimationData.dbc, vanilla 1.12).
// SkinnedGlbWriter bakes 0/4/5/13/37/38/39/40 (DefaultAnimationsToBake).
const ANIM = {
    STAND: 0, WALK: 4, RUN: 5, WALK_BACK: 13,
    JUMP_START: 37, JUMP: 38, JUMP_END: 39, FALL: 40,
};

// ── Strafe torso split (MSUIClient CharacterRenderer.StrafeStyle.Split) ──────
// The legs take the FULL strafe angle by turning the whole model; the torso is
// pulled back by the remainder so it only follows part way. TorsoFollow 0.66 is
// Nico's eyeball match of the real client (legs ~90 deg, torso ~60). The twist
// applied to the SpineLow bone is therefore (TorsoFollow - 1) * moveYaw.
const TORSO_FOLLOW = 0.66;
// Past ~110 deg the character is going backwards: swap to Walkbackwards and take
// the angle off what is LEFT after the half turn (CharacterRenderer.cs 1589).
const STRAFE_BACKWARD = 1.92;   // rad
// _moveYaw easing (CharacterRenderer.Update: blend = 1 - exp(-dt*14)).
const MOVE_YAW_RATE = 14.0;
// Motion-direction smoothing for forwardness/sideness (MeasureMotion: dt*12).
const MOTION_SMOOTH_RATE = 12.0;
// Key-bone ids carried in the GLB bone names as "_k<id>" (SkinnedGlbWriter).
const KEY_BONE_SPINE_LOW = 4;   // torso subtree root
const KEY_BONE_WAIST = 5;       // legs subtree root — fallback if SpineLow absent

// Starter clothes. showDefaultGeosets() gives the NAKED baseline (base body,
// one hair variant, default underwear) — correct, but not what you want to look
// at while walking around. Same two pieces the items page dresses its mannequin
// in, so the resolution path is already proven.
//   Recruit's Shirt  itemId 38, displayId 9891, inventoryType 4
//   Recruit's Pants  itemId 39, displayId 9892, inventoryType 7
const STARTER_OUTFIT = [
    { displayId: 9891, itemId: 38, inventoryType: 4 },
    { displayId: 9892, itemId: 39, inventoryType: 7 },
];

// Clip names as SkinnedGlbWriter emits them.
// Names as SkinnedGlbWriter emits them (its AnimationName switch).
const CLIP_NAMES = {
    [ANIM.STAND]: 'Stand',
    [ANIM.WALK]: 'Walk',
    [ANIM.RUN]: 'Run',
    [ANIM.WALK_BACK]: 'Walkbackwards',
    [ANIM.JUMP_START]: 'JumpStart',
    [ANIM.JUMP]: 'Jump',
    [ANIM.JUMP_END]: 'JumpEnd',
    [ANIM.FALL]: 'Fall',
};

// JumpStart and JumpEnd are the ONLY one-shots. M2Sequence's 0x20 flag is
// not a loop flag — it reads clear on Stand/Walk/Run — so this is hardcoded
// rather than derived. Jump(38) and Fall(40) loop while airborne.
const ONE_SHOT = new Set([ANIM.JUMP_START, ANIM.JUMP_END]);

// ─────────────────────────────────────────────────────────────────────────
// Model URL lookup
// ─────────────────────────────────────────────────────────────────────────
//
// There is no JSON endpoint for this. /Items/CharacterPreview returns HTML
// carrying data-glb-url / data-skin-url, and both URLs are version-stamped by
// CacheVersionRegistry, so they CANNOT be constructed client-side — the stamp
// is the whole point of the cache invalidation. items-character-panel.js
// scrapes the same two attributes; this mirrors it deliberately rather than
// inventing a second convention.
//
// If a JSON endpoint ever lands, this is the only function that changes.

export async function fetchCharacterUrls(race, gender) {
    const url = '/Items/CharacterPreview?race=' + encodeURIComponent(race) +
        '&gender=' + encodeURIComponent(gender);
    const res = await fetch(url, { credentials: 'same-origin' });
    if (!res.ok) throw new Error('CharacterPreview ' + res.status);

    const html = await res.text();
    const glbMatch = /data-glb-url="([^"]+)"/.exec(html);
    const skinMatch = /data-skin-url="([^"]+)"/.exec(html);

    if (!glbMatch) {
        // The view emits an error div and NO canvas when GLB generation fails,
        // so a missing attribute means the server could not build the model —
        // surface that rather than failing later on an undefined URL.
        throw new Error('No character GLB for ' + race + ' ' + gender +
            ' — the server could not generate it (check its log).');
    }
    return { glbUrl: glbMatch[1], skinUrl: skinMatch ? skinMatch[1] : null };
}

// ─────────────────────────────────────────────────────────────────────────
// PlayerCharacter
// ─────────────────────────────────────────────────────────────────────────

export class PlayerCharacter {
    constructor(editor) {
        this.editor = editor;

        this.character = null;   // loader.js result
        this.root = null;        // THREE.Object3D added to the scene
        this.mixer = null;
        this.actions = {};       // animId -> AnimationAction
        this.currentAnim = null;

        this.race = 'Human';
        this.gender = 'Male';

        // World state
        this.position = new THREE.Vector3();
        this.heading = 0;        // radians, camera-style yaw (see file header)
        this.groundSpeed = 0;    // smoothed, yd/s
        this.rawSpeed = 0;       // this frame's measured speed, yd/s

        // ── Strafe torso split state ──
        this._moveYaw = 0;       // eased model turn toward travel direction
        this._forwardness = 0;   // smoothed dot(travelDir, facing)
        this._sideness = 0;      // smoothed dot(travelDir, right)
        this._torsoBone = null;  // SpineLow THREE.Bone, twisted back by the split
        this._twistQuat = new THREE.Quaternion();
        this._twistAxis = new THREE.Vector3();
        this._parentQuat = new THREE.Quaternion();

        // ── Air / jump state (set by the controller each frame) ──
        this._airborne = false;
        this._wasAirborne = false;
        this._airVelY = 0;

        this._baseSkin = null;   // decoded body-atlas bitmap; see load()
        this._prevPos = new THREE.Vector3();
        this._hasPrev = false;
        this._loading = false;
        this.visible = false;

        // Third-person framing. Modest numbers on purpose — this is the M4.1
        // "see yourself" camera, not the real orbit rig that lands in M4.2.
        this.clothed = true;
    }

    get isLoaded() { return this.root !== null; }

    /**
     * Load (or swap) the character model and add it to the editor scene.
     * Safe to call repeatedly; concurrent calls are ignored while one is in
     * flight.
     */
    async load(race, gender) {
        if (this._loading) return null;
        this._loading = true;
        try {
            this.race = race || this.race;
            this.gender = gender || this.gender;

            const { glbUrl, skinUrl } = await fetchCharacterUrls(this.race, this.gender);

            const scene = this.editor.viewport.scene;
            if (this.root) this.dispose();

            const character = await loadCharacterGlb(glbUrl);
            this.character = character;
            this.root = character.root;

            // Naked baseline: base body + one hair variant + default underwear.
            showDefaultGeosets(character);

            // Paint the body atlas. equip.js's unequipAll() with an explicit
            // baseSkin is the proven path — passing the bitmap directly avoids
            // its canvas.dataset.skinUrl lookup, which assumes the items page's
            // DOM and would silently miss here.
            //
            // THE BLANK-FACE / WASHED-OUT-LIMBS BUG (2026-07-26)
            //
            // equip.js's internal loadDefaultSkin() finds the skin by reading
            // document.getElementById('char-preview-canvas').dataset.skinUrl —
            // the items page's DOM, which does not exist here. Its fallback
            // derives an UNVERSIONED /character_textures/skin/{Key}Skin00_00.png,
            // and that 404s after a cache sweep because the real file carries a
            // CacheVersionRegistry stamp. It then returns null.
            //
            // The first version passed the bitmap to unequipAll() but NOT to
            // equipMultiple(), so dressing re-composited the atlas from a null
            // base: item layers painted, no body underneath. On screen that is a
            // blank white face and pale washed-out arms and legs with a
            // correctly-textured shirt and trousers.
            //
            // Fix: hold the decoded bitmap and hand it to EVERY call that
            // rebuilds the atlas. Never rely on equip.js finding it itself —
            // outside the items page, it cannot.
            if (skinUrl) {
                try {
                    // equip.js keeps a module-level singleton skin cache that
                    // remembers the FIRST base skin it ever saw. Without this
                    // clear, swapping race gives you the previous race's skin —
                    // the "Orc body, Human skin" bug from the items page.
                    clearSkinCache();
                    const res = await fetch(skinUrl);
                    if (!res.ok) throw new Error('skin ' + res.status + ' ' + skinUrl);
                    this._baseSkin = await createImageBitmap(await res.blob());
                    await unequipAll(character, { baseSkin: this._baseSkin });
                } catch (err) {
                    console.warn('[character] base skin failed, model will use ' +
                        'whatever the GLB shipped with:', err);
                }
            }

            // Dress. Failure here is cosmetic — an undressed character is still
            // a working character, so it must not abort the load.
            if (this.clothed) {
                try {
                    await equipMultiple(character, STARTER_OUTFIT,
                        { baseSkin: this._baseSkin });
                } catch (err) {
                    console.warn('[character] starter outfit failed, ' +
                        'character stays in underwear:', err);
                }
            }

            // Character GLBs are in yards already (a human male is ~2 units
            // tall), which is why M1.1 mattered: before 1:1 world scale the
            // model would have been the only correctly-scaled thing in a
            // world stretched up to 3.5x vertically.
            this.root.position.copy(this.position);
            this.root.rotation.y = this.heading - Math.PI / 2;
            this.root.visible = this.visible;
            scene.add(this.root);

            this._normalizeMaterials(character);
            this._bindAnimations(character);
            this._findStrafeBones();

            console.log('[character] loaded', this.race + this.gender,
                '-', character.geosetList.length, 'geosets,',
                character.animations.length, 'clips');
            return character;
        } finally {
            this._loading = false;
        }
    }

    /**
     * Make the character light the same way the world does.
     *
     * MSUIClient is explicit about this in Shaders/character.frag: it is
     * wmo.frag with ONE change (final alpha comes from the texture instead of a
     * hardcoded 1.0), and the header says why the rest is byte-identical —
     * "a character that lights differently from the ground he stands on looks
     * wrong in a way that is hard to name and easy to avoid". So the goal is
     * not a special character look; it is the SAME look as the buildings.
     *
     * Here that means the same material class the rest of the scene uses:
     * MeshStandardMaterial, metalness 0, roughness in the doodad range, fog on.
     *
     * Two notes on the traps in here:
     *
     * 1. glTF's default metallicFactor is 1.0. A GLB that omits
     *    pbrMetallicRoughness therefore arrives FULLY METALLIC, and a metal with
     *    no environment map has essentially no diffuse response — it goes dark
     *    and flat under analytic lights. Character skin is a dielectric.
     *
     * 2. THE BLACK CHARACTER. The previous version multiplied material.color by
     *    an exposure scale to stop bright skin clipping white. traverse() walks
     *    MESHES, but every body geoset samples the one body atlas, so
     *    GLTFLoader hands them all the SAME material instance — and the multiply
     *    landed on it once per mesh. Fourteen geosets is 0.62^14, about 0.0008.
     *    A silhouette.
     *
     *    It is not fixed by deduplicating. It is fixed by not doing it: scaling
     *    albedo to compensate for an over-bright rig is a per-object hack for a
     *    renderer-wide problem, and it breaks the "lights like the world" rule
     *    this function exists to keep. If the whole scene is over-exposed — and
     *    it is, the rig sums past 1.0 with NoToneMapping — the fix is tone
     *    mapping on the renderer, applied to everything at once.
     *
     * Every op below is idempotent, so a re-dress or a race swap cannot
     * compound it the way the multiply did.
     */
    _normalizeMaterials(character) {
        const seen = new Set();
        character.root.traverse((o) => {
            if (!o.isMesh && !o.isSkinnedMesh) return;
            const mats = Array.isArray(o.material) ? o.material : [o.material];
            for (const m of mats) {
                if (!m || seen.has(m)) continue;
                seen.add(m);
                if (m.metalness !== undefined) m.metalness = 0.0;
                if (m.roughness !== undefined) m.roughness = 0.7;   // doodad range
                m.fog = true;
                // Same lighting model as the terrain shader and, per MSUIClient's
                // character.frag, as the buildings. Without it the character keeps
                // three.js' accumulated rig while the ground uses the simple model,
                // and reads as a pale cut-out over a correctly-lit world.
                applyWorldLighting(m,
                    this.editor.viewport && this.editor.viewport.lighting);
                m.needsUpdate = true;
            }
        });
        console.log('[character] normalized', seen.size, 'unique material(s)');
    }

    _bindAnimations(character) {
        this.actions = {};
        this.currentAnim = null;
        this.mixer = null;

        if (!character.animations || character.animations.length === 0) {
            // Bind-pose only. Worth saying loudly — it looks like a rigging
            // bug and is actually a stale server cache.
            console.warn('[character] GLB has no baked clips — the character ' +
                'will slide around in bind pose. Bump ' +
                'CacheVersionRegistry.SkinnedGlbVersion and rebuild.');
            return;
        }

        this.mixer = new THREE.AnimationMixer(character.root);

        const byName = {};
        for (const clip of character.animations) byName[clip.name] = clip;

        for (const idStr of Object.keys(CLIP_NAMES)) {
            const id = parseInt(idStr, 10);
            const clip = byName[CLIP_NAMES[id]];
            if (!clip) continue;
            const action = this.mixer.clipAction(clip);
            // Everything here loops. NOTE for when JumpStart(37)/JumpEnd(39)
            // are baked: those two are the ONLY one-shots. M2Sequence's 0x20
            // flag is NOT a loop flag — it reads clear on Stand/Walk/Run, and
            // trusting it turns every clip into a one-shot that clamps and
            // holds. three.js loops by default, which is exactly why that bug
            // stayed hidden in the character viewer.
            if (ONE_SHOT.has(id)) {
                action.setLoop(THREE.LoopOnce, 1);
                action.clampWhenFinished = true;
            } else {
                action.setLoop(THREE.LoopRepeat, Infinity);
                action.clampWhenFinished = false;
            }
            this.actions[id] = action;
        }

        const missing = Object.keys(CLIP_NAMES)
            .filter((id) => !this.actions[id])
            .map((id) => CLIP_NAMES[id]);
        if (missing.length) {
            console.warn('[character] missing clips:', missing.join(', '),
                '— available:', character.animations.map((c) => c.name).join(', '));
        }

        this._play(ANIM.STAND, 0);
    }

    _play(animId, fade) {
        const next = this.actions[animId];
        if (!next || this.currentAnim === animId) return;

        const prev = this.actions[this.currentAnim];
        next.enabled = true;
        next.setEffectiveWeight(1);
        next.play();

        if (prev && fade > 0) {
            prev.crossFadeTo(next, fade, false);
        } else if (prev) {
            prev.stop();
        }
        if (!prev) next.reset().play();

        this.currentAnim = animId;
    }

    /**
     * Place the character's FEET at a world point. Y is the ground height;
     * the GLB's origin is at the feet, so no offset is applied.
     */
    setGroundPosition(x, y, z) {
        this.position.set(x, y, z);
        if (this.root) this.root.position.set(x, y, z);
    }

    setHeading(yaw) {
        this.heading = yaw;
        this._applyModelYaw();
    }

    /**
     * Rotate the whole model to (heading + strafe moveYaw). The model's forward
     * is +X in the GLB, and rotation.y = h - PI/2 makes +X point along the
     * facing (see file header); the strafe split adds _moveYaw so the LEGS turn
     * to face travel while the camera stays behind `heading`. Torso is pulled
     * back afterwards in _applyTorsoTwist.
     */
    _applyModelYaw() {
        if (this.root) this.root.rotation.y = (this.heading + this._moveYaw) - Math.PI / 2;
    }

    /**
     * Told by the controller each frame: is the character off the ground, and
     * what is its vertical velocity (yd/s, + up). Drives the jump/fall clips.
     */
    setAir(airborne, velY) {
        this._airborne = !!airborne;
        this._airVelY = velY || 0;
    }

    /**
     * Find the SpineLow bone (key bone 4) the torso split twists. SkinnedGlbWriter
     * encodes the M2 key-bone id in the node name as "_k<id>"; GLTFLoader keeps
     * the name on the THREE.Bone. Waist (5) would drive the LEGS, but in Split the
     * legs turn with the whole model, so only the torso bone is needed here.
     */
    _findStrafeBones() {
        this._torsoBone = null;
        if (!this.root) return;
        let spineLow = null, waist = null;
        this.root.traverse((o) => {
            if (!o.isBone || !o.name) return;
            const m = /_k(\d+)$/.exec(o.name);
            if (!m) return;
            const k = parseInt(m[1], 10);
            if (k === KEY_BONE_SPINE_LOW && !spineLow) spineLow = o;
            else if (k === KEY_BONE_WAIST && !waist) waist = o;
        });
        // Torso is SpineLow; fall back to Waist only if SpineLow is absent.
        this._torsoBone = spineLow || waist;
        if (this._torsoBone) {
            console.log('[character] strafe torso bone:', this._torsoBone.name);
        } else {
            console.warn('[character] no SpineLow/Waist key bone in the GLB — ' +
                'torso strafe split unavailable (rebuild after the SkinnedGlbWriter ' +
                'key-bone-name change).');
        }
    }

    /**
     * Pull the torso subtree BACK by (TorsoFollow - 1) * moveYaw about WORLD up,
     * so the torso ends at TorsoFollow of the model's turn while the legs keep
     * the full angle — the 90-vs-60 split. Ported from M2Animator: a rotation
     * appended in model space about the vertical axis. In three.js a bone stores
     * a LOCAL quaternion, so the world-Y axis is expressed in the bone's parent
     * frame and pre-multiplied. Must run AFTER mixer.update (which overwrites the
     * bone each frame) and after the world matrices are current.
     */
    _applyTorsoTwist() {
        const bone = this._torsoBone;
        if (!bone || !bone.parent) return;
        const twist = (TORSO_FOLLOW - 1) * this._moveYaw;
        if (Math.abs(twist) < 1e-4) return;
        // World matrices must reflect this frame's animation + model rotation
        // before we read the parent's world orientation.
        this.root.updateMatrixWorld(true);
        bone.parent.getWorldQuaternion(this._parentQuat);
        // World up expressed in the bone's parent frame.
        this._twistAxis.set(0, 1, 0).applyQuaternion(this._parentQuat.clone().invert());
        this._twistQuat.setFromAxisAngle(this._twistAxis, twist);
        bone.quaternion.premultiply(this._twistQuat);
        bone.updateMatrixWorld(true);
    }

    setVisible(v) {
        this.visible = !!v;
        if (this.root) this.root.visible = this.visible;
        if (!v) {
            // Drop the displacement history so re-showing it elsewhere doesn't
            // register as one enormous frame of movement and snap to Run.
            this._hasPrev = false;
            this.groundSpeed = 0;
            this.rawSpeed = 0;
            this._moveYaw = 0;
            this._forwardness = 0;
            this._sideness = 0;
            this._airborne = this._wasAirborne = false;
        }
    }

    /**
     * Per-frame update. Call AFTER the character's position has been set for
     * this frame — speed is measured from the change since the last call.
     */
    /**
     * Tell the character it is moving backwards, so it plays Walkbackwards
     * instead of moonwalking through the forward cycle. Set by the controller
     * from the sign of its own forward input — measured displacement cannot
     * distinguish forward from backward on its own.
     */
    setReverse(v) { this._reverse = !!v; }

    update(dt) {
        if (!this.root || !this.visible) return;
        if (!(dt > 0)) dt = 1 / 60;

        // ── Measured ground speed + travel direction (XZ only) ──
        let raw = 0, dx = 0, dz = 0;
        if (this._hasPrev) {
            dx = this.position.x - this._prevPos.x;
            dz = this.position.z - this._prevPos.z;
            raw = Math.sqrt(dx * dx + dz * dz) / dt;
        }
        this._prevPos.copy(this.position);
        this._hasPrev = true;
        this.rawSpeed = raw;

        // ── Asymmetric smoothing ──
        // Speeding up eases in; dropping below the threshold takes effect on
        // the same frame. The sharp stop is deliberate (see MOVE_THRESHOLD).
        if (raw < MOVE_THRESHOLD) {
            this.groundSpeed = raw;
        } else {
            const a = 1 - Math.exp(-SPEED_SMOOTH_RATE * dt);
            this.groundSpeed += (raw - this.groundSpeed) * a;
        }

        // ── Strafe angle (MSUIClient MeasureMotion + ChooseClip) ──
        // forwardness/sideness are the smoothed dot products of the travel
        // direction against the character's facing and its right vector. In the
        // web, forward = (sin h, cos h), right = forward x up = (-cos h, sin h).
        // moveYaw = atan2(-sideness, forwardness) turns the model to face travel.
        const blend = 1 - Math.exp(-MOTION_SMOOTH_RATE * dt);
        const flatLen = Math.hypot(dx, dz);
        let reverse = this._reverse;
        if (flatLen > 1e-4 && raw >= MOVE_THRESHOLD) {
            const ix = dx / flatLen, iz = dz / flatLen;
            const sh = Math.sin(this.heading), ch = Math.cos(this.heading);
            const fwd = ix * sh + iz * ch;             // dot(dir, forward)
            const side = ix * (-ch) + iz * sh;         // dot(dir, right)
            this._forwardness += (fwd - this._forwardness) * blend;
            this._sideness += (side - this._sideness) * blend;
        }

        let phi = Math.atan2(-this._sideness, this._forwardness);
        // Past ~110 deg the character is backing up: take the angle off what is
        // left after the half turn, so straight-back is unrotated, and play the
        // backwards clip. Matches CharacterRenderer.cs rotating-branch.
        if (Math.abs(phi) > STRAFE_BACKWARD) {
            phi = phi - Math.sign(phi) * Math.PI;
            reverse = true;
        }

        // Target model turn: only while genuinely moving on the ground; ease to
        // zero on a stop or in the air so the torso unwinds cleanly.
        const targetYaw = (raw >= MOVE_THRESHOLD && !this._airborne) ? phi : 0;
        {
            const a = 1 - Math.exp(-MOVE_YAW_RATE * dt);
            this._moveYaw += (targetYaw - this._moveYaw) * a;
            if (Math.abs(this._moveYaw) < 0.002) this._moveYaw = 0;
        }

        // ── State: air first, then ground locomotion ──
        const speed = this.groundSpeed;
        let want, authored;
        if (this._airborne) {
            // Rising -> Jump, descending -> Fall. JumpStart leads the takeoff so
            // the launch reads; it clamps and Jump/Fall take over by velocity.
            if (!this._wasAirborne && this.actions[ANIM.JUMP_START]) {
                want = ANIM.JUMP_START;
            } else if (this._airVelY <= 0 && this.actions[ANIM.FALL]) {
                want = ANIM.FALL;
            } else if (this.actions[ANIM.JUMP]) {
                want = ANIM.JUMP;
            } else {
                want = ANIM.JUMP_START;   // whatever exists
            }
            authored = 0;
        } else if (speed < MOVE_THRESHOLD) {
            want = ANIM.STAND; authored = 0;
        } else if (reverse && this.actions[ANIM.WALK_BACK]) {
            // Vanilla's backpedal is its own clip at its own speed.
            want = ANIM.WALK_BACK; authored = 4.5;
        } else if (speed < WALK_RUN_SPLIT) {
            want = ANIM.WALK; authored = WALK_SPEED;
        } else {
            want = ANIM.RUN; authored = RUN_SPEED;
        }

        // Fall back to whatever clip actually exists rather than freezing.
        if (!this.actions[want]) {
            if (this.actions[ANIM.STAND]) { want = ANIM.STAND; authored = 0; }
            else { this._wasAirborne = this._airborne; return; }
        }

        // A shorter crossfade into the air so the jump doesn't lag the launch.
        this._play(want, this._airborne !== this._wasAirborne ? 0.06 : CROSSFADE);
        this._wasAirborne = this._airborne;

        // ── Match playback to ground speed so the feet don't skate ──
        const action = this.actions[want];
        if (action) {
            action.timeScale = authored > 0
                ? Math.max(RATE_MIN, Math.min(RATE_MAX, speed / authored))
                : 1;
        }

        // Apply the model turn for the legs BEFORE stepping the mixer, so the
        // torso twist reads a current parent world orientation.
        this._applyModelYaw();

        if (this.mixer) this.mixer.update(dt);

        // Pull the torso back by the split remainder (after the mixer wrote the
        // bone). Skipped in the air, where _moveYaw is eased to zero anyway.
        this._applyTorsoTwist();
    }

    dispose() {
        const scene = this.editor.viewport && this.editor.viewport.scene;
        if (this.mixer) { this.mixer.stopAllAction(); this.mixer = null; }
        if (this.root) {
            if (scene) scene.remove(this.root);
            this.root.traverse((n) => {
                if (n.isMesh || n.isSkinnedMesh) {
                    if (n.geometry) n.geometry.dispose();
                    const mats = Array.isArray(n.material) ? n.material : [n.material];
                    for (const m of mats) {
                        if (!m) continue;
                        if (m.map) m.map.dispose();
                        m.dispose();
                    }
                }
            });
        }
        this.root = null;
        this.character = null;
        if (this._baseSkin && this._baseSkin.close) this._baseSkin.close();
        this._baseSkin = null;
        this.actions = {};
        this.currentAnim = null;
        this._hasPrev = false;
    }
}
