using MangosSuperUI.BotLogic.Core;

namespace MangosSuperUI.BotLogic.Planners;

// ============================================================================
// TeleportAssist — the shared teleport-assist primitive (final-NPC-approach).
//
// When a final-approach MOVE_TO to a SERVICE NPC (trainer / vendor / repair) keeps
// returning no_path while the bot is already in the vicinity, the NPC sits in a
// nav-dead pocket the pathfinder can't reach the last few yards into (building
// interior, bad mesh stitch at the door). MOVE_TO is exactly what's refused, so the
// escape is a short TELEPORT_TO the NPC: hop in, do the business at real proximity,
// hop BACK to where the bot came from. This is the deliberate, accepted shortcut.
//
// One generic C++ primitive (BridgeHandleTeleport: NearTeleportTo + TELEPORT_ACK
// x|y|z|map, max_dist-capped) backs both this and the future hearth.
//
// Decomposition (so the two callers — TrainingPlanner, MaintenancePlanner — share
// ALL the round-trip mechanics and only own their own interaction):
//   • The approach no_path COUNT lives on the caller's scratch (TrainScratch.ApproachFails
//     / ServiceScratch.RouteFails), reset for free each trip by the fresh scratch.
//   • Decide(count, …) → Retry (re-issue the approach) | Teleport (commit) | GiveUp (too far / cross-map).
//   • BeginOutbound(ctx, target) captures the anchor (= the bot's CURRENT pos — it pathed
//     there, so it's a reachable on-mesh return point) and builds the outbound TELEPORT_TO.
//   • ctx.Teleport (TeleportTrip) holds the committed round-trip; the GoalSelector pins the
//     goal while it's non-null; the executor updates ctx.Pos from TELEPORT_ACK so the planner
//     sees DistToTarget≈0 and fires the interaction the same cycle (no 5 s STATE lag).
//   • BeginReturn(ctx) flips to Inbound and builds the return TELEPORT_TO (to the anchor).
//
// The caller weaves its own interaction (TRAIN_AT_NPC / SELL_ITEMS / REPAIR_AT_NPC) between
// Outbound and the return; this helper never knows what the business is.
// ============================================================================
public static class TeleportAssist
{
    // The hop radius. ALSO the C++ max_dist safety rail (BridgeHandleTeleport refuses a hop
    // beyond this), so a bad C# coord can't fling a live bot across the zone. 50 yd is already
    // a large hop and covers the building-interior / door-stitch case 90%+ of the time; a
    // genuine far failure (> 50 yd no_path) is NOT a final-approach pocket and falls to give-up.
    public const float ReachYards = 50f;

    // Teleport only after this many consecutive no_paths on the approach leg (each of which
    // already had C++ exhaust its own nudge+ring retries first). The first no_path just re-issues
    // the approach (one more chance for continuation travel to close the gap); the second, in
    // reach, teleports.
    public const int AfterNoPaths = 2;

    // TELEPORT_TO → TELEPORT_ACK is near-instant (same-map NearTeleportTo); the deadline is only
    // the lost-ack backstop. On a miss the brain surfaces ctx.Failure(deadline) and the planner
    // abandons the hop (Outbound) or completes anyway (Inbound) — bounded, never a wedge.
    public static readonly TimeSpan AckDeadline = TimeSpan.FromSeconds(10);

    public enum TpDecision { Retry, Teleport, GiveUp }

    /// <summary>A failure that the teleport-assist handles: a no_path / empty_path on a MOVE_TO leg.
    /// path_unsafe (a danger band — defer/level-gate, the hearth's job) and deadline (broad travel
    /// stall) are deliberately excluded.</summary>
    public static bool IsApproachNoPath(WaitFailure f)
        => f.CommandType == "MOVE_TO" && (f.Reason == "no_path" || f.Reason == "empty_path");

    /// <summary>Given the running no_path count for THIS approach leg and where the bot now stands,
    /// decide whether to retry the approach, commit the teleport, or give up (genuinely too far —
    /// not a final-approach pocket). Pure: increments nothing, mutates nothing.</summary>
    public static TpDecision Decide(int noPathCount, Vec3 botPos, Vec4 target, int botMap)
    {
        if (noPathCount < AfterNoPaths)
            return TpDecision.Retry;
        if (botMap == target.Map && botPos.Dist2D(target.Pos) <= ReachYards)
            return TpDecision.Teleport;
        return TpDecision.GiveUp;   // exhausted retries but the NPC is far / cross-map — a real travel failure
    }

    /// <summary>Commit the outbound hop: capture the anchor (the bot's current, reachable pos) and
    /// the NPC target on ctx.Teleport (Phase = Outbound), and return the TELEPORT_TO command. The
    /// caller issues it with a WAIT on TELEPORT_ACK.</summary>
    public static BridgeCommand BeginOutbound(BotContext ctx, Vec4 target)
    {
        ctx.Teleport = new TeleportTrip
        {
            Anchor = new Vec4(ctx.Pos.X, ctx.Pos.Y, ctx.Pos.Z, ctx.MapId),
            Target = target,
            Phase = TpPhase.Outbound
        };
        return new BridgeCommand("TELEPORT_TO",
            new { mapId = target.Map, x = target.X, y = target.Y, z = target.Z, max_dist = ReachYards });
    }

    /// <summary>Flip the committed trip to Inbound and build the return TELEPORT_TO (back to the
    /// captured anchor). The anchor is a coord the bot was literally standing on, so the same
    /// max_dist rail can't spuriously refuse it. The caller sets ctx.Teleport.Failed first if the
    /// business failed at the NPC (so the Inbound completion gives up instead of finishing).</summary>
    public static BridgeCommand BeginReturn(BotContext ctx)
    {
        var tp = ctx.Teleport!;
        tp.Phase = TpPhase.Inbound;
        var a = tp.Anchor;
        return new BridgeCommand("TELEPORT_TO",
            new { mapId = a.Map, x = a.X, y = a.Y, z = a.Z, max_dist = ReachYards });
    }
}
