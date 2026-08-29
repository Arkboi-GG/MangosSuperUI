using MangosSuperUI.Services.ArmorForge;
using MangosSuperUI.Services.WeaponForge;

namespace MangosSuperUI.Services.UnifiedPatch;

/// <summary>
/// What one lane hands the unified builder: the display rows it wants added and the MPQ members
/// that back them. Every lane sources these from its OWN database tables, never from a mounted
/// archive — that is what lets patch-5 and patch-6 stop existing without losing anything already
/// forged, and what makes the first unified rebuild a straight repackage of existing content.
/// </summary>
public sealed class WeaponLaneContribution
{
    public IReadOnlyList<WeaponDisplayInfoParams> Displays { get; init; } = [];
    public IReadOnlyList<MpqMember> Members { get; init; } = [];

    /// <summary>Weapons in the registry with no packageable bytes. They are reported, not shipped —
    /// a display row naming art the archive does not contain is a guaranteed error model in-game.</summary>
    public int SkippedCount { get; init; }
}

public sealed class ArmorLaneContribution
{
    public IReadOnlyList<ArmorDisplayEntry> Displays { get; init; } = [];
    public IReadOnlyList<MpqMember> Members { get; init; } = [];

    /// <summary>Forged tier sets and the stock ItemSet.dbc to extend. Armor is the only lane that
    /// ships a second DBC.</summary>
    public IReadOnlyList<ArmorSetDefinition> Sets { get; init; } = [];
    public byte[]? CleanItemSetDbc { get; init; }
    public bool SetsOmitted { get; init; }

    public int SkippedCount { get; init; }
}

public sealed class RetextureLaneContribution
{
    public IReadOnlyList<RetextureDisplayEntry> Displays { get; init; } = [];
    public IReadOnlyList<MpqMember> Members { get; init; } = [];
}
