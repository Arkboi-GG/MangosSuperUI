using System.Diagnostics;

namespace MangosSuperUI.Services;

/// <summary>One phase of the brain loop, summarised over the recent sample ring.</summary>
public sealed record BrainLoopPhaseStats(
    string Phase,
    long Iterations,
    int SampleCount,
    double LastMilliseconds,
    double MedianMilliseconds,
    double P95Milliseconds,
    double MaxMilliseconds,
    double PeakMillisecondsSinceLastRead,
    bool PeakWasReset);

public sealed record BrainLoopStats(
    int TrackedContexts,
    BrainLoopPhaseStats RosterSync,
    BrainLoopPhaseStats BrainTicks,
    BrainLoopPhaseStats FleetReport,
    BrainLoopPhaseStats LoopIteration);

/// <summary>
/// Timing for the 4 Hz brain loop, shared between <see cref="BotBrainService"/>
/// (which records) and <see cref="RuntimeScaleDiagnosticsService"/> (which
/// reports). It exists as its own singleton precisely so the diagnostics service
/// does not have to take a dependency on the brain.
///
/// Why it is worth measuring before anything is optimised: one pass builds two
/// roster-sized dictionaries and awaits a per-bot mutation gate. At 692 bots
/// that is roughly 5,500 semaphore waits/s; at 10,000 it is ~80,000/s plus
/// ~160,000 dictionary entries/s of garbage. That scan is both an allocation
/// source and the work the eligible/due-queue rewrite is meant to remove, so it
/// needs a before-number now, while the fleet is still small enough to be safe.
/// </summary>
public sealed class BrainLoopMetrics
{
    /// <summary>Recent-sample ring per phase. 512 samples at 4 Hz is ~2 minutes.</summary>
    internal const int SampleCapacity = 512;

    private readonly PhaseRing _rosterSync = new("roster-sync");
    private readonly PhaseRing _brainTicks = new("brain-ticks");
    private readonly PhaseRing _fleetReport = new("fleet-report");
    private readonly PhaseRing _loopIteration = new("loop-iteration");
    private int _trackedContexts;

    public void RecordRosterSync(TimeSpan elapsed) => _rosterSync.Record(elapsed);
    public void RecordBrainTicks(TimeSpan elapsed) => _brainTicks.Record(elapsed);
    public void RecordFleetReport(TimeSpan elapsed) => _fleetReport.Record(elapsed);
    public void RecordLoopIteration(TimeSpan elapsed) => _loopIteration.Record(elapsed);

    /// <summary>Roster size observed by the most recent loop pass.</summary>
    public void RecordTrackedContexts(int count) => Volatile.Write(ref _trackedContexts, count);

    /// <param name="resetPeaks">
    /// True only for the periodic scale sampler, which owns the "since last
    /// sample" window.
    /// </param>
    public BrainLoopStats GetStats(bool resetPeaks = false) => new(
        TrackedContexts: Volatile.Read(ref _trackedContexts),
        RosterSync: _rosterSync.Summarise(resetPeaks),
        BrainTicks: _brainTicks.Summarise(resetPeaks),
        FleetReport: _fleetReport.Summarise(resetPeaks),
        LoopIteration: _loopIteration.Summarise(resetPeaks));

    /// <summary>
    /// Times <paramref name="record"/> even when the body throws, so an
    /// exception path cannot silently drop the sample that explains it.
    /// </summary>
    public static async Task TimeAsync(Action<TimeSpan> record, Func<Task> body)
    {
        long start = Stopwatch.GetTimestamp();
        try
        {
            await body();
        }
        finally
        {
            record(Stopwatch.GetElapsedTime(start));
        }
    }

    private sealed class PhaseRing
    {
        private readonly string _phase;
        private readonly object _gate = new();
        private readonly double[] _samples = new double[SampleCapacity];
        private int _count;
        private int _next;
        private long _iterations;
        private double _last;
        private double _peakSinceLastRead;

        public PhaseRing(string phase) => _phase = phase;

        public void Record(TimeSpan elapsed)
        {
            double ms = elapsed.TotalMilliseconds;
            if (double.IsNaN(ms) || ms < 0)
                ms = 0;

            lock (_gate)
            {
                _iterations++;
                _last = ms;
                if (ms > _peakSinceLastRead)
                    _peakSinceLastRead = ms;
                _samples[_next] = ms;
                _next = (_next + 1) % SampleCapacity;
                if (_count < SampleCapacity)
                    _count++;
            }
        }

        public BrainLoopPhaseStats Summarise(bool resetPeak)
        {
            double[] ordered;
            long iterations;
            double last;
            double peak;
            int count;

            lock (_gate)
            {
                count = _count;
                iterations = _iterations;
                last = _last;
                peak = _peakSinceLastRead;
                if (resetPeak)
                    _peakSinceLastRead = 0;
                ordered = new double[count];
                Array.Copy(_samples, ordered, count);
            }

            if (count == 0)
                return new BrainLoopPhaseStats(_phase, iterations, 0, 0, 0, 0, 0, peak, resetPeak);

            Array.Sort(ordered);
            return new BrainLoopPhaseStats(
                Phase: _phase,
                Iterations: iterations,
                SampleCount: count,
                LastMilliseconds: last,
                MedianMilliseconds: Percentile(ordered, 0.50),
                P95Milliseconds: Percentile(ordered, 0.95),
                MaxMilliseconds: ordered[^1],
                PeakMillisecondsSinceLastRead: peak,
                PeakWasReset: resetPeak);
        }

        /// <summary>Nearest-rank percentile over an ascending, non-empty array.</summary>
        private static double Percentile(double[] ascending, double fraction)
        {
            int rank = (int)Math.Ceiling(fraction * ascending.Length) - 1;
            return ascending[Math.Clamp(rank, 0, ascending.Length - 1)];
        }
    }
}
