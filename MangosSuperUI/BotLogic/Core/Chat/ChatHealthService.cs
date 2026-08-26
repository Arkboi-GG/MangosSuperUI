using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using MangosSuperUI.Models;
using MangosSuperUI.BotLogic.Chat.Core;
using MangosSuperUI.BotLogic.Chat.Engine;
using MangosSuperUI.BotLogic.Chat.Voice;
using MangosSuperUI.BotLogic.Tracking;

namespace MangosSuperUI.BotLogic.Chat.Health;

/// <summary>
/// CHAT_ARCHITECTURE §14.3 amendment (2026-07-13) — everything an operator needs to
/// diagnose and repair the chat layer, computed server-side and rendered on the Capacity
/// page. NOTHING here should ever require a SQL client.
///
/// This exists because the repetition bug of 2026-07-13 was invisible from the UI. The
/// Capacity page cheerfully said "Voice library: 0/300 voices" — technically true, and it
/// never occurred to anyone that 0 voices meant all 25 bots were sharing one hardcoded
/// fallback card and parroting its three few-shot lines. The numbers that would have
/// screamed (distinct given names, distinct example lines, opening-bigram spread, the
/// out-line duplication rate) were only reachable by hand-writing queries. That is a
/// product defect, not an operator skill issue.
///
/// Three surfaces:
///   • LibraryHealth  — is the voice library any good? Runs TODAY'S shape guards over the
///                      cards already in it, so a library built by a weak batch model is
///                      visibly bad instead of silently bad.
///   • ChatHealth     — is the fleet actually repeating itself right now? Straight off
///                      chat_log, which already records every out-line.
///   • Preflight      — can a build even succeed? Probes the endpoint, resolves which
///                      model the Batch lane will really use, and blocks the button when
///                      the answer is "none".
///
/// Also owns the destructive library ops (retire / detach personas), because "just run
/// UPDATE chat_voice SET retired=1" is not a feature.
/// </summary>
public class ChatHealthService
{
    private readonly ConnectionFactory _db;
    private readonly ChatSettingsService _settings;
    private readonly ILogger<ChatHealthService> _logger;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    public ChatHealthService(ConnectionFactory db, ChatSettingsService settings, ILogger<ChatHealthService> logger)
    {
        _db = db;
        _settings = settings;
        _logger = logger;
    }

    // ==================== DTOs ====================

    /// <summary>status ∈ pass | warn | fail. 'fail' on a preflight check blocks the build.</summary>
    public sealed record Check(string Id, string Label, string Status, string Detail);

    public sealed record Counted(string Key, int Count);

    public sealed record LibraryHealth(
        int Voices, int SchemaCurrent, int SchemaOld,
        int DistinctNames, int DistinctOccupations,
        int ExampleTotal, int ExampleDistinct,
        int ShapeViolations,
        IReadOnlyList<Counted> TopNames,
        IReadOnlyList<Counted> TopOpeningBigrams,
        IReadOnlyList<Counted> DuplicateLines,
        IReadOnlyList<Counted> SwearLevels,
        IReadOnlyList<Counted> CapsStyles,
        IReadOnlyList<Counted> ShapeReasons,
        IReadOnlyList<Check> Checks,
        IReadOnlyList<string> SampleCards);

    public sealed record ChatHealth(
        int OutLines, int DistinctLines, int Bots,
        IReadOnlyList<Counted> TopRepeated,
        IReadOnlyList<Counted> TopOpeningBigrams,
        IReadOnlyList<Counted> Discards,
        IReadOnlyList<Check> Checks);

    public sealed record Preflight(
        bool CanBuild, string ProfileName, string Endpoint, string Flavor,
        string EffectiveBatchModel, bool UsingReactiveFallback,
        int ExistingVoices, int OldSchemaVoices, int Target, int PersonasOnOldCards,
        IReadOnlyList<string> AvailableModels,
        IReadOnlyList<Check> Checks);

    // ==================== Library health ====================

