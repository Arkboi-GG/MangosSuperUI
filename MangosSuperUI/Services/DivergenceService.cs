using System.Text.RegularExpressions;
using Dapper;
using MySqlConnector;
using MangosSuperUI.Models;

namespace MangosSuperUI.Services;

/// <summary>
/// Answers "what differs from stock VMaNGOS right now?" — the state view, as opposed to
/// ChangeGraphService's event view.
///
/// The distinction matters. The audit log records that something happened; it cannot say
/// whether the result still stands. An entry cloned and later deleted leaves two audit rows
/// and zero divergence, and a graph built from events will happily offer to undo a spell
/// that no longer exists. Everything here is derived from the live tables against their
/// og_* baselines at read time, so a node exists only while the difference does.
///
/// Classification is set arithmetic performed in SQL — never a query per entry. A server
/// with a large Lootifier run has tens of thousands of custom items, and per-entry round
/// trips would make the page unusable at exactly the moment it is most needed.
///
/// Two detection modes:
///   Tracked — custom-content entry ranges plus whatever the audit log names. Fast, and
///             complete for content this panel creates, because custom ranges are found by
///             an indexed range scan rather than by trusting the audit log.
///   Deep    — the same comparison with no entry restriction, so it also finds rows edited
///             by direct SQL or by tools that never logged anything.
/// </summary>
public class DivergenceService
{
    private readonly ConnectionFactory _db;
    private readonly AuditService _audit;
    private readonly SpellCreatorService _spellCreator;
    private readonly DbcService _dbc;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<DivergenceService> _logger;

    /// <summary>base item entry → profession, built once from the recipe DBCs.</summary>
    private Dictionary<int, (uint Id, string Name)>? _professionByItem;
    private readonly object _professionLock = new();

    private readonly SemaphoreSlim _scanLock = new(1, 1);
    private readonly Dictionary<string, CachedScan> _cache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    /// <summary>Pulls "#12345" out of a target_name for rows that never set target_id.</summary>
    private static readonly Regex EntryInName = new(@"#(\d{1,9})", RegexOptions.Compiled);

    public DivergenceService(
        ConnectionFactory db,
        AuditService audit,
        SpellCreatorService spellCreator,
        DbcService dbc,
        IWebHostEnvironment env,
        ILogger<DivergenceService> logger)
    {
        _db = db;
        _audit = audit;
        _spellCreator = spellCreator;
        _dbc = dbc;
        _env = env;
        _logger = logger;
    }

    /// <summary>
    /// Reverses the recipe DBCs into "which profession makes this item". Crafting variants
    /// only record their base item, so this is what lets them be bucketed by profession.
    /// </summary>
    private Dictionary<int, (uint Id, string Name)> ProfessionByItem()
    {
        lock (_professionLock)
        {
            if (_professionByItem != null) return _professionByItem;

            var map = new Dictionary<int, (uint Id, string Name)>();
            try
            {
                foreach (var (id, name) in _dbc.GetProfessions())
                    foreach (var (itemEntry, _) in _dbc.GetProfessionOutputs(id))
                        map.TryAdd((int)itemEntry, (id, name));
            }
            catch (Exception ex)
            {
                // No DBCs loaded — crafting variants simply group as "Unknown profession".
                _logger.LogWarning(ex, "Divergence: profession map unavailable");
            }

            _professionByItem = map;
            return map;
        }
    }

    // ==================================================================
    //  SURFACES
    // ==================================================================

    /// <summary>
    /// A table that can be diffed against a baseline. <paramref name="CustomFrom"/>/<paramref name="CustomTo"/>
    /// mark the entry range reserved for content this panel creates — those rows have no
    /// baseline by definition, so they are additions and are found by range scan rather
    /// than by consulting the audit log.
    /// </summary>
    public record Surface(
        string Domain,
        string Label,
        string Icon,
        string Color,
        string Table,
        int CustomFrom,
        int CustomTo,
        string[] AuditTargetTypes);

    private static readonly Surface[] Surfaces =
    {
        // 900000+ covers both hand-made customs and the 950000+ Lootifier range.
        new("items", "Items", "fa-box-open", "#3b82c4", "item_template",
            900000, int.MaxValue,
            new[] { "item_template", "item_base_game", "item_custom" }),

        new("spells", "Spells", "fa-wand-sparkles", "#f59e0b", "spell_template",
            SpellCreatorService.CUSTOM_SPELL_BASE, SpellCreatorService.CUSTOM_SPELL_MAX,
            new[] { "spell_template", "spell_completer" }),

        new("world", "World & Objects", "fa-map-location-dot", "#22c55e", "gameobject_template",
            900000, int.MaxValue,
            new[] { "gameobject_template", "gameobject_base_game", "gameobject_custom" }),

        // Loot rows key on loot id, not a creature; there is no custom range.
        new("loot", "Creature Loot", "fa-dice-d20", "#a855f7", "creature_loot_template",
            int.MaxValue, int.MaxValue,
            new[] { "creature_loot", "loot_row", "loot_table", "loot_tables" }),
    };

    public static IReadOnlyList<Surface> AllSurfaces => Surfaces;

    private static Surface? SurfaceFor(string domain) =>
        Surfaces.FirstOrDefault(s => s.Domain.Equals(domain, StringComparison.OrdinalIgnoreCase));

    // ==================================================================
    //  MODEL
    // ==================================================================

    public const string StatusModified = "modified";
    public const string StatusAdded = "added";
    public const string StatusRemoved = "removed";

    public class DivergenceNode
    {
        public string Domain { get; set; } = "";
        public string Table { get; set; } = "";
        public int Entry { get; set; }
        public string? Name { get; set; }
        public string Status { get; set; } = StatusModified;
        public int FieldCount { get; set; }
        public List<FieldDiff> Fields { get; set; } = new();

        public int TouchCount { get; set; }
        public DateTime? LastTouched { get; set; }
        public string? LastAction { get; set; }
        public string? LastBatchLabel { get; set; }

        /// <summary>Nothing in the audit log explains this difference.</summary>
        public bool Untracked { get; set; }
    }

    public class FieldDiff
    {
        public string Field { get; set; } = "";
        public string? Baseline { get; set; }
        public string? Current { get; set; }
    }

    /// <summary>Everything known about one diverging entry, resolved once per scan.</summary>
    private class EntryInfo
    {
        public int Entry { get; set; }
        public string Status { get; set; } = "";
        public string? Name { get; set; }
        public LootOrigin? Loot { get; set; }
        public uint? ProfessionId { get; set; }
        public string? ProfessionName { get; set; }

