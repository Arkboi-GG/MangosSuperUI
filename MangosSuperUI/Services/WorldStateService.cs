using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Options;
using MangosSuperUI.Models;

namespace MangosSuperUI.Services;

/// <summary>
/// Owns the world lifecycle: which world is mounted right now, which ones are parked,
/// and the suspend/resume machinery that swaps one for another.
///
/// A "world" is the whole realm bundle — mangos + vmangos_admin (world), characters +
/// realmd (players), and src/ + sql/ + mangosd.conf (core). Suspending unloads it:
/// the server stops, every group is dumped, and nothing is mounted until a world is
/// resumed. Resuming while another world is live performs a swap — the live world is
/// suspended first, then the target is mounted in its place.
///
/// The registry lives on disk (worlds.json under the backup root), NOT in vmangos_admin.
/// vmangos_admin is itself part of the world bundle and gets swapped out, so a registry
/// stored there would be clobbered by the first resume.
///
/// Singleton — long-running operations are single-flight and exposed as a pollable job.
/// </summary>
public class WorldStateService
{
    private const string ManagedRtsSettingPredicate =
        "`key`='mode' OR `key`='state.flush_ms' OR `key` LIKE 'rate.%' OR " +
        "`key` LIKE 'bots.cap.%' OR `key` LIKE 'honor.weight.%' OR " +
        "`key` IN ('honor.enabled','honor.suppress_bot_hk','control.faction_bots','hero.enabled','hero.slots_fixed')";

    private readonly ConnectionFactory _db;
    private readonly ProcessManagerService _proc;
    private readonly IOptionsMonitor<VmangosSettings> _settings;
    private readonly IConfiguration _config;
    private readonly ILogger<WorldStateService> _logger;
    private readonly WorldArtifactService _artifacts;
    private readonly RtsWorldCreationService _rtsWorlds;
    private readonly DbInitializationService _dbInitialization;
    private readonly WorldMaintenanceGate _worldMaintenance;

    private readonly SemaphoreSlim _registryLock = new(1, 1);
    private readonly SemaphoreSlim _jobLock = new(1, 1);

    private WorldJob? _currentJob;

    private static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Every group a world bundle is made of, in dump/restore order.</summary>
    public static readonly string[] AllGroups = { "world", "players", "core" };

    public WorldStateService(
        ConnectionFactory db,
        ProcessManagerService proc,
        IOptionsMonitor<VmangosSettings> settings,
        IConfiguration config,
        WorldArtifactService artifacts,
        RtsWorldCreationService rtsWorlds,
        DbInitializationService dbInitialization,
        WorldMaintenanceGate worldMaintenance,
        ILogger<WorldStateService> logger)
    {
        _db = db;
        _proc = proc;
        _settings = settings;
        _config = config;
        _artifacts = artifacts;
        _rtsWorlds = rtsWorlds;
        _dbInitialization = dbInitialization;
        _worldMaintenance = worldMaintenance;
        _logger = logger;
    }

    private VmangosSettings Settings => _settings.CurrentValue;
    private string WorldsRoot => string.IsNullOrWhiteSpace(Settings.BackupDirectory) ? "/home/wowvmangos/backups" : Settings.BackupDirectory;
    private string SourceRoot => string.IsNullOrWhiteSpace(Settings.VmangosSourcePath) ? "/home/wowvmangos/vmangos/src" : Settings.VmangosSourcePath;
    private string SqlRoot => string.IsNullOrWhiteSpace(Settings.VmangosSqlPath) ? "/home/wowvmangos/vmangos/sql" : Settings.VmangosSqlPath;
    private string MangosdConfPath => string.IsNullOrWhiteSpace(Settings.MangosdConfPath)
        ? "/home/wowvmangos/vmangos/run/etc/mangosd.conf"
        : Settings.MangosdConfPath;
    private string RotationAssignmentsPath
    {
        get
        {
            var configured = _config["Rotations:Path"] ?? "Rotations";
            var directory = Path.IsPathRooted(configured)
                ? configured
                : Path.Combine(Directory.GetCurrentDirectory(), configured);
            return Path.Combine(directory, "assignments.json");
        }
    }
    private string RegistryPath => Path.Combine(WorldsRoot, "worlds.json");

    // ==================================================================
    //  REGISTRY
    // ==================================================================

    /// <summary>
    /// Loads the registry, bootstrapping it on first run. Bootstrap is non-destructive:
    /// whatever is currently in the databases becomes a world, and any pre-existing
    /// backup folders are adopted into an archive world so nothing is orphaned.
    /// </summary>
    public async Task<WorldRegistry> GetRegistryAsync()
    {
        await _registryLock.WaitAsync();
        try
        {
            return await LoadUnlockedAsync();
        }
        finally
        {
            _registryLock.Release();
        }
    }

    private async Task<WorldRegistry> LoadUnlockedAsync()
    {
        Directory.CreateDirectory(WorldsRoot);

        WorldRegistry? registry = null;
        if (File.Exists(RegistryPath))
        {
            try
            {
                registry = JsonSerializer.Deserialize<WorldRegistry>(await File.ReadAllTextAsync(RegistryPath), _json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "worlds.json is unreadable — rebuilding registry from disk");
            }
        }

        if (registry == null)
        {
            registry = Bootstrap();
            await SaveUnlockedAsync(registry);
        }
        else if (AdoptOrphanFolders(registry))
        {
            await SaveUnlockedAsync(registry);
        }

        return registry;
    }

    /// <summary>
    /// First-run registry. The live databases become a world in their own right, and any
    /// folders left behind by the old backup page are parked in an archive world.
    /// </summary>
    private WorldRegistry Bootstrap()
    {
        var mangosdUp = SafeIsRunning();

        var current = new WorldRecord
        {
            Id = NewId(),
            Name = "Live Realm",
            Flavor = "mmo",
            Notes = "Adopted from the databases that were already mounted when world tracking was enabled.",
            CreatedUtc = DateTime.UtcNow,
            State = mangosdUp ? WorldState.Live : WorldState.Suspended,
            LiveSinceUtc = mangosdUp ? DateTime.UtcNow : null,
            SuspendedUtc = mangosdUp ? null : DateTime.UtcNow
        };

        var registry = new WorldRegistry
        {
            LiveWorldId = mangosdUp ? current.Id : null,
            // The databases hold this world's data whether or not the server is up. Tracking
            // that lets a resume of the same world skip the import entirely.
            MaterializedWorldId = current.Id,
            MaterializedSnapshot = null,
            Worlds = { current }
        };

        AdoptOrphanFolders(registry);
        return registry;
    }

