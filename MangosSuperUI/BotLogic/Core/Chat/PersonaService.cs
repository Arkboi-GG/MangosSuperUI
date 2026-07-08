using Dapper;
using MangosSuperUI.Models;
using System.Collections.Concurrent;

namespace MangosSuperUI.BotLogic.Chat.Core;

/// <summary>A bot's live speaking self: card + mood + situation (+ Tier-3 narrative).</summary>
public sealed record BotPersona(int Guid, PersonaCard Card, float MoodValence,
                                float MoodEnergy, string Situation, string Narrative);

/// <summary>
/// Loads/creates bot_persona rows (CHAT_ARCHITECTURE §6). Creation is LAZY on first chat
/// involvement (D3 timing): C2's creation path is the three SeedPersonas cards assigned
/// round-robin — REPLACED in C6 by voice-library assignment + jitter, which slots in
/// behind this same GetOrCreateAsync without touching callers.
/// Rows are cached in-memory; LifeSim (C9) becomes the writer of mood/situation and will
/// invalidate through <see cref="Invalidate"/>.
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
            var card = PersonaCard.Parse(row.CardJson);
            if (card != null)
            {
                var loaded = new BotPersona(guid, card, row.MoodValence,
                    row.MoodEnergy, row.Situation, row.Narrative);
                _cache[guid] = loaded;
                return loaded;
            }
            _logger.LogWarning("[CHAT-ENGINE] bot_persona.card_json unparseable for guid={Guid} — reseeding", guid);
        }

        // First chat involvement → materialize a persona (C2: seed card, round-robin).
        var seed = SeedPersonas.Pick(guid);
        var narrative = SeedPersonas.OriginNarrative(seed, botName);
        await conn.ExecuteAsync(@"
            INSERT INTO bot_persona (guid, voice_id, card_json, mood_valence, mood_energy,
                                     situation, narrative, updated_utc)
            VALUES (@guid, NULL, @cardJson, 0, 0, '', @narrative, UTC_TIMESTAMP())
            ON DUPLICATE KEY UPDATE card_json=@cardJson, narrative=@narrative, updated_utc=UTC_TIMESTAMP()",
            new { guid, cardJson = seed.ToJson(), narrative });

        _logger.LogInformation("[CHAT-ENGINE] persona created for {BotName} (guid={Guid}): seed '{Given}' (C2 round-robin)",
            botName, guid, seed.GivenName);

        var created = new BotPersona(guid, seed, 0, 0, "", narrative);
        _cache[guid] = created;
        return created;
    }

    /// <summary>LifeSim (C9) and compaction (C8) call this after writing mood/situation/narrative.</summary>
    public void Invalidate(int guid) => _cache.TryRemove(guid, out _);

    private sealed class PersonaRow
    {
        public string CardJson { get; set; } = "";
        public float MoodValence { get; set; }
        public float MoodEnergy { get; set; }
        public string Situation { get; set; } = "";
        public string Narrative { get; set; } = "";
    }
}
