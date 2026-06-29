namespace MangosSuperUI.BotLogic.Core;

/// <summary>
/// Lightweight snapshot populated from the most recent bridge STATE message.
/// Passed into every domain method so they don't need to query the bridge themselves.
/// </summary>
public class BotStateSnapshot
{
    // From STATE message
    public int Health { get; set; }
    public int MaxHealth { get; set; }
    public int Mana { get; set; }
    public int MaxMana { get; set; }
    public int Level { get; set; }
    public int MapId { get; set; }
    public int ZoneId { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public bool InCombat { get; set; }
    public bool IsDead { get; set; }
    public long TargetGuid { get; set; }

    // Enriched STATE fields (Session 6 C++ � freeSlots/totalSlots/copper)
    public uint FreeSlots { get; set; } = 16;
    public uint TotalSlots { get; set; } = 16;
    public uint Copper { get; set; } = 0;

    // Min equipped-slot durability % (100 = full / no damageable gear). Added to STATE
    // alongside the at_graveyard rez; drives the durability-repair errand.
    public uint Durability { get; set; } = 100;

    // Computed
    public float HealthPercent => MaxHealth > 0 ? Health / (float)MaxHealth : 1f;
    public float ManaPercent => MaxMana > 0 ? Mana / (float)MaxMana : 1f;
    public float BagFullness => TotalSlots > 0 ? (TotalSlots - FreeSlots) / (float)TotalSlots : 0f;

    // Enriched by BotStateTracker
    public long XP { get; set; }
    public long XPToNextLevel { get; set; }
    public bool IsNearTown { get; set; }
    public int NearbyPlayerCount { get; set; }
    public int NearbyBotCount { get; set; }

    // Server-side quest status (from C++ GetQuestStatus � authoritative)
    public uint ServerQuestId { get; set; } = 0;
    public uint ServerQuestStatus { get; set; } = 0;

    // --- Group state (Session 31 — enriched by BotBrainService from GroupManager + bridge) ---
    public int? GroupId { get; set; }
    public int? GroupLeaderGuid { get; set; }
    public float? LeaderX { get; set; }
    public float? LeaderY { get; set; }
    public float? LeaderZ { get; set; }
    public bool IsGrouped => GroupId.HasValue;

    // --- C++ held-task echo (Held-Objective build §4) — what C++ reports running right now ---
    // Unknown until the C++ STATE echo lands (Session 3). The bridge's STATE parse fills this from the
    // task_* fields in the producer movement; until then it stays Unknown and the reconcile is a no-op
    // (FromBridgeState does not set it, so the default applies).
    public HeldTaskEcho HeldTask { get; set; } = HeldTaskEcho.Unknown;

    /// <summary>
    /// Build a snapshot from the existing BotState (BotBridgeService model).
    /// </summary>
    public static BotStateSnapshot FromBridgeState(Services.BotState bs)
    {
        return new BotStateSnapshot
        {
            Health = bs.Health,
            MaxHealth = bs.MaxHealth,
            Mana = bs.Mana,
            MaxMana = bs.MaxMana,
            Level = bs.Level,
            MapId = bs.MapId,
            ZoneId = bs.ZoneId,
            X = bs.X,
            Y = bs.Y,
            Z = bs.Z,
            InCombat = bs.InCombat,
            IsDead = bs.IsDead,
            TargetGuid = bs.TargetGuid,
            FreeSlots = bs.FreeSlots,
            TotalSlots = bs.TotalSlots,
            Copper = bs.Copper,
            Durability = bs.Durability,
            ServerQuestId = bs.QuestId,
            ServerQuestStatus = bs.QuestStatus,
            HeldTask = ParseHeldTask(bs)
        };
    }

    // Build the C++ held-task echo from STATE (Held-Objective build §4). GATED on TaskActivity being
    // non-empty: a pre-Session-3 binary sends no activity → HeldTaskEcho.Unknown → the reconcile is a
    // no-op (today's behavior). Once C++ emits taskActivity, the kind comes from the existing taskState
    // string, the headway from taskActivity, plus the target creature / MOVE_TO dest / kill count.
    private static HeldTaskEcho ParseHeldTask(Services.BotState bs)
    {
        if (string.IsNullOrWhiteSpace(bs.TaskActivity))
            return HeldTaskEcho.Unknown;   // no readback yet

        var kind = ParseTaskKind(bs.TaskKind);   // committed task kind — NOT bs.TaskState (a display status)
        var activity = ParseTaskActivity(bs.TaskActivity);
        var dest = new Vec4(bs.TaskDestX, bs.TaskDestY, bs.TaskDestZ, bs.MapId);
        return new HeldTaskEcho(kind, activity, (int)bs.TaskCreature, dest, bs.TaskKills);
    }

    private static HeldTaskKind ParseTaskKind(string s) => (s ?? "").Trim().ToUpperInvariant() switch
    {
        "GRIND" => HeldTaskKind.Grind,
        "MOVE_TO" or "MOVE" or "MOVETO" => HeldTaskKind.MoveTo,
        "INTERACT" => HeldTaskKind.Interact,
        "IDLE" or "" => HeldTaskKind.Idle,
        _ => HeldTaskKind.Idle
    };

    private static TaskActivity ParseTaskActivity(string s) => (s ?? "").Trim().ToLowerInvariant() switch
    {
        "traveling" or "travelling" => Core.TaskActivity.Traveling,
        "searching" => Core.TaskActivity.Searching,
        "engaged" => Core.TaskActivity.Engaged,
        "recovering" => Core.TaskActivity.Recovering,
        "blocked" => Core.TaskActivity.Blocked,
        "idle" => Core.TaskActivity.Idle,
        _ => Core.TaskActivity.Unknown
    };
}