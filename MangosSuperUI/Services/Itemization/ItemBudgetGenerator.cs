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
