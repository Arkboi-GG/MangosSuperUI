using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using MangosSuperUI.Models;

namespace MangosSuperUI.Services;

/// <summary>
/// Builds the Bots/Talents read model from three authoritative sources:
/// playerbot.spec_tab/active_role (identity), character_spell (actual learned
/// state), and the installed build-5875 Talent/TalentTab DBCs (rank identity and
/// layout). General class spells and talent-name heuristics are never consulted.
/// </summary>
public sealed class BotTalentVisibilityService
{
    private const string ManifestResource = "MangosSuperUI.BotLogic.Talents.talent_profiles.json";

    private static readonly IReadOnlyDictionary<int, string> ClassNames = new Dictionary<int, string>
    {
        [1] = "warrior", [2] = "paladin", [3] = "hunter", [4] = "rogue",
        [5] = "priest", [7] = "shaman", [8] = "mage", [9] = "warlock", [11] = "druid"
    };

    private static readonly IReadOnlyDictionary<int, string> RoleNames = new Dictionary<int, string>
    {
        [0] = "Unassigned",
        [1] = "Melee DPS",
        [2] = "Ranged DPS",
        [3] = "Tank",
        [4] = "Healer"
    };

    private readonly ConnectionFactory _db;
    private readonly DbcService _dbc;
    private readonly ILogger<BotTalentVisibilityService> _logger;
    private readonly Lazy<TalentCatalog> _catalog;

