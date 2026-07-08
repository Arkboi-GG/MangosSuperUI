using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using MangosSuperUI.Models;
using MangosSuperUI.Services;
using MangosSuperUI.Hubs;
using MangosSuperUI.BotLogic.Chat.Core;
using MangosSuperUI.BotLogic.Chat.Voice;
using Dapper;
using System.Text.Json;

namespace MangosSuperUI.Controllers;

/// <summary>
/// CHAT_ARCHITECTURE §14.3 — the two chat surfaces:
///   /BotChat/Settings  ("Chat Feel"):     preset bar + collapsible setting groups
///   /BotChat/Capacity  ("Chat Capacity"): profiles, kill switches, broker panel (stub → C5)
/// Every settings write logs [CHAT-SET] who/key/old/new (§15); profile switches log
/// [CHAT-CAP]. Preset applies and profile changes additionally hit AuditService (house
/// pattern). A SignalR "ChatSettingsChanged" ping follows every write so open dashboards
/// refresh (§14.3). Profile writes are storage-only in C1 — the broker reads them in C5.
/// </summary>
[Route("BotChat")]
public class ChatSettingsController : Controller
{
    private readonly ConnectionFactory _db;
    private readonly ChatSettingsService _settings;
    private readonly VoiceLibraryBuilder _voiceBuilder;
    private readonly PersonaService _personas;
    private readonly AuditService _audit;
    private readonly IHubContext<BotBridgeHub> _hub;
    private readonly ILogger<ChatSettingsController> _logger;

    public ChatSettingsController(ConnectionFactory db, ChatSettingsService settings,
        VoiceLibraryBuilder voiceBuilder, PersonaService personas,
        AuditService audit, IHubContext<BotBridgeHub> hub, ILogger<ChatSettingsController> logger)
    {
        _db = db;
        _settings = settings;
        _voiceBuilder = voiceBuilder;
        _personas = personas;
        _audit = audit;
        _hub = hub;
        _logger = logger;
    }

    private string Who => $"admin@{HttpContext.Connection.RemoteIpAddress}";

    // ==================== Pages ====================

    [HttpGet("Settings")]
    public IActionResult Settings() => View("~/Views/BotChat/Settings.cshtml");

    [HttpGet("Capacity")]
    public IActionResult Capacity() => View("~/Views/BotChat/Capacity.cshtml");

    // ==================== Chat Feel — data + writes ====================

    /// <summary>Page model: every §14.4 setting (registry metadata + live value) + presets.</summary>
    [HttpGet("Settings/Data")]
    public async Task<IActionResult> SettingsData()
    {
        var live = _settings.GetScope("global");

        using var conn = _db.Admin();
        var presets = (await conn.QueryAsync<(string Name, int Builtin)>(
            "SELECT name, builtin FROM chat_preset ORDER BY builtin DESC, name"))
            .Select(p => new { name = p.Name, builtin = p.Builtin == 1 });

        return Json(new
        {
            settings = ChatSettingsRegistry.All.Select(d => new
            {
                key = d.Key,
                group = d.Group,
                type = d.Type,
                meaning = d.Meaning,
                min = d.Min,
                max = d.Max,
                step = d.Step,
                def = d.Default,
                value = live.TryGetValue(d.Key, out var v) ? v : d.Default
            }),
            presets,
            activePreset = _settings.Get("global.active_preset")
        });
    }

    /// <summary>
    /// C1 exit-criteria endpoint: reads through the ChatSettingsService SNAPSHOT (not the
    /// DB), proving the 5 s TTL hot-apply — a fresh write must show here within 5 s.
    /// </summary>
    [HttpGet("Settings/Debug")]
    public IActionResult Debug(string key, int zoneId = 0)
        => Json(new { key, zoneId, value = _settings.Get(zoneId, key) });

    public record SetRequest(string key, string value, string? scope);

