using Microsoft.AspNetCore.Mvc;
using MangosSuperUI.Services;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MangosSuperUI.Controllers;

public class SettingsController : Controller
{
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;
    private readonly AuditService _audit;
    private readonly ILogger<SettingsController> _logger;
    private readonly ComfyUIDispatcher? _comfyDispatcher;

    private string ConfigFilePath => Path.Combine(_env.ContentRootPath, "server-config.json");

    public SettingsController(
        IWebHostEnvironment env,
        IConfiguration config,
        AuditService audit,
        ILogger<SettingsController> logger,
        ComfyUIDispatcher? comfyDispatcher = null)
    {
        _env = env;
        _config = config;
        _audit = audit;
        _logger = logger;
        _comfyDispatcher = comfyDispatcher;
    }

    public IActionResult Index()
    {
        return View();
    }

    /// <summary>
    /// Returns the current running configuration merged from all sources.
    /// </summary>
    [HttpGet]
    public IActionResult Current()
    {
        var settings = new ServerConfig
        {
            ConnectionStrings = new ConnectionStringsConfig
            {
                Mangos = _config.GetConnectionString("Mangos") ?? "",
                Characters = _config.GetConnectionString("Characters") ?? "",
                Realmd = _config.GetConnectionString("Realmd") ?? "",
                Logs = _config.GetConnectionString("Logs") ?? "",
                Admin = _config.GetConnectionString("Admin") ?? ""
            },
            RemoteAccess = new RemoteAccessConfig
            {
                Host = _config["RemoteAccess:Host"] ?? "127.0.0.1",
                Port = int.TryParse(_config["RemoteAccess:Port"], out var p) ? p : 3443,
                Username = _config["RemoteAccess:Username"] ?? "",
                Password = _config["RemoteAccess:Password"] ?? "",
                ReconnectDelayMs = int.TryParse(_config["RemoteAccess:ReconnectDelayMs"], out var rd) ? rd : 3000,
                CommandTimeoutMs = int.TryParse(_config["RemoteAccess:CommandTimeoutMs"], out var ct) ? ct : 5000
            },
            Vmangos = new VmangosConfig
            {
                BinDirectory = _config["Vmangos:BinDirectory"] ?? "",
                RunDirectory = _config["Vmangos:RunDirectory"] ?? "",
                LogDirectory = _config["Vmangos:LogDirectory"] ?? "",
                ConfigDirectory = _config["Vmangos:ConfigDirectory"] ?? "",
                MangosdProcess = _config["Vmangos:MangosdProcess"] ?? "mangosd",
                RealmdProcess = _config["Vmangos:RealmdProcess"] ?? "realmd",
                MangosdConfPath = _config["Vmangos:MangosdConfPath"] ?? "",
                LogsDir = _config["Vmangos:LogsDir"] ?? "",
                DbcPath = _config["Vmangos:DbcPath"] ?? "",
                MapsDataPath = _config["Vmangos:MapsDataPath"] ?? "",
                BackupDirectory = _config["Vmangos:BackupDirectory"] ?? "",
                VmangosSourcePath = _config["Vmangos:VmangosSourcePath"] ?? "",
                VmangosSqlPath = _config["Vmangos:VmangosSqlPath"] ?? "",
                ExtractorsPath = _config["Vmangos:ExtractorsPath"] ?? "",
                ServerDataPath = _config["Vmangos:ServerDataPath"] ?? "",
                ClientDataPath = _config["Vmangos:ClientDataPath"] ?? "",
                VmapsDataPath = _config["Vmangos:VmapsDataPath"] ?? ""
            },
            SpellCreator = BuildSpellCreatorConfig(),
            WeaponForge = new WeaponForgeConfig
            {
                TbcDataPath = _config["WeaponForge:TbcDataPath"] ?? "",
                WotlkDataPath = _config["WeaponForge:WotlkDataPath"] ?? ""
            },
            Wiki = new WikiConfig
            {
                Root = _config["Wiki:Root"] ?? ""
            },
            Kestrel = new KestrelConfig
            {
                Url = _config["Kestrel:Endpoints:Http:Url"] ?? "http://0.0.0.0:5000"
            }
        };

        // Also return whether a server-config.json override file exists.
        // fileStamp identifies the exact bytes on disk the client is looking at, so a
        // later Save can refuse to overwrite an edit that landed in between (hand edits,
        // the setup script, a second browser tab).
        var overrideExists = System.IO.File.Exists(ConfigFilePath);

        return Json(new { settings, overrideExists, configFilePath = ConfigFilePath, fileStamp = ComputeStamp() });
    }

