// character-control.js — third-person character + camera.
//
// ═══════════════════════════════════════════════════════════════════════════
// THIS IS A PORT, NOT AN INTERPRETATION
// ═══════════════════════════════════════════════════════════════════════════
// Ported from MSUIClient, reading the actual source rather than notes about it:
//
//   Engine/Camera.cs               Yaw / OrbitYaw / ViewYaw / Pitch / Distance,
//                                  Rotate, RotateView, FoldOrbitIntoFacing,
//                                  EaseOrbitBehind, Zoom
//   Engine/ClientWindow.cs         PollMouse (fold on right-button transition),
//                                  MouseMove (which drag feeds which yaw)
//   Program.cs  ~1262-1372         the exact key mapping, turn/strafe swap,
//                                  MovementInput, and the `moving` test
//   Player/CharacterController.cs  Update(): speed selection and the wish vector
//   Shaders/character.frag         "lighting byte-identical to wmo.frag"
//
// Numbers below are MSUIClient's, not invented. Where this file must differ it
// is because three.js is Y-up and MSUIClient works natively in WoW space
// (+X north, +Y west, +Z up); those places are marked.
//
// ═══════════════════════════════════════════════════════════════════════════
// THE MODEL, IN ONE PARAGRAPH
// ═══════════════════════════════════════════════════════════════════════════
// There is exactly ONE heading: `yaw`, the character's facing. The camera does
// not have its own — it has an OFFSET from his, `orbitYaw`, and sits at
// `viewYaw = yaw + orbitYaw`. Left-drag moves the offset (swing around and look
// at your own face; he keeps facing where he was). Right-drag moves the facing
// (steer him). Pressing the right button folds any accumulated offset INTO the
// facing, so he spins to put his back to you and the view does not move a pixel
// — the same angle just moved from one term to the other. Moving eases the
// offset back to zero, unless you are holding left, because fighting a user who
// is deliberately looking at something is worse than not having the feature.
//
// ═══════════════════════════════════════════════════════════════════════════
// WHAT WAS WRONG BEFORE
// ═══════════════════════════════════════════════════════════════════════════
//   - EaseOrbitBehind was a LINEAR 3 rad/s ramp. MSUIClient uses an exponential
//     with a 0.15s time constant, i.e. ~6.7/s and far snappier at small offsets.
//     Mine crawled, so any camera swing was undone slowly and turning read as
//     "the camera is welded behind him".
//   - Strafe carried an invented 0.85 scale and backpedal an invented 0.55.
//     MSUIClient scales NEITHER: it selects a different SPEED for backward
//     (4.5 yd/s, vanilla's MOVE_RUN_BACK) and leaves the wish vector alone.
//   - Arrow keys did not turn, PageUp/PageDown did not tilt.
//   - Camera collision did not exist, so zooming out pushed the view into
//     terrain.

import * as THREE from 'three';

// ── Movement (MSUIClient MovementConfig) ─────────────────────────────────────
export const RUN_SPEED = 7.0;
export const WALK_SPEED = 2.5;
const BACKWARD_SPEED = 4.5;      // vanilla MOVE_RUN_BACK; NOT run scaled down
const TURN_SPEED = 2.8;          // rad/s  (Program.cs _turnSpeed)
const TILT_SCALE = 0.6;          // PageUp/PageDown use turnSpeed * 0.6

// ── Camera (Engine/Camera.cs defaults) ───────────────────────────────────────
const CAM_DEFAULT_PITCH = 0.35;
const CAM_DEFAULT_DISTANCE = 9.0;
const CAM_MIN_DISTANCE = 1.5;
const CAM_MAX_DISTANCE = 40.0;
const CAM_EYE_HEIGHT = 2.2;
const PITCH_LIMIT = 1.45;        // ~83 deg, short of gimbal lock
const ZOOM_PER_NOTCH = 1.0;      // Camera.Zoom is additive: Distance - delta

// ── CameraConfig ─────────────────────────────────────────────────────────────
const MOUSE_SENSITIVITY = 0.004;
const INVERT_PITCH = false;
const CAM_COLLISION = true;
const CAM_CLEARANCE = 0.35;
const CAM_RESTORE_SPEED = 8.0;
const MAX_DELTA_PIXELS = 300;

// ── Ground follow ────────────────────────────────────────────────────────────
// MSUIClient samples an O(1) bilinear height grid. We have no such grid in the
// browser, so this raycasts the terrain BVH instead — same intent, different
// mechanism, hence the throttle and the smoothing that MSUIClient does not need.
const GROUND_RATE = 18.0;
const PROBE_HZ = 20;

// ── Gravity / jump / collision (MSUIClient MovementConfig, verbatim) ─────────
const GRAVITY = 19.29110527;     // yd/s^2
const JUMP_VELOCITY = 7.9558;    // yd/s
const TERMINAL_VELOCITY = 60.148;
const BODY_RADIUS = 0.4;         // capsule radius for wall sweep
const BODY_HEIGHT = 2.1;         // probe from mid-body so low rubble isn't a wall
const STEP_HEIGHT = 1.0;         // ledges up to this are stepped onto, not blocking
const GROUND_SNAP = 0.5;         // descending-stairs adhesion (GroundSnapDistance)
const GROUND_EPS = 0.05;         // GroundContactEpsilon; the jump landing test
const MAX_SLOPE_DEG = 55;
// cos(maxSlope): a surface whose up-component exceeds this is a floor, not a wall.
const MIN_GROUND_Y = Math.cos(MAX_SLOPE_DEG * Math.PI / 180);
// Feet this far above the probed ground while grounded => walked off a ledge, so
// start falling. Generous (1 yd) so the 20Hz probe lag on a slope never triggers
// a false fall — real cliffs are far deeper than any single frame of walking.
const FALL_THRESHOLD = 1.0;
// WMO building meshes have no BVH, so refresh the swept set at ~12Hz (not per
// frame) and cap how many are swept, bounding the brute-force raycast cost.
const WMO_GATHER_HZ = 12;
// Sweep nearby building + doodad meshes. The residency cap bounds total
// instances, and a lazy BVH per swept geometry keeps per-instance rejection
// cheap. WMO and doodad meshes get SEPARATE budgets: they are per-model
// InstancedMeshes, and a dense city has enough WMO submeshes (~280) to fill a
// single shared cap entirely — which starved doodads and silently dropped ALL
// tree collision. Independent budgets guarantee trees are always swept.
const WMO_HARD_CAP = 160;
const DOODAD_HARD_CAP = 140;
const OBSTACLE_HARD_CAP = WMO_HARD_CAP + DOODAD_HARD_CAP;

