using System.Numerics;
using System.Security.Cryptography;
using SharpGLTF.Schema2;
using SkiaSharp;

namespace MangosSuperUI.Services.WeaponForge;

/// <summary>
/// Imports an arbitrary GLB (a hand-drawn sketch reconstructed by TRELLIS, or any single-mesh weapon
/// model) and normalizes it into a validated <see cref="RigidWeaponMesh"/> (WEAPON_GEN.md §5 Route B,
/// §7.1). This is the convergence point: a sketch and a FLUX concept both arrive here as a GLB.
///
/// It is strict on purpose — it bakes node transforms, selects exactly one triangle primitive, and
/// REJECTS skins, animation, morph targets, non-triangle primitives, missing normals/UV0, and unsafe
/// counts rather than silently repairing them. Orientation/scale to the sword envelope (grip at the
/// origin, blade along +X) is heuristic and always reported: ambiguous cases produce diagnostics, not
/// silent guesses. The final mesh still passes the same compiler validation ladder as every route;
/// emitting it as an M2 additionally requires the Phase-5 variable-topology writer.
/// </summary>
public sealed class GlbWeaponImporter
{
    private readonly ILogger<GlbWeaponImporter> _logger;

    /// <summary>Donor blade X-extent (WoW units) that imports are scaled to fit (WEAPON_GEN.md §2.3
    /// golden donor: min X ≈ -0.206, max X ≈ 0.889 → ~1.095).</summary>
    public const float DonorBladeExtent = 1.095f;

    public GlbWeaponImporter(ILogger<GlbWeaponImporter> logger) => _logger = logger;

    public GlbImportResult Import(byte[] glb, GlbImportOptions? options = null)
    {
        options ??= new GlbImportOptions();
        var diag = new ForgeDiagnostics("glb-import");
        string sourceSha = Convert.ToHexString(SHA256.HashData(glb)).ToLowerInvariant();

        ModelRoot model;
        try { model = ModelRoot.ReadGLB(new MemoryStream(glb)); }
        catch (Exception ex) { diag.Error("glb.parse", $"Not a readable GLB: {ex.Message}"); return Fail(diag, sourceSha); }

        if (model.LogicalAnimations.Count > 0)
            diag.Error("glb.animation", $"GLB contains {model.LogicalAnimations.Count} animation(s); weapons must be static.");

        // Collect triangle primitives across every mesh-bearing node, baking each node's world matrix.
        var candidates = new List<(MeshPrimitive Prim, Matrix4x4 World)>();
        foreach (var node in model.LogicalNodes)
        {
            if (node.Mesh is null) continue;
            if (node.Skin is not null) diag.Error("glb.skin", "GLB is skinned; weapons must be rigid.");
            foreach (var p in node.Mesh.Primitives)
            {
                if (p.DrawPrimitiveType != PrimitiveType.TRIANGLES)
                { diag.Error("glb.primtype", $"Primitive type {p.DrawPrimitiveType} is not TRIANGLES."); continue; }
                if (p.MorphTargetsCount > 0)
                    diag.Error("glb.morph", "Primitive has morph targets; not allowed.");
                candidates.Add((p, node.WorldMatrix));
            }
        }

        if (diag.HasErrors) return Fail(diag, sourceSha);
        if (candidates.Count == 0) { diag.Error("glb.nomesh", "No triangle mesh found."); return Fail(diag, sourceSha); }

        // Choose exactly one primitive; if several, take the largest and say so.
        if (candidates.Count > 1)
            diag.Warn("glb.multiprim", $"{candidates.Count} primitives found; using the one with the most triangles. Merge upstream for a single-material weapon.");
        var (prim, world) = candidates.OrderByDescending(c => c.Prim.GetIndices()?.Count ?? 0).First();

        var posAcc = prim.GetVertexAccessor("POSITION");
        var nrmAcc = prim.GetVertexAccessor("NORMAL");
        var uvAcc = prim.GetVertexAccessor("TEXCOORD_0");
        if (posAcc is null) { diag.Error("glb.nopos", "Primitive has no POSITION."); return Fail(diag, sourceSha); }
        if (nrmAcc is null) { diag.Error("glb.nonormal", "Primitive has no NORMAL; the importer does not fabricate normals."); return Fail(diag, sourceSha); }
        if (uvAcc is null) { diag.Error("glb.nouv", "Primitive has no TEXCOORD_0 (UV0)."); return Fail(diag, sourceSha); }

        var srcPos = posAcc.AsVector3Array();
        var srcNrm = nrmAcc.AsVector3Array();
        var srcUv = uvAcc.AsVector2Array();
        int n = srcPos.Count;
        if (n > ushort.MaxValue) { diag.Error("glb.count", $"{n} vertices exceeds the UInt16 ceiling."); return Fail(diag, sourceSha); }
        if (srcNrm.Count != n || srcUv.Count != n) { diag.Error("glb.attrlen", "POSITION/NORMAL/TEXCOORD_0 length mismatch."); return Fail(diag, sourceSha); }

        // Bake the node world transform. Positions as points; normals via inverse-transpose. A
        // negative-determinant (mirrored) transform flips winding once here (never again for the
        // det-+1 Y↔Z rotation, which the mesh does not undergo — GLB is already Y-up glTF space).
        Matrix4x4.Invert(world, out var inv);
        var normalMatrix = Matrix4x4.Transpose(inv);
        bool flip = CoordinateContract.NodeTransformFlipsWinding(world);

        var baked = new Vector3[n];
        var bakedNrm = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            baked[i] = Vector3.Transform(srcPos[i], world);
            bakedNrm[i] = CoordinateContract.Normalize(Vector3.TransformNormal(srcNrm[i], normalMatrix));
        }

