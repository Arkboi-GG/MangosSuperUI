using System.Text;
using System.Text.RegularExpressions;
using Markdig;

namespace MangosSuperUI.Services;

/// <summary>
/// Reads the generated documentation corpus (SourceMapper's StageE output) off disk and
/// serves it to the wiki. The corpus is a folder-mirrored tree of one Markdown file per
/// C++ unit under <c>Wiki:Root</c> (default <c>/home/wowvmangos/docs_full</c>).
///
/// This is NOT the CADM reader ported over. The CADM DocStore was multi-project,
/// controller-grouped, and joined graph.json against a Windows source tree to build a
/// file-coverage rail. The SuperUI corpus is a single folder-mirrored set of unit docs,
/// and the thing that makes it a *wiki* is cross-linking: every <c>Unit/Member</c> token
/// in a MAP table (and every backticked identifier in prose) becomes a live link to that
/// unit's page. That auto-linking, a per-page table of contents, and a browsable nav tree
/// are what this store produces.
///
/// Read-only. The corpus is regenerated out-of-band by SourceMapper on the box; the store
/// caches the label index and rebuilds it when the corpus changes on disk.
/// </summary>
public sealed class WikiDocStore
{
    private static readonly StringComparer OIC = StringComparer.OrdinalIgnoreCase;

    private readonly string _root;
    private readonly MarkdownPipeline _pipeline;

    // label index cache (stem -> relPath-without-extension), rebuilt when the corpus changes
    private readonly object _lock = new();
    private Dictionary<string, string>? _labels;
    private string _labelsSig = "";

    public WikiDocStore(IConfiguration config)
    {
        _root = (config["Wiki:Root"]?.Trim()).NullIfEmpty()
                ?? "/home/wowvmangos/docs_full";
        _pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()   // pipe tables (the machine-true MAP), autolinks, etc.
            .Build();
    }

    public bool RootExists => _root.Length > 0 && Directory.Exists(_root);
    public string Root => _root;

    // ----------------------------------------------------------------- tree

    /// <summary>Folder-mirrored nav tree: directories and pages, sorted dirs-then-pages.</summary>
    public WikiTree Tree()
    {
        var root = new WikiNode { Name = RootLabel(), Path = "", Type = "dir" };
        if (!RootExists) return new WikiTree(RootLabel(), root.Children, 0);

        int pages = 0;
        var index = new Dictionary<string, WikiNode>(OIC) { [""] = root };

        foreach (var rel in EnumerateDocs())
        {
            pages++;
            var parts = rel.Split('/');
            var dir = EnsureDir(index, parts[..^1]);
            var stem = Path.GetFileNameWithoutExtension(parts[^1]);
            dir.Children.Add(new WikiNode
            {
                Name = stem,
                Label = stem,
                Path = rel[..^3],           // strip ".md" -> page path
                Type = "page"
            });
        }

        Sort(root);
        Compress(root);   // collapse single-child folder chains (game > AI -> "game/AI")
        return new WikiTree(RootLabel(), root.Children, pages);
    }

    // ----------------------------------------------------------------- page

    /// <summary>Renders one page: title, provenance, linked HTML, ToC, breadcrumbs.</summary>
    public WikiPage? Page(string path)
    {
        var file = ResolveFile(path);
        if (file is null) return null;

        var relNoExt = Path.GetRelativePath(_root, file).Replace('\\', '/');
        if (relNoExt.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) relNoExt = relNoExt[..^3];
        var stem = Path.GetFileName(relNoExt);

        var raw = File.ReadAllText(file);
        var (title, provenance, body) = Preprocess(raw, stem);

        var html = Markdown.ToHtml(body, _pipeline);
        var toc = new List<WikiTocItem>();
        html = InjectHeadingIds(html, toc);
        html = InjectMemberIds(html);
        html = Linkify(html);

        return new WikiPage(
            Path: relNoExt,
            Label: stem,
            Title: title.NullIfEmpty() ?? stem,
            Html: html,
            Toc: toc,
            Breadcrumbs: Breadcrumbs(relNoExt),
            Provenance: provenance,
            Infobox: ExtractInfobox(body, stem),
            Modified: File.GetLastWriteTimeUtc(file));
    }

    // ---------------------------------------------------------------- infobox

