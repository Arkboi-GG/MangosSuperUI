using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Numerics;
using MangosSuperUI.Services.WeaponForge.RawM2;

namespace MangosSuperUI.Services.WeaponForge;

/// <summary>
/// Resolves one stock visual donor per weapon family, data-driven from the installed client
/// archives instead of hardcoded model names that may not exist. For a profile it scans the
/// mounted ItemDisplayInfo for rows whose ModelName1 matches the family's patterns, extracts each
/// candidate M2, and accepts the first one that satisfies the writer's scaffold assumptions
/// (v256 parse, four inline views, exactly one submesh + one batch in view 0, a Type-2 texture
/// slot). The winning donor supplies everything type-specific downstream:
///
///   • the M2 scaffold the variable-topology writer appends onto (bones, attachments, sequences
///     — this is what makes a forged 2H/staff carry proper bone/attachment anatomy);
///   • the DBC row's GroupSoundIndex (axe thunk vs sword shing) and inventory icon;
///   • the fallback BLP when an import carries no texture;
///   • the measured vertex box, from which the target length (X extent) and the palm-back
///     fraction (−minX/extent) are derived — the stock model itself records where Blizzard put
///     the palm, so a staff grips mid-shaft and an axe low on the haft with no tuned constants.
///
/// The golden 1H sword pins display row 679 and reproduces the proven 1.095 extent / 0.188
/// palm-back values from its own bytes. Resolution is cached per type for the app lifetime
/// (stock rows never change under a running app). Fails closed with a listed-patterns error
/// when no stock donor passes validation.
/// </summary>
public sealed class WeaponDonorResolver
{
    private readonly MpqReaderService _mpq;
    private readonly ILogger<WeaponDonorResolver> _logger;
    private readonly ConcurrentDictionary<string, WeaponDonorInfo> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _dbcLock = new();
    private DbcWriterService? _dbc;

    /// <summary>Bound on extract-and-parse attempts per scan pass, so a pattern that matches
    /// hundreds of rows cannot turn resolution into a full-archive sweep.</summary>
    private const int MaxCandidateProbes = 60;

    public WeaponDonorResolver(MpqReaderService mpq, ILogger<WeaponDonorResolver> logger)
    {
        _mpq = mpq;
        _logger = logger;
    }

    public WeaponDonorInfo Resolve(WeaponTypeProfile profile) =>
        _cache.GetOrAdd(profile.Key, _ => ResolveUncached(profile));

    private WeaponDonorInfo ResolveUncached(WeaponTypeProfile profile)
    {
        var dbc = LoadDbc();

        if (profile.PinnedDisplayRow is { } pinned)
        {
            var row = dbc.GetRow(pinned)
                ?? throw new InvalidOperationException($"Pinned donor display row {pinned} for {profile.Label} is missing from the mounted ItemDisplayInfo.");
            return TryCandidate(dbc, row, profile, strict: true)
                ?? throw new InvalidOperationException($"Pinned donor display row {pinned} for {profile.Label} failed structural validation.");
        }

        // Two passes: strict prefers single-texture Type-2 donors with an extractable BLP
        // (the exact golden-sword shape); relaxed accepts any donor whose first texture slot is
        // Type 2, which still means the DBC TextureName1 drives what the batch samples.
        foreach (bool strict in new[] { true, false })
        {
            int probes = 0;
            foreach (var row in MatchingRows(dbc, profile))
            {
                if (++probes > MaxCandidateProbes) break;
                var info = TryCandidate(dbc, row, profile, strict);
                if (info is not null)
                {
                    _logger.LogInformation(
                        "WeaponDonorResolver: {Type} → display row {Row} ({Model}), extent {Extent:0.###}, palm-back {Back:P0}{Relaxed}",
                        profile.Key, info.DisplayRow, info.ModelName, info.ExtentX, info.PalmBackFraction,
                        strict ? "" : " (relaxed texture pass)");
                    return info;
                }
            }
        }

        throw new InvalidOperationException(
            $"No usable stock visual donor found for {profile.Label} " +
            $"(ModelName1 patterns tried: {string.Join(", ", profile.DonorModelPatterns)}).");
    }

    /// <summary>Stock rows (below the custom floor) whose ModelName1 starts with a family pattern
    /// and which carry no second model (fist weapons pair two models — never a valid scaffold),
    /// in ascending row-id order so the choice is deterministic.</summary>
    private static IEnumerable<uint[]> MatchingRows(DbcWriterService dbc, WeaponTypeProfile profile)
    {
        var rows = new List<uint[]>();
        foreach (var row in dbc.GetAllRows())
        {
            if (row.Length < WeaponDisplayInfoRow.FieldCount) continue;
            if (row[WeaponDisplayInfoRow.F_Id] >= WeaponIdReservationService.ItemDisplayFloor) continue;
            var model = dbc.ReadString(row[WeaponDisplayInfoRow.F_ModelName1]);
            if (model.Length == 0) continue;
            if (dbc.ReadString(row[WeaponDisplayInfoRow.F_ModelName2]).Length != 0) continue;

            string stem = ModelStem(model);
            foreach (var pattern in profile.DonorModelPatterns)
                if (stem.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    rows.Add(row);
                    break;
                }
        }
        rows.Sort((a, b) => a[0].CompareTo(b[0]));
        return rows;
    }

