using System.Numerics;

namespace MangosSuperUI.Services.WeaponForge;

/// <summary>Topology the compiler will accept. Through Phase 4 only the golden donor's fixed
/// 34-vertex / 48-triangle structure is legal, because the writer preserves the donor's four
/// proven view-local structures byte-for-byte and cannot yet synthesize views for new topology.
/// <see cref="Variable"/> is unlocked only once the Phase-5 four-view generator is proven in the
/// reference client (WEAPON_GEN.md §4.1, §7.1).</summary>
public enum WeaponTopologyMode
{
    FixedGolden = 0,
    Variable = 1,
}

/// <summary>Knobs for <see cref="RigidWeaponMeshValidator"/>. Defaults are the strict v1 contract.</summary>
public sealed class MeshValidationOptions
{
    public WeaponTopologyMode Topology { get; init; } = WeaponTopologyMode.FixedGolden;

    // Variable-topology triangle policy (Route A / Phase 5+). Ignored in FixedGolden mode, where
    // the count is pinned to the donor's exact 48. Retuned to the real-vanilla band: measured client
    // weapons run ~100–230 triangles (a plain blade is ~30–50; ornate legendaries like Ashbringer top
    // out at ~226), because the detail is painted into the texture, not modelled. Target ~150–300.
    public int VariableTargetMin { get; init; } = 150;
    public int VariableTargetMax { get; init; } = 300;
    public int VariableHeroWarn { get; init; } = 450;
    public int VariableHardCeiling { get; init; } = 1000;

    /// <summary>Grip must sit within this fraction of the blade-axis extent from the origin.
    /// The donor's pommel reaches ~0.206 into -X against an ~1.095 X-extent (≈19%).</summary>
    public float GripOriginToleranceFraction { get; init; } = 0.30f;

    /// <summary>Allowed slack outside [0,1] for UV0 before it is an error (texels, as a fraction).</summary>
    public float UvEpsilon { get; init; } = 1e-3f;
}

/// <summary>Constants of the golden donor fixture (Sword_1H_Short_A_01.m2, DBC display 679),
/// measured in WEAPON_GEN.md §2.3. The fixed-topology phases validate against these.</summary>
public static class GoldenDonor
{
    public const int VertexCount = 34;
    public const int TriangleCount = 48;
    public const int IndexCount = TriangleCount * 3; // 144
}

/// <summary>Implements the input-mesh validation ladder of WEAPON_GEN.md §7.1. Pure — no runtime
/// data, no side effects — so it is fully unit-testable. Produces structured diagnostics; the
/// caller treats any error as a hard rejection.</summary>
public static class RigidWeaponMeshValidator
{
    public static ForgeDiagnostics Validate(RigidWeaponMesh mesh, MeshValidationOptions? options = null)
    {
        options ??= new MeshValidationOptions();
        var d = new ForgeDiagnostics("input");

        // ── Array parallelism / non-emptiness ───────────────────────────────────────────────
        int vc = mesh.Positions.Length;
        if (vc == 0) { d.Error("mesh.empty", "Mesh has no vertices."); return d; }
        if (mesh.Normals.Length != vc)
            d.Error("mesh.array.mismatch", $"Normals length {mesh.Normals.Length} != vertex count {vc}.");
        if (mesh.Uv0.Length != vc)
            d.Error("mesh.array.mismatch", $"Uv0 length {mesh.Uv0.Length} != vertex count {vc}.");
        if (mesh.VertexIds is { } ids && ids.Length != vc)
            d.Error("mesh.array.mismatch", $"VertexIds length {ids.Length} != vertex count {vc}.");

        int ic = mesh.Indices.Length;
        if (ic == 0) d.Error("mesh.noindices", "Mesh has no triangle indices.");
        if (ic % 3 != 0) d.Error("mesh.index.multiple3", $"Index count {ic} is not a multiple of 3.");
        int tc = ic / 3;
        if (mesh.TriangleRegionIds is { } regs && regs.Length != tc)
            d.Error("mesh.region.mismatch", $"TriangleRegionIds length {regs.Length} != triangle count {tc}.");

        // Bail early if the arrays are structurally inconsistent — the per-element checks below
        // index into them and would throw on a mismatch.
        if (d.HasErrors) return d;

        // ── UInt16-safe global counts (M2 view lookups are uint16) ───────────────────────────
        if (vc > ushort.MaxValue)
            d.Error("mesh.count.u16", $"Vertex count {vc} exceeds the UInt16 ceiling {ushort.MaxValue}.");

        // ── Finite positions / normals / UV0; non-zero normals ───────────────────────────────
        for (int i = 0; i < vc; i++)
        {
            if (!IsFinite(mesh.Positions[i]))
                d.Error("mesh.pos.nonfinite", $"Vertex {i} position is not finite.", mesh.Positions[i].ToString());

            var n = mesh.Normals[i];
            if (!IsFinite(n))
                d.Error("mesh.normal.nonfinite", $"Vertex {i} normal is not finite.");
            else if (n.LengthSquared() < 1e-10f)
                d.Error("mesh.normal.zero", $"Vertex {i} normal is zero-length.");
            else if (MathF.Abs(n.Length() - 1f) > 0.05f)
                d.Warn("mesh.normal.unnormalized", $"Vertex {i} normal is not unit length ({n.Length():0.###}); will be renormalized.");

            var uv = mesh.Uv0[i];
            if (!float.IsFinite(uv.X) || !float.IsFinite(uv.Y))
                d.Error("mesh.uv.nonfinite", $"Vertex {i} UV0 is not finite.");
            else if (uv.X < -options.UvEpsilon || uv.X > 1f + options.UvEpsilon ||
                     uv.Y < -options.UvEpsilon || uv.Y > 1f + options.UvEpsilon)
                d.Error("mesh.uv.range", $"Vertex {i} UV0 {uv} is outside the [0,1] policy.");
        }

        // ── Index range + degenerate triangles ───────────────────────────────────────────────
        for (int t = 0; t < tc; t++)
        {
            uint a = mesh.Indices[t * 3 + 0];
            uint b = mesh.Indices[t * 3 + 1];
            uint c = mesh.Indices[t * 3 + 2];

            if (a >= vc || b >= vc || c >= vc)
            {
                d.Error("mesh.index.range", $"Triangle {t} references out-of-range vertex ({a},{b},{c}); vertex count {vc}.");
                continue;
            }
            if (a == b || b == c || a == c)
            {
                d.Error("mesh.tri.degenerate", $"Triangle {t} has a repeated vertex ({a},{b},{c}).");
                continue;
            }
            // Zero-area (collinear) test in mesh space.
            var e0 = mesh.Positions[b] - mesh.Positions[a];
            var e1 = mesh.Positions[c] - mesh.Positions[a];
            if (Vector3.Cross(e0, e1).LengthSquared() < 1e-14f)
                d.Error("mesh.tri.zeroarea", $"Triangle {t} is degenerate (zero area).");
        }

        // ── Pivot / orientation: grip near origin, blade along +X ────────────────────────────
        ValidateOrientation(mesh, options, d);

        // ── Topology gate + triangle budget ──────────────────────────────────────────────────
        ValidateTopology(mesh, options, d, vc, tc);

        // ── Overlap / island-gutter analysis is intentionally NOT claimed yet ────────────────
        // A real UV-island overlap + gutter check is Phase-4/5 work. Emit Info rather than
        // silently implying the mesh passed a check that did not run (WEAPON_GEN.md §7.1).
        d.Info("mesh.uv.overlap.skipped", "UV island overlap/guttering analysis not yet implemented; not validated.");

        return d;
    }

