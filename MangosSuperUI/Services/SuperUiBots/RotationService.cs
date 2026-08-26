using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;
using System.Text.RegularExpressions;

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
    private const int MaxProfileIdUtf8Bytes = 63;
    private const int MaxWireDataUtf8Bytes = 2047;
    private const int MaxInstructions = 64;
    private static readonly Regex SafeProfileId = new(
        "^[A-Za-z0-9][A-Za-z0-9_.-]{0,62}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly BotBridgeService _bridge;
    private readonly ILogger<RotationService> _logger;
    private readonly string _dir;
    private readonly string _assignmentsPath;
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<BotConnection, HelloHydrationRegistration> _helloHydrations
        = new(ReferenceEqualityComparer.Instance);
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _assignmentGates = new();

    // bot name (case-insensitive) -> profile name. Persisted; pushed on HELLO.
    private Dictionary<string, string> _assignments = new(StringComparer.OrdinalIgnoreCase);

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
        /// <summary>
        /// Required for assignments made through the combat-loadout API. Legacy
        /// profiles without compatibility metadata remain readable by the old
        /// curl endpoints, but cannot be selected for a destructive build change.
        /// </summary>
        public int ClassId { get; set; }
        public int[] AllowedSpecTabs { get; set; } = Array.Empty<int>();
        public int[] AllowedRoles { get; set; } = Array.Empty<int>();
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

    public sealed record PreparedRotation(
        string Name,
        string? Description,
        string WireData,
        int InstructionCount,
        int ClassId,
        IReadOnlyList<int> AllowedSpecTabs,
        IReadOnlyList<int> AllowedRoles);

    public sealed record RotationProfileSummary(
        string Name,
        string? Description,
        int InstructionCount,
        int ClassId,
        IReadOnlyList<int> AllowedSpecTabs,
        IReadOnlyList<int> AllowedRoles);

    /// <summary>
    /// Two-phase HELLO replay token. The bridge registers it before publishing the
    /// connection, then starts it immediately afterward. Identity is the concrete
    /// socket, never merely the bot guid.
    /// </summary>
    public sealed class HelloHydrationRegistration
    {
        internal HelloHydrationRegistration(BotConnection connection, string name)
        {
            Connection = connection;
            Name = name;
        }

        public BotConnection Connection { get; }
        public string Name { get; }
        internal TaskCompletionSource<bool> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Started;
    }

    private sealed class AssignmentGateReleaser : IDisposable
    {
        private SemaphoreSlim? _gate;

        public AssignmentGateReleaser(SemaphoreSlim gate) => _gate = gate;

        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
    }

    public sealed class RotationValidationException : Exception
    {
        public RotationValidationException(string code, string message) : base(message)
        {
            Code = code;
        }

        public string Code { get; }
    }

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
                if (p == null || string.IsNullOrWhiteSpace(p.Name) || p.Instructions == null || p.Instructions.Count == 0)
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

    /// <summary>
    /// Validate and serialize one custom rotation before the core is asked to
    /// mutate talents. This is deliberately stricter than the legacy assign API:
    /// a loadout change may not destroy a build and only then discover that the
    /// replacement slate was malformed or intended for another class/spec/role.
    /// </summary>
    public PreparedRotation PrepareForBot(string profileName, int classId, int specTab, int activeRole)
    {
        string requested = (profileName ?? "").Trim();
        if (requested.Length == 0)
            throw new RotationValidationException("rotation_profile_required", "A custom rotation profile is required.");

        var matches = LoadProfiles()
            .Where(p => string.Equals(p.Name, requested, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length == 0)
            throw new RotationValidationException("rotation_profile_not_found", $"Rotation profile '{requested}' was not found.");
        if (matches.Length > 1)
            throw new RotationValidationException("rotation_profile_ambiguous", $"Rotation profile id '{requested}' is defined more than once.");
        var profile = matches[0];

        string name = profile.Name.Trim();
        if (!SafeProfileId.IsMatch(name) || Encoding.UTF8.GetByteCount(name) > MaxProfileIdUtf8Bytes)
            throw new RotationValidationException("rotation_profile_id_invalid",
                "Rotation profile ids must be 1-63 ASCII letters, digits, dots, underscores, or hyphens.");
        if (profile.ClassId != classId)
            throw new RotationValidationException("rotation_class_mismatch",
                $"Rotation '{name}' is for class {profile.ClassId}, not class {classId}.");
        int[] allowedSpecTabs = profile.AllowedSpecTabs ?? Array.Empty<int>();
        int[] allowedRoles = profile.AllowedRoles ?? Array.Empty<int>();
        if (allowedSpecTabs.Length == 0 || !allowedSpecTabs.Contains(specTab))
            throw new RotationValidationException("rotation_spec_mismatch",
                $"Rotation '{name}' does not allow specialization slot {specTab}.");
        if (allowedRoles.Length == 0 || !allowedRoles.Contains(activeRole))
            throw new RotationValidationException("rotation_role_mismatch",
                $"Rotation '{name}' does not allow combat role {activeRole}.");
        if (profile.Instructions.Count == 0 || profile.Instructions.Count > MaxInstructions)
            throw new RotationValidationException("rotation_instruction_count_invalid",
                $"Rotation '{name}' must contain between 1 and {MaxInstructions} instructions.");

        for (int index = 0; index < profile.Instructions.Count; index++)
        {
            var instruction = profile.Instructions[index];
            string label = $"Rotation '{name}' instruction {index + 1}";
            if (instruction.SpellId == 0)
                throw new RotationValidationException("rotation_spell_invalid", $"{label} has no spell id.");
            if (instruction.Priority is < 0 or > 10000)
                throw new RotationValidationException("rotation_priority_invalid", $"{label} has priority outside 0-10000.");
            if (instruction.HpMin is < 0 or > 100 || instruction.HpMax is < 0 or > 100 || instruction.HpMin > instruction.HpMax)
                throw new RotationValidationException("rotation_health_window_invalid", $"{label} has an invalid health window.");

            string target = (instruction.Target ?? "").Trim().ToUpperInvariant();
            if (target is not ("SELF" or "CURRENT_TARGET" or "LOWEST_HP_PARTY"))
                throw new RotationValidationException("rotation_target_invalid", $"{label} has unsupported target '{instruction.Target}'.");
        }

        string data = BuildWireData(profile);
        if (Encoding.UTF8.GetByteCount(data) > MaxWireDataUtf8Bytes)
            throw new RotationValidationException("rotation_payload_too_large",
                $"Rotation '{name}' exceeds the bridge payload limit of {MaxWireDataUtf8Bytes} UTF-8 bytes.");

        return new PreparedRotation(
            name,
            profile.Description,
            data,
            profile.Instructions.Count,
            profile.ClassId,
            allowedSpecTabs,
            allowedRoles);
    }

    public IReadOnlyList<RotationProfileSummary> GetProfileSummaries()
        => LoadProfiles()
            .Select(p => new RotationProfileSummary(
                p.Name,
                p.Description,
                p.Instructions.Count,
                p.ClassId,
                p.AllowedSpecTabs ?? Array.Empty<int>(),
                p.AllowedRoles ?? Array.Empty<int>()))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    /// <summary>The pipe payload BridgeHandleLoadRotation parses — pre-sorted by priority.</summary>
    private static string BuildWireData(RotationProfile profile)
        => string.Join("|", profile.Instructions
            .OrderBy(i => i.Priority)
            .Select(i => $"{i.SpellId}:{i.Priority}:{TargetToWire(i.Target)}:{i.HpMin}:{i.HpMax}:{i.Aura}:{(i.AuraPresent ? 1 : 0)}"));

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

    public bool TryGetAssignment(string botName, out string profileName)
    {
        lock (_gate)
        {
            LoadAssignments();
            return _assignments.TryGetValue(botName.Trim(), out profileName!);
        }
    }

    /// <summary>
    /// Persist an assignment after APPLY_COMBAT_LOADOUT has already been
    /// acknowledged. It intentionally does not send LOAD_ROTATION again.
    /// </summary>
    public void CommitAssignmentWithoutPush(string botName, string profileName)
    {
        string bot = (botName ?? "").Trim();
        string profile = (profileName ?? "").Trim();
        if (bot.Length == 0)
            throw new ArgumentException("A bot name is required.", nameof(botName));
        if (!SafeProfileId.IsMatch(profile))
            throw new ArgumentException("The rotation profile id is invalid.", nameof(profileName));

        lock (_gate)
        {
            LoadAssignments(throwOnError: true);
            var before = new Dictionary<string, string>(_assignments, StringComparer.OrdinalIgnoreCase);
            _assignments[bot] = profile;
            try
            {
                SaveAssignments();
            }
            catch
            {
                _assignments = before;
                throw;
            }
        }
    }

    /// <summary>
    /// Remove the persisted override after the core has acknowledged its return
    /// to the built-in spec rotation. No second bridge command is emitted.
    /// </summary>
    public bool ClearAssignmentWithoutPush(string botName)
    {
        string bot = (botName ?? "").Trim();
        if (bot.Length == 0)
            throw new ArgumentException("A bot name is required.", nameof(botName));

        lock (_gate)
        {
            LoadAssignments(throwOnError: true);
            var before = new Dictionary<string, string>(_assignments, StringComparer.OrdinalIgnoreCase);
            bool removed = _assignments.Remove(bot);
            if (!removed)
                return false;
            try
            {
                SaveAssignments();
                return true;
            }
            catch
            {
                _assignments = before;
                throw;
            }
        }
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
            LoadAssignments(throwOnError: true);
            var before = new Dictionary<string, string>(_assignments, StringComparer.OrdinalIgnoreCase);
            _assignments[botName.Trim()] = profile.Name;
            try
            {
                SaveAssignments();
            }
            catch
            {
                _assignments = before;
                throw;
            }
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
            LoadAssignments(throwOnError: true);
            var before = new Dictionary<string, string>(_assignments, StringComparer.OrdinalIgnoreCase);
            had = _assignments.Remove(botName.Trim());
            if (had)
            {
                try
                {
                    SaveAssignments();
                }
                catch
                {
                    _assignments = before;
                    throw;
                }
            }
        }

        var bot = FindOnlineBot(botName);
        if (bot != null)
            await _bridge.SendToBotAsync(bot.Value.guid, "LOAD_ROTATION", new { profile = "", data = "" });

        _logger.LogInformation("[ROTATION] {Bot}: assignment cleared (had={Had}, online={Online})", botName, had, bot != null);
        return had ? $"cleared {botName} — vanilla class AI resumes" : $"{botName} had no assignment";
    }

    /// <summary>
    /// Register a HELLO replay synchronously before the bridge publishes the
    /// connection. Registration intentionally performs no file or socket I/O.
    /// </summary>
    public HelloHydrationRegistration RegisterHelloHydration(
        BotConnection connection,
        string name)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var registration = new HelloHydrationRegistration(connection, name?.Trim() ?? "");
        _helloHydrations[connection] = registration;
        return registration;
    }

    /// <summary>Start a previously registered exact-connection replay once.</summary>
    public void StartHelloHydration(HelloHydrationRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        if (Interlocked.Exchange(ref registration.Started, 1) != 0)
            return;
        _ = CompleteHelloHydrationAsync(registration);
    }

    /// <summary>
    /// Serialize persisted-assignment replay with the ACK-to-assignment-commit
    /// window of an atomic combat loadout. Callers must acquire this only after
    /// awaiting any existing HELLO hydration for their captured connection.
    /// </summary>
    public async Task<IDisposable> AcquireAssignmentGateAsync(
        int guid,
        CancellationToken cancellationToken)
    {
        SemaphoreSlim gate = _assignmentGates.GetOrAdd(guid, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new AssignmentGateReleaser(gate);
    }

    /// <summary>
    /// Wait until the persisted HELLO rotation replay has either been written or
    /// failed. Combat-loadout writes call this so an older fire-and-forget replay
    /// can never arrive after and overwrite the newly selected rotation.
    /// </summary>
    public async Task WaitForHelloHydrationAsync(
        BotConnection connection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (_helloHydrations.TryGetValue(connection, out HelloHydrationRegistration? hydration))
            await hydration.Completion.Task.WaitAsync(cancellationToken);
    }

    private async Task CompleteHelloHydrationAsync(HelloHydrationRegistration registration)
    {
        try
        {
            using (await AcquireAssignmentGateAsync(
                registration.Connection.Guid,
                CancellationToken.None))
            {
                await HydrateBotHelloAsync(registration);
            }
        }
        catch (Exception ex)
        {
            // A failed replay must be observed, but it must also settle so a new
            // atomic loadout can safely replace it rather than waiting forever.
            _logger.LogError(ex,
                "[ROTATION] persisted HELLO rotation replay failed for {Bot} (guid={Guid})",
                registration.Name, registration.Connection.Guid);
        }
        finally
        {
            registration.Completion.TrySetResult(true);
            _helloHydrations.TryRemove(registration.Connection, out _);
        }
    }

    private async Task HydrateBotHelloAsync(HelloHydrationRegistration registration)
    {
        string name = registration.Name;
        string? profileName;
        lock (_gate)
        {
            LoadAssignments();
            _assignments.TryGetValue(name.Trim(), out profileName);
        }
        if (profileName == null)
            return;

        var profile = FindProfile(profileName);
        if (profile == null)
        {
            _logger.LogWarning("[ROTATION] {Bot} is assigned '{Profile}' but no such profile file exists — nothing pushed", name, profileName);
            return;
        }
        await PushAsync(registration.Connection, name, profile);
    }

    // --------------------------------------------------------------- internals

    private async Task PushAsync(int guid, string name, RotationProfile profile)
    {
        string data = BuildWireData(profile);
        await _bridge.SendToBotAsync(guid, "LOAD_ROTATION", new { profile = profile.Name, data });
        _logger.LogInformation("[ROTATION] pushed '{Profile}' to {Bot} (guid={Guid}, {Count} instructions) — watch for ROTATION_ACK",
            profile.Name, name, guid, profile.Instructions.Count);
    }

    private async Task PushAsync(BotConnection connection, string name, RotationProfile profile)
    {
        string data = BuildWireData(profile);
        await _bridge.SendToBotConnectionAsync(
            connection,
            "LOAD_ROTATION",
            new { profile = profile.Name, data },
            CancellationToken.None);
        _logger.LogInformation(
            "[ROTATION] replayed '{Profile}' to {Bot} (guid={Guid}, {Count} instructions) on its exact HELLO connection — watch for ROTATION_ACK",
            profile.Name, name, connection.Guid, profile.Instructions.Count);
    }

    private (int guid, string name)? FindOnlineBot(string botName)
    {
        foreach (var kvp in _bridge.Connections)
            if (string.Equals(kvp.Value.State.Name, botName, StringComparison.OrdinalIgnoreCase))
                return (kvp.Key, kvp.Value.State.Name);
        return null;
    }

    private void LoadAssignments(bool throwOnError = false)
    {
        try
        {
            var loaded = File.Exists(_assignmentsPath)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(
                    File.ReadAllText(_assignmentsPath), JsonOpts)
                : null;

            _assignments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var assignment in loaded ?? new Dictionary<string, string>())
            {
                var botName = assignment.Key.Trim();
                var profileName = assignment.Value?.Trim();
                if (botName.Length > 0 && !string.IsNullOrWhiteSpace(profileName))
                    _assignments[botName] = profileName;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[ROTATION] assignments.json failed to parse ({Err}) — starting empty", ex.Message);
            _assignments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (throwOnError)
                throw new IOException($"Could not load rotation assignments from '{_assignmentsPath}'.", ex);
        }
    }

    private void SaveAssignments()
    {
        var tempPath = Path.Combine(
            Path.GetDirectoryName(_assignmentsPath) ?? _dir,
            $".{Path.GetFileName(_assignmentsPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            var ordered = _assignments
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            File.WriteAllText(tempPath, JsonSerializer.Serialize(ordered, JsonOpts));
            File.Move(tempPath, _assignmentsPath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[ROTATION] failed to save assignments.json: {Err}", ex.Message);
            throw new IOException($"Could not save rotation assignments to '{_assignmentsPath}'.", ex);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[ROTATION] could not remove temporary assignment file '{Path}'", tempPath);
            }
        }
    }
}
