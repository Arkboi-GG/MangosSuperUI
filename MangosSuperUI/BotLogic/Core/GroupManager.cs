using Dapper;
using MangosSuperUI.BotLogic.Core;
using MangosSuperUI.BotLogic.Tracking;
using MangosSuperUI.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace MangosSuperUI.BotLogic.Core;

// ════════════════════════════════════════════════════════════════════
// GroupManager — Formation, persistence, and lifecycle for bot groups
//
// Session 31: Lives as a singleton on BotBrainService.
//
// All public methods are gated by GroupingMode — when Off, they're no-ops.
// DB table: vmangos_admin.bot_groups (created by BotBrainDbInit).
//
// Design for future extensibility:
//   - OnPlayerInvite / OnPlayerGroupLeft stubs for real player integration
//   - GroupLeaderType.PlayerLed path for human-led groups
//   - Opportunistic formation logic placeholder
// ════════════════════════════════════════════════════════════════════

public class GroupManager
{
    private readonly ConnectionFactory _db;
    private readonly ILogger _logger;

    // GroupId → BotGroup
    private readonly ConcurrentDictionary<int, BotGroup> _groups = new();

    // BotGuid → GroupId (reverse lookup for fast "what group am I in?")
    private readonly ConcurrentDictionary<int, int> _botToGroup = new();

    // Server-wide grouping mode (set from dashboard, defaults to Off)
    private GroupingMode _mode = GroupingMode.Off;

    // Auto-increment for in-memory group IDs (DB uses AUTO_INCREMENT,
    // but we need IDs for groups formed before first DB flush)
    private int _nextGroupId = 1;

    public GroupManager(ConnectionFactory db, ILogger logger)
    {
        _db = db;
        _logger = logger;
    }

    // ════════════════════════════════════════════════════════════════
    // Mode control (called from dashboard / BotBrainService)
    // ════════════════════════════════════════════════════════════════

    public GroupingMode Mode
    {
        get => _mode;
        set
        {
            var old = _mode;
            _mode = value;
            _logger.LogInformation("[BOT-GROUP] Grouping mode changed: {Old} → {New}", old, value);

            // If switching to Off, disband everything
            if (value == GroupingMode.Off && old != GroupingMode.Off)
            {
                CircuitTrace.Hit(0, "groupmgr: mode switched off, disbanding all groups");
                DisbandAll();
            }
        }
    }

    // ════════════════════════════════════════════════════════════════
    // Queries (always available regardless of mode)
    // ════════════════════════════════════════════════════════════════

    /// <summary>Get the group a bot belongs to, or null if solo.</summary>
    public BotGroup? GetGroup(int botGuid)
    {
        if (_botToGroup.TryGetValue(botGuid, out int groupId))
            if (_groups.TryGetValue(groupId, out var group))   // cb:fold pure lookup accessor, no routing information
                return group;   // cb:fold pure lookup accessor, no routing information
        return null;
    }

    /// <summary>Is this bot in any group?</summary>
    public bool IsGrouped(int botGuid) => _botToGroup.ContainsKey(botGuid);

    /// <summary>Is this bot the leader of its group?</summary>
    public bool IsLeader(int botGuid)
    {
        var group = GetGroup(botGuid);
        return group?.IsLeader(botGuid) ?? false;
    }

    /// <summary>Is this bot a follower (grouped but not leader)?</summary>
    public bool IsFollower(int botGuid)
    {
        var group = GetGroup(botGuid);
        return group != null && !group.IsLeader(botGuid);
    }

    /// <summary>Get the leader's GUID for a grouped bot, or null if solo.</summary>
    public int? GetLeaderGuid(int botGuid)
    {
        var group = GetGroup(botGuid);
        return group?.LeaderGuid;
    }

    /// <summary>Get all current groups (for dashboard display).</summary>
    public IReadOnlyCollection<BotGroup> GetAllGroups() => _groups.Values.ToList();

