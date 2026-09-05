using MangosSuperUI.Models;
using MangosSuperUI.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;

namespace MangosSuperUI.Controllers;

public class SourceMapController : Controller
{
    private readonly SourceIndexerService _indexer;
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;

    // Fallback: lazy-load old function-graph.json if no index exists yet
    private static object? _legacyGraph;
    private static readonly object _legacyLock = new();

    public SourceMapController(SourceIndexerService indexer, IConfiguration config, IWebHostEnvironment env)
    {
        _indexer = indexer;
        _config = config;
        _env = env;
    }

    // ──────────── Page ────────────

    public IActionResult Index() => View();

    // ──────────── Index Stats ────────────

    [HttpGet]
    public IActionResult Stats()
    {
        var idx = _indexer.GetIndex();
        if (idx != null)
        {
            // Build stats from live index
            var classStats = idx.Types.Values
                .Where(t => t.Kind == "class")
                .OrderByDescending(t => t.QualifiedMethods.Count)
                .Take(20)
                .Select(t => new { name = t.Name, function_count = t.QualifiedMethods.Count, functions = t.QualifiedMethods })
                .ToList();

            var fileStats = idx.Files.Values
                .Where(f => f.Extension == ".cpp")
                .OrderByDescending(f => f.DefinedSymbols.Count)
                .Take(20)
                .Select(f => new { path = f.Path, function_count = f.DefinedSymbols.Count, functions = f.DefinedSymbols, line_count = f.LineCount })
                .ToList();

            // Top hub symbols (most total connections)
            var hubSymbols = idx.Symbols.Values
                .OrderByDescending(s => s.CallsOut.Count + s.CalledBy.Count)
                .Take(15)
                .Select(s => new { id = s.Id, qualified = s.Id, total_connections = s.CallsOut.Count + s.CalledBy.Count, call_count = s.CallsOut.Count, caller_count = s.CalledBy.Count })
                .ToList();

            // Most called (most callers)
            var mostCalled = idx.Symbols.Values
                .OrderByDescending(s => s.CalledBy.Count)
                .Take(15)
                .Select(s => new { id = s.Id, qualified = s.Id, caller_count = s.CalledBy.Count })
                .ToList();

            // Most complex (most calls out)
            var mostComplex = idx.Symbols.Values
                .OrderByDescending(s => s.CallsOut.Count)
                .Take(15)
                .Select(s => new { id = s.Id, qualified = s.Id, call_count = s.CallsOut.Count, complexity = s.Complexity })
                .ToList();

            // Deepest (placeholder — we'd need to compute max_depth via recursive walk)
            // For now, use complexity as a proxy
            var deepest = idx.Symbols.Values
                .OrderByDescending(s => s.Complexity)
                .Take(15)
                .Select(s => new { id = s.Id, qualified = s.Id, complexity = s.Complexity, line_count = s.LineCount })
                .ToList();

            return Json(new
            {
                meta = new
                {
                    total_functions = idx.TotalSymbols,
                    total_types = idx.TotalTypes,
                    total_enums = idx.TotalEnums,
                    total_files = idx.TotalFiles,
                    total_lines = idx.TotalLines,
                    total_edges = idx.Symbols.Values.Sum(s => s.CallsOut.Count),
                    indexed_at = idx.IndexedAt,
                    source_path = idx.SourcePath
                },
                stats = new
                {
                    hub_functions = hubSymbols,
                    top_callers = mostCalled,
                    top_complex = mostComplex,
                    top_deep = deepest
                },
                classes = classStats.ToDictionary(c => c.name, c => (object)c),
                files = fileStats.ToDictionary(f => f.path, f => (object)f),
                source = "live_index"
            });
        }

        // Fallback to legacy graph
        var legacy = LoadLegacyGraph();
        if (legacy == null)
            return Json(new
            {
                meta = new { total_functions = 0, total_types = 0, total_enums = 0, total_files = 0, total_lines = 0, total_edges = 0 },
                stats = new { hub_functions = Array.Empty<object>(), top_callers = Array.Empty<object>(), top_complex = Array.Empty<object>(), top_deep = Array.Empty<object>() },
                classes = new Dictionary<string, object>(),
                files = new Dictionary<string, object>(),
                source = "none"
            });

        return Json(new
        {
            meta = legacy.Value.GetProperty("_meta"),
            stats = legacy.Value.GetProperty("stats"),
            classes = legacy.Value.GetProperty("classes"),
            files = legacy.Value.GetProperty("files"),
            source = "legacy"
        });
    }

    // ──────────── Search ────────────

