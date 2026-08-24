using System.Security.Cryptography;
using MangosSuperUI.Services;
using MangosSuperUI.Services.Mpq;
using MangosSuperUI.Services.WeaponForge;

namespace MangosSuperUI.Services.ArmorForge;

/// <summary>
/// The pure snapshot patch builder for forged armor — the armor-side sibling of
/// <see cref="WeaponPatchBuilder"/>. Given the clean base ItemDisplayInfo.dbc (and, for tier sets,
/// the clean base ItemSet.dbc) plus the armor display rows and model/component BLP members, it
/// produces one immutable <c>patch-6.MPQ</c> and validates it by reopening and byte-comparing every
/// member. No live side effects — the only file it touches is a caller-supplied temp path used to
/// reopen the archive for verification.
///
/// patch-6 is the TOP patch in the mount order (patch-6 &gt; patch-5 weapons &gt; patch-4 retextures),
/// so its ItemDisplayInfo.dbc must be built on the effective mounted state BENEATH patch-6 — the
/// caller resolves that base (excluding patch-6) so patch-6 re-unions patch-4 + patch-5 rows.
/// </summary>
public sealed class ArmorPatchBuilder
{
    private readonly ILogger<ArmorPatchBuilder>? _logger;

    public ArmorPatchBuilder(ILogger<ArmorPatchBuilder>? logger = null) => _logger = logger;

    public ArmorPatchResult Build(ArmorPatchInput input, string tempDir)
    {
        if (input.CleanItemDisplayInfoDbc is null || input.CleanItemDisplayInfoDbc.Length == 0)
            throw new ArgumentException("Clean ItemDisplayInfo.dbc bytes are required.", nameof(input));

        // 1) ItemDisplayInfo.dbc snapshot: clean base + the union of armor display rows.
        var dbc = DbcWriterService.ReadDbc(input.CleanItemDisplayInfoDbc, ArmorNaming.ItemDisplayInfoMember);
        if (dbc.RecordSize != ArmorDisplayInfoRow.RecordSize)
            throw new InvalidOperationException(
                $"Base DBC record size {dbc.RecordSize} != ItemDisplayInfo {ArmorDisplayInfoRow.RecordSize}.");

        var diag = new ForgeDiagnostics("armor-package");
        foreach (var display in input.Displays.OrderBy(d => d.Params.DisplayId))
        {
            if (dbc.GetRow(display.Params.DisplayId) is not null)
                throw new InvalidOperationException(
                    $"Display id {display.Params.DisplayId} already exists in the base DBC; the reservation registry should have prevented this.");
            var row = ArmorDisplayInfoRow.BuildAndAdd(dbc, display.Params);
            ArmorDisplayInfoRow.Validate(dbc, row, display.RenderKind, diag);
        }
        if (diag.HasErrors)
            throw new InvalidOperationException("Armor DBC row validation failed: " +
                string.Join("; ", diag.Items.Where(i => i.Severity == ForgeSeverity.Error).Select(i => i.Message)));

        byte[] dbcBytes = dbc.Write();

        // 2) ItemSet.dbc snapshot (only when there are tier sets AND a base to extend).
        byte[]? setBytes = null;
        if (input.Sets is { Count: > 0 })
        {
            if (input.CleanItemSetDbc is null || input.CleanItemSetDbc.Length == 0)
                throw new InvalidOperationException(
                    "Tier sets were requested but no base ItemSet.dbc was supplied. Cannot ship set bonuses.");
            setBytes = ArmorItemSetDbc.Build(input.CleanItemSetDbc, input.Sets);
        }

        // 3) Assemble members: DBC(s) + every model + every texture, canonical paths.
        var members = new Dictionary<string, (string Canonical, byte[] Data)>(StringComparer.OrdinalIgnoreCase);
        void AddMember(string mpqPath, byte[] data)
        {
            string canonical = CanonicalMpqPath(mpqPath);
            if (members.ContainsKey(canonical))
                throw new InvalidOperationException($"Duplicate MPQ member path '{canonical}'.");
            members[canonical] = (canonical, data);
        }

        AddMember(ArmorNaming.ItemDisplayInfoMember, dbcBytes);
        if (setBytes != null) AddMember(ArmorNaming.ItemSetMember, setBytes);
        foreach (var m in input.Models) AddMember(m.MpqPath, m.Data);
        foreach (var t in input.Textures) AddMember(t.MpqPath, t.Data);

        // 4) Insert in canonical path order and build the MPQ bytes (pure, no I/O).
        var ordered = members.Values
            .OrderBy(v => v.Canonical, StringComparer.OrdinalIgnoreCase)
            .Select(v => new KeyValuePair<string, byte[]>(v.Canonical, v.Data))
            .ToList();
        byte[] mpqBytes = MpqArchiveWriter.Build(ordered);

        // 5) Reopen and byte-verify every member.
        var packaged = ReopenAndVerify(mpqBytes, ordered, tempDir);

        string mpqSha = Sha256(mpqBytes);
        _logger?.LogInformation("ArmorForge: built patch-6 ({Members} members, {Bytes:N0} bytes, sha {Sha})",
            packaged.Count, mpqBytes.Length, mpqSha[..12]);

        return new ArmorPatchResult
        {
            MpqBytes = mpqBytes,
            MpqSha256 = mpqSha,
            DbcBytes = dbcBytes,
            DbcSha256 = Sha256(dbcBytes),
            ItemSetDbcBytes = setBytes,
            ItemSetOmitted = input.SetsOmitted,
            Members = packaged,
            AllVerified = packaged.All(p => p.Verified),
        };
    }