// Feet must be this far UNDER terrain before a lower collision floor (a mine, a
// crypt, a bridge underside) may win over it — vanilla Map::GetHeight's "closer
// surface" clause, gated so an ordinary uphill step never drops you through the
// world (CharacterController.UndergroundSlack).
const UNDERGROUND_SLACK = 1.0;

const TAU = Math.PI * 2;

/** Wrap to (-pi, pi], so easing toward zero always takes the short way. */
function wrap(r) {
    r = ((r % TAU) + TAU) % TAU;
    return r > Math.PI ? r - TAU : r;
}
/** Normalize to [0, 2pi), matching Camera.Rotate. */
function norm(r) { return ((r % TAU) + TAU) % TAU; }

function clamp(v, lo, hi) { return v < lo ? lo : (v > hi ? hi : v); }

export class CharacterController {
    constructor(editor, player) {
        this.editor = editor;
        this.player = player;
        this.enabled = false;

        // ── Character ──
        this.position = new THREE.Vector3();
        this.yaw = 0;                    // THE heading. Character's facing.

        // ── Camera ──
        this.orbitYaw = 0;               // offset from yaw, wrapped to (-pi, pi]
        this.pitch = CAM_DEFAULT_PITCH;  // elevation ABOVE the target
        this.distance = CAM_DEFAULT_DISTANCE;         // what the user asked for
        this.effectiveDistance = CAM_DEFAULT_DISTANCE; // what collision allows

        // ── Input ──
        this.held = new Set();
        this.leftDown = false;
        this.rightDown = false;
        this._prevRightDown = false;
        this._lastX = 0; this._lastY = 0;
        this._pendingYaw = 0;
        this._pendingOrbitYaw = 0;
        this._pendingPitch = 0;
        this._pendingZoom = 0;
        this._detach = null;

        this._targetGroundY = null;
        this._probeAccum = 0;

        // ── Vertical physics (gravity + jump) ──
        this.velY = 0;
        this.grounded = false;

        // ── Collision caches ──
        this._obstacles = [];         // nearby building + doodad meshes, refreshed at ~12Hz
        this._wmoGatherAccum = 0;
        this._rayH = new THREE.Raycaster();   // horizontal wall sweep
        this._rayD = new THREE.Raycaster();   // downward step/ground probe
        this._downVec = new THREE.Vector3(0, -1, 0);   // NOT _down: that is the input key-check method
        this._nrm = new THREE.Vector3();
        this._nmat = new THREE.Matrix3();

        // Instrumentation. Guessing at whether input arrives is what turned
        // three separate control bugs into three separate round trips; a live
        // readout answers it at a glance. See _buildReadout.
        this.keyEvents = 0;
        this.mouseEvents = 0;
        this._dragging = false;
        this._readout = null;
        this._foliageCtl = null;         // in-world foliage-density chip (character mode)
        this.showReadout = true;
        // True once any keydown carried a usable e.key, after which letter
        // bindings resolve by character rather than physical position.
        this._sawChars = false;
        this.lastInput = { forward: 0, strafe: 0, turn: 0 };

        this._ray = new THREE.Raycaster();
        this._v = new THREE.Vector3();
    }

    get viewYaw() { return this.yaw + this.orbitYaw; }

    // ── Camera.cs verbatim ──────────────────────────────────────────────────

    /** Turn the character, and with him the camera. Right-drag and A/D. */
    rotate(yawDelta, pitchDelta) {
        this.yaw = norm(this.yaw + yawDelta);
        this.pitch = clamp(this.pitch + pitchDelta, -PITCH_LIMIT, PITCH_LIMIT);
    }

    /** Swing the camera around him without turning him. Left-drag. */
    rotateView(yawDelta) {
        if (yawDelta === 0) return;
        this.orbitYaw = wrap(this.orbitYaw + yawDelta);
    }

    /**
     * Turn him to wherever the camera has been swung and drop the offset. The
     * camera does not move: viewYaw is unchanged because the same angle simply
     * moved from one term to the other.
     */
    foldOrbitIntoFacing() {
        if (this.orbitYaw === 0) return;
        this.yaw = norm(this.yaw + this.orbitYaw);
        this.orbitYaw = 0;
    }

    /** Exponential ease with a 0.15s time constant — Camera.EaseOrbitBehind. */
    easeOrbitBehind(dt, seconds = 0.15) {
        if (this.orbitYaw === 0) return;
        const blend = seconds <= 0 ? 1 : 1 - Math.exp(-dt / seconds);
        this.orbitYaw -= this.orbitYaw * blend;
        if (Math.abs(this.orbitYaw) < 0.002) this.orbitYaw = 0;
    }

    /** Additive, like Camera.Zoom. Zooming IN takes effect immediately. */
    zoom(delta) {
        this.distance = clamp(this.distance - delta, CAM_MIN_DISTANCE, CAM_MAX_DISTANCE);
        if (this.effectiveDistance > this.distance) this.effectiveDistance = this.distance;
    }

    // ── Lifecycle ───────────────────────────────────────────────────────────

    spawnAt(x, groundY, z, facingYaw) {
        this.position.set(x, groundY, z);
        this._targetGroundY = groundY;
        this.velY = 0;
        this.grounded = true;
        if (facingYaw !== undefined) this.yaw = norm(facingYaw);
        this.orbitYaw = 0;
        this.pitch = CAM_DEFAULT_PITCH;
        this.distance = this.effectiveDistance = CAM_DEFAULT_DISTANCE;
        this.player.setGroundPosition(x, groundY, z);
        this.player.setHeading(this.yaw);
        this._applyCamera();
    }

    enable() {
        if (this.enabled) return;
        this.enabled = true;
        const rig = this.editor.viewport.rig;
        // OrbitControls and walk mode each write camera.position every frame.
        // Two authorities over one transform is the M4.1 runaway; leave one.
        rig.controls.enabled = false;
        if (rig.walk.mode) rig.leaveWalkMode();
        this._attachInput();
        this._buildReadout();
        this._buildFoliageControl();
    }

    disable() {
        if (!this.enabled) return;
        this.enabled = false;
        this.held.clear();
        this.leftDown = this.rightDown = this._prevRightDown = false;
        if (this._detach) { this._detach(); this._detach = null; }
        if (this._readout && this._readout.parentElement) {
            this._readout.parentElement.removeChild(this._readout);
        }
        this._readout = null;
        this._removeFoliageControl();
        const rig = this.editor.viewport.rig;
        rig.controls.enabled = true;
        rig.controls.target.set(
            this.position.x, this.position.y + CAM_EYE_HEIGHT, this.position.z);
        rig.controls.update();
    }

