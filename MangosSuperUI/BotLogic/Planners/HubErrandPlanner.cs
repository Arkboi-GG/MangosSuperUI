using System.Collections.Concurrent;
using MangosSuperUI.BotLogic.Core;
using MangosSuperUI.BotLogic.Data;

namespace MangosSuperUI.BotLogic.Planners;

// ============================================================================
// HubErrandPlanner — Goal.Vendoring (the "do your rounds" hub errand, 2026-07-08 §3).
//
// A REAL player's companion bot, standing at a hub, is told "do your rounds" in
// party chat. The BotBridgeService CHAT_RECV recognizer stamps a bounded run
// token (conn.State.HubErrandUntil, ~4 min); the GoalSelector routes the bot
// here — under Goal.Vendoring, verified unclaimed by any other planner — while
// the token is LIVE and != ctx.HubErrandDone. On the C++ side the escort hook
// YIELDS to the task machinery for TASK_MOVE_TO while in-party, so the legs this
// planner issues actually walk; between legs the bot briefly re-follows the boss
// (the accepted 1-2 tick cosmetic).
//
// The round, in order, every target within HubRadiusYards of the ANCHOR (where
// the bot stood when the run began — the hub), same map only:
//   (0) leading VENDOR visit when bags are tight (FreeSlots <= 1) — turn-in
//       rewards need bag room, so sell FIRST when there is none;
//   (1) TURN-INS — every held quest at server-COMPLETE (or with no objectives)
//       whose ender is in reach. First because it frees quest-log slots for (2);
//   (2) ACCEPTS  — every giver-in-reach quest passing QuestPlanner.IsPickable
//       (the full shared gate: race/class/level, red/grey, deferrals, blacklists,
//       rails — the same filter the solo spine trusts, so a post-party bot's log
//       never holds something the solo planner can't cope with);
//   (3) VENDOR   — always (SELL_ITEMS → REPAIR_AT_NPC when the NPC can);
//   (4) TRAINER  — always (TRAIN_AT_NPC buys whatever the bot can afford; the
//       stale-flag problem makes "only when HasUnlearnedSpells" unreliable, and
//       an empty visit costs one short walk).
//
// One route/arrive/business machine drives all target kinds; the WMO-interior
// last-yards problem rides the SAME TeleportAssist ladder Training/Maintenance
// proved (retry → hop in → do the business → hop back — never strand the bot in
// the pocket it teleported into). Per-target failures SKIP that target and note
// it; only the guards end the run early:
//   • boss guard — snap.PartyBossDist > BossAbortYards or off-map sentinel →
//     drop the rounds, ack "coming!", resume follow (the follow catch-up /
//     instance-follow teleports do the rest);
//   • death — the GoalSelector's dead branch consumes the token (post-rez the
//     bot follows, it does not resume a half round);
//   • "lets move" / the 4-min window — the stamp goes null/expired, the
//     GoalSelector reverts to the Idle hold, and the goal change SET_TASK IDLEs
//     C++ back into formation. The planner just drops its scratch.
//
// The run token IS the once-only latch: on finish/abort this planner stamps
// ctx.HubErrandDone = the stamp; a re-issued "do your rounds" carries a NEW
// timestamp and therefore a fresh run. Scratch is planner-owned per guid (NOT a
// BotContext scratch slot — the brain's goal-change reset doesn't know it, and
// the Stamp mismatch check self-heals staleness anyway), so the only BotContext
// addition this feature needs is the HubErrandDone property.
//
// Party-chat narration: one line when the round is planned, one when it ends
// (tallies + skips), one on a boss-guard abort — SAY_TEXT chatType=1 (the C++
// party branch is live). Fire-and-forget; personas may ad-lib on top.
// ============================================================================
public sealed class HubErrandPlanner : IBotPlanner
{
    private readonly QuestGraphLoader _quests;
    private readonly ZoneDataLoader _zoneData;
    private readonly SpellProgressionLoader _trainers;
    private readonly ILogger<HubErrandPlanner> _log;

    // Every target must sit within this of the ANCHOR (the spot the command was given at).
    // "Do your rounds" means the LOCAL hub — a cross-town march is not a round. Tune here.
    private const float HubRadiusYards = 100f;

