using Microsoft.AspNetCore.Mvc;
using System.Text.RegularExpressions;

namespace MangosSuperUI.Controllers;

// ============================================================================
//  Fleet View — fleet-wide error / anomaly correlation board
// ============================================================================
// Companion to the Individual Bot Monitor (Bots/Index). Where the Monitor is a
// single-bot drill-down, this is the bird's-eye triage board: it replays the
// in-memory log ring per bot and threads every error back to the COMMAND that
// preceded it, the POSITION the bot held when it fired, and the TARGET it was
// interacting with — the same buffer LiveLog/BotReport already read, scanned
// fleet-wide here.
//
// Self-contained against confirmed accessors only: _brain.AllBots (Guid/Name/
// Level), _log.Query(name, 0, int.MaxValue) -> (lines{Seq,Utc,Message}, lastSeq),
// and the existing EnrichCreatureNames() helper. No unseen C++/C# shapes.
public partial class BotsController
{
    // Renders Views/Bots/Fleet.cshtml. Page is pull-only (polls FleetDiagnostics).
    public IActionResult Fleet() => View();

    // ---- category table -----------------------------------------------------
    // key / label / severity tier / feed color / recognizer. FIRST match wins, so
    // order is most-specific-first: a MOVE_FAILED carrying reason=no_path must
    // bucket as no_path, never as the generic move_failed below it.
    private sealed record FleetCat(string Key, string Label, string Tier, string Color, Regex Rx);

