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
            '.setting-label .key { display: flex; align-items: center; gap: 4px; }',
            // --- knob import/export (2026-07-20) ---
            '.knobio-bar { display: flex; gap: 8px; align-items: center; margin: 0 0 12px 0; flex-wrap: wrap; }',
            '.knobio-backdrop { position: fixed; inset: 0; background: rgba(0,0,0,0.55); z-index: 4000; display: flex; align-items: center; justify-content: center; padding: 24px; }',
            '.knobio-modal { background: var(--bg-panel, #1c1f26); color: var(--text, #e8e8e8); border: 1px solid var(--border, #333); border-radius: 10px; width: min(900px, 100%); max-height: 88vh; display: flex; flex-direction: column; box-shadow: 0 18px 60px rgba(0,0,0,0.6); }',
            '.knobio-head { display: flex; align-items: center; justify-content: space-between; gap: 12px; padding: 14px 16px; border-bottom: 1px solid var(--border, #333); }',
            '.knobio-head h3 { margin: 0; font-size: 15px; }',
            '.knobio-sub { font-size: 12px; color: var(--text-muted, #9aa0aa); margin-top: 3px; line-height: 1.5; }',
            '.knobio-body { padding: 14px 16px; overflow: auto; }',
            '.knobio-body textarea { width: 100%; min-height: 340px; font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace; font-size: 12px; line-height: 1.5; background: rgba(127,127,127,0.08); color: inherit; border: 1px solid var(--border, #333); border-radius: 6px; padding: 10px; resize: vertical; }',
            '.knobio-foot { display: flex; gap: 8px; justify-content: flex-end; padding: 12px 16px; border-top: 1px solid var(--border, #333); flex-wrap: wrap; }',
            '.knobio-diff { margin-top: 10px; font-size: 12px; max-height: 220px; overflow: auto; border: 1px solid var(--border, #333); border-radius: 6px; }',
            '.knobio-diff table { width: 100%; border-collapse: collapse; }',
            '.knobio-diff td { padding: 4px 8px; border-bottom: 1px solid rgba(127,127,127,0.15); font-family: ui-monospace, Menlo, Consolas, monospace; }',
            '.knobio-diff td.k { color: var(--text-muted, #9aa0aa); }',
            '.knobio-diff td.old { color: #d98a8a; text-decoration: line-through; }',
            '.knobio-diff td.new { color: #8ad9a0; }',
            '.knobio-diff .bad { color: #e6aa3c; }'
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

    // Last payload from /BotChat/Settings/Data — the source of truth for Export, and the
    // whitelist an Import is validated against (unknown keys are rejected, never written blind).
    var currentData = { settings: [], presets: [], activePreset: null };

    function load() {
        $.ajax({
            url: '/BotChat/Settings/Data',
            method: 'GET',
            success: function (data) {
                currentData = data || currentData;
                $('#activePresetName').text(data.activePreset || '—');
                renderPresets(data.presets, data.activePreset);
                renderGroups(data.settings);
                ensureKnobIoBar();
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


    // ===================== Knob Import / Export (2026-07-20) =====================
    //
    // WHY: tuning the chat feel means moving several knobs together and judging the result,
    // then moving them again. Doing that one slider at a time — and having no way to hand the
    // whole state to someone else — made every tuning pass slow and unreproducible. These two
    // buttons make the knob set a value you can copy, paste, share and roll back.
    //
    // Deliberately 100% client-side: it reads what /BotChat/Settings/Data already returns and
    // writes through the existing /BotChat/Settings/Set. No new endpoint, no controller change,
    // no SQL, nothing for an operator to edit on disk.

    function settingsMap() {
        var m = {};
        (currentData.settings || []).forEach(function (s) { m[s.key] = String(s.value); });
        return m;
    }

    function settingsIndex() {
        var m = {};
        (currentData.settings || []).forEach(function (s) { m[s.key] = s; });
        return m;
    }

    function buildExport() {
        var idx = settingsIndex();
        var out = {
            _comment: 'MangosSuperUI chat knobs. Edit the values under "settings" and paste back via Import. ' +
                'The "_schema" block is reference only (type/range/meaning) and is ignored on import.',
            exportedUtc: new Date().toISOString(),
            activePreset: currentData.activePreset || null,
            settings: settingsMap(),
            _schema: {}
        };
        Object.keys(idx).sort().forEach(function (k) {
            var s = idx[k];
            var meta = { type: s.type, meaning: s.meaning };
            if (s.min !== undefined && s.min !== null) meta.min = s.min;
            if (s.max !== undefined && s.max !== null) meta.max = s.max;
            if (s.step !== undefined && s.step !== null) meta.step = s.step;
            out._schema[k] = meta;
        });
        // Re-key settings in sorted order so two exports diff cleanly.
        var sorted = {};
        Object.keys(out.settings).sort().forEach(function (k) { sorted[k] = out.settings[k]; });
        out.settings = sorted;
        return JSON.stringify(out, null, 2);
    }

    function closeModal() { $('.knobio-backdrop').remove(); $(document).off('keydown.knobio'); }

    function openModal(title, subtitle, textValue, readOnly, footButtons) {
        closeModal();
        var backdrop = $('<div class="knobio-backdrop">');
        var modal = $('<div class="knobio-modal">');
        var head = $('<div class="knobio-head">')
            .append($('<div>')
                .append($('<h3>').text(title))
                .append($('<div class="knobio-sub">').text(subtitle)))
            .append($('<button class="btn btn-sm">').html('<i class="fa-solid fa-xmark"></i>').on('click', closeModal));
        var ta = $('<textarea spellcheck="false">').val(textValue).prop('readonly', !!readOnly);
        var body = $('<div class="knobio-body">').append(ta);
        var foot = $('<div class="knobio-foot">');
        (footButtons || []).forEach(function (b) {
            foot.append($('<button class="btn btn-sm ' + (b.cls || '') + '">').html(b.label).on('click', function () { b.fn(ta, body, foot); }));
        });
        foot.append($('<button class="btn btn-sm">').text('Close').on('click', closeModal));
        modal.append(head).append(body).append(foot);
        backdrop.append(modal).appendTo('body');
        backdrop.on('click', function (e) { if (e.target === backdrop[0]) closeModal(); });
        $(document).on('keydown.knobio', function (e) { if (e.key === 'Escape') closeModal(); });
        if (readOnly) { ta.trigger('focus'); ta[0].select(); }
        return ta;
    }

    // ---------- Export ----------

    function doExport() {
        var text = buildExport();
        openModal(
            'Export chat knobs',
            'Every current setting as JSON. Copy this to share it, keep it as a rollback point, or hand it over for tuning.',
            text, true,
            [{
                label: '<i class="fa-solid fa-copy"></i> Copy to clipboard',
                cls: 'btn-primary',
                fn: function (ta) {
                    ta[0].select();
                    if (navigator.clipboard && navigator.clipboard.writeText) {
                        navigator.clipboard.writeText(ta.val())
                            .then(function () { showToast('Copied ' + Object.keys(settingsMap()).length + ' settings'); })
                            .catch(function () { showToast('Select-all + Ctrl+C', 'error'); });
                    } else {
                        try { document.execCommand('copy'); showToast('Copied'); }
                        catch (e) { showToast('Select-all + Ctrl+C', 'error'); }
                    }
                }
            }]
        );
    }

    // ---------- Import ----------

    // Accepts EITHER a full export object ({ settings: {...} }) or a bare { key: value } map,
    // so a hand-written patch touching three knobs is as valid as a full round-trip.
    function parseIncoming(raw) {
        var obj = JSON.parse(raw);
        if (obj && typeof obj === 'object' && obj.settings && typeof obj.settings === 'object') return obj.settings;
        if (obj && typeof obj === 'object') {
            var copy = {};
            Object.keys(obj).forEach(function (k) { if (k.charAt(0) !== '_' && k !== 'exportedUtc' && k !== 'activePreset') copy[k] = obj[k]; });
            return copy;
        }
        throw new Error('not a JSON object');
    }

    function computeDiff(incoming) {
        var known = settingsIndex();
        var cur = settingsMap();
        var changes = [], unknown = [], outOfRange = [];
        Object.keys(incoming).forEach(function (k) {
            var v = String(incoming[k]);
            if (!known[k]) { unknown.push(k); return; }
            var meta = known[k];
            if (meta.type === 'int' || meta.type === 'float') {
                var n = parseFloat(v);
                if (isNaN(n)) { outOfRange.push(k + ' = ' + v + ' (not a number)'); return; }
                if (meta.min !== undefined && meta.min !== null && n < parseFloat(meta.min)) { outOfRange.push(k + ' = ' + v + ' (min ' + meta.min + ')'); return; }
                if (meta.max !== undefined && meta.max !== null && n > parseFloat(meta.max)) { outOfRange.push(k + ' = ' + v + ' (max ' + meta.max + ')'); return; }
            }
            if (cur[k] !== v) changes.push({ key: k, from: cur[k], to: v });
        });
        return { changes: changes, unknown: unknown, outOfRange: outOfRange };
    }

    function renderDiff(d) {
        var wrap = $('<div class="knobio-diff">');
        if (!d.changes.length && !d.unknown.length && !d.outOfRange.length) {
            return wrap.append($('<div style="padding:8px 10px;">').text('No differences — pasted values match what is already set.'));
        }
        var tbl = $('<table>');
        d.changes.forEach(function (c) {
            tbl.append($('<tr>')
                .append($('<td class="k">').text(c.key))
                .append($('<td class="old">').text(c.from))
                .append($('<td class="new">').text('\u2192 ' + c.to)));
        });
        d.outOfRange.forEach(function (m) {
            tbl.append($('<tr>').append($('<td class="bad" colspan="3">').text('SKIP (out of range): ' + m)));
        });
        d.unknown.forEach(function (k) {
            tbl.append($('<tr>').append($('<td class="bad" colspan="3">').text('SKIP (unknown key): ' + k)));
        });
        return wrap.append(tbl);
    }

    // Sequential writes: one /Settings/Set per changed key, in order, stopping on the first
    // hard failure. 40-odd knobs is a blink, and sequential keeps the [CHAT-SET] audit trail
    // readable instead of interleaving 40 parallel writes.
    function applyChanges(changes, done) {
        var applied = 0, failed = [];
        function next(i) {
            if (i >= changes.length) { done(applied, failed); return; }
            var c = changes[i];
            $.ajax({
                url: '/BotChat/Settings/Set',
                method: 'POST',
                contentType: 'application/json',
                data: JSON.stringify({ key: c.key, value: c.to }),
                success: function (r) {
                    if (r && r.success) applied++; else failed.push(c.key + ': ' + ((r && r.error) || 'write failed'));
                    next(i + 1);
                },
                error: function () { failed.push(c.key + ': request failed'); next(i + 1); }
            });
        }
        next(0);
    }

    function doImport() {
        var ta = openModal(
            'Import chat knobs',
            'Paste a full export, or just the keys you want to change. Nothing is written until you hit Preview and then Apply. Unknown keys and out-of-range values are skipped, not written.',
            '', false,
            [
                {
                    label: '<i class="fa-solid fa-list-check"></i> Preview changes',
                    cls: '',
                    fn: function (ta2, body) {
                        body.find('.knobio-diff').remove();
                        var incoming;
                        try { incoming = parseIncoming(ta2.val()); }
                        catch (e) { showToast('Invalid JSON: ' + e.message, 'error'); return; }
                        var d = computeDiff(incoming);
                        body.append(renderDiff(d));
                        window.__knobioPending = d;
                        showToast(d.changes.length + ' change(s) ready');
                    }
                },
                {
                    label: '<i class="fa-solid fa-check"></i> Apply',
                    cls: 'btn-primary',
                    fn: function (ta2, body) {
                        var d = window.__knobioPending;
                        if (!d) {
                            try { d = computeDiff(parseIncoming(ta2.val())); }
                            catch (e) { showToast('Invalid JSON: ' + e.message, 'error'); return; }
                        }
                        if (!d.changes.length) { showToast('Nothing to apply', 'error'); return; }
                        if (!confirm('Apply ' + d.changes.length + ' setting change(s)?')) return;
                        applyChanges(d.changes, function (applied, failed) {
                            window.__knobioPending = null;
                            if (failed.length) showToast(applied + ' applied, ' + failed.length + ' failed \u2014 see console', 'error');
                            else showToast(applied + ' setting(s) applied');
                            if (failed.length) console.warn('[knob import] failures:', failed);
                            closeModal();
                            load();
                        });
                    }
                }
            ]
        );
        ta.attr('placeholder', '{\n  "settings": {\n    "responsiveness.urge_threshold": "2.0",\n    "voice.hold_max_ms": "9000"\n  }\n}');
        window.__knobioPending = null;
    }

    // ---------- toolbar ----------

    function ensureKnobIoBar() {
        if ($('#knobIoBar').length) return;
        var bar = $('<div class="knobio-bar" id="knobIoBar">')
            .append($('<button class="btn btn-sm" id="btnKnobExport">')
                .html('<i class="fa-solid fa-file-export"></i> Export settings'))
            .append($('<button class="btn btn-sm" id="btnKnobImport">')
                .html('<i class="fa-solid fa-file-import"></i> Import settings'))
            .append($('<span style="font-size:12px;color:var(--text-muted,#9aa0aa);">')
                .text('Copy the whole knob set as JSON, or paste one back in.'));
        $('#settingGroups').before(bar);
        $('#btnKnobExport').on('click', doExport);
        $('#btnKnobImport').on('click', doImport);
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