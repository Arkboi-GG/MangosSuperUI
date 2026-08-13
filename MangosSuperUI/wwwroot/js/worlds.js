// MangosSuperUI — World State JS
// Suspend the mounted world, park it, resume another in its place.

$(function () {

    // ===================== STATE =====================

    var status = null;          // last /Worlds/Status payload
    var jobTimer = null;        // poll handle while a suspend/resume runs
    var hideTheatreTimer = null;

    var pendingResume = null;   // { worldId, snapshot }
    var pendingDelete = null;   // { kind: 'world'|'snapshot', worldId, folder }
    var editContext = null;     // { mode: 'edit'|'fork', worldId, snapshot }

    var FLAVORS = {
        mmo: { label: 'MMO', icon: 'fa-earth-americas', color: '#3b82c4' },
        rts: { label: 'RTS', icon: 'fa-chess-rook', color: '#f59e0b' },
        sandbox: { label: 'Sandbox', icon: 'fa-flask', color: '#a855f7' },
        archive: { label: 'Archive', icon: 'fa-box-archive', color: '#8d96a0' },
        custom: { label: 'Custom', icon: 'fa-star', color: '#22c55e' }
    };

    var GROUP_META = {
        world: { label: 'Game World', icon: 'fa-earth-americas' },
        players: { label: 'Characters', icon: 'fa-users' },
        core: { label: 'Core Source', icon: 'fa-code' }
    };

    var SNAP_KINDS = {
        'suspend': 'Suspend',
        'safety': 'Safety',
        'legacy': 'Legacy',
        'fork-origin': 'Fork'
    };

    // ===================== INIT =====================

    load();

    // ===================== LOAD =====================

    function load() {
        return $.getJSON('/Worlds/Status', function (data) {
            if (data.error) {
                $('#wsStage').html('<div class="ws-stage-loading" style="color: var(--status-error);">' +
                    '<i class="fa-solid fa-triangle-exclamation"></i> ' + esc(data.error) + '</div>');
                return;
            }
            status = data;
            renderProcStrip();
            renderStage();
            renderLineage();
            renderShelf();

            if (data.job && data.job.state === 'running') {
                renderTheatre(data.job);
                startJobPolling();
            }
        }).fail(function () {
            $('#wsStage').html('<div class="ws-stage-loading" style="color: var(--status-error);">' +
                '<i class="fa-solid fa-triangle-exclamation"></i> Failed to reach the world service</div>');
        });
    }

    function busy() {
        return !!(status && status.job && status.job.state === 'running');
    }

    // ===================== PROCESS STRIP =====================

    function renderProcStrip() {
        var h = '';
        h += procPill('mangosd', status.mangosdRunning);
        h += procPill('realmd', status.realmdRunning);
        $('#wsProcStrip').html(h);
    }

    function procPill(name, up) {
        return '<span class="ws-proc">' +
            '<span class="status-dot ' + (up ? 'online' : 'offline') + '"></span>' +
            esc(name) + '</span>';
    }

    // ===================== STAGE =====================

    function renderStage() {
        var world = status.liveWorld;

        if (!world) {
            var parked = (status.worlds || []).length;
            $('#wsStage').html(
                '<div class="ws-empty-stage">' +
                '<i class="fa-solid fa-circle-nodes ws-empty-icon"></i>' +
                '<div class="ws-empty-title">No world mounted</div>' +
                '<div class="ws-empty-sub">' +
                (parked > 0
                    ? 'The server is unloaded. Resume one of the ' + parked + ' parked world' + (parked !== 1 ? 's' : '') + ' below to bring it online.'
                    : 'Nothing has been captured yet.') +
                '</div>' +
                '</div>'
            );
            return;
        }

        var f = flavor(world.flavor);
        var st = status.stats || {};
        var running = !!status.mangosdRunning;

        var h = '<div class="ws-mounted" style="--ws-flavor: ' + f.color + ';">';

        h += '<div class="ws-orb' + (running ? ' live' : '') + '"><i class="fa-solid ' + f.icon + '"></i></div>';

        h += '<div class="ws-stage-main">';
        h += '<div class="ws-stage-eyebrow">';
        h += running
            ? '<span class="ws-live-pip"><span class="status-dot"></span> Mounted &amp; serving</span>'
            : '<span style="color: var(--status-warning);">Mounted — not serving</span>';
        h += '<span>·</span><span>' + esc(f.label) + '</span>';
        h += '</div>';

        h += '<div class="ws-stage-name">' + esc(world.name) + '</div>';

        h += '<div class="ws-stage-meta">';
        if (world.liveSinceUtc)
            h += '<span><i class="fa-solid fa-clock"></i> Mounted ' + relTime(world.liveSinceUtc) + '</span>';
        if (status.uptimeSeconds)
            h += '<span><i class="fa-solid fa-heart-pulse"></i> Up ' + duration(status.uptimeSeconds) + '</span>';
        h += '<span><i class="fa-solid fa-camera"></i> ' + (world.snapshots || []).length + ' snapshot' + ((world.snapshots || []).length !== 1 ? 's' : '') + '</span>';
        if (world.notes) h += '<span><i class="fa-solid fa-note-sticky"></i> ' + esc(world.notes) + '</span>';
        h += '</div>';

        // Live counts — what this world actually contains right now
        h += '<div class="ws-vitals">';
        h += vital(st.totalCharacters, 'Characters');
        h += vital(st.totalAccounts, 'Accounts');
        h += vital(st.customItems, 'Custom Items');
        h += vital(st.lootifierItems, 'Lootified');
        h += vital(st.auditLogRows, 'Changes Logged');
        h += '</div>';

        if (status.stalled) {
            h += '<div class="ws-stalled-banner"><i class="fa-solid fa-triangle-exclamation"></i> ' +
                'This world is mounted but mangosd is not running. Suspend it to freeze the current state, or start the server from the dashboard.' +
                '</div>';
        }

        h += '</div>'; // stage-main

        h += '<div class="ws-stage-actions">';
        h += '<button class="ws-btn ws-btn-warn ws-btn-lg" id="wsBtnSuspend"' + (busy() ? ' disabled' : '') + '>' +
            '<i class="fa-solid fa-circle-pause"></i> Suspend &amp; Unload</button>';
        h += '<button class="ws-btn ws-btn-ghost ws-fork-btn" data-world="' + esc(world.id) + '"' +
            (busy() || !(world.snapshots || []).length ? ' disabled' : '') +
            ' title="' + ((world.snapshots || []).length ? 'Branch a new world from a snapshot' : 'Suspend first to create a snapshot to fork from') + '">' +
            '<i class="fa-solid fa-code-branch"></i> Fork</button>';
        h += '<button class="ws-btn ws-btn-ghost ws-edit-btn" data-world="' + esc(world.id) + '">' +
            '<i class="fa-solid fa-pen"></i> Rename</button>';
        h += '</div>';

        h += '</div>';
        $('#wsStage').html(h);
    }

    function vital(value, label) {
        return '<div class="ws-vital">' +
            '<div class="ws-vital-value">' + (value !== undefined && value !== null ? Number(value).toLocaleString() : '—') + '</div>' +
            '<div class="ws-vital-label">' + esc(label) + '</div>' +
            '</div>';
    }

    // ===================== LINEAGE =====================

    function renderLineage() {
        var worlds = status.worlds || [];
        // Only worth drawing once something has actually branched.
        var hasForks = worlds.some(function (w) { return !!w.parentId; });
        if (!hasForks || worlds.length < 2) {
            $('#wsLineageCard').hide();
            return;
        }

        var byParent = {};
        worlds.forEach(function (w) {
            var key = w.parentId || '__root';
            (byParent[key] = byParent[key] || []).push(w);
        });

        var h = '';
        function walk(parentKey, depth) {
            (byParent[parentKey] || []).forEach(function (w) {
                var f = flavor(w.flavor);
                var isLive = w.id === status.liveWorldId;
                h += '<div class="ws-lineage-row" style="--ws-flavor: ' + f.color + ';">';
                if (depth > 0)
                    h += '<span class="ws-lineage-indent">' + repeat('    ', depth - 1) + '└── </span>';
                h += '<span class="ws-lineage-dot' + (isLive ? '' : ' suspended') + '"></span>';
                h += '<span class="ws-lineage-name ws-focus-world" data-world="' + esc(w.id) + '">' + esc(w.name) + '</span>';
                h += '<span class="ws-lineage-meta">' + esc(f.label);
                if (isLive) h += ' · mounted';
                else if (w.suspendedUtc) h += ' · suspended ' + relTime(w.suspendedUtc);
                if (w.forkedFromFolder) h += ' · forked at ' + esc(w.forkedFromFolder);
                h += '</span>';
                h += '</div>';
                walk(w.id, depth + 1);
            });
        }
        walk('__root', 0);

        // Anything whose parent was deleted still deserves a line.
        worlds.forEach(function (w) {
            if (w.parentId && !worlds.some(function (p) { return p.id === w.parentId; })) {
                var f = flavor(w.flavor);
                h += '<div class="ws-lineage-row" style="--ws-flavor: ' + f.color + ';">' +
                    '<span class="ws-lineage-dot suspended"></span>' +
                    '<span class="ws-lineage-name ws-focus-world" data-world="' + esc(w.id) + '">' + esc(w.name) + '</span>' +
                    '<span class="ws-lineage-meta">parent deleted</span></div>';
            }
        });

        $('#wsLineage').html(h);
        $('#wsLineageCard').show();
    }

    // ===================== SHELF =====================

    function renderShelf() {
        var parked = (status.worlds || []).filter(function (w) { return w.id !== status.liveWorldId; });

        $('#wsShelfCount').text(parked.length + ' parked');

        if (parked.length === 0) {
            $('#wsShelf').html(
                '<div class="ws-shelf-empty">' +
                '<i class="fa-solid fa-layer-group"></i>' +
                '<p>No parked worlds yet. Suspend the mounted world, or fork it to start a second one.</p>' +
                '</div>'
            );
            return;
        }

        // Most recently suspended first — that's the one you're most likely to want back.
        parked.sort(function (a, b) {
            return new Date(b.suspendedUtc || b.createdUtc) - new Date(a.suspendedUtc || a.createdUtc);
        });

        $('#wsShelf').html(parked.map(renderWorldCard).join(''));
    }

    function renderWorldCard(w) {
        var f = flavor(w.flavor);
        var snaps = w.snapshots || [];
        var newest = snaps[0];
        var isMaterialized = w.id === status.materializedWorldId;
        var totalBytes = snaps.reduce(function (sum, s) { return sum + (s.totalBytes || 0); }, 0);
        var stats = (newest && newest.stats) || {};

        var h = '<div class="ws-card" data-world="' + esc(w.id) + '" style="--ws-flavor: ' + f.color + ';">';
        h += '<div class="ws-card-top">';
        h += '<div class="ws-card-icon"><i class="fa-solid ' + f.icon + '"></i></div>';
        h += '<div class="ws-card-body">';

        h += '<div class="ws-card-name">' + esc(w.name);
        h += '<span class="ws-chip ws-chip-flavor">' + esc(f.label) + '</span>';
        h += '<span class="ws-chip ws-chip-state-' + esc(w.state) + '">' + esc(w.state) + '</span>';
        if (isMaterialized)
            h += '<span class="ws-chip ws-chip-materialized" title="This world&#39;s data is still in the databases — resuming it skips the import">instant resume</span>';
        h += '</div>';

        if (w.notes) h += '<div class="ws-card-notes">' + esc(w.notes) + '</div>';

        h += '<div class="ws-card-meta">';
        if (w.suspendedUtc) h += '<span><i class="fa-solid fa-circle-pause"></i> Suspended ' + relTime(w.suspendedUtc) + '</span>';
        h += '<span><i class="fa-solid fa-camera"></i> ' + snaps.length + ' snapshot' + (snaps.length !== 1 ? 's' : '') + '</span>';
        if (totalBytes) h += '<span><i class="fa-solid fa-hard-drive"></i> ' + formatBytes(totalBytes) + '</span>';
        if (stats.totalCharacters !== undefined) h += '<span><i class="fa-solid fa-user"></i> ' + stats.totalCharacters + ' characters</span>';
        if (stats.customItems !== undefined) h += '<span><i class="fa-solid fa-star"></i> ' + stats.customItems + ' custom items</span>';
        if (stats.auditLogRows !== undefined) h += '<span><i class="fa-solid fa-clipboard-list"></i> ' + stats.auditLogRows + ' changes</span>';
        h += '</div>';

        h += '<div class="ws-card-actions">';
        var canResume = snaps.length > 0 || isMaterialized;
        h += '<button class="ws-btn ws-btn-accent ws-resume-btn" data-world="' + esc(w.id) + '"' +
            (busy() || !canResume ? ' disabled' : '') + '>' +
            '<i class="fa-solid fa-play"></i> Resume</button>';
        if (snaps.length)
            h += '<button class="ws-btn ws-btn-ghost ws-fork-btn" data-world="' + esc(w.id) + '"' + (busy() ? ' disabled' : '') + '>' +
                '<i class="fa-solid fa-code-branch"></i> Fork</button>';
        h += '<button class="ws-btn ws-btn-ghost ws-edit-btn" data-world="' + esc(w.id) + '"><i class="fa-solid fa-pen"></i> Edit</button>';
        if (snaps.length)
            h += '<button class="ws-btn ws-btn-ghost ws-toggle-snaps"><i class="fa-solid fa-clock-rotate-left"></i> History</button>';
        h += '<button class="ws-btn ws-btn-ghost ws-delete-world" data-world="' + esc(w.id) + '" style="color: var(--status-error); border-color: var(--status-error);"' +
            (busy() ? ' disabled' : '') + '><i class="fa-solid fa-trash"></i></button>';
        h += '</div>';

        // Snapshot history drawer
        h += '<div class="ws-snaps">' + snaps.map(function (s) { return renderSnapRow(w, s); }).join('') + '</div>';

        h += '</div></div></div>';
        return h;
    }

    function renderSnapRow(w, s) {
        var kindLabel = SNAP_KINDS[s.kind] || s.kind;
        var groups = (s.groups || []).map(function (g) {
            return (GROUP_META[g] || { label: g }).label;
        }).join(', ');

        var h = '<div class="ws-snap" data-folder="' + esc(s.folder) + '">';
        h += '<span class="ws-snap-kind ws-snap-kind-' + esc(s.kind) + '">' + esc(kindLabel) + '</span>';
        h += '<span class="ws-snap-when" title="' + esc(s.folder) + '">' + esc(shortStamp(s.takenUtc)) + '</span>';
        h += '<span class="ws-snap-label ws-snap-edit-label" data-world="' + esc(w.id) + '" data-folder="' + esc(s.folder) + '" title="Click to retitle">' +
            esc(s.label || 'add a note…') + '</span>';
        h += '<span class="ws-snap-right">';
        if (groups) h += '<span class="ws-snap-size">' + esc(groups) + '</span>';
        if (s.totalBytes) h += '<span class="ws-snap-size">' + formatBytes(s.totalBytes) + '</span>';
        h += '<button class="ws-btn ws-btn-ghost ws-resume-btn" data-world="' + esc(w.id) + '" data-snapshot="' + esc(s.folder) + '"' +
            (busy() ? ' disabled' : '') + ' title="Resume this exact point"><i class="fa-solid fa-play"></i></button>';
        h += '<button class="ws-btn ws-btn-ghost ws-delete-snap" data-world="' + esc(w.id) + '" data-folder="' + esc(s.folder) + '"' +
            (busy() ? ' disabled' : '') + ' style="color: var(--status-error);"><i class="fa-solid fa-trash"></i></button>';
        h += '</span></div>';
        return h;
    }

    $(document).on('click', '.ws-toggle-snaps', function () {
        $(this).closest('.ws-card').toggleClass('open');
    });

    $(document).on('click', '.ws-focus-world', function () {
        var id = $(this).data('world');
        var card = $('.ws-card[data-world="' + id + '"]');
        if (card.length) {
            card[0].scrollIntoView({ behavior: 'smooth', block: 'center' });
            card.addClass('open');
        }
    });

    // ===================== JOB THEATRE =====================

    function startJobPolling() {
        if (jobTimer) return;
        jobTimer = setInterval(pollJob, 1200);
    }

    function stopJobPolling() {
        if (jobTimer) { clearInterval(jobTimer); jobTimer = null; }
    }

    function pollJob() {
        $.getJSON('/Worlds/Job', function (data) {
            var job = data.job;
            if (!job) { stopJobPolling(); $('#wsTheatre').hide(); return; }

            renderTheatre(job);

            if (job.state !== 'running') {
                stopJobPolling();
                if (job.state === 'done') {
                    showToast(job.title + ' — complete', 'success');
                    // A successful mount leaves the theatre up briefly as confirmation.
                    clearTimeout(hideTheatreTimer);
                    hideTheatreTimer = setTimeout(function () { $('#wsTheatre').fadeOut(300); }, 12000);
                } else {
                    showToast(job.title + ' failed: ' + (job.error || 'unknown error'), 'error');
                }
                load();
            }
        });
    }

    function renderTheatre(job) {
        clearTimeout(hideTheatreTimer);

        var icon = job.state === 'running' ? 'fa-spinner fa-spin'
            : job.state === 'done' ? 'fa-circle-check' : 'fa-circle-exclamation';
        var color = job.state === 'running' ? 'var(--accent)'
            : job.state === 'done' ? 'var(--status-online)' : 'var(--status-error)';

        var done = (job.steps || []).filter(function (s) { return s.state === 'done' || s.state === 'skipped'; }).length;

        var h = '<div class="ws-theatre-title"><i class="fa-solid ' + icon + '" style="color: ' + color + ';"></i> ' + esc(job.title) + '</div>';
        h += '<div class="ws-theatre-sub">' + done + ' of ' + (job.steps || []).length + ' steps · started ' + relTime(job.startedUtc) + '</div>';

        h += '<div class="ws-steps">';
        (job.steps || []).forEach(function (s) {
            var si = s.state === 'running' ? 'fa-spinner fa-spin'
                : s.state === 'done' ? 'fa-circle-check'
                    : s.state === 'failed' ? 'fa-circle-xmark'
                        : s.state === 'skipped' ? 'fa-circle-minus' : 'fa-circle';
            h += '<div class="ws-step ' + esc(s.state) + '">';
            h += '<span class="ws-step-icon"><i class="fa-solid ' + si + '"></i></span>';
            h += '<span>' + esc(s.label) + '</span>';
            if (s.detail) h += '<span class="ws-step-detail">' + esc(s.detail) + '</span>';
            h += '</div>';
        });
        h += '</div>';

        if (job.error) h += '<div class="ws-theatre-error"><i class="fa-solid fa-triangle-exclamation"></i> ' + esc(job.error) + '</div>';

        $('#wsTheatre')
            .removeClass('done failed')
            .addClass(job.state === 'done' ? 'done' : job.state === 'failed' ? 'failed' : '')
            .html(h)
            .show();
    }

    // ===================== SUSPEND =====================

    $(document).on('click', '#wsBtnSuspend', function () {
        if (!status.liveWorld) return;
        $('#wsSuspendName').text(status.liveWorld.name);
        $('#wsSuspendLabel').val('');
        new bootstrap.Modal($('#wsSuspendModal')[0]).show();
    });

    $('#wsConfirmSuspend').on('click', function () {
        var btn = $(this);
        btn.prop('disabled', true).html('<i class="fa-solid fa-spinner fa-spin"></i> Suspending...');

        post('/Worlds/Suspend', { label: $('#wsSuspendLabel').val().trim() }, function (result) {
            btn.prop('disabled', false).html('<i class="fa-solid fa-circle-pause"></i> Suspend &amp; Unload');
            bootstrap.Modal.getInstance($('#wsSuspendModal')[0]).hide();

            if (result.success) {
                renderTheatre(result.job);
                startJobPolling();
                load();
            } else {
                showToast(result.error || 'Suspend failed', 'error');
            }
        });
    });

    // ===================== RESUME / SWAP CEREMONY =====================

    $(document).on('click', '.ws-resume-btn', function (e) {
        e.stopPropagation();
        var worldId = $(this).data('world');
        var snapshot = $(this).data('snapshot') || null;

        var target = findWorld(worldId);
        if (!target) return;

        var outgoing = status.liveWorld;
        var isSwap = !!outgoing;

        pendingResume = { worldId: worldId, snapshot: snapshot };

        $('#wsResumeTitle').text(isSwap ? 'Swap Worlds' : 'Resume World');
        $('#wsConfirmResumeLabel').text(isSwap ? 'Swap' : 'Resume');

        // ---- The exchange ----
        var tf = flavor(target.flavor);
        var x = '';

        if (isSwap) {
            var of_ = flavor(outgoing.flavor);
            x += '<div class="ws-exchange-side" style="--ws-flavor: ' + of_.color + ';">';
            x += '<div class="ws-exchange-role">Currently mounted</div>';
            x += '<div class="ws-exchange-name">' + esc(outgoing.name) + '</div>';
            x += '<div class="ws-exchange-fate out"><i class="fa-solid fa-circle-pause"></i> Will be suspended</div>';
            x += '<div class="ws-exchange-note">Frozen to a new snapshot first — nothing is lost.</div>';
            x += '</div>';
        } else {
            x += '<div class="ws-exchange-side">';
            x += '<div class="ws-exchange-role">Currently mounted</div>';
            x += '<div class="ws-exchange-name" style="color: var(--text-muted);">Nothing</div>';
            x += '<div class="ws-exchange-note">The server is unloaded.</div>';
            x += '</div>';
        }

        x += '<div class="ws-exchange-arrow"><i class="fa-solid fa-arrow-right-long"></i></div>';

        x += '<div class="ws-exchange-side" style="--ws-flavor: ' + tf.color + ';">';
        x += '<div class="ws-exchange-role">Will be mounted</div>';
        x += '<div class="ws-exchange-name">' + esc(target.name) + '</div>';
        x += '<div class="ws-exchange-fate in"><i class="fa-solid fa-play"></i> Will be resumed</div>';
        var instant = target.id === status.materializedWorldId;
        x += '<div class="ws-exchange-note">' +
            (instant ? 'Still in the databases — the import is skipped.' : 'Restored from snapshot, then booted.') +
            '</div>';
        x += '</div>';

        $('#wsExchange').html(x);

        // ---- Step preview ----
        var steps = [];
        if (isSwap) {
            steps.push(['fa-power-off', 'Stop mangosd & realmd']);
            steps.push(['fa-snowflake', 'Freeze “' + outgoing.name + '” and park it']);
        } else {
            steps.push(['fa-power-off', 'Confirm mangosd & realmd are stopped']);
        }
        if (instant) {
            steps.push(['fa-forward', 'Skip import — “' + target.name + '” is already in place']);
        } else {
            steps.push(['fa-database', 'Restore world, characters & core from snapshot']);
        }
        steps.push(['fa-play', 'Boot realmd & mangosd']);

        $('#wsResumeSteps').html(steps.map(function (s) {
            return '<div class="ws-step-preview-item"><i class="fa-solid ' + s[0] + '"></i> ' + esc(s[1]) + '</div>';
        }).join(''));

        // ---- Snapshot picker (only when there's a real choice) ----
        var snaps = target.snapshots || [];
        if (snaps.length > 1 && !snapshot) {
            $('#wsSnapshotSelect').html(snaps.map(function (s) {
                return '<option value="' + esc(s.folder) + '">' +
                    esc(shortStamp(s.takenUtc)) + ' — ' + esc(s.label || (SNAP_KINDS[s.kind] || s.kind)) + '</option>';
            }).join(''));
            $('#wsSnapshotPick').show();
        } else {
            $('#wsSnapshotPick').hide();
        }

        new bootstrap.Modal($('#wsResumeModal')[0]).show();
    });

    $('#wsConfirmResume').on('click', function () {
        if (!pendingResume) return;
        var btn = $(this);
        var original = btn.html();
        btn.prop('disabled', true).html('<i class="fa-solid fa-spinner fa-spin"></i> Starting...');

        if ($('#wsSnapshotPick').is(':visible'))
            pendingResume.snapshot = $('#wsSnapshotSelect').val();

        post('/Worlds/Resume', pendingResume, function (result) {
            btn.prop('disabled', false).html(original);
            bootstrap.Modal.getInstance($('#wsResumeModal')[0]).hide();

            if (result.success) {
                renderTheatre(result.job);
                startJobPolling();
                load();
            } else {
                showToast(result.error || 'Resume failed', 'error');
            }
            pendingResume = null;
        });
    });

    // ===================== FORK / EDIT =====================

    $(document).on('click', '.ws-fork-btn', function (e) {
        e.stopPropagation();
        var world = findWorld($(this).data('world'));
        if (!world) return;

        var newest = (world.snapshots || [])[0];
        editContext = { mode: 'fork', worldId: world.id, snapshot: newest ? newest.folder : null };

        $('#wsEditTitle').text('Fork World');
        $('#wsEditIcon').attr('class', 'fa-solid fa-code-branch');
        $('#wsEditLead').show().html(
            'Branches a new world from <strong>' + esc(world.name) + '</strong>' +
            (newest ? ' at <strong>' + esc(shortStamp(newest.takenUtc)) + '</strong>' : '') +
            '. The fork shares that snapshot on disk, so nothing is copied until you suspend it with changes of its own.');
        $('#wsEditName').val(world.name + ' fork');
        $('#wsEditNotes').val('');
        setFlavor(world.flavor);

        new bootstrap.Modal($('#wsEditModal')[0]).show();
    });

    $(document).on('click', '.ws-edit-btn', function (e) {
        e.stopPropagation();
        var world = findWorld($(this).data('world'));
        if (!world) return;

        editContext = { mode: 'edit', worldId: world.id };

        $('#wsEditTitle').text('Edit World');
        $('#wsEditIcon').attr('class', 'fa-solid fa-pen');
        $('#wsEditLead').hide();
        $('#wsEditName').val(world.name);
        $('#wsEditNotes').val(world.notes || '');
        setFlavor(world.flavor);

        new bootstrap.Modal($('#wsEditModal')[0]).show();
    });

    $(document).on('click', '.ws-flavor', function () {
        $('.ws-flavor').removeClass('active');
        $(this).addClass('active');
    });

    function setFlavor(key) {
        $('.ws-flavor').removeClass('active');
        var el = $('.ws-flavor[data-flavor="' + (key || 'mmo') + '"]');
        (el.length ? el : $('.ws-flavor[data-flavor="mmo"]')).addClass('active');
    }

    $('#wsConfirmEdit').on('click', function () {
        if (!editContext) return;
        var name = $('#wsEditName').val().trim();
        if (!name) { showToast('Give the world a name', 'error'); return; }

        var payload = {
            worldId: editContext.worldId,
            name: name,
            flavor: $('.ws-flavor.active').data('flavor') || 'mmo',
            notes: $('#wsEditNotes').val().trim()
        };

        var btn = $(this);
        var original = btn.html();
        btn.prop('disabled', true).html('<i class="fa-solid fa-spinner fa-spin"></i>');

        var url = '/Worlds/Update';
        if (editContext.mode === 'fork') {
            url = '/Worlds/Fork';
            payload.snapshot = editContext.snapshot;
        }

        post(url, payload, function (result) {
            btn.prop('disabled', false).html(original);
            bootstrap.Modal.getInstance($('#wsEditModal')[0]).hide();

            if (result.success) {
                showToast(editContext.mode === 'fork' ? 'Forked “' + name + '”' : 'World updated', 'success');
                load();
            } else {
                showToast(result.error || 'Save failed', 'error');
            }
            editContext = null;
        });
    });

    // ===================== SNAPSHOT LABEL =====================

    $(document).on('click', '.ws-snap-edit-label', function (e) {
        e.stopPropagation();
        var el = $(this);
        var current = el.text() === 'add a note…' ? '' : el.text();
        var next = prompt('Note for this snapshot:', current);
        if (next === null) return;

        post('/Worlds/SnapshotLabel', {
            worldId: el.data('world'),
            folder: el.data('folder'),
            label: next
        }, function (result) {
            if (result.success) el.text(next || 'add a note…');
            else showToast(result.error || 'Failed to update note', 'error');
        });
    });

    // ===================== DELETE =====================

    $(document).on('click', '.ws-delete-world', function (e) {
        e.stopPropagation();
        var world = findWorld($(this).data('world'));
        if (!world) return;

        pendingDelete = { kind: 'world', worldId: world.id };
        $('#wsDeleteTitle').text('Delete World');
        $('#wsDeleteBody').html(
            'Delete <strong>' + esc(world.name) + '</strong> and its ' + (world.snapshots || []).length +
            ' snapshot(s)? Snapshots shared with a fork are kept on disk.');
        new bootstrap.Modal($('#wsDeleteModal')[0]).show();
    });

    $(document).on('click', '.ws-delete-snap', function (e) {
        e.stopPropagation();
        var worldId = $(this).data('world');
        var folder = $(this).data('folder');

        pendingDelete = { kind: 'snapshot', worldId: worldId, folder: folder };
        $('#wsDeleteTitle').text('Delete Snapshot');
        $('#wsDeleteBody').html('Delete snapshot <strong>' + esc(folder) + '</strong>?');
        new bootstrap.Modal($('#wsDeleteModal')[0]).show();
    });

    $('#wsConfirmDelete').on('click', function () {
        if (!pendingDelete) return;
        var btn = $(this);
        var original = btn.html();
        btn.prop('disabled', true).html('<i class="fa-solid fa-spinner fa-spin"></i>');

        var url = pendingDelete.kind === 'world' ? '/Worlds/DeleteWorld' : '/Worlds/DeleteSnapshot';

        post(url, pendingDelete, function (result) {
            btn.prop('disabled', false).html(original);
            bootstrap.Modal.getInstance($('#wsDeleteModal')[0]).hide();

            if (result.success) {
                showToast('Deleted', 'success');
                load();
            } else {
                showToast(result.error || 'Delete failed', 'error');
            }
            pendingDelete = null;
        });
    });

    // ===================== HELPERS =====================

    function post(url, body, done) {
        $.ajax({
            url: url,
            method: 'POST',
            contentType: 'application/json',
            data: JSON.stringify(body),
            success: done,
            error: function () { done({ success: false, error: 'Request failed' }); }
        });
    }

    function findWorld(id) {
        return (status.worlds || []).filter(function (w) { return w.id === id; })[0];
    }

    function flavor(key) {
        return FLAVORS[key] || FLAVORS.custom;
    }

    function repeat(str, n) {
        var out = '';
        for (var i = 0; i < n; i++) out += str;
        return out;
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

    function duration(seconds) {
        if (!seconds) return '—';
        var d = Math.floor(seconds / 86400);
        var h = Math.floor((seconds % 86400) / 3600);
        var m = Math.floor((seconds % 3600) / 60);
        if (d) return d + 'd ' + h + 'h';
        if (h) return h + 'h ' + m + 'm';
        return m + 'm';
    }

    function shortStamp(iso) {
        var d = new Date(iso);
        if (isNaN(d)) return String(iso || '');
        return d.toLocaleDateString() + ' ' + d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
    }

    function formatBytes(bytes) {
        if (!bytes || bytes <= 0) return '0 B';
        if (bytes < 1024) return bytes + ' B';
        if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + ' KB';
        if (bytes < 1024 * 1024 * 1024) return (bytes / (1024 * 1024)).toFixed(1) + ' MB';
        return (bytes / (1024 * 1024 * 1024)).toFixed(1) + ' GB';
    }

    function esc(text) {
        if (text == null) return '';
        var div = document.createElement('div');
        div.textContent = String(text);
        return div.innerHTML;
    }

    function showToast(msg, type) {
        var el = $('<div class="ws-toast ' + type + '">' + esc(msg) + '</div>');
        $('body').append(el);
        setTimeout(function () { el.fadeOut(300, function () { el.remove(); }); }, 5000);
    }

});