    // Boss guard: he's leaving (same-map distance past this, or the off-map sentinel) →
    // abort to follow. ppdist is up to one 5s STATE stale — fine for a gate this wide.
    private const int BossAbortYards = 150;
    private const int BossOffMapSentinel = 99999;   // C++ sends this when the boss is on ANOTHER map
    private const float NpcReachYards = 10f;    // close enough to interact without a fresh MOVE_TO (C++ searches 15yd) — mirrors QuestPlanner
    private const int MaxLegsPerTarget = 5;     // a <=100yd target should take ONE leg; this caps a jittering approach
    private const int BagsTightFreeSlots = 1;   // at/below this, prepend a vendor visit so turn-in rewards have room
    private const int RepairRequiredBelowDurability = 70;   // mirrors MaintenancePlanner — below this, hard-prefer an armorer

    private static readonly TimeSpan LegDeadline = TimeSpan.FromSeconds(120);      // per-MOVE_TO ceiling (== MaintenancePlanner's vendor leg)
    private static readonly TimeSpan InteractDeadline = TimeSpan.FromSeconds(20);  // QUEST_INTERACT acks are near-instant (== QuestPlanner)
    private static readonly TimeSpan AckDeadline = TimeSpan.FromSeconds(30);       // SELL/REPAIR/TRAIN ack window (== MaintenancePlanner)

    private enum HubKind { TurnIn, Accept, Vendor, Train }

    private sealed class HubTarget
    {
        public HubKind Kind;
        public int QuestId;         // TurnIn / Accept only
        public int NpcEntry;
        public Vec4 Pos;
        public bool CanRepair;      // Vendor only
        public int Legs;            // MOVE_TO legs issued toward this target
        public int ApproachFails;   // TeleportAssist ladder counter
    }

    private sealed class HubScratch
    {
        public DateTime Stamp;                       // the HubErrandUntil this run serves — the run token
        public float AnchorX, AnchorY;
        public int AnchorMap;
        public Queue<HubTarget> Work = new();
        public HubTarget? Cur;
        public int TurnIns, Accepts;
        public bool Vendored, Trained;
        public bool Aborting;                        // boss-guard unwind in flight (return hop first)
        public List<string> Notes = new();           // per-target skips, surfaced in the wrap line + log
    }

    // Planner-owned per-bot scratch. Deliberately NOT a BotContext scratch slot: the brain's
    // goal-change ResetScratch doesn't know this planner, and the Stamp check below self-heals
    // any staleness (a scratch from an interrupted run never survives into a new stamp).
    private readonly ConcurrentDictionary<int, HubScratch> _scratch = new();

    public HubErrandPlanner(QuestGraphLoader quests, ZoneDataLoader zoneData,
        SpellProgressionLoader trainers, ILogger<HubErrandPlanner> log)
    {
        _quests = quests;
        _zoneData = zoneData;
        _trainers = trainers;
        _log = log;
    }

    public Goal Handles => Goal.Vendoring;