    [HttpPost("Settings/Set")]
    public async Task<IActionResult> Set([FromBody] SetRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.key) || req.value == null)
            return Json(new { success = false, error = "key and value are required" });
        if (!ChatSettingsRegistry.ByKey.ContainsKey(req.key))
            return Json(new { success = false, error = $"unknown setting key '{req.key}'" });

        var scope = string.IsNullOrWhiteSpace(req.scope) ? "global" : req.scope;
        var old = await _settings.SetAsync(scope, req.key, req.value);

        _logger.LogInformation("[CHAT-SET] {Who} {Scope}/{Key}: '{Old}' → '{New}'",
            Who, scope, req.key, old ?? "(unset)", req.value);
        await _hub.Clients.All.SendAsync("ChatSettingsChanged", new { keys = new[] { req.key } });

        return Json(new { success = true, key = req.key, old, value = req.value });
    }

    public record PresetRequest(string name);

    [HttpPost("Settings/ApplyPreset")]
    public async Task<IActionResult> ApplyPreset([FromBody] PresetRequest req)
    {
        var changes = await _settings.ApplyPresetAsync(req.name);
        if (changes == null)
            return Json(new { success = false, error = $"preset '{req.name}' not found" });

        foreach (var (key, old, val) in changes)
            _logger.LogInformation("[CHAT-SET] {Who} preset '{Preset}' global/{Key}: '{Old}' → '{New}'",
                Who, req.name, key, old ?? "(unset)", val);

        await _audit.LogAsync(new AuditEntry
        {
            Operator = "admin",
            OperatorIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
            Category = "bots",
            Action = "chat_preset_apply",
            TargetType = "chat_settings",
            TargetName = req.name,
            StateAfter = JsonSerializer.Serialize(changes.Select(c => new { c.Key, c.New })),
            IsReversible = true,
            Success = true,
            Notes = $"Applied chat preset '{req.name}' ({changes.Count} settings)"
        });
        await _hub.Clients.All.SendAsync("ChatSettingsChanged",
            new { keys = changes.Select(c => c.Key).ToArray() });

        return Json(new { success = true, applied = changes.Count });
    }

    [HttpPost("Settings/SavePreset")]
    public async Task<IActionResult> SavePreset([FromBody] PresetRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.name) || req.name.Length > 48)
            return Json(new { success = false, error = "preset name must be 1–48 chars" });

        var ok = await _settings.SavePresetAsync(req.name.Trim());
        if (!ok)
            return Json(new { success = false, error = "cannot overwrite a built-in preset" });

        _logger.LogInformation("[CHAT-SET] {Who} saved custom preset '{Preset}'", Who, req.name);
        return Json(new { success = true });
    }

    // ==================== Chat Capacity — profiles + switches ====================

    [HttpGet("Capacity/Data")]
    public async Task<IActionResult> CapacityData()
    {
        using var conn = _db.Admin();
        var profiles = await conn.QueryAsync(@"
            SELECT id, name, endpoint_url AS endpointUrl, api_flavor AS apiFlavor,
                   model_reactive AS modelReactive, model_ambient AS modelAmbient,
                   model_batch AS modelBatch, ctx_budget_tokens AS ctxBudgetTokens,
                   concurrency, reactive_reserved AS reactiveReserved,
                   ambient_rate_mult AS ambientRateMult, active
            FROM chat_inference_profile ORDER BY id");

        var voiceCount = await conn.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM chat_voice WHERE retired=0");
        var seedPersonaCount = await conn.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM bot_persona WHERE voice_id IS NULL");

        return Json(new
        {
            profiles,
            chatEnabled = _settings.GetBool(0, "global.chat_enabled", true),
            ambientEnabled = _settings.GetBool(0, "global.ambient_enabled", true),
            voiceCount,
            voiceTarget = _settings.GetInt(0, "voice.library_target", 300),
            seedPersonaCount,
            voiceBuild = _voiceBuilder.Status
        });
    }

    // ==================== Voice library (C6) ====================

    [HttpPost("Capacity/BuildVoiceLibrary")]
    public async Task<IActionResult> BuildVoiceLibrary()
    {
        if (!_voiceBuilder.TryStart())
            return Json(new { success = false, error = "a build is already running" });

        _logger.LogInformation("[CHAT-CAP] {Who} started a voice library build", Who);
        await _audit.LogAsync(new AuditEntry
        {
            Operator = "admin",
            OperatorIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
            Category = "bots",
            Action = "voice_library_build",
            TargetType = "chat_voice",
            TargetName = "library",
            IsReversible = false,
            Success = true,
            Notes = "Voice library build started (Batch class)"
        });
        return Json(new { success = true });
    }

    [HttpGet("Capacity/VoiceBuildStatus")]
    public IActionResult VoiceBuildStatus() => Json(_voiceBuilder.Status);

    /// <summary>One-shot: reassign pre-library (seed-era) personas onto library voices.</summary>
    [HttpPost("Capacity/RerollSeedPersonas")]
    public async Task<IActionResult> RerollSeedPersonas()
    {
        var count = await _personas.RerollSeedPersonasAsync();
        _logger.LogInformation("[CHAT-CAP] {Who} rerolled {Count} seed-era personas onto the library", Who, count);
        return Json(new { success = true, rerolled = count });
    }

    public class ProfileDto
    {
        public int id { get; set; }                       // 0 = create
        public string name { get; set; } = "";
        public string endpointUrl { get; set; } = "";
        public string apiFlavor { get; set; } = "ollama";   // 'ollama' | 'openai' (vLLM etc.)
        public string modelReactive { get; set; } = "";
        public string modelAmbient { get; set; } = "";
        public string modelBatch { get; set; } = "";      // '' = batch lane disabled
        public int ctxBudgetTokens { get; set; } = 3000;
        public int concurrency { get; set; } = 2;
        public int reactiveReserved { get; set; } = 1;
        public float ambientRateMult { get; set; } = 1.0f;
    }

    [HttpPost("Capacity/SaveProfile")]
    public async Task<IActionResult> SaveProfile([FromBody] ProfileDto p)
    {
        if (string.IsNullOrWhiteSpace(p.name) || string.IsNullOrWhiteSpace(p.endpointUrl))
            return Json(new { success = false, error = "name and endpoint_url are required" });
        if (p.reactiveReserved > p.concurrency)
            return Json(new { success = false, error = "reactive_reserved cannot exceed concurrency" });
        p.apiFlavor = (p.apiFlavor ?? "ollama").Trim().ToLowerInvariant();
        if (p.apiFlavor is not ("ollama" or "openai"))
            return Json(new { success = false, error = "api_flavor must be 'ollama' or 'openai'" });

        using var conn = _db.Admin();
        if (p.id == 0)
        {
            var dup = await conn.QuerySingleOrDefaultAsync<int?>(
                "SELECT id FROM chat_inference_profile WHERE name=@name", new { p.name });
            if (dup != null)
                return Json(new { success = false, error = $"profile '{p.name}' already exists" });

            await conn.ExecuteAsync(@"
                INSERT INTO chat_inference_profile
                  (name, endpoint_url, api_flavor, model_reactive, model_ambient, model_batch,
                   ctx_budget_tokens, concurrency, reactive_reserved, ambient_rate_mult, active)
                VALUES (@name, @endpointUrl, @apiFlavor, @modelReactive, @modelAmbient, @modelBatch,
                        @ctxBudgetTokens, @concurrency, @reactiveReserved, @ambientRateMult, 0)", p);
        }
        else
        {
            await conn.ExecuteAsync(@"
                UPDATE chat_inference_profile SET
                  name=@name, endpoint_url=@endpointUrl, api_flavor=@apiFlavor, model_reactive=@modelReactive,
                  model_ambient=@modelAmbient, model_batch=@modelBatch,
                  ctx_budget_tokens=@ctxBudgetTokens, concurrency=@concurrency,
                  reactive_reserved=@reactiveReserved, ambient_rate_mult=@ambientRateMult
                WHERE id=@id", p);
        }

        _logger.LogInformation("[CHAT-CAP] {Who} saved profile '{Name}' (id={Id})", Who, p.name, p.id);
        return Json(new { success = true });
    }

    public record ProfileIdRequest(int id);

    [HttpPost("Capacity/DeleteProfile")]
    public async Task<IActionResult> DeleteProfile([FromBody] ProfileIdRequest req)
    {
        using var conn = _db.Admin();
        var active = await conn.QuerySingleOrDefaultAsync<int?>(
            "SELECT active FROM chat_inference_profile WHERE id=@id", new { req.id });
        if (active == null) return Json(new { success = false, error = "profile not found" });
        if (active == 1) return Json(new { success = false, error = "cannot delete the ACTIVE profile" });

        await conn.ExecuteAsync("DELETE FROM chat_inference_profile WHERE id=@id", new { req.id });
        _logger.LogInformation("[CHAT-CAP] {Who} deleted profile id={Id}", Who, req.id);
        return Json(new { success = true });
    }

    /// <summary>Exactly-one-active flip. Storage-only in C1; the InferenceBroker reads it in C5.</summary>
    [HttpPost("Capacity/ActivateProfile")]
    public async Task<IActionResult> ActivateProfile([FromBody] ProfileIdRequest req)
    {
        using var conn = _db.Admin();
        var name = await conn.QuerySingleOrDefaultAsync<string?>(
            "SELECT name FROM chat_inference_profile WHERE id=@id", new { req.id });
        if (name == null) return Json(new { success = false, error = "profile not found" });

        await conn.ExecuteAsync("UPDATE chat_inference_profile SET active = (id=@id)", new { req.id });

        _logger.LogInformation("[CHAT-CAP] {Who} profile switch → '{Name}' (id={Id})", Who, name, req.id);
        await _audit.LogAsync(new AuditEntry
        {
            Operator = "admin",
            OperatorIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
            Category = "bots",
            Action = "chat_profile_activate",
            TargetType = "chat_inference_profile",
            TargetName = name,
            IsReversible = true,
            Success = true,
            Notes = $"Activated inference profile '{name}' (broker consumes in C5)"
        });
        return Json(new { success = true, name });
    }
}