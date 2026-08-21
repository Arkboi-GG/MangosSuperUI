namespace MangosSuperUI.BotLogic.Core;

/// <summary>
/// The single object that represents everything about one bot.
/// Domains never store per-bot state — they read/write BotIdentity.
/// </summary>
public class BotIdentity
{
    // --- Immutable (set at spawn/load) ---
    public int Guid { get; set; }
    public string Name { get; set; } = "";
    public int Race { get; set; }
    public int ClassId { get; set; }
    public string Faction { get; set; } = "";
    public BotPersonality Personality { get; set; } = new();

    // --- Mutable (updated by events/state messages) ---
    public int Level { get; set; } = 1;
    public long XP { get; set; }
    public long XPToNextLevel { get; set; }
    public long CopperBalance { get; set; }

    public float? CorpseX { get; set; }
    public float? CorpseY { get; set; }
    public float? CorpseZ { get; set; }
    public int? CorpseMapId { get; set; }


    // --- Activity tracking ---
    public ActivityState CurrentActivity { get; set; } = new();
    public ActivityState? PreviousActivity { get; set; }
    public DateTime NextDecisionTick { get; set; } = DateTime.UtcNow;

    // --- Strategic tick (DecisionEngine split-cadence) ---
    /// <summary>
    /// When the next strategic re-evaluation should fire. Tactical ticks (sub-phase
    /// driving) continue on the normal 10-30s cadence. Strategic evals (should I
    /// switch activities?) fire on a 3-10 minute cadence, personality-modulated.
    /// </summary>
    public DateTime NextStrategicEval { get; set; } = DateTime.UtcNow;

    // --- Quest tracking ---
    public int? ActiveQuestId { get; set; }
    public string? ActiveQuestPhase { get; set; }
    public float CurrentQuestProgress { get; set; }
    public HashSet<int> CompletedQuestIds { get; set; } = new();

    /// <summary>
    /// Quests abandoned because they went GREY (out-leveled — the vanilla gray
    /// level formula on the quest's level). Distance never drops a quest and danger
    /// never drops a quest; greying is the ONLY drop (batching policy). Unlike a
    /// deferral this NEVER clears — the bot only levels up, so a grey quest stays
    /// grey. Excluded from picks (QuestPlanner.IsPickable) and never resumed into a
    /// batch. Survives reconnect like CompletedQuestIds.
    /// </summary>
    public HashSet<int> AbandonedGreyQuestIds { get; set; } = new();

    /// <summary>
    /// Quests hydrated from character_queststatus on reconnect that are still
    /// in the bot's quest log (accepted but not yet rewarded). QuestingDomain.OnEnter
    /// consumes this to rebuild the ActiveQuestEntry batch, then clears it.
    /// Carries DB progress columns so we can verify whether objectives are truly done.
    /// </summary>
    public List<HydratedQuest>? HydratedActiveQuests { get; set; }
    // --- Quest objective progress (per-slot tracking for active quest) ---
    public Dictionary<int, int> QuestObjectiveProgress { get; set; } = new(); // slot → current count
    public Dictionary<int, int> QuestItemProgress { get; set; } = new();      // itemId → current count

    // --- Quest deferral (quests the bot tried and failed/died doing) ---
    /// <summary>
    /// Quests the bot has shelved because it died, got stuck, or PATH_UNSAFE'd.
    /// Key = questId, Value = deferral info with optional level gate.
    /// Time-gated deferrals (death, stuck) expire after 10-30 min.
    /// Level-gated deferrals (PATH_UNSAFE) expire when bot reaches the safe level.
    /// </summary>
    public Dictionary<int, QuestDeferral> DeferredQuestIds { get; set; } = new();

    /// <summary>
    /// Session 33: Cumulative deferral count per quest (survives across deferral expiries).
    /// When a quest gets deferred 3+ times AND it's not part of a chain AND has no
    /// item rewards, the bot will abandon it to free up the quest log slot.
    /// Key = questId, Value = total times deferred this session.
    /// Cleared on level-up (fresh start, new capabilities).
    /// </summary>
    public Dictionary<int, int> QuestDeferralCounts { get; set; } = new();

    /// <summary>
    /// Overflow-grind attempts per quest. The server still reports a kill quest INCOMPLETE
    /// (status != 3) even though our local QuestNode counts are all met — our requirement is
    /// stale/under (a quest_template patch override the graph loaded at patch=0 doesn't carry)
    /// or the quest has an objective our graph doesn't model. QuestPlanner keeps killing past
    /// our count so the server can credit it, BOUNDED by this counter — past the cap it durably
    /// defers the quest instead of grinding forever. Cleared on turn-in / grey-abandon.
    /// Key = questId, Value = overflow grinds issued since the last reset.
    /// </summary>
    public Dictionary<int, int> QuestOverflowGrinds { get; set; } = new();

