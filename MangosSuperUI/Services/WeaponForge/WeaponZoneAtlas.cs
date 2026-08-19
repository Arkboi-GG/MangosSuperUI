using System.Numerics;

namespace MangosSuperUI.Services.WeaponForge;

/// <summary>
/// The manual material-zone atlas (WEAPON_FORGE.md §"Fill sources"): a newly drawn/imported weapon
/// has no meaningful UVs, so the operator carves it into four zones ALONG THE BLADE AXIS — blade,
/// guard, grip, pommel — and each zone is planar-projected into one quadrant of a fixed 2×2 texture
/// atlas. The zones are defined by three normalized split points on the +X axis (t = 0 at the back
/// end, t = 1 at the tip), NOT by a per-triangle selection: the preview GLB welds/reorders vertices
/// so a clicked face cannot round-trip to a server triangle index, but a POSITION-keyed split is
/// reproduced identically on both sides from the same three numbers. Auto-detect proposes the
/// splits from the mesh; the UI drags them.
///
/// Front and back faces of a zone project onto the SAME quadrant (shared, half the texture, correct
/// for a symmetric weapon — WEAPON_FORGE.md). Seams fall exactly on zone boundaries, where the
/// material genuinely changes, so they are invisible rather than ugly.
/// </summary>
public static class WeaponZoneAtlas
{
    /// <summary>Zone order is fixed and indexes the atlas quadrants (Q1..Q4).</summary>
    public static readonly string[] ZoneNames = { "blade", "guard", "grip", "pommel" };

    /// <summary>Atlas quadrant rectangles in UV space (top-left origin, U right, V down). 2×2:
    /// blade=top-left, guard=top-right, grip=bottom-left, pommel=bottom-right. A small gutter is
    /// applied at projection time so DXT/mipmap bleed never crosses a quadrant boundary.</summary>
    public static readonly (float U0, float V0, float U1, float V1)[] Cells =
    {
        (0.0f, 0.0f, 0.5f, 0.5f), // blade
        (0.5f, 0.0f, 1.0f, 0.5f), // guard
        (0.0f, 0.5f, 0.5f, 1.0f), // grip
        (0.5f, 0.5f, 1.0f, 1.0f), // pommel
    };

    private const float Gutter = 0.04f; // fraction of a cell kept clear on every side

    /// <summary>Propose the three splits from the geometry. The guard sits at the palm (mesh x≈0 by
    /// the palm-at-origin convention), so it is centered on the t of x=0 and the other zones fall
    /// out around it; fully overridable by the operator afterwards.</summary>
    public static ZoneBoundaries AutoDetect(RigidWeaponMesh mesh)
    {
        var (minX, maxX) = XRange(mesh);
        float range = MathF.Max(maxX - minX, 1e-6f);
        float tOrigin = Math.Clamp((0f - minX) / range, 0.05f, 0.9f); // where mesh x=0 lands

        float b1 = Math.Clamp(tOrigin + 0.10f, 0.10f, 0.97f); // blade above the guard
        float b2 = Math.Clamp(tOrigin - 0.04f, 0.05f, b1 - 0.02f); // guard band around the origin
        float b3 = Math.Clamp(b2 - 0.14f, 0.02f, b2 - 0.02f); // grip, then pommel below
        return new ZoneBoundaries(b1, b2, b3);
    }

    /// <summary>Zone index (0..3) for one along-axis coordinate t. Descending: blade, guard, grip, pommel.</summary>
    public static int ZoneOf(float t, ZoneBoundaries b) =>
        t >= b.BladeGuard ? 0 : t >= b.GuardGrip ? 1 : t >= b.GripPommel ? 2 : 3;

    /// <summary>Per-vertex zone assignment, parallel to <see cref="RigidWeaponMesh.Positions"/>.</summary>
    public static int[] AssignVertexZones(RigidWeaponMesh mesh, ZoneBoundaries b)
    {
        var (minX, maxX) = XRange(mesh);
        float range = MathF.Max(maxX - minX, 1e-6f);
        var zones = new int[mesh.VertexCount];
        for (int i = 0; i < mesh.VertexCount; i++)
            zones[i] = ZoneOf((mesh.Positions[i].X - minX) / range, b);
        return zones;
    }

