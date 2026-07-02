using MangosSuperUI.BotLogic.Core;
using MangosSuperUI.BotLogic.Data;

namespace MangosSuperUI.BotLogic.Planners;

// ============================================================================
// QuestPlanner — Goal.Questing (§ P3, the regression-killer) + BATCHING.
//
// Drives the whole BATCH the bot accepted this sweep, not one quest at a time:
//
//   gather -> accept-all -> objective-sweep -> turn-in-all -> reprocess -> (loop)
//
//   * gather: seed on the nearest in-reach giver (zone cap), travel there, then
//     fold in every Pickable quest within BatchRadius of the bot (the hub cluster).
//   * accept-all: visit each unaccepted giver nearest-first, accept its quests.
//   * objective-sweep: work the nearest unmet kill objective across the batch as a
//     section-4 ENRICHED MOVE_TO (creature_entry+grind_radius+kill_count -> C++
//     grinds at the mouth). After each, re-QUERY_QUEST_STATUS and recompute -- the
//     server credits a kill to EVERY accepted quest that needs it, so overlapping
//     objectives fall for free. A quest whose farthest unmet objective is >=2x the
//     mean of the others' is shelved for this sweep (the far-outlier rule).
//   * turn-in-all: only after the close objectives drain, turn in every COMPLETE
//     quest nearest-first (objectives BEFORE turn-ins; turn-ins BEFORE any far trek).
//   * reprocess: re-gather (follow-ups now eligible + locals passed en route) and
//     clear the sweep's deferrals so the far quest competes fresh. It becomes the
//     active target only when a reprocess yields nothing closer.
//
// Carry/drop policy (batching): a shelved quest stays ACCEPTED in the C++ log with
// its progress -- distance NEVER drops a quest, danger NEVER drops a quest
// (path_unsafe blacklists the route and shelves the sweep). The ONLY drop is a
// quest going GREY (out-leveled), via ABANDON_QUEST + BotIdentity.AbandonGrey.
//
// The carried set's durable truth is the C++ log + QUEST_STATUS_ALL; the batch
// scratch is rebuilt from it on (re)entry, so shelving survives goal bounces.
//
// Scope: kill, no-objective, and creature-sourced item quests (incl. kill+item). An item
// objective is driven as a grind leg on its best drop creature (auto-loot credits the drop).
// GO-interact and GO-sourced items are phase 2 (a USE_GAMEOBJECT leg); escort needs C++ FOLLOW.
// ============================================================================
public sealed class QuestPlanner : IBotPlanner
{
    private readonly QuestGraphLoader _quests;
    // _spawns / CreatureSpawnLoader: UNUSED on the solo path as of 2026-06-30 (was Scatter() — see below).
    // Kept as a constructor parameter rather than removed: changing the constructor signature risks
    // breaking DI wiring this file can't see (Program.cs registration). Harmless to leave; if grouping
    // ever wants per-bot dispersal again, the injected loader is right here.
    private readonly CreatureSpawnLoader _spawns;
    private readonly ILogger<QuestPlanner> _logger;
    // (removed) ZoneSafetyMap _zones + Random _rng — were used ONLY by RelocateGrindCenter, which is
    // deleted (its sole caller, the overpull relocate, was replaced by the unstick detour 2026-06-29).

    private static readonly TimeSpan TravelDeadline = TimeSpan.FromMinutes(8);    // continuation travel can be long (section 4.11); also the enriched-objective WAIT bound (travel + first kill) — the KILL-push then tightens it to 120s no-kill
    private static readonly TimeSpan InteractDeadline = TimeSpan.FromSeconds(20); // accept/turn-in acks are near-instant
    private static readonly TimeSpan DetourDeadline = TimeSpan.FromSeconds(60);   // unstick "kill 1 of whatever" — guard-bypassed pull lands fast; if it can't (truly empty area) the deadline escalates to a normal accepted-quest defer

    private const float GrindRadius = 60f;
    private const float ForceRadius = 150f;     // bot within this of a failed giver/turn-in => WMO last leg -> force_*
    private const int SafetyMargin = 3;         // level-gate = danger_level - margin
    private const int DeferMinutes = 15;
    private const int AbandonAfterDefers = 3;
    private const int QuestStatusComplete = 1;  // VMaNGOS QUEST_STATUS_COMPLETE
    private const double LogSyncCapSec = 3;      // (solo path retired) — kept only for the group-path GroupSyncSec sibling below
    private const double StateFreshCapSec = 6;   // obj_sync: wait up to this long for a post-kill STATE before re-deriving (one 5s heartbeat + margin). Must exceed the STATE interval so we never re-derive off pre-kill counts.
    private const double GroupSyncSec = 5;      // group consult: re-QUERY the log at most this often so the god bot's all-eligible-done gate sees server truth (shared kill-credit advances counts with no local ack)
    private const int GroupGrindSentinel = 9999;// group consult: kill_count for the shared-objective grind -- a ceiling never reached (no-WAIT), so C++ grinds the mob indefinitely until the god bot moves the objective. NOT 0 (0 can insta-complete the enriched leg)

    // -- Overflow grind (server-authoritative completion) --
    // When the SERVER still reports a kill quest INCOMPLETE (status != 3) but our local
    // QuestNode counts are all met, our requirement is stale/under (a quest_template patch
    // override the graph loaded at patch=0 doesn't have) or the quest has an objective our
    // graph doesn't model. Keep killing past our count so the server's credit can catch up,
    // BOUNDED — after MaxOverflowGrinds with no completion, durably defer it.
    private const int OverflowChunk = 3;        // extra kills per overflow attempt
    private const int MaxOverflowGrinds = 4;     // give-up cap → durable defer (can't finish with this data)
    private const int OverflowDeferMinutes = 60;

    // -- Batching knobs (BotTuning candidates) --
    private const float BatchRadius = 100f;     // cluster radius around a hub: fold these givers into the batch
    private const float OrderSlackYards = 175f; // "within reason" band: legs within this many yards of each other count as equidistant — progress/level decide inside the band, a nearer band still wins outright. The hub-vs-chain knob (BotTuning candidate).
    // A pending hand-in does NOT jump the queue ahead of an unfinished objective that is closer by more than
    // this. 0 = the objective wins whenever it is strictly closer than the ender (ties favor the hand-in, which
    // drains a log slot + unlocks follow-ups). Raise it to bias toward handing in on near-ties. (BotTuning candidate.)
    private const float TurnInYieldSlackYards = 0f;
    private const int BatchCap = 8;             // max quests carried at once (well under the 20-slot log cap)
    private const float GatherRescanYards = 50f;// re-gather for new local givers once moved this far mid-sweep
    private const float NpcReachYards = 10f;    // close enough to interact without a fresh MOVE_TO (C++ searches 15yd)
    private const int MaxReachTier = 3;         // widening scan: 0 = local hub (baseline cap); each tier adds ~900yd (one hub-hop), bounded by ZoneSafetyMap's level-aware ceiling. A bot that has drained the local hub scans OUTWARD for the next level-appropriate hub instead of grinding in place.

    // -- Macro-loop exit (durable shelve + commit-to-grind) --
    // A hard MOVE failure on an accepted quest's objective bumps the SAME unified per-quest fail
    // streak as an attributed death (BotIdentity.QuestFailStreak, also written by MaintenancePlanner).
    // At QuestFailCap the quest is durably deferred for DurableDeferMinutes (the bot stops re-resuming
    // → re-failing it); below the cap it takes the shorter escalating sweep-defer (transient blips).
    // When the batch then exhausts WITH active deferrals, GrindLock the bot for GrindLockMinutes so it
    // grinds for levels instead of oscillating quest⇄grind at tick speed (the spin backoff).
    private const int QuestFailCap = 3;          // death + no_path share this cap (mirror MaintenancePlanner) -- was 1; one death over-shelved bots into grind-lock
    private const int DurableDeferMinutes = 20;  // the at-cap durable shelve window
    private const int GrindLockMinutes = 20;     // commit-to-grind window on a deferral-driven batch exhaust
    private const int RequirementsRetryMinutes = 2; // requirements_not_met on accept = server-gated eligibility mismatch, not failed work -> transient skip only (never durable grind-lock ammo)

    // -- Red gate (acquisition ceiling; mirrors the grey FLOOR) --
    // A quest whose level is more than RedMargin above the bot is "red" (too hard) and must NOT be
    // NEWLY ACQUIRED. This is the missing upper bound: IsGrey drops out-leveled quests, but nothing
    // stopped an L9 bot from SEEDING an L12 Westfall quest (givers open well below quest level), so a
    // storm-emptied batch got refilled with next-zone, over-level work while in-progress local quests
    // sat shelved. Applied at ACQUISITION only (IsPickable) -- a red quest already in the C++ log is
    // still resumed (step 1 of BuildBatch) so we never abandon committed progress; we just stop GRABBING
    // new reds. Vanilla "red" begins at +5 (GetRedLevel); +4 keeps a one-level safety cushion for a bot
    // that is actively leveling and will reach the quest band within a kill or two.
    private const int RedMargin = 4;            // newly-acquire only quests whose level <= botLevel + this

    public QuestPlanner(QuestGraphLoader quests, CreatureSpawnLoader spawns, ILogger<QuestPlanner> logger)
    {
        _quests = quests;
        _spawns = spawns;
        _logger = logger;
    }

    public Goal Handles => Goal.Questing;