    private WeaponDonorInfo? TryCandidate(DbcWriterService dbc, uint[] row, WeaponTypeProfile profile, bool strict)
    {
        uint displayRow = row[WeaponDisplayInfoRow.F_Id];
        string modelName = dbc.ReadString(row[WeaponDisplayInfoRow.F_ModelName1]);
        string stem = ModelStem(modelName);
        if (stem.Length == 0) return null;

        string m2Path = $@"{WeaponNaming.WeaponDir}\{stem}.m2";
        byte[]? m2;
        try { m2 = _mpq.ExtractFile(m2Path); }
        catch { return null; }
        if (m2 is null || m2.Length < 0x100) return null;

        var doc = RawM2Document.Parse(m2, out _);
        if (doc is null) return null;
        if (doc.Views.Count != 4) return null;
        var v0 = doc.Views[0];
        if (v0.Submeshes.Count != 1 || v0.Batches.Count != 1) return null;

        // Texture contract: the batch must sample a Type-2 (DBC-named) slot so TextureName1 drives
        // the pixels. Vanilla v256 header: nTextures at 0x5C, ofsTextures at 0x60, 16-byte records.
        uint nTextures = U32(m2, 0x5C);
        uint ofsTextures = U32(m2, 0x60);
        if (nTextures == 0 || ofsTextures + 16 > m2.Length) return null;
        if (U32(m2, (int)ofsTextures) != 2) return null;      // first slot must be Type 2
        if (strict && nTextures != 1) return null;

        // Vertex box (0xB4 min / 0xC0 max) — the donor's own record of length and palm placement.
        var min = V3(m2, 0x0B4);
        var max = V3(m2, 0x0C0);
        float extent = max.X - min.X;
        if (!float.IsFinite(extent) || extent < 0.15f || extent > 6f) return null;
        float palmBack = Math.Clamp(-min.X / extent, 0f, 0.9f);

        string texStem = dbc.ReadString(row[WeaponDisplayInfoRow.F_TextureName1]);
        string? blpPath = null;
        if (texStem.Length > 0)
        {
            string candidate = $@"{WeaponNaming.WeaponDir}\{texStem}.blp";
            byte[]? blp = null;
            try { blp = _mpq.ExtractFile(candidate); } catch { /* treated as missing */ }
            if (blp is { Length: > 0 }) blpPath = candidate;
        }
        if (strict && blpPath is null) return null;

        return new WeaponDonorInfo
        {
            TypeKey = profile.Key,
            DisplayRow = displayRow,
            ModelName = stem,
            M2Path = m2Path,
            BlpPath = blpPath,
            IconStem = dbc.ReadString(row[WeaponDisplayInfoRow.F_InventoryIcon]),
            GroupSoundIndex = row[WeaponDisplayInfoRow.F_GroupSoundIndex],
            ExtentX = extent,
            PalmBackFraction = palmBack,
        };
    }

    /// <summary>The effective mounted ItemDisplayInfo, read once. Excludes the Forge's own patch-5
    /// so donor scanning never reads Forge output back as input; stock rows are identical either way.</summary>
    private DbcWriterService LoadDbc()
    {
        lock (_dbcLock)
        {
            if (_dbc is not null) return _dbc;
            var bytes = _mpq.ExtractFile(WeaponNaming.ItemDisplayInfoMember,
                    skipArchive: name => name.StartsWith("patch-5", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("Could not extract ItemDisplayInfo.dbc from the mounted archives.");
            _dbc = DbcWriterService.ReadDbc(bytes, WeaponNaming.ItemDisplayInfoMember);
            return _dbc;
        }
    }

    /// <summary>"ITEM\...\Sword_1H_Short_A_01.mdx" → "Sword_1H_Short_A_01".</summary>
    private static string ModelStem(string modelName)
    {
        int slash = modelName.LastIndexOfAny(['\\', '/']);
        string file = slash >= 0 ? modelName[(slash + 1)..] : modelName;
        int dot = file.LastIndexOf('.');
        return dot > 0 ? file[..dot] : file;
    }

    private static uint U32(byte[] b, int o) =>
        o + 4 <= b.Length ? BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o, 4)) : 0;
    private static float F32(byte[] b, int o) =>
        o + 4 <= b.Length ? BinaryPrimitives.ReadSingleLittleEndian(b.AsSpan(o, 4)) : 0f;
    private static Vector3 V3(byte[] b, int o) => new(F32(b, o), F32(b, o + 4), F32(b, o + 8));
}

/// <summary>The resolved stock donor for one weapon family — everything type-specific the
/// importer, writer, and build service consume.</summary>
public sealed record WeaponDonorInfo
{
    public required string TypeKey { get; init; }
    public required uint DisplayRow { get; init; }
    /// <summary>Model stem without directory/extension, e.g. "Sword_1H_Short_A_01".</summary>
    public required string ModelName { get; init; }
    public required string M2Path { get; init; }
    /// <summary>Extractable donor BLP member, when the donor row names one; null on the relaxed pass.</summary>
    public string? BlpPath { get; init; }
    public required string IconStem { get; init; }
    public required uint GroupSoundIndex { get; init; }
    /// <summary>Donor vertex-box X extent (WoW units) — the length imports are scaled to.</summary>
    public required float ExtentX { get; init; }
    /// <summary>−minX/extent of the donor vertex box: how far the weapon reaches behind the palm.
    /// 0.188 for the golden sword; ~mid-shaft for staves.</summary>
    public required float PalmBackFraction { get; init; }
}