    public BotTalentVisibilityService(
        ConnectionFactory db,
        DbcService dbc,
        ILogger<BotTalentVisibilityService> logger)
    {
        _db = db;
        _dbc = dbc;
        _logger = logger;
        _catalog = new Lazy<TalentCatalog>(LoadCatalog, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>
    /// Return the three validated, class-local profile choices used by both the
    /// read-only talent view and the combat-loadout mutation service. Keeping the
    /// mapping here prevents the web command path from inventing a second
    /// spec-tab/profile/role catalog that can drift from the embedded manifest.
    /// </summary>
    public IReadOnlyList<BotTalentProfileOption> GetProfileOptions(int classId)
    {
        if (!ClassNames.TryGetValue(classId, out string? classKey))
            return Array.Empty<BotTalentProfileOption>();

        TalentCatalog catalog = _catalog.Value;
        if (!catalog.Manifest.TreeOrder.TryGetValue(classKey, out string[]? specKeys))
            return Array.Empty<BotTalentProfileOption>();

        var result = new List<BotTalentProfileOption>(specKeys.Length);
        for (int specTab = 0; specTab < specKeys.Length; specTab++)
        {
            string spec = specKeys[specTab];
            TalentProfile? profile = catalog.Manifest.Profiles.SingleOrDefault(p =>
                p.ClassId == classId && string.Equals(p.Spec, spec, StringComparison.OrdinalIgnoreCase));
            if (profile == null)
                continue;

            int[] roles = AllowedRoles(profile);
            result.Add(new BotTalentProfileOption
            {
                Id = profile.Id,
                ClassId = classId,
                SpecTab = specTab,
                Spec = profile.Spec,
                Name = Humanize(profile.Spec),
                RolePolicy = profile.RolePolicy,
                GearPolicy = profile.GearPolicy,
                TreePoints = profile.TreePoints,
                AllowedRoles = roles,
                DefaultRole = roles.Length == 1 ? roles[0] : 0
            });
        }

        return result;
    }

    public BotTalentProfileOption? FindProfileOption(int classId, int specTab)
        => GetProfileOptions(classId).SingleOrDefault(p => p.SpecTab == specTab);

    public static string RoleName(int role)
        => RoleNames.TryGetValue(role, out string? name) ? name : "Invalid";

    /// <summary>
    /// Map every talent RANK spell of one class to the talent that grants it.
    /// The spellbook read model uses this so a learned spell can say "this came
    /// from a talent" without parsing Talent.dbc a second time — a second parse
    /// is exactly how two views of the same build start disagreeing.
    ///
    /// A catalog failure is not propagated: the spellbook is a plain
    /// character_spell projection and stays useful when the profile manifest or
    /// Talent.dbc is unusable. Callers get an empty map and simply show no
    /// talent provenance.
    /// </summary>
    public IReadOnlyDictionary<uint, BotTalentSpellOrigin> GetTalentSpellOrigins(int classId)
    {
        TalentCatalog catalog;
        try
        {
            catalog = _catalog.Value;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Spellbook: talent catalog unavailable; talent provenance is omitted");
            return new Dictionary<uint, BotTalentSpellOrigin>();
        }

        ClassNames.TryGetValue(classId, out string? classKey);

        var origins = new Dictionary<uint, BotTalentSpellOrigin>();
        foreach (var (rankSpellId, position) in catalog.ByRankSpell)
        {
            if (!catalog.Tabs.TryGetValue(position.Talent.TabId, out var tab) || !tab.SupportsClass(classId))
                continue;

            origins[rankSpellId] = new BotTalentSpellOrigin
            {
                TalentId = position.Talent.Id,
                TreeId = tab.Id,
                TreeName = string.IsNullOrWhiteSpace(tab.Name) && classKey != null
                    ? ResolveTreeName(catalog.Manifest, classKey, tab.Order)
                    : tab.Name,
                Rank = position.Rank,
                MaxRank = position.Talent.MaxRank
            };
        }

        return origins;
    }

    public async Task<BotTalentVisibility> GetAsync(int guid, CancellationToken cancellationToken = default)
    {
        if (guid <= 0)
            return Error(guid, "not_found", "A positive character guid is required.");

        BotTalentDbRow? bot;
        IReadOnlyCollection<uint> learnedSpells;
        try
        {
            using var conn = _db.Characters();
            await conn.OpenAsync(cancellationToken);

            bot = await conn.QueryFirstOrDefaultAsync<BotTalentDbRow>(new CommandDefinition(@"
                SELECT c.guid AS Guid, c.name AS Name, c.`class` AS ClassId, c.level AS Level,
                       COALESCE(pb.spec_tab, 255) AS SpecTab,
                       COALESCE(pb.active_role, 0) AS ActiveRole
                FROM characters c
                LEFT JOIN playerbot pb ON pb.char_guid = c.guid
                WHERE c.guid = @Guid",
                new { Guid = guid }, cancellationToken: cancellationToken));

            if (bot == null)
                return Error(guid, "not_found", $"Character {guid} was not found.");

            learnedSpells = (await conn.QueryAsync<uint>(new CommandDefinition(@"
                SELECT spell
                FROM character_spell
                WHERE guid = @Guid AND disabled = 0",
                new { Guid = guid }, cancellationToken: cancellationToken))).ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Talents: failed to read character/playerbot state for guid {Guid}", guid);
            bool migrationMissing = ex.Message.Contains("Unknown column", StringComparison.OrdinalIgnoreCase)
                && (ex.Message.Contains("spec_tab", StringComparison.OrdinalIgnoreCase)
                    || ex.Message.Contains("active_role", StringComparison.OrdinalIgnoreCase));
            return Error(guid,
                migrationMissing ? "schema_not_ready" : "database_unavailable",
                migrationMissing
                    ? "The core playerbot specialization migration has not been applied yet."
                    : "Character talent data is temporarily unavailable.");
        }

        TalentCatalog catalog;
        try
        {
            catalog = _catalog.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Talents: profile/DBC catalog failed to load");
            return Error(guid, "catalog_unavailable",
                "The build-5875 talent catalog is unavailable or failed validation.", bot);
        }

        if (!ClassNames.TryGetValue(bot.ClassId, out var classKey))
            return Error(guid, "unsupported_class", $"Class {bot.ClassId} has no Vanilla talent profile.", bot);

        var classTabs = catalog.Tabs.Values
            .Where(t => t.SupportsClass(bot.ClassId))
            .OrderBy(t => t.Order)
            .ToArray();

        var actualRanks = new Dictionary<uint, int>();
        foreach (uint spellId in learnedSpells)
        {
            if (!catalog.ByRankSpell.TryGetValue(spellId, out var pos))
                continue;
            if (!actualRanks.TryGetValue(pos.Talent.Id, out int current) || pos.Rank > current)
                actualRanks[pos.Talent.Id] = pos.Rank;
        }

        TalentProfile? profile = null;
        string? profileProblem = null;
        if (bot.SpecTab is >= 0 and <= 2)
        {
            if (!catalog.Manifest.TreeOrder.TryGetValue(classKey, out var specKeys) || bot.SpecTab >= specKeys.Length)
            {
                profileProblem = $"Profile slot {bot.SpecTab} is not defined for {Humanize(classKey)}.";
            }
            else
            {
                string specKey = specKeys[bot.SpecTab];
                profile = catalog.Manifest.Profiles.SingleOrDefault(p =>
                    p.ClassId == bot.ClassId && string.Equals(p.Spec, specKey, StringComparison.OrdinalIgnoreCase));
                if (profile == null)
                    profileProblem = $"Manifest profile {classKey}/{specKey} is missing.";
            }
        }
        else if (bot.SpecTab != 255)
        {
            profileProblem = $"Persisted spec_tab {bot.SpecTab} is invalid; expected 0-2 or 255.";
        }

        var fullPlan = profile == null ? Array.Empty<uint>() : Expand(profile);
        int earnedPoints = Math.Clamp(bot.Level - 9, 0, 51);
        var levelPlan = fullPlan.Take(earnedPoints).ToArray();
        var fullRanks = CountRanks(fullPlan);
        var levelRanks = CountRanks(levelPlan);

        int spentPoints = actualRanks.Values.Sum();
        int foreignPoints = actualRanks
            .Where(kv => !catalog.Talents.TryGetValue(kv.Key, out var t)
                         || !catalog.Tabs.TryGetValue(t.TabId, out var tab)
                         || !tab.SupportsClass(bot.ClassId))
            .Sum(kv => kv.Value);

        var unexpected = profile == null
            ? Array.Empty<uint>()
            : actualRanks
                .Where(kv => !fullRanks.TryGetValue(kv.Key, out int planned) || kv.Value > planned)
                .Select(kv => kv.Key)
                .OrderBy(id => id)
                .ToArray();

        var missingAtLevel = levelRanks
            .Where(kv => !actualRanks.TryGetValue(kv.Key, out int actual) || actual < kv.Value)
            .Select(kv => kv.Key)
            .OrderBy(id => id)
            .ToArray();

        var warnings = new List<string>();
        bool roleAssigned = RoleNames.ContainsKey(bot.ActiveRole) && bot.ActiveRole != 0;
        bool roleAllowed = profile == null || (roleAssigned && IsRoleAllowed(profile, bot.ActiveRole));
        if (!roleAssigned)
            warnings.Add("The active combat role is unassigned or invalid.");
        else if (!roleAllowed)
            warnings.Add($"The active role {RoleNames[bot.ActiveRole]} is not allowed by profile {profile!.Id}; the core will normalize it on login.");
        if (foreignPoints > 0)
            warnings.Add($"{foreignPoints} point(s) belong to a talent tree outside the character class.");

        string compatibilityStatus;
        string compatibilityMessage;
        bool compatible;

        if (profileProblem != null)
        {
            compatibilityStatus = "invalid_profile";
            compatibilityMessage = profileProblem;
            compatible = false;
        }
        else if (profile == null)
        {
            compatibilityStatus = "unassigned";
            compatibilityMessage = bot.SpecTab == 255
                ? "No specialization profile has been assigned yet. Existing talents were preserved."
                : "The selected specialization profile could not be resolved.";
            compatible = false;
        }
        else if (unexpected.Length > 0 || spentPoints > earnedPoints)
        {
            compatibilityStatus = "conflict";
            compatibilityMessage = unexpected.Length > 0
                ? $"{unexpected.Length} learned talent(s) exceed the selected profile. The core will preserve and flag this build."
                : $"The character has {spentPoints - earnedPoints} more talent point(s) than its level permits.";
            compatible = false;
        }
        else if (spentPoints < earnedPoints || missingAtLevel.Length > 0)
        {
            compatibilityStatus = "compatible_incomplete";
            compatibilityMessage = spentPoints < earnedPoints
                ? $"The learned build is compatible; {earnedPoints - spentPoints} earned point(s) remain to place."
                : "The learned choices fit the final profile, but their purchase order differs; the next earned point will fill the earliest missing planned rank.";
            compatible = true;
        }
        else
        {
            compatibilityStatus = "compatible";
            compatibilityMessage = "The learned ranks are compatible with the selected profile through this level.";
            compatible = true;
        }

        var trees = new List<BotTalentTreeView>(classTabs.Length);
        foreach (var tab in classTabs)
        {
            var treeTalents = catalog.Talents.Values
                .Where(t => t.TabId == tab.Id)
                .OrderBy(t => t.Row)
                .ThenBy(t => t.Column)
                .Select(t =>
                {
                    int current = actualRanks.GetValueOrDefault(t.Id);
                    int target = fullRanks.GetValueOrDefault(t.Id);
                    int targetAtLevel = levelRanks.GetValueOrDefault(t.Id);
                    var firstSpell = t.RankSpellIds.FirstOrDefault(id => id != 0);
                    string name = catalog.SpellInfo.TryGetValue(firstSpell, out var info) && !string.IsNullOrWhiteSpace(info.Name)
                        ? info.Name
                        : $"Talent #{t.Id}";
                    string icon = catalog.SpellInfo.TryGetValue(firstSpell, out info)
                        ? _dbc.GetSpellIconPath(info.SpellIconId)
                        : "/Icon/Get?name=inv_misc_questionmark";

                    return new BotTalentRankView
                    {
                        TalentId = t.Id,
                        TreeId = tab.Id,
                        Row = t.Row,
                        Column = t.Column,
                        Name = name,
                        IconUrl = icon,
                        CurrentRank = current,
                        MaxRank = t.MaxRank,
                        PlannedRank = target,
                        PlannedRankAtLevel = targetAtLevel,
                        CurrentSpellId = current > 0 && current <= t.RankSpellIds.Length
                            ? t.RankSpellIds[current - 1]
                            : 0,
                        IsUnexpected = unexpected.Contains(t.Id)
                    };
                })
                .ToArray();

            int actualTreePoints = treeTalents.Sum(t => t.CurrentRank);
            int plannedTreePoints = profile != null && tab.Order < profile.TreePoints.Length
                ? profile.TreePoints[tab.Order]
                : 0;

            trees.Add(new BotTalentTreeView
            {
                TreeId = tab.Id,
                Order = tab.Order,
                Name = string.IsNullOrWhiteSpace(tab.Name)
                    ? ResolveTreeName(catalog.Manifest, classKey, tab.Order)
                    : tab.Name,
                Points = actualTreePoints,
                PlannedPoints = plannedTreePoints,
                PlannedPointsAtLevel = treeTalents.Sum(t => t.PlannedRankAtLevel),
                Talents = treeTalents
            });
        }

        int availablePoints = Math.Max(0, earnedPoints - spentPoints);
        var next = profile == null ? null : FindNextPurchase(fullPlan, actualRanks, catalog, bot.Level, compatible, availablePoints);
        int activeRoleId = RoleNames.ContainsKey(bot.ActiveRole) ? bot.ActiveRole : 0;

        return new BotTalentVisibility
        {
            Guid = bot.Guid,
            Name = bot.Name,
            ClassId = bot.ClassId,
            ClassName = Humanize(classKey),
            Level = bot.Level,
            SpecTab = bot.SpecTab,
            ActiveRole = new BotActiveRoleView { Id = activeRoleId, Name = RoleNames[activeRoleId] },
            Profile = profile == null ? null : new BotTalentProfileView
            {
                Id = profile.Id,
                Spec = profile.Spec,
                Name = Humanize(profile.Spec),
                RolePolicy = profile.RolePolicy,
                GearPolicy = profile.GearPolicy,
                TreePoints = profile.TreePoints
            },
            Points = new BotTalentPointSummary
            {
                Earned = earnedPoints,
                Spent = spentPoints,
                Available = availablePoints,
                Overspent = Math.Max(0, spentPoints - earnedPoints)
            },
            Trees = trees,
            NextPlannedPurchase = next,
            Compatibility = new BotTalentCompatibilityView
            {
                Status = compatibilityStatus,
                Compatible = compatible,
                RoleAllowed = roleAllowed,
                MatchesLevelPlan = compatible && missingAtLevel.Length == 0,
                Message = compatibilityMessage,
                UnexpectedTalentIds = unexpected,
                MissingPlannedTalentIdsAtLevel = missingAtLevel,
                Warnings = warnings
            },
            AsOfUtc = DateTime.UtcNow
        };
    }

    private TalentCatalog LoadCatalog()
    {
        using var manifestStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ManifestResource)
            ?? throw new InvalidDataException($"Embedded resource {ManifestResource} is missing.");
        var manifest = JsonSerializer.Deserialize<TalentProfileManifest>(manifestStream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Talent profile manifest is empty.");

        if (manifest.ClientBuild != 5875)
            throw new InvalidDataException($"Talent profile build {manifest.ClientBuild} is not supported; expected 5875.");
        if (manifest.Profiles.Count != 27 || manifest.Profiles.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != 27)
            throw new InvalidDataException("Talent profile manifest must contain 27 unique profiles.");
        foreach (var profile in manifest.Profiles)
        {
            if (profile.TreePoints.Length != 3 || profile.TreePoints.Sum() != 51 || Expand(profile).Length != 51)
                throw new InvalidDataException($"Talent profile {profile.Id} does not define exactly 51 points across three trees.");
        }

        string talentPath = Path.Combine(_dbc.DbcPath, "Talent.dbc");
        string tabPath = Path.Combine(_dbc.DbcPath, "TalentTab.dbc");
        var talentDbc = WowDbcFile.Parse(File.ReadAllBytes(talentPath))
            ?? throw new InvalidDataException($"Could not parse {talentPath}.");
        var tabDbc = WowDbcFile.Parse(File.ReadAllBytes(tabPath))
            ?? throw new InvalidDataException($"Could not parse {tabPath}.");

        if (talentDbc.FieldCount != 21 || talentDbc.RecordSize != 84)
            throw new InvalidDataException($"Talent.dbc layout is {talentDbc.FieldCount} fields/{talentDbc.RecordSize} bytes; expected 21/84.");
        if (tabDbc.FieldCount < 14 || tabDbc.RecordSize < 56)
            throw new InvalidDataException("TalentTab.dbc does not contain the required build-5875 fields.");

        var tabs = new Dictionary<uint, TalentTabDefinition>();
        for (int row = 0; row < tabDbc.RecordCount; row++)
        {
            uint id = tabDbc.GetUInt(row, 0);
            tabs[id] = new TalentTabDefinition(
                id,
                tabDbc.GetStringIfStart(row, 1) ?? "",
                tabDbc.GetUInt(row, 12),
                checked((int)tabDbc.GetUInt(row, 13)));
        }

        var talents = new Dictionary<uint, TalentDefinition>();
        var byRankSpell = new Dictionary<uint, TalentSpellPosition>();
        for (int row = 0; row < talentDbc.RecordCount; row++)
        {
            uint id = talentDbc.GetUInt(row, 0);
            var ranks = Enumerable.Range(4, 5).Select(f => talentDbc.GetUInt(row, f)).ToArray();
            var talent = new TalentDefinition(
                id,
                talentDbc.GetUInt(row, 1),
                checked((int)talentDbc.GetUInt(row, 2)),
                checked((int)talentDbc.GetUInt(row, 3)),
                ranks);
            talents[id] = talent;
            for (int rank = 0; rank < ranks.Length && ranks[rank] != 0; rank++)
                byRankSpell[ranks[rank]] = new TalentSpellPosition(talent, rank + 1);
        }

        foreach (var profile in manifest.Profiles)
        foreach (uint talentId in Expand(profile))
            if (!talents.ContainsKey(talentId))
                throw new InvalidDataException($"Profile {profile.Id} references missing TalentID {talentId}.");

        return new TalentCatalog(manifest, tabs, talents, byRankSpell, _dbc.AllSpellEntries);
    }

    private static BotNextTalentView? FindNextPurchase(
        IReadOnlyList<uint> plan,
        IReadOnlyDictionary<uint, int> actualRanks,
        TalentCatalog catalog,
        int level,
        bool compatible,
        int availablePoints)
    {
        var occurrence = new Dictionary<uint, int>();
        for (int index = 0; index < plan.Count; index++)
        {
            uint talentId = plan[index];
            int rank = occurrence.GetValueOrDefault(talentId) + 1;
            occurrence[talentId] = rank;
            if (actualRanks.GetValueOrDefault(talentId) >= rank)
                continue;
            if (!catalog.Talents.TryGetValue(talentId, out var talent))
                return null;

            uint rankSpell = rank <= talent.RankSpellIds.Length ? talent.RankSpellIds[rank - 1] : 0;
            string name = catalog.SpellInfo.TryGetValue(rankSpell, out var info) && !string.IsNullOrWhiteSpace(info.Name)
                ? info.Name
                : $"Talent #{talentId}";
            int requiredLevel = 10 + index;
            return new BotNextTalentView
            {
                TalentId = talentId,
                Name = name,
                Rank = rank,
                SpellId = rankSpell,
                RequiredLevel = requiredLevel,
                DueNow = compatible && availablePoints > 0 && requiredLevel <= level
            };
        }
        return null;
    }

    private static uint[] Expand(TalentProfile profile)
    {
        var result = new List<uint>(51);
        foreach (var chunk in profile.Chunks)
        {
            if (chunk.Length != 2 || chunk[0] <= 0 || chunk[1] <= 0)
                throw new InvalidDataException($"Talent profile {profile.Id} contains an invalid chunk.");
            for (int i = 0; i < chunk[1]; i++)
                result.Add(checked((uint)chunk[0]));
        }
        return result.ToArray();
    }

    private static Dictionary<uint, int> CountRanks(IEnumerable<uint> sequence)
    {
        var result = new Dictionary<uint, int>();
        foreach (uint id in sequence)
            result[id] = result.GetValueOrDefault(id) + 1;
        return result;
    }

    private static string ResolveTreeName(TalentProfileManifest manifest, string classKey, int order)
        => manifest.TreeOrder.TryGetValue(classKey, out var trees) && order >= 0 && order < trees.Length
            ? Humanize(trees[order])
            : $"Tree {order + 1}";

    private static int[] AllowedRoles(TalentProfile profile) => (profile.ClassId, profile.Spec) switch
    {
        (1, "arms" or "fury") => new[] { 1, 3 },
        (1, "protection") => new[] { 3 },
        (2, "holy") => new[] { 4 },
        (2, "protection") => new[] { 3 },
        (2, "retribution") => new[] { 1 },
        (3, _) => new[] { 2 },
        (4, _) => new[] { 1 },
        (5, "discipline" or "holy") => new[] { 4 },
        (5, "shadow") => new[] { 2 },
        (7, "elemental") => new[] { 2 },
        (7, "enhancement") => new[] { 1 },
        (7, "restoration") => new[] { 4 },
        (8, _) => new[] { 2 },
        (9, _) => new[] { 2 },
        (11, "balance") => new[] { 2 },
        (11, "feral_combat") => new[] { 1, 3 },
        (11, "restoration") => new[] { 4 },
        _ => Array.Empty<int>()
    };

    private static bool IsRoleAllowed(TalentProfile profile, int role)
        => AllowedRoles(profile).Contains(role);

    private static string Humanize(string value)
        => string.Join(' ', value.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..]));

