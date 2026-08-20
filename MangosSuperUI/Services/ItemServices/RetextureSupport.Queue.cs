// RetextureSupport.Queue.cs
//
// The lootifier retexture QUEUE, moved off ItemsController and onto the Retexture
// Engine. Partial half of RetextureSupport (the other half — recolor, commit,
// browse — is in RetextureSupport.cs).
//
// WHY THIS FILE EXISTS
// --------------------
// The Retexture Engine section used to borrow /Items/LootifierRetextureSources,
// BuildRetextureQueue, RetextureQueueStatus, ResetRetextureQueue and
// RebuildRetexturePatch. Those endpoints know nothing about `source`, so:
//
//   * you could BUILD a queue for one lootifier but never RUN, RESET or UNDO one;
//   * "Clear" deleted queue rows and left every applied retexture in place;
//   * a re-run minted a BRAND NEW display id each time and orphaned the previous
//     one in custom_item_retexture[_atlas], where RebuildPatchM kept packing it
//     into patch-4.MPQ. Re-running looked like it ADDED and never undid.
//
// Everything here is scoped by `source` ("quest" | "crafting" | "loot"), null
// meaning all. Three verbs that did not exist before:
//
//   ResetQueueAsync(src, "all")      re-arm done rows       -> re-retexture
//   RevertQueueAsync(src, ...)       put base_display_id back -> real undo
//   PurgeOrphansAsync(apply)         delete unreferenced minted displays
//
// The undo key was always in the table: lootifier_retexture_queue.base_display_id
// is the pre-retexture display of every variant in the job. Nothing read it.
//
// ItemsController keeps its own copies of these endpoints, untouched, for the old
// Items UI. Nothing here calls them.

using Dapper;

namespace MangosSuperUI.Services;

public partial class RetextureSupport
{
    // ── Sources ─────────────────────────────────────────────────────────────
    // The three lootifiers share lootifier_generated_items and are told apart by
    // the creature_entry sentinel: quest = 0, crafting = -1, loot/ARPG = a real
    // creature entry. Same contract the lootifiers write with.

    public static readonly string[] LootifierSources = { "quest", "crafting", "loot" };

    public static string SourceLabel(string source) => source switch
    {
        "quest" => "Quest Rewards",
        "crafting" => "Crafted Items",
        "loot" => "Loot / ARPG",
        _ => source
    };

    private static string SourceFilterSql(string source) => source switch
    {
        "quest" => "gi.creature_entry = 0",
        "crafting" => "gi.creature_entry = -1",
        "loot" => "gi.creature_entry > 0",
        _ => "1=0"
    };

