using System.Text.Json;
using Dapper;
using MangosSuperUI.Models;
using MySqlConnector;

namespace MangosSuperUI.Services;

// ─────────────────────────────────────────────────────────────────────────────
// The OG-baseline side of the NPC dev window: "was this mob changed from original?"
// (Diff) and "reset it back to original" (Reset). The og_creature* baseline tables live
// in vmangos_admin (captured by BaselineController/Initialize from the mangos DB); the
// live tables live in mangos. Both are on one MySQL server, so these queries cross-
// reference `vmangos_admin`.`og_*` directly from the mangos connection.
//
// Reset restores from the baseline and audits it (category "npc", RevertKind.Baseline-style
// action), then returns per-target verdicts. It only writes the DB — the client makes the
// change live afterwards with its own owner-clicked reload.
// ─────────────────────────────────────────────────────────────────────────────
public sealed class NpcDevBaselineService
{
    private readonly ConnectionFactory _db;
    private readonly AuditService _audit;
    private readonly ILogger<NpcDevBaselineService> _logger;

    public NpcDevBaselineService(ConnectionFactory db, AuditService audit, ILogger<NpcDevBaselineService> logger)
    {
        _db = db;
        _audit = audit;
        _logger = logger;
    }

    private const double Eps = 0.05;
    private const string SpawnFields =
        "position_x, position_y, position_z, orientation, spawntimesecsmin, spawntimesecsmax, movement_type, wander_distance";

    // ── diff (changed from original?) ─────────────────────────────────────────

    public async Task<NpcDiffResult> DiffAsync(uint guid, uint entry)
    {
        var r = new NpcDiffResult { Guid = guid, Entry = entry };
        await using var conn = _db.Mangos();
        await conn.OpenAsync();

        if (!await OgTableExists(conn, "og_creature"))
            return r;   // HasBaseline stays false → client hides diff/reset
        r.HasBaseline = true;

        r.BaselineHasSpawn = await conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM `vmangos_admin`.`og_creature` WHERE `guid` = @g", new { g = guid }) > 0;

        if (r.BaselineHasSpawn)
        {
            r.SpawnModified = await conn.ExecuteScalarAsync<long>(
                $"""
                 SELECT COUNT(*) FROM `creature` c
                 JOIN `vmangos_admin`.`og_creature` o ON o.`guid` = c.`guid`
                 WHERE c.`guid` = @g AND (
                     ABS(c.position_x - o.position_x) > {Eps} OR
                     ABS(c.position_y - o.position_y) > {Eps} OR
                     ABS(c.position_z - o.position_z) > {Eps} OR
                     ABS(c.orientation - o.orientation) > 0.01 OR
                     c.spawntimesecsmin <> o.spawntimesecsmin OR
                     c.spawntimesecsmax <> o.spawntimesecsmax OR
                     c.movement_type <> o.movement_type OR
                     ABS(c.wander_distance - o.wander_distance) > 0.01)
                 """, new { g = guid }) > 0;

            r.PathModified = await PathModifiedAsync(conn, guid);
        }

        if (entry != 0 && await OgTableExists(conn, "og_creature_template"))
            r.TemplateModified = await conn.ExecuteScalarAsync<long>(
                $"""
                 SELECT COUNT(*) FROM `creature_template` c
                 JOIN `vmangos_admin`.`og_creature_template` o
                     ON o.`entry` = c.`entry` AND o.`patch` = c.`patch`
                 WHERE c.`entry` = @e AND ABS(c.detection_range - o.detection_range) > {Eps}
                 """, new { e = entry }) > 0;

        return r;
    }

    private async Task<bool> PathModifiedAsync(MySqlConnection conn, uint guid)
    {
        if (!await OgTableExists(conn, "og_creature_movement")) return false;
        // Different node count, OR any shared node's position/waittime differs.
        long diff = await conn.ExecuteScalarAsync<long>(
            $"""
             SELECT
               ((SELECT COUNT(*) FROM `creature_movement` WHERE `id` = @g) <>
                (SELECT COUNT(*) FROM `vmangos_admin`.`og_creature_movement` WHERE `id` = @g))
               + (SELECT COUNT(*) FROM `creature_movement` c
                  JOIN `vmangos_admin`.`og_creature_movement` o ON o.`id` = c.`id` AND o.`point` = c.`point`
                  WHERE c.`id` = @g AND (
                      ABS(c.position_x - o.position_x) > {Eps} OR
                      ABS(c.position_y - o.position_y) > {Eps} OR
                      ABS(c.position_z - o.position_z) > {Eps} OR
                      c.waittime <> o.waittime))
             """, new { g = guid });
        return diff > 0;
    }

    // ── reset to original ─────────────────────────────────────────────────────

