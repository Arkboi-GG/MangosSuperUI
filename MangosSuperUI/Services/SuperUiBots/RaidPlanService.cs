using System.Text.Json;
using System.Text.Json.Serialization;
using MangosSuperUI.BotLogic.Tracking;

namespace MangosSuperUI.Services;

// ============================================================================
// RaidPlanService — [RAID-PLAN] store + assign + push raid plan documents
// (PLAN_19 M-B; MSUIClient docs/plans/PLAN_19_RAID_DOCTRINE_PIPELINE.md).
//
// Plan documents are the EXACT files the MSUIClient Encounter Lab exports
// ("Export plan" on the Game Plan tab): doctrine + per-body encounter rules +
// inlined rotations, one JSON file. Drop them in RaidPlans/ (config key
// "RaidPlans:Path"), read fresh per use — the RotationService hot-reload law.
//
// Assignment is per BOT NAME → plan name, persisted in RaidPlans/assignments.json
// and re-pushed on every HELLO. The push flattens each bot's slice into the
// LOAD_RAID_PLAN wire (flat JSON + house pipe strings) that RaidPlanLaw.cpp
// parses on the core: doctrine switches, bucket assignments, maintain-aura
// chains, add-control jobs, and the bot's own job/side/class/targets/avoids.
// A bot assigned a plan that has no matching body entry still receives the
// raid-wide doctrine with a defaulted slice — doctrine is a group fact.
//
// Same late-wire pattern as RotationService: injects the bridge and calls
// SetRaidPlanService(this) so the HELLO hook can re-push without a DI cycle.
// ============================================================================
public class RaidPlanService
{
    private readonly BotBridgeService _bridge;
    private readonly ILogger<RaidPlanService> _logger;
    private readonly string _dir;
    private readonly string _assignmentsPath;
    private readonly object _gate = new();

    private Dictionary<string, string> _assignments = new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public RaidPlanService(BotBridgeService bridge, IConfiguration config, ILogger<RaidPlanService> logger)
    {
        _bridge = bridge;
        _logger = logger;
        _dir = config["RaidPlans:Path"] ?? "RaidPlans";
        Directory.CreateDirectory(_dir);
        _assignmentsPath = Path.Combine(_dir, "assignments.json");
        LoadAssignments();
        _bridge.SetRaidPlanService(this);
        _logger.LogInformation("[RAID-PLAN] service up — dir='{Dir}', {Count} assignment(s) on file",
            Path.GetFullPath(_dir), _assignments.Count);
    }

    // ------------------------------------------------------------------ model
    // Mirrors the client's RaidPlanDocument (RaidPlanDocument.cs). Enums arrive
    // as strings (the client writes JsonStringEnumConverter); unknown fields are
    // ignored so an additive client schema never breaks the web side.

    public enum RaidJob { None, Tank, Healer, Melee, Ranged }
    public enum RaidSide { None, Left, Center, Right }
    public enum CombatEnemyKind { AnyAdd, CurrentEnemy, PrimaryEnemy }

    public class PhaseJobAssignment
    {
        public string PhaseKey { get; set; } = "";
        public RaidJob Job { get; set; }
        public int FromOrdinal { get; set; } = 1;
        public CombatEnemyKind Target { get; set; }
    }

    public class MaintainAuraRule
    {
        public uint SpellId { get; set; }
        public string Name { get; set; } = "";
        public uint CasterClassId { get; set; }
        public int DurationMs { get; set; }
        public int CooldownMs { get; set; }
        public int TargetTankOrdinal { get; set; } = 1;
    }

    public class AddControlJob
    {
        public uint CasterClassId { get; set; }
        public float RadiusYards { get; set; } = 8f;
        public float SlowFactor { get; set; } = 0.5f;
        public int MinAdds { get; set; } = 3;
        public float CastRangeYards { get; set; } = 30f;
    }

    public class RaidDoctrine
    {
        public bool DeriveFormation { get; set; } = true;
        public bool DodgeTelegraphs { get; set; } = true;
        public bool KeepClearOfCones { get; set; } = true;
        public bool SpreadFromTargetedCasts { get; set; } = true;
        public float SpreadYards { get; set; } = 8f;
        public bool GroupHealing { get; set; } = true;
        public List<PhaseJobAssignment>? Assignments { get; set; }
        public List<MaintainAuraRule>? MaintainAuras { get; set; }
        public List<AddControlJob>? AddControl { get; set; }
        public bool BossThreatLite { get; set; }
    }

    public class CombatEnemyPriority
    {
        public CombatEnemyKind Kind { get; set; }
        public bool Enabled { get; set; } = true;
    }

