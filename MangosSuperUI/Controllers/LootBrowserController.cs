// LootBrowserController.cs
//
// Generates MSUI_LootBrowserData.lua for the in-game MSUI LootBrowser addon, and keeps the copy
// that lives in wwwroot/addons/MSUI_LootBrowser/ honest about whether it has gone stale.
//
//   GET  /LootBrowser/Preview      counts per category, byte size -- check before shipping
//   GET  /LootBrowser/Export       streams MSUI_LootBrowserData.lua as a download
//   POST /LootBrowser/Regenerate   writes it into wwwroot/addons/MSUI_LootBrowser/ (Downloads page)
//   GET  /LootBrowser/Status       is the packaged file current, and if not, why
//
// STALENESS
// ---------
// The exported file carries a STAMP comment on line 3: counts of everything the
// export is derived from. Status recomputes those counts against the live DB and
// diffs them. Cheap enough to run on every Downloads page load, and it names the
// reason ("2,145 new generated items") instead of just going red.
//
// THREE SOURCES, because no single one has the whole picture:
//
//   drops       mangos.creature + creature_loot_template + reference_loot_template.
//               A reference used by MANY creatures is a shared world-drop table
//               (ref 30048 alone is on 922 loot ids); those are excluded, or one
//               trash mob expands into 250 rows of Canvas Vests. A reference used
//               by <= RefUserLimit creatures is that creature's own drop table --
//               which is ALSO the boss test, because rank is useless here: rank 3
//               is world boss in mangos, and dungeon bosses are rank 1 elite.
//               Reference rows carry ChanceOrQuestChance = 0 meaning EQUAL CHANCE
//               within their group, so the real chance is the parent row's chance
//               divided by the number of zero-chance rows in that group.
//
//   crafting    DbcService.Professions. world.spell_template holds ~1000 rows at
//               build 5875 -- vmangos reads the real spell set from Spell.dbc, so
//               SQL cannot answer "what does this profession make".
//
//   variants    vmangos_admin.lootifier_generated_items. generated_entry ->
//               base_entry + tier_name; a variant is filed UNDER the item it was
//               minted from rather than listed as loot of its own.
//
// Emits format v3: categories -> sets -> nodes -> items -> variants. Every
// ordinary mob in an instance folds into one "Trash Mobs" node.

using System.Globalization;
using System.Text;
using System.Text.Json;
using MangosSuperUI.Models;
using MangosSuperUI.Services;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace MangosSuperUI.Controllers;

public class LootBrowserController : Controller
{
    private readonly ConnectionFactory _db;
    private readonly DbcService _dbc;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<LootBrowserController> _logger;

    public LootBrowserController(ConnectionFactory db, DbcService dbc,
        IWebHostEnvironment env, ILogger<LootBrowserController> logger)
    {
        _db = db;
        _dbc = dbc;
        _env = env;
        _logger = logger;
    }

    // generated_entry -> base_entry. Generated items sit IN the loot tables (360
    // rows in reference_loot_template), so without this they surface as their own
    // top-level rows instead of folding under the item they were minted from.
    private readonly Dictionary<int, int> _genToBase = new();

    // Bosses with loot but no row in `creature` -- summoned rather than spawned,
    // so the map join cannot see them. Ragnaros is the obvious one.
    private readonly List<string> _unplaced = new();

    private const string AddonFolder = "MSUI_LootBrowser";
    private const string DataFileName = "MSUI_LootBrowserData.lua";

    // A reference table used by more creatures than this is shared loot, not a
    // boss table.
    //
    // This was 3, "to leave room for a wing sharing a table across a few
    // spawns". That was wrong: the lootifier writes reference tables that ARE
    // shared between bosses in the same instance, so a limit of 3 handed Gilnid
    // Mr. Smite's weapons and gave Miner Johnson Captain Greenskin's harpoon.
    // A real boss drop table has exactly one user.
    private const int RefUserLimit = 1;

    // Custom entries start here. Stock vanilla stops around 24k.
    private const int CustomEntryFloor = 1_000_000;

    // Instance metadata. Ordered by level, because alphabetical is useless when
    // you are asking "what should I run at 24".
    private sealed record MapInfo(string Name, string Category, int Order, string Level);

