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
    var histMode = false;       // Activity tab: macro episodes with decision/source drill-down
    var histEpisodes = [];      // frozen or live snapshot from /CircuitTrace/Timeline
    var histChanges = [];       // flattened decisions (old name retained for small helper reuse)
    var histEpisodeIx = -1;
    var histIx = -1;            // selected decision in the flattened list
    var histPathIx = -1;        // selected literal probe inside that decision
    var histNavIndexes = [];    // meaningful transitions/events; confirmations stay drill-down only
    var histFrozenNewestId = 0;
    var histFrozenMomentIds = {};
    var histFrozenEpisodeState = {};
    var histNewCount = 0;
    var histConfirmationOpen = {};
    var histWindowTruncated = false;
    var histRequestSerial = 0;
    var histLoading = false;
    var sourceRequestSerial = 0;
    var sourceCache = {};
    var sourceVersion = String($('#cbSource').attr('data-source-version') || '');
    var botList = [];           // last /Bots/States list — names for the "Traced…" modal
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
            botList = bots;
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
        if (paused || histMode || guid <= 0) return;   // Changes tab is frozen — never poll under it
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
        histRequestSerial++;
        sourceRequestSerial++;
        histEpisodes = []; histChanges = [];
        histNavIndexes = [];
        histEpisodeIx = histIx = histPathIx = -1;
        histFrozenNewestId = histNewCount = 0;
        histFrozenMomentIds = {};
        histFrozenEpisodeState = {};
        histConfirmationOpen = {};
        histWindowTruncated = false;
        $('#cbDetail').hide();
        if (histMode) captureHistory({ force: true, follow: !paused }); else poll();
        refreshStatus();
    });
    $('#cbArm').on('click', function () { if (guid > 0) $.post('/CircuitTrace/Arm?guid=' + guid).done(refreshStatus); });
    $('#cbDisarm').on('click', function () { if (guid > 0) $.post('/CircuitTrace/Disarm?guid=' + guid).done(refreshStatus); });
    $('#cbDump').on('click', function () { if (guid > 0) $.post('/CircuitTrace/Dump?guid=' + guid); });
    $('#cbShadow').on('click', function () {
        var next = lastMode === 'shadow' ? 'off' : 'shadow';
        $.post('/CircuitTrace/Mode?mode=' + next).done(refreshStatus);
    });
    function setPaused(next) {
        paused = !!next;
        if (paused && histMode && histChanges.length) {
            histFrozenNewestId = histChanges[histChanges.length - 1].id || histFrozenNewestId;
            freezeHistorySnapshot();
        }
        $('#cbPause').text(paused ? 'Resume' : 'Pause').toggleClass('cb-active', paused);
        renderHistoryLiveState();
        if (!paused) {
            histNewCount = 0;
            if (histMode) captureHistory({ force: true, follow: true });
            else poll();
        }
    }
    $('#cbPause').on('click', function () { setPaused(!paused); });
    $('#cbAllLanes').on('click', function () { showAllLanes = !showAllLanes; $(this).toggleClass('cb-active', showAllLanes); builtKey = ''; poll(); });
    $('#cbFit').on('click', function () { userAdjusted = false; fitWidth(); });
    function setView(mode) {
        feedMode = mode === 'feed';
        histMode = mode === 'history';
        $('#cbTabBoard').toggleClass('cb-active', mode === 'board');
        $('#cbTabFeed').toggleClass('cb-active', mode === 'feed');
        $('#cbTabHistory').toggleClass('cb-active', mode === 'history');
        $('#cbBoardWrap').toggle(mode === 'board');
        $('#cbFeed').toggle(mode === 'feed');
        $('#cbHistory').css('display', mode === 'history' ? 'grid' : 'none');
        $('.cb-footer').toggle(mode !== 'history');
        $('#cbAllLanes,#cbFit').toggle(mode === 'board');
        if (mode !== 'board') $('#cbWedge').hide();
        if (mode === 'history') captureHistory({ force: histEpisodes.length === 0, follow: !paused });
        else poll();
    }
    $('#cbTabBoard').on('click', function () { setView('board'); });
    $('#cbTabFeed').on('click', function () { setView('feed'); });
    $('#cbTabHistory').on('click', function () { setView('history'); });

    // ---------- Activity reader: incident/routine → meaningful change → source ----------
    // The server retains exact decision runs for twenty minutes, groups routine work
    // by durable objective, and holds sustained alarms open as conditions. Confirming
    // frames remain available here without masquerading as new incidents.
    function resetHistory(message) {
        histEpisodes = [];
        histChanges = [];
        histNavIndexes = [];
        histEpisodeIx = histIx = histPathIx = -1;
        histFrozenNewestId = histNewCount = 0;
        histFrozenMomentIds = {};
        histFrozenEpisodeState = {};
        histConfirmationOpen = {};
        $('#cbHistList').html('<div class="cb-reader-empty"><strong>' + esc(message) + '</strong>' +
            (guid <= 0 ? 'Find → fight → loot → heal loops will appear as one routine episode.' : '') + '</div>');
        $('#cbHistDetail').html('<div class="cb-reader-empty"><strong>No decision selected.</strong></div>');
        $('#cbDecisionList,#cbPathList').empty();
        renderSourceEmpty('Choose a decision step to read its code.');
        updateHistNav();
        renderHistoryLiveState();
    }

    function flattenEpisodes(episodes) {
        var out = [];
        (episodes || []).forEach(function (episode, episodeIx) {
            (episode.decisions || []).forEach(function (decision, localIx) {
                decision._episodeIx = episodeIx;
                decision._localIx = localIx;
                out.push(decision);
            });
        });
        return out;
    }

    function episodeKind(episode) {
        var kind = String((episode && episode.kind) || '').toLowerCase();
        if (kind === 'condition' || kind === 'eventburst' || kind === 'routine') return kind;
        return episode && episode.severity === 'alarm' ? 'eventburst' : 'routine';
    }

    function decisionPresentation(decision, episode, localIx) {
        var value = String((decision && decision.presentation) || '').toLowerCase();
        if (value === 'transition' || value === 'confirmation' || value === 'event' || value === 'decision')
            return value;

        var kind = episodeKind(episode);
        if (kind === 'eventburst') return 'event';
        if (kind !== 'condition') return 'decision';
        if (localIx === 0 || (decision && decision.transition)) return 'transition';
        if (episode && episode.status === 'resolved' && localIx === (episode.decisions || []).length - 1)
            return 'transition';
        return 'confirmation';
    }

    function isNavigableDecision(decision) {
        var episode = histEpisodes[decision && decision._episodeIx];
        if (!decision || !episode) return false;
        if (episode.startedBeforeWindow && decision._localIx === 0) return true;
        return decisionPresentation(decision, episode, decision._localIx) !== 'confirmation';
    }

    function rebuildHistNav() {
        histNavIndexes = [];
        var representedEpisodes = {};
        histChanges.forEach(function (decision, flatIx) {
            if (!isNavigableDecision(decision)) return;
            histNavIndexes.push(flatIx);
            representedEpisodes[decision._episodeIx] = true;
        });

        // A retained window can begin in the middle of an already-active condition.
        // Keep one representative selectable even when every retained frame is a confirmation.
        histEpisodes.forEach(function (episode, episodeIx) {
            if (representedEpisodes[episodeIx] || !(episode.decisions || []).length) return;
            var firstIx = histChanges.findIndex(function (decision) {
                return decision._episodeIx === episodeIx;
            });
            if (firstIx >= 0) histNavIndexes.push(firstIx);
        });
        histNavIndexes.sort(function (a, b) { return a - b; });
    }

    function momentKey(decision) {
        return 'decision:' + String(decision && decision.id);
    }

    function episodeKey(episode) {
        if (episodeKind(episode) === 'condition')
            return 'episode:condition:' + String(episode && episode.id);
        return 'episode:' + String(episode && episode.id) + ':' + String((episode && episode.start) || '');
    }

    function freezeHistorySnapshot() {
        histFrozenMomentIds = {};
        histNavIndexes.forEach(function (flatIx) {
            histFrozenMomentIds[momentKey(histChanges[flatIx])] = true;
        });
        histFrozenEpisodeState = {};
        histEpisodes.forEach(function (episode) {
            histFrozenEpisodeState[episodeKey(episode)] = {
                kind: episodeKind(episode),
                status: String(episode.status || ''),
                transitionCount: +episode.transitionCount || 0,
                occurrenceCount: +episode.occurrenceCount || 0
            };
        });
    }

    function countHumanHistoryUpdates(incomingEpisodes, incomingDecisions) {
        var unseenByEpisode = {};
        var count = 0;
        incomingDecisions.forEach(function (decision) {
            var episode = incomingEpisodes[decision._episodeIx];
            var presentation = decisionPresentation(decision, episode, decision._localIx);
            var navigable = presentation !== 'confirmation' || (episode && episode.startedBeforeWindow && decision._localIx === 0);
            if (!navigable || histFrozenMomentIds[momentKey(decision)]) return;
            count++;
            unseenByEpisode[episodeKey(episode)] = true;
        });

        incomingEpisodes.forEach(function (episode) {
            var key = episodeKey(episode);
            var before = histFrozenEpisodeState[key];
            if (!before) {
                if (!unseenByEpisode[key]) count++;
                return;
            }

            var meaningfulStateChanged = before.status !== String(episode.status || '')
                || before.transitionCount !== (+episode.transitionCount || 0)
                || (episodeKind(episode) === 'eventburst'
                    && before.occurrenceCount !== (+episode.occurrenceCount || 0));
            if (meaningfulStateChanged && !unseenByEpisode[key]) count++;
        });
        return count;
    }

    function captureHistory(options) {
        options = options || {};
        if (guid <= 0) {
            resetHistory('Pick a bot to read its activity.');
            return;
        }
        if (histLoading && options.quiet) return;

        var requestedGuid = guid;
        var serial = ++histRequestSerial;
        var priorDecisionId = histChanges[histIx] && histChanges[histIx].id;
        var priorPathIx = histPathIx;
        histLoading = true;
        $.getJSON('/CircuitTrace/Timeline/' + requestedGuid + '?maxRuns=2048')
            .done(function (d) {
                if (serial !== histRequestSerial || requestedGuid !== guid || !d) return;
                lastMode = d.mode || lastMode;
                var incomingEpisodes = d.episodes || [];
                var incomingDecisions = flattenEpisodes(incomingEpisodes);
                var incomingNewest = d.newestDecisionId ||
                    (incomingDecisions.length ? incomingDecisions[incomingDecisions.length - 1].id : 0);

                // A paused reader is a real snapshot. Poll only to light the "new"
                // badge; never move or replace the material somebody is teaching from.
                if (paused && !options.force && histChanges.length) {
                    histNewCount = countHumanHistoryUpdates(incomingEpisodes, incomingDecisions);
                    renderHistoryLiveState();
                    return;
                }

                histEpisodes = incomingEpisodes;
                histChanges = incomingDecisions;
                rebuildHistNav();
                histWindowTruncated = !!d.windowTruncated;
                histNewCount = 0;
                if (!histChanges.length) {
                    resetHistory(lastMode === 'shadow'
                        ? 'No activity has been recorded for this bot yet.'
                        : 'Shadow mode is off — turn it on to record activity.');
                    return;
                }

                var preserved = -1;
                if (options.preserve && priorDecisionId != null) {
                    preserved = histChanges.findIndex(function (decision) { return decision.id === priorDecisionId; });
                }
                histIx = preserved >= 0 ? preserved
                    : (histNavIndexes.length ? histNavIndexes[histNavIndexes.length - 1] : histChanges.length - 1);
                histEpisodeIx = histChanges[histIx]._episodeIx;
                histPathIx = preserved >= 0 ? priorPathIx : -1;
                histFrozenNewestId = incomingNewest;
                freezeHistorySnapshot();
                renderHistList();
                renderHistDetail();
                renderHistoryLiveState();
            })
            .fail(function () {
                if (serial !== histRequestSerial || options.quiet || histChanges.length) return;
                resetHistory('The activity timeline could not be loaded.');
            })
            .always(function () {
                if (serial === histRequestSerial) histLoading = false;
            });
    }

    function timeOf(value) {
        if (!value) return '—';
        var date = new Date(value);
        if (isNaN(date.getTime())) return ('' + value).replace('T', ' ').substring(11, 19);
        return date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false });
    }

    function durationText(ms, start, end) {
        if (ms == null && start && end) ms = new Date(end) - new Date(start);
        ms = Math.max(0, +ms || 0);
        if (ms < 1000) return Math.round(ms) + ' ms';
        var seconds = Math.round(ms / 1000);
        if (seconds < 60) return seconds + ' s';
        var minutes = Math.floor(seconds / 60);
        var rest = seconds % 60;
        return minutes + 'm' + (rest ? ' ' + rest + 's' : '');
    }

    function friendlyActivity(name) {
        return ({
            Searching: 'find target', Engaged: 'fight', Recovering: 'heal / recover',
            Traveling: 'travel', Idle: 'idle', Blocked: 'blocked', Unknown: 'unknown'
        })[name] || name || 'unknown';
    }

    function pluralCount(value, singular, plural) {
        var count = Math.max(0, +value || 0);
        return count + ' ' + (count === 1 ? singular : (plural || singular + 's'));
    }

    function cleanAlarmLabel(value) {
        return String(value || '').replace(/^alarm\s*[·:\-]\s*/i, '').trim();
    }

    function friendlyCondition(episode) {
        var condition = String((episode && episode.condition) || '').toLowerCase();
        if (condition === 'dead') return 'Dead';
        if (condition === 'blocked') return 'Activity blocked';
        return cleanAlarmLabel(episode && episode.label) || 'Attention needed';
    }

    function secondaryAlarmReasons(episode, reasons) {
        var primary = String((episode && episode.condition) || '').toLowerCase() === 'blocked'
            ? 'activity blocked' : String((episode && episode.condition) || '').toLowerCase();
        return (reasons || []).filter(function (reason) {
            return episodeKind(episode) !== 'condition'
                || cleanAlarmLabel(reason).toLowerCase() !== primary;
        });
    }

    function episodeTitle(episode) {
        var kind = episodeKind(episode);
        if (kind === 'condition') return friendlyCondition(episode);
        if (kind === 'eventburst') return cleanAlarmLabel(episode.label) || 'Attention event';
        return episode.label || 'Activity';
    }

    function episodeStatus(episode) {
        if (episodeKind(episode) !== 'condition') return '';
        if (episode.continuationUnknown) return 'end unknown';
        if (String(episode.status).toLowerCase() === 'ongoing') return 'ongoing';
        if (String(episode.status).toLowerCase() === 'resolved') return 'resolved';
        return 'observed';
    }

    function episodeTimeRange(episode) {
        var start = (episode.startedBeforeWindow ? '≤' : '') + timeOf(episode.start);
        if (episodeKind(episode) === 'condition' && episodeStatus(episode) === 'ongoing')
            return start + '–…';
        return start + '–' + timeOf(episode.end);
    }

    function episodeMetaParts(episode) {
        var kind = episodeKind(episode);
        var parts = [];
        if (kind === 'condition') {
            if (episode.startedBeforeWindow) parts.push('already active at window start');
            if (!episode.startedBeforeWindow || (+episode.transitionCount || 0) > 0)
                parts.push(pluralCount(episode.transitionCount, 'meaningful transition'));
            parts.push(pluralCount(episode.confirmationCount, 'confirmation'));
            if ((episode.events || []).length)
                parts.push(pluralCount((episode.events || []).reduce(function (sum, event) {
                    return sum + Math.max(1, +event.occurrenceCount || 1);
                }, 0), 'related event'));
        } else if (kind === 'eventburst') {
            parts.push(pluralCount(episode.occurrenceCount || episode.decisionCount, 'occurrence'));
        } else {
            parts.push(pluralCount(episode.decisionCount, 'activity change'));
            parts.push(pluralCount(episode.rawSegmentCount, 'observed frame'));
        }
        parts.push(durationText(episode.durationMs, episode.start, episode.end));
        if (episode.killDelta > 0) parts.push('+' + episode.killDelta + ' kills');
        return parts;
    }

    function activityClass(name, attention) {
        if (attention || name === 'Blocked') return 'cb-attention';
        return ({ Searching: 'cb-search', Engaged: 'cb-combat', Recovering: 'cb-heal',
            Traveling: 'cb-search', Idle: 'cb-wait' })[name] || 'cb-wait';
    }

    function activityStrip(episode) {
        var spans = episode.activitySpans || [];
        if (!spans.length) return '';
        return '<div class="cb-activity-strip">' + spans.map(function (span) {
            var amount = Math.max(1, Math.min(24, span.rawSegmentCount || span.decisionCount || 1));
            var title = friendlyActivity(span.activity) + ' · ' + durationText(span.durationMs, span.start, span.end) +
                ' · ' + pluralCount(span.rawSegmentCount || 1, 'observed frame');
            return '<span class="cb-activity-chip ' + activityClass(span.activity, episode.severity === 'alarm') + '"' +
                ' style="flex-grow:' + amount + '" title="' + esc(title) + '"></span>';
        }).join('') + '</div>';
    }

    function episodeFlowLabel(episode) {
        if (episode.cycle && episode.cycle.pattern && episode.cycle.pattern.length) {
            return episode.cycle.pattern.map(friendlyActivity).join(' → ') + ' ×' + episode.cycle.completeCycles;
        }
        var names = (episode.activitySpans || []).map(function (span) { return friendlyActivity(span.activity); });
        if (names.length > 6) names = names.slice(0, 5).concat(['…']);
        return names.join(' → ') || 'no activity readback';
    }

    function renderHistList() {
        if (!histEpisodes.length) return;
        var html = '';
        histEpisodes.forEach(function (episode, ix) {
            var kind = episodeKind(episode);
            var attention = kind !== 'routine' || episode.severity === 'alarm';
            var selected = ix === histEpisodeIx;
            var status = episodeStatus(episode);
            var stateLabel = kind === 'condition' ? 'CONDITION'
                : (kind === 'eventburst' ? 'EVENT ×' + Math.max(1, +episode.occurrenceCount || +episode.decisionCount || 1) : 'ROUTINE');
            var meta = episodeMetaParts(episode).map(function (part) {
                return '<span>' + esc(part) + '</span>';
            }).join('');
            var extraReasons = secondaryAlarmReasons(episode, episode.alarmReasons);
            var reason = attention && extraReasons.length
                ? '<div class="cb-episode-reason">' + esc(extraReasons.join(' · ')) + '</div>'
                : '';
            var relatedEvents = kind === 'condition' && (episode.events || []).length
                ? '<div class="cb-episode-events">Also observed: ' + (episode.events || []).map(function (event) {
                    var count = Math.max(1, +event.occurrenceCount || 1);
                    return esc(cleanAlarmLabel(event.reason || event.label || 'attention event')) + (count > 1 ? ' ×' + count : '');
                }).join(' · ') + '</div>'
                : '';
            var bodyLead = kind === 'condition'
                ? 'One continuous condition. Repeated observations are confirmations, not new incidents.'
                : (kind === 'eventburst'
                    ? pluralCount(episode.occurrenceCount || episode.decisionCount, 'point event') + ' grouped into one burst.'
                    : episodeFlowLabel(episode));
            html += '<article class="cb-episode ' + (attention ? 'cb-episode-attention' : 'cb-episode-normal') +
                ' cb-episode-' + kind +
                (selected ? ' cb-selected cb-expanded' : '') + '" data-episode-ix="' + ix + '"' +
                ' aria-selected="' + selected + '" aria-expanded="' + selected + '">' +
                '<div class="cb-episode-summary"><div class="cb-episode-top">' +
                '<span class="cb-episode-state">' + esc(stateLabel) + '</span>' +
                '<span class="cb-episode-title">' + esc(episodeTitle(episode)) + '</span>' +
                (status ? '<span class="cb-episode-status cb-status-' + esc(status.replace(/\s+/g, '-')) + '">' + esc(status) + '</span>' : '') +
                '<span class="cb-episode-time">' + esc(episodeTimeRange(episode)) + '</span></div>' +
                '<div class="cb-episode-meta">' + meta + '</div>' +
                (kind === 'routine' ? activityStrip(episode) : '') + '</div>' +
                '<div class="cb-episode-body"><strong>' + esc(bodyLead) + '</strong>' + reason + relatedEvents +
                '<div class="cb-episode-meta">Select to inspect meaningful changes and their source.</div></div></article>';
        });
        $('#cbHistList').html(html);
        var selected = $('#cbHistList .cb-selected');
        if (selected.length) selected[0].scrollIntoView({ block: 'nearest' });
        updateHistNav();
    }

    function siteForHit(hit) {
        return (hit && hit.site) || (hit && sites[hit.siteId]) || null;
    }

    function siteShort(site, siteId) {
        if (!site) return 'site #' + siteId;
        var desc = site.desc || site.description || '';
        var colon = desc.indexOf(':');
        return colon >= 0 ? desc.substring(colon + 1).trim() : (desc || ('site #' + siteId));
    }

    function findDecisionIndex(id) {
        return histChanges.findIndex(function (decision) { return decision.id === id; });
    }

    function preferredPathIndex(decision) {
        var hits = (decision && decision.hits) || [];
        if (!hits.length) return -1;
        var focus = decision.focusSiteId;
        for (var i = hits.length - 1; i >= 0; i--) if (hits[i].siteId === focus) return i;
        return hits.length - 1;
    }

    function displayPresentation(decision, episode, localIx) {
        var presentation = decisionPresentation(decision, episode, localIx);
        if (presentation === 'confirmation' && episode.startedBeforeWindow && localIx === 0)
            return 'windowstart';
        return presentation;
    }

    function decisionPointEvents(decision) {
        return (decision && decision.events) || [];
    }

    function decisionEventCount(decision) {
        var events = decisionPointEvents(decision);
        if (!events.length) return Math.max(1, +decision.rawSegmentCount || 1);
        return events.reduce(function (sum, event) {
            return sum + Math.max(1, +event.occurrenceCount || 1);
        }, 0);
    }

    function decisionMomentTitle(decision, episode, localIx) {
        var presentation = displayPresentation(decision, episode, localIx);
        var condition = String(episode.condition || '').toLowerCase();
        var transition = String(decision.transition || '').toLowerCase();
        if (presentation === 'windowstart') return 'Condition already active';
        if (presentation === 'confirmation') return 'Condition unchanged';
        if (presentation === 'event') {
            var point = decisionPointEvents(decision)[0];
            return cleanAlarmLabel((point && (point.reason || point.label)) || (decision.alarmReasons || [])[0] || decision.label) || 'Attention event';
        }
        if (presentation === 'transition') {
            if (transition === 'onset') {
                if (condition === 'dead') return 'Death observed';
                if (condition === 'blocked') return 'Activity became blocked';
                return 'Condition began';
            }
            if (transition === 'clear') {
                if (condition === 'dead') return 'Alive again';
                if (condition === 'blocked') return 'Activity moving again';
                return 'Condition cleared';
            }
            if (transition === 'phase') {
                if (condition === 'dead') return 'Recovery phase changed';
                if (condition === 'blocked') return 'Blocked-state handling changed';
                return 'Condition phase changed';
            }
            return cleanAlarmLabel(decision.label) || 'Condition changed';
        }
        var state = decision.state || {};
        return friendlyActivity(state.activity) || decision.label || decision.kind || 'Activity changed';
    }

    function decisionMomentSubtitle(decision, episode, localIx) {
        var presentation = displayPresentation(decision, episode, localIx);
        var state = decision.state || {};
        if (presentation === 'confirmation')
            return 'No new incident; the same condition was observed again.';
        if (presentation === 'windowstart')
            return 'The condition began before the retained reader window.';
        if (presentation === 'event') {
            var events = decisionPointEvents(decision);
            if (events.length > 1) return pluralCount(decisionEventCount(decision), 'related point event');
            return cleanAlarmLabel((events[0] && (events[0].reason || events[0].label)) || (decision.alarmReasons || [])[0] || state.step || decision.label);
        }
        return state.step || cleanAlarmLabel(decision.label) || '';
    }

    function renderDecisionRow(item, episode, localIx, rawConfirmation) {
        var flatIx = findDecisionIndex(item.id);
        var selected = flatIx === histIx;
        var presentation = displayPresentation(item, episode, localIx);
        var marker = presentation === 'transition'
            ? (String(item.transition).toLowerCase() === 'clear' ? 'OUT' : 'IN')
            : (presentation === 'windowstart' ? '…' : (presentation === 'event' ? '!' : (presentation === 'confirmation' ? '·' : (localIx + 1))));
        var meta = '';
        if (presentation === 'confirmation')
            meta = pluralCount(item.rawSegmentCount || 1, 'confirmation');
        else if (presentation === 'event')
            meta = pluralCount(decisionEventCount(item), 'occurrence');
        else if (presentation === 'transition' || presentation === 'windowstart')
            meta = presentation === 'windowstart' ? 'window start' : (item.transition || 'transition');
        else
            meta = pluralCount(item.rawSegmentCount || 1, 'observed frame');

        return '<div class="cb-decision-row cb-decision-' + presentation +
            (rawConfirmation ? ' cb-raw-confirmation' : '') +
            (selected ? ' cb-selected' : '') +
            (presentation === 'event' || item.severity === 'alarm' ? ' cb-attention' : '') +
            '" data-decision-id="' + item.id + '" aria-selected="' + selected + '">' +
            '<span class="cb-decision-index">' + esc(marker) + '</span>' +
            '<span class="cb-decision-copy"><strong>' + esc(decisionMomentTitle(item, episode, localIx)) + '</strong>' +
            '<span>' + esc(decisionMomentSubtitle(item, episode, localIx)) + '</span></span>' +
            '<span class="cb-decision-meta">' + esc(meta) + '<br>' + esc(timeOf(item.start)) + '</span></div>';
    }

    function renderConfirmationGroup(items, episode, groupIx) {
        if (!items.length) return '';
        var count = items.reduce(function (sum, entry) {
            return sum + Math.max(1, +entry.item.rawSegmentCount || 1);
        }, 0);
        var open = !!histConfirmationOpen[episodeKey(episode)];
        var selectedInside = items.some(function (entry) { return findDecisionIndex(entry.item.id) === histIx; });
        var html = '<button type="button" class="cb-confirmation-group' + (selectedInside ? ' cb-selected' : '') +
            '" data-confirm-toggle="' + esc(episodeKey(episode)) + '" data-confirm-group="' + groupIx + '" aria-expanded="' + open + '">' +
            '<span class="cb-decision-index">' + (open ? '−' : '+') + '</span>' +
            '<span class="cb-decision-copy"><strong>' + esc(pluralCount(count, 'unchanged confirmation')) + '</strong>' +
            '<span>' + pluralCount(items.length, 'recorded frame') + '; no new incident.</span></span>' +
            '<span class="cb-decision-meta">' + (open ? 'Hide raw' : 'Show raw') + '</span></button>';
        if (open) {
            items.forEach(function (entry) {
                html += renderDecisionRow(entry.item, episode, entry.localIx, true);
            });
        }
        return html;
    }

    function renderDecisionMoments(episode) {
        var html = '';
        var confirmations = [];
        var groupIx = 0;
        function flushConfirmations() {
            if (!confirmations.length) return;
            html += renderConfirmationGroup(confirmations, episode, groupIx++);
            confirmations = [];
        }
        (episode.decisions || []).forEach(function (item, localIx) {
            if (displayPresentation(item, episode, localIx) === 'confirmation') {
                confirmations.push({ item: item, localIx: localIx });
                return;
            }
            flushConfirmations();
            html += renderDecisionRow(item, episode, localIx, false);
        });
        flushConfirmations();
        return html || '<div class="cb-reader-empty"><strong>No trace moments were retained for this episode.</strong></div>';
    }

    function renderHistDetail() {
        var decision = histChanges[histIx];
        var episode = histEpisodes[histEpisodeIx];
        if (!decision || !episode) {
            $('#cbHistDetail').html('<div class="cb-reader-empty"><strong>No decision to show.</strong></div>');
            $('#cbDecisionList,#cbPathList').empty();
            renderSourceEmpty('Choose a decision step to read its code.');
            return;
        }

        var state = decision.state || {};
        var kind = episodeKind(episode);
        var presentation = displayPresentation(decision, episode, decision._localIx);
        var enter = decision.enter || [], exit = decision.exit || [];
        var chips = '';
        enter.forEach(function (id) {
            var site = sites[id];
            chips += '<span class="cb-chip cb-chip-in">+ ' + esc(siteShort(site, id)) + '</span>';
        });
        exit.forEach(function (id) {
            var site = sites[id];
            chips += '<span class="cb-chip cb-chip-out">− ' + esc(siteShort(site, id)) + '</span>';
        });
        if (decision.orderChanged) chips += '<span class="cb-chip cb-chip-in">execution order changed</span>';
        if (!chips) chips = '<span class="cb-chip cb-chip-none">' +
            (presentation === 'confirmation' ? 'same condition; no meaningful transition' : 'same probes; activity/state changed') + '</span>';

        var decisionAlarmReasons = secondaryAlarmReasons(episode, decision.alarmReasons);
        var alarm = decisionAlarmReasons.length
            ? '<div style="color:var(--danger,#f7768e);margin-top:6px">' + esc(decisionAlarmReasons.join(' · ')) + '</div>' : '';
        var status = episodeStatus(episode);
        var episodeMeta = episodeMetaParts(episode).join(' · ');
        var selectedFrames = Math.max(1, +decision.rawSegmentCount || 1);
        var childEvents = kind === 'condition' && (episode.events || []).length
            ? '<div class="cb-hd-events"><strong>Related point events</strong>' + (episode.events || []).map(function (event) {
                var count = Math.max(1, +event.occurrenceCount || 1);
                return '<span>' + esc(cleanAlarmLabel(event.reason || event.label || 'attention event')) + (count > 1 ? ' ×' + count : '') + '</span>';
            }).join('') + '</div>' : '';
        $('#cbHistDetail').html(
            '<div class="cb-hd-head"><b>' + esc(episodeTitle(episode)) + '</b>' +
            (status ? ' · ' + esc(status) : ' · ' + esc(episodeFlowLabel(episode))) + '</div>' +
            '<div><strong>' + esc(decisionMomentTitle(decision, episode, decision._localIx)) + '</strong>' +
            (decisionMomentSubtitle(decision, episode, decision._localIx) ? ' · ' + esc(decisionMomentSubtitle(decision, episode, decision._localIx)) : '') + '</div>' +
            '<div class="cb-hd-head" style="margin-top:4px">Episode: ' + esc(episodeMeta) + '</div>' +
            '<div class="cb-hd-head">Selected moment: ' + esc(timeOf(decision.start)) + ' → ' + esc(timeOf(decision.end)) +
            ' · ' + esc(pluralCount(selectedFrames, presentation === 'confirmation' ? 'confirmation' : 'observed frame')) +
            ' · ' + esc(durationText(decision.durationMs, decision.start, decision.end)) +
            (state.taskKills != null ? ' · kill count ' + state.taskKills : '') + '</div>' +
            '<div class="cb-hd-diff">' + chips + '</div>' + alarm + childEvents +
            (histWindowTruncated ? '<div class="cb-hd-head">Older activity has rolled out of the 20-minute reader window.</div>' : ''));

        $('#cbDecisionList').html(renderDecisionMoments(episode));

        var hits = decision.hits || [];
        if (histPathIx < 0 || histPathIx >= hits.length) histPathIx = preferredPathIndex(decision);
        var pathHtml = '';
        hits.forEach(function (hit, ix) {
            var site = siteForHit(hit);
            var file = site && site.file ? site.file.split(/[\\/]/).pop() + ':' + site.line : 'source unavailable';
            var payload = hit.value !== null && hit.value !== undefined ? ' = ' + hit.value :
                (hit.note ? ' · ' + hit.note : '');
            pathHtml += '<li class="cb-path-step' + (ix === histPathIx ? ' cb-selected' : '') + '" data-path-ix="' + ix +
                '" aria-current="' + (ix === histPathIx ? 'step' : 'false') + '"><div class="cb-path-copy">' +
                esc(siteShort(site, hit.siteId) + payload) + '<small>' + esc(file) + '</small></div></li>';
        });
        $('#cbPathList').html(pathHtml || '<li class="cb-reader-empty"><strong>No own-context probes were recorded in this decision.</strong></li>');
        showSelectedSource();

        var selectedDecision = $('#cbDecisionList .cb-selected');
        if (selectedDecision.length) selectedDecision[0].scrollIntoView({ block: 'nearest' });
        updateHistNav();
    }

    function renderSourceEmpty(message) {
        $('#cbSourceHeader').html('<div class="cb-pane-title">Source at this step</div>' +
            '<div class="cb-pane-kicker">A few lines before the fired probe show the literal branch that led here.</div>');
        $('#cbSourceBody').html('<div class="cb-reader-empty"><strong>' + esc(message) + '</strong></div>');
    }

    function showSelectedSource() {
        var decision = histChanges[histIx];
        var hits = (decision && decision.hits) || [];
        var hit = hits[histPathIx];
        if (!hit) {
            renderSourceEmpty('This decision has no source-bearing probe.');
            return;
        }
        var previous = histPathIx > 0 ? hits[histPathIx - 1] : null;
        var site = siteForHit(hit);
        var priorSite = siteForHit(previous);
        var sourceName = site && site.file ? site.file.split(/[\\/]/).pop() + ':' + site.line : 'registered site #' + hit.siteId;
        var flow = priorSite ? siteShort(priorSite, previous.siteId) + ' → ' + siteShort(site, hit.siteId)
            : 'decision entry → ' + siteShort(site, hit.siteId);
        $('#cbSourceHeader').html('<div class="cb-pane-title">Step ' + (histPathIx + 1) + ' · ' + esc(sourceName) + '</div>' +
            '<div class="cb-pane-kicker">' + esc(flow) + '</div>');
        $('#cbSourceBody').html('<div class="cb-reader-empty"><strong>Reading source…</strong></div>');

        var token = ++sourceRequestSerial;
        var cacheKey = sourceVersion + ':' + hit.siteId;
        if (sourceCache[cacheKey]) {
            renderSourceSnippet(sourceCache[cacheKey], token, flow, histPathIx + 1);
            return;
        }
        $.getJSON('/CircuitTrace/Source?siteId=' + encodeURIComponent(hit.siteId) +
            '&before=6&after=1&sourceVersion=' + encodeURIComponent(sourceVersion))
            .done(function (data) {
                var responseVersion = String((data && data.sourceVersion) || '');
                if (responseVersion && responseVersion !== sourceVersion) {
                    sourceVersion = responseVersion;
                    sourceCache = {};
                    $('#cbSource').attr('data-source-version', sourceVersion);
                }
                sourceCache[sourceVersion + ':' + hit.siteId] = data;
                renderSourceSnippet(data, token, flow, histPathIx + 1);
            })
            .fail(function (xhr) {
                if (token !== sourceRequestSerial) return;
                var message = xhr.responseJSON && xhr.responseJSON.error
                    ? xhr.responseJSON.error : 'Source could not be read for this registered probe.';
                $('#cbSourceBody').html('<div class="cb-reader-empty"><strong>' + esc(message) + '</strong></div>');
            });
    }

    function renderSourceSnippet(data, token, flow, stepNumber) {
        if (token !== sourceRequestSerial) return;
        if (!data || !data.available) {
            $('#cbSourceBody').html('<div class="cb-reader-empty"><strong>' +
                esc((data && (data.error || data.sourceNote)) || 'Source is unavailable for this probe.') + '</strong></div>');
            return;
        }
        $('#cbSourceHeader').html('<div class="cb-pane-title">Step ' + stepNumber + ' · ' +
            esc(data.file + ':' + data.line) + '</div><div class="cb-pane-kicker">' + esc(flow) + '</div>');
        var lines = data.lines || [];
        var html = '<div style="padding:2px 14px 8px;color:var(--text-muted);font-size:10.5px">' +
            esc(data.sourceNote || 'Configured source checkout; build revision is not verified.') + '</div>';
        lines.forEach(function (line) {
            html += '<div class="cb-source-line' + (line.isTarget ? ' cb-target' : '') + '" data-target="' + !!line.isTarget + '">' +
                '<span class="cb-source-number">' + line.number + '</span><span class="cb-source-text">' + esc(line.text) + '</span></div>';
        });
        $('#cbSourceBody').html(html);
        var target = $('#cbSourceBody .cb-target');
        if (target.length) target[0].scrollIntoView({ block: 'center' });
    }

    function renderHistoryLiveState() {
        $('#cbHistLive').toggleClass('cb-frozen', paused)
            .attr('data-state', paused ? 'frozen' : 'live')
            .text(paused ? 'paused · this change and source are frozen' : 'live · newest meaningful change follows automatically');
        $('#cbHistNew').toggleClass('cb-show', paused && histNewCount > 0)
            .text(histNewCount + ' new ' + (histNewCount === 1 ? 'change' : 'changes'));
    }

    function updateHistNav() {
        var position = histNavIndexes.indexOf(histIx);
        var previous = histNavIndexes.some(function (flatIx) { return flatIx < histIx; });
        var next = histNavIndexes.some(function (flatIx) { return flatIx > histIx; });
        $('#cbHistPrev').prop('disabled', !previous);
        $('#cbHistNext').prop('disabled', !next);
        $('#cbHistCount').text(!histNavIndexes.length ? '0 changes'
            : (position >= 0 ? 'change ' + (position + 1) + ' / ' + histNavIndexes.length
                : 'confirmation · ' + histNavIndexes.length + ' changes'));
    }

    function selectDecision(flatIx, freeze) {
        if (flatIx < 0 || flatIx >= histChanges.length) return;
        var changed = !histChanges[histIx] || histChanges[histIx].id !== histChanges[flatIx].id;
        if (freeze) setPaused(true);
        histIx = flatIx;
        histEpisodeIx = histChanges[flatIx]._episodeIx;
        if (changed) histPathIx = -1;
        renderHistList();
        renderHistDetail();
    }

    function stepHistory(delta) {
        if (!histNavIndexes.length) return;
        var candidates = histNavIndexes.filter(function (flatIx) {
            return delta < 0 ? flatIx < histIx : flatIx > histIx;
        });
        var next = delta < 0 ? candidates[candidates.length - 1] : candidates[0];
        if (next != null && next !== histIx) selectDecision(next, true);
    }

    function stepPath(delta) {
        var decision = histChanges[histIx];
        var hits = (decision && decision.hits) || [];
        if (!hits.length) return;
        setPaused(true);
        histPathIx = Math.max(0, Math.min(hits.length - 1, histPathIx + delta));
        $('#cbPathList .cb-path-step').removeClass('cb-selected').attr('aria-current', 'false')
            .filter('[data-path-ix="' + histPathIx + '"]').addClass('cb-selected').attr('aria-current', 'step');
        showSelectedSource();
    }

    function refreshActiveView() {
        if (histMode) captureHistory({ quiet: true, preserve: paused });
        else poll();
    }

    $('#cbHistPrev').on('click', function () { stepHistory(-1); });
    $('#cbHistNext').on('click', function () { stepHistory(1); });
    $('#cbHistRefresh,#cbHistNew').on('click', function () { setPaused(false); });
    $(document).on('click', '.cb-episode', function () {
        var episodeIx = +$(this).data('episode-ix');
        var episode = histEpisodes[episodeIx];
        if (!episode || !(episode.decisions || []).length) return;
        var firstMeaningful = histNavIndexes.find(function (flatIx) {
            return histChanges[flatIx] && histChanges[flatIx]._episodeIx === episodeIx;
        });
        selectDecision(firstMeaningful != null ? firstMeaningful : findDecisionIndex(episode.decisions[0].id), true);
    });
    $(document).on('click', '.cb-decision-row', function () {
        selectDecision(findDecisionIndex(+$(this).data('decision-id')), true);
    });
    $(document).on('click', '.cb-confirmation-group', function (e) {
        e.preventDefault();
        e.stopPropagation();
        var key = String($(this).data('confirm-toggle') || '');
        histConfirmationOpen[key] = !histConfirmationOpen[key];
        renderHistDetail();
    });
    $(document).on('click', '.cb-path-step', function () {
        setPaused(true);
        histPathIx = +$(this).data('path-ix');
        $('#cbPathList .cb-path-step').removeClass('cb-selected').attr('aria-current', 'false');
        $(this).addClass('cb-selected').attr('aria-current', 'step');
        showSelectedSource();
    });
    $(document).on('keydown', function (e) {
        var tag = (e.target && e.target.tagName || '').toLowerCase();
        if (histMode && tag !== 'input' && tag !== 'select' && tag !== 'textarea' && !$('#cbWhoModal').hasClass('cb-open')) {
            if (e.key === 'ArrowLeft') { e.preventDefault(); stepHistory(-1); }
            else if (e.key === 'ArrowRight') { e.preventDefault(); stepHistory(1); }
            else if (e.key === 'ArrowUp') { e.preventDefault(); stepPath(-1); }
            else if (e.key === 'ArrowDown') { e.preventDefault(); stepPath(1); }
        }
        if (e.key === 'Escape') closeWho();
    });

    // ---------- "Traced…" modal: who is armed / recording right now ----------
    function openWho() { $('#cbWhoModal').addClass('cb-open'); renderWho(); }
    function closeWho() { $('#cbWhoModal').removeClass('cb-open'); }

    function renderWho() {
        $.getJSON('/CircuitTrace/Status', function (d) {
            if (!d) return;
            var armed = d.armed || [];
            $('#cbWhoSub').text('mode: ' + d.mode + ' · ' + armed.length + ' armed (writing to disk) · ' +
                d.ringBots + ' bots ringing in memory · ' + d.sites + ' sites');
            if (!armed.length) {
                $('#cbWhoBody').html('<div class="cb-who-empty">No bots are armed.' +
                    (d.mode === 'shadow'
                        ? ' Shadow is on, so every bot is recording in memory — arm one to write it to disk and follow it here.'
                        : ' Shadow mode is off — turn it on to record.') + '</div>');
                return;
            }
            // Resolve the armed guids against the characters DB + bridge: an armed guid
            // outlives its bot (deleted bots stayed "armed" forever), so label the dead
            // and offline ones and offer a one-click prune of the deleted ones.
            $.getJSON('/CircuitTrace/ArmedRoster', function (r) {
                var rows = (r && r.rows) || [];
                var missing = (r && r.missing) || 0;
                var html = '';
                if (missing > 0) {
                    html += '<div class="cb-who-row cb-who-prune" style="justify-content:space-between">' +
                        '<span class="cb-who-name" style="color:var(--text-secondary)">' + missing +
                        ' armed guid' + (missing === 1 ? '' : 's') + ' no longer exist in the characters DB</span>' +
                        '<button type="button" id="cbWhoPrune" class="cb-who-go" style="cursor:pointer">untrack deleted →</button></div>';
                }
                rows.forEach(function (b) {
                    var g = b.guid;
                    var state = !b.exists ? 'deleted' : (!b.onBridge ? 'offline' : (b.ringing ? 'rec' : 'idle'));
                    var rec = state === 'rec' ? '● REC' : state === 'idle' ? '○ ARMED' : state === 'offline' ? '○ OFFLINE' : '✕ DELETED';
                    var dim = state === 'deleted' || state === 'offline' ? ' style="opacity:.55"' : '';
                    html += '<div class="cb-who-row" data-guid="' + g + '" data-state="' + state + '"' + dim + '>' +
                        '<span class="cb-who-rec">' + rec + '</span>' +
                        '<span class="cb-who-name">' + esc(b.name || ('bot ' + g)) + '</span>' +
                        '<span class="cb-who-guid">#' + g + '</span>' +
                        (state === 'deleted'
                            ? '<span class="cb-who-go cb-who-disarm" data-guid="' + g + '">disarm →</span>'
                            : '<span class="cb-who-go">select →</span>') + '</div>';
                });
                $('#cbWhoBody').html(html);
            }).fail(function () {
                // Roster lookup failed (DB down?) — fall back to the plain armed list.
                var nameOf = {};
                botList.forEach(function (b) { nameOf[b.guid] = b.name; });
                var html = '';
                armed.slice().sort(function (a, b) { return (nameOf[a] || ('' + a)).localeCompare(nameOf[b] || ('' + b)); })
                    .forEach(function (g) {
                        html += '<div class="cb-who-row" data-guid="' + g + '">' +
                            '<span class="cb-who-rec">● REC</span>' +
                            '<span class="cb-who-name">' + esc(nameOf[g] || ('bot ' + g)) + '</span>' +
                            '<span class="cb-who-guid">#' + g + '</span>' +
                            '<span class="cb-who-go">select →</span></div>';
                    });
                $('#cbWhoBody').html(html);
            });
        });
    }

    $(document).on('click', '#cbWhoPrune', function (e) {
        e.stopPropagation();
        $.post('/CircuitTrace/Prune').done(function (r) {
            showToastLite('untracked ' + ((r && r.removed) || []).length + ' deleted bot(s)');
            refreshStatus(); renderWho();
        });
    });
    $(document).on('click', '.cb-who-disarm', function (e) {
        e.stopPropagation();
        var g = +$(this).data('guid');
        $.post('/CircuitTrace/Disarm?guid=' + g).done(function () { refreshStatus(); renderWho(); });
    });

    $('#cbWho').on('click', openWho);
    $('#cbWhoClose').on('click', closeWho);
    $('#cbWhoModal').on('click', function (e) { if (e.target === this) closeWho(); });
    $(document).on('click', '.cb-who-row', function () {
        var g = +$(this).data('guid');
        if ($('#cbBot').find('option[value="' + g + '"]').length) $('#cbBot').val(g).trigger('change');
        else showToastLite('bot ' + g + ' is armed but not in the live list');
        closeWho();
    });

    // The viewer is standalone (no bots.js) — a tiny toast for the rare miss above.
    function showToastLite(msg) {
        var el = $('#cbToastLite');
        if (!el.length) el = $('<div id="cbToastLite" style="position:fixed;bottom:18px;left:50%;transform:translateX(-50%);z-index:60;padding:8px 14px;border-radius:6px;background:var(--bg-card-alt);border:1px solid var(--border-light);color:var(--text-primary);font-size:12.5px;box-shadow:0 6px 20px rgba(0,0,0,.4)"></div>').appendTo('body');
        el.text(msg).stop(true, true).fadeIn(120).delay(2200).fadeOut(300);
    }
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
    loadSites().always(function () { setView('history'); });
    loadBots();
    refreshStatus();
    applyView();
    setInterval(refreshActiveView, 1500);
    setInterval(refreshStatus, 5000);
    setInterval(loadBots, 30000);
    $(window).on('resize', function () { if (!userAdjusted) fitWidth(); else applyView(); });
});
