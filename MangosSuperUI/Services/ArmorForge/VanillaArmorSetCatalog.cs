using MangosSuperUI.Services.WeaponForge;

namespace MangosSuperUI.Services.ArmorForge;

/// <summary>One stock 1.12 item set, as the mounted client's <c>ItemSet.dbc</c> defines it.</summary>
/// <param name="SetId">Stock set id. Measured range on a real 1.12 mount: 1..551, 172 sets.</param>
/// <param name="MemberEntries">The set's <c>item_template.entry</c> values, in DBC order. Every stock
/// vanilla set populates this (measured: 0 of 172 sets have an empty member list), so the DBC alone is
/// a sufficient membership source and the browse does not need to group by <c>item_template.set_id</c>.</param>
/// <param name="Bonuses">(spellId, pieceThreshold) pairs. Unlike the TBC/WotLK lanes — whose source
/// spells do not exist in a vanilla core — these are VANILLA spell ids, so a clone can offer them back
/// to the operator as the starting bonus table.</param>
public sealed record VanillaSetInfo(
    int SetId,
    string Name,
    IReadOnlyList<uint> MemberEntries,
    IReadOnlyList<VanillaSetBonusInfo> Bonuses,
    int RequiredSkill,
    int RequiredSkillRank);

public sealed record VanillaSetBonusInfo(int SpellId, int Threshold);

/// <summary>
/// The stock vanilla item sets, read from the mounted client's <c>DBFilesClient\ItemSet.dbc</c>.
///
/// This is the vanilla clone lane's answer to <see cref="LegacyArmorCatalog"/>'s set index. It is much
/// smaller because the vanilla lane has no client archive to walk and no art to classify: set identity,
/// membership and bonuses all live in one 172-row DBC, and everything else the browse needs (name,
/// quality, item level, display id, slot) comes from the live <c>item_template</c> rows the browse is
/// already reading.
///
/// The DBC is read with <c>skipArchive: patch-6</c> so the forge's OWN patch — which appends forged sets
/// to the same file — is excluded. What this catalog reports is Blizzard's sets, never ours.
/// </summary>
public sealed class VanillaArmorSetCatalog
{
    // ItemSet.dbc field indices (45 fields, 180 bytes/record). Same layout ArmorItemSetDbc writes; see
    // its header for the full annotated schema.
    private const int F_Id = ArmorItemSetDbc.F_Id;
    private const int F_NameEnUs = ArmorItemSetDbc.F_NameEnUs;
    private const int F_ItemId0 = ArmorItemSetDbc.F_ItemId0;
    private const int F_SetSpell0 = ArmorItemSetDbc.F_SetSpell0;
    private const int F_SetThreshold0 = ArmorItemSetDbc.F_SetThreshold0;
    private const int F_RequiredSkill = ArmorItemSetDbc.F_RequiredSkill;
    private const int F_RequiredSkillRank = ArmorItemSetDbc.F_RequiredSkillRank;

    /// <summary>Item level at which a vanilla set counts as a headline set, mirroring
    /// <c>TbcArmorCatalog</c>'s 120 (T4) and <c>WotlkArmorCatalog</c>'s 200 (T7). 63 is where vanilla's
    /// endgame sets begin — the Tier 0 dungeon sets sit exactly there.</summary>
    public const int FeaturedMinItemLevel = 63;

    /// <summary>...and a headline set must also be a real multi-piece armor set. This second test is
    /// what the TBC/WotLK lanes do not need, and leaving it out was measurably wrong: the direct port
    /// (<c>quality &gt;= 4 &amp;&amp; ilvl &gt;= 66</c>) promoted the nine AQ20 sets — a cloak plus a ring and a
    /// weapon, so ONE cloneable armor piece each — and, because their cloak is ilvl 67, ranked all nine
    /// ABOVE Lawbringer/Judgement at 66. Measured over the real 1.12 ItemSet.dbc joined to the shipped
    /// item catalogue: 163 sets have a cloneable armor member; <c>pieces &gt;= 5 &amp;&amp; ilvl &gt;= 63</c> gives
    /// 103 featured / 60 other and promotes every headline family — T0, T0.5, T1, T2, T2.5 (AQ40),
    /// T3 (Naxxramas) and the Rank 10-14 PvP sets — while the one-piece cards fall to the drawer.
    ///
    /// Quality is deliberately NOT part of it, unlike the import lanes: the Tier 0 dungeon sets are RARE
    /// (Vestments of the Devout, Magister's Regalia, Ironweave Battlesuit are all quality 3, ilvl 63),
    /// and in vanilla those are tier sets by every name a player uses.</summary>
    public const int FeaturedMinArmorPieces = 5;

    private readonly MpqReaderService _mpq;
    private readonly ILogger<VanillaArmorSetCatalog> _logger;

    private readonly object _lock = new();
    private List<VanillaSetInfo>? _sets;
    private Dictionary<uint, VanillaSetInfo>? _byMember;
    private string? _error;

