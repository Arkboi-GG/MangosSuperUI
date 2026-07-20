// ItemSourceResolver.cs
//
// "Where does this item come from?" — one resolver, shared by the Items page
// (/Items/Sources) and the Retexture Engine (/RetextureEngine/ItemSources).
//
// WHY THIS EXISTS
// ---------------
// The previous implementation asked three flat questions (creature_loot_template
// joined on entry=entry, npc_vendor, quest rewards) and swallowed every exception.
// That missed most of the game:
//
//   * reference_loot_template. On this DB 28,368 loot rows are references
//     (mincountOrRef < 0) — a loot row that says "roll table #N" instead of
//     naming an item. Nearly all dungeon and raid loot lives behind one, so a
//     direct `WHERE item = @E` never sees it. This resolver walks the reference
//     graph (bounded depth) and maps the references back to concrete loot ids.
//
//   * loot ids are not entity ids. creature_template.loot_id is the key into
//     creature_loot_template, and likewise pickpocket_loot_id / skinning_loot_id.
//     On this DB loot_id == entry for all but 21 creatures, so the old join was
//     accidentally almost right — but "almost" isn't a reason to keep it, and
//     pickpocket/skinning were never covered at all.
//
//   * whole source classes were absent: gameobject_loot_template (chests, herb
//     and ore nodes), item_loot_template (lockboxes, containers), disenchanting,
//     fishing, skinning, pickpocketing, mail, crafting spells, quest objective
//     items, and items that START a quest.
//
// FAILURES ARE REPORTED, NOT SWALLOWED. Every probe that throws (a table or
// column this DB doesn't have) appends a line to Notes instead of silently
// returning nothing, so a missing source class is visible in the UI rather than
// looking like "this item has no sources".
//
// No DI: everything is static and takes an open IDbConnection, so both callers
// use their existing _db.Mangos() connection and Program.cs needs no change.

using System.Data;
using Dapper;

namespace MangosSuperUI.Services;

/// <summary>One place an item can come from.</summary>
public sealed class ItemSource
{
    /// <summary>Owner id (creature entry, gameobject entry, quest id, item entry, spell id...). 0 when not applicable.</summary>
    public int Id { get; set; }

    /// <summary>Display name — creature/object/quest/item name, or a synthesized label.</summary>
    public string Name { get; set; } = "";

    /// <summary>Free-text qualifier shown after the name: "skinning", "reference table #12345", "quest objective"...</summary>
    public string? Detail { get; set; }

    /// <summary>Drop chance percent where the loot row gives one; null otherwise. 0 in VMaNGOS means "quest/conditional drop".</summary>
    public double? Chance { get; set; }
}

/// <summary>Everything known about where an item comes from.</summary>
public sealed class ItemSourceResult
{
    public bool Success { get; set; } = true;
    public int Entry { get; set; }

    public List<ItemSource> Creatures { get; set; } = new();   // killed / skinned / pickpocketed
    public List<ItemSource> Objects { get; set; } = new();   // chests, nodes, fishing
    public List<ItemSource> Containers { get; set; } = new();   // opened from another item
    public List<ItemSource> Vendors { get; set; } = new();
    public List<ItemSource> Quests { get; set; } = new();   // reward, objective, starts
    public List<ItemSource> Crafted { get; set; } = new();   // create-item spells + their recipes
    public List<ItemSource> Disenchant { get; set; } = new();   // disenchanted from
    public List<ItemSource> Other { get; set; } = new();   // mail templates, unmapped loot ids

    /// <summary>Diagnostics: probes that failed, and loot ids that resolved to no owner.</summary>
    public List<string> Notes { get; set; } = new();

    public int TotalCount =>
        Creatures.Count + Objects.Count + Containers.Count + Vendors.Count +
        Quests.Count + Crafted.Count + Disenchant.Count + Other.Count;
}