    // ── Input ───────────────────────────────────────────────────────────────
    //
    // MSUIClient derives button state by POLLING IsButtonPressed rather than
    // counting up/down events, because a swallowed MouseUp otherwise strands the
    // look. The browser equivalent is listening for mouseup on WINDOW (not the
    // canvas) plus a blur reset — a release outside the canvas still lands.

    _attachInput() {
        const canvas = this.editor.viewport.canvas;

        // ── Why this is defensive ────────────────────────────────────────────
        //
        // This page already has FOUR document-level keydown listeners
        // (input.js x2, transform.js, this) and several canvas pointer
        // listeners, some of which call preventDefault or
        // stopImmediatePropagation. A browser also suppresses the compatibility
        // mouse* events entirely if anything preventDefaults the matching
        // pointer* event, so a handler that only listens for 'mousedown' can go
        // permanently silent because of code it never touches.
        //
        // MSUIClient does not have this problem because it owns its input
        // pipeline outright and ImGui is the only thing that can claim it
        // (ClientWindow: `if (ImGui.GetIO().WantCaptureMouse) return;`). The
        // equivalent here is: while character mode is ON, character mode owns
        // input, and the only thing that outranks it is a focused text field.
        //
        // So:
        //   - CAPTURE phase on window, which runs before any of the bubble-phase
        //     listeners above and cannot be pre-empted by them;
        //   - consumed keys are stopped, so movement never also toggles a tool;
        //   - BOTH pointer* and mouse* families are listened to, deduplicated by
        //     timestamp, so suppression of either one is survivable;
        //   - button state is POLLED from e.buttons on every move, which is the
        //     browser's authoritative bitmask. MSUIClient polls IsButtonPressed
        //     for exactly this reason: "a MouseUp that never arrived cannot
        //     leave the cursor hidden and the view spinning."

        // Keys this mode consumes. Anything here is stopped so it cannot also
        // trigger an editor shortcut (B toggling sculpt mid-run, for instance).
        const OWNED = new Set([
            'KeyW', 'KeyA', 'KeyS', 'KeyD', 'KeyQ', 'KeyE',
            'ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight',
            'PageUp', 'PageDown', 'Space',
            'ShiftLeft', 'ShiftRight', 'ControlLeft', 'ControlRight',
        ]);

        const typing = (e) => {
            const t = e.target;
            if (!t || !t.tagName) return false;
            return t.tagName === 'INPUT' || t.tagName === 'SELECT' ||
                   t.tagName === 'TEXTAREA' || t.isContentEditable === true;
        };

        const onKeyDown = (e) => {
            if (typing(e)) return;
            this.held.add(e.code);
            // ALSO index by the produced character. e.code is physical-position
            // based on a US layout: on AZERTY the key labelled A reports
            // 'KeyQ', on QWERTZ Y and Z are swapped. Recording both means the
            // binding works regardless of layout, and it removes layout as a
            // suspect when something does not respond.
            if (e.key && e.key.length === 1) {
                this.held.add('#' + e.key.toLowerCase());
                this._sawChars = true;
            }
            this.keyEvents++;
            if (OWNED.has(e.code)) {
                e.preventDefault();
                e.stopImmediatePropagation();
            }
        };
        const onKeyUp = (e) => {
            this.held.delete(e.code);
            if (e.key && e.key.length === 1) this.held.delete('#' + e.key.toLowerCase());
            if (OWNED.has(e.code)) e.stopImmediatePropagation();
        };

        // ── Mouse / pointer ─────────────────────────────────────────────────
        // Identity alone is brittle: a wrapper element, an overlay child, or a
        // synthetic event can all make `target === canvas` false for a gesture
        // that visually happened on the canvas. Accept a descendant too, and
        // treat "no usable target" as on-canvas rather than dropping the input —
        // failing open here costs a stray drag, failing closed costs ALL mouse
        // control, which is the failure that has been reported twice.
        const overCanvas = (e) => {
            const t = e && e.target;
            if (!t) return true;
            if (t === canvas) return true;
            if (typeof canvas.contains === 'function' && canvas.contains(t)) return true;
            // Fall back to a tag check for synthetic events with a stub target.
            return t.tagName === 'CANVAS';
        };

        // Both families fire for the same physical action. Collapse them.
        let lastDownStamp = -1, lastMoveStamp = -1;

        const applyButtons = (e) => {
            // e.buttons: bit 0 = left, bit 1 = right. Authoritative.
            if (typeof e.buttons !== 'number') return;
            this.leftDown = (e.buttons & 1) !== 0;
            this.rightDown = (e.buttons & 2) !== 0;
        };

        const onDown = (e) => {
            if (e.timeStamp === lastDownStamp) return;
            lastDownStamp = e.timeStamp;
            if (!overCanvas(e)) return;          // do not steal drags on the UI
            this._dragging = true;
            if (typeof e.buttons === 'number') applyButtons(e);
            else { if (e.button === 0) this.leftDown = true;
                   if (e.button === 2) this.rightDown = true; }
            this._lastX = e.clientX; this._lastY = e.clientY;
            this.mouseEvents++;
        };

        const onUp = (e) => {
            if (typeof e.buttons === 'number') applyButtons(e);
            else { if (e.button === 0) this.leftDown = false;
                   if (e.button === 2) this.rightDown = false; }
            if (!this.leftDown && !this.rightDown) this._dragging = false;
        };

        const onMove = (e) => {
            if (e.timeStamp === lastMoveStamp) return;
            lastMoveStamp = e.timeStamp;

            // Poll, never trust bookkeeping. A release that happened over
            // another element, or an event that got swallowed, self-heals here.
            const wasDragging = this._dragging;
            applyButtons(e);
            if (!this.leftDown && !this.rightDown) { this._dragging = false; return; }
            if (!wasDragging) { this._lastX = e.clientX; this._lastY = e.clientY; return; }

            const dx = e.clientX - this._lastX;
            const dy = e.clientY - this._lastY;
            this._lastX = e.clientX; this._lastY = e.clientY;
            if (Math.abs(dx) > MAX_DELTA_PIXELS || Math.abs(dy) > MAX_DELTA_PIXELS) return;
            if (dx === 0 && dy === 0) return;

            this.mouseEvents++;

            // The one line that separates the two drag modes.
            if (this.rightDown) this._pendingYaw -= dx * MOUSE_SENSITIVITY;
            else this._pendingOrbitYaw -= dx * MOUSE_SENSITIVITY;

            // Screen Y grows DOWN and pitch is elevation ABOVE the target, so
            // standard (non-inverted) behaviour ADDS the delta.
            this._pendingPitch += dy * MOUSE_SENSITIVITY * (INVERT_PITCH ? -1 : 1);
        };

        const onWheel = (e) => {
            if (!overCanvas(e)) return;
            e.preventDefault();
            e.stopImmediatePropagation();
            // Browser deltaY is POSITIVE scrolling down/away; MSUIClient's
            // wheel.Y is positive scrolling up. Negate to match Camera.Zoom.
            this._pendingZoom += (e.deltaY > 0 ? -1 : 1) * ZOOM_PER_NOTCH;
        };
        const onContextMenu = (e) => { if (overCanvas(e)) e.preventDefault(); };
        const onBlur = () => {
            this.held.clear();
            this.leftDown = this.rightDown = this._dragging = false;
        };

        const CAP = true;
        const add = (t, type, fn, opts) => t.addEventListener(type, fn, opts);

        add(window, 'keydown', onKeyDown, CAP);
        add(window, 'keyup', onKeyUp, CAP);
        for (const t of ['pointerdown', 'mousedown']) add(window, t, onDown, CAP);
        for (const t of ['pointerup', 'mouseup']) add(window, t, onUp, CAP);
        for (const t of ['pointermove', 'mousemove']) add(window, t, onMove, CAP);
        add(window, 'wheel', onWheel, { capture: true, passive: false });
        add(window, 'contextmenu', onContextMenu, CAP);
        add(window, 'blur', onBlur);

        this._detach = () => {
            window.removeEventListener('keydown', onKeyDown, CAP);
            window.removeEventListener('keyup', onKeyUp, CAP);
            for (const t of ['pointerdown', 'mousedown']) window.removeEventListener(t, onDown, CAP);
            for (const t of ['pointerup', 'mouseup']) window.removeEventListener(t, onUp, CAP);
            for (const t of ['pointermove', 'mousemove']) window.removeEventListener(t, onMove, CAP);
            window.removeEventListener('wheel', onWheel, { capture: true });
            window.removeEventListener('contextmenu', onContextMenu, CAP);
            window.removeEventListener('blur', onBlur);
        };
    }

