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
                    '<span class="rmBand" data-band="' + i + '"><i class="fa-solid fa-xmark"></i></span>' +
                    '</div>';
            });
            $('#' + ids.bandEditor).html(h);
            validateBands();
        }

        function collectBands() {
            var out = [];
            $('#' + ids.bandEditor + ' .lf-band-row').each(function () {
                out.push({
                    label: $(this).find('.b-label').val() || '',
                    position: $(this).find('.b-pos').val() || 'suffix',
                    minBoostPct: parseFloat($(this).find('.b-min').val()) || 0,
                    maxBoostPct: parseFloat($(this).find('.b-max').val()) || 0,
                    slots: parseInt($(this).find('.b-slots').val()) || 0
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
            $('#' + ids.offset).val(0);
            $('#' + ids.offsetVal).text('+0');
            renderBands();
        };

        state.collect = function () {
            return {
                variantsPerItem: 10,
                allowNewAffixes: $('#' + ids.allowNew).is(':checked'),
                maxAffixCountChange: parseInt($('#' + ids.maxAffix).val()) || 0,
                existingBumpBias: parseFloat($('#' + ids.bump).val()),
                includeLegendaryBand: $('#' + ids.legendary).is(':checked'),
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
            var nb = { label: 'New Band', position: 'suffix', minBoostPct: Math.max(0, 10 + state.offset), maxBoostPct: Math.max(0, 20 + state.offset), slots: 1 };
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
        maxAffix: 'rsMaxAffixChange', legendary: 'rsLegendaryBand', offset: 'rsOffset', offsetVal: 'rsOffsetVal',
        bandEditor: 'bandEditor', bandWarn: 'bandWarn', addBand: 'btnAddBand', reset: 'btnResetRuleset'
    });
    var profRuleset = RulesetEditor({
        bump: 'pRsBumpBias', bumpVal: 'pRsBumpBiasVal', allowNew: 'pRsAllowNewAffixes',
        maxAffix: 'pRsMaxAffixChange', legendary: 'pRsLegendaryBand', offset: 'pRsOffset', offsetVal: 'pRsOffsetVal',
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
                    '<img src="' + esc(r.iconPath || '/icons/inv_misc_questionmark.png') + '" />' +
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
                baseTypes: (info.stats || []).map(function (s) { return s.statType; })
            };
            renderSelected();
        });
    });

    function renderSelected() {
        var keys = Object.keys(selected);
        var h = '';
        keys.forEach(function (k) {
            var it = selected[k];
            h += '<div class="lf-selected-item">' +
                '<span class="quality-' + it.quality + '">' + esc(it.name) + '</span>' +
                '<span class="text-muted">#' + it.entry + '</span>' +
                '<span class="rm" data-entry="' + it.entry + '"><i class="fa-solid fa-xmark"></i></span>' +
                '</div>';
        });
        $('#selectedItems').html(h);
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
            h += '<div class="lf-item-block">' +
                '<div class="lf-item-title quality-' + item.quality + '">' + esc(item.name) + '</div>';
            item.variants.forEach(function (v) { h += renderVariantRow(v, baseTypes); });
            h += '</div>';
        });
        $('#previewContainer').html(h);
        $('#previewInfo').text(res.items.length + ' item(s) · 20% of crafts return the base');
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
        var q = tierQuality(v.tier);
        return '<div class="lf-variant-row">' +
            (v.iconPath ? '<img src="' + esc(v.iconPath) + '" />' : '') +
            '<span class="lf-variant-name quality-' + q + '">' + esc(v.name) + '</span>' +
            '<span class="lf-boost">' + Number(v.boostPct).toFixed(0) + '%</span>' +
            '<span class="lf-award">' + Number(v.awardPct).toFixed(1) + '%</span>' +
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
                    showToast(res.itemsCreated + ' variants across ' + res.basesProcessed + ' items. ' + res.reloadHint, 'success');
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
                    '<img src="' + esc(r.iconPath || '/icons/inv_misc_questionmark.png') + '" />' +
                    '<span class="quality-' + r.quality + '">' + esc(r.name) + '</span>' +
                    '<span class="text-muted">#' + r.entry + '</span>' +
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
                    showToast(res.profession + ': ' + res.itemsCreated + ' variants across ' + res.basesProcessed + ' items. ' + res.reloadHint, 'success');
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
                    '<img src="' + esc(it.iconPath || '/icons/inv_misc_questionmark.png') + '" style="width:22px;height:22px;border-radius:3px;" />' +
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