    // A Wikipedia-style summary card, built from the machine-true MAP section that every
    // doc carries: what the doc actually documents (its Source file/pair), what kind of
    // unit it is, and the shape of it (member/action count, tables touched, cross-unit
    // boundary counts). Pure projection of the MAP — no graph.json, no pipeline change.
    private static WikiInfobox? ExtractInfobox(string body, string stem)
    {
        var idx = body.IndexOf("## Map", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        var map = body[idx..];
        var lines = map.Split('\n');

        // *Source:* a.cpp, b.h
        var source = new List<string>();
        var sm = Regex.Match(map, @"\*Source:\*\s*(.+)", RegexOptions.IgnoreCase);
        if (sm.Success)
            source = sm.Groups[1].Value.Replace("`", "").Replace("  ", " ")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

        // find the member/action table: header row, then '|---' separator, then data rows
        int hdr = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            var t = lines[i].TrimStart();
            if (t.StartsWith("|") && !t.Contains("---") &&
                (t.Contains("Member") || t.Contains("Action")))
            { hdr = i; break; }
        }

        var facts = new List<WikiFact>();
        string kind = "Unit";
        string firstColName = "Members";
        int memberCount = 0;

        if (hdr >= 0)
        {
            var cols = Cells(lines[hdr]);
            int ixOf(string name) => cols.FindIndex(c => c.Contains(name, StringComparison.OrdinalIgnoreCase));
            int iTables = ixOf("Tables"), iOut = ixOf("Calls out"), iIn = ixOf("Called by"), iVerb = ixOf("Verb");
            firstColName = cols.Count > 0 && cols[0].Contains("Action", StringComparison.OrdinalIgnoreCase) ? "Actions" : "Members";

            var tables = new SortedSet<string>(OIC);
            var callsOut = new HashSet<string>(OIC);
            var calledBy = new HashSet<string>(OIC);

            for (int i = hdr + 2; i < lines.Length; i++)   // +1 header, +1 separator
            {
                var t = lines[i].TrimStart();
                if (!t.StartsWith("|")) break;             // table ended
                var cells = Cells(lines[i]);
                if (cells.Count == 0 || cells.All(c => c.Length == 0)) continue;
                memberCount++;
                CollectTokens(cells, iTables, tables, unitOnly: false);
                CollectUnits(cells, iOut, callsOut);
                CollectUnits(cells, iIn, calledBy);
            }

            kind = KindLabel(source, iVerb >= 0, iOut >= 0);

            facts.Add(new WikiFact(firstColName, memberCount.ToString()));
            if (tables.Count > 0) facts.Add(new WikiFact("Tables", string.Join(", ", tables)));
            if (iOut >= 0 && callsOut.Count > 0) facts.Add(new WikiFact("Calls out", Plural(callsOut.Count, "unit")));
            if (iIn >= 0 && calledBy.Count > 0) facts.Add(new WikiFact("Called by", Plural(calledBy.Count, "unit")));
        }

        var head = new List<WikiFact>();
        if (source.Count > 0) head.Add(new WikiFact("Source", string.Join(", ", source)));
        head.Add(new WikiFact("Kind", kind));
        head.AddRange(facts);

        return new WikiInfobox(stem, head);
    }

    private static List<string> Cells(string row)
    {
        var t = row.Trim();
        if (t.StartsWith("|")) t = t[1..];
        if (t.EndsWith("|")) t = t[..^1];
        return t.Split('|').Select(c => c.Trim()).ToList();
    }

    private static void CollectTokens(List<string> cells, int col, SortedSet<string> into, bool unitOnly)
    {
        if (col < 0 || col >= cells.Count) return;
        var v = cells[col];
        if (v.Length == 0 || v == "—") return;
        foreach (var raw in v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var tok = raw.Trim();
            // strip trailing "(via)"/"(dynamic)" annotations the C# MAP adds
            var paren = tok.IndexOf(" (");
            if (paren > 0) tok = tok[..paren];
            if (tok.Length > 0 && tok != "—") into.Add(tok);
        }
    }

    private static void CollectUnits(List<string> cells, int col, HashSet<string> into)
    {
        if (col < 0 || col >= cells.Count) return;
        var v = cells[col];
        if (v.Length == 0 || v == "—") return;
        foreach (var raw in v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var tok = raw.Trim();
            var slash = tok.IndexOf('/');           // "Unit.Partial/Member" -> unit label
            if (slash > 0) tok = tok[..slash];
            if (tok.Length > 0 && tok != "—") into.Add(tok);
        }
    }

