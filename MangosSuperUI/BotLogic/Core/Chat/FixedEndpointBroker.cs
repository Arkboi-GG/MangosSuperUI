using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using MangosSuperUI.Models;
using MangosSuperUI.BotLogic.Chat.Core;

namespace MangosSuperUI.BotLogic.Chat.Capacity;

// ======================== Broker contract (CHAT_ARCHITECTURE §12.1) ========================
// NOTE (C5): IInferenceBroker/GenOptions/InferenceLease move next to the real
// InferenceBroker.cs, and BrokerStatus gets its own file, when this temp broker is deleted.

/// <summary>
/// Generation options. Extended 2026-07-13 with the repetition controls that were never
/// being sent — the request body carried temperature/top_p/num_predict and nothing else,
/// so both backends were silently applying their own defaults.
///
/// HONEST SCOPE: repeat_penalty, presence_penalty and frequency_penalty act WITHIN a
/// single generation. They stop a line from eating its own tail; they do NOT stop two
/// separate calls from producing the same line, because the backend has no memory across
/// calls. Cross-call sameness is fixed upstream (persona diversity, shuffled few-shot)
/// and downstream (StylePostPass step 10's emission ledger) — not here. `stop` is the
/// quietly valuable one: it keeps a chat reply to ONE line instead of the model helpfully
/// writing the other guy's next turn too.
/// </summary>
public sealed record GenOptions(
    float Temperature,
    float TopP,
    int NumPredict,
    float RepeatPenalty = 1.1f,
    int RepeatLastN = 256,
    float PresencePenalty = 0f,
    float FrequencyPenalty = 0f,
    int? Seed = null,
    IReadOnlyList<string>? Stop = null);

/// <summary>A granted slot. Dispose releases it. Carries the model tag for its class.</summary>
public sealed class InferenceLease : IDisposable
{
    public TrafficClass Class { get; }
    public string Model { get; }
    public string Endpoint { get; }
    /// <summary>'ollama' → /api/generate; 'openai' → /v1/chat/completions (vLLM, LM Studio, …).</summary>
    public string ApiFlavor { get; }
    private readonly Action _release;
    private int _released;

    public InferenceLease(TrafficClass cls, string model, string endpoint, string apiFlavor, Action release)
    {
        Class = cls; Model = model; Endpoint = endpoint; ApiFlavor = apiFlavor; _release = release;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _released, 1) == 0) _release();
    }
}

public sealed record BrokerStatus(string ProfileName, string Endpoint, int SlotsTotal,
                                  int SlotsFree, double LatencyEmaMs);

public interface IInferenceBroker
{
    Task<InferenceLease?> TryAcquireAsync(TrafficClass cls, TimeSpan maxWait, CancellationToken ct);
    Task<string?> GenerateAsync(InferenceLease lease, string system, string prompt,
                                GenOptions opts, CancellationToken ct);
    BrokerStatus GetStatus();
    /// <summary>The active profile's ctx_budget_tokens — the PromptAssembler's cap input (§10.2).</summary>
    int CtxBudgetTokens { get; }
}

// ======================== FixedEndpointBroker (C2 — REPLACED in C5) ========================