        /// <summary>Top-level bucket: modified | new | lootified | removed.</summary>
        public string Bucket { get; set; } = "";
    }

    /// <summary>One surface's diverging entries, fully resolved, plus when it was measured.</summary>
    private class CachedScan
    {
        public Dictionary<int, EntryInfo> Entries { get; set; } = new();
        public DateTime ScannedAt { get; set; }
        public string Mode { get; set; } = "";
    }

    public const string BucketModified = "modified";
    public const string BucketNew = "new";
    public const string BucketLootified = "lootified";
    public const string BucketRemoved = "removed";

    // ==================================================================
    //  OVERVIEW
    // ==================================================================

    public async Task<object> GetOverviewAsync(string mode)
    {
        var deep = IsDeep(mode);
        var domains = new List<object>();
        var problems = new List<string>();

        foreach (var surface in Surfaces)
        {
            try
            {
                var scan = await ScanAsync(surface, deep);
                domains.Add(new
                {
                    key = surface.Domain,
                    label = surface.Label,
                    icon = surface.Icon,
                    color = surface.Color,
                    table = surface.Table,
                    total = scan.Entries.Count,
                    modified = scan.Entries.Values.Count(e => e.Status == StatusModified),
                    added = scan.Entries.Values.Count(e => e.Status == StatusAdded),
                    removed = scan.Entries.Values.Count(e => e.Status == StatusRemoved),
                    // Bucket counts drive the second tier straight from the board.
                    lootified = scan.Entries.Values.Count(e => e.Bucket == BucketLootified),
                    newlyAdded = scan.Entries.Values.Count(e => e.Bucket == BucketNew),
                    scannedAt = scan.ScannedAt,
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Divergence: {Domain} could not be measured", surface.Domain);
                problems.Add($"{surface.Label}: {ex.Message}");
                domains.Add(new
                {
                    key = surface.Domain,
                    label = surface.Label,
                    icon = surface.Icon,
                    color = surface.Color,
                    table = surface.Table,
                    total = 0,
                    modified = 0,
                    added = 0,
                    removed = 0,
                    scannedAt = (DateTime?)null,
                    error = ex.Message,
                });
            }
        }

        return new { mode = deep ? "deep" : "tracked", domains, problems };
    }

    // ==================================================================
    //  DOMAIN LISTING
    // ==================================================================

    /// <summary>
    /// Resolves every diverging entry into its display identity and bucket, once per scan.
    /// Doing it here rather than per tree level means drilling Items → Lootified → Instance
    /// → Deadmines → a boss is pure in-memory grouping, however deep the tree gets.
    /// </summary>
    private async Task<CachedScan> EnrichAsync(
        MySqlConnection mangos, MySqlConnection admin,
        Surface surface, string ogTable, Dictionary<int, string> statuses)
    {
        var entries = statuses.Keys.ToList();
        var nameCol = await HasColumnAsync(mangos, surface.Table, "name") ? "name" : null;
        var names = await NamesAsync(mangos, admin, surface.Table, ogTable, nameCol, entries);

        var loot = surface.Domain == "items"
            ? await LootOriginsAsync(mangos, admin, entries)
            : new Dictionary<int, LootOrigin>();

        var professions = loot.Count > 0
            ? ProfessionByItem()
            : new Dictionary<int, (uint Id, string Name)>();

        var result = new CachedScan { ScannedAt = DateTime.UtcNow };

        foreach (var (entry, status) in statuses)
        {
            var info = new EntryInfo
            {
                Entry = entry,
                Status = status,
                Name = names.GetValueOrDefault(entry),
                Loot = loot.GetValueOrDefault(entry),
            };

            // Crafted variants only record their base item; the profession comes from
            // whichever recipe produces that base.
            if (info.Loot is { Kind: "crafting" } &&
                professions.TryGetValue(info.Loot.BaseEntry, out var prof))
            {
                info.ProfessionId = prof.Id;
                info.ProfessionName = prof.Name;
            }

            info.Bucket = status switch
            {
                StatusRemoved => BucketRemoved,
                StatusModified => BucketModified,
                // An addition is "lootified" only when a lootifier registry row claims it;
                // anything else you added by hand is a plain new item.
                _ => info.Loot != null ? BucketLootified : BucketNew,
            };

            result.Entries[entry] = info;
        }

        return result;
    }

    // ==================================================================
    //  TREE — variable-depth facet drill
    // ==================================================================

    /// <summary>
    /// One level of the drill-down. The path decides both what is filtered and what the
    /// next facet is, so the tree can be as deep as the data justifies:
    ///
    ///   Items
    ///     modified / new / removed        → name groups
    ///     lootified
    ///       quest                         → name groups
    ///       crafting                      → profession → name groups
    ///       instance                      → instance → boss → name groups
    ///
    /// Other domains stop at the first level, because nothing else has a registry to
    /// explain where its entries came from.
    /// </summary>
    public async Task<object> GetTreeAsync(string domain, string mode, string? path, string? search, int limit = 200)
    {
        var surface = SurfaceFor(domain);
        if (surface == null) return new { error = "Unknown domain: " + domain };

        var scan = await ScanAsync(surface, IsDeep(mode));
        var segments = (path ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries);

        IEnumerable<EntryInfo> rows = scan.Entries.Values;

        // ---- Filter down the path ----
        var crumbs = new List<object>();
        string? nextFacet = "bucket";

        for (var i = 0; i < segments.Length; i++)
        {
            var seg = segments[i];
            switch (nextFacet)
            {
                case "bucket":
                    rows = rows.Where(r => r.Bucket == seg);
                    crumbs.Add(new { key = seg, label = BucketLabel(seg) });
                    nextFacet = seg == BucketLootified ? "loot-kind" : null;
                    break;

                case "loot-kind":
                    rows = rows.Where(r => r.Loot?.Kind == seg);
                    crumbs.Add(new { key = seg, label = LootKindLabel(seg) });
                    nextFacet = seg switch
                    {
                        "crafting" => "profession",
                        "creature" => "instance",
                        "quest" => "baseitem",
                        _ => null,
                    };
                    break;

                case "profession":
                    rows = int.TryParse(seg, out var profId)
                        ? rows.Where(r => r.ProfessionId == (uint)profId)
                        : rows.Where(r => r.ProfessionId == null);
                    crumbs.Add(new { key = seg, label = rows.FirstOrDefault()?.ProfessionName ?? "Unknown profession" });
                    nextFacet = "baseitem";
                    break;

                case "instance":
                    rows = int.TryParse(seg, out var mapId)
                        ? rows.Where(r => r.Loot?.MapId == mapId)
                        : rows.Where(r => r.Loot?.MapId == null);
                    crumbs.Add(new { key = seg, label = int.TryParse(seg, out var m) ? InstanceCatalog.NameFor(m) : "Unknown location" });
                    nextFacet = "boss";
                    break;

                case "boss":
                    rows = int.TryParse(seg, out var creature)
                        ? rows.Where(r => r.Loot?.CreatureEntry == creature)
                        : rows;
                    crumbs.Add(new { key = seg, label = rows.FirstOrDefault()?.Loot?.CreatureName ?? $"Creature {seg}" });
                    nextFacet = "baseitem";
                    break;

                // The unique item a set of variants was rolled from. Under one boss,
                // "26 entries" is really three base items wearing different affixes.
                case "baseitem":
                    rows = int.TryParse(seg, out var baseEntry)
                        ? rows.Where(r => r.Loot?.BaseEntry == baseEntry)
                        : rows;
                    crumbs.Add(new { key = seg, label = rows.FirstOrDefault()?.Loot?.BaseName ?? $"Item {seg}" });
                    nextFacet = null;
                    break;

                default:
                    nextFacet = null;
                    break;
            }
        }

        var list = rows.ToList();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.Trim();
            list = list.Where(r =>
                r.Entry.ToString().Contains(q) ||
                (r.Name?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
        }

        // ---- Still a facet level? Emit buckets rather than entries ----
        if (nextFacet != null)
        {
            var facets = BuildFacets(nextFacet, list, path);
            if (facets.Count > 0)
                return new
                {
                    domain,
                    label = surface.Label,
                    color = surface.Color,
                    icon = surface.Icon,
                    mode = IsDeep(mode) ? "deep" : "tracked",
                    path = path ?? "",
                    crumbs,
                    kind = "facets",
                    facets,
                    total = list.Count,
                    scannedAt = scan.ScannedAt,
                };
            // A facet level with nothing to split on falls through to the leaf list.
        }

        // ---- Leaf: name groups ----
        var groups = await BuildGroupsAsync(surface, list, limit);

        return new
        {
            domain,
            label = surface.Label,
            color = surface.Color,
            icon = surface.Icon,
            mode = IsDeep(mode) ? "deep" : "tracked",
            path = path ?? "",
            crumbs,
            kind = "groups",
            groups,
            total = list.Count,
            totalGroups = groups.Count,
            truncated = groups.Count >= limit,
            scannedAt = scan.ScannedAt,
        };
    }

    private static string BucketLabel(string b) => b switch
    {
        BucketModified => "Modified items",
        BucketNew => "New items",
        BucketLootified => "Lootified items",
        BucketRemoved => "Removed",
        _ => b,
    };

    private static string LootKindLabel(string k) => k switch
    {
        "quest" => "Quest Lootifier",
        "crafting" => "Crafting Lootifier",
        "creature" => "Instance & World Lootifier",
        _ => k,
    };

    private List<object> BuildFacets(string facet, List<EntryInfo> rows, string? path)
    {
        var prefix = string.IsNullOrEmpty(path) ? "" : path + "/";

        IEnumerable<IGrouping<string, EntryInfo>> grouped;
        Func<IGrouping<string, EntryInfo>, string> label;
        Func<IGrouping<string, EntryInfo>, string> icon;
        Func<IGrouping<string, EntryInfo>, string?> hint = _ => null;
        // Facets normally sort by size; a level with a meaningful natural order overrides this.
        Func<IGrouping<string, EntryInfo>, (int, int)>? order = null;

        switch (facet)
        {
            case "bucket":
                grouped = rows.GroupBy(r => r.Bucket);
                label = g => BucketLabel(g.Key);
                icon = g => g.Key switch
                {
                    BucketModified => "fa-pen",
                    BucketNew => "fa-plus",
                    BucketLootified => "fa-dice-d20",
                    _ => "fa-minus",
                };
                hint = g => g.Key switch
                {
                    BucketModified => "Base-game entries changed from stock",
                    BucketNew => "Entries you created by hand",
                    BucketLootified => "Generated by the Lootifiers",
                    _ => "Stock entries no longer present",
                };
                break;

            case "loot-kind":
                grouped = rows.Where(r => r.Loot != null).GroupBy(r => r.Loot!.Kind);
                label = g => LootKindLabel(g.Key);
                icon = g => g.Key switch
                {
                    "quest" => "fa-scroll",
                    "crafting" => "fa-hammer",
                    _ => "fa-dungeon",
                };
                break;

            case "profession":
                grouped = rows.GroupBy(r => r.ProfessionId?.ToString() ?? "unknown");
                label = g => g.First().ProfessionName ?? "Unknown profession";
                icon = _ => "fa-hammer";
                break;

            case "instance":
                grouped = rows.Where(r => r.Loot != null).GroupBy(r => r.Loot!.MapId?.ToString() ?? "unknown");
                label = g => g.First().Loot?.MapName ?? "Unknown location";
                icon = g => g.First().Loot?.Category switch
                {
                    "raid" => "fa-skull",
                    "dungeon" => "fa-dungeon",
                    _ => "fa-earth-americas",
                };
                hint = g => g.First().Loot?.Category;
                break;

            case "baseitem":
                grouped = rows.Where(r => r.Loot != null).GroupBy(r => r.Loot!.BaseEntry.ToString());
                label = g => g.First().Loot?.BaseName ?? $"Item #{g.Key}";
                icon = _ => "fa-box";
                hint = g => "base item #" + g.Key;
                break;

            case "boss":
                grouped = rows.Where(r => r.Loot != null).GroupBy(r => r.Loot!.CreatureEntry.ToString());
                label = g => g.First().Loot?.CreatureName ?? $"Creature #{g.Key}";
                icon = g => g.First().Loot?.IsBoss == true ? "fa-skull" : "fa-paw";
                hint = g => g.First().Loot?.IsBoss == true
                    ? (g.First().Loot!.Optional ? "optional boss" : "boss")
                    : "trash";
                // Encounter order from the curated list, so bosses read in kill order and
                // trash sinks below them — matching how the Instance Loot page presents them.
                order = g => (g.First().Loot?.IsBoss == true ? 0 : 1, g.First().Loot?.BossOrder ?? 0);
                break;

            default:
                return new List<object>();
        }

        var projected = grouped.Select(g => new
        {
            key = g.Key,
            path = prefix + g.Key,
            label = label(g),
            icon = icon(g),
            hint = hint(g),
            count = g.Count(),
            names = g.Select(r => r.Name).Where(n => n != null).Distinct().Count(),
            modified = g.Count(r => r.Status == StatusModified),
            added = g.Count(r => r.Status == StatusAdded),
            removed = g.Count(r => r.Status == StatusRemoved),
            sort = order?.Invoke(g) ?? (0, 0),
        }).ToList();

        return (order != null
                ? projected.OrderBy(f => f.sort.Item1).ThenBy(f => f.sort.Item2).ThenBy(f => f.label)
                : projected.OrderByDescending(f => f.count).ThenBy(f => f.label))
            .Cast<object>()
            .ToList();
    }

    /// <summary>
    /// Collapses a leaf list into name groups — ranks of a spell, or the variants a
    /// Lootifier made from one base item — and attaches field diffs for the page.
    /// </summary>
    private async Task<List<object>> BuildGroupsAsync(Surface surface, List<EntryInfo> rows, int limit)
    {
        using var mangos = _db.Mangos();
        using var admin = _db.Admin();

        var ogTable = "og_" + surface.Table;
        var keyCols = await KeyColumnsAsync(admin, ogTable);
        if (keyCols.Count == 0) keyCols = new List<string> { "entry" };

        var grouped = rows
            .GroupBy(r => string.IsNullOrWhiteSpace(r.Name) ? " entry:" + r.Entry : r.Name.Trim().ToLowerInvariant())
            .Select(g => g.OrderBy(r => r.Entry).ToList())
            .OrderBy(g => g[0].Entry)
            .Take(limit)
            .ToList();

        var pageEntries = grouped.SelectMany(g => g.Select(r => r.Entry)).ToList();

        var fields = await FieldDiffsAsync(mangos, admin, surface, ogTable, keyCols,
            grouped.SelectMany(g => g).Where(r => r.Status == StatusModified).Select(r => r.Entry).ToList());

        var provenance = await ProvenanceAsync(admin, surface, pageEntries);

        return grouped.Select(members =>
        {
            var children = members.Select(r =>
            {
                provenance.TryGetValue(r.Entry, out var p);
                var diffs = fields.GetValueOrDefault(r.Entry) ?? new List<FieldDiff>();
                return new
                {
                    r.Entry,
                    r.Name,
                    r.Status,
                    FieldCount = diffs.Count,
                    Fields = diffs,
                    TouchCount = p?.Count ?? 0,
                    LastTouched = p?.LastTouched,
                    LastAction = p?.LastAction,
                    LastBatchLabel = p?.LastBatchLabel,
                    Untracked = p == null,
                    loot = r.Loot,
                };
            }).ToList();

            var statuses = children.Select(c => c.Status).Distinct().ToList();
            var origins = members.Select(m => m.Loot).Where(o => o != null).Cast<LootOrigin>().ToList();

            return (object)new
            {
                key = members[0].Entry,
                name = members[0].Name ?? $"#{members[0].Entry}",
                count = children.Count,
                minEntry = members.First().Entry,
                maxEntry = members.Last().Entry,
                status = statuses.Count == 1 ? statuses[0] : "mixed",
                modified = children.Count(c => c.Status == StatusModified),
                added = children.Count(c => c.Status == StatusAdded),
                removed = children.Count(c => c.Status == StatusRemoved),
                fieldCount = children.Sum(c => c.FieldCount),
                untracked = children.All(c => c.Untracked),
                lastTouched = children.Max(c => c.LastTouched),
                origin = OriginRollup(origins),
                children,
            };
        }).ToList();
    }

    /// <summary>
    /// One line describing where a whole group of variants came from — "Drops in Deadmines,
    /// Stratholme" rather than repeating a source per row.
    /// </summary>
    private static object? OriginRollup(List<LootOrigin> origins)
    {
        if (origins.Count == 0) return null;

        var kinds = origins.Select(o => o.Kind).Distinct().ToList();
        var places = origins.Where(o => o.MapName != null).Select(o => o.MapName!).Distinct().OrderBy(x => x).ToList();
        var creatures = origins.Where(o => o.CreatureName != null).Select(o => o.CreatureName!).Distinct().OrderBy(x => x).ToList();
        var bases = origins.Where(o => o.BaseName != null).Select(o => o.BaseName!).Distinct().ToList();

        string summary;
        if (kinds.Count > 1)
            summary = $"Mixed sources — {string.Join(", ", kinds)}";
        else
            summary = kinds[0] switch
            {
                "quest" => "Quest reward variants",
                "crafting" => "Crafted variants",
                _ when places.Count == 1 => $"Drops in {places[0]}",
                _ when places.Count > 1 => $"Drops in {string.Join(", ", places.Take(3))}" +
                                          (places.Count > 3 ? $" +{places.Count - 3} more" : ""),
                _ => "Creature drops",
            };

        return new
        {
            kind = kinds.Count == 1 ? kinds[0] : "mixed",
            summary,
            places,
            creatures = creatures.Take(8),
            creatureCount = creatures.Count,
            baseName = bases.FirstOrDefault(),
            baseEntry = origins[0].BaseEntry,
            tiers = origins.Where(o => o.Tier != null).Select(o => o.Tier!).Distinct().Take(6),
        };
    }

    private static bool IsDeep(string? mode) => string.Equals(mode, "deep", StringComparison.OrdinalIgnoreCase);

    // ==================================================================
    //  SCAN — the whole classification, in SQL
    // ==================================================================

    private async Task<CachedScan> ScanAsync(Surface surface, bool deep)
    {
        var key = surface.Domain + "|" + (deep ? "deep" : "tracked");

        await _scanLock.WaitAsync();
        try
        {
            if (_cache.TryGetValue(key, out var cached) && DateTime.UtcNow - cached.ScannedAt < CacheTtl)
                return cached;

            using var mangos = _db.Mangos();
            using var admin = _db.Admin();

            var ogTable = "og_" + surface.Table;
            if (!await TableExistsAsync(admin, ogTable))
                throw new InvalidOperationException(
                    $"Baseline table {ogTable} does not exist — initialize the baseline before measuring drift.");

            var adminDb = await admin.ExecuteScalarAsync<string>("SELECT DATABASE()");
            var og = $"`{adminDb}`.`{ogTable}`";

            var liveCols = await ColumnsAsync(mangos, surface.Table);
            var ogCols = await ColumnsAsync(admin, ogTable);
            var shared = liveCols.Intersect(ogCols, StringComparer.OrdinalIgnoreCase).ToList();
            if (shared.Count == 0)
                throw new InvalidOperationException($"{surface.Table} and {ogTable} share no columns.");

            var keyCols = (await KeyColumnsAsync(admin, ogTable))
                .Where(k => shared.Contains(k, StringComparer.OrdinalIgnoreCase))
                .ToList();
            if (keyCols.Count == 0) keyCols = new List<string> { "entry" };

            // Scope. Deep looks at everything; tracked restricts to custom ranges plus
            // whatever the audit log names — the range half is what actually finds
            // Lootifier output and custom spells, since those audit rows never recorded
            // an entry id at all.
            var p = new DynamicParameters();
            string liveScope = "1=1", ogScope = "1=1";
            if (!deep)
            {
                var audited = await AuditedEntriesAsync(admin, surface);
                p.Add("cfrom", surface.CustomFrom);
                p.Add("cto", surface.CustomTo);

                var clauses = new List<string>();
                if (surface.CustomFrom != int.MaxValue)
                    clauses.Add("{0}.`entry` BETWEEN @cfrom AND @cto");
                if (audited.Count > 0)
                {
                    p.Add("audited", audited);
                    clauses.Add("{0}.`entry` IN @audited");
                }

                // Nothing to look at — an empty IN () is a syntax error, so short-circuit.
                if (clauses.Count == 0)
                {
                    var empty = new CachedScan { Entries = new(), ScannedAt = DateTime.UtcNow, Mode = "tracked" };
                    _cache[key] = empty;
                    return empty;
                }

                var template = "(" + string.Join(" OR ", clauses) + ")";
                liveScope = string.Format(template, "t");
                ogScope = string.Format(template, "o");
            }

            var joinOn = string.Join(" AND ", keyCols.Select(k => $"t.`{k}` = o.`{k}`"));
            var statuses = new Dictionary<int, string>();

            // ---- Added: present live, no baseline row for that entry ----
            foreach (var e in await mangos.QueryAsync<int>($@"
                SELECT DISTINCT t.`entry`
                FROM `{surface.Table}` t
                LEFT JOIN {og} o ON t.`entry` = o.`entry`
                WHERE o.`entry` IS NULL AND {liveScope}", p))
                statuses[e] = StatusAdded;

            // ---- Removed: baseline has it, live does not ----
            foreach (var e in await mangos.QueryAsync<int>($@"
                SELECT DISTINCT o.`entry`
                FROM {og} o
                LEFT JOIN `{surface.Table}` t ON t.`entry` = o.`entry`
                WHERE t.`entry` IS NULL AND {ogScope}", p))
                statuses[e] = StatusRemoved;

            // ---- Modified: same entry on both sides, some column differs ----
            // <=> is null-safe equality, so NULL on one side counts as a real difference
            // instead of an unknown that quietly drops the row.
            var compareCols = shared.Where(c => !keyCols.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList();
            if (compareCols.Count > 0)
            {
                var same = string.Join(" AND ", compareCols.Select(c => $"t.`{c}` <=> o.`{c}`"));
                foreach (var e in await mangos.QueryAsync<int>($@"
                    SELECT DISTINCT t.`entry`
                    FROM `{surface.Table}` t
                    JOIN {og} o ON {joinOn}
                    WHERE NOT ({same}) AND {liveScope}", p))
                    if (!statuses.ContainsKey(e)) statuses[e] = StatusModified;
            }

            // ---- Modified: same entry, different number of rows ----
            // These tables are keyed (entry, patch/build), so an entry can gain or lose a
            // patch row without any single row differing. The column comparison above joins
            // on the full key and would never see it.
            foreach (var e in await mangos.QueryAsync<int>($@"
                SELECT a.`entry` FROM
                    (SELECT t.`entry`, COUNT(*) AS c FROM `{surface.Table}` t WHERE {liveScope} GROUP BY t.`entry`) a
                JOIN
                    (SELECT o.`entry`, COUNT(*) AS c FROM {og} o WHERE {ogScope} GROUP BY o.`entry`) b
                    ON a.`entry` = b.`entry`
                WHERE a.c <> b.c", p))
                if (!statuses.ContainsKey(e)) statuses[e] = StatusModified;

            // Resolve names, loot origin and profession once, here, so every tree level
            // downstream is pure in-memory grouping instead of another round of queries.
            var scan = await EnrichAsync(mangos, admin, surface, ogTable, statuses);
            scan.Mode = deep ? "deep" : "tracked";
            _cache[key] = scan;

            _logger.LogInformation("Divergence: {Mode} scan of {Table} → {Count} diverging entries",
                scan.Mode, surface.Table, statuses.Count);

            return scan;
        }
        finally
        {
            _scanLock.Release();
        }
    }

    /// <summary>
    /// Entries the audit log names for this surface. Most call sites never set target_id —
    /// spell_clone, for one, only ever put the entry inside target_name as "(#40028)" — so
    /// the id is recovered from the text too. Without that, tracked mode sees almost nothing.
    /// </summary>
    private static async Task<List<int>> AuditedEntriesAsync(MySqlConnection admin, Surface surface)
    {
        var rows = await admin.QueryAsync<(int? TargetId, string? TargetName)>(
            @"SELECT target_id AS TargetId, target_name AS TargetName
              FROM audit_log
              WHERE success = 1 AND target_type IN @types",
            new { types = surface.AuditTargetTypes });

        var entries = new HashSet<int>();
        foreach (var (id, name) in rows)
            foreach (var e in EntriesMentionedBy(id, name))
                entries.Add(e);

        return entries.ToList();
    }

    /// <summary>
    /// The entry ids one audit row refers to. Prefers target_id, and falls back to the
    /// "(#12345)" form embedded in target_name because most call sites never populate the
    /// column — spell_clone among them, which is why spells were invisible before.
    /// </summary>
    private static IEnumerable<int> EntriesMentionedBy(int? targetId, string? targetName)
    {
        if (targetId is > 0)
        {
            yield return targetId.Value;
            yield break;
        }

        if (string.IsNullOrEmpty(targetName)) yield break;

        foreach (Match m in EntryInName.Matches(targetName))
            if (int.TryParse(m.Groups[1].Value, out var parsed) && parsed > 0)
                yield return parsed;
    }

    // ==================================================================
    //  NAMES / FIELD DIFFS — bulk, never per entry
    // ==================================================================

    private static async Task<Dictionary<int, string?>> NamesAsync(
        MySqlConnection mangos, MySqlConnection admin,
        string table, string ogTable, string? nameCol, List<int> entries)
    {
        var result = new Dictionary<int, string?>();
        if (entries.Count == 0 || nameCol == null) return result;

        foreach (var chunk in Chunk(entries, 2000))
        {
            foreach (var r in await mangos.QueryAsync<(int Entry, string? Name)>(
                $"SELECT `entry` AS Entry, `{nameCol}` AS Name FROM `{table}` WHERE `entry` IN @e",
                new { e = chunk }))
                result[r.Entry] = r.Name;

            // Removed entries only exist in the baseline, so their name comes from there.
            var missing = chunk.Where(e => !result.ContainsKey(e)).ToList();
            if (missing.Count == 0) continue;

            try
            {
                foreach (var r in await admin.QueryAsync<(int Entry, string? Name)>(
                    $"SELECT `entry` AS Entry, `{nameCol}` AS Name FROM `{ogTable}` WHERE `entry` IN @e",
                    new { e = missing }))
                    result[r.Entry] = r.Name;
            }
            catch
            {
                // Baseline may not carry the column — names are cosmetic, so carry on.
            }
        }

        return result;
    }

    /// <summary>
    /// Per-entry field diffs, in two bulk queries per chunk.
    ///
    /// Rows are matched on the FULL primary key, not just entry. These tables are keyed
    /// (entry, patch) or (entry, build), so an entry routinely holds several rows — taking
    /// the first one on each side and comparing those would report "0 fields changed" for a
    /// difference that lives in any other row, and miss rows added or removed within an
    /// entry entirely.
    /// </summary>
    private static async Task<Dictionary<int, List<FieldDiff>>> FieldDiffsAsync(
        MySqlConnection mangos, MySqlConnection admin,
        Surface surface, string ogTable, List<string> keyCols, List<int> entries)
    {
        var result = new Dictionary<int, List<FieldDiff>>();
        if (entries.Count == 0) return result;

        // Everything past `entry` in the key is what separates rows within one entry.
        var subKey = keyCols.Where(k => !k.Equals("entry", StringComparison.OrdinalIgnoreCase)).ToList();

        string RowKey(IDictionary<string, object> row) => subKey.Count == 0
            ? ""
            : string.Join("|", subKey.Select(k => row.TryGetValue(k, out var v) ? v?.ToString() ?? "" : ""));

        foreach (var chunk in Chunk(entries, 500))
        {
            var live = (await mangos.QueryAsync<dynamic>(
                    $"SELECT * FROM `{surface.Table}` WHERE `entry` IN @e", new { e = chunk }))
                .Cast<IDictionary<string, object>>()
                .GroupBy(r => Convert.ToInt32(r["entry"]))
                .ToDictionary(g => g.Key, g => g.ToList());

            var baseline = (await admin.QueryAsync<dynamic>(
                    $"SELECT * FROM `{ogTable}` WHERE `entry` IN @e", new { e = chunk }))
                .Cast<IDictionary<string, object>>()
                .GroupBy(r => Convert.ToInt32(r["entry"]))
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var entry in chunk)
            {
                live.TryGetValue(entry, out var curRows);
                baseline.TryGetValue(entry, out var ogRows);
                if (curRows == null || ogRows == null) continue;

                var curByKey = curRows.GroupBy(RowKey).ToDictionary(g => g.Key, g => g.First());
                var ogByKey = ogRows.GroupBy(RowKey).ToDictionary(g => g.Key, g => g.First());

                var diffs = new List<FieldDiff>();

                foreach (var (rowKey, og) in ogByKey)
                {
                    var label = subKey.Count == 0 || rowKey.Length == 0 ? "" : $" [{string.Join("/", subKey)}={rowKey}]";

                    if (!curByKey.TryGetValue(rowKey, out var cur))
                    {
                        diffs.Add(new FieldDiff
                        {
                            Field = $"(row{label})",
                            Baseline = "present",
                            Current = "deleted",
                        });
                        continue;
                    }

                    foreach (var kv in og)
                    {
                        if (!cur.TryGetValue(kv.Key, out var curVal)) continue;
                        var a = kv.Value?.ToString() ?? "";
                        var b = curVal?.ToString() ?? "";
                        if (a != b)
                            diffs.Add(new FieldDiff { Field = kv.Key + label, Baseline = a, Current = b });
                    }
                }

                // Rows this entry gained that the baseline never had.
                foreach (var rowKey in curByKey.Keys.Where(k => !ogByKey.ContainsKey(k)))
                {
                    var label = subKey.Count == 0 || rowKey.Length == 0 ? "" : $" [{string.Join("/", subKey)}={rowKey}]";
                    diffs.Add(new FieldDiff
                    {
                        Field = $"(row{label})",
                        Baseline = "absent",
                        Current = "added",
                    });
                }

                if (diffs.Count > 0) result[entry] = diffs;
            }
        }

        return result;
    }

    private static IEnumerable<List<T>> Chunk<T>(List<T> source, int size)
    {
        for (var i = 0; i < source.Count; i += size)
            yield return source.GetRange(i, Math.Min(size, source.Count - i));
    }

    // ==================================================================
    //  LOOT PROVENANCE — what a generated item actually is
    // ==================================================================

    public class LootOrigin
    {
        /// <summary>creature | quest | crafting.</summary>
        public string Kind { get; set; } = "";
        public string Summary { get; set; } = "";
        public int BaseEntry { get; set; }
        public string? BaseName { get; set; }
        public int CreatureEntry { get; set; }
        public string? CreatureName { get; set; }
        public int? MapId { get; set; }
        public string? MapName { get; set; }
        public string? Category { get; set; }
        public string? Tier { get; set; }
        public float BudgetPct { get; set; }

        /// <summary>From the curated boss list, not inferred from spawns.</summary>
        public bool IsBoss { get; set; }
        public int BossOrder { get; set; }
        public bool Optional { get; set; }
    }

    /// <summary>
    /// Resolves generated items back to what produced them, from the Lootifier's own
    /// registry — the audit log never recorded these, so this is the only source.
    ///
    /// All three lootifiers share lootifier_generated_items and are told apart by the
    /// creature_entry sentinel: &gt;0 is a real creature (ARPG), 0 is a quest reward,
    /// -1 is crafting.
    /// </summary>
    private async Task<Dictionary<int, LootOrigin>> LootOriginsAsync(
        MySqlConnection mangos, MySqlConnection admin, List<int> entries)
    {
        var result = new Dictionary<int, LootOrigin>();
        if (entries.Count == 0) return result;
        if (!await TableExistsAsync(admin, "lootifier_generated_items")) return result;

        var rows = new List<(int Generated, int Base, int Creature, string? Tier, float Budget)>();
        foreach (var chunk in Chunk(entries, 2000))
            rows.AddRange(await admin.QueryAsync<(int, int, int, string?, float)>(
                @"SELECT generated_entry, base_entry, creature_entry, tier_name, budget_pct
                  FROM lootifier_generated_items WHERE generated_entry IN @e",
                new { e = chunk }));

        if (rows.Count == 0) return result;

        // Names and maps resolved in bulk rather than per item.
        var baseIds = rows.Select(r => r.Base).Where(b => b > 0).Distinct().ToList();
        var creatureIds = rows.Select(r => r.Creature).Where(c => c > 0).Distinct().ToList();

        var baseNames = new Dictionary<int, string?>();
        foreach (var chunk in Chunk(baseIds, 2000))
            foreach (var r in await mangos.QueryAsync<(int Entry, string? Name)>(
                "SELECT entry AS Entry, name AS Name FROM item_template WHERE entry IN @e", new { e = chunk }))
                baseNames[r.Entry] = r.Name;

        var creatureNames = new Dictionary<int, string?>();
        var creatureMaps = new Dictionary<int, int>();
        foreach (var chunk in Chunk(creatureIds, 2000))
        {
            foreach (var r in await mangos.QueryAsync<(int Entry, string? Name)>(
                "SELECT entry AS Entry, name AS Name FROM creature_template WHERE entry IN @e", new { e = chunk }))
                creatureNames[r.Entry] = r.Name;

            // Spawn table is the fallback only. Curated bosses win below, because a boss's
            // instance is a fact about the encounter, not something to infer from where its
            // model happens to be placed.
            foreach (var r in await mangos.QueryAsync<(int Id, int Map)>(
                @"SELECT id AS Id, map AS Map FROM (
                      SELECT id, map, COUNT(*) AS c FROM creature WHERE id IN @e GROUP BY id, map
                  ) x ORDER BY c DESC", new { e = chunk }))
                creatureMaps.TryAdd(r.Id, r.Map);
        }

        foreach (var (generated, baseEntry, creature, tier, budget) in rows)
        {
            var origin = new LootOrigin
            {
                BaseEntry = baseEntry,
                BaseName = baseNames.GetValueOrDefault(baseEntry),
                CreatureEntry = creature,
                Tier = string.IsNullOrWhiteSpace(tier) ? null : tier,
                BudgetPct = budget,
            };

            var baseLabel = origin.BaseName ?? $"#{baseEntry}";

            if (creature > 0)
            {
                origin.Kind = "creature";
                origin.CreatureName = creatureNames.GetValueOrDefault(creature);

                // The curated boss list is authoritative for both the instance and the
                // encounter order — the same data the Instance Loot page renders from.
                var boss = InstanceCatalog.BossFor(creature, _env.WebRootPath);
                if (boss != null)
                {
                    origin.MapId = boss.Value.MapId;
                    origin.IsBoss = true;
                    origin.BossOrder = boss.Value.Boss.Order;
                    origin.Optional = boss.Value.Boss.Optional;
                    if (string.IsNullOrWhiteSpace(origin.CreatureName))
                        origin.CreatureName = boss.Value.Boss.Name;
                }
                else if (creatureMaps.TryGetValue(creature, out var map))
                {
                    origin.MapId = map;
                }

                if (origin.MapId is { } mid)
                {
                    origin.MapName = InstanceCatalog.NameFor(mid);
                    origin.Category = InstanceCatalog.Find(mid)?.Category ?? "world";
                }

                var who = origin.CreatureName ?? $"creature #{creature}";
                origin.Summary = origin.MapName != null
                    ? $"Drops from {who} — {origin.MapName}"
                    : $"Drops from {who}";
            }
            else if (creature == 0)
            {
                origin.Kind = "quest";
                origin.Category = "quest";
                origin.Summary = $"Quest reward variant of {baseLabel}";
            }
            else
            {
                origin.Kind = "crafting";
                origin.Category = "crafting";
                origin.Summary = $"Crafted variant of {baseLabel}";
            }

            result[generated] = origin;
        }

        return result;
    }

    // ==================================================================
    //  PROVENANCE
    // ==================================================================

    private class Provenance
    {
        public int Count { get; set; }
        public DateTime? LastTouched { get; set; }
        public string? LastAction { get; set; }
        public string? LastBatchLabel { get; set; }
    }

    private static async Task<Dictionary<int, Provenance>> ProvenanceAsync(
        MySqlConnection admin, Surface surface, List<int> entries)
    {
        var result = new Dictionary<int, Provenance>();
        if (entries.Count == 0) return result;

        // Cannot be filtered SQL-side: most rows carry the entry only inside target_name as
        // "(#12345)", so matching means reading this surface's rows and resolving each one
        // the same way the scan does. Ordered newest-first, so the first sighting of an
        // entry is its latest touch.
        var wanted = entries.ToHashSet();

        var rows = await admin.QueryAsync<(int? TargetId, string? TargetName, DateTime Ts, string Action, string? BatchLabel)>(
            @"SELECT target_id AS TargetId, target_name AS TargetName,
                     timestamp AS Ts, action AS Action, batch_label AS BatchLabel
              FROM audit_log
              WHERE success = 1 AND target_type IN @types
              ORDER BY id DESC",
            new { types = surface.AuditTargetTypes });

        foreach (var r in rows)
        {
            foreach (var e in EntriesMentionedBy(r.TargetId, r.TargetName))
            {
                if (!wanted.Contains(e)) continue;

                if (!result.TryGetValue(e, out var prov))
                {
                    prov = new Provenance
                    {
                        LastTouched = r.Ts,
                        LastAction = r.Action,
                        LastBatchLabel = string.IsNullOrWhiteSpace(r.BatchLabel) ? null : r.BatchLabel,
                    };
                    result[e] = prov;
                }
                prov.Count++;
            }
        }

        return result;
    }

    // ==================================================================
    //  RESOLVE
    // ==================================================================

    public class ResolveResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public string Summary { get; set; } = "";
    }

    /// <summary>
    /// Removes an entry's divergence: restore from baseline, delete if it is custom, or
    /// re-insert if it was deleted from stock. State is re-derived from the tables first,
    /// so a stale page can never drive a destructive write.
    /// </summary>
    public async Task<ResolveResult> ResolveAsync(string domain, int entry, string? operatorIp)
    {
        var surface = SurfaceFor(domain);
        if (surface == null) return new ResolveResult { Error = "Unknown domain: " + domain };

        using var mangos = _db.Mangos();
        using var admin = _db.Admin();

        var ogTable = "og_" + surface.Table;
        if (!await TableExistsAsync(admin, ogTable))
            return new ResolveResult { Error = $"Baseline table {ogTable} does not exist." };

        var liveCount = await mangos.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM `{surface.Table}` WHERE entry = @e", new { e = entry });
        var ogRows = (await admin.QueryAsync<dynamic>(
            $"SELECT * FROM `{ogTable}` WHERE entry = @e", new { e = entry })).ToList();

        if (liveCount == 0 && ogRows.Count == 0)
            return new ResolveResult { Error = $"{surface.Table} #{entry} exists in neither the world nor the baseline — nothing to resolve." };

        string summary;

        if (ogRows.Count == 0)
        {
            // Added by us — resolving means removing it.
            if (surface.Table == "spell_template")
            {
                // Custom spells own skill_line_ability, spell_chain and trainer rows, so
                // dropping the template alone would leave the world pointing at a ghost.
                var ranks = await _spellCreator.DeleteRankChainAsync(entry, operatorIp);
                var ok = await _spellCreator.DeleteCustomSpellAsync(entry, operatorIp);
                if (!ok && ranks.Count == 0)
                    return new ResolveResult { Error = $"Spell #{entry} could not be deleted." };
                summary = $"deleted custom spell #{entry} and {ranks.Count} rank(s)";
            }
            else
            {
                var removed = await mangos.ExecuteAsync(
                    $"DELETE FROM `{surface.Table}` WHERE entry = @e", new { e = entry });
                if (removed == 0)
                    return new ResolveResult { Error = $"{surface.Table} #{entry} was not found." };
                summary = $"deleted added {surface.Table} #{entry} ({removed} row(s))";
            }
        }
        else
        {
            var rows = await ReplaceRowsAsync(mangos, surface.Table, entry,
                ogRows.Cast<IDictionary<string, object>>());
            summary = liveCount == 0
                ? $"re-inserted {surface.Table} #{entry} from baseline ({rows} row(s))"
                : $"restored {surface.Table} #{entry} to baseline ({rows} row(s))";
        }

        await _audit.LogAsync(new AuditEntry
        {
            Operator = "admin",
            OperatorIp = operatorIp,
            Category = "content",
            Action = "divergence_resolve",
            TargetType = surface.AuditTargetTypes[0],
            TargetName = $"{surface.Table} #{entry}",
            TargetId = entry,
            IsReversible = false,
            RevertKind = RevertKind.None,
            Success = true,
            Notes = $"Resolved drift on {surface.Table} #{entry}: {summary}",
        });

        InvalidateCache();
        return new ResolveResult { Success = true, Summary = summary };
    }

    public async Task<object> ResolveManyAsync(string domain, int[] entries, string? operatorIp)
    {
        if (entries.Length == 0) return new { success = false, error = "No entries selected." };

        using var scope = AuditBatch.Begin($"Resolve drift — {domain}, {entries.Length} entr(y/ies)");

        int done = 0, failed = 0;
        var errors = new List<string>();

        foreach (var entry in entries.Distinct())
        {
            var r = await ResolveAsync(domain, entry, operatorIp);
            if (r.Success) done++;
            else
            {
                failed++;
                if (errors.Count < 5) errors.Add($"#{entry}: {r.Error}");
            }
        }

        return new { success = done > 0, resolved = done, failed, attempted = entries.Length, errors };
    }

    public void InvalidateCache()
    {
        lock (_cache) _cache.Clear();
    }

    // ==================================================================
    //  SCHEMA HELPERS
    // ==================================================================

    private static async Task<bool> TableExistsAsync(MySqlConnection conn, string table) =>
        await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @t",
            new { t = table }) > 0;

    private static async Task<bool> HasColumnAsync(MySqlConnection conn, string table, string column) =>
        await conn.ExecuteScalarAsync<int>(
            @"SELECT COUNT(*) FROM information_schema.COLUMNS
              WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @t AND COLUMN_NAME = @c",
            new { t = table, c = column }) > 0;

    private static async Task<List<string>> ColumnsAsync(MySqlConnection conn, string table) =>
        (await conn.QueryAsync<string>(
            @"SELECT COLUMN_NAME FROM information_schema.COLUMNS
              WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @t ORDER BY ORDINAL_POSITION",
            new { t = table })).ToList();

    private static async Task<List<string>> KeyColumnsAsync(MySqlConnection conn, string table) =>
        (await conn.QueryAsync<string>(
            @"SELECT COLUMN_NAME FROM information_schema.STATISTICS
              WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @t AND INDEX_NAME = 'PRIMARY'
              ORDER BY SEQ_IN_INDEX",
            new { t = table })).ToList();

    private static async Task<int> ReplaceRowsAsync(
        MySqlConnection conn, string table, int entry, IEnumerable<IDictionary<string, object>> rows)
    {
        var list = rows.ToList();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();

        using var tx = await conn.BeginTransactionAsync();
        try
        {
            await conn.ExecuteAsync($"DELETE FROM `{table}` WHERE entry = @entry", new { entry }, tx);

            var inserted = 0;
            foreach (var row in list)
            {
                var cols = string.Join(", ", row.Keys.Select(k => $"`{k}`"));
                var vals = string.Join(", ", row.Keys.Select(k => $"@{k}"));
                var p = new DynamicParameters();
                foreach (var kv in row) p.Add(kv.Key, kv.Value);
                inserted += await conn.ExecuteAsync($"INSERT INTO `{table}` ({cols}) VALUES ({vals})", p, tx);
            }

            await tx.CommitAsync();
            return inserted;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }
}