    private static BotTalentVisibility Error(
        int guid,
        string code,
        string message,
        BotTalentDbRow? bot = null)
        => new()
        {
            Guid = guid,
            Name = bot?.Name ?? "",
            ClassId = bot?.ClassId ?? 0,
            Level = bot?.Level ?? 0,
            SpecTab = bot?.SpecTab ?? 255,
            ErrorCode = code,
            Error = message,
            AsOfUtc = DateTime.UtcNow,
            Compatibility = new BotTalentCompatibilityView
            {
                Status = code,
                Compatible = false,
                Message = message
            }
        };

    private sealed class BotTalentDbRow
    {
        public int Guid { get; set; }
        public string Name { get; set; } = "";
        public int ClassId { get; set; }
        public int Level { get; set; }
        public int SpecTab { get; set; } = 255;
        public int ActiveRole { get; set; }
    }

    private sealed record TalentTabDefinition(uint Id, string Name, uint ClassMask, int Order)
    {
        public bool SupportsClass(int classId)
            => classId > 0 && (ClassMask & (1u << (classId - 1))) != 0;
    }

    private sealed record TalentDefinition(uint Id, uint TabId, int Row, int Column, uint[] RankSpellIds)
    {
        public int MaxRank => RankSpellIds.TakeWhile(id => id != 0).Count();
    }

