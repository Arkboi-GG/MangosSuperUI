namespace MangosSuperUI.Services.ArmorForge;

/// <summary>How a piece of armor puts itself on the character — the single biggest divergence from
/// the Weapon Forge (ARMOR_FORGE.md §1).</summary>
public enum ArmorRenderKind
{
    /// <summary>Real object-component M2 attached to a bone (helm, shoulder). Uses ModelName/TextureName
    /// + geoset groups + (helm only) helmet hair/facial visibility.</summary>
    Modelled,
    /// <summary>No model — paints partial BLPs into the shared character body atlas (chest, legs,
    /// gloves, boots, bracers, belt, shirt, tabard). Uses the eight m_texture[] component fields.</summary>
    Painted,
    /// <summary>Cloak — a single texture applied to the character's built-in cape geoset via
    /// TextureName1. Neither a full model nor a body-atlas paint.</summary>
    Cloak,
}

/// <summary>Vanilla armor materials = class-4 subclasses. Also the default armor multiplier tier.</summary>
public enum ArmorMaterial { Misc = 0, Cloth = 1, Leather = 2, Mail = 3, Plate = 4 }

/// <summary>
/// One forgeable armor family — the armor-side sibling of <c>WeaponTypeProfile</c>. Keyed by SLOT
/// (helm, shoulder, chest, …); the material (cloth/leather/mail/plate) is chosen at build time and
/// drives the class-4 subclass and the armor value, so a single "chest" family can forge a cloth
/// robe or a plate breastplate.
/// </summary>
public sealed record ArmorTypeProfile
{
    /// <summary>Stable key used in the UI and stored on forged rows, e.g. "chest", "helm".</summary>
    public required string Key { get; init; }
    /// <summary>Human label, e.g. "Chest / Breastplate".</summary>
    public required string Label { get; init; }
    /// <summary>Noun used when auto-naming ("Forged {Noun}").</summary>
    public required string DefaultNoun { get; init; }

    /// <summary>ItemDisplayInfo / rendering behaviour.</summary>
    public required ArmorRenderKind RenderKind { get; init; }

    /// <summary>item_template.inventory_type: head 1, shoulder 3, shirt 4, chest 5, waist 6, legs 7,
    /// feet 8, wrist 9, hands 10, back(cloak) 16, tabard 19, robe 20.</summary>
    public required int InventoryType { get; init; }

    /// <summary>For MODELLED pieces, the MPQ object-components directory the model + its texture are
    /// written to and read from (Head / Shoulder). Null for painted pieces.</summary>
    public string? ComponentDir { get; init; }

    /// <summary>Shoulders are an L/R PAIR of distinct files (stock ModelName1=LShoulder_X.mdx,
    /// ModelName2=RShoulder_X.mdx, one shared texture) — measured on the 1.12 client. The importer
    /// emits both; the row carries two names. (An earlier "mirror" assumption was wrong.)</summary>
    public bool IsShoulderPair { get; init; }

    /// <summary>Whether equipping this piece hides hair/beard (helms only — the row's HelmetGeosetVis
    /// fields). Carried from the imported source; this flag just says the fields are meaningful.</summary>
    public bool UsesHelmetVisibility { get; init; }

    /// <summary>Body-atlas component slots (0..7) this equip type legitimately paints — the same
    /// slot→region rule the game client applies when dressing (chest: sleeves + torso; legs: LU+LL;
    /// gloves: AL+HA; boots: LL+FO; belt: TL+LU; bracers: AL; tabard: TU+TL). Empty for modelled /
    /// cloak. THIS IS A FILTER, not just a default: TBC/WotLK ItemDisplayInfo rows are often
    /// authored from a shared set template and carry textures (and even shoulder models) for slots
    /// the item does not occupy — the client ignores them, so the importer and the preview must too,
    /// or every piece smears the template over the others (measured on Onslaught Battlegear).</summary>
    public IReadOnlyList<int> PaintedSlots { get; init; } = Array.Empty<int>();

    /// <summary>Whether a GLB can be imported for this family. Only the modelled pieces (helm,
    /// shoulder) have a mesh to import; painted pieces are authored as textures.</summary>
    public bool GlbImportSupported => RenderKind == ArmorRenderKind.Modelled;

