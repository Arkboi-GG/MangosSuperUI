// Quest Lootifier — generate stat-reroll variants for quest reward items.
// Two modes: single-quest (search → pick rewards → preview → commit) and
// all-quests (bulk generate for every eligible reward item in the game).
(function () {
    'use strict';

    var mode = 'single';
    var selectedQuest = null;     // { entry, title }
    var rewardItems = [];         // resolved reward item objects
    var selectedItems = {};       // itemEntry -> true
    var iconMap = {};
    var browseState = { page: 1, pageSize: 40, q: '', total: 0 };

    function esc(s) {
        return String(s == null ? '' : s)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    // Toast notification. Self-contained here — the ARPG Lootifier defines its
    // own showToast privately inside lootifier.js, which isn't in scope on this
    // page, so the Quest Lootifier needs its own. Reuses the shared .lf-toast
    // styles if present; otherwise the inline fallback keeps it visible.
    function showToast(msg, type) {
        var el = $('<div class="lf-toast ' + (type || 'info') + '">' + esc(msg) + '</div>');
        // Inline fallback styling so it works even without the .lf-toast CSS.
        var colors = { success: '#2ea043', error: '#c0392b', info: '#3b82c4' };
        el.css({
            position: 'fixed', right: '20px', bottom: '20px', zIndex: 9999,
            background: colors[type] || colors.info, color: '#fff',
            padding: '10px 16px', borderRadius: '8px', fontSize: '13px',
            boxShadow: '0 4px 14px rgba(0,0,0,0.25)', maxWidth: '360px',
            marginTop: '8px'
        });
        $('body').append(el);
        setTimeout(function () { el.fadeOut(300, function () { el.remove(); }); }, 4000);
    }

    // Quality color class (matches the app-wide quality-0..5 styles used by the
    // OG Lootifier). 0 poor/grey, 1 white, 2 green, 3 blue, 4 epic, 5 legendary.
    function qualClass(q) { return 'quality-' + (q == null ? 1 : q); }

    // Fallback preview quality, only used if the server omits `quality` on a
    // preview variant (it normally sends it via VariantQualityForTier). The band
    // colour ladder is name-driven server-side, so trust v.quality when present.
    function variantQuality(baseQuality, budgetPct) {
        var bq = baseQuality == null ? 1 : baseQuality;
        if (budgetPct >= 90 && bq < 4) return 4;
        return bq;
    }

    // Ruleset is now BAND-BASED (like the Crafting Lootifier): the admin edits a
    // list of bands (tier name / position / min-max boost % / slot count) and the
    // server rolls `slots` additive variants per band. The legacy single
    // budget-ceiling + variant-count inputs are retired.
    // Current master Gold scale % (survives band-editor re-renders; 100 = as entered).
    function goldScaleValue() {
        var v = parseFloat($('#qlGoldScale').val());
        if (isNaN(v)) v = 100;
        return Math.max(0, v);
    }

    // Current Legendary gold +% (survives re-renders; 500 = old x6 behavior).
    function legGoldValue() {
        var v = parseFloat($('#qlLegGold').val());
        if (isNaN(v)) v = 500;
        return Math.max(0, v);
    }

    function collectRuleset() {
        var bias = parseFloat($('#qlBumpBias').val());
        if (isNaN(bias)) bias = 0.5;
        return {
            allowNewAffixes: $('#qlAllowNew').is(':checked'),
            maxAffixCountChange: parseInt($('#qlMaxAffix').val()) || 1,
            existingBumpBias: Math.min(1, Math.max(0, bias)),
            generateLegendary: $('#qlLegendary').is(':checked'),
            includeGodsBand: true,
            goldValueScalePct: goldScaleValue(),
            legendaryGoldBumpPct: legGoldValue(),
            bands: readBands()
        };
    }

    // ── Band editor (tiers are band-chosen, mirroring the Crafting Lootifier) ──
    // Defaults match the server's DefaultBands so preview/commit agree out of the box.
    var DEFAULT_BANDS = [
        { label: 'Improved', position: 'prefix', minBoostPct: 10, maxBoostPct: 20, slots: 5, goldBumpPct: 25 },
        { label: 'of Power', position: 'suffix', minBoostPct: 20, maxBoostPct: 30, slots: 2, goldBumpPct: 50 },
        { label: 'of Glory', position: 'suffix', minBoostPct: 30, maxBoostPct: 40, slots: 2, goldBumpPct: 100 },
        { label: 'of the Gods', position: 'suffix', minBoostPct: 40, maxBoostPct: 60, slots: 1, goldBumpPct: 200 }
    ];

    function bandRowHtml(b) {
        return '<div class="ql-band-row">' +
            '<input class="ql-b-label" type="text" value="' + esc(b.label) + '" placeholder="Tier name" />' +
            '<select class="ql-b-pos">' +
            '<option value="prefix"' + (b.position === 'prefix' ? ' selected' : '') + '>prefix</option>' +
            '<option value="suffix"' + (b.position !== 'prefix' ? ' selected' : '') + '>suffix</option>' +
            '</select>' +
            '<input class="ql-b-min" type="number" min="0" max="200" step="1" value="' + b.minBoostPct + '" title="Minimum boost %" />' +
            '<input class="ql-b-max" type="number" min="0" max="200" step="1" value="' + b.maxBoostPct + '" title="Maximum boost %" />' +
            '<input class="ql-b-slots" type="number" min="0" max="50" step="1" value="' + b.slots + '" title="Variants rolled in this band" />' +
            '<input class="ql-b-gold" type="number" min="0" max="10000" step="5" value="' + (b.goldBumpPct != null ? b.goldBumpPct : '') + '" placeholder="curve" title="Gold price bump above base (%) for this tier. Blank = legacy budget-derived curve." />' +
            '<button class="ql-b-del" title="Remove band"><i class="fa-solid fa-xmark"></i></button>' +
            '</div>';
    }

    function renderBandEditor(bands) {
        var rows = (bands || DEFAULT_BANDS).map(bandRowHtml).join('');
        var html =
            '<div class="ql-band-title">Tiers (bands)</div>' +
            '<div class="ql-band-head"><span>Tier name</span><span>Pos</span><span>Min %</span><span>Max %</span><span>Slots</span><span title="Gold price bump above base (%). Blank = legacy curve.">Gold +%</span><span></span></div>' +
            '<div id="qlBandRows">' + rows + '</div>' +
            '<div class="ql-band-actions">' +
            '<button id="qlAddBand" class="ql-band-btn"><i class="fa-solid fa-plus"></i> Add band</button>' +
            '<button id="qlResetBands" class="ql-band-btn"><i class="fa-solid fa-rotate-left"></i> Defaults</button>' +
            '<label class="ql-bump" title="Split of the additive bonus: 0 = all into new affixes, 1 = all into existing stats">' +
            'Bump bias <input id="qlBumpBias" type="number" min="0" max="1" step="0.1" value="0.5" /></label>' +
            '<label class="ql-bump" title="Gold price bump above base (%) for the quest legendary. 500 = the old \u00d76 behavior.">' +
            'Legendary gold +% <input id="qlLegGold" type="number" min="0" max="10000" step="25" value="' + legGoldValue() + '" /></label>' +
            '<label class="ql-bump" title="Master scale on ALL gold bumps: 100% = as entered, 0% = prices unchanged, 200% = double every bump.">' +
            'Gold scale % <input id="qlGoldScale" type="number" min="0" max="1000" step="5" value="' + goldScaleValue() + '" /></label>' +
            '<span id="qlBandTotal" class="text-muted"></span>' +
            '</div>';
        $('#qlBandEditor').html(html);
        ensureBandGoldCss();
        updateBandTotal();
    }

    // The site CSS sizes the band grid for 6 columns; the Gold +% column makes 7,
    // so pin the layout here (scoped to the editor, wins on specificity).
    function ensureBandGoldCss() {
        if (document.getElementById('qlBandGoldCss')) return;
        $('<style id="qlBandGoldCss">' +
            '#qlBandEditor .ql-band-head, #qlBandEditor .ql-band-row {' +
            ' display: grid;' +
            ' grid-template-columns: minmax(90px,1fr) 70px 58px 58px 52px 70px 28px;' +
            ' gap: 4px; align-items: center; }' +
            '</style>').appendTo('head');
    }

    function readBands() {
        var bands = [];
        $('#qlBandRows .ql-band-row').each(function () {
            var $r = $(this);
            var min = parseFloat($r.find('.ql-b-min').val());
            var max = parseFloat($r.find('.ql-b-max').val());
            var slots = parseInt($r.find('.ql-b-slots').val());
            if (isNaN(min)) min = 0;
            if (isNaN(max)) max = min;
            if (max < min) { var t = min; min = max; max = t; }   // tolerate swapped entry
            if (isNaN(slots) || slots < 0) slots = 0;
            var gold = parseFloat($r.find('.ql-b-gold').val());
            bands.push({
                label: ($r.find('.ql-b-label').val() || '').trim(),
                position: $r.find('.ql-b-pos').val() || 'suffix',
                minBoostPct: min,
                maxBoostPct: max,
                slots: slots,
                goldBumpPct: isNaN(gold) ? null : Math.max(0, gold)
            });
        });
        return bands;
    }

    function bandSlotTotal() {
        return readBands().reduce(function (n, b) { return n + (b.slots || 0); }, 0)
            + ($('#qlLegendary').is(':checked') ? 1 : 0);
    }

    function updateBandTotal() {
        var leg = $('#qlLegendary').is(':checked');
        $('#qlBandTotal').text('≈ ' + bandSlotTotal() + ' variants / item' + (leg ? ' (incl. legendary)' : ''));
        updateCommitBar();
    }

    $(document).on('click', '#qlAddBand', function (e) {
        e.preventDefault();
        $('#qlBandRows').append(bandRowHtml({ label: 'Custom', position: 'suffix', minBoostPct: 15, maxBoostPct: 25, slots: 1 }));
        updateBandTotal();
    });
    $(document).on('click', '#qlResetBands', function (e) {
        e.preventDefault();
        renderBandEditor(DEFAULT_BANDS);
        $('#qlGoldScale').val(100);   // Defaults resets the gold controls too (like bump bias)
        $('#qlLegGold').val(500);
    });
    $(document).on('click', '.ql-b-del', function (e) {
        e.preventDefault();
        $(this).closest('.ql-band-row').remove();
        updateBandTotal();
    });
    $(document).on('input change', '#qlBandRows input, #qlBandRows select, #qlBumpBias', updateBandTotal);
    $(document).on('change', '#qlLegendary', updateBandTotal);

    // Self-bootstrap: mount the editor (creating a container if the view doesn't
    // provide #qlBandEditor) and retire the old ceiling / variant-count inputs.
    function initBandEditor() {
        if (!$('#qlBandEditorStyles').length) {
            $('head').append('<style id="qlBandEditorStyles">' +
                '.ql-band-editor{margin:10px 0;padding:10px;border:1px solid rgba(128,128,128,.28);border-radius:8px;background:rgba(128,128,128,.06);}' +
                '.ql-band-title{font-size:12px;font-weight:600;margin-bottom:8px;opacity:.85;}' +
                '.ql-band-head,.ql-band-row{display:grid;grid-template-columns:1.5fr .8fr .7fr .7fr .6fr 30px;gap:6px;align-items:center;}' +
                '.ql-band-head{font-size:10px;text-transform:uppercase;letter-spacing:.04em;opacity:.55;margin-bottom:6px;padding:0 2px;}' +
                '.ql-band-row{margin-bottom:6px;}' +
                '.ql-band-row input,.ql-band-row select{width:100%;box-sizing:border-box;padding:4px 6px;font-size:12px;border-radius:5px;border:1px solid rgba(128,128,128,.35);background:rgba(0,0,0,.18);color:inherit;}' +
                '.ql-b-del{border:none;background:transparent;color:#c0392b;cursor:pointer;font-size:14px;}' +
                '.ql-band-actions{display:flex;align-items:center;gap:10px;margin-top:8px;flex-wrap:wrap;}' +
                '.ql-band-btn{font-size:11px;padding:4px 9px;border-radius:6px;border:1px solid rgba(128,128,128,.35);background:rgba(128,128,128,.12);color:inherit;cursor:pointer;}' +
                '.ql-bump{font-size:11px;opacity:.85;display:inline-flex;align-items:center;gap:6px;}' +
                '.ql-bump input{width:56px;padding:3px 5px;border-radius:5px;border:1px solid rgba(128,128,128,.35);background:rgba(0,0,0,.18);color:inherit;}' +
                '#qlBandTotal{font-size:11px;margin-left:auto;}' +
                '</style>');
        }
        if (!$('#qlBandEditor').length) {
            var $anchor = $('#qlLegendary').closest('label');
            if (!$anchor.length) $anchor = $('#qlLegendary');
            var $ed = $('<div id="qlBandEditor" class="ql-band-editor"></div>');
            if ($anchor.length) $ed.insertAfter($anchor); else $('#qlSinglePanel').prepend($ed);
        } else {
            $('#qlBandEditor').addClass('ql-band-editor');
        }
        // Hide the retired inputs if the .cshtml still renders them.
        ['#qlBudgetCeiling', '#qlVariants'].forEach(function (sel) {
            var $g = $(sel).closest('label');
            (($g.length ? $g : $(sel))).hide();
        });
        renderBandEditor(DEFAULT_BANDS);
    }
    $(initBandEditor);

    // ── Mode toggle ──
    $(document).on('click', '.ql-mode', function () {
        mode = $(this).data('mode');
        $('.ql-mode').removeClass('active');
        $(this).addClass('active');

        if (mode === 'single') {
            $('#qlSinglePanel').show();
            $('#qlAllPanel').hide();
            $('#qlBrowsePanel').hide();
            $('#qlModeHint').text('Search a quest and lootify its reward items.');
            updateCommitBar();
        } else if (mode === 'all') {
            $('#qlSinglePanel').hide();
            $('#qlAllPanel').show();
            $('#qlBrowsePanel').hide();
            $('#qlCommitBar').hide();
            $('#qlModeHint').text('Generate variants for every eligible quest reward in the game.');
        } else { // browse
            $('#qlSinglePanel').hide();
            $('#qlAllPanel').hide();
            $('#qlBrowsePanel').show();
            $('#qlCommitBar').hide();
            $('#qlModeHint').text('Everything that has been lootified, per item.');
            browseState.page = 1;
            loadBrowse();
        }
    });

    // ── Search ──
    function doSearch() {
        var q = $('#qlSearch').val().trim();
        if (q.length < 2) { showToast('Enter at least 2 characters', 'error'); return; }

        $('#qlSearchBtn').prop('disabled', true).html('<i class="fa-solid fa-spinner fa-spin"></i>');
        $.getJSON('/QuestLootifier/SearchQuest', { q: q }, function (data) {
            $('#qlSearchBtn').prop('disabled', false).html('<i class="fa-solid fa-magnifying-glass"></i>');
            renderSearchResults(data.results || []);
        }).fail(function () {
            $('#qlSearchBtn').prop('disabled', false).html('<i class="fa-solid fa-magnifying-glass"></i>');
            showToast('Search failed', 'error');
        });
    }
    $('#qlSearchBtn').on('click', doSearch);
    $('#qlSearch').on('keypress', function (e) { if (e.which === 13) doSearch(); });

    function renderSearchResults(results) {
        $('#qlSelectedQuest').hide();
        if (results.length === 0) {
            $('#qlSearchResults').html('<div class="ql-empty">No quests with item rewards found.</div>');
            return;
        }
        var h = '';
        results.forEach(function (r) {
            var lvl = r.questLevel > 0 ? ('Lv ' + r.questLevel) : (r.minLevel > 0 ? ('Min ' + r.minLevel) : '');
            h += '<div class="ql-result" data-entry="' + r.entry + '" data-title="' + esc(r.title) + '">' +
                '<span style="flex:1;">' + esc(r.title) + '</span>' +
                '<span class="ql-lvl">#' + r.entry + '</span>' +
                '<span class="ql-lvl">' + esc(lvl) + '</span>' +
                '</div>';
        });
        $('#qlSearchResults').html(h);
    }

    $(document).on('click', '.ql-result', function () {
        var entry = parseInt($(this).data('entry'));
        loadQuestRewards(entry);
    });

    // ── Load rewards for a quest ──
    function loadQuestRewards(questEntry) {
        $.getJSON('/QuestLootifier/QuestRewards', { questEntry: questEntry }, function (data) {
            if (!data.success) { showToast(data.error || 'Failed to load quest', 'error'); return; }

            selectedQuest = data.quest;
            rewardItems = data.items || [];
            iconMap = data.icons || {};
            selectedItems = {};
            rewardItems.forEach(function (it) { if (it.eligible) selectedItems[it.entry] = true; });

            $('#qlSearchResults').html('');
            renderRewards();
            $('#qlSelectedQuest').show();
            $('#qlPreviewBtn').show();
            updateCommitBar();
        }).fail(function () { showToast('Failed to load quest rewards', 'error'); });
    }

    function renderRewards() {
        $('#qlQuestHead').text('#' + selectedQuest.entry + ' — ' + selectedQuest.title);
        var eligibleCount = rewardItems.filter(function (i) { return i.eligible; }).length;
        $('#qlRewardCount').text('(' + eligibleCount + ' lootifiable of ' + rewardItems.length + ')');

        var h = '';
        rewardItems.forEach(function (it) {
            var icon = iconMap[it.displayId] || '/icons/inv_misc_questionmark.png';
            var checked = selectedItems[it.entry] ? 'checked' : '';
            var disabled = it.eligible ? '' : 'disabled';
            h += '<div class="ql-reward-wrap" data-entry="' + it.entry + '">' +
                '<div class="ql-reward ' + (it.eligible ? '' : 'ineligible') + '">' +
                '<input type="checkbox" class="ql-reward-check" data-entry="' + it.entry + '" ' + checked + ' ' + disabled + ' />' +
                '<img src="' + esc(icon) + '" />' +
                '<span class="ql-name ' + qualClass(it.quality) + '">' + esc(it.name) + '</span>' +
                (it.lootified
                    ? '<button class="ql-view-variants" data-entry="' + it.entry + '" title="Show the generated variants">' +
                    '<i class="fa-solid fa-check"></i> ' + it.variantCount + ' variants <i class="fa-solid fa-chevron-down ql-chev"></i></button>'
                    : '') +
                '<span class="ql-kind ' + it.kind + '">' + it.kind + '</span>' +
                (it.eligible ? '' : '<span class="text-muted" style="font-size:10px;">no stats</span>') +
                '</div>' +
                '<div class="ql-reward-variants" data-entry="' + it.entry + '" style="display:none;"></div>' +
                '</div>';
        });
        $('#qlRewardList').html(h);
    }

    // Expand/collapse the actual generated variants for a lootified reward.
    $(document).on('click', '.ql-view-variants', function (e) {
        e.stopPropagation();
        var entry = parseInt($(this).data('entry'));
        var $panel = $('.ql-reward-variants[data-entry="' + entry + '"]');
        var $chev = $(this).find('.ql-chev');

        if ($panel.is(':visible')) {
            $panel.slideUp(150);
            $chev.removeClass('fa-chevron-up').addClass('fa-chevron-down');
            return;
        }

        $chev.removeClass('fa-chevron-down').addClass('fa-chevron-up');
        if ($panel.data('loaded')) { $panel.slideDown(150); return; }

        $panel.html('<div class="ql-empty" style="padding:10px 0;"><i class="fa-solid fa-spinner fa-spin"></i> Loading variants…</div>').slideDown(150);
        $.getJSON('/QuestLootifier/ItemVariants', { baseEntry: entry }, function (data) {
            if (!data.success) { $panel.html('<div class="ql-empty">Failed to load.</div>'); return; }
            renderItemVariants($panel, data.variants || [], data.icons || {});
            $panel.data('loaded', true);
        }).fail(function () { $panel.html('<div class="ql-empty">Failed to load.</div>'); });
    });

    function renderItemVariants($panel, variants, icons) {
        if (variants.length === 0) { $panel.html('<div class="ql-empty">No variants.</div>'); return; }
        var h = '<div class="ql-ivhead"><span>Variant</span><span>Stats</span><span title="Chance this variant is the one awarded at turn-in">Award %</span></div>';
        variants.forEach(function (v) {
            var statStr = v.stats.map(function (s) { return '+' + s.statValue + ' ' + s.name; }).join(', ') || '—';
            var vq = v.isLegendary ? 5 : v.quality;
            var chance = (v.dropChance != null ? v.dropChance : 0) + '%';
            var chanceCol = v.isLegendary
                ? '<span class="ql-vleg">LEG</span> ' + chance
                : chance;
            h += '<div class="ql-ivrow">' +
                '<span class="ql-ivname ' + qualClass(vq) + '">' + esc(v.name) + '</span>' +
                '<span class="ql-ivstats">' + esc(statStr) + '</span>' +
                '<span class="ql-ivbudget">' + chanceCol + '</span>' +
                '</div>';
        });
        $panel.html(h);
    }

    $(document).on('change', '.ql-reward-check', function () {
        var entry = parseInt($(this).data('entry'));
        if ($(this).is(':checked')) selectedItems[entry] = true;
        else delete selectedItems[entry];
        updateCommitBar();
    });

    function selectedEntries() {
        return Object.keys(selectedItems).map(Number);
    }

    function updateCommitBar() {
        if (mode !== 'single') return;
        var n = selectedEntries().length;
        if (n === 0 || !selectedQuest) {
            $('#qlCommitBar').hide();
            return;
        }
        var per = bandSlotTotal();
        $('#qlCommitBar').show();
        $('#qlCommitSummary').text(n + ' reward item(s) × ~' + per + ' variants = ~' + (n * per) + ' new items for "' + selectedQuest.title + '".');
        $('#qlCommitBtn').html('<i class="fa-solid fa-bolt"></i> Generate Variants');
    }

    // (Variant count is driven by the band editor now; see updateBandTotal.)

    // ── Preview ──
    $('#qlPreviewBtn').on('click', function () {
        var entries = selectedEntries();
        if (entries.length === 0) { showToast('Select at least one reward item', 'error'); return; }

        $('#qlPreview').html('<div class="ql-empty"><i class="fa-solid fa-spinner fa-spin"></i> Rolling samples...</div>');
        $.ajax({
            url: '/QuestLootifier/Preview',
            method: 'POST',
            contentType: 'application/json',
            data: JSON.stringify({ itemEntries: entries, ruleset: collectRuleset() }),
            success: function (data) {
                if (!data.success) { showToast(data.error || 'Preview failed', 'error'); return; }
                renderPreview(data.items || []);
            },
            error: function () { showToast('Preview failed', 'error'); }
        });
    });

    function renderPreview(items) {
        if (items.length === 0) {
            $('#qlPreview').html('<div class="ql-empty">No previewable items.</div>');
            return;
        }
        var h = '';
        items.forEach(function (it) {
            var icon = it.baseItem.iconPath || '/icons/inv_misc_questionmark.png';
            var baseQ = it.baseItem.quality;
            h += '<div class="ql-variant-block">' +
                '<div class="ql-variant-head"><img src="' + esc(icon) + '" /> ' +
                '<span class="' + qualClass(baseQ) + '">' + esc(it.baseItem.name) + '</span>' +
                ' <span class="text-muted" style="font-weight:400;">' + it.variants.length + ' variants</span></div>';
            it.variants.forEach(function (v) {
                var statStr = v.stats.map(function (s) { return '+' + s.statValue + ' ' + s.name; }).join(', ');
                var vq = v.isLegendary ? 5 : (v.quality != null ? v.quality : variantQuality(baseQ, v.budgetPct));
                var budgetLabel = v.isLegendary ? 'LEG' : ('+' + v.budgetPct + '%');
                h += '<div class="ql-variant-row">' +
                    '<span class="ql-vname ' + qualClass(vq) + '">' + esc(v.name) + '</span>' +
                    '<span class="ql-vstats">' + esc(statStr) + '</span>' +
                    '<span class="ql-vbudget">' + budgetLabel + '</span>' +
                    '</div>';
            });
            h += '</div>';
        });
        $('#qlPreview').html(h);
    }

    // ── Single-quest commit ──
    $('#qlCommitBtn').on('click', function () {
        var entries = selectedEntries();
        if (entries.length === 0) { showToast('Select at least one reward item', 'error'); return; }
        var payload = { allQuests: false, itemEntries: entries, ruleset: collectRuleset() };

        var $btn = $('#qlCommitBtn');
        $btn.prop('disabled', true).html('<i class="fa-solid fa-spinner fa-spin"></i> Generating...');
        $.ajax({
            url: '/QuestLootifier/Commit',
            method: 'POST',
            contentType: 'application/json',
            data: JSON.stringify(payload),
            success: function (r) {
                $btn.prop('disabled', false).html('<i class="fa-solid fa-bolt"></i> Generate Variants');
                if (r.success) {
                    var msg = r.itemsCreated + ' variants across ' + r.basesProcessed + ' reward items';
                    if (r.basesSkipped > 0) msg += ' (' + r.basesSkipped + ' already done)';
                    showToast(msg, 'success');
                    if (r.reloadHint) showToast(r.reloadHint, 'info');
                    loadCoverage();
                    if (selectedQuest) loadQuestRewards(selectedQuest.entry); // refresh badges
                } else {
                    showToast(r.error || 'Commit failed', 'error');
                }
            },
            error: function () {
                $btn.prop('disabled', false).html('<i class="fa-solid fa-bolt"></i> Generate Variants');
                showToast('Commit failed — server error', 'error');
            }
        });
    });

    // ── Chunked "All Quests" runner ──
    var runState = { active: false, cancel: false };
    var CHUNK_SIZE = 100;

    $('#qlRunAllBtn').on('click', function () {
        if (runState.active) return;
        var regenerate = $('#qlRegenerate').is(':checked');
        var confirmMsg = 'Generate variants for EVERY eligible quest reward item in the game'
            + (regenerate ? ', REPLACING items already done' : '') + '? Reversible via Rollback All.';
        if (!window.confirm(confirmMsg)) return;

        runState = { active: true, cancel: false };
        $('#qlRunAllBtn').hide();
        $('#qlRunProgress').show();
        setProgress(0, 'Planning…');

        $.getJSON('/QuestLootifier/PlanAllQuests', function (plan) {
            if (!plan.success) { finishRun('Planning failed.'); return; }

            // Regenerate replaces everything → one deliberate server call (the
            // work set includes already-done items, which we don't list here).
            if (regenerate) {
                setProgress(5, 'Regenerating all items…');
                $.ajax({
                    url: '/QuestLootifier/Commit', method: 'POST', contentType: 'application/json',
                    data: JSON.stringify({ allQuests: true, regenerate: true, ruleset: collectRuleset() }),
                    success: function (r) {
                        setProgress(100, 'Done.');
                        showToast((r.itemsCreated || 0) + ' variants generated.', 'success');
                        if (r.reloadHint) showToast(r.reloadHint, 'info');
                        loadCoverage();
                        finishRun(null);
                    },
                    error: function () { finishRun('Regenerate failed.'); }
                });
                return;
            }

            // Normal path: chunk through the remaining (not-yet-done) items.
            var work = plan.remaining || [];
            var total = work.length;
            if (total === 0) {
                setProgress(100, 'Everything already lootified.');
                showToast('All eligible items already lootified.', 'info');
                finishRun(null);
                return;
            }

            var index = 0, createdTotal = 0, doneItems = 0;
            function nextChunk() {
                if (runState.cancel) {
                    showToast('Stopped. ' + doneItems + ' of ' + total + ' items done.', 'info');
                    showToast("Run '.reload quest_variants' for what was generated.", 'info');
                    loadCoverage();
                    finishRun(null);
                    return;
                }
                if (index >= total) {
                    setProgress(100, 'Done — ' + createdTotal + ' variants across ' + doneItems + ' items.');
                    showToast(createdTotal + ' variants across ' + doneItems + ' items.', 'success');
                    showToast("Run '.reload quest_variants' so the core picks them up.", 'info');
                    loadCoverage();
                    finishRun(null);
                    return;
                }
                var batch = work.slice(index, index + CHUNK_SIZE);
                $.ajax({
                    url: '/QuestLootifier/Commit', method: 'POST', contentType: 'application/json',
                    data: JSON.stringify({ allQuests: false, itemEntries: batch, ruleset: collectRuleset() }),
                    success: function (r) {
                        if (r.success) {
                            createdTotal += (r.itemsCreated || 0);
                            doneItems += (r.basesProcessed || 0);
                        }
                        index += batch.length;
                        var pct = Math.round(100 * index / total);
                        setProgress(pct, index + ' / ' + total + ' items · ' + createdTotal + ' variants');
                        nextChunk();
                    },
                    error: function () {
                        showToast('A batch failed at item ' + index + '. Stopping.', 'error');
                        loadCoverage();
                        finishRun(null);
                    }
                });
            }
            setProgress(0, '0 / ' + total + ' items');
            nextChunk();
        }).fail(function () { finishRun('Could not plan the run.'); });
    });

    $('#qlCancelRun').on('click', function () {
        if (runState.active) { runState.cancel = true; showToast('Stopping after current batch…', 'info'); }
    });

    function setProgress(pct, text) {
        $('#qlProgressFill').css('width', pct + '%');
        if (text != null) $('#qlProgressText').text(text);
    }

    function finishRun(errMsg) {
        runState.active = false;
        $('#qlRunAllBtn').show();
        if (errMsg) { showToast(errMsg, 'error'); $('#qlRunProgress').hide(); return; }
        setTimeout(function () { $('#qlRunProgress').fadeOut(400); }, 2500);
    }

    // ── Coverage ──
    function loadCoverage() {
        $.getJSON('/QuestLootifier/Status', function (s) {
            var pct = s.coveragePct || 0;
            $('#qlCoverageFill').css('width', pct + '%');
            $('#qlCoverageLabel').text(
                (s.baseItems || 0) + ' / ' + (s.eligibleTotal || 0) + ' items (' + pct + '%)');
        });
    }
    loadCoverage();

    // ── Status ──
    $('#btnQlStatus').on('click', function () {
        $.getJSON('/QuestLootifier/Status', function (s) {
            if (!s.active) { showToast('No quest variants generated yet.', 'info'); return; }
            showToast(s.totalVariants + ' variants · ' + s.baseItems + ' / ' + s.eligibleTotal +
                ' items (' + s.coveragePct + '% coverage).', 'info');
        });
    });

    // ── Rollback all ──
    $('#btnQlRollbackAll').on('click', function () {
        if (!window.confirm('Remove ALL quest reward variants? Players will get the plain base item again until you regenerate. (The C++ store must be reloaded after.)')) return;

        var $btn = $(this);
        $btn.prop('disabled', true).html('<i class="fa-solid fa-spinner fa-spin"></i> Rolling back...');
        $.ajax({
            url: '/QuestLootifier/Rollback',
            method: 'POST',
            contentType: 'application/json',
            data: JSON.stringify({ baseEntry: 0 }),
            success: function (r) {
                $btn.prop('disabled', false).html('<i class="fa-solid fa-rotate-left"></i> Rollback All');
                if (r.success) {
                    showToast(r.removed + ' variants removed.', 'success');
                    if (r.reloadHint) showToast(r.reloadHint, 'info');
                    loadCoverage();
                } else {
                    showToast(r.error || 'Rollback failed', 'error');
                }
            },
            error: function () {
                $btn.prop('disabled', false).html('<i class="fa-solid fa-rotate-left"></i> Rollback All');
                showToast('Rollback failed', 'error');
            }
        });
    });

    var RV_LABEL = 'Quest Lootifier';
    var RV_BTN_ID = 'btnQlRevalue';
    var RV_ANCHOR_ID = 'btnQlRollbackAll';
    var RV_URL_TIERS = '/QuestLootifier/RevalueTiers';
    var RV_URL_APPLY = '/QuestLootifier/Revalue';

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

    // ── Browse lootified ──
    function loadBrowse() {
        $('#qlBrowseList').html('<div class="ql-empty"><i class="fa-solid fa-spinner fa-spin"></i> Loading...</div>');
        $.getJSON('/QuestLootifier/Browse', {
            q: browseState.q, page: browseState.page, pageSize: browseState.pageSize
        }, function (data) {
            browseState.total = data.total || 0;
            renderBrowse(data);
        }).fail(function () {
            $('#qlBrowseList').html('<div class="ql-empty">Failed to load.</div>');
        });
    }

    function renderBrowse(data) {
        var items = data.items || [];
        var icons = data.icons || {};
        $('#qlBrowseCount').text(browseState.total + ' item(s)');

        if (items.length === 0) {
            $('#qlBrowseList').html('<div class="ql-empty">Nothing lootified' + (browseState.q ? ' matches your filter.' : ' yet.') + '</div>');
            $('#qlBrowsePager').hide();
            return;
        }

        var h = '';
        items.forEach(function (it) {
            var icon = icons[it.displayId] || '/icons/inv_misc_questionmark.png';
            h += '<div class="ql-browse-wrap">' +
                '<div class="ql-browse-row" data-entry="' + it.baseEntry + '">' +
                '<img src="' + esc(icon) + '" />' +
                '<span class="ql-bname ' + qualClass(it.quality) + '">' + esc(it.name) + '</span>' +
                (it.hasLegendary ? '<span class="ql-bleg">Leg</span>' : '') +
                '<button class="ql-bcount ql-view-variants" data-entry="' + it.baseEntry + '" title="Show variants">' +
                it.variantCount + ' variants <i class="fa-solid fa-chevron-down ql-chev"></i></button>' +
                '<span class="text-muted" style="font-size:10px;">#' + it.baseEntry + '</span>' +
                '<button class="ql-brollback" data-entry="' + it.baseEntry + '" title="Remove this item\'s variants"><i class="fa-solid fa-trash"></i></button>' +
                '</div>' +
                '<div class="ql-reward-variants" data-entry="' + it.baseEntry + '" style="display:none;"></div>' +
                '</div>';
        });
        $('#qlBrowseList').html(h);

        var totalPages = Math.max(1, Math.ceil(browseState.total / browseState.pageSize));
        if (totalPages > 1) {
            $('#qlBrowsePager').show();
            $('#qlBrowsePageInfo').text('Page ' + data.page + ' of ' + totalPages);
            $('#qlBrowsePrev').prop('disabled', data.page <= 1);
            $('#qlBrowseNext').prop('disabled', data.page >= totalPages);
        } else {
            $('#qlBrowsePager').hide();
        }
    }

    var browseSearchTimer = null;
    $('#qlBrowseSearch').on('input', function () {
        var val = $(this).val().trim();
        clearTimeout(browseSearchTimer);
        browseSearchTimer = setTimeout(function () {
            browseState.q = val;
            browseState.page = 1;
            loadBrowse();
        }, 300);
    });

    $('#qlBrowsePrev').on('click', function () {
        if (browseState.page > 1) { browseState.page--; loadBrowse(); }
    });
    $('#qlBrowseNext').on('click', function () {
        browseState.page++; loadBrowse();
    });

    // Per-item rollback from the browse list
    $(document).on('click', '.ql-brollback', function (e) {
        e.stopPropagation();
        var entry = parseInt($(this).data('entry'));
        if (!window.confirm('Remove all variants for item #' + entry + '? (Reload the C++ store after.)')) return;

        var $row = $(this).closest('.ql-browse-row');
        $(this).html('<i class="fa-solid fa-spinner fa-spin"></i>');
        $.ajax({
            url: '/QuestLootifier/Rollback',
            method: 'POST',
            contentType: 'application/json',
            data: JSON.stringify({ baseEntry: entry }),
            success: function (r) {
                if (r.success) {
                    showToast(r.removed + ' variants removed for #' + entry + '.', 'success');
                    $row.fadeOut(150, function () { $(this).remove(); });
                    browseState.total = Math.max(0, browseState.total - 1);
                    $('#qlBrowseCount').text(browseState.total + ' item(s)');
                    loadCoverage();
                    if (r.reloadHint) showToast(r.reloadHint, 'info');
                } else {
                    showToast(r.error || 'Rollback failed', 'error');
                }
            },
            error: function () { showToast('Rollback failed', 'error'); }
        });
    });

})();