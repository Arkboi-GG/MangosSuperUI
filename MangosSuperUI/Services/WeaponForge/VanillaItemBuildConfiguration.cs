using System.Globalization;

namespace MangosSuperUI.Services.WeaponForge;

/// <summary>
/// Optional gameplay configuration applied while a forged visual is still being built.
/// Every scalar is nullable on purpose: omitted values leave the proven donor/family/TBC
/// value untouched. Supplying <see cref="Stats"/> or <see cref="Spells"/> is an explicit
/// replacement of that complete slot array (an empty array clears it).
/// </summary>
public sealed class VanillaItemBuildConfiguration
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public int? Quality { get; init; }
    public int? InventoryType { get; init; }
    public int? ItemLevel { get; init; }
    public int? RequiredLevel { get; init; }

    public float? DamageMin { get; init; }
    public float? DamageMax { get; init; }
    public int? DamageType { get; init; }
    public int? DelayMs { get; init; }

    public List<VanillaItemStatConfiguration>? Stats { get; init; }
    public int? Armor { get; init; }
    public long? Block { get; init; }
    public int? HolyRes { get; init; }
    public int? FireRes { get; init; }
    public int? NatureRes { get; init; }
    public int? FrostRes { get; init; }
    public int? ShadowRes { get; init; }
    public int? ArcaneRes { get; init; }
    public int? MaxDurability { get; init; }

    public int? Bonding { get; init; }
    public int? AllowableClass { get; init; }
    public int? AllowableRace { get; init; }
    public int? RequiredSkill { get; init; }
    public int? RequiredSkillRank { get; init; }
    public int? RequiredSpell { get; init; }
    public int? RequiredHonorRank { get; init; }
    public int? RequiredReputationFaction { get; init; }
    public int? RequiredReputationRank { get; init; }

    public long? BuyPrice { get; init; }
    public long? SellPrice { get; init; }
    public List<VanillaItemSpellConfiguration>? Spells { get; init; }
}

/// <summary>
/// One of the ten direct item-stat slots. Type is nullable so Vanilla Mana (type 0)
/// remains distinguishable from a missing type in JSON.
/// </summary>
public sealed class VanillaItemStatConfiguration
{
    public int? Type { get; init; }
    public int? Value { get; init; }
}

/// <summary>One of the five item spell slots in the Vanilla item query contract.</summary>
public sealed class VanillaItemSpellConfiguration
{
    public int? SpellId { get; init; }
    public int? Trigger { get; init; }
    public int? Charges { get; init; }
    public float? PpmRate { get; init; }
    public int? CooldownMs { get; init; }
    public int? Category { get; init; }
    public int? CategoryCooldownMs { get; init; }
}

/// <summary>The validated name and item_template overrides produced from one request.</summary>
public sealed record ValidatedVanillaItemBuildConfiguration(
    string? Name,
    IReadOnlyDictionary<string, string> Overrides);

/// <summary>
/// Converts the public request contract into the literal numeric/string overrides accepted by
/// <see cref="WeaponItemTemplateSql"/>. This is deliberately the only translation path: callers
/// never pass arbitrary column names or SQL-shaped strings through from the browser.
/// </summary>
public static class VanillaItemBuildConfigurationTranslator
{
    public const int MaxStatSlots = 10;
    public const int MaxSpellSlots = 5;

    /// <summary>item_template carries dmg_min1..5 / dmg_max1..5 / dmg_type1..5. Only slot 1 is
    /// configurable; the rest are cleared whenever damage is written so an inherited second damage
    /// line cannot stack on top of a fresh roll.</summary>
    public const int DamageSlots = 5;

    // Vanilla class masks use (class id - 1), and class ids 6 and 10 do not exist.
    // Warrior, Paladin, Hunter, Rogue, Priest, Shaman, Mage, Warlock, Druid.
    public const int VanillaPlayableClassMask =
        (1 << 0) | (1 << 1) | (1 << 2) | (1 << 3) | (1 << 4) |
        (1 << 6) | (1 << 7) | (1 << 8) | (1 << 10);

    // Human, Orc, Dwarf, Night Elf, Undead, Tauren, Gnome, Troll.
    public const int VanillaPlayableRaceMask = 0xFF;

    private const long MediumIntUnsignedMax = 16_777_215;