    public StepResult PlanNext(BotContext ctx, BotStateSnapshot snap)
    {
        // ── Run-token liveness ──────────────────────────────────────────────
        // The GoalSelector only routes here while the stamp is live+unconsumed, but a WAIT
        // resolution can land one tick after "lets move" nulled it / the window lapsed.
        // Nothing to send: the goal flip's SET_TASK IDLE puts C++ back into follow.
        if (snap.HubErrandUntil is not DateTime stamp || DateTime.UtcNow >= stamp)
        {
            _scratch.TryRemove(ctx.Guid, out _);
            ctx.Failure = null;
            return StepResult.Complete();
        }

        _scratch.TryGetValue(ctx.Guid, out var s);
        if (s != null && s.Stamp != stamp)
            s = null;   // a NEW "do your rounds" mid-flight — the old round is dead, plan fresh

        // ── Fresh entry ─────────────────────────────────────────────────────
        if (s == null)
        {
            // Unwind any dangling teleport-assist BEFORE planning — an interrupted previous
            // run (re-command mid-hop) can leave the bot hopped into a nav pocket; never
            // anchor a new round inside one. The GoalSelector's teleport hold guarantees we
            // are still on this goal to run the unwind.
            if (ctx.Failure is { CommandType: "TELEPORT_TO" })
            {
                ctx.Failure = null;
                ctx.Teleport = null;   // the hop itself failed — we never left the mesh
            }
            if (ctx.Teleport is { } danglingTp)
            {
                if (danglingTp.Phase == TpPhase.Outbound || danglingTp.Phase == TpPhase.AtTarget)
                {
                    danglingTp.Phase = TpPhase.AtTarget;
                    _log.LogInformation("[HUB] {Name} fresh run found a dangling hop-in — returning to anchor before planning", ctx.Name);
                    return StepResult.Send(TeleportAssist.BeginReturn(ctx), "TELEPORT_ACK", TeleportAssist.AckDeadline);
                }
                ctx.Teleport = null;   // Inbound just landed — we're home
            }

            ctx.Failure = null;   // a stale failure can't belong to a run that hasn't started
            s = BuildPlan(ctx, stamp);
            _scratch[ctx.Guid] = s;

            if (s.Work.Count == 0)
                return Finish(ctx, s, "Nothing in reach here — sticking with you.");

            int ti = s.Work.Count(t => t.Kind == HubKind.TurnIn);
            int ac = s.Work.Count(t => t.Kind == HubKind.Accept);
            bool vend = s.Work.Any(t => t.Kind == HubKind.Vendor);
            bool trn = s.Work.Any(t => t.Kind == HubKind.Train);
            _log.LogInformation("[HUB] {Name} round planned: turnins={Ti} accepts={Ac} vendor={V} trainer={T} anchor=({X:F0},{Y:F0})@{Map} until={Until:HH:mm:ss}Z",
                ctx.Name, ti, ac, vend, trn, s.AnchorX, s.AnchorY, s.AnchorMap, stamp);

            ctx.SetStep("hub_start");
            var bits = new List<string>();
            if (ti > 0) bits.Add($"{ti} hand-in{(ti == 1 ? "" : "s")}");
            if (ac > 0) bits.Add($"{ac} pickup{(ac == 1 ? "" : "s")}");
            if (vend) bits.Add("vendor");
            if (trn) bits.Add("trainer");
            return Say(ctx, $"On it — doing my rounds: {string.Join(", ", bits)}.");
        }

        // ── Consume a failure FIRST (TrainingPlanner order) ─────────────────
        if (ctx.Failure != null)
        {
            var f = ctx.Failure;
            ctx.Failure = null;
            return OnFailure(ctx, s, f);
        }

        // ── Advance a committed teleport-assist round trip ──────────────────
        if (ctx.Teleport is { Phase: TpPhase.Outbound })
        {
            // Hopped in — the executor set ctx.Pos from the ack, so we're AT the target.
            ctx.Teleport.Phase = TpPhase.AtTarget;
            if (s.Cur is { } hopped)
            {
                _log.LogInformation("[HUB] {Name} teleported in → {Kind} entry={Entry}", ctx.Name, hopped.Kind, hopped.NpcEntry);
                return Business(ctx, s, hopped);
            }
            // No current target (shouldn't happen) — just go home.
            ctx.Teleport.Failed = true;
            return StepResult.Send(TeleportAssist.BeginReturn(ctx), "TELEPORT_ACK", TeleportAssist.AckDeadline);
        }
        if (ctx.Teleport is { Phase: TpPhase.Inbound })
        {
            // Back at the pre-hop anchor. The business outcome was already tallied/noted at
            // the target; the hop home is pure unwinding — advance (or finish an abort).
            ctx.Teleport = null;
            s.Cur = null;
            if (s.Aborting)
                return Finish(ctx, s, "Coming!");
            return DriveNext(ctx, s);
        }

        // ── Boss guard ──────────────────────────────────────────────────────
        // AFTER the teleport advance (a committed hop unwinds via the return leg above; the
        // GoalSelector's teleport hold keeps us on this goal until it does). -1 = no reading
        // yet — never abort on unknown.
        if (snap.PartyBossDist == BossOffMapSentinel || snap.PartyBossDist > BossAbortYards)
            return Abort(ctx, s, $"boss at {snap.PartyBossDist}yd");

        // ── Apply the leg whose WAIT just cleared ───────────────────────────
        switch (ctx.Step)
        {
            case "hub_route":
                {
                    // TASK_COMPLETE landed (the bridge refreshed ctx.Pos from its x|y|z).
                    if (s.Cur is not { } c) return DriveNext(ctx, s);
                    if (AtNpc(ctx, c)) return Business(ctx, s, c);
                    return DriveNext(ctx, s);   // short of the gate — another leg (capped)
                }

            case "hub_interact":
                {
                    // QUEST_ACCEPT_ACK / QUEST_COMPLETE_ACK cleared the WAIT.
                    if (s.Cur is { } c)
                    {
                        if (c.Kind == HubKind.TurnIn) s.TurnIns++;
                        else s.Accepts++;
                        _log.LogInformation("[HUB] {Name} {Kind} ok quest={Q} entry={E} (ti={Ti} ac={Ac})",
                            ctx.Name, c.Kind, c.QuestId, c.NpcEntry, s.TurnIns, s.Accepts);
                    }
                    return TargetDone(ctx, s);
                }

            case "hub_sell":
                {
                    // SELL_ACK landed. Gear may be wrecked even when nothing sold — repair when
                    // the NPC can, exactly the MaintenancePlanner shape.
                    s.Vendored = true;
                    if (s.Cur is { CanRepair: true } v)
                    {
                        ctx.SetStep("hub_repair");
                        _log.LogInformation("[HUB] {Name} sold (bag={Bag}) → REPAIR_AT_NPC entry={E}", ctx.Name, ctx.FreeSlots, v.NpcEntry);
                        return StepResult.Send(
                            new BridgeCommand("REPAIR_AT_NPC", new { npc_entry = v.NpcEntry }),
                            "REPAIR_ACK", AckDeadline);
                    }
                    _log.LogInformation("[HUB] {Name} sold (bag={Bag} dur={Dur}) — vendor done", ctx.Name, ctx.FreeSlots, ctx.Durability);
                    return TargetDone(ctx, s);
                }

            case "hub_repair":
                _log.LogInformation("[HUB] {Name} repaired (dur={Dur} cu={Cu}) — vendor done", ctx.Name, ctx.Durability, ctx.Copper);
                return TargetDone(ctx, s);

            case "hub_train":
                {
                    // TRAIN_ACK — C++ bought whatever the bot could afford. Clear the solo
                    // trigger's flag exactly as TrainingPlanner does; the next LEVEL_UP re-arms it.
                    if (ctx.Identity is { } id)
                    {
                        id.HasUnlearnedSpells = false;
                        id.TicksSinceLastTrained = 0;
                    }
                    s.Trained = true;
                    _log.LogInformation("[HUB] {Name} trained (cu={Cu})", ctx.Name, ctx.Copper);
                    return TargetDone(ctx, s);
                }
        }

        // No leg applied (hub_start just said its line / a resume tick) → drive the round.
        return DriveNext(ctx, s);
    }

