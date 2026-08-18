using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using SkiaSharp;
using MangosSuperUI.Models;
using MangosSuperUI.Services;
using MangosSuperUI.Services.WeaponForge;
using MangosSuperUI.Services.WeaponForge.RawM2;
using Microsoft.AspNetCore.Mvc;

namespace MangosSuperUI.Controllers;

/// <summary>
/// Weapon Forge (WEAPON_GEN.md) HTTP surface. Everything here is build/staging only — it never
/// applies SQL, copies into a live client Data directory, reloads the core, or restarts a client.
/// Those remain owner-acceptance actions. These endpoints let the owner exercise the offline
/// pipeline against real client bytes (extracted on demand from the mounted MPQ archives).
/// </summary>
public class WeaponForgeController : Controller
{
    // Golden donor fixture paths (WEAPON_GEN.md §13.3).
    private const string DonorM2Path = @"ITEM\ObjectComponents\WEAPON\Sword_1H_Short_A_01.m2";
    private const string DonorBlpPath = @"ITEM\ObjectComponents\WEAPON\Sword_1H_Short_A_01Blue.blp";

    private readonly MpqReaderService _mpq;
    private readonly WeaponPreviewService _preview;
    private readonly WeaponIdReservationService _ids;
    private readonly CustomWeaponBuildService _builder;
    private readonly IWeaponMeshWriter _writer;
    private readonly GlbWeaponImporter _glbImporter;
    private readonly ComfyUIWeapon3DGenerator _image3d;
    private readonly WeaponPipelineProfileService _pipelineProfiles;
    private readonly TripoWeapon3DProvider _tripo;
    private readonly WeaponSketchViewService _sketchViews;
    private readonly ComfyUIDispatcher _dispatcher;
    private readonly ItemRetextureService _retex;
    private readonly BlpWriterService _blp;
    private readonly IWebHostEnvironment _env;
    private readonly ConnectionFactory _db;
    private readonly ILogger<WeaponForgeController> _logger;

    // Bounded upload limits for the inbound file surfaces.
    private const long MaxGlbBytes = 32 * 1024 * 1024;    // 32 MB
    private const long MaxImageBytes = 16 * 1024 * 1024;  // 16 MB

    public WeaponForgeController(MpqReaderService mpq, WeaponPreviewService preview,
        WeaponIdReservationService ids, CustomWeaponBuildService builder,
        IWeaponMeshWriter writer, GlbWeaponImporter glbImporter, ComfyUIWeapon3DGenerator image3d,
        WeaponPipelineProfileService pipelineProfiles, TripoWeapon3DProvider tripo,
        WeaponSketchViewService sketchViews, ComfyUIDispatcher dispatcher, ItemRetextureService retex,
        BlpWriterService blp, IWebHostEnvironment env, ConnectionFactory db,
        ILogger<WeaponForgeController> logger)
    {
        _mpq = mpq;
        _preview = preview;
        _ids = ids;
        _builder = builder;
        _writer = writer;
        _glbImporter = glbImporter;
        _image3d = image3d;
        _pipelineProfiles = pipelineProfiles;
        _tripo = tripo;
        _sketchViews = sketchViews;
        _dispatcher = dispatcher;
        _retex = retex;
        _blp = blp;
        _env = env;
        _db = db;
        _logger = logger;
    }

    /// <summary>Uniform response for every full weapon build: ids, hashes, direct downloads for the
    /// straight patch-4.MPQ and the item SQL (no ZIP), preview, and diagnostics.</summary>
    private object BuildResultJson(CustomWeaponBuildResult r) => new
    {
        ok = true,
        r.BuildId,
        r.ItemEntry,
        r.DisplayId,
        r.ModelIndex,
        r.Name,
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
        apply = r.Apply,
        diagnostics = r.Diagnostics,
    };

    /// <summary>GET /WeaponForge — the Item Assets page (Game Development). The UI drives the
    /// endpoints below; nothing on it applies to a live server or client.</summary>
    [HttpGet]
    public IActionResult Index() => View();

