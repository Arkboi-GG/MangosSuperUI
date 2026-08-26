using System.Collections.Concurrent;
using System.Text;
using Dapper;
using MangosSuperUI.Models;
using MangosSuperUI.BotLogic.Chat.Core;
using MangosSuperUI.BotLogic.Tracking;

namespace MangosSuperUI.BotLogic.Chat.Memory;

/// <summary>
/// Tier-1 verbatim memory + Tier-2 relationship bumps (CHAT_ARCHITECTURE §7.2–§7.3, C3).
///
/// IDENTITY-BLIND (D3): the counterpart NAME is the memory key; no roster consult
/// happens anywhere in this file — a bot remembers "Thudgar", not "bot #123".
///
/// Write path is BUFFERED: callers enqueue, the coordinator's housekeeping flushes every
/// 5 s as multi-row INSERTs — never per-line round trips at fleet scale (§16 C3).
/// Relationship bumps aggregate per flush; strength recomputes in SQL with the §7.3
/// formula (recencyFactor = 1 at bump time since daysSinceLast = 0; the C8 compactor
/// recomputes with real decay).
///
/// Salience (§7.2): out-lines 1; addressed-to-bot 2; in-thread 1; overheard only via
/// name-mention (2) or question + overhear_log_chance roll (1); FIRST-EVER meeting 3.
/// (Other salience-3 "memorable" classes — death talk, loot brags — are compactor-era
/// judgments, C8.) Hourly valve: ≤ memory.t1_lines_per_bot_hour rows/bot/hour, dropping
/// salience ≤1 overheard writes first; participated lines always log.
/// </summary>
public class ChatMemoryStore
{
    private sealed record PendingLog(DateTime Utc, int BotGuid, string Counterpart,
        uint CounterpartGuid, string Direction, string Kind, string ChannelName,
        int ZoneId, string Message, int Salience, bool Participated);

    private readonly ConnectionFactory _db;
    private readonly ChatSettingsService _settings;
    private readonly ILogger<ChatMemoryStore> _logger;

    private readonly ConcurrentQueue<PendingLog> _buffer = new();

    // Hourly valve: (botGuid, hourBucket) → rows written this hour
    private readonly ConcurrentDictionary<(int Bot, long Hour), int> _hourCounts = new();

    // Known pairs (for first-meeting salience 3) + max-salience-seen (for the strength term)
    private readonly ConcurrentDictionary<(int Bot, string Cp), byte> _knownPairs = new();
    private readonly ConcurrentDictionary<(int Bot, string Cp), int> _maxSalience = new();

    public ChatMemoryStore(ConnectionFactory db, ChatSettingsService settings, ILogger<ChatMemoryStore> logger)
    {
        _db = db;
        _settings = settings;
        _logger = logger;
    }

    // ==================== Enqueue API (called from the coordinator pipeline) ====================

    /// <summary>Incoming line the bot PARTICIPATES in (§9.3 pipeline point, direction=in).</summary>
    public void LogParticipatedIn(int botGuid, string counterpart, uint counterpartGuid,
        ChatKind kind, string channelName, int zoneId, string message, bool addressed)
    {
        int salience = IsFirstMeeting(botGuid, counterpart) ? 3 : (addressed ? 2 : 1);
        Enqueue(new PendingLog(DateTime.UtcNow, botGuid, Norm(counterpart), counterpartGuid,
            "in", KindStr(kind), channelName, zoneId, message, salience, Participated: true));
    }

    /// <summary>The bot's own outgoing line (§9.3 pipeline point, direction=out). Always salience 1.</summary>
    public void LogOut(int botGuid, string counterpart, uint counterpartGuid,
        ChatKind kind, string channelName, int zoneId, string message)
    {
        Enqueue(new PendingLog(DateTime.UtcNow, botGuid, Norm(counterpart), counterpartGuid,
            "out", KindStr(kind), channelName, zoneId, message, Salience: 1, Participated: true));
    }