    [HttpGet]
    public IActionResult Search([FromQuery] string q, [FromQuery] string kind = "all")
    {
        if (string.IsNullOrWhiteSpace(q))
            return Json(new { results = Array.Empty<object>() });

        // The live index knows "symbol|type|enum|file"; the legacy graph ignored kind, so
        // callers learned to pass "function"/"method" and silently got nothing back once
        // the live index existed. Fold the natural aliases instead of returning [].
        kind = NormalizeKind(kind);

        var idx = _indexer.GetIndex();
        if (idx != null)
        {
            var results = _indexer.Search(q, kind);
            return Json(new { results, source = "live_index" });
        }

        // Fallback to legacy search
        var legacy = LoadLegacyGraph();
        if (legacy == null) return Json(new { results = Array.Empty<object>() });

        var nodes = legacy.Value.GetProperty("nodes");
        var query = q.Trim().ToLowerInvariant();
        var legacyResults = new List<object>();

        foreach (var prop in nodes.EnumerateObject())
        {
            var node = prop.Value;
            var qualified = node.GetProperty("qualified").GetString() ?? "";
            var sig = node.GetProperty("signature").GetString() ?? "";

            if (qualified.ToLowerInvariant().Contains(query) || sig.ToLowerInvariant().Contains(query))
            {
                legacyResults.Add(new
                {
                    id = prop.Name,
                    Name = qualified,
                    type = "symbol",
                    Kind = "method",
                    className = node.GetProperty("class").GetString(),
                    file = node.GetProperty("file").GetString(),
                    callCount = node.GetProperty("call_count").GetInt32(),
                    callerCount = node.GetProperty("caller_count").GetInt32(),
                });
                if (legacyResults.Count >= 50) break;
            }
        }

        return Json(new { results = legacyResults, source = "legacy" });
    }

    // ──────────── Smart Search (resolves C++ expressions) ────────────

    [HttpGet]
    public IActionResult SmartSearch([FromQuery] string q)
    {
        if (string.IsNullOrWhiteSpace(q))
            return Json(new { results = Array.Empty<object>(), parsed = (object?)null });

        var result = _indexer.SmartSearch(q);
        return Json(result);
    }

    // ──────────── Symbol Detail ────────────

    [HttpGet]
    public IActionResult Symbol([FromQuery] string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return BadRequest(new { error = "id required" });

        var idx = _indexer.GetIndex();
        if (idx != null)
        {
            var sym = _indexer.GetSymbol(id);
            if (sym == null) return NotFound(new { error = $"Symbol '{id}' not found" });

            return Json(new
            {
                symbol = sym,
                source = "live_index"
            });
        }

        return NotFound(new { error = "No index available" });
    }

    // ──────────── Node (graph view — compatible with old FunctionGraph format) ────────────

    [HttpGet]
    public IActionResult Node([FromQuery] string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return BadRequest(new { error = "id required" });

        var idx = _indexer.GetIndex();
        if (idx != null)
        {
            var sym = _indexer.GetSymbol(id);
            if (sym == null) return NotFound(new { error = $"Node '{id}' not found" });

            var neighbors = new List<object>();

            // Outgoing calls
            foreach (var callId in sym.CallsOut)
            {
                var target = _indexer.GetSymbol(callId);
                if (target == null) continue;
                var dir = sym.CalledBy.Contains(callId) ? "both" : "outgoing";
                neighbors.Add(BuildNeighborObj(target, dir));
            }

            // Incoming callers
            foreach (var callerId in sym.CalledBy)
            {
                if (sym.CallsOut.Contains(callerId)) continue; // already added as "both"
                var caller = _indexer.GetSymbol(callerId);
                if (caller == null) continue;
                neighbors.Add(BuildNeighborObj(caller, "incoming"));
            }

            return Json(new
            {
                center = new
                {
                    id = sym.Id,
                    qualified = sym.Id,
                    name = sym.Name,
                    className = sym.MemberOf ?? "",
                    file = sym.DefinedInFile ?? "",
                    line_start = sym.BodyLineStart,
                    line_end = sym.BodyLineEnd,
                    line_count = sym.LineCount,
                    signature = sym.Signature,
                    call_count = sym.CallsOut.Count,
                    caller_count = sym.CalledBy.Count,
                    max_depth = sym.Complexity, // using complexity as depth proxy for now
                    total_connections = sym.CallsOut.Count + sym.CalledBy.Count,
                    kind = sym.Kind,
                    returnType = sym.ReturnType,
                    isVirtual = sym.IsVirtual,
                    isStatic = sym.IsStatic,
                    isConst = sym.IsConst,
                    usesTypes = sym.UsesTypes,
                    memberOf = sym.MemberOf,
                    declaredIn = sym.DeclaredIn
                },
                neighbors,
                source = "live_index"
            });
        }

        // Fallback to legacy
        var legacy = LoadLegacyGraph();
        if (legacy == null) return NotFound(new { error = "No index available" });

        var nodes = legacy.Value.GetProperty("nodes");
        if (!nodes.TryGetProperty(id, out var centerNode))
            return NotFound(new { error = $"Node '{id}' not found" });

        return Json(BuildLegacyNodeResponse(nodes, id, centerNode));
    }

    // ──────────── Type Detail ────────────

    [HttpGet]
    public IActionResult Type([FromQuery] string name)
    {
        var t = _indexer.GetType(name ?? "");
        if (t == null) return NotFound(new { error = $"Type '{name}' not found" });
        return Json(new { type = t });
    }

    // ──────────── Enum Detail ────────────

    [HttpGet]
    public IActionResult Enum([FromQuery] string name)
    {
        var e = _indexer.GetEnum(name ?? "");
        if (e == null) return NotFound(new { error = $"Enum '{name}' not found" });
        return Json(new { @enum = e });
    }

    // ──────────── File Detail ────────────

