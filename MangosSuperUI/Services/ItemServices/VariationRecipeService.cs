using System.Text;
using System.Text.Json;

namespace MangosSuperUI.Services;

/// <summary>
/// Generates COHERENT color-swap recipes for the variation mode.
///
/// The model never touches pixels and never measures the image — it does pure
/// creative ideation, which is its genuine strength. We hand it:
///   - a loose theme ("corrupted", "frost", "blood", or "surprise me")
///   - the list of color families actually present in the texture (detected
///     deterministically by PaletteSwapService.DetectFamilies — no vision)
///
/// It returns N recipes. Each recipe is a set of family→color swaps that form
/// a coherent palette (frost = blues/silvers/white; fel = greens/black/sickly
/// yellow; etc.). Those recipes are then executed by the proven deterministic
/// brute-force engine, optionally finished with a Flux cohesion pass.
///
/// If the model is unavailable or returns garbage, a built-in set of themed
/// palettes is used as a fallback so the feature always produces results.
///
/// ── HIGHLIGHT-FAMILY INJECTION (see GenerateRecipesAsync) ──────────────
/// DetectFamilies drops any family under 2% of pixels as noise. The problem:
/// the BRIGHTEST pixels on a texture — the spec highlights — are often a tiny
/// fraction of the area (well under 2%) yet are the most visually prominent
/// part of the item. On Ironfoe those are the icy cyan/white core of the
/// blade glow. If the recipe has no swap for "white"/"blue", the per-pixel
/// engine leaves those pixels UNTOUCHED (first-match-wins, no matching family),
/// so they keep their original icy look while everything around them recolors —
/// the "upper-right quadrant stayed blue/white" artifact.
///
/// Fix: we ALWAYS guarantee "white" and "blue" appear in the family list handed
/// to the recipe generator (LLM and fallback both), flagged as highlight/accent
/// families so they get a theme-coherent target (typically the BRIGHTEST tone
/// of the new palette). The brute-force engine then has a target for the
/// highlight core no matter how few pixels it occupies.
/// </summary>
public class VariationRecipeService
{
    private readonly IConfiguration _config;
    private readonly ILogger<VariationRecipeService> _logger;
    private readonly HttpClient _http;
    private readonly VramManager _vram;
    private readonly PaletteSwapService _palette;

    /// <summary>
    /// Families we ALWAYS ensure have a swap target, even when DetectFamilies
    /// drops them below the 2% noise floor. These are the high-lightness /
    /// high-saturation accent families that form the spec-highlight core —
    /// tiny in area, huge in visual weight. Without a target they're left
    /// untouched by the per-pixel engine and visually "escape" the recolor.
    /// </summary>
    private static readonly string[] HighlightFamilies = { "white", "blue" };