    /// <summary>
    /// Pulls any snapshot folder on disk that no world claims into an archive world.
    /// Returns true if the registry changed.
    /// </summary>
    private bool AdoptOrphanFolders(WorldRegistry registry)
    {
        if (!Directory.Exists(WorldsRoot)) return false;

        var claimed = registry.Worlds
            .SelectMany(w => w.Snapshots)
            .Select(s => s.Folder)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var orphans = new List<WorldSnapshot>();

        foreach (var dir in new DirectoryInfo(WorldsRoot).GetDirectories().OrderBy(d => d.Name))
        {
            if (claimed.Contains(dir.Name)) continue;

            var manifestPath = Path.Combine(dir.FullName, "manifest.json");
            if (!File.Exists(manifestPath)) continue;

            try
            {
                var manifest = JsonSerializer.Deserialize<SnapshotManifest>(File.ReadAllText(manifestPath), _json);
                orphans.Add(new WorldSnapshot
                {
                    SchemaVersion = manifest?.SchemaVersion ?? 1,
                    Folder = dir.Name,
                    TakenUtc = manifest?.Timestamp ?? dir.CreationTimeUtc,
                    Kind = manifest?.Kind ?? (dir.Name.Contains("_pre-restore", StringComparison.OrdinalIgnoreCase)
                        ? SnapshotKind.Safety
                        : SnapshotKind.Legacy),
                    Label = manifest?.Label ?? "",
                    Groups = manifest?.Groups ?? Array.Empty<string>(),
                    Sizes = manifest?.Sizes ?? new(),
                    Stats = manifest?.Stats ?? new(),
                    TotalBytes = DirectorySize(dir),
                    Artifacts = manifest?.Artifacts ?? new(),
                    SourceWorldId = manifest?.SourceWorldId,
                    SourceSnapshot = manifest?.SourceSnapshot,
                    ProfileId = manifest?.ProfileId,
                    LaunchConfiguration = manifest?.LaunchConfiguration,
                    NamePoolSha256 = manifest?.NamePoolSha256,
                    NamePoolEligible = manifest?.NamePoolEligible
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Skipping unreadable manifest in {Dir}", dir.Name);
            }
        }

        if (orphans.Count == 0) return false;

        var archive = registry.Worlds.FirstOrDefault(w => w.IsArchive);
        if (archive == null)
        {
            archive = new WorldRecord
            {
                Id = NewId(),
                Name = "Archived Snapshots",
                Flavor = "archive",
                Notes = "Snapshots taken before worlds had identity. Resume one to promote it into a world of its own.",
                CreatedUtc = DateTime.UtcNow,
                State = WorldState.Archived,
                IsArchive = true
            };
            registry.Worlds.Add(archive);
        }

        archive.Snapshots.AddRange(orphans);
        archive.Snapshots.Sort((a, b) => b.TakenUtc.CompareTo(a.TakenUtc));

        _logger.LogInformation("Adopted {Count} unclaimed snapshot folder(s) into '{World}'", orphans.Count, archive.Name);
        return true;
    }

    private async Task SaveUnlockedAsync(WorldRegistry registry)
    {
        Directory.CreateDirectory(WorldsRoot);
        registry.SchemaVersion = Math.Max(registry.SchemaVersion, 2);
        // Write-then-move so a crash mid-write can't leave a truncated registry.
        var tmp = RegistryPath + ".tmp";
        await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(registry, _json));
        File.Move(tmp, RegistryPath, overwrite: true);
    }

    /// <summary>Read-modify-write the registry under lock.</summary>
    private async Task<T> MutateAsync<T>(Func<WorldRegistry, T> mutate)
    {
        await _registryLock.WaitAsync();
        try
        {
            var registry = await LoadUnlockedAsync();
            var result = mutate(registry);
            await SaveUnlockedAsync(registry);
            return result;
        }
        finally
        {
            _registryLock.Release();
        }
    }

    // ==================================================================
    //  STATUS
    // ==================================================================

    /// <summary>
    /// Everything the World State page needs for its opening frame: which world is
    /// mounted, whether the processes backing it are actually up, and live counts.
    /// </summary>
    public async Task<object> GetStatusAsync()
    {
        var registry = await GetRegistryAsync();
        var live = registry.Worlds.FirstOrDefault(w => w.Id == registry.LiveWorldId);

        var mangosd = _proc.GetMangosdStatus();
        var realmd = _proc.GetRealmdStatus();

        return new
        {
            liveWorldId = registry.LiveWorldId,
            liveWorld = live,
            materializedWorldId = registry.MaterializedWorldId,
            worlds = registry.Worlds,
            mangosdRunning = mangosd.IsRunning,
            realmdRunning = realmd.IsRunning,
            uptimeSeconds = mangosd.Uptime?.TotalSeconds,
            // A world can be mounted-but-down (crashed, or stopped outside the panel).
            // The banner needs to say so rather than claiming everything is fine.
            stalled = live != null && !mangosd.IsRunning,
            job = _currentJob,
            stats = await GatherStatsAsync()
        };
    }

    /// <summary>Live headline counts, used both for the banner and for snapshot manifests.</summary>
    public async Task<Dictionary<string, object>> GatherStatsAsync()
    {
        var stats = new Dictionary<string, object>();
        try
        {
            using var mangos = _db.Mangos();
            stats["customItems"] = await mangos.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM item_template WHERE entry >= 900000 AND entry < 950000");
            stats["lootifierItems"] = await mangos.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM item_template WHERE entry >= 950000");
            stats["totalItems"] = await mangos.ExecuteScalarAsync<int>(
                "SELECT COUNT(DISTINCT entry) FROM item_template");

            using var admin = _db.Admin();
            stats["baselineInitialized"] = await admin.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM og_baseline_meta") > 0;
            stats["auditLogRows"] = await admin.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM audit_log");

            using var chars = _db.Characters();
            stats["totalCharacters"] = await chars.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM characters");

            using var realmd = _db.Realmd();
            stats["totalAccounts"] = await realmd.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM account");

            stats["sourceExists"] = Directory.Exists(SourceRoot);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to gather some world stats");
            stats["error"] = ex.Message;
        }
        return stats;
    }

    /// <summary>
    /// Performs every read-only check available before a resume can replace the
    /// materialized databases. Legacy snapshots are structurally checked but are
    /// explicitly reported as unhashed.
    /// </summary>
    public async Task<int> GetSnapshotRealmIdAsync(
        string snapshotFolder, CancellationToken cancellationToken = default)
    {
        var directory = ResolveSnapshotDirectory(snapshotFolder);
        return await _rtsWorlds.ReadSourceRealmIdAsync(directory, cancellationToken);
    }

    public Task<BotNamePoolStats> GetBotNamePoolStatsAsync(CancellationToken cancellationToken = default) =>
        _rtsWorlds.GetNamePoolStatsAsync(cancellationToken);

    public async Task<WorldRestorePreflight> PreflightResumeAsync(
        string worldId, string? snapshotFolder, bool forceFullRestore,
        CancellationToken cancellationToken = default)
    {
        var registry = await GetRegistryAsync();
        var target = registry.Worlds.FirstOrDefault(w => w.Id == worldId)
            ?? throw new InvalidOperationException("World not found.");
        var snapshot = snapshotFolder != null
            ? target.Snapshots.FirstOrDefault(s => s.Folder == snapshotFolder)
            : target.Snapshots.FirstOrDefault();
        var result = new WorldRestorePreflight
        {
            ForceFullRestore = forceFullRestore,
            AlreadyMaterialized = registry.MaterializedWorldId == target.Id &&
                (snapshot == null || registry.MaterializedSnapshot == snapshot.Folder),
            SchemaVersion = snapshot?.SchemaVersion ?? 0,
            MangosdRunning = _proc.GetMangosdStatus().IsRunning,
            RealmdRunning = _proc.GetRealmdStatus().IsRunning,
            SavedConfiguration = (snapshot?.LaunchConfiguration ?? target.LaunchConfiguration)?.Clone()
        };
        result.Strategy = result.AlreadyMaterialized && !forceFullRestore ? "instant" : "full-restore";

        if (string.Equals(target.Flavor, "rts", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var namePool = await _rtsWorlds.GetNamePoolStatsAsync(cancellationToken);
                result.NamePoolEligible = namePool.ValidUniqueNames;
                result.NamePoolSha256 = namePool.Sha256;
            }
            catch (Exception ex)
            {
                result.Blockers.Add("Bot name pool: " + ex.Message);
            }
        }

        if (snapshot == null)
        {
            if (!result.AlreadyMaterialized || forceFullRestore)
                result.Blockers.Add($"'{target.Name}' has no snapshot to restore.");
            result.Integrity = result.AlreadyMaterialized ? "materialized-only" : "missing";
            result.ConfigStatus = "not-captured";
            return result;
        }

        string directory;
        try { directory = ResolveSnapshotDirectory(snapshot.Folder); }
        catch (Exception ex)
        {
            result.Blockers.Add(ex.Message);
            return result;
        }
        if (!Directory.Exists(directory))
        {
            result.Blockers.Add($"Snapshot folder '{snapshot.Folder}' is missing from disk.");
            return result;
        }

        var manifestInspection = await InspectSnapshotManifestAsync(snapshot, directory, cancellationToken);
        result.Legacy = !manifestInspection.IsV2;
        result.SchemaVersion = result.Legacy
            ? 1
            : Math.Max(snapshot.SchemaVersion, manifestInspection.DiskManifest?.SchemaVersion ?? 0);
        result.Blockers.AddRange(manifestInspection.Errors);
        if (result.Legacy)
        {
            result.Integrity = "legacy-structural-only";
            result.Warnings.Add("Legacy v1 snapshot: gzip/tar structure can be checked, but no historical hashes exist.");
            var legacy = new[]
            {
                new SnapshotArtifact { Id="world-mangos", Group="world", FileName=WorldArtifactService.WorldMangos, Format="sql+gzip" },
                new SnapshotArtifact { Id="world-admin", Group="world", FileName=WorldArtifactService.WorldAdmin, Format="sql+gzip" },
                new SnapshotArtifact { Id="players-characters", Group="players", FileName=WorldArtifactService.PlayersCharacters, Format="sql+gzip" },
                new SnapshotArtifact { Id="players-realmd", Group="players", FileName=WorldArtifactService.PlayersRealmd, Format="sql+gzip" },
                new SnapshotArtifact { Id="core-source", Group="core", FileName=WorldArtifactService.CoreArchive, Format="tar+gzip" }
            };
            foreach (var artifact in legacy)
            {
                var check = await _artifacts.ValidateArtifactAsync(directory, artifact, cancellationToken);
                result.Artifacts.Add(check);
                if (!check.Valid) result.Blockers.Add($"{artifact.FileName}: {check.Detail}");
            }
        }
        else
        {
            result.Integrity = manifestInspection.Errors.Count == 0 ? "checksummed" : "invalid-manifest";
            foreach (var artifact in snapshot.Artifacts)
            {
                var check = await _artifacts.ValidateArtifactAsync(directory, artifact, cancellationToken);
                result.Artifacts.Add(check);
                // Optional means the artifact may be absent from both manifests. Once it
                // is listed with a checksum, a mismatch is still an integrity failure.
                if (!check.Valid)
                    result.Blockers.Add($"{artifact.FileName}: {check.Detail}");
            }
        }

        var tempDirectory = Path.Combine(Path.GetTempPath(), $"msui-world-preflight-{Guid.NewGuid():N}");
        try
        {
            string configPath;
            if (File.Exists(Path.Combine(directory, WorldArtifactService.CoreConfig)))
            {
                configPath = Path.Combine(directory, WorldArtifactService.CoreConfig);
                result.ConfigStatus = "v2 sidecar verified";
            }
            else
            {
                configPath = await _artifacts.ExtractLegacyConfigToTempAsync(
                    Path.Combine(directory, WorldArtifactService.CoreArchive), tempDirectory, cancellationToken);
                result.ConfigStatus = "legacy config safely extractable";
            }
            var config = await MangosdConfigDocument.LoadAsync(configPath, cancellationToken);
            var realmId = config.GetInt("RealmID");
            if (realmId is null or <= 0)
                throw new InvalidDataException("RealmID must be one positive integer.");
            result.ConfigValues["RealmID"] = realmId;
            result.ConfigValues["PlayerLimit"] = config.GetInt("PlayerLimit");
            result.ConfigValues["PlayerHardLimit"] = config.GetInt("PlayerHardLimit");
            result.ConfigValues["LoginPerTick"] = config.GetInt("LoginPerTick");
        }
        catch (Exception ex)
        {
            result.ConfigStatus = "invalid";
            result.Blockers.Add("mangosd.conf: " + ex.Message);
        }
        finally
        {
            if (Directory.Exists(tempDirectory)) Directory.Delete(tempDirectory, recursive: true);
        }

        result.RtsSourceEligible = !result.Legacy && result.Allowed &&
            File.Exists(Path.Combine(directory, WorldArtifactService.PlayersCharactersSchema)) &&
            File.Exists(Path.Combine(directory, WorldArtifactService.PlayersCharactersSystem)) &&
            File.Exists(Path.Combine(directory, WorldArtifactService.CoreConfig));
        if (result.RtsSourceEligible)
        {
            try
            {
                var definition = await _artifacts.InspectDatabaseDumpAsync(
                    Path.Combine(directory, WorldArtifactService.PlayersCharacters),
                    "characters", cancellationToken);
                if (definition == null)
                {
                    result.RtsSourceEligible = false;
                    result.Warnings.Add(
                        "This snapshot's characters dump predates self-contained schema charset/collation data. " +
                        "Restore and suspend it once with the updated World State system before creating an RTS world.");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result.RtsSourceEligible = false;
                result.Warnings.Add(
                    "The characters database preamble could not be inspected for RTS creation: " + ex.Message);
            }
        }
        if (!result.RtsSourceEligible)
        {
            if (!result.Warnings.Any(warning => warning.Contains("RTS world", StringComparison.OrdinalIgnoreCase)))
                result.Warnings.Add("This snapshot can be restored, but cannot seed a clean RTS world until it is restored and suspended once in v2 format.");
        }
        return result;
    }

    private async Task<SnapshotManifestInspection> InspectSnapshotManifestAsync(
        WorldSnapshot snapshot,
        string directory,
        CancellationToken cancellationToken = default)
    {
        var result = new SnapshotManifestInspection();
        snapshot.Artifacts ??= new List<SnapshotArtifact>();
        var registryClaimsV2 = snapshot.SchemaVersion >= 2 || snapshot.Artifacts.Count > 0;
        var manifestPath = Path.Combine(directory, "manifest.json");
        Exception? manifestError = null;

        if (File.Exists(manifestPath))
        {
            try
            {
                result.DiskManifest = JsonSerializer.Deserialize<SnapshotManifest>(
                    await File.ReadAllTextAsync(manifestPath, cancellationToken), _json)
                    ?? throw new InvalidDataException("manifest.json is empty.");
            }
            catch (Exception ex)
            {
                manifestError = ex;
            }
        }

        if (result.DiskManifest != null)
            result.DiskManifest.Artifacts ??= new List<SnapshotArtifact>();
        var diskClaimsV2 = result.DiskManifest != null &&
            (result.DiskManifest.SchemaVersion >= 2 || result.DiskManifest.Artifacts.Count > 0);
        var hasV2OnlyArtifacts = File.Exists(Path.Combine(directory, WorldArtifactService.CoreConfig)) ||
            File.Exists(Path.Combine(directory, WorldArtifactService.PlayersCharactersSchema)) ||
            File.Exists(Path.Combine(directory, WorldArtifactService.PlayersCharactersSystem));
        result.IsV2 = registryClaimsV2 || diskClaimsV2 || hasV2OnlyArtifacts;
        if (!result.IsV2) return result;

        if (snapshot.SchemaVersion < 2)
            result.Errors.Add("worlds.json claims legacy schema metadata for a v2 snapshot.");
        if (snapshot.Artifacts.Count == 0)
            result.Errors.Add("worlds.json has no v2 artifact metadata.");

        result.Errors.AddRange(WorldArtifactService.ValidateV2ArtifactMetadata(
            snapshot.Artifacts, "worlds.json"));

        if (!File.Exists(manifestPath))
        {
            result.Errors.Add("The v2 snapshot is missing its on-disk manifest.json.");
            return result;
        }
        if (manifestError != null)
        {
            result.Errors.Add("The v2 snapshot manifest.json is unreadable: " + manifestError.Message);
            return result;
        }

        var diskManifest = result.DiskManifest!;
        if (diskManifest.SchemaVersion < 2)
            result.Errors.Add("manifest.json does not declare snapshot schema v2.");
        if (diskManifest.SchemaVersion != snapshot.SchemaVersion)
            result.Errors.Add(
                $"Snapshot schema differs between worlds.json ({snapshot.SchemaVersion}) and manifest.json ({diskManifest.SchemaVersion}).");
        result.Errors.AddRange(WorldArtifactService.ValidateV2ArtifactMetadata(
            diskManifest.Artifacts, "manifest.json"));
        result.Errors.AddRange(WorldArtifactService.CompareV2ArtifactMetadata(
            snapshot.Artifacts, diskManifest.Artifacts));
        return result;
    }

    private async Task<string> ValidateSelectedGroupSnapshotAsync(
        WorldSnapshot snapshot,
        string directory,
        string group)
    {
        var inspection = await InspectSnapshotManifestAsync(snapshot, directory);
        if (inspection.Errors.Count > 0)
            throw new InvalidDataException("Snapshot manifest preflight failed: " + string.Join("; ", inspection.Errors));

        List<SnapshotArtifact> artifacts;
        if (inspection.IsV2)
        {
            artifacts = snapshot.Artifacts
                .Where(artifact => string.Equals(artifact.Group, group, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        else
        {
            artifacts = group.ToLowerInvariant() switch
            {
                "world" => new()
                {
                    new SnapshotArtifact { Id="world-mangos", Group="world", FileName=WorldArtifactService.WorldMangos, Format="sql+gzip" },
                    new SnapshotArtifact { Id="world-admin", Group="world", FileName=WorldArtifactService.WorldAdmin, Format="sql+gzip" }
                },
                "players" => new()
                {
                    new SnapshotArtifact { Id="players-characters", Group="players", FileName=WorldArtifactService.PlayersCharacters, Format="sql+gzip" },
                    new SnapshotArtifact { Id="players-realmd", Group="players", FileName=WorldArtifactService.PlayersRealmd, Format="sql+gzip" }
                },
                "core" => new()
                {
                    new SnapshotArtifact { Id="core-source", Group="core", FileName=WorldArtifactService.CoreArchive, Format="tar+gzip" }
                },
                _ => throw new InvalidOperationException("Unknown group: " + group)
            };
        }

        if (artifacts.Count == 0)
            throw new InvalidDataException($"Snapshot contains no '{group}' artifacts.");
        foreach (var artifact in artifacts)
        {
            var check = await _artifacts.ValidateArtifactAsync(directory, artifact);
            if (!check.Valid)
                throw new InvalidDataException($"{artifact.FileName}: {check.Detail}");
        }

        if (string.Equals(group, "core", StringComparison.OrdinalIgnoreCase))
        {
            var sidecar = Path.Combine(directory, WorldArtifactService.CoreConfig);
            var tempDirectory = Path.Combine(Path.GetTempPath(), $"msui-world-group-preflight-{Guid.NewGuid():N}");
            try
            {
                var configPath = File.Exists(sidecar)
                    ? sidecar
                    : await _artifacts.ExtractLegacyConfigToTempAsync(
                        Path.Combine(directory, WorldArtifactService.CoreArchive), tempDirectory);
                var config = await MangosdConfigDocument.LoadAsync(configPath);
                _ = config.GetInt("PlayerLimit");
            }
            finally
            {
                if (Directory.Exists(tempDirectory)) Directory.Delete(tempDirectory, recursive: true);
            }
        }

        return $"{artifacts.Count} {group} artifact(s) verified";
    }

    private sealed class SnapshotManifestInspection
    {
        public bool IsV2 { get; set; }
        public SnapshotManifest? DiskManifest { get; set; }
        public List<string> Errors { get; } = new();
    }

    // ==================================================================
    //  JOBS — long operations run in the background and are polled
    // ==================================================================

    public WorldJob? CurrentJob => _currentJob;

    /// <summary>
    /// Starts a job if none is running. Single-flight: suspend/resume/swap all mutate the
    /// same databases, so a second one running concurrently would corrupt both.
    /// </summary>
    private async Task<WorldJob> StartJobAsync(string kind, string title, List<WorldJobStep> steps, Func<WorldJob, Task> body)
    {
        await _jobLock.WaitAsync();
        try
        {
            if (_currentJob is { State: JobState.Running })
                throw new InvalidOperationException($"'{_currentJob.Title}' is still running — wait for it to finish.");

            var job = new WorldJob
            {
                Id = NewId(),
                Kind = kind,
                Title = title,
                Steps = steps,
                State = JobState.Running,
                StartedUtc = DateTime.UtcNow
            };
            _currentJob = job;

            _ = Task.Run(async () =>
            {
                try
                {
                    await body(job);
                    job.State = JobState.Done;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "World job '{Title}' failed", title);
                    job.State = JobState.Failed;
                    job.Error = ex.Message;
                    foreach (var s in job.Steps.Where(s => s.State == StepState.Running))
                    {
                        s.State = StepState.Failed;
                        s.FinishedUtc = DateTime.UtcNow;
                    }
                }
                finally
                {
                    job.FinishedUtc = DateTime.UtcNow;
                }
            });

            return job;
        }
        finally
        {
            _jobLock.Release();
        }
    }

    private static WorldJobStep Step(string key, string label) => new() { Key = key, Label = label };

    private static async Task RunStep(WorldJob job, string key, Func<Task<string?>> action)
    {
        var step = job.Steps.FirstOrDefault(s => s.Key == key);
        if (step == null) return;

        step.State = StepState.Running;
        step.StartedUtc = DateTime.UtcNow;
        try
        {
            step.Detail = await action();
            step.State = StepState.Done;
        }
        catch
        {
            step.State = StepState.Failed;
            throw;
        }
        finally
        {
            step.FinishedUtc = DateTime.UtcNow;
        }
    }

    private static void SkipStep(WorldJob job, string key, string reason)
    {
        var step = job.Steps.FirstOrDefault(s => s.Key == key);
        if (step == null) return;
        step.State = StepState.Skipped;
        step.Detail = reason;
        step.FinishedUtc = DateTime.UtcNow;
    }

    // ==================================================================
    //  SUSPEND — unload the live world
    // ==================================================================

    /// <summary>
    /// Stops the server, dumps every group, and leaves nothing mounted. The databases
    /// still physically hold this world's data, which is what makes resuming it again
    /// instant (the import is skipped).
    /// </summary>
    public async Task<WorldJob> SuspendAsync(string? label, string? operatorIp)
    {
        var registry = await GetRegistryAsync();
        var live = registry.Worlds.FirstOrDefault(w => w.Id == registry.LiveWorldId)
            ?? throw new InvalidOperationException("No world is mounted — there is nothing to suspend.");

        var steps = new List<WorldJobStep>
        {
            Step("stop", $"Unload “{live.Name}” — stop mangosd & realmd"),
            Step("dump-world", "Freeze game world (mangos + vmangos_admin)"),
            Step("dump-players", "Freeze characters (characters + realmd)"),
            Step("dump-core", "Freeze core (src + sql + mangosd.conf)"),
            Step("park", "Park the world")
        };

        return await StartJobAsync("suspend", $"Suspending {live.Name}", steps, async job =>
        {
            var folder = NewSnapshotFolder("suspend");
            var dir = CreateStagingDirectory(job.Id, "suspend");
            try
            {

                await RunStep(job, "stop", async () => await StopServerAsync());

                var stats = await GatherStatsAsync();
                await RunStep(job, "dump-world", async () => await DumpGroupAsync("world", dir));
                await RunStep(job, "dump-players", async () => await DumpGroupAsync("players", dir));
                await RunStep(job, "dump-core", async () => await DumpGroupAsync("core", dir));

                await RunStep(job, "park", async () =>
                {
                    var snapshot = await WriteManifestAsync(dir, folder, SnapshotKind.Suspend, label ?? "", AllGroups, stats,
                        live.LaunchConfiguration);

                    await PublishSnapshotAsync(dir, folder, reg =>
                    {
                        var w = reg.Worlds.First(x => x.Id == live.Id);
                        w.Snapshots.Insert(0, snapshot);
                        w.State = WorldState.Suspended;
                        w.SuspendedUtc = DateTime.UtcNow;
                        w.LiveSinceUtc = null;
                        reg.LiveWorldId = null;
                        reg.MaterializedWorldId = w.Id;
                        reg.MaterializedSnapshot = folder;
                        return true;
                    });

                    job.ResultFolder = folder;
                    return FormatBytes(snapshot.TotalBytes) + " frozen";
                });
            }
            finally
            {
                CleanupStagingDirectory(dir);
            }
        });
    }

    // ==================================================================
    //  RESUME — mount a world, swapping out whatever is live
    // ==================================================================

    /// <summary>
    /// Mounts <paramref name="worldId"/>. If another world is live it is suspended first —
    /// that combined operation is the swap the UI narrates step by step.
    /// </summary>
    public async Task<WorldJob> ResumeAsync(
        string worldId,
        string? snapshotFolder,
        string? operatorIp,
        bool forceFullRestore = false,
        WorldLaunchConfiguration? requestedConfiguration = null)
    {
        var registry = await GetRegistryAsync();

        var target = registry.Worlds.FirstOrDefault(w => w.Id == worldId)
            ?? throw new InvalidOperationException("World not found.");
        if (target.Id == registry.LiveWorldId)
            throw new InvalidOperationException($"“{target.Name}” is already mounted.");

        // Newest snapshot unless the caller picked a specific one.
        var snapshot = snapshotFolder != null
            ? target.Snapshots.FirstOrDefault(s => s.Folder == snapshotFolder)
            : target.Snapshots.FirstOrDefault();

        WorldLaunchConfiguration? launchConfiguration = null;
        var isRts = string.Equals(target.Flavor, "rts", StringComparison.OrdinalIgnoreCase);
        if (isRts)
        {
            var sourceConfiguration = requestedConfiguration ?? snapshot?.LaunchConfiguration ?? target.LaunchConfiguration
                ?? throw new InvalidOperationException("The RTS world has no saved launch configuration.");
            launchConfiguration = WorldConfigurationCatalog.NormalizeAndValidate(sourceConfiguration);
        }
        else if (requestedConfiguration != null)
        {
            throw new InvalidOperationException("RTS launch configuration can only be applied to an RTS world.");
        }

        // RTS boot configuration is immutable once the core starts. Preparing the
        // selected profile therefore always uses a fresh, ephemeral restore input;
        // the parked snapshot itself is never edited and its runtime state is kept.
        var effectiveForceFullRestore = forceFullRestore || launchConfiguration != null;
        if (launchConfiguration != null && snapshot == null)
            throw new InvalidOperationException(
                "An RTS profile can only be loaded from a captured snapshot; suspend this world once to create one.");

        var outgoing = registry.Worlds.FirstOrDefault(w => w.Id == registry.LiveWorldId);

        // A world with no snapshot can only be resumed if its data is still sitting in the
        // databases — otherwise there is genuinely nothing to mount.
        var alreadyMaterialized = !effectiveForceFullRestore && registry.MaterializedWorldId == target.Id
            && (snapshot == null || registry.MaterializedSnapshot == snapshot.Folder);

        if (snapshot == null && !alreadyMaterialized)
            throw new InvalidOperationException($"“{target.Name}” has no snapshot to resume from.");

        var initialPreflight = await PreflightResumeAsync(target.Id, snapshot?.Folder, effectiveForceFullRestore);
        if (!initialPreflight.Allowed)
            throw new InvalidOperationException("Snapshot preflight failed: " + string.Join("; ", initialPreflight.Blockers));
        if (launchConfiguration != null)
            ValidateRtsLaunchConfigurationContract(launchConfiguration, initialPreflight);

        var steps = new List<WorldJobStep> { Step("preflight", "Verify snapshot artifacts and configuration") };
        if (outgoing != null)
        {
            steps.Add(Step("stop", $"Unload “{outgoing.Name}” — stop mangosd & realmd"));
            steps.Add(Step("dump-world", $"Freeze “{outgoing.Name}” game world"));
            steps.Add(Step("dump-players", $"Freeze “{outgoing.Name}” characters"));
            steps.Add(Step("dump-core", $"Freeze “{outgoing.Name}” core"));
            steps.Add(Step("park", $"Park “{outgoing.Name}”"));
        }
        else
        {
            steps.Add(Step("stop", "Make sure mangosd & realmd are stopped"));
        }

        steps.Add(Step("restore-world", $"Mount “{target.Name}” game world"));
        steps.Add(Step("restore-players", $"Mount “{target.Name}” characters"));
        steps.Add(Step("restore-core", $"Mount “{target.Name}” core"));
        steps.Add(Step("start", $"Boot “{target.Name}” — start realmd & mangosd"));

        var title = outgoing != null ? $"Swapping {outgoing.Name} → {target.Name}" : $"Resuming {target.Name}";

        if (launchConfiguration != null)
            steps.Insert(steps.Count - 1, Step("configure", "Apply RTS rules and launch configuration"));

        return await StartJobAsync(outgoing != null ? "swap" : "resume", title, steps, async job =>
        {
            await RunStep(job, "preflight", async () =>
            {
                var current = await PreflightResumeAsync(target.Id, snapshot?.Folder, effectiveForceFullRestore);
                if (!current.Allowed)
                    throw new InvalidOperationException(string.Join("; ", current.Blockers));
                if (launchConfiguration != null)
                    ValidateRtsLaunchConfigurationContract(launchConfiguration, current);
                return current.Legacy
                    ? "legacy structure verified (no historical hashes)"
                    : "SHA-256 and archive structure verified";
            });

            // ---- Phase 1: unload whatever is live ----
            await RunStep(job, "stop", async () => await StopServerAsync());

            if (outgoing != null)
            {
                var folder = NewSnapshotFolder("suspend");
                var dir = CreateStagingDirectory(job.Id, "auto-suspend");
                try
                {

                    var stats = await GatherStatsAsync();
                    await RunStep(job, "dump-world", async () => await DumpGroupAsync("world", dir));
                    await RunStep(job, "dump-players", async () => await DumpGroupAsync("players", dir));
                    await RunStep(job, "dump-core", async () => await DumpGroupAsync("core", dir));

                    await RunStep(job, "park", async () =>
                    {
                        var snap = await WriteManifestAsync(dir, folder, SnapshotKind.Suspend,
                            $"Auto-suspended to make room for {target.Name}", AllGroups, stats,
                            outgoing.LaunchConfiguration);

                        await PublishSnapshotAsync(dir, folder, reg =>
                        {
                            var w = reg.Worlds.First(x => x.Id == outgoing.Id);
                            w.Snapshots.Insert(0, snap);
                            w.State = WorldState.Suspended;
                            w.SuspendedUtc = DateTime.UtcNow;
                            w.LiveSinceUtc = null;
                            reg.LiveWorldId = null;
                            reg.MaterializedWorldId = w.Id;
                            reg.MaterializedSnapshot = folder;
                            return true;
                        });

                        return FormatBytes(snap.TotalBytes) + " frozen";
                    });
                }
                finally
                {
                    CleanupStagingDirectory(dir);
                }
            }

            // ---- Phase 2: mount the target ----
            if (alreadyMaterialized)
            {
                var reason = "Already in the databases — no import needed";
                SkipStep(job, "restore-world", reason);
                SkipStep(job, "restore-players", reason);
                SkipStep(job, "restore-core", reason);
            }
            else
            {
                // The next operation destructively replaces canonical schemas. Clear the
                // trusted marker first so any failure forces a complete recovery restore.
                await MutateAsync(reg =>
                {
                    reg.LiveWorldId = null;
                    reg.MaterializedWorldId = null;
                    reg.MaterializedSnapshot = null;
                    return true;
                });

                var dir = ResolveSnapshotDirectory(snapshot!.Folder);
                if (!Directory.Exists(dir))
                    throw new InvalidOperationException($"Snapshot folder '{snapshot.Folder}' is missing from disk.");

                string? preparedWorldDirectory = null;
                try
                {
                    if (launchConfiguration != null)
                    {
                        preparedWorldDirectory = await PrepareRtsWorldRestoreDirectoryAsync(
                            dir, launchConfiguration, job.Id);
                    }
                    await RestoreRequired(
                        job, "restore-world", "world", preparedWorldDirectory ?? dir);
                }
                finally
                {
                    if (preparedWorldDirectory != null)
                        CleanupStagingDirectory(preparedWorldDirectory);
                }
                await RestoreRequired(job, "restore-players", "players", dir);
                await RestoreRequired(job, "restore-core", "core", dir);

            }

            if (launchConfiguration != null)
            {
                await RunStep(job, "configure", async () =>
                {
                    await ApplyWorldLaunchConfigurationAsync(launchConfiguration);
                    await MutateAsync(reg =>
                    {
                        var world = reg.Worlds.First(x => x.Id == target.Id);
                        world.LaunchConfiguration = launchConfiguration.Clone();
                        return true;
                    });
                    return $"{launchConfiguration.ProfileId}; PlayerLimit {launchConfiguration.PlayerLimit:N0}; bots {launchConfiguration.AllianceBotCap:N0}A/{launchConfiguration.HordeBotCap:N0}H";
                });
            }

            // Only trust the materialization marker after both the snapshot restore and
            // any per-world launch configuration have completed. A configuration failure
            // must force the next attempt through a complete restore.
            if (!alreadyMaterialized)
            {
                await MutateAsync(reg =>
                {
                    reg.MaterializedWorldId = target.Id;
                    reg.MaterializedSnapshot = snapshot!.Folder;
                    return true;
                });
            }

            // ---- Phase 3: boot ----
            await RunStep(job, "start", async () =>
            {
                var started = await StartServerAndVerifyAsync();

                await MutateAsync(reg =>
                {
                    var w = reg.Worlds.First(x => x.Id == target.Id);
                    w.State = WorldState.Live;
                    w.LiveSinceUtc = DateTime.UtcNow;
                    w.SuspendedUtc = null;
                    // Resuming an archived snapshot promotes it out of the archive.
                    if (w.IsArchive) w.IsArchive = false;
                    reg.LiveWorldId = w.Id;
                    reg.MaterializedWorldId = w.Id;
                    reg.MaterializedSnapshot = snapshot?.Folder;
                    return true;
                });

                job.ResultFolder = snapshot?.Folder;
                return started;
            });
        });
    }

    private async Task RestoreRequired(WorldJob job, string stepKey, string group, string dir)
    {
        // A whole-world resume is all-or-nothing. Preflight has already required the
        // artifacts for every group, so never silently turn a stale/empty manifest
        // Groups list into a partial restore that is then marked live.
        await RunStep(job, stepKey, async () => await RestoreGroupAsync(group, dir));
    }

    private async Task<string> PrepareRtsWorldRestoreDirectoryAsync(
        string snapshotDirectory,
        WorldLaunchConfiguration configuration,
        string jobId)
    {
        var stagingDirectory = CreateStagingDirectory(jobId, "rts-world-profile");
        try
        {
            await _artifacts.TransformGzipAsync(
                Path.Combine(snapshotDirectory, WorldArtifactService.WorldMangos),
                Path.Combine(stagingDirectory, WorldArtifactService.WorldMangos),
                RtsHeroSpellWorldStore.BuildResumeArtifactPostlude(configuration));
            await _artifacts.CopyAtomicAsync(
                Path.Combine(snapshotDirectory, WorldArtifactService.WorldAdmin),
                Path.Combine(stagingDirectory, WorldArtifactService.WorldAdmin));
            await _artifacts.ValidateGzipAsync(
                Path.Combine(stagingDirectory, WorldArtifactService.WorldMangos));
            await _artifacts.ValidateGzipAsync(
                Path.Combine(stagingDirectory, WorldArtifactService.WorldAdmin));
            return stagingDirectory;
        }
        catch
        {
            CleanupStagingDirectory(stagingDirectory);
            throw;
        }
    }

    private async Task ApplyWorldLaunchConfigurationAsync(WorldLaunchConfiguration input)
    {
        var configuration = WorldConfigurationCatalog.NormalizeAndValidate(input);
        var expectedRows = WorldConfigurationCatalog.ToWorldStateRows(configuration);
        var expectedHeroRules = WorldConfigurationCatalog.ToHeroRuleRows(configuration);
        var namePool = await _rtsWorlds.GetNamePoolStatsAsync();
        WorldConfigurationCatalog.ValidateNamePoolCapacity(configuration, namePool.ValidUniqueNames);
        if (!File.Exists(MangosdConfPath))
            throw new FileNotFoundException("The restored mangosd.conf is missing.", MangosdConfPath);
        var configDocument = await MangosdConfigDocument.LoadAsync(MangosdConfPath);
        var restoredRealmId = configDocument.GetInt("RealmID");
        if (restoredRealmId is null or <= 0)
            throw new InvalidDataException("The restored mangosd.conf must contain one positive RealmID value.");
        if (restoredRealmId != configuration.RealmId)
            throw new InvalidDataException(
                $"RTS launch configuration RealmId {configuration.RealmId} does not match restored RealmID {restoredRealmId}.");
        configDocument.ApplyWorldConfiguration(configuration);
        await configDocument.SaveAtomicAsync(MangosdConfPath);
        var verifiedConfig = await MangosdConfigDocument.LoadAsync(MangosdConfPath);
        if (verifiedConfig.GetInt("RealmID") != configuration.RealmId ||
            verifiedConfig.GetInt("PlayerLimit") != configuration.PlayerLimit ||
            verifiedConfig.GetInt("PlayerHardLimit") != configuration.PlayerHardLimit ||
            verifiedConfig.GetInt("LoginPerTick") != configuration.LoginPerTick)
            throw new InvalidDataException("RTS mangosd.conf verification failed after atomic save.");

        using var characters = _db.Characters();
        await characters.OpenAsync();
        // RTS schema is created once by RtsWorldCreationService. A resume restores
        // that complete snapshot and updates managed rows only; it never self-heals
        // or upgrades schema here.
        using var transaction = await characters.BeginTransactionAsync();
        try
        {
            await characters.ExecuteAsync(
                "DELETE FROM `superui_worldstate` WHERE " + ManagedRtsSettingPredicate,
                transaction: transaction);
            foreach (var row in expectedRows)
            {
                await characters.ExecuteAsync(
                    "INSERT INTO `superui_worldstate` (`key`,`value`) VALUES (@key,@value) " +
                    "ON DUPLICATE KEY UPDATE `value`=VALUES(`value`)",
                    new { key = row.Key, value = row.Value }, transaction);
            }

            // These are rules, not runtime match state. Resume replaces the five
            // configured target levels but deliberately preserves faction Honor and
            // the persistent hero roster in superui_faction/superui_heroes.
            await characters.ExecuteAsync("DELETE FROM `superui_rules_hero`", transaction: transaction);
            foreach (var rule in expectedHeroRules)
            {
                await characters.ExecuteAsync(
                    "INSERT INTO `superui_rules_hero` (`hero_level`,`declare_cost`,`revive_fee`,`spell_id`,`scale_percent`,`damage_percent`) " +
                    "VALUES (@HeroLevel,@HonorCost,@ReviveFee,@SpellId,@ScalePercent,@DamagePercent)",
                    rule, transaction);
            }
            await characters.ExecuteAsync(
                "INSERT IGNORE INTO `superui_faction` (`team`,`honor_pool`) VALUES (0,0),(1,0)",
                transaction: transaction);

            var actualRows = (await characters.QueryAsync<WorldStateSettingRow>(
                "SELECT `key` AS `Key`, `value` AS `Value` FROM `superui_worldstate` " +
                "WHERE " + ManagedRtsSettingPredicate,
                transaction: transaction))
                .ToDictionary(row => row.Key, row => row.Value, StringComparer.OrdinalIgnoreCase);
            if (actualRows.Count != expectedRows.Count)
                throw new InvalidDataException("RTS worldstate verification found unexpected managed settings.");
            foreach (var expected in expectedRows)
            {
                if (!actualRows.TryGetValue(expected.Key, out var actual) ||
                    !string.Equals(actual, expected.Value, StringComparison.Ordinal))
                    throw new InvalidDataException($"RTS worldstate verification failed for '{expected.Key}'.");
            }

            var actualHeroRules = (await characters.QueryAsync<HeroRuleSettingRow>(
                "SELECT `hero_level` AS `HeroLevel`, `declare_cost` AS `HonorCost`, `revive_fee` AS `ReviveFee`, " +
                "`spell_id` AS `SpellId`, `scale_percent` AS `ScalePercent`, `damage_percent` AS `DamagePercent` " +
                "FROM `superui_rules_hero` ORDER BY `hero_level`",
                transaction: transaction)).ToArray();
            if (actualHeroRules.Length != expectedHeroRules.Count)
                throw new InvalidDataException("RTS hero-rule verification found an unexpected row count.");
            for (var index = 0; index < expectedHeroRules.Count; index++)
            {
                var expected = expectedHeroRules[index];
                var actual = actualHeroRules[index];
                if (actual.HeroLevel != expected.HeroLevel || actual.HonorCost != expected.HonorCost ||
                    actual.ReviveFee != expected.ReviveFee || actual.SpellId != expected.SpellId ||
                    actual.ScalePercent != expected.ScalePercent || actual.DamagePercent != expected.DamagePercent)
                    throw new InvalidDataException($"RTS hero-rule verification failed for level {expected.HeroLevel}.");
            }
            await ValidateRtsHeroSpellRowsAsync(configuration);
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private async Task ValidateRtsHeroSpellRowsAsync(WorldLaunchConfiguration configuration)
    {
        using var world = _db.Mangos();
        await world.OpenAsync();
        var rows = (await world.QueryAsync<RtsHeroSpellValidationRow>(
            @"SELECT `entry` AS `SpellId`,`build` AS `Build`,`attributes` AS `Attributes`,
                     `durationIndex` AS `DurationIndex`,`stackAmount` AS `StackAmount`,
                     `equippedItemClass` AS `EquippedItemClass`,
                     `equippedItemSubClassMask` AS `EquippedItemSubClassMask`,
                     `equippedItemInventoryTypeMask` AS `EquippedItemInventoryTypeMask`,
                     `effect1` AS `Effect1`,`effect2` AS `Effect2`,`effect3` AS `Effect3`,
                     `effectBaseDice1` AS `EffectBaseDice1`,`effectBaseDice2` AS `EffectBaseDice2`,
                     `effectDieSides1` AS `EffectDieSides1`,`effectDieSides2` AS `EffectDieSides2`,
                     `effectBasePoints1` AS `EffectBasePoints1`,`effectBasePoints2` AS `EffectBasePoints2`,
                     `effectImplicitTargetA1` AS `EffectImplicitTargetA1`,
                     `effectImplicitTargetA2` AS `EffectImplicitTargetA2`,
                     `effectImplicitTargetB1` AS `EffectImplicitTargetB1`,
                     `effectImplicitTargetB2` AS `EffectImplicitTargetB2`,
                     `effectApplyAuraName1` AS `EffectApplyAuraName1`,
                     `effectApplyAuraName2` AS `EffectApplyAuraName2`,
                     `effectMiscValue1` AS `EffectMiscValue1`,`effectMiscValue2` AS `EffectMiscValue2`,
                     `targets` AS `Targets`,`procFlags` AS `ProcFlags`,`procChance` AS `ProcChance`,
                     `procCharges` AS `ProcCharges`,`effectAmplitude1` AS `EffectAmplitude1`,
                     `effectAmplitude2` AS `EffectAmplitude2`,`effectTriggerSpell1` AS `EffectTriggerSpell1`,
                     `effectTriggerSpell2` AS `EffectTriggerSpell2`,`customFlags` AS `CustomFlags`
              FROM `spell_template` WHERE `entry` IN @Ids ORDER BY `entry`,`build`",
            new { Ids = RtsHeroSpellWorldStore.ReservedSpellIds })).ToArray();
        if (rows.Length != 5)
            throw new InvalidDataException(
                "RTS R2 requires exactly one build-5875 world spell row for each reserved ID 51001-51005.");

        var rules = configuration.HeroRules.OrderBy(rule => rule.SpellId).ToArray();
        for (var index = 0; index < rules.Length; index++)
        {
            var rule = rules[index];
            var row = rows[index];
            if (row.SpellId != rule.SpellId || row.Build != 5875 || row.Attributes != 0x80000040UL ||
                row.DurationIndex != 21 || row.StackAmount != 1 || row.EquippedItemClass != -1 ||
                row.EquippedItemSubClassMask != 0 || row.EquippedItemInventoryTypeMask != 0 ||
                row.Effect1 != 6 || row.Effect2 != 6 || row.Effect3 != 0 ||
                row.EffectBaseDice1 != 1 || row.EffectBaseDice2 != 1 ||
                row.EffectDieSides1 != 1 || row.EffectDieSides2 != 1 ||
                row.EffectBasePoints1 != rule.ScalePercent - 101 ||
                row.EffectBasePoints2 != rule.DamagePercent - 101 ||
                row.EffectImplicitTargetA1 != 1 || row.EffectImplicitTargetA2 != 1 ||
                row.EffectImplicitTargetB1 != 0 || row.EffectImplicitTargetB2 != 0 ||
                row.EffectApplyAuraName1 != 61 || row.EffectApplyAuraName2 != 79 ||
                row.EffectMiscValue1 != 0 || row.EffectMiscValue2 != 127 || row.Targets != 0 ||
                row.ProcFlags != 0 || row.ProcChance != 0 || row.ProcCharges != 0 ||
                row.EffectAmplitude1 != 0 || row.EffectAmplitude2 != 0 ||
                row.EffectTriggerSpell1 != 0 || row.EffectTriggerSpell2 != 0 || row.CustomFlags != 0)
                throw new InvalidDataException(
                    $"RTS R2 world spell {rule.SpellId} does not match its configured passive scale/damage aura contract.");
        }
    }

    /// <summary>
    /// Serializes user-facing registry CRUD against lifecycle job admission. Job
    /// bodies deliberately continue to call MutateAsync directly: they already own
    /// the admitted lifecycle slot and must not wait on this lock again.
    /// </summary>
    private async Task<T> RunCrudAsync<T>(Func<Task<T>> action)
    {
        await _jobLock.WaitAsync();
        try
        {
            if (_currentJob is { State: JobState.Running })
                throw new InvalidOperationException(
                    $"'{_currentJob.Title}' is still running — world records cannot be changed until it finishes.");
            return await action();
        }
        finally
        {
            _jobLock.Release();
        }
    }

    private Task<T> MutateCrudAsync<T>(Func<WorldRegistry, T> mutate) =>
        RunCrudAsync(() => MutateAsync(mutate));

    private string CreateStagingDirectory(string jobId, string purpose)
    {
        var stagingRoot = Path.Combine(WorldsRoot, ".staging");
        Directory.CreateDirectory(stagingRoot);
        var directory = Path.Combine(stagingRoot, $"{jobId}_{purpose}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>
    /// Publishes a completed snapshot folder and its registry mutation while the
    /// registry gate is held. Status polling therefore cannot adopt the folder as
    /// an orphan between the move and worlds.json update.
    /// </summary>
    private async Task<T> PublishSnapshotAsync<T>(
        string stagingDirectory,
        string finalFolder,
        Func<WorldRegistry, T> mutate)
    {
        var finalDirectory = ResolveSnapshotDirectory(finalFolder);
        await _registryLock.WaitAsync();
        try
        {
            var registry = await LoadUnlockedAsync();
            if (Directory.Exists(finalDirectory))
                throw new IOException($"Snapshot folder '{finalFolder}' already exists.");

            Directory.Move(stagingDirectory, finalDirectory);
            try
            {
                var result = mutate(registry);
                await SaveUnlockedAsync(registry);
                return result;
            }
            catch (Exception publishError)
            {
                try
                {
                    if (Directory.Exists(finalDirectory) && !Directory.Exists(stagingDirectory))
                        Directory.Move(finalDirectory, stagingDirectory);
                }
                catch (Exception rollbackError)
                {
                    _logger.LogError(rollbackError,
                        "Snapshot publish rollback failed for {Folder}; its manifest remains recoverable as an orphan",
                        finalFolder);
                    throw new AggregateException(
                        $"Snapshot publish failed and '{finalFolder}' could not be moved back to staging. " +
                        "Its manifest remains recoverable as an orphan.",
                        publishError, rollbackError);
                }
                throw;
            }
        }
        finally
        {
            _registryLock.Release();
        }
    }

    private void CleanupStagingDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not remove World State staging directory {Directory}", directory);
        }
    }

    private static void ValidateRtsLaunchConfigurationContract(
        WorldLaunchConfiguration configuration, WorldRestorePreflight preflight)
    {
        if (!preflight.ConfigValues.TryGetValue("RealmID", out var capturedRealmId) ||
            capturedRealmId is null or <= 0)
            throw new InvalidOperationException("The selected snapshot does not expose a valid captured RealmID.");
        if (capturedRealmId != configuration.RealmId)
            throw new InvalidOperationException(
                $"Realm ID is inherited from the selected snapshot ({capturedRealmId}) and cannot be changed to {configuration.RealmId}.");
        if (preflight.NamePoolEligible is null)
            throw new InvalidOperationException("The current bot name pool could not be validated.");
        WorldConfigurationCatalog.ValidateNamePoolCapacity(
            configuration, preflight.NamePoolEligible.Value);
    }

    /// <summary>
    /// Escape hatch for surgical work: put a single group back from one snapshot without a
    /// full world swap — e.g. roll the core source back but keep the characters you have.
    /// Only allowed with nothing mounted; grafting a group under a live world would leave
    /// it half-overwritten.
    /// </summary>
    public async Task<WorldJob> RestoreSingleGroupAsync(string worldId, string folder, string group)
    {
        if (!AllGroups.Contains(group, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unknown group: " + group);

        var registry = await GetRegistryAsync();

        if (registry.LiveWorldId != null)
            throw new InvalidOperationException("Suspend the mounted world first — a single-group restore underneath it would leave the world inconsistent.");

        var world = registry.Worlds.FirstOrDefault(w => w.Id == worldId)
            ?? throw new InvalidOperationException("World not found.");
        var snapshot = world.Snapshots.FirstOrDefault(s => s.Folder == folder)
            ?? throw new InvalidOperationException("Snapshot not found.");
        if (snapshot.Groups?.Contains(group, StringComparer.OrdinalIgnoreCase) != true)
            throw new InvalidOperationException($"'{group}' was not captured in this snapshot.");

        var dir = ResolveSnapshotDirectory(snapshot.Folder);
        if (!Directory.Exists(dir))
            throw new InvalidOperationException($"Snapshot folder '{snapshot.Folder}' is missing from disk.");

        var steps = new List<WorldJobStep>
        {
            Step("preflight", $"Verify {group} snapshot artifacts"),
            Step("stop", "Make sure mangosd & realmd are stopped"),
            Step("restore", $"Graft “{group}” from {snapshot.Folder}")
        };

        return await StartJobAsync("restore-group", $"Restoring {group} from {snapshot.Folder}", steps, async job =>
        {
            string? validatedDirectory = null;
            await RunStep(job, "preflight", async () =>
            {
                // The initial lookup happens before job admission. Resolve it again
                // after admission so a concurrently deleted/unlinked snapshot cannot
                // be restored from a stale in-memory record.
                var currentRegistry = await GetRegistryAsync();
                var currentWorld = currentRegistry.Worlds.FirstOrDefault(w => w.Id == worldId)
                    ?? throw new InvalidOperationException("World not found.");
                var currentSnapshot = currentWorld.Snapshots.FirstOrDefault(s => s.Folder == folder)
                    ?? throw new InvalidOperationException("Snapshot not found.");
                if (currentSnapshot.Groups?.Contains(group, StringComparer.OrdinalIgnoreCase) != true)
                    throw new InvalidOperationException($"'{group}' was not captured in this snapshot.");
                validatedDirectory = ResolveSnapshotDirectory(currentSnapshot.Folder);
                if (!Directory.Exists(validatedDirectory))
                    throw new InvalidOperationException(
                        $"Snapshot folder '{currentSnapshot.Folder}' is missing from disk.");
                return await ValidateSelectedGroupSnapshotAsync(
                    currentSnapshot, validatedDirectory, group);
            });
            await RunStep(job, "stop", async () => await StopServerAsync());

            // Invalidate the trusted whole-world marker before the first destructive
            // statement. A failed graft must never leave an instant-resume claim behind.
            await MutateAsync(reg =>
            {
                reg.MaterializedWorldId = null;
                reg.MaterializedSnapshot = null;
                return true;
            });
            await RunStep(job, "restore", async () =>
                await RestoreGroupAsync(group, validatedDirectory!));
        });
    }

    // ==================================================================
    //  CREATE RTS WORLD — offline artifact construction only
    // ==================================================================

    public async Task<WorldJob> CreateRtsWorldAsync(CreateRtsWorldRequestModel request)
    {
        var configuration = WorldConfigurationCatalog.NormalizeAndValidate(request.Configuration);
        var registry = await GetRegistryAsync();
        if (registry.LiveWorldId != null)
            throw new InvalidOperationException("Suspend the mounted world before creating a new campaign.");
        if (_proc.GetMangosdStatus().IsRunning || _proc.GetRealmdStatus().IsRunning)
            throw new InvalidOperationException("mangosd and realmd must both be stopped before creating a campaign.");
        if (registry.Worlds.Any(w => string.Equals(w.Name, request.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("A world with that name already exists.");

        var source = registry.Worlds.FirstOrDefault(w => w.Id == request.SourceWorldId)
            ?? throw new InvalidOperationException("Source world not found.");
        var sourceSnapshot = source.Snapshots.FirstOrDefault(s => s.Folder == request.SourceSnapshot)
            ?? throw new InvalidOperationException("Source snapshot not found.");
        var sourceDirectory = ResolveSnapshotDirectory(sourceSnapshot.Folder);
        var sourceRealmId = await _rtsWorlds.ReadSourceRealmIdAsync(sourceDirectory);
        if (configuration.RealmId != sourceRealmId)
            throw new InvalidOperationException(
                $"Realm ID is inherited from the selected snapshot ({sourceRealmId}) and cannot be changed to {configuration.RealmId}.");
        var namePool = await _rtsWorlds.GetNamePoolStatsAsync();
        WorldConfigurationCatalog.ValidateNamePoolCapacity(configuration, namePool.ValidUniqueNames);

        var steps = new List<WorldJobStep>
        {
            Step("validate-source", "Verify clean-template source snapshot"),
            Step("build", "Build zero-roster world, accounts and admin state"),
            Step("configure", "Apply RTS rules and per-world server configuration"),
            Step("validate-output", "Hash and validate generated artifacts"),
            Step("publish", "Publish parked RTS world")
        };

        return await StartJobAsync("create-rts", $"Creating {request.Name.Trim()}", steps, async job =>
        {
            var stagingDirectory = CreateStagingDirectory(job.Id, "create-rts");
            try
            {
                await RunStep(job, "validate-source", async () =>
                {
                    var preflight = await PreflightResumeAsync(source.Id, sourceSnapshot.Folder, forceFullRestore: true);
                    if (!preflight.Allowed)
                        throw new InvalidOperationException(string.Join("; ", preflight.Blockers));
                    if (!preflight.RtsSourceEligible)
                        throw new InvalidOperationException(
                            "This is a legacy snapshot. Restore it and suspend once with World State v2 before creating RTS.");
                    if (!preflight.ConfigValues.TryGetValue("RealmID", out var verifiedRealmId) ||
                        verifiedRealmId != configuration.RealmId)
                        throw new InvalidOperationException(
                            $"Source RealmID changed during validation; expected {configuration.RealmId}.");
                    var currentNamePool = await _rtsWorlds.GetNamePoolStatsAsync();
                    WorldConfigurationCatalog.ValidateNamePoolCapacity(
                        configuration, currentNamePool.ValidUniqueNames);
                    return $"v2 hashes, RealmID {configuration.RealmId}, clean character schema and {currentNamePool.ValidUniqueNames:N0} names verified";
                });

                RtsWorldBuildResult? build = null;
                await RunStep(job, "build", async () =>
                {
                    build = await _rtsWorlds.BuildAsync(sourceDirectory, stagingDirectory, request);
                    return $"0 characters, 0 bots; {build.NamePoolEligible:N0} eligible names";
                });
                await RunStep(job, "configure", () => Task.FromResult<string?>(
                    $"{configuration.ProfileId} · PlayerLimit {configuration.PlayerLimit:N0} · bot caps {configuration.AllianceBotCap:N0}/{configuration.HordeBotCap:N0}"));

                var finalFolder = NewSnapshotFolder("rts-seed") + "_" + job.Id[..6];
                WorldSnapshot? snapshot = null;
                await RunStep(job, "validate-output", async () =>
                {
                    var stats = new Dictionary<string, object>
                    {
                        ["totalCharacters"] = 0,
                        ["persistedBots"] = 0,
                        ["sourceWorld"] = source.Name,
                        ["namePoolEligible"] = build!.NamePoolEligible
                    };
                    snapshot = await WriteManifestAsync(
                        stagingDirectory, finalFolder, SnapshotKind.RtsSeed,
                        $"Clean RTS genesis from {source.Name}", AllGroups, stats,
                        configuration, source.Id, sourceSnapshot.Folder,
                        configuration.ProfileId,
                        build.NamePoolSha256, build.NamePoolEligible);
                    foreach (var artifact in snapshot.Artifacts)
                    {
                        var check = await _artifacts.ValidateArtifactAsync(stagingDirectory, artifact);
                        if (!check.Valid && artifact.Required)
                            throw new InvalidDataException($"{artifact.FileName}: {check.Detail}");
                    }
                    return $"{snapshot.Artifacts.Count} artifacts checksummed";
                });

                await RunStep(job, "publish", async () =>
                {
                    await PublishSnapshotAsync(stagingDirectory, finalFolder, reg =>
                    {
                        if (reg.Worlds.Any(w => string.Equals(
                                w.Name, request.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
                            throw new InvalidOperationException("A world with that name already exists.");
                        reg.Worlds.Add(new WorldRecord
                        {
                            Id = NewId(),
                            Name = request.Name.Trim(),
                            Flavor = "rts",
                            Notes = request.Notes,
                            ParentId = source.Id,
                            ForkedFromFolder = sourceSnapshot.Folder,
                            CreatedUtc = DateTime.UtcNow,
                            State = WorldState.Suspended,
                            SuspendedUtc = DateTime.UtcNow,
                            LaunchConfiguration = configuration.Clone(),
                            Snapshots = { snapshot! }
                        });
                        return true;
                    });
                    job.ResultFolder = finalFolder;
                    return "parked; no services started";
                });
            }
            finally
            {
                CleanupStagingDirectory(stagingDirectory);
            }
        });
    }

    // ==================================================================
    //  WORLD CRUD
    // ==================================================================

    /// <summary>
    /// Forks a world at one of its snapshots. The fork shares the snapshot folder rather
    /// than copying it — these are multi-gigabyte dumps, and deletion is reference-counted.
    /// </summary>
    public async Task<WorldRecord> ForkAsync(string worldId, string? snapshotFolder, string name, string flavor, string? notes)
    {
        return await MutateCrudAsync(reg =>
        {
            var parent = reg.Worlds.FirstOrDefault(w => w.Id == worldId)
                ?? throw new InvalidOperationException("World not found.");

            var snapshot = snapshotFolder != null
                ? parent.Snapshots.FirstOrDefault(s => s.Folder == snapshotFolder)
                : parent.Snapshots.FirstOrDefault();

            if (snapshot == null)
                throw new InvalidOperationException($"“{parent.Name}” has no snapshot to fork from. Suspend it first.");

            var fork = new WorldRecord
            {
                Id = NewId(),
                Name = string.IsNullOrWhiteSpace(name) ? parent.Name + " (fork)" : name.Trim(),
                Flavor = string.IsNullOrWhiteSpace(flavor) ? parent.Flavor : flavor,
                Notes = notes,
                ParentId = parent.Id,
                ForkedFromFolder = snapshot.Folder,
                CreatedUtc = DateTime.UtcNow,
                State = WorldState.Suspended,
                SuspendedUtc = DateTime.UtcNow,
                Snapshots = { CloneAsForkOrigin(snapshot, parent.Name) }
            };

            reg.Worlds.Add(fork);
            return fork;
        });
    }

    private static WorldSnapshot CloneAsForkOrigin(WorldSnapshot source, string parentName) => new()
    {
        SchemaVersion = source.SchemaVersion,
        Folder = source.Folder,
        TakenUtc = source.TakenUtc,
        Kind = SnapshotKind.ForkOrigin,
        Label = $"Forked from {parentName}" + (string.IsNullOrEmpty(source.Label) ? "" : $" — {source.Label}"),
        Groups = source.Groups,
        Sizes = source.Sizes,
        Stats = source.Stats,
        TotalBytes = source.TotalBytes,
        Artifacts = source.Artifacts.Select(a => new SnapshotArtifact
        {
            Id = a.Id,
            Group = a.Group,
            FileName = a.FileName,
            Format = a.Format,
            Length = a.Length,
            Sha256 = a.Sha256,
            Required = a.Required
        }).ToList(),
        SourceWorldId = source.SourceWorldId,
        SourceSnapshot = source.SourceSnapshot,
        ProfileId = source.ProfileId,
        LaunchConfiguration = source.LaunchConfiguration?.Clone(),
        NamePoolSha256 = source.NamePoolSha256,
        NamePoolEligible = source.NamePoolEligible
    };

    public async Task<WorldRecord> UpdateAsync(string worldId, string? name, string? flavor, string? notes)
    {
        return await MutateCrudAsync(reg =>
        {
            var w = reg.Worlds.FirstOrDefault(x => x.Id == worldId)
                ?? throw new InvalidOperationException("World not found.");
            if (!string.IsNullOrWhiteSpace(name)) w.Name = name.Trim();
            if (!string.IsNullOrWhiteSpace(flavor)) w.Flavor = flavor;
            if (notes != null) w.Notes = notes;
            return w;
        });
    }

    public async Task<bool> UpdateSnapshotLabelAsync(string worldId, string folder, string label)
    {
        return await MutateCrudAsync(reg =>
        {
            var snap = reg.Worlds.FirstOrDefault(x => x.Id == worldId)?.Snapshots.FirstOrDefault(s => s.Folder == folder);
            if (snap == null) return false;
            snap.Label = label;
            return true;
        });
    }

    /// <summary>Deletes a world. Snapshot folders shared with another world are left on disk.</summary>
    public async Task<object> DeleteWorldAsync(string worldId)
    {
        return await RunCrudAsync<object>(async () =>
        {
            var toDelete = await MutateAsync(reg =>
            {
                var w = reg.Worlds.FirstOrDefault(x => x.Id == worldId)
                    ?? throw new InvalidOperationException("World not found.");
                if (reg.LiveWorldId == w.Id)
                    throw new InvalidOperationException("Suspend this world before deleting it.");

                reg.Worlds.Remove(w);

                // Children keep their history but lose the broken parent pointer.
                foreach (var child in reg.Worlds.Where(c => c.ParentId == w.Id))
                    child.ParentId = null;

                if (reg.MaterializedWorldId == w.Id)
                {
                    reg.MaterializedWorldId = null;
                    reg.MaterializedSnapshot = null;
                }

                var stillReferenced = reg.Worlds.SelectMany(x => x.Snapshots).Select(s => s.Folder)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                return w.Snapshots.Select(s => s.Folder)
                    .Where(f => !stillReferenced.Contains(f))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            });

            int removed = 0;
            foreach (var folder in toDelete)
            {
                if (TryDeleteFolder(folder)) removed++;
            }

            return new { deletedFolders = removed, keptShared = toDelete.Count - removed };
        });
    }

    /// <summary>Deletes one snapshot. Refuses if it is the world's only way back.</summary>
    public async Task<bool> DeleteSnapshotAsync(string worldId, string folder)
    {
        return await RunCrudAsync(async () =>
        {
            var shouldDeleteFolder = await MutateAsync(reg =>
            {
                var w = reg.Worlds.FirstOrDefault(x => x.Id == worldId)
                    ?? throw new InvalidOperationException("World not found.");

                var snap = w.Snapshots.FirstOrDefault(s => s.Folder == folder)
                    ?? throw new InvalidOperationException("Snapshot not found.");

                if (w.Snapshots.Count == 1 && reg.MaterializedWorldId != w.Id)
                    throw new InvalidOperationException(
                        $"This is the only snapshot of “{w.Name}” — deleting it would make the world unresumable.");

                w.Snapshots.Remove(snap);
                if (reg.MaterializedSnapshot == folder) reg.MaterializedSnapshot = null;

                // Forks share folders with their parent — only unlink from disk when nobody else points at it.
                return !reg.Worlds.SelectMany(x => x.Snapshots).Any(s => string.Equals(s.Folder, folder, StringComparison.OrdinalIgnoreCase));
            });

            return shouldDeleteFolder && TryDeleteFolder(folder);
        });
    }

    private bool TryDeleteFolder(string folder)
    {
        try
        {
            var dir = ResolveSnapshotDirectory(folder);
            if (!Directory.Exists(dir)) return false;
            Directory.Delete(dir, true);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete snapshot folder {Folder}", folder);
            return false;
        }
    }

    // ==================================================================
    //  DUMP / RESTORE MECHANICS
    // ==================================================================

    private async Task<string> StopServerAsync()
    {
        Exception? mangosdError = null;
        Exception? realmdError = null;
        try { await _proc.StopMangosdAsync(); } catch (Exception ex) { mangosdError = ex; }
        try { await _proc.StopRealmdAsync(); } catch (Exception ex) { realmdError = ex; }

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        ProcessStatus mangosd;
        ProcessStatus realmd;
        do
        {
            mangosd = _proc.GetMangosdStatus();
            realmd = _proc.GetRealmdStatus();
            if (!mangosd.IsRunning && !realmd.IsRunning)
            {
                // Give final DB saves a bounded quiet period after both processes disappear.
                await Task.Delay(1000);
                return "mangosd & realmd confirmed stopped";
            }
            await Task.Delay(250);
        } while (DateTime.UtcNow < deadline);

        var detail = new List<string>();
        if (mangosd.IsRunning) detail.Add($"mangosd still running (PID {mangosd.Pid?.ToString() ?? "unknown"})");
        if (realmd.IsRunning) detail.Add($"realmd still running (PID {realmd.Pid?.ToString() ?? "unknown"})");
        if (mangosdError != null) detail.Add("mangosd stop: " + mangosdError.Message);
        if (realmdError != null) detail.Add("realmd stop: " + realmdError.Message);
        throw new InvalidOperationException("World processes did not stop; refusing to dump or restore: " + string.Join("; ", detail));
    }

    private async Task<string> StartServerAndVerifyAsync()
    {
        await _proc.StartRealmdAsync();
        await WaitForProcessAsync("realmd", () => _proc.GetRealmdStatus(), TimeSpan.FromSeconds(30));
        await _proc.StartMangosdAsync();
        await WaitForProcessAsync("mangosd", () => _proc.GetMangosdStatus(), TimeSpan.FromSeconds(45));
        await WaitForStableProcessesAsync(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15));
        return "mangosd & realmd confirmed running and stable";
    }

    private async Task WaitForStableProcessesAsync(TimeSpan stableFor, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        DateTime? stableSince = null;
        do
        {
            var bothRunning = _proc.GetRealmdStatus().IsRunning && _proc.GetMangosdStatus().IsRunning;
            if (bothRunning)
            {
                stableSince ??= DateTime.UtcNow;
                if (DateTime.UtcNow - stableSince >= stableFor) return;
            }
            else
            {
                stableSince = null;
            }

            await Task.Delay(250);
        } while (DateTime.UtcNow < deadline);

        throw new InvalidOperationException(
            $"mangosd and realmd did not remain running together for {stableFor.TotalSeconds:F0} seconds.");
    }

    private static async Task WaitForProcessAsync(string name, Func<ProcessStatus> status, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        do
        {
            if (status().IsRunning) return;
            await Task.Delay(250);
        } while (DateTime.UtcNow < deadline);
        throw new InvalidOperationException($"{name} did not become ready within {timeout.TotalSeconds:F0} seconds.");
    }

    private async Task<string> DumpGroupAsync(string group, string dir)
    {
        switch (group)
        {
            case "world":
            {
                var (host, port, user, pass) = ParseConnectionString("Mangos");
                await RunMysqlDump(host, port, user, pass, "mangos", Path.Combine(dir, "world_mangos.sql.gz"));
                await RunMysqlDump(host, port, user, pass, "vmangos_admin", Path.Combine(dir, "world_vmangos_admin.sql.gz"));
                break;
            }
            case "players":
            {
                var (host, port, user, pass) = ParseConnectionString("Characters");
                await RunMysqlDump(host, port, user, pass, "characters", Path.Combine(dir, "players_characters.sql.gz"));
                await RunMysqlDump(host, port, user, pass, "realmd", Path.Combine(dir, "players_realmd.sql.gz"));

                // A full dump is the restorable save. These two additional artifacts are
                // the clean, version-matched template used to create a zero-roster campaign.
                await RunMysqlDump(host, port, user, pass, "characters",
                    Path.Combine(dir, WorldArtifactService.PlayersCharactersSchema),
                    new[] { "--no-data" }, Array.Empty<string>());

                using (var characters = _db.Characters())
                {
                    var tables = (await characters.QueryAsync<string>("SHOW TABLES"))
                        .Where(t => string.Equals(t, "migrations", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(t, "db_version", StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                    if (tables.Length > 0)
                    {
                        await RunMysqlDump(host, port, user, pass, "characters",
                            Path.Combine(dir, WorldArtifactService.PlayersCharactersSystem),
                            new[] { "--no-create-info", "--skip-triggers" }, tables);
                    }
                    else
                    {
                        await _artifacts.WriteGzipTextAsync(
                            Path.Combine(dir, WorldArtifactService.PlayersCharactersSystem),
                            "-- No character schema bookkeeping tables were present.\n");
                    }
                }
                break;
            }
            case "core":
            {
                var outputPath = Path.Combine(dir, "core_source.tar.gz");

                if (!Directory.Exists(SourceRoot))
                    throw new DirectoryNotFoundException($"Configured source root '{SourceRoot}' does not exist.");
                if (!Directory.Exists(SqlRoot))
                    throw new DirectoryNotFoundException($"Configured SQL root '{SqlRoot}' does not exist.");

                var args = new List<string> { "czf", outputPath };
                args.AddRange(new[] { "-C", Path.GetDirectoryName(SourceRoot)!, Path.GetFileName(SourceRoot) });
                args.AddRange(new[] { "-C", Path.GetDirectoryName(SqlRoot)!, Path.GetFileName(SqlRoot) });

                await RunProcess("tar", string.Join(" ", args.Select(a => $"\"{a}\"")));

                if (!File.Exists(MangosdConfPath))
                    throw new FileNotFoundException("The configured mangosd.conf does not exist.", MangosdConfPath);
                await _artifacts.CopyAtomicAsync(MangosdConfPath,
                    Path.Combine(dir, WorldArtifactService.CoreConfig));

                var assignmentsOutput = Path.Combine(dir, WorldArtifactService.RotationAssignments);
                if (File.Exists(RotationAssignmentsPath))
                    await _artifacts.CopyAtomicAsync(RotationAssignmentsPath, assignmentsOutput);
                else
                    await File.WriteAllTextAsync(assignmentsOutput, "{}\n", new UTF8Encoding(false));
                break;
            }
            default:
                throw new InvalidOperationException("Unknown group: " + group);
        }

        var bytes = new DirectoryInfo(dir).GetFiles($"{group}*").Sum(f => f.Length);
        return FormatBytes(bytes);
    }

    private async Task<string> RestoreGroupAsync(string group, string dir)
    {
        switch (group)
        {
            case "world":
            {
                var (host, port, user, pass) = ParseConnectionString("Mangos");
                var mangosPath = Path.Combine(dir, "world_mangos.sql.gz");
                if (!File.Exists(mangosPath))
                    throw new FileNotFoundException("world_mangos.sql.gz missing from snapshot");
                var adminPath = Path.Combine(dir, "world_vmangos_admin.sql.gz");
                if (!File.Exists(adminPath))
                    throw new FileNotFoundException("world_vmangos_admin.sql.gz missing from snapshot");

                // vmangos_admin carries queue intent as part of this snapshot. Block
                // new queue work, drain work already in flight, and keep the lease
                // through both destructive restores plus schema rebootstrap.
                await using WorldMaintenanceGate.Lease maintenance =
                    await _worldMaintenance.AcquireMaintenanceAsync();
                await RunMysqlRestore(host, port, user, pass, "mangos", mangosPath);
                await RunMysqlRestore(host, port, user, pass, "vmangos_admin", adminPath);

                // A snapshot may predate the durable combat-loadout queue or carry an
                // early table shape. The web process stays alive during world grafts,
                // so startup bootstrap will not run on its own after this destructive
                // database replacement. Recreate/migrate the admin schema now, before
                // the restore step can be marked complete or materialized/live markers
                // can be trusted again.
                await _dbInitialization.InitializeAsync();
                if (!_dbInitialization.AdminDbReady)
                {
                    throw new InvalidOperationException(
                        "vmangos_admin was restored, but its required schema could not be initialized: " +
                        (_dbInitialization.AdminDbError ?? "unknown initialization error"));
                }
                return "mangos + vmangos_admin";
            }
            case "players":
            {
                var (host, port, user, pass) = ParseConnectionString("Characters");
                var charsPath = Path.Combine(dir, "players_characters.sql.gz");
                if (!File.Exists(charsPath))
                    throw new FileNotFoundException("players_characters.sql.gz missing from snapshot");

                await RunMysqlRestore(host, port, user, pass, "characters", charsPath);

                var realmPath = Path.Combine(dir, "players_realmd.sql.gz");
                if (!File.Exists(realmPath))
                    throw new FileNotFoundException("players_realmd.sql.gz missing from snapshot");
                await RunMysqlRestore(host, port, user, pass, "realmd", realmPath);
                return "characters + realmd";
            }
            case "core":
            {
                var archivePath = Path.Combine(dir, "core_source.tar.gz");
                if (!File.Exists(archivePath))
                    throw new FileNotFoundException("core_source.tar.gz missing from snapshot");

                var sidecar = Path.Combine(dir, WorldArtifactService.CoreConfig);
                var rotationAssignments = Path.Combine(dir, WorldArtifactService.RotationAssignments);
                var restored = await _artifacts.RestoreCoreArtifactsAsync(
                    archivePath,
                    SourceRoot,
                    SqlRoot,
                    File.Exists(sidecar) ? sidecar : null,
                    MangosdConfPath,
                    File.Exists(rotationAssignments) ? rotationAssignments : null,
                    RotationAssignmentsPath);
                return $"src ({restored.SourceFiles:N0} files) + sql ({restored.SqlFiles:N0} files) + exact-path config" +
                       (restored.LegacyConfig ? " (legacy adapter)" : "");
            }
            default:
                throw new InvalidOperationException("Unknown group: " + group);
        }
    }

    private async Task<WorldSnapshot> WriteManifestAsync(
        string dir, string folder, string kind, string label, string[] groups, Dictionary<string, object> stats,
        WorldLaunchConfiguration? launchConfiguration = null,
        string? sourceWorldId = null,
        string? sourceSnapshot = null,
        string? profileId = null,
        string? namePoolSha256 = null,
        int? namePoolEligible = null)
    {
        var artifacts = await _artifacts.DescribeV2ArtifactsAsync(dir);
        var sizes = new Dictionary<string, string>();
        foreach (var g in groups)
        {
            var names = artifacts.Where(a => string.Equals(a.Group, g, StringComparison.OrdinalIgnoreCase))
                .Select(a => a.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var bytes = new DirectoryInfo(dir).GetFiles().Where(f => names.Contains(f.Name)).Sum(f => f.Length);
            sizes[g] = FormatBytes(bytes);
        }

        var manifest = new SnapshotManifest
        {
            SchemaVersion = 2,
            Timestamp = DateTime.UtcNow,
            Kind = kind,
            Label = label,
            Groups = groups,
            Sizes = sizes,
            Stats = stats,
            Artifacts = artifacts,
            SourceWorldId = sourceWorldId,
            SourceSnapshot = sourceSnapshot,
            ProfileId = profileId,
            LaunchConfiguration = launchConfiguration?.Clone(),
            NamePoolSha256 = namePoolSha256,
            NamePoolEligible = namePoolEligible
        };

        var manifestPath = Path.Combine(dir, "manifest.json");
        var manifestTemp = manifestPath + ".tmp";
        await File.WriteAllTextAsync(manifestTemp, JsonSerializer.Serialize(manifest, _json));
        File.Move(manifestTemp, manifestPath, overwrite: true);

        return new WorldSnapshot
        {
            SchemaVersion = 2,
            Folder = folder,
            TakenUtc = manifest.Timestamp,
            Kind = kind,
            Label = label,
            Groups = groups,
            Sizes = sizes,
            Stats = stats,
            TotalBytes = DirectorySize(new DirectoryInfo(dir)),
            Artifacts = artifacts,
            SourceWorldId = sourceWorldId,
            SourceSnapshot = sourceSnapshot,
            ProfileId = profileId,
            LaunchConfiguration = launchConfiguration?.Clone(),
            NamePoolSha256 = namePoolSha256,
            NamePoolEligible = namePoolEligible
        };
    }

    // ==================================================================
    //  SHELL HELPERS
    // ==================================================================

    private Task RunMysqlDump(string host, string port, string user, string pass, string database, string outputPath) =>
        RunMysqlDump(host, port, user, pass, database, outputPath, Array.Empty<string>(), Array.Empty<string>());

    private static async Task RunMysqlDump(
        string host, string port, string user, string pass, string database, string outputPath,
        IReadOnlyList<string> options, IReadOnlyList<string> tables)
    {
        // Only complete database dumps are self-contained. The character schema/data
        // template fragments intentionally remain composable inside an already selected DB.
        var includeDatabaseDefinition = options.Count == 0 && tables.Count == 0;
        var psi = MysqlStartInfo("mysqldump", host, port, user, pass);
        psi.RedirectStandardOutput = true;
        psi.ArgumentList.Add("--single-transaction");
        psi.ArgumentList.Add("--routines");
        psi.ArgumentList.Add("--triggers");
        if (includeDatabaseDefinition)
        {
            psi.ArgumentList.Add("--add-drop-database");
            psi.ArgumentList.Add("--databases");
        }
        foreach (var option in options) psi.ArgumentList.Add(option);
        psi.ArgumentList.Add(database);
        foreach (var table in tables) psi.ArgumentList.Add(table);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start mysqldump.");
        var stderrTask = process.StandardError.ReadToEndAsync();
        try
        {
            await using (var outputFile = File.Create(outputPath))
            await using (var gzip = new GZipStream(outputFile, CompressionLevel.SmallestSize, leaveOpen: false))
                await process.StandardOutput.BaseStream.CopyToAsync(gzip);
            await process.WaitForExitAsync();
            var stderr = await stderrTask;
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"mysqldump failed (exit {process.ExitCode}): {stderr.Trim()}");
        }
        catch
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            if (File.Exists(outputPath)) File.Delete(outputPath);
            throw;
        }
    }

    private async Task RunMysqlRestore(string host, string port, string user, string pass, string database, string inputPath)
    {
        var selfContained = await _artifacts.InspectDatabaseDumpAsync(inputPath, database) != null;
        if (!selfContained)
        {
            // Legacy v1 dumps contain tables but no CREATE DATABASE statement. Preserve the
            // exact currently-materialized schema definition before the destructive reset.
            // If SHOW CREATE cannot be read, fail before dropping anything.
            var createDatabaseDdl = await ReadCreateDatabaseDdlAsync(host, port, user, pass, database);
            await RunMysqlExecuteAsync(host, port, user, pass,
                $"DROP DATABASE IF EXISTS `{database}`; {createDatabaseDdl};");
        }

        var psi = MysqlStartInfo("mysql", host, port, user, pass);
        psi.RedirectStandardInput = true;
        // A self-contained v2 dump owns DROP/CREATE/USE and can recover even when a
        // previous interrupted restore left the canonical database absent. Legacy table-
        // only dumps need the preserved database selected explicitly.
        if (!selfContained) psi.ArgumentList.Add(database);
        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start mysql restore.");
        var stderrTask = process.StandardError.ReadToEndAsync();
        await using (var inputFile = File.OpenRead(inputPath))
        await using (var gzip = new GZipStream(inputFile, CompressionMode.Decompress, leaveOpen: false))
            await gzip.CopyToAsync(process.StandardInput.BaseStream);
        process.StandardInput.Close();
        await process.WaitForExitAsync();
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"mysql restore for '{database}' failed (exit {process.ExitCode}): {stderr.Trim()}");
    }

    private static async Task<string> ReadCreateDatabaseDdlAsync(
        string host, string port, string user, string pass, string database)
    {
        var escapedIdentifier = database.Replace("`", "``", StringComparison.Ordinal);
        var psi = MysqlStartInfo("mysql", host, port, user, pass);
        psi.RedirectStandardOutput = true;
        psi.ArgumentList.Add("--batch");
        psi.ArgumentList.Add("--skip-column-names");
        psi.ArgumentList.Add("-e");
        psi.ArgumentList.Add($"SHOW CREATE DATABASE `{escapedIdentifier}`");

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to inspect the current database definition.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var stdout = (await stdoutTask).Trim();
        var stderr = (await stderrTask).Trim();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"Could not preserve the current '{database}' database definition; restore was not started: {stderr}");

        var tab = stdout.IndexOf('\t');
        var ddl = tab >= 0 ? stdout[(tab + 1)..].Trim() : "";
        if (!ddl.StartsWith("CREATE DATABASE", StringComparison.OrdinalIgnoreCase) ||
            !ddl.Contains($"`{escapedIdentifier}`", StringComparison.Ordinal) ||
            ddl.Contains(';') || ddl.Contains('\r') || ddl.Contains('\n'))
        {
            throw new InvalidDataException(
                $"The server returned an unsafe or unrecognized CREATE DATABASE definition for '{database}'; restore was not started.");
        }
        return ddl;
    }

    private static async Task RunMysqlExecuteAsync(string host, string port, string user, string pass, string sql)
    {
        var psi = MysqlStartInfo("mysql", host, port, user, pass);
        psi.ArgumentList.Add("-e");
        psi.ArgumentList.Add(sql);
        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start mysql.");
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"mysql failed (exit {process.ExitCode}): {stderr.Trim()}");
    }

    private static ProcessStartInfo MysqlStartInfo(string executable, string host, string port, string user, string pass)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add($"-h{host}");
        psi.ArgumentList.Add($"-P{port}");
        psi.ArgumentList.Add($"-u{user}");
        psi.Environment["MYSQL_PWD"] = pass;
        return psi;
    }

    private static async Task RunBash(string command)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            Arguments = $"-c \"{command.Replace("\"", "\\\"")}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start bash");
        var stderr = await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();

        // mysqldump writes warnings to stderr even on success — only the exit code decides.
        if (proc.ExitCode != 0)
            throw new InvalidOperationException($"Command failed (exit {proc.ExitCode}): {stderr.Trim()}");
    }

    private static async Task RunProcess(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {fileName}");
        var stderr = await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();

        if (proc.ExitCode != 0)
            throw new InvalidOperationException($"{fileName} failed (exit {proc.ExitCode}): {stderr.Trim()}");
    }

    private (string host, string port, string user, string pass) ParseConnectionString(string name)
    {
        var cs = _config.GetConnectionString(name)
            ?? throw new InvalidOperationException($"Connection string '{name}' not found");

        var parts = cs.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0].Trim().ToLower(), p => p[1].Trim());

        return (
            parts.GetValueOrDefault("server", "127.0.0.1"),
            parts.GetValueOrDefault("port", "3306"),
            parts.GetValueOrDefault("user", "mangos"),
            parts.GetValueOrDefault("password", "mangos")
        );
    }

    private bool SafeIsRunning()
    {
        try { return _proc.GetMangosdStatus().IsRunning; }
        catch { return false; }
    }

    private static string NewId() => Guid.NewGuid().ToString("N")[..12];

    private sealed class WorldStateSettingRow
    {
        public string Key { get; set; } = "";
        public string Value { get; set; } = "";
    }

    private sealed class HeroRuleSettingRow
    {
        public int HeroLevel { get; set; }
        public int HonorCost { get; set; }
        public int ReviveFee { get; set; }
        public int SpellId { get; set; }
        public int ScalePercent { get; set; }
        public int DamagePercent { get; set; }
    }

    private sealed class RtsHeroSpellValidationRow
    {
        public int SpellId { get; set; }
        public int Build { get; set; }
        public ulong Attributes { get; set; }
        public int DurationIndex { get; set; }
        public int StackAmount { get; set; }
        public int EquippedItemClass { get; set; }
        public long EquippedItemSubClassMask { get; set; }
        public long EquippedItemInventoryTypeMask { get; set; }
        public int Effect1 { get; set; }
        public int Effect2 { get; set; }
        public int Effect3 { get; set; }
        public int EffectBaseDice1 { get; set; }
        public int EffectBaseDice2 { get; set; }
        public int EffectDieSides1 { get; set; }
        public int EffectDieSides2 { get; set; }
        public int EffectBasePoints1 { get; set; }
        public int EffectBasePoints2 { get; set; }
        public int EffectImplicitTargetA1 { get; set; }
        public int EffectImplicitTargetA2 { get; set; }
        public int EffectImplicitTargetB1 { get; set; }
        public int EffectImplicitTargetB2 { get; set; }
        public int EffectApplyAuraName1 { get; set; }
        public int EffectApplyAuraName2 { get; set; }
        public int EffectMiscValue1 { get; set; }
        public int EffectMiscValue2 { get; set; }
        public ulong Targets { get; set; }
        public ulong ProcFlags { get; set; }
        public int ProcChance { get; set; }
        public int ProcCharges { get; set; }
        public int EffectAmplitude1 { get; set; }
        public int EffectAmplitude2 { get; set; }
        public int EffectTriggerSpell1 { get; set; }
        public int EffectTriggerSpell2 { get; set; }
        public ulong CustomFlags { get; set; }
    }

    private string ResolveSnapshotDirectory(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || Path.GetFileName(folder) != folder)
            throw new InvalidDataException("Snapshot folder names must be basenames.");
        var root = Path.GetFullPath(WorldsRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, folder));
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Snapshot folder escapes the configured backup directory.");
        return full;
    }

    private static string NewSnapshotFolder(string kind) =>
        $"{DateTime.UtcNow:yyyy-MM-dd_HH-mm-ss-fff}_{kind}_{Guid.NewGuid():N}";

    private static long DirectorySize(DirectoryInfo dir) =>
        dir.GetFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);

    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return bytes + " B";
        if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("F1") + " KB";
        if (bytes < 1024L * 1024 * 1024) return (bytes / (1024.0 * 1024)).ToString("F1") + " MB";
        return (bytes / (1024.0 * 1024 * 1024)).ToString("F1") + " GB";
    }
}

// ==================================================================
//  MODELS
// ==================================================================

public static class WorldState
{
    public const string Live = "live";
    public const string Suspended = "suspended";
    public const string Archived = "archived";
}

public static class SnapshotKind
{
    public const string Suspend = "suspend";
    public const string Safety = "safety";
    public const string Legacy = "legacy";
    public const string ForkOrigin = "fork-origin";
    public const string RtsSeed = "rts-seed";
}

public static class JobState
{
    public const string Running = "running";
    public const string Done = "done";
    public const string Failed = "failed";
}

public static class StepState
{
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Done = "done";
    public const string Failed = "failed";
    public const string Skipped = "skipped";
}

public class WorldRegistry
{
    public int SchemaVersion { get; set; } = 1;

    /// <summary>The world currently mounted, or null when nothing is loaded.</summary>
    public string? LiveWorldId { get; set; }

    /// <summary>
    /// Whose data physically sits in the databases right now. Survives suspend, which is
    /// what lets "suspend then resume the same world" skip the multi-gigabyte import.
    /// </summary>
    public string? MaterializedWorldId { get; set; }

    public string? MaterializedSnapshot { get; set; }

    public List<WorldRecord> Worlds { get; set; } = new();
}

public class WorldRecord
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>mmo | rts | sandbox | archive | custom — drives the card's icon and colour.</summary>
    public string Flavor { get; set; } = "mmo";

    public string? Notes { get; set; }
    public string? ParentId { get; set; }
    public string? ForkedFromFolder { get; set; }
    public DateTime CreatedUtc { get; set; }
    public string State { get; set; } = WorldState.Suspended;
    public DateTime? LiveSinceUtc { get; set; }
    public DateTime? SuspendedUtc { get; set; }
    public bool IsArchive { get; set; }
    public WorldLaunchConfiguration? LaunchConfiguration { get; set; }
    public List<WorldSnapshot> Snapshots { get; set; } = new();
}

public class WorldSnapshot
{
    public int SchemaVersion { get; set; } = 1;
    public string Folder { get; set; } = "";
    public DateTime TakenUtc { get; set; }
    public string Kind { get; set; } = SnapshotKind.Suspend;
    public string Label { get; set; } = "";
    public string[] Groups { get; set; } = Array.Empty<string>();
    public Dictionary<string, string> Sizes { get; set; } = new();
    public Dictionary<string, object> Stats { get; set; } = new();
    public long TotalBytes { get; set; }
    public List<SnapshotArtifact> Artifacts { get; set; } = new();
    public string? SourceWorldId { get; set; }
    public string? SourceSnapshot { get; set; }
    public string? ProfileId { get; set; }
    public WorldLaunchConfiguration? LaunchConfiguration { get; set; }
    public string? NamePoolSha256 { get; set; }
    public int? NamePoolEligible { get; set; }
}

/// <summary>On-disk manifest.json inside each snapshot folder.</summary>
public class SnapshotManifest
{
    public int SchemaVersion { get; set; } = 1;
    public DateTime Timestamp { get; set; }
    public string Kind { get; set; } = SnapshotKind.Suspend;
    public string Label { get; set; } = "";
    public string[] Groups { get; set; } = Array.Empty<string>();
    public Dictionary<string, string> Sizes { get; set; } = new();
    public Dictionary<string, object> Stats { get; set; } = new();
    public List<SnapshotArtifact> Artifacts { get; set; } = new();
    public string? SourceWorldId { get; set; }
    public string? SourceSnapshot { get; set; }
    public string? ProfileId { get; set; }
    public WorldLaunchConfiguration? LaunchConfiguration { get; set; }
    public string? NamePoolSha256 { get; set; }
    public int? NamePoolEligible { get; set; }
}

public class WorldJob
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Title { get; set; } = "";
    public string State { get; set; } = JobState.Running;
    public string? Error { get; set; }
    public string? ResultFolder { get; set; }
    public DateTime StartedUtc { get; set; }
    public DateTime? FinishedUtc { get; set; }
    public List<WorldJobStep> Steps { get; set; } = new();
}

public class WorldJobStep
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string State { get; set; } = StepState.Pending;
    public string? Detail { get; set; }
    public DateTime? StartedUtc { get; set; }
    public DateTime? FinishedUtc { get; set; }
}
