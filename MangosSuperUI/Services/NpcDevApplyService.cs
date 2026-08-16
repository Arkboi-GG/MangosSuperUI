using System.Globalization;
using System.Text.Json;
using Dapper;
using MangosSuperUI.Models;
using MySqlConnector;

namespace MangosSuperUI.Services;

// ─────────────────────────────────────────────────────────────────────────────
// The write side of the NPC dev window. The client POSTs its change-set (the same
// document it also writes to dev-changes/*.json); this service VERIFIES each packet
// against the current DB (a genuine drift from the packet's `before` blocks it as
// stale), APPLIES the survivors to the mangos world DB, and AUDITS every one through
// AuditService — one AuditBatch per commit, so the Change Graph shows the whole
// commit as a single grouped change (category = "npc").
//
// It only writes the DB. Making the change LIVE on the running server is the client's
// separate owner-clicked reload (.reload creature_template for aggro; .npc reloadspawn
// <guid> for spawn/waypoint) — never issued here.
//
// Column whitelists per packet type keep this from writing anything the tool did not
// author, even though the client is trusted.
// ─────────────────────────────────────────────────────────────────────────────
public sealed class NpcDevApplyService
{
    private readonly ConnectionFactory _db;
    private readonly AuditService _audit;
    private readonly ILogger<NpcDevApplyService> _logger;

    public NpcDevApplyService(ConnectionFactory db, AuditService audit, ILogger<NpcDevApplyService> logger)
    {
        _db = db;
        _audit = audit;
        _logger = logger;
    }

    private const double Eps = 0.1;   // staleness tolerance (yards / seconds / enum steps)

    // after-key -> DB column whitelist, per packet type. Only these columns can be written.
    private static readonly Dictionary<string, string[]> AllowedCols = new()
    {
        ["spawn-move"] = new[] { "position_x", "position_y", "position_z", "orientation" },
        ["spawn-timer"] = new[] { "spawntimesecsmin", "spawntimesecsmax" },
        ["spawn-field"] = new[] { "movement_type", "wander_distance" },
    };

    public async Task<NpcApplyResult> ApplyAsync(NpcApplyRequest req, string? operatorIp)
    {
        string @operator = string.IsNullOrWhiteSpace(req.Session?.Character) ? "msui-client" : req.Session!.Character;
        var result = new NpcApplyResult();

        using var batch = AuditBatch.Begin($"NPC dev — {@operator}");
        result.BatchId = AuditBatch.CurrentId;

        await using var conn = _db.Mangos();
        await conn.OpenAsync();

        foreach (NpcApplyPacket packet in req.Packets)
        {
            var v = new NpcPacketVerdict { Id = packet.Id, Type = packet.Type };
            try
            {
                await ApplyPacketAsync(conn, packet, @operator, operatorIp, v);
            }
            catch (Exception ex)
            {
                v.Verdict = "failed";
                v.Message = ex.Message;
                _logger.LogError(ex, "NPC apply packet #{Id} ({Type}) failed", packet.Id, packet.Type);
            }
            result.Results.Add(v);
        }

        result.Applied = result.Results.Count(r => r.Verdict == "applied");
        result.Stale = result.Results.Count(r => r.Verdict == "stale");
        result.Failed = result.Results.Count(r => r.Verdict is "failed" or "unsupported" or "missing");
        _logger.LogInformation("NPC dev commit by {Op}: {Applied} applied, {Stale} stale, {Failed} failed (batch {Batch})",
            @operator, result.Applied, result.Stale, result.Failed, result.BatchId);
        return result;
    }

    private async Task ApplyPacketAsync(MySqlConnection conn, NpcApplyPacket packet,
        string @operator, string? ip, NpcPacketVerdict v)
    {
        switch (packet.Type)
        {
            case "spawn-move":
            case "spawn-timer":
            case "spawn-field":
                await ApplyCreatureFieldsAsync(conn, packet, @operator, ip, v);
                break;
            case "template-field":
                await ApplyTemplateFieldAsync(conn, packet, @operator, ip, v);
                break;
            case "waypoint-path-replace":
                await ApplyWaypointAsync(conn, packet, @operator, ip, v);
                break;
            default:
                v.Verdict = "unsupported";
                v.Message = $"packet type '{packet.Type}' is not handled yet";
                break;
        }
    }

    // ── creature spawn row (position / timers / movement fields) ──────────────