    private static string KindLabel(List<string> source, bool hasVerb, bool hasBoundary)
    {
        bool cpp = source.Any(f => f.EndsWith(".cpp", StringComparison.OrdinalIgnoreCase));
        bool h = source.Any(f => f.EndsWith(".h", StringComparison.OrdinalIgnoreCase));
        bool cs = source.Any(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase));
        bool js = source.Any(f => f.EndsWith(".js", StringComparison.OrdinalIgnoreCase));

        if (hasVerb) return "Controller";
        if (js) return "Script";
        if (cs) return "C# class";
        if (cpp && h) return "C++ translation unit";
        if (cpp) return "C++ source";
        if (h) return "C++ header";
        if (hasBoundary) return "C++ unit";
        return "Unit";
    }

    private static string Plural(int n, string noun) => n + " " + noun + (n == 1 ? "" : "s");


    // ---------------------------------------------------------------- stats

    public WikiStats Stats()
    {
        if (!RootExists) return new WikiStats(RootLabel(), 0, 0, null, false);
        int pages = 0;
        var folders = new HashSet<string>(OIC);
        DateTime? last = null;
        foreach (var rel in EnumerateDocs())
        {
            pages++;
            var slash = rel.LastIndexOf('/');
            if (slash > 0) folders.Add(rel[..slash]);
            var m = File.GetLastWriteTimeUtc(Path.Combine(_root, rel.Replace('/', Path.DirectorySeparatorChar)));
            if (last is null || m > last) last = m;
        }
        return new WikiStats(RootLabel(), pages, folders.Count, last, true);
    }

    // ------------------------------------------------------------- rendering

    // Strip our own "# <stem>" title line and the StageE provenance comment; drop the
    // model's duplicate title heading if it merely repeats ours (the known cosmetic
    // artifact). Everything else — the narrative, the "---" rule, the machine-true MAP
    // table — is kept and rendered.
    private static (string title, string? provenance, string body) Preprocess(string md, string stem)
    {
        string? provenance =
            md.Contains("model call failed", StringComparison.OrdinalIgnoreCase) ? "failed" :
            md.Contains("model-written from source", StringComparison.OrdinalIgnoreCase) ? "model" : null;

        string? title = null;
        var kept = new List<string>();
        bool titleTaken = false;

        using var sr = new StringReader(md);
        for (string? line; (line = sr.ReadLine()) is not null;)
        {
            var t = line.TrimStart();

            // drop HTML comments (provenance / machine-true markers) — invisible anyway,
            // but stripping keeps the rendered body clean.
            if (t.StartsWith("<!--", StringComparison.Ordinal) && t.Contains("-->")) continue;

            if (!titleTaken && t.StartsWith("# ", StringComparison.Ordinal))
            {
                title = t[2..].Trim();
                titleTaken = true;
                continue;   // remove our title line from the body
            }
            kept.Add(line);
        }

        // De-dupe the model's echoed title heading (only when it equals ours).
        string norm(string s) => s.Replace("`", "").Trim();
        var wanted = norm(title ?? stem);
        for (int i = 0; i < kept.Count; i++)
        {
            var t = kept[i].TrimStart();
            if (t.Length == 0) continue;
            var hm = Regex.Match(t, @"^#{1,6}\s+(.*)$");
            if (hm.Success && OIC.Equals(norm(hm.Groups[1].Value), wanted))
                kept.RemoveAt(i);
            break;   // only inspect the first non-empty body line
        }

        return (title ?? stem, provenance, string.Join('\n', kept));
    }

    // Give every h2..h4 a stable id via WikiSlug and collect the ToC. h1 (the model's own
    // heading, if any survived) is left out of the ToC.
    private static string InjectHeadingIds(string html, List<WikiTocItem> toc)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return Regex.Replace(html, @"<h([2-4])>(.*?)</h\1>", m =>
        {
            int level = m.Groups[1].Value[0] - '0';
            string inner = m.Groups[2].Value;
            string text = StripTags(inner);
            string slug = WikiSlug.Heading(text);
            if (slug.Length == 0) return m.Value;
            // ensure uniqueness within the page
            string id = slug; int n = 2;
            while (!seen.Add(id)) id = slug + "-" + n++;
            toc.Add(new WikiTocItem(level, text, id));
            return $"<h{level} id=\"{id}\">{inner}</h{level}>";
        }, RegexOptions.Singleline);
    }

    // Anchor the leading-bold member entries (Member Reference + Member-by-Member) so
    // "…#member-loadaura" deep links land. First occurrence of each member wins.
    private static string InjectMemberIds(string html)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return Regex.Replace(html,
            @"(<(?:p|li)>\s*)<strong>(<code>)?([A-Za-z_][\w#]*)(</code>)?</strong>",
            m =>
            {
                string name = m.Groups[3].Value;
                string id = WikiSlug.Member(name);
                if (!seen.Add(id)) return m.Value;   // don't duplicate ids
                string open = m.Groups[1].Value;
                string codeO = m.Groups[2].Value;
                string codeC = m.Groups[4].Value;
                // put the id on the <strong> so the anchor sits exactly on the member name
                return $"{open}<strong id=\"{id}\">{codeO}{name}{codeC}</strong>";
            });
    }

    // The wiki spine: turn cross-references into links.
    //   (a) MAP table cells: comma-separated "Unit/Member" tokens.
    //   (b) inline <code>Unit</code> / <code>Unit/Member</code> in the narrative.
    // Only labels that exist as pages become links; everything else is left verbatim.
    private string Linkify(string html)
    {
        var labels = Labels();

        // (a) table cells
        html = Regex.Replace(html, @"<td>(.*?)</td>", m =>
        {
            string cell = m.Groups[1].Value;
            // token: Label(with optional dotted partials) '/' Member(with optional #N)
            cell = Regex.Replace(cell, @"([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z0-9_]+)*)/([A-Za-z0-9_]+(?:#\d+)?)", tm =>
            {
                string label = tm.Groups[1].Value, member = tm.Groups[2].Value;
                return labels.TryGetValue(label, out var rel)
                    ? Xref(rel, WikiSlug.Member(member), tm.Value)
                    : tm.Value;
            });
            return $"<td>{cell}</td>";
        }, RegexOptions.Singleline);

        // (b) inline code identifiers
        html = Regex.Replace(html, @"<code>([^<]+)</code>", m =>
        {
            string codeHtml = m.Groups[1].Value;                 // may contain &lt;…&gt;
            string content = System.Net.WebUtility.HtmlDecode(codeHtml);
            string bare = Regex.Replace(content, @"<.*$", "").Trim();   // drop template args
            string? anchor = null; string? rel = null;

            int slash = bare.IndexOf('/');
            if (slash > 0)
            {
                string label = bare[..slash], member = bare[(slash + 1)..];
                if (labels.TryGetValue(label, out rel)) anchor = WikiSlug.Member(member);
            }
            else if (labels.TryGetValue(bare, out rel)) { /* whole-unit link */ }

            return rel is null ? m.Value : Xref(rel, anchor, $"<code>{codeHtml}</code>");
        });

        return html;
    }

    private static string Xref(string relPath, string? anchor, string inner)
    {
        string href = "/Wiki?path=" + Uri.EscapeDataString(relPath);
        if (anchor is not null) href += "#" + anchor;
        string a = $" data-wiki-path=\"{System.Net.WebUtility.HtmlEncode(relPath)}\"";
        if (anchor is not null) a += $" data-wiki-anchor=\"{anchor}\"";
        return $"<a class=\"wiki-xref\" href=\"{href}\"{a}>{inner}</a>";
    }

    // ------------------------------------------------------------- corpus io

    private IEnumerable<string> EnumerateDocs()
    {
        // relative, '/'-separated, "*.md" only, hidden dirs skipped
        foreach (var f in Directory.EnumerateFiles(_root, "*.md", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(_root, f).Replace('\\', '/');
            if (rel.Split('/').Any(seg => seg.StartsWith('.'))) continue;
            yield return rel;
        }
    }

    private string? ResolveFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !RootExists) return null;
        var rel = path.Replace('\\', '/').Trim().TrimStart('/');
        if (rel.Length == 0) return null;
        if (rel.Split('/').Any(seg => seg is "" or "." or "..")) return null;   // no traversal
        if (!rel.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) rel += ".md";

        var full = Path.GetFullPath(Path.Combine(_root, rel.Replace('/', Path.DirectorySeparatorChar)));
        var rootFull = Path.GetFullPath(_root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootFull, StringComparison.Ordinal)) return null;   // escaped root
        return File.Exists(full) ? full : null;
    }

    // stem -> relPath-without-extension, cached until the corpus changes on disk
    private Dictionary<string, string> Labels()
    {
        var sig = CorpusSignature();
        lock (_lock)
        {
            if (_labels is not null && _labelsSig == sig) return _labels;
            var map = new Dictionary<string, string>(StringComparer.Ordinal);   // C++ is case-sensitive
            foreach (var rel in EnumerateDocs())
            {
                var stem = Path.GetFileNameWithoutExtension(rel);
                map.TryAdd(stem, rel[..^3]);   // first-wins on any stem collision
            }
            _labels = map;
            _labelsSig = sig;
            return map;
        }
    }

    private string CorpusSignature()
    {
        if (!RootExists) return "none";
        long count = 0, ticks = 0;
        foreach (var f in Directory.EnumerateFiles(_root, "*.md", SearchOption.AllDirectories))
        {
            count++;
            var t = File.GetLastWriteTimeUtc(f).Ticks;
            if (t > ticks) ticks = t;
        }
        return count + ":" + ticks;
    }

    // --------------------------------------------------------------- helpers

    private string RootLabel()
    {
        var name = Path.GetFileName(_root.TrimEnd('\\', '/'));
        return name.NullIfEmpty() ?? "Wiki";
    }

    private static WikiNode EnsureDir(Dictionary<string, WikiNode> index, string[] parts)
    {
        var path = ""; var cur = index[""];
        foreach (var p in parts)
        {
            path = path.Length == 0 ? p : path + "/" + p;
            if (!index.TryGetValue(path, out var n))
            {
                index[path] = n = new WikiNode { Name = p, Path = path, Type = "dir" };
                cur.Children.Add(n);
            }
            cur = n;
        }
        return cur;
    }

    private static void Sort(WikiNode node)
    {
        node.Children = node.Children
            .OrderBy(c => c.Type == "dir" ? 0 : 1)
            .ThenBy(c => c.Name, OIC)
            .ToList();
        foreach (var c in node.Children) Sort(c);
    }

    private static void Compress(WikiNode node)
    {
        foreach (var c in node.Children)
        {
            if (c.Type != "dir") continue;
            while (c.Children.Count == 1 && c.Children[0].Type == "dir")
            {
                var only = c.Children[0];
                c.Name = c.Name + "/" + only.Name;
                c.Path = only.Path;
                c.Children = only.Children;
            }
            Compress(c);
        }
    }

    private static List<WikiCrumb> Breadcrumbs(string relNoExt)
    {
        var crumbs = new List<WikiCrumb>();
        var parts = relNoExt.Split('/');
        // folder crumbs (no link target of their own in W1 — they filter the tree)
        for (int i = 0; i < parts.Length - 1; i++)
            crumbs.Add(new WikiCrumb(parts[i], null));
        // the page itself
        crumbs.Add(new WikiCrumb(parts[^1], relNoExt));
        return crumbs;
    }

    private static string StripTags(string html) =>
        System.Net.WebUtility.HtmlDecode(Regex.Replace(html, "<.*?>", "")).Trim();
}

internal static class StrExt
{
    public static string? NullIfEmpty(this string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}

// ---------------------------------------------------------------------- DTOs

public sealed class WikiNode
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";     // page path (no ".md") or folder path
    public string Type { get; set; } = "";     // dir | page
    public string? Label { get; set; }         // unit label for pages
    public List<WikiNode> Children { get; set; } = new();
}

public sealed record WikiTree(string Root, List<WikiNode> Children, int PageCount);
public sealed record WikiTocItem(int Level, string Text, string Anchor);
public sealed record WikiCrumb(string Name, string? Path);
public sealed record WikiFact(string Label, string Value);
public sealed record WikiInfobox(string Title, List<WikiFact> Facts);
public sealed record WikiPage(
    string Path, string Label, string Title, string Html,
    List<WikiTocItem> Toc, List<WikiCrumb> Breadcrumbs, string? Provenance,
    WikiInfobox? Infobox, DateTime Modified);
public sealed record WikiStats(string Root, int PageCount, int FolderCount, DateTime? LastUpdated, bool Ready);