    /// <summary>GET /WeaponForge/Status — foundation self-checks: fixture integrity, contract
    /// version, and whether the golden donor resolves from the mounted archives.</summary>
    [HttpGet]
    public IActionResult Status()
    {
        var donorM2 = SafeExtract(DonorM2Path);
        var donorBlp = SafeExtract(DonorBlpPath);
        return Json(new
        {
            fixtureVerified = DonorItemTemplateFixture.Verify(),
            fixtureSha = DonorItemTemplateFixture.ExpectedSha256,
            coordinateContractVersion = CoordinateContract.Version,
            donorM2Found = donorM2 is not null,
            donorM2Bytes = donorM2?.Length ?? 0,
            donorBlpFound = donorBlp is not null,
            donorBlpBytes = donorBlp?.Length ?? 0,
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
    /// from the raw bytes (content-hash addressed, no display-id lookup). Proves the direct preview
    /// path the display-id-driven EnsureGlb cannot do.</summary>
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

    /// <summary>POST /WeaponForge/UploadGlb (multipart, field "file") — import an arbitrary GLB (a
    /// sketch reconstructed by TRELLIS, or any single-mesh weapon), normalize it to the sword
    /// envelope, validate it, and preview the mesh WITHOUT packaging anything. ForgeGlb is the
    /// package-it-for-real counterpart.</summary>
    [HttpPost]
    [RequestSizeLimit(MaxGlbBytes)]
    public async Task<IActionResult> UploadGlb(IFormFile? file, bool reorient = true)
    {
        var (bytes, err) = await ReadBounded(file, MaxGlbBytes);
        if (err is not null) return BadRequest(new { ok = false, error = err });
        if (bytes!.Length < 12 || !(bytes[0] == 'g' && bytes[1] == 'l' && bytes[2] == 'T' && bytes[3] == 'F'))
            return BadRequest(new { ok = false, error = "Not a binary glTF (.glb): missing 'glTF' magic." });

        var result = _glbImporter.Import(bytes, new GlbImportOptions { Reorient = reorient });
        WeaponPreviewResult? preview = result.Mesh is not null ? _preview.RenderMesh(result.Mesh, result.TexturePng) : null;

        return Json(new
        {
            ok = result.Ok && preview?.Ok == true,
            result.VertexCount,
            result.TriangleCount,
            result.SourceSha256,
            hasTexture = result.TexturePng is { Length: > 0 },
            normalization = result.Mesh?.Normalization,
            preview,
            diagnostics = result.Diagnostics.Items.Select(i => i.ToString()),
            note = "Preview only — nothing was packaged. Forge builds it into the game.",
        });
    }

    /// <summary>POST /WeaponForge/UploadSketch (multipart, field "file") — the hand-drawn entry point.
    /// Dispatches the image to the image→3D generator (owner-operated TRELLIS worker). When no worker
    /// is configured it returns the manual flow; when one is, the returned GLB is imported and previewed.</summary>
    [HttpPost]
    [RequestSizeLimit(MaxImageBytes)]
    public async Task<IActionResult> UploadSketch(IFormFile? file, CancellationToken ct)
    {
        var (bytes, err) = await ReadBounded(file, MaxImageBytes);
        if (err is not null) return BadRequest(new { ok = false, error = err });
        if (!LooksLikeImage(bytes!))
            return BadRequest(new { ok = false, error = "Uploaded file is not a PNG or JPEG image." });

        var gen = await _image3d.GenerateGlbAsync(bytes!, ct);
        if (!gen.Ok || gen.Glb is null)
            return Json(new { ok = false, configured = _image3d.IsConfigured, message = gen.Message });

        var result = _glbImporter.Import(gen.Glb, new GlbImportOptions());
        WeaponPreviewResult? preview = result.Mesh is not null ? _preview.RenderMesh(result.Mesh, result.TexturePng) : null;
        return Json(new
        {
            ok = result.Ok && preview?.Ok == true,
            result.VertexCount,
            result.TriangleCount,
            preview,
            diagnostics = result.Diagnostics.Items.Select(i => i.ToString()),
        });
    }

    /// <summary>GET /WeaponForge/GenerateSword?bladeLength=0.75&amp;bladeWidth=0.09&amp;bladeSegments=10 —
    /// Route A. Generates a parametric sword, writes it through the variable-topology M2 writer, and
    /// previews it. Proves params → mesh → valid M2 → preview, all offline.</summary>
    [HttpGet]
    public IActionResult GenerateSword(float bladeLength = 0.75f, float bladeWidth = 0.09f, int bladeSegments = 10)
    {
        var p = new SwordParams { BladeLength = bladeLength, BladeWidth = bladeWidth, BladeSegments = Math.Clamp(bladeSegments, 3, 40) };
        var mesh = ParametricSwordGenerator.Generate(p);

        var diag = new ForgeDiagnostics("generate");
        var meshDiag = RigidWeaponMeshValidator.Validate(mesh, new MeshValidationOptions { Topology = WeaponTopologyMode.Variable });
        diag.AddRange(meshDiag);

        byte[]? m2 = meshDiag.HasErrors ? null : _writer.WriteM2(mesh, new WeaponWriteContext { ModelIndex = 1 }, diag);
        // Preview the mesh geometry directly (no generated texture yet).
        var preview = _preview.RenderMesh(mesh, null);

        var regionCounts = (mesh.TriangleRegionIds ?? Array.Empty<string>())
            .GroupBy(r => r).ToDictionary(g => g.Key, g => g.Count());

        return Json(new
        {
            ok = preview.Ok,
            vertexCount = mesh.VertexCount,
            triangleCount = mesh.TriangleCount,
            regionCounts,
            m2Written = m2 is not null,
            m2Bytes = m2?.Length ?? 0,
            preview,
            diagnostics = diag.Items.Select(i => i.ToString()),
        });
    }

    /// <summary>POST /WeaponForge/ForgeSword — Route A end-to-end: generate the parametric sword AND
    /// package it for real. keepDonorName=true is a debug lever that skips the internal-name rewrite
    /// so the rename can be isolated in the reference client.</summary>
    [HttpPost]
    public async Task<IActionResult> ForgeSword(float bladeLength = 0.75f, float bladeWidth = 0.09f,
        int bladeSegments = 10, string? name = null, bool keepDonorName = false)
    {
        try
        {
            var p = new SwordParams { BladeLength = bladeLength, BladeWidth = bladeWidth, BladeSegments = Math.Clamp(bladeSegments, 3, 40) };
            var mesh = ParametricSwordGenerator.Generate(p);

            var result = await _builder.BuildAsync(new CustomWeaponBuildRequest
            {
                Name = name,
                SourceKind = "parametric",
                Mesh = mesh,
                Topology = WeaponTopologyMode.Variable,
                GeneratorParamsJson = System.Text.Json.JsonSerializer.Serialize(p),
                WriterVersion = "variable-topology-v1",
                KeepDonorInternalName = keepDonorName,
            });
            return Json(BuildResultJson(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WeaponForge: ForgeSword failed");
            return Json(new { ok = false, error = ex.Message });
        }
    }

    /// <summary>POST /WeaponForge/ForgeGlb (multipart, field "file") — Route B end-to-end for an
    /// existing GLB: import + normalize, then package it for real into the unified patch-4.MPQ. The
    /// GLB's embedded texture (when present) becomes the weapon's BLP.</summary>
    [HttpPost]
    [RequestSizeLimit(MaxGlbBytes)]
    public async Task<IActionResult> ForgeGlb(IFormFile? file, string? name = null, bool reorient = true)
    {
        var (bytes, err) = await ReadBounded(file, MaxGlbBytes);
        if (err is not null) return BadRequest(new { ok = false, error = err });
        if (bytes!.Length < 12 || !(bytes[0] == 'g' && bytes[1] == 'l' && bytes[2] == 'T' && bytes[3] == 'F'))
            return BadRequest(new { ok = false, error = "Not a binary glTF (.glb): missing 'glTF' magic." });

        var import = _glbImporter.Import(bytes, new GlbImportOptions { Reorient = reorient });
        if (!import.Ok || import.Mesh is null)
            return Json(new
            {
                ok = false,
                error = "GLB import failed — fix the model and retry.",
                diagnostics = import.Diagnostics.Items.Select(i => i.ToString()),
            });

        try
        {
            var result = await _builder.BuildAsync(new CustomWeaponBuildRequest
            {
                Name = name,
                SourceKind = "glb_import",
                Mesh = import.Mesh,
                Topology = WeaponTopologyMode.Variable,
                TexturePng = import.TexturePng,
                SourceBlob = bytes,
                WriterVersion = "variable-topology-v1",
            });
            return Json(BuildResultJson(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WeaponForge: ForgeGlb failed");
            return Json(new { ok = false, error = ex.Message });
        }
    }

    /// <summary>POST /WeaponForge/ForgeSketch (multipart, field "file") — the hand-drawn flow end to
    /// end: sketch → image→3D on the ComfyUI pool → GLB import → unified patch-4.MPQ. Until the
    /// image→3D workflow is installed this returns the setup guidance instead.</summary>
    [HttpPost]
    [RequestSizeLimit(MaxImageBytes)]
    public async Task<IActionResult> ForgeSketch(IFormFile? file, string? name, CancellationToken ct)
    {
        var (bytes, err) = await ReadBounded(file, MaxImageBytes);
        if (err is not null) return BadRequest(new { ok = false, error = err });
        if (!LooksLikeImage(bytes!))
            return BadRequest(new { ok = false, error = "Uploaded file is not a PNG or JPEG image." });

        var gen = await _image3d.GenerateGlbAsync(bytes!, ct);
        if (!gen.Ok || gen.Glb is null)
            return Json(new { ok = false, configured = _image3d.IsConfigured, message = gen.Message });

        var import = _glbImporter.Import(gen.Glb, new GlbImportOptions());
        if (!import.Ok || import.Mesh is null)
            return Json(new
            {
                ok = false,
                error = "The reconstructed 3D model failed import — try a cleaner, front-facing sketch.",
                diagnostics = import.Diagnostics.Items.Select(i => i.ToString()),
            });

        try
        {
            var result = await _builder.BuildAsync(new CustomWeaponBuildRequest
            {
                Name = name,
                SourceKind = "sketch3d",
                Mesh = import.Mesh,
                Topology = WeaponTopologyMode.Variable,
                TexturePng = import.TexturePng,
                SourceBlob = bytes,
                WriterVersion = "variable-topology-v1",
            });
            return Json(BuildResultJson(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WeaponForge: ForgeSketch failed");
            return Json(new { ok = false, error = ex.Message });
        }
    }

    // ── Material-zone atlas (WEAPON_FORGE.md "quadrant atlas") ──────────────────────────────────
    // A previewed import is carved into four axis zones (blade/guard/grip/pommel); each zone is
    // planar-projected into one quadrant of a 2×2 atlas, and each quadrant is filled independently
    // (solid colour, uploaded image, or a locally generated texture). The already-normalized preview
    // GLB IS the geometry source, so this works identically for a GLB upload and a sketch result
    // without persisting a session mesh — the split is keyed on position, not triangle index.

    /// <summary>POST /WeaponForge/PreviewZonedTexture — reproject the previewed mesh's UVs into the
    /// four zone quadrants, composite the atlas from the supplied fills, and re-preview. Packages
    /// nothing.</summary>
    [HttpPost]
    public IActionResult PreviewZonedTexture(string previewPath, float bladeGuard, float guardGrip,
        float gripPommel, string? fillsJson)
    {
        var (mesh, err) = LoadPreviewMesh(previewPath);
        if (mesh is null) return BadRequest(new { ok = false, error = err });

        var boundaries = new ZoneBoundaries(bladeGuard, guardGrip, gripPommel);
        var zoned = WeaponZoneAtlas.WithZonedUv(mesh, boundaries, out var triZones);
        var atlasPng = WeaponAtlasComposer.ComposePng(ParseFills(fillsJson));
        var preview = _preview.RenderMesh(zoned, atlasPng);

        return Json(new
        {
            ok = preview.Ok,
            preview,
            atlasDataUrl = "data:image/png;base64," + Convert.ToBase64String(atlasPng),
            boundaries = new { boundaries.BladeGuard, boundaries.GuardGrip, boundaries.GripPommel },
            zoneTriangleCounts = ZoneCounts(triZones),
            zoneNames = WeaponZoneAtlas.ZoneNames,
            note = "Preview only — quadrant UVs rewritten, nothing packaged.",
        });
    }

    /// <summary>POST /WeaponForge/ForgeZonedTexture — the package-it counterpart: build the zoned
    /// mesh + composited atlas into a real item through the one packaging path.</summary>
    [HttpPost]
    public async Task<IActionResult> ForgeZonedTexture(string previewPath, string? name,
        float bladeGuard, float guardGrip, float gripPommel, string? fillsJson)
    {
        var (mesh, err) = LoadPreviewMesh(previewPath);
        if (mesh is null) return BadRequest(new { ok = false, error = err });

        var boundaries = new ZoneBoundaries(bladeGuard, guardGrip, gripPommel);
        var zoned = WeaponZoneAtlas.WithZonedUv(mesh, boundaries, out _);
        var atlasPng = WeaponAtlasComposer.ComposePng(ParseFills(fillsJson));

        try
        {
            var result = await _builder.BuildAsync(new CustomWeaponBuildRequest
            {
                Name = name,
                SourceKind = "glb_zoned",
                Mesh = zoned,
                Topology = WeaponTopologyMode.Variable,
                TexturePng = atlasPng,
                WriterVersion = "variable-topology-v1",
            });
            return Json(BuildResultJson(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WeaponForge: ForgeZonedTexture failed");
            return Json(new { ok = false, error = ex.Message });
        }
    }

    /// <summary>POST /WeaponForge/GenerateCellTexture — run a bare Flux prompt through the local
    /// ComfyUI (the same engine as the Retexture Engine) and return a PNG data URL to drop into one
    /// atlas quadrant. Returns ok:false with guidance when no node is online.</summary>
    [HttpPost]
    public async Task<IActionResult> GenerateCellTexture(string? prompt, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return BadRequest(new { ok = false, error = "Enter a prompt for the material (e.g. \"polished steel blade, seamless\")." });

        var png = await _retex.GenerateTexturePngAsync(prompt, 512, 256, ct);
        if (png is null)
            return Json(new { ok = false, error = "No local ComfyUI node is online, or generation failed. Configure it under the Retexture Engine / setup dialog." });

        return Json(new { ok = true, imageDataUrl = "data:image/png;base64," + Convert.ToBase64String(png) });
    }

    /// <summary>Resolve a previously rendered preview GLB (under wwwroot/weapon_forge_cache) back to
    /// a normalized RigidWeaponMesh. The preview is already in the sword envelope, so it is imported
    /// with Reorient=false — geometry, not orientation, is what the zone split needs.</summary>
    private (RigidWeaponMesh? Mesh, string? Error) LoadPreviewMesh(string? previewPath)
    {
        if (string.IsNullOrWhiteSpace(previewPath))
            return (null, "No preview to texture — run a GLB/sketch preview first.");
        var fileName = Path.GetFileName(previewPath); // strips any path traversal
        if (!fileName.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
            return (null, "Preview path must be a .glb.");
        var full = Path.Combine(_env.WebRootPath, "weapon_forge_cache", fileName);
        if (!System.IO.File.Exists(full))
            return (null, "Preview GLB not found in cache — re-run the preview.");

        byte[] bytes;
        try { bytes = System.IO.File.ReadAllBytes(full); }
        catch (Exception ex) { return (null, $"Could not read preview GLB: {ex.Message}"); }

        var import = _glbImporter.Import(bytes, new GlbImportOptions { Reorient = false });
        if (!import.Ok || import.Mesh is null)
            return (null, "Preview GLB failed re-import — " + string.Join("; ", import.Diagnostics.Items.Select(i => i.ToString())));
        return (import.Mesh, null);
    }

    /// <summary>Parse the UI's fills payload (a JSON array of {kind,color,image}) into exactly four
    /// cell fills in zone order, padding with neutral solids.</summary>
    private static List<WeaponCellFill> ParseFills(string? fillsJson)
    {
        var fills = new List<WeaponCellFill>();
        if (!string.IsNullOrWhiteSpace(fillsJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(fillsJson);
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    string kind = el.TryGetProperty("kind", out var k) ? (k.GetString() ?? "solid") : "solid";
                    string? color = el.TryGetProperty("color", out var c) ? c.GetString() : null;
                    byte[]? png = null;
                    if (el.TryGetProperty("image", out var img) && img.ValueKind == JsonValueKind.String)
                        png = DecodeDataUrl(img.GetString());
                    fills.Add(new WeaponCellFill
                    {
                        Kind = png is { Length: > 0 } && !string.Equals(kind, "solid", StringComparison.OrdinalIgnoreCase)
                            ? WeaponCellFillKind.Image : WeaponCellFillKind.Solid,
                        ColorHex = color,
                        ImagePng = png,
                    });
                }
            }
            catch { /* fall through to padding */ }
        }
        while (fills.Count < 4) fills.Add(new WeaponCellFill { Kind = WeaponCellFillKind.Solid });
        return fills.Take(4).ToList();
    }

    private static byte[]? DecodeDataUrl(string? dataUrl)
    {
        if (string.IsNullOrWhiteSpace(dataUrl)) return null;
        int comma = dataUrl.IndexOf(',');
        var b64 = comma >= 0 ? dataUrl[(comma + 1)..] : dataUrl;
        try { return Convert.FromBase64String(b64); }
        catch { return null; }
    }

    private static Dictionary<string, int> ZoneCounts(int[] triZones)
    {
        var d = new Dictionary<string, int>();
        foreach (var z in triZones)
        {
            var name = WeaponZoneAtlas.ZoneNames[z];
            d[name] = d.TryGetValue(name, out var v) ? v + 1 : 1;
        }
        return d;
    }

    // ── Texture an existing model: local img2img refine (WEAPON_FORGE.md) ───────────────────────
    // Upload a GLB that already carries a baked texture, add intricacy with a prompt via local
    // ComfyUI/Flux img2img. The source is already registered to the model's UVs, so detail lands
    // correctly — no unwrap/"which-way-is-up" problem. Preview refines once and returns the atlas;
    // Forge re-uses that atlas so Flux is not run twice.

    /// <summary>POST /WeaponForge/RefineGlbTexture (multipart "file" + prompt + denoise) — import the
    /// GLB, pull its embedded texture, and refine it with a local img2img pass. Preview only.</summary>
    [HttpPost]
    [RequestSizeLimit(MaxGlbBytes)]
    public async Task<IActionResult> RefineGlbTexture(IFormFile? file, string? prompt, float denoise, CancellationToken ct)
    {
        var (bytes, err) = await ReadBounded(file, MaxGlbBytes);
        if (err is not null) return BadRequest(new { ok = false, error = err });
        if (string.IsNullOrWhiteSpace(prompt))
            return BadRequest(new { ok = false, error = "Enter a prompt describing the intricacy to add (e.g. \"ornate runes, gold filigree, weathered steel\")." });

        var import = _glbImporter.Import(bytes!, new GlbImportOptions { Reorient = true });
        if (!import.Ok || import.Mesh is null)
            return Json(new { ok = false, error = "GLB import failed.", diagnostics = import.Diagnostics.Items.Select(i => i.ToString()) });
        if (import.TexturePng is not { Length: > 0 })
            return Json(new { ok = false, error = "This GLB has no embedded texture to refine. Use the Texture Zones panel (colour / upload / generate) to build one from scratch instead." });

        var refined = await _retex.RefineTexturePngAsync(import.TexturePng, prompt!, 512, denoise <= 0 ? 0.45f : denoise, ct);
        if (refined is null)
            return Json(new { ok = false, error = "Refine failed — no local ComfyUI node online, or the img2img job failed." });

        var preview = _preview.RenderMesh(import.Mesh, refined);
        return Json(new
        {
            ok = preview.Ok,
            preview,
            refinedDataUrl = "data:image/png;base64," + Convert.ToBase64String(refined),
            import.TriangleCount, import.VertexCount,
            note = "Preview only — refined texture mapped through the model's own UVs. Forge to package it.",
        });
    }

    /// <summary>POST /WeaponForge/ForgeRefinedGlb (multipart "file" + name + refinedDataUrl) — package
    /// the GLB with the already-refined atlas (no second Flux pass) through the one build path.</summary>
    [HttpPost]
    [RequestSizeLimit(MaxGlbBytes)]
    public async Task<IActionResult> ForgeRefinedGlb(IFormFile? file, string? name, string? refinedDataUrl)
    {
        var (bytes, err) = await ReadBounded(file, MaxGlbBytes);
        if (err is not null) return BadRequest(new { ok = false, error = err });
        var atlas = DecodeDataUrl(refinedDataUrl);
        if (atlas is not { Length: > 0 })
            return BadRequest(new { ok = false, error = "No refined texture to forge — run Refine first." });

        var import = _glbImporter.Import(bytes!, new GlbImportOptions { Reorient = true });
        if (!import.Ok || import.Mesh is null)
            return Json(new { ok = false, error = "GLB import failed.", diagnostics = import.Diagnostics.Items.Select(i => i.ToString()) });

        try
        {
            var result = await _builder.BuildAsync(new CustomWeaponBuildRequest
            {
                Name = name,
                SourceKind = "glb_refined",
                Mesh = import.Mesh,
                Topology = WeaponTopologyMode.Variable,
                TexturePng = atlas,
                SourceBlob = bytes,
                WriterVersion = "variable-topology-v1",
            });
            return Json(BuildResultJson(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WeaponForge: ForgeRefinedGlb failed");
            return Json(new { ok = false, error = ex.Message });
        }
    }

    // ── Rework a FORGED weapon: view it, refine its texture, apply in place ──────────────────────
    // Pull an already-forged weapon back out of the registry: render its stored M2+BLP into the
    // viewer, refine its texture with local img2img, and write the new BLP back (mesh untouched) —
    // then RebuildPatchAsync repackages + redeploys patch-5. The geometry never changes; only the
    // texture is reworked, so no re-validation of triangle budget is needed.

    /// <summary>GET /WeaponForge/PreviewForged?displayId= — render a forged weapon's stored M2+BLP
    /// into a preview GLB so it can be inspected/reworked in the viewer.</summary>
    [HttpGet]
    public async Task<IActionResult> PreviewForged(long displayId)
    {
        var (m2, blp) = await LoadForgedBytesAsync(displayId);
        if (m2 is null) return NotFound(new { ok = false, error = $"No stored M2 for display id {displayId}." });
        var preview = _preview.RenderFromBytes(m2, blp);
        return Json(new { ok = preview.Ok, preview, hasTexture = blp is { Length: > 0 }, displayId });
    }

    /// <summary>POST /WeaponForge/ReworkForgedTexture — decode the forged weapon's BLP, refine it with
    /// a local img2img pass, and re-preview. Commits nothing; ApplyForgedTexture writes it back.</summary>
    [HttpPost]
    public async Task<IActionResult> ReworkForgedTexture(long displayId, string? prompt, float denoise, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return BadRequest(new { ok = false, error = "Enter a prompt for the rework (e.g. \"ornate runes, gold filigree\")." });
        var (m2, blp) = await LoadForgedBytesAsync(displayId);
        if (m2 is null) return NotFound(new { ok = false, error = $"No stored weapon for display id {displayId}." });
        if (blp is not { Length: > 0 }) return Json(new { ok = false, error = "This weapon has no stored texture to rework." });

        var srcPng = BlpToPng(blp);
        if (srcPng is null) return Json(new { ok = false, error = "Could not decode the stored BLP." });

        var refined = await _retex.RefineTexturePngAsync(srcPng, prompt!, 512, denoise <= 0 ? 0.45f : denoise, ct);
        if (refined is null) return Json(new { ok = false, error = "Refine failed — no local ComfyUI node online, or the img2img job failed." });

        var previewBlp = EncodeAtlasBlp(refined);
        var preview = previewBlp is not null ? _preview.RenderFromBytes(m2, previewBlp) : _preview.RenderFromBytes(m2, blp);
        return Json(new
        {
            ok = preview.Ok, preview, displayId,
            refinedDataUrl = "data:image/png;base64," + Convert.ToBase64String(refined),
            note = "Preview only — texture reworked over the model's existing UVs. Apply to write it to the server.",
        });
    }

    /// <summary>POST /WeaponForge/ApplyForgedTexture — write the reworked texture back to the weapon's
    /// display row and repackage/redeploy patch-5. The mesh is untouched.</summary>
    [HttpPost]
    public async Task<IActionResult> ApplyForgedTexture(long displayId, string? refinedDataUrl)
    {
        var png = DecodeDataUrl(refinedDataUrl);
        if (png is not { Length: > 0 }) return BadRequest(new { ok = false, error = "No reworked texture — run Refine first." });
        var blp = EncodeAtlasBlp(png);
        if (blp is null) return Json(new { ok = false, error = "Could not encode the reworked texture to BLP." });

        string sha = Convert.ToHexString(SHA256.HashData(blp)).ToLowerInvariant();
        int rows;
        await using (var conn = _db.Admin())
        {
            await conn.OpenAsync();
            rows = await conn.ExecuteAsync(
                "UPDATE custom_weapon_display SET compiled_blp = @blp, blp_sha256 = @sha WHERE display_id = @displayId",
                new { blp, sha, displayId });
        }
        if (rows == 0) return NotFound(new { ok = false, error = $"No display row for id {displayId}." });

        try
        {
            var summary = await _builder.RebuildPatchAsync($"reworked texture for display {displayId}");
            return Json(new
            {
                ok = true, displayId,
                patchDeployed = summary.PatchDeployed,
                patchDeployMessage = summary.PatchDeployMessage,
                mpqSha256 = summary.MpqSha256,
                patchDownloadUrl = summary.PatchRemoved ? null : "/WeaponForge/DownloadPatch",
                message = "Texture applied and patch redeployed. Restart the client to see it.",
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WeaponForge: ApplyForgedTexture rebuild failed for {DisplayId}", displayId);
            return Json(new { ok = false, error = "Texture saved but patch rebuild failed: " + ex.Message });
        }
    }

    /// <summary>Load a forged weapon's compiled M2 (+ BLP) from the registry tables (model_id == display_id).</summary>
    private async Task<(byte[]? M2, byte[]? Blp)> LoadForgedBytesAsync(long displayId)
    {
        await using var conn = _db.Admin();
        await conn.OpenAsync();
        var row = await conn.QueryFirstOrDefaultAsync(
            @"SELECT m.compiled_m2 AS M2, d.compiled_blp AS Blp
              FROM custom_weapon_model m
              LEFT JOIN custom_weapon_display d ON d.model_id = m.model_id
              WHERE m.model_id = @displayId", new { displayId });
        if (row is null) return (null, null);
        return ((byte[]?)row.M2, (byte[]?)row.Blp);
    }

    /// <summary>Decode a stored BLP2 (mip 0) to PNG bytes for the img2img source.</summary>
    private static byte[]? BlpToPng(byte[] blp)
    {
        try
        {
            var bgra = BlpDecoder.GetPixels(blp, 0, out int w, out int h);
            if (w <= 0 || h <= 0 || bgra.Length < w * h * 4) return null;
            var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            using var bmp = new SKBitmap(info);
            System.Runtime.InteropServices.Marshal.Copy(bgra, 0, bmp.GetPixels(), bgra.Length);
            using var img = SKImage.FromBitmap(bmp);
            using var png = img.Encode(SKEncodedImageFormat.Png, 100);
            return png?.ToArray();
        }
        catch { return null; }
    }

    /// <summary>Encode a PNG to a 256² DXT1 BLP (the weapon texture envelope).</summary>
    private byte[]? EncodeAtlasBlp(byte[] png)
    {
        try
        {
            using var src = SKBitmap.Decode(png);
            if (src is null) return null;
            using var resized = src.Resize(new SKImageInfo(256, 256, SKColorType.Rgba8888, SKAlphaType.Unpremul),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            return resized is null ? null : _blp.EncodeBitmapToBlp(resized, useDxt1: true);
        }
        catch { return null; }
    }

    /// <summary>GET /WeaponForge/PipelineProfile — public/masked staged-pipeline settings. The API
    /// key is write-only and export intentionally excludes it.</summary>
    [HttpGet]
    public async Task<IActionResult> PipelineProfile()
    {
        var p = await _pipelineProfiles.LoadAsync();
        return Json(new { ok = true, profile = WeaponPipelineProfileService.Public(p) });
    }

    /// <summary>POST /WeaponForge/SavePipelineProfile — configure the production sketch provider
    /// entirely from this page. A blank key preserves the stored key.</summary>
    [HttpPost]
    public async Task<IActionResult> SavePipelineProfile(string provider = "comfyui",
        string? tripoBaseUrl = null, string? tripoModel = null, string? tripoApiKey = null,
        string? comfyNodeName = null, string? comfyBaseUrl = null,
        int targetTriangles = 800, string textureQuality = "detailed",
        bool enableImageAutofix = true, bool smartLowPoly = true)
    {
        try
        {
            var p = await _pipelineProfiles.SaveAsync(new WeaponPipelineProfile
            {
                Provider = provider,
                TripoBaseUrl = tripoBaseUrl ?? "",
                TripoModel = tripoModel ?? "",
                TripoApiKey = tripoApiKey,
                ComfyNodeName = comfyNodeName ?? "",
                ComfyBaseUrl = comfyBaseUrl ?? "",
                TargetTriangles = targetTriangles,
                TextureQuality = textureQuality,
                EnableImageAutofix = enableImageAutofix,
                SmartLowPoly = smartLowPoly,
            });
            if (p.Provider == "comfyui") _dispatcher.AddRuntimeNode(p.ComfyNodeName, p.ComfyBaseUrl);
            return Json(new { ok = true, profile = WeaponPipelineProfileService.Public(p), message = "Pipeline profile saved." });
        }
        catch (Exception ex) { return BadRequest(new { ok = false, error = ex.Message }); }
    }

    [HttpGet]
    public async Task<IActionResult> ExportPipelineProfile()
    {
        var p = await _pipelineProfiles.LoadAsync();
        return Json(WeaponPipelineProfileService.Exportable(p));
    }

    [HttpPost]
    [RequestSizeLimit(256 * 1024)]
    public async Task<IActionResult> ImportPipelineProfile(string? profileJson, IFormFile? file)
    {
        if (string.IsNullOrWhiteSpace(profileJson) && file is { Length: > 0 })
        {
            using var reader = new StreamReader(file.OpenReadStream());
            profileJson = await reader.ReadToEndAsync();
        }
        if (string.IsNullOrWhiteSpace(profileJson))
            return BadRequest(new { ok = false, error = "Paste or upload an exported pipeline profile." });
        try
        {
            var imported = JsonSerializer.Deserialize<WeaponPipelineProfile>(profileJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("Profile was empty.");
            var p = await _pipelineProfiles.SaveAsync(imported);
            if (p.Provider == "comfyui") _dispatcher.AddRuntimeNode(p.ComfyNodeName, p.ComfyBaseUrl);
            return Json(new { ok = true, profile = WeaponPipelineProfileService.Public(p), message = "Portable profile imported. Credentials were preserved separately." });
        }
        catch (Exception ex) { return BadRequest(new { ok = false, error = ex.Message }); }
    }

    /// <summary>POST /WeaponForge/PrepareSketchViews — paper cleanup + transparent crop + starter
    /// construction views. Side views are deliberately labelled guides and remain editable in the
    /// browser; they are never silently treated as observed truth.</summary>
    [HttpPost]
    [RequestSizeLimit(MaxImageBytes)]
    public async Task<IActionResult> PrepareSketchViews(IFormFile? file)
    {
        var (bytes, err) = await ReadBounded(file, MaxImageBytes);
        if (err is not null) return BadRequest(new { ok = false, error = err });
        if (!LooksLikeImage(bytes!)) return BadRequest(new { ok = false, error = "Upload a PNG or JPEG drawing." });
        try
        {
            var v = _sketchViews.Prepare(bytes!);
            string Data(byte[] png) => "data:image/png;base64," + Convert.ToBase64String(png);
            return Json(new
            {
                ok = true,
                front = Data(v.Front), back = Data(v.Back), left = Data(v.Left), right = Data(v.Right),
                threeQuarter = Data(v.ThreeQuarter), sourceAxisDegrees = v.SourceAxisRadians * 180f / MathF.PI,
                note = "Front is cleaned source. Back/edge/three-quarter images are editable construction starters, not inferred observations. Replace weak views or leave them unchecked.",
            });
        }
        catch (Exception ex) { return BadRequest(new { ok = false, error = ex.Message }); }
    }

    /// <summary>POST /WeaponForge/PreviewSketchPipeline — runs the selected provider from inspected
    /// reference views, applies bounded game-mesh corrections, then previews without packaging.</summary>
    [HttpPost]
    [RequestSizeLimit(64 * 1024 * 1024)]
    public async Task<IActionResult> PreviewSketchPipeline(IFormFile? front, IFormFile? left,
        IFormFile? back, IFormFile? right, bool straightenBlade = true, float depthScale = 1f,
        float rollDegrees = 0f, bool flipGripEnd = false, CancellationToken ct = default)
    {
        var run = await RunSketchPipeline(front, left, back, right, straightenBlade, depthScale, rollDegrees, flipGripEnd, ct);
        if (!run.Ok || run.Import?.Mesh is null)
            return Json(new { ok = false, error = run.Error, diagnostics = run.Import?.Diagnostics.Items.Select(i => i.ToString()), stage = run.Stage });
        var preview = _preview.RenderMesh(run.Import.Mesh, run.Import.TexturePng);
        return Json(new
        {
            ok = preview.Ok, stage = run.Stage, providerMessage = run.ProviderMessage,
            run.Import.VertexCount, run.Import.TriangleCount, hasTexture = run.Import.TexturePng is { Length: > 0 },
            quality = WeaponMeshQualityAnalyzer.Analyze(run.Import.Mesh), preview,
            diagnostics = run.Import.Diagnostics.Items.Select(i => i.ToString()),
            note = "Preview only. Inspect front, edge, and three-quarter angles before forging.",
        });
    }

    /// <summary>POST /WeaponForge/ForgeSketchPipeline — production workbench counterpart: the exact
    /// inspected views/settings go through the same provider/import result and the one packaging path.</summary>
    [HttpPost]
    [RequestSizeLimit(64 * 1024 * 1024)]
    public async Task<IActionResult> ForgeSketchPipeline(IFormFile? front, IFormFile? left,
        IFormFile? back, IFormFile? right, string? name, bool straightenBlade = true,
        float depthScale = 1f, float rollDegrees = 0f, bool flipGripEnd = false, CancellationToken ct = default)
    {
        var run = await RunSketchPipeline(front, left, back, right, straightenBlade, depthScale, rollDegrees, flipGripEnd, ct);
        if (!run.Ok || run.Import?.Mesh is null)
            return Json(new { ok = false, error = run.Error, diagnostics = run.Import?.Diagnostics.Items.Select(i => i.ToString()), stage = run.Stage });
        try
        {
            var source = await ReadFormFile(front!, MaxImageBytes);
            var result = await _builder.BuildAsync(new CustomWeaponBuildRequest
            {
                Name = name,
                SourceKind = "sketch_pipeline",
                Mesh = run.Import.Mesh,
                Topology = WeaponTopologyMode.Variable,
                TexturePng = run.Import.TexturePng,
                SourceBlob = source,
                WriterVersion = "variable-topology-v1",
            });
            return Json(BuildResultJson(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WeaponForge: production sketch pipeline forge failed");
            return Json(new { ok = false, error = ex.Message });
        }
    }

    private async Task<SketchPipelineRun> RunSketchPipeline(IFormFile? front, IFormFile? left,
        IFormFile? back, IFormFile? right, bool straightenBlade, float depthScale,
        float rollDegrees, bool flipGripEnd, CancellationToken ct)
    {
        if (front is null || front.Length == 0) return new(false, "references", "A front/broadside view is required.", null, null);
        var views = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var (role, file) in new[] { ("front", front), ("left", left), ("back", back), ("right", right) })
        {
            if (file is null || file.Length == 0) continue;
            if (file.Length > MaxImageBytes) return new(false, "references", $"{role} view exceeds 16 MB.", null, null);
            var bytes = await ReadFormFile(file, MaxImageBytes);
            if (!LooksLikeImage(bytes)) return new(false, "references", $"{role} view is not PNG/JPEG.", null, null);
            views[role] = bytes;
        }

        var profile = await _pipelineProfiles.LoadAsync();
        if (profile.Provider == "comfyui") _dispatcher.AddRuntimeNode(profile.ComfyNodeName, profile.ComfyBaseUrl);
        Weapon3DGenerationResult generated = profile.Provider == "tripo"
            ? await _tripo.GenerateAsync(views, profile, ct)
            : await _image3d.GenerateGlbAsync(views, ct);
        if (!generated.Ok || generated.Glb is null)
            return new(false, "reconstruction", generated.Message, null, generated.Message);

        var import = _glbImporter.Import(generated.Glb, new GlbImportOptions
        {
            Reorient = true,
            StraightenBlade = straightenBlade,
            FlipGripEnd = flipGripEnd,
            DepthScale = depthScale,
            RollDegrees = rollDegrees,
        });
        if (!import.Ok || import.Mesh is null)
            return new(false, "game-mesh", "The generated GLB failed game-mesh preparation.", import, generated.Message);
        return new(true, "ready", null, import, generated.Message);
    }

    private static async Task<byte[]> ReadFormFile(IFormFile file, long max)
    {
        if (file.Length > max) throw new InvalidDataException($"File exceeds {max:N0} bytes.");
        using var ms = new MemoryStream((int)file.Length);
        await file.CopyToAsync(ms);
        return ms.ToArray();
    }

    private sealed record SketchPipelineRun(bool Ok, string Stage, string? Error,
        GlbImportResult? Import, string? ProviderMessage);

    /// <summary>GET /WeaponForge/Image3DSetup — everything the setup dialog needs: the stored-workflow
    /// status, the configured ComfyUI nodes, and the ready-made copy-paste prompt an AI agent can
    /// follow to install the image→3D pipeline on the server.</summary>
    [HttpGet]
    public async Task<IActionResult> Image3DSetup()
    {
        var nodes = _dispatcher.ConfiguredNodes;
        var wf = await _image3d.GetWorkflowInfoAsync();
        return Json(new
        {
            configured = wf.Present,
            workflow = new { wf.Present, wf.NodeCount, wf.HasLoadImage },
            nodes = nodes.Select(n => new { name = n.Name, url = n.BaseUrl }),
            prompt = ComfyUIWeapon3DGenerator.BuildAgentSetupPrompt(nodes),
        });
    }

    /// <summary>POST /WeaponForge/SaveImage3DWorkflow — store the API-format workflow JSON that the
    /// setup agent handed back. Accepts a pasted text field or an uploaded .json file; validated
    /// (API format, has a LoadImage node) before it replaces anything. Activates Forge sketch.</summary>
    [HttpPost]
    [RequestSizeLimit(4 * 1024 * 1024)]
    public async Task<IActionResult> SaveImage3DWorkflow(string? workflowJson, IFormFile? file)
    {
        string? json = workflowJson;
        if (string.IsNullOrWhiteSpace(json) && file is { Length: > 0 })
        {
            using var reader = new StreamReader(file.OpenReadStream());
            json = await reader.ReadToEndAsync();
        }
        if (string.IsNullOrWhiteSpace(json))
            return BadRequest(new { ok = false, error = "Paste the workflow JSON or choose a .json file." });

        var (ok, message) = await _image3d.SaveWorkflowAsync(json);
        if (ok) _logger.LogInformation("WeaponForge: image→3D workflow uploaded via setup dialog");
        return Json(new { ok, message });
    }

    /// <summary>POST /WeaponForge/ClearImage3DWorkflow — remove the stored workflow (Forge sketch
    /// returns to the not-configured guidance).</summary>
    [HttpPost]
    public async Task<IActionResult> ClearImage3DWorkflow()
    {
        await _image3d.ClearWorkflowAsync();
        return Json(new { ok = true, message = "Workflow removed — sketch → 3D is deactivated." });
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

    private static bool LooksLikeImage(byte[] b)
    {
        if (b.Length < 4) return false;
        bool png = b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47;
        bool jpg = b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF;
        return png || jpg;
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