    private static readonly Dictionary<int, MapInfo> Maps = new()
    {
        [389] = new("Ragefire Chasm", "Dungeons", 10, "13-18"),
        [36] = new("The Deadmines", "Dungeons", 20, "17-26"),
        [43] = new("Wailing Caverns", "Dungeons", 30, "17-24"),
        [33] = new("Shadowfang Keep", "Dungeons", 40, "22-30"),
        [34] = new("The Stockade", "Dungeons", 50, "24-32"),
        [48] = new("Blackfathom Deeps", "Dungeons", 60, "24-32"),
        [90] = new("Gnomeregan", "Dungeons", 70, "29-38"),
        [47] = new("Razorfen Kraul", "Dungeons", 80, "29-38"),
        [189] = new("Scarlet Monastery", "Dungeons", 90, "34-45"),
        [129] = new("Razorfen Downs", "Dungeons", 100, "37-46"),
        [70] = new("Uldaman", "Dungeons", 110, "41-51"),
        [209] = new("Zul'Farrak", "Dungeons", 120, "44-54"),
        [349] = new("Maraudon", "Dungeons", 130, "46-55"),
        [109] = new("Sunken Temple", "Dungeons", 140, "50-60"),
        [230] = new("Blackrock Depths", "Dungeons", 150, "52-60"),
        [229] = new("Blackrock Spire", "Dungeons", 160, "55-60"),
        [429] = new("Dire Maul", "Dungeons", 170, "55-60"),
        [289] = new("Scholomance", "Dungeons", 180, "58-60"),
        [329] = new("Stratholme", "Dungeons", 190, "58-60"),

        [409] = new("Molten Core", "Raids", 10, "60"),
        [249] = new("Onyxia's Lair", "Raids", 20, "60"),
        [469] = new("Blackwing Lair", "Raids", 30, "60"),
        [309] = new("Zul'Gurub", "Raids", 40, "60"),
        [509] = new("Ruins of Ahn'Qiraj", "Raids", 50, "60"),
        [531] = new("Temple of Ahn'Qiraj", "Raids", 60, "60"),
        [533] = new("Naxxramas", "Raids", 70, "60")
    };

    // Bosses that are SUMMONED rather than placed, so they have no row in
    // `creature` and the map join cannot see them. Ragnaros is summoned by
    // Majordomo, Gandling appears once the six wings are cleared, Sneed climbs
    // out of the shredder. The entries came out of the DB with a "boss-like
    // loot, no spawn" query; only the map assignment is knowledge rather than
    // data, so anything uncertain is left out and reported as unplaced.
    //
    // Deliberately absent, because they belong to no instance: Scorn, Shadow of
    // Doom, Bone Witch, Spirit of the Damned, Lumbering Horror (Scourge
    // Invasion), Ivus and Lokholar (Alterac Valley), Negolash.
    private static readonly Dictionary<int, int> SummonedBosses = new()
    {
        [643] = 36,   // Sneed -- inside Sneed's Shredder
        [3654] = 43,   // Mutanus the Devourer -- Naralex event
        [7275] = 129,  // Shadowpriest Sezz'ziz
        [7355] = 129,  // Tuten'kash
        [7356] = 129,  // Plaguemaw the Rotting
        [8443] = 109,  // Avatar of Hakkar
        [9027] = 230,  // Gorosh the Dervish    -- Ring of Law
        [9028] = 230,  // Grizzle               -- Ring of Law
        [9029] = 230,  // Eviscerator           -- Ring of Law
        [9030] = 230,  // Ok'thor the Breaker   -- Ring of Law
        [9031] = 230,  // Anub'shiah            -- Ring of Law
        [9032] = 230,  // Hedrum the Creeper    -- Ring of Law
        [9537] = 230,  // Hurley Blackbreath    -- the broken barrel
        [9596] = 230,  // Bannok Grimaxe
        [11120] = 230,  // Crimson Hammersmith   -- the anvil
        [10263] = 229,  // Burning Felguard
        [10264] = 229,  // Solakar Flamewreath   -- rookery eggs
        [10268] = 229,  // Gizrul the Slavener
        [10339] = 229,  // Gyth
        [10584] = 229,  // Urok Doomhowl         -- Roughshod Pike
        [1853] = 289,  // Darkmaster Gandling   -- after the six wings
        [10506] = 289,  // Kirtonos the Herald   -- Blood of Innocents
        [10516] = 289,  // The Unforgiven
        [14516] = 289,  // Death Knight Darkreaver
        [10439] = 329,  // Ramstein the Gorger
        [10808] = 329,  // Timmy the Cruel
        [10813] = 329,  // Balnazzar             -- Dathrohan transforms
        [11143] = 329,  // Postmaster Malown     -- three mailboxes
        [14506] = 429,  // Lord Hel'nurath
        [11502] = 409,  // Ragnaros              -- summoned by Majordomo
        [11583] = 469,  // Nefarian              -- Victor Nefarius transforms
        [14515] = 309,  // High Priestess Arlokk
        [15082] = 309,  // Gri'lek
        [15083] = 309,  // Hazza'rah
        [15084] = 309,  // Renataki
        [15085] = 309,  // Wushoolay
        [15517] = 531,  // Ouro
        [15989] = 533   // Sapphiron
    };

