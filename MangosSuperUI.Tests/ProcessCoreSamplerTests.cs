using MangosSuperUI.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MangosSuperUI.Tests;

public sealed class ProcessCoreSamplerTests
{
    /// <summary>
    /// Builds a realistic /proc/&lt;pid&gt;/task/&lt;tid&gt;/stat line. Everything after
    /// the comm is positional, and the fields the sampler wants are utime (14),
    /// stime (15) and processor (39) — so the line needs at least 37 fields past
    /// the closing paren.
    /// </summary>
    private static string StatLine(string comm, long utime, long stime, int processor, int fieldCount = 37)
    {
        var fields = new string[fieldCount];
        for (int i = 0; i < fields.Length; i++)
            fields[i] = "0";

        fields[0] = "S";                                // field 3, state
        if (fieldCount > 11) fields[11] = utime.ToString();      // field 14
        if (fieldCount > 12) fields[12] = stime.ToString();      // field 15
        if (fieldCount > 36) fields[36] = processor.ToString();  // field 39

        return $"1234 ({comm}) " + string.Join(' ', fields);
    }

    [Fact]
    public void ParsesUtimeStimeAndLastRunCore()
    {
        Assert.True(ProcessCoreSampler.TryParseThreadStat(
            StatLine("mangosd-main", utime: 900, stime: 100, processor: 10),
            out long jiffies,
            out int core));

        Assert.Equal(1000, jiffies);   // utime + stime is the thread's CPU time
        Assert.Equal(10, core);
    }

    [Theory]
    [InlineData("weird (thread) name")]
    [InlineData("a)b")]
    [InlineData("((()))")]
    [InlineData("has spaces")]
    public void SurvivesCommsContainingSpacesAndParens(string comm)
    {
        // Thread names are arbitrary and unescaped in /proc. Splitting on the
        // FIRST paren — or on whitespace — silently shifts every field and
        // attributes CPU time to the wrong core. Parsing must start after the LAST ')'.
        Assert.True(ProcessCoreSampler.TryParseThreadStat(
            StatLine(comm, utime: 40, stime: 2, processor: 27),
            out long jiffies,
            out int core));

        Assert.Equal(42, jiffies);
        Assert.Equal(27, core);
    }

    [Fact]
    public void AcceptsNegativeCoreBecauseProcIsAllowedToReportIt()
    {
        Assert.True(ProcessCoreSampler.TryParseThreadStat(
            StatLine("t", utime: 1, stime: 1, processor: -1),
            out _,
            out int core));

        Assert.Equal(-1, core);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no parens here at all")]
    [InlineData("1234 (comm)")]
    public void RejectsMalformedLines(string stat)
    {
        Assert.False(ProcessCoreSampler.TryParseThreadStat(stat, out _, out _));
    }

    [Fact]
    public void RejectsTruncatedLineWithoutTheProcessorField()
    {
        // A short line must fail rather than read a neighbouring field as a core.
        Assert.False(ProcessCoreSampler.TryParseThreadStat(
            StatLine("t", utime: 5, stime: 5, processor: 3, fieldCount: 20),
            out _,
            out _));
    }

    [Fact]
    public void UnknownPidIsReportedNotRunningRatherThanThrowing()
    {
        var sampler = new ProcessCoreSampler(NullLogger<ProcessCoreSampler>.Instance);

        ProcessCoreBreakdown breakdown = sampler.Sample("mangosd", null);

        Assert.False(breakdown.IsRunning);
        Assert.Empty(breakdown.Cores);
        Assert.Equal(0, breakdown.CoresInUse);
        // The machine width is known even with nothing to measure, so the
        // dashboard can still say how many cores exist.
        Assert.True(breakdown.ProcessorCount >= 1);
    }

    [Fact]
    public void SamplingIsSafeOnEveryPlatform()
    {
        var sampler = new ProcessCoreSampler(NullLogger<ProcessCoreSampler>.Instance);

        ProcessCoreBreakdown self = sampler.Sample("superui", Environment.ProcessId);
        HostCoreUsage host = sampler.SampleHost();

        // Off Linux there is no /proc, so the breakdown reports Supported=false
        // and the UI falls back to the aggregate rather than rendering nothing.
        Assert.Equal(OperatingSystem.IsLinux(), self.Supported);
        Assert.Equal(OperatingSystem.IsLinux(), host.Supported);
        Assert.True(self.ProcessorCount >= 1);
        Assert.True(host.ProcessorCount >= 1);
    }

    [Fact]
    public void FirstSampleHasNoRatesBecauseCountersAreCumulative()
    {
        var sampler = new ProcessCoreSampler(NullLogger<ProcessCoreSampler>.Instance);

        ProcessCoreBreakdown first = sampler.Sample("superui", Environment.ProcessId);

        // /proc counters are lifetime totals. Without a baseline the only honest
        // answer is "nothing measured yet", never a percentage.
        Assert.Empty(first.Cores);
        Assert.Equal(0, first.TotalPercent);
        Assert.Equal(0, first.CoresInUse);
    }
}
