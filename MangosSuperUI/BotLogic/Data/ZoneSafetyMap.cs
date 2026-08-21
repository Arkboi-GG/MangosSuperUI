using Dapper;
using MangosSuperUI.Models;
using MangosSuperUI.Services;
using Microsoft.Extensions.Logging;
using System.Numerics;
using System.Reflection;

namespace MangosSuperUI.BotLogic.Data;

// =============================================================================
// Zone Safety Map — Precomputed creature-level grid for path safety checks
//
// At startup, loads all creature spawns + levels from the mangos DB and builds
// a spatial grid: each cell is CELL_SIZE×CELL_SIZE yards and stores the average
// and max creature level of all spawns in that cell.
//
// Used by QuestingDomain.SelectQuest to hard-reject quests whose travel paths
// cross through zones with creatures significantly above the bot's level.
//
// This is THE fix for the Session 13 death loop: bots walking through Redridge
// at level 2 because the quest giver is far away and no hard safety gate existed.
// =============================================================================

/// <summary>Which player team a danger grid is computed FOR. A creature belongs in a team's grid
/// only if it is hostile to that team (FINDING_002).</summary>
public enum Team { Alliance = 0, Horde = 1 }

public class ZoneSafetyMap
{
    private readonly ConnectionFactory _db;
    private readonly ILogger<ZoneSafetyMap> _logger;

    // Grid resolution: 100×100 yard cells. Vanilla maps are ~17,000 yards across.
    // That's ~170×170 = ~29,000 cells per map — trivial memory.
    // Public so callers (FindSafeRezSpot) can step their sampling at grid granularity:
    // sampling finer than CELL_SIZE just re-reads the same cell.
    public const float CELL_SIZE = 100f;

    // WoW coordinate space: roughly -17066 to 17066 on each axis.
    // We offset by COORD_OFFSET to make all indices positive.
    private const float COORD_OFFSET = 17100f;
    private const int GRID_DIM = (int)((COORD_OFFSET * 2) / CELL_SIZE) + 1; // ~343

    // Per-TEAM, per-map grid. Each cell stores (avgLevel, maxLevel, spawnCount) of the spawns that
    // are HOSTILE to that team. Indexed by (int)Team. (FINDING_002: the old single global grid used a
    // hardcoded Alliance reactance mask, so Horde-faction city guards — hostile to Alliance, friendly
    // to Horde — read as danger for Horde bots routing to their own towns. A mixed-faction fleet needs
    // one grid per team.) Only maps with spawns get an entry within each team's dictionary.
    private readonly Dictionary<int, CellData[,]>[] _gridsByTeam =
        { new Dictionary<int, CellData[,]>(), new Dictionary<int, CellData[,]>() };

    private Dictionary<int, CellData[,]> TeamGrid(Team team) => _gridsByTeam[(int)team];

    // Per-TEAM guard cells: the (ix,iy) cells holding a creature flagged CREATURE_FLAG_EXTRA_GUARD that is
    // HOSTILE to that team (FINDING_005). City guards (Menethil/Stormwind/Orgrimmar/…) mutually social-
    // assist and respawn infinitely, so an enemy bot that grinds one triggers an unwinnable town-wide
    // chain-pull (an L18 strayed into Menethil pulls L47 guards → 100-attacker set → 1%-HP grind-lock).
    // The danger grid already carries their (high) LEVEL; this is the level-INDEPENDENT "never grind here"
    // signal GrindPlanner consults so a strayed bot bails instead of pinning on the garrison — the C#
    // complement to the C++ SelectGrindTarget IsGuard() exclusion. Bucketed exactly like the danger grid:
    // a guard only enters the set of the team it is hostile to (hostile_mask), so it is an "enemy" guard
    // for that team only. Only maps with guard spawns get an entry within each team's dictionary.
    private readonly Dictionary<int, HashSet<(int ix, int iy)>>[] _guardCellsByTeam =
        { new Dictionary<int, HashSet<(int, int)>>(), new Dictionary<int, HashSet<(int, int)>>() };

    private const int CREATURE_FLAG_EXTRA_GUARD = 0x00000400;  // creature_template.flags_extra guard bit (1024)

    /// <summary>Map a bot's faction string ("Horde"/"Alliance"/…) to its danger-grid team.
    /// Unknown/null defaults to Alliance — the pre-FINDING_002 behaviour, so an unclassified bot is
    /// no worse off than before the split.</summary>
    public static Team TeamFromFaction(string? faction) =>
        string.Equals(faction, "Horde", StringComparison.OrdinalIgnoreCase) ? Team.Horde : Team.Alliance;

    // entry → (max level, is-critter). For "real kill" gating: the KILL event carries only the entry,
    // and without this a CHICKEN kill (critter, 0 XP) counts as progress and resets every stall net.
    private readonly Dictionary<int, (int MaxLevel, bool Critter)> _byEntry = new();
    private const int CREATURE_TYPE_CRITTER = 8;   // VMaNGOS creature_template.type

