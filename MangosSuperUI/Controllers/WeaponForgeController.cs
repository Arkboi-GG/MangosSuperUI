using System.Buffers.Binary;
using System.Numerics;
using Dapper;
using SkiaSharp;
using MangosSuperUI.Models;
using MangosSuperUI.Services;
using MangosSuperUI.Services.WeaponForge;
using MangosSuperUI.Services.WeaponForge.RawM2;
using Microsoft.AspNetCore.Mvc;

namespace MangosSuperUI.Controllers;

/// <summary>
/// Weapon Forge (WEAPON_GEN.md) HTTP surface — the IMPORT page. It accepts a finished,
/// pre-textured GLB (UVs + embedded texture authored elsewhere), decimates it to a game budget
/// with the UV-preserving decimator, and packages it through the one proofed path: M2 + BLP
/// compile, world-DB insert + reload, unified patch-5.MPQ deploy, registry entry.
///
/// The creation tooling that used to live here (sketch workbench, texture zones, local AI
/// texturing) is archived under Desktop\ItemForgeMSUIFiles.
///
/// Everything here is build/staging only in spirit: forging inserts and deploys via the audited
/// build service; nothing else touches a live server or client.
/// </summary>
public class WeaponForgeController : Controller
{
    // Golden donor fixture paths (WEAPON_GEN.md §13.3).
    private const string DonorM2Path = @"ITEM\ObjectComponents\WEAPON\Sword_1H_Short_A_01.m2";
    private const string DonorBlpPath = @"ITEM\ObjectComponents\WEAPON\Sword_1H_Short_A_01Blue.blp";

    private readonly MpqReaderService _mpq;
    private readonly WeaponPreviewService _preview;
    private readonly CustomWeaponBuildService _builder;
    private readonly GlbWeaponImporter _glbImporter;
    private readonly WeaponDonorResolver _donors;
    private readonly TbcMpqSource _tbc;
    private readonly TbcItemCatalog _tbcItems;
    private readonly ConnectionFactory _db;
    private readonly ILogger<WeaponForgeController> _logger;

    // High-poly sources are welcome — they are decimated to budget before forging.
    private const long MaxGlbBytes = 128 * 1024 * 1024;   // 128 MB
    // The variable-topology M2 writer's hard ceiling (RigidWeaponMeshValidator.VariableHardCeiling).
    private const int MaxForgeTriangles = 1000;
    // A preserved TBC mesh is bounded by the vanilla view's UInt16 index count, not the Forge's
    // authoring/decimation policy used for arbitrary GLB uploads.
    private const int MaxTbcForgeTriangles = ushort.MaxValue / 3;

    public WeaponForgeController(MpqReaderService mpq, WeaponPreviewService preview,
        CustomWeaponBuildService builder, GlbWeaponImporter glbImporter, WeaponDonorResolver donors,
        TbcMpqSource tbc, TbcItemCatalog tbcItems, ConnectionFactory db, ILogger<WeaponForgeController> logger)
    {
        _mpq = mpq;
        _preview = preview;
        _builder = builder;
        _glbImporter = glbImporter;
        _donors = donors;
        _tbc = tbc;
        _tbcItems = tbcItems;
        _db = db;
        _logger = logger;
    }

    /// <summary>Uniform response for every full weapon build: ids, hashes, direct downloads for the
    /// straight patch MPQ and the item SQL (no ZIP), preview, grip markers, and diagnostics.</summary>
    private object BuildResultJson(CustomWeaponBuildResult r, object? grip = null) => new
    {
        ok = true,
        r.BuildId,
        r.ItemEntry,
        r.DisplayId,
        r.ModelIndex,
        r.Name,
        r.WeaponType,
        r.WeaponTypeLabel,
        r.InventoryType,
        r.InventoryTypeLabel,
        r.SourceKind,
        r.ModelMember,
        r.TextureMember,
        r.MpqSha256,
        r.DbcSha256,
        r.SqlSha256,
        r.AllMembersVerified,
        r.PackagedWeaponCount,
        r.SkippedWeaponCount,
        r.PreviewGlbWebPath,
        r.TriangleCount,
        r.VertexCount,
        mpqDownloadUrl = $"/WeaponForge/DownloadBuild?build={Uri.EscapeDataString(r.BuildDirName)}&file={CustomWeaponBuildService.PatchFileName}",
        sqlDownloadUrl = $"/WeaponForge/DownloadBuild?build={Uri.EscapeDataString(r.BuildDirName)}&file=item_template.sql",
        grip,
        apply = r.Apply,
        diagnostics = r.Diagnostics,
    };

    /// <summary>Grip-marker payload for the viewer, computed on the final normalized mesh. The
    /// main-hand band sits at the model origin — that is exactly where the client's hand bone
    /// mounts the weapon, so it is precise. The off-hand band (two-handers only) is an approximate
    /// zone: the character animation places the second hand, not the weapon file.</summary>
    private static object BuildGripInfo(RigidWeaponMesh mesh, WeaponTypeProfile profile, WeaponDonorInfo donor)
    {
        float minX = float.MaxValue, maxX = float.MinValue;
        foreach (var p in mesh.Positions)
        {
            minX = MathF.Min(minX, p.X);
            maxX = MathF.Max(maxX, p.X);
        }
        float len = MathF.Max(maxX - minX, 1e-6f);

        // Largest cross-section radius near an X station, for sizing the band around the shaft.
        float RadiusAt(float station)
        {
            float best = 0f;
            int hits = 0;
            float halfWindow = len * 0.06f;
            foreach (var p in mesh.Positions)
            {
                if (MathF.Abs(p.X - station) > halfWindow) continue;
                best = MathF.Max(best, MathF.Sqrt(p.Y * p.Y + p.Z * p.Z));
                hits++;
            }
            if (hits == 0)
                foreach (var p in mesh.Positions)
                    best = MathF.Max(best, MathF.Sqrt(p.Y * p.Y + p.Z * p.Z));
            return best;
        }

        object? secondHand = null;
        if (profile.SecondHandFraction is { } fraction)
        {
            float x2 = fraction * len;
            secondHand = new { x = x2, radius = RadiusAt(x2) };
        }

        return new
        {
            type = profile.Key,
            label = profile.Label,
            twoHanded = profile.TwoHanded,
            palm = new { x = 0f, radius = RadiusAt(0f) },
            secondHand,
            minX,
            maxX,
            extent = donor.ExtentX,
            palmBackFraction = donor.PalmBackFraction,
            note = "Green band = main-hand palm (model origin, exact)." +
                   (secondHand is not null ? " Blue band ≈ off-hand zone (animation-placed, approximate)." : ""),
        };
    }

    /// <summary>GET /WeaponForge — the Item Assets import page (Game Development).</summary>
    [HttpGet]
    public IActionResult Index() => View();