    private static Regex Rc(string p) => new(p, RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly FleetCat[] _fleetCats = new[]
    {
        new FleetCat("stack_overflow", "host crash (Derive)", "error", "#ff5d62", Rc(@"QuestPlanner\.Derive")),
        new FleetCat("path_unsafe",    "path unsafe",         "error", "#f7768e", Rc(@"PATH_UNSAFE|path_unsafe")),
        new FleetCat("combat_stalemate","combat stalemate",   "error", "#f7768e", Rc(@"combat_stalemate")),
        new FleetCat("no_path",        "no path",             "error", "#f7768e", Rc(@"no_path|empty_path|PATHFIND_NOPATH")),
        new FleetCat("repair_fail",    "repair failed",       "error", "#f7768e", Rc(@"REPAIR_FAIL|not_enough_gold")),
        new FleetCat("move_failed",    "move failed",         "error", "#ff9e64", Rc(@"MOVE_FAILED")),
        new FleetCat("wedge",          "wedge / stuck",       "error", "#ff9e64", Rc(@"\bWEDGE\b|wedge-backoff")),
        new FleetCat("stall",          "stall",               "error", "#ff9e64", Rc(@"\bSTALL")),
        new FleetCat("npc_not_found",  "npc not found",       "error", "#ff9e64", Rc(@"npc_not_found")),
        new FleetCat("vendor_giveup",  "vendor give-up",      "warn",  "#e0af68", Rc(@"\[VENDOR\][^\n]*GIVEUP")),
        new FleetCat("train_giveup",   "train give-up",       "warn",  "#e0af68", Rc(@"\[TRAIN\][^\n]*GIVEUP")),
        new FleetCat("giveup",         "give-up",             "warn",  "#e0af68", Rc(@"GIVEUP")),
        new FleetCat("shelve",         "quest shelved / lock","warn",  "#e0af68", Rc(@"shelving \[|deferring|grind-lock")),
        new FleetCat("death",          "death",               "warn",  "#bb9af7", Rc(@"\bDEATH\b|\bDIED\b")),
        new FleetCat("trash_kill",     "trash kill (no xp)",  "info",  "#7aa2f7", Rc(@"trash kill")),
    };

    // ---- structured extractors ---------------------------------------------
    private static readonly Regex _fvPos = new(@"\(\s*(-?\d+(?:\.\d+)?)\s*,\s*(-?\d+(?:\.\d+)?)(?:\s*,\s*(-?\d+(?:\.\d+)?))?\s*\)", RegexOptions.Compiled);
    private static readonly Regex _fvMap = new(@"\bmap(?:Id)?\s*[=:]\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex _fvWhy = new(@"\bwhy=([A-Za-z0-9_\-:]+)", RegexOptions.Compiled);
    private static readonly Regex _fvReason = new(@"\breason=([A-Za-z0-9_\-]+)", RegexOptions.Compiled);
    private static readonly Regex _fvGoal = new(@"\bGoal=([A-Za-z]+)", RegexOptions.Compiled);
    private static readonly Regex _fvCmd = new(@"\b(MOVE_TO|MOVE_FAILED|SET_TASK(?:[ _]\w+)?|INTERACT_NPC|ATTACK_TARGET|QUEST_INTERACT|ABANDON_QUEST|LEARN_SPELL|TAKE_FLIGHT|GRAVEYARD_PORT|HEARTH|GRIND)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex _fvTarget = new(@"\b(?:creature_entry|c_entry|creature_guid|entry|guid)\s*[=:]\s*\d+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex _fvFleet = new(@"\bFLEET\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // A single correlated incident: the error line plus the context that led to it.
    private sealed class FleetIncident
    {
        public long Seq;
        public DateTime Utc;
        public int Guid;
        public string Name = "";
        public int Level;
        public string CatKey = "";
        public string Label = "";
        public string Tier = "";
        public string Color = "";
        public bool HasPos;
        public double X, Y, Z;
        public int Map = -1;
        public string Why = "";      // why= context active when the error fired
        public string PreCmd = "";   // the command/goal that preceded the error
        public string Target = "";   // creature/npc token at/near the error
        public string Msg = "";      // trimmed raw line (creature-enriched on the way out)
    }

    [HttpGet]
    public IActionResult FleetDiagnostics()
    {
        // Roster from the brain. Brain off / no bots => valid empty payload, not a 500.
        var roster = _brain.AllBots.Values
            .Where(b => !string.IsNullOrWhiteSpace(b.Name))
            .GroupBy(b => b.Name)
            .Select(g => g.First())
            .ToList();

        if (roster.Count == 0)
            return Json(new { ok = true, empty = true, reason = "No brain-active bots. Enable the engine on the Individual Bot Monitor, then run a session.", generatedUtc = DateTime.UtcNow });

        var incidents = new List<FleetIncident>();
        int attributedLines = 0, fleetLines = 0;
        DateTime minUtc = DateTime.MaxValue, maxUtc = DateTime.MinValue;

        foreach (var bot in roster)
        {
            // Per-bot slice is already attributed (whole-word name filter) AND ordered —
            // correlation is naturally per-bot, so look-back can't cross bots.
            var (raw, _) = _log.Query(bot.Name, 0, int.MaxValue);
            var slice = raw.ToList();
            if (slice.Count == 0) continue;

            // rolling context (what we know about this bot just before the current line)
            bool havePos = false; double lx = 0, ly = 0, lz = 0; int lmap = -1;
            string lWhy = "", lCmd = "", lTarget = "";

            foreach (var line in slice)
            {
                var msg = line.Message ?? "";
                if (msg.Length == 0) continue;

                // Fold out the fleet heartbeat (it names every bot, so it lands in every
                // slice). It is not a per-bot event and must not pollute context.
                if (_fvFleet.IsMatch(msg) || Regex.Matches(msg, "pick=").Count >= 2) { fleetLines++; continue; }

                attributedLines++;
                if (line.Utc < minUtc) minUtc = line.Utc;
                if (line.Utc > maxUtc) maxUtc = line.Utc;

                // --- this line's own structured fields ---
                bool tPos = false; double tx = 0, ty = 0, tz = 0;
                var mPos = _fvPos.Match(msg);
                if (mPos.Success
                    && double.TryParse(mPos.Groups[1].Value, out tx)
                    && double.TryParse(mPos.Groups[2].Value, out ty))
                {
                    tPos = true;
                    if (mPos.Groups[3].Success) double.TryParse(mPos.Groups[3].Value, out tz);
                }
                int tMap = -1;
                var mMap = _fvMap.Match(msg);
                if (mMap.Success) int.TryParse(mMap.Groups[1].Value, out tMap);

                string tWhy = _fvWhy.Match(msg) is { Success: true } w ? w.Groups[1].Value : "";
                string tCmd = "";
                var mCmd = _fvCmd.Match(msg);
                if (mCmd.Success) tCmd = mCmd.Groups[1].Value.ToUpperInvariant().Replace('_', '_');
                string tTarget = _fvTarget.Match(msg) is { Success: true } tg ? tg.Value : "";

                // --- classify (first match wins) ---
                FleetCat? cat = null;
                foreach (var c in _fleetCats) { if (c.Rx.IsMatch(msg)) { cat = c; break; } }

                if (cat != null)
                {
                    // Snapshot using context accumulated BEFORE this line, but prefer the
                    // error line's own coords/target when it carries them.
                    var inc = new FleetIncident
                    {
                        Seq = line.Seq,
                        Utc = line.Utc,
                        Guid = bot.Guid,
                        Name = bot.Name,
                        Level = bot.Level,
                        CatKey = cat.Key,
                        Label = cat.Label,
                        Tier = cat.Tier,
                        Color = cat.Color,
                        Why = !string.IsNullOrEmpty(tWhy) ? tWhy : lWhy,
                        PreCmd = lCmd,
                        Target = !string.IsNullOrEmpty(tTarget) ? tTarget : lTarget,
                        Map = tMap >= 0 ? tMap : lmap,
                    };
                    if (tPos) { inc.HasPos = true; inc.X = tx; inc.Y = ty; inc.Z = tz; }
                    else if (havePos) { inc.HasPos = true; inc.X = lx; inc.Y = ly; inc.Z = lz; }

                    // trim for the feed; full enrichment happens in one batch below
                    var trimmed = msg.Length > 240 ? msg.Substring(0, 240) : msg;
                    inc.Msg = trimmed;
                    incidents.Add(inc);
                }

                // --- advance rolling context (after snapshot) ---
                if (tPos) { havePos = true; lx = tx; ly = ty; lz = tz; }
                if (tMap >= 0) lmap = tMap;
                if (!string.IsNullOrEmpty(tWhy)) lWhy = tWhy;
                if (!string.IsNullOrEmpty(tCmd)) lCmd = tCmd;
                if (!string.IsNullOrEmpty(tTarget)) lTarget = tTarget;
            }
        }

        // ---- aggregate ----
        int errorTotal = incidents.Count(i => i.Tier != "info");
        int infoTotal = incidents.Count(i => i.Tier == "info");
        double windowSec = (maxUtc > minUtc) ? (maxUtc - minUtc).TotalSeconds : 0;
        double windowMin = Math.Max(1.0 / 60.0, windowSec / 60.0);

        var byCategory = incidents
            .GroupBy(i => i.CatKey)
            .Select(g => new
            {
                key = g.Key,
                label = g.First().Label,
                tier = g.First().Tier,
                color = g.First().Color,
                count = g.Count()
            })
            .OrderByDescending(x => x.count)
            .ToList();

        var byBot = incidents
            .GroupBy(i => i.Guid)
            .Select(g => new
            {
                guid = g.Key,
                name = g.First().Name,
                level = g.First().Level,
                count = g.Count(i => i.Tier != "info"),
                infoCount = g.Count(i => i.Tier == "info"),
                top = g.GroupBy(i => i.CatKey)
                       .OrderByDescending(x => x.Count())
                       .Take(3)
                       .Select(x => new { key = x.Key, color = x.First().Color, count = x.Count() })
                       .ToList()
            })
            .OrderByDescending(x => x.count)
            .ThenByDescending(x => x.infoCount)
            .Take(40)
            .ToList();

        // coordinate hotspots — round to a 100-yd cell per map; cell label is its center.
        var hotspots = incidents
            .Where(i => i.HasPos && i.Map >= 0 && i.Tier != "info")
            .GroupBy(i => new { i.Map, cx = (int)Math.Floor(i.X / 100.0), cy = (int)Math.Floor(i.Y / 100.0) })
            .Select(g => new
            {
                map = g.Key.Map,
                x = g.Key.cx * 100 + 50,
                y = g.Key.cy * 100 + 50,
                count = g.Count(),
                topCategory = g.GroupBy(i => i.CatKey).OrderByDescending(x => x.Count()).First().Key,
                color = g.GroupBy(i => i.CatKey).OrderByDescending(x => x.Count()).First().First().Color,
                bots = g.Select(i => i.Name).Distinct().Take(4).ToList()
            })
            .OrderByDescending(x => x.count)
            .Take(14)
            .ToList();

        // what preceded the error — why= first, else the command, else unknown.
        var byPreceding = incidents
            .Select(i => !string.IsNullOrEmpty(i.Why) ? "why=" + i.Why
                       : !string.IsNullOrEmpty(i.PreCmd) ? i.PreCmd
                       : "—")
            .GroupBy(s => s)
            .Select(g => new { label = g.Key, count = g.Count() })
            .OrderByDescending(x => x.count)
            .Take(12)
            .ToList();

        // recent feed — newest first, enrich creature tokens in one batch.
        var recentSrc = incidents.OrderByDescending(i => i.Seq).Take(180).ToList();
        var enrichedMsgs = EnrichCreatureNames(recentSrc.Select(i => i.Msg).ToList());
        var enrichedTargets = EnrichCreatureNames(recentSrc.Select(i => string.IsNullOrEmpty(i.Target) ? "" : i.Target).ToList());

        var recent = recentSrc.Select((i, idx) => new
        {
            seq = i.Seq,
            t = i.Utc,
            guid = i.Guid,
            name = i.Name,
            level = i.Level,
            category = i.CatKey,
            label = i.Label,
            tier = i.Tier,
            color = i.Color,
            hasPos = i.HasPos,
            map = i.Map,
            x = i.HasPos ? Math.Round(i.X, 1) : (double?)null,
            y = i.HasPos ? Math.Round(i.Y, 1) : (double?)null,
            why = i.Why,
            preCmd = i.PreCmd,
            target = enrichedTargets[idx],
            msg = enrichedMsgs[idx]
        }).ToList();

        return Json(new
        {
            ok = true,
            empty = false,
            generatedUtc = DateTime.UtcNow,
            windowSec = Math.Round(windowSec),
            attributedLines,
            fleetLines,
            botCount = roster.Count,
            errorTotal,
            infoTotal,
            errorsPerMin = Math.Round(errorTotal / windowMin, 1),
            byCategory,
            byBot,
            hotspots,
            byPreceding,
            recent
        });
    }

    // ---- in-service journald quantizers --------------------------------------
    // These run the embedded diagnostic scripts (bot_run_report.sh / bot_diag.sh)
    // from inside the service via BotDiagnosticsService, so the Quantized Report
    // can pull the full journald digest without anyone shelling out by hand. The
    // service is resolved per-request to keep the primary controller ctor untouched.

    [HttpGet]
    public async Task<IActionResult> QuantizedDigest(int? pid)
    {
        var diag = HttpContext.RequestServices.GetRequiredService<Services.BotDiagnosticsService>();
        if (!diag.RunReportAvailable)
            return Json(new { ok = false, available = false, error = "bot_run_report.sh is not embedded in the service yet (add it as an EmbeddedResource and rebuild)." });

        var r = await diag.RunFleetReportAsync(pid, HttpContext.RequestAborted);
        return Json(new
        {
            ok = r.Ok,
            available = true,
            exit = r.ExitCode,
            output = r.Stdout,
            error = r.Ok ? null : (r.Error ?? (string.IsNullOrEmpty(r.Stderr) ? "unknown error" : r.Stderr)),
            stderr = r.Stderr
        });
    }

    [HttpGet]
    public async Task<IActionResult> BotDiag(string name)
    {
        var diag = HttpContext.RequestServices.GetRequiredService<Services.BotDiagnosticsService>();
        if (!diag.BotDiagAvailable)
            return Json(new { ok = false, available = false, error = "bot_diag.sh is not embedded in the service yet (add it as an EmbeddedResource and rebuild)." });

        var r = await diag.RunBotDiagAsync(name ?? "", HttpContext.RequestAborted);
        return Json(new
        {
            ok = r.Ok,
            available = true,
            exit = r.ExitCode,
            output = r.Stdout,
            error = r.Ok ? null : (r.Error ?? (string.IsNullOrEmpty(r.Stderr) ? "unknown error" : r.Stderr)),
            stderr = r.Stderr
        });
    }
}