    // ========================================================================
    // PlanNext -- apply the leg whose WAIT just cleared (read from ctx.Step),
    //            then derive the next action from the batch + quest-log state.
    // ========================================================================
    public StepResult PlanNext(BotContext ctx, BotStateSnapshot snap)
    {
        // Group execution directive (grouping §3.2): the god bot stamped ONE shared objective for the
        // whole team this tick. Drive THAT objective instead of this bot's own batch -- every member
        // runs the same coords and the combat directive focus-fires the same mob. The god bot keeps
        // the objective stamped until every eligible holder is done (its server-count gate), so no
        // member runs ahead. None => fall through to the normal solo batch (accept / turn in / pick).
        // We deliberately leave ctx.Quest untouched: when the stamp clears, the solo path resumes the
        // SAME batch from the live log (turning in anything the team just completed).
        if (ctx.GroupOrder.IsActive)
            return DriveGroup(ctx);

        // A negated/expired WAIT surfaced a failure -> recover (batch-aware).
        if (ctx.Failure != null)
            return Recover(ctx);

        var q = ctx.Quest;

        // First entry -> new batch scratch, built straight off ctx.QuestLog. That log is fed by the 5s STATE
        // push (the QUERY_QUEST_STATUS pull is retired), so it is already complete C++ ground truth — no
        // per-entry handshake, no re-query throttle, no flood. A reselect that re-enters Questing just rebuilds
        // off the same live log.
        if (q == null)
        {
            // STATE keeps ctx.QuestLog continuously current (the QUERY_QUEST_STATUS pull is retired), and the
            // host only ticks a bot once its first STATE has landed (HasReceivedState), so the log is ground
            // truth right now — build straight off it. No sync handshake, no stale/empty-cache window: a goal
            // bounce (Training / level-up) returns here, BuildBatch resumes EVERY quest still in the C++ log,
            // and the collapse-to-one-quest is structurally impossible.
            ctx.Quest = q = new QuestScratch();
            BuildBatch(ctx, q);
            RefreshActiveIds(q);
            return Derive(ctx, q);
        }

        // Apply the completed leg (ctx.Step encodes which WAIT just cleared).
        switch (ctx.Step)
        {
            case "obj_sync":
                {
                    // After an objective grind completes, wait for the NEXT STATE so ctx.QuestLog reflects the
                    // kills the server just credited before we re-derive — otherwise we'd re-pick a now-satisfied
                    // leg off pre-kill counts. Synced = a STATE landed since we entered this step (stamp is the
                    // STATE arrival time, set in Sense); StateFreshCapSec (one heartbeat + margin) is the
                    // fallback if the heartbeat stalls. This replaces the old QUERY round-trip with the same
                    // freshness guarantee and no pull.
                    if (!Synced(ctx) && ctx.TimeInStepSec < StateFreshCapSec)
                        return StepResult.Wait();
                    q.Active = null;      // fresh counts -> derive the next objective
                    break;
                }
            case "to_giver":
                // Gate the accept on ACTUAL proximity. The travel leg's TASK_COMPLETE can be a stale or
                // duplicate ack (matches by type, no corr) that lands while the bot is still en route —
                // firing the accept from far away returns npc_not_found. Not actually at the giver →
                // re-issue the travel instead of a doomed interact. The executor's ArrivalGate normally
                // absorbs this; this is the planner-side backstop.
                //
                // In-log abort (re-accept march fix): a QUERY may have landed mid-trip and the log now
                // shows we already hold this quest (it was always in the C++ log; the giver leg was issued
                // off a stale snapshot). Don't finish the walk just to fire a redundant accept -- mark it
                // accepted, drop the leg, and re-derive straight to its objective/turn-in this same tick.
                if (q.Active != null && ctx.QuestLog.ContainsKey(q.Active.QuestId))
                {
                    q.Active.Accepted = true;
                    _logger.LogDebug("[QUEST] {Name} to_giver aborted -- [{Id}] already in log, resuming (no re-accept walk)",
                        ctx.Name, q.Active.QuestId);
                    q.Active = null;
                    ctx.SetStep("plan");
                    break;
                }
                if (q.Active?.Node.Giver is { } gv)
                {
                    if (AtNpc(ctx, gv)) { ctx.SetStep("accept"); return Interact(q.Active, accept: true); }
                    return MoveTo(gv);
                }
                break;
            case "accept":
                if (q.Active != null)
                {
                    q.Active.Accepted = true;
                    q.Active = null;
                    // We just accepted AT a giver, so any OTHER quest this giver (or
                    // one within BatchRadius) offers should be folded in and accepted now, before we leave --
                    // otherwise the seed/accept arrives from outside the cluster, grabs only the one quest, and
                    // the 10s en-route Rescan discovers the rest after we have walked off, marching us back (the
                    // accept-then-backtrack waste). Bounded (BatchCap/BatchRadius) and idempotent (adds only new);
                    // resets LastGatherPos so Derive's gated en-route gather this tick no-ops.
                    GatherLocals(ctx, q);
                }
                break;
            case "to_objective":
                // §4 enriched objective: the MOVE_TO carried creature_entry/grind_radius/kill_count,
                // so C++ engaged the mob on approach (ScanApproachTarget) and ground in place — its
                // single TASK_COMPLETE ("GRIND finished" at kill_count) = THIS objective done. There
                // is NO separate SET_TASK GRIND leg now. Re-sync so opportunistic credit on the OTHER
                // batched quests is seen, then derive the next objective / turn-in. The enriched
                // MOVE_TO never emits "arrived (seam crossed)" (the C++ seam-divert grinds at the
                // mouth), and its one WAIT is the backpressure — the brain can't re-issue under it,
                // so the June-16 seam-cross flood cannot recur.
                ctx.ClearObjective();   // grind leg done — drop Held so the obj_sync tick can't spuriously reconcile
                ctx.SetStep("obj_sync");
                return StepResult.Wait();   // wait for the next STATE (obj_sync re-derives on fresh counts) — pull retired
            case "grind_obj":
                // TASK_COMPLETE = kill_count reached. Re-sync so opportunistic credit on the
                // OTHER batched quests is seen, then derive the next objective / turn-in.
                ctx.ClearObjective();   // grind leg done — drop Held so the obj_sync tick can't spuriously reconcile
                ctx.SetStep("obj_sync");
                return StepResult.Wait();   // wait for the next STATE (obj_sync re-derives on fresh counts) — pull retired
            case "detour":
                // The unstick detour's single kill just completed (TASK_COMPLETE "GRIND finished"). The freeze
                // is broken (+1 kill, +XP, and the nearest mob was usually the quest mob itself → often quest
                // credit too). Snap back to the objective: re-sync the log (so any credit the detour kill
                // earned is seen) and re-derive — the still-unmet blocked leg is the nearest, so the bot
                // returns to it; if the kill COMPLETED it, derive advances to turn-in. Same tail as
                // to_objective. Held was cleared at detour-issue (the detour was the commitment); derive
                // re-stamps it. The premature-arrival guard exempts this (it's a grind TASK_COMPLETE, not travel).
                ctx.ClearObjective();
                ctx.SetStep("obj_sync");
                return StepResult.Wait();   // wait for the next STATE (obj_sync re-derives on fresh counts) — pull retired
            case "to_turnin":
                // Same proximity gate as to_giver: a stale/duplicate travel ack must not fire the turn-in
                // from far away (npc_not_found). Re-travel if we're not actually at the ender.
                if (q.Active != null)
                {
                    var npc = TurnInNpc(q.Active);
                    if (AtNpc(ctx, npc)) { ctx.SetStep("turnin"); return Interact(q.Active, accept: false); }
                    return MoveTo(npc);
                }
                break;
            case "turnin":
                if (q.Active != null)
                {
                    ctx.Identity?.CompletedQuestIds.Add(q.Active.QuestId);
                    ctx.Identity?.QuestDeferralCounts.Remove(q.Active.QuestId);
                    ctx.Identity?.QuestOverflowGrinds.Remove(q.Active.QuestId);
                    ctx.Identity?.QuestFailStreak.Remove(q.Active.QuestId);   // beat the quest → forget its fail history
                    _logger.LogInformation("[QUEST] {Name} completed [{Id}] \"{Title}\"",
                        ctx.Name, q.Active.QuestId, q.Active.Node.Title);
                    q.Batch.Remove(q.Active);
                    q.Active = null;
                    // Drain follow-ups in place. A turn-in just added this quest to CompletedQuestIds, so any
                    // follow-up gated on it -- frequently offered by the VERY NPC we are standing on (the ender
                    // is often also the next giver) -- is now Pickable. Re-gather HERE so it enters the batch and
                    // the accept phase grabs it in place this same derive, instead of deriving a far objective,
                    // leaving, and having the 10s en-route Rescan walk us all the way back to this NPC to accept
                    // it (the turn-in-then-backtrack waste seen live: complete 5261 at npc 196 -> leave -> rescan
                    // -> return to 196 for follow-up 33). Bounded + idempotent; resets LastGatherPos so Derive's
                    // gated en-route gather this tick no-ops.
                    GatherLocals(ctx, q);
                }
                break;
        }

        RefreshActiveIds(q);
        return Derive(ctx, q);
    }

    // ------------------------------------------------------------------------
    // IsProgressing -- lenient backstop (the real liveness is per-leg WAITs).
    // ------------------------------------------------------------------------
    public bool IsProgressing(BotContext ctx, BotStateSnapshot snap)
    {
        if (ctx.TimeInGoalSec < 30) return true;                 // arm grace on entering Questing
        return ctx.TimeSinceProgressSec < 300;                   // 5 min no progress + no WAIT => reselect
    }

    public StallAction OnStall(BotContext ctx)
        => StallAction.Of(StallActionKind.ReselectGoal, "quest:no_progress");

    // ------------------------------------------------------------------------
    // Rescan -- en-route interrupt for an objective trek (the BotBrain §3c hook).
    // Called WHILE an enriched-objective MOVE_TO is still in flight (its WAIT carries
    // RescanAtUtc). Re-gather around the bot's CURRENT position, throttled by the same
    // moved->=GatherRescanYards gate so a near-stationary grind no-ops cheaply. If new
    // local givers fold in, drop the in-flight far leg and re-derive -- the closer work
    // wins now; the far quest stays CARRIED (in the batch + the C++ log) and re-competes
    // on the next sweep. Otherwise keep trekking (Wait -> the brain rides the journey out,
    // no re-path). This is the piece that stops a single long trek from monopolizing.
    // ------------------------------------------------------------------------
    public StepResult Rescan(BotContext ctx, BotStateSnapshot snap)
    {
        var q = ctx.Quest;
        if (q == null) return StepResult.Wait();

        // Throttle: only re-scan once the bot has crossed a gather boundary.
        if (ctx.Pos.Dist2D(q.LastGatherPos) <= GatherRescanYards)
            return StepResult.Wait();

        bool added = GatherLocals(ctx, q);
        if (!added)
            return StepResult.Wait();          // nothing new on the way -> ride the trek out

        _logger.LogInformation("[QUEST] {Name} en-route rescan folded in new local quest(s) -- preempting trek",
            ctx.Name);
        q.Active = null;                        // release the in-flight far leg (still carried in the batch)
        return Derive(ctx, q);                  // closer work preempts; far quest re-competes next sweep
    }

