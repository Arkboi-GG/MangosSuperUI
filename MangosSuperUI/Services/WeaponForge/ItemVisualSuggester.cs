using System.Numerics;
using System.Text.RegularExpressions;

namespace MangosSuperUI.Services.WeaponForge;

/// <summary>
/// Approximates later-client (TBC/WotLK) weapon particle effects with vanilla's enchant-style
/// <b>ItemVisual</b> glows. The 1.12 donor scaffold cannot host a later-client particle emitter graph,
/// but ItemDisplayInfo field 22 (ItemVisual → ItemVisuals.dbc → up to five ItemVisualEffects
/// models hung on the weapon's attachment points 0..4) is how vanilla itself gives weapons a
/// permanent glow (Ashbringer = RedGlow_Low, Thaurissan's hammer = YellowGlow_Low, Shaman fire /
/// frost / rock / wind totems…). The 1.12 client ships 34 visuals (measured): coloured glows in
/// low/high intensity, coloured flames, and a few specials.
///
/// There are two glow sources to approximate, and the forge reads both:
///
///   • <see cref="MapLaterClientVisual"/> — the source weapon's OWN ItemVisual, off its display row
///     (see <see cref="LegacyItemVisualIndex"/>). This is the common case and usually exact: the
///     later clients only append to 1.12's table, so most ids copy across untouched.
///   • <see cref="Suggest"/> — the source emitters' textures and colours (parsed into
///     <see cref="M2Model.ParticleEmitters"/> by both readers), matched to the nearest family:
///     frost-blue sparks ⇒ blue flame, fel-green smoke ⇒ green glow, and so on.
///
/// Either way it is a suggestion: the owner can pick any visual (or none) on the import card.
/// </summary>
public static class ItemVisualSuggester
{
    public enum Hue { None, Blue, Red, Yellow, White, Purple, Green, Black }

    /// <summary><paramref name="Stem"/> is the <c>ItemVisualEffects</c> model stem the row's slots
    /// name ("PurpleGlow_Low"). Matching later-client visuals by stem is exact where the two clients
    /// share an effect model, which is most of them.</summary>
    public sealed record VanillaVisual(uint Id, string Label, Hue Hue, bool Flame, bool High, string Stem);

    /// <summary>The 1.12 ItemVisuals.dbc rows that are plain weapon glows (ids measured on the local
    /// client; labels from their ItemVisualEffects model stems). Specials (SkullBalls 1, PoisonDrip 26,
    /// Sparkle 28, Rune_Intellect 30, Shaman totems 32/33/61/81/131–134) are listed for the picker
    /// but never auto-suggested.</summary>
    public static readonly IReadOnlyList<VanillaVisual> Catalog = new[]
    {
        new VanillaVisual(42, "Blue glow (low)", Hue.Blue, false, false, "BlueGlow_Low"),
        new VanillaVisual(2, "Blue glow (medium)", Hue.Blue, false, false, "BlueGlow_Med"),
        new VanillaVisual(24, "Blue glow (high)", Hue.Blue, false, true, "BlueGlow_High"),
        new VanillaVisual(27, "Blue flame", Hue.Blue, true, false, "BlueFlame_Low"),
        new VanillaVisual(31, "Red glow (low)", Hue.Red, false, false, "RedGlow_Low"),
        new VanillaVisual(101, "Red glow (high)", Hue.Red, false, true, "RedGlow_High"),
        new VanillaVisual(25, "Red flame", Hue.Red, true, false, "RedFlame_Low"),
        new VanillaVisual(29, "Yellow glow (low)", Hue.Yellow, false, false, "YellowGlow_Low"),
        new VanillaVisual(102, "Yellow glow (high)", Hue.Yellow, false, true, "YellowGlow_High"),
        new VanillaVisual(129, "Yellow flame", Hue.Yellow, true, false, "YellowFlame_Low"),
        new VanillaVisual(103, "White glow (low)", Hue.White, false, false, "WhiteGlow_Low"),
        new VanillaVisual(104, "White glow (high)", Hue.White, false, true, "WhiteGlow_High"),
        new VanillaVisual(126, "White flame", Hue.White, true, false, "WhiteFlame_Low"),
        new VanillaVisual(107, "Purple glow (low)", Hue.Purple, false, false, "PurpleGlow_Low"),
        new VanillaVisual(105, "Purple glow (high)", Hue.Purple, false, true, "PurpleGlow_High"),
        new VanillaVisual(128, "Purple flame", Hue.Purple, true, false, "PurpleFlame_Low"),
        new VanillaVisual(125, "Green glow (low)", Hue.Green, false, false, "GreenGlow_Low"),
        new VanillaVisual(106, "Green glow (high)", Hue.Green, false, true, "GreenGlow_High"),
        new VanillaVisual(127, "Green flame", Hue.Green, true, false, "GreenFlame_Low"),
        new VanillaVisual(123, "Black glow (low)", Hue.Black, false, false, "BlackGlow_Low"),
        new VanillaVisual(124, "Black glow (high)", Hue.Black, false, true, "BlackGlow_High"),
        new VanillaVisual(130, "Black flame", Hue.Black, true, false, "BlackFlame_Low"),
        new VanillaVisual(1, "Skull balls (special)", Hue.None, false, false, "SkullBalls"),
        new VanillaVisual(26, "Poison drip (special)", Hue.None, false, false, "PoisonDrip"),
        new VanillaVisual(28, "Sparkle (special)", Hue.None, false, false, "Sparkle_A"),
        new VanillaVisual(30, "Intellect rune + yellow glow (special)", Hue.None, false, false, "Rune_Intellect"),
        new VanillaVisual(32, "Shaman fire (totem)", Hue.None, false, false, "Shaman_Fire"),
        new VanillaVisual(33, "Shaman frost (totem)", Hue.None, false, false, "Shaman_Frost"),
        new VanillaVisual(61, "Shaman rock (totem)", Hue.None, false, false, "Shaman_Rock"),
        new VanillaVisual(81, "Shaman wind (totem)", Hue.None, false, false, "Shaman_Wind"),
        new VanillaVisual(131, "Shaman purple (totem)", Hue.None, false, false, "Shaman_Purple"),
        new VanillaVisual(132, "Shaman green (totem)", Hue.None, false, false, "Shaman_Green"),
        new VanillaVisual(133, "Shaman red (totem)", Hue.None, false, false, "Shaman_Red"),
        new VanillaVisual(134, "Shaman yellow (totem)", Hue.None, false, false, "Shaman_Yellow"),
    };