    private sealed record TalentSpellPosition(TalentDefinition Talent, int Rank);

    private sealed record TalentCatalog(
        TalentProfileManifest Manifest,
        IReadOnlyDictionary<uint, TalentTabDefinition> Tabs,
        IReadOnlyDictionary<uint, TalentDefinition> Talents,
        IReadOnlyDictionary<uint, TalentSpellPosition> ByRankSpell,
        IReadOnlyDictionary<uint, SpellDbcEntry> SpellInfo);

    private sealed class TalentProfileManifest
    {
        [JsonPropertyName("client_build")]
        public int ClientBuild { get; set; }

        [JsonPropertyName("tree_order")]
        public Dictionary<string, string[]> TreeOrder { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        [JsonPropertyName("profiles")]
        public List<TalentProfile> Profiles { get; set; } = new();
    }

    private sealed class TalentProfile
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("class_id")]
        public int ClassId { get; set; }

        [JsonPropertyName("spec")]
        public string Spec { get; set; } = "";

        [JsonPropertyName("tree_points")]
        public int[] TreePoints { get; set; } = Array.Empty<int>();

        [JsonPropertyName("role_policy")]
        public string RolePolicy { get; set; } = "";

        [JsonPropertyName("gear_policy")]
        public string GearPolicy { get; set; } = "";