    /// <summary>GET /WeaponForge/Status — foundation self-checks: fixture integrity, contract
    /// version, whether the golden donor resolves from the mounted archives, and per-family
    /// donor resolution (which stock model each weapon type will scaffold on).</summary>
    [HttpGet]
    public IActionResult Status()
    {
        var donorM2 = SafeExtract(DonorM2Path);
        var donorBlp = SafeExtract(DonorBlpPath);

        var types = WeaponTypeCatalog.All.Select(p =>
        {
            try
            {
                var d = _donors.Resolve(p);
                return new
                {
                    key = p.Key,
                    label = p.Label,
                    inventoryType = p.InventoryType,
                    inventoryTypeLabel = CustomWeaponBuildService.InventoryTypeLabel(p.InventoryType),
                    twoHanded = p.TwoHanded,
                    ok = true,
                    donorModel = (string?)d.ModelName,
                    donorDisplayRow = d.DisplayRow,
                    extent = d.ExtentX,
                    palmBackFraction = d.PalmBackFraction,
                    error = (string?)null,
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning("WeaponForge: donor resolution for {Type} failed: {Error}", p.Key, ex.Message);
                return new
                {
                    key = p.Key,
                    label = p.Label,
                    inventoryType = p.InventoryType,
                    inventoryTypeLabel = CustomWeaponBuildService.InventoryTypeLabel(p.InventoryType),
                    twoHanded = p.TwoHanded,
                    ok = false,
                    donorModel = (string?)null,
                    donorDisplayRow = 0u,
                    extent = 0f,
                    palmBackFraction = 0f,
                    error = (string?)ex.Message,
                };
            }
        }).ToArray();

        return Json(new
        {
            fixtureVerified = DonorItemTemplateFixture.Verify(),
            fixtureSha = DonorItemTemplateFixture.ExpectedSha256,
            coordinateContractVersion = CoordinateContract.Version,
            donorM2Found = donorM2 is not null,
            donorM2Bytes = donorM2?.Length ?? 0,
            donorBlpFound = donorBlp is not null,
            donorBlpBytes = donorBlp?.Length ?? 0,
            weaponTypes = types,
            note = "Build/staging only. No SQL/patch is applied to any live server or client.",
        });
    }

    /// <summary>GET /WeaponForge/InspectDonor — run the lossless raw M2 inspector on the golden
    /// donor and confirm the byte-exact round trip. Proves the Phase-0 inspector on real bytes.</summary>
    [HttpGet]
    public IActionResult InspectDonor()
    {
        var m2 = SafeExtract(DonorM2Path);
        if (m2 is null) return NotFound(new { error = $"Donor M2 not found in mounted archives: {DonorM2Path}" });

        var doc = RawM2Document.Parse(m2, out var err);
        if (doc is null) return Json(new { ok = false, error = err });

        var report = RawM2Inspector.Inspect(doc);
        bool roundTrips = RawM2Inspector.RoundTripsExact(m2);
        return Json(new { ok = true, roundTripsExact = roundTrips, report });
    }

    /// <summary>GET /WeaponForge/PreviewDonor — extract the donor M2 + BLP and render a preview GLB
    /// from the raw bytes (content-hash addressed, no display-id lookup).</summary>
    [HttpGet]
    public IActionResult PreviewDonor()
    {
        var m2 = SafeExtract(DonorM2Path);
        if (m2 is null) return NotFound(new { error = $"Donor M2 not found: {DonorM2Path}" });
        var blp = SafeExtract(DonorBlpPath);

        var result = _preview.RenderFromBytes(m2, blp);
        return Json(result);
    }

    private static readonly string[] DownloadableBuildFiles =
        { CustomWeaponBuildService.PatchFileName, "item_template.sql", "manifest.json", "validation-report.md", "OWNER_CHECKLIST.md" };

    /// <summary>GET /WeaponForge/DownloadBuild?build=weapon-build-xxx&amp;file=patch-4.MPQ — serves one
    /// file from a prepared build directory. Pure read: never rebuilds or deploys anything.</summary>
    [HttpGet]
    public IActionResult DownloadBuild(string build, string file)
    {
        var safeBuild = Path.GetFileName(build ?? "");
        var safeFile = Path.GetFileName(file ?? "");
        if (!safeBuild.StartsWith("weapon-build-", StringComparison.Ordinal) ||
            !DownloadableBuildFiles.Contains(safeFile, StringComparer.OrdinalIgnoreCase))
            return BadRequest(new { error = "Invalid build or file name." });

        var fullPath = Path.Combine(_builder.ArtifactRoot, safeBuild, safeFile);
        if (!System.IO.File.Exists(fullPath))
            return NotFound(new { error = $"Not found: {safeBuild}/{safeFile}" });

        string contentType = Path.GetExtension(safeFile).ToLowerInvariant() switch
        {
            ".mpq" => "application/octet-stream",
            ".sql" => "text/plain",
            ".json" => "application/json",
            ".md" => "text/markdown",
            _ => "application/octet-stream",
        };
        return PhysicalFile(fullPath, contentType, safeFile);
    }

    /// <summary>GET /WeaponForge/DownloadPatch — the canonical latest unified patch-5.MPQ (every
    /// custom weapon recorded in the database), refreshed on every build/delete/rebuild.</summary>
    [HttpGet]
    public IActionResult DownloadPatch()
    {
        var fullPath = Path.Combine(_builder.ArtifactRoot, CustomWeaponBuildService.PatchFileName);
        if (!System.IO.File.Exists(fullPath))
            return NotFound(new { error = "No patch built yet — forge a weapon first." });
        return PhysicalFile(fullPath, "application/octet-stream", CustomWeaponBuildService.PatchFileName);
    }

    /// <summary>GET /WeaponForge/ListWeapons — the Forge's inventory: every weapon currently
    /// recorded in the registry (and therefore packaged into patch-5).</summary>
    [HttpGet]
    public async Task<IActionResult> ListWeapons()
    {
        try
        {
            var weapons = await _builder.ListWeaponsAsync();
            return Json(new
            {
                ok = true,
                weapons = weapons.Select(w => new
                {
                    w.DisplayId,
                    w.ItemEntry,
                    w.Name,
                    weaponType = w.WeaponType,
                    weaponTypeLabel = w.WeaponType is null ? null : WeaponTypeCatalog.Get(w.WeaponType).Label,
                    inventoryType = w.InventoryType,
                    inventoryTypeLabel = w.InventoryTypeLabel,
                    w.SourceKind,
                    w.ModelMpqPath,
                    w.BuildId,
                    createdAt = w.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                }),
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WeaponForge: ListWeapons failed");
            return Json(new { ok = false, error = ex.Message });
        }
    }

    /// <summary>POST /WeaponForge/DeleteWeapon?displayId= — remove one forged weapon EVERYWHERE:
    /// registry, world-DB item row (+reload), and the deployed patch (repackaged without it). The
    /// weapon's ids are released for reuse; the audit log keeps the history.</summary>
    [HttpPost]
    public async Task<IActionResult> DeleteWeapon(long displayId)
    {
        try
        {
            var result = await _builder.DeleteWeaponAsync(displayId);
            return Json(new
            {
                ok = true,
                deleted = result.Deleted,
                weaponsRemaining = result.Rebuild.WeaponCount,
                patchRemoved = result.Rebuild.PatchRemoved,
                mpqSha256 = result.Rebuild.MpqSha256,
                itemRowDeleted = result.ItemRowDeleted,
                itemRowMessage = result.ItemRowMessage,
                reloaded = result.Reloaded,
                reloadMessage = result.ReloadMessage,
                patchDeployed = result.Rebuild.PatchDeployed,
                patchDeployMessage = result.Rebuild.PatchDeployMessage,
                patchDownloadUrl = result.Rebuild.PatchRemoved ? null : "/WeaponForge/DownloadPatch",
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { ok = false, error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WeaponForge: DeleteWeapon {DisplayId} failed", displayId);
            return Json(new { ok = false, error = ex.Message });
        }
    }

    /// <summary>POST /WeaponForge/RebuildPatch — repackage patch-5 from current DB state without
    /// adding anything, and redeploy it to the client Data folder.</summary>
    [HttpPost]
    public async Task<IActionResult> RebuildPatch()
    {
        try
        {
            var summary = await _builder.RebuildPatchAsync("manual rebuild from UI");
            return Json(new
            {
                ok = true,
                weaponCount = summary.WeaponCount,
                patchRemoved = summary.PatchRemoved,
                mpqSha256 = summary.MpqSha256,
                patchDeployed = summary.PatchDeployed,
                patchDeployMessage = summary.PatchDeployMessage,
                diagnostics = summary.Diagnostics,
                patchDownloadUrl = summary.PatchRemoved ? null : "/WeaponForge/DownloadPatch",
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WeaponForge: RebuildPatch failed");
            return Json(new { ok = false, error = ex.Message });
        }
    }

    /// <summary>Shared import + decimation front half for preview and forge. The GLB must carry
    /// UV0 and (normally) an embedded texture; targetTriangles ≤ 0 skips decimation. The family
    /// donor supplies the target length and palm-back fraction the normalizer lands on.</summary>
    private (RigidWeaponMesh? Mesh, GlbImportResult Import, int OriginalTriangles, string? Decimation, string? Error)
        ImportAndDecimate(byte[] bytes, WeaponDonorInfo donor, bool reorient, int targetTriangles,
            float rollDegrees, bool flipGripEnd, bool straightenBlade, int bladeProfile)
    {
        var import = _glbImporter.Import(bytes, new GlbImportOptions
        {
            Reorient = reorient,
            TargetExtent = donor.ExtentX,
            PalmBackFraction = donor.PalmBackFraction,
            RollDegrees = rollDegrees,
            FlipGripEnd = flipGripEnd,
            StraightenBlade = straightenBlade,
            BladeProfile = Math.Clamp(bladeProfile, 0, 100) / 100f,
        });
        if (!import.Ok || import.Mesh is null)
            return (null, import, 0, null, "GLB import failed — fix the model and retry.");

        var mesh = import.Mesh;
        int original = mesh.TriangleCount;
        string? decimation = null;
        if (targetTriangles > 0 && mesh.TriangleCount > targetTriangles)
        {
            int target = Math.Clamp(targetTriangles, 50, MaxForgeTriangles);
            try
            {
                mesh = UvPreservingDecimator.Decimate(mesh, target, out decimation);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WeaponForge: decimation to {Target} failed", target);
                return (null, import, original, null, "Decimation failed: " + ex.Message);
            }
        }
        return (mesh, import, original, decimation, null);
    }

    /// <summary>POST /WeaponForge/UploadGlb (multipart, field "file") — import a finished,
    /// pre-textured GLB (any triangle count), decimate it to the requested budget with the
    /// UV-preserving decimator, and preview the result WITHOUT packaging anything. What you see is
    /// exactly what ForgeGlb builds at the same target.</summary>
    [HttpPost]
    [RequestSizeLimit(MaxGlbBytes)]
    public async Task<IActionResult> UploadGlb(IFormFile? file, string? weaponType = null, bool reorient = true,
        int targetTriangles = 500, float rollDegrees = 0f, bool flipGripEnd = false, bool straightenBlade = false,
        int bladeProfile = 0, int brightness = 0, int saturation = 0)
    {
        var (bytes, err) = await ReadBounded(file, MaxGlbBytes);
        if (err is not null) return BadRequest(new { ok = false, error = err });
        if (bytes!.Length < 12 || !(bytes[0] == 'g' && bytes[1] == 'l' && bytes[2] == 'T' && bytes[3] == 'F'))
            return BadRequest(new { ok = false, error = "Not a binary glTF (.glb): missing 'glTF' magic." });

        var profile = WeaponTypeCatalog.Get(weaponType);
        WeaponDonorInfo donor;
        try { donor = _donors.Resolve(profile); }
        catch (Exception ex)
        { return Json(new { ok = false, error = $"No stock donor for {profile.Label}: {ex.Message}" }); }

        var (mesh, import, original, decimation, importErr) =
            ImportAndDecimate(bytes, donor, reorient, targetTriangles, rollDegrees, flipGripEnd, straightenBlade, bladeProfile);
        if (mesh is null)
            return Json(new
            {
                ok = false,
                error = importErr,
                diagnostics = import.Diagnostics.Items.Select(i => i.ToString()),
            });

        var preview = _preview.RenderMesh(mesh, AdjustTexture(import.TexturePng, brightness, saturation));
        return Json(new
        {
            ok = preview.Ok,
            weaponType = profile.Key,
            weaponTypeLabel = profile.Label,
            vertexCount = mesh.VertexCount,
            triangleCount = mesh.TriangleCount,
            originalTriangleCount = original,
            decimation,
            sourceSha256 = import.SourceSha256,
            hasTexture = import.TexturePng is { Length: > 0 },
            withinForgeBudget = mesh.TriangleCount <= MaxForgeTriangles,
            normalization = mesh.Normalization,
            grip = BuildGripInfo(mesh, profile, donor),
            preview,
            diagnostics = import.Diagnostics.Items.Select(i => i.ToString()),
            note = "Preview only — nothing was packaged. Forge builds this geometry and material into the game.",
        });
    }

    /// <summary>POST /WeaponForge/ForgeGlb (multipart, field "file") — end-to-end: import the
    /// pre-textured GLB, decimate to the requested budget, then package it for real into the
    /// unified patch MPQ. The GLB's embedded texture becomes the weapon's BLP.</summary>
    [HttpPost]
    [RequestSizeLimit(MaxGlbBytes)]
    public async Task<IActionResult> ForgeGlb(IFormFile? file, string? name = null, string? weaponType = null,
        bool reorient = true,
        int targetTriangles = 500, float rollDegrees = 0f, bool flipGripEnd = false, bool straightenBlade = false,
        int bladeProfile = 0, int brightness = 0, int saturation = 0)
    {
        var (bytes, err) = await ReadBounded(file, MaxGlbBytes);
        if (err is not null) return BadRequest(new { ok = false, error = err });
        if (bytes!.Length < 12 || !(bytes[0] == 'g' && bytes[1] == 'l' && bytes[2] == 'T' && bytes[3] == 'F'))
            return BadRequest(new { ok = false, error = "Not a binary glTF (.glb): missing 'glTF' magic." });

        var profile = WeaponTypeCatalog.Get(weaponType);
        WeaponDonorInfo donor;
        try { donor = _donors.Resolve(profile); }
        catch (Exception ex)
        { return Json(new { ok = false, error = $"No stock donor for {profile.Label}: {ex.Message}" }); }

        var (mesh, import, _, decimation, importErr) =
            ImportAndDecimate(bytes, donor, reorient, targetTriangles, rollDegrees, flipGripEnd, straightenBlade, bladeProfile);
        if (mesh is null)
            return Json(new
            {
                ok = false,
                error = importErr,
                diagnostics = import.Diagnostics.Items.Select(i => i.ToString()),
            });
        if (mesh.TriangleCount > MaxForgeTriangles)
            return Json(new
            {
                ok = false,
                error = $"{mesh.TriangleCount:N0} triangles exceeds the M2 budget ({MaxForgeTriangles:N0}). " +
                        "Lower the target-triangles slider — the decimator preserves the UVs and texture.",
            });

        try
        {
            var result = await _builder.BuildAsync(new CustomWeaponBuildRequest
            {
                Name = name,
                SourceKind = "glb_import",
                WeaponTypeKey = profile.Key,
                Mesh = mesh,
                Topology = WeaponTopologyMode.Variable,
                TexturePng = AdjustTexture(import.TexturePng, brightness, saturation),
                SourceBlob = bytes,
                WriterVersion = "variable-topology-v1",
            });
            if (decimation is not null)
                _logger.LogInformation("WeaponForge: ForgeGlb {Decimation}", decimation);
            return Json(BuildResultJson(result, BuildGripInfo(mesh, profile, donor)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WeaponForge: ForgeGlb failed");
            return Json(new { ok = false, error = ex.Message });
        }
    }

    /// <summary>GET /WeaponForge/PreviewForged?displayId= — render a forged weapon's stored M2+BLP
    /// into a preview GLB so it can be inspected in the viewer.</summary>
    [HttpGet]
    public async Task<IActionResult> PreviewForged(long displayId)
    {
        var (m2, blp, effects) = await LoadForgedBytesAsync(displayId);
        if (m2 is null) return NotFound(new { ok = false, error = $"No stored M2 for display id {displayId}." });
        var preview = _preview.RenderFromBytes(m2, blp, effects);
        return Json(new { ok = preview.Ok, preview, hasTexture = blp is { Length: > 0 }, displayId });
    }

    /// <summary>Load a forged weapon's compiled M2 (+ BLP + effect textures) from the registry
    /// tables (model_id == display_id).</summary>
    private async Task<(byte[]? M2, byte[]? Blp, Dictionary<string, byte[]>? Effects)> LoadForgedBytesAsync(long displayId)
    {
        await using var conn = _db.Admin();
        await conn.OpenAsync();
        var row = await conn.QueryFirstOrDefaultAsync(
            @"SELECT m.compiled_m2 AS M2, d.compiled_blp AS Blp
              FROM custom_weapon_model m
              LEFT JOIN custom_weapon_display d ON d.model_id = m.model_id
              WHERE m.model_id = @displayId", new { displayId });
        if (row is null) return (null, null, null);

        Dictionary<string, byte[]>? effects = null;
        var texRows = await conn.QueryAsync(
            @"SELECT mpq_path AS MpqPath, compiled_blp AS Blp
              FROM custom_weapon_model_texture
              WHERE model_id = @displayId AND compiled_blp IS NOT NULL
              ORDER BY slot", new { displayId });
        foreach (var t in texRows)
        {
            effects ??= new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            effects[(string)t.MpqPath] = (byte[])t.Blp;
        }
        return ((byte[]?)row.M2, (byte[]?)row.Blp, effects);
    }

    /// <summary>GET /WeaponForge/InspectWeapon?displayId= — structural side-by-side dump of a forged
    /// weapon's stored M2 against the golden donor: header fields, bounds, all views, submesh/batch
    /// records, sample vertices, binary-validator output, and automated comparison checks. Built for
    /// debugging renders-invisible failures without client round-trips.</summary>
    [HttpGet]
    public async Task<IActionResult> InspectWeapon(long displayId)
    {
        byte[]? forged;
        await using (var conn = _db.Admin())
        {
            await conn.OpenAsync();
            forged = await conn.QueryFirstOrDefaultAsync<byte[]?>(
                "SELECT compiled_m2 FROM custom_weapon_model WHERE model_id = @displayId", new { displayId });
        }
        if (forged is null or { Length: 0 })
            return NotFound(new { error = $"No stored compiled M2 for display id {displayId}." });

        var donor = SafeExtract(DonorM2Path);
        if (donor is null)
            return NotFound(new { error = $"Donor M2 not found in mounted archives: {DonorM2Path}" });

        return Json(new
        {
            displayId,
            donor = DumpM2(donor, expectedViews: 4),
            forged = DumpM2(forged, expectedViews: 4),
            checks = CompareM2(donor, forged),
        });
    }

    // ═══════════════════════════════════════════════════════════════════
    // TBC IMPORT (WeaponForge:TbcDataPath — Settings page)
    //
    // A TBC weapon is NOT byte-compatible with the 1.12 client (M2 v260–263 vs
    // the required v256), but it doesn't need a converter either: the lossy web
    // M2Reader parses anything below v264, so the TBC model is read into a mesh
    // (positions/normals/UV0/triangles, already palm-at-origin in WoW space) and
    // fed through the exact pipeline a GLB import uses — re-emitted as a genuine
    // vanilla v256 on the family donor scaffold, its TBC BLP decoded to PNG and
    // re-encoded. Models are addressed by their stem and resolved through the
    // server-built index — raw MPQ paths are never accepted from the client.
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>GET /WeaponForge/TbcStatus — mount state of the configured TBC Data folder plus
    /// the shipped item-name catalog join.</summary>
    [HttpGet]
    public IActionResult TbcStatus()
    {
        var (configured, path, archiveCount, error) = _tbc.Status();
        int weaponCount = 0, itemCount = 0;
        if (configured && error is null)
        {
            try
            {
                var index = _tbc.WeaponIndex();
                weaponCount = index.Count;
                var rows = index.Select(w => w.DisplayRow).ToHashSet();
                itemCount = _tbcItems.Items.Count(i => i.ItemClass == 2 && rows.Contains(i.DisplayId));
            }
            catch (Exception ex) { error = ex.Message; }
        }
        return Json(new
        {
            configured,
            path,
            archiveCount,
            weaponCount,
            itemCount,
            catalogItems = _tbcItems.Items.Count,
            error,
            note = "Set the TBC client Data path on the Settings page (Weapon Forge section).",
        });
    }

    /// <summary>GET /WeaponForge/TbcWeapons?search=&amp;page=&amp;pageSize= — paged browse. When the
    /// shipped item catalog is present, rows are real TBC ITEMS (name/quality/ilvl, joined to the
    /// mounted archives by display id, weapon type pre-mapped from the TBC subclass); without it,
    /// the browse degrades to raw model stems.</summary>
    [HttpGet]
    public IActionResult TbcWeapons(string? search = null, int page = 1, int pageSize = 60)
    {
        IReadOnlyList<TbcWeaponEntry> index;
        try { index = _tbc.WeaponIndex(); }
        catch (Exception ex) { return Json(new { ok = false, error = ex.Message }); }

        pageSize = Math.Clamp(pageSize, 10, 200);
        string s = search?.Trim() ?? "";

        var byRow = index.ToDictionary(w => w.DisplayRow, w => w);

        // Item mode: shipped names joined to the user's archives. Weapons only — armor/shields
        // ship in the catalog for the future armor import but are not forgeable yet.
        var items = _tbcItems.Items
            .Where(i => i.ItemClass == 2 &&
                        TbcItemCatalog.TypeKeyForSubclass(i.Subclass) is not null &&
                        byRow.ContainsKey(i.DisplayId))
            .ToList();
        if (items.Count > 0)
        {
            IEnumerable<TbcItemInfo> filtered = items;
            if (s.Length > 0)
                filtered = items.Where(i =>
                    i.Name.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                    byRow[i.DisplayId].ModelStem.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                    i.Entry.ToString() == s);

            var list = filtered.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ThenBy(i => i.Entry).ToList();
            int total = list.Count;
            int pages = Math.Max(1, (total + pageSize - 1) / pageSize);
            page = Math.Clamp(page, 1, pages);

            return Json(new
            {
                ok = true,
                mode = "items",
                total,
                page,
                pages,
                weapons = list.Skip((page - 1) * pageSize).Take(pageSize).Select(i =>
                {
                    var w = byRow[i.DisplayId];
                    string typeKey = TbcItemCatalog.TypeKeyForSubclass(i.Subclass)!;
                    return new
                    {
                        entry = i.Entry,
                        name = i.Name,
                        quality = i.Quality,
                        itemLevel = i.ItemLevel,
                        typeKey,
                        typeLabel = WeaponTypeCatalog.Get(typeKey).Label,
                        inventoryType = i.InventoryType,
                        inventoryTypeLabel = CustomWeaponBuildService.InventoryTypeLabel(i.InventoryType),
                        w.DisplayRow,
                        model = w.ModelStem,
                        texture = w.TextureStem,
                    };
                }),
            });
        }

        // Model-stem fallback (catalog missing, or nothing joined).
        IEnumerable<TbcWeaponEntry> mFiltered = index;
        if (s.Length > 0)
            mFiltered = index.Where(w =>
                w.ModelStem.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                w.TextureStem.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                w.IconStem.Contains(s, StringComparison.OrdinalIgnoreCase));

        var mList = mFiltered.OrderBy(w => w.ModelStem, StringComparer.OrdinalIgnoreCase)
            .ThenBy(w => w.DisplayRow).ToList();
        int mTotal = mList.Count;
        int mPages = Math.Max(1, (mTotal + pageSize - 1) / pageSize);
        page = Math.Clamp(page, 1, mPages);

        return Json(new
        {
            ok = true,
            mode = "models",
            total = mTotal,
            page,
            pages = mPages,
            weapons = mList.Skip((page - 1) * pageSize).Take(pageSize).Select(w => new
            {
                entry = 0u,
                name = w.ModelStem,
                quality = 1,
                itemLevel = 0,
                typeKey = (string?)null,
                typeLabel = (string?)null,
                inventoryType = (int?)null,
                inventoryTypeLabel = (string?)null,
                w.DisplayRow,
                model = w.ModelStem,
                texture = w.TextureStem,
            }),
        });
    }

    /// <summary>Resolve a client-supplied model stem (+ optional display row when one model has
    /// several texture variants) through the server-built index. Never trusts a raw path.</summary>
    private TbcWeaponEntry? ResolveTbcEntry(string? model, uint displayRow)
    {
        if (string.IsNullOrWhiteSpace(model)) return null;
        var index = _tbc.WeaponIndex();
        if (displayRow > 0)
        {
            var byRow = index.FirstOrDefault(w => w.DisplayRow == displayRow &&
                w.ModelStem.Equals(model.Trim(), StringComparison.OrdinalIgnoreCase));
            if (byRow is not null) return byRow;
        }
        return index.FirstOrDefault(w => w.ModelStem.Equals(model.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Resolve a browse selection: a catalog item entry (preferred — carries name, quality
    /// and the subclass-mapped weapon type) or a bare model stem/display row from the fallback mode.</summary>
    private (TbcWeaponEntry? Entry, TbcItemInfo? Item) ResolveTbcSelection(uint itemEntry, string? model, uint displayRow)
    {
        TbcItemInfo? item = itemEntry > 0 ? _tbcItems.FindByEntry(itemEntry) : null;
        if (item is not null)
        {
            var byRow = _tbc.WeaponIndex().FirstOrDefault(w => w.DisplayRow == item.DisplayId);
            if (byRow is not null) return (byRow, item);
        }
        return (ResolveTbcEntry(model, displayRow), item);
    }

    /// <summary>Shared TBC extract + parse + mesh-build front half. PNGs feed the web preview;
    /// original BLP2 bytes feed the forge so the source texture/mips/compression are not altered.</summary>
    private (RigidWeaponMesh? Mesh, byte[]? M2Bytes, byte[]? TexturePng, List<byte[]>? EffectPngs,
        byte[]? TextureBlp, List<byte[]>? EffectBlps, ForgeDiagnostics Diag, string? Error)
        LoadTbcWeapon(TbcWeaponEntry entry, int targetTriangles)
    {
        var diag = new ForgeDiagnostics("tbc-import");

        var m2Bytes = _tbc.ExtractFile(entry.M2Path);
        if (m2Bytes is null)
            return (null, null, null, null, null, null, diag, $"Could not extract {entry.M2Path} from the TBC archives.");

        var m2 = M2Reader.Parse(m2Bytes);
        if (m2 is null)
            return (null, m2Bytes, null, null, null, null, diag, "The TBC M2 could not be parsed (version ≥ 264 or malformed).");
        diag.Info("tbc.source", $"{entry.ModelStem}: M2 v{m2.Version}, {m2.Vertices.Count} verts, {m2.Indices.Count / 3} tris.");

        var extracted = TbcWeaponMeshExtractor.Extract(m2, diag);
        if (extracted is null)
            return (null, m2Bytes, null, null, null, null, diag, "The TBC model has no usable triangles.");
        var mesh = extracted.Mesh;

        // Resolve each texture slot: a hardcoded TBC path, or null = the display row's Type-2 BLP.
        (byte[]? Png, byte[]? Blp) SlotTexture(string? sourcePath, string slotName)
        {
            string? path = sourcePath ?? entry.BlpPath;
            if (path is null) { diag.Warn("tbc.texture", $"No texture source for {slotName}."); return (null, null); }
            var blp = _tbc.ExtractFile(path);
            if (blp is not { Length: > 0 }) { diag.Warn("tbc.texture", $"TBC BLP {path} not found ({slotName})."); return (null, null); }
            var png = BlpToPng(blp);
            if (png is null) diag.Warn("tbc.texture", $"TBC BLP {path} could not be decoded ({slotName}).");
            return (png, blp);
        }

        var baseTexture = SlotTexture(extracted.SourceTextures.Count > 0 ? extracted.SourceTextures[0].SourcePath : null, "base");
        byte[]? texturePng = baseTexture.Png;
        if (texturePng is null)
            return (null, m2Bytes, null, null, baseTexture.Blp, null, diag,
                "The TBC weapon's required base texture is unavailable; fidelity mode will not substitute the donor texture.");
        List<byte[]>? effectPngs = null;
        List<byte[]>? effectBlps = null;
        if (extracted.SourceTextures.Count > 1 && mesh.Passes is not null)
        {
            effectPngs = new List<byte[]>();
            effectBlps = new List<byte[]>();
            for (int s = 1; s < extracted.SourceTextures.Count; s++)
            {
                var effect = SlotTexture(extracted.SourceTextures[s].SourcePath, $"effect slot {s}");
                if (effect.Png is null || effect.Blp is null)
                    return (null, m2Bytes, texturePng, null, baseTexture.Blp, null, diag,
                        $"The TBC weapon's required texture slot {s} is unavailable; fidelity mode will not drop its render pass.");
                effectPngs.Add(effect.Png);
                effectBlps.Add(effect.Blp);
            }
        }

        // TBC imports are fidelity-first. Decimation merges submeshes and destroys the source
        // batch/pass structure that carries cutouts, overlays and glow, so the legacy triangle
        // target is intentionally ignored on this route. Arbitrary GLB imports retain their
        // separate 1,000-triangle authoring policy.
        if (targetTriangles > 0)
            diag.Info("tbc.fidelity.target-ignored",
                $"Triangle target {targetTriangles:N0} ignored in TBC fidelity mode; preserved all {mesh.TriangleCount:N0} source triangles and render passes.");

        return (mesh, m2Bytes, texturePng, effectPngs, baseTexture.Blp, effectBlps, diag, null);
    }

    /// <summary>GET /WeaponForge/TbcPreviewWeapon — render one TBC weapon through the import
    /// pipeline (same mesh + texture the forge would package) without packaging anything.</summary>
    [HttpGet]
    public IActionResult TbcPreviewWeapon(uint entry = 0, string? model = null, uint displayRow = 0,
        string? weaponType = null, int targetTriangles = 0, int brightness = 0, int saturation = 0)
    {
        var (sel, item) = ResolveTbcSelection(entry, model, displayRow);
        if (sel is null) return NotFound(new { ok = false, error = $"Unknown TBC weapon (entry {entry}, model '{model}')." });

        var (mesh, _, texturePng, effectPngs, _, _, diag, err) = LoadTbcWeapon(sel, targetTriangles);
        if (mesh is null)
            return Json(new { ok = false, error = err, diagnostics = diag.Items.Select(i => i.ToString()) });

        string? typeKey = string.IsNullOrWhiteSpace(weaponType) && item is not null
            ? TbcItemCatalog.TypeKeyForSubclass(item.Subclass)
            : weaponType;
        var profile = WeaponTypeCatalog.Get(typeKey);
        int effectiveInventoryType = EffectiveTbcInventoryType(item, profile);
        object? grip = null;
        try { grip = BuildGripInfo(mesh, profile, _donors.Resolve(profile)); }
        catch { /* grip markers are optional for preview */ }

        var preview = _preview.RenderMesh(mesh, AdjustTexture(texturePng, brightness, saturation), effectPngs);
        return Json(new
        {
            ok = preview.Ok,
            itemEntry = item?.Entry ?? 0,
            itemName = item?.Name,
            model = sel.ModelStem,
            texture = sel.TextureStem,
            sel.DisplayRow,
            weaponType = profile.Key,
            weaponTypeLabel = profile.Label,
            inventoryType = effectiveInventoryType,
            inventoryTypeLabel = CustomWeaponBuildService.InventoryTypeLabel(effectiveInventoryType),
            vertexCount = mesh.VertexCount,
            triangleCount = mesh.TriangleCount,
            hasTexture = texturePng is { Length: > 0 },
            withinForgeBudget = mesh.TriangleCount <= MaxTbcForgeTriangles,
            grip,
            preview,
            diagnostics = diag.Items.Select(i => i.ToString()),
            note = "Preview only — nothing was packaged. Geometry, sidedness and pass order match the forge; WebGL approximates WoW multi-texture combiners and shows UV animation at its rest frame, while the forged M2 retains supported global UV tracks.",
        });
    }

    /// <summary>POST /WeaponForge/ForgeTbc — package one TBC weapon for real: its render graph is
    /// emitted as vanilla v256 on the family donor scaffold and compatible BLP2 bytes stay intact.</summary>
    [HttpPost]
    public async Task<IActionResult> ForgeTbc(uint entry = 0, string? model = null, uint displayRow = 0,
        string? name = null, string? weaponType = null, int targetTriangles = 0,
        int brightness = 0, int saturation = 0)
    {
        var (sel, item) = ResolveTbcSelection(entry, model, displayRow);
        if (sel is null) return NotFound(new { ok = false, error = $"Unknown TBC weapon (entry {entry}, model '{model}')." });

        string? typeKey = string.IsNullOrWhiteSpace(weaponType) && item is not null
            ? TbcItemCatalog.TypeKeyForSubclass(item.Subclass)
            : weaponType;
        var profile = WeaponTypeCatalog.Get(typeKey);
        WeaponDonorInfo donor;
        try { donor = _donors.Resolve(profile); }
        catch (Exception ex)
        { return Json(new { ok = false, error = $"No stock donor for {profile.Label}: {ex.Message}" }); }

        var (mesh, m2Bytes, texturePng, effectPngs, textureBlp, effectBlps, diag, err) = LoadTbcWeapon(sel, targetTriangles);
        if (mesh is null)
            return Json(new { ok = false, error = err, diagnostics = diag.Items.Select(i => i.ToString()) });
        if (mesh.TriangleCount > MaxTbcForgeTriangles)
            return Json(new
            {
                ok = false,
                error = $"{mesh.TriangleCount:N0} triangles exceeds the vanilla M2 UInt16 index budget ({MaxTbcForgeTriangles:N0}).",
            });

        // Carry the SOURCE item's own presentation fields over the family defaults: sheath is the
        // big one (Warglaives are 1H swords with the two-hander back-sheath value 1 — the crossed-
        // on-back look; the client picks back-LEFT for the main-hand slot and back-RIGHT for the
        // off-hand slot automatically), plus the authentic slot binding and swing delay. Damage
        // stays donor-level on purpose — stats are made in vanilla terms, not imported TBC power.
        Dictionary<string, string>? itemOverrides = null;
        if (item is not null)
        {
            itemOverrides = new Dictionary<string, string>
            {
                ["sheath"] = item.Sheath.ToString(),
                ["delay"] = item.DelayMs.ToString(),
            };
            if (item.InventoryType is 13 or 17 or 21 or 22)
                itemOverrides["inventory_type"] = item.InventoryType.ToString();
        }

        try
        {
            byte[]? adjustedTexturePng = AdjustTexture(texturePng, brightness, saturation);
            bool sourceGradeUnchanged = brightness == 0 && saturation == 0 ||
                ReferenceEquals(adjustedTexturePng, texturePng);
            var result = await _builder.BuildAsync(new CustomWeaponBuildRequest
            {
                Name = !string.IsNullOrWhiteSpace(name) ? name
                     : item is not null ? item.Name
                     : PrettyTbcName(sel.ModelStem),
                SourceKind = "tbc_import",
                WeaponTypeKey = profile.Key,
                ItemOverrides = itemOverrides,
                Mesh = mesh,
                Topology = WeaponTopologyMode.Variable,
                VariableTriangleHardCeiling = MaxTbcForgeTriangles,
                TexturePng = adjustedTexturePng,
                TextureBlp = sourceGradeUnchanged ? textureBlp : null,
                EffectTexturesPng = effectPngs,
                EffectTexturesBlp = effectBlps,
                SourceBlob = m2Bytes,
                GeneratorParamsJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    tbcItemEntry = item?.Entry ?? 0,
                    tbcItemName = item?.Name,
                    tbcModel = sel.ModelStem,
                    tbcTexture = sel.TextureStem,
                    tbcDisplayRow = sel.DisplayRow,
                    tbcSheath = item?.Sheath,
                    tbcInventoryType = item?.InventoryType,
                    tbcGlowPasses = mesh.Passes?.Count(p => p.BlendMode >= 3) ?? 0,
                    targetTriangles,
                }),
                WriterVersion = "tbc-rendergraph-v2",
            });
            return Json(BuildResultJson(result, BuildGripInfo(mesh, profile, donor)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WeaponForge: ForgeTbc {Model} failed", sel.ModelStem);
            return Json(new { ok = false, error = ex.Message });
        }
    }

    /// <summary>"Sword_2H_Blood_D_02" → "Sword 2H Blood D 02" — a readable default item name.</summary>
    private static string PrettyTbcName(string stem) => stem.Replace('_', ' ');

    private static int EffectiveTbcInventoryType(TbcItemInfo? item, WeaponTypeProfile profile) =>
        item?.InventoryType is 13 or 17 or 21 or 22 ? item.InventoryType : profile.InventoryType;

    /// <summary>Decode a TBC BLP2's base mip to PNG for the texture pipeline. Null on failure.</summary>
    private static byte[]? BlpToPng(byte[] blp)
    {
        try
        {
            var bgra = BlpDecoder.GetPixels(blp, 0, out int w, out int h);
            var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            using var img = SKImage.FromPixelCopy(info, bgra);
            if (img is null) return null;
            using var png = img.Encode(SKEncodedImageFormat.Png, 100);
            return png?.ToArray();
        }
        catch
        {
            return null;
        }
    }

    // ── M2 structural dump helpers (vanilla MD20 v256 fixed header offsets) ──

    private static uint HU32(byte[] b, int o) =>
        o + 4 <= b.Length ? BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o, 4)) : 0;
    private static ushort HU16(byte[] b, int o) =>
        o + 2 <= b.Length ? BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(o, 2)) : (ushort)0;
    private static float HF(byte[] b, int o) =>
        o + 4 <= b.Length ? BinaryPrimitives.ReadSingleLittleEndian(b.AsSpan(o, 4)) : 0f;
    private static float[] HV3(byte[] b, int o) => new[] { HF(b, o), HF(b, o + 4), HF(b, o + 8) };

    private static object DumpM2(byte[] m2, int expectedViews)
    {
        var doc = RawM2Document.Parse(m2, out var err);
        var validator = M2BinaryValidator.Validate(m2, expectedVertexCount: doc?.VertexCount ?? 0, expectedViews: expectedViews);

        uint nVerts = HU32(m2, 0x44), ofsVerts = HU32(m2, 0x48);
        uint nViews = HU32(m2, 0x4C), ofsViews = HU32(m2, 0x50);

        object DumpVertex(uint index)
        {
            int o = (int)(ofsVerts + index * 48);
            if (o + 48 > m2.Length) return new { index, error = "out of bounds" };
            return new
            {
                index,
                pos = HV3(m2, o),
                weights = new[] { m2[o + 12], m2[o + 13], m2[o + 14], m2[o + 15] },
                bones = new[] { m2[o + 16], m2[o + 17], m2[o + 18], m2[o + 19] },
                normal = HV3(m2, o + 20),
                uv = new[] { HF(m2, o + 32), HF(m2, o + 36) },
            };
        }

        object? DumpView(RawM2View v)
        {
            if (!v.HeaderInBounds) return new { v.Index, error = "header out of bounds" };
            object? submesh = null;
            if (v.Submeshes.Count > 0 && v.Submeshes.InBounds)
            {
                int s = (int)v.Submeshes.Offset;
                submesh = new
                {
                    id = HU32(m2, s + 0),
                    vertexStart = HU16(m2, s + 4),
                    vertexCount = HU16(m2, s + 6),
                    indexStart = HU16(m2, s + 8),
                    indexCount = HU16(m2, s + 10),
                    boneCount = HU16(m2, s + 12),
                    boneComboIndex = HU16(m2, s + 14),
                    boneInfluences = HU16(m2, s + 16),
                    centerBoneIndex = HU16(m2, s + 18),
                    center = HV3(m2, s + 20),
                    rawHex = Convert.ToHexString(m2.AsSpan(s, Math.Min(32, m2.Length - s))),
                };
            }
            object? batch = null;
            if (v.Batches.Count > 0 && v.Batches.InBounds)
            {
                int t = (int)v.Batches.Offset;
                var u16s = new ushort[12];
                for (int k = 0; k < 12; k++) u16s[k] = HU16(m2, t + k * 2);
                batch = new { fieldsU16 = u16s, rawHex = Convert.ToHexString(m2.AsSpan(t, Math.Min(24, m2.Length - t))) };
            }

            var lookupSample = new List<ushort>();
            var triSample = new List<ushort>();
            if (v.VertexLookup.InBounds)
                for (uint k = 0; k < Math.Min(8, v.VertexLookup.Count); k++)
                    lookupSample.Add(HU16(m2, (int)(v.VertexLookup.Offset + k * 2)));
            if (v.Triangles.InBounds)
                for (uint k = 0; k < Math.Min(12, v.Triangles.Count); k++)
                    triSample.Add(HU16(m2, (int)(v.Triangles.Offset + k * 2)));

            return new
            {
                v.Index,
                headerOffset = v.HeaderOffset,
                vertexLookup = new { v.VertexLookup.Count, v.VertexLookup.Offset, v.VertexLookup.InBounds },
                triangles = new { v.Triangles.Count, v.Triangles.Offset, v.Triangles.InBounds },
                properties = new { v.Properties.Count, v.Properties.Offset, v.Properties.InBounds },
                submeshes = new { v.Submeshes.Count, v.Submeshes.Offset, v.Submeshes.InBounds },
                batches = new { v.Batches.Count, v.Batches.Offset, v.Batches.InBounds },
                lod = v.Lod,
                lookupSample,
                triSample,
                submesh0 = submesh,
                batch0 = batch,
            };
        }

        var vertexSamples = new List<object>();
        if (nVerts > 0)
        {
            vertexSamples.Add(DumpVertex(0));
            if (nVerts > 2) vertexSamples.Add(DumpVertex(nVerts / 2));
            vertexSamples.Add(DumpVertex(nVerts - 1));
        }

        return new
        {
            fileSize = m2.Length,
            parseError = err,
            name = doc?.Name,
            nameLen = HU32(m2, 0x08),
            nameOfs = HU32(m2, 0x0C),
            globalFlags = HU32(m2, 0x10),
            nVertices = nVerts,
            ofsVertices = ofsVerts,
            nViews,
            ofsViews,
            vertexBox = new { min = HV3(m2, 0x0B4), max = HV3(m2, 0x0C0), radius = HF(m2, 0x0CC) },
            boundingBox = new { min = HV3(m2, 0x0D0), max = HV3(m2, 0x0DC), radius = HF(m2, 0x0E8) },
            headerHex = Convert.ToHexString(m2.AsSpan(0, Math.Min(0x100, m2.Length))),
            views = doc?.Views.Select(DumpView).ToArray(),
            vertexSamples,
            validator = validator.Items.Select(i => i.ToString()).ToArray(),
        };
    }

    private static List<string> CompareM2(byte[] donor, byte[] forged)
    {
        var notes = new List<string>();
        var dDoc = RawM2Document.Parse(donor, out _);
        var fDoc = RawM2Document.Parse(forged, out _);
        if (dDoc is null || fDoc is null) { notes.Add("parse failure — see per-file parseError"); return notes; }

        void Check(string label, bool ok, string detail) => notes.Add($"{(ok ? "OK " : "BAD")} {label}: {detail}");

        // Vertex weights: a zero weight sum collapses vertices to the origin in the client.
        uint fOfs = HU32(forged, 0x48);
        uint fN = HU32(forged, 0x44);
        int zeroWeight = 0;
        for (uint i = 0; i < fN; i++)
        {
            int o = (int)(fOfs + i * 48);
            if (o + 16 > forged.Length) break;
            if (forged[o + 12] + forged[o + 13] + forged[o + 14] + forged[o + 15] == 0) zeroWeight++;
        }
        Check("vertex weights", zeroWeight == 0, $"{zeroWeight}/{fN} vertices have all-zero bone weights");

        // Triangle indices in range of the lookup.
        var fv0 = fDoc.Views[0];
        bool triOk = true; uint triMax = 0;
        for (uint k = 0; k < fv0.Triangles.Count && fv0.Triangles.InBounds; k++)
        {
            ushort ix = HU16(forged, (int)(fv0.Triangles.Offset + k * 2));
            triMax = Math.Max(triMax, ix);
            if (ix >= fv0.VertexLookup.Count) { triOk = false; break; }
        }
        Check("triangle indices", triOk, $"max {triMax} vs lookup count {fv0.VertexLookup.Count}");

        // Bounds sanity.
        float fRadius = HF(forged, 0x0CC);
        Check("bounds radius", fRadius > 0.01f && fRadius < 50f, $"vertexBox radius {fRadius}");

        // Structural equality of the donor-templated records.
        var dv0 = dDoc.Views[0];
        if (dv0.Batches.Count > 0 && fv0.Batches.Count > 0)
        {
            var dB = donor.AsSpan((int)dv0.Batches.Offset, 24).ToArray();
            var fB = forged.AsSpan((int)fv0.Batches.Offset, 24).ToArray();
            Check("batch template", dB.AsSpan().SequenceEqual(fB), Convert.ToHexString(dB) + " vs " + Convert.ToHexString(fB));
        }
        if (dv0.Submeshes.Count > 0 && fv0.Submeshes.Count > 0)
        {
            int ds = (int)dv0.Submeshes.Offset, fs = (int)fv0.Submeshes.Offset;
            Check("submesh bone fields",
                HU16(donor, ds + 12) == HU16(forged, fs + 12) && HU16(donor, ds + 14) == HU16(forged, fs + 14) &&
                HU16(donor, ds + 16) == HU16(forged, fs + 16) && HU16(donor, ds + 18) == HU16(forged, fs + 18),
                $"donor ({HU16(donor, ds + 12)},{HU16(donor, ds + 14)},{HU16(donor, ds + 16)},{HU16(donor, ds + 18)}) vs " +
                $"forged ({HU16(forged, fs + 12)},{HU16(forged, fs + 14)},{HU16(forged, fs + 16)},{HU16(forged, fs + 18)})");
            Check("submesh id", HU32(donor, ds) == HU32(forged, fs), $"donor {HU32(donor, ds)} vs forged {HU32(forged, fs)}");
        }
        Check("view lod dword", dv0.Lod == fv0.Lod, $"donor {dv0.Lod} vs forged {fv0.Lod}");
        Check("view count", fDoc.Views.Count == dDoc.Views.Count, $"donor {dDoc.Views.Count} vs forged {fDoc.Views.Count}");

        // Every forged view array must be in bounds.
        foreach (var v in fDoc.Views)
            Check($"view {v.Index} arrays in bounds",
                v.HeaderInBounds && v.VertexLookup.InBounds && v.Triangles.InBounds &&
                v.Properties.InBounds && v.Submeshes.InBounds && v.Batches.InBounds,
                "vertexLookup/triangles/properties/submeshes/batches");

        return notes;
    }

    /// <summary>Brightness/saturation grade on the embedded texture (−100..+100 each), applied
    /// IDENTICALLY for preview and forge so what you see is what packages. Brightness is
    /// multiplicative (+100 ≈ ×2, −100 ≈ ×½ — blacks stay black); saturation blends toward
    /// (−) or away from (+) luminance grey. Zero/zero returns the input untouched.</summary>
    private static byte[]? AdjustTexture(byte[]? png, int brightness, int saturation)
    {
        if (png is null || (brightness == 0 && saturation == 0)) return png;
        try
        {
            using var src = SKBitmap.Decode(png);
            if (src is null) return png;
            float bright = MathF.Pow(2f, Math.Clamp(brightness, -100, 100) / 100f);
            float sat = 1f + Math.Clamp(saturation, -100, 100) / 100f;
            const float lr = 0.2126f, lg = 0.7152f, lb = 0.0722f;
            float sr = (1 - sat) * lr, sg = (1 - sat) * lg, sb = (1 - sat) * lb;
            var m = new float[]
            {
                (sr + sat) * bright, sg * bright,         sb * bright,         0, 0,
                sr * bright,         (sg + sat) * bright, sb * bright,         0, 0,
                sr * bright,         sg * bright,         (sb + sat) * bright, 0, 0,
                0, 0, 0, 1, 0,
            };
            using var surface = SKSurface.Create(new SKImageInfo(src.Width, src.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
            using var paint = new SKPaint { ColorFilter = SKColorFilter.CreateColorMatrix(m) };
            surface.Canvas.DrawBitmap(src, 0, 0, paint);
            surface.Canvas.Flush();
            using var img = surface.Snapshot();
            using var outPng = img.Encode(SKEncodedImageFormat.Png, 95);
            return outPng.ToArray();
        }
        catch { return png; }
    }

    private static async Task<(byte[]? Bytes, string? Error)> ReadBounded(IFormFile? file, long maxBytes)
    {
        if (file is null || file.Length == 0) return (null, "No file uploaded.");
        if (file.Length > maxBytes) return (null, $"File is {file.Length:N0} bytes; limit is {maxBytes:N0}.");
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var bytes = ms.ToArray();
        if (bytes.Length > maxBytes) return (null, "File exceeds the size limit.");
        return (bytes, null);
    }

    private byte[]? SafeExtract(string mpqPath)
    {
        try { return _mpq.ExtractFile(mpqPath); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WeaponForge: extract failed for {Path}", mpqPath);
            return null;
        }
    }
}
