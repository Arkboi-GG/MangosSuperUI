using System.Numerics;
using MangosSuperUI.Services.WeaponForge.RawM2;

namespace MangosSuperUI.Services.WeaponForge;

/// <summary>
/// The Phase-3 donor-scaffold M2 writer (WEAPON_GEN.md §5 Route 0, Phase 3). It serializes a
/// fixed-golden-topology <see cref="RigidWeaponMesh"/> into a valid MD20 v256 by cloning the golden
/// donor scaffold and surgically replacing only the geometry — preserving the donor's sequences,
/// bones, lookups, attachments, events, and four proven view structures byte-for-byte. Because the
/// topology is the donor's (34 verts / 48 tris), nothing changes size and every nested pointer stays
/// valid; the internal name is made canonical by an end-of-file append that moves no existing offset.
///
/// This replaces <see cref="NullWeaponMeshWriter"/> in DI, so the compiler can now emit real custom
/// geometry. Variable topology remains rejected until the Phase-5 four-view generator exists.
/// </summary>
public sealed class DonorScaffoldWriter : IWeaponMeshWriter
{
    /// <summary>Fallback scaffold when the write context names no per-family donor.</summary>
    private const string GoldenDonorM2Path = @"ITEM\ObjectComponents\WEAPON\Sword_1H_Short_A_01.m2";

    private readonly MpqReaderService _mpq;
    private readonly ILogger<DonorScaffoldWriter> _logger;

    /// <summary>When true, the emitted M2's internal name is rewritten to the canonical
    /// <c>SUI_W_####.mdx</c> (appended at EOF, offset-preserving). If the reference client ever
    /// rejects the appended name, set false to keep the donor's proven internal name instead.</summary>
    public bool CanonicalInternalName { get; set; } = true;

    public DonorScaffoldWriter(MpqReaderService mpq, ILogger<DonorScaffoldWriter> logger)
    {
        _mpq = mpq;
        _logger = logger;
    }

    public byte[]? WriteM2(RigidWeaponMesh mesh, WeaponWriteContext ctx, ForgeDiagnostics diag)
    {
        string donorPath = ctx.DonorM2Path ?? GoldenDonorM2Path;
        var donor = _mpq.ExtractFile(donorPath);
        if (donor is null)
        {
            diag.Error("writer.donor.missing", $"Donor scaffold M2 not found in mounted archives: {donorPath}");
            return null;
        }
        diag.Info("writer.donor", $"Scaffold donor: {donorPath}");

        var doc = RawM2Document.Parse(donor, out var perr);
        if (doc is null) { diag.Error("writer.donor.parse", perr ?? "donor parse failed"); return null; }

        int donorVerts = doc.VertexCount;
        bool fixedTopology = mesh.VertexIds is not null && mesh.VertexCount == donorVerts;

        byte[]? outBytes = fixedTopology
            ? WriteFixedTopology(mesh, donor, doc, diag)
            : WriteVariableTopology(mesh, donor, doc, ctx, diag);
        if (outBytes is null) return null;

        // Canonical internal name (offset-preserving EOF append). Reported so the one deliberate
        // difference from the donor is explicit. ctx.CanonicalInternalName=false is the per-build
        // debug lever for isolating the rename in the reference client.
        if (CanonicalInternalName && ctx.CanonicalInternalName)
        {
            outBytes = M2GeometryPatcher.RewriteInternalName(outBytes, WeaponNaming.DbcModelName(ctx.ModelIndex));
            diag.Info("writer.name", $"Internal M2 name set to {WeaponNaming.DbcModelName(ctx.ModelIndex)} (appended at EOF).");
        }
        else
        {
            diag.Info("writer.name", $"Internal M2 name kept as donor '{doc.Name}'.");
        }

        // §7.2 binary validation on the emitted bytes.
        var m2Diag = M2BinaryValidator.Validate(outBytes, expectedVertexCount: mesh.VertexCount, expectedViews: 4);
        diag.AddRange(m2Diag);
        if (m2Diag.HasErrors)
        {
            _logger.LogWarning("DonorScaffoldWriter: emitted M2 failed binary validation ({Errors} errors)", m2Diag.ErrorCount);
            return null;
        }

        _logger.LogInformation("DonorScaffoldWriter: wrote {Bytes} byte M2 ({Verts} verts, {Mode})",
            outBytes.Length, mesh.VertexCount, fixedTopology ? "fixed" : "variable");
        return outBytes;
    }

