using System.Text.Json;
using System.Text.Json.Serialization;

namespace MangosSuperUI.Services;

// ============================================================================
// RotationService — [ROTATION] C#-authored combat rotations, dynamically loaded
// (2026-07-16; RotationSlate design 2026-05-11).
//
// Profiles are JSON files in the Rotations/ folder (config key "Rotations:Path"),
// read fresh on every use — edit a file, re-assign, done; no restart, no watcher.
// Assignments (bot name → profile name) persist in Rotations/assignments.json and
// are re-pushed automatically on every bot HELLO, so a server restart or a bot
// relog never silently drops back to vanilla without the assignment saying so.
//
// The wire (LOAD_ROTATION → BridgeHandleLoadRotation) carries the instructions as
// the house pipe idiom, pre-sorted by priority:
//     spellId:priority:target:hpMin:hpMax:auraId:auraPresent | ...
// Empty data clears the slate — the bot's vanilla class AI resumes next tick.
// C++ resolves SpellEntry pointers at load and acks ROTATION_ACK with
// loaded/skipped counts; a skipped>0 ack is logged loudly by the bridge so a bad
// profile (wrong rank, unlearned spell) is visible, not a silent under-perform.
//
// Circularity: mirrors the BotBrainService pattern — this service injects the
// bridge and wires itself in via SetRotationService(this) at construction.
// ============================================================================
public class RotationService
{
    private readonly BotBridgeService _bridge;
    private readonly ILogger<RotationService> _logger;
    private readonly string _dir;
    private readonly string _assignmentsPath;
    private readonly object _gate = new();

    // bot name (lowercased) -> profile name. Persisted; pushed on HELLO.
    private Dictionary<string, string> _assignments = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    public RotationService(BotBridgeService bridge, IConfiguration config, ILogger<RotationService> logger)
    {
        _bridge = bridge;
        _logger = logger;
        _dir = config["Rotations:Path"] ?? "Rotations";
        Directory.CreateDirectory(_dir);
        _assignmentsPath = Path.Combine(_dir, "assignments.json");
        LoadAssignments();
        _bridge.SetRotationService(this);
        _logger.LogInformation("[ROTATION] service up — dir='{Dir}', {Count} assignment(s) on file",
            Path.GetFullPath(_dir), _assignments.Count);
    }

    // ------------------------------------------------------------------ model

    public class RotationProfile
    {
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public List<RotationInstruction> Instructions { get; set; } = new();
    }

    public class RotationInstruction
    {
        public uint SpellId { get; set; }
        public int Priority { get; set; } = 100;
        // SELF | CURRENT_TARGET | LOWEST_HP_PARTY (see ROTATIONS.md; C++ ResolveRotationTarget)
        public string Target { get; set; } = "CURRENT_TARGET";
        public int HpMin { get; set; } = 0;      // resolved target's health % window, inclusive
        public int HpMax { get; set; } = 100;
        public uint Aura { get; set; } = 0;      // 0 = no aura condition
        public bool AuraPresent { get; set; } = false;   // Aura != 0: fire only if target HAS (true) / LACKS (false) it
        public string? Note { get; set; }        // human-readable; never hits the wire
    }

    private static int TargetToWire(string target) => target.Trim().ToUpperInvariant() switch
    {
        "SELF" => 0,
        "CURRENT_TARGET" => 1,
        "LOWEST_HP_PARTY" => 2,
        _ => 1   // unknown kinds degrade to CURRENT_TARGET; the profile lists valid names
    };

    // ---------------------------------------------------------------- profiles

    /// <summary>All profiles on disk, read fresh (hot-reload by construction).</summary>
    public List<RotationProfile> LoadProfiles()
    {
        var profiles = new List<RotationProfile>();
        foreach (var file in Directory.EnumerateFiles(_dir, "*.json"))
        {
            if (string.Equals(Path.GetFileName(file), "assignments.json", StringComparison.OrdinalIgnoreCase))
                continue;
            try
            {
                var p = JsonSerializer.Deserialize<RotationProfile>(File.ReadAllText(file), JsonOpts);
                if (p == null || string.IsNullOrWhiteSpace(p.Name) || p.Instructions.Count == 0)
                {
                    _logger.LogWarning("[ROTATION] profile file '{File}' is empty/nameless — ignored", file);
                    continue;
                }
                profiles.Add(p);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[ROTATION] profile file '{File}' failed to parse: {Err}", file, ex.Message);
            }
        }
        return profiles;
    }

