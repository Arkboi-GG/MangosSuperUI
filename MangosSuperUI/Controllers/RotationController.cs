using Microsoft.AspNetCore.Mvc;
using MangosSuperUI.Services;

namespace MangosSuperUI.Controllers;

// ============================================================================
// RotationController — [ROTATION] the assignment surface (2026-07-16).
//
// curl-first by design (querystring params, plain-text friendly JSON status):
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

    public RotationController(RotationService rotations)
    {
        _rotations = rotations;
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
        return Ok(new { status = await _rotations.AssignAsync(bot, profile) });
    }

    [HttpPost("clear")]
    public async Task<IActionResult> Clear([FromQuery] string bot)
    {
        if (string.IsNullOrWhiteSpace(bot))
            return BadRequest(new { status = "bot is required" });
        return Ok(new { status = await _rotations.ClearAsync(bot) });
    }
}
