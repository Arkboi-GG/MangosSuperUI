using System.Threading.Channels;
using MangosSuperUI.BotLogic.Chat.Core;
using MangosSuperUI.BotLogic.Chat.Engine;
using MangosSuperUI.BotLogic.Chat.Memory;
using MangosSuperUI.BotLogic.Core;
using MangosSuperUI.Services;

namespace MangosSuperUI.BotLogic.Chat.Coordinator;

// ======================== Stimulus records (CHAT_ARCHITECTURE §9.1) ========================

/// <summary>One raw CHAT_RECV event as heard by ONE bot; N hearers → N of these.</summary>
public record ChatStimulusRaw(int HearerGuid, string Sender, uint SenderGuid, string Message,
                              string Kind, string ChannelName, DateTime Utc);

/// <summary>The folded, deduped stimulus — one per message, all hearers (§9.1).</summary>
public record ChatStimulus(string Sender, uint SenderGuid, bool SenderIsBot, string Message,
                           ChatKind Kind, string ChannelName, int ZoneId,
                           List<int> HearerGuids, int ChainDepth, DateTime Utc);

// ======================== Coordinator contract ========================

public interface IChatCoordinator
{
    /// <summary>Fire-and-forget hand-off from the bridge's CHAT_RECV path (§5.5). Never blocks.</summary>
    void EnqueueStimulus(ChatStimulusRaw raw);
}

// ======================== ChatCoordinator (C4 — arbitration complete) ========================

/// <summary>
/// The AiBot social layer's central arbiter (CHAT_ARCHITECTURE §9). Separate from
/// BotBrainService (D9); all output is fire-and-forget SAY_TEXT.
///
/// C4 SCOPE: the loud channels are OPEN. Say/channel/party stimuli fold per §9.1
/// (2 s collection window, N hearers → ONE decision), every hearer gets §9.2 urge
/// scoring, and the §9.4 anti-storm guards are live: chain-depth cap (guard 2, via
/// ChainGuard), token buckets (guard 3, via BudgetBuckets), plus the C++ self-echo
/// filter (guard 1) and the chat_enabled kill switch (guard 4).
/// Whispers stay single-hearer: whisper_always_replies=true skips scoring (§9.2);
/// false routes the whisper through the same urge pass as everything else.
/// </summary>
public class ChatCoordinator : BackgroundService, IChatCoordinator
{
    private const int ReactiveWorkers = 4;
    private static readonly TimeSpan FoldWindow = TimeSpan.FromSeconds(2);   // §9.1, locked

    private sealed record ReactiveJob(int HearerGuid, string Sender, uint SenderGuid,
        string Message, ChatKind Kind, string ChannelName, DateTime RecvUtc,
        bool ThreadActiveAtRecv, int StimulusDepth);

    private sealed class PendingFold
    {
        public required string Sender;
        public required uint SenderGuid;
        public required string Message;
        public required ChatKind Kind;
        public required string ChannelName;
        public required DateTime FirstUtc;
        public DateTime Deadline;
        public readonly HashSet<int> Hearers = new();
    }

    private readonly ILogger<ChatCoordinator> _logger;
    private readonly BotBridgeService _bridge;
    private readonly ChatSettingsService _settings;
    private readonly PersonaService _personas;
    private readonly IChatEngine _engine;
    private readonly StylePostPass _postPass;
    private readonly ConversationTracker _tracker;
    private readonly TypingScheduler _scheduler;
    private readonly ChatMemoryStore _memory;
    private readonly UrgeScorer _urge;
    private readonly ChainGuard _chain;
    private readonly BudgetBuckets _buckets;

