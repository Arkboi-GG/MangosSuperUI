using System.Text;
using System.Text.Json;

namespace MangosSuperUI.Services;

/// <summary>
/// Frees GPU VRAM before a heavy Ollama model load.
///
/// THE PROBLEM
/// ───────────
/// Ollama and ComfyUI/Flux share the GPU. When several models are resident
/// (a small text model, the vision model, whatever Open WebUI last touched,
/// plus Flux), a large text model (e.g. a 35B) can't fit — Ollama queues or
/// fails the load, and the segmented-variation recipe call silently falls back
/// to the deterministic path (nothing new appears on nvtop, but you still get
/// results). See ollama FAQ: "If there is insufficient available memory to load
/// a new model while one or more models are already loaded, all new requests
/// will be queued until the new model can be loaded."
///
/// THE FIX
/// ───────
/// Before loading the target model, evict everything else:
///   - Ollama: GET /api/ps lists resident models; for each one that is NOT the
///     target, POST /api/generate {model, keep_alive:0, prompt:""} which unloads
///     it (confirmed by Ollama docs: empty prompt + keep_alive 0 → done_reason
///     "unload"). Then poll /api/ps until they're actually gone (unload isn't
///     instantaneous — a 20GB model takes several seconds).
///   - ComfyUI: POST /free {unload_models:true, free_memory:true} on each node,
///     which unloads diffusion models and clears the execution cache.
///
/// Gated by the caller on "target not already loaded" so it adds no latency
/// when the model is already hot.
///
/// All operations are best-effort: a node being unreachable or not supporting
/// an endpoint must never break the recipe path — we log and continue.
/// </summary>
public class VramManager
{
    private readonly IConfiguration _config;
    private readonly ILogger<VramManager> _logger;
    private readonly HttpClient _http;

