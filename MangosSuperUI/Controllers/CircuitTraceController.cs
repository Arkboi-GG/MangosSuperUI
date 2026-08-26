using Microsoft.AspNetCore.Mvc;
using MangosSuperUI.BotLogic.Tracking;
using MangosSuperUI.Services;

namespace MangosSuperUI.Controllers;

// ════════════════════════════════════════════════════════════════════════════
// CircuitTraceController — the ONE trace query surface (CIRCUIT_BOARD.md R10).
//
// Both consumers — the SuperUI visual layers (logic view + world-map layer) and
// the LLM context-pack — read THESE endpoints and nothing else, so the two
// views can never drift. Decoding is client-side: /Sites is the id→(file,line,
// description) registry, /Peek returns raw segments carrying site ids, world
// position, and value/note payloads.
//
//   GET  /CircuitTrace/Status          mode, armed guids, site count, ring count
//   GET  /CircuitTrace/Sites           the session site manifest (the decoder ring)
//   GET  /CircuitTrace/Peek/{guid}     recent sealed segments for one bot (ring copy, non-destructive)
//   POST /CircuitTrace/Arm/{guid}      arm one bot (persists; flushes continuously)
//   POST /CircuitTrace/Disarm/{guid}   disarm (flushes the tail first; persists)
//   POST /CircuitTrace/Mode?mode=off|shadow   global recording mode (persists)
//   POST /CircuitTrace/Dump/{guid}     manual ring dump to the daily JSONL (like the wedge auto-dump)
// ════════════════════════════════════════════════════════════════════════════
public class CircuitTraceController : Controller
{
    private readonly BotBrainService _brain;

    public CircuitTraceController(BotBrainService brain)
    {
        _brain = brain;
    }

    /// <summary>The Circuit Board viewer page (Bot Development → Circuit Board).</summary>
    public IActionResult Index() => View();

    [HttpGet]
    public IActionResult Status() => Json(_brain.Circuit.Status());

    [HttpGet]
    public IActionResult Sites() =>
        Json(CircuitTrace.Sites.Select(s => new { id = s.Id, file = s.File, line = s.Line, desc = s.Description }));

    /// <summary>Recent sealed segments for one bot, oldest first. Non-destructive ring copy —
    /// safe to poll. maxSegments caps the window (default 256 ≈ ~1 min of ticks).</summary>
    [HttpGet]
    public IActionResult Peek(int guid, int maxSegments = 256)
    {
        var segs = CircuitTrace.PeekSegments(guid, maxSegments);
        return Json(new
        {
            guid,
            mode = CircuitTrace.Mode.ToString().ToLowerInvariant(),
            armed = CircuitTrace.IsArmed(guid),
            segments = segs.Select(s => new
            {
                k = s.Kind,
                t0 = s.StartUtc,
                t1 = s.EndUtc,
                pos = s.HasPos ? new { map = s.MapId, zone = s.ZoneId, x = s.X, y = s.Y, z = s.Z } : null,
                h = s.Hits.Select(h =>
                    h.Note != null ? new object?[] { h.SiteId, h.Value, h.Note }
                    : h.Value != null ? new object?[] { h.SiteId, h.Value }
                    : new object?[] { h.SiteId })
            })
        });
    }

    [HttpPost]
    public async Task<IActionResult> Arm(int guid)
    {
        await _brain.Circuit.ArmAsync(guid);
        return Json(new { ok = true, armed = CircuitTrace.ArmedGuids() });
    }

    [HttpPost]
    public async Task<IActionResult> Disarm(int guid)
    {
        await _brain.Circuit.DisarmAsync(guid);
        return Json(new { ok = true, armed = CircuitTrace.ArmedGuids() });
    }

    [HttpPost]
    public async Task<IActionResult> Mode(string mode)
    {
        var parsed = string.Equals(mode, "shadow", StringComparison.OrdinalIgnoreCase)
            ? CircuitTrace.TraceMode.Shadow
            : CircuitTrace.TraceMode.Off;
        await _brain.Circuit.SetModeAsync(parsed);
        return Json(new { ok = true, mode = parsed.ToString().ToLowerInvariant() });
    }

    /// <summary>Manual dump: flush this bot's whole ring to the daily JSONL now (the same
    /// path the wedge auto-dump uses). Works for any bot while mode is shadow.</summary>
    [HttpPost]
    public IActionResult Dump(int guid)
    {
        CircuitTrace.RequestDump(guid, "manual");
        return Json(new { ok = true, note = "queued; the brain loop flushes it within ~250ms" });
    }
}
