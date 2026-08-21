// MangosSuperUI — Bot Map Viewer (botmap.js)
// The spatial twin of Fleet View. Reuses the WorldMap minimap tiles + the EXACT
// WoW->Leaflet projection from worldmap.js, and overlays:
//   1. Fault dots      — every positioned incident from /Bots/FleetDiagnostics,
//                        coloured by fault category (same keys/colours as Fleet View).
//   2. Hotspot rings    — the pre-binned 100-yd cells from the same payload.
//   3. Live bot pins    — current position of every bot from /Bots/MapBots
//                        (brain GetLiveFleet), coloured by class, flagged dead/
//                        stalled/in-combat, with intent + last-failure lines.
//   4. Trails          — a bot's incidents stitched in sequence: see the loop.
// Filter by bot, by fault type, by tier, and toggle each layer. Pull-only, 4s poll.
//
// Coordinate note: incidents without an explicit map= token remain map -1
// (unknown). They stay available to the non-spatial diagnostics, but must never
// be projected onto an arbitrary continent.

(function () {
    'use strict';

    // ── projection (verbatim from worldmap.js — dots align with World Map markers) ──
    var TILE_PX = 256;
    var TILE_YARDS = 533.33333;
    function worldToLatLng(x, y) {
        var colF = 32 - (x / TILE_YARDS);
        var rowF = 32 - (y / TILE_YARDS);
        return L.latLng(-colF * TILE_PX, rowF * TILE_PX);
    }

    var MAP_DEFS = {
        'Azeroth': { folder: 'Azeroth', mapId: 0, label: 'Eastern Kingdoms' },
        'Kalimdor': { folder: 'Kalimdor', mapId: 1, label: 'Kalimdor' }
    };
    var MAP_NAMES = { 0: 'E. Kingdoms', 1: 'Kalimdor', 30: 'Alterac', 489: 'Warsong', 529: 'Arathi' };

    var CLASS_NAMES = {
        1: 'Warrior', 2: 'Paladin', 3: 'Hunter', 4: 'Rogue',
        5: 'Priest', 7: 'Shaman', 8: 'Mage', 9: 'Warlock', 11: 'Druid'
    };
    // canonical vanilla class colours
    var CLASS_COLORS = {
        1: '#C79C6E', 2: '#F58CBA', 3: '#ABD473', 4: '#FFF569',
        5: '#FFFFFF', 7: '#0070DE', 8: '#69CCF0', 9: '#9482C9', 11: '#FF7D0A'
    };

    var TIER_OPACITY = { error: 0.92, warn: 0.66, info: 0.4 };

    // ── state ──
    var map = null;
    var currentMapKey = 'Azeroth';
    var tileOverlays = [];
    var tileIndex = [];
    var didFit = false;

    var incidentLayer, hotspotLayer, botLayer, trailLayer, intentLayer, groupLayer;

    var data = null;          // last FleetDiagnostics payload (faults + hotspots + KPIs)
    var botsLive = [];        // last MapBots payload (.bots)
    var classOf = {};         // guid -> classId (from /Bots/States)
    var groupOf = {};         // guid -> groupId (from /Bots/States .groups)
    var leaderOf = {};        // guid -> true if group leader
    // Deterministic per-group colour, shared with Fleet / roster: groupId is a stable int, so index a
    // fixed palette by it — a group reads the same colour everywhere and every poll.
    var GROUP_COLORS = ['#7aa2f7', '#bb9af7', '#9ece6a', '#e0af68', '#f7768e', '#2ac3de', '#ff9e64', '#7dcfff', '#73daca', '#c0caf5', '#d7a65f', '#9d7cd8'];
    function groupColor(gid) { return gid > 0 ? GROUP_COLORS[(gid - 1) % GROUP_COLORS.length] : null; }

    var timer = null, intervalMs = 4000, paused = false;
    var popupOpen = false;    // freeze marker repaint while a popup is open (else the poll closes it)

    // filters
    var hiddenCats = {};                              // catKey -> true => hidden
    var tierOn = { error: true, warn: true, info: false };
    var layerOn = { incidents: true, hotspots: true, bots: true, trails: false };
    var selectedBots = {};                            // guid -> true; empty => all
    var botListFilter = '';

    // ══════════════════════════════════════════════════════════
    //  BOOT
    // ══════════════════════════════════════════════════════════
    $(document).ready(function () {
        if (typeof L === 'undefined') {
            $('#bmMapEmpty').css('display', 'flex').find('.bm-empty-sub')
                .text('Leaflet failed to load. Point the leaflet.css/js tags in Map.cshtml at the same path the World Map page uses.');
            return;
        }
        initMap();
        loadAvailableMaps();
        bindControls();
        bindClusterExport();
        loadBasket();
        start();
    });

    function initMap() {
        map = L.map('botmap', {
            crs: L.CRS.Simple,
            minZoom: -4,
            maxZoom: 3,
            zoomSnap: 0.5,
            zoomDelta: 0.5,
            attributionControl: false
        });
        document.getElementById('botmap').style.background = '#0a0e14';

        // draw order: hotspots under faults under trails/intent under bot pins
        hotspotLayer = L.layerGroup().addTo(map);
        incidentLayer = L.layerGroup().addTo(map);
        trailLayer = L.layerGroup().addTo(map);
        intentLayer = L.layerGroup().addTo(map);
        groupLayer = L.layerGroup().addTo(map);   // group cohesion overlay (spokes + centroid), under pins
        botLayer = L.layerGroup().addTo(map);

        map.on('mousemove', function (e) {
            var w = latLngToWorld(e.latlng);
            $('#bmCoord').text(w.x.toFixed(0) + ', ' + w.y.toFixed(0));
        });

        // Keep a clicked popup open across polls: stop repainting markers while one is
        // open (clearLayers would close it), then repaint the moment it's dismissed.
        map.on('popupopen', function () { popupOpen = true; });
        map.on('popupclose', function () { popupOpen = false; renderMarkers(); });

        switchMap('Azeroth');
    }

    function latLngToWorld(latlng) {
        var rowF = latlng.lng / TILE_PX;
        var colF = -latlng.lat / TILE_PX;
        return { x: (32 - colF) * TILE_YARDS, y: (32 - rowF) * TILE_YARDS };
    }

    // ══════════════════════════════════════════════════════════
    //  TILES (same as worldmap.js)
    // ══════════════════════════════════════════════════════════
    function loadAvailableMaps() {
        $.getJSON('/WorldMap/AvailableMaps', function (d) {
            var have = {};
            (d.maps || []).forEach(function (m) { have[m.name] = m.tileCount; });
            $('.bm-map-btn').each(function () {
                var key = $(this).data('map');
                var def = MAP_DEFS[key];
                var ok = def && have[def.folder] > 0;
                $(this).prop('disabled', !ok).attr('title', ok ? (have[def.folder] + ' tiles') : 'no tiles on disk');
            });
        });
    }

    function switchMap(mapKey) {
        if (!MAP_DEFS[mapKey]) return;
        currentMapKey = mapKey;
        didFit = false;
        $('.bm-map-btn').removeClass('active').filter(function () { return $(this).data('map') === mapKey; }).addClass('active');

        $.getJSON('/WorldMap/TileIndex?map=' + encodeURIComponent(MAP_DEFS[mapKey].folder), function (d) {
            tileIndex = (d.tiles || []).map(function (t) { return { row: t[0], col: t[1] }; });
            buildTileOverlays(MAP_DEFS[mapKey].folder);
            fitToTiles();
            renderMarkers();
        });
    }

    function buildTileOverlays(folder) {
        tileOverlays.forEach(function (ov) { map.removeLayer(ov); });
        tileOverlays = [];
        tileIndex.forEach(function (t) {
            var bounds = L.latLngBounds(
                L.latLng(-(t.col + 1) * TILE_PX, t.row * TILE_PX),
                L.latLng(-t.col * TILE_PX, (t.row + 1) * TILE_PX)
            );
            // Tiles are decoded from the client MPQs on demand (server disk-caches them)
            var url = '/WorldMap/Tile?map=' + encodeURIComponent(folder) + '&row=' + t.row + '&col=' + t.col;
            tileOverlays.push(L.imageOverlay(url, bounds, { opacity: 1, interactive: false }).addTo(map));
        });
    }

    function fitToTiles() {
        if (didFit || tileIndex.length === 0) return;
        var minRow = 999, maxRow = -1, minCol = 999, maxCol = -1;
        tileIndex.forEach(function (t) {
            minRow = Math.min(minRow, t.row); maxRow = Math.max(maxRow, t.row);
            minCol = Math.min(minCol, t.col); maxCol = Math.max(maxCol, t.col);
        });
        map.fitBounds(L.latLngBounds(
            L.latLng(-(maxCol + 1) * TILE_PX, minRow * TILE_PX),
            L.latLng(-minCol * TILE_PX, (maxRow + 1) * TILE_PX)
        ), { padding: [20, 20] });
        didFit = true;
    }

    function zeroPad(n) { return n < 10 ? '0' + n : '' + n; }
    function currentMapId() { return MAP_DEFS[currentMapKey].mapId; }
    function onCurrentMap(mapId) { return mapId === currentMapId(); }

    // ══════════════════════════════════════════════════════════
    //  POLLING
    // ══════════════════════════════════════════════════════════
    function start() {
        stop();
        tick();
        if (!paused) timer = setInterval(tick, intervalMs);
        $('#bmLiveDot').toggleClass('paused', paused);
    }
    function stop() { if (timer) { clearInterval(timer); timer = null; } }
    function tick() { fetchStates(); fetchBots(); fetchDiag(); }

    function fetchStates() {
        $.getJSON('/Bots/States', function (d) {
            classOf = {};
            ((d && d.bots) || []).forEach(function (b) { classOf[b.guid] = b.classId; });
            groupOf = {}; leaderOf = {};
            ((d && d.groups) || []).forEach(function (g) { groupOf[g.guid] = g.groupId; if (g.isGroupLeader) leaderOf[g.guid] = true; });
            var conn = (d && d.connected) || 0, tracked = (d && d.totalTracked) || 0;
            setBridge(conn > 0, conn + ' connected · ' + tracked + ' tracked');
        }).fail(function () { setBridge(false, 'Bridge offline'); });
    }

    function fetchBots() {
        $.getJSON('/Bots/MapBots', function (d) {
            botsLive = (d && d.bots) || [];
            updateEmptyOverlay();
            if (!popupOpen) renderMarkers();
        }).fail(function () { botsLive = []; });
    }

    function fetchDiag() {
        $.getJSON('/Bots/FleetDiagnostics', function (d) {
            data = d;
            $('#bmUpdated').text('updated ' + nowClock());
            updateEmptyOverlay();
            renderKpis();
            renderCatChips();
            renderBotList();
            if (!popupOpen) renderMarkers();
        }).fail(function () { $('#bmUpdated').text('request failed'); });
    }

    // Only nag "no telemetry" when there's genuinely nothing to show — brain on but
    // quiet (live bots, no faults yet) should still render the bot pins cleanly.
    function updateEmptyOverlay() {
        var noFaults = !data || data.empty;
        var noBots = !botsLive || botsLive.length === 0;
        if (noFaults && noBots) {
            $('#bmMapEmpty').css('display', 'flex').find('.bm-empty-sub')
                .text((data && data.reason) || 'Enable the engine on the IBot Monitor, then run a session.');
        } else {
            $('#bmMapEmpty').css('display', 'none');
        }
    }

    function setBridge(ok, text) {
        $('#bmBridge').removeClass('online offline').addClass(ok ? 'online' : 'offline');
        $('#bmBridgeText').text(text);
    }

    // ══════════════════════════════════════════════════════════
    //  FILTER PREDICATES
    // ══════════════════════════════════════════════════════════
    function botSelected(guid) { return Object.keys(selectedBots).length === 0 || !!selectedBots[guid]; }
    function anyBotSelected() { return Object.keys(selectedBots).length > 0; }

    function incidentVisible(i) {
        if (!i.hasPos) return false;
        if (!onCurrentMap(i.map)) return false;
        if (hiddenCats[i.category]) return false;
        if (!tierOn[i.tier]) return false;
        if (!botSelected(i.guid)) return false;
        return true;
    }

    // ══════════════════════════════════════════════════════════
    //  MARKER RENDER (rebuilds the overlay layers from data + filters)
    // ══════════════════════════════════════════════════════════
    function renderMarkers() {
        if (!map) return;
        hotspotLayer.clearLayers();
        incidentLayer.clearLayers();
        trailLayer.clearLayers();
        intentLayer.clearLayers();
        groupLayer.clearLayers();
        botLayer.clearLayers();

        if (layerOn.hotspots) renderHotspots();
        if (layerOn.incidents) renderIncidents();
        if (layerOn.trails) renderTrails();
        if (layerOn.bots) { renderGroups(); renderBots(); }

        updateShownCount();
    }

    function renderIncidents() {
        var inc = (data && data.recent) || [];
        for (var k = 0; k < inc.length; k++) {
            var i = inc[k];
            if (!incidentVisible(i)) continue;
            var m = L.circleMarker(worldToLatLng(i.x, i.y), {
                radius: 4,
                color: '#0a0e14',
                weight: 1,
                fillColor: i.color || '#7aa2f7',
                fillOpacity: TIER_OPACITY[i.tier] != null ? TIER_OPACITY[i.tier] : 0.7
            });
            m.bindTooltip(incTooltip(i), { direction: 'top', opacity: 0.95 });
            m.bindPopup(incPopup(i), { className: 'bm-popup', maxWidth: 320 });
            m.addTo(incidentLayer);
        }
    }

    function renderHotspots() {
        var hs = (data && data.hotspots) || [];
        for (var k = 0; k < hs.length; k++) {
            var h = hs[k];
            if (!onCurrentMap(h.map)) continue;
            if (hiddenCats[h.topCategory]) continue;
            if (anyBotSelected() && !(h.bots || []).some(function (n) { return selectedNameSet()[n]; })) continue;
            var r = 9 + Math.min(30, Math.sqrt(h.count) * 5);
            var c = L.circleMarker(worldToLatLng(h.x, h.y), {
                radius: r,
                color: h.color || '#f7768e',
                weight: 1.5,
                opacity: 0.85,
                fillColor: h.color || '#f7768e',
                fillOpacity: 0.16
            });
            c.bindTooltip(String(h.count), { permanent: true, direction: 'center', className: 'bm-hot-label' });
            c.bindPopup(hotPopup(h), { className: 'bm-popup', maxWidth: 320 });
            c.addTo(hotspotLayer);
        }
    }

    // Group cohesion overlay: thin spokes from each grouped member to its group centroid + a small
    // labelled centroid disc, group-coloured. Answers "who is running together" at a glance — distinct
    // from class colour (the pin fill) and the combat anchor (shown in the pin popup). Drawn under pins.
    function renderGroups() {
        var buckets = {};
        for (var k = 0; k < botsLive.length; k++) {
            var b = botsLive[k];
            if (!b.pos || !onCurrentMap(b.mapId) || !botSelected(b.guid)) continue;
            var gid = groupOf[b.guid];
            if (!gid) continue;
            (buckets[gid] || (buckets[gid] = [])).push(b);
        }
        Object.keys(buckets).forEach(function (gid) {
            var members = buckets[gid];
            if (members.length < 2) return;            // a lone visible member needs no cohesion line
            var col = groupColor(+gid);
            var cx = 0, cy = 0;
            members.forEach(function (b) { cx += b.pos.x; cy += b.pos.y; });
            cx /= members.length; cy /= members.length;
            var cll = worldToLatLng(cx, cy);
            members.forEach(function (b) {
                L.polyline([worldToLatLng(b.pos.x, b.pos.y), cll],
                    { color: col, weight: 1.5, opacity: 0.35 }).addTo(groupLayer);
            });
            L.circleMarker(cll, { radius: 4, color: col, weight: 1.5, opacity: 0.9, fillColor: col, fillOpacity: 0.4 }).addTo(groupLayer);
            L.marker(cll, {
                interactive: false, zIndexOffset: 450,
                icon: L.divIcon({
                    className: 'bm-pin-wrap',   // reuse the transparent divIcon override
                    html: '<span style="position:relative;left:6px;top:-8px;padding:0 4px;border-radius:3px;font:700 10px/1.5 monospace;color:#0a0e14;background:' + col + ';box-shadow:0 0 6px ' + col + ';white-space:nowrap;">G' + gid + '</span>',
                    iconSize: [0, 0], iconAnchor: [0, 0]
                })
            }).addTo(groupLayer);
        });
    }

    function renderBots() {
        for (var k = 0; k < botsLive.length; k++) {
            var b = botsLive[k];
            if (!b.pos) continue;
            if (!onCurrentMap(b.mapId)) continue;
            if (!botSelected(b.guid)) continue;

            var cls = classOf[b.guid];
            var col = CLASS_COLORS[cls] || '#8d96a0';
            var state = b.dead ? 'dead' : (b.stall ? 'stall' : (b.inCombat ? 'combat' : 'ok'));
            var sel = !!selectedBots[b.guid];

            var ll = worldToLatLng(b.pos.x, b.pos.y);

            // intent + failure lines for selected bots only (avoid clutter)
            if (sel) {
                if (b.target && onCurrentMap(b.target.map)) {
                    L.polyline([ll, worldToLatLng(b.target.x, b.target.y)],
                        { color: col, weight: 1.5, opacity: 0.7, dashArray: '4 4' }).addTo(intentLayer);
                }
                if (b.failure && b.failure.dest && onCurrentMap(b.failure.dest.map)) {
                    L.polyline([ll, worldToLatLng(b.failure.dest.x, b.failure.dest.y)],
                        { color: '#f7768e', weight: 1.5, opacity: 0.6, dashArray: '2 5' }).addTo(intentLayer);
                }
            }

            var m = L.marker(ll, { icon: botIcon(b, col, state, sel), zIndexOffset: sel ? 1000 : 500, riseOnHover: true });
            m.bindPopup(botPopup(b, cls), { className: 'bm-popup', maxWidth: 340 });
            m.on('click', (function (g) { return function () { /* keep selection; popup shows */ }; })(b.guid));
            m.addTo(botLayer);
        }
    }

    function botIcon(b, col, state, sel) {
        var ring = state === 'dead' ? '#f7768e' : state === 'stall' ? '#e0af68' : state === 'combat' ? '#ff9e64' : col;
        var pulse = (state === 'dead' || state === 'stall') ? ' bm-pin-pulse' : '';
        var label = sel ? ('<span class="bm-pin-label">' + esc(b.name) + ' L' + b.level + '</span>') : '';
        var glyph = state === 'dead' ? '\u2620' : '';   // skull when dead
        // Group halo: an outer ring in the group colour, layered outside the state border so a pin shows
        // all three channels at once — fill = class, border = state, halo = group.
        var gcol = groupColor(groupOf[b.guid]);
        var halo = gcol ? ';box-shadow:0 0 0 2px ' + gcol + ',0 0 5px ' + gcol : '';
        var html =
            '<div class="bm-pin' + pulse + (sel ? ' sel' : '') + '">' +
            '<span class="bm-pin-dot" style="background:' + col + ';border-color:' + ring + halo + ';">' + glyph + '</span>' +
            label + '</div>';
        return L.divIcon({ className: 'bm-pin-wrap', html: html, iconSize: [14, 14], iconAnchor: [7, 7] });
    }

    function renderTrails() {
        var inc = (data && data.recent) || [];
        // which bots to trail: selected ones, else the top few worst (avoid spaghetti)
        var guids;
        if (anyBotSelected()) guids = Object.keys(selectedBots).map(Number);
        else guids = ((data && data.byBot) || []).slice(0, 4).map(function (b) { return b.guid; });

        guids.forEach(function (g) {
            var pts = inc.filter(function (i) { return i.guid === g && i.hasPos && onCurrentMap(i.map); })
                .sort(function (a, b) { return a.seq - b.seq; });
            if (pts.length < 2) return;
            var col = CLASS_COLORS[classOf[g]] || '#7aa2f7';
            var latlngs = pts.map(function (i) { return worldToLatLng(i.x, i.y); });
            L.polyline(latlngs, { color: col, weight: 2, opacity: 0.5 }).addTo(trailLayer);
            // small vertex dots so the order/loop reads
            latlngs.forEach(function (ll, idx) {
                L.circleMarker(ll, {
                    radius: idx === latlngs.length - 1 ? 3.5 : 2,
                    color: col, weight: 1, fillColor: col,
                    fillOpacity: idx === latlngs.length - 1 ? 1 : 0.5
                }).addTo(trailLayer);
            });
        });
    }

    function selectedNameSet() {
        var s = {};
        botsLive.forEach(function (b) { if (selectedBots[b.guid]) s[b.name] = true; });
        ((data && data.byBot) || []).forEach(function (b) { if (selectedBots[b.guid]) s[b.name] = true; });
        return s;
    }

    // ── popups / tooltips ──
    function incTooltip(i) {
        return '<b style="color:' + (i.color || '#fff') + '">' + esc(i.label || i.category) + '</b> · ' + esc(i.name) + ' L' + i.level;
    }
    function incPopup(i) {
        var rows = [];
        rows.push('<div class="bm-pop-head" style="border-color:' + (i.color || '#444') + '"><b style="color:' + (i.color || '#fff') + '">' + esc(i.label || i.category) + '</b><span>' + esc(i.name) + ' · L' + i.level + ' ' + (CLASS_NAMES[classOf[i.guid]] || '') + '</span></div>');
        rows.push(kv('When', clock(i.t)));
        rows.push(kv('Where', i.x + ', ' + i.y + ' @ ' + mapName(i.map)));
        if (i.why) rows.push(kv('Why', 'why=' + esc(i.why)));
        if (i.preCmd) rows.push(kv('After', esc(i.preCmd)));
        if (i.target) rows.push(kv('Target', esc(i.target)));
        if (i.msg) rows.push('<div class="bm-pop-msg">' + esc(i.msg) + '</div>');
        rows.push('<div class="bm-pop-act"><a href="#" data-bm-focusbot="' + i.guid + '">focus this bot</a></div>');
        return rows.join('');
    }
    function hotPopup(h) {
        var cx = Math.round((h.x - 50) / 100), cy = Math.round((h.y - 50) / 100);
        var rows = [];
        rows.push('<div class="bm-pop-head" style="border-color:' + (h.color || '#444') + '"><b style="color:' + (h.color || '#fff') + '">Hotspot · ' + h.count + ' faults</b><span>' + mapName(h.map) + '</span></div>');
        rows.push(kv('Cell', h.x + ', ' + h.y + ' (100-yd)'));
        rows.push(kv('Top fault', esc(h.topCategory)));
        rows.push(kv('Bots', esc((h.bots || []).join(', '))));
        rows.push('<div class="bm-pop-act"><a href="#" data-bm-inspect="' + h.map + '|' + cx + '|' + cy + '"><i class="fa-solid fa-magnifying-glass-chart"></i> inspect cluster →</a></div>');
        return rows.join('');
    }
    function botPopup(b, cls) {
        var col = CLASS_COLORS[cls] || '#8d96a0';
        var rows = [];
        var stateTxt = b.dead ? '<span style="color:#f7768e">DEAD</span>' : b.inCombat ? '<span style="color:#ff9e64">in combat</span>' : 'alive';
        rows.push('<div class="bm-pop-head" style="border-color:' + col + '"><b style="color:' + col + '">' + esc(b.name) + '</b><span>L' + b.level + ' ' + (CLASS_NAMES[cls] || '') + ' · ' + stateTxt + '</span></div>');
        var gid = groupOf[b.guid];
        if (gid) {
            var gc = groupColor(gid);
            rows.push(kv('Group', '<span style="display:inline-block;width:9px;height:9px;border-radius:2px;background:' + gc + ';margin-right:5px;vertical-align:-1px;"></span><span style="color:' + gc + ';font-weight:700;">G' + gid + '</span>' + (leaderOf[b.guid] ? ' <span class="bm-dim">· leader</span>' : '')));
        }
        rows.push(kv('Goal', esc(b.goal) + (b.why ? ' <span class="bm-dim">(' + esc(b.why) + ')</span>' : '')));
        if (b.step) rows.push(kv('Step', esc(b.step)));
        if (b.combat) {
            if (b.combat.anchorGuid === b.guid) {
                rows.push(kv('Focus-fire', '<span style="color:#7dcfff">anchor — team assists this bot</span>'));
            } else {
                var anc = botsLive.filter(function (x) { return x.guid === b.combat.anchorGuid; })[0];
                rows.push(kv('Focus-fire', '<span style="color:#7dcfff">assisting ' + esc(anc ? anc.name : ('#' + b.combat.anchorGuid)) + '</span>'));
            }
        }
        rows.push(kv('Where', Math.round(b.pos.x) + ', ' + Math.round(b.pos.y) + ' @ ' + mapName(b.mapId)));
        rows.push(kv('HP / Mana', b.hpPct + '% / ' + b.manaPct + '%'));
        var econ = [];
        if (b.durability != null) econ.push('dur ' + b.durability + '%');
        if (b.copper != null) econ.push(money(b.copper));
        if (econ.length) rows.push(kv('Gear', econ.join(' · ')));
        if (b.stall) rows.push(kv('Stall', '<span style="color:#e0af68">' + esc(b.stall.reason) + ' · ' + b.stall.sinceSec + 's</span>'));
        if (b.failure) {
            var f = b.failure.cmd + ' → ' + b.failure.reason + (b.failure.danger ? (' (danger ' + b.failure.danger + ')') : '') + ' · ' + b.failure.ageSec + 's ago';
            rows.push(kv('Last fail', '<span style="color:#f7768e">' + esc(f) + '</span>'));
        }
        if (b.pending) rows.push(kv('Waiting', esc(b.pending.cmd) + ' → ' + esc(b.pending.expect) + ' (' + b.pending.secsToDeadline + 's)'));
        rows.push('<div class="bm-pop-act"><a href="#" data-bm-focusbot="' + b.guid + '">isolate</a> · <a href="#" data-bm-trailbot="' + b.guid + '">trail</a></div>');
        return rows.join('');
    }
    function kv(k, v) { return '<div class="bm-pop-kv"><span>' + k + '</span><b>' + v + '</b></div>'; }

    // ══════════════════════════════════════════════════════════
    //  KPI STRIP (mirror of Fleet View)
    // ══════════════════════════════════════════════════════════
    function renderKpis() {
        if (!data || data.empty) {
            ['#bmKErr', '#bmKRate', '#bmKBots'].forEach(function (id) { $(id).text('0'); });
            ['#bmKCat', '#bmKHot', '#bmKBot'].forEach(function (id) { $(id).text('—'); });
            $('.bm-kpi').removeClass('bad warn');
            return;
        }
        var err = data.errorTotal || 0, info = data.infoTotal || 0;
        $('#bmKErr').text(err); $('#bmKErrSub').text(info > 0 ? ('+' + info + ' low-pri') : '\u00a0');
        $('.bm-kpi').eq(0).removeClass('bad warn').addClass(err > 0 ? 'bad' : '');

        var rate = data.errorsPerMin != null ? data.errorsPerMin : 0;
        $('#bmKRate').text(rate); $('#bmKRateSub').text('faults / min');
        $('.bm-kpi').eq(1).removeClass('bad warn').addClass(rate >= 10 ? 'bad' : (rate >= 3 ? 'warn' : ''));

        var affected = (data.byBot || []).filter(function (b) { return b.count > 0; }).length;
        $('#bmKBots').text(affected); $('#bmKBotsSub').text('of ' + (data.botCount || 0) + ' active');

        var topCat = (data.byCategory || []).filter(function (c) { return c.tier !== 'info'; })[0];
        if (topCat) { $('#bmKCat').text(topCat.label).css('color', topCat.color); $('#bmKCatSub').text(topCat.count + '×'); }
        else { $('#bmKCat').text('—').css('color', ''); $('#bmKCatSub').html('&nbsp;'); }

        var hot = (data.hotspots || [])[0];
        if (hot) {
            $('#bmKHot').html('<span class="bm-coord">' + hot.x + ', ' + hot.y + '</span>');
            $('#bmKHotSub').text(mapName(hot.map) + ' · ' + hot.count + ' faults');
            $('#bmKHotTile').data('h', hot).css('cursor', 'pointer');
        } else { $('#bmKHot').text('—'); $('#bmKHotSub').html('&nbsp;'); $('#bmKHotTile').removeData('h'); }

        var worst = (data.byBot || [])[0];
        if (worst && worst.count > 0) {
            $('#bmKBot').text(worst.name); $('#bmKBotSub').text('L' + worst.level + ' · ' + worst.count + ' faults');
            $('#bmKBotTile').data('guid', worst.guid).css('cursor', 'pointer');
        } else { $('#bmKBot').text('—'); $('#bmKBotSub').html('&nbsp;'); $('#bmKBotTile').removeData('guid'); }
    }

    // ══════════════════════════════════════════════════════════
    //  CATEGORY CHIPS + TIER + LAYER TOGGLES
    // ══════════════════════════════════════════════════════════
    function renderCatChips() {
        var cats = (data && data.byCategory) || [];
        if (!cats.length) { $('#bmCats').html('<div class="bm-dim">No faults in window</div>'); return; }
        var html = '';
        for (var i = 0; i < cats.length; i++) {
            var c = cats[i], off = !!hiddenCats[c.key];
            html += '<span class="bm-chip' + (off ? ' off' : '') + '" data-cat="' + esc(c.key) + '" style="color:' + c.color + ';">' +
                '<span class="bm-chip-dot" style="background:' + c.color + ';"></span>' +
                '<span class="bm-chip-lbl">' + esc(c.label) + '</span>' +
                '<span class="bm-chip-n">' + c.count + '</span></span>';
        }
        $('#bmCats').html(html);
    }

    function updateShownCount() {
        var inc = (data && data.recent) || [];
        var shown = layerOn.incidents ? inc.filter(incidentVisible).length : 0;
        var bots = layerOn.bots ? botsLive.filter(function (b) { return b.pos && onCurrentMap(b.mapId) && botSelected(b.guid); }).length : 0;
        $('#bmShown').text(shown + ' faults · ' + bots + ' bots on map');
        $('#bmClear').toggle(hasFilters());
    }
    function hasFilters() {
        return anyBotSelected() || Object.keys(hiddenCats).some(function (k) { return hiddenCats[k]; }) || !tierOn.error || !tierOn.warn || tierOn.info;
    }

    // ══════════════════════════════════════════════════════════
    //  BOT LIST (left rail) — union of live bots + faulting bots
    // ══════════════════════════════════════════════════════════
    function renderBotList() {
        var byGuid = {};
        botsLive.forEach(function (b) {
            byGuid[b.guid] = { guid: b.guid, name: b.name, level: b.level, goal: b.goal, dead: b.dead, stall: !!b.stall, faults: 0, live: true };
        });
        ((data && data.byBot) || []).forEach(function (b) {
            if (!byGuid[b.guid]) byGuid[b.guid] = { guid: b.guid, name: b.name, level: b.level, goal: '', dead: false, stall: false, faults: 0, live: false };
            byGuid[b.guid].faults = b.count;
        });
        var rows = Object.keys(byGuid).map(function (k) { return byGuid[k]; });
        var f = botListFilter;
        if (f) rows = rows.filter(function (r) { return (r.name || '').toLowerCase().indexOf(f) >= 0; });
        rows.sort(function (a, b) { return (b.faults - a.faults) || a.name.localeCompare(b.name); });

        if (!rows.length) { $('#bmBotList').html('<div class="bm-dim" style="padding:8px;">No bots</div>'); return; }
        var html = '';
        rows.forEach(function (r) {
            var col = CLASS_COLORS[classOf[r.guid]] || '#8d96a0';
            var sel = !!selectedBots[r.guid];
            var badge = r.dead ? '<span class="bm-bl-badge dead">dead</span>'
                : r.stall ? '<span class="bm-bl-badge stall">stall</span>'
                    : r.goal ? '<span class="bm-bl-badge">' + esc(r.goal) + '</span>' : '';
            html += '<div class="bm-bl-row' + (sel ? ' sel' : '') + (r.live ? '' : ' off') + '" data-guid="' + r.guid + '">' +
                '<span class="bm-bl-dot" style="background:' + col + ';"></span>' +
                '<span class="bm-bl-name">' + esc(r.name) + '</span>' +
                '<span class="bm-bl-lvl">L' + r.level + '</span>' +
                badge +
                (r.faults ? '<span class="bm-bl-faults">' + r.faults + '</span>' : '') +
                '</div>';
        });
        $('#bmBotList').html(html);
        $('#bmBotSelMeta').text(anyBotSelected() ? (Object.keys(selectedBots).length + ' selected') : 'all bots');
    }

    function focusBot(guid, fly) {
        selectedBots = {}; selectedBots[guid] = true;
        renderBotList(); renderMarkers();
        if (fly) flyToBot(guid);
    }
    function toggleBot(guid) {
        if (selectedBots[guid]) delete selectedBots[guid]; else selectedBots[guid] = true;
        renderBotList(); renderMarkers();
    }
    function flyToBot(guid) {
        var b = botsLive.filter(function (x) { return x.guid === guid && x.pos; })[0];
        if (b && onCurrentMap(b.mapId)) { map.setView(worldToLatLng(b.pos.x, b.pos.y), Math.max(map.getZoom(), 0)); return; }
        // fall back to the bot's most recent positioned incident
        var inc = ((data && data.recent) || []).filter(function (i) { return i.guid === guid && i.hasPos && onCurrentMap(i.map); })
            .sort(function (a, c) { return c.seq - a.seq; })[0];
        if (inc) map.setView(worldToLatLng(inc.x, inc.y), Math.max(map.getZoom(), 0));
    }

    // ══════════════════════════════════════════════════════════
    //  CONTROL BINDINGS
    // ══════════════════════════════════════════════════════════
    function bindControls() {
        $('.bm-map-btn').on('click', function () { if (!$(this).prop('disabled')) switchMap($(this).data('map')); });

        $('#bmPause').on('click', function () {
            paused = !paused;
            $(this).html(paused ? '<i class="fa-solid fa-play"></i> Resume' : '<i class="fa-solid fa-pause"></i> Pause');
            $('#bmLiveDot').toggleClass('paused', paused);
            if (paused) stop(); else start();
        });
        $('#bmRefresh').on('click', tick);
        $('#bmInterval').on('change', function () { intervalMs = parseInt($(this).val(), 10) || 4000; if (!paused) start(); });

        // layer toggles
        $('.bm-tog[data-layer]').on('click', function () {
            var key = $(this).data('layer');
            layerOn[key] = !layerOn[key];
            $(this).toggleClass('off', !layerOn[key]);
            renderMarkers();
        });
        // tier toggles
        $('.bm-tier-tog').on('click', function () {
            var key = $(this).data('tier');
            tierOn[key] = !tierOn[key];
            $(this).toggleClass('off', !tierOn[key]);
            renderMarkers();
        });

        // category chips (delegated — rebuilt each poll)
        $('#bmCats').on('click', '.bm-chip', function () {
            var k = $(this).data('cat');
            hiddenCats[k] = !hiddenCats[k];
            $(this).toggleClass('off', !!hiddenCats[k]);
            renderMarkers();
        });

        // bot list (delegated)
        $('#bmBotList').on('click', '.bm-bl-row', function (e) {
            var guid = parseInt($(this).data('guid'), 10);
            if (e.shiftKey || e.metaKey || e.ctrlKey) toggleBot(guid);
            else { focusBot(guid, true); }
        });
        $('#bmBotSearch').on('input', function () { botListFilter = $(this).val().toLowerCase(); renderBotList(); });
        $('#bmBotAll').on('click', function () { selectedBots = {}; renderBotList(); renderMarkers(); });
        $('#bmClear').on('click', function () {
            selectedBots = {}; hiddenCats = {}; tierOn = { error: true, warn: true, info: false }; botListFilter = '';
            $('#bmBotSearch').val('');
            $('.bm-tier-tog').each(function () { $(this).toggleClass('off', !tierOn[$(this).data('tier')]); });
            renderCatChips(); renderBotList(); renderMarkers();
        });

        // KPI tile click-throughs
        $('#bmKHotTile').on('click', function () {
            var h = $(this).data('h'); if (h && onCurrentMap(h.map)) map.setView(worldToLatLng(h.x, h.y), Math.max(map.getZoom(), 1));
        });
        $('#bmKBotTile').on('click', function () { var g = $(this).data('guid'); if (g != null) focusBot(g, true); });

        // popup action links (delegated on the map container)
        $('#botmap').on('click', '[data-bm-focusbot]', function (e) { e.preventDefault(); focusBot(parseInt($(this).attr('data-bm-focusbot'), 10), true); });
        $('#botmap').on('click', '[data-bm-trailbot]', function (e) {
            e.preventDefault();
            layerOn.trails = true; $('.bm-tog[data-layer="trails"]').removeClass('off');
            focusBot(parseInt($(this).attr('data-bm-trailbot'), 10), true);
        });
    }

    // ══════════════════════════════════════════════════════════
    //  CLUSTER INSPECT → CONTEXT EXPORT
    // ══════════════════════════════════════════════════════════
    var basket = [];          // pinned context entries (snapshots taken at pin time)
    var exportNote = '';
    var LS_KEY = 'bm_export_v1';
    var curCluster = null;    // {map, cx, cy} currently open in the inspect modal

    // incidents that fall in a 100-yd cell (same binning the server hotspots use)
    function clusterIncidents(mapId, cx, cy) {
        return ((data && data.recent) || []).filter(function (i) {
            if (!i.hasPos) return false;
            if (i.map !== mapId) return false;
            return Math.floor(i.x / 100) === cx && Math.floor(i.y / 100) === cy;
        });
    }

    // snapshot one bot's slice of a cluster (incidents + its live state) at pin time
    function botEntry(mapId, cx, cy, guid) {
        var incs = clusterIncidents(mapId, cx, cy).filter(function (i) { return i.guid === guid; })
            .sort(function (a, b) { return a.seq - b.seq; });
        var live = botsLive.filter(function (b) { return b.guid === guid; })[0] || null;
        var nm = (incs[0] && incs[0].name) || (live && live.name) || ('bot ' + guid);
        var lv = (incs[0] && incs[0].level) || (live && live.level) || 0;
        return {
            guid: guid, name: nm, level: lv, cls: classOf[guid],
            live: live ? { goal: live.goal, why: live.why, hp: live.hpPct, mana: live.manaPct, dead: live.dead, inCombat: live.inCombat, stall: live.stall ? live.stall.reason : null, durability: live.durability, copper: live.copper, x: live.pos ? Math.round(live.pos.x) : null, y: live.pos ? Math.round(live.pos.y) : null, mapId: live.mapId } : null,
            incidents: incs.map(function (i) { return { seq: i.seq, t: i.t, x: i.x, y: i.y, map: i.map, category: i.category, label: i.label, why: i.why, preCmd: i.preCmd, target: i.target, msg: i.msg }; })
        };
    }

    function clusterBotGuids(mapId, cx, cy) {
        var seen = {}, out = [];
        clusterIncidents(mapId, cx, cy).forEach(function (i) { if (!seen[i.guid]) { seen[i.guid] = 1; out.push(i.guid); } });
        return out;
    }

    function openClusterModal(mapId, cx, cy) {
        curCluster = { map: mapId, cx: cx, cy: cy };
        var incs = clusterIncidents(mapId, cx, cy);
        var guids = clusterBotGuids(mapId, cx, cy);
        var centerX = cx * 100 + 50, centerY = cy * 100 + 50;

        // headline count from the server hotspot if we have it (it counts the full window,
        // not just the 180-cap recent feed) — so we can say "showing N of M".
        var hs = ((data && data.hotspots) || []).filter(function (h) { return Math.round((h.x - 50) / 100) === cx && Math.round((h.y - 50) / 100) === cy && (h.map === mapId || h.map < 0); })[0];
        var fullCount = hs ? hs.count : incs.length;

        $('#bmCluTitle').text(centerX + ', ' + centerY);
        $('#bmCluMeta').html(mapName(mapId) + ' · <b>' + incs.length + '</b> faults shown' + (fullCount > incs.length ? (' <span class="bm-dim">(of ' + fullCount + ' in window — rest aged out of the live feed)</span>') : '') + ' · ' + guids.length + ' bots');
        $('#bmCluPinAll').data('cluster', { map: mapId, cx: cx, cy: cy });

        var html = '';
        if (!guids.length) {
            html = '<div class="bm-dim" style="padding:14px;">No positioned incidents for this cell are still in the live feed.</div>';
        } else {
            guids.forEach(function (g) {
                var be = botEntry(mapId, cx, cy, g);
                var col = CLASS_COLORS[be.cls] || '#8d96a0';
                var liveBadge = be.live ? ('<span class="bm-clu-live">' + esc(be.live.goal || '') + (be.live.dead ? ' · <span style="color:#f7768e">dead</span>' : be.live.stall ? ' · <span style="color:#e0af68">stall</span>' : '') + '</span>') : '';
                html += '<div class="bm-clu-bot" data-guid="' + g + '">' +
                    '<div class="bm-clu-bot-head" data-guid="' + g + '">' +
                    '<span class="bm-clu-caret"><i class="fa-solid fa-chevron-right"></i></span>' +
                    '<span class="bm-bl-dot" style="background:' + col + ';"></span>' +
                    '<span class="bm-clu-name">' + esc(be.name) + ' <span class="bm-dim">L' + be.level + ' ' + (CLASS_NAMES[be.cls] || '') + '</span></span>' +
                    liveBadge +
                    '<span class="bm-clu-fcount">' + be.incidents.length + '</span>' +
                    '<button class="bm-pin-btn" data-pinbot="' + g + '" title="Pin this bot to the export"><i class="fa-solid fa-plus"></i> bot</button>' +
                    '</div>' +
                    '<div class="bm-clu-incs">' + be.incidents.map(function (i) { return incRow(i); }).join('') + '</div>' +
                    '</div>';
            });
        }
        $('#bmCluBody').html(html);
        $('#bmClusterOverlay').css('display', 'flex');
    }

    function incRow(i) {
        var where = i.x + ', ' + i.y;
        var after = i.why ? ('why=' + i.why) : (i.preCmd || '');
        return '<div class="bm-clu-inc" data-seq="' + i.seq + '">' +
            '<button class="bm-pin-btn sm" data-pininc="' + i.seq + '" title="Pin just this incident"><i class="fa-solid fa-plus"></i></button>' +
            '<span class="bm-clu-inc-t">' + clock(i.t) + '</span>' +
            '<span class="bm-clu-inc-cat" style="color:' + catColor(i.category) + '">' + esc(i.label || i.category) + '</span>' +
            '<span class="bm-clu-inc-where"><a href="#" data-jump="' + i.x + '|' + i.y + '">@' + where + '</a></span>' +
            (after ? '<span class="bm-clu-inc-after">' + esc(after) + '</span>' : '') +
            (i.target ? '<span class="bm-clu-inc-tgt">' + esc(i.target) + '</span>' : '') +
            '<div class="bm-clu-inc-msg">' + esc(i.msg) + '</div>' +
            '</div>';
    }

    function catColor(key) {
        var c = ((data && data.byCategory) || []).filter(function (x) { return x.key === key; })[0];
        return c ? c.color : '#7aa2f7';
    }

    // ── pinning ──
    function pinCluster(mapId, cx, cy) {
        var guids = clusterBotGuids(mapId, cx, cy);
        var entry = {
            pid: 'p' + Date.now() + Math.random().toString(36).slice(2, 6),
            key: 'cluster:' + mapId + ':' + cx + ':' + cy,
            kind: 'cluster', map: mapId, x: cx * 100 + 50, y: cy * 100 + 50,
            bots: guids.map(function (g) { return botEntry(mapId, cx, cy, g); })
        };
        addPin(entry);
    }
    function pinBot(mapId, cx, cy, guid) {
        var be = botEntry(mapId, cx, cy, guid);
        addPin({
            pid: 'p' + Date.now() + Math.random().toString(36).slice(2, 6),
            key: 'bot:' + mapId + ':' + cx + ':' + cy + ':' + guid,
            kind: 'bot', map: mapId, x: cx * 100 + 50, y: cy * 100 + 50, bots: [be]
        });
    }
    function pinIncident(mapId, cx, cy, seq) {
        var i = clusterIncidents(mapId, cx, cy).filter(function (x) { return x.seq === seq; })[0];
        if (!i) return;
        var be = botEntry(mapId, cx, cy, i.guid);
        be.incidents = be.incidents.filter(function (x) { return x.seq === seq; });
        addPin({
            pid: 'p' + Date.now() + Math.random().toString(36).slice(2, 6),
            key: 'inc:' + seq, kind: 'incident', map: mapId, x: i.x, y: i.y, bots: [be]
        });
    }

    function addPin(entry) {
        if (basket.some(function (p) { return p.key === entry.key; })) { flashExport('already pinned'); return; }
        basket.push(entry);
        saveBasket(); updateExportBadge(); flashExport('pinned ✓');
    }
    function removePin(pid) { basket = basket.filter(function (p) { return p.pid !== pid; }); saveBasket(); updateExportBadge(); renderExport(); }
    function clearBasket() { basket = []; saveBasket(); updateExportBadge(); renderExport(); }

    function updateExportBadge() {
        var n = basket.length;
        $('#bmExportN').text(n);
        $('#bmExport').toggleClass('has', n > 0);
    }
    var flashTimer = null;
    function flashExport(txt) {
        var $b = $('#bmExport'); $b.addClass('flash');
        $('#bmExportFlash').text(txt).css('opacity', 1);
        clearTimeout(flashTimer);
        flashTimer = setTimeout(function () { $b.removeClass('flash'); $('#bmExportFlash').css('opacity', 0); }, 1200);
    }

    // ── export modal ──
    function openExportModal() { renderExport(); $('#bmExportOverlay').css('display', 'flex'); }
    function renderExport() {
        $('#bmExportNote').val(exportNote);
        var n = basket.length;
        $('#bmExportCount').text(n + (n === 1 ? ' item' : ' items'));
        var list = basket.map(function (p) {
            var botNames = p.bots.map(function (b) { return b.name; }).join(', ');
            var ninc = p.bots.reduce(function (a, b) { return a + b.incidents.length; }, 0);
            var label = p.kind === 'cluster' ? ('Cluster @ ' + p.x + ',' + p.y)
                : p.kind === 'bot' ? (botNames + ' @ ' + p.x + ',' + p.y)
                    : ('Incident · ' + botNames);
            return '<div class="bm-exp-row"><span class="bm-exp-kind ' + p.kind + '">' + p.kind + '</span>' +
                '<span class="bm-exp-label">' + esc(label) + '</span>' +
                '<span class="bm-exp-meta">' + ninc + ' fault' + (ninc === 1 ? '' : 's') + '</span>' +
                '<button class="bm-exp-x" data-rmpin="' + p.pid + '" title="Remove"><i class="fa-solid fa-xmark"></i></button></div>';
        }).join('');
        $('#bmExportList').html(n ? list : '<div class="bm-dim" style="padding:10px;">Nothing pinned yet. Click a hotspot → Inspect cluster → pin a cluster, bot, or incident.</div>');
        $('#bmExportPreview').val(buildExportMarkdown());
    }

    function buildExportMarkdown() {
        var L = [];
        L.push('# Barrens Chat — fleet debug context');
        var ctx = [];
        if (data && !data.empty) {
            ctx.push('window ' + fmtDur(data.windowSec));
            if (data.errorsPerMin != null) ctx.push(data.errorsPerMin + ' faults/min');
            if (data.botCount != null) ctx.push(data.botCount + ' bots active');
        }
        L.push('_' + new Date().toISOString() + (ctx.length ? ' · ' + ctx.join(' · ') : '') + '_');
        L.push('');
        if (exportNote && exportNote.trim()) { L.push('> ' + exportNote.trim().replace(/\n/g, '\n> ')); L.push(''); }
        if (!basket.length) { L.push('_(nothing pinned)_'); return L.join('\n'); }

        basket.forEach(function (p) {
            var head = p.kind === 'cluster' ? ('Cluster @ (' + p.x + ', ' + p.y + ') · ' + mapName(p.map))
                : p.kind === 'bot' ? ('Bot @ (' + p.x + ', ' + p.y + ') · ' + mapName(p.map))
                    : ('Incident @ (' + p.x + ', ' + p.y + ') · ' + mapName(p.map));
            L.push('## ' + head);
            p.bots.forEach(function (b) {
                var liveBits = b.live ? (' — live: ' + (b.live.goal || '?') + (b.live.why ? '(' + b.live.why + ')' : '') + ', hp ' + b.live.hp + '%' + (b.live.dead ? ', DEAD' : '') + (b.live.stall ? ', stall=' + b.live.stall : '') + (b.live.durability != null ? ', dur ' + b.live.durability + '%' : '')) : '';
                L.push('### ' + b.name + ' — L' + b.level + ' ' + (CLASS_NAMES[b.cls] || '') + liveBits);
                if (b.live && b.live.x != null) L.push('- now at (' + b.live.x + ', ' + b.live.y + ') @ ' + mapName(b.live.mapId));
                b.incidents.forEach(function (i) {
                    var after = i.why ? ('after why=' + i.why) : (i.preCmd ? ('after ' + i.preCmd) : '');
                    var tgt = i.target ? (' · target ' + i.target) : '';
                    L.push('- ' + clock(i.t) + ' @(' + i.x + ',' + i.y + ') **' + (i.label || i.category) + '** ' + after + tgt + ' — ' + i.msg);
                });
                L.push('');
            });
        });
        return L.join('\n');
    }

    function bindClusterExport() {
        // open inspect modal from a hotspot popup
        $('#botmap').on('click', '[data-bm-inspect]', function (e) {
            e.preventDefault();
            var p = String($(this).attr('data-bm-inspect')).split('|');
            openClusterModal(parseInt(p[0], 10), parseInt(p[1], 10), parseInt(p[2], 10));
        });

        // cluster modal: expand bot, pin bot/incident/cluster, jump-to
        $('#bmCluBody').on('click', '.bm-clu-bot-head', function (e) {
            if ($(e.target).closest('.bm-pin-btn').length) return;
            $(this).closest('.bm-clu-bot').toggleClass('open');
        });
        $('#bmCluBody').on('click', '[data-pinbot]', function (e) {
            e.stopPropagation();
            if (!curCluster) return;
            pinBot(curCluster.map, curCluster.cx, curCluster.cy, parseInt($(this).attr('data-pinbot'), 10));
        });
        $('#bmCluBody').on('click', '[data-pininc]', function (e) {
            e.stopPropagation();
            if (!curCluster) return;
            pinIncident(curCluster.map, curCluster.cx, curCluster.cy, parseInt($(this).attr('data-pininc'), 10));
        });
        $('#bmCluBody').on('click', '[data-jump]', function (e) {
            e.preventDefault();
            var p = String($(this).attr('data-jump')).split('|');
            map.setView(worldToLatLng(parseFloat(p[0]), parseFloat(p[1])), Math.max(map.getZoom(), 1));
            $('#bmClusterOverlay').css('display', 'none');
        });
        $('#bmCluPinAll').on('click', function () { var c = $(this).data('cluster'); if (c) pinCluster(c.map, c.cx, c.cy); });
        $('#bmCluClose').on('click', function () { $('#bmClusterOverlay').css('display', 'none'); });
        $('#bmClusterOverlay').on('click', function (e) { if (e.target === this) $(this).css('display', 'none'); });

        // export modal
        $('#bmExport').on('click', openExportModal);
        $('#bmExportClose').on('click', function () { $('#bmExportOverlay').css('display', 'none'); });
        $('#bmExportOverlay').on('click', function (e) { if (e.target === this) $(this).css('display', 'none'); });
        $('#bmExportNote').on('input', function () { exportNote = $(this).val(); saveBasket(); $('#bmExportPreview').val(buildExportMarkdown()); });
        $('#bmExportList').on('click', '[data-rmpin]', function () { removePin($(this).attr('data-rmpin')); });
        $('#bmExportCopy').on('click', function () { copyText($('#bmExportPreview').val()); var $b = $(this); var t = $b.html(); $b.html('<i class="fa-solid fa-check"></i> Copied'); setTimeout(function () { $b.html(t); }, 1200); });
        $('#bmExportDownload').on('click', function () {
            var blob = new Blob([$('#bmExportPreview').val()], { type: 'text/markdown' });
            var a = document.createElement('a'); a.href = URL.createObjectURL(blob);
            a.download = 'barrens-debug-' + stamp() + '.md'; document.body.appendChild(a); a.click(); document.body.removeChild(a);
        });
        $('#bmExportClear').on('click', function () { if (confirm('Clear all pinned items? (your note stays)')) clearBasket(); });
    }

    function copyText(t) {
        if (navigator.clipboard && navigator.clipboard.writeText) { navigator.clipboard.writeText(t).catch(function () { }); return; }
        var ta = document.createElement('textarea'); ta.value = t; document.body.appendChild(ta);
        ta.select(); try { document.execCommand('copy'); } catch (e) { } document.body.removeChild(ta);
    }
    function stamp() { var d = new Date(); return d.getFullYear() + pad(d.getMonth() + 1) + pad(d.getDate()) + '-' + pad(d.getHours()) + pad(d.getMinutes()); }

    function saveBasket() { try { localStorage.setItem(LS_KEY, JSON.stringify({ note: exportNote, pins: basket })); } catch (e) { } }
    function loadBasket() {
        try {
            var s = JSON.parse(localStorage.getItem(LS_KEY));
            if (s) { basket = s.pins || []; exportNote = s.note || ''; }
        } catch (e) { basket = []; exportNote = ''; }
        updateExportBadge();
    }

    // ══════════════════════════════════════════════════════════
    //  HELPERS
    // ══════════════════════════════════════════════════════════
    function mapName(m) { if (m == null || m < 0) return MAP_DEFS[currentMapKey].label + '?'; return MAP_NAMES[m] || ('map ' + m); }
    function clock(utc) { var d = new Date(utc); if (isNaN(d)) return ''; return pad(d.getHours()) + ':' + pad(d.getMinutes()) + ':' + pad(d.getSeconds()); }
    function nowClock() { var d = new Date(); return pad(d.getHours()) + ':' + pad(d.getMinutes()) + ':' + pad(d.getSeconds()); }
    function pad(n) { return n < 10 ? '0' + n : '' + n; }
    function money(c) { c = c || 0; var g = Math.floor(c / 10000), s = Math.floor((c % 10000) / 100), cp = c % 100; return (g ? g + 'g ' : '') + (s ? s + 's ' : '') + cp + 'c'; }
    function fmtDur(sec) {
        sec = Math.round(sec || 0);
        if (sec < 60) return sec + 's';
        var m = Math.floor(sec / 60), s = sec % 60;
        if (m < 60) return m + 'm' + (s ? ' ' + s + 's' : '');
        var h = Math.floor(m / 60); m = m % 60;
        return h + 'h' + (m ? ' ' + m + 'm' : '');
    }
    function esc(s) {
        return String(s == null ? '' : s)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

})();