    [HttpGet]
    public IActionResult File([FromQuery] string path)
    {
        var f = _indexer.GetFile(path ?? "");
        if (f == null)
        {
            // Accept a bare file name or any unique path suffix ("AiBotAIMain.cpp",
            // "SuiBots/AiBotAIMain.cpp") — the Circuit site manifest only carries "cpp/<name>".
            var resolved = ResolveFile(path ?? "", out var ambiguous);
            if (resolved != null) f = _indexer.GetFile(resolved);
            if (f == null)
                return NotFound(new { error = $"File '{path}' not found", ambiguous });
        }
        return Json(new { file = f });
    }

    // ──────────── Inheritance Chain ────────────

    [HttpGet]
    public IActionResult InheritanceChain([FromQuery] string type)
    {
        var idx = _indexer.GetIndex();
        if (idx == null) return NotFound(new { error = "No index" });

        var chain = new List<object>();
        var visited = new HashSet<string>();

        // Walk up
        void WalkUp(string name, int depth)
        {
            if (visited.Contains(name) || depth > 20) return;
            visited.Add(name);
            if (!idx.Types.TryGetValue(name, out var t)) return;
            chain.Add(new { name = t.Name, kind = t.Kind, inherits = t.Inherits, inheritedBy = t.InheritedBy, depth, declaredIn = t.DeclaredIn });
            foreach (var baseName in t.Inherits)
                WalkUp(baseName, depth - 1);
        }

        // Walk down
        void WalkDown(string name, int depth)
        {
            if (visited.Contains(name) || depth > 20) return;
            visited.Add(name);
            if (!idx.Types.TryGetValue(name, out var t)) return;
            chain.Add(new { name = t.Name, kind = t.Kind, inherits = t.Inherits, inheritedBy = t.InheritedBy, depth, declaredIn = t.DeclaredIn });
            foreach (var child in t.InheritedBy)
                WalkDown(child, depth + 1);
        }

        WalkUp(type ?? "", 0);
        if (idx.Types.TryGetValue(type ?? "", out var root))
            foreach (var child in root.InheritedBy)
                WalkDown(child, 1);

        return Json(new { chain });
    }

    // ──────────── Function Body ────────────

    [HttpGet]
    public IActionResult Body([FromQuery] string id, [FromQuery] string format = "json")
    {
        var idx = _indexer.GetIndex();
        if (idx == null) return NotFound(new { error = "No index" });

        var sym = _indexer.GetSymbol(id ?? "");
        if (sym == null) return NotFound(new { error = $"Symbol '{id}' not found" });

        if (sym.DefinedInFile == null || sym.BodyLineStart == 0)
            return Json(new { body = "", signature = sym.Signature, note = "No body found (declaration only)" });

        var lines = ReadSourceLines(idx, sym.DefinedInFile);
        if (lines == null)
            return Json(new { body = "", signature = sym.Signature, note = "Source file not found" });

        if (IsText(format))
        {
            // Plain text with line numbers: no JSON escaping (> for every '>'), so a
            // reader that only wants the code pays for the code. ~15% smaller than json.
            var t = new StringBuilder();
            t.AppendLine($"// {sym.Id}  —  {sym.DefinedInFile}:{sym.BodyLineStart}-{sym.BodyLineEnd}");
            AppendNumbered(t, lines, sym.BodyLineStart, sym.BodyLineEnd);
            return Content(t.ToString(), "text/plain; charset=utf-8");
        }

        var sb = new StringBuilder();
        for (int i = sym.BodyLineStart - 1; i < sym.BodyLineEnd && i < lines.Length; i++)
            sb.AppendLine(lines[i]);

        return Json(new { body = sb.ToString().TrimEnd(), signature = sym.Signature, file = sym.DefinedInFile, lineStart = sym.BodyLineStart, lineEnd = sym.BodyLineEnd });
    }

    // ──────────── At (file:line window, plain text) ────────────
    //
    // The Circuit site manifest names code as "AiBotAICombat.cpp:448". This turns that into
    // a numbered source window plus the enclosing function, in one call — the thing that
    // otherwise costs an ssh + sed per site. `file` may be a bare name or any unique suffix.

    [HttpGet]
    public IActionResult At([FromQuery] string file, [FromQuery] int line, [FromQuery] int before = 30,
        [FromQuery] int after = 30, [FromQuery] string format = "text")
    {
        var idx = _indexer.GetIndex();
        if (idx == null) return NotFound(new { error = "No index" });
        if (string.IsNullOrWhiteSpace(file) || line <= 0) return BadRequest(new { error = "file and line required" });

        var rel = ResolveFile(file, out var ambiguous);
        if (rel == null) return NotFound(new { error = $"File '{file}' not found", ambiguous });
        var lines = ReadSourceLines(idx, rel);
        if (lines == null) return NotFound(new { error = $"File '{rel}' not on disk" });

        before = Math.Clamp(before, 0, 400);
        after = Math.Clamp(after, 0, 400);
        int start = Math.Max(1, line - before);
        int end = Math.Min(lines.Length, line + after);
        var enclosing = EnclosingSymbol(idx, rel, line);

        if (!IsText(format))
        {
            return Json(new
            {
                file = rel,
                line,
                start,
                end,
                enclosing = enclosing == null ? null : new { id = enclosing.Id, signature = enclosing.Signature, enclosing.BodyLineStart, enclosing.BodyLineEnd },
                lines = lines.Skip(start - 1).Take(end - start + 1).ToArray()
            });
        }

        var t = new StringBuilder();
        t.AppendLine(enclosing == null
            ? $"// {rel}:{line}  —  lines {start}-{end} (no enclosing symbol)"
            : $"// {rel}:{line}  —  lines {start}-{end}  —  in {enclosing.Id} ({enclosing.BodyLineStart}-{enclosing.BodyLineEnd})");
        AppendNumbered(t, lines, start, end, mark: line);
        return Content(t.ToString(), "text/plain; charset=utf-8");
    }

