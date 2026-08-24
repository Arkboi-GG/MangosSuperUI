using MangosSuperUI.Services;
using MangosSuperUI.Services.WeaponForge;

namespace MangosSuperUI.Services.ArmorForge;

/// <summary>
/// Builds ItemSet.dbc rows for forged tier sets (ARMOR_FORGE.md §4). A tier set is a group of forged
/// armor pieces plus a table of set-spell bonuses that activate at piece-count thresholds.
///
/// Vanilla 1.12.1 ItemSet.dbc layout — 45 uint32 fields, 180 bytes/record (server read format
/// <c>dssssssssxxxxxxxxxxxxxxxxxxiiiiiiiiiiiiiiiiii</c>, mangos DBCfmt):
///
///   [0]       m_ID
///   [1..8]    m_name_lang[8]        localized name stringrefs (enUS = [1])
///   [9]       m_name_flags
///   [10..26]  m_itemID[17]          the set's item entries (client tooltip membership)
///   [27..34]  m_setSpellID[8]       bonus spell ids
///   [35..42]  m_setThreshold[8]     piece counts each bonus activates at
///   [43]      m_requiredSkill
///   [44]      m_requiredSkillRank
///
/// IMPORTANT (server vs client): the CLIENT reads this DBC from the mounted patch to render the set
/// tooltip. The SERVER reads its OWN dbc directory (sItemSetStore) to actually apply the bonuses, and
/// DBCs are loaded once at startup — so a forged set's bonuses only take effect after the server (and
/// client) restart, and only if the same ItemSet.dbc is deployed to the server's dbc directory
/// (ArmorForge:ServerDbcPath). item_template.set_id on each piece is what the server counts.
/// </summary>
public static class ArmorItemSetDbc
{
    public const int RecordSize = 180;
    public const int FieldCount = 45;

    public const int F_Id = 0;
    public const int F_NameEnUs = 1;      // [1..8] locales, we write enUS only
    public const int F_NameFlags = 9;
    public const int F_ItemId0 = 10;      // [10..26] itemID[17]
    public const int MaxItems = 17;
    public const int F_SetSpell0 = 27;    // [27..34] setSpellID[8]
    public const int F_SetThreshold0 = 35; // [35..42] setThreshold[8]
    public const int MaxBonuses = 8;
    public const int F_RequiredSkill = 43;
    public const int F_RequiredSkillRank = 44;

    /// <summary>Set-spell floor. Custom sets allocate ids at or above this to avoid colliding with the
    /// ~180 stock vanilla sets (max stock id ≈ 500). Kept well clear.</summary>
    public const int CustomSetIdFloor = 5000;

    /// <summary>Append every set in <paramref name="sets"/> to the base ItemSet.dbc and return the new
    /// bytes. Guards the schema (180-byte record) and refuses to overwrite an existing set id.</summary>
    public static byte[] Build(byte[] baseItemSetDbc, IReadOnlyList<ArmorSetDefinition> sets)
    {
        if (baseItemSetDbc is null || baseItemSetDbc.Length == 0)
            throw new ArgumentException("Base ItemSet.dbc bytes are required.", nameof(baseItemSetDbc));

        var dbc = DbcWriterService.ReadDbc(baseItemSetDbc, ArmorNaming.ItemSetMember);
        if (dbc.RecordSize != RecordSize)
            throw new InvalidOperationException(
                $"Base ItemSet.dbc record size {dbc.RecordSize} != expected {RecordSize} (45 fields). " +
                "Refusing to write set rows into an unknown schema.");

        foreach (var set in sets.OrderBy(s => s.SetId))
        {
            if (dbc.GetRow((uint)set.SetId) is not null)
                throw new InvalidOperationException(
                    $"ItemSet id {set.SetId} already exists in the base DBC; the reservation registry should have prevented this.");

            var row = new uint[dbc.FieldCount];
            row[F_Id] = (uint)set.SetId;
            row[F_NameEnUs] = string.IsNullOrEmpty(set.Name) ? 0u : dbc.AddString(set.Name);
            row[F_NameFlags] = 0;

            for (int i = 0; i < set.ItemEntries.Count && i < MaxItems; i++)
                row[F_ItemId0 + i] = (uint)set.ItemEntries[i];

            for (int i = 0; i < set.Bonuses.Count && i < MaxBonuses; i++)
            {
                row[F_SetSpell0 + i] = (uint)set.Bonuses[i].SpellId;
                row[F_SetThreshold0 + i] = (uint)set.Bonuses[i].Threshold;
            }

            row[F_RequiredSkill] = (uint)set.RequiredSkill;
            row[F_RequiredSkillRank] = (uint)set.RequiredSkillRank;

            dbc.AddRow(row);
        }

        return dbc.Write();
    }
}

/// <summary>One tier-set bonus: a spell that activates once <see cref="Threshold"/> pieces are equipped.</summary>
public sealed record ArmorSetBonus(int Threshold, int SpellId);

/// <summary>A forged tier set: the pieces (item entries), the bonus table, and any skill requirement.</summary>
public sealed class ArmorSetDefinition
{
    public required int SetId { get; init; }
    public required string Name { get; init; }
    /// <summary>Forged piece item_template entries that belong to the set (max 17 in the DBC).</summary>
    public required IReadOnlyList<int> ItemEntries { get; init; }
    /// <summary>Threshold → spell bonuses (max 8), sorted ascending by threshold by convention.</summary>
    public IReadOnlyList<ArmorSetBonus> Bonuses { get; init; } = Array.Empty<ArmorSetBonus>();
    public int RequiredSkill { get; init; }
    public int RequiredSkillRank { get; init; }
}
