using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.SignalR;
using MangosSuperUI.Models;
using MangosSuperUI.Hubs;
using MangosSuperUI.BotLogic.Chat.Core;
using MangosSuperUI.BotLogic.Chat.Capacity;
using MangosSuperUI.BotLogic.Chat.Engine;
using MangosSuperUI.BotLogic.Tracking;

namespace MangosSuperUI.BotLogic.Chat.Voice;

/// <summary>
/// CHAT_ARCHITECTURE §6.3 — builds the ~voice.library_target card library:
///
///  1. STRATIFIED SKELETON FIRST (VoiceTables) — every diversity axis sampled before any
///     LLM call, INCLUDING the given name (see below). Triple quota checked pre-call: an
///     (age band, region, occupation) triple with ≥4 accepted cards is resampled for free,
///     and so is a given name already used 4 times.
///  2. ONE inference call per card, Batch class. The model writes ONLY occupation detail,
///     life_situation_seed, opinions, example_lines — to fit the skeleton (+ active era
///     pack source when one exists, §13.4(1)).
///  3. MECHANICAL GUARDS, then dedup. Prose regenerates (skeleton kept) up to 4 tries.
///  4. Store in chat_voice. RESUMABLE: counts existing non-retired rows toward target.
///
/// WHAT THE FIRST 300-CARD BUILD TAUGHT US (4B batch model, 2026-07-13). The structural
/// half worked perfectly — 300/300 distinct occupations, 1498/1500 distinct example
/// lines, full spread on swear_level and caps. The LLM half collapsed in two places, and
/// both are now closed HERE rather than by asking the model more nicely:
///
///   • 48 Dereks, 24 Dales — 76 distinct names in 300 cards. `given_name` was the one
///     identity field the LLM invented, and a small model has favorites. It is now a
///     VoiceTables axis; the model never sees a naming decision again.
///   • ~230 of 1500 example lines opened with a swear-comma ("damn, ...", "shit, ...",
///     "hell yeah, ..."). Told that register was the point, the model made profanity the
///     SUBJECT instead of the texture — which is the same register collapse we were
///     hired to fix, wearing different clothes. The prompt now says swearing goes
///     mid-sentence, never as an opener, and the GUARDS below enforce it, because a 4B
///     will ignore any instruction it finds inconvenient.
///
/// THIS IS THE FLEET'S ONLY DIVERSITY SOURCE. Every persona descends from a card here,
/// and the card's example_lines are what a small reactive model actually imitates — far
/// more than the bio does. Run it on the biggest model you can serve (profile's
/// model_batch); the broker warns when it silently falls back to the reactive tag.
///
/// Runs as a fire-and-forget admin action from the Capacity tab; progress via SignalR
/// ("VoiceLibraryProgress") + [CHAT-CAP] logs. One run at a time.
/// </summary>
public class VoiceLibraryBuilder
{
    private readonly ConnectionFactory _db;
    private readonly ChatSettingsService _settings;
    private readonly IInferenceBroker _broker;
    private readonly IHubContext<BotBridgeHub> _hub;
    private readonly ILogger<VoiceLibraryBuilder> _logger;

    private int _running;   // interlocked flag

    /// <summary>RejectedShape added 2026-07-13 — the guard rejects, so a bad batch model is visible.</summary>
    public sealed record BuildStatus(bool Running, int Accepted, int Target,
        int RejectedDedup, int RejectedParse, int RejectedShape,
        DateTime? StartedUtc, DateTime? FinishedUtc, string? Error);

    private volatile BuildStatus _status = new(false, 0, 0, 0, 0, 0, null, null, null);
    public BuildStatus Status => _status;

    public VoiceLibraryBuilder(ConnectionFactory db, ChatSettingsService settings,
        IInferenceBroker broker, IHubContext<BotBridgeHub> hub, ILogger<VoiceLibraryBuilder> logger)
    {
        _db = db;
        _settings = settings;
        _broker = broker;
        _hub = hub;
        _logger = logger;
    }

