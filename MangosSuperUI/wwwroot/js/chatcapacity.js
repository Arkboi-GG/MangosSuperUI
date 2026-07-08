// MangosSuperUI — Chat Capacity JS (CHAT_ARCHITECTURE §14.3, Phase C1)
// Kill switches write global.chat_enabled / global.ambient_enabled through the same
// settings endpoint as the Feel page. Profile table is full CRUD + exactly-one-active
// flip; the broker consumes the active row in C5.

$(function () {

    function esc(s) { return $('<div>').text(s == null ? '' : String(s)).html(); }

    function showToast(msg, type) {
        var el = $('<div class="chat-toast ' + (type || 'success') + '">' + esc(msg) + '</div>');
        $('body').append(el);
        setTimeout(function () { el.fadeOut(200, function () { el.remove(); }); }, 2400);
    }

    // ===================== Load =====================

    function load() {
        $.ajax({
            url: '/BotChat/Capacity/Data',
            method: 'GET',
            success: function (data) {
                renderSwitch($('#ksChat'), data.chatEnabled);
                renderSwitch($('#ksAmbient'), data.ambientEnabled);
                renderProfiles(data.profiles || []);
                renderVoiceStatus(data);
            },
            error: function () { showToast('Failed to load capacity data', 'error'); }
        });
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

        // API flavor: ollama (/api/generate) vs openai (/v1/chat/completions — vLLM etc.)
        var flavorSel = $('<select class="form-control">')
            .append($('<option>').val('ollama').text('ollama'))
            .append($('<option>').val('openai').text('openai'))
            .val(p.apiFlavor || 'ollama')
            .data('field', 'apiFlavor');
        tr.append($('<td class="col-flavor">').append(flavorSel));

        cell('col-model', 'modelReactive', p.modelReactive);
        cell('col-model', 'modelAmbient', p.modelAmbient);
        cell('col-model', 'modelBatch', p.modelBatch);
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

    // ===================== Voice library (C6) =====================

    var voicePoll = null;

    function renderVoiceStatus(data) {
        var b = data.voiceBuild || {};
        var line;
        if (b.running) {
            line = 'Voice library: BUILDING — ' + b.accepted + '/' + b.target +
                ' (dedup rejects ' + b.rejectedDedup + ', parse rejects ' + b.rejectedParse + ')';
            startVoicePoll();
        } else {
            line = 'Voice library: ' + data.voiceCount + '/' + data.voiceTarget + ' voices';
            if (data.seedPersonaCount > 0) line += ' — ' + data.seedPersonaCount + ' seed-era personas (reroll to upgrade)';
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
                            ' (dedup rejects ' + b.rejectedDedup + ', parse rejects ' + b.rejectedParse + ')');
                    } else { stopVoicePoll(); load(); }
                }
            });
        }, 5000);
    }

    function stopVoicePoll() { if (voicePoll) { clearInterval(voicePoll); voicePoll = null; } }

    $('#btnBuildVoices').on('click', function () {
        if (!confirm('Build the voice library? This runs one LLM call per card on the active profile and can take a while.')) return;
        $.ajax({
            url: '/BotChat/Capacity/BuildVoiceLibrary', method: 'POST', contentType: 'application/json',
            success: function (r) {
                if (r.success) { showToast('Voice library build started'); startVoicePoll(); }
                else showToast(r.error || 'Start failed', 'error');
            },
            error: function () { showToast('Start failed', 'error'); }
        });
    });

    $('#btnRerollSeeds').on('click', function () {
        if (!confirm('Reassign all pre-library (seed-era) personas onto library voices? Their current cards are REPLACED.')) return;
        $.ajax({
            url: '/BotChat/Capacity/RerollSeedPersonas', method: 'POST', contentType: 'application/json',
            success: function (r) {
                if (r.success) { showToast('Rerolled ' + r.rerolled + ' personas'); load(); }
                else showToast(r.error || 'Reroll failed', 'error');
            },
            error: function () { showToast('Reroll failed', 'error'); }
        });
    });

    $('#btnAddProfile').on('click', function () {
        $('#profileTable tbody').append(profileRow({
            id: 0, name: '', endpointUrl: 'http://', apiFlavor: 'ollama', modelReactive: '', modelAmbient: '',
            modelBatch: '', ctxBudgetTokens: 3000, concurrency: 2, reactiveReserved: 1,
            ambientRateMult: 1.0, active: false
        }));
    });

    load();
});