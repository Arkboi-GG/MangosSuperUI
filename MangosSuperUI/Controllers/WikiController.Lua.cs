using System.Text;
using System.Text.Json;
using MangosSuperUI.Services;
using Microsoft.AspNetCore.Mvc;

namespace MangosSuperUI.Controllers;

// The Lua half of the wiki controller — the 1.12 client-side counterpart to the C++
// corpus that WikiController.Code.cs serves. Slots in as a sibling partial exactly as
// that file's header promised: no edit to it, services via action-level injection.
//
//   GET  /Wiki/Lua              -> the reader shell (Views/Wiki/Lua.cshtml)
//   GET  /Wiki/LuaSearch?q=&scope=&take=   -> ranked hits across the FrameXML catalog
//   GET  /Wiki/LuaRecord?kind=&name=       -> one full record, expanded
//   GET  /Wiki/LuaStatus                   -> what is deployed on disk
//   GET  /Wiki/LuaCapabilities             -> the hand-curated capability manifest
//   GET  /Wiki/LuaContext?q=&budget=       -> ONE markdown block, cited, budgeted
//
// WHY LuaContext EXISTS
// ---------------------
// The catalog already stops "send me that XML file". The remaining failure is subtler:
// a model answering from memory doesn't just get facts wrong, it UNDERSTATES what is
// reachable, because its idea of a vanilla admin panel is read-only web pages. So the
// bundle carries three things: the hard 1.12/Lua 5.0 rules, the matching ground-truth
// records with file:line, and the capability manifest listing every road in and out of
// this app. Paste it and the answer changes shape.
//
// The catalog is STATIC DATA under wwwroot/data/framexml (framexml_split.py output),
// cached in-process behind a write-time signature — the same discipline WikiDocStore
// uses for the corpus. No DI registration, no Program.cs edit.
public sealed partial class WikiController
{
    // GET /Wiki/Lua
    public IActionResult Lua() => View();

    // GET /Wiki/LuaStatus
    public IActionResult LuaStatus([FromServices] IWebHostEnvironment env)
        => Json(LuaCatalog.For(env).Status());

    // GET /Wiki/LuaCapabilities
    public IActionResult LuaCapabilities([FromServices] IWebHostEnvironment env)
        => Json(LuaCatalog.For(env).Capabilities());

    // GET /Wiki/LuaSearch?q=SpellBook&scope=all&take=40
    public IActionResult LuaSearch(
        [FromServices] IWebHostEnvironment env,
        string? q, string scope = "all", int take = 40)
        => Json(LuaCatalog.For(env).Search(q ?? "", scope, Math.Clamp(take, 1, 200)));

    // GET /Wiki/LuaRecord?kind=template&name=UIPanelButtonTemplate
    public IActionResult LuaRecord(
        [FromServices] IWebHostEnvironment env, string kind, string name)
    {
        var rec = LuaCatalog.For(env).Record(kind, name);
        return rec is null ? NotFound() : Json(rec);
    }

    // GET /Wiki/LuaContext?q=talent%20frame&budget=8000&docs=true
    // Returns { markdown, chars, approxTokens, sections } — the client shows it and
    // copies it; nothing here is rendered as HTML.
    public async Task<IActionResult> LuaContext(
        [FromServices] IWebHostEnvironment env,
        [FromServices] WikiSearchStore search,
        string? q, int budget = 8000, bool docs = true, bool rules = true, bool caps = true,
        CancellationToken ct = default)
    {
        var query = (q ?? "").Trim();
        var cat = LuaCatalog.For(env);

        List<WikiSearchHit> prose = new();
        if (docs && query.Length >= 2)
        {
            try
            {
                var res = await search.SearchAsync(query, 6, ct);
                if (res.Ready) prose = res.Hits;
            }
            catch { /* the bundle is still worth producing without prose */ }
        }

        var md = cat.BuildContext(query, Math.Clamp(budget, 1000, 60000), prose, rules, caps);
        return Json(new
        {
            query,
            markdown = md.Text,
            chars = md.Text.Length,
            approxTokens = md.Text.Length / 4,
            sections = md.Sections,
            truncated = md.Truncated
        });
    }
}

/// <summary>
/// Read-only index over the split FrameXML catalog in <c>wwwroot/data/framexml</c>.
/// One instance per web root, cached behind the directory's newest write time so a
/// re-run of framexml_split.py is picked up without a restart.
///
/// Every field is read defensively through <see cref="JsonElement"/>: the harvester
/// owns the shape, and a missing field should degrade a record, never throw.
/// </summary>
public sealed class LuaCatalog
{
    private static readonly object Gate = new();
    private static LuaCatalog? _instance;

