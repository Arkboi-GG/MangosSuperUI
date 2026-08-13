using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using MangosSuperUI.Services;

namespace MangosSuperUI.Controllers;

/// <summary>
/// World State — suspend the running world, park it, and resume another in its place.
/// The heavy lifting lives in <see cref="WorldStateService"/>; this is the HTTP surface.
/// </summary>
public class WorldsController : Controller
{
    private readonly WorldStateService _worlds;
    private readonly AuditService _audit;
    private readonly ILogger<WorldsController> _logger;

    public WorldsController(WorldStateService worlds, AuditService audit, ILogger<WorldsController> logger)
    {
        _worlds = worlds;
        _audit = audit;
        _logger = logger;
    }

    // ===================== PAGE =====================

    public IActionResult Index() => View();

    // ===================== STATUS =====================

    /// <summary>GET /Worlds/Status — the full picture: live world, shelf, processes, live counts.</summary>
    [HttpGet]
    public async Task<IActionResult> Status()
    {
        try
        {
            return Json(await _worlds.GetStatusAsync());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read world status");
            return Json(new { error = ex.Message });
        }
    }

    /// <summary>GET /Worlds/Job — poll the in-flight suspend/resume so the UI can narrate it.</summary>
    [HttpGet]
    public IActionResult Job() => Json(new { job = _worlds.CurrentJob });

    // ===================== LIFECYCLE =====================