    public class PhaseTargetOverride
    {
        public string PhaseKey { get; set; } = "";
        public List<CombatEnemyPriority> Priorities { get; set; } = new();
    }

    public class RaidPlanBody
    {
        public string Key { get; set; } = "";
        public string Name { get; set; } = "";
        public RaidJob Job { get; set; }
        public RaidSide Side { get; set; }
        public uint ClassId { get; set; }
        public string? RotationId { get; set; }
        public List<PhaseTargetOverride>? PhaseTargets { get; set; }
        public List<string>? AvoidAbilityKeys { get; set; }
    }

    public class RaidPlanDocument
    {
        public int SchemaVersion { get; set; }
        public string Name { get; set; } = "";
        public string? EncounterKey { get; set; }
        public RaidDoctrine Doctrine { get; set; } = new();
        public List<RaidPlanBody> Bodies { get; set; } = new();
    }

    // ---------------------------------------------------------------- plans

    /// <summary>All plan documents on disk, read fresh (hot-reload by construction).</summary>
    public List<RaidPlanDocument> LoadPlans()
    {
        var plans = new List<RaidPlanDocument>();
        foreach (var file in Directory.EnumerateFiles(_dir, "*.json"))
        {
            if (string.Equals(Path.GetFileName(file), "assignments.json", StringComparison.OrdinalIgnoreCase))
                continue;   // cb:fold plan-file enumeration detail, no per-bot routing
            try
            {
                var plan = JsonSerializer.Deserialize<RaidPlanDocument>(File.ReadAllText(file), JsonOpts);
                if (plan == null || string.IsNullOrWhiteSpace(plan.Name) || plan.SchemaVersion != 1)
                {
                    CircuitTrace.Hit(0, "raidplan: plan file invalid, ignored");
                    _logger.LogWarning("[RAID-PLAN] plan file '{File}' is empty/nameless/wrong-schema — ignored", file);
                    continue;
                }
                plans.Add(plan);
            }
            catch (Exception ex)
            {
                CircuitTrace.Hit(0, "raidplan: plan file parse failed, ignored");
                _logger.LogWarning("[RAID-PLAN] plan file '{File}' failed to parse: {Err}", file, ex.Message);
            }
        }
        return plans;
    }

    public RaidPlanDocument? FindPlan(string name)
        => LoadPlans().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    // ------------------------------------------------------------- assignments