    private readonly string _dir;
    private string _sig = "";

    private List<Rec> _templates = new();
    private List<Rec> _frames = new();
    private List<Rec> _textures = new();
    private List<Rec> _api = new();
    private JsonElement? _caps;

    private LuaCatalog(string dir) => _dir = dir;

    public static LuaCatalog For(IWebHostEnvironment env)
    {
        var dir = Path.Combine(env.WebRootPath, "data", "framexml");
        lock (Gate)
        {
            if (_instance is null || !string.Equals(_instance._dir, dir, StringComparison.OrdinalIgnoreCase))
                _instance = new LuaCatalog(dir);
            _instance.EnsureLoaded(env);
            return _instance;
        }
    }

    // ------------------------------------------------------------------ load

    private string Signature()
    {
        if (!Directory.Exists(_dir)) return "none";
        var sb = new StringBuilder();
        foreach (var f in Directory.EnumerateFiles(_dir, "*.json").OrderBy(x => x))
            sb.Append(Path.GetFileName(f)).Append(':').Append(new FileInfo(f).LastWriteTimeUtc.Ticks).Append(';');
        return sb.ToString();
    }

    private void EnsureLoaded(IWebHostEnvironment env)
    {
        var sig = Signature();
        if (sig == _sig && _templates.Count > 0) { LoadCaps(env); return; }
        _sig = sig;

        _templates = ReadArray("templates.json", ParseTemplate);
        _frames = ReadArray("frames.json", e => Simple(e, "frame"));
        _textures = ReadArray("textures.json", ParseTexture);
        _api = ReadApi();
        LoadCaps(env);
    }

    private void LoadCaps(IWebHostEnvironment env)
    {
        if (_caps is not null) return;
        var path = Path.Combine(env.WebRootPath, "data", "superui-capabilities.json");
        if (!System.IO.File.Exists(path)) return;
        try { _caps = JsonSerializer.Deserialize<JsonElement>(System.IO.File.ReadAllText(path)); }
        catch { /* a malformed manifest just means no manifest */ }
    }

