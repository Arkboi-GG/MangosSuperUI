using System.Security.Cryptography;
using MangosSuperUI.Services.ArmorForge;
using MangosSuperUI.Services.Mpq;
using MangosSuperUI.Services.WeaponForge;

namespace MangosSuperUI.Services.UnifiedPatch;

/// <summary>
/// The ONE patch builder for every lane that writes ItemDisplayInfo.dbc — retextures, forged
/// weapons and forged armor — replacing the patch-4 → patch-5 → patch-6 chain with a single archive.
///
/// Why one archive. MPQ resolves WHOLE FILES by rank and never merges rows across archives, so when
/// three patches each shipped their own ItemDisplayInfo.dbc only the topmost copy was ever read.
/// Every lane therefore had to re-union the ones beneath it, and a change anywhere forced a rebuild
/// AND a re-download of the highest patch (change a retexture, re-download patch-6). One archive
/// makes that exactly one rebuild and one download, and the cascade disappears outright because
/// there is nothing above left to shadow it.
///
/// Why lanes contribute rows instead of handing over finished ones. DBC rows carry string OFFSETS
/// into a per-file string block, so a row built against one writer is meaningless in another —
/// concatenating three lanes' pre-built rows would silently scramble every name. Each lane instead
/// writes INTO the single <see cref="DbcWriterService"/> this builder owns.
///
/// Determinism, inherited from the two builders this replaces: rows are added in a fixed lane order
/// (retexture → weapon → armor, the old patch rank order) and by ascending id within a lane; member
/// paths are canonicalized and compared case-insensitively; members are inserted in canonical path
/// order. Unlike those builders a duplicate member path is COLLAPSED rather than fatal — with all
/// lanes in one archive, two items legitimately sharing a bag icon is now expected, not a bug.
/// </summary>
public sealed class UnifiedPatchBuilder
{
    private readonly ILogger<UnifiedPatchBuilder>? _logger;

    public UnifiedPatchBuilder(ILogger<UnifiedPatchBuilder>? logger = null) => _logger = logger;

    public UnifiedPatchResult Build(UnifiedPatchInput input, string tempDir)
    {
        if (input.CleanItemDisplayInfoDbc is null || input.CleanItemDisplayInfoDbc.Length == 0)
            throw new ArgumentException("Clean ItemDisplayInfo.dbc bytes are required.", nameof(input));

        var diag = input.Diagnostics ?? new ForgeDiagnostics("unified-package");

        // 1) One DBC snapshot: the stock base plus every lane's rows, in the old patch rank order.
        var dbc = DbcWriterService.ReadDbc(input.CleanItemDisplayInfoDbc, WeaponNaming.ItemDisplayInfoMember);
        if (dbc.RecordSize != WeaponDisplayInfoRow.RecordSize)
            throw new InvalidOperationException(
                $"Base DBC record size {dbc.RecordSize} != ItemDisplayInfo {WeaponDisplayInfoRow.RecordSize}.");

        int retextureRows = AddRetextureRows(dbc, input.RetextureDisplays, diag);
        int weaponRows = AddWeaponRows(dbc, input.WeaponDisplays, diag);
        int armorRows = AddArmorRows(dbc, input.ArmorDisplays, diag);

        if (diag.HasErrors)
            throw new InvalidOperationException("Unified DBC row validation failed: " +
                string.Join("; ", diag.Items.Where(i => i.Severity == ForgeSeverity.Error).Select(i => i.Message)));

        byte[] dbcBytes = dbc.Write();

        // 2) ItemSet.dbc rides along when forged armor declares tier sets (armor lane only).
        byte[]? setBytes = null;
        if (input.Sets is { Count: > 0 })
        {
            if (input.CleanItemSetDbc is null || input.CleanItemSetDbc.Length == 0)
                throw new InvalidOperationException(
                    "Tier sets were requested but no base ItemSet.dbc was supplied. Cannot ship set bonuses.");
            setBytes = ArmorItemSetDbc.Build(input.CleanItemSetDbc, input.Sets);
        }

        // 3) Members: the authoritative DBC(s) plus every lane's models and textures, collapsed by
        //    canonical path. Identical bytes ARE the same file (the common case: two items imported
        //    from one source ship one icon). Differing bytes keep the first and say so, rather than
        //    letting whichever lane happened to run last win in silence.
        var members = new List<KeyValuePair<string, byte[]>>();
        var byPath = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        void AddMember(string mpqPath, byte[] data, string lane)
        {
            string canonical = CanonicalMpqPath(mpqPath);
            if (byPath.TryGetValue(canonical, out var kept))
            {
                if (!kept.AsSpan().SequenceEqual(data))
                    diag.Warn("package.member.conflict",
                        $"'{canonical}' is shipped with DIFFERENT bytes by more than one lane " +
                        $"({kept.Length:N0} vs {data.Length:N0}, second seen in {lane}); keeping the first. " +
                        "Re-forge the later item with its own texture or icon name if both are meant to differ.");
                return;
            }
            byPath[canonical] = data;
            members.Add(new KeyValuePair<string, byte[]>(canonical, data));
        }

        AddMember(WeaponNaming.ItemDisplayInfoMember, dbcBytes, "dbc");
        if (setBytes != null) AddMember(ArmorNaming.ItemSetMember, setBytes, "dbc");
        foreach (var m in input.RetextureMembers) AddMember(m.MpqPath, m.Data, "retexture");
        foreach (var m in input.WeaponMembers) AddMember(m.MpqPath, m.Data, "weapon");
        foreach (var m in input.ArmorMembers) AddMember(m.MpqPath, m.Data, "armor");

        // 4) Insert in canonical path order and build the archive bytes (pure, no I/O).
        var ordered = members.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).ToList();
        byte[] mpqBytes = MpqArchiveWriter.Build(ordered);

