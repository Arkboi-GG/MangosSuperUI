// input.js — keyboard + pointer input outside the canvas tool dispatch.
//
// Sections:
//   1. createMovementTicker — WASD + arrows + sprint (suppressed during placement)
//   2. attachWalkLook       — right-click drag → yaw/pitch in walk mode
//   3. attachKeyboard       — global Esc + Ctrl-Z/Ctrl-Y + key forward to active tool

import * as THREE from 'three';

const MOUSE_LOOK_SENSITIVITY = 0.0045;

// M1.2 — all movement is now YARDS PER SECOND (M1.1 made a yard a real yard).
// These reproduce the old per-frame feel at 60fps (3.0 and 10.0 per frame) so
// the editor flycam handles exactly as before on a 60Hz display, but a 144Hz
// machine no longer moves 2.4x faster for the same keypress.
//
// They are FLYCAM speeds, not character speeds — for reference, a 1.12 player
// walks at 2.5 yd/s and runs at 7.0 yd/s. Those land with the character in M4.
const DEFAULT_MOVE_SPEED = 180.0;   // yd/s  (= 3.0/frame at 60fps)
const DEFAULT_SPRINT_SPEED = 600.0; // yd/s  (= 10.0/frame at 60fps)

// Arrow-key look, radians per second (= 0.03 and 0.015 per frame at 60fps).
const TURN_SPEED = 1.8;
const TILT_SPEED = 0.9;

export const MOVEMENT_DEFAULTS = {
    move: DEFAULT_MOVE_SPEED,
    sprint: DEFAULT_SPRINT_SPEED
};

// ─────────────────────────────────────────────────────────────────────────────
// 1. Movement ticker
// ─────────────────────────────────────────────────────────────────────────────
//
// Returns a function compatible with Viewport.addTicker. Suppressed when the
// active tool is 'place-wmo' so the camera doesn't fly off during placement.
// Exposes .setMoveSpeed and .setSprintSpeed for the options modal.

export function createMovementTicker(editor) {
    const moveKeys = {};
    let sprinting = false;
    let moveSpeed = DEFAULT_MOVE_SPEED;
    let sprintSpeed = DEFAULT_SPRINT_SPEED;

    document.addEventListener('keydown', (e) => {
        if (e.target.tagName === 'INPUT' || e.target.tagName === 'BUTTON' || e.target.tagName === 'SELECT') {
            e.target.blur();
        }
        if (e.target.tagName === 'INPUT' || e.target.tagName === 'SELECT') return;
        moveKeys[e.code] = true;
        if (e.code === 'ShiftLeft' || e.code === 'ShiftRight') sprinting = true;
    });
    document.addEventListener('keyup', (e) => {
        moveKeys[e.code] = false;
        if (e.code === 'ShiftLeft' || e.code === 'ShiftRight') sprinting = false;
    });

    const ticker = (vp, dt) => {
        // Tolerate a caller that predates the dt argument.
        if (!(dt > 0)) dt = (vp && vp.deltaTime > 0) ? vp.deltaTime : 1 / 60;
        if (editor.tools.activeId === 'place-wmo') return; // suppress during placement

        const camera = vp.rig.camera;
        const controls = vp.rig.controls;
        const walk = vp.rig.walk;

        const forward = new THREE.Vector3();
        camera.getWorldDirection(forward);
        forward.y = 0;
        if (forward.lengthSq() > 0) forward.normalize();

        const right = new THREE.Vector3();
        right.crossVectors(forward, new THREE.Vector3(0, 1, 0)).normalize();

        const delta = new THREE.Vector3();
        if (moveKeys['KeyW']) delta.add(forward);
        if (moveKeys['KeyS']) delta.sub(forward);
        if (moveKeys['KeyD']) delta.add(right);
        if (moveKeys['KeyA']) delta.sub(right);
        if (moveKeys['KeyE'] || moveKeys['Space']) delta.y += 1;
        if (moveKeys['KeyQ']) delta.y -= 1;

        if (delta.lengthSq() > 0) {
            const speed = (sprinting ? sprintSpeed : moveSpeed) * dt;
            delta.normalize().multiplyScalar(speed);
            camera.position.add(delta);
            controls.target.add(delta);
        }

        const turnSpeed = TURN_SPEED * dt;
        const tiltSpeed = TILT_SPEED * dt;
        const arrowPressed = moveKeys['ArrowLeft'] || moveKeys['ArrowRight'] ||
            moveKeys['ArrowUp'] || moveKeys['ArrowDown'];
        if (!arrowPressed) return;

        if (walk.mode) {
            if (!walk.inited) {
                const dir2 = new THREE.Vector3();
                camera.getWorldDirection(dir2);
                walk.yaw = Math.atan2(dir2.x, dir2.z);
                walk.pitch = Math.asin(Math.max(-0.95, Math.min(0.95, dir2.y)));
                walk.inited = true;
            }
            if (moveKeys['ArrowLeft']) walk.yaw += turnSpeed;
            if (moveKeys['ArrowRight']) walk.yaw -= turnSpeed;
            if (moveKeys['ArrowUp']) walk.pitch += tiltSpeed;
            if (moveKeys['ArrowDown']) walk.pitch -= tiltSpeed;
            walk.pitch = Math.max(-1.4, Math.min(1.4, walk.pitch));
            vp.rig.applyWalkLook();
        } else {
            const offset = new THREE.Vector3().subVectors(controls.target, camera.position);
            const spherical = new THREE.Spherical().setFromVector3(offset);
            if (moveKeys['ArrowLeft'] || moveKeys['ArrowRight']) {
                spherical.theta += moveKeys['ArrowLeft'] ? turnSpeed : -turnSpeed;
            }
            if (moveKeys['ArrowUp'] || moveKeys['ArrowDown']) {
                spherical.phi += moveKeys['ArrowDown'] ? tiltSpeed : -tiltSpeed;
                spherical.phi = Math.max(0.1, Math.min(Math.PI - 0.1, spherical.phi));
            }
            offset.setFromSpherical(spherical);
            controls.target.copy(camera.position).add(offset);
        }
    };

    ticker.setMoveSpeed = (v) => { moveSpeed = v; };
    ticker.setSprintSpeed = (v) => { sprintSpeed = v; };

    return ticker;
}