    // ──────────── Outline (a function's probe skeleton, plain text) ────────────
    //
    // Circuit traces are sequences of probe descriptions ("cpp-combat: stalemate nudge hop").
    // To map a trace back onto the logic you rarely need the whole body — you need WHERE each
    // probe sits, what the function calls, and who calls it. For HandleCombatStalemate that
    // is ~1.5 KB instead of the 10 KB body. Probes: CB_HIT / CB_HITV / CircuitTrace.Hit; the
    // sLog tags are kept too because Server.log lines are the other half of the evidence.

    [HttpGet]
    public IActionResult Outline([FromQuery] string id, [FromQuery] string? file = null, [FromQuery] int line = 0)
    {
        var idx = _indexer.GetIndex();
        if (idx == null) return NotFound(new { error = "No index" });

        SymbolEntry? sym = null;
        if (!string.IsNullOrWhiteSpace(id)) sym = _indexer.GetSymbol(id);
        else if (!string.IsNullOrWhiteSpace(file) && line > 0)
        {
            var rel = ResolveFile(file, out _);
            if (rel != null) sym = EnclosingSymbol(idx, rel, line);
        }
        if (sym == null) return NotFound(new { error = "Symbol not found (pass id=, or file=&line=)" });
        if (sym.DefinedInFile == null || sym.BodyLineStart == 0)
            return Content($"// {sym.Id}: declaration only ({sym.DeclaredIn})", "text/plain; charset=utf-8");

        var lines = ReadSourceLines(idx, sym.DefinedInFile);
        if (lines == null) return NotFound(new { error = "Source file not on disk" });

        var t = new StringBuilder();
        t.AppendLine($"{sym.Signature}");
        t.AppendLine($"  {sym.DefinedInFile}:{sym.BodyLineStart}-{sym.BodyLineEnd}  ({sym.LineCount} lines, complexity {sym.Complexity})");
        t.AppendLine("probes / log tags (line: text):");
        int shown = 0, lastPrinted = -1;
        for (int i = sym.BodyLineStart - 1; i < sym.BodyLineEnd && i < lines.Length; i++)
        {
            string s = lines[i];
            if (s.Contains("CB_HIT") || s.Contains("CircuitTrace.Hit") || s.Contains("[AIBOT") || s.Contains("sLog.Out"))
            {
                // Collapse a multi-line sLog.Out to the line that carries the tag text, and
                // don't print that tag line again when the loop reaches it.
                int at = i;
                if (s.Contains("sLog.Out") && !s.Contains('"') && i + 2 < lines.Length)
                {
                    if (lines[i + 1].Contains('"')) at = i + 1;
                    else if (lines[i + 2].Contains('"')) at = i + 2;
                    else continue;
                    s = lines[at];
                }
                if (at <= lastPrinted) continue;
                lastPrinted = at;
                t.AppendLine($"  {at + 1,6}: {s.Trim()}");
                shown++;
            }
        }
        if (shown == 0) t.AppendLine("  (none)");
        var calls = sym.CallsOut.Distinct().OrderBy(c => c, StringComparer.Ordinal).ToList();
        t.AppendLine($"calls ({calls.Count}): {string.Join(", ", calls)}");
        var callers = sym.CalledBy.Distinct().OrderBy(c => c, StringComparer.Ordinal).ToList();
        t.AppendLine($"called by ({callers.Count}): {string.Join(", ", callers)}");
        return Content(t.ToString(), "text/plain; charset=utf-8");
    }

    // ──────────── Grep (literal search across the indexed tree, plain text) ────────────
    //
    // "where is m_stalemateNudges reset?" without an ssh. Literal, case-insensitive by
    // default; `dir` narrows to a path fragment (e.g. SuiBots). Capped so a wide query can't
    // return the tree.

    [HttpGet]
    public IActionResult Grep([FromQuery] string q, [FromQuery] string? dir = null, [FromQuery] int max = 60,
        [FromQuery] bool caseSensitive = false)
    {
        var idx = _indexer.GetIndex();
        if (idx == null) return NotFound(new { error = "No index" });
        if (string.IsNullOrWhiteSpace(q)) return BadRequest(new { error = "q required" });
        max = Math.Clamp(max, 1, 500);
        var cmp = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        var t = new StringBuilder();
        int hits = 0, filesHit = 0;
        foreach (var rel in idx.Files.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            if (dir != null && !rel.Contains(dir, StringComparison.OrdinalIgnoreCase)) continue;
            var lines = ReadSourceLines(idx, rel);
            if (lines == null) continue;
            bool any = false;
            for (int i = 0; i < lines.Length && hits < max; i++)
            {
                if (lines[i].Contains(q, cmp))
                {
                    t.AppendLine($"{rel}:{i + 1}: {lines[i].Trim()}");
                    hits++; any = true;
                }
            }
            if (any) filesHit++;
            if (hits >= max) { t.AppendLine($"// capped at {max} hits — narrow with dir= or a longer q"); break; }
        }
        t.Insert(0, $"// grep '{q}'{(dir != null ? $" in *{dir}*" : "")}: {hits} hit(s) in {filesHit} file(s)\n");
        return Content(t.ToString(), "text/plain; charset=utf-8");
    }

