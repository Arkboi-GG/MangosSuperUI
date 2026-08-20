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
/// counts rather than silently repairing them. Orientation/scale to the weapon envelope (palm at the
/// origin, long axis +X, per-family donor extent/grip) is heuristic and always reported: ambiguous
/// cases produce diagnostics, not
/// silent guesses. The final mesh still passes the same compiler validation ladder as every route;
/// emitting it as an M2 additionally requires the Phase-5 variable-topology writer.
/// </summary>
public sealed class GlbWeaponImporter
{
    private readonly ILogger<GlbWeaponImporter> _logger;

    /// <summary>Fallback donor X-extent (WoW units) when no family donor is resolved — the golden
    /// 1H sword's measured envelope (WEAPON_GEN.md §2.3: min X ≈ -0.206, max X ≈ 0.889 → ~1.095).
    /// Per-family extents come from <see cref="WeaponDonorResolver"/>.</summary>
    public const float DefaultDonorExtent = 1.095f;

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

        // Merge EVERY triangle primitive into one weapon mesh. Multi-material AI exports split
        // detail pieces (gems, fittings) into their own primitives — choosing "the biggest" used to
        // silently drop them. Winding is fixed per primitive for mirrored node transforms; the
        // base-color texture comes from the largest primitive that carries one.
        candidates = candidates.OrderByDescending(c => c.Prim.GetIndices()?.Count ?? 0).ToList();
        if (candidates.Count > 1)
            diag.Info("glb.multiprim", $"{candidates.Count} triangle primitives merged into one weapon mesh.");

        var bakedList = new List<Vector3>();
        var bakedNrmList = new List<Vector3>();
        var mergedUv = new List<Vector2>();
        var idxAll = new List<uint>();
        MeshPrimitive? texPrim = null;

        foreach (var (p, world) in candidates)
        {
            var posAcc = p.GetVertexAccessor("POSITION");
            var nrmAcc = p.GetVertexAccessor("NORMAL");
            var uvAcc = p.GetVertexAccessor("TEXCOORD_0");
            if (posAcc is null) { diag.Error("glb.nopos", "A primitive has no POSITION."); return Fail(diag, sourceSha); }
            if (nrmAcc is null) { diag.Error("glb.nonormal", "A primitive has no NORMAL; the importer does not fabricate normals."); return Fail(diag, sourceSha); }
            if (uvAcc is null) { diag.Error("glb.nouv", "A primitive has no TEXCOORD_0 (UV0)."); return Fail(diag, sourceSha); }

            var srcPos = posAcc.AsVector3Array();
            var srcNrm = nrmAcc.AsVector3Array();
            var srcUv = uvAcc.AsVector2Array();
            if (srcNrm.Count != srcPos.Count || srcUv.Count != srcPos.Count)
            { diag.Error("glb.attrlen", "POSITION/NORMAL/TEXCOORD_0 length mismatch."); return Fail(diag, sourceSha); }

            // Bake the node world transform. Positions as points; normals via inverse-transpose. A
            // negative-determinant (mirrored) transform flips winding once here (never again for the
            // det-+1 Y↔Z rotation, which the mesh does not undergo — GLB is already Y-up glTF space).
            Matrix4x4.Invert(world, out var inv);
            var normalMatrix = Matrix4x4.Transpose(inv);
            bool flip = CoordinateContract.NodeTransformFlipsWinding(world);

            uint baseIdx = (uint)bakedList.Count;
            for (int i = 0; i < srcPos.Count; i++)
            {
                bakedList.Add(Vector3.Transform(srcPos[i], world));
                bakedNrmList.Add(CoordinateContract.Normalize(Vector3.TransformNormal(srcNrm[i], normalMatrix)));
                mergedUv.Add(srcUv[i]);
            }

            var idxList = p.GetIndices();
            int startTri = idxAll.Count;
            if (idxList is { Count: > 0 })
                foreach (var ix in idxList) idxAll.Add(baseIdx + ix);
            else
                for (uint i = 0; i < srcPos.Count; i++) idxAll.Add(baseIdx + i); // non-indexed: sequential
            if (flip)
                for (int t = startTri; t + 2 < idxAll.Count; t += 3)
                    (idxAll[t + 1], idxAll[t + 2]) = (idxAll[t + 2], idxAll[t + 1]);

            if (texPrim is null && p.Material?.FindChannel("BaseColor")?.Texture?.PrimaryImage is not null)
                texPrim = p;
        }

