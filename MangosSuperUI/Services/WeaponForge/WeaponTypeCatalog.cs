namespace MangosSuperUI.Services.WeaponForge;

/// <summary>
/// One forgeable weapon family: the item_template gameplay contract (class 2 subclass /
/// inventory type / sheath / material / delay), the visual-donor search patterns, and the
/// grip presentation hints the import page uses. Everything type-specific the Forge needs
/// hangs off this record so adding a family is one table row, not a code hunt.
///
/// Grip placement itself (how far the weapon extends behind the palm, and the target length)
/// is NOT stored here — it is measured from the resolved stock donor's vertex box by
/// <see cref="WeaponDonorResolver"/>, because the stock model already encodes exactly where
/// Blizzard put the palm. Only <see cref="SecondHandFraction"/> is a hint: the off-hand is
/// placed by the character animation, so its band in the preview is approximate by nature.
/// </summary>
public sealed record WeaponTypeProfile
{
    public required string Key { get; init; }
    public required string Label { get; init; }

    /// <summary>Noun for the default item name, e.g. "Staff" → "Forged Staff 60012".</summary>
    public required string DefaultNoun { get; init; }

    // item_template gameplay contract (class is always 2 = weapon).
    public required int Subclass { get; init; }
    public required int InventoryType { get; init; }   // 13 one-hand, 17 two-hand
    public required int Sheath { get; init; }          // 3 hip, 1 back, 2 staff-back
    public required int Material { get; init; }        // 1 metal, 2 wood
    public required int DelayMs { get; init; }

    public bool TwoHanded { get; init; }

    /// <summary>Approximate off-hand grip station for the preview band, as a fraction of the
    /// weapon's X extent ahead of the palm. Null for one-handers. The real off-hand is placed
    /// by the character animation — this is a visual guide, not a serialized value.</summary>
    public float? SecondHandFraction { get; init; }

    /// <summary>Explicit ItemDisplayInfo donor row (the proven golden sword pins 679).
    /// When null, <see cref="WeaponDonorResolver"/> scans by <see cref="DonorModelPatterns"/>.</summary>
    public uint? PinnedDisplayRow { get; init; }

    /// <summary>Case-insensitive ModelName1 prefixes used to find a stock visual donor
    /// (e.g. "stave_2h" matches Stave_2H_Long_A_01.mdx).</summary>
    public string[] DonorModelPatterns { get; init; } = [];

    /// <summary>Column overrides applied to the donor-2131 gameplay clone so a forged axe is an
    /// axe (subclass/inventory/sheath/material/delay) instead of inheriting sword values.</summary>
    public Dictionary<string, string> ItemTemplateOverrides() => new()
    {
        ["subclass"] = Subclass.ToString(),
        ["inventory_type"] = InventoryType.ToString(),
        ["sheath"] = Sheath.ToString(),
        ["material"] = Material.ToString(),
        ["delay"] = DelayMs.ToString(),
    };
}

/// <summary>The forgeable weapon families. Vanilla enums: item class 2 subclasses
/// (0 axe1H, 1 axe2H, 4 mace1H, 5 mace2H, 6 polearm, 7 sword1H, 8 sword2H, 10 staff,
/// 15 dagger); inventory types 13 one-hand / 17 two-hand; item_template sheath
/// (1 back, 2 staff-back, 3 hip).</summary>
public static class WeaponTypeCatalog
{
    public const string DefaultKey = "sword1h";

    public static readonly IReadOnlyList<WeaponTypeProfile> All =
    [
        new()
        {
            Key = "sword1h", Label = "1H Sword", DefaultNoun = "Sword",
            Subclass = 7, InventoryType = 13, Sheath = 3, Material = 1, DelayMs = 2600,
            PinnedDisplayRow = 679, // the proven golden donor (Sword_1H_Short_A_01)
        },
        new()
        {
            Key = "dagger", Label = "Dagger", DefaultNoun = "Dagger",
            Subclass = 15, InventoryType = 13, Sheath = 3, Material = 1, DelayMs = 1800,
            DonorModelPatterns = ["knife_1h", "dagger"],
        },
        new()
        {
            Key = "axe1h", Label = "1H Axe", DefaultNoun = "Axe",
            Subclass = 0, InventoryType = 13, Sheath = 3, Material = 1, DelayMs = 2700,
            DonorModelPatterns = ["axe_1h"],
        },
        new()
        {
            Key = "mace1h", Label = "1H Mace", DefaultNoun = "Mace",
            Subclass = 4, InventoryType = 13, Sheath = 3, Material = 1, DelayMs = 2700,
            DonorModelPatterns = ["mace_1h", "hammer_1h"],
        },
        new()
        {
            Key = "sword2h", Label = "2H Sword", DefaultNoun = "Greatsword",
            Subclass = 8, InventoryType = 17, Sheath = 1, Material = 1, DelayMs = 3300,
            TwoHanded = true, SecondHandFraction = 0.12f,
            DonorModelPatterns = ["sword_2h"],
        },
        new()
        {
            Key = "axe2h", Label = "2H Axe", DefaultNoun = "Battleaxe",
            Subclass = 1, InventoryType = 17, Sheath = 1, Material = 1, DelayMs = 3400,
            TwoHanded = true, SecondHandFraction = 0.18f,
            DonorModelPatterns = ["axe_2h"],
        },
        new()
        {
            Key = "mace2h", Label = "2H Mace", DefaultNoun = "Maul",
            Subclass = 5, InventoryType = 17, Sheath = 1, Material = 1, DelayMs = 3500,
            TwoHanded = true, SecondHandFraction = 0.18f,
            DonorModelPatterns = ["mace_2h", "hammer_2h"],
        },
        new()
        {
            Key = "staff", Label = "Staff", DefaultNoun = "Staff",
            Subclass = 10, InventoryType = 17, Sheath = 2, Material = 2, DelayMs = 3200,
            TwoHanded = true, SecondHandFraction = 0.30f,
            // Vanilla names staves Stave_2H_* (Stave_2H_Long_A_01 ...); "staff_" is kept for
            // clients whose patches add Staff_* rows.
            DonorModelPatterns = ["stave_2h", "staff_"],
        },
        new()
        {
            Key = "polearm", Label = "Polearm", DefaultNoun = "Polearm",
            Subclass = 6, InventoryType = 17, Sheath = 1, Material = 1, DelayMs = 3300,
            TwoHanded = true, SecondHandFraction = 0.18f,
            // Vanilla names every polearm Polearm_2H_* (Polearm_2H_Bladed_A_01 ...); the
            // spear/halberd/pike stems only appear in later clients.
            DonorModelPatterns = ["polearm_2h", "spear_2h", "halberd_2h", "pike_", "spear_"],
        },
    ];

    /// <summary>Lookup by key; unknown/empty falls back to the proven 1H sword.</summary>
    public static WeaponTypeProfile Get(string? key)
    {
        if (!string.IsNullOrWhiteSpace(key))
            foreach (var p in All)
                if (string.Equals(p.Key, key.Trim(), StringComparison.OrdinalIgnoreCase))
                    return p;
        return All[0];
    }
}