    /// <summary>
    /// Per-quest cumulative FAILURE streak driving the durable death/no_path shelve (the
    /// macro-loop exit). Bumped by an attributed DEATH (MaintenancePlanner, via
    /// BotContext.DeathBlameQuestId) and by a hard MOVE failure on the quest's objective
    /// (QuestPlanner.DeferAcceptedQuest). At QuestFailCap the quest is durably deferred (~60 min)
    /// and the streak is cleared, so the bot stops walking back into the kill / re-resuming an
    /// unreachable quest. Also cleared on turn-in and grey-abandon. Transient (in-memory).
    /// Key = questId, Value = failures attributed since the last clear.
    /// </summary>
    public Dictionary<int, int> QuestFailStreak { get; set; } = new();

    // --- Path blacklist (destinations rejected by C++ IsPathSafe) ---
    /// <summary>
    /// Destinations that C++ PATH_UNSAFE rejected because the mmap path crossed
    /// through high-level creature spawns. Key = (destX, destY) rounded to int
    /// for coarse matching. Value = dangerLevel the path hit.
    /// Expires ONLY when bot reaches dangerLevel - 3 (can handle those creatures).
    /// No time expiry — a level 1 bot should never retry a path through level 6 wolves
    /// every 30 minutes. It should wait until it's strong enough.
    /// </summary>
    public Dictionary<(int X, int Y), int> PathBlacklist { get; set; } = new();

    /// <summary>
    /// Count of PATH_UNSAFE events received since last quest pick or activity change.
    /// Used to detect "everything is blacklisted" and fall back to grinding.
    /// </summary>
    public int PathUnsafeCountSinceLastPick { get; set; }

    /// <summary>
    /// Durable per-destination consecutive no_path count (2026-07-03, the GroupVendor livelock fix).
    /// Key = map plus destination rounded to the nearest yard (same granularity as PathBlacklist). Distinct from
    /// BotContext.ConsecutiveFailures (resets to 0 on every BotBrain.TryBreakWedgeAsync trip) and from
    /// PathBlacklist (a level-gated danger veto, not a reachability fact) — this is durable memory that
    /// "MOVE_TO to THIS coordinate has failed with reason=no_path N times in a row," surviving the wedge
    /// park so a genuinely unreachable leg (a real navmesh graph disconnection C++ cannot self-heal —
    /// confirmed live 2026-07-03) is eventually recognized instead of retried at ~1Hz forever. Bumped by
    /// BotExecutor.OnEvent on each no_path MOVE_FAILED; consulted by the group-leg
    /// quarantine and cleared after a real arrival or an order change.
    /// </summary>
    public Dictionary<(int Map, int X, int Y), int> NoPathStreak { get; set; } = new();
    private readonly object _noPathStreakLock = new();

    // --- Death tracking (for reactive quest shelving) ---
    /// <summary>Number of deaths since the bot last changed quests or activities.</summary>
    public int DeathsSinceQuestStart { get; set; }
    /// <summary>Where the bot last died (for "death zone" detection).</summary>
    public (float X, float Y, int Map) LastDeathLocation { get; set; }
    /// <summary>UTC time of last death.</summary>
    public DateTime LastDeathTime { get; set; }

    /// <summary>
    /// Consecutive same-spot deaths (each death within DeathLoopRadius of the previous one).
    /// Drives the ESCALATING relocate offset in MaintenancePlanner — each loop death pushes
    /// the post-rez relocate further out so a bot buried in a pack eventually clears aggro.
    /// Bumped on a loop death, reset to 0 on a death somewhere new (the last relocate worked)
    /// or on ResetDeathCounter (picked a quest / changed activity). A live-run knob.
    /// </summary>
    public int DeathLoopStreak { get; set; }

    // --- Spell/training tracking ---
    public HashSet<int> KnownSpellIds { get; set; } = new();
    public bool HasUnlearnedSpells { get; set; }
    public int TicksSinceLastTrained { get; set; }
    public uint TrainingCostNeeded { get; set; } = 0;     // Session 45: full training bill owed at last trainer visit
    public float GroupAnchorRadius { get; set; } = 10f;   // Session 45: grind radius for the current errand anchor

    // --- Shadow inventory (in-memory, flushed to DB periodically) ---
    public List<ShadowInventoryItem> ShadowInventory { get; set; } = new();

    // --- PendingAction (cross-domain return stack) ---
    /// <summary>
    /// Saved domain state for cross-domain interruptions (e.g., bags-full during quest turn-in).
    /// DecisionEngine checks this before strategic rolls and forces return to the saved domain.
    /// Set by QuestingDomain on QUEST_INTERACT_FAIL with bags-full, cleared by DecisionEngine on return.
    /// </summary>
    public PendingAction? PendingAction { get; set; }

    // --- EconomyDomain transient vendoring state (not persisted) ---
    public int? VendorNpcEntry { get; set; }
    public float VendorX { get; set; }
    public float VendorY { get; set; }
    public float VendorZ { get; set; }
    public int VendorMapId { get; set; }
    public DateTime? VendorTravelStarted { get; set; }

