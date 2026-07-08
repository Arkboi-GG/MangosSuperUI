using System.Text.Json;
using System.Text.Json.Serialization;

namespace MangosSuperUI.BotLogic.Chat.Core;

/// <summary>
/// The fictional human behind a bot (CHAT_ARCHITECTURE §6.2 — authoritative schema).
/// Stored as bot_persona.card_json (post-jitter, evolves) and chat_voice.card_json
/// (library copy, frozen). Field contracts: caps ∈ lower|proper|mixed|CRUISE;
/// punctuation ∈ minimal|normal|heavy; abbrev_level 0–3; example_lines exactly 5 —
/// they are the few-shot anchors and matter more than the bio for small models.
/// </summary>
public class PersonaCard
{
    [JsonPropertyName("v")] public int V { get; set; } = 1;
    [JsonPropertyName("given_name")] public string GivenName { get; set; } = "";
    [JsonPropertyName("age")] public int Age { get; set; }
    [JsonPropertyName("region")] public string Region { get; set; } = "";
    [JsonPropertyName("timezone_offset")] public int TimezoneOffset { get; set; }
    [JsonPropertyName("occupation")] public string Occupation { get; set; } = "";
    [JsonPropertyName("life_situation_seed")] public string LifeSituationSeed { get; set; } = "";
    [JsonPropertyName("disposition")] public PersonaDisposition Disposition { get; set; } = new();
    [JsonPropertyName("interests")] public List<string> Interests { get; set; } = new();
    [JsonPropertyName("gaming_background")] public string GamingBackground { get; set; } = "";
    [JsonPropertyName("opinions")] public List<string> Opinions { get; set; } = new();
    [JsonPropertyName("typing")] public PersonaTyping Typing { get; set; } = new();
    [JsonPropertyName("example_lines")] public List<string> ExampleLines { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOpts);

    /// <summary>Null on unparseable input — callers fall back to a seed card and log.</summary>
    public static PersonaCard? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<PersonaCard>(json, JsonOpts); }
        catch { return null; }
    }
}

public class PersonaDisposition
{
    [JsonPropertyName("warmth")] public float Warmth { get; set; } = 0.5f;
    [JsonPropertyName("irritability")] public float Irritability { get; set; } = 0.3f;
    [JsonPropertyName("confidence")] public float Confidence { get; set; } = 0.5f;
    [JsonPropertyName("openness")] public float Openness { get; set; } = 0.5f;
    [JsonPropertyName("humor")] public string Humor { get; set; } = "dry";
}

public class PersonaTyping
{
    [JsonPropertyName("caps")] public string Caps { get; set; } = "lower";
    [JsonPropertyName("punctuation")] public string Punctuation { get; set; } = "minimal";
    [JsonPropertyName("abbrev_level")] public int AbbrevLevel { get; set; } = 2;
    [JsonPropertyName("typo_rate")] public float TypoRate { get; set; } = 0.04f;
    [JsonPropertyName("wpm")] public int Wpm { get; set; } = 45;
    [JsonPropertyName("think_min_s")] public int ThinkMinS { get; set; } = 2;
    [JsonPropertyName("think_max_s")] public int ThinkMaxS { get; set; } = 8;
    [JsonPropertyName("split_threshold_chars")] public int SplitThresholdChars { get; set; } = 90;
    [JsonPropertyName("alt_tab_chance")] public float AltTabChance { get; set; } = 0.05f;
    [JsonPropertyName("tics")] public List<string> Tics { get; set; } = new();
}