    /// <summary>
    /// Build SpellCreator config from IConfiguration, reading the Nodes[] array dynamically.
    /// </summary>
    private SpellCreatorConfig BuildSpellCreatorConfig()
    {
        var cfg = new SpellCreatorConfig
        {
            ComfyUI = new ComfyUIConfig
            {
                ClipModel2 = _config["SpellCreator:ComfyUI:ClipModel2"] ?? "",
                Nodes = new List<ComfyUINodeConfig>()
            },
            Ollama = new OllamaConfig
            {
                BaseUrl = _config["SpellCreator:Ollama:BaseUrl"] ?? "",
                Model = _config["SpellCreator:Ollama:Model"] ?? "",
                VisionModel = _config["SpellCreator:Ollama:VisionModel"] ?? ""
            },
            RawBlpPath = _config["SpellCreator:RawBlpPath"] ?? "",
            DataPath = _config["SpellCreator:DataPath"] ?? "",
            ClientM2Path = _config["SpellCreator:ClientM2Path"] ?? "",
            ClientDataPath = _config["SpellCreator:ClientDataPath"] ?? "",
            PatchOutputPath = _config["SpellCreator:PatchOutputPath"] ?? ""
        };

        // Read the Nodes[] array from config
        var nodesSection = _config.GetSection("SpellCreator:ComfyUI:Nodes").GetChildren().ToList();
        foreach (var node in nodesSection)
        {
            cfg.ComfyUI.Nodes.Add(new ComfyUINodeConfig
            {
                Name = node["Name"] ?? "",
                BaseUrl = node["BaseUrl"] ?? ""
            });
        }

        return cfg;
    }

    /// <summary>
    /// Returns just the override file contents (server-config.json), or empty if it doesn't exist.
    /// </summary>
    [HttpGet]
    public IActionResult Override()
    {
        if (!System.IO.File.Exists(ConfigFilePath))
            return Json(new { exists = false });

        try
        {
            return Json(new { exists = true, settings = ReadOverrideSettings(), fileStamp = ComputeStamp() });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read server-config.json");
            return Json(new { exists = false, error = ex.Message });
        }
    }

