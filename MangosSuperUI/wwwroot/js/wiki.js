/* ============================================================
   MangosSuperUI — wiki.js
   The code-docs wiki client. Renders the SourceMapper corpus:
   a browsable page tree, a rendered article with an on-this-page
   ToC, and cross-references (Unit/Member) that navigate in-place.
   ============================================================ */

(function () {
    'use strict';

    var API = {
        tree: '/Wiki/Tree',
        page: '/Wiki/Page',
        stats: '/Wiki/Stats'
    };

    var els = {};
    var state = { path: null, tree: null };

    // ---------------------------------------------------------- utilities

    function el(id) { return document.getElementById(id); }
    function esc(s) {
        return String(s == null ? '' : s)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }
    function icon(name) { return '<i class="fa-solid ' + name + '"></i>'; }

    function getJSON(url) {
        return fetch(url, { headers: { 'Accept': 'application/json' } })
            .then(function (r) {
                if (!r.ok) throw new Error(r.status + ' ' + r.statusText);
                return r.json();
            });
    }

    // ---------------------------------------------------------- the tree

    function renderTree(tree) {
        state.tree = tree;
        els.pageCount.textContent = tree.pageCount;
        els.tree.innerHTML = '';
        if (!tree.children || tree.children.length === 0) {
            els.tree.innerHTML = '<div class="wiki-tree-empty">No pages found.</div>';
            return;
        }
        var frag = document.createDocumentFragment();
        tree.children.forEach(function (node) { frag.appendChild(buildNode(node, 0)); });
        els.tree.appendChild(frag);
    }

    function buildNode(node, depth) {
        if (node.type === 'dir') {
            var wrap = document.createElement('div');
            wrap.className = 'wiki-tree-dir';
            wrap.dataset.name = node.name.toLowerCase();

            var head = document.createElement('button');
            head.className = 'wiki-tree-row wiki-tree-dirrow';
            head.style.paddingLeft = (8 + depth * 14) + 'px';
            head.innerHTML =
                '<span class="wiki-tree-chev">' + icon('fa-chevron-right') + '</span>' +
                '<span class="wiki-tree-name">' + esc(node.name) + '</span>';
            head.addEventListener('click', function () { wrap.classList.toggle('open'); });
            wrap.appendChild(head);

            var kids = document.createElement('div');
            kids.className = 'wiki-tree-kids';
            (node.children || []).forEach(function (c) { kids.appendChild(buildNode(c, depth + 1)); });
            wrap.appendChild(kids);
            return wrap;
        }

        var a = document.createElement('a');
        a.className = 'wiki-tree-row wiki-tree-page';
        a.style.paddingLeft = (8 + depth * 14 + 14) + 'px';
        a.href = '/Wiki?path=' + encodeURIComponent(node.path);
        a.dataset.wikiPath = node.path;
        a.dataset.name = (node.label || node.name).toLowerCase();
        a.innerHTML =
            '<span class="wiki-tree-dot"></span>' +
            '<span class="wiki-tree-name">' + esc(node.name) + '</span>';
        a.addEventListener('click', function (e) {
            if (e.metaKey || e.ctrlKey || e.button === 1) return;  // let new-tab through
            e.preventDefault();
            navigate(node.path, null, true);
            openDrawer(false);   // picking a page closes the browse drawer
        });
        return a;
    }

    function markActive(path) {
        var prev = els.tree.querySelector('.wiki-tree-page.active');
        if (prev) prev.classList.remove('active');
        if (!path) return;
        var a = els.tree.querySelector('.wiki-tree-page[data-wiki-path="' + cssEsc(path) + '"]');
        if (!a) return;
        a.classList.add('active');
        // expand ancestor folders
        var p = a.parentElement;
        while (p && p !== els.tree) {
            if (p.classList && p.classList.contains('wiki-tree-dir')) p.classList.add('open');
            p = p.parentElement;
        }
        a.scrollIntoView({ block: 'nearest' });
    }

    function cssEsc(s) { return String(s).replace(/["\\]/g, '\\$&'); }

    // -------------------------------------------------------- filtering

    function applyFilter(q) {
        q = q.trim().toLowerCase();
        var pages = els.tree.querySelectorAll('.wiki-tree-page');
        var dirs = els.tree.querySelectorAll('.wiki-tree-dir');

        if (!q) {
            pages.forEach(function (p) { p.classList.remove('hidden'); });
            dirs.forEach(function (d) { d.classList.remove('hidden'); d.classList.remove('filter-open'); });
            markActive(state.path);
            return;
        }
        pages.forEach(function (p) {
            var hit = p.dataset.name.indexOf(q) !== -1;
            p.classList.toggle('hidden', !hit);
        });
        // a folder shows (and force-opens) iff it has a visible descendant page
        dirs.forEach(function (d) {
            var anyVisible = d.querySelector('.wiki-tree-page:not(.hidden)') != null;
            d.classList.toggle('hidden', !anyVisible);
            d.classList.toggle('filter-open', anyVisible);
        });
    }

    // ------------------------------------------------------- the article

    function showState(kind, html) {
        els.article.className = 'wiki-article state-' + kind;
        els.article.innerHTML = html;
        setContentsEmpty(true);
    }

    function renderLanding(stats) {
        var updated = stats.lastUpdated ? new Date(stats.lastUpdated).toLocaleString() : '—';
        showState('landing',
            '<div class="wiki-landing">' +
            '<div class="wiki-landing-mark">' + icon('fa-book-open') + '</div>' +
            '<h2>' + esc(stats.root) + '</h2>' +
            '<p>Browse the engine one unit at a time, or open a page and follow the ' +
            'cross-references. Every <code>Unit/Member</code> reference in a page links ' +
            'to where it lives.</p>' +
            '<div class="wiki-landing-stats">' +
            '<div><b>' + stats.pageCount + '</b><span>pages</span></div>' +
            '<div><b>' + stats.folderCount + '</b><span>folders</span></div>' +
            '<div><b>' + esc(updated) + '</b><span>last generated</span></div>' +
            '</div>' +
            (stats.ready ? '<p class="wiki-landing-hint">Pick a page on the left to start.</p>'
                : '<p class="wiki-landing-hint err">No corpus found at the configured Wiki:Root.</p>') +
            '</div>');
    }

    function renderError(path, msg) {
        showState('error',
            '<div class="wiki-msg">' +
            icon('fa-triangle-exclamation') +
            '<h2>Couldn\u2019t open that page</h2>' +
            '<p>' + esc(path) + '</p>' +
            '<p class="wiki-msg-detail">' + esc(msg) + '</p>' +
            '<button class="btn btn-sm" id="wikiRetry">' + icon('fa-rotate-right') + ' Retry</button>' +
            '</div>');
        var b = el('wikiRetry');
        if (b) b.addEventListener('click', function () { navigate(path, null, false); });
    }

    function infoboxMarkup(ib) {
        if (!ib || !ib.facts || ib.facts.length === 0) return '';
        var rows = ib.facts.map(function (f) {
            return '<div class="wiki-ib-row"><dt>' + esc(f.label) + '</dt><dd>' + esc(f.value) + '</dd></div>';
        }).join('');
        return '<aside class="wiki-infobox">' +
            '<div class="wiki-ib-title">' + esc(ib.title) + '</div>' +
            '<dl>' + rows + '</dl>' +
            '</aside>';
    }

    function renderPage(page, anchor) {
        var badge = '';
        if (page.provenance === 'model') {
            badge = '<span class="wiki-badge model" title="Written by the local model from source — review before trusting">' +
                '<span class="wiki-badge-led"></span>model-written</span>';
        } else if (page.provenance === 'failed') {
            badge = '<span class="wiki-badge failed" title="The model call failed for this unit">' +
                '<span class="wiki-badge-led"></span>generation failed</span>';
        }

        var crumbs = page.breadcrumbs.map(function (c, i, arr) {
            var last = i === arr.length - 1;
            if (last || !c.path) return '<span>' + esc(c.name) + '</span>';
            return '<a href="/Wiki?path=' + encodeURIComponent(c.path) + '" data-wiki-path="' + esc(c.path) + '">' + esc(c.name) + '</a>';
        }).join('<span class="wiki-crumb-sep">/</span>');

        var infobox = infoboxMarkup(page.infobox);

        els.article.className = 'wiki-article state-page';
        els.article.innerHTML =
            '<div class="wiki-doc">' +
            '<div class="wiki-page-head">' +
            '<nav class="wiki-crumbs">' + crumbs + '</nav>' +
            '<div class="wiki-page-titlerow">' +
            '<h1 class="wiki-page-title">' + esc(page.title) + '</h1>' +
            badge +
            '</div>' +
            '</div>' +
            '<div class="wiki-md" id="wikiMd">' + infobox + page.html + '</div>' +
            '</div>';

        buildToc(page.toc);

        // scroll to anchor (member deep-link) or top
        if (anchor) scrollToAnchor(anchor);
        else els.article.scrollTop = 0;
    }

    function buildToc(toc) {
        var body = els.contents;
        if (!toc || toc.length === 0) {
            body.innerHTML = '';
            setContentsEmpty(true);      // nothing to show -> collapse the rail
            return;
        }
        setContentsEmpty(false);
        var html = '<ul><li class="lvl-top"><a href="#" data-toc="__top">(Top)</a></li>';
        toc.forEach(function (t) {
            html += '<li class="lvl-' + t.level + '"><a href="#' + t.anchor + '" data-toc="' + t.anchor + '">' + esc(t.text) + '</a></li>';
        });
        html += '</ul>';
        body.innerHTML = html;
        body.querySelectorAll('a[data-toc]').forEach(function (a) {
            a.addEventListener('click', function (e) {
                e.preventDefault();
                var id = a.getAttribute('data-toc');
                if (id === '__top') { els.article.scrollTop = 0; history.replaceState(history.state, '', location.pathname + location.search); }
                else scrollToAnchor(id, true);
            });
        });
    }

    function scrollToAnchor(id, updateHash) {
        var target = document.getElementById(id);
        if (!target) return;
        target.scrollIntoView({ behavior: 'smooth', block: 'start' });
        target.classList.add('wiki-flash');
        setTimeout(function () { target.classList.remove('wiki-flash'); }, 1200);
        if (updateHash) history.replaceState(history.state, '', '#' + id);
    }

    // --------------------------------------------------------- navigation

    // Central navigation. push=true adds a history entry (user click);
    // push=false replaces (initial load / retry / back-forward).
    function navigate(path, anchor, push) {
        if (!path) { state.path = null; loadLanding(); if (push) history.pushState({}, '', '/Wiki'); return; }

        state.path = path;
        markActive(path);
        showState('loading', '<div class="wiki-msg"><span class="wiki-spinner"></span><p>Loading…</p></div>');

        getJSON(API.page + '?path=' + encodeURIComponent(path))
            .then(function (page) {
                renderPage(page, anchor);
                markActive(path);
                var url = '/Wiki?path=' + encodeURIComponent(path) + (anchor ? '#' + anchor : '');
                if (push) history.pushState({ path: path, anchor: anchor }, '', url);
                else history.replaceState({ path: path, anchor: anchor }, '', url);
                document.title = page.title + ' — Wiki';
            })
            .catch(function (err) { renderError(path, err.message); });
    }

    var _stats = null;
    function loadLanding() {
        if (_stats) { renderLanding(_stats); return; }
        getJSON(API.stats)
            .then(function (s) { _stats = s; renderLanding(s); })
            .catch(function () { renderLanding({ root: 'Wiki', pageCount: '—', folderCount: '—', lastUpdated: null, ready: false }); });
    }

    // Intercept any in-article cross-reference (or breadcrumb) click for SPA nav.
    function onArticleClick(e) {
        var a = e.target.closest('a[data-wiki-path]');
        if (!a) return;
        if (e.metaKey || e.ctrlKey || e.button === 1) return;  // new tab
        e.preventDefault();
        navigate(a.getAttribute('data-wiki-path'), a.getAttribute('data-wiki-anchor') || null, true);
    }

    function onPopState() {
        var params = new URLSearchParams(location.search);
        var path = params.get('path');
        var anchor = location.hash ? location.hash.slice(1) : null;
        if (path) navigate(path, anchor, false);
        else { state.path = null; markActive(null); loadLanding(); }
    }

    // ----------------------------------------------------- layout controls

    // Browse drawer: the full page tree, closed by default, opened by the header's ☰.
    function openDrawer(open) {
        if (!els.drawer) return;
        var show = open === undefined ? els.drawer.classList.contains('open') === false : open;
        els.drawer.classList.toggle('open', show);
        els.drawer.setAttribute('aria-hidden', String(!show));
        if (els.drawerBackdrop) els.drawerBackdrop.toggleAttribute('hidden', !show);
        if (els.browseBtn) els.browseBtn.setAttribute('aria-expanded', String(show));
        if (show) {
            if (els.optsPanel) openOpts(false);           // don't stack the two overlays
            var active = els.tree.querySelector('.wiki-tree-page.active');
            if (active) active.scrollIntoView({ block: 'nearest' });
            if (els.filter) els.filter.focus();
        }
    }

    // Contents rail: 'empty' = the doc has no sections (auto); 'hidden' = the user hid it.
    function setContentsEmpty(empty) { if (els.body) els.body.classList.toggle('contents-empty', !!empty); }
    function setContentsHidden(hidden) { if (els.body) els.body.classList.toggle('contents-hidden', !!hidden); }

    function wireLayout() {
        els.browseBtn = el('wikiBrowseBtn');
        els.drawer = el('wikiDrawer');
        els.drawerBackdrop = el('wikiDrawerBackdrop');
        els.drawerClose = el('wikiDrawerClose');
        els.contentsHide = el('wikiContentsHide');
        els.contentsShow = el('wikiContentsShow');

        if (els.browseBtn) els.browseBtn.addEventListener('click', function () { openDrawer(); });
        if (els.drawerClose) els.drawerClose.addEventListener('click', function () { openDrawer(false); });
        if (els.drawerBackdrop) els.drawerBackdrop.addEventListener('click', function () { openDrawer(false); });
        if (els.contentsHide) els.contentsHide.addEventListener('click', function () { setContentsHidden(true); });
        if (els.contentsShow) els.contentsShow.addEventListener('click', function () { setContentsHidden(false); });

        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' && els.drawer && els.drawer.classList.contains('open')) openDrawer(false);
        });
    }


    // A tiny, generic display-prefs layer. Prefs are applied as data-attributes on the
    // wiki root (.wiki-body); wiki.css does all the visual work by reacting to them. The
    // options panel is declarative: each control carries data-opt (+ data-val for the
    // segmented ones), so ADDING an option is just markup + a CSS rule + a default here —
    // no new JS. That's the seam future ideas (themes, link-preview toggle, etc.) plug into.

    var PREFS_KEY = 'superui.wiki.prefs';
    var PREF_DEFAULTS = { width: 'comfortable', size: 'normal', spacing: 'normal', surface: false, infobox: true };

    var prefs = Object.assign({}, PREF_DEFAULTS);

    function loadPrefs() {
        try {
            var saved = JSON.parse(localStorage.getItem(PREFS_KEY) || '{}');
            prefs = Object.assign({}, PREF_DEFAULTS, saved);
        } catch (e) { prefs = Object.assign({}, PREF_DEFAULTS); }
    }
    function savePrefs() {
        try { localStorage.setItem(PREFS_KEY, JSON.stringify(prefs)); } catch (e) { /* private mode — session only */ }
    }
    function applyPrefs() {
        var b = els.body;
        if (!b) return;
        b.dataset.width = prefs.width;
        b.dataset.size = prefs.size;
        b.dataset.spacing = prefs.spacing;
        b.dataset.surface = prefs.surface ? 'on' : 'off';
        b.dataset.infobox = prefs.infobox ? 'on' : 'off';
    }
    function syncPanel() {
        if (!els.optsPanel) return;
        // segmented controls: mark the active value in each group
        els.optsPanel.querySelectorAll('.wiki-opt[data-opt]').forEach(function (grp) {
            var opt = grp.dataset.opt;
            grp.querySelectorAll('.wiki-seg [data-val]').forEach(function (btn) {
                btn.classList.toggle('active', String(prefs[opt]) === btn.dataset.val);
            });
            // boolean switch controls
            var sw = grp.querySelector('.wiki-switch');
            if (sw) { sw.classList.toggle('on', !!prefs[opt]); sw.setAttribute('aria-checked', String(!!prefs[opt])); }
        });
    }
    function setPref(opt, val) {
        prefs[opt] = val;
        applyPrefs(); savePrefs(); syncPanel();
    }
    function resetPrefs() {
        prefs = Object.assign({}, PREF_DEFAULTS);
        applyPrefs(); savePrefs(); syncPanel();
    }

    function openOpts(open) {
        if (!els.optsPanel) return;
        var show = open === undefined ? els.optsPanel.hasAttribute('hidden') : open;
        if (show) els.optsPanel.removeAttribute('hidden'); else els.optsPanel.setAttribute('hidden', '');
        if (els.optsBtn) els.optsBtn.setAttribute('aria-expanded', String(show));
    }

    function wireOptions() {
        els.optsBtn = el('wikiOptsBtn');
        els.optsPanel = el('wikiOptsPanel');
        if (!els.optsBtn || !els.optsPanel) return;   // panel is optional markup

        els.optsBtn.addEventListener('click', function (e) { e.stopPropagation(); openOpts(); });

        // one delegated handler for every control in the panel
        els.optsPanel.addEventListener('click', function (e) {
            var seg = e.target.closest('.wiki-seg [data-val]');
            if (seg) { setPref(seg.closest('.wiki-opt').dataset.opt, seg.dataset.val); return; }
            var sw = e.target.closest('.wiki-switch');
            if (sw) { var opt = sw.closest('.wiki-opt').dataset.opt; setPref(opt, !prefs[opt]); return; }
            if (e.target.closest('[data-wiki-reset]')) resetPrefs();
        });

        // click-away + Esc close
        document.addEventListener('click', function (e) {
            if (els.optsPanel.hasAttribute('hidden')) return;
            if (!els.optsPanel.contains(e.target) && e.target !== els.optsBtn) openOpts(false);
        });
        document.addEventListener('keydown', function (e) { if (e.key === 'Escape') openOpts(false); });

        syncPanel();
    }

    // ---------------------------------------------------------------- init

    document.addEventListener('DOMContentLoaded', function () {
        els.body = document.querySelector('.wiki-body');
        els.tree = el('wikiTree');
        els.article = el('wikiArticle');
        els.contents = el('wikiContentsBody');
        els.pageCount = el('wikiPageCount');
        els.filter = el('wikiFilter');

        loadPrefs();
        applyPrefs();
        wireOptions();
        wireLayout();

        els.article.addEventListener('click', onArticleClick);
        els.filter.addEventListener('input', function () { applyFilter(els.filter.value); });
        window.addEventListener('popstate', onPopState);

        getJSON(API.tree)
            .then(function (tree) {
                renderTree(tree);
                var params = new URLSearchParams(location.search);
                var path = params.get('path');
                var anchor = location.hash ? location.hash.slice(1) : null;
                if (path) navigate(path, anchor, false);
                else loadLanding();
            })
            .catch(function (err) {
                els.tree.innerHTML = '<div class="wiki-tree-empty">Tree failed to load.</div>';
                renderError('the page tree', err.message);
            });
    });

})();