    // These are the only direct stat types Player::_ApplyItemBonuses handles for 1.12.
    // Hit/crit/dodge/parry/defense/AP/spell power/etc. are on-equip aura spells instead.
    private static readonly HashSet<int> DirectStatTypes = [0, 1, 3, 4, 5, 6, 7];

    // A forged weapon supports the three executable item effects: use, passive equip, and
    // chance-on-hit. Recipe-learning/soulstone/client-special triggers do not belong here.
    private static readonly HashSet<int> WeaponSpellTriggers = [0, 1, 2];

    // The forged assets are static melee weapons; these are the weapon inventory bindings the
    // pipeline and its TBC preservation path support.
    /// <summary>Equippable vanilla weapon slots: 13 one-hand, 17 two-hand, 21 main hand, 22 off
    /// hand, 15 ranged (bows), 25 thrown, 26 ranged-right (guns, crossbows, wands). Which of
    /// these a given family accepts is enforced per family by the controller.</summary>
    private static readonly HashSet<int> WeaponInventoryTypes = [13, 14, 15, 17, 21, 22, 25, 26];

    /// <summary>Wearable vanilla armor slots the Armor Forge itemizes: 1 head, 3 shoulder,
    /// 5 chest, 6 waist, 7 legs, 8 feet, 9 wrists, 10 hands, 16 back (cloak), 20 robe (long
    /// chest), 23 held-in-off-hand. Shields (14) stay on the weapon side. Passed by the armor
    /// caller as <paramref name="allowedInventoryTypes"/> so this one translator serves both forges.</summary>
    public static readonly IReadOnlySet<int> ArmorInventoryTypes =
        new HashSet<int> { 1, 3, 5, 6, 7, 8, 9, 10, 16, 20, 23 };

    public static bool TryTranslate(
        VanillaItemBuildConfiguration configuration,
        Func<uint, bool>? installedSpellExists,
        Func<int, bool>? requiredSkillExists,
        Func<int, bool>? reputationFactionExists,
        out ValidatedVanillaItemBuildConfiguration? validated,
        out IReadOnlyList<string> errors,
        IReadOnlySet<int>? allowedInventoryTypes = null,
        string? inventoryTypeError = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var problems = new List<string>();
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal);

        string? configuredName = null;
        if (configuration.Name is not null)
        {
            configuredName = configuration.Name.Trim();
            if (configuredName.Length is < 1 or > 255)
                problems.Add("name must contain 1 to 255 characters when supplied.");
            else if (configuredName.Any(char.IsControl))
                problems.Add("name cannot contain line breaks or other control characters.");
        }

        if (configuration.Description is not null)
        {
            if (configuration.Description.Length > 255)
                problems.Add("description cannot exceed 255 characters.");
            else
                overrides["description"] = configuration.Description;
        }

        AddInt(configuration.Quality, "quality", 0, 6, "quality", overrides, problems);

        var inventoryTypes = allowedInventoryTypes ?? WeaponInventoryTypes;
        if (configuration.InventoryType is int inventoryType)
        {
            if (!inventoryTypes.Contains(inventoryType))
                problems.Add(inventoryTypeError
                    ?? "inventoryType must be a supported Vanilla weapon slot: 13, 17, 21, 22 (melee) or 15, 25, 26 (ranged).");
            else
                overrides["inventory_type"] = IntLiteral(inventoryType);
        }

        // Storage permits bytes, but the forge contract is an equippable Vanilla 1.12 item.
        AddInt(configuration.ItemLevel, "item_level", 1, byte.MaxValue, "itemLevel", overrides, problems);
        AddInt(configuration.RequiredLevel, "required_level", 0, 60, "requiredLevel", overrides, problems);

