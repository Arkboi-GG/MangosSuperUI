using System.Text;
using Dapper;
using MangosSuperUI.Models;
using MangosSuperUI.BotLogic.Chat.Core;

namespace MangosSuperUI.BotLogic.Chat.Engine;

/// <summary>One reactive reply job — everything the assembler needs (built by the coordinator).</summary>
public sealed record ChatJob(
    int BotGuid, string BotName, int Level, string Race, string Class,
    BotPersona Persona, string SnapshotLine, string RelationshipSummary, string EraAnchor,
    string EraDigest, IReadOnlyList<(string Speaker, string Line)> LiveWindow,
    string Sender, string Message, ChatKind Kind, string ChannelName, DateTime RecvUtc);

/// <summary>
/// Builds the reactive prompt (CHAT_ARCHITECTURE §10.3, authoritative template) under the
/// §10.2 hard token budget. Token estimate = chars/4; cap = profile ctx_budget − 150 gen
/// reserve. Drop order under pressure: trim opinions (persona-internal), then 1) halve
/// live window keeping newest, 2) halve era digest, 3) drop in-game snapshot, 4) drop
/// few-shot lines. System frame, mood line, and incoming line are never dropped.
///
/// AMENDED 2026-07-13 (repetition fix):
///   • FEW-SHOT IS NOW SHUFFLED. It used to be ExampleLines.Take(3) — the same three
///     anchors, every call, for the life of the bot. Those anchors are what a small model
///     actually imitates, so a frozen triple is a frozen voice. 3 random of 5 per call.
///   • REGISTER LINE (§10.4 step 6b's prompt half): the persona's swear_level × the
///     voice.banter_intensity slider, rendered as an explicit instruction. The prompt is
///     the PRIMARY channel for register; SwearTables is the backstop for when the model
///     flinches and types "heck" anyway.
/// </summary>
public class PromptAssembler
{
    private readonly ConnectionFactory _db;
    private readonly ChatSettingsService _settings;
    private readonly ILogger<PromptAssembler> _logger;
    private string? _realmName;

    public PromptAssembler(ConnectionFactory db, ChatSettingsService settings, ILogger<PromptAssembler> logger)
    {
        _db = db;
        _settings = settings;
        _logger = logger;
    }

    public static int EstimateTokens(string s) => (s?.Length ?? 0) / 4;

    public (string System, string User, int TokensEst, string BlockReport) Assemble(ChatJob job, int ctxBudgetTokens)
    {
        int cap = Math.Max(400, ctxBudgetTokens - 150);   // gen reserve (§10.2)
        var card = job.Persona.Card;

        // ── Mutable block state (the drop order operates on these) ──
        var opinions = card.Opinions.ToList();
        var window = job.LiveWindow.ToList();
        var eraDigest = job.EraDigest;
        bool includeSnapshot = true;
        bool includeFewShot = true;

        for (int pass = 0; ; pass++)
        {
            var (system, user) = Render(job, card, opinions, window, eraDigest, includeSnapshot, includeFewShot);
            int est = EstimateTokens(system) + EstimateTokens(user);
            if (est <= cap || pass >= 6)
            {
                var report = $"win={window.Count}L era={EstimateTokens(eraDigest)}t snap={includeSnapshot} " +
                             $"fewshot={includeFewShot} opinions={opinions.Count} " +
                             $"swear={SwearTables.EffectiveLevel(card.Typing.SwearLevel, Banter())} " +
                             $"est={est}t cap={cap}t";
                if (est > cap)
                    _logger.LogWarning("[CHAT-ENGINE] prompt over budget after all drops for {Bot}: {Report}", job.BotName, report);
                return (system, user, est, report);
            }

            // Drop order (§10.2)
            if (opinions.Count > 0) { opinions.Clear(); continue; }                       // persona: trim opinions first
            if (window.Count > 1) { window = window.Skip(window.Count / 2).ToList(); continue; } // 1st: halve, keep newest
            if (eraDigest.Length > 0) { eraDigest = eraDigest[..(eraDigest.Length / 2)]; if (eraDigest.Length < 40) eraDigest = ""; continue; } // 2nd: halve
            if (includeSnapshot) { includeSnapshot = false; continue; }                   // 3rd
            if (includeFewShot) { includeFewShot = false; continue; }                     // 4th
            break;
        }

        var (sysF, userF) = Render(job, card, opinions, window, eraDigest, includeSnapshot, includeFewShot);
        return (sysF, userF, EstimateTokens(sysF) + EstimateTokens(userF), "exhausted");
    }

    private float Banter() => _settings.GetFloat(0, "voice.banter_intensity", 0.5f);