    /// <summary>
    /// Overheard say/party/channel the bot is NOT involved in (§7.2 row 4). Coded fully
    /// now per the C3 checklist; call sites arrive with the loud channels in C4.
    /// </summary>
    public void LogOverheard(int botGuid, string counterpart, uint counterpartGuid,
        ChatKind kind, string channelName, int zoneId, string message,
        bool mentionsBotName, bool isQuestion)
    {
        int salience;
        if (mentionsBotName) { CircuitTrace.Hit(botGuid, "chat: overheard name-mention, salience 2"); salience = 2; }
        else if (isQuestion && Random.Shared.NextDouble() < _settings.GetFloat(0, "memory.overhear_log_chance", 0.15f)) { CircuitTrace.Hit(botGuid, "chat: overheard question logged on roll"); salience = 1; }
        else { CircuitTrace.Hit(botGuid, "chat: overheard not memorable, dropped"); return; }   // not memorable — never buffered

        Enqueue(new PendingLog(DateTime.UtcNow, botGuid, Norm(counterpart), counterpartGuid,
            "in", KindStr(kind), channelName, zoneId, message, salience, Participated: false));
    }

    private void Enqueue(PendingLog log)
    {
        // Hourly valve (§7.2): the growth cap at fleet scale. Participated lines always
        // pass (bounded by real conversation volume); overheard salience ≤1 drops first
        // at the cap; overheard salience 2 survives until 1.5× cap.
        int cap = Math.Max(10, _settings.GetInt(0, "memory.t1_lines_per_bot_hour", 120));
        long hour = DateTime.UtcNow.Ticks / TimeSpan.TicksPerHour;
        int count = _hourCounts.AddOrUpdate((log.BotGuid, hour), 1, (_, c) => c + 1);

        if (!log.Participated)
        {
            CircuitTrace.Hit(log.BotGuid, "chat: overheard write hits hourly valve check", count);
            if ((count > cap && log.Salience <= 1) || count > cap * 3 / 2)
            {
                CircuitTrace.Hit(log.BotGuid, "chat: hourly valve dropped overheard write", count);
                _logger.LogDebug("[CHAT-MEM] valve drop bot={Bot} sal={Sal} ({Count}/{Cap} this hour)",
                    log.BotGuid, log.Salience, count, cap);
                return;
            }
        }
        else if (count == cap + 1)
        {
            CircuitTrace.Hit(log.BotGuid, "chat: tier1 hourly cap reached", cap);
            _logger.LogInformation("[CHAT-MEM] Tier-1 cap hit for bot={Bot} ({Cap}/hr) — overheard writes now dropping",
                log.BotGuid, cap);
        }

        _buffer.Enqueue(log);
    }

    // ==================== Flush (coordinator housekeeping, every 5 s) ====================