    /// <summary>Kick a build. Returns false if one is already running.</summary>
    public bool TryStart()
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) { CircuitTrace.Hit(0, "chat: voice build already running, start refused"); return false; }
        _ = Task.Run(RunAsync);
        return true;
    }

    private const int MaxPerName = 4;       // no more Dereks
    private const int MaxPerTriple = 4;     // §6.3
    private const int ProseTries = 4;

    private async Task RunAsync()
    {
        int target = Math.Max(10, _settings.GetInt(0, "voice.library_target", 300));
        int accepted = 0, rejectedDedup = 0, rejectedParse = 0, rejectedShape = 0;
        var started = DateTime.UtcNow;
        var shapeReasons = new Dictionary<string, int>();

        try
        {
            using var conn = _db.Admin();

            // Resumable: existing non-retired cards count toward the target.
            var existing = (await conn.QueryAsync<string>(
                "SELECT card_json FROM chat_voice WHERE retired=0")).ToList();
            var existingCards = existing.Select(PersonaCard.Parse).Where(c => c != null).Select(c => c!).ToList();
            accepted = existing.Count;

            var acceptedTrigrams = existingCards.Select(c => Trigrams(c.ExampleLines)).ToList();
            var tripleCounts = new Dictionary<(string, string, string), int>();
            var nameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in existingCards)
            {
                var t = TripleOf(c);
                tripleCounts[t] = tripleCounts.GetValueOrDefault(t) + 1;
                nameCounts[c.GivenName] = nameCounts.GetValueOrDefault(c.GivenName) + 1;
            }

            // Era pack source in the generation context when one is active (§13.4(1)).
            string eraContext = await conn.QuerySingleOrDefaultAsync<string?>(
                "SELECT source_md FROM chat_era_pack WHERE active=1 LIMIT 1") ?? "";
            if (eraContext.Length > 3000) { CircuitTrace.Hit(0, "chat: era context truncated for build"); eraContext = eraContext[..3000]; }

            _logger.LogInformation("[CHAT-CAP] voice library build started: {Existing} existing, target {Target}",
                accepted, target);
            await Publish(accepted, target, rejectedDedup, rejectedParse, rejectedShape, started, null, null, running: true);

            var rng = Random.Shared;
            int attempts = 0, maxAttempts = target * 6;

            while (accepted < target && attempts < maxAttempts)
            {
                attempts++;

                // ── 1. Skeleton — free resampling until the triple AND name quotas pass ──
                VoiceTables.Skeleton skel;
                int resamples = 0;
                do { skel = VoiceTables.Sample(rng); }
                while ((tripleCounts.GetValueOrDefault(TripleOf(skel)) >= MaxPerTriple ||
                        nameCounts.GetValueOrDefault(skel.GivenName) >= MaxPerName)
                       && ++resamples < 200);

                // ── 2. Prose ──
                PersonaCard? card = null;
                for (int prose = 0; prose < ProseTries && card == null; prose++)
                {
                    using var lease = await _broker.TryAcquireAsync(TrafficClass.Batch,
                        TimeSpan.FromSeconds(30), CancellationToken.None);
                    if (lease == null)
                    {
                        CircuitTrace.Hit(0, "chat: voice build starved of batch lease, pausing");
                        _logger.LogWarning("[CHAT-CAP] voice build: no Batch lease — pausing 15 s");
                        await Task.Delay(TimeSpan.FromSeconds(15));
                        continue;
                    }

                    var (system, prompt) = BuildProsePrompt(skel, eraContext);
                    var raw = await _broker.GenerateAsync(lease, system, prompt,
                        new GenOptions(0.95f, 0.92f, 400, RepeatPenalty: 1.05f, RepeatLastN: 256,
                                       PresencePenalty: 0.3f, Seed: rng.Next()),
                        CancellationToken.None);
                    if (raw == null) { CircuitTrace.Hit(0, "chat: voice build raw generation failed"); rejectedParse++; continue; }

                    var parsed = ParseProse(raw);
                    if (parsed == null) { CircuitTrace.Hit(0, "chat: voice build prose unparseable"); rejectedParse++; continue; }

                    var candidate = Materialize(skel, parsed);

                    // ── 3a. Shape guards (the 4B ignores instructions; these do not) ──
                    var bad = ShapeViolation(candidate);
                    if (bad != null)
                    {
                        CircuitTrace.HitNote(0, "chat: voice build card rejected on shape", bad);
                        rejectedShape++;
                        shapeReasons[bad] = shapeReasons.GetValueOrDefault(bad) + 1;
                        continue;   // regenerate prose, skeleton kept
                    }

                    // ── 3b. Mechanical dedup: >2 shared word-trigrams with ANY accepted card ──
                    var tris = Trigrams(candidate.ExampleLines);
                    if (acceptedTrigrams.Any(a => a.Intersect(tris).Count() > 2))
                    {
                        CircuitTrace.Hit(0, "chat: voice build card rejected on trigram dedup");
                        rejectedDedup++;
                        continue;
                    }

                    card = candidate;
                    acceptedTrigrams.Add(tris);
                }

                if (card == null) { CircuitTrace.Hit(0, "chat: voice build skeleton exhausted prose tries"); continue; }   // skeleton exhausted its prose tries — resample

                // ── 4. Store ──
                await conn.ExecuteAsync(
                    "INSERT INTO chat_voice (card_json, era_pack_id, created_utc) VALUES (@json, NULL, UTC_TIMESTAMP())",
                    new { json = card.ToJson() });
                var triple = TripleOf(card);
                tripleCounts[triple] = tripleCounts.GetValueOrDefault(triple) + 1;
                nameCounts[card.GivenName] = nameCounts.GetValueOrDefault(card.GivenName) + 1;
                accepted++;

                if (accepted % 10 == 0)
                {
                    CircuitTrace.Hit(0, "chat: voice build progress checkpoint", accepted);
                    _logger.LogInformation("[CHAT-CAP] voice build: {Accepted}/{Target} (rejects — dedup {Dedup}, parse {Parse}, shape {Shape})",
                        accepted, target, rejectedDedup, rejectedParse, rejectedShape);
                    await Publish(accepted, target, rejectedDedup, rejectedParse, rejectedShape, started, null, null, running: true);
                }
            }

            var note = accepted >= target ? "complete" : $"stopped at attempt cap ({maxAttempts})";
            _logger.LogInformation("[CHAT-CAP] voice library build {Note}: {Accepted}/{Target}, rejects — dedup {Dedup}, parse {Parse}, shape {Shape}",
                note, accepted, target, rejectedDedup, rejectedParse, rejectedShape);
            if (shapeReasons.Count > 0)
                _logger.LogInformation("[CHAT-CAP] shape rejects by reason: {Reasons}",   // cb:fold logging only, rejects carried by shape reject probes
                    string.Join(", ", shapeReasons.OrderByDescending(k => k.Value).Select(k => $"{k.Key}={k.Value}")));

            await Publish(accepted, target, rejectedDedup, rejectedParse, rejectedShape, started, DateTime.UtcNow, null, running: false);
        }
        catch (Exception ex)
        {
            CircuitTrace.Hit(0, "chat: voice library build failed");
            _logger.LogError(ex, "[CHAT-CAP] voice library build failed");
            await Publish(accepted, target, rejectedDedup, rejectedParse, rejectedShape, started, DateTime.UtcNow, ex.Message, running: false);
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }

    // ==================== Shape guards ====================

    private static readonly Regex SwearOpener = new(
        @"^\s*(fuck|fuckin|fucking|shit|damn|damned|goddamn|hell|crap|ass|asshole|bastard|bullshit|christ|jesus|piss|dick)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex GreetingOpener = new(
        @"^\s*(hey|hi|hello|yo|sup|wassup|wazzup|what'?s up|howdy|greetings|good morning|good evening)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Bowdlerism = new(
        @"(?<![a-z0-9])(heck|darn|darned|freaking|freakin|frickin|friggin|gosh|golly|shucks)(?![a-z0-9])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>How many of the five lines may contain profanity at all, by swear level.
    /// Two is the ceiling even for a sailor: these are FEW-SHOT ANCHORS, and if most of
    /// them swear, every reply the bot ever writes will swear.</summary>
    private static int MaxSwearLines(int level) => level switch { 0 => 0, 1 => 1, _ => 2 };   // cb:fold pure level table, verdict carried by shape reject probe

    /// <summary>Null = the card is fine; otherwise the reason, for the reject log.</summary>
    public static string? ShapeViolation(PersonaCard c)
    {
        var lines = c.ExampleLines;
        int level = c.Typing.SwearLevel;

        int swearLines = lines.Count(SwearTables.ContainsSwear);
        if (swearLines > MaxSwearLines(level)) return "swear-density";   // cb:fold shape reason carried by build reject probe

        // Profanity is texture, not the subject of the sentence. At most one of five may
        // lead with it, and only for a persona who actually swears that much.
        int swearOpeners = lines.Count(l => SwearOpener.IsMatch(l));
        if (swearOpeners > (level >= 2 ? 1 : 0)) return "swear-opener";   // cb:fold shape reason carried by build reject probe

        // "hey guys" x5 is exactly how the fallback card collapsed the fleet.
        int greetings = lines.Count(l => GreetingOpener.IsMatch(l));
        if (greetings > 1) return "greeting-opener";   // cb:fold shape reason carried by build reject probe

        // A swearing persona whose anchors say "darn" teaches the bot to say "darn".
        if (level >= 1 && lines.Any(l => Bowdlerism.IsMatch(l))) return "bowdlerized";   // cb:fold shape reason carried by build reject probe

        // The model likes writing the persona's own name into their chat lines
        // ("hey kaito here again") — nobody types their own name at people.
        if (!string.IsNullOrEmpty(c.GivenName) &&
            lines.Any(l => l.Contains(c.GivenName, StringComparison.OrdinalIgnoreCase)))
            return "self-name";   // cb:fold shape reason carried by build reject probe

        // Anchors must be chat, not prose. Five long lines produce a bot that writes essays.
        if (lines.Count(l => l.Length > 90) > 1) return "too-long";   // cb:fold shape reason carried by build reject probe

        return null;
    }

    // ==================== Prose prompt + parse ====================

    /// <summary>Plain-English register brief for the prose model (§6.2 v2 swear_level).</summary>
    private static string SwearBrief(int level) => level switch
    {
        0 => "never swears. Not once. This person says \"darn\" and means it — the one clean mouth in the zone. " +   // cb:fold prompt content table, no guid in reach
             "NONE of the five lines may contain profanity",
        1 => "swears mildly, and only when something goes wrong: damn, crap, hell. AT MOST ONE of the five " +   // cb:fold prompt content table, no guid in reach
             "lines contains any profanity, and it falls mid-sentence, never at the start",
        2 => "swears casually the way most people did in 2005: damn, shit, ass, bastard; will call a bad " +   // cb:fold prompt content table, no guid in reach
             "player a shitter or a scrub. AT MOST TWO of the five lines contain profanity, woven " +
             "mid-sentence where the emphasis falls",
        _ => "swears a lot — profanity is punctuation to this person. Even so, AT MOST TWO of the five lines " +   // cb:fold prompt content table, no guid in reach
             "contain profanity, woven mid-sentence rather than parked at the front",
    };

    private static (string System, string Prompt) BuildProsePrompt(VoiceTables.Skeleton s, string eraContext)
    {
        string system =
            "You write fictional 2005-era World of Warcraft player personas for an offline game server. " +
            "You are given a fixed skeleton (name, age, region, occupation, interests, typing style, " +
            "swearing register) and you write ONLY the prose fields to fit it. " +
            "Era: 2005 — flip phones, dial-up jokes, no smartphones, no streaming, no post-2005 references. " +
            "Write ordinary people, not characters. Output ONLY a JSON object, no markdown fences.";

        string era = string.IsNullOrWhiteSpace(eraContext) ? "" :
            $"\nActive era pack source (match its slang/references):\n{eraContext}\n";

        string prompt = $$"""
            Skeleton (fixed — do not contradict it):
            - name: {{s.GivenName}}, age {{s.Age}}, {{s.Region}}
            - occupation category: {{s.OccupationCategory}}
            - interests: {{string.Join(", ", s.Interests)}}
            - gaming background: {{s.GamingBackground}}
            - humor: {{s.Humor}}
            - typing: caps={{s.Typing.Caps}}, punctuation={{s.Typing.Punctuation}}, abbreviation level {{s.Typing.AbbrevLevel}}/3
            - swearing: level {{s.Typing.SwearLevel}}/3 — {{SwearBrief(s.Typing.SwearLevel)}}
            {{era}}
            The five example_lines are the most important field by far: they are the ONLY thing a small
            model sees when it imitates this person, so whatever pattern is in them is the pattern the
            bot will repeat forever. Therefore:

            - FIVE DIFFERENT SHAPES. At most ONE may be a greeting. Include at least one question and at
              least one line about their life outside the game. Not every line is about WoW.
            - PROFANITY IS TEXTURE, NOT THE SUBJECT. If this person swears, the swearing sits inside a
              sentence about something else ("that quest is a damn maze") — it is NEVER the first word,
              and it is NEVER the point of the line. Obey the swearing limit above exactly.
            - Never write asterisks (f***) and never write "heck", "darn" or "freaking" unless this
              person's swear level is 0.
            - Never write {{s.GivenName}}'s own name into a chat line. Nobody types their own name.
            - Ordinary, specific, unglamorous: what this person types on a Tuesday night. Not catchphrases,
              not slogans, not one-liners.
            - No slurs of any kind. Nothing sexual.

            Write JSON with EXACTLY these fields:
            {
              "occupation": "one specific sentence expanding the occupation category",
              "life_situation_seed": "one line about their current life situation",
              "opinions": ["two short opinions about WoW or life", "..."],
              "example_lines": ["five short chat lines, each under 15 words, matching the typing and",
                                "swearing style above, five different shapes, no emojis, era-correct",
                                "...", "...", "..."]
            }
            """;
        return (system, prompt);
    }

    private sealed class ProseResult
    {
        [JsonPropertyName("occupation")] public string Occupation { get; set; } = "";
        [JsonPropertyName("life_situation_seed")] public string LifeSituationSeed { get; set; } = "";
        [JsonPropertyName("opinions")] public List<string> Opinions { get; set; } = new();
        [JsonPropertyName("example_lines")] public List<string> ExampleLines { get; set; } = new();
    }

    private static ProseResult? ParseProse(string raw)
    {
        var text = raw.Trim();
        int start = text.IndexOf('{');
        int end = text.LastIndexOf('}');
        if (start < 0 || end <= start) return null;   // cb:fold parse detail carried by build parse-reject probe
        try
        {
            var r = JsonSerializer.Deserialize<ProseResult>(text[start..(end + 1)]);
            if (r == null) return null;   // cb:fold parse detail carried by build parse-reject probe
            if (string.IsNullOrWhiteSpace(r.Occupation)) return null;   // cb:fold parse detail carried by build parse-reject probe
            if (r.ExampleLines.Count != 5 || r.ExampleLines.Any(string.IsNullOrWhiteSpace)) return null;   // cb:fold parse detail carried by build parse-reject probe
            if (r.ExampleLines.Any(l => l.Length > 120)) return null;   // cb:fold parse detail carried by build parse-reject probe
            if (r.Opinions.Count < 1) return null;   // cb:fold parse detail carried by build parse-reject probe
            return r;
        }
        catch { return null; }   // cb:fold parse detail carried by build parse-reject probe
    }

    /// <summary>
    /// given_name now comes from the SKELETON, never from the model (see class doc).
    /// Example lines get compound repair on the way in ("ass hole" → "asshole") — no point
    /// burning a whole generation on a spelling mistake we can fix deterministically.
    /// </summary>
    private static PersonaCard Materialize(VoiceTables.Skeleton s, ProseResult prose) => new()
    {
        V = 2,
        GivenName = s.GivenName,
        Age = s.Age,
        Region = s.Region,
        TimezoneOffset = s.TimezoneOffset,
        Occupation = prose.Occupation.Trim(),
        LifeSituationSeed = prose.LifeSituationSeed.Trim(),
        Disposition = s.Disposition,
        Interests = s.Interests,
        GamingBackground = s.GamingBackground,
        Opinions = prose.Opinions.Take(3).Select(o => o.Trim()).ToList(),
        Typing = s.Typing,
        ExampleLines = prose.ExampleLines.Take(5)
            .Select(l => SwearTables.RepairCompounds(l.Trim()))
            .ToList()
    };

    // ==================== Dedup mechanics ====================

    private static (string, string, string) TripleOf(PersonaCard c) =>
        (BandOf(c.Age), c.Region, c.Occupation.Split(',')[0].Trim().ToLowerInvariant());

    private static (string, string, string) TripleOf(VoiceTables.Skeleton s) =>
        (BandOf(s.Age), s.Region, s.OccupationCategory.ToLowerInvariant());

    private static string BandOf(int age) =>
        VoiceTables.AgeBands.FirstOrDefault(b => age >= b.Min && age <= b.Max)?.Name ?? "19-23";

    private static HashSet<string> Trigrams(IEnumerable<string> lines)
    {
        var set = new HashSet<string>();
        foreach (var line in lines)
        {
            var words = line.ToLowerInvariant()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (int i = 0; i + 2 < words.Length; i++)
                set.Add($"{words[i]} {words[i + 1]} {words[i + 2]}");
        }
        return set;
    }

    private async Task Publish(int accepted, int target, int dedup, int parse, int shape,
        DateTime started, DateTime? finished, string? error, bool running)
    {
        _status = new BuildStatus(running, accepted, target, dedup, parse, shape, started, finished, error);
        try
        {
            await _hub.Clients.All.SendAsync("VoiceLibraryProgress",
                new { running, accepted, target, rejectedDedup = dedup, rejectedParse = parse, rejectedShape = shape, error });
        }
        catch { CircuitTrace.Hit(0, "chat: voice build progress push failed"); /* dashboard push is best-effort */ }
    }
}