    public RotationProfile? FindProfile(string name)
        => LoadProfiles().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The pipe payload BridgeHandleLoadRotation parses — pre-sorted by priority.</summary>
    private static string BuildWireData(RotationProfile profile)
        => string.Join("|", profile.Instructions
            .OrderBy(i => i.Priority)
            .Select(i => $"{i.SpellId}:{i.Priority}:{TargetToWire(i.Target)}:{i.HpMin}:{i.HpMax}:{i.Aura}:{(i.AuraPresent ? 1 : 0)}"));

    // ------------------------------------------------------------- assignments

    public IReadOnlyDictionary<string, string> Assignments
    {
        get { lock (_gate) return new Dictionary<string, string>(_assignments); }
    }

    /// <summary>
    /// Assign a profile to a bot by NAME (stable across sessions; guid resolved per push).
    /// Persists immediately; pushes immediately when the bot is online, otherwise the next
    /// HELLO delivers it. Returns a human-readable status for the API/UI.
    /// </summary>
    public async Task<string> AssignAsync(string botName, string profileName)
    {
        var profile = FindProfile(profileName);
        if (profile == null)
            return $"profile '{profileName}' not found (have: {string.Join(", ", LoadProfiles().Select(p => p.Name))})";

        lock (_gate)
        {
            _assignments[botName.ToLowerInvariant()] = profile.Name;
            SaveAssignments();
        }

        var bot = FindOnlineBot(botName);
        if (bot == null)
        {
            _logger.LogInformation("[ROTATION] '{Profile}' assigned to {Bot} (offline — pushes on next HELLO)", profile.Name, botName);
            return $"assigned '{profile.Name}' to {botName} (offline — pushes on next login)";
        }

        await PushAsync(bot.Value.guid, bot.Value.name, profile);
        return $"assigned '{profile.Name}' to {botName} and pushed ({profile.Instructions.Count} instructions)";
    }

    /// <summary>Clear a bot's assignment; if online, clears the live slate too (vanilla resumes).</summary>
    public async Task<string> ClearAsync(string botName)
    {
        bool had;
        lock (_gate)
        {
            had = _assignments.Remove(botName.ToLowerInvariant());
            if (had) SaveAssignments();
        }

        var bot = FindOnlineBot(botName);
        if (bot != null)
            await _bridge.SendToBotAsync(bot.Value.guid, "LOAD_ROTATION", new { profile = "", data = "" });

        _logger.LogInformation("[ROTATION] {Bot}: assignment cleared (had={Had}, online={Online})", botName, had, bot != null);
        return had ? $"cleared {botName} — vanilla class AI resumes" : $"{botName} had no assignment";
    }

    /// <summary>
    /// HELLO hook (called by BotBridgeService): re-push the persisted assignment so restarts
    /// and relogs never silently lose the slate. Fire-and-forget from the bridge's side.
    /// </summary>
    public async Task OnBotHelloAsync(int guid, string name)
    {
        string? profileName;
        lock (_gate)
            _assignments.TryGetValue(name.ToLowerInvariant(), out profileName);
        if (profileName == null)
            return;

        var profile = FindProfile(profileName);
        if (profile == null)
        {
            _logger.LogWarning("[ROTATION] {Bot} is assigned '{Profile}' but no such profile file exists — nothing pushed", name, profileName);
            return;
        }
        await PushAsync(guid, name, profile);
    }

    // --------------------------------------------------------------- internals

    private async Task PushAsync(int guid, string name, RotationProfile profile)
    {
        string data = BuildWireData(profile);
        await _bridge.SendToBotAsync(guid, "LOAD_ROTATION", new { profile = profile.Name, data });
        _logger.LogInformation("[ROTATION] pushed '{Profile}' to {Bot} (guid={Guid}, {Count} instructions) — watch for ROTATION_ACK",
            profile.Name, name, guid, profile.Instructions.Count);
    }

    private (int guid, string name)? FindOnlineBot(string botName)
    {
        foreach (var kvp in _bridge.BotStates)
            if (string.Equals(kvp.Value.Name, botName, StringComparison.OrdinalIgnoreCase))
                return (kvp.Key, kvp.Value.Name);
        return null;
    }

    private void LoadAssignments()
    {
        try
        {
            if (File.Exists(_assignmentsPath))
                _assignments = JsonSerializer.Deserialize<Dictionary<string, string>>(
                    File.ReadAllText(_assignmentsPath), JsonOpts) ?? new();
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[ROTATION] assignments.json failed to parse ({Err}) — starting empty", ex.Message);
            _assignments = new();
        }
    }

    private void SaveAssignments()
    {
        try
        {
            File.WriteAllText(_assignmentsPath, JsonSerializer.Serialize(_assignments, JsonOpts));
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[ROTATION] failed to save assignments.json: {Err}", ex.Message);
        }
    }
}
