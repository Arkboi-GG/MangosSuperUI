/* ============================================================
   circuittrace.js — the Circuit Board viewer (CIRCUIT_BOARD.md).

   v3: vertical stage flow. Lanes are horizontal BANDS stacked in
   pipeline order (the mermaid reading: top = the tick entering,
   bottom = actions leaving), nodes wrap left-to-right inside a
   band, edges flow downward, loop-backs curve around the right
   margin. The wedge banner OVERLAYS the board (zero layout
   shift) and only goes red on genuine alarm probes — a steady
   idle loop shows as a quiet ×N chip in the footer instead.
   "Packet" downloads the whole decoded window as a markdown
   file ready to paste to an LLM.

   Hand-rolled SVG, same approach as sourcemap.js. No libraries.
   ============================================================ */

$(function () {
    'use strict';

    // ---------- state ----------
    var sites = {};            // id -> {id, file, line, desc, lane, label}
    var guid = 0;
    var botName = '';
    var paused = false;
    var lastMode = '?';
    var feedMode = false;
    var laneFilter = null;
    var showAllLanes = false;
    var selectedSite = null;

    var nodePos = {};           // id -> {x, y, w, h, cx, cy}
    var boardW = 0, boardH = 0;
    var builtKey = '';

    var fires = {};             // id -> count in window
    var lastVal = {};
    var lastNote = {};
    var edges = {};             // "a>b" -> count
    var activePath = [];        // latest tick's ordered hit ids
    var cycleRun = 1;
    var lastPos = null;
    var lastSegs = [];

    var view = { x: 0, y: 0, k: 1 };
    var userAdjusted = false;

    // Pipeline order — the mermaid reading, top to bottom.
    var LANE_ORDER = ['bridge', 'chain', 'cpp-chain', 'cpp-bridge', 'cpp-move', 'cpp-task', 'cpp-grind',
        'cpp-combat', 'cpp-quest', 'host', 'event', 'tick', 'quarantine', 'stuck-still', 'island', 'wedge',
        'reconcile', 'goal', 'goal-change', 'grind', 'grind-relocate', 'grind-hub', 'quest', 'train',
        'maint', 'errand', 'escape-bands', 'group', 'groupmgr', 'plan', 'dispatch', 'stall', 'issue',
        'fire', 'negate', 'supervisor', 'spawn', 'chat', 'loadout', 'rotation', 'raidplan',
        'spellbook', 'talentvis', 'ctx', 'identity'];

    // A repeat is only an ALARM if the repeating path contains one of these.
    var ALARM = /wedge: TRIPPED|stall:|negate:|GIVEUP|quarantine: ENGAGED|island: escape|stranded|barren|blocked/i;

    var NODE_W = 190, NODE_H = 30, ROW_GAP = 8, COL_GAP = 14, BAND_GAP = 26, LABEL_W = 120, VIRT_W = 1260;

    // ---------- data plumbing ----------
    function loadSites() {
        return $.getJSON('/CircuitTrace/Sites', function (list) {
            (list || []).forEach(function (s) {
                if (sites[s.id]) return;
                var ix = s.desc.indexOf(':');
                s.lane = ix > 0 ? s.desc.substring(0, ix) : 'other';
                s.label = ix > 0 ? s.desc.substring(ix + 1).trim() : s.desc;
                sites[s.id] = s;
            });
        });
    }

    function loadBots() {
        $.getJSON('/Bots/States', function (d) {
            var bots = (d && d.bots) || [];
            var sel = $('#cbBot');
            var cur = sel.val();
            sel.find('option:not(:first)').remove();
            bots.sort(function (a, b) { return (a.name || '').localeCompare(b.name || ''); })
                .forEach(function (b) { sel.append($('<option>').val(b.guid).text(b.name + ' (' + b.guid + ')')); });
            if (cur && cur !== '0') sel.val(cur);
        });
    }

    function refreshStatus() {
        $.getJSON('/CircuitTrace/Status', function (d) {
            if (!d) return;
            lastMode = d.mode;
            $('#cbShadow').text('Shadow: ' + d.mode).toggleClass('cb-active', d.mode === 'shadow');
            var mine = guid > 0 && (d.armed || []).indexOf(guid) >= 0;
            $('#cbArm').toggleClass('cb-active', mine).text(mine ? 'Armed ✓' : 'Arm');
            $('#cbStatus').text(d.mode + ' · ' + (d.armed || []).length + ' armed · ' +
                d.ringBots + ' ringing · ' + d.sites + ' sites');
        });
    }

    function pathKey(seg) { return (seg.h || []).map(function (h) { return h[0]; }).join(','); }

    function aggregate(segs) {
        fires = {}; edges = {}; activePath = []; cycleRun = 1; lastPos = null;
        var prevHit = null;
        segs.forEach(function (seg) {
            (seg.h || []).forEach(function (h) {
                fires[h[0]] = (fires[h[0]] || 0) + 1;
                if (h.length > 1 && h[1] !== null && h[1] !== undefined) lastVal[h[0]] = h[1];
                if (h.length > 2 && h[2]) lastNote[h[0]] = h[2];
                if (prevHit !== null) {
                    var k = prevHit + '>' + h[0];
                    edges[k] = (edges[k] || 0) + 1;
                }
                prevHit = h[0];
            });
            if (seg.pos) lastPos = seg.pos;
        });
        for (var i = segs.length - 1; i >= 0; i--) {
            if ((segs[i].h || []).length) {
                activePath = segs[i].h.map(function (h) { return h[0]; });
                var key = pathKey(segs[i]);
                for (var j = i - 1; j >= 0; j--) {
                    if (pathKey(segs[j]) === key) cycleRun++;
                    else break;
                }
                break;
            }
        }
    }

    // ---------- board layout: vertical bands ----------
    function visibleIds() {
        return Object.keys(sites).filter(function (id) {
            return showAllLanes || fires[id] || activePath.indexOf(+id) >= 0;
        }).map(Number);
    }

    function orderedLanes(ids) {
        var present = {};
        ids.forEach(function (id) { present[sites[id].lane] = true; });
        var lanes = Object.keys(present);
        lanes.sort(function (a, b) {
            var ia = LANE_ORDER.indexOf(a), ib = LANE_ORDER.indexOf(b);
            if (ia < 0) ia = 999; if (ib < 0) ib = 999;
            return ia - ib || a.localeCompare(b);
        });
        return lanes;
    }

    function buildBoard() {
        var ids = visibleIds();
        var lanes = orderedLanes(ids);
        var key = lanes.join('|') + '#' + ids.slice().sort().join(',') + '#' + (laneFilter || '');
        if (key === builtKey) return;
        builtKey = key;

        nodePos = {};
        var svgNodes = '', svgHeads = '', svgBands = '';
        var y = 10;
        var perRow = Math.max(1, Math.floor((VIRT_W - LABEL_W - 20) / (NODE_W + COL_GAP)));

        lanes.forEach(function (lane) {
            var laneIds = ids.filter(function (id) { return sites[id].lane === lane; });
            laneIds.sort(function (a, b) {
                var A = sites[a], B = sites[b];
                return A.file === B.file ? A.line - B.line : A.file.localeCompare(B.file);
            });
            if (!laneIds.length) return;

            var rows = Math.ceil(laneIds.length / perRow);
            var bandH = rows * (NODE_H + ROW_GAP) - ROW_GAP + 16;
            var dim = laneFilter && laneFilter !== lane;

            svgBands += '<rect class="cb-band' + (dim ? ' cb-dim' : '') + '" x="4" y="' + (y - 8) + '" width="' + (VIRT_W - 8) + '" height="' + (bandH + 8) + '" rx="8"></rect>';
            svgHeads += '<text class="cb-lane-head' + (dim ? ' cb-dim' : '') + '" x="14" y="' + (y + 14) + '" data-lane="' + esc(lane) + '">' + esc(lane) + '</text>';

            laneIds.forEach(function (id, i) {
                var col = i % perRow, row = Math.floor(i / perRow);
                var x = LABEL_W + col * (NODE_W + COL_GAP);
                var ny = y + row * (NODE_H + ROW_GAP);
                nodePos[id] = { x: x, y: ny, w: NODE_W, h: NODE_H, cx: x + NODE_W / 2, cy: ny + NODE_H / 2 };
            });

            y += bandH + BAND_GAP;
        });

        boardW = VIRT_W;
        boardH = y + 10;

        Object.keys(nodePos).forEach(function (id) {
            var p = nodePos[id], s = sites[id];
            var dim = laneFilter && laneFilter !== s.lane;
            svgNodes += '<g class="cb-node' + (dim ? ' cb-dim' : '') + '" data-id="' + id + '">' +
                '<rect x="' + p.x + '" y="' + p.y + '" width="' + p.w + '" height="' + p.h + '" rx="6" id="cbn' + id + '"></rect>' +
                '<text x="' + (p.x + 8) + '" y="' + (p.y + 19) + '" id="cbt' + id + '"></text>' +
                '<title>' + esc(s.desc + '\n' + s.file + ':' + s.line) + '</title></g>';
        });

        $('#cbBands').html(svgBands);
        $('#cbHeads').html(svgHeads);
        $('#cbNodes').html(svgNodes);
        if (!userAdjusted) fitWidth();
    }

    function fitWidth() {
        var w = $('#cbBoard').width() || 900;
        view.k = w / (boardW || VIRT_W);
        view.x = 0; view.y = 0;
        applyView();
    }

    // ---------- dynamic layers ----------
    function paint() {
        var maxFire = 1;
        Object.keys(fires).forEach(function (id) { maxFire = Math.max(maxFire, fires[id]); });
        var onPath = {};
        activePath.forEach(function (id) { onPath[id] = true; });

        Object.keys(nodePos).forEach(function (id) {
            var el = document.getElementById('cbn' + id);
            if (!el) return;
            var f = fires[id] || 0;
            var heat = f ? (0.15 + 0.55 * Math.log(1 + f) / Math.log(1 + maxFire)) : 0;
            el.setAttribute('class', 'cb-rect' + (onPath[id] ? ' cb-on' : '') + (selectedSite === +id ? ' cb-sel' : ''));
            el.style.fillOpacity = heat;
            var t = document.getElementById('cbt' + id);
            if (t) {
                var extra = lastVal[id] !== undefined ? ('  =' + lastVal[id]) : (lastNote[id] ? ('  ·' + trim(lastNote[id], 12)) : '');
                t.textContent = trim(sites[id].label, extra ? 24 : 30) + extra;
            }
        });

        var maxE = 1;
        Object.keys(edges).forEach(function (k) { maxE = Math.max(maxE, edges[k]); });
        var activeEdges = {};
        for (var i = 1; i < activePath.length; i++) activeEdges[activePath[i - 1] + '>' + activePath[i]] = true;

        var svg = '';
        Object.keys(edges).forEach(function (k) {
            var ab = k.split('>');
            var a = nodePos[ab[0]], b = nodePos[ab[1]];
            if (!a || !b) return;
            if (laneFilter && sites[ab[0]].lane !== laneFilter && sites[ab[1]].lane !== laneFilter) return;
            var w = 1 + 2.5 * Math.log(1 + edges[k]) / Math.log(1 + maxE);
            var cls = activeEdges[k] ? 'cb-edge cb-edge-on' : 'cb-edge';
            svg += edgePath(a, b, w, cls, edges[k]);
        });
        $('#cbEdges').html(svg);

        // banner (overlay — never shifts layout) vs quiet footer chip
        var alarm = false;
        activePath.forEach(function (id) { if (sites[id] && ALARM.test(sites[id].desc)) alarm = true; });
        if (alarm && cycleRun >= 3) {
            var names = activePath.filter(function (id) { return sites[id] && ALARM.test(sites[id].desc); })
                .map(function (id) { return sites[id].desc; });
            $('#cbWedge').show().text('⚠ alarm path ×' + cycleRun + ' — ' + trim(names.join(' · '), 140));
        } else $('#cbWedge').hide();
        $('#cbSteady').text(cycleRun > 1 ? '×' + cycleRun : '').attr('title', cycleRun > 1 ? 'this exact tick path has repeated ' + cycleRun + ' times' : '');

        var f2 = activePath.map(function (id) { return sites[id] ? sites[id].label : ('#' + id); }).join('  →  ');
        $('#cbNow').text(f2 || '—');
        $('#cbPos').text(lastPos ? ('map ' + lastPos.map + ' · zone ' + lastPos.zone + ' · (' + lastPos.x.toFixed(0) + ', ' + lastPos.y.toFixed(0) + ')') : '');
    }

    function edgePath(a, b, w, cls, count) {
        var x1, y1, x2, y2;
        if (b.y > a.y + a.h) {              // downward: bottom -> top
            x1 = a.cx; y1 = a.y + a.h; x2 = b.cx; y2 = b.y;
            var my = (y1 + y2) / 2;
            return '<path class="' + cls + '" stroke-width="' + w + '" d="M' + x1 + ' ' + y1 +
                ' C' + x1 + ' ' + my + ' ' + x2 + ' ' + my + ' ' + x2 + ' ' + y2 + '"><title>' + count + '×</title></path>';
        }
        if (Math.abs(b.y - a.y) < 1 && b.x !== a.x) {   // same row: side to side
            x1 = b.x > a.x ? a.x + a.w : a.x; y1 = a.cy;
            x2 = b.x > a.x ? b.x : b.x + b.w; y2 = b.cy;
            return '<path class="' + cls + '" stroke-width="' + w + '" d="M' + x1 + ' ' + y1 + ' L' + x2 + ' ' + y2 + '"><title>' + count + '×</title></path>';
        }
        // upward loop-back: out the right margin and around
        x1 = a.x + a.w; y1 = a.cy; x2 = b.x + b.w; y2 = b.cy;
        var bulge = VIRT_W - Math.max(x1, x2) + 30 + Math.min(60, Math.abs(a.cy - b.cy) / 8);
        return '<path class="' + cls + '" stroke-width="' + w + '" d="M' + x1 + ' ' + y1 +
            ' C' + (x1 + bulge) + ' ' + y1 + ' ' + (x2 + bulge) + ' ' + y2 + ' ' + x2 + ' ' + y2 +
            '"><title>' + count + '×</title></path>';
    }

    // ---------- feed tab ----------
    function renderFeed(segs) {
        var html = '';
        var i = 0;
        while (i < segs.length) {
            var seg = segs[i], key = seg.k + '#' + pathKey(seg), run = 1;
            while (i + run < segs.length && (segs[i + run].k + '#' + pathKey(segs[i + run])) === key) run++;
            var t = (seg.t1 || seg.t0 || '').replace('T', ' ').substring(11, 23);
            html += '<div class="cb-seg' + (run >= 8 ? ' cb-cycle' : '') + '"><div class="cb-seg-head"><span class="cb-kind">' +
                seg.k + (run > 1 ? ' ×' + run : '') + '</span>' + t +
                (seg.pos ? '<span class="cb-pos-inline"> · (' + seg.pos.x.toFixed(0) + ', ' + seg.pos.y.toFixed(0) + ')</span>' : '') + '</div>' +
                (seg.h || []).map(function (h) {
                    var s = sites[h[0]];
                    return '<div class="cb-hit">' + esc(s ? s.desc : ('site #' + h[0])) +
                        (h.length > 1 && h[1] !== null && h[1] !== undefined ? ' <b>' + h[1] + '</b>' : '') +
                        (h.length > 2 && h[2] ? ' · ' + esc(h[2]) : '') + '</div>';
                }).join('') + '</div>';
            i += run;
        }
        var box = $('#cbFeed');
        var stick = box.length && box[0].scrollHeight - box.scrollTop() - box.height() < 60;
        box.html(html || '<div class="cb-empty">No segments yet.</div>');
        if (stick) box.scrollTop(box[0].scrollHeight);
    }

    // ---------- packet export (paste-to-LLM download) ----------
    function buildPacket() {
        var now = new Date().toISOString();
        var L = [];
        L.push('# Circuit packet — ' + (botName || 'bot') + ' (guid ' + guid + ') — ' + now);
        L.push('');
        L.push('This is a decision trace from the bot "circuit board": every line is a probe that fired');
        L.push('inside the bot AI (C# brain). A `tick` block is one ~250ms brain tick, in firing order;');
        L.push('`inter` blocks are hits arriving between ticks (bridge/chat threads). `=N` is the value');
        L.push('the branch looked at; text after `·` is a reason/note string. `×N` means that exact');
        L.push('tick path repeated N times consecutively (collapsed). Site ids map to code in the');
        L.push('reference table at the bottom.');
        L.push('');
        L.push('- mode: ' + lastMode + (CircuitArmedText()));
        if (lastPos) L.push('- position: map ' + lastPos.map + ', zone ' + lastPos.zone + ', (' + lastPos.x.toFixed(1) + ', ' + lastPos.y.toFixed(1) + ', ' + (lastPos.z || 0).toFixed(1) + ')');
        L.push('- window: ' + lastSegs.length + ' segments (' +
            (lastSegs.length ? (lastSegs[0].t0 + ' → ' + lastSegs[lastSegs.length - 1].t1) : '—') + ')');
        L.push('');
        L.push('## Current tick path' + (cycleRun > 1 ? ' (repeating ×' + cycleRun + ')' : ''));
        activePath.forEach(function (id) {
            var s = sites[id];
            L.push('- ' + (s ? s.desc : '#' + id) + valNote(id));
        });
        L.push('');
        L.push('## Trace window (oldest first, identical consecutive paths collapsed)');
        var i = 0;
        while (i < lastSegs.length) {
            var seg = lastSegs[i], key = seg.k + '#' + pathKey(seg), run = 1;
            while (i + run < lastSegs.length && (lastSegs[i + run].k + '#' + pathKey(lastSegs[i + run])) === key) run++;
            var t = (seg.t0 || '').replace('T', ' ').substring(11, 23);
            L.push('### ' + seg.k + (run > 1 ? ' ×' + run : '') + ' @ ' + t +
                (seg.pos ? ' — (' + seg.pos.x.toFixed(0) + ', ' + seg.pos.y.toFixed(0) + ') map ' + seg.pos.map : ''));
            (seg.h || []).forEach(function (h) {
                var s = sites[h[0]];
                var line = '- ' + (s ? s.desc : '#' + h[0]);
                if (h.length > 1 && h[1] !== null && h[1] !== undefined) line += ' = ' + h[1];
                if (h.length > 2 && h[2]) line += ' · ' + h[2];
                L.push(line);
            });
            i += run;
        }
        L.push('');
        L.push('## Site reference (id → code location)');
        var used = {};
        lastSegs.forEach(function (seg) { (seg.h || []).forEach(function (h) { used[h[0]] = true; }); });
        Object.keys(used).map(Number).sort(function (a, b) { return a - b; }).forEach(function (id) {
            var s = sites[id];
            if (s) L.push('- ' + id + ': `' + s.file + ':' + s.line + '` — ' + s.desc);
        });
        L.push('');
        return L.join('\n');
    }

    function CircuitArmedText() {
        return $('#cbArm').hasClass('cb-active') ? ', this bot armed (also flushing to disk)' : ', shadow ring only';
    }

    function valNote(id) {
        var t = '';
        if (lastVal[id] !== undefined) t += ' = ' + lastVal[id];
        if (lastNote[id]) t += ' · ' + lastNote[id];
        return t;
    }

    $('#cbPacket').on('click', function () {
        if (guid <= 0) return;
        var md = buildPacket();
        var blob = new Blob([md], { type: 'text/markdown' });
        var a = document.createElement('a');
        a.href = URL.createObjectURL(blob);
        a.download = 'circuit_' + (botName || 'bot').replace(/\W+/g, '') + '_' + guid + '_' +
            new Date().toISOString().replace(/[:.]/g, '-').substring(0, 19) + '.md';
        document.body.appendChild(a);
        a.click();
        setTimeout(function () { URL.revokeObjectURL(a.href); a.remove(); }, 500);
    });

    // ---------- polling ----------
    function poll() {
        if (paused || guid <= 0) return;
        $.getJSON('/CircuitTrace/Peek?guid=' + guid + '&maxSegments=256', function (d) {
            var segs = (d && d.segments) || [];
            lastSegs = segs;
            var unknown = segs.some(function (s) { return (s.h || []).some(function (h) { return !sites[h[0]]; }); });
            var go = function () {
                aggregate(segs);
                if (feedMode) { renderFeed(segs); return; }
                buildBoard();
                paint();
            };
            if (unknown) loadSites().always(go); else go();
        });
    }

    // ---------- pan / zoom ----------
    function applyView() {
        var svg = document.getElementById('cbSvg');
        if (!svg) return;
        var w = $('#cbBoard').width() || 900, h = $('#cbBoard').height() || 500;
        svg.setAttribute('viewBox', view.x + ' ' + view.y + ' ' + (w / view.k) + ' ' + (h / view.k));
    }

    $('#cbBoard').on('wheel', function (e) {
        e.preventDefault();
        userAdjusted = true;
        var delta = e.originalEvent.deltaY > 0 ? 0.9 : 1.1;
        view.k = Math.max(0.2, Math.min(3, view.k * delta));
        applyView();
    });
    (function () {
        var drag = null;
        $('#cbBoard').on('mousedown', function (e) { drag = { x: e.clientX, y: e.clientY, vx: view.x, vy: view.y, moved: false }; });
        $(document).on('mousemove', function (e) {
            if (!drag) return;
            if (Math.abs(e.clientX - drag.x) + Math.abs(e.clientY - drag.y) > 3) { drag.moved = true; userAdjusted = true; }
            view.x = drag.vx - (e.clientX - drag.x) / view.k;
            view.y = drag.vy - (e.clientY - drag.y) / view.k;
            applyView();
        }).on('mouseup', function () { drag = null; });
    })();

    // ---------- events ----------
    $('#cbBot').on('change', function () {
        guid = parseInt($(this).val(), 10) || 0;
        botName = ($(this).find('option:selected').text() || '').replace(/\s*\(\d+\)\s*$/, '');
        builtKey = ''; selectedSite = null; userAdjusted = false;
        $('#cbDetail').hide();
        poll(); refreshStatus();
    });
    $('#cbArm').on('click', function () { if (guid > 0) $.post('/CircuitTrace/Arm?guid=' + guid).done(refreshStatus); });
    $('#cbDisarm').on('click', function () { if (guid > 0) $.post('/CircuitTrace/Disarm?guid=' + guid).done(refreshStatus); });
    $('#cbDump').on('click', function () { if (guid > 0) $.post('/CircuitTrace/Dump?guid=' + guid); });
    $('#cbShadow').on('click', function () {
        var next = lastMode === 'shadow' ? 'off' : 'shadow';
        $.post('/CircuitTrace/Mode?mode=' + next).done(refreshStatus);
    });
    $('#cbPause').on('click', function () { paused = !paused; $(this).text(paused ? 'Resume' : 'Pause').toggleClass('cb-active', paused); });
    $('#cbAllLanes').on('click', function () { showAllLanes = !showAllLanes; $(this).toggleClass('cb-active', showAllLanes); builtKey = ''; poll(); });
    $('#cbFit').on('click', function () { userAdjusted = false; fitWidth(); });
    $('#cbTabBoard, #cbTabFeed').on('click', function () {
        feedMode = this.id === 'cbTabFeed';
        $('#cbTabBoard').toggleClass('cb-active', !feedMode);
        $('#cbTabFeed').toggleClass('cb-active', feedMode);
        $('#cbBoardWrap').toggle(!feedMode);
        $('#cbFeed').toggle(feedMode);
        poll();
    });
    $(document).on('click', '.cb-node', function (e) { e.stopPropagation(); showDetails(+$(this).data('id')); });
    $(document).on('click', '.cb-lane-head', function (e) {
        e.stopPropagation();
        var lane = $(this).data('lane');
        laneFilter = laneFilter === lane ? null : lane;
        builtKey = ''; buildBoard(); paint();
    });
    $('#cbBoard').on('click', function () { selectedSite = null; $('#cbDetail').hide(); paint(); });

    function showDetails(id) {
        selectedSite = id;
        var s = sites[id];
        if (!s) { $('#cbDetail').hide(); return; }
        $('#cbDetail').show().html(
            '<div class="cbd-lane">' + esc(s.lane) + '</div>' +
            '<div class="cbd-desc">' + esc(s.desc) + '</div>' +
            '<div class="cbd-row">' + esc(s.file.split('/').pop()) + ':' + s.line + '</div>' +
            '<div class="cbd-row">fired <b>' + (fires[id] || 0) + '×</b> in window</div>' +
            (lastVal[id] !== undefined ? '<div class="cbd-row">last value <b>' + lastVal[id] + '</b></div>' : '') +
            (lastNote[id] ? '<div class="cbd-row">last note: ' + esc(lastNote[id]) + '</div>' : '') +
            '<div class="cbd-close">click board to close</div>');
        paint();
    }

    // ---------- helpers ----------
    function esc(t) { return $('<i>').text(t == null ? '' : t).html(); }
    function trim(t, n) { return t.length > n ? t.substring(0, n - 1) + '…' : t; }

    // ---------- boot ----------
    loadSites();
    loadBots();
    refreshStatus();
    applyView();
    setInterval(poll, 1500);
    setInterval(refreshStatus, 5000);
    setInterval(loadBots, 30000);
    $(window).on('resize', function () { if (!userAdjusted) fitWidth(); else applyView(); });
});