    // ========================================================================
    // Derive -- the four-phase priority machine. One action per call.
    // ITERATIVE (bounded for-loop, hard pass cap) so a runaway re-derive can NEVER grow the stack:
    // the cap-outlier that was re-deferred every pass (StackOverflow that took the host down) now
    // degrades to a bounded loop that exits to grind. Logic fix is in phase 4 -- reprocess only on
    // genuinely-new locals (added), never on TagOutliers' STABLE cap-deferrals.
    // ========================================================================
    private StepResult Derive(BotContext ctx, QuestScratch q)
    {
        const int MaxDerivePasses = 16;   // legit reprocess is <= BatchCap overflow give-ups + 1 gather; 16 = headroom
        // Solo held-objective hygiene (Held-Objective build §2, solo extension): every fresh derive starts
        // with NO held objective; only an objective leg issued below (re)stamps it. So Held mirrors exactly
        // "the reconcilable solo leg currently in flight" — it survives the in-flight WAIT + a goal bounce
        // (the strand fix), but a turn-in / accept / grind-lock derive leaves it clear, so a stale Grind can
        // not drive a spurious re-issue. Group Held is coordinator-owned and stamped elsewhere (untouched).
        ctx.ClearObjective();
        for (int pass = 0; pass < MaxDerivePasses; pass++)
        {
            // -- 0. grey drop (the ONLY drop) -- one ABANDON per tick for accepted greys --
            var greyAccepted = q.Batch.FirstOrDefault(b => b.Accepted && !b.TurnedIn && IsGrey(b.Node, ctx.Level));
            if (greyAccepted != null)
            {
                _logger.LogInformation("[QUEST] {Name} grey-drop [{Id}] \"{Title}\" (qlvl {Lvl}, bot {Bot})",
                    ctx.Name, greyAccepted.QuestId, greyAccepted.Node.Title,
                    QuestLevelOf(greyAccepted.Node), ctx.Level);
                ctx.Identity?.AbandonGrey(greyAccepted.QuestId);
                q.Batch.Remove(greyAccepted);
                return StepResult.Fire(new BridgeCommand("ABANDON_QUEST", new { quest_id = greyAccepted.QuestId }));
            }
            // Un-accepted greys never enter the batch (filtered in GatherLocals); drop any that slipped in.
            q.Batch.RemoveAll(b => !b.Accepted && IsGrey(b.Node, ctx.Level));

            // En-route discovery: fold in new local givers once the bot has moved enough.
            if (ctx.Pos.Dist2D(q.LastGatherPos) > GatherRescanYards)
                GatherLocals(ctx, q);

            // In-log reconcile (re-accept march fix): flip any batch quest the C++ log already shows we
            // hold to Accepted=true so the accept phase never walks the bot to its giver for a redundant
            // accept (the idempotent C++ ack just rubber-stamps it -- the trip is pure wasted travel, hit
            // on every level-up / Training bounce when BuildBatch resumed off a pre-QUERY snapshot).
            ReconcileAcceptedFromLog(ctx, q);

            // -- 1. ACCEPT phase -- visit unaccepted givers nearest-first --
            var toAccept = q.Batch.Where(b => !b.Accepted && !b.Failed && b.Node.Giver != null).ToList();
            if (toAccept.Count > 0)
            {
                var bq = Nearest(ctx, toAccept, b => (b.Node.Giver!.X, b.Node.Giver!.Y, b.Node.Giver!.Map));
                if (bq != null)
                {
                    q.Active = bq;
                    if (AtNpc(ctx, bq.Node.Giver!)) { ctx.SetStep("accept"); return Interact(bq, accept: true); }
                    ctx.SetStep("to_giver");
                    return MoveTo(bq.Node.Giver!);
                }
            }

            // -- 1b. LOCAL TURN-IN -- hand in any complete quest whose ender is within the immediate
            //      cluster (BatchRadius, the same radius as the gather/accept hub) BEFORE leaving for an
            //      objective. The hub is now drained fully on both sides: accept everything nearby (phase 1),
            //      turn in everything nearby (here). This closes the starvation where a ready hand-in at an
            //      adjacent giver (e.g. an immediate follow-up hub) — and the chain it unlocks — sat behind a
            //      freshly-accepted FAR objective: turn-in was phase 3 (after the objective sweep), so the bot
            //      grabbed a new quest, ground its objective, and never reached the nearby hand-in. DISTANT
            //      enders are deliberately NOT caught here — they fall through to the phase-3 turn-in AFTER the
            //      objective sweep, preserving the "kill in the field, then hand in" no-yo-yo optimization. The
            //      bot is between legs when Derive runs (an in-flight objective leg holds its WAIT), so this is
            //      drain-between-legs, never a mid-travel divert.
            var localComplete = q.Batch.Where(b => b.Accepted && !b.TurnedIn && !b.Failed
                                                   && (IsComplete(ctx, b) || !b.Node.HasObjectives)
                                                   && WithinTurnInCluster(ctx, b)).ToList();
            if (localComplete.Count > 0)
            {
                var bq = Nearest(ctx, localComplete, b => { var l = TurnInNpc(b); return (l.X, l.Y, l.Map); });
                if (bq != null)
                {
                    q.Active = bq;
                    var npc = TurnInNpc(bq);
                    if (AtNpc(ctx, npc)) { ctx.SetStep("turnin"); return Interact(bq, accept: false); }
                    ctx.SetStep("to_turnin");
                    return MoveTo(npc);
                }
            }

            // -- 2. NEAR OBJECTIVE -- nearest unmet GRIND LEG, taken ONLY if within the immediate cluster
            //      (BatchRadius). A farther-but-reachable leg is NOT parked here: distance no longer changes
            //      batch membership, only PHASE ORDER. The far leg stays a live candidate and is taken as the
            //      phase-4b lone-far trek, AFTER local turn-ins (phase 1b/3) and a reprocess have had their
            //      shot — so the bot chains the near cluster, turns in for XP, grabs follow-ups, and only
            //      treks to the far one when nothing nearer remains. (This is "deprioritize, don't defer" —
            //      the old reach>cap shelve pulled far quests from the batch and could strand the bot into
            //      grind-lock; that shelve is removed in TagOutliers.)
            var withObj = q.Batch.Where(b => b.Accepted && !b.TurnedIn && !b.Failed && HasUnmet(ctx, b)).ToList();
            var candidates = withObj.Where(b => !b.Deferred).ToList();
            // Carries a near-pick that PriorityLeg chose but the BatchRadius gate below rejected —
            // phase 3 (TURN-IN) consults this so a turn-in can't silently jump the queue ahead of an
            // unmet objective that lost only because it sat just past the cluster radius (see the
            // yield check in phase 3 for the bug this closes). Null unless phase 2 actually computed
            // a candidate and rejected it for distance.
            (BatchQuest Quest, GrindLeg Leg)? nearMiss = null;
            if (candidates.Count > 0)
            {
                TagOutliers(ctx, candidates);                       // red-deprioritize only now (no distance park)
                var live = candidates.Where(b => !b.Deferred).ToList();
                var pick = PriorityLeg(ctx, live, startedGlobal: false);   // near pick: within-band (band -> started -> level -> dist)
                if (pick != null
                    && Dist2(ctx.Pos.X, ctx.Pos.Y, pick.Value.Leg.X, pick.Value.Leg.Y) <= BatchRadius)
                    return DispatchObjectiveLeg(ctx, q, pick.Value);   // near → work it now
                nearMiss = pick;   // null, or FAR → carried into phase 3's yield check; fall through (turn-ins, reprocess, then the phase-4b far trek)
            }

            // -- 2b. OVERFLOW grind -- the SERVER still reports this kill quest INCOMPLETE even
            //      though our local counts are all met. Our QuestNode.Count is stale/under (a
            //      quest_template patch override the graph loaded at patch=0 doesn't carry) or the
            //      quest has an objective our graph doesn't model. Keep killing past our count so
            //      the server can credit it -- BOUNDED by MaxOverflowGrinds, then durably defer
            //      (we can't finish it with this data; back off instead of looping forever). This
            //      is the server-authoritative completion the cannot_reward flood was missing.
            var stuck = q.Batch.Where(b => b.Accepted && !b.TurnedIn && !b.Failed
                                           && b.Node.HasKillObjectives
                                           && !IsComplete(ctx, b)
                                           && !HasUnmet(ctx, b)).ToList();
            if (stuck.Count > 0)
            {
                var oPick = NearestCreatureSlot(ctx, stuck);
                if (oPick != null)
                {
                    var bq = oPick.Value.Quest;
                    var o = oPick.Value.Obj;
                    var id = ctx.Identity;
                    int tries = id?.QuestOverflowGrinds.GetValueOrDefault(bq.QuestId, 0) ?? 0;

                    if (tries >= MaxOverflowGrinds)
                    {
                        id?.QuestOverflowGrinds.Remove(bq.QuestId);
                        id?.DeferQuest(bq.QuestId, TimeSpan.FromMinutes(OverflowDeferMinutes));
                        bq.Failed = true;   // carried in the log; resume skips it while deferred
                        _logger.LogInformation("[QUEST] {Name} can't complete [{Id}] \"{Title}\" — server INCOMPLETE after {N} overflow grinds (stale count / unmodeled objective); deferring {Min}min",
                            ctx.Name, bq.QuestId, bq.Node.Title, tries, OverflowDeferMinutes);
                        q.Active = null;
                        continue;
                    }

                    if (id != null) id.QuestOverflowGrinds[bq.QuestId] = tries + 1;
                    q.Active = bq;
                    q.ActiveSlot = oPick.Value.Slot;
                    ctx.SetStep("to_objective");
                    _logger.LogInformation("[QUEST] {Name} overflow grind [{Id}] slot {Slot} (server still INCOMPLETE past our count, try {N}/{Max})",
                        ctx.Name, bq.QuestId, o.Slot, tries + 1, MaxOverflowGrinds);
                    // Real-spawn coord, same fix as the normal sweep (UnmetLegs/NearestCreatureSlot):
                    // the giver-scoped cluster nearest the bot, not a single canonical centroid and not
                    // an unscoped global Scatter() pick (see NearestSpawnPoint for why). Routed through
                    // MoveToObjectiveLeg (a GrindLeg with Count=OverflowChunk → kill_count=OverflowChunk,
                    // identical to the retired MoveToObjective).
                    var op = NearestSpawnPoint(o.SpawnPositions, ctx.Pos.X, ctx.Pos.Y, o.GrindX, o.GrindY, o.GrindZ);
                    var oleg = new GrindLeg(o.CreatureEntry, op.X, op.Y, op.Z, o.GrindMap, OverflowChunk);
                    ctx.SetObjective(Objective.Grind(ObjectiveSource.SelfSolo,
                        oleg.CreatureEntry, oleg.X, oleg.Y, oleg.Z, oleg.Map,
                        OverflowChunk, bq.QuestId, o.Slot));   // SelfSolo overflow grind — same self-heal anchor
                    return MoveToObjectiveLeg(oleg);
                }
            }

            // -- 3. TURN-IN -- every quest the server says is done, nearest-first (after
            //      objectives, before any far trek). Ready = server COMPLETE (status 3) OR the
            //      quest has NO kill/item objectives at all. The second clause is for a
            //      NO-OBJECTIVE quest (783): it sits at status INCOMPLETE until you hand it in,
            //      so we walk to the ender and the turn-in interaction completes+rewards it.
            //      CRITICAL: the no-objective clause is gated to !HasObjectives, NOT !HasUnmet.
            //      A kill quest must reach server status==3 — turning in on a local "all kills
            //      met" tally is the cannot_reward flood (quest 21: our count says 12/12 done,
            //      server still says INCOMPLETE → reward refused forever). Server is authoritative;
            //      overflow grind (2b) drives a stale-count quest to real completion. !Failed
            //      avoids retrying a turn-in that already bounced this sweep.
            var complete = q.Batch.Where(b => b.Accepted && !b.TurnedIn && !b.Failed
                                              && (IsComplete(ctx, b) || !b.Node.HasObjectives)).ToList();
            if (complete.Count > 0)
            {
                var bq = Nearest(ctx, complete, b => { var l = TurnInNpc(b); return (l.X, l.Y, l.Map); });
                if (bq != null)
                {
                    var npc = TurnInNpc(bq);

                    // YIELD CHECK: a turn-in does not jump the queue ahead of an unfinished objective
                    // that is genuinely closer. Phase 2 only dispatches an unmet leg within BatchRadius
                    // (100yd) — a leg just past that cliff falls through to here UNWORKED even when it
                    // is closer than the turn-in we're about to walk to. Observed live: quest 33's wolves
                    // at d=113 lost the BatchRadius gate by 13yd, so quest 7's turn-in at d=139 went
                    // first — backwards, since 113 < 139. nearMiss is the exact candidate phase 2
                    // rejected for distance this pass; if it's still meaningfully closer than this
                    // turn-in (by more than TurnInYieldSlackYards), work it now instead — this is a
                    // direct distance comparison between two real options, not a hub-radius gate, so no
                    // cap applies here.
                    if (nearMiss != null)
                    {
                        float turnInD = Dist2(ctx.Pos.X, ctx.Pos.Y, npc.X, npc.Y);
                        float objD = Dist2(ctx.Pos.X, ctx.Pos.Y, nearMiss.Value.Leg.X, nearMiss.Value.Leg.Y);
                        if (objD + TurnInYieldSlackYards < turnInD)
                        {
                            _logger.LogInformation(
                                "[QUEST] {Name} turn-in [{TurnId}] yields to closer unmet objective [{ObjId}] (obj d={ObjD:F0} < turnin d={TiD:F0})",
                                ctx.Name, bq.QuestId, nearMiss.Value.Quest.QuestId, objD, turnInD);
                            return DispatchObjectiveLeg(ctx, q, nearMiss.Value);
                        }
                    }

                    q.Active = bq;
                    if (AtNpc(ctx, npc)) { ctx.SetStep("turnin"); return Interact(bq, accept: false); }
                    ctx.SetStep("to_turnin");
                    return MoveTo(npc);
                }
            }

            // -- 4. REPROCESS -- re-gather (follow-ups + new locals) and clear the sweep's
            //      deferrals so the far quest competes fresh. If anything changes, re-derive
            //      once: with closer locals the far one re-defers; with nothing closer it is
            //      no longer an outlier and gets worked (the lone-far trek). Terminates.
            bool added = GatherLocals(ctx, q);
            if (added)
            {
                foreach (var b in q.Batch) b.Deferred = false;
                continue;
            }

            // -- 4b. FAR OBJECTIVE (the lone-far trek) -- nothing near to grind, no local turn-in pending,
            //      nothing new to gather. NOW take the nearest unmet leg with NO distance cap: a deliberate
            //      trek to a reachable-but-far objective the near sweep (phase 2) skipped. Taken only as a
            //      last resort, so the bot never idles or grind-locks while real in-zone work remains.
            //      Cross-map legs are excluded by PriorityLeg, so a cross-continent objective is never walked
            //      to here — it rides in the log until the bot out-levels it and it greys out (the only drop).
            //      Path-safety (C++ IsPathSafe + the path_unsafe blacklist) still guards the trek itself.
            var farObj = q.Batch.Where(b => b.Accepted && !b.TurnedIn && !b.Failed && !b.Deferred && HasUnmet(ctx, b)).ToList();
            var farPick = PriorityLeg(ctx, farObj, startedGlobal: true);    // far trek: started-global — finish a started quest before starting fresh far work
            if (farPick != null)
                return DispatchObjectiveLeg(ctx, q, farPick.Value);

            // -- 5. nothing to accept / work / turn in / discover -> batch exhausted --
            // The carried set (incl. any Failed-this-sweep quests still in the C++ log) is resumed on
            // the next entry to Questing. If we got here BY SHELVING (there are active deferrals), the
            // in-reach content is all death/no_path-shelved — commit to grinding for a window so the bot
            // gains levels instead of oscillating quest⇄grind at tick speed (the spin backoff). A
            // genuinely quest-less bot (nothing deferred) skips the lock and grinds via normal
            // arbitration, so it still folds in a quest as it wanders into a fresh hub.
            // GRIND-LOCK INVARIANT (the owed guard). Before stamping a 20-min lock, two gates so the bot
            // can NEVER lock while it still has real work -- the failure seen live (locked at L4 with #33
            // mid-grind 5/8 and #15 unstarted, decided one tick BEFORE the QUEST_STATUS_ALL landed):
            //   (a) FRESHNESS -- never decide an exhaust off a stale/in-flight log. After a Training/
            //       Maintenance round-trip the re-sync may not have landed (grind credit + a just-done
            //       turn-in invisible). Stale -> re-QUERY and wait; the verdict is made on server truth only.
            //   (b) WORKABILITY -- never grind-lock while ANY in-log quest is workable on this map, read
            //       straight off ctx.QuestLog (WorkableInLog), INDEPENDENT of the batch. A quest the batch
            //       dropped (resume filter) or a stale-snapshot race cannot strand the bot. Workable-in-log
            //       but no batch leg = batch starvation, not a real exhaust: reselect (rebuild off the fresh
            //       log), do not lock.
            // (FRESHNESS gate retired.) ctx.QuestLog is fed continuously by STATE (≤ one 5s heartbeat old) and
            // is the complete C++ log, so there is no stale/in-flight snapshot to re-QUERY for before deciding
            // an exhaust — the verdict is always made on current server truth. WORKABILITY still guards the
            // lock: never grind-lock while any in-log quest is workable on this map (read straight off
            // ctx.QuestLog, independent of the batch), so a batch the resume filter dropped or a transient
            // race cannot strand the bot — it reselects and rebuilds off the live log instead.
            int workableId = WorkableInLog(ctx);
            if (workableId != 0)
            {
                _logger.LogWarning("[QUEST] {Name} exhaust suppressed -- quest [{Id}] still workable in the log (batch starvation, not a real exhaust); reselecting instead of grind-lock",
                    ctx.Name, workableId);
                ctx.Quest = null;
                return StepResult.Block("no_quests");   // reselect -> re-enter -> rebuild off the fresh log
            }
            var lockId = ctx.Identity;
            if (lockId != null && lockId.DeferredQuestIds.Count > 0
                && !(lockId.GrindLockUntil is DateTime gl && DateTime.UtcNow < gl))
            {
                lockId.GrindLockUntil = DateTime.UtcNow.AddMinutes(GrindLockMinutes);
                _logger.LogInformation("[QUEST] {Name} batch exhausted with {N} deferred — grind-lock {Min}min (commit to leveling)",
                    ctx.Name, lockId.DeferredQuestIds.Count, GrindLockMinutes);
            }
            ctx.Quest = null;
            return StepResult.Block("no_quests");
        }

        // Pass cap hit -- a re-derive ran away (should not happen with the phase-4 gate). Exit to grind
        // rather than spin: any future loop-class regression becomes a logged no-op, never an SOE.
        _logger.LogWarning("[QUEST] {Name} derive exceeded {Max} passes -- exhausting to grind (recursion guard)",
            ctx.Name, MaxDerivePasses);
        ctx.Quest = null;
        return StepResult.Block("no_quests");
    }

