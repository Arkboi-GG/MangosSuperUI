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

    // Per-map grid. Each cell stores (avgLevel, maxLevel, spawnCount).
    // Only maps with spawns get an entry.
    private readonly Dictionary<int, CellData[,]> _grids = new();

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
    public int GetMaxCreatureLevel(int mapId, float x, float y)
    {
        if (!_grids.TryGetValue(mapId, out var grid)) return 0;
        var (ix, iy) = WorldToGrid(x, y);
        if (ix < 0 || ix >= GRID_DIM || iy < 0 || iy >= GRID_DIM) return 0;
        return grid[ix, iy].MaxLevel;
    }

    /// <summary>
    /// Get the average creature level in the cell containing the given world position.
    /// Returns 0 if no creatures are known in that cell.
    /// </summary>
    public float GetAvgCreatureLevel(int mapId, float x, float y)
    {
        if (!_grids.TryGetValue(mapId, out var grid)) return 0;
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
    public int GetSpawnCount(int mapId, float x, float y)
    {
        if (!_grids.TryGetValue(mapId, out var grid)) return 0;
        var (ix, iy) = WorldToGrid(x, y);
        if (ix < 0 || ix >= GRID_DIM || iy < 0 || iy >= GRID_DIM) return 0;
        return grid[ix, iy].SpawnCount;
    }

    /// <summary>
    /// Find the nearest cell worth GRINDING for level-appropriate XP. Box-scans cells within
    /// maxRadiusYards of (botX,botY) and keeps the best-scoring one that is: populated but not a
    /// dogpile, average level in [L-lowOffset, L+highOffset] (XP, not grey, not a wall), max level
    /// <= L+dangerCeil (no red mob lurking), and not caller-vetoed. Returns null if none qualify.
    ///
    /// Cells already exclude critters / guards / service NPCs (load filter 995478 | 1026), so a
    /// populated cell is a genuine aggressive pack -- this is why steering here fixes the
    /// "kill-anything finds a chicken in a farmyard" spin (chickens are NO_AGGRO -> never in a cell).
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
        int mapId, float botX, float botY, int botLevel, float maxRadiusYards,
        int lowOffset = 5, int highOffset = 2, int dangerCeil = 3,
        int minSpawn = 1, int maxSpawn = 40,
        Func<float, float, bool>? reject = null)
    {
        if (!_grids.TryGetValue(mapId, out var grid)) return null;

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

    /// <summary>
    /// Check if a straight-line path from (x1,y1) to (x2,y2) on the given map
    /// crosses any cells with max creature level above the given threshold.
    /// Samples every CELL_SIZE/2 yards along the path (ensures we don't skip cells).
    ///
    /// Returns the highest creature level encountered on the path, or 0 if safe.
    /// </summary>
    public int GetMaxCreatureLevelOnPath(int mapId, float x1, float y1, float x2, float y2)
    {
        if (!_grids.TryGetValue(mapId, out var grid)) return 0;

        float dx = x2 - x1;
        float dy = y2 - y1;
        float dist = MathF.Sqrt(dx * dx + dy * dy);

        if (dist < 1f) return GetMaxCreatureLevel(mapId, x1, y1);

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
        float? turnInX, float? turnInY)
    {
        int threshold = botLevel + safetyMargin;
        int highestLevel = 0;
        string dangerLeg = "";

        // Leg 1: bot → giver
        if (giverX.HasValue && giverY.HasValue)
        {
            int legMax = GetMaxCreatureLevelOnPath(mapId, botX, botY, giverX.Value, giverY.Value);
            if (legMax > highestLevel) { highestLevel = legMax; dangerLeg = "bot→giver"; }
        }

        // Leg 2: giver → objective
        if (giverX.HasValue && giverY.HasValue && objX.HasValue && objY.HasValue)
        {
            int legMax = GetMaxCreatureLevelOnPath(mapId, giverX.Value, giverY.Value, objX.Value, objY.Value);
            if (legMax > highestLevel) { highestLevel = legMax; dangerLeg = "giver→objective"; }
        }

        // Leg 3: objective → turnin (or giver → turnin if no objective)
        float fromX = objX ?? giverX ?? botX;
        float fromY = objY ?? giverY ?? botY;
        if (turnInX.HasValue && turnInY.HasValue)
        {
            int legMax = GetMaxCreatureLevelOnPath(mapId, fromX, fromY, turnInX.Value, turnInY.Value);
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
            <= 15 => 4000f,   // zone + adjacent zones (Westfall, Redridge)
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
                    ct.level_max AS MaxLevel
                FROM creature c
                JOIN creature_template ct ON ct.entry = c.id AND ct.patch = 0
                WHERE c.map IN (0, 1)
                  AND ct.level_min > 0
                  AND ct.level_max > 0
                  AND ct.level_max <= 63
                  AND (ct.npc_flags & 995478) = 0
                  AND (ct.flags_extra & 1026) = 0");
            //995478 = questgiver | trainer | vendor | repair | flightmaster | innkeeper | banker | auctioneer | stablemaster
            // 1026 = NO_AGGRO(2) | NO_AGGRO_ON_SIGHT(1024) — excludes guards +spirit healers

            // Accumulate per-cell
            var accum = new Dictionary<int, Dictionary<(int ix, int iy), CellAccum>>();

            int spawnCount = 0;
            foreach (var r in rows)
            {
                int mapId = (int)r.map;
                float x = (float)r.position_x;
                float y = (float)r.position_y;
                int minLvl = (int)r.MinLevel;
                int maxLvl = (int)r.MaxLevel;
                float avgLvl = (minLvl + maxLvl) / 2f;

                var (ix, iy) = WorldToGrid(x, y);
                if (ix < 0 || ix >= GRID_DIM || iy < 0 || iy >= GRID_DIM) continue;

                if (!accum.TryGetValue(mapId, out var mapAccum))
                {
                    mapAccum = new Dictionary<(int, int), CellAccum>();
                    accum[mapId] = mapAccum;
                }

                var key = (ix, iy);
                if (!mapAccum.TryGetValue(key, out var cell))
                {
                    cell = new CellAccum();
                    mapAccum[key] = cell;
                }

                cell.TotalLevel += avgLvl;
                cell.Count++;
                if (maxLvl > cell.MaxLevel) cell.MaxLevel = maxLvl;

                spawnCount++;
            }

            // Build grids
            foreach (var (mapId, mapAccum) in accum)
            {
                var grid = new CellData[GRID_DIM, GRID_DIM];

                foreach (var ((ix, iy), cell) in mapAccum)
                {
                    grid[ix, iy] = new CellData
                    {
                        AvgLevel = cell.TotalLevel / cell.Count,
                        MaxLevel = cell.MaxLevel,
                        SpawnCount = cell.Count
                    };
                }

                _grids[mapId] = grid;
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

            int cellsPopulated = accum.Values.Sum(m => m.Count);
            _logger.LogInformation(
                "ZoneSafetyMap: loaded {Spawns} creature spawns into {Cells} cells across {Maps} maps in {Ms}ms",
                spawnCount, cellsPopulated, accum.Count, sw.ElapsedMilliseconds);
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
    }

    private class CellAccum
    {
        public float TotalLevel;
        public int Count;
        public int MaxLevel;
    }
}

/// <summary>A grind-worthy cell: world center + the cell's level/density + distance from the bot.</summary>
public readonly struct GrindCell
{
    public readonly float X, Y, AvgLevel, DistYards;
    public readonly int MaxLevel, SpawnCount;
    public GrindCell(float x, float y, float avg, int max, int spawn, float dist)
    { X = x; Y = y; AvgLevel = avg; MaxLevel = max; SpawnCount = spawn; DistYards = dist; }
}