    // --- Relationships (future) ---
    public HashSet<int> MetPlayerGuids { get; set; } = new();

    // --- Group membership (Session 31 → Session 35 "Band of Brothers" rework) ---
    // Stamped by GroupManager.EnrichBotIdentity.
    //
    // Every grouped bot is a fully autonomous quester. The "pace-setter" is the one
    // who decides WHICH quests the group works on. All members accept, grind, and
    // turn in independently. The group synchronizes at two gates:
    //   1. DoingObjectives → TravelingToTurnIn: wait until ALL members finished objectives
    //   2. BatchComplete → PickingQuests: wait until ALL members turned in current batch
    // Members can still vendor/train/eat independently — the group waits for them.

    /// <summary>Group ID this bot belongs to, or null if solo.</summary>
    public int? GroupId { get; set; }
    /// <summary>Pace-setter's GUID. Equals this bot's Guid if pace-setter. Null if solo.</summary>
    public int? GroupLeaderGuid { get; set; }
    /// <summary>True if in a group AND is the pace-setter (picks quests for the group).</summary>
    public bool IsGroupLeader => GroupId.HasValue && GroupLeaderGuid == Guid;
    /// <summary>True if in a group AND is NOT the pace-setter.</summary>
    public bool IsGroupFollower => GroupId.HasValue && GroupLeaderGuid.HasValue && GroupLeaderGuid != Guid;
    /// <summary>True if in any group (leader or member).</summary>
    public bool IsGrouped => GroupId.HasValue;

    /// <summary>
    /// Lowest level among all group members (including pace-setter).
    /// Pace-setter uses this for quest selection so everyone can accept.
    /// Stamped by BotBrainService during group coordination injection. Null if solo.
    /// </summary>
    public int? GroupMinMemberLevel { get; set; }

    // ── §3.5 (grouping rebuild): the GroupAll* boolean gate-flags were REMOVED here. ──
    // GroupAllObjectivesDone / GroupAllMembersTurnedIn / GroupAllMembersQuesting were the
    // exact miscountable stored-flag shape that deadlocked the old leader -- a stored bool
    // drifting from reality (Grinding miscounted as not-questing). The new GroupCoordinator
    // computes every gate as a LIVE POLL over ground-truth member state each tick, with a
    // timeout + a liveness escape -- never a flag (§3.5). Nothing reads these now; their
    // stamper, the old BotBrainService god-brain, was removed in the rebuild.

    // ── Session 42: GroupCoordinator directive (ARCH §7a) ──
    // Computed once per group per decision pass in BotBrainService and stamped
    // on every member. DecisionEngine ENFORCES it at strategic eval; domains
    // EXECUTE it (CombatDomain grinds at the anchor; QuestingDomain adopts the
    // leader's quests). Domains never DECIDE it — that is the "each domain its
    // own mind" trap this layer exists to kill.

    /// <summary>What the group should be doing right now. None = solo / coordinator off.</summary>
    public GroupDirective GroupDirective { get; set; } = GroupDirective.None;
    /// <summary>Group anchor (= leader's live position). Where HoldAndGrind/Regroup converge.</summary>
    public float GroupAnchorX { get; set; }
    public float GroupAnchorY { get; set; }
    public float GroupAnchorZ { get; set; }
    public int GroupAnchorMap { get; set; }
    /// <summary>UTC stamp of the last directive computation. Consumers treat a directive
    /// older than ~2 min as None (disband/restart staleness guard).</summary>
    public DateTime GroupDirectiveUtc { get; set; }
    /// <summary>Session 42 (semantics reworked Session 44): the group's quest BATCH,
    /// stamped with the directive. S42 stamped the leader's live (not-turned-in) IDs,
    /// which bricked any follower that missed an accept window: the moment the leader
    /// turned in a chain head (783), every downstream quest failed CanTakeQuest
    /// PrevQ=0 forever. S44: the coordinator stamps the per-group BATCH SET instead —
    /// every quest the leader accepted this run, minus the ones THIS member has
    /// already turned in or given up on. A quest leaves the batch only when every
    /// member rewarded it (or gave up: class/race-locked) or the batch TTL expires.
    /// Followers restrict picking/opportunistic accepts to this set.</summary>
    public HashSet<int>? GroupLeaderQuestIds { get; set; }

    // ── Session 44: formation quest-sync leader holds ──
    // Computed by the BotBrainService coordinator on the LEADER's pass only,
    // from the group quest batch + live member ack state. QuestingDomain's
    // AcceptingQuests/TurningIn exit points consume these: the leader waits
    // (timeout-capped, see GROUP_HOLD_TIMEOUT_SEC) at the NPC instead of
    // sprinting off while glued followers are still mid-accept/turn-in.

    /// <summary>Leader-only. True while some Following member still needs to ACCEPT
    /// a batch quest whose giver is near the leader's current position.</summary>
    public bool GroupFollowersNeedAccept { get; set; }
    /// <summary>Leader-only. True while some Following member that holds a batch quest
    /// the leader already rewarded still needs to TURN IT IN at a nearby ender.</summary>
    public bool GroupFollowersNeedTurnIn { get; set; }