    public VramManager(IConfiguration config, ILogger<VramManager> logger)
    {
        _config = config;
        _logger = logger;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    private string OllamaBaseUrl => (_config["SpellCreator:Ollama:BaseUrl"] ?? "").TrimEnd('/');

    /// <summary>ComfyUI node base URLs from config (Nodes[] or legacy BaseUrl).</summary>
    private List<string> ComfyBaseUrls()
    {
        var urls = new List<string>();
        var nodes = _config.GetSection("SpellCreator:ComfyUI:Nodes").GetChildren().ToList();
        foreach (var nc in nodes)
        {
            var u = nc["BaseUrl"];
            if (!string.IsNullOrEmpty(u)) urls.Add(u.TrimEnd('/'));
        }
        if (urls.Count == 0)
        {
            var legacy = _config["SpellCreator:ComfyUI:BaseUrl"]?.TrimEnd('/');
            if (!string.IsNullOrEmpty(legacy)) urls.Add(legacy);
        }
        return urls;
    }

    // ═══════════════════════════════════════════════════════════════════
    // PUBLIC API
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns true if the named model is currently resident in Ollama VRAM.
    /// Used by callers to skip eviction entirely when the target is already hot.
    /// Conservative: on any error returns false (caller will then try to free,
    /// which is harmless if it turns out it was loaded).
    /// </summary>
    public async Task<bool> IsModelLoadedAsync(string model, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(OllamaBaseUrl) || string.IsNullOrEmpty(model)) return false;
        try
        {
            var loaded = await ListLoadedOllamaModelsAsync(ct);
            return loaded.Any(m => ModelMatches(m, model));
        }
        catch (Exception ex)
        {
            _logger.LogInformation("VramManager: /api/ps check failed ({Err})", ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Make room for <paramref name="targetModel"/>: evict every OTHER resident
    /// Ollama model and free ComfyUI VRAM on all nodes. No-op for the target if
    /// it's already loaded. Best-effort; never throws.
    /// </summary>
    public async Task FreeForModelAsync(string targetModel, CancellationToken ct = default)
    {
        try
        {
            // 1. Evict other Ollama models.
            var loaded = await ListLoadedOllamaModelsAsync(ct);
            var toEvict = loaded.Where(m => !ModelMatches(m, targetModel)).ToList();

            if (toEvict.Count == 0)
                _logger.LogInformation("VramManager: no other Ollama models resident (target '{T}')", targetModel);
            else
                _logger.LogInformation("VramManager: evicting {N} Ollama model(s): {List}",
                    toEvict.Count, string.Join(", ", toEvict));

            foreach (var m in toEvict)
                await UnloadOllamaModelAsync(m, ct);

            // 2. Wait until the evicted models are actually gone (bounded).
            if (toEvict.Count > 0)
                await WaitForEvictionAsync(toEvict, targetModel, ct);

            // 3. Free ComfyUI VRAM on every node.
            await FreeComfyUIAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "VramManager: FreeForModelAsync encountered an error (continuing)");
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // OLLAMA
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>GET /api/ps → list of resident model names.</summary>
    private async Task<List<string>> ListLoadedOllamaModelsAsync(CancellationToken ct)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(OllamaBaseUrl)) return result;

        var resp = await _http.GetAsync($"{OllamaBaseUrl}/api/ps", ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        if (doc.RootElement.TryGetProperty("models", out var models) &&
            models.ValueKind == JsonValueKind.Array)
        {
            foreach (var m in models.EnumerateArray())
            {
                // "name" and "model" are both present; prefer "model" (the tag
                // you'd pass back to the API), fall back to "name".
                string? name =
                    m.TryGetProperty("model", out var mm) ? mm.GetString() :
                    m.TryGetProperty("name", out var nn) ? nn.GetString() : null;
                if (!string.IsNullOrEmpty(name)) result.Add(name);
            }
        }
        return result;
    }

    /// <summary>
    /// Unload a model: POST /api/generate {model, keep_alive:0, prompt:""}.
    /// Ollama returns done_reason "unload". Best-effort.
    /// </summary>
    private async Task UnloadOllamaModelAsync(string model, CancellationToken ct)
    {
        try
        {
            var body = JsonSerializer.Serialize(new
            {
                model,
                prompt = "",
                keep_alive = 0,
                stream = false
            });
            var resp = await _http.PostAsync($"{OllamaBaseUrl}/api/generate",
                new StringContent(body, Encoding.UTF8, "application/json"), ct);

            string reason = "(no body)";
            if (resp.IsSuccessStatusCode)
            {
                try
                {
                    using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
                    if (doc.RootElement.TryGetProperty("done_reason", out var dr))
                        reason = dr.GetString() ?? reason;
                }
                catch { /* body parse optional */ }
            }
            _logger.LogInformation("VramManager: unload '{Model}' → {Status} ({Reason})",
                model, (int)resp.StatusCode, reason);
        }
        catch (Exception ex)
        {
            _logger.LogInformation("VramManager: unload '{Model}' failed ({Err})", model, ex.Message);
        }
    }

    /// <summary>
    /// Poll /api/ps until the evicted models are gone, or a timeout elapses.
    /// Unloading a multi-GB model takes a few seconds; loading the target right
    /// after eviction would otherwise race the freed VRAM.
    /// </summary>
    private async Task WaitForEvictionAsync(
        List<string> evicted, string targetModel, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(12);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(500, ct);
            List<string> still;
            try { still = await ListLoadedOllamaModelsAsync(ct); }
            catch { return; }   // can't check — stop waiting, proceed

            bool anyLeft = evicted.Any(e =>
                still.Any(s => s == e) && !ModelMatches(e, targetModel));
            if (!anyLeft)
            {
                _logger.LogInformation("VramManager: eviction confirmed, VRAM freed");
                return;
            }
        }
        _logger.LogInformation("VramManager: eviction wait timed out (proceeding anyway)");
    }

    /// <summary>
    /// Tolerant model-name match. Ollama may report "qwen3:35b" while config
    /// has "qwen3:35b" — but also handles a bare name vs name:latest, and
    /// case differences. Exact-or-prefix-before-colon comparison.
    /// </summary>
    private static bool ModelMatches(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
        // strip an implicit :latest on either side
        string Norm(string s) => s.EndsWith(":latest", StringComparison.OrdinalIgnoreCase)
            ? s[..^7] : s;
        return string.Equals(Norm(a), Norm(b), StringComparison.OrdinalIgnoreCase);
    }

    // ═══════════════════════════════════════════════════════════════════
    // COMFYUI
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// POST /free {unload_models:true, free_memory:true} on every ComfyUI node.
    /// /free is the core ComfyUI route; some installs also expose /api/free via
    /// ComfyUI-Manager — we try /free first, then /api/free. Best-effort.
    /// </summary>
    private async Task FreeComfyUIAsync(CancellationToken ct)
    {
        var urls = ComfyBaseUrls();
        if (urls.Count == 0) return;

        string body = JsonSerializer.Serialize(new
        {
            unload_models = true,
            free_memory = true
        });

        foreach (var baseUrl in urls)
        {
            bool freed = false;
            foreach (var path in new[] { "/free", "/api/free" })
            {
                try
                {
                    var resp = await _http.PostAsync($"{baseUrl}{path}",
                        new StringContent(body, Encoding.UTF8, "application/json"), ct);
                    if (resp.IsSuccessStatusCode)
                    {
                        _logger.LogInformation("VramManager: ComfyUI freed via {Url}{Path}", baseUrl, path);
                        freed = true;
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogInformation("VramManager: ComfyUI {Url}{Path} free failed ({Err})",
                        baseUrl, path, ex.Message);
                }
            }
            if (!freed)
                _logger.LogInformation("VramManager: ComfyUI free not confirmed for {Url}", baseUrl);
        }
    }
}
