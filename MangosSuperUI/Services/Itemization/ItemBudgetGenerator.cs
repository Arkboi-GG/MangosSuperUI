using MangosSuperUI.Services.WeaponForge;

namespace MangosSuperUI.Services.Itemization;

/// <summary>What the caller asks the generator for.</summary>
/// <param name="InventoryType">Vanilla equip slot (armor slot ids).</param>
/// <param name="ClassId">Intended class id (0 = none → generic distribution).</param>
/// <param name="ArchetypeKey">Intended archetype/spec key (null → class default).</param>
/// <param name="Level">Target required/character level (null or 0 → 60).</param>
/// <param name="Tier">Target vanilla tier for a level-60 item (null → untiered / level-based).</param>
public readonly record struct ItemBudgetRequest(
    int InventoryType, int ClassId, string? ArchetypeKey, int? Level, double? Tier);

/// <summary>One generated stat line.</summary>
public sealed record StatLine(int Type, string Label, int Value);

/// <summary>What the Weapon Forge asks the generator for.</summary>
/// <param name="InventoryType">Vanilla equip slot (13/17/21/22 melee, 15/25/26 ranged, 14 shield).</param>
/// <param name="FamilyKey">Weapon family key (WeaponTypeCatalog) — distinguishes wand from gun on
/// slot 26 and provides the family swing speed when no delay is given.</param>
/// <param name="ClassId">Intended class id (0 = none → generic distribution).</param>
/// <param name="ArchetypeKey">Intended archetype/spec key (null → class default).</param>
/// <param name="Level">Target required/character level (null or 0 → 60).</param>
/// <param name="Tier">Target vanilla tier for a level-60 item (null → untiered / level-based).</param>
/// <param name="DelayMs">Swing delay to budget the damage roll at (null → family default).</param>
public readonly record struct WeaponBudgetRequest(
    int InventoryType, string? FamilyKey, int ClassId, string? ArchetypeKey, int? Level, double? Tier, int? DelayMs);

/// <summary>The Weapon Forge's editable starting point: damage nestled into the vanilla DPS curve
/// plus the same stat/value budget shape the Armor Forge uses. Every field is a suggestion.</summary>
public sealed record WeaponBudgetDraft(
    int Quality, int ItemLevel, int RequiredLevel, long BuyPrice, long SellPrice, int? AllowableClass,
    double Dps, int DamageMin, int DamageMax, int DamageType, int DelayMs,
    int Armor, int Block, int MaxDurability,
    IReadOnlyList<StatLine> Stats, IReadOnlyList<string> EffectSuggestions, string Summary);

/// <summary>The editable starting point: a validated-shape gameplay config plus a display stat list,
/// on-equip effect suggestions, and a human-readable summary of where it sits on the curve.</summary>
public sealed record ItemBudgetDraft(
    VanillaItemBuildConfiguration Config,
    IReadOnlyList<StatLine> Stats,
    IReadOnlyList<string> EffectSuggestions,
    string Summary);

/// <summary>
/// Curated, deterministic itemization generator: given a slot + class/archetype + target level (and,
/// at 60, a vanilla tier), it nestles a stat/value budget into the real vanilla progression and shapes
/// its distribution to the spec. It is a STARTING POINT — the caller edits every field afterwards. It
/// never writes spell ids (on-equip effects are surfaced as suggestions the user fulfils from the
/// validated native catalog) and never touches the DB.
/// </summary>
public sealed class ItemBudgetGenerator
{
    public ItemBudgetDraft Generate(ItemBudgetRequest req)
    {
        var spec = SpecProfileCatalog.Resolve(req.ClassId, req.ArchetypeKey);
        int level = req.Level is > 0 ? Math.Clamp(req.Level.Value, 1, 60) : 60;

        double chestPoints;
        int ilvl, quality, requiredLevel;
        string band;

        if (req.Tier is double t && level >= 60)
        {
            chestPoints = TierAnchorTable.ChestPoints(t);
            ilvl = Math.Clamp(TierAnchorTable.ItemLevel(t), 1, 255);
            quality = t < 1.0 ? 3 : 4;          // dungeon sets Rare (blue); tier sets Epic (purple)
            requiredLevel = 60;
            band = $"T{t:0.#} · {TierBand(t)}";
        }
        else
        {
            // Sub-60 / untiered: budget scales with level; Uncommon by default (user edits).
            chestPoints = level * 0.45;
            ilvl = Math.Clamp(level, 1, 255);
            quality = level >= 55 ? 3 : 2;
            requiredLevel = level;
            band = $"level {level} gear";
        }

        double slot = TierAnchorTable.SlotFraction(req.InventoryType);
        double totalPoints = chestPoints * slot;

        var stats = DistributeStats(spec, totalPoints);

        long sell = ValueCopper(ilvl, quality);
        long buy = sell * 5;

        var config = new VanillaItemBuildConfiguration
        {
            Quality = quality,
            ItemLevel = ilvl,
            RequiredLevel = requiredLevel,
            BuyPrice = buy,
            SellPrice = sell,
            // Restrict to the chosen class (vanilla allowable_class = 1 << (classId-1)); the operator can
            // widen/clear it in the modal. Without this the itemized piece imported usable by everyone.
            AllowableClass = req.ClassId > 0 ? (1 << (req.ClassId - 1)) : (int?)null,
            Stats = stats.Select(s => new VanillaItemStatConfiguration { Type = s.Type, Value = s.Value }).ToList(),
        };

        string summary = $"{band}, {spec.Label}: {(int)Math.Round(totalPoints)} stat pts across {stats.Count} " +
                         $"stat(s) (slot ×{slot:0.###}); value ≈ {FormatCopper(sell)} sell.";

        return new ItemBudgetDraft(config, stats, spec.EffectHints.ToList(), summary);
    }