    // --- Computed helpers ---
    public float XPPercent => XPToNextLevel > 0 ? XP / (float)XPToNextLevel : 0f;
    public bool IsNearLevelUp => XPPercent > 0.85f;
    public string RaceClassName => $"{(WowRace)Race} {(WowClass)ClassId}";

    public DateTime? VendorCooldownUntil { get; set; }

    /// <summary>
    /// Suppress Questing until this UTC time. The bot has shelved its way out of all in-reach
    /// content (everything currently deferred), so instead of oscillating quest⇄grind at tick
    /// speed it COMMITS to grinding for a fixed window to actually gain levels — the returning
    /// 60-min defers then bring that content back when the bot is a level or two stronger. Set by
    /// QuestPlanner when the batch exhausts WITH active deferrals; honored by GoalSelector (which
    /// still lets death/heal/vendor recovery preempt). Expires by clock. Null = no lock.
    /// </summary>
    public DateTime? GrindLockUntil { get; set; }

    /// <summary>
    /// Spacing between grind-lock EARLY-RELEASE attempts (FINDING_003 fix hardening). GoalSelector's
    /// release check models less than BuildBatch's dispatch gates, so a "workable" quest can insta-fail
    /// and re-lock — without spacing that loops at tick speed (release⇄re-lock livelock, 2026-08-08).
    /// Set on each release; the next release attempt waits for this to lapse. Expires by clock.
    /// Null = no attempt yet / free to release.
    /// </summary>
    public DateTime? GrindLockReleaseCooldownUntil { get; set; }

    /// <summary>
    /// Set by the brain's no-progress circuit breaker (BotBrain.TryBreakWedgeAsync) when a bot has made
    /// zero real progress for too long OR is in a fast fail-loop (e.g. relocate MOVE_FAILED no_path at
    /// 1Hz, the off-mesh case). GoalSelector returns Idle while this is in the future so the bot PARKS
    /// instead of thrashing; on lapse it resumes and relocates to a fresh cell. Recovery (dead/heal/
    /// vendor) still preempts. Expires by clock. Null = not parked.
    /// </summary>
    public DateTime? WedgeBackoffUntil { get; set; }

    /// <summary>
    /// Consecutive wedge-breaker trips with no real kill in between (FINDING_010). The breaker's
    /// park→local-relocate ladder moves a bot ~50yd; a STRANDED bot (no killable content, no
    /// dispatchable quest — Everlook L18, Badlands L21) cycles it forever. At the cap the brain
    /// escalates to a PORT_HOME escape (racial start) instead of another local shuffle. Cleared by
    /// a real kill (BotContext.OnGrindProgress) and on the escape itself.
    /// </summary>
    public int WedgeStreak { get; set; }

    /// <summary>
    /// [FINDING_020] Consecutive MOVE_FAILED events tagged start_isolated=1 by the core from the same
    /// ~10yd spot (BotExecutor.OnEvent). The bot's own start is a navmesh island / WMO pocket / water
    /// and — post-FINDING_011 (no straight-line shortcut) — it has no move that can succeed. At
    /// BotBrain.IslandEscapeCap the brain ports it to its level-band home (TryEscapeIslandAsync).
    /// Reset when a failure is NOT isolated, when the bot has moved >10yd, and on the escape.
    /// </summary>
    public int IslandStreak { get; set; }
    public float IslandStreakX { get; set; }
    public float IslandStreakY { get; set; }
    /// <summary>[FINDING_020] Don't re-port for this long after an island escape (lets the bot walk
    /// away from home before the next verdict; bounds the worst-case port churn).</summary>
    public DateTime? IslandEscapeCooldownUntil { get; set; }

    /// <summary>[FINDING_020 round 4] Rotates the alt-town escape pick each time a stuck bot is
    /// ported, so a bot that re-sticks at one town goes to a different one next time (diffusion,
    /// not a re-pile). Bumped on every escape port.</summary>
    public int EscapeRotation { get; set; }

    /// <summary>[STUCK-STILL 2026-08-21] Ground-truth physical-stuck detector, INDEPENDENT of the
    /// outcome-based wedge/streak machinery. Anchor = where the bot was when it last physically moved
    /// more than the still-radius; StillSinceUtc = when that was. If it stays within the radius of the
    /// anchor for the still-window (alive/solo/out-of-combat), it is physically stuck — a walking,
    /// questing or grinding bot always moves — and BotBrain ejects it to a friendly hub on the FIRST
    /// window, no wedge streak. default(DateTime) = not seeded yet (first sight seeds it).</summary>
    public float StillAnchorX { get; set; }
    public float StillAnchorY { get; set; }
    public DateTime StillSinceUtc { get; set; }