    private StepResult Recover(BotContext ctx)
    {
        var f = ctx.Failure!;
        ctx.Failure = null;
        var q = ctx.Quest;
        var active = q?.Active;
        if (q == null || active == null) return StepResult.Wait();   // unknown leg -> re-derive next tick

        bool lastLeg = ctx.DistToTarget >= 0 && ctx.DistToTarget < ForceRadius;

        // no_path on the LAST LEG to a giver/turn-in = a WMO-interior NPC the navmesh
        // can't reach. force_* bypasses proximity (300 yd, all eligibility gates intact).
        if (f.Reason == "no_path" && lastLeg && !active.ForceMode
            && (ctx.Step == "to_giver" || ctx.Step == "to_turnin"))
        {
            active.ForceMode = true;
            _logger.LogInformation("[QUEST] {Name} no_path last-leg -> force {Step} [{Id}]",
                ctx.Name, ctx.Step, active.QuestId);
            if (ctx.Step == "to_giver") { ctx.SetStep("accept"); return Interact(active, accept: true); }
            ctx.SetStep("turnin"); return Interact(active, accept: false);
        }

        // PATH_UNSAFE: blacklist the route. Not yet accepted -> level-defer the pick and
        // drop it. Already accepted (committed) -> keep it in the log, shelve this sweep.
        if (f.Reason == "path_unsafe")
        {
            if (f.Dest.HasValue)
                ctx.Identity?.AddPathBlacklist(f.Dest.Value.X, f.Dest.Value.Y, f.DangerLevel);
            if (!active.Accepted)
            {
                ctx.Identity?.DeferQuestUntilLevel(active.QuestId, f.DangerLevel, SafetyMargin);
                q.Batch.Remove(active);
                _logger.LogInformation("[QUEST] {Name} deferring pick [{Id}] (path_unsafe, until lvl {Lvl})",
                    ctx.Name, active.QuestId, Math.Max(1, f.DangerLevel - SafetyMargin));
            }
            else
            {
                // R21: durably level-defer too, so the BuildBatch resume loop skips it until
                // the bot out-levels the danger. Without this the quest re-resumes every entry,
                // re-walks the blacklisted route, re-fails — the path_unsafe churn flood.
                ctx.Identity?.DeferQuestUntilLevel(active.QuestId, f.DangerLevel, SafetyMargin);
                active.Failed = true;   // carried in the log; resume skips it while deferred
                _logger.LogInformation("[QUEST] {Name} shelving [{Id}] (path_unsafe, deferred to lvl {Lvl}, kept accepted)",
                    ctx.Name, active.QuestId, Math.Max(1, f.DangerLevel - SafetyMargin));
            }
            q.Active = null;
            ctx.SetStep("plan");      // clear the stale leg so next tick derives, never re-fires a sync QUERY
            return StepResult.Wait();
        }

        // GRIND FREEZE (overpull_dwell | no_target): C++ froze on this objective's field — every reachable
        // candidate is over the solo cap, OR the quest mobs are all dead/tapped right now. The quest is NOT
        // at fault and the field is NOT unsafe: do NOT shelve, blacklist, or defer. Break the freeze with a
        // guaranteed single kill — an in-place SET_TASK GRIND entry=0 kill_count=1 at the bot's spot. C++
        // treats (entry==0 && killGoal==1) as an UNSTICK pull and bypasses the overpull veto once (the
        // in-combat retreat stays armed), so it ALWAYS lands a kill — and the nearest valid mob is usually
        // the very mob we were frozen beside, so this often credits the blocked quest directly. On its
        // TASK_COMPLETE (step "detour") we re-sync + re-derive, snapping back to the objective.
        //
        // Held is CLEARED here on purpose: during the detour the bot's committed task is the detour, not the
        // objective — leaving Held set would make the reconcile (BotBrain 1c) fight the detour (Held=Grind
        // entry=K vs C++ echo entry=0 → spurious re-issue). Derive re-stamps Held when the objective resumes.
        if (f.Reason == "overpull_dwell" || f.Reason == "no_target")
        {
            ctx.ClearObjective();
            ctx.SetStep("detour");

            _logger.LogInformation("[QUEST] {Name} grind freeze ({Reason}) [{Id}] — unstick: kill 1 in place, then resume",
                ctx.Name, f.Reason, active.QuestId);

            return StepResult.Send(
                new BridgeCommand("SET_TASK", new
                {
                    task = "GRIND",
                    x = ctx.Pos.X,
                    y = ctx.Pos.Y,
                    z = ctx.Pos.Z,
                    radius = GrindRadius,
                    creature_entry = 0,
                    kill_count = 1
                }),
                "TASK_COMPLETE", DetourDeadline);
        }

        // A NO-OBJECTIVE quest whose TURN-IN the server refused: it needs an action this
        // planner can't perform (use-item / explore / talk-to-roamer), and there is no kill
        // progress to preserve. Abandon it so it can't wedge the batch (the June-16 spin's
        // worst case). Gated hard to !HasObjectives, so a kill quest with real progress is
        // NEVER abandoned here — those only ever shelve.
        if (f.CommandType == "QUEST_INTERACT" && ctx.Step == "turnin"
            && active.Accepted && !active.Node.HasObjectives)
        {
            _logger.LogInformation("[QUEST] {Name} abandoning un-completable no-objective quest [{Id}] (turn-in refused: {Reason})",
                ctx.Name, active.QuestId, f.Reason);
            ctx.Identity?.AbandonGrey(active.QuestId);   // permanent skip set (also excludes from resume)
            q.Batch.Remove(active);
            q.Active = null;
            ctx.SetStep("plan");
            return StepResult.Fire(new BridgeCommand("ABANDON_QUEST", new { quest_id = active.QuestId }));
        }

        // Everything else (no_path far / no_progress / empty_path / cross_map / deadline /
        // interact requirements_not_met). Not accepted -> time-defer + drop. Accepted ->
        // shelve this sweep (keep accepted, carried).
        // requirements_not_met on an UN-accepted pick = the graph offered a quest the server gates (a
        // prereq/condition the graph doesn't model, or a negative-exclusive-group sibling still owed). This
        // is an eligibility mismatch, NOT failed work, so it must NOT durably defer (that made it grind-lock
        // ammo). Drop it with a short transient skip: out of this sweep, eligible again the moment its real
        // gate clears. (The QuestGraphLoader negative-group fix narrows most of these at the source.)
        if (f.Reason == "requirements_not_met" && !active.Accepted)
        {
            ctx.Identity?.DeferQuest(active.QuestId, TimeSpan.FromMinutes(RequirementsRetryMinutes));
            ctx.Quest!.Batch.Remove(active);
            _logger.LogInformation("[QUEST] {Name} dropping ineligible pick [{Id}] (requirements_not_met -- server-gated; transient {Min}min, not a durable defer)",
                ctx.Name, active.QuestId, RequirementsRetryMinutes);
            q.Active = null;
            ctx.SetStep("plan");
            return StepResult.Wait();
        }

        if (!active.Accepted)
            DeferPick(ctx, active, f.Reason);
        else
            DeferAcceptedQuest(ctx, active, f.Reason);   // R21: durable escalating defer + Failed (carried)
        q.Active = null;
        ctx.SetStep("plan");          // clear the stale leg so next tick derives, never re-fires a sync QUERY
        return StepResult.Wait();
    }

    // Time-defer an UN-accepted pick and drop it from the batch (the deferral keeps it
    // out of the next gather; it becomes eligible again when the gate clears).
    private void DeferPick(BotContext ctx, BatchQuest bq, string reason)
    {
        var id = ctx.Identity;
        if (id != null)
        {
            int prior = id.QuestDeferralCounts.GetValueOrDefault(bq.QuestId, 0);
            bool valuable = bq.Node.IsPartOfChain || bq.Node.HasItemReward;
            bool frustrated = !valuable && prior + 1 >= AbandonAfterDefers;
            id.DeferQuest(bq.QuestId, TimeSpan.FromMinutes(frustrated ? 60 : DeferMinutes));
            _logger.LogInformation("[QUEST] {Name} deferring pick [{Id}] ({Reason}){Frus}",
                ctx.Name, bq.QuestId, reason, frustrated ? " [frustrated 60min]" : "");
        }
        ctx.Quest!.Batch.Remove(bq);
    }

    // R21: durably defer an ACCEPTED quest (escalating) while KEEPING it in the C++ log.
    // Unlike DeferPick it does NOT drop the quest from the log — the kill progress is real
    // and worth preserving — it only shelves it (Failed=true) and stamps a durable deferral
    // so the BuildBatch resume loop skips it for the window instead of re-resuming → re-failing
    // every tick (the no_progress / cannot_reward churn). Becomes eligible again on expiry.
    private void DeferAcceptedQuest(BotContext ctx, BatchQuest bq, string reason)
    {
        var id = ctx.Identity;
        if (id != null)
        {
            // Unified fail streak: a hard MOVE failure on this quest's objective counts toward the
            // SAME durable shelve as an attributed death (MaintenancePlanner bumps the same map). At
            // the cap the quest is shelved for the long window so the bot stops re-resuming →
            // re-failing it (the no_path churn); below the cap it takes the shorter escalating
            // sweep-defer for transient reachability blips. With QuestFailCap=1 the first hard
            // failure goes straight to the durable shelve.
            int fails = id.QuestFailStreak.GetValueOrDefault(bq.QuestId, 0) + 1;
            id.QuestFailStreak[bq.QuestId] = fails;

            if (fails >= QuestFailCap)
            {
                id.DeferQuest(bq.QuestId, TimeSpan.FromMinutes(DurableDeferMinutes));
                id.QuestFailStreak.Remove(bq.QuestId);
                _logger.LogInformation("[QUEST] {Name} shelving [{Id}] ({Reason}, durable {Min}min, kept accepted) — {N} fail(s)",
                    ctx.Name, bq.QuestId, reason, DurableDeferMinutes, fails);
            }
            else
            {
                int prior = id.QuestDeferralCounts.GetValueOrDefault(bq.QuestId, 0);
                bool valuable = bq.Node.IsPartOfChain || bq.Node.HasItemReward;
                bool frustrated = !valuable && prior + 1 >= AbandonAfterDefers;
                id.DeferQuest(bq.QuestId, TimeSpan.FromMinutes(frustrated ? 60 : DeferMinutes));
                _logger.LogInformation("[QUEST] {Name} shelving [{Id}] ({Reason}, deferred, kept accepted)",
                    ctx.Name, bq.QuestId, reason);
            }
        }
        else
        {
            _logger.LogInformation("[QUEST] {Name} shelving [{Id}] ({Reason}, deferred, kept accepted)",
                ctx.Name, bq.QuestId, reason);
        }
        bq.Failed = true;   // carried in the log; resume skips it while the deferral holds
    }