    // ========================================================================
    // The route/business machine — one shape for all four target kinds.
    // ========================================================================

    private StepResult DriveNext(BotContext ctx, HubScratch s)
    {
        if (s.Cur == null)
        {
            if (s.Work.Count == 0)
                return Finish(ctx, s, WrapLine(s));
            s.Cur = s.Work.Dequeue();
            _log.LogInformation("[HUB] {Name} next target: {Kind} quest={Q} entry={E} @ ({X:F0},{Y:F0}) d={D:F0}yd",
                ctx.Name, s.Cur.Kind, s.Cur.QuestId, s.Cur.NpcEntry, s.Cur.Pos.X, s.Cur.Pos.Y,
                Dist2(ctx.Pos.X, ctx.Pos.Y, s.Cur.Pos.X, s.Cur.Pos.Y));
        }

        var c = s.Cur;
        if (AtNpc(ctx, c))
            return Business(ctx, s, c);   // consecutive same-NPC targets skip the route for free

        if (++c.Legs > MaxLegsPerTarget)
            return SkipCur(ctx, s, $"{c.Kind}:{c.NpcEntry} legs-exhausted");

        ctx.SetStep("hub_route");
        return StepResult.Send(
            new BridgeCommand("MOVE_TO", new { mapId = c.Pos.Map, x = c.Pos.X, y = c.Pos.Y, z = c.Pos.Z }),
            "TASK_COMPLETE", LegDeadline);
    }

