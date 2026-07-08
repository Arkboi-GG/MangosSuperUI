using System.Text.RegularExpressions;
using MySqlConnector;

namespace MangosSuperUI.Services;

/// <summary>
/// The W2 search reader (WIKI_PLAN v0.2 §5). SELECT-only over the <c>docs_*</c> tables
/// in the admin database, which the app provisions and fills itself: schema comes from
/// <see cref="WikiDocsSchema"/> (idempotent, ensured on first touch), rows come from
/// <see cref="WikiIndexer"/>, which this store kicks on every search — cheap, throttled,
/// and a no-op unless the corpus on disk changed. No manual SQL step, no external tool:
/// clone the app, point Wiki:Root at a docs folder, search works.
///
/// Connection: the app's existing <c>connectionStrings:admin</c> entry by default —
/// zero new configuration; <c>Wiki:IndexConnection</c> overrides it if the index ever
/// moves to its own server.
///
/// Ranking model, in order:
///   1. <b>Alias pin</b> — a normalized exact match in <c>docs_alias</c> forces the page
///      to the top ("make a spell hit harder" → the Spell Damage guide, §6.5), before
///      any FULLTEXT scoring.
///   2. <b>Coverage tier</b> — chunks are retrieved by two FULLTEXT passes (a BOOLEAN
///      all-terms pass when the tokenizer permits, then natural-language recall with
///      G1 shadow expansion), but ORDERED first by how many of the query's meaningful
///      terms each chunk actually contains (token-boundary check in C# against
///      <c>body_ft</c>). "xp rates" therefore prefers pages containing both words over
///      pages merely dense in "rates" — and because the check reads the real text, it
///      holds even on an untuned tokenizer where "xp" is invisible to the index.
///   3. <b>Score within tier</b> — tf-idf relevance times the page's precomputed
///      <c>rank_boost</c> (topic 3.0 / overview 1.5 / unit 1.0, ×0.3 trivial). The
///      reader never recomputes boost policy; the indexer owns it.
///
/// Degrades honestly: unreachable server → <c>Ready=false</c> (negative-cached briefly
/// so a dead DB doesn't add a timeout to every keystroke); tables present but EMPTY →
/// <c>Ready=false</c> carrying the indexer's live progress ("building… 214/1148") so
/// the UI can say results are on the way. G2's tokenizer prerequisite
/// (<c>innodb_ft_min_token_size=2</c> — a global, non-dynamic server variable the app
/// must not change under a live mangosd) is detected once and surfaced as
/// <c>Notice</c> so operators see the fix in-product.
/// </summary>
public sealed class WikiSearchStore
{
    private readonly string? _conn;
    private readonly WikiIndexer _indexer;

    // negative cache: after a failed probe, report not-ready without retrying for a while
    private readonly object _probeLock = new();
    private DateTime _failedUntil = DateTime.MinValue;

    private volatile bool _schemaEnsured;
    private string? _notice;        // G2 token-size warning, set once at schema-ensure time
    private volatile int _ftMinToken = 3;   // server's FULLTEXT minimum, read at ensure time

    public WikiSearchStore(IConfiguration config, WikiIndexer indexer)
    {
        _conn = (config["Wiki:IndexConnection"]?.Trim()).NullIfEmpty()
                ?? (config.GetConnectionString("admin")?.Trim()).NullIfEmpty();
        _indexer = indexer;
    }

    public bool Configured => _conn is not null;

