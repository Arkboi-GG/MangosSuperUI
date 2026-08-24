using MangosSuperUI.Services.WeaponForge.RawM2;

namespace MangosSuperUI.Services.WeaponForge.Motion;

/// <summary>
/// The stock 1.12 item models the forge lifts working particle emitters out of
/// (see <see cref="M2EmitterTransplanter"/> for why a transplant beats authoring one).
///
/// === Why these entries and not others (measured on a real 1.12 client — do NOT re-derive) ===
///
/// A sweep of every <c>Item\ObjectComponents\{Weapon,Shield,Shoulder,Head}</c> model named by
/// 1.12's own ItemDisplayInfo found <b>391 particle emitters across 130 models</b>, and all 391 were
/// self-contained and terminal, i.e. transplantable. Grouped by emitter texture they form a palette
/// of effect "shapes": glow balls, flares, stars, smoke sheets, flame licks, mist.
///
/// The important measurement is that <b>this palette is the same one the later clients draw from</b>.
/// Sweeping TBC and WotLK weapon models, the emitter texture is one 1.12 also ships on an item for
/// <b>3,071 of 3,692 (83.2%)</b> of TBC emitters and <b>4,246 of 6,438 (66.0%)</b> of WotLK ones. For
/// those the result is not an approximation at all — same texture, same blend, source colour, source
/// position, with Blizzard's own emission behaviour underneath.
///
/// The remainder are item-specific textures (<c>FROSTMOURNEA</c>, <c>SHIELD_ICECROWNRAID_D_04PARTICLE</c>,
/// the <c>SHOULDER_*_PARTICLE</c> sheets Worldbreaker-era sets use). Those still work: the import
/// already packages source effect textures as its own MPQ members, so the donor supplies the motion
/// and the source supplies the look. <see cref="ByShape"/> picks which motion by what the texture
/// name says it is.
///
/// Each entry names the SMALLEST stock model carrying that emitter, so the transplanted blob stays
/// small. Every donor is re-validated at use time by <see cref="M2EmitterTransplanter.IsTransplantable"/> —
/// a hardcoded list must never be trusted blind against an unknown client build.
/// </summary>
public static class VanillaEmitterDonors
{
    private const string W = @"Item\ObjectComponents\Weapon\";
    private const string S = @"Item\ObjectComponents\Shoulder\";
    private const string H = @"Item\ObjectComponents\Shield\";

    /// <summary>A transplantable stock emitter.</summary>
    /// <param name="Texture">Emitter texture stem, the key the source is matched on.</param>
    /// <param name="ModelPath">Stock 1.12 model to lift from.</param>
    /// <param name="EmitterIndex">Which emitter of that model.</param>
    /// <param name="TexturePath">Full MPQ path of the emitter texture — already in every 1.12
    /// client, so referencing it packages nothing.
    ///
    /// MEASURED, not derived. Every one of these was originally written as "the texture stem under
    /// the donor model's own directory", and that is wrong for 20 of the 27 rows: vanilla emitter
    /// sheets overwhelmingly live under <c>SPELLS\</c>, <c>CREATURE\</c>, <c>WORLD\</c> or
    /// <c>INTERFACE\</c>, not beside the item that samples them. A graft naming a member the client
    /// cannot open produces an emitter with no texture, which draws nothing — and a missing effect is
    /// indistinguishable from an effect that was never grafted, so it hid for a long time.
    ///
    /// This field is now a FALLBACK only: <see cref="M2EmitterTransplanter.ResolveDonorTexture"/>
    /// reads the path out of the donor model's own texture table at graft time, which cannot go
    /// stale. Keep these correct anyway — they document where the art actually lives.</param>
    /// <param name="Shape">Which family this reads as, for the by-name fallback.</param>
    /// <param name="Tiles">Cells in the donor's texture sheet (rows × cols), read off the emitter
    /// record at +48/+50 of the real 1.12 file. 1 = a still sprite; &gt; 1 = a flipbook the client
    /// steps through over each particle's life. It is a property of the DONOR'S OWN TEXTURE, which
    /// the graft keeps, so it cannot be retargeted — it can only be selected for. See
    /// <see cref="Best"/>.</param>
    /// <param name="Representative">Marks the donor a shape should fall back to when the source names
    /// a texture 1.12 does not ship. Without it the fallback is "first of that shape in catalog
    /// order", and catalog order is by how often the LATER clients use the texture — which says
    /// nothing about whether the donor is a good generic member of its family.</param>
    public sealed record Donor(string Texture, string ModelPath, int EmitterIndex, string TexturePath, EffectShape Shape,
        int Tiles = 1, bool Representative = false);

