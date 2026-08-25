using System.Text.RegularExpressions;
using MangosSuperUI.Services;
using MangosSuperUI.Services.WeaponForge;

namespace MangosSuperUI.Services.ArmorForge;

/// <summary>
/// The TBC-armor browse surface (ARMOR_FORGE.md §3): joins the shipped TBC item catalog
/// (<see cref="TbcItemCatalog"/>, real item names) to the user's own mounted TBC (2.4.3) client
/// (<see cref="TbcMpqSource"/>) and exposes:
///
///   • <see cref="Browse"/> — every class-4 armor piece the forge can handle, junk-filtered (the
///     open-source tbc-db carries ~13% GM/test/deprecated rows — "130 Epic Warrior …", "NN TEST
///     Green …", "63 Green Frost Belt", "Monster -", "[PH]", "Deprecated", "OLD…" — filtered by
///     <see cref="JunkName"/>), each tagged with its TBC set when it belongs to one.
///   • <see cref="Sets"/> — the TBC sets, read from the CLIENT's own ItemSet.dbc (53 fields /
///     212 bytes in 2.4.3: name at [1], member item ids at [18..34]) — so set grouping works for
///     any user with just their TBC install, no TBC database needed. We read sets only to know which
///     pieces belong together visually; bonuses are vanilla's business (never imported).
///   • <see cref="GetDisplayRow"/> — the raw TBC ItemDisplayInfo row (24 fields / 96 bytes = the
///     vanilla 23 + a trailing particleColorID; fields 0..22 identical) for the importer.
///
/// Shields are deliberately NOT here — they're the Weapon Forge's (class 4 / subclass 6, TBC import
/// there). Weapon Forge browses weapons + shields; Armor Forge browses armor. One catalog file.
/// </summary>
public abstract class LegacyArmorCatalog
{
    private readonly LegacyItemCatalog _catalog;
    private readonly LegacyMpqSource _mpq;
    private readonly ILogger _logger;

    private readonly object _lock = new();
    private string? _indexedPath;
    private DbcWriterService? _displayDbc;
    private int _componentBase = 14;
    private Dictionary<uint, LegacySetInfo>? _sets;
    private Dictionary<uint, uint>? _entryToSet;
    private List<LegacyArmorEntry>? _browse;