    /// <summary>
    /// Saves settings to server-config.json and returns the file as it now stands on disk.
    /// </summary>
    /// <remarks>
    /// The response carries the re-read file rather than IConfiguration on purpose. The
    /// override is registered with reloadOnChange, but that reload is driven by a file
    /// watcher that fires AFTER this response has already gone out — a client that
    /// re-populated itself from /Settings/Current here would paint pre-save values over
    /// the operator's edits, and a second Save would then write those stale values back,
    /// silently reverting the first one.
    /// </remarks>
    [HttpPost]
    public async Task<IActionResult> Save([FromBody] SettingsSaveRequest req)
    {
        var settings = req?.Settings;
        if (settings is null)
        {
            // Never treat an unparseable/empty body as "blank everything out".
            return Json(new { success = false, error = "No settings in the request body — nothing was written." });
        }

        try
        {
            // Refuse to clobber an edit that landed between this page loading and Save:
            // a hand edit, the setup script, or a second tab. Force skips the check.
            var stampBefore = ComputeStamp();
            if (!req!.Force
                && !string.IsNullOrEmpty(req.ExpectedStamp)
                && stampBefore is not null
                && !string.Equals(stampBefore, req.ExpectedStamp, StringComparison.Ordinal))
            {
                _logger.LogWarning("Refused Save: server-config.json changed on disk since the page loaded.");
                return Json(new
                {
                    success = false,
                    conflict = true,
                    error = "server-config.json changed on disk since this page loaded.",
                    settings = ReadOverrideSettings(),
                    fileStamp = stampBefore
                });
            }

            // MERGE into the existing override rather than rewriting it wholesale:
            // server-config.json carries sections this page doesn't manage
            // (BotChat.InferenceProfiles, future additions) — the old full rewrite
            // silently dropped them on every save. JSONC comments in the existing file
            // are lost on the first save (JsonNode can't round-trip comments); the
            // section VALUES are what must survive.
            JsonObject root = new();
            if (System.IO.File.Exists(ConfigFilePath))
            {
                try
                {
                    var docOpts = new JsonDocumentOptions
                    {
                        CommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true
                    };
                    root = JsonNode.Parse(System.IO.File.ReadAllText(ConfigFilePath), null, docOpts) as JsonObject ?? new JsonObject();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Existing server-config.json unreadable — writing a fresh file.");
                    root = new JsonObject();
                }
            }

            // Snapshot before the merge so we can report exactly what this save changed.
            var before = root.DeepClone();

            // MERGE the section rather than replacing the node. Several keys inside
            // sections this page manages are deliberately file-only and have no form
            // field — WeaponForge:ArtifactRoot, Wiki:IndexConnection — and a wholesale
            // replace deleted them on every save from the page.
            void Set(string name, object? section)
            {
                if (section is null) return;
                var incoming = JsonSerializer.SerializeToNode(section, JsonOpts);

                // Pull out any existing section, whatever its casing, so we never end
                // up with both "Wiki" and "wiki".
                var existingKey = root.FirstOrDefault(kv => string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)).Key;
                JsonNode? existing = null;
                if (existingKey is not null)
                {
                    existing = root[existingKey];
                    root.Remove(existingKey);
                }

                if (existing is JsonObject existingObj && incoming is JsonObject incomingObj)
                {
                    MergeInto(existingObj, incomingObj);
                    root[name] = existingObj;
                }
                else
                {
                    root[name] = incoming;
                }
            }

            Set("connectionStrings", settings.ConnectionStrings);
            Set("remoteAccess", settings.RemoteAccess);
            Set("vmangos", settings.Vmangos);
            Set("spellCreator", settings.SpellCreator);
            Set("weaponForge", settings.WeaponForge);
            Set("wiki", settings.Wiki);
            Set("kestrel", settings.Kestrel);

            var json = root.ToJsonString(JsonOpts);
            System.IO.File.WriteAllText(ConfigFilePath, json);
            _logger.LogInformation("Saved server-config.json to {Path}", ConfigFilePath);

            // Paths only — the values include the RA password and connection strings.
            var changedKeys = DiffKeys(before, root);
            await _audit.LogConfigChangeAsync(
                json,
                changedKeys.Count > 0 ? JsonSerializer.Serialize(changedKeys) : null);

            var message = changedKeys.Count == 0
                ? "No changes — server-config.json already matched the form."
                : $"Saved {changedKeys.Count} change(s) to server-config.json.";

            return Json(new
            {
                success = true,
                message,
                changedKeys,
                // The file as it now stands — what the client binds to.
                settings = ReadOverrideSettings(),
                fileStamp = ComputeStamp(),
                restartRequired = changedKeys.Count > 0,
                restartCommand = "sudo systemctl restart mangossuperui"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save server-config.json");
            return Json(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// Deletes the override file, reverting to appsettings.json defaults.
    /// </summary>
    [HttpPost]
    public IActionResult Reset()
    {
        try
        {
            if (System.IO.File.Exists(ConfigFilePath))
            {
                System.IO.File.Delete(ConfigFilePath);
                _logger.LogInformation("Deleted server-config.json");
            }
            return Json(new
            {
                success = true,
                message = "Override removed. Restart to revert to appsettings.json defaults.",
                fileStamp = (string?)null
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete server-config.json");
            return Json(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// Returns the live status of all ComfyUI nodes in the dispatcher pool.
    /// Used by the Settings page to show node health indicators.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ComfyPoolStatus()
    {
        if (_comfyDispatcher == null)
            return Json(Array.Empty<object>());

        var statuses = await _comfyDispatcher.GetPoolStatusAsync();
        return Json(statuses);
    }

    // ==================== File helpers ====================

    /// <summary>
    /// Short content hash of server-config.json, or null when there is no override file.
    /// Content-based rather than timestamp-based so two writes inside the same filesystem
    /// timestamp tick still compare as different.
    /// </summary>
    private string? ComputeStamp()
    {
        try
        {
            if (!System.IO.File.Exists(ConfigFilePath)) return null;
            var bytes = System.IO.File.ReadAllBytes(ConfigFilePath);
            return Convert.ToHexString(SHA256.HashData(bytes))[..16];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not stamp server-config.json");
            return null;
        }
    }

    /// <summary>Reads server-config.json into a ServerConfig, or null if absent/unreadable.</summary>
    private ServerConfig? ReadOverrideSettings()
    {
        if (!System.IO.File.Exists(ConfigFilePath)) return null;
        return JsonSerializer.Deserialize<ServerConfig>(System.IO.File.ReadAllText(ConfigFilePath), JsonOpts);
    }

    /// <summary>
    /// Overlays <paramref name="source"/> onto <paramref name="target"/> in place.
    /// Nested objects merge key by key (case-insensitively, adopting the source's
    /// casing); arrays and scalars replace outright. Keys present only in the target
    /// survive — that is what keeps the file-only advanced overrides alive.
    /// </summary>
    private static void MergeInto(JsonObject target, JsonObject source)
    {
        foreach (var kv in source.ToList())
        {
            var existingKey = target.FirstOrDefault(t => string.Equals(t.Key, kv.Key, StringComparison.OrdinalIgnoreCase)).Key;
            var existing = existingKey is null ? null : target[existingKey];

            if (existing is JsonObject existingObj && kv.Value is JsonObject sourceObj)
            {
                MergeInto(existingObj, sourceObj);
                if (existingKey != kv.Key)
                {
                    target.Remove(existingKey!);
                    target[kv.Key] = existingObj;
                }
                continue;
            }

            if (existingKey is not null) target.Remove(existingKey);
            target[kv.Key] = kv.Value?.DeepClone();
        }
    }

    /// <summary>
    /// Flattens a JSON tree to leaf path -> raw value, so two versions can be compared.
    /// </summary>
    private static void Flatten(JsonNode? node, string prefix, IDictionary<string, string> into)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var kv in obj)
                    Flatten(kv.Value, prefix.Length == 0 ? kv.Key : prefix + "." + kv.Key, into);
                break;
            case JsonArray arr:
                for (var i = 0; i < arr.Count; i++)
                    Flatten(arr[i], prefix + "[" + i + "]", into);
                break;
            default:
                into[prefix] = node?.ToJsonString() ?? "null";
                break;
        }
    }

    /// <summary>
    /// Leaf paths that differ between two config trees. Case-insensitive on key names so a
    /// section renamed from "Wiki" to "wiki" by the camelCase writer isn't reported as a
    /// change. Returns paths only — values carry the RA password and connection strings.
    /// </summary>
    private static List<string> DiffKeys(JsonNode? before, JsonNode? after)
    {
        var a = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var b = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Flatten(before, "", a);
        Flatten(after, "", b);

        var changed = new List<string>();
        foreach (var kv in b)
            if (!a.TryGetValue(kv.Key, out var old) || !string.Equals(old, kv.Value, StringComparison.Ordinal))
                changed.Add(kv.Key);
        foreach (var kv in a)
            if (!b.ContainsKey(kv.Key))
                changed.Add(kv.Key + " (removed)");

        changed.Sort(StringComparer.OrdinalIgnoreCase);
        return changed;
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // read-side: the live override is JSONC with mixed-case section names —
        // without these, Override() fails to parse the very file Save() writes to
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
}

// ==================== Config DTOs ====================

/// <summary>
/// Body of POST /Settings/Save. Settings is the form; ExpectedStamp is the fileStamp the
/// page was handed when it loaded, which lets the server detect a competing write instead
/// of silently overwriting it.
/// </summary>
public class SettingsSaveRequest
{
    public ServerConfig? Settings { get; set; }
    public string? ExpectedStamp { get; set; }
    /// <summary>Save anyway after a conflict was reported to the operator.</summary>
    public bool Force { get; set; }
}

public class ServerConfig
{
    public ConnectionStringsConfig? ConnectionStrings { get; set; }
    public RemoteAccessConfig? RemoteAccess { get; set; }
    public VmangosConfig? Vmangos { get; set; }
    public SpellCreatorConfig? SpellCreator { get; set; }
    public WeaponForgeConfig? WeaponForge { get; set; }
    public WikiConfig? Wiki { get; set; }
    public KestrelConfig? Kestrel { get; set; }
}

public class WeaponForgeConfig
{
    /// <summary>TBC (2.4.3) client Data folder — enables the Forges' TBC-import sections.
    /// (WeaponForge:ArtifactRoot stays a config-file-only advanced override.)</summary>
    public string TbcDataPath { get; set; } = "";
    /// <summary>WotLK (3.3.5a) client Data folder — enables the Forges' WotLK-import sections.</summary>
    public string WotlkDataPath { get; set; } = "";
}

public class ConnectionStringsConfig
{
    public string Mangos { get; set; } = "";
    public string Characters { get; set; } = "";
    public string Realmd { get; set; } = "";
    public string Logs { get; set; } = "";
    public string Admin { get; set; } = "";
}

public class RemoteAccessConfig
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 3443;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public int ReconnectDelayMs { get; set; } = 3000;
    public int CommandTimeoutMs { get; set; } = 5000;
}

public class VmangosConfig
{
    public string BinDirectory { get; set; } = "";
    public string RunDirectory { get; set; } = "";
    public string LogDirectory { get; set; } = "";
    public string ConfigDirectory { get; set; } = "";
    public string MangosdProcess { get; set; } = "mangosd";
    public string RealmdProcess { get; set; } = "realmd";
    public string MangosdConfPath { get; set; } = "";
    public string LogsDir { get; set; } = "";
    public string DbcPath { get; set; } = "";
    public string MapsDataPath { get; set; } = "";
    public string BackupDirectory { get; set; } = "";
    public string VmangosSourcePath { get; set; } = "";
    public string VmangosSqlPath { get; set; } = "";
    public string ExtractorsPath { get; set; } = "";
    public string ServerDataPath { get; set; } = "";
    public string ClientDataPath { get; set; } = "";
    // Optional explicit vmaps override for the World Editor's collision (VMaNGOS
    // extracted vmaps). Blank → falls back to ServerDataPath/vmaps. See
    // WorldEditorController.GetVmapsDirectory.
    public string VmapsDataPath { get; set; } = "";
}

public class SpellCreatorConfig
{
    public ComfyUIConfig ComfyUI { get; set; } = new();
    public OllamaConfig Ollama { get; set; } = new();
    public string RawBlpPath { get; set; } = "";
    public string DataPath { get; set; } = "";
    public string ClientM2Path { get; set; } = "";
    public string ClientDataPath { get; set; } = "";
    public string PatchOutputPath { get; set; } = "";
}

public class ComfyUIConfig
{
    public List<ComfyUINodeConfig> Nodes { get; set; } = new();
    public string ClipModel2 { get; set; } = "";
}

public class ComfyUINodeConfig
{
    public string Name { get; set; } = "";
    public string BaseUrl { get; set; } = "";
}

public class OllamaConfig
{
    public string BaseUrl { get; set; } = "";
    public string Model { get; set; } = "";
    public string VisionModel { get; set; } = "";
}

public class WikiConfig
{
    // Root of the generated documentation corpus the /Wiki page renders and indexes.
    // (Wiki:IndexConnection remains a config-file-only advanced override; it defaults
    // to the Admin connection string, which is already editable above.)
    public string Root { get; set; } = "";
}

public class KestrelConfig
{
    public string Url { get; set; } = "http://0.0.0.0:5000";
}