    public VariationRecipeService(IConfiguration config, ILogger<VariationRecipeService> logger,
        VramManager vram, PaletteSwapService palette)
    {
        _config = config;
        _logger = logger;
        _vram = vram;
        _palette = palette;
        // HTTP timeout must EXCEED the per-call cancel cap, or the HTTP layer
        // cuts the request off before our token does. Margin of 30s over the
        // configured LLM timeout (default 300+30 = 330s).
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(LlmTimeoutSeconds + 30)
        };
    }

    private string OllamaBaseUrl => (_config["SpellCreator:Ollama:BaseUrl"] ?? "").TrimEnd('/');
    private string TextModel => _config["SpellCreator:Ollama:Model"] ?? "";

    /// <summary>
    /// Seconds to allow per LLM generate call. Must absorb a COLD model load
    /// (a 20-40GB model can take 30-60s to load from disk) PLUS generation, so
    /// the default is generous. Configurable via SpellCreator:Ollama:TimeoutSeconds.
    /// </summary>
    private int LlmTimeoutSeconds =>
        int.TryParse(_config["SpellCreator:Ollama:TimeoutSeconds"], out var t) && t > 0 ? t : 300;

    /// <summary>
    /// keep_alive for generate calls. Returns a NUMBER (int) when the config
    /// value is purely numeric, and a STRING when it's a duration like "30m".
    /// This matters: Ollama accepts keep_alive as a number (-1 = resident
    /// forever, 0 = unload after, 3600 = seconds) OR a duration string ("30m",
    /// "-1m"). A bare quoted "-1" with no unit is an INVALID duration and Ollama
    /// rejects it with 400 Bad Request — which is exactly what we hit. So we
    /// emit -1 as the integer -1, not the string "-1".
    /// Configurable via SpellCreator:Ollama:KeepAlive (default "-1" → int -1).
    /// </summary>
    private object LlmKeepAlive
    {
        get
        {
            var raw = _config["SpellCreator:Ollama:KeepAlive"];
            if (string.IsNullOrWhiteSpace(raw)) return -1;          // default: resident forever
            if (int.TryParse(raw.Trim(), out var secs)) return secs; // numeric → number
            return raw.Trim();                                       // duration string → string
        }
    }

    /// <summary>
    /// Token budget per generate call. CRITICAL for reasoning models: the
    /// model's thinking tokens count against this budget, so a value too low
    /// means the model exhausts it mid-thought and returns an EMPTY response —
    /// exactly the bug we hit (the model thinks verbosely, hits the cap, never
    /// emits the answer). Default high (4096) so thinking finishes AND leaves
    /// room for the JSON. Configurable via SpellCreator:Ollama:NumPredict.
    /// </summary>
    private int LlmNumPredict =>
        int.TryParse(_config["SpellCreator:Ollama:NumPredict"], out var n) && n > 0 ? n : 4096;

    public bool LlmAvailable =>
        !string.IsNullOrEmpty(OllamaBaseUrl) && !string.IsNullOrEmpty(TextModel);

    /// <summary>
    /// Produce N swap recipes for the given theme and detected families.
    /// Each recipe is rendered as an instruction string the brute-force engine
    /// already understands ("grey for frost blue, gold for silver, ...").
    /// </summary>
    public async Task<List<VariationRecipe>> GenerateRecipesAsync(
        string theme, List<DetectedFamily> families, int count, CancellationToken ct = default)
    {
        var familyNames = families.Select(f => f.Family).Distinct().ToList();
        if (familyNames.Count == 0)
            familyNames = new List<string> { "grey", "gold", "brown" };

        // ── Always guarantee the highlight families have a target ──
        // DetectFamilies may have dropped "white"/"blue" below the 2% floor,
        // but the bright spec-highlight core lives there and MUST get recolored.
        // Track which families were injected (vs genuinely detected) so we can
        // tell the LLM to treat them as the brightest accent of the palette.
        var injectedHighlights = new List<string>();
        foreach (var hf in HighlightFamilies)
        {
            if (!familyNames.Contains(hf, StringComparer.OrdinalIgnoreCase))
            {
                familyNames.Add(hf);
                injectedHighlights.Add(hf);
            }
        }
        if (injectedHighlights.Count > 0)
            _logger.LogInformation(
                "VariationRecipe: Injected highlight families [{List}] (sub-threshold spec-highlight core)",
                string.Join(", ", injectedHighlights));

        if (LlmAvailable)
        {
            try
            {
                var llm = await GenerateWithLlmAsync(theme, familyNames, injectedHighlights, count, ct);
                if (llm.Count > 0) return llm;
            }
            catch (Exception ex)
            {
                _logger.LogInformation("VariationRecipe: LLM generation failed ({Err}), using fallback", ex.Message);
            }
        }

        return FallbackRecipes(theme, familyNames, injectedHighlights, count);
    }

    /// <summary>
    /// Turn a USER'S free-text recolor instruction into ONE coherent family→target
    /// swaps map, using the LLM as the parser. This is the "AI helps the recolor"
    /// path for the Palette Swap box: the model reads the literal intent
    /// ("greys → marble white, reddish-browns → steel, yellows → obsidian") plus
    /// the families actually in the texture, and returns a single clean mapping —
    /// deduping conflicts (a family named in two clauses gets ONE target),
    /// normalizing loose target phrases ("a dark stone obsidian" → "obsidian
    /// black"), and covering EVERY detected family so nothing is left unrecolored.
    ///
    /// Unlike GenerateRecipesAsync (which invents N THEMED variants), this honors
    /// the user's stated colors as closely as the palette allows. Returns the
    /// rendered VariationRecipe (its .Instruction feeds the brute-force engine
    /// unchanged), or null if the LLM is unavailable / returns nothing — callers
    /// then fall back to the regex ParseInstruction.
    /// </summary>
    public async Task<VariationRecipe?> GenerateSwapsFromInstructionAsync(
        string instruction, List<DetectedFamily> families, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(instruction)) return null;
        if (!LlmAvailable) return null;

        var familyNames = families.Select(f => f.Family).Distinct().ToList();
        if (familyNames.Count == 0)
            familyNames = new List<string> { "grey", "gold", "brown" };
        // Include the spec-highlight families so the model gives them a target too.
        foreach (var hf in HighlightFamilies)
            if (!familyNames.Contains(hf, StringComparer.OrdinalIgnoreCase))
                familyNames.Add(hf);

        string familyList = string.Join(", ", familyNames);

        // Plain concatenation (literal JSON braces in the example would collide
        // with C# interpolation).
        string systemPrompt =
            "You translate a user's recolor request into a precise color mapping for a\n" +
            "fantasy game weapon texture. A deterministic engine will apply your mapping by\n" +
            "recoloring whole color families — it CANNOT split one family into two colors,\n" +
            "and it preserves each pixel's lightness (so pick hues/tones, not brightness).\n\n" +
            "The texture contains exactly these color families: " + familyList + ".\n\n" +
            "Rules:\n" +
            "1. Map EVERY family in the list to a target color — none may be omitted.\n" +
            "2. Honor the user's request as closely as possible. Map their described\n" +
            "   source colors onto the families above (e.g. 'reddish-brown' and 'adjacent\n" +
            "   dark browns' both fall under the 'brown' family; 'steels' and 'adjacent\n" +
            "   blues' fall under 'grey'/'blue').\n" +
            "3. If the user maps the SAME family to two different colors across their\n" +
            "   request, CHOOSE THE ONE that best fits their dominant intent and use it\n" +
            "   once — never emit a family twice.\n" +
            "4. Normalize loose target phrases to a simple color name the engine knows\n" +
            "   ('a dark stone obsidian' → 'obsidian black'; 'a marble white theme' →\n" +
            "   'marble white'; 'a steel' → 'steel').\n" +
            "5. For any family the user DIDN'T mention, pick a target that stays coherent\n" +
            "   with the palette they described (don't leave it jarring).\n" +
            "6. Prefer these tonal/neutral names when they fit the user's words:\n" +
            "   'marble white', 'obsidian black', 'steel', 'gunmetal', 'silver', 'gold'.\n\n" +
            "Output ONLY a JSON object, no markdown, no explanation:\n" +
            "  {\"name\": \"<short name>\", \"swaps\": {\"<family>\": \"<target color>\", ...}}\n" +
            "Every family in [" + familyList + "] must be a key in swaps.";

        var body = JsonSerializer.Serialize(new
        {
            model = TextModel,
            prompt = "User recolor request:\n\"" + instruction.Trim() + "\"",
            system = systemPrompt,
            stream = false,
            think = false,
            keep_alive = LlmKeepAlive,
            // Lower temperature than the variant generator — we want faithful
            // intent, not creative spread.
            options = new { temperature = 0.3, num_predict = LlmNumPredict }
        });

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(LlmTimeoutSeconds));

            var resp = await _http.PostAsync($"{OllamaBaseUrl}/api/generate",
                new StringContent(body, Encoding.UTF8, "application/json"), cts.Token);
            resp.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(cts.Token));
            string? rawResp = doc.RootElement.GetProperty("response").GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(rawResp)) return null;

            var thinkEnd = rawResp.IndexOf("</think>", StringComparison.OrdinalIgnoreCase);
            if (thinkEnd >= 0) rawResp = rawResp[(thinkEnd + 8)..].Trim();
            rawResp = rawResp.Replace("```json", "").Replace("```", "").Trim();

            // Single object — grab the first {...}.
            int objStart = rawResp.IndexOf('{');
            int objEnd = rawResp.LastIndexOf('}');
            if (objStart < 0 || objEnd <= objStart) return null;
            string objText = rawResp.Substring(objStart, objEnd - objStart + 1);

            using var objDoc = JsonDocument.Parse(objText);
            var root = objDoc.RootElement;
            string name = root.TryGetProperty("name", out var n)
                ? (n.GetString() ?? "Custom recolor") : "Custom recolor";
            if (!root.TryGetProperty("swaps", out var sw) || sw.ValueKind != JsonValueKind.Object)
                return null;

            var swaps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in sw.EnumerateObject())
            {
                var fam = p.Name.Trim().ToLowerInvariant();
                var col = p.Value.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(fam) && !string.IsNullOrWhiteSpace(col))
                    swaps[fam] = col;
            }
            if (swaps.Count == 0) return null;

            _logger.LogInformation(
                "VariationRecipe: instruction→swaps produced {N} mappings: {List}",
                swaps.Count, string.Join(", ", swaps.Select(kv => $"{kv.Key}→{kv.Value}")));

            return new VariationRecipe(name, swaps, RenderInstruction(swaps));
        }
        catch (Exception ex)
        {
            _logger.LogInformation(
                "VariationRecipe: instruction→swaps failed ({Err})", ex.Message);
            return null;
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // LLM RECIPE GENERATION
    // ═══════════════════════════════════════════════════════════════════


    private async Task<List<VariationRecipe>> GenerateWithLlmAsync(
        string theme, List<string> families, List<string> highlightFamilies,
        int count, CancellationToken ct)
    {
        string familyList = string.Join(", ", families);
        string themeText = string.IsNullOrWhiteSpace(theme) ? "surprising but coherent" : theme;

        // If any highlight families were injected, add an explicit note so the
        // model assigns them the BRIGHTEST tone of the palette rather than a
        // random color — these pixels are the spec-highlight core of the item.
        string highlightNote = "";
        if (highlightFamilies.Count > 0)
        {
            string hl = string.Join(", ", highlightFamilies);
            highlightNote =
                "IMPORTANT: the families [" + hl + "] are the small but very bright\n" +
                "SPECULAR HIGHLIGHT core of the item (glints, edge glow, hot spots).\n" +
                "Assign them the BRIGHTEST, most luminous tone of your palette so they\n" +
                "still read as highlights — but keep them in the SAME palette family as\n" +
                "everything else (e.g. for a dark/obsidian theme, make them a pale steel\n" +
                "or bright silver, NOT icy blue; for a fel theme, a bright sickly yellow-\n" +
                "green). They must NOT keep their original icy/blue look.\n\n";
        }

        // Built with plain concatenation (no interpolated raw string) so the
        // literal JSON braces in the example don't collide with C# interpolation.
        string systemPrompt =
            "You design COHERENT color palettes for recoloring a fantasy game weapon texture.\n\n" +
            "The texture contains these color families: " + familyList + ".\n\n" +
            "For each variant, assign every family a NEW target color so the whole palette\n" +
            "looks intentional and coherent (like a real themed item - not random colors).\n" +
            "Use evocative but simple color names (\"frost blue\", \"bone white\", \"fel green\",\n" +
            "\"obsidian black\", \"blood crimson\", \"tarnished silver\", etc.).\n\n" +
            highlightNote +
            "Theme for the variants: \"" + themeText + "\".\n\n" +
            "Output ONLY a JSON array of " + count + " objects. Each object:\n" +
            "  {\"name\": \"<short variant name>\", \"swaps\": {\"<family>\": \"<target color>\", ...}}\n" +
            "Every family in [" + familyList + "] must appear in each swaps object.\n" +
            "No explanation, no markdown.";

        var body = JsonSerializer.Serialize(new
        {
            model = TextModel,
            prompt = $"Generate {count} coherent recolor variants for theme: {themeText}",
            system = systemPrompt,
            stream = false,
            think = false,               // verified on 201: think:false → clean JSON in
                                         // response, empty thinking, done_reason "stop".
                                         // Without it the model over-reasons and exhausts
                                         // num_predict before emitting the answer.
            keep_alive = LlmKeepAlive,
            options = new { temperature = 0.9, num_predict = LlmNumPredict }
        });

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(LlmTimeoutSeconds));

        var resp = await _http.PostAsync($"{OllamaBaseUrl}/api/generate",
            new StringContent(body, Encoding.UTF8, "application/json"), cts.Token);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(cts.Token));
        string? raw = doc.RootElement.GetProperty("response").GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(raw)) return new();

        var thinkEnd = raw.IndexOf("</think>", StringComparison.OrdinalIgnoreCase);
        if (thinkEnd >= 0) raw = raw[(thinkEnd + 8)..].Trim();
        raw = raw.Replace("```json", "").Replace("```", "").Trim();

        int arrStart = raw.IndexOf('[');
        int arrEnd = raw.LastIndexOf(']');
        if (arrStart < 0 || arrEnd <= arrStart) return new();
        string arrText = raw.Substring(arrStart, arrEnd - arrStart + 1);

        var recipes = new List<VariationRecipe>();
        try
        {
            using var arrDoc = JsonDocument.Parse(arrText);
            foreach (var el in arrDoc.RootElement.EnumerateArray())
            {
                string name = el.TryGetProperty("name", out var n) ? (n.GetString() ?? "Variant") : "Variant";
                if (!el.TryGetProperty("swaps", out var sw) || sw.ValueKind != JsonValueKind.Object) continue;
                var swaps = new Dictionary<string, string>();
                foreach (var p in sw.EnumerateObject())
                {
                    var fam = p.Name.Trim().ToLowerInvariant();
                    var col = p.Value.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(col)) swaps[fam] = col;
                }

                // Safety net: if the model omitted a highlight family despite the
                // instruction, fill it with a bright neutral so the spec core is
                // still recolored (never left to keep its original icy look).
                EnsureHighlightTargets(swaps, highlightFamilies);

                if (swaps.Count > 0)
                    recipes.Add(new VariationRecipe(name, swaps, RenderInstruction(swaps)));
            }
        }
        catch (JsonException ex)
        {
            _logger.LogInformation("VariationRecipe: JSON parse failed ({Err})", ex.Message);
            return new();
        }

        _logger.LogInformation("VariationRecipe: LLM produced {N} recipes for theme '{Theme}'",
            recipes.Count, themeText);
        return recipes;
    }

    /// <summary>
    /// Guarantee every highlight family has a target in the swaps dict. If the
    /// generator omitted one, assign a bright neutral derived from an existing
    /// swap (so it stays in-palette) — never leave a highlight family targetless,
    /// because that would let the spec-highlight core escape the recolor.
    /// </summary>
    private static void EnsureHighlightTargets(
        Dictionary<string, string> swaps, List<string> highlightFamilies)
    {
        if (highlightFamilies.Count == 0) return;

        foreach (var hf in highlightFamilies)
        {
            if (swaps.ContainsKey(hf)) continue;

            // Derive a bright accent from the palette already chosen. Prefer the
            // grey/silver target if present (its bright cousin), else fall back
            // to a generic bright neutral that still darkens away from icy blue.
            string accent =
                swaps.TryGetValue("grey", out var g) ? BrightenName(g) :
                swaps.TryGetValue("silver", out var s) ? BrightenName(s) :
                swaps.TryGetValue("steel", out var st) ? BrightenName(st) :
                "tarnished silver";
            swaps[hf] = accent;
        }
    }

    /// <summary>
    /// Map a base color name to a brighter, in-family relative for use as a
    /// highlight accent. Deliberately conservative: if we don't recognize the
    /// base, we return a bright silver, which reads as a highlight on any dark
    /// palette without reintroducing the icy-blue look we're trying to remove.
    /// </summary>
    private static string BrightenName(string baseColor)
    {
        var b = baseColor.Trim().ToLowerInvariant();
        if (b.Contains("obsidian") || b.Contains("black") || b.Contains("onyx") ||
            b.Contains("charcoal") || b.Contains("steel") || b.Contains("silver") ||
            b.Contains("grey") || b.Contains("gray"))
            return "bright silver";
        if (b.Contains("gold") || b.Contains("bronze") || b.Contains("amber"))
            return "pale gold";
        if (b.Contains("green") || b.Contains("fel") || b.Contains("emerald"))
            return "bright sickly green";
        if (b.Contains("red") || b.Contains("crimson") || b.Contains("blood"))
            return "bright ember red";
        if (b.Contains("purple") || b.Contains("violet") || b.Contains("void"))
            return "pale violet";
        // Unknown base — bright neutral, never icy blue.
        return "bright silver";
    }

    /// <summary>Render a swaps dict as an instruction string the brute force understands.</summary>
    private static string RenderInstruction(Dictionary<string, string> swaps)
    {
        // "grey for frost blue. gold for silver. brown for obsidian."
        return string.Join(". ", swaps.Select(kv => $"{kv.Key} for {kv.Value}")) + ".";
    }

    // ═══════════════════════════════════════════════════════════════════
    // FALLBACK THEMED PALETTES (no LLM)
    // ═══════════════════════════════════════════════════════════════════

    private List<VariationRecipe> FallbackRecipes(
        string theme, List<string> families, List<string> highlightFamilies, int count)
    {
        // A handful of hand-built coherent palettes keyed by theme keyword.
        // Each maps "role" → color; we then assign roles to the detected families
        // by their typical brightness (grey=neutral base, gold=accent, brown=dark).
        //
        // The palette arrays are ordered DARK → MID → BRIGHT. The brightest entry
        // (last) is reserved as the highlight accent so injected highlight
        // families (white/blue) get the luminous-but-in-palette tone, matching
        // the LLM-path behavior.
        var themed = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["frost"] = new[] { "deep navy", "tarnished silver", "frost blue" },
            ["fel"] = new[] { "black", "sickly fel green", "bright sickly green" },
            ["corrupted"] = new[] { "obsidian black", "void purple", "pale violet" },
            ["blood"] = new[] { "black", "blood crimson", "bone white" },
            ["holy"] = new[] { "azure blue", "bright gold", "ivory white" },
            ["shadow"] = new[] { "obsidian black", "void purple", "charcoal" },
            ["volcanic"] = new[] { "obsidian black", "ember red", "molten orange" },
            ["arcane"] = new[] { "royal", "violet", "azure blue" },
            // A generic dark theme so "make it dark/grey/black" requests behave —
            // dark base, steel mid, bright silver highlight (the Ironfoe case).
            ["dark"] = new[] { "obsidian black", "charcoal", "bright silver" },
        };

        // Pick palettes: if theme matches a key use it + neighbors, else cycle all.
        var keys = themed.Keys.ToList();
        var chosen = new List<string>();
        if (!string.IsNullOrWhiteSpace(theme))
        {
            foreach (var k in keys)
                if (theme.Contains(k, StringComparison.OrdinalIgnoreCase)) chosen.Add(k);
        }
        if (chosen.Count == 0) chosen = keys;

        // Non-highlight families get the dark→mid spread; highlight families get
        // the brightest entry. Split the family list so we can assign roles.
        var baseFamilies = families
            .Where(f => !highlightFamilies.Contains(f, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (baseFamilies.Count == 0) baseFamilies = families.ToList();

        var recipes = new List<VariationRecipe>();
        var rng = new Random();
        for (int i = 0; i < count; i++)
        {
            var key = chosen[i % chosen.Count];
            var palette = themed[key];
            string brightest = palette[palette.Length - 1];

            var swaps = new Dictionary<string, string>();

            // Base families spread across the dark→mid portion of the palette
            // (everything except the reserved brightest highlight tone). For a
            // 3-entry palette that's the first two entries; for longer palettes
            // it's all but the last.
            int baseCount = Math.Max(1, palette.Length - 1);
            for (int f = 0; f < baseFamilies.Count; f++)
                swaps[baseFamilies[f]] = palette[f % baseCount];

            // Highlight families always get the brightest in-palette tone so the
            // spec-highlight core recolors instead of keeping its icy look.
            foreach (var hf in highlightFamilies)
                swaps[hf] = brightest;

            // Slight shuffle of the BASE assignments for variety on repeat keys,
            // but never disturb the highlight→brightest mapping.
            if (i >= chosen.Count)
            {
                var shuffledBase = palette.Take(baseCount).OrderBy(_ => rng.Next()).ToArray();
                for (int f = 0; f < baseFamilies.Count; f++)
                    swaps[baseFamilies[f]] = shuffledBase[f % shuffledBase.Length];
                foreach (var hf in highlightFamilies)
                    swaps[hf] = brightest;
            }

            recipes.Add(new VariationRecipe(
                $"{char.ToUpper(key[0])}{key[1..]} {i + 1}", swaps, RenderInstruction(swaps)));
        }

        _logger.LogInformation("VariationRecipe: Fallback produced {N} recipes (theme '{Theme}')",
            recipes.Count, theme);
        return recipes;
    }

    // ═══════════════════════════════════════════════════════════════════
    // SEGMENTED (PER-UNIT) RECIPE GENERATION
    // ═══════════════════════════════════════════════════════════════════
    //
    // The region-aware path. Instead of broad color families, the caller has
    // already segmented the texture into UNITS (TextureSegmentationService) —
    // each a coherent material region with a mean color + descriptor. Here we
    // ask the model to assign a target color PER UNIT, so materials that share
    // a family but differ in tone (Ironfoe's straw handle vs amber ring vs gold
    // emblem) get independent targets and stay visually distinct.
    //
    // Returns N recipes; each maps unitId → target color NAME (the controller
    // resolves names to H/S via PaletteSwapService.ResolveToHs, then calls
    // TextureSegmentationService.RecolorByUnits).

    /// <summary>
    /// Generate N per-unit recolor recipes for the given theme and segmented
    /// units. Each unit arrives pre-labeled with its mean color so the model
    /// can target it independently.
    /// </summary>
    public async Task<List<SegmentedRecipe>> GenerateSegmentedRecipesAsync(
        string theme, List<SegmentUnitDto> units, int count, CancellationToken ct = default)
    {
        if (units.Count == 0) return new();

        if (LlmAvailable)
        {
            try
            {
                var llm = await GenerateSegmentedWithLlmAsync(theme, units, count, ct);
                if (llm.Count > 0) return llm;
            }
            catch (Exception ex)
            {
                _logger.LogInformation(
                    "VariationRecipe(seg): LLM generation failed ({Err}), using fallback", ex.Message);
            }
        }

        return FallbackSegmentedRecipes(theme, units, count);
    }

    private async Task<List<SegmentedRecipe>> GenerateSegmentedWithLlmAsync(
        string theme, List<SegmentUnitDto> units, int count, CancellationToken ct)
    {
        string themeText = string.IsNullOrWhiteSpace(theme) ? "surprising but coherent" : theme;

        // Group units into MATERIALS by hue+saturation (ignoring lightness).
        // The segmenter intentionally emits granular units — it splits a single
        // material into several brightness bands (steel highlight vs steel
        // shadow). We don't want to color those differently; that produced the
        // busy, incoherent result. So we cluster units that share hue+sat into a
        // material group and ask the model for ONE color per GROUP. Per-pixel
        // lightness preservation then reproduces each unit's shading naturally.
        var groups = GroupUnitsByMaterial(units);

        // Assign each group a ROLE from its coverage + lightness, so the model
        // reassigns colors by ROLE (dark→dark, accent→accent) across a NEW
        // palette per variant — rather than anchoring each material to its
        // ORIGINAL hue (which produced brown→brown, teal→teal: same intent but
        // no real color change). One group is tagged the HERO accent (the small,
        // most-saturated material — the emblem) which should always POP.
        var roles = AssignRoles(groups);

        // ── CONTRAST PRESERVATION ───────────────────────────────────────────
        // Measure how much hue/sat contrast the SOURCE groups already have.
        // This single number drives two behaviors (see ContrastSpec):
        //   • High source spread  → PRESERVE it (a scheme must reproduce ≳70%).
        //     Stops the "five shades of blue" collapse on items like Ironfoe
        //     that genuinely span cool steel + warm gold + warm leather.
        //   • Low source spread (a bland/monochrome item) → license to INVENT
        //     contrast (spruce it up by forcing groups apart on the wheel).
        // The group reps' mean H/S are the source colors we measure over,
        // coverage-weighted so a 2% sliver can't dominate the score.
        var spec = ComputeContrastSpec(groups);
        _logger.LogInformation(
            "VariationRecipe(seg): source contrast spread={Src:F3} → mode={Mode}, target≥{Tgt:F3}",
            spec.SourceSpread, spec.Invent ? "INVENT" : "PRESERVE", spec.TargetSpread);

        var sb = new StringBuilder();
        foreach (var grp in groups)
        {
            var members = grp.Value;
            float pct = members.Sum(u => u.Percent);
            var rep = members.OrderByDescending(u => u.Percent).First();
            string ids = string.Join(",", members.Select(u => u.Id));
            // Lead with ROLE; mention current color only as "currently" so the
            // model knows it may move OFF that hue.
            sb.Append($"  group {grp.Key} [{roles[grp.Key]}]: {pct:F0}% of item, " +
                      $"currently {rep.Descriptor} (lightness {rep.MeanL:F2}); covers units [{ids}]\n");
        }
        string groupList = sb.ToString();
        string groupIdList = string.Join(", ", groups.Keys);

        string systemPrompt =
            "You are recoloring a fantasy game weapon into " + count + " DISTINCT color schemes.\n\n" +
            "The weapon is made of these MATERIAL GROUPS, each tagged with its visual ROLE\n" +
            "(brightness variation inside a group is shading and is preserved automatically):\n" +
            groupList + "\n" +
            "CORE IDEA — same STRUCTURE, brand-new COLOR SCHEME each time:\n" +
            "• Do NOT keep a material near its current color. A group that is 'currently brown'\n" +
            "  or 'currently teal' SHOULD move to a completely new hue if the scheme calls for\n" +
            "  it (brown trim can become marble white, frost blue, blood red — anything).\n" +
            "• Assign each group ONE color. Same group = same color everywhere (its shading is\n" +
            "  kept). One color per group; never subdivide a group.\n" +
            "• Preserve ROLES by value/contrast: a 'dark' group stays among the darker colors,\n" +
            "  a 'base' group is the dominant mid tone, and the 'HERO accent' must always POP —\n" +
            "  give it the most vivid, eye-catching color of the scheme so it stands out.\n" +
            "• Make the groups in a scheme visibly DIFFERENT colors from each other (it should\n" +
            "  read as a designed object with distinct materials, not one flat tint).\n" +
            spec.PromptDirective +
            "• Make the " + count + " schemes visibly DIFFERENT FROM EACH OTHER — different hue\n" +
            "  families / moods, not the same palette reworded. If two schemes would look alike,\n" +
            "  push one to a different color family entirely.\n\n" +
            (string.IsNullOrWhiteSpace(theme)
                ? "Themes: invent a different evocative theme for each scheme (frost, fel, blood, volcanic, holy, void, gilded, etc.).\n\n"
                : "Anchor theme: \"" + themeText + "\". Give each scheme a DIFFERENT take on it.\n\n") +
            "Use simple evocative color names (\"frost blue\", \"bone white\", \"fel green\",\n" +
            "\"obsidian black\", \"blood crimson\", \"polished copper\", \"marble white\", etc.).\n\n" +
            "Output ONLY a JSON array of " + count + " objects. Each object:\n" +
            "  {\"name\": \"<short scheme name>\", \"groups\": {\"<groupId>\": \"<target color>\", ...}}\n" +
            "Every group id in [" + groupIdList + "] must appear in each groups object.\n" +
            "No explanation, no markdown.";

        // ── Make room on the GPU before loading the (large) text model ──
        // If the target model is already resident, this is a no-op. Otherwise
        // evict every other Ollama model and free ComfyUI VRAM so the model can
        // actually load instead of silently queueing/failing (which would fall
        // back to the deterministic path with nothing new on the GPU).
        if (!await _vram.IsModelLoadedAsync(TextModel, ct))
        {
            _logger.LogInformation(
                "VariationRecipe(seg): target model '{Model}' not resident — freeing VRAM", TextModel);
            await _vram.FreeForModelAsync(TextModel, ct);
        }

        var body = JsonSerializer.Serialize(new
        {
            model = TextModel,
            prompt = string.IsNullOrWhiteSpace(theme)
                ? $"Generate {count} distinct coherent color schemes."
                : $"Generate {count} distinct coherent color schemes themed around: {themeText}",
            system = systemPrompt,
            stream = false,
            think = false,               // verified on 201: think:false → clean JSON in
                                         // response, empty thinking, done_reason "stop".
                                         // The model otherwise over-reasons (11k+ chars)
                                         // and exhausts num_predict before the answer.
            keep_alive = LlmKeepAlive,
            // With think:false there's no reasoning to budget for, so this is
            // just headroom for the JSON answer itself.
            options = new { temperature = 1.0, num_predict = LlmNumPredict }
        });

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(LlmTimeoutSeconds));

        var resp = await _http.PostAsync($"{OllamaBaseUrl}/api/generate",
            new StringContent(body, Encoding.UTF8, "application/json"), cts.Token);
        resp.EnsureSuccessStatusCode();

        string respJson = await resp.Content.ReadAsStringAsync(cts.Token);
        using var doc = JsonDocument.Parse(respJson);
        var root = doc.RootElement;
        string? raw = root.TryGetProperty("response", out var rEl) ? rEl.GetString()?.Trim() : null;

        // Defensive: if "response" is empty but the model put content in a
        // "thinking" field (some Qwen3.x builds route the answer there even with
        // think:false), use that — the JSON array extraction below handles it.
        if (string.IsNullOrWhiteSpace(raw) &&
            root.TryGetProperty("thinking", out var tEl))
        {
            var think = tEl.GetString()?.Trim();
            if (!string.IsNullOrWhiteSpace(think)) raw = think;
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            // Log the full HTTP body so done_reason / field shape is visible.
            _logger.LogWarning(
                "VariationRecipe(seg): model returned EMPTY response. Body: {Body}",
                respJson.Length > 500 ? respJson[..500] + "…" : respJson);
            return new();
        }

        // Log the raw output (truncated) so we can see what the model actually
        // produced — a thinking model may emit reasoning/prose instead of clean
        // JSON, and the array-extraction below would then find nothing.
        _logger.LogInformation("VariationRecipe(seg): raw model output ({Len} chars): {Raw}",
            raw.Length, raw.Length > 600 ? raw[..600] + "…[truncated]" : raw);

        // The model often returns reasoning prose BEFORE the JSON, sometimes
        // without <think> tags, and that prose contains brackets ("Group 0
        // [base]", "units [1,2,3]"). A naive first-'['/last-']' grab lands on a
        // stray bracket inside the thinking and fails to parse. So strip any
        // <think> block we can, then find the actual JSON ARRAY-OF-OBJECTS: a
        // '[' followed (ignoring whitespace) by '{', with a balanced close.
        var thinkEnd = raw.IndexOf("</think>", StringComparison.OrdinalIgnoreCase);
        if (thinkEnd >= 0) raw = raw[(thinkEnd + 8)..].Trim();
        raw = raw.Replace("```json", "").Replace("```", "").Trim();

        string? arrText = ExtractJsonArrayOfObjects(raw);
        if (arrText == null)
        {
            _logger.LogWarning(
                "VariationRecipe(seg): no JSON array-of-objects found in model output: {Raw}",
                raw.Length > 400 ? raw[..400] + "…" : raw);
            return new();
        }

        var recipes = new List<SegmentedRecipe>();
        try
        {
            using var arrDoc = JsonDocument.Parse(arrText);
            int variantIndex = 0;
            foreach (var el in arrDoc.RootElement.EnumerateArray())
            {
                string name = el.TryGetProperty("name", out var n) ? (n.GetString() ?? "Variant") : "Variant";
                if (!el.TryGetProperty("groups", out var go) || go.ValueKind != JsonValueKind.Object) continue;

                // Parse group → color, then EXPAND to unit → color so every unit
                // in a group gets that group's single color. This is what makes
                // same-material units recolor identically.
                var groupColor = new Dictionary<int, string>();
                foreach (var p in go.EnumerateObject())
                {
                    if (!int.TryParse(p.Name.Trim(), out int gid)) continue;
                    if (!groups.ContainsKey(gid)) continue;
                    var col = p.Value.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(col)) groupColor[gid] = col;
                }

                // ── CONTRAST REPAIR ─────────────────────────────────────────
                // The prompt asks for spread, but the model still drifts toward
                // mono-hue "coherent" palettes (all-blue frost, all-orange decay).
                // Measure the scheme's actual spread; if it's below target, push
                // the most-clustered groups apart on the wheel (role-guided) until
                // it clears, or we run out of moves. Mutates groupColor in place.
                RepairContrast(name, groupColor, groups, roles, spec);

                var map = new Dictionary<int, string>();
                foreach (var grp in groups)
                {
                    if (!groupColor.TryGetValue(grp.Key, out var col))
                        col = grp.Value.OrderByDescending(u => u.Percent).First().Descriptor; // fallback
                    foreach (var u in grp.Value)
                        map[u.Id] = col;
                }

                // Backfill any unit the model skipped with its own descriptor as
                // a safe no-op-ish target (keeps it close to original rather than
                // leaving it untargeted, which RecolorByUnits would skip).
                foreach (var u in units)
                    if (!map.ContainsKey(u.Id))
                        map[u.Id] = u.Descriptor;

                // ── LIGHTNESS PLAN ──────────────────────────────────────────
                // Per-scheme brightness strategy: some variants preserve, some
                // INVERT (globally or selectively). This is the dimension hue-only
                // swaps can't reach — a dark-steel/bright-gold hammer can now also
                // render as bright-steel/dark-gold. Cycles across the gallery.
                var lightPlan = BuildLightnessPlan(variantIndex, groups, roles);
                if (lightPlan.Values.Any(v => v.L == LMode.Invert))
                    _logger.LogInformation(
                        "VariationRecipe(seg): '{Scheme}' (variant {Idx}) lightness plan includes INVERT",
                        name, variantIndex);

                if (map.Count > 0)
                    recipes.Add(new SegmentedRecipe(name, map, lightPlan));
                variantIndex++;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(
                "VariationRecipe(seg): JSON parse failed ({Err}) on text: {Text}",
                ex.Message, arrText.Length > 400 ? arrText[..400] + "…" : arrText);
            return new();
        }

        if (recipes.Count == 0)
            _logger.LogWarning(
                "VariationRecipe(seg): parsed array but produced 0 recipes (no valid 'groups' objects?)");
        else
            _logger.LogInformation("VariationRecipe(seg): LLM produced {N} recipes for theme '{Theme}'",
                recipes.Count, themeText);
        return recipes;
    }

    /// <summary>
    /// Fallback per-unit recipes (no LLM). Groups units into materials, assigns
    /// roles, and maps each variant to a DISTINCT themed palette: the HERO accent
    /// gets the palette's vivid 'pop' color, the rest spread dark→bright by role.
    /// Variants cycle different palettes so they don't samify even for one theme.
    /// </summary>
    private List<SegmentedRecipe> FallbackSegmentedRecipes(
        string theme, List<SegmentUnitDto> units, int count)
    {
        // Each palette: { dark, mid, bright, HERO-pop }. Ordered dark→bright for
        // the first three; the 4th is the vivid accent reserved for the hero.
        var themed = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["frost"] = new[] { "deep navy", "tarnished silver", "bone white", "frost blue" },
            ["fel"] = new[] { "black", "charcoal", "bone white", "fel green" },
            ["blood"] = new[] { "black", "charcoal", "bone white", "blood crimson" },
            ["holy"] = new[] { "tarnished silver", "ivory", "white", "bright gold" },
            ["shadow"] = new[] { "obsidian black", "charcoal", "tarnished silver", "void purple" },
            ["volcanic"] = new[] { "obsidian black", "charcoal", "ember red", "molten gold" },
            ["arcane"] = new[] { "midnight", "royal", "silver", "violet" },
            ["rust"] = new[] { "dark rust", "rust", "bronze", "copper" },
            ["marble"] = new[] { "charcoal", "stone", "white", "azure" },
            ["gilded"] = new[] { "obsidian black", "bronze", "ivory", "gold" },
        };

        var keys = themed.Keys.ToList();
        // Build the per-variant palette order. If a theme is named and matches a
        // key, lead with it but still cycle the others so variants differ. If it
        // doesn't match a key, still cycle all (gives variety). If blank, all.
        var order = new List<string>();
        if (!string.IsNullOrWhiteSpace(theme))
        {
            foreach (var k in keys)
                if (theme.Contains(k, StringComparison.OrdinalIgnoreCase)) order.Add(k);
        }
        foreach (var k in keys) if (!order.Contains(k)) order.Add(k);

        var groups = GroupUnitsByMaterial(units);
        var roles = AssignRoles(groups);

        // Non-hero groups ordered dark→bright for value-preserving spread.
        int heroId = roles.FirstOrDefault(kv => kv.Value == "HERO accent").Key;
        float GMeanL(int g) => groups[g].Sum(u => u.MeanL * u.Percent)
                               / Math.Max(0.001f, groups[g].Sum(u => u.Percent));
        var nonHero = groups.Keys.Where(g => g != heroId).OrderBy(GMeanL).ToList();
        bool hasHero = roles.ContainsValue("HERO accent");

        var recipes = new List<SegmentedRecipe>();
        for (int i = 0; i < count; i++)
        {
            var key = order[i % order.Count];
            var palette = themed[key];
            string heroColor = palette[palette.Length - 1];      // vivid pop
            int spreadN = palette.Length - 1;                    // dark→bright pool
            var map = new Dictionary<int, string>();

            // Hero pops with the vivid color.
            if (hasHero)
                foreach (var u in groups[heroId]) map[u.Id] = heroColor;

            // Everyone else spreads across the dark→bright pool by value rank.
            for (int g = 0; g < nonHero.Count; g++)
            {
                int idx = nonHero.Count <= 1
                    ? spreadN - 1
                    : (int)Math.Round((double)g / (nonHero.Count - 1) * (spreadN - 1));
                string color = palette[Math.Clamp(idx, 0, spreadN - 1)];
                foreach (var u in groups[nonHero[g]]) map[u.Id] = color;
            }

            recipes.Add(new SegmentedRecipe(
                $"{char.ToUpper(key[0])}{key[1..]} {i + 1}", map,
                BuildLightnessPlan(i, groups, roles)));
        }

        _logger.LogInformation("VariationRecipe(seg): Fallback produced {N} recipes (theme '{Theme}')",
            recipes.Count, theme);
        return recipes;
    }

    // ═══════════════════════════════════════════════════════════════════
    // CONTRAST PRESERVATION
    // ═══════════════════════════════════════════════════════════════════
    //
    // GOAL (from observed failure): items that genuinely span distant colors
    // — Ironfoe's cool steel (~210°) + warm gold (~45°) + warm-red leather
    // (~20°) — were rerolling into mono-hue palettes (five shades of indigo,
    // five shades of orange). Coherent in name, flat to the eye. The model
    // honored "different names" but not "spread across the wheel".
    //
    // FIX: measure the source's group spread once. Drive two behaviors from it:
    //   PRESERVE (source already contrasty): every scheme must reproduce ≳70%
    //            of that spread, else repair it.
    //   INVENT   (source bland/monochrome): no contrast to betray, so force a
    //            spread floor — license to spruce a plain item up.
    // Spread is a coverage-weighted mean pairwise HS-distance between groups.

    private const float BlandFloor = 0.18f;     // source spread below this = bland → invent
    private const float PreserveFrac = 0.70f;    // a scheme must hit ≥70% of source spread
    private const float InventTarget = 0.34f;    // forced spread when inventing
    private const float TargetCap = 0.55f;       // never demand more than this (avoids thrash)

    /// <summary>
    /// The contrast plan for one source texture: how much spread it has, the
    /// per-scheme target, whether we're preserving or inventing, and the prompt
    /// directive that communicates this to the model.
    /// </summary>
    private sealed class ContrastSpec
    {
        public float SourceSpread;
        public float TargetSpread;
        public bool Invent;
        public string PromptDirective = "";
    }

    /// <summary>
    /// Coverage-weighted mean H/S of a group (its representative color point).
    /// Hue averaged circularly. Used both for measuring source spread and for
    /// scoring a candidate scheme's resolved colors.
    /// </summary>
    private static (float H, float S) GroupMeanHs(List<SegmentUnitDto> members)
    {
        float wsum = 0, sx = 0, sy = 0, ssat = 0;
        foreach (var u in members)
        {
            float w = MathF.Max(0.01f, u.Percent);
            float rad = u.MeanH * MathF.PI / 180f;
            sx += w * MathF.Cos(rad);
            sy += w * MathF.Sin(rad);
            ssat += w * u.MeanS;
            wsum += w;
        }
        if (wsum <= 0) return (0, 0);
        float h = MathF.Atan2(sy / wsum, sx / wsum) * 180f / MathF.PI;
        h = ((h % 360f) + 360f) % 360f;
        return (h, ssat / wsum);
    }

    /// <summary>
    /// Distance between two colors in the same hue+sat space used everywhere
    /// else. Hue normalized to 0..1 (180° = max), saturation linear. Returns
    /// 0..~1.1. This is the single notion of "how different two colors look"
    /// the whole contrast system measures with.
    /// </summary>
    private static float HsDistance(float h1, float s1, float h2, float s2)
    {
        float dh = MathF.Abs(h1 - h2) % 360f;
        if (dh > 180f) dh = 360f - dh;
        dh /= 180f;
        float ds = MathF.Abs(s1 - s2);
        // Hue dominates (it's what reads as warm/cool tension); sat is a
        // secondary axis (steel-vs-gold is partly a saturation contrast).
        return MathF.Sqrt(0.75f * dh * dh + 0.45f * ds * ds);
    }

    /// <summary>
    /// Coverage-weighted mean pairwise HS-distance over a set of color points,
    /// each carrying a weight. This is the "spread score": ~0 when everything
    /// is one color, larger when colors fan across the wheel. Weighting by the
    /// PRODUCT of two groups' coverage keeps a tiny sliver from inflating (or
    /// deflating) the score — the spread that matters is between the regions
    /// you actually see.
    /// </summary>
    private static float SpreadScore(List<(float H, float S, float W)> pts)
    {
        if (pts.Count < 2) return 0f;
        float acc = 0, wsum = 0;
        for (int i = 0; i < pts.Count; i++)
            for (int j = i + 1; j < pts.Count; j++)
            {
                float w = pts[i].W * pts[j].W;
                acc += w * HsDistance(pts[i].H, pts[i].S, pts[j].H, pts[j].S);
                wsum += w;
            }
        return wsum <= 0 ? 0f : acc / wsum;
    }

    /// <summary>
    /// Measure the source groups' spread and build the contrast plan: preserve
    /// vs invent, the numeric target, and the prompt directive.
    /// </summary>
    private ContrastSpec ComputeContrastSpec(Dictionary<int, List<SegmentUnitDto>> groups)
    {
        var pts = new List<(float H, float S, float W)>();
        foreach (var grp in groups.Values)
        {
            var (h, s) = GroupMeanHs(grp);
            float cov = MathF.Max(0.01f, grp.Sum(u => u.Percent));
            pts.Add((h, s, cov));
        }

        float src = SpreadScore(pts);
        var spec = new ContrastSpec { SourceSpread = src };

        if (src < BlandFloor)
        {
            // Bland source — nothing to preserve, so manufacture tension.
            spec.Invent = true;
            spec.TargetSpread = InventTarget;
            spec.PromptDirective =
                "• This item's materials are CLOSE in color (a plain/monochrome piece). For each\n" +
                "  scheme, deliberately INTRODUCE contrast: spread the groups across DIFFERENT,\n" +
                "  well-separated hues (e.g. a cool base with a warm hero accent), so the result\n" +
                "  looks intentionally designed rather than flat.\n";
        }
        else
        {
            spec.Invent = false;
            spec.TargetSpread = MathF.Min(TargetCap, src * PreserveFrac);
            spec.PromptDirective =
                "• CRITICAL — this item already has strong color CONTRAST between its materials\n" +
                "  (e.g. a cool metal beside a warm gold beside a different-hued trim). Every\n" +
                "  scheme MUST keep that warm/cool tension: do NOT make all groups shades of one\n" +
                "  color. If the base is cool, push the hero accent WARM (or vice-versa), and keep\n" +
                "  at least one group clearly opposed on the color wheel. A 'frost' scheme is still\n" +
                "  allowed — but make it icy-steel + frost-GOLD + cold-VIOLET, not five blues.\n";
        }
        return spec;
    }

    // Role-keyed pools of well-separated, dictionary-resolvable colors, fanned
    // across the wheel. The repair pass pulls from these to push clustered
    // groups apart while respecting each group's value role. Every name here
    // resolves in PaletteSwapService's dictionary (verified against the logs).
    private static readonly string[] WarmPool =
        { "blood crimson", "burnt orange", "molten gold", "amber", "rust red", "copper" };
    private static readonly string[] CoolPool =
        { "frost blue", "deep indigo", "fel green", "teal", "royal blue", "violet" };
    private static readonly string[] NeutralPool =
        { "bone white", "charcoal", "tarnished silver", "obsidian black", "stone", "pearl white" };

    /// <summary>
    /// If a scheme's spread is below target, push the most-clustered groups apart
    /// on the wheel until it clears (or we exhaust candidates). Mutates
    /// <paramref name="groupColor"/> in place.
    ///
    /// Strategy, value-preserving:
    ///   1. Resolve the current scheme's group colors to H/S; score the spread.
    ///   2. While below target: find the group contributing LEAST to spread
    ///      (the one nearest its neighbours) that ISN'T the dominant base, and
    ///      reassign it to the pool color (warm/cool/neutral chosen to maximize
    ///      distance from the rest) that best fits its role — HERO/light → vivid,
    ///      dark → deep, base left alone as the anchor.
    ///   3. Re-score; stop when target met or no improving move remains.
    /// Unresolvable names are treated as max-distance unknowns (left in place).
    /// </summary>
    private void RepairContrast(
        string schemeName,
        Dictionary<int, string> groupColor,
        Dictionary<int, List<SegmentUnitDto>> groups,
        Dictionary<int, string> roles,
        ContrastSpec spec)
    {
        // Build the working point set from current assignments.
        (float H, float S)? Resolve(string name)
        {
            var hs = _palette.ResolveToHs(name);
            return hs;
        }

        var ids = groups.Keys.ToList();
        float Cov(int g) => MathF.Max(0.01f, groups[g].Sum(u => u.Percent));
        int baseG = ids.OrderByDescending(Cov).First();

        List<(float H, float S, float W)> CurrentPts()
        {
            var pts = new List<(float, float, float)>();
            foreach (var g in ids)
            {
                if (!groupColor.TryGetValue(g, out var nm)) continue;
                var hs = Resolve(nm);
                if (hs == null) continue;          // unknown → omit (treated as not pinning spread)
                pts.Add((hs.Value.H, hs.Value.S, Cov(g)));
            }
            return pts;
        }

        float score = SpreadScore(CurrentPts());
        if (score >= spec.TargetSpread)
        {
            _logger.LogInformation(
                "VariationRecipe(seg): '{Scheme}' spread={S:F3} ≥ target {T:F3} — OK",
                schemeName, score, spec.TargetSpread);
            return;
        }

        _logger.LogInformation(
            "VariationRecipe(seg): '{Scheme}' spread={S:F3} < target {T:F3} — repairing",
            schemeName, score, spec.TargetSpread);

        // Greedy: up to N moves. Each move re-colors the group that is currently
        // LEAST distinct (nearest to the weighted centroid of the others),
        // skipping the base anchor so the scheme keeps an identity.
        int maxMoves = Math.Max(1, ids.Count - 1);
        for (int move = 0; move < maxMoves && score < spec.TargetSpread; move++)
        {
            int worst = -1;
            float worstContribution = float.MaxValue;
            foreach (var g in ids)
            {
                if (g == baseG) continue;
                if (!groupColor.TryGetValue(g, out var nm)) continue;
                var hs = Resolve(nm);
                if (hs == null) continue;
                // contribution = mean distance from this group to all others
                float sum = 0; int cnt = 0;
                foreach (var o in ids)
                {
                    if (o == g) continue;
                    if (!groupColor.TryGetValue(o, out var onm)) continue;
                    var ohs = Resolve(onm);
                    if (ohs == null) continue;
                    sum += HsDistance(hs.Value.H, hs.Value.S, ohs.Value.H, ohs.Value.S);
                    cnt++;
                }
                float contrib = cnt == 0 ? 0 : sum / cnt;
                if (contrib < worstContribution) { worstContribution = contrib; worst = g; }
            }
            if (worst < 0) break;

            // Pick the replacement color that maximizes distance from the other
            // groups, drawn from the pool that fits this group's role.
            string role = roles.TryGetValue(worst, out var r) ? r : "secondary";
            string[] pool =
                role == "dark" ? NeutralPool :
                role == "highlight" ? CoolPool.Concat(WarmPool).ToArray() :
                role.StartsWith("HERO") ? WarmPool.Concat(CoolPool).ToArray() :
                CoolPool.Concat(WarmPool).Concat(NeutralPool).ToArray();

            // Other groups' current points (excluding the one we're moving).
            var others = new List<(float H, float S)>();
            foreach (var o in ids)
            {
                if (o == worst) continue;
                if (!groupColor.TryGetValue(o, out var onm)) continue;
                var ohs = Resolve(onm);
                if (ohs != null) others.Add((ohs.Value.H, ohs.Value.S));
            }

            string bestName = groupColor[worst];
            float bestMinDist = -1f;
            foreach (var cand in pool)
            {
                var chs = Resolve(cand);
                if (chs == null) continue;
                float minD = float.MaxValue;
                foreach (var o in others)
                    minD = MathF.Min(minD, HsDistance(chs.Value.H, chs.Value.S, o.H, o.S));
                if (others.Count == 0) minD = 1f;
                if (minD > bestMinDist) { bestMinDist = minD; bestName = cand; }
            }

            if (!string.Equals(bestName, groupColor[worst], StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation(
                    "VariationRecipe(seg): '{Scheme}' repair — group {G} [{Role}] '{Old}' → '{New}'",
                    schemeName, worst, role, groupColor[worst], bestName);
                groupColor[worst] = bestName;
            }
            else break;   // no improving move

            score = SpreadScore(CurrentPts());
        }

        _logger.LogInformation(
            "VariationRecipe(seg): '{Scheme}' post-repair spread={S:F3} (target {T:F3})",
            schemeName, score, spec.TargetSpread);
    }

    // ═══════════════════════════════════════════════════════════════════
    // MATERIAL GROUPING
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Build a per-UNIT lightness plan for variant <paramref name="variantIndex"/>.
    /// Across the gallery the plan cycles through strategies so the user sees the
    /// full range, not just hue rerolls:
    ///   slot 0 → PRESERVE all          (classic recolor, shading intact)
    ///   slot 1 → INVERT all            (global tonal flip — dark↔bright coherent)
    ///   slot 2 → INVERT hero only      (e.g. the gold emblem flips, rest normal)
    ///   slot 3 → INVERT base only      (the dominant field flips: dark steel → white)
    ///   slot 4 → INVERT everything-but-hero (field flips, accent stays anchored)
    ///   slot 5 → INVERT dark+highlight (deepen lights, lift darks — high drama)
    ///   slot 6+ → PRESERVE (fall back to safe recolors for the remainder)
    /// "all-invert" is the coherent "just inverted" look; the selective slots are
    /// the white-blade-with-obsidian-gold idea. Roles come from AssignRoles.
    /// Returns unitId → (LMode, param). A small +0.04 lift on inverted darks keeps
    /// inverted shadows from crushing to pure black.
    /// </summary>
    private static Dictionary<int, (LMode L, float Param)> BuildLightnessPlan(
        int variantIndex,
        Dictionary<int, List<SegmentUnitDto>> groups,
        Dictionary<int, string> roles)
    {
        var plan = new Dictionary<int, (LMode, float)>();
        if (groups.Count == 0) return plan;

        int slot = variantIndex % 7;
        int heroG = roles.FirstOrDefault(kv => kv.Value == "HERO accent").Key;
        float Cov(int g) => groups[g].Sum(u => u.Percent);
        int baseG = groups.Keys.OrderByDescending(Cov).First();

        bool InvertGroup(int g) => slot switch
        {
            0 => false,                                   // preserve all
            1 => true,                                    // invert all
            2 => g == heroG,                              // hero flips
            3 => g == baseG,                              // base flips
            4 => g != heroG,                              // everything but hero
            5 => roles.TryGetValue(g, out var r) && (r == "dark" || r == "highlight"),
            _ => false,                                   // preserve (safe remainder)
        };

        foreach (var g in groups.Keys)
        {
            var mode = InvertGroup(g) ? LMode.Invert : LMode.Preserve;
            // tiny lift on inversion stops inverted deep-shadows hitting pure black
            float param = mode == LMode.Invert ? 0.04f : 0f;
            foreach (var u in groups[g])
                plan[u.Id] = (mode, param);
        }
        return plan;
    }

    /// <summary>
    /// Find a JSON array-of-objects within arbitrary model text that may contain
    /// reasoning prose with stray brackets. Locates each '[' that is followed
    /// (ignoring whitespace) by '{', then scans forward tracking depth while
    /// respecting string literals/escapes to find the balanced ']'. Returns the
    /// LAST such balanced array (the real answer follows the thinking), or null.
    /// </summary>
    private static string? ExtractJsonArrayOfObjects(string text)
    {
        string? best = null;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '[') continue;
            // must be followed by optional whitespace then '{'
            int j = i + 1;
            while (j < text.Length && char.IsWhiteSpace(text[j])) j++;
            if (j >= text.Length || text[j] != '{') continue;

            // scan for the balanced close
            int depth = 0; bool inStr = false; bool esc = false;
            for (int k = i; k < text.Length; k++)
            {
                char c = text[k];
                if (esc) { esc = false; continue; }
                if (c == '\\') { esc = true; continue; }
                if (c == '"') { inStr = !inStr; continue; }
                if (inStr) continue;
                if (c == '[' || c == '{') depth++;
                else if (c == ']' || c == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        // candidate [i..k]; keep the last one that parses
                        var candidate = text.Substring(i, k - i + 1);
                        try { using var _ = JsonDocument.Parse(candidate); best = candidate; }
                        catch { /* not valid JSON, keep scanning */ }
                        break;
                    }
                }
            }
        }
        return best;
    }


    /// <summary>
    /// Cluster segmentation units into MATERIAL groups by hue+saturation,
    /// IGNORING lightness. The segmenter deliberately over-splits a material
    /// into brightness bands (steel highlight vs steel shadow); those share
    /// hue+sat and should recolor with one color (the per-pixel lightness
    /// preservation reproduces their shading). Single-link union-find with a
    /// hue+sat distance threshold. Returns groupId → member units, groupIds
    /// assigned by descending coverage so group 0 is the dominant material.
    /// </summary>
    private static Dictionary<int, List<SegmentUnitDto>> GroupUnitsByMaterial(List<SegmentUnitDto> units)
    {
        const float Thresh = 0.22f;   // validated on vanilla weapon atlases

        int n = units.Count;
        var parent = Enumerable.Range(0, n).ToArray();
        int Find(int a) { while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; } return a; }

        float HueDelta(float a, float b) { float d = MathF.Abs(a - b) % 360f; return d > 180f ? 360f - d : d; }
        float HsDist(SegmentUnitDto a, SegmentUnitDto b)
        {
            float dh = HueDelta(a.MeanH, b.MeanH) / 180f;
            float ds = MathF.Abs(a.MeanS - b.MeanS);
            float sw = MathF.Min(a.MeanS, b.MeanS);
            float wh = 0.4f + 0.4f * sw;          // hue weight floored (warm/cool always split)
            return MathF.Sqrt(wh * dh * dh + 0.9f * ds * ds);   // NO lightness term
        }

        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
                if (HsDist(units[i], units[j]) < Thresh)
                    parent[Find(i)] = Find(j);

        // Collect members per root.
        var byRoot = new Dictionary<int, List<SegmentUnitDto>>();
        for (int i = 0; i < n; i++)
        {
            int r = Find(i);
            if (!byRoot.TryGetValue(r, out var list)) { list = new(); byRoot[r] = list; }
            list.Add(units[i]);
        }

        // Reassign contiguous group ids by descending coverage.
        var ordered = byRoot.Values
            .OrderByDescending(members => members.Sum(u => u.Percent))
            .ToList();
        var result = new Dictionary<int, List<SegmentUnitDto>>();
        for (int g = 0; g < ordered.Count; g++) result[g] = ordered[g];
        return result;
    }

    /// <summary>
    /// Tag each material group with a visual ROLE so the recipe layer can ask
    /// the model to reassign colors by role (dark→dark, accent→accent) across a
    /// fresh palette, instead of anchoring each material to its original hue.
    ///
    /// Roles:
    ///   HERO accent — the small but most-SATURATED group (the emblem/glint);
    ///                 should always pop with the most vivid color.
    ///   base        — the largest-coverage group (dominant material).
    ///   dark        — low mean lightness.
    ///   highlight   — high mean lightness.
    ///   secondary   — everything else.
    /// A group can only hold one role; HERO and base take priority.
    /// </summary>
    private static Dictionary<int, string> AssignRoles(Dictionary<int, List<SegmentUnitDto>> groups)
    {
        var roles = new Dictionary<int, string>();
        if (groups.Count == 0) return roles;

        // Aggregate per-group stats.
        float Cov(int g) => groups[g].Sum(u => u.Percent);
        float MeanL(int g) => groups[g].Sum(u => u.MeanL * u.Percent) / Math.Max(0.001f, Cov(g));
        float MeanS(int g) => groups[g].Sum(u => u.MeanS * u.Percent) / Math.Max(0.001f, Cov(g));

        int baseG = groups.Keys.OrderByDescending(Cov).First();

        // HERO = most saturated group that is NOT the dominant base and is
        // reasonably small (an accent, not a field). Fall back to most saturated
        // overall if nothing small qualifies.
        int hero = groups.Keys
            .Where(g => g != baseG && Cov(g) <= 25f)
            .OrderByDescending(MeanS)
            .DefaultIfEmpty(groups.Keys.OrderByDescending(MeanS).First())
            .First();

        foreach (var g in groups.Keys)
        {
            if (g == hero) { roles[g] = "HERO accent"; continue; }
            if (g == baseG) { roles[g] = "base"; continue; }
            float l = MeanL(g);
            roles[g] = l < 0.30f ? "dark" : l > 0.70f ? "highlight" : "secondary";
        }
        return roles;
    }
}

