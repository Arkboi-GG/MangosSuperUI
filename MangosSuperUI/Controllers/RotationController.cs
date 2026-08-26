using Microsoft.AspNetCore.Mvc;
using MangosSuperUI.Services;

namespace MangosSuperUI.Controllers;

// ============================================================================
// RotationController — [ROTATION] the assignment surface (2026-07-16).
//
// Legacy curl-compatible surface. Writes are routed through the same atomic
// combat-loadout coordinator as the Build Workshop; these endpoints may no
// longer push LOAD_ROTATION around the queue/revision fences.
//   GET  /api/rotations                                  → profiles + assignments
//   POST /api/rotations/assign?bot=Grimjaw&profile=priest_smite_v1
//   POST /api/rotations/clear?bot=Grimjaw
//
// Profiles live as JSON files in Rotations/ and are read fresh per call — edit
// the file, POST assign again, and the new slate is live (no restart). A proper
// Rotation Editor panel on the Bots page rides on these same endpoints later.
// ============================================================================
[ApiController]
[Route("api/rotations")]
public class RotationController : ControllerBase
{
    private readonly RotationService _rotations;
    private readonly BotBridgeService _bridge;
    private readonly BotCombatLoadoutService _loadouts;
    private readonly BotCombatLoadoutQueueService _queue;

    public RotationController(
        RotationService rotations,
        BotBridgeService bridge,
        BotCombatLoadoutService loadouts,
        BotCombatLoadoutQueueService queue)
    {
        _rotations = rotations;
        _bridge = bridge;
        _loadouts = loadouts;
        _queue = queue;
    }

    [HttpGet]
    public IActionResult List()
    {
        var profiles = _rotations.LoadProfiles()
            .Select(p => new { p.Name, p.Description, instructionCount = p.Instructions.Count })
            .ToList();
        return Ok(new { profiles, assignments = _rotations.Assignments });
    }

    [HttpPost("assign")]
    public async Task<IActionResult> Assign([FromQuery] string bot, [FromQuery] string profile)
    {
        if (string.IsNullOrWhiteSpace(bot) || string.IsNullOrWhiteSpace(profile))
            return BadRequest(new { status = "bot and profile are required" });
        return await ChangeRotationAsync(bot, "custom", profile);
    }

    [HttpPost("clear")]
    public async Task<IActionResult> Clear([FromQuery] string bot)
    {
        if (string.IsNullOrWhiteSpace(bot))
            return BadRequest(new { status = "bot is required" });
        return await ChangeRotationAsync(bot, "spec_default", null);
    }

    private async Task<IActionResult> ChangeRotationAsync(
        string botName,
        string mode,
        string? profile)
    {
        BotConnection? connection = _bridge.Connections.Values.FirstOrDefault(c =>
            string.Equals(c.State.Name, botName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (connection == null)
        {
            return Conflict(new
            {
                errorCode = "bot_offline",
                error = "Rotation changes now require an online bot and use the guarded combat-loadout queue."
            });
        }

        try
        {
            int guid = connection.State.Guid;
            BotCombatLoadoutView current = await _loadouts.GetAsync(guid, HttpContext.RequestAborted);
            BotCombatLoadoutQueueView? pending = await _queue.GetAsync(guid, HttpContext.RequestAborted);
            var request = new BotCombatLoadoutRequest
            {
                ExpectedQueueId = pending?.QueueId,
                ExpectedRevision = current.CombatConfigRevision,
                SpecTab = current.SpecTab,
                ActiveRole = current.ActiveRoleId,
                RotationMode = mode,
                RotationProfile = profile,
                ResetTalents = false,
                ConfirmReset = false
            };

            if (current.CanApply && pending == null)
            {
                try
                {
                    BotCombatLoadoutApplyResult applied = await _queue.ApplyDirectAsync(
                        guid,
                        request,
                        HttpContext.RequestAborted,
                        User.Identity?.Name ?? "legacy_rotation_api",
                        HttpContext.Connection.RemoteIpAddress?.ToString());
                    return Ok(new { status = applied.Message, result = applied });
                }
                catch (BotCombatLoadoutException ex)
                    when (BotCombatLoadoutQueueService.CanQueueAfterDirectRejection(ex.Code))
                {
                    // A verified pre-mutation safety rejection is queueable. Fall
                    // through to the same durable path used when GET already knew
                    // that the bot was busy.
                }
            }

            BotCombatLoadoutQueueMutationResult queued = await _queue.EnqueueAsync(
                guid,
                request,
                User.Identity?.Name ?? "legacy_rotation_api",
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                HttpContext.RequestAborted);
            return Accepted(new { status = queued.Message, queue = queued.Queue });
        }
        catch (BotCombatLoadoutException ex)
        {
            return StatusCode(ex.StatusCode, new { errorCode = ex.Code, error = ex.Message });
        }
        catch (BotCombatLoadoutQueueException ex)
        {
            return StatusCode(ex.StatusCode, new { errorCode = ex.Code, error = ex.Message });
        }
    }
}
