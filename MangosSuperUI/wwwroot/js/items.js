// MangosSuperUI — Items Browser + Editor JS

$(function () {

    var currentPage = 1;
    var totalPages = 1;
    var currentPageSize = parseInt(localStorage.getItem('msui_items_pageSize')) || 50;
    var currentIcons = {};
    var currentDetailEntry = null;
    var currentDetailItem = null;

    // Edit state
    var editMode = false;
    var editEntry = null;
    var editIsClone = false;
    var editIsBaseGame = false;
    var editSourceEntry = null;
    var editOriginalRow = null; // Full DB row — base for collectFormData()

    // Icon picker state
    var iconPickerPage = 1;
    var iconPickerQuery = '';
    var iconPickerCallback = null;

    var CUSTOM_RANGE_START = 900000;

    // ── Segmented retexture staging (preview-before-save) ──────────────
    // A selected variant lives here, fully temporary, until Save commits it.
    //   stagedRetexture = { displayId, mpqPath, filename, itemName,
    //                       unitColors, unitLightness, glbUrl, attachments,
    //                       pngUrl, name }
    // glbUrl is the primary temp preview GLB; paired slots such as shoulders
    // also carry their authored side URLs in attachments. No DB row or patch
    // exists until Save commits the staged PNG.
    var stagedRetexture = null;
    var stagedPreviewGlbs = [];   // temp GLB urls to clean up on modal close

    // When a retexture entry point is clicked from the DETAIL view (e.g. the
    // header "Retexture variations" button), the retexture panel lives inside
    // the hidden #colEdit column and can't show. We stash the request here,
    // enter edit mode, and openRetexturePanel runs again once #colEdit is
    // visible (consumed at the end of showEditPanel).
    var pendingRetexOpen = null;

    function hasUnsavedRetexture() {
        return stagedRetexture != null && !stagedRetexture.committed;
    }

    // Return every temp GLB referenced by a preview response/staging object.
    // For shoulders `glbUrl` is also attachments.shoulderLeft, so de-duplicate
    // before tracking or deleting it.
    function previewGlbUrls(preview) {
        if (!preview) return [];
        var urls = [];
        if (preview.glbUrl) urls.push(preview.glbUrl);
        var attachments = preview.attachments || {};
        Object.keys(attachments).forEach(function (key) {
            if (attachments[key]) urls.push(attachments[key]);
        });
        return urls.filter(function (url, idx) { return urls.indexOf(url) === idx; });
    }

    // Guard against losing an unsaved retexture on tab close / navigation.
    // (The panel's own back/cancel routes through closeRetexturePanel, which
    // warns separately; this catches the browser-level navigate-away case.)
    window.addEventListener('beforeunload', function (e) {
        if (hasUnsavedRetexture()) {
            e.preventDefault();
            e.returnValue = '';   // triggers the browser's generic confirm
            return '';
        }
    });


    // ===================== BASELINE INTEGRATION =====================

    BaselineSystem.checkStatus(function (status) {
        BaselineSystem.renderWarningBanner('#baselineWarning');
    });

    $(document).on('baseline:initialized', function () {
        if (currentDetailEntry) {
            loadItemChangelog(currentDetailEntry);
        }
    });

    // ===================== CONSTANTS =====================

    // Show the download button whenever a retexture patch can be produced.
    // Backed by the DB (durable) via /Items/PatchStatus, not the wwwroot file
    // (which a redeploy wipes); the download itself regenerates the file on
    // demand if it's missing, so the button never points at a dead link.
    function checkPatchMAvailable() {
        $.ajax({
            url: '/Items/PatchStatus',
            type: 'GET',
            success: function (data) {
                if (data && data.available) { $('#btnDownloadPatchM, #btnDownloadPatchTop').show(); }
                else { $('#btnDownloadPatchM, #btnDownloadPatchTop').hide(); }
            },
            error: function () { $('#btnDownloadPatchM, #btnDownloadPatchTop').hide(); }
        });
    }
    checkPatchMAvailable();

    var QUALITY_NAMES = ['Poor', 'Common', 'Uncommon', 'Rare', 'Epic', 'Legendary', 'Artifact'];
    var QUALITY_COLORS = ['#9d9d9d', 'inherit', '#1eff00', '#0070dd', '#a335ee', '#ff8000', '#e6cc80'];

    var CLASS_NAMES = {
        0: 'Consumable', 1: 'Container', 2: 'Weapon', 4: 'Armor',
        5: 'Reagent', 7: 'Trade Goods', 9: 'Recipe', 12: 'Quest', 15: 'Misc'
    };

    var SLOT_NAMES = {
        0: '', 1: 'Head', 2: 'Neck', 3: 'Shoulder', 4: 'Shirt', 5: 'Chest',
        6: 'Waist', 7: 'Legs', 8: 'Feet', 9: 'Wrist', 10: 'Hands', 11: 'Finger',
        12: 'Trinket', 13: 'One-Hand', 14: 'Shield', 15: 'Ranged', 16: 'Back',
        17: 'Two-Hand', 18: 'Bag', 19: 'Tabard', 20: 'Robe', 21: 'Main Hand',
        22: 'Off Hand', 23: 'Held In Off-Hand', 24: 'Ammo', 25: 'Thrown', 26: 'Ranged'
    };

    var BONDING_NAMES = { 0: 'No Binding', 1: 'Binds on Pickup', 2: 'Binds on Equip', 3: 'Binds on Use', 4: 'Quest Item' };

    var STAT_TYPES = {
        0: 'Mana', 1: 'Health', 3: 'Agility', 4: 'Strength', 5: 'Intellect',
        6: 'Spirit', 7: 'Stamina', 12: 'Defense', 13: 'Dodge', 14: 'Parry',
        15: 'Block', 31: 'Hit', 32: 'Crit', 35: 'Resilience', 36: 'Haste'
    };

    var TRIGGER_NAMES = {
        0: 'Use (right-click)',
        1: 'On Equip (passive)',
        2: 'Chance on Hit (proc)',
        5: 'Use (no delay)',
        6: 'Learn Spell (recipe)'
    };

    var DMG_TYPE_NAMES = {
        0: 'Physical', 1: 'Holy', 2: 'Fire', 3: 'Nature', 4: 'Frost', 5: 'Shadow', 6: 'Arcane'
    };

    var WOW_CLASSES = [
        { bit: 0, name: 'Warrior' }, { bit: 1, name: 'Paladin' }, { bit: 2, name: 'Hunter' },
        { bit: 3, name: 'Rogue' }, { bit: 4, name: 'Priest' }, { bit: 5, name: 'Shaman' },
        { bit: 6, name: 'Mage' }, { bit: 7, name: 'Warlock' }, { bit: 8, name: 'Druid' }
    ];

    var WOW_RACES = [
        { bit: 0, name: 'Human' }, { bit: 1, name: 'Orc' }, { bit: 2, name: 'Dwarf' },
        { bit: 3, name: 'Night Elf' }, { bit: 4, name: 'Undead' }, { bit: 5, name: 'Tauren' },
        { bit: 6, name: 'Gnome' }, { bit: 7, name: 'Troll' }
    ];

    // ===================== SEARCH =====================

    // ── Filter state ────────────────────────────────────────────────────────
    //
    // Every control here maps to a parameter /Items/Search understands, so all
    // filtering is server-side and correct across pagination. Filter state is
    // persisted, because losing your filters on every reload is the single most
    // annoying thing a browse page can do.

    var FILTER_KEY = 'msui_items_filters';

    // Combined weapon-type / armor-material dropdown. Value is "class:subclass",
    // so one pick implies both — no class -> subclass cascade to fight with, and
    // "Dagger" or "Plate" is a single choice rather than two.
    var TYPE_OPTIONS = [
        ['2:0', 'Axe (One-Hand)'], ['2:1', 'Axe (Two-Hand)'], ['2:2', 'Bow'],
        ['2:3', 'Gun'], ['2:4', 'Mace (One-Hand)'], ['2:5', 'Mace (Two-Hand)'],
        ['2:6', 'Polearm'], ['2:7', 'Sword (One-Hand)'], ['2:8', 'Sword (Two-Hand)'],
        ['2:10', 'Staff'], ['2:13', 'Fist Weapon'], ['2:14', 'Miscellaneous'],
        ['2:15', 'Dagger'], ['2:16', 'Thrown'], ['2:17', 'Spear'],
        ['2:18', 'Crossbow'], ['2:19', 'Wand'], ['2:20', 'Fishing Pole'],
        ['4:0', 'Misc (Armor)'], ['4:1', 'Cloth'], ['4:2', 'Leather'],
        ['4:3', 'Mail'], ['4:4', 'Plate'], ['4:6', 'Shield'],
        ['4:7', 'Libram'], ['4:8', 'Idol'], ['4:9', 'Totem']
    ];

    function buildTypeFilter() {
        var h = '<option value="">All types</option>';
        TYPE_OPTIONS.forEach(function (o) {
            h += '<option value="' + o[0] + '">' + esc(o[1]) + '</option>';
        });
        $('#filterType').html(h);
    }

    // Reads the controls into the exact parameter shape /Items/Search wants.
    function readFilters() {
        var f = {};

        var q = ($('#itemSearch').val() || '').trim();
        if (q) f.q = q;

        // Type wins over Class: it already carries a class of its own.
        var type = $('#filterType').val();
        if (type) {
            var parts = type.split(':');
            f.classFilter = parts[0];
            f.subclassFilter = parts[1];
        } else {
            var c = $('#filterClass').val();
            if (c !== '' && c != null) f.classFilter = c;
        }

        function pick(sel, key) {
            var v = $(sel).val();
            if (v !== '' && v != null) f[key] = v;
        }
        pick('#filterSlot', 'inventoryTypeFilter');
        pick('#filterQuality', 'qualityFilter');
        pick('#filterMinLvl', 'minLevel');
        pick('#filterMaxLvl', 'maxLevel');
        pick('#filterMinIlvl', 'minItemLevel');
        pick('#filterMaxIlvl', 'maxItemLevel');

        if ($('#filterCustomOnly').is(':checked')) f.customOnly = true;
        if ($('#filterHasDisplay').is(':checked')) f.hasDisplay = true;

        f.sort = $('#filterSort').val() || 'entry';
        f.dir = $('#filterDir').val() || 'asc';
        return f;
    }

    function saveFilters() {
        try { localStorage.setItem(FILTER_KEY, JSON.stringify(readFilters())); } catch (e) { }
    }

    function restoreFilters() {
        var f = null;
        try { f = JSON.parse(localStorage.getItem(FILTER_KEY) || 'null'); } catch (e) { }
        if (!f) return false;

        if (f.q) $('#itemSearch').val(f.q);
        // A stored class+subclass pair round-trips back into the type dropdown.
        if (f.subclassFilter != null && f.classFilter != null) {
            $('#filterType').val(f.classFilter + ':' + f.subclassFilter);
            if (!$('#filterType').val()) $('#filterClass').val(f.classFilter);
        } else if (f.classFilter != null) {
            $('#filterClass').val(f.classFilter);
        }
        if (f.inventoryTypeFilter != null) $('#filterSlot').val(f.inventoryTypeFilter);
        if (f.qualityFilter != null) $('#filterQuality').val(f.qualityFilter);
        if (f.minLevel != null) $('#filterMinLvl').val(f.minLevel);
        if (f.maxLevel != null) $('#filterMaxLvl').val(f.maxLevel);
        if (f.minItemLevel != null) $('#filterMinIlvl').val(f.minItemLevel);
        if (f.maxItemLevel != null) $('#filterMaxIlvl').val(f.maxItemLevel);
        $('#filterCustomOnly').prop('checked', !!f.customOnly);
        $('#filterHasDisplay').prop('checked', !!f.hasDisplay);
        if (f.sort) $('#filterSort').val(f.sort);
        if (f.dir) $('#filterDir').val(f.dir);

        // Any active filter means the advanced row was probably in use.
        return true;
    }

    function clearFilters() {
        $('#itemSearch').val('');
        $('#filterClass, #filterType, #filterSlot, #filterQuality').val('');
        $('#filterMinLvl, #filterMaxLvl, #filterMinIlvl, #filterMaxIlvl').val('');
        $('#filterCustomOnly, #filterHasDisplay').prop('checked', false);
        $('#filterSort').val('entry');
        $('#filterDir').val('asc');
        try { localStorage.removeItem(FILTER_KEY); } catch (e) { }
        doSearch(1);
    }

    // A one-line summary of what's actually narrowing the list, each chip
    // clickable to drop that one filter.
    var SORT_LABELS = {
        entry: 'Entry', name: 'Name', quality: 'Quality',
        itemLevel: 'Item level', requiredLevel: 'Req level', dps: 'DPS'
    };

    function renderFilterChips() {
        var chips = [];
        function chip(label, targets) { chips.push({ label: label, targets: targets }); }

        var q = ($('#itemSearch').val() || '').trim();
        if (q) chip('"' + q + '"', ['#itemSearch']);

        var type = $('#filterType').val();
        if (type) {
            var name = $('#filterType option:selected').text();
            chip(name, ['#filterType']);
        } else if ($('#filterClass').val()) {
            chip($('#filterClass option:selected').text(), ['#filterClass']);
        }
        if ($('#filterSlot').val()) chip($('#filterSlot option:selected').text(), ['#filterSlot']);
        if ($('#filterQuality').val()) chip($('#filterQuality option:selected').text(), ['#filterQuality']);

        var lo = $('#filterMinLvl').val(), hi = $('#filterMaxLvl').val();
        if (lo || hi) chip('Req lvl ' + (lo || '0') + '–' + (hi || '∞'), ['#filterMinLvl', '#filterMaxLvl']);

        var ilo = $('#filterMinIlvl').val(), ihi = $('#filterMaxIlvl').val();
        if (ilo || ihi) chip('Item lvl ' + (ilo || '0') + '–' + (ihi || '∞'), ['#filterMinIlvl', '#filterMaxIlvl']);

        if ($('#filterCustomOnly').is(':checked')) chip('Custom only', ['#filterCustomOnly']);
        if ($('#filterHasDisplay').is(':checked')) chip('Has display', ['#filterHasDisplay']);

        var sort = $('#filterSort').val(), dir = $('#filterDir').val();
        if (sort !== 'entry' || dir !== 'asc')
            chip('Sort: ' + (SORT_LABELS[sort] || sort) + (dir === 'desc' ? ' ↓' : ' ↑'), null);

        var $wrap = $('#itemFilterChips');
        if (chips.length === 0) { $wrap.empty().hide(); return; }

        var html = '';
        chips.forEach(function (c, i) {
            html += '<span class="filter-chip" data-chip="' + i + '">' + esc(c.label) +
                (c.targets ? ' <i class="fa-solid fa-xmark"></i>' : '') + '</span>';
        });
        html += '<button type="button" class="filter-chip filter-chip-clear" id="btnClearFiltersChip">Clear all</button>';
        $wrap.html(html).show();

        $wrap.find('.filter-chip[data-chip]').each(function () {
            var c = chips[parseInt($(this).data('chip'), 10)];
            if (!c.targets) return;
            $(this).on('click', function () {
                c.targets.forEach(function (t) {
                    var $t = $(t);
                    if ($t.is(':checkbox')) $t.prop('checked', false); else $t.val('');
                });
                doSearch(1);
            });
        });
        $('#btnClearFiltersChip').on('click', clearFilters);
    }

    function doSearch(page) {
        currentPage = page || 1;

        var params = readFilters();
        params.page = currentPage;
        params.pageSize = currentPageSize;
        Object.keys(params).forEach(function (k) {
            if (params[k] === undefined || params[k] === '') delete params[k];
        });

        saveFilters();
        renderFilterChips();

        $('#itemListContainer').html('<div class="text-center p-4"><i class="fa-solid fa-spinner fa-spin"></i> Searching...</div>');

        $.getJSON('/Items/Search', params, function (data) {
            currentIcons = data.icons || {};
            totalPages = data.totalPages;
            $('#totalItemCount').text(data.totalCount.toLocaleString());
            $('#resultInfo').text('Showing ' + data.items.length + ' of ' + data.totalCount.toLocaleString());

            if (data.items.length === 0) {
                $('#itemListContainer').html('<div class="text-center p-4 text-muted">No items match these filters</div>');
                $('#paginationBar').hide();
                return;
            }

            var html = '';
            data.items.forEach(function (item) {
                var iconPath = currentIcons[item.displayId] || '/Icon/Get?name=inv_misc_questionmark';
                var qualityClass = 'quality-' + (item.quality || 0);
                var slot = SLOT_NAMES[item.inventoryType] || '';
                var cls = CLASS_NAMES[item.class] || '';
                var isCustom = item.entry >= CUSTOM_RANGE_START;

                // Richer meta line: what it is, where it goes, how strong it is.
                var meta = [cls, slot].filter(Boolean);
                if (item.itemLevel > 0) meta.push('ilvl ' + item.itemLevel);
                if (item.requiredLevel > 1) meta.push('Req ' + item.requiredLevel);
                if (item.dmgMax1 > 0 && item.delay > 0) {
                    var dps = ((item.dmgMin1 + item.dmgMax1) / 2) / (item.delay / 1000);
                    meta.push(dps.toFixed(1) + ' dps');
                } else if (item.armor > 0) {
                    meta.push(item.armor + ' armor');
                }

                html += '<div class="item-row" data-entry="' + item.entry + '">' +
                    '<img class="item-icon" src="' + esc(iconPath) + '" alt="" loading="lazy" />' +
                    '<div style="flex: 1; min-width: 0;">' +
                    '<div class="item-name ' + qualityClass + '">' + esc(item.name) +
                    (isCustom ? ' <span style="font-size:9px;color:var(--status-online);">★</span>' : '') +
                    '</div>' +
                    '<div class="item-meta">' + esc(meta.join(' · ')) + '</div>' +
                    '</div>' +
                    '<div class="item-entry">#' + item.entry + '</div>' +
                    '</div>';
            });

            $('#itemListContainer').html(html);

            if (data.totalPages > 1) {
                $('#paginationBar').show();
                $('#pageJumpInput').val(data.page).attr('max', data.totalPages);
                $('#pageTotalLabel').text(data.totalPages);
                $('#btnFirstPage').prop('disabled', data.page <= 1);
                $('#btnPrevPage').prop('disabled', data.page <= 1);
                $('#btnNextPage').prop('disabled', data.page >= data.totalPages);
                $('#btnLastPage').prop('disabled', data.page >= data.totalPages);
            } else if (data.items.length > 0) {
                // Single page — still show bar for page size selector access
                $('#paginationBar').show();
                $('#pageJumpInput').val(1).attr('max', 1);
                $('#pageTotalLabel').text('1');
                $('#btnFirstPage, #btnPrevPage, #btnNextPage, #btnLastPage').prop('disabled', true);
            } else {
                $('#paginationBar').hide();
            }
        }).fail(function () {
            $('#itemListContainer').html('<div class="text-center p-4 text-muted">Search failed</div>');
        });
    }

    // ===================== ITEM SOURCES =====================
    //
    // "Where does this come from?" — served by /Items/Sources, which walks the
    // reference_loot_template graph rather than only reading flat loot rows, so
    // dungeon and raid drops appear instead of silently resolving to nothing.
    //
    // Buckets are rendered in the order a player would care about them. Rows
    // whose id is an ITEM entry (containers, recipes, disenchant sources) are
    // clickable and load that item.

    var SOURCE_SECTIONS = [
        { key: 'creatures', label: 'Dropped by', icon: 'fa-skull', clickable: false },
        { key: 'objects', label: 'Found in / gathered', icon: 'fa-box-archive', clickable: false },
        { key: 'containers', label: 'Contained in', icon: 'fa-box-open', clickable: true },
        { key: 'quests', label: 'Quests', icon: 'fa-scroll', clickable: false },
        { key: 'vendors', label: 'Sold by', icon: 'fa-coins', clickable: false },
        { key: 'crafted', label: 'Crafted', icon: 'fa-hammer', clickable: true },
        { key: 'disenchant', label: 'Disenchanted from', icon: 'fa-wand-sparkles', clickable: true },
        { key: 'other', label: 'Other', icon: 'fa-circle-question', clickable: false }
    ];

    function loadItemSources(entry) {
        var $panel = $('#itemSourcesPanel'), $body = $('#itemSourcesContent');
        if ($panel.length === 0) return;

        $panel.show();
        $body.html('<div class="text-center p-2" style="font-size:12px;color:var(--text-muted);">' +
            '<i class="fa-solid fa-spinner fa-spin"></i> Resolving sources…</div>');

        $.getJSON('/Items/Sources', { entry: entry }, function (data) {
            // Guard against a slow response landing after the user moved on.
            if (currentDetailEntry !== entry) return;

            if (!data || !data.success) {
                $body.html('<div class="src-empty">Source lookup failed: ' +
                    esc((data && data.error) || 'unknown error') + '</div>');
                return;
            }

            var html = '';
            SOURCE_SECTIONS.forEach(function (sec) {
                var rows = data[sec.key] || [];
                if (rows.length === 0) return;

                html += '<div class="src-section"><div class="src-section-title">' +
                    '<i class="fa-solid ' + sec.icon + '"></i> ' + esc(sec.label) +
                    ' <span class="src-count">' + rows.length + '</span></div>';

                rows.forEach(function (row) {
                    var clickable = sec.clickable && row.id > 0;
                    html += '<div class="src-row' + (clickable ? ' src-clickable' : '') + '"' +
                        (clickable ? ' data-entry="' + row.id + '"' : '') + '>' +
                        '<span class="src-name">' + esc(row.name) + '</span>';
                    if (row.detail) html += '<span class="src-detail">' + esc(row.detail) + '</span>';
                    if (row.chance != null && row.chance > 0)
                        html += '<span class="src-chance">' + row.chance.toFixed(row.chance < 1 ? 2 : 1) + '%</span>';
                    else if (row.chance === 0)
                        html += '<span class="src-chance src-chance-quest">quest/cond</span>';
                    html += '</div>';
                });
                html += '</div>';
            });

            if (html === '')
                html = '<div class="src-empty">No known source — this item is not on any loot table, vendor, quest or recipe.</div>';

            // Diagnostics stay visible rather than being swallowed: a probe that
            // failed is very different from an item that genuinely has no source.
            if (data.notes && data.notes.length) {
                html += '<div class="src-notes"><div class="src-notes-title">' +
                    '<i class="fa-solid fa-triangle-exclamation"></i> Lookup notes</div>';
                data.notes.forEach(function (n) { html += '<div class="src-note">' + esc(n) + '</div>'; });
                html += '</div>';
            }

            $body.html(html);
            $body.find('.src-clickable').on('click', function () {
                if (editMode) return;
                loadDetail(parseInt($(this).data('entry'), 10));
            });
        }).fail(function () {
            if (currentDetailEntry !== entry) return;
            $body.html('<div class="src-empty">Source request failed.</div>');
        });
    }

    // ===================== DETAIL =====================

    function loadDetail(entry) {
        currentDetailEntry = entry;
        $('#detailContent').html('<div class="text-center p-3"><i class="fa-solid fa-spinner fa-spin"></i></div>');

        $.getJSON('/Items/Detail', { entry: entry }, function (data) {
            if (!data.found) {
                $('#detailContent').html('<div class="text-center text-muted p-3">Item not found</div>');
                $('#detailActions').hide();
                return;
            }

            currentDetailItem = data.item;
            var item = data.item;
            var q = item.quality || 0;
            var qualityClass = 'quality-' + q;
            var isCustom = entry >= CUSTOM_RANGE_START;

            var html = '<div class="item-detail-header">' +
                '<img class="detail-icon-lg" src="' + esc(data.iconPath) + '" data-entry="' + item.entry + '" title="Click to edit" />' +
                '<div style="flex:1;min-width:0;">' +
                '<div class="' + qualityClass + '" style="font-size: 18px; font-weight: 700; line-height: 1.2;">' + esc(item.name) + '</div>' +
                '<div style="font-size: 13px; color: var(--text-muted); margin-top: 2px;">' +
                esc(QUALITY_NAMES[q] || '') + ' · Entry #' + item.entry +
                (isCustom ? ' <span style="color: var(--status-online);">★ Custom</span>' : '') +
                '</div>' +
                '</div>' +
                '</div>';

            // 3D model preview (if available). Mounted by /js/character-viewer/item-preview.js
            // rather than <model-viewer>: the GLB carries WoW blend modes in its material names plus
            // a `suiFx` manifest (material animation + particle emitters + ItemVisual enchant
            // glows), and a stock glTF viewer decodes none of it — enchant effects in particular are
            // emitter-only, so they render as nothing at all.
            if (data.modelPath) {
                html += '<div class="model-preview-container" data-sui-glb="' + escAttr(data.modelPath) + '"></div>';
            }

            if (item.bonding > 0)
                html += '<div style="font-size: 12px; color: var(--text-secondary);">' + esc(BONDING_NAMES[item.bonding] || '') + '</div>';

            var slotText = SLOT_NAMES[item.inventory_type] || '';
            var classText = CLASS_NAMES[item.class] || '';
            if (slotText || classText)
                html += '<div class="d-flex justify-content-between" style="font-size: 12.5px; color: var(--text-secondary);"><span>' + esc(slotText) + '</span><span>' + esc(classText) + '</span></div>';

            if (item.armor > 0)
                html += '<div style="font-size: 12.5px;">' + item.armor + ' Armor</div>';

            if (item.dmg_min1 > 0 || item.dmg_max1 > 0) {
                var speed = (item.delay || 2000) / 1000;
                var dps = ((item.dmg_min1 + item.dmg_max1) / 2) / speed;
                html += '<div class="d-flex justify-content-between" style="font-size: 12.5px;"><span>' + item.dmg_min1 + ' - ' + item.dmg_max1 + ' Damage</span><span>Speed ' + speed.toFixed(2) + '</span></div>' +
                    '<div style="font-size: 12.5px;">(' + dps.toFixed(1) + ' damage per second)</div>';
            }

            var stats = [];
            for (var i = 1; i <= 10; i++) {
                var st = item['stat_type' + i], sv = item['stat_value' + i];
                if (st > 0 && sv !== 0)
                    stats.push((sv > 0 ? '+' : '') + sv + ' ' + (STAT_TYPES[st] || 'Stat ' + st));
            }
            if (stats.length > 0) {
                html += '<div class="detail-section">';
                stats.forEach(function (s) { html += '<div class="stat-line">' + esc(s) + '</div>'; });
                html += '</div>';
            }

            // Prefer the server-resolved spells (with name + Spell.dbc tooltip
            // text); fall back to reconstructing bare ids from raw item fields.
            var spells = (data.spells && data.spells.length) ? data.spells : [];
            if (!spells.length) {
                for (var j = 1; j <= 5; j++) {
                    var sid = item['spellid_' + j] || item['spell_id_' + j];
                    var trigger = item['spelltrigger_' + j] || item['spell_trigger_' + j];
                    if (sid > 0) spells.push({ id: sid, trigger: trigger });
                }
            }
            if (spells.length > 0) {
                html += '<div class="detail-section"><div class="detail-section-title">Spells</div>';
                spells.forEach(function (sp) {
                    var triggerLabel = esc(TRIGGER_NAMES[sp.trigger] || 'Trigger ' + sp.trigger);
                    var title = sp.name ? esc(sp.name) : ('Spell #' + sp.id);
                    html += '<div class="spell-line"><i class="fa-solid fa-bolt" style="font-size: 10px;"></i> ' +
                        triggerLabel + ': ' + title +
                        ' <span class="spell-id-tag">#' + sp.id + '</span></div>';
                    if (sp.description)
                        html += '<div class="spell-desc">' + esc(sp.description) + '</div>';
                });
                html += '</div>';
            }

            html += '<div class="detail-section"><div class="detail-section-title">Info</div>';
            if (item.required_level > 1)
                html += '<div class="detail-row"><span class="label">Required Level</span><span class="value">' + item.required_level + '</span></div>';
            html += '<div class="detail-row"><span class="label">Item Level</span><span class="value">' + (item.item_level || 0) + '</span></div>';
            if (item.buy_price > 0)
                html += '<div class="detail-row"><span class="label">Buy Price</span><span class="value">' + formatCopper(item.buy_price) + '</span></div>';
            if (item.sell_price > 0)
                html += '<div class="detail-row"><span class="label">Sell Price</span><span class="value">' + formatCopper(item.sell_price) + '</span></div>';
            if (item.stackable > 1)
                html += '<div class="detail-row"><span class="label">Max Stack</span><span class="value">' + item.stackable + '</span></div>';
            html += '<div class="detail-row"><span class="label">Display ID</span><span class="value">' + (item.display_id || 0) + '</span></div>';
            html += '</div>';

            if (item.description)
                html += '<div style="font-size: 12px; color: #ffd100; font-style: italic; margin-top: 10px;">"' + esc(item.description) + '"</div>';

            $('#detailContent').html(html);
            // #detailContent is visible here, so the preview sizes correctly on its first frame.
            window.suiItemPreview?.mountPending(document.getElementById('detailContent'));

            // Show action buttons
            $('#detailActions').show();
            // If item is custom, change Edit button text
            if (isCustom) {
                $('#btnEditOriginal').html('<i class="fa-solid fa-pen"></i> Edit');
            } else {
                $('#btnEditOriginal').html('<i class="fa-solid fa-pen"></i> Edit Original');
            }

            // Load OG changelog
            loadItemChangelog(entry);

            // Where the item comes from (drops / vendors / quests / crafting)
            loadItemSources(entry);

            // Load textures from MPQ
            var did = item.display_id || 0;
            loadItemTextures(did);

            // 3D character viewer integration — fire-and-forget event the
            // items-character-panel module listens for. Only equippable
            // items with a valid displayId are forwarded; consumables and
            // trade goods (inventory_type=0) are skipped so the viewer
            // doesn't try to dress a character in a potion.
            if (did > 0 && item.inventory_type > 0) {
                document.dispatchEvent(new CustomEvent('cv:item-selected', {
                    detail: {
                        itemId: item.entry,
                        displayId: did,
                        inventoryType: item.inventory_type,
                        name: item.name,
                    },
                }));
            }
        });
    }

    // ===================== ITEM TEXTURES =====================

    function loadItemTextures(displayId) {
        if (!displayId || displayId <= 0) {
            $('#itemTexturePanel').hide();
            return;
        }

        $('#itemTexturePanel').show();
        $('#itemTextureContent').html(
            '<div class="text-center p-2" style="font-size: 12px; color: var(--text-muted);">' +
            '<i class="fa-solid fa-spinner fa-spin"></i> Extracting textures from MPQ...</div>'
        );

        loadTextureGrid(displayId, '#itemTextureContent');
    }

    function loadEditTextures(displayId) {
        if (!displayId || displayId <= 0) {
            $('#editTextureContent').html(
                '<div class="text-center p-2" style="font-size:11px;color:var(--text-muted);">No display ID</div>');
            return;
        }

        $('#editTextureContent').html(
            '<div class="text-center p-2" style="font-size: 11px; color: var(--text-muted);">' +
            '<i class="fa-solid fa-spinner fa-spin"></i> Loading...</div>'
        );

        loadTextureGrid(displayId, '#editTextureContent');
    }

    function loadTextureGrid(displayId, targetSelector) {
        $.getJSON('/Items/TextureInfo', { displayId: displayId }, function (data) {
            if (!data.found || !data.textures || data.textures.length === 0) {
                var isEditEmpty = (targetSelector === '#editTextureContent');
                var invTypeEmpty = currentDetailItem ? (currentDetailItem.inventory_type || 0) : 0;
                // Body-atlas (painted armor) has no model textures, so TextureInfo
                // is empty — but it CAN be retextured via the component-slot path.
                // Render the entry point HERE, inside the edit panel (#colEdit), so
                // the slide-in panel is actually visible when clicked. (Putting it
                // in the detail panel opened a panel in a hidden column — it only
                // appeared after entering edit. This is the right home.)
                if (isEditEmpty && isBodyAtlasType(invTypeEmpty)) {
                    $(targetSelector).html(
                        '<div class="text-center p-2" style="font-size:11px;color:var(--text-muted);margin-bottom:8px;">' +
                        'Painted armor — no model texture. Recolor its body-atlas components:</div>' +
                        '<div class="text-center"><button class="btn-retex-panel" id="btnBodyAtlasRetex" ' +
                        'title="Retexture variations (painted armor)">' +
                        '<i class="fa-solid fa-wand-magic-sparkles"></i> Retexture variations</button></div>'
                    );
                    return;
                }
                $(targetSelector).html(
                    '<div class="text-center p-2" style="font-size: 11px; color: var(--text-muted);">' +
                    'No textures found for this model</div>'
                );
                return;
            }

            var html = '';

            // Model info header — and, in edit mode, a launcher button for
            // the new retexture panel (sibling of the per-texture wand icons,
            // for users who want to retexture without picking a specific tex
            // first; it opens the panel on the first texture).
            var isEdit = (targetSelector === '#editTextureContent');
            html += '<div style="font-size: 11px; color: var(--text-muted); margin-bottom: 8px; padding: 0 2px; display:flex; align-items:center; gap:6px;">' +
                '<span style="color: var(--accent);">' + esc(data.modelName) + '</span>' +
                ' · ' + data.vertexCount.toLocaleString() + 'v/' + data.triangleCount.toLocaleString() + 't' +
                ' · ' + (data.m2Size / 1024).toFixed(0) + 'KB';
            if (isEdit) {
                html += '<button class="btn-retex-panel" id="btnOpenRetexturePanel" ' +
                    'title="Open retexture panel" style="margin-left:auto;">' +
                    '<i class="fa-solid fa-wand-magic-sparkles"></i> Retexture textures</button>';
            }
            html += '</div>';

            // Texture grid
            html += '<div class="item-texture-grid">';

            data.textures.forEach(function (tex) {
                var sizeLabel = tex.width + '×' + tex.height;
                var formatLabel = tex.format + (tex.alphaDepth > 0 ? ' α' + tex.alphaDepth : '');
                var blpKB = (tex.blpFileSize / 1024).toFixed(0);

                html += '<div class="item-texture-card" data-mpq-path="' + esc(tex.mpqPath) + '" ' +
                    'data-tex-index="' + tex.index + '" data-width="' + tex.width + '" data-height="' + tex.height + '" ' +
                    'data-format="' + esc(tex.format) + '" title="' + esc(tex.mpqPath) + '">';

                if (tex.hasPreview) {
                    html += '<img class="item-texture-preview" src="' + esc(tex.previewUrl) + '" ' +
                        'alt="' + esc(tex.filename) + '" loading="lazy" />';
                } else {
                    html += '<div class="item-texture-preview" style="display:flex;align-items:center;justify-content:center;' +
                        'background:var(--bg-input);color:var(--text-muted);font-size:10px;">No preview</div>';
                }

                html += '<div class="item-texture-info">' +
                    '<div class="item-texture-name" title="' + esc(tex.filename) + '">' + esc(tex.filename) + '</div>' +
                    '<div class="item-texture-meta">' + sizeLabel + ' · ' + formatLabel + ' · ' + blpKB + 'KB</div>' +
                    '<button class="btn-retexture" title="AI Retexture"><i class="fa-solid fa-wand-magic-sparkles"></i></button>' +
                    '</div>';

                html += '</div>';
            });

            html += '</div>';

            $(targetSelector).html(html);
        }).fail(function () {
            $(targetSelector).html(
                '<div class="text-center p-2" style="font-size: 11px; color: var(--status-error);">' +
                'Failed to load textures</div>'
            );
        });
    }

    // Texture card click → show full-size preview in a modal overlay
    $(document).on('click', '.item-texture-card', function (e) {
        // Don't open overlay if they clicked the retexture button
        if ($(e.target).closest('.btn-retexture').length) return;

        var $img = $(this).find('.item-texture-preview');
        if (!$img.is('img')) return;

        var src = $img.attr('src');
        var filename = $(this).find('.item-texture-name').text();
        var meta = $(this).find('.item-texture-meta').text();
        var mpqPath = $(this).data('mpq-path');

        // Simple overlay
        var overlay = $('<div class="texture-overlay">' +
            '<div class="texture-overlay-content">' +
            '<img src="' + esc(src) + '" style="max-width:100%;max-height:70vh;image-rendering:pixelated;border-radius:4px;" />' +
            '<div style="margin-top:10px;text-align:center;">' +
            '<div style="font-size:14px;font-weight:600;color:var(--text-primary);">' + esc(filename) + '</div>' +
            '<div style="font-size:11px;color:var(--text-muted);margin-top:2px;">' + esc(meta) + '</div>' +
            '<div style="font-size:10px;color:var(--text-muted);margin-top:2px;word-break:break-all;">' + esc(mpqPath) + '</div>' +
            '</div>' +
            '</div>' +
            '</div>');

        overlay.on('click', function () { $(this).remove(); });
        $('body').append(overlay);
    });

    // ===================== RETEXTURE =====================

    // Right-click on texture card → show retexture dialog
    // ── Retexture Panel: content factory ────────────────────────────────
    // Returns the inner HTML for the retexture panel. Same content as the
    // old modal (mode tabs, palette/variations/segmented sections, footer)
    // minus the overlay wrapper and the modal-internal 3D viewer pane.
    // Preview is driven via window.retexEquipWeaponGlbDirect on the existing
    // character viewer to the right — see the .segmented-card click handler.
    // ── Body-atlas panel: Variations-only, no source BLP ────────────────
    // Painted armor (boots/gloves/belts/chest/legs/etc.) has no model texture,
    // so the model-texture modes don't apply. This panel reuses the SAME
    // variations element ids (variationTheme/variationCount/variationGenBtn/
    // variationDetected/variationGallery) so the existing generate handler and
    // the body-atlas dispatch work unchanged. A slot-status line shows which
    // component slots were found vs missing after the first generate.
    function buildBodyAtlasPanelContent(texData) {
        var slot = SLOT_NAMES[texData.inventoryType] || 'Armor';
        return (
            '<div class="retex-panel-header">' +
            '<i class="fa-solid fa-wand-magic-sparkles" style="color:var(--accent);font-size:16px;"></i>' +
            '<div class="retex-panel-title">Retexture<span class="retex-tex-name">' + esc(texData.filename) + '</span></div>' +
            '<button class="btn-sm btn-outline-subtle" id="retexBackBtn" title="Back to edit">' +
            '<i class="fa-solid fa-arrow-left"></i> Back</button>' +
            '</div>' +
            '<div class="retex-panel-body">' +
            '<div style="font-size:11px;color:var(--text-muted);margin-bottom:6px;">' +
            esc(slot) + ' · painted armor (body atlas) · no model texture</div>' +
            '<div style="font-size:10px;color:var(--text-muted);margin-bottom:12px;">' +
            'This piece paints into the shared body atlas. Variations recolors every ' +
            'component slot it can find in the MPQ. Click a card to preview on your character.</div>' +

            // Slot status — filled in after the first generate (found vs missing).
            '<div id="bodyAtlasSlotStatus" style="font-size:10px;margin-bottom:10px;"></div>' +

            // Variations section — same ids as the model panel so handlers reuse.
            '<div id="retexVariationsSection" style="margin-bottom:12px;">' +
            '<label style="display:block;font-size:11px;font-weight:600;color:var(--text-secondary);margin-bottom:3px;">Theme (or leave blank for surprise)</label>' +
            '<input type="text" id="variationTheme" class="form-input" ' +
            'placeholder="e.g. marble &amp; gold, obsidian, frost, blood, holy" ' +
            'style="margin-bottom:8px;font-size:12px;" />' +
            '<div style="display:flex;gap:8px;align-items:center;margin-bottom:8px;">' +
            '<label style="font-size:11px;color:var(--text-secondary);">Variants:</label>' +
            '<select id="variationCount" class="form-input" style="width:60px;font-size:12px;padding:2px 4px;">' +
            '<option>2</option><option selected>4</option><option>6</option><option>8</option></select>' +
            '<button class="btn-sm btn-accent" id="variationGenBtn"><i class="fa-solid fa-dice"></i> Generate variants</button>' +
            '</div>' +
            '<div id="variationDetected" style="font-size:10px;color:var(--text-muted);margin-bottom:8px;"></div>' +
            '<div id="variationGallery" class="retex-card-grid"></div>' +
            '</div>' +
            '</div>'
        );
    }

    function buildRetexturePanelContent(texData) {
        // Body-atlas (painted armor) variant: no source BLP, no model-texture
        // modes (scratch/modify/palette/segmented all operate on a model
        // texture this item doesn't have). Only Variations applies — it recolors
        // the component slots server-side. Render a slimmed panel locked to it.
        if (texData.bodyAtlas) {
            return buildBodyAtlasPanelContent(texData);
        }
        var mpqPath = texData.mpqPath;
        var filename = texData.filename;
        var width = texData.width;
        var height = texData.height;
        var format = texData.format;
        return (
            '<div class="retex-panel-header">' +
            '<i class="fa-solid fa-wand-magic-sparkles" style="color:var(--accent);font-size:16px;"></i>' +
            '<div class="retex-panel-title">Retexture<span class="retex-tex-name">' + esc(filename) + '</span></div>' +
            '<button class="btn-sm btn-outline-subtle" id="retexBackBtn" title="Back to edit">' +
            '<i class="fa-solid fa-arrow-left"></i> Back</button>' +
            '</div>' +
            '<div class="retex-panel-body">' +
            '<div style="font-size:11px;color:var(--text-muted);margin-bottom:12px;">' +
            width + '×' + height + ' · ' + esc(format) + ' · ' + esc(mpqPath) + '</div>' +

            // Mode toggle. Variations first + default; Palette swap second;
            // From scratch / Modify after. (Segmented mode removed May 2026.)
            '<div style="display:flex;gap:6px;margin-bottom:12px;flex-wrap:wrap;">' +
            '<label style="display:flex;align-items:center;gap:5px;font-size:11px;color:var(--text-primary);cursor:pointer;padding:5px 10px;border:1px solid var(--border);border-radius:var(--radius-sm);flex:1;transition:all 0.15s;min-width:100px;" class="retex-mode-btn active" data-mode="variations">' +
            '<input type="radio" name="retexMode" value="variations" checked style="accent-color:var(--accent);"> Variations</label>' +
            '<label style="display:flex;align-items:center;gap:5px;font-size:11px;color:var(--text-primary);cursor:pointer;padding:5px 10px;border:1px solid var(--border);border-radius:var(--radius-sm);flex:1;transition:all 0.15s;min-width:100px;" class="retex-mode-btn" data-mode="palette">' +
            '<input type="radio" name="retexMode" value="palette" style="accent-color:var(--accent);"> Palette swap</label>' +
            '<label style="display:flex;align-items:center;gap:5px;font-size:11px;color:var(--text-primary);cursor:pointer;padding:5px 10px;border:1px solid var(--border);border-radius:var(--radius-sm);flex:1;transition:all 0.15s;min-width:100px;" class="retex-mode-btn" data-mode="scratch">' +
            '<input type="radio" name="retexMode" value="scratch" style="accent-color:var(--accent);"> From scratch</label>' +
            '<label style="display:flex;align-items:center;gap:5px;font-size:11px;color:var(--text-primary);cursor:pointer;padding:5px 10px;border:1px solid var(--border);border-radius:var(--radius-sm);flex:1;transition:all 0.15s;min-width:100px;" class="retex-mode-btn" data-mode="modify">' +
            '<input type="radio" name="retexMode" value="modify" style="accent-color:var(--accent);"> Modify existing</label>' +
            '</div>' +

            // Vision recolor section (hidden unless palette mode)
            '<div id="retexPaletteSection" style="display:none;margin-bottom:12px;">' +
            '<label style="display:block;font-size:11px;font-weight:600;color:var(--text-secondary);margin-bottom:3px;">Recolor Instruction</label>' +
            '<textarea id="recolorInstruction" class="form-input" rows="2" ' +
            'placeholder="e.g. Swap all gray/steel for obsidian black. Make gold borders brighter. Change blue tints to deep crimson." ' +
            'style="margin-bottom:8px;resize:vertical;font-size:12px;"></textarea>' +
            '<div style="display:flex;gap:8px;align-items:center;margin-bottom:8px;">' +
            '<button class="btn-sm btn-outline-subtle" id="recolorPreviewBtn">' +
            '<i class="fa-solid fa-eye"></i> Preview recolor</button>' +
            '<label style="display:flex;align-items:center;gap:5px;font-size:11px;color:var(--text-primary);cursor:pointer;">' +
            '<input type="checkbox" id="paletteChainAI" style="accent-color:var(--accent);"> + AI polish</label>' +
            '<label style="display:flex;align-items:center;gap:5px;font-size:11px;color:var(--status-warning, #d49a00);cursor:pointer;" title="TEST: skip brute-force, send original straight to Flux at 0.5">' +
            '<input type="checkbox" id="paletteSkipBrute" style="accent-color:var(--accent);"> Flux-only (test)</label>' +
            '<div id="paletteAIDenoiseRow" style="display:none;flex:1;">' +
            '<input type="range" id="paletteAIDenoise" min="10" max="80" value="50" style="width:100%;accent-color:var(--accent);" />' +
            '<span style="font-size:10px;color:var(--text-muted);" id="paletteAIDenoiseVal">0.50</span>' +
            '</div></div>' +
            '<div id="recolorPreview" style="display:none;"></div>' +
            '</div>' +

            // Variations section (default mode — visible on open).
            '<div id="retexVariationsSection" style="margin-bottom:12px;">' +
            '<label style="display:block;font-size:11px;font-weight:600;color:var(--text-secondary);margin-bottom:3px;">Theme (or leave blank for surprise)</label>' +
            '<input type="text" id="variationTheme" class="form-input" ' +
            'placeholder="e.g. frost, fel corruption, blood, volcanic, holy, shadow" ' +
            'style="margin-bottom:8px;font-size:12px;" />' +
            '<div style="display:flex;gap:8px;align-items:center;margin-bottom:8px;">' +
            '<label style="font-size:11px;color:var(--text-secondary);">Variants:</label>' +
            '<select id="variationCount" class="form-input" style="width:60px;font-size:12px;padding:2px 4px;">' +
            '<option>2</option><option selected>4</option><option>6</option><option>8</option></select>' +
            '<button class="btn-sm btn-accent" id="variationGenBtn"><i class="fa-solid fa-dice"></i> Generate variants</button>' +
            '</div>' +
            '<div id="variationDetected" style="font-size:10px;color:var(--text-muted);margin-bottom:8px;"></div>' +
            '<div id="variationGallery" class="retex-card-grid"></div>' +
            '</div>' +

            // Denoise + AI fields (shown for modify mode)
            '<div id="retexDenoiseRow" style="margin-bottom:12px;display:none;">' +
            '<label style="display:block;font-size:11px;font-weight:600;color:var(--text-secondary);margin-bottom:3px;">' +
            'Modification Strength: <span id="retexDenoiseVal">0.50</span></label>' +
            '<div style="display:flex;align-items:center;gap:8px;">' +
            '<span style="font-size:10px;color:var(--text-muted);">Subtle</span>' +
            '<input type="range" id="retexDenoise" min="10" max="95" value="50" style="flex:1;accent-color:var(--accent);" />' +
            '<span style="font-size:10px;color:var(--text-muted);">Major</span>' +
            '</div>' +
            '</div>' +
            '<div id="retexAIFields" style="display:none;">' +
            '<label style="display:block;font-size:11px;font-weight:600;color:var(--text-secondary);margin-bottom:3px;">Style Direction</label>' +
            '<input type="text" id="retextureStyle" class="form-input" placeholder="e.g. dark corrupted, frost-enchanted, golden holy" style="margin-bottom:10px;" />' +
            '<label style="display:block;font-size:11px;font-weight:600;color:var(--text-secondary);margin-bottom:3px;">Custom Prompt (optional — bypasses Ollama, sent directly to Flux)</label>' +
            '<textarea id="retexturePrompt" class="form-input" rows="3" placeholder="⚠ Must describe a FLAT TEXTURE, not an object." style="margin-bottom:14px;resize:vertical;"></textarea>' +
            '</div>' +
            '<div id="retextureStatus" style="margin-top:12px;display:none;"></div>' +
            '</div>' +  // /retex-panel-body

            // Footer
            '<div class="retex-panel-foot">' +
            '<span class="retex-unsaved-dot">Unsaved retexture — Save to keep</span>' +
            '<button class="btn-sm btn-outline-subtle" id="retextureCancel">Close</button>' +
            '<button class="btn-sm btn-accent" id="retextureGo" style="display:none;" ' +
            'data-mpq="' + esc(mpqPath) + '" data-filename="' + esc(filename) + '">' +
            '<i class="fa-solid fa-wand-magic-sparkles"></i> Generate</button>' +
            '</div>'
        );
    }

    // ── Retexture Panel: open / close (slide-swap with edit card) ────────
    // The panel lives in #colEdit alongside #editPanel. Toggling the
    // .retex-swap-active class on #colEdit triggers the CSS slide. The
    // character viewer to the right stays mounted — click a card to push a
    // staged retexture onto it via equipWeaponGlbDirect.
    function openRetexturePanel(texData) {
        if (!texData || (!texData.mpqPath && !texData.bodyAtlas)) {
            showToast('No texture selected', 'warning');
            return;
        }

        // The retexture panel slides in over #colEdit. If we're still in the
        // detail view (#colEdit hidden), enter edit mode FIRST, then re-open
        // once the edit panel is visible — otherwise the slide happens in a
        // hidden column and nothing appears until the user manually hits Edit.
        // Covers every entry point (detail-header button, edit-panel button,
        // weapon texture clicks) since they all funnel through here.
        if (!editMode || !$('#colEdit').is(':visible')) {
            if (!currentDetailEntry) {
                showToast('Open an item first', 'warning');
                return;
            }
            pendingRetexOpen = texData;
            // Same path the Edit button uses: custom items edit directly,
            // base-game items get the clone/edit confirm modal first. Either
            // way, showEditPanel consumes pendingRetexOpen when #colEdit shows.
            $('#btnEditOriginal').trigger('click');
            return;
        }

        // Reset staging on (re)open. Closing the panel deliberately keeps the
        // selected preview alive so Save can commit it; if the user opens the
        // retexture panel again, that staged selection is being discarded, so
        // release every temp GLB (including both shoulder sides) first.
        cleanupStagedPreviewGlbs(null);
        stagedRetexture = null;

        var $panel = $('#retexturePanel');
        $panel.html(buildRetexturePanelContent(texData));

        // Store the panel's "from texture" data on the panel itself so the
        // mode handlers (segGenBtn, retextureGo) can pull mpq/filename/etc.
        // without grovelling through #retextureGo's data-attrs (which still
        // carry them too, for backward-compat with the existing handlers).
        $panel.data('retex-tex', texData);

        // Engage the slide. The CSS rule keeps the panel always absolutely-
        // positioned and parked off-right via translateX; adding the
        // .retex-swap-active class on #colEdit slides it into place via a
        // CSS transition (0.32s ease). requestAnimationFrame ensures the
        // new innerHTML has committed before the class flips, so the
        // transition starts from a fully-painted off-right state.
        requestAnimationFrame(function () {
            $('#colEdit').addClass('retex-swap-active');
        });
    }

    function closeRetexturePanel(opts) {
        opts = opts || {};
        if (hasUnsavedRetexture() && !opts.force) {
            var ok = window.confirm(
                'You have an unsaved retexture preview ("' + (stagedRetexture.name || 'variant') +
                '").\n\nGo back to edit? The preview stays on the character — but you must hit Save on the edit panel to keep it. Closing without Save will discard it.');
            if (!ok) return false;
        }

        // Slide back: remove the active class; CSS transitions the panels.
        $('#colEdit').removeClass('retex-swap-active');

        // After the transition, sweep any temp preview GLBs we haven't
        // committed (the staged one, if any, stays on the character via the
        // earlier equipWeaponGlbDirect call — its temp file lives until Save
        // or until the stale-sweep runs server-side).
        cleanupStagedPreviewGlbs(
            hasUnsavedRetexture() ? previewGlbUrls(stagedRetexture) : null);

        return true;
    }

    // Per-texture wand button (inside the edit form's texture cards) →
    // opens the retexture panel for that texture.
    $(document).on('click', '.btn-retexture', function (e) {
        e.stopPropagation();
        var $card = $(this).closest('.item-texture-card');
        openRetexturePanel({
            mpqPath: $card.data('mpq-path'),
            filename: $card.find('.item-texture-name').text(),
            width: $card.data('width'),
            height: $card.data('height'),
            format: $card.data('format')
        });
    });

    // Panel-level "Retexture textures" button on the edit form → opens with
    // the first texture preselected. The button is injected by renderEditForm
    // (or wherever the edit panel is rendered) with id #btnOpenRetexturePanel.
    $(document).on('click', '#btnOpenRetexturePanel', function (e) {
        e.stopPropagation();
        // Find the first texture card in the edit form's texture list. The
        // texture cards are .item-texture-card elements rendered by
        // loadEditTextures. If none yet rendered, bail with a hint.
        var $first = $('.item-texture-card').first();
        if (!$first.length) {
            showToast('Loading textures — try again in a moment', 'info');
            return;
        }
        openRetexturePanel({
            mpqPath: $first.data('mpq-path'),
            filename: $first.find('.item-texture-name').text(),
            width: $first.data('width'),
            height: $first.data('height'),
            format: $first.data('format')
        });
    });

    // Body-atlas armor (boots/gloves/belts/chest/legs) → open the retexture
    // panel DIRECTLY to Variations, no clicked BLP. Synthesizes a texData
    // flagged bodyAtlas so openRetexturePanel skips the mpqPath guard and
    // buildRetexturePanelContent renders the body-atlas variant of the panel.
    $(document).on('click', '#btnBodyAtlasRetex', function (e) {
        e.stopPropagation();
        if (!currentDetailItem || !(currentDetailItem.display_id > 0)) {
            showToast('No display ID for this item', 'warning');
            return;
        }
        openRetexturePanel({
            bodyAtlas: true,
            displayId: currentDetailItem.display_id,
            filename: currentDetailItem.name || 'Armor',
            inventoryType: currentDetailItem.inventory_type || 0
        });
    });

    // Close panel button(s).
    $(document).on('click', '#retextureCancel, #retexBackBtn', function () {
        closeRetexturePanel();
    });

    // Check vision model availability whenever the panel is shown — moved
    // out of the per-open callback so it runs each time the panel opens.
    $(document).on('change', 'input[name="retexMode"]', function (ev) {
        // Lazy vision-model check: only when palette mode is selected.
        if ($(this).val() === 'palette') {
            $.get('/Items/VisionRecolorStatus', function (data) {
                if (!data.available && $('#retexPaletteSection').is(':visible')
                    && !$('#retexPaletteSection .vision-warning').length) {
                    $('#retexPaletteSection').prepend(
                        '<div class="vision-warning" style="font-size:11px;color:var(--status-warning);margin-bottom:6px;">' +
                        '<i class="fa-solid fa-triangle-exclamation"></i> Vision model not configured — set Ollama Vision Model in Settings</div>');
                }
            });
        }
    });

    // Vision recolor preview button
    $(document).on('click', '#recolorPreviewBtn', function () {
        var $btn = $(this);
        var instruction = $('#recolorInstruction').val() || '';
        if (!instruction.trim()) {
            showToast('Enter a recolor instruction', 'warning');
            return;
        }
        var displayId = currentDetailItem ? (currentDetailItem.display_id || 0) : 0;
        var mpqPath = $('#retextureGo').data('mpq');
        var filename = $('#retextureGo').data('filename');

        $btn.prop('disabled', true).html('<i class="fa-solid fa-spinner fa-spin"></i> Analyzing...');
        $.ajax({
            url: '/Items/VisionRecolorPreview',
            method: 'POST',
            contentType: 'application/json',
            data: JSON.stringify({
                displayId: displayId,
                originalMpqPath: mpqPath,
                originalBlpFilename: filename,
                instruction: instruction
            }),
            success: function (data) {
                $btn.prop('disabled', false).html('<i class="fa-solid fa-eye"></i> Preview recolor');
                if (data.success && data.previewUrl) {
                    $('#recolorPreview').show().html(
                        '<img src="' + esc(data.previewUrl) + '?t=' + Date.now() + '" ' +
                        'style="max-width:100%;border-radius:4px;image-rendering:pixelated;" />');
                } else {
                    $('#recolorPreview').show().html(
                        '<div style="font-size:11px;color:var(--status-error);">' + esc(data.error || 'Preview failed') + '</div>');
                }
            },
            error: function () {
                $btn.prop('disabled', false).html('<i class="fa-solid fa-eye"></i> Preview recolor');
                showToast('Preview request failed', 'error');
            }
        });
    });

    // Best-effort delete of temp preview GLBs (skip `keepUrls` if the staged
    // pair is still in use on the character). Called by closeRetexturePanel
    // and by the panel's re-open path (which resets staging).
    function cleanupStagedPreviewGlbs(keepUrls) {
        if (typeof keepUrls === 'string') keepUrls = [keepUrls];
        var keep = new Set(keepUrls || []);
        (stagedPreviewGlbs || []).forEach(function (url) {
            if (keep.has(url)) return;
            $.ajax({
                url: '/Items/DeletePreviewGlb',
                method: 'POST',
                contentType: 'application/json',
                data: JSON.stringify({ glbUrl: url })
            });
        });
        stagedPreviewGlbs = Array.from(keep);
    }

    // Mode toggle: show/hide sections based on mode. No modal-layout
    // gymnastics — the panel is always the panel; only the inner sections
    // change visibility per mode.
    $(document).on('change', 'input[name="retexMode"]', function () {
        var mode = $(this).val();
        var isVar = mode === 'variations';
        // Gallery modes have their own Generate + per-card click-to-preview,
        // so the main Generate button and AI fields are irrelevant in them.
        var isGallery = isVar;
        $('#retexDenoiseRow').toggle(mode === 'modify');
        $('#retexAIFields').toggle(mode !== 'palette' && !isGallery);
        $('#retexPaletteSection').toggle(mode === 'palette');
        $('#retexVariationsSection').toggle(isVar);
        $('#retextureGo').toggle(!isGallery);
        $('.retex-mode-btn').removeClass('active');
        $(this).closest('.retex-mode-btn').addClass('active');
    });

    // ── Variations: generate variants ──
    // Painted-armor (body-atlas) inventory types: chest/legs/feet/waist/wrist/
    // hands/back/tabard/robe/shirt. These have no standalone model — they paint
    // component textures into the character body atlas, so they use the
    // GenerateBodyAtlasVariations endpoint + equipBodyAtlasRetextureDirect paint
    // path instead of the GLB preview/mount path. Helm(1)/shoulder(3)/weapons
    // have models and keep the GLB path.
    var BODY_ATLAS_INVENTORY_TYPES = [4, 5, 6, 7, 8, 9, 10, 16, 19, 20];
    function isBodyAtlasType(t) { return BODY_ATLAS_INVENTORY_TYPES.indexOf(t) !== -1; }
    // Body-atlas variation cards store their recolored per-slot URLs here, keyed
    // by card index (a slot dict doesn't fit cleanly in a data-attribute).
    var bodyAtlasVariants = [];

    $(document).on('click', '#variationGenBtn', function () {
        var $b = $(this);
        var displayId = currentDetailItem ? (currentDetailItem.display_id || 0) : 0;
        var invType = currentDetailItem ? (currentDetailItem.inventory_type || 0) : 0;
        var bodyAtlas = isBodyAtlasType(invType);
        var mpqPath = $('#retextureGo').data('mpq');
        var filename = $('#retextureGo').data('filename');
        var theme = $('#variationTheme').val() || '';
        var count = parseInt($('#variationCount').val()) || 4;

        $b.prop('disabled', true).html('<i class="fa-solid fa-spinner fa-spin"></i> Generating...');
        $('#variationGallery').html(
            '<div style="grid-column:1/-1;font-size:11px;color:var(--text-muted);">' +
            '<i class="fa-solid fa-spinner fa-spin"></i> Designing coherent palettes & rendering...</div>');

        $.ajax({
            url: bodyAtlas ? '/Items/GenerateBodyAtlasVariations' : '/Items/GenerateVariations',
            method: 'POST',
            contentType: 'application/json',
            data: JSON.stringify(bodyAtlas ? {
                displayId: displayId,
                theme: theme,
                count: count
            } : {
                displayId: displayId,
                originalMpqPath: mpqPath,
                originalBlpFilename: filename,
                theme: theme,
                count: count
            }),
            success: function (data) {
                $b.prop('disabled', false).html('<i class="fa-solid fa-dice"></i> Generate variants');
                if (!data.success) {
                    $('#variationGallery').html('<div style="grid-column:1/-1;font-size:11px;color:var(--status-error);">' +
                        (data.error || 'Failed') + '</div>');
                    return;
                }
                if (data.detectedFamilies && data.detectedFamilies.length) {
                    $('#variationDetected').text('Detected: ' +
                        data.detectedFamilies.map(function (f) { return f.family + ' ' + f.percent + '%'; }).join(', '));
                }
                // Body-atlas cards keep their recolored slot URLs in the
                // index-keyed array; weapon cards carry the recolor instruction.
                bodyAtlasVariants = bodyAtlas ? (data.variants || []) : [];

                // Slot transparency: show which component slots resolved in the
                // MPQ and which were missing. data.slots = found slot indices.
                if (bodyAtlas) {
                    var SLOT_LABELS = {
                        0: 'Arm upper', 1: 'Arm lower', 2: 'Hand', 3: 'Torso upper',
                        4: 'Torso lower', 5: 'Leg upper', 6: 'Leg lower', 7: 'Foot'
                    };
                    var found = data.slots || [];
                    var foundSet = {};
                    found.forEach(function (s) { foundSet[s] = true; });
                    var parts = [];
                    for (var s = 0; s <= 7; s++) {
                        if (foundSet[s]) {
                            parts.push('<span style="color:var(--status-success,#3fb950);">' +
                                '<i class="fa-solid fa-check"></i> ' + SLOT_LABELS[s] + '</span>');
                        }
                    }
                    var missingLabels = [];
                    for (var s2 = 0; s2 <= 7; s2++) {
                        if (!foundSet[s2]) missingLabels.push(SLOT_LABELS[s2]);
                    }
                    var statusHtml = '<div style="display:flex;flex-wrap:wrap;gap:8px;margin-bottom:4px;">' +
                        parts.join('') + '</div>';
                    if (missingLabels.length) {
                        statusHtml += '<div style="color:var(--text-muted);">Not in MPQ (skipped): ' +
                            esc(missingLabels.join(', ')) + '</div>';
                    }
                    $('#bodyAtlasSlotStatus').html(statusHtml);
                }

                var html = '';
                (data.variants || []).forEach(function (v, i) {
                    var swapStr = Object.keys(v.swaps || {}).map(function (k) {
                        return k + '→' + v.swaps[k];
                    }).join(', ');
                    // Same .retex-card pattern as segmented — clickable card,
                    // selected ring, no Apply button. The whole card click
                    // previews-on-character and stages.
                    var common =
                        '<img class="retex-card-img" src="' + v.previewUrl + '" alt="" />' +
                        '<div class="retex-card-body">' +
                        '<div class="retex-card-title">' + esc(v.name) +
                        '<span class="retex-card-selectmark"><i class="fa-solid fa-circle-check"></i></span></div>' +
                        '<div class="retex-card-sub">' + esc(swapStr) + '</div>' +
                        '</div></div>';
                    if (bodyAtlas) {
                        // No instruction/GLB — the click paints v.slotUrls (looked
                        // up by data-idx) straight onto the body atlas.
                        html +=
                            '<div class="retex-card variation-card" data-idx="' + i + '" ' +
                            'data-mode="bodyatlas" ' +
                            'data-name="' + escAttr(v.name || ('Variant ' + (i + 1))) + '">' +
                            common;
                    } else {
                        html +=
                            '<div class="retex-card variation-card" data-idx="' + i + '" ' +
                            'data-name="' + escAttr(v.name || ('Variant ' + (i + 1))) + '" ' +
                            'data-instruction="' + escAttr(v.instruction) + '">' +
                            common;
                    }
                });
                $('#variationGallery').html(html ||
                    '<div style="grid-column:1/-1;font-size:11px;color:var(--text-muted);">No variants produced.</div>');
            },
            error: function () {
                $b.prop('disabled', false).html('<i class="fa-solid fa-dice"></i> Generate variants');
                $('#variationGallery').html('<div style="grid-column:1/-1;font-size:11px;color:var(--status-error);">Request failed</div>');
            }
        });
    });

    // ── Variations: click a card → stage it + push onto the character ───
    // Mirrors the segmented-card handler exactly. No DB write, no patch.
    // PreviewVariationGlb re-runs the brute-force palette swap from the
    // variant's instruction into a temp PNG, wraps it in a throwaway GLB,
    // and we mount that on the character viewer to the right via
    // equipWeaponGlbDirect. The selection is staged; it commits only on Save
    // (via /Items/CommitStagedRetexture → RetextureFromBitmapAsync on the
    // same temp PNG, so what was previewed is what gets persisted).
    $(document).on('click', '.variation-card', function () {
        var $card = $(this);

        // ── Body-atlas (painted armor): paint the recolored component slots
        //    straight onto the character. No GLB, no PreviewVariationGlb. ──
        if ($card.attr('data-mode') === 'bodyatlas') {
            var baIdx = parseInt($card.attr('data-idx'), 10);
            var bv = bodyAtlasVariants[baIdx];
            if (!bv || !bv.slotUrls) { showToast('Variant has no slots', 'error'); return; }

            $('.variation-card').removeClass('selected');
            $card.addClass('selected');

            if (window.cv && window.cv.character && window.cv.equip &&
                window.cv.equip.equipBodyAtlasRetextureDirect) {
                Promise.resolve(window.cv.equip.equipBodyAtlasRetextureDirect(
                    window.cv.character, bv.slotUrls
                )).catch(function (err) {
                    console.warn('[retex] body-atlas preview apply failed', err);
                    showToast('Couldn\u2019t apply preview to character', 'warning');
                });

                // Stage it (commit path for body-atlas is still TODO — this just
                // records the selection so the unsaved-dot shows and a future
                // Save handler can persist the recolored slots).
                stagedRetexture = {
                    displayId: currentDetailItem ? (currentDetailItem.display_id || 0) : 0,
                    itemName: currentDetailItem ? (currentDetailItem.name || '') : '',
                    name: $card.attr('data-name') || 'Variant',
                    inventoryType: currentDetailItem ? (currentDetailItem.inventory_type || 0) : 0,
                    slotUrls: bv.slotUrls,
                    swaps: bv.swaps,
                    mode: 'bodyatlas',
                    committed: false
                };
                $('.retex-unsaved-dot').css('display', 'inline-flex');
            } else {
                showToast('Character viewer not mounted — click an item first', 'warning');
            }
            return;
        }

        var instruction = $card.attr('data-instruction') || '';
        if (!instruction) {
            showToast('Variant has no instruction', 'error');
            return;
        }
        var displayId = currentDetailItem ? (currentDetailItem.display_id || 0) : 0;
        var mpqPath = $('#retextureGo').data('mpq');
        var filename = $('#retextureGo').data('filename');
        var itemName = currentDetailItem ? (currentDetailItem.name || '') : '';
        var name = $card.attr('data-name') || 'Variant';

        // Selected-state ring + spinner on the clicked card.
        $('.variation-card').removeClass('selected');
        $card.addClass('selected');
        var $img = $card.find('.retex-card-img');
        var prevOpacity = $img.css('opacity');
        $img.css('opacity', 0.5);

        $.ajax({
            url: '/Items/PreviewVariationGlb',
            method: 'POST',
            contentType: 'application/json',
            data: JSON.stringify({
                displayId: displayId,
                itemName: itemName,
                originalBlpFilename: filename,
                originalMpqPath: mpqPath,
                instruction: instruction
            }),
            success: function (data) {
                $img.css('opacity', prevOpacity || 1);
                if (!data.success || previewGlbUrls(data).length === 0) {
                    showToast('Preview failed: ' + (data.error || 'unknown'), 'error');
                    $card.removeClass('selected');
                    return;
                }
                previewGlbUrls(data).forEach(function (url) { stagedPreviewGlbs.push(url); });

                // Stage the selection (temporary — commits on Save).
                var inventoryType = currentDetailItem ? (currentDetailItem.inventory_type || 0) : 0;
                stagedRetexture = {
                    displayId: displayId,
                    mpqPath: mpqPath,
                    filename: filename,
                    itemName: itemName,
                    glbUrl: data.glbUrl,
                    attachments: data.attachments || null,
                    pngUrl: data.pngUrl,
                    pngPath: data.pngPath,
                    name: name,
                    inventoryType: inventoryType,
                    styleDirection: instruction,
                    mode: 'variation',
                    committed: false
                };
                $('.retex-unsaved-dot').css('display', 'inline-flex');

                if (window.retexEquipWeaponGlbDirect && window.cv && window.cv.character) {
                    Promise.resolve(window.retexEquipWeaponGlbDirect(
                        window.cv.character, data.glbUrl, inventoryType, data.attachments
                    )).catch(function (err) {
                        console.warn('[retex] character preview apply failed', err);
                        showToast('Couldn\u2019t apply preview to character', 'warning');
                    });
                } else {
                    showToast('Character viewer not mounted — click an item first', 'warning');
                }
            },
            error: function () {
                $img.css('opacity', prevOpacity || 1);
                $card.removeClass('selected');
                showToast('Preview request failed', 'error');
            }
        });
    });


    // Chain-to-AI / Flux-only checkbox toggles
    $(document).on('change', '#paletteChainAI', function () {
        if ($(this).is(':checked')) $('#paletteSkipBrute').prop('checked', false);
        $('#paletteAIDenoiseRow').toggle(
            $('#paletteChainAI').is(':checked') || $('#paletteSkipBrute').is(':checked'));
    });
    $(document).on('change', '#paletteSkipBrute', function () {
        if ($(this).is(':checked')) $('#paletteChainAI').prop('checked', false);
        $('#paletteAIDenoiseRow').toggle(
            $('#paletteChainAI').is(':checked') || $('#paletteSkipBrute').is(':checked'));
    });
    $(document).on('input', '#paletteAIDenoise', function () {
        $('#paletteAIDenoiseVal').text(($(this).val() / 100).toFixed(2));
    });

    // Denoise slider label update
    $(document).on('input', '#retexDenoise', function () {
        $('#retexDenoiseVal').text(($(this).val() / 100).toFixed(2));
    });

    // ── Stage a preview result on the character + show the 2D thumbnail ───
    // Shared between palette/scratch/modify paths. The 2D image stays in
    // #retextureStatus (info-dense, user wanted both); the 3D preview rides
    // the existing character viewer via retexEquipWeaponGlbDirect.
    // ctx carries the form data needed at commit time (displayId / itemName
    // / mpqPath / filename / mode / styleDirection).
    function stagePreviewResult(data, ctx) {
        if (!data || !data.success || previewGlbUrls(data).length === 0 || !data.pngPath) {
            showToast('Preview failed: ' + ((data && data.error) || 'unknown'), 'error');
            $('#retextureStatus').html(
                '<div style="font-size:11px;color:var(--status-error);">' +
                '<i class="fa-solid fa-triangle-exclamation"></i> ' +
                esc((data && data.error) || 'Preview failed') + '</div>');
            return;
        }

        previewGlbUrls(data).forEach(function (url) { stagedPreviewGlbs.push(url); });
        stagedRetexture = {
            displayId: ctx.displayId,
            mpqPath: ctx.mpqPath,
            filename: ctx.filename,
            itemName: ctx.itemName,
            glbUrl: data.glbUrl,
            attachments: data.attachments || null,
            pngUrl: data.pngUrl,
            pngPath: data.pngPath,
            name: ctx.name || (data.mode || 'Preview'),
            inventoryType: currentDetailItem ? (currentDetailItem.inventory_type || 0) : 0,
            styleDirection: ctx.styleDirection || '',
            mode: ctx.mode,
            committed: false
        };
        $('.retex-unsaved-dot').css('display', 'inline-flex');

        // 2D thumbnail in the status block (user explicitly wanted both).
        var html =
            '<div style="font-size:12px;color:var(--accent);font-weight:600;margin-bottom:8px;">' +
            '<i class="fa-solid fa-eye"></i> Preview ready — Save to keep</div>' +
            '<img src="' + esc(data.pngUrl) + '?t=' + Date.now() + '" ' +
            'style="max-width:100%;border-radius:4px;image-rendering:pixelated;margin-bottom:8px;" />' +
            '<div style="font-size:10px;color:var(--text-muted);">Mode: ' + esc(data.mode || ctx.mode) + '</div>';
        $('#retextureStatus').show().html(html);

        // Mount on the live character viewer.
        if (window.retexEquipWeaponGlbDirect && window.cv && window.cv.character) {
            Promise.resolve(window.retexEquipWeaponGlbDirect(
                window.cv.character, data.glbUrl, stagedRetexture.inventoryType,
                stagedRetexture.attachments
            )).catch(function (err) {
                console.warn('[retex] character preview apply failed', err);
                showToast('Couldn\u2019t apply preview to character', 'warning');
            });
        } else {
            showToast('Character viewer not mounted — click an item first', 'warning');
        }
    }

    $(document).on('click', '#retextureGo', function () {
        var $btn = $(this);
        var mpqPath = $btn.data('mpq');
        var filename = $btn.data('filename');
        var style = $('#retextureStyle').val() || '';
        var customPrompt = $('#retexturePrompt').val() || '';
        var itemName = currentDetailItem ? (currentDetailItem.name || '') : '';
        var displayId = currentDetailItem ? (currentDetailItem.display_id || 0) : 0;
        var mode = $('input[name="retexMode"]:checked').val();

        if (!displayId) {
            showToast('No item selected', 'error');
            return;
        }

        // Palette swap mode (vision-guided recolor → preview-on-character)
        if (mode === 'palette') {
            var instruction = $('#recolorInstruction').val() || '';
            if (!instruction.trim()) {
                showToast('Enter a recolor instruction', 'warning');
                return;
            }
            var chainToAI = $('#paletteChainAI').is(':checked');
            var skipBrute = $('#paletteSkipBrute').is(':checked');
            var aiDenoise = (chainToAI || skipBrute)
                ? parseInt($('#paletteAIDenoise').val()) / 100.0 : 0;

            $btn.prop('disabled', true).html('<i class="fa-solid fa-spinner fa-spin"></i> Previewing...');
            $('#retextureStatus').show().html(
                '<div style="font-size:11px;color:var(--text-muted);">' +
                '<i class="fa-solid fa-spinner fa-spin"></i> ' +
                (skipBrute ? 'Flux-only test (no brute force)... 30-60s.'
                    : chainToAI ? 'Brute-force draft + Flux polish... 30-60s.'
                        : 'Brute-force palette swap... 5-15s.') +
                '</div>');

            $.ajax({
                url: '/Items/PreviewPaletteGlb',
                method: 'POST',
                contentType: 'application/json',
                data: JSON.stringify({
                    displayId: displayId,
                    itemName: itemName,
                    originalBlpFilename: filename,
                    originalMpqPath: mpqPath,
                    instruction: instruction,
                    chainToAI: chainToAI,
                    skipBruteForce: skipBrute,
                    styleDirection: style,
                    aiDenoise: aiDenoise
                }),
                success: function (data) {
                    $btn.prop('disabled', false).html('<i class="fa-solid fa-wand-magic-sparkles"></i> Preview again');
                    stagePreviewResult(data, {
                        displayId: displayId, mpqPath: mpqPath, filename: filename,
                        itemName: itemName, styleDirection: instruction,
                        mode: 'palette', name: 'Palette swap'
                    });
                },
                error: function () {
                    $btn.prop('disabled', false).html('<i class="fa-solid fa-wand-magic-sparkles"></i> Retry');
                    $('#retextureStatus').html(
                        '<div style="font-size:11px;color:var(--status-error);">Request failed</div>');
                }
            });
            return;
        }

        // AI modes (scratch / modify → preview-on-character via Flux)
        var modifyExisting = mode === 'modify';
        var denoise = modifyExisting ? parseInt($('#retexDenoise').val()) / 100.0 : 1.0;

        $btn.prop('disabled', true).html('<i class="fa-solid fa-spinner fa-spin"></i> Previewing...');
        $('#retextureStatus').show().html(
            '<div style="font-size:11px;color:var(--text-muted);">' +
            '<i class="fa-solid fa-spinner fa-spin"></i> Sending to Ollama + Flux pipeline... This may take 30-60s.' +
            '</div>');

        $.ajax({
            url: '/Items/PreviewFluxGlb',
            method: 'POST',
            contentType: 'application/json',
            data: JSON.stringify({
                displayId: displayId,
                itemName: itemName,
                originalBlpFilename: filename,
                originalMpqPath: mpqPath,
                styleDirection: style,
                customPrompt: customPrompt || null,
                modifyExisting: modifyExisting,
                denoiseStrength: denoise
            }),
            success: function (data) {
                $btn.prop('disabled', false).html('<i class="fa-solid fa-wand-magic-sparkles"></i> Preview again');
                stagePreviewResult(data, {
                    displayId: displayId, mpqPath: mpqPath, filename: filename,
                    itemName: itemName, styleDirection: style,
                    mode: modifyExisting ? 'flux_img2img' : 'flux_txt2img',
                    name: modifyExisting ? 'Modify' : 'From scratch'
                });
            },
            error: function () {
                $btn.prop('disabled', false).html('<i class="fa-solid fa-wand-magic-sparkles"></i> Retry');
                $('#retextureStatus').html(
                    '<div style="font-size:11px;color:var(--status-error);">Request failed</div>');
            }
        });
    });

    function handleRetextureError() {
        $('#retextureGo').prop('disabled', false).html('<i class="fa-solid fa-wand-magic-sparkles"></i> Generate');
        $('#retextureStatus').html('<div style="font-size:11px;color:var(--status-error);">Request failed</div>');
    }

    function handleRetextureSuccess(data) {
        if (data.success) {
            var html = '<div style="font-size:12px;color:var(--status-online);font-weight:600;margin-bottom:8px;">' +
                '<i class="fa-solid fa-check"></i> Retexture complete!</div>';

            if (data.previewUrl) {
                html += '<img src="' + esc(data.previewUrl) + '?t=' + Date.now() + '" style="max-width:100%;border-radius:4px;image-rendering:pixelated;margin-bottom:8px;" />';
            }

            html += '<div style="font-size:10px;color:var(--text-muted);margin-bottom:4px;">' +
                (data.originalWidth || '?') + '×' + (data.originalHeight || '?') + ' ' + esc(data.originalFormat || '') +
                (data.blpSize ? ' · BLP: ' + (data.blpSize / 1024).toFixed(1) + 'KB' : '') + '</div>';

            if (data.mode) {
                html += '<div style="font-size:10px;color:var(--accent);margin-bottom:4px;">Mode: ' + esc(data.mode) + '</div>';
            }

            if (data.newDisplayId > 0) {
                html += '<div style="font-size:11px;color:var(--accent);margin-bottom:8px;font-weight:600;">' +
                    'New Display ID: ' + data.newDisplayId + '</div>';

                if (editMode && editEntry) {
                    html += '<button class="btn-sm btn-accent" id="btnApplyNewDisplayId" data-did="' + data.newDisplayId + '" style="margin-bottom:8px;">' +
                        '<i class="fa-solid fa-arrow-right"></i> Apply to this item</button> ';
                }
            }

            if (data.prompt) {
                html += '<div style="font-size:10px;color:var(--text-muted);margin-bottom:8px;font-style:italic;word-break:break-word;">' +
                    'Prompt: "' + esc(data.prompt.substring(0, 150)) + (data.prompt.length > 150 ? '...' : '') + '"</div>';
            }

            if (data.patchUrl) {
                var patchFile = data.patchUrl.split('/').pop();
                html += '<a href="/Items/DownloadPatch?file=' + encodeURIComponent(patchFile) + '" class="btn-sm btn-accent" download="' + esc(patchFile) + '" style="text-decoration:none;display:inline-block;">' +
                    '<i class="fa-solid fa-download"></i> Download ' + esc(patchFile) + '</a>';
            }

            $('#retextureStatus').html(html);
            $('#retextureGo').prop('disabled', false).html('<i class="fa-solid fa-wand-magic-sparkles"></i> Generate Another');
            checkPatchMAvailable();
        } else {
            $('#retextureStatus').html(
                '<div style="font-size:11px;color:var(--status-error);">' +
                '<i class="fa-solid fa-triangle-exclamation"></i> ' + esc(data.error || 'Unknown error') + '</div>');
            $('#retextureGo').prop('disabled', false).html('<i class="fa-solid fa-wand-magic-sparkles"></i> Retry');
        }
    }

    // ===================== ITEM CHANGELOG =====================

    function loadItemChangelog(entry) {
        if (!BaselineSystem.isInitialized()) {
            $('#itemChangelogPanel').hide();
            $('#itemResetContainer').hide();
            return;
        }

        BaselineSystem.loadItemDiff(entry, '#itemChangelogContent', function (data) {
            if (!data || !data.available || !data.hasOriginal) {
                if (entry >= CUSTOM_RANGE_START) {
                    $('#itemChangelogPanel').show();
                    $('#itemChangeCount').text('—').addClass('clean');
                } else {
                    $('#itemChangelogPanel').hide();
                }
                $('#itemResetContainer').hide();
                return;
            }

            $('#itemChangelogPanel').show();

            if (data.isModified) {
                var count = data.changes ? data.changes.length : 0;
                $('#itemChangeCount').text(count).removeClass('clean');
                $('#itemResetContainer').show();
            } else {
                $('#itemChangeCount').text('0').addClass('clean');
                $('#itemResetContainer').hide();
            }
        });
    }

    // ===================== EDIT FORM =====================

    function openEditPanel(sourceEntry, asClone) {
        // Fetch full row data
        $.getJSON('/Items/FullRow', { entry: sourceEntry }, function (data) {
            if (!data.found) {
                showToast('Item not found', 'error');
                return;
            }

            var item = data.item;
            editSourceEntry = sourceEntry;
            editIsClone = asClone;
            editIsBaseGame = !asClone && sourceEntry < CUSTOM_RANGE_START;
            editOriginalRow = item; // Stash full DB row as base for saves

            if (asClone) {
                // Get next custom ID
                $.getJSON('/Items/NextCustomId', function (idData) {
                    editEntry = idData.nextId;
                    renderEditForm(item, data.iconPath, data.modelPath);
                    showEditPanel();
                });
            } else {
                editEntry = sourceEntry;
                renderEditForm(item, data.iconPath, data.modelPath);
                showEditPanel();
            }
        });
    }

    function showEditPanel() {
        editMode = true;

        // Update header
        var name = $('#editFieldName').val() || 'New Item';
        $('#editHeaderName').text(name + (editIsClone ? ' (Clone)' : ''));

        var badge = $('#editBadge');
        if (editIsBaseGame) {
            badge.text('⚠ BASE GAME').addClass('base-game');
            $('#editPanel').addClass('base-game-mode');
            $('#editWarningBar').show();
        } else {
            badge.text('CUSTOM').removeClass('base-game');
            $('#editPanel').removeClass('base-game-mode');
            $('#editWarningBar').hide();
        }

        // Show edit panel, hide detail panel
        $('#colDetail').hide();
        $('#colEdit').show();

        // Mount the 3D preview only now that #colEdit is visible — renderEditForm builds its markup
        // while the column is still hidden, so mounting any earlier would size the canvas to 0x0.
        window.suiItemPreview?.mountPending(document.getElementById('editItemModelPreview'));

        // The detail panel's own preview is now off-screen; drop its GL context rather than leaving
        // it rendering behind a hidden column.
        window.suiItemPreview?.unmount(document.getElementById('detailContent'));

        // If a retexture open was requested from the detail view, fulfill it
        // now that #colEdit (which hosts the slide-in retexture panel) is
        // visible. Defer a tick so the column's layout is committed first.
        if (pendingRetexOpen) {
            var pend = pendingRetexOpen;
            pendingRetexOpen = null;
            setTimeout(function () { openRetexturePanel(pend); }, 0);
        }
    }

    function closeEditPanel() {
        editMode = false;
        editEntry = null;
        editIsClone = false;
        editIsBaseGame = false;
        editSourceEntry = null;
        editOriginalRow = null;

        // Reset retexture panel state — if it was slid in, slide it back
        // (no warn; closing the edit panel is an explicit exit). Any temp
        // preview GLBs get swept; staging is dropped.
        $('#colEdit').removeClass('retex-swap-active');
        if (stagedRetexture) cleanupStagedPreviewGlbs(null);
        stagedRetexture = null;

        // Tear the preview down before the column is hidden — an orphaned canvas keeps its WebGL
        // context and its rAF loop alive, and browsers cap live contexts at roughly 8–16.
        window.suiItemPreview?.unmount(document.getElementById('editItemModelPreview'));

        $('#colEdit').hide();
        $('#colDetail').show();
    }

    function renderEditForm(item, iconPath, modelPath) {
        var h = '';

        // ── Section 1: Identity ──
        h += sectionStart('identity', 'Identity', 'fa-tag', true);
        h += field('Name', '<input type="text" id="editFieldName" value="' + escAttr(item.name) + '" />');
        h += field('Quality', buildQualityDropdown(item.quality || 0));
        h += field('Icon / Appearance', buildIconPicker(iconPath, item.display_id || 0));

        // 3D model preview in edit form
        h += '<div class="edit-field"><label>3D Model <button type="button" class="btn-sm btn-outline-subtle" id="btnCheckItemModel" title="Check for 3D model" style="padding:1px 6px;font-size:10px;margin-left:6px;"><i class="fa-solid fa-cube"></i></button></label>';
        h += '<div id="editItemModelPreview">';
        if (modelPath) {
            // Mounted after showEditPanel() — see openEditPanel. The container is inside #colEdit,
            // which is still display:none while this markup is built, so mounting here would
            // initialize into a 0x0 box.
            h += '<div class="model-preview-container" style="height:180px;" data-sui-glb="' + escAttr(modelPath) + '"></div>';
        }
        h += '</div></div>';

        // Model Textures in edit form (retexture-capable)
        h += '<div class="edit-field"><label>Model Textures</label>';
        h += '<div id="editTextureContent"><div class="text-center p-2 text-muted" style="font-size:11px;">Loading textures...</div></div>';
        h += '</div>';

        h += field('Item Class', buildClassDropdown(item.class));
        h += field('Description', '<textarea id="editFieldDescription" placeholder="Orange flavor text shown in-game">' + esc(item.description || '') + '</textarea>');
        h += sectionEnd();

        // ── Section 2: Equipment & Stats ──
        h += sectionStart('equip', 'Equipment & Stats', 'fa-shield-halved', false);
        h += field('Inventory Slot', buildSlotDropdown(item.inventory_type));
        h += field('Item Level', '<input type="number" id="editFieldItemLevel" value="' + (item.item_level || 1) + '" min="1" max="100" />');
        h += '<div class="edit-field"><label>Stats</label><div id="statRowsContainer">';
        for (var i = 1; i <= 10; i++) {
            var st = item['stat_type' + i] || 0;
            var sv = item['stat_value' + i] || 0;
            if (st > 0 || sv !== 0)
                h += buildStatRow(i, st, sv);
        }
        h += '</div><button type="button" class="btn-add-row" id="btnAddStat"><i class="fa-solid fa-plus"></i> Add Stat</button></div>';
        h += field('Armor', '<input type="number" id="editFieldArmor" value="' + (item.armor || 0) + '" min="0" />');

        // Resistances (inline row)
        h += '<div class="edit-field"><label>Resistances</label><div class="edit-field-inline">';
        var resTypes = ['holy_res', 'fire_res', 'nature_res', 'frost_res', 'shadow_res', 'arcane_res'];
        var resLabels = ['Holy', 'Fire', 'Nature', 'Frost', 'Shadow', 'Arcane'];
        for (var r = 0; r < resTypes.length; r++) {
            h += '<div class="edit-field" style="flex: 0 0 auto;">' +
                '<label style="font-size:10px;">' + resLabels[r] + '</label>' +
                '<input type="number" class="editRes" data-col="' + resTypes[r] + '" value="' + (item[resTypes[r]] || 0) + '" min="0" style="width:54px;" />' +
                '</div>';
        }
        h += '</div></div>';
        h += sectionEnd();

        // ── Section 3: Weapon (only if class=2) ──
        var isWeapon = (item.class === 2);
        h += sectionStart('weapon', 'Weapon', 'fa-khanda', isWeapon);
        h += '<div class="edit-field-inline">';
        h += field('Damage Min', '<input type="number" id="editFieldDmgMin1" value="' + (item.dmg_min1 || 0) + '" min="0" />');
        h += field('Damage Max', '<input type="number" id="editFieldDmgMax1" value="' + (item.dmg_max1 || 0) + '" min="0" />');
        h += '</div>';
        h += '<div class="edit-field-inline">';
        h += field('Damage Type', buildDmgTypeDropdown(1, item.dmg_type1 || 0));
        h += field('Speed (sec)', '<input type="number" id="editFieldSpeed" value="' + ((item.delay || 2000) / 1000).toFixed(2) + '" min="0.1" step="0.1" />');
        h += '</div>';
        h += '<div class="edit-field"><label>DPS (calculated)</label><div id="dpsPreview" style="font-size: 13px; color: var(--text-secondary);">—</div></div>';

        // Second damage type (rare but supported)
        h += '<div class="edit-field-inline" style="margin-top:8px;">';
        h += field('Damage 2 Min', '<input type="number" id="editFieldDmgMin2" value="' + (item.dmg_min2 || 0) + '" min="0" />');
        h += field('Damage 2 Max', '<input type="number" id="editFieldDmgMax2" value="' + (item.dmg_max2 || 0) + '" min="0" />');
        h += '</div>';
        h += field('Damage 2 Type', buildDmgTypeDropdown(2, item.dmg_type2 || 0));
        h += sectionEnd();

        // ── Section 4: Spell Effects ──
        h += sectionStart('spells', 'Spell Effects', 'fa-bolt', false);
        h += '<div id="spellSlotsContainer">';
        for (var s = 1; s <= 5; s++) {
            var sid = item['spellid_' + s] || 0;
            var strig = item['spelltrigger_' + s] || 0;
            var scd = item['spellcooldown_' + s] || -1;
            var sch = item['spellcharges_' + s] || 0;
            if (sid > 0)
                h += buildSpellSlot(s, sid, strig, scd, sch);
        }
        h += '</div>';
        h += '<button type="button" class="btn-add-row" id="btnAddSpell"><i class="fa-solid fa-plus"></i> Add Spell Slot</button>';
        h += sectionEnd();

        // ── Section 5: Restrictions ──
        h += sectionStart('restrict', 'Restrictions', 'fa-lock', false);
        h += field('Required Level', '<input type="number" id="editFieldReqLevel" value="' + (item.required_level || 0) + '" min="0" max="60" />');
        h += field('Binding', buildBindingDropdown(item.bonding));
        h += '<div class="edit-field"><label>Allowed Classes</label>' + buildBitmaskGrid('class', WOW_CLASSES, item.allowable_class) + '</div>';
        h += '<div class="edit-field"><label>Allowed Races</label>' + buildBitmaskGrid('race', WOW_RACES, item.allowable_race) + '</div>';
        h += sectionEnd();

        // ── Section 6: Economics ──
        h += sectionStart('econ', 'Economics', 'fa-coins', false);
        h += field('Buy Price', buildPriceInputs('buy', item.buy_price || 0));
        h += field('Sell Price', buildPriceInputs('sell', item.sell_price || 0));
        h += '<div class="edit-field-inline">';
        h += field('Stack Size', '<input type="number" id="editFieldStackable" value="' + (item.stackable || 1) + '" min="1" />');
        h += field('Max Carry', '<input type="number" id="editFieldMaxCount" value="' + (item.max_count || 0) + '" min="0" />');
        h += '</div>';
        h += sectionEnd();

        // Delete button (only for custom items being edited, not clones)
        if (!editIsClone && editEntry >= CUSTOM_RANGE_START) {
            h += '<div style="margin-top: 16px; padding-top: 16px; border-top: 1px solid var(--border-light);">' +
                '<button type="button" class="btn-sm" id="btnDeleteItem" style="color: var(--status-error); background: none; border: 1px solid var(--status-error); border-radius: var(--radius-sm); padding: 4px 12px; font-size: 12px; cursor: pointer;">' +
                '<i class="fa-solid fa-trash"></i> Delete Item</button></div>';
        }

        $('#editFormContainer').html(h);

        // Update header icon
        $('#editHeaderIcon').attr('src', iconPath || '/Icon/Get?name=inv_misc_questionmark');

        // Load textures into edit form
        var editDisplayId = item.display_id || 0;
        if (editDisplayId > 0) {
            loadEditTextures(editDisplayId);
        }

        // Wire DPS preview
        updateDpsPreview();

        // Resolve spell names
        $('#spellSlotsContainer .spell-id-input').each(function () {
            var sid = parseInt($(this).val());
            if (sid > 0) resolveSpellName($(this).closest('.spell-slot-card'), sid);
        });
    }

    // ===================== FORM BUILDERS =====================

    function sectionStart(id, title, icon, open) {
        return '<div class="edit-section" data-section="' + id + '">' +
            '<div class="edit-section-header' + (open ? '' : ' collapsed') + '" data-target="' + id + '">' +
            '<i class="fa-solid ' + icon + '" style="color: var(--accent); font-size: 12px;"></i> ' + title +
            '<i class="fa-solid fa-chevron-down chevron"></i></div>' +
            '<div class="edit-section-body' + (open ? '' : ' collapsed') + '" data-body="' + id + '">';
    }

    function sectionEnd() {
        return '</div></div>';
    }

    function field(label, inner) {
        return '<div class="edit-field"><label>' + label + '</label>' + inner + '</div>';
    }

    function buildQualityDropdown(selected) {
        var h = '<select id="editFieldQuality">';
        for (var i = 0; i <= 6; i++) {
            h += '<option value="' + i + '" class="quality-option-' + i + '"' + (i === selected ? ' selected' : '') + '>' + QUALITY_NAMES[i] + '</option>';
        }
        return h + '</select>';
    }

    function buildClassDropdown(selected) {
        var h = '<select id="editFieldClass">';
        var keys = Object.keys(CLASS_NAMES).sort(function (a, b) { return +a - +b; });
        keys.forEach(function (k) {
            h += '<option value="' + k + '"' + (+k === selected ? ' selected' : '') + '>' + CLASS_NAMES[k] + '</option>';
        });
        return h + '</select>';
    }

    function buildSlotDropdown(selected) {
        var h = '<select id="editFieldSlot">';
        var keys = Object.keys(SLOT_NAMES).sort(function (a, b) { return +a - +b; });
        keys.forEach(function (k) {
            var label = SLOT_NAMES[k] || '(None)';
            h += '<option value="' + k + '"' + (+k === selected ? ' selected' : '') + '>' + label + '</option>';
        });
        return h + '</select>';
    }

    function buildBindingDropdown(selected) {
        var h = '<select id="editFieldBonding">';
        [0, 1, 2, 3, 4].forEach(function (v) {
            h += '<option value="' + v + '"' + (v === selected ? ' selected' : '') + '>' + BONDING_NAMES[v] + '</option>';
        });
        return h + '</select>';
    }

    function buildDmgTypeDropdown(index, selected) {
        var h = '<select id="editFieldDmgType' + index + '">';
        var keys = Object.keys(DMG_TYPE_NAMES).sort(function (a, b) { return +a - +b; });
        keys.forEach(function (k) {
            h += '<option value="' + k + '"' + (+k === selected ? ' selected' : '') + '>' + DMG_TYPE_NAMES[k] + '</option>';
        });
        return h + '</select>';
    }

    function buildIconPicker(iconPath, displayId) {
        return '<div class="icon-picker-trigger" id="iconPickerTrigger">' +
            '<img id="editIconPreview" src="' + esc(iconPath || '/Icon/Get?name=inv_misc_questionmark') + '" />' +
            '<div><div style="font-size: 13px; color: var(--text-primary);">Display ID: <span id="editDisplayIdLabel">' + (displayId || 0) + '</span></div>' +
            '<div class="change-text"><i class="fa-solid fa-images"></i> Change Icon</div></div>' +
            '<input type="hidden" id="editFieldDisplayId" value="' + (displayId || 0) + '" />' +
            '</div>';
    }

    function buildStatRow(index, statType, statValue) {
        return '<div class="stat-row" data-stat-index="' + index + '">' +
            '<select class="stat-type-select">' + buildStatTypeOptions(statType) + '</select>' +
            '<input type="number" class="stat-value-input" value="' + statValue + '" />' +
            '<button type="button" class="btn-remove-stat" title="Remove"><i class="fa-solid fa-xmark"></i></button>' +
            '</div>';
    }

    function buildStatTypeOptions(selected) {
        var h = '<option value="0">(None)</option>';
        var keys = Object.keys(STAT_TYPES).filter(function (k) { return +k > 0; }).sort(function (a, b) { return +a - +b; });
        keys.forEach(function (k) {
            h += '<option value="' + k + '"' + (+k === selected ? ' selected' : '') + '>' + STAT_TYPES[k] + '</option>';
        });
        return h;
    }

    function buildSpellSlot(index, spellId, trigger, cooldown, charges) {
        var cdSec = cooldown > 0 ? (cooldown / 1000).toFixed(0) : (cooldown === -1 ? '' : '0');
        return '<div class="spell-slot-card" data-spell-index="' + index + '">' +
            '<div class="spell-slot-header"><span>Spell Slot ' + index + '</span>' +
            '<button type="button" class="btn-remove-stat" title="Remove"><i class="fa-solid fa-xmark"></i></button></div>' +
            '<div class="edit-field-inline">' +
            '<div class="edit-field"><label>Spell ID</label><div class="d-flex gap-1">' +
            '<input type="number" class="spell-id-input" value="' + spellId + '" min="0" style="flex:1;" />' +
            '<a class="btn-sm btn-outline-subtle" title="Browse Spells" href="/Spells" target="_blank" style="flex-shrink:0; padding: 6px 8px;"><i class="fa-solid fa-magnifying-glass"></i></a>' +
            '</div><div class="spell-name-preview"></div></div>' +
            '<div class="edit-field"><label>Trigger</label><select class="spell-trigger-select">' + buildTriggerOptions(trigger) + '</select></div>' +
            '</div>' +
            '<div class="edit-field-inline" style="margin-top: 6px;">' +
            '<div class="edit-field"><label>Cooldown (sec)</label><input type="number" class="spell-cooldown-input" value="' + cdSec + '" min="0" placeholder="Use spell default" /></div>' +
            '<div class="edit-field"><label>Charges</label><input type="number" class="spell-charges-input" value="' + charges + '" /></div>' +
            '</div>' +
            '</div>';
    }

    function buildTriggerOptions(selected) {
        var h = '';
        var keys = Object.keys(TRIGGER_NAMES).sort(function (a, b) { return +a - +b; });
        keys.forEach(function (k) {
            h += '<option value="' + k + '"' + (+k === selected ? ' selected' : '') + '>' + TRIGGER_NAMES[k] + '</option>';
        });
        return h;
    }

    function buildBitmaskGrid(prefix, entries, value) {
        // value of -1 means "all allowed"
        var allSet = (value === -1 || value === undefined || value === null);
        var h = '<div style="margin-bottom: 4px;"><label style="display:flex; align-items:center; gap:5px; font-size:12px; font-weight:400; text-transform:none; letter-spacing:0; cursor:pointer;">' +
            '<input type="checkbox" class="bitmask-all" data-prefix="' + prefix + '"' + (allSet ? ' checked' : '') + ' /> <strong>All</strong></label></div>';
        h += '<div class="checkbox-grid">';
        entries.forEach(function (e) {
            var checked = allSet || ((value >> e.bit) & 1);
            h += '<label><input type="checkbox" class="bitmask-bit" data-prefix="' + prefix + '" data-bit="' + e.bit + '"' + (checked ? ' checked' : '') + ' /> ' + e.name + '</label>';
        });
        h += '</div>';
        return h;
    }

    function buildPriceInputs(prefix, copper) {
        var gold = Math.floor((copper || 0) / 10000);
        var silver = Math.floor(((copper || 0) % 10000) / 100);
        var cop = (copper || 0) % 100;
        return '<div class="price-inputs">' +
            '<div class="price-part"><input type="number" class="price-gold" data-prefix="' + prefix + '" value="' + gold + '" min="0" /><span class="coin-label coin-gold">g</span></div>' +
            '<div class="price-part"><input type="number" class="price-silver" data-prefix="' + prefix + '" value="' + silver + '" min="0" max="99" /><span class="coin-label coin-silver">s</span></div>' +
            '<div class="price-part"><input type="number" class="price-copper" data-prefix="' + prefix + '" value="' + cop + '" min="0" max="99" /><span class="coin-label coin-copper">c</span></div>' +
            '</div>';
    }

    // ===================== COLLECT FORM DATA =====================

    function collectFormData() {
        // Start with ALL original DB values as the base.
        // This ensures columns not represented in the form keep their original values
        // instead of being silently zeroed out.
        var data = {};

        if (editOriginalRow) {
            // Copy every column from the original row
            var keys = Object.keys(editOriginalRow);
            for (var k = 0; k < keys.length; k++) {
                data[keys[k]] = editOriginalRow[keys[k]];
            }
        }

        // Override entry (could be different if cloning)
        data.entry = editEntry;

        // ── Form overrides — only fields the UI actually controls ──

        // Identity
        data.name = $('#editFieldName').val() || 'Custom Item';
        data.quality = int('#editFieldQuality');
        data.display_id = int('#editFieldDisplayId');
        data['class'] = int('#editFieldClass');
        data.description = $('#editFieldDescription').val() || '';

        // Equipment & Stats
        data.inventory_type = int('#editFieldSlot');
        data.item_level = int('#editFieldItemLevel');
        data.armor = int('#editFieldArmor');

        // Resistances
        $('.editRes').each(function () {
            data[$(this).data('col')] = parseInt($(this).val()) || 0;
        });

        // Stats — collect in order from form rows
        var statIndex = 1;
        $('#statRowsContainer .stat-row').each(function () {
            var st = parseInt($(this).find('.stat-type-select').val()) || 0;
            var sv = parseInt($(this).find('.stat-value-input').val()) || 0;
            if (st > 0) {
                data['stat_type' + statIndex] = st;
                data['stat_value' + statIndex] = sv;
                statIndex++;
            }
        });
        // Zero out remaining stat slots (user removed them)
        for (var i = statIndex; i <= 10; i++) {
            data['stat_type' + i] = 0;
            data['stat_value' + i] = 0;
        }

        // Weapon
        data.dmg_min1 = intFloat('#editFieldDmgMin1');
        data.dmg_max1 = intFloat('#editFieldDmgMax1');
        data.dmg_type1 = int('#editFieldDmgType1');
        data.dmg_min2 = intFloat('#editFieldDmgMin2');
        data.dmg_max2 = intFloat('#editFieldDmgMax2');
        data.dmg_type2 = int('#editFieldDmgType2');
        var speed = parseFloat($('#editFieldSpeed').val()) || 2.0;
        data.delay = Math.round(speed * 1000);

        // Spells — only override slots that exist in the form
        for (var s = 1; s <= 5; s++) {
            var card = $('#spellSlotsContainer .spell-slot-card[data-spell-index="' + s + '"]');
            if (card.length) {
                data['spellid_' + s] = parseInt(card.find('.spell-id-input').val()) || 0;
                data['spelltrigger_' + s] = parseInt(card.find('.spell-trigger-select').val()) || 0;
                var cdVal = card.find('.spell-cooldown-input').val().trim();
                if (cdVal === '') {
                    // Empty = "use spell default" — preserve original DB value if we have it
                    var origCd = editOriginalRow ? editOriginalRow['spellcooldown_' + s] : null;
                    data['spellcooldown_' + s] = (origCd !== null && origCd !== undefined) ? origCd : -1;
                } else {
                    data['spellcooldown_' + s] = (parseInt(cdVal) || 0) * 1000;
                }
                data['spellcharges_' + s] = parseInt(card.find('.spell-charges-input').val()) || 0;
            } else {
                // Slot was removed or never added — zero it out only if the original had a spell here
                data['spellid_' + s] = 0;
                data['spelltrigger_' + s] = 0;
                // Preserve original cooldown if the original didn't have a spell either
                if (!editOriginalRow || !(editOriginalRow['spellid_' + s] > 0)) {
                    // No spell in original either — keep whatever original had
                    if (editOriginalRow && editOriginalRow['spellcooldown_' + s] !== undefined) {
                        data['spellcooldown_' + s] = editOriginalRow['spellcooldown_' + s];
                    } else {
                        data['spellcooldown_' + s] = 0;
                    }
                } else {
                    data['spellcooldown_' + s] = 0;
                }
                data['spellcharges_' + s] = 0;
            }
        }

        // Restrictions
        data.required_level = int('#editFieldReqLevel');
        data.bonding = int('#editFieldBonding');
        data.allowable_class = collectBitmask('class');
        data.allowable_race = collectBitmask('race');

        // Economics
        data.buy_price = collectPrice('buy');
        data.sell_price = collectPrice('sell');
        data.stackable = int('#editFieldStackable') || 1;
        data.max_count = int('#editFieldMaxCount');

        return data;
    }

    function int(sel) { return parseInt($(sel).val()) || 0; }
    function intFloat(sel) { return parseFloat($(sel).val()) || 0; }

    function collectBitmask(prefix) {
        if ($('.bitmask-all[data-prefix="' + prefix + '"]').is(':checked')) return -1;
        var val = 0;
        $('.bitmask-bit[data-prefix="' + prefix + '"]').each(function () {
            if ($(this).is(':checked')) val |= (1 << $(this).data('bit'));
        });
        return val || -1; // default to all if nothing checked
    }

    function collectPrice(prefix) {
        var g = parseInt($('.price-gold[data-prefix="' + prefix + '"]').val()) || 0;
        var s = parseInt($('.price-silver[data-prefix="' + prefix + '"]').val()) || 0;
        var c = parseInt($('.price-copper[data-prefix="' + prefix + '"]').val()) || 0;
        return g * 10000 + s * 100 + c;
    }

    // ===================== SAVE =====================

    function saveItem() {
        var data = collectFormData();

        if (!data.name || data.name.trim() === '') {
            showToast('Item name is required', 'error');
            return;
        }

        $('#btnSaveItem').prop('disabled', true).html('<i class="fa-solid fa-spinner fa-spin"></i> Saving...');

        // If there's a staged (preview-only) retexture, commit it FIRST — this
        // is the single point where the retexture is persisted to the DB and
        // patch-4.MPQ. The returned displayId is written onto the item so the
        // normal Save below assigns it. Nothing was persisted before this
        // moment.
        //
        // Commit endpoint depends on the mode:
        //   - Body-atlas (painted armor) → /Items/CommitBodyAtlasRetexture
        //                        (per-slot component BLPs + m_texture[0..7]).
        //   - Everything else  → /Items/CommitStagedRetexture (commits the
        //                        EXACT temp PNG that was previewed; pngPath is
        //                        validated server-side as being under
        //                        wwwroot/item_textures_cache/).
        if (hasUnsavedRetexture()) {
            $('#btnSaveItem').html('<i class="fa-solid fa-spinner fa-spin"></i> Committing retexture...');
            var commitUrl, commitBody;
            if (stagedRetexture.mode === 'bodyatlas') {
                // Painted armor: commit the per-slot recolored PNGs. The server
                // validates each slot path under item_textures_cache, encodes a
                // component BLP per slot, and patches m_texture[0..7].
                commitUrl = '/Items/CommitBodyAtlasRetexture';
                commitBody = {
                    displayId: stagedRetexture.displayId,
                    itemName: stagedRetexture.itemName,
                    styleDirection: stagedRetexture.name || '[body-atlas]',
                    slotUrls: stagedRetexture.slotUrls
                };
            } else {
                commitUrl = '/Items/CommitStagedRetexture';
                commitBody = {
                    displayId: stagedRetexture.displayId,
                    itemName: stagedRetexture.itemName,
                    originalBlpFilename: stagedRetexture.filename,
                    originalMpqPath: stagedRetexture.mpqPath,
                    pngPath: stagedRetexture.pngPath,
                    styleDirection: stagedRetexture.styleDirection || '',
                    mode: stagedRetexture.mode || 'staged_commit'
                };
            }
            $.ajax({
                url: commitUrl,
                method: 'POST',
                contentType: 'application/json',
                data: JSON.stringify(commitBody),
                success: function (res) {
                    if (!res.success || !(res.newDisplayId > 0)) {
                        $('#btnSaveItem').prop('disabled', false).html('<i class="fa-solid fa-floppy-disk"></i> Save');
                        showToast('Retexture commit failed: ' + (res.error || 'unknown'), 'error');
                        return;
                    }
                    // Mark committed so the close warning won't fire, point the
                    // item at the new displayId, and continue the normal save.
                    stagedRetexture.committed = true;
                    data.display_id = res.newDisplayId;
                    var $f = $('#editFieldDisplayId');
                    if ($f.length) $f.val(res.newDisplayId);
                    doActualSave(data);
                },
                error: function () {
                    $('#btnSaveItem').prop('disabled', false).html('<i class="fa-solid fa-floppy-disk"></i> Save');
                    showToast('Retexture commit failed — server error', 'error');
                }
            });
            return;
        }

        doActualSave(data);
    }

    function doActualSave(data) {
        $.ajax({
            url: '/Items/Save',
            method: 'POST',
            contentType: 'application/json',
            data: JSON.stringify(data),
            success: function (result) {
                if (result.success) {
                    showToast(result.isInsert ? 'Item #' + data.entry + ' created!' : 'Item #' + data.entry + ' saved!', 'success');
                    var savedEntry = data.entry;
                    // Reset save button before closing
                    $('#btnSaveItem').prop('disabled', false).html('<i class="fa-solid fa-floppy-disk"></i> Save');
                    // Clear every temp GLB (both authored shoulder sides, when
                    // present) now that the committed display is assigned.
                    cleanupStagedPreviewGlbs(null);
                    stagedRetexture = null;
                    // Close editor and return to browse mode
                    closeEditPanel();
                    // Refresh the search results to reflect changes
                    doSearch(currentPage);
                    // Show the saved item in the detail panel
                    loadDetail(savedEntry);
                } else {
                    $('#btnSaveItem').prop('disabled', false).html('<i class="fa-solid fa-floppy-disk"></i> Save');
                    showToast('Save failed: ' + (result.error || 'Unknown error'), 'error');
                }
            },
            error: function () {
                $('#btnSaveItem').prop('disabled', false).html('<i class="fa-solid fa-floppy-disk"></i> Save');
                showToast('Save failed — server error', 'error');
            }
        });
    }

    function deleteItem() {
        if (!editEntry || editEntry < CUSTOM_RANGE_START) return;
        if (!confirm('Delete this custom item permanently? This cannot be undone.')) return;

        $.post('/Items/Delete', { entry: editEntry }, function (result) {
            if (result.success) {
                showToast('Item #' + editEntry + ' deleted', 'success');
                closeEditPanel();
                doSearch(currentPage);
                $('#detailContent').html('<div class="text-center text-muted p-3">Item deleted</div>');
                $('#detailActions').hide();
            } else {
                showToast('Delete failed: ' + (result.error || 'Unknown error'), 'error');
            }
        });
    }

    // ===================== ICON PICKER =====================

    function openIconPicker() {
        iconPickerPage = 1;
        iconPickerQuery = '';
        $('#iconPickerSearch').val('');
        loadIconPickerPage();
        new bootstrap.Modal($('#iconPickerModal')[0]).show();

        // Focus search after modal opens
        setTimeout(function () { $('#iconPickerSearch').focus(); }, 300);
    }

    function loadIconPickerPage() {
        var params = { q: iconPickerQuery, page: iconPickerPage, pageSize: 60 };
        $('#iconPickerGrid').html('<div class="text-center p-4"><i class="fa-solid fa-spinner fa-spin"></i></div>');

        $.getJSON('/Items/IconSearch', params, function (data) {
            $('#iconPickerInfo').text(data.totalCount + ' icons found');
            $('#iconPickerPageInfo').text(data.page + ' / ' + data.totalPages);
            $('#btnIconPrevPage').prop('disabled', data.page <= 1);
            $('#btnIconNextPage').prop('disabled', data.page >= data.totalPages);

            var currentDisplayId = parseInt($('#editFieldDisplayId').val()) || 0;
            var h = '';
            data.icons.forEach(function (icon) {
                var isSelected = icon.displayIds.indexOf(currentDisplayId) >= 0;
                h += '<div class="icon-picker-cell' + (isSelected ? ' selected' : '') + '" ' +
                    'data-icon-name="' + escAttr(icon.iconName) + '" ' +
                    'data-display-ids="' + escAttr(JSON.stringify(icon.displayIds)) + '" ' +
                    'title="' + escAttr(icon.iconName) + ' (IDs: ' + icon.displayIds.slice(0, 5).join(', ') + (icon.displayIds.length > 5 ? '...' : '') + ')">' +
                    '<img src="' + esc(icon.iconPath) + '" loading="lazy" />' +
                    '</div>';
            });

            if (data.icons.length === 0)
                h = '<div class="text-center text-muted p-4">No icons match your search</div>';

            $('#iconPickerGrid').html(h);
        });
    }

    function selectIcon(cell) {
        var displayIds = JSON.parse($(cell).attr('data-display-ids') || '[]');
        var iconName = $(cell).data('icon-name');
        if (displayIds.length === 0) return;

        // Use the first displayId
        var displayId = displayIds[0];
        var iconPath = '/Icon/Get?name=' + iconName;

        $('#editFieldDisplayId').val(displayId);
        $('#editDisplayIdLabel').text(displayId);
        $('#editIconPreview').attr('src', iconPath);
        $('#editHeaderIcon').attr('src', iconPath);

        // Refresh 3D model for new display ID
        checkItemModel(displayId);

        bootstrap.Modal.getInstance($('#iconPickerModal')[0]).hide();
    }

    function checkItemModel(displayId) {
        var host = document.getElementById('editItemModelPreview');
        // Dispose BEFORE replacing the markup: this function wholesale-replaces the container the
        // preview mounted into, and an orphaned canvas keeps its WebGL context and rAF loop alive.
        window.suiItemPreview?.unmount(host);
        if (!displayId || displayId <= 0) {
            $('#editItemModelPreview').html('');
            return;
        }
        $.getJSON('/Items/ModelExists', { displayId: displayId }, function (data) {
            if (data.exists) {
                $('#editItemModelPreview').html(
                    '<div class="model-preview-container" style="height:180px;" data-sui-glb="' + escAttr(data.path) + '"></div>'
                );
                window.suiItemPreview?.mountPending(host);
            } else {
                $('#editItemModelPreview').html('');
            }
        });
    }

    // ===================== SPELL NAME RESOLUTION =====================

    function resolveSpellName(card, spellId) {
        if (!spellId || spellId <= 0) {
            card.find('.spell-name-preview').text('');
            return;
        }
        $.getJSON('/Spells/Detail', { entry: spellId }, function (data) {
            if (data.found) {
                var name = (data.item && data.item.name) || (data.spell && data.spell.name) || data.name || ('Spell #' + spellId);
                card.find('.spell-name-preview').text(name);
            } else {
                card.find('.spell-name-preview').text('Unknown spell');
            }
        }).fail(function () {
            card.find('.spell-name-preview').text('');
        });
    }

    // ===================== DPS PREVIEW =====================

    function updateDpsPreview() {
        var min = parseFloat($('#editFieldDmgMin1').val()) || 0;
        var max = parseFloat($('#editFieldDmgMax1').val()) || 0;
        var speed = parseFloat($('#editFieldSpeed').val()) || 2.0;
        if (speed > 0 && (min > 0 || max > 0)) {
            var dps = ((min + max) / 2) / speed;
            $('#dpsPreview').text(dps.toFixed(1) + ' DPS');
        } else {
            $('#dpsPreview').text('—');
        }
    }

    // ===================== HELPERS =====================

    function formatCopper(copper) {
        if (!copper || copper <= 0) return '0';
        var gold = Math.floor(copper / 10000);
        var silver = Math.floor((copper % 10000) / 100);
        var cop = copper % 100;
        var parts = [];
        if (gold > 0) parts.push(gold + 'g');
        if (silver > 0) parts.push(silver + 's');
        if (cop > 0) parts.push(cop + 'c');
        return parts.join(' ');
    }

    function esc(text) {
        if (text == null) return '';
        var div = document.createElement('div');
        div.textContent = String(text);
        return div.innerHTML;
    }

    function escAttr(text) {
        if (text == null) return '';
        return String(text).replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/'/g, '&#39;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
    }

    function showToast(msg, type) {
        var el = $('<div class="edit-toast ' + type + '">' + esc(msg) + '</div>');
        $('body').append(el);
        setTimeout(function () { el.fadeOut(300, function () { el.remove(); }); }, 3000);
    }

    // ===================== EVENTS =====================

    // Search
    $('#btnSearchItems').on('click', function () { doSearch(1); });
    $('#itemSearch').on('keydown', function (e) { if (e.key === 'Enter') doSearch(1); });
    $('#filterClass, #filterType, #filterSlot, #filterQuality, #filterSort, #filterDir, #filterCustomOnly, #filterHasDisplay')
        .on('change', function () { doSearch(1); });

    // Level boxes: debounce so typing "60" doesn't fire a search on "6".
    var levelTimer = null;
    $('#filterMinLvl, #filterMaxLvl, #filterMinIlvl, #filterMaxIlvl').on('input', function () {
        clearTimeout(levelTimer);
        levelTimer = setTimeout(function () { doSearch(1); }, 400);
    });

    $('#btnClearFilters').on('click', clearFilters);

    // Advanced row is collapsed until wanted — but opens itself if a stored
    // filter set is using it, so restored state is never invisible.
    $('#btnToggleAdvanced').on('click', function () {
        var open = $('#itemFilterAdvanced').toggle().is(':visible');
        $(this).find('.adv-chevron').toggleClass('fa-chevron-down', !open).toggleClass('fa-chevron-up', open);
    });

    // Populate the type dropdown, restore saved filters, and run the first search.
    buildTypeFilter();
    if (restoreFilters()) {
        var usesAdvanced = $('#filterMinLvl').val() || $('#filterMaxLvl').val() ||
            $('#filterMinIlvl').val() || $('#filterMaxIlvl').val() ||
            $('#filterCustomOnly').is(':checked') || $('#filterHasDisplay').is(':checked') ||
            ($('#filterSort').val() || 'entry') !== 'entry' || ($('#filterDir').val() || 'asc') !== 'asc';
        if (usesAdvanced) $('#btnToggleAdvanced').trigger('click');
    }
    renderFilterChips();

    // Pagination
    $('#btnFirstPage').on('click', function () { doSearch(1); });
    $('#btnPrevPage').on('click', function () { if (currentPage > 1) doSearch(currentPage - 1); });
    $('#btnNextPage').on('click', function () { if (currentPage < totalPages) doSearch(currentPage + 1); });
    $('#btnLastPage').on('click', function () { doSearch(totalPages); });
    $('#pageJumpInput').on('keydown', function (e) {
        if (e.key === 'Enter') {
            var p = parseInt($(this).val()) || 1;
            p = Math.max(1, Math.min(p, totalPages));
            doSearch(p);
        }
    });
    $('#pageJumpInput').on('blur', function () {
        var p = parseInt($(this).val()) || 1;
        p = Math.max(1, Math.min(p, totalPages));
        if (p !== currentPage) doSearch(p);
    });
    $('#pageSizeSelect').val(currentPageSize);
    $('#pageSizeSelect').on('change', function () {
        currentPageSize = parseInt($(this).val()) || 50;
        try { localStorage.setItem('msui_items_pageSize', currentPageSize); } catch (e) { }
        doSearch(1);
    });

    // Item list click → detail
    $(document).on('click', '.item-row', function () {
        if (editMode) return; // Don't switch items while editing
        $('.item-row').removeClass('active');
        $(this).addClass('active');
        loadDetail($(this).data('entry'));
    });

    // ── Clone button ──
    $('#btnCloneItem').on('click', function () {
        if (!currentDetailEntry) return;
        openEditPanel(currentDetailEntry, true);
    });

    // ── Detail icon click → open edit ──
    $(document).on('click', '.detail-icon-lg', function () {
        if (!currentDetailEntry || editMode) return;
        var isCustom = currentDetailEntry >= CUSTOM_RANGE_START;
        openEditPanel(currentDetailEntry, !isCustom);
    });

    // ── Edit Original button ──
    $('#btnEditOriginal').on('click', function () {
        if (!currentDetailEntry) return;
        var isCustom = currentDetailEntry >= CUSTOM_RANGE_START;

        if (isCustom) {
            // Custom items can be edited directly — no confirmation needed
            openEditPanel(currentDetailEntry, false);
        } else {
            // Show confirmation modal for base game items
            $('#confirmItemName').text(currentDetailItem ? currentDetailItem.name : 'this item');
            $('#confirmItemEntry').text('(Entry #' + currentDetailEntry + ')');
            new bootstrap.Modal($('#editOriginalModal')[0]).show();
        }
    });

    // Confirmation modal — Clone Instead
    $('#btnConfirmCloneInstead').on('click', function () {
        bootstrap.Modal.getInstance($('#editOriginalModal')[0]).hide();
        openEditPanel(currentDetailEntry, true);
    });

    // Confirmation modal — Edit Original confirmed
    $('#btnConfirmEditOriginal').on('click', function () {
        bootstrap.Modal.getInstance($('#editOriginalModal')[0]).hide();
        openEditPanel(currentDetailEntry, false);
    });

    // If the confirm modal is dismissed (X / backdrop / Cancel) WITHOUT entering
    // edit, drop any pending retexture-open so it doesn't auto-fire on a later
    // edit. The confirm buttons above route through showEditPanel, which
    // consumes pendingRetexOpen first, so this only clears genuine cancels.
    $(document).on('hidden.bs.modal', '#editOriginalModal', function () {
        if (!editMode) pendingRetexOpen = null;
    });

    // ── Save / Cancel ──
    $('#btnSaveItem').on('click', saveItem);
    $('#btnCancelEdit').on('click', closeEditPanel);

    // ── Delete ──
    $(document).on('click', '#btnDeleteItem', deleteItem);

    // ── Section toggle ──
    $(document).on('click', '.edit-section-header', function () {
        var target = $(this).data('target');
        $(this).toggleClass('collapsed');
        $('[data-body="' + target + '"]').toggleClass('collapsed');
    });

    // ── Add stat row ──
    $(document).on('click', '#btnAddStat', function () {
        var count = $('#statRowsContainer .stat-row').length;
        if (count >= 10) { showToast('Maximum 10 stats', 'error'); return; }
        $('#statRowsContainer').append(buildStatRow(count + 1, 0, 0));
    });

    // ── Remove stat row ──
    $(document).on('click', '.stat-row .btn-remove-stat', function () {
        $(this).closest('.stat-row').remove();
    });

    // ── Add spell slot ──
    $(document).on('click', '#btnAddSpell', function () {
        var count = $('#spellSlotsContainer .spell-slot-card').length;
        if (count >= 5) { showToast('Maximum 5 spell slots', 'error'); return; }
        var nextIndex = count + 1;
        // Reindex: find next unused
        for (var i = 1; i <= 5; i++) {
            if ($('#spellSlotsContainer .spell-slot-card[data-spell-index="' + i + '"]').length === 0) {
                nextIndex = i;
                break;
            }
        }
        $('#spellSlotsContainer').append(buildSpellSlot(nextIndex, 0, 0, -1, 0));
    });

    // ── Remove spell slot ──
    $(document).on('click', '.spell-slot-card .btn-remove-stat', function () {
        $(this).closest('.spell-slot-card').remove();
    });

    // ── Spell ID change → resolve name ──
    $(document).on('change', '.spell-id-input', function () {
        var card = $(this).closest('.spell-slot-card');
        var sid = parseInt($(this).val()) || 0;
        resolveSpellName(card, sid);
    });

    // ── Bitmask "All" checkbox ──
    $(document).on('change', '.bitmask-all', function () {
        var prefix = $(this).data('prefix');
        var checked = $(this).is(':checked');
        $('.bitmask-bit[data-prefix="' + prefix + '"]').prop('checked', checked);
    });

    // ── Individual bitmask checkbox ──
    $(document).on('change', '.bitmask-bit', function () {
        var prefix = $(this).data('prefix');
        var total = $('.bitmask-bit[data-prefix="' + prefix + '"]').length;
        var checked = $('.bitmask-bit[data-prefix="' + prefix + '"]:checked').length;
        $('.bitmask-all[data-prefix="' + prefix + '"]').prop('checked', checked === total);
    });

    // ── Icon picker trigger ──
    $(document).on('click', '#iconPickerTrigger', openIconPicker);

    // ── Icon picker search ──
    var iconSearchTimer = null;
    $('#iconPickerSearch').on('input', function () {
        clearTimeout(iconSearchTimer);
        iconSearchTimer = setTimeout(function () {
            iconPickerQuery = $('#iconPickerSearch').val();
            iconPickerPage = 1;
            loadIconPickerPage();
        }, 300);
    });

    // ── Icon picker pagination ──
    $('#btnIconPrevPage').on('click', function () {
        if (iconPickerPage > 1) { iconPickerPage--; loadIconPickerPage(); }
    });
    $('#btnIconNextPage').on('click', function () {
        iconPickerPage++;
        loadIconPickerPage();
    });

    // ── Icon picker selection ──
    $(document).on('click', '.icon-picker-cell', function () {
        selectIcon(this);
    });

    // ── DPS live update ──
    $(document).on('input', '#editFieldDmgMin1, #editFieldDmgMax1, #editFieldSpeed', updateDpsPreview);

    // ── Check for 3D model button ──
    $(document).on('click', '#btnCheckItemModel', function () {
        var did = parseInt($('#editFieldDisplayId').val()) || 0;
        if (did <= 0) { showToast('No Display ID set', 'error'); return; }
        checkItemModel(did);
    });

    // ── Apply retextured displayId to item in edit mode ──
    $(document).on('click', '#btnApplyNewDisplayId', function () {
        var newDid = $(this).data('did');
        if (!newDid || !editMode) return;

        $('#editFieldDisplayId').val(newDid);
        // Update the icon picker display too
        var $trigger = $('.icon-picker-trigger');
        if ($trigger.length) {
            $trigger.find('.change-text').text('Display ID: ' + newDid);
        }

        showToast('Display ID updated to ' + newDid + ' — Save to apply', 'success');
        closeRetexturePanel({ force: true });

        // Reload textures and model preview for the new displayId
        loadEditTextures(newDid);
    });

    // ── Changelog toggle ──
    $('#itemChangelogToggle').on('click', function () {
        $(this).toggleClass('collapsed');
        $('#itemChangelogBody').toggleClass('collapsed');
    });

    // ── Texture panel toggle ──
    $('#itemTextureToggle').on('click', function (e) {
        if ($(e.target).closest('.btn-download-patch').length) return;
        $(this).toggleClass('collapsed');
        $('#itemTextureBody').toggleClass('collapsed');
    });

    $('#itemSourcesToggle').on('click', function () {
        $(this).toggleClass('collapsed');
        $('#itemSourcesBody').toggleClass('collapsed');
    });

    // ── Reset to OG ──
    $('#btnResetItemOG').on('click', function () {
        if (!currentDetailEntry || currentDetailEntry >= CUSTOM_RANGE_START) return;
        BaselineSystem.resetItem(currentDetailEntry, function (success) {
            if (success) {
                loadDetail(currentDetailEntry);
                doSearch(currentPage);
            }
        });
    });

    // ── Name change → update header ──
    $(document).on('input', '#editFieldName', function () {
        $('#editHeaderName').text($(this).val() || 'New Item');
    });

    // ── Quality change → update header color ──
    $(document).on('change', '#editFieldQuality', function () {
        var q = parseInt($(this).val()) || 0;
        $('#editHeaderName').css('color', QUALITY_COLORS[q] || 'inherit');
    });

    // ═══════════════════════════════════════════════════════════════
    //  LOOTIFIER RETEXTURE QUEUE
    //
    //  One place to recolor every Lootifier's variants. Pick the sources
    //  (Quest / Crafting / Loot-ARPG), pick a theme per colour tier, and it
    //  queues ONE recolor per (base item × tier) — every variant in that tier
    //  shares the resulting display_id. Lives here on the Items page so it
    //  reuses the existing retexture + patch pipeline: one patch, one download.
    // ═══════════════════════════════════════════════════════════════

    var lrqPolling = false;

    function lrqEnsureModal() {
        if ($('#lootRetextureModal').length) return;

        var html =
            '<div class="modal fade" id="lootRetextureModal" tabindex="-1">' +
            '<div class="modal-dialog modal-lg">' +
            '<div class="modal-content">' +
            '<div class="modal-header">' +
            '<h5 class="modal-title"><i class="fa-solid fa-palette"></i> Lootifier Variant Retexture</h5>' +
            '<button type="button" class="btn-close" data-bs-dismiss="modal"></button>' +
            '</div>' +
            '<div class="modal-body">' +
            '<p class="text-muted" style="font-size:12px;">' +
            'Queues <strong>one recolor per colour tier per item</strong> — every variant in a tier shares the ' +
            'resulting display. Recolors run as hard palette swaps, so no vision model is required.' +
            '</p>' +
            '<div id="lrqSources" class="mb-3"><div class="text-muted">Loading sources…</div></div>' +
            '<div class="mb-2" style="font-size:12px;font-weight:600;">Theme per tier</div>' +
            '<div class="row g-2 mb-3" id="lrqThemes">' +
            lrqThemeField('improved', 'Improved') +
            lrqThemeField('power', 'of Power') +
            lrqThemeField('glory', 'of Glory') +
            lrqThemeField('gods', 'of the Gods / Legendary') +
            '</div>' +
            '<div class="form-check mb-3">' +
            '<input class="form-check-input" type="checkbox" id="lrqRequeue">' +
            '<label class="form-check-label" for="lrqRequeue" style="font-size:12px;">' +
            'Requeue items that already have jobs (otherwise they are skipped)</label>' +
            '</div>' +
            '<div id="lrqQueueBox" class="p-2 mb-2" style="border:1px solid rgba(128,128,128,.28);border-radius:8px;font-size:12px;">' +
            '<span id="lrqQueueText" class="text-muted">Queue: —</span>' +
            '<div class="progress mt-2" style="height:6px;display:none;" id="lrqBarWrap">' +
            '<div class="progress-bar" id="lrqBar" style="width:0%"></div>' +
            '</div>' +
            '</div>' +
            '<div id="lrqFailures" style="font-size:11px;"></div>' +
            '</div>' +
            '<div class="modal-footer">' +
            '<button class="btn btn-sm btn-outline-secondary" id="lrqResetBtn">Requeue failed</button>' +
            '<button class="btn btn-sm btn-outline-danger" id="lrqClearBtn">Clear queue</button>' +
            '<button class="btn btn-sm btn-secondary" id="lrqBuildBtn"><i class="fa-solid fa-layer-group"></i> Build Queue</button>' +
            '<button class="btn btn-sm btn-primary" id="lrqRunBtn"><i class="fa-solid fa-play"></i> Run Queue</button>' +
            '</div>' +
            '</div>' +
            '</div>' +
            '</div>';

        $('body').append(html);
    }

    function lrqThemeField(tier, label) {
        return '<div class="col-md-6">' +
            '<label class="form-label" style="font-size:11px;">' + label + '</label>' +
            '<input type="text" class="form-control form-control-sm lrq-theme" data-tier="' + tier + '" placeholder="server default" />' +
            '</div>';
    }

    function lrqLoadSources() {
        $.getJSON('/Items/LootifierRetextureSources', function (d) {
            if (!d.success) { $('#lrqSources').html('<div class="text-danger">' + (d.error || 'Failed to load') + '</div>'); return; }

            if (d.defaultThemes) {
                $('.lrq-theme').each(function () {
                    var t = $(this).data('tier');
                    if (d.defaultThemes[t]) $(this).attr('placeholder', d.defaultThemes[t]);
                });
            }

            var rows = (d.sources || []).map(function (s) {
                var none = s.bases === 0;
                return '<div class="form-check">' +
                    '<input class="form-check-input lrq-src" type="checkbox" value="' + s.source + '" id="lrqSrc_' + s.source + '"' +
                    (none ? ' disabled' : ' checked') + '>' +
                    '<label class="form-check-label" for="lrqSrc_' + s.source + '" style="font-size:12px;">' +
                    '<strong>' + s.label + '</strong> — ' + s.bases + ' base items, ' + s.variants + ' variants' +
                    (s.queued ? ' <span class="text-muted">(' + s.done + '/' + s.queued + ' retextured)</span>' : '') +
                    '</label></div>';
            }).join('');

            $('#lrqSources').html(rows || '<div class="text-muted">No lootifier variants found yet.</div>');
            lrqRefreshQueue();
        });
    }

    function lrqSelectedSources() {
        return $('.lrq-src:checked').map(function () { return this.value; }).get();
    }

    function lrqThemes() {
        var t = {};
        $('.lrq-theme').each(function () {
            var v = ($(this).val() || '').trim();
            if (v) t[$(this).data('tier')] = v;
        });
        return t;
    }

    function lrqRefreshQueue(cb) {
        $.getJSON('/Items/RetextureQueueStatus', function (d) {
            if (!d.success) return;
            var total = d.pending + d.done + d.failed;
            $('#lrqQueueText').text(
                'Queue: ' + d.pending + ' pending · ' + d.done + ' done' +
                (d.failed ? ' · ' + d.failed + ' failed' : '') +
                (d.llmAssistAvailable ? '' : '  (no LLM — using hard palette swaps)'));

            if (total > 0) {
                $('#lrqBarWrap').show();
                $('#lrqBar').css('width', Math.round(((d.done + d.failed) / total) * 100) + '%');
            } else {
                $('#lrqBarWrap').hide();
            }

            if (d.failures && d.failures.length) {
                $('#lrqFailures').html('<div class="text-danger mt-1">' +
                    d.failures.slice(0, 5).map(function (f) {
                        return esc(f.itemName) + ' [' + f.tier + ']: ' + esc(f.error || 'failed');
                    }).join('<br>') + '</div>');
            } else {
                $('#lrqFailures').empty();
            }

            if (cb) cb(d);
        });
    }

    // Process the queue a few jobs at a time — each is slow (recolor → BLP →
    // patch), so we chain small batches and let the bar advance.
    function lrqRunQueue() {
        if (lrqPolling) return;
        lrqPolling = true;
        $('#lrqRunBtn').prop('disabled', true).html('<i class="fa-solid fa-spinner fa-spin"></i> Running…');

        function step() {
            $.ajax({
                url: '/Items/ProcessRetextureQueue',
                method: 'POST',
                contentType: 'application/json',
                data: JSON.stringify({ max: 3 })
            }).done(function (r) {
                if (!r.success) {
                    showToast(r.error || 'Retexture failed', 'error');
                    return lrqStop();
                }
                lrqRefreshQueue();
                if (r.remaining > 0) { step(); }
                else {
                    showToast('Retexture queue complete', 'success');
                    lrqStop();
                }
            }).fail(function () {
                showToast('Retexture request failed', 'error');
                lrqStop();
            });
        }

        function lrqStop() {
            lrqPolling = false;
            $('#lrqRunBtn').prop('disabled', false).html('<i class="fa-solid fa-play"></i> Run Queue');
            lrqRefreshQueue();
        }

        step();
    }

    $(document).on('click', '#lrqBuildBtn', function () {
        var sources = lrqSelectedSources();
        if (sources.length === 0) { showToast('Pick at least one source', 'warning'); return; }

        var $b = $(this).prop('disabled', true);
        $.ajax({
            url: '/Items/BuildRetextureQueue',
            method: 'POST',
            contentType: 'application/json',
            data: JSON.stringify({
                sources: sources,
                themes: lrqThemes(),
                requeue: $('#lrqRequeue').is(':checked')
            })
        }).done(function (r) {
            if (!r.success) { showToast(r.error || 'Build failed', 'error'); return; }
            showToast('Queued ' + r.queued + ' retexture jobs across ' + r.basesCovered + ' items' +
                (r.skipped ? ' (' + r.skipped + ' already queued)' : '') +
                (r.noDisplay ? ' — ' + r.noDisplay + ' skipped: no display_id' : ''), 'success');
            lrqRefreshQueue();
        }).fail(function () {
            showToast('Build request failed', 'error');
        }).always(function () { $b.prop('disabled', false); });
    });

    $(document).on('click', '#lrqRunBtn', lrqRunQueue);

    $(document).on('click', '#lrqResetBtn', function () {
        $.ajax({
            url: '/Items/ResetRetextureQueue', method: 'POST',
            contentType: 'application/json', data: JSON.stringify({ clear: false })
        }).done(function (r) {
            showToast('Requeued ' + (r.affected || 0) + ' failed jobs', 'info');
            lrqRefreshQueue();
        });
    });

    $(document).on('click', '#lrqClearBtn', function () {
        if (!confirm('Clear the entire retexture queue? Already-applied retextures stay applied.')) return;
        $.ajax({
            url: '/Items/ResetRetextureQueue', method: 'POST',
            contentType: 'application/json', data: JSON.stringify({ clear: true })
        }).done(function (r) {
            showToast('Cleared ' + (r.affected || 0) + ' queue rows', 'info');
            lrqRefreshQueue();
        });
    });

    // Open the modal (self-mounts a toolbar button if the view has no hook).
    $(document).on('click', '#btnLootifierRetexture', function () {
        lrqEnsureModal();
        lrqLoadSources();
        new bootstrap.Modal($('#lootRetextureModal')[0]).show();
    });

    (function lrqMountButton() {
        if ($('#btnLootifierRetexture').length) return;
        var btn = '<button id="btnLootifierRetexture" class="btn btn-sm btn-outline-secondary ms-2" ' +
            'title="Recolor Lootifier variants by colour tier">' +
            '<i class="fa-solid fa-palette"></i> Lootifier Retexture</button>';
        var $anchor = $('#btnPatchStatus, #btnDownloadPatch').first();
        if ($anchor.length) $anchor.after(btn);
        else $('.card-header, .page-header, h1, h2').first().append(btn);
    })();

    // ===================== INIT =====================
    doSearch(1);

});