    public enum EffectShape { Glow, Flare, Star, Smoke, Flame, Mist }

    /// <summary>Measured donors, keyed by emitter texture stem. Ordered by how often the later
    /// clients use that texture, so the common cases are the well-tested ones.
    ///
    /// <c>Tiles</c> is rows × cols read off each real record at +48/+50 on the 1.12 client, i.e. how
    /// many cells the donor's own sheet is cut into. Only four donors are flipbooks; the rest are
    /// still sprites, which is why "first of the shape" quietly handed a 16-cell flipbook
    /// (FLAMELICKSMALL) to every unnamed fire source.</summary>
    public static readonly IReadOnlyList<Donor> Catalog = new[]
    {
        new Donor("GLOWBALL",      W + "Misc_1H_Orb_A_01.m2",            1, @"CREATURE\FIREELEMENTAL\GLOWBALL.BLP",      EffectShape.Glow,  Tiles: 1, Representative: true),
        new Donor("FLARE",         W + "Sword_1H_Mage_D_01.m2",          0, @"ITEM\OBJECTCOMPONENTS\WEAPON\FLARE.BLP",         EffectShape.Flare, Tiles: 1, Representative: true),
        new Donor("GLOWSTAR",      W + "Misc_1H_Sparkler_A_01Red.m2",    0, @"INTERFACE\BUTTONS\GLOWSTAR.BLP",      EffectShape.Star,  Tiles: 1, Representative: true),
        new Donor("TOONSMOKE16_2", W + "Firearm_2H_Rifle_A_02.m2",       5, @"CREATURE\SPELLS\TOONSMOKE16_2.BLP", EffectShape.Smoke, Tiles: 64),
        new Donor("SPARKLE",       S + "LShoulder_Robe_PVPAlliance_C_01.m2", 0, @"WORLD\SKILLACTIVATED\CONTAINERS\SPARKLE.BLP",   EffectShape.Star,  Tiles: 1),
        new Donor("TOONSMOKE16",   S + "LShoulder_Robe_RaidMage_B_01.m2", 0, @"SPELLS\TOONSMOKE16.BLP",  EffectShape.Smoke, Tiles: 64),
        new Donor("STAR5A",        S + "LShoulder_Plate_RaidPaladin_C_01.m2", 0, @"SPELLS\STAR5A.BLP",   EffectShape.Star,  Tiles: 1),
        new Donor("SMOKE_1",       W + "Sword_1H_Stratholme_D_02.m2",    0, @"WORLD\GOOBER\SMOKE_1.BLP",       EffectShape.Smoke, Tiles: 1, Representative: true),
        new Donor("FLAMELICKSMALL", W + "Thrown_1H_Molotov_A_01.m2",     0, @"ITEM\OBJECTCOMPONENTS\WEAPON\FLAMELICKSMALL.BLP", EffectShape.Flame, Tiles: 16),
        new Donor("FLARE_LOW",     W + "Wand_1H_Horde_A_02.m2",          0, @"WORLD\OUTLAND\PASSIVEDOODADS\HANGINGCRYSTALS\FLARE_LOW.BLP",     EffectShape.Flare, Tiles: 1),
        new Donor("WATERMIST",     H + "Shield_Naxxramas_D_03.m2",       0, @"CREATURE\ELEMENTALEARTH\WATERMIST.BLP",     EffectShape.Mist,  Tiles: 1, Representative: true),
        new Donor("GENERICGLOW1",  W + "Misc_1H_ZulGurub_D_01.m2",       0, @"WORLD\GENERIC\ORC\PASSIVE DOODADS\VOODOOSTUFF\GENERICGLOW1.BLP",  EffectShape.Glow,  Tiles: 1),
        new Donor("FIRESWIRL",     W + "Misc_1H_Orb_A_01.m2",            0, @"ITEM\OBJECTCOMPONENTS\WEAPON\FIRESWIRL.BLP",     EffectShape.Flame, Tiles: 1),
        new Donor("GLOW",          W + "Misc_1H_Lantern_A_01.m2",        2, @"WORLD\GENERIC\NIGHTELF\PASSIVE DOODADS\MAGICALIMPLEMENTS\GLOW.BLP",          EffectShape.Glow,  Tiles: 1),
        new Donor("STAR10",        W + "Offhand_Blackwing_A_01.m2",      0, @"ITEM\OBJECTCOMPONENTS\WEAPON\STAR10.BLP",        EffectShape.Star,  Tiles: 1),
        new Donor("CYANSTARFLASH", W + "Stave_2H_Flaming_D_01.m2",       1, @"SPELLS\CYANSTARFLASH.BLP", EffectShape.Star,  Tiles: 1),
        new Donor("SMOKE02A",      S + "LShoulder_Robe_PVPHorde_C_01.m2", 0, @"CREATURE\MOUNTEDDEATHKNIGHT\SMOKE02A.BLP",     EffectShape.Smoke, Tiles: 1),
        new Donor("GLOW2",         W + "Sword_2H_Stratholme_D_01.m2",    0, @"ITEM\OBJECTCOMPONENTS\WEAPON\GLOW2.BLP",         EffectShape.Glow,  Tiles: 1),
        // The Flame family's representative: a plain upward lick, authored on a SHOULDER pad, still
        // sprite. Every measured "unnamed *_PARTICLE sheet, fire-coloured" source is a brazier of
        // some kind, and this is the stock emitter closest to that.
        new Donor("FIRE1",         S + "LShoulder_Leather_PVPHorde_C_01.m2", 0, @"SPELLS\FIRE1.BLP",     EffectShape.Flame, Tiles: 1, Representative: true),
        new Donor("DUST5A",        W + "Misc_1H_Bag_A_01.m2",            0, @"CREATURE\SPELLS\DUST5A.BLP",        EffectShape.Smoke, Tiles: 1),
        new Donor("BUILDING_BASE1_ALPHA", W + "Knife_1H_Naxxramas_D_01.m2", 0, @"ITEM\OBJECTCOMPONENTS\WEAPON\BUILDING_BASE1_ALPHA.BLP", EffectShape.Glow, Tiles: 1),
        new Donor("FIRE1WHITE",    S + "LShoulder_Robe_RaidPriest_C_01.m2", 0, @"WORLD\KALIMDOR\MAURADON\PASSIVEDOODADS\SATYRHANGINGBRAZIERS\FIRE1WHITE.BLP", EffectShape.Flame, Tiles: 1),
        new Donor("FLAMELICKSMALLMAGICBLUE", W + "Mace_1H_Naxxramas_D_01.m2", 0, @"WORLD\GENERIC\OGRE\PASSIVE DOODADS\TORCHES\FLAMELICKSMALLMAGICBLUE.BLP", EffectShape.Flame, Tiles: 16),
        new Donor("SMOKE02B",      S + "RShoulder_Leather_RaidRogue_B_01.m2", 0, @"ITEM\OBJECTCOMPONENTS\SHOULDER\SMOKE02B.BLP", EffectShape.Smoke, Tiles: 1),
        new Donor("PRIESTHELM01",  S + "LShoulder_Leather_RaidDruid_C_01.m2", 0, @"ITEM\OBJECTCOMPONENTS\HEAD\PRIESTHELM01.BLP", EffectShape.Glow, Tiles: 1),
        new Donor("Flame01",       W + "Misc_1H_HolySymbol_A_01.m2",     0, @"Creature\Infernal\Flame01.blp",       EffectShape.Flame, Tiles: 1),
        new Donor("YELLOW_GLOW",   W + "Bow_1H_HunterEpic.m2",           1, @"SPELLS\YELLOW_GLOW.BLP",   EffectShape.Glow,  Tiles: 1),
    };