    /// <summary>Base armor value at material=Cloth, item level 60-ish. Scaled by the material tier in
    /// <see cref="DefaultArmor"/>. A sensible seed the operator can override in the item modal.</summary>
    public int BaseArmor { get; init; }

    /// <summary>item_template.sheath — armor never sheathes, so 0 for every armor family.</summary>
    public int Sheath { get; init; } = 0;

    /// <summary>Default armor value for a chosen material tier, scaled off <see cref="BaseArmor"/>
    /// (cloth ×1, leather ×2, mail ×3, plate ×4 — the vanilla per-slot pattern, rounded).</summary>
    public int DefaultArmor(ArmorMaterial material) =>
        material == ArmorMaterial.Misc ? 0 : BaseArmor * (int)material;

    /// <summary>Which class-4 subclass a chosen material maps to. Shirt/Tabard are always Misc(0);
    /// cloaks are Cloth(1). Everything else honours the operator's material choice.</summary>
    public int SubclassFor(ArmorMaterial material) => (int)material;

    /// <summary>The item_template column overrides for a forged piece of this family + material +
    /// armor value. Applied on top of the donor-2131 clone (a weapon row) — so it zeros every
    /// weapon-only field (damage, delay, ammo, block) and sets the armor identity.</summary>
    public Dictionary<string, string> ItemTemplateOverrides(ArmorMaterial material, int armor, int setId = 0)
    {
        var o = new Dictionary<string, string>
        {
            ["class"] = "4",
            ["subclass"] = SubclassFor(material).ToString(),
            ["inventory_type"] = InventoryType.ToString(),
            ["sheath"] = Sheath.ToString(),
            ["material"] = MaterialSoundGroup(material).ToString(),
            ["armor"] = armor.ToString(),
            // Clear every weapon-only field the sword donor carries.
            ["delay"] = "0",
            ["range_mod"] = "0",
            ["ammo_type"] = "0",
            ["dmg_min1"] = "0",
            ["dmg_max1"] = "0",
            ["dmg_type1"] = "0",
            ["block"] = "0",
        };
        if (setId > 0) o["set_id"] = setId.ToString();
        return o;
    }

    /// <summary>item_template.material — the equip/hit sound group. Vanilla: 1 metal, 2 wood,
    /// 5 chain, 6 plate, 7 cloth, 8 leather. Maps the armor material to its sound group.</summary>
    private static int MaterialSoundGroup(ArmorMaterial material) => material switch
    {
        ArmorMaterial.Cloth => 7,
        ArmorMaterial.Leather => 8,
        ArmorMaterial.Mail => 5,
        ArmorMaterial.Plate => 6,
        _ => 0,
    };
}

/// <summary>
/// The forgeable armor families, keyed by equipment slot. Painted-slot sets follow the vanilla
/// body-atlas convention (BodyAtlasTextureService slot map): ArmUpper 0, ArmLower 1, Hand 2,
/// TorsoUpper 3, TorsoLower 4, LegUpper 5, LegLower 6, Foot 7.
/// </summary>
public static class ArmorTypeCatalog
{
    public const string DefaultKey = "chest";

