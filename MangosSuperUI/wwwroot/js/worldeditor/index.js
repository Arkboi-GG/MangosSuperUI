// index.js — boot orchestrator. Wires every subsystem to the Editor.
//
// Razor view loads only this file (plus the import map for `three`).
// Air-gapped: point the import map at locally-vendored Three.js, e.g.:
//
//   <script type="importmap">
//   { "imports": {
//       "three": "/lib/three/build/three.module.js",
//       "three/addons/": "/lib/three/examples/jsm/",
//       "three-mesh-bvh": "/lib/three-mesh-bvh/index.js"
//   } }
//   </script>
//   <script type="module" src="~/js/worldeditor/index.js"></script>

import { Editor } from './core.js';
import { Viewport, WalkMode } from './render.js';
import { TileGrid, ObjectStream } from './streaming.js';
import {
    PlacementStore,
    PlaceWmoTool,
    PlacementModal
} from './placement.js';
import {
    SelectionSet,
    OutlineProxyManager,
    SelectTool
} from './selection.js';
import { TransformGizmoManager } from './transform.js';
import { PlayerCharacter } from './character.js';
import { FoliageField } from './foliage.js';
import { WorldLighting } from './world-lighting.js';
import { CharacterController } from './character-control.js';
import { CollisionWorld } from './collision-world.js';
import { tickWater } from './water.js';
import {
    createMovementTicker,
    attachWalkLook,
    attachKeyboard
} from './input.js';
import {
    Status, HUD, Compass,
    OptionsModal,
    addToolbarShortcuts
} from './ui.js';
import { getJSON } from './net.js';

// Phase 8: terrain sculpting
import { SculptTool, SculptPanel } from './sculpt.js';

// ─────────────────────────────────────────────────────────────────────────────
// Inlined URL teleport — small enough to live here. World Map links into
// WorldEditor via ?mapId=&gridX=&gridY=&worldX=&worldY=. After consuming, we
// clean the URL so a refresh doesn't re-teleport.
// ─────────────────────────────────────────────────────────────────────────────

const TILE_YARDS = 533.33333;

function readUrlTeleport() {
    const params = new URLSearchParams(window.location.search);
    const pMapId = params.get('mapId');
    const pGridX = params.get('gridX');
    const pGridY = params.get('gridY');

    if (pMapId === null || pGridX === null || pGridY === null) return null;

    const mi = parseInt(pMapId);
    const gx = parseInt(pGridX);
    const gy = parseInt(pGridY);
    if (isNaN(mi) || isNaN(gx) || isNaN(gy)) return null;

    const syntheticPreset = '@' + mi + '_' + gx + '_' + gy;
    let cameraOffset = null;

    const pWorldX = params.get('worldX');
    const pWorldY = params.get('worldY');
    if (pWorldX !== null && pWorldY !== null) {
        const worldX = parseFloat(pWorldX);
        const worldY = parseFloat(pWorldY);
        if (!isNaN(worldX) && !isNaN(worldY)) {
            const tileCenterWX = (32 - gx - 0.5) * TILE_YARDS;
            const tileCenterWY = (32 - gy - 0.5) * TILE_YARDS;
            cameraOffset = {
                meshX: tileCenterWX - worldX,
                meshZ: tileCenterWY - worldY
            };
        }
    }
    return { preset: syntheticPreset, cameraOffset: cameraOffset };
}