    public IReadOnlyDictionary<string, string> Assignments
    {
        get
        {
            lock (_gate)
            {
                LoadAssignments();
                return new Dictionary<string, string>(_assignments, StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    /// <summary>Assign a plan to one bot or to every online bot ("*"). Persists,
    /// then pushes to whoever is online; offline bots get it on their next HELLO.</summary>
    public async Task<string> AssignAsync(string botName, string planName)
    {
        var plan = FindPlan(planName);
        if (plan == null)
        {
            CircuitTrace.Hit(0, "raidplan: assign failed, plan not found");
            return $"plan '{planName}' not found (have: {string.Join(", ", LoadPlans().Select(p => p.Name))})";
        }

        List<(int guid, string name)> targets = botName == "*"
            ? _bridge.Connections.Select(kvp => (kvp.Key, kvp.Value.State.Name)).ToList()
            : new List<(int, string)>();
        if (botName != "*")
        {
            CircuitTrace.Hit(0, "raidplan: assigning plan to single bot");
            lock (_gate)
            {
                LoadAssignments();
                _assignments[botName.Trim()] = plan.Name;
                SaveAssignments();
            }
            var online = FindOnlineBot(botName);
            if (online == null)
            {
                CircuitTrace.Hit(0, "raidplan: assignment persisted, bot offline");
                return $"assigned '{plan.Name}' to {botName} (offline — pushes on next login)";
            }
            await PushAsync(online.Value.guid, online.Value.name, plan);
            return $"assigned '{plan.Name}' to {botName} and pushed";
        }

        lock (_gate)
        {
            LoadAssignments();
            foreach (var (_, name) in targets)
                _assignments[name.Trim()] = plan.Name;
            SaveAssignments();
        }
        foreach (var (guid, name) in targets)
            await PushAsync(guid, name, plan);
        return $"assigned '{plan.Name}' to {targets.Count} online bot(s) and pushed";
    }

    public async Task<string> ClearAsync(string botName)
    {
        bool had;
        lock (_gate)
        {
            LoadAssignments();
            had = _assignments.Remove(botName.Trim());
            if (had) { CircuitTrace.Hit(0, "raidplan: assignment cleared and saved"); SaveAssignments(); }
        }
        // No live "unload" wire yet: the plan stands on the bot until replaced or
        // relog. Honest status over a pretend-clear.
        await Task.CompletedTask;
        return had
            ? $"cleared {botName}'s assignment (live plan stands until replaced or relog)"
            : $"{botName} had no assignment";
    }

    /// <summary>HELLO hook (called by BotBridgeService): re-push the persisted plan.</summary>
    public async Task OnBotHelloAsync(int guid, string name)
    {
        string? planName;
        lock (_gate)
        {
            LoadAssignments();
            _assignments.TryGetValue(name.Trim(), out planName);
        }
        if (planName == null) { CircuitTrace.Hit(guid, "raidplan: no plan assigned on hello"); return; }
        var plan = FindPlan(planName);
        if (plan == null)
        {
            CircuitTrace.Hit(guid, "raidplan: assigned plan file missing, nothing pushed");
            _logger.LogWarning("[RAID-PLAN] {Bot} is assigned '{Plan}' but no such plan file exists — nothing pushed", name, planName);
            return;
        }
        await PushAsync(guid, name, plan);
    }

    // --------------------------------------------------------------- the wire

    private sealed record FormationMeta(int SideWire, int Slot, int Count, bool MainTank);

    /// <summary>Per-body formation meta, resolved HERE because one bot only knows
    /// itself while the pusher sees the whole roster. Mirrors the sim's derivation
    /// law exactly (EncounterSim.AdvanceFormation): MT = first Tank by key; buckets
    /// MT | melee (spare tanks + Melee) | healer | ranged (+None); explicit
    /// Left/Right kept, unsided bodies alternate per bucket; slot index/count per
    /// (bucket, flank) in stable key order.</summary>
    private static Dictionary<string, FormationMeta> BuildFormationMeta(RaidPlanDocument plan)
    {
        var bodies = plan.Bodies.OrderBy(b => b.Key, StringComparer.Ordinal).ToList();
        var mainTank = bodies.FirstOrDefault(b => b.Job == RaidJob.Tank);
        string Bucket(RaidPlanBody b) => ReferenceEquals(b, mainTank) ? "mt" : b.Job switch
        {
            RaidJob.Tank or RaidJob.Melee => CircuitTrace.Pass("melee", 0, "raidplan: formation bucket melee"),
            RaidJob.Healer => CircuitTrace.Pass("healer", 0, "raidplan: formation bucket healer"),
            _ => CircuitTrace.Pass("ranged", 0, "raidplan: formation bucket ranged"),
        };
        var meta = new Dictionary<string, FormationMeta>(StringComparer.Ordinal);
        foreach (var bucket in bodies.GroupBy(Bucket))
        {
            int unsided = 0;
            var flanked = bucket.Select(b => (Body: b, Sign: b.Side switch
            {
                RaidSide.Left => CircuitTrace.Pass(1, 0, "raidplan: flank kept left"),
                RaidSide.Right => CircuitTrace.Pass(-1, 0, "raidplan: flank kept right"),
                _ => CircuitTrace.Pass(unsided++ % 2 == 0 ? 1 : -1, 0, "raidplan: unsided body alternated"),
            })).ToList();
            foreach (var flank in flanked.GroupBy(entry => entry.Sign))
            {
                int count = flank.Count(), index = 0;
                foreach (var (body, sign) in flank)
                    meta[body.Key] = new FormationMeta(
                        sign > 0 ? 1 : 3, index++, count, ReferenceEquals(body, mainTank));
            }
        }
        return meta;
    }

    /// <summary>Flatten one bot's slice of the plan into the LOAD_RAID_PLAN payload
    /// RaidPlanLaw.cpp parses. Body matched by bot name (the fleet's stable id);
    /// no match ⇒ doctrine with a defaulted slice, because doctrine is a group fact.
    /// Bucket assignments ("tanks 2+ on adds") are baked into b_targets here — the
    /// pusher knows every body's job ordinal, the bot does not.</summary>
    private async Task PushAsync(int guid, string name, RaidPlanDocument plan)
    {
        var body = plan.Bodies.FirstOrDefault(b =>
                string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase)) ??
            plan.Bodies.FirstOrDefault(b =>
                string.Equals(b.Key, name, StringComparison.OrdinalIgnoreCase));
        var d = plan.Doctrine;
        FormationMeta? formation = body != null &&
            BuildFormationMeta(plan).TryGetValue(body.Key, out var found) ? found : null;

        // Bucket assignments matching this body's job + ordinal become extra phase
        // orders — the body's own authored overrides always win their phase.
        IEnumerable<string> bakedAssignments = Enumerable.Empty<string>();
        if (body != null && d.Assignments is { Count: > 0 })
        {
            CircuitTrace.Hit(guid, "raidplan: baking bucket assignments for body");
            int ordinal = 1 + plan.Bodies.Count(other => other.Job == body.Job &&
                string.CompareOrdinal(other.Key, body.Key) < 0);
            bakedAssignments = d.Assignments
                .Where(a => a.Job == body.Job && ordinal >= a.FromOrdinal)
                .Where(a => (body.PhaseTargets ?? new())
                    .All(t => !string.Equals(t.PhaseKey, a.PhaseKey, StringComparison.Ordinal)))
                .Select(a => $"{a.PhaseKey}:{(int)a.Target}");
        }

        string assignments = string.Join("|", (d.Assignments ?? new())
            .Select(a => $"{a.PhaseKey}:{(int)a.Job}:{a.FromOrdinal}:{(int)a.Target}"));
        string auras = string.Join("|", (d.MaintainAuras ?? new())
            .Select(r => $"{r.SpellId}:{r.CasterClassId}:{r.DurationMs}:{r.CooldownMs}:{r.TargetTankOrdinal}"));
        string addctl = string.Join("|", (d.AddControl ?? new())
            .Select(j => string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"{j.CasterClassId}:{j.RadiusYards:0.0#}:{j.SlowFactor:0.0#}:{j.MinAdds}:{j.CastRangeYards:0.0#}")));
        string targets = string.Join("|", (body?.PhaseTargets ?? new())
            .Where(t => t.Priorities.Any(p => p.Enabled))
            .Select(t => $"{t.PhaseKey}:" + string.Join(",",
                t.Priorities.Where(p => p.Enabled).Select(p => (int)p.Kind)))
            .Concat(bakedAssignments));
        string avoid = string.Join(",", body?.AvoidAbilityKeys ?? new());

        await _bridge.SendToBotAsync(guid, "LOAD_RAID_PLAN", new
        {
            schema = 1,
            plan = plan.Name,
            encounter = plan.EncounterKey ?? "",
            d_formation = d.DeriveFormation ? 1 : 0,
            d_dodge = d.DodgeTelegraphs ? 1 : 0,
            d_cones = d.KeepClearOfCones ? 1 : 0,
            d_spread = d.SpreadFromTargetedCasts ? 1 : 0,
            d_spreadyd = d.SpreadYards,
            d_groupheal = d.GroupHealing ? 1 : 0,
            d_threatlite = d.BossThreatLite ? 1 : 0,
            assignments,
            auras,
            addctl,
            b_job = (int)(body?.Job ?? RaidJob.None),
            // the DERIVED flank (auto-split resolved here), not the raw authored side
            b_side = formation?.SideWire ?? (int)(body?.Side ?? RaidSide.None),
            b_class = body?.ClassId ?? 0,
            b_rot = body?.RotationId ?? "",
            b_avoid = avoid,
            b_targets = targets,
            b_slot = formation?.Slot ?? 0,
            b_slotcount = formation?.Count ?? 1,
            b_mt = formation is { MainTank: true } ? 1 : 0
        });
        _logger.LogInformation(
            "[RAID-PLAN] pushed '{Plan}' to {Bot} (guid={Guid}, body={Body}) — watch for RAID_PLAN_ACK",
            plan.Name, name, guid, body?.Key ?? "(doctrine only)");
    }

    private (int guid, string name)? FindOnlineBot(string botName)
    {
        foreach (var kvp in _bridge.Connections)
            if (string.Equals(kvp.Value.State.Name, botName, StringComparison.OrdinalIgnoreCase))
            {
                CircuitTrace.Hit(kvp.Key, "raidplan: online bot matched by name");
                return (kvp.Key, kvp.Value.State.Name);
            }
        return null;
    }

    private void LoadAssignments()
    {
        try
        {
            _assignments = (File.Exists(_assignmentsPath)
                    ? JsonSerializer.Deserialize<Dictionary<string, string>>(
                        File.ReadAllText(_assignmentsPath), JsonOpts)
                    : null)
                is { } loaded
                ? new Dictionary<string, string>(loaded, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            CircuitTrace.Hit(0, "raidplan: assignments file parse failed, starting empty");
            _logger.LogWarning("[RAID-PLAN] assignments.json failed to parse ({Err}) — starting empty", ex.Message);
            _assignments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void SaveAssignments()
    {
        var tempPath = Path.Combine(_dir, "assignments.json.tmp");
        File.WriteAllText(tempPath, JsonSerializer.Serialize(_assignments, JsonOpts));
        File.Move(tempPath, _assignmentsPath, overwrite: true);
    }
}