    // ---------------------------------------------------------------- endpoints

    [HttpGet]
    public async Task<IActionResult> Preview(
        int minQuality = 2, bool includeTrash = true, bool crafting = true,
        CancellationToken ct = default)
    {
        try
        {
            var model = await BuildAsync(minQuality, includeTrash, crafting, ct);
            var lua = Emit(model);
            return Json(new
            {
                success = true,
                categories = model.Categories.Select(c => new
                {
                    c.Name,
                    sets = c.Sets.Count,
                    nodes = c.Sets.Sum(s => s.Nodes.Count),
                    items = c.Sets.Sum(s => s.Nodes.Sum(n => n.Items.Count)),
                    variants = c.Sets.Sum(s => s.Nodes.Sum(n => n.Items.Sum(i => i.Variants.Count)))
                }),
                totals = Totals(model, Encoding.UTF8.GetByteCount(lua)),
                stamp = model.Stamp
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LootBrowser/Preview failed");
            return Json(new { success = false, error = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> Export(
        int minQuality = 2, bool includeTrash = true, bool crafting = true,
        CancellationToken ct = default)
    {
        try
        {
            var model = await BuildAsync(minQuality, includeTrash, crafting, ct);
            var bytes = Encoding.UTF8.GetBytes(Emit(model));
            _logger.LogInformation("LootBrowser: exported {Bytes} bytes", bytes.Length);
            return File(bytes, "text/plain; charset=utf-8", DataFileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LootBrowser/Export failed");
            return Json(new { success = false, error = ex.Message });
        }
    }

    /// <summary>POST /LootBrowser/Regenerate — rebuild the copy the Downloads page
    /// hands out. The zip is built from that folder, so this is what makes a
    /// download current.</summary>
    [HttpPost]
    public async Task<IActionResult> Regenerate(
        int minQuality = 2, bool includeTrash = true, bool crafting = true,
        CancellationToken ct = default)
    {
        try
        {
            var dir = Path.Combine(_env.WebRootPath, "addons", AddonFolder);
            if (!Directory.Exists(dir))
                return Json(new { success = false, error = $"wwwroot/addons/{AddonFolder}/ does not exist" });

            var model = await BuildAsync(minQuality, includeTrash, crafting, ct);
            var lua = Emit(model);
            await System.IO.File.WriteAllTextAsync(Path.Combine(dir, DataFileName), lua, new UTF8Encoding(false), ct);

            _logger.LogInformation("LootBrowser: regenerated packaged data, {Bytes} bytes", lua.Length);
            return Json(new
            {
                success = true,
                totals = Totals(model, Encoding.UTF8.GetByteCount(lua)),
                stamp = model.Stamp
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LootBrowser/Regenerate failed");
            return Json(new { success = false, error = ex.Message });
        }
    }

    /// <summary>GET /LootBrowser/Status — has the packaged data drifted from the DB.
    /// Only counts, so it is cheap enough for every Downloads page load.</summary>
    [HttpGet]
    public async Task<IActionResult> Status(CancellationToken ct = default)
    {
        try
        {
            var live = await FingerprintAsync(ct);
            var packaged = ReadPackagedStamp(out var generated, out var packagedPath);

            if (packaged is null)
            {
                return Json(new
                {
                    success = true,
                    present = System.IO.File.Exists(packagedPath),
                    stale = true,
                    reasons = new[] { "No generated data has been packaged yet." },
                    live
                });
            }

            var reasons = new List<string>();
            Compare(reasons, "generated items", packaged.Variants, live.Variants);
            Compare(reasons, "loot rows", packaged.LootRows, live.LootRows);
            Compare(reasons, "reference loot rows", packaged.RefRows, live.RefRows);
            Compare(reasons, "custom items", packaged.CustomItems, live.CustomItems);

            return Json(new
            {
                success = true,
                present = true,
                stale = reasons.Count > 0,
                generated,
                reasons,
                packaged,
                live
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LootBrowser/Status failed");
            return Json(new { success = false, error = ex.Message });
        }
    }

    private static void Compare(List<string> reasons, string label, int was, int now)
    {
        if (was == now) return;
        int delta = now - was;
        reasons.Add(delta > 0
            ? $"{delta:N0} new {label} since the packaged export"
            : $"{-delta:N0} fewer {label} than the packaged export");
    }

    private static object Totals(AtlasModel model, int bytes) => new
    {
        categories = model.Categories.Count,
        sets = model.Categories.Sum(c => c.Sets.Count),
        nodes = model.Categories.Sum(c => c.Sets.Sum(s => s.Nodes.Count)),
        items = model.Categories.Sum(c => c.Sets.Sum(s => s.Nodes.Sum(n => n.Items.Count))),
        variants = model.Categories.Sum(c => c.Sets.Sum(s => s.Nodes.Sum(n => n.Items.Sum(i => i.Variants.Count)))),
        bytes
    };

    // ---------------------------------------------------------------- stamp

    public sealed record Fingerprint(int LootRows, int RefRows, int CustomItems, int Variants);

    private async Task<Fingerprint> FingerprintAsync(CancellationToken ct)
    {
        int lootRows, refRows, customItems, variants;

        await using (var conn = _db.Mangos())
        {
            await conn.OpenAsync(ct);
            lootRows = await ScalarAsync(conn, "SELECT COUNT(*) FROM creature_loot_template", ct);
            refRows = await ScalarAsync(conn, "SELECT COUNT(*) FROM reference_loot_template", ct);
            customItems = await ScalarAsync(conn,
                $"SELECT COUNT(*) FROM item_template WHERE entry >= {CustomEntryFloor}", ct);
        }

        await using (var admin = _db.Admin())
        {
            await admin.OpenAsync(ct);
            variants = await ScalarAsync(admin,
                "SELECT COUNT(*) FROM lootifier_generated_items WHERE base_entry > 0", ct);
        }

        return new Fingerprint(lootRows, refRows, customItems, variants);
    }

    private static async Task<int> ScalarAsync(MySqlConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var v = await cmd.ExecuteScalarAsync(ct);
        return v is null or DBNull ? 0 : Convert.ToInt32(v, CultureInfo.InvariantCulture);
    }

    /// <summary>Pulls the STAMP comment back out of the packaged file. Reads only
    /// the first few lines -- the file itself can be megabytes.</summary>
    private Fingerprint? ReadPackagedStamp(out string? generated, out string path)
    {
        generated = null;
        path = Path.Combine(_env.WebRootPath, "addons", AddonFolder, DataFileName);
        if (!System.IO.File.Exists(path)) return null;

        try
        {
            foreach (var line in System.IO.File.ReadLines(path).Take(10))
            {
                if (!line.StartsWith("-- STAMP ", StringComparison.Ordinal)) continue;
                var json = line["-- STAMP ".Length..];
                var doc = JsonSerializer.Deserialize<JsonElement>(json);
                generated = doc.TryGetProperty("generated", out var g) ? g.GetString() : null;
                return new Fingerprint(
                    doc.TryGetProperty("lootRows", out var a) ? a.GetInt32() : 0,
                    doc.TryGetProperty("refRows", out var b) ? b.GetInt32() : 0,
                    doc.TryGetProperty("customItems", out var c) ? c.GetInt32() : 0,
                    doc.TryGetProperty("variants", out var d) ? d.GetInt32() : 0);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LootBrowser: packaged stamp unreadable");
        }
        return null;
    }

    // ---------------------------------------------------------------- build

    private async Task<AtlasModel> BuildAsync(int minQuality, bool includeTrash, bool crafting, CancellationToken ct)
    {
        var model = new AtlasModel { Stamp = await FingerprintAsync(ct) };

        await using var mangos = _db.Mangos();
        await mangos.OpenAsync(ct);

        var variants = await LoadVariantsAsync(mangos, ct);
        await AddDropsAsync(model, mangos, variants, minQuality, includeTrash, ct);
        if (crafting) await AddCraftingAsync(model, mangos, variants, ct);

        return model;
    }

    /// <summary>base_entry -> its generated variants, tier-ordered. Names come from
    /// item_template so a row can show something before the client's item query
    /// comes back.</summary>
    private async Task<Dictionary<int, List<Variant>>> LoadVariantsAsync(MySqlConnection mangos, CancellationToken ct)
    {
        var map = new Dictionary<int, List<Variant>>();
        _genToBase.Clear();

        await using (var admin = _db.Admin())
        {
            await admin.OpenAsync(ct);
            await using var cmd = admin.CreateCommand();
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
                _genToBase[gen] = baseEntry;
            }
        }

        var names = new Dictionary<int, string>();
        var quals = new Dictionary<int, int>();
        await using (var cmd = mangos.CreateCommand())
        {
            cmd.CommandText = $"SELECT entry, name, Quality FROM item_template WHERE entry >= {CustomEntryFloor}";
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
            {
                names[rd.GetInt32(0)] = rd.IsDBNull(1) ? "" : rd.GetString(1);
                quals[rd.GetInt32(0)] = rd.IsDBNull(2) ? 0 : rd.GetInt32(2);
            }
        }

        foreach (var list in map.Values)
        {
            foreach (var v in list)
            {
                v.Name = names.TryGetValue(v.Entry, out var n) ? n : "";
                v.Quality = quals.TryGetValue(v.Entry, out var q) ? q : 0;
            }
            list.RemoveAll(v => v.Name.Length == 0);   // generated row whose item is gone
            list.Sort((a, b) => string.Compare(TierRank(a.Tier) + a.Name, TierRank(b.Tier) + b.Name,
                StringComparison.OrdinalIgnoreCase));
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

    /// <summary>Instance drops. is_ref marks a row that came from a low-use
    /// reference table, which is the boss signal -- creature_template.rank is not,
    /// since rank 3 is world boss and dungeon bosses are rank 1.</summary>
    private async Task AddDropsAsync(
        AtlasModel model, MySqlConnection conn, Dictionary<int, List<Variant>> variants,
        int minQuality, bool includeTrash, CancellationToken ct)
    {
        string mapList = string.Join(",", Maps.Keys);
        string summonedList = string.Join(",", SummonedBosses.Keys);

        // Both passes expand loot identically. Spawned creatures get their map
        // from `creature`; summoned ones have no row there at all.
        string lootSubquery = $@"
    SELECT l.entry AS loot_id, l.item, ABS(l.ChanceOrQuestChance) AS chance, 0 AS is_ref
    FROM creature_loot_template l
    WHERE l.mincountOrRef > 0 AND l.patch_min <= 10 AND l.patch_max >= 10
    UNION ALL
    SELECT l.entry, r.item,
           IF(r.ChanceOrQuestChance = 0,
              ABS(l.ChanceOrQuestChance) / COALESCE(g.n, 1),
              ABS(r.ChanceOrQuestChance)), 1
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
      AND r.patch_min <= 10 AND r.patch_max >= 10";

        var byMap = new Dictionary<int, Dictionary<int, Node>>();
        var isBoss = new Dictionary<int, bool>();

        // One row of loot, folded into the right place.
        void Ingest(int map, int creature, string cname, int item, string itemName,
                    int quality, double chance, bool viaRef)
        {
            if (viaRef) isBoss[creature] = true;

            if (!byMap.TryGetValue(map, out var nodes))
                byMap[map] = nodes = new Dictionary<int, Node>();
            if (!nodes.TryGetValue(creature, out var node))
            {
                node = new Node { Name = cname, Kind = "boss", Entry = creature };
                nodes[creature] = node;
            }

            // A generated item folds under the item it was minted from, exactly
            // as the crafting section does it. Generated entries sit IN the loot
            // tables, so without this they stand as their own top-level rows.
            if (_genToBase.TryGetValue(item, out var baseEntry))
            {
                var host = node.Items.FirstOrDefault(x => x.Entry == baseEntry);
                if (host is null)
                {
                    host = new Item { Entry = baseEntry, Name = "", Chance = 0, Variants = new List<Variant>() };
                    node.Items.Add(host);
                    node.NeedBaseName.Add(baseEntry);
                }
                if (!host.Variants.Any(x => x.Entry == item))
                    host.Variants.Add(new Variant
                    {
                        Entry = item,
                        Name = itemName,
                        Tier = "",
                        Chance = chance,
                        Quality = quality
                    });
                return;
            }

            if (node.Items.Any(x => x.Entry == item)) return;

            node.Items.Add(new Item
            {
                Entry = item,
                Name = itemName,
                Chance = chance,
                Quality = quality,
                Variants = variants.TryGetValue(item, out var v)
                    ? v.Select(x => new Variant
                    {
                        Entry = x.Entry,
                        Name = x.Name,
                        Tier = x.Tier,
                        Quality = x.Quality
                    }).ToList()
                    : new List<Variant>()
            });
        }

        // ---- pass 1: everything spawned inside an instance map
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $@"
SELECT c.map, ct.entry, ct.name, src.item, it.name, it.Quality,
       MAX(src.chance) AS chance, MAX(src.is_ref) AS is_ref
FROM creature c
JOIN creature_template ct ON ct.entry = c.id
JOIN ({lootSubquery}) src ON src.loot_id = ct.loot_id
JOIN item_template it ON it.entry = src.item
WHERE c.map IN ({mapList})
  AND (it.Quality >= {minQuality} OR it.entry >= {CustomEntryFloor})
GROUP BY c.map, ct.entry, src.item
ORDER BY c.map, ct.name, it.Quality DESC, it.name";

            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
            {
                int creature = rd.GetInt32(1);
                Ingest(rd.GetInt32(0), creature,
                       rd.IsDBNull(2) ? $"Creature {creature}" : rd.GetString(2),
                       rd.GetInt32(3),
                       rd.IsDBNull(4) ? "" : rd.GetString(4),
                       rd.IsDBNull(5) ? 0 : rd.GetInt32(5),
                       rd.IsDBNull(6) ? 0 : rd.GetDouble(6),
                       !rd.IsDBNull(7) && rd.GetInt32(7) == 1);
            }
        }

        // ---- pass 2: summoned bosses, mapped by the override table
        await using (var cmd2 = conn.CreateCommand())
        {
            cmd2.CommandText = $@"
SELECT ct.entry, ct.name, src.item, it.name, it.Quality,
       MAX(src.chance) AS chance
FROM creature_template ct
JOIN ({lootSubquery}) src ON src.loot_id = ct.loot_id
JOIN item_template it ON it.entry = src.item
WHERE ct.entry IN ({summonedList})
  AND (it.Quality >= {minQuality} OR it.entry >= {CustomEntryFloor})
GROUP BY ct.entry, src.item
ORDER BY ct.name, it.Quality DESC, it.name";

            await using var rd = await cmd2.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
            {
                int creature = rd.GetInt32(0);
                if (!SummonedBosses.TryGetValue(creature, out var map)) continue;
                Ingest(map, creature,
                       rd.IsDBNull(1) ? $"Creature {creature}" : rd.GetString(1),
                       rd.GetInt32(2),
                       rd.IsDBNull(3) ? "" : rd.GetString(3),
                       rd.IsDBNull(4) ? 0 : rd.GetInt32(4),
                       rd.IsDBNull(5) ? 0 : rd.GetDouble(5),
                       true);
                isBoss[creature] = true;
            }
        }

        // ---- boss-like, has loot, no spawn, no override: reported rather than
        // silently dropped, so a missing boss surfaces as a number in Preview
        await using (var cmd3 = conn.CreateCommand())
        {
            cmd3.CommandText = $@"
SELECT ct.entry, ct.name
FROM creature_template ct
WHERE ct.loot_id > 0
  AND ct.entry NOT IN ({summonedList})
  AND EXISTS (SELECT 1 FROM creature_loot_template l
              JOIN (SELECT mincountOrRef, COUNT(DISTINCT entry) u FROM creature_loot_template
                     WHERE mincountOrRef < 0 GROUP BY mincountOrRef) x
                ON x.mincountOrRef = l.mincountOrRef AND x.u <= {RefUserLimit}
              WHERE l.entry = ct.loot_id AND l.mincountOrRef < 0)
  AND NOT EXISTS (SELECT 1 FROM creature c WHERE c.id = ct.entry)
ORDER BY ct.name";

            await using var rd = await cmd3.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
                _unplaced.Add((rd.IsDBNull(1) ? "?" : rd.GetString(1)) + " (" + rd.GetInt32(0) + ")");
        }

        // ---- base rows invented to host a variant still need their name, and
        // folded variants still need their tier
        var needNames = new HashSet<int>();
        foreach (var nodes in byMap.Values)
            foreach (var n in nodes.Values)
                foreach (var e in n.NeedBaseName)
                    needNames.Add(e);

        if (needNames.Count > 0)
        {
            var baseInfo = await LoadItemInfoAsync(conn, needNames.ToList(), ct);
            foreach (var nodes in byMap.Values)
                foreach (var n in nodes.Values)
                    foreach (var it in n.Items)
                        if (it.Name.Length == 0 && baseInfo.TryGetValue(it.Entry, out var bi))
                        {
                            it.Name = bi.Name;
                            it.Quality = bi.Quality;
                        }
        }

        var tierOf = new Dictionary<int, string>();
        foreach (var kv in variants)
            foreach (var v in kv.Value)
                tierOf[v.Entry] = v.Tier;

        foreach (var nodes in byMap.Values)
        {
            foreach (var n in nodes.Values)
            {
                n.Items.RemoveAll(i => i.Name.Length == 0 && i.Variants.Count == 0);
                foreach (var it in n.Items)
                {
                    foreach (var v in it.Variants)
                        if (v.Tier.Length == 0 && tierOf.TryGetValue(v.Entry, out var t)) v.Tier = t;
                    it.Variants.Sort((a, b) => string.Compare(TierRank(a.Tier) + a.Name,
                        TierRank(b.Tier) + b.Name, StringComparison.OrdinalIgnoreCase));
                }
            }
        }

        // ---- assemble, level-ordered, trash folded into one node per instance
        var dungeons = new Category { Name = "Dungeons", Kind = "dungeon" };
        var raids = new Category { Name = "Raids", Kind = "raid" };

        foreach (var (mapId, info) in Maps.OrderBy(m => m.Value.Category).ThenBy(m => m.Value.Order))
        {
            if (!byMap.TryGetValue(mapId, out var nodes) || nodes.Count == 0) continue;

            var set = new Set { Name = info.Name, Level = info.Level };

            set.Nodes.AddRange(nodes.Values
                .Where(n => isBoss.ContainsKey(n.Entry))
                .OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase));

            if (includeTrash)
            {
                var trash = new Node { Name = "Trash Mobs", Kind = "trash", Entry = 0 };
                var seen = new Dictionary<int, Item>();
                foreach (var n in nodes.Values.Where(n => !isBoss.ContainsKey(n.Entry)))
                {
                    foreach (var it in n.Items)
                    {
                        // the same green drops off twenty mobs; keep the best chance
                        if (seen.TryGetValue(it.Entry, out var have))
                        {
                            if (it.Chance > have.Chance) have.Chance = it.Chance;
                            foreach (var v in it.Variants)
                                if (!have.Variants.Any(x => x.Entry == v.Entry)) have.Variants.Add(v);
                        }
                        else
                        {
                            seen[it.Entry] = it;
                        }
                    }
                }
                trash.Items.AddRange(seen.Values.OrderByDescending(i => i.Chance).ThenBy(i => i.Name));
                if (trash.Items.Count > 0) set.Nodes.Add(trash);
            }

            if (set.Nodes.Count == 0) continue;
            (info.Category == "Raids" ? raids : dungeons).Sets.Add(set);
        }

        if (dungeons.Sets.Count > 0) model.Categories.Add(dungeons);
        if (raids.Sets.Count > 0) model.Categories.Add(raids);
    }

    /// <summary>Professions. The craftable list comes from the DBC pair; names and
    /// the variant lineage come from SQL.</summary>
    private async Task AddCraftingAsync(
        AtlasModel model, MySqlConnection conn, Dictionary<int, List<Variant>> variants, CancellationToken ct)
    {
        var cat = new Category { Name = "Crafting", Kind = "crafting" };

        foreach (var (skillId, profName) in _dbc.GetProfessions())
        {
            var outputs = _dbc.GetProfessionOutputs(skillId);
            if (outputs.Count == 0) continue;

            var info = await LoadItemInfoAsync(conn, outputs.Select(o => (int)o.itemEntry).Distinct().ToList(), ct);

            var set = new Set { Name = profName, Level = "" };
            var node = new Node { Name = profName, Kind = "profession", Entry = (int)skillId };

            foreach (var (itemEntry, _) in outputs)
            {
                int e = (int)itemEntry;
                if (!info.TryGetValue(e, out var rec) || rec.Name.Length == 0) continue;
                node.Items.Add(new Item
                {
                    Entry = e,
                    Name = rec.Name,
                    Quality = rec.Quality,
                    Chance = 0,                       // crafted: nothing to roll
                    Variants = variants.TryGetValue(e, out var v) ? v : new List<Variant>()
                });
            }

            if (node.Items.Count == 0) continue;
            set.Nodes.Add(node);
            cat.Sets.Add(set);
        }

        if (cat.Sets.Count > 0) model.Categories.Add(cat);
    }

    private static async Task<Dictionary<int, (string Name, int Quality)>> LoadItemInfoAsync(
        MySqlConnection conn, List<int> entries, CancellationToken ct)
    {
        var info = new Dictionary<int, (string, int)>();
        if (entries.Count == 0) return info;

        string list = string.Join(",", entries.Select(e => e.ToString(CultureInfo.InvariantCulture)));
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT entry, name, Quality FROM item_template WHERE entry IN ({list})";
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
            info[rd.GetInt32(0)] = (rd.IsDBNull(1) ? "" : rd.GetString(1),
                                    rd.IsDBNull(2) ? 0 : rd.GetInt32(2));
        return info;
    }

    // ---------------------------------------------------------------- emit

    /// <summary>Format v3, documented in the addon's MSUI_LootBrowserData.lua header:
    /// categories -> sets -> nodes -> items -> variants.</summary>
    private static string Emit(AtlasModel model)
    {
        var now = DateTime.Now;
        var sb = new StringBuilder(1 << 20);

        sb.Append("-- GENERATED by MangosSuperUI (/LootBrowser/Export). Do not hand-edit.\n");
        sb.Append("-- Regenerate after any lootifier or retexture run.\n");
        sb.Append("-- STAMP ").Append(JsonSerializer.Serialize(new
        {
            generated = now.ToString("s", CultureInfo.InvariantCulture),
            lootRows = model.Stamp?.LootRows ?? 0,
            refRows = model.Stamp?.RefRows ?? 0,
            customItems = model.Stamp?.CustomItems ?? 0,
            variants = model.Stamp?.Variants ?? 0
        })).Append('\n');
        sb.Append("-- The line above is machine-read by /LootBrowser/Status to detect stale data.\n\n");

        sb.Append("MSUILB_DB = {\n");
        sb.Append("\tversion = 4,\n");
        sb.Append("\tgenerated = \"").Append(now.ToString("yyyy-MM-dd HH:mm")).Append("\",\n");
        sb.Append("\tcategories = {\n");

        foreach (var cat in model.Categories)
        {
            sb.Append("\t\t{\n");
            sb.Append("\t\t\tname = \"").Append(Esc(cat.Name)).Append("\",\n");
            sb.Append("\t\t\tkind = \"").Append(cat.Kind).Append("\",\n");
            sb.Append("\t\t\tsets = {\n");

            foreach (var set in cat.Sets)
            {
                sb.Append("\t\t\t\t{\n");
                sb.Append("\t\t\t\t\tname = \"").Append(Esc(set.Name)).Append("\",\n");
                if (!string.IsNullOrEmpty(set.Level))
                    sb.Append("\t\t\t\t\tlevel = \"").Append(Esc(set.Level)).Append("\",\n");
                sb.Append("\t\t\t\t\tnodes = {\n");

                foreach (var node in set.Nodes)
                {
                    sb.Append("\t\t\t\t\t\t{ name = \"").Append(Esc(node.Name))
                      .Append("\", kind = \"").Append(node.Kind)
                      .Append("\", entry = ").Append(node.Entry).Append(", items = {\n");

                    foreach (var item in node.Items)
                    {
                        // { entry, chance, name, variants|nil, quality }
                        sb.Append("\t\t\t\t\t\t\t{ ").Append(item.Entry).Append(", ")
                          .Append(item.Chance.ToString("0.##", CultureInfo.InvariantCulture))
                          .Append(", \"").Append(Esc(item.Name)).Append('"');

                        if (item.Variants.Count > 0)
                        {
                            sb.Append(", {\n");
                            foreach (var v in item.Variants)
                                // { entry, name, tier, chance, quality }
                                sb.Append("\t\t\t\t\t\t\t\t{ ").Append(v.Entry).Append(", \"")
                                  .Append(Esc(v.Name)).Append("\", \"").Append(Esc(v.Tier)).Append("\", ")
                                  .Append(v.Chance.ToString("0.##", CultureInfo.InvariantCulture))
                                  .Append(", ").Append(v.Quality).Append(" },\n");
                            sb.Append("\t\t\t\t\t\t\t}");
                        }
                        else
                        {
                            sb.Append(", nil");
                        }

                        sb.Append(", ").Append(item.Quality).Append(" },\n");
                    }

                    sb.Append("\t\t\t\t\t\t} },\n");
                }

                sb.Append("\t\t\t\t\t},\n");
                sb.Append("\t\t\t\t},\n");
            }

            sb.Append("\t\t\t},\n");
            sb.Append("\t\t},\n");
        }

        sb.Append("\t},\n};\n");
        return sb.ToString();
    }

    // Lua string escaping. Backslash first, or it escapes the escapes it just added.
    private static string Esc(string s) =>
        (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", "");

    // ---------------------------------------------------------------- model

    private sealed class AtlasModel
    {
        public List<Category> Categories { get; } = new();
        public Fingerprint? Stamp { get; set; }
    }

    private sealed class Category
    {
        public string Name = "";
        public string Kind = "dungeon";
        public List<Set> Sets { get; } = new();
    }

    private sealed class Set
    {
        public string Name = "";
        public string Level = "";
        public List<Node> Nodes { get; } = new();
    }

    private sealed class Node
    {
        public string Name = "";
        public string Kind = "boss";
        public int Entry;
        public List<Item> Items { get; } = new();
        // base rows invented to host a folded variant, pending a name lookup
        public HashSet<int> NeedBaseName { get; } = new();
    }

    private sealed class Item
    {
        public int Entry;
        public string Name = "";
        public double Chance;
        public int Quality;                // item_template.Quality, not the client's cache
        public List<Variant> Variants = new();
    }

    private sealed class Variant
    {
        public int Entry;
        public string Name = "";
        public string Tier = "";
        public double Chance;      // drop chance when the variant is loot; 0 when crafted
        public int Quality;
    }
}