public static class ItemSourceResolver
{
    // ── Dapper row shapes ─────────────────────────────────────────────────────
    // Explicit types, not ValueTuples: Dapper maps by property name, and tuple
    // element names are compiler metadata it cannot see.
    private sealed class LootRow { public int Entry { get; set; } public double Chance { get; set; } }
    private sealed class OwnerRow { public int Entry { get; set; } public string? Name { get; set; } public int LootId { get; set; } }
    private sealed class NamedRow { public int Entry { get; set; } public string? Name { get; set; } }
    private sealed class QuestRow { public int Entry { get; set; } public string? Title { get; set; } }
    private sealed class ItemRow { public int Entry { get; set; } public string? Name { get; set; } public int Quality { get; set; } }
    private sealed class RecipeRow { public int Entry { get; set; } public string? Name { get; set; } public int S1 { get; set; } public int S2 { get; set; } }

    private const int MaxPerBucket = 60;     // keep the panel readable
    private const int MaxRefDepth = 4;      // reference chains are 1-2 deep in vanilla; 4 is slack

    /// <summary>SPELL_EFFECT_CREATE_ITEM.</summary>
    private const int EFFECT_CREATE_ITEM = 24;

    /// <summary>Concrete loot tables and the column on their owner that holds the loot id.</summary>
    private static readonly (string Table, string Bucket)[] LootTables =
    {
        ("creature_loot_template",      "creature"),
        ("pickpocketing_loot_template", "pickpocket"),
        ("skinning_loot_template",      "skinning"),
        ("gameobject_loot_template",    "gameobject"),
        ("item_loot_template",          "item"),
        ("disenchant_loot_template",    "disenchant"),
        ("fishing_loot_template",       "fishing"),
        ("mail_loot_template",          "mail"),
    };