    // At the target — fire its business command and WAIT on its ack.
    private StepResult Business(BotContext ctx, HubScratch s, HubTarget c)
    {
        switch (c.Kind)
        {
            case HubKind.TurnIn:
                ctx.SetStep("hub_interact");
                return StepResult.Send(
                    new BridgeCommand("QUEST_INTERACT", new { action = "complete", quest_id = c.QuestId, npc_entry = c.NpcEntry }),
                    "QUEST_COMPLETE_ACK", InteractDeadline);

            case HubKind.Accept:
                ctx.SetStep("hub_interact");
                return StepResult.Send(
                    new BridgeCommand("QUEST_INTERACT", new { action = "accept", quest_id = c.QuestId, npc_entry = c.NpcEntry }),
                    "QUEST_ACCEPT_ACK", InteractDeadline);

            case HubKind.Vendor:
                ctx.SetStep("hub_sell");
                return StepResult.Send(
                    new BridgeCommand("SELL_ITEMS", new { npc_entry = c.NpcEntry, keep_quality = 2 }),
                    "SELL_ACK", AckDeadline);

            default:   // HubKind.Train
                ctx.SetStep("hub_train");
                return StepResult.Send(
                    new BridgeCommand("TRAIN_AT_NPC", new { npc_entry = c.NpcEntry }),
                    "TRAIN_ACK", AckDeadline);
        }
    }

    // Current target finished cleanly. If we teleported INTO it, hop home BEFORE advancing —
    // never strand the bot in the pocket (the next leg's MOVE_TO out would no_path).
    private StepResult TargetDone(BotContext ctx, HubScratch s)
    {
        if (ctx.Teleport is { Phase: TpPhase.AtTarget })
            return StepResult.Send(TeleportAssist.BeginReturn(ctx), "TELEPORT_ACK", TeleportAssist.AckDeadline);
        s.Cur = null;
        return DriveNext(ctx, s);
    }

    // Skip the current target (route/interact failure) — note it, unwind a hop-in if one is
    // committed, move on. Per-target failure never ends the round; only the guards do.
    private StepResult SkipCur(BotContext ctx, HubScratch s, string why)
    {
        s.Notes.Add(why);
        _log.LogWarning("[HUB] {Name} skip target: {Why} pos=({X:F0},{Y:F0})@{Map}",
            ctx.Name, why, ctx.Pos.X, ctx.Pos.Y, ctx.MapId);
        if (ctx.Teleport is { Phase: TpPhase.AtTarget } tp)
        {
            tp.Failed = true;   // informational — the Inbound completion advances either way
            return StepResult.Send(TeleportAssist.BeginReturn(ctx), "TELEPORT_ACK", TeleportAssist.AckDeadline);
        }
        s.Cur = null;
        return DriveNext(ctx, s);
    }