    private bool _loaded;
    public bool IsLoaded => _loaded;

    public ZoneSafetyMap(ConnectionFactory db, ILogger<ZoneSafetyMap> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Get the max creature level in the cell containing the given world position.
    /// Returns 0 if no creatures are known in that cell.
    /// </summary>
    public int GetMaxCreatureLevel(int mapId, float x, float y, Team team)
    {
        if (!TeamGrid(team).TryGetValue(mapId, out var grid)) return 0;
        var (ix, iy) = WorldToGrid(x, y);
        if (ix < 0 || ix >= GRID_DIM || iy < 0 || iy >= GRID_DIM) return 0;
        return grid[ix, iy].MaxLevel;
    }

    /// <summary>
    /// Get the average creature level in the cell containing the given world position.
    /// Returns 0 if no creatures are known in that cell.
    /// </summary>
    public float GetAvgCreatureLevel(int mapId, float x, float y, Team team)
    {
        if (!TeamGrid(team).TryGetValue(mapId, out var grid)) return 0;
        var (ix, iy) = WorldToGrid(x, y);
        if (ix < 0 || ix >= GRID_DIM || iy < 0 || iy >= GRID_DIM) return 0;
        return grid[ix, iy].AvgLevel;
    }

    /// <summary>
    /// Get the number of known creature spawns in the cell containing the given world
    /// position. Returns 0 if no creatures are known in that cell (or no grid for the map).
    /// This is spawn DENSITY: a high count means a pack/field, even when every mob in it is
    /// trivially low level. A level-only metric is blind to death-by-dogpile; this is not.
    /// </summary>
    public int GetSpawnCount(int mapId, float x, float y, Team team)
    {
        if (!TeamGrid(team).TryGetValue(mapId, out var grid)) return 0;
        var (ix, iy) = WorldToGrid(x, y);
        if (ix < 0 || ix >= GRID_DIM || iy < 0 || iy >= GRID_DIM) return 0;
        return grid[ix, iy].SpawnCount;
    }

    /// <summary>
    /// True if the cell containing (x,y) holds a CITY GUARD hostile to <paramref name="team"/> — a place an
    /// enemy bot must never FILLER-grind (unwinnable town-wide social-assist chain-pull, FINDING_005). This
    /// is level-INDEPENDENT, unlike GetMaxCreatureLevel: a guard town stays off-limits even to a bot that
    /// out-levels the guards. Returns false when no grid/guard data exists for the map (fail-open).
    /// </summary>
    public bool IsEnemyGuardCell(int mapId, float x, float y, Team team)
    {
        if (!_guardCellsByTeam[(int)team].TryGetValue(mapId, out var cells)) return false;
        var (ix, iy) = WorldToGrid(x, y);
        return cells.Contains((ix, iy));
    }

    /// <summary>
    /// Find the nearest cell worth GRINDING for level-appropriate XP. Box-scans cells within
    /// maxRadiusYards of (botX,botY) and keeps the best-scoring one that is: populated but not a
    /// dogpile, average level in [L-lowOffset, L+highOffset] (XP, not grey, not a wall), max level
    /// <= L+dangerCeil (no red mob lurking), and not caller-vetoed. Returns null if none qualify.
    ///
    /// Cells already exclude non-hostile NPCs (2026-07-03: a faction-reactance filter — a spawn is in the
    /// grid only if its faction is hostile to a player, so guards / town NPCs / spirit healers / critters are
    /// all gone), so a populated cell is a genuine aggressive pack -- this is why steering here fixes the
    /// "kill-anything finds a chicken in a farmyard" spin (chickens are non-hostile -> never in a cell).
    /// </summary>
    /// <summary>
    /// Is killing this creature REAL progress for a bot of the given level? False for critters
    /// (chickens/rabbits — 0 XP) and clearly-grey mobs (maxLevel <= botLevel - greyBand). Unknown
    /// entries default TRUE (don't punish what we can't classify). Drives the C# progress/stall nets
    /// only — NOT quest completion (that stays server-authoritative via TASK_COMPLETE).
    /// </summary>
    public bool IsRealKill(int entry, int botLevel, int greyBand = 7)
    {
        if (entry <= 0) return true;
        if (!_byEntry.TryGetValue(entry, out var c)) return true;
        if (c.Critter) return false;
        if (c.MaxLevel <= Math.Max(1, botLevel - greyBand)) return false;
        return true;
    }

    public GrindCell? FindGrindCell(
        int mapId, float botX, float botY, int botLevel, float maxRadiusYards, Team team,
        int lowOffset = 5, int highOffset = 2, int dangerCeil = 3,
        int minSpawn = 1, int maxSpawn = 40,
        Func<float, float, bool>? reject = null)
    {
        if (!TeamGrid(team).TryGetValue(mapId, out var grid)) return null;

        int loLvl = Math.Max(1, botLevel - lowOffset);
        int hiLvl = botLevel + highOffset;
        int ceilLvl = botLevel + dangerCeil;
        float maxRadSq = maxRadiusYards * maxRadiusYards;

        var (cx, cy) = WorldToGrid(botX, botY);
        int reach = Math.Max(1, (int)(maxRadiusYards / CELL_SIZE) + 1);

        GrindCell? best = null;
        float bestScore = float.MinValue;

        for (int ix = Math.Max(0, cx - reach); ix <= Math.Min(GRID_DIM - 1, cx + reach); ix++)
            for (int iy = Math.Max(0, cy - reach); iy <= Math.Min(GRID_DIM - 1, cy + reach); iy++)
            {
                var cell = grid[ix, iy];
                if (cell.SpawnCount < minSpawn || cell.SpawnCount > maxSpawn) continue;
                if (cell.MaxLevel > ceilLvl) continue;                          // a red mob lives here
                if (cell.AvgLevel < loLvl || cell.AvgLevel > hiLvl) continue;   // grey or too hot on average

                float wx = ix * CELL_SIZE - COORD_OFFSET + CELL_SIZE * 0.5f;
                float wy = iy * CELL_SIZE - COORD_OFFSET + CELL_SIZE * 0.5f;
                float dsq = (wx - botX) * (wx - botX) + (wy - botY) * (wy - botY);
                if (dsq > maxRadSq) continue;
                if (reject != null && reject(wx, wy)) continue;

                float dist = MathF.Sqrt(dsq);
                float levelFit = 1f - MathF.Abs(cell.AvgLevel - botLevel) / 10f;        // 1.0 on-level
                float score = -dist + levelFit * 60f + MathF.Min(cell.SpawnCount, 8) * 3f; // nearest, on-level, decent density
                if (score > bestScore)
                {
                    bestScore = score;
                    best = new GrindCell(wx, wy, cell.AvgLevel, cell.MaxLevel, cell.SpawnCount, dist);
                }
            }

        return best;
    }

    // ── Fleet-wide no-path destination memory (FINDING_017 follow-up) ──────────
    // When ONE bot proves a destination unpathable (an honest MOVE_FAILED
    // no_path/empty_path from the core), every other bot can skip that pocket
    // for a while instead of re-proving it. TTL'd (terrain doesn't change, but
    // fixes/mmap reloads do). In-memory only — a restart forgets, fine: re-proving costs seconds.
    // [FINDING_019] The 017 follow-up over-blacklisted and froze ~66% of the fleet from leveling.
    // Two fixes here: (1) cell 50yd->12yd — one objective that's only ~5-15yd off-mesh no longer
    // blacklists a 2500yd^2 block full of REACHABLE neighbors; (2) record-ONCE, not refresh (see
    // RecordNoPathDest) so the 90-min TTL actually elapses and the fleet re-proves. The old
    // unconditional refresh kept hot cells alive forever under fleet load (many bots re-hitting the
    // same dest thousands of times) -> the reachable quest set emptied -> frozen-questing livelock.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(int Map, int Cx, int Cy), DateTime> _noPathDests = new();
    private const float NOPATH_CELL_YARDS = 12f;
    private const int NOPATH_TTL_MINUTES = 90;
    private const int NOPATH_CAP = 4096;

    public void RecordNoPathDest(int mapId, float x, float y)
    {
        if (_noPathDests.Count >= NOPATH_CAP)
        {
            var now = DateTime.UtcNow;
            foreach (var kv in _noPathDests)
                if (kv.Value < now)
                    _noPathDests.TryRemove(kv.Key, out _);
            if (_noPathDests.Count >= NOPATH_CAP) return;   // full of live entries — drop the record
        }
        var key = (mapId, (int)MathF.Round(x / NOPATH_CELL_YARDS), (int)MathF.Round(y / NOPATH_CELL_YARDS));
        // [FINDING_019] Record ONCE. Keep the ORIGINAL expiry while the entry is still live — do NOT
        // refresh on every re-failure (that unconditional write made the TTL never elapse under fleet
        // load). Only arm a fresh 90-min window when the key is new or has already expired, so a
        // genuinely-bad pocket is periodically re-proven and a transiently-bad one decays out.
        var expiry = DateTime.UtcNow.AddMinutes(NOPATH_TTL_MINUTES);
        _noPathDests.AddOrUpdate(key, expiry, (_, existing) => existing > DateTime.UtcNow ? existing : expiry);
    }

    public bool IsNoPathDest(int mapId, float x, float y)
    {
        var key = (mapId, (int)MathF.Round(x / NOPATH_CELL_YARDS), (int)MathF.Round(y / NOPATH_CELL_YARDS));
        if (!_noPathDests.TryGetValue(key, out var until)) return false;
        if (DateTime.UtcNow >= until)
        {
            _noPathDests.TryRemove(key, out _);
            return false;
        }
        return true;
    }

    /// <summary>
    /// Check if a straight-line path from (x1,y1) to (x2,y2) on the given map
    /// crosses any cells with max creature level above the given threshold.
    /// Samples every CELL_SIZE/2 yards along the path (ensures we don't skip cells).
    ///
    /// Returns the highest creature level encountered on the path, or 0 if safe.
    /// </summary>
    public int GetMaxCreatureLevelOnPath(int mapId, float x1, float y1, float x2, float y2, Team team)
    {
        if (!TeamGrid(team).TryGetValue(mapId, out var grid)) return 0;

        float dx = x2 - x1;
        float dy = y2 - y1;
        float dist = MathF.Sqrt(dx * dx + dy * dy);

        if (dist < 1f) return GetMaxCreatureLevel(mapId, x1, y1, team);

        // Sample every half-cell to avoid skipping thin cells
        float step = CELL_SIZE * 0.5f;
        int samples = Math.Max(2, (int)(dist / step) + 1);

        int maxLevel = 0;

        for (int i = 0; i <= samples; i++)
        {
            float t = (float)i / samples;
            float sx = x1 + dx * t;
            float sy = y1 + dy * t;

            var (ix, iy) = WorldToGrid(sx, sy);
            if (ix < 0 || ix >= GRID_DIM || iy < 0 || iy >= GRID_DIM) continue;

            int cellMax = grid[ix, iy].MaxLevel;
            if (cellMax > maxLevel)
                maxLevel = cellMax;
        }

        return maxLevel;
    }

    /// <summary>
    /// Cluster-aware corridor threat (2026-07-04, rounds 4/5). Walks the same half-cell-sampled
    /// straight line as GetMaxCreatureLevelOnPath, but instead of returning only the corridor's
    /// single highest level, it counts — per DEDUPED cell — how many hostile spawns actually
    /// exceed the caller's threshold. This is what makes the group path gate DYNAMIC in the
    /// sense Nico asked for: mobs patrol and a bot can path AROUND one high-level mob, so a lone
    /// over-band rare (Thuros Lightfingers L11 vetoing the whole kobold-cave corridor for a
    /// weakest-under-8 group, round 4) must read differently from an over-band CAMP. The caller
    /// applies the rule; this just reports the shape of the threat:
    ///   MaxLevel     — corridor max (legacy semantics; feeds the defer-level math unchanged)
    ///   OverCount    — total spawns with level &gt; threshold across the corridor
    ///   MaxCellOver  — the worst single cell's over-threshold count (the camp detector)
    ///   MaxCellDeep  — the worst single cell's count of spawns &gt; threshold+2 (deep reds)
    /// Cells are deduped via a visited set (half-cell sampling re-hits each cell ~2×; counting
    /// a camp twice would double its apparent size).
    /// </summary>
    public PathThreat GetPathThreat(int mapId, float x1, float y1, float x2, float y2, int thresholdLevel, Team team)
    {
        if (!TeamGrid(team).TryGetValue(mapId, out var grid)) return default;

        float dx = x2 - x1;
        float dy = y2 - y1;
        float dist = MathF.Sqrt(dx * dx + dy * dy);
        float step = CELL_SIZE * 0.5f;
        int samples = Math.Max(2, (int)(dist / step) + 1);

        int maxLevel = 0, overCount = 0, maxCellOver = 0, maxCellDeep = 0;
        var visited = new HashSet<(int, int)>();

        for (int i = 0; i <= samples; i++)
        {
            float t = (float)i / samples;
            var (ix, iy) = WorldToGrid(x1 + dx * t, y1 + dy * t);
            if (ix < 0 || ix >= GRID_DIM || iy < 0 || iy >= GRID_DIM) continue;
            if (!visited.Add((ix, iy))) continue;

            var cell = grid[ix, iy];
            if (cell.SpawnCount == 0 || cell.LevelCounts == null) continue;
            if (cell.MaxLevel > maxLevel) maxLevel = cell.MaxLevel;

            int cellOver = 0, cellDeep = 0;
            for (int lvl = Math.Clamp(thresholdLevel + 1, 1, 63); lvl <= 63; lvl++)
            {
                int n = cell.LevelCounts[lvl];
                if (n == 0) continue;
                cellOver += n;
                if (lvl > thresholdLevel + 2) cellDeep += n;
            }

            overCount += cellOver;
            if (cellOver > maxCellOver) maxCellOver = cellOver;
            if (cellDeep > maxCellDeep) maxCellDeep = cellDeep;
        }

        return new PathThreat(maxLevel, overCount, maxCellOver, maxCellDeep);
    }

    /// <summary>
    /// Check if a full quest travel path is safe for a bot of the given level.
    /// Samples the path from bot → giver → objective → turnin.
    /// Returns (isSafe, highestCreatureLevel, dangerousLegDescription).
    ///
    /// A path is "safe" if no sampled cell has max creature level > botLevel + safetyMargin.
    /// </summary>
    public (bool isSafe, int highestLevel, string dangerLeg) IsQuestPathSafe(
        int mapId, int botLevel, int safetyMargin,
        float botX, float botY,
        float? giverX, float? giverY,
        float? objX, float? objY,
        float? turnInX, float? turnInY,
        Team team)
    {
        int threshold = botLevel + safetyMargin;
        int highestLevel = 0;
        string dangerLeg = "";

        // Leg 1: bot → giver
        if (giverX.HasValue && giverY.HasValue)
        {
            int legMax = GetMaxCreatureLevelOnPath(mapId, botX, botY, giverX.Value, giverY.Value, team);
            if (legMax > highestLevel) { highestLevel = legMax; dangerLeg = "bot→giver"; }
        }

        // Leg 2: giver → objective
        if (giverX.HasValue && giverY.HasValue && objX.HasValue && objY.HasValue)
        {
            int legMax = GetMaxCreatureLevelOnPath(mapId, giverX.Value, giverY.Value, objX.Value, objY.Value, team);
            if (legMax > highestLevel) { highestLevel = legMax; dangerLeg = "giver→objective"; }
        }

        // Leg 3: objective → turnin (or giver → turnin if no objective)
        float fromX = objX ?? giverX ?? botX;
        float fromY = objY ?? giverY ?? botY;
        if (turnInX.HasValue && turnInY.HasValue)
        {
            int legMax = GetMaxCreatureLevelOnPath(mapId, fromX, fromY, turnInX.Value, turnInY.Value, team);
            if (legMax > highestLevel) { highestLevel = legMax; dangerLeg = "obj→turnin"; }
        }

        return (highestLevel <= threshold, highestLevel, dangerLeg);
    }

    /// <summary>
    /// Hard distance cap by bot level, with optional zone-aware expansion.
    ///
    /// The base level-only cap prevents low-level bots from crossing entire
    /// continents. The zone-aware overload recognizes that once a bot has
    /// graduated from a starter zone (e.g., Northshire zone 9) into a proper
    /// leveling zone (e.g., Elwynn Forest zone 12), it needs access to the
    /// ENTIRE zone plus adjacent cities (Stormwind) for trainers, quests, and
    /// vendors. Without this, a level 6 bot at Maclure Vineyards can't pick
    /// up quests from Goldshire because the grind center is 830yd away and
    /// the level-based cap is only 800yd.
    ///
    /// Starter sub-zones (keep tight radius):
    ///   9=Northshire, 132=Coldridge, 188=Shadowglen,
    ///   363=Valley of Trials, 154=Deathknell, 220=Red Cloud Mesa
    ///
    /// Leveling zones (full zone + capital city access once level 5+):
    ///   12=Elwynn, 1=Dun Morogh, 14=Durotar, 85=Tirisfal,
    ///   141=Teldrassil, 215=Mulgore
    /// </summary>
    /// <summary>
    /// Stuck-aware travel reach. <paramref name="escalationTier"/> widens the radius
    /// in steps when a bot has been unable to find ANY pickable quest for a while
    /// (QuestingDomain time-gates the tier off the no-quest streak). Tier 0 == baseline,
    /// byte-identical to every existing caller. This loosens the SOFT distance guardrail
    /// only; the HARD guardrail (creature-level path safety: C++ IsPathSafe + the C#
    /// PathBlacklist) is independent and is NOT affected here. The ceiling is level-bounded
    /// so escalation can never reach continent-spanning.
    /// </summary>
    public static float GetMaxTravelDistance(int botLevel, int zoneId = 0, int escalationTier = 0)
    {
        float baseCap = ComputeBaseTravelDistance(botLevel, zoneId);
        if (escalationTier <= 0)
            return baseCap;

        // Level-bounded ceiling — escalation widens reach but never past what a bot of
        // this level should be ranging. Kept TIGHT at low levels: a level 2-3 bot must
        // stay in its starter valley / the road to the first town, never range the whole
        // zone (unbounded low-level reach produced the level-2 no_path thrash across east
        // Elwynn on June 13).
        float ceiling = botLevel switch
        {
            <= 3 => 900f,    // starter sub-zone + road to first town (Northshire -> Goldshire), no further
            <= 6 => 1600f,   // first town + immediate surroundings
            <= 10 => 3000f,   // full starter leveling zone + capital
            <= 15 => 3600f,   // 2026-07-06 drift tighten: base(3000) + one 900yd hub hop. The old
                              // shared 6000 ceiling admitted CROSS-CLUSTER givers -- EK is vertically
                              // compact, so straight-line caps can't tell Redridge (right) from
                              // Kharanos (wrong) except by magnitude: from Goldshire, Sentinel Hill
                              // ~1,530 / Darkshire ~1,600 / Lakeshire ~2,230 / Kharanos ~3,900yd.
                              // 3,600 keeps every legit intra-cluster hop (dwarf side too:
                              // Kharanos->Thelsamar ~2,500) and excludes the trans-Steppes hops that
                              // put L11-13s on dwarf content 4,000-6,000yd from home.
            <= 20 => 6000f,
            _ => 15000f
        };

        // ~900yd per tier — one "hub hop" (Northshire -> Goldshire is ~900yd).
        float widened = baseCap + escalationTier * 900f;
        return MathF.Min(widened, ceiling);
    }

    private static float ComputeBaseTravelDistance(int botLevel, int zoneId)
    {
        // Starter sub-zones: always use tight radius regardless of level.
        // These are small areas where bots should finish the intro chain
        // before venturing out.
        bool isStarterZone = zoneId is 9 or 132 or 188 or 363 or 154 or 220;

        if (isStarterZone || zoneId == 0)
        {
            // Original level-based caps (used for starter zones, vendoring
            // without zone context, and any caller that doesn't pass zoneId)
            return botLevel switch
            {
                <= 3 => 400f,
                <= 6 => 800f,
                <= 10 => 1500f,
                <= 15 => 2500f,
                <= 20 => 4000f,
                <= 30 => 6000f,
                _ => 15000f
            };
        }

        // Leveling zones: sub-5 bots stay tight (still finishing the intro chain near
        // spawn). Once level 5+ the bot gets full zone access — a leveling zone is ~2k
        // across (Elwynn ~2k), so 2200yd covers it end-to-end with no spill into the
        // higher-level adjacent zones.
        if (botLevel < 5)
            return 800f;

        // Full leveling zone access tiers
        return botLevel switch
        {
            <= 10 => 2200f,   // full leveling zone end-to-end (Elwynn ~2k); no capital spillover
            <= 15 => 3000f,   // zone + ADJACENT-CLUSTER zones only (2026-07-06 drift tighten, was
                              // 4000). 4000 at tier 0 already admitted Kharanos (~3,900 straight-line
                              // from Goldshire -- EK's clusters are closer as the crow flies than they
                              // are on foot) with ZERO escalation, which is how L11-13 Alliance bots
                              // ended up questing dwarf content: level-legal (red/grey both pass, Dun
                              // Morogh is an L1-10 zone), in reach, no proactive route check. Every
                              // legit human-cluster hop measures <=~2,300 (Sentinel Hill 1,530 /
                              // Darkshire 1,600 / Lakeshire 2,230); 3,000 covers them with margin and
                              // excludes the cross-cluster admits. Escalation (ReachTier) still widens
                              // to the 3,600 ceiling when the local hubs drain.
            <= 20 => 5000f,
            <= 30 => 6000f,
            _ => 15000f
        };
    }

    // ── Startup Load ──────────────────────────────────────────────────────

    /// <summary>
    /// Load creature spawns + levels from the mangos DB and build the spatial grid.
    /// Call once at startup (after quest graph loader or in parallel).
    /// </summary>
    public async Task LoadAsync()
    {
        _logger.LogInformation("ZoneSafetyMap: loading creature level grid from mangos DB...");
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            using var conn = _db.Mangos();

            // Load all creature spawns with their template levels.
            // We use MinLevel/MaxLevel from creature_template — take the average for the cell.
            // Filter to Eastern Kingdoms (0) and Kalimdor (1) — no need for instances/BGs.
            var rows = await conn.QueryAsync<dynamic>(@"
                SELECT
                    c.map,
                    c.position_x,
                    c.position_y,
                    ct.level_min AS MinLevel,
                    ct.level_max AS MaxLevel,
                    ct.flags_extra AS FlagsExtra,
                    ft.hostile_mask AS HostileMask
                FROM creature c
                JOIN creature_template ct ON ct.entry = c.id AND ct.patch = 0
                JOIN faction_template ft ON ft.id = ct.faction AND ft.build = 4222
                WHERE c.map IN (0, 1)
                  AND ct.level_min > 0
                  AND ct.level_max > 0
                  AND ct.level_max <= 63
                  AND (ft.hostile_mask & 7) <> 0");
            // FACTION REACTANCE FILTER (2026-07-03), now PER-TEAM (FINDING_002, 2026-08-06).
            // A spawn contributes to a team's DANGER grid only if its faction is actually HOSTILE to that
            // team. (This replaced an older npc_flags/flags_extra exclusion that LEAKED: friendly Goldshire
            // guards carry NO_AGGRO_ON_SIGHT but not NO_AGGRO, so a level-55 guard still landed in the grid
            // and vetoed every corridor through town as path_unsafe — the 2026-07-03 grind-lock cascade.)
            //
            // Confirmed against THIS DB at build 4222 (all 204 creature factions on the fork resolve, no
            // fallback): player faction-templates use our_mask bit 1 = "all players", bit 2 = Alliance,
            // bit 4 = Horde. A creature is hostile to Alliance iff hostile_mask & 3, to Horde iff
            // hostile_mask & 5. Verified: Garrick (ft 17, hm 1) → in BOTH; Goldshire/Stormwind guard
            // (hm 12 = Horde-hostile) → Horde grid only; an Orgrimmar guard (Alliance-hostile) → Alliance
            // grid only; town service NPC (hm 4) / spirit healer (hm 0) → neither.
            //
            // The OLD code applied only `& 3` (Alliance) and built ONE global grid. That was correct while
            // the fleet was Alliance-only, but the fleet is now MIXED: a Horde bot read Orgrimmar's L55-60
            // guards (Alliance-hostile → they passed `& 3`) as danger and stray-flagged every trip to its
            // own trainer/vendor. We now bucket each spawn into whichever team grid(s) it threatens, and
            // callers query with the bot's own team. The `& 7` WHERE pre-filters to any-player-hostile
            // spawns so purely-friendly NPCs never reach either accumulator.

            // Accumulate per-cell, PER TEAM. Alliance-danger = hostile_mask & 3, Horde-danger = & 5
            // (see the reactance comment above). A mob hostile to all players lands in both; a faction
            // guard lands only in the ENEMY team's grid.
            var accumByTeam = new[]
            {
                new Dictionary<int, Dictionary<(int ix, int iy), CellAccum>>(),  // [Team.Alliance]
                new Dictionary<int, Dictionary<(int ix, int iy), CellAccum>>(),  // [Team.Horde]
            };

            static void AddSpawn(Dictionary<int, Dictionary<(int ix, int iy), CellAccum>> accum,
                                 int mapId, int ix, int iy, float avgLvl, int maxLvl)
            {
                if (!accum.TryGetValue(mapId, out var mapAccum))
                {
                    mapAccum = new Dictionary<(int ix, int iy), CellAccum>();
                    accum[mapId] = mapAccum;
                }
                if (!mapAccum.TryGetValue((ix, iy), out var cell))
                {
                    cell = new CellAccum();
                    mapAccum[(ix, iy)] = cell;
                }
                cell.TotalLevel += avgLvl;
                cell.Count++;
                if (maxLvl > cell.MaxLevel) cell.MaxLevel = maxLvl;
                cell.Levels[Math.Clamp(maxLvl, 1, 63)]++;   // threat histogram keys the spawn's MAX level (danger is worst-case)
            }

            // Record a guard spawn's cell into the enemy team's guard set (FINDING_005). Same team
            // bucketing as the danger grid: a guard is hostile to exactly one player team, so it is an
            // "enemy guard" for that team only.
            static void AddGuardCell(Dictionary<int, HashSet<(int ix, int iy)>> set, int mapId, int ix, int iy)
            {
                if (!set.TryGetValue(mapId, out var cells))
                {
                    cells = new HashSet<(int, int)>();
                    set[mapId] = cells;
                }
                cells.Add((ix, iy));
            }

            int spawnRows = 0, allianceContribs = 0, hordeContribs = 0;
            foreach (var r in rows)
            {
                int mapId = (int)r.map;
                float x = (float)r.position_x;
                float y = (float)r.position_y;
                int minLvl = (int)r.MinLevel;
                int maxLvl = (int)r.MaxLevel;
                int hostileMask = (int)r.HostileMask;
                long flagsExtra = Convert.ToInt64(r.FlagsExtra);   // may be a wide/unsigned column; widen before masking
                bool isGuard = (flagsExtra & CREATURE_FLAG_EXTRA_GUARD) != 0;
                float avgLvl = (minLvl + maxLvl) / 2f;

                var (ix, iy) = WorldToGrid(x, y);
                if (ix < 0 || ix >= GRID_DIM || iy < 0 || iy >= GRID_DIM) continue;

                if ((hostileMask & 3) != 0) { AddSpawn(accumByTeam[(int)Team.Alliance], mapId, ix, iy, avgLvl, maxLvl); allianceContribs++; }
                if ((hostileMask & 5) != 0) { AddSpawn(accumByTeam[(int)Team.Horde],    mapId, ix, iy, avgLvl, maxLvl); hordeContribs++; }
                if (isGuard)   // guard-flagged → mark the ENEMY team's guard set (same bucketing as the grid)
                {
                    if ((hostileMask & 3) != 0) AddGuardCell(_guardCellsByTeam[(int)Team.Alliance], mapId, ix, iy);
                    if ((hostileMask & 5) != 0) AddGuardCell(_guardCellsByTeam[(int)Team.Horde],    mapId, ix, iy);
                }
                spawnRows++;
            }

            // Build grids per team
            for (int t = 0; t < accumByTeam.Length; t++)
            {
                foreach (var (mapId, mapAccum) in accumByTeam[t])
                {
                    var grid = new CellData[GRID_DIM, GRID_DIM];
                    foreach (var ((ix, iy), cell) in mapAccum)
                    {
                        grid[ix, iy] = new CellData
                        {
                            AvgLevel = cell.TotalLevel / cell.Count,
                            MaxLevel = cell.MaxLevel,
                            SpawnCount = cell.Count,
                            LevelCounts = cell.Levels
                        };
                    }
                    _gridsByTeam[t][mapId] = grid;
                }
            }

            // Creature classifier — ALL creatures by entry (includes critters/no-aggro, unlike the grid
            // query above) so a trash kill can be RECOGNIZED. Same DB/patch as the grid.
            var ctRows = await conn.QueryAsync<dynamic>(@"
                SELECT entry, level_max AS MaxLevel, type AS CType
                FROM creature_template
                WHERE patch = 0 AND level_max > 0");
            foreach (var r in ctRows)
            {
                int entry = Convert.ToInt32(r.entry);
                _byEntry[entry] = (Convert.ToInt32(r.MaxLevel), Convert.ToInt32(r.CType) == CREATURE_TYPE_CRITTER);
            }
            _logger.LogInformation("ZoneSafetyMap: classified {N} creature entries (critter/level)", _byEntry.Count);

            _loaded = true;
            sw.Stop();

            int allianceCells = accumByTeam[(int)Team.Alliance].Values.Sum(m => m.Count);
            int hordeCells    = accumByTeam[(int)Team.Horde].Values.Sum(m => m.Count);
            int allianceGuardCells = _guardCellsByTeam[(int)Team.Alliance].Values.Sum(s => s.Count);
            int hordeGuardCells    = _guardCellsByTeam[(int)Team.Horde].Values.Sum(s => s.Count);
            _logger.LogInformation(
                "ZoneSafetyMap: {Rows} hostile spawns → Alliance grid {AC} contribs/{ACells} cells ({AGuard} guard cells), Horde grid {HC} contribs/{HCells} cells ({HGuard} guard cells), across {Maps} maps in {Ms}ms",
                spawnRows, allianceContribs, allianceCells, allianceGuardCells, hordeContribs, hordeCells, hordeGuardCells,
                _gridsByTeam[(int)Team.Alliance].Count, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ZoneSafetyMap: failed to load creature level grid");
            _loaded = false;
        }
    }

    // ── Grid Helpers ──────────────────────────────────────────────────────

    private static (int ix, int iy) WorldToGrid(float x, float y)
    {
        int ix = (int)((x + COORD_OFFSET) / CELL_SIZE);
        int iy = (int)((y + COORD_OFFSET) / CELL_SIZE);
        return (ix, iy);
    }

    // ── Data Structs ──────────────────────────────────────────────────────

    private struct CellData
    {
        public float AvgLevel;
        public int MaxLevel;
        public int SpawnCount;

        // Per-level spawn histogram (index = level 1..63), populated cells only (null when empty).
        // Added 2026-07-04 (round 4/5): the (avg, max, count) aggregate cannot distinguish "twenty
        // L6 kobolds + ONE L11 named" from "a camp of L11s" — and a single lone rare (Thuros
        // Lightfingers) was vetoing every corridor past it for any group whose weakest was under 8.
        // The histogram lets GetPathThreat count how many spawns actually exceed a caller's
        // threshold, which is what separates "path around one mob" from "that's a camp".
        // Memory: ~64 ints per POPULATED cell, a few thousand cells per map — ~2MB total.
        public int[]? LevelCounts;
    }

    private class CellAccum
    {
        public float TotalLevel;
        public int Count;
        public int MaxLevel;
        public int[] Levels = new int[64];   // per-level spawn counts (see CellData.LevelCounts)
    }
}

/// <summary>Corridor threat shape from ZoneSafetyMap.GetPathThreat — see its doc for field semantics.</summary>
public readonly struct PathThreat
{
    public readonly int MaxLevel;
    public readonly int OverCount;
    public readonly int MaxCellOver;
    public readonly int MaxCellDeep;
    public PathThreat(int maxLevel, int overCount, int maxCellOver, int maxCellDeep)
    { MaxLevel = maxLevel; OverCount = overCount; MaxCellOver = maxCellOver; MaxCellDeep = maxCellDeep; }
}

/// <summary>A grind-worthy cell: world center + the cell's level/density + distance from the bot.</summary>
public readonly struct GrindCell
{
    public readonly float X, Y, AvgLevel, DistYards;
    public readonly int MaxLevel, SpawnCount;
    public GrindCell(float x, float y, float avg, int max, int spawn, float dist)
    { X = x; Y = y; AvgLevel = avg; MaxLevel = max; SpawnCount = spawn; DistYards = dist; }
}