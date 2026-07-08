using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using Microsoft.AspNetCore.SignalR;
using MangosSuperUI.Models;
using MangosSuperUI.Hubs;
using MangosSuperUI.BotLogic.Chat.Core;
using MangosSuperUI.BotLogic.Chat.Capacity;

namespace MangosSuperUI.BotLogic.Chat.Voice;

/// <summary>
/// CHAT_ARCHITECTURE §6.3 — builds the ~voice.library_target card library:
///
///  1. STRATIFIED SKELETON FIRST (VoiceTables) — every diversity axis sampled before
///     any LLM call. Triple quota checked pre-call: an (age band, region, occupation)
///     triple with ≥4 accepted cards is resampled for free.
///  2. ONE inference call per card, Batch class. The model writes ONLY occupation
///     detail, life_situation_seed, opinions, example_lines — to fit the skeleton
///     (+ active era pack source when one exists, §13.4(1)).
///  3. MECHANICAL DEDUP: reject when example_lines share &gt;2 exact word-trigrams with
///     any single accepted card. Prose regenerates (skeleton kept) up to 3 tries.
///  4. Store in chat_voice. RESUMABLE: counts existing non-retired rows toward target.
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

    public sealed record BuildStatus(bool Running, int Accepted, int Target,
        int RejectedDedup, int RejectedParse, DateTime? StartedUtc, DateTime? FinishedUtc, string? Error);

    private volatile BuildStatus _status = new(false, 0, 0, 0, 0, null, null, null);
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
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return false;
        _ = Task.Run(RunAsync);
        return true;
    }

    private async Task RunAsync()
    {
        int target = Math.Max(10, _settings.GetInt(0, "voice.library_target", 300));
        int accepted = 0, rejectedDedup = 0, rejectedParse = 0;
        var started = DateTime.UtcNow;

        try
        {
            using var conn = _db.Admin();

            // Resumable: existing non-retired cards count toward the target.
            var existing = (await conn.QueryAsync<string>(
                "SELECT card_json FROM chat_voice WHERE retired=0")).ToList();
            accepted = existing.Count;

            var acceptedTrigrams = existing
                .Select(PersonaCard.Parse).Where(c => c != null)
                .Select(c => Trigrams(c!.ExampleLines)).ToList();
            var tripleCounts = new Dictionary<(string, string, string), int>();
            foreach (var c in existing.Select(PersonaCard.Parse).Where(c => c != null))
            {
                var t = TripleOf(c!);
                tripleCounts[t] = tripleCounts.GetValueOrDefault(t) + 1;
            }

            // Era pack source in the generation context when one is active (§13.4(1)).
            string eraContext = await conn.QuerySingleOrDefaultAsync<string?>(
                "SELECT source_md FROM chat_era_pack WHERE active=1 LIMIT 1") ?? "";
            if (eraContext.Length > 3000) eraContext = eraContext[..3000];

            _logger.LogInformation("[CHAT-CAP] voice library build started: {Existing} existing, target {Target}",
                accepted, target);
            await Publish(accepted, target, rejectedDedup, rejectedParse, started, null, null, running: true);

            var rng = Random.Shared;
            int attempts = 0, maxAttempts = target * 4;

            while (accepted < target && attempts < maxAttempts)
            {
                attempts++;

                // ── 1. Skeleton (free until the triple quota passes) ──
                VoiceTables.Skeleton skel;
                int resamples = 0;
                do { skel = VoiceTables.Sample(rng); }
                while (tripleCounts.GetValueOrDefault(TripleOf(skel)) >= 4 && ++resamples < 50);

                // ── 2. Prose: up to 3 tries per skeleton (§6.3: regenerate prose, keep skeleton) ──
                PersonaCard? card = null;
                for (int prose = 0; prose < 3 && card == null; prose++)
                {
                    using var lease = await _broker.TryAcquireAsync(TrafficClass.Batch,
                        TimeSpan.FromSeconds(30), CancellationToken.None);
                    if (lease == null)
                    {
                        _logger.LogWarning("[CHAT-CAP] voice build: no Batch lease — pausing 15 s");
                        await Task.Delay(TimeSpan.FromSeconds(15));
                        continue;
                    }

                    var (system, prompt) = BuildProsePrompt(skel, eraContext);
                    var raw = await _broker.GenerateAsync(lease, system, prompt,
                        new GenOptions(0.95f, 0.92f, 400), CancellationToken.None);
                    if (raw == null) { rejectedParse++; continue; }

                    var parsed = ParseProse(raw);
                    if (parsed == null) { rejectedParse++; continue; }

                    var candidate = Materialize(skel, parsed);

                    // ── 3. Mechanical dedup: >2 shared word-trigrams with ANY accepted card ──
                    var tris = Trigrams(candidate.ExampleLines);
                    if (acceptedTrigrams.Any(a => a.Intersect(tris).Count() > 2))
                    {
                        rejectedDedup++;
                        continue;   // regenerate prose, skeleton kept
                    }
                    card = candidate;
                    acceptedTrigrams.Add(tris);
                }

                if (card == null) continue;   // skeleton exhausted its prose tries — resample next loop

                // ── 4. Store ──
                await conn.ExecuteAsync(
                    "INSERT INTO chat_voice (card_json, era_pack_id, created_utc) VALUES (@json, NULL, UTC_TIMESTAMP())",
                    new { json = card.ToJson() });
                var triple = TripleOf(card);
                tripleCounts[triple] = tripleCounts.GetValueOrDefault(triple) + 1;
                accepted++;

                if (accepted % 10 == 0)
                {
                    _logger.LogInformation("[CHAT-CAP] voice build: {Accepted}/{Target} (dedup-rej {Dedup}, parse-rej {Parse})",
                        accepted, target, rejectedDedup, rejectedParse);
                    await Publish(accepted, target, rejectedDedup, rejectedParse, started, null, null, running: true);
                }
            }

            var note = accepted >= target ? "complete" : $"stopped at attempt cap ({maxAttempts})";
            _logger.LogInformation("[CHAT-CAP] voice library build {Note}: {Accepted}/{Target}, dedup-rej {Dedup}, parse-rej {Parse}",
                note, accepted, target, rejectedDedup, rejectedParse);
            await Publish(accepted, target, rejectedDedup, rejectedParse, started, DateTime.UtcNow, null, running: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CHAT-CAP] voice library build failed");
            await Publish(accepted, target, rejectedDedup, rejectedParse, started, DateTime.UtcNow, ex.Message, running: false);
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }

    // ==================== Prose prompt + parse ====================

    private static (string System, string Prompt) BuildProsePrompt(VoiceTables.Skeleton s, string eraContext)
    {
        string system =
            "You write fictional 2005-era World of Warcraft player personas. You are given a fixed " +
            "skeleton (age, region, occupation category, interests, typing style) and write ONLY the " +
            "prose fields to fit it. Era: 2005 — flip phones, dial-up jokes, no smartphones, no " +
            "streaming, no post-2005 references of any kind. Output ONLY a JSON object, no markdown fences.";

        string era = string.IsNullOrWhiteSpace(eraContext) ? "" :
            $"\nActive era pack source (match its slang/references):\n{eraContext}\n";

        string prompt = $$"""
            Skeleton (fixed — do not contradict it):
            - age: {{s.Age}} ({{s.Region}})
            - occupation category: {{s.OccupationCategory}}
            - interests: {{string.Join(", ", s.Interests)}}
            - gaming background: {{s.GamingBackground}}
            - humor: {{s.Humor}}
            - typing: caps={{s.Typing.Caps}}, punctuation={{s.Typing.Punctuation}}, abbreviation level {{s.Typing.AbbrevLevel}}/3
            {{era}}
            Write JSON with EXACTLY these fields:
            {
              "given_name": "one plausible first name for this person",
              "occupation": "one specific sentence expanding the occupation category",
              "life_situation_seed": "one line about their current life situation",
              "opinions": ["two short opinions about WoW or life", "..."],
              "example_lines": ["EXACTLY five short chat lines this person would type in-game",
                                "each under 15 words, matching the typing style above",
                                "no emojis, era-correct, everyday chat not catchphrases", "...", "..."]
            }
            """;
        return (system, prompt);
    }

    private sealed class ProseResult
    {
        [JsonPropertyName("given_name")] public string GivenName { get; set; } = "";
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
        if (start < 0 || end <= start) return null;
        try
        {
            var r = JsonSerializer.Deserialize<ProseResult>(text[start..(end + 1)]);
            if (r == null) return null;
            if (string.IsNullOrWhiteSpace(r.GivenName) || string.IsNullOrWhiteSpace(r.Occupation)) return null;
            if (r.ExampleLines.Count != 5 || r.ExampleLines.Any(string.IsNullOrWhiteSpace)) return null;
            if (r.ExampleLines.Any(l => l.Length > 120)) return null;
            if (r.Opinions.Count < 1) return null;
            return r;
        }
        catch { return null; }
    }

    private static PersonaCard Materialize(VoiceTables.Skeleton s, ProseResult prose) => new()
    {
        V = 1,
        GivenName = prose.GivenName.Trim(),
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
        ExampleLines = prose.ExampleLines.Take(5).Select(l => l.Trim()).ToList()
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

    private async Task Publish(int accepted, int target, int dedup, int parse,
        DateTime started, DateTime? finished, string? error, bool running)
    {
        _status = new BuildStatus(running, accepted, target, dedup, parse, started, finished, error);
        try
        {
            await _hub.Clients.All.SendAsync("VoiceLibraryProgress",
                new { running, accepted, target, rejectedDedup = dedup, rejectedParse = parse, error });
        }
        catch { /* dashboard push is best-effort */ }
    }
}