    /**
     * ClientWindow.Axis: +1 for the first key, -1 for the second.
     *
     * LETTER bindings resolve by the character the key PRODUCES, not by its
     * physical position — which is what vanilla does, and what the hands expect:
     * the key labelled E strafes right whatever layout you are on.
     *
     * Checking both without preferring one is worse than checking either. On
     * Dvorak the key labelled E sits at physical KeyD, so a press reports
     * code 'KeyD' + key 'e'; matching both made ONE keypress satisfy the turn
     * binding (KeyD) AND the strafe binding (e) at once, and the character
     * walked off diagonally. Verified in the harness.
     *
     * Non-letter bindings — arrows, PageUp/Down, Space, Shift — have no
     * character to speak of and stay on code.
     */
    _down(code) {
        const isLetter = code.length === 4 && code.startsWith('Key');
        if (isLetter && this._sawChars) {
            return this.held.has('#' + code[3].toLowerCase());
        }
        return this.held.has(code);
    }

    _axis(pos, neg) {
        return (this._down(pos) ? 1 : 0) - (this._down(neg) ? 1 : 0);
    }

    // ── Per-frame ───────────────────────────────────────────────────────────

    update(dt) {
        if (!this.enabled || !this.player.isLoaded) return;
        if (!(dt > 0)) dt = 1 / 60;
        dt = Math.min(dt, 0.05);      // CharacterController.Update's own clamp

        // ── Drain pending mouse input (ClientWindow.HandleUpdate order) ──────
        // The right-button FOLD happens on the DOWN TRANSITION, before this
        // frame's deltas are applied, so a held button does not re-fold zero.
        if (this.rightDown && !this._prevRightDown) this.foldOrbitIntoFacing();
        this._prevRightDown = this.rightDown;

        this.rotate(this._pendingYaw, this._pendingPitch);
        this.rotateView(this._pendingOrbitYaw);
        if (this._pendingZoom !== 0) this.zoom(this._pendingZoom);
        this._pendingYaw = this._pendingOrbitYaw = this._pendingPitch = this._pendingZoom = 0;

        // ── Keys (Program.cs 1262-1293, verbatim mapping) ────────────────────
        //
        // A and D TURN, they do not strafe. That is vanilla's default bind and
        // it is what the hands expect; strafe lives on Q and E. Holding the
        // RIGHT mouse button swaps them, exactly as the real client does: you
        // are already steering with the mouse, so A and D become strafe and your
        // hand does not have to move mid-fight.
        const mouseSteering = this.rightDown;

        let turn = this._axis('ArrowLeft', 'ArrowRight');
        if (!mouseSteering) turn += this._axis('KeyA', 'KeyD');
        turn = clamp(turn, -1, 1);
        if (turn !== 0) this.rotate(turn * TURN_SPEED * dt, 0);

        let strafe = this._axis('KeyE', 'KeyQ');
        if (mouseSteering) strafe += this._axis('KeyD', 'KeyA');
        strafe = clamp(strafe, -1, 1);

        const tilt = this._axis('PageUp', 'PageDown');
        if (tilt !== 0) this.rotate(0, tilt * TURN_SPEED * TILT_SCALE * dt);

        const forward = clamp(
            this._axis('KeyW', 'KeyS') + this._axis('ArrowUp', 'ArrowDown'), -1, 1);

        const walking = this.held.has('ShiftLeft') || this.held.has('ShiftRight');

        // ── Move (CharacterController.Update) ────────────────────────────────
        //
        // Vanilla has a distinct MOVE_RUN_BACK speed. Scaling run down would
        // make backpedalling a fraction of forward in every direction; the real
        // client picks a different speed and leaves the direction alone.
        // Push out of any wall we are already inside BEFORE moving. Without this
        // the sweep is a one-way door: it only ever STOPS motion, so anything
        // that lands the body inside geometry (a step-up flush to a wall, a snap
        // under an overhang, a ray slipping past a corner) welds it there and
        // reads exactly like "collision is offset" (CharacterController.Depenetrate).
        this._depenetrate();

        const speed = walking ? WALK_SPEED
                    : (forward < -0.01 ? BACKWARD_SPEED : RUN_SPEED);

        // Facing and its right-hand side. MSUIClient works in WoW space where
        // forward is (cos, sin, 0) and right is (sin, -cos, 0). Here the world
        // is three.js Y-up with the horizontal plane in XZ, so the equivalent
        // pair — verified against the model's own rotation.y = yaw - PI/2 — is:
        //     forward = ( sin yaw, cos yaw)
        //     right   = (-cos yaw, sin yaw)      // = forward x up
        const sy = Math.sin(this.yaw), cy = Math.cos(this.yaw);
        let wx = sy * forward - cy * strafe;
        let wz = cy * forward + sy * strafe;
        const wlen = Math.hypot(wx, wz);
        if (wlen > 1e-3) {
            wx /= wlen; wz /= wlen;
            // Swept horizontal move: slides along walls and buildings instead of
            // walking through them (CharacterController.MoveHorizontal).
            this._moveHorizontal(wx * speed * dt, wz * speed * dt);
        }

        // ── Jump (CharacterController.Update: Jump && Grounded) ───────────────
        const jumpHeld = this.held.has('Space');
        let jumped = false;
        if (jumpHeld && this.grounded) {
            this.velY = JUMP_VELOCITY;
            this.grounded = false;
            jumped = true;
        }

        // ── Ground probe (throttled BVH raycast into _targetGroundY) ──────────
        this._probeAccum += dt;
        if (this._targetGroundY === null || this._probeAccum >= 1 / PROBE_HZ || !this.grounded) {
            this._probeAccum = 0;
            const hit = this._probeGround(this.position.x, this.position.z);
            if (hit !== null) this._targetGroundY = hit;
        }
        const groundY = this._targetGroundY;

        // ── Vertical: gravity + landing (CharacterController.ResolveGround) ───
        // The Velocity.Z<=0 guard is why jumping works: a rising character is
        // never snapped back to the ground (that swallowed the jump entirely,
        // frame-rate dependently, in MSUIClient before the guard).
        if (this.grounded && !jumped) {
            if (groundY !== null) {
                if (this.position.y - groundY > FALL_THRESHOLD) {
                    // Walked off a ledge -> fall.
                    this.grounded = false;
                    this.velY = 0;
                } else {
                    // Follow the ground (smoothed; MSUIClient snaps on a real grid).
                    const a = 1 - Math.exp(-GROUND_RATE * dt);
                    this.position.y += (groundY - this.position.y) * a;
                    this.velY = 0;
                }
            }
        }
        if (!this.grounded) {
            this.velY -= GRAVITY * dt;
            if (this.velY < -TERMINAL_VELOCITY) this.velY = -TERMINAL_VELOCITY;
            this.position.y += this.velY * dt;
            if (groundY !== null && this.velY <= 0 &&
                this.position.y <= groundY + GROUND_EPS) {
                this.position.y = groundY;
                this.velY = 0;
                this.grounded = true;
            } else if (groundY !== null && this.velY <= 0 && !jumped &&
                       this.position.y - groundY <= GROUND_SNAP) {
                // Short gaps between narrow supports read as continuous ground.
                this.position.y = groundY;
                this.velY = 0;
                this.grounded = true;
            }
        }

        // ── Re-centre while moving (Program.cs 1358-1363) ────────────────────
        // Turning counts as moving: "if you hit wasd it snaps you back behind
        // the character", and A and D are part of WASD.
        this.lastInput.forward = forward;
        this.lastInput.strafe = strafe;
        this.lastInput.turn = turn;
        this.lastInput.steering = mouseSteering;

        // One-shot console proof that a binding fired, so "it does nothing" can
        // be separated from "it does something the camera hides".
        if (strafe !== 0 && !this._loggedStrafe) {
            this._loggedStrafe = true;
            console.log('[character] strafe input active:', strafe,
                'mouseSteering:', mouseSteering, 'held:', [...this.held].join(','));
        }

        const moving = Math.abs(forward) > 0.01 || Math.abs(strafe) > 0.01 || turn !== 0;
        if (moving && !this.leftDown) this.easeOrbitBehind(dt);

        // ── Push to the visual character, then let it measure itself ─────────
        this.player.setGroundPosition(this.position.x, this.position.y, this.position.z);
        this.player.setHeading(this.yaw);
        // Measured displacement cannot tell forward from backward, so the sign
        // of the input has to be handed over explicitly.
        if (this.player.setReverse) this.player.setReverse(forward < -0.01);
        // Air state drives the jump/fall clips and pauses the strafe torso split.
        if (this.player.setAir) this.player.setAir(!this.grounded, this.velY);
        this.player.update(dt);

        this._resolveCameraCollision(dt);
        this._applyCamera();
        this._updateReadout();
    }

