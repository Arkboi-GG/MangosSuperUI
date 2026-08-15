// MangosSuperUI — World State JS
// Suspend the mounted world, park it, resume another in its place.

$(function () {

    // ===================== STATE =====================

    var status = null;          // last /Worlds/Status payload
    var jobTimer = null;        // poll handle while a suspend/resume runs
    var hideTheatreTimer = null;
    var createOptions = null;   // server-owned RTS profile, fields and eligible sources
    var optionsRequest = null;
    var resumePreflightSerial = 0;

    var pendingResume = null;   // { worldId, snapshot, target, preflight }
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
        'fork-origin': 'Fork',
        'rts-seed': 'RTS Seed'
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
                '<div class="ws-empty-title">No world serving players</div>' +
                '<div class="ws-empty-sub">' +
                (parked > 0
                    ? 'World data may still be materialized, but no world is active. Resume one of the ' + parked + ' parked world' + (parked !== 1 ? 's' : '') + ' below.'
                    : 'No saved world exists yet. Create a parked RTS campaign from an eligible v2 snapshot when one becomes available.') +
                '</div>' +
                '<button class="ws-btn ws-btn-accent ws-new-rts-inline" style="margin-top: 13px;"><i class="fa-solid fa-plus"></i> New RTS World</button>' +
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
                'This world is mounted but mangosd is not running, so no world is serving players. Suspend it to capture the current state or use the normal server controls to start it.' +
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
                '<p>No parked worlds yet. Suspend the mounted world, or create a zero-roster RTS campaign from an eligible v2 snapshot.</p>' +
                '<button class="ws-btn ws-btn-accent ws-new-rts-inline"><i class="fa-solid fa-plus"></i> New RTS World</button>' +
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
        if (newest) {
            var legacy = (newest.schemaVersion || 1) < 2 || !(newest.artifacts || []).length;
            h += '<span class="ws-status-badge ' + (legacy ? 'warn' : 'good') + '" title="' +
                (legacy ? 'Legacy snapshots are structurally validated during resume; historical checksums are unavailable.' : 'Snapshot v2 includes a checksummed artifact manifest.') + '">' +
                '<i class="fa-solid ' + (legacy ? 'fa-triangle-exclamation' : 'fa-shield-halved') + '"></i> ' +
                (legacy ? 'v1 legacy' : 'v2 checksummed') + '</span>';
        }
        if (isMaterialized)
            h += '<span class="ws-chip ws-chip-materialized" title="This world&#39;s data is still in the databases; resume can skip the import unless Force full restore is selected.">materialized</span>';
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
        var legacy = (s.schemaVersion || 1) < 2 || !(s.artifacts || []).length;
        h += '<span class="ws-status-badge ' + (legacy ? 'warn' : 'good') + '">' + (legacy ? 'v1' : 'v2') + '</span>';
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
                if (job.state === 'done' && (job.kind === 'suspend' || job.kind === 'resume' || job.kind === 'swap' || job.kind === 'create-rts'))
                    createOptions = null;
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

    // ===================== CREATE PARKED RTS WORLD =====================

    $(document).on('click', '#wsBtnCreateRts, .ws-new-rts-inline', function () {
        $('#wsCreateName').val('RTS Campaign');
        $('#wsCreateNotes').val('');
        $('#wsCreateSource').html('<option>Loading eligible snapshots...</option>').prop('disabled', true);
        $('#wsCreateSourceStatus').html('');
        $('#wsCreateValidation').html('');
        $('#wsConfirmCreateRts').prop('disabled', true);
        new bootstrap.Modal($('#wsCreateRtsModal')[0]).show();

        ensureCreateOptions(function (error) {
            if (error) {
                $('#wsCreateSource').html('<option>Unable to load sources</option>');
                $('#wsRateFields').html('<div class="ws-message-list blockers">' + esc(error) + '</div>');
                renderCreateValidation();
                return;
            }
            applyCreateDefaults();
            renderCreateSources();
            renderCreateValidation();
        });
    });

    function ensureCreateOptions(done) {
        if (createOptions) { done(null); return; }
        if (optionsRequest) {
            optionsRequest.done(function () { done(createOptions ? null : 'World creation options were unavailable.'); })
                .fail(function () { done('Failed to load the RTS profile.'); });
            return;
        }
        optionsRequest = $.getJSON('/Worlds/CreateOptions')
            .done(function (data) {
                if (data && data.success) createOptions = data;
                else createOptions = null;
            })
            .always(function () { optionsRequest = null; });
        optionsRequest.done(function (data) { done(createOptions ? null : (data && data.error) || 'World creation options were unavailable.'); })
            .fail(function () { done('Failed to load the RTS profile.'); });
    }

    function applyCreateDefaults() {
        var profileId = (createOptions && (createOptions.defaultProfileId || createOptions.profileId)) || 'rts-r2-v1';
        renderProfileOptions('#wsCreateProfile', profileId);
        applyConfigurationFields('create', profileDefaults(profileId));
    }

    function renderCreateSources() {
        var sources = createOptions.sources || [];
        if (!sources.length) {
            $('#wsCreateSource').html('<option value="">No snapshots available</option>').prop('disabled', true);
            renderCreateSourceStatus();
            return;
        }
        var firstEligible = -1;
        var html = sources.map(function (source, index) {
            if (firstEligible < 0 && source.eligible) firstEligible = index;
            return '<option value="' + index + '">' +
                esc(source.displayName || ((source.worldName || 'World') + ' — ' + (source.snapshot || 'snapshot'))) +
                (source.eligible ? '' : ' — unavailable') + '</option>';
        }).join('');
        $('#wsCreateSource').html(html).prop('disabled', false);
        if (firstEligible >= 0) $('#wsCreateSource').val(String(firstEligible));
        else $('#wsCreateSource').val('0');
        renderCreateSourceStatus();
    }

    function selectedCreateSource() {
        if (!createOptions) return null;
        var index = parseInt($('#wsCreateSource').val(), 10);
        return isNaN(index) ? null : (createOptions.sources || [])[index] || null;
    }

    function renderCreateSourceStatus() {
        var source = selectedCreateSource();
        if (!source) {
            $('#wsCfgRealmIdText').text('Select a source');
            var all = (createOptions && createOptions.sources) || [];
            var reasons = all.reduce(function (out, item) {
                if (item.reason && out.indexOf(item.reason) < 0) out.push(item.reason);
                return out;
            }, []);
            $('#wsCreateSourceStatus').html(
                '<span class="ws-status-badge bad"><i class="fa-solid fa-circle-xmark"></i> No eligible v2 source</span>' +
                messageList(reasons.length ? reasons : ['Create or refresh a v2 snapshot before starting an RTS campaign.'], 'blockers'));
            return;
        }
        var legacy = !!source.legacy || Number(source.schemaVersion || 1) < 2;
        $('#wsCfgRealmIdText').text(source.realmId ? 'Realm ' + source.realmId : 'Unavailable');
        var h = '<div class="ws-preflight-badges">';
        h += statusBadge(legacy ? 'warn' : 'good', legacy ? 'fa-triangle-exclamation' : 'fa-shield-halved', legacy ? 'v1 legacy' : 'v2 manifest');
        h += statusBadge(source.eligible ? 'good' : 'bad', source.eligible ? 'fa-circle-check' : 'fa-circle-xmark', source.integrity || 'integrity unknown');
        h += statusBadge(configTone(source.configStatus), 'fa-sliders', 'config: ' + (source.configStatus || 'unknown'));
        h += statusBadge(source.realmId ? 'good' : 'bad', 'fa-earth-americas', source.realmId ? 'RealmID ' + source.realmId + ' · inherited' : 'RealmID unavailable');
        if (createOptions && createOptions.namePoolEligible !== undefined)
            h += statusBadge('good', 'fa-signature', formatNumber(createOptions.namePoolEligible) + ' eligible bot names');
        h += '</div>';
        h += messageList(source.blockers || [], 'blockers');
        h += messageList(source.warnings || [], 'warnings');
        $('#wsCreateSourceStatus').html(h);
    }

    function renderProfileRateFields(selector, scope, configuration) {
        var fields = (createOptions && (createOptions.rateFields || createOptions.fields)) || [];
        var rates = (configuration && configuration.rates) || {};
        if (!fields.length) {
            $(selector).html('<div class="text-muted" style="font-size: 12px;">No profile tuning fields were returned by the server.</div>');
            return;
        }
        var sections = {};
        fields.forEach(function (field) {
            var section = field.section || 'Settings';
            (sections[section] = sections[section] || []).push(field);
        });
        var html = Object.keys(sections).map(function (section) {
            return '<div class="ws-rate-section"><div class="ws-rate-heading">' + esc(section) + '</div><div class="ws-rate-grid">' +
                sections[section].map(function (field) {
                    var supplied = rates[field.key];
                    var value = supplied !== undefined && supplied !== null ? supplied : field.defaultValue;
                    return '<label class="ws-rate-field"><span>' + esc(field.label || field.key) + '</span>' +
                        '<input class="form-input" type="number" data-' + scope + '-rate="' + esc(field.key) + '"' +
                        ' min="' + esc(field.min) + '" max="' + esc(field.max) + '" step="' + esc(field.step || 1) + '" value="' + esc(value) + '" />' +
                        '<small title="' + esc(field.help || '') + '">' + esc(field.help || field.key) + '</small></label>';
                }).join('') + '</div></div>';
        }).join('');
        $(selector).html(html);
    }

    function availableProfiles() {
        return (createOptions && createOptions.profiles) || [];
    }

    function profileDefinition(profileId) {
        return availableProfiles().filter(function (profile) {
            return String(objectValue(profile, 'id') || '') === String(profileId || '');
        })[0] || null;
    }

    function profileDefaults(profileId) {
        var profile = profileDefinition(profileId);
        var defaults = profile && objectValue(profile, 'defaults');
        if (defaults) return mergeConfiguration({}, defaults);
        var fallback = mergeConfiguration({}, (createOptions && createOptions.defaults) || {});
        fallback.profileId = profileId || objectValue(fallback, 'profileId') || 'rts-r2-v1';
        return fallback;
    }

    function renderProfileOptions(selector, selectedProfileId) {
        var profiles = availableProfiles();
        if (!profiles.length) {
            $(selector).html('<option value="rts-r2-v1">RTS - Honor + Heroes</option>')
                .val(selectedProfileId || 'rts-r2-v1');
            return;
        }
        $(selector).html(profiles.map(function (profile) {
            var id = objectValue(profile, 'id');
            return '<option value="' + esc(id) + '">' + esc(objectValue(profile, 'label') || id) + '</option>';
        }).join('')).val(selectedProfileId);
        if (!$(selector).val()) $(selector).val(objectValue(profiles[0], 'id'));
    }

    function selectedProfileId(scope) {
        var selector = scope === 'resume' ? '#wsResumeProfile' : '#wsCreateProfile';
        return $(selector).val() || (createOptions && (createOptions.defaultProfileId || createOptions.profileId)) || 'rts-r2-v1';
    }

    function applyConfigurationFields(scope, configuration) {
        var prefix = scope === 'resume' ? '#wsResumeCfg' : '#wsCfg';
        var profileSelector = scope === 'resume' ? '#wsResumeProfile' : '#wsCreateProfile';
        var profileId = objectValue(configuration, 'profileId') || 'rts-r2-v1';
        $(profileSelector).val(profileId);
        setNumber(prefix + 'PlayerLimit', objectValue(configuration, 'playerLimit'), 2600);
        setNumber(prefix + 'PlayerHardLimit', objectValue(configuration, 'playerHardLimit'), 2600);
        setNumber(prefix + 'LoginPerTick', objectValue(configuration, 'loginPerTick'), 0);
        setNumber(prefix + 'StateFlushMs', objectValue(configuration, 'stateFlushMs'), 30000);
        setNumber(prefix + 'AllianceBotCap', objectValue(configuration, 'allianceBotCap'), 1250);
        setNumber(prefix + 'HordeBotCap', objectValue(configuration, 'hordeBotCap'), 1250);
        renderProfileRateFields(scope === 'resume' ? '#wsResumeRateFields' : '#wsRateFields', scope, configuration);
        renderR2Fields(scope === 'resume' ? '#wsResumeR2Fields' : '#wsCreateR2Fields', scope, configuration);
    }

    function renderR2Fields(selector, scope, configuration) {
        var profileId = objectValue(configuration, 'profileId') || selectedProfileId(scope);
        var profile = profileDefinition(profileId);
        var description = profile && objectValue(profile, 'description');
        var defaults = profileDefaults(profileId);
        var rules = objectValue(configuration, 'heroRules') || objectValue(defaults, 'heroRules') || [];
        function setting(name, fallback) {
            var value = objectValue(configuration, name);
            if (value === undefined || value === null) value = objectValue(defaults, name);
            return value === undefined || value === null ? fallback : value;
        }
        function numberField(label, suffix, value, min, max) {
            return '<label><span>' + esc(label) + '</span><input class="form-input" type="number" id="' +
                (scope === 'resume' ? 'wsResumeCfg' : 'wsCfg') + suffix + '" min="' + min + '" max="' + max +
                '" step="1" value="' + esc(value) + '" /></label>';
        }

        var html = '<div class="ws-r2-heading"><div><strong>Honor + Heroes</strong><small>' +
            esc(description || 'Faction combat funds persistent hero declaration, promotion, and revival.') +
            '</small></div>' + statusBadge('good', 'fa-bolt', 'active at next boot') + '</div>';
        html += '<div class="ws-r2-grid">' +
            numberField('Player kill Honor', 'HonorWeightPlayer', setting('honorWeightPlayer', 10), 0, 1000000) +
            numberField('Bot kill Honor', 'HonorWeightBot', setting('honorWeightBot', 5), 0, 1000000) +
            numberField('Faction NPC Honor', 'HonorWeightFactionNpc', setting('honorWeightFactionNpc', 1), 0, 1000000) +
            numberField('Faction elite Honor', 'HonorWeightFactionElite', setting('honorWeightFactionElite', 3), 0, 1000000) +
            numberField('Fixed hero slots', 'HeroSlotsFixed', setting('heroSlotsFixed', 4), 1, 127) +
            '<label class="ws-r2-check"><input type="checkbox" id="' + (scope === 'resume' ? 'wsResumeCfg' : 'wsCfg') +
            'SuppressBotHonorHistory"' + (setting('suppressBotHonorHistory', true) ? ' checked' : '') +
            ' /><span>Suppress bot-vs-bot vanilla HK history</span></label>' +
            '<label class="ws-r2-check"><input type="checkbox" id="' + (scope === 'resume' ? 'wsResumeCfg' : 'wsCfg') +
            'FactionWideBotControl"' + (setting('factionWideBotControl', true) ? ' checked' : '') +
            ' /><span>Enable same-faction bot control</span></label></div>';
        html += '<div class="ws-field-help"><strong>Hero eligibility is bot-only server law.</strong> Human characters can earn and spend faction Honor, but cannot occupy a hero slot.</div>';
        html += '<table class="ws-hero-rules"><thead><tr><th>Level</th><th>Enter cost</th><th>Revive</th><th>Spell ID</th><th>Scale %</th><th>Damage %</th></tr></thead><tbody>';
        rules.forEach(function (rule, index) {
            var level = objectValue(rule, 'heroLevel') || index + 1;
            function cell(field, value, min, max, readOnly) {
                return '<td><input class="form-input" type="number" data-' + scope + '-hero="' + field +
                    '" min="' + min + '" max="' + max + '" step="1" value="' + esc(value) + '"' +
                    (readOnly ? ' readonly aria-readonly="true"' : '') + ' /></td>';
            }
            html += '<tr data-' + scope + '-hero-level="' + esc(level) + '"><td><strong>' + esc(level) + '</strong></td>' +
                cell('honorCost', objectValue(rule, 'honorCost'), 0, 2147483647) +
                cell('reviveFee', objectValue(rule, 'reviveFee'), 0, 2147483647) +
                cell('spellId', objectValue(rule, 'spellId'), 51001, 51005, true) +
                cell('scalePercent', objectValue(rule, 'scalePercent'), 100, 200) +
                cell('damagePercent', objectValue(rule, 'damagePercent'), 100, 200) + '</tr>';
        });
        html += '</tbody></table><div class="ws-field-help">Enter cost is the declaration cost at level 1 and promotion cost for levels 2-5. ' +
            'Reserved spell IDs 51001-51005 are native passive auras in this RTS world only. World State writes their configured scale/damage bonuses into the staged restore artifact and validates the matching save-bound rule rows at boot.</div>';
        $(selector).html(html);
    }

    function switchProfile(scope, profileId) {
        var current = collectConfiguration(scope);
        var next = profileDefaults(profileId);
        next.profileId = profileId;
        next.realmId = current.realmId;
        next.playerLimit = current.playerLimit;
        next.playerHardLimit = current.playerHardLimit;
        next.loginPerTick = current.loginPerTick;
        next.stateFlushMs = current.stateFlushMs;
        next.allianceBotCap = current.allianceBotCap;
        next.hordeBotCap = current.hordeBotCap;
        next.rates = current.rates;
        applyConfigurationFields(scope, next);
    }

    function collectConfiguration(scope) {
        var prefix = scope === 'resume' ? '#wsResumeCfg' : '#wsCfg';
        var profileId = selectedProfileId(scope);
        var defaults = profileDefaults(profileId);
        var realmId = scope === 'resume'
            ? objectValue(pendingResume && pendingResume.preflight && pendingResume.preflight.configValues, 'realmId')
            : objectValue(selectedCreateSource(), 'realmId');
        var configuration = {
            profileId: profileId,
            realmId: Number(realmId),
            playerLimit: numberValue(prefix + 'PlayerLimit'),
            playerHardLimit: numberValue(prefix + 'PlayerHardLimit'),
            loginPerTick: numberValue(prefix + 'LoginPerTick'),
            stateFlushMs: numberValue(prefix + 'StateFlushMs'),
            allianceBotCap: numberValue(prefix + 'AllianceBotCap'),
            hordeBotCap: numberValue(prefix + 'HordeBotCap'),
            rates: {},
            honorWeightPlayer: numberValueOr(prefix + 'HonorWeightPlayer', objectValue(defaults, 'honorWeightPlayer') || 10),
            honorWeightBot: numberValueOr(prefix + 'HonorWeightBot', objectValue(defaults, 'honorWeightBot') || 5),
            honorWeightFactionNpc: numberValueOr(prefix + 'HonorWeightFactionNpc', objectValue(defaults, 'honorWeightFactionNpc') || 1),
            honorWeightFactionElite: numberValueOr(prefix + 'HonorWeightFactionElite', objectValue(defaults, 'honorWeightFactionElite') || 3),
            suppressBotHonorHistory: $(prefix + 'SuppressBotHonorHistory').length
                ? $(prefix + 'SuppressBotHonorHistory').prop('checked')
                : objectValue(defaults, 'suppressBotHonorHistory') !== false,
            factionWideBotControl: $(prefix + 'FactionWideBotControl').length
                ? $(prefix + 'FactionWideBotControl').prop('checked')
                : objectValue(defaults, 'factionWideBotControl') !== false,
            heroSlotsFixed: numberValueOr(prefix + 'HeroSlotsFixed', objectValue(defaults, 'heroSlotsFixed') || 4),
            heroRules: []
        };
        $('[data-' + scope + '-rate]').each(function () {
            configuration.rates[$(this).attr('data-' + scope + '-rate')] = Number($(this).val());
        });
        $('tr[data-' + scope + '-hero-level]').each(function () {
            var row = $(this);
            function heroValue(field) { return Number(row.find('[data-' + scope + '-hero="' + field + '"]').val()); }
            configuration.heroRules.push({
                heroLevel: Number(row.attr('data-' + scope + '-hero-level')),
                honorCost: heroValue('honorCost'),
                reviveFee: heroValue('reviveFee'),
                spellId: heroValue('spellId'),
                scalePercent: heroValue('scalePercent'),
                damagePercent: heroValue('damagePercent')
            });
        });
        if (!configuration.heroRules.length)
            configuration.heroRules = $.extend(true, [], objectValue(defaults, 'heroRules') || []);
        return configuration;
    }

    function configurationErrors(configuration, scope, eligibleNameCount) {
        var errors = [];
        if (!Number.isFinite(configuration.realmId) || configuration.realmId < 1) errors.push('The selected snapshot does not expose a valid inherited RealmID.');
        if (!Number.isFinite(configuration.playerLimit) || configuration.playerLimit < 1 || configuration.playerLimit > 100000) errors.push('PlayerLimit must be between 1 and 100,000.');
        if (!Number.isFinite(configuration.playerHardLimit) || (configuration.playerHardLimit !== 0 && configuration.playerHardLimit < configuration.playerLimit)) errors.push('PlayerHardLimit must be 0 or at least PlayerLimit.');
        if (Number.isFinite(configuration.playerHardLimit) && configuration.playerHardLimit > 100000) errors.push('PlayerHardLimit cannot exceed 100,000.');
        if (!Number.isFinite(configuration.loginPerTick) || configuration.loginPerTick < 0 || configuration.loginPerTick > 100) errors.push('LoginPerTick must be between 0 and 100.');
        if (!Number.isFinite(configuration.stateFlushMs) || configuration.stateFlushMs < 1000 || configuration.stateFlushMs > 600000) errors.push('RTS state flush must be between 1,000 and 600,000 ms.');
        if (!Number.isFinite(configuration.allianceBotCap) || !Number.isFinite(configuration.hordeBotCap) || configuration.allianceBotCap < 0 || configuration.allianceBotCap > 50000 || configuration.hordeBotCap < 0 || configuration.hordeBotCap > 50000) errors.push('Faction bot caps must be between 0 and 50,000.');
        var combinedBotCap = configuration.allianceBotCap + configuration.hordeBotCap;
        if (Number.isFinite(combinedBotCap) && Number.isFinite(configuration.playerLimit) && combinedBotCap > configuration.playerLimit)
            errors.push('Combined faction bot caps cannot exceed PlayerLimit; the remainder is session headroom for humans and other logins.');
        if (eligibleNameCount === undefined || eligibleNameCount === null || !Number.isFinite(Number(eligibleNameCount)))
            errors.push('The current eligible bot-name count is unavailable.');
        else if (Number.isFinite(combinedBotCap) && combinedBotCap > Number(eligibleNameCount))
            errors.push('Combined faction bot caps cannot exceed the current pool of ' + formatNumber(eligibleNameCount) + ' eligible unique names.');
        $('[data-' + (scope || 'create') + '-rate]').each(function () {
            var value = Number($(this).val());
            var min = Number($(this).attr('min'));
            var max = Number($(this).attr('max'));
            if (!Number.isFinite(value) || value < min || value > max)
                errors.push($(this).closest('label').find('span').first().text() + ' must be between ' + min + ' and ' + max + '.');
        });
        ['honorWeightPlayer', 'honorWeightBot', 'honorWeightFactionNpc', 'honorWeightFactionElite'].forEach(function (key) {
            var value = Number(configuration[key]);
            if (!Number.isInteger(value) || value < 0 || value > 1000000)
                errors.push('RTS Honor weights must be whole numbers between 0 and 1,000,000.');
        });
        if (!Number.isInteger(Number(configuration.heroSlotsFixed)) || configuration.heroSlotsFixed < 1 || configuration.heroSlotsFixed > 127)
            errors.push('Fixed hero slots must be between 1 and 127.');
        if (!configuration.heroRules || configuration.heroRules.length !== 5)
            errors.push('RTS requires five hero target-level rows.');
        (configuration.heroRules || []).forEach(function (rule) {
            if (!Number.isInteger(rule.heroLevel) || rule.heroLevel < 1 || rule.heroLevel > 5 ||
                !Number.isInteger(rule.honorCost) || rule.honorCost < 0 ||
                !Number.isInteger(rule.reviveFee) || rule.reviveFee < 0 ||
                !Number.isInteger(rule.spellId) || rule.spellId < 1 ||
                !Number.isInteger(rule.scalePercent) || rule.scalePercent < 100 || rule.scalePercent > 200 ||
                !Number.isInteger(rule.damagePercent) || rule.damagePercent < 100 || rule.damagePercent > 200)
                errors.push('Every hero row needs valid whole-number costs, spell ID, scale, and damage values.');
            if (Number.isInteger(rule.heroLevel) && rule.spellId !== 51000 + rule.heroLevel)
                errors.push('Hero level ' + rule.heroLevel + ' must use reserved spell ID ' + (51000 + rule.heroLevel) + '.');
        });
        return errors;
    }

    function renderCapacitySummary(selector, configuration, eligibleNameCount) {
        var combined = Number(configuration.allianceBotCap) + Number(configuration.hordeBotCap);
        var playerLimit = Number(configuration.playerLimit);
        if (!Number.isFinite(combined) || !Number.isFinite(playerLimit)) {
            $(selector).text('Enter valid capacity values to calculate session headroom.');
            return;
        }
        var headroom = playerLimit - combined;
        var text = formatNumber(combined) + ' bot slots inside PlayerLimit ' + formatNumber(playerLimit) +
            ' leaves ' + formatNumber(headroom) + ' session slot' + (headroom === 1 ? '' : 's') +
            ' for humans and other logins.';
        if (eligibleNameCount !== undefined && eligibleNameCount !== null)
            text += ' Current eligible-name pool: ' + formatNumber(eligibleNameCount) + '.';
        $(selector).css('color', headroom < 0 ? 'var(--status-error)' : headroom === 0 ? 'var(--status-warning)' : '').text(text);
    }

    function configurationReviewHtml(configuration) {
        if (!configuration) return '';
        var profile = profileDefinition(configuration.profileId);
        var label = profile && objectValue(profile, 'label') || configuration.profileId;
        var html = '<div class="ws-config-summary">' +
            statusBadge('good', 'fa-layer-group', label) +
            statusBadge('', 'fa-users', 'limit ' + formatNumber(configuration.playerLimit)) +
            statusBadge('', 'fa-robot', formatNumber(configuration.allianceBotCap) + 'A / ' + formatNumber(configuration.hordeBotCap) + 'H');
        html += statusBadge(configuration.factionWideBotControl ? 'good' : 'warn', 'fa-chess-knight',
            configuration.factionWideBotControl ? 'faction bot control on' : 'faction bot control off');
        html += statusBadge('good', 'fa-shield-halved', 'heroes: bots only, ' + formatNumber(configuration.heroSlotsFixed) + ' slots');
        html += statusBadge(configuration.suppressBotHonorHistory ? 'good' : 'warn', 'fa-scroll',
            configuration.suppressBotHonorHistory ? 'bot HK history suppressed' : 'bot HK history retained');
        html += '</div><div class="ws-field-help"><strong>Honor weights:</strong> player ' + esc(configuration.honorWeightPlayer) +
            ', bot ' + esc(configuration.honorWeightBot) + ', faction NPC ' + esc(configuration.honorWeightFactionNpc) +
            ', elite ' + esc(configuration.honorWeightFactionElite) + '. <strong>Hero ladder:</strong> ' +
            (configuration.heroRules || []).map(function (rule) {
                return 'L' + rule.heroLevel + ' ' + rule.honorCost + '/' + rule.reviveFee +
                    ' Honor, ' + rule.scalePercent + '% scale, ' + rule.damagePercent + '% damage, #' + rule.spellId;
            }).join(' -> ') + '.</div>';
        return html;
    }

    function renderCreateValidation() {
        var blockers = [];
        if (!createOptions) blockers.push('The RTS profile could not be loaded.');
        if (status && status.liveWorld) blockers.push('Suspend the currently mounted world before creating a campaign.');
        if (status && (status.mangosdRunning || status.realmdRunning)) blockers.push('mangosd and realmd must both be stopped before creation.');
        if (busy()) blockers.push('Wait for the current World State job to finish.');
        var source = selectedCreateSource();
        if (!source || !source.eligible) blockers.push('Select an eligible v2 source snapshot.');
        var name = $('#wsCreateName').val().trim();
        if (!name) blockers.push('Give the RTS world a name.');
        var configuration = createOptions ? collectConfiguration('create') : null;
        if (configuration) blockers = blockers.concat(configurationErrors(
            configuration, 'create', createOptions.namePoolEligible));

        if (configuration) renderCapacitySummary(
            '#wsCreateCapacitySummary', configuration, createOptions.namePoolEligible);

        var h = blockers.length
            ? '<div class="ws-preflight"><div class="ws-preflight-head"><div class="ws-preflight-title"><i class="fa-solid fa-circle-xmark" style="color: var(--status-error);"></i> Creation blocked</div></div><div class="ws-preflight-body">' + messageList(unique(blockers), 'blockers') + '</div></div>'
            : '<div class="ws-preflight"><div class="ws-preflight-head"><div class="ws-preflight-title"><i class="fa-solid fa-circle-check" style="color: var(--status-online);"></i> Ready to build a parked zero-roster world</div><span class="ws-status-badge good">' + esc(configuration.profileId) + ' · 0 characters · 0 persisted bots</span></div></div>';
        h += configurationReviewHtml(configuration);
        $('#wsCreateValidation').html(h);
        $('#wsConfirmCreateRts').prop('disabled', blockers.length > 0);
    }

    $('#wsCreateSource').on('change', function () { renderCreateSourceStatus(); renderCreateValidation(); });
    $(document).on('change', '#wsCreateProfile', function () {
        switchProfile('create', $(this).val());
        renderCreateValidation();
    });
    $(document).on('input change', '#wsCreateName, #wsCreateRtsModal input, #wsCreateRtsModal select', renderCreateValidation);

    $('#wsConfirmCreateRts').on('click', function () {
        renderCreateValidation();
        if ($(this).prop('disabled')) return;
        var source = selectedCreateSource();
        var payload = {
            name: $('#wsCreateName').val().trim(),
            sourceWorldId: source.worldId,
            sourceSnapshot: source.snapshot,
            notes: $('#wsCreateNotes').val().trim(),
            configuration: collectConfiguration('create')
        };
        var btn = $(this);
        btn.prop('disabled', true).html('<i class="fa-solid fa-spinner fa-spin"></i> Starting build...');
        post('/Worlds/CreateRts', payload, function (result) {
            btn.html('<i class="fa-solid fa-wand-magic-sparkles"></i> Create Parked RTS World');
            if (!result.success) {
                showToast(result.error || 'RTS world creation failed', 'error');
                renderCreateValidation();
                return;
            }
            bootstrap.Modal.getInstance($('#wsCreateRtsModal')[0]).hide();
            renderTheatre(result.job);
            startJobPolling();
            showToast('Building a parked RTS world. It will not be mounted automatically.', 'info');
        });
    });

    // ===================== RESUME / SWAP CEREMONY =====================
    // Single preflighted lifecycle path; the server repeats validation before mutation.
    $(document).on('click', '.ws-resume-btn', function (e) {
        e.stopPropagation();
        var worldId = $(this).data('world');
        var requestedSnapshot = $(this).data('snapshot') || null;
        var target = findWorld(worldId);
        if (!target) return;

        var snapshots = target.snapshots || [];
        var selected = requestedSnapshot || (snapshots[0] && snapshots[0].folder) || null;
        pendingResume = {
            worldId: worldId,
            snapshot: selected,
            target: target,
            preflight: null,
            configInitialized: false,
            configScope: String(target.flavor || '').toLowerCase() === 'rts' ? 'resume' : null
        };

        var outgoing = status.liveWorld;
        var isSwap = !!outgoing;
        $('#wsResumeTitle').text(isSwap ? 'Swap Worlds' : 'Resume World');
        $('#wsConfirmResumeLabel').text(isSwap ? 'Swap Worlds' : 'Resume');
        var isRtsTarget = String(target.flavor || '').toLowerCase() === 'rts';
        $('#wsForceFullRestore').prop('checked', isRtsTarget).prop('disabled', isRtsTarget);
        $('#wsForceFullRestoreHelp').text(isRtsTarget
            ? 'Required for RTS profiles: restore the selected snapshot, apply immutable boot rules to an ephemeral world artifact, and preserve captured runtime state.'
            : 'Validate and restore the selected snapshot even when this world\'s data is already materialized.');
        renderResumeExchange(outgoing, target);

        if (snapshots.length > 1 && !requestedSnapshot) {
            $('#wsSnapshotSelect').html(snapshots.map(function (snapshot) {
                return '<option value="' + esc(snapshot.folder) + '">' +
                    esc(shortStamp(snapshot.takenUtc)) + ' — ' + esc(snapshot.label || (SNAP_KINDS[snapshot.kind] || snapshot.kind)) +
                    ' · ' + ((snapshot.schemaVersion || 1) < 2 ? 'v1 legacy' : 'v2') + '</option>';
            }).join('')).val(selected);
            $('#wsSnapshotPick').show();
        } else {
            $('#wsSnapshotPick').hide();
        }

        $('#wsResumePreflight').html('<div class="ws-preflight-loading"><i class="fa-solid fa-spinner fa-spin"></i> Running read-only preflight...</div>');
        $('#wsResumeConfiguration').html('<div class="ws-preflight-loading"><i class="fa-solid fa-spinner fa-spin"></i> Reading captured launch configuration...</div>');
        $('#wsConfirmResume').prop('disabled', true);
        new bootstrap.Modal($('#wsResumeModal')[0]).show();

        ensureCreateOptions(function (error) {
            if (error && pendingResume && pendingResume.configScope === 'resume') {
                $('#wsResumeConfiguration').html(messageList(
                    ['RTS configuration fields could not be loaded: ' + error], 'blockers'));
            }
            maybeRenderResumeConfiguration();
            refreshResumeConfirm();
        });
        runResumePreflight(true);
    });

    function renderResumeExchange(outgoing, target) {
        var tf = flavor(target.flavor);
        var html = '';
        if (outgoing) {
            var of_ = flavor(outgoing.flavor);
            html += '<div class="ws-exchange-side" style="--ws-flavor: ' + of_.color + ';">';
            html += '<div class="ws-exchange-role">Currently serving</div>';
            html += '<div class="ws-exchange-name">' + esc(outgoing.name) + '</div>';
            html += '<div class="ws-exchange-fate out"><i class="fa-solid fa-circle-pause"></i> Will be suspended first</div>';
            html += '<div class="ws-exchange-note">A new validated snapshot is captured before the swap.</div></div>';
        } else {
            html += '<div class="ws-exchange-side"><div class="ws-exchange-role">Currently serving</div>';
            html += '<div class="ws-exchange-name" style="color: var(--text-muted);">No world</div>';
            html += '<div class="ws-exchange-note">Data may remain materialized, but no world is active.</div></div>';
        }
        html += '<div class="ws-exchange-arrow"><i class="fa-solid fa-arrow-right-long"></i></div>';
        html += '<div class="ws-exchange-side" style="--ws-flavor: ' + tf.color + ';">';
        html += '<div class="ws-exchange-role">Will serve after resume</div>';
        html += '<div class="ws-exchange-name">' + esc(target.name) + '</div>';
        html += '<div class="ws-exchange-fate in"><i class="fa-solid fa-play"></i> Validate, restore, then boot</div>';
        html += '<div class="ws-exchange-note">' +
            (target.id === status.materializedWorldId
                ? 'Can use the materialized data, or Force full restore to test the selected snapshot.'
                : 'The selected snapshot is restored before realmd and mangosd start.') +
            '</div></div>';
        $('#wsExchange').html(html);
    }

    $('#wsSnapshotSelect').on('change', function () {
        if (!pendingResume) return;
        pendingResume.snapshot = $(this).val() || null;
        pendingResume.configInitialized = false;
        $('#wsResumeConfiguration').html('<div class="ws-preflight-loading"><i class="fa-solid fa-spinner fa-spin"></i> Reading selected snapshot configuration...</div>');
        runResumePreflight(true);
    });

    $('#wsForceFullRestore').on('change', function () {
        if (!pendingResume) return;
        runResumePreflight(false);
    });

    $('#wsResumeModal').on('hidden.bs.modal', function () {
        pendingResume = null;
        resumePreflightSerial++;
    });

    function runResumePreflight(resetConfiguration) {
        if (!pendingResume) return;
        if (resetConfiguration) pendingResume.configInitialized = false;
        var serial = ++resumePreflightSerial;
        var query = $.param({
            worldId: pendingResume.worldId,
            snapshot: pendingResume.snapshot || '',
            forceFullRestore: $('#wsForceFullRestore').prop('checked')
        });
        $('#wsConfirmResume').prop('disabled', true);
        $('#wsResumePreflight').html('<div class="ws-preflight-loading"><i class="fa-solid fa-spinner fa-spin"></i> Running read-only preflight...</div>');

        $.getJSON('/Worlds/Preflight?' + query)
            .done(function (result) {
                if (!pendingResume || serial !== resumePreflightSerial) return;
                pendingResume.preflight = result && result.success
                    ? result.preflight
                    : { allowed: false, blockers: [(result && result.error) || 'Preflight failed.'], warnings: [], artifacts: [] };
                renderResumePreflight();
                maybeRenderResumeConfiguration();
                refreshResumeConfirm();
            })
            .fail(function () {
                if (!pendingResume || serial !== resumePreflightSerial) return;
                pendingResume.preflight = { allowed: false, blockers: ['The restore preflight endpoint could not be reached.'], warnings: [], artifacts: [] };
                renderResumePreflight();
                maybeRenderResumeConfiguration();
                refreshResumeConfirm();
            });
    }

    function renderResumePreflight() {
        var p = pendingResume && pendingResume.preflight;
        if (!p) return;
        var allowed = !!p.allowed;
        var legacy = !!p.legacy || Number(p.schemaVersion || 0) === 1;
        var strategy = p.strategy || 'full-restore';
        var html = '<div class="ws-preflight-head"><div class="ws-preflight-title">' +
            '<i class="fa-solid ' + (allowed ? 'fa-circle-check' : 'fa-circle-xmark') + '" style="color: ' +
            (allowed ? 'var(--status-online)' : 'var(--status-error)') + ';"></i> ' +
            (allowed ? 'Restore preflight passed' : 'Restore preflight blocked') + '</div>' +
            statusBadge(allowed ? 'good' : 'bad', allowed ? 'fa-check' : 'fa-xmark', strategy === 'instant' ? 'materialized / instant' : 'full restore') +
            '</div><div class="ws-preflight-body"><div class="ws-preflight-badges">';

        html += statusBadge(legacy ? 'warn' : 'good', legacy ? 'fa-triangle-exclamation' : 'fa-shield-halved',
            legacy ? 'v1 legacy · structural checks only' : 'v' + (p.schemaVersion || 2) + ' · checksummed');
        html += statusBadge(configTone(p.configStatus), 'fa-sliders', 'config: ' + (p.configStatus || 'unknown'));
        if (p.alreadyMaterialized)
            html += statusBadge('good', 'fa-database', p.forceFullRestore ? 'materialized · restore forced' : 'already materialized');
        if (p.mangosdRunning !== undefined)
            html += statusBadge(p.mangosdRunning ? 'good' : 'warn', 'fa-server', 'mangosd ' + (p.mangosdRunning ? 'running' : 'stopped'));
        if (p.realmdRunning !== undefined)
            html += statusBadge(p.realmdRunning ? 'good' : 'warn', 'fa-server', 'realmd ' + (p.realmdRunning ? 'running' : 'stopped'));
        var currentPlayerLimit = objectValue(p.configValues, 'playerLimit');
        var currentHardLimit = objectValue(p.configValues, 'playerHardLimit');
        var currentLoginPerTick = objectValue(p.configValues, 'loginPerTick');
        if (currentPlayerLimit !== undefined && currentPlayerLimit !== null)
            html += statusBadge('', 'fa-users', 'current PlayerLimit ' + formatNumber(currentPlayerLimit));
        if (currentHardLimit !== undefined && currentHardLimit !== null)
            html += statusBadge('', 'fa-user-shield', 'current hard limit ' + formatNumber(currentHardLimit));
        if (currentLoginPerTick !== undefined && currentLoginPerTick !== null)
            html += statusBadge('', 'fa-right-to-bracket', 'current login/tick ' + formatNumber(currentLoginPerTick));
        var capturedRealmId = objectValue(p.configValues, 'realmId');
        if (capturedRealmId !== undefined && capturedRealmId !== null)
            html += statusBadge('good', 'fa-earth-americas', 'RealmID ' + formatNumber(capturedRealmId) + ' · fixed');
        if (p.namePoolEligible !== undefined && p.namePoolEligible !== null)
            html += statusBadge('good', 'fa-signature', formatNumber(p.namePoolEligible) + ' eligible bot names now');
        html += '</div>';

        if (legacy)
            html += '<div class="ws-field-help">Legacy v1: files are decompressed and structurally checked now, but historical SHA-256 hashes do not exist. Config status above confirms whether mangosd.conf can be safely extracted from the core archive.</div>';

        html += messageList(p.blockers || [], 'blockers');
        html += messageList(p.warnings || [], 'warnings');
        var artifacts = p.artifacts || [];
        if (artifacts.length) {
            html += '<div class="ws-artifact-list">' + artifacts.map(function (artifact) {
                var good = !!artifact.valid;
                return '<div class="ws-artifact ' + (good ? 'good' : 'bad') + '" title="' + esc(artifact.detail || '') + '">' +
                    '<i class="fa-solid ' + (good ? 'fa-circle-check' : 'fa-circle-xmark') + '"></i> ' +
                    esc(artifact.fileName || artifact.id || 'artifact') + '</div>';
            }).join('') + '</div>';
        }
        html += '</div>';
        $('#wsResumePreflight').html(html);
    }

    function maybeRenderResumeConfiguration() {
        if (!pendingResume || !pendingResume.preflight || pendingResume.configInitialized) return;
        var target = pendingResume.target;
        var p = pendingResume.preflight;
        var isRts = String(target.flavor || '').toLowerCase() === 'rts';

        if (!isRts) {
            var captured = p.savedConfiguration || selectedResumeSnapshotConfiguration() || target.launchConfiguration || p.configValues;
            var html = '<h6>Captured launch configuration</h6>' +
                '<div class="ws-resume-config-intro">MMO worlds resume with their captured configuration unchanged; no configuration override is sent.</div>';
            if (captured) {
                html += '<div class="ws-config-summary">';
                var playerLimit = objectValue(captured, 'playerLimit');
                var hardLimit = objectValue(captured, 'playerHardLimit');
                var loginPerTick = objectValue(captured, 'loginPerTick');
                var stateFlushMs = objectValue(captured, 'stateFlushMs');
                if (playerLimit !== undefined) html += statusBadge('', 'fa-users', 'PlayerLimit ' + formatNumber(playerLimit));
                if (hardLimit !== undefined) html += statusBadge('', 'fa-user-shield', 'Hard limit ' + formatNumber(hardLimit));
                if (loginPerTick !== undefined) html += statusBadge('', 'fa-right-to-bracket', 'Login/tick ' + formatNumber(loginPerTick));
                if (stateFlushMs !== undefined) html += statusBadge('', 'fa-floppy-disk', 'RTS flush ' + formatNumber(stateFlushMs) + ' ms');
                html += '</div>';
            } else {
                html += '<div class="ws-field-help">No structured values were captured; the snapshot configuration is restored as stored.</div>';
            }
            $('#wsResumeConfiguration').html(html);
            pendingResume.configInitialized = true;
            pendingResume.configScope = null;
            return;
        }

        if (!createOptions) return;
        var saved = p.savedConfiguration || selectedResumeSnapshotConfiguration() || target.launchConfiguration || {};
        var savedProfileId = objectValue(saved, 'profileId') || 'rts-r2-v1';
        var seed = mergeConfiguration(profileDefaults(savedProfileId), saved);
        seed.profileId = savedProfileId;
        seed.realmId = Number(objectValue(p.configValues, 'realmId'));
        var html = '<h6>RTS launch configuration</h6>' +
            '<div class="ws-resume-config-intro">Review or change this load-time profile. World State performs a full stopped-world restore, preserves the selected snapshot’s runtime Honor/hero state, and makes the configuration immutable for the next boot.</div>' +
            '<label class="form-label" for="wsResumeProfile">Rules profile</label><select class="form-input ws-profile-select" id="wsResumeProfile"></select>' +
            '<div class="ws-config-summary">' +
            statusBadge(seed.realmId > 0 ? 'good' : 'bad', 'fa-earth-americas', seed.realmId > 0 ? 'RealmID ' + seed.realmId + ' · inherited, not editable' : 'Captured RealmID unavailable') +
            statusBadge(p.namePoolEligible !== undefined && p.namePoolEligible !== null ? 'good' : 'bad', 'fa-signature', p.namePoolEligible !== undefined && p.namePoolEligible !== null ? formatNumber(p.namePoolEligible) + ' eligible bot names now' : 'Name pool unavailable') +
            '</div>' +
            '<div class="ws-field-grid">' +
            resumeNumberField('PlayerLimit', 'PlayerLimit', seed.playerLimit, 1, 100000) +
            resumeNumberField('PlayerHardLimit', 'PlayerHardLimit', seed.playerHardLimit, 0, 100000) +
            resumeNumberField('LoginPerTick', 'LoginPerTick', seed.loginPerTick, 0, 100) +
            resumeNumberField('RTS state flush (ms)', 'StateFlushMs', seed.stateFlushMs, 1000, 600000, 1000) +
            resumeNumberField('Alliance bot cap', 'AllianceBotCap', seed.allianceBotCap, 0, 50000) +
            resumeNumberField('Horde bot cap', 'HordeBotCap', seed.hordeBotCap, 0, 50000) +
            '</div><div class="ws-field-help" id="wsResumeCapacitySummary"></div>' +
            '<div id="wsResumeRateFields" style="margin-top: 13px;"></div>' +
            '<div class="ws-r2-settings" id="wsResumeR2Fields"></div>' +
            '<div id="wsResumeRulesReview"></div>';
        $('#wsResumeConfiguration').html(html);
        renderProfileOptions('#wsResumeProfile', seed.profileId);
        applyConfigurationFields('resume', seed);
        pendingResume.configInitialized = true;
        pendingResume.configScope = 'resume';
        renderCapacitySummary('#wsResumeCapacitySummary', seed, p.namePoolEligible);
        $('#wsResumeRulesReview').html(configurationReviewHtml(seed));
    }

    function selectedResumeSnapshotConfiguration() {
        if (!pendingResume) return null;
        var snapshot = (pendingResume.target.snapshots || []).filter(function (item) {
            return item.folder === pendingResume.snapshot;
        })[0];
        return snapshot && snapshot.launchConfiguration;
    }

    function resumeNumberField(label, suffix, value, min, max, step) {
        return '<label><span>' + esc(label) + '</span><input class="form-input" type="number" id="wsResumeCfg' + suffix + '"' +
            ' min="' + min + '"' + (max === null ? '' : ' max="' + max + '"') + ' step="' + (step || 1) + '" value="' + esc(value) + '" /></label>';
    }

    function refreshResumeConfirm() {
        if (!pendingResume || !pendingResume.preflight) {
            $('#wsConfirmResume').prop('disabled', true);
            return;
        }
        var blocked = !pendingResume.preflight.allowed;
        if (pendingResume.configScope === 'resume') {
            if (!createOptions || !pendingResume.configInitialized) blocked = true;
            else {
                var configuration = collectConfiguration('resume');
                renderCapacitySummary('#wsResumeCapacitySummary', configuration, pendingResume.preflight.namePoolEligible);
                $('#wsResumeRulesReview').html(configurationReviewHtml(configuration));
                blocked = blocked || configurationErrors(
                    configuration, 'resume', pendingResume.preflight.namePoolEligible).length > 0;
            }
        }
        $('#wsConfirmResume').prop('disabled', blocked);
    }

    $(document).on('change', '#wsResumeProfile', function () {
        switchProfile('resume', $(this).val());
        refreshResumeConfirm();
    });
    $(document).on('input change', '#wsResumeConfiguration input, #wsResumeConfiguration select', refreshResumeConfirm);

    $('#wsConfirmResume').on('click', function () {
        if (!pendingResume || !pendingResume.preflight || !pendingResume.preflight.allowed) return;
        var configuration = null;
        if (pendingResume.configScope === 'resume') {
            configuration = collectConfiguration('resume');
            var errors = configurationErrors(
                configuration, 'resume', pendingResume.preflight.namePoolEligible);
            if (errors.length) {
                showToast(errors[0], 'error');
                return;
            }
        }

        var payload = {
            worldId: pendingResume.worldId,
            snapshot: pendingResume.snapshot,
            forceFullRestore: $('#wsForceFullRestore').prop('checked'),
            configuration: configuration
        };
        var btn = $(this);
        var original = btn.html();
        btn.prop('disabled', true).html('<i class="fa-solid fa-spinner fa-spin"></i> Starting...');

        post('/Worlds/Resume', payload, function (result) {
            btn.html(original);
            if (!result.success) {
                showToast(result.error || 'Resume failed', 'error');
                refreshResumeConfirm();
                return;
            }
            bootstrap.Modal.getInstance($('#wsResumeModal')[0]).hide();
            renderTheatre(result.job);
            startJobPolling();
            load();
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

    function setNumber(selector, value, fallback) {
        $(selector).val(value !== undefined && value !== null ? value : fallback);
    }

    function numberValue(selector) {
        var value = Number($(selector).val());
        return Number.isFinite(value) ? value : NaN;
    }

    function numberValueOr(selector, fallback) {
        if (!$(selector).length) return Number(fallback);
        var value = Number($(selector).val());
        return Number.isFinite(value) ? value : Number(fallback);
    }

    function statusBadge(tone, icon, label) {
        return '<span class="ws-status-badge ' + esc(tone || '') + '"><i class="fa-solid ' + esc(icon) + '"></i> ' + esc(label) + '</span>';
    }

    function configTone(value) {
        var text = String(value || '').toLowerCase();
        if (text.indexOf('invalid') >= 0 || text.indexOf('missing') >= 0 || text.indexOf('not-captured') >= 0) return 'bad';
        if (text.indexOf('legacy') >= 0 || text.indexOf('extract') >= 0 || text.indexOf('embedded') >= 0 || text.indexOf('unknown') >= 0) return 'warn';
        return 'good';
    }

    function messageList(items, cssClass) {
        if (!items || !items.length) return '';
        return '<ul class="ws-message-list ' + esc(cssClass || '') + '">' + items.map(function (item) {
            return '<li>' + esc(item) + '</li>';
        }).join('') + '</ul>';
    }

    function unique(items) {
        return items.filter(function (item, index) { return items.indexOf(item) === index; });
    }

    function mergeConfiguration(base, override) {
        var merged = $.extend(true, {}, base || {}, override || {});
        merged.rates = $.extend({}, (base && base.rates) || {}, (override && override.rates) || {});
        if (override && objectValue(override, 'heroRules') !== undefined)
            merged.heroRules = $.extend(true, [], objectValue(override, 'heroRules') || []);
        else
            merged.heroRules = $.extend(true, [], objectValue(base, 'heroRules') || []);
        return merged;
    }

    function objectValue(source, camelKey) {
        if (!source) return undefined;
        if (source[camelKey] !== undefined) return source[camelKey];
        var pascalKey = camelKey.charAt(0).toUpperCase() + camelKey.slice(1);
        if (source[pascalKey] !== undefined) return source[pascalKey];
        var wanted = camelKey.toLowerCase();
        var key = Object.keys(source).filter(function (candidate) { return candidate.toLowerCase() === wanted; })[0];
        return key === undefined ? undefined : source[key];
    }

    function formatNumber(value) {
        var number = Number(value);
        return Number.isFinite(number) ? number.toLocaleString() : String(value);
    }

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