    /// <summary>Names the open-source tbc-db/azerothcore dumps mark as GM/test/placeholder rows.
    /// Word-bounded TEST so legitimate names ("Protector", "Contested") survive.
    ///
    /// <c>^\d</c> is the big one: Blizzard's internal gear-up sets are named
    /// "&lt;ilvl&gt; &lt;quality&gt; &lt;theme&gt; &lt;slot&gt;" — "63 Green Frost Belt",
    /// "63 Blue Shadow Gloves", "90 Epic Rogue Cap", "5% Test Speed Boots". Measured over both
    /// shipped catalogs, EVERY class-4 name beginning with a digit is one of these (1,217 of 14,591
    /// TBC rows, 133 of 23,578 WotLK rows; no real armor name starts with a digit), so the whole
    /// leading-digit family goes, not just the ones that happen to say TEST or Epic Warrior.</summary>
    private static readonly Regex JunkName = new(
        @"^\s*$|^\d|^Monster\s*-|^OLD[A-Z]|^zz|\bTEST\b|\(test\)|^Deprecated|\[PH\]|\bPH\b|\(DND\)|DO NOT USE|^Unused\b|^QA Test|\bEpic Warrior\b|Item Properties Test|^NPC\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    protected LegacyArmorCatalog(LegacyItemCatalog catalog, LegacyMpqSource mpq, ILogger logger)
    {
        _catalog = catalog;
        _mpq = mpq;
        _logger = logger;
    }

    /// <summary>Item level at which this lane's sets count as "current-expansion endgame" — the
    /// tier/arena sets the owner actually came here for. An epic set at or above it is
    /// <see cref="LegacySetInfo.Featured"/>; everything else (levelling greens, dungeon blues, crafted
    /// sets, and the PREVIOUS expansions' tiers, which ship in the later client's ItemSet.dbc too)
    /// falls into the browse's "Other sets" drawer.
    ///
    /// Measured against the shipped catalogs joined to the client's ItemSet.dbc:
    /// TBC ≥120 ⇒ 73 featured / 259 other — exactly T4 (120), Gladiator (123), T5 (133), T6 (154)
    /// plus the five crafted epic sets at 120. WotLK ≥200 ⇒ ~350 / ~460 — T7 (200/213) through
    /// T10 (251/264/277) plus EVERY arena season, with T1–T6 dropping out; the count grew when the
    /// catalog's item_template.itemset union + variant splitting (steps 2b/3b below) surfaced the
    /// seasons ItemSet.dbc alone does not list.</summary>
    protected abstract int FeaturedMinItemLevel { get; }

    /// <summary>The same threshold, for the browse payload's caption ("tier &amp; arena, ilvl 120+").</summary>
    public int FeaturedItemLevelForDisplay => FeaturedMinItemLevel;

    /// <summary>Lane key ("tbc" / "wotlk") and human label of the mounted later client.</summary>
    public string Key => _mpq.Key;
    public string Label => _mpq.Label;
    /// <summary>The underlying mount (for callers that need version-aware model parsing).</summary>
    public LegacyMpqSource Mpq => _mpq;

    public (bool Configured, string? Path, int ArchiveCount, string? Error) Status() => _mpq.Status();

    /// <summary>All browsable armor (junk filtered), sorted by name. Cached per mounted path.</summary>
    public IReadOnlyList<LegacyArmorEntry> Browse()
    {
        lock (_lock)
        {
            EnsureIndexedLocked();
            return _browse ?? (IReadOnlyList<LegacyArmorEntry>)Array.Empty<LegacyArmorEntry>();
        }
    }

    /// <summary>The TBC sets that have at least one browsable armor member.</summary>
    public IReadOnlyList<LegacySetInfo> Sets()
    {
        lock (_lock)
        {
            EnsureIndexedLocked();
            return _sets?.Values.Where(s => s.MemberEntries.Count > 0).OrderBy(s => s.Name).ToList()
                ?? new List<LegacySetInfo>();
        }
    }

    public LegacySetInfo? GetSet(uint setId)
    {
        lock (_lock) { EnsureIndexedLocked(); return _sets != null && _sets.TryGetValue(setId, out var s) ? s : null; }
    }

    public LegacyArmorEntry? FindEntry(uint entry)
    {
        lock (_lock) { EnsureIndexedLocked(); return _browse?.FirstOrDefault(e => e.Entry == entry); }
    }

    /// <summary>Raw TBC ItemDisplayInfo row for a display id, decoded. Null when unmounted/missing.</summary>
    public LegacyDisplayRow? GetDisplayRow(uint displayId)
    {
        lock (_lock)
        {
            EnsureIndexedLocked();
            if (_displayDbc is null) return null;
            var row = _displayDbc.GetRow(displayId);
            if (row is null) return null;
            // The second inventory icon (field 6) shifts EVERY field ≥ 6 up by one. _componentBase
            // (14 or 15) is detected by content in EnsureIndexedLocked; apply that shift uniformly to
            // geoset/sound/helmet-vis AND the component stems. A stock 24-field 2.4.3 record HAS the
            // icon (base 15), so reading these at the vanilla offsets would be one column short.
            int shift = _componentBase - 14;
            uint F(int vanillaIndex) { int f = vanillaIndex >= 6 ? vanillaIndex + shift : vanillaIndex; return f < row.Length ? row[f] : 0u; }
            var comps = new string[8];
            for (int s = 0; s < 8; s++) comps[s] = _displayDbc.ReadString(F(14 + s));
            return new LegacyDisplayRow
            {
                DisplayId = displayId,
                ModelName1 = _displayDbc.ReadString(row[1]),
                ModelName2 = _displayDbc.ReadString(row[2]),
                TextureName1 = _displayDbc.ReadString(row[3]),
                TextureName2 = _displayDbc.ReadString(row[4]),
                IconStem = _displayDbc.ReadString(row[5]),
                GeosetGroup = new[] { (int)F(6), (int)F(7), (int)F(8) },
                GroupSoundIndex = F(11),
                HelmetVis0 = F(12),
                HelmetVis1 = F(13),
                ComponentPartials = comps,
            };
        }
    }

    /// <summary>Extract a later-client member (BLP/M2/skin) — convenience passthrough for the importer.</summary>
    public byte[]? ExtractFile(string mpqPath) => _mpq.ExtractFile(mpqPath);

    /// <summary>Version-aware model parse (TBC inline views / WotLK v264 + .skin) — see
    /// <see cref="LegacyMpqSource.LoadM2"/>.</summary>
    public M2Model? LoadM2(string m2MpqPath) => _mpq.LoadM2(m2MpqPath);

    // ── indexing ───────────────────────────────────────────────────────

    private void EnsureIndexedLocked()
    {
        var path = _mpq.ConfiguredPath;
        if (path is not null && string.Equals(_indexedPath, path, StringComparison.OrdinalIgnoreCase) && _browse is not null)
            return;

        _indexedPath = path;
        _displayDbc = null; _sets = null; _entryToSet = null; _browse = null;

        // 1) ItemDisplayInfo (optional for browse; required for import).
        var idi = path is null ? null : _mpq.ExtractFile(WeaponNaming.ItemDisplayInfoMember);
        if (idi is { Length: > 0 })
        {
            try
            {
                _displayDbc = DbcWriterService.ReadDbc(idi, Key + "-armor:" + WeaponNaming.ItemDisplayInfoMember);
                // Detect the second inventory icon by CONTENT, not field count: a 24-field record is
                // ambiguous (stock 2.4.3 has the icon → base 15; a particleColorID-stripped WotLK does
                // too), and the old "FieldCount >= 25" guess read stock TBC one column short. See
                // ItemDisplayInfoLayout.
                _componentBase = ItemDisplayInfoLayout.DetectComponentBase(_displayDbc);
                _logger.LogInformation(
                    "{Label} armor: ItemDisplayInfo {Fields} fields → component base {Base} (second inventory icon {Icon})",
                    Label, _displayDbc.FieldCount, _componentBase, _componentBase == 15 ? "present" : "absent");
            }
            catch (Exception ex) { _logger.LogWarning(ex, "{Label} armor: ItemDisplayInfo.dbc parse failed", Label); }
        }

        // 2) ItemSet.dbc → set membership (client-derived).
        _sets = new Dictionary<uint, LegacySetInfo>();
        _entryToSet = new Dictionary<uint, uint>();
        var isb = path is null ? null : _mpq.ExtractFile(ArmorNaming.ItemSetMember);
        if (isb is { Length: > 0 })
        {
            try
            {
                var dbc = DbcWriterService.ReadDbc(isb, Key + "-armor:" + ArmorNaming.ItemSetMember);
                // 2.4.3 and 3.3.5a: 53 fields — [1..16] name locales, [17] flags, [18..34] itemID[17].
                // 1.12:  45 fields — [1..8] name, [9] flags, [10..26] itemID[17]. Detect by width.
                int itemBase = dbc.FieldCount >= 53 ? 18 : 10;
                foreach (var row in dbc.GetAllRows())
                {
                    uint setId = row[0];
                    string name = dbc.ReadString(row[1]);
                    var members = new List<uint>();
                    for (int i = 0; i < 17 && itemBase + i < row.Length; i++)
                        if (row[itemBase + i] != 0) members.Add(row[itemBase + i]);
                    if (members.Count == 0) continue;
                    var info = new LegacySetInfo { SetId = setId, Name = name, AllItemEntries = members, MemberEntries = new List<uint>() };
                    _sets[setId] = info;
                    foreach (var m in members) _entryToSet[m] = setId;
                }
            }
            catch (Exception ex) { _logger.LogWarning(ex, "{Label} armor: ItemSet.dbc parse failed — set grouping unavailable", Label); }
        }

        // 2b) Catalog-declared membership (item_template.itemset). From 3.3.5a on Blizzard stopped
        //     maintaining ItemSet.dbc's itemID[17] for variants: a row lists only the base items
        //     (arena Season 5, tier-10 ilvl 251) and every later season / higher-ilvl version links
        //     to its set on the ITEM instead. Union those in so Furious/Relentless/Wrathful arena
        //     gear and the Sanctified tier versions group like everything else.
        //
        //     The column OVERRIDES DBC membership on conflict: the T9 DBC rows are themselves wrong —
        //     measured on 3.3.5a, row 823 "Worldbreaker Battlegear" (enhancement) lists 46303
        //     (a Garb Spaulders) and 46307 (a Regalia Kilt) among its five, which mixed the three
        //     shaman spec sets into 7- and 10-piece cards. item_template.itemset is what the server
        //     itself counts for set bonuses, so it is the authority. Set ids the client's DBC does
        //     not know are ignored, and the TBC catalog has no setId column (2.4.3 member lists are
        //     complete), so the TBC lane is untouched either way.
        foreach (var it in _catalog.Items)
            if (it.SetId != 0 && _sets.ContainsKey(it.SetId))
                _entryToSet[it.Entry] = it.SetId;

        // 3) Browse list.
        var list = new List<LegacyArmorEntry>();
        foreach (var it in _catalog.Items)
        {
            if (it.ItemClass != 4) continue;
            if (JunkName.IsMatch(it.Name)) continue;
            var key = ArmorTypeCatalog.TypeKeyFor(it.ItemClass, it.Subclass, it.InventoryType);
            if (key is null) continue;
            var profile = ArmorTypeCatalog.Get(key);
            uint setId = _entryToSet.TryGetValue(it.Entry, out var sid) ? sid : 0;
            var e = new LegacyArmorEntry
            {
                Entry = it.Entry, Name = it.Name, DisplayId = it.DisplayId, Quality = it.Quality,
                FamilyKey = key, FamilyLabel = profile.Label, RenderKind = profile.RenderKind,
                Material = ArmorTypeCatalog.MaterialForSubclass(it.Subclass), InventoryType = it.InventoryType,
                ItemLevel = it.ItemLevel, RequiredLevel = it.RequiredLevel,
                SetId = setId, SetName = setId != 0 && _sets.TryGetValue(setId, out var s) ? s.Name : null,
            };
            list.Add(e);
            if (setId != 0) ((List<uint>)_sets[setId].MemberEntries).Add(it.Entry);
        }

        // 3b) Variant splitting. After the catalog union, a WotLK set row can hold several copies of
        //     each slot — set 767 "Gladiator's Redemption" is S5 base + Savage/Hateful/Deadly/Furious/
        //     Relentless/Wrathful, a T10 row is ilvl 251/264/277. One card with 30+ chest-to-boots
        //     pieces is unusable, so a set with duplicated slots splits into per-variant virtual sets
        //     keyed by (season adjective before the set name's first word, item level):
        //     "Wrathful " + "Gladiator's Redemption" becomes its own 5-piece set. Sets without slot
        //     duplicates (classic mixed-ilvl sets like Shadowcraft) are left alone. Virtual ids are
        //     setId·1000+ilvl — above the client DBC's own id range, and resolvable through
        //     <see cref="GetSet"/> so the set-import flow treats them like any real set.
        var byEntryIdx = list.ToDictionary(e => e.Entry);
        var remap = new Dictionary<uint, (uint SetId, string Name)>();
        foreach (var baseSet in _sets.Values.ToList())
        {
            var members = baseSet.MemberEntries.Where(byEntryIdx.ContainsKey).Select(m => byEntryIdx[m]).ToList();
            if (members.Count == 0 || members.GroupBy(m => m.InventoryType).All(g => g.Count() == 1)) continue;
            string firstWord = baseSet.Name.Split(' ').FirstOrDefault() ?? "";
            string AdjectiveOf(string itemName)
            {
                if (firstWord.Length == 0) return "";
                int idx = itemName.IndexOf(firstWord, StringComparison.OrdinalIgnoreCase);
                return idx > 0 ? itemName[..idx] : "";
            }
            var groups = members.GroupBy(m => (Adjective: AdjectiveOf(m.Name), m.ItemLevel)).ToList();
            if (groups.Count <= 1) continue;

            _sets.Remove(baseSet.SetId);
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var g in groups.OrderBy(x => x.Key.ItemLevel).ThenBy(x => x.Key.Adjective, StringComparer.OrdinalIgnoreCase))
            {
                uint vid = baseSet.SetId * 1000u + (uint)Math.Clamp(g.Key.ItemLevel, 0, 999);
                while (_sets.ContainsKey(vid)) vid++;
                string vname = g.Key.Adjective.Length > 0 ? g.Key.Adjective + baseSet.Name : baseSet.Name;
                if (!taken.Add(vname)) { vname = $"{vname} (item level {g.Key.ItemLevel})"; taken.Add(vname); }
                // Same name twice inside one variant = two purchase paths for the same piece (T10 251
                // ships under both an emblem id and a token id). One member per name keeps the card —
                // and a whole-set import — at one piece per slot; prefer the id the DBC itself lists.
                var pieces = g.GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(ng => ng.OrderByDescending(m => baseSet.AllItemEntries.Contains(m.Entry)).ThenBy(m => m.Entry).First())
                    .ToList();
                var entries = pieces.Select(m => m.Entry).ToList();
                _sets[vid] = new LegacySetInfo { SetId = vid, Name = vname, AllItemEntries = entries, MemberEntries = entries.ToList() };
                // Every group member (dropped duplicates included) points at the virtual set, so a
                // name search for a duplicate id still lands on the right card.
                foreach (var m in g) { _entryToSet[m.Entry] = vid; remap[m.Entry] = (vid, vname); }
            }
        }
        if (remap.Count > 0)
            for (int i = 0; i < list.Count; i++)
                if (remap.TryGetValue(list[i].Entry, out var r))
                    list[i] = list[i] with { SetId = r.SetId, SetName = r.Name };

        _browse = list.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();

        // 4) Featured vs "other": the later client's ItemSet.dbc carries EVERY set the game ever
        //    shipped — this expansion's tiers sit in the same 300–500 rows as vanilla's T1/T2, the
        //    levelling greens and every crafted three-piece. Rank each set by its best browsable
        //    armor member so the browse can lead with the tier/arena sets and file the rest away.
        var byEntryForRank = _browse.ToDictionary(e => e.Entry);
        int featured = 0;
        foreach (var set in _sets.Values)
        {
            var members = set.MemberEntries.Where(byEntryForRank.ContainsKey).Select(m => byEntryForRank[m]).ToList();
            if (members.Count == 0) continue;
            set.MaxQuality = members.Max(m => m.Quality);
            set.MaxItemLevel = members.Max(m => m.ItemLevel);
            set.Featured = set.MaxQuality >= 4 && set.MaxItemLevel >= FeaturedMinItemLevel;
            if (set.Featured) featured++;
        }

        _logger.LogInformation("{Label} armor: indexed {Count} armor pieces, {Sets} sets with armor members ({Featured} featured at ilvl {Ilvl}+)",
            Label, _browse.Count, _sets.Values.Count(s => s.MemberEntries.Count > 0), featured, FeaturedMinItemLevel);
    }
}
/// <summary>One browsable later-client armor item (TBC or WotLK lane).</summary>
public sealed record LegacyArmorEntry
{
    public required uint Entry { get; init; }
    public required string Name { get; init; }
    public required uint DisplayId { get; init; }
    public required int Quality { get; init; }
    public required string FamilyKey { get; init; }
    public required string FamilyLabel { get; init; }
    public required ArmorRenderKind RenderKind { get; init; }
    public required ArmorMaterial Material { get; init; }
    public required int InventoryType { get; init; }
    public required int ItemLevel { get; init; }
    public required int RequiredLevel { get; init; }
    public uint SetId { get; init; }
    public string? SetName { get; init; }
}

