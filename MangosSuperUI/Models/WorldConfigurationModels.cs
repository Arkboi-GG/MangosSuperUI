using System.Globalization;

namespace MangosSuperUI.Models;

/// <summary>
/// Configuration which travels with a parked world and is applied after its
/// snapshot is restored but before the world processes are started.
/// </summary>
public sealed class WorldLaunchConfiguration
{
    public string ProfileId { get; set; } = WorldConfigurationCatalog.RtsR1ProfileId;
    public int RealmId { get; set; } = 1;
    public int PlayerLimit { get; set; } = 2600;
    public int PlayerHardLimit { get; set; } = 2600;
    public int LoginPerTick { get; set; }
    public int AllianceBotCap { get; set; } = 1250;
    public int HordeBotCap { get; set; } = 1250;
    public int StateFlushMs { get; set; } = 30000;
    public Dictionary<string, double> Rates { get; set; } = WorldConfigurationCatalog.CreateDefaultRates();

    public WorldLaunchConfiguration Clone() => new()
    {
        ProfileId = ProfileId,
        RealmId = RealmId,
        PlayerLimit = PlayerLimit,
        PlayerHardLimit = PlayerHardLimit,
        LoginPerTick = LoginPerTick,
        AllianceBotCap = AllianceBotCap,
        HordeBotCap = HordeBotCap,
        StateFlushMs = StateFlushMs,
        Rates = new Dictionary<string, double>(Rates ?? new(), StringComparer.OrdinalIgnoreCase)
    };
}

public sealed class WorldConfigurationField
{
    public string Key { get; init; } = "";
    public string Label { get; init; } = "";
    public string Section { get; init; } = "";
    public string Help { get; init; } = "";
    public double DefaultValue { get; init; }
    public double Min { get; init; }
    public double Max { get; init; }
    public double Step { get; init; } = 1;
}

public static class WorldConfigurationCatalog
{
    public const string RtsR1ProfileId = "rts-r1-v1";

    public static readonly IReadOnlyList<WorldConfigurationField> RateFields = new[]
    {
        Rate("rate.xp_kill", "Kill XP", "Progression", 40, 0.01, 1000, 0.25,
            "Multiplier for XP awarded by normal creature kills."),
        Rate("rate.xp_kill_elite", "Elite XP factor", "Progression", 1, 0.01, 1000, 0.25,
            "Additional factor applied to elite kill XP."),
        Rate("rate.xp_quest", "Quest XP", "Progression", 40, 0.01, 1000, 0.25,
            "Multiplier for quest XP."),
        Rate("rate.drop_money", "Money drops", "Economy", 1, 0.01, 1000, 0.25,
            "Multiplier for creature money drops."),
        Rate("rate.drop_item_poor", "Poor item drops", "Economy", 1, 0.01, 1000, 0.25, "Poor-quality item drop multiplier."),
        Rate("rate.drop_item_normal", "Common item drops", "Economy", 1, 0.01, 1000, 0.25, "Common-quality item drop multiplier."),
        Rate("rate.drop_item_uncommon", "Uncommon item drops", "Economy", 1, 0.01, 1000, 0.25, "Uncommon-quality item drop multiplier."),
        Rate("rate.drop_item_rare", "Rare item drops", "Economy", 1, 0.01, 1000, 0.25, "Rare-quality item drop multiplier."),
        Rate("rate.drop_item_epic", "Epic item drops", "Economy", 1, 0.01, 1000, 0.25, "Epic-quality item drop multiplier."),
        Rate("rate.drop_item_legendary", "Legendary item drops", "Economy", 1, 0.01, 1000, 0.25, "Legendary-quality item drop multiplier."),
        Rate("rate.drop_item_artifact", "Artifact item drops", "Economy", 1, 0.01, 1000, 0.25, "Artifact-quality item drop multiplier."),
        Rate("rate.drop_item_referenced", "Referenced loot", "Economy", 1, 0.01, 1000, 0.25, "Referenced loot-table multiplier.")
    };

    public static Dictionary<string, double> CreateDefaultRates() =>
        RateFields.ToDictionary(x => x.Key, x => x.DefaultValue, StringComparer.OrdinalIgnoreCase);

