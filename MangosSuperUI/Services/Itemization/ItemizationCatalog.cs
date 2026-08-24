namespace MangosSuperUI.Services.Itemization;

/// <summary>Vanilla playable classes, valued as their class id (used for allowable_class masks).</summary>
public enum PlayerClass
{
    Warrior = 1,
    Paladin = 2,
    Hunter = 3,
    Rogue = 4,
    Priest = 5,
    Shaman = 7,
    Mage = 8,
    Warlock = 9,
    Druid = 11,
}

/// <summary>Coarse role, used for generic distribution and effect hints when a spec has no curated profile.</summary>
public enum RoleKind { MeleeDps, RangedDps, CasterDps, Tank, Healer }

/// <summary>The five direct stat types the item budget distributes over. Mana/Health (0/1) are valid
/// direct types but are not used for gear here.</summary>
public static class StatTypes
{
    public const int Agility = 3;
    public const int Strength = 4;
    public const int Intellect = 5;
    public const int Spirit = 6;
    public const int Stamina = 7;

    public static string Label(int t) => t switch
    {
        3 => "Agility", 4 => "Strength", 5 => "Intellect", 6 => "Spirit", 7 => "Stamina",
        0 => "Mana", 1 => "Health", _ => $"stat {t}"
    };
}

/// <summary>One class archetype (spec/playstyle) with the curated primary-stat weighting the budget
/// is distributed by, plus tier-agnostic on-equip effect hints (the actual spell is picked by the user
/// from the validated native catalog — the generator never fabricates spell ids).</summary>
public sealed record SpecProfile(
    string Key,
    string Label,
    RoleKind Role,
    IReadOnlyDictionary<int, double> Weights,
    IReadOnlyList<string> EffectHints);

/// <summary>Curated per-class archetype profiles. Weights are relative (normalized at distribution
/// time). Where a spec has a well-known itemization target (tank defense to the raid cap, caster spell
/// hit, DPS hit cap) it is surfaced as an effect hint for the user to fulfil from the spell catalog.</summary>
public static class SpecProfileCatalog
{
    // Small helpers to keep the table readable.
    private static Dictionary<int, double> W(double sta = 0, double str = 0, double agi = 0, double @int = 0, double spi = 0)
    {
        var d = new Dictionary<int, double>();
        if (sta > 0) d[StatTypes.Stamina] = sta;
        if (str > 0) d[StatTypes.Strength] = str;
        if (agi > 0) d[StatTypes.Agility] = agi;
        if (@int > 0) d[StatTypes.Intellect] = @int;
        if (spi > 0) d[StatTypes.Spirit] = spi;
        return d;
    }

    private static readonly IReadOnlyList<string> TankHints =
        ["+Defense on-equip toward the 440 (5.4%) raid crit-immunity cap", "+Dodge / +Block on-equip", "extra Stamina if you want more effective health"];
    private static readonly IReadOnlyList<string> MeleeDpsHints =
        ["+Hit on-equip toward the ~9% melee hit cap", "+Crit or +Attack Power on-equip"];
    private static readonly IReadOnlyList<string> RangedDpsHints =
        ["+Ranged Hit toward the ~9% cap", "+Ranged Attack Power or +Crit on-equip"];
    private static readonly IReadOnlyList<string> CasterHints =
        ["+Spell Damage on-equip", "+Spell Hit toward the 16% cap", "+Spell Crit on-equip"];
    private static readonly IReadOnlyList<string> HealerHints =
        ["+Healing Power on-equip", "+Mana per 5s (mp5) on-equip"];