    /// <summary>
    /// Suppress the Training goal until this UTC time. Set by TrainingPlanner on a give-up
    /// (trainer unreachable / TRAIN_FAIL / timeout) so a bot doesn't immediately re-trek toward
    /// the same unreachable trainer. Cleared on LEVEL_UP (new spells justify a fresh attempt) and
    /// lapses by clock. Null = no cooldown.
    /// </summary>
    public DateTime? TrainCooldownUntil { get; set; }

    // --- Grind relocation (aware-grind: leave a barren/grey/contested spot for a level-appropriate cell) ---
    // Durable HERE (not on GrindScratch) because the no-kills reselect bounces through Idle and
    // ResetScratch wipes ctx.Grind every cycle. GrindPlanner.IsProgressing returns true while
    // GrindRelocating so the move advances tick-to-tick without an Idle bounce mid-flight.
    public bool GrindRelocating { get; set; }
    public bool GrindRelocatePatrolStopped { get; set; }   // SET_TASK IDLE sent for this relocate
    public bool GrindRelocateMoveIssued { get; set; }      // MOVE_TO issued for this relocate
    public float GrindRelocateX { get; set; }
    public float GrindRelocateY { get; set; }
    public float GrindRelocateZ { get; set; }

    /// <summary>One relocate ATTEMPT per window (FINDING_003 residual fix). Deliberately NOT
    /// cleared by ClearGrindRelocate — a failed/aborted relocate must not re-arm at tick speed
    /// (the FINDING_009 lesson); the bot grinds in place until the cooldown lapses.</summary>
    public DateTime? GrindRelocateCooldownUntil { get; set; }

    /// <summary>
    /// After C++ explicitly reports that a filler grind has no target, the brain may world-port to
    /// a data-backed camp on the other continent. Bound retries so a bad landing/data cell cannot
    /// produce a cross-continent ping-pong; GrindHubJumpRotation changes the selected top-ranked camp.
    /// </summary>
    public DateTime? GrindHubJumpCooldownUntil { get; set; }
    public int GrindHubJumpRotation { get; set; }

    /// <summary>Clear all grind-relocation phase flags (relocate finished or aborted).</summary>
    public void ClearGrindRelocate()
    {
        GrindRelocating = false;
        GrindRelocatePatrolStopped = false;
        GrindRelocateMoveIssued = false;
    }




    /// <summary>
    /// Per-bot causal story emitter (BotStoryRider). Carried here so any code path
    /// holding the bot can emit its story without threading a ref through every
    /// signature. Created in BotBrainService.InitializeBotAsync. Nullable + invoked
    /// null-conditionally at call sites (bot.Story?.Intent(...)) so it is a safe
    /// no-op before init or when absent. Passive: read + emit only, never alters flow.
    /// </summary>
    public Tracking.BotStoryRider? Story { get; set; }
    /// <summary>
    /// Clear expired deferrals. Called during quest selection.
    /// Time-gated deferrals expire by clock. Level-gated deferrals expire
    /// when bot reaches the required level (dangerLevel - SAFETY_MARGIN).
    /// </summary>
    public void PruneExpiredDeferrals()
    {
        var now = DateTime.UtcNow;
        var expired = DeferredQuestIds
            .Where(kv => kv.Value.IsExpired(now, Level))
            .Select(kv => kv.Key)
            .ToList();
        foreach (var id in expired)
            DeferredQuestIds.Remove(id);
    }

    /// <summary>
    /// Defer a quest for a duration (time-gated). Used for death/stuck deferrals.
    /// Session 33: Also increments the cumulative deferral count for frustration tracking.
    /// </summary>
    public void DeferQuest(int questId, TimeSpan duration)
    {
        DeferredQuestIds[questId] = QuestDeferral.TimeBased(DateTime.UtcNow + duration);
        QuestDeferralCounts.TryGetValue(questId, out int count);
        QuestDeferralCounts[questId] = count + 1;
    }

    /// <summary>
    /// Defer a quest until bot reaches a safe level (level-gated). Used for PATH_UNSAFE.
    /// Bot won't retry until it's within SAFETY_MARGIN levels of the danger.
    /// </summary>
    public void DeferQuestUntilLevel(int questId, int dangerLevel, int safetyMargin = 3)
    {
        int requiredLevel = Math.Max(1, dangerLevel - safetyMargin);
        DeferredQuestIds[questId] = QuestDeferral.LevelBased(requiredLevel);
    }

    /// <summary>
    /// Drop a quest because it went grey (out-leveled). Adds it to the permanent
    /// skip set and clears any stale deferral bookkeeping so it can't be re-picked.
    /// The QuestPlanner emits ABANDON_QUEST to C++ when the quest was accepted.
    /// </summary>
    public void AbandonGrey(int questId)
    {
        AbandonedGreyQuestIds.Add(questId);
        DeferredQuestIds.Remove(questId);
        QuestDeferralCounts.Remove(questId);
        QuestOverflowGrinds.Remove(questId);
    }