function clearUrlTeleport() {
    if (window.history.replaceState) {
        window.history.replaceState({}, '', window.location.pathname);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// DOM hooks (created by Razor)
// ─────────────────────────────────────────────────────────────────────────────

const canvas = document.getElementById('weCanvas');
const presetSelect = document.getElementById('wePresetSelect');
const loadBtn = document.getElementById('weLoadBtn');

if (!canvas) throw new Error('worldeditor: #weCanvas not found');
if (!presetSelect) throw new Error('worldeditor: #wePresetSelect not found');
if (!loadBtn) throw new Error('worldeditor: #weLoadBtn not found');

// ─────────────────────────────────────────────────────────────────────────────
// Editor + Viewport
// ─────────────────────────────────────────────────────────────────────────────

const editor = new Editor();
const viewport = new Viewport(editor, canvas);

// ─────────────────────────────────────────────────────────────────────────────
// Subsystems
// ─────────────────────────────────────────────────────────────────────────────

const tileGrid = new TileGrid(editor);
const objectStream = new ObjectStream(editor);
objectStream.attachTo(editor.viewport.scene);

const placementStore = new PlacementStore(editor, objectStream.pool.wmoGroup);

editor.tileGrid = tileGrid;
editor.objectStream = objectStream;
editor.placementStore = placementStore;
editor.walkModeImpl = new WalkMode(editor);

// ── Ground-effect foliage (grass, ferns, flowers, pebbles) ───────────────────
//
// Off until switched on in Options. The scatter is cheap but it does hold a
// per-tile payload and a handful of instanced draws, and a world editor is not
// always a place you want grass in the way.
const foliage = new FoliageField(editor);
editor.foliage = foliage;
viewport.addTicker((vp, dt) => foliage.tick(vp, dt));

// Water animation. The per-liquid shader materials share SHARED.uTime, which
// tickWater advances every frame (and syncs to the fog + light rig). Without
// this ticker uTime never moves and the surface freezes — the registration was
// dropped in the Rev 3 lighting rewrite, which is why "water isn't animated
// anymore." (The materials themselves, built by streaming.js via water.js, were
// fine.)
viewport.addTicker((vp, dt) => tickWater(vp, dt));
// Grass on by default — this is a game-parity client, and it lets the ground
// effects be seen without hunting for the toggle. The scatter is tick-driven,
// so enabling here is safe before any tile has streamed in.
foliage.setEnabled(true);

// ── Authored exterior lighting (Light.dbc) ──────────────────────────────────
//
// Off until switched on in Options, and it OWNS the lighting rig while on:
// sun colour, ambient colour, fog colour, fog distances and the sky all come
// from the data. Switching it off restores the rig exactly as it was.
const worldLighting = new WorldLighting(editor);
editor.worldLighting = worldLighting;
viewport.addTicker((vp, dt) => worldLighting.tick(vp, dt));
// Light.dbc exterior lighting ON by default — it is MSUIClient's own lighting
// model and the base look we want ("outdoor lighting pretty good with light.dbc
// on from the get-go"). skyEnabled defaults true, so this also brings up the
// authored sky. The Options toggles were already marked On; the explicit enable
// had been dropped (same as the water ticker), so the actual state was off. Safe
// before tiles stream — the first tick resolves once terrain loads.
worldLighting.setEnabled(true);

// ─────────────────────────────────────────────────────────────────────────────
// Tools — 'select' (real picker, Phase 4) + 'place-wmo' + 'sculpt' (Phase 8).
// ─────────────────────────────────────────────────────────────────────────────

// Selection state replaces the placeholder Set on Editor. Construction order
// matters: SelectionSet subscribes to lifecycle signals; OutlineProxyManager
// subscribes to selectionChanged and needs viewport.outlinePass to exist
// (already set by Viewport ctor above).
editor.selection = new SelectionSet(editor);
const outlineProxies = new OutlineProxyManager(editor);

// Phase 5: TransformControls integration. Must be constructed after
// SelectionSet (subscribes to selectionChanged) and after the Viewport
// (uses rig.camera and canvas). Exposed on editor so SelectTool's G/R
// hotkeys can reach it without an import cycle.
const transformGizmo = new TransformGizmoManager(editor);
editor.transformGizmo = transformGizmo;

editor.tools.register(new SelectTool(editor));
editor.tools.register(new PlaceWmoTool(editor));

// Phase 8: terrain sculpting
const sculptTool = new SculptTool(editor);
editor.tools.register(sculptTool);
const sculptPanel = new SculptPanel(editor, sculptTool);

editor.tools.setActive('select');

// ─────────────────────────────────────────────────────────────────────────────
// Input
// ─────────────────────────────────────────────────────────────────────────────

const movementTicker = createMovementTicker(editor);
viewport.addTicker((vp, dt) => {
    // Character mode owns the camera. Running the flycam ticker alongside it
    // means two writers on camera.position — the exact shape of the M4.1
    // runaway. One authority at a time.
    if (editor.characterController && editor.characterController.enabled) return;
    movementTicker(vp, dt);
});
attachWalkLook(editor);

// ─────────────────────────────────────────────────────────────────────────────
// Loader (preset switching)
// ─────────────────────────────────────────────────────────────────────────────

let pendingTeleport = null;

function loadPresetByKey(presetKey, label) {
    // Tear down — fire BEFORE we touch anything else so listeners can capture
    // the outgoing preset.
    editor.signals.presetClearing.dispatch(editor.currentPreset);

    // Drop collision for the outgoing world; the new block loads below.
    if (editor.collisionWorld) editor.collisionWorld.clear();

    // Reset history (commands referring to deleted placements would dangle).
    editor.history.clear();

    // Clear placement store + object stream (tile grid clears its own tiles
    // internally during loadPreset).
    placementStore.clearAll();
    objectStream.clearAll();

    Status.set('Loading terrain...');
    tileGrid.loadPreset(presetKey, Status.set).then((hm) => {
        if (!hm) return; // failure path already wrote the status
        Status.set(label || hm.label || presetKey);

        // Load saved WMO placements for this preset.
        placementStore.loadSaved();
        // NOTE: collision is built LAZILY on entering Character mode (see the
        // Character button), never on preset load — that synchronous city-sized
        // BVH build is what froze Stormwind for minutes on load.

        // Apply pending teleport if any.
        if (pendingTeleport) {
            const rig = editor.viewport.rig;
            if (!rig.walk.mode) {
                rig.enterWalkMode();
                editor.signals.walkModeChanged.dispatch(true);
            }
            // M1.1: land ON the terrain at the teleport target. The old code
            // hard-coded y=30, which only worked because the scene used to be
            // centred on Y=0; at true world height it drops you underground in
            // high zones and in mid-air in low ones.
            tileGrid.frameCameraOnCentre(pendingTeleport.meshX, pendingTeleport.meshZ);
            pendingTeleport = null;
        }
    });
}

// ─────────────────────────────────────────────────────────────────────────────
// UI
// ─────────────────────────────────────────────────────────────────────────────

const placementModal = new PlacementModal(editor);
const optionsModal = new OptionsModal(editor, movementTicker, loadPresetByKey);
addToolbarShortcuts(editor);

// Phase 8: Sculpt toolbar button
(function addSculptButton() {
    const toolbar = document.getElementById('weLoadBtn');
    if (!toolbar || !toolbar.parentElement) return;
    const container = toolbar.parentElement;

    const sculptBtn = document.createElement('button');
    sculptBtn.textContent = 'Sculpt';
    sculptBtn.id = 'weSculptBtn';
    sculptBtn.className = 'btn btn-sm btn-dark';
    sculptBtn.style.cssText = 'margin-left:8px;font-size:12px;padding:2px 8px;';
    sculptBtn.title = 'Terrain sculpt tool (B)';
    sculptBtn.addEventListener('click', () => {
        const active = editor.tools.activeId;
        if (active === 'sculpt') {
            editor.tools.setActive('select');
        } else {
            editor.tools.setActive('sculpt');
        }
        sculptBtn.blur();
    });
    container.appendChild(sculptBtn);

    // Sync button highlight with tool changes
    editor.signals.toolChanged.add((toolId) => {
        sculptBtn.className = toolId === 'sculpt'
            ? 'btn btn-sm btn-success'
            : 'btn btn-sm btn-dark';
    });
})();

// ── M4.1: player character ───────────────────────────────────────────────────
//
// Loaded lazily on first toggle — the GLB is a few MB and the server may have
// to generate it, so paying that cost on page load for a feature nobody has
// asked for yet would be rude.
//
// The button reports its own state honestly: "Loading..." while the fetch is in
// flight, and the failure reason in the title attribute if the server could not
// build the model (usually a missing MPQ or a stale SkinnedGlbVersion).
const playerCharacter = new PlayerCharacter(editor);
editor.playerCharacter = playerCharacter;

const characterController = new CharacterController(editor, playerCharacter);
editor.characterController = characterController;

// Real building/tree/interior collision (VMaNGOS vmaps). Loaded per preset
// block; the character raycasts it instead of the render meshes. Until it
// resolves (or where vmaps are unavailable) the controller falls back to the
// old render-mesh sweep, so nothing regresses.
const collisionWorld = new CollisionWorld(editor);
editor.collisionWorld = collisionWorld;

(function addCharacterButton() {
    const anchor = document.getElementById('weLoadBtn');
    if (!anchor || !anchor.parentElement) return;
    const container = anchor.parentElement;

    const btn = document.createElement('button');
    btn.id = 'weCharacterBtn';
    btn.textContent = 'Character';
    btn.className = 'btn btn-sm btn-dark';
    btn.style.cssText = 'margin-left:8px;font-size:12px;padding:2px 8px;';
    btn.title = 'Play as a character. W/S move, A/D turn, Q/E strafe, ' +
                'Shift walk, right-drag steer (swaps A/D to strafe), ' +
                'left-drag look without turning, wheel zoom, PgUp/PgDn tilt.';

    function paint(state, detail) {
        if (state === 'loading') {
            btn.textContent = 'Loading...';
            btn.className = 'btn btn-sm btn-secondary';
            btn.disabled = true;
        } else {
            btn.disabled = false;
            btn.textContent = 'Character';
            btn.className = characterController.enabled
                ? 'btn btn-sm btn-success'
                : 'btn btn-sm btn-dark';
        }
        if (detail) btn.title = detail;
    }

    function spawnHere() {
        // Drop the character on the ground under the current view pivot, facing
        // the way the camera is looking.
        const rig = editor.viewport.rig;
        const t = rig.controls.target;
        // Face the way the camera is looking. Derived from the camera's own
        // basis rather than allocating a Vector3, so index.js does not need a
        // THREE import for one lookup.
        const e = rig.camera.matrixWorld.elements;
        const yaw = Math.atan2(-e[8], -e[10]);   // -Z column = view direction

        // Probe for real ground rather than trusting the pivot's height.
        const tg = editor.tileGrid;
        let meshes = null;
        if (tg) {
            meshes = tg.isDungeon
                ? tg.dungeonMeshes
                : (tg.terrainMeshes ? tg.terrainMeshes() : null);
        }
        const hit = rig.probeGroundY(meshes, t.x, t.z, rig.camera.position.y);
        characterController.spawnAt(
            t.x, (hit !== null && isFinite(hit)) ? hit : t.y, t.z, yaw);
    }

    btn.addEventListener('click', async () => {
        btn.blur();
        if (characterController.enabled) {
            characterController.disable();
            playerCharacter.setVisible(false);
            paint('idle', 'Play as a character');
            Status.set('Character off');
            return;
        }

        if (!playerCharacter.isLoaded) {
            paint('loading');
            try {
                await playerCharacter.load('Human', 'Male');
            } catch (err) {
                console.error('[character]', err);
                paint('idle', 'Failed: ' + err.message);
                Status.set('Character failed: ' + err.message);
                return;
            }
        }

        // Build vmap collision lazily — only when you actually enter the world,
        // never on preset load. The city-sized BVH now builds here, in the
        // background and incrementally (one chunk per turn), so it neither froze
        // the load nor hard-freezes now. Walls come online a moment after spawn.
        if (editor.collisionWorld) editor.collisionWorld.loadForPreset(editor.currentPreset).catch(() => {});

        playerCharacter.setVisible(true);
        spawnHere();
        characterController.enable();
        paint('idle');
        Status.set('Character on — W/S move, A/D turn, Q/E strafe, wheel zoom');
    });

    container.appendChild(btn);

    // A preset swap moves the world out from under the character.
    editor.signals.presetClearing.add(() => {
        if (characterController.enabled) characterController.disable();
        if (playerCharacter.isLoaded) playerCharacter.setVisible(false);
        paint('idle');
    });
})();

const compass = new Compass(editor);
const hud = new HUD(editor);

// Cheap UI tickers (compass + coords + FPS) on the viewport frame.
viewport.addTicker(() => { compass.tick(); hud.tick(); });

// Keyboard: global Esc + Ctrl-Z/Ctrl-Y + tool key dispatch.
attachKeyboard(editor, [placementModal, optionsModal, sculptPanel]);

// ─────────────────────────────────────────────────────────────────────────────
// Preset list + URL teleport
// ─────────────────────────────────────────────────────────────────────────────

loadBtn.addEventListener('click', () => {
    const preset = presetSelect.value;
    if (!preset) return;
    const label = presetSelect.options[presetSelect.selectedIndex].textContent;
    pendingTeleport = null;
    loadPresetByKey(preset, label);
});

getJSON('/WorldEditor/Presets').then((data) => {
    if (data.success && data.presets) {
        presetSelect.innerHTML = '';
        data.presets.forEach((p) => {
            const opt = document.createElement('option');
            opt.value = p.key;
            opt.textContent = p.name;
            presetSelect.appendChild(opt);
        });
    }
    // URL teleport (from World Map link)
    const tp = readUrlTeleport();
    if (tp) {
        pendingTeleport = tp.cameraOffset; // null if not provided
        editor.currentPreset = tp.preset;
        Status.set('Teleporting...');
        loadPresetByKey(tp.preset, tp.preset);
        clearUrlTeleport();
    }
});

// ─────────────────────────────────────────────────────────────────────────────
// Debug handle (dev only) — `window.we.editor`, `window.we.tools`, etc.
// ─────────────────────────────────────────────────────────────────────────────

window.we = {
    editor: editor,
    viewport: viewport,
    tools: editor.tools,
    history: editor.history,
    placement: placementStore,
    objectStream: objectStream,
    tileGrid: tileGrid,
    foliage: foliage,
    collision: collisionWorld,
    lighting: worldLighting,
    selection: editor.selection,
    outlineProxies: outlineProxies,
    transformGizmo: transformGizmo,
    sculptTool: sculptTool
};

editor.signals.sceneReady.dispatch();