    public async Task<NpcApplyResult> ResetAsync(NpcResetRequest req, string? ip)
    {
        string @operator = string.IsNullOrWhiteSpace(req.Character) ? "msui-client" : req.Character!;
        var result = new NpcApplyResult();
        using var batch = AuditBatch.Begin($"NPC dev reset — {@operator}");
        result.BatchId = AuditBatch.CurrentId;

        await using var conn = _db.Mangos();
        await conn.OpenAsync();

        bool hasCreature = await OgTableExists(conn, "og_creature");
        bool hasTemplate = await OgTableExists(conn, "og_creature_template");

        int vid = 0;
        foreach (uint guid in req.Guids.Distinct())
        {
            var v = new NpcPacketVerdict { Id = ++vid, Type = "reset-spawn" };
            try { await ResetSpawnAsync(conn, guid, hasCreature, @operator, ip, v); }
            catch (Exception ex) { v.Verdict = "failed"; v.Message = ex.Message; _logger.LogError(ex, "reset spawn {Guid}", guid); }
            result.Results.Add(v);
        }
        foreach (uint entry in req.Entries.Distinct())
        {
            var v = new NpcPacketVerdict { Id = ++vid, Type = "reset-template" };
            try { await ResetTemplateAsync(conn, entry, hasTemplate, @operator, ip, v); }
            catch (Exception ex) { v.Verdict = "failed"; v.Message = ex.Message; _logger.LogError(ex, "reset template {Entry}", entry); }
            result.Results.Add(v);
        }

        result.Applied = result.Results.Count(r => r.Verdict == "applied");
        result.Failed = result.Results.Count(r => r.Verdict is "failed" or "missing" or "unsupported");
        _logger.LogInformation("NPC dev reset by {Op}: {Applied} applied, {Failed} failed (batch {Batch})",
            @operator, result.Applied, result.Failed, result.BatchId);
        return result;
    }

    private async Task ResetSpawnAsync(MySqlConnection conn, uint guid, bool hasBaseline, string op, string? ip, NpcPacketVerdict v)
    {
        if (!hasBaseline) { v.Verdict = "missing"; v.Message = "no og_creature baseline captured yet"; return; }
        long inBaseline = await conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM `vmangos_admin`.`og_creature` WHERE `guid` = @g", new { g = guid });
        if (inBaseline == 0) { v.Verdict = "missing"; v.Message = $"guid {guid} is not in the baseline (added after capture?)"; return; }

        string beforeJson = await SpawnStateJson(conn, guid);

        await using var tx = await conn.BeginTransactionAsync();
        // Restore the whole spawn row + its per-guid path from the baseline (og_* schemas are
        // CREATE TABLE LIKE the source, so column order matches for SELECT *).
        await conn.ExecuteAsync("REPLACE INTO `creature` SELECT * FROM `vmangos_admin`.`og_creature` WHERE `guid` = @g", new { g = guid }, tx);
        await conn.ExecuteAsync("DELETE FROM `creature_movement` WHERE `id` = @g", new { g = guid }, tx);
        if (await OgTableExists(conn, "og_creature_movement", tx))
            await conn.ExecuteAsync("INSERT INTO `creature_movement` SELECT * FROM `vmangos_admin`.`og_creature_movement` WHERE `id` = @g", new { g = guid }, tx);
        await tx.CommitAsync();

        string afterJson = await SpawnStateJson(conn, guid);

        v.Verdict = "applied";
        v.AuditId = await _audit.LogAsync(new AuditEntry
        {
            Operator = op, OperatorIp = ip, Category = "npc", Action = "baseline_reset_creature",
            TargetType = "creature", TargetId = (int)guid, TargetName = $"guid {guid}",
            StateBefore = beforeJson, StateAfter = afterJson,
            IsReversible = false, RevertKind = RevertKind.None, Success = true,
            Notes = "NPC dev reset to OG baseline (spawn + path)",
        });
    }

    private async Task ResetTemplateAsync(MySqlConnection conn, uint entry, bool hasBaseline, string op, string? ip, NpcPacketVerdict v)
    {
        if (!hasBaseline) { v.Verdict = "missing"; v.Message = "no og_creature_template baseline captured yet"; return; }
        double? before = await conn.ExecuteScalarAsync<double?>(
            "SELECT detection_range FROM `creature_template` WHERE `entry` = @e ORDER BY `patch` DESC LIMIT 1", new { e = entry });
        int n = await conn.ExecuteAsync(
            @"UPDATE `creature_template` c
              JOIN `vmangos_admin`.`og_creature_template` o ON o.`entry` = c.`entry` AND o.`patch` = c.`patch`
              SET c.detection_range = o.detection_range WHERE c.`entry` = @e", new { e = entry });
        if (n == 0) { v.Verdict = "missing"; v.Message = $"entry {entry} is not in the baseline"; return; }
        double? after = await conn.ExecuteScalarAsync<double?>(
            "SELECT detection_range FROM `creature_template` WHERE `entry` = @e ORDER BY `patch` DESC LIMIT 1", new { e = entry });

        v.Verdict = "applied";
        v.AuditId = await _audit.LogAsync(new AuditEntry
        {
            Operator = op, OperatorIp = ip, Category = "npc", Action = "baseline_reset_creature_template",
            TargetType = "creature_template", TargetId = (int)entry, TargetName = $"entry {entry}",
            StateBefore = JsonSerializer.Serialize(new { detection_range = before }),
            StateAfter = JsonSerializer.Serialize(new { detection_range = after }),
            IsReversible = false, RevertKind = RevertKind.None, Success = true,
            Notes = "NPC dev reset to OG baseline (template detection_range)",
        });
    }

    private static async Task<string> SpawnStateJson(MySqlConnection conn, uint guid)
    {
        var row = await conn.QueryFirstOrDefaultAsync(
            $"SELECT {SpawnFields} FROM `creature` WHERE `guid` = @g", new { g = guid });
        return row is IDictionary<string, object> dict ? JsonSerializer.Serialize(dict) : "{}";
    }

    private static async Task<bool> OgTableExists(MySqlConnection conn, string table, MySqlTransaction? tx = null) =>
        await conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'vmangos_admin' AND table_name = @t",
            new { t = table }, tx) > 0;
}
