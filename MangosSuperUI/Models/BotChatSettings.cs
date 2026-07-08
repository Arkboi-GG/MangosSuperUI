namespace MangosSuperUI.Models;

/// <summary>
/// "BotChat" configuration section — infrastructure seeds for the AiBot social layer.
/// The GitHub-shipped appsettings.json carries an EMPTY InferenceProfiles array;
/// operator endpoints/models live in server-config.json (the local overlay), exactly
/// like the SpellCreator section. BotBrainDbInit seeds these into
/// chat_inference_profile with INSERT IGNORE: rows are created once and never
/// overwritten, so Capacity-page edits always win over config afterwards.
/// If no profiles are configured, chat generation stays off until one is created on
/// the Chat Capacity page — a clean cold-start for fresh installs.
/// </summary>
public class BotChatSettings
{
    public List<InferenceProfileSeed> InferenceProfiles { get; set; } = new();
}

public class InferenceProfileSeed
{
    public string Name { get; set; } = "";
    public string EndpointUrl { get; set; } = "";
    /// <summary>'ollama' → /api/generate; 'openai' → /v1/chat/completions (vLLM etc.).</summary>
    public string ApiFlavor { get; set; } = "ollama";
    /// <summary>On an 'openai' profile, empty tags auto-resolve from /v1/models at runtime.</summary>
    public string ModelReactive { get; set; } = "";
    public string ModelAmbient { get; set; } = "";
    /// <summary>Empty = batch lane disabled on this profile.</summary>
    public string ModelBatch { get; set; } = "";
    public int CtxBudgetTokens { get; set; } = 3000;
    public int Concurrency { get; set; } = 2;
    public int ReactiveReserved { get; set; } = 1;
    public float AmbientRateMult { get; set; } = 1.0f;
    /// <summary>Honored on first seed only (INSERT IGNORE) — the Capacity page owns it after.</summary>
    public bool Active { get; set; } = false;
}