    // ========================================================================
    // Batch construction
    // ========================================================================
    private void BuildBatch(BotContext ctx, QuestScratch q)
    {
        q.Batch.Clear();
        q.LastGatherPos = ctx.Pos;
        var id = ctx.Identity;
        if (id == null || !_quests.IsLoaded) return;
        id.PruneExpiredDeferrals();
        id.PrunePathBlacklist();

        // 1. Resume every in-log accepted quest (the carried set -- durable truth).
        //    INSTRUMENTED: ctx.QuestLog is ground truth (the C++ player log). If a quest is in the cache
        //    but does NOT make it into the batch, the bot has "lost" a live quest -- so every skip is
        //    recorded with its exact reason and the carried-vs-skipped split is logged. This is the line
        //    that names WHICH continue drops an in-progress quest (the collapse-to-one-quest bug).
        var carried = new List<int>();
        var skipped = new List<string>();
        foreach (var kv in ctx.QuestLog)
        {
            var node = _quests.GetQuest(kv.Key);
            if (node?.Giver == null) { skipped.Add($"{kv.Key}:giver-null"); continue; }
            if (id.CompletedQuestIds.Contains(node.QuestId)) { skipped.Add($"{kv.Key}:completed"); continue; }     // already rewarded
            if (id.AbandonedGreyQuestIds.Contains(node.QuestId)) { skipped.Add($"{kv.Key}:grey"); continue; }      // greyed out
            if (id.DeferredQuestIds.ContainsKey(node.QuestId)) { skipped.Add($"{kv.Key}:deferred"); continue; }    // R21: backing off (level/time defer)
            // A server-COMPLETE or no-objective quest needs only a turn-in (no objective driving) -- carry
            // it regardless of objective drivability so it can be handed in. Otherwise it vanishes from the
            // planner's view and the batch reads "exhausted" while a ready quest sits in the log (the
            // grind-lock-at-L4 trap). An INCOMPLETE quest we can't drive (GO / unresolved item) is still
            // phase-2 deferred as before -- and is now RECORDED so a wrongly-dropped in-progress quest is visible.
            bool actionableOnTurnIn = kv.Value.Status == QuestStatusComplete || !node.HasObjectives;
            if (!actionableOnTurnIn)
            {
                if (!node.Objectives.All(o => o.IsCreature)) { skipped.Add($"{kv.Key}:noncreature-obj"); continue; }          // GO-interact objectives: phase 2
                if (!node.ItemObjectives.All(it => it.BestDropSource != null)) { skipped.Add($"{kv.Key}:item-unresolved"); continue; }  // GO-sourced/unresolved items: phase 2
            }
            if (q.Batch.Any(b => b.QuestId == node.QuestId)) { skipped.Add($"{kv.Key}:dup"); continue; }
            q.Batch.Add(new BatchQuest { QuestId = node.QuestId, Node = node, Accepted = true });
            carried.Add(node.QuestId);
        }
        if (ctx.QuestLog.Count > 0)
            _logger.LogInformation("[QUEST] {Name} resume: cache=[{Cache}] carried=[{Carried}] skipped=[{Skipped}]",
                ctx.Name, string.Join(",", ctx.QuestLog.Keys), string.Join(",", carried),
                skipped.Count == 0 ? "none" : string.Join(" ", skipped));
        // 2. Fold in the local cluster around the bot; if nothing's accepted yet and no
        //    cluster exists here, seed on the nearest in-reach giver (zone cap) to travel to.
        GatherLocals(ctx, q);
        if (q.Batch.Count == 0)
        {
            var seed = PickFor(ctx);
            if (seed != null && !IsGrey(seed, id.Level))
            {
                q.Batch.Add(new BatchQuest { QuestId = seed.QuestId, Node = seed, Accepted = false });
                _logger.LogInformation("[QUEST] {Name} seeding batch on [{Id}] \"{Title}\"",
                    ctx.Name, seed.QuestId, seed.Title);
            }
        }

        _logger.LogInformation("[QUEST] {Name} batch built -- {N} quests", ctx.Name, q.Batch.Count);
    }

    // Add every Pickable quest whose giver is within BatchRadius of the bot (the hub
    // cluster) that isn't already batched. Returns true if anything new was added.
    private bool GatherLocals(BotContext ctx, QuestScratch q)
    {
        var id = ctx.Identity;
        if (id == null || !_quests.IsLoaded) return false;
        id.PruneExpiredDeferrals();
        q.LastGatherPos = ctx.Pos;

        var have = q.Batch.Select(b => b.QuestId).ToHashSet();
        bool added = false;

        foreach (var node in Pickable(_quests, id)
                     .Where(n => !have.Contains(n.QuestId))
                     .Where(n => !ctx.QuestLog.ContainsKey(n.QuestId))   // already in the C++ log => not a fresh pick (resumed by BuildBatch step 1). Pickable uses the no-active GetAvailableQuests overload, which returns in-log quests, so without this an already-held quest gets re-seeded Accepted=false and the bot marches to its giver to re-accept (the re-accept walk).
                     .Where(n => WithinBatch(ctx, n))
                     .OrderBy(n => Dist2(ctx.Pos.X, ctx.Pos.Y, n.Giver!.X, n.Giver!.Y)))
        {
            if (q.Batch.Count >= BatchCap) break;
            if (IsGrey(node, id.Level)) { id.AbandonGrey(node.QuestId); continue; }
            q.Batch.Add(new BatchQuest { QuestId = node.QuestId, Node = node, Accepted = false });
            added = true;
        }
        return added;
    }

    // In-log accept reconcile (re-accept march fix). The C++ log (ctx.QuestLog, refreshed by
    // QUEST_STATUS_ALL) is the durable truth for what is accepted. A batch quest can read Accepted=false
    // yet ALREADY be in the log -- BuildBatch step-1 resume ran off a pre-QUERY snapshot (a Questing
    // re-entry right after a level-up / Training bounce), so it missed the quest, and GatherLocals then
    // re-seeded it as a fresh pick (Pickable uses the no-active GetAvailableQuests overload, which does
    // NOT exclude in-log quests). The accept phase would then march the bot ALL THE WAY to the giver to
    // fire an accept the C++ idempotent-ack just rubber-stamps -- pure wasted travel, on every level-up.
    // Flip any such quest to Accepted=true so it skips straight to objective/turn-in. No per-quest
    // seeding is needed: the objective/turn-in phases read ctx.QuestLog by id+slot (HasUnmet / IsComplete
    // / RawRemaining), exactly like the BuildBatch step-1 resume shape.
    private void ReconcileAcceptedFromLog(BotContext ctx, QuestScratch q)
    {
        foreach (var b in q.Batch)
        {
            if (b.Accepted || b.TurnedIn) continue;
            if (!ctx.QuestLog.ContainsKey(b.QuestId)) continue;
            b.Accepted = true;
            _logger.LogDebug("[QUEST] {Name} already-in-log [{Id}] \"{Title}\" -- skip giver, resume from log (no re-accept walk)",
                ctx.Name, b.QuestId, b.Node.Title);
        }
    }

    // The nearest in-reach pickable -- used only to SEED an empty batch so the bot travels to
    // a hub it isn't standing on yet. Reach escalates by tier (ReachTier): a bot that has drained
    // the local hub scans OUTWARD for the next level-appropriate hub rather than grinding in place.
    // GoalSelector arbitrates on the SAME ReachTier, so the goal and this seed never disagree.
    private QuestNode? PickFor(BotContext ctx)
    {
        var id = ctx.Identity;
        if (id == null || !_quests.IsLoaded) return null;
        id.PruneExpiredDeferrals();
        var pickable = Pickable(_quests, id).ToList();
        int tier = ReachTier(pickable, id, ctx.Pos.X, ctx.Pos.Y, ctx.MapId, ctx.ZoneId);
        if (tier < 0) return null;
        float cap = ZoneSafetyMap.GetMaxTravelDistance(id.Level, ctx.ZoneId, tier);
        return pickable
            .Where(n => InReach(n, ctx.Pos.X, ctx.Pos.Y, ctx.MapId, cap))
            .OrderBy(n => Dist2(ctx.Pos.X, ctx.Pos.Y, n.Giver!.X, n.Giver!.Y))
            .FirstOrDefault();
    }

    /// <summary>
    /// Quests this planner can take + complete now: known giver, kill-only (or no)
    /// objectives, not deferred, giver not blacklisted, not greyed out. Shared with
    /// GoalSelector so arbitration matches what the planner can pick.
    /// </summary>
    public static IEnumerable<QuestNode> Pickable(QuestGraphLoader graph, BotIdentity id)
    {
        int raceBit = QuestGraphLoader.RaceToBitmask(id.Race);
        int classBit = QuestGraphLoader.ClassToBitmask(id.ClassId);
        return graph.GetAvailableQuests(raceBit, classBit, id.Level, id.CompletedQuestIds)
                    .Where(q => IsPickable(q, id));
    }

    public static bool IsPickable(QuestNode q, BotIdentity id)
         => q.Giver != null
            && q.Objectives.All(o => o.IsCreature)                       // GO-interact objectives: phase 2
            && q.ItemObjectives.All(it => it.BestDropSource != null)     // creature-sourced items only; GO-sourced/unresolved: phase 2
            && !id.CompletedQuestIds.Contains(q.QuestId)                 // 2026-06-30 fix: GatherLocals/GoalSelector's only chokepoint that did NOT
                                                                         // re-check completion — GetAvailableQuests was trusted blindly here while
                                                                         // BuildBatch's resume loop and WorkableInLog both double-check explicitly.
                                                                         // A goal bounce (Training/level-up) rebuilds the batch fresh; if the loader
                                                                         // ever hands back an already-rewarded quest, this was the one place nothing
                                                                         // caught it — re-offering a just-turned-in quest as a brand-new pick, walking
                                                                         // the bot back to re-"accept" it (idempotent ACK, no real accept), and
                                                                         // dispatching a second full objective grind for a quest that can never be
                                                                         // turned in again. Mirrors the existing defensive check at BuildBatch's
                                                                         // resume loop and WorkableInLog — this was the one gap.
            && !id.DeferredQuestIds.ContainsKey(q.QuestId)
            && !id.AbandonedGreyQuestIds.Contains(q.QuestId)
            && !IsGrey(q, id.Level)                                       // grey-filter hole: GoalSelector's pick must agree with BuildBatch's grey-reject, else pick>0 / batch=0 → the quest⇄grind tick-spin
            && !IsRed(q, id.Level)                                        // acquisition ceiling: don't NEWLY grab an over-level (red) quest -- e.g. an L9 bot seeding an L12 Westfall quest. Already-accepted reds resume via BuildBatch step 1 (this filters NEW picks only, and GoalSelector shares it so goal==pick).
            && !id.IsPathBlacklisted(q.Giver.X, q.Giver.Y)
            && q.Objectives.All(o => !id.IsPathBlacklisted(o.GrindX, o.GrindY));   // back off a death pocket as an AREA: a kill objective in a blacklisted cell is unpickable, not just its giver


    /// <summary>
    /// Range gate (OPEN #1): giver on the bot's map within the level/zone travel cap.
    /// Applied alongside IsPickable by both PickFor and GoalSelector (shared-filter invariant).
    /// </summary>
    public static bool InReach(QuestNode q, float botX, float botY, int botMap, float cap)
    {
        if (q.Giver == null || q.Giver.Map != botMap) return false;
        float dx = botX - q.Giver.X, dy = botY - q.Giver.Y;
        return dx * dx + dy * dy <= cap * cap;
    }

    /// <summary>
    /// Lowest escalation tier (0..MaxReachTier) at which at least one of <paramref name="pickable"/>
    /// is in reach of the bot, or -1 if none even at the widest tier. Tier 0 is the baseline cap;
    /// each higher tier widens the radius (ZoneSafetyMap.GetMaxTravelDistance, level-bounded ceiling)
    /// so a bot that has drained the local hub scans OUTWARD for the next level-appropriate hub
    /// instead of grinding in place. Shared by GoalSelector (arbitration) and PickFor (seed) so the
    /// goal and the pick are computed off the IDENTICAL reach -- no quest/grind bounce. The hard
    /// path-safety guardrail (C++ IsPathSafe + the C# PathBlacklist) is independent and still applies,
    /// so a widened pick on an unsafe route still path_unsafe-defers (R21) rather than thrashing.
    /// </summary>
    public static int ReachTier(IReadOnlyList<QuestNode> pickable, BotIdentity id,
        float botX, float botY, int botMap, int zoneId)
    {
        for (int tier = 0; tier <= MaxReachTier; tier++)
        {
            float cap = ZoneSafetyMap.GetMaxTravelDistance(id.Level, zoneId, tier);
            for (int i = 0; i < pickable.Count; i++)
                if (InReach(pickable[i], botX, botY, botMap, cap))
                    return tier;
        }
        return -1;
    }

    // Giver within the batch CLUSTER radius of the bot (same map).
    private static bool WithinBatch(BotContext ctx, QuestNode n)
    {
        if (n.Giver == null || n.Giver.Map != ctx.MapId) return false;
        return Dist2(ctx.Pos.X, ctx.Pos.Y, n.Giver.X, n.Giver.Y) <= BatchRadius;
    }

    // The TURN-IN ender is within the immediate hub cluster (BatchRadius — the same radius the gather
    // and accept use), i.e. a hand-in to drain locally before leaving for an objective rather than a far
    // trek. Same-map only; a cross-map ender is never "local". Mirrors WithinBatch but keys on the ender.
    private static bool WithinTurnInCluster(BotContext ctx, BatchQuest b)
    {
        var npc = TurnInNpc(b);
        if (npc.Map != ctx.MapId) return false;
        return Dist2(ctx.Pos.X, ctx.Pos.Y, npc.X, npc.Y) <= BatchRadius;
    }

