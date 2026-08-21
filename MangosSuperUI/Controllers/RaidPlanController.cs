using Microsoft.AspNetCore.Mvc;
using MangosSuperUI.Services;

namespace MangosSuperUI.Controllers;

// ============================================================================
// RaidPlanController — [RAID-PLAN] the assignment surface (PLAN_19 M-B).
//
// curl-first, mirroring RotationController:
//   GET  /api/raidplans                                → plans + assignments
//   POST /api/raidplans/assign?bot=Grimjaw&plan=Onyxia raid plan
//   POST /api/raidplans/assign?bot=*&plan=...          → every online bot
//   POST /api/raidplans/clear?bot=Grimjaw
//
// Plan documents are the files the MSUIClient Encounter Lab exports — drop them
// in RaidPlans/, assign, done. Same LAN-prototype authority caveat as the
// rotation surface (PLAN_19 §5): production assignment belongs to SuperUI-Core
// behind authenticated sessions; this is the editor/browser loop.
// ============================================================================
[ApiController]
[Route("api/raidplans")]
public class RaidPlanController : ControllerBase
{
    private readonly RaidPlanService _plans;

    public RaidPlanController(RaidPlanService plans)
    {
        _plans = plans;
    }

    [HttpGet]
    public IActionResult List()
    {
        var plans = _plans.LoadPlans()
            .Select(p => new
            {
                p.Name,
                p.EncounterKey,
                bodies = p.Bodies.Count,
                assignments = p.Doctrine.Assignments?.Count ?? 0,
                auras = p.Doctrine.MaintainAuras?.Count ?? 0,
                addControl = p.Doctrine.AddControl?.Count ?? 0,
                p.Doctrine.BossThreatLite,
            })
            .ToList();
        return Ok(new { plans, assignments = _plans.Assignments });
    }

    [HttpPost("assign")]
    public async Task<IActionResult> Assign([FromQuery] string bot, [FromQuery] string plan)
    {
        if (string.IsNullOrWhiteSpace(bot) || string.IsNullOrWhiteSpace(plan))
            return BadRequest(new { status = "bot and plan are required" });
        return Ok(new { status = await _plans.AssignAsync(bot, plan) });
    }

    [HttpPost("clear")]
    public async Task<IActionResult> Clear([FromQuery] string bot)
    {
        if (string.IsNullOrWhiteSpace(bot))
            return BadRequest(new { status = "bot is required" });
        return Ok(new { status = await _plans.ClearAsync(bot) });
    }
}