    /// <summary>
    /// Prune deferrals on level-up. Only clears deferrals the bot has outleveled.
    /// Time-based deferrals are left alone (they expire by clock).
    /// Level-based deferrals clear when bot reaches the required level.
    /// Session 33: Also clears deferral counts — fresh level = fresh start.
    /// </summary>
    public void ClearAllDeferrals()
    {
        PruneExpiredDeferrals();
        QuestDeferralCounts.Clear();
    }

    /// <summary>
    /// Blacklist a destination that C++ rejected via PATH_UNSAFE.
    /// Rounds coords to int for coarse matching (~1yd granularity).
    /// Persists until bot can handle the danger level (dangerLevel - 3).
    /// </summary>
    public void AddPathBlacklist(float destX, float destY, int dangerLevel)
    {
        var key = ((int)MathF.Round(destX), (int)MathF.Round(destY));
        // Keep the higher danger level if already blacklisted
        if (PathBlacklist.TryGetValue(key, out int existing) && existing >= dangerLevel)
            return;
        PathBlacklist[key] = dangerLevel;
        PathUnsafeCountSinceLastPick++;
    }

    /// <summary>
    /// Check if a coordinate is blacklisted. Uses ±20yd tolerance to catch
    /// jittered MOVE_TO variants of the same destination.
    /// Only clears when bot has leveled past dangerLevel - 3.
    /// </summary>
    public bool IsPathBlacklisted(float x, float y)
    {
        int ix = (int)MathF.Round(x);
        int iy = (int)MathF.Round(y);

        foreach (var kvp in PathBlacklist)
        {
            // Bot has leveled past the danger — can handle it now
            if (Level >= kvp.Value - 3) continue;

            int dx = Math.Abs(kvp.Key.X - ix);
            int dy = Math.Abs(kvp.Key.Y - iy);
            if (dx <= 20 && dy <= 20) return true;
        }

        return false;
    }

    /// <summary>
    /// Remove level-obsolete blacklist entries. Called during quest selection.
    /// Entry clears when botLevel >= dangerLevel - 3.
    /// </summary>
    public void PrunePathBlacklist()
    {
        var expired = PathBlacklist
            .Where(kvp => Level >= kvp.Value - 3)
            .Select(kvp => kvp.Key)
            .ToList();
        foreach (var key in expired)
            PathBlacklist.Remove(key);
    }

    /// <summary>
    /// Prune path blacklist on level-up. Does NOT clear everything — only removes
    /// entries the bot has now outleveled. A level 1→2 bot shouldn't clear the
    /// level 6 wolf blacklist. That clears at level 3 (dangerLevel 6 - 3).
    /// </summary>
    public void ClearPathBlacklist()
    {
        PrunePathBlacklist();
        PathUnsafeCountSinceLastPick = 0;
    }

    /// <summary>Bump (or seed) the no_path streak for a destination. Returns the new count.</summary>
    public int RecordNoPath(int map, float destX, float destY)
    {
        var key = (map, (int)MathF.Round(destX), (int)MathF.Round(destY));
        lock (_noPathStreakLock)
        {
            NoPathStreak.TryGetValue(key, out int count);
            count++;
            NoPathStreak[key] = count;
            return count;
        }
    }

    /// <summary>Current no_path streak for a destination (0 = never failed / cleared).</summary>
    public int GetNoPathStreak(int map, float destX, float destY)
    {
        var key = (map, (int)MathF.Round(destX), (int)MathF.Round(destY));
        lock (_noPathStreakLock)
            return NoPathStreak.TryGetValue(key, out int count) ? count : 0;
    }

    /// <summary>Clear the no_path streak for a destination after a successful arrival or when the
    /// quarantined group order changes and the blocked leg is no longer current.</summary>
    public void ClearNoPathStreak(int map, float destX, float destY)
    {
        var key = (map, (int)MathF.Round(destX), (int)MathF.Round(destY));
        lock (_noPathStreakLock)
            NoPathStreak.Remove(key);
    }

    /// <summary>
    /// Record a death for quest-shelving logic.
    /// </summary>
    public void RecordDeath(float x, float y, int map)
    {
        DeathsSinceQuestStart++;
        LastDeathLocation = (x, y, map);
        LastDeathTime = DateTime.UtcNow;
    }

    /// <summary>
    /// Rolling timestamps of recent deaths — ANY spot, ANY goal — for the goal-agnostic
    /// death-cluster escape (MaintenancePlanner). Distinct from DeathLoopStreak (same-spot,
    /// 30yd / 300s, the no_path-pocket detector): this catches a bot chain-dying at a lethal AREA
    /// (e.g. murlocs in a lake) during a vendor errand or grind, where the deaths are spread out
    /// and timeout-spaced so DeathLoopStreak never trips and Questing-gated attribution never fires.
    /// Cleared on any graveyard port (fresh window at the new location). Transient (in-memory).
    /// </summary>
    public List<DateTime> RecentDeaths { get; set; } = new();