    /// <summary>
    /// The Weapon Forge's flavour of <see cref="Generate"/>: same tier curve, same spec-shaped stat
    /// distribution, plus a weapon damage roll nestled into the real vanilla DPS progression (2H is
    /// the reference; 1H/ranged/wand scale down; shields trade DPS for armor + block). Deterministic,
    /// read-only, and a STARTING POINT — the operator edits every field in the Configure-item modal.
    /// </summary>
    public WeaponBudgetDraft GenerateWeapon(WeaponBudgetRequest req)
    {
        var spec = SpecProfileCatalog.Resolve(req.ClassId, req.ArchetypeKey);
        int level = req.Level is > 0 ? Math.Clamp(req.Level.Value, 1, 60) : 60;

        double chestPoints, dps2h;
        int ilvl, quality, requiredLevel;
        string band;

        if (req.Tier is double t && level >= 60)
        {
            chestPoints = TierAnchorTable.ChestPoints(t);
            dps2h = TierAnchorTable.TwoHandDps(t);
            ilvl = Math.Clamp(TierAnchorTable.ItemLevel(t), 1, 255);
            quality = t < 1.0 ? 3 : 4;          // dungeon band Rare (blue); tier band Epic (purple)
            requiredLevel = 60;
            band = $"T{t:0.#} · {TierBand(t)}";
        }
        else
        {
            // Sub-60 / untiered: budgets scale with level; Uncommon by default (user edits).
            chestPoints = level * 0.45;
            dps2h = 4 + level * 0.78;           // level 60 ≈ 51 = the T0 anchor, so the curves meet
            ilvl = Math.Clamp(level, 1, 255);
            quality = level >= 55 ? 3 : 2;
            requiredLevel = level;
            band = $"level {level} gear";
        }

        // Weapon kind: family key wins (it distinguishes wand from gun on slot 26); slot is the fallback.
        var profile = WeaponTypeCatalog.Get(req.FamilyKey);
        bool familyKnown = !string.IsNullOrWhiteSpace(req.FamilyKey);
        bool shield = familyKnown ? profile.IsShield : req.InventoryType == 14;
        bool wand = familyKnown && string.Equals(profile.Key, "wand", StringComparison.OrdinalIgnoreCase);
        bool ranged = !wand && (familyKnown ? profile.IsRanged : req.InventoryType is 15 or 25 or 26);
        bool twoHand = !shield && !wand && !ranged && (familyKnown ? profile.TwoHanded : req.InventoryType == 17);

        // DPS scale per kind, measured against the same endgame drops as the 2H anchors:
        // 1H ≈ 0.78× (Vis'kag → Gressil), ranged ≈ 0.70× (Striker's Mark → Nerubian Slavemaker),
        // wands ≈ 0.85× (their whole budget is the bolt — no white swings to balance).
        double dpsFactor = shield ? 0 : twoHand ? 1.0 : wand ? 0.85 : ranged ? 0.70 : 0.78;
        double dps = dps2h * dpsFactor;

        int delayMs = req.DelayMs is > 0 ? Math.Clamp(req.DelayMs.Value, 100, 65535)
                    : familyKnown ? profile.DelayMs
                    : req.InventoryType switch { 17 => 3400, 15 => 2800, 25 => 2000, 26 => 2900, _ => 2600 };

        // Damage roll around the DPS mean with the classic ±~22% spread; wands shoot Shadow by default.
        double meanHit = dps * delayMs / 1000.0;
        int dmgMin = shield ? 0 : Math.Max(1, (int)Math.Round(meanHit * 0.78));
        int dmgMax = shield ? 0 : Math.Max(dmgMin + 1, (int)Math.Round(meanHit * 1.22));
        int damageType = wand ? 5 : 0;

        // Stat budget: weapons spend most of their budget on the damage roll, so the flat-stat
        // fraction is smaller than armor's slot weights. 2H carries a chest-sized budget.
        double slot = shield ? 0.5625 : twoHand ? 1.0 : (wand || ranged) ? 0.30 : 0.45;
        double totalPoints = chestPoints * slot;
        var stats = DistributeStats(spec, totalPoints);

        // Shields: armor + block on their own anchors (sub-60 approximates the levelling curve).
        int armor = 0, block = 0;
        if (shield)
        {
            armor = req.Tier is double st && level >= 60 ? TierAnchorTable.ShieldArmor(st) : 60 + level * 28;
            block = req.Tier is double sb && level >= 60 ? TierAnchorTable.ShieldBlock(sb) : Math.Max(1, (int)Math.Round(level * 0.55));
        }

        int durability = shield || twoHand ? 100 : wand ? 65 : ranged ? 75 : 90;
        if (req.Tier is >= 2.0 && level >= 60) durability += 20;

        // Rough value curve: weapons vendor noticeably above armor of the same ilvl.
        long sell = (long)Math.Round(ValueCopper(ilvl, quality) * (twoHand ? 2.0 : shield ? 1.0 : 1.5));
        long buy = sell * 5;

        int? allowableClass = req.ClassId > 0 ? (1 << (req.ClassId - 1)) : null;

        string kindLabel = shield ? "shield" : wand ? "wand" : ranged ? "ranged" : twoHand ? "two-hand" : "one-hand";
        string summary = shield
            ? $"{band}, {spec.Label}: {armor} armor · {block} block, {(int)Math.Round(totalPoints)} stat pts across {stats.Count} stat(s); value ≈ {FormatCopper(sell)} sell."
            : $"{band}, {spec.Label} {kindLabel}: {dps:0.#} DPS ({dmgMin}–{dmgMax} @ {delayMs / 1000.0:0.0}s), " +
              $"{(int)Math.Round(totalPoints)} stat pts across {stats.Count} stat(s); value ≈ {FormatCopper(sell)} sell.";

        return new WeaponBudgetDraft(quality, ilvl, requiredLevel, buy, sell, allowableClass,
            Math.Round(dps, 1), dmgMin, dmgMax, damageType, delayMs, armor, block, durability,
            stats, spec.EffectHints.ToList(), summary);
    }