    private static void ValidateOrientation(RigidWeaponMesh mesh, MeshValidationOptions options, ForgeDiagnostics d)
    {
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (var p in mesh.Positions)
        {
            minX = MathF.Min(minX, p.X); maxX = MathF.Max(maxX, p.X);
            minY = MathF.Min(minY, p.Y); maxY = MathF.Max(maxY, p.Y);
            minZ = MathF.Min(minZ, p.Z); maxZ = MathF.Max(maxZ, p.Z);
        }
        float extX = maxX - minX, extY = maxY - minY, extZ = maxZ - minZ;

        // Blade axis should be the longest and should be X.
        if (extX < extY || extX < extZ)
            d.Warn("mesh.orient.axis", $"Longest axis is not +X (extents X={extX:0.###} Y={extY:0.###} Z={extZ:0.###}); the blade should run +X after normalization.");

        // Grip should sit near the origin along the blade axis (small negative for the pommel is fine).
        float tol = MathF.Max(extX, 1e-4f) * options.GripOriginToleranceFraction;
        if (MathF.Abs(minX) > tol && MathF.Abs(maxX) < extX * 0.5f)
            d.Warn("mesh.orient.grip", $"Grip does not appear to sit near the origin (minX={minX:0.###}, tolerance {tol:0.###}).");
    }

    private static void ValidateTopology(RigidWeaponMesh mesh, MeshValidationOptions options, ForgeDiagnostics d, int vc, int tc)
    {
        if (options.Topology == WeaponTopologyMode.FixedGolden)
        {
            if (vc != GoldenDonor.VertexCount)
                d.Error("mesh.topology.fixed.verts", $"FixedGolden topology requires exactly {GoldenDonor.VertexCount} vertices; got {vc}.");
            if (tc != GoldenDonor.TriangleCount)
                d.Error("mesh.topology.fixed.tris", $"FixedGolden topology requires exactly {GoldenDonor.TriangleCount} triangles; got {tc}.");

            // Stable vertex IDs must be present and be a permutation of 0..33, so an
            // offset-preserving edit provably neither lost, duplicated, nor reordered a vertex.
            if (mesh.VertexIds is null)
                d.Error("mesh.topology.fixed.ids", "FixedGolden topology requires stable VertexIds (the donor's 0..33).");
            else if (vc == GoldenDonor.VertexCount)
            {
                var seen = new bool[GoldenDonor.VertexCount];
                foreach (var id in mesh.VertexIds)
                {
                    if (id < 0 || id >= GoldenDonor.VertexCount)
                    { d.Error("mesh.topology.fixed.idrange", $"VertexId {id} outside the golden range 0..{GoldenDonor.VertexCount - 1}."); continue; }
                    if (seen[id]) d.Error("mesh.topology.fixed.iddup", $"VertexId {id} appears more than once.");
                    seen[id] = true;
                }
            }
            return;
        }

        // Variable topology (Phase 5+): enforce the measured sword budget and UInt16 safety.
        if (tc < options.VariableTargetMin || tc > options.VariableTargetMax)
            d.Warn("mesh.budget.target", $"Triangle count {tc} is outside the v1 target {options.VariableTargetMin}–{options.VariableTargetMax}.");
        if (tc > options.VariableHeroWarn)
            d.Warn("mesh.budget.hero", $"Triangle count {tc} exceeds the hero warning threshold {options.VariableHeroWarn}.");
        if (tc > options.VariableHardCeiling)
            d.Error("mesh.budget.ceiling", $"Triangle count {tc} exceeds the hard ceiling {options.VariableHardCeiling}.");
    }

    private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
