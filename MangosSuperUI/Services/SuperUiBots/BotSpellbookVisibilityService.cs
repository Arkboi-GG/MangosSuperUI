using Dapper;
using MangosSuperUI.BotLogic.Tracking;
using MangosSuperUI.Models;

namespace MangosSuperUI.Services;

/// <summary>
/// Builds the Bots/Spellbook read model. Like the talent view, it reports what is
/// actually true rather than what a profile intends: the rows come from
/// <c>character_spell</c>, and every label, rank, icon, and grouping comes from the
/// installed build-5875 client tables — never from a name heuristic.
///
/// Authoritative sources:
///   character_spell        — the learned set (disabled rows are reported separately)
///   Spell.dbc              — display name, "Rank N" subtext, icon, level, PASSIVE bit
///   SkillLineAbility.dbc   — spell → skill line, and the forward_spellid rank chain
///   SkillLine.dbc          — the spellbook tab a skill line belongs to
///   SkillLineCategory.dbc  — the client's own tab ordering (Class, Professions, …)
///   Talent.dbc             — provenance, borrowed from BotTalentVisibilityService
///
/// The rank chain matters more than it looks: a bot that still knows Heroic Strike
/// ranks 1-8 is normal, but a rotation instruction naming rank 3 will underperform
/// forever and never fail a validation. Marking the highest known rank of each chain
/// is what makes the spellbook usable as a rotation-authoring source.
///
/// Nothing here mutates. It is a per-bot lazy read, exactly like the talent endpoint:
/// opening the cockpit must never fan a fleet-sized query out at the character DB.
/// </summary>
public sealed class BotSpellbookVisibilityService
{
    private static readonly IReadOnlyDictionary<int, string> ClassNames = new Dictionary<int, string>
    {
        [1] = "Warrior", [2] = "Paladin", [3] = "Hunter", [4] = "Rogue",
        [5] = "Priest", [7] = "Shaman", [8] = "Mage", [9] = "Warlock", [11] = "Druid"
    };

    /// <summary>SkillLineCategory.dbc id for "Class Skills" — the tabs a rotation draws from.</summary>
    private const uint ClassSkillCategoryId = 7;

    private readonly ConnectionFactory _db;
    private readonly DbcService _dbc;
    private readonly BotTalentVisibilityService _talents;
    private readonly RotationService _rotations;
    private readonly ILogger<BotSpellbookVisibilityService> _logger;
    private readonly Lazy<SkillCatalog> _catalog;