    /**
     * Live input readout.
     *
     * Every control bug in this feature so far has come down to one question —
     * is the input arriving? — and answering it by reading code has been wrong
     * three times running. This makes it a glance:
     *
     *   keys 412 mouse 88 | held W,KeyQ | fwd 1.0 str -1.0 turn 0.0 | L0 R1
     *   yaw 137 orbit -12 pitch 20 dist 9.0/9.0
     *
     * If a key is physically down and does not appear in `held`, something
     * upstream is eating it and the problem is not the movement maths. If it
     * appears but fwd/str/turn stay zero, the mapping is wrong. If those move
     * but the character does not, it is the ground probe or the speed.
     */
    _buildReadout() {
        if (!this.showReadout || this._readout) return;
        const parent = this.editor.viewport.canvas.parentElement;
        if (!parent) return;
        const el = document.createElement('div');
        el.id = 'weCharDebug';
        el.style.cssText =
            'position:absolute;bottom:16px;left:16px;padding:4px 8px;' +
            'font:11px/1.45 ui-monospace,SFMono-Regular,Menlo,Consolas,monospace;' +
            'color:#9fe3a0;background:rgba(0,0,0,0.62);border-radius:4px;' +
            'white-space:pre;pointer-events:none;z-index:11;';
        parent.appendChild(el);
        this._readout = el;
    }

    _updateReadout() {
        if (!this._readout) return;
        const i = this.lastInput;
        const held = [...this.held]
            .filter((k) => k.startsWith('Key') || k.startsWith('Arrow') ||
                           k.startsWith('Shift') || k.startsWith('Page'))
            .map((k) => k.replace('Key', '').replace('Arrow', '<'))
            .join(',') || '-';
        const deg = (r) => (r * 180 / Math.PI).toFixed(0);
        this._readout.textContent =
            `keys ${this.keyEvents} mouse ${this.mouseEvents} | held ${held}\n` +
            `fwd ${i.forward.toFixed(1)} STR ${i.strafe.toFixed(1)} ` +
            `turn ${i.turn.toFixed(1)} | L${this.leftDown ? 1 : 0} ` +
            `R${this.rightDown ? 1 : 0}${i.steering ? ' STEER' : ''}\n` +
            `yaw ${deg(this.yaw)} orbit ${deg(this.orbitYaw)} pitch ${deg(this.pitch)} ` +
            `dist ${this.effectiveDistance.toFixed(1)}/${this.distance.toFixed(1)}`;
    }

