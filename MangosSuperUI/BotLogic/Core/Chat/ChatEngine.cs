using MangosSuperUI.BotLogic.Chat.Capacity;
using MangosSuperUI.BotLogic.Chat.Core;

namespace MangosSuperUI.BotLogic.Chat.Engine;

// ── Ambient contract placeholders (C7 fills these; declared so §10.1's interface is
//    complete from day one and C7 doesn't touch the interface) ──
public sealed record AmbientJob(int BotAGuid, int BotBGuid, int ZoneId, string Topic, int LineCount);
public sealed record AmbientScript(IReadOnlyList<(string Speaker, string Line)> Lines);

/// <summary>CHAT_ARCHITECTURE §10.1 — the engine contract.</summary>
public interface IChatEngine
{
    Task<string?> ComposeReplyAsync(ChatJob job, CancellationToken ct);                 // Reactive
    Task<AmbientScript?> ComposeAmbientAsync(AmbientJob job, CancellationToken ct);     // Ambient (C7)
}

/// <summary>
/// Stateless composer (§10.1): acquire broker lease for the class → assemble prompt
/// within the profile's ctx_budget_tokens → generate → return RAW text. StylePostPass
/// and scheduling belong to the coordinator, keeping this engine stateless.
/// Reactive maxWait 15 s — a starve is [CHAT-CAP]-alert-worthy (§12.1).
///
/// Generation options AMENDED 2026-07-13 (§10.3 was: temp 0.85, top_p 0.9, num_predict 60,
/// nothing else sent):
///   • temperature 0.90 / top_p 0.92 — a touch wider; these are 25-word chat lines, not code.
///   • repeat_penalty 1.15, repeat_last_n 128 — stops a line eating its own tail.
///   • presence 0.5 / frequency 0.3 — pushes off the model's pet openers WITHIN a line.
///   • seed — explicitly randomized per call. Note this is belt-and-braces: an unset seed
///     is already random. It buys reproducibility when debugging a bad line, not diversity.
///   • stop ["\n"] — the model was free to keep going and write the OTHER guy's next turn.
///     One line is one line.
///
/// BE CLEAR ABOUT WHAT THIS DOES NOT FIX: every one of these penalties operates inside a
/// single generation. The backend has no memory between calls, so none of it prevents two
/// separate replies from landing on the same sentence. That is a persona-diversity problem
/// (voice library) and a ledger problem (StylePostPass step 10), not a sampler problem.
/// </summary>
public class ChatEngine : IChatEngine
{
    private static readonly string[] ReactiveStop = { "\n" };

    private static readonly GenOptions ReactiveOpts = new(
        Temperature: 0.90f,
        TopP: 0.92f,
        NumPredict: 60,
        RepeatPenalty: 1.15f,
        RepeatLastN: 128,
        PresencePenalty: 0.5f,
        FrequencyPenalty: 0.3f,
        Seed: null,
        Stop: ReactiveStop);

    private static readonly TimeSpan ReactiveMaxWait = TimeSpan.FromSeconds(15);

    private readonly IInferenceBroker _broker;
    private readonly PromptAssembler _assembler;
    private readonly ILogger<ChatEngine> _logger;

    public ChatEngine(IInferenceBroker broker, PromptAssembler assembler, ILogger<ChatEngine> logger)
    {
        _broker = broker;
        _assembler = assembler;
        _logger = logger;
    }

    public async Task<string?> ComposeReplyAsync(ChatJob job, CancellationToken ct)
    {
        using var lease = await _broker.TryAcquireAsync(TrafficClass.Reactive, ReactiveMaxWait, ct);
        if (lease == null)
        {
            _logger.LogWarning("[CHAT-CAP] reactive starved — no lease within {Wait}s for {Bot} (should never happen)",
                ReactiveMaxWait.TotalSeconds, job.BotName);
            return null;
        }

        var (system, user, tokensEst, report) = _assembler.Assemble(job, _broker.CtxBudgetTokens);
        _logger.LogInformation("[CHAT-ENGINE] prompt assembled — bot={Bot} reply-to={Sender} kind={Kind} [{Report}]",
            job.BotName, job.Sender, job.Kind, report);
        _logger.LogDebug("[CHAT-ENGINE] prompt dump for {Bot}:\n--- SYSTEM ---\n{System}\n--- USER ---\n{User}",
            job.BotName, system, user);

        var opts = ReactiveOpts with { Seed = Random.Shared.Next() };
        return await _broker.GenerateAsync(lease, system, user, opts, ct);
    }

    public Task<AmbientScript?> ComposeAmbientAsync(AmbientJob job, CancellationToken ct)
    {
        _logger.LogDebug("[CHAT-ENGINE] ambient compose requested — lands in C7");
        return Task.FromResult<AmbientScript?>(null);
    }
}