    // ========================================================================
    // Objective selection
    // ========================================================================

    // Shelve (Deferred = true for this sweep) ONLY a quest that would take the bot OUTSIDE its
    // current allowed travel range — i.e. its FARTHEST unmet objective is beyond GetMaxTravelDistance
    // for this level/zone (tier-0 baseline, ~2200yd pre-10). A quest whose objectives are ALL within
    // range is never shelved here, no matter how many it has or how much nearer another quest happens
    // to be: PriorityLeg orders by band -> progress -> level -> distance, so an in-range quest simply becomes "#2" and gets
    // worked. (Replaces the old relative 2x-mean-of-others rule, which shelved trivially-reachable
    // quests — e.g. a 375yd objective next to a 5yd one — and dropped bots into needless grinding.)
    private void TagOutliers(BotContext ctx, List<BatchQuest> candidates)
    {
        var id = ctx.Identity;
        if (id == null) return;

        // (2026-06-29) The out-of-range DISTANCE PARK was REMOVED here. Distance no longer DEFERS an
        // objective — it only changes PHASE ORDER (near sweep = phase 2 ≤ BatchRadius; farther-but-reachable
        // = the phase-4b lone-far trek, after local turn-ins + reprocess). A far quest is therefore never
        // pulled from the batch — the pull is what used to strand a bot into grind-lock — it just sorts last
        // and, if the bot out-levels it first, greys out and drops. Cross-map legs are auto-excluded by
        // PriorityLeg. Only the red deprioritize below still sweep-defers: an over-level quest is a QUALITY
        // problem (don't pull the bot into red content while in-level work remains), not a distance one.

        // Red-deprioritize: an already-ACCEPTED over-level (red) quest -- e.g. an L9 bot that grabbed an
        // L12 Westfall quest before the IsRed acquisition gate, then got death-ported INTO Westfall so its
        // red objective is now the NEAREST leg -- otherwise keeps pulling the bot deeper into over-level
        // content ahead of its in-range Elwynn work. We never ABANDON it (carry policy: only grey drops),
        // so it stays in the log and resumes once the bot levels into its band. We only SWEEP-DEFER it here,
        // and ONLY while at least one non-red in-range quest remains to work this sweep -- so a bot that has
        // nothing BUT a red still works the red rather than idling.
        var nonRedLive = candidates.Any(b => !b.Deferred && !IsRed(b.Node, id.Level));
        if (nonRedLive)
        {
            foreach (var b in candidates)
            {
                if (b.Deferred) continue;
                if (IsRed(b.Node, id.Level))
                {
                    b.Deferred = true;
                    _logger.LogInformation("[QUEST] {Name} deprioritizing accepted red quest [{Id}] \"{Title}\" (qlvl {Lvl}, bot {Bot}) — working in-level quests first",
                        ctx.Name, b.QuestId, b.Node.Title, QuestLevelOf(b.Node), id.Level);
                }
            }
        }
    }

    // A drivable grind leg: kill CreatureEntry at (X,Y,Z) until Count is owed. Count is
    // kills-owed for a kill objective, or items-owed for a creature-sourced item objective
    // (routed to the item's best drop creature). The §4 enriched MOVE_TO is identical for
    // both — C++ grinds the entry and auto-loots; the server credits kills AND drops.
    //
    // AltEntries (2026-06-30, wolf-meat fix): OTHER creature entries that satisfy the SAME
    // item-drop objective as CreatureEntry — set ONLY for item-drop legs whose
    // ItemDropSource ties on drop chance with one or more local same-map siblings
    // (QuestGraphLoader.AltDropEntries). Null/default for every kill-objective leg — a kill
    // quest names one specific creature and the server only credits that exact entry, so
    // widening match there would be wrong, not just unnecessary. CreatureEntry alone still
    // drives the dispatch coordinate (X/Y/Z); AltEntries only widens what C++ treats as a
    // valid hit once it's out there (the approach scan / grind target picker / kill-credit).
    private readonly record struct GrindLeg(int CreatureEntry, float X, float Y, float Z, int Map, int Count,
        IReadOnlyList<int>? AltEntries = null);

    // The unmet grind legs of a quest THIS tick: one per still-short kill objective + one per
    // still-short creature-sourced item objective. GO-interact objectives and GO-sourced items
    // are phase 2 (not emitted → not driven here).
    //
    // COORDINATE SOURCE (fixed 2026-06-30): GrindX/GrindY is ONE representative point for the
    // whole objective — the spawn nearest the cluster centroid, snapped by QuestGraphLoader at
    // load time (giver-scoped: nearest cluster of real spawns to the quest giver, see
    // ResolveKillTargetsPerQuest). That single point is what PriorityLeg used to score against —
    // so "is this quest close right now" was measured to one fixed anchor, not to whichever real
    // mob the bot is actually standing next to. The loader ALSO keeps the full cluster
    // (obj.SpawnPositions / src.SpawnPositions) for exactly this reason (Session 31: Spawn
    // Fan-Out). We now score AND dispatch off the spawn in that cluster nearest the bot's CURRENT
    // position — still giver-scoped (never reaches outside the curated cluster into an unrelated
    // pack of the same creature elsewhere on the map), but reflects where the bot actually is.
    // Falls back to the canonical GrindX/Y/Z when SpawnPositions is empty (unresolved objective).
    private IEnumerable<GrindLeg> UnmetLegs(BotContext ctx, BatchQuest b)
    {
        foreach (var o in b.Node.Objectives)
        {
            if (!o.IsCreature || o.Count <= 0) continue;
            int rem = RawRemaining(ctx, b.QuestId, o);
            if (rem <= 0) continue;
            var p = NearestSpawnPoint(o.SpawnPositions, ctx.Pos.X, ctx.Pos.Y, o.GrindX, o.GrindY, o.GrindZ);
            yield return new GrindLeg(o.CreatureEntry, p.X, p.Y, p.Z, o.GrindMap, rem);   // kill objective — no alt entries, ever
        }
        foreach (var it in b.Node.ItemObjectives)
        {
            if (it.Count <= 0) continue;
            int rem = RawItemRemaining(ctx, b.QuestId, it);
            if (rem <= 0) continue;
            var src = it.BestDropSource;                       // creature-sourced only (GO-sourced = phase 2)
            if (src == null || src.SpawnCount <= 0) continue;
            var p = NearestSpawnPoint(src.SpawnPositions, ctx.Pos.X, ctx.Pos.Y, src.GrindX, src.GrindY, src.GrindZ);
            yield return new GrindLeg(src.CreatureEntry, p.X, p.Y, p.Z, src.GrindMap, rem, it.AltDropEntries);
        }
    }


    // The real spawn within a giver-scoped cluster nearest (fromX,fromY) — 2D, Z carried along for
    // the matching point. Empty/null cluster → the canonical single point (today's pre-fix
    // behavior, the correct degrade when an objective's cluster never resolved).
    private static (float X, float Y, float Z) NearestSpawnPoint(
        IReadOnlyList<(float X, float Y, float Z)> spawns, float fromX, float fromY,
        float fallbackX, float fallbackY, float fallbackZ)
    {
        if (spawns == null || spawns.Count == 0) return (fallbackX, fallbackY, fallbackZ);
        var best = spawns[0];
        float bestD = Dist2(fromX, fromY, best.X, best.Y);
        for (int i = 1; i < spawns.Count; i++)
        {
            float d = Dist2(fromX, fromY, spawns[i].X, spawns[i].Y);
            if (d < bestD) { bestD = d; best = spawns[i]; }
        }
        return best;
    }

    // The best same-map unmet leg across the live batch, by a priority key (replaces the old pure-nearest
    // pick). "Closest" is no longer the only axis: from a hub the bot should chain the quest it already
    // STARTED / a lower-level quest, not bolt to whatever harder objective happens to sit a few yards
    // nearer. The key is a lexicographic ValueTuple; the only real knob is OrderSlackYards (the band width).
    //
    //   within-band   (startedGlobal == false, the phase-2 NEAR pick):  (band, started, level, dist)
    //   started-global(startedGlobal == true,  the phase-4b FAR trek):  (started, band, level, dist)
    //
    // band     = floor(dist / OrderSlackYards) — legs in the same band count as equidistant ("within
    //            reason"); in within-band mode a nearer band wins outright.
    // started  = 0 if the quest has accrued ANY kill/item credit (mid-quest — finish it), else 1. Outranks
    //            mere level: completing in-progress work beats starting a slightly-lower quest.
    // level    = quest level, lower first (unknown/scaling sorts last).
    // dist     = raw distance, the final tiebreak (the old behaviour, now the LAST word not the only one).
    //
    // within-band is band-FIRST, so its pick is always in the nearest occupied band — it can never skip
    // near work for something far (keeps the phase-2 <=BatchRadius gate honest). started-global is used
    // only by the far trek, which by construction runs AFTER near work is exhausted: there, finishing a
    // started quest first is the right fallback even if it's a band or two out.
    private (BatchQuest Quest, GrindLeg Leg)? PriorityLeg(BotContext ctx, List<BatchQuest> live, bool startedGlobal)
    {
        // Lexicographic priority: STARTED (finish what you have accrued credit on) > lower QUEST LEVEL >
        // nearest "within reason" distance BAND > true DISTANCE. started+level dominate distance, so a
        // started OR lower-level quest is NEVER abandoned for a nearer-but-harder one (the live bug: a
        // started L2 kobold quest [7] dropped for an unstarted L4 Defias quest [18] whose objective sat in
        // a nearer band). Distance only decides between equals -- the band is the "within reason" cushion at
        // equal level (equal level -> nearest band -> true distance, your rule 5). The SAME key is used near
        // and far; the near/far/turn-in sequencing is enforced by PHASE ORDER (phase-2 <=BatchRadius dispatch
        // gate, then turn-ins, then the phase-4b far fallback), not by re-ranking here. startedGlobal now
        // only labels the decision log.
        var scored = new List<((int started, int level, int band, float dist) key, BatchQuest q, GrindLeg leg, string desc)>();
        foreach (var b in live)
        {
            int started = HasProgress(ctx, b) ? 0 : 1;
            int level = QuestLevelOf(b.Node);
            if (level <= 0) level = int.MaxValue;                       // unknown/scaling sorts last
            foreach (var leg in UnmetLegs(ctx, b))
            {
                if (leg.Map != ctx.MapId) continue;                     // cross-map legs ride the log (carry policy)
                float d = Dist2(ctx.Pos.X, ctx.Pos.Y, leg.X, leg.Y);
                int band = (int)(d / OrderSlackYards);
                scored.Add(((started, level, band, d), b, leg,
                    $"[{b.QuestId}] st={started} lvl={(level == int.MaxValue ? -1 : level)} band={band} d={d:F0}"));
            }
        }
        if (scored.Count == 0) return null;
        scored.Sort((a, c) => a.key.CompareTo(c.key));
        // Decision log (only on real contention, >=2 competing legs): the winning key vs the runner-up, so
        // an ordering question is answered from the log instead of inferred from snapshots.
        if (scored.Count >= 2)
            _logger.LogInformation("[QUEST] {Name} PriorityLeg ({Mode}, started>level>band>dist) chose {Best} over {Next}",
                ctx.Name, startedGlobal ? "far" : "near", scored[0].desc, scored[1].desc);
        return (scored[0].q, scored[0].leg);
    }

    // "Started" = the quest has accrued any server-side kill or item credit on a leg, i.e. the bot is
    // mid-quest on it. Read from ctx.QuestLog (the QUEST_STATUS_ALL-refreshed counts), so it survives a
    // train/level round-trip — exactly the case where the old pure-nearest pick abandoned in-progress work.
    private static bool HasProgress(BotContext ctx, BatchQuest b)
    {
        if (!ctx.QuestLog.TryGetValue(b.QuestId, out var e)) return false;
        foreach (var c in e.MobCounts) if (c > 0) return true;
        foreach (var c in e.ItemCounts) if (c > 0) return true;
        return false;
    }

