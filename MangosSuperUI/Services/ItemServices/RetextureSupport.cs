// RetextureSupport.cs
//
// Shared retexture helpers, extracted from ItemsController so the new
// RetextureEngineController can drive the seeded / tier / VALUE recolor pipeline
// without duplicating the subtle source-resolution logic (TextureName1 vs baked
// M2 textures vs env-maps) or the seed/tier maths.
//
// This service is the source of truth going forward. ItemsController still
// carries its own private copies for now (left untouched deliberately);
// converge it onto this service once the Retexture Engine section is proven.
//
// DI: register in Program.cs  ->  builder.Services.AddScoped<RetextureSupport>();
//
// NOTE: ItemTextureEntry is whatever type ItemTextureService returns in its
// texture list. If it does not resolve here, add the using for its namespace
// (the same one ItemsController imports for it).

using Dapper;
using MangosSuperUI.Models;

namespace MangosSuperUI.Services;

public class RetextureSupport
{
    private readonly ItemTextureService _itemTextures;
    private readonly DbcService _dbc;
    private readonly BodyAtlasTextureService _bodyAtlas;
    private readonly PaletteSwapService _palette;
    private readonly ItemRetextureService _retexture;
    private readonly ConnectionFactory _db;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<RetextureSupport> _logger;

    public RetextureSupport(
        ItemTextureService itemTextures, DbcService dbc,
        BodyAtlasTextureService bodyAtlas, PaletteSwapService palette,
        ItemRetextureService retexture, ConnectionFactory db,
        IWebHostEnvironment env, ILogger<RetextureSupport> logger)
    {
        _itemTextures = itemTextures;
        _dbc = dbc;
        _bodyAtlas = bodyAtlas;
        _palette = palette;
        _retexture = retexture;
        _db = db;
        _env = env;
        _logger = logger;
    }

    // ── Retextureability kind (mirrors ItemsController) ─────────────────────
    public const string KIND_ATLAS = "atlas";   // paints m_texture[0..7] into the body atlas
    public const string KIND_MODEL = "model";   // own M2 + texture
    public const string KIND_CAPE = "cape";    // no M2, no atlas — ObjectComponents\\Cape\\
    public const string KIND_NONE = "none";    // no visual representation at all

    public static string KindForInventoryType(int invType) => invType switch
    {
        4 or 5 or 6 or 7 or 8 or 9 or 10 or 19 or 20 => KIND_ATLAS,
        16 => KIND_CAPE,
        1 or 3 or 13 or 14 or 15 or 17 or 21 or 22 or 23 or 25 or 26 => KIND_MODEL,
        _ => KIND_NONE,
    };

    public static string ObjectComponentSubdir(int invType) => invType switch
    {
        1 => "Head",
        3 => "Shoulder",
        14 => "Shield",
        _ => "Weapon",
    };

    // ── Seed + tier maths (mirrors ItemsController) ─────────────────────────
    /// <summary>FNV-1a stable seed for (base item, tier). NOT GetHashCode (per-process randomized).</summary>
    public static int SeedFor(int baseEntry, string tier)
    {
        unchecked
        {
            uint h = 2166136261u;
            h = (h ^ (uint)baseEntry) * 16777619u;
            h = (h ^ (uint)(baseEntry >> 16)) * 16777619u;
            foreach (char c in tier ?? "") h = (h ^ c) * 16777619u;
            return (int)h;
        }
    }

    /// <summary>RETIRED as the tier axis (kept for themed-path compat). TierShape owns the ladder.</summary>
    public static (float satScale, float lightBias) TierIntensity(string tier) => tier switch
    {
        "improved" => (1.00f, 0.00f),
        "power" => (1.15f, 0.03f),
        "glory" => (1.30f, 0.06f),
        "gods" => (1.50f, 0.10f),
        _ => (1.00f, 0.00f),
    };

    /// <summary>Tier as VALUE STRUCTURE — the post-tent stage's kd/ku/m/pop. Pass WITH satScale=1, lightBias=0.</summary>
    public static (float kd, float ku, float m, float pop) TierShape(string tier) => tier switch
    {
        "improved" => (0.00f, 0.00f, 0.00f, 0.00f),
        "power" => (0.05f, 0.25f, 0.20f, 0.02f),
        "glory" => (0.09f, 0.50f, 0.45f, 0.05f),
        "gods" => (0.13f, 0.85f, 0.80f, 0.10f),
        _ => (0.00f, 0.00f, 0.00f, 0.00f),
    };

    /// <summary>Progressive tier policy — swapBudget (cumulative pixel share) + hueLeash (hue roll cap).</summary>
    public static (float swapBudget, float hueLeash) TierPolicy(string tier) => tier switch
    {
        "improved" => (0.20f, 40f),
        "power" => (0.40f, 120f),
        "glory" => (0.70f, 180f),
        "gods" => (1.01f, 180f),
        _ => (1.01f, 180f),
    };