    public async Task<LibraryHealth> GetLibraryHealthAsync()
    {
        using var conn = _db.Admin();
        var json = (await conn.QueryAsync<string>(
            "SELECT card_json FROM chat_voice WHERE retired=0")).ToList();

        var cards = json.Select(PersonaCard.Parse).Where(c => c != null).Select(c => c!).ToList();
        int voices = cards.Count;

        int schemaCurrent = cards.Count(c => c.V >= 2);
        int schemaOld = voices - schemaCurrent;

        var names = cards.Select(c => c.GivenName).ToList();
        var occupations = cards.Select(c => Head(c.Occupation)).ToList();
        var lines = cards.SelectMany(c => c.ExampleLines).Select(l => l.Trim().ToLowerInvariant()).ToList();

        var shapeReasons = new Dictionary<string, int>();
        foreach (var c in cards)
        {
            var bad = VoiceLibraryBuilder.ShapeViolation(c);
            if (bad != null) { CircuitTrace.HitNote(0, "chat: library card shape violation counted", bad); shapeReasons[bad] = shapeReasons.GetValueOrDefault(bad) + 1; }
        }
        int shapeViolations = shapeReasons.Values.Sum();

        int exampleTotal = lines.Count;
        int exampleDistinct = lines.Distinct().Count();
        int distinctNames = names.Distinct(StringComparer.OrdinalIgnoreCase).Count();

        var checks = new List<Check>();

        if (voices == 0)
        {
            CircuitTrace.Hit(0, "chat: library health, library empty");
            checks.Add(new("empty", "Library is empty", "fail",
                "Every bot falls back to one generic card, and the fleet will parrot its example lines. " +
                "This is the failure mode that produced the 2026-07-13 repetition bug. Build the library."));
        }
        else
        {
            CircuitTrace.Hit(0, "chat: library health checks computed", voices);
            int target = _settings.GetInt(0, "voice.library_target", 300);
            checks.Add(voices >= target
                ? new("size", "Library size", "pass", $"{voices} voices (target {target})")
                : new("size", "Library size", "warn", $"{voices} of {target} — build did not finish"));

            // Name concentration — the "48 Dereks" check.
            var topName = Top(names, 1).FirstOrDefault();
            double nameShare = topName == null || voices == 0 ? 0 : (double)topName.Count / voices;
            checks.Add(nameShare > 0.06
                ? new("names", "Name spread", "fail",
                    $"{distinctNames} distinct names; '{topName!.Key}' is {topName.Count} cards ({nameShare:P0}). " +
                    "A name should be a skeleton axis, not an LLM choice — rebuild on the current code.")
                : new("names", "Name spread", "pass", $"{distinctNames} distinct names across {voices} cards"));

            // Example-line uniqueness — the anchors are what the reactive model imitates.
            double dupRate = exampleTotal == 0 ? 0 : 1.0 - (double)exampleDistinct / exampleTotal;
            checks.Add(dupRate switch
            {
                > 0.10 => CircuitTrace.Pass(new Check("lines", "Example-line uniqueness", "fail",
                    $"{exampleDistinct}/{exampleTotal} distinct ({dupRate:P1} duplicated) — the batch model is recycling phrasings"), 0, "chat: library line uniqueness fail"),
                > 0.03 => CircuitTrace.Pass(new Check("lines", "Example-line uniqueness", "warn",
                    $"{exampleDistinct}/{exampleTotal} distinct ({dupRate:P1} duplicated)"), 0, "chat: library line uniqueness warn"),
                _ => CircuitTrace.Pass(new Check("lines", "Example-line uniqueness", "pass",
                    $"{exampleDistinct}/{exampleTotal} distinct"), 0, "chat: library line uniqueness pass")
            });

            // Shape contract — would today's guards accept the cards already in the library?
            double shapeRate = voices == 0 ? 0 : (double)shapeViolations / voices;
            checks.Add(shapeRate switch
            {
                > 0.15 => CircuitTrace.Pass(new Check("shape", "Shape contract", "fail",
                    $"{shapeViolations} of {voices} cards would be REJECTED by today's guards " +
                    $"({string.Join(", ", shapeReasons.OrderByDescending(k => k.Value).Take(3).Select(k => $"{k.Key} {k.Value}"))}) — rebuild"), 0, "chat: library shape contract fail"),
                > 0.02 => CircuitTrace.Pass(new Check("shape", "Shape contract", "warn",
                    $"{shapeViolations} of {voices} cards violate the current shape guards"), 0, "chat: library shape contract warn"),
                _ => CircuitTrace.Pass(new Check("shape", "Shape contract", "pass", "cards satisfy the current guards"), 0, "chat: library shape contract pass")
            });

            if (schemaOld > 0)
            {
                CircuitTrace.Hit(0, "chat: library has old-schema cards", schemaOld);
                checks.Add(new("schema", "Card schema", "warn",
                    $"{schemaOld} cards predate schema v2 (no swear_level) — they default to level 1. Rebuild to fix."));
            }
            else
            {
                CircuitTrace.Hit(0, "chat: library all schema v2");
                checks.Add(new("schema", "Card schema", "pass", $"all {voices} cards on schema v2"));
            }

            // Occupation spread is the canary for the stratified sampler doing its job.
            int distinctOcc = occupations.Distinct(StringComparer.OrdinalIgnoreCase).Count();
            checks.Add(distinctOcc < voices / 3
                ? new("occ", "Occupation spread", "warn", $"{distinctOcc} distinct occupations across {voices} cards")
                : new("occ", "Occupation spread", "pass", $"{distinctOcc} distinct occupations"));
        }

        var swearDist = cards
            .GroupBy(c => c.Typing.SwearLevel)
            .OrderBy(g => g.Key)
            .Select(g => new Counted($"level {g.Key}", g.Count()))
            .ToList();

        var capsDist = cards
            .GroupBy(c => string.IsNullOrWhiteSpace(c.Typing.Caps) ? "(unset)" : c.Typing.Caps)
            .OrderByDescending(g => g.Count())
            .Select(g => new Counted(g.Key, g.Count()))
            .ToList();

        return new LibraryHealth(
            voices, schemaCurrent, schemaOld,
            distinctNames,
            occupations.Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            exampleTotal, exampleDistinct, shapeViolations,
            Top(names, 8),
            Top(lines.Select(OpeningBigram).Where(b => b.Length > 0), 12),
            Top(lines, 8).Where(c => c.Count > 1).ToList(),
            swearDist, capsDist,
            shapeReasons.OrderByDescending(k => k.Value).Select(k => new Counted(k.Key, k.Value)).ToList(),
            checks,
            SampleCards(cards, 3));
    }

