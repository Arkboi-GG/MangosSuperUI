using Microsoft.AspNetCore.Mvc;

namespace MangosSuperUI.Controllers;

// ============================================================================
//  Bot Map Viewer — the spatial twin of Fleet View
// ============================================================================
// Fleet View answers "what is failing and how often." This answers "WHERE."
// It plots the same correlated incidents (from /Bots/FleetDiagnostics — every
// fault already carries x/y/map) as dots on the WorldMap minimap tiles, plus a
// live "where is every bot right now" layer driven by the brain's GetLiveFleet().
//
// Pull-only, front-end heavy: the page reuses /WorldMap/TileIndex (tiles),
// /Bots/FleetDiagnostics (fault dots + hotspots), /Bots/States (class colours),
// and the one new endpoint below (live positions). No spine/wire change.
//
// Self-contained against confirmed accessors only: _brain.GetLiveFleet()
// (public IReadOnlyList<object>, the same projection the Live tab renders).
public partial class BotsController
{
    // Renders Views/Bots/Map.cshtml. Page polls MapBots + FleetDiagnostics + States.
    public IActionResult Map() => View();

    /// <summary>
    /// GET /Bots/MapBots
    /// Live per-bot spine projection for the "current position" layer: pos{x,y,z},
    /// mapId, goal/why, hp/mana, dead/inCombat/stall, current target, last failure.
    /// classId is NOT in BotContext — the client joins it from /Bots/States by guid
    /// (same pattern Fleet View uses). Brain off / no bots => valid empty payload.
    /// </summary>
    [HttpGet]
    public IActionResult MapBots()
    {
        var bots = _brain.GetLiveFleet();
        return Json(new
        {
            ok = true,
            generatedUtc = DateTime.UtcNow,
            count = bots.Count,
            bots
        });
    }
}
