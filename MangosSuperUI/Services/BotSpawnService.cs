using System.Diagnostics;
using Dapper;
using MangosSuperUI.Hubs;
using MangosSuperUI.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace MangosSuperUI.Services;

/// <summary>
/// Background runner for the Bot Monitor "Add Bots" batch.
///
/// The batch used to be spawned inside the POST /Bots/AddBots request itself: one `.bot addai`
/// RA round-trip per bot, serially, with the HTTP request held open and a hard 200-per-request
/// cap. Each command costs roughly one world tick, so a big batch meant a long request with no
/// progress, and a single RA timeout aborted whatever was left of the batch.
///
/// Now the request validates, draws the names and returns immediately; the RA loop runs here on
/// a background task, one batch at a time, and streams <c>SpawnProgress</c> snapshots over the
/// BotBridge hub (GET /Bots/AddBotsStatus is the polling fallback). A failed RA send is retried
/// once after RaService reconnects; a bot the core refuses is counted as failed and the batch
/// keeps going; three transport failures in a row abort the batch ("RA unreachable") instead of
/// grinding through thousands of retries.
///
/// The per-batch ceiling is BotSpawn:MaxPerRequest (default 4000, 0 = unlimited). The real
/// lifetime ceiling is the unused-name pool in wwwroot/data — a batch that would exhaust it is
/// refused up front, exactly as before.
/// </summary>
public sealed class BotSpawnService
{
    public sealed record SpawnRequest(string Race, string Cls);

    public sealed class SpawnJobSnapshot
    {
        public string Id { get; init; } = "";
        public string Phase { get; init; } = "";   // running | done | cancelled | failed
        public int Requested { get; init; }
        public int Sent { get; init; }
        public int Failed { get; init; }
        public string? Current { get; init; }      // name being spawned right now
        public string? Error { get; init; }
        public List<string> FailedNames { get; init; } = new();
        public long ElapsedMs { get; init; }
        public DateTime StartedUtc { get; init; }
        public DateTime? FinishedUtc { get; init; }
    }

    private sealed class SpawnJob
    {
        public readonly string Id = Guid.NewGuid().ToString("N")[..8];
        public string Phase = "running";
        public int Requested;
        public int Sent;
        public int Failed;
        public string? Current;
        public string? Error;
        public readonly List<string> FailedNames = new();
        public readonly Stopwatch Clock = Stopwatch.StartNew();
        public readonly DateTime StartedUtc = DateTime.UtcNow;
        public DateTime? FinishedUtc;
        public readonly CancellationTokenSource Cts = new();
    }

    private enum Outcome { Sent, CoreRefused, TransportFailure, Cancelled }

    private const int MaxConsecutiveTransportFailures = 3;
    private const int RetryDelayMs = 1000;
    private const int ProgressPushIntervalMs = 250;
    private const string NameListRelativePath = "data/wow_era_5000_names.txt";

    private readonly RaService _ra;
    private readonly ConnectionFactory _db;
    private readonly IHubContext<BotBridgeHub> _hub;
    private readonly IWebHostEnvironment _env;
    private readonly IOptionsMonitor<BotSpawnSettings> _settings;
    private readonly IOptionsMonitor<VmangosSettings> _vmangos;
    private readonly ILogger<BotSpawnService> _logger;

    private readonly object _gate = new();
    private SpawnJob? _job;   // the running batch, or the last finished one

    public BotSpawnService(
        RaService ra,
        ConnectionFactory db,
        IHubContext<BotBridgeHub> hub,
        IWebHostEnvironment env,
        IOptionsMonitor<BotSpawnSettings> settings,
        IOptionsMonitor<VmangosSettings> vmangos,
        ILogger<BotSpawnService> logger)
    {
        _ra = ra;
        _db = db;
        _hub = hub;
        _env = env;
        _settings = settings;
        _vmangos = vmangos;
        _logger = logger;
    }

    /// <summary>Configured per-batch ceiling; 0 means unlimited (bounded by unused names only).</summary>
    public int MaxPerRequest => Math.Max(0, _settings.CurrentValue.MaxPerRequest);

    public bool IsRunning
    {
        get { lock (_gate) return _job is { Phase: "running" }; }
    }

    /// <summary>The running batch, or the last one that finished. Null if none has run yet.</summary>
    public SpawnJobSnapshot? Snapshot()
    {
        lock (_gate) return _job == null ? null : Snapshot(_job);
    }