    /// <summary>Three random cards rendered for eyeballing — the check no metric replaces.</summary>
    private static List<string> SampleCards(List<PersonaCard> cards, int n)
    {
        var rng = Random.Shared;
        return cards.OrderBy(_ => rng.Next()).Take(n).Select(c =>
        {
            var t = c.Typing;
            var head = $"{c.GivenName} · {c.Age} · {c.Region} · {c.Occupation}\n" +
                       $"caps={t.Caps} abbrev={t.AbbrevLevel} swear={t.SwearLevel} tics=[{string.Join(", ", t.Tics)}]";
            var body = string.Join("\n", c.ExampleLines.Select(l => "   | " + l));
            return head + "\n" + body;
        }).ToList();
    }

    // ==================== Live chat health ====================

    public async Task<ChatHealth> GetChatHealthAsync(int days = 7)
    {
        using var conn = _db.Admin();

        var rows = (await conn.QueryAsync<(int BotGuid, string Message)>(@"
            SELECT bot_guid, message FROM chat_log
            WHERE direction='out' AND utc > UTC_TIMESTAMP() - INTERVAL @days DAY
            ORDER BY utc DESC LIMIT 5000", new { days })).ToList();

        var messages = rows.Select(r => (r.Message ?? "").Trim().ToLowerInvariant())
                           .Where(m => m.Length > 0).ToList();

        int total = messages.Count;
        int distinct = messages.Distinct().Count();
        int bots = rows.Select(r => r.BotGuid).Distinct().Count();

        var checks = new List<Check>();

        if (total < 20)
        {
            CircuitTrace.Hit(0, "chat: chat health thin sample", total);
            checks.Add(new("volume", "Sample size", "warn",
                $"only {total} out-lines in the last {days} days — talk to some bots, then re-check"));
        }
        else
        {
            CircuitTrace.Hit(0, "chat: chat health checks computed", total);
            double dupRate = 1.0 - (double)distinct / total;
            checks.Add(dupRate switch
            {
                > 0.15 => CircuitTrace.Pass(new Check("dupes", "Exact duplication", "fail",
                    $"{distinct}/{total} distinct — {dupRate:P0} of lines are verbatim repeats"), 0, "chat: duplication check fail"),
                > 0.05 => CircuitTrace.Pass(new Check("dupes", "Exact duplication", "warn", $"{distinct}/{total} distinct ({dupRate:P0} repeats)"), 0, "chat: duplication check warn"),
                _ => CircuitTrace.Pass(new Check("dupes", "Exact duplication", "pass", $"{distinct}/{total} distinct"), 0, "chat: duplication check pass")
            });

            // The one that actually caught the bug: exact-dupe rate looked FINE (74/68) while
            // every line still opened the same way. Register collapse hides from uniq -c.
            var bigrams = Top(messages.Select(OpeningBigram).Where(b => b.Length > 0), 1);
            var topBigram = bigrams.FirstOrDefault();
            double bigramShare = topBigram == null || total == 0 ? 0 : (double)topBigram.Count / total;
            checks.Add(bigramShare switch
            {
                > 0.15 => CircuitTrace.Pass(new Check("register", "Opening variety", "fail",
                    $"{bigramShare:P0} of all lines open with \"{topBigram!.Key}\" — register collapse, " +
                    "not exact duplication. Check the persona few-shot anchors."), 0, "chat: opening variety fail"),
                > 0.08 => CircuitTrace.Pass(new Check("register", "Opening variety", "warn",
                    $"{bigramShare:P0} of lines open with \"{topBigram!.Key}\""), 0, "chat: opening variety warn"),
                _ => CircuitTrace.Pass(new Check("register", "Opening variety", "pass", "no single opener dominates"), 0, "chat: opening variety pass")
            });

            checks.Add(bots < 5
                ? new("bots", "Speaking bots", "warn", $"{bots} bots have spoken — a thin sample")
                : new("bots", "Speaking bots", "pass", $"{bots} bots have spoken"));
        }

        var discards = StylePostPass.DiscardSnapshot()
            .OrderByDescending(k => k.Value)
            .Select(k => new Counted(k.Key, k.Value))
            .ToList();

        return new ChatHealth(
            total, distinct, bots,
            Top(messages, 10).Where(c => c.Count > 1).ToList(),
            Top(messages.Select(OpeningBigram).Where(b => b.Length > 0), 12),
            discards,
            checks);
    }

    // ==================== Build preflight ====================

    public async Task<Preflight> GetBuildPreflightAsync()
    {
        using var conn = _db.Admin();
        var checks = new List<Check>();

        var p = await conn.QuerySingleOrDefaultAsync<ProfileRow>(@"
            SELECT name AS Name, endpoint_url AS Endpoint, api_flavor AS ApiFlavor,
                   model_reactive AS ModelReactive, model_batch AS ModelBatch
            FROM chat_inference_profile WHERE active=1 LIMIT 1");

        int target = _settings.GetInt(0, "voice.library_target", 300);
        int voices = await conn.QuerySingleAsync<int>("SELECT COUNT(*) FROM chat_voice WHERE retired=0");
        var oldSchema = (await conn.QueryAsync<string>("SELECT card_json FROM chat_voice WHERE retired=0"))
            .Select(PersonaCard.Parse).Count(c => c == null || c.V < 2);
        int personasOnOld = await conn.QuerySingleAsync<int>("SELECT COUNT(*) FROM bot_persona WHERE voice_id IS NULL");

        if (p == null)
        {
            CircuitTrace.Hit(0, "chat: preflight fail, no active profile");
            checks.Add(new("profile", "Active inference profile", "fail",
                "No profile is active. Activate one in the table above."));
            return new Preflight(false, "(none)", "", "", "", false, voices, oldSchema, target, personasOnOld,
                Array.Empty<string>(), checks);
        }

        checks.Add(new("profile", "Active inference profile", "pass", $"'{p.Name}' → {p.Endpoint}"));

        var flavor = string.IsNullOrWhiteSpace(p.ApiFlavor) ? "ollama" : p.ApiFlavor.Trim().ToLowerInvariant();
        var models = await ProbeModelsAsync(p.Endpoint, flavor);

        if (models.Count == 0)
        {
            CircuitTrace.Hit(0, "chat: preflight endpoint unreachable");
            checks.Add(new("endpoint", "Endpoint reachable", "fail",
                $"No models returned from {p.Endpoint}. Is it up, and is the API flavor right?"));
        }
        else
        {
            CircuitTrace.Hit(0, "chat: preflight endpoint ok", models.Count);
            checks.Add(new("endpoint", "Endpoint reachable", "pass", $"{models.Count} models served"));
        }

        bool fallback = string.IsNullOrWhiteSpace(p.ModelBatch);
        var effective = fallback ? p.ModelReactive : p.ModelBatch;

        if (string.IsNullOrWhiteSpace(effective))
        {
            CircuitTrace.Hit(0, "chat: preflight no batch model at all");
            checks.Add(new("model", "Batch model", "fail",
                "Neither model_batch nor model_reactive is set on this profile."));
        }
        else if (fallback)
        {
            CircuitTrace.Hit(0, "chat: preflight batch falls back to reactive model");
            checks.Add(new("model", "Batch model", "warn",
                $"model_batch is empty → the build will run on the REACTIVE model '{effective}'. " +
                "The library is written once and every bot on the server descends from it. " +
                "If you have a bigger model, put it in the Batch column first."));
        }
        else
        {
            CircuitTrace.Hit(0, "chat: preflight batch model set");
            checks.Add(new("model", "Batch model", "pass", $"'{effective}'"));
        }

        if (!string.IsNullOrWhiteSpace(effective) && models.Count > 0 &&
            !models.Any(m => m.Equals(effective, StringComparison.OrdinalIgnoreCase)))
        {
            CircuitTrace.Hit(0, "chat: preflight model tag missing at endpoint");
            checks.Add(new("modeltag", "Model tag exists", "fail",
                $"'{effective}' is not served at this endpoint. Pick one of: {string.Join(", ", models.Take(6))}"));
        }
        else if (models.Count > 0)
        {
            CircuitTrace.Hit(0, "chat: preflight model tag found");
            checks.Add(new("modeltag", "Model tag exists", "pass", "tag found at the endpoint"));
        }

        if (voices > 0)
        {
            CircuitTrace.Hit(0, "chat: preflight existing library present", voices);
            checks.Add(new("existing", "Existing library", "warn",
                $"{voices} voices already present. A build is RESUMABLE — it tops up to the target and leaves " +
                $"these alone{(oldSchema > 0 ? $", including {oldSchema} on the old schema" : "")}. " +
                "Use \"Rebuild from scratch\" if you want them replaced."));
        }
        else
        {
            CircuitTrace.Hit(0, "chat: preflight clean build, empty library");
            checks.Add(new("existing", "Existing library", "pass", "empty — a clean build"));
        }

        bool canBuild = !checks.Any(c => c.Status == "fail");

        return new Preflight(canBuild, p.Name, p.Endpoint, flavor, effective ?? "", fallback,
            voices, oldSchema, target, personasOnOld, models, checks);
    }

    /// <summary>Ask the endpoint what it actually serves. Populates the model dropdowns.</summary>
    public async Task<List<string>> ProbeModelsAsync(string endpoint, string flavor)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) { CircuitTrace.Hit(0, "chat: model probe skipped, empty endpoint"); return new(); }
        try
        {
            var baseUrl = endpoint.TrimEnd('/');
            if (flavor == "openai")
            {
                CircuitTrace.Hit(0, "chat: model probe via openai /v1/models");
                if (baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) baseUrl = baseUrl[..^3];   // cb:fold url normalization detail, probe outcome carried by flavor probes
                var json = await Http.GetStringAsync($"{baseUrl}/v1/models");
                var list = JsonSerializer.Deserialize<OpenAiModelList>(json);
                return list?.Data?.Select(d => d.Id).Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? new();
            }
            else
            {
                CircuitTrace.Hit(0, "chat: model probe via ollama /api/tags");
                var json = await Http.GetStringAsync($"{baseUrl}/api/tags");
                var list = JsonSerializer.Deserialize<OllamaTagList>(json);
                return list?.Models?.Select(m => m.Name).Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? new();
            }
        }
        catch (Exception ex)
        {
            CircuitTrace.Hit(0, "chat: model probe failed");
            _logger.LogWarning("[CHAT-CAP] model probe failed for {Endpoint} ({Flavor}): {Error}",
                endpoint, flavor, ex.Message);
            return new();
        }
    }

    // ==================== Destructive ops (so nobody opens a SQL client) ====================

    /// <summary>Retire every voice. Cards stay in the table (retired=1) — recoverable, and
    /// bot_persona.voice_id foreign keys don't dangle.</summary>
    public async Task<int> RetireLibraryAsync()
    {
        using var conn = _db.Admin();
        int n = await conn.ExecuteAsync("UPDATE chat_voice SET retired=1 WHERE retired=0");
        _logger.LogWarning("[CHAT-CAP] retired {Count} voices — the library is now empty", n);
        return n;
    }

    /// <summary>Detach every persona from its voice so the reroll action picks them all up.</summary>
    public async Task<int> DetachAllPersonasAsync()
    {
        using var conn = _db.Admin();
        int n = await conn.ExecuteAsync("UPDATE bot_persona SET voice_id=NULL");
        _logger.LogWarning("[CHAT-CAP] detached {Count} personas from their voices — they will be reassigned", n);
        return n;
    }

    // ==================== helpers ====================

    private static string Head(string s) =>
        (s ?? "").Split(',')[0].Trim().ToLowerInvariant();

    private static string OpeningBigram(string line)
    {
        var w = (line ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (w.Length == 0) return "";   // cb:fold pure text helper, no guid in reach
        return w.Length == 1 ? w[0] : $"{w[0]} {w[1]}";
    }

    private static List<Counted> Top(IEnumerable<string> items, int n) =>
        items.Where(s => !string.IsNullOrWhiteSpace(s))
             .GroupBy(s => s, StringComparer.OrdinalIgnoreCase)
             .OrderByDescending(g => g.Count())
             .ThenBy(g => g.Key)
             .Take(n)
             .Select(g => new Counted(g.Key, g.Count()))
             .ToList();

    private sealed class ProfileRow
    {
        public string Name { get; set; } = "";
        public string Endpoint { get; set; } = "";
        public string ApiFlavor { get; set; } = "ollama";
        public string ModelReactive { get; set; } = "";
        public string ModelBatch { get; set; } = "";
    }

    private sealed class OllamaTagList
    {
        [JsonPropertyName("models")] public List<OllamaTag>? Models { get; set; }
    }

    private sealed class OllamaTag
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
    }

    private sealed class OpenAiModelList
    {
        [JsonPropertyName("data")] public List<OpenAiModel>? Data { get; set; }
    }

    private sealed class OpenAiModel
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
    }
}