    public static readonly IReadOnlyList<ArmorTypeProfile> All =
    [
        // ── Modelled pieces (real M2 attached to a bone) ─────────────────────────────
        new()
        {
            Key = "helm", Label = "Head / Helm", DefaultNoun = "Helm",
            RenderKind = ArmorRenderKind.Modelled, InventoryType = 1,
            ComponentDir = ArmorNaming.HeadDir, UsesHelmetVisibility = true, BaseArmor = 40,
        },
        new()
        {
            Key = "shoulder", Label = "Shoulder", DefaultNoun = "Shoulders",
            RenderKind = ArmorRenderKind.Modelled, InventoryType = 3,
            ComponentDir = ArmorNaming.ShoulderDir, IsShoulderPair = true, BaseArmor = 35,
        },

        // ── Painted pieces (body-atlas component textures) ───────────────────────────
        new()
        {
            Key = "chest", Label = "Chest / Breastplate", DefaultNoun = "Chestpiece",
            RenderKind = ArmorRenderKind.Painted, InventoryType = 5, BaseArmor = 50,
            // sleeves + torso
            PaintedSlots = new[] { 0, 1, 3, 4 },
        },
        new()
        {
            Key = "robe", Label = "Robe (chest slot)", DefaultNoun = "Robe",
            RenderKind = ArmorRenderKind.Painted, InventoryType = 20, BaseArmor = 50,
            // sleeves + torso + skirt
            PaintedSlots = new[] { 0, 1, 3, 4, 5, 6 },
        },
        new()
        {
            Key = "legs", Label = "Legs", DefaultNoun = "Legguards",
            RenderKind = ArmorRenderKind.Painted, InventoryType = 7, BaseArmor = 45,
            PaintedSlots = new[] { 5, 6 },
        },
        new()
        {
            Key = "gloves", Label = "Hands / Gloves", DefaultNoun = "Gauntlets",
            RenderKind = ArmorRenderKind.Painted, InventoryType = 10, BaseArmor = 25,
            PaintedSlots = new[] { 1, 2 },
        },
        new()
        {
            Key = "boots", Label = "Feet / Boots", DefaultNoun = "Boots",
            RenderKind = ArmorRenderKind.Painted, InventoryType = 8, BaseArmor = 25,
            PaintedSlots = new[] { 6, 7 },
        },
        new()
        {
            Key = "bracers", Label = "Wrist / Bracers", DefaultNoun = "Bracers",
            RenderKind = ArmorRenderKind.Painted, InventoryType = 9, BaseArmor = 20,
            PaintedSlots = new[] { 1 },
        },
        new()
        {
            Key = "belt", Label = "Waist / Belt", DefaultNoun = "Girdle",
            RenderKind = ArmorRenderKind.Painted, InventoryType = 6, BaseArmor = 20,
            PaintedSlots = new[] { 4, 5 },
        },
        new()
        {
            Key = "shirt", Label = "Shirt (cosmetic)", DefaultNoun = "Shirt",
            RenderKind = ArmorRenderKind.Painted, InventoryType = 4, BaseArmor = 0,
            PaintedSlots = new[] { 0, 1, 3, 4 },
        },
        new()
        {
            Key = "tabard", Label = "Tabard (cosmetic)", DefaultNoun = "Tabard",
            RenderKind = ArmorRenderKind.Painted, InventoryType = 19, BaseArmor = 0,
            PaintedSlots = new[] { 3, 4 },
        },

        // ── Cloak (texture on the built-in cape geoset) ──────────────────────────────
        new()
        {
            Key = "cloak", Label = "Back / Cloak", DefaultNoun = "Cloak",
            RenderKind = ArmorRenderKind.Cloak, InventoryType = 16, BaseArmor = 25,
        },
    ];

    public static ArmorTypeProfile Get(string? key) =>
        All.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase))
        ?? All.First(p => p.Key == DefaultKey);

    /// <summary>Map a TBC/vanilla (class, subclass, inventoryType) to a forge family key, for the
    /// TBC importer. Armor is class 4; the SLOT (inventory_type) selects the family, not the
    /// subclass (which is only the material). Returns null for slots the forge doesn't handle
    /// (neck/finger/trinket/relic have no visible model).</summary>
    public static string? TypeKeyFor(int itemClass, int subclass, int inventoryType)
    {
        if (itemClass != 4) return null;
        return inventoryType switch
        {
            1 => "helm",
            3 => "shoulder",
            4 => "shirt",
            5 => "chest",
            6 => "belt",
            7 => "legs",
            8 => "boots",
            9 => "bracers",
            10 => "gloves",
            16 => "cloak",
            19 => "tabard",
            20 => "robe",
            _ => null,
        };
    }

    /// <summary>The material of a class-4 subclass, for the TBC importer's default material choice.</summary>
    public static ArmorMaterial MaterialForSubclass(int subclass) => subclass switch
    {
        1 => ArmorMaterial.Cloth,
        2 => ArmorMaterial.Leather,
        3 => ArmorMaterial.Mail,
        4 => ArmorMaterial.Plate,
        _ => ArmorMaterial.Misc,
    };
}