        [JsonPropertyName("chunks")]
        public List<int[]> Chunks { get; set; } = new();
    }
}

public sealed class BotTalentVisibility
{
    public int Guid { get; set; }
    public string Name { get; set; } = "";
    public int ClassId { get; set; }
    public string ClassName { get; set; } = "";
    public int Level { get; set; }
    public int SpecTab { get; set; } = 255;
    public BotActiveRoleView? ActiveRole { get; set; }
    public BotTalentProfileView? Profile { get; set; }
    public BotTalentPointSummary? Points { get; set; }
    public IReadOnlyList<BotTalentTreeView> Trees { get; set; } = Array.Empty<BotTalentTreeView>();
    public BotNextTalentView? NextPlannedPurchase { get; set; }
    public BotTalentCompatibilityView Compatibility { get; set; } = new();
    public DateTime AsOfUtc { get; set; }
    public string? ErrorCode { get; set; }
    public string? Error { get; set; }
}

public sealed class BotActiveRoleView
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed class BotTalentProfileView
{
    public string Id { get; set; } = "";
    public string Spec { get; set; } = "";
    public string Name { get; set; } = "";
    public string RolePolicy { get; set; } = "";
    public string GearPolicy { get; set; } = "";
    public int[] TreePoints { get; set; } = Array.Empty<int>();
}

