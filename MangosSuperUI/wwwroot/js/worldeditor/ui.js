// ui.js — UI overlays and the options modal.
//
// Sections:
//   1. Status — proxy for #weStatus text
//   2. HUD — small FPS counter top-left
//   3. Compass — bottom-right dial + coords + fullscreen button
//   4. OptionsModal — gear button + editor settings panel
//   5. addToolbarShortcuts — Walk + Map toolbar buttons

import * as THREE from 'three';
import { setLitMode, setWireframe, setTerrainDetail, isTerrainDetailOn } from './render.js';
import { MOVEMENT_DEFAULTS } from './input.js';
import { setSplatEnabled, isSplatEnabled } from './terrain-splat.js';
import { setAuthoredNormals, isAuthoredNormals } from './streaming.js';
import { FOLIAGE_DEFAULTS } from './foliage.js';

// ─────────────────────────────────────────────────────────────────────────────
// 1. Status — bottom status bar
// ─────────────────────────────────────────────────────────────────────────────
//
// The element with id="weStatus" already exists in the Razor view; we just
// proxy text changes through here so other modules don't reach for it directly.

export const Status = {
    set(msg) {
        const el = document.getElementById('weStatus');
        if (el) el.textContent = msg;
    },
    clear() { Status.set(''); }
};

// ─────────────────────────────────────────────────────────────────────────────
// 2. HUD — stats counters
// ─────────────────────────────────────────────────────────────────────────────
//
// Two modes:
//   - Bound:    cshtml has #weFps + #weDoodadCount + #weWmoCount + #weModelCount.
//               HUD writes counts directly into those spans, no overlay.
//   - Floating: cshtml has none of them. HUD createElement's an overlay div
//               with FPS only. This is the safe fallback for hosts that
//               embed the editor in a different view.
//
// Counts read from runtime state on every tick. They're cheap (a few Object
// key reads, no traversal), but only the FPS changes frame-to-frame in most
// cases, so the writes are guarded by "did the value actually change".

export class HUD {
    constructor(editor) {
        this.editor = editor;

        // Try to bind to cshtml spans first.
        this.elFps = document.getElementById('weFps');
        this.elDoodads = document.getElementById('weDoodadCount');
        this.elWmos = document.getElementById('weWmoCount');
        this.elModels = document.getElementById('weModelCount');

        this.bound = !!(this.elFps && this.elDoodads && this.elWmos && this.elModels);

        if (!this.bound) {
            // Floating fallback — single FPS overlay.
            const el = document.createElement('div');
            el.style.cssText = 'position:absolute;top:8px;left:8px;padding:2px 8px;' +
                'background:rgba(0,0,0,0.5);color:#9eff9e;font-family:monospace;font-size:11px;' +
                'border-radius:3px;pointer-events:none;z-index:10;';
            el.textContent = 'FPS: --';
            editor.viewport.canvas.parentElement.appendChild(el);
            this.elFps = el;
        }

        this._lastFps = -1;
        this._lastDoodads = -1;
        this._lastWmos = -1;
        this._lastModels = -1;
    }

