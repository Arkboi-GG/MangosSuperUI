/* MangosSuperUI :: Addon Builder -- addon-catalog.js
 *
 * Loads and indexes the harvested FrameXML catalog. This is the layer the
 * palette, the properties panel and the validator all sit on -- nothing else
 * should touch the raw JSON.
 *
 * Expects the SPLIT output of framexml_split.py:
 *     wwwroot/data/framexml/templates.json     ~280 KB   palette
 *     wwwroot/data/framexml/textures.json      ~72 KB    art browser
 *     wwwroot/data/framexml/fontObjects.json   ~2 KB
 *     wwwroot/data/framexml/frames.json        ~1.7 MB   lazy
 *     wwwroot/data/framexml/api.json           ~731 KB   lazy
 *
 * templates.json already carries a precomputed `effective` block resolving the
 * inheritance chain. 86 templates inherit another template and 42 of them get
 * their SIZE from an ancestor -- anything reading `size` directly is wrong for
 * those. Always go through effective().
 */
(function (root) {
    "use strict";

    function Catalog() {
        this.templates = [];
        this.textures = [];
        this.fontObjects = [];
        this.frames = null;      /* lazy */
        this.api = null;         /* lazy */
        this._byName = {};
        this._byType = {};
        this._frameByName = null;
    }

    Catalog.prototype._index = function () {
        var i, t;
        this._byName = {};
        this._byType = {};
        for (i = 0; i < this.templates.length; i += 1) {
            t = this.templates[i];
            this._byName[t.name] = t;
            if (!this._byType[t.type]) { this._byType[t.type] = []; }
            this._byType[t.type].push(t);
        }
    };

    /* ---------------- construction ---------------- */

    Catalog.prototype.setTemplates = function (arr) {
        this.templates = arr || [];
        this._index();
        return this;
    };

    Catalog.prototype.setTextures = function (arr) {
        this.textures = arr || [];
        return this;
    };

    Catalog.prototype.setFontObjects = function (arr) {
        this.fontObjects = arr || [];
        return this;
    };

    /* Build from the UNSPLIT framexml_index.json (testing, or small stores).
     * Resolves inheritance here since the splitter did not. */
    Catalog.prototype.fromDocument = function (doc) {
        this.setTemplates(doc.templates);
        this.setTextures(doc.textures);
        this.setFontObjects(doc.fontObjects);
        this.frames = doc.frames || null;
        this.api = {
            luaFunctions: doc.luaFunctions || [],
            luaGlobals: doc.luaGlobals || [],
            bindings: doc.bindings || []
        };
        var i;
        for (i = 0; i < this.templates.length; i += 1) {
            if (!this.templates[i].effective) {
                this.templates[i].effective = this._resolve(this.templates[i].name);
            }
        }
        return this;
    };

    /* Fallback resolver, used only when `effective` is absent. */
    Catalog.prototype._resolve = function (name) {
        var chain = [], seen = {}, cur = name, rec;
        while (cur && !seen[cur] && chain.length < 12) {
            seen[cur] = true;
            chain.push(cur);
            rec = this._byName[cur];
            if (!rec) { break; }
            cur = rec.inherits;
        }

        var size = null, sizeFrom = null, hidden = false;
        var textures = {}, scripts = {}, calls = {}, fonts = {};
        var i, j, r;

        for (i = 0; i < chain.length; i += 1) {
            r = this._byName[chain[i]];
            if (!r) { continue; }
            if (!size && r.size) { size = r.size; sizeFrom = chain[i]; }
            if (r.hidden) { hidden = true; }
            for (j = 0; j < (r.textures || []).length; j += 1) { textures[r.textures[j]] = 1; }
            for (j = 0; j < (r.scripts || []).length; j += 1) { scripts[r.scripts[j]] = 1; }
            for (j = 0; j < (r.calls || []).length; j += 1) { calls[r.calls[j]] = 1; }
            for (j = 0; j < (r.fonts || []).length; j += 1) { fonts[r.fonts[j]] = 1; }
        }

        return {
            chain: chain,
            size: size,
            sizeFrom: sizeFrom,
            hidden: hidden,
            textures: Object.keys(textures).sort(),
            scripts: Object.keys(scripts).sort(),
            calls: Object.keys(calls).sort(),
            fonts: Object.keys(fonts).sort()
        };
    };

    /* Browser load. Palette sections up front; frames/api on demand. */
    Catalog.prototype.load = function (baseUrl) {
        var self = this;
        baseUrl = (baseUrl || "/data/framexml").replace(/\/$/, "");
        self._baseUrl = baseUrl;

        function grab(name) {
            return fetch(baseUrl + "/" + name).then(function (r) {
                if (!r.ok) { throw new Error(name + ": " + r.status); }
                return r.json();
            });
        }

        return Promise.all([
            grab("templates.json"),
            grab("textures.json"),
            grab("fontObjects.json")
        ]).then(function (parts) {
            self.setTemplates(parts[0]);
            self.setTextures(parts[1]);
            self.setFontObjects(parts[2]);
            return self;
        });
    };

    Catalog.prototype.loadFrames = function () {
        var self = this;
        if (self.frames) { return Promise.resolve(self.frames); }
        return fetch(self._baseUrl + "/frames.json")
            .then(function (r) { return r.json(); })
            .then(function (d) { self.frames = d; return d; });
    };

    Catalog.prototype.loadApi = function () {
        var self = this;
        if (self.api) { return Promise.resolve(self.api); }
        return fetch(self._baseUrl + "/api.json")
            .then(function (r) { return r.json(); })
            .then(function (d) { self.api = d; return d; });
    };

    /* ---------------- queries ---------------- */

    Catalog.prototype.byName = function (name) {
        return this._byName[name] || null;
    };

    Catalog.prototype.byType = function (type) {
        return this._byType[type] || [];
    };

    Catalog.prototype.types = function () {
        var out = [], k;
        for (k in this._byType) {
            if (this._byType.hasOwnProperty(k)) {
                out.push({ type: k, count: this._byType[k].length });
            }
        }
        out.sort(function (a, b) { return b.count - a.count; });
        return out;
    };

    /* Resolved properties. ALWAYS use this rather than reading .size. */
    Catalog.prototype.effective = function (name) {
        var t = this._byName[name];
        if (!t) { return null; }
        if (!t.effective) { t.effective = this._resolve(name); }
        return t.effective;
    };

    /* The UIPanelButtonTemplate trap: no size anywhere in the chain means the
     * designer must supply one or the widget renders invisible. */
    Catalog.prototype.needsSize = function (name) {
        var eff = this.effective(name);
        return !!eff && !eff.size;
    };

    Catalog.prototype.usedBy = function (name) {
        var t = this._byName[name];
        return t ? (t.usedBy || []) : [];
    };

    /* Ranked search. Exact > prefix > substring, then by how much Blizzard
     * leans on it -- a template with 95 uses is a safer suggestion than one
     * with 0. */
    Catalog.prototype.search = function (query, opts) {
        opts = opts || {};
        var q = String(query || "").toLowerCase().trim();
        var limit = opts.limit || 50;
        var pool = opts.type ? this.byType(opts.type) : this.templates;
        var out = [], i, t, name, score;

        if (!q) {
            out = pool.slice(0);
        } else {
            for (i = 0; i < pool.length; i += 1) {
                t = pool[i];
                name = t.name.toLowerCase();
                score = -1;
                if (name === q) { score = 1000; }
                else if (name.indexOf(q) === 0) { score = 500; }
                else if (name.indexOf(q) !== -1) { score = 200; }
                else if (t.file && t.file.toLowerCase().indexOf(q) !== -1) { score = 50; }
                if (score >= 0) {
                    out.push({ t: t, score: score + Math.min(t.useCount || 0, 99) });
                }
            }
            out.sort(function (a, b) { return b.score - a.score; });
            out = out.map(function (r) { return r.t; });
        }

        if (!opts.includeUnused) {
            /* keep them, just sink them -- 31 templates Blizzard never used are
             * untested paths, worth flagging rather than hiding */
            out.sort(function (a, b) {
                var au = (a.useCount || 0) === 0 ? 1 : 0;
                var bu = (b.useCount || 0) === 0 ? 1 : 0;
                return au - bu;
            });
        }

        return out.slice(0, limit);
    };

    /* Palette grouping, biggest buckets first. */
    Catalog.prototype.paletteGroups = function () {
        var self = this;
        return self.types().map(function (row) {
            return {
                type: row.type,
                count: row.count,
                items: self.byType(row.type).slice(0).sort(function (a, b) {
                    return (b.useCount || 0) - (a.useCount || 0);
                })
            };
        });
    };

    /* ---------------- textures ---------------- */

    Catalog.prototype.textureTree = function () {
        var dirs = {}, i, p, dir;
        for (i = 0; i < this.textures.length; i += 1) {
            p = this.textures[i].path;
            dir = p.lastIndexOf("\\") === -1 ? "" : p.substring(0, p.lastIndexOf("\\"));
            if (!dirs[dir]) { dirs[dir] = []; }
            dirs[dir].push(this.textures[i]);
        }
        var out = [], k;
        for (k in dirs) {
            if (dirs.hasOwnProperty(k)) {
                out.push({ dir: k, count: dirs[k].length, items: dirs[k] });
            }
        }
        out.sort(function (a, b) { return b.count - a.count; });
        return out;
    };

    /* Path the controller serves BLP->PNG from. */
    Catalog.prototype.textureUrl = function (path) {
        return "/AddonBuilder/Texture?path=" + encodeURIComponent(path);
    };

    Catalog.prototype.searchTextures = function (query, limit) {
        var q = String(query || "").toLowerCase();
        var out = [], i;
        for (i = 0; i < this.textures.length && out.length < (limit || 100); i += 1) {
            if (!q || this.textures[i].path.toLowerCase().indexOf(q) !== -1) {
                out.push(this.textures[i]);
            }
        }
        return out;
    };

    /* ---------------- api index (after loadApi) ---------------- */

    Catalog.prototype.findFunction = function (name) {
        if (!this.api) { return null; }
        var i;
        for (i = 0; i < this.api.luaFunctions.length; i += 1) {
            if (this.api.luaFunctions[i].name === name) { return this.api.luaFunctions[i]; }
        }
        return null;
    };

    Catalog.prototype.frameByName = function (name) {
        if (!this.frames) { return null; }
        if (!this._frameByName) {
            this._frameByName = {};
            var i;
            for (i = 0; i < this.frames.length; i += 1) {
                this._frameByName[this.frames[i].name] = this.frames[i];
            }
        }
        return this._frameByName[name] || null;
    };

    root.AddonCatalog = {
        create: function () { return new Catalog(); },
        Catalog: Catalog
    };

}(typeof window !== "undefined" ? window : global));
