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

    // Effective display quality of a variant, mirroring the server's promotion
    // in InsertVariantItemFast: budget >= 90 promotes to Epic (4) when the base
    // is below Epic. Otherwise the variant keeps the base item's quality.
    function variantQuality(baseQuality, budgetPct) {
        var bq = baseQuality == null ? 1 : baseQuality;
        if (budgetPct >= 90 && bq < 4) return 4;
        return bq;
    }

    function collectRuleset() {
        return {
            budgetCeilingPct: parseFloat($('#qlBudgetCeiling').val()) || 35,
            variantsPerItem: parseInt($('#qlVariants').val()) || 10,
            allowNewAffixes: $('#qlAllowNew').is(':checked'),
            maxAffixCountChange: parseInt($('#qlMaxAffix').val()) || 1,
            generateLegendary: $('#qlLegendary').is(':checked')
        };
    }

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
        var v = parseInt($('#qlVariants').val()) || 10;
        $('#qlCommitBar').show();
        $('#qlCommitSummary').text(n + ' reward item(s) × ' + v + ' variants = ~' + (n * v) + ' new items for "' + selectedQuest.title + '".');
        $('#qlCommitBtn').html('<i class="fa-solid fa-bolt"></i> Generate Variants');
    }

    $('#qlVariants').on('input', updateCommitBar);

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
                var vq = v.isLegendary ? 5 : variantQuality(baseQ, v.budgetPct);
                var budgetLabel = v.isLegendary ? 'LEG' : (v.budgetPct + '%');
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