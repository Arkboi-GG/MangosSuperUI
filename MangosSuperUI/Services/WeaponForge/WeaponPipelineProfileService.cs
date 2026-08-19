using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using MangosSuperUI.Models;

namespace MangosSuperUI.Services.WeaponForge;

/// <summary>
/// Durable, installation-local configuration for the staged sketch pipeline.  Nothing here names
/// Nico's hosts, model folders, or credentials: another SuperUI installation can select a provider,
/// import the same public profile, and enter its own credential/workflow from the Forge page.
/// Secrets are never returned by the status/profile endpoints.
/// </summary>
public sealed class WeaponPipelineProfileService
{
    private const string ProfileKey = "sketch_pipeline_profile_v1";
    private readonly ConnectionFactory _db;

    public WeaponPipelineProfileService(ConnectionFactory db) => _db = db;

    public async Task<WeaponPipelineProfile> LoadAsync()
    {
        await using var conn = _db.Admin();
        await conn.OpenAsync();
        var json = await conn.QueryFirstOrDefaultAsync<string?>(
            "SELECT v FROM weapon_forge_config WHERE k=@k", new { k = ProfileKey });
        if (string.IsNullOrWhiteSpace(json)) return new WeaponPipelineProfile();
        try { return JsonSerializer.Deserialize<WeaponPipelineProfile>(json) ?? new WeaponPipelineProfile(); }
        catch { return new WeaponPipelineProfile(); }
    }

    public async Task<WeaponPipelineProfile> SaveAsync(WeaponPipelineProfile input)
    {
        var current = await LoadAsync();
        var profile = input with
        {
            Provider = NormalizeProvider(input.Provider),
            TripoBaseUrl = NormalizeBaseUrl(input.TripoBaseUrl),
            TripoModel = string.IsNullOrWhiteSpace(input.TripoModel) ? "tripo-p1" : input.TripoModel.Trim(),
            ComfyNodeName = string.IsNullOrWhiteSpace(input.ComfyNodeName) ? "weapon-forge" : input.ComfyNodeName.Trim(),
            ComfyBaseUrl = NormalizeComfyUrl(input.ComfyBaseUrl),
            TripoApiKey = string.IsNullOrWhiteSpace(input.TripoApiKey) ? current.TripoApiKey : input.TripoApiKey.Trim(),
            TargetTriangles = Math.Clamp(input.TargetTriangles, 200, 1000),
            TextureQuality = input.TextureQuality is "standard" or "detailed" ? input.TextureQuality : "detailed",
        };
        await using var conn = _db.Admin();
        await conn.OpenAsync();
        await conn.ExecuteAsync(
            @"INSERT INTO weapon_forge_config (k,v) VALUES (@k,@v)
              ON DUPLICATE KEY UPDATE v=VALUES(v)",
            new { k = ProfileKey, v = JsonSerializer.Serialize(profile) });
        return profile;
    }

    public static object Public(WeaponPipelineProfile p) => new
    {
        p.Provider,
        p.TripoBaseUrl,
        p.TripoModel,
        p.ComfyNodeName,
        p.ComfyBaseUrl,
        p.TargetTriangles,
        p.TextureQuality,
        p.EnableImageAutofix,
        p.SmartLowPoly,
        hasTripoApiKey = !string.IsNullOrWhiteSpace(p.TripoApiKey),
    };

    public static object Exportable(WeaponPipelineProfile p) => new
    {
        schema = "weapon-forge-pipeline/v1",
        p.Provider,
        p.TripoBaseUrl,
        p.TripoModel,
        p.ComfyNodeName,
        p.ComfyBaseUrl,
        p.TargetTriangles,
        p.TextureQuality,
        p.EnableImageAutofix,
        p.SmartLowPoly,
        note = "Credentials are intentionally excluded. Enter your own key in Weapon Forge setup.",
    };

    private static string NormalizeProvider(string? value) =>
        string.Equals(value, "tripo", StringComparison.OrdinalIgnoreCase) ? "tripo" : "comfyui";

    private static string NormalizeBaseUrl(string? value)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "https://openapi.tripo3d.ai/v3" : value.Trim().TrimEnd('/');
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
            throw new ArgumentException("Provider URL must be HTTPS (HTTP is allowed only for localhost).", nameof(value));
        return text;
    }

    private static string NormalizeComfyUrl(string? value)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "http://localhost:8188" : value.Trim().TrimEnd('/');
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("ComfyUI URL must be an absolute HTTP or HTTPS URL.", nameof(value));
        return text;
    }
}

