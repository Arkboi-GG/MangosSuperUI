using MangosSuperUI.BotLogic.Chat.Core;
using MangosSuperUI.BotLogic.Tracking;

namespace MangosSuperUI.BotLogic.Chat.Coordinator;

/// <summary>One message on the send timeline, drained by the coordinator's housekeeping loop.</summary>
public sealed record ScheduledSend(int BotGuid, string Text, int ChatTypeWire, string? Target,
                                   string? Channel, DateTime SendUtc, string ConversationKey,
                                   DateTime HoldUntil, DateTime ReadyAt, int ChainDepth,
                                   DateTime StimulusUtc);

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

    // SUPERSEDE LEDGER (2026-07-20). Newest stimulus time seen per (bot, conversation).
    // A held reply whose StimulusUtc is older than this was answering a line the conversation
    // has already moved past — see DrainDue.
    private readonly Dictionary<(int BotGuid, string Key), DateTime> _newestStimulus = new();

    // A held line older than this at send time is stale. Deliberately a constant and not a
    // §14.4 knob: this is a correctness rule (never say something the conversation moved past),
    // not a feel dial. The feel dials are hold_min_ms / hold_max_ms.
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(20);

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

        // Alt-tab tail (§11): the "player wandered off mid-message" beat.
        //
        // FIXED 2026-07-20. This used to fire on ANY non-whisper reply, including a bot in a
        // live back-and-forth — observed live as a +144 s tail that shipped a reply 2.5 minutes
        // after the line that provoked it, three messages into the past. That is not a player
        // alt-tabbing, it is a bot answering a question nobody remembers asking.
        //
        // A real person ghosts when they are NOT mid-exchange. So: no tail at all while a thread
        // is active, on any kind (the old rule only softened it for whispers, and only to 1/3).
        // Outside a live thread the tail is exactly as before — that is where it reads as human.
        double altChance = t.AltTabChance;
        if (threadActive) { CircuitTrace.Hit(botGuid, "chat: alt-tab tail suppressed, thread active"); altChance = 0.0; }
        if (rng.NextDouble() < altChance)
        {
            CircuitTrace.Hit(botGuid, "chat: alt-tab tail added to hold");
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
            CircuitTrace.Hit(botGuid, "chat: line split into two sends", line.Length);
            sends.Add(new ScheduledSend(botGuid, first, wire, target, channel, sendUtc, conversationKey, holdUntil, inferenceReadyUtc, chainDepth, recvUtc));
            sends.Add(new ScheduledSend(botGuid, second, wire, target, channel,
                sendUtc.AddSeconds(rng.Next(2, 7)), conversationKey, holdUntil, inferenceReadyUtc, chainDepth, recvUtc));
        }
        else
        {
            CircuitTrace.Hit(botGuid, "chat: line scheduled unsplit", line.Length);
            sends.Add(new ScheduledSend(botGuid, line, wire, target, channel, sendUtc, conversationKey, holdUntil, inferenceReadyUtc, chainDepth, recvUtc));
        }

        lock (_gate)
        {
            _cancelled.Remove((botGuid, conversationKey));   // a fresh schedule supersedes an old cancel
            foreach (var s in sends) _queue.Enqueue(s, s.SendUtc);
        }
        return sends;
    }

    /// <summary>
    /// Record that a NEWER line arrived for this bot in this conversation (2026-07-20).
    /// Called by the coordinator the moment a stimulus is accepted — BEFORE generation, since
    /// the whole point is to invalidate replies that are still being composed or still held.
    /// </summary>
    public void NoteStimulus(int botGuid, string conversationKey, DateTime stimulusUtc)
    {
        lock (_gate)
        {
            var key = (botGuid, conversationKey);
            if (!_newestStimulus.TryGetValue(key, out var prev) || stimulusUtc > prev)
            {
                CircuitTrace.Hit(botGuid, "chat: newest stimulus stamped for conversation");
                _newestStimulus[key] = stimulusUtc;
            }
        }
    }

    /// <summary>
    /// Pop everything due. Called by the coordinator's 1 s housekeeping loop.
    ///
    /// SUPERSEDE RULE (2026-07-20). A reply is composed against the conversation as it stood at
    /// StimulusUtc, then held for think+type time. If the humans and bots kept talking during
    /// that hold, the line is answering a message that has scrolled away — observed live as a
    /// bot greeting arriving one second AFTER the player had already asked the next question,
    /// which reads as the bot being confused rather than slow.
    ///
    /// So at send time: if a STRICTLY NEWER stimulus exists for the same (bot, conversation) and
    /// this line has been sitting more than StaleAfter, drop it. Two guards, both needed —
    /// "newer stimulus exists" alone would kill a perfectly good fast reply in a busy party,
    /// and an age check alone would kill a slow reply nobody had spoken over.
    ///
    /// Dropping beats sending: the bot simply didn't get to that one, which is exactly what a
    /// real person does when a conversation outruns their typing. The newer stimulus already
    /// has its own reply in flight.
    /// </summary>
    public List<ScheduledSend> DrainDue(DateTime nowUtc)
    {
        var due = new List<ScheduledSend>();
        var superseded = new List<ScheduledSend>();
        lock (_gate)
        {
            while (_queue.TryPeek(out var head, out var at) && at <= nowUtc)
            {
                _queue.Dequeue();
                if (_cancelled.Contains((head.BotGuid, head.ConversationKey))) { CircuitTrace.Hit(head.BotGuid, "chat: due send dropped, conversation cancelled"); continue; }

                if (_newestStimulus.TryGetValue((head.BotGuid, head.ConversationKey), out var newest)
                    && newest > head.StimulusUtc
                    && (nowUtc - head.StimulusUtc) > StaleAfter)
                {
                    CircuitTrace.Hit(head.BotGuid, "chat: due send superseded by newer stimulus");
                    superseded.Add(head);
                    continue;
                }

                due.Add(head);
            }
        }

        foreach (var s in superseded)
            _logger.LogInformation(
                "[CHAT-COORD] superseded bot={Bot} conv={Conv} age={Age:0.0}s — dropped \"{Text}\"",
                s.BotGuid, s.ConversationKey, (nowUtc - s.StimulusUtc).TotalSeconds, s.Text);

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
        lock (_gate) { _queue.Clear(); _cancelled.Clear(); _newestStimulus.Clear(); }
    }

    /// <summary>Drop supersede entries older than 5 minutes. Called from the 1 s housekeeping sweep.</summary>
    public void SweepStimulusLedger(DateTime nowUtc)
    {
        lock (_gate)
        {
            if (_newestStimulus.Count < 256) { CircuitTrace.Hit(0, "chat: stimulus ledger sweep skipped, below cap"); return; }
            var cutoff = nowUtc.AddMinutes(-5);
            foreach (var k in _newestStimulus.Where(kv => kv.Value < cutoff).Select(kv => kv.Key).ToList())
                _newestStimulus.Remove(k);
        }
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
            if (!boundary) continue;   // cb:fold pure text split helper, split decision probed at Schedule
            int dist = Math.Abs(i - mid);
            if (dist < bestDist) { bestDist = dist; best = i + 1; }   // cb:fold pure text split helper, split decision probed at Schedule
        }
        if (best < 0)
        {   // cb:fold pure text split helper, split decision probed at Schedule
            best = line.LastIndexOf(' ', Math.Min(mid + 20, line.Length - 1));
            if (best < 10) return false;   // cb:fold pure text split helper, split decision probed at Schedule
        }
        first = line[..best].Trim();
        second = line[best..].Trim();
        return first.Length > 0 && second.Length > 0;
    }
}