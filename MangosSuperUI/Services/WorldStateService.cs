using System.Diagnostics;
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
    private readonly ConnectionFactory _db;
    private readonly ProcessManagerService _proc;
    private readonly IOptionsMonitor<VmangosSettings> _settings;
    private readonly IConfiguration _config;
    private readonly ILogger<WorldStateService> _logger;

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
        ILogger<WorldStateService> logger)
    {
        _db = db;
        _proc = proc;
        _settings = settings;
        _config = config;
        _logger = logger;
    }

    private VmangosSettings Settings => _settings.CurrentValue;
    private string WorldsRoot => string.IsNullOrWhiteSpace(Settings.BackupDirectory) ? "/home/wowvmangos/backups" : Settings.BackupDirectory;
    private string SourceRoot => string.IsNullOrWhiteSpace(Settings.VmangosSourcePath) ? "/home/wowvmangos/vmangos/src" : Settings.VmangosSourcePath;
    private string SqlRoot => string.IsNullOrWhiteSpace(Settings.VmangosSqlPath) ? "/home/wowvmangos/vmangos/sql" : Settings.VmangosSqlPath;
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
                    Folder = dir.Name,
                    TakenUtc = manifest?.Timestamp ?? dir.CreationTimeUtc,
                    Kind = dir.Name.Contains("_pre-restore", StringComparison.OrdinalIgnoreCase)
                        ? SnapshotKind.Safety
                        : SnapshotKind.Legacy,
                    Label = manifest?.Label ?? "",
                    Groups = manifest?.Groups ?? Array.Empty<string>(),
                    Sizes = manifest?.Sizes ?? new(),
                    Stats = manifest?.Stats ?? new(),
                    TotalBytes = DirectorySize(dir)
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
        // Write-then-move so a crash mid-write can't leave a truncated registry.
        var tmp = RegistryPath + ".tmp";
        await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(registry, _json));
        File.Move(tmp, RegistryPath, overwrite: true);
    }

    /// <summary>Read-modify-write the registry under lock.</summary>
    public async Task<T> MutateAsync<T>(Func<WorldRegistry, T> mutate)
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
            var dir = Path.Combine(WorldsRoot, folder);
            Directory.CreateDirectory(dir);

            await RunStep(job, "stop", async () => await StopServerAsync());

            var stats = await GatherStatsAsync();
            await RunStep(job, "dump-world", async () => await DumpGroupAsync("world", dir));
            await RunStep(job, "dump-players", async () => await DumpGroupAsync("players", dir));
            await RunStep(job, "dump-core", async () => await DumpGroupAsync("core", dir));

            await RunStep(job, "park", async () =>
            {
                var snapshot = await WriteManifestAsync(dir, folder, SnapshotKind.Suspend, label ?? "", AllGroups, stats);

                await MutateAsync(reg =>
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
        });
    }

    // ==================================================================
    //  RESUME — mount a world, swapping out whatever is live
    // ==================================================================

    /// <summary>
    /// Mounts <paramref name="worldId"/>. If another world is live it is suspended first —
    /// that combined operation is the swap the UI narrates step by step.
    /// </summary>
    public async Task<WorldJob> ResumeAsync(string worldId, string? snapshotFolder, string? operatorIp)
    {
        var registry = await GetRegistryAsync();

        var target = registry.Worlds.FirstOrDefault(w => w.Id == worldId)
            ?? throw new InvalidOperationException("World not found.");
        if (target.Id == registry.LiveWorldId)
            throw new InvalidOperationException($"“{target.Name}” is already mounted.");

        var outgoing = registry.Worlds.FirstOrDefault(w => w.Id == registry.LiveWorldId);

        // Newest snapshot unless the caller picked a specific one.
        var snapshot = snapshotFolder != null
            ? target.Snapshots.FirstOrDefault(s => s.Folder == snapshotFolder)
            : target.Snapshots.FirstOrDefault();

        // A world with no snapshot can only be resumed if its data is still sitting in the
        // databases — otherwise there is genuinely nothing to mount.
        var alreadyMaterialized = registry.MaterializedWorldId == target.Id
            && (snapshot == null || registry.MaterializedSnapshot == snapshot.Folder);

        if (snapshot == null && !alreadyMaterialized)
            throw new InvalidOperationException($"“{target.Name}” has no snapshot to resume from.");

        var steps = new List<WorldJobStep>();
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

        return await StartJobAsync(outgoing != null ? "swap" : "resume", title, steps, async job =>
        {
            // ---- Phase 1: unload whatever is live ----
            await RunStep(job, "stop", async () => await StopServerAsync());

            if (outgoing != null)
            {
                var folder = NewSnapshotFolder("suspend");
                var dir = Path.Combine(WorldsRoot, folder);
                Directory.CreateDirectory(dir);

                var stats = await GatherStatsAsync();
                await RunStep(job, "dump-world", async () => await DumpGroupAsync("world", dir));
                await RunStep(job, "dump-players", async () => await DumpGroupAsync("players", dir));
                await RunStep(job, "dump-core", async () => await DumpGroupAsync("core", dir));

                await RunStep(job, "park", async () =>
                {
                    var snap = await WriteManifestAsync(dir, folder, SnapshotKind.Suspend,
                        $"Auto-suspended to make room for {target.Name}", AllGroups, stats);

                    await MutateAsync(reg =>
                    {
                        var w = reg.Worlds.First(x => x.Id == outgoing.Id);
                        w.Snapshots.Insert(0, snap);
                        w.State = WorldState.Suspended;
                        w.SuspendedUtc = DateTime.UtcNow;
                        w.LiveSinceUtc = null;
                        reg.LiveWorldId = null;
                        return true;
                    });

                    return FormatBytes(snap.TotalBytes) + " frozen";
                });
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
                var dir = Path.Combine(WorldsRoot, snapshot!.Folder);
                if (!Directory.Exists(dir))
                    throw new InvalidOperationException($"Snapshot folder '{snapshot.Folder}' is missing from disk.");

                await RestoreOrSkip(job, "restore-world", "world", snapshot, dir);
                await RestoreOrSkip(job, "restore-players", "players", snapshot, dir);
                await RestoreOrSkip(job, "restore-core", "core", snapshot, dir);
            }

            // ---- Phase 3: boot ----
            await RunStep(job, "start", async () =>
            {
                await _proc.StartRealmdAsync();
                await Task.Delay(1500);
                await _proc.StartMangosdAsync();

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
                return "mangosd & realmd starting";
            });
        });
    }

    private async Task RestoreOrSkip(WorldJob job, string stepKey, string group, WorldSnapshot snapshot, string dir)
    {
        if (!snapshot.Groups.Contains(group, StringComparer.OrdinalIgnoreCase))
        {
            SkipStep(job, stepKey, "Not captured in this snapshot");
            return;
        }
        await RunStep(job, stepKey, async () => await RestoreGroupAsync(group, dir));
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
        if (!snapshot.Groups.Contains(group, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"'{group}' was not captured in this snapshot.");

        var dir = Path.Combine(WorldsRoot, snapshot.Folder);
        if (!Directory.Exists(dir))
            throw new InvalidOperationException($"Snapshot folder '{snapshot.Folder}' is missing from disk.");

        var steps = new List<WorldJobStep>
        {
            Step("stop", "Make sure mangosd & realmd are stopped"),
            Step("restore", $"Graft “{group}” from {snapshot.Folder}")
        };

        return await StartJobAsync("restore-group", $"Restoring {group} from {snapshot.Folder}", steps, async job =>
        {
            await RunStep(job, "stop", async () => await StopServerAsync());
            await RunStep(job, "restore", async () => await RestoreGroupAsync(group, dir));

            // The databases now hold a mix of two worlds, so no world is cleanly materialized.
            // Clearing this forces the next resume to do a full import rather than skipping it.
            await MutateAsync(reg =>
            {
                reg.MaterializedWorldId = null;
                reg.MaterializedSnapshot = null;
                return true;
            });
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
        return await MutateAsync(reg =>
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
        Folder = source.Folder,
        TakenUtc = source.TakenUtc,
        Kind = SnapshotKind.ForkOrigin,
        Label = $"Forked from {parentName}" + (string.IsNullOrEmpty(source.Label) ? "" : $" — {source.Label}"),
        Groups = source.Groups,
        Sizes = source.Sizes,
        Stats = source.Stats,
        TotalBytes = source.TotalBytes
    };

    public async Task<WorldRecord> UpdateAsync(string worldId, string? name, string? flavor, string? notes)
    {
        return await MutateAsync(reg =>
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
        return await MutateAsync(reg =>
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
    }

    /// <summary>Deletes one snapshot. Refuses if it is the world's only way back.</summary>
    public async Task<bool> DeleteSnapshotAsync(string worldId, string folder)
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
    }

    private bool TryDeleteFolder(string folder)
    {
        try
        {
            var dir = Path.Combine(WorldsRoot, Path.GetFileName(folder));
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
        try { await _proc.StopMangosdAsync(); } catch (Exception ex) { _logger.LogWarning(ex, "mangosd stop reported an error (may already be down)"); }
        try { await _proc.StopRealmdAsync(); } catch (Exception ex) { _logger.LogWarning(ex, "realmd stop reported an error (may already be down)"); }
        // Give mangosd a moment to flush its final saves before we read the tables.
        await Task.Delay(3000);
        return "stopped";
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
                break;
            }
            case "core":
            {
                var confPath = string.IsNullOrWhiteSpace(Settings.MangosdConfPath)
                    ? "/home/wowvmangos/vmangos/run/etc/mangosd.conf"
                    : Settings.MangosdConfPath;
                var outputPath = Path.Combine(dir, "core_source.tar.gz");

                var args = new List<string> { "czf", outputPath };
                if (Directory.Exists(SourceRoot))
                    args.AddRange(new[] { "-C", Path.GetDirectoryName(SourceRoot)!, Path.GetFileName(SourceRoot) });
                if (Directory.Exists(SqlRoot))
                    args.AddRange(new[] { "-C", Path.GetDirectoryName(SqlRoot)!, Path.GetFileName(SqlRoot) });
                if (File.Exists(confPath))
                    args.AddRange(new[] { "-C", Path.GetDirectoryName(confPath)!, Path.GetFileName(confPath) });

                await RunProcess("tar", string.Join(" ", args.Select(a => $"\"{a}\"")));
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

                await RunMysqlRestore(host, port, user, pass, "mangos", mangosPath);

                var adminPath = Path.Combine(dir, "world_vmangos_admin.sql.gz");
                if (File.Exists(adminPath))
                    await RunMysqlRestore(host, port, user, pass, "vmangos_admin", adminPath);
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
                if (File.Exists(realmPath))
                    await RunMysqlRestore(host, port, user, pass, "realmd", realmPath);
                return "characters + realmd";
            }
            case "core":
            {
                var archivePath = Path.Combine(dir, "core_source.tar.gz");
                if (!File.Exists(archivePath))
                    throw new FileNotFoundException("core_source.tar.gz missing from snapshot");

                var srcParent = Path.GetDirectoryName(SourceRoot) ?? "/home/wowvmangos/vmangos";
                await RunProcess("tar", $"xzf \"{archivePath}\" -C \"{srcParent}\"");
                return "src + sql + conf";
            }
            default:
                throw new InvalidOperationException("Unknown group: " + group);
        }
    }

    private async Task<WorldSnapshot> WriteManifestAsync(
        string dir, string folder, string kind, string label, string[] groups, Dictionary<string, object> stats)
    {
        var sizes = new Dictionary<string, string>();
        foreach (var g in groups)
        {
            var bytes = new DirectoryInfo(dir).GetFiles($"{g}*").Sum(f => f.Length);
            sizes[g] = FormatBytes(bytes);
        }

        var manifest = new SnapshotManifest
        {
            Timestamp = DateTime.UtcNow,
            Kind = kind,
            Label = label,
            Groups = groups,
            Sizes = sizes,
            Stats = stats
        };

        await File.WriteAllTextAsync(Path.Combine(dir, "manifest.json"), JsonSerializer.Serialize(manifest, _json));

        return new WorldSnapshot
        {
            Folder = folder,
            TakenUtc = manifest.Timestamp,
            Kind = kind,
            Label = label,
            Groups = groups,
            Sizes = sizes,
            Stats = stats,
            TotalBytes = DirectorySize(new DirectoryInfo(dir))
        };
    }

    // ==================================================================
    //  SHELL HELPERS
    // ==================================================================

    private async Task RunMysqlDump(string host, string port, string user, string pass, string database, string outputPath)
    {
        var cmd = $"mysqldump -h{host} -P{port} -u{user} -p{pass} --single-transaction --routines --triggers {database} | gzip > \"{outputPath}\"";
        await RunBash(cmd);
    }

    private async Task RunMysqlRestore(string host, string port, string user, string pass, string database, string inputPath)
    {
        var dropCreate = $"mysql -h{host} -P{port} -u{user} -p{pass} -e \"DROP DATABASE IF EXISTS \\`{database}\\`; CREATE DATABASE \\`{database}\\`;\"";
        await RunBash(dropCreate);

        var restore = $"gunzip < \"{inputPath}\" | mysql -h{host} -P{port} -u{user} -p{pass} {database}";
        await RunBash(restore);
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

    private static string NewSnapshotFolder(string kind) =>
        DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + "_" + kind;

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
    public List<WorldSnapshot> Snapshots { get; set; } = new();
}

public class WorldSnapshot
{
    public string Folder { get; set; } = "";
    public DateTime TakenUtc { get; set; }
    public string Kind { get; set; } = SnapshotKind.Suspend;
    public string Label { get; set; } = "";
    public string[] Groups { get; set; } = Array.Empty<string>();
    public Dictionary<string, string> Sizes { get; set; } = new();
    public Dictionary<string, object> Stats { get; set; } = new();
    public long TotalBytes { get; set; }
}

/// <summary>On-disk manifest.json inside each snapshot folder.</summary>
public class SnapshotManifest
{
    public DateTime Timestamp { get; set; }
    public string Kind { get; set; } = SnapshotKind.Suspend;
    public string Label { get; set; } = "";
    public string[] Groups { get; set; } = Array.Empty<string>();
    public Dictionary<string, string> Sizes { get; set; } = new();
    public Dictionary<string, object> Stats { get; set; } = new();
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