        var idxList = prim.GetIndices();
        var indices = new uint[idxList is { Count: > 0 } ? idxList.Count : n];
        if (idxList is { Count: > 0 })
            for (int i = 0; i < idxList.Count; i++) indices[i] = idxList[i];
        else
            for (uint i = 0; i < n; i++) indices[i] = i; // non-indexed: sequential
        if (flip)
            for (int t = 0; t + 2 < indices.Length; t += 3)
                (indices[t + 1], indices[t + 2]) = (indices[t + 2], indices[t + 1]);

        // Orientation + scale to the sword envelope (heuristic, reported).
        var record = new MeshNormalizationRecord();
        Vector3[] finalPos = baked;
        Vector3[] finalNrm = bakedNrm;
        if (options.Reorient)
            (finalPos, finalNrm, record) = SwordNormalizer.Normalize(baked, bakedNrm, DonorBladeExtent, diag);

        if (options.FlipGripEnd)
        {
            var turn = Matrix4x4.CreateRotationY(MathF.PI);
            for (int i = 0; i < finalPos.Length; i++)
            {
                finalPos[i] = Vector3.Transform(finalPos[i], turn);
                finalNrm[i] = CoordinateContract.Normalize(Vector3.TransformNormal(finalNrm[i], turn));
            }
            float min = finalPos.Min(p => p.X), max = finalPos.Max(p => p.X);
            float back = -.188f * Math.Max(max - min, 1e-6f);
            float shift = back - min;
            for (int i = 0; i < finalPos.Length; i++) finalPos[i].X += shift;
            diag.Info("glb.grip.manual", "Grip/tip choice reversed by the workbench; palm convention reapplied.");
        }

