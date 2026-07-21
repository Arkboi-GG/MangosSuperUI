// LootAtlasController.cs
//
// Generates LootAtlasData.lua for the in-game LootAtlas addon. A build step, not
// a browsable page: hit /LootAtlas/Export, drop the file into the addon folder,
// /reload. Re-run it after any lootifier or retexture pass and the addon is current.
//
// THREE SOURCES, because no single one has the whole picture:
//
//   drops       mangos.creature + creature_loot_template + reference_loot_template.
//               References used by MANY creatures are the shared world-drop tables
//               (ref 30048 alone is on 922 loot ids) and are excluded -- otherwise a
//               single trash mob expands into 250 rows of Canvas Vests. A reference
//               used by <= RefUserLimit creatures is that creature's real drop table.
//               Reference rows carry ChanceOrQuestChance = 0 meaning EQUAL CHANCE
//               within their group, so the real chance is the parent row's chance
//               divided by the number of zero-chance rows in that group.
//
//   crafting    DbcService.Professions. world.spell_template holds only ~1000 rows
//               at build 5875 -- vmangos reads the real spell set from Spell.dbc, so
//               SQL cannot answer "what does this profession make". GetProfessionOutputs
//               already resolves SkillLineAbility.dbc -> Spell.dbc effect 24.
//
//   variants    vmangos_admin.lootifier_generated_items. generated_entry -> base_entry
//               + tier_name is the authoritative lineage; variants are filed UNDER the
//               base item they were minted from rather than listed as loot of their own.
//
// Endpoints:
//   GET /LootAtlas/Export   -> LootAtlasData.lua as a download
//   GET /LootAtlas/Preview  -> JSON counts, to sanity check before downloading
//
// No DI registration needed: ConnectionFactory and DbcService are already registered.

using System.Globalization;
using System.Text;
using MangosSuperUI.Models;
using MangosSuperUI.Services;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace MangosSuperUI.Controllers;

public class LootAtlasController : Controller
{
    private readonly ConnectionFactory _db;
    private readonly DbcService _dbc;
    private readonly ILogger<LootAtlasController> _logger;

    public LootAtlasController(ConnectionFactory db, DbcService dbc, ILogger<LootAtlasController> logger)
    {
        _db = db;
        _dbc = dbc;
        _logger = logger;
    }

    // A reference table used by more creatures than this is a shared world-drop
    // table, not a boss's loot. 1 is the common case for real drop tables; 3 gives
    // a little room for wings that share one table across a few spawns.
    private const int RefUserLimit = 3;

    // Custom entries start here. Stock vanilla stops around 24k.
    private const int CustomEntryFloor = 1_000_000;

    private static readonly Dictionary<int, string> Maps = new()
    {
        [33] = "Shadowfang Keep",      [34] = "The Stockade",
        [36] = "The Deadmines",        [43] = "Wailing Caverns",
        [47] = "Razorfen Kraul",       [48] = "Blackfathom Deeps",
        [70] = "Uldaman",              [90] = "Gnomeregan",
        [109] = "Sunken Temple",       [129] = "Razorfen Downs",
        [189] = "Scarlet Monastery",   [209] = "Zul'Farrak",
        [229] = "Blackrock Spire",     [230] = "Blackrock Depths",
        [249] = "Onyxia's Lair",       [289] = "Scholomance",
        [309] = "Zul'Gurub",           [329] = "Stratholme",
        [349] = "Maraudon",            [389] = "Ragefire Chasm",
        [409] = "Molten Core",         [429] = "Dire Maul",
        [469] = "Blackwing Lair",      [509] = "Ruins of Ahn'Qiraj",
        [531] = "Temple of Ahn'Qiraj", [533] = "Naxxramas"
    };

    // ---------------------------------------------------------------- endpoints

    /// <summary>GET /LootAtlas/Export — the generated addon data file.</summary>
    [HttpGet]
    public async Task<IActionResult> Export(
        int minQuality = 2, bool includeTrash = true, bool crafting = true,
        CancellationToken ct = default)
    {
        try
        {
            var model = await BuildAsync(minQuality, includeTrash, crafting, ct);
            var lua = Emit(model);
            var bytes = Encoding.UTF8.GetBytes(lua);
            _logger.LogInformation("LootAtlas: exported {Sets} sets, {Nodes} nodes, {Items} items, {Bytes} bytes",
                model.Sets.Count, model.Sets.Sum(s => s.Nodes.Count),
                model.Sets.Sum(s => s.Nodes.Sum(n => n.Items.Count)), bytes.Length);
            return File(bytes, "text/plain; charset=utf-8", "LootAtlasData.lua");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LootAtlas/Export failed");
            return Json(new { success = false, error = ex.Message });
        }
    }