    /// <summary>Get all ungrouped bot GUIDs from a given set.</summary>
    public List<int> GetUngroupedBots(IEnumerable<int> allBotGuids)
        => allBotGuids.Where(g => !_botToGroup.ContainsKey(g)).ToList();

    // ════════════════════════════════════════════════════════════════
    // Formation (gated by mode)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Form a new bot-coordinated group. Leader = first GUID.
    /// Returns the BotGroup if formed, null if mode is Off or invalid.
    /// Does NOT send FORM_GROUP to C++ — caller must do that after
    /// (BotBrainService has the bridge reference).
    /// </summary>
    public BotGroup? FormGroup(int leaderGuid, params int[] followerGuids)
    {
        if (_mode == GroupingMode.Off)
        {
            CircuitTrace.Hit(leaderGuid, "groupmgr: form rejected, grouping mode off");
            _logger.LogDebug("[BOT-GROUP] FormGroup rejected — mode is Off");
            return null;
        }

        // Validate: no one is already grouped
        var allMembers = new List<int> { leaderGuid };
        allMembers.AddRange(followerGuids);

        foreach (var guid in allMembers)
        {
            if (_botToGroup.ContainsKey(guid))
            {
                CircuitTrace.Hit(guid, "groupmgr: form rejected, bot already grouped");
                _logger.LogWarning("[BOT-GROUP] FormGroup rejected — bot {Guid} is already in group {GroupId}",
                    guid, _botToGroup[guid]);
                return null;
            }
        }

        if (allMembers.Count < 2 || allMembers.Count > 5)
        {
            CircuitTrace.Hit(leaderGuid, "groupmgr: form rejected, invalid size", allMembers.Count);
            _logger.LogWarning("[BOT-GROUP] FormGroup rejected — invalid size {Size} (need 2-5)", allMembers.Count);
            return null;
        }

        var group = new BotGroup
        {
            GroupId = _nextGroupId++,
            LeaderGuid = leaderGuid,
            MemberGuids = allMembers,
            LeaderType = GroupLeaderType.BotCoordinated,
            FormedAt = DateTime.UtcNow
        };

        _groups[group.GroupId] = group;
        foreach (var guid in allMembers)
            _botToGroup[guid] = group.GroupId;

        _logger.LogInformation("[BOT-GROUP] Formed group {GroupId}: leader={Leader}, members=[{Members}]",
            group.GroupId, leaderGuid, string.Join(",", allMembers));


        return group;
    }

    /// <summary>
    /// Disband a group by groupId. Removes all tracking.
    /// Does NOT send DISBAND_GROUP to C++ — caller must do that.
    /// </summary>
    public bool DisbandGroup(int groupId)
    {
        if (!_groups.TryRemove(groupId, out var group))
        {
            CircuitTrace.Hit(0, "groupmgr: disband rejected, unknown group");
            return false;
        }

        foreach (var guid in group.MemberGuids)
            _botToGroup.TryRemove(guid, out _);

        _logger.LogInformation("[BOT-GROUP] Disbanded group {GroupId} (was: [{Members}])",
            groupId, string.Join(",", group.MemberGuids));


        return true;
    }

    /// <summary>Remove a single bot from its group. If group drops to 1, disband.</summary>
    public bool RemoveFromGroup(int botGuid)
    {
        var group = GetGroup(botGuid);
        if (group == null) { CircuitTrace.Hit(botGuid, "groupmgr: remove rejected, bot not grouped"); return false; }

        group.MemberGuids.Remove(botGuid);
        _botToGroup.TryRemove(botGuid, out _);

        _logger.LogInformation("[BOT-GROUP] Removed bot {Guid} from group {GroupId}", botGuid, group.GroupId);


        // If only 1 member left, disband
        if (group.MemberGuids.Count <= 1)
        {
            CircuitTrace.Hit(botGuid, "groupmgr: removal leaves <=1 member, disband group");
            DisbandGroup(group.GroupId);
        }
        // If the leader left, promote lowest GUID
        else if (group.LeaderGuid == botGuid)
        {
            CircuitTrace.Hit(botGuid, "groupmgr: leader left, promote lowest guid");
            group.LeaderGuid = group.MemberGuids.Min();
            _logger.LogInformation("[BOT-GROUP] New leader for group {GroupId}: {NewLeader}",
                group.GroupId, group.LeaderGuid);
        }

        return true;
    }

