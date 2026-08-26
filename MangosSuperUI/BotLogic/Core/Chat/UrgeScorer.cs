using System.Collections.Concurrent;
using Dapper;
using MangosSuperUI.Models;
using MangosSuperUI.BotLogic.Chat.Core;
using MangosSuperUI.BotLogic.Tracking;

namespace MangosSuperUI.BotLogic.Chat.Coordinator;

/// <summary>Everything one hearer's urge computation needs (assembled by the coordinator).</summary>
public sealed record UrgeInputs(
    int BotGuid, bool Addressed, bool ThreadActive, float RelationshipStrength,
    float Proximity, double SecondsSinceSpoke, bool InCombat, bool IsDead, int ChainDepth);

/// <summary>
/// The §9.2 urge formula — every weight from chat_settings, verbatim:
///
///   urge = W_addr·addressed + W_thread·threadActive + W_rel·clamp01(strength/3)
///        + W_pers·(spontaneity·0.6 + chattiness·0.4) + W_prox·proximity + U(0, W_noise)
///        − cooldownPenalty − stateMod − ChainDepth·chain_penalty
///
/// spontaneity/chat_style come from the existing bot_personality row (§6.1 layer 2 —
/// consumed, never duplicated into the card), lazily cached per bot. chattiness maps
/// from chat_style via Chattiness.FromChatStyle (value set VERIFIED against
/// PersonalityRoller, the generator of those rows).
/// Consequences preserved: an addressed bot almost always clears threshold (W_addr 2.0
/// alone); strangers get replies only from high-spontaneity bots or lucky noise rolls.
/// </summary>
public class UrgeScorer
{
    private readonly ConnectionFactory _db;
    private readonly ChatSettingsService _settings;
    private readonly ILogger<UrgeScorer> _logger;
    private readonly ConcurrentDictionary<int, (float Spontaneity, float Chattiness)> _personality = new();

    public UrgeScorer(ConnectionFactory db, ChatSettingsService settings, ILogger<UrgeScorer> logger)
    {
        _db = db;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>Returns (urge, thresholdMet, breakdown-for-log). Zone-scoped settings resolve per hearer's zone.</summary>
    public (float Urge, bool Speaks, string Breakdown) Score(UrgeInputs x, int zoneId)
    {
        float wAddr = _settings.GetFloat(zoneId, "responsiveness.w_addr", 2.0f);
        float wThread = _settings.GetFloat(zoneId, "responsiveness.w_thread", 1.2f);
        float wRel = _settings.GetFloat(zoneId, "responsiveness.w_rel", 0.6f);
        float wPers = _settings.GetFloat(zoneId, "responsiveness.w_pers", 0.5f);
        float wProx = _settings.GetFloat(zoneId, "responsiveness.w_prox", 0.4f);
        float wNoise = _settings.GetFloat(zoneId, "noise.w_noise", 0.35f);
        float threshold = _settings.GetFloat(zoneId, "responsiveness.urge_threshold", 1.0f);
        float chainPen = _settings.GetFloat(zoneId, "noise.chain_penalty", 0.8f);
        int cooldownS = _settings.GetInt(zoneId, "responsiveness.bot_cooldown_s", 8);

        var (spont, chatty) = GetPersonality(x.BotGuid);
        float personality = spont * 0.6f + chatty * 0.4f;

        float noise = (float)(Random.Shared.NextDouble() * wNoise);
        float cooldown = x.SecondsSinceSpoke < cooldownS ? 1.0f : 0f;
        float stateMod = x.InCombat ? 0.6f : x.IsDead ? 0.3f : 0f;   // dead men still type sometimes
        float chain = x.ChainDepth * chainPen;

        float urge =
              wAddr * (x.Addressed ? 1 : 0)
            + wThread * (x.ThreadActive ? 1 : 0)
            + wRel * Math.Clamp(x.RelationshipStrength / 3.0f, 0f, 1f)
            + wPers * personality
            + wProx * x.Proximity
            + noise
            - cooldown
            - stateMod
            - chain;

        string breakdown =
            $"addr={(x.Addressed ? 1 : 0)}×{wAddr} thread={(x.ThreadActive ? 1 : 0)}×{wThread} " +
            $"rel={x.RelationshipStrength:0.00} pers={personality:0.00}×{wPers} prox={x.Proximity:0.00}×{wProx} " +
            $"noise={noise:0.00} cd=-{cooldown} state=-{stateMod} chain=-{chain:0.00} " +
            $"→ {urge:0.00} vs {threshold:0.00}";

        return (urge, urge >= threshold, breakdown);
    }

    private (float Spontaneity, float Chattiness) GetPersonality(int botGuid)
    {
        return _personality.GetOrAdd(botGuid, guid =>
        {
            try
            {
                using var conn = _db.Admin();
                var row = conn.QuerySingleOrDefault<PersonalityRow>(
                    "SELECT spontaneity AS Spontaneity, chat_style AS ChatStyle FROM bot_personality WHERE bot_guid=@guid",
                    new { guid });
                if (row != null)
                {
                    CircuitTrace.Hit(guid, "chat: personality row loaded for urge scoring");
                    return (row.Spontaneity, Chattiness.FromChatStyle(row.ChatStyle));
                }
            }
            catch (Exception ex)
            {
                CircuitTrace.Hit(guid, "chat: personality read failed, defaults used");
                _logger.LogWarning("[CHAT-COORD] bot_personality read failed for {Guid}: {Error}", guid, ex.Message);
            }
            return (0.5f, 0.5f);   // rolled-average defaults when the row is missing
        });
    }

    private sealed class PersonalityRow
    {
        public float Spontaneity { get; set; }
        public string ChatStyle { get; set; } = "casual";
    }
}