    tick() {
        const fps = this.editor.viewport.currentFps;
        if (fps !== this._lastFps) {
            this._lastFps = fps;
            this.elFps.textContent = this.bound ? String(fps) : ('FPS: ' + fps);
        }

        if (!this.bound) return;

        // Doodad / WMO counts come from ObjectStream's activePlacements,
        // discriminated by `kind: 'd' | 'w'`. Custom placed WMOs are in
        // placementStore.placedWmos (alias on PlacementStore — Phase 7
        // will rename to .placed).
        let doodads = 0, wmos = 0;
        const stream = this.editor.objectStream;
        if (stream) {
            const ap = stream.activePlacements;
            for (const id in ap) {
                if (ap[id].kind === 'd') doodads++;
                else if (ap[id].kind === 'w') wmos++;
            }
        }
        const store = this.editor.placementStore;
        if (store && store.placedWmos) wmos += store.placedWmos.length;

        // Unique model archetypes currently loaded into the pool.
        const pool = stream && stream.pool;
        const models = pool && pool.sets ? Object.keys(pool.sets).length : 0;

        if (doodads !== this._lastDoodads) {
            this._lastDoodads = doodads;
            this.elDoodads.textContent = doodads;
        }
        if (wmos !== this._lastWmos) {
            this._lastWmos = wmos;
            this.elWmos.textContent = wmos;
        }
        if (models !== this._lastModels) {
            this._lastModels = models;
            this.elModels.textContent = models;
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 3. Compass + coords + fullscreen button
// ─────────────────────────────────────────────────────────────────────────────

export class Compass {
    constructor(editor) {
        this.editor = editor;
        this.canvas = editor.viewport.canvas;
        this._build();
    }

    _build() {
        const parent = this.canvas.parentElement;
        parent.style.position = 'relative';

        // Compass dial
        this.compassEl = document.createElement('div');
        this.compassEl.style.cssText = 'position:absolute;bottom:60px;right:16px;width:80px;height:80px;' +
            'border-radius:50%;background:rgba(0,0,0,0.6);border:2px solid rgba(255,255,255,0.3);' +
            'pointer-events:none;z-index:10;';

        const labels = [
            { txt: 'N', x: '50%', y: '6px', tx: '-50%', ty: '0' },
            { txt: 'S', x: '50%', b: '4px', tx: '-50%', ty: '0' },
            { txt: 'E', r: '6px', y: '50%', tx: '0', ty: '-50%' },
            { txt: 'W', x: '6px', y: '50%', tx: '0', ty: '-50%' }
        ];
        labels.forEach((l) => {
            const lbl = document.createElement('span');
            lbl.textContent = l.txt;
            lbl.style.cssText = 'position:absolute;color:' + (l.txt === 'N' ? '#ff4444' : 'rgba(255,255,255,0.7)') +
                ';font-size:11px;font-weight:bold;';
            if (l.x) lbl.style.left = l.x;
            if (l.y) lbl.style.top = l.y;
            if (l.r) lbl.style.right = l.r;
            if (l.b) lbl.style.bottom = l.b;
            lbl.style.transform = 'translate(' + l.tx + ',' + l.ty + ')';
            this.compassEl.appendChild(lbl);
        });

        this.arrow = document.createElement('div');
        this.arrow.style.cssText = 'position:absolute;left:50%;top:50%;width:4px;height:28px;' +
            'margin-left:-2px;margin-top:-24px;transform-origin:2px 24px;' +
            'background:linear-gradient(to bottom, #ff4444 50%, #ffffff 50%);border-radius:2px;';
        this.compassEl.appendChild(this.arrow);

        const dot = document.createElement('div');
        dot.style.cssText = 'position:absolute;left:50%;top:50%;width:6px;height:6px;margin:-3px;' +
            'border-radius:50%;background:#fff;';
        this.compassEl.appendChild(dot);

        parent.appendChild(this.compassEl);

        // Coords
        this.coords = document.createElement('div');
        this.coords.style.cssText = 'position:absolute;bottom:16px;right:16px;padding:4px 10px;' +
            'background:rgba(0,0,0,0.6);color:#ccc;font-size:12px;font-family:monospace;' +
            'border-radius:4px;pointer-events:none;z-index:10;';
        this.coords.textContent = 'X: 0  Z: 0';
        parent.appendChild(this.coords);

        // Fullscreen toggle
        const fsBtn = document.createElement('button');
        fsBtn.innerHTML = '&#x26F6;';
        fsBtn.title = 'Toggle fullscreen';
        fsBtn.style.cssText = 'position:absolute;bottom:148px;right:16px;width:36px;height:36px;' +
            'background:rgba(0,0,0,0.6);color:#ccc;border:1px solid rgba(255,255,255,0.3);' +
            'border-radius:6px;font-size:18px;cursor:pointer;z-index:10;display:flex;' +
            'align-items:center;justify-content:center;';
        fsBtn.addEventListener('click', () => {
            const el = this.canvas.parentElement;
            if (!document.fullscreenElement) {
                (el.requestFullscreen || el.webkitRequestFullscreen || el.msRequestFullscreen).call(el);
            } else {
                (document.exitFullscreen || document.webkitExitFullscreen || document.msExitFullscreen).call(document);
            }
        });
        parent.appendChild(fsBtn);

        document.addEventListener('fullscreenchange', () => {
            if (document.fullscreenElement) {
                this.editor.viewport.rememberPreFullscreen();
            } else {
                // Chrome ate Esc to exit fullscreen — if placement was active
                // (it needs the cursor), drop to select.
                if (this.editor.tools.activeId === 'place-wmo') {
                    this.editor.tools.setActive('select');
                }
            }
            setTimeout(() => this.editor.viewport.resize(), 100);
            setTimeout(() => this.editor.viewport.resize(), 300);
        });
    }

    tick() {
        const dir = new THREE.Vector3();
        this.editor.viewport.rig.camera.getWorldDirection(dir);
        const angle = Math.atan2(dir.x, dir.z);
        this.arrow.style.transform = 'rotate(' + (-angle * 180 / Math.PI) + 'deg)';

        const p = this.editor.viewport.rig.camera.position;

        // Convert Three.js scene coords → WoW world coords (.gps).
        // Inverse of the forward transform in WorldEditor_TechnicalRef §13.6:
        //   modfPosX = (meshX / (128 * CELL) + 0.5 + gridY) * 533.33
        //   modfPosZ = (meshZ / (128 * CELL) + 0.5 + gridX) * 533.33
        //   wowX = 32*533.33 - modfPosZ   (axis swap!)
        //   wowY = 32*533.33 - modfPosX   (axis swap!)
        //   wowZ = meshY / heightScale + midHeight
        const tg = this.editor.tileGrid;
        if (tg && tg.tileWidthMesh > 0 && tg.globalHeightScale) {
            const CELL = tg.tileWidthMesh / 128;  // ~4.167
            const GRID = 533.333;
            const gx = tg.centerGridX;    // preset gridX (row)
            const gy = tg.centerGridY;    // preset gridY (col)

            const modfPosX = (p.x / (128 * CELL) + 0.5 + gy) * GRID;
            const modfPosZ = (p.z / (128 * CELL) + 0.5 + gx) * GRID;
            const wowX = 32 * GRID - modfPosZ;
            const wowY = 32 * GRID - modfPosX;
            const wowZ = p.y / tg.globalHeightScale + tg.globalMidHeight;

            this.coords.textContent =
                'X:' + Math.round(wowX) +
                ' Y:' + Math.round(wowY) +
                ' Z:' + Math.round(wowZ);
        } else {
            // Fallback before heightmap loads — raw scene coords.
            this.coords.textContent = 'X:' + p.x.toFixed(0) + ' Y:' + p.z.toFixed(0) + ' Z:' + p.y.toFixed(0);
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 4. OptionsModal — gear button + editor settings panel
// ─────────────────────────────────────────────────────────────────────────────
//
// Controls: Speed, Walk Mode, Draw Distance, Lighting (lit/flat),
// Terrain Detail (texture pixels-per-chunk), Doodads/Buildings/Wireframe.

export class OptionsModal {
    constructor(editor, movementTicker, loadPresetByKey) {
        this.editor = editor;
        this._movementTicker = movementTicker;
        this._loadPresetByKey = loadPresetByKey;
        this._build();
    }

    isOpen() { return this._modal && this._modal.style.display !== 'none'; }
    closeIfOpen() { if (this.isOpen()) this._hide(); }

    _build() {
        const toolbar = document.getElementById('weLoadBtn');
        if (!toolbar || !toolbar.parentElement) return;
        const container = toolbar.parentElement;

        const optBtn = document.createElement('button');
        optBtn.textContent = '\u2699 Options';
        optBtn.className = 'btn btn-sm btn-dark';
        optBtn.style.cssText = 'margin-left:8px;font-size:12px;padding:2px 10px;';
        container.appendChild(optBtn);

        const backdrop = document.createElement('div');
        backdrop.style.cssText = 'display:none;position:fixed;top:0;left:0;width:100%;height:100%;background:rgba(0,0,0,0.5);z-index:9998;';
        this.editor.viewport.canvas.parentElement.appendChild(backdrop);

        const modal = document.createElement('div');
        modal.style.cssText = 'display:none;position:fixed;top:50%;left:50%;transform:translate(-50%,-50%);' +
            'background:#1e1e2a;color:#ddd;border:1px solid #444;border-radius:10px;padding:24px 28px;' +
            'z-index:9999;min-width:340px;max-width:420px;font-family:system-ui,sans-serif;' +
            'box-shadow:0 8px 32px rgba(0,0,0,0.6);';
        this.editor.viewport.canvas.parentElement.appendChild(modal);
        this._modal = modal;

        const show = () => { modal.style.display = 'block'; backdrop.style.display = 'block'; };
        const hide = () => { modal.style.display = 'none'; backdrop.style.display = 'none'; };
        this._hide = hide;
        optBtn.addEventListener('click', show);
        backdrop.addEventListener('click', hide);

        const row = (label, id, type, opts) => {
            let r = '<div style="display:flex;align-items:center;justify-content:space-between;margin-bottom:12px;">' +
                '<label style="font-size:13px;color:#bbb;min-width:100px;">' + label + '</label>';
            if (type === 'slider') {
                r += '<div style="display:flex;align-items:center;gap:8px;flex:1;justify-content:flex-end;">' +
                    '<input type="range" id="' + id + '" min="' + opts.min + '" max="' + opts.max + '" value="' + opts.val + '" style="width:120px;cursor:pointer;">' +
                    '<span id="' + id + 'Val" style="font-size:12px;font-family:monospace;min-width:36px;text-align:right;">' + opts.display + '</span></div>';
            } else if (type === 'toggle') {
                r += '<button id="' + id + '" class="btn btn-sm ' + (opts.active ? 'btn-outline-warning active' : 'btn-outline-secondary') + '" ' +
                    'style="font-size:11px;padding:2px 12px;min-width:60px;">' + opts.textOn + '</button>';
            } else if (type === 'select') {
                r += '<select id="' + id + '" style="background:#2a2a3a;color:#ddd;border:1px solid #555;border-radius:4px;padding:2px 8px;font-size:12px;">';
                opts.options.forEach((o) => {
                    r += '<option value="' + o.value + '"' + (o.value === opts.val ? ' selected' : '') + '>' + o.label + '</option>';
                });
                r += '</select>';
            }
            r += '</div>';
            return r;
        };
        const divider = (title) =>
            '<div style="font-size:11px;color:#777;text-transform:uppercase;letter-spacing:1px;margin:16px 0 8px;border-bottom:1px solid #333;padding-bottom:4px;">' + title + '</div>';

        let html =
            '<div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:16px;">' +
            '<span style="font-size:16px;font-weight:600;color:#fff;">\u2699 Editor Options</span>' +
            '<button id="weOptClose" style="background:none;border:none;color:#999;font-size:20px;cursor:pointer;padding:0 4px;">\u2715</button>' +
            '</div>';

        html += divider('Camera & Movement');
        html += row('Speed', 'optSpeed', 'slider', { min: 0, max: 30, val: 10, display: '1.0x' });
        html += row('Walk Mode', 'optWalk', 'toggle', { active: false, textOn: 'Off' });

        html += divider('Rendering');
        html += row('Draw Distance', 'optDraw', 'slider', { min: 1, max: 30, val: 10, display: '1.0x' });
        html += row('Lighting', 'optLit', 'toggle', { active: true, textOn: 'Lit' });
        html += row('Terrain Splat', 'optSplat', 'toggle', { active: true, textOn: 'On' });
        html += row('Detail Overlay', 'optDetailOverlay', 'toggle',
            { active: true, textOn: 'On' });
        html += row('MCNR Normals', 'optMcnr', 'toggle', { active: true, textOn: 'On' });
        html += row('Terrain Detail', 'optDetail', 'select', {
            val: '128',
            options: [
                { value: '64', label: 'Low (1024²)' },
                { value: '128', label: 'Medium (2048²)' },
                { value: '256', label: 'High (4096²)' }
            ]
        });

        // FOLIAGE. Three knobs, not the fifteen MSUIClient's dev panel carries:
        // this is a player-facing surface. The authenticity switches
        // (UseCellLayerMap / UseNoDoodadMask / SkipHoles) are deliberately NOT
        // here — SYSTEM_FOLIAGE calls them diagnostics, not settings, and they
        // stay reachable on window.we.foliage for A/B work.
        //
        // Coverage and visibility are ONE intent: FoliageField links the fade
        // window to Radius by default, because a fixed 45-yard fade against a
        // 120-yard Radius slider scattered grass nobody could see.
        // WORLD LIGHTING. The one switch that decides whether the client
        // believes Light.dbc. MSUIClient shipped a build where the equivalent
        // switch was accidentally the DevTools flag, so the resolve never ran
        // and every colour silently fell back to the invented constants the
        // system exists to replace. Keep this a user setting and nothing else.
        //
        // Sun and ambient STRENGTH are deliberately not exposed: 1.0 means
        // "use the data exactly", which is the correctness check, and a slider
        // that quietly moves off 1.0 destroys it. They live on
        // window.we.lighting for A/B work.
        html += divider('World Lighting');
        html += row('Light.dbc', 'optWorldLight', 'toggle', { active: false, textOn: 'Off' });
        html += row('Authored Sky', 'optWorldSky', 'toggle', { active: true, textOn: 'On' });
        html += row('Time of Day', 'optTimeOfDay', 'slider',
            { min: 0, max: 1440, val: 720, display: '12:00' });
        html += row('Day Cycle', 'optDayCycle', 'select', {
            val: '0',
            options: [
                { value: '0', label: 'Frozen' },
                { value: '0.0167', label: '1 hour / minute' },
                { value: '0.1', label: '1 hour / 10 sec' }
            ]
        });

        html += divider('Foliage');
        html += row('Foliage', 'optFoliage', 'toggle', { active: false, textOn: 'Off' });
        html += row('Density', 'optFoliageDensity', 'slider',
            { min: 0, max: 20, val: Math.round(FOLIAGE_DEFAULTS.densityScale * 10),
              display: FOLIAGE_DEFAULTS.densityScale.toFixed(1) + 'x' });
        html += row('Draw Radius', 'optFoliageRadius', 'slider',
            { min: 15, max: 120, val: FOLIAGE_DEFAULTS.radius,
              display: FOLIAGE_DEFAULTS.radius + 'yd' });

        html += divider('Visibility');
        html += row('Doodads', 'optDoodads', 'toggle', { active: true, textOn: 'On' });
        html += row('Buildings', 'optWmos', 'toggle', { active: true, textOn: 'On' });
        html += row('Wireframe', 'optWire', 'toggle', { active: false, textOn: 'Off' });

        modal.innerHTML = html;
        modal.querySelector('#weOptClose').addEventListener('click', hide);

        const editor = this.editor;

        // Speed slider
        const speedSlider = modal.querySelector('#optSpeed');
        const speedVal = modal.querySelector('#optSpeedVal');
        speedSlider.addEventListener('input', () => {
            const mult = parseInt(speedSlider.value) / 10;
            // M1.2: speeds are yards/second now (see input.js MOVEMENT_DEFAULTS).
            this._movementTicker.setMoveSpeed(MOVEMENT_DEFAULTS.move * mult);
            this._movementTicker.setSprintSpeed(MOVEMENT_DEFAULTS.sprint * mult);
            speedVal.textContent = mult.toFixed(1) + 'x';
        });

        // Draw distance slider
        const drawSlider = modal.querySelector('#optDraw');
        const drawVal = modal.querySelector('#optDrawVal');
        drawSlider.addEventListener('input', () => {
            const drawMult = parseInt(drawSlider.value) / 10;
            const tg = editor.tileGrid;
            tg.setTileRadius(Math.max(1, Math.round(drawMult)));
            drawVal.textContent = drawMult.toFixed(1) + 'x';
            tg.updateFogForRadius(editor.viewport.scene, editor.viewport.rig.camera, drawMult);
            const radii = tg.objectRadiiForCurrent();
            if (editor.objectStream) editor.objectStream.setLoadRadii(radii.load, radii.unload);
            if (editor.currentPreset && tg.tileWidthMesh > 0) {
                const cam = tg.cameraToGrid(editor.viewport.rig.controls.target);
                Object.keys(tg.tiles).forEach((key) => {
                    const t = tg.tiles[key];
                    const dgx = t.gridX - cam.gridX;
                    const dgy = t.gridY - cam.gridY;
                    if (Math.abs(dgx) > tg.UNLOAD_RADIUS || Math.abs(dgy) > tg.UNLOAD_RADIUS) {
                        tg.unloadTile(key);
                    }
                });
            }
        });

        // Walk mode toggle (in modal)
        const walkBtn = modal.querySelector('#optWalk');
        const syncWalkBtn = () => {
            walkBtn.classList.toggle('active', editor.viewport.rig.walk.mode);
            walkBtn.classList.toggle('btn-outline-warning', editor.viewport.rig.walk.mode);
            walkBtn.classList.toggle('btn-outline-secondary', !editor.viewport.rig.walk.mode);
            walkBtn.textContent = editor.viewport.rig.walk.mode ? 'On' : 'Off';
        };
        walkBtn.addEventListener('click', () => {
            if (editor.viewport.rig.walk.mode) editor.viewport.rig.leaveWalkMode();
            else editor.viewport.rig.enterWalkMode();
            editor.signals.walkModeChanged.dispatch(editor.viewport.rig.walk.mode);
            syncWalkBtn();
        });
        editor.signals.walkModeChanged.add(syncWalkBtn);

        // Terrain splat. Real 4-layer blending vs the server-baked composite.
        // Switching reloads the preset because it changes how every tile's
        // material is built, and rebuilding them in place is not worth the code.
        const splatBtn = modal.querySelector('#optSplat');
        if (splatBtn) {
            const syncSplat = () => {
                const on = isSplatEnabled();
                splatBtn.textContent = on ? 'On' : 'Off';
                splatBtn.classList.toggle('active', on);
                splatBtn.classList.toggle('btn-outline-warning', on);
                splatBtn.classList.toggle('btn-outline-secondary', !on);
            };
            splatBtn.addEventListener('click', () => {
                setSplatEnabled(!isSplatEnabled());
                syncSplat();
                if (this._loadPresetByKey && editor.currentPreset) {
                    const el = document.getElementById('wePresetSelect');
                    const opt = el ? el.options[el.selectedIndex] : null;
                    this._loadPresetByKey(editor.currentPreset,
                        opt ? opt.textContent : editor.currentPreset);
                }
            });
            syncSplat();
        }

        // Terrain detail overlay (M4.2c). A toggle rather than a fixed
        // behaviour because it injects a shader chunk into every terrain
        // material — if a driver rejects it, turning this off restores plain
        // terrain without a reload.
        const detailBtn = modal.querySelector('#optDetailOverlay');
        if (detailBtn) {
            const syncDetail = () => {
                const on = isTerrainDetailOn();
                detailBtn.textContent = on ? 'On' : 'Off';
                detailBtn.classList.toggle('active', on);
                detailBtn.classList.toggle('btn-outline-warning', on);
                detailBtn.classList.toggle('btn-outline-secondary', !on);
            };
            detailBtn.addEventListener('click', () => {
                setTerrainDetail(!isTerrainDetailOn());
                syncDetail();
            });
            syncDetail();
        }

        // World lighting: Light.dbc -> LightParams -> Light(Int|Float)Band.
        const wlBtn = modal.querySelector('#optWorldLight');
        if (wlBtn) {
            const syncWl = () => {
                const on = !!(editor.worldLighting && editor.worldLighting.enabled);
                wlBtn.textContent = on ? 'On' : 'Off';
                wlBtn.classList.toggle('active', on);
                wlBtn.classList.toggle('btn-outline-warning', on);
                wlBtn.classList.toggle('btn-outline-secondary', !on);
            };
            wlBtn.addEventListener('click', () => {
                if (!editor.worldLighting) return;
                editor.worldLighting.setEnabled(!editor.worldLighting.enabled);
                syncWl();
            });
            syncWl();
        }

        const skyBtn = modal.querySelector('#optWorldSky');
        if (skyBtn) {
            const syncSky = () => {
                const on = !!(editor.worldLighting && editor.worldLighting.skyEnabled);
                skyBtn.textContent = on ? 'On' : 'Off';
                skyBtn.classList.toggle('active', on);
                skyBtn.classList.toggle('btn-outline-warning', on);
                skyBtn.classList.toggle('btn-outline-secondary', !on);
            };
            skyBtn.addEventListener('click', () => {
                if (!editor.worldLighting) return;
                editor.worldLighting.setSkyEnabled(!editor.worldLighting.skyEnabled);
                syncSky();
            });
            syncSky();
        }

        const todSlider = modal.querySelector('#optTimeOfDay');
        const todVal = modal.querySelector('#optTimeOfDayVal');
        if (todSlider) {
            const paint = (h) => {
                const hh = Math.floor(h) % 24;
                const mm = Math.round((h - Math.floor(h)) * 60);
                todVal.textContent = String(hh).padStart(2, '0') + ':' + String(mm).padStart(2, '0');
            };
            todSlider.addEventListener('input', () => {
                const h = parseInt(todSlider.value) / 60;   // slider is minutes
                paint(h);
                if (editor.worldLighting) editor.worldLighting.timeHours = h;
            });
            paint(12);
        }

        const cycleSelect = modal.querySelector('#optDayCycle');
        if (cycleSelect) {
            cycleSelect.addEventListener('change', () => {
                if (!editor.worldLighting) return;
                // Vanilla's day IS real time — one game day per real day — so a
                // visible cycle is a viewing aid, not emulation. Frozen is the
                // default for exactly that reason.
                editor.worldLighting.cycleSpeed = parseFloat(cycleSelect.value) || 0;
            });
        }

        // Foliage: ground-effect clutter. Off by default; the first switch-on
        // fetches a per-tile payload and the grass models, so the button is
        // honest about the fact that nothing appears instantly.
        const foliageBtn = modal.querySelector('#optFoliage');
        if (foliageBtn) {
            const syncFoliage = () => {
                const on = !!(editor.foliage && editor.foliage.enabled);
                foliageBtn.textContent = on ? 'On' : 'Off';
                foliageBtn.classList.toggle('active', on);
                foliageBtn.classList.toggle('btn-outline-warning', on);
                foliageBtn.classList.toggle('btn-outline-secondary', !on);
            };
            foliageBtn.addEventListener('click', () => {
                if (!editor.foliage) return;
                editor.foliage.setEnabled(!editor.foliage.enabled);
                syncFoliage();
            });
            syncFoliage();
        }

        const foliageDensity = modal.querySelector('#optFoliageDensity');
        const foliageDensityVal = modal.querySelector('#optFoliageDensityVal');
        if (foliageDensity) {
            foliageDensity.addEventListener('input', () => {
                const v = parseInt(foliageDensity.value) / 10;
                foliageDensityVal.textContent = v.toFixed(1) + 'x';
                if (!editor.foliage) return;
                editor.foliage.densityScale = v;
                editor.foliage.forceRescatter();
            });
        }

        const foliageRadius = modal.querySelector('#optFoliageRadius');
        const foliageRadiusVal = modal.querySelector('#optFoliageRadiusVal');
        if (foliageRadius) {
            foliageRadius.addEventListener('input', () => {
                const v = parseInt(foliageRadius.value);
                foliageRadiusVal.textContent = v + 'yd';
                if (!editor.foliage) return;
                // Cost scales with AREA, so doubling this roughly quadruples the
                // instance count — maxInstances (24,000) is the backstop.
                editor.foliage.radius = v;
                editor.foliage.forceRescatter();
            });
        }

        // Authored terrain normals. A toggle because a wrong axis conversion
        // lights the whole world as though it were on its side, and the way out
        // of that must not be a reload.
        const mcnrBtn = modal.querySelector('#optMcnr');
        if (mcnrBtn) {
            const syncMcnr = () => {
                const on = isAuthoredNormals();
                mcnrBtn.textContent = on ? 'On' : 'Off';
                mcnrBtn.classList.toggle('active', on);
                mcnrBtn.classList.toggle('btn-outline-warning', on);
                mcnrBtn.classList.toggle('btn-outline-secondary', !on);
            };
            mcnrBtn.addEventListener('click', () => {
                setAuthoredNormals(!isAuthoredNormals());
                syncMcnr();
                if (editor.tileGrid && editor.tileGrid.applyNormalSource) {
                    editor.tileGrid.applyNormalSource();
                }
            });
            syncMcnr();
        }

        // Lighting
        const litBtn = modal.querySelector('#optLit');
        litBtn.addEventListener('click', () => {
            const next = !editor.viewport.lighting.isLit();
            editor.viewport.lighting.setLit(next);
            setLitMode(next);
            litBtn.classList.toggle('active', next);
            litBtn.classList.toggle('btn-outline-warning', next);
            litBtn.classList.toggle('btn-outline-secondary', !next);
            litBtn.textContent = next ? 'Lit' : 'Flat';
        });

        // Terrain detail
        const detailSelect = modal.querySelector('#optDetail');
        detailSelect.addEventListener('change', () => {
            const v = parseInt(detailSelect.value);
            editor.tileGrid.setTextureRes(v);
            if (editor.currentPreset) {
                const el = document.getElementById('weStatus');
                this._loadPresetByKey(editor.currentPreset, el ? el.textContent : editor.currentPreset);
            }
        });

        // Doodads
        const doodBtn = modal.querySelector('#optDoodads');
        let doodOn = true;
        doodBtn.addEventListener('click', () => {
            doodOn = !doodOn;
            editor.objectStream.setDoodadsVisible(doodOn);
            doodBtn.classList.toggle('active', doodOn);
            doodBtn.classList.toggle('btn-outline-warning', doodOn);
            doodBtn.classList.toggle('btn-outline-secondary', !doodOn);
            doodBtn.textContent = doodOn ? 'On' : 'Off';
        });

        // WMOs
        const wmoBtn = modal.querySelector('#optWmos');
        let wmoOn = true;
        wmoBtn.addEventListener('click', () => {
            wmoOn = !wmoOn;
            editor.objectStream.setWmosVisible(wmoOn);
            wmoBtn.classList.toggle('active', wmoOn);
            wmoBtn.classList.toggle('btn-outline-warning', wmoOn);
            wmoBtn.classList.toggle('btn-outline-secondary', !wmoOn);
            wmoBtn.textContent = wmoOn ? 'On' : 'Off';
        });

        // Wireframe
        const wireBtn = modal.querySelector('#optWire');
        let wireOn = false;
        wireBtn.addEventListener('click', () => {
            wireOn = !wireOn;
            setWireframe(wireOn);
            editor.tileGrid.applyWireframe(wireOn);
            editor.objectStream.pool.wmoGroup.traverse((child) => {
                if (child.isMesh && child.material) child.material.wireframe = wireOn;
            });
            wireBtn.classList.toggle('active', wireOn);
            wireBtn.classList.toggle('btn-outline-warning', wireOn);
            wireBtn.classList.toggle('btn-outline-secondary', !wireOn);
            wireBtn.textContent = wireOn ? 'On' : 'Off';
        });
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 5. addToolbarShortcuts — Walk + Map buttons in the top toolbar
// ─────────────────────────────────────────────────────────────────────────────

export function addToolbarShortcuts(editor) {
    const toolbar = document.getElementById('weLoadBtn');
    if (!toolbar || !toolbar.parentElement) return;
    const container = toolbar.parentElement;

    const walkBtn = document.createElement('button');
    walkBtn.textContent = 'Walk';
    walkBtn.className = 'btn btn-sm btn-dark';
    walkBtn.style.cssText = 'margin-left:8px;font-size:12px;padding:2px 8px;';
    walkBtn.title = 'Toggle walk mode (snap to terrain)';
    walkBtn.addEventListener('click', () => {
        if (editor.viewport.rig.walk.mode) editor.viewport.rig.leaveWalkMode();
        else editor.viewport.rig.enterWalkMode();
        editor.signals.walkModeChanged.dispatch(editor.viewport.rig.walk.mode);
        walkBtn.className = editor.viewport.rig.walk.mode ? 'btn btn-sm btn-primary' : 'btn btn-sm btn-dark';
        walkBtn.blur();
    });
    container.appendChild(walkBtn);

    const mapBtn = document.createElement('button');
    mapBtn.innerHTML = '<i class="fa-solid fa-map"></i>';
    mapBtn.className = 'btn btn-sm btn-dark';
    mapBtn.style.cssText = 'margin-left:8px;font-size:12px;padding:2px 8px;';
    mapBtn.title = 'Open World Map';
    mapBtn.addEventListener('click', () => {
        mapBtn.blur();
        window.location.href = '/WorldMap';
    });
    container.appendChild(mapBtn);
}