    public static VanillaVisual? Find(uint id) => Catalog.FirstOrDefault(v => v.Id == id);

    public sealed record Suggestion(uint ItemVisual, string Label, string Reason, string EmitterSummary);

    private static readonly Regex FlameWords = new(@"fire|flame|ember|smoke|lava|burn|torch|steam", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Pick the nearest vanilla glow for the source emitters; null when the model has none.</summary>
    public static Suggestion? Suggest(IReadOnlyList<M2ParticleEmitterInfo> emitters)
    {
        if (emitters is null || emitters.Count == 0) return null;

        // Vote per emitter: hue from the colour key when present, else from texture keywords.
        var votes = new Dictionary<Hue, float>();
        bool anyFlame = false;
        float maxScale = 0f;
        var texStems = new List<string>();
        foreach (var e in emitters)
        {
            string stem = e.TextureName is null ? "" : Path.GetFileNameWithoutExtension(e.TextureName);
            if (stem.Length > 0 && !texStems.Contains(stem, StringComparer.OrdinalIgnoreCase)) texStems.Add(stem);
            if (FlameWords.IsMatch(e.TextureName ?? "")) anyFlame = true;
            maxScale = MathF.Max(maxScale, e.Scale);

            Hue h = e.ColorRgb is { } c ? HueOf(c) : HueFromName(e.TextureName ?? "");
            if (h == Hue.None) continue;
            float weight = 1f + MathF.Min(e.Scale, 0.5f) * 4f;   // bigger particles dominate the look
            votes[h] = votes.GetValueOrDefault(h) + weight;
        }
        string summary = $"{emitters.Count} particle emitter(s)" + (texStems.Count > 0 ? $": {string.Join(", ", texStems.Take(4))}" : "");
        if (votes.Count == 0)
            return new Suggestion(0, "none", "emitter colours/textures gave no hue to match", summary);

        var hue = votes.OrderByDescending(v => v.Value).First().Key;
        bool high = emitters.Count >= 4 || maxScale >= 0.2f;
        var pick = Catalog.FirstOrDefault(v => v.Hue == hue && v.Flame == anyFlame && (v.Flame || v.High == high))
                   ?? Catalog.FirstOrDefault(v => v.Hue == hue && !v.Flame)
                   ?? Catalog.First(v => v.Hue == hue);
        string reason = $"dominant {hue.ToString().ToLowerInvariant()} " + (anyFlame ? "flame/smoke textures" : "glow") +
                        (high ? ", strong (many/large emitters)" : ", subtle");
        return new Suggestion(pick.Id, pick.Label, reason, summary);
    }

    // ── The source client's own ItemVisual ─────────────────────────────────────
    //
    // Emitters are only half the story. TBC/WotLK give most glowing weapons their glow through
    // ItemDisplayInfo.ItemVisual, exactly as vanilla does (see LegacyItemVisualIndex for the
    // measured counts). Both later clients only ever APPEND to 1.12's table, so:
    //
    //   • an id 1.12 already ships           ⇒ copy it across verbatim, pixel-identical result;
    //   • a later-client-only id (137–169)   ⇒ match its effect-model stems to a vanilla stem, then
    //                                          to a curated equivalent, then to the hue words in
    //                                          the name — and say which of the three it was.
    //
    // A stem the client uses for a spell-cast animation rather than a weapon glow (Fear_State_Head,
    // *_PreCast_*, ConjureItem…) maps to nothing on purpose: hanging a cast effect on a weapon
    // permanently looks broken, so the owner gets "no vanilla equivalent" and an empty picker
    // instead of a wrong glow.

    /// <summary>Later-client effect stems with a defensible vanilla equivalent, keyed by a word in
    /// the stem. Only entries whose name carries the hue are listed — anything else falls through to
    /// "no confident match" rather than being guessed at.</summary>
    private static readonly (string Word, Hue Hue, bool Flame, bool High, string Why)[] StemEquivalents =
    {
        ("icyenchant",   Hue.Blue,   false, true,  "Icy Enchant is a frost-blue weapon glow"),
        ("soulfrost",    Hue.Blue,   false, true,  "Soulfrost is a frost glow"),
        ("frozenrune",   Hue.Blue,   false, true,  "the rune-weapon frost glow"),
        ("sunfire",      Hue.Yellow, false, true,  "Sunfire is a warm yellow glow"),
        ("executioner",  Hue.Red,    false, true,  "the Executioner glow is red"),
        ("fel_fire",     Hue.Green,  true,  false, "fel fire is green flame"),
        ("felfire",      Hue.Green,  true,  false, "fel fire is green flame"),
        ("fire_blue",    Hue.Blue,   true,  false, "blue fire"),
        ("smoketrail",   Hue.Black,  true,  false, "a smoke trail reads as black flame"),
        ("infernal_smoke", Hue.Black, true, false, "infernal smoke reads as black flame"),
    };

    /// <summary>Stems that are spell-cast/state animations rather than permanent weapon glows.</summary>
    private static readonly Regex NotAWeaponGlow = new(
        @"_precast|_cast_|_state|conjureitem|detectmagic|dispel_|summon_|faeriefire|_missile",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Map a LATER client's ItemVisual (the id on the source weapon's display row) onto the
    /// nearest 1.12 one. <paramref name="effectStems"/> are the source visual's own
    /// ItemVisualEffects model stems, from <see cref="LegacyItemVisualIndex"/>. Returns null when the
    /// source row has no glow; a suggestion with id 0 when it has one that vanilla cannot express.</summary>
    public static Suggestion? MapLaterClientVisual(uint sourceId, IReadOnlyList<string>? effectStems)
    {
        if (sourceId == 0) return null;
        var stems = (effectStems ?? Array.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        string summary = $"source ItemVisual {sourceId}" + (stems.Count > 0 ? $" ({string.Join(" + ", stems.Take(3))})" : "");

        // 1) 1.12 ships this row already. "Same id" is only "same glow" if the effect models agree:
        //    3.3.5a reuses row 1 (vanilla's SkullBalls) for the Death Knight rune-weapon glow, so an
        //    id whose stems disagree with vanilla's falls through to be matched on its own merits.
        //    Stems can legitimately be empty (the source names effect ids its own DBC lacks), and an
        //    id with nothing to compare is taken at face value.
        if (Find(sourceId) is { } exact &&
            (stems.Count == 0 || stems.Contains(exact.Stem, StringComparer.OrdinalIgnoreCase)))
            return new Suggestion(exact.Id, exact.Label, "1.12 ships this exact ItemVisual — copied across unchanged", summary);

        // 2) The source names an effect model vanilla also has (SkullBalls, GreenFlame_Low, …).
        foreach (var stem in stems)
            if (Catalog.FirstOrDefault(v => string.Equals(v.Stem, stem, StringComparison.OrdinalIgnoreCase)) is { } byStem)
                return new Suggestion(byStem.Id, byStem.Label, $"the source's {stem} effect is a stock 1.12 effect", summary);

        // 3) A curated equivalent for the later clients' own enchant glows.
        foreach (var stem in stems)
            foreach (var (word, hue, flame, high, why) in StemEquivalents)
                if (stem.Contains(word, StringComparison.OrdinalIgnoreCase) && PickByHue(hue, flame, high) is { } curated)
                    return new Suggestion(curated.Id, curated.Label, $"no 1.12 equivalent — {why}", summary);

        // 4) A cast/state animation is not a weapon glow: refuse rather than hang one on permanently.
        if (stems.Count > 0 && stems.All(st => NotAWeaponGlow.IsMatch(st)))
            return new Suggestion(0, "none", $"the source effect ({stems[0]}) is a spell-cast animation, not a permanent weapon glow", summary);

        // 5) Last resort: the hue words in the stem name.
        foreach (var stem in stems)
        {
            Hue h = HueFromName(stem);
            if (h == Hue.None) continue;
            bool flame = FlameWords.IsMatch(stem);
            bool high = stem.Contains("high", StringComparison.OrdinalIgnoreCase) || stem.Contains("uber", StringComparison.OrdinalIgnoreCase);
            if (PickByHue(h, flame, high) is { } byName)
                return new Suggestion(byName.Id, byName.Label, $"nearest 1.12 hue for the source's {stem} effect", summary);
        }

        return new Suggestion(0, "none", "the source glow has no 1.12 equivalent", summary);
    }

    /// <summary>Nearest catalog row for a hue, preferring the requested flame/intensity variant.</summary>
    private static VanillaVisual? PickByHue(Hue hue, bool flame, bool high) =>
        Catalog.FirstOrDefault(v => v.Hue == hue && v.Flame == flame && (v.Flame || v.High == high))
        ?? Catalog.FirstOrDefault(v => v.Hue == hue && v.Flame == flame)
        ?? Catalog.FirstOrDefault(v => v.Hue == hue && !v.Flame)
        ?? Catalog.FirstOrDefault(v => v.Hue == hue);

    /// <summary>Classify an RGB (0–255) into the vanilla glow hues.</summary>
    public static Hue HueOf(Vector3 rgb)
    {
        float r = Math.Clamp(rgb.X, 0, 255) / 255f, g = Math.Clamp(rgb.Y, 0, 255) / 255f, b = Math.Clamp(rgb.Z, 0, 255) / 255f;
        float max = MathF.Max(r, MathF.Max(g, b)), min = MathF.Min(r, MathF.Min(g, b));
        if (max < 0.12f) return Hue.Black;
        float sat = max <= 0f ? 0f : (max - min) / max;
        if (sat < 0.18f) return max > 0.85f ? Hue.White : (max > 0.4f ? Hue.White : Hue.Black);
        float hDeg;
        if (max == r) hDeg = 60f * (((g - b) / (max - min)) % 6f);
        else if (max == g) hDeg = 60f * ((b - r) / (max - min) + 2f);
        else hDeg = 60f * ((r - g) / (max - min) + 4f);
        if (hDeg < 0) hDeg += 360f;
        return hDeg switch
        {
            < 20f or >= 335f => Hue.Red,
            < 45f => r > 0.8f && g > 0.5f ? Hue.Yellow : Hue.Red,   // orange → fire red unless pale
            < 75f => Hue.Yellow,
            < 165f => Hue.Green,
            < 255f => Hue.Blue,
            < 335f => Hue.Purple,
            _ => Hue.None,
        };
    }

    public static Hue HueFromName(string textureName)
    {
        string n = textureName.ToLowerInvariant();
        if (Regex.IsMatch(n, @"frost|ice|icy|snow|arcane|mana|blue|water|lightning")) return Hue.Blue;
        if (Regex.IsMatch(n, @"fire|flame|lava|ember|burn|red|blood|orange")) return Hue.Red;
        if (Regex.IsMatch(n, @"fel|poison|nature|acid|green|leaf")) return Hue.Green;
        if (Regex.IsMatch(n, @"shadow|void|purple|violet|arcane_purple|dark(?!en)")) return Hue.Purple;
        if (Regex.IsMatch(n, @"holy|light|yellow|gold|sun|divine")) return Hue.Yellow;
        if (Regex.IsMatch(n, @"white|glow|sparkle|star|flare")) return Hue.White;
        if (Regex.IsMatch(n, @"smoke|black|ash|dust")) return Hue.Black;
        return Hue.None;
    }
}