    // ── In-world foliage density control (next to the character) ──────────────
    //
    // A small options chip in the bottom-right of the canvas, shown only while
    // the character is active, that scales ground-effect (grass/clutter) density
    // live without opening the Options modal. It drives the same knob
    // (foliage.densityScale) the Options → Density slider does, and keeps that
    // slider in sync if it exists.

    _buildFoliageControl() {
        if (this._foliageCtl) return;
        const foliage = this.editor.foliage;
        if (!foliage) return;
        const parent = this.editor.viewport.canvas.parentElement;
        if (!parent) return;

        const fmt = (v) => v.toFixed(1) + '×';   // "1.0×"

        const wrap = document.createElement('div');
        wrap.id = 'weCharFoliage';
        wrap.style.cssText =
            'position:absolute;bottom:16px;right:16px;z-index:12;' +
            'font:11px/1.4 ui-monospace,SFMono-Regular,Menlo,Consolas,monospace;' +
            'color:#d8f5d9;background:rgba(0,0,0,0.62);border-radius:6px;padding:6px 8px;';

        const row = document.createElement('div');
        row.style.cssText = 'display:flex;align-items:center;gap:8px;cursor:pointer;';
        const label = document.createElement('span');
        label.textContent = '🌿 Foliage';   // 🌿
        label.style.cssText = 'font-weight:600;';
        const valEl = document.createElement('span');
        valEl.textContent = fmt(foliage.densityScale);
        valEl.style.cssText = 'color:#9fe3a0;min-width:34px;text-align:right;';
        const caret = document.createElement('span');
        caret.textContent = '▸';                 // ▸
        caret.style.opacity = '0.7';
        row.appendChild(label); row.appendChild(valEl); row.appendChild(caret);
        wrap.appendChild(row);

        const panel = document.createElement('div');
        panel.style.cssText = 'display:none;margin-top:6px;align-items:center;gap:8px;';
        const cap = document.createElement('span');
        cap.textContent = 'Density'; cap.style.opacity = '0.7';
        const slider = document.createElement('input');
        slider.type = 'range';
        slider.min = '0'; slider.max = '20'; slider.step = '1';
        slider.value = String(Math.round(foliage.densityScale * 10));
        slider.style.cssText = 'width:130px;accent-color:#5cb85c;';
        panel.appendChild(cap); panel.appendChild(slider);
        wrap.appendChild(panel);

        let open = false;
        row.addEventListener('click', () => {
            open = !open;
            panel.style.display = open ? 'flex' : 'none';
            caret.textContent = open ? '▾' : '▸';   // ▾ / ▸
        });

        const apply = () => {
            const v = parseInt(slider.value, 10) / 10;
            valEl.textContent = fmt(v);
            if (foliage.setDensityScale) foliage.setDensityScale(v);
            else { foliage.densityScale = v; foliage.forceRescatter(); }
            // Changing density is a clear intent to see it — turn foliage on.
            if (!foliage.enabled && foliage.setEnabled) foliage.setEnabled(true);
            // Keep the Options-modal density slider in sync if it is built.
            const opt = document.getElementById('optFoliageDensity');
            const optVal = document.getElementById('optFoliageDensityVal');
            if (opt) opt.value = slider.value;
            if (optVal) optVal.textContent = fmt(v);
        };
        slider.addEventListener('input', apply);
        // The character owns window input in capture phase, but it gates on
        // overCanvas(), and this chip is a sibling of the canvas, not a child —
        // so drags here never reach the look/zoom handlers. stopPropagation is
        // belt-and-braces for any bubble-phase listeners.
        for (const t of ['pointerdown', 'mousedown', 'pointermove', 'mousemove', 'wheel', 'click']) {
            slider.addEventListener(t, (e) => e.stopPropagation(), false);
        }

        parent.appendChild(wrap);
        this._foliageCtl = wrap;
    }

    _removeFoliageControl() {
        if (this._foliageCtl && this._foliageCtl.parentElement) {
            this._foliageCtl.parentElement.removeChild(this._foliageCtl);
        }
        this._foliageCtl = null;
    }

    _worldMeshes() {
        const tg = this.editor.tileGrid;
        if (!tg) return null;
        if (tg.isDungeon) return tg.dungeonMeshes;
        return tg.terrainMeshes ? tg.terrainMeshes() : null;
    }

    _probeGround(x, z) {
        const rig = this.editor.viewport.rig;
        const th = rig.probeGroundY(this._worldMeshes(), x, z, this.position.y + 5);
        const terrainY = (th !== null && isFinite(th)) ? th : null;

        // WMO floors, bridges and tunnel floors come from the vmap collision
        // world, not the terrain grid — this is what makes interiors walkable.
        const colY = this._probeCollisionGround(x, z);
        if (colY === null) return terrainY;
        if (terrainY === null) return colY;

        // Vanilla Map::GetHeight, in the two clauses it actually is: the
        // collision surface wins when it is ABOVE terrain, or when the feet are
        // genuinely UNDER terrain and it is the CLOSER of the two — the second
        // clause is the entire reason a mine floor beats the mountain above it.
        // The slack gate stops an ordinary uphill step qualifying and dropping
        // the character through the world.
        const feet = this.position.y;
        if (colY > terrainY) return colY;
        const underTerrain = terrainY - feet > UNDERGROUND_SLACK;
        const closer = Math.abs(terrainY - feet) > Math.abs(colY - feet);
        return (underTerrain && closer) ? colY : terrainY;
    }

    /**
     * Downward probe against the vmap CollisionWorld for a floor above terrain
     * (WMO floors, bridges, interiors). Null when no collision, or the hit is a
     * wall not a floor. Started at StepHeight above the feet, reaching well below
     * so a fast fall onto a floor is not missed between frames
     * (CharacterController.ResolveGround's collision probe).
     */
    _probeCollisionGround(x, z) {
        const cw = this.editor.collisionWorld;
        if (!cw || !cw.ready) return null;
        const oy = this.position.y + STEP_HEIGHT;
        this._rayD.set(new THREE.Vector3(x, oy, z), this._downVec);
        this._rayD.far = STEP_HEIGHT + 5;
        this._rayD.firstHitOnly = true;
        const hits = this._rayD.intersectObjects(cw.meshes, false);
        const hit = hits.length ? hits[0] : null;
        if (!hit) return null;
        // abs(): vmap winding is inconsistent, so a floor's face normal may point
        // down. Its up-COMPONENT magnitude still tells floor (≈1) from wall (≈0).
        const nrm = this._worldNormalAt(hit);
        if (Math.abs(nrm.y) <= MIN_GROUND_Y) return null;
        return oy - hit.distance;
    }

