using System.Text.Json;
using MangosSuperUI.BotLogic.Tracking;
using MangosSuperUI.Services;
using Xunit;

namespace MangosSuperUI.Tests;

public sealed class CircuitEpochTests
{
    private static int _nextGuid = 1_500_000_000;

    [Fact]
    public void Hello_DeserializesOpaqueCircuitEpoch()
    {
        const string json = """
            {"bridgeProtocol":5,"circuitEpoch":"core-20260827T143000Z-a91f","guid":14}
            """;

        BotHelloPayload? hello = JsonSerializer.Deserialize<BotHelloPayload>(json);

        Assert.NotNull(hello);
        Assert.Equal("core-20260827T143000Z-a91f", hello.CircuitEpoch);
    }

    [Fact]
    public void EpochAwareConnection_RequiresExactEpochOnEveryCircuitPayload()
    {
        var conn = new BotConnection { SessionId = 41, Guid = 14 };
        BotBridgeService.AdoptCircuitEpoch(conn, "core-A");

        using JsonDocument exact = JsonDocument.Parse("""{"circuitEpoch":"core-A"}""");
        Assert.True(BotBridgeService.TryResolveCircuitEpoch(
            exact.RootElement,
            conn,
            out string adopted,
            out string acceptedReason));
        Assert.Equal("core-A", adopted);
        Assert.Equal("", acceptedReason);

        using JsonDocument missing = JsonDocument.Parse("{}" );
        Assert.False(BotBridgeService.TryResolveCircuitEpoch(
            missing.RootElement,
            conn,
            out _,
            out string missingReason));
        Assert.Equal("epoch_missing_or_invalid", missingReason);

        using JsonDocument mismatch = JsonDocument.Parse("""{"circuitEpoch":"core-B"}""");
        Assert.False(BotBridgeService.TryResolveCircuitEpoch(
            mismatch.RootElement,
            conn,
            out _,
            out string mismatchReason));
        Assert.Equal("epoch_mismatch", mismatchReason);
    }

    [Fact]
    public void LegacyConnection_GetsPerSocketEpochAndCannotChangeIdentityMidstream()
    {
        var first = new BotConnection { SessionId = 51, Guid = 14 };
        var second = new BotConnection { SessionId = 52, Guid = 14 };
        BotBridgeService.AdoptCircuitEpoch(first, "");
        BotBridgeService.AdoptCircuitEpoch(second, null);

        Assert.Equal("legacy-session-51", first.CircuitEpoch);
        Assert.Equal("legacy-session-52", second.CircuitEpoch);
        Assert.NotEqual(first.CircuitEpoch, second.CircuitEpoch);
        Assert.False(first.CircuitEpochAdvertised);

        using JsonDocument omitted = JsonDocument.Parse("{}");
        Assert.True(BotBridgeService.TryResolveCircuitEpoch(
            omitted.RootElement,
            first,
            out string adopted,
            out _));
        Assert.Equal("legacy-session-51", adopted);

        using JsonDocument injected = JsonDocument.Parse("""{"circuitEpoch":"late-epoch"}""");
        Assert.False(BotBridgeService.TryResolveCircuitEpoch(
            injected.RootElement,
            first,
            out _,
            out string rejection));
        Assert.Equal("epoch_not_declared_by_hello", rejection);
    }

    [Fact]
    public void HelloIdentity_IsOneShotAndInvalidGuidDoesNotConsumeClaim()
    {
        var invalid = new BotConnection { SessionId = 60 };
        Assert.False(BotBridgeService.TryClaimHelloIdentity(invalid, 0, out string invalidReason));
        Assert.Equal("invalid_guid", invalidReason);
        Assert.True(BotBridgeService.TryClaimHelloIdentity(invalid, 14, out _));

        invalid.Guid = 14;
        BotBridgeService.AdoptCircuitEpoch(invalid, "core-A");
        Assert.False(BotBridgeService.TryClaimHelloIdentity(invalid, 15, out string duplicateReason));
        Assert.Equal("hello_already_accepted", duplicateReason);
        Assert.Equal(14, invalid.Guid);
        Assert.Equal("core-A", invalid.CircuitEpoch);
    }

