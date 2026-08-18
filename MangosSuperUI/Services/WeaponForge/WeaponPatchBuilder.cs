using System.Security.Cryptography;
using MangosSuperUI.Services.Mpq;

namespace MangosSuperUI.Services.WeaponForge;

/// <summary>
/// The pure snapshot patch builder (WEAPON_GEN.md §2.5, §4.3). Given the clean base
/// ItemDisplayInfo.dbc plus the weapon's model/texture/display records, it produces one immutable
/// patch MPQ and validates it by reopening and byte-comparing every member. It has NO live side
/// effects: it never copies into a client Data directory, never writes wwwroot, never issues RA
/// commands — the only file it touches is a caller-supplied temp path used to reopen the archive
/// for verification. This is the deliberate replacement for ItemRetextureService.RebuildPatchMAsync
/// (which early-returns on an empty weapon table, omits M2 bytes entirely, and auto-deploys).
///
/// Determinism (WEAPON_GEN.md §4.3): member paths are normalized to canonical backslashes and
/// compared case-insensitively (duplicates fail the build rather than silently overwrite), display
/// rows are added by ascending id, the DBC writer sorts rows and rebuilds the string block, and
/// members are inserted in canonical path order.
/// </summary>
public sealed class WeaponPatchBuilder
{
    private readonly ILogger<WeaponPatchBuilder>? _logger;

    public WeaponPatchBuilder(ILogger<WeaponPatchBuilder>? logger = null) => _logger = logger;

    public WeaponPatchResult Build(WeaponPatchInput input, string tempDir)
    {
        if (input.CleanItemDisplayInfoDbc is null || input.CleanItemDisplayInfoDbc.Length == 0)
            throw new ArgumentException("Clean ItemDisplayInfo.dbc bytes are required.", nameof(input));

        // 1) Build the authoritative DBC snapshot: clean base + the union of custom display rows.
        var dbc = DbcWriterService.ReadDbc(input.CleanItemDisplayInfoDbc, WeaponNaming.ItemDisplayInfoMember);
        if (dbc.RecordSize != WeaponDisplayInfoRow.RecordSize)
            throw new InvalidOperationException($"Base DBC record size {dbc.RecordSize} != ItemDisplayInfo {WeaponDisplayInfoRow.RecordSize}.");

        var diag = new ForgeDiagnostics("package");
        foreach (var display in input.Displays.OrderBy(d => d.DisplayId))
        {
            if (dbc.GetRow(display.DisplayId) is not null)
                throw new InvalidOperationException($"Display id {display.DisplayId} already exists in the base DBC; the reservation registry should have prevented this.");
            var row = WeaponDisplayInfoRow.BuildAndAdd(dbc, display);
            WeaponDisplayInfoRow.Validate(dbc, row, diag);
        }
        if (diag.HasErrors)
            throw new InvalidOperationException("DBC row validation failed: " + string.Join("; ", diag.Items.Where(i => i.Severity == ForgeSeverity.Error).Select(i => i.Message)));

        byte[] dbcBytes = dbc.Write();

        // 2) Assemble members: one authoritative DBC + every model + every texture, canonical paths.
        var members = new Dictionary<string, (string Canonical, byte[] Data)>(StringComparer.OrdinalIgnoreCase);
        void AddMember(string mpqPath, byte[] data)
        {
            string canonical = CanonicalMpqPath(mpqPath);
            if (members.ContainsKey(canonical))
                throw new InvalidOperationException($"Duplicate MPQ member path '{canonical}'.");
            members[canonical] = (canonical, data);
        }

        AddMember(WeaponNaming.ItemDisplayInfoMember, dbcBytes);
        foreach (var m in input.Models) AddMember(m.MpqPath, m.Data);
        foreach (var t in input.Textures) AddMember(t.MpqPath, t.Data);

        // 3) Insert in canonical path order and build the MPQ bytes (pure, no I/O).
        var ordered = members.Values
            .OrderBy(v => v.Canonical, StringComparer.OrdinalIgnoreCase)
            .Select(v => new KeyValuePair<string, byte[]>(v.Canonical, v.Data))
            .ToList();
        byte[] mpqBytes = MpqArchiveWriter.Build(ordered);

        // 4) Reopen and byte-verify every member against what we packed.
        var packaged = ReopenAndVerify(mpqBytes, ordered, tempDir);

        string mpqSha = Sha256(mpqBytes);
        string dbcSha = Sha256(dbcBytes);
        _logger?.LogInformation("WeaponForge: built patch ({Members} members, {Bytes:N0} bytes, sha {Sha})",
            packaged.Count, mpqBytes.Length, mpqSha[..12]);

        return new WeaponPatchResult
        {
            MpqBytes = mpqBytes,
            MpqSha256 = mpqSha,
            DbcBytes = dbcBytes,
            DbcSha256 = dbcSha,
            Members = packaged,
            AllVerified = packaged.All(p => p.Verified),
        };
    }