    /// <summary>class id → ordered archetypes. First entry is the class default.</summary>
    private static readonly Dictionary<int, List<SpecProfile>> ByClass = new()
    {
        [(int)PlayerClass.Warrior] = new()
        {
            new("arms-fury", "Arms / Fury (DPS)", RoleKind.MeleeDps, W(sta: 0.35, str: 0.5, agi: 0.15), MeleeDpsHints),
            new("protection", "Protection (Tank)", RoleKind.Tank, W(sta: 0.6, str: 0.25, agi: 0.15), TankHints),
        },
        [(int)PlayerClass.Paladin] = new()
        {
            new("retribution", "Retribution (DPS)", RoleKind.MeleeDps, W(sta: 0.35, str: 0.45, agi: 0.1, @int: 0.1), MeleeDpsHints),
            new("protection", "Protection (Tank)", RoleKind.Tank, W(sta: 0.55, str: 0.2, agi: 0.1, @int: 0.15), TankHints),
            new("holy", "Holy (Healer)", RoleKind.Healer, W(sta: 0.3, @int: 0.45, spi: 0.25), HealerHints),
        },
        [(int)PlayerClass.Hunter] = new()
        {
            new("ranged", "Beast/Marks/Surv (DPS)", RoleKind.RangedDps, W(sta: 0.3, agi: 0.5, str: 0.1, @int: 0.1), RangedDpsHints),
        },
        [(int)PlayerClass.Rogue] = new()
        {
            new("combat", "Combat / Assassination (DPS)", RoleKind.MeleeDps, W(sta: 0.35, agi: 0.55, str: 0.1), MeleeDpsHints),
        },
        [(int)PlayerClass.Priest] = new()
        {
            new("shadow", "Shadow (Caster DPS)", RoleKind.CasterDps, W(sta: 0.35, @int: 0.4, spi: 0.25), CasterHints),
            new("holy-disc", "Holy / Discipline (Healer)", RoleKind.Healer, W(sta: 0.25, @int: 0.4, spi: 0.35), HealerHints),
        },
        [(int)PlayerClass.Shaman] = new()
        {
            new("enhancement", "Enhancement (Melee)", RoleKind.MeleeDps, W(sta: 0.3, str: 0.35, agi: 0.25, @int: 0.1), MeleeDpsHints),
            new("elemental", "Elemental (Caster DPS)", RoleKind.CasterDps, W(sta: 0.3, @int: 0.45, spi: 0.25), CasterHints),
            new("restoration", "Restoration (Healer)", RoleKind.Healer, W(sta: 0.3, @int: 0.4, spi: 0.3), HealerHints),
        },
        [(int)PlayerClass.Mage] = new()
        {
            new("caster", "Arcane/Fire/Frost (Caster DPS)", RoleKind.CasterDps, W(sta: 0.35, @int: 0.45, spi: 0.2), CasterHints),
        },
        [(int)PlayerClass.Warlock] = new()
        {
            new("caster", "Affliction/Demo/Destro (Caster DPS)", RoleKind.CasterDps, W(sta: 0.4, @int: 0.4, spi: 0.2), CasterHints),
        },
        [(int)PlayerClass.Druid] = new()
        {
            new("balance", "Balance / Moonkin (Caster DPS)", RoleKind.CasterDps, W(sta: 0.3, @int: 0.4, spi: 0.3), CasterHints),
            new("feral-cat", "Feral — Cat (DPS)", RoleKind.MeleeDps, W(sta: 0.35, agi: 0.45, str: 0.2), MeleeDpsHints),
            new("feral-bear", "Feral — Bear (Tank)", RoleKind.Tank, W(sta: 0.55, agi: 0.2, str: 0.25), TankHints),
            new("restoration", "Restoration (Healer)", RoleKind.Healer, W(sta: 0.3, @int: 0.4, spi: 0.3), HealerHints),
        },
    };

    // Generic role fallbacks when no class/archetype is chosen.
    private static readonly Dictionary<RoleKind, SpecProfile> GenericByRole = new()
    {
        [RoleKind.MeleeDps] = new("melee-dps", "Melee DPS", RoleKind.MeleeDps, W(sta: 0.4, str: 0.35, agi: 0.25), MeleeDpsHints),
        [RoleKind.RangedDps] = new("ranged-dps", "Ranged DPS", RoleKind.RangedDps, W(sta: 0.35, agi: 0.5, @int: 0.15), RangedDpsHints),
        [RoleKind.CasterDps] = new("caster-dps", "Caster DPS", RoleKind.CasterDps, W(sta: 0.35, @int: 0.45, spi: 0.2), CasterHints),
        [RoleKind.Tank] = new("tank", "Tank", RoleKind.Tank, W(sta: 0.6, str: 0.25, agi: 0.15), TankHints),
        [RoleKind.Healer] = new("healer", "Healer", RoleKind.Healer, W(sta: 0.3, @int: 0.4, spi: 0.3), HealerHints),
    };

    public static readonly SpecProfile GenericBalanced =
        new("generic", "Generic (balanced)", RoleKind.Tank, W(sta: 0.5, str: 0.2, agi: 0.15, @int: 0.15), []);

    /// <summary>Archetypes for a class, most-common first; empty if the class id is unknown.</summary>
    public static IReadOnlyList<SpecProfile> ForClass(int classId) =>
        ByClass.TryGetValue(classId, out var list) ? list : Array.Empty<SpecProfile>();

    /// <summary>Resolve a (class, archetype-key) pair to a profile, falling back to the class default,
    /// then to a generic balanced profile.</summary>
    public static SpecProfile Resolve(int classId, string? archetypeKey)
    {
        var list = ForClass(classId);
        if (list.Count == 0) return GenericBalanced;
        if (!string.IsNullOrWhiteSpace(archetypeKey))
        {
            var hit = list.FirstOrDefault(p => string.Equals(p.Key, archetypeKey, StringComparison.OrdinalIgnoreCase));
            if (hit is not null) return hit;
        }
        return list[0];
    }

    public static SpecProfile Generic(RoleKind role) =>
        GenericByRole.TryGetValue(role, out var p) ? p : GenericBalanced;