    // RETIRED 2026-06-30 (Build 2 scatter, shipped 2026-06-29). No longer called from the solo path.
    //
    // It sampled a RANDOM REAL SPAWN COORD across the creature's WHOLE per-map footprint
    // (CreatureSpawnLoader — every spawn of this entry anywhere on the map, unscoped) so co-holders
    // wouldn't dogpile the single representative GrindX/GrindY. That fixed the dogpile, but it also
    // decoupled the dispatch coordinate from whatever PriorityLeg had just scored: a leg could be
    // ranked "near" (distance to the canonical centroid) and then dispatched to a random — possibly
    // far, possibly in an unrelated patch of the same creature elsewhere on the map — spawn. That
    // mismatch is what put a bot at (-8915,-252) for a leg scored against (-8869,-163).
    //
    // UnmetLegs/NearestCreatureSlot now resolve coordinates from QuestObjective.SpawnPositions /
    // ItemDropSource.SpawnPositions — the GIVER-SCOPED cluster QuestGraphLoader already curates per
    // quest (ResolveKillTargetsPerQuest) — picking the real spawn in THAT cluster nearest the bot
    // (NearestSpawnPoint). That point is both accurate (never reaches into an unrelated pack of the
    // same creature elsewhere on the map, unlike the global per-entry table here) and walked to
    // directly, so score and dispatch can't disagree. Anti-dogpile is now incidental: bots at
    // different positions naturally resolve to different nearest spawns within the cluster.
    //
    // Left defined (not deleted) in case grouping wants global per-bot dispersal again — _spawns
    // (CreatureSpawnLoader) is still injected for that reason. Not currently called.
    private GrindLeg Scatter(GrindLeg leg)
    {
        var sp = _spawns.SampleScatterPoint(leg.CreatureEntry, leg.Map);
        if (sp == null) return leg;                                  // no footprint → canonical
        return leg with { X = sp.X, Y = sp.Y, Z = sp.Z };           // Z is a real spawn Z; C++ ReGroundZ snaps on arrival
    }

    // Overflow target: the nearest same-map creature slot among quests the server still calls
    // INCOMPLETE despite our local counts being met. Unlike the §2 leg picker it does NOT gate on
    // RawRemaining > 0 (by definition all slots are already model-met here) — we re-grind to push
    // the server's credit past our stale count. Scored against the nearest real spawn in the
    // giver-scoped cluster (NearestSpawnPoint), same fix as UnmetLegs — not the single canonical
    // GrindX/GrindY, which can read far closer or farther than where the bot actually is.
    private (BatchQuest Quest, int Slot, QuestObjective Obj)? NearestCreatureSlot(BotContext ctx, List<BatchQuest> set)
    {
        (BatchQuest, int, QuestObjective)? best = null;
        float bestD = float.MaxValue;
        foreach (var b in set)
        {
            for (int i = 0; i < b.Node.Objectives.Length; i++)
            {
                var o = b.Node.Objectives[i];
                if (!o.IsCreature || o.Count <= 0) continue;
                if (o.GrindMap != ctx.MapId) continue;
                var p = NearestSpawnPoint(o.SpawnPositions, ctx.Pos.X, ctx.Pos.Y, o.GrindX, o.GrindY, o.GrindZ);
                float d = Dist2(ctx.Pos.X, ctx.Pos.Y, p.X, p.Y);
                if (d < bestD) { bestD = d; best = (b, i, o); }
            }
        }
        return best;
    }

    // ========================================================================
    // Quest-log readers
    // ========================================================================
    private bool HasUnmet(BotContext ctx, BatchQuest b) => UnmetLegs(ctx, b).Any();

    private bool IsComplete(BotContext ctx, BatchQuest b)
        => ctx.QuestLog.TryGetValue(b.QuestId, out var e) && e.Status == QuestStatusComplete;

    // Is ANY quest in the C++ log workable BY THIS PLANNER, ON THIS MAP, right now? Read straight off
    // ctx.QuestLog (server truth), INDEPENDENT of the batch, so a quest the batch dropped (resume filter)
    // or a stale-snapshot race cannot be missed. Mirrors exactly what Derive can act on this map: a
    // COMPLETE / no-objective quest whose ender is here (phase 1b/3 turn-in), or an INCOMPLETE quest with
    // a drivable creature/item leg here (phase 2/4b). A cross-map-only quest is NOT counted (it rides in
    // the log until the bot is on that map or out-levels it -- the carry policy). Deferred quests are
    // skipped (legitimately shelved). Returns the first workable quest id, else 0 -- the id is logged so a
    // batch-vs-log starvation is visible. This is the read behind the phase-5 grind-lock invariant.
    private int WorkableInLog(BotContext ctx)
    {
        var id = ctx.Identity;
        if (id == null) return 0;
        foreach (var kv in ctx.QuestLog)
        {
            int qid = kv.Key;
            if (id.CompletedQuestIds.Contains(qid)) continue;        // already rewarded
            if (id.AbandonedGreyQuestIds.Contains(qid)) continue;    // abandoned
            if (id.DeferredQuestIds.ContainsKey(qid)) continue;      // legitimately shelved (path_unsafe / durable)
            var node = _quests.GetQuest(qid);
            if (node?.Giver == null) continue;
            if (IsGrey(node, ctx.Level)) continue;                   // out-leveled -> will be dropped, not workable
            var e = kv.Value;

            // COMPLETE or no-objective -> a turn-in; workable iff the ender is on this map (phase 1b/3).
            if (e.Status == QuestStatusComplete || !node.HasObjectives)
            {
                var ender = node.TurnIn ?? node.Giver;
                if (ender != null && ender.Map == ctx.MapId) return qid;
                continue;
            }

            // INCOMPLETE -> workable iff a drivable creature/item leg is unmet ON THIS MAP (phase 2/4b).
            foreach (var o in node.Objectives)
            {
                if (!o.IsCreature || o.Count <= 0) continue;
                if (o.GrindMap != ctx.MapId) continue;
                int got = (o.Slot >= 1 && o.Slot <= e.MobCounts.Length) ? e.MobCounts[o.Slot - 1] : 0;
                if (got < o.Count) return qid;
            }
            foreach (var it in node.ItemObjectives)
            {
                if (it.Count <= 0) continue;
                var src = it.BestDropSource;                          // creature-sourced only (GO-sourced = phase 2)
                if (src == null || src.GrindMap != ctx.MapId) continue;
                int got = (it.Slot >= 1 && it.Slot <= e.ItemCounts.Length) ? e.ItemCounts[it.Slot - 1] : 0;
                if (got < it.Count) return qid;
            }
        }
        return 0;
    }

    // Raw kills still owed for an objective (can be <=0 = satisfied). Full count if unknown.
    private static int RawRemaining(BotContext ctx, int questId, QuestObjective o)
    {
        if (ctx.QuestLog.TryGetValue(questId, out var e) && o.Slot >= 1 && o.Slot <= e.MobCounts.Length)
            return o.Count - e.MobCounts[o.Slot - 1];
        return o.Count;
    }

    // Items still owed for a creature-sourced item objective (server ItemCounts authoritative).
    // Full count if unknown.
    private static int RawItemRemaining(BotContext ctx, int questId, QuestItemReq it)
    {
        if (ctx.QuestLog.TryGetValue(questId, out var e) && it.Slot >= 1 && it.Slot <= e.ItemCounts.Length)
            return it.Count - e.ItemCounts[it.Slot - 1];
        return it.Count;
    }

    // ========================================================================
    // Grey-out (vanilla gray-level formula on quest level)
    // ========================================================================
    private static int QuestLevelOf(QuestNode n) => n.QuestLevel > 0 ? n.QuestLevel : n.MinLevel;

    private static bool IsGrey(QuestNode n, int botLevel)
    {
        int ql = QuestLevelOf(n);
        if (ql <= 0) return false;                    // scaling / unknown level -> never auto-drop
        return ql <= GrayLevel(botLevel);
    }

    // Player::GetGrayLevel (vanilla). Target/quest is gray when its level <= this.
    private static int GrayLevel(int pl)
    {
        if (pl <= 5) return 0;
        if (pl <= 39) return pl - 5 - pl / 10;
        if (pl <= 59) return pl - 1 - pl / 5;
        return pl - 9;
    }

    // Red = too far ABOVE the bot to newly acquire (the acquisition ceiling, mirror of IsGrey).
    // Scaling/unknown-level quests (ql <= 0) are never red-blocked, same as the grey side. Gates
    // ACQUISITION only (IsPickable) -- a red already in the log is resumed, never abandoned for being red.
    private static bool IsRed(QuestNode n, int botLevel)
    {
        int ql = QuestLevelOf(n);
        if (ql <= 0) return false;                    // scaling / unknown level -> never block
        return ql > botLevel + RedMargin;
    }

    // ========================================================================
    // Geometry / batch bookkeeping
    // ========================================================================
    private static bool AtNpc(BotContext ctx, QuestNpcLocation npc)
        => npc.Map == ctx.MapId && Dist2(ctx.Pos.X, ctx.Pos.Y, npc.X, npc.Y) <= NpcReachYards;

    private static QuestNpcLocation TurnInNpc(BatchQuest b) => b.Node.TurnIn ?? b.Node.Giver!;

    private static BatchQuest? Nearest(BotContext ctx, List<BatchQuest> set,
        Func<BatchQuest, (float X, float Y, int Map)> loc)
    {
        BatchQuest? best = null;
        float bestD = float.MaxValue;
        foreach (var b in set)
        {
            var p = loc(b);
            if (p.Map != ctx.MapId) continue;
            float d = Dist2(ctx.Pos.X, ctx.Pos.Y, p.X, p.Y);
            if (d < bestD) { bestD = d; best = b; }
        }
        return best;
    }

    private static void RefreshActiveIds(QuestScratch q)
    {
        q.ActiveQuestIds.Clear();
        foreach (var b in q.Batch) q.ActiveQuestIds.Add(b.QuestId);
    }

    private bool Synced(BotContext ctx)
        => (DateTime.UtcNow - ctx.QuestLogStampUtc).TotalSeconds <= ctx.TimeInStepSec;

    // ========================================================================
    // Command builders
    // ========================================================================
    private static StepResult MoveTo(QuestNpcLocation loc)
        => StepResult.Send(
            new BridgeCommand("MOVE_TO", new { mapId = loc.Map, x = loc.X, y = loc.Y, z = loc.Z }),
            "TASK_COMPLETE", TravelDeadline);

    // Dispatch one objective grind leg — shared by the NEAR sweep (phase 2) and the FAR lone-trek
    // (phase 4b). pick.Leg's coords come from UnmetLegs, which already resolves to the real spawn
    // in the quest's giver-scoped cluster nearest the bot (NearestSpawnPoint) — the same point
    // PriorityLeg scored this leg against. Dispatching pick.Leg AS-IS keeps the walk distance and
    // the scored distance identical by construction: no second coordinate-pick step to disagree
    // with the first. (Previously this re-randomized via Scatter() — a GLOBAL, unscoped per-entry
    // sample from CreatureSpawnLoader — which could send the bot to a different, possibly far,
    // spawn of the same creature elsewhere on the map: the score said "near", the walk wasn't.)
    // The §4 enriched MOVE_TO carries creature_entry/grind_radius/kill_count so C++ engages the
    // mob on approach and grinds in place; its single TASK_COMPLETE = this leg. Held (SelfSolo)
    // survives the goal bounce + the in-flight WAIT so the reconcile re-issues within one STATE
    // cycle if C++ drops the task.
    private StepResult DispatchObjectiveLeg(BotContext ctx, QuestScratch q, (BatchQuest Quest, GrindLeg Leg) pick)
    {
        q.Active = pick.Quest;
        q.ActiveSlot = 0;                               // legs aren't slot-routed (item legs live in ItemObjectives)
        ctx.SetStep("to_objective");
        var leg = pick.Leg;
        ctx.SetObjective(Objective.Grind(ObjectiveSource.SelfSolo,
            leg.CreatureEntry, leg.X, leg.Y, leg.Z,
            leg.Map, leg.Count > 0 ? leg.Count : 1,
            pick.Quest.QuestId, 0));
        return MoveToObjectiveLeg(leg);
    }

    // §4 enriched MOVE_TO for a grind leg (kill or creature-drop item). kill_count = remaining
    // (never 0 — 0 = an indefinite C++ grind that never acks). For an item leg the count is the
    // items still owed; an unlucky drop streak just re-derives another leg after the TASK_COMPLETE.
    // Both objective dispatch paths (normal sweep + overflow) route through here — UnmetLegs
    // already resolved the leg's coords to the real spawn nearest the bot (NearestSpawnPoint).
    //
    // alt_entry1/2/3 (2026-06-30, wolf-meat fix): the leg's tied item-drop siblings, if any
    // (GrindLeg.AltEntries — null/empty for every kill-objective leg). Always emitted, 0 for
    // unused slots — matches this wire's existing flat-key convention (every other optional
    // STATE/MOVE_TO field defaults to 0/absent rather than being conditionally omitted).
    // BridgeHandleMoveTo stashes them onto m_currentTask.altCreatureEntries[], which widens
    // what ScanApproachTarget / SelectGrindTarget / UpdateAI's kill-credit check treat as a
    // hit for this objective — so a tied local creature standing in the same field as the
    // dispatched one is no longer invisible to the bot.
    private static StepResult MoveToObjectiveLeg(GrindLeg leg)
    {
        int alt1 = leg.AltEntries != null && leg.AltEntries.Count > 0 ? leg.AltEntries[0] : 0;
        int alt2 = leg.AltEntries != null && leg.AltEntries.Count > 1 ? leg.AltEntries[1] : 0;
        int alt3 = leg.AltEntries != null && leg.AltEntries.Count > 2 ? leg.AltEntries[2] : 0;
        return StepResult.Send(
            new BridgeCommand("MOVE_TO", new
            {
                mapId = leg.Map,
                x = leg.X,
                y = leg.Y,
                z = leg.Z,
                creature_entry = leg.CreatureEntry,
                grind_radius = GrindRadius,
                kill_count = leg.Count > 0 ? leg.Count : 1,
                alt_entry1 = alt1,
                alt_entry2 = alt2,
                alt_entry3 = alt3
            }),
            "TASK_COMPLETE", TravelDeadline);   // IsObjectiveGrind => KILL-push tightens to 120s no-kill
    }

