using Microsoft.AspNetCore.Mvc;
using MangosSuperUI.Models;
using MangosSuperUI.Services;
using Dapper;

namespace MangosSuperUI.Controllers;

/// <summary>
/// Data + apply endpoints for the MSUI client's NPC dev window. <c>Snapshot</c> is the
/// read-only feed (spawns, patrol paths, aggro-relevant template fields) — one round
/// trip for an area instead of four CSV dumps. <c>Apply</c> is the write path: the
/// window's change-set is verified, applied to the mangos world DB, and audited
/// (<see cref="NpcDevApplyService"/> → AuditService, category "npc"), so the client
/// commits directly instead of hand-uploading a file.
///
/// Making a committed change LIVE on the running server is the client's separate
/// owner-clicked reload (.reload creature_template / .npc reloadspawn); this controller
/// never issues runtime-control commands.
///
/// Contract documented in MSUIClient repo: NPC_DEV_WINDOW.md § HTTP contracts.
/// </summary>
public class NpcDevController : Controller
{
    private const int MaxGuids = 500;       // explicit ?guids= list cap
    private const int MaxEntries = 500;     // explicit ?entries= list cap
    private const int MaxSpawnRows = 4000;  // spatial query hard cap
    private const float MaxRange = 600f;    // yards; spatial half-box cap

    private readonly ConnectionFactory _db;
    private readonly NpcDevApplyService _apply;
    private readonly NpcDevBaselineService _baseline;
    private readonly ILogger<NpcDevController> _logger;

    public NpcDevController(ConnectionFactory db, NpcDevApplyService apply,
        NpcDevBaselineService baseline, ILogger<NpcDevController> logger)
    {
        _db = db;
        _apply = apply;
        _baseline = baseline;
        _logger = logger;
    }

