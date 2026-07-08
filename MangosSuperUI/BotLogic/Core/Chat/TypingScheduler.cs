using MangosSuperUI.BotLogic.Chat.Core;

namespace MangosSuperUI.BotLogic.Chat.Coordinator;

/// <summary>One message on the send timeline, drained by the coordinator's housekeeping loop.</summary>
public sealed record ScheduledSend(int BotGuid, string Text, int ChatTypeWire, string? Target,
                                   string? Channel, DateTime SendUtc, string ConversationKey,
                                   DateTime HoldUntil, DateTime ReadyAt, int ChainDepth);

/// <summary>
/// Owns the send timeline (CHAT_ARCHITECTURE §11 — formula LOCKED, D8/D15):
///
///   thinkMs   = U(think_min_s, think_max_s) * 1000
///   typeMs    = lineChars / (wpm * 5 / 60) * 1000
///   holdUntil = recvUtc + clamp(thinkMs + typeMs, 3000, 45000)
///   sendUtc   = max(inferenceReadyUtc, holdUntil)
///
/// Splitting: line > split_threshold_chars → 2 messages at the nearest sentence boundary,
/// second +U(2,6) s, its typeMs NOT recomputed (the hold covered it), max 2 per reply.
/// Alt-tab tail: alt_tab_chance adds U(60,120) s; on whisper+threadActive allowed at 1/3
/// rate (briefly ghosting mid-whisper is peak 2005). voice.wpm_mult scales typing speed;
/// voice.split_aggressiveness scales split_threshold inversely.
/// Implementation: one PriorityQueue drained at 1 s resolution; sends cancellable by
/// (botGuid, conversationKey) — used when chat_enabled flips off (and §9.5(5) in C7).
/// </summary>
public class TypingScheduler
{
    private readonly ChatSettingsService _settings;
    private readonly ILogger<TypingScheduler> _logger;
    private readonly object _gate = new();
    private readonly PriorityQueue<ScheduledSend, DateTime> _queue = new();
    private readonly HashSet<(int BotGuid, string Key)> _cancelled = new();

    public TypingScheduler(ChatSettingsService settings, ILogger<TypingScheduler> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    /// <summary>Schedule one reply (possibly split into two sends). Returns the scheduled sends.</summary>
    public List<ScheduledSend> Schedule(int botGuid, string line, PersonaCard card, ChatKind kind,
        string? target, string? channel, DateTime recvUtc, DateTime inferenceReadyUtc,
        string conversationKey, bool threadActive, int chainDepth = 1)
    {
        var t = card.Typing;
        var rng = Random.Shared;

        float wpm = Math.Max(5f, t.Wpm * _settings.GetFloat(0, "voice.wpm_mult", 1.0f));
        double thinkMs = rng.Next(Math.Min(t.ThinkMinS, t.ThinkMaxS), Math.Max(t.ThinkMinS, t.ThinkMaxS) + 1) * 1000.0;
        double typeMs = line.Length / (wpm * 5.0 / 60.0) * 1000.0;
        // Clamp bounds are settings (voice.hold_min_ms / hold_max_ms) — a fast persona
        // firing "lol" can land at ~2 s while Denise composing a sentence still takes 8+.
        double holdMin = Math.Max(500, _settings.GetInt(0, "voice.hold_min_ms", 2000));
        double holdMax = Math.Max(holdMin + 1000, _settings.GetInt(0, "voice.hold_max_ms", 45000));
        var holdUntil = recvUtc.AddMilliseconds(Math.Clamp(thinkMs + typeMs, holdMin, holdMax));

        // Alt-tab tail (§11): whisper mid-thread only at 1/3 rate — don't fully ghost.
        double altChance = t.AltTabChance;
        if (kind == ChatKind.Whisper && threadActive) altChance /= 3.0;
        if (rng.NextDouble() < altChance)
        {
            holdUntil = holdUntil.AddSeconds(rng.Next(60, 121));
            _logger.LogDebug("[CHAT-COORD] alt-tab tail for bot={Guid} (+{S}s)", botGuid, (holdUntil - recvUtc).TotalSeconds);
        }

        var sendUtc = inferenceReadyUtc > holdUntil ? inferenceReadyUtc : holdUntil;   // never hold past ready-if-later
        int wire = ChatWire.WireTypeFor(kind);

        // Splitting (§11): threshold scaled inversely by split_aggressiveness; max 2 messages.
        float aggr = Math.Max(0.1f, _settings.GetFloat(0, "voice.split_aggressiveness", 1.0f));
        int threshold = (int)(t.SplitThresholdChars / aggr);

        var sends = new List<ScheduledSend>();
        if (line.Length > threshold && TrySplit(line, out var first, out var second))
        {
            sends.Add(new ScheduledSend(botGuid, first, wire, target, channel, sendUtc, conversationKey, holdUntil, inferenceReadyUtc, chainDepth));
            sends.Add(new ScheduledSend(botGuid, second, wire, target, channel,
                sendUtc.AddSeconds(rng.Next(2, 7)), conversationKey, holdUntil, inferenceReadyUtc, chainDepth));
        }
        else
        {
            sends.Add(new ScheduledSend(botGuid, line, wire, target, channel, sendUtc, conversationKey, holdUntil, inferenceReadyUtc, chainDepth));
        }

        lock (_gate)
        {
            _cancelled.Remove((botGuid, conversationKey));   // a fresh schedule supersedes an old cancel
            foreach (var s in sends) _queue.Enqueue(s, s.SendUtc);
        }
        return sends;
    }

    /// <summary>Pop everything due. Called by the coordinator's 1 s housekeeping loop.</summary>
    public List<ScheduledSend> DrainDue(DateTime nowUtc)
    {
        var due = new List<ScheduledSend>();
        lock (_gate)
        {
            while (_queue.TryPeek(out var head, out var at) && at <= nowUtc)
            {
                _queue.Dequeue();
                if (_cancelled.Contains((head.BotGuid, head.ConversationKey))) continue;
                due.Add(head);
            }
        }
        return due;
    }

    /// <summary>Cancel every pending send for one conversation (§11 cancellation).</summary>
    public void Cancel(int botGuid, string conversationKey)
    {
        lock (_gate) _cancelled.Add((botGuid, conversationKey));
    }

    /// <summary>Drop the whole timeline — chat_enabled flipped off.</summary>
    public void CancelAll()
    {
        lock (_gate) { _queue.Clear(); _cancelled.Clear(); }
    }

    public int PendingCount { get { lock (_gate) return _queue.Count; } }

    /// <summary>Nearest sentence boundary split; falls back to the middle space. Max 2 parts.</summary>
    private static bool TrySplit(string line, out string first, out string second)
    {
        first = line; second = "";
        int mid = line.Length / 2;
        int best = -1, bestDist = int.MaxValue;
        for (int i = 10; i < line.Length - 10; i++)
        {
            bool boundary = (line[i] == '.' || line[i] == '?' || line[i] == '!') && i + 1 < line.Length && line[i + 1] == ' ';
            if (!boundary) continue;
            int dist = Math.Abs(i - mid);
            if (dist < bestDist) { bestDist = dist; best = i + 1; }
        }
        if (best < 0)
        {
            best = line.LastIndexOf(' ', Math.Min(mid + 20, line.Length - 1));
            if (best < 10) return false;
        }
        first = line[..best].Trim();
        second = line[best..].Trim();
        return first.Length > 0 && second.Length > 0;
    }
}