    // ── Collision ─────────────────────────────────────────────────────────────
    //
    // Ported from CharacterController.MoveHorizontal: a single probe ray from
    // mid-body along the move; on a wall hit the remaining motion is projected
    // onto the wall plane so the character slides, and a step-up is attempted so
    // stairs and curbs are walkable. Two iterations handle inside corners.
    //
    // Meshes swept: terrain + dungeon (three-mesh-bvh, cheap) and nearby WMO
    // buildings (InstancedMesh, no BVH — gathered and distance-filtered at 12Hz).
    // Walkable slopes (normal.y > cos(maxSlope)) are NOT walls: the gravity/ground
    // resolver takes those, so a hill never blocks you.

    /** Terrain/dungeon (BVH) plus nearby building AND doodad meshes, for wall sweeps. */
    _horizontalMeshes(dt) {
        const base = this._worldMeshes();
        const out = base ? base.slice() : [];

        // ── Real collision (vmap) takes over from the render-mesh sweep ───────
        // When the vmap CollisionWorld is loaded, walls/trees/interiors come
        // from the server's OWN collision geometry (doorways, floors, stairs) —
        // the whole point of the port. The render-mesh gather below (a crude
        // outer shell, the "collision is false" bug) is then skipped entirely.
        const cw = this.editor.collisionWorld;
        if (cw && cw.ready) {
            for (const m of cw.meshes) out.push(m);
            return out;
        }

        // Buildings (WMO) and doodads (trees/rocks) are InstancedMesh WITHOUT a
        // BVH, so a brute-force raycast costs their whole triangle count. Two
        // things keep this bounded: rebuild the swept set at 12Hz and cap it, and
        // lazily build a BVH on each swept geometry (three-mesh-bvh is
        // monkey-patched globally by streaming.js) so per-instance rejection is
        // O(1). The sweep ray is only ~0.5 yd long. Short ground clutter is below
        // the torso-height ray, so this naturally hits trunks/walls, not pebbles.
        const tg = this.editor.tileGrid;
        const os = this.editor.objectStream;
        if (tg && !tg.isDungeon && os) {
            this._wmoGatherAccum += dt;
            if (this._obstacles.length === 0 || this._wmoGatherAccum >= 1 / WMO_GATHER_HZ) {
                this._wmoGatherAccum = 0;
                const gathered = [];
                const ensureBvh = (m) => {
                    if (m.geometry && !m.geometry.boundsTree && m.geometry.computeBoundsTree) {
                        try { m.geometry.computeBoundsTree(); } catch (e) { /* fall back to brute force */ }
                    }
                };
                // Separate budgets so a wall of WMO submeshes cannot crowd out
                // doodads — that shared-cap starvation is what silently deleted
                // ALL tree collision.
                let wmoCount = 0, doodadCount = 0;
                const takeWmo = (m) => {
                    if (!m || (!m.isInstancedMesh && !m.isMesh)) return;
                    if (wmoCount >= WMO_HARD_CAP) return;
                    ensureBvh(m); gathered.push(m); wmoCount++;
                };
                const takeDoodad = (m) => {
                    if (!m || (!m.isInstancedMesh && !m.isMesh)) return;
                    if (doodadCount >= DOODAD_HARD_CAP) return;
                    ensureBvh(m); gathered.push(m); doodadCount++;
                };
                let wmoN = 0;
                if (os.wmoMeshList) { const w = os.wmoMeshList(); wmoN = w.length; for (const m of w) takeWmo(m); }
                const dg = os.pool && os.pool.doodadGroup;
                if (dg) dg.traverse((c) => takeDoodad(c));
                this._obstacles = gathered;
                if (!this._loggedObstacles) {
                    this._loggedObstacles = true;
                    console.log('[collision] sweeping', gathered.length, 'obstacle meshes (' +
                        wmoCount + '/' + wmoN + ' buildings, ' + doodadCount + ' doodads).');
                }
            }
            for (const m of this._obstacles) out.push(m);
        }
        return out;
    }

    /**
     * World-space surface normal of a raycast hit, for wall-vs-floor.
     *
     * Terrain/dungeon are plain Mesh with reliable face normals — use them so a
     * hill is a floor, not a wall. Buildings/doodads are InstancedMesh whose face
     * normal does NOT carry the per-instance rotation, so a rotated wall could be
     * misread as a floor and let you walk through it. For those, return a
     * HORIZONTAL normal (out of the wall toward the body): an obstacle is always
     * a wall, which is exactly the behaviour we want for buildings and trees.
     */
    _worldNormalAt(hit) {
        const n = this._nrm;
        if (!hit.object.isInstancedMesh && hit.face && hit.face.normal) {
            n.copy(hit.face.normal);
            this._nmat.getNormalMatrix(hit.object.matrixWorld);
            n.applyMatrix3(this._nmat).normalize();
            return n;
        }
        // Instanced obstacle (or no face): outward horizontal normal from the
        // wall back to the body — robust for any instance rotation, and n.y = 0
        // guarantees it is treated as a wall, not a walkable slope.
        n.set(this.position.x - hit.point.x, 0, this.position.z - hit.point.z);
        if (n.lengthSq() < 1e-8) n.set(0, 0, 1);
        return n.normalize();
    }

    /**
     * Push out of any wall we are already inside — 8 rays around the body at
     * chest height, against the vmap CollisionWorld ONLY (never terrain: shoving
     * off a slope would make ramps unclimbable). Walkable slopes are skipped. The
     * push direction is the horizontal from the wall toward the body, and the
     * total is capped at the radius so a corner hitting several rays at once does
     * not fling the body across the room. Faithful to CharacterController.Depenetrate,
     * which the render-mesh build could not have (a render shell has no inside).
     */
    _depenetrate() {
        const cw = this.editor.collisionWorld;
        if (!cw || !cw.ready) return;

        const ox = this.position.x, oy = this.position.y + BODY_HEIGHT * 0.5, oz = this.position.z;
        let px = 0, pz = 0;
        const rays = 8;
        for (let i = 0; i < rays; i++) {
            const ang = i * Math.PI * 2 / rays;
            this._rayH.set(new THREE.Vector3(ox, oy, oz),
                new THREE.Vector3(Math.cos(ang), 0, Math.sin(ang)));
            this._rayH.far = BODY_RADIUS;
            this._rayH.firstHitOnly = true;
            const hits = this._rayH.intersectObjects(cw.meshes, false);
            const hit = hits.length ? hits[0] : null;
            if (!hit) continue;
            const nrm = this._worldNormalAt(hit);
            if (Math.abs(nrm.y) > MIN_GROUND_Y) continue;   // a floor, not a wall
            const depth = BODY_RADIUS - hit.distance;
            if (depth <= 0) continue;
            let nx = this.position.x - hit.point.x, nz = this.position.z - hit.point.z;
            const nl = Math.hypot(nx, nz);
            if (nl < 1e-4) continue;
            px += (nx / nl) * depth; pz += (nz / nl) * depth;
        }

        const mag = Math.hypot(px, pz);
        if (mag < 1e-4) return;
        const capped = Math.min(mag, BODY_RADIUS);
        this.position.x += px / mag * capped;
        this.position.z += pz / mag * capped;
    }