    /// <summary>Return a copy of the mesh whose UV0 has been rewritten so each vertex is planar-
    /// projected into its zone's atlas quadrant, and whose per-triangle region ids reflect the zone
    /// split. Positions/normals/indices are untouched — only the texture mapping changes.</summary>
    public static RigidWeaponMesh WithZonedUv(RigidWeaponMesh mesh, ZoneBoundaries b, out int[] triangleZones)
    {
        var (minX, maxX) = XRange(mesh);
        float rangeX = MathF.Max(maxX - minX, 1e-6f);

        // Cross-axis (blade width) runs along mesh Y by the donor-measured convention; project it
        // into the vertical span of each quadrant so a blade fills its cell top-to-bottom.
        float minY = float.MaxValue, maxY = float.MinValue;
        foreach (var p in mesh.Positions) { minY = MathF.Min(minY, p.Y); maxY = MathF.Max(maxY, p.Y); }
        float rangeY = MathF.Max(maxY - minY, 1e-6f);

        float[] zoneStart = { b.BladeGuard, b.GuardGrip, b.GripPommel, 0f };
        float[] zoneEnd = { 1f, b.BladeGuard, b.GuardGrip, b.GripPommel };

        var uv = new Vector2[mesh.VertexCount];
        for (int i = 0; i < mesh.VertexCount; i++)
        {
            var p = mesh.Positions[i];
            float t = (p.X - minX) / rangeX;
            int z = ZoneOf(t, b);
            float span = MathF.Max(zoneEnd[z] - zoneStart[z], 1e-6f);
            float uLocal = Math.Clamp((t - zoneStart[z]) / span, 0f, 1f);
            float vLocal = Math.Clamp((p.Y - minY) / rangeY, 0f, 1f);

            var cell = Cells[z];
            float gw = (cell.U1 - cell.U0) * Gutter, gh = (cell.V1 - cell.V0) * Gutter;
            float u = (cell.U0 + gw) + uLocal * ((cell.U1 - gw) - (cell.U0 + gw));
            float v = (cell.V0 + gh) + vLocal * ((cell.V1 - gh) - (cell.V0 + gh));
            uv[i] = new Vector2(u, v);
        }

        // Per-triangle region id by majority vote of its three vertices (metadata for the manifest).
        var vz = AssignVertexZones(mesh, b);
        int tris = mesh.Indices.Length / 3;
        triangleZones = new int[tris];
        var regions = new string[tris];
        for (int f = 0; f < tris; f++)
        {
            int a = vz[mesh.Indices[f * 3]], bb = vz[mesh.Indices[f * 3 + 1]], c = vz[mesh.Indices[f * 3 + 2]];
            int zone = a == bb || a == c ? a : (bb == c ? bb : a);
            triangleZones[f] = zone;
            regions[f] = ZoneNames[zone];
        }

        return new RigidWeaponMesh
        {
            Positions = mesh.Positions,
            Normals = mesh.Normals,
            Uv0 = uv,
            Indices = mesh.Indices,
            VertexIds = mesh.VertexIds,
            Material = mesh.Material,
            TriangleRegionIds = regions,
            Normalization = mesh.Normalization,
        };
    }

    private static (float Min, float Max) XRange(RigidWeaponMesh mesh)
    {
        float min = float.MaxValue, max = float.MinValue;
        foreach (var p in mesh.Positions) { min = MathF.Min(min, p.X); max = MathF.Max(max, p.X); }
        if (mesh.VertexCount == 0) return (0f, 1f);
        return (min, max);
    }
}

/// <summary>Three normalized split points along the blade axis (t = 0 back end → 1 tip), strictly
/// descending: blade above <see cref="BladeGuard"/>, guard down to <see cref="GuardGrip"/>, grip down
/// to <see cref="GripPommel"/>, pommel below. Constructor clamps + orders so a UI slider can never
/// invert them.</summary>
public sealed class ZoneBoundaries
{
    public float BladeGuard { get; }
    public float GuardGrip { get; }
    public float GripPommel { get; }

    public ZoneBoundaries(float bladeGuard, float guardGrip, float gripPommel)
    {
        // Order + separate so zones never collapse or cross.
        const float eps = 0.01f;
        float b1 = Math.Clamp(bladeGuard, 0f, 1f);
        float b2 = Math.Clamp(guardGrip, 0f, 1f);
        float b3 = Math.Clamp(gripPommel, 0f, 1f);
        if (b2 > b1 - eps) b2 = b1 - eps;
        if (b3 > b2 - eps) b3 = b2 - eps;
        BladeGuard = Math.Clamp(b1, 3 * eps, 1f);
        GuardGrip = Math.Clamp(b2, 2 * eps, BladeGuard - eps);
        GripPommel = Math.Clamp(b3, eps, GuardGrip - eps);
    }
}
