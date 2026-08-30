using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

namespace MangosSuperUI.Services;

/// <summary>One core, and how much of it a process used since the last sample.</summary>
/// <param name="Percent">
/// Percent of THAT core, so 100 means the core is saturated by this process.
/// This is the number that matters: an aggregate like "0.44 of 32 cores" averages
/// a pegged core and 31 idle ones into a figure that looks like plenty of
/// headroom, which is exactly backwards for a serial world loop.
/// </param>
public sealed record CoreUsage(int Core, double Percent, int Threads);

public sealed record ProcessCoreBreakdown(
    string Name,
    int? Pid,
    bool IsRunning,
    bool Supported,
    int ProcessorCount,
    int ThreadCount,
    double TotalPercent,
    double TotalCores,
    int CoresInUse,
    IReadOnlyList<CoreUsage> Cores);

public sealed record HostCoreUsage(
    bool Supported,
    int ProcessorCount,
    int CoresInUse,
    double TotalPercent,
    IReadOnlyList<CoreUsage> Cores);

/// <summary>
/// Per-core CPU attribution, per process.
///
/// Linux gives each thread the core it last ran on in
/// <c>/proc/&lt;pid&gt;/task/&lt;tid&gt;/stat</c> field 39, alongside its cumulative
/// utime/stime. Delta the CPU time per thread between samples and attribute it to
/// that core and you get the same view <c>top -H</c> and htop show.
///
/// It is an attribution, not a measurement: a thread that migrated mid-interval
/// has all of its time credited to wherever it finished. Over a multi-second
/// window with long-lived threads — which is what the map update threads and the
/// bridge threads are — it tracks reality closely enough to answer the only
/// question being asked: is any single core near saturation?
/// </summary>
public sealed class ProcessCoreSampler
{
    /// <summary>
    /// A core counts as "in use" above this. Below it, a reading is one
    /// scheduling blip in the sample window rather than real work.
    /// </summary>
    internal const double InUseThresholdPercent = 1.0;

    /// <summary>
    /// USER_HZ, the unit of /proc CPU time. Fixed at 100 on Linux regardless of
    /// the kernel's internal HZ — this is the value getconf CLK_TCK reports.
    /// </summary>
    private const double ClockTicksPerSecond = 100d;

    private sealed class ThreadSnapshot
    {
        public readonly Dictionary<int, long> JiffiesByThread = new();
        public long Timestamp;
    }

    private readonly ConcurrentDictionary<int, ThreadSnapshot> _previous = new();
    private readonly ILogger<ProcessCoreSampler> _logger;

    // Host totals come from /proc/stat, which is cumulative like the per-thread
    // counters, so it needs its own baseline.
    private readonly Dictionary<int, (long Busy, long Total)> _previousHost = new();
    private readonly object _hostGate = new();

    public ProcessCoreSampler(ILogger<ProcessCoreSampler> logger) => _logger = logger;

    public ProcessCoreBreakdown Sample(string name, int? pid)
    {
        int cores = Math.Max(1, Environment.ProcessorCount);

        if (!OperatingSystem.IsLinux())
            return Unsupported(name, pid, cores);

        if (pid is not int id || id <= 0)
            return NotRunning(name, pid, cores);

        try
        {
            string taskDir = $"/proc/{id}/task";
            if (!Directory.Exists(taskDir))
            {
                _previous.TryRemove(id, out _);
                return NotRunning(name, pid, cores);
            }

            long now = Stopwatch.GetTimestamp();
            var current = new ThreadSnapshot { Timestamp = now };
            var perCore = new Dictionary<int, (double Percent, int Threads)>();

            _previous.TryGetValue(id, out ThreadSnapshot? previous);
            double elapsedSeconds = previous is null
                ? 0
                : Stopwatch.GetElapsedTime(previous.Timestamp, now).TotalSeconds;

            int threadCount = 0;
            foreach (string threadDir in Directory.EnumerateDirectories(taskDir))
            {
                string tidText = Path.GetFileName(threadDir);
                if (!int.TryParse(tidText, NumberStyles.None, CultureInfo.InvariantCulture, out int tid))
                    continue;

                string? stat = TryReadAllText($"{threadDir}/stat");
                if (stat is null || !TryParseThreadStat(stat, out long jiffies, out int core))
                    continue;

                threadCount++;
                current.JiffiesByThread[tid] = jiffies;

                // A thread we have not seen before contributes nothing yet: its
                // cumulative total is a level, and we need a rate.
                if (previous is null
                    || elapsedSeconds <= 0
                    || !previous.JiffiesByThread.TryGetValue(tid, out long before))
                    continue;

                long delta = jiffies - before;
                if (delta <= 0)
                    continue;

                double percent = 100d * (delta / ClockTicksPerSecond) / elapsedSeconds;
                perCore.TryGetValue(core, out var accumulated);
                perCore[core] = (accumulated.Percent + percent, accumulated.Threads + 1);
            }

            // Threads that vanished drop out naturally: the new snapshot replaces
            // the old one wholesale rather than accumulating dead tids.
            _previous[id] = current;

            List<CoreUsage> usage = perCore
                .Select(kv => new CoreUsage(kv.Key, kv.Value.Percent, kv.Value.Threads))
                .OrderByDescending(c => c.Percent)
                .ThenBy(c => c.Core)
                .ToList();

            double total = usage.Sum(c => c.Percent);
            return new ProcessCoreBreakdown(
                Name: name,
                Pid: id,
                IsRunning: true,
                Supported: true,
                ProcessorCount: cores,
                ThreadCount: threadCount,
                TotalPercent: total,
                TotalCores: total / 100d,
                CoresInUse: usage.Count(c => c.Percent >= InUseThresholdPercent),
                Cores: usage);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Per-core sample for {Name} failed", name);
            return NotRunning(name, pid, cores);
        }
    }