    /// <summary>POST /Worlds/Suspend — stop the server and freeze the live world.</summary>
    [HttpPost]
    public async Task<IActionResult> Suspend([FromBody] SuspendRequest request)
    {
        try
        {
            var job = await _worlds.SuspendAsync(request?.Label, Ip);

            await _audit.LogAsync(new AuditEntry
            {
                Operator = "admin",
                OperatorIp = Ip,
                Category = "system",
                Action = "world_suspend",
                TargetType = "world",
                TargetName = job.Title,
                IsReversible = true,
                Success = true,
                Notes = "Suspend started" + (string.IsNullOrWhiteSpace(request?.Label) ? "" : $" — {request!.Label}")
            });

            return Json(new { success = true, job });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = ex.Message });
        }
    }

    /// <summary>POST /Worlds/Resume — mount a world, suspending the live one first if there is one.</summary>
    [HttpPost]
    public async Task<IActionResult> Resume([FromBody] ResumeRequest request)
    {
        if (string.IsNullOrEmpty(request?.WorldId))
            return Json(new { success = false, error = "Missing worldId" });

        try
        {
            var job = await _worlds.ResumeAsync(request.WorldId, request.Snapshot, Ip);

            await _audit.LogAsync(new AuditEntry
            {
                Operator = "admin",
                OperatorIp = Ip,
                Category = "system",
                Action = job.Kind == "swap" ? "world_swap" : "world_resume",
                TargetType = "world",
                TargetName = job.Title,
                IsReversible = true,
                Success = true,
                Notes = $"{job.Title} started"
            });

            return Json(new { success = true, job });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// POST /Worlds/RestoreGroup — advanced: put back a single group from one snapshot
    /// without a full world swap. Requires the server to already be unloaded.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> RestoreGroup([FromBody] RestoreGroupRequest request)
    {
        if (string.IsNullOrEmpty(request?.WorldId) || string.IsNullOrEmpty(request.Folder) || string.IsNullOrEmpty(request.Group))
            return Json(new { success = false, error = "Missing worldId, folder or group" });

        try
        {
            var job = await _worlds.RestoreSingleGroupAsync(request.WorldId, request.Folder, request.Group);

            await _audit.LogAsync(new AuditEntry
            {
                Operator = "admin",
                OperatorIp = Ip,
                Category = "system",
                Action = "world_restore_group",
                TargetType = "world",
                TargetName = request.Folder,
                IsReversible = false,
                Success = true,
                Notes = $"Restored group '{request.Group}' from {request.Folder}"
            });

            return Json(new { success = true, job });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = ex.Message });
        }
    }

    // ===================== WORLD CRUD =====================

    /// <summary>POST /Worlds/Fork — branch a new world off an existing snapshot.</summary>
    [HttpPost]
    public async Task<IActionResult> Fork([FromBody] ForkRequest request)
    {
        if (string.IsNullOrEmpty(request?.WorldId))
            return Json(new { success = false, error = "Missing worldId" });

        try
        {
            var fork = await _worlds.ForkAsync(request.WorldId, request.Snapshot, request.Name ?? "", request.Flavor ?? "", request.Notes);

            await _audit.LogAsync(new AuditEntry
            {
                Operator = "admin",
                OperatorIp = Ip,
                Category = "system",
                Action = "world_fork",
                TargetType = "world",
                TargetName = fork.Name,
                StateAfter = JsonSerializer.Serialize(new { fork.Id, fork.Name, fork.ParentId, fork.ForkedFromFolder }),
                IsReversible = true,
                Success = true,
                Notes = $"Forked '{fork.Name}' from snapshot {fork.ForkedFromFolder}"
            });

            return Json(new { success = true, world = fork });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = ex.Message });
        }
    }

    /// <summary>POST /Worlds/Update — rename / re-flavour / re-note a world.</summary>
    [HttpPost]
    public async Task<IActionResult> Update([FromBody] UpdateRequest request)
    {
        if (string.IsNullOrEmpty(request?.WorldId))
            return Json(new { success = false, error = "Missing worldId" });

        try
        {
            var world = await _worlds.UpdateAsync(request.WorldId, request.Name, request.Flavor, request.Notes);
            return Json(new { success = true, world });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = ex.Message });
        }
    }

    /// <summary>POST /Worlds/SnapshotLabel — retitle a single snapshot.</summary>
    [HttpPost]
    public async Task<IActionResult> SnapshotLabel([FromBody] SnapshotLabelRequest request)
    {
        if (string.IsNullOrEmpty(request?.WorldId) || string.IsNullOrEmpty(request.Folder))
            return Json(new { success = false, error = "Missing worldId or folder" });

        try
        {
            var ok = await _worlds.UpdateSnapshotLabelAsync(request.WorldId, request.Folder, request.Label ?? "");
            return Json(new { success = ok, error = ok ? null : "Snapshot not found" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = ex.Message });
        }
    }

    /// <summary>POST /Worlds/DeleteWorld — drop a world and any snapshots nobody else shares.</summary>
    [HttpPost]
    public async Task<IActionResult> DeleteWorld([FromBody] DeleteWorldRequest request)
    {
        if (string.IsNullOrEmpty(request?.WorldId))
            return Json(new { success = false, error = "Missing worldId" });

        try
        {
            var result = await _worlds.DeleteWorldAsync(request.WorldId);

            await _audit.LogAsync(new AuditEntry
            {
                Operator = "admin",
                OperatorIp = Ip,
                Category = "system",
                Action = "world_delete",
                TargetType = "world",
                TargetName = request.WorldId,
                StateAfter = JsonSerializer.Serialize(result),
                IsReversible = false,
                Success = true,
                Notes = "Deleted world and its unshared snapshots"
            });

            return Json(new { success = true, result });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = ex.Message });
        }
    }

    /// <summary>POST /Worlds/DeleteSnapshot — drop one snapshot from a world's history.</summary>
    [HttpPost]
    public async Task<IActionResult> DeleteSnapshot([FromBody] DeleteSnapshotRequest request)
    {
        if (string.IsNullOrEmpty(request?.WorldId) || string.IsNullOrEmpty(request.Folder))
            return Json(new { success = false, error = "Missing worldId or folder" });

        try
        {
            var removedFromDisk = await _worlds.DeleteSnapshotAsync(request.WorldId, request.Folder);

            await _audit.LogAsync(new AuditEntry
            {
                Operator = "admin",
                OperatorIp = Ip,
                Category = "system",
                Action = "world_delete_snapshot",
                TargetType = "world_snapshot",
                TargetName = request.Folder,
                IsReversible = false,
                Success = true,
                Notes = removedFromDisk
                    ? $"Deleted snapshot {request.Folder}"
                    : $"Unlinked snapshot {request.Folder} (still shared with a fork)"
            });

            return Json(new { success = true, removedFromDisk });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = ex.Message });
        }
    }

    // ===================== HELPERS =====================

    private string? Ip => HttpContext.Connection.RemoteIpAddress?.ToString();

    // ===================== REQUEST DTOs =====================

    public class SuspendRequest
    {
        public string? Label { get; set; }
    }

    public class ResumeRequest
    {
        public string? WorldId { get; set; }
        public string? Snapshot { get; set; }
    }

    public class RestoreGroupRequest
    {
        public string? WorldId { get; set; }
        public string? Folder { get; set; }
        public string? Group { get; set; }
    }

    public class ForkRequest
    {
        public string? WorldId { get; set; }
        public string? Snapshot { get; set; }
        public string? Name { get; set; }
        public string? Flavor { get; set; }
        public string? Notes { get; set; }
    }

    public class UpdateRequest
    {
        public string? WorldId { get; set; }
        public string? Name { get; set; }
        public string? Flavor { get; set; }
        public string? Notes { get; set; }
    }

    public class SnapshotLabelRequest
    {
        public string? WorldId { get; set; }
        public string? Folder { get; set; }
        public string? Label { get; set; }
    }

    public class DeleteWorldRequest
    {
        public string? WorldId { get; set; }
    }

    public class DeleteSnapshotRequest
    {
        public string? WorldId { get; set; }
        public string? Folder { get; set; }
    }
}