    public BotSpellbookVisibilityService(
        ConnectionFactory db,
        DbcService dbc,
        BotTalentVisibilityService talents,
        RotationService rotations,
        ILogger<BotSpellbookVisibilityService> logger)
    {
        _db = db;
        _dbc = dbc;
        _talents = talents;
        _rotations = rotations;
        _logger = logger;
        _catalog = new Lazy<SkillCatalog>(LoadCatalog, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public async Task<BotSpellbookVisibility> GetAsync(int guid, CancellationToken cancellationToken = default)
    {
        if (guid <= 0)
        {
            CircuitTrace.Hit(0, "spellbook: rejected, invalid guid");
            return Error(guid, "not_found", "A positive character guid is required.");
        }

        BotSpellbookDbRow? bot;
        IReadOnlyList<KnownSpellRow> known;
        try
        {
            using var conn = _db.Characters();
            await conn.OpenAsync(cancellationToken);

            bot = await conn.QueryFirstOrDefaultAsync<BotSpellbookDbRow>(new CommandDefinition(@"
                SELECT guid AS Guid, name AS Name, `class` AS ClassId, race AS RaceId, level AS Level
                FROM characters
                WHERE guid = @Guid",
                new { Guid = guid }, cancellationToken: cancellationToken));

            if (bot == null)
            {
                CircuitTrace.Hit(guid, "spellbook: character not found");
                return Error(guid, "not_found", $"Character {guid} was not found.");
            }

            known = (await conn.QueryAsync<KnownSpellRow>(new CommandDefinition(@"
                SELECT spell AS SpellId, active AS Active, disabled AS Disabled
                FROM character_spell
                WHERE guid = @Guid",
                new { Guid = guid }, cancellationToken: cancellationToken))).ToArray();
        }
        catch (Exception ex)
        {
            CircuitTrace.Hit(guid, "spellbook: character_spell read failed");
            _logger.LogWarning(ex, "Spellbook: failed to read character_spell for guid {Guid}", guid);
            return Error(guid, "database_unavailable", "Character spell data is temporarily unavailable.");
        }

        SkillCatalog catalog;
        try
        {
            catalog = _catalog.Value;
        }
        catch (Exception ex)
        {
            CircuitTrace.Hit(guid, "spellbook: skill catalog unavailable");
            _logger.LogError(ex, "Spellbook: skill-line catalog failed to load");
            return Error(guid, "catalog_unavailable",
                "The build-5875 skill-line catalog is unavailable or failed validation.", bot);
        }

        // Learned set. `disabled` rows are spells the core has suppressed (a
        // superseded rank kept on the row, a removed talent); they are counted but
        // never presented as castable, because a rotation naming one silently
        // resolves to nothing on the C++ side.
        var learned = new Dictionary<uint, KnownSpellRow>();
        int disabledCount = 0;
        foreach (KnownSpellRow row in known)
        {
            if (row.Disabled != 0)
            {
                CircuitTrace.Hit(guid, "spellbook: disabled spell row suppressed", row.SpellId);
                disabledCount++;
                continue;
            }
            learned[row.SpellId] = row;
        }

        IReadOnlyDictionary<uint, BotTalentSpellOrigin> talentOrigins = _talents.GetTalentSpellOrigins(bot.ClassId);
        RotationCrossReference rotation = BuildRotationCrossReference(bot, learned, catalog);

        var entries = new List<BotSpellbookEntryView>(learned.Count);
        int unresolved = 0;

        foreach (KnownSpellRow row in learned.Values.OrderBy(r => r.SpellId))
        {
            _dbc.AllSpellEntries.TryGetValue(row.SpellId, out SpellDbcEntry? spell);
            if (spell == null)
            {
                CircuitTrace.Hit(guid, "spellbook: learned spell unresolved in Spell.dbc", row.SpellId);
                unresolved++;
            }

            // A chain's top is the last link still present in THIS bot's learned set.
            // Walking forward_spellid (rather than comparing "Rank N" strings) is the
            // only method that survives the ranks that share a subtext or have none.
            uint forward = catalog.Forward.GetValueOrDefault(row.SpellId);
            bool highest = forward == 0 || !learned.ContainsKey(forward);

            (uint rootId, int chainLength, int rankIndex) = catalog.LocateInChain(row.SpellId, learned);

            uint skillId = catalog.SkillOf.GetValueOrDefault(row.SpellId);
            talentOrigins.TryGetValue(row.SpellId, out BotTalentSpellOrigin? origin);
            rotation.BySpellId.TryGetValue(row.SpellId, out int rotationPriority);

            entries.Add(new BotSpellbookEntryView
            {
                SpellId = row.SpellId,
                Name = string.IsNullOrWhiteSpace(spell?.Name) ? $"Spell #{row.SpellId}" : spell!.Name,
                Rank = spell?.NameSubtext ?? "",
                IconUrl = spell == null
                    ? "/Icon/Get?name=inv_misc_questionmark"
                    : _dbc.GetSpellIconPath(spell.SpellIconId),
                Level = (int)(spell?.SpellLevel ?? 0),
                SkillLineId = skillId,
                Passive = spell?.Passive ?? false,
                Hidden = spell?.Hidden ?? false,
                Resolved = spell != null,
                ActiveOnBar = row.Active != 0,
                HighestKnownRank = highest,
                SupersededBySpellId = highest ? 0u : forward,
                ChainRootSpellId = rootId,
                ChainLength = chainLength,
                RankIndex = rankIndex,
                FromTalent = origin != null,
                TalentId = origin?.TalentId ?? 0,
                TalentTree = origin?.TreeName ?? "",
                InRotation = rotationPriority > 0,
                RotationPriority = rotationPriority
            });
        }

        var groups = entries
            .GroupBy(e => e.SkillLineId)
            .Select(g =>
            {
                catalog.Lines.TryGetValue(g.Key, out SkillLineDefinition? line);
                uint categoryId = line?.CategoryId ?? 0;
                catalog.Categories.TryGetValue(categoryId, out SkillCategoryDefinition? category);

                return new BotSpellbookGroupView
                {
                    SkillLineId = g.Key,
                    // An unmapped spell is real and learned; it just has no
                    // SkillLineAbility row (item-granted, event, or core-taught).
                    Name = line?.Name ?? (g.Key == 0 ? "Unclassified" : $"Skill line {g.Key}"),
                    CategoryId = categoryId,
                    CategoryName = category?.Name ?? (g.Key == 0 ? "Other" : "Unknown"),
                    // Unclassified sorts last; every real category keeps the client's own order.
                    SortIndex = line == null ? 99 : (int)(category?.SortIndex ?? 98),
                    IsClassSkill = categoryId == ClassSkillCategoryId,
                    Count = g.Count(),
                    HighestRankCount = g.Count(e => e.HighestKnownRank),
                    Spells = g
                        .OrderByDescending(e => e.HighestKnownRank)
                        .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(e => e.RankIndex)
                        .ToArray()
                };
            })
            .OrderBy(g => g.SortIndex)
            .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new BotSpellbookVisibility
        {
            Guid = bot.Guid,
            Name = bot.Name,
            ClassId = bot.ClassId,
            ClassName = ClassNames.GetValueOrDefault(bot.ClassId, $"Class {bot.ClassId}"),
            Level = bot.Level,
            Summary = new BotSpellbookSummary
            {
                Known = entries.Count,
                Castable = entries.Count(e => !e.Passive),
                Passive = entries.Count(e => e.Passive),
                HighestRank = entries.Count(e => e.HighestKnownRank && !e.Passive),
                Superseded = entries.Count(e => !e.HighestKnownRank),
                FromTalents = entries.Count(e => e.FromTalent),
                ClassSkills = groups.Where(g => g.IsClassSkill).Sum(g => g.Count),
                Disabled = disabledCount,
                Unresolved = unresolved
            },
            Rotation = rotation.View,
            Groups = groups,
            AsOfUtc = DateTime.UtcNow
        };
    }

    // ------------------------------------------------------------- rotation link

    /// <summary>
    /// Cross-reference the bot's PERSISTED custom-rotation assignment against the
    /// learned set. This is deliberately the assignment file and not the live
    /// runtime slate: the runtime already reports loaded/skipped counts, but only
    /// the profile itself knows WHICH spell was skipped, and that is the fact an
    /// operator needs in order to fix the profile.
    /// </summary>
    private RotationCrossReference BuildRotationCrossReference(
        BotSpellbookDbRow bot,
        IReadOnlyDictionary<uint, KnownSpellRow> learned,
        SkillCatalog catalog)
    {
        var bySpellId = new Dictionary<uint, int>();
        if (!_rotations.TryGetAssignment(bot.Name, out string profileName) || string.IsNullOrWhiteSpace(profileName))
        {
            CircuitTrace.Hit(bot.Guid, "spellbook: no rotation assignment for bot");
            return new RotationCrossReference(bySpellId, new BotSpellbookRotationView { Assigned = false });
        }

        RotationService.RotationProfile? profile;
        try
        {
            profile = _rotations.FindProfile(profileName);
        }
        catch (Exception ex)
        {
            CircuitTrace.HitNote(bot.Guid, "spellbook: rotation profile read failed", profileName);
            _logger.LogWarning(ex, "Spellbook: rotation profile '{Profile}' could not be read", profileName);
            profile = null;
        }

        if (profile == null)
        {
            CircuitTrace.HitNote(bot.Guid, "spellbook: assigned rotation profile not found", profileName);
            return new RotationCrossReference(bySpellId, new BotSpellbookRotationView
            {
                Assigned = true,
                ProfileName = profileName,
                ProfileFound = false
            });
        }

        var missing = new List<BotSpellbookRotationGapView>();
        var staleRank = new List<BotSpellbookRotationGapView>();

        foreach (RotationService.RotationInstruction instruction in profile.Instructions)
        {
            uint spellId = instruction.SpellId;
            if (spellId == 0)
            {
                CircuitTrace.Hit(bot.Guid, "spellbook: rotation instruction without spell id");
                continue;
            }

            // First writer wins so the displayed priority is the strongest one the
            // slate assigns to that spell, matching the C++ first-match-wins walk.
            if (!bySpellId.ContainsKey(spellId) || instruction.Priority < bySpellId[spellId])
            {
                CircuitTrace.Hit(bot.Guid, "spellbook: rotation priority recorded", instruction.Priority);
                bySpellId[spellId] = instruction.Priority;
            }

            _dbc.AllSpellEntries.TryGetValue(spellId, out SpellDbcEntry? spell);
            string label = string.IsNullOrWhiteSpace(spell?.Name) ? $"Spell #{spellId}" : spell!.Name;
            string rank = spell?.NameSubtext ?? "";

            if (!learned.ContainsKey(spellId))
            {
                CircuitTrace.Hit(bot.Guid, "spellbook: rotation names unknown spell", spellId);
                missing.Add(new BotSpellbookRotationGapView
                {
                    SpellId = spellId,
                    Name = label,
                    Rank = rank,
                    Priority = instruction.Priority,
                    Note = instruction.Note ?? ""
                });
                continue;
            }

            uint forward = catalog.Forward.GetValueOrDefault(spellId);
            if (forward != 0 && learned.ContainsKey(forward))
            {
                CircuitTrace.Hit(bot.Guid, "spellbook: rotation names outgrown rank", spellId);
                // Castable, so the core loads it and reports zero skipped — but the
                // bot knows a strictly better rank of the same spell. This is the
                // failure mode a loaded/skipped count can never surface.
                _dbc.AllSpellEntries.TryGetValue(forward, out SpellDbcEntry? better);
                staleRank.Add(new BotSpellbookRotationGapView
                {
                    SpellId = spellId,
                    Name = label,
                    Rank = rank,
                    Priority = instruction.Priority,
                    BetterSpellId = forward,
                    BetterRank = better?.NameSubtext ?? "",
                    Note = instruction.Note ?? ""
                });
            }
        }

        return new RotationCrossReference(bySpellId, new BotSpellbookRotationView
        {
            Assigned = true,
            ProfileName = profile.Name,
            ProfileFound = true,
            Description = profile.Description ?? "",
            InstructionCount = profile.Instructions.Count,
            CoveredCount = bySpellId.Keys.Count(learned.ContainsKey),
            MissingSpells = missing,
            StaleRankSpells = staleRank
        });
    }

    // ------------------------------------------------------------------ catalog

    /// <summary>
    /// SkillLineAbility.dbc (15 fields / 60 bytes), SkillLine.dbc (22 / 88), and
    /// SkillLineCategory.dbc (11 / 44) — all verified against the installed
    /// build-5875 files. The layouts are asserted rather than assumed: a client
    /// swap that changes a record size must fail loudly here, not silently
    /// scatter every spell into the wrong spellbook tab.
    /// </summary>
    private SkillCatalog LoadCatalog()
    {
        WowDbcFile abilityDbc = ParseRequired("SkillLineAbility.dbc", 15, 60);
        WowDbcFile lineDbc = ParseRequired("SkillLine.dbc", 22, 88);
        WowDbcFile categoryDbc = ParseRequired("SkillLineCategory.dbc", 11, 44);

        var categories = new Dictionary<uint, SkillCategoryDefinition>();
        for (int row = 0; row < categoryDbc.RecordCount; row++)
        {
            uint id = categoryDbc.GetUInt(row, 0);
            categories[id] = new SkillCategoryDefinition(
                id,
                categoryDbc.GetStringIfStart(row, 1) ?? "",
                categoryDbc.GetInt(row, 10));
        }

        var lines = new Dictionary<uint, SkillLineDefinition>();
        for (int row = 0; row < lineDbc.RecordCount; row++)
        {
            uint id = lineDbc.GetUInt(row, 0);
            lines[id] = new SkillLineDefinition(
                id,
                lineDbc.GetUInt(row, 1),
                lineDbc.GetStringIfStart(row, 3) ?? $"Skill line {id}");
        }

        // A spell can appear on several skill lines (weapon skills across classes,
        // profession recipes). Prefer the first row, but let a Class Skills row win
        // outright — that is the tab the client itself shows the ability under.
        var skillOf = new Dictionary<uint, uint>();
        var forward = new Dictionary<uint, uint>();
        var backward = new Dictionary<uint, uint>();
        for (int row = 0; row < abilityDbc.RecordCount; row++)
        {
            uint skillId = abilityDbc.GetUInt(row, 1);
            uint spellId = abilityDbc.GetUInt(row, 2);
            if (spellId == 0)
                continue;   // cb:fold load-time catalog assembly, no per-bot routing

            bool isClassSkill = lines.TryGetValue(skillId, out SkillLineDefinition? line)
                && line.CategoryId == ClassSkillCategoryId;
            if (!skillOf.ContainsKey(spellId) || (isClassSkill && !IsClassSkill(skillOf[spellId], lines)))
                skillOf[spellId] = skillId;   // cb:fold load-time catalog assembly, no per-bot routing

            uint forwardSpellId = abilityDbc.GetUInt(row, 8);
            if (forwardSpellId != 0 && forwardSpellId != spellId && !forward.ContainsKey(spellId))
            {   // cb:fold load-time catalog assembly, no per-bot routing
                forward[spellId] = forwardSpellId;
                backward.TryAdd(forwardSpellId, spellId);
            }
        }

        _logger.LogInformation(
            "Spellbook: skill catalog ready — {Abilities} ability rows, {Lines} skill lines, {Chains} rank links",
            skillOf.Count, lines.Count, forward.Count);

        return new SkillCatalog(categories, lines, skillOf, forward, backward);
    }

    private static bool IsClassSkill(uint skillId, IReadOnlyDictionary<uint, SkillLineDefinition> lines)
        => lines.TryGetValue(skillId, out SkillLineDefinition? line) && line.CategoryId == ClassSkillCategoryId;

    private WowDbcFile ParseRequired(string fileName, int fieldCount, int recordSize)
    {
        string path = Path.Combine(_dbc.DbcPath, fileName);
        WowDbcFile parsed = WowDbcFile.Parse(File.ReadAllBytes(path))
            ?? throw new InvalidDataException($"Could not parse {path}.");
        if (parsed.FieldCount != fieldCount || parsed.RecordSize != recordSize)
        {
            CircuitTrace.Hit(0, "spellbook: dbc layout mismatch, failing catalog load");
            throw new InvalidDataException(
                $"{fileName} layout is {parsed.FieldCount} fields/{parsed.RecordSize} bytes; expected {fieldCount}/{recordSize}.");
        }
        return parsed;
    }

    private static BotSpellbookVisibility Error(
        int guid,
        string code,
        string message,
        BotSpellbookDbRow? bot = null)
        => new()
        {
            Guid = guid,
            Name = bot?.Name ?? "",
            ClassId = bot?.ClassId ?? 0,
            ClassName = bot == null ? "" : ClassNames.GetValueOrDefault(bot.ClassId, $"Class {bot.ClassId}"),
            Level = bot?.Level ?? 0,
            ErrorCode = code,
            Error = message,
            AsOfUtc = DateTime.UtcNow
        };

    private sealed class BotSpellbookDbRow
    {
        public int Guid { get; set; }
        public string Name { get; set; } = "";
        public int ClassId { get; set; }
        public int RaceId { get; set; }
        public int Level { get; set; }
    }

    private sealed class KnownSpellRow
    {
        public uint SpellId { get; set; }
        public int Active { get; set; }
        public int Disabled { get; set; }
    }

    private sealed record SkillCategoryDefinition(uint Id, string Name, int SortIndex);

    private sealed record SkillLineDefinition(uint Id, uint CategoryId, string Name);

    private sealed record RotationCrossReference(
        IReadOnlyDictionary<uint, int> BySpellId,
        BotSpellbookRotationView View);

    private sealed record SkillCatalog(
        IReadOnlyDictionary<uint, SkillCategoryDefinition> Categories,
        IReadOnlyDictionary<uint, SkillLineDefinition> Lines,
        IReadOnlyDictionary<uint, uint> SkillOf,
        IReadOnlyDictionary<uint, uint> Forward,
        IReadOnlyDictionary<uint, uint> Backward)
    {
        /// <summary>
        /// Walk one spell's rank chain, counting only the links this bot actually
        /// knows. Both directions are bounded by the chain length so a malformed
        /// self-referential forward_spellid can never spin the request.
        /// </summary>
        public (uint RootSpellId, int ChainLength, int RankIndex) LocateInChain(
            uint spellId,
            IReadOnlyDictionary<uint, KnownSpellRow> learned)
        {
            const int MaxChain = 32;

            uint root = spellId;
            int before = 0;
            var seen = new HashSet<uint> { spellId };
            for (int step = 0; step < MaxChain; step++)
            {
                if (!Backward.TryGetValue(root, out uint previous) || !seen.Add(previous))
                    break;   // cb:fold pure chain-walk helper without guid, result carried in entry view
                root = previous;
                if (learned.ContainsKey(previous))
                    before++;   // cb:fold pure chain-walk helper without guid, result carried in entry view
            }

            int after = 0;
            uint cursor = spellId;
            for (int step = 0; step < MaxChain; step++)
            {
                if (!Forward.TryGetValue(cursor, out uint next) || !seen.Add(next))
                    break;   // cb:fold pure chain-walk helper without guid, result carried in entry view
                cursor = next;
                if (learned.ContainsKey(next))
                    after++;   // cb:fold pure chain-walk helper without guid, result carried in entry view
            }

            return (root, before + 1 + after, before + 1);
        }
    }
}

// ---------------------------------------------------------------- view models

public sealed class BotSpellbookVisibility
{
    public int Guid { get; set; }
    public string Name { get; set; } = "";
    public int ClassId { get; set; }
    public string ClassName { get; set; } = "";
    public int Level { get; set; }
    public BotSpellbookSummary Summary { get; set; } = new();
    public BotSpellbookRotationView Rotation { get; set; } = new();
    public IReadOnlyList<BotSpellbookGroupView> Groups { get; set; } = Array.Empty<BotSpellbookGroupView>();
    public DateTime AsOfUtc { get; set; }
    public string? ErrorCode { get; set; }
    public string? Error { get; set; }
}

public sealed class BotSpellbookSummary
{
    /// <summary>Enabled rows in character_spell.</summary>
    public int Known { get; set; }
    public int Castable { get; set; }
    public int Passive { get; set; }
    /// <summary>Castable spells that are the top known rank of their chain — the rotation-eligible set.</summary>
    public int HighestRank { get; set; }
    public int Superseded { get; set; }
    public int FromTalents { get; set; }
    public int ClassSkills { get; set; }
    /// <summary>Rows the core has disabled; never presented as castable.</summary>
    public int Disabled { get; set; }
    /// <summary>Learned ids with no Spell.dbc row — a custom or missing spell.</summary>
    public int Unresolved { get; set; }
}

public sealed class BotSpellbookGroupView
{
    public uint SkillLineId { get; set; }
    public string Name { get; set; } = "";
    public uint CategoryId { get; set; }
    public string CategoryName { get; set; } = "";
    public int SortIndex { get; set; }
    public bool IsClassSkill { get; set; }
    public int Count { get; set; }
    public int HighestRankCount { get; set; }
    public IReadOnlyList<BotSpellbookEntryView> Spells { get; set; } = Array.Empty<BotSpellbookEntryView>();
}

public sealed class BotSpellbookEntryView
{
    public uint SpellId { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Spell.dbc Subtext — "Rank 4", "Passive", or empty.</summary>
    public string Rank { get; set; } = "";
    public string IconUrl { get; set; } = "";
    public int Level { get; set; }
    public uint SkillLineId { get; set; }
    public bool Passive { get; set; }
    public bool Hidden { get; set; }
    /// <summary>False when the id has no Spell.dbc row at all.</summary>
    public bool Resolved { get; set; }
    /// <summary>character_spell.active — the client action-bar flag, not castability.</summary>
    public bool ActiveOnBar { get; set; }
    public bool HighestKnownRank { get; set; }
    public uint SupersededBySpellId { get; set; }
    public uint ChainRootSpellId { get; set; }
    public int ChainLength { get; set; }
    public int RankIndex { get; set; }
    public bool FromTalent { get; set; }
    public uint TalentId { get; set; }
    public string TalentTree { get; set; } = "";
    public bool InRotation { get; set; }
    public int RotationPriority { get; set; }
}

public sealed class BotSpellbookRotationView
{
    public bool Assigned { get; set; }
    public string ProfileName { get; set; } = "";
    public bool ProfileFound { get; set; }
    public string Description { get; set; } = "";
    public int InstructionCount { get; set; }
    /// <summary>Distinct rotation spell ids the bot actually knows.</summary>
    public int CoveredCount { get; set; }
    /// <summary>Instructions naming a spell this bot does not know — the core skips them.</summary>
    public IReadOnlyList<BotSpellbookRotationGapView> MissingSpells { get; set; }
        = Array.Empty<BotSpellbookRotationGapView>();
    /// <summary>Instructions that are castable but name a rank the bot has outgrown.</summary>
    public IReadOnlyList<BotSpellbookRotationGapView> StaleRankSpells { get; set; }
        = Array.Empty<BotSpellbookRotationGapView>();
}

public sealed class BotSpellbookRotationGapView
{
    public uint SpellId { get; set; }
    public string Name { get; set; } = "";
    public string Rank { get; set; } = "";
    public int Priority { get; set; }
    public uint BetterSpellId { get; set; }
    public string BetterRank { get; set; } = "";
    public string Note { get; set; } = "";
}