    public async Task FlushAsync()
    {
        if (_buffer.IsEmpty) { CircuitTrace.Hit(0, "chat: memory flush skipped, buffer empty"); return; }

        var rows = new List<PendingLog>();
        while (rows.Count < 500 && _buffer.TryDequeue(out var r)) rows.Add(r);
        if (rows.Count == 0) { CircuitTrace.Hit(0, "chat: memory flush found no rows"); return; }

        try
        {
            using var conn = _db.Admin();

            // ── One multi-row INSERT (never per-line round trips) ──
            var sql = new StringBuilder(
                "INSERT INTO chat_log (utc, bot_guid, counterpart_name, counterpart_guid, direction, kind, channel_name, zone_id, message, salience) VALUES ");
            var p = new DynamicParameters();
            for (int i = 0; i < rows.Count; i++)
            {
                if (i > 0) sql.Append(',');   // cb:fold sql text build detail, flush outcome probed below
                sql.Append($"(@u{i},@b{i},@c{i},@g{i},@d{i},@k{i},@ch{i},@z{i},@m{i},@s{i})");
                var r = rows[i];
                p.Add($"u{i}", r.Utc); p.Add($"b{i}", r.BotGuid); p.Add($"c{i}", r.Counterpart);
                p.Add($"g{i}", r.CounterpartGuid); p.Add($"d{i}", r.Direction); p.Add($"k{i}", r.Kind);
                p.Add($"ch{i}", r.ChannelName); p.Add($"z{i}", r.ZoneId); p.Add($"m{i}", r.Message);
                p.Add($"s{i}", r.Salience);
            }
            await conn.ExecuteAsync(sql.ToString(), p);

            // ── Relationship bumps: aggregate participated rows per (bot, counterpart) ──
            // §7.3: interact_count++, last_interact_utc=now on each participated write;
            // strength = ln(1+count) * recency(=1 at bump) * (1 + 0.25 * maxSalienceSeen).
            // MySQL ON DUPLICATE assignments apply left→right, so `strength` reads the
            // freshly-updated interact_count.
            var bumps = rows.Where(r => r.Participated)
                .GroupBy(r => (r.BotGuid, r.Counterpart))
                .Select(g => new
                {
                    Bot = g.Key.BotGuid,
                    Cp = g.Key.Counterpart,
                    CpGuid = g.Max(r => r.CounterpartGuid),
                    N = g.Count(),
                    BatchMaxSal = g.Max(r => r.Salience)
                });

            foreach (var b in bumps)
            {
                int maxSal = _maxSalience.AddOrUpdate((b.Bot, b.Cp), b.BatchMaxSal,
                    (_, prev) => Math.Max(prev, b.BatchMaxSal));

                await conn.ExecuteAsync(@"
                    INSERT INTO chat_relationship
                      (bot_guid, counterpart_name, counterpart_guid, summary, strength,
                       interact_count, first_interact_utc, last_interact_utc)
                    VALUES (@Bot, @Cp, @CpGuid, '', LN(1 + @N) * (1 + 0.25 * @MaxSal), @N, UTC_TIMESTAMP(), UTC_TIMESTAMP())
                    ON DUPLICATE KEY UPDATE
                      interact_count    = interact_count + @N,
                      last_interact_utc = UTC_TIMESTAMP(),
                      counterpart_guid  = IF(@CpGuid > 0, @CpGuid, counterpart_guid),
                      strength          = LN(1 + interact_count) * (1 + 0.25 * @MaxSal)",
                    new { b.Bot, b.Cp, b.CpGuid, b.N, MaxSal = maxSal });

                _knownPairs[(b.Bot, b.Cp)] = 1;
            }

            _logger.LogDebug("[CHAT-MEM] flushed {Rows} Tier-1 rows, {Bumps} relationship bumps",
                rows.Count, bumps.Count());
        }
        catch (Exception ex)
        {
            CircuitTrace.Hit(0, "chat: memory flush failed, rows lost", rows.Count);
            _logger.LogError(ex, "[CHAT-MEM] flush failed — {Count} rows lost this pass", rows.Count);
        }

        // Retire stale valve buckets so the dictionary doesn't grow forever
        long currentHour = DateTime.UtcNow.Ticks / TimeSpan.TicksPerHour;
        foreach (var key in _hourCounts.Keys)
            if (key.Hour < currentHour - 1) { CircuitTrace.Hit(key.Bot, "chat: stale valve bucket retired"); _hourCounts.TryRemove(key, out _); }
    }

    // ==================== Prompt-facing read ====================