// ─────────────────────────────────────────────────────────────────────────────
// 2. Walk-look — right-click drag → yaw/pitch when walk mode is on
// ─────────────────────────────────────────────────────────────────────────────
//
// Only active while rig.walk.mode is true. The capture-phase pointerdown
// runs before OrbitControls so we own right-click while walking. (RIGHT is
// also explicitly disabled in OrbitControls.mouseButtons, so this is belt
// and suspenders.)

export function attachWalkLook(editor) {
    const canvas = editor.viewport.canvas;
    const rig = editor.viewport.rig;

    canvas.addEventListener('pointerdown', (e) => {
        if (e.button !== 2 || !rig.walk.mode) return;
        rig.walk.rightMouseDown = true;
        rig.walk.lastMouseX = e.clientX;
        rig.walk.lastMouseY = e.clientY;

        if (!rig.walk.inited) {
            const dir = new THREE.Vector3();
            rig.camera.getWorldDirection(dir);
            rig.walk.yaw = Math.atan2(dir.x, dir.z);
            rig.walk.pitch = Math.asin(Math.max(-0.95, Math.min(0.95, dir.y)));
            rig.walk.inited = true;
        }
        e.preventDefault();
        e.stopImmediatePropagation();
    }, true);

    document.addEventListener('pointerup', (e) => {
        if (e.button === 2) rig.walk.rightMouseDown = false;
    });

    document.addEventListener('pointermove', (e) => {
        if (!rig.walk.rightMouseDown || !rig.walk.mode) return;
        const dx = e.clientX - rig.walk.lastMouseX;
        const dy = e.clientY - rig.walk.lastMouseY;
        rig.walk.lastMouseX = e.clientX;
        rig.walk.lastMouseY = e.clientY;
        rig.walk.yaw -= dx * MOUSE_LOOK_SENSITIVITY;
        rig.walk.pitch -= dy * MOUSE_LOOK_SENSITIVITY;
        rig.walk.pitch = Math.max(-1.4, Math.min(1.4, rig.walk.pitch));
        rig.applyWalkLook();
    });
}

// ─────────────────────────────────────────────────────────────────────────────
// 3. Keyboard — global Esc + Ctrl-Z/Ctrl-Y + tool forward
// ─────────────────────────────────────────────────────────────────────────────
//
// Movement keys (WASD, arrows, Shift) are owned by createMovementTicker —
// it attaches its own keydown/keyup listeners.

export function attachKeyboard(editor, modalRegistry) {
    document.addEventListener('keydown', (ev) => {
        const tag = ev.target.tagName;
        const inField = (tag === 'INPUT' || tag === 'SELECT' || tag === 'TEXTAREA');

        // Ctrl-Z / Ctrl-Y → history
        if (!inField && (ev.ctrlKey || ev.metaKey)) {
            if (ev.code === 'KeyZ' && !ev.shiftKey) {
                editor.history.undo();
                ev.preventDefault();
                return;
            }
            if ((ev.code === 'KeyZ' && ev.shiftKey) || ev.code === 'KeyY') {
                editor.history.redo();
                ev.preventDefault();
                return;
            }
        }

        // Phase 8: B → sculpt tool toggle
        if (!inField && ev.code === 'KeyB' && !ev.ctrlKey && !ev.metaKey) {
            const current = editor.tools.activeId;
            editor.tools.setActive(current === 'sculpt' ? 'select' : 'sculpt');
            ev.preventDefault();
            return;
        }

        // Forward to active tool
        const active = editor.tools.active;
        if (active && typeof active.onKeyDown === 'function') {
            try {
                const handled = active.onKeyDown(ev);
                if (handled) return;
            } catch (err) { console.error('onKeyDown', err); }
        }

        // Escape — close modals (registered first-wins)
        if (!inField && ev.code === 'Escape' && modalRegistry) {
            for (const m of modalRegistry) {
                if (m && typeof m.closeIfOpen === 'function') m.closeIfOpen();
            }
        }
    });

    document.addEventListener('keyup', (ev) => {
        const active = editor.tools.active;
        if (active && typeof active.onKeyUp === 'function') {
            try { active.onKeyUp(ev); }
            catch (err) { console.error('onKeyUp', err); }
        }
    });
}