public sealed class BotTalentPointSummary
{
    public int Earned { get; set; }
    public int Spent { get; set; }
    public int Available { get; set; }
    public int Overspent { get; set; }
}

public sealed class BotTalentTreeView
{
    public uint TreeId { get; set; }
    public int Order { get; set; }
    public string Name { get; set; } = "";
    public int Points { get; set; }
    public int PlannedPoints { get; set; }
    public int PlannedPointsAtLevel { get; set; }
    public IReadOnlyList<BotTalentRankView> Talents { get; set; } = Array.Empty<BotTalentRankView>();
}

public sealed class BotTalentRankView
{
    public uint TalentId { get; set; }
    public uint TreeId { get; set; }
    public int Row { get; set; }
    public int Column { get; set; }
    public string Name { get; set; } = "";
    public string IconUrl { get; set; } = "";
    public int CurrentRank { get; set; }
    public int MaxRank { get; set; }
    public int PlannedRank { get; set; }
    public int PlannedRankAtLevel { get; set; }
    public uint CurrentSpellId { get; set; }
    public bool IsUnexpected { get; set; }
}

public sealed class BotNextTalentView
{
    public uint TalentId { get; set; }
    public string Name { get; set; } = "";
    public int Rank { get; set; }
    public uint SpellId { get; set; }
    public int RequiredLevel { get; set; }
    public bool DueNow { get; set; }
}