    private static IReadOnlyList<StatLine> DistributeStats(SpecProfile spec, double totalPoints)
    {
        double sum = spec.Weights.Values.Sum();
        if (sum <= 0 || totalPoints <= 0) return Array.Empty<StatLine>();

        var lines = new List<StatLine>();
        foreach (var (type, w) in spec.Weights.OrderByDescending(kv => kv.Value))
        {
            int val = (int)Math.Round(totalPoints * (w / sum));
            if (val >= 1) lines.Add(new StatLine(type, StatTypes.Label(type), val));
        }
        return lines.Take(VanillaItemBuildConfigurationTranslator.MaxStatSlots).ToList();
    }

    // Rough monotonic value curve (copper). A starting point; the user edits it.
    private static long ValueCopper(int ilvl, int quality)
    {
        double qMult = quality switch { >= 4 => 3.0, 3 => 2.0, 2 => 1.0, _ => 0.5 };
        return (long)Math.Round((double)ilvl * ilvl * qMult);
    }

    private static string FormatCopper(long c)
    {
        long g = c / 10000; c %= 10000;
        long s = c / 100; long cop = c % 100;
        var parts = new List<string>();
        if (g > 0) parts.Add($"{g}g");
        if (s > 0) parts.Add($"{s}s");
        if (cop > 0 || parts.Count == 0) parts.Add($"{cop}c");
        return string.Join(" ", parts);
    }

    private static string TierBand(double t)
    {
        if (t <= 0.25) return "Dungeon Set (T0)";
        if (t < 1.0) return "Dungeon Set 2 band (T0.5)";
        if (t <= 1.25) return "Molten Core (T1)";
        if (t < 2.0) return "between MC (T1) and BWL (T2)";
        if (t <= 2.25) return "Blackwing Lair (T2)";
        if (t < 2.75) return "between BWL (T2) and AQ40 (T2.5)";
        if (t <= 3.0) return "AQ40–Naxxramas (T2.5–T3)";
        return "beyond Naxxramas (>T3)";
    }
}
