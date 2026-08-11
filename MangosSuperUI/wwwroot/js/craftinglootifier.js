// Crafting Lootifier — page logic.
// Additive, per-band customizable, with a Professions tab for browsing recipes
// and batch-lootifying a whole profession (equippable outputs only).
(function () {
    'use strict';

    var meta = null;
    var selected = {};              // entry -> { entry, name, quality, baseTypes:[] }
    var browsePage = 1;
    var rollbackTarget = null;
    var selectedProf = null;

    function esc(s) {
        return String(s == null ? '' : s)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;')
            .replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    }

    function showToast(msg, type) {
        var el = $('<div class="lf-toast ' + (type || '') + '">' + esc(msg) + '</div>');
        $('body').append(el);
        setTimeout(function () { el.fadeOut(300, function () { el.remove(); }); }, 4500);
    }

    function bucketOf(pct) {
        if (pct >= 40) return 3;
        if (pct >= 30) return 2;
        if (pct >= 20) return 1;
        return 0;
    }

    // Tier now comes from the band NAME, so recognized names accept any boost range.
    function isKnownTier(label) {
        return /god|legend|glory|power|improv/.test((label || '').toLowerCase());
    }

    // ===================== REUSABLE RULESET EDITOR =====================
    // One instance per tab (Generate + Professions), each bound to its own element
    // ids, so a profession batch can carry its own band tuning.
    function RulesetEditor(ids) {
        var state = { bands: [], offset: 0 };

        function renderBands() {
            var h = '';
            state.bands.forEach(function (b, i) {
                h += '<div class="lf-band-row" data-band="' + i + '">' +
                    '<input type="text" class="form-input b-label" value="' + esc(b.label) + '" />' +
                    '<select class="form-input b-pos">' +
                    '<option value="prefix"' + (b.position === 'prefix' ? ' selected' : '') + '>Pre</option>' +
                    '<option value="suffix"' + (b.position === 'suffix' ? ' selected' : '') + '>Suf</option>' +
                    '</select>' +
                    '<span class="boost"><input type="number" class="form-input b-min" value="' + b.minBoostPct + '" min="0" max="200" />' +
                    '<span class="text-muted">-</span>' +
                    '<input type="number" class="form-input b-max" value="' + b.maxBoostPct + '" min="0" max="200" /></span>' +
                    '<input type="number" class="form-input b-slots" value="' + b.slots + '" min="0" max="30" />' +
                    '<input type="number" class="form-input b-gold" value="' + (b.goldBumpPct != null ? b.goldBumpPct : '') + '" min="0" max="10000" step="5" placeholder="curve" style="width:64px" title="Gold price bump above base (%) for this tier. Blank = legacy boost curve." />' +
                    '<input type="number" class="form-input b-dps" value="' + (b.dpsBumpPct != null ? b.dpsBumpPct : '') + '" min="0" max="500" step="0.5" placeholder="0" style="width:60px" title="Weapon DAMAGE bump above base (%) for this tier (weapons only; speed unchanged). Blank = damage left as-is." />' +
                    '<span class="rmBand" data-band="' + i + '"><i class="fa-solid fa-xmark"></i></span>' +
                    '</div>';
            });
            $('#' + ids.bandEditor).html(h);
            validateBands();
        }

        function collectBands() {
            var out = [];
            $('#' + ids.bandEditor + ' .lf-band-row').each(function () {
                var gold = parseFloat($(this).find('.b-gold').val());
                var dps = parseFloat($(this).find('.b-dps').val());
                out.push({
                    label: $(this).find('.b-label').val() || '',
                    position: $(this).find('.b-pos').val() || 'suffix',
                    minBoostPct: parseFloat($(this).find('.b-min').val()) || 0,
                    maxBoostPct: parseFloat($(this).find('.b-max').val()) || 0,
                    slots: parseInt($(this).find('.b-slots').val()) || 0,
                    goldBumpPct: isNaN(gold) ? null : Math.max(0, gold),
                    dpsBumpPct: isNaN(dps) ? null : Math.max(0, dps)
                });
            });
            return out;
        }

        function validateBands() {
            var warns = [];
            collectBands().forEach(function (b) {
                if (b.maxBoostPct <= b.minBoostPct) { warns.push('"' + b.label + '": max must exceed min.'); return; }
                if (!isKnownTier(b.label) && bucketOf(b.minBoostPct) !== bucketOf(b.maxBoostPct - 0.001)) {
                    warns.push('"' + b.label + '" is a custom band name, so its tier falls back to boost % — and ' + b.minBoostPct + '-' + b.maxBoostPct + '% crosses a boundary. Put Improved/Power/Glory/Gods in the name, or keep its range inside one of <20 / 20-30 / 30-40 / \u226540.');
                }
            });
            if (warns.length) $('#' + ids.bandWarn).html(warns.join('<br>')).show();
            else $('#' + ids.bandWarn).hide();
        }

        // Offset slider — shifts every band's min/max by the same flat amount, so the
        // whole ladder slides together, spacing preserved, no overlap. Each band keeps
        // its offset-0 base so sliding is drift-free and reversible; manual edits
        // re-anchor that base.
        function applyOffset(off) {
            state.offset = off;
            state.bands.forEach(function (b) {
                b.minBoostPct = Math.max(0, Math.round((b._baseMin + off) * 10) / 10);
                b.maxBoostPct = Math.max(0, Math.round((b._baseMax + off) * 10) / 10);
            });
            renderBands();
            $('#' + ids.offsetVal).text((off >= 0 ? '+' : '') + off);
        }

        function syncFromInputs() {
            var cur = collectBands();
            cur.forEach(function (b) {
                b._baseMin = b.minBoostPct - state.offset;
                b._baseMax = b.maxBoostPct - state.offset;
            });
            state.bands = cur;
        }

        // Self-bootstrap the Gold value % input if the .cshtml doesn't render one
        // (mirrors the Quest Lootifier's band-editor injection pattern). Anchors
        // after the Legendary checkbox's label; falls back to above the band editor.
        function ensureGoldInput() {
            ensureBandGridCss();
            var inputCss = 'width:64px;padding:3px 5px;border-radius:5px;border:1px solid rgba(128,128,128,.35);background:rgba(0,0,0,.18);color:inherit;';
            var labelCss = 'font-size:11px;opacity:.85;display:inline-flex;align-items:center;gap:6px;margin-left:10px;';
            var $anchor = $('#' + ids.legendary).closest('label');
            if (!$anchor.length) $anchor = $('#' + ids.legendary);
            if ($('#' + ids.legGold).length === 0) {
                var lh = '<label style="' + labelCss + '" title="Gold price bump above base (%) for legendary (quality 5) variants. 400 \u2248 old stock behavior.">' +
                    'Legendary gold +% <input id="' + ids.legGold + '" type="number" min="0" max="10000" step="25" value="400" style="' + inputCss + '" /></label>';
                if ($anchor.length) $(lh).insertAfter($anchor);
                else $('#' + ids.bandEditor).before(lh);
            }
            if ($('#' + ids.gold).length === 0) {
                var gh = '<label style="' + labelCss + '" title="Master scale on ALL gold bumps: 100% = as entered, 0% = prices unchanged, 200% = double every bump.">' +
                    'Gold scale % <input id="' + ids.gold + '" type="number" min="0" max="1000" step="5" value="100" style="' + inputCss + '" /></label>';
                var $g = $('#' + ids.legGold).closest('label');
                if ($g.length) $(gh).insertAfter($g);
                else if ($anchor.length) $(gh).insertAfter($anchor);
                else $('#' + ids.bandEditor).before(gh);
            }
            // Legendary weapon DAMAGE bump (%), parallel to legendary gold +%.
            if (ids.legDps && $('#' + ids.legDps).length === 0) {
                var dh = '<label style="' + labelCss + '" title="Weapon DAMAGE bump above base (%) for legendary (quality 5) weapons (speed unchanged). Nominal \u2014 vanilla legendaries were hand-tuned.">' +
                    'Legendary DPS +% <input id="' + ids.legDps + '" type="number" min="0" max="500" step="0.5" value="30" style="' + inputCss + '" /></label>';
                var $lg = $('#' + ids.legGold).closest('label');
                if ($lg.length) $(dh).insertAfter($lg);
                else if ($anchor.length) $(dh).insertAfter($anchor);
                else $('#' + ids.bandEditor).before(dh);
            }
        }

        // The band grid gains a DPS +% column (now 7 cells: name/pos/boost/slots/
        // gold/dps/x). Pin the layout here so it's right regardless of the cshtml.
        function ensureBandGridCss() {
            if (document.getElementById('lfBandGridCss')) return;
            $('<style id="lfBandGridCss">' +
                '#bandEditor .lf-band-row, #pBandEditor .lf-band-row {' +
                ' display:grid;' +
                ' grid-template-columns: 1fr 54px 88px 44px 60px 60px 22px;' +
                ' gap:6px; align-items:center; }' +
                '</style>').appendTo('head');
        }

        state.seed = function (dr) {
            state.offset = 0;
            state.bands = (dr.bands || []).map(function (b) {
                var c = $.extend({}, b);
                c._baseMin = c.minBoostPct;
                c._baseMax = c.maxBoostPct;
                return c;
            });
            $('#' + ids.bump).val(dr.existingBumpBias);
            $('#' + ids.bumpVal).text(Number(dr.existingBumpBias).toFixed(2));
            $('#' + ids.allowNew).prop('checked', dr.allowNewAffixes);
            $('#' + ids.maxAffix).val(dr.maxAffixCountChange);
            $('#' + ids.legendary).prop('checked', dr.includeLegendaryBand);
            ensureGoldInput();
            $('#' + ids.gold).val(dr.goldValueScalePct != null ? dr.goldValueScalePct : 100);
            $('#' + ids.legGold).val(dr.legendaryGoldBumpPct != null ? dr.legendaryGoldBumpPct : 400);
            if (ids.legDps) $('#' + ids.legDps).val(dr.legendaryDpsBumpPct != null ? dr.legendaryDpsBumpPct : 30);
            $('#' + ids.offset).val(0);
            $('#' + ids.offsetVal).text('+0');
            renderBands();
        };

        state.collect = function () {
            var gold = parseFloat($('#' + ids.gold).val());
            if (isNaN(gold)) gold = 100;
            var legGold = parseFloat($('#' + ids.legGold).val());
            if (isNaN(legGold)) legGold = 400;
            var legDps = ids.legDps ? parseFloat($('#' + ids.legDps).val()) : NaN;
            if (isNaN(legDps)) legDps = 30;
            return {
                variantsPerItem: 10,
                allowNewAffixes: $('#' + ids.allowNew).is(':checked'),
                maxAffixCountChange: parseInt($('#' + ids.maxAffix).val()) || 0,
                existingBumpBias: parseFloat($('#' + ids.bump).val()),
                includeLegendaryBand: $('#' + ids.legendary).is(':checked'),
                goldValueScalePct: Math.max(0, gold),
                legendaryGoldBumpPct: Math.max(0, legGold),
                legendaryDpsBumpPct: Math.max(0, legDps),
                bands: collectBands()
            };
        };

        $(document).on('input change', '#' + ids.bandEditor + ' input, #' + ids.bandEditor + ' select', function () {
            syncFromInputs();
            validateBands();
        });
        $(document).on('click', '#' + ids.bandEditor + ' .rmBand', function () {
            var i = parseInt($(this).data('band'));
            syncFromInputs();
            state.bands.splice(i, 1);
            renderBands();
        });
        $('#' + ids.addBand).on('click', function () {
            syncFromInputs();
            var nb = { label: 'New Band', position: 'suffix', minBoostPct: Math.max(0, 10 + state.offset), maxBoostPct: Math.max(0, 20 + state.offset), slots: 1, dpsBumpPct: 8 };
            nb._baseMin = 10; nb._baseMax = 20;
            state.bands.push(nb);
            renderBands();
        });
        $('#' + ids.reset).on('click', function () {
            if (meta) state.seed(meta.defaultRuleset);
        });
        $('#' + ids.bump).on('input', function () {
            $('#' + ids.bumpVal).text(Number($(this).val()).toFixed(2));
        });
        $('#' + ids.offset).on('input', function () {
            applyOffset(parseFloat($(this).val()) || 0);
        });

        return state;
    }

    var genRuleset = RulesetEditor({
        bump: 'rsBumpBias', bumpVal: 'rsBumpBiasVal', allowNew: 'rsAllowNewAffixes',
        maxAffix: 'rsMaxAffixChange', legendary: 'rsLegendaryBand', gold: 'rsGoldScale', legGold: 'rsLegGold', legDps: 'rsLegDps', offset: 'rsOffset', offsetVal: 'rsOffsetVal',
        bandEditor: 'bandEditor', bandWarn: 'bandWarn', addBand: 'btnAddBand', reset: 'btnResetRuleset'
    });
    var profRuleset = RulesetEditor({
        bump: 'pRsBumpBias', bumpVal: 'pRsBumpBiasVal', allowNew: 'pRsAllowNewAffixes',
        maxAffix: 'pRsMaxAffixChange', legendary: 'pRsLegendaryBand', gold: 'pRsGoldScale', legGold: 'pRsLegGold', legDps: 'pRsLegDps', offset: 'pRsOffset', offsetVal: 'pRsOffsetVal',
        bandEditor: 'pBandEditor', bandWarn: 'pBandWarn', addBand: 'pBtnAddBand', reset: 'pBtnResetRuleset'
    });

    // ===================== INIT =====================
    $.getJSON('/CraftingLootifier/Meta', function (data) {
        meta = data;
        genRuleset.seed(data.defaultRuleset);
        profRuleset.seed(data.defaultRuleset);
        loadStatus();
    });

    // ===================== ITEM SEARCH / SELECT =====================
    function searchItem() {
        var q = $('#itemSearch').val().trim();
        if (!q) return;
        $.getJSON('/CraftingLootifier/SearchItem?q=' + encodeURIComponent(q), function (rows) {
            if (!rows.length) { $('#searchResults').html('<div class="text-muted" style="padding:8px;">No gear found.</div>').show(); return; }
            var h = '';
            rows.forEach(function (r) {
                h += '<div class="lf-search-item' + (r.lootified ? ' done' : '') + '" data-entry="' + r.entry + '">' +
                    '<img src="' + esc(r.iconPath || '/Icon/Get?name=inv_misc_questionmark') + '" />' +
                    '<span class="quality-' + r.quality + '">' + esc(r.name) + '</span>' +
                    '<span class="text-muted" style="margin-left:auto;">#' + r.entry + (r.lootified ? ' \u2713' : '') + '</span>' +
                    '</div>';
            });
            $('#searchResults').html(h).show();
        });
    }
    $('#btnSearchItem').on('click', searchItem);
    $('#itemSearch').on('keydown', function (e) { if (e.key === 'Enter') searchItem(); });

    $(document).on('click', '.lf-search-item', function () {
        var entry = parseInt($(this).data('entry'));
        if (selected[entry]) return;
        $.getJSON('/CraftingLootifier/ItemInfo?entry=' + entry, function (info) {
            if (!info.found) return;
            if (!info.eligible) { showToast(info.name + ' is not lootifiable gear.', 'error'); return; }
            selected[entry] = {
                entry: entry, name: info.name, quality: info.quality,
                lootified: !!info.lootified, variantCount: info.variantCount || 0,
                baseTypes: (info.stats || []).map(function (s) { return s.statType; })
            };
            renderSelected();
        });
    });

    function renderSelected() {
        var keys = Object.keys(selected);
        var anyLootified = false;
        var h = '';
        keys.forEach(function (k) {
            var it = selected[k];
            if (it.lootified) anyLootified = true;
            var badge = it.lootified
                ? '<span class="lf-relootify" title="Already lootified. Generating replaces its ' + it.variantCount +
                ' variants in kind — same tiers rolled fresh. Nothing is removed: any player-owned copy is rerolled into a new variant of the same tier.">' +
                '<i class="fa-solid fa-rotate"></i> re-lootify \u00d7' + it.variantCount + '</span>'
                : '';
            h += '<div class="lf-selected-item">' +
                '<span class="quality-' + it.quality + '">' + esc(it.name) + '</span>' +
                '<span class="text-muted">#' + it.entry + '</span>' +
                badge +
                '<span class="rm" data-entry="' + it.entry + '"><i class="fa-solid fa-xmark"></i></span>' +
                '</div>';
        });
        $('#selectedItems').html(h);
        $('#relootifyNote').toggle(anyLootified);
        $('#rulesetPanel').toggle(keys.length > 0);
        $('#btnGenerate').prop('disabled', keys.length === 0);
    }
    $(document).on('click', '.lf-selected-item .rm', function () {
        delete selected[parseInt($(this).data('entry'))];
        renderSelected();
    });

    function selectedEntries() { return Object.keys(selected).map(function (k) { return parseInt(k); }); }

    // ===================== PREVIEW / GENERATE (single) =====================
    $('#btnPreview').on('click', function () {
        var entries = selectedEntries();
        if (!entries.length) return;
        $('#previewInfo').text('Rolling...');
        $.ajax({
            url: '/CraftingLootifier/Preview', method: 'POST', contentType: 'application/json',
            data: JSON.stringify({ itemEntries: entries, ruleset: genRuleset.collect() }),
            success: function (res) {
                if (!res.success) { showToast(res.error || 'Preview failed', 'error'); return; }
                renderPreview(res);
            },
            error: function () { showToast('Preview request failed', 'error'); }
        });
    });

    function renderPreview(res) {
        var h = '';
        res.items.forEach(function (item) {
            var baseTypes = (selected[item.entry] || {}).baseTypes || [];
            var w = item.weapon;
            h += '<div class="lf-item-block">' +
                '<div class="lf-item-title quality-' + item.quality + '">' + esc(item.name) + '</div>' +
                dpsReferenceLine(w, item.quality);
            item.variants.forEach(function (v) { h += renderVariantRow(v, baseTypes); });
            h += '</div>';
        });
        $('#previewContainer').html(h);
        $('#previewInfo').text(res.items.length + ' item(s) · 20% of crafts return the base');
    }

    // Map an item quality to the vanilla DPS multiplier key.
    function dpsQualityMult(q) {
        var m = (meta && meta.dpsReference && meta.dpsReference.qualityMult) || { green: 1, blue: 1.105, purple: 1.215, legendary: 1.3 };
        if (q >= 5) return m.legendary;
        if (q === 4) return m.purple;
        if (q === 3) return m.blue;
        return m.green; // green / white baseline
    }

    // "relative to that tier of that level": base DPS + what blue/purple/legendary
    // SHOULD be at this weapon's level (base DPS scaled by the vanilla quality ratio
    // off the base's own quality). Non-weapons render nothing.
    function dpsReferenceLine(w, baseQuality) {
        if (!w || !w.isWeapon) return '';
        var base = w.baseDps, bm = dpsQualityMult(baseQuality);
        function tgt(q) { return (base * dpsQualityMult(q) / bm).toFixed(1); }
        var speed = (w.delay / 1000).toFixed(2);
        return '<div class="lf-dps-ref">\u2694 base <b>' + base.toFixed(1) + '</b> DPS \u00b7 ' + speed + 's' +
            ' <span class="text-muted">\u2014 vanilla line: blue ' + tgt(3) + ' / purple ' + tgt(4) + ' / leg ' + tgt(5) + '</span></div>';
    }

    function tierQuality(tier) {
        var s = (tier || '').toLowerCase();
        if (s.indexOf('gods') >= 0 || s.indexOf('legend') >= 0) return 5;
        if (s.indexOf('glory') >= 0) return 4;
        return 3;
    }

    function renderVariantRow(v, baseTypes) {
        var baseSet = {};
        (baseTypes || []).forEach(function (t) { baseSet[t] = true; });
        var pills = '';
        (v.stats || []).forEach(function (s) {
            var isNew = !baseSet[s.statType];
            pills += '<span class="lf-stat-pill' + (isNew ? ' new' : '') + '">+' + s.statValue + ' ' + esc(s.name) + '</span>';
        });
        var q = (v.quality && v.quality >= 2) ? v.quality : tierQuality(v.tier);
        var dpsChip = (v.dps != null)
            ? '<span class="lf-dps" title="Resulting weapon DPS (damage-only bump; speed unchanged)">' + Number(v.dps).toFixed(1) + ' DPS' +
            (v.dpsBumpPct ? ' <span class="text-muted">+' + Number(v.dpsBumpPct).toFixed(1) + '%</span>' : '') + '</span>'
            : '';
        return '<div class="lf-variant-row">' +
            (v.iconPath ? '<img src="' + esc(v.iconPath) + '" />' : '') +
            '<span class="lf-variant-name quality-' + q + '">' + esc(v.name) + '</span>' +
            '<span class="lf-boost">' + Number(v.boostPct).toFixed(0) + '%</span>' +
            '<span class="lf-award">' + Number(v.awardPct).toFixed(1) + '%</span>' +
            dpsChip +
            '<span style="flex:1;">' + pills + '</span>' +
            '</div>';
    }

    $('#btnGenerate').on('click', function () {
        var entries = selectedEntries();
        if (!entries.length) return;
        $('#btnGenerate').prop('disabled', true).html('<i class="fa-solid fa-spinner fa-spin"></i> Generating...');
        $.ajax({
            url: '/CraftingLootifier/Commit', method: 'POST', contentType: 'application/json',
            data: JSON.stringify({ itemEntries: entries, ruleset: genRuleset.collect() }),
            success: function (res) {
                if (!res.success) { showToast(res.error || 'Generate failed', 'error'); }
                else {
                    showToast(res.itemsCreated + ' variants across ' + res.basesProcessed + ' items' +
                        (res.itemsRemapped ? ' (' + res.itemsRemapped + ' owned items rerolled)' : '') + '. ' + res.reloadHint, 'success');
                    loadStatus();
                }
            },
            error: function () { showToast('Generate request failed', 'error'); },
            complete: function () { $('#btnGenerate').prop('disabled', false).html('<i class="fa-solid fa-bolt"></i> Generate'); }
        });
    });

    // ===================== PROFESSIONS =====================
    function loadProfessions() {
        $.getJSON('/CraftingLootifier/Professions', function (res) {
            var health = res.dbcCreateItems > 100
                ? '<span class="text-muted">' + res.dbcCreateItems + ' craftable spells indexed from Spell.dbc</span>'
                : '<span style="color:var(--status-warning);">DBC create-item index looks empty (' + res.dbcCreateItems + ') — the Spell.dbc effect offset may be wrong.</span>';
            var h = '<div class="lf-prof-health">' + health + '</div><div class="lf-prof-grid">';
            res.professions.forEach(function (p) {
                h += '<div class="lf-prof-card' + (selectedProf === p.id ? ' active' : '') + '" data-prof="' + p.id + '">' +
                    '<div class="lf-prof-name">' + esc(p.name) + '</div>' +
                    '<div class="text-muted" style="font-size:11px;">' + p.equippableOutputs + ' equippable · ' + p.lootified + ' lootified</div>' +
                    '</div>';
            });
            h += '</div>';
            $('#profList').html(h);
        });
    }

    $(document).on('click', '.lf-prof-card', function () {
        selectedProf = parseInt($(this).data('prof'));
        $('.lf-prof-card').removeClass('active');
        $(this).addClass('active');
        $('#profBatchPanel').show();
        loadRecipes(selectedProf);
    });

    function loadRecipes(id) {
        $('#recipeList').html('<div class="text-muted" style="padding:14px;">Loading recipes...</div>');
        $.getJSON('/CraftingLootifier/ProfessionRecipes?skillLineId=' + id, function (res) {
            var equipCount = res.recipes.filter(function (r) { return r.equippable; }).length;
            $('#profBatchInfo').text(res.name + ' — ' + equipCount + ' equippable of ' + res.recipes.length + ' recipes');
            $('#btnProfBatch').html('<i class="fa-solid fa-bolt"></i> Lootify all ' + equipCount + ' equippable');
            var h = '';
            res.recipes.forEach(function (r) {
                h += '<div class="lf-recipe-row' + (r.equippable ? '' : ' skip') + '">' +
                    '<img src="' + esc(r.iconPath || '/Icon/Get?name=inv_misc_questionmark') + '" />' +
                    '<span class="quality-' + r.quality + '">' + esc(r.name) + '</span>' +
                    '<span class="text-muted">#' + r.entry + ' \u00b7 ilvl ' + r.itemLevel + '</span>' +
                    (r.equippable ? '' : '<span class="lf-skip-tag">not equippable</span>') +
                    (r.lootified ? '<span class="lf-done-tag">\u2713 lootified</span>' : '') +
                    '</div>';
            });
            $('#recipeList').html(h || '<div class="text-muted" style="padding:14px;">No recipes resolved.</div>');
        });
    }

    $('#btnProfBatch').on('click', function () {
        if (!selectedProf) return;
        var btn = $(this);
        btn.prop('disabled', true).html('<i class="fa-solid fa-spinner fa-spin"></i> Generating whole profession — this can take a bit...');
        $.ajax({
            url: '/CraftingLootifier/ProfessionBatchCommit', method: 'POST', contentType: 'application/json',
            data: JSON.stringify({ skillLineId: selectedProf, ruleset: profRuleset.collect() }),
            success: function (res) {
                if (!res.success) { showToast(res.error || 'Batch failed', 'error'); }
                else {
                    showToast(res.profession + ': ' + res.itemsCreated + ' variants across ' + res.basesProcessed + ' items' +
                        (res.itemsRemapped ? ' (' + res.itemsRemapped + ' owned items rerolled)' : '') + '. ' + res.reloadHint, 'success');
                    loadStatus();
                    loadProfessions();
                }
            },
            error: function () { showToast('Batch request failed (it may still be running server-side for a large profession)', 'error'); },
            complete: function () { btn.prop('disabled', false); if (selectedProf) loadRecipes(selectedProf); }
        });
    });

    // ===================== STATUS =====================
    function loadStatus() {
        $.getJSON('/CraftingLootifier/Status', function (s) {
            var extra = s.orphans > 0 ? ' · ' + s.orphans + ' orphans' : '';
            $('#coverageBar').text(s.totalVariants + ' variants across ' + s.baseItems + ' lootified items' + extra);
        });
    }
    $('#btnViewStatus').on('click', function () {
        $.getJSON('/CraftingLootifier/Status', function (s) {
            $('#statusBody').html(
                '<div style="font-size:13px;line-height:1.8;">' +
                '<div><strong>' + s.totalVariants + '</strong> generated variants</div>' +
                '<div><strong>' + s.baseItems + '</strong> base items lootified</div>' +
                (s.orphans > 0
                    ? '<div style="color:var(--status-warning);"><strong>' + s.orphans + '</strong> orphan rows (crashed runs) <button class="btn-micro" id="btnSweep">Sweep</button></div>'
                    : '<div class="text-muted">No orphans</div>') +
                '</div>');
            openModal('#statusModal');
        });
    });
    $(document).on('click', '#btnSweep', function () {
        $.ajax({
            url: '/CraftingLootifier/SweepOrphans', method: 'POST',
            success: function (r) { showToast('Swept ' + r.orphans + ' orphans', 'success'); closeModal('#statusModal'); loadStatus(); },
            error: function () { showToast('Sweep failed', 'error'); }
        });
    });

    // ===================== BROWSE =====================
    function loadBrowse() {
        var q = $('#browseSearch').val().trim();
        $.getJSON('/CraftingLootifier/Browse?q=' + encodeURIComponent(q) + '&page=' + browsePage, function (res) {
            var h = '';
            if (!res.items.length) h = '<div class="text-muted" style="padding:14px;">Nothing lootified yet.</div>';
            res.items.forEach(function (it) {
                h += '<div class="lf-item-block" data-base="' + it.baseEntry + '">' +
                    '<div class="d-flex align-items-center gap-1" style="cursor:pointer;" data-toggle-base="' + it.baseEntry + '">' +
                    '<img src="' + esc(it.iconPath || '/Icon/Get?name=inv_misc_questionmark') + '" style="width:22px;height:22px;border-radius:3px;" />' +
                    '<span class="quality-' + it.quality + '">' + esc(it.name) + '</span>' +
                    '<span class="text-muted">#' + it.baseEntry + ' · ' + it.variantCount + ' variants</span>' +
                    '<span class="rm" data-rollback-base="' + it.baseEntry + '" style="margin-left:auto;cursor:pointer;color:var(--status-warning);"><i class="fa-solid fa-trash-can"></i></span>' +
                    '</div>' +
                    '<div class="lf-variants-expand" data-expand="' + it.baseEntry + '" style="display:none;margin-top:8px;"></div>' +
                    '</div>';
            });
            $('#browseContainer').html(h);
            var pages = Math.ceil(res.total / res.pageSize);
            if (pages > 1) {
                $('#browsePager').show().html(
                    '<button class="btn-micro" id="prevPage"' + (browsePage <= 1 ? ' disabled' : '') + '>Prev</button> ' +
                    '<span class="text-muted">Page ' + browsePage + ' / ' + pages + '</span> ' +
                    '<button class="btn-micro" id="nextPage"' + (browsePage >= pages ? ' disabled' : '') + '>Next</button>');
            } else $('#browsePager').hide();
        });
    }

    $(document).on('click', '[data-toggle-base]', function (e) {
        if ($(e.target).closest('[data-rollback-base]').length) return;
        var base = parseInt($(this).data('toggle-base'));
        var box = $('.lf-variants-expand[data-expand="' + base + '"]');
        if (box.is(':visible')) { box.hide(); return; }
        $.getJSON('/CraftingLootifier/ItemVariants?baseEntry=' + base, function (res) {
            var h = '<div class="text-muted" style="margin-bottom:4px;">Base quality ' + res.baseQuality + ' · ' + res.basePct + '% base passthrough</div>';
            res.variants.forEach(function (v) { h += renderVariantRow(v, []); });
            box.html(h).show();
        });
    });
    $(document).on('click', '[data-rollback-base]', function () {
        askRollback(parseInt($(this).data('rollback-base')));
    });
    $(document).on('click', '#prevPage', function () { if (browsePage > 1) { browsePage--; loadBrowse(); } });
    $(document).on('click', '#nextPage', function () { browsePage++; loadBrowse(); });
    $('#browseSearch').on('input', function () { browsePage = 1; loadBrowse(); });

    // ===================== ROLLBACK =====================
    function askRollback(baseEntry) {
        rollbackTarget = baseEntry;
        $('#rollbackDesc').text(baseEntry
            ? 'Remove all crafting variants for item #' + baseEntry + '? This deletes the generated item_template rows.'
            : 'Remove ALL crafting variants for every lootified item, and sweep orphan rows? This cannot be undone.');
        openModal('#rollbackModal');
    }
    $('#btnRollbackAll').on('click', function () { askRollback(0); });
    $('#btnConfirmRollback').on('click', function () {
        $.ajax({
            url: '/CraftingLootifier/Rollback', method: 'POST', contentType: 'application/json',
            data: JSON.stringify({ baseEntry: rollbackTarget }),
            success: function (res) {
                closeModal('#rollbackModal');
                var msg = 'Removed ' + res.removed + ' variants';
                if (res.orphans) msg += ' + ' + res.orphans + ' orphans';
                showToast(msg + '. ' + res.reloadHint, 'success');
                loadStatus();
                if ($('#tab-browse').is(':visible')) loadBrowse();
                if ($('#tab-professions').is(':visible')) { loadProfessions(); if (selectedProf) loadRecipes(selectedProf); }
            },
            error: function () { showToast('Rollback failed', 'error'); }
        });
    });

    var RV_LABEL = 'Crafting Lootifier';
    var RV_BTN_ID = 'btnRevalueAll';
    var RV_ANCHOR_ID = 'btnRollbackAll';
    var RV_URL_TIERS = '/CraftingLootifier/RevalueTiers';
    var RV_URL_APPLY = '/CraftingLootifier/Revalue';

    // ═══════════════════ REVALUE GOLD (per-tier, you set the numbers) ═══════════════════
    // Opens a dialog listing the tiers that ACTUALLY EXIST in the tracking table,
    // shows what each one is currently priced at (measured from the DB), and lets
    // you type the bump you want. Blank = that tier is left alone. Prices are
    // rebuilt from each base item (never compounds), and only buy/sell change —
    // entries, names, display IDs and retextures are untouched.

    var __rvTiers = [];

    function rvFmtMoney(c) {
        c = Math.max(0, Math.round(c || 0));
        var g = Math.floor(c / 10000), s = Math.floor((c % 10000) / 100), cc = c % 100;
        var out = [];
        if (g) out.push(g + 'g');
        if (s || g) out.push(s + 's');
        out.push(cc + 'c');
        return out.join(' ');
    }

    function rvEnsureCss() {
        if (document.getElementById('rvCss')) return;
        $('<style id="rvCss">' +
            '#rvOverlay{position:fixed;inset:0;z-index:99999;background:rgba(0,0,0,.6);display:flex;align-items:center;justify-content:center;}' +
            '#rvBox{background:#1e2128;color:#e8e8ea;border:1px solid rgba(255,255,255,.14);border-radius:10px;' +
            'width:min(760px,94vw);max-height:88vh;display:flex;flex-direction:column;box-shadow:0 18px 50px rgba(0,0,0,.55);font-size:13px;}' +
            '#rvBox h3{margin:0;padding:14px 18px;font-size:15px;border-bottom:1px solid rgba(255,255,255,.1);}' +
            '#rvBody{padding:12px 18px;overflow:auto;}' +
            '#rvBody .rv-note{opacity:.7;font-size:12px;margin-bottom:12px;line-height:1.5;}' +
            '#rvTable{width:100%;border-collapse:collapse;}' +
            '#rvTable th{text-align:left;font-weight:600;opacity:.65;font-size:11px;text-transform:uppercase;' +
            'letter-spacing:.4px;padding:6px 8px;border-bottom:1px solid rgba(255,255,255,.1);}' +
            '#rvTable td{padding:7px 8px;border-bottom:1px solid rgba(255,255,255,.05);vertical-align:middle;}' +
            '#rvTable input{width:82px;padding:5px 7px;border-radius:6px;border:1px solid rgba(255,255,255,.2);' +
            'background:rgba(0,0,0,.3);color:#fff;font-size:13px;}' +
            '#rvTable .rv-tier{font-weight:600;}' +
            '#rvTable .rv-now{opacity:.75;font-variant-numeric:tabular-nums;}' +
            '#rvTable .rv-prev{font-size:12px;opacity:.85;font-variant-numeric:tabular-nums;}' +
            '#rvTable .rv-prev .rv-arrow{opacity:.45;margin:0 5px;}' +
            '#rvTable .rv-prev .rv-new{color:#ffd873;font-weight:600;}' +
            '#rvTable .rv-skip{opacity:.4;font-style:italic;font-size:12px;}' +
            '#rvFoot{padding:12px 18px;border-top:1px solid rgba(255,255,255,.1);display:flex;' +
            'justify-content:space-between;align-items:center;gap:10px;}' +
            '#rvFoot .rv-sum{font-size:12px;opacity:.7;}' +
            '#rvFoot button{padding:7px 16px;border-radius:7px;border:1px solid rgba(255,255,255,.18);' +
            'background:rgba(255,255,255,.06);color:#e8e8ea;cursor:pointer;font-size:13px;}' +
            '#rvFoot #rvApply{background:#c8922e;border-color:#c8922e;color:#1a1a1a;font-weight:600;}' +
            '#rvFoot #rvApply:disabled{opacity:.4;cursor:not-allowed;}' +
            '</style>').appendTo('head');
    }

    function rvRowHtml(t, i) {
        var now = (t.currentMult != null)
            ? '&times;' + t.currentMult.toFixed(2) + ' <span style="opacity:.55">(+' + Math.round((t.currentMult - 1) * 100) + '%)</span>'
            : '<span style="opacity:.45">n/a</span>';
        return '<tr data-i="' + i + '">' +
            '<td class="rv-tier">' + esc(t.tier || '(no tier)') + '</td>' +
            '<td style="opacity:.7">' + t.count + '</td>' +
            '<td class="rv-now">' + now + '</td>' +
            '<td><input class="rv-in" type="number" min="0" max="100000" step="5" placeholder="leave" /></td>' +
            '<td class="rv-prev"><span class="rv-skip">unchanged</span></td>' +
            '</tr>';
    }

    function rvUpdateRow($tr) {
        var i = parseInt($tr.attr('data-i'));
        var t = __rvTiers[i];
        var raw = $tr.find('.rv-in').val();
        var $p = $tr.find('.rv-prev');

        if (raw === '' || raw == null || isNaN(parseFloat(raw))) {
            $p.html('<span class="rv-skip">unchanged</span>');
            return;
        }
        var pct = Math.max(0, parseFloat(raw));
        if (!t.sampleBaseSell) {
            $p.html('<span class="rv-skip">no priced sample</span>');
            return;
        }
        var nu = Math.round(t.sampleBaseSell * (1 + pct / 100));
        $p.html('<span title="' + esc(t.sampleName) + '">' + rvFmtMoney(t.sampleCurrentSell) + '</span>' +
            '<span class="rv-arrow">&rarr;</span>' +
            '<span class="rv-new">' + rvFmtMoney(nu) + '</span>');
    }

    function rvRefresh() {
        var n = 0, v = 0;
        $('#rvTable tbody tr').each(function () {
            var raw = $(this).find('.rv-in').val();
            if (raw !== '' && !isNaN(parseFloat(raw))) { n++; v += __rvTiers[parseInt($(this).attr('data-i'))].count; }
        });
        $('#rvApply').prop('disabled', n === 0);
        $('#rvSum').text(n === 0
            ? 'Enter a value on at least one tier.'
            : n + ' tier' + (n === 1 ? '' : 's') + ' \u2192 ' + v + ' variant' + (v === 1 ? '' : 's') + ' will be repriced.');
    }

    function rvOpen(tiers) {
        __rvTiers = tiers || [];
        rvEnsureCss();
        $('#rvOverlay').remove();

        if (__rvTiers.length === 0) {
            showToast('No lootified variants tracked yet — nothing to revalue.', 'info');
            return;
        }

        var rows = __rvTiers.map(rvRowHtml).join('');
        $('<div id="rvOverlay"><div id="rvBox">' +
            '<h3><i class="fa-solid fa-coins"></i> Revalue gold &mdash; ' + RV_LABEL + '</h3>' +
            '<div id="rvBody">' +
            '<div class="rv-note">Tiers below are read from your tracking table. Type the price bump you want ' +
            'above the <b>base item</b> \u2014 e.g. <b>150</b> makes a variant cost 2.5&times; its base. ' +
            'Leave a tier blank and it is <b>not touched</b>. Prices are rebuilt from the base item every time, ' +
            'so re-running never compounds. Items, names and retextures are untouched.</div>' +
            '<table id="rvTable"><thead><tr>' +
            '<th>Tier</th><th>Variants</th><th>Now</th><th>New gold +%</th><th>Preview (sample item)</th>' +
            '</tr></thead><tbody>' + rows + '</tbody></table>' +
            '</div>' +
            '<div id="rvFoot"><span class="rv-sum" id="rvSum"></span><span>' +
            '<button id="rvCancel">Cancel</button> ' +
            '<button id="rvApply" disabled>Apply</button>' +
            '</span></div>' +
            '</div></div>').appendTo('body');

        rvRefresh();
        $('#rvTable .rv-in').first().trigger('focus');
    }

    $(document).on('input change', '#rvTable .rv-in', function () {
        rvUpdateRow($(this).closest('tr'));
        rvRefresh();
    });
    $(document).on('click', '#rvCancel', function () { $('#rvOverlay').remove(); });
    $(document).on('click', '#rvOverlay', function (e) { if (e.target.id === 'rvOverlay') $('#rvOverlay').remove(); });
    $(document).on('keydown', function (e) { if (e.key === 'Escape') $('#rvOverlay').remove(); });

    $(document).on('click', '#rvApply', function () {
        var payload = [];
        $('#rvTable tbody tr').each(function () {
            var raw = $(this).find('.rv-in').val();
            if (raw === '' || isNaN(parseFloat(raw))) return;
            payload.push({ tier: __rvTiers[parseInt($(this).attr('data-i'))].tier, goldBumpPct: Math.max(0, parseFloat(raw)) });
        });
        if (payload.length === 0) return;

        var $b = $(this).prop('disabled', true).text('Applying...');
        $.ajax({
            url: RV_URL_APPLY, method: 'POST', contentType: 'application/json',
            data: JSON.stringify({ tiers: payload }),
            success: function (r) {
                if (r.success) {
                    $('#rvOverlay').remove();
                    showToast(r.updated + ' variants repriced.', 'success');
                    if (r.reloadHint) showToast(r.reloadHint, 'info');
                } else {
                    $b.prop('disabled', false).text('Apply');
                    showToast(r.error || 'Revalue failed', 'error');
                }
            },
            error: function () {
                $b.prop('disabled', false).text('Apply');
                showToast('Revalue failed', 'error');
            }
        });
    });

    function rvInitButton() {
        if ($('#' + RV_BTN_ID).length) return;
        var $anchor = $('#' + RV_ANCHOR_ID);
        if (!$anchor.length) return;
        $('<button id="' + RV_BTN_ID + '" title="Set gold prices per tier for the variants that already exist. In place \u2014 items, names and retextures are untouched.">' +
            '<i class="fa-solid fa-coins"></i> Revalue Gold</button>')
            .attr('class', $anchor.attr('class') || '')
            .css('margin-left', '6px')
            .insertAfter($anchor);
    }
    $(rvInitButton);

    $(document).on('click', '#' + RV_BTN_ID, function () {
        var $btn = $(this).prop('disabled', true);
        $.getJSON(RV_URL_TIERS, function (r) {
            $btn.prop('disabled', false);
            if (r && r.success) rvOpen(r.tiers);
            else showToast((r && r.error) || 'Could not load tiers', 'error');
        }).fail(function () {
            $btn.prop('disabled', false);
            showToast('Could not load tiers', 'error');
        });
    });

    // ===================== TABS / MODALS =====================
    $('.lf-tab').on('click', function () {
        $('.lf-tab').removeClass('active');
        $(this).addClass('active');
        var tab = $(this).data('tab');
        $('#tab-generate').toggle(tab === 'generate');
        $('#tab-browse').toggle(tab === 'browse');
        $('#tab-professions').toggle(tab === 'professions');
        if (tab === 'browse') { browsePage = 1; loadBrowse(); }
        if (tab === 'professions') loadProfessions();
    });

    function openModal(sel) {
        if (window.bootstrap && bootstrap.Modal) { bootstrap.Modal.getOrCreateInstance($(sel)[0]).show(); }
        else { $(sel).addClass('show').css({ display: 'block' }); }
    }
    function closeModal(sel) {
        if (window.bootstrap && bootstrap.Modal) { var m = bootstrap.Modal.getInstance($(sel)[0]); if (m) m.hide(); }
        else { $(sel).removeClass('show').css({ display: 'none' }); }
    }
    $(document).on('click', '[data-bs-dismiss="modal"], #btnCancelRollback', function () {
        closeModal('#rollbackModal'); closeModal('#statusModal');
    });
})();