        int n = bakedList.Count;
        // The UInt16 ceiling is an M2 WRITER constraint, not an import constraint: high-poly source
        // meshes are welcome here and are decimated to game budgets before forging. A hard sanity
        // cap remains so a corrupt file cannot allocate absurd arrays.
        if (n > 2_000_000) { diag.Error("glb.count", $"{n} vertices — not a plausible weapon mesh."); return Fail(diag, sourceSha); }
        if (n > ushort.MaxValue) diag.Warn("glb.count", $"{n} vertices exceeds the M2 UInt16 ceiling — decimation is required before this can forge.");

        var baked = bakedList.ToArray();
        var bakedNrm = bakedNrmList.ToArray();
        var indices = idxAll.ToArray();

        // Orientation + scale to the family's weapon envelope (heuristic, reported).
        var record = new MeshNormalizationRecord();
        Vector3[] finalPos = baked;
        Vector3[] finalNrm = bakedNrm;
        if (options.Reorient)
            (finalPos, finalNrm, record) = WeaponNormalizer.Normalize(baked, bakedNrm,
                options.TargetExtent, options.PalmBackFraction, diag);

        if (options.FlipGripEnd)
        {
            var turn = Matrix4x4.CreateRotationY(MathF.PI);
            for (int i = 0; i < finalPos.Length; i++)
            {
                finalPos[i] = Vector3.Transform(finalPos[i], turn);
                finalNrm[i] = CoordinateContract.Normalize(Vector3.TransformNormal(finalNrm[i], turn));
            }
            float min = finalPos.Min(p => p.X), max = finalPos.Max(p => p.X);
            float back = -options.PalmBackFraction * Math.Max(max - min, 1e-6f);
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

        if (options.BladeProfile > 0.001f)
        {
            ApplyLensProfile(finalPos, Math.Clamp(options.BladeProfile, 0f, 1f));
            RecomputeSmoothNormals(finalPos, indices, finalNrm);
            diag.Info("glb.profile", $"Lens cross-section applied at {options.BladeProfile:P0} — centre depth boosted, tapering to the edges.");
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

        byte[]? texturePng;
        if (texPrim is not null) texturePng = ExtractBaseColorPng(texPrim, diag);
        else { diag.Info("glb.notex", "GLB has no base-color texture; a texture must be supplied separately."); texturePng = null; }

        var mesh = new RigidWeaponMesh
        {
            Positions = finalPos,
            Normals = finalNrm,
            Uv0 = mergedUv.ToArray(),
            Indices = indices,
            VertexIds = null, // variable topology — no stable golden ids
            Material = new WeaponMaterial(),
            Normalization = record,
        };

        // Same validation ladder as every route (variable topology).
        // Import-stage validation: capacity ceilings (u16 verts, 1000-tri budget) are WARNINGS here
        // — the import page decimates to budget before forging, and forge-time validation still
        // hard-rejects. Without this, a high-poly source can never reach the decimator at all.
        var meshDiag = RigidWeaponMeshValidator.Validate(mesh, new MeshValidationOptions
        {
            Topology = WeaponTopologyMode.Variable,
            CapacityAsWarnings = true,
        });
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

    /// <summary>Diamond/lens cross-section: per length-station (X bin), push each vertex's depth (Z)
    /// away from the local depth centre by an amount that peaks at the width centreline and falls to
    /// zero at the edges. Added centre depth at full strength ≈ 35% of the local half-width, so the
    /// thickness follows the blade's own taper toward the tip and never touches the silhouette.</summary>
    private static void ApplyLensProfile(Vector3[] pos, float strength)
    {
        const int Bins = 24;
        float minX = float.MaxValue, maxX = float.MinValue;
        foreach (var p in pos) { minX = MathF.Min(minX, p.X); maxX = MathF.Max(maxX, p.X); }
        float range = MathF.Max(maxX - minX, 1e-6f);

        var minYb = new float[Bins]; var maxYb = new float[Bins];
        var minZb = new float[Bins]; var maxZb = new float[Bins];
        var count = new int[Bins];
        Array.Fill(minYb, float.MaxValue); Array.Fill(maxYb, float.MinValue);
        Array.Fill(minZb, float.MaxValue); Array.Fill(maxZb, float.MinValue);
        int BinOf(float x) => Math.Clamp((int)((x - minX) / range * Bins), 0, Bins - 1);
        foreach (var p in pos)
        {
            int b = BinOf(p.X);
            minYb[b] = MathF.Min(minYb[b], p.Y); maxYb[b] = MathF.Max(maxYb[b], p.Y);
            minZb[b] = MathF.Min(minZb[b], p.Z); maxZb[b] = MathF.Max(maxZb[b], p.Z);
            count[b]++;
        }

        for (int i = 0; i < pos.Length; i++)
        {
            int b = BinOf(pos[i].X);
            if (count[b] == 0) continue;
            float cy = (minYb[b] + maxYb[b]) * 0.5f;
            float hw = MathF.Max((maxYb[b] - minYb[b]) * 0.5f, 1e-5f);
            float cz = (minZb[b] + maxZb[b]) * 0.5f;
            float edge = Math.Clamp(MathF.Abs(pos[i].Y - cy) / hw, 0f, 1f);
            float lens = 1f - edge * edge;                 // smooth peak at the centreline
            float side = pos[i].Z - cz;
            if (MathF.Abs(side) < 1e-5f) continue;         // single-sheet interior — stays in plane
            pos[i].Z += MathF.Sign(side) * strength * 0.35f * hw * lens;
        }
    }

    /// <summary>Area-weighted smooth normals recomputed from the displaced faces (in place).</summary>
    private static void RecomputeSmoothNormals(Vector3[] pos, uint[] indices, Vector3[] nrm)
    {
        var acc = new Vector3[pos.Length];
        for (int t = 0; t + 2 < indices.Length; t += 3)
        {
            var a = pos[indices[t]]; var b = pos[indices[t + 1]]; var c = pos[indices[t + 2]];
            var fn = Vector3.Cross(b - a, c - a);
            acc[indices[t]] += fn; acc[indices[t + 1]] += fn; acc[indices[t + 2]] += fn;
        }
        for (int i = 0; i < nrm.Length; i++)
            if (acc[i].LengthSquared() > 1e-12f) nrm[i] = Vector3.Normalize(acc[i]);
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
    /// <summary>Reorient/scale to the weapon envelope (palm at origin, long axis +X). When false, the
    /// mesh is imported in its authored space and only structurally validated.</summary>
    public bool Reorient { get; init; } = true;

    /// <summary>Target X extent in WoW units — the resolved family donor's measured length.
    /// Defaults to the golden 1H sword.</summary>
    public float TargetExtent { get; init; } = GlbWeaponImporter.DefaultDonorExtent;

    /// <summary>Fraction of the extent placed behind the palm/origin — the resolved family donor's
    /// measured value (golden sword 0.188; staff ~mid-shaft).</summary>
    public float PalmBackFraction { get; init; } = 0.188f;
    public bool StraightenBlade { get; init; }
    public bool FlipGripEnd { get; init; }
    public float DepthScale { get; init; } = 1f;
    public float RollDegrees { get; init; }

    /// <summary>0..1 lens profile: displaces depth (Z) by distance-from-centerline per length
    /// station, so a flat slab gains a diamond/lens cross-section — thickest at the centerline,
    /// tapering to nothing at the edges. Added centre depth at 1.0 ≈ 35% of the local half-width
    /// (so the taper follows the blade's own narrowing toward the tip). 0 = untouched.</summary>
    public float BladeProfile { get; init; }
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
