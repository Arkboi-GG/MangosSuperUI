/* ============================================================
   MangosSuperUI — wiki-lua.js
   The 1.12 client-side reference. Searches the harvested FrameXML
   catalog (templates / frames / textures / Lua API), shows the
   RESOLVED record for a hit, and builds the context bundle that
   gets pasted into a chat.

   Same client idiom as wiki.js: one IIFE, an API map, el/esc/getJSON.
   ============================================================ */

(function () {
    'use strict';

    var API = {
        status: '/Wiki/LuaStatus',
        search: '/Wiki/LuaSearch',
        record: '/Wiki/LuaRecord',
        context: '/Wiki/LuaContext'
    };

    var els = {};
    var state = { scope: 'all', query: '', hits: [], selected: null, markdown: '' };
    var timer = null;

    function el(id) { return document.getElementById(id); }

    function esc(s) {
        return String(s == null ? '' : s)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    function getJSON(url) {
        return fetch(url, { headers: { 'Accept': 'application/json' } })
            .then(function (r) {
                if (!r.ok) throw new Error(r.status + ' ' + r.statusText);
                return r.json();
            });
    }

    // ---------------------------------------------------------- boot

    document.addEventListener('DOMContentLoaded', function () {
        els.query = el('luaQuery');
        els.scopes = el('luaScopes');
        els.count = el('luaCount');
        els.results = el('luaResults');
        els.detail = el('luaDetail');
        els.counts = el('luaCounts');
        els.ctxOut = el('luaCtxOut');
        els.ctxSize = el('luaCtxSize');
        els.ctxState = el('luaCtxState');

        els.query.addEventListener('input', function () {
            clearTimeout(timer);
            timer = setTimeout(runSearch, 180);
        });

        els.scopes.addEventListener('click', function (e) {
            var btn = e.target.closest('.lua-scope');
            if (!btn) return;
            Array.prototype.forEach.call(els.scopes.children, function (b) { b.classList.remove('active'); });
            btn.classList.add('active');
            state.scope = btn.getAttribute('data-scope');
            runSearch();
        });

        el('luaBuild').addEventListener('click', buildContext);
        el('luaCopy').addEventListener('click', copyContext);
        el('luaDownload').addEventListener('click', downloadContext);

        // '/' focuses search, same reflex as the code wiki
        document.addEventListener('keydown', function (e) {
            if (e.key === '/' && document.activeElement !== els.query) {
                e.preventDefault();
                els.query.focus();
            }
        });

        loadStatus();
    });

    function loadStatus() {
        getJSON(API.status).then(function (s) {
            if (!s.ready) {
                els.counts.textContent = 'catalog not deployed';
                els.results.innerHTML =
                    '<div class="lua-empty"><b>The FrameXML catalog is not deployed.</b><br><br>' +
                    'Expected in <code>' + esc(s.directory) + '</code>.<br>' +
                    'Produce it with:<br><code>' + esc(s.command) + '</code></div>';
                return;
            }
            var c = s.counts;
            els.counts.textContent =
                c.templates + ' templates, ' + c.frames + ' frames, ' +
                c.textures + ' textures, ' + c.api + ' API entries';
            if (!s.capabilities) {
                els.ctxState.textContent = 'no capability manifest on disk';
            }
        }).catch(function (err) {
            els.counts.textContent = 'status failed: ' + err.message;
        });
    }

    // ---------------------------------------------------------- search

    function runSearch() {
        var q = els.query.value.trim();
        state.query = q;
        if (q.length < 2) {
            els.count.textContent = '';
            els.results.innerHTML = '<div class="lua-empty">Type at least two characters.</div>';
            return;
        }

        getJSON(API.search + '?q=' + encodeURIComponent(q) +
                '&scope=' + encodeURIComponent(state.scope) + '&take=60')
            .then(function (res) {
                state.hits = res.hits || [];
                renderHits(res);
            })
            .catch(function (err) {
                els.results.innerHTML = '<div class="lua-empty">Search failed: ' + esc(err.message) + '</div>';
            });
    }

    function renderHits(res) {
        if (!state.hits.length) {
            els.count.textContent = '';
            els.results.innerHTML = '<div class="lua-empty">Nothing matched. The catalog only knows what Blizzard shipped in 1.12 — if it is not here, it does not exist client-side.</div>';
            return;
        }

        els.count.textContent = state.hits.length + ' shown' +
            (res.count > state.hits.length ? ' of ' + res.count : '');

        var html = '';
        state.hits.forEach(function (h, i) {
            html += '<div class="lua-hit" data-i="' + i + '">' +
                '<div class="lua-hit-top">' +
                '<span class="lua-hit-name">' + esc(h.name) + '</span>' +
                '<span class="lua-kind lua-kind-' + esc(h.kind) + '">' + esc(h.kind) + '</span>' +
                (h.type ? '<span class="lua-muted">' + esc(h.type) + '</span>' : '') +
                (h.needsSize ? '<span class="lua-kind lua-warn">no size</span>' : '') +
                (h.hidden ? '<span class="lua-kind">hidden</span>' : '') +
                (h.kind === 'template' && !h.useCount ? '<span class="lua-kind">unused</span>' : '') +
                '</div>' +
                (h.file ? '<div class="lua-cite">' + esc(h.file) + ':' + h.line + '</div>' : '') +
                '</div>';
        });
        els.results.innerHTML = html;

        Array.prototype.forEach.call(els.results.querySelectorAll('.lua-hit'), function (row) {
            row.addEventListener('click', function () {
                Array.prototype.forEach.call(els.results.querySelectorAll('.lua-hit'), function (r) {
                    r.classList.remove('selected');
                });
                row.classList.add('selected');
                showRecord(state.hits[parseInt(row.getAttribute('data-i'), 10)]);
            });
        });
    }

    // ---------------------------------------------------------- detail

    function showRecord(h) {
        state.selected = h;
        var out = '<h2>' + esc(h.name) + '</h2>';
        out += '<div class="lua-muted">' + esc(h.kind) + (h.type ? ' · ' + esc(h.type) : '') + '</div>';

        var facts = '';
        if (h.file) facts += fact('Source', h.file + ':' + h.line);

        if (h.kind === 'template') {
            facts += fact('Size', (h.width || h.height)
                ? h.width + '×' + h.height + (h.sizeFrom && h.sizeFrom !== h.name ? ' (inherited from ' + h.sizeFrom + ')' : '')
                : 'none declared');
            if (h.chain && h.chain.length > 1) facts += fact('Inherits', h.chain.slice(1).join(' ← '));
            if (h.scripts && h.scripts.length) facts += fact('Template scripts', h.scripts.join(', '));
            if (h.textures && h.textures.length) facts += fact('Textures', h.textures.slice(0, 8).join('<br>'));
            facts += fact('Used by Blizzard', h.useCount + ' time' + (h.useCount === 1 ? '' : 's'));
        }

        out += '<ul class="lua-facts">' + facts + '</ul>';

        if (h.needsSize) {
            out += '<span class="lua-flag lua-flag-err"><b>No size anywhere in the chain.</b> ' +
                'Set width and height or the widget renders invisible — this one costs real debugging time.</span>';
        }
        if (h.hidden) {
            out += '<span class="lua-flag lua-flag-warn"><b>hidden="true".</b> Nothing appears until you call :Show().</span>';
        }
        if (h.calls && h.calls.length) {
            out += '<span class="lua-flag lua-flag-warn"><b>Hardcodes an XML handler call</b> (' +
                esc(h.calls.slice(0, 4).join(', ')) + '). Override with SetScript or you call Blizzard\'s handler.</span>';
        }
        if (h.kind === 'template' && !h.useCount) {
            out += '<span class="lua-flag">Blizzard never uses this template. It is an untested path, not a recommendation.</span>';
        }

        out += '<div class="lua-row" style="margin-top:14px;">' +
            '<button type="button" class="lua-btn" id="luaCopyRecord">Copy as markdown</button></div>';

        els.detail.innerHTML = out;
        var btn = el('luaCopyRecord');
        if (btn) btn.addEventListener('click', function () { copyText(recordMarkdown(h), btn, 'Copy as markdown'); });
    }

    function fact(label, value) {
        return '<li><b>' + esc(label) + '</b><br>' + value + '</li>';
    }

    function recordMarkdown(h) {
        var lines = ['### ' + h.name + (h.type ? ' — ' + h.type : '')];
        if (h.file) lines.push('- source: `' + h.file + ':' + h.line + '`');
        if (h.kind === 'template') {
            lines.push('- size: ' + ((h.width || h.height)
                ? h.width + 'x' + h.height + (h.sizeFrom && h.sizeFrom !== h.name ? ' (inherited from ' + h.sizeFrom + ')' : '')
                : '**none declared — must set width and height or it renders invisible**'));
            if (h.chain && h.chain.length > 1) lines.push('- inherits: ' + h.chain.slice(1).join(' <- '));
            if (h.hidden) lines.push('- hidden="true" — call :Show()');
            if (h.scripts && h.scripts.length) lines.push('- template scripts: ' + h.scripts.join(', '));
            if (h.calls && h.calls.length) lines.push('- hardcodes XML handler calls: ' + h.calls.join(', '));
            lines.push('- used ' + h.useCount + ' times in Blizzard\'s own UI');
        }
        return lines.join('\n');
    }

    // ---------------------------------------------------------- context bundle

    function buildContext() {
        var q = els.query.value.trim();
        var url = API.context +
            '?q=' + encodeURIComponent(q) +
            '&budget=' + encodeURIComponent(el('luaBudget').value) +
            '&rules=' + (el('luaOptRules').checked ? 'true' : 'false') +
            '&docs=' + (el('luaOptDocs').checked ? 'true' : 'false') +
            '&caps=' + (el('luaOptCaps').checked ? 'true' : 'false');

        els.ctxState.textContent = 'building…';
        getJSON(url).then(function (res) {
            state.markdown = res.markdown || '';
            els.ctxOut.textContent = state.markdown;
            els.ctxSize.textContent = res.chars + ' chars · ~' + res.approxTokens + ' tokens';
            els.ctxState.textContent = (res.sections || []).join(' · ') +
                (res.truncated ? ' · truncated to budget' : '');
        }).catch(function (err) {
            els.ctxState.textContent = 'failed: ' + err.message;
        });
    }

    function copyContext() {
        if (!state.markdown) { els.ctxState.textContent = 'build a bundle first'; return; }
        copyText(state.markdown, el('luaCopy'), 'Copy');
    }

    function copyText(text, btn, restore) {
        function done(ok) {
            if (!btn) return;
            btn.textContent = ok ? 'Copied' : 'Copy failed';
            setTimeout(function () { btn.textContent = restore; }, 1400);
        }
        if (navigator.clipboard && navigator.clipboard.writeText) {
            navigator.clipboard.writeText(text).then(function () { done(true); }, function () { done(false); });
            return;
        }
        // http:// origins get no clipboard API — fall back to a hidden textarea
        var ta = document.createElement('textarea');
        ta.value = text;
        ta.style.position = 'fixed';
        ta.style.left = '-9999px';
        document.body.appendChild(ta);
        ta.select();
        var ok = false;
        try { ok = document.execCommand('copy'); } catch (e) { ok = false; }
        document.body.removeChild(ta);
        done(ok);
    }

    function downloadContext() {
        if (!state.markdown) { els.ctxState.textContent = 'build a bundle first'; return; }
        var name = (state.query || 'context').replace(/[^A-Za-z0-9_-]+/g, '-').toLowerCase();
        var blob = new Blob([state.markdown], { type: 'text/markdown' });
        var url = URL.createObjectURL(blob);
        var a = document.createElement('a');
        a.href = url;
        a.download = 'wow112-' + name + '.md';
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
    }

}());
