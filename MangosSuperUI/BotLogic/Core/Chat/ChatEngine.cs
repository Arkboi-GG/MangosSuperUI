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
/// Reactive generation options are locked (§10.3): temperature 0.85, top_p 0.9,
/// num_predict 60. Reactive maxWait 15 s — a starve is [CHAT-CAP]-alert-worthy (§12.1).
/// </summary>
public class ChatEngine : IChatEngine
{
    private static readonly GenOptions ReactiveOpts = new(0.85f, 0.9f, 60);
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

        return await _broker.GenerateAsync(lease, system, user, ReactiveOpts, ct);
    }

    public Task<AmbientScript?> ComposeAmbientAsync(AmbientJob job, CancellationToken ct)
    {
        _logger.LogDebug("[CHAT-ENGINE] ambient compose requested — lands in C7");
        return Task.FromResult<AmbientScript?>(null);
    }
}