    private readonly Channel<ChatStimulusRaw> _stimuli =
        Channel.CreateUnbounded<ChatStimulusRaw>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly Channel<ReactiveJob> _reactiveJobs =
        Channel.CreateUnbounded<ReactiveJob>(new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });

    // §9.1 fold buffer — keyed by (sender identity, message, kind, channel); intake-loop-only
    private readonly Dictionary<(string SenderKey, string Message, ChatKind Kind, string Channel), PendingFold> _folds = new();
    private readonly object _foldGate = new();

    // Cooldown input for urge (§9.2) — last actual send per bot
    private readonly Dictionary<int, DateTime> _lastSpoke = new();

    private bool _lastChatEnabled = true;

    public ChatCoordinator(ILogger<ChatCoordinator> logger, BotBridgeService bridge,
        ChatSettingsService settings, PersonaService personas, IChatEngine engine,
        StylePostPass postPass, ConversationTracker tracker, TypingScheduler scheduler,
        ChatMemoryStore memory, UrgeScorer urge, ChainGuard chain, BudgetBuckets buckets)
    {
        _logger = logger;
        _bridge = bridge;
        _settings = settings;
        _personas = personas;
        _engine = engine;
        _postPass = postPass;
        _tracker = tracker;
        _scheduler = scheduler;
        _memory = memory;
        _urge = urge;
        _chain = chain;
        _buckets = buckets;
    }

    public void EnqueueStimulus(ChatStimulusRaw raw)
    {
        if (!_stimuli.Writer.TryWrite(raw))
            _logger.LogWarning("[CHAT-COORD] stimulus dropped — channel closed (shutdown?)");
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _bridge.SetChatCoordinator(this);
        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[CHAT-COORD] ChatCoordinator started (C4 — say/channel/party arbitration live)");

        var loops = new List<Task> { IntakeLoopAsync(stoppingToken), HousekeepingLoopAsync(stoppingToken) };
        for (int i = 0; i < ReactiveWorkers; i++)
            loops.Add(ReactiveWorkerAsync(i, stoppingToken));

        try { await Task.WhenAll(loops); }
        catch (OperationCanceledException) { /* shutdown */ }

        _logger.LogInformation("[CHAT-COORD] ChatCoordinator stopped");
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _stimuli.Writer.TryComplete();
        _reactiveJobs.Writer.TryComplete();
        return base.StopAsync(cancellationToken);
    }

    // ==================== Intake: whisper fast-path + loud-kind fold (§9.1) ====================

    private async Task IntakeLoopAsync(CancellationToken ct)
    {
        await foreach (var raw in _stimuli.Reader.ReadAllAsync(ct))
        {
            var kind = ChatWire.ParseKind(raw.Kind);
            _logger.LogInformation(
                "[CHAT-COORD] stimulus hearer={Hearer} sender={Sender} sender_guid={SenderGuid} kind={Kind} channel={Channel} msg=\"{Message}\"",
                raw.HearerGuid, raw.Sender, raw.SenderGuid, kind, raw.ChannelName, raw.Message);

            if (!_settings.GetBool(0, "global.chat_enabled", true))
            {
                _logger.LogDebug("[CHAT-COORD] dropped hearer={Hearer} reason=disabled", raw.HearerGuid);
                continue;
            }

            if (kind == ChatKind.Whisper)
            {
                await HandleWhisperAsync(raw, ct);
                continue;
            }

            // Loud kinds: accumulate into the fold buffer; emission happens in housekeeping
            // once the 2 s window closes (§9.1 — hold the first raw event, gather hearers).
            var key = (SenderKey(raw), raw.Message, kind, raw.ChannelName ?? "");
            lock (_foldGate)
            {
                if (_folds.TryGetValue(key, out var fold))
                {
                    fold.Hearers.Add(raw.HearerGuid);
                }
                else
                {
                    var f = new PendingFold
                    {
                        Sender = raw.Sender,
                        SenderGuid = raw.SenderGuid,
                        Message = raw.Message,
                        Kind = kind,
                        ChannelName = raw.ChannelName ?? "",
                        FirstUtc = raw.Utc,
                        Deadline = DateTime.UtcNow + FoldWindow
                    };
                    f.Hearers.Add(raw.HearerGuid);
                    _folds[key] = f;
                }
            }
        }
    }

    private static string SenderKey(ChatStimulusRaw raw) =>
        raw.SenderGuid != 0 ? $"g{raw.SenderGuid}" : $"n{raw.Sender.Trim().ToLowerInvariant()}";

    // ==================== Whisper path (single hearer, §9.2 special case) ====================

    private async Task HandleWhisperAsync(ChatStimulusRaw raw, CancellationToken ct)
    {
        bool senderIsBot = raw.SenderGuid != 0 && _bridge.BotStates.ContainsKey((int)raw.SenderGuid);
        int depth = senderIsBot ? _chain.GetStimulusDepth(raw.Sender) : 0;

        // Chain cap applies to whispers too — otherwise two bots whispering each other
        // would ping-pong forever (the C++ self-echo filter only stops self-loops).
        int maxDepth = _settings.GetInt(0, "noise.max_bot_chain_depth", 2);
        if (depth >= maxDepth)
        {
            _logger.LogInformation("[CHAT-COORD] chain drop — whisper from bot {Sender} at depth {Depth} ≥ {Max}",
                raw.Sender, depth, maxDepth);
            return;
        }

        bool threadActive = _tracker.IsThreadActive(raw.HearerGuid, raw.Sender);
        _tracker.Append(raw.HearerGuid, raw.Sender, ChatKind.Whisper, raw.Sender, raw.Message);

        int zoneId = _bridge.BotStates.TryGetValue(raw.HearerGuid, out var hs) ? hs.ZoneId : 0;
        _memory.LogParticipatedIn(raw.HearerGuid, raw.Sender, raw.SenderGuid,
            ChatKind.Whisper, "", zoneId, raw.Message, addressed: true);

        if (!_settings.GetBool(0, "responsiveness.whisper_always_replies", true))
        {
            // §9.2: whisper_always off → whispers go through urge like everything else.
            var inputs = new UrgeInputs(raw.HearerGuid, Addressed: true, threadActive,
                await _memory.GetStrengthAsync(raw.HearerGuid, raw.Sender),
                Proximity: 0.5f, SecondsSinceSpoke(raw.HearerGuid),
                hs?.InCombat ?? false, hs?.IsDead ?? false, depth);
            var (urge, speaks, breakdown) = _urge.Score(inputs, zoneId);
            _logger.LogInformation("[CHAT-COORD] urge bot={Bot} kind=whisper {Verdict}: {Breakdown}",
                raw.HearerGuid, speaks ? "spoke" : "held", breakdown);
            if (!speaks) return;
            if (Random.Shared.NextDouble() < _settings.GetFloat(zoneId, "noise.ignore_chance", 0.06f))
            {
                _logger.LogInformation("[CHAT-COORD] ignored bot={Bot} reason=ignore-roll (urge {Urge:0.00})", raw.HearerGuid, urge);
                return;
            }
        }

        _scheduler.NoteStimulus(raw.HearerGuid, $"{raw.Sender}|{ChatKind.Whisper}", raw.Utc);
        await _reactiveJobs.Writer.WriteAsync(new ReactiveJob(raw.HearerGuid, raw.Sender,
            raw.SenderGuid, raw.Message, ChatKind.Whisper, "", raw.Utc, threadActive, depth), ct);
    }

    // ==================== Fold emission + arbitration (§9.1 + §9.2 + §9.4) ====================

    private async Task EmitDueFoldsAsync(CancellationToken ct)
    {
        List<PendingFold> due;
        lock (_foldGate)
        {
            var now = DateTime.UtcNow;
            due = _folds.Where(kv => kv.Value.Deadline <= now).Select(kv => kv.Value).ToList();
            foreach (var kv in _folds.Where(kv => kv.Value.Deadline <= now).ToList())
                _folds.Remove(kv.Key);
        }

        foreach (var fold in due)
            await ArbitrateAsync(fold, ct);
    }

    private async Task ArbitrateAsync(PendingFold fold, CancellationToken ct)
    {
        // D3: the roster is the ONLY place identity is resolved.
        bool senderIsBot = fold.SenderGuid != 0 && _bridge.BotStates.ContainsKey((int)fold.SenderGuid);
        int stimulusDepth = senderIsBot ? _chain.GetStimulusDepth(fold.Sender) : 0;
        int maxDepth = _settings.GetInt(0, "noise.max_bot_chain_depth", 2);
        bool chainDropped = stimulusDepth >= maxDepth;

        var hearers = fold.Hearers.Where(h => h != (int)fold.SenderGuid).ToList();
        _logger.LogInformation(
            "[CHAT-COORD] fold sender={Sender} bot={IsBot} kind={Kind} hearers={Count} depth={Depth}{Dropped} msg=\"{Message}\"",
            fold.Sender, senderIsBot, fold.Kind, hearers.Count, stimulusDepth,
            chainDropped ? " CHAIN-DROP" : "", fold.Message);

        bool isQuestion = fold.Message.Contains('?');
        var speakers = new List<(int Guid, bool Addressed, bool ThreadActive)>();

        foreach (var hearerGuid in hearers)
        {
            if (!_bridge.BotStates.TryGetValue(hearerGuid, out var state)) continue;

            bool addressed = IsAddressed(fold.Message, state.Name);
            bool threadActive = _tracker.IsThreadActive(hearerGuid, fold.Sender);
            if (threadActive) addressed = true;   // §9.1: Tier-0 active counterpart = addressed

            // Design amendment (Nico, 2026-07-07): PARTY CHAT IS INHERENTLY ADDRESSED.
            // You only hear /p from your own group, and nobody types in /p except to
            // talk TO the group — a partymate's line deserves the full W_addr weight.
            // (Cooldown, ignore roll, and budgets still thin replies in big parties.)
            if (fold.Kind == ChatKind.Party) addressed = true;

            // ── Tier-1 policy (§7.2), independent of the speak decision ──
            if (addressed)
                _memory.LogParticipatedIn(hearerGuid, fold.Sender, fold.SenderGuid,
                    fold.Kind, fold.ChannelName, state.ZoneId, fold.Message, addressed: true);
            else if (threadActive)
                _memory.LogParticipatedIn(hearerGuid, fold.Sender, fold.SenderGuid,
                    fold.Kind, fold.ChannelName, state.ZoneId, fold.Message, addressed: false);
            else
                _memory.LogOverheard(hearerGuid, fold.Sender, fold.SenderGuid,
                    fold.Kind, fold.ChannelName, state.ZoneId, fold.Message,
                    mentionsBotName: false, isQuestion);

            if (chainDropped) continue;   // memory recorded; nobody replies past the cap

            // ── §9.2 urge ──
            var inputs = new UrgeInputs(hearerGuid, addressed, threadActive,
                await _memory.GetStrengthAsync(hearerGuid, fold.Sender),
                Proximity(fold, state), SecondsSinceSpoke(hearerGuid),
                state.InCombat, state.IsDead, stimulusDepth);
            var (urge, speaks, breakdown) = _urge.Score(inputs, state.ZoneId);

            if (!speaks)
            {
                _logger.LogInformation("[CHAT-COORD] urge bot={Bot}({Name}) held: {Breakdown}",
                    hearerGuid, state.Name, breakdown);
                continue;
            }

            // Post-threshold ignore roll (D18: sometimes the guy just doesn't feel like it)
            if (Random.Shared.NextDouble() < _settings.GetFloat(state.ZoneId, "noise.ignore_chance", 0.06f))
            {
                _logger.LogInformation("[CHAT-COORD] urge bot={Bot}({Name}) ignored (roll): {Breakdown}",
                    hearerGuid, state.Name, breakdown);
                continue;
            }

            // Crosstalk gate: continuations always pass; NEW conversation starts are
            // capped by live threads among the hearer set (§9.2).
            if (!threadActive)
            {
                int maxConvos = _settings.GetInt(state.ZoneId, "noise.max_parallel_convos_per_spot", 2);
                int active = _tracker.CountActiveThreads(hearers);
                if (active + speakers.Count >= maxConvos)
                {
                    _logger.LogInformation("[CHAT-COORD] urge bot={Bot}({Name}) held reason=crosstalk ({Active} live ≥ {Max})",
                        hearerGuid, state.Name, active, maxConvos);
                    continue;
                }
            }

            // Token buckets (§9.4.3) — consumed at the decision, not the send
            if (!_buckets.TryConsume(hearerGuid, state.ZoneId, fold.Kind))
            {
                _logger.LogDebug("[CHAT-COORD] bucket drop bot={Bot}({Name}) kind={Kind}", hearerGuid, state.Name, fold.Kind);
                continue;
            }

            _logger.LogInformation("[CHAT-COORD] urge bot={Bot}({Name}) SPOKE: {Breakdown}",
                hearerGuid, state.Name, breakdown);
            speakers.Add((hearerGuid, addressed, threadActive));
        }

        foreach (var (guid, _, threadActive) in speakers)
        {
            // Tier 0: the incoming line enters the speaker's window so the reply threads
            _tracker.Append(guid, fold.Sender, fold.Kind, fold.Sender, fold.Message);

            // SUPERSEDE (2026-07-20): stamp the newest stimulus for this (bot, conversation)
            // BEFORE the job is queued, so a reply still being generated — or already held on
            // the timeline — can be recognised as stale at send time. Key must match the
            // convKey built in ProcessReactiveAsync.
            _scheduler.NoteStimulus(guid, $"{fold.Sender}|{fold.Kind}", fold.FirstUtc);

            await _reactiveJobs.Writer.WriteAsync(new ReactiveJob(guid, fold.Sender,
                fold.SenderGuid, fold.Message, fold.Kind, fold.ChannelName, fold.FirstUtc,
                threadActive, stimulusDepth), ct);
        }
    }

    /// <summary>
    /// §9.1 addressing: whole-word name match (case-insensitive), plus the vanilla
    /// name-prefix habit — any 4+-char token that prefixes the bot's name ("thud u
    /// there" hits Thudgar).
    /// </summary>
    private static bool IsAddressed(string message, string botName)
    {
        if (string.IsNullOrEmpty(botName)) return false;
        foreach (var token in message.Split(_tokenSeps, StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Equals(botName, StringComparison.OrdinalIgnoreCase)) return true;
            if (token.Length >= 4 && botName.StartsWith(token, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static readonly char[] _tokenSeps =
        { ' ', ',', '.', '!', '?', ':', ';', '\'', '"', '(', ')', '[', ']', '-', '/' };

    /// <summary>
    /// §9.2 proximity: say = 1 at 0 yd → 0 at 25 yd (real distance when the sender is a
    /// bot with a known position; 0.5 fallback for human senders — C# doesn't track
    /// player positions). Channel/party: flat 0.5.
    /// </summary>
    private float Proximity(PendingFold fold, BotState hearer)
    {
        if (fold.Kind != ChatKind.Say) return 0.5f;
        if (fold.SenderGuid != 0 && _bridge.BotStates.TryGetValue((int)fold.SenderGuid, out var sender)
            && sender.MapId == hearer.MapId)
        {
            float dx = sender.X - hearer.X, dy = sender.Y - hearer.Y;
            float dist = MathF.Sqrt(dx * dx + dy * dy);
            return Math.Clamp(1f - dist / 25f, 0f, 1f);
        }
        return 0.5f;
    }

    private double SecondsSinceSpoke(int botGuid)
    {
        lock (_lastSpoke)
            return _lastSpoke.TryGetValue(botGuid, out var t) ? (DateTime.UtcNow - t).TotalSeconds : 3600;
    }

    // ==================== Reactive workers (§9.3 body) ====================

    private async Task ReactiveWorkerAsync(int workerId, CancellationToken ct)
    {
        await foreach (var job in _reactiveJobs.Reader.ReadAllAsync(ct))
        {
            try { await ProcessReactiveAsync(job, ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CHAT-COORD] reactive worker {Id} failed for hearer={Hearer}",
                    workerId, job.HearerGuid);
            }
        }
    }

    private async Task ProcessReactiveAsync(ReactiveJob job, CancellationToken ct)
    {
        if (!_bridge.BotStates.TryGetValue(job.HearerGuid, out var state))
        {
            _logger.LogWarning("[CHAT-COORD] dropped hearer={Hearer} reason=no-bot-state", job.HearerGuid);
            return;
        }

        var persona = await _personas.GetOrCreateAsync(job.HearerGuid, state.Name);

        var chatJob = new ChatJob(
            BotGuid: job.HearerGuid,
            BotName: state.Name,
            Level: state.Level,
            Race: ((WowRace)state.Race).ToString(),
            Class: ((WowClass)state.ClassId).ToString(),
            Persona: persona,
            SnapshotLine: BuildSnapshotLine(state),
            RelationshipSummary: await _memory.GetRelationshipSummaryAsync(job.HearerGuid, job.Sender),
            EraAnchor: "vanilla, 2005",       // C10: active era pack's ## Anchor replaces this
            EraDigest: "",                    // C10: chat_era_pack.digest
            LiveWindow: _tracker.GetWindow(job.HearerGuid, job.Sender, job.Kind),
            Sender: job.Sender,
            Message: job.Message,
            Kind: job.Kind,
            ChannelName: job.ChannelName,
            RecvUtc: job.RecvUtc,
            Activity: ActivityOf(state));

        var rawReply = await _engine.ComposeReplyAsync(chatJob, ct);
        if (rawReply == null) return;

        var (line, discardReason) = _postPass.Apply(persona.Card, state.Name, rawReply);
        if (line == null)
        {
            _logger.LogInformation("[CHAT-ENGINE] discard bot={Bot} reason={Reason}", state.Name, discardReason);
            return;
        }

        _tracker.Append(job.HearerGuid, job.Sender, job.Kind, state.Name, line);
        _memory.LogOut(job.HearerGuid, job.Sender, job.SenderGuid,
            job.Kind, job.ChannelName, state.ZoneId, line);

        // §9.3 routing: whisper→whisper back (target), channel→same channel, say/party plain.
        string? target = job.Kind == ChatKind.Whisper ? job.Sender : null;
        string? channel = job.Kind == ChatKind.Channel ? job.ChannelName : null;

        var convKey = $"{job.Sender}|{job.Kind}";
        var sends = _scheduler.Schedule(job.HearerGuid, line, persona.Card, job.Kind,
            target, channel, job.RecvUtc, DateTime.UtcNow, convKey,
            threadActive: job.ThreadActiveAtRecv,
            chainDepth: job.StimulusDepth + 1);   // our emission is one deeper than what provoked it

        foreach (var s in sends)
            _logger.LogInformation(
                "[CHAT-COORD] schedule bot={Bot} kind={Kind} → {Target} sendUtc={SendUtc:HH:mm:ss.f} (holdUntil={Hold:HH:mm:ss.f} readyAt={Ready:HH:mm:ss.f}) \"{Line}\"",
                state.Name, job.Kind, target ?? channel ?? "nearby", s.SendUtc, s.HoldUntil, s.ReadyAt, s.Text);
    }

    /// <summary>
    /// Live activity for the prompt's situational framing (2026-07-20). Same state
    /// BuildSnapshotLine reads, given to the assembler as a value instead of prose so the
    /// prompt can branch on it. An earlier prompt hardcoded "you're stood around doing
    /// nothing", which is wrong the moment the bot is fighting, dead or running somewhere —
    /// and what a bot is doing changes how it would type.
    /// </summary>
    private static ChatActivity ActivityOf(BotState state)
    {
        if (state.IsDead) return ChatActivity.Dead;
        if (state.InCombat) return ChatActivity.Fighting;
        return (state.TaskActivity ?? "").ToLowerInvariant() switch
        {
            "traveling" or "travelling" => ChatActivity.Travelling,
            "searching" => ChatActivity.Grinding,
            "engaged" => ChatActivity.Fighting,
            "recovering" => ChatActivity.Recovering,
            "blocked" => ChatActivity.Stuck,
            _ => ChatActivity.Idle
        };
    }

    private static string BuildSnapshotLine(BotState state)
    {
        string doing = state.IsDead ? "dead and running back to my body"
            : state.InCombat ? "fighting something"
            : (state.TaskActivity ?? "").ToLowerInvariant() switch
            {
                "traveling" or "travelling" => "traveling somewhere",
                "searching" => "out grinding mobs",
                "engaged" => "fighting something",
                "recovering" => "resting up after a fight",
                "blocked" => "kind of stuck honestly",
                _ => "just hanging out"
            };
        var zone = ZoneNames.Get(state.ZoneId);
        return string.IsNullOrEmpty(zone)
            ? $"lvl {state.Level}, {doing}"
            : $"lvl {state.Level}, {doing} in {zone}";
    }

    // ==================== Housekeeping (1 s: folds, sends, refills, sweeps) ====================

    private async Task HousekeepingLoopAsync(CancellationToken ct)
    {
        var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        int sweepCounter = 0;

        while (await timer.WaitForNextTickAsync(ct))
        {
            bool chatEnabled = _settings.GetBool(0, "global.chat_enabled", true);
            if (_lastChatEnabled && !chatEnabled)
            {
                int pending = _scheduler.PendingCount;
                _scheduler.CancelAll();
                lock (_foldGate) _folds.Clear();
                _logger.LogWarning("[CHAT-COORD] chat_enabled flipped OFF — cancelled {Count} pending sends", pending);
            }
            _lastChatEnabled = chatEnabled;

            if (chatEnabled)
                await EmitDueFoldsAsync(ct);

            _buckets.Refill();

            foreach (var send in _scheduler.DrainDue(DateTime.UtcNow))
            {
                if (!chatEnabled) continue;
                await _bridge.SendSayTextAsync(send.BotGuid, send.Text, send.ChatTypeWire, send.Target, send.Channel);

                // Guard-2 bookkeeping + the cooldown clock, at ACTUAL send time
                string botName = _bridge.BotStates.TryGetValue(send.BotGuid, out var bs) ? bs.Name : send.BotGuid.ToString();
                _chain.RecordEmission(botName, send.ChainDepth);
                lock (_lastSpoke) _lastSpoke[send.BotGuid] = DateTime.UtcNow;

                _logger.LogInformation("[CHAT-COORD] sent bot={Name} wire={Wire} target={Target} depth={Depth} \"{Text}\"",
                    botName, send.ChatTypeWire, send.Target ?? send.Channel ?? "-", send.ChainDepth, send.Text);
            }

            if (sweepCounter % 5 == 4)
                await _memory.FlushAsync();

            if (++sweepCounter >= 30)
            {
                sweepCounter = 0;
                _tracker.Sweep();
                _chain.Sweep();
                _scheduler.SweepStimulusLedger(DateTime.UtcNow);
            }
        }
    }
}