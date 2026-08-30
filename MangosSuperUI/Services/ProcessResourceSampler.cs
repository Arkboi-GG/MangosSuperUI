using System.Collections.Concurrent;
using System.Diagnostics;

namespace MangosSuperUI.Services;

/// <summary>
/// CPU and memory for one process at one moment.
/// </summary>
/// <param name="CpuPercent">
/// Percent of a SINGLE core, the convention top and htop use, so it can exceed
/// 100 on a multi-threaded process. This is the headline figure deliberately:
/// the world loop is effectively single-threaded, so a pegged core reads as
/// ~100% here but as ~3% once divided across 32 CPUs — which would hide the
/// exact saturation the number exists to reveal.
/// Null on the first sample for a process, because a rate needs two readings.
/// </param>
/// <param name="CpuPercentOfHost">
/// The same measurement divided by core count: "how much of the whole box".
/// </param>
public sealed record ProcessResourceSample(
    string Name,
    int? Pid,
    bool IsRunning,
    double? CpuPercent,
    double? CpuPercentOfHost,
    long? MemoryBytes,
    double? MemoryPercentOfHost,
    long HostTotalMemoryBytes,
    int ProcessorCount);

/// <summary>
/// Samples CPU and resident memory for the processes the dashboard shows.
///
/// CPU is a rate, not a level, so it only exists between two readings: the
/// sampler keeps the previous CPU-time reading per pid and reports the delta
/// over wall-clock elapsed. The first call for a process therefore reports a
/// null CpuPercent rather than a fabricated zero.
/// </summary>
public sealed class ProcessResourceSampler
{
    private readonly record struct CpuBaseline(TimeSpan CpuTime, long Timestamp);

    private readonly ConcurrentDictionary<int, CpuBaseline> _baselines = new();
    private readonly ILogger<ProcessResourceSampler> _logger;

    public ProcessResourceSampler(ILogger<ProcessResourceSampler> logger) => _logger = logger;

    /// <summary>The SuperUI web app itself.</summary>
    public ProcessResourceSample SampleCurrentProcess(string name)
    {
        using Process process = Process.GetCurrentProcess();
        return Sample(name, process);
    }

    /// <summary>
    /// A process located elsewhere (mangosd/realmd via ProcessManagerService).
    /// A null or dead pid yields a not-running sample rather than an error.
    /// </summary>
    public ProcessResourceSample Sample(string name, int? pid)
    {
        if (pid is not int id || id <= 0)
            return NotRunning(name, pid);

        try
        {
            using Process process = Process.GetProcessById(id);
            return Sample(name, process);
        }
        catch (ArgumentException)
        {
            // Exited between the status check and here.
            _baselines.TryRemove(id, out _);
            return NotRunning(name, pid);
        }
        catch (InvalidOperationException)
        {
            _baselines.TryRemove(id, out _);
            return NotRunning(name, pid);
        }
    }

    private ProcessResourceSample Sample(string name, Process process)
    {
        long hostMemory = HostTotalMemoryBytes();
        int cores = Math.Max(1, Environment.ProcessorCount);

        try
        {
            process.Refresh();
            int pid = process.Id;
            long memoryBytes = process.WorkingSet64;
            TimeSpan cpuTime = process.TotalProcessorTime;
            long now = Stopwatch.GetTimestamp();

            double? cpuPercent = null;
            if (_baselines.TryGetValue(pid, out CpuBaseline previous))
            {
                double elapsedSeconds = Stopwatch.GetElapsedTime(previous.Timestamp, now).TotalSeconds;
                double cpuSeconds = (cpuTime - previous.CpuTime).TotalSeconds;

                // A negative delta means the pid was reused or the counter reset;
                // report nothing rather than a nonsense spike.
                if (elapsedSeconds > 0 && cpuSeconds >= 0)
                    cpuPercent = 100d * cpuSeconds / elapsedSeconds;
            }

            _baselines[pid] = new CpuBaseline(cpuTime, now);

            return new ProcessResourceSample(
                Name: name,
                Pid: pid,
                IsRunning: true,
                CpuPercent: cpuPercent,
                CpuPercentOfHost: cpuPercent / cores,
                MemoryBytes: memoryBytes,
                MemoryPercentOfHost: hostMemory > 0 ? 100d * memoryBytes / hostMemory : null,
                HostTotalMemoryBytes: hostMemory,
                ProcessorCount: cores);
        }
        catch (Exception ex)
        {
            // Never let a dashboard metric take down the status endpoint.
            _logger.LogDebug(ex, "Resource sample for {Name} failed", name);
            return NotRunning(name, null);
        }
    }

    private static ProcessResourceSample NotRunning(string name, int? pid) => new(
        Name: name,
        Pid: pid,
        IsRunning: false,
        CpuPercent: null,
        CpuPercentOfHost: null,
        MemoryBytes: null,
        MemoryPercentOfHost: null,
        HostTotalMemoryBytes: HostTotalMemoryBytes(),
        ProcessorCount: Math.Max(1, Environment.ProcessorCount));

    /// <summary>
    /// Physical memory visible to this process. GC info carries it on every
    /// platform, which avoids a /proc/meminfo parse that would be Linux-only.
    /// </summary>
    internal static long HostTotalMemoryBytes()
    {
        long total = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        return total > 0 ? total : 0;
    }
}