    /// <summary>GET /LootAtlas/Preview — counts only, so a bad export is obvious
    /// before it lands in the addon folder.</summary>
    [HttpGet]
    public async Task<IActionResult> Preview(
        int minQuality = 2, bool includeTrash = true, bool crafting = true,
        CancellationToken ct = default)
    {
        try
        {
            var model = await BuildAsync(minQuality, includeTrash, crafting, ct);
            var bytes = Encoding.UTF8.GetByteCount(Emit(model));
            return Json(new
            {
                success = true,
                sets = model.Sets.Select(s => new
                {
                    s.Name,
                    s.Kind,
                    nodes = s.Nodes.Count,
                    items = s.Nodes.Sum(n => n.Items.Count),
                    variants = s.Nodes.Sum(n => n.Items.Sum(i => i.Variants.Count))
                }),
                totals = new
                {
                    sets = model.Sets.Count,
                    nodes = model.Sets.Sum(s => s.Nodes.Count),
                    items = model.Sets.Sum(s => s.Nodes.Sum(n => n.Items.Count)),
                    variants = model.Sets.Sum(s => s.Nodes.Sum(n => n.Items.Sum(i => i.Variants.Count))),
                    bytes
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LootAtlas/Preview failed");
            return Json(new { success = false, error = ex.Message });
        }
    }

    // ---------------------------------------------------------------- build

    private async Task<AtlasModel> BuildAsync(int minQuality, bool includeTrash, bool crafting, CancellationToken ct)
    {
        var model = new AtlasModel();

        await using var mangos = _db.Mangos();
        await mangos.OpenAsync(ct);

        var variants = await LoadVariantsAsync(mangos, ct);
        await AddDropsAsync(model, mangos, variants, minQuality, includeTrash, ct);
        if (crafting) await AddCraftingAsync(model, mangos, variants, ct);

        return model;
    }

    /// <summary>base_entry -> its generated variants, newest tier last. Names come
    /// from item_template so the addon can show something before the client's item
    /// query returns.</summary>
    private async Task<Dictionary<int, List<Variant>>> LoadVariantsAsync(MySqlConnection mangos, CancellationToken ct)
    {
        var map = new Dictionary<int, List<Variant>>();

        await using var admin = _db.Admin();
        await admin.OpenAsync(ct);

        await using (var cmd = admin.CreateCommand())
        {
            cmd.CommandText = @"SELECT generated_entry, base_entry, tier_name
                                FROM lootifier_generated_items
                                WHERE base_entry > 0";
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
            {
                int gen = rd.GetInt32(0);
                int baseEntry = rd.GetInt32(1);
                string tier = rd.IsDBNull(2) ? "" : rd.GetString(2);
                if (!map.TryGetValue(baseEntry, out var list)) map[baseEntry] = list = new List<Variant>();
                list.Add(new Variant { Entry = gen, Tier = tier });
            }
        }

        // names for the generated entries, in one pass
        var names = new Dictionary<int, string>();
        await using (var cmd = mangos.CreateCommand())
        {
            cmd.CommandText = $"SELECT entry, name FROM item_template WHERE entry >= {CustomEntryFloor}";
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
                names[rd.GetInt32(0)] = rd.IsDBNull(1) ? "" : rd.GetString(1);
        }

        foreach (var list in map.Values)
        {
            foreach (var v in list)
                v.Name = names.TryGetValue(v.Entry, out var n) ? n : "";
            // drop variants whose item no longer exists, then order by tier name so
            // the expanded list reads Improved -> Power -> Glory -> Legendary
            list.RemoveAll(v => v.Name.Length == 0);
            list.Sort((a, b) => string.Compare(TierRank(a.Tier) + a.Name, TierRank(b.Tier) + b.Name, StringComparison.OrdinalIgnoreCase));
        }
        return map;
    }

    private static string TierRank(string tier) => tier.ToLowerInvariant() switch
    {
        "improved" => "1",
        "power" or "of power" => "2",
        "glory" or "of glory" => "3",
        "of fury" => "4",
        "gods" or "of the gods" => "5",
        "legendary" => "6",
        "of azeroth" => "7",
        "immortal" => "8",
        _ => "9"
    };

    /// <summary>Instance drops. One query, grouped in memory — the reference
    /// expansion and the equal-chance division are both done in SQL because they
    /// need the group counts.</summary>
    private async Task AddDropsAsync(
        AtlasModel model, MySqlConnection conn, Dictionary<int, List<Variant>> variants,
        int minQuality, bool includeTrash, CancellationToken ct)
    {
        string mapList = string.Join(",", Maps.Keys);
        string rankFilter = includeTrash ? "" : " AND ct.rank = 3 ";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
SELECT c.map, ct.entry, ct.name, ct.rank, src.item, it.name, it.Quality, src.chance
FROM creature c
JOIN creature_template ct ON ct.entry = c.id
JOIN (
    SELECT l.entry AS loot_id, l.item, ABS(l.ChanceOrQuestChance) AS chance
    FROM creature_loot_template l
    WHERE l.mincountOrRef > 0 AND l.patch_min <= 10 AND l.patch_max >= 10
    UNION ALL
    SELECT l.entry, r.item,
           IF(r.ChanceOrQuestChance = 0,
              ABS(l.ChanceOrQuestChance) / COALESCE(g.n, 1),
              ABS(r.ChanceOrQuestChance))
    FROM creature_loot_template l
    JOIN reference_loot_template r ON r.entry = -l.mincountOrRef
    LEFT JOIN (SELECT entry, groupid, COUNT(*) n FROM reference_loot_template
                WHERE ChanceOrQuestChance = 0 GROUP BY entry, groupid) g
           ON g.entry = r.entry AND g.groupid = r.groupid
    JOIN (SELECT mincountOrRef, COUNT(DISTINCT entry) users FROM creature_loot_template
           WHERE mincountOrRef < 0 GROUP BY mincountOrRef) u
           ON u.mincountOrRef = l.mincountOrRef
    WHERE l.mincountOrRef < 0 AND u.users <= {RefUserLimit}
      AND l.patch_min <= 10 AND l.patch_max >= 10
      AND r.patch_min <= 10 AND r.patch_max >= 10
) src ON src.loot_id = ct.loot_id
JOIN item_template it ON it.entry = src.item
WHERE c.map IN ({mapList})
  AND (it.Quality >= {minQuality} OR it.entry >= {CustomEntryFloor})
  {rankFilter}
GROUP BY c.map, ct.entry, src.item
ORDER BY c.map, ct.rank DESC, ct.name, it.Quality DESC, it.name";

        var byMap = new Dictionary<int, Dictionary<int, Node>>();

        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            int map = rd.GetInt32(0);
            int creature = rd.GetInt32(1);
            string creatureName = rd.IsDBNull(2) ? $"Creature {creature}" : rd.GetString(2);
            int rank = rd.GetInt32(3);
            int item = rd.GetInt32(4);
            string itemName = rd.IsDBNull(5) ? "" : rd.GetString(5);
            double chance = rd.IsDBNull(7) ? 0 : rd.GetDouble(7);

            if (!byMap.TryGetValue(map, out var nodes)) byMap[map] = nodes = new Dictionary<int, Node>();
            if (!nodes.TryGetValue(creature, out var node))
            {
                node = new Node { Name = creatureName, Kind = rank == 3 ? "boss" : "trash", Entry = creature };
                nodes[creature] = node;
            }

            node.Items.Add(new Item
            {
                Entry = item,
                Name = itemName,
                Chance = chance,
                Variants = variants.TryGetValue(item, out var v) ? v : new List<Variant>()
            });
        }

        foreach (var map in Maps.Keys.OrderBy(k => Maps[k], StringComparer.OrdinalIgnoreCase))
        {
            if (!byMap.TryGetValue(map, out var nodes) || nodes.Count == 0) continue;
            var set = new Set { Name = Maps[map], Kind = "instance" };
            set.Nodes.AddRange(nodes.Values
                .OrderByDescending(n => n.Kind == "boss")
                .ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase));
            model.Sets.Add(set);
        }
    }

    /// <summary>Professions. The craftable list comes from the DBC pair; names,
    /// quality and the custom-variant lineage come from SQL.</summary>
    private async Task AddCraftingAsync(
        AtlasModel model, MySqlConnection conn, Dictionary<int, List<Variant>> variants, CancellationToken ct)
    {
        var set = new Set { Name = "Professions", Kind = "crafting" };

        foreach (var (skillId, profName) in _dbc.GetProfessions())
        {
            var outputs = _dbc.GetProfessionOutputs(skillId);
            if (outputs.Count == 0) continue;

            var entries = outputs.Select(o => (int)o.itemEntry).Distinct().ToList();
            var names = await LoadItemNamesAsync(conn, entries, ct);

            var node = new Node { Name = profName, Kind = "profession", Entry = (int)skillId };
            foreach (var (itemEntry, _) in outputs)
            {
                int e = (int)itemEntry;
                if (!names.TryGetValue(e, out var name)) continue;   // recipe output not in item_template
                node.Items.Add(new Item
                {
                    Entry = e,
                    Name = name,
                    Chance = 0,                                       // crafted: no roll
                    Variants = variants.TryGetValue(e, out var v) ? v : new List<Variant>()
                });
            }

            if (node.Items.Count > 0) set.Nodes.Add(node);
        }

        if (set.Nodes.Count > 0) model.Sets.Add(set);
    }

    private static async Task<Dictionary<int, string>> LoadItemNamesAsync(
        MySqlConnection conn, List<int> entries, CancellationToken ct)
    {
        var names = new Dictionary<int, string>();
        if (entries.Count == 0) return names;

        // entries come from the DBC, not user input, but they are still forced
        // through int parsing before they reach the statement
        string list = string.Join(",", entries.Select(e => e.ToString(CultureInfo.InvariantCulture)));
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT entry, name FROM item_template WHERE entry IN ({list})";
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
            names[rd.GetInt32(0)] = rd.IsDBNull(1) ? "" : rd.GetString(1);
        return names;
    }

    // ---------------------------------------------------------------- emit

    /// <summary>Writes the v2 format documented in the addon's LootAtlasData.lua
    /// header: item = { id, chance, name, variants }, variant = { id, name, tier }.</summary>
    private static string Emit(AtlasModel model)
    {
        var sb = new StringBuilder(1 << 20);
        sb.Append("-- GENERATED by MangosSuperUI (/LootAtlas/Export). Do not hand-edit.\n");
        sb.Append("-- Regenerate after any lootifier or retexture run.\n\n");
        sb.Append("LootAtlasDB = {\n");
        sb.Append("\tversion = 2,\n");
        sb.Append("\tgenerated = \"").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm")).Append("\",\n");
        sb.Append("\tsets = {\n");

        foreach (var set in model.Sets)
        {
            sb.Append("\t\t{\n");
            sb.Append("\t\t\tname = \"").Append(Esc(set.Name)).Append("\",\n");
            sb.Append("\t\t\tkind = \"").Append(set.Kind).Append("\",\n");
            sb.Append("\t\t\tnodes = {\n");

            foreach (var node in set.Nodes)
            {
                sb.Append("\t\t\t\t{ name = \"").Append(Esc(node.Name))
                  .Append("\", kind = \"").Append(node.Kind)
                  .Append("\", entry = ").Append(node.Entry).Append(", items = {\n");

                foreach (var item in node.Items)
                {
                    sb.Append("\t\t\t\t\t{ ").Append(item.Entry).Append(", ")
                      .Append(item.Chance.ToString("0.##", CultureInfo.InvariantCulture)).Append(", \"")
                      .Append(Esc(item.Name)).Append('"');

                    if (item.Variants.Count > 0)
                    {
                        sb.Append(", {\n");
                        foreach (var v in item.Variants)
                        {
                            sb.Append("\t\t\t\t\t\t{ ").Append(v.Entry).Append(", \"")
                              .Append(Esc(v.Name)).Append("\", \"").Append(Esc(v.Tier)).Append("\" },\n");
                        }
                        sb.Append("\t\t\t\t\t}");
                    }

                    sb.Append(" },\n");
                }

                sb.Append("\t\t\t\t} },\n");
            }

            sb.Append("\t\t\t},\n");
            sb.Append("\t\t},\n");
        }

        sb.Append("\t},\n};\n");
        return sb.ToString();
    }

    // Lua string escaping. Backslash first, or it doubles the escapes it just added.
    private static string Esc(string s) =>
        (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", "");

    // ---------------------------------------------------------------- model

    private sealed class AtlasModel
    {
        public List<Set> Sets { get; } = new();
    }

    private sealed class Set
    {
        public string Name = "";
        public string Kind = "instance";
        public List<Node> Nodes { get; } = new();
    }

    private sealed class Node
    {
        public string Name = "";
        public string Kind = "boss";
        public int Entry;
        public List<Item> Items { get; } = new();
    }

    private sealed class Item
    {
        public int Entry;
        public string Name = "";
        public double Chance;
        public List<Variant> Variants = new();
    }

    private sealed class Variant
    {
        public int Entry;
        public string Name = "";
        public string Tier = "";
    }
}