    /// <summary>Fixed golden topology (34 verts): offset-preserving surgery on the donor scaffold.</summary>
    private byte[]? WriteFixedTopology(RigidWeaponMesh mesh, byte[] donor, RawM2Document doc, ForgeDiagnostics diag)
    {
        int donorVerts = doc.VertexCount;
        var posWoW = new Vector3[donorVerts];
        var nrmWoW = new Vector3[donorVerts];
        var uv0 = new Vector2[donorVerts];
        var filled = new bool[donorVerts];
        for (int k = 0; k < mesh.VertexCount; k++)
        {
            int id = mesh.VertexIds![k];
            if (id < 0 || id >= donorVerts) { diag.Error("writer.id.range", $"VertexId {id} outside 0..{donorVerts - 1}."); return null; }
            if (filled[id]) { diag.Error("writer.id.dup", $"VertexId {id} appears more than once."); return null; }
            filled[id] = true;
            posWoW[id] = CoordinateContract.MeshToWoW(mesh.Positions[k]);
            nrmWoW[id] = CoordinateContract.MeshNormalToWoW(mesh.Normals[k]);
            uv0[id] = mesh.Uv0[k];
        }
        for (int i = 0; i < donorVerts; i++)
            if (!filled[i]) { diag.Error("writer.id.gap", $"No mesh vertex maps to donor slot {i}."); return null; }

        try { return M2GeometryPatcher.Patch(donor, posWoW, nrmWoW, uv0).Bytes; }
        catch (Exception ex) { diag.Error("writer.patch", ex.Message); return null; }
    }

    /// <summary>Variable topology (Phase 5): append a fresh vertex block + four equivalent views,
    /// reusing the donor's preserved tables. Both policies below need reference-client proof.</summary>
    private byte[]? WriteVariableTopology(RigidWeaponMesh mesh, byte[] donor, RawM2Document doc,
        WeaponWriteContext ctx, ForgeDiagnostics diag)
    {
        diag.Warn("writer.variable.views", "Variable topology emits four EQUIVALENT views (not per-LOD); confirm in the reference client.");
        diag.Warn("writer.variable.layout", "Geometry is appended after the donor, leaving the donor's original geometry as dead bytes; confirm in the reference client.");

        var posWoW = new Vector3[mesh.VertexCount];
        var nrmWoW = new Vector3[mesh.VertexCount];
        for (int i = 0; i < mesh.VertexCount; i++)
        {
            posWoW[i] = CoordinateContract.MeshToWoW(mesh.Positions[i]);
            nrmWoW[i] = CoordinateContract.MeshNormalToWoW(mesh.Normals[i]);
        }
        if (mesh.Passes is { Count: > 0 })
            diag.Info("writer.passes",
                $"Multi-pass output: {mesh.SubmeshRanges?.Count ?? 0} submesh(es), {mesh.Passes.Count} pass(es), " +
                $"{ctx.EffectTexturePaths?.Count ?? 0} effect texture(s).");
        else if (mesh.Material.BlendMode != WeaponBlendMode.Opaque || mesh.Material.TwoSided)
            diag.Info("writer.material",
                $"Render flag carries source material: blend={(int)mesh.Material.BlendMode}, twoSided={mesh.Material.TwoSided}.");
        try
        {
            return M2VariableTopologyBuilder.Build(donor, posWoW, nrmWoW, mesh.Uv0, mesh,
                viewCount: 4, material: mesh.Material, effectTexturePaths: ctx.EffectTexturePaths);
        }
        catch (Exception ex) { diag.Error("writer.variable.build", ex.Message); return null; }
    }
}