    /// <summary>
    /// Validates the batch against the ceiling and the name pool, then starts it in the background.
    /// Returns (job, null) on success or (current snapshot, error) when refused.
    /// </summary>
    public async Task<(SpawnJobSnapshot? Job, string? Error)> StartAsync(IReadOnlyList<SpawnRequest> spawns)
    {
        if (spawns.Count == 0)
            return (Snapshot(), "Nothing to spawn");

        var max = MaxPerRequest;
        if (max > 0 && spawns.Count > max)
            return (Snapshot(), $"Too many at once (max {max} per batch — BotSpawn:MaxPerRequest)");

        if (IsRunning)
            return (Snapshot(), "A spawn batch is already running");

        List<string> names;
        try
        {
            names = await DrawNamesAsync(spawns.Count);
        }
        catch (Exception ex)
        {
            return (Snapshot(), ex.Message);
        }

        var job = new SpawnJob { Requested = spawns.Count };
        lock (_gate)
        {
            if (_job is { Phase: "running" })
                return (Snapshot(_job), "A spawn batch is already running");
            _job = job;
        }

        _logger.LogInformation("AddBots: batch {Id} started — {Count} bot(s)", job.Id, job.Requested);
        _ = Task.Run(() => RunAsync(job, spawns, names));
        return (Snapshot(), null);
    }

    /// <summary>Stops the running batch after the in-flight command completes. False if none is running.</summary>
    public bool Cancel()
    {
        lock (_gate)
        {
            if (_job is not { Phase: "running" })
                return false;
            _job.Cts.Cancel();
            return true;
        }
    }

    private async Task RunAsync(SpawnJob job, IReadOnlyList<SpawnRequest> spawns, List<string> names)
    {
        var ct = job.Cts.Token;
        long lastPush = 0;
        int consecutiveTransportFailures = 0;
        string? lastFailure = null;

        try
        {
            for (int i = 0; i < spawns.Count; i++)
            {
                if (ct.IsCancellationRequested)
                {
                    lock (_gate) job.Phase = "cancelled";
                    break;
                }

                var (race, cls) = spawns[i];
                var name = names[i];
                lock (_gate) job.Current = name;

                var (outcome, failure) = await SendOneAsync($".bot addai {cls} {race} {name}", name, ct);
                if (failure != null)
                    lastFailure = failure;
                if (outcome == Outcome.Cancelled)
                {
                    lock (_gate) job.Phase = "cancelled";
                    break;
                }

                lock (_gate)
                {
                    if (outcome == Outcome.Sent)
                        job.Sent++;
                    else
                    {
                        job.Failed++;
                        job.FailedNames.Add(name);
                    }
                }

                consecutiveTransportFailures = outcome == Outcome.TransportFailure ? consecutiveTransportFailures + 1 : 0;
                if (consecutiveTransportFailures >= MaxConsecutiveTransportFailures)
                {
                    lock (_gate)
                    {
                        job.Phase = "failed";
                        job.Error = $"RA unreachable — aborted after {consecutiveTransportFailures} consecutive send failures";
                    }
                    break;
                }

                if (job.Clock.ElapsedMilliseconds - lastPush >= ProgressPushIntervalMs)
                {
                    lastPush = job.Clock.ElapsedMilliseconds;
                    await PushAsync(job);
                }
            }

            lock (_gate)
            {
                if (job.Phase == "running")
                {
                    // Every bot failing is a failed batch, whatever the size — not a "done" one.
                    if (job.Sent == 0 && job.Failed > 0)
                    {
                        job.Phase = "failed";
                        job.Error = lastFailure ?? "No bots were spawned";
                    }
                    else
                        job.Phase = "done";
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AddBots: batch {Id} crashed", job.Id);
            lock (_gate)
            {
                job.Phase = "failed";
                job.Error = ex.Message;
            }
        }
        finally
        {
            lock (_gate)
            {
                job.Current = null;
                job.FinishedUtc = DateTime.UtcNow;
            }
            job.Clock.Stop();
            _logger.LogInformation("AddBots: batch {Id} {Phase} — {Sent}/{Requested} sent, {Failed} failed, {Ms} ms",
                job.Id, job.Phase, job.Sent, job.Requested, job.Failed, job.Clock.ElapsedMilliseconds);
            await PushAsync(job);
        }
    }

    private async Task<(Outcome Outcome, string? Failure)> SendOneAsync(string command, string name, CancellationToken ct)
    {
        string? failure = null;
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                // Deliberately no cancellation token on the RA call: RaService disconnects on ANY
                // exception, so a cancel must let the in-flight command finish and stop before the
                // next one rather than tear the connection down.
                var response = await _ra.SendCommandAsync(command);
                if (IsCoreRefusal(response))
                {
                    _logger.LogWarning("AddBots: core refused {Name}: {Response}", name, response);
                    return (Outcome.CoreRefused, "core: " + response.Trim());
                }
                return (Outcome.Sent, null);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AddBots: RA send failed for {Name} (attempt {Attempt}/2)", name, attempt);
                failure = "RA: " + ex.Message;
                if (attempt == 2)
                    return (Outcome.TransportFailure, failure);
                try
                {
                    await Task.Delay(RetryDelayMs, ct);   // RaService reconnects lazily on the retry
                }
                catch (OperationCanceledException)
                {
                    return (Outcome.Cancelled, failure);
                }
            }
        }
        return (Outcome.TransportFailure, failure);
    }