/// <summary>A TBC item set as read from the client's ItemSet.dbc.</summary>
public sealed record LegacySetInfo
{
    public required uint SetId { get; init; }
    public required string Name { get; init; }
    /// <summary>Every item id the DBC lists (armor + weapons + junk).</summary>
    public required IReadOnlyList<uint> AllItemEntries { get; init; }
    /// <summary>The browsable ARMOR members (what the Armor Forge imports).</summary>
    public required IReadOnlyList<uint> MemberEntries { get; init; }

    /// <summary>Best quality / item level over the browsable armor members, and whether that makes
    /// this a current-expansion tier-or-arena set. Set once at index time; see
    /// <see cref="LegacyArmorCatalog.FeaturedMinItemLevel"/>.</summary>
    public int MaxQuality { get; set; }
    public int MaxItemLevel { get; set; }
    public bool Featured { get; set; }
}

/// <summary>A decoded TBC ItemDisplayInfo row.</summary>
public sealed record LegacyDisplayRow
{
    public required uint DisplayId { get; init; }
    public required string ModelName1 { get; init; }
    public required string ModelName2 { get; init; }
    public required string TextureName1 { get; init; }
    public required string TextureName2 { get; init; }
    public required string IconStem { get; init; }
    public required int[] GeosetGroup { get; init; }
    public required uint GroupSoundIndex { get; init; }
    public required uint HelmetVis0 { get; init; }
    public required uint HelmetVis1 { get; init; }
    /// <summary>m_texture[0..7] partial names (bare; client appends subdir + gender suffix).</summary>
    public required string[] ComponentPartials { get; init; }
}
