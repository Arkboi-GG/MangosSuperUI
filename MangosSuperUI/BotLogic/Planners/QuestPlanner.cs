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
    private readonly ILogger<QuestPlanner> _logger;

    private static readonly TimeSpan TravelDeadline = TimeSpan.FromMinutes(8);    // continuation travel can be long (section 4.11); also the enriched-objective WAIT bound (travel + first kill) — the KILL-push then tightens it to 120s no-kill
    private static readonly TimeSpan InteractDeadline = TimeSpan.FromSeconds(20); // accept/turn-in acks are near-instant

    private const float GrindRadius = 60f;
    private const float ForceRadius = 150f;     // bot within this of a failed giver/turn-in => WMO last leg -> force_*
    private const int SafetyMargin = 3;         // level-gate = danger_level - margin
    private const int DeferMinutes = 15;
    private const int AbandonAfterDefers = 3;
    private const int QuestStatusComplete = 1;  // VMaNGOS QUEST_STATUS_COMPLETE
    private const double LogSyncCapSec = 3;     // wait this long for QUEST_STATUS_ALL before proceeding blind

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
    private const int BatchCap = 8;             // max quests carried at once (well under the 20-slot log cap)
    private const float GatherRescanYards = 50f;// re-gather for new local givers once moved this far mid-sweep
    private const float OutlierFactor = 2f;     // shelve a quest whose reach >= this x the mean reach of the others
    private const float NpcReachYards = 10f;    // close enough to interact without a fresh MOVE_TO (C++ searches 15yd)
    private const int MaxReachTier = 3;         // widening scan: 0 = local hub (baseline cap); each tier adds ~900yd (one hub-hop), bounded by ZoneSafetyMap's level-aware ceiling. A bot that has drained the local hub scans OUTWARD for the next level-appropriate hub instead of grinding in place.

    // -- Macro-loop exit (durable shelve + commit-to-grind) --
    // A hard MOVE failure on an accepted quest's objective bumps the SAME unified per-quest fail
    // streak as an attributed death (BotIdentity.QuestFailStreak, also written by MaintenancePlanner).
    // At QuestFailCap the quest is durably deferred for DurableDeferMinutes (the bot stops re-resuming
    // → re-failing it); below the cap it takes the shorter escalating sweep-defer (transient blips).
    // When the batch then exhausts WITH active deferrals, GrindLock the bot for GrindLockMinutes so it
    // grinds for levels instead of oscillating quest⇄grind at tick speed (the spin backoff).
    private const int QuestFailCap = 1;          // death + no_path share this cap (mirror MaintenancePlanner)
    private const int DurableDeferMinutes = 20;  // the at-cap durable shelve window
    private const int GrindLockMinutes = 20;     // commit-to-grind window on a deferral-driven batch exhaust

    public QuestPlanner(QuestGraphLoader quests, ILogger<QuestPlanner> logger)
    {
        _quests = quests;
        _logger = logger;
    }

    public Goal Handles => Goal.Questing;

    // ========================================================================
    // PlanNext -- apply the leg whose WAIT just cleared (read from ctx.Step),
    //            then derive the next action from the batch + quest-log state.
    // ========================================================================
    public StepResult PlanNext(BotContext ctx, BotStateSnapshot snap)
    {
        // A negated/expired WAIT surfaced a failure -> recover (batch-aware).
        if (ctx.Failure != null)
            return Recover(ctx);

        var q = ctx.Quest;

        // First entry -> new batch scratch, sync the log once. THROTTLED: only re-query
        // if the log cache is stale. A reselect that re-enters Questing within the sync
        // window reuses the fresh cache instead of flooding the bridge with QUERY every
        // tick (the June-16 spin: a wedged batch Block'd -> reselect -> re-enter -> QUERY).
        if (q == null)
        {
            ctx.Quest = q = new QuestScratch();
            ctx.SetStep("sync_log");
            if ((DateTime.UtcNow - ctx.QuestLogStampUtc).TotalSeconds > LogSyncCapSec)
                return StepResult.Fire(new BridgeCommand("QUERY_QUEST_STATUS"));
            BuildBatch(ctx, q);          // cache fresh -> build straight away, no re-query
            RefreshActiveIds(q);
            return Derive(ctx, q);
        }

        // Apply the completed leg (ctx.Step encodes which WAIT just cleared).
        switch (ctx.Step)
        {
            case "sync_log":
                {
                    if (!Synced(ctx) && ctx.TimeInStepSec < LogSyncCapSec)
                        return StepResult.Wait();
                    BuildBatch(ctx, q);   // resume in-log quests + seed/gather the local cluster
                    break;
                }
            case "obj_sync":
                {
                    if (!Synced(ctx) && ctx.TimeInStepSec < LogSyncCapSec)
                        return StepResult.Wait();
                    q.Active = null;      // fresh counts -> derive the next objective
                    break;
                }
            case "to_giver":
                if (q.Active != null) { ctx.SetStep("accept"); return Interact(q.Active, accept: true); }
                break;
            case "accept":
                if (q.Active != null) { q.Active.Accepted = true; q.Active = null; }
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
                ctx.SetStep("obj_sync");
                return StepResult.Fire(new BridgeCommand("QUERY_QUEST_STATUS"));
            case "grind_obj":
                // TASK_COMPLETE = kill_count reached. Re-sync so opportunistic credit on the
                // OTHER batched quests is seen, then derive the next objective / turn-in.
                ctx.SetStep("obj_sync");
                return StepResult.Fire(new BridgeCommand("QUERY_QUEST_STATUS"));
            case "to_turnin":
                if (q.Active != null) { ctx.SetStep("turnin"); return Interact(q.Active, accept: false); }
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
    // ========================================================================
    private StepResult Derive(BotContext ctx, QuestScratch q)
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

        // -- 2. OBJECTIVE sweep -- nearest unmet GRIND LEG (kill or creature-drop item), outliers shelved --
        var withObj = q.Batch.Where(b => b.Accepted && !b.TurnedIn && !b.Failed && HasUnmet(ctx, b)).ToList();
        var candidates = withObj.Where(b => !b.Deferred).ToList();
        if (candidates.Count > 0)
        {
            TagOutliers(ctx, candidates);                       // shelve far quests for this sweep
            var live = candidates.Where(b => !b.Deferred).ToList();
            var pick = NearestLeg(ctx, live);
            if (pick != null)
            {
                q.Active = pick.Value.Quest;
                q.ActiveSlot = 0;                               // legs aren't slot-routed (item legs live in ItemObjectives)
                ctx.SetStep("to_objective");
                // §4 enriched MOVE_TO: carry creature_entry/grind_radius/kill_count so C++ engages the
                // mob on approach and grinds in place (ScanApproachTarget → ConvertMoveToGrindInPlace),
                // never marching to / teleporting into the deep loader coord. One TASK_COMPLETE = the leg.
                return MoveToObjectiveLeg(pick.Value.Leg);
            }
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
                    return Derive(ctx, q);
                }

                if (id != null) id.QuestOverflowGrinds[bq.QuestId] = tries + 1;
                q.Active = bq;
                q.ActiveSlot = oPick.Value.Slot;
                ctx.SetStep("to_objective");
                _logger.LogInformation("[QUEST] {Name} overflow grind [{Id}] slot {Slot} (server still INCOMPLETE past our count, try {N}/{Max})",
                    ctx.Name, bq.QuestId, o.Slot, tries + 1, MaxOverflowGrinds);
                return MoveToObjective(o, OverflowChunk);
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
                q.Active = bq;
                var npc = TurnInNpc(bq);
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
        bool anyDeferred = q.Batch.Any(b => b.Deferred);
        if (added || anyDeferred)
        {
            foreach (var b in q.Batch) b.Deferred = false;
            return Derive(ctx, q);
        }

        // -- 5. nothing to accept / work / turn in / discover -> batch exhausted --
        // The carried set (incl. any Failed-this-sweep quests still in the C++ log) is resumed on
        // the next entry to Questing. If we got here BY SHELVING (there are active deferrals), the
        // in-reach content is all death/no_path-shelved — commit to grinding for a window so the bot
        // gains levels instead of oscillating quest⇄grind at tick speed (the spin backoff). A
        // genuinely quest-less bot (nothing deferred) skips the lock and grinds via normal
        // arbitration, so it still folds in a quest as it wanders into a fresh hub.
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

    // ========================================================================
    // Failure recovery (batch-aware): shelve the ONE quest, keep the batch.
    // ========================================================================
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
        foreach (var kv in ctx.QuestLog)
        {
            var node = _quests.GetQuest(kv.Key);
            if (node?.Giver == null) continue;
            if (id.CompletedQuestIds.Contains(node.QuestId)) continue;     // already rewarded
            if (id.AbandonedGreyQuestIds.Contains(node.QuestId)) continue; // greyed out
            if (id.DeferredQuestIds.ContainsKey(node.QuestId)) continue;   // R21: backing off (level/time defer) — don't re-resume into a churn
            if (!node.Objectives.All(o => o.IsCreature)) continue;                       // GO-interact objectives: phase 2
            if (!node.ItemObjectives.All(it => it.BestDropSource != null)) continue;     // GO-sourced/unresolved items: phase 2
            if (q.Batch.Any(b => b.QuestId == node.QuestId)) continue;
            q.Batch.Add(new BatchQuest { QuestId = node.QuestId, Node = node, Accepted = true });
        }

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
           && !id.DeferredQuestIds.ContainsKey(q.QuestId)
           && !id.AbandonedGreyQuestIds.Contains(q.QuestId)
           && !IsGrey(q, id.Level)                                       // grey-filter hole: GoalSelector's pick must agree with BuildBatch's grey-reject, else pick>0 / batch=0 → the quest⇄grind tick-spin
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

    // ========================================================================
    // Objective selection + the far-outlier rule
    // ========================================================================
    // A quest's "reach" = distance from the bot to its FARTHEST unmet kill objective
    // (it can't turn in until all are done, so the worst leg defines the commitment).
    private float QuestReach(BotContext ctx, BatchQuest b)
    {
        float worst = 0f;
        foreach (var leg in UnmetLegs(ctx, b))
        {
            if (leg.Map != ctx.MapId) return float.MaxValue;   // cross-map leg = maximally far
            float d = Dist2(ctx.Pos.X, ctx.Pos.Y, leg.X, leg.Y);
            if (d > worst) worst = d;
        }
        return worst;
    }

    // Shelve (Deferred = true) any candidate whose reach >= OutlierFactor x the MEAN reach
    // of the OTHERS -- so the far one doesn't inflate its own threshold. With <2 candidates,
    // or once only far ones remain (all ~equal), nothing is an outlier -> they get worked.
    private void TagOutliers(BotContext ctx, List<BatchQuest> candidates)
    {
        if (candidates.Count < 2) return;
        var reach = candidates.ToDictionary(b => b, b => QuestReach(ctx, b));
        foreach (var b in candidates)
        {
            var others = candidates.Where(o => o != b).Select(o => reach[o]).Where(r => r > 0f).ToList();
            if (others.Count == 0) continue;
            float mean = others.Average();
            if (reach[b] >= OutlierFactor * mean)
            {
                b.Deferred = true;
                _logger.LogDebug("[QUEST] {Name} shelving far quest [{Id}] reach={R:F0} (2x mean {M:F0})",
                    ctx.Name, b.QuestId, reach[b], mean);
            }
        }
    }

    // A drivable grind leg: kill CreatureEntry at (X,Y,Z) until Count is owed. Count is
    // kills-owed for a kill objective, or items-owed for a creature-sourced item objective
    // (routed to the item's best drop creature). The §4 enriched MOVE_TO is identical for
    // both — C++ grinds the entry and auto-loots; the server credits kills AND drops.
    private readonly record struct GrindLeg(int CreatureEntry, float X, float Y, float Z, int Map, int Count);

    // The unmet grind legs of a quest THIS tick: one per still-short kill objective + one per
    // still-short creature-sourced item objective. GO-interact objectives and GO-sourced items
    // are phase 2 (not emitted → not driven here).
    private IEnumerable<GrindLeg> UnmetLegs(BotContext ctx, BatchQuest b)
    {
        foreach (var o in b.Node.Objectives)
        {
            if (!o.IsCreature || o.Count <= 0) continue;
            int rem = RawRemaining(ctx, b.QuestId, o);
            if (rem <= 0) continue;
            yield return new GrindLeg(o.CreatureEntry, o.GrindX, o.GrindY, o.GrindZ, o.GrindMap, rem);
        }
        foreach (var it in b.Node.ItemObjectives)
        {
            if (it.Count <= 0) continue;
            int rem = RawItemRemaining(ctx, b.QuestId, it);
            if (rem <= 0) continue;
            var src = it.BestDropSource;                       // creature-sourced only (GO-sourced = phase 2)
            if (src == null || src.SpawnCount <= 0) continue;
            yield return new GrindLeg(src.CreatureEntry, src.GrindX, src.GrindY, src.GrindZ, src.GrindMap, rem);
        }
    }

    // Nearest same-map unmet leg across the live batch.
    private (BatchQuest Quest, GrindLeg Leg)? NearestLeg(BotContext ctx, List<BatchQuest> live)
    {
        (BatchQuest, GrindLeg)? best = null;
        float bestD = float.MaxValue;
        foreach (var b in live)
            foreach (var leg in UnmetLegs(ctx, b))
            {
                if (leg.Map != ctx.MapId) continue;
                float d = Dist2(ctx.Pos.X, ctx.Pos.Y, leg.X, leg.Y);
                if (d < bestD) { bestD = d; best = (b, leg); }
            }
        return best;
    }

    // Overflow target: the nearest same-map creature slot among quests the server still calls
    // INCOMPLETE despite our local counts being met. Unlike the §2 leg picker it does NOT gate on
    // RawRemaining > 0 (by definition all slots are already model-met here) — we re-grind to push
    // the server's credit past our stale count.
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
                float d = Dist2(ctx.Pos.X, ctx.Pos.Y, o.GrindX, o.GrindY);
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

    // §4 enriched objective: ONE MOVE_TO carries the kill target (creature_entry/grind_radius/
    // kill_count). C++ scans for the mob during the approach and hands off to GRIND in place,
    // re-centering on the bot — at the cave mouth on a seam, mid-approach on a scan hit, or at
    // the dest on auto-arrive — never the bare "arrived (seam crossed)" teleport (that path is
    // creature_entry==0 only). Its single TASK_COMPLETE ("GRIND finished" at kill_count) = this
    // objective done. The one WAIT is the backpressure (no re-issue under it); the executor flags
    // it IsObjectiveGrind, so the KILL-push rolls the deadline to now+120s on each kill — it fails
    // only on a 120s no-kill gap once grinding, or the travel ceiling before the first kill. A
    // genuinely all-deep-past-the-50yd-mouth objective therefore shelves at the deadline (bounded),
    // it never loops. kill_count is the remaining count (>=1; 0 = an indefinite C++ grind that never acks).
    private static StepResult MoveToObjective(QuestObjective obj, int killCount)
        => StepResult.Send(
            new BridgeCommand("MOVE_TO", new
            {
                mapId = obj.GrindMap,
                x = obj.GrindX,
                y = obj.GrindY,
                z = obj.GrindZ,
                creature_entry = obj.CreatureEntry,
                grind_radius = GrindRadius,
                kill_count = killCount
            }),
            "TASK_COMPLETE", TravelDeadline);   // IsObjectiveGrind => KILL-push tightens to 120s no-kill

    // §4 enriched MOVE_TO for a grind leg (kill or creature-drop item). kill_count = remaining
    // (never 0 — 0 = an indefinite C++ grind that never acks). For an item leg the count is the
    // items still owed; an unlucky drop streak just re-derives another leg after the TASK_COMPLETE.
    private static StepResult MoveToObjectiveLeg(GrindLeg leg)
        => StepResult.Send(
            new BridgeCommand("MOVE_TO", new
            {
                mapId = leg.Map,
                x = leg.X,
                y = leg.Y,
                z = leg.Z,
                creature_entry = leg.CreatureEntry,
                grind_radius = GrindRadius,
                kill_count = leg.Count > 0 ? leg.Count : 1
            }),
            "TASK_COMPLETE", TravelDeadline);   // IsObjectiveGrind => KILL-push tightens to 120s no-kill

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