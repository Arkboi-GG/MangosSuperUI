using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MySqlConnector;

namespace MangosSuperUI.Services;

/// <summary>
/// The W2 index writer — inside SuperUI, not SourceMapper. The corpus (.md docs under
/// <c>Wiki:Root</c>) is the distributable artifact; SourceMapper is the private pipeline
/// that generates it. So anyone who has the app and the docs gets search: the indexer
/// reads the corpus off disk and fills the <c>docs_*</c> tables in the admin DB, with
/// no external tool in the loop.
///
/// Everything the index needs is already in each doc:
///   • narrative sections, member entries, MAP rows → the three chunk kinds (D20);
///   • the machine-true <c>*Source:*</c> line → <c>primary_file</c> + <c>docs_page_file</c>
///     roles (D26/D29), with 'shared' computed per folder (a non-primary file appearing
///     under ≥2 partials of the same class);
///   • the MAP table's shape → <c>is_trivial</c> (1 member, no out-edges, no tables);
///   • stem structure per folder → <c>class_group</c> and overview detection (a doc named
///     exactly like a ≥2-partial class prefix beside it);
///   • path → kind (<c>_topics/</c> = topic). Topic docs may carry an
///     <c>&lt;!-- aliases: a, b, c --&gt;</c> comment; its entries land in <c>docs_alias</c>
///     (that comment is the W3 emission contract).
///
/// Behavior: <see cref="KickIfStale"/> is cheap and self-throttled — callers (the search
/// path) fire it blindly; a background task runs only when the corpus signature changed,
/// and re-processes only pages whose SHA-256 changed (idempotent reindex, W5). Status is
/// observable for the UI ("building… 214/1148"). <see cref="ForceReindex"/> rebuilds
/// everything (use after an indexer logic change). Anchors are computed by WikiSlug
/// ONLY — the indexer delegates, never reimplements (G4).
/// </summary>
public sealed class WikiIndexer
{
    private static readonly StringComparer OIC = StringComparer.OrdinalIgnoreCase;

    // Bump when chunking/shadow/alias logic changes: doc hashes won't have moved, but
    // the stored chunks are stale — a version mismatch forces a full rebuild on the
    // next kick, so upgrades retro-chunk themselves without a manual reindex.
    private const int IndexerVersion = 2;

    private readonly string _root;
    private readonly string? _conn;

    private readonly object _lock = new();
    private Task? _run;
    private DateTime _lastKickUtc = DateTime.MinValue;

    private volatile IndexerStatus _status = new(false, 0, 0, null, null);
    public IndexerStatus Status => _status;

    public WikiIndexer(IConfiguration config)
    {
        _root = (config["Wiki:Root"]?.Trim()).NullIfEmpty() ?? "/home/wowvmangos/docs_full";
        _conn = (config["Wiki:IndexConnection"]?.Trim()).NullIfEmpty()
                ?? (config.GetConnectionString("admin")?.Trim()).NullIfEmpty();
    }

    /// <summary>Fire-and-forget freshness check; throttled to one kick per 30s. The run
    /// itself exits immediately when the corpus signature matches the stored one.</summary>
    public void KickIfStale()
    {
        if (_conn is null || !Directory.Exists(_root)) return;
        lock (_lock)
        {
            if (_run is { IsCompleted: false }) return;
            if ((DateTime.UtcNow - _lastKickUtc).TotalSeconds < 30) return;
            _lastKickUtc = DateTime.UtcNow;
            _run = Task.Run(() => RunAsync(force: false));
        }
    }

    /// <summary>Full rebuild regardless of signature/hashes. Returns false if a run is
    /// already in flight.</summary>
    public bool ForceReindex()
    {
        if (_conn is null || !Directory.Exists(_root)) return false;
        lock (_lock)
        {
            if (_run is { IsCompleted: false }) return false;
            _lastKickUtc = DateTime.UtcNow;
            _run = Task.Run(() => RunAsync(force: true));
            return true;
        }
    }

    // ------------------------------------------------------------------ run