    /// <summary>Disband all groups. Called when mode switches to Off.</summary>
    public void DisbandAll()
    {
        var groupIds = _groups.Keys.ToList();
        foreach (var gid in groupIds)
            DisbandGroup(gid);

        _logger.LogInformation("[BOT-GROUP] Disbanded all groups ({Count})", groupIds.Count);
    }

    // ════════════════════════════════════════════════════════════════
    // Player integration stubs (future — Ollama chat / right-click invite)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// (Future) Called when a real player invites a bot into their party.
    /// Removes bot from any bot-bot group and marks as PlayerLed.
    /// </summary>
    public void OnPlayerInvite(int botGuid, int playerGuid)
    {
        // Remove from existing bot group if any
        RemoveFromGroup(botGuid);

        // TODO: Create a PlayerLed group entry, set bot as follower
        // The bot should switch to a "follow player" mode in DecisionEngine
        _logger.LogInformation("[BOT-GROUP] (stub) Player {Player} invited bot {Bot} — not yet implemented",
            playerGuid, botGuid);
    }

    /// <summary>
    /// (Future) Called when a bot leaves a player-led group (player kicks, disbands, or logs out).
    /// Bot returns to solo or re-joins a bot group if Sticky/Opportunistic mode is on.
    /// </summary>
    public void OnPlayerGroupLeft(int botGuid)
    {
        _logger.LogInformation("[BOT-GROUP] (stub) Bot {Bot} left player group — not yet implemented", botGuid);
    }

    // ════════════════════════════════════════════════════════════════
    // Auto-formation (called explicitly, not automatically)
    // ════════════════════════════════════════════════════════════════

    private const int MAX_LEVEL_GAP = 2;        // bots must be within 2 levels
    private const float MAX_PAIR_DISTANCE = 200f; // must be within 200yd to pair

    // ── Role buckets (2026-07-01, replaces the hardcoded class-pair passes) ──
    // A fixed classification, not level-aware: a level-1 Druid counts as a Healer here even though it
    // has no heal spell yet, exactly as the OLD code already counted Paladin as "off-healer" regardless
    // of level. If that granularity ever matters, this is the one place to make it level-aware later.
    private enum Role { Tank, Healer, Dps }

    private static Role RoleFor(int classId) => (WowClass)classId switch
    {
        WowClass.Warrior => Role.Tank,   // cb:fold pure role classification, static with no guid in reach
        WowClass.Paladin or WowClass.Priest or WowClass.Shaman or WowClass.Druid => Role.Healer,   // cb:fold pure role classification, static with no guid in reach
        _ => Role.Dps   // Hunter, Rogue, Mage, Warlock (and anything unrecognized)   // cb:fold pure role classification, static with no guid in reach
    };

