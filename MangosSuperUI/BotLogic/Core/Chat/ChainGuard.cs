using System.Collections.Concurrent;

namespace MangosSuperUI.BotLogic.Chat.Coordinator;

/// <summary>
/// Anti-storm guard 2 (CHAT_ARCHITECTURE §9.4): bot→bot reply chains must be provably
/// finite. Every coordinator-emitted line is remembered for 30 s with its chain depth.
/// A stimulus whose SENDER is a bot inherits that bot's last emitted depth; the reply we
/// emit to it carries depth+1; the hard cap drops stimuli at depth ≥ max_bot_chain_depth.
///
/// Depth semantics follow the doc's worked example (which is the authoritative reading —
/// the prose's "+1" is the EMISSION increment):
///   player line = stimulus d0 → bot replies, EMITS d1
///   → other bot hears it: stimulus d1 (penalized 1×chain_penalty) → may chime, EMITS d2
///   → any bot hearing THAT: stimulus d2 ≥ cap(2) → dropped. Dead. Organic, finite.
/// A bot line with no recorded emission (manual SAY_TEXT, pre-C7 sources) is treated as
/// depth 1 — it IS a bot talking, so at most one chime can follow.
/// </summary>
public class ChainGuard
{
    private static readonly TimeSpan Memory = TimeSpan.FromSeconds(30);

    private sealed record Emission(int Depth, DateTime Utc);

    // Last emission per bot NAME (identity via name — the stimulus only carries the name)
    private readonly ConcurrentDictionary<string, Emission> _lastEmission = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Call at actual send time (drain) for every coordinator-emitted line.</summary>
    public void RecordEmission(string botName, int depth)
    {
        _lastEmission[botName.Trim()] = new Emission(depth, DateTime.UtcNow);
    }

    /// <summary>Chain depth of a stimulus whose sender is a BOT (player senders are depth 0 — don't call).</summary>
    public int GetStimulusDepth(string senderBotName)
    {
        if (_lastEmission.TryGetValue(senderBotName.Trim(), out var e) &&
            DateTime.UtcNow - e.Utc < Memory)
            return e.Depth;
        return 1;   // bot spoke outside the coordinator's knowledge — still a bot line
    }

    /// <summary>Housekeeping sweep (cheap; keeps the dictionary bounded).</summary>
    public void Sweep()
    {
        var cutoff = DateTime.UtcNow - Memory;
        foreach (var kv in _lastEmission)
            if (kv.Value.Utc < cutoff) _lastEmission.TryRemove(kv.Key, out _);
    }
}