        // User-visible game-mesh preparation. These operations deliberately happen after the
        // provider has produced its textured low-poly mesh and before strict validation/M2 writing.
        // They are bounded, deterministic corrections—not another opaque reconstruction pass.
        if (options.StraightenBlade)
            StraightenBlade(finalPos);
        if (MathF.Abs(options.DepthScale - 1f) > .001f)
        {
            float depth = Math.Clamp(options.DepthScale, .25f, 4f);
            for (int i = 0; i < finalPos.Length; i++)
            {
                finalPos[i].Z *= depth;
                finalNrm[i] = CoordinateContract.Normalize(new Vector3(finalNrm[i].X, finalNrm[i].Y, finalNrm[i].Z / depth));
            }
            diag.Info("glb.depth", $"Depth scaled ×{depth:0.##} after normalization.");
        }
        if (MathF.Abs(options.RollDegrees) > .01f)
        {
            float degrees = Math.Clamp(options.RollDegrees, -180f, 180f);
            var roll = Matrix4x4.CreateRotationX(degrees * MathF.PI / 180f);
            for (int i = 0; i < finalPos.Length; i++)
            {
                finalPos[i] = Vector3.Transform(finalPos[i], roll);
                finalNrm[i] = CoordinateContract.Normalize(Vector3.TransformNormal(finalNrm[i], roll));
            }
            diag.Info("glb.roll.manual", $"Applied an additional {degrees:0.#}° roll about the blade axis.");
        }

        // Decimating to game budgets routinely leaves a few zero-area sliver triangles (collapsed
        // edges). They render as nothing but would hard-fail validation, so sweep them out here —
        // same epsilon the validator uses on the same final positions — and report the count.
        int trisBefore = indices.Length / 3;
        var kept = new List<uint>(indices.Length);
        for (int t = 0; t + 2 < indices.Length; t += 3)
        {
            uint ia = indices[t], ib = indices[t + 1], ic = indices[t + 2];
            if (ia == ib || ib == ic || ia == ic) continue;
            var e0 = finalPos[ib] - finalPos[ia];
            var e1 = finalPos[ic] - finalPos[ia];
            if (Vector3.Cross(e0, e1).LengthSquared() < 1e-14f) continue;
            kept.Add(ia); kept.Add(ib); kept.Add(ic);
        }
        if (kept.Count < indices.Length)
        {
            int dropped = trisBefore - kept.Count / 3;
            indices = kept.ToArray();
            diag.Info("glb.degenerate.dropped",
                $"{dropped} degenerate (zero-area) triangle(s) dropped — decimation slivers, invisible either way.");
        }
        if (indices.Length == 0)
        {
            diag.Error("glb.empty", "Every triangle was degenerate after cleanup — the mesh has no renderable surface.");
            return Fail(diag, sourceSha);
        }

        var texturePng = ExtractBaseColorPng(prim, diag);

        var mesh = new RigidWeaponMesh
        {
            Positions = finalPos,
            Normals = finalNrm,
            Uv0 = srcUv.ToArray(),
            Indices = indices,
            VertexIds = null, // variable topology — no stable golden ids
            Material = new WeaponMaterial(),
            Normalization = record,
        };

        // Same validation ladder as every route (variable topology).
        var meshDiag = RigidWeaponMeshValidator.Validate(mesh, new MeshValidationOptions { Topology = WeaponTopologyMode.Variable });
        diag.AddRange(meshDiag);
        WeaponMeshQualityAnalyzer.AddDiagnostics(WeaponMeshQualityAnalyzer.Analyze(mesh), diag);