    /// <summary>
    /// Role-aware auto-formation for ungrouped bots. Builds trios where possible, falls back to
    /// duos. Filters by level proximity and zone/distance. Composed from ROLE buckets (Tank = Warrior;
    /// Healer = Paladin/Priest/Shaman/Druid; Dps = Hunter/Rogue/Mage/Warlock) instead of hardcoded
    /// class pairs, so e.g. a Priest and a Shaman are interchangeable "Healer" candidates rather than
    /// needing their own explicit combo -- adding a class only means teaching RoleFor about it, not a
    /// new pass.
    ///
    /// Composition priority (best → worst):
    ///   Trio:  Tank + Healer + Dps      (full trinity — best possible)
    ///   Trio:  Tank + Healer + Healer   (still solid — a spare healer never hurts)
    ///   Trio:  Healer + Dps + Dps       (no tank available)
    ///   Trio:  Tank + Dps + Dps         (no healer available)
    ///   Duo:   Tank + Healer
    ///   Duo:   Tank + Dps
    ///   Duo:   Healer + Dps
    ///
    /// Leader selection: Tank > Healer > Dps (tankiest leads). Within the same role, lowest level
    /// leads (so quests are available to all).
    /// </summary>
    public List<BotGroup> AutoFormGroups(
        IReadOnlyDictionary<int, BotIdentity> allBots,
        Func<int, BotPosition?>? getPosition = null)
    {
        if (_mode == GroupingMode.Off)
        {
            CircuitTrace.Hit(0, "groupmgr: autoform skipped, grouping mode off");
            _logger.LogDebug("[BOT-GROUP] AutoFormGroups skipped — mode is Off");
            return new List<BotGroup>();
        }

        var formed = new List<BotGroup>();
        var claimed = new HashSet<int>(); // GUIDs already assigned this round

        var ungrouped = allBots.Values
            .Where(b => !IsGrouped(b.Guid))
            .ToList();

        // Helper: check if two bots are compatible (level + distance)
        bool AreCompatible(BotIdentity a, BotIdentity b)
        {
            if (Math.Abs(a.Level - b.Level) > MAX_LEVEL_GAP) { CircuitTrace.Hit(a.Guid, "groupmgr: pair incompatible, level gap", Math.Abs(a.Level - b.Level)); return false; }
            if (getPosition != null)
            {   // cb:fold trivial data-shape guard, position provider availability
                var posA = getPosition(a.Guid);
                var posB = getPosition(b.Guid);
                if (posA != null && posB != null)
                {   // cb:fold trivial data-shape guard, positions available
                    if (posA.MapId != posB.MapId) { CircuitTrace.Hit(a.Guid, "groupmgr: pair incompatible, different maps"); return false; }
                    float dx = posA.X - posB.X;
                    float dy = posA.Y - posB.Y;
                    if (dx * dx + dy * dy > MAX_PAIR_DISTANCE * MAX_PAIR_DISTANCE) { CircuitTrace.Hit(a.Guid, "groupmgr: pair incompatible, too far apart"); return false; }
                }
            }
            return true;
        }

        // Helper: check if a third bot is compatible with both existing members
        bool IsCompatibleWithBoth(BotIdentity candidate, BotIdentity a, BotIdentity b)
            => AreCompatible(candidate, a) && AreCompatible(candidate, b);

        // Helper: pick leader — Tank > Healer > Dps, then lowest level
        int PickLeader(params BotIdentity[] members)
        {
            int RolePriority(int classId) => RoleFor(classId) switch
            {
                Role.Tank => 0,   // cb:fold pure ranking helper, leader choice carried by the form probes
                Role.Healer => 1,   // cb:fold pure ranking helper, leader choice carried by the form probes
                _ => 2   // Dps   // cb:fold pure ranking helper, leader choice carried by the form probes
            };
            return members
                .OrderBy(m => RolePriority(m.ClassId))
                .ThenBy(m => m.Level)
                .ThenBy(m => m.Guid)
                .First().Guid;
        }

        // Helper: try to form a group, returns true if successful
        bool TryForm(params BotIdentity[] members)
        {
            if (members.Any(m => claimed.Contains(m.Guid))) { CircuitTrace.Hit(members[0].Guid, "groupmgr: tryform rejected, member already claimed this round"); return false; }
            int leader = PickLeader(members);
            var followers = members.Where(m => m.Guid != leader).Select(m => m.Guid).ToArray();
            var group = FormGroup(leader, followers);
            if (group == null) return false;   // cb:fold reject reason probed inside FormGroup
            formed.Add(group);
            foreach (var m in members) claimed.Add(m.Guid);
            return true;
        }

        // Buckets (only unclaimed, re-filtered each pass) — by ROLE now, not raw class id.
        List<BotIdentity> GetAvailable(Role role) =>
            ungrouped.Where(b => RoleFor(b.ClassId) == role && !claimed.Contains(b.Guid))
                     .OrderBy(b => b.Level).ThenBy(b => b.Guid).ToList();

        // ── Pass 1: Trios, best composition first ──

        // Tank + Healer + Dps (full trinity)
        foreach (var t in GetAvailable(Role.Tank))
        {
            var healer = GetAvailable(Role.Healer).FirstOrDefault(h => AreCompatible(t, h));
            if (healer == null) { CircuitTrace.Hit(t.Guid, "groupmgr: trinity pass, no compatible healer for tank"); continue; }

            var dps = GetAvailable(Role.Dps).FirstOrDefault(d => IsCompatibleWithBoth(d, t, healer));
            if (dps != null)
            {
                CircuitTrace.Hit(t.Guid, "groupmgr: trio attempt tank+healer+dps");
                TryForm(t, healer, dps);
            }
        }

        // Tank + Healer + Healer (a spare healer beats a missing one)
        foreach (var t in GetAvailable(Role.Tank))
        {
            var healers = GetAvailable(Role.Healer).Where(h => AreCompatible(t, h)).ToList();
            if (healers.Count < 2) { CircuitTrace.Hit(t.Guid, "groupmgr: tank+2healer pass, not enough healers", healers.Count); continue; }
            var h1 = healers[0];
            var h2 = healers.Skip(1).FirstOrDefault(h => IsCompatibleWithBoth(h, t, h1));
            if (h2 != null)
            {
                CircuitTrace.Hit(t.Guid, "groupmgr: trio attempt tank+healer+healer");
                TryForm(t, h1, h2);
            }
        }

        // Healer + Dps + Dps (no tank left)
        foreach (var h in GetAvailable(Role.Healer))
        {
            var dpsList = GetAvailable(Role.Dps).Where(d => AreCompatible(h, d)).ToList();
            if (dpsList.Count < 2) { CircuitTrace.Hit(h.Guid, "groupmgr: healer+2dps pass, not enough dps", dpsList.Count); continue; }
            var d1 = dpsList[0];
            var d2 = dpsList.Skip(1).FirstOrDefault(d => IsCompatibleWithBoth(d, h, d1));
            if (d2 != null)
            {
                CircuitTrace.Hit(h.Guid, "groupmgr: trio attempt healer+dps+dps");
                TryForm(h, d1, d2);
            }
        }

        // Tank + Dps + Dps (no healer left)
        foreach (var t in GetAvailable(Role.Tank))
        {
            var dpsList = GetAvailable(Role.Dps).Where(d => AreCompatible(t, d)).ToList();
            if (dpsList.Count < 2) { CircuitTrace.Hit(t.Guid, "groupmgr: tank+2dps pass, not enough dps", dpsList.Count); continue; }
            var d1 = dpsList[0];
            var d2 = dpsList.Skip(1).FirstOrDefault(d => IsCompatibleWithBoth(d, t, d1));
            if (d2 != null)
            {
                CircuitTrace.Hit(t.Guid, "groupmgr: trio attempt tank+dps+dps");
                TryForm(t, d1, d2);
            }
        }

        // ── Pass 2: Duos (remaining ungrouped) ──

        // Tank + Healer
        foreach (var t in GetAvailable(Role.Tank))
        {
            var healer = GetAvailable(Role.Healer).FirstOrDefault(h => AreCompatible(t, h));
            if (healer != null) { CircuitTrace.Hit(t.Guid, "groupmgr: duo attempt tank+healer"); TryForm(t, healer); }
        }

        // Tank + Dps
        foreach (var t in GetAvailable(Role.Tank))
        {
            var dps = GetAvailable(Role.Dps).FirstOrDefault(d => AreCompatible(t, d));
            if (dps != null) { CircuitTrace.Hit(t.Guid, "groupmgr: duo attempt tank+dps"); TryForm(t, dps); }
        }

        // Healer + Dps (no tanks left)
        foreach (var h in GetAvailable(Role.Healer))
        {
            var dps = GetAvailable(Role.Dps).FirstOrDefault(d => AreCompatible(h, d));
            if (dps != null) { CircuitTrace.Hit(h.Guid, "groupmgr: duo attempt healer+dps"); TryForm(h, dps); }
        }

        // ── Pass 3 (Session 33): Any-remaining duos — better grouped than solo ──
        // After all role-priority combos are exhausted, pair up whoever's left, role-blind.
        // Rogue + Rogue? Fine. Priest + Priest? Still shared kill credit.
        {
            var stillUngrouped = ungrouped.Where(b => !claimed.Contains(b.Guid)).ToList();
            while (stillUngrouped.Count >= 2)
            {
                var first = stillUngrouped[0];
                var partner = stillUngrouped.Skip(1)
                    .FirstOrDefault(b => AreCompatible(first, b));

                if (partner != null)
                {
                    CircuitTrace.Hit(first.Guid, "groupmgr: role-blind duo pairing");
                    TryForm(first, partner);
                    stillUngrouped.Remove(first);
                    stillUngrouped.Remove(partner);
                }
                else
                {
                    CircuitTrace.Hit(first.Guid, "groupmgr: no compatible partner, bot left solo this pass");
                    // Can't pair this bot with anyone (level/distance gap) — skip
                    stillUngrouped.Remove(first);
                }
            }
        }

        // ── Pass 4 (Session 33): No bot left behind ──
        // If exactly 1 bot remains ungrouped, stuff them into the smallest
        // existing group (making it 3 or 4). Better than solo.
        {
            var loner = ungrouped.Where(b => !claimed.Contains(b.Guid)).ToList();
            if (loner.Count == 1)
            {
                CircuitTrace.Hit(loner[0].Guid, "groupmgr: single stray, try absorbing into smallest group");
                var stray = loner[0];
                // Find smallest compatible group to absorb the stray
                var bestGroup = formed
                    .Where(g => g.Size < 5) // WoW party max
                    .Where(g =>
                    {
                        // Check level compatibility with all members
                        return g.MemberGuids.All(mg =>
                        {
                            if (!allBots.TryGetValue(mg, out var member)) return true;   // cb:fold trivial data-shape guard, missing roster entry
                            return Math.Abs(member.Level - stray.Level) <= MAX_LEVEL_GAP;
                        });
                    })
                    .OrderBy(g => g.Size) // smallest first — prefer making a trio over a quad
                    .FirstOrDefault();

                if (bestGroup != null)
                {
                    CircuitTrace.Hit(stray.Guid, "groupmgr: stray absorbed into existing group");
                    bestGroup.MemberGuids.Add(stray.Guid);
                    _botToGroup[stray.Guid] = bestGroup.GroupId;
                    claimed.Add(stray.Guid);

                    _logger.LogInformation(
                        "[BOT-GROUP] No-bot-left-behind: added {Name}({Guid}) to group {GroupId} (now {Size} members)",
                        stray.Name, stray.Guid, bestGroup.GroupId, bestGroup.Size);

                }
            }
        }

        var remaining = ungrouped.Count(b => !claimed.Contains(b.Guid));
        _logger.LogInformation(
            "[BOT-GROUP] AutoFormGroups: formed {Count} groups ({Trios} trios, {Duos} duos, {Larger} 4+), {Remaining} bots ungrouped",
            formed.Count,
            formed.Count(g => g.Size == 3),
            formed.Count(g => g.Size == 2),
            formed.Count(g => g.Size >= 4),
            remaining);

        return formed;
    }

