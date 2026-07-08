using Dapper;
using MangosSuperUI.Models;
using MangosSuperUI.BotLogic.Chat.Voice;
using System.Collections.Concurrent;

namespace MangosSuperUI.BotLogic.Chat.Core;

/// <summary>A bot's live speaking self: card + mood + situation (+ Tier-3 narrative).</summary>
public sealed record BotPersona(int Guid, PersonaCard Card, float MoodValence,
                                float MoodEnergy, string Situation, string Narrative);

/// <summary>
/// Loads/creates bot_persona rows (CHAT_ARCHITECTURE §6). Creation is LAZY on first chat
/// involvement (D3 timing). C6: creation assigns from the VOICE LIBRARY — the
/// least-assigned non-retired voice (uniform among ties) — then jitters per §6.4:
/// ±10% on numeric typing fields, ±0.1 on disposition floats, 20% chance to swap one
/// interest. The library copy never changes; the bot's card diverges freely from here.
/// Existing personas (including C2 seed-era ones, voice_id NULL) are left alone; the
/// Capacity tab's "Reroll seed personas" action reassigns those on demand.
/// Empty library → a generic fallback card + a loud log pointing at the build button.
/// </summary>
public class PersonaService
{
    private readonly ConnectionFactory _db;
    private readonly ILogger<PersonaService> _logger;
    private readonly ConcurrentDictionary<int, BotPersona> _cache = new();