        // 5) Reopen and byte-verify every member against what we packed.
        var packaged = ReopenAndVerify(mpqBytes, ordered, tempDir);

        string mpqSha = Sha256(mpqBytes);
        _logger?.LogInformation(
            "UnifiedPatch: built {Members} members, {Bytes:N0} bytes, sha {Sha} " +
            "(rows: {Retexture} retexture + {Weapon} weapon + {Armor} armor)",
            packaged.Count, mpqBytes.Length, mpqSha[..12], retextureRows, weaponRows, armorRows);

        return new UnifiedPatchResult
        {
            MpqBytes = mpqBytes,
            MpqSha256 = mpqSha,
            DbcBytes = dbcBytes,
            DbcSha256 = Sha256(dbcBytes),
            Members = packaged,
            AllVerified = packaged.All(p => p.Verified),
            ItemSetDbcBytes = setBytes,
            RetextureRows = retextureRows,
            WeaponRows = weaponRows,
            ArmorRows = armorRows,
            SetCount = input.Sets?.Count ?? 0,
            Diagnostics = diag,
        };
    }

    /// <summary>Retextures clone a STOCK row and repoint texture fields on the copy; they never add
    /// a row from scratch. A missing source row is skipped with a diagnostic rather than throwing —
    /// one stale retexture must not take the whole patch, and every other lane, down with it.</summary>
    private static int AddRetextureRows(DbcWriterService dbc, IReadOnlyList<RetextureDisplayEntry> rows,
        ForgeDiagnostics diag)
    {
        int added = 0;
        foreach (var r in rows.OrderBy(r => r.NewDisplayId))
        {
            if (dbc.GetRow(r.NewDisplayId) is not null)
            {
                diag.Warn("retexture.row.exists",
                    $"Display {r.NewDisplayId} already exists in the base DBC — retexture skipped.");
                continue;
            }
            if (dbc.GetRow(r.SourceDisplayId) is null)
            {
                diag.Warn("retexture.row.missing",
                    $"Retexture {r.NewDisplayId} clones display {r.SourceDisplayId}, which is not in the base DBC — skipped.");
                continue;
            }
            dbc.CloneRow(r.SourceDisplayId, r.NewDisplayId);
            foreach (var patch in r.TexturePatches.OrderBy(k => k.Key))
                dbc.PatchRow(r.NewDisplayId, patch.Key, dbc.AddString(patch.Value));
            added++;
        }
        return added;
    }

    private static int AddWeaponRows(DbcWriterService dbc, IReadOnlyList<WeaponDisplayInfoParams> rows,
        ForgeDiagnostics diag)
    {
        int added = 0;
        foreach (var display in rows.OrderBy(d => d.DisplayId))
        {
            if (dbc.GetRow(display.DisplayId) is not null)
                throw new InvalidOperationException(
                    $"Weapon display id {display.DisplayId} already exists in the base DBC; the reservation registry should have prevented this.");
            var row = WeaponDisplayInfoRow.BuildAndAdd(dbc, display);
            WeaponDisplayInfoRow.Validate(dbc, row, diag);
            added++;
        }
        return added;
    }

    private static int AddArmorRows(DbcWriterService dbc, IReadOnlyList<ArmorDisplayEntry> rows,
        ForgeDiagnostics diag)
    {
        int added = 0;
        foreach (var display in rows.OrderBy(d => d.Params.DisplayId))
        {
            if (dbc.GetRow(display.Params.DisplayId) is not null)
                throw new InvalidOperationException(
                    $"Armor display id {display.Params.DisplayId} already exists in the base DBC; the reservation registry should have prevented this.");
            var row = ArmorDisplayInfoRow.BuildAndAdd(dbc, display.Params);
            ArmorDisplayInfoRow.Validate(dbc, row, display.RenderKind, diag);
            added++;
        }
        return added;
    }

    private List<PackagedMember> ReopenAndVerify(byte[] mpqBytes, List<KeyValuePair<string, byte[]>> expected,
        string tempDir)
    {
        Directory.CreateDirectory(tempDir);
        string tempPath = Path.Combine(tempDir, $"verify-{Guid.NewGuid():N}.MPQ");
        try
        {
            File.WriteAllBytes(tempPath, mpqBytes);
            using var archive = MpqArchive.Open(tempPath)
                ?? throw new InvalidOperationException("Reopen of freshly built MPQ failed (no header).");

            var result = new List<PackagedMember>(expected.Count);
            foreach (var kv in expected)
            {
                byte[]? readBack = archive.ReadFile(kv.Key);
                bool verified = readBack is not null && readBack.AsSpan().SequenceEqual(kv.Value);
                result.Add(new PackagedMember
                {
                    MpqPath = kv.Key,
                    Size = kv.Value.Length,
                    Sha256 = Sha256(kv.Value),
                    Verified = verified,
                });
                if (!verified)
                    _logger?.LogError("UnifiedPatch: member '{Path}' failed byte verification after repack", kv.Key);
            }
            return result;
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* scratch cleanup best-effort */ }
        }
    }

    /// <summary>Same canonicalization the per-lane builders use, so "same path" means the same thing
    /// everywhere in the pipeline.</summary>
    internal static string CanonicalMpqPath(string path)
    {
        var p = path.Replace('/', '\\').Trim().TrimStart('\\');
        while (p.Contains("\\\\")) p = p.Replace("\\\\", "\\");
        return p;
    }

    private static string Sha256(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}