    // ════════════════════════════════════════════════════════════════
    // BotIdentity enrichment (called after group changes)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Stamp GroupId + GroupLeaderGuid onto BotIdentity for a bot.
    /// Called after forming/disbanding groups so domains can read group state
    /// off BotIdentity without needing a GroupManager reference.
    /// </summary>
    public void EnrichBotIdentity(BotIdentity bot)
    {
        var group = GetGroup(bot.Guid);
        if (group != null)
        {
            CircuitTrace.Hit(bot.Guid, "groupmgr: enrich stamps grouped state");
            bot.GroupId = group.GroupId;
            bot.GroupLeaderGuid = group.IsLeader(bot.Guid) ? bot.Guid : group.LeaderGuid;
        }
        else
        {
            CircuitTrace.Hit(bot.Guid, "groupmgr: enrich stamps solo state");
            bot.GroupId = null;
            bot.GroupLeaderGuid = null;
        }
    }

    /// <summary>Enrich all bots in the roster.</summary>
    public void EnrichAllBots(IEnumerable<BotIdentity> bots)
    {
        foreach (var bot in bots)
            EnrichBotIdentity(bot);
    }

    // ════════════════════════════════════════════════════════════════
    // DB persistence
    // ════════════════════════════════════════════════════════════════

    /// <summary>Load groups from DB on startup. Assigns GroupId/GroupLeaderGuid on BotIdentity.</summary>
    public async Task LoadGroupsFromDbAsync()
    {
        try
        {
            using var conn = _db.Admin();
            var rows = await conn.QueryAsync<BotGroupRow>(
                "SELECT group_id, leader_guid, member_guids, leader_type, formed_at FROM bot_groups");

            int loaded = 0;
            foreach (var row in rows)
            {
                var memberGuids = row.member_guids
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => int.Parse(s.Trim()))
                    .ToList();

                if (memberGuids.Count < 2) continue;   // cb:fold trivial data-shape guard on the DB row

                var group = new BotGroup
                {
                    GroupId = row.group_id,
                    LeaderGuid = row.leader_guid,
                    MemberGuids = memberGuids,
                    LeaderType = (GroupLeaderType)row.leader_type,
                    FormedAt = row.formed_at
                };

                _groups[group.GroupId] = group;
                foreach (var guid in memberGuids)
                    _botToGroup[guid] = group.GroupId;

                if (group.GroupId >= _nextGroupId)
                    _nextGroupId = group.GroupId + 1;   // cb:fold id-counter bookkeeping, no routing information

                loaded++;
            }

            _logger.LogInformation("[BOT-GROUP] Loaded {Count} groups from DB", loaded);
        }
        catch (Exception ex)
        {
            CircuitTrace.Hit(0, "groupmgr: load groups from DB failed");
            _logger.LogError(ex, "[BOT-GROUP] Failed to load groups from DB");
        }
    }

    /// <summary>Persist current groups to DB (full replace).</summary>
    public async Task SaveGroupsToDbAsync()
    {
        try
        {
            using var conn = _db.Admin();

            // Truncate and re-insert (simple for small group counts)
            await conn.ExecuteAsync("DELETE FROM bot_groups");

            foreach (var group in _groups.Values)
            {
                await conn.ExecuteAsync(@"
                    INSERT INTO bot_groups (group_id, leader_guid, member_guids, leader_type, formed_at)
                    VALUES (@GroupId, @LeaderGuid, @MemberGuids, @LeaderType, @FormedAt)",
                    new
                    {
                        group.GroupId,
                        group.LeaderGuid,
                        MemberGuids = string.Join(",", group.MemberGuids),
                        LeaderType = (int)group.LeaderType,
                        group.FormedAt
                    });
            }

            _logger.LogDebug("[BOT-GROUP] Saved {Count} groups to DB", _groups.Count);
        }
        catch (Exception ex)
        {
            CircuitTrace.Hit(0, "groupmgr: save groups to DB failed");
            _logger.LogError(ex, "[BOT-GROUP] Failed to save groups to DB");
        }
    }

    // ── DB row model ──
    private class BotGroupRow
    {
        public int group_id { get; set; }
        public int leader_guid { get; set; }
        public string member_guids { get; set; } = "";
        public int leader_type { get; set; }
        public DateTime formed_at { get; set; }
    }
}