/// <summary>
/// C2 TEMPORARY broker (§16 Phase C2): single endpoint read from the ACTIVE
/// chat_inference_profile row, total `concurrency` honored via one semaphore, NO traffic
/// classes (reactive_reserved / batch elasticity land with the real InferenceBroker in
/// C5, which adapts the existing parallel workload service — this file is then deleted).
/// Profile row is re-read every 60 s so a Capacity-tab edit or activation is picked up
/// without restart. Generation speaks BOTH backend dialects per the profile's api_flavor:
/// 'ollama' → /api/generate (proven DTO shape); 'openai' → /v1/chat/completions
/// (vLLM et al). An 'openai' profile with empty model tags auto-resolves the served
/// model via /v1/models on profile refresh (vLLM serves one; we ask it which).
/// </summary>
public class FixedEndpointBroker : IInferenceBroker
{
    private readonly ConnectionFactory _db;
    private readonly ILogger<FixedEndpointBroker> _logger;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(120) };
    private readonly object _gate = new();

    private SemaphoreSlim _slots = new(2, 2);
    private string _profileName = "(none)";
    private string _endpoint = "";
    private string _modelReactive = "";
    private string _modelAmbient = "";
    private string _modelBatch = "";
    private string _apiFlavor = "ollama";
    private int _concurrency = 2;
    private int _ctxBudget = 3000;
    private DateTime _profileUtc = DateTime.MinValue;
    private double _latencyEma;

    public FixedEndpointBroker(ConnectionFactory db, ILogger<FixedEndpointBroker> logger)
    {
        _db = db;
        _logger = logger;
    }

    public int CtxBudgetTokens { get { RefreshProfileIfStale(); return _ctxBudget; } }

    public async Task<InferenceLease?> TryAcquireAsync(TrafficClass cls, TimeSpan maxWait, CancellationToken ct)
    {
        RefreshProfileIfStale();
        SemaphoreSlim slots;
        string endpoint, model, flavor;
        lock (_gate)
        {
            slots = _slots;
            endpoint = _endpoint;
            flavor = _apiFlavor;
            model = cls switch
            {
                TrafficClass.Ambient => _modelAmbient,
                // Batch prefers model_batch; manual runs (voice build button) fall back
                // to the reactive tag when batch is unset — the operator clicked, honor it.
                TrafficClass.Batch => string.IsNullOrEmpty(_modelBatch) ? _modelReactive : _modelBatch,
                _ => _modelReactive
            };
        }
        if (string.IsNullOrEmpty(endpoint)) return null;
        if (string.IsNullOrEmpty(model))
        {
            _logger.LogWarning("[CHAT-CAP] no model tag for {Class} on profile '{Name}' — check the Capacity page", cls, _profileName);
            return null;
        }

        // The voice library is the fleet's ONLY diversity source and it is written once.
        // If Batch is silently running on the small reactive model, say so out loud.
        if (cls == TrafficClass.Batch && string.IsNullOrEmpty(_modelBatch))
            _logger.LogWarning("[CHAT-CAP] Batch has no model_batch tag on profile '{Name}' — falling back to the " +
                               "REACTIVE model '{Model}'. The voice library is generated once and every persona " +
                               "descends from it; set model_batch to the largest model you can serve.",
                               _profileName, model);

        bool got;
        try { got = await slots.WaitAsync(maxWait, ct); }
        catch (OperationCanceledException) { return null; }
        if (!got) return null;

        return new InferenceLease(cls, model, endpoint, flavor, () =>
        {
            try { slots.Release(); } catch (ObjectDisposedException) { /* profile swapped */ }
        });
    }

    public async Task<string?> GenerateAsync(InferenceLease lease, string system, string prompt,
                                             GenOptions opts, CancellationToken ct)
        => lease.ApiFlavor == "openai"
            ? await GenerateOpenAiAsync(lease, system, prompt, opts, ct)
            : await GenerateOllamaAsync(lease, system, prompt, opts, ct);

    private async Task<string?> GenerateOllamaAsync(InferenceLease lease, string system, string prompt,
                                                    GenOptions opts, CancellationToken ct)
    {
        var body = new OllamaRequest
        {
            Model = lease.Model,
            Prompt = prompt,
            System = system,
            Stream = false,
            Options = new OllamaOptions
            {
                Temperature = opts.Temperature,
                TopP = opts.TopP,
                NumPredict = opts.NumPredict,
                RepeatPenalty = opts.RepeatPenalty,
                RepeatLastN = opts.RepeatLastN,
                PresencePenalty = opts.PresencePenalty,
                FrequencyPenalty = opts.FrequencyPenalty,
                Seed = opts.Seed,
                Stop = opts.Stop?.ToList()
            }
        };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var content = new StringContent(JsonSerializer.Serialize(body, JsonOpts), Encoding.UTF8, "application/json");
            var resp = await _http.PostAsync($"{lease.Endpoint.TrimEnd('/')}/api/generate", content, ct);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize<OllamaResponse>(json, JsonOpts);
            sw.Stop();

            lock (_gate) { _latencyEma = _latencyEma <= 0 ? sw.ElapsedMilliseconds : _latencyEma * 0.8 + sw.ElapsedMilliseconds * 0.2; }

            if (string.IsNullOrEmpty(result?.Response))
            {
                _logger.LogWarning("[CHAT-ENGINE] generation fail — empty response (model={Model}, {Ms} ms)", lease.Model, sw.ElapsedMilliseconds);
                return null;
            }
            _logger.LogInformation("[CHAT-ENGINE] generation ok — {Class} model={Model} latency={Ms} ms", lease.Class, lease.Model, sw.ElapsedMilliseconds);
            return result.Response;
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning("[CHAT-ENGINE] generation fail — {Error} (model={Model}, {Ms} ms)", ex.Message, lease.Model, sw.ElapsedMilliseconds);
            return null;
        }
    }

    public BrokerStatus GetStatus()
    {
        lock (_gate)
            return new BrokerStatus(_profileName, _endpoint, _concurrency, _slots.CurrentCount, _latencyEma);
    }

    private async Task<string?> GenerateOpenAiAsync(InferenceLease lease, string system, string prompt,
                                                    GenOptions opts, CancellationToken ct)
    {
        var body = new OpenAiChatRequest
        {
            Model = lease.Model,
            Messages = new List<OpenAiMessage>
            {
                new() { Role = "system", Content = system },
                new() { Role = "user", Content = prompt }
            },
            Temperature = opts.Temperature,
            TopP = opts.TopP,
            MaxTokens = opts.NumPredict,
            PresencePenalty = opts.PresencePenalty,
            FrequencyPenalty = opts.FrequencyPenalty,
            Seed = opts.Seed,
            Stop = opts.Stop?.ToList(),
            Stream = false
        };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var content = new StringContent(JsonSerializer.Serialize(body, JsonOpts), Encoding.UTF8, "application/json");
            var resp = await _http.PostAsync($"{NormalizeBase(lease.Endpoint)}/v1/chat/completions", content, ct);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize<OpenAiChatResponse>(json, JsonOpts);
            sw.Stop();

            lock (_gate) { _latencyEma = _latencyEma <= 0 ? sw.ElapsedMilliseconds : _latencyEma * 0.8 + sw.ElapsedMilliseconds * 0.2; }

            var text = result?.Choices?.FirstOrDefault()?.Message?.Content;
            if (string.IsNullOrEmpty(text))
            {
                _logger.LogWarning("[CHAT-ENGINE] generation fail — empty openai response (model={Model}, {Ms} ms)", lease.Model, sw.ElapsedMilliseconds);
                return null;
            }
            _logger.LogInformation("[CHAT-ENGINE] generation ok — {Class} openai model={Model} latency={Ms} ms", lease.Class, lease.Model, sw.ElapsedMilliseconds);
            return text;
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning("[CHAT-ENGINE] generation fail — {Error} (openai model={Model}, {Ms} ms)", ex.Message, lease.Model, sw.ElapsedMilliseconds);
            return null;
        }
    }

    /// <summary>Accepts base URLs with or without a trailing /v1 (users paste both).</summary>
    private static string NormalizeBase(string endpoint)
    {
        var e = endpoint.TrimEnd('/');
        return e.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? e[..^3] : e;
    }

    // ---------- profile plumbing ----------

    private void RefreshProfileIfStale()
    {
        lock (_gate)
        {
            if (DateTime.UtcNow - _profileUtc < TimeSpan.FromSeconds(60)) return;
            _profileUtc = DateTime.UtcNow;
        }
        try
        {
            using var conn = _db.Admin();
            var p = conn.QuerySingleOrDefault<ProfileRow>(@"
                SELECT name AS Name, endpoint_url AS Endpoint, api_flavor AS ApiFlavor,
                       model_reactive AS ModelReactive, model_ambient AS ModelAmbient,
                       model_batch AS ModelBatch,
                       concurrency AS Concurrency, ctx_budget_tokens AS CtxBudget
                FROM chat_inference_profile WHERE active=1 LIMIT 1");
            if (p == null)
            {
                _logger.LogWarning("[CHAT-CAP] no ACTIVE inference profile — chat generation disabled until one is activated");
                return;
            }
            // OpenAI-flavored profile with empty tags → ask the server what it serves
            // (vLLM exposes exactly one model on /v1/models).
            if (string.Equals(p.ApiFlavor?.Trim(), "openai", StringComparison.OrdinalIgnoreCase) && (string.IsNullOrEmpty(p.ModelReactive) || string.IsNullOrEmpty(p.ModelAmbient) || string.IsNullOrEmpty(p.ModelBatch)))
            {
                var served = ResolveServedModel(p.Endpoint);
                if (served != null)
                {
                    if (string.IsNullOrEmpty(p.ModelReactive)) p.ModelReactive = served;
                    if (string.IsNullOrEmpty(p.ModelAmbient)) p.ModelAmbient = served;
                    if (string.IsNullOrEmpty(p.ModelBatch)) p.ModelBatch = served;
                    _logger.LogInformation("[CHAT-CAP] profile '{Name}' auto-resolved served model → '{Model}'", p.Name, served);
                }
                else
                {
                    _logger.LogWarning("[CHAT-CAP] profile '{Name}' has empty model tags and /v1/models is unreachable", p.Name);
                }
            }

            lock (_gate)
            {
                var v = p;
                if (v.Name != _profileName || v.Concurrency != _concurrency)
                {
                    _logger.LogInformation("[CHAT-CAP] profile in use → '{Name}' ({Endpoint}, concurrency {Conc}, ctx {Ctx})",
                        v.Name, v.Endpoint, v.Concurrency, v.CtxBudget);
                    _slots = new SemaphoreSlim(Math.Max(1, v.Concurrency), Math.Max(1, v.Concurrency));
                }
                _profileName = v.Name;
                _endpoint = v.Endpoint;
                _apiFlavor = string.IsNullOrWhiteSpace(v.ApiFlavor) ? "ollama" : v.ApiFlavor.Trim().ToLowerInvariant();
                _modelReactive = v.ModelReactive;
                _modelBatch = v.ModelBatch;
                _modelAmbient = v.ModelAmbient;
                _concurrency = v.Concurrency;
                _ctxBudget = v.CtxBudget;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CHAT-CAP] profile refresh failed");
        }
    }

    private sealed class ProfileRow
    {
        public string Name { get; set; } = "";
        public string Endpoint { get; set; } = "";
        public string ApiFlavor { get; set; } = "ollama";
        public string ModelReactive { get; set; } = "";
        public string ModelAmbient { get; set; } = "";
        public string ModelBatch { get; set; } = "";
        public int Concurrency { get; set; }
        public int CtxBudget { get; set; }
    }

    private string? ResolveServedModel(string endpoint)
    {
        try
        {
            var json = _http.GetStringAsync($"{NormalizeBase(endpoint)}/v1/models").GetAwaiter().GetResult();
            var models = JsonSerializer.Deserialize<OpenAiModelList>(json, JsonOpts);
            return models?.Data?.FirstOrDefault()?.Id;
        }
        catch { return null; }
    }

    // ---------- OpenAI-compatible DTOs (vLLM /v1/chat/completions + /v1/models) ----------

    private class OpenAiChatRequest
    {
        [JsonPropertyName("model")] public string Model { get; set; } = "";
        [JsonPropertyName("messages")] public List<OpenAiMessage> Messages { get; set; } = new();
        [JsonPropertyName("temperature")] public float Temperature { get; set; }
        [JsonPropertyName("top_p")] public float TopP { get; set; }
        [JsonPropertyName("max_tokens")] public int MaxTokens { get; set; }
        [JsonPropertyName("presence_penalty")] public float PresencePenalty { get; set; }
        [JsonPropertyName("frequency_penalty")] public float FrequencyPenalty { get; set; }
        [JsonPropertyName("seed")] public int? Seed { get; set; }
        [JsonPropertyName("stop")] public List<string>? Stop { get; set; }
        [JsonPropertyName("stream")] public bool Stream { get; set; }
    }

    private class OpenAiMessage
    {
        [JsonPropertyName("role")] public string Role { get; set; } = "";
        [JsonPropertyName("content")] public string Content { get; set; } = "";
    }

    private class OpenAiChatResponse
    {
        [JsonPropertyName("choices")] public List<OpenAiChoice>? Choices { get; set; }
    }

    private class OpenAiChoice
    {
        [JsonPropertyName("message")] public OpenAiMessage? Message { get; set; }
    }

    private class OpenAiModelList
    {
        [JsonPropertyName("data")] public List<OpenAiModelEntry>? Data { get; set; }
    }

    private class OpenAiModelEntry
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
    }

    // ---------- Ollama DTOs (proven shape; the C5 broker formally ports these) ----------

    private class OllamaRequest
    {
        [JsonPropertyName("model")] public string Model { get; set; } = "";
        [JsonPropertyName("prompt")] public string Prompt { get; set; } = "";
        [JsonPropertyName("system")] public string System { get; set; } = "";
        [JsonPropertyName("stream")] public bool Stream { get; set; }
        [JsonPropertyName("options")] public OllamaOptions Options { get; set; } = new();
    }

    private class OllamaOptions
    {
        [JsonPropertyName("temperature")] public float Temperature { get; set; }
        [JsonPropertyName("top_p")] public float TopP { get; set; }
        [JsonPropertyName("num_predict")] public int NumPredict { get; set; }
        [JsonPropertyName("repeat_penalty")] public float RepeatPenalty { get; set; }
        [JsonPropertyName("repeat_last_n")] public int RepeatLastN { get; set; }
        [JsonPropertyName("presence_penalty")] public float PresencePenalty { get; set; }
        [JsonPropertyName("frequency_penalty")] public float FrequencyPenalty { get; set; }
        [JsonPropertyName("seed")] public int? Seed { get; set; }
        [JsonPropertyName("stop")] public List<string>? Stop { get; set; }
    }

    private class OllamaResponse
    {
        [JsonPropertyName("response")] public string? Response { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}