    /**
     * Swept horizontal move with wall slide + step-up. dx/dz are this frame's
     * intended displacement in world XZ.
     */
    _moveHorizontal(dx, dz) {
        const meshes = this._horizontalMeshes(this.editor.viewport.deltaTime || 1 / 60);
        if (!meshes || meshes.length === 0) {
            this.position.x += dx; this.position.z += dz;
            return;
        }

        let mx = dx, mz = dz;
        for (let iter = 0; iter < 2; iter++) {
            let dist = Math.hypot(mx, mz);
            if (dist < 1e-5) return;
            const dirx = mx / dist, dirz = mz / dist;

            const ox = this.position.x, oy = this.position.y + BODY_HEIGHT * 0.5, oz = this.position.z;
            this._rayH.set(new THREE.Vector3(ox, oy, oz), new THREE.Vector3(dirx, 0, dirz));
            this._rayH.far = dist + BODY_RADIUS;
            const hits = this._rayH.intersectObjects(meshes, false);
            const hit = hits.length ? hits[0] : null;

            if (!hit || hit.distance > dist + BODY_RADIUS) {
                this.position.x += mx; this.position.z += mz;
                return;
            }

            const nrm = this._worldNormalAt(hit);
            // A walkable slope is a floor, not a wall — let the ground resolver
            // take it. abs(): vmap winding is inconsistent, so a floor normal may
            // point down; its up-COMPONENT magnitude still separates floor from wall.
            if (Math.abs(nrm.y) > MIN_GROUND_Y) {
                this.position.x += mx; this.position.z += mz;
                return;
            }

            // Try to step up onto a low ledge (stairs, curbs) before sliding.
            if (this.grounded && this._tryStepUp(mx, mz, meshes)) return;

            // Advance up to the wall, then slide along it (strip the into-wall
            // component of the remaining move; horizontal only).
            const advance = Math.max(0, hit.distance - BODY_RADIUS);
            if (advance > 1e-5) { this.position.x += dirx * advance; this.position.z += dirz * advance; }

            const into = mx * nrm.x + mz * nrm.z;   // move . horizontal normal
            mx -= nrm.x * into; mz -= nrm.z * into;
            mx *= 0.98; mz *= 0.98;
        }
    }

    /** Probe for a ledge within STEP_HEIGHT the move could stand on. */
    _tryStepUp(mx, mz, meshes) {
        const px = this.position.x + mx, pz = this.position.z + mz;
        const top = this.position.y + STEP_HEIGHT + 0.1;
        this._rayD.set(new THREE.Vector3(px, top, pz), this._downVec);
        this._rayD.far = STEP_HEIGHT + 0.4;
        const hits = this._rayD.intersectObjects(meshes, false);
        const hit = hits.length ? hits[0] : null;
        if (!hit) return false;
        const nrm = this._worldNormalAt(hit);
        if (Math.abs(nrm.y) <= MIN_GROUND_Y) return false;   // not a floor
        const stepTop = top - hit.distance;
        const rise = stepTop - this.position.y;
        if (rise < -0.05 || rise > STEP_HEIGHT) return false;
        this.position.x = px; this.position.z = pz; this.position.y = stepTop;
        this.velY = 0; this.grounded = true;
        return true;
    }

    /**
     * Camera collision — GameLoop.ResolveCameraCollision.
     *
     * `distance` is what the user asked for; `effectiveDistance` is what the
     * world allows. Keeping them separate is the whole point: clamping the
     * user's zoom directly means walking past a tree permanently zooms you in,
     * which feels broken in a way that is hard to describe and easy to ship.
     * Pull in instantly, ease back out.
     */
    _resolveCameraCollision(dt) {
        if (!CAM_COLLISION) { this.effectiveDistance = this.distance; return; }

        const meshes = this._worldMeshes();
        let allowed = this.distance;

        if (meshes && meshes.length) {
            const ex = this.position.x;
            const ey = this.position.y + CAM_EYE_HEIGHT;
            const ez = this.position.z;

            const vy = this.viewYaw;
            const cp = Math.cos(this.pitch);
            // Unit vector from the eye target TOWARD the camera.
            this._v.set(-Math.sin(vy) * cp, Math.sin(this.pitch), -Math.cos(vy) * cp);

            this._ray.set(new THREE.Vector3(ex, ey, ez), this._v);
            this._ray.far = this.distance;
            const hits = this._ray.intersectObjects(meshes, false);
            if (hits.length > 0) {
                allowed = Math.max(CAM_MIN_DISTANCE, hits[0].distance - CAM_CLEARANCE);
            }
        }

        if (allowed < this.effectiveDistance) {
            this.effectiveDistance = allowed;          // pull in instantly
        } else {
            const a = 1 - Math.exp(-CAM_RESTORE_SPEED * dt);
            this.effectiveDistance += (allowed - this.effectiveDistance) * a;
        }
    }

    _applyCamera() {
        const rig = this.editor.viewport.rig;
        const cam = rig.camera;

        const tx = this.position.x;
        const ty = this.position.y + CAM_EYE_HEIGHT;
        const tz = this.position.z;

        const vy = this.viewYaw;
        const cp = Math.cos(this.pitch);
        const d = this.effectiveDistance;

        cam.position.set(
            tx - Math.sin(vy) * cp * d,
            ty + Math.sin(this.pitch) * d,
            tz - Math.cos(vy) * cp * d);
        cam.lookAt(tx, ty, tz);

        // Keep the (disabled) OrbitControls pivot in sync so toggling character
        // mode off does not teleport the view.
        rig.controls.target.set(tx, ty, tz);
    }
}