        if (configuration.DamageMin.HasValue != configuration.DamageMax.HasValue)
        {
            problems.Add("damageMin and damageMax must be supplied together.");
        }
        else if (configuration.DamageMin is float damageMin && configuration.DamageMax is float damageMax)
        {
            if (!float.IsFinite(damageMin) || damageMin < 0)
                problems.Add("damageMin must be a finite non-negative number.");
            if (!float.IsFinite(damageMax) || damageMax < 0)
                problems.Add("damageMax must be a finite non-negative number.");
            if (float.IsFinite(damageMin) && float.IsFinite(damageMax) && damageMax < damageMin)
                problems.Add("damageMax must be greater than or equal to damageMin.");

            if (float.IsFinite(damageMin) && damageMin >= 0 &&
                float.IsFinite(damageMax) && damageMax >= damageMin)
            {
                overrides["dmg_min1"] = FloatLiteral(damageMin);
                overrides["dmg_max1"] = FloatLiteral(damageMax);

                // Slots 2..5 are cleared for the same reason TranslateStats and TranslateSpells clear
                // theirs: the row this lands on is a CLONE of something, and whatever it carried in
                // the higher slots survives an override that only writes slot 1. A vanilla weapon with
                // a second (elemental) damage line, re-rolled here, would keep that line stacked on
                // top of the new roll and hit for more than the budget it was generated against.
                for (int slot = 2; slot <= DamageSlots; slot++)
                {
                    overrides[$"dmg_min{slot}"] = "0";
                    overrides[$"dmg_max{slot}"] = "0";
                    overrides[$"dmg_type{slot}"] = "0";
                }
            }
        }

        AddInt(configuration.DamageType, "dmg_type1", 0, 6, "damageType", overrides, problems);
        AddInt(configuration.DelayMs, "delay", 100, ushort.MaxValue, "delayMs", overrides, problems);

        TranslateStats(configuration.Stats, overrides, problems);

        AddInt(configuration.Armor, "armor", 0, short.MaxValue, "armor", overrides, problems);
        AddLong(configuration.Block, "block", 0, MediumIntUnsignedMax, "block", overrides, problems);
        AddInt(configuration.HolyRes, "holy_res", 0, short.MaxValue, "holyRes", overrides, problems);
        AddInt(configuration.FireRes, "fire_res", 0, short.MaxValue, "fireRes", overrides, problems);
        AddInt(configuration.NatureRes, "nature_res", 0, short.MaxValue, "natureRes", overrides, problems);
        AddInt(configuration.FrostRes, "frost_res", 0, short.MaxValue, "frostRes", overrides, problems);
        AddInt(configuration.ShadowRes, "shadow_res", 0, short.MaxValue, "shadowRes", overrides, problems);
        AddInt(configuration.ArcaneRes, "arcane_res", 0, short.MaxValue, "arcaneRes", overrides, problems);
        AddInt(configuration.MaxDurability, "max_durability", 0, ushort.MaxValue, "maxDurability", overrides, problems);

        AddInt(configuration.Bonding, "bonding", 0, 4, "bonding", overrides, problems);
        AddMask(configuration.AllowableClass, "allowable_class", VanillaPlayableClassMask,
            "allowableClass", overrides, problems);
        AddMask(configuration.AllowableRace, "allowable_race", VanillaPlayableRaceMask,
            "allowableRace", overrides, problems);
        AddInt(configuration.RequiredSkill, "required_skill", 0, ushort.MaxValue,
            "requiredSkill", overrides, problems);
        AddInt(configuration.RequiredSkillRank, "required_skill_rank", 0, 300,
            "requiredSkillRank", overrides, problems);
        if (configuration.RequiredSkill is 0 or null && configuration.RequiredSkillRank is > 0)
            problems.Add("requiredSkillRank must be 0 when requiredSkill is 0.");
        if (configuration.RequiredSkill is > 0 && requiredSkillExists is null)
            problems.Add("requiredSkill cannot be validated because the installed Vanilla SkillLine.dbc catalog is unavailable.");
        else if (configuration.RequiredSkill is > 0 && !requiredSkillExists!(configuration.RequiredSkill.Value))
            problems.Add($"requiredSkill {configuration.RequiredSkill.Value} is not present in the installed Vanilla SkillLine.dbc.");

        AddInt(configuration.RequiredSpell, "required_spell", 0, ushort.MaxValue,
            "requiredSpell", overrides, problems);
        if (configuration.RequiredSpell is > 0 && installedSpellExists is null)
        {
            problems.Add("requiredSpell cannot be validated because the installed Vanilla Spell.dbc catalog is unavailable.");
        }
        else if (configuration.RequiredSpell is > 0 &&
                 !installedSpellExists!((uint)configuration.RequiredSpell.Value))
        {
            problems.Add($"requiredSpell {configuration.RequiredSpell.Value} is not present in the installed Vanilla Spell.dbc.");
        }