    private List<PackagedMember> ReopenAndVerify(byte[] mpqBytes, List<KeyValuePair<string, byte[]>> expected, string tempDir)
    {
        Directory.CreateDirectory(tempDir);
        string tempPath = Path.Combine(tempDir, $"verify-{Guid.NewGuid():N}.MPQ");
        try
        {
            File.WriteAllBytes(tempPath, mpqBytes);
            using var archive = MpqArchive.Open(tempPath)
                ?? throw new InvalidOperationException("Reopen of freshly built MPQ failed (no header).");

            var result = new List<PackagedMember>(expected.Count);
            foreach (var (path, data) in expected)
            {
                byte[]? readBack = archive.ReadFile(path);
                bool verified = readBack is not null && readBack.AsSpan().SequenceEqual(data);
                result.Add(new PackagedMember
                {
                    MpqPath = path,
                    Size = data.Length,
                    Sha256 = Sha256(data),
                    Verified = verified,
                });
                if (!verified)
                    _logger?.LogError("WeaponForge: member '{Path}' failed byte verification after repack", path);
            }
            return result;
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* scratch cleanup best-effort */ }
        }
    }

    /// <summary>Canonicalize an MPQ member path: forward slashes → backslashes, trim leading
    /// separators, collapse doubles. Casing is preserved but compared case-insensitively.</summary>
    private static string CanonicalMpqPath(string path)
    {
        var p = path.Replace('/', '\\').Trim().TrimStart('\\');
        while (p.Contains("\\\\")) p = p.Replace("\\\\", "\\");
        return p;
    }

    private static string Sha256(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}

/// <summary>One MPQ member to pack (model M2 or texture BLP).</summary>
public sealed class MpqMember
{
    public required string MpqPath { get; init; }
    public required byte[] Data { get; init; }
}

/// <summary>Everything the patch builder needs: the clean base DBC, the display rows to add, and
/// the model/texture member bytes.</summary>
public sealed class WeaponPatchInput
{
    public required byte[] CleanItemDisplayInfoDbc { get; init; }
    public required IReadOnlyList<WeaponDisplayInfoParams> Displays { get; init; }
    public required IReadOnlyList<MpqMember> Models { get; init; }
    public required IReadOnlyList<MpqMember> Textures { get; init; }
}

public sealed class WeaponPatchResult
{
    public required byte[] MpqBytes { get; init; }
    public required string MpqSha256 { get; init; }
    public required byte[] DbcBytes { get; init; }
    public required string DbcSha256 { get; init; }
    public required IReadOnlyList<PackagedMember> Members { get; init; }
    public required bool AllVerified { get; init; }
}

public sealed class PackagedMember
{
    public required string MpqPath { get; init; }
    public required long Size { get; init; }
    public required string Sha256 { get; init; }
    public required bool Verified { get; init; }
}