    public static async Task<ItemSourceResult> ResolveAsync(IDbConnection conn, int entry)
    {
        var r = new ItemSourceResult { Entry = entry };

        // ── 1. Reference graph ────────────────────────────────────────────────
        // Which reference_loot_template tables can yield this item, directly or
        // through a nested reference?
        var refIds = await ResolveReferenceIdsAsync(conn, entry, r);

        // ── 2. Loot ids per concrete table ────────────────────────────────────
        // Direct rows (item = @E) plus rows that point at any reference we found
        // (mincountOrRef = -refId). Chance travels with the row so the UI can
        // show it.
        var lootIds = new Dictionary<string, Dictionary<int, double?>>();
        foreach (var (table, bucket) in LootTables)
        {
            var map = await LootIdsForTableAsync(conn, table, entry, refIds, r);
            if (map.Count > 0) lootIds[bucket] = map;
        }

        // ── 3. Map loot ids back to the things that own them ──────────────────
        await MapCreatureLootAsync(conn, r, lootIds, "creature", "loot_id", null);
        await MapCreatureLootAsync(conn, r, lootIds, "pickpocket", "pickpocket_loot_id", "pickpocketed");
        await MapCreatureLootAsync(conn, r, lootIds, "skinning", "skinning_loot_id", "skinned");
        await MapGameObjectLootAsync(conn, r, lootIds);
        await MapItemLootAsync(conn, r, lootIds);
        await MapDisenchantAsync(conn, r, lootIds);
        MapFishingAndMail(r, lootIds);

        // ── 4. Non-loot sources ───────────────────────────────────────────────
        await AddVendorsAsync(conn, r, entry);
        await AddQuestsAsync(conn, r, entry);
        await AddCraftingAsync(conn, r, entry);

        foreach (var list in new[] { r.Creatures, r.Objects, r.Containers, r.Vendors, r.Quests, r.Crafted, r.Disenchant, r.Other })
            Trim(list);

        return r;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Reference resolution
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Every reference_loot_template id that can produce this item — the tables
    /// that name it directly, plus any reference table that rolls one of those.
    /// </summary>
    private static async Task<HashSet<int>> ResolveReferenceIdsAsync(IDbConnection conn, int entry, ItemSourceResult r)
    {
        var found = new HashSet<int>();
        try
        {
            var direct = await conn.QueryAsync<int>(
                "SELECT DISTINCT entry FROM reference_loot_template WHERE item = @E", new { E = entry });
            foreach (var id in direct) found.Add(id);

            // Walk upward: a reference table can itself be rolled by another one.
            var frontier = new HashSet<int>(found);
            for (int depth = 0; depth < MaxRefDepth && frontier.Count > 0; depth++)
            {
                var negatives = frontier.Select(id => -id).ToList();
                var parents = await conn.QueryAsync<int>(
                    "SELECT DISTINCT entry FROM reference_loot_template WHERE mincountOrRef IN @Refs",
                    new { Refs = negatives });

                var next = new HashSet<int>();
                foreach (var p in parents)
                    if (found.Add(p)) next.Add(p);
                frontier = next;
            }
        }
        catch (Exception ex)
        {
            r.Notes.Add("reference_loot_template probe failed: " + Short(ex));
        }
        return found;
    }

    /// <summary>Loot ids in one concrete table that yield the item, direct or via reference.</summary>
    private static async Task<Dictionary<int, double?>> LootIdsForTableAsync(
        IDbConnection conn, string table, int entry, HashSet<int> refIds, ItemSourceResult r)
    {
        var map = new Dictionary<int, double?>();

        try
        {
            var direct = await conn.QueryAsync<LootRow>(
                $"SELECT entry AS Entry, ChanceOrQuestChance AS Chance FROM {table} WHERE item = @E", new { E = entry });
            foreach (var row in direct)
                map[row.Entry] = row.Chance;
        }
        catch (Exception ex)
        {
            r.Notes.Add($"{table} direct probe failed: " + Short(ex));
            return map;
        }

        if (refIds.Count == 0) return map;

        try
        {
            var negatives = refIds.Select(id => -id).ToList();
            var viaRef = await conn.QueryAsync<LootRow>(
                $"SELECT entry AS Entry, ChanceOrQuestChance AS Chance FROM {table} WHERE mincountOrRef IN @Refs",
                new { Refs = negatives });
            foreach (var row in viaRef)
                if (!map.ContainsKey(row.Entry)) map[row.Entry] = row.Chance;   // direct chance wins
        }
        catch (Exception ex)
        {
            r.Notes.Add($"{table} reference probe failed: " + Short(ex));
        }

        return map;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Loot id -> owner
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// creature_template holds three loot-id columns. The loot table key is the
    /// LOOT ID, not the creature entry — they coincide for most creatures on this
    /// DB but not all, and never for reference-driven boss tables.
    /// </summary>
    private static async Task MapCreatureLootAsync(
        IDbConnection conn, ItemSourceResult r,
        Dictionary<string, Dictionary<int, double?>> lootIds, string bucket, string column, string? detail)
    {
        if (!lootIds.TryGetValue(bucket, out var map) || map.Count == 0) return;

        try
        {
            var rows = await conn.QueryAsync<OwnerRow>(
                $"SELECT entry AS Entry, name AS Name, {column} AS LootId FROM creature_template WHERE {column} IN @Ids",
                new { Ids = map.Keys.ToList() });

            var matched = new HashSet<int>();
            foreach (var row in rows)
            {
                matched.Add(row.LootId);
                r.Creatures.Add(new ItemSource
                {
                    Id = row.Entry,
                    Name = row.Name ?? $"creature #{row.Entry}",
                    Detail = detail,
                    Chance = map.TryGetValue(row.LootId, out var c) ? c : null
                });
            }

            var orphans = map.Keys.Where(id => !matched.Contains(id)).ToList();
            if (orphans.Count > 0)
                r.Notes.Add($"{bucket}: {orphans.Count} loot id(s) with no owning creature (e.g. {string.Join(", ", orphans.Take(5))})");
        }
        catch (Exception ex)
        {
            r.Notes.Add($"creature_template.{column} lookup failed: " + Short(ex));
        }
    }

    /// <summary>
    /// Chests and gathering nodes. In VMaNGOS a gameobject's loot id lives in the
    /// type-dependent data columns: data1 for chests (type 3). Other types are
    /// reported as raw loot ids rather than guessed at.
    /// </summary>
    private static async Task MapGameObjectLootAsync(
        IDbConnection conn, ItemSourceResult r, Dictionary<string, Dictionary<int, double?>> lootIds)
    {
        if (!lootIds.TryGetValue("gameobject", out var map) || map.Count == 0) return;

        var matched = new HashSet<int>();
        try
        {
            var rows = await conn.QueryAsync<OwnerRow>(
                "SELECT entry AS Entry, name AS Name, data1 AS LootId FROM gameobject_template WHERE type = 3 AND data1 IN @Ids",
                new { Ids = map.Keys.ToList() });

            foreach (var row in rows)
            {
                matched.Add(row.LootId);
                r.Objects.Add(new ItemSource
                {
                    Id = row.Entry,
                    Name = row.Name ?? $"object #{row.Entry}",
                    Detail = "container / node",
                    Chance = map.TryGetValue(row.LootId, out var c) ? c : null
                });
            }
        }
        catch (Exception ex)
        {
            r.Notes.Add("gameobject_template lookup failed: " + Short(ex));
        }

        foreach (var id in map.Keys.Where(id => !matched.Contains(id)))
            r.Objects.Add(new ItemSource { Id = 0, Name = $"gameobject loot table #{id}", Detail = "no chest maps to this table", Chance = map[id] });
    }

    /// <summary>Lockboxes, pouches, any item that opens into other items. The loot id IS the container's item entry.</summary>
    private static async Task MapItemLootAsync(
        IDbConnection conn, ItemSourceResult r, Dictionary<string, Dictionary<int, double?>> lootIds)
    {
        if (!lootIds.TryGetValue("item", out var map) || map.Count == 0) return;

        try
        {
            var rows = await conn.QueryAsync<NamedRow>(
                "SELECT entry AS Entry, name AS Name FROM item_template WHERE entry IN @Ids GROUP BY entry, name",
                new { Ids = map.Keys.ToList() });

            foreach (var row in rows)
                r.Containers.Add(new ItemSource
                {
                    Id = row.Entry,
                    Name = row.Name ?? $"item #{row.Entry}",
                    Detail = "opened from",
                    Chance = map.TryGetValue(row.Entry, out var c) ? c : null
                });
        }
        catch (Exception ex)
        {
            r.Notes.Add("item_loot_template owner lookup failed: " + Short(ex));
        }
    }

    /// <summary>Items whose disenchant_id points at a table that yields this item.</summary>
    private static async Task MapDisenchantAsync(
        IDbConnection conn, ItemSourceResult r, Dictionary<string, Dictionary<int, double?>> lootIds)
    {
        if (!lootIds.TryGetValue("disenchant", out var map) || map.Count == 0) return;

        try
        {
            // Can be thousands of items; summarize rather than list them all.
            var rows = (await conn.QueryAsync<ItemRow>(
                "SELECT entry AS Entry, name AS Name, quality AS Quality FROM item_template WHERE disenchant_id IN @Ids ORDER BY quality DESC, name ASC LIMIT 200",
                new { Ids = map.Keys.ToList() })).ToList();

            foreach (var row in rows.Take(MaxPerBucket))
                r.Disenchant.Add(new ItemSource { Id = row.Entry, Name = row.Name ?? $"item #{row.Entry}", Detail = "disenchanted from" });

            if (rows.Count > MaxPerBucket)
                r.Disenchant.Add(new ItemSource { Id = 0, Name = $"...and more ({rows.Count}+ items disenchant into this)" });
        }
        catch (Exception ex)
        {
            r.Notes.Add("disenchant owner lookup failed (item_template.disenchant_id): " + Short(ex));
        }
    }

    /// <summary>Fishing loot is keyed by zone; mail loot by mail template. Neither has a name in the world DB.</summary>
    private static void MapFishingAndMail(ItemSourceResult r, Dictionary<string, Dictionary<int, double?>> lootIds)
    {
        if (lootIds.TryGetValue("fishing", out var fish))
            foreach (var kv in fish)
                r.Objects.Add(new ItemSource { Id = kv.Key, Name = $"Fishing — zone/area #{kv.Key}", Detail = "fished up", Chance = kv.Value });

        if (lootIds.TryGetValue("mail", out var mail))
            foreach (var kv in mail)
                r.Other.Add(new ItemSource { Id = kv.Key, Name = $"Mail template #{kv.Key}", Detail = "sent by mail", Chance = kv.Value });
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Non-loot sources
    // ══════════════════════════════════════════════════════════════════════════

    private static async Task AddVendorsAsync(IDbConnection conn, ItemSourceResult r, int entry)
    {
        try
        {
            var rows = await conn.QueryAsync<NamedRow>(
                "SELECT ct.entry AS Entry, ct.name AS Name FROM npc_vendor nv " +
                "JOIN creature_template ct ON ct.entry = nv.entry " +
                "WHERE nv.item = @E GROUP BY ct.entry, ct.name ORDER BY ct.name",
                new { E = entry });

            foreach (var row in rows)
                r.Vendors.Add(new ItemSource { Id = row.Entry, Name = row.Name ?? $"creature #{row.Entry}", Detail = "sells" });
        }
        catch (Exception ex)
        {
            r.Notes.Add("npc_vendor probe failed: " + Short(ex));
        }

        // Shared vendor lists. The link column between creature_template and
        // npc_vendor_template varies by core revision, so this is a best-effort
        // probe and reports rather than hides a schema mismatch.
        try
        {
            var rows = await conn.QueryAsync<NamedRow>(
                "SELECT ct.entry AS Entry, ct.name AS Name FROM npc_vendor_template nvt " +
                "JOIN creature_template ct ON ct.vendor_id = nvt.entry " +
                "WHERE nvt.item = @E GROUP BY ct.entry, ct.name ORDER BY ct.name",
                new { E = entry });

            var known = new HashSet<int>(r.Vendors.Select(v => v.Id));
            foreach (var row in rows)
                if (known.Add(row.Entry))
                    r.Vendors.Add(new ItemSource { Id = row.Entry, Name = row.Name ?? $"creature #{row.Entry}", Detail = "sells (shared list)" });
        }
        catch (Exception ex)
        {
            r.Notes.Add("npc_vendor_template probe skipped: " + Short(ex));
        }
    }

    private static async Task AddQuestsAsync(IDbConnection conn, ItemSourceResult r, int entry)
    {
        // Rewards — given on turn-in, fixed or chosen.
        try
        {
            var rows = await conn.QueryAsync<QuestRow>(
                "SELECT entry AS Entry, Title AS Title FROM quest_template WHERE " +
                "RewItemId1=@E OR RewItemId2=@E OR RewItemId3=@E OR RewItemId4=@E " +
                "ORDER BY Title", new { E = entry });
            foreach (var row in rows)
                r.Quests.Add(new ItemSource { Id = row.Entry, Name = row.Title ?? $"quest #{row.Entry}", Detail = "quest reward" });

            var choice = await conn.QueryAsync<QuestRow>(
                "SELECT entry AS Entry, Title AS Title FROM quest_template WHERE " +
                "RewChoiceItemId1=@E OR RewChoiceItemId2=@E OR RewChoiceItemId3=@E OR " +
                "RewChoiceItemId4=@E OR RewChoiceItemId5=@E OR RewChoiceItemId6=@E " +
                "ORDER BY Title", new { E = entry });
            var known = new HashSet<int>(r.Quests.Select(q => q.Id));
            foreach (var row in choice)
                if (known.Add(row.Entry))
                    r.Quests.Add(new ItemSource { Id = row.Entry, Name = row.Title ?? $"quest #{row.Entry}", Detail = "quest reward (choice)" });
        }
        catch (Exception ex)
        {
            r.Notes.Add("quest reward probe failed: " + Short(ex));
        }

        // Objective / provided items — why a quest item drops at all.
        try
        {
            var rows = await conn.QueryAsync<QuestRow>(
                "SELECT entry AS Entry, Title AS Title FROM quest_template WHERE " +
                "ReqItemId1=@E OR ReqItemId2=@E OR ReqItemId3=@E OR ReqItemId4=@E OR " +
                "ReqSourceId1=@E OR ReqSourceId2=@E OR ReqSourceId3=@E OR ReqSourceId4=@E " +
                "ORDER BY Title", new { E = entry });
            var known = new HashSet<int>(r.Quests.Select(q => q.Id));
            foreach (var row in rows)
                if (known.Add(row.Entry))
                    r.Quests.Add(new ItemSource { Id = row.Entry, Name = row.Title ?? $"quest #{row.Entry}", Detail = "quest objective" });
        }
        catch (Exception ex)
        {
            r.Notes.Add("quest objective probe failed: " + Short(ex));
        }

        // Items that START a quest.
        try
        {
            var startId = await conn.ExecuteScalarAsync<int?>(
                "SELECT start_quest FROM item_template WHERE entry = @E ORDER BY patch DESC LIMIT 1", new { E = entry });
            if (startId.HasValue && startId.Value > 0)
            {
                var title = await conn.ExecuteScalarAsync<string?>(
                    "SELECT Title FROM quest_template WHERE entry = @Q", new { Q = startId.Value });
                r.Quests.Add(new ItemSource { Id = startId.Value, Name = title ?? $"quest #{startId.Value}", Detail = "this item starts the quest" });
            }
        }
        catch (Exception ex)
        {
            r.Notes.Add("start_quest probe failed: " + Short(ex));
        }
    }

    /// <summary>
    /// Crafting. A create-item spell names its output in effectItemTypeN where
    /// effectN = 24 (SPELL_EFFECT_CREATE_ITEM). The recipe/pattern item is then
    /// whatever item_template teaches or casts that spell.
    /// </summary>
    private static async Task AddCraftingAsync(IDbConnection conn, ItemSourceResult r, int entry)
    {
        List<int> spells;
        try
        {
            spells = (await conn.QueryAsync<int>(
                "SELECT entry FROM spell_template WHERE " +
                "(effect1 = @Create AND effectItemType1 = @E) OR " +
                "(effect2 = @Create AND effectItemType2 = @E) OR " +
                "(effect3 = @Create AND effectItemType3 = @E)",
                new { E = entry, Create = EFFECT_CREATE_ITEM })).Distinct().ToList();
        }
        catch (Exception ex)
        {
            r.Notes.Add("spell_template create-item probe failed: " + Short(ex));
            return;
        }

        if (spells.Count == 0) return;

        // Recipe items: an item that teaches (spelltrigger 6) or casts the spell.
        var recipeBySpell = new Dictionary<int, (int Entry, string Name)>();
        try
        {
            var recipes = await conn.QueryAsync<RecipeRow>(
                "SELECT entry AS Entry, name AS Name, spellid_1 AS S1, spellid_2 AS S2 FROM item_template " +
                "WHERE spellid_1 IN @S OR spellid_2 IN @S GROUP BY entry, name, spellid_1, spellid_2",
                new { S = spells });

            foreach (var row in recipes)
            {
                foreach (var s in new[] { row.S1, row.S2 })
                    if (spells.Contains(s) && !recipeBySpell.ContainsKey(s))
                        recipeBySpell[s] = (row.Entry, row.Name ?? $"item #{row.Entry}");
            }
        }
        catch (Exception ex)
        {
            r.Notes.Add("recipe item lookup failed: " + Short(ex));
        }

        foreach (var spellId in spells)
        {
            if (recipeBySpell.TryGetValue(spellId, out var recipe))
                r.Crafted.Add(new ItemSource { Id = recipe.Entry, Name = recipe.Name, Detail = $"recipe — crafted by spell #{spellId}" });
            else
                r.Crafted.Add(new ItemSource { Id = spellId, Name = $"Crafting spell #{spellId}", Detail = "trainer-taught or no recipe item" });
        }
    }

    // ══════════════════════════════════════════════════════════════════════════

    private static void Trim(List<ItemSource> list)
    {
        // De-dup on id+detail, keeping the best chance seen, then cap.
        var seen = new Dictionary<string, ItemSource>();
        foreach (var s in list)
        {
            var key = s.Id + "|" + (s.Detail ?? "") + "|" + s.Name;
            if (seen.TryGetValue(key, out var prev))
            {
                if ((s.Chance ?? 0) > (prev.Chance ?? 0)) prev.Chance = s.Chance;
            }
            else seen[key] = s;
        }

        var ordered = seen.Values
            .OrderByDescending(s => s.Chance ?? -1)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        list.Clear();
        list.AddRange(ordered.Take(MaxPerBucket));
        if (ordered.Count > MaxPerBucket)
            list.Add(new ItemSource { Id = 0, Name = $"...and {ordered.Count - MaxPerBucket} more" });
    }

    private static string Short(Exception ex)
    {
        var msg = ex.Message ?? ex.GetType().Name;
        return msg.Length > 180 ? msg.Substring(0, 180) + "..." : msg;
    }
}
