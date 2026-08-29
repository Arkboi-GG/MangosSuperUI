// UnifiedPatchController.cs
//
// The one patch, end to end. Retextures, forged weapons and forged armor all write
// ItemDisplayInfo.dbc, and MPQ resolves whole files by rank — so three archives each carrying their
// own copy meant only the topmost was ever read, every lane had to re-union the ones below it, and
// changing anything forced a rebuild AND a re-download of the highest patch. This builds all three
// lanes into a single patch-4.MPQ: one rebuild, one download, no cascade.
//
// Endpoints:
//   POST     /UnifiedPatch/Rebuild[?deploy=false]  -> build (and by default deploy + retire 5/6)
//   GET      /UnifiedPatch/Status                  -> what is built and what is still shadowing
//   GET|HEAD /UnifiedPatch/DownloadPatch           -> the archive itself
//
// Rebuild DEPLOYS by default, which also deletes patch-5.MPQ and patch-6.MPQ out of the live
// client — they outrank patch-4 and would otherwise keep serving their own stale DBC. Pass
// deploy=false to build the artifact and inspect it without touching the client.

using Microsoft.AspNetCore.Mvc;
using MangosSuperUI.Services.UnifiedPatch;

namespace MangosSuperUI.Controllers;

public class UnifiedPatchController : Controller
{
    private readonly UnifiedPatchService _unified;
    private readonly IConfiguration _config;
    private readonly ILogger<UnifiedPatchController> _logger;

    public UnifiedPatchController(UnifiedPatchService unified, IConfiguration config,
        ILogger<UnifiedPatchController> logger)
    {
        _unified = unified; _config = config; _logger = logger;
    }

    /// <summary>POST /UnifiedPatch/Rebuild — repackage every lane from the database into the single
    /// patch. <paramref name="deploy"/> false builds the artifact only and leaves the client alone,
    /// which is how you inspect the output before switching over.</summary>
    [HttpPost]
    public async Task<IActionResult> Rebuild(bool deploy = true, string? reason = null)
    {
        try
        {
            var summary = await _unified.RebuildAsync(reason ?? "manual rebuild from UI", deploy);
            return Json(summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UnifiedPatch: rebuild failed");
            return Json(new { ok = false, message = "rebuild failed: " + ex.Message });
        }
    }

    /// <summary>GET /UnifiedPatch/Status — whether the artifact exists, whether it is deployed, and
    /// critically whether either superseded archive is STILL in the client. A leftover patch-6
    /// outranks patch-4 and silently makes the unified patch inert, so it is worth naming out loud
    /// rather than leaving someone to wonder why a rebuild changed nothing.</summary>
    [HttpGet]
    public IActionResult Status()
    {
        string? dataPath = _config["Vmangos:ClientDataPath"] ?? _config["SpellCreator:ClientDataPath"];
        bool haveClient = !string.IsNullOrEmpty(dataPath) && Directory.Exists(dataPath);

        var shadowing = new List<string>();
        if (haveClient)
            foreach (var name in UnifiedPatchService.SupersededPatchFileNames)
                if (System.IO.File.Exists(Path.Combine(dataPath!, name)))
                    shadowing.Add(name);

        string artifact = _unified.ArtifactPath;
        bool built = System.IO.File.Exists(artifact);
        bool deployed = haveClient &&
            System.IO.File.Exists(Path.Combine(dataPath!, UnifiedPatchService.PatchFileName));

        return Json(new
        {
            patch = UnifiedPatchService.PatchFileName,
            built,
            builtBytes = built ? new FileInfo(artifact).Length : 0,
            deployed,
            clientDataPath = haveClient ? dataPath : null,
            shadowing,
            shadowingWarning = shadowing.Count > 0
                ? $"{string.Join(" and ", shadowing)} still in the client Data folder and outrank " +
                  $"{UnifiedPatchService.PatchFileName} — it will have NO effect in game until they are removed. " +
                  "Run Rebuild with deploy=true, or delete them by hand."
                : null,
        });
    }

    /// <summary>GET|HEAD /UnifiedPatch/DownloadPatch — hand over the archive. wwwroot is ephemeral
    /// (wiped on publish/restart), so a GET rebuilds from the DB when the file is missing; a HEAD
    /// probe does not, because a probe should never have a side effect. The rebuild triggered here
    /// is build-only: downloading a patch must not silently rewrite the live client.</summary>
    [HttpGet]
    [HttpHead]
    public async Task<IActionResult> DownloadPatch()
    {
        string path = _unified.ArtifactPath;

        if (!System.IO.File.Exists(path) && HttpMethods.IsGet(Request.Method))
        {
            var summary = await _unified.RebuildAsync("download requested, artifact missing", deploy: false);
            if (!summary.Ok && summary.TotalRows == 0)
                return NotFound("Nothing forged yet — no patch to download.");
        }

        if (!System.IO.File.Exists(path))
            return NotFound($"{UnifiedPatchService.PatchFileName} has not been built.");

        if (HttpMethods.IsHead(Request.Method))
        {
            Response.ContentLength = new FileInfo(path).Length;
            Response.ContentType = "application/octet-stream";
            return new EmptyResult();
        }

        return PhysicalFile(path, "application/octet-stream", UnifiedPatchService.PatchFileName);
    }
}
