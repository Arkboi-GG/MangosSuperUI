// Spell Completer — upload an MSUIClient design session (spell-session.json),
// finalize each spell's data (name, icon, class tab, damage, mechanics, ranks),
// and rebuild the unified patch. Design phase lives in MSUIClient; this page is
// the data phase. Every field is PREFILLED from the inherited source spell
// (/SpellCompleter/SourceInfo); only what the user changes diverges from it.
(function () {
    'use strict';

    let session = null;          // parsed spell-session.json
    let skillTabs = {};          // key -> {skillId, classMask, spellFamilyName}
    let refs = { durations: [], castTimes: [], ranges: [] };
    let customIcons = [];        // [{name, fileName, path, webPath}]
    const sourceInfo = {};       // sourceSpellId -> SourceInfo payload

    const SCHOOLS = ['Physical', 'Holy', 'Fire', 'Nature', 'Frost', 'Shadow', 'Arcane'];

    // Curated vanilla ids — enough for direct damage, DoTs, slows, heals, drains.
    const EFFECT_TYPES = [
        { id: 0, label: 'None' },
        { id: 2, label: 'School Damage' },
        { id: 6, label: 'Apply Aura' },
        { id: 10, label: 'Heal' },
        { id: 30, label: 'Energize (restore mana)' },
        { id: 31, label: 'Weapon % Damage' }
    ];
    const AURA_TYPES = [
        { id: 3, label: 'Periodic Damage (DoT)', periodic: true },
        { id: 8, label: 'Periodic Heal (HoT)', periodic: true },
        { id: 53, label: 'Health Leech (drain)', periodic: true },
        { id: 33, label: 'Decrease Speed (slow)' },
        { id: 12, label: 'Stun' },
        { id: 26, label: 'Root' },
        { id: 7, label: 'Fear' }
    ];

    // ── session upload ──────────────────────────────────────────────────────

    const dropZone = document.getElementById('scDropZone');
    const fileInput = document.getElementById('scFileInput');

    dropZone.addEventListener('dragover', function (e) { e.preventDefault(); dropZone.classList.add('dragover'); });
    dropZone.addEventListener('dragleave', function () { dropZone.classList.remove('dragover'); });
    dropZone.addEventListener('drop', function (e) {
        e.preventDefault();
        dropZone.classList.remove('dragover');
        if (e.dataTransfer.files.length > 0) readSessionFile(e.dataTransfer.files[0]);
    });
    fileInput.addEventListener('change', function () {
        if (fileInput.files.length > 0) readSessionFile(fileInput.files[0]);
    });

    function readSessionFile(file) {
        const reader = new FileReader();
        reader.onload = function () {
            try {
                const parsed = JSON.parse(reader.result);
                if (parsed.format !== 'msui-spell-session' || !Array.isArray(parsed.spells)) {
                    if (parsed.spellId && Array.isArray(parsed.models)) {
                        setSummary('This is a per-spell tuning JSON (spell-' + parsed.spellId +
                            '-tuning.json) — it carries only the dial metadata, not the patched ' +
                            'models. In the creator’s Export section, type a temp name and click ' +
                            '"Add to session", then upload spell-session.json from the directory ' +
                            'MSUIClient was launched from.', true);
                    } else {
                        setSummary('Not an MSUIClient spell session file (missing format marker).', true);
                    }
                    return;
                }
                if (parsed.spells.length === 0) {
                    setSummary('Session is empty — add spells in the creator first.', true);
                    return;
                }
                session = parsed;
                setSummary(file.name + ' — ' + parsed.spells.length + ' spell(s), exported by ' +
                    (parsed.exportedBy || 'unknown'));
                loadSourceInfoThenRender();
            } catch (err) {
                setSummary('Could not parse the file: ' + err.message, true);
            }
        };
        reader.readAsText(file);
    }

    function setSummary(text, isError) {
        const el = document.getElementById('scSessionSummary');
        el.style.display = '';
        el.textContent = text;
        el.className = isError ? 'sc-err mt-2' : 'text-muted mt-2';
    }

    function loadSourceInfoThenRender() {
        const entries = session.spells.map(function (s) { return s.sourceSpellId; });
        let pending = entries.length;
        entries.forEach(function (entry) {
            if (sourceInfo[entry]) { if (--pending === 0) renderSpells(); return; }
            $.getJSON('/SpellCompleter/SourceInfo', { entry: entry })
                .done(function (res) { if (res.success) sourceInfo[entry] = res; })
                .always(function () { if (--pending === 0) renderSpells(); });
        });
        if (entries.length === 0) renderSpells();
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    function escapeHtml(s) {
        return String(s == null ? '' : s).replace(/[&<>"]/g, function (c) {
            return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c];
        });
    }

    function tabLabel(key) {
        return key.split('_').map(function (p) { return p.charAt(0).toUpperCase() + p.slice(1); }).join(' — ');
    }

    function modSummary(spell) {
        let edits = 0, disabled = 0, added = 0, hues = 0, tints = 0, swaps = 0, dials = 0;
        (spell.models || []).forEach(function (m) {
            const t = m.tuning || {};
            edits += (t.emitters || []).length;
            disabled += (t.disabledEmitters || []).length;
            added += (t.addedEmitters || []).length;
            hues += (t.textureHues || []).length;
            tints += (t.textureTints || []).length;
            swaps += (t.textureSwaps || []).length;
            const d = t.dials || {};
            if (d.hueShift || d.rateMultiplier !== 1 || d.scaleMultiplier !== 1 ||
                d.lifespanMultiplier !== 1 || d.speedMultiplier !== 1 || d.gravityAdd !== 0) dials++;
        });
        const parts = [(spell.models || []).length + ' model(s)'];
        if (dials) parts.push(dials + ' with model dials');
        if (edits) parts.push(edits + ' emitter edit(s)');
        if (added) parts.push(added + ' added emitter(s)');
        if (disabled) parts.push(disabled + ' disabled emitter(s)');
        if (hues) parts.push(hues + ' per-image hue(s)');
        if (tints) parts.push(tints + ' tint(s)');
        if (swaps) parts.push(swaps + ' texture swap(s)');
        if ((spell.tintedBlps || []).length) parts.push((spell.tintedBlps || []).length + ' recolored BLP(s)');
        return parts.join(' · ');
    }

    // Points shown to the user are min/max; SQL stores basePoints/dieSides.
    function pointsFromSql(bp, ds) { return { min: bp + 1, max: bp + ds + 1 }; }

    function effectSlotLabel(ef) {
        const type = EFFECT_TYPES.find(function (t) { return t.id === Number(ef.effect); });
        if (Number(ef.effect) === 6) {
            const aura = AURA_TYPES.find(function (a) { return a.id === Number(ef.aura); });
            return aura ? aura.label : 'Apply Aura #' + ef.aura;
        }
        return type ? type.label : 'effect #' + ef.effect;
    }

    // The $token cheat-sheet under the description box. The tokens are filled in
    // by the game CLIENT from the spell's own data — per spell, per rank — which
    // is why keeping them beats hardcoding numbers.
    function descLegend(src) {
        const general = [
            ['$s1 $s2 $s3', 'the VALUE of Effect 1 / 2 / 3 (damage, heal amount, slow %, …)'],
            ['$o1 $o2 $o3', 'the TOTAL of a periodic effect: per-tick value × number of ticks (e.g. full DoT damage)'],
            ['$t1 $t2 $t3', 'the tick interval of Effect 1 / 2 / 3, in seconds'],
            ['$d', 'the spell’s duration (from the Duration dropdown above)'],
            ['$a1 $a2 $a3', 'the radius of Effect 1 / 2 / 3, in yards']
        ];
        let html = '<div class="sc-legend-title">Placeholders — the game fills these from the ' +
            'spell’s own numbers, per rank. Keep them and every rank’s tooltip stays ' +
            'correct automatically; a hardcoded number would be wrong for the other ranks.</div>' +
            '<table class="sc-legend-table">';
        general.forEach(function (row) {
            html += '<tr><td><code>' + row[0] + '</code></td><td>' + escapeHtml(row[1]) + '</td></tr>';
        });
        html += '</table>';

        // What the tokens resolve to for THIS spell's slots, so "$s2" stops
        // being abstract ("$s2 = Effect 2: School Damage").
        const used = (src.effects || []).filter(function (ef) { return Number(ef.effect) !== 0; });
        if (used.length > 0) {
            html += '<div class="sc-legend-title mt-1">In this spell:</div><table class="sc-legend-table">';
            used.forEach(function (ef) {
                html += '<tr><td><code>$s' + ef.slot + '</code></td><td>Effect ' + ef.slot +
                    ' — ' + escapeHtml(effectSlotLabel(ef)) + '</td></tr>';
            });
            html += '</table>';
        }
        return html;
    }

    function selectHtml(f, options, selected, extra) {
        let html = '<select class="form-input" data-f="' + f + '"' + (extra || '') + '>';
        let found = false;
        options.forEach(function (o) {
            const sel = String(o.id) === String(selected);
            if (sel) found = true;
            html += '<option value="' + o.id + '"' + (sel ? ' selected' : '') + '>' +
                escapeHtml(o.label) + '</option>';
        });
        if (!found && selected != null && selected !== '')
            html += '<option value="' + selected + '" selected>(source: ' + selected + ')</option>';
        return html + '</select>';
    }

    function refOptions(list) {
        return list.map(function (r) { return { id: r.id, label: '#' + r.id + ' — ' + r.label }; });
    }

    function field(label, key, value, opts) {
        opts = opts || {};
        return '<div' + (opts.wide ? ' class="sc-wide"' : '') + '><label>' + label + '</label>' +
            '<input type="' + (opts.type || 'text') + '" class="form-input" data-f="' + key +
            '" value="' + escapeHtml(value == null ? '' : value) + '"' +
            (opts.placeholder ? ' placeholder="' + escapeHtml(opts.placeholder) + '"' : '') + ' /></div>';
    }

    // ── spell cards ─────────────────────────────────────────────────────────

    function renderSpells() {
        const list = document.getElementById('scSpellList');
        list.innerHTML = '';
        document.getElementById('scSpellsCard').style.display = '';
        document.getElementById('scBuildCard').style.display = '';

        session.spells.forEach(function (spell, i) {
            const src = sourceInfo[spell.sourceSpellId] || {};
            const card = document.createElement('div');
            card.className = 'sc-spell';
            card.dataset.index = i;
            card.innerHTML = buildCardHtml(spell, src);
            list.appendChild(card);
            wireEffectRows(card, src);
        });
    }

    function buildCardHtml(spell, src) {
        const tabOptions = [{ id: '', label: '(auto — tab matching the chosen school)' }]
            .concat(Object.keys(skillTabs).map(function (k) { return { id: k, label: tabLabel(k) }; }));
        const schoolOptions = SCHOOLS.map(function (s, idx) { return { id: idx, label: s }; });

        // ── icon picker: source icon (default) / school fallback / custom PNGs ──
        let iconHtml =
            '<label class="sc-icon-opt"><input type="radio" name="icon-' + spell.tempName +
            '" data-f="iconSource" value="source" checked /> Source icon' +
            (src.spellIconId ? ' (#' + src.spellIconId + ')' : '') + '</label>' +
            '<label class="sc-icon-opt"><input type="radio" name="icon-' + spell.tempName +
            '" data-f="iconSource" value="school" /> School icon</label>';
        if (customIcons.length > 0) {
            iconHtml += '<label class="sc-icon-opt"><input type="radio" name="icon-' + spell.tempName +
                '" data-f="iconSource" value="custom" /> Custom:</label>' +
                '<div class="sc-icon-grid">' +
                customIcons.map(function (ic) {
                    return '<img src="' + ic.webPath + '" title="' + escapeHtml(ic.name) +
                        '" class="sc-icon" data-icon-path="' + escapeHtml(ic.path) + '" />';
                }).join('') + '</div>';
        } else {
            iconHtml += '<span class="text-muted">(no custom icons yet — generate some in Spell Creator)</span>';
        }

        // ── effects: three slots, prefilled from the source spell ──
        let effectsHtml = '';
        (src.effects || []).forEach(function (ef) {
            const pts = pointsFromSql(Number(ef.basePoints), Number(ef.dieSides));
            effectsHtml +=
                '<div class="sc-effect" data-slot="' + ef.slot + '" data-src=\'' + JSON.stringify({
                    effect: Number(ef.effect), aura: Number(ef.aura), min: pts.min, max: pts.max,
                    amplitude: Number(ef.amplitude), misc: Number(ef.miscValue)
                }).replace(/'/g, '&#39;') + '\'>' +
                '<span class="sc-effect-n">Effect ' + ef.slot + '</span>' +
                '<div><label>Type</label>' + selectHtml('efType', EFFECT_TYPES, Number(ef.effect)) + '</div>' +
                '<div class="sc-aura"><label>Aura</label>' + selectHtml('efAura', AURA_TYPES, Number(ef.aura)) + '</div>' +
                '<div class="sc-pts"><label>Value min</label><input type="number" class="form-input" data-f="efMin" value="' + pts.min + '" /></div>' +
                '<div class="sc-pts"><label>Value max</label><input type="number" class="form-input" data-f="efMax" value="' + pts.max + '" /></div>' +
                '<div class="sc-amp"><label>Tick every (ms)</label><input type="number" class="form-input" data-f="efAmp" value="' + ef.amplitude + '" step="500" /></div>' +
                '<div class="sc-misc"><label>Misc</label><input type="number" class="form-input" data-f="efMisc" value="' + ef.miscValue + '" /></div>' +
                '<span class="sc-effect-hint" data-f="efHint"></span>' +
                '</div>';
        });
        if (!src.effects) effectsHtml = '<div class="text-muted">Source spell data unavailable — mechanics keep the inherited values.</div>';

        const ranksLabel = src.rankCount > 1
            ? 'Generate all ' + src.rankCount + ' ranks (scaled from your Rank 1)'
            : 'Generate all ranks';

        return '' +
            '<div class="sc-spell-head">' +
                '<span class="sc-temp"><i class="fa-solid fa-wand-sparkles"></i> ' + escapeHtml(spell.tempName) + '</span>' +
                '<span class="text-muted">designed from #' + spell.sourceSpellId + ' ' +
                    escapeHtml(spell.sourceSpellName || src.name || '') + '</span>' +
            '</div>' +
            '<div class="sc-mods">' + escapeHtml(modSummary(spell)) + '</div>' +

            '<div class="sc-section">Identity</div>' +
            '<div class="sc-grid">' +
                field('Spell name *', 'name', '') +
                field('Rank text', 'subtext', 'Rank 1') +
                '<div><label>School</label>' + selectHtml('school', schoolOptions, src.school != null ? src.school : 0) + '</div>' +
                '<div><label>Class / skill tab</label>' + selectHtml('tab', tabOptions, '') + '</div>' +
            '</div>' +

            '<div class="sc-section">Tooltip description</div>' +
            '<div class="sc-desc">' +
                '<textarea class="form-input" data-f="desc" rows="3" spellcheck="false">' +
                    escapeHtml(src.description || '') + '</textarea>' +
                '<div class="sc-legend">' + descLegend(src) + '</div>' +
            '</div>' +

            '<div class="sc-section">Icon</div>' +
            '<div class="sc-icon-row">' + iconHtml + '</div>' +

            '<div class="sc-section">Costs, timing &amp; range <span class="text-muted">(prefilled from the source spell)</span></div>' +
            '<div class="sc-grid">' +
                field('Mana cost', 'mana', src.manaCost, { type: 'number' }) +
                field('Spell level', 'level', src.spellLevel, { type: 'number' }) +
                field('Max level', 'maxLevel', src.maxLevel, { type: 'number' }) +
                field('Cooldown (ms)', 'cooldown', src.cooldown, { type: 'number' }) +
                '<div><label>Cast time</label>' + selectHtml('castTime', refOptions(refs.castTimes), src.castingTimeIndex) + '</div>' +
                '<div><label>Range</label>' + selectHtml('range', refOptions(refs.ranges), src.rangeIndex) + '</div>' +
                '<div><label>Duration (auras/DoTs)</label>' + selectHtml('duration', refOptions(refs.durations), src.durationIndex) + '</div>' +
            '</div>' +

            '<div class="sc-section">Mechanics <span class="text-muted">(damage, DoT, slow, heal — only changed slots are overridden)</span></div>' +
            effectsHtml +

            '<div class="sc-grid mt-2">' +
                '<div><label>&nbsp;</label><label class="sc-check">' +
                    '<input type="checkbox" data-f="ranks" ' + (src.rankCount > 1 ? 'checked' : '') + ' /> ' + ranksLabel + '</label></div>' +
                '<div><label>&nbsp;</label><label class="sc-check">' +
                    '<input type="checkbox" data-f="trainers" checked /> Copy source trainers</label></div>' +
            '</div>' +

            '<button class="btn-sm btn-outline-subtle mt-2" data-act="complete">' +
                '<i class="fa-solid fa-flag-checkered"></i> Create this spell</button>' +
            '<span class="sc-row-status" data-f="status"></span>';
    }

    // Show/hide aura & tick fields to match the chosen effect type, and hint at
    // conventions (negative % for slows).
    function wireEffectRows(card, src) {
        card.querySelectorAll('.sc-effect').forEach(function (row) {
            const typeSel = row.querySelector('[data-f="efType"]');
            const auraSel = row.querySelector('[data-f="efAura"]');
            function refresh() {
                const type = parseInt(typeSel.value, 10);
                const aura = parseInt(auraSel.value, 10);
                const isAura = type === 6;
                const auraDef = AURA_TYPES.find(function (a) { return a.id === aura; });
                row.querySelector('.sc-aura').style.display = isAura ? '' : 'none';
                row.querySelector('.sc-amp').style.display = isAura && auraDef && auraDef.periodic ? '' : 'none';
                const showPts = type !== 0 && !(isAura && (aura === 12 || aura === 26 || aura === 7));
                row.querySelectorAll('.sc-pts').forEach(function (p) { p.style.display = showPts ? '' : 'none'; });
                const hint = row.querySelector('[data-f="efHint"]');
                hint.textContent =
                    type === 0 ? 'slot unused' :
                    isAura && aura === 33 ? 'value is a % speed change — use negatives, e.g. -50 = 50% slower' :
                    isAura && auraDef && auraDef.periodic ? 'value is damage/heal PER TICK' :
                    type === 31 ? 'value is % of weapon damage' : '';
            }
            typeSel.addEventListener('change', refresh);
            auraSel.addEventListener('change', refresh);
            refresh();
        });

        // custom icon grid: clicking a thumbnail selects it and flips the radio
        card.querySelectorAll('.sc-icon').forEach(function (img) {
            img.addEventListener('click', function () {
                card.querySelectorAll('.sc-icon').forEach(function (o) { o.classList.remove('selected'); });
                img.classList.add('selected');
                const radio = card.querySelector('[data-f="iconSource"][value="custom"]');
                if (radio) radio.checked = true;
            });
        });
    }

    // ── completing a spell ──────────────────────────────────────────────────

    document.getElementById('scSpellList').addEventListener('click', function (e) {
        const btn = e.target.closest('button[data-act="complete"]');
        if (!btn) return;
        completeSpell(btn.closest('.sc-spell'), btn);
    });

    function val(card, key) { const el = card.querySelector('[data-f="' + key + '"]'); return el ? el.value.trim() : ''; }
    function num(card, key) { const v = val(card, key); return v === '' ? null : parseInt(v, 10); }
    function checked(card, key) { const el = card.querySelector('[data-f="' + key + '"]'); return !!(el && el.checked); }

    function changedEffects(card) {
        const out = [];
        card.querySelectorAll('.sc-effect').forEach(function (row) {
            const src = JSON.parse(row.dataset.src);
            const cur = {
                effect: parseInt(row.querySelector('[data-f="efType"]').value, 10) || 0,
                aura: parseInt(row.querySelector('[data-f="efAura"]').value, 10) || 0,
                min: parseInt(row.querySelector('[data-f="efMin"]').value, 10),
                max: parseInt(row.querySelector('[data-f="efMax"]').value, 10),
                amplitude: parseInt(row.querySelector('[data-f="efAmp"]').value, 10) || 0,
                misc: parseInt(row.querySelector('[data-f="efMisc"]').value, 10) || 0
            };
            const effAura = cur.effect === 6 ? cur.aura : 0;
            const srcAura = src.effect === 6 ? src.aura : 0;
            if (cur.effect === src.effect && effAura === srcAura && cur.min === src.min &&
                cur.max === src.max && cur.amplitude === src.amplitude && cur.misc === src.misc)
                return;   // untouched slot — inherit
            out.push({
                slot: parseInt(row.dataset.slot, 10),
                effect: cur.effect,
                aura: cur.aura,
                pointsMin: isNaN(cur.min) ? null : cur.min,
                pointsMax: isNaN(cur.max) ? null : cur.max,
                amplitude: cur.amplitude || null,
                miscValue: cur.misc
            });
        });
        return out;
    }

    function completeSpell(card, btn) {
        const spell = session.spells[parseInt(card.dataset.index, 10)];
        const src = sourceInfo[spell.sourceSpellId] || {};
        const status = card.querySelector('[data-f="status"]');
        const name = val(card, 'name');
        if (!name) {
            status.textContent = 'Spell name is required.';
            status.className = 'sc-row-status sc-err';
            return;
        }

        const iconRadio = card.querySelector('[data-f="iconSource"]:checked');
        const selectedIcon = card.querySelector('.sc-icon.selected');
        const iconSource = iconRadio ? iconRadio.value : 'source';

        const body = {
            tempName: spell.tempName,
            sourceSpellEntry: spell.sourceSpellId,
            exportedAtUtc: spell.exportedAtUtc,
            spellName: name,
            nameSubtext: val(card, 'subtext') || 'Rank 1',
            // Unchanged text = inherit (null): the source description stays the
            // single source of truth unless the user actually edited it.
            description: (function () {
                const d = val(card, 'desc');
                return d && d !== (src.description || '').trim() ? d : null;
            })(),
            school: parseInt(val(card, 'school'), 10) || 0,
            skillTabKey: val(card, 'tab') || null,
            manaCost: num(card, 'mana'),
            spellLevel: num(card, 'level'),
            maxLevel: num(card, 'maxLevel'),
            cooldown: num(card, 'cooldown'),
            castingTimeIndex: num(card, 'castTime'),
            rangeIndex: num(card, 'range'),
            durationIndex: num(card, 'duration'),
            generateAllRanks: checked(card, 'ranks'),
            copySourceTrainers: checked(card, 'trainers'),
            iconSource: iconSource,
            iconPath: iconSource === 'custom' && selectedIcon ? selectedIcon.dataset.iconPath : null,
            effects: changedEffects(card),
            models: (spell.models || []).map(function (m) {
                return { path: m.path, phases: m.phases, m2Base64: m.m2Base64 || null };
            }),
            tintedBlps: (spell.tintedBlps || []).map(function (b) {
                return { path: b.path, blpBase64: b.blpBase64 || null };
            })
        };

        if (iconSource === 'custom' && !selectedIcon) {
            status.textContent = 'Pick a custom icon from the grid (or choose Source/School).';
            status.className = 'sc-row-status sc-err';
            return;
        }

        btn.disabled = true;
        status.textContent = 'Creating…';
        status.className = 'sc-row-status text-muted';

        $.ajax({
            url: '/SpellCompleter/Complete',
            method: 'POST',
            contentType: 'application/json',
            data: JSON.stringify(body)
        }).done(function (res) {
            if (res.success) {
                status.innerHTML = '<i class="fa-solid fa-check"></i> Created as #' + res.spellEntry +
                    (res.ranksGenerated ? ' with ' + res.ranksGenerated + ' rank(s)' : '') +
                    ' — ' + res.m2Count + ' patched model(s), ' + res.extraFileCount + ' extra file(s), ' +
                    body.effects.length + ' mechanic override(s) stored. ' +
                    '<b>Restart the world server</b> (spell/trainer tables load at startup) and delete ' +
                    'the client’s WDB cache after installing the rebuilt patch.';
                status.className = 'sc-row-status sc-done';
                document.getElementById('btnRebuildPatch').disabled = false;
            } else {
                status.textContent = res.error || 'Failed.';
                status.className = 'sc-row-status sc-err';
                btn.disabled = false;
            }
        }).fail(function (xhr) {
            status.textContent = 'Request failed (' + xhr.status + ').';
            status.className = 'sc-row-status sc-err';
            btn.disabled = false;
        });
    }

    // ── patch rebuild ───────────────────────────────────────────────────────

    document.getElementById('btnRebuildPatch').addEventListener('click', function () {
        const btn = this;
        const status = document.getElementById('scBuildStatus');
        btn.disabled = true;
        status.textContent = 'Rebuilding unified patch — this reads clean DBCs and repatches every custom spell…';

        $.ajax({ url: '/Patch/RebuildClientPatch', method: 'POST' })
            .done(function (res) {
                if (res.success) {
                    status.innerHTML = '<span class="sc-done"><i class="fa-solid fa-check"></i> ' +
                        'Built ' + escapeHtml(res.patchFileName || 'patch-3.MPQ') + ' — ' +
                        (res.totalFiles || '?') + ' file(s), ' + (res.spellsIncluded || '?') + ' spell(s).</span>' +
                        // A rebuild can succeed overall while individual spells fail
                        // (bad source visual, missing rank, ...) — those must be seen.
                        ((res.errors && res.errors.length)
                            ? '<div class="sc-err mt-1">Per-spell problems: ' +
                              escapeHtml(res.errors.join(' · ')) + '</div>'
                            : '');
                    const link = document.getElementById('scDownloadLink');
                    link.href = '/Patch/Download?file=' + encodeURIComponent(res.patchFileName || 'patch-3.MPQ');
                    document.getElementById('scDownloadRow').style.display = '';
                } else {
                    status.innerHTML = '<span class="sc-err">' +
                        escapeHtml((res.errors || []).join('; ') || 'Rebuild failed.') + '</span>';
                }
                btn.disabled = false;
            })
            .fail(function (xhr) {
                status.innerHTML = '<span class="sc-err">Rebuild request failed (' + xhr.status + ').</span>';
                btn.disabled = false;
            });
    });

    // ── boot: reference data, then render whenever the session is ready ─────

    let bootPending = 3;
    function bootDone() { if (--bootPending === 0 && session) renderSpells(); }

    $.getJSON('/Patch/SkillTabMap').done(function (res) {
        skillTabs = {};
        (res && res.tabs ? res.tabs : []).forEach(function (t) { skillTabs[t.key] = t; });
    }).always(bootDone);

    $.getJSON('/SpellCompleter/Refs').done(function (res) {
        if (res) refs = res;
    }).always(bootDone);

    $.getJSON('/Patch/CustomIcons').done(function (res) {
        customIcons = (res && res.icons) ? res.icons : [];
    }).always(bootDone);
})();
