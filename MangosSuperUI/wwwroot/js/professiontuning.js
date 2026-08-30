// Profession Tuning — page logic.
// Reduce reagent counts across a whole profession's recipes by a global %.
// Every recipe starts toggled ON; untoggle to exclude before applying — "exactly
// like the lootifier". Applies server-side to spell_template (build-resolved per
// recipe) and folds the client reagent counts into patch-3's Spell.dbc so the tradeskill
// UI's Create gate and tooltip match the server. Reductions are computed off a
// stored original, so re-applying never compounds; rollback restores originals.
(function () {
    'use strict';

    var meta = null;
    var selectedProf = null;
    var selectedProfName = '';
    var recipes = [];   // current profession's recipe list from the server

    function esc(s) {
        return String(s == null ? '' : s)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;')
            .replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    }

    // WoW item quality → CSS class. Values are defined in the view so they can
    // be tuned per-theme in one place.
    function qClass(q) {
        var n = parseInt(q);
        if (isNaN(n)) n = 1;
        if (n < 0) n = 0; if (n > 8) n = 8;
        return 'q' + n;
    }

    function showToast(msg, type) {
        var el = $('<div class="pt-toast ' + (type || '') + '">' + esc(msg) + '</div>');
        $('body').append(el);
        setTimeout(function () { el.fadeOut(300, function () { el.remove(); }); }, 5000);
    }

    // ===================== META =====================
    $.getJSON('/ProfessionTuning/Meta', function (m) {
        meta = m;
        if (m && m.defaultPct != null) $('#ptPct').val(m.defaultPct);
        updateApplyLabel();
    });

    // ===================== PROFESSIONS =====================
    function loadProfessions() {
        $.getJSON('/ProfessionTuning/Professions', function (res) {
            var h = '<div class="pt-prof-grid">';
            (res.professions || []).forEach(function (p) {
                var tuned = p.tunedRecipes > 0
                    ? '<div class="pt-done-tag">' + p.tunedRecipes + ' tuned</div>' : '';
                h += '<div class="pt-prof-card' + (selectedProf === p.id ? ' active' : '') + '" data-prof="' + p.id + '" data-name="' + esc(p.name) + '">' +
                    '<div class="pt-prof-name">' + esc(p.name) + '</div>' +
                    '<div class="text-muted" style="font-size:11px;">' + p.totalRecipes + ' recipe' + (p.totalRecipes === 1 ? '' : 's') + '</div>' +
                    tuned +
                    '</div>';
            });
            h += '</div>';
            $('#profList').html(h);
            // profList is re-rendered after every apply/rollback, which wipes the
            // active highlight — restore it, and keep the collapsed label current.
            if (selectedProf) {
                $('.pt-prof-card[data-prof="' + selectedProf + '"]').addClass('active');
                if ($('#profCard').hasClass('collapsed')) $('#profCurrent').text(selectedProfName);
            }
        });
    }

    $(document).on('click', '.pt-prof-card', function () {
        selectedProf = parseInt($(this).data('prof'));
        selectedProfName = $(this).data('name');
        $('.pt-prof-card').removeClass('active');
        $(this).addClass('active');
        $('#tunePanel').show();
        $('#tunePlaceholder').hide();
        // Collapse the picker so the recipe list gets the full width. The chosen
        // profession stays visible in the collapsed bar, so nothing is lost.
        setPickerCollapsed(true);
        loadRecipes(selectedProf);
    });

    // ===================== PICKER COLLAPSE =====================
    function setPickerCollapsed(collapsed) {
        $('#profCard').toggleClass('collapsed', collapsed);
        $('#profCurrent').text(collapsed && selectedProfName ? selectedProfName : '');
    }

    $('#profToggle').on('click', function () {
        setPickerCollapsed(!$('#profCard').hasClass('collapsed'));
    });

    // keyboard parity — the header is role="button"
    $('#profToggle').on('keydown', function (e) {
        if (e.key === 'Enter' || e.key === ' ') {
            e.preventDefault();
            setPickerCollapsed(!$('#profCard').hasClass('collapsed'));
        }
    });

    // ===================== RECIPES =====================
    // orig = the pristine count Apply always computes from (the snapshot if this
    // recipe is already tuned, otherwise what's in the DB now).
    // current = what the DB holds right now. The preview swaps between them.
    function reagentChip(r) {
        var orig = (r.origCount != null) ? r.origCount : r.count;
        var img = r.iconPath ? '<img src="' + esc(r.iconPath) + '" />' : '';
        return '<span class="pt-reagent" title="' + esc(r.name) + '"' +
            ' data-orig="' + orig + '" data-current="' + r.count + '">' + img +
            '<span class="pt-reagent-name ' + qClass(r.quality) + '">' + esc(r.name) + '</span>' +
            '&nbsp;\u00d7<span class="pt-count"></span></span>';
    }

    function recipeRow(rc) {
        var tunedBadge = rc.currentPct > 0
            ? '<span class="pt-tuned-badge">\u2212' + rc.currentPct + '%</span>' : '';
        var restoreBtn = rc.currentPct > 0
            ? '<button class="btn-micro pt-restore" data-spell="' + rc.spellEntry + '" title="Restore this recipe to original mats"><i class="fa-solid fa-rotate-left"></i></button>'
            : '';
        var icon = rc.iconPath ? '<img src="' + esc(rc.iconPath) + '" class="pt-out-icon" />' : '<span class="pt-out-icon pt-out-none"></span>';
        var reagents = (rc.reagents || []).map(reagentChip).join('');
        return '<div class="pt-recipe-row" data-spell="' + rc.spellEntry + '">' +
            '<input type="checkbox" class="pt-check"' + (rc.sel === false ? '' : ' checked') + ' />' +
            icon +
            '<span class="pt-recipe-name ' + qClass(rc.quality) + '">' + esc(rc.name) +
            '<span class="pt-spell-id text-muted">#' + rc.spellEntry + (rc.effBuild ? ' \u00b7 b' + rc.effBuild : '') + '</span>' +
            '</span>' +
            (rc.minRank
                ? '<span class="pt-rank" title="Required skill level">' + rc.minRank + '</span>'
                : '<span class="pt-rank"></span>') +
            tunedBadge +
            '<span class="pt-reagents">' + reagents + '</span>' +
            restoreBtn +
            '</div>';
    }

    function loadRecipes(id) {
        $('#recipeList').html('<div class="text-muted" style="padding:16px;">Loading recipes\u2026</div>');
        $.getJSON('/ProfessionTuning/ProfessionRecipes?skillLineId=' + id, function (res) {
            recipes = res.recipes || [];
            // Selection lives on the model, NOT in the DOM — re-sorting re-renders
            // the list, and DOM-held checkboxes would be wiped every time.
            recipes.forEach(function (rc) { rc.sel = true; });
            $('#tuneProfName').text(res.name || selectedProfName);
            if (!recipes.length) {
                $('#recipeList').html('<div class="text-muted" style="padding:16px;">No reagent-consuming recipes found for this profession.</div>');
                updateApplyLabel();
                return;
            }
            renderList();
        });
    }

    // Mirrors ProfessionTuningStore.ApplyReduction exactly:
    //   max(1, round(orig * (1 - pct/100)))
    // JS Math.round is round-half-up which matches C#'s MidpointRounding.AwayFromZero
    // for positive numbers, so preview and commit can't disagree.
    function previewCount(orig, pct) {
        return Math.max(1, Math.round(orig * (1 - pct / 100)));
    }

    // Repaint every reagent count to show what Apply WOULD produce. Checked rows
    // preview the reduction; unchecked rows fall back to what's actually stored.
    function refreshPreview() {
        var pct = parseFloat($('#ptPct').val()) || 0;
        var live = pct > 0 && pct <= 90;

        $('#recipeList .pt-recipe-row').each(function () {
            var $row = $(this);
            var on = $row.find('.pt-check').is(':checked');
            $row.toggleClass('pt-off', !on);

            $row.find('.pt-reagent').each(function () {
                var $chip = $(this);
                var orig = parseInt($chip.attr('data-orig'), 10) || 0;
                var cur = parseInt($chip.attr('data-current'), 10) || 0;
                var $out = $chip.find('.pt-count');

                if (on && live) {
                    var next = previewCount(orig, pct);
                    $out.html(next !== orig
                        ? '<s class="pt-was">' + orig + '</s>&nbsp;<strong class="pt-new">' + next + '</strong>'
                        : '<strong>' + orig + '</strong>');
                } else {
                    // not previewing — show the stored value, marking it if already tuned
                    $out.html(cur !== orig
                        ? '<s class="pt-was">' + orig + '</s>&nbsp;<strong class="pt-applied">' + cur + '</strong>'
                        : '<strong>' + cur + '</strong>');
                }
            });
        });
    }

    // ===================== SORTING =====================
    // Client-side: the whole profession is already loaded, so re-sorting is instant
    // and costs no round trip. Every comparator falls back to name so the order is
    // stable and repeatable rather than dependent on the server's row order.
    var SORTS = {
        'name':        function (a, b) { return cmpName(a, b); },
        'name-desc':   function (a, b) { return -cmpName(a, b); },
        'level':       function (a, b) { return (a.minRank - b.minRank) || cmpName(a, b); },
        'level-desc':  function (a, b) { return (b.minRank - a.minRank) || cmpName(a, b); },
        'rarity-desc': function (a, b) { return (b.quality - a.quality) || cmpName(a, b); },
        'rarity':      function (a, b) { return (a.quality - b.quality) || cmpName(a, b); },
        'tuned':       function (a, b) { return (b.currentPct - a.currentPct) || cmpName(a, b); }
    };

    function cmpName(a, b) {
        return String(a.name || '').localeCompare(String(b.name || ''));
    }

    function sortedRecipes() {
        var key = $('#ptSort').val() || 'name';
        var fn = SORTS[key] || SORTS.name;
        return recipes.slice().sort(fn);
    }

    function renderList() {
        $('#recipeList').html(sortedRecipes().map(recipeRow).join(''));
        updateApplyLabel();
    }

    function checkedSpells() {
        var out = [];
        recipes.forEach(function (rc) { if (rc.sel !== false) out.push(rc.spellEntry); });
        return out;
    }

    function setAllSelected(v) {
        recipes.forEach(function (rc) { rc.sel = v; });
        $('#recipeList .pt-check').prop('checked', v);
        updateApplyLabel();
    }

    function updateApplyLabel() {
        refreshPreview();
        var n = checkedSpells().length;
        var pct = parseFloat($('#ptPct').val()) || 0;
        $('#btnApply')
            .html('<i class="fa-solid fa-wand-magic-sparkles"></i> Apply \u2212' + pct + '% to ' + n + ' recipe' + (n === 1 ? '' : 's'))
            .prop('disabled', n === 0 || pct <= 0 || pct > 90);
        $('#ptCheckedCount').text(n + ' of ' + recipes.length + ' selected');
    }

    $(document).on('change', '.pt-check', function () {
        var spell = parseInt($(this).closest('.pt-recipe-row').data('spell'), 10);
        var on = $(this).is(':checked');
        for (var i = 0; i < recipes.length; i++) {
            if (recipes[i].spellEntry === spell) { recipes[i].sel = on; break; }
        }
        updateApplyLabel();
    });
    $('#ptPct').on('input', updateApplyLabel);
    $('#ptSort').on('change', renderList);
    $('#btnSelectAll').on('click', function () { setAllSelected(true); });
    $('#btnSelectNone').on('click', function () { setAllSelected(false); });

    // ===================== CLIENT PATCH REBUILD =====================
    // The server reads reagents from spell_template, but the 1.12.1 client reads
    // them from Spell.dbc \u2014 for the tradeskill tooltip AND for the client-side
    // "Create" gate. Without this the client keeps blocking at the old count.
    // Spell.dbc ships inside patch-3 (the Spell Creator's patch), so tuning rides
    // that existing rebuild instead of shipping a patch of its own: a higher
    // numbered patch would override patch-3 wholesale and silently drop every
    // custom spell in-client.
    function rebuildClientPatch() {
        $('#ptRebuildState').text('Rebuilding patch-3.MPQ\u2026').show();
        $.ajax({
            url: '/Patch/RebuildClientPatch', method: 'POST',
            contentType: 'application/json', data: '{}',
            success: function (res) {
                if (res && res.success === false) {
                    showToast('Client patch rebuild failed: ' + (res.error || 'unknown'), 'error');
                    $('#ptRebuildState').text('Client patch rebuild FAILED');
                    return;
                }
                showToast('patch-3.MPQ rebuilt \u2014 deploy it to the client Data folder', 'success');
                $('#ptRebuildState').text('patch-3.MPQ rebuilt');
            },
            error: function () {
                showToast('Client patch rebuild request failed \u2014 mats changed server-side only', 'error');
                $('#ptRebuildState').text('Client patch rebuild FAILED');
            }
        });
    }

    // ===================== APPLY =====================
    $('#btnApply').on('click', function () {
        if (!selectedProf) return;
        var spells = checkedSpells();
        if (!spells.length) return;
        var pct = parseFloat($('#ptPct').val()) || 0;
        if (pct <= 0 || pct > 90) { showToast('Enter a reduction between 1 and 90%.', 'error'); return; }

        var btn = $(this).prop('disabled', true)
            .html('<i class="fa-solid fa-spinner fa-spin"></i> Applying + rebuilding patch-3.MPQ\u2026');
        $.ajax({
            url: '/ProfessionTuning/Apply', method: 'POST', contentType: 'application/json',
            data: JSON.stringify({ skillLineId: selectedProf, pct: pct, spellEntries: spells }),
            success: function (res) {
                if (!res.success) { showToast(res.error || 'Apply failed', 'error'); return; }
                showToast(res.recipesTuned + ' recipes \u2212' + pct + '% in spell_template', 'success');
                loadRecipes(selectedProf);
                loadProfessions();
                if (res.needsClientRebuild) rebuildClientPatch();
            },
            error: function () { showToast('Apply request failed (large professions can take a moment server-side)', 'error'); },
            complete: function () { btn.prop('disabled', false); updateApplyLabel(); }
        });
    });

    // ===================== RESTORE ONE =====================
    $(document).on('click', '.pt-restore', function (e) {
        e.stopPropagation();
        var spell = parseInt($(this).data('spell'));
        $.ajax({
            url: '/ProfessionTuning/RestoreRecipe', method: 'POST', contentType: 'application/json',
            data: JSON.stringify({ spellEntry: spell }),
            success: function (res) {
                if (!res.success) { showToast(res.error || 'Restore failed', 'error'); return; }
                showToast('Recipe restored to original mats', 'success');
                if (selectedProf) loadRecipes(selectedProf);
                loadProfessions();
                if (res.needsClientRebuild) rebuildClientPatch();
            },
            error: function () { showToast('Restore request failed', 'error'); }
        });
    });

    // ===================== ROLLBACK ALL =====================
    $('#btnRollbackAll').on('click', function () {
        if (!confirm('Restore EVERY tuned recipe to its original reagent counts and rebuild patch-3.MPQ? This clears all profession tuning.')) return;
        var btn = $(this).prop('disabled', true).html('<i class="fa-solid fa-spinner fa-spin"></i> Rolling back\u2026');
        $.ajax({
            url: '/ProfessionTuning/RollbackAll', method: 'POST', contentType: 'application/json', data: '{}',
            success: function (res) {
                if (!res.success) { showToast(res.error || 'Rollback failed', 'error'); return; }
                showToast(res.restored + ' recipes restored to original', 'success');
                if (selectedProf) loadRecipes(selectedProf);
                loadProfessions();
                if (res.needsClientRebuild) rebuildClientPatch();
            },
            error: function () { showToast('Rollback request failed', 'error'); },
            complete: function () { btn.prop('disabled', false).html('<i class="fa-solid fa-trash-can"></i> Rollback All'); }
        });
    });

    // ===================== STATUS TAB =====================
    function loadStatus() {
        $.getJSON('/ProfessionTuning/Status', function (res) {
            if (!res.tuned || !res.tuned.length) {
                $('#statusList').html('<div class="text-muted" style="padding:16px;">No recipes are currently tuned.</div>');
                return;
            }
            var h = '<table class="pt-status-table"><thead><tr>' +
                '<th>Spell</th><th>Recipe</th><th>Profession</th><th>Reduction</th></tr></thead><tbody>';
            res.tuned.forEach(function (t) {
                h += '<tr><td class="text-muted">#' + t.spellEntry + '</td><td>' + esc(t.name) +
                    '</td><td>' + esc(t.profession) + '</td><td>\u2212' + t.pct + '%</td></tr>';
            });
            h += '</tbody></table>';
            $('#statusList').html(h);
        });
    }

    // ===================== TABS =====================
    $(document).on('click', '.pt-tab', function () {
        var tab = $(this).data('tab');
        $('.pt-tab').removeClass('active');
        $(this).addClass('active');
        $('#tab-tune').toggle(tab === 'tune');
        $('#tab-status').toggle(tab === 'status');
        if (tab === 'status') loadStatus();
        if (tab === 'tune') loadProfessions();
    });

    // ===================== INIT =====================
    loadProfessions();
})();
