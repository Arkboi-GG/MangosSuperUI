// MangosSuperUI — Fleet View JS
// Fleet-wide error triage board. Pull-only: polls /Bots/FleetDiagnostics (the
// correlated incident payload) plus /Bots/States (class colours), joins them
// client-side, and renders KPIs, category bars, hotspots, a bot leaderboard, and
// a compact expandable incident feed. The "Quantized Report" modal builds a
// paste-ready markdown digest from the same payload.

$(function () {

    var CLASS_NAMES = {
        1: 'Warrior', 2: 'Paladin', 3: 'Hunter', 4: 'Rogue',
        5: 'Priest', 7: 'Shaman', 8: 'Mage', 9: 'Warlock', 11: 'Druid'
    };
    var CLASS_CSS = {
        1: 'class-warrior', 2: 'class-paladin', 3: 'class-hunter', 4: 'class-rogue',
        5: 'class-priest', 7: 'class-shaman', 8: 'class-mage', 9: 'class-warlock', 11: 'class-druid'
    };
    var MAP_NAMES = { 0: 'E. Kingdoms', 1: 'Kalimdor', 30: 'Alterac', 489: 'Warsong', 529: 'Arathi' };

    // ---- state ----
    var data = null;            // last FleetDiagnostics payload
    var classOf = {};           // guid -> classId (from States)
    var timer = null;
    var intervalMs = 4000;
    var paused = false;
    var hiddenCats = {};        // catKey -> true means hidden from feed
    var botFilter = '';         // lowercased substring on bot name
    var botFilterDebounce = null;
    var openSeqs = {};          // seq -> true: which incident rows are expanded (sticky across polls)
    var expandAll = false;       // applies to the popped-out feed only
    var feedPopped = false;      // is the pop-out overlay open
    var journaldDigest = '';    // in-service journald digest folded into the report

    // ===================== POLLING =====================
    function start() {
        stop();
        tick();
        if (!paused) timer = setInterval(tick, intervalMs);
        $('#fvLiveDot').toggleClass('paused', paused);
    }
    function stop() { if (timer) { clearInterval(timer); timer = null; } }
    function tick() { fetchStates(); fetchDiag(); }

    function setBridge(ok, text) {
        $('#fvBridge').removeClass('online offline').addClass(ok ? 'online' : 'offline');
        $('#fvBridgeText').text(text);
    }

    function fetchStates() {
        $.getJSON('/Bots/States', function (d) {
            classOf = {};
            var bots = (d && d.bots) || [];
            for (var i = 0; i < bots.length; i++) classOf[bots[i].guid] = bots[i].classId;
            var conn = (d && d.connected) || 0;
            var tracked = (d && d.totalTracked) || bots.length;
            setBridge(conn > 0, conn + ' connected · ' + tracked + ' tracked');
        }).fail(function () { setBridge(false, 'Bridge offline'); });
    }

    function fetchDiag() {
        $.getJSON('/Bots/FleetDiagnostics', function (d) {
            data = d;
            $('#fvUpdated').text('updated ' + nowClock());
            render();
        }).fail(function () { $('#fvUpdated').text('request failed'); });
    }

    // ===================== RENDER =====================
    function render() {
        if (!data || data.empty) { renderEmpty(data && data.reason); return; }
        $('#fvWindowMeta').html('<b>' + (data.attributedLines || 0) + '</b> lines · <b>' +
            fmtDur(data.windowSec) + '</b> window · ' + (data.fleetLines || 0) + ' heartbeats folded');
        renderKpis();
        renderChips();
        renderCatBars();
        renderPreBars();
        renderHotspots();
        renderLeaderboard();
        renderFeed();
        $('#fvClearFilters').toggle(hasFilters());
    }

    function renderEmpty(reason) {
        $('#fvWindowMeta').text('—');
        $('#fvFeed').html('<div class="fv-empty"><i class="fa-solid fa-satellite-dish"></i>' +
            '<div class="fv-empty-title">No telemetry yet</div>' +
            '<div class="fv-empty-sub">' + esc(reason || 'Waiting for bot activity in the log ring.') + '</div></div>');
        ['#kpiErrors', '#kpiRate', '#kpiBots'].forEach(function (id) { $(id).text('0'); });
        ['#kpiTopCat', '#kpiHotspot', '#kpiWorstBot'].forEach(function (id) { $(id).text('—'); });
        $('#kpiErrorsSub,#kpiRateSub,#kpiBotsSub,#kpiTopCatSub,#kpiHotspotSub,#kpiWorstBotSub').html('&nbsp;');
        $('#fvChips').empty();
        $('#fvCatBars,#fvPreBars').html('<div style="color:var(--text-muted);font-size:12px;">—</div>');
        $('#fvHotspots tbody, #fvLeaderboard tbody').html('<tr><td style="color:var(--text-muted);">—</td></tr>');
        $('#fvFeedCount').text('0 shown');
        $('.fv-kpi').removeClass('bad warn');
    }

    function renderKpis() {
        var err = data.errorTotal || 0, info = data.infoTotal || 0;
        $('#kpiErrors').text(err);
        $('#kpiErrorsSub').text(info > 0 ? ('+' + info + ' low-priority') : '\u00a0');
        $('.fv-kpi').eq(0).removeClass('bad warn').addClass(err > 0 ? 'bad' : '');

        var rate = data.errorsPerMin != null ? data.errorsPerMin : 0;
        $('#kpiRate').text(rate);
        $('#kpiRateSub').text('faults / min');
        $('.fv-kpi').eq(1).removeClass('bad warn').addClass(rate >= 10 ? 'bad' : (rate >= 3 ? 'warn' : ''));

        var botsAffected = (data.byBot || []).filter(function (b) { return b.count > 0; }).length;
        $('#kpiBots').text(botsAffected);
        $('#kpiBotsSub').text('of ' + (data.botCount || 0) + ' active');

        var topCat = (data.byCategory || []).filter(function (c) { return c.tier !== 'info'; })[0];
        if (topCat) { $('#kpiTopCat').text(topCat.label).css('color', topCat.color); $('#kpiTopCatSub').text(topCat.count + ' occurrences'); }
        else { $('#kpiTopCat').text('—').css('color', ''); $('#kpiTopCatSub').html('&nbsp;'); }

        var hot = (data.hotspots || [])[0];
        if (hot) { $('#kpiHotspot').html('<span class="fv-coord">' + hot.x + ', ' + hot.y + '</span>'); $('#kpiHotspotSub').text(mapName(hot.map) + ' · ' + hot.count + ' faults'); }
        else { $('#kpiHotspot').text('—'); $('#kpiHotspotSub').html('&nbsp;'); }

        var worst = (data.byBot || [])[0];
        if (worst && worst.count > 0) { $('#kpiWorstBot').text(worst.name).css('color', ''); $('#kpiWorstBotSub').text('L' + worst.level + ' · ' + worst.count + ' faults'); }
        else { $('#kpiWorstBot').text('—'); $('#kpiWorstBotSub').html('&nbsp;'); }
    }

    function renderChips() {
        var cats = data.byCategory || [], html = '';
        for (var i = 0; i < cats.length; i++) {
            var c = cats[i], off = !!hiddenCats[c.key];
            html += '<span class="fv-chip' + (off ? ' off' : '') + '" data-cat="' + esc(c.key) + '" style="color:' + c.color + ';">' +
                '<span class="fv-chip-dot" style="background:' + c.color + ';"></span>' +
                '<span style="color:var(--text-secondary);">' + esc(c.label) + '</span>' +
                '<span class="fv-chip-n">' + c.count + '</span></span>';
        }
        $('#fvChips').html(html);
    }

    function renderCatBars() {
        var cats = data.byCategory || [];
        if (!cats.length) { $('#fvCatBars').html('<div style="color:var(--text-muted);font-size:12px;">No faults in window</div>'); return; }
        var max = cats[0].count || 1, html = '';
        for (var i = 0; i < cats.length; i++) {
            var c = cats[i], pct = Math.max(4, Math.round((c.count / max) * 100));
            html += '<div class="fv-bar-row"><span class="fv-bar-label" title="' + esc(c.label) + '">' + esc(c.label) + '</span>' +
                '<span class="fv-bar-track"><span class="fv-bar-fill" style="width:' + pct + '%;background:' + c.color + ';"></span></span>' +
                '<span class="fv-bar-val">' + c.count + '</span></div>';
        }
        $('#fvCatBars').html(html);
    }

    function renderPreBars() {
        var pre = data.byPreceding || [];
        if (!pre.length) { $('#fvPreBars').html('<div style="color:var(--text-muted);font-size:12px;">No context captured</div>'); return; }
        var max = pre[0].count || 1, html = '';
        for (var i = 0; i < pre.length; i++) {
            var p = pre[i], pct = Math.max(4, Math.round((p.count / max) * 100));
            html += '<div class="fv-bar-row"><span class="fv-bar-label" title="' + esc(p.label) + '">' + esc(p.label) + '</span>' +
                '<span class="fv-bar-track"><span class="fv-bar-fill" style="width:' + pct + '%;background:var(--accent);"></span></span>' +
                '<span class="fv-bar-val">' + p.count + '</span></div>';
        }
        $('#fvPreBars').html(html);
    }

    function renderHotspots() {
        var hs = data.hotspots || [];
        if (!hs.length) { $('#fvHotspots tbody').html('<tr><td style="color:var(--text-muted);">No positioned faults in window</td></tr>'); return; }
        var head = '<thead><tr><th>Where</th><th>Map</th><th>Top fault</th><th>Bots</th><th style="text-align:right;">Faults</th></tr></thead>', rows = '';
        for (var i = 0; i < hs.length; i++) {
            var h = hs[i];
            rows += '<tr class="fv-clickable" data-cat="' + esc(h.topCategory) + '">' +
                '<td><span class="fv-coord">' + h.x + ', ' + h.y + '</span></td>' +
                '<td>' + esc(mapName(h.map)) + '</td>' +
                '<td><span style="color:' + h.color + ';font-weight:600;">' + esc(h.topCategory) + '</span></td>' +
                '<td style="color:var(--text-muted);">' + esc((h.bots || []).join(', ')) + '</td>' +
                '<td style="text-align:right;"><span class="fv-num">' + h.count + '</span></td></tr>';
        }
        $('#fvHotspots').html(head + '<tbody>' + rows + '</tbody>');
    }

    function renderLeaderboard() {
        var bots = (data.byBot || []).filter(function (b) { return b.count > 0 || b.infoCount > 0; });
        if (!bots.length) { $('#fvLeaderboard tbody').html('<tr><td style="color:var(--text-muted);">No faults attributed</td></tr>'); return; }
        var head = '<thead><tr><th>Bot</th><th>Mix</th><th style="text-align:right;">Faults</th></tr></thead>', rows = '';
        for (var i = 0; i < bots.length; i++) {
            var b = bots[i], cls = classOf[b.guid], clsName = CLASS_NAMES[cls] || '';
            var mix = '<span class="fv-mini-cats">';
            for (var j = 0; j < (b.top || []).length; j++) mix += '<span class="fv-mini-cat" title="' + esc(b.top[j].key) + ' ×' + b.top[j].count + '" style="background:' + b.top[j].color + ';"></span>';
            mix += '</span>';
            rows += '<tr class="fv-clickable" data-bot="' + esc(b.name) + '">' +
                '<td><span style="color:var(--text-primary);font-weight:600;">' + esc(b.name) + '</span>' +
                (clsName ? '<span class="bt-class-badge ' + (CLASS_CSS[cls] || '') + '" style="font-size:9px;margin-left:6px;padding:1px 5px;border-radius:3px;">' + clsName + '</span>' : '') +
                '<span class="fv-lvlpill">L' + b.level + '</span></td>' +
                '<td>' + mix + '</td>' +
                '<td style="text-align:right;"><span class="fv-num">' + b.count + '</span>' +
                (b.infoCount ? '<span style="color:var(--text-muted);font-size:10px;"> +' + b.infoCount + '</span>' : '') + '</td></tr>';
        }
        $('#fvLeaderboard').html(head + '<tbody>' + rows + '</tbody>');
    }

    function renderFeed() {
        var rows = (data.recent || []).filter(passesFilter);
        var allN = (data.recent || []).length;
        $('#fvFeedCount').text(rows.length + ' shown' + (rows.length !== allN ? ' of ' + allN : ''));
        if (!rows.length) {
            $('#fvFeed').html('<div class="fv-empty" style="padding:24px 20px;"><i class="fa-solid fa-circle-check"></i>' +
                '<div class="fv-empty-title">Nothing matches</div>' +
                '<div class="fv-empty-sub">' + (hasFilters() ? 'No incidents match the active filters.' : 'No incidents in the current window.') + '</div></div>');
            if (feedPopped) renderBigFeed(rows);
            return;
        }
        var html = '';
        for (var i = 0; i < rows.length; i++) html += incidentHtml(rows[i], false);
        $('#fvFeed').html(html);
        if (feedPopped) renderBigFeed(rows);
    }

    function renderBigFeed(rows) {
        rows = rows || (data.recent || []).filter(passesFilter);
        $('#fvBigCount').text(rows.length + ' shown');
        if (!rows.length) {
            $('#fvFeedBig').html('<div class="fv-empty"><i class="fa-solid fa-circle-check"></i>' +
                '<div class="fv-empty-title">Nothing matches</div></div>');
            return;
        }
        var html = '';
        for (var i = 0; i < rows.length; i++) html += incidentHtml(rows[i], true);
        $('#fvFeedBig').html(html);
    }

    // Compact single-line row; click to expand a detail drawer.
    function incidentHtml(r, allowAll) {
        var open = (allowAll && expandAll) || !!openSeqs[r.seq];
        var hints = '';
        if (r.hasPos) hints += '<i class="fa-solid fa-location-dot" title="has position"></i>';
        if (r.target) hints += '<i class="fa-solid fa-bullseye" title="has target"></i>';

        var ctx = [];
        if (r.hasPos) {
            ctx.push('<span><b>where</b> <span class="fv-ctx-pos" data-x="' + r.x + '" data-y="' + r.y + '" title="Copy coords">' +
                r.x + ', ' + r.y + (r.map >= 0 ? ' (' + esc(mapName(r.map)) + ')' : '') + '</span></span>');
        }
        var preceded = r.why ? ('why=' + r.why) : (r.preCmd || '');
        if (preceded) ctx.push('<span><b>after</b> ' + esc(preceded) + '</span>');
        if (r.target) ctx.push('<span><b>target</b> ' + esc(r.target) + '</span>');
        ctx.push('<span><b>seq</b> ' + r.seq + '</span>');

        return '<div class="fv-inc' + (open ? ' open' : '') + '" data-seq="' + r.seq + '">' +
            '<div class="fv-inc-row" style="border-left-color:' + r.color + ';">' +
            '<i class="fa-solid fa-chevron-right fv-inc-chev"></i>' +
            '<span class="fv-inc-time">' + clock(r.t) + '</span>' +
            '<span class="fv-inc-cat" style="color:' + r.color + ';background:' + hexA(r.color, 0.14) + ';">' + esc(r.label) + '</span>' +
            '<span class="fv-inc-bot">' + esc(r.name) + '<span class="lvl">L' + r.level + '</span></span>' +
            '<span class="fv-inc-msg">' + esc(r.msg) + '</span>' +
            '<span class="fv-inc-hint">' + hints + '</span>' +
            '</div>' +
            '<div class="fv-inc-body">' +
            '<div class="fv-inc-full">' + esc(r.msg) + '</div>' +
            '<div class="fv-inc-ctx">' + ctx.join('') + '</div>' +
            '</div></div>';
    }

    function passesFilter(r) {
        if (hiddenCats[r.category]) return false;
        if (botFilter && (r.name || '').toLowerCase().indexOf(botFilter) === -1) return false;
        return true;
    }
    function hasFilters() {
        if (botFilter) return true;
        for (var k in hiddenCats) if (hiddenCats[k]) return true;
        return false;
    }

    // ===================== EVENTS =====================
    $('#fvPause').on('click', function () {
        paused = !paused;
        $(this).html(paused ? '<i class="fa-solid fa-play"></i> Resume' : '<i class="fa-solid fa-pause"></i> Pause');
        if (paused) { stop(); $('#fvLiveDot').addClass('paused'); } else { start(); }
    });
    $('#fvRefresh').on('click', tick);
    $('#fvInterval').on('change', function () { intervalMs = parseInt(this.value, 10) || 4000; if (!paused) start(); });

    $('#fvChips').on('click', '.fv-chip', function () {
        var k = $(this).data('cat'); hiddenCats[k] = !hiddenCats[k];
        renderChips(); renderFeed(); $('#fvClearFilters').toggle(hasFilters());
    });

    $('#fvBotFilter').on('input', function () {
        var v = this.value; clearTimeout(botFilterDebounce);
        botFilterDebounce = setTimeout(function () {
            botFilter = (v || '').trim().toLowerCase();
            $('#fvBigFilter').val(v);
            renderFeed(); $('#fvClearFilters').toggle(hasFilters());
        }, 180);
    });

    $('#fvClearFilters').on('click', function () {
        hiddenCats = {}; botFilter = ''; $('#fvBotFilter').val(''); $('#fvBigFilter').val('');
        renderChips(); renderFeed(); $(this).hide();
    });

    // pop the feed out into a roomy overlay (keeps the inline feed compact)
    $('#fvPopOut').on('click', function (e) {
        e.preventDefault();
        feedPopped = true;
        $('#fvBigFilter').val(botFilter);
        $('#fvFeedOverlay').addClass('active');
        renderBigFeed();
    });
    function closeFeedOverlay() { feedPopped = false; $('#fvFeedOverlay').removeClass('active'); }
    $('#fvFeedClose').on('click', closeFeedOverlay);
    $('#fvFeedOverlay').on('click', function (e) { if (e.target === this) closeFeedOverlay(); });

    $('#fvBigExpand').on('click', function (e) {
        e.preventDefault();
        expandAll = !expandAll;
        if (!expandAll) openSeqs = {};
        $(this).text(expandAll ? 'collapse all' : 'expand all');
        renderBigFeed();
    });

    $('#fvBigFilter').on('input', function () {
        var v = this.value; clearTimeout(botFilterDebounce);
        botFilterDebounce = setTimeout(function () {
            botFilter = (v || '').trim().toLowerCase();
            $('#fvBotFilter').val(v);
            renderFeed(); $('#fvClearFilters').toggle(hasFilters());
        }, 180);
    });

    // expand / collapse a single incident — works in both the inline and popped feed
    $('#fvFeed, #fvFeedBig').on('click', '.fv-inc-row', function () {
        var $inc = $(this).closest('.fv-inc');
        var seq = $inc.data('seq');
        var nowOpen = !$inc.hasClass('open');
        $inc.toggleClass('open', nowOpen);
        if (nowOpen) openSeqs[seq] = true; else delete openSeqs[seq];
    });

    // copy coords on position click (don't toggle the row)
    $('#fvFeed, #fvFeedBig').on('click', '.fv-ctx-pos', function (e) {
        e.stopPropagation();
        copyText($(this).data('x') + ' ' + $(this).data('y'));
        var $el = $(this), orig = $el.html();
        $el.html('copied'); setTimeout(function () { $el.html(orig); }, 900);
    });

    // leaderboard row -> filter feed to that bot
    $('#fvLeaderboard').on('click', 'tr.fv-clickable', function () {
        var name = $(this).data('bot'); if (!name) return;
        botFilter = String(name).toLowerCase(); $('#fvBotFilter').val(name); $('#fvBigFilter').val(name);
        renderFeed(); $('#fvClearFilters').show();
        $('html,body').animate({ scrollTop: $('.fv-grid').offset().top - 70 }, 200);
    });

    // hotspot row -> isolate that fault type
    $('#fvHotspots').on('click', 'tr.fv-clickable', function () {
        var cat = $(this).data('cat'); if (!cat) return;
        var cats = (data.byCategory || []); hiddenCats = {};
        for (var i = 0; i < cats.length; i++) if (cats[i].key !== cat) hiddenCats[cats[i].key] = true;
        renderChips(); renderFeed(); $('#fvClearFilters').show();
        $('html,body').animate({ scrollTop: $('.fv-grid').offset().top - 70 }, 200);
    });

    // ===================== REPORT MODAL =====================
    function openReport() {
        if (!data || data.empty) { return; }
        rebuildReport();
        $('#fvReportOverlay').addClass('active');
    }
    function closeReport() { $('#fvReportOverlay').removeClass('active'); }

    function rebuildReport() {
        var opts = {
            incidents: $('#fvOptIncidents').is(':checked'),
            timelines: $('#fvOptTimelines').is(':checked'),
            filters: $('#fvOptFilters').is(':checked')
        };
        var md = buildReport(opts);
        $('#fvReportText').val(md);
        $('#fvReportMeta').text(md.length.toLocaleString() + ' chars');
    }

    $('#fvReport').on('click', openReport);
    $('#fvReportClose').on('click', closeReport);
    $('#fvReportOverlay').on('click', function (e) { if (e.target === this) closeReport(); });
    $(document).on('keydown', function (e) { if (e.key === 'Escape') { closeReport(); closeFeedOverlay(); } });
    $('#fvOptIncidents,#fvOptTimelines,#fvOptFilters').on('change', rebuildReport);

    $('#fvReportCopy').on('click', function () {
        copyText($('#fvReportText').val());
        var $b = $(this), orig = $b.html();
        $b.html('<i class="fa-solid fa-check"></i> Copied'); setTimeout(function () { $b.html(orig); }, 1100);
    });
    $('#fvReportDownload').on('click', function () {
        var name = 'fleet-report-' + stamp() + '.md';
        var blob = new Blob([$('#fvReportText').val()], { type: 'text/markdown' });
        var a = document.createElement('a'); a.href = URL.createObjectURL(blob); a.download = name;
        document.body.appendChild(a); a.click(); document.body.removeChild(a);
        setTimeout(function () { URL.revokeObjectURL(a.href); }, 1000);
    });

    // Pull the in-service journald digests and fold them into the report body.
    $('#fvDigestRun').on('click', function () {
        var pid = ($('#fvDigestPid').val() || '').trim();
        runDigest('/Bots/QuantizedDigest' + (pid ? ('?pid=' + encodeURIComponent(pid)) : ''), this);
    });
    $('#fvDigestBotRun').on('click', function () {
        var name = ($('#fvDigestBot').val() || '').trim();
        if (!name) { $('#fvDigestMeta').css('color', '#f7768e').text('enter a bot name'); return; }
        runDigest('/Bots/BotDiag?name=' + encodeURIComponent(name), this);
    });

    function runDigest(url, btn) {
        var $b = $(btn), orig = $b.html();
        $b.prop('disabled', true).html('<i class="fa-solid fa-spinner fa-spin"></i> running…');
        $('#fvDigestMeta').css('color', 'var(--text-muted)').text('');
        $.getJSON(url, function (d) {
            if (!d || !d.ok) {
                var msg = (d && (d.error || (d.available === false ? 'script not embedded yet' : 'failed'))) || 'failed';
                $('#fvDigestMeta').css('color', '#f7768e').text(msg);
                return;
            }
            journaldDigest = d.output || '(script ran, no output)';
            $('#fvDigestMeta').css('color', 'var(--text-muted)').text('digest appended (' + journaldDigest.length.toLocaleString() + ' chars)');
            rebuildReport();
            var ta = document.getElementById('fvReportText'); if (ta) ta.scrollTop = ta.scrollHeight;
        }).fail(function () {
            $('#fvDigestMeta').css('color', '#f7768e').text('request failed');
        }).always(function () {
            $b.prop('disabled', false).html(orig);
        });
    }

    // Build a paste-ready markdown digest from the current payload.
    function buildReport(opts) {
        var L = [];
        var inc = (data.recent || []).slice();
        if (opts.filters) inc = inc.filter(passesFilter);

        L.push('# Barrens Chat — Quantized fleet report');
        L.push('_Generated ' + new Date().toISOString() + ' · window ' + fmtDur(data.windowSec) +
            ' · ' + (data.attributedLines || 0) + ' lines · ' + (data.botCount || 0) + ' bots active' +
            (opts.filters && hasFilters() ? ' · filtered view' : '') + '_');
        L.push('');
        L.push('> In-memory log-ring snapshot (C# brain/EXEC log; not all C++ ticks). Append the in-service journald digest below for the full bounded run report.');
        L.push('');

        L.push('## Summary');
        L.push('- Errors & warnings: **' + (data.errorTotal || 0) + '** (' + (data.errorsPerMin != null ? data.errorsPerMin : 0) + '/min)');
        L.push('- Low-priority (trash kills etc.): ' + (data.infoTotal || 0));
        var affected = (data.byBot || []).filter(function (b) { return b.count > 0; }).length;
        L.push('- Bots affected: ' + affected + ' / ' + (data.botCount || 0) + ' active');
        L.push('');

        // faults by type
        if ((data.byCategory || []).length) {
            L.push('## Faults by type');
            L.push('| Fault | Tier | Count |');
            L.push('|---|---|---|');
            data.byCategory.forEach(function (c) { L.push('| ' + cell(c.label) + ' | ' + c.tier + ' | ' + c.count + ' |'); });
            L.push('');
        }

        // what preceded
        if ((data.byPreceding || []).length) {
            L.push('## What preceded the fault');
            L.push('| Preceding command / why | Count |');
            L.push('|---|---|');
            data.byPreceding.forEach(function (p) { L.push('| ' + cell(p.label) + ' | ' + p.count + ' |'); });
            L.push('');
        }

        // hotspots
        if ((data.hotspots || []).length) {
            L.push('## Hotspots (100-yd cells)');
            L.push('| Where | Map | Top fault | Bots | Count |');
            L.push('|---|---|---|---|---|');
            data.hotspots.forEach(function (h) {
                L.push('| ' + h.x + ', ' + h.y + ' | ' + mapName(h.map) + ' | ' + h.topCategory + ' | ' + cell((h.bots || []).join(', ')) + ' | ' + h.count + ' |');
            });
            L.push('');
        }

        // leaderboard
        if ((data.byBot || []).length) {
            L.push('## Bot leaderboard');
            L.push('| Bot | Lvl | Class | Faults | +info | Top mix |');
            L.push('|---|---|---|---|---|---|');
            data.byBot.forEach(function (b) {
                var mix = (b.top || []).map(function (t) { return t.key + '×' + t.count; }).join(', ');
                L.push('| ' + cell(b.name) + ' | ' + b.level + ' | ' + (CLASS_NAMES[classOf[b.guid]] || '?') + ' | ' + b.count + ' | ' + (b.infoCount || 0) + ' | ' + cell(mix) + ' |');
            });
            L.push('');
        }

        // per-bot chronological timelines (worst few) — shows the LOOP, not just the tally
        if (opts.timelines) {
            var worst = (data.byBot || []).filter(function (b) { return b.count > 0; }).slice(0, 3);
            if (worst.length) {
                L.push('## Per-bot timelines (worst ' + worst.length + ', oldest → newest)');
                worst.forEach(function (b) {
                    var mine = inc.filter(function (i) { return i.guid === b.guid; })
                        .sort(function (a, c) { return a.seq - c.seq; }).slice(-24);
                    L.push('');
                    L.push('### ' + b.name + ' — L' + b.level + ' ' + (CLASS_NAMES[classOf[b.guid]] || '?') + ' · ' + b.count + ' faults');
                    mine.forEach(function (i) { L.push('- ' + incLine(i)); });
                });
                L.push('');
            }
        }

        // full incident log
        if (opts.incidents && inc.length) {
            L.push('## Incident log (' + inc.length + ', newest first)');
            L.push('| Time | Bot | Lvl | Fault | Where | After | Target | Message |');
            L.push('|---|---|---|---|---|---|---|---|');
            inc.forEach(function (i) {
                var where = i.hasPos ? (i.x + ',' + i.y + (i.map >= 0 ? '@' + mapName(i.map) : '')) : '';
                var after = i.why ? ('why=' + i.why) : (i.preCmd || '');
                L.push('| ' + clock(i.t) + ' | ' + cell(i.name) + ' | ' + i.level + ' | ' + i.category +
                    ' | ' + cell(where) + ' | ' + cell(after) + ' | ' + cell(i.target || '') + ' | ' + cell(i.msg) + ' |');
            });
            L.push('');
        }

        if (journaldDigest) {
            L.push('## Journald digest (in-service)');
            L.push('```');
            L.push(journaldDigest);
            L.push('```');
            L.push('');
        }

        return L.join('\n');
    }

    // one-line incident string for the timeline bullets
    function incLine(i) {
        var where = i.hasPos ? (' @(' + i.x + ',' + i.y + ')') : '';
        var after = i.why ? (' after why=' + i.why) : (i.preCmd ? (' after ' + i.preCmd) : '');
        var tgt = i.target ? (' target ' + i.target) : '';
        return clock(i.t) + '  ' + i.category + where + after + tgt + ' — ' + i.msg;
    }

    // ===================== HELPERS =====================
    function mapName(m) { if (m == null || m < 0) return '?'; return MAP_NAMES[m] || ('map ' + m); }
    function clock(utc) { var d = new Date(utc); if (isNaN(d)) return ''; return pad(d.getHours()) + ':' + pad(d.getMinutes()) + ':' + pad(d.getSeconds()); }
    function nowClock() { var d = new Date(); return pad(d.getHours()) + ':' + pad(d.getMinutes()) + ':' + pad(d.getSeconds()); }
    function stamp() { var d = new Date(); return d.getFullYear() + pad(d.getMonth() + 1) + pad(d.getDate()) + '-' + pad(d.getHours()) + pad(d.getMinutes()); }
    function pad(n) { return n < 10 ? '0' + n : '' + n; }

    function fmtDur(sec) {
        sec = Math.round(sec || 0);
        if (sec < 60) return sec + 's';
        var m = Math.floor(sec / 60), s = sec % 60;
        if (m < 60) return m + 'm' + (s ? ' ' + s + 's' : '');
        var h = Math.floor(m / 60); m = m % 60;
        return h + 'h' + (m ? ' ' + m + 'm' : '');
    }

    function hexA(hex, a) {
        var h = (hex || '').replace('#', '');
        if (h.length === 3) h = h[0] + h[0] + h[1] + h[1] + h[2] + h[2];
        var r = parseInt(h.substr(0, 2), 16), g = parseInt(h.substr(2, 2), 16), b = parseInt(h.substr(4, 2), 16);
        if (isNaN(r)) return 'rgba(122,162,247,' + a + ')';
        return 'rgba(' + r + ',' + g + ',' + b + ',' + a + ')';
    }

    function copyText(t) {
        if (navigator.clipboard && navigator.clipboard.writeText) { navigator.clipboard.writeText(t).catch(function () { }); return; }
        var ta = document.createElement('textarea'); ta.value = t; document.body.appendChild(ta);
        ta.select(); try { document.execCommand('copy'); } catch (e) { } document.body.removeChild(ta);
    }

    // markdown table cell: neutralize pipes and newlines
    function cell(s) { return String(s == null ? '' : s).replace(/\|/g, '\\|').replace(/\r?\n/g, ' '); }

    function esc(s) {
        return String(s == null ? '' : s)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    // ===================== BOOT =====================
    start();
});