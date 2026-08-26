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
    var chainEdges = {};        // "csSite>cppSite" -> {n, last}: cause crossing the wire
    var chainOpen = 0;          // commands sent whose C++ adoption never showed up
    var cppSegs = 0;            // C++ batches in this window
    var lastCppAt = null;

    var view = { x: 0, y: 0, k: 1 };
    var userAdjusted = false;

    // The board reads top-to-bottom as ONE causal loop across BOTH programs:
    // C# senses → decides → dispatches → THE WIRE → C++ executes in the game
    // world → answers come back up. Sections are drawn as titled groups with the
    // wire as an explicit boundary, so "which side is this happening on?" is
    // never a guess — the founding question of the whole instrument.
    var SECTIONS = [
        {
            key: 'in', title: 'C#  ·  inbound from the wire', side: 'cs',
            lanes: ['bridge', 'event', 'host', 'identity', 'ctx']
        },
        {
            key: 'spine', title: 'C#  ·  tick spine + safety nets', side: 'cs',
            lanes: ['tick', 'quarantine', 'stuck-still', 'island', 'wedge', 'stall', 'reconcile', 'supervisor']
        },
        {
            key: 'decide', title: 'C#  ·  goal arbitration + planners', side: 'cs',
            lanes: ['goal', 'goal-change', 'grind', 'grind-relocate', 'grind-hub', 'quest', 'train',
                'maint', 'errand', 'escape-bands', 'group', 'groupmgr', 'plan', 'spawn', 'chat',
                'loadout', 'rotation', 'raidplan', 'spellbook', 'talentvis']
        },
        {
            key: 'out', title: 'C#  ·  dispatch', side: 'cs',
            lanes: ['dispatch', 'issue', 'fire', 'negate', 'chain']
        },
        { key: 'wire', title: '⇅   THE WIRE   ·   chain ids cross here', side: 'wire', lanes: [] },
        {
            key: 'cpp', title: 'C++  ·  actuator in the game world', side: 'cpp',
            lanes: ['cpp-chain', 'cpp-bridge', 'cpp-main', 'cpp-task', 'cpp-move', 'cpp-path',
                'cpp-grind', 'cpp-combat', 'cpp-spec', 'cpp-combatcfg', 'cpp-doctrine', 'cpp-quest',
                'cpp-loot', 'cpp-vendor', 'cpp-train', 'cpp-talent', 'cpp-raid', 'cpp-group',
                'cpp-rez', 'cpp-flight', 'cpp-go']
        }
    ];

    // lane -> {sectionIx, laneIx}. A lane nobody listed still lands on the right
    // SIDE (cpp-* goes to the C++ section) and sorts after the named ones, so a
    // new probe vocabulary shows up in the right half of the board on day one.
    var LANE_HOME = {};
    SECTIONS.forEach(function (sec, si) {
        sec.lanes.forEach(function (lane, li) { LANE_HOME[lane] = { s: si, l: li }; });
    });
    var CPP_SECTION_IX = 5, MISC_SECTION_IX = 2;

    function laneHome(lane) {
        if (LANE_HOME[lane]) return LANE_HOME[lane];
        var isCpp = lane.indexOf('cpp-') === 0;
        return { s: isCpp ? CPP_SECTION_IX : MISC_SECTION_IX, l: 900 };
    }

    function sideOfLane(lane) { return lane.indexOf('cpp-') === 0 ? 'cpp' : 'cs'; }

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
                // Which program the probe lives in. The host stamps remote sites
                // with a "cpp/" file prefix on registration, so this is the
                // authoritative answer, not a guess from the description.
                s.side = (s.file || '').indexOf('cpp/') === 0 ? 'cpp' : 'cs';
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
            var cs = 0, cpp = 0;
            Object.keys(sites).forEach(function (id) { if (sites[id].side === 'cpp') cpp++; else cs++; });
            $('#cbStatus').text(d.mode + ' · ' + (d.armed || []).length + ' armed · ' +
                d.ringBots + ' ringing · ' + cs + ' C# + ' + cpp + ' C++ sites' +
                (cpp === 0 ? ' (core has not reported any probes yet)' : ''));
        });
    }

    function pathKey(seg) { return (seg.h || []).map(function (h) { return h[0]; }).join(','); }

    function aggregate(segs) {
        fires = {}; edges = {}; activePath = []; cycleRun = 1; lastPos = null;
        chainEdges = {}; chainOpen = 0; cppSegs = 0; lastCppAt = null;
        var pendingChain = {};     // chain id -> the C# site that sent it
        var prevHit = null, prevCtx = 0;
        segs.forEach(function (seg) {
            // A segment boundary is not a control-flow edge either: the next tick
            // (or an arriving C++ batch) is a fresh entry, not a continuation.
            prevHit = null;
            var isCpp = (seg.k || '').indexOf('cpp') === 0;
            if (isCpp) { cppSegs++; lastCppAt = seg.t1 || seg.t0 || lastCppAt; }
            (seg.h || []).forEach(function (h) {
                var id = h[0], val = (h.length > 1 && h[1] !== null && h[1] !== undefined) ? h[1] : null;
                // 4th element = a FOREIGN context id: this hit came from another
                // thread (bridge socket, chat loop) and merely landed in the same
                // open segment. Undefined = the segment's own context. Two hits are
                // only control-flow adjacent when their contexts match — drawing an
                // edge across a context change invents a transition that never
                // happened, which is what the first Layer 3 scan caught.
                var ctx = (h.length > 3 && h[3] !== null && h[3] !== undefined) ? h[3] : 0;
                fires[id] = (fires[id] || 0) + 1;
                if (val !== null) lastVal[id] = val;
                if (h.length > 2 && h[2]) lastNote[id] = h[2];
                if (prevHit !== null && prevCtx === ctx) {
                    var k = prevHit + '>' + id;
                    edges[k] = (edges[k] || 0) + 1;
                }
                prevHit = id; prevCtx = ctx;

                // R2 chains: C# stamps a chain id on the command it sends, C++
                // echoes the same id when it adopts that command. Matching the
                // two VALUES is what proves cause crossed the wire — draw it.
                var s = sites[id];
                if (!s || val === null) return;
                if (s.desc.indexOf('chain: command sent') === 0) pendingChain[val] = id;
                else if (s.desc.indexOf('cpp-chain: command adopted') === 0 && pendingChain[val] !== undefined) {
                    var ck = pendingChain[val] + '>' + id;
                    var rec = chainEdges[ck] || (chainEdges[ck] = { n: 0, last: null });
                    rec.n++; rec.last = val;
                    delete pendingChain[val];
                }
            });
            if (seg.pos) lastPos = seg.pos;
        });
        chainOpen = Object.keys(pendingChain).length;
        for (var i = segs.length - 1; i >= 0; i--) {
            if ((segs[i].h || []).length) {
                // Only this segment's OWN context: a hit another thread dropped in
                // is not a step in this path, and showing it as one is a lie.
                activePath = segs[i].h.filter(function (h) {
                    return !(h.length > 3 && h[3] !== null && h[3] !== undefined);
                }).map(function (h) { return h[0]; });
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
            var A = laneHome(a), B = laneHome(b);
            return A.s - B.s || A.l - B.l || a.localeCompare(b);
        });
        return lanes;
    }

    function buildBoard() {
        var ids = visibleIds();
        var lanes = orderedLanes(ids);
        var key = lanes.join('|') + '#' + ids.slice().sort().join(',') + '#' + (laneFilter || '') +
            '#' + (lanes.some(function (l) { return sideOfLane(l) === 'cpp'; }) ? 'x' : '-');
        if (key === builtKey) return;
        builtKey = key;

        nodePos = {};
        var svgNodes = '', svgHeads = '', svgBands = '';
        var y = 10;
        var perRow = Math.max(1, Math.floor((VIRT_W - LABEL_W - 20) / (NODE_W + COL_GAP)));

        // Group the present lanes by section so the two programs are drawn as two
        // labelled halves with the wire between them.
        var bySection = {};
        lanes.forEach(function (lane) {
            var h = laneHome(lane);
            (bySection[h.s] = bySection[h.s] || []).push(lane);
        });
        var cppPresent = lanes.some(function (l) { return sideOfLane(l) === 'cpp'; });
        var csPresent = lanes.some(function (l) { return sideOfLane(l) === 'cs'; });

        SECTIONS.forEach(function (sec, si) {
            var secLanes = bySection[si] || [];

            // The wire is a boundary marker, not a lane: draw it whenever the C#
            // half is on screen, and say plainly when the far side is silent.
            if (sec.side === 'wire') {
                if (!csPresent) return;
                var msg = cppPresent
                    ? sec.title
                    : '⇅   THE WIRE   ·   no C++ traffic in this window (bot not armed on the core, or mangosd not rebuilt)';
                svgBands += '<rect class="cb-wire' + (cppPresent ? '' : ' cb-wire-quiet') + '" x="4" y="' + (y - 4) +
                    '" width="' + (VIRT_W - 8) + '" height="26" rx="6"></rect>';
                svgHeads += '<text class="cb-wire-head' + (cppPresent ? '' : ' cb-wire-quiet-head') + '" x="' + (VIRT_W / 2) +
                    '" y="' + (y + 14) + '">' + esc(msg) + '</text>';
                y += 26 + BAND_GAP;
                return;
            }
            if (!secLanes.length) return;

            svgHeads += '<text class="cb-sec-head cb-side-' + sec.side + '" x="14" y="' + (y + 4) + '">' +
                esc(sec.title) + '</text>';
            y += 14;

            secLanes.forEach(function (lane) {
                var laneIds = ids.filter(function (id) { return sites[id].lane === lane; });
                laneIds.sort(function (a, b) {
                    var A = sites[a], B = sites[b];
                    return A.file === B.file ? A.line - B.line : A.file.localeCompare(B.file);
                });
                if (!laneIds.length) return;

                var rows = Math.ceil(laneIds.length / perRow);
                var bandH = rows * (NODE_H + ROW_GAP) - ROW_GAP + 16;
                var dim = laneFilter && laneFilter !== lane;
                var side = sideOfLane(lane);

                svgBands += '<rect class="cb-band cb-side-' + side + (dim ? ' cb-dim' : '') + '" x="4" y="' + (y - 8) +
                    '" width="' + (VIRT_W - 8) + '" height="' + (bandH + 8) + '" rx="8"></rect>';
                svgHeads += '<text class="cb-lane-head cb-side-' + side + (dim ? ' cb-dim' : '') + '" x="14" y="' +
                    (y + 14) + '" data-lane="' + esc(lane) + '">' + esc(lane) + '</text>';

                laneIds.forEach(function (id, i) {
                    var col = i % perRow, row = Math.floor(i / perRow);
                    var x = LABEL_W + col * (NODE_W + COL_GAP);
                    var ny = y + row * (NODE_H + ROW_GAP);
                    nodePos[id] = { x: x, y: ny, w: NODE_W, h: NODE_H, cx: x + NODE_W / 2, cy: ny + NODE_H / 2 };
                });

                y += bandH + BAND_GAP;
            });
        });

        boardW = VIRT_W;
        boardH = y + 10;

        Object.keys(nodePos).forEach(function (id) {
            var p = nodePos[id], s = sites[id];
            var dim = laneFilter && laneFilter !== s.lane;
            svgNodes += '<g class="cb-node cb-side-' + (s.side || 'cs') + (dim ? ' cb-dim' : '') + '" data-id="' + id + '">' +
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

        // Chain links: the same command, seen from both sides. Drawn on top,
        // dashed and accented, because this is the one edge in the picture that
        // proves the two programs are talking about the same thing.
        Object.keys(chainEdges).forEach(function (k) {
            var ab = k.split('>');
            var a = nodePos[ab[0]], b = nodePos[ab[1]];
            if (!a || !b) return;
            var rec = chainEdges[k];
            svg += edgePath(a, b, 2, 'cb-edge cb-chain', rec.n) +
                '<text class="cb-chain-tag" x="' + ((a.cx + b.cx) / 2) + '" y="' + ((a.cy + b.cy) / 2) +
                '">chain ' + rec.last + ' ·' + rec.n + '</text>';
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

        // Two-sided health, stated plainly: is the far half of the instrument
        // actually reporting, and are its answers tied to our commands?
        var chainsMatched = 0;
        Object.keys(chainEdges).forEach(function (k) { chainsMatched += chainEdges[k].n; });
        var sideEl = $('#cbSides');
        if (cppSegs > 0) {
            sideEl.attr('class', 'cb-two-sided').text('C#+C++ · ' + cppSegs + ' cpp batches' +
                (chainsMatched ? ' · ' + chainsMatched + ' chains stitched' : '') +
                (chainOpen ? ' · ' + chainOpen + ' unanswered' : ''));
        } else {
            sideEl.attr('class', 'cb-one-sided').text('C# only — no C++ in this window');
        }

        var f2 = activePath.map(function (id) {
            var s = sites[id];
            if (!s) return '#' + id;
            return (s.side === 'cpp' ? '⟨c++⟩ ' : '') + s.label;
        }).join('  →  ');
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
            var segSide = (seg.k || '').indexOf('cpp') === 0 ? 'cpp' : 'cs';
            html += '<div class="cb-seg cb-side-' + segSide + (run >= 8 ? ' cb-cycle' : '') + '"><div class="cb-seg-head"><span class="cb-kind">' +
                (segSide === 'cpp' ? 'C++ ' : '') + seg.k + (run > 1 ? ' ×' + run : '') + '</span>' + t +
                (seg.pos ? '<span class="cb-pos-inline"> · (' + seg.pos.x.toFixed(0) + ', ' + seg.pos.y.toFixed(0) + ')</span>' : '') + '</div>' +
                (seg.h || []).map(function (h) {
                    var s = sites[h[0]];
                    var foreign = (h.length > 3 && h[3] !== null && h[3] !== undefined);
                    return '<div class="cb-hit' + (foreign ? ' cb-foreign' : '') + '"' +
                        (foreign ? ' title="another thread wrote this into the segment — not part of this path"' : '') + '>' +
                        (foreign ? '⇢ ' : '') + esc(s ? s.desc : ('site #' + h[0])) +
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
        L.push('This is a decision trace from the bot "circuit board". Every line is a probe that fired');
        L.push('at a real branch, and the trace spans TWO programs merged into one timeline:');
        L.push('');
        L.push('- `tick` / `inter` blocks are the **C# brain** (this repo, BotLogic/**) — one ~250ms');
        L.push('  brain tick in firing order, or hits arriving between ticks (bridge/chat threads).');
        L.push('- `cpp` blocks are the **C++ actuator** (mangosd, SuiBots/**) executing in the game');
        L.push('  world and reporting back over the bridge. Their descriptions start with `cpp-`.');
        L.push('');
        L.push('`=N` is the value the branch looked at; text after `·` is a reason/note string. `×N`');
        L.push('means that exact path repeated N times consecutively (collapsed). Site ids map to code');
        L.push('in the reference table at the bottom — `cpp/` paths are core files, not this repo.');
        L.push('');
        L.push('**Chains** are how cause is proven across the boundary: `chain: command sent = 4172`');
        L.push('on the C# side and `cpp-chain: command adopted = 4172` on the C++ side are the SAME');
        L.push('command. If a C++ chapter ends in an outcome C# never hears about, that is a missing');
        L.push('wire message, not a C# bug — that is the specific trap this instrument exists to catch.');
        L.push('');
        L.push('- mode: ' + lastMode + (CircuitArmedText()));
        var pktCpp = 0, pktChains = 0;
        lastSegs.forEach(function (s) { if ((s.k || '').indexOf('cpp') === 0) pktCpp++; });
        Object.keys(chainEdges).forEach(function (k) { pktChains += chainEdges[k].n; });
        L.push('- sides in this window: ' + (lastSegs.length - pktCpp) + ' C# segments, ' + pktCpp +
            ' C++ segments' + (pktCpp === 0 ? '  ← NOTE: no C++ data here, so a C++-side cause would be invisible' : '') +
            (pktChains ? ', ' + pktChains + ' chains stitched across the wire' : '') +
            (chainOpen ? ', ' + chainOpen + ' commands with no C++ adoption seen' : ''));
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