    public VanillaArmorSetCatalog(MpqReaderService mpq, ILogger<VanillaArmorSetCatalog> logger)
    {
        _mpq = mpq;
        _logger = logger;
    }

    /// <summary>Every stock set, or an empty list when the client is not mounted (with <see cref="Error"/>
    /// explaining why). Never throws — a missing DBC must degrade the set cards, not the whole browse.</summary>
    public IReadOnlyList<VanillaSetInfo> Sets()
    {
        lock (_lock)
        {
            EnsureLoadedLocked();
            return _sets ?? (IReadOnlyList<VanillaSetInfo>)Array.Empty<VanillaSetInfo>();
        }
    }

    public VanillaSetInfo? Get(int setId) => Sets().FirstOrDefault(s => s.SetId == setId);

    /// <summary>The set an item belongs to, by item entry, ACCORDING TO THE CLIENT DBC — which is what
    /// renders the tooltip. It is not the last word: the SERVER counts <c>item_template.set_id</c>, and
    /// the WotLK lane already had to learn that the column wins when they disagree (ARMOR_FORGE.md
    /// §4b-ii). The browse therefore unions this with the column rather than trusting it alone; nothing
    /// here has measured that stock 1.12 data agrees, only that it is expected to.</summary>
    public VanillaSetInfo? SetForEntry(uint entry)
    {
        lock (_lock)
        {
            EnsureLoadedLocked();
            return _byMember is not null && _byMember.TryGetValue(entry, out var s) ? s : null;
        }
    }

    /// <summary>Why the catalog is empty, when it is. Null once loaded successfully.</summary>
    public string? Error { get { lock (_lock) { EnsureLoadedLocked(); return _error; } } }

    private void EnsureLoadedLocked()
    {
        if (_sets is not null) return;

        try
        {
            // skipArchive patch-6: our own patch appends forged sets to this very file, and a forged set
            // is not a clone source. Same predicate ComputeSetIdFloorAsync uses when it reads the DBC to
            // find the next free set id.
            byte[]? bytes = _mpq.ExtractFile(
                ArmorNaming.ItemSetMember,
                skipArchive: n => n.StartsWith("patch-6", StringComparison.OrdinalIgnoreCase));
            if (bytes is null || bytes.Length == 0)
            {
                _sets = new List<VanillaSetInfo>();
                _byMember = new Dictionary<uint, VanillaSetInfo>();
                _error = "ItemSet.dbc is not in the mounted 1.12 client, so stock sets cannot be listed.";
                return;
            }

            var dbc = DbcWriterService.ReadDbc(bytes, ArmorNaming.ItemSetMember);
            if (dbc.RecordSize != ArmorItemSetDbc.RecordSize)
            {
                _sets = new List<VanillaSetInfo>();
                _byMember = new Dictionary<uint, VanillaSetInfo>();
                _error = $"ItemSet.dbc record size {dbc.RecordSize} != expected {ArmorItemSetDbc.RecordSize} " +
                         "— refusing to read set membership out of an unknown schema.";
                return;
            }

            var list = new List<VanillaSetInfo>(dbc.RecordCount);
            var byMember = new Dictionary<uint, VanillaSetInfo>();
            foreach (var row in dbc.GetAllRows())
            {
                if (row.Length < ArmorItemSetDbc.FieldCount) continue;

                var members = new List<uint>(ArmorItemSetDbc.MaxItems);
                for (int i = 0; i < ArmorItemSetDbc.MaxItems; i++)
                    if (row[F_ItemId0 + i] != 0) members.Add(row[F_ItemId0 + i]);
                if (members.Count == 0) continue;   // nothing to clone

                var bonuses = new List<VanillaSetBonusInfo>(ArmorItemSetDbc.MaxBonuses);
                for (int i = 0; i < ArmorItemSetDbc.MaxBonuses; i++)
                {
                    uint spell = row[F_SetSpell0 + i], threshold = row[F_SetThreshold0 + i];
                    if (spell != 0 && threshold != 0) bonuses.Add(new VanillaSetBonusInfo((int)spell, (int)threshold));
                }

                var info = new VanillaSetInfo(
                    (int)row[F_Id],
                    dbc.ReadString(row[F_NameEnUs]),
                    members,
                    bonuses.OrderBy(b => b.Threshold).ToList(),
                    (int)row[F_RequiredSkill],
                    (int)row[F_RequiredSkillRank]);

                list.Add(info);
                // First set wins a contested entry. Stock membership does not overlap, so this only
                // guards against a malformed mount rather than expressing a real precedence.
                foreach (var m in members) byMember.TryAdd(m, info);
            }

            _sets = list;
            _byMember = byMember;
            _error = null;
            _logger.LogInformation("Vanilla armor: indexed {Count} stock item sets from ItemSet.dbc", list.Count);
        }
        catch (Exception ex)
        {
            _sets = new List<VanillaSetInfo>();
            _byMember = new Dictionary<uint, VanillaSetInfo>();
            _error = ex.Message;
            _logger.LogWarning(ex, "Vanilla armor: reading ItemSet.dbc failed");
        }
    }
}
