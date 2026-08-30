// MangosSuperUI — ARPG Lootifier JS (v3 — tier-quota + prefix/suffix + spell-effect items)

$(function () {

    var meta = null;
    var selectedCreature = null;
    var lootTreeData = null;
    var previewData = null;
    var selectedItems = {};
    var rollbackCreature = 0;
    var batchData = null;
    var batchSelectedItems = {}; // creatureEntry → { itemEntry: true }
    var currentMode = 'single'; // 'single' or 'batch'
    var tierState = null;       // editable tier bands (min/max/label/pos/slots/gold/dps); null until meta loads
    var legendaryBand = null;   // { min, max } boost band for the boss legendary; seeded from meta

    var RANK_NAMES = { 0: 'Normal', 1: 'Elite', 2: 'Rare Elite', 3: 'Boss', 4: 'Rare' };
    var QUALITY_NAMES = ['Poor', 'Common', 'Uncommon', 'Rare', 'Epic', 'Reforged', 'Legendary', 'Artifact', 'Relic'];

    // ===================== INIT =====================

    BaselineSystem.checkStatus(function () {
        BaselineSystem.renderWarningBanner('#baselineWarning');
    });

    $.getJSON('/Lootifier/Meta', function (data) {
        meta = data;
        renderNamingTiers();
        buildBatchFilters();
    });

    // ===================== MODE TABS =====================

    function switchMode(mode) {
        currentMode = mode;
        $('.lf-mode-tab').removeClass('active');
        $('.lf-mode-tab[data-mode="' + mode + '"]').addClass('active');

        if (mode === 'single') {
            $('#singlePanel').show();
            $('#batchPanel').hide();
        } else {
            $('#singlePanel').hide();
            $('#batchPanel').show();
        }

        // Reset preview
        previewData = null;
        batchData = null;
        $('#previewContainer').html('<div class="lf-empty-state"><i class="fa-solid fa-dragon"></i>' +
            (mode === 'single' ? 'Search for a creature, select items, then generate variants' : 'Configure batch filters and scan for items') +
            '</div>');
        $('#previewInfo').text(mode === 'single' ? 'Select a creature and items' : 'Configure filters and scan');
        $('#commitPanel').hide();
        $('#batchSamplePanel').hide();
        $('#batchSampleContainer').hide().html('');
    }

    $(document).on('click', '.lf-mode-tab', function () {
        switchMode($(this).data('mode'));
    });

    // ===================== CREATURE SEARCH =====================

    function searchCreature() {
        var q = $('#creatureSearch').val().trim();
        if (q.length < 2) return;

        $('#btnSearchCreature').prop('disabled', true).html('<i class="fa-solid fa-spinner fa-spin"></i>');

        $.getJSON('/Lootifier/SearchCreature?q=' + encodeURIComponent(q), function (data) {
            $('#btnSearchCreature').prop('disabled', false).html('<i class="fa-solid fa-magnifying-glass"></i>');

            if (!data.results || data.results.length === 0) {
                $('#searchResults').show().html('<div class="lf-empty-state" style="padding:12px;font-size:12px;">No creatures found</div>');
                return;
            }

            var h = '';
            data.results.forEach(function (c) {
                var rankName = RANK_NAMES[c.rank] || 'Unknown';
                h += '<div class="lf-search-item" data-entry="' + c.entry + '">' +
                    '<div><span class="lf-sr-name">' + esc(c.name) + '</span></div>' +
                    '<div class="lf-sr-meta">' + rankName + ' &middot; Lv ' + c.level_min + '-' + c.level_max + '</div>' +
                    '</div>';
            });
            $('#searchResults').show().html(h);
        });
    }

    function selectCreature(entry) {
        $('#searchResults').hide();
        $('#selectedCreature').show();
        $('#lootTreeContainer').show().find('#lootTree').html('<div class="text-center p-4"><i class="fa-solid fa-spinner fa-spin"></i> Loading loot tree...</div>');
        $('#rulesetPanel').show();
        $('#btnGenerate').prop('disabled', true);

        $.getJSON('/Lootifier/LootTree?creatureEntry=' + entry, function (data) {
            if (!data.success) {
                showToast('Failed to load loot tree: ' + (data.error || ''), 'error');
                return;
            }

            lootTreeData = data;
            selectedCreature = data.creature;
            selectedItems = {};

            var c = data.creature;
            $('#selCreatureName').text(c.name);
            var rankName = RANK_NAMES[c.rank] || 'Unknown';
            var rankClass = c.rank === 3 ? 'boss' : (c.rank === 1 ? 'elite' : 'normal');
            $('#selCreatureRank').text(rankName).attr('class', 'lf-rank-badge ' + rankClass);
            $('#selCreatureLevel').text('Lv ' + c.level_min + '-' + c.level_max);
            $('#selCreatureLootId').text(c.loot_id);

            renderLootTree(data);
        });
    }

    // ===================== LOOT TREE RENDER =====================

    function renderLootTree(data) {
        var h = '';
        var icons = data.icons || {};

        if (data.directItems && data.directItems.length > 0) {
            h += '<div class="lf-loot-section">Direct Drops</div>';
            data.directItems.forEach(function (item) {
                h += renderLootRow(item, icons, true);
            });
        }

        if (data.referenceGroups && data.referenceGroups.length > 0) {
            data.referenceGroups.forEach(function (rg) {
                h += '<div class="lf-loot-section">Reference #' + rg.refEntry +
                    ' <span style="float:right;font-weight:400;">' + formatChance(rg.pointerChance) + '% roll</span></div>';
                rg.items.forEach(function (item) {
                    h += renderLootRow(item, icons, false);
                });
            });
        }

        if (h === '') {
            h = '<div class="lf-empty-state" style="padding:16px;">No loot data found</div>';
        }

        $('#lootTree').html(h);
        updateGenerateButton();

        // Populate legendary item picker with selectable items
        populateLegendaryPicker();
    }

    function renderLootRow(item, icons, isDirect) {
        var iconPath = icons[item.displayId] || '/Icon/Get?name=inv_misc_questionmark';
        var qualClass = 'quality-' + item.quality;
        var hasStats = item.totalStats > 0;
        var hasSpells = item.hasSpellEffects;
        var equippable = item.equippable !== false; // treat missing as equippable (older data)
        // Lootifier output already in this creature's pool. Never a valid base —
        // lootifying it produces 'Improved Improved <name>' and squares the count.
        var isGenerated = item.isGenerated === true;
        var isLootifiable = equippable && !isGenerated && (hasStats || hasSpells);
        var noStatsClass = isLootifiable ? '' : ' no-stats';
        var isSelected = selectedItems[item.itemEntry] ? ' selected' : '';

        var chanceStr = item.chance === 0 ? 'equal' : formatChance(Math.abs(item.chance)) + '%';

        var familyBadge;
        if (isGenerated) {
            familyBadge = '<span class="lf-item-family" style="color:var(--accent);">variant</span>';
        } else if (!equippable) {
            familyBadge = '<span class="lf-item-family" style="color:var(--status-error);">not gear</span>';
        } else if (hasStats) {
            familyBadge = '<span class="lf-item-family">' + esc(item.detectedFamily) + '</span>';
        } else if (hasSpells) {
            familyBadge = '<span class="lf-item-family" style="color:var(--accent);"><i class="fa-solid fa-bolt" style="font-size:8px;"></i> spell</span>';
        } else {
            familyBadge = '<span class="lf-item-family" style="color:var(--status-error);">no stats</span>';
        }

        var budgetStr = hasStats ? Math.round(item.weightedBudget) + 'wp' : (hasSpells ? 'spell' : '—');

        return '<div class="lf-loot-row' + noStatsClass + isSelected + '" data-item="' + item.itemEntry + '" data-has-stats="' + (isLootifiable ? '1' : '0') + '">' +
            (isLootifiable && !isDirect ? '<input type="checkbox" class="lf-loot-check" ' + (isSelected ? 'checked' : '') + ' />' : '<span style="width:14px;"></span>') +
            '<img src="' + esc(iconPath) + '" />' +
            '<span class="lf-item-name ' + qualClass + '">' + esc(item.itemName) + '</span>' +
            familyBadge +
            '<span class="lf-item-budget">' + budgetStr + '</span>' +
            '<span class="lf-item-chance">' + chanceStr + '</span>' +
            '</div>';
    }

    // ===================== NAMING TIERS (editable bands) =====================

    function ensureTierState() {
        if (tierState) return true;
        if (!meta || !meta.defaultNamingTiers) return false;
        var dr = meta.defaultRuleset || {};
        legendaryBand = {
            min: (dr.legendaryBoostMinPct != null ? dr.legendaryBoostMinPct : 55),
            max: (dr.legendaryBoostMaxPct != null ? dr.legendaryBoostMaxPct : 75),
            drop: (dr.legendaryDropPct != null ? dr.legendaryDropPct : 0.2)
        };
        tierState = meta.defaultNamingTiers.map(function (t) {
            return {
                minPct: t.minPct, maxPct: t.maxPct, label: t.label, position: t.position,
                slots: t.slots || 0,
                minBoostPct: (t.minBoostPct != null ? t.minBoostPct : null),
                maxBoostPct: (t.maxBoostPct != null ? t.maxBoostPct : null),
                goldBumpPct: (t.goldBumpPct != null ? t.goldBumpPct : null),
                dpsBumpPct: (t.dpsBumpPct != null ? t.dpsBumpPct : null)
            };
        });
        return true;
    }

    function tierRowHtml(t, i) {
        // Range % (the old percentile-of-ceiling pair) is no longer rendered — Boost %
        // replaced it. The values ride along hidden so an old saved ruleset round-trips.
        return '<div class="lf-tier-row" data-tier="' + i + '">' +
            '<input type="hidden" class="lf-tier-min" value="' + t.minPct + '" />' +
            '<input type="hidden" class="lf-tier-max" value="' + t.maxPct + '" />' +
            '<select class="form-input lf-tier-position" data-tier="' + i + '" title="Prefix or suffix the tier name">' +
            '<option value="prefix"' + (t.position === 'prefix' ? ' selected' : '') + '>Pre</option>' +
            '<option value="suffix"' + (t.position === 'suffix' ? ' selected' : '') + '>Suf</option>' +
            '</select>' +
            '<input type="text" class="form-input lf-tier-input" data-tier="' + i + '" value="' + esc(t.label) + '" placeholder="Tier name" />' +
            '<span class="lf-tier-boost">' +
            '<input type="number" class="form-input lf-tier-bmin" data-tier="' + i + '" value="' + (t.minBoostPct != null ? t.minBoostPct : 10) + '" min="0" max="500" step="1" title="How much stronger than the base item this tier rolls, as a % of the base stat budget. 30-40 = 30 to 40% above base." />' +
            '<span class="lf-tier-dash">\u2013</span>' +
            '<input type="number" class="form-input lf-tier-bmax" data-tier="' + i + '" value="' + (t.maxBoostPct != null ? t.maxBoostPct : 20) + '" min="0" max="500" step="1" title="Upper end of the boost over base for this tier (%)." />' +
            '</span>' +
            '<input type="number" class="form-input lf-tier-slots" data-tier="' + i + '" value="' + (t.slots ? t.slots : '') + '" min="0" max="30" step="1" placeholder="auto" title="Fixed number of variants for this tier. Blank/0 = auto (shares Variants-per-Item with the other auto tiers)." />' +
            '<input type="number" class="form-input lf-tier-gold" data-tier="' + i + '" value="' + (t.goldBumpPct != null ? t.goldBumpPct : '') + '" min="0" max="10000" step="5" placeholder="curve" title="Gold price bump above base (%). Blank = legacy budget curve." />' +
            '<input type="number" class="form-input lf-tier-dps" data-tier="' + i + '" value="' + (t.dpsBumpPct != null ? t.dpsBumpPct : '') + '" min="0" max="500" step="0.5" placeholder="0" title="Weapon DAMAGE bump above base (%) — weapons only, speed unchanged." />' +
            '<span class="lf-tier-rm" data-tier="' + i + '" title="Remove this tier"><i class="fa-solid fa-xmark"></i></span>' +
            '</div>';
    }

    // The boss legendary sits on the SAME ladder as the naming tiers and is read in
    // the same unit, so it gets a row here rather than a separate hardcoded budget
    // you can't see. It is NOT a naming tier: the name comes from the boss/suffix
    // rules, there is always exactly one per base item, and it only exists when the
    // Boss Legendary checkbox is on — so name, pos, slots and remove are inert.
    function legendaryRowHtml() {
        var lo = (legendaryBand && legendaryBand.min != null) ? legendaryBand.min : 55;
        var hi = (legendaryBand && legendaryBand.max != null) ? legendaryBand.max : 75;
        var drop = (legendaryBand && legendaryBand.drop != null) ? legendaryBand.drop : 0.2;
        return '<div class="lf-tier-row lf-tier-legendary" title="The boss legendary. Every knob that fits the ladder lives on this row.">' +
            '<span class="lf-tier-legpos">\u2014</span>' +
            '<span class="lf-tier-legname" title="Named from the boss, or from the melee/ranged/caster suffix below when the item drops from more than one creature."><i class="fa-solid fa-crown"></i> Boss Legendary</span>' +
            '<span class="lf-tier-boost">' +
            '<input type="number" class="form-input lf-tier-bmin lf-leg-bmin" value="' + lo + '" min="0" max="500" step="1" title="How much stronger than the base item the legendary rolls, as a % of the base stat budget \u2014 same unit as the tiers above." />' +
            '<span class="lf-tier-dash">\u2013</span>' +
            '<input type="number" class="form-input lf-tier-bmax lf-leg-bmax" value="' + hi + '" min="0" max="500" step="1" title="Upper end of the legendary boost over base (%)." />' +
            '</span>' +
            '<input type="number" class="form-input lf-tier-slots" value="1" disabled title="Always exactly one legendary per lootified base item - flagging it IS the slot." />' +
            '<input type="number" class="form-input lf-tier-gold lf-leg-gold" min="0" max="10000" step="25" title="Legendary gold bump above base (%)." />' +
            '<input type="number" class="form-input lf-tier-dps lf-leg-dps" min="0" max="500" step="0.5" title="Legendary weapon DAMAGE bump above base (%) \u2014 weapons only, speed unchanged." />' +
            '<input type="number" class="form-input lf-tier-drop lf-leg-drop" value="' + drop + '" min="0.01" max="100" step="0.05" title="Effective drop %. Legendary only - naming tiers take their share from the base item original pool chance." />' +
            '<span class="lf-tier-legtoggle" title="Include boss legendaries. Same switch as the old Boss Legendary checkbox."><input type="checkbox" class="lf-leg-on" /></span>' +
            '</div>';
    }

    function renderNamingTiers() {
        if (!ensureTierState()) return;
        // Preserve an in-progress edit of the legendary band across a re-render
        // (add/remove tier rebuilds the whole panel).
        var $liveLeg = $('#' + activeTierContainer() + ' .lf-tier-legendary');
        if ($liveLeg.length) {
            var lm = parseFloat($liveLeg.find('.lf-leg-bmin').val());
            var lx = parseFloat($liveLeg.find('.lf-leg-bmax').val());
            var ld = parseFloat($liveLeg.find('.lf-leg-drop').val());
            if (!isNaN(lm)) legendaryBand.min = lm;
            if (!isNaN(lx)) legendaryBand.max = lx;
            if (!isNaN(ld)) legendaryBand.drop = ld;
        }
        // Header cells are grid children in the SAME grid as every row, so they
        // stay aligned no matter how the browser sizes the flexible name column.
        var head = '<div class="lf-tier-head">' +
            '<span>Pos</span>' +
            '<span>Tier name</span>' +
            '<span title="How much stronger than the base item this tier rolls, as a % of the base stat budget.">Boost&nbsp;%</span>' +
            '<span title="Fixed count, or blank/0 for auto.">Slots</span>' +
            '<span title="Gold price bump above base (%). Blank = legacy curve.">Gold&nbsp;+%</span>' +
            '<span title="Weapon damage bump above base (%) - weapons only, speed unchanged.">DPS&nbsp;+%</span>' +
            '<span title="Legendary only. Naming tiers inherit their share from the base item original pool chance.">Drop&nbsp;%</span>' +
            '<span></span>' +
            '</div>';
        var h = head;
        tierState.forEach(function (t, i) { h += tierRowHtml(t, i); });
        h += legendaryRowHtml();
        h += '<button type="button" class="btn-micro lf-tier-add" style="margin-top:6px;"><i class="fa-solid fa-plus"></i> Add tier</button>';

        $('#namingTiers').html(h);
        $('#batchNamingTiers').html(h.replace(/data-tier="/g, 'data-batch-tier="'));
        syncLegendaryRow();
    }

    // Read the tier bands out of one panel's DOM (single or batch), in row order.
    function readTiersFromContainer(containerId) {
        var out = [];
        $('#' + containerId + ' .lf-tier-row').not('.lf-tier-legendary').each(function () {
            var $r = $(this);
            var g = parseFloat($r.find('.lf-tier-gold').val());
            var d = parseFloat($r.find('.lf-tier-dps').val());
            var s = parseInt($r.find('.lf-tier-slots').val(), 10);
            var bmin = parseFloat($r.find('.lf-tier-bmin').val());
            var bmax = parseFloat($r.find('.lf-tier-bmax').val());
            // Boost is the strength control now, so both bounds always go up. One
            // blank is treated as 0 rather than dropping the tier to legacy mode.
            if (isNaN(bmin) && isNaN(bmax)) { bmin = 0; bmax = 0; }
            else if (isNaN(bmin)) bmin = bmax;
            else if (isNaN(bmax)) bmax = bmin;
            out.push({
                minPct: parseFloat($r.find('.lf-tier-min').val()) || 0,
                maxPct: parseFloat($r.find('.lf-tier-max').val()) || 0,
                position: $r.find('.lf-tier-position').val() || 'suffix',
                label: $r.find('.lf-tier-input').val() || '',
                slots: isNaN(s) ? 0 : Math.max(0, s),
                goldBumpPct: isNaN(g) ? null : Math.max(0, g),
                dpsBumpPct: isNaN(d) ? null : Math.max(0, d),
                minBoostPct: Math.max(0, Math.min(bmin, bmax)),
                maxBoostPct: Math.max(0, Math.max(bmin, bmax))
            });
        });
        return out;
    }

    function activeTierContainer() {
        return currentMode === 'batch' ? 'batchNamingTiers' : 'namingTiers';
    }

    // Add / remove operate on tierState, preserving current edits, then re-render both panels.
    $(document).on('click', '.lf-tier-add', function () {
        var container = $(this).closest('#namingTiers, #batchNamingTiers').attr('id') || activeTierContainer();
        tierState = readTiersFromContainer(container);
        tierState.push({ minPct: 0, maxPct: 100, label: 'New Tier', position: 'suffix', slots: 0, goldBumpPct: null, dpsBumpPct: null, minBoostPct: 10, maxBoostPct: 20 });
        renderNamingTiers();
    });

    $(document).on('click', '.lf-tier-rm', function () {
        var container = $(this).closest('#namingTiers, #batchNamingTiers').attr('id') || activeTierContainer();
        var idx = $('#' + container + ' .lf-tier-row').index($(this).closest('.lf-tier-row'));
        tierState = readTiersFromContainer(container);
        if (idx >= 0 && tierState.length > 1) {
            tierState.splice(idx, 1);
            renderNamingTiers();
        }
    });

    function collectRuleset() {
        // Tier bands are read live from the active panel (any count, editable ranges).
        var tiers = readTiersFromContainer(activeTierContainer());
        if (tiers.length === 0 && ensureTierState()) tiers = tierState.slice();

        // Read from correct panel: batch shared inputs sync to single IDs
        var budgetCeiling, variantsPerItem, allowNew, maxAffix;
        if (currentMode === 'batch') {
            budgetCeiling = parseFloat($('.lf-rs-shared[data-target="rsBudgetCeiling"]').val()) || 35;
            variantsPerItem = parseInt($('.lf-rs-shared[data-target="rsVariantsPerItem"]').val()) || 9;
            allowNew = $('.lf-rs-shared-check[data-target="rsAllowNewAffixes"]').is(':checked');
            maxAffix = parseInt($('.lf-rs-shared[data-target="rsMaxAffixChange"]').val()) || 1;
        } else {
            budgetCeiling = parseFloat($('#rsBudgetCeiling').val()) || 35;
            variantsPerItem = parseInt($('#rsVariantsPerItem').val()) || 9;
            allowNew = $('#rsAllowNewAffixes').is(':checked');
            maxAffix = parseInt($('#rsMaxAffixChange').val()) || 1;
        }

        // Drop-chance strategy: "preserve" (split existing loot) vs "additive"
        // (independent tunable-chance pool that adds drops without dilution).
        var dropStrategy = $('#lfDropStrategy').is(':checked') ? 'additive' : 'preserve';
        var poolDropPct = parseFloat($('#lfPoolDropPct').val());
        if (isNaN(poolDropPct)) poolDropPct = 100;
        poolDropPct = Math.min(100, Math.max(0, poolDropPct));

        // Global value tuning (gold scale + legendary gold/dps). Batch mirrors sync
        // to the single-mode IDs; read the active panel like budget ceiling does.
        function tune(id, fallback) {
            var v;
            if (currentMode === 'batch') v = parseFloat($('.lf-rs-shared[data-target="' + id + '"]').val());
            if (isNaN(v)) v = parseFloat($('#' + id).val());
            if (isNaN(v)) v = fallback;
            return Math.max(0, v);
        }
        var legGold = tune('rsLegGold', 500);
        var legDps = tune('rsLegDps', 30);
        // The legendary now has its own row in the band editor, so its strength is
        // read from there in the same unit as every other tier.
        var $legRow = $('#' + activeTierContainer() + ' .lf-tier-row.lf-tier-legendary');
        var legBMin = parseFloat($legRow.find('.lf-tier-bmin').val());
        var legBMax = parseFloat($legRow.find('.lf-tier-bmax').val());
        if (isNaN(legBMin)) legBMin = 60;
        if (isNaN(legBMax)) legBMax = 80;

        return {
            budgetCeilingPct: budgetCeiling,
            variantsPerItem: variantsPerItem,
            allowNewAffixes: allowNew,
            maxAffixCountChange: maxAffix,
            dropChanceStrategy: dropStrategy,
            poolDropChancePct: poolDropPct,
            legendaryGoldBumpPct: legGold,
            legendaryDpsBumpPct: legDps,
            legendaryBoostMinPct: Math.max(0, Math.min(legBMin, legBMax)),
            legendaryBoostMaxPct: Math.max(0, Math.max(legBMin, legBMax)),
            namingTiers: tiers,
            // Legendary
            generateLegendary: currentMode === 'batch'
                ? $('.lf-batch-legendary-toggle').is(':checked')
                : $('#rsLegendaryToggle').is(':checked'),
            legendaryDropPct: currentMode === 'batch'
                ? (parseFloat($('.lf-batch-leg-drop').val()) || 0.2)
                : (parseFloat($('#rsLegendaryDropPct').val()) || 0.2),
            legendarySuffixMelee: currentMode === 'batch'
                ? ($('.lf-batch-leg-melee').val() || 'of Destruction')
                : ($('#rsLegSuffixMelee').val() || 'of Destruction'),
            legendarySuffixRanged: currentMode === 'batch'
                ? ($('.lf-batch-leg-ranged').val() || 'of the Hunt')
                : ($('#rsLegSuffixRanged').val() || 'of the Hunt'),
            legendarySuffixCaster: currentMode === 'batch'
                ? ($('.lf-batch-leg-caster').val() || 'of Arcana')
                : ($('#rsLegSuffixCaster').val() || 'of Arcana'),
            legendaryNameStyle: currentMode === 'batch'
                ? ($('.lf-batch-leg-namestyle').val() || 'named')
                : ($('#rsLegNameStyle').val() || 'named'),
            legendaryItemEntry: parseInt($('#rsLegendaryItem').val()) || 0
        };
    }

    // ===================== BATCH FILTERS =====================

    function buildBatchFilters() {
        if (!meta) return;

        var DUNGEONS = [
            { id: 389, name: 'Ragefire Chasm', level: '13-18' },
            { id: 36, name: 'Deadmines', level: '17-21' },
            { id: 43, name: 'Wailing Caverns', level: '17-24' },
            { id: 34, name: 'The Stockade', level: '22-30' },
            { id: 48, name: 'Blackfathom Deeps', level: '24-32' },
            { id: 33, name: 'Shadowfang Keep', level: '22-30' },
            { id: 47, name: 'Razorfen Kraul', level: '29-38' },
            { id: 90, name: 'Gnomeregan', level: '29-38' },
            { id: 189, name: 'Scarlet Monastery', level: '28-45' },
            { id: 129, name: 'Razorfen Downs', level: '37-46' },
            { id: 70, name: 'Uldaman', level: '41-51' },
            { id: 209, name: 'Zul\'Farrak', level: '44-54' },
            { id: 349, name: 'Maraudon', level: '46-55' },
            { id: 109, name: 'Sunken Temple', level: '50-56' },
            { id: 230, name: 'Blackrock Depths', level: '52-60' },
            { id: 229, name: 'Blackrock Spire', level: '55-60' },
            { id: 429, name: 'Dire Maul', level: '55-60' },
            { id: 329, name: 'Stratholme', level: '58-60' },
            { id: 289, name: 'Scholomance', level: '58-60' }
        ];
        var RAIDS = [
            { id: 249, name: 'Onyxia\'s Lair', level: '60' },
            { id: 409, name: 'Molten Core', level: '60' },
            { id: 469, name: 'Blackwing Lair', level: '60' },
            { id: 309, name: 'Zul\'Gurub', level: '60' },
            { id: 509, name: 'Ruins of Ahn\'Qiraj', level: '60' },
            { id: 531, name: 'Temple of Ahn\'Qiraj', level: '60' },
            { id: 533, name: 'Naxxramas', level: '60' }
        ];

        var h = '<div class="instance-category">Dungeons</div>';
        DUNGEONS.forEach(function (d) {
            h += '<button class="instance-chip" data-map="' + d.id + '">' +
                esc(d.name) + ' <span class="inst-level">' + d.level + '</span></button>';
        });
        h += '<div class="instance-category">Raids</div>';
        RAIDS.forEach(function (r) {
            h += '<button class="instance-chip" data-map="' + r.id + '">' +
                esc(r.name) + ' <span class="inst-level">' + r.level + '</span></button>';
        });
        $('#batchInstancePicker').html(h);
        loadZoneChips();
    }

    function loadZoneChips() {
        $.get('/Lootifier/Zones', function (data) {
            if (!data || !data.success) return;

            if (!data.available) {
                $('#batchInstancePicker').append(
                    '<div class="instance-category">Zones</div>' +
                    '<div style="font-size:11px;color:var(--text-muted);padding:4px 2px;">' +
                    'WorldMapArea.dbc not found in the DBC path (' + esc(data.dbcPath || 'Vmangos:DbcPath') + ') \u2014 zone filtering disabled.' +
                    '</div>');
                return;
            }

            function chips(list) {
                var s = '';
                list.forEach(function (z) {
                    s += '<button class="instance-chip zone-chip" data-zone="' + z.areaId + '">' + esc(z.name) + '</button>';
                });
                return s;
            }

            var ek = data.zones.filter(function (z) { return z.mapId === 0; });
            var kal = data.zones.filter(function (z) { return z.mapId === 1; });

            var zh = '';
            if (ek.length > 0) zh += '<div class="instance-category">Zones \u2014 Eastern Kingdoms</div>' + chips(ek);
            if (kal.length > 0) zh += '<div class="instance-category">Zones \u2014 Kalimdor</div>' + chips(kal);
            $('#batchInstancePicker').append(zh);
        });
    }

    function collectBatchFilters() {
        var filter = {};

        var quals = [];
        $('#batchPanel [data-quality].active').each(function () { quals.push(parseInt($(this).data('quality'))); });
        if (quals.length > 0) filter.qualities = quals;

        var ranks = [];
        $('#batchPanel [data-rank].active').each(function () { ranks.push(parseInt($(this).data('rank'))); });
        if (ranks.length > 0) filter.creatureRanks = ranks;

        var maps = [];
        $('#batchPanel .instance-chip.active[data-map]').each(function () { maps.push(parseInt($(this).data('map'))); });
        if (maps.length > 0) filter.mapIds = maps;

        var zones = [];
        $('#batchPanel .instance-chip.active[data-zone]').each(function () { zones.push(parseInt($(this).data('zone'))); });
        if (zones.length > 0) filter.zoneIds = zones;

        var lvlMin = parseInt($('#batchLevelMin').val());
        var lvlMax = parseInt($('#batchLevelMax').val());
        if (lvlMin > 0) filter.levelMin = lvlMin;
        if (lvlMax > 0) filter.levelMax = lvlMax;

        filter.ruleset = collectRuleset();
        return filter;
    }

    // ===================== BATCH SCAN =====================

    function batchScan() {
        var filter = collectBatchFilters();

        if (!filter.qualities || filter.qualities.length === 0) {
            showToast('Select at least one quality', 'error');
            return;
        }

        $('#btnBatchScan').prop('disabled', true).html('<i class="fa-solid fa-spinner fa-spin"></i> Scanning...');
        $('#previewContainer').html('<div class="text-center p-4"><i class="fa-solid fa-spinner fa-spin"></i> Scanning loot tables...</div>');
        $('#commitPanel').hide();

        $.ajax({
            url: '/Lootifier/BatchPreview',
            method: 'POST',
            contentType: 'application/json',
            data: JSON.stringify(filter),
            success: function (data) {
                $('#btnBatchScan').prop('disabled', false).html('<i class="fa-solid fa-magnifying-glass"></i> Scan Loot Tables');

                if (!data.success) {
                    showToast('Scan failed: ' + (data.error || ''), 'error');
                    return;
                }

                batchData = data;
                batchSelectedItems = {};

                data.creatures.forEach(function (c) {
                    batchSelectedItems[c.creatureEntry] = {};
                    c.items.forEach(function (it) {
                        batchSelectedItems[c.creatureEntry][it.itemEntry] = true;
                    });
                });

                renderBatchPreview(data);
            },
            error: function () {
                $('#btnBatchScan').prop('disabled', false).html('<i class="fa-solid fa-magnifying-glass"></i> Scan Loot Tables');
                showToast('Scan failed', 'error');
            }
        });
    }

    function renderBatchPreview(data) {
        if (!data.creatures || data.creatures.length === 0) {
            $('#previewContainer').html('<div class="lf-empty-state">No matching items found</div>');
            $('#previewInfo').text('No results');
            return;
        }

        var truncNote = data.truncated ? '<div style="padding:8px 14px;font-size:11px;color:var(--status-warning);"><i class="fa-solid fa-triangle-exclamation"></i> Showing first 500 rows — results truncated</div>' : '';

        var h = truncNote;
        var totalItems = 0;

        data.creatures.forEach(function (c) {
            var rankName = RANK_NAMES[c.creatureRank] || '';
            var icons = data.icons || {};

            h += '<div class="lf-batch-creature">';
            h += '<div class="lf-batch-creature-header" data-creature="' + c.creatureEntry + '">' +
                '<input type="checkbox" class="lf-batch-creature-check" data-creature="' + c.creatureEntry + '" checked />' +
                '<span class="lf-batch-creature-name">' + esc(c.creatureName) + '</span>' +
                '<span class="lf-rank-badge ' + (c.creatureRank === 3 ? 'boss' : (c.creatureRank === 1 ? 'elite' : 'normal')) + '">' + rankName + '</span>' +
                '<span class="text-muted" style="font-size:11px;">Lv ' + c.levelMin + '-' + c.levelMax + '</span>' +
                '<span class="lf-batch-item-count">' + c.items.length + ' items</span>' +
                '</div>';

            c.items.forEach(function (it) {
                var iconPath = icons[it.displayId] || '/Icon/Get?name=inv_misc_questionmark';
                var qualClass = 'quality-' + it.quality;
                totalItems++;

                h += '<div class="lf-batch-item" data-creature="' + c.creatureEntry + '" data-item="' + it.itemEntry + '">' +
                    '<input type="checkbox" class="lf-batch-item-check" data-creature="' + c.creatureEntry + '" data-item="' + it.itemEntry + '" checked />' +
                    '<img src="' + esc(iconPath) + '" style="width:18px;height:18px;image-rendering:pixelated;border-radius:2px;" />' +
                    '<span class="' + qualClass + '" style="flex:1;font-size:12px;">' + esc(it.itemName) + '</span>' +
                    '<span style="font-family:monospace;font-size:11px;color:var(--text-muted);">Lv' + it.requiredLevel + '</span>' +
                    '</div>';
            });

            h += '</div>';
        });

        $('#previewContainer').html(h);
        updateBatchStats();

        // Show sample preview option AND commit — both available immediately
        $('#batchSamplePanel').show();
        $('#batchSampleContainer').html('');
        $('#commitPanel').show();
    }

    // Dedup-aware estimates matching server BatchCommit behavior:
    // one variant set per DISTINCT base item; loot rows per (creature, item) pair.
    function computeBatchSelection() {
        var distinctItems = {};
        var pairs = 0;
        var creaturesWithSelection = 0;
        if (batchData && batchData.creatures) {
            batchData.creatures.forEach(function (c) {
                var sel = batchSelectedItems[c.creatureEntry];
                if (!sel) return;
                var any = false;
                c.items.forEach(function (it) {
                    if (sel[it.itemEntry]) {
                        distinctItems[it.itemEntry] = true;
                        pairs++;
                        any = true;
                    }
                });
                if (any) creaturesWithSelection++;
            });
        }
        return {
            distinct: Object.keys(distinctItems).length,
            pairs: pairs,
            creatures: creaturesWithSelection
        };
    }

    function updateBatchStats() {
        var s = computeBatchSelection();
        // Read the variants-per-item from the field that actually drives the
        // commit in the current mode (batch has its own shared input; the
        // single-mode #rsVariantsPerItem defaults to 9 and would mis-report).
        var variantsPerItem = currentMode === 'batch'
            ? (parseInt($('.lf-rs-shared[data-target="rsVariantsPerItem"]').val()) || 9)
            : (parseInt($('#rsVariantsPerItem').val()) || 9);

        $('#previewInfo').text(s.distinct + ' unique items \u00b7 ' + s.pairs + ' placements across ' + s.creatures + ' creatures');
        $('#commitItemCount').text(s.distinct * variantsPerItem);
        $('#commitLootRows').text('~' + (s.pairs * variantsPerItem));
        $('#commitBaseItems').text(s.distinct);
    }

    // ===================== BATCH SAMPLE PREVIEW =====================

    function batchSamplePreview() {
        if (!batchData || !batchData.creatures) return;

        // Pick up to 3 representative items: try for one physical, one caster, one spell-effect
        var allItems = [];
        batchData.creatures.forEach(function (c) {
            var sel = batchSelectedItems[c.creatureEntry];
            if (!sel) return;
            c.items.forEach(function (it) {
                if (sel[it.itemEntry]) allItems.push(it);
            });
        });

        if (allItems.length === 0) {
            showToast('No items selected', 'error');
            return;
        }

        // Pick diverse samples (up to 3)
        var samples = pickSampleItems(allItems, 3);
        var sampleEntries = samples.map(function (it) { return it.itemEntry; });

        $('#btnBatchSample').prop('disabled', true).html('<i class="fa-solid fa-spinner fa-spin"></i> Generating sample...');

        // Pick a creature entry for legendary preview context (first creature in scan)
        var sampleCreatureEntry = batchData.creatures.length > 0 ? batchData.creatures[0].creatureEntry : 0;

        $.ajax({
            url: '/Lootifier/BatchSamplePreview',
            method: 'POST',
            contentType: 'application/json',
            data: JSON.stringify({ creatureEntry: sampleCreatureEntry, itemEntries: sampleEntries, ruleset: collectRuleset() }),
            success: function (data) {
                $('#btnBatchSample').prop('disabled', false).html('<i class="fa-solid fa-eye"></i> Preview Sample Variants');

                if (!data.success || !data.items || data.items.length === 0) {
                    showToast('Sample preview failed', 'error');
                    return;
                }

                renderBatchSamplePreview(data);
            },
            error: function () {
                $('#btnBatchSample').prop('disabled', false).html('<i class="fa-solid fa-eye"></i> Preview Sample Variants');
                showToast('Sample preview failed', 'error');
            }
        });
    }

    function pickSampleItems(allItems, maxSamples) {
        // Try to get variety: different quality levels, different item types
        var byQuality = {};
        allItems.forEach(function (it) {
            if (!byQuality[it.quality]) byQuality[it.quality] = [];
            byQuality[it.quality].push(it);
        });

        var picked = [];
        var qualKeys = Object.keys(byQuality).sort(function (a, b) { return b - a; }); // highest quality first

        // Pick one from each quality tier
        for (var q = 0; q < qualKeys.length && picked.length < maxSamples; q++) {
            var pool = byQuality[qualKeys[q]];
            var idx = Math.floor(Math.random() * pool.length);
            picked.push(pool[idx]);
        }

        // Fill remaining slots randomly
        while (picked.length < maxSamples && picked.length < allItems.length) {
            var idx = Math.floor(Math.random() * allItems.length);
            var candidate = allItems[idx];
            if (!picked.find(function (p) { return p.itemEntry === candidate.itemEntry; })) {
                picked.push(candidate);
            }
        }

        return picked;
    }

    function renderBatchSamplePreview(data) {
        var h = '<div style="padding:10px 14px;font-size:12px;color:var(--accent);font-weight:600;border-bottom:1px solid var(--border-light);">' +
            '<i class="fa-solid fa-flask"></i> Sample Preview — ' + data.items.length + ' representative items' +
            '</div>';

        data.items.forEach(function (itemGroup) {
            var base = itemGroup.baseItem;
            var analysis = itemGroup.analysis;
            var variants = itemGroup.variants;

            var iconPath = base.iconPath || '/Icon/Get?name=inv_misc_questionmark';
            var qualClass = 'quality-' + base.quality;

            var spellBadge = '';
            if (analysis.hasSpellEffects && analysis.spellEffects.length > 0) {
                var spellNames = analysis.spellEffects.map(function (se) { return se.triggerName + ' #' + se.spellId; });
                spellBadge = ' <span class="lf-spell-badge"><i class="fa-solid fa-bolt"></i> ' + spellNames.join(', ') + '</span>';
            }

            var analysisStr = analysis.totalStats > 0
                ? 'Base: ' + analysis.totalStats + ' stats / ' + Math.round(analysis.weightedBudget) + 'wp / ' + esc(analysis.detectedFamily)
                : 'Spell-effect item';

            h += '<div class="lf-preview-group">';
            h += '<div class="lf-preview-header">' +
                '<img src="' + esc(iconPath) + '" />' +
                '<span class="' + qualClass + '">' + esc(base.name) + '</span>' +
                spellBadge +
                '<span class="lf-preview-analysis">' + analysisStr + '</span>' +
                '</div>';
            h += dpsRefLine(base);

            h += '<table class="lf-variant-table"><thead><tr>' +
                '<th>#</th><th>Name</th><th>Budget</th><th>Tier</th>' + dpsHeadCell(base) + '<th>Stats</th>' +
                '</tr></thead><tbody>';

            variants.forEach(function (v, idx) {
                var tierClass = getTierClass(v.tierLabel);
                var budgetColor = getBudgetColor(v.budgetPct);

                h += '<tr>' +
                    '<td style="color:var(--text-muted);font-size:11px;">' + (idx + 1) + '</td>' +
                    '<td style="font-weight:500;">' + esc(v.name) + '</td>' +
                    '<td><span class="lf-budget-bar"><span class="lf-budget-fill" style="width:' + Math.min(100, v.budgetPct) + '%;background:' + budgetColor + ';"></span></span>' +
                    '<span style="font-family:monospace;font-size:11px;">' + v.budgetPct + '%</span></td>' +
                    '<td><span class="lf-tier-badge ' + tierClass + '">' + esc(v.tierLabel || '—') + '</span></td>' +
                    dpsBodyCell(base, v) +
                    '<td>' + renderStatPills(v.stats, analysis.presentStatTypes) + '</td>' +
                    '</tr>';
            });

            h += '</tbody></table></div>';
        });

        // Legendary preview card
        if (data.legendary) {
            h += renderLegendaryCard(data.legendary);
        }

        $('#batchSampleContainer').html(h).show();
    }

    // ===================== GENERATE PREVIEW (single) =====================

    function generatePreview() {
        var entries = Object.keys(selectedItems).filter(function (k) { return selectedItems[k]; }).map(Number);
        if (entries.length === 0) {
            showToast('Select at least one item', 'error');
            return;
        }

        var ruleset = collectRuleset();

        $('#btnGenerate').prop('disabled', true).html('<i class="fa-solid fa-spinner fa-spin"></i> Generating...');
        $('#previewContainer').html('<div class="text-center p-4"><i class="fa-solid fa-spinner fa-spin"></i> Rolling variants...</div>');
        $('#commitPanel').hide();

        $.ajax({
            url: '/Lootifier/GeneratePreview',
            method: 'POST',
            contentType: 'application/json',
            data: JSON.stringify({ creatureEntry: selectedCreature ? selectedCreature.entry : 0, itemEntries: entries, ruleset: ruleset }),
            success: function (data) {
                $('#btnGenerate').prop('disabled', false).html('<i class="fa-solid fa-dice-d20"></i> Generate Variants Preview');

                if (!data.success) {
                    showToast('Generation failed: ' + (data.error || ''), 'error');
                    return;
                }

                previewData = data;
                renderSinglePreview(data);
            },
            error: function () {
                $('#btnGenerate').prop('disabled', false).html('<i class="fa-solid fa-dice-d20"></i> Generate Variants Preview');
                showToast('Generation failed', 'error');
            }
        });
    }

    function renderSinglePreview(data) {
        if (!data.items || data.items.length === 0) {
            $('#previewContainer').html('<div class="lf-empty-state">No variants generated. Selected items may have no rollable stats or spell effects.</div>');
            return;
        }

        var totalVariants = 0;
        var h = '';

        data.items.forEach(function (itemGroup) {
            var base = itemGroup.baseItem;
            var analysis = itemGroup.analysis;
            var variants = itemGroup.variants;
            totalVariants += variants.length;

            var iconPath = lootTreeData && lootTreeData.icons ? (lootTreeData.icons[base.displayId] || '/Icon/Get?name=inv_misc_questionmark') : '/Icon/Get?name=inv_misc_questionmark';
            var qualClass = 'quality-' + base.quality;

            // Show spell effects in header if present
            var spellBadge = '';
            if (analysis.hasSpellEffects && analysis.spellEffects.length > 0) {
                var spellNames = analysis.spellEffects.map(function (se) { return se.triggerName + ' #' + se.spellId; });
                spellBadge = ' <span class="lf-spell-badge"><i class="fa-solid fa-bolt"></i> ' + spellNames.join(', ') + '</span>';
            }

            var analysisStr = analysis.totalStats > 0
                ? 'Base: ' + analysis.totalStats + ' stats / ' + Math.round(analysis.weightedBudget) + 'wp / ' + esc(analysis.detectedFamily)
                : 'Spell-effect item';

            h += '<div class="lf-preview-group">';
            h += '<div class="lf-preview-header">' +
                '<img src="' + esc(iconPath) + '" />' +
                '<span class="' + qualClass + '">' + esc(base.name) + '</span>' +
                spellBadge +
                '<span class="lf-preview-analysis">' + analysisStr + '</span>' +
                '</div>';
            h += dpsRefLine(base);

            h += '<table class="lf-variant-table"><thead><tr>' +
                '<th>#</th><th>Name</th><th>Budget</th><th>Tier</th>' + dpsHeadCell(base) + '<th>Stats</th>' +
                '</tr></thead><tbody>';

            variants.forEach(function (v, idx) {
                var tierClass = getTierClass(v.tierLabel);
                var budgetColor = getBudgetColor(v.budgetPct);

                h += '<tr>' +
                    '<td style="color:var(--text-muted);font-size:11px;">' + (idx + 1) + '</td>' +
                    '<td style="font-weight:500;">' + esc(v.name) + '</td>' +
                    '<td><span class="lf-budget-bar"><span class="lf-budget-fill" style="width:' + Math.min(100, v.budgetPct) + '%;background:' + budgetColor + ';"></span></span>' +
                    '<span style="font-family:monospace;font-size:11px;">' + v.budgetPct + '%</span></td>' +
                    '<td><span class="lf-tier-badge ' + tierClass + '">' + esc(v.tierLabel || '—') + '</span></td>' +
                    dpsBodyCell(base, v) +
                    '<td>' + renderStatPills(v.stats, analysis.presentStatTypes) + '</td>' +
                    '</tr>';
            });

            h += '</tbody></table></div>';
        });

        // Legendary preview card
        if (data.legendary) {
            h += renderLegendaryCard(data.legendary);
            totalVariants += 1;
        }

        $('#previewContainer').html(h);
        var legendaryNote = data.legendary ? ' (includes 1 legendary)' : '';
        $('#previewInfo').text(totalVariants + ' variants across ' + data.items.length + ' items' + legendaryNote);

        $('#commitItemCount').text(totalVariants);
        $('#commitLootRows').text('~' + totalVariants);
        $('#commitBaseItems').text(data.items.length);
        $('#commitPanel').show();
    }

    function renderStatPills(stats, baseTypes) {
        var baseSet = {};
        if (baseTypes) baseTypes.forEach(function (t) { baseSet[t] = true; });

        var h = '';
        stats.forEach(function (s) {
            var isNew = !baseSet[s.statType];
            h += '<span class="lf-stat-pill' + (isNew ? ' new' : '') + '">+' + s.statValue + ' ' + esc(s.name) + '</span>';
        });
        return h;
    }

    function getTierClass(label) {
        if (!label) return 'variation';
        var s = label.toLowerCase();
        if (s.indexOf('gods') >= 0) return 'gods';
        if (s.indexOf('glory') >= 0) return 'glory';
        if (s.indexOf('power') >= 0) return 'power';
        return 'variation';
    }

    function getBudgetColor(pct) {
        if (pct >= 98) return '#ff8000';
        if (pct >= 90) return '#a335ee';
        if (pct >= 80) return 'var(--accent)';
        return 'var(--text-muted)';
    }

    // ── Weapon DPS preview (damage-only tier bump; speed unchanged) ──
    // Vanilla quality multipliers (mirror of Meta.dpsReference).
    var DPS_QUALITY_MULT = { 1: 1.0, 2: 1.0, 3: 1.105, 4: 1.215, 5: 1.30 };
    function dpsQualMult(q) { return DPS_QUALITY_MULT[q] || 1.0; }

    function isWeaponBase(base) { return base && base.weapon && base.weapon.isWeapon; }

    // Header cell only when the base is a weapon.
    function dpsHeadCell(base) { return isWeaponBase(base) ? '<th title="Resulting weapon DPS (damage-only bump; speed unchanged)">DPS</th>' : ''; }

    // Per-variant DPS cell (empty string for non-weapons so the row still lines up).
    function dpsBodyCell(base, v) {
        if (!isWeaponBase(base)) return '';
        if (v.dps == null) return '<td class="lf-dps-cell">—</td>';
        var bump = v.dpsBumpPct ? ' <span style="color:var(--text-muted);">+' + Number(v.dpsBumpPct).toFixed(1) + '%</span>' : '';
        return '<td class="lf-dps-cell">' + Number(v.dps).toFixed(1) + bump + '</td>';
    }

    // "relative to that tier of that level": base DPS + what blue/purple/legendary
    // would be at this weapon's level (base DPS scaled by the vanilla quality ratio
    // off the base's own quality).
    function dpsRefLine(base) {
        if (!isWeaponBase(base)) return '';
        var w = base.weapon, bm = dpsQualMult(base.quality);
        function tgt(q) { return (w.baseDps * dpsQualMult(q) / bm).toFixed(1); }
        return '<div class="lf-dps-ref">⚔ base <b>' + w.baseDps.toFixed(1) + '</b> DPS · ' + (w.delay / 1000).toFixed(2) + 's' +
            ' <span style="color:var(--text-muted);">— vanilla line: blue ' + tgt(3) + ' / purple ' + tgt(4) + ' / leg ' + tgt(5) + '</span></div>';
    }

    function renderLegendaryCard(legendary) {
        if (!legendary) return '';

        var iconPath = legendary.iconPath || '/Icon/Get?name=inv_misc_questionmark';
        var h = '<div class="lf-legendary-card">';
        h += '<div class="lf-legendary-card-header">' +
            '<i class="fa-solid fa-crown" style="color:#ff8000;font-size:14px;"></i>' +
            '<span style="color:#ff8000;font-weight:700;font-size:13px;margin-left:6px;">Boss Legendary Preview</span>' +
            '<span style="color:var(--text-muted);font-size:11px;margin-left:auto;">Drop: ' + legendary.dropPct + '%</span>' +
            '</div>';

        h += '<div class="lf-legendary-card-body">' +
            '<img src="' + esc(iconPath) + '" style="width:28px;height:28px;border-radius:4px;border:1px solid #ff8000;" />' +
            '<div style="flex:1;min-width:0;">' +
            '<div class="quality-6" style="font-weight:700;font-size:13px;">' + esc(legendary.legendaryName) + '</div>' +
            '<div style="font-size:11px;color:var(--text-muted);">Base: <span class="quality-' + legendary.baseItemQuality + '">' + esc(legendary.baseItemName) + '</span>' +
            ' &middot; Boss: ' + esc(legendary.bossName) +
            ' &middot; Budget: <span style="color:#ff8000;font-weight:600;">150%</span></div>' +
            '</div></div>';

        h += '<div class="lf-legendary-card-stats">';
        legendary.stats.forEach(function (s) {
            h += '<span class="lf-stat-pill" style="border-color:#ff8000;background:rgba(255,128,0,0.08);">+' + s.statValue + ' ' + esc(s.name) + '</span>';
        });
        h += '</div></div>';

        return h;
    }

    // ===================== COMMIT =====================

    function doCommit() {
        if (currentMode === 'single') {
            doSingleCommit();
        } else {
            doBatchCommit();
        }
    }

    function doSingleCommit() {
        if (!previewData || !selectedCreature) return;

        var commitPayload = {
            creatureEntry: selectedCreature.entry,
            ruleset: collectRuleset(),
            regenerate: $('#lfRegenerate').is(':checked'),
            variants: previewData.items.map(function (itemGroup) {
                return {
                    baseItemEntry: itemGroup.baseItem.entry,
                    rolls: itemGroup.variants.map(function (v) {
                        return {
                            budgetPct: v.budgetPct,
                            tierLabel: v.tierLabel || '',
                            tierPosition: v.tierPosition || 'suffix',
                            stats: v.stats.map(function (s) {
                                return { statType: s.statType, statValue: s.statValue };
                            })
                        };
                    })
                };
            })
        };

        $('#btnCommit').prop('disabled', true).html('<i class="fa-solid fa-spinner fa-spin"></i> Committing...');

        $.ajax({
            url: '/Lootifier/Commit',
            method: 'POST',
            contentType: 'application/json',
            data: JSON.stringify(commitPayload),
            success: function (result) {
                $('#btnCommit').prop('disabled', false).html('<i class="fa-solid fa-bolt"></i> <span>Commit to Database</span>');
                if (result.success) {
                    var msg = result.totalItemsCreated + ' items created + ' + result.totalLootRowsCreated + ' loot rows added';
                    if (result.regenReused) msg += ' · ' + result.regenReused + ' refreshed in place'
                        + (result.regenRemapped ? ', ' + result.regenRemapped + ' owned copies rerolled' : '')
                        + (result.regenRemoved ? ', ' + result.regenRemoved + ' removed' : '');
                    showToast(msg, 'success');
                    showPoolFeedback(result);
                    if (selectedCreature) selectCreature(selectedCreature.entry);
                } else {
                    showToast('Commit failed: ' + (result.error || ''), 'error');
                }
            },
            error: function () {
                $('#btnCommit').prop('disabled', false).html('<i class="fa-solid fa-bolt"></i> <span>Commit to Database</span>');
                showToast('Commit failed — server error', 'error');
            }
        });
    }

    function doBatchCommit() {
        if (!batchData) return;

        var creatures = [];
        batchData.creatures.forEach(function (c) {
            var sel = batchSelectedItems[c.creatureEntry];
            if (!sel) return;
            var items = [];
            c.items.forEach(function (it) {
                if (sel[it.itemEntry]) items.push(it.itemEntry);
            });
            if (items.length > 0) {
                creatures.push({ creatureEntry: c.creatureEntry, itemEntries: items });
            }
        });

        if (creatures.length === 0) {
            showToast('No items selected', 'error');
            return;
        }

        var s = computeBatchSelection();

        $('#btnCommit').prop('disabled', true).html('<i class="fa-solid fa-spinner fa-spin"></i> Committing ' + s.distinct + ' unique items (' + s.pairs + ' placements)...');

        $.ajax({
            url: '/Lootifier/BatchCommit',
            method: 'POST',
            contentType: 'application/json',
            data: JSON.stringify({ creatures: creatures, ruleset: collectRuleset(), regenerate: $('#lfRegenerate').is(':checked') }),
            success: function (result) {
                $('#btnCommit').prop('disabled', false).html('<i class="fa-solid fa-bolt"></i> <span>Commit to Database</span>');
                if (result.success) {
                    var msg = result.totalItemsCreated + ' items + ' + result.totalLootRowsCreated + ' loot rows across ' + result.creaturesProcessed + ' creatures';
                    if (result.pairsSkipped > 0) msg += ' (' + result.pairsSkipped + ' already-lootified pairs skipped)';
                    if (result.regenReused) msg += ' · ' + result.regenReused + ' refreshed in place'
                        + (result.regenRemapped ? ', ' + result.regenRemapped + ' owned copies rerolled' : '')
                        + (result.regenRemoved ? ', ' + result.regenRemoved + ' removed' : '');
                    showToast(msg, 'success');
                    // Weak bases can exhaust the distinct-stat-roll space, so fewer
                    // variants land than were asked for. Say so instead of hiding it.
                    if (result.variantsShort > 0) {
                        showToast(result.variantsShort + ' variant slot(s) across ' + result.itemsShort +
                            ' item(s) had no distinct stat roll left \u2014 those bases are too weak for that many tiers.', 'warning');
                    }
                    if (result.warnings && result.warnings.length > 0) {
                        showToast(result.warnings.length + ' pool(s) exceeded floor capacity — ' + result.warnings[0], 'warning');
                    }
                    showPoolFeedback(result, true);
                    $('#commitPanel').hide();
                } else {
                    showToast('Batch commit failed: ' + (result.error || ''), 'error');
                }
            },
            error: function () {
                $('#btnCommit').prop('disabled', false).html('<i class="fa-solid fa-bolt"></i> <span>Commit to Database</span>');
                showToast('Batch commit failed', 'error');
            }
        });
    }

    // Surface pool-health feedback from a commit response: a hard red toast when
    // a grouped loot pool exceeds 100% (members past the 100% mark can never
    // drop), plus the softer floor-capacity warnings on the single-commit path
    // (the batch path shows those itself, so it passes skipWarnings).
    function showPoolFeedback(result, skipWarnings) {
        if (!skipWarnings && result.warnings && result.warnings.length > 0) {
            showToast(result.warnings.length + ' pool warning(s) — ' + result.warnings[0], 'warning');
        }
        if (result.poolViolations && result.poolViolations.length > 0) {
            var v = result.poolViolations[0];
            showToast(result.poolViolations.length + ' loot pool(s) over 100% — e.g. ' + v.scope + ' #' + v.entry +
                ' group ' + v.groupId + ' at ' + v.total + '%. Items past 100% never drop; roll back and rebuild.', 'error');
        }
    }

    // ===================== ROLLBACK =====================

    function showRollbackModal(creatureEntry) {
        rollbackCreature = creatureEntry || 0;
        var desc = creatureEntry > 0
            ? 'This will remove all lootifier-generated items and loot entries for creature #' + creatureEntry + '.'
            : 'This will remove ALL lootifier-generated items and restore ALL modified loot tables.';
        $('#rollbackDesc').text(desc);
        new bootstrap.Modal($('#rollbackModal')[0]).show();
    }

    function doRollback() {
        bootstrap.Modal.getInstance($('#rollbackModal')[0]).hide();

        $.ajax({
            url: '/Lootifier/Rollback',
            method: 'POST',
            contentType: 'application/json',
            data: JSON.stringify({ creatureEntry: rollbackCreature }),
            success: function (result) {
                if (result.success) {
                    showToast('Rolled back: ' + result.itemsRemoved + ' items removed, ' + result.lootRowsFixed + ' loot entries restored', 'success');
                    if (selectedCreature) selectCreature(selectedCreature.entry);
                } else {
                    showToast('Rollback failed: ' + (result.error || ''), 'error');
                }
            },
            error: function () {
                showToast('Rollback failed', 'error');
            }
        });
    }

    // ===================== STATUS =====================

    function showStatus() {
        var modal = new bootstrap.Modal($('#statusModal')[0]);
        $('#statusBody').html('<div class="text-center p-4"><i class="fa-solid fa-spinner fa-spin"></i></div>');
        modal.show();

        $.getJSON('/Lootifier/Status', function (data) {
            if (!data.active) {
                $('#statusBody').html('<div class="lf-empty-state" style="padding:20px;"><i class="fa-solid fa-check-circle" style="color:var(--status-online);"></i>No lootifier data. Database is clean.</div>');
                return;
            }

            var h = '<div style="text-align:center;margin-bottom:16px;">' +
                '<div style="font-size:28px;font-weight:700;color:var(--accent);">' + data.totalItems + '</div>' +
                '<div style="font-size:11px;color:var(--text-muted);text-transform:uppercase;">Generated Items</div></div>';

            if (data.creatures && data.creatures.length > 0) {
                h += '<table class="table-clean"><thead><tr><th>Creature</th><th>Variants</th><th>Actions</th></tr></thead><tbody>';
                data.creatures.forEach(function (c) {
                    h += '<tr><td>Creature #' + c.creatureEntry + '</td>' +
                        '<td>' + c.variantCount + '</td>' +
                        '<td><button class="btn-micro lf-rollback-one" data-creature="' + c.creatureEntry + '">Rollback</button></td></tr>';
                });
                h += '</tbody></table>';
            }

            $('#statusBody').html(h);
        });
    }

    // ===================== HELPERS =====================

    function formatChance(val) {
        if (val === 0) return '0';
        if (val >= 10) return val.toFixed(1);
        if (val >= 1) return val.toFixed(2);
        return val.toFixed(3);
    }

    function esc(text) {
        if (text == null) return '';
        var div = document.createElement('div');
        div.textContent = String(text);
        return div.innerHTML;
    }

    // Toasts go into one fixed stack rather than each pinning itself to the same
    // bottom-right corner — a commit that also returns pool warnings fires two, and
    // they used to land on top of each other and make both unreadable.
    function toastStack() {
        var $s = $('#lfToastStack');
        if (!$s.length) $s = $('<div id="lfToastStack" class="lf-toast-stack"></div>').appendTo('body');
        return $s;
    }

    function showToast(msg, type) {
        var $stack = toastStack();
        // Newest first (column-reverse), and never more than 3 on screen.
        var el = $('<div class="lf-toast ' + (type || '') + '">' + esc(msg) + '</div>');
        $stack.append(el);
        $stack.children('.lf-toast').slice(0, -3).remove();
        setTimeout(function () {
            el.fadeOut(300, function () {
                el.remove();
                if (!$stack.children().length) $stack.remove();
            });
        }, 4000);
    }

    function updateGenerateButton() {
        var count = Object.keys(selectedItems).filter(function (k) { return selectedItems[k]; }).length;
        $('#btnGenerate').prop('disabled', count === 0);
    }

    function populateLegendaryPicker() {
        var sel = $('#rsLegendaryItem');
        sel.html('<option value="0">Random (any selected item)</option>');

        if (!lootTreeData) return;

        var items = [];
        if (lootTreeData.directItems) items = items.concat(lootTreeData.directItems);
        if (lootTreeData.referenceGroups) {
            lootTreeData.referenceGroups.forEach(function (rg) {
                items = items.concat(rg.items);
            });
        }

        items.forEach(function (item) {
            if (item.isGenerated === true) return; // lootifier output, not a base
            if (item.totalStats > 0 || item.hasSpellEffects) {
                var qualName = QUALITY_NAMES[item.quality] || '';
                sel.append('<option value="' + item.itemEntry + '">' + esc(item.itemName) + ' (' + qualName + ')</option>');
            }
        });
    }

    // ===================== EVENTS =====================

    // Search
    $('#btnSearchCreature').on('click', searchCreature);
    $('#creatureSearch').on('keydown', function (e) { if (e.key === 'Enter') searchCreature(); });

    // Select creature from results
    $(document).on('click', '.lf-search-item', function () {
        selectCreature(parseInt($(this).data('entry')));
    });

    // Toggle item in single-source loot tree
    $(document).on('click', '.lf-loot-row', function (e) {
        if ($(this).hasClass('no-stats')) return;
        if ($(e.target).is('input')) return;

        var entry = parseInt($(this).data('item'));
        var check = $(this).find('.lf-loot-check');
        if (check.length === 0) return;

        var isSelected = !selectedItems[entry];
        selectedItems[entry] = isSelected;
        check.prop('checked', isSelected);
        $(this).toggleClass('selected', isSelected);
        updateGenerateButton();
        previewData = null;
        $('#commitPanel').hide();
    });

    $(document).on('change', '.lf-loot-check', function (e) {
        e.stopPropagation();
        var row = $(this).closest('.lf-loot-row');
        var entry = parseInt(row.data('item'));
        selectedItems[entry] = $(this).is(':checked');
        row.toggleClass('selected', selectedItems[entry]);
        updateGenerateButton();
        previewData = null;
        $('#commitPanel').hide();
    });

    // Batch: toggle chips
    $(document).on('click', '#batchPanel .toggle-chip', function () {
        $(this).toggleClass('active');
    });

    $(document).on('click', '#batchPanel .instance-chip', function () {
        $(this).toggleClass('active');
    });

    // Batch: creature-level checkbox
    $(document).on('change', '.lf-batch-creature-check', function () {
        var ce = parseInt($(this).data('creature'));
        var checked = $(this).is(':checked');
        $(this).closest('.lf-batch-creature').find('.lf-batch-item-check[data-creature="' + ce + '"]').prop('checked', checked);
        if (!batchSelectedItems[ce]) batchSelectedItems[ce] = {};
        if (checked && batchData) {
            var c = batchData.creatures.find(function (cr) { return cr.creatureEntry === ce; });
            if (c) c.items.forEach(function (it) { batchSelectedItems[ce][it.itemEntry] = true; });
        } else {
            batchSelectedItems[ce] = {};
        }
        updateBatchStats();
    });

    // Live-refresh the projected counts when the variants-per-item field changes
    // (batch shared input or the single-mode input), so NEW ITEMS stays accurate.
    $(document).on('input change', '.lf-rs-shared[data-target="rsVariantsPerItem"], #rsVariantsPerItem', function () {
        updateBatchStats();
    });

    // Batch: item-level checkbox
    $(document).on('change', '.lf-batch-item-check', function () {
        var ce = parseInt($(this).data('creature'));
        var ie = parseInt($(this).data('item'));
        if (!batchSelectedItems[ce]) batchSelectedItems[ce] = {};
        batchSelectedItems[ce][ie] = $(this).is(':checked');
        updateBatchStats();
    });

    // Generate (single)
    $('#btnGenerate').on('click', generatePreview);

    // Batch scan
    $('#btnBatchScan').on('click', batchScan);

    // Batch sample preview
    $('#btnBatchSample').on('click', batchSamplePreview);

    // Commit
    $('#btnCommit').on('click', doCommit);

    // Rollback
    $('#btnRollbackAll').on('click', function () { showRollbackModal(0); });
    $('#btnConfirmRollback').on('click', doRollback);
    $(document).on('click', '.lf-rollback-one', function () {
        showRollbackModal(parseInt($(this).data('creature')));
    });

    // Status
    $('#btnViewStatus').on('click', showStatus);

    // Legendary toggle show/hide
    // The legendary row is the single source of truth for everything that fits on
    // the ladder: boost, drop %, gold, dps and the on/off switch. The old duplicate
    // fields (Legendary Gold/DPS in Value Tuning, Effective Drop % and the checkbox
    // in the Boss Legendary block) are now hidden inputs that simply follow the row,
    // so collectRuleset and any existing handler keep working unchanged.
    function pushLegendaryToHidden($row) {
        function push(sel, id) {
            var v = $row.find(sel).val();
            if (v === undefined) return;
            $('#' + id).val(v);
            $('.lf-rs-shared[data-target="' + id + '"]').val(v);
        }
        push('.lf-leg-gold', 'rsLegGold');
        push('.lf-leg-dps', 'rsLegDps');
        var d = $row.find('.lf-leg-drop').val();
        if (d !== undefined) { $('#rsLegendaryDropPct').val(d); $('.lf-batch-leg-drop').val(d); }
        var on = $row.find('.lf-leg-on').is(':checked');
        $('#rsLegendaryToggle').prop('checked', on);
        $('.lf-batch-legendary-toggle').prop('checked', on);
    }

    function syncLegendaryRow() {
        var gold = $('#rsLegGold').val();
        var dps = $('#rsLegDps').val();
        var on = $('#rsLegendaryToggle').is(':checked') || $('.lf-batch-legendary-toggle').is(':checked');
        $('.lf-leg-gold').each(function () { if ($(this).val() !== gold) $(this).val(gold); });
        $('.lf-leg-dps').each(function () { if ($(this).val() !== dps) $(this).val(dps); });
        $('.lf-leg-on').prop('checked', on);
        // Dim when off, but never disable: the operator must be able to set the
        // numbers up before switching it on.
        $('.lf-tier-legendary').toggleClass('lf-tier-off', !on);
    }

    $(document).on('input change', '.lf-tier-legendary input', function () {
        pushLegendaryToHidden($(this).closest('.lf-tier-legendary'));
        syncLegendaryRow();
    });
    $(document).on('change', '#rsLegendaryToggle, .lf-batch-legendary-toggle', syncLegendaryRow);

    // Suffix block visibility follows the legendary switch wherever it is set.
    $(document).on('change', '#rsLegendaryToggle, .lf-batch-legendary-toggle, .lf-leg-on', function () {
        var on = $('#rsLegendaryToggle').is(':checked') || $('.lf-batch-legendary-toggle').is(':checked');
        $('#legendaryConfig').toggle(on);
        $('.lf-batch-legendary-config').toggle(on);
    });

    // Reset ruleset
    $('#btnResetRuleset').on('click', function () {
        $('#rsBudgetCeiling').val(35);
        $('#rsVariantsPerItem').val(9);
        $('#rsAllowNewAffixes').prop('checked', true);
        $('#rsMaxAffixChange').val(1);
        tierState = null;       // rebuild the tier bands from meta defaults
        legendaryBand = null;   // ...and the legendary band with them
        renderNamingTiers();
    });

    // ── Drop-chance strategy control (self-bootstrapping) ──
    // Preserve = split existing loot (dungeon-safe, current behavior).
    // Additive = independent tunable-chance pool that ADDS drops without dilution.
    $(document).on('change', '#lfDropStrategy', function () {
        $('#lfPoolDropWrap').css('display', $(this).is(':checked') ? 'inline-flex' : 'none');
    });

    function initDropStrategyUI() {
        if ($('#lfDropStrategyPanel').length) return;
        if (!$('#lfDropStrategyStyles').length) {
            $('head').append('<style id="lfDropStrategyStyles">' +
                '.lf-drop-panel{margin:10px 0;padding:10px 12px;border:1px solid rgba(128,128,128,.28);border-radius:8px;background:rgba(128,128,128,.06);font-size:12px;}' +
                '.lf-drop-panel .lf-drop-row{display:flex;align-items:center;gap:8px;flex-wrap:wrap;}' +
                '.lf-drop-panel label{display:inline-flex;align-items:center;gap:6px;cursor:pointer;}' +
                '.lf-drop-panel input[type=number]{width:64px;padding:3px 6px;border-radius:5px;border:1px solid rgba(128,128,128,.35);background:rgba(0,0,0,.18);color:inherit;}' +
                '.lf-drop-hint{opacity:.7;font-size:11px;margin-top:6px;line-height:1.4;}' +
                '#lfPoolDropWrap{display:none;align-items:center;gap:6px;}' +
                '</style>');
        }
        var html =
            '<div id="lfDropStrategyPanel" class="lf-drop-panel">' +
            '<div class="lf-drop-row">' +
            '<label title="Preserve = split the existing loot share (dungeon-safe). Additive = add an independent tunable-chance pool without diluting existing drops.">' +
            '<input type="checkbox" id="lfDropStrategy" /> Additive drop pool' +
            '</label>' +
            '<span id="lfPoolDropWrap">' +
            '<span>· drops</span>' +
            '<input type="number" id="lfPoolDropPct" min="0" max="100" step="5" value="100" />' +
            '<span>% of the time</span>' +
            '</span>' +
            '</div>' +
            '<div class="lf-drop-hint">Additive mints a shared pool (creating a loot table if the mob has none), moves the base item in at 0.5%, and attaches it as an independent roll — existing loot is untouched. Built once per creature; roll back to rebuild.</div>' +
            '</div>';
        var $anchor = $('#rsBudgetCeiling').closest('.lf-ruleset, .lf-panel, .lf-settings, fieldset, section');
        if ($anchor.length) $(html).insertAfter($anchor.first());
        else if ($('#lootTree').length) $(html).insertBefore($('#lootTree'));
        else $('body').prepend(html);
    }
    $(initDropStrategyUI);

});