    // ========================================================================
    // Failure consumption
    // ========================================================================
    private StepResult OnFailure(BotContext ctx, HubScratch s, WaitFailure f)
    {
        // The TELEPORT_TO hop ITSELF failed/deadlined.
        if (f.CommandType == "TELEPORT_TO" && ctx.Teleport is { } tpf)
        {
            var phase = tpf.Phase;
            ctx.Teleport = null;
            if (phase == TpPhase.Inbound)
            {
                // Business already ran at a safe town NPC — we just couldn't get home. The
                // per-target ladder self-heals the next leg out; note it and carry on.
                s.Notes.Add($"tp-return:{f.Reason}");
                s.Cur = null;
                if (s.Aborting)
                    return Finish(ctx, s, "Coming!");
                return DriveNext(ctx, s);
            }
            return SkipCur(ctx, s, $"teleport:{f.Reason}");   // couldn't hop in
        }

        // A no_path on the final approach, in the vicinity → the proven assist ladder:
        // first no_path retries (the leg may close it), the second, within reach, hops.
        if (ctx.Step == "hub_route" && s.Cur is { } c && TeleportAssist.IsApproachNoPath(f))
        {
            c.ApproachFails++;
            switch (TeleportAssist.Decide(c.ApproachFails, ctx.Pos, c.Pos, ctx.MapId))
            {
                case TeleportAssist.TpDecision.Teleport:
                    _log.LogInformation("[HUB] {Name} {Kind} entry={E} unreachable ({N}× no_path, {D:F0}yd) — TELEPORT_TO",
                        ctx.Name, c.Kind, c.NpcEntry, c.ApproachFails, Dist2(ctx.Pos.X, ctx.Pos.Y, c.Pos.X, c.Pos.Y));
                    return StepResult.Send(TeleportAssist.BeginOutbound(ctx, c.Pos), "TELEPORT_ACK", TeleportAssist.AckDeadline);
                case TeleportAssist.TpDecision.Retry:
                    ctx.SetStep("hub_route");
                    return StepResult.Send(
                        new BridgeCommand("MOVE_TO", new { mapId = c.Pos.Map, x = c.Pos.X, y = c.Pos.Y, z = c.Pos.Z }),
                        "TASK_COMPLETE", LegDeadline);
                    // TpDecision.GiveUp → fall through to the skip below (genuinely far — not a pocket)
            }
        }

        switch (ctx.Step)
        {
            case "hub_route":
                return SkipCur(ctx, s, $"route:{f.Reason}");

            case "hub_interact":
                // quest_log_full ends the ACCEPT phase for the whole round — every further
                // accept would fail the same way. Turn-ins/vendor/trainer are unaffected.
                if (f.Reason == "quest_log_full")
                {
                    int pruned = 0;
                    var kept = new Queue<HubTarget>();
                    while (s.Work.Count > 0)
                    {
                        var t = s.Work.Dequeue();
                        if (t.Kind == HubKind.Accept) { pruned++; continue; }
                        kept.Enqueue(t);
                    }
                    s.Work = kept;
                    return SkipCur(ctx, s, $"log-full (+{pruned} pickups dropped)");
                }
                return SkipCur(ctx, s, $"interact:{f.Reason}");

            case "hub_sell":
                // A phantom vendor (runtime despawn) skips; a deadline proceeds best-effort
                // to repair/done — the old economy-domain timeout behaviour, never a wedge.
                if (f.Reason is "vendor_not_found" or "missing_npc_entry")
                    return SkipCur(ctx, s, $"vendor-phantom:{f.Reason}");
                s.Vendored = true;
                if (s.Cur is { CanRepair: true } v2)
                {
                    ctx.SetStep("hub_repair");
                    return StepResult.Send(
                        new BridgeCommand("REPAIR_AT_NPC", new { npc_entry = v2.NpcEntry }),
                        "REPAIR_ACK", AckDeadline);
                }
                return TargetDone(ctx, s);

            case "hub_repair":
                // not_enough_gold is the economic wall, not an error; npc_not_found is a
                // phantom. Either way the sell leg already ran — finish the vendor stop.
                if (f.Reason != "not_enough_gold")
                    s.Notes.Add($"repair:{f.Reason}");
                return TargetDone(ctx, s);

            case "hub_train":
                return SkipCur(ctx, s, $"train:{f.Reason}");
        }

        return SkipCur(ctx, s, $"fail:{f.Reason}");
    }