        // Vanilla honor ranks are 0 (none) through 14 (Grand Marshal/High Warlord).
        AddInt(configuration.RequiredHonorRank, "required_honor_rank", 0, 14,
            "requiredHonorRank", overrides, problems);
        AddInt(configuration.RequiredReputationFaction, "required_reputation_faction", 0, ushort.MaxValue,
            "requiredReputationFaction", overrides, problems);
        AddInt(configuration.RequiredReputationRank, "required_reputation_rank", 0, 7,
            "requiredReputationRank", overrides, problems);
        if (configuration.RequiredReputationFaction is > 0 && reputationFactionExists is null)
            problems.Add("requiredReputationFaction cannot be validated because the installed Vanilla Faction.dbc catalog is unavailable.");
        else if (configuration.RequiredReputationFaction is > 0 &&
                 !reputationFactionExists!(configuration.RequiredReputationFaction.Value))
            problems.Add($"requiredReputationFaction {configuration.RequiredReputationFaction.Value} is not present in the installed Vanilla Faction.dbc.");

        AddLong(configuration.BuyPrice, "buy_price", 0, uint.MaxValue,
            "buyPrice", overrides, problems);
        AddLong(configuration.SellPrice, "sell_price", 0, uint.MaxValue,
            "sellPrice", overrides, problems);

        TranslateSpells(configuration.Spells, installedSpellExists, overrides, problems);

        errors = problems;
        if (problems.Count > 0)
        {
            validated = null;
            return false;
        }