    /// <summary>The whole catalog for the UI spec picker: class id/name → archetypes.</summary>
    public static IEnumerable<object> ForUi() =>
        ByClass.Select(kv => new
        {
            classId = kv.Key,
            className = ((PlayerClass)kv.Key).ToString(),
            archetypes = kv.Value.Select(p => new { key = p.Key, label = p.Label, role = p.Role.ToString() }),
        });
}

/// <summary>The curated tier → budget/ilvl anchors, slot weights, and interpolation. Anchors encode
/// the real vanilla progression (Dungeon → MC → BWL → AQ40 → Naxx); fractional tiers interpolate
/// (T1.5 lands between MC and BWL) and tiers beyond T3 extrapolate the top segment's slope.</summary>
public static class TierAnchorTable
{
    // (tier, chest primary-stat points). Chest is the reference slot (weight 1.0).
    private static readonly (double Tier, double Points)[] PointAnchors =
    {
        (0.0, 24), (0.5, 33), (1.0, 44), (2.0, 60), (2.5, 72), (3.0, 90),
    };

    // (tier, item level) anchors for the endgame sets.
    private static readonly (double Tier, double Ilvl)[] IlvlAnchors =
    {
        (0.0, 55), (0.5, 60), (1.0, 66), (2.0, 76), (2.5, 83), (3.0, 90),
    };

    // inventory_type → fraction of the chest budget (classic armor slot modifiers).
    private static readonly Dictionary<int, double> SlotWeight = new()
    {
        [1] = 1.0,     // head
        [5] = 1.0,     // chest
        [7] = 1.0,     // legs
        [20] = 1.0,    // robe (long chest)
        [3] = 0.75,    // shoulder
        [6] = 0.75,    // waist
        [8] = 0.75,    // feet
        [10] = 0.75,   // hands
        [9] = 0.5625,  // wrists
        [16] = 0.5625, // back (cloak)
        [23] = 0.5625, // held in off-hand
    };

    public static double SlotFraction(int inventoryType) =>
        SlotWeight.TryGetValue(inventoryType, out var w) ? w : 0.75;

    public static double ChestPoints(double tier) => Interpolate(PointAnchors, tier);
    public static int ItemLevel(double tier) => (int)Math.Round(Interpolate(IlvlAnchors, tier));

    // ── Weapon anchors (Weapon Forge itemization) ───────────────────────

    // (tier, two-hand melee DPS). The reference weapon kind (multiplier 1.0). Anchored on the real
    // endgame drops: dungeon-blue 2H ≈ 51, MC ≈ 61 (Obsidian Edged Blade), BWL ≈ 68 (Drake Talon
    // Cleaver), AQ40 ≈ 78, Naxx ≈ 91 (Might of Menethil). Other kinds scale via WeaponKindDpsFactor.
    private static readonly (double Tier, double Dps)[] Dps2HAnchors =
    {
        (0.0, 51), (0.5, 55), (1.0, 61), (2.0, 68), (2.5, 78), (3.0, 91),
    };

    // (tier, shield armor) and (tier, shield block) — Drillborer Disk (MC) 2121/42 through
    // Elementium Reinforced Bulwark (BWL) 2893/76 and the Naxx wall shields.
    private static readonly (double Tier, double Armor)[] ShieldArmorAnchors =
    {
        (0.0, 1700), (0.5, 1900), (1.0, 2250), (2.0, 2900), (2.5, 3300), (3.0, 3800),
    };
    private static readonly (double Tier, double Block)[] ShieldBlockAnchors =
    {
        (0.0, 30), (0.5, 34), (1.0, 41), (2.0, 57), (2.5, 70), (3.0, 85),
    };

    public static double TwoHandDps(double tier) => Interpolate(Dps2HAnchors, tier);
    public static int ShieldArmor(double tier) => (int)Math.Round(Interpolate(ShieldArmorAnchors, tier));
    public static int ShieldBlock(double tier) => (int)Math.Round(Interpolate(ShieldBlockAnchors, tier));

    /// <summary>Piecewise-linear over the anchors; extrapolates the first/last segment beyond range.</summary>
    private static double Interpolate((double X, double Y)[] anchors, double x)
    {
        if (x <= anchors[0].X)
        {
            // Extrapolate below the first anchor using the first segment slope, clamped non-negative.
            var (x0, y0) = anchors[0];
            var (x1, y1) = anchors[1];
            double slope = (y1 - y0) / (x1 - x0);
            return Math.Max(0, y0 + slope * (x - x0));
        }
        for (int i = 1; i < anchors.Length; i++)
        {
            if (x <= anchors[i].X)
            {
                var (x0, y0) = anchors[i - 1];
                var (x1, y1) = anchors[i];
                double t = (x - x0) / (x1 - x0);
                return y0 + t * (y1 - y0);
            }
        }
        // Above the last anchor: extrapolate the final segment's slope (beyond-Naxx tiers).
        var (px, py) = anchors[^2];
        var (lx, ly) = anchors[^1];
        double s = (ly - py) / (lx - px);
        return ly + s * (x - lx);
    }
}