    // ──────────── Trace Export ────────────

    [HttpGet]
    public IActionResult ExportTrace([FromQuery] string root, [FromQuery] int depth = 2,
        [FromQuery] bool types = true, [FromQuery] bool headers = true, [FromQuery] string format = "json")
    {
        var idx = _indexer.GetIndex();
        if (idx != null)
        {
            var trace = _indexer.ExportTrace(root ?? "", depth, types, headers);
            if (trace == null) return NotFound(new { error = $"Symbol '{root}' not found" });

            if (format == "text")
            {
                var safeName = (root ?? "trace").Replace("::", "_").Replace("/", "_").Replace(" ", "_");
                return File(Encoding.UTF8.GetBytes(trace.FormattedText), "text/plain", $"{safeName}_trace_d{depth}.txt");
            }

            return Json(trace);
        }

        // Fallback to legacy export tree
        var legacy = LoadLegacyGraph();
        if (legacy == null) return NotFound(new { error = "No index" });

        var nodes = legacy.Value.GetProperty("nodes");
        if (!nodes.TryGetProperty(root ?? "", out _))
            return NotFound(new { error = $"Node '{root}' not found" });

        var expanded = new HashSet<string>();
        var tree = BuildLegacyCallTree(nodes, root!, new HashSet<string>(), expanded, 0);
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(tree, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            MaxDepth = 1024
        });
        var safeName2 = root!.Replace("::", "_").Replace("/", "_").Replace(" ", "_");
        return File(jsonBytes, "application/json", $"{safeName2}_calltree.json");
    }

    // ──────────── Topic Explorer ────────────

    [HttpGet]
    public IActionResult TopicExplore([FromQuery] string q, [FromQuery] string format = "json")
    {
        if (string.IsNullOrWhiteSpace(q))
            return BadRequest(new { error = "Query required" });

        var report = _indexer.ExploreTopic(q);
        if (report == null)
            return Json(new { query = q, totalSymbols = 0, message = "No matches found" });

        if (format == "text")
        {
            var safeName = q.Replace(" ", "_").Replace("::", "_");
            return File(Encoding.UTF8.GetBytes(report.FormattedText), "text/plain", $"topic_{safeName}.txt");
        }

        return Json(report);
    }

    // ──────────── File Content (raw source file) ────────────

    [HttpGet]
    public IActionResult FileContent([FromQuery] string path)
    {
        var idx = _indexer.GetIndex();
        if (idx == null) return NotFound(new { error = "No index" });
        if (string.IsNullOrWhiteSpace(path)) return BadRequest(new { error = "path required" });

        var fullPath = Path.Combine(idx.SourcePath, path);
        if (!System.IO.File.Exists(fullPath))
            return NotFound(new { error = $"File '{path}' not found on disk" });

        var content = System.IO.File.ReadAllText(fullPath);
        var lineCount = content.Split('\n').Length;
        return Json(new { path, content, lineCount });
    }

    // ──────────── Research Bundle (batch fetch for Deep Dive) ────────────

    [HttpPost]
    public IActionResult ResearchBundle([FromBody] ResearchBundleRequest request)
    {
        var idx = _indexer.GetIndex();
        if (idx == null) return NotFound(new { error = "No index" });

        var result = new ResearchBundleResult();

        // Full file contents
        if (request.Files != null)
        {
            foreach (var filePath in request.Files)
            {
                var fullPath = Path.Combine(idx.SourcePath, filePath);
                if (System.IO.File.Exists(fullPath))
                {
                    result.Files.Add(new BundleFile
                    {
                        Path = filePath,
                        Content = System.IO.File.ReadAllText(fullPath),
                        LineCount = System.IO.File.ReadAllLines(fullPath).Length
                    });
                }
            }
        }

        // Function bodies
        if (request.Functions != null)
        {
            foreach (var funcId in request.Functions)
            {
                var sym = _indexer.GetSymbol(funcId);
                if (sym == null) continue;
                if (sym.DefinedInFile == null || sym.BodyLineStart == 0)
                {
                    result.Functions.Add(new BundleFunction { Id = funcId, Signature = sym.Signature, Body = "", Note = "Declaration only, no body" });
                    continue;
                }
                var fullPath = Path.Combine(idx.SourcePath, sym.DefinedInFile);
                if (!System.IO.File.Exists(fullPath)) continue;
                var lines = System.IO.File.ReadAllLines(fullPath);
                var sb = new StringBuilder();
                for (int i = sym.BodyLineStart - 1; i < sym.BodyLineEnd && i < lines.Length; i++)
                    sb.AppendLine(lines[i]);
                result.Functions.Add(new BundleFunction
                {
                    Id = funcId,
                    Signature = sym.Signature,
                    File = sym.DefinedInFile,
                    LineStart = sym.BodyLineStart,
                    LineEnd = sym.BodyLineEnd,
                    Body = sb.ToString().TrimEnd()
                });
            }
        }

        // Type definitions
        if (request.Types != null)
        {
            foreach (var typeName in request.Types)
            {
                var t = _indexer.GetType(typeName);
                if (t != null)
                    result.Types.Add(t);
            }
        }

        // Enum definitions
        if (request.Enums != null)
        {
            foreach (var enumName in request.Enums)
            {
                var e = _indexer.GetEnum(enumName);
                if (e != null)
                    result.Enums.Add(e);
            }
        }

        return Json(result);
    }

    // ──────────── FK Research (foreign-key relationship mining pipeline) ────────────
    //
    // These two endpoints power the offline FK mining pipeline at /opt/fk_mining/.
    // They use the SourceIndexerService's string-literal inverted index to find
    // every C++ function that references a given SQL table / column inside a
    // quoted "..." literal. This is dramatically more accurate than grep-with-
    // ranked-snippets because the LLM sees the actual loader, its full callers,
    // and its full callees — not a guessed window of lines.

    /// <summary>
    /// GET or POST: find every indexed symbol whose body contains one or more
    /// needle strings inside C++ string literals. Lightweight — no bodies returned.
    ///
    /// GET form:  /SourceMap/FindStringReferences?needles=loot_id&needles=creature_template&requireAll=true&max=50
    /// POST form: JSON body matching FindStringReferencesRequest.
    /// </summary>
    [HttpGet]
    public IActionResult FindStringReferences(
        [FromQuery] string[] needles,
        [FromQuery] bool requireAll = false,
        [FromQuery] int max = 0)
    {
        if (needles == null || needles.Length == 0)
            return BadRequest(new { error = "At least one 'needles' query parameter is required." });

        if (_indexer.GetIndex() == null)
            return NotFound(new { error = "No index. POST /SourceMap/Reindex first." });

        var result = _indexer.FindStringReferences(needles, requireAll, max);
        return Json(result);
    }

    [HttpPost]
    public IActionResult FindStringReferencesPost([FromBody] FindStringReferencesRequest request)
    {
        if (request == null || request.Needles == null || request.Needles.Count == 0)
            return BadRequest(new { error = "Request must include non-empty 'needles' array." });

        if (_indexer.GetIndex() == null)
            return NotFound(new { error = "No index. POST /SourceMap/Reindex first." });

        var result = _indexer.FindStringReferences(request.Needles, request.RequireAll, request.MaxResults);
        return Json(result);
    }

    /// <summary>
    /// GET: find every indexed symbol whose body references the given struct
    /// member names via `foo->member` or `foo.member` access patterns.
    /// This is the FK Layer 2 lookup — used to find CONSUMERS of a struct field,
    /// not just the loader.
    ///
    /// Example: /SourceMap/FindMemberReferences?needles=loot_id&max=20
    /// </summary>
    [HttpGet]
    public IActionResult FindMemberReferences(
        [FromQuery] string[] needles,
        [FromQuery] bool requireAll = false,
        [FromQuery] int max = 0)
    {
        if (needles == null || needles.Length == 0)
            return BadRequest(new { error = "At least one 'needles' query parameter is required." });

        if (_indexer.GetIndex() == null)
            return NotFound(new { error = "No index. POST /SourceMap/Reindex first." });

        var result = _indexer.FindMemberReferences(needles, requireAll, max);
        return Json(result);
    }

    /// <summary>
    /// POST: build a complete FK research bundle for a (db, table, column).
    /// Returns prompt-ready code for the LLM auditor: the loader function(s),
    /// their callers and callees, the containing class definitions, and a
    /// cross-reference table listing which other candidate tables show up in
    /// the same code paths.
    ///
    /// Body: FkResearchBundleRequest JSON.
    /// </summary>
    [HttpPost]
    public IActionResult FkResearchBundle([FromBody] FkResearchBundleRequest request)
    {
        if (request == null)
            return BadRequest(new { error = "Request body is required." });
        if (string.IsNullOrWhiteSpace(request.Table) || string.IsNullOrWhiteSpace(request.Column))
            return BadRequest(new { error = "Both 'table' and 'column' are required." });

        if (_indexer.GetIndex() == null)
            return NotFound(new { error = "No index. POST /SourceMap/Reindex first." });

        var result = _indexer.BuildFkResearchBundle(request);
        if (result == null)
            return NotFound(new { error = "Bundle could not be built (index missing or empty)." });

        return Json(result);
    }

    /// <summary>
    /// Same as FkResearchBundle but returns the pre-formatted prompt text directly
    /// as a .txt download — convenient for ad-hoc inspection or for piping into a
    /// command-line LLM client.
    /// </summary>
    [HttpPost]
    public IActionResult FkResearchBundleText([FromBody] FkResearchBundleRequest request)
    {
        if (request == null)
            return BadRequest(new { error = "Request body is required." });
        if (string.IsNullOrWhiteSpace(request.Table) || string.IsNullOrWhiteSpace(request.Column))
            return BadRequest(new { error = "Both 'table' and 'column' are required." });
        if (_indexer.GetIndex() == null)
            return NotFound(new { error = "No index. POST /SourceMap/Reindex first." });

        var result = _indexer.BuildFkResearchBundle(request);
        if (result == null)
            return NotFound(new { error = "Bundle could not be built." });

        var safeName = $"fk_{request.Db}_{request.Table}_{request.Column}"
            .Replace("/", "_").Replace("\\", "_").Replace(" ", "_");
        return File(Encoding.UTF8.GetBytes(result.FormattedText), "text/plain", $"{safeName}_bundle.txt");
    }

    // ──────────── Reindex ────────────

    [HttpPost]
    public async Task<IActionResult> Reindex()
    {
        var sourcePath = SourceIndexWarmupService.ResolveSourcePath(_config);
        var result = await _indexer.ReindexAsync(sourcePath);
        return Json(result);
    }

    [HttpGet]
    public IActionResult ReindexProgress()
    {
        return Json(_indexer.CurrentProgress);
    }

    // ──────────── Text-endpoint helpers ────────────

    private static string NormalizeKind(string? kind) => (kind ?? "all").Trim().ToLowerInvariant() switch
    {
        "function" or "functions" or "method" or "methods" or "symbols" or "func" => "symbol",
        "types" or "class" or "classes" or "struct" => "type",
        "enums" => "enum",
        "files" => "file",
        "" => "all",
        var k => k
    };

    private static bool IsText(string? format) =>
        string.Equals(format, "text", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(format, "txt", StringComparison.OrdinalIgnoreCase);

    /// <summary>Exact key, else a unique case-insensitive path-suffix / file-name match.
    /// Sets <paramref name="ambiguous"/> to the candidates when more than one file matches.</summary>
    private string? ResolveFile(string file, out string[]? ambiguous)
    {
        ambiguous = null;
        var idx = _indexer.GetIndex();
        if (idx == null || string.IsNullOrWhiteSpace(file)) return null;
        string f = file.Trim().Replace('\\', '/');
        if (f.StartsWith("cpp/", StringComparison.OrdinalIgnoreCase)) f = f[4..];   // Circuit manifest prefix
        if (idx.Files.ContainsKey(f)) return f;

        bool bare = !Path.HasExtension(f);   // "AiBotAIMain" → both AiBotAIMain.cpp and .h (reported as ambiguous)
        var matches = idx.Files.Keys
            .Where(k => k.EndsWith("/" + f, StringComparison.OrdinalIgnoreCase)
                     || string.Equals(k, f, StringComparison.OrdinalIgnoreCase)
                     || string.Equals(idx.Files[k].FileName, f, StringComparison.OrdinalIgnoreCase)
                     || (bare && string.Equals(Path.GetFileNameWithoutExtension(idx.Files[k].FileName), f, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (matches.Length == 1) return matches[0];
        if (matches.Length > 1) ambiguous = matches;
        return null;
    }

    private static string[]? ReadSourceLines(SourceIndex idx, string rel)
    {
        var fullPath = Path.Combine(idx.SourcePath, rel);
        return System.IO.File.Exists(fullPath) ? System.IO.File.ReadAllLines(fullPath) : null;
    }

    /// <summary>Innermost indexed symbol whose body span contains <paramref name="line"/>.</summary>
    private static SymbolEntry? EnclosingSymbol(SourceIndex idx, string rel, int line)
    {
        if (!idx.FileToSymbols.TryGetValue(rel, out var ids)) return null;
        SymbolEntry? best = null;
        foreach (var id in ids)
        {
            if (!idx.Symbols.TryGetValue(id, out var s)) continue;
            if (s.DefinedInFile != rel || s.BodyLineStart == 0) continue;
            if (line < s.BodyLineStart || line > s.BodyLineEnd) continue;
            if (best == null || s.LineCount < best.LineCount) best = s;
        }
        return best;
    }

    private static void AppendNumbered(StringBuilder t, string[] lines, int start, int end, int mark = 0)
    {
        for (int i = Math.Max(1, start); i <= end && i <= lines.Length; i++)
            t.Append(i == mark ? "=>" : "  ").Append(i.ToString().PadLeft(5)).Append(": ").AppendLine(lines[i - 1]);
    }

    // ──────────── Legacy fallback helpers ────────────

    private JsonElement? LoadLegacyGraph()
    {
        lock (_legacyLock)
        {
            if (_legacyGraph == null)
            {
                var path = Path.Combine(_env.WebRootPath, "data", "function-graph.json");
                if (!System.IO.File.Exists(path)) return null;
                var json = System.IO.File.ReadAllText(path);
                _legacyGraph = JsonSerializer.Deserialize<JsonElement>(json);
            }
        }
        return (JsonElement)_legacyGraph!;
    }

    private static object BuildNeighborObj(SymbolEntry sym, string direction)
    {
        return new
        {
            id = sym.Id,
            qualified = sym.Id,
            name = sym.Name,
            className = sym.MemberOf ?? "",
            file = sym.DefinedInFile ?? "",
            line_start = sym.BodyLineStart,
            line_count = sym.LineCount,
            call_count = sym.CallsOut.Count,
            caller_count = sym.CalledBy.Count,
            max_depth = sym.Complexity,
            total_connections = sym.CallsOut.Count + sym.CalledBy.Count,
            direction
        };
    }

    private static object BuildLegacyNodeResponse(JsonElement nodes, string id, JsonElement centerNode)
    {
        var outSet = new HashSet<string>();
        var inSet = new HashSet<string>();

        if (centerNode.TryGetProperty("calls", out var calls))
            foreach (var c in calls.EnumerateArray())
            {
                var cid = c.GetString()!;
                if (cid != id) outSet.Add(cid);
            }
        if (centerNode.TryGetProperty("called_by", out var callers))
            foreach (var c in callers.EnumerateArray())
            {
                var cid = c.GetString()!;
                if (cid != id) inSet.Add(cid);
            }

        var allIds = new HashSet<string>(outSet);
        allIds.UnionWith(inSet);

        var neighbors = new List<object>();
        foreach (var nid in allIds)
        {
            if (!nodes.TryGetProperty(nid, out var neighbor)) continue;
            var dir = outSet.Contains(nid) && inSet.Contains(nid) ? "both" : outSet.Contains(nid) ? "outgoing" : "incoming";
            neighbors.Add(new
            {
                id = nid,
                qualified = neighbor.GetProperty("qualified").GetString(),
                name = neighbor.GetProperty("name").GetString(),
                className = neighbor.GetProperty("class").GetString(),
                file = neighbor.GetProperty("file").GetString(),
                line_start = neighbor.GetProperty("line_start").GetInt32(),
                line_count = neighbor.GetProperty("line_count").GetInt32(),
                call_count = neighbor.GetProperty("call_count").GetInt32(),
                caller_count = neighbor.GetProperty("caller_count").GetInt32(),
                max_depth = neighbor.GetProperty("max_depth").GetInt32(),
                total_connections = neighbor.GetProperty("total_connections").GetInt32(),
                direction = dir
            });
        }

        return new
        {
            center = new
            {
                id = centerNode.GetProperty("id").GetString(),
                qualified = centerNode.GetProperty("qualified").GetString(),
                name = centerNode.GetProperty("name").GetString(),
                className = centerNode.GetProperty("class").GetString(),
                file = centerNode.GetProperty("file").GetString(),
                line_start = centerNode.GetProperty("line_start").GetInt32(),
                line_end = centerNode.GetProperty("line_end").GetInt32(),
                line_count = centerNode.GetProperty("line_count").GetInt32(),
                signature = centerNode.GetProperty("signature").GetString(),
                call_count = centerNode.GetProperty("call_count").GetInt32(),
                caller_count = centerNode.GetProperty("caller_count").GetInt32(),
                max_depth = centerNode.GetProperty("max_depth").GetInt32(),
                total_connections = centerNode.GetProperty("total_connections").GetInt32(),
            },
            neighbors,
            source = "legacy"
        };
    }

    private static object BuildLegacyCallTree(JsonElement nodes, string nodeId, HashSet<string> ancestors, HashSet<string> expanded, int depth)
    {
        if (ancestors.Contains(nodeId))
            return new { name = nodeId, cycle = true, calls = Array.Empty<object>() };
        if (depth > 80)
            return new { name = nodeId, truncated = true, calls = Array.Empty<object>() };
        if (!nodes.TryGetProperty(nodeId, out var node))
            return new { name = nodeId, external = true, calls = Array.Empty<object>() };

        var qualified = node.GetProperty("qualified").GetString();
        var file = node.GetProperty("file").GetString();
        var line = node.GetProperty("line_start").GetInt32();

        if (expanded.Contains(nodeId))
            return new { name = qualified, file, line, @ref = true, calls = Array.Empty<object>() };

        expanded.Add(nodeId);
        var newAncestors = new HashSet<string>(ancestors) { nodeId };
        var children = new List<object>();

        if (node.TryGetProperty("calls", out var calls))
            foreach (var call in calls.EnumerateArray())
            {
                var callId = call.GetString()!;
                if (callId != nodeId)
                    children.Add(BuildLegacyCallTree(nodes, callId, newAncestors, expanded, depth + 1));
            }

        return new { name = qualified, file, line, calls = children };
    }
}

// ──────────── Research Bundle DTOs ────────────

public class ResearchBundleRequest
{
    public List<string>? Files { get; set; }
    public List<string>? Functions { get; set; }
    public List<string>? Types { get; set; }
    public List<string>? Enums { get; set; }
}

public class ResearchBundleResult
{
    public List<BundleFile> Files { get; set; } = new();
    public List<BundleFunction> Functions { get; set; } = new();
    public List<TypeEntry> Types { get; set; } = new();
    public List<EnumEntry> Enums { get; set; } = new();
}

public class BundleFile
{
    public string Path { get; set; } = "";
    public string Content { get; set; } = "";
    public int LineCount { get; set; }
}

public class BundleFunction
{
    public string Id { get; set; } = "";
    public string Signature { get; set; } = "";
    public string? File { get; set; }
    public int LineStart { get; set; }
    public int LineEnd { get; set; }
    public string Body { get; set; } = "";
    public string? Note { get; set; }
}