/// <summary>One variant recipe: a name, the family→color map, and the rendered instruction.</summary>
public record VariationRecipe(string Name, Dictionary<string, string> Swaps, string Instruction);

/// <summary>
/// One per-unit (segmented) recipe: a name and a map of segmentation unit id →
/// target color NAME. The controller resolves names to H/S via
/// PaletteSwapService.ResolveToHs, then recolors via
/// TextureSegmentationService.RecolorByUnits.
/// </summary>
/// <summary>
/// One per-unit (segmented) recipe: a name and a map of segmentation unit id →
/// target color NAME. The controller resolves names to H/S via
/// PaletteSwapService.ResolveToHs, then recolors via
/// TextureSegmentationService.RecolorByUnits.
///
/// UnitLightness optionally carries a per-unit lightness behavior (Preserve by
/// default). When a unit maps to LMode.Invert, its tonal range is flipped on
/// recolor — enabling whole-texture or selective brightness inversion. Units
/// absent from the map preserve their lightness (the original behavior).
/// </summary>
public record SegmentedRecipe(
    string Name,
    Dictionary<int, string> UnitColors,
    Dictionary<int, (LMode L, float Param)>? UnitLightness = null);

/// <summary>Request to generate N themed recolor variants for an item.</summary>
public class VariationRequest
{
    public uint DisplayId { get; set; }
    public string OriginalMpqPath { get; set; } = "";
    public string OriginalBlpFilename { get; set; } = "";
    public string ItemName { get; set; } = "";
    public string Theme { get; set; } = "";
    public int Count { get; set; } = 4;
}

/// <summary>
/// Request to commit a chosen SEGMENTED variant. UnitColors is keyed by unit id
/// as a string (JSON object keys are strings) → target color name. The
/// controller re-segments (deterministic), re-renders this map, and commits the
/// exact PNG so the committed texture matches the preview.
/// </summary>
public class VariationApplyRequest
{
    public uint DisplayId { get; set; }
    public string ItemName { get; set; } = "";
    public string OriginalMpqPath { get; set; } = "";
    public string OriginalBlpFilename { get; set; } = "";
    public Dictionary<string, string> UnitColors { get; set; } = new();

    /// <summary>
    /// Optional per-unit lightness behavior echoed back from the preview so the
    /// committed texture matches the chosen card exactly. Keyed by unit id (string,
    /// JSON object keys are strings) → behavior name ("preserve" | "invert" |
    /// "lift" | "drop"). Absent/unknown = preserve. The param defaults to a small
    /// lift on invert (matching the preview) and is not transmitted separately.
    /// </summary>
    public Dictionary<string, string> UnitLightness { get; set; } = new();
}