    private List<Rec> ReadArray(string file, Func<JsonElement, Rec?> map)
    {
        var path = Path.Combine(_dir, file);
        var outp = new List<Rec>();
        if (!System.IO.File.Exists(path)) return outp;
        try
        {
            using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return outp;
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                var r = map(e);
                if (r is not null) outp.Add(r);
            }
        }
        catch { /* leave the section empty; Status() reports the file as unreadable */ }
        return outp;
    }

    private List<Rec> ReadApi()
    {
        var path = Path.Combine(_dir, "api.json");
        var outp = new List<Rec>();
        if (!System.IO.File.Exists(path)) return outp;
        try
        {
            using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
            foreach (var (prop, kind) in new[] { ("luaFunctions", "function"), ("luaGlobals", "global"), ("bindings", "binding") })
            {
                if (!doc.RootElement.TryGetProperty(prop, out var arr) || arr.ValueKind != JsonValueKind.Array) continue;
                foreach (var e in arr.EnumerateArray())
                {
                    var r = Simple(e, kind);
                    if (r is not null) outp.Add(r);
                }
            }
        }
        catch { }
        return outp;
    }

    // ---------------------------------------------------------------- parsing

    private static string? Str(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    private static int Int(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.TryGetInt32(out var i) ? i : 0;

    private static bool Bool(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static List<string> Arr(JsonElement e, string name)
    {
        var outp = new List<string>();
        if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array)
            foreach (var x in v.EnumerateArray())
                if (x.ValueKind == JsonValueKind.String) outp.Add(x.GetString()!);
        return outp;
    }

    private static Rec? Simple(JsonElement e, string kind)
    {
        var name = Str(e, "name");
        if (string.IsNullOrEmpty(name)) return null;
        return new Rec
        {
            Kind = kind,
            Name = name,
            Type = Str(e, "type") ?? "",
            File = Str(e, "file") ?? "",
            Line = Int(e, "line"),
            UseCount = Int(e, "useCount")
        };
    }

    private static Rec? ParseTexture(JsonElement e)
    {
        var path = Str(e, "path") ?? Str(e, "name");
        if (string.IsNullOrEmpty(path)) return null;
        var uses = Int(e, "useCount");
        if (uses == 0) uses = Arr(e, "usedBy").Count;
        return new Rec { Kind = "texture", Name = path, UseCount = uses, File = Str(e, "file") ?? "", Line = Int(e, "line") };
    }

    private static Rec? ParseTemplate(JsonElement e)
    {
        var name = Str(e, "name");
        if (string.IsNullOrEmpty(name)) return null;

        var rec = new Rec
        {
            Kind = "template",
            Name = name,
            Type = Str(e, "type") ?? "",
            File = Str(e, "file") ?? "",
            Line = Int(e, "line"),
            Inherits = Str(e, "inherits"),
            UseCount = Int(e, "useCount"),
            Hidden = Bool(e, "hidden"),
            Textures = Arr(e, "textures"),
            Scripts = Arr(e, "scripts"),
            Calls = Arr(e, "calls")
        };

        // `effective` is precomputed by framexml_split.py: 86 templates inherit another
        // template and 42 take their SIZE from an ancestor, so the resolved block is the
        // only correct source for size/hidden/scripts. Fall back to the raw record only
        // when the catalog was not split.
        if (e.TryGetProperty("effective", out var eff) && eff.ValueKind == JsonValueKind.Object)
        {
            rec.Chain = Arr(eff, "chain");
            rec.SizeFrom = Str(eff, "sizeFrom");
            rec.Hidden = Bool(eff, "hidden") || rec.Hidden;
            var t = Arr(eff, "textures"); if (t.Count > 0) rec.Textures = t;
            var s = Arr(eff, "scripts"); if (s.Count > 0) rec.Scripts = s;
            var c = Arr(eff, "calls"); if (c.Count > 0) rec.Calls = c;
            ReadSize(eff, rec);
        }
        if (rec.Width == 0 && rec.Height == 0) ReadSize(e, rec);
        return rec;
    }

    private static void ReadSize(JsonElement e, Rec rec)
    {
        if (!e.TryGetProperty("size", out var sz) || sz.ValueKind != JsonValueKind.Object) return;
        rec.Width = Int(sz, "w");
        rec.Height = Int(sz, "h");
    }

    // ---------------------------------------------------------------- queries

    public object Status()
    {
        var files = new[] { "meta.json", "templates.json", "textures.json", "fontObjects.json", "frames.json", "api.json" }
            .Select(n =>
            {
                var fi = new FileInfo(Path.Combine(_dir, n));
                return (object)new
                {
                    name = n,
                    present = fi.Exists,
                    sizeKb = fi.Exists ? Math.Round(fi.Length / 1024.0, 1) : 0
                };
            }).ToList();

        return new
        {
            ready = _templates.Count > 0,
            directory = _dir,
            counts = new
            {
                templates = _templates.Count,
                frames = _frames.Count,
                textures = _textures.Count,
                api = _api.Count
            },
            capabilities = _caps is not null,
            command = "python framexml_split.py framexml_index.json -o wwwroot/data/framexml",
            files
        };
    }

    public object Capabilities() => _caps ?? (object)new { note = "wwwroot/data/superui-capabilities.json not deployed" };

    private IEnumerable<Rec> Pool(string scope) => scope switch
    {
        "templates" => _templates,
        "frames" => _frames,
        "textures" => _textures,
        "api" => _api,
        _ => _templates.Concat(_api).Concat(_frames).Concat(_textures)
    };

    /// <summary>Exact &gt; prefix &gt; substring, then by how hard Blizzard leans on it —
    /// a template with 95 uses is a safer answer than one with none.</summary>
    public object Search(string q, string scope, int take)
    {
        var query = q.Trim();
        if (query.Length == 0)
            return new { ready = _templates.Count > 0, query, hits = Array.Empty<object>() };

        var lower = query.ToLowerInvariant();
        var scored = new List<(int Score, Rec R)>();

        foreach (var r in Pool(scope))
        {
            var n = r.Name.ToLowerInvariant();
            int s = n == lower ? 1000
                : n.StartsWith(lower, StringComparison.Ordinal) ? 500
                : n.Contains(lower, StringComparison.Ordinal) ? 200
                : r.File.Contains(lower, StringComparison.OrdinalIgnoreCase) ? 40
                : -1;
            if (s < 0) continue;
            if (r.Kind == "template") s += 30;                 // the palette is the point
            scored.Add((s + Math.Min(r.UseCount, 99), r));
        }

        var hits = scored.OrderByDescending(x => x.Score).Take(take)
            .Select(x => (object)x.R.ToDto()).ToList();

        return new { ready = _templates.Count > 0, query, count = scored.Count, hits };
    }

    public object? Record(string kind, string name)
    {
        var r = Pool(kind == "template" ? "templates" : kind == "texture" ? "textures" : kind == "frame" ? "frames" : "api")
            .FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        return r?.ToDto();
    }

    // ---------------------------------------------------------------- context

    public sealed record ContextResult(string Text, List<string> Sections, bool Truncated);

    /// <summary>
    /// The paste-into-a-chat bundle. Order is deliberate: rules first (they are absolute
    /// and cheap), then the records that answer the question with citations, then prose,
    /// then the capability manifest. Budget is spent top-down, so a small budget still
    /// carries the parts that prevent wrong answers.
    /// </summary>
    public ContextResult BuildContext(string q, int budget, List<WikiSearchHit> prose, bool rules, bool caps)
    {
        var sb = new StringBuilder();
        var sections = new List<string>();
        bool truncated = false;

        void Section(string name, string body)
        {
            if (truncated || body.Length == 0) return;
            if (sb.Length + body.Length > budget) { truncated = true; return; }
            sb.Append(body);
            sections.Add(name);
        }

        sb.Append("# WoW 1.12.1 client-side ground truth");
        if (q.Length > 0) sb.Append(" — query: \"").Append(q).Append('"');
        sb.Append("\n\nSource: MangosSuperUI FrameXML catalog, harvested from the real 1.12.1 ")
          .Append("Interface\\FrameXML folder. Every record below cites file and line. ")
          .Append("Prefer these over recalled knowledge; where they disagree, these win.\n\n");

        if (rules) Section("rules", Rules());

        var query = q.Trim().ToLowerInvariant();
        if (query.Length > 0)
        {
            Section("templates", RenderTemplates(query));
            Section("api", RenderApi(query));
            Section("frames", RenderFrames(query));
            Section("textures", RenderTextures(query));
        }

        if (prose.Count > 0) Section("docs", RenderProse(prose));
        if (caps) Section("capabilities", RenderCaps());

        return new ContextResult(sb.ToString(), sections, truncated);
    }

    private static string Rules() =>
        "## Hard rules for 1.12 (Lua 5.0) — these are absolute\n\n" +
        "- `SetPoint` takes 5 arguments, always: point, relativeTo, relativePoint, x, y.\n" +
        "- Handlers read the globals `this`, `event`, `arg1`..`argN`. There is no `self` parameter.\n" +
        "- No `string.match`, no `string.gmatch`, no `#` length operator. Use `string.find`, `string.sub`, `for i = 1, n do`.\n" +
        "- Layout belongs in XML, which validates against `Interface\\FrameXML\\UI.xsd`. Lua is for behaviour.\n" +
        "- Positioning is ANCHOR-based, never absolute x/y. A widget with no anchor lands at its parent's centre.\n" +
        "- Textures and FontStrings are leaves inside `<Layers>`, grouped by draw layer.\n" +
        "  Paint order is BACKGROUND < BORDER < ARTWORK < OVERLAY < HIGHLIGHT, and it is load-bearing.\n" +
        "- A template that declares no size anywhere in its inheritance chain renders INVISIBLE unless you set width and height.\n" +
        "- A template with `hidden=\"true\"` needs an explicit `:Show()`.\n" +
        "- A template that hardcodes an `OnClick` in its XML must be overridden with `SetScript`, or you call Blizzard's handler.\n" +
        "- For a load-on-demand Blizzard addon (e.g. `Blizzard_TalentUI`), gate on `ADDON_LOADED`.\n" +
        "  Hooking its toggle global does not work: the addon redefines that global when it loads and eats the hook.\n" +
        "- One mail carries ONE attachment. That is the client's packet writer; no core change lifts it. Batch instead.\n\n";

    private string RenderTemplates(string q)
    {
        var hits = _templates
            .Where(t => t.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(t => t.UseCount).Take(8).ToList();
        if (hits.Count == 0) return "";

        var sb = new StringBuilder("## Templates\n\n");
        foreach (var t in hits)
        {
            sb.Append("### ").Append(t.Name).Append(" — ").Append(t.Type).Append('\n');
            sb.Append("- source: `").Append(t.File).Append(':').Append(t.Line).Append("`\n");
            sb.Append("- size: ").Append(t.Width > 0 || t.Height > 0
                ? $"{t.Width}x{t.Height}" + (t.SizeFrom is not null && t.SizeFrom != t.Name ? $" (inherited from {t.SizeFrom})" : "")
                : "**none declared — you must set width and height or it renders invisible**").Append('\n');
            if (t.Chain.Count > 1) sb.Append("- inherits: ").Append(string.Join(" <- ", t.Chain.Skip(1))).Append('\n');
            if (t.Hidden) sb.Append("- hidden=\"true\" — call :Show() at runtime\n");
            if (t.Scripts.Count > 0) sb.Append("- template scripts: ").Append(string.Join(", ", t.Scripts)).Append('\n');
            if (t.Calls.Count > 0) sb.Append("- hardcodes XML handler calls: ").Append(string.Join(", ", t.Calls))
                                     .Append(" — override with SetScript\n");
            if (t.Textures.Count > 0) sb.Append("- textures: ").Append(string.Join(", ", t.Textures.Take(5))).Append('\n');
            sb.Append("- used ").Append(t.UseCount).Append(" times in Blizzard's own UI")
              .Append(t.UseCount == 0 ? " — UNUSED, an untested path" : "").Append("\n\n");
        }
        return sb.ToString();
    }

    private string RenderApi(string q)
    {
        var hits = _api.Where(r => r.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).Take(20).ToList();
        if (hits.Count == 0) return "";
        var sb = new StringBuilder("## Lua API (defined in FrameXML, not the client binary)\n\n");
        foreach (var r in hits)
            sb.Append("- `").Append(r.Name).Append("` (").Append(r.Kind).Append(") — `")
              .Append(r.File).Append(':').Append(r.Line).Append("`\n");
        return sb.Append('\n').ToString();
    }

    private string RenderFrames(string q)
    {
        var hits = _frames.Where(r => r.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).Take(15).ToList();
        if (hits.Count == 0) return "";
        var sb = new StringBuilder("## Named frames that exist at runtime\n\n");
        foreach (var r in hits)
            sb.Append("- `").Append(r.Name).Append('`').Append(r.Type.Length > 0 ? " (" + r.Type + ")" : "")
              .Append(" — `").Append(r.File).Append(':').Append(r.Line).Append("`\n");
        return sb.Append('\n').ToString();
    }

    private string RenderTextures(string q)
    {
        var hits = _textures.Where(r => r.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).Take(12).ToList();
        if (hits.Count == 0) return "";
        var sb = new StringBuilder("## Texture paths (real, shipped in the client)\n\n");
        foreach (var r in hits) sb.Append("- `").Append(r.Name).Append("`\n");
        return sb.Append('\n').ToString();
    }

    private static string RenderProse(List<WikiSearchHit> prose)
    {
        var sb = new StringBuilder("## Project documentation\n\n");
        foreach (var h in prose)
            sb.Append("- **").Append(h.Title).Append("** (").Append(h.Kind).Append(") — `/Wiki?path=")
              .Append(h.Path).Append("`\n  ").Append(h.Snippet.Replace('\n', ' ')).Append('\n');
        return sb.Append('\n').ToString();
    }

    private string RenderCaps()
    {
        if (_caps is null || _caps.Value.ValueKind != JsonValueKind.Object) return "";
        if (!_caps.Value.TryGetProperty("capabilities", out var arr) || arr.ValueKind != JsonValueKind.Array) return "";

        var sb = new StringBuilder("## What MangosSuperUI can actually do\n\n");
        if (_caps.Value.TryGetProperty("note", out var note) && note.ValueKind == JsonValueKind.String)
            sb.Append(note.GetString()).Append("\n\n");

        foreach (var c in arr.EnumerateArray())
        {
            var title = Str(c, "title"); if (title is null) continue;
            sb.Append("- **").Append(title).Append("** (").Append(Str(c, "direction") ?? "read").Append(") — ")
              .Append(Str(c, "what") ?? "").Append('\n');
            var route = Str(c, "route"); if (route is not null) sb.Append("  route: `").Append(route).Append("`\n");
            var code = Str(c, "code"); if (code is not null) sb.Append("  code: `").Append(code).Append("`\n");
            var caveat = Str(c, "caveat"); if (caveat is not null) sb.Append("  caveat: ").Append(caveat).Append('\n');
        }
        return sb.Append('\n').ToString();
    }

    // ------------------------------------------------------------------ model

    public sealed class Rec
    {
        public string Kind = "";
        public string Name = "";
        public string Type = "";
        public string File = "";
        public int Line;
        public string? Inherits;
        public int UseCount;
        public bool Hidden;
        public int Width;
        public int Height;
        public string? SizeFrom;
        public List<string> Chain = new();
        public List<string> Textures = new();
        public List<string> Scripts = new();
        public List<string> Calls = new();

        public object ToDto() => new
        {
            kind = Kind,
            name = Name,
            type = Type,
            file = File,
            line = Line,
            inherits = Inherits,
            useCount = UseCount,
            hidden = Hidden,
            width = Width,
            height = Height,
            sizeFrom = SizeFrom,
            needsSize = Kind == "template" && Width == 0 && Height == 0
                        && Type != "Font" && Type != "FontString",
            chain = Chain,
            textures = Textures,
            scripts = Scripts,
            calls = Calls
        };
    }
}