    public PersonaService(ConnectionFactory db, ILogger<PersonaService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<BotPersona> GetOrCreateAsync(int guid, string botName)
    {
        if (_cache.TryGetValue(guid, out var cached))
            return cached;

        using var conn = _db.Admin();
        var row = await conn.QuerySingleOrDefaultAsync<PersonaRow>(@"
            SELECT card_json AS CardJson, mood_valence AS MoodValence, mood_energy AS MoodEnergy,
                   situation AS Situation, narrative AS Narrative
            FROM bot_persona WHERE guid=@guid", new { guid });

        if (row != null)
        {
            var existing = PersonaCard.Parse(row.CardJson);
            if (existing != null)
            {
                var loaded = new BotPersona(guid, existing, row.MoodValence,
                    row.MoodEnergy, row.Situation, row.Narrative);
                _cache[guid] = loaded;
                return loaded;
            }
            _logger.LogWarning("[CHAT-ENGINE] bot_persona.card_json unparseable for guid={Guid} — reassigning", guid);
        }

        // ── First chat involvement → assign from the library (§6.4) ──
        var (card, voiceId) = await AssignFromLibraryAsync(conn, guid);
        var narrative = OriginNarrative(card, botName);

        await conn.ExecuteAsync(@"
            INSERT INTO bot_persona (guid, voice_id, card_json, mood_valence, mood_energy,
                                     situation, narrative, updated_utc)
            VALUES (@guid, @voiceId, @cardJson, 0, 0, '', @narrative, UTC_TIMESTAMP())
            ON DUPLICATE KEY UPDATE voice_id=@voiceId, card_json=@cardJson, narrative=@narrative, updated_utc=UTC_TIMESTAMP()",
            new { guid, voiceId, cardJson = card.ToJson(), narrative });

        _logger.LogInformation("[CHAT-ENGINE] persona assigned for {BotName} (guid={Guid}): '{Given}', {Age} {Region}, voice_id={VoiceId}",
            botName, guid, card.GivenName, card.Age, card.Region, voiceId?.ToString() ?? "fallback");

        var created = new BotPersona(guid, card, 0, 0, "", narrative);
        _cache[guid] = created;
        return created;
    }

    /// <summary>
    /// One-shot admin action (C6 checklist — provided; Nico decides): reassign every
    /// persona created before the library existed (voice_id IS NULL) onto library
    /// voices. Their evolved cards are REPLACED; narratives reset. Returns count.
    /// </summary>
    public async Task<int> RerollSeedPersonasAsync()
    {
        using var conn = _db.Admin();
        var seedGuids = (await conn.QueryAsync<int>(
            "SELECT guid FROM bot_persona WHERE voice_id IS NULL")).ToList();

        foreach (var guid in seedGuids)
        {
            var (card, voiceId) = await AssignFromLibraryAsync(conn, guid);
            if (voiceId == null) break;   // library empty — nothing sensible to do
            await conn.ExecuteAsync(@"
                UPDATE bot_persona SET voice_id=@voiceId, card_json=@cardJson,
                       narrative=@narrative, updated_utc=UTC_TIMESTAMP()
                WHERE guid=@guid",
                new { guid, voiceId, cardJson = card.ToJson(), narrative = OriginNarrative(card, $"guid {guid}") });
            _cache.TryRemove(guid, out _);
            _logger.LogInformation("[CHAT-ENGINE] rerolled seed persona guid={Guid} → '{Given}' (voice {VoiceId})",
                guid, card.GivenName, voiceId);
        }
        return seedGuids.Count;
    }

    /// <summary>LifeSim (C9) and compaction (C8) call this after writing mood/situation/narrative.</summary>
    public void Invalidate(int guid) => _cache.TryRemove(guid, out _);

    // ==================== §6.4 assignment + jitter ====================

    private async Task<(PersonaCard Card, int? VoiceId)> AssignFromLibraryAsync(
        MySqlConnector.MySqlConnection conn, int guid)
    {
        // Least-assigned non-retired voice, uniform among ties.
        var pick = await conn.QuerySingleOrDefaultAsync<VoiceRow>(@"
            SELECT v.id AS Id, v.card_json AS CardJson
            FROM chat_voice v
            LEFT JOIN bot_persona p ON p.voice_id = v.id
            WHERE v.retired = 0
            GROUP BY v.id, v.card_json
            ORDER BY COUNT(p.guid) ASC, RAND()
            LIMIT 1");

        var card = pick != null ? PersonaCard.Parse(pick.CardJson) : null;
        if (card == null)
        {
            _logger.LogWarning("[CHAT-ENGINE] voice library EMPTY (or unparseable) — using generic fallback. " +
                               "Build the library on the Chat Capacity page.");
            return (FallbackCard(guid), null);
        }

        Jitter(card);
        return (card, pick!.Id);
    }

    /// <summary>§6.4: ±10% numeric typing fields, ±0.1 disposition floats, 20% interest swap.</summary>
    private static void Jitter(PersonaCard card)
    {
        var rng = Random.Shared;
        static float J10(Random r, float v) => v * (0.9f + (float)r.NextDouble() * 0.2f);
        static float JDisp(Random r, float v) => Math.Clamp(v + ((float)r.NextDouble() * 0.2f - 0.1f), 0f, 1f);

        var t = card.Typing;
        t.Wpm = Math.Max(15, (int)J10(rng, t.Wpm));
        t.TypoRate = Math.Clamp(J10(rng, t.TypoRate), 0f, 0.15f);
        t.ThinkMinS = Math.Max(1, (int)Math.Round(J10(rng, t.ThinkMinS)));
        t.ThinkMaxS = Math.Max(t.ThinkMinS + 1, (int)Math.Round(J10(rng, t.ThinkMaxS)));
        t.SplitThresholdChars = Math.Max(40, (int)J10(rng, t.SplitThresholdChars));
        t.AltTabChance = Math.Clamp(J10(rng, t.AltTabChance), 0f, 0.3f);

        var d = card.Disposition;
        d.Warmth = JDisp(rng, d.Warmth);
        d.Irritability = JDisp(rng, d.Irritability);
        d.Confidence = JDisp(rng, d.Confidence);
        d.Openness = JDisp(rng, d.Openness);

        if (card.Interests.Count > 0 && rng.NextDouble() < 0.20)
        {
            card.Interests.RemoveAt(rng.Next(card.Interests.Count));
            var add = VoiceTables.RandomInterest(rng);
            if (!card.Interests.Contains(add)) card.Interests.Add(add);
        }
    }

    private static string OriginNarrative(PersonaCard c, string botName) =>
        $"{c.GivenName} is {c.Age}, from {c.Region}. {c.Occupation}; {c.LifeSituationSeed}. " +
        $"{c.GamingBackground}. Plays a character named {botName} and mostly keeps to " +
        $"{string.Join(", ", c.Interests.Take(2))} talk when not playing.";

    /// <summary>Empty-library fallback — deliberately bland so operators notice and build.</summary>
    private static PersonaCard FallbackCard(int guid) => new()
    {
        GivenName = "Sam",
        Age = 21,
        Region = "US-Midwest",
        TimezoneOffset = -6,
        Occupation = "college student",
        LifeSituationSeed = "between classes most days",
        Interests = new() { "video games", "music" },
        GamingBackground = "first MMO",
        Opinions = new() { "still figuring the game out" },
        Typing = new PersonaTyping(),
        ExampleLines = new()
        {
            "hey", "lol nice", "anyone know where to go for quests", "brb", "gg"
        }
    };

    private sealed class PersonaRow
    {
        public string CardJson { get; set; } = "";
        public float MoodValence { get; set; }
        public float MoodEnergy { get; set; }
        public string Situation { get; set; } = "";
        public string Narrative { get; set; } = "";
    }

    private sealed class VoiceRow
    {
        public int Id { get; set; }
        public string CardJson { get; set; } = "";
    }
}