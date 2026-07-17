// MangosSuperUI — Chat Capacity JS (CHAT_ARCHITECTURE §14.3, amended 2026-07-13)
//
// Kill switches, profile CRUD, and — new — the whole no-SQL surface: voice library health,
// live chat health, endpoint model detection, and a preflight-gated build. The repetition
// bug of 2026-07-13 was invisible from this page (it said "0/300 voices" and nobody read
// that as "all 25 bots share one hardcoded card"), so the numbers that would have caught
// it are now on the page by default: distinct names, distinct example lines, opening-bigram
// spread, and the live out-line duplication rate.

$(function () {

    function esc(s) { return $('<div>').text(s == null ? '' : String(s)).html(); }

    function showToast(msg, type) {
        var el = $('<div class="chat-toast ' + (type || 'success') + '">' + esc(msg) + '</div>');
        $('body').append(el);
        setTimeout(function () { el.fadeOut(200, function () { el.remove(); }); }, 3200);
    }

    // ===================== Shared renderers =====================

    var ICON = { pass: 'fa-circle-check', warn: 'fa-triangle-exclamation', fail: 'fa-circle-xmark' };

    function renderChecks(el, checks) {
        el.empty();
        if (!checks || !checks.length) { el.append('<div class="hc-loading">No checks.</div>'); return; }
        checks.forEach(function (c) {
            el.append(
                '<div class="hc ' + esc(c.status) + '">' +
                '  <i class="fa-solid ' + (ICON[c.status] || 'fa-circle') + '"></i>' +
                '  <div><div class="hc-label">' + esc(c.label) + '</div>' +
                '       <div class="hc-detail">' + esc(c.detail) + '</div></div>' +
                '</div>');
        });
    }

    function renderStats(el, cells) {
        el.empty();
        cells.forEach(function (c) {
            el.append('<div class="stat-cell' + (c.bad ? ' bad' : '') + '">' +
                '<div class="s-label">' + esc(c.label) + '</div>' +
                '<div class="s-value">' + esc(c.value) + '</div></div>');
        });
    }

    function renderBars(el, items) {
        el = $(el).empty();
        if (!items || !items.length) { el.append('<div class="dist-empty">nothing yet</div>'); return; }
        var max = Math.max.apply(null, items.map(function (i) { return i.count; }));
        items.forEach(function (i) {
            var pct = max > 0 ? Math.round(i.count / max * 100) : 0;
            el.append(
                '<div class="bar-row" title="' + esc(i.key) + '">' +
                '  <span class="b-n">' + i.count + '</span>' +
                '  <span class="b-k"><span class="b-fill" style="width:' + pct + '%"></span>' +
                '        <span class="b-t">' + esc(i.key) + '</span></span>' +
                '</div>');
        });
    }

    // ===================== Load =====================

    var lastData = {};
    var detectedModels = {};   // profileId → [tags]

    function load() {
        $.ajax({
            url: '/BotChat/Capacity/Data',
            method: 'GET',
            success: function (data) {
                lastData = data;
                renderSwitch($('#ksChat'), data.chatEnabled);
                renderSwitch($('#ksAmbient'), data.ambientEnabled);
                renderProfiles(data.profiles || []);
                renderVoiceStatus(data);
            },
            error: function () { showToast('Failed to load capacity data', 'error'); }
        });
        loadLibraryHealth();
        loadChatHealth();
    }

    // ===================== Kill switches =====================

    function renderSwitch(btn, on) {
        btn.data('on', !!on)
            .toggleClass('on', !!on)
            .toggleClass('off', !on)
            .find('.ks-state').text(on ? 'ENABLED' : 'DISABLED');
    }

    $('.kill-switch').on('click', function () {
        var btn = $(this);
        var next = !btn.data('on');
        $.ajax({
            url: '/BotChat/Settings/Set',
            method: 'POST',
            contentType: 'application/json',
            data: JSON.stringify({ key: btn.data('key'), value: next ? 'true' : 'false' }),
            success: function (r) {
                if (r.success) { renderSwitch(btn, next); showToast(btn.data('key') + ' → ' + next); }
                else showToast(r.error || 'Write failed', 'error');
            },
            error: function () { showToast('Write failed', 'error'); }
        });
    });

    // ===================== Profiles =====================

    function profileRow(p) {
        var tr = $('<tr>').data('id', p.id).toggleClass('active-row', !!p.active);

        var radio = $('<input type="radio" name="activeProfile">').prop('checked', !!p.active);
        radio.on('change', function () { activate(p.id, p.name); });
        tr.append($('<td>').append(p.id ? radio : $('<span class="phase-tag">new</span>')));

        function cell(cls, field, val, type) {
            var inp = $('<input>').attr('type', type || 'text').addClass('form-control').val(val);
            if (type === 'number') inp.attr({ min: 0, step: field === 'ambientRateMult' ? 0.05 : 1 });
            tr.append($('<td class="' + cls + '">').append(inp));
            inp.data('field', field);
            return inp;
        }
        cell('col-name', 'name', p.name);
        cell('col-url', 'endpointUrl', p.endpointUrl);

        var flavorSel = $('<select class="form-control">')
            .append($('<option>').val('ollama').text('ollama'))
            .append($('<option>').val('openai').text('openai'))
            .val(p.apiFlavor || 'ollama')
            .data('field', 'apiFlavor');
        tr.append($('<td class="col-flavor">').append(flavorSel));

        // Model cells get a datalist once "Detect models" has probed the endpoint, so an
        // operator picks a real tag instead of typing one from memory.
        var listId = 'models-' + (p.id || 'new');
        function modelCell(cls, field, val) {
            var inp = $('<input type="text" class="form-control">').val(val).attr('list', listId).data('field', field);
            tr.append($('<td class="' + cls + '">').append(inp));
        }
        modelCell('col-model', 'modelReactive', p.modelReactive);
        modelCell('col-model', 'modelAmbient', p.modelAmbient);
        modelCell('col-model col-batch', 'modelBatch', p.modelBatch);

        var dl = $('<datalist>').attr('id', listId);
        (detectedModels[p.id] || []).forEach(function (m) { dl.append($('<option>').val(m)); });
        tr.append($('<td style="display:none">').append(dl));

        cell('col-num', 'ctxBudgetTokens', p.ctxBudgetTokens, 'number');
        cell('col-num', 'concurrency', p.concurrency, 'number');
        cell('col-num', 'reactiveReserved', p.reactiveReserved, 'number');
        cell('col-num', 'ambientRateMult', p.ambientRateMult, 'number');

        var actions = $('<td class="row-actions">');
        actions.append($('<button class="btn-sm btn-outline-subtle"><i class="fa-solid fa-floppy-disk"></i></button>')
            .attr('title', 'Save').on('click', function () { saveRow(tr); }));
        if (p.id) {
            actions.append(' ');
            actions.append($('<button class="btn-sm btn-outline-subtle"><i class="fa-solid fa-trash"></i></button>')
                .attr('title', 'Delete').on('click', function () { removeRow(tr, p.name); }));
        }
        tr.append(actions);
        return tr;
    }

    function renderProfiles(profiles) {
        var body = $('#profileTable tbody').empty();
        profiles.forEach(function (p) { body.append(profileRow(p)); });
    }

    function collect(tr) {
        var dto = { id: tr.data('id') || 0 };
        tr.find('input.form-control, select.form-control').each(function () {
            var f = $(this).data('field');
            if (!f) return;
            var v = $(this).val();
            dto[f] = (this.type === 'number') ? parseFloat(v || 0) : v;
        });
        dto.ctxBudgetTokens = Math.round(dto.ctxBudgetTokens || 3000);
        dto.concurrency = Math.round(dto.concurrency || 1);
        dto.reactiveReserved = Math.round(dto.reactiveReserved || 1);
        return dto;
    }

    function saveRow(tr) {
        $.ajax({
            url: '/BotChat/Capacity/SaveProfile',
            method: 'POST',
            contentType: 'application/json',
            data: JSON.stringify(collect(tr)),
            success: function (r) {
                if (r.success) { showToast('Profile saved'); load(); }
                else showToast(r.error || 'Save failed', 'error');
            },
            error: function () { showToast('Save failed', 'error'); }
        });
    }

    function removeRow(tr, name) {
        if (!confirm('Delete profile "' + name + '"?')) return;
        $.ajax({
            url: '/BotChat/Capacity/DeleteProfile',
            method: 'POST',
            contentType: 'application/json',
            data: JSON.stringify({ id: tr.data('id') }),
            success: function (r) {
                if (r.success) { showToast('Profile deleted'); load(); }
                else showToast(r.error || 'Delete failed', 'error');
            },
            error: function () { showToast('Delete failed', 'error'); }
        });
    }

    function activate(id, name) {
        $.ajax({
            url: '/BotChat/Capacity/ActivateProfile',
            method: 'POST',
            contentType: 'application/json',
            data: JSON.stringify({ id: id }),
            success: function (r) {
                if (r.success) { showToast('Active profile → ' + r.name); load(); }
                else { showToast(r.error || 'Activate failed', 'error'); load(); }
            },
            error: function () { showToast('Activate failed', 'error'); load(); }
        });
    }

    $('#btnAddProfile').on('click', function () {
        $('#profileTable tbody').append(profileRow({
            id: 0, name: '', endpointUrl: 'http://', apiFlavor: 'ollama', modelReactive: '', modelAmbient: '',
            modelBatch: '', ctxBudgetTokens: 3000, concurrency: 2, reactiveReserved: 1,
            ambientRateMult: 1.0, active: false
        }));
    });

    // ── Detect models: ask each endpoint what it serves ──
    $('#btnDetectModels').on('click', function () {
        var btn = $(this).prop('disabled', true);
        var rows = $('#profileTable tbody tr').toArray();
        var pending = rows.length, found = 0;
        if (!pending) { btn.prop('disabled', false); return; }

        rows.forEach(function (row) {
            var tr = $(row);
            var dto = collect(tr);
            $.ajax({
                url: '/BotChat/Capacity/ProbeModels',
                method: 'GET',
                data: { endpoint: dto.endpointUrl, flavor: dto.apiFlavor },
                success: function (r) {
                    if (r.success) {
                        detectedModels[dto.id] = r.models;
                        found += r.models.length;
                        var dl = tr.find('datalist').empty();
                        r.models.forEach(function (m) { dl.append($('<option>').val(m)); });
                    }
                },
                complete: function () {
                    if (--pending === 0) {
                        btn.prop('disabled', false);
                        showToast(found > 0
                            ? 'Detected ' + found + ' model tags — the model fields are now pick-lists'
                            : 'No endpoints answered', found > 0 ? 'success' : 'error');
                    }
                }
            });
        });
    });

    // ===================== Voice library health =====================

    function loadLibraryHealth() {
        $.ajax({
            url: '/BotChat/Capacity/LibraryHealth',
            method: 'GET',
            success: function (h) {
                renderChecks($('#libChecks'), h.checks);

                var dupRate = h.exampleTotal ? (1 - h.exampleDistinct / h.exampleTotal) : 0;
                renderStats($('#libStats'), [
                    { label: 'Voices', value: h.voices, bad: h.voices === 0 },
                    { label: 'Distinct names', value: h.distinctNames },
                    { label: 'Distinct occupations', value: h.distinctOccupations },
                    { label: 'Example lines', value: h.exampleDistinct + ' / ' + h.exampleTotal, bad: dupRate > 0.1 },
                    { label: 'Shape violations', value: h.shapeViolations, bad: h.shapeViolations > h.voices * 0.15 },
                    { label: 'Old-schema cards', value: h.schemaOld, bad: h.schemaOld > 0 }
                ]);

                renderBars('#libSwear', h.swearLevels);
                renderBars('#libCaps', h.capsStyles);
                renderBars('#libNames', h.topNames);
                renderBars('#libBigrams', h.topOpeningBigrams);
                $('#libSamples').text((h.sampleCards || []).join('\n\n') || 'No cards yet.');
            },
            error: function () { $('#libChecks').html('<div class="hc-loading">Health check failed.</div>'); }
        });
    }

    $('#btnRefreshLibHealth').on('click', loadLibraryHealth);

    // ===================== Chat health =====================

    function loadChatHealth() {
        var days = parseInt($('#healthDays').val(), 10) || 7;
        $('#chatHealthWindow').text('last ' + days + (days === 1 ? ' day' : ' days'));
        $.ajax({
            url: '/BotChat/Capacity/ChatHealth',
            method: 'GET',
            data: { days: days },
            success: function (h) {
                renderChecks($('#chatChecks'), h.checks);

                var dupRate = h.outLines ? (1 - h.distinctLines / h.outLines) : 0;
                renderStats($('#chatStats'), [
                    { label: 'Lines sent', value: h.outLines },
                    { label: 'Distinct', value: h.distinctLines + ' / ' + h.outLines, bad: dupRate > 0.15 },
                    { label: 'Bots speaking', value: h.bots },
                    { label: 'Discarded', value: (h.discards || []).reduce(function (a, d) { return a + d.count; }, 0) }
                ]);

                renderBars('#chatRepeats', h.topRepeated);
                renderBars('#chatBigrams', h.topOpeningBigrams);
                renderBars('#chatDiscards', h.discards);
            },
            error: function () { $('#chatChecks').html('<div class="hc-loading">Health check failed.</div>'); }
        });
    }

    $('#btnRefreshChatHealth').on('click', loadChatHealth);
    $('#healthDays').on('change', loadChatHealth);

    // ===================== Voice build status =====================

    var voicePoll = null;

    function renderVoiceStatus(data) {
        var b = data.voiceBuild || {};
        var line;
        if (b.running) {
            line = 'Voice library: BUILDING — ' + b.accepted + '/' + b.target +
                ' (rejects: dedup ' + b.rejectedDedup + ', parse ' + b.rejectedParse +
                ', shape ' + (b.rejectedShape || 0) + ')';
            startVoicePoll();
        } else {
            line = 'Voice library: ' + data.voiceCount + '/' + data.voiceTarget + ' voices · ' +
                data.personaCount + ' personas (' + data.seedPersonaCount + ' unassigned) · ' +
                'banter_intensity ' + (data.banterIntensity != null ? data.banterIntensity : '—');
            if (b.error) line += ' — last build error: ' + b.error;
            stopVoicePoll();
        }
        $('#voiceStatus').text(line);
    }

    function startVoicePoll() {
        if (voicePoll) return;
        voicePoll = setInterval(function () {
            $.ajax({
                url: '/BotChat/Capacity/VoiceBuildStatus', method: 'GET',
                success: function (b) {
                    if (b.running) {
                        $('#voiceStatus').text('Voice library: BUILDING — ' + b.accepted + '/' + b.target +
                            ' (rejects: dedup ' + b.rejectedDedup + ', parse ' + b.rejectedParse +
                            ', shape ' + (b.rejectedShape || 0) + ')');
                    } else {
                        stopVoicePoll();
                        load();
                        onBuildFinished(b);
                    }
                }
            });
        }, 5000);
    }

    function stopVoicePoll() { if (voicePoll) { clearInterval(voicePoll); voicePoll = null; } }

    // A finished build leaves every persona still holding a card from the OLD library.
    // Don't make the operator know that — offer the follow-up.
    function onBuildFinished(b) {
        if (b && b.error) { showToast('Build ended with an error: ' + b.error, 'error'); return; }
        showToast('Voice library build finished — ' + b.accepted + '/' + b.target + ' voices');
        setTimeout(function () {
            var unassigned = (lastData && lastData.seedPersonaCount) || 0;
            var total = (lastData && lastData.personaCount) || 0;
            if (total === 0) return;
            var msg = unassigned > 0
                ? unassigned + ' persona(s) have no library voice. Assign them from the new library now?'
                : 'Reassign all ' + total + ' personas from the new library now? (Their current cards are replaced.)';
            if (confirm(msg)) reassignAll();
        }, 600);
    }

    // ===================== Preflight modal =====================

    var pfAction = null;

    function openPreflight(opts) {
        pfAction = opts;
        $('#pfTitle').html('<i class="fa-solid fa-clipboard-check"></i> ' + esc(opts.title));
        $('#pfChecks').html('<div class="hc-loading">Running preflight…</div>');
        $('#pfSummary').empty();
        $('#pfDestructive').prop('hidden', !opts.destructiveText);
        $('#pfDestructiveText').text(opts.destructiveText || '');
        $('#pfForceWrap').prop('hidden', true);
        $('#pfForce').prop('checked', false);
        $('#pfConfirm').prop('disabled', true).text(opts.confirmLabel || 'Start');
        $('#pfModal').prop('hidden', false);

        $.ajax({
            url: '/BotChat/Capacity/BuildPreflight',
            method: 'GET',
            success: function (pre) {
                renderChecks($('#pfChecks'), pre.checks);
                $('#pfSummary').html(
                    'Profile <strong>' + esc(pre.profileName) + '</strong> → ' + esc(pre.endpoint) +
                    ' (' + esc(pre.flavor) + ')<br>' +
                    'Batch model: <strong>' + esc(pre.effectiveBatchModel || '(none)') + '</strong>' +
                    (pre.usingReactiveFallback
                        ? ' <span style="color:#e6aa3c">— reactive fallback, no model_batch set</span>' : '') +
                    '<br>Target: ' + pre.target + ' voices · currently ' + pre.existingVoices);
                $('#pfConfirm').prop('disabled', !pre.canBuild);
                $('#pfForceWrap').prop('hidden', pre.canBuild);
            },
            error: function () {
                $('#pfChecks').html('<div class="hc-loading">Preflight failed to run.</div>');
            }
        });
    }

    function closePreflight() { $('#pfModal').prop('hidden', true); pfAction = null; }

    $('#pfClose, #pfCancel').on('click', closePreflight);
    $('#pfForce').on('change', function () { $('#pfConfirm').prop('disabled', !this.checked); });

    $('#pfConfirm').on('click', function () {
        if (!pfAction) return;
        var force = $('#pfForce').is(':checked');
        var url = pfAction.url;
        closePreflight();

        $.ajax({
            url: url, method: 'POST', contentType: 'application/json',
            data: JSON.stringify({ force: force }),
            success: function (r) {
                if (!r.success) { showToast(r.error || 'Start failed', 'error'); return; }
                var msg = 'Build started on ' + (r.model || 'the active model');
                if (r.fallback) msg += ' (reactive fallback)';
                if (r.retired) msg = 'Retired ' + r.retired + ' voices, detached ' + r.detached + ' personas. ' + msg;
                showToast(msg);
                startVoicePoll();
                load();
            },
            error: function () { showToast('Start failed', 'error'); }
        });
    });

    // ===================== Library actions =====================

    $('#btnBuildVoices').on('click', function () {
        openPreflight({
            title: 'Build / top up the voice library',
            url: '/BotChat/Capacity/BuildVoiceLibrary',
            confirmLabel: 'Build'
        });
    });

    $('#btnRebuildVoices').on('click', function () {
        openPreflight({
            title: 'Rebuild the voice library from scratch',
            url: '/BotChat/Capacity/RebuildLibrary',
            confirmLabel: 'Retire & rebuild',
            destructiveText: 'This retires EVERY voice in the library and detaches EVERY persona from its ' +
                'voice, then builds a clean library. Existing bot personalities will be replaced when you ' +
                'reassign them afterwards. Retired cards stay in the database and are not deleted.'
        });
    });

    function reassignAll() {
        $.ajax({
            url: '/BotChat/Capacity/ReassignAllPersonas', method: 'POST', contentType: 'application/json',
            success: function (r) {
                if (r.success) { showToast('Reassigned ' + r.rerolled + ' personas from the library'); load(); }
                else showToast(r.error || 'Reassign failed', 'error');
            },
            error: function () { showToast('Reassign failed', 'error'); }
        });
    }

    $('#btnReassignAll').on('click', function () {
        if (!confirm('Redraw EVERY persona from the current voice library? Their current cards are replaced.')) return;
        reassignAll();
    });

    $('#btnRerollSeeds').on('click', function () {
        if (!confirm('Reassign personas that never had a library voice? Their current cards are REPLACED.')) return;
        $.ajax({
            url: '/BotChat/Capacity/RerollSeedPersonas', method: 'POST', contentType: 'application/json',
            success: function (r) {
                if (r.success) { showToast('Rerolled ' + r.rerolled + ' personas'); load(); }
                else showToast(r.error || 'Reroll failed', 'error');
            },
            error: function () { showToast('Reroll failed', 'error'); }
        });
    });

    load();
});