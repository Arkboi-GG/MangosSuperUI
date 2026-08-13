using System.Text.Json;
using MangosSuperUI.Controllers;

namespace MangosSuperUI.Services;

/// <summary>
/// Map id → dungeon/raid identity. Lifted out of InstancesController so services can name
/// a map without depending on a controller; InstancesController now reads from here, so
/// there is still exactly one copy of the table.
/// </summary>
public static class InstanceCatalog
{
    public static readonly List<InstanceInfo> All = new()
    {
        // Dungeons
        new(389, "Ragefire Chasm", "dungeon", "13-18"),
        new(36,  "Deadmines", "dungeon", "17-21"),
        new(43,  "Wailing Caverns", "dungeon", "17-24"),
        new(34,  "The Stockade", "dungeon", "22-30"),
        new(33,  "Shadowfang Keep", "dungeon", "22-30"),
        new(48,  "Blackfathom Deeps", "dungeon", "24-32"),
        new(47,  "Razorfen Kraul", "dungeon", "29-38"),
        new(90,  "Gnomeregan", "dungeon", "29-38"),
        new(189, "Scarlet Monastery", "dungeon", "28-45"),
        new(129, "Razorfen Downs", "dungeon", "37-46"),
        new(70,  "Uldaman", "dungeon", "41-51"),
        new(209, "Zul'Farrak", "dungeon", "44-54"),
        new(349, "Maraudon", "dungeon", "46-55"),
        new(109, "Sunken Temple", "dungeon", "50-56"),
        new(230, "Blackrock Depths", "dungeon", "52-60"),
        new(229, "Blackrock Spire", "dungeon", "55-60"),
        new(429, "Dire Maul", "dungeon", "55-60"),
        new(329, "Stratholme", "dungeon", "58-60"),
        new(289, "Scholomance", "dungeon", "58-60"),
        // Raids
        new(249, "Onyxia's Lair", "raid", "60"),
        new(409, "Molten Core", "raid", "60"),
        new(469, "Blackwing Lair", "raid", "60"),
        new(309, "Zul'Gurub", "raid", "60"),
        new(509, "Ruins of Ahn'Qiraj", "raid", "60"),
        new(531, "Temple of Ahn'Qiraj", "raid", "60"),
        new(533, "Naxxramas", "raid", "60")
    };

    private static readonly Dictionary<int, InstanceInfo> ById =
        All.ToDictionary(i => i.MapId);

    /// <summary>Human name for a map id — the instance name, the two continents, or a fallback.</summary>
    public static string NameFor(int mapId) => ById.TryGetValue(mapId, out var i)
        ? i.Name
        : mapId switch
        {
            0 => "Eastern Kingdoms",
            1 => "Kalimdor",
            30 => "Alterac Valley",
            489 => "Warsong Gulch",
            529 => "Arathi Basin",
            _ => $"Map {mapId}",
        };

    public static InstanceInfo? Find(int mapId) => ById.GetValueOrDefault(mapId);

    // ══════════ Curated bosses (wwwroot/data/instance-bosses.json) ══════════
    //
    // The same file the Instance Loot page reads. Anything naming a boss should come from
    // here rather than re-deriving it from creature spawns, so the Change Graph lists the
    // same bosses, in the same kill order, that the Instance page does.

    private static Dictionary<int, List<BossEntry>>? _bossMap;
    private static Dictionary<int, (int MapId, BossEntry Boss)>? _bossByEntry;
    private static readonly object _bossLock = new();

    public static Dictionary<int, List<BossEntry>> BossMap(string? webRootPath = null)
    {
        if (_bossMap != null) return _bossMap;
        lock (_bossLock)
        {
            if (_bossMap != null) return _bossMap;

            var map = new Dictionary<int, List<BossEntry>>();
            var path = Path.Combine(webRootPath ?? "wwwroot", "data", "instance-bosses.json");

            if (File.Exists(path))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(path));
                    foreach (var inst in doc.RootElement.GetProperty("instances").EnumerateArray())
                    {
                        var mapId = inst.GetProperty("mapId").GetInt32();
                        var bosses = new List<BossEntry>();
                        var order = 0;
                        foreach (var b in inst.GetProperty("bosses").EnumerateArray())
                        {
                            bosses.Add(new BossEntry
                            {
                                Entry = b.GetProperty("entry").GetInt32(),
                                Name = b.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                                Order = b.TryGetProperty("order", out var o) ? o.GetInt32() : order,
                                Optional = b.TryGetProperty("optional", out var op) && op.GetBoolean(),
                            });
                            order++;
                        }
                        map[mapId] = bosses;
                    }
                }
                catch
                {
                    // A malformed curation file degrades boss naming; it must not break the page.
                }
            }

            _bossMap = map;
            _bossByEntry = map
                .SelectMany(kv => kv.Value.Select(b => (MapId: kv.Key, Boss: b)))
                .GroupBy(x => x.Boss.Entry)
                .ToDictionary(g => g.Key, g => (g.First().MapId, g.First().Boss));

            return _bossMap;
        }
    }

    /// <summary>Which instance a creature is a boss of, if it is one at all.</summary>
    public static (int MapId, BossEntry Boss)? BossFor(int creatureEntry, string? webRootPath = null)
    {
        BossMap(webRootPath);
        return _bossByEntry!.TryGetValue(creatureEntry, out var hit) ? hit : null;
    }
}
