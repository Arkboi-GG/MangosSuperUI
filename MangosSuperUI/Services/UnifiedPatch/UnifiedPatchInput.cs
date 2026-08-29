using MangosSuperUI.Services.ArmorForge;
using MangosSuperUI.Services.WeaponForge;

namespace MangosSuperUI.Services.UnifiedPatch;

/// <summary>
/// Everything <see cref="UnifiedPatchBuilder"/> needs to emit the single patch: the stock base DBC
/// and, per lane, the display rows to add plus the MPQ members to pack. Members are kept in
/// separate per-lane lists purely so a duplicate-path collapse can name which lane the loser came
/// from; the builder concatenates them in retexture → weapon → armor order.
/// </summary>
public sealed class UnifiedPatchInput
{
    /// <summary>Stock ItemDisplayInfo.dbc, resolved from strictly BENEATH the unified patch so the
    /// builder never reads its own previous output back as input.</summary>
    public required byte[] CleanItemDisplayInfoDbc { get; init; }

    public IReadOnlyList<RetextureDisplayEntry> RetextureDisplays { get; init; } = [];
    public IReadOnlyList<WeaponDisplayInfoParams> WeaponDisplays { get; init; } = [];
    public IReadOnlyList<ArmorDisplayEntry> ArmorDisplays { get; init; } = [];

    public IReadOnlyList<MpqMember> RetextureMembers { get; init; } = [];
    public IReadOnlyList<MpqMember> WeaponMembers { get; init; } = [];
    public IReadOnlyList<MpqMember> ArmorMembers { get; init; } = [];

    /// <summary>Forged armor tier sets; requires <see cref="CleanItemSetDbc"/> when non-empty.</summary>
    public IReadOnlyList<ArmorSetDefinition> Sets { get; init; } = [];
    public byte[]? CleanItemSetDbc { get; init; }

    /// <summary>True when sets existed but the base ItemSet.dbc could not be read, so they were
    /// dropped from this build. Surfaced to the caller rather than failing the whole patch.</summary>
    public bool SetsOmitted { get; init; }

    /// <summary>Shared diagnostics sink. Supplied by the orchestrator so lane-gathering warnings and
    /// packaging warnings land in one report; a fresh one is created when null.</summary>
    public ForgeDiagnostics? Diagnostics { get; init; }
}

/// <summary>
/// One retexture: clone stock display <see cref="SourceDisplayId"/> into <see cref="NewDisplayId"/>
/// and repoint texture fields on the copy. Field 3 is TextureName1 (model texture); fields 14..21
/// are m_texture[0..7] (body-atlas component slots). Values are BARE names — no directory, no
/// extension, no gender suffix — because the client composes those itself.
/// </summary>
public sealed class RetextureDisplayEntry
{
    public required uint SourceDisplayId { get; init; }
    public required uint NewDisplayId { get; init; }

    /// <summary>DBC field index → bare texture name to write into it.</summary>
    public required IReadOnlyDictionary<int, string> TexturePatches { get; init; }
}

public sealed class UnifiedPatchResult
{
    public required byte[] MpqBytes { get; init; }
    public required string MpqSha256 { get; init; }
    public required byte[] DbcBytes { get; init; }
    public required string DbcSha256 { get; init; }
    public required IReadOnlyList<PackagedMember> Members { get; init; }
    public required bool AllVerified { get; init; }

    /// <summary>The built ItemSet.dbc, when forged armor declared tier sets. Null otherwise. The
    /// caller needs these bytes separately from the archive: mangosd reads ItemSet.dbc as a FILE in
    /// its own dbc folder, and zeroes every forged set_id without it, whatever the client got.</summary>
    public byte[]? ItemSetDbcBytes { get; init; }

    public required int RetextureRows { get; init; }
    public required int WeaponRows { get; init; }
    public required int ArmorRows { get; init; }
    public required int SetCount { get; init; }

    public required ForgeDiagnostics Diagnostics { get; init; }

    public int TotalRows => RetextureRows + WeaponRows + ArmorRows;
}