        validated = new ValidatedVanillaItemBuildConfiguration(configuredName, overrides);
        return true;
    }

    private static void TranslateStats(
        List<VanillaItemStatConfiguration>? stats,
        Dictionary<string, string> overrides,
        List<string> problems)
    {
        if (stats is null)
            return;

        if (stats.Count > MaxStatSlots)
        {
            problems.Add($"stats supports at most {MaxStatSlots} entries.");
            return;
        }

        for (int slot = 1; slot <= MaxStatSlots; slot++)
        {
            overrides[$"stat_type{slot}"] = "0";
            overrides[$"stat_value{slot}"] = "0";
        }

        for (int i = 0; i < stats.Count; i++)
        {
            var stat = stats[i];
            int slot = i + 1;
            if (stat is null)
            {
                problems.Add($"stats[{i}] cannot be null.");
                continue;
            }

            if (stat.Type is not int type)
            {
                problems.Add($"stats[{i}].type is required (Mana is numeric type 0).");
                continue;
            }
            if (!DirectStatTypes.Contains(type))
            {
                problems.Add($"stats[{i}].type {type} is not a direct Vanilla stat; allowed types are 0, 1, 3, 4, 5, 6, and 7. Use an on-equip spell for hit/crit/defense and other effects.");
                continue;
            }
            if (stat.Value is not int value)
            {
                problems.Add($"stats[{i}].value is required.");
                continue;
            }
            if (value is < short.MinValue or > short.MaxValue)
            {
                problems.Add($"stats[{i}].value must fit a signed 16-bit item stat ({short.MinValue}..{short.MaxValue}).");
                continue;
            }
            if (value == 0)
            {
                problems.Add($"stats[{i}].value cannot be zero; remove the empty stat row instead.");
                continue;
            }

            overrides[$"stat_type{slot}"] = IntLiteral(type);
            overrides[$"stat_value{slot}"] = IntLiteral(value);
        }
    }

    private static void TranslateSpells(
        List<VanillaItemSpellConfiguration>? spells,
        Func<uint, bool>? installedSpellExists,
        Dictionary<string, string> overrides,
        List<string> problems)
    {
        if (spells is null)
            return;

        if (spells.Count > MaxSpellSlots)
        {
            problems.Add($"spells supports at most {MaxSpellSlots} entries.");
            return;
        }

        for (int slot = 1; slot <= MaxSpellSlots; slot++)
        {
            overrides[$"spellid_{slot}"] = "0";
            overrides[$"spelltrigger_{slot}"] = "0";
            overrides[$"spellcharges_{slot}"] = "0";
            overrides[$"spellppmrate_{slot}"] = "0";
            overrides[$"spellcooldown_{slot}"] = "-1";
            overrides[$"spellcategory_{slot}"] = "0";
            overrides[$"spellcategorycooldown_{slot}"] = "-1";
        }

        for (int i = 0; i < spells.Count; i++)
        {
            var spell = spells[i];
            int slot = i + 1;
            if (spell is null)
            {
                problems.Add($"spells[{i}] cannot be null.");
                continue;
            }

            if (spell.SpellId is not int spellId || spellId is < 1 or > ushort.MaxValue)
            {
                problems.Add($"spells[{i}].spellId must be 1..{ushort.MaxValue}.");
                continue;
            }
            if (installedSpellExists is null)
            {
                problems.Add($"spells[{i}].spellId cannot be validated because the installed Vanilla Spell.dbc catalog is unavailable.");
                continue;
            }
            if (!installedSpellExists((uint)spellId))
            {
                problems.Add($"spells[{i}].spellId {spellId} is not present in the installed Vanilla Spell.dbc.");
                continue;
            }
            if (spell.Trigger is not int trigger || !WeaponSpellTriggers.Contains(trigger))
            {
                problems.Add($"spells[{i}].trigger must be 0 (use), 1 (on equip), or 2 (chance on hit).");
                continue;
            }

            int charges = spell.Charges ?? 0;
            if (charges is < short.MinValue or > short.MaxValue)
            {
                problems.Add($"spells[{i}].charges must fit the signed 16-bit Vanilla item field ({short.MinValue}..{short.MaxValue}).");
                continue;
            }

            float ppmRate = spell.PpmRate ?? 0;
            if (!float.IsFinite(ppmRate) || ppmRate < 0 || ppmRate > 1000)
            {
                problems.Add($"spells[{i}].ppmRate must be a finite value from 0 through 1000.");
                continue;
            }
            if (trigger != 2 && ppmRate != 0)
            {
                problems.Add($"spells[{i}].ppmRate must be zero unless trigger is 2 (chance on hit).");
                continue;
            }

            int cooldown = spell.CooldownMs ?? -1;
            if (cooldown < -1)
            {
                problems.Add($"spells[{i}].cooldownMs must be -1 (inherit the spell default) or a non-negative millisecond value.");
                continue;
            }

            int category = spell.Category ?? 0;
            if (category is < 0 or > ushort.MaxValue)
            {
                problems.Add($"spells[{i}].category must be 0..{ushort.MaxValue}.");
                continue;
            }

            int categoryCooldown = spell.CategoryCooldownMs ?? -1;
            if (categoryCooldown < -1)
            {
                problems.Add($"spells[{i}].categoryCooldownMs must be -1 (inherit) or a non-negative millisecond value.");
                continue;
            }

            overrides[$"spellid_{slot}"] = IntLiteral(spellId);
            overrides[$"spelltrigger_{slot}"] = IntLiteral(trigger);
            overrides[$"spellcharges_{slot}"] = IntLiteral(charges);
            overrides[$"spellppmrate_{slot}"] = FloatLiteral(ppmRate);
            overrides[$"spellcooldown_{slot}"] = IntLiteral(cooldown);
            overrides[$"spellcategory_{slot}"] = IntLiteral(category);
            overrides[$"spellcategorycooldown_{slot}"] = IntLiteral(categoryCooldown);
        }
    }

    private static void AddInt(
        int? value,
        string column,
        int minimum,
        int maximum,
        string field,
        Dictionary<string, string> overrides,
        List<string> problems)
    {
        if (value is not int actual)
            return;
        if (actual < minimum || actual > maximum)
        {
            problems.Add($"{field} must be {minimum}..{maximum}.");
            return;
        }
        overrides[column] = IntLiteral(actual);
    }

    private static void AddLong(
        long? value,
        string column,
        long minimum,
        long maximum,
        string field,
        Dictionary<string, string> overrides,
        List<string> problems)
    {
        if (value is not long actual)
            return;
        if (actual < minimum || actual > maximum)
        {
            problems.Add($"{field} must be {minimum}..{maximum}.");
            return;
        }
        overrides[column] = actual.ToString(CultureInfo.InvariantCulture);
    }

    private static void AddMask(
        int? value,
        string column,
        int validBits,
        string field,
        Dictionary<string, string> overrides,
        List<string> problems)
    {
        if (value is not int actual)
            return;
        if (actual != -1 && (actual <= 0 || (actual & ~validBits) != 0))
        {
            problems.Add($"{field} must be -1 (all) or a non-empty mask containing only Vanilla playable bits (0x{validBits:X}).");
            return;
        }
        overrides[column] = IntLiteral(actual);
    }

    private static string IntLiteral(int value) => value.ToString(CultureInfo.InvariantCulture);
    private static string FloatLiteral(float value) => value.ToString("R", CultureInfo.InvariantCulture);
}