        _logger.LogInformation("GlbWeaponImporter: imported {V} verts / {T} tris (sha {Sha})", n, indices.Length / 3, sourceSha[..12]);
        return new GlbImportResult
        {
            Mesh = mesh,
            TexturePng = texturePng,
            Diagnostics = diag,
            SourceSha256 = sourceSha,
            VertexCount = n,
            TriangleCount = indices.Length / 3,
        };
    }

    private byte[]? ExtractBaseColorPng(MeshPrimitive prim, ForgeDiagnostics diag)
    {
        var mat = prim.Material;
        var channel = mat?.FindChannel("BaseColor");
        var image = channel?.Texture?.PrimaryImage;
        if (image is null)
        {
            diag.Info("glb.notex", "GLB has no base-color texture; a texture must be supplied separately.");
            return null;
        }
        var content = image.Content; // MemoryImage
        if (content.Content.Length == 0)
        {
            diag.Info("glb.notex", "GLB base-color image is empty; a texture must be supplied separately.");
            return null;
        }
        // Normalize every provider image (including Tripo/TRELLIS WebP) to PNG before the BLP
        // compiler. Skia is already a cross-platform app dependency, so this removes a provider-
        // specific failure without teaching the WoW texture writer about modern containers.
        try
        {
            using var bitmap = SKBitmap.Decode(content.Content.ToArray());
            if (bitmap is null) throw new InvalidDataException("decoder returned no bitmap");
            using var imageOut = SKImage.FromBitmap(bitmap);
            using var png = imageOut.Encode(SKEncodedImageFormat.Png, 95);
            if (content.IsWebp) diag.Info("glb.webp.converted", "Embedded WebP base color converted to PNG for the BLP compiler.");
            return png.ToArray();
        }
        catch (Exception ex)
        {
            diag.Warn("glb.texture.decode", $"Embedded base-color image could not be decoded ({ex.Message}); donor texture will be used.");
            return null;
        }
    }

    private static void StraightenBlade(Vector3[] positions)
    {
        if (positions.Length == 0) return;
        float minX = positions.Min(p => p.X), maxX = positions.Max(p => p.X);
        float range = Math.Max(maxX - minX, 1e-6f);
        float start = minX + range * .28f;
        const int bins = 24;
        var sums = new Vector2[bins]; var counts = new int[bins];
        foreach (var p in positions)
        {
            if (p.X < start) continue;
            int b = Math.Clamp((int)((p.X - start) / Math.Max(maxX - start, 1e-6f) * bins), 0, bins - 1);
            sums[b] += new Vector2(p.Y, p.Z); counts[b]++;
        }
        var centers = new Vector2[bins];
        for (int i = 0; i < bins; i++) centers[i] = counts[i] == 0 ? Vector2.Zero : sums[i] / counts[i];
        for (int i = 0; i < positions.Length; i++)
        {
            var p = positions[i];
            if (p.X < start) continue;
            float u = Math.Clamp((p.X - start) / Math.Max(maxX - start, 1e-6f) * (bins - 1), 0, bins - 1);
            int lo = (int)MathF.Floor(u), hi = Math.Min(lo + 1, bins - 1);
            var center = Vector2.Lerp(centers[lo], centers[hi], u - lo);
            float blend = Math.Clamp((p.X - start) / (range * .12f), 0f, 1f);
            positions[i] = new Vector3(p.X, p.Y - center.X * blend, p.Z - center.Y * blend);
        }
    }

    private static GlbImportResult Fail(ForgeDiagnostics diag, string sha) =>
        new() { Mesh = null, TexturePng = null, Diagnostics = diag, SourceSha256 = sha, VertexCount = 0, TriangleCount = 0 };
}

public sealed class GlbImportOptions
{
    /// <summary>Reorient/scale to the sword envelope (grip at origin, blade +X). When false, the mesh
    /// is imported in its authored space and only structurally validated.</summary>
    public bool Reorient { get; init; } = true;
    public bool StraightenBlade { get; init; }
    public bool FlipGripEnd { get; init; }
    public float DepthScale { get; init; } = 1f;
    public float RollDegrees { get; init; }
}

public sealed class GlbImportResult
{
    public required RigidWeaponMesh? Mesh { get; init; }
    public required byte[]? TexturePng { get; init; }
    public required ForgeDiagnostics Diagnostics { get; init; }
    public required string SourceSha256 { get; init; }
    public required int VertexCount { get; init; }
    public required int TriangleCount { get; init; }
    public bool Ok => Mesh is not null && !Diagnostics.HasErrors;
}