    public static WorldLaunchConfiguration NormalizeAndValidate(WorldLaunchConfiguration? input)
    {
        var value = input?.Clone() ?? new WorldLaunchConfiguration();
        if (!string.Equals(value.ProfileId, RtsR1ProfileId, StringComparison.Ordinal))
            throw new InvalidOperationException($"Unsupported world profile '{value.ProfileId}'.");
        if (value.RealmId <= 0)
            throw new InvalidOperationException("Realm ID must be positive.");
        if (value.PlayerLimit is < 1 or > 100000)
            throw new InvalidOperationException("PlayerLimit must be between 1 and 100000.");
        if (value.PlayerHardLimit != 0 && value.PlayerHardLimit < value.PlayerLimit)
            throw new InvalidOperationException("PlayerHardLimit must be zero or at least PlayerLimit.");
        if (value.PlayerHardLimit > 100000)
            throw new InvalidOperationException("PlayerHardLimit cannot exceed 100000.");
        if (value.LoginPerTick is < 0 or > 100)
            throw new InvalidOperationException("LoginPerTick must be between 0 and 100.");
        if (value.AllianceBotCap is < 0 or > 50000 || value.HordeBotCap is < 0 or > 50000)
            throw new InvalidOperationException("Faction bot caps must be between 0 and 50000.");
        var combinedBotCap = checked(value.AllianceBotCap + value.HordeBotCap);
        if (combinedBotCap > value.PlayerLimit)
            throw new InvalidOperationException(
                $"Combined faction bot caps ({combinedBotCap}) cannot exceed PlayerLimit ({value.PlayerLimit}). " +
                "PlayerLimit minus the combined caps is the session headroom left for humans and other logins.");
        if (value.StateFlushMs is < 1000 or > 600000)
            throw new InvalidOperationException("RTS state flush interval must be between 1000 and 600000 milliseconds.");

        var supplied = value.Rates ?? new Dictionary<string, double>();
        var normalized = CreateDefaultRates();
        var known = RateFields.ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in supplied)
        {
            if (!known.TryGetValue(pair.Key, out var field))
                throw new InvalidOperationException($"Unsupported RTS setting '{pair.Key}'.");
            if (!double.IsFinite(pair.Value) || pair.Value < field.Min || pair.Value > field.Max)
                throw new InvalidOperationException($"{field.Label} must be between {field.Min} and {field.Max}.");
            normalized[field.Key] = pair.Value;
        }
        value.Rates = normalized;
        return value;
    }

    public static void ValidateNamePoolCapacity(WorldLaunchConfiguration input, int eligibleUniqueNames)
    {
        var value = NormalizeAndValidate(input);
        if (eligibleUniqueNames < 0)
            throw new InvalidOperationException("Eligible bot-name count cannot be negative.");
        var combinedBotCap = checked(value.AllianceBotCap + value.HordeBotCap);
        if (combinedBotCap > eligibleUniqueNames)
            throw new InvalidOperationException(
                $"Combined faction bot caps require {combinedBotCap:N0} unique names, " +
                $"but the current bot name pool has {eligibleUniqueNames:N0} eligible unique names.");
    }

    public static IReadOnlyDictionary<string, string> ToWorldStateRows(WorldLaunchConfiguration input)
    {
        var value = NormalizeAndValidate(input);
        var rows = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["mode"] = "rts",
            ["bots.cap.alliance"] = value.AllianceBotCap.ToString(CultureInfo.InvariantCulture),
            ["bots.cap.horde"] = value.HordeBotCap.ToString(CultureInfo.InvariantCulture),
            ["state.flush_ms"] = value.StateFlushMs.ToString(CultureInfo.InvariantCulture)
        };
        foreach (var field in RateFields)
            rows[field.Key] = value.Rates[field.Key].ToString("R", CultureInfo.InvariantCulture);
        return rows;
    }

    private static WorldConfigurationField Rate(
        string key, string label, string section, double defaultValue, double min, double max, double step, string help) => new()
        {
            Key = key,
            Label = label,
            Section = section,
            Help = help,
            DefaultValue = defaultValue,
            Min = min,
            Max = max,
            Step = step
        };
}

public sealed class CreateRtsWorldRequestModel
{
    public string Name { get; set; } = "RTS Campaign";
    public string SourceWorldId { get; set; } = "";
    public string SourceSnapshot { get; set; } = "";
    public string? Notes { get; set; }
    public WorldLaunchConfiguration Configuration { get; set; } = new();
}

public sealed class SnapshotArtifact
{
    public string Id { get; set; } = "";
    public string Group { get; set; } = "";
    public string FileName { get; set; } = "";
    public string Format { get; set; } = "";
    public long Length { get; set; }
    public string Sha256 { get; set; } = "";
    public bool Required { get; set; } = true;
}

public sealed class SnapshotArtifactCheck
{
    public string Id { get; set; } = "";
    public string FileName { get; set; } = "";
    public bool Present { get; set; }
    public bool Valid { get; set; }
    public string Detail { get; set; } = "";
}

public sealed class WorldRestorePreflight
{
    public bool Allowed => Blockers.Count == 0;
    public bool Legacy { get; set; }
    public int SchemaVersion { get; set; }
    public bool AlreadyMaterialized { get; set; }
    public bool ForceFullRestore { get; set; }
    public string Strategy { get; set; } = "full-restore";
    public bool RtsSourceEligible { get; set; }
    public string Integrity { get; set; } = "unknown";
    public string ConfigStatus { get; set; } = "unknown";
    public bool MangosdRunning { get; set; }
    public bool RealmdRunning { get; set; }
    public Dictionary<string, int?> ConfigValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public WorldLaunchConfiguration? SavedConfiguration { get; set; }
    public int? NamePoolEligible { get; set; }
    public string? NamePoolSha256 { get; set; }
    public List<string> Blockers { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<SnapshotArtifactCheck> Artifacts { get; set; } = new();
}