    /// <summary>
    /// Record a death for the rolling cluster detector and return how many deaths have landed
    /// within the last <paramref name="windowSec"/> seconds (this one included). Prunes older entries.
    /// </summary>
    public int RecordRecentDeathAndCount(double windowSec)
    {
        var now = DateTime.UtcNow;
        RecentDeaths.Add(now);
        var cutoff = now.AddSeconds(-windowSec);
        RecentDeaths.RemoveAll(t => t < cutoff);
        return RecentDeaths.Count;
    }

    /// <summary>
    /// Hearth-escape death window (FINDING_008). Like RecentDeaths, but DELIBERATELY NOT cleared by a
    /// graveyard port — so it keeps counting across the ports that FAIL to break a loop (SneakyShock:
    /// 307 deaths, graveyard adjacent to the killer, DeathLoopStreak resets because the camp is wider
    /// than the 30yd radius). At the cap the bot is hearth-ported to its racial start instead. Cleared
    /// only on ResetDeathCounter (a real recovery / activity change) or a completed hearth.
    /// </summary>
    public List<DateTime> HearthDeaths { get; set; } = new();

    public int RecordHearthDeathAndCount(double windowSec)
    {
        var now = DateTime.UtcNow;
        HearthDeaths.Add(now);
        var cutoff = now.AddSeconds(-windowSec);
        HearthDeaths.RemoveAll(t => t < cutoff);
        return HearthDeaths.Count;
    }

    /// <summary>
    /// The bot's racial START location (playercreateinfo) — a guaranteed-safe, faction-appropriate
    /// spot used as the hearth-escape destination for a persistent death loop (FINDING_008). Returns
    /// (X, Y, Z, Map). Same values as the DB playercreateinfo rows verified 2026-08-06.
    /// </summary>
    public static (float X, float Y, float Z, int Map) RacialStart(int race) => race switch
    {
        1 => (-8949.95f, -132.493f, 83.5312f, 0),   // Human      — Northshire (EK)
        2 => (-618.518f, -4251.67f, 38.718f, 1),    // Orc        — Durotar (Kalimdor)
        3 => (-6240.32f, 331.033f, 382.758f, 0),    // Dwarf      — Coldridge (EK)
        4 => (10311.3f, 831.463f, 1326.41f, 1),     // Night Elf  — Shadowglen (Kalimdor)
        5 => (1676.35f, 1677.45f, 121.67f, 0),      // Undead     — Deathknell/Tirisfal (EK)
        6 => (-2917.58f, -257.98f, 52.9968f, 1),    // Tauren     — Camp Narache (Kalimdor)
        7 => (-6240.32f, 331.033f, 382.758f, 0),    // Gnome      — Coldridge (EK)
        8 => (-618.518f, -4251.67f, 38.718f, 1),    // Troll      — Durotar (Kalimdor)
        _ => (0f, 0f, 0f, -1),                       // unknown → no valid home (hearth won't fire)
    };

    /// <summary>
    /// LEVEL-BANDED home town (FINDING_010 refinement / Northshire-pileup fix, 2026-08-09). Both
    /// port streams (008 death-hearth + 010 stranded escape) dumped EVERY bot at the L1 racial
    /// start regardless of level: 97 bots piled at Northshire, whose map-update cost drove the
    /// core's dynamic visibility to its floor (MapUpdate.MinVisibilityDistance) and "despawned"
    /// the zone's NPCs for real players. An over-leveled bot at a starter also can't land REAL
    /// (non-grey) kills, so it re-strands in place. Route by level to a same-map guarded town with
    /// level-appropriate content in reach: ≤9 racial start, 10–15 first town, 16+ second town.
    /// All coords are inn/flightpath spots inside guard coverage; Z is approximate (the C++
    /// PORT_HOME/hearth seam ReGroundZ-snaps same-map ports).
    /// </summary>
    public static (float X, float Y, float Z, int Map) HomeFor(int race, int level)
    {
        if (level <= 9) return RacialStart(race);
        bool mid = level <= 15;
        return race switch
        {
            1 => mid ? (-10628.0f, 1036.0f, 33.0f, 0)    // Human   — Sentinel Hill (Westfall)
                     : (-10559.0f, -1189.0f, 28.0f, 0),  //          Darkshire (Duskwood)
            3 or 7 => mid ? (-5360.0f, -2953.0f, 323.0f, 0)   // Dwarf/Gnome — Thelsamar (Loch Modan)
                          : (-3688.0f, -830.0f, 10.0f, 0),    //             Menethil Harbor (Wetlands)
            4 => mid ? (9821.0f, 959.0f, 1314.0f, 1)     // Night Elf — Dolanaar (Teldrassil)
                     : (6420.0f, 529.0f, 9.0f, 1),       //            Auberdine (Darkshore)
            2 or 8 => mid ? (338.0f, -4688.0f, 17.0f, 1)      // Orc/Troll — Razor Hill (Durotar)
                          : (-472.0f, -2653.0f, 97.0f, 1),    //            The Crossroads (Barrens)
            5 => mid ? (2247.0f, 252.0f, 34.0f, 0)       // Undead  — Brill (Tirisfal)
                     : (457.0f, 1548.0f, 132.0f, 0),     //          The Sepulcher (Silverpine)
            6 => mid ? (-2361.0f, -349.0f, -9.0f, 1)     // Tauren  — Bloodhoof Village (Mulgore)
                     : (-472.0f, -2653.0f, 97.0f, 1),    //          The Crossroads (Barrens)
            _ => (0f, 0f, 0f, -1),
        };
    }

