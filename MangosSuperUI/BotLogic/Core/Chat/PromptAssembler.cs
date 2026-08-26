using System.Text;
using Dapper;
using MangosSuperUI.Models;
using MangosSuperUI.BotLogic.Chat.Core;
using MangosSuperUI.BotLogic.Tracking;

namespace MangosSuperUI.BotLogic.Chat.Engine;

/// <summary>One reactive reply job — everything the assembler needs (built by the coordinator).</summary>
public sealed record ChatJob(
    int BotGuid, string BotName, int Level, string Race, string Class,
    BotPersona Persona, string SnapshotLine, string RelationshipSummary, string EraAnchor,
    string EraDigest, IReadOnlyList<(string Speaker, string Line)> LiveWindow,
    string Sender, string Message, ChatKind Kind, string ChannelName, DateTime RecvUtc,
    ChatActivity Activity = ChatActivity.Idle);

/// <summary>
/// What the bot is ACTUALLY doing while it types (2026-07-20). Set by the coordinator from
/// live BotState — never assumed. The prompt frames the conversation around this, because a
/// bot mid-fight, a bot corpse-running and a bot stood in a city do not chat the same way, and
/// hardcoding any one of them was the previous version's mistake.
/// </summary>
public enum ChatActivity { Idle, Fighting, Dead, Travelling, Grinding, Recovering, Stuck }

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
                CircuitTrace.Hit(job.BotGuid, "chat: prompt render accepted", est);
                var report = $"win={window.Count}L era={EstimateTokens(eraDigest)}t snap={includeSnapshot} " +
                             $"fewshot={includeFewShot} opinions={opinions.Count} " +
                             $"swear={SwearTables.EffectiveLevel(card.Typing.SwearLevel, Banter())} " +
                             $"est={est}t cap={cap}t";
                if (est > cap)
                {
                    CircuitTrace.Hit(job.BotGuid, "chat: prompt over budget after all drops", est);
                    _logger.LogWarning("[CHAT-ENGINE] prompt over budget after all drops for {Bot}: {Report}", job.BotName, report);
                }
                return (system, user, est, report);
            }

            // Drop order (§10.2)
            if (opinions.Count > 0) { CircuitTrace.Hit(job.BotGuid, "chat: budget drop, opinions trimmed"); opinions.Clear(); continue; }                       // persona: trim opinions first
            if (window.Count > 1) { CircuitTrace.Hit(job.BotGuid, "chat: budget drop, window halved", window.Count); window = window.Skip(window.Count / 2).ToList(); continue; } // 1st: halve, keep newest
            if (eraDigest.Length > 0)
            {
                CircuitTrace.Hit(job.BotGuid, "chat: budget drop, era digest halved");
                eraDigest = eraDigest[..(eraDigest.Length / 2)];
                if (eraDigest.Length < 40) { CircuitTrace.Hit(job.BotGuid, "chat: era digest under floor, cleared"); eraDigest = ""; }
                continue;
            } // 2nd: halve
            if (includeSnapshot) { CircuitTrace.Hit(job.BotGuid, "chat: budget drop, snapshot removed"); includeSnapshot = false; continue; }                   // 3rd
            if (includeFewShot) { CircuitTrace.Hit(job.BotGuid, "chat: budget drop, few-shot removed"); includeFewShot = false; continue; }                     // 4th
            break;
        }

        var (sysF, userF) = Render(job, card, opinions, window, eraDigest, includeSnapshot, includeFewShot);
        return (sysF, userF, EstimateTokens(sysF) + EstimateTokens(userF), "exhausted");
    }

    private float Banter() => _settings.GetFloat(0, "voice.banter_intensity", 0.5f);

    /// <summary>
    /// PROMPT REWRITE (2026-07-20) — conversation completion, not character performance.
    ///
    /// THE PROBLEM WITH THE OLD SHAPE. The prompt handed the model a character brief, nine
    /// rules, and a stage direction ("Justin's character Vase says in party chat:"). That is an
    /// INSTRUCTION, so the model performed: every line came back a tidy, complete, in-character
    /// statement, and because the system block also carried "Right now in game: lvl 16, just
    /// hanging out in Westfall", nearly every one of those statements was about the game. Live
    /// transcript, four consecutive lines: farming Westfall for cash, hit some rats, farming
    /// raptor hides, sitting here in westfall. Nobody standing in a party talks like that. They
    /// shoot the shit. A player in-game said "u guys sound like monotone bots" and he was right.
    ///
    /// THE NEW SHAPE. Short human sketch, few rules, then the transcript, ending with the bot's
    /// own name and a colon. The model is not being asked to act — it is being asked to finish a
    /// chat log, which is a thing base models are extremely good at and which produces the
    /// fragments, the non-sequiturs and the drift that real chat is made of.
    ///
    /// The in-game snapshot is now RARE by design: it goes in only when something is actually
    /// happening (dead, fighting, travelling). "Just hanging out" is not news and mentioning it
    /// every call is precisely what caused the constant game-narration.
    /// </summary>
    private (string System, string User) Render(ChatJob job, PersonaCard card, List<string> opinions,
        List<(string Speaker, string Line)> window, string eraDigest, bool snapshot, bool fewShot)
    {
        var sb = new StringBuilder();
        bool alreadySpoke = window.Any(w => string.Equals(w.Speaker, job.BotName, StringComparison.OrdinalIgnoreCase));

        // ── Who you are (person first, character second) ──
        sb.AppendLine($"You're {card.GivenName}, {card.Age}, from {card.Region}. You play World of Warcraft ({job.EraAnchor}) " +
                      $"in your spare time. Tonight you're logged in as {job.BotName}, a level {job.Level} {job.Race} {job.Class} " +
                      $"on {RealmName()}.");
        sb.AppendLine("You are a real person typing in a chat window — never an AI, never an NPC, and you never mention these notes.");
        sb.AppendLine();

        // PERSONA DECAY (2026-07-20): full introduction only until the bot has spoken. Restating
        // "I work the night shift, my radiator quit" every turn is what made Him name his radiator
        // in 3 of 4 consecutive lines and Vase his kiosk in 3 of 4.
        if (!alreadySpoke)
        {
            CircuitTrace.Hit(job.BotGuid, "chat: prompt full persona intro");
            sb.AppendLine($"About you: {card.Occupation}. {card.LifeSituationSeed}.");
            sb.AppendLine($"Into: {string.Join(", ", card.Interests)}.");
            if (opinions.Count > 0) { CircuitTrace.Hit(job.BotGuid, "chat: prompt includes opinions", opinions.Count); sb.AppendLine($"Things you think: {string.Join("; ", opinions)}."); }
        }
        else
        {
            CircuitTrace.Hit(job.BotGuid, "chat: prompt decayed persona intro");
            sb.AppendLine($"Into: {string.Join(", ", card.Interests)}. Your job and home situation already came up — don't bring them up again.");
        }

        var situation = string.IsNullOrWhiteSpace(job.Persona.Situation) ? "" : $" {job.Persona.Situation}.";
        sb.AppendLine($"Mood: {MoodWords(job.Persona.MoodValence, job.Persona.MoodEnergy)}.{situation}");

        // The bot's REAL in-game state. Always included — it is live context, not decoration.
        if (snapshot && !string.IsNullOrWhiteSpace(job.SnapshotLine))
        {
            CircuitTrace.Hit(job.BotGuid, "chat: prompt includes in-game snapshot");
            sb.AppendLine($"In game right now: {job.SnapshotLine}.");
        }
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(eraDigest))
        {
            CircuitTrace.Hit(job.BotGuid, "chat: prompt includes era digest");
            sb.AppendLine(eraDigest.Trim());
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(job.RelationshipSummary))
        {
            CircuitTrace.Hit(job.BotGuid, "chat: prompt includes relationship summary");
            sb.AppendLine(job.RelationshipSummary);
            sb.AppendLine();
        }

        if (fewShot && card.ExampleLines.Count > 0)
        {
            CircuitTrace.Hit(job.BotGuid, "chat: prompt includes few-shot lines");
            sb.AppendLine("How you type:");
            foreach (var line in card.ExampleLines.OrderBy(_ => Random.Shared.Next()).Take(3))
                sb.AppendLine(line);
            sb.AppendLine();
        }

        // ── The few rules that survive ──
        //
        // Rule count is itself a variable: the more instructions, the more the output reads as
        // compliance. What's left is (a) the anti-narration rule, which is the actual fix for
        // "every line is about farming", (b) shape, and (c) the two things that break immersion
        // if the model invents them.
        // SITUATIONAL FRAMING (2026-07-20) — derived from live state, never assumed.
        // An earlier version hardcoded "you're stood around doing nothing", which is wrong the
        // moment the bot is fighting, dead or running somewhere. What the bot is doing changes
        // how it would type, so the frame is chosen from Activity.
        sb.AppendLine(job.Activity switch
        {
            ChatActivity.Fighting => CircuitTrace.Pass("You're in the middle of a fight while you type this — short, clipped, maybe a bit late.", job.BotGuid, "chat: prompt frame fighting"),
            ChatActivity.Dead => CircuitTrace.Pass("You're dead and running back to your corpse, so you've got nothing but time to type.", job.BotGuid, "chat: prompt frame dead"),
            ChatActivity.Travelling => CircuitTrace.Pass("You're running somewhere with autorun on, half paying attention.", job.BotGuid, "chat: prompt frame travelling"),
            ChatActivity.Grinding => CircuitTrace.Pass("You're killing mobs while you chat — typing between pulls.", job.BotGuid, "chat: prompt frame grinding"),
            ChatActivity.Recovering => CircuitTrace.Pass("You're sat down eating and drinking after a fight, so you can type properly.", job.BotGuid, "chat: prompt frame recovering"),
            ChatActivity.Stuck => CircuitTrace.Pass("You're stuck on something in the game and mildly annoyed about it.", job.BotGuid, "chat: prompt frame stuck"),
            _ => CircuitTrace.Pass("You're not doing anything in particular right now — just stood about with your party.", job.BotGuid, "chat: prompt frame idle")
        });

        // ANTI-NARRATION (2026-07-20). This is the fix for "every other line is 'gonna farm
        // raptor hides'". The rule is about PROMPTING, not about being idle: talk about the game
        // when it is actually happening to you or when someone asks. Do not announce plans into
        // silence. Live transcript before this rule existed: four consecutive lines about farming
        // Westfall, hitting rats, farming raptor hides, sitting in Westfall — and a player replied
        // "u guys sound like monotone bots".
        sb.AppendLine("This is just chat — shooting the shit. Don't announce your plans or narrate what you're");
        sb.AppendLine("doing unless someone asks you or something actually just happened to you. Real players go");
        sb.AppendLine("whole conversations without mentioning the game at all.");

        // REPLY SHAPE (2026-07-20): sampled per call. The old prompt asked for "1-2 sentences,
        // under 25 words" every single time, which is why every line came out the same size and a
        // transcript of them read as a list. Length variation is most of what sounds human.
        sb.AppendLine(Random.Shared.Next(100) switch
        {
            < 40 => CircuitTrace.Pass("Write ONE short fragment — a few words. Not a sentence. lowercase is fine.", job.BotGuid, "chat: reply shape fragment"),
            < 80 => CircuitTrace.Pass("Write ONE short line, under 12 words.", job.BotGuid, "chat: reply shape short line"),
            _ => CircuitTrace.Pass("Write one or two short sentences, under 25 words.", job.BotGuid, "chat: reply shape sentences")
        });
        sb.AppendLine($"You are {job.BotName}. Your class, race, level and location are exactly as stated above.");
        sb.AppendLine("Don't invent places or dungeons. No emojis. No quote marks around your line.");

        var register = SwearTables.RegisterLine(SwearTables.EffectiveLevel(card.Typing.SwearLevel, Banter()));
        if (!string.IsNullOrEmpty(register)) { CircuitTrace.Hit(job.BotGuid, "chat: prompt includes swear register line"); sb.AppendLine(register); }

        var ownRecent = window
            .Where(w => string.Equals(w.Speaker, job.BotName, StringComparison.OrdinalIgnoreCase))
            .Select(w => w.Line).TakeLast(2).ToList();
        if (ownRecent.Count > 0)
        {
            CircuitTrace.Hit(job.BotGuid, "chat: prompt includes no-repeat block", ownRecent.Count);
            sb.AppendLine();
            sb.AppendLine("You already said these — don't repeat yourself:");
            foreach (var l in ownRecent) sb.AppendLine($"- {l}");
        }

        sb.Append("/no_think");

        // ── USER: the transcript, ending on the bot's own name ──
        //
        // This is the whole point of the rewrite. The old cue was a stage direction
        // ("Justin's character Vase says in party chat:") which asks for a performance. Ending on
        // "Vase:" asks the model to CONTINUE A CHAT LOG — the thing it is best at, and the thing
        // that produces natural turn-taking instead of narrated statements.
        var user = new StringBuilder();
        if (job.Kind == ChatKind.Whisper)
        {
            CircuitTrace.Hit(job.BotGuid, "chat: transcript header whisper");
            user.AppendLine($"[private whisper between you and {job.Sender}]");
        }
        else if (job.Kind == ChatKind.Channel)
        {
            CircuitTrace.Hit(job.BotGuid, "chat: transcript header channel");
            user.AppendLine($"[{job.ChannelName} channel]");
        }
        else if (job.Kind == ChatKind.Party)
        {
            CircuitTrace.Hit(job.BotGuid, "chat: transcript header party");
            user.AppendLine("[party chat]");
        }

        if (window.Count > 0)
        {
            CircuitTrace.Hit(job.BotGuid, "chat: transcript from live window", window.Count);
            foreach (var (speaker, line) in window)
                user.AppendLine($"{speaker}: {line}");
        }
        else
        {
            CircuitTrace.Hit(job.BotGuid, "chat: transcript from single stimulus");
            user.AppendLine($"{job.Sender}: {job.Message}");
        }

        user.Append($"{job.BotName}:");

        return (sb.ToString(), user.ToString());
    }

    /// <summary>§10.6 quadrant table — one line, never numbers in the prompt.</summary>
    public static string MoodWords(float valence, float energy)
    {
        if (Math.Abs(valence) < 0.15f && Math.Abs(energy) < 0.15f) return "normal";   // cb:fold pure prose transform for prompt, no guid in reach
        return (valence >= 0, energy >= 0) switch
        {
            (true, true) => "in a good mood, upbeat",   // cb:fold pure prose transform for prompt, no guid in reach
            (true, false) => "content, low-key",   // cb:fold pure prose transform for prompt, no guid in reach
            (false, true) => "irritated, wound up",   // cb:fold pure prose transform for prompt, no guid in reach
            (false, false) => "kind of down, tired"   // cb:fold pure prose transform for prompt, no guid in reach
        };
    }

    private string RealmName()
    {
        if (_realmName != null) { CircuitTrace.Hit(0, "chat: realm name cache hit"); return _realmName; }
        try
        {
            using var conn = _db.Realmd();
            _realmName = conn.QuerySingleOrDefault<string>("SELECT name FROM realmlist LIMIT 1") ?? "Azeroth";
        }
        catch (Exception ex)
        {
            CircuitTrace.Hit(0, "chat: realm name lookup failed, fallback used");
            _logger.LogWarning("[CHAT-ENGINE] realm name lookup failed ({Error}) — using fallback", ex.Message);
            _realmName = "Azeroth";
        }
        return _realmName;
    }
}