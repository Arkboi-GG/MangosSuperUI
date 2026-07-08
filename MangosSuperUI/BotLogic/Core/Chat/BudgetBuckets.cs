using System.Collections.Concurrent;
using MangosSuperUI.BotLogic.Chat.Core;

namespace MangosSuperUI.BotLogic.Chat.Coordinator;

/// <summary>
/// Anti-storm guard 3 (CHAT_ARCHITECTURE §9.4): token buckets. Per-bot
/// (budget.bot_lines_per_min, default 4) and per-zone-per-kind (say 20 / channel 10 /
/// party 10 per minute). Consumed at the SPEAK decision (before scheduling) so a burst
/// can't over-commit the timeline; refilled fractionally by the coordinator's 1 s
/// housekeeping loop. Exhaustion silently drops (debug log at the call site).
/// Capacities are read live from settings on every refill, so slider changes hot-apply.
/// </summary>
public class BudgetBuckets
{
    private sealed class Bucket
    {
        public double Tokens;
        public Bucket(double initial) { Tokens = initial; }
    }

    private readonly ChatSettingsService _settings;
    private readonly ConcurrentDictionary<int, Bucket> _botBuckets = new();
    private readonly ConcurrentDictionary<(int Zone, ChatKind Kind), Bucket> _zoneBuckets = new();
    private DateTime _lastRefill = DateTime.UtcNow;

    public BudgetBuckets(ChatSettingsService settings)
    {
        _settings = settings;
    }

    /// <summary>Both buckets must afford the line; consumes both atomically-enough for chat.</summary>
    public bool TryConsume(int botGuid, int zoneId, ChatKind kind)
    {
        var botBucket = _botBuckets.GetOrAdd(botGuid, _ => new Bucket(BotCap()));
        var zoneBucket = _zoneBuckets.GetOrAdd((zoneId, kind), _ => new Bucket(ZoneCap(kind)));

        lock (botBucket)
        {
            if (botBucket.Tokens < 1) return false;
            lock (zoneBucket)
            {
                if (zoneBucket.Tokens < 1) return false;
                botBucket.Tokens -= 1;
                zoneBucket.Tokens -= 1;
                return true;
            }
        }
    }

    /// <summary>Called from the 1 s housekeeping loop (§9: "budget bucket refills").</summary>
    public void Refill()
    {
        var now = DateTime.UtcNow;
        double elapsed = Math.Clamp((now - _lastRefill).TotalSeconds, 0, 10);
        _lastRefill = now;
        if (elapsed <= 0) return;

        double botCap = BotCap();
        foreach (var b in _botBuckets.Values)
            lock (b) b.Tokens = Math.Min(botCap, b.Tokens + botCap / 60.0 * elapsed);

        foreach (var kv in _zoneBuckets)
        {
            double cap = ZoneCap(kv.Key.Kind);
            var b = kv.Value;
            lock (b) b.Tokens = Math.Min(cap, b.Tokens + cap / 60.0 * elapsed);
        }
    }

    private double BotCap() => Math.Max(1, _settings.GetInt(0, "budget.bot_lines_per_min", 4));

    private double ZoneCap(ChatKind kind) => Math.Max(1, kind switch
    {
        ChatKind.Channel => _settings.GetInt(0, "budget.zone_channel_lines_per_min", 10),
        ChatKind.Party => _settings.GetInt(0, "budget.zone_party_lines_per_min", 10),
        _ => _settings.GetInt(0, "budget.zone_say_lines_per_min", 20)
    });
}
