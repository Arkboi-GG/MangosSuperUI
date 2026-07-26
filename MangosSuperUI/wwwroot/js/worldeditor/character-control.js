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

        // Instrumentation. Guessing at whether input arrives is what turned
        // three separate control bugs into three separate round trips; a live
        // readout answers it at a glance. See _buildReadout.
        this.keyEvents = 0;
        this.mouseEvents = 0;
        this._dragging = false;
        this._readout = null;
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
            this.position.x += wx * speed * dt;
            this.position.z += wz * speed * dt;
        }

        // ── Ground ───────────────────────────────────────────────────────────
        this._probeAccum += dt;
        if (this._targetGroundY === null || this._probeAccum >= 1 / PROBE_HZ) {
            this._probeAccum = 0;
            const hit = this._probeGround(this.position.x, this.position.z);
            if (hit !== null) this._targetGroundY = hit;
        }
        if (this._targetGroundY !== null) {
            const a = 1 - Math.exp(-GROUND_RATE * dt);
            this.position.y += (this._targetGroundY - this.position.y) * a;
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

    _worldMeshes() {
        const tg = this.editor.tileGrid;
        if (!tg) return null;
        if (tg.isDungeon) return tg.dungeonMeshes;
        return tg.terrainMeshes ? tg.terrainMeshes() : null;
    }

    _probeGround(x, z) {
        const rig = this.editor.viewport.rig;
        const hit = rig.probeGroundY(this._worldMeshes(), x, z, this.position.y + 5);
        return (hit !== null && isFinite(hit)) ? hit : null;
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