    public static Donor? ByTexture(string? textureStem) =>
        string.IsNullOrWhiteSpace(textureStem)
            ? null
            : Catalog.FirstOrDefault(d => string.Equals(d.Texture, textureStem, StringComparison.OrdinalIgnoreCase));

    /// <summary>Fallback for a source texture 1.12 does not ship: read the NAME for what the effect
    /// is meant to be. Keyword-driven, because the artists' own naming is the most reliable statement
    /// of intent available offline. <paramref name="named"/> reports whether a keyword actually
    /// matched — a bare "…_PARTICLE" says nothing, and the caller has a better signal (colour) than
    /// defaulting to a glow ball.</summary>
    public static EffectShape ShapeFromName(string? textureStem, out bool named)
    {
        string n = (textureStem ?? "").ToLowerInvariant();
        named = true;
        if (Match(n, "flame", "fire", "ember", "lava", "burn", "torch", "blaze")) return EffectShape.Flame;
        if (Match(n, "smoke", "cloud", "dust", "ash", "fog", "steam")) return EffectShape.Smoke;
        if (Match(n, "star", "spark", "flash", "lightning", "bolt", "shard", "twinkle")) return EffectShape.Star;
        if (Match(n, "flare", "beam", "ray", "streak")) return EffectShape.Flare;
        if (Match(n, "water", "wave", "splash", "bubble", "mist")) return EffectShape.Mist;
        if (Match(n, "glow", "aura", "orb", "halo")) return EffectShape.Glow;
        named = false;
        return EffectShape.Glow;
    }