    [Fact]
    public async Task CircuitCommit_RevalidatesConnectionUnderHelloPublicationGate()
    {
        int guid = Interlocked.Increment(ref _nextGuid);
        string epoch = "atomic-" + Guid.NewGuid().ToString("N");
        var bridge = new BotBridgeService(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<BotBridgeService>.Instance,
            hub: null!);
        var original = new BotConnection
        {
            Guid = guid,
            SessionId = 71,
            CircuitEpoch = epoch,
            CircuitEpochAdvertised = true
        };
        var replacement = new BotConnection
        {
            Guid = guid,
            SessionId = 72,
            CircuitEpoch = "replacement-" + Guid.NewGuid().ToString("N"),
            CircuitEpochAdvertised = true
        };
        bridge.Connections[guid] = original;

        object publishGate = Assert.IsType<object>(
            typeof(BotBridgeService)
                .GetField("_connectionPublishGate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.GetValue(bridge));
        var started = new CountdownEvent(2);
        CircuitTrace.TraceMode priorMode = CircuitTrace.Mode;
        CircuitTrace.Mode = CircuitTrace.TraceMode.Shadow;
        Task<bool> siteCommit;
        Task<bool> batchCommit;
        Monitor.Enter(publishGate);
        try
        {
            siteCommit = Task.Run(() =>
            {
                started.Signal();
                return bridge.TryCommitCircuitSite(
                    original, epoch, 91, "Atomic.cpp", 91, "must not commit", out _);
            });
            batchCommit = Task.Run(() =>
            {
                started.Signal();
                return bridge.TryCommitCircuitBatch(
                    original,
                    epoch,
                    guid,
                    1,
                    2,
                    3,
                    4,
                    5,
                    new() { (91, null, null) },
                    0,
                    out _);
            });
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
            bridge.Connections[guid] = replacement;
        }
        finally
        {
            Monitor.Exit(publishGate);
        }

        try
        {
            Assert.False(await siteCommit);
            Assert.False(await batchCommit);
            Assert.DoesNotContain(CircuitTrace.Sites, site => site.RemoteEpoch == epoch);
            Assert.DoesNotContain(CircuitTrace.PeekSegments(guid), segment => segment.RemoteEpoch == epoch);
        }
        finally
        {
            CircuitTrace.Forget(guid);
            CircuitTrace.Mode = priorMode;
        }
    }

    [Fact]
    public void TraceSessionIdentity_IsNonceScopedAndWriterNeverAppends()
    {
        DateTime now = new(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc);
        string first = CircuitTraceHost.CreateTraceSessionId(now, 1, Guid.NewGuid());
        string second = CircuitTraceHost.CreateTraceSessionId(now, 1, Guid.NewGuid());
        Assert.NotEqual(first, second);

        string path = Path.Combine(Path.GetTempPath(), $"circuit-create-new-{Guid.NewGuid():N}.jsonl");
        try
        {
            using (StreamWriter writer = CircuitTraceHost.CreateNewTraceWriter(path))
                writer.WriteLine("sentinel");

            Assert.Throws<IOException>(() => CircuitTraceHost.CreateNewTraceWriter(path));
            Assert.Equal("sentinel", File.ReadAllText(path).Trim());
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void SameRemoteId_InDifferentEpochs_GetsDifferentSelfDescribingSiteIds()
    {
        int guid = Interlocked.Increment(ref _nextGuid);
        string firstEpoch = "test-first-" + Guid.NewGuid().ToString("N");
        string secondEpoch = "test-second-" + Guid.NewGuid().ToString("N");
        CircuitTrace.TraceMode priorMode = CircuitTrace.Mode;
        try
        {
            CircuitTrace.Mode = CircuitTrace.TraceMode.Shadow;
            Assert.Equal(
                CircuitTrace.RemoteSiteRegistration.Added,
                CircuitTrace.RegisterRemoteSite(firstEpoch, 1, "First.cpp", 10, "first meaning"));
            Assert.Equal(
                CircuitTrace.RemoteSiteRegistration.AlreadyRegistered,
                CircuitTrace.RegisterRemoteSite(firstEpoch, 1, "First.cpp", 10, "first meaning"));
            Assert.Equal(
                CircuitTrace.RemoteSiteRegistration.Added,
                CircuitTrace.RegisterRemoteSite(secondEpoch, 1, "Second.cpp", 20, "second meaning"));

            CircuitTrace.IngestRemoteSegment(
                firstEpoch, guid, 0, 1, 1, 2, 3,
                new() { (1, null, null) }, 0);
            CircuitTrace.IngestRemoteSegment(
                secondEpoch, guid, 0, 1, 1, 2, 3,
                new() { (1, null, null) }, 0);

            CircuitTrace.TickSegment[] segments = CircuitTrace.PeekSegments(guid).ToArray();
            Assert.Equal(2, segments.Length);
            int firstSiteId = Assert.Single(segments[0].Hits).SiteId;
            int secondSiteId = Assert.Single(segments[1].Hits).SiteId;
            Assert.NotEqual(firstSiteId, secondSiteId);

            IReadOnlyDictionary<int, CircuitTrace.ProbeSite> sites =
                CircuitTrace.Sites.ToDictionary(site => site.Id);
            Assert.Equal(firstEpoch, sites[firstSiteId].RemoteEpoch);
            Assert.Equal(1, sites[firstSiteId].RemoteId);
            Assert.Equal("cpp/First.cpp", sites[firstSiteId].File);
            Assert.Equal(secondEpoch, sites[secondSiteId].RemoteEpoch);
            Assert.Equal("cpp/Second.cpp", sites[secondSiteId].File);
        }
        finally
        {
            CircuitTrace.Forget(guid);
            CircuitTrace.Mode = priorMode;
        }
    }

    [Fact]
    public void ConflictingSiteWithinEpoch_IsQuarantinedInsteadOfAliased()
    {
        int guid = Interlocked.Increment(ref _nextGuid);
        string epoch = "test-conflict-" + Guid.NewGuid().ToString("N");
        CircuitTrace.TraceMode priorMode = CircuitTrace.Mode;
        try
        {
            CircuitTrace.Mode = CircuitTrace.TraceMode.Shadow;
            Assert.Equal(
                CircuitTrace.RemoteSiteRegistration.Added,
                CircuitTrace.RegisterRemoteSite(epoch, 7, "Original.cpp", 70, "original meaning"));
            Assert.Equal(
                CircuitTrace.RemoteSiteRegistration.Conflict,
                CircuitTrace.RegisterRemoteSite(epoch, 7, "Replacement.cpp", 71, "replacement meaning"));

            CircuitTrace.RemoteIngestResult result = CircuitTrace.IngestRemoteSegment(
                epoch, guid, 1, 2, 3, 4, 5,
                new() { (7, 12d, null) }, 0);

            Assert.Equal(0, result.UnknownSites);
            Assert.Equal(1, result.ConflictedSites);
            int siteId = Assert.Single(Assert.Single(CircuitTrace.PeekSegments(guid)).Hits).SiteId;
            CircuitTrace.ProbeSite site = Assert.Single(CircuitTrace.Sites, site => site.Id == siteId);
            Assert.Equal("cpp/<circuit-site-conflict>", site.File);
            Assert.Contains("reused inside epoch", site.Description);
            Assert.Equal(epoch, site.RemoteEpoch);
            Assert.Equal(7, site.RemoteId);
        }
        finally
        {
            CircuitTrace.Forget(guid);
            CircuitTrace.Mode = priorMode;
        }
    }

    [Fact]
    public void BatchBeforeManifest_UsesExplicitPlaceholderThenRecovers()
    {
        int guid = Interlocked.Increment(ref _nextGuid);
        string epoch = "test-unregistered-" + Guid.NewGuid().ToString("N");
        CircuitTrace.TraceMode priorMode = CircuitTrace.Mode;
        try
        {
            CircuitTrace.Mode = CircuitTrace.TraceMode.Shadow;
            CircuitTrace.RemoteIngestResult unknown = CircuitTrace.IngestRemoteSegment(
                epoch, guid, 0, 0, 0, 0, 0,
                new() { (9, null, null) }, 0);
            Assert.Equal(1, unknown.UnknownSites);

            Assert.Equal(
                CircuitTrace.RemoteSiteRegistration.Added,
                CircuitTrace.RegisterRemoteSite(epoch, 9, "Late.cpp", 90, "late manifest"));
            CircuitTrace.RemoteIngestResult known = CircuitTrace.IngestRemoteSegment(
                epoch, guid, 0, 0, 0, 0, 0,
                new() { (9, null, null) }, 0);
            Assert.Equal(0, known.UnknownSites);

            CircuitTrace.TickSegment[] segments = CircuitTrace.PeekSegments(guid).ToArray();
            int placeholderId = Assert.Single(segments[0].Hits).SiteId;
            int registeredId = Assert.Single(segments[1].Hits).SiteId;
            Assert.NotEqual(placeholderId, registeredId);
            IReadOnlyDictionary<int, CircuitTrace.ProbeSite> sites =
                CircuitTrace.Sites.ToDictionary(site => site.Id);
            Assert.Equal("cpp/<unregistered-circuit-site>", sites[placeholderId].File);
            Assert.Equal("cpp/Late.cpp", sites[registeredId].File);
        }
        finally
        {
            CircuitTrace.Forget(guid);
            CircuitTrace.Mode = priorMode;
        }
    }
}