public sealed class BotTalentCompatibilityView
{
    public string Status { get; set; } = "unknown";
    public bool Compatible { get; set; }
    public bool RoleAllowed { get; set; }
    public bool MatchesLevelPlan { get; set; }
    public string Message { get; set; } = "";
    public IReadOnlyList<uint> UnexpectedTalentIds { get; set; } = Array.Empty<uint>();
    public IReadOnlyList<uint> MissingPlannedTalentIdsAtLevel { get; set; } = Array.Empty<uint>();
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Where one learned spell came from, when Talent.dbc grants it. Consumed by the
/// spellbook read model; the talent view itself works from the full catalog.
/// </summary>
public sealed class BotTalentSpellOrigin
{
    public uint TalentId { get; set; }
    public uint TreeId { get; set; }
    public string TreeName { get; set; } = "";
    public int Rank { get; set; }
    public int MaxRank { get; set; }
}

public sealed class BotTalentProfileOption
{
    public string Id { get; set; } = "";
    public int ClassId { get; set; }
    public int SpecTab { get; set; }
    public string Spec { get; set; } = "";
    public string Name { get; set; } = "";
    public string RolePolicy { get; set; } = "";
    public string GearPolicy { get; set; } = "";
    public int[] TreePoints { get; set; } = Array.Empty<int>();
    public int[] AllowedRoles { get; set; } = Array.Empty<int>();
    /// <summary>Zero means the core should choose its deterministic profile default.</summary>
    public int DefaultRole { get; set; }
}
