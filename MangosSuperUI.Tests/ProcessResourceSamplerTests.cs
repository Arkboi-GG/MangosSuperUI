using System.Diagnostics;
using MangosSuperUI.BotLogic.Tracking;
using MangosSuperUI.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MangosSuperUI.Tests;

public sealed class ProcessResourceSamplerTests
{
    private static ProcessResourceSampler NewSampler()
        => new(NullLogger<ProcessResourceSampler>.Instance);

    [Fact]
    public void FirstSample_ReportsMemoryButNotCpu()
    {
        ProcessResourceSample sample = NewSampler().SampleCurrentProcess("superui");

        Assert.True(sample.IsRunning);
        Assert.Equal("superui", sample.Name);
        Assert.NotNull(sample.Pid);
        Assert.True(sample.MemoryBytes > 0);

        // A rate needs two readings. Reporting 0% here would read as "idle" when
        // it actually means "not measured yet".
        Assert.Null(sample.CpuPercent);
        Assert.Null(sample.CpuPercentOfHost);
    }

    [Fact]
    public void SecondSample_ReportsCpuAgainstTheFirstAsBaseline()
    {
        ProcessResourceSampler sampler = NewSampler();
        sampler.SampleCurrentProcess("superui");

        // Burn a little CPU so the delta covers a meaningful window rather than
        // a few microseconds, where CPU-time quantisation dominates.
        long spinStart = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(spinStart).TotalMilliseconds < 120)
        {
            // deliberate busy work
        }

        ProcessResourceSample sample = sampler.SampleCurrentProcess("superui");

        Assert.NotNull(sample.CpuPercent);
        Assert.True(sample.CpuPercent >= 0, $"cpu was {sample.CpuPercent}");
        Assert.NotNull(sample.CpuPercentOfHost);

        // Per-core percent divided by cores is the whole-box figure.
        Assert.Equal(
            sample.CpuPercent!.Value / sample.ProcessorCount,
            sample.CpuPercentOfHost!.Value,
            5);
    }

    [Fact]
    public void MemoryPercent_IsTheProcessShareOfHostMemory()
    {
        ProcessResourceSample sample = NewSampler().SampleCurrentProcess("superui");

        Assert.True(sample.HostTotalMemoryBytes > 0);
        Assert.NotNull(sample.MemoryPercentOfHost);
        Assert.Equal(
            100d * sample.MemoryBytes!.Value / sample.HostTotalMemoryBytes,
            sample.MemoryPercentOfHost!.Value,
            5);
        Assert.InRange(sample.MemoryPercentOfHost!.Value, 0, 100);
    }

    [Fact]
    public void NullPid_ReportsNotRunningWithoutThrowing()
    {
        ProcessResourceSample sample = NewSampler().Sample("mangosd", null);

        Assert.False(sample.IsRunning);
        Assert.Null(sample.CpuPercent);
        Assert.Null(sample.MemoryBytes);
        Assert.Null(sample.MemoryPercentOfHost);
        // Host capacity is still known even when the process is down, so the
        // dashboard can keep rendering the rest of the card.
        Assert.True(sample.HostTotalMemoryBytes > 0);
        Assert.True(sample.ProcessorCount >= 1);
    }

    [Fact]
    public void DeadPid_ReportsNotRunningRatherThanThrowing()
    {
        // A pid that cannot be live: the sampler is called with whatever
        // ProcessManagerService last saw, which may have exited since.
        ProcessResourceSample sample = NewSampler().Sample("mangosd", int.MaxValue);

        Assert.False(sample.IsRunning);
        Assert.Null(sample.MemoryBytes);
    }

    [Fact]
    public void NegativePid_IsTreatedAsNotRunning()
    {
        Assert.False(NewSampler().Sample("mangosd", -1).IsRunning);
        Assert.False(NewSampler().Sample("mangosd", 0).IsRunning);
    }
}

public sealed class CircuitTraceStartupModeTests
{
    [Theory]
    [InlineData("shadow")]
    [InlineData("Shadow")]
    [InlineData("SHADOW")]
    public void PersistedShadow_IsSuppressedAndReportedSoItCanBeHealed(string persisted)
    {
        var (mode, suppressed) = CircuitTraceHost.ResolveStartupMode(persisted);

        // Shadow cost roughly 1.4 GiB retained and ~20% allocation at 692 bots.
        // A sticky setting is how it stayed on unnoticed across restarts.
        Assert.Equal(CircuitTrace.TraceMode.Off, mode);
        Assert.True(suppressed);
    }

    [Theory]
    [InlineData("off")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("nonsense")]
    public void AnythingElse_StartsOffWithNothingToWarnAbout(string? persisted)
    {
        var (mode, suppressed) = CircuitTraceHost.ResolveStartupMode(persisted);

        Assert.Equal(CircuitTrace.TraceMode.Off, mode);
        Assert.False(suppressed);
    }
}