    public static Donor ByShape(string? textureStem) => First(ShapeFromName(textureStem, out _));

    /// <summary>Shape override for a source whose colour/keywords say "frost" — the icy-blue flame
    /// lick reads far better than an orange one recoloured blue.</summary>
    public static Donor First(EffectShape shape) =>
        Catalog.FirstOrDefault(d => d.Shape == shape) ?? Catalog[0];

    /// <summary>Best donor of a shape for a source with <paramref name="sourceTiles"/> texture cells.
    ///
    /// The graft keeps the DONOR's texture, and therefore the donor's tile grid — that pair cannot be
    /// split, because a tile count only means anything against the sheet it cuts up. So the tile
    /// grid is chosen rather than retargeted: match a still source to a still donor and a flipbook
    /// source to a flipbook donor, and the cadence the client plays is the one the source implied.
    /// Mismatch it and the effect either strobes (still source on a 16-cell donor — the measured
    /// Worldbreaker case) or freezes (flipbook source on a still donor).
    ///
    /// Ranking: tile compatibility, then the family's <see cref="Donor.Representative"/>, then
    /// catalog order. Falls back to <see cref="First"/> when the shape has no compatible member.</summary>
    public static Donor Best(EffectShape shape, int sourceTiles)
    {
        bool sourceIsFlipbook = sourceTiles > 1;
        var ranked = Catalog
            .Select((d, i) => (Donor: d, Index: i))
            .Where(x => x.Donor.Shape == shape)
            .OrderByDescending(x => x.Donor.Tiles > 1 == sourceIsFlipbook)
            .ThenByDescending(x => x.Donor.Representative)
            .ThenBy(x => x.Index)
            .Select(x => x.Donor)
            .FirstOrDefault();
        return ranked ?? First(shape);
    }

    private static bool Match(string haystack, params string[] needles) =>
        needles.Any(x => haystack.Contains(x, StringComparison.Ordinal));
}