public sealed record WeaponPipelineProfile
{
    public string Provider { get; init; } = "comfyui";
    public string TripoBaseUrl { get; init; } = "https://openapi.tripo3d.ai/v3";
    public string TripoModel { get; init; } = "tripo-p1";
    public string ComfyNodeName { get; init; } = "weapon-forge";
    public string ComfyBaseUrl { get; init; } = "http://localhost:8188";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TripoApiKey { get; init; }
    public int TargetTriangles { get; init; } = 800;
    public string TextureQuality { get; init; } = "detailed";
    public bool EnableImageAutofix { get; init; } = true;
    public bool SmartLowPoly { get; init; } = true;
}

/// <summary>Tripo v3 provider used by the optional all-in-Forge cloud route.  It uploads private
/// inputs with presigned URLs, uses single-image or true multiview generation as appropriate,
/// requests a textured game-budget GLB, polls the async task, then returns bytes to the exact same
/// importer/compiler as the local ComfyUI route.</summary>
public sealed class TripoWeapon3DProvider : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(12) };
    private readonly ILogger<TripoWeapon3DProvider> _logger;

    public TripoWeapon3DProvider(ILogger<TripoWeapon3DProvider> logger) => _logger = logger;

    public async Task<Weapon3DGenerationResult> GenerateAsync(
        IReadOnlyDictionary<string, byte[]> views, WeaponPipelineProfile profile, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(profile.TripoApiKey))
            return new(false, null, "Tripo is selected but no API key is stored. Open pipeline setup on this page.");
        if (!views.TryGetValue("front", out var front) || front.Length == 0)
            return new(false, null, "A front/broadside reference is required.");

        try
        {
            var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (role, bytes) in views.Where(v => v.Value.Length > 0))
                tokens[role] = await UploadPngAsync(bytes, profile, ct);

            bool multiview = tokens.Keys.Count(k => k is "front" or "left" or "back" or "right") >= 2;
            object body = multiview
                ? BuildMultiviewBody(tokens, profile)
                : BuildSingleBody(tokens["front"], profile);
            string endpoint = multiview ? "/generation/multiview-to-model" : "/generation/image-to-model";
            string taskId = await PostTaskAsync(profile, endpoint, body, ct);
            var (url, credits) = await WaitForModelAsync(profile, taskId, ct);
            var glb = await _http.GetByteArrayAsync(url, ct);
            if (glb.Length < 12 || glb[0] != 'g' || glb[1] != 'l' || glb[2] != 'T' || glb[3] != 'F')
                return new(false, null, "Provider completed but its model download was not a binary GLB.");
            _logger.LogInformation("WeaponForge: Tripo {Mode} produced {Bytes:N0} byte GLB ({Credits} credits)",
                multiview ? "multiview" : "single-image", glb.Length, credits);
            return new(true, glb,
                $"Tripo {(multiview ? "multiview" : "single-image")} reconstruction completed" +
                (credits is null ? "." : $" ({credits} credits)."));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "WeaponForge: Tripo generation failed");
            return new(false, null, ex.Message);
        }
    }

    private async Task<string> UploadPngAsync(byte[] bytes, WeaponPipelineProfile p, CancellationToken ct)
    {
        using var req = Authorized(p, HttpMethod.Post, "/files/presign");
        req.Content = JsonContent.Create(new { format = "png" });
        using var resp = await _http.SendAsync(req, ct);
        var root = await ReadSuccessAsync(resp, ct);
        var data = root.GetProperty("data");
        string uploadUrl = data.GetProperty("presigned_url").GetString()!;
        string token = data.GetProperty("file_token").GetString()!;
        using var put = new HttpRequestMessage(HttpMethod.Put, uploadUrl)
        {
            Content = new ByteArrayContent(bytes),
        };
        put.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var putResp = await _http.SendAsync(put, ct);
        if (!putResp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Reference upload failed ({(int)putResp.StatusCode}).");
        return token;
    }

    private static object BuildSingleBody(string token, WeaponPipelineProfile p) => new
    {
        input = token,
        model = p.TripoModel,
        face_limit = p.TargetTriangles,
        texture = true,
        pbr = true,
        texture_quality = p.TextureQuality,
        geometry_quality = p.TripoModel.Contains("p1", StringComparison.OrdinalIgnoreCase) ? null : "detailed",
        enable_image_autofix = p.EnableImageAutofix,
        smart_low_poly = p.TripoModel.Contains("p1", StringComparison.OrdinalIgnoreCase) ? (bool?)null : p.SmartLowPoly,
        orientation = "align_image",
        export_uv = true,
    };

    private static object BuildMultiviewBody(Dictionary<string, string> tokens, WeaponPipelineProfile p)
    {
        var inputs = new List<Dictionary<string, string>>();
        foreach (var role in new[] { "front", "left", "back", "right" })
            if (tokens.TryGetValue(role, out var token)) inputs.Add(new() { [role] = token });
        return new
        {
            inputs,
            model = p.TripoModel,
            face_limit = p.TargetTriangles,
            texture = true,
            pbr = true,
            texture_quality = p.TextureQuality,
            geometry_quality = p.TripoModel.Contains("p1", StringComparison.OrdinalIgnoreCase) ? null : "detailed",
            smart_low_poly = p.TripoModel.Contains("p1", StringComparison.OrdinalIgnoreCase) ? (bool?)null : p.SmartLowPoly,
            orientation = "align_image",
            export_uv = true,
        };
    }

    private async Task<string> PostTaskAsync(WeaponPipelineProfile p, string endpoint, object body, CancellationToken ct)
    {
        using var req = Authorized(p, HttpMethod.Post, endpoint);
        req.Content = JsonContent.Create(body, options: new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        });
        using var resp = await _http.SendAsync(req, ct);
        var root = await ReadSuccessAsync(resp, ct);
        return root.GetProperty("data").GetProperty("task_id").GetString()
               ?? throw new InvalidOperationException("Provider response had no task id.");
    }

    private async Task<(string Url, int? Credits)> WaitForModelAsync(
        WeaponPipelineProfile p, string taskId, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(10);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
            using var req = Authorized(p, HttpMethod.Get, $"/tasks/{Uri.EscapeDataString(taskId)}");
            using var resp = await _http.SendAsync(req, ct);
            var root = await ReadSuccessAsync(resp, ct);
            var data = root.GetProperty("data");
            string status = data.GetProperty("status").GetString() ?? "unknown";
            if (status is "failed" or "cancelled")
            {
                string error = data.TryGetProperty("error_message", out var e) ? e.GetString() ?? status : status;
                throw new InvalidOperationException($"Provider task {status}: {error}");
            }
            if (status != "success") continue;
            if (!data.TryGetProperty("output", out var output) ||
                !output.TryGetProperty("model_url", out var urlEl) || string.IsNullOrWhiteSpace(urlEl.GetString()))
                throw new InvalidOperationException("Provider task succeeded without a model URL.");
            int? credits = data.TryGetProperty("credits_consumed", out var c) && c.TryGetInt32(out var value) ? value : null;
            return (urlEl.GetString()!, credits);
        }
        throw new TimeoutException("Provider did not finish within 10 minutes.");
    }

    private static async Task<JsonElement> ReadSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var text = await response.Content.ReadAsStringAsync(ct);
        JsonElement root;
        try { root = JsonSerializer.Deserialize<JsonElement>(text); }
        catch { throw new InvalidOperationException($"Provider returned HTTP {(int)response.StatusCode}: {text[..Math.Min(300, text.Length)]}"); }
        int code = root.TryGetProperty("code", out var c) && c.TryGetInt32(out var v) ? v : -1;
        if (!response.IsSuccessStatusCode || code != 0)
        {
            string message = root.TryGetProperty("message", out var m) ? m.GetString() ?? text : text;
            throw new InvalidOperationException($"Provider rejected the request ({(int)response.StatusCode}, code {code}): {message}");
        }
        return root;
    }

    private static HttpRequestMessage Authorized(WeaponPipelineProfile p, HttpMethod method, string path)
    {
        var req = new HttpRequestMessage(method, p.TripoBaseUrl.TrimEnd('/') + path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", p.TripoApiKey);
        return req;
    }

    public void Dispose() => _http.Dispose();
}