    /// <summary>
    /// The {relationship_summary} block. Post-C8 the compactor's §7.3-format summary is
    /// returned verbatim; until then a stub is synthesized from the row.  // C8 replaces
    /// Empty string = no history (the assembler renders "You don't know this person.").
    /// </summary>
    public async Task<string> GetRelationshipSummaryAsync(int botGuid, string counterpart)
    {
        try
        {
            using var conn = _db.Admin();
            var row = await conn.QuerySingleOrDefaultAsync<RelRow>(@"
                SELECT summary AS Summary, interact_count AS InteractCount,
                       last_interact_utc AS LastUtc
                FROM chat_relationship
                WHERE bot_guid=@botGuid AND counterpart_name=@cp",
                new { botGuid, cp = Norm(counterpart) });

            if (row == null || row.InteractCount == 0) { CircuitTrace.Hit(botGuid, "chat: no relationship history"); return ""; }
            if (!string.IsNullOrWhiteSpace(row.Summary)) { CircuitTrace.Hit(botGuid, "chat: compacted relationship summary used"); return row.Summary; }   // C8's compacted summary wins

            return $"You've talked with them {row.InteractCount} times before; last time {Ago(row.LastUtc)}.";
        }
        catch (Exception ex)
        {
            CircuitTrace.Hit(botGuid, "chat: relationship read failed");
            _logger.LogWarning("[CHAT-MEM] relationship read failed for bot={Bot}/{Cp}: {Error}",
                botGuid, counterpart, ex.Message);
            return "";
        }
    }

    /// <summary>chat_relationship.strength for the urge W_rel term (§9.2). 0 = stranger.</summary>
    public async Task<float> GetStrengthAsync(int botGuid, string counterpart)
    {
        try
        {
            using var conn = _db.Admin();
            return await conn.QuerySingleOrDefaultAsync<float?>(
                "SELECT strength FROM chat_relationship WHERE bot_guid=@b AND counterpart_name=@c",
                new { b = botGuid, c = Norm(counterpart) }) ?? 0f;
        }
        catch { CircuitTrace.Hit(botGuid, "chat: strength read failed, stranger assumed"); return 0f; }
    }

    // ==================== internals ====================

    private bool IsFirstMeeting(int botGuid, string counterpart)
    {
        var key = (botGuid, Norm(counterpart));
        if (_knownPairs.ContainsKey(key)) { CircuitTrace.Hit(botGuid, "chat: pair already known (cache)"); return false; }

        // Lazy one-time check against the table (cheap PK lookup, once per pair per boot)
        try
        {
            using var conn = _db.Admin();
            var exists = conn.QuerySingleOrDefault<int?>(
                "SELECT 1 FROM chat_relationship WHERE bot_guid=@b AND counterpart_name=@c",
                new { b = botGuid, c = key.Item2 });
            if (exists != null) { CircuitTrace.Hit(botGuid, "chat: pair known in db"); _knownPairs[key] = 1; return false; }
        }
        catch { CircuitTrace.Hit(botGuid, "chat: first-meeting check db hiccup, assume known"); /* on DB hiccup, assume known — a missed salience-3 is harmless */ return false; }

        _knownPairs[key] = 1;   // mark now; the flush creates the row
        return true;
    }

    private static string Ago(DateTime utc)
    {
        var span = DateTime.UtcNow - utc;
        if (span.TotalMinutes < 30) return "just now";   // cb:fold pure prose transform for prompt, no guid in reach
        if (span.TotalHours < 20) return "earlier today";   // cb:fold pure prose transform for prompt, no guid in reach
        if (span.TotalHours < 44) return "yesterday";   // cb:fold pure prose transform for prompt, no guid in reach
        return $"{Math.Max(2, (int)span.TotalDays)} days ago";
    }

    private static string Norm(string name) => (name ?? "").Trim();

    private static string KindStr(ChatKind kind) => kind switch
    {
        ChatKind.Whisper => "whisper",   // cb:fold pure kind-to-string mapping, no guid in reach
        ChatKind.Channel => "channel",   // cb:fold pure kind-to-string mapping, no guid in reach
        ChatKind.Party => "party",   // cb:fold pure kind-to-string mapping, no guid in reach
        _ => "say"   // cb:fold pure kind-to-string mapping, no guid in reach
    };

    private sealed class RelRow
    {
        public string Summary { get; set; } = "";
        public int InteractCount { get; set; }
        public DateTime LastUtc { get; set; }
    }
}