    /// <summary>
    /// Whole-machine per-core load. Without it a process figure has no
    /// denominator: 19% of core 10 reads very differently when the core is
    /// otherwise idle than when something else already has it at 90%.
    /// </summary>
    public HostCoreUsage SampleHost()
    {
        int cores = Math.Max(1, Environment.ProcessorCount);
        if (!OperatingSystem.IsLinux())
            return new HostCoreUsage(false, cores, 0, 0, Array.Empty<CoreUsage>());

        try
        {
            var usage = new List<CoreUsage>();
            lock (_hostGate)
            {
                foreach (string line in File.ReadLines("/proc/stat"))
                {
                    if (!line.StartsWith("cpu", StringComparison.Ordinal))
                        continue;

                    string[] fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    // "cpu" alone is the aggregate row; only per-core "cpuN" rows here.
                    if (fields.Length < 5 || fields[0].Length <= 3
                        || !int.TryParse(fields[0].AsSpan(3), NumberStyles.None, CultureInfo.InvariantCulture, out int core))
                        continue;

                    long total = 0, idle = 0;
                    for (int i = 1; i < fields.Length; i++)
                    {
                        if (!long.TryParse(fields[i], NumberStyles.None, CultureInfo.InvariantCulture, out long v))
                            continue;
                        total += v;
                        // idle (field 4) and iowait (field 5) are both not-working.
                        if (i is 4 or 5)
                            idle += v;
                    }

                    long busy = total - idle;
                    if (_previousHost.TryGetValue(core, out var before))
                    {
                        long deltaTotal = total - before.Total;
                        long deltaBusy = busy - before.Busy;
                        if (deltaTotal > 0 && deltaBusy >= 0)
                            usage.Add(new CoreUsage(core, 100d * deltaBusy / deltaTotal, 0));
                    }

                    _previousHost[core] = (busy, total);
                }
            }

            usage.Sort((a, b) => b.Percent.CompareTo(a.Percent));
            return new HostCoreUsage(
                Supported: true,
                ProcessorCount: cores,
                CoresInUse: usage.Count(c => c.Percent >= InUseThresholdPercent),
                TotalPercent: usage.Sum(c => c.Percent),
                Cores: usage);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Host per-core sample failed");
            return new HostCoreUsage(false, cores, 0, 0, Array.Empty<CoreUsage>());
        }
    }

    /// <summary>
    /// Reads a /proc file, returning null instead of throwing when it vanishes.
    /// Threads exit constantly, so a stat file disappearing between the
    /// directory enumeration and the read is the normal case here, not an error
    /// — that thread simply contributes nothing to this sample.
    /// </summary>
    private static string? TryReadAllText(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>
    /// Pulls utime+stime and the last-run core out of a /proc thread stat line.
    /// The comm field is parenthesised and may contain spaces AND parens
    /// (mangosd-main is tame, but a thread name is arbitrary), so parsing always
    /// starts after the LAST ')' — the classic /proc parsing trap.
    /// </summary>
    internal static bool TryParseThreadStat(string stat, out long jiffies, out int core)
    {
        jiffies = 0;
        core = -1;

        int close = stat.LastIndexOf(')');
        if (close < 0 || close + 2 >= stat.Length)
            return false;

        string[] f = stat[(close + 2)..]
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // f[0] is field 3 (state), so field N lives at index N-3:
        // utime = 14 -> 11, stime = 15 -> 12, processor = 39 -> 36.
        if (f.Length <= 36
            || !long.TryParse(f[11], NumberStyles.None, CultureInfo.InvariantCulture, out long utime)
            || !long.TryParse(f[12], NumberStyles.None, CultureInfo.InvariantCulture, out long stime)
            || !int.TryParse(f[36], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out core))
            return false;

        jiffies = utime + stime;
        return true;
    }

    private static ProcessCoreBreakdown NotRunning(string name, int? pid, int cores)
        => new(name, pid, false, true, cores, 0, 0, 0, 0, Array.Empty<CoreUsage>());

    private static ProcessCoreBreakdown Unsupported(string name, int? pid, int cores)
        => new(name, pid, pid is > 0, false, cores, 0, 0, 0, 0, Array.Empty<CoreUsage>());
}