    // ========================================================================
    // Round planning
    // ========================================================================
    private HubScratch BuildPlan(BotContext ctx, DateTime stamp)
    {
        var s = new HubScratch
        {
            Stamp = stamp,
            AnchorX = ctx.Pos.X,
            AnchorY = ctx.Pos.Y,
            AnchorMap = ctx.MapId
        };

        var id = ctx.Identity;
        if (id == null || !_quests.IsLoaded)
        {
            s.Notes.Add(id == null ? "no-identity" : "graph-loading");
            // Vendor + trainer below still work without the quest graph.
        }

        // Nearest usable vendor (repair-biased below the durability floor, with a sell-only
        // fallback — selling still helps even when no armorer is in the hub).
        HubTarget? vendor = null;
        {
            bool requireRepair = ctx.Durability < RepairRequiredBelowDurability;
            var v = _zoneData.GetNearestVendor(ctx.ZoneId, ctx.MapId, ctx.Pos.X, ctx.Pos.Y, ctx.Level, requireRepair);
            if (v == null && requireRepair)
                v = _zoneData.GetNearestVendor(ctx.ZoneId, ctx.MapId, ctx.Pos.X, ctx.Pos.Y, ctx.Level, false);
            if (v != null && v.MapId == s.AnchorMap
                && Dist2(s.AnchorX, s.AnchorY, v.X, v.Y) <= HubRadiusYards)
            {
                vendor = new HubTarget
                {
                    Kind = HubKind.Vendor,
                    NpcEntry = v.NpcEntry,
                    Pos = new Vec4(v.X, v.Y, v.Z, v.MapId),
                    CanRepair = v.CanRepair
                };
            }
            else
                s.Notes.Add("no-vendor-in-hub");
        }

        // (0) Bags tight → sell FIRST so turn-in rewards have room (a full-bag turn-in fails
        // CanRewardQuest → cannot_reward). A second vendor stop still runs at (3) — by then
        // the bot is usually standing next to it, so the extra stop is nearly free.
        if (vendor != null && ctx.FreeSlots <= BagsTightFreeSlots)
            s.Work.Enqueue(new HubTarget
            {
                Kind = HubKind.Vendor,
                NpcEntry = vendor.NpcEntry,
                Pos = vendor.Pos,
                CanRepair = vendor.CanRepair
            });

        // (1) Turn-ins: server-COMPLETE (or objective-less) held quests whose ender is in the hub.
        if (id != null && _quests.IsLoaded)
        {
            var turnIns = new List<HubTarget>();
            foreach (var kv in ctx.QuestLog)
            {
                var node = _quests.GetQuest(kv.Key);
                if (node == null) continue;
                bool actionable = kv.Value.Status == QuestStatusComplete || !node.HasObjectives;
                if (!actionable) continue;
                var ender = node.TurnIn ?? node.Giver;
                if (ender == null || ender.Map != s.AnchorMap) continue;
                if (Dist2(s.AnchorX, s.AnchorY, ender.X, ender.Y) > HubRadiusYards) continue;
                turnIns.Add(new HubTarget
                {
                    Kind = HubKind.TurnIn,
                    QuestId = kv.Key,
                    NpcEntry = ender.NpcEntry,
                    Pos = new Vec4(ender.X, ender.Y, ender.Z, ender.Map)
                });
            }
            foreach (var t in turnIns.OrderBy(t => Dist2(s.AnchorX, s.AnchorY, t.Pos.X, t.Pos.Y)))
                s.Work.Enqueue(t);

            // (2) Accepts: the FULL shared pick gate (IsPickable — race/class/level, red/grey,
            // deferrals, blacklists, drift rails, kill-only content), givers in the hub. The
            // active-set overload excludes in-log quests, so a held quest is never re-picked.
            // Knob: widening companions to item/GO content for party play is exactly the
            // objective-type clauses inside IsPickable — do it THERE (shared-filter invariant),
            // not here.
            int raceBit = QuestGraphLoader.RaceToBitmask(id.Race);
            int classBit = QuestGraphLoader.ClassToBitmask(id.ClassId);
            var active = new HashSet<int>(ctx.QuestLog.Keys);
            var accepts = new List<HubTarget>();
            foreach (var q in _quests.GetAvailableQuests(raceBit, classBit, id.Level, id.CompletedQuestIds, active))
            {
                if (!QuestPlanner.IsPickable(q, id)) continue;
                var g = q.Giver!;
                if (g.Map != s.AnchorMap) continue;
                if (Dist2(s.AnchorX, s.AnchorY, g.X, g.Y) > HubRadiusYards) continue;
                accepts.Add(new HubTarget
                {
                    Kind = HubKind.Accept,
                    QuestId = q.QuestId,
                    NpcEntry = g.NpcEntry,
                    Pos = new Vec4(g.X, g.Y, g.Z, g.Map)
                });
            }
            foreach (var t in accepts.OrderBy(t => Dist2(s.AnchorX, s.AnchorY, t.Pos.X, t.Pos.Y)))
                s.Work.Enqueue(t);
        }

        // (3) Vendor — always.
        if (vendor != null)
            s.Work.Enqueue(vendor);

        // (4) Trainer — always (see the header for why not HasUnlearnedSpells-gated).
        if (_trainers.IsLoaded && id != null)
        {
            var tr = _trainers.GetNearestTrainer(id.ClassId, ctx.MapId, ctx.Pos.X, ctx.Pos.Y);
            if (tr != null && tr.Map == s.AnchorMap
                && Dist2(s.AnchorX, s.AnchorY, tr.X, tr.Y) <= HubRadiusYards)
            {
                s.Work.Enqueue(new HubTarget
                {
                    Kind = HubKind.Train,
                    NpcEntry = tr.NpcEntry,
                    Pos = new Vec4(tr.X, tr.Y, tr.Z, tr.Map)
                });
            }
            else
                s.Notes.Add("no-trainer-in-hub");
        }

        return s;
    }