    // ========================================================================
    // Group execution (the god bot's shared objective)
    // ========================================================================
    // Self-contained: drive the ONE objective the god bot stamped on the whole team. Every member --
    // holder or helper -- runs the SAME indefinite grind at the shared coords (the combat directive
    // focus-fires the same mob), and the member's quest log is refreshed on a cadence so the
    // coordinator's "all eligible holders done" gate sees server truth. There is NO per-member count
    // here: shared group kill-credit advances every holder together, and the god bot clears the stamp
    // (objective satisfied for the team) -> we fall back to the solo batch, which turns in whatever
    // just completed. Deliberately no counted WAIT: a holder whose credit arrived from a teammate's
    // kill would never fire its OWN TASK_COMPLETE and would lock.
    // ── The §3 phase executor: branch on the god bot's stamped GroupOrder.Phase. ───────────────
    // Thin: every leg reuses the solo planner's primitives (MoveTo / AtNpc / the enriched grind /
    // QUEST_INTERACT). The COORDINATOR owns all gating + advancement; this only EXECUTES the current
    // phase for THIS bot, reading the bot's OWN log for its eligible/owed subset. v1 drives the
    // questing phases + HoldAtAnchor + the transient Forming. GroupVendor / GroupTrain are not stamped
    // by the v1 coordinator (group-maintenance is a follow-on); if ever seen they hit the default
    // (idle refresh), never deadlocking on an unhandled phase.
    private StepResult DriveGroup(BotContext ctx)
    {
        var o = ctx.GroupOrder;

        // A leg failed under the order -> drop it; the coordinator re-stamps / re-picks next tick (it
        // owns recovery). A dying member already peeled to Maintenance via the GoalSelector hard-need
        // before reaching here, so this is the benign "couldn't path / interact right now" case.
        if (ctx.Failure != null)
        {
            ctx.Failure = null;
            ctx.LastGroupOrder = GroupOrder.None;   // force a fresh leg next tick
            return StepResult.Wait();
        }

        // Keep the quest-log cache FRESH before ANY group phase reads it. The accept / turn-in gates
        // (NextAcceptableAtGiver here; NextGiver / NextEnder in the coordinator) key on ctx.QuestLog --
        // an in-memory cache refreshed only by QUEST_STATUS_ALL. Without a sync, a just-landed accept
        // never updates the cache, so the gates keep re-selecting an already-held quest -> the C++
        // CanTakeQuest-failed re-accept loop (and the same class would wedge TurnIn). GroupObjective
        // self-synced and the default (Forming) case synced; hoisting the SAME cadence here covers
        // every phase, so no group decision is ever made off a pre-accept snapshot.
        if ((DateTime.UtcNow - ctx.QuestLogStampUtc).TotalSeconds > GroupSyncSec)
            return StepResult.Fire(new BridgeCommand("QUERY_QUEST_STATUS"));

        switch (o.Phase)
        {
            case GroupPhase.TravelToGiver:
            case GroupPhase.TravelToTurnIn:
                return GroupTravel(ctx, o);
            case GroupPhase.Accept:
                return GroupAccept(ctx, o);
            case GroupPhase.Objective:
                return GroupObjective(ctx, o);
            case GroupPhase.TurnIn:
                return GroupTurnIn(ctx, o);
            case GroupPhase.HoldAtAnchor:
                return GroupHold(ctx, o);
            case GroupPhase.GroupTrain:
                return GroupTrainHold(ctx, o);
            default:
                // Forming (transient) or a phase this v1 executor doesn't drive: keep the log fresh on a
                // cadence so the coordinator's gates see server truth, then idle until it stamps a
                // concrete phase. Never a hard wait that could strand the bot.
                if ((DateTime.UtcNow - ctx.QuestLogStampUtc).TotalSeconds > GroupSyncSec)
                    return StepResult.Fire(new BridgeCommand("QUERY_QUEST_STATUS"));
                return StepResult.Wait();
        }
    }

    // Travel together to the stamped giver / ender. The coordinator flips the phase to Accept / TurnIn
    // once the whole group is within reach, so arrival needs no local detection beyond "am I there yet".
    private StepResult GroupTravel(BotContext ctx, GroupOrder o)
    {
        var npc = NpcOf(o);
        if (AtNpc(ctx, npc))
            return StepResult.Wait();          // arrived; the coordinator advances the phase next tick
        ctx.SetStep("grp_travel");
        return MoveTo(npc);
    }

    // In range, accept every quest this giver offers that we're eligible for and don't already hold
    // (§1 breadth -- fire for all we CAN hold; C++ bounces any it won't). One per tick (Send the ack);
    // the coordinator keeps the whole group in Accept until no eligible member still owes one here (§2).
    private StepResult GroupAccept(BotContext ctx, GroupOrder o)
    {
        var npc = NpcOf(o);
        if (!AtNpc(ctx, npc))
        {
            ctx.SetStep("grp_travel");
            return MoveTo(npc);                // close the last yards individually
        }
        if (NextAcceptableAtGiver(ctx, o.TargetNpcEntry) is int qid)
        {
            ctx.SetStep("grp_accept");
            return GroupInteract(qid, o.TargetNpcEntry, accept: true);
        }
        return StepResult.Wait();              // nothing left to accept here; wait for the team
    }

    // Grind the ONE shared objective the coordinator stamped (its embedded kill directive). Indefinite
    // enriched grind (sentinel count, no WAIT), (re)issued when the objective changes OR when we just
    // arrived from another phase (Step guard) -- a member already grinding the same mob stays quiet,
    // otherwise we (re)engage. The log is refreshed on a cadence so the coordinator's "all holders done"
    // gate sees server-credited kills (shared group credit advances counts with no local ack).
    private StepResult GroupObjective(BotContext ctx, GroupOrder o)
    {
        if ((DateTime.UtcNow - ctx.QuestLogStampUtc).TotalSeconds > GroupSyncSec)
            return StepResult.Fire(new BridgeCommand("QUERY_QUEST_STATUS"));
        if (o != ctx.LastGroupOrder || ctx.Step != "grp_obj")
        {
            ctx.SetStep("grp_obj");
            ctx.LastGroupOrder = o;
            return GroupObjectiveLeg(o.Objective);
        }
        return StepResult.Wait();
    }

    // In range, turn in every COMPLETE pool quest we hold at the stamped ender. One per tick; the
    // coordinator keeps the group in TurnIn until none holds a complete quest here.
    private StepResult GroupTurnIn(BotContext ctx, GroupOrder o)
    {
        var npc = NpcOf(o);
        if (!AtNpc(ctx, npc))
        {
            ctx.SetStep("grp_travel");
            return MoveTo(npc);
        }
        if (NextCompleteAtEnder(ctx, o.TargetNpcEntry) is int qid)
        {
            ctx.SetStep("grp_turnin");
            return GroupInteract(qid, o.TargetNpcEntry, accept: false);
        }
        return StepResult.Wait();
    }

    // A teammate is recovering: keep grinding the latched objective if there is one, else hold at the
    // anchor coords. (The recovering member itself peeled to Maintenance via the GoalSelector hard-need.)
    private StepResult GroupHold(BotContext ctx, GroupOrder o)
    {
        if (o.Objective.IsActive)
            return GroupObjective(ctx, o);     // grind the latched mob (change-guarded)
        var anchor = NpcOf(o);                 // TargetPos carries the anchor coords (NpcEntry 0)
        if (AtNpc(ctx, anchor))
            return StepResult.Wait();
        ctx.SetStep("grp_hold");
        return MoveTo(anchor);
    }

    // The group-gated training window (§4): THIS bot either has nothing new to learn, or already
    // peeled to Goal.Training via GoalSelector's groupTrainWindow carve-out (in which case DriveGroup
    // isn't even running for it -- QuestPlanner only runs under Goal.Questing). So a bot reaching this
    // case is a non-trainee waiting out the round. Unlike GroupHold there's no embedded anchor coord
    // (GroupOrder.Train carries no TargetPos -- trainees scatter to their OWN class trainers, there's
    // nothing to converge ON), so keep grinding the latched objective if one exists; otherwise just
    // sit -- moving to (0,0,0) off an unset TargetPos would be wrong.
    private StepResult GroupTrainHold(BotContext ctx, GroupOrder o)
    {
        if (o.Objective.IsActive)
            return GroupObjective(ctx, o);     // grind the latched mob (change-guarded) while trainees are away
        return StepResult.Wait();              // nothing latched yet and nowhere to converge — sit tight
    }

    // QUEST_INTERACT for the group path (no BatchQuest): accept / complete by quest id at the NPC.
    private static StepResult GroupInteract(int questId, int npcEntry, bool accept)
    {
        string action = accept ? "accept" : "complete";
        string expect = accept ? "QUEST_ACCEPT_ACK" : "QUEST_COMPLETE_ACK";
        return StepResult.Send(
            new BridgeCommand("QUEST_INTERACT", new { action, quest_id = questId, npc_entry = npcEntry }),
            expect, InteractDeadline);
    }

    // The next quest THIS bot can accept at the stamped giver (eligible + unheld + uncompleted). The
    // graph scan already excludes in-log / completed, so a returned id is genuinely acceptable here.
    private int? NextAcceptableAtGiver(BotContext ctx, int giverEntry)
    {
        var id = ctx.Identity;
        if (id == null) return null;
        int raceBit = QuestGraphLoader.RaceToBitmask(id.Race);
        int classBit = QuestGraphLoader.ClassToBitmask(id.ClassId);
        var active = new HashSet<int>(ctx.QuestLog.Keys);
        foreach (var q in _quests.GetAvailableQuests(raceBit, classBit, id.Level, id.CompletedQuestIds, active))
            if (q.Giver?.NpcEntry == giverEntry)
                return q.QuestId;
        return null;
    }

    // The next quest THIS bot holds at server-COMPLETE whose turn-in is the stamped ender.
    private int? NextCompleteAtEnder(BotContext ctx, int enderEntry)
    {
        foreach (var kv in ctx.QuestLog)
        {
            if (kv.Value.Status != QuestStatusComplete) continue;
            var q = _quests.GetQuest(kv.Key);
            var ender = q?.TurnIn ?? q?.Giver;
            if (ender != null && ender.NpcEntry == enderEntry)
                return kv.Key;
        }
        return null;
    }

    // Build a travel/interact target from the stamped order (TargetNpcEntry + TargetPos coords).
    private static QuestNpcLocation NpcOf(GroupOrder o) => new QuestNpcLocation
    {
        NpcEntry = o.TargetNpcEntry,
        X = o.TargetPos.X,
        Y = o.TargetPos.Y,
        Z = o.TargetPos.Z,
        Map = o.TargetPos.Map
    };

    // The shared-objective grind: enriched MOVE_TO (travel to the coords, then grind the creature in
    // place) with a sentinel kill_count never reached -> indefinite. Fire = no WAIT, so DriveGroup
    // keeps re-evaluating each tick (refresh cadence + change guard) and the unmatched "GRIND finished"
    // never lands. Completion is the COORDINATOR's server-count gate, not a local count.
    private static StepResult GroupObjectiveLeg(ExecDirective d)
        => StepResult.Fire(
            new BridgeCommand("MOVE_TO", new
            {
                mapId = d.Map,
                x = d.X,
                y = d.Y,
                z = d.Z,
                creature_entry = d.CreatureEntry,
                grind_radius = GrindRadius,
                kill_count = GroupGrindSentinel
            }));

    private static StepResult Interact(BatchQuest b, bool accept)
    {
        var npc = accept ? b.Node.Giver! : (b.Node.TurnIn ?? b.Node.Giver!);
        string action = accept
            ? (b.ForceMode ? "force_accept" : "accept")
            : (b.ForceMode ? "force_complete" : "complete");
        string expect = accept ? "QUEST_ACCEPT_ACK" : "QUEST_COMPLETE_ACK";
        return StepResult.Send(
            new BridgeCommand("QUEST_INTERACT", new { action, quest_id = b.QuestId, npc_entry = npc.NpcEntry }),
            expect, InteractDeadline);
    }

    private static float Dist2(float ax, float ay, float bx, float by)
    {
        float dx = ax - bx, dy = ay - by;
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}