    private List<PackagedMember> ReopenAndVerify(byte[] mpqBytes, List<KeyValuePair<string, byte[]>> expected, string tempDir)
    {
        Directory.CreateDirectory(tempDir);
        string tempPath = Path.Combine(tempDir, $"verify-armor-{Guid.NewGuid():N}.MPQ");
        try
        {
            File.WriteAllBytes(tempPath, mpqBytes);
            using var archive = MpqArchive.Open(tempPath)
                ?? throw new InvalidOperationException("Reopen of freshly built armor MPQ failed (no header).");

            var result = new List<PackagedMember>(expected.Count);
            foreach (var (path, data) in expected)
            {
                byte[]? readBack = archive.ReadFile(path);
                bool verified = readBack is not null && readBack.AsSpan().SequenceEqual(data);
                result.Add(new PackagedMember { MpqPath = path, Size = data.Length, Sha256 = Sha256(data), Verified = verified });
                if (!verified)
                    _logger?.LogError("ArmorForge: member '{Path}' failed byte verification after repack", path);
            }
            return result;
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* scratch cleanup best-effort */ }
        }
    }

    private static string CanonicalMpqPath(string path)
    {
        var p = path.Replace('/', '\\').Trim().TrimStart('\\');
        while (p.Contains("\\\\")) p = p.Replace("\\\\", "\\");
        return p;
    }

    private static string Sha256(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}

/// <summary>One armor display row to pack + the render kind (drives which validator branch runs).</summary>
public sealed class ArmorDisplayEntry
{
    public required ArmorDisplayInfoParams Params { get; init; }
    public required ArmorRenderKind RenderKind { get; init; }
}

/// <summary>Everything the armor patch builder needs.</summary>
public sealed class ArmorPatchInput
{
    public required byte[] CleanItemDisplayInfoDbc { get; init; }
    public required IReadOnlyList<ArmorDisplayEntry> Displays { get; init; }
    public required IReadOnlyList<MpqMember> Models { get; init; }
    public required IReadOnlyList<MpqMember> Textures { get; init; }
    /// <summary>Tier sets to write into ItemSet.dbc; empty means no ItemSet.dbc is added to the patch.</summary>
    public IReadOnlyList<ArmorSetDefinition> Sets { get; init; } = Array.Empty<ArmorSetDefinition>();
    /// <summary>Clean base ItemSet.dbc bytes (required only when <see cref="Sets"/> is non-empty).</summary>
    public byte[]? CleanItemSetDbc { get; init; }
    /// <summary>True when forged sets exist but their rows had to be dropped (no readable base
    /// ItemSet.dbc). Carried onto the result so the deploy step reports a failure rather than the
    /// "nothing to deploy" success it is otherwise indistinguishable from.</summary>
    public bool SetsOmitted { get; init; }
}

public sealed class ArmorPatchResult
{
    public required byte[] MpqBytes { get; init; }
    public required string MpqSha256 { get; init; }
    public required byte[] DbcBytes { get; init; }
    public required string DbcSha256 { get; init; }
    public byte[]? ItemSetDbcBytes { get; init; }
    /// <summary>Forged sets exist but no ItemSet.dbc could be built for them — see
    /// <see cref="ArmorPatchInput.SetsOmitted"/>. Always a bug, never a legitimate state.</summary>
    public bool ItemSetOmitted { get; init; }
    public required IReadOnlyList<PackagedMember> Members { get; init; }
    public required bool AllVerified { get; init; }
}

/// <summary>Outcome of pushing ItemSet.dbc into the server's own dbc directory.</summary>
public enum ItemSetDeployState
{
    /// <summary>No forged sets exist, so the server needs nothing.</summary>
    NotNeeded,
    /// <summary>Written and byte-verified. Takes effect on the next mangosd restart.</summary>
    Deployed,
    /// <summary>Could not be written — forged sets will be zeroed by the core at load.</summary>
    Failed,
}