    public async Task<WikiSearchResponse> SearchAsync(string query, int take, CancellationToken ct)
    {
        var q = (query ?? "").Trim();
        if (_conn is null || q.Length < 2)
            return new WikiSearchResponse(_conn is not null, q, new List<WikiSearchHit>(), _notice);

        // self-healing index: cheap, throttled, no-op when the corpus signature matches
        _indexer.KickIfStale();

        lock (_probeLock)
        {
            if (DateTime.UtcNow < _failedUntil)
                return NotReady(q);
        }

        try
        {
            await using var db = new MySqlConnection(_conn);
            await db.OpenAsync(ct);
            await EnsureSchemaAsync(db, ct);

            // tables exist from the ensure above — EMPTY means the first build hasn't
            // finished; the response carries the indexer's progress for the UI
            await using (var probe = db.CreateCommand())
            {
                probe.CommandText = "SELECT EXISTS(SELECT 1 FROM docs_page)";
                if (Convert.ToInt64(await probe.ExecuteScalarAsync(ct)) == 0)
                    return NotReady(q);
            }

            var hits = new List<WikiSearchHit>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            // -- 1. alias pin (normalized exact match)
            var norm = Regex.Replace(q.ToLowerInvariant(), @"\s+", " ").Trim();
            await using (var cmd = db.CreateCommand())
            {
                cmd.CommandText =
                    @"SELECT p.path, p.label, p.title, p.kind, p.folder, p.primary_file
                      FROM docs_alias a
                      JOIN docs_page  p ON p.id = a.page_id
                      WHERE a.alias = @alias
                      ORDER BY p.rank_boost DESC
                      LIMIT 5";
                cmd.Parameters.AddWithValue("@alias", norm);
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                {
                    var path = r.GetString(0);
                    if (!seen.Add(path)) continue;
                    hits.Add(new WikiSearchHit(
                        Path: path,
                        Label: r.GetString(1),
                        Title: r.GetString(2),
                        Kind: r.GetString(3),
                        Folder: r.GetString(4),
                        PrimaryFile: r.IsDBNull(5) ? null : r.GetString(5),
                        Anchor: null,
                        Snippet: "",
                        Score: double.MaxValue));
                }
            }

            // -- 2. chunk retrieval, two passes into one per-page pool:
            //    (a) BOOLEAN AND over the meaningful terms — guarantees full-coverage
            //        chunks reach the pool even when single-term chunks would flood the
            //        NL pass's LIMIT. Only possible when every term is long enough for
            //        the server's tokenizer (sub-min terms don't exist in the index).
            //    (b) NATURAL LANGUAGE recall with the shadow-expanded query.
            //    Ranking is then COVERAGE-TIERED in C#: a chunk containing more of the
            //    query's meaningful terms (token-boundary check against body_ft, so
            //    shadow tokens count) always outranks one containing fewer, regardless
            //    of tf-idf — "xp rates" can't lose to something merely dense in "rates".
            //    The C# check reads the actual text, so it works even on an untuned
            //    tokenizer where "xp" is invisible to the index.
            var origTerms = CoverageTerms(q);
            var perPage = new Dictionary<string, ScoredHit>(StringComparer.Ordinal);

            if (origTerms.Count > 0 && origTerms.All(t => t.Length >= _ftMinToken))
            {
                var boolQ = string.Join(' ', origTerms.Select(t => "+" + Regex.Replace(t, @"[^\w#]", "")));
                await CollectChunksAsync(db, boolQ, boolMode: true, limit: 150, q, origTerms, perPage, ct);
            }
            await CollectChunksAsync(db, ExpandQuery(q), boolMode: false, limit: 300, q, origTerms, perPage, ct);

            hits.AddRange(perPage.Values
                .Where(v => seen.Add(v.Hit.Path))
                .OrderByDescending(v => v.Coverage)
                .ThenByDescending(v => v.Score)
                .Select(v => v.Hit));

            return new WikiSearchResponse(true, q, hits.Take(Math.Clamp(take, 1, 50)).ToList(), _notice);
        }
        catch (MySqlException)
        {
            // unreachable server (or a DDL privilege surprise): honest not-ready,
            // and don't hammer on every keystroke
            lock (_probeLock) _failedUntil = DateTime.UtcNow.AddSeconds(30);
            return NotReady(q);
        }
    }

    private readonly record struct ScoredHit(int Coverage, double Score, WikiSearchHit Hit);

