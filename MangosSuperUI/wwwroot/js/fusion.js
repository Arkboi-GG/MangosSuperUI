/* ============================================================================
   SuperUI Fusion — Bots surface
   Drives the sidebar-less, client-framing dashboard. Pull-only against the
   existing BotsController REST endpoints. Also speaks to the SuperUIFusion WPF
   shell over the WebView2 message channel:

     page -> shell : { type:'holeRect', x,y,w,h }   (physical px; place client)
                     { type:'clientVisible', visible:bool }  (hide for modals)
     shell -> page : "toggleMaximize"               (Ctrl+Alt+H global hotkey)

   Runs identically in a plain browser tab (the messages are simply no-ops when
   window.chrome.webview is absent).
   ============================================================================ */
(function () {
    "use strict";

    // ----- constants (mirror bots.js) -----
    var CLASS_NAMES = { 1: 'Warrior', 2: 'Paladin', 3: 'Hunter', 4: 'Rogue', 5: 'Priest', 7: 'Shaman', 8: 'Mage', 9: 'Warlock', 11: 'Druid' };
    var CLASS_CSS = { 1: 'warrior', 2: 'paladin', 3: 'hunter', 4: 'rogue', 5: 'priest', 7: 'shaman', 8: 'mage', 9: 'warlock', 11: 'druid' };
    var RACE_NAMES = { 1: 'Human', 2: 'Orc', 3: 'Dwarf', 4: 'Night Elf', 5: 'Undead', 6: 'Tauren', 7: 'Gnome', 8: 'Troll' };
    var GOAL_COLOR = {
        Questing: '#7aa2f7', Grinding: '#ff9e64', Training: '#bb9af7', Following: '#9ece6a',
        Maintenance: '#e0af68', Idle: '#5c6773'
    };
    var QUALITY_COLORS = { 0: '#9d9d9d', 1: '#ffffff', 2: '#1eff00', 3: '#0070dd', 4: '#a335ee', 5: '#40c4ff', 6: '#ff8000', 7: '#e6cc80', 8: '#e62020' };
    var GROUPING_MODES = ['Off', 'Sticky', 'Opportunistic'];

    // ----- state -----
    var botStates = {};      // guid -> BotState (bridge)
    var groupsByGuid = {};   // guid -> { groupId, isGroupLeader }
    var brain = { enabled: false, activeBots: 0, groupingMode: 0, groups: [] };
    var fleet = { errorsPerMin: 0, recent: [] };
    var selectedGuid = null;
    var detailTab = 'live';
    var feedMode = 'bot';    // 'bot' | 'group' | 'fleet'
    var liveData = null;     // last LiveState for selected
    var logSeq = 0;
    var groupFeedId = -1;    // groupId currently aggregated in the feed
    var groupFeedSeq = 0;    // shared log cursor for the group aggregate
    var rosterFilterText = '';
    var openModals = 0;

    // ----- tiny DOM helpers -----
    function $(id) { return document.getElementById(id); }
    function el(html) { var t = document.createElement('template'); t.innerHTML = html.trim(); return t.content.firstChild; }
    function esc(s) {
        return (s == null ? '' : String(s)).replace(/[&<>"']/g, function (c) {
            return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
        });
    }
    function pct(a, b) { if (!b) return 0; return Math.max(0, Math.min(100, Math.round(a / b * 100))); }
    function secs(s) { if (s == null) return '—'; s = Math.round(s); if (s < 60) return s + 's'; var m = Math.floor(s / 60); return m + 'm' + (s % 60) + 's'; }
    function gold(c) {
        c = c || 0; var g = Math.floor(c / 10000), s = Math.floor((c % 10000) / 100), cp = c % 100;
        return g + '<span style="color:#e0af68">g</span> ' + s + '<span style="color:#c0c0c0">s</span> ' + cp + '<span style="color:#b87333">c</span>';
    }
    function clsColor(id) { return 'var(--class-' + (CLASS_CSS[id] || 'priest') + ')'; }

    // ----- shell bridge -----
    var hasShell = !!(window.chrome && window.chrome.webview);
    function postShell(obj) { if (hasShell) { try { window.chrome.webview.postMessage(obj); } catch (e) { } } }
    function postHoleRect() {
        var vp = $('client-viewport'); if (!vp) return;
        var r = vp.getBoundingClientRect();
        var dpr = window.devicePixelRatio || 1;
        postShell({
            type: 'holeRect',
            x: Math.round(r.left * dpr), y: Math.round(r.top * dpr),
            w: Math.round(r.width * dpr), h: Math.round(r.height * dpr)
        });
    }
    function setClientVisible(v) { postShell({ type: 'clientVisible', visible: !!v }); }
    if (hasShell) {
        document.documentElement.classList.add('has-shell');
        $('root').classList.add('embedded');
        // shell -> page
        window.chrome.webview.addEventListener('message', function (e) {
            var d = e.data;
            if (d === 'toggleMaximize' || (d && d.type === 'toggleMaximize')) toggleMax();
        });
    }

    // keep the hole glued to the layout
    var ro = new ResizeObserver(function () { postHoleRect(); });
    function startRectTracking() {
        ro.observe($('client-viewport'));
        window.addEventListener('resize', postHoleRect);
        // safety net for transitions/animations the observer might miss
        setInterval(postHoleRect, 700);
        requestAnimationFrame(postHoleRect);
    }

    // ----- fetch helper -----
    function getJSON(url) { return fetch(url, { headers: { 'Accept': 'application/json' } }).then(function (r) { return r.json(); }); }
    function postJSON(url, body) {
        return fetch(url, {
            method: 'POST', headers: { 'Content-Type': 'application/json' },
            body: body !== undefined ? JSON.stringify(body) : undefined
        }).then(function (r) { return r.json(); });
    }

    function toast(msg, kind) {
        var t = $('toast'); t.textContent = msg; t.className = 'toast show ' + (kind || '');
        clearTimeout(toast._t); toast._t = setTimeout(function () { t.className = 'toast ' + (kind || ''); }, 2600);
    }

    // ========================================================================
    //  POLLING
    // ========================================================================
    function pollStates() {
        getJSON('/Bots/States').then(function (d) {
            if (!d) return;
            $('kTracked').textContent = d.totalTracked || 0;
            $('kConnected').textContent = d.connected || 0;
            botStates = {};
            (d.bots || []).forEach(function (b) { botStates[b.guid] = b; });
            groupsByGuid = {};
            (d.groups || []).forEach(function (g) { groupsByGuid[g.guid] = g; });
            renderRoster();
            if (selectedGuid && botStates[selectedGuid]) renderFocusHeader();
        }).catch(function () { });
    }

    function pollBrain() {
        getJSON('/Bots/BrainStatus').then(function (d) {
            if (!d) return;
            brain.enabled = !!d.enabled;
            brain.activeBots = d.activeBots || 0;
            brain.groupingMode = d.groupingMode || 0;
            brain.groups = d.groups || [];
            $('kBrains').textContent = brain.activeBots;
            $('kGroups').textContent = brain.groups.length;
            var bb = $('btnBrain');
            bb.classList.toggle('on', brain.enabled);
            bb.classList.toggle('off', !brain.enabled);
            $('brainLbl').textContent = brain.enabled ? 'Brain ON' : 'Brain OFF';
            $('gmLbl').textContent = 'Grouping: ' + (GROUPING_MODES[brain.groupingMode] || '—');
        }).catch(function () { });
    }

    function pollFleet() {
        getJSON('/Bots/FleetDiagnostics').then(function (d) {
            if (!d || d.empty) { $('kErr').textContent = '0'; $('kErrWrap').className = 'kpi'; fleet.recent = []; if (feedMode === 'fleet') renderFeed(); return; }
            fleet.errorsPerMin = d.errorsPerMin || 0;
            fleet.recent = d.recent || [];
            $('kErr').textContent = fleet.errorsPerMin;
            $('kErrWrap').className = 'kpi ' + (fleet.errorsPerMin > 20 ? 'danger' : fleet.errorsPerMin > 5 ? 'warnish' : '');
            if (feedMode === 'fleet') renderFeed();
        }).catch(function () { });
    }

    function pollFocus() {
        if (!selectedGuid) return;
        getJSON('/Bots/LiveState/' + selectedGuid).then(function (d) {
            if (d && !d.error) { liveData = d; if (detailTab === 'live') renderTab(); }
        }).catch(function () { });
    }

    function pollLog() {
        if (!selectedGuid || feedMode !== 'bot') return;
        var st = botStates[selectedGuid]; if (!st) return;
        getJSON('/Bots/LiveLog?name=' + encodeURIComponent(st.name) + '&after=' + logSeq).then(function (d) {
            if (!d || !d.lines) return;
            if (d.lines.length) { logSeq = d.lastSeq; appendFeedItems(d.lines.map(function (l) { return { seq: l.seq, t: l.t, msg: l.msg, name: st.name, color: clsColor(st.classId) }; })); }
        }).catch(function () { });
    }

    function pollReport() {
        if (!selectedGuid || detailTab !== 'report') return;
        var st = botStates[selectedGuid]; if (!st) return;
        getJSON('/Bots/BotReport?name=' + encodeURIComponent(st.name)).then(function (d) {
            if (d) { liveData && (liveData._report = d); if (detailTab === 'report') renderTab(); }
        }).catch(function () { });
    }

    function pollGroupLive() {
        if (detailTab !== 'group' || !selectedGuid) return;
        getJSON('/Bots/LiveFleet').then(function (d) {
            if (d && d.bots) { window._liveFleet = d.bots; if (detailTab === 'group') renderTab(); }
        }).catch(function () { });
    }

    // ----- group helpers -----
    // Ordered member guids of the group the given bot belongs to (leader-first
    // when the brain reports membership; falls back to the per-guid group map).
    function groupMembersOf(guid) {
        var grp = groupsByGuid[guid]; if (!grp) return [];
        var def = brain.groups.filter(function (g) { return g.groupId === grp.groupId; })[0];
        var ids = def ? def.memberGuids.slice()
            : Object.keys(groupsByGuid).filter(function (g) { return groupsByGuid[g].groupId === grp.groupId; }).map(Number);
        return ids.filter(function (id) { return botStates[id]; });
    }
    function cycleGroup(dir) {
        var ids = groupMembersOf(selectedGuid);
        if (ids.length < 2) return;
        var i = ids.indexOf(Number(selectedGuid));
        var ni = ((i < 0 ? 0 : i) + dir + ids.length) % ids.length;
        selectBot(ids[ni]);
    }

    // Aggregate live feed for the focused bot's whole group. One LiveLog call per
    // member sharing a single cursor; merged, sorted by seq, fleet-noise stripped.
    function pollGroupFeed() {
        if (feedMode !== 'group' || !selectedGuid) return;
        var grp = groupsByGuid[selectedGuid];
        if (!grp) { setFeedMode('bot'); return; }            // focus lost its group
        if (grp.groupId !== groupFeedId) { groupFeedId = grp.groupId; groupFeedSeq = 0; $('feed').innerHTML = ''; }
        var ids = groupMembersOf(selectedGuid);
        if (!ids.length) return;
        var after = groupFeedSeq;
        Promise.all(ids.map(function (id) {
            var s = botStates[id];
            return getJSON('/Bots/LiveLog?name=' + encodeURIComponent(s.name) + '&after=' + after)
                .then(function (d) { return { s: s, d: d }; }).catch(function () { return null; });
        })).then(function (results) {
            var items = [], maxSeq = after;
            results.forEach(function (r) {
                if (!r || !r.d || !r.d.lines) return;
                if (r.d.lastSeq > maxSeq) maxSeq = r.d.lastSeq;
                r.d.lines.forEach(function (l) { items.push({ seq: l.seq, t: l.t, msg: l.msg, name: r.s.name, color: clsColor(r.s.classId) }); });
            });
            if (items.length) { items.sort(function (a, b) { return a.seq - b.seq; }); appendFeedItems(items); }
            if (maxSeq > groupFeedSeq) groupFeedSeq = maxSeq;
        });
    }

    // ========================================================================
    //  ROSTER
    // ========================================================================
    function renderRoster() {
        var box = $('roster');
        var guids = Object.keys(botStates).sort(function (a, b) {
            var A = botStates[a], B = botStates[b];
            return (B.level || 0) - (A.level || 0) || (A.name || '').localeCompare(B.name || '');
        });
        var f = rosterFilterText.toLowerCase();
        var shown = 0, frag = document.createDocumentFragment();
        guids.forEach(function (g) {
            var s = botStates[g];
            var cls = CLASS_NAMES[s.classId] || '?';
            if (f && (s.name || '').toLowerCase().indexOf(f) < 0 && cls.toLowerCase().indexOf(f) < 0) return;
            shown++;
            var dotCls = s.isDead ? 'dead' : s.inCombat ? 'combat' : 'alive';
            var grp = groupsByGuid[g];
            var grpHtml = grp ? '<span class="grp-badge ' + (grp.isGroupLeader ? 'leader' : '') + '">' + (grp.isGroupLeader ? '★' : '') + 'G' + grp.groupId + '</span>' : '';
            var row = el(
                '<div class="bot-row ' + (String(g) === String(selectedGuid) ? 'sel' : '') + '" data-guid="' + g + '">' +
                '<span class="bot-dot ' + dotCls + '"></span>' +
                '<div class="bot-main">' +
                '<div class="bot-name" style="color:' + clsColor(s.classId) + '">' + esc(s.name) + grpHtml + '</div>' +
                '<div class="bot-sub">L' + (s.level || 0) + ' ' + (RACE_NAMES[s.race] || '') + ' ' + cls + '</div>' +
                '</div>' +
                '<span class="bot-act">' + esc(shortAct(s.taskState)) + '</span>' +
                '</div>');
            row.addEventListener('click', function () { selectBot(g); });
            frag.appendChild(row);
        });
        box.innerHTML = ''; box.appendChild(frag);
        $('rosterCount').textContent = shown;
    }
    function shortAct(t) { if (!t) return '—'; t = String(t); return t.length > 10 ? t.slice(0, 10) : t; }

    // ========================================================================
    //  FOCUS — header + vitals + tabs
    // ========================================================================
    function selectBot(g) {
        selectedGuid = Number(g); liveData = null; logSeq = 0;
        $('focusEmpty').style.display = 'none';
        $('focusContent').style.display = 'flex';
        renderRoster(); renderFocusHeader();
        if (detailTab === 'live') $('fTabBody').innerHTML = '<div class="empty-note">Loading live state…</div>';
        else renderTab();
        // Feed follows only in single-bot mode; group/fleet feeds stay put so you
        // can cycle the right pane through members without losing the aggregate.
        if (feedMode === 'bot') { $('feed').innerHTML = ''; logSeq = 0; pollLog(); }
        refreshFeedTabs();
        pollFocus();
    }

    function renderFocusHeader() {
        var s = botStates[selectedGuid]; if (!s) return;
        var grp = groupsByGuid[selectedGuid];
        var cyc = '';
        if (grp) {
            var n = groupMembersOf(selectedGuid).length;
            cyc = '<span class="grp-cycle">' +
                '<button class="gcbtn" id="gPrev" title="Previous member">&lsaquo;</button>' +
                '<span class="gclabel" id="gFeedBtn" title="Show this group\'s aggregate feed">' + (grp.isGroupLeader ? '★' : '') + 'G' + grp.groupId + ' · ' + n + '</span>' +
                '<button class="gcbtn" id="gNext" title="Next member">&rsaquo;</button>' +
                '</span>';
        }
        $('fName').innerHTML = '<span style="color:' + clsColor(s.classId) + '">' + esc(s.name) + '</span>' +
            ' <span class="class-tag" style="color:' + clsColor(s.classId) + '">' + (CLASS_NAMES[s.classId] || '?') + '</span>' + cyc;
        $('fMeta').innerHTML = 'GUID ' + s.guid + ' · L' + (s.level || 0) + ' ' + (RACE_NAMES[s.race] || '') +
            (s.mapId != null ? ' · map ' + s.mapId : '') + (grp ? ' · group ' + grp.groupId + (grp.isGroupLeader ? ' (leader)' : '') : '');
        if (grp) {
            $('gPrev').onclick = function () { cycleGroup(-1); };
            $('gNext').onclick = function () { cycleGroup(1); };
            $('gFeedBtn').onclick = function () { setFeedMode('group'); };
        }
        renderVitals(s);
        refreshFeedTabs();
    }

    function renderVitals(s) {
        var hp = pct(s.health, s.maxHealth), mp = pct(s.mana, s.maxMana);
        var d = liveData || {};
        var dur = d.durability != null ? Math.round(d.durability) + '%' : '—';
        var html =
            vital('Health', hp + '%', hp, '#9ece6a') +
            (s.maxMana > 0 ? vital('Mana', mp + '%', mp, '#7aa2f7') : vital('Mana', '—', 0, '#7aa2f7')) +
            '<div class="vital"><div class="lbl">Gold</div><div class="val" style="font-size:12px">' + gold(s.copper) + '</div></div>' +
            '<div class="vital"><div class="lbl">Bags free</div><div class="val">' + (s.freeSlots != null ? s.freeSlots : '—') + '<span class="muted" style="font-size:11px">/' + (s.totalSlots || '—') + '</span></div></div>' +
            '<div class="vital"><div class="lbl">Durability</div><div class="val">' + dur + '</div></div>' +
            '<div class="vital"><div class="lbl">Status</div><div class="val" style="font-size:12px;color:' + (s.isDead ? 'var(--danger)' : s.inCombat ? 'var(--warn)' : 'var(--status-online)') + '">' +
            (s.isDead ? 'Dead' : s.inCombat ? 'In combat' : 'Alive') + '</div></div>';
        $('fVitals').innerHTML = html;
    }
    function vital(lbl, val, p, color) {
        return '<div class="vital"><div class="lbl">' + lbl + '</div><div class="val">' + val + '</div>' +
            '<div class="bar"><span style="width:' + p + '%;background:' + color + '"></span></div></div>';
    }

    // ----- tab router -----
    function renderTab() {
        if (!selectedGuid) return;
        if (detailTab === 'live') return renderLive();
        if (detailTab === 'report') return renderReport();
        if (detailTab === 'quests') return renderQuests();
        if (detailTab === 'bags') return renderBags();
        if (detailTab === 'group') return renderGroup();
    }

    function renderLive() {
        var d = liveData;
        var body = $('fTabBody');
        if (!d) { body.innerHTML = '<div class="empty-note">No live context — is the brain enabled for this bot?</div>'; return; }
        var gc = GOAL_COLOR[d.goal] || 'var(--accent)';
        var h = '';
        h += '<div class="spine-goal" style="color:' + gc + '">' + esc(d.goal || '—') + (d.step ? ' <small>/ ' + esc(d.step) + '</small>' : '') + '</div>';
        if (d.why) h += '<div class="spine-why">' + esc(d.why) + '</div>';

        // state chips
        h += '<div style="margin:10px 0 4px">';
        if (d.inCombat) h += '<span class="chip warn"><i class="fa-solid fa-gavel"></i> in combat' + (d.combat && d.combat.anchorGuid ? ' · lock ' + d.combat.anchorGuid : '') + '</span>';
        if (d.dead) h += '<span class="chip bad"><i class="fa-solid fa-skull"></i> dead</span>';
        if (d.pending && d.pending.isObjectiveGrind) h += '<span class="chip"><i class="fa-solid fa-bullseye"></i> objective grind</span>';
        if (d.distToTarget != null) h += '<span class="chip"><i class="fa-solid fa-location-arrow"></i> ' + Math.round(d.distToTarget) + 'y to target</span>';
        h += '</div>';

        // timers / progress
        h += '<div class="section-label">Timing</div>';
        h += kv('In goal', secs(d.timeInGoalSec));
        h += kv('In step', secs(d.timeInStepSec));
        if (d.lastKillSec != null) h += kv('Last kill', secs(d.lastKillSec) + ' ago');
        if (d.noProgressSec != null) h += kv('No progress', secs(d.noProgressSec));
        if (d.pending && d.pending.secsToDeadline != null) h += kv('Deadline in', secs(d.pending.secsToDeadline));

        // position
        if (d.pos) h += kv('Position', 'map ' + (d.mapId != null ? d.mapId : '?') + (d.zoneId ? ' · zone ' + d.zoneId : '') + '  (' + Math.round(d.pos.x) + ', ' + Math.round(d.pos.y) + ', ' + Math.round(d.pos.z) + ')');

        // failure / stall
        if (d.failure) {
            h += '<div class="section-label">Last failure</div>';
            h += '<div class="chip bad" style="display:block">' + esc(d.failure.cmd || '?') + ' → ' + esc(d.failure.reason || '?') +
                (d.failure.danger > 0 ? ' · danger ' + d.failure.danger : '') + ' <span class="muted">(' + secs(d.failure.ageSec) + ' ago)</span></div>';
        }
        if (d.stall) {
            h += '<div class="chip warn" style="display:block;margin-top:6px"><i class="fa-solid fa-hand"></i> stall: ' + esc(d.stall.reason || '?') + ' for ' + secs(d.stall.sinceSec) + '</div>';
        }

        // scratch (typed)
        if (d.scratch && Object.keys(d.scratch).length) {
            h += '<div class="section-label">Scratch</div>';
            Object.keys(d.scratch).forEach(function (k) { h += kv(k, esc(typeof d.scratch[k] === 'object' ? JSON.stringify(d.scratch[k]) : d.scratch[k])); });
        }
        body.innerHTML = h;
    }
    function kv(k, v) { return '<div class="kv"><span class="k">' + esc(k) + '</span><span class="v">' + v + '</span></div>'; }

    function renderReport() {
        var body = $('fTabBody');
        var r = liveData && liveData._report;
        if (!r) { body.innerHTML = '<div class="empty-note">Loading report…</div>'; return; }
        if (r.empty) { body.innerHTML = '<div class="empty-note">No buffered activity yet for this bot.</div>'; return; }
        var c = r.census || {};
        function cell(n, t, color) { return '<div class="census-cell"><div class="n" style="color:' + (color || 'var(--text-primary)') + '">' + (n || 0) + '</div><div class="t">' + t + '</div></div>'; }
        var h = '<div class="muted" style="font-size:11px;margin-bottom:8px">' + r.botLines + ' lines over ' + secs(r.spanSec) + '</div>';
        h += '<div class="census-grid">';
        h += cell(c.kills, 'kills', '#9ece6a') + cell(c.completions, 'quest credits', '#7aa2f7') +
            cell(c.levelUps, 'level-ups', '#e6cc80') + cell(c.deaths, 'deaths', '#f7768e') +
            cell(c.resurrects, 'resurrects', '#bb9af7') + cell(c.rewarded, 'rewarded', '#7aa2f7') +
            cell(c.noPath, 'no-path', '#ff9e64') + cell(c.stalls, 'stalls', '#ff9e64') +
            cell(c.repairs, 'repairs') + cell(c.sells, 'vendor sells');
        h += '</div>';
        if (r.health) {
            h += '<div class="section-label">Health</div>';
            h += kv('Kills vs credit', esc(r.health.killsVsCompletions));
            if (r.health.rezPerKill != null) h += kv('Rez / kill', r.health.rezPerKill);
            if (r.health.deathSpiral) h += '<div class="chip bad" style="display:block;margin-top:6px"><i class="fa-solid fa-skull-crossbones"></i> death-spiral risk</div>';
        }
        if (r.top && r.top.length) {
            h += '<div class="section-label">Top repeated lines</div>';
            r.top.slice(0, 8).forEach(function (t) { h += '<div class="kv"><span class="k" style="font-family:monospace;font-size:11px">' + esc(t.sig) + '</span><span class="v">' + t.n + '</span></div>'; });
        }
        body.innerHTML = h;
    }

    function renderQuests() {
        var body = $('fTabBody');
        body.innerHTML = '<div class="empty-note">Loading quests…</div>';
        getJSON('/Bots/QuestStatus?guid=' + selectedGuid).then(function (d) {
            if (!d || !d.quests || !d.quests.length) { body.innerHTML = '<div class="empty-note">No quest log entries.</div>'; return; }
            var h = '';
            d.quests.forEach(function (q) {
                var done = q.rewarded || q.status === 1;
                var objs = [];
                (q.mobRequired || []).forEach(function (req, i) { if (req > 0) objs.push((q.mobCounts[i] || 0) + '/' + req + ' mob'); });
                (q.itemRequired || []).forEach(function (req, i) { if (req > 0) objs.push((q.itemCounts[i] || 0) + '/' + req + ' item'); });
                h += '<div class="qrow"><div class="qt">' +
                    '<span class="q-state ' + (done ? 'done' : 'prog') + '">' + (done ? 'DONE' : 'WIP') + '</span> ' + esc(q.title) + '</div>' +
                    '<div class="qm">L' + q.questLevel + (objs.length ? ' · ' + objs.join(' · ') : '') + (q.turnInName ? ' · turn-in: ' + esc(q.turnInName) : '') + '</div></div>';
            });
            body.innerHTML = h;
        }).catch(function () { body.innerHTML = '<div class="empty-note">Failed to load quests.</div>'; });
    }

    function renderBags() {
        var body = $('fTabBody');
        body.innerHTML = '<div class="empty-note">Loading bags…</div>';
        getJSON('/Bots/Inventory?guid=' + selectedGuid).then(function (d) {
            if (!d || d.error) { body.innerHTML = '<div class="empty-note">' + esc((d && d.error) || 'No inventory.') + '</div>'; return; }
            var h = '<div class="census-grid">';
            h += '<div class="census-cell"><div class="n">' + (d.totalItems || 0) + '</div><div class="t">items</div></div>';
            h += '<div class="census-cell"><div class="n">' + (d.freeSlots || 0) + '</div><div class="t">free slots</div></div>';
            h += '</div>';
            h += '<div class="section-label">Equipped</div>';
            (d.equipped || []).slice(0, 20).forEach(function (it) {
                h += '<div class="kv"><span class="k" style="color:' + (QUALITY_COLORS[it.quality] || '#fff') + '">' + esc(it.name) + '</span><span class="v">iLvl ' + (it.itemLevel || 0) + '</span></div>';
            });
            if ((d.backpack || []).length) {
                h += '<div class="section-label">Backpack</div>';
                d.backpack.slice(0, 30).forEach(function (it) {
                    h += '<div class="kv"><span class="k" style="color:' + (QUALITY_COLORS[it.quality] || '#fff') + '">' + esc(it.name) + (it.stackCount > 1 ? ' ×' + it.stackCount : '') + '</span><span class="v"></span></div>';
                });
            }
            body.innerHTML = h;
        }).catch(function () { body.innerHTML = '<div class="empty-note">Failed to load inventory.</div>'; });
    }

    function renderGroup() {
        var body = $('fTabBody');
        var grp = groupsByGuid[selectedGuid];
        if (!grp) { body.innerHTML = '<div class="empty-note">This bot isn\'t in a group.<br><span class="muted">Use Auto-Form, or set a grouping mode above.</span></div>'; return; }
        var def = brain.groups.filter(function (g) { return g.groupId === grp.groupId; })[0];
        var memberGuids = def ? def.memberGuids : Object.keys(groupsByGuid).filter(function (g) { return groupsByGuid[g].groupId === grp.groupId; }).map(Number);
        var live = window._liveFleet || [];
        var liveByGuid = {}; live.forEach(function (b) { liveByGuid[b.guid] = b; });

        var alive = 0, combat = 0, lvlSum = 0, n = 0;
        var h = '<div class="muted" style="font-size:11px;margin-bottom:8px">Group ' + grp.groupId + ' · ' + memberGuids.length + ' members' + (def ? ' · leader ' + def.leaderGuid : '') + '</div>';
        memberGuids.forEach(function (mg) {
            var s = botStates[mg]; if (!s) return; n++;
            lvlSum += s.level || 0; if (!s.isDead) alive++; if (s.inCombat) combat++;
            var lf = liveByGuid[mg] || {};
            var hp = pct(s.health, s.maxHealth);
            var isLeader = def && def.leaderGuid === mg;
            h += '<div class="gmember" data-guid="' + mg + '">' +
                '<span class="bot-dot ' + (s.isDead ? 'dead' : s.inCombat ? 'combat' : 'alive') + '"></span>' +
                '<span class="mn" style="color:' + clsColor(s.classId) + '">' + (isLeader ? '★ ' : '') + esc(s.name) + '</span>' +
                '<span class="muted" style="font-size:10px">' + esc(lf.goal || shortAct(s.taskState)) + '</span>' +
                '<span class="gmini-bar"><span style="width:' + hp + '%;background:' + (hp < 35 ? '#f7768e' : hp < 70 ? '#e0af68' : '#9ece6a') + '"></span></span>' +
                '</div>';
        });
        var agg = '<div class="census-grid" style="margin-top:10px">' +
            '<div class="census-cell"><div class="n">' + n + '</div><div class="t">members</div></div>' +
            '<div class="census-cell"><div class="n">' + alive + '</div><div class="t">alive</div></div>' +
            '<div class="census-cell"><div class="n" style="color:' + (combat ? 'var(--warn)' : 'inherit') + '">' + combat + '</div><div class="t">in combat</div></div>' +
            '<div class="census-cell"><div class="n">' + (n ? Math.round(lvlSum / n) : 0) + '</div><div class="t">avg level</div></div>' +
            '</div>';
        h += agg;
        h += '<button class="btn off" id="btnDisband" style="margin-top:12px;width:100%;justify-content:center"><i class="fa-solid fa-link-slash"></i> Disband group</button>';
        body.innerHTML = h;
        body.querySelectorAll('.gmember').forEach(function (m) { m.addEventListener('click', function () { selectBot(Number(m.dataset.guid)); }); });
        var db = $('btnDisband');
        if (db) db.addEventListener('click', function () {
            postJSON('/Bots/DisbandGroup', { groupId: grp.groupId }).then(function (result) {
                if (result && result.success) {
                    toast('Group ' + grp.groupId + ' disbanded', 'ok');
                } else {
                    toast('Disband failed: ' + ((result && (result.error || result.status)) || 'unknown') +
                        (result && result.cbt ? ' [cbt=' + result.cbt + ']' : ''), 'err');
                }
                pollBrain(); pollStates();
            });
        });
    }

    // ========================================================================
    //  FEED (bottom)
    // ========================================================================
    // The fleet heartbeat ("FLEET 19 bots …") names every bot, so it lands in
    // every single-bot LiveLog slice. Mirror the server's IsFleet test and drop it
    // so a focused feed shows only that bot's own activity.
    function isFleetLine(msg) {
        if (!msg) return false;
        if (/\bFLEET\b/i.test(msg)) return true;
        return (msg.match(/pick=/g) || []).length >= 2;
    }
    function feedRow(it) {
        var cls = /error|fail|no_path|unsafe|stall|wedge/i.test(it.msg) ? 'err'
            : /death|died|giveup|shelv|defer/i.test(it.msg) ? 'warn' : '';
        return el('<div class="ln ' + cls + '"><span class="ts">' + fmtT(it.t) + '</span>' +
            '<span class="nm" style="color:' + (it.color || 'var(--accent)') + '">' + esc(it.name) + '</span>' +
            '<span class="mg">' + esc(it.msg) + '</span></div>');
    }
    function appendFeedItems(items) {
        var box = $('feed');
        items.forEach(function (it) { if (isFleetLine(it.msg)) return; box.appendChild(feedRow(it)); });
        while (box.childNodes.length > 400) box.removeChild(box.firstChild);
        box.scrollTop = box.scrollHeight;
    }
    function renderFeed() {
        var box = $('feed');
        if (feedMode === 'fleet') {
            box.innerHTML = '';
            fleet.recent.slice().reverse().forEach(function (i) {
                if (isFleetLine(i.msg)) return;
                var cls = i.tier === 'error' ? 'err' : i.tier === 'warn' ? 'warn' : '';
                box.appendChild(el('<div class="ln ' + cls + '"><span class="ts">' + fmtT(i.t) + '</span><span class="nm">' + esc(i.name) + '</span>' +
                    '<span class="mg" style="color:' + (i.color || '') + '">[' + esc(i.label) + '] ' + esc(i.msg) + '</span></div>'));
            });
            box.scrollTop = box.scrollHeight;
        } else if (feedMode === 'group') {
            box.innerHTML = ''; groupFeedId = -1; groupFeedSeq = 0; pollGroupFeed();
        } else {
            box.innerHTML = ''; logSeq = 0; pollLog();
        }
    }
    function setFeedMode(mode) {
        if (mode === 'group' && !(selectedGuid && groupsByGuid[selectedGuid])) { toast('Focus a grouped bot first', 'err'); return; }
        feedMode = mode;
        document.querySelectorAll('.ft').forEach(function (x) { x.classList.toggle('active', x.dataset.feed === mode); });
        renderFeed();
    }
    // Group feed tab is only meaningful when the focused bot is grouped.
    function refreshFeedTabs() {
        var grouped = !!(selectedGuid && groupsByGuid[selectedGuid]);
        var gt = document.querySelector('.ft[data-feed="group"]');
        if (!gt) return;
        gt.style.opacity = grouped ? '' : '0.35';
        gt.style.pointerEvents = grouped ? '' : 'none';
        if (!grouped && feedMode === 'group') setFeedMode('bot');
    }
    function fmtT(t) { try { var d = new Date(t); return ('0' + d.getHours()).slice(-2) + ':' + ('0' + d.getMinutes()).slice(-2) + ':' + ('0' + d.getSeconds()).slice(-2); } catch (e) { return ''; } }

    // ========================================================================
    //  MODALS + client-visibility coordination
    // ========================================================================
    function openModal(id) {
        var m = $(id);
        if (id === 'mapModal') { var fr = $('mapFrame'); if (!fr.src) fr.src = fr.dataset.src; }
        m.classList.add('open');
        openModals++; if (openModals === 1) setClientVisible(false);   // hide native client so the DOM modal shows
    }
    function closeModal(id) {
        $(id).classList.remove('open');
        openModals = Math.max(0, openModals - 1);
        if (openModals === 0) { setClientVisible(true); requestAnimationFrame(postHoleRect); }
    }
    document.querySelectorAll('[data-close]').forEach(function (b) { b.addEventListener('click', function () { closeModal(b.dataset.close); }); });
    document.querySelectorAll('.modal-scrim').forEach(function (sc) { sc.addEventListener('click', function (e) { if (e.target === sc) closeModal(sc.id); }); });

    // ========================================================================
    //  MAXIMIZE (hole fills window)
    // ========================================================================
    function toggleMax() {
        var maxed = $('root').classList.toggle('client-max');
        $('btnMax').innerHTML = maxed ? '<i class="fa-solid fa-compress"></i>' : '<i class="fa-solid fa-expand"></i>';
        // re-measure across the layout transition
        [0, 60, 160, 320].forEach(function (ms) { setTimeout(postHoleRect, ms); });
    }

    // ========================================================================
    //  CONTROLS WIRING
    // ========================================================================
    function groupMutationOutcomeText(outcome) {
        var members = (outcome.memberGuids || []).join(',');
        return (outcome.operation || 'group') + ' leader=' + (outcome.leaderGuid || '?') +
            ' members=[' + members + '] → ' + (outcome.status || 'unknown') +
            ': ' + (outcome.detail || 'no detail') + (outcome.cbt ? ' [cbt=' + outcome.cbt + ']' : '');
    }

    function offerUnknownFormReconciliation(outcome) {
        if (!outcome || outcome.operation !== 'form' || outcome.status !== 'outcome_unknown') return;
        var members = outcome.memberGuids || [];
        var followers = members.filter(function (guid) { return guid !== outcome.leaderGuid; });
        if (!outcome.leaderGuid || !followers.length) return;
        if (!confirm(groupMutationOutcomeText(outcome) + '\n\nRe-send this exact formation with a fresh cbt to reconcile?')) return;

        postJSON('/Bots/FormGroup', { leaderGuid: outcome.leaderGuid, followerGuids: followers })
            .then(function (result) {
                if (result && result.success) {
                    toast('Group formation reconciled [cbt=' + result.cbt + ']', 'ok');
                } else {
                    toast('Reconciliation failed: ' + ((result && (result.error || result.status)) || 'unknown') +
                        (result && result.cbt ? ' [cbt=' + result.cbt + ']' : ''), 'err');
                }
                pollBrain(); pollStates();
            })
            .catch(function () { toast('Reconciliation request failed', 'err'); });
    }

    function wire() {
        $('rosterFilter').addEventListener('input', function (e) { rosterFilterText = e.target.value; renderRoster(); });

        document.querySelectorAll('.tab').forEach(function (t) {
            t.addEventListener('click', function () {
                detailTab = t.dataset.tab;
                document.querySelectorAll('.tab').forEach(function (x) { x.classList.toggle('active', x === t); });
                renderTab();
                if (detailTab === 'report') pollReport();
                if (detailTab === 'group') pollGroupLive();
            });
        });

        document.querySelectorAll('.ft').forEach(function (t) {
            t.addEventListener('click', function () { setFeedMode(t.dataset.feed); });
        });

        $('btnBrain').addEventListener('click', function () {
            var next = !brain.enabled;
            postJSON('/Bots/ToggleBrain?enabled=' + next).then(function () { toast('Brain ' + (next ? 'enabled' : 'disabled'), next ? 'ok' : ''); pollBrain(); });
        });

        $('btnGroupMode').addEventListener('click', function () {
            var next = (brain.groupingMode + 1) % 3;
            postJSON('/Bots/SetGroupingMode', { mode: next }).then(function (result) {
                if (result && result.success) {
                    toast('Grouping: ' + (result.modeName || GROUPING_MODES[next]), 'ok');
                } else {
                    var detail = ((result && result.outcomes) || []).filter(function (outcome) { return !outcome.success; })
                        .map(groupMutationOutcomeText).join(' | ');
                    toast('Grouping mode unchanged: ' + (detail || (result && result.error) || 'unknown'), 'err');
                }
                pollBrain(); pollStates();
            });
        });

        $('btnAutoForm').addEventListener('click', function () {
            postJSON('/Bots/AutoFormGroups').then(function (d) {
                var outcomes = (d && d.outcomes) || [];
                var failures = outcomes.filter(function (outcome) { return !outcome.success; });
                var detail = failures.map(groupMutationOutcomeText).join(' | ');
                toast(((d && d.groupsFormed) || 0) + ' formed, ' + failures.length + ' unresolved' +
                    (detail ? ': ' + detail : ''), failures.length ? 'err' : 'ok');
                failures.forEach(function (outcome) {
                    offerUnknownFormReconciliation(outcome);
                });
                // Partial success still changes topology; refresh on every response.
                pollBrain(); pollStates();
            });
        });

        $('btnMap').addEventListener('click', function () { openModal('mapModal'); });
        $('btnAdd').addEventListener('click', function () { buildClassPicker(); openModal('addModal'); });
        $('btnMax').addEventListener('click', toggleMax);

        $('btnAddGo').addEventListener('click', function () {
            var classes = [];
            document.querySelectorAll('#classPick input').forEach(function (inp) {
                var n = parseInt(inp.value, 10) || 0;
                for (var i = 0; i < n; i++) classes.push(inp.dataset.cls);
            });
            if (!classes.length) { toast('Pick at least one', 'err'); return; }
            if (classes.length > 50) { toast('Max 50 per batch', 'err'); return; }
            postJSON('/Bots/AddBots', { classes: classes }).then(function (d) {
                if (d && d.success) { toast('Spawned ' + d.sent + ' bot(s)', 'ok'); closeModal('addModal'); setTimeout(pollStates, 1500); }
                else toast((d && d.error) || 'Add failed', 'err');
            });
        });

        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape') { ['mapModal', 'addModal'].forEach(function (id) { if ($(id).classList.contains('open')) closeModal(id); }); }
        });
    }

    function buildClassPicker() {
        var classes = ['warrior', 'paladin', 'hunter', 'rogue', 'priest', 'mage', 'warlock', 'druid'];
        $('classPick').innerHTML = classes.map(function (c) {
            return '<div class="cp"><label style="text-transform:capitalize">' + c + '</label>' +
                '<input type="number" min="0" max="20" value="0" data-cls="' + c + '"></div>';
        }).join('');
    }

    // ========================================================================
    //  BOOT
    // ========================================================================
    function boot() {
        wire();
        startRectTracking();
        pollStates(); pollBrain(); pollFleet();
        setInterval(pollStates, 2000);
        setInterval(pollBrain, 3000);
        setInterval(pollFleet, 6000);
        setInterval(pollFocus, 1000);
        setInterval(pollLog, 2000);
        setInterval(pollGroupFeed, 2000);
        setInterval(pollReport, 6000);
        setInterval(pollGroupLive, 2500);
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot);
    else boot();
})();