    /// <summary>
    /// Reset death counter (called when bot picks a new quest or changes activity).
    /// </summary>
    public void ResetDeathCounter()
    {
        DeathsSinceQuestStart = 0;
        DeathLoopStreak = 0;
        HearthDeaths.Clear();
    }

    /// <summary>
    /// Derive faction from race ID.
    /// </summary>
    public static string FactionForRace(int race) => race switch
    {
        2 or 5 or 6 or 8 => "Horde",
        1 or 3 or 4 or 7 => "Alliance",
        _ => "Unknown"
    };
}

public class ShadowInventoryItem
{
    public int ItemId { get; set; }
    public int Count { get; set; }
    public int Quality { get; set; }
    public int SellPrice { get; set; }
    public string Source { get; set; } = "loot";
    public int SourceCreatureEntry { get; set; }
    public DateTime AcquiredAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Saved domain state for cross-domain interruptions.
/// When a bot can't turn in a quest because bags are full, QuestingDomain saves
/// the return target here. DecisionEngine forces EconomyDomain vendoring, then
/// restores the saved domain/sub-phase on completion.
/// </summary>
public class PendingAction
{
    public ActivityType ReturnTo { get; set; }
    public string SubPhase { get; set; } = "";
    public int? QuestId { get; set; }
    public Dictionary<string, string> PhaseData { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A quest deferral that can be time-gated, level-gated, or both.
/// Time-gated: expires after a clock duration (death/stuck deferrals).
/// Level-gated: expires when bot reaches a required level (PATH_UNSAFE deferrals).
/// </summary>
public class QuestDeferral
{
    /// <summary>UTC time expiry for time-based deferrals. Null = no time limit.</summary>
    public DateTime? ExpiresAt { get; set; }
    /// <summary>Bot level required for level-based deferrals. Null = no level gate.</summary>
    public int? RequiredLevel { get; set; }

    public bool IsExpired(DateTime now, int botLevel)
    {
        // Time-gated: expired if past the clock
        if (ExpiresAt.HasValue && now >= ExpiresAt.Value) return true;
        // Level-gated: expired if bot reached the required level
        if (RequiredLevel.HasValue && botLevel >= RequiredLevel.Value) return true;
        // Neither condition met — still deferred
        return false;
    }

    public static QuestDeferral TimeBased(DateTime expiresAt) => new() { ExpiresAt = expiresAt };
    public static QuestDeferral LevelBased(int requiredLevel) => new() { RequiredLevel = requiredLevel };
}

/// <summary>
/// Session 42: per-group directive computed by the GroupCoordinator in
/// BotBrainService (ARCH §7a). One value per group per pass, stamped on every member.
/// </summary>
public enum GroupDirective
{
    None = 0,         // solo bot or coordinator off — normal weighted roll
    Questing = 1,     // all members present → work the shared batch together
    HoldAndGrind = 2, // ≥1 member away on an errand → grind at the anchor, don't advance
    Regroup = 3,      // spread past threshold → converge on the anchor (grind there)
    GroupErrand = 4   // Session 44b: the TEAM travels to a service stop (trainer/vendor).
                      // Anchor = the service NPC. Leader anchor-travels there (CombatDomain),
                      // followers arrive glued, and the BotBrainService macro brain fires
                      // TRAIN_AT_NPC / SELL_ITEMS / REPAIR_AT_NPC for every member that
                      // needs it. No member ever leaves the formation for maintenance.
}

public enum WowRace : int
{
    Human = 1, Orc = 2, Dwarf = 3, NightElf = 4, Undead = 5,
    Tauren = 6, Gnome = 7, Troll = 8
}

public enum WowClass : int
{
    Warrior = 1, Paladin = 2, Hunter = 3, Rogue = 4, Priest = 5,
    Shaman = 7, Mage = 8, Warlock = 9, Druid = 11
}

/// <summary>
/// Snapshot of a quest's DB state from character_queststatus, used to rebuild
/// ActiveQuestEntry on reconnect. Carries mob_count/item_count progress columns
/// so QuestingDomain can verify objectives are actually done before marking
/// ServerComplete (the nuke script wipes inventory but doesn't reset status).
/// </summary>
public class HydratedQuest
{
    public int QuestId { get; set; }
    public int Status { get; set; }      // 1=incomplete, 3=complete
    public int[] MobCounts { get; set; } = new int[4];
    public int[] ItemCounts { get; set; } = new int[4];
}