    private async Task RunAsync(bool force)
    {
        try
        {
            await using var db = new MySqlConnection(_conn);
            await db.OpenAsync();
            await WikiDocsSchema.EnsureAsync(db, CancellationToken.None);

            var sig = CorpusSignature();
            var storedSig = await GetMetaAsync(db, "corpus_signature");
            var storedVer = await GetMetaAsync(db, "indexer_version");
            var pageCount = Convert.ToInt64(await ScalarAsync(db, "SELECT COUNT(*) FROM docs_page"));
            if (!force && storedSig == sig && pageCount > 0 && storedVer == IndexerVersion.ToString())
            {
                _status = _status with { Building = false };
                return;   // fresh — nothing to do
            }
            // a version bump means the chunking logic changed: hashes would match but the
            // stored chunks are stale — rebuild everything without anyone asking
            var rebuildAll = force || storedVer != IndexerVersion.ToString();

            // -- phase 1: parse the whole corpus (folder context is needed for class
            //    groups, overview detection, and shared-file roles)
            var docs = new List<ParsedDoc>();
            foreach (var rel in EnumerateDocs())
            {
                var full = Path.Combine(_root, rel.Replace('/', Path.DirectorySeparatorChar));
                string raw;
                try { raw = await File.ReadAllTextAsync(full); }
                catch { continue; }   // vanished mid-run — the next signature pass catches it
                docs.Add(ParseDoc(rel, raw, File.GetLastWriteTimeUtc(full)));
            }
            ResolveFolderContext(docs);

            _status = new IndexerStatus(true, 0, docs.Count, _status.LastCompletedUtc, null);

            // -- phase 2: write (per-page transaction; hash-skip unless forced)
            var existing = new Dictionary<string, string>(StringComparer.Ordinal);
            await using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = "SELECT path, content_hash FROM docs_page";
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync()) existing[r.GetString(0)] = r.GetString(1);
            }

            int done = 0;
            foreach (var d in docs)
            {
                if (!rebuildAll && existing.TryGetValue(d.PagePath, out var h) && h == d.Hash)
                {
                    _status = _status with { Done = ++done };
                    continue;
                }
                await WritePageAsync(db, d);
                _status = _status with { Done = ++done };
            }

