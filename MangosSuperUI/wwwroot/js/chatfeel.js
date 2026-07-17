// MangosSuperUI — Chat Feel JS (CHAT_ARCHITECTURE §14.3, Phase C1)
// Renders every §14.4 tunable from /BotChat/Settings/Data and writes each change
// straight through /BotChat/Settings/Set (one control = one chat_settings row).

$(function () {

    // Help UI styles — injected once so the row (?) toggles don't depend on edits to
    // Settings.cshtml. Uses the same CSS variables as the rest of the page.
    if (!document.getElementById('chatfeel-help-styles')) {
        var st = document.createElement('style');
        st.id = 'chatfeel-help-styles';
        st.textContent = [
            '.help-toggle { background: none; border: none; padding: 0 2px; cursor: pointer; color: var(--text-muted); font-size: 12px; line-height: 1; vertical-align: baseline; }',
            '.help-toggle:hover, .help-toggle.open { color: var(--accent); }',
            '.help-text { margin-top: 6px; padding: 8px 10px; border-left: 2px solid var(--accent); background: rgba(127,127,127,0.06); border-radius: 0 6px 6px 0; font-size: 12px; line-height: 1.55; color: var(--text-muted); max-width: 640px; }',
            '.setting-label .key { display: flex; align-items: center; gap: 4px; }'
        ].join('\n');
        document.head.appendChild(st);
    }

    // §14.3 group order + display titles. 'global' is excluded here: the kill switches
    // live BIG on the Capacity page; active_preset shows in the header badge.
    var GROUPS = [
        { id: 'density', title: 'Density', icon: 'fa-users', open: true },
        { id: 'responsiveness', title: 'Responsiveness', icon: 'fa-reply', open: true },
        { id: 'noise', title: 'Noise', icon: 'fa-dice', open: false },
        { id: 'voice', title: 'Voice & Typing Feel', icon: 'fa-keyboard', open: false },
        { id: 'topicality', title: 'Topicality', icon: 'fa-tags', open: false },
        { id: 'era', title: 'Era & Memory', icon: 'fa-brain', open: false, also: ['memory'] },
        {
            id: 'budget', title: 'Advanced — Budgets, Barks, LifeSim, Pairing, Tier 0',
            icon: 'fa-gears', open: false, also: ['barks', 'lifesim', 'pairing', 'tier0']
        }
    ];

    var CURVE_HOURS = ['02h', '06h', '10h', '14h', '18h', '22h'];

    function esc(s) { return $('<div>').text(s == null ? '' : String(s)).html(); }

    function showToast(msg, type) {
        var el = $('<div class="chat-toast ' + (type || 'success') + '">' + esc(msg) + '</div>');
        $('body').append(el);
        setTimeout(function () { el.fadeOut(200, function () { el.remove(); }); }, 2400);
    }

    // ===================== Load + render =====================

    function load() {
        $.ajax({
            url: '/BotChat/Settings/Data',
            method: 'GET',
            success: function (data) {
                $('#activePresetName').text(data.activePreset || '—');
                renderPresets(data.presets, data.activePreset);
                renderGroups(data.settings);
            },
            error: function () {
                $('#settingGroups').html('<div class="text-center p-4" style="color: var(--status-error);">Failed to load settings</div>');
            }
        });
    }

    function renderPresets(presets, active) {
        var sel = $('#presetSelect').empty();
        (presets || []).forEach(function (p) {
            var label = p.name + (p.builtin ? '' : ' (custom)');
            sel.append($('<option>').val(p.name).text(label));
        });
        if (active) sel.val(active);
    }

    function renderGroups(settings) {
        var byGroup = {};
        (settings || []).forEach(function (s) {
            (byGroup[s.group] = byGroup[s.group] || []).push(s);
        });

        var root = $('#settingGroups').empty();
        GROUPS.forEach(function (g) {
            var items = (byGroup[g.id] || []).slice();
            (g.also || []).forEach(function (extra) { items = items.concat(byGroup[extra] || []); });
            items = items.filter(function (s) { return s.type !== 'label'; });
            if (!items.length) return;

            var card = $('<div class="card setting-group' + (g.open ? '' : ' collapsed') + '" data-group="' + g.id + '">');
            var header = $(
                '<div class="card-header">' +
                '  <span><i class="fa-solid ' + g.icon + '" style="color: var(--accent);"></i> ' + esc(g.title) + '</span>' +
                '  <span><span class="group-count">' + items.length + ' settings</span> <i class="fa-solid fa-chevron-down chev"></i></span>' +
                '</div>');
            header.on('click', function () { card.toggleClass('collapsed'); });

            var body = $('<div class="card-body">');
            items.forEach(function (s) { body.append(renderRow(s)); });
            card.append(header).append(body);
            root.append(card);
        });
    }

    function renderRow(s) {
        var row = $('<div class="setting-row" data-key="' + esc(s.key) + '">');
        var hasHelp = s.help && s.help.length;
        var label = $(
            '<div class="setting-label">' +
            '  <div class="key">' + esc(s.key) +
            (hasHelp ? ' <button class="help-toggle" title="What does this do?" tabindex="0"><i class="fa-solid fa-circle-question"></i></button>' : '') +
            '  </div>' +
            '  <div class="meaning">' + esc(s.meaning) + '</div>' +
            (hasHelp ? '  <div class="help-text" hidden>' + esc(s.help) + '</div>' : '') +
            '</div>');
        if (hasHelp) {
            label.find('.help-toggle').on('click', function (e) {
                e.stopPropagation();
                label.find('.help-text').prop('hidden', function (_, h) { return !h; });
                $(this).toggleClass('open');
            });
        }
        row.append(label);

        if (s.type === 'bool') {
            var on = String(s.value).toLowerCase() === 'true';
            var cb = $('<input type="checkbox">').prop('checked', on);
            var state = $('<span class="state">').text(on ? 'ON' : 'OFF');
            cb.on('change', function () {
                var v = cb.prop('checked');
                state.text(v ? 'ON' : 'OFF');
                save(row, s.key, v ? 'true' : 'false');
            });
            row.append($('<div class="setting-control">'));
            row.append($('<div class="setting-value">').append($('<label class="bool-toggle">').append(cb).append(state)));
        }
        else if (s.type === 'curve') {
            var vals = String(s.value).split(',');
            var wrap = $('<div class="curve-inputs">');
            var inputs = [];
            CURVE_HOURS.forEach(function (h, i) {
                var inp = $('<input type="number" class="form-control">')
                    .attr({ min: s.min, max: s.max, step: s.step })
                    .val(parseFloat(vals[i] || '0'));
                inp.on('change', function () {
                    save(row, s.key, inputs.map(function (x) { return parseFloat(x.val() || 0); }).join(','));
                });
                inputs.push(inp);
                wrap.append($('<div class="curve-point">').append($('<label>').text(h)).append(inp));
            });
            row.append($('<div class="setting-control">').append(wrap));
            row.append($('<div class="setting-value">'));
        }
        else if (s.type === 'string') {
            var txt = $('<input type="text" class="form-control">').val(s.value);
            txt.on('change', function () { save(row, s.key, txt.val()); });
            row.append($('<div class="setting-control">').append(txt));
            row.append($('<div class="setting-value">'));
        }
        else { // float / int → slider + synced number box
            var slider = $('<input type="range">').attr({ min: s.min, max: s.max, step: s.step }).val(s.value);
            var num = $('<input type="number" class="form-control">').attr({ min: s.min, max: s.max, step: s.step }).val(s.value);
            slider.on('input', function () { num.val(slider.val()); });
            slider.on('change', function () { save(row, s.key, String(slider.val())); });
            num.on('change', function () { slider.val(num.val()); save(row, s.key, String(num.val())); });
            row.append($('<div class="setting-control">').append(slider));
            row.append($('<div class="setting-value">').append(num));
        }
        return row;
    }

    // ===================== Writes =====================

    function save(row, key, value) {
        row.addClass('dirty');
        $.ajax({
            url: '/BotChat/Settings/Set',
            method: 'POST',
            contentType: 'application/json',
            data: JSON.stringify({ key: key, value: value }),
            success: function (r) {
                row.removeClass('dirty');
                if (r.success) showToast(key + ' → ' + value);
                else showToast(r.error || 'Write failed', 'error');
            },
            error: function () { row.removeClass('dirty'); showToast('Write failed', 'error'); }
        });
    }

    // ===================== Presets =====================

    $('#btnApplyPreset').on('click', function () {
        var name = $('#presetSelect').val();
        if (!name) return;
        var btn = $(this).prop('disabled', true).html('<i class="fa-solid fa-spinner fa-spin"></i> Applying…');
        $.ajax({
            url: '/BotChat/Settings/ApplyPreset',
            method: 'POST',
            contentType: 'application/json',
            data: JSON.stringify({ name: name }),
            success: function (r) {
                btn.prop('disabled', false).html('<i class="fa-solid fa-play"></i> Apply');
                if (r.success) { showToast('Preset "' + name + '" applied (' + r.applied + ' settings)'); load(); }
                else showToast(r.error || 'Apply failed', 'error');
            },
            error: function () {
                btn.prop('disabled', false).html('<i class="fa-solid fa-play"></i> Apply');
                showToast('Apply failed', 'error');
            }
        });
    });

    $('#btnSavePreset').on('click', function () {
        var name = ($('#customPresetName').val() || '').trim();
        if (!name) { showToast('Enter a preset name first', 'error'); return; }
        $.ajax({
            url: '/BotChat/Settings/SavePreset',
            method: 'POST',
            contentType: 'application/json',
            data: JSON.stringify({ name: name }),
            success: function (r) {
                if (r.success) { showToast('Saved preset "' + name + '"'); $('#customPresetName').val(''); load(); }
                else showToast(r.error || 'Save failed', 'error');
            },
            error: function () { showToast('Save failed', 'error'); }
        });
    });

    load();
});