    // ========================================================================
    // Round end
    // ========================================================================

    // Boss guard tripped: he's leaving — drop the rounds, unwind any committed hop-in first,
    // consume the token, and follow (the C++ catch-up / instance-follow teleports close any gap).
    private StepResult Abort(BotContext ctx, HubScratch s, string why)
    {
        _log.LogInformation("[HUB] {Name} ABORT ({Why}) ti={Ti} ac={Ac} vendored={V} trained={T}",
            ctx.Name, why, s.TurnIns, s.Accepts, s.Vendored, s.Trained);
        if (ctx.Teleport is { Phase: TpPhase.AtTarget } tp)
        {
            s.Aborting = true;
            tp.Failed = true;
            return StepResult.Send(TeleportAssist.BeginReturn(ctx), "TELEPORT_ACK", TeleportAssist.AckDeadline);
        }
        return Finish(ctx, s, "Coming!");
    }

    // Consume the run token, drop the scratch, say the line. The Fire arms no WAIT, so the
    // very next tick the GoalSelector sees Done == stamp, falls to the Idle follow hold, and
    // the goal change SET_TASK IDLEs C++ back into formation.
    private StepResult Finish(BotContext ctx, HubScratch s, string line)
    {
        ctx.HubErrandDone = s.Stamp;
        _scratch.TryRemove(ctx.Guid, out _);
        ctx.SetStep("hub_done");
        _log.LogInformation("[HUB] {Name} FINISH ti={Ti} ac={Ac} vendored={V} trained={T} notes=[{Notes}]",
            ctx.Name, s.TurnIns, s.Accepts, s.Vendored, s.Trained, string.Join("; ", s.Notes));
        return Say(ctx, line);
    }

    private string WrapLine(HubScratch s)
    {
        var bits = new List<string>();
        if (s.TurnIns > 0) bits.Add($"turned in {s.TurnIns}");
        if (s.Accepts > 0) bits.Add($"picked up {s.Accepts}");
        if (s.Vendored) bits.Add("vendored");
        if (s.Trained) bits.Add("trained");
        string body = bits.Count > 0 ? string.Join(", ", bits) : "nothing needed doing";
        string skips = s.Notes.Count > 0 ? $" ({s.Notes.Count} skipped)" : "";
        return $"Rounds done — {body}{skips}.";
    }

    // Party line, fire-and-forget (chatType 1 = CHAT_MSG_PARTY; the C++ branch is live and
    // Group::BroadcastPacket delivers to every member incl. the boss's real client).
    private static StepResult Say(BotContext ctx, string text)
        => StepResult.Fire(new BridgeCommand("SAY_TEXT", new { text, chatType = 1 }));

    private static bool AtNpc(BotContext ctx, HubTarget c)
        => c.Pos.Map == ctx.MapId
           && Dist2(ctx.Pos.X, ctx.Pos.Y, c.Pos.X, c.Pos.Y) <= NpcReachYards;

    private static float Dist2(float ax, float ay, float bx, float by)
    {
        float dx = ax - bx, dy = ay - by;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private const int QuestStatusComplete = 1;   // VMaNGOS QUEST_STATUS_COMPLETE (== QuestPlanner)

    // Lenient backstop — every leg owns real liveness via its WAIT. Arm grace on entry, then
    // a long no-progress ceiling. On a genuine wedge the run token is consumed RIGHT HERE
    // (OnStall only receives ctx, and a reselect with a live+unconsumed stamp would route
    // straight back into this planner — the one loop this feature must never have).
    public bool IsProgressing(BotContext ctx, BotStateSnapshot snap)
    {
        if (ctx.TimeInGoalSec < 30) return true;
        if (ctx.TimeSinceProgressSec < 300) return true;
        if (snap.HubErrandUntil is DateTime st)
            ctx.HubErrandDone = st;
        _scratch.TryRemove(ctx.Guid, out _);
        _log.LogWarning("[HUB] {Name} no-progress ceiling — run token consumed, reselecting", ctx.Name);
        return false;
    }

    public StallAction OnStall(BotContext ctx)
        => StallAction.Of(StallActionKind.ReselectGoal, "hub:no_progress");
}