    private async Task ApplyCreatureFieldsAsync(MySqlConnection conn, NpcApplyPacket packet,
        string @operator, string? ip, NpcPacketVerdict v)
    {
        if (U(packet.Target, "guid") is not { } guid)
        {
            v.Verdict = "failed"; v.Message = "target.guid missing"; return;
        }
        string[] allowed = AllowedCols[packet.Type];

        var rowObj = await conn.QueryFirstOrDefaultAsync("SELECT * FROM `creature` WHERE `guid` = @guid", new { guid });
        if (rowObj is not IDictionary<string, object> row)
        {
            v.Verdict = "missing"; v.Message = $"creature guid {guid} no longer exists"; return;
        }

        // Verify: every `before` value must still match the live row.
        foreach ((string col, JsonElement beforeEl) in packet.Before)
        {
            if (Array.IndexOf(allowed, col) < 0) { v.Verdict = "failed"; v.Message = $"column '{col}' not allowed for {packet.Type}"; return; }
            if (AsNum(beforeEl) is { } beforeVal && row.TryGetValue(col, out object? dbRaw) && !NearlyEq(ToDouble(dbRaw), beforeVal))
            {
                v.Verdict = "stale";
                v.Message = $"{col} drifted (DB {ToDouble(dbRaw):0.##} vs before {beforeVal:0.##}) — re-snapshot and redo";
                return;
            }
        }

        // Apply: only the after keys, all whitelisted.
        var sets = new List<string>();
        var p = new DynamicParameters();
        int i = 0;
        foreach ((string col, JsonElement afterEl) in packet.After)
        {
            if (Array.IndexOf(allowed, col) < 0) { v.Verdict = "failed"; v.Message = $"column '{col}' not allowed for {packet.Type}"; return; }
            if (AsNum(afterEl) is not { } val) continue;
            sets.Add($"`{col}` = @p{i}");
            p.Add($"p{i}", val);
            i++;
        }
        if (sets.Count == 0) { v.Verdict = "applied"; v.Message = "no-op (no changed fields)"; v.AuditId = await AuditAsync(ActionFor(packet.Type), "creature", guid, CreatureName(packet), packet, @operator, ip); return; }
        p.Add("guid", guid);
        await conn.ExecuteAsync($"UPDATE `creature` SET {string.Join(", ", sets)} WHERE `guid` = @guid", p);

        v.Verdict = "applied";
        v.AuditId = await AuditAsync(ActionFor(packet.Type), "creature", guid, CreatureName(packet), packet, @operator, ip);
    }

    // ── creature_template field (detection_range / aggro) — per ENTRY ─────────

    private async Task ApplyTemplateFieldAsync(MySqlConnection conn, NpcApplyPacket packet,
        string @operator, string? ip, NpcPacketVerdict v)
    {
        if (U(packet.Target, "entry") is not { } entry)
        {
            v.Verdict = "failed"; v.Message = "target.entry missing"; return;
        }
        if (Num(packet.After, "detection_range") is not { } after)
        {
            v.Verdict = "failed"; v.Message = "after.detection_range missing"; return;
        }

        // Highest-patch row is what the client saw; verify against it.
        var cur = await conn.ExecuteScalarAsync<double?>(
            "SELECT `detection_range` FROM `creature_template` WHERE `entry` = @entry ORDER BY `patch` DESC LIMIT 1",
            new { entry });
        if (cur is null) { v.Verdict = "missing"; v.Message = $"creature_template entry {entry} not found"; return; }
        if (Num(packet.Before, "detection_range") is { } before && !NearlyEq(cur.Value, before))
        {
            v.Verdict = "stale";
            v.Message = $"detection_range drifted (DB {cur:0.##} vs before {before:0.##})";
            return;
        }

        // detection_range is per-entry; update every patch row so all spawns get it.
        await conn.ExecuteAsync("UPDATE `creature_template` SET `detection_range` = @v WHERE `entry` = @entry",
            new { v = after, entry });

        v.Verdict = "applied";
        v.AuditId = await AuditAsync("npc_aggro", "creature_template", entry, $"entry {entry}", packet, @operator, ip);
    }

    // ── waypoint path (full replace, transactional, renumbered 1-based) ───────

