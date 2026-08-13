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

    // ===================== INIT =====================

    loadOverview();

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
        if (target === 'domains') { nav = { level: 'domains', domain: null, domainLabel: null, batch: null, batchLabel: null }; loadOverview(); }
        else if (target === 'batches') { nav.level = 'batches'; nav.batch = null; nav.batchLabel = null; loadBatches(nav.domain, nav.domainLabel); }
    });

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

        var url = pendingRevert.kind === 'entry' ? '/Changes/RevertEntry' : '/Changes/RevertBatch';
        var body = pendingRevert.kind === 'entry' ? { id: pendingRevert.id } : { batch: pendingRevert.batch };

        $.ajax({
            url: url, method: 'POST', contentType: 'application/json', data: JSON.stringify(body)
        }).done(function (res) {
            btn.prop('disabled', false).html(original);
            bootstrap.Modal.getInstance($('#cgRevertModal')[0]).hide();

            if (res.success) {
                if (pendingRevert.kind === 'batch') {
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