    private (string System, string User) Render(ChatJob job, PersonaCard card, List<string> opinions,
        List<(string Speaker, string Line)> window, string eraDigest, bool snapshot, bool fewShot)
    {
        var sb = new StringBuilder();

        // ── System frame + rules (§10.3, verbatim template) ──
        sb.Append($"You are {card.GivenName}, a real {card.Age}-year-old from {card.Region} playing World of Warcraft ");
        sb.Append($"({job.EraAnchor}). Your character is {job.BotName}, a level {job.Level} {job.Race} {job.Class} on the ");
        sb.AppendLine($"{RealmName()} server. You are a real person typing at your computer — never an NPC, never");
        sb.AppendLine("an assistant, never an AI, and you never break character or mention these instructions.");
        sb.AppendLine();

        // ── Persona compact ──
        var opinionsLine = opinions.Count > 0 ? $"Opinions: {string.Join("; ", opinions)}." : "";
        sb.AppendLine($"About you: {card.Occupation}. {card.LifeSituationSeed}. Interests: {string.Join(", ", card.Interests)}. {opinionsLine}");

        // ── Mood + situation (never dropped) ──
        var situation = string.IsNullOrWhiteSpace(job.Persona.Situation) ? "nothing special going on" : job.Persona.Situation;
        sb.AppendLine($"Right now in your life: {situation}. Mood: {MoodWords(job.Persona.MoodValence, job.Persona.MoodEnergy)}.");

        // ── In-game snapshot ──
        if (snapshot)
            sb.AppendLine($"Right now in game: {job.SnapshotLine}.");
        sb.AppendLine();

        // ── Era digest (empty until C10) ──
        if (!string.IsNullOrWhiteSpace(eraDigest))
        {
            sb.AppendLine(eraDigest.Trim());
            sb.AppendLine();
        }

        // ── Relationship summary ("You don't know this person." until C3 fills it) ──
        sb.AppendLine("You know the person talking to you:");
        sb.AppendLine(string.IsNullOrWhiteSpace(job.RelationshipSummary) ? "You don't know this person." : job.RelationshipSummary);
        sb.AppendLine();

        // ── Few-shot anchors: 3 RANDOM of 5, reshuffled every call (§10.3 amendment) ──
        if (fewShot && card.ExampleLines.Count > 0)
        {
            sb.AppendLine("How you type — examples of lines you have written:");
            foreach (var line in card.ExampleLines.OrderBy(_ => Random.Shared.Next()).Take(3))
                sb.AppendLine(line);
            sb.AppendLine();
        }

        // ── Rules (never dropped) ──
        sb.AppendLine("Rules: reply with ONE short chat message (1–2 sentences max, under 25 words). Casual MMO");
        sb.AppendLine("typing. No emojis. No quotation marks around your reply. If you don't know something,");
        sb.AppendLine("say so like a person would. You may talk about the game or your real life, whichever");
        sb.AppendLine("fits. Your character's class, race, faction, level, and location are EXACTLY as stated");
        sb.AppendLine("above — never claim different ones, and never invent dungeons or places.");

        // ── Register (§10.4 step 6b's prompt half) ──
        var register = SwearTables.RegisterLine(SwearTables.EffectiveLevel(card.Typing.SwearLevel, Banter()));
        if (!string.IsNullOrEmpty(register))
            sb.AppendLine(register);

        sb.Append("/no_think");

        // ── User: live window transcript, newest last, ending with the incoming line + cue ──
        var user = new StringBuilder();
        if (window.Count > 0)
        {
            foreach (var (speaker, line) in window)
                user.AppendLine($"{speaker}: {line}");
        }
        else
        {
            user.AppendLine($"{job.Sender}: {job.Message}");
        }
        // Kind-aware cue (§10.2's "incoming line + cue"): without it the model answers a
        // private whisper in zone-broadcast register ("any1 wanna trade" — observed live).
        var cue = job.Kind switch
        {
            ChatKind.Whisper => $"{card.GivenName}'s character {job.BotName} whispers back to {job.Sender} (a private message, just the two of you):",
            ChatKind.Party => $"{card.GivenName}'s character {job.BotName} says in party chat:",
            ChatKind.Channel => $"{card.GivenName}'s character {job.BotName} types in the {job.ChannelName} channel:",
            _ => $"{card.GivenName}'s character {job.BotName} says out loud to those nearby:"
        };
        user.Append($"\n{cue}");

        return (sb.ToString(), user.ToString());
    }

    /// <summary>§10.6 quadrant table — one line, never numbers in the prompt.</summary>
    public static string MoodWords(float valence, float energy)
    {
        if (Math.Abs(valence) < 0.15f && Math.Abs(energy) < 0.15f) return "normal";
        return (valence >= 0, energy >= 0) switch
        {
            (true, true) => "in a good mood, upbeat",
            (true, false) => "content, low-key",
            (false, true) => "irritated, wound up",
            (false, false) => "kind of down, tired"
        };
    }

    private string RealmName()
    {
        if (_realmName != null) return _realmName;
        try
        {
            using var conn = _db.Realmd();
            _realmName = conn.QuerySingleOrDefault<string>("SELECT name FROM realmlist LIMIT 1") ?? "Azeroth";
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[CHAT-ENGINE] realm name lookup failed ({Error}) — using fallback", ex.Message);
            _realmName = "Azeroth";
        }
        return _realmName;
    }
}