    /// <summary>null (= every source) for anything unrecognised or blank.</summary>
    public static string? NormalizeSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;
        var s = source.Trim().ToLowerInvariant();
        return Array.IndexOf(LootifierSources, s) >= 0 ? s : null;
    }

    /// <summary>
    /// Canonical colour tier for a tracked variant. Mirrors the lootifiers'
    /// CanonicalTier so the grouping here matches the ladder they applied.
    /// </summary>
    private static string CanonicalTierOf(string? tierName, float budgetPct)
    {
        var l = (tierName ?? "").ToLowerInvariant();
        if (l.Contains("god") || l.Contains("legend") || l.Contains("immortal") || l.Contains("azeroth")) return "gods";
        if (l.Contains("glory") || l.Contains("fury")) return "glory";
        if (l.Contains("power")) return "power";
        if (l.Contains("improv")) return "improved";
        if (budgetPct >= 98f) return "gods";
        if (budgetPct >= 90f) return "glory";
        if (budgetPct >= 80f) return "power";
        return "improved";
    }

    private static async Task<bool> TableExistsAsync(MySqlConnector.MySqlConnection conn, string table) =>
        await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @T",
            new { T = table }) > 0;

    // ── Sources panel ───────────────────────────────────────────────────────

    /// <summary>
    /// Per-lootifier counts for the batch panel: how much there is to retexture,
    /// and how far the queue has got with it. Adds pending/failed/reverted to what
    /// the old /Items/ version returned, so the UI can label the scope buttons.
    /// </summary>
    public async Task<object> LootifierSourcesAsync()
    {
        using var admin = _db.Admin();
        await EnsureRetextureQueueTable(admin);

        if (!await TableExistsAsync(admin, "lootifier_generated_items"))
            return new { success = true, sources = Array.Empty<object>(), note = "No lootifier data yet" };

        var list = new List<object>();
        foreach (var src in LootifierSources)
        {
            var stats = await admin.QueryFirstOrDefaultAsync<dynamic>($@"
                SELECT COUNT(DISTINCT gi.base_entry) AS bases, COUNT(*) AS variants
                FROM lootifier_generated_items gi
                WHERE {SourceFilterSql(src)}");

            var q = (await admin.QueryAsync<dynamic>(
                "SELECT status, COUNT(*) AS n FROM lootifier_retexture_queue WHERE source = @S GROUP BY status",
                new { S = src })).ToList();

            int Count(string status) => q.Where(r => (string)r.status == status)
                                         .Select(r => (int)(long)r.n).FirstOrDefault();

            int pending = Count("pending"), done = Count("done"),
                failed = Count("failed"), reverted = Count("reverted");

            list.Add(new
            {
                source = src,
                label = SourceLabel(src),
                bases = stats != null ? (int)(long)stats.bases : 0,
                variants = stats != null ? (int)(long)stats.variants : 0,
                queued = pending + done + failed + reverted,
                pending,
                done,
                failed,
                reverted
            });
        }

        return new { success = true, sources = list };
    }

    // ── Build ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Scan the selected lootifier sources and queue ONE recolor per
    /// (base item x colour tier). Every variant in a tier shares the resulting
    /// display_id, so a base with 10 variants queues at most 4 jobs.
    ///
    /// requeue=true replaces rows that already exist for a (source, base, tier) —
    /// that is how you pick up a base whose variants changed. To re-run jobs that
    /// are already DONE without rebuilding the queue, use ResetQueueAsync(src,
    /// "all") instead; it keeps new_display_id so the old display gets recycled.
    ///
    /// No `themes`: the seeded engine derives the colourway from (base x tier) and
    /// DefaultTierTheme has been "" since the rarity-colour switch was removed.
    /// </summary>
    public async Task<object> BuildQueueAsync(List<string> sources, bool requeue)
    {
        var srcs = (sources ?? new List<string>())
            .Select(NormalizeSource).Where(s => s != null).Select(s => s!).Distinct().ToList();
        if (srcs.Count == 0) return new { success = false, error = "No sources selected" };

        using var admin = _db.Admin();
        using var mangos = _db.Mangos();
        await EnsureRetextureQueueTable(admin);

        if (!await TableExistsAsync(admin, "lootifier_generated_items"))
            return new { success = false, error = "No lootifier data found" };

        int queued = 0, skipped = 0, basesCovered = 0, noDisplay = 0, ineligible = 0;

        foreach (var src in srcs)
        {
            var tracked = (await admin.QueryAsync<dynamic>($@"
                SELECT gi.base_entry, gi.generated_entry, gi.tier_name, gi.budget_pct
                FROM lootifier_generated_items gi
                WHERE {SourceFilterSql(src)}")).ToList();
            if (tracked.Count == 0) continue;

            // Existing keys for this source, so a rebuild doesn't duplicate rows.
            var existing = new HashSet<string>((await admin.QueryAsync<dynamic>(
                "SELECT base_entry, tier FROM lootifier_retexture_queue WHERE source = @S", new { S = src }))
                .Select(r => $"{(int)r.base_entry}|{(string)r.tier}"));

            var baseEntries = tracked.Select(t => (int)t.base_entry).Distinct().ToList();

            // Base display_id / name / inventory_type live in the world DB — a
            // separate query, not a join (different databases).
            var baseInfo = new Dictionary<int, (string name, int displayId, int invType)>();
            foreach (var r in await mangos.QueryAsync<dynamic>(@"
                SELECT entry, name, display_id, inventory_type FROM item_template
                WHERE entry IN @E AND patch = (SELECT MAX(patch) FROM item_template it2 WHERE it2.entry = item_template.entry)",
                new { E = baseEntries }))
                baseInfo[(int)r.entry] = ((string)r.name, (int)(uint)r.display_id, Convert.ToInt32(r.inventory_type));

            foreach (var grp in tracked.GroupBy(t => (int)t.base_entry))
            {
                int baseEntry = grp.Key;
                if (!baseInfo.TryGetValue(baseEntry, out var info)) continue;
                var (itemName, displayId, invType) = info;
                if (displayId == 0) { noDisplay++; continue; }

                // Necks, rings, trinkets, bags, ammo, quivers and relics have a
                // display_id but no texture any system can reach. Queuing them just
                // manufactures "No textures found" failures — skip at the source.
                if (KindForInventoryType(invType) == KIND_NONE) { ineligible++; continue; }

                basesCovered++;

                foreach (var tg in grp.GroupBy(t => CanonicalTierOf((string?)t.tier_name, (float)t.budget_pct)))
                {
                    string tier = tg.Key;
                    if (existing.Contains($"{baseEntry}|{tier}"))
                    {
                        if (!requeue) { skipped++; continue; }
                        // Replacing the row drops new_display_id, so the display it
                        // minted would leak. Hand it back first.
                        foreach (var old in await admin.QueryAsync<int>(@"
                            SELECT new_display_id FROM lootifier_retexture_queue
                            WHERE source=@S AND base_entry=@B AND tier=@T AND new_display_id > 0",
                            new { S = src, B = baseEntry, T = tier }))
                            await PurgeCustomDisplayAsync(admin, mangos, old);

                        await admin.ExecuteAsync(
                            "DELETE FROM lootifier_retexture_queue WHERE source=@S AND base_entry=@B AND tier=@T",
                            new { S = src, B = baseEntry, T = tier });
                    }

                    await admin.ExecuteAsync(@"
                        INSERT INTO lootifier_retexture_queue
                            (source, base_entry, base_display_id, item_name, tier, variant_entries,
                             theme, instruction, status, created_at)
                        VALUES (@S, @B, @Did, @Name, @Tier, @Entries, '', '', 'pending', NOW())",
                        new
                        {
                            S = src,
                            B = baseEntry,
                            Did = displayId,
                            Name = itemName.Length > 255 ? itemName.Substring(0, 255) : itemName,
                            Tier = tier,
                            Entries = string.Join(",", tg.Select(x => (int)x.generated_entry).Distinct())
                        });
                    queued++;
                }
            }
        }

        return new { success = true, queued, skipped, basesCovered, noDisplay, ineligible, sources = srcs };
    }

    // ── Status ──────────────────────────────────────────────────────────────

    /// <summary>Queue counts + recent failures, scoped to one source or all.</summary>
    public async Task<object> QueueStatusAsync(string? source)
    {
        source = NormalizeSource(source);
        using var admin = _db.Admin();
        await EnsureRetextureQueueTable(admin);

        var rows = (await admin.QueryAsync<dynamic>(@"
            SELECT status, COUNT(*) AS n FROM lootifier_retexture_queue
            WHERE (@Src IS NULL OR source = @Src) GROUP BY status", new { Src = source })).ToList();

        int Count(string status) => rows.Where(r => (string)r.status == status)
                                        .Select(r => (int)(long)r.n).FirstOrDefault();

        var failures = (await admin.QueryAsync<dynamic>(@"
            SELECT base_entry, item_name, tier, error FROM lootifier_retexture_queue
            WHERE status = 'failed' AND (@Src IS NULL OR source = @Src)
            ORDER BY id DESC LIMIT 20", new { Src = source })).ToList();

        return new
        {
            success = true,
            source,
            pending = Count("pending"),
            done = Count("done"),
            failed = Count("failed"),
            reverted = Count("reverted"),
            failures = failures.Select(f => new
            {
                baseEntry = (int)f.base_entry,
                itemName = (string)f.item_name,
                tier = (string)f.tier,
                error = (string?)f.error
            })
        };
    }

    // ── Reset / re-arm ──────────────────────────────────────────────────────

    /// <summary>
    /// mode = "failed"  requeue this source's failures (the old behaviour);
    ///        "all"     re-arm every done/failed/reverted row -> RE-RETEXTURE.
    ///                  new_display_id is kept so ProcessQueueAsync recycles the
    ///                  display it minted last time instead of orphaning it;
    ///        "clear"   delete this source's rows. Applied retextures STAY
    ///                  applied — clearing the queue is not an undo. Use
    ///                  RevertQueueAsync for that.
    /// </summary>
    public async Task<object> ResetQueueAsync(string? source, string mode)
    {
        source = NormalizeSource(source);
        mode = (mode ?? "failed").Trim().ToLowerInvariant();
        using var admin = _db.Admin();
        await EnsureRetextureQueueTable(admin);

        int affected = mode switch
        {
            "clear" => await admin.ExecuteAsync(
                "DELETE FROM lootifier_retexture_queue WHERE (@Src IS NULL OR source = @Src)",
                new { Src = source }),

            "all" => await admin.ExecuteAsync(@"
                UPDATE lootifier_retexture_queue
                SET status='pending', error=NULL, processed_at=NULL
                WHERE status <> 'pending' AND (@Src IS NULL OR source = @Src)",
                new { Src = source }),

            _ => await admin.ExecuteAsync(@"
                UPDATE lootifier_retexture_queue
                SET status='pending', error=NULL, processed_at=NULL
                WHERE status = 'failed' AND (@Src IS NULL OR source = @Src)",
                new { Src = source })
        };

        return new { success = true, affected, mode, source };
    }

    // ── Revert (the real undo) ──────────────────────────────────────────────

    /// <summary>
    /// Put every variant of this source back on its ORIGINAL display
    /// (base_display_id), delete the displays that were minted for it, and rebuild
    /// patch-4.MPQ so the archive stops carrying them.
    ///
    /// The item_template update is guarded on display_id = new_display_id, so a
    /// variant that has since been retextured by something else is left alone.
    ///
    /// requeue=true leaves the rows 'pending' (undo, then run again from clean);
    /// false parks them as 'reverted' — inert, and still counted as existing by
    /// BuildQueueAsync so a plain rebuild won't silently re-add them.
    /// </summary>
    public async Task<object> RevertQueueAsync(string? source, bool requeue)
    {
        source = NormalizeSource(source);
        using var admin = _db.Admin();
        using var mangos = _db.Mangos();
        await EnsureRetextureQueueTable(admin);

        var rows = (await admin.QueryAsync<RetextureJobRow>(@"
            SELECT * FROM lootifier_retexture_queue
            WHERE new_display_id > 0 AND (@Src IS NULL OR source = @Src) ORDER BY id",
            new { Src = source })).ToList();

        int reverted = 0, itemsRestored = 0, displaysPurged = 0, skipped = 0;

        foreach (var row in rows)
        {
            if (row.base_display_id <= 0) { skipped++; continue; }

            var entries = ParseEntryCsv(row.variant_entries);
            if (entries.Count > 0)
                itemsRestored += await mangos.ExecuteAsync(@"
                    UPDATE item_template SET display_id = @Base
                    WHERE entry IN @E AND display_id = @New",
                    new { Base = row.base_display_id, E = entries, New = row.new_display_id });

            if (await PurgeCustomDisplayAsync(admin, mangos, row.new_display_id) > 0) displaysPurged++;

            await admin.ExecuteAsync(@"
                UPDATE lootifier_retexture_queue
                SET status = @St, new_display_id = 0, error = NULL, processed_at = NULL
                WHERE id = @Id",
                new { St = requeue ? "pending" : "reverted", Id = row.id });
            reverted++;
        }

        var rb = reverted > 0 ? await _retexture.RebuildPatchMAsync() : null;
        return new
        {
            success = true,
            source,
            reverted,
            itemsRestored,
            displaysPurged,
            skipped,
            requeued = requeue,
            patchRebuilt = rb?.Success ?? false,
            patchError = rb?.Error,
            mpqFiles = rb?.MpqFileCount ?? 0
        };
    }

    // ── Orphan sweep ────────────────────────────────────────────────────────

    /// <summary>
    /// Every minted display that nothing in item_template points at any more —
    /// the accumulated debris of re-runs and rolled-back lootifier items. apply
    /// =false counts them (dry run), true deletes and rebuilds the patch.
    /// </summary>
    public async Task<object> PurgeOrphansAsync(bool apply)
    {
        using var admin = _db.Admin();
        using var mangos = _db.Mangos();

        var minted = (await admin.QueryAsync<long>(@"
            SELECT DISTINCT new_display_id FROM custom_item_retexture WHERE new_display_id > 0
            UNION
            SELECT DISTINCT new_display_id FROM custom_item_retexture_atlas WHERE new_display_id > 0"))
            .Select(v => (int)v).ToList();

        if (minted.Count == 0)
            return new { success = true, minted = 0, orphans = 0, deleted = 0, applied = apply };

        var used = new HashSet<int>((await mangos.QueryAsync<long>(
            "SELECT DISTINCT display_id FROM item_template WHERE display_id IN @E", new { E = minted }))
            .Select(v => (int)v));

        var orphans = minted.Where(d => !used.Contains(d)).ToList();

        int deleted = 0;
        if (apply)
            foreach (var d in orphans)
                deleted += await PurgeCustomDisplayAsync(admin, mangos, d);

        var rb = (apply && deleted > 0) ? await _retexture.RebuildPatchMAsync() : null;
        return new
        {
            success = true,
            minted = minted.Count,
            orphans = orphans.Count,
            deleted,
            applied = apply,
            sample = orphans.Take(20),
            patchRebuilt = rb?.Success ?? false,
            patchError = rb?.Error,
            mpqFiles = rb?.MpqFileCount ?? 0
        };
    }

    /// <summary>
    /// Delete a minted display's BLP rows — but only if it is genuinely custom
    /// (>= CUSTOM_DISPLAY_BASE) and nothing in item_template still points at it.
    /// Returns rows deleted across both tables.
    /// </summary>
    private async Task<int> PurgeCustomDisplayAsync(
        MySqlConnector.MySqlConnection admin, MySqlConnector.MySqlConnection mangos, int newDisplayId)
    {
        if (newDisplayId < (int)ItemRetextureService.CUSTOM_DISPLAY_BASE) return 0;

        int stillUsed = await mangos.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM item_template WHERE display_id = @D", new { D = newDisplayId });
        if (stillUsed > 0) return 0;

        int n = await admin.ExecuteAsync(
            "DELETE FROM custom_item_retexture WHERE new_display_id = @D", new { D = newDisplayId });
        n += await admin.ExecuteAsync(
            "DELETE FROM custom_item_retexture_atlas WHERE new_display_id = @D", new { D = newDisplayId });

        if (n > 0) _logger.LogInformation("Retexture: purged custom display {Did} ({N} rows)", newDisplayId, n);
        return n;
    }

    // ── The artifact itself ─────────────────────────────────────────────────
    //
    // The Retexture Engine is what BUILDS patch-4.MPQ, so it should also be where
    // you pick it up. wwwroot is wiped on every redeploy while the retextures live
    // in the DB, so a missing file is not an error — it is a rebuild trigger.

    public const string PatchFileName = "patch-4.MPQ";

    /// <summary>
    /// Absolute path to a built patch, rebuilding it from the DB first if wwwroot
    /// has been wiped. build=false (HEAD probes) skips the rebuild.
    /// </summary>
    public async Task<(string? Path, string FileName, string? Error)> EnsurePatchFileAsync(
        string? file = null, bool build = true)
    {
        string name = string.IsNullOrWhiteSpace(file) ? PatchFileName : System.IO.Path.GetFileName(file);
        string full = System.IO.Path.Combine(_env.WebRootPath, "patches", "retexture", name);

        if (!System.IO.File.Exists(full) && build
            && string.Equals(name, PatchFileName, StringComparison.OrdinalIgnoreCase))
            await _retexture.EnsurePatchBuiltAsync();

        return System.IO.File.Exists(full)
            ? (full, name, null)
            : (null, name, $"Patch '{name}' not found and could not be built");
    }

    /// <summary>
    /// Whether a patch can be produced (DB-based, so it survives a redeploy) plus
    /// what is currently on disk, for the download button's label.
    /// </summary>
    public async Task<object> PatchStatusAsync()
    {
        bool available = await _retexture.HasAnyRetexturesAsync();
        string full = System.IO.Path.Combine(_env.WebRootPath, "patches", "retexture", PatchFileName);
        var fi = System.IO.File.Exists(full) ? new FileInfo(full) : null;

        return new
        {
            success = true,
            available,
            fileName = PatchFileName,
            onDisk = fi != null,
            sizeBytes = fi?.Length ?? 0L,
            sizeMb = fi != null ? Math.Round(fi.Length / 1048576.0, 1) : 0.0,
            builtUtc = fi?.LastWriteTimeUtc,
            url = "/RetextureEngine/DownloadPatch"
        };
    }

    /// <summary>Force a patch-4.MPQ rebuild from the retexture tables.</summary>
    public async Task<object> RebuildPatchAsync()
    {
        var rb = await _retexture.RebuildPatchMAsync();
        return new
        {
            success = rb.Success,
            error = rb.Error,
            patchUrl = rb.PatchWebPath,
            entries = rb.TotalEntries,
            mpqFiles = rb.MpqFileCount
        };
    }
}