    private static async Task CollectChunksAsync(
        MySqlConnection db, string ftQuery, bool boolMode, int limit,
        string originalQuery, List<string> coverageTerms,
        Dictionary<string, ScoredHit> perPage, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ftQuery)) return;
        var mode = boolMode ? "IN BOOLEAN MODE" : "IN NATURAL LANGUAGE MODE";

        await using var cmd = db.CreateCommand();
        cmd.CommandText =
            $@"SELECT p.path, p.label, p.title, p.kind, p.folder, p.primary_file,
                      p.rank_boost, c.anchor, c.body, c.body_ft, c.kind,
                      MATCH(c.body_ft) AGAINST (@q {mode}) AS score
               FROM docs_chunk c
               JOIN docs_page  p ON p.id = c.page_id
               WHERE MATCH(c.body_ft) AGAINST (@q {mode})
               ORDER BY score DESC
               LIMIT {limit}";
        cmd.Parameters.AddWithValue("@q", ftQuery);

        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var path = r.GetString(0);
            var score = r.GetDouble(11) * r.GetDouble(6);            // chunk score x page rank_boost
            var cov = Coverage(r.GetString(9), coverageTerms);       // boundary check incl. shadow tokens

            // Structural evidence ranks below prose: a maprow is a relation listing, not
            // a discussion. Halve its score, and on multi-term queries demote it one
            // coverage tier — hub caller-lists (getConfig, GetLevel) contain nearly every
            // identifier word incidentally and must not win "xp rates"-style queries over
            // pages whose prose actually covers both terms. Single-term member-name
            // lookups keep full maprow coverage.
            if (r.GetString(10) == "maprow")
            {
                score *= 0.5;
                if (coverageTerms.Count >= 2 && cov > 0) cov--;
            }

            if (perPage.TryGetValue(path, out var prev) &&
                (prev.Coverage > cov || (prev.Coverage == cov && prev.Score >= score)))
                continue;

            perPage[path] = new ScoredHit(cov, score, new WikiSearchHit(
                Path: path,
                Label: r.GetString(1),
                Title: r.GetString(2),
                Kind: r.GetString(3),
                Folder: r.GetString(4),
                PrimaryFile: r.IsDBNull(5) ? null : r.GetString(5),
                Anchor: r.IsDBNull(7) ? null : r.GetString(7),
                Snippet: Snippet(r.GetString(8), originalQuery),
                Score: score));
        }
    }

    // The terms that count toward coverage: the query's meaningful words — glue/question
    // words are excluded so "how do I increase xp rates" demands "increase xp rates",
    // not "how" and "do".
    private static readonly HashSet<string> Stopwords = new(StringComparer.Ordinal)
    {
        "a","an","and","are","can","do","does","for","how","i","in","is","it","my",
        "of","on","or","should","that","the","this","to","what","when","where","why","with"
    };

    internal static List<string> CoverageTerms(string q) =>
        Regex.Split(q.ToLowerInvariant(), @"[^a-z0-9_#]+")
             .Where(t => t.Length >= 2 && !Stopwords.Contains(t))
             .Distinct(StringComparer.Ordinal)
             .ToList();

    private static int Coverage(string bodyFt, List<string> terms)
    {
        int n = 0;
        foreach (var t in terms)
            if (FindTokenStart(bodyFt, t) >= 0) n++;
        return n;
    }

    private WikiSearchResponse NotReady(string q)
    {
        var s = _indexer.Status;
        return new WikiSearchResponse(
            Ready: false, Query: q, Hits: new List<WikiSearchHit>(), Notice: _notice,
            Building: s.Building,
            Progress: s.Building ? s.Done + " / " + s.Total : null);
    }

    // ------------------------------------------------------- schema bootstrap
    // Delegated to WikiDocsSchema (shared with WikiIndexer) so either side can be hit
    // first on a fresh install; this store additionally runs the G2 tokenizer check.

    private async Task EnsureSchemaAsync(MySqlConnection db, CancellationToken ct)
    {
        if (_schemaEnsured) return;

        await WikiDocsSchema.EnsureAsync(db, ct);

        // G2: the FULLTEXT tokenizer must keep 2-char tokens ("XP", "GM", "AI", "HP").
        // innodb_ft_min_token_size is global and non-dynamic — fixing it takes [mysqld]
        // + a MariaDB restart + a FULLTEXT rebuild, none of which an app should do to
        // the database mangosd is attached to. Detect once, surface in every response.
        await using (var v = db.CreateCommand())
        {
            v.CommandText = "SELECT @@GLOBAL.innodb_ft_min_token_size";
            var size = Convert.ToInt32(await v.ExecuteScalarAsync(ct));
            _ftMinToken = size;
            _notice = size <= 2 ? null
                : "MariaDB innodb_ft_min_token_size is " + size + " (wiki search needs 2) — short terms like \u201CXP\u201D won\u2019t match. " +
                  "Set innodb_ft_min_token_size=2 under [mysqld], restart MariaDB, then rebuild the index: " +
                  "ALTER TABLE docs_chunk DROP INDEX ft_body; ALTER TABLE docs_chunk ADD FULLTEXT INDEX ft_body (body_ft);";
        }

        _schemaEnsured = true;
    }

    // ---------------------------------------------------------------- helpers

    // Mirror of the indexer's G1 shadow-token rule, applied query-side: identifier-looking
    // terms (CamelCase or snake_case, ≥4 chars) contribute their split words so both
    // "SpellDamageBonusDone" and "spell damage" land on the same chunks.
    internal static string ExpandQuery(string q)
    {
        var extra = new List<string>();
        foreach (var tok in Regex.Split(q, @"[^A-Za-z0-9_#]+"))
        {
            if (tok.Length < 4) continue;
            var words = Regex.Matches(tok, @"[A-Z]?[a-z0-9]+|[A-Z]+(?![a-z])")
                             .Select(m => m.Value)
                             .Where(w => w.Length >= 2)
                             .ToList();
            if (words.Count >= 2) extra.AddRange(words);
            if (tok.Contains('_'))
                extra.AddRange(tok.Split('_', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 2));
        }
        return extra.Count == 0
            ? q
            : q + " " + string.Join(' ', extra.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    // ~200-char window around the earliest token-boundary occurrence of any query term;
    // word-boundary trimmed. Boundary rule: the hit must not be preceded by a letter or
    // digit — "xp" anchors on "XP gain" and "XPValue", never inside "exposing".
    internal static string Snippet(string body, string q)
    {
        var text = Regex.Replace(body ?? "", @"\s+", " ").Trim();
        if (text.Length == 0) return "";

        int at = -1;
        foreach (var t in q.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (t.Length < 2) continue;
            var hit = FindTokenStart(text, t);
            if (hit >= 0 && (at < 0 || hit < at)) at = hit;
        }
        // no token-boundary anchor (the match lived in shadow tokens or mid-identifier,
        // e.g. "GiveXP") — a mid-identifier anchor still beats an unrelated chunk head
        if (at < 0)
        {
            foreach (var t in q.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (t.Length < 2) continue;
                var hit = text.IndexOf(t, StringComparison.OrdinalIgnoreCase);
                if (hit >= 0 && (at < 0 || hit < at)) at = hit;
            }
        }
        if (at < 0) at = 0;

        int start = Math.Max(0, at - 80);
        int len = Math.Min(200, text.Length - start);
        var slice = text.Substring(start, len);

        if (start > 0)
        {
            var sp = slice.IndexOf(' ');
            if (sp > 0 && sp < 30) slice = slice[(sp + 1)..];
            slice = "\u2026" + slice;
        }
        if (start + len < text.Length)
        {
            var sp = slice.LastIndexOf(' ');
            if (sp > slice.Length - 30 && sp > 0) slice = slice[..sp];
            slice += "\u2026";
        }
        return slice;
    }

    // First occurrence of term that starts at a token boundary, or -1.
    private static int FindTokenStart(string text, string term)
    {
        int from = 0;
        while (from <= text.Length - term.Length)
        {
            int i = text.IndexOf(term, from, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return -1;
            if (i == 0 || !char.IsLetterOrDigit(text[i - 1])) return i;
            from = i + 1;
        }
        return -1;
    }
}

// ---------------------------------------------------------------------- DTOs

public sealed record WikiSearchHit(
    string Path, string Label, string Title, string Kind, string Folder,
    string? PrimaryFile, string? Anchor, string Snippet, double Score);

public sealed record WikiSearchResponse(
    bool Ready, string Query, List<WikiSearchHit> Hits, string? Notice = null,
    bool Building = false, string? Progress = null);