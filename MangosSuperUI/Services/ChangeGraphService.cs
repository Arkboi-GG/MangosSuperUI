using System.Text.Json;
using Dapper;
using MySqlConnector;
using MangosSuperUI.Models;

namespace MangosSuperUI.Services;

/// <summary>
/// Reads audit_log as a drillable graph — Domain → Batch → Entry → Field — and undoes
/// changes at the batch or entry level.
///
/// The log itself is flat. This service supplies the structure: a domain classifier that
/// buckets rows by what they touched, batch grouping from batch_id (with rows that predate
/// batching treated as batches of one), and a revert dispatcher keyed on revert_kind.
///
/// Reverts are deliberately conservative. A strategy that cannot prove it knows how to put
/// something back refuses and says why, rather than half-applying and leaving the world in
/// a state nobody can reason about.
/// </summary>
public class ChangeGraphService
{
    private readonly ConnectionFactory _db;
    private readonly AuditService _audit;
    private readonly SpellCreatorService _spellCreator;
    private readonly ILogger<ChangeGraphService> _logger;

    public ChangeGraphService(
        ConnectionFactory db,
        AuditService audit,
        SpellCreatorService spellCreator,
        ILogger<ChangeGraphService> logger)
    {
        _db = db;
        _audit = audit;
        _spellCreator = spellCreator;
        _logger = logger;
    }

    // ==================================================================
    //  DOMAINS
    // ==================================================================

    /// <summary>
    /// A bucket in the graph's top level. <paramref name="Predicate"/> is raw SQL over
    /// audit_log columns; the classifier turns the whole set into one CASE expression so
    /// a single grouped query produces every rollup.
    /// </summary>
    public record DomainDef(string Key, string Label, string Icon, string Color, string Predicate);

    // First match wins — order runs most specific to least.
    private static readonly DomainDef[] Domains =
    {
        new("loot", "Loot", "fa-dice-d20", "#a855f7",
            @"action LIKE 'loot\_%' OR action LIKE '%lootifier%'
              OR action IN ('baseline_reset_creature_loot','baseline_reset_instance','baseline_reset_table')
              OR target_type IN ('creature_loot','loot_row','loot_table','loot_tables','lootifier','quest_lootifier','crafting_lootifier','instance')"),

        new("spells", "Spells", "fa-wand-sparkles", "#f59e0b",
            @"action LIKE 'spell\_%' OR action LIKE 'patch\_%' OR action = 'baseline_reset_spell'
              OR target_type IN ('spell_template','character_spell','patch','spell_completer')"),

        new("items", "Items", "fa-box-open", "#3b82c4",
            @"action IN ('item_delete','baseline_reset_item','icon_generate')
              OR target_type IN ('item_template','item_base_game','item_custom','icon')"),

        new("npc", "NPCs & Spawns", "fa-dragon", "#e11d48",
            @"category = 'npc' OR action LIKE 'npc\_%'
              OR target_type IN ('creature','creature_movement','creature_movement_template','creature_template')"),

        new("world", "World & Objects", "fa-map-location-dot", "#22c55e",
            @"action LIKE 'gameobject\_%' OR action = 'baseline_reset_gameobject'
              OR target_type IN ('gameobject_template','gameobject_base_game','gameobject_custom')"),

        new("professions", "Professions", "fa-hammer", "#14b8a6",
            @"target_type = 'profession_tuning' OR action LIKE 'profession\_%'"),

        new("bots", "Bots & Chat", "fa-robot", "#5eaaa8",
            @"category = 'bots' OR action LIKE 'chat\_%' OR action LIKE 'persona\_%' OR action LIKE 'voice\_%'
              OR target_type LIKE 'chat\_%' OR target_type = 'bot_persona'"),

        new("database", "Direct Edits", "fa-database", "#8d96a0",
            @"category = 'database' OR action IN ('row_insert','row_delete','cell_edit')"),

        new("config", "Config & Realm", "fa-sliders", "#6366f1",
            @"category IN ('config','paths') OR action IN ('save_config','mangosd_conf_update','realm_update')"),

        new("system", "System & Worlds", "fa-layer-group", "#64748b",
            @"category IN ('system','process','ra') OR action LIKE 'world\_%' OR action LIKE 'backup\_%'
              OR action = 'baseline_initialize'"),
    };

    public static IReadOnlyList<DomainDef> AllDomains => Domains;

    private static string DomainCase()
    {
        var sql = "CASE ";
        foreach (var d in Domains)
            sql += $"WHEN ({d.Predicate}) THEN '{d.Key}' ";
        return sql + "ELSE 'other' END";
    }

    private static string? DomainPredicate(string key)
    {
        if (string.Equals(key, "other", StringComparison.OrdinalIgnoreCase))
            return "NOT (" + string.Join(" OR ", Domains.Select(d => $"({d.Predicate})")) + ")";

        var def = Domains.FirstOrDefault(d => d.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        return def == null ? null : $"({def.Predicate})";
    }

    // ==================================================================
    //  FILTERS
    // ==================================================================

    public class GraphFilter
    {
        public string? Search { get; set; }
        public string? Operator { get; set; }
        /// <summary>Limit to the last N days. Null means all history.</summary>
        public int? Days { get; set; }
        /// <summary>"all" (default), "revertable", "reverted", "failed".</summary>
        public string? Show { get; set; }
    }

    private static (string where, DynamicParameters p) BuildWhere(GraphFilter? f, string? extra = null)
    {
        var clauses = new List<string> { "1=1" };
        var p = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(extra)) clauses.Add(extra);

        if (!string.IsNullOrWhiteSpace(f?.Search))
        {
            clauses.Add("(target_name LIKE @search OR action LIKE @search OR notes LIKE @search OR batch_label LIKE @search)");
            p.Add("search", "%" + f!.Search.Trim() + "%");
        }

        if (!string.IsNullOrWhiteSpace(f?.Operator))
        {
            clauses.Add("operator = @op");
            p.Add("op", f!.Operator);
        }

        if (f?.Days is > 0)
        {
            clauses.Add("timestamp >= DATE_SUB(NOW(), INTERVAL @days DAY)");
            p.Add("days", f.Days!.Value);
        }

        switch (f?.Show)
        {
            case "revertable":
                clauses.Add("revert_kind <> 'none' AND reverted_at IS NULL AND success = 1");
                break;
            case "reverted":
                clauses.Add("reverted_at IS NOT NULL");
                break;
            case "failed":
                clauses.Add("success = 0");
                break;
        }

        return (string.Join(" AND ", clauses), p);
    }

    // ==================================================================
    //  LEVEL 1 — DOMAIN ROLLUP
    // ==================================================================

    public async Task<object> GetOverviewAsync(GraphFilter? filter)
    {
        using var conn = _db.Admin();
        var (where, p) = BuildWhere(filter);

        var rows = (await conn.QueryAsync<DomainRollup>($@"
            SELECT {DomainCase()} AS domainKey,
                   COUNT(*)                                                                    AS changes,
                   COUNT(DISTINCT COALESCE(batch_id, CONCAT('solo:', id)))                     AS batches,
                   SUM(CASE WHEN reverted_at IS NOT NULL THEN 1 ELSE 0 END)                    AS reverted,
                   SUM(CASE WHEN revert_kind <> 'none' AND reverted_at IS NULL AND success = 1
                            THEN 1 ELSE 0 END)                                                 AS revertable,
                   SUM(CASE WHEN success = 0 THEN 1 ELSE 0 END)                                AS failures,
                   MAX(timestamp)                                                              AS lastChange
            FROM audit_log
            WHERE {where}
            GROUP BY domainKey", p)).ToList();

        var byKey = rows.ToDictionary(r => r.DomainKey, StringComparer.OrdinalIgnoreCase);

        // Every known domain is returned even at zero, so the graph's top level is a stable
        // board rather than one that reshuffles as filters change.
        var domains = Domains.Select(d =>
        {
            byKey.TryGetValue(d.Key, out var r);
            return Project(d.Key, d.Label, d.Icon, d.Color, r);
        }).ToList();

        if (byKey.TryGetValue("other", out var other) && other.Changes > 0)
            domains.Add(Project("other", "Unclassified", "fa-circle-question", "#94a3b8", other));

        return new
        {
            domains,
            totals = new
            {
                changes = rows.Sum(r => r.Changes),
                batches = rows.Sum(r => r.Batches),
                reverted = rows.Sum(r => r.Reverted),
                revertable = rows.Sum(r => r.Revertable),
                failures = rows.Sum(r => r.Failures),
            },
            operators = await conn.QueryAsync<string>(
                "SELECT DISTINCT operator FROM audit_log ORDER BY operator"),
        };
    }

    private static object Project(string key, string label, string icon, string color, DomainRollup? r) => new
    {
        key,
        label,
        icon,
        color,
        changes = r?.Changes ?? 0,
        batches = r?.Batches ?? 0,
        reverted = r?.Reverted ?? 0,
        revertable = r?.Revertable ?? 0,
        failures = r?.Failures ?? 0,
        lastChange = r?.LastChange,
    };

    private class DomainRollup
    {
        public string DomainKey { get; set; } = "";
        public int Changes { get; set; }
        public int Batches { get; set; }
        public int Reverted { get; set; }
        public int Revertable { get; set; }
        public int Failures { get; set; }
        public DateTime? LastChange { get; set; }
    }

    // ==================================================================
    //  LEVEL 2 — BATCHES WITHIN A DOMAIN
    // ==================================================================

    public async Task<object> GetBatchesAsync(string domain, GraphFilter? filter, int page = 1, int pageSize = 40)
    {
        var domainPred = DomainPredicate(domain);
        if (domainPred == null) return new { error = "Unknown domain: " + domain };

        using var conn = _db.Admin();
        var (where, p) = BuildWhere(filter, domainPred);

        var total = await conn.ExecuteScalarAsync<int>(
            $@"SELECT COUNT(DISTINCT COALESCE(batch_id, CONCAT('solo:', id)))
               FROM audit_log WHERE {where}", p);

        p.Add("limit", pageSize);
        p.Add("offset", Math.Max(0, page - 1) * pageSize);

        // Rows written before batch_id existed still need to appear, so each becomes a
        // batch of one keyed 'solo:<id>'. Everything downstream treats both shapes alike.
        var batches = await conn.QueryAsync($@"
            SELECT COALESCE(batch_id, CONCAT('solo:', id)) AS batchKey,
                   MAX(COALESCE(batch_label,
                                CONCAT(action, IFNULL(CONCAT(' — ', target_name), '')))) AS label,
                   MAX(batch_id IS NOT NULL)                                             AS isRealBatch,
                   MIN(timestamp)                                                        AS startedAt,
                   MAX(timestamp)                                                        AS endedAt,
                   MAX(operator)                                                         AS operator,
                   COUNT(*)                                                              AS changes,
                   COUNT(DISTINCT action)                                                AS actionCount,
                   MAX(action)                                                           AS sampleAction,
                   SUM(CASE WHEN reverted_at IS NOT NULL THEN 1 ELSE 0 END)              AS reverted,
                   SUM(CASE WHEN revert_kind <> 'none' AND reverted_at IS NULL AND success = 1
                            THEN 1 ELSE 0 END)                                           AS revertable,
                   SUM(CASE WHEN success = 0 THEN 1 ELSE 0 END)                          AS failures
            FROM audit_log
            WHERE {where}
            GROUP BY batchKey
            ORDER BY startedAt DESC
            LIMIT @limit OFFSET @offset", p);

        return new
        {
            domain,
            batches,
            total,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling((double)total / pageSize),
        };
    }

    // ==================================================================
    //  LEVEL 3 — ENTRIES WITHIN A BATCH
    // ==================================================================

    public async Task<object> GetEntriesAsync(string batchKey, int page = 1, int pageSize = 100)
    {
        using var conn = _db.Admin();
        var (where, p) = BatchWhere(batchKey);

        var total = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM audit_log WHERE {where}", p);

        p.Add("limit", pageSize);
        p.Add("offset", Math.Max(0, page - 1) * pageSize);

        var entries = await conn.QueryAsync<AuditLogRow>($@"
            SELECT id, timestamp, operator, operator_ip AS operatorIp, category, action,
                   target_type AS targetType, target_name AS targetName, target_id AS targetId,
                   is_reversible AS isReversible, reverses_id AS reversesId, success, notes,
                   batch_id AS batchId, batch_label AS batchLabel, revert_kind AS revertKind,
                   reverted_at AS revertedAt, reverted_by_id AS revertedById
            FROM audit_log
            WHERE {where}
            ORDER BY id ASC
            LIMIT @limit OFFSET @offset", p);

        return new { batchKey, entries, total, page, pageSize, totalPages = (int)Math.Ceiling((double)total / pageSize) };
    }

    /// <summary>Resolves a batch key back into a WHERE clause. 'solo:N' addresses one row.</summary>
    private static (string where, DynamicParameters p) BatchWhere(string batchKey)
    {
        var p = new DynamicParameters();
        if (batchKey.StartsWith("solo:", StringComparison.Ordinal)
            && long.TryParse(batchKey.AsSpan(5), out var id))
        {
            p.Add("id", id);
            return ("id = @id", p);
        }
        p.Add("batch", batchKey);
        return ("batch_id = @batch", p);
    }

    // ==================================================================
    //  LEVEL 4 — ONE ENTRY, WITH ITS FIELD DIFF
    // ==================================================================

    public async Task<object> GetEntryAsync(long id)
    {
        using var conn = _db.Admin();
        var entry = await conn.QueryFirstOrDefaultAsync<AuditLogRow>(@"
            SELECT id, timestamp, operator, operator_ip AS operatorIp, category, action,
                   target_type AS targetType, target_name AS targetName, target_id AS targetId,
                   ra_command AS raCommand, ra_response AS raResponse,
                   state_before AS stateBefore, state_after AS stateAfter,
                   is_reversible AS isReversible, reverses_id AS reversesId, success, notes,
                   batch_id AS batchId, batch_label AS batchLabel, revert_kind AS revertKind,
                   reverted_at AS revertedAt, reverted_by_id AS revertedById
            FROM audit_log WHERE id = @id", new { id });

        if (entry == null) return new { found = false };

        object? diff = null;
        string? diffNote = null;
        try
        {
            (diff, diffNote) = await BuildDiffAsync(entry);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Change graph: diff failed for audit row {Id}", id);
            diffNote = "Could not compute a diff: " + ex.Message;
        }

        return new
        {
            found = true,
            entry,
            diff,
            diffNote,
            revert = await DescribeRevertAsync(entry),
        };
    }

    /// <summary>
    /// What the undo button will actually do, in plain terms, so the confirm dialog can be
    /// specific instead of saying "revert this?".
    ///
    /// Every answer is checked against the live tables, not against the audit row alone. The
    /// log records that something happened, never whether the result still stands — an entry
    /// cloned and later deleted still carries a 'delete_custom' row, and offering to delete
    /// it again produces a button that cannot possibly succeed.
    /// </summary>
    private async Task<object> DescribeRevertAsync(AuditLogRow e)
    {
        if (e.RevertedAt != null)
            return new { available = false, reason = "Already reverted." };
        if (!e.Success)
            return new { available = false, reason = "This action failed, so there is nothing to undo." };

        var table = ResolveTable(e.TargetType);

        switch (e.RevertKind)
        {
            case RevertKind.Baseline:
            {
                if (table == null)
                    return new { available = false, reason = $"No baseline table is mapped for target type '{e.TargetType}'." };
                if (e.TargetId is not > 0)
                    return new { available = false, reason = "This entry has no target id to restore." };

                using var admin = _db.Admin();
                var ogTable = "og_" + table;
                if (!await TableExistsAsync(admin, ogTable))
                    return new { available = false, reason = $"Baseline table {ogTable} does not exist yet." };

                var hasBaseline = await admin.ExecuteScalarAsync<int>(
                    $"SELECT COUNT(*) FROM `{ogTable}` WHERE entry = @e", new { e = e.TargetId }) > 0;

                return hasBaseline
                    ? new { available = true, summary = $"Restore {table} #{e.TargetId} from the {ogTable} baseline." }
                    : new
                    {
                        available = false,
                        reason = $"{table} #{e.TargetId} has no baseline row — it was created after the snapshot, so there is nothing to restore it to.",
                    };
            }

            case RevertKind.StateBefore:
            {
                if (string.IsNullOrWhiteSpace(e.StateBefore))
                    return new { available = false, reason = "No before-state was captured for this change." };
                if (table == null)
                    return new { available = false, reason = $"No table is mapped for target type '{e.TargetType}'." };
                return new { available = true, summary = $"Re-apply the captured {table} rows for #{e.TargetId}." };
            }

            case RevertKind.DeleteCustom:
            {
                var (exists, what) = await CreatedThingStillExistsAsync(e);
                return exists
                    ? new { available = true, summary = $"Delete {what}." }
                    : new { available = false, reason = $"{what} no longer exists — this change has already been undone." };
            }

            case RevertKind.Registry:
                return new
                {
                    available = false,
                    reason = "This tool owns its own rollback — use the Rollback action on its page so its registry tables stay consistent.",
                };

            default:
                return new { available = false, reason = "No undo path is defined for this kind of change." };
        }
    }

    /// <summary>
    /// Does the thing a creation-type change made still exist? Returns a description either
    /// way so the UI can name it rather than saying "the target".
    /// </summary>
    private async Task<(bool exists, string what)> CreatedThingStillExistsAsync(AuditLogRow e)
    {
        var entry = e.Action == "spell_completer_run"
            ? ParseCompleterSummary(e.StateAfter).entry
            : e.TargetId ?? 0;

        if (entry <= 0) return (false, "the created row");

        var table = e.Action is "spell_completer_run" or "spell_clone"
            ? "spell_template"
            : ResolveTable(e.TargetType);

        if (table == null) return (false, $"#{entry}");

        using var mangos = _db.Mangos();
        var count = await mangos.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM `{table}` WHERE entry = @e", new { e = entry });

        return (count > 0, $"{table} #{entry}");
    }

    private static readonly Dictionary<string, string> TargetTables = new(StringComparer.OrdinalIgnoreCase)
    {
        ["item_template"] = "item_template",
        ["item_base_game"] = "item_template",
        ["item_custom"] = "item_template",
        ["spell_template"] = "spell_template",
        ["gameobject_template"] = "gameobject_template",
        ["gameobject_base_game"] = "gameobject_template",
        ["gameobject_custom"] = "gameobject_template",
    };

    private static string? ResolveTable(string? targetType) =>
        targetType != null && TargetTables.TryGetValue(targetType, out var t) ? t : null;

    /// <summary>
    /// Field-level diff for display. Baseline-kind rows compare live vs og_*; rows carrying
    /// a before-state compare live vs what was captured. Everything else falls back to the
    /// raw state JSON, which the UI renders as-is.
    /// </summary>
    private async Task<(object? diff, string? note)> BuildDiffAsync(AuditLogRow e)
    {
        var table = ResolveTable(e.TargetType);
        if (table == null || e.TargetId is not > 0)
            return (null, null);

        using var mangos = _db.Mangos();
        var current = await mangos.QueryFirstOrDefaultAsync<dynamic>(
            $"SELECT * FROM `{table}` WHERE entry = @e LIMIT 1", new { e = e.TargetId });

        IDictionary<string, object>? reference = null;
        string refLabel;

        if (e.RevertKind == RevertKind.Baseline)
        {
            using var admin = _db.Admin();
            var ogTable = "og_" + table;
            if (!await TableExistsAsync(admin, ogTable))
                return (null, $"Baseline table {ogTable} does not exist yet — initialize the baseline to see field diffs.");

            var og = await admin.QueryFirstOrDefaultAsync<dynamic>(
                $"SELECT * FROM `{ogTable}` WHERE entry = @e LIMIT 1", new { e = e.TargetId });
            reference = og as IDictionary<string, object>;
            refLabel = "baseline";
        }
        else
        {
            reference = FirstRow(e.StateBefore);
            refLabel = "captured before-state";
        }

        if (reference == null)
            return (null, $"No {refLabel} row is available for #{e.TargetId}.");

        if (current is not IDictionary<string, object> cur)
            return (null, $"{table} #{e.TargetId} no longer exists — it was deleted after this change.");

        var fields = new List<object>();
        foreach (var key in reference.Keys)
        {
            if (!cur.TryGetValue(key, out var curVal)) continue;
            var a = reference[key]?.ToString() ?? "";
            var b = curVal?.ToString() ?? "";
            if (a != b) fields.Add(new { field = key, original = a, current = b });
        }

        return (new { table, entry = e.TargetId, reference = refLabel, fields }, null);
    }

    private static IDictionary<string, object>? FirstRow(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var el = doc.RootElement;
            if (el.ValueKind == JsonValueKind.Array)
            {
                if (el.GetArrayLength() == 0) return null;
                el = el[0];
            }
            if (el.ValueKind != JsonValueKind.Object) return null;

            var dict = new Dictionary<string, object>();
            foreach (var prop in el.EnumerateObject())
                dict[prop.Name] = prop.Value.ToString();
            return dict;
        }
        catch
        {
            return null;
        }
    }

    // TABLE_SCHEMA = DATABASE() rather than conn.Database: these connections are handed
    // out closed and Dapper opens them per query, so reading the client-side property
    // first would risk probing an empty schema name and reporting "table missing".
    private static async Task<bool> TableExistsAsync(MySqlConnection conn, string table)
    {
        var count = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @t",
            new { t = table });
        return count > 0;
    }

    // ==================================================================
    //  REVERT
    // ==================================================================

    public class RevertResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public int RowsAffected { get; set; }
        public string Summary { get; set; } = "";
    }

    /// <summary>Undoes one logged change and marks the original row reverted.</summary>
    public async Task<RevertResult> RevertEntryAsync(long id, string? operatorIp)
    {
        using var conn = _db.Admin();
        var entry = await conn.QueryFirstOrDefaultAsync<AuditLogRow>(@"
            SELECT id, timestamp, operator, category, action, target_type AS targetType,
                   target_name AS targetName, target_id AS targetId,
                   state_before AS stateBefore, state_after AS stateAfter,
                   revert_kind AS revertKind, reverted_at AS revertedAt, success, notes,
                   batch_id AS batchId, batch_label AS batchLabel
            FROM audit_log WHERE id = @id", new { id });

        if (entry == null)
            return new RevertResult { Error = "Audit entry not found." };
        if (entry.RevertedAt != null)
            return new RevertResult { Error = "This change has already been reverted." };
        if (!entry.Success)
            return new RevertResult { Error = "This action failed — there is nothing to undo." };

        RevertResult result;
        try
        {
            result = entry.RevertKind switch
            {
                RevertKind.Baseline => await RevertToBaselineAsync(entry),
                RevertKind.StateBefore => await RevertToStateBeforeAsync(entry),
                RevertKind.DeleteCustom => await RevertByDeletingAsync(entry, operatorIp),
                RevertKind.Registry => new RevertResult
                {
                    Error = "This tool owns its own rollback — use the Rollback action on its page so its registry tables stay consistent.",
                },
                _ => new RevertResult { Error = "No undo path is defined for this kind of change." },
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Change graph: revert of audit row {Id} failed", id);
            return new RevertResult { Error = ex.Message };
        }

        if (!result.Success) return result;

        // The revert is itself a change, so it gets its own row pointing back at what it undid.
        var revertId = await _audit.LogAsync(new AuditEntry
        {
            Operator = "admin",
            OperatorIp = operatorIp,
            Category = entry.Category,
            Action = "change_revert",
            TargetType = entry.TargetType,
            TargetName = entry.TargetName,
            TargetId = entry.TargetId,
            ReversesId = entry.Id,
            IsReversible = false,
            RevertKind = RevertKind.None,
            Success = true,
            Notes = $"Reverted audit #{entry.Id} ({entry.Action}): {result.Summary}",
        });

        await conn.ExecuteAsync(
            "UPDATE audit_log SET reverted_at = NOW(3), reverted_by_id = @revertId WHERE id = @id",
            new { revertId, id = entry.Id });

        return result;
    }

    /// <summary>
    /// Undoes a whole batch, newest change first so overlapping edits unwind in the order
    /// they were applied. Entries with no undo path are skipped, not treated as failures —
    /// a Lootifier batch mixes revertable loot rows with registry-owned ones.
    /// </summary>
    public async Task<object> RevertBatchAsync(string batchKey, string? operatorIp)
    {
        using var conn = _db.Admin();
        var (where, p) = BatchWhere(batchKey);

        var ids = (await conn.QueryAsync<long>($@"
            SELECT id FROM audit_log
            WHERE {where} AND success = 1 AND reverted_at IS NULL AND revert_kind <> 'none'
            ORDER BY id DESC", p)).ToList();

        if (ids.Count == 0)
            return new { success = false, error = "Nothing in this batch can be reverted." };

        // One scope so every revert row this produces is itself grouped and can be reviewed
        // (or audited) as a single operation later.
        using var scope = AuditBatch.Begin($"Revert of batch {batchKey}");

        int reverted = 0, failed = 0;
        var errors = new List<string>();

        foreach (var id in ids)
        {
            var r = await RevertEntryAsync(id, operatorIp);
            if (r.Success) reverted++;
            else
            {
                failed++;
                if (errors.Count < 5) errors.Add($"#{id}: {r.Error}");
            }
        }

        return new
        {
            success = reverted > 0,
            reverted,
            failed,
            attempted = ids.Count,
            errors,
        };
    }

    // ---------- strategies ----------

    private async Task<RevertResult> RevertToBaselineAsync(AuditLogRow e)
    {
        var table = ResolveTable(e.TargetType);
        if (table == null)
            return new RevertResult { Error = $"No baseline table is mapped for target type '{e.TargetType}'." };
        if (e.TargetId is not > 0)
            return new RevertResult { Error = "This entry has no target id to restore." };

        using var admin = _db.Admin();
        var ogTable = "og_" + table;
        if (!await TableExistsAsync(admin, ogTable))
            return new RevertResult { Error = $"Baseline table {ogTable} does not exist — initialize the baseline first." };

        var ogRows = (await admin.QueryAsync<dynamic>(
            $"SELECT * FROM `{ogTable}` WHERE entry = @e", new { e = e.TargetId })).ToList();

        if (ogRows.Count == 0)
            return new RevertResult
            {
                Error = $"{table} #{e.TargetId} has no baseline row — it was created after the snapshot, so there is nothing to restore it to.",
            };

        using var mangos = _db.Mangos();
        var rows = await ReplaceRowsAsync(mangos, table, e.TargetId!.Value, ogRows.Cast<IDictionary<string, object>>());

        return new RevertResult
        {
            Success = true,
            RowsAffected = rows,
            Summary = $"restored {table} #{e.TargetId} from {ogTable} ({rows} row(s))",
        };
    }

    private async Task<RevertResult> RevertToStateBeforeAsync(AuditLogRow e)
    {
        var table = ResolveTable(e.TargetType);
        if (table == null)
            return new RevertResult { Error = $"No table is mapped for target type '{e.TargetType}'." };
        if (e.TargetId is not > 0)
            return new RevertResult { Error = "This entry has no target id to restore." };
        if (string.IsNullOrWhiteSpace(e.StateBefore))
            return new RevertResult { Error = "No before-state was captured for this change." };

        var captured = AllRows(e.StateBefore);
        if (captured.Count == 0)
            return new RevertResult { Error = "The captured before-state has no usable rows." };

        using var mangos = _db.Mangos();

        // Only write back columns the table actually has — schemas drift between the
        // capture and now, and an unknown column would fail the whole insert.
        var live = (await mangos.QueryAsync<string>(
            @"SELECT COLUMN_NAME FROM information_schema.COLUMNS
              WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @t",
            new { t = table })).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var filtered = captured
            .Select(r => (IDictionary<string, object>)r
                .Where(kv => live.Contains(kv.Key))
                .ToDictionary(kv => kv.Key, kv => kv.Value))
            .ToList();

        if (filtered.Count == 0 || filtered[0].Count == 0)
            return new RevertResult { Error = "None of the captured columns exist in the current schema." };

        var rows = await ReplaceRowsAsync(mangos, table, e.TargetId!.Value, filtered);

        return new RevertResult
        {
            Success = true,
            RowsAffected = rows,
            Summary = $"re-applied the captured {table} rows for #{e.TargetId} ({rows} row(s))",
        };
    }

    private async Task<RevertResult> RevertByDeletingAsync(AuditLogRow e, string? operatorIp)
    {
        // Spell Completer runs record every rank they produced, so undoing one removes the
        // whole chain rather than orphaning ranks 2+ behind a deleted rank 1.
        if (e.Action == "spell_completer_run")
        {
            var summary = ParseCompleterSummary(e.StateAfter);
            if (summary.entry <= 0)
                return new RevertResult { Error = "This completion did not record which spell it created." };

            var deletedRanks = new List<int>();
            try { deletedRanks = await _spellCreator.DeleteRankChainAsync(summary.entry, operatorIp); }
            catch (Exception ex) { _logger.LogWarning(ex, "Change graph: rank chain delete failed for #{Entry}", summary.entry); }

            var ok = await _spellCreator.DeleteCustomSpellAsync(summary.entry, operatorIp);
            if (!ok && deletedRanks.Count == 0)
                return new RevertResult { Error = $"Spell #{summary.entry} could not be deleted — it may already be gone." };

            return new RevertResult
            {
                Success = true,
                RowsAffected = 1 + deletedRanks.Count,
                Summary = $"deleted spell #{summary.entry} and {deletedRanks.Count} rank(s). Patch rebuild and server restart required",
            };
        }

        if (e.Action == "spell_clone" && e.TargetId is > 0)
        {
            var ok = await _spellCreator.DeleteCustomSpellAsync(e.TargetId.Value, operatorIp);
            return ok
                ? new RevertResult { Success = true, RowsAffected = 1, Summary = $"deleted spell #{e.TargetId}. Server restart required" }
                : new RevertResult { Error = $"Spell #{e.TargetId} could not be deleted — it may already be gone." };
        }

        if (string.Equals(e.TargetType, "item_custom", StringComparison.OrdinalIgnoreCase) && e.TargetId is > 0)
        {
            // Guard the custom range explicitly: deleting a base-game item because a
            // target_type was mislabelled would be unrecoverable without a restore.
            if (e.TargetId < 900000)
                return new RevertResult { Error = $"Item #{e.TargetId} is outside the custom range — refusing to delete a base-game item." };

            using var mangos = _db.Mangos();
            var rows = await mangos.ExecuteAsync(
                "DELETE FROM item_template WHERE entry = @e", new { e = e.TargetId });

            return rows > 0
                ? new RevertResult { Success = true, RowsAffected = rows, Summary = $"deleted custom item #{e.TargetId}" }
                : new RevertResult { Error = $"Item #{e.TargetId} was not found — it may already be gone." };
        }

        return new RevertResult { Error = $"No delete path is defined for '{e.Action}'." };
    }

    private static (int entry, List<int> ranks) ParseCompleterSummary(string? stateAfter)
    {
        var ranks = new List<int>();
        if (string.IsNullOrWhiteSpace(stateAfter)) return (0, ranks);
        try
        {
            using var doc = JsonDocument.Parse(stateAfter);
            var root = doc.RootElement;
            var entry = root.TryGetProperty("entry", out var e) && e.TryGetInt32(out var v) ? v : 0;

            if (root.TryGetProperty("ranks", out var r) && r.ValueKind == JsonValueKind.Array)
                foreach (var item in r.EnumerateArray())
                    if (item.TryGetProperty("entry", out var re) && re.TryGetInt32(out var rv))
                        ranks.Add(rv);

            return (entry, ranks);
        }
        catch
        {
            return (0, ranks);
        }
    }

    private static List<IDictionary<string, object>> AllRows(string? json)
    {
        var list = new List<IDictionary<string, object>>();
        if (string.IsNullOrWhiteSpace(json)) return list;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var items = root.ValueKind == JsonValueKind.Array
                ? root.EnumerateArray().ToList()
                : new List<JsonElement> { root };

            foreach (var el in items)
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                var dict = new Dictionary<string, object>();
                foreach (var prop in el.EnumerateObject())
                {
                    dict[prop.Name] = prop.Value.ValueKind switch
                    {
                        JsonValueKind.Null => null!,
                        JsonValueKind.Number => prop.Value.TryGetInt64(out var l) ? l : prop.Value.GetDouble(),
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        _ => prop.Value.ToString(),
                    };
                }
                if (dict.Count > 0) list.Add(dict);
            }
        }
        catch
        {
            // A malformed capture is not fatal — the caller reports "no usable rows".
        }
        return list;
    }

    /// <summary>
    /// Swaps every row for one entry key in a single transaction: delete what is there,
    /// insert the replacements. Partial application here would be worse than failing.
    /// </summary>
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