    /// <summary>
    /// GET /NpcDev/Snapshot?map=0&amp;nearX=-9450&amp;nearY=-14&amp;range=300&amp;guids=1,2,3&amp;entries=448
    ///
    /// Selection = spawns on <paramref name="map"/> matching EITHER the explicit guid
    /// list OR the square around (nearX, nearY). Movement rows are returned for every
    /// selected spawn's guid; movement-template rows and template subsets for every
    /// selected spawn's entry pool plus the explicit entry list.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Snapshot(
        int map, string? guids = null, string? entries = null,
        float? nearX = null, float? nearY = null, float range = 250f)
    {
        List<uint> guidList = ParseIdList(guids, MaxGuids);
        List<uint> entryList = ParseIdList(entries, MaxEntries);
        bool spatial = nearX.HasValue && nearY.HasValue;
        if (!spatial && guidList.Count == 0 && entryList.Count == 0)
            return BadRequest("provide nearX/nearY, guids, or entries");
        range = Math.Clamp(range, 1f, MaxRange);

        try
        {
            using var conn = _db.Mangos();

            // ── spawn rows ────────────────────────────────────────────────────
            var where = new List<string>();
            var args = new DynamicParameters();
            args.Add("map", map);
            if (guidList.Count > 0) { where.Add("guid IN @guids"); args.Add("guids", guidList); }
            if (spatial)
            {
                where.Add("(position_x BETWEEN @x0 AND @x1 AND position_y BETWEEN @y0 AND @y1)");
                args.Add("x0", nearX!.Value - range); args.Add("x1", nearX.Value + range);
                args.Add("y0", nearY!.Value - range); args.Add("y1", nearY.Value + range);
            }
            string spawnFilter = where.Count > 0 ? $"({string.Join(" OR ", where)})" : "0";
            var creatures = (await conn.QueryAsync(
                $"""
                 SELECT guid, id, id2, id3, id4, id5, map,
                        position_x AS positionX, position_y AS positionY, position_z AS positionZ,
                        orientation,
                        spawntimesecsmin AS spawnTimeSecsMin, spawntimesecsmax AS spawnTimeSecsMax,
                        wander_distance AS wanderDistance, movement_type AS movementType,
                        spawn_flags AS spawnFlags, patch_min AS patchMin, patch_max AS patchMax
                 FROM creature
                 WHERE map = @map AND {spawnFilter}
                 LIMIT {MaxSpawnRows}
                 """, args)).ToList();

            // ── per-guid movement + entry pool derived from the found spawns ──
            var movementGuids = new HashSet<uint>(guidList);
            var templateEntries = new HashSet<uint>(entryList);
            foreach (dynamic row in creatures)
            {
                movementGuids.Add((uint)row.guid);
                foreach (uint entry in new[]
                         { (uint)row.id, (uint)row.id2, (uint)row.id3, (uint)row.id4, (uint)row.id5 })
                    if (entry != 0) templateEntries.Add(entry);
            }

            var movement = movementGuids.Count == 0 ? [] : (await conn.QueryAsync(
                """
                SELECT id, point,
                       position_x AS positionX, position_y AS positionY, position_z AS positionZ,
                       orientation, waittime, wander_distance AS wanderDistance,
                       script_id AS scriptId, path_id AS pathId
                FROM creature_movement
                WHERE id IN @guids
                ORDER BY id, point
                """, new { guids = movementGuids })).ToList();

            var movementTemplates = templateEntries.Count == 0 ? [] : (await conn.QueryAsync(
                """
                SELECT entry, point,
                       position_x AS positionX, position_y AS positionY, position_z AS positionZ,
                       orientation, waittime, wander_distance AS wanderDistance,
                       script_id AS scriptId, path_id AS pathId
                FROM creature_movement_template
                WHERE entry IN @entries
                ORDER BY entry, path_id, point
                """, new { entries = templateEntries })).ToList();

            // Highest patch wins per entry (same rule as the client's full-table dump).
            var templates = new Dictionary<uint, object>();
            if (templateEntries.Count > 0)
                foreach (dynamic row in await conn.QueryAsync(
                    """
                    SELECT entry, patch, name,
                           level_min AS levelMin, level_max AS levelMax, faction,
                           detection_range AS detectionRange,
                           call_for_help_range AS callForHelpRange, leash_range AS leashRange,
                           movement_type AS movementType, flags_extra AS flagsExtra,
                           static_flags1 AS staticFlags
                    FROM creature_template
                    WHERE entry IN @entries
                    ORDER BY patch
                    """, new { entries = templateEntries }))
                    templates[(uint)row.entry] = row;

            return Json(new
            {
                fetchedUtc = DateTime.UtcNow,
                map,
                creatures,
                movement,
                movementTemplates,
                templates = templates.Values,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NpcDev snapshot failed (map {Map})", map);
            return StatusCode(500, ex.Message);
        }
    }

    /// <summary>
    /// POST /NpcDev/Apply — the dev window's change-set (schemaVersion + session + packets,
    /// the same document shape as dev-changes/*.json). Each packet is verified against the
    /// current DB (drift from its `before` blocks it as stale), applied to the mangos world
    /// DB, and audited under one batch (category "npc"). Returns per-packet verdicts. Does
    /// NOT make the change live — that is the client's separate reload.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Apply([FromBody] NpcApplyRequest? req)
    {
        if (req?.Packets is not { Count: > 0 })
            return BadRequest("no packets");
        try
        {
            NpcApplyResult result = await _apply.ApplyAsync(req, HttpContext.Connection.RemoteIpAddress?.ToString());
            return Json(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NpcDev apply failed");
            return StatusCode(500, ex.Message);
        }
    }

    /// <summary>
    /// GET /NpcDev/Diff?guid=G&amp;entry=E — is this spawn (its row + path) and/or its entry's
    /// template changed from the captured og_creature* baseline? `hasBaseline=false` when the
    /// owner hasn't run Baseline/Initialize with the creature tables yet.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Diff(uint guid, uint entry = 0)
    {
        if (guid == 0) return BadRequest("guid required");
        try
        {
            return Json(await _baseline.DiffAsync(guid, entry));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NpcDev diff failed (guid {Guid})", guid);
            return StatusCode(500, ex.Message);
        }
    }

    /// <summary>
    /// POST /NpcDev/Reset — restore each guid's spawn row + path, and each entry's template
    /// (detection_range), from the og_creature* baseline. Audited (category "npc"). Does NOT
    /// make it live — the client reloads afterwards. Returns per-target verdicts.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Reset([FromBody] NpcResetRequest? req)
    {
        if (req is null || (req.Guids.Count == 0 && req.Entries.Count == 0))
            return BadRequest("nothing to reset");
        try
        {
            return Json(await _baseline.ResetAsync(req, HttpContext.Connection.RemoteIpAddress?.ToString()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NpcDev reset failed");
            return StatusCode(500, ex.Message);
        }
    }

    private static List<uint> ParseIdList(string? csv, int cap)
    {
        var result = new List<uint>();
        if (string.IsNullOrWhiteSpace(csv)) return result;
        foreach (string part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (uint.TryParse(part, out uint id) && id != 0) result.Add(id);
            if (result.Count >= cap) break;
        }
        return result;
    }
}
