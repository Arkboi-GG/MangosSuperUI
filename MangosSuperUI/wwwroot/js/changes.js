// MangosSuperUI — Change Graph JS
// audit_log as a drillable tree: Domain → Batch → Entry → Field, with undo at each level.

$(function () {

    // ===================== STATE =====================

    // Where we are in the drill-down. Everything renders from this.
    var nav = { level: 'domains', domain: null, domainLabel: null, batch: null, batchLabel: null };

    var filter = { search: '', op: '', days: '', show: 'all' };
    var domains = [];               // last overview payload, kept for breadcrumb colours
    var pendingRevert = null;       // { kind: 'entry'|'batch', id, batch, label }
    var searchTimer = null;

    var KIND_LABEL = {
        baseline: 'Baseline',
        state_before: 'Snapshot',
        delete_custom: 'Delete',
        registry: 'Tool',
        none: 'No undo'
    };

    // Drift view state — the default. "What differs from stock right now", computed live,
    // as opposed to `nav` above which walks the event log.
    var drift = { level: 'domains', domain: null, domainLabel: null, mode: 'tracked', search: '', path: '', crumbs: [] };
    var view = 'drift';
    var driftSearchTimer = null;

    var STATUS_LABEL = { modified: 'Modified', added: 'Added', removed: 'Removed', mixed: 'Mixed' };
    var driftGroups = [];   // last DriftDomain payload — children carry their own diffs

    // ===================== INIT =====================

    loadDrift();

    // ===================== VIEW SWITCH =====================

    $('.cg-view').on('click', function () {
        var target = $(this).data('view');
        if (target === view) return;

        $('.cg-view').removeClass('active');
        $(this).addClass('active');
        view = target;

        if (view === 'drift') {
            $('#cgDriftControls').show();
            $('#cgHistoryControls').hide();
            $('#cgSubtitle').text('What your server has that stock VMaNGOS does not');
            loadDrift();
        } else {
            $('#cgDriftControls').hide();
            $('#cgHistoryControls').show();
            $('#cgSubtitle').text('Every action ever taken — including ones already undone');
            loadOverview();
        }
    });

    // ===================== DRIFT — CONTROLS =====================

    $('#cgModeChips').on('click', '.cg-chip', function () {
        $('#cgModeChips .cg-chip').removeClass('active');
        $(this).addClass('active');
        drift.mode = $(this).data('mode');
        loadDriftLevel();
    });

    $('#cgRescan').on('click', function () {
        var btn = $(this);
        btn.prop('disabled', true).html('<i class="fa-solid fa-spinner fa-spin"></i> Rescanning...');
        $.ajax({ url: '/Changes/Rescan', method: 'POST' }).always(function () {
            btn.prop('disabled', false).html('<i class="fa-solid fa-rotate"></i> Rescan');
            loadDriftLevel();
        });
    });

    $('#cgDriftSearch').on('input', function () {
        var val = this.value.trim();
        clearTimeout(driftSearchTimer);
        driftSearchTimer = setTimeout(function () {
            drift.search = val;
            if (drift.level === 'entries') loadDriftDomain(drift.domain, drift.domainLabel, drift.path);
        }, 280);
    });

    function loadDriftLevel() {
        if (drift.level === 'domains') loadDrift();
        else loadDriftDomain(drift.domain, drift.domainLabel, drift.path);
    }

    // ===================== DRIFT — LEVEL 1 =====================

    function loadDrift() {
        drift.level = 'domains';
        drift.domain = null;
        drift.path = '';
        drift.crumbs = [];
        renderDriftCrumbs();
        $('#cgLevel').html(loading(drift.mode === 'deep'
            ? 'Comparing every table against its baseline — this one takes a moment...'
            : 'Measuring drift against baseline...'));

        $.getJSON('/Changes/Drift', { mode: drift.mode }, function (data) {
            if (data.error) { $('#cgLevel').html(errorBox(data.error)); return; }

            renderModeNote(data);
            renderDriftTotals(data.domains);
            renderDriftCrumbs();

            var total = (data.domains || []).reduce(function (s, d) { return s + d.total; }, 0);
            if (total === 0) {
                $('#cgLevel').html(
                    '<div class="cg-clean"><i class="fa-solid fa-circle-check"></i>' +
                    '<div class="cg-clean-title">No drift from stock</div>' +
                    '<div class="cg-clean-sub">' +
                    (drift.mode === 'deep'
                        ? 'A full baseline comparison found nothing different.'
                        : 'Nothing the panel has touched still differs from stock. Run a deep scan to include edits made outside the panel.') +
                    '</div></div>');
                return;
            }

            $('#cgLevel').html('<div class="cg-domains">' + data.domains.map(renderDriftNode).join('') + '</div>');
        }).fail(function () {
            $('#cgLevel').html(errorBox('Failed to measure drift'));
        });
    }

    function renderDriftNode(d) {
        var empty = d.total === 0;
        var h = '<div class="cg-node' + (empty ? ' empty' : '') + '" style="--cg-color: ' + d.color + ';"' +
            (empty ? '' : ' data-drift-domain="' + esc(d.key) + '" data-label="' + esc(d.label) + '"') + '>';

        h += '<div class="cg-node-head">';
        h += '<div class="cg-node-icon"><i class="fa-solid ' + esc(d.icon) + '"></i></div>';
        h += '<div class="cg-node-label">' + esc(d.label) + '</div>';
        h += '</div>';

        if (d.error) {
            h += '<div class="cg-note cg-note-warn" style="margin-top:12px;">' + esc(d.error) + '</div></div>';
            return h;
        }

        h += '<div class="cg-node-count">' + Number(d.total).toLocaleString() + '</div>';
        h += '<div class="cg-node-unit">' + (d.total === 1 ? 'entry differs' : 'entries differ') + '</div>';

        h += '<div class="cg-node-tags">';
        if (d.modified) h += '<span class="cg-status cg-status-modified">' + d.modified + ' modified</span>';
        if (d.added) h += '<span class="cg-status cg-status-added">' + d.added + ' added</span>';
        if (d.removed) h += '<span class="cg-status cg-status-removed">' + d.removed + ' removed</span>';
        h += '</div>';

        if (d.scannedAt) h += '<div class="cg-node-when">Measured ' + relTime(d.scannedAt) + '</div>';

        h += '</div>';
        return h;
    }

    $(document).on('click', '.cg-node[data-drift-domain]', function () {
        loadDriftDomain($(this).data('drift-domain'), $(this).data('label'));
    });

    function renderDriftTotals(domains) {
        var t = (domains || []).reduce(function (a, d) {
            a.total += d.total; a.modified += d.modified; a.added += d.added; a.removed += d.removed;
            return a;
        }, { total: 0, modified: 0, added: 0, removed: 0 });

        var h = '<span class="cg-total">' + t.total.toLocaleString() + ' differ from stock</span>';
        if (t.modified) h += '<span class="cg-total warn">' + t.modified.toLocaleString() + ' modified</span>';
        if (t.added) h += '<span class="cg-total">' + t.added.toLocaleString() + ' added</span>';
        if (t.removed) h += '<span class="cg-total err">' + t.removed.toLocaleString() + ' removed</span>';
        $('#cgTotals').html(h);
    }

    function renderModeNote(data) {
        $('#cgModeNote').text(data.mode === 'deep'
            ? 'Deep scan compares every row against its baseline — catches direct SQL edits. Cached for 10 minutes.'
            : 'Tracked mode only checks entries the panel has touched. Deep scan also finds edits made outside it.');
    }

    // ===================== DRIFT — LEVEL 2 =====================

    function loadDriftDomain(domain, label, path) {
        drift.level = 'entries';
        drift.domain = domain;
        drift.domainLabel = label || domain;
        drift.path = path || '';
        $('#cgLevel').html(loading('Loading differences...'));

        $.getJSON('/Changes/DriftDomain',
            { domain: domain, mode: drift.mode, search: drift.search, path: drift.path },
            function (data) {
                if (data.error) { $('#cgLevel').html(errorBox(data.error)); return; }

                drift.crumbs = data.crumbs || [];
                renderDriftCrumbs();

                // The server decides whether this level splits further or lists entries,
                // so the tree can be as deep as the data justifies without the UI knowing
                // the shape in advance.
                if (data.kind === 'facets') { renderFacets(data); return; }
                renderLeafGroups(data, domain);
            }).fail(function () {
                $('#cgLevel').html(errorBox('Failed to load differences'));
            });
    }

    function renderFacets(data) {
        driftGroups = [];
        var facets = data.facets || [];
        if (!facets.length) {
            $('#cgLevel').html('<div class="cg-clean"><i class="fa-solid fa-circle-check"></i>' +
                '<div class="cg-clean-title">Nothing here</div></div>');
            return;
        }
        $('#cgLevel').html('<div class="cg-domains">' + facets.map(renderFacetNode).join('') + '</div>');
    }

    function renderFacetNode(f) {
        var h = '<div class="cg-node" data-facet-path="' + esc(f.path) + '" data-label="' + esc(f.label) + '">';
        h += '<div class="cg-node-head">';
        h += '<div class="cg-node-icon"><i class="fa-solid ' + esc(f.icon) + '"></i></div>';
        h += '<div class="cg-node-label">' + esc(f.label) + '</div>';
        h += '</div>';

        h += '<div class="cg-node-count">' + Number(f.count).toLocaleString() + '</div>';
        h += '<div class="cg-node-unit">' + (f.count === 1 ? 'entry' : 'entries') +
            (f.names ? ' · ' + Number(f.names).toLocaleString() + ' name(s)' : '') + '</div>';

        h += '<div class="cg-node-tags">';
        if (f.modified) h += '<span class="cg-status cg-status-modified">' + f.modified + ' modified</span>';
        if (f.added) h += '<span class="cg-status cg-status-added">' + f.added + ' added</span>';
        if (f.removed) h += '<span class="cg-status cg-status-removed">' + f.removed + ' removed</span>';
        h += '</div>';

        if (f.hint) h += '<div class="cg-node-when">' + esc(f.hint) + '</div>';
        h += '</div>';
        return h;
    }

    $(document).on('click', '.cg-node[data-facet-path]', function () {
        loadDriftDomain(drift.domain, drift.domainLabel, $(this).data('facet-path'));
    });

    function renderLeafGroups(data, domain) {
        driftGroups = data.groups || [];
        if (!driftGroups.length) {
            $('#cgLevel').html(
                '<div class="cg-clean"><i class="fa-solid fa-circle-check"></i>' +
                '<div class="cg-clean-title">Nothing differs here</div>' +
                '<div class="cg-clean-sub">Every ' + esc(data.label || domain) + ' entry matches stock.</div></div>');
            return;
        }

        var h = '<div class="cg-list">' + driftGroups.map(renderDriftGroup).join('') + '</div>';
        h += '<div style="font-size:12px;color:var(--text-muted);margin-top:10px;text-align:center;">' +
            Number(data.totalGroups).toLocaleString() + ' group(s) · ' +
            Number(data.total).toLocaleString() + ' entr(y/ies) differ' +
            (data.truncated ? ' — showing the first ' + driftGroups.length + ', narrow with the search box' : '') +
            '</div>';
        $('#cgLevel').html(h);
    }

    // A group of one renders as a plain row; only real variant sets get the expander,
    // so a single modified spell doesn't need a click to see anything.
    function renderDriftGroup(g) {
        var single = g.count === 1;
        var only = g.children[0];

        var h = '<div class="cg-group" data-group="' + g.key + '">';

        h += '<div class="cg-row cg-group-head"' + (single ? ' data-drift-entry="' + only.entry + '"' : '') + '>';
        h += '<div class="cg-row-spine">' +
            (single ? '└' : '<i class="fa-solid fa-chevron-right cg-group-chevron"></i>') + '</div>';

        h += '<div class="cg-row-main">';
        h += '<div class="cg-row-title">' + esc(g.name);
        if (single) h += ' <span style="font-weight:500;color:var(--text-muted);">#' + only.entry + '</span>';
        else h += ' <span class="cg-group-count">' + g.count + '</span>' +
            ' <span style="font-weight:500;color:var(--text-muted);font-size:11.5px;">#' +
            g.minEntry + '–' + g.maxEntry + '</span>';
        h += '</div>';

        h += '<div class="cg-row-meta">';
        if (g.origin) h += '<span style="color:var(--accent);"><i class="fa-solid fa-dice-d20"></i> ' + esc(g.origin.summary) + '</span>';
        if (g.fieldCount) h += '<span><i class="fa-solid fa-pen"></i> ' + g.fieldCount + ' field(s) changed</span>';
        if (g.lastTouched) h += '<span><i class="fa-solid fa-clock"></i> ' + relTime(g.lastTouched) + '</span>';
        if (!single && g.status === 'mixed')
            h += '<span>' + [g.modified && g.modified + ' modified', g.added && g.added + ' added',
                g.removed && g.removed + ' removed'].filter(Boolean).join(' · ') + '</span>';
        if (g.untracked) h += '<span style="color:#a855f7;"><i class="fa-solid fa-circle-question"></i> not in the audit log</span>';
        h += '</div></div>';

        h += '<div class="cg-row-right">';
        h += '<span class="cg-status cg-status-' + esc(g.status) + '">' +
            esc(STATUS_LABEL[g.status] || g.status) + '</span>';
        h += '<button class="cg-btn cg-btn-warn ' + (single ? 'cg-resolve' : 'cg-resolve-group') + '"' +
            (single ? ' data-entry="' + only.entry + '"' : ' data-group="' + g.key + '"') +
            ' data-name="' + esc(g.name) + '" data-status="' + esc(g.status) + '"' +
            ' data-count="' + g.count + '">' +
            '<i class="fa-solid fa-rotate-left"></i> ' +
            (g.status === 'added' ? 'Delete' : 'Restore') + (single ? '' : ' all ' + g.count) + '</button>';
        h += '<i class="fa-solid fa-chevron-right" style="color: var(--text-muted); font-size: 11px;"></i>';
        h += '</div></div>';

        // Children — indented, one row per entry, revealed by the expander.
        if (!single) {
            h += '<div class="cg-children">';
            g.children.forEach(function (c) {
                h += '<div class="cg-row cg-child" data-drift-entry="' + c.entry + '">';
                h += '<div class="cg-row-spine">└</div>';
                h += '<div class="cg-row-main">';
                h += '<div class="cg-row-title" style="font-size:12.5px;">#' + c.entry +
                    ' <span style="font-weight:500;color:var(--text-secondary);">' + esc(c.name || '') + '</span></div>';
                h += '<div class="cg-row-meta">';
                if (c.loot) h += '<span><i class="fa-solid fa-location-dot"></i> ' + esc(c.loot.summary) + '</span>';
                if (c.loot && c.loot.tier) h += '<span><i class="fa-solid fa-gem"></i> ' + esc(c.loot.tier) + '</span>';
                if (c.status === 'modified') h += '<span><i class="fa-solid fa-pen"></i> ' + c.fieldCount + ' field(s)</span>';
                if (c.lastBatchLabel) h += '<span><i class="fa-solid fa-layer-group"></i> ' + esc(truncate(c.lastBatchLabel, 40)) + '</span>';
                h += '</div></div>';
                h += '<div class="cg-row-right">';
                h += '<span class="cg-status cg-status-' + esc(c.status) + '">' + esc(STATUS_LABEL[c.status] || c.status) + '</span>';
                h += '<button class="cg-btn cg-btn-ghost cg-resolve" data-entry="' + c.entry + '"' +
                    ' data-name="' + esc(c.name || ('#' + c.entry)) + '" data-status="' + esc(c.status) + '">' +
                    '<i class="fa-solid fa-rotate-left"></i></button>';
                h += '</div></div>';
            });
            h += '</div>';
        }

        h += '</div>';
        return h;
    }

    $(document).on('click', '.cg-group-head', function (e) {
        if ($(e.target).closest('button').length) return;
        var group = $(this).closest('.cg-group');
        if (group.find('.cg-children').length) { group.toggleClass('open'); return; }
        openDriftDrawer($(this).data('drift-entry'));
    });

    $(document).on('click', '.cg-child', function (e) {
        if ($(e.target).closest('button').length) return;
        openDriftDrawer($(this).data('drift-entry'));
    });

    function openDriftDrawer(entry) {
        var found = null;
        (driftGroups || []).forEach(function (g) {
            g.children.forEach(function (c) { if (c.entry === entry) found = c; });
        });
        if (!found) { showToast('That entry is no longer in the list', 'error'); return; }

        $('#cgDrawer').addClass('open');
        renderDriftDrawer(found);
    }

    function renderDriftDrawer(n) {
        $('#cgDrawerTitle').text((n.name || ('#' + n.entry)));
        $('#cgDrawerSub').html('#' + n.entry + ' · ' +
            '<span class="cg-status cg-status-' + esc(n.status) + '">' + esc(STATUS_LABEL[n.status] || n.status) + '</span>');

        var h = '';
        h += detail('Status', esc(STATUS_LABEL[n.status] || n.status));

        // Where a generated item actually came from — the Lootifier registry knows this
        // even though the audit log never recorded it.
        if (n.loot) {
            h += detail('Origin', esc(n.loot.summary));
            if (n.loot.baseName) h += detail('Base item', esc(n.loot.baseName) + ' <span style="color:var(--text-muted);">#' + n.loot.baseEntry + '</span>');
            if (n.loot.creatureName) h += detail('Creature', esc(n.loot.creatureName) + ' <span style="color:var(--text-muted);">#' + n.loot.creatureEntry + '</span>');
            if (n.loot.mapName) h += detail('Location', esc(n.loot.mapName) + (n.loot.category ? ' <span style="color:var(--text-muted);">(' + esc(n.loot.category) + ')</span>' : ''));
            if (n.loot.tier) h += detail('Tier', esc(n.loot.tier) + (n.loot.budgetPct ? ' · ' + Math.round(n.loot.budgetPct) + '% budget' : ''));
        }

        if (n.touchCount) h += detail('Panel edits', n.touchCount + ' audit row(s)');
        if (n.lastTouched) h += detail('Last touched', shortStamp(n.lastTouched));
        if (n.lastBatchLabel) h += detail('Last batch', esc(n.lastBatchLabel));
        if (n.untracked)
            h += '<div class="cg-note cg-note-warn" style="margin-top:10px;">' +
                '<i class="fa-solid fa-circle-question"></i> Nothing in the audit log explains this difference — ' +
                'it was most likely changed by direct SQL or before the panel tracked it.</div>';

        if (n.status === 'added') {
            h += '<div class="cg-note cg-note-info" style="margin-top:14px;">' +
                'This entry exists on your server but not in the baseline — it is content you added. ' +
                'Restoring to stock means deleting it.</div>';
        } else if (n.status === 'removed') {
            h += '<div class="cg-note cg-note-warn" style="margin-top:14px;">' +
                'This entry exists in stock VMaNGOS but not on your server — it was deleted. ' +
                'Restoring puts it back from the baseline.</div>';
        } else if (n.fields && n.fields.length) {
            h += '<div class="cg-section-title">' + n.fields.length + ' field(s) differ from baseline</div>';
            h += '<table class="cg-diff"><thead><tr><th>Field</th><th>Stock</th><th>Yours</th></tr></thead><tbody>';
            n.fields.forEach(function (f) {
                h += '<tr><td class="cg-diff-field">' + esc(f.field) + '</td>' +
                    '<td class="cg-diff-old">' + esc(truncate(f.baseline, 90)) + '</td>' +
                    '<td class="cg-diff-new">' + esc(truncate(f.current, 90)) + '</td></tr>';
            });
            h += '</tbody></table>';
        }

        $('#cgDrawerBody').html(h);

        $('#cgDrawerFoot').html(
            '<button class="cg-btn cg-btn-warn cg-resolve" data-entry="' + n.entry + '"' +
            ' data-name="' + esc(n.name || ('#' + n.entry)) + '" data-status="' + esc(n.status) + '">' +
            '<i class="fa-solid fa-rotate-left"></i> ' +
            (n.status === 'added' ? 'Delete this' : 'Restore to stock') + '</button>' +
            '<span style="font-size:11.5px;color:var(--text-muted);">Recorded in the audit log either way.</span>');
    }

    // ===================== DRIFT — RESOLVE =====================

    $(document).on('click', '.cg-resolve', function (e) {
        e.stopPropagation();
        var entry = $(this).data('entry');
        var name = $(this).data('name');
        var status = $(this).data('status');

        pendingRevert = { kind: 'drift', domain: drift.domain, entry: entry, label: name };

        $('#cgRevertTitle').text(status === 'added' ? 'Delete Added Entry' : 'Restore to Stock');
        $('#cgRevertBody').html(status === 'added'
            ? 'Delete <strong>' + esc(name) + '</strong> (#' + entry + ')? It does not exist in stock VMaNGOS.'
            : 'Restore <strong>' + esc(name) + '</strong> (#' + entry + ') to its stock values?');
        $('#cgRevertPlan').html(status === 'added'
            ? '<i class="fa-solid fa-trash"></i> The entry and everything hanging off it is removed.' +
              '<br><span style="color:var(--text-muted);">For custom spells that includes the rank chain, so no orphaned ranks are left behind.</span>'
            : '<i class="fa-solid fa-database"></i> Rows are replaced from the og_ baseline inside a transaction.' +
              '<br><span style="color:var(--text-muted);">The current state is re-checked first, so a stale page cannot drive this.</span>');

        new bootstrap.Modal($('#cgRevertModal')[0]).show();
    });

    $(document).on('click', '.cg-resolve-group', function (e) {
        e.stopPropagation();
        var key = $(this).data('group');
        var name = $(this).data('name');
        var status = $(this).data('status');
        var count = $(this).data('count');

        var group = (driftGroups || []).filter(function (g) { return g.key === key; })[0];
        if (!group) return;

        var entries = group.children.map(function (c) { return c.entry; });
        pendingRevert = { kind: 'drift-group', domain: drift.domain, entries: entries, label: name };

        $('#cgRevertTitle').text(status === 'added' ? 'Delete Variant Set' : 'Restore Variant Set');
        $('#cgRevertBody').html(
            (status === 'added' ? 'Delete all <strong>' : 'Restore all <strong>') + count +
            '</strong> entries named <strong>' + esc(name) + '</strong>? (#' +
            group.minEntry + '–' + group.maxEntry + ')');
        $('#cgRevertPlan').html(
            '<i class="fa-solid fa-list-ol"></i> Each entry is resolved individually and recorded under one batch.' +
            '<br><span style="color:var(--text-muted);">Anything that already matches stock is skipped, not failed.</span>');

        new bootstrap.Modal($('#cgRevertModal')[0]).show();
    });

    // ===================== HISTORY (event log) =====================

    // ===================== FILTERS =====================

    $('#cgSearch').on('input', function () {
        var val = this.value.trim();
        clearTimeout(searchTimer);
        searchTimer = setTimeout(function () {
            filter.search = val;
            reloadCurrentLevel();
        }, 280);
    });

    $('#cgShowChips').on('click', '.cg-chip', function () {
        $('#cgShowChips .cg-chip').removeClass('active');
        $(this).addClass('active');
        filter.show = $(this).data('show') || 'all';
        reloadCurrentLevel();
    });

    $('#cgDaysChips').on('click', '.cg-chip', function () {
        $('#cgDaysChips .cg-chip').removeClass('active');
        $(this).addClass('active');
        filter.days = $(this).data('days') || '';
        reloadCurrentLevel();
    });

    $('#cgOperator').on('change', function () {
        filter.op = this.value;
        reloadCurrentLevel();
    });

    function query(extra) {
        var q = {};
        if (filter.search) q.search = filter.search;
        if (filter.op) q.op = filter.op;
        if (filter.days) q.days = filter.days;
        if (filter.show && filter.show !== 'all') q.show = filter.show;
        return $.extend(q, extra || {});
    }

    // Filters apply to whichever level is on screen — re-run that one, not always the root.
    function reloadCurrentLevel() {
        if (view === 'drift') { loadDriftLevel(); return; }
        if (nav.level === 'domains') loadOverview();
        else if (nav.level === 'batches') loadBatches(nav.domain, nav.domainLabel);
        else loadEntries(nav.batch, nav.batchLabel);
    }

    // ===================== BREADCRUMB =====================

    function renderBreadcrumb() {
        var h = '';
        h += crumb('All Changes', 'fa-diagram-project', nav.level === 'domains', 'domains');

        if (nav.domain) {
            h += '<span class="cg-crumb-sep"><i class="fa-solid fa-chevron-right"></i></span>';
            var d = domainByKey(nav.domain);
            h += crumb(nav.domainLabel || nav.domain, d ? d.icon : 'fa-folder', nav.level === 'batches', 'batches');
        }

        if (nav.batch) {
            h += '<span class="cg-crumb-sep"><i class="fa-solid fa-chevron-right"></i></span>';
            h += crumb(nav.batchLabel || nav.batch, 'fa-layer-group', nav.level === 'entries', 'entries');
        }

        $('#cgBreadcrumb').html(h);
    }

    function crumb(label, icon, current, target) {
        return '<span class="cg-crumb' + (current ? ' current' : '') + '" data-goto="' + target + '">' +
            '<i class="fa-solid ' + icon + '"></i>' + esc(truncate(label, 54)) + '</span>';
    }

    $(document).on('click', '.cg-crumb', function () {
        var target = $(this).data('goto');

        if (view === 'drift') {
            if (target === 'drift-domains') loadDrift();
            else if (target === 'drift-path') loadDriftDomain(drift.domain, drift.domainLabel, $(this).data('path'));
            return;
        }

        if (target === 'domains') { nav = { level: 'domains', domain: null, domainLabel: null, batch: null, batchLabel: null }; loadOverview(); }
        else if (target === 'batches') { nav.level = 'batches'; nav.batch = null; nav.batchLabel = null; loadBatches(nav.domain, nav.domainLabel); }
    });

    // The trail grows with the facet path, so every level stays one click away.
    function renderDriftCrumbs() {
        var atRoot = drift.level === 'domains';
        var h = crumb('Current Drift', 'fa-code-compare', atRoot, 'drift-domains');

        if (drift.domain) {
            var crumbs = drift.crumbs || [];
            h += '<span class="cg-crumb-sep"><i class="fa-solid fa-chevron-right"></i></span>';
            h += '<span class="cg-crumb' + (crumbs.length === 0 ? ' current' : '') + '"' +
                ' data-goto="drift-path" data-path="">' +
                '<i class="fa-solid fa-folder"></i>' + esc(drift.domainLabel || drift.domain) + '</span>';

            var running = [];
            crumbs.forEach(function (c, i) {
                running.push(c.key);
                var last = i === crumbs.length - 1;
                h += '<span class="cg-crumb-sep"><i class="fa-solid fa-chevron-right"></i></span>';
                h += '<span class="cg-crumb' + (last ? ' current' : '') + '"' +
                    ' data-goto="drift-path" data-path="' + esc(running.join('/')) + '">' +
                    esc(truncate(c.label, 40)) + '</span>';
            });
        }

        $('#cgBreadcrumb').html(h);
    }

    // ===================== LEVEL 1 — DOMAINS =====================

    function loadOverview() {
        nav.level = 'domains';
        renderBreadcrumb();
        $('#cgLevel').html(loading('Reading the change log...'));

        $.getJSON('/Changes/Overview', query(), function (data) {
            if (data.error) { $('#cgLevel').html(errorBox(data.error)); return; }

            domains = data.domains || [];
            renderTotals(data.totals);
            renderOperators(data.operators);
            renderBreadcrumb();

            var any = domains.some(function (d) { return d.changes > 0; });
            if (!any) {
                $('#cgLevel').html(
                    '<div class="cg-empty"><i class="fa-solid fa-diagram-project"></i>' +
                    '<div style="font-size:14px;font-weight:600;">No changes match these filters</div>' +
                    '<div style="font-size:12.5px;margin-top:4px;">Widen the time range or clear the search.</div></div>');
                return;
            }

            $('#cgLevel').html('<div class="cg-domains">' + domains.map(renderDomainNode).join('') + '</div>');
        }).fail(function () {
            $('#cgLevel').html(errorBox('Failed to reach the change graph service'));
        });
    }

    function renderDomainNode(d) {
        var empty = d.changes === 0;
        var h = '<div class="cg-node' + (empty ? ' empty' : '') + '" style="--cg-color: ' + d.color + ';"' +
            (empty ? '' : ' data-domain="' + esc(d.key) + '" data-label="' + esc(d.label) + '"') + '>';

        h += '<div class="cg-node-head">';
        h += '<div class="cg-node-icon"><i class="fa-solid ' + esc(d.icon) + '"></i></div>';
        h += '<div class="cg-node-label">' + esc(d.label) + '</div>';
        h += '</div>';

        h += '<div class="cg-node-count">' + Number(d.changes).toLocaleString() + '</div>';
        h += '<div class="cg-node-unit">change' + (d.changes !== 1 ? 's' : '') +
            ' · ' + Number(d.batches).toLocaleString() + ' batch' + (d.batches !== 1 ? 'es' : '') + '</div>';

        h += '<div class="cg-node-tags">';
        if (d.revertable) h += '<span class="cg-tag cg-tag-revertable">' + d.revertable + ' undoable</span>';
        if (d.reverted) h += '<span class="cg-tag cg-tag-reverted">' + d.reverted + ' undone</span>';
        if (d.failures) h += '<span class="cg-tag cg-tag-failed">' + d.failures + ' failed</span>';
        h += '</div>';

        if (d.lastChange) h += '<div class="cg-node-when">Last change ' + relTime(d.lastChange) + '</div>';

        h += '</div>';
        return h;
    }

    $(document).on('click', '.cg-node[data-domain]', function () {
        loadBatches($(this).data('domain'), $(this).data('label'));
    });

    function renderTotals(t) {
        if (!t) { $('#cgTotals').empty(); return; }
        var h = '';
        h += '<span class="cg-total">' + Number(t.changes).toLocaleString() + ' changes</span>';
        h += '<span class="cg-total muted">' + Number(t.batches).toLocaleString() + ' batches</span>';
        if (t.revertable) h += '<span class="cg-total">' + Number(t.revertable).toLocaleString() + ' undoable</span>';
        if (t.reverted) h += '<span class="cg-total muted">' + Number(t.reverted).toLocaleString() + ' undone</span>';
        if (t.failures) h += '<span class="cg-total err">' + Number(t.failures).toLocaleString() + ' failed</span>';
        $('#cgTotals').html(h);
    }

    function renderOperators(ops) {
        if (!ops || !ops.length) return;
        var sel = $('#cgOperator');
        if (sel.find('option').length > 1) return;   // populated once
        ops.forEach(function (o) {
            if (o) sel.append('<option value="' + esc(o) + '">' + esc(o) + '</option>');
        });
    }

    // ===================== LEVEL 2 — BATCHES =====================

    function loadBatches(domain, label) {
        nav.level = 'batches';
        nav.domain = domain;
        nav.domainLabel = label || domain;
        nav.batch = null;
        nav.batchLabel = null;
        renderBreadcrumb();
        $('#cgLevel').html(loading('Loading batches...'));

        $.getJSON('/Changes/Batches', query({ domain: domain }), function (data) {
            if (data.error) { $('#cgLevel').html(errorBox(data.error)); return; }

            var batches = data.batches || [];
            if (!batches.length) {
                $('#cgLevel').html(
                    '<div class="cg-empty"><i class="fa-solid fa-layer-group"></i>' +
                    '<div style="font-size:14px;font-weight:600;">Nothing here yet</div>' +
                    '<div style="font-size:12.5px;margin-top:4px;">No batches in this domain match the filters.</div></div>');
                return;
            }

            $('#cgLevel').html('<div class="cg-list">' + batches.map(renderBatchRow).join('') + '</div>' + pager(data));
        }).fail(function () {
            $('#cgLevel').html(errorBox('Failed to load batches'));
        });
    }

    function renderBatchRow(b) {
        var allReverted = b.changes > 0 && b.reverted === b.changes;

        var h = '<div class="cg-row' + (allReverted ? ' reverted' : '') + (b.failures ? ' failed' : '') + '"' +
            ' data-batch="' + esc(b.batchKey) + '" data-label="' + esc(b.label) + '">';

        h += '<div class="cg-row-spine"><i class="fa-solid ' +
            (b.isRealBatch ? 'fa-layer-group' : 'fa-minus') + '"></i></div>';

        h += '<div class="cg-row-main">';
        h += '<div class="cg-row-title">' + esc(b.label) + '</div>';
        h += '<div class="cg-row-meta">';
        h += '<span><i class="fa-solid fa-clock"></i> ' + relTime(b.startedAt) + '</span>';
        h += '<span><i class="fa-solid fa-user"></i> ' + esc(b.operator || 'system') + '</span>';
        if (b.actionCount > 1) h += '<span><i class="fa-solid fa-code-branch"></i> ' + b.actionCount + ' action types</span>';
        if (b.revertable) h += '<span style="color: var(--status-online);"><i class="fa-solid fa-rotate-left"></i> ' + b.revertable + ' undoable</span>';
        if (b.reverted) h += '<span><i class="fa-solid fa-check"></i> ' + b.reverted + ' undone</span>';
        if (b.failures) h += '<span style="color: var(--status-error);"><i class="fa-solid fa-triangle-exclamation"></i> ' + b.failures + ' failed</span>';
        h += '</div></div>';

        h += '<div class="cg-row-right">';
        h += '<div style="text-align:right;"><div class="cg-row-count">' + Number(b.changes).toLocaleString() + '</div>' +
            '<div class="cg-row-count-unit">change' + (b.changes !== 1 ? 's' : '') + '</div></div>';
        if (b.revertable > 0)
            h += '<button class="cg-btn cg-btn-warn cg-revert-batch" data-batch="' + esc(b.batchKey) + '"' +
                ' data-label="' + esc(b.label) + '" data-count="' + b.revertable + '">' +
                '<i class="fa-solid fa-rotate-left"></i> Undo ' + b.revertable + '</button>';
        h += '<i class="fa-solid fa-chevron-right" style="color: var(--text-muted); font-size: 11px;"></i>';
        h += '</div></div>';
        return h;
    }

    $(document).on('click', '.cg-row[data-batch]', function (e) {
        if ($(e.target).closest('.cg-revert-batch').length) return;   // the button owns its click
        loadEntries($(this).data('batch'), $(this).data('label'));
    });

    // ===================== LEVEL 3 — ENTRIES =====================

    function loadEntries(batch, label) {
        nav.level = 'entries';
        nav.batch = batch;
        nav.batchLabel = label || batch;
        renderBreadcrumb();
        $('#cgLevel').html(loading('Loading changes...'));

        $.getJSON('/Changes/Entries', { batch: batch }, function (data) {
            if (data.error) { $('#cgLevel').html(errorBox(data.error)); return; }

            var entries = data.entries || [];
            if (!entries.length) {
                $('#cgLevel').html('<div class="cg-empty"><i class="fa-solid fa-list"></i><div>No changes in this batch.</div></div>');
                return;
            }

            $('#cgLevel').html('<div class="cg-list">' + entries.map(renderEntryRow).join('') + '</div>' + pager(data));
        }).fail(function () {
            $('#cgLevel').html(errorBox('Failed to load changes'));
        });
    }

    function renderEntryRow(e) {
        var h = '<div class="cg-row' + (e.revertedAt ? ' reverted' : '') + (!e.success ? ' failed' : '') + '"' +
            ' data-entry="' + e.id + '">';

        h += '<div class="cg-row-spine">└</div>';

        h += '<div class="cg-row-main">';
        h += '<div class="cg-row-title">' + esc(prettyAction(e.action)) +
            (e.targetName ? ' <span style="font-weight:500;color:var(--text-secondary);">' + esc(e.targetName) + '</span>' : '') +
            '</div>';
        h += '<div class="cg-row-meta">';
        h += '<span><i class="fa-solid fa-clock"></i> ' + relTime(e.timestamp) + '</span>';
        if (e.targetType) h += '<span><i class="fa-solid fa-table"></i> ' + esc(e.targetType) + (e.targetId ? ' #' + e.targetId : '') + '</span>';
        if (e.revertedAt) h += '<span><i class="fa-solid fa-clock-rotate-left"></i> undone ' + relTime(e.revertedAt) + '</span>';
        if (!e.success) h += '<span style="color: var(--status-error);"><i class="fa-solid fa-triangle-exclamation"></i> failed</span>';
        h += '</div></div>';

        h += '<div class="cg-row-right">';
        h += '<span class="cg-kind cg-kind-' + esc(e.revertKind || 'none') + '">' +
            esc(KIND_LABEL[e.revertKind] || e.revertKind || 'No undo') + '</span>';
        h += '<i class="fa-solid fa-chevron-right" style="color: var(--text-muted); font-size: 11px;"></i>';
        h += '</div></div>';
        return h;
    }

    $(document).on('click', '.cg-row[data-entry]', function () {
        openDrawer($(this).data('entry'));
    });

    // ===================== LEVEL 4 — ENTRY DRAWER =====================

    function openDrawer(id) {
        $('#cgDrawer').addClass('open');
        $('#cgDrawerTitle').text('Loading...');
        $('#cgDrawerSub').text('');
        $('#cgDrawerBody').html(loading('Reading the change...'));
        $('#cgDrawerFoot').empty();

        $.getJSON('/Changes/Entry', { id: id }, function (data) {
            if (!data.found) {
                $('#cgDrawerBody').html(errorBox(data.error || 'Change not found'));
                return;
            }
            renderDrawer(data);
        });
    }

    function renderDrawer(data) {
        var e = data.entry;

        $('#cgDrawerTitle').text(prettyAction(e.action));
        $('#cgDrawerSub').html(
            esc(e.targetName || e.targetType || '') +
            ' · <span style="font-family:monospace;">#' + e.id + '</span> · ' + esc(shortStamp(e.timestamp)));

        var h = '';

        // ---- Facts ----
        h += detail('When', shortStamp(e.timestamp));
        h += detail('Operator', (e.operator || 'system') + (e.operatorIp ? ' (' + e.operatorIp + ')' : ''));
        h += detail('Category', e.category);
        if (e.targetType) h += detail('Target', e.targetType + (e.targetId ? ' #' + e.targetId : ''));
        if (e.batchLabel) h += detail('Batch', e.batchLabel);
        h += detail('Undo path', '<span class="cg-kind cg-kind-' + esc(e.revertKind || 'none') + '">' +
            esc(KIND_LABEL[e.revertKind] || 'No undo') + '</span>');
        if (e.revertedAt) h += detail('Undone', shortStamp(e.revertedAt) +
            (e.revertedById ? ' (by change #' + e.revertedById + ')' : ''));
        if (e.notes) h += detail('Notes', esc(e.notes));

        // ---- Field diff ----
        if (data.diff && data.diff.fields && data.diff.fields.length) {
            h += '<div class="cg-section-title">Fields differing from ' + esc(data.diff.reference) +
                ' — ' + esc(data.diff.table) + ' #' + data.diff.entry + '</div>';
            h += '<table class="cg-diff"><thead><tr><th>Field</th><th>' +
                esc(titleCase(data.diff.reference)) + '</th><th>Now</th></tr></thead><tbody>';
            data.diff.fields.forEach(function (f) {
                h += '<tr><td class="cg-diff-field">' + esc(f.field) + '</td>' +
                    '<td class="cg-diff-old">' + esc(truncate(f.original, 90)) + '</td>' +
                    '<td class="cg-diff-new">' + esc(truncate(f.current, 90)) + '</td></tr>';
            });
            h += '</tbody></table>';
        } else if (data.diff) {
            h += '<div class="cg-section-title">Field diff</div>';
            h += '<div class="cg-note cg-note-muted">Nothing differs from ' + esc(data.diff.reference) +
                ' right now — this change may already have been undone or overwritten.</div>';
        } else if (data.diffNote) {
            h += '<div class="cg-section-title">Field diff</div>';
            h += '<div class="cg-note cg-note-warn">' + esc(data.diffNote) + '</div>';
        }

        // ---- Raw state ----
        if (e.stateBefore) {
            h += '<div class="cg-section-title">Captured before-state</div>';
            h += '<div class="cg-json">' + esc(prettyJson(e.stateBefore)) + '</div>';
        }
        if (e.stateAfter) {
            h += '<div class="cg-section-title">Recorded result</div>';
            h += '<div class="cg-json">' + esc(prettyJson(e.stateAfter)) + '</div>';
        }
        if (e.raCommand) {
            h += '<div class="cg-section-title">Command</div>';
            h += '<div class="cg-json">' + esc(e.raCommand) + (e.raResponse ? '\n\n' + e.raResponse : '') + '</div>';
        }

        $('#cgDrawerBody').html(h);

        // ---- Undo control ----
        var r = data.revert || {};
        if (r.available) {
            $('#cgDrawerFoot').html(
                '<button class="cg-btn cg-btn-warn cg-revert-entry" data-id="' + e.id + '"' +
                ' data-label="' + esc(prettyAction(e.action) + (e.targetName ? ' — ' + e.targetName : '')) + '"' +
                ' data-plan="' + esc(r.summary || '') + '">' +
                '<i class="fa-solid fa-rotate-left"></i> Undo this change</button>' +
                '<span style="font-size:11.5px;color:var(--text-muted);">' + esc(r.summary || '') + '</span>');
        } else {
            $('#cgDrawerFoot').html(
                '<div class="cg-note cg-note-muted" style="flex:1;"><i class="fa-solid fa-circle-info"></i> ' +
                esc(r.reason || 'This change cannot be undone from here.') + '</div>');
        }
    }

    function detail(label, valueHtml) {
        return '<div class="cg-detail-row"><div class="cg-detail-label">' + esc(label) + '</div>' +
            '<div class="cg-detail-value">' + valueHtml + '</div></div>';
    }

    $('#cgDrawerClose').on('click', closeDrawer);
    $('#cgDrawer').on('click', function (e) { if (e.target === this) closeDrawer(); });
    $(document).on('keydown', function (e) { if (e.key === 'Escape') closeDrawer(); });

    function closeDrawer() { $('#cgDrawer').removeClass('open'); }

    // ===================== REVERT =====================

    $(document).on('click', '.cg-revert-entry', function () {
        pendingRevert = { kind: 'entry', id: $(this).data('id'), label: $(this).data('label') };
        $('#cgRevertTitle').text('Undo Change');
        $('#cgRevertBody').html('Undo <strong>' + esc(pendingRevert.label) + '</strong>?');
        $('#cgRevertPlan').html('<i class="fa-solid fa-wrench"></i> ' + esc($(this).data('plan') || '') +
            '<br><span style="color:var(--text-muted);">The undo is itself recorded, so this stays in history either way.</span>');
        new bootstrap.Modal($('#cgRevertModal')[0]).show();
    });

    $(document).on('click', '.cg-revert-batch', function (e) {
        e.stopPropagation();
        var count = $(this).data('count');
        pendingRevert = { kind: 'batch', batch: $(this).data('batch'), label: $(this).data('label') };
        $('#cgRevertTitle').text('Undo Batch');
        $('#cgRevertBody').html('Undo <strong>' + count + '</strong> change' + (count !== 1 ? 's' : '') +
            ' from <strong>' + esc(pendingRevert.label) + '</strong>?');
        $('#cgRevertPlan').html(
            '<i class="fa-solid fa-list-ol"></i> Changes are undone newest first, so overlapping edits unwind in the order they were applied.' +
            '<br><span style="color:var(--text-muted);">Entries with no undo path are skipped, not failed.</span>');
        new bootstrap.Modal($('#cgRevertModal')[0]).show();
    });

    $('#cgConfirmRevert').on('click', function () {
        if (!pendingRevert) return;
        var btn = $(this);
        var original = btn.html();
        btn.prop('disabled', true).html('<i class="fa-solid fa-spinner fa-spin"></i> Undoing...');

        var url, body;
        if (pendingRevert.kind === 'drift') {
            url = '/Changes/Resolve';
            body = { domain: pendingRevert.domain, entry: pendingRevert.entry };
        } else if (pendingRevert.kind === 'drift-group') {
            url = '/Changes/Resolve';
            body = { domain: pendingRevert.domain, entries: pendingRevert.entries };
        } else if (pendingRevert.kind === 'entry') {
            url = '/Changes/RevertEntry';
            body = { id: pendingRevert.id };
        } else {
            url = '/Changes/RevertBatch';
            body = { batch: pendingRevert.batch };
        }

        $.ajax({
            url: url, method: 'POST', contentType: 'application/json', data: JSON.stringify(body)
        }).done(function (res) {
            btn.prop('disabled', false).html(original);
            bootstrap.Modal.getInstance($('#cgRevertModal')[0]).hide();

            if (res.success) {
                if (pendingRevert.kind === 'drift') {
                    showToast('Resolved — ' + (res.summary || 'back to stock'), 'success');
                    closeDrawer();
                } else if (pendingRevert.kind === 'drift-group') {
                    var m = 'Resolved ' + res.resolved + ' of ' + res.attempted;
                    if (res.failed) m += ' — ' + res.failed + ' skipped';
                    showToast(m, res.failed ? 'info' : 'success');
                    if (res.errors && res.errors.length) console.warn('Resolve issues:', res.errors);
                    closeDrawer();
                } else if (pendingRevert.kind === 'batch') {
                    var msg = 'Undid ' + res.reverted + ' of ' + res.attempted + ' change(s)';
                    if (res.failed) msg += ' — ' + res.failed + ' could not be undone';
                    showToast(msg, res.failed ? 'info' : 'success');
                    if (res.errors && res.errors.length) console.warn('Change graph revert issues:', res.errors);
                } else {
                    showToast('Undone — ' + (res.summary || 'change reverted'), 'success');
                    closeDrawer();
                }
                reloadCurrentLevel();
            } else {
                showToast(res.error || 'Undo failed', 'error');
            }
            pendingRevert = null;
        }).fail(function () {
            btn.prop('disabled', false).html(original);
            showToast('Undo request failed', 'error');
            pendingRevert = null;
        });
    });

    // ===================== HELPERS =====================

    function domainByKey(key) {
        return domains.filter(function (d) { return d.key === key; })[0];
    }

    function pager(data) {
        if (!data.totalPages || data.totalPages <= 1) return '';
        return '<div style="font-size:12px;color:var(--text-muted);margin-top:10px;text-align:center;">' +
            'Showing page ' + data.page + ' of ' + data.totalPages +
            ' (' + Number(data.total).toLocaleString() + ' total) — narrow the filters to see the rest.</div>';
    }

    function loading(msg) {
        return '<div class="cg-empty"><i class="fa-solid fa-spinner fa-spin"></i><div>' + esc(msg) + '</div></div>';
    }

    function errorBox(msg) {
        return '<div class="cg-empty" style="color: var(--status-error);">' +
            '<i class="fa-solid fa-triangle-exclamation"></i><div>' + esc(msg) + '</div></div>';
    }

    function prettyAction(action) {
        return String(action || '').replace(/_/g, ' ');
    }

    function titleCase(s) {
        s = String(s || '');
        return s.charAt(0).toUpperCase() + s.slice(1);
    }

    function truncate(s, n) {
        s = String(s == null ? '' : s);
        return s.length > n ? s.slice(0, n - 1) + '…' : s;
    }

    function prettyJson(raw) {
        try { return JSON.stringify(JSON.parse(raw), null, 2); }
        catch (e) { return String(raw); }
    }

    function relTime(iso) {
        if (!iso) return '—';
        var then = new Date(iso);
        if (isNaN(then)) return '—';
        var secs = (Date.now() - then.getTime()) / 1000;
        if (secs < 45) return 'just now';
        if (secs < 3600) return Math.round(secs / 60) + 'm ago';
        if (secs < 86400) return Math.round(secs / 3600) + 'h ago';
        if (secs < 2592000) return Math.round(secs / 86400) + 'd ago';
        return then.toLocaleDateString();
    }

    function shortStamp(iso) {
        var d = new Date(iso);
        if (isNaN(d)) return String(iso || '');
        return d.toLocaleDateString() + ' ' + d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' });
    }

    function esc(text) {
        if (text == null) return '';
        var div = document.createElement('div');
        div.textContent = String(text);
        return div.innerHTML;
    }

    function showToast(msg, type) {
        var el = $('<div class="cg-toast ' + type + '">' + esc(msg) + '</div>');
        $('body').append(el);
        setTimeout(function () { el.fadeOut(300, function () { el.remove(); }); }, 5000);
    }

});