    // ── Source resolution ───────────────────────────────────────────────────
    /// <summary>
    /// Resolve the item's PRIMARY recolor source PNG on disk: largest atlas slot
    /// for painted armor, else the DBC-controlled model texture. Mirrors
    /// TheorySheet's resolution. Returns (null, error) when nothing is resolvable.
    /// </summary>
    public async Task<(string? SrcPng, string? Error)> ResolvePrimarySourceAsync(
        uint displayId, CancellationToken ct = default)
    {
        var atlas = await _bodyAtlas.EnsureAtlasTexturesAsync(displayId);
        if (atlas != null && atlas.SlotUrls.Count > 0)
        {
            var best = atlas.SlotUrls.OrderByDescending(kv =>
            {
                var pth = Path.Combine(_env.WebRootPath,
                    kv.Value.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                return System.IO.File.Exists(pth) ? new FileInfo(pth).Length : 0;
            }).First();
            var srcPng = Path.Combine(_env.WebRootPath,
                best.Value.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            if (System.IO.File.Exists(srcPng)) return (srcPng, null);
        }

        // Model item: the DBC-controlled M2 texture.
        var (tex, previewPath, err) = ResolveTargetTexture(displayId, "", "");
        if (tex != null && previewPath != null && System.IO.File.Exists(previewPath))
            return (previewPath, null);

        // Object-component BLP (weapons / helms / shoulders / shields) — the SAME
        // fallback the commit path uses when TextureName1 isn't one of the M2's
        // textures (e.g. a sabre whose BLP lives under Item\ObjectComponents\Weapon\).
        // Without this the preview wrongly reports "the DBC does not control this
        // texture" for items the batch retextures fine.
        foreach (var subdir in new[] { "Weapon", "Head", "Shoulder", "Shield" })
        {
            var oc = _itemTextures.GetObjectComponentTexture(displayId, subdir);
            if (oc != null && !string.IsNullOrEmpty(oc.PreviewPngPath))
            {
                var p = Path.Combine(_env.WebRootPath,
                    oc.PreviewPngPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                if (System.IO.File.Exists(p)) return (p, null);
            }
        }

        // Cape.
        var cape = _itemTextures.GetCapeTexture(displayId);
        if (cape != null && !string.IsNullOrEmpty(cape.PreviewPngPath))
        {
            var cp = Path.Combine(_env.WebRootPath,
                cape.PreviewPngPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            if (System.IO.File.Exists(cp)) return (cp, null);
        }

        return (null, err ?? "no resolvable source texture for this display");
    }

    private string DiskOf(string webUrl) => Path.Combine(_env.WebRootPath,
        webUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

    // ── Retexture-to-asset (for the 3D viewer overlay) ──────────────────────
    /// <summary>
    /// Recolor EVERY body-atlas slot with ONE seed (so the whole piece recolors
    /// coherently) and return slot -> recolored web URL, ready for
    /// equip.equipBodyAtlasRetextureDirect. Mirrors GenerateBodyAtlasVariations,
    /// but seeded (theory + tier + value) instead of an LLM recipe. null when the
    /// display has no atlas (i.e. it is not painted armor).
    /// </summary>
    public async Task<Dictionary<int, string>?> RecolorAtlasSlotsAsync(
        uint displayId, int seed, string theory,
        (float kd, float ku, float m, float pop) shape,
        (float budget, float leash) policy,
        ValueSettings value, string outSubdir, CancellationToken ct)
    {
        var atlas = await _bodyAtlas.EnsureAtlasTexturesAsync(displayId);
        if (atlas == null || atlas.SlotUrls.Count == 0) return null;

        var outDir = Path.Combine(_env.WebRootPath, "item_textures_cache", outSubdir);
        Directory.CreateDirectory(outDir);

        var recolored = new Dictionary<int, string>();
        foreach (var (slot, webUrl) in atlas.SlotUrls)
        {
            string srcDisk = DiskOf(webUrl);
            if (!System.IO.File.Exists(srcDisk)) continue;
            string outName = $"model_{displayId}_s{slot}_{Guid.NewGuid():N}.png";
            string outPng = Path.Combine(outDir, outName);
            var ok = await _palette.RecolorSeededAsync(
                srcDisk, outPng, seed, 1.0f, 0.0f, false, ct,
                theory, shape.kd, shape.ku, shape.m, shape.pop, policy.budget, policy.leash, value);
            if (ok == null) continue;
            recolored[slot] = $"/item_textures_cache/{outSubdir}/{outName}";
        }
        return recolored.Count > 0 ? recolored : null;
    }

    /// <summary>
    /// Recolor the model's DBC-controlled texture with the seeded recipe and bake
    /// a preview GLB; returns its web URL for equip.equipWeaponGlbDirect. The
    /// recolor is deterministic and pixel-sharp, so we skip the upscaler cleanup
    /// pass (BuildPreviewResponse's skipCleanup path). null when the display has
    /// no recolorable model texture.
    /// </summary>
    public async Task<string?> RecolorModelGlbAsync(
        uint displayId, int seed, string theory,
        (float kd, float ku, float m, float pop) shape,
        (float budget, float leash) policy,
        ValueSettings value, string outSubdir, CancellationToken ct)
    {
        var (previewPath, _) = await ResolvePrimarySourceAsync(displayId, ct);
        if (previewPath == null || !System.IO.File.Exists(previewPath)) return null;

        var outDir = Path.Combine(_env.WebRootPath, "item_textures_cache", outSubdir);
        Directory.CreateDirectory(outDir);
        string outPng = Path.Combine(outDir, $"model_{displayId}_{Guid.NewGuid():N}.png");

        var ok = await _palette.RecolorSeededAsync(
            previewPath, outPng, seed, 1.0f, 0.0f, false, ct,
            theory, shape.kd, shape.ku, shape.m, shape.pop, policy.budget, policy.leash, value);
        if (ok == null) return null;

        return _itemTextures.BuildPreviewGlb(displayId, outPng);
    }

    // ══ Commit-to-DB retexture (self-contained; SEEDED path only) ═══════════
    // The Retexture Engine section drives these. ItemsController keeps its own
    // single-item + lootifier-queue copies untouched, to be converged here later.
    // Seeded only: no LLM recipe branch and no upscaler pass — the recolor is
    // deterministic and pixel-exact, so the raw recolor IS the committed texture.

    private class RetextureJobRow
    {
        public int id { get; set; }
        public string source { get; set; } = "";
        public int base_entry { get; set; }
        public int base_display_id { get; set; }
        public string item_name { get; set; } = "";
        public string tier { get; set; } = "";
        public string variant_entries { get; set; } = "";
        public string theme { get; set; } = "";
        public string instruction { get; set; } = "";
        public string status { get; set; } = "";
        public int new_display_id { get; set; }
        public string? error { get; set; }
    }

    private static async Task EnsureRetextureQueueTable(MySqlConnector.MySqlConnection adminConn)
    {
        await adminConn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS lootifier_retexture_queue (
                id INT AUTO_INCREMENT PRIMARY KEY,
                source VARCHAR(24) NOT NULL DEFAULT 'quest',
                base_entry INT NOT NULL,
                base_display_id INT NOT NULL,
                item_name VARCHAR(255) NOT NULL DEFAULT '',
                tier VARCHAR(32) NOT NULL,
                variant_entries TEXT NOT NULL,
                theme VARCHAR(128) NOT NULL DEFAULT '',
                instruction VARCHAR(512) NOT NULL DEFAULT '',
                status VARCHAR(16) NOT NULL DEFAULT 'pending',
                new_display_id INT NOT NULL DEFAULT 0,
                error VARCHAR(512) NULL,
                created_at DATETIME NOT NULL,
                processed_at DATETIME NULL,
                INDEX idx_status (status),
                INDEX idx_base (base_entry)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
    }

    private static List<int> ParseEntryCsv(string csv) =>
        (csv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var v) ? v : 0)
            .Where(v => v > 0)
            .Distinct()
            .ToList();

    /// <summary>
    /// Recolor + commit ONE item at a tier (seeded), routing by inventory_type
    /// exactly like the queue: atlas (all slots, one seed) -> CommitBodyAtlasAsync;
    /// model/cape (single BLP) -> RetextureFromBitmapAsync. seedOverride shares a
    /// colourway across a set. rebuildPatch is deferred to the caller (once).
    /// </summary>
    public async Task<(bool ok, string? err, uint newDid)> ProcessJobAsync(
        int baseEntry, uint baseDid, int invType, string tier, string itemName,
        string theory, ValueSettings value, int? seedOverride, CancellationToken ct)
    {
        if (baseDid == 0) return (false, "base item has no display_id", 0);

        int seed = seedOverride ?? SeedFor(baseEntry, tier);
        var (kd, ku, m, pop) = TierShape(tier);
        var (budget, leash) = TierPolicy(tier);
        string instruction = $"seeded:{seed} shape=({kd:F2},{ku:F2},{m:F2},{pop:F2}) policy=({budget:F2},{leash:F0})"
            + (value.IsInvert ? " value=invert" : "");

        var outDir = Path.Combine(_env.WebRootPath, "item_textures_cache", "retexture_engine_commit");
        Directory.CreateDirectory(outDir);

        switch (KindForInventoryType(invType))
        {
            case KIND_ATLAS:
                {
                    var atlas = await _bodyAtlas.EnsureAtlasTexturesAsync(baseDid);
                    if (atlas == null || atlas.SlotUrls.Count == 0)
                        return (false, $"painted armor (invType {invType}) but no body-atlas slots for display {baseDid}", 0);

                    var slotPngPaths = new Dictionary<int, string>();
                    foreach (var kv in atlas.SlotUrls)
                    {
                        string srcDisk = DiskOf(kv.Value);
                        if (!System.IO.File.Exists(srcDisk)) continue;
                        string outPng = Path.Combine(outDir, $"ba_{baseDid}_{tier}_s{kv.Key}_{Guid.NewGuid():N}.png");
                        var okp = await _palette.RecolorSeededAsync(srcDisk, outPng, seed, 1.0f, 0.0f, false, ct,
                            theory, kd, ku, m, pop, budget, leash, value);
                        if (okp != null) slotPngPaths[kv.Key] = outPng;
                    }
                    if (slotPngPaths.Count == 0) return (false, "body-atlas recolor produced no slots", 0);

                    var res = await _retexture.CommitBodyAtlasAsync(baseDid, itemName, instruction, slotPngPaths, ct, rebuildPatch: false);
                    return res.Success ? (true, null, (uint)res.NewDisplayId) : (false, res.Error ?? "body-atlas commit failed", 0);
                }

            case KIND_CAPE:
                {
                    var cape = _itemTextures.GetCapeTexture(baseDid);
                    if (cape == null || string.IsNullOrEmpty(cape.PreviewPngPath))
                        return (false, $"cloak: no BLP/preview under Item\\ObjectComponents\\Cape\\ for display {baseDid}", 0);
                    string capePreview = Path.Combine(_env.WebRootPath,
                        cape.PreviewPngPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                    if (!System.IO.File.Exists(capePreview)) return (false, "cloak: preview PNG missing on disk", 0);
                    return await CommitSingleAsync(baseDid, itemName, cape, capePreview, seed, theory, kd, ku, m, pop, budget, leash, value, instruction, outDir, ct);
                }

            case KIND_MODEL:
                {
                    var (tex, previewPath, terr) = ResolveTargetTexture(baseDid, "", "");
                    if (tex != null && !string.IsNullOrEmpty(previewPath))
                        return await CommitSingleAsync(baseDid, itemName, tex, previewPath!, seed, theory, kd, ku, m, pop, budget, leash, value, instruction, outDir, ct);

                    string subdir = ObjectComponentSubdir(invType);
                    var oc = _itemTextures.GetObjectComponentTexture(baseDid, subdir);
                    if (oc == null || string.IsNullOrEmpty(oc.PreviewPngPath))
                        return (false, $"model item (invType {invType}): no M2 texture ({terr}) and no {subdir} BLP", 0);
                    string ocPreview = Path.Combine(_env.WebRootPath,
                        oc.PreviewPngPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                    if (!System.IO.File.Exists(ocPreview)) return (false, "model item: object-component preview PNG missing", 0);
                    return await CommitSingleAsync(baseDid, itemName, oc, ocPreview, seed, theory, kd, ku, m, pop, budget, leash, value, instruction, outDir, ct);
                }

            default:
                return (false, $"inventory_type {invType} has no texture to recolor", 0);
        }
    }

    private async Task<(bool ok, string? err, uint newDid)> CommitSingleAsync(
        uint baseDid, string itemName, ItemTextureEntry tex, string previewPath,
        int seed, string theory, float kd, float ku, float m, float pop, float budget, float leash,
        ValueSettings value, string instruction, string outDir, CancellationToken ct)
    {
        string outPng = Path.Combine(outDir, $"tier_{baseDid}_{Guid.NewGuid():N}.png");
        var recolored = await _palette.RecolorSeededAsync(previewPath, outPng, seed, 1.0f, 0.0f, false, ct,
            theory, kd, ku, m, pop, budget, leash, value);
        if (recolored == null) return (false, "palette recolor failed", 0);

        var req = new RetextureRequest
        {
            DisplayId = baseDid,
            ItemName = itemName,
            OriginalBlpFilename = tex.Filename,
            OriginalMpqPath = tex.MpqPath,
            StyleDirection = instruction,
        };
        var res = await _retexture.RetextureFromBitmapAsync(req, outPng, ct, rebuildPatch: false, preResolved: tex);
        return res.Success ? (true, null, (uint)res.NewDisplayId) : (false, res.Error ?? "commit failed", 0);
    }

    /// <summary>
    /// Drain pending lootifier_retexture_queue jobs under the chosen theory + value.
    /// Same queue table BuildRetextureQueue fills; rebuilds patch-4.MPQ once when
    /// the queue drains. Returns a JSON-ready summary object.
    /// </summary>
    public async Task<object> ProcessQueueAsync(string theory, ValueSettings value, int max, CancellationToken ct)
    {
        max = Math.Clamp(max, 1, 25);
        using var adminConn = _db.Admin();
        using var mangosConn = _db.Mangos();
        await EnsureRetextureQueueTable(adminConn);

        var jobs = (await adminConn.QueryAsync<RetextureJobRow>(
            "SELECT * FROM lootifier_retexture_queue WHERE status = 'pending' ORDER BY id LIMIT @Max",
            new { Max = max })).ToList();

        var invTypes = new Dictionary<int, int>();
        if (jobs.Count > 0)
        {
            var baseEntries = jobs.Select(j => j.base_entry).Distinct().ToList();
            foreach (var r in await mangosConn.QueryAsync<dynamic>(
                "SELECT entry, inventory_type FROM item_template WHERE entry IN @E", new { E = baseEntries }))
                invTypes[(int)r.entry] = Convert.ToInt32(r.inventory_type);
        }

        int processed = 0, succeeded = 0, failed = 0, restyled = 0;
        var results = new List<object>();

        foreach (var job in jobs)
        {
            processed++;
            try
            {
                int invType = invTypes.GetValueOrDefault(job.base_entry, 0);
                var (ok, err, newDid) = await ProcessJobAsync(
                    job.base_entry, (uint)job.base_display_id, invType, job.tier ?? "", job.item_name ?? "",
                    theory, value, null, ct);

                if (!ok || newDid == 0)
                {
                    failed++;
                    string emsg = (err ?? "unknown error"); if (emsg.Length > 500) emsg = emsg.Substring(0, 500);
                    await adminConn.ExecuteAsync(
                        "UPDATE lootifier_retexture_queue SET status='failed', error=@E, processed_at=NOW() WHERE id=@Id",
                        new { E = emsg, Id = job.id });
                    results.Add(new { job.id, job.tier, ok = false, error = err });
                    continue;
                }

                var entries = ParseEntryCsv(job.variant_entries);
                if (entries.Count > 0)
                {
                    await mangosConn.ExecuteAsync(
                        "UPDATE item_template SET display_id=@Did WHERE entry IN @E", new { Did = (int)newDid, E = entries });
                    restyled += entries.Count;
                }
                await adminConn.ExecuteAsync(
                    "UPDATE lootifier_retexture_queue SET status='done', new_display_id=@Did, error=NULL, processed_at=NOW() WHERE id=@Id",
                    new { Did = (int)newDid, Id = job.id });
                succeeded++;
                results.Add(new { job.id, job.tier, ok = true, newDisplayId = newDid, variants = entries.Count });
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogError(ex, "RetextureEngine queue job {Id} failed", job.id);
                string msg = ex.Message.Length > 500 ? ex.Message.Substring(0, 500) : ex.Message;
                await adminConn.ExecuteAsync(
                    "UPDATE lootifier_retexture_queue SET status='failed', error=@E, processed_at=NOW() WHERE id=@Id",
                    new { E = msg, Id = job.id });
                results.Add(new { job.id, job.tier, ok = false, error = ex.Message });
            }
        }

        int remaining = await adminConn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM lootifier_retexture_queue WHERE status = 'pending'");

        bool patchRebuilt = false; string? patchError = null;
        if (remaining == 0 && succeeded > 0)
        {
            var rb = await _retexture.RebuildPatchMAsync();
            patchRebuilt = rb.Success; patchError = rb.Error;
        }

        return new { success = true, processed, succeeded, failed, remaining, restyled, patchRebuilt, patchError, results };
    }

    /// <summary>
    /// Retexture an AD-HOC selection (one or many) at the chosen theory + value,
    /// scoped to one tier or all. asSet=true shares ONE seed across the whole
    /// selection so items that are a set but were never grouped match. Commits
    /// each directly, rebuilds patch-4.MPQ once. JSON-ready summary object.
    /// </summary>
    public async Task<object> RetextureSelectionAsync(
        List<int> entries, List<string> tiers, string theory, ValueSettings value, bool asSet, CancellationToken ct)
    {
        entries = (entries ?? new List<int>()).Distinct().ToList();
        if (entries.Count == 0) return new { success = false, error = "No items selected" };

        var allTiers = new[] { "improved", "power", "glory", "gods" };
        tiers = (tiers ?? new List<string>()).Where(allTiers.Contains).Distinct().ToList();
        if (tiers.Count == 0) tiers = allTiers.ToList();

        using var mangosConn = _db.Mangos();
        var infos = (await mangosConn.QueryAsync<dynamic>(
            "SELECT entry, display_id, name, inventory_type FROM item_template WHERE entry IN @E", new { E = entries })).ToList();
        if (infos.Count == 0) return new { success = false, error = "No matching items" };

        int groupAnchor = entries.Min();
        int succeeded = 0, failed = 0, restyled = 0;
        var results = new List<object>();

        foreach (var info in infos)
        {
            int entry = Convert.ToInt32(info.entry);
            int did = Convert.ToInt32(info.display_id);
            string name = (string)(info.name ?? "");
            int invType = Convert.ToInt32(info.inventory_type);

            if (did <= 0) { failed++; results.Add(new { entry, ok = false, error = "no display_id" }); continue; }
            if (KindForInventoryType(invType) == KIND_NONE)
            {
                failed++; results.Add(new { entry, ok = false, error = $"inventory_type {invType} has no texture" });
                continue;
            }

            foreach (var tier in tiers)
            {
                int? seedOverride = asSet ? SeedFor(groupAnchor, tier) : (int?)null;
                try
                {
                    var (ok, err, newDid) = await ProcessJobAsync(entry, (uint)did, invType, tier, name, theory, value, seedOverride, ct);
                    if (ok && newDid != 0)
                    {
                        await mangosConn.ExecuteAsync(
                            "UPDATE item_template SET display_id=@D WHERE entry=@E", new { D = (int)newDid, E = entry });
                        succeeded++; restyled++;
                        results.Add(new { entry, tier, ok = true, newDisplayId = newDid });
                    }
                    else { failed++; results.Add(new { entry, tier, ok = false, error = err }); }
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogError(ex, "RetextureSelection {E}/{T} failed", entry, tier);
                    results.Add(new { entry, tier, ok = false, error = ex.Message });
                }
            }
        }

        var rb = await _retexture.RebuildPatchMAsync();
        return new
        {
            success = true,
            succeeded,
            failed,
            restyled,
            asSet,
            tiers,
            theory,
            value = value.IsInvert ? "invert" : "keep",
            patchRebuilt = rb.Success,
            patchError = rb.Error,
            results
        };
    }

    // ══ Base-name navigation (lootifier bases <-> their tier variants) ══════
    /// <summary>All generated_entry ids across the lootifier — the UI hides these
    /// so the browse lists BASE items; a base's variants surface via the strip.</summary>
    public async Task<List<int>> GeneratedEntriesAsync()
    {
        try
        {
            using var admin = _db.Admin();
            var rows = await admin.QueryAsync<int>("SELECT generated_entry FROM lootifier_generated_items");
            return rows.ToList();
        }
        catch { return new List<int>(); }
    }

    private static string QualityName(int q) => q switch
    {
        0 => "Poor",
        1 => "Common",
        2 => "Uncommon",
        3 => "Rare",
        4 => "Epic",
        5 => "Legendary",
        6 => "Artifact",
        _ => "Tier " + q
    };

    /// <summary>
    /// Given ANY entry (a base or one of its generated variants), return the base's
    /// tier lineup: one representative generated item per tier (entry + display +
    /// name + inventory_type). hasVariants=false for non-lootifier items.
    /// </summary>
    public async Task<object> BaseVariantsAsync(int entry)
    {
        int baseEntry;
        try
        {
            using var admin0 = _db.Admin();
            baseEntry = await admin0.ExecuteScalarAsync<int?>(
                "SELECT base_entry FROM lootifier_generated_items WHERE generated_entry=@E OR base_entry=@E LIMIT 1",
                new { E = entry }) ?? 0;
        }
        catch { baseEntry = 0; }
        if (baseEntry == 0) return new { success = true, hasVariants = false };

        using var admin = _db.Admin();
        using var mangos = _db.Mangos();

        var gen = (await admin.QueryAsync<dynamic>(
            "SELECT generated_entry, tier_name FROM lootifier_generated_items WHERE base_entry=@E",
            new { E = baseEntry })).ToList();
        if (gen.Count == 0) return new { success = true, hasVariants = false };

        var genEntries = gen.Select(g => (int)g.generated_entry).Distinct().ToList();
        var items = (await mangos.QueryAsync<dynamic>(
            "SELECT entry, name, display_id AS displayId, inventory_type AS inventoryType, quality FROM item_template WHERE entry IN @E",
            new { E = genEntries })).ToList();

        var baseItem = await mangos.QueryFirstOrDefaultAsync<dynamic>(
            "SELECT name, display_id AS displayId FROM item_template WHERE entry=@E", new { E = baseEntry });

        // Group by TIER (tier_name) so each distinct tier is ONE tile. A tier can span
        // several qualities via stat rolls, so grouping by quality wrongly split one tier
        // into two and dropped the others. Keep the lowest-quality representative per tier
        // (its floor) and ORDER the tiers by that quality, so the strip still comes out in
        // rarity order regardless of naming.
        var tierByEntry = gen
            .GroupBy(g => (int)g.generated_entry)
            .ToDictionary(grp => grp.Key, grp => (string)(grp.First().tier_name ?? ""));
        var perTier = new Dictionary<string, (int entry, int quality, string name, int display, int invType)>();
        foreach (var it in items)
        {
            int e = (int)it.entry;
            string tn = tierByEntry.TryGetValue(e, out var t) ? t : "";
            int q = (int)it.quality;
            if (!perTier.TryGetValue(tn, out var cur) || q < cur.quality)
                perTier[tn] = (e, q, (string)it.name, (int)it.displayId, (int)it.inventoryType);
        }
        var tiers = perTier
            .OrderBy(kv => kv.Value.quality)
            .Select(kv => (object)new
            {
                tier = kv.Key,
                quality = kv.Value.quality,
                qualityName = QualityName(kv.Value.quality),
                entry = kv.Value.entry,
                name = kv.Value.name,
                displayId = kv.Value.display,
                inventoryType = kv.Value.invType
            }).ToList();

        return new
        {
            success = true,
            hasVariants = tiers.Count > 0,
            baseEntry,
            baseName = baseItem != null ? (string)baseItem.name : "",
            baseDisplayId = baseItem != null ? (int)baseItem.displayId : 0,
            tiers
        };
    }

    /// <summary>
    /// Where an item comes from — creature drops, vendors, quest rewards. Each
    /// source is wrapped so a schema mismatch (VMaNGOS column-name differences)
    /// just yields an empty list for that source instead of failing the request.
    /// The creature-drop join uses loot_id == entry (true for the common case
    /// where a creature's loot template id equals its entry).
    /// </summary>
    public async Task<object> ItemSourcesAsync(int entry)
    {
        using var m = _db.Mangos();
        async Task<List<string>> Q(string sql)
        {
            try
            {
                var rows = await m.QueryAsync<string>(sql, new { E = entry });
                return rows.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().Take(30).ToList();
            }
            catch { return new List<string>(); }
        }

        var creatures = await Q(
            "SELECT DISTINCT ct.name FROM creature_loot_template clt " +
            "JOIN creature_template ct ON ct.entry = clt.entry " +
            "WHERE clt.item = @E ORDER BY ct.name LIMIT 30");

        var vendors = await Q(
            "SELECT DISTINCT ct.name FROM npc_vendor nv " +
            "JOIN creature_template ct ON ct.entry = nv.entry " +
            "WHERE nv.item = @E ORDER BY ct.name LIMIT 30");

        var quests = await Q(
            "SELECT DISTINCT Title FROM quest_template WHERE " +
            "RewItemId1=@E OR RewItemId2=@E OR RewItemId3=@E OR RewItemId4=@E OR " +
            "RewChoiceItemId1=@E OR RewChoiceItemId2=@E OR RewChoiceItemId3=@E OR " +
            "RewChoiceItemId4=@E OR RewChoiceItemId5=@E OR RewChoiceItemId6=@E " +
            "ORDER BY Title LIMIT 30");

        return new { success = true, creatures, vendors, quests };
    }

    private string? WebUrlOf(string diskPath)
    {
        var rootNorm = _env.WebRootPath.Replace('/', Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar);
        var norm = diskPath.Replace('/', Path.DirectorySeparatorChar);
        if (!norm.StartsWith(rootNorm, StringComparison.OrdinalIgnoreCase)) return null;
        return "/" + norm.Substring(rootNorm.Length).TrimStart(Path.DirectorySeparatorChar).Replace(Path.DirectorySeparatorChar, '/');
    }

    /// <summary>The display's CURRENT primary texture as a web URL, with NO recolor —
    /// i.e. what is committed right now. ItemTextureService.GetTexturesForDisplay resolves
    /// custom retextures (custom_item_retexture) FIRST, decoding the committed BLP to a PNG,
    /// so a retextured display returns its COMMITTED texture (matching the 3D viewer) and an
    /// un-retextured one returns the base. Powers the "view existing" mode.</summary>
    public async Task<object> SourceTextureAsync(uint displayId)
    {
        // A committed retexture is served by GetTexturesForDisplay as the primary
        // texture, marked with a leading star (U+2605) by ItemTextureService. Use THAT
        // for retextured displays only. For everything else, resolve the same primary
        // texture the recolor targets (ResolvePrimarySourceAsync) so the Base tile
        // matches the tier tiles instead of whatever texture is index 0 in the model.
        try
        {
            var info = _itemTextures.GetTexturesForDisplay(displayId);
            var custom = info?.Textures?.FirstOrDefault(t =>
                !string.IsNullOrEmpty(t.PreviewPngPath) && t.Filename != null && t.Filename.StartsWith("\u2605"));
            if (custom != null) return new { success = true, url = custom.PreviewPngPath };
        }
        catch { /* fall through to the primary-texture resolver */ }

        var (src, err) = await ResolvePrimarySourceAsync(displayId);
        if (src == null) return new { success = false, error = err ?? "no source texture" };
        var url = WebUrlOf(src);
        return url == null ? new { success = false, error = "texture is outside web root" } : new { success = true, url };
    }

    public (ItemTextureEntry? Tex, string? PreviewPath, string? Error) ResolveTargetTexture(
        uint displayId, string mpqPath, string filename)
    {
        if (displayId == 0) return (null, null, "No displayId");
        var texInfo = _itemTextures.GetTexturesForDisplay(displayId);
        if (texInfo == null) return (null, null, "No textures found");

        // Explicit request from the interactive panel — honour it exactly.
        var targetTex = texInfo.Textures.FirstOrDefault(t =>
            t.MpqPath.Equals(mpqPath, StringComparison.OrdinalIgnoreCase)
            || t.Filename.Equals(filename, StringComparison.OrdinalIgnoreCase));

        if (targetTex == null)
        {
            // ── No explicit texture (the batch path) ──
            //
            // This used to fall back to Textures.FirstOrDefault() — the first
            // texture in the M2's list — and that is wrong for any multi-texture
            // model.
            //
            // The commit patches ItemDisplayInfo field 3 = TextureName1, and that
            // field feeds exactly ONE of the model's texture slots. Every other
            // texture in an M2 is type 0: its path is BAKED INTO THE MODEL and no
            // DBC field can redirect it.
            //
            // Staff of Westfall has two textures — a 64x64 M2-embedded glow and the
            // 128x32 skin supplied by TextureName1. FirstOrDefault() grabbed the
            // GLOW, recolored it pink, and then wrote that recolor's name into
            // TextureName1: a recolor of texture A aimed at the slot that feeds
            // texture B. The staff rendered vanilla green, and the pink went nowhere.
            //
            // Match on TextureName1 so we recolor the texture the DBC actually owns.
            var dbcInfo = _dbc.GetItemModelInfo(displayId);
            string tex1 = dbcInfo?.TextureName1 ?? "";

            // Environment/reflection maps are shared cubemap-style textures the
            // engine applies for shine — they are NOT the item's skin, and
            // recoloring one repaints every reflective item in the game (or, via
            // the DBC redirect, points the skin slot at a reflection map and the
            // weapon renders BLACK — the Haggard's Sword incident, where
            // Textures[0] of Sword_1H_Long_A_02 turned out to be ARMORREFLECT3).
            static bool IsEnvMap(string? name)
            {
                var n = (name ?? "").ToUpperInvariant();
                return n.Contains("REFLECT") || n.Contains("ENVMAP") || n.Contains("GENERICGLOW");
            }

            if (!string.IsNullOrWhiteSpace(tex1) && !IsEnvMap(tex1))
            {
                // Filename may be bare or a full path depending on the M2 —
                // normalize before taking the stem (Linux GetFileName* does not
                // split on backslashes).
                targetTex = texInfo.Textures.FirstOrDefault(t =>
                    Path.GetFileNameWithoutExtension((t.Filename ?? "").Replace('\\', '/'))
                        .Equals(tex1, StringComparison.OrdinalIgnoreCase));

                if (targetTex == null)
                    return (null, null,
                        $"TextureName1 '{tex1}' matches none of the model's {texInfo.Textures.Count} M2 texture(s) — cannot recolor a texture the DBC does not control");
            }
            else if (IsEnvMap(tex1))
            {
                // The DBC's own TextureName1 is a reflection map (some vanilla
                // rows genuinely do this — the skin is baked, only the env map is
                // DBC-supplied). There is nothing recolorable that the DBC
                // controls. Refuse rather than paint the world's shine.
                return (null, null,
                    $"TextureName1 '{tex1}' is an environment/reflection map, not the item's skin — recoloring it is wrong for every item sharing it");
            }
            else
            {
                // Every texture is baked into the M2. Writing a name into
                // TextureName1 would be a silent no-op — fail loudly instead.
                return (null, null,
                    $"model has no DBC-supplied texture (TextureName1 is empty); all {texInfo.Textures.Count} texture(s) are baked into the M2 and cannot be swapped via ItemDisplayInfo");
            }

            // Dead-override guard: TextureName1 can name a texture the model never
            // renders — no Type-2 slot for the DBC to fill, so the override is inert
            // and the model shows its BAKED skin instead (Gressil: a Naxxramas/
            // Frostmourne override on a model that renders ITEM\OBJECTCOMPONENTS\
            // WEAPON\1HSWD_02). If every geometry-sampled skin slot is a baked Type-0
            // texture (none is an empty-filename Type-2 slot), recolor the baked skin
            // the item actually shows rather than the dead override. Weapons with a
            // real Type-2 skin (Staff of Westfall, Haggard) keep the TextureName1 path.
            var sampledSkins = _itemTextures.GetSampledSkinSlots(displayId);
            if (sampledSkins.Count > 0 && sampledSkins.All(s => !string.IsNullOrEmpty(s.Filename)))
            {
                string stem = Path.GetFileNameWithoutExtension((sampledSkins[0].Filename ?? "").Replace('\\', '/'));
                var rendered = texInfo.Textures.FirstOrDefault(t =>
                    Path.GetFileNameWithoutExtension((t.Filename ?? "").Replace('\\', '/'))
                        .Equals(stem, StringComparison.OrdinalIgnoreCase));
                if (rendered != null) targetTex = rendered;
            }
        }

        // Preview is required for palette/segmented/variation/img2img modes but
        // NOT for Flux txt2img. Return the tex regardless; previewPath stays
        // null when the preview isn't on disk and callers that need it check.
        if (!targetTex.HasPreview)
            return (targetTex, null, null);

        string previewPath = Path.Combine(_env.WebRootPath,
            targetTex.PreviewPngPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        if (!System.IO.File.Exists(previewPath))
            return (targetTex, null, null);

        return (targetTex, previewPath, null);
    }
}