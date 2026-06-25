using Microsoft.AspNetCore.Mvc;

namespace MangosSuperUI.Controllers;

// ============================================================================
//  Fusion — full-bleed, sidebar-less bot command surface
// ============================================================================
// Designed to be embedded in SuperUIFusion's WebView2 with the live WoW client
// floating in the center "hole". This is the third bots surface alongside the
// Individual Bot Monitor (Index) and the Fleet board (Fleet); it folds the most
// valuable real-time telemetry from both into a single screen that wraps the
// client.
//
// Pull-only: every datum comes from the REST endpoints already defined on this
// controller (States, BrainStatus, LiveState, LiveLog, BotReport, QuestStatus,
// Inventory, LiveFleet, FleetDiagnostics) and the group endpoints. The Map is
// reused as-is via an <iframe src="/Bots/Map"> inside a modal — no new map code.
//
// View sets Layout = null and is fully self-contained so it renders identically
// whether viewed in the shell or a plain browser tab at /Bots/Fusion.
public partial class BotsController
{
    public IActionResult Fusion() => View();
}