            // -- phase 3: remove pages whose .md left the disk (FKs cascade the children)
            var onDisk = new HashSet<string>(docs.Select(d => d.PagePath), StringComparer.Ordinal);
            var stale = new List<uint>();
            await using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = "SELECT id, path FROM docs_page";
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                    if (!onDisk.Contains(r.GetString(1))) stale.Add(r.GetUInt32(0));
            }
            foreach (var batch in stale.Chunk(100))
            {
                await using var cmd = db.CreateCommand();
                cmd.CommandText = "DELETE FROM docs_page WHERE id IN (" + string.Join(',', batch) + ")";
                await cmd.ExecuteNonQueryAsync();
            }

            await SetMetaAsync(db, "corpus_signature", sig);
            await SetMetaAsync(db, "indexer_version", IndexerVersion.ToString());
            _status = new IndexerStatus(false, done, docs.Count, DateTime.UtcNow, null);
        }
        catch (Exception ex)
        {
            _status = new IndexerStatus(false, _status.Done, _status.Total, _status.LastCompletedUtc, ex.Message);
        }
    }

    // ---------------------------------------------------------- doc parsing

    private sealed record ParsedDoc(
        string Rel, string PagePath, string Stem, string Folder, string Hash, DateTime Modified)
    {
        public string Title = "";
        public string? Provenance;
        public string Kind = "unit";
        public List<string> Files = new();
        public string? Primary;
        public string? ClassGroup;
        public bool IsTrivial;
        public List<string> Aliases = new();
        public List<Chunk> Chunks = new();
        public Dictionary<string, string> FileRoles = new(OIC);   // file -> primary|paired|shared
    }

    private sealed record Chunk(string Kind, string? Heading, string? Member, string? Anchor, string Body);

    private ParsedDoc ParseDoc(string rel, string raw, DateTime modified)
    {
        var stem = Path.GetFileNameWithoutExtension(rel);
        var slash = rel.LastIndexOf('/');
        var d = new ParsedDoc(
            Rel: rel,
            PagePath: rel[..^3],
            Stem: stem,
            Folder: slash < 0 ? "" : rel[..slash],
            Hash: Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant(),
            Modified: modified);

        d.Provenance =
            raw.Contains("model call failed", StringComparison.OrdinalIgnoreCase) ? "failed" :
            raw.Contains("model-written from source", StringComparison.OrdinalIgnoreCase) ? "model" : null;
        d.Kind = rel.StartsWith("_topics/", StringComparison.OrdinalIgnoreCase) ? "topic" : "unit";

        // W3 emission contract: topic docs may carry "<!-- aliases: a, b, c -->"
        var am = Regex.Match(raw, @"<!--\s*aliases:\s*(.+?)\s*-->", RegexOptions.IgnoreCase);
        if (am.Success)
            d.Aliases.AddRange(am.Groups[1].Value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(NormAlias));

        // *Source:* files (same machine-true line the reader's tree uses — D29)
        var mapIdx = raw.IndexOf("## Map", StringComparison.OrdinalIgnoreCase);
        var mapText = mapIdx >= 0 ? raw[mapIdx..] : "";
        var sm = Regex.Match(mapText.Length > 0 ? mapText : raw, @"\*Source:\*\s*(.+)", RegexOptions.IgnoreCase);
        if (sm.Success)
            d.Files = sm.Groups[1].Value.Replace("`", "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(f => f.Length > 0).Distinct(OIC).ToList();
        d.Primary =
            d.Files.FirstOrDefault(f => f.EndsWith(".cpp", StringComparison.OrdinalIgnoreCase))
            ?? d.Files.FirstOrDefault(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            ?? d.Files.FirstOrDefault();

        BuildChunks(d, raw, mapText);
        return d;
    }

    // Sections by h2; member-ish sections yield 'member' chunks, the Map yields
    // 'maprow' chunks (+ is_trivial), everything else yields 'narrative' chunks.
    private void BuildChunks(ParsedDoc d, string raw, string mapText)
    {
        var lines = raw.Replace("\r\n", "\n").Split('\n');
        string? title = null;
        var memberSectionRx = new Regex(@"^(member[\s-]*by[\s-]*member|member reference|path reference)", RegexOptions.IgnoreCase);

        string? curHeading = null;      // current h2 text (null = preamble)
        bool inMemberSection = false, inMap = false;
        var narrative = new StringBuilder();
        var entryName = ""; var entry = new StringBuilder();

        void flushNarrative()
        {
            var text = narrative.ToString().Trim();
            narrative.Clear();
            if (text.Length > 0)
                d.Chunks.Add(new Chunk("narrative", curHeading, null, null, text));
        }
        void flushEntry()
        {
            var text = entry.ToString().Trim();
            entry.Clear();
            if (entryName.Length > 0 && text.Length > 0)
                d.Chunks.Add(new Chunk("member", curHeading, entryName, WikiSlug.Member(entryName), text));
            entryName = "";
        }

        foreach (var line in lines)
        {
            var t = line.TrimStart();

            if (t.StartsWith("<!--", StringComparison.Ordinal) && t.Contains("-->")) continue;
            if (title is null && t.StartsWith("# ", StringComparison.Ordinal)) { title = t[2..].Trim(); continue; }

            var h2 = Regex.Match(t, @"^##\s+(.*)$");
            if (h2.Success && !t.StartsWith("###", StringComparison.Ordinal))
            {
                flushEntry(); flushNarrative();
                curHeading = h2.Groups[1].Value.Trim();
                inMap = curHeading.StartsWith("Map", StringComparison.OrdinalIgnoreCase);
                inMemberSection = memberSectionRx.IsMatch(curHeading);
                continue;
            }

            if (inMap) continue;   // the Map is chunked from mapText below, row by row

            if (inMemberSection)
            {
                var em = Regex.Match(t, @"^\*\*`?([A-Za-z_][A-Za-z0-9_#]*)`?");
                if (em.Success) { flushEntry(); entryName = em.Groups[1].Value; }
                if (t.StartsWith("###", StringComparison.Ordinal) || t == "---") { flushEntry(); continue; }
                if (entryName.Length > 0) entry.AppendLine(line);
                continue;
            }

            narrative.AppendLine(line);
        }
        flushEntry(); flushNarrative();

        d.Title = (title?.Trim()).NullIfEmpty() ?? d.Stem;

        // -- MAP rows -> maprow chunks + trivial signals
        int members = 0; bool anyOut = false, anyTables = false;
        List<string>? header = null;
        foreach (var line in mapText.Split('\n'))
        {
            var t = line.TrimStart();
            if (!t.StartsWith("|")) { if (header is not null && t.Length > 0) break; continue; }
            if (t.Contains("---")) continue;
            var cells = Cells(t);
            if (header is null)
            {
                if (cells.Any(c => c.Contains("Member", StringComparison.OrdinalIgnoreCase) ||
                                   c.Contains("Action", StringComparison.OrdinalIgnoreCase)))
                    header = cells;
                continue;
            }
            if (cells.Count == 0 || cells.All(c => c.Length == 0)) continue;

            members++;
            var name = cells[0].Replace("`", "").Trim();
            var parts = new List<string>();
            for (int i = 0; i < cells.Count && i < header.Count; i++)
                if (cells[i].Length > 0 && cells[i] != "\u2014")
                {
                    // G3 at the index layer: hub members (getConfig, GetLevel) carry
                    // caller lists spanning half the engine — untruncated, their shadow
                    // splits contain nearly every identifier word in the codebase and
                    // the chunk matches every query incidentally. Cap each cell's list.
                    parts.Add(header[i] + ": " + TrimList(cells[i]));
                    if (header[i].Contains("Calls out", StringComparison.OrdinalIgnoreCase)) anyOut = true;
                    if (header[i].Contains("Tables", StringComparison.OrdinalIgnoreCase)) anyTables = true;
                }
            if (name.Length > 0)
                d.Chunks.Add(new Chunk("maprow", "Map", name, WikiSlug.Member(name), string.Join("; ", parts)));
        }
        d.IsTrivial = members == 1 && !anyOut && !anyTables;

        // -- auto aliases: the label itself + its human split ("Spell.Effects" -> "spell effects")
        d.Aliases.Add(NormAlias(d.Stem));
        var split = SplitWords(d.Stem);
        if (split.Count >= 2) d.Aliases.Add(NormAlias(string.Join(' ', split)));
    }

    // Per-folder context: class groups (≥2 dotted stems sharing a prefix), overview docs
    // (a doc named exactly like a class prefix beside its partials), and file roles —
    // a non-primary file under ≥2 partials of the same class is 'shared' (G10).
    private static void ResolveFolderContext(List<ParsedDoc> docs)
    {
        foreach (var folder in docs.GroupBy(d => d.Folder, OIC))
        {
            var byPrefix = folder.Where(d => d.Stem.Contains('.'))
                                 .GroupBy(d => d.Stem.Split('.')[0], StringComparer.Ordinal)
                                 .Where(g => g.Count() >= 2)
                                 .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

            foreach (var d in folder)
            {
                if (d.Stem.Contains('.'))
                {
                    var prefix = d.Stem.Split('.')[0];
                    if (byPrefix.ContainsKey(prefix)) d.ClassGroup = prefix;
                }
                else if (byPrefix.ContainsKey(d.Stem) && d.Kind == "unit")
                {
                    d.Kind = "overview";        // the class-group landing page (D28)
                    d.ClassGroup = d.Stem;
                }
            }

            var sharedByClass = byPrefix.ToDictionary(
                kv => kv.Key,
                kv => new HashSet<string>(
                    kv.Value.SelectMany(p => p.Files.Where(f => !OIC.Equals(f, p.Primary)))
                            .GroupBy(f => f, OIC).Where(g => g.Count() >= 2).Select(g => g.Key),
                    OIC),
                StringComparer.Ordinal);

            foreach (var d in folder)
            {
                var shared = d.ClassGroup is not null && sharedByClass.TryGetValue(d.ClassGroup, out var s)
                    ? s : null;
                foreach (var f in d.Files)
                    d.FileRoles[f] = OIC.Equals(f, d.Primary) ? "primary"
                        : (shared is not null && shared.Contains(f)) ? "shared" : "paired";
            }
        }
    }

    // ------------------------------------------------------------- db write

    private static double RankBoost(ParsedDoc d)
    {
        var boost = d.Kind switch { "topic" => 3.0, "overview" => 1.5, _ => 1.0 };
        return d.IsTrivial ? boost * 0.3 : boost;
    }

    private async Task WritePageAsync(MySqlConnection db, ParsedDoc d)
    {
        await using var tx = await db.BeginTransactionAsync();

        uint pageId;
        await using (var cmd = db.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText =
                @"INSERT INTO docs_page
                    (path, label, title, kind, folder, primary_file, class_group,
                     is_trivial, provenance, rank_boost, content_hash, modified_at)
                  VALUES
                    (@path, @label, @title, @kind, @folder, @pf, @cg,
                     @triv, @prov, @boost, @hash, @mod)
                  ON DUPLICATE KEY UPDATE
                    id = LAST_INSERT_ID(id), label = VALUES(label), title = VALUES(title),
                    kind = VALUES(kind), folder = VALUES(folder),
                    primary_file = VALUES(primary_file), class_group = VALUES(class_group),
                    is_trivial = VALUES(is_trivial), provenance = VALUES(provenance),
                    rank_boost = VALUES(rank_boost), content_hash = VALUES(content_hash),
                    modified_at = VALUES(modified_at)";
            cmd.Parameters.AddWithValue("@path", d.PagePath);
            cmd.Parameters.AddWithValue("@label", d.Stem);
            cmd.Parameters.AddWithValue("@title", Cap(d.Title, 255));
            cmd.Parameters.AddWithValue("@kind", d.Kind);
            cmd.Parameters.AddWithValue("@folder", d.Folder);
            cmd.Parameters.AddWithValue("@pf", (object?)d.Primary ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@cg", (object?)d.ClassGroup ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@triv", d.IsTrivial ? 1 : 0);
            cmd.Parameters.AddWithValue("@prov", (object?)d.Provenance ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@boost", RankBoost(d));
            cmd.Parameters.AddWithValue("@hash", d.Hash);
            cmd.Parameters.AddWithValue("@mod", d.Modified);
            await cmd.ExecuteNonQueryAsync();
        }
        await using (var cmd = db.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT LAST_INSERT_ID()";
            pageId = Convert.ToUInt32(await cmd.ExecuteScalarAsync());
        }

        foreach (var table in new[] { "docs_chunk", "docs_alias", "docs_page_file" })
        {
            await using var cmd = db.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"DELETE FROM {table} WHERE page_id = @id";
            cmd.Parameters.AddWithValue("@id", pageId);
            await cmd.ExecuteNonQueryAsync();
        }

        // chunks, batched multi-row
        int seq = 0;
        foreach (var batch in d.Chunks.Chunk(100))
        {
            await using var cmd = db.CreateCommand();
            cmd.Transaction = tx;
            var sb = new StringBuilder("INSERT INTO docs_chunk (page_id, seq, kind, heading, member, anchor, body, body_ft) VALUES ");
            int i = 0;
            foreach (var c in batch)
            {
                if (i > 0) sb.Append(',');
                sb.Append($"(@p, @s{i}, @k{i}, @h{i}, @m{i}, @a{i}, @b{i}, @f{i})");
                cmd.Parameters.AddWithValue($"@s{i}", seq++);
                cmd.Parameters.AddWithValue($"@k{i}", c.Kind);
                cmd.Parameters.AddWithValue($"@h{i}", (object?)Cap(c.Heading, 255) ?? DBNull.Value);
                cmd.Parameters.AddWithValue($"@m{i}", (object?)Cap(c.Member, 255) ?? DBNull.Value);
                cmd.Parameters.AddWithValue($"@a{i}", (object?)Cap(c.Anchor, 255) ?? DBNull.Value);
                cmd.Parameters.AddWithValue($"@b{i}", c.Body);
                cmd.Parameters.AddWithValue($"@f{i}", BuildFt(c.Body, d.Stem));
                i++;
            }
            cmd.CommandText = sb.ToString();
            cmd.Parameters.AddWithValue("@p", pageId);
            await cmd.ExecuteNonQueryAsync();
        }

        // aliases (normalized, distinct)
        var aliases = d.Aliases.Select(NormAlias).Where(a => a.Length is > 1 and <= 255)
                               .Distinct(StringComparer.Ordinal).ToList();
        if (aliases.Count > 0)
        {
            await using var cmd = db.CreateCommand();
            cmd.Transaction = tx;
            var sb = new StringBuilder("INSERT IGNORE INTO docs_alias (page_id, alias) VALUES ");
            for (int i = 0; i < aliases.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append($"(@p, @a{i})");
                cmd.Parameters.AddWithValue($"@a{i}", aliases[i]);
            }
            cmd.CommandText = sb.ToString();
            cmd.Parameters.AddWithValue("@p", pageId);
            await cmd.ExecuteNonQueryAsync();
        }

        // file roles
        if (d.FileRoles.Count > 0)
        {
            await using var cmd = db.CreateCommand();
            cmd.Transaction = tx;
            var sb = new StringBuilder("INSERT IGNORE INTO docs_page_file (page_id, file_path, role) VALUES ");
            int i = 0;
            foreach (var (file, role) in d.FileRoles)
            {
                if (i > 0) sb.Append(',');
                sb.Append($"(@p, @f{i}, @r{i})");
                cmd.Parameters.AddWithValue($"@f{i}", Cap(file, 512));
                cmd.Parameters.AddWithValue($"@r{i}", role);
                i++;
            }
            cmd.CommandText = sb.ToString();
            cmd.Parameters.AddWithValue("@p", pageId);
            await cmd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
    }

    // --------------------------------------------------------------- helpers

    private IEnumerable<string> EnumerateDocs()
    {
        foreach (var f in Directory.EnumerateFiles(_root, "*.md", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(_root, f).Replace('\\', '/');
            if (rel.Split('/').Any(seg => seg.StartsWith('.'))) continue;
            yield return rel;
        }
    }

    private string CorpusSignature()
    {
        long count = 0, ticks = 0;
        foreach (var f in Directory.EnumerateFiles(_root, "*.md", SearchOption.AllDirectories))
        {
            count++;
            var t = File.GetLastWriteTimeUtc(f).Ticks;
            if (t > ticks) ticks = t;
        }
        return count + ":" + ticks;
    }

    private static List<string> Cells(string row)
    {
        var t = row.Trim();
        if (t.StartsWith("|")) t = t[1..];
        if (t.EndsWith("|")) t = t[..^1];
        return t.Split('|').Select(c => c.Trim()).ToList();
    }

    // Cap a comma-separated cell at its first N items ("A, B, … +173 more").
    private static string TrimList(string cell, int keep = 12)
    {
        var items = cell.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (items.Length <= keep) return cell;
        return string.Join(", ", items.Take(keep)) + " +" + (items.Length - keep) + " more";
    }

    // G1 shadow tokens: every CamelCase/underscore identifier contributes its split
    // words once, appended after the body; the page label's split is always included.
    internal static string BuildFt(string body, string label)
    {
        var seen = new HashSet<string>(OIC);
        var extra = new List<string>();

        void addSplits(string tok)
        {
            foreach (var w in SplitWords(tok))
                if (w.Length >= 2 && seen.Add(w)) extra.Add(w);
        }

        addSplits(label);
        foreach (Match m in Regex.Matches(body, @"[A-Za-z_][A-Za-z0-9_#]{3,}"))
        {
            if (extra.Count > 400) break;
            var tok = m.Value;
            if (Regex.IsMatch(tok, "[a-z][A-Z]") || tok.Contains('_')) addSplits(tok);
        }

        return extra.Count == 0 ? body : body + "\n" + string.Join(' ', extra);
    }

    internal static List<string> SplitWords(string identifier) =>
        Regex.Matches(identifier.Replace('.', '_'), @"[A-Z]?[a-z0-9]+|[A-Z]+(?![a-z])")
             .Select(m => m.Value).Where(w => w.Length >= 2).ToList();

    private static string NormAlias(string s) =>
        Regex.Replace(s.ToLowerInvariant(), @"\s+", " ").Trim();

    private static string? Cap(string? s, int len) =>
        s is null ? null : (s.Length <= len ? s : s[..len]);

    private static async Task<object?> ScalarAsync(MySqlConnection db, string sql)
    {
        await using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        return await cmd.ExecuteScalarAsync();
    }

    private static async Task<string?> GetMetaAsync(MySqlConnection db, string name)
    {
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT value FROM docs_meta WHERE name = @n";
        cmd.Parameters.AddWithValue("@n", name);
        return (await cmd.ExecuteScalarAsync()) as string;
    }

    private static async Task SetMetaAsync(MySqlConnection db, string name, string value)
    {
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO docs_meta (name, value) VALUES (@n, @v) ON DUPLICATE KEY UPDATE value = VALUES(value)";
        cmd.Parameters.AddWithValue("@n", name);
        cmd.Parameters.AddWithValue("@v", value);
        await cmd.ExecuteNonQueryAsync();
    }
}

public sealed record IndexerStatus(bool Building, int Done, int Total, DateTime? LastCompletedUtc, string? LastError);

/// <summary>
/// The docs_* schema — single source of truth, self-provisioned by the app on Linux
/// (idempotent CREATE TABLE IF NOT EXISTS). Both the indexer (writer) and the search
/// store (reader) ensure it, so either can be hit first on a fresh install.
/// </summary>
internal static class WikiDocsSchema
{
    public static async Task EnsureAsync(MySqlConnection db, CancellationToken ct)
    {
        foreach (var ddl in Ddl)
        {
            await using var cmd = db.CreateCommand();
            cmd.CommandText = ddl;
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    // Contract notes for anything writing these tables:
    //   docs_page.rank_boost — precomputed: topic 3.0 | overview 1.5 | unit 1.0, ×0.3
    //     when is_trivial (1 member, no out-edges, no tables — the packet-struct shape).
    //   docs_page_file.role — 'primary' (first .cpp of the pairing, D26) | 'paired' |
    //     'shared' (non-primary file under ≥2 partials of one class, G10).
    //   docs_chunk.kind — 'narrative' (per h2 section) | 'member' (per bold-led member
    //     entry; anchor via WikiSlug ONLY, G4) | 'maprow' (per MAP table row).
    //   docs_chunk.body_ft — body + G1 CamelCase/underscore shadow-token splits.
    //   docs_alias — normalized (lowercase, single-spaced); topic docs contribute via
    //     the "<!-- aliases: a, b, c -->" comment; every page auto-contributes its
    //     label and the label's word split.
    //   Idempotent reindex — upsert by path; skip when content_hash unchanged.
    public static readonly string[] Ddl =
    {
        @"CREATE TABLE IF NOT EXISTS docs_page (
            id            INT UNSIGNED NOT NULL AUTO_INCREMENT,
            path          VARCHAR(512) NOT NULL,
            label         VARCHAR(255) NOT NULL,
            title         VARCHAR(255) NOT NULL,
            kind          ENUM('unit','overview','topic') NOT NULL DEFAULT 'unit',
            folder        VARCHAR(512) NOT NULL DEFAULT '',
            primary_file  VARCHAR(255) NULL,
            class_group   VARCHAR(255) NULL,
            is_trivial    TINYINT(1) NOT NULL DEFAULT 0,
            provenance    ENUM('model','failed') NULL,
            rank_boost    DOUBLE NOT NULL DEFAULT 1.0,
            content_hash  CHAR(64) NOT NULL DEFAULT '',
            modified_at   DATETIME NULL,
            indexed_at    DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            PRIMARY KEY (id),
            UNIQUE KEY uq_path (path),
            KEY ix_label (label),
            KEY ix_kind  (kind),
            KEY ix_class (class_group)
          ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci",

        @"CREATE TABLE IF NOT EXISTS docs_page_file (
            page_id   INT UNSIGNED NOT NULL,
            file_path VARCHAR(512) NOT NULL,
            role      ENUM('primary','paired','shared') NOT NULL,
            PRIMARY KEY (page_id, file_path),
            KEY ix_file (file_path),
            CONSTRAINT fk_dpf_page FOREIGN KEY (page_id) REFERENCES docs_page (id) ON DELETE CASCADE
          ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci",

        @"CREATE TABLE IF NOT EXISTS docs_chunk (
            id      BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
            page_id INT UNSIGNED NOT NULL,
            seq     INT NOT NULL,
            kind    ENUM('narrative','member','maprow') NOT NULL,
            heading VARCHAR(255) NULL,
            member  VARCHAR(255) NULL,
            anchor  VARCHAR(255) NULL,
            body    MEDIUMTEXT NOT NULL,
            body_ft MEDIUMTEXT NOT NULL,
            PRIMARY KEY (id),
            KEY ix_page (page_id, seq),
            FULLTEXT KEY ft_body (body_ft),
            CONSTRAINT fk_dc_page FOREIGN KEY (page_id) REFERENCES docs_page (id) ON DELETE CASCADE
          ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci",

        @"CREATE TABLE IF NOT EXISTS docs_alias (
            id      INT UNSIGNED NOT NULL AUTO_INCREMENT,
            page_id INT UNSIGNED NOT NULL,
            alias   VARCHAR(255) NOT NULL,
            PRIMARY KEY (id),
            UNIQUE KEY uq_alias_page (alias, page_id),
            KEY ix_alias (alias),
            CONSTRAINT fk_da_page FOREIGN KEY (page_id) REFERENCES docs_page (id) ON DELETE CASCADE
          ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci",

        @"CREATE TABLE IF NOT EXISTS docs_meta (
            name       VARCHAR(64) NOT NULL,
            value      TEXT NOT NULL,
            updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            PRIMARY KEY (name)
          ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci"
    };
}