    // `.bot addai` answers "[AiBot] Spawned ..." on success; everything the handler rejects comes
    // back as one of these (RaService strips the RA +/- prompt marker, so match on the text).
    private static bool IsCoreRefusal(string response) =>
        response.Contains("[AiBot] Error", StringComparison.OrdinalIgnoreCase)
        || response.Contains("Invalid race/class", StringComparison.OrdinalIgnoreCase)
        || response.Contains("Unknown class", StringComparison.OrdinalIgnoreCase)
        || response.Contains("Unknown race", StringComparison.OrdinalIgnoreCase);

    private async Task PushAsync(SpawnJob job)
    {
        SpawnJobSnapshot snapshot;
        lock (_gate) snapshot = Snapshot(job);
        try
        {
            await _hub.Clients.All.SendAsync("SpawnProgress", snapshot);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "AddBots: progress push failed");
        }
    }

    // Caller holds _gate.
    private static SpawnJobSnapshot Snapshot(SpawnJob j) => new()
    {
        Id = j.Id,
        Phase = j.Phase,
        Requested = j.Requested,
        Sent = j.Sent,
        Failed = j.Failed,
        Current = j.Current,
        Error = j.Error,
        FailedNames = new List<string>(j.FailedNames),
        ElapsedMs = j.Clock.ElapsedMilliseconds,
        StartedUtc = j.StartedUtc,
        FinishedUtc = j.FinishedUtc,
    };

    // ==================== Name pool ====================

    /// <summary>
    /// Reads the era name list from wwwroot/data, keeps valid 1.12 names (2-12 letters), and drops
    /// any already used by an existing character (the name column is unique). Throws if the file is
    /// missing. The count is the lifetime ceiling on bots spawned through this path.
    /// </summary>
    public async Task<List<string>> LoadAvailableNamesAsync()
    {
        var root = _env.WebRootPath;
        if (string.IsNullOrEmpty(root))
            root = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        var path = Path.Combine(root, NameListRelativePath);
        if (!File.Exists(path))
            throw new Exception("Name list not found at wwwroot/" + NameListRelativePath);

        var all = (await File.ReadAllLinesAsync(path))
            .Select(l => l.Trim())
            .Where(l => l.Length >= 2 && l.Length <= 12 && l.All(char.IsLetter))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        using var charConn = _db.Characters();
        var taken = (await charConn.QueryAsync<string>("SELECT name FROM characters"))
            .Where(n => !string.IsNullOrEmpty(n))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return all.Where(n => !taken.Contains(n)).ToList();
    }

    private async Task<List<string>> DrawNamesAsync(int need)
    {
        var avail = await LoadAvailableNamesAsync();
        if (avail.Count < need)
            throw new Exception($"Only {avail.Count} unused names available (need {need}). Add more names to the list.");

        var rng = Random.Shared;
        for (int i = avail.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (avail[i], avail[j]) = (avail[j], avail[i]);
        }
        return avail.Take(need).ToList();
    }

    // ==================== PlayerLimit ====================

    /// <summary>
    /// mangosd's PlayerLimit, read from the configured mangosd.conf. Bot sessions are plain
    /// SEC_PLAYER sessions to the core, so they count against it: once bots + players exceed it,
    /// non-GM players are put in the login queue. Null when the conf can't be read.
    /// </summary>
    public async Task<int?> ReadPlayerLimitAsync()
    {
        var path = _vmangos.CurrentValue.MangosdConfPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;
        try
        {
            var doc = await MangosdConfigDocument.LoadAsync(path);
            return doc.GetInt("PlayerLimit");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "AddBots: could not read PlayerLimit from {Path}", path);
            return null;
        }
    }
}
