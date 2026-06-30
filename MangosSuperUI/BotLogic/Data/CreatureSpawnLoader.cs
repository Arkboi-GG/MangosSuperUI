using Dapper;
using MangosSuperUI.Models;
using MangosSuperUI.Services;

namespace MangosSuperUI.BotLogic.Data;

/// <summary>
/// Per-creature-entry SPAWN FOOTPRINT loader + sampler (Scatter Build 2, 2026-06-29).
///
/// The problem this removes: the objective-grind dispatch (QuestPlanner.to_objective) built
/// the enriched MOVE_TO from a single representative GrindX/GrindY per objective. Every bot
/// that holds the same kill quest — grouped OR independently-solo (same starter quest, same
/// canonical coord) — marched to the identical pixel and dogpiled it. In a dense starter field
/// that tripped the OverpullGuard → GRIND_BLOCKED handback churn the unstick detour was only
/// BANDAGING. Scatter removes the cause: each dispatch samples a RANDOM REAL SPAWN COORD of the
/// target creature, so co-holders fan out across the mob's actual footprint with no cross-bot
/// coordination needed (which is exactly what the solo case wants).
///
/// Why "real spawn coords" and not a Delaunay hull sample (the design's Version B): every row
/// in the `creature` spawn table is, by definition, valid ground the mob occupies — navmesh
/// validity comes free, with no C# nav check (which matters because the enriched-objective
/// MOVE_TO deliberately SKIPS the C++ arrival jitter, so C# must hand over a good coord). A hull
/// interior point could land in a pond in the middle of a spawn ring; a real spawn point cannot.
///
/// Mirrors ZoneDataLoader: load the whole set once at boot (LoadAsync from Program.cs, after the
/// other loaders), cache forever (spawn geometry is static world data), sync lookups thereafter.
/// The creature spawn table reference to the template entry is column `id` on THIS core (same as
/// ZoneDataLoader's `INNER JOIN creature_template ct ON c.id = ct.entry`).
/// </summary>
public class CreatureSpawnLoader
{
    private readonly ConnectionFactory _db;
    private readonly ILogger<CreatureSpawnLoader> _logger;

    // Cache: creature_template.entry → its spawn points (each carries its own map; we filter at
    // sample time, same runtime-filter style as ZoneDataLoader.GetNearestVendor).
    private readonly Dictionary<int, List<SpawnPoint>> _spawnsByEntry = new();

    private bool _loaded = false;

    public CreatureSpawnLoader(ConnectionFactory db, ILogger<CreatureSpawnLoader> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Preload every creature spawn coord, grouped by template entry. Called once at startup
    /// (Program.cs), after QuestGraphLoader/ZoneDataLoader. ~tens of thousands of rows, cached.
    /// Never throws — logs and leaves the cache empty (SampleScatterPoint then returns null
    /// everywhere → every dispatch falls back to the canonical GrindX/GrindY = today's behavior).
    /// </summary>
    public async Task LoadAsync()
    {
        if (_loaded) return;

        try
        {
            using var conn = _db.Mangos();

            // Pure coords from the spawn table — no creature_template join needed (we only want
            // "where does entry N stand"). VMaNGOS `creature`: id = template entry, map,
            // position_x/y/z. Event-gated spawns are NOT excluded here (kill targets are not
            // event-gated like the Darkmoon vendor phantoms; and on the rare miss the C++ 100yd
            // re-scan + the 120s no-kill grace + the canonical fallback all cover it).
            var rows = await conn.QueryAsync<SpawnRow>(@"
                SELECT id AS entry, map, position_x, position_y, position_z
                FROM creature");

            int total = 0;
            foreach (var r in rows)
            {
                if (r.entry <= 0) continue;
                if (!_spawnsByEntry.TryGetValue(r.entry, out var list))
                {
                    list = new List<SpawnPoint>();
                    _spawnsByEntry[r.entry] = list;
                }
                list.Add(new SpawnPoint
                {
                    Map = r.map,
                    X = r.position_x,
                    Y = r.position_y,
                    Z = r.position_z
                });
                total++;
            }

            _loaded = true;
            _logger.LogInformation(
                "CreatureSpawnLoader: cached {Total} spawn points across {Entries} creature entries",
                total, _spawnsByEntry.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CreatureSpawnLoader: failed to load creature spawns — scatter disabled, dispatch falls back to canonical coords");
        }
    }

    /// <summary>
    /// A random real spawn coord for <paramref name="entry"/> on <paramref name="mapId"/>, or
    /// null when the entry has fewer than 2 known spawns on that map (summoned / scripted /
    /// pooled-absent / single-spawn). The caller MUST treat null as "use the canonical
    /// GrindX/GrindY" so there is no regression — and so the Held objective stamp and the MOVE_TO
    /// stay consistent (both fall back together). With a footprint of ≥2, returns a varied point
    /// each call → uncoordinated dispersal across the footprint.
    /// </summary>
    public SpawnPoint? SampleScatterPoint(int entry, int mapId)
    {
        if (entry <= 0) return null;
        if (!_spawnsByEntry.TryGetValue(entry, out var list)) return null;

        var onMap = list.Where(s => s.Map == mapId).ToList();
        if (onMap.Count < 2) return null;   // 0 or 1 → no dispersal value → canonical fallback

        return onMap[Core.WeightedRoller.RangeInt(0, onMap.Count - 1)];
    }

    /// <summary>How many spawns of this entry are known on this map (0 if none). Diagnostics only.</summary>
    public int SpawnCount(int entry, int mapId)
    {
        if (!_spawnsByEntry.TryGetValue(entry, out var list)) return 0;
        return list.Count(s => s.Map == mapId);
    }
}

// Internal row DTO for Dapper.
internal class SpawnRow
{
    public int entry { get; set; }
    public int map { get; set; }
    public float position_x { get; set; }
    public float position_y { get; set; }
    public float position_z { get; set; }
}

public class SpawnPoint
{
    public int Map { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
}