    private async Task ApplyWaypointAsync(MySqlConnection conn, NpcApplyPacket packet,
        string @operator, string? ip, NpcPacketVerdict v)
    {
        string source = Str(packet.Target, "source") ?? "creature_movement";
        bool template = source == "creature_movement_template";
        string keyCol = template ? "entry" : "id";
        if (U(packet.Target, keyCol) is not { } keyVal)
        {
            v.Verdict = "failed"; v.Message = $"target.{keyCol} missing"; return;
        }
        int pathId = (int)(U(packet.Target, "pathId") ?? 0);

        if (!template)
        {
            long exists = await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM `creature` WHERE `guid` = @g", new { g = keyVal });
            if (exists == 0) { v.Verdict = "missing"; v.Message = $"creature guid {keyVal} no longer exists"; return; }
        }

        if (!packet.After.TryGetValue("points", out JsonElement ptsEl) || ptsEl.ValueKind != JsonValueKind.Array)
        {
            v.Verdict = "failed"; v.Message = "after.points missing or not an array"; return;
        }

        await using var tx = await conn.BeginTransactionAsync();
        await conn.ExecuteAsync(
            $"DELETE FROM `{source}` WHERE `{keyCol}` = @k AND `path_id` = @pid",
            new { k = keyVal, pid = pathId }, tx);

        int point = 1;
        foreach (JsonElement n in ptsEl.EnumerateArray())
        {
            await conn.ExecuteAsync(
                $"""
                 INSERT INTO `{source}`
                   (`{keyCol}`, `point`, `position_x`, `position_y`, `position_z`, `orientation`,
                    `waittime`, `wander_distance`, `script_id`, `path_id`)
                 VALUES (@k, @point, @x, @y, @z, @o, @wait, @wander, @script, @pid)
                 """,
                new
                {
                    k = keyVal,
                    point,
                    x = ElNum(n, "x"),
                    y = ElNum(n, "y"),
                    z = ElNum(n, "z"),
                    o = ElNum(n, "orientation"),
                    wait = (uint)Math.Max(0, ElNum(n, "waittime")),
                    wander = ElNum(n, "wander_distance"),
                    script = (uint)Math.Max(0, ElNum(n, "script_id")),
                    pid = pathId,
                }, tx);
            point++;
        }
        await tx.CommitAsync();

        v.Verdict = "applied";
        v.Message = $"{point - 1} node(s)";
        v.AuditId = await AuditAsync("npc_waypoint", source, keyVal, $"{source} {keyVal}", packet, @operator, ip);
    }

    // ── audit ─────────────────────────────────────────────────────────────────

    private async Task<long> AuditAsync(string action, string targetType, uint targetId,
        string targetName, NpcApplyPacket packet, string @operator, string? ip) =>
        await _audit.LogAsync(new AuditEntry
        {
            Operator = @operator,
            OperatorIp = ip,
            Category = "npc",
            Action = action,
            TargetType = targetType,
            TargetId = (int)targetId,
            TargetName = targetName,
            StateBefore = JsonSerializer.Serialize(packet.Before),
            StateAfter = JsonSerializer.Serialize(packet.After),
            // History-only for now; before/after are captured so a later pass can wire revert.
            IsReversible = false,
            RevertKind = RevertKind.None,
            Success = true,
            Notes = $"NPC dev commit (packet #{packet.Id})",
        });

    private static string ActionFor(string type) => type switch
    {
        "spawn-move" => "npc_move",
        "spawn-timer" => "npc_timer",
        "spawn-field" => "npc_field",
        "template-field" => "npc_aggro",
        "waypoint-path-replace" => "npc_waypoint",
        _ => "npc_change",
    };

    private static string CreatureName(NpcApplyPacket packet)
    {
        uint? guid = U(packet.Target, "guid");
        uint? entry = U(packet.Target, "entry");
        return entry is { } e ? $"guid {guid} (entry {e})" : $"guid {guid}";
    }

    // ── JsonElement helpers ─────────────────────────────────────────────────────

    private static double? AsNum(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Number => e.GetDouble(),
        JsonValueKind.String => double.TryParse(e.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double d) ? d : null,
        _ => null,
    };

    private static double? Num(Dictionary<string, JsonElement> d, string k) =>
        d.TryGetValue(k, out JsonElement e) ? AsNum(e) : null;

    private static uint? U(Dictionary<string, JsonElement> d, string k) =>
        Num(d, k) is { } n && n >= 0 ? (uint)Math.Round(n) : null;

    private static string? Str(Dictionary<string, JsonElement> d, string k) =>
        d.TryGetValue(k, out JsonElement e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;

    private static double ElNum(JsonElement obj, string k) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(k, out JsonElement e) ? AsNum(e) ?? 0 : 0;

    private static double ToDouble(object? v) =>
        v is null ? 0 : Convert.ToDouble(v, CultureInfo.InvariantCulture);

    private static bool NearlyEq(double a, double b) => Math.Abs(a - b) <= Eps;
}
