using System.Numerics;

namespace MangosSuperUI.Services.WeaponForge;

/// <summary>
/// Deterministic parametric sword generator (WEAPON_GEN.md §5 Route A). Builds a
/// <see cref="RigidWeaponMesh"/> by construction — a tapered lens-section blade, a box crossguard, a
/// prism grip, and a pommel — in the canonical authoring space (Y-up, +X = grip→tip, PALM at the
/// origin: the grip spans x=0 and the guard base lands at the donor-measured ≈+0.10). Geometry is
/// flat-shaded (each triangle owns its 3 vertices), so normals and the fixed UV
/// atlas are trivial and correct by construction. "Valid by construction" only means the mesh
/// satisfies the RigidWeaponMesh contract; the compiled M2 still runs the full validation ladder.
///
/// Every triangle carries a semantic region id (blade/edge/guard/grip/pommel) so the texture stage
/// can paint per region, and UVs are packed into a fixed per-region atlas band.
/// </summary>
public static class ParametricSwordGenerator
{
    // Fixed UV atlas bands (U ranges; V spans 0..1). Blade gets the most room.
    private static readonly Dictionary<string, (float U0, float U1)> Atlas = new()
    {
        ["blade"] = (0.00f, 0.55f),
        ["guard"] = (0.55f, 0.70f),
        ["grip"] = (0.70f, 0.88f),
        ["pommel"] = (0.88f, 1.00f),
    };

    public static RigidWeaponMesh Generate(SwordParams p)
    {
        var b = new Builder();

        float guardHalfDepth = p.GuardDepth * 0.5f;
        BuildBlade(b, p, guardHalfDepth);
        BuildBox(b, "guard",
            center: new Vector3(0, 0, 0),
            half: new Vector3(guardHalfDepth, p.GuardWidth * 0.5f, p.GuardThickness * 0.5f));
        BuildGrip(b, p, guardHalfDepth);
        float gripEndX = -guardHalfDepth - p.GripLength;
        BuildBox(b, "pommel",
            center: new Vector3(gripEndX - p.PommelSize * 0.4f, 0, 0),
            half: new Vector3(p.PommelSize * 0.5f, p.PommelSize * 0.5f, p.PommelSize * 0.5f));

        var mesh = b.ToMesh();

        // Palm-at-origin convention (donor-measured, first-render proof 2026-08-18): the client
        // places the model ORIGIN in the character's palm. The donor's guard base sits at ≈+0.10
        // with the grip spanning the origin (pommel back at −0.206). Built with the guard centered
        // at 0, the fist closed around the guard — so shift +X until ~70% of the grip sits above
        // the origin and the guard base lands at the donor's position.
        float palmShift = guardHalfDepth + p.GripLength * 0.7f;
        for (int i = 0; i < mesh.Positions.Length; i++)
            mesh.Positions[i] = new Vector3(mesh.Positions[i].X + palmShift, mesh.Positions[i].Y, mesh.Positions[i].Z);

        return mesh;
    }

    private static void BuildBlade(Builder b, SwordParams p, float guardHalfDepth)
    {
        int k = p.BladeSides;
        float x0 = guardHalfDepth;
        float x1 = x0 + p.BladeLength;

        // Cross-section orientation is MEASURED, not guessed: the donor sword is WIDE along WoW Z
        // and THIN along WoW Y (InspectWeapon, 2026-08-18), which maps to mesh space as width along
        // Y and thickness along Z — the blade's flat faces the viewer. The first in-client render
        // had these swapped and the sword sat rolled 90° in the hand.
        Vector3 Ring(float x, int i, float scale)
        {
            float a = MathF.Tau * i / k;
            return new Vector3(x, p.BladeWidth * 0.5f * scale * MathF.Sin(a), p.BladeThickness * 0.5f * scale * MathF.Cos(a));
        }
        float WidthScale(float f) => MathF.Max(0.06f, 1f - f * 0.55f); // taper toward the tip

        var band = Atlas["blade"];
        for (int seg = 0; seg < p.BladeSegments; seg++)
        {
            float f0 = seg / (float)p.BladeSegments, f1 = (seg + 1) / (float)p.BladeSegments;
            float xa = Lerp(x0, x1, f0), xb = Lerp(x0, x1, f1);
            float wa = WidthScale(f0), wb = WidthScale(f1);
            float ua = band.U0 + f0 * (band.U1 - band.U0), ub = band.U0 + f1 * (band.U1 - band.U0);
            for (int i = 0; i < k; i++)
            {
                int j = (i + 1) % k;
                float v0 = i / (float)k, v1 = (i + 1) / (float)k;
                var p00 = Ring(xa, i, wa); var p01 = Ring(xa, j, wa);
                var p10 = Ring(xb, i, wb); var p11 = Ring(xb, j, wb);
                b.AddQuad("blade", p00, p10, p11, p01,
                    new Vector2(ua, v0), new Vector2(ub, v0), new Vector2(ub, v1), new Vector2(ua, v1));
            }
        }
        // Tip fan to a point.
        var apex = new Vector3(x1 + p.BladeLength * 0.06f, 0, 0);
        float wTip = WidthScale(1f);
        for (int i = 0; i < k; i++)
        {
            int j = (i + 1) % k;
            b.AddTri("blade", Ring(x1, i, wTip), apex, Ring(x1, j, wTip),
                new Vector2(band.U1, i / (float)k), new Vector2(band.U1, 0.5f), new Vector2(band.U1, (i + 1) / (float)k));
        }
    }

    private static void BuildGrip(Builder b, SwordParams p, float guardHalfDepth)
    {
        int k = p.GripSides;
        float x0 = -guardHalfDepth;
        float x1 = -guardHalfDepth - p.GripLength;
        var band = Atlas["grip"];

        Vector3 Ring(float x, int i)
        {
            float a = MathF.Tau * i / k;
            return new Vector3(x, p.GripRadius * MathF.Sin(a), p.GripRadius * MathF.Cos(a));
        }
        for (int i = 0; i < k; i++)
        {
            int j = (i + 1) % k;
            float v0 = i / (float)k, v1 = (i + 1) / (float)k;
            b.AddQuad("grip", Ring(x0, i), Ring(x1, i), Ring(x1, j), Ring(x0, j),
                new Vector2(band.U0, v0), new Vector2(band.U1, v0), new Vector2(band.U1, v1), new Vector2(band.U0, v1));
        }
    }

    private static void BuildBox(Builder b, string region, Vector3 center, Vector3 half)
    {
        var band = Atlas[region];
        // 8 corners.
        Vector3 C(int sx, int sy, int sz) => center + new Vector3(sx * half.X, sy * half.Y, sz * half.Z);
        var v000 = C(-1, -1, -1); var v001 = C(-1, -1, 1); var v010 = C(-1, 1, -1); var v011 = C(-1, 1, 1);
        var v100 = C(1, -1, -1); var v101 = C(1, -1, 1); var v110 = C(1, 1, -1); var v111 = C(1, 1, 1);
        Vector2 U(float u, float v) => new(Lerp(band.U0, band.U1, u), v);
        // 6 faces (CCW outward).
        b.AddQuad(region, v100, v101, v111, v110, U(0, 0), U(1, 0), U(1, 1), U(0, 1)); // +X
        b.AddQuad(region, v001, v000, v010, v011, U(0, 0), U(1, 0), U(1, 1), U(0, 1)); // -X
        b.AddQuad(region, v010, v110, v111, v011, U(0, 0), U(1, 0), U(1, 1), U(0, 1)); // +Y
        b.AddQuad(region, v000, v001, v101, v100, U(0, 0), U(1, 0), U(1, 1), U(0, 1)); // -Y
        b.AddQuad(region, v001, v011, v111, v101, U(0, 0), U(1, 0), U(1, 1), U(0, 1)); // +Z
        b.AddQuad(region, v000, v100, v110, v010, U(0, 0), U(1, 0), U(1, 1), U(0, 1)); // -Z
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    /// <summary>Accumulates flat-shaded triangles (fresh verts per tri) with region ids + UVs.</summary>
    private sealed class Builder
    {
        private readonly List<Vector3> _pos = new();
        private readonly List<Vector3> _nrm = new();
        private readonly List<Vector2> _uv = new();
        private readonly List<uint> _idx = new();
        private readonly List<string> _regions = new();

        public void AddTri(string region, Vector3 a, Vector3 b, Vector3 c, Vector2 ua, Vector2 ub, Vector2 uc)
        {
            var normal = Vector3.Cross(b - a, c - a);
            if (normal.LengthSquared() < 1e-16f) return; // skip degenerate
            normal = Vector3.Normalize(normal);
            uint baseIdx = (uint)_pos.Count;
            _pos.Add(a); _pos.Add(b); _pos.Add(c);
            _nrm.Add(normal); _nrm.Add(normal); _nrm.Add(normal);
            _uv.Add(Clamp01(ua)); _uv.Add(Clamp01(ub)); _uv.Add(Clamp01(uc));
            _idx.Add(baseIdx); _idx.Add(baseIdx + 1); _idx.Add(baseIdx + 2);
            _regions.Add(region);
        }

        public void AddQuad(string region, Vector3 a, Vector3 b, Vector3 c, Vector3 d,
            Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            AddTri(region, a, b, c, ua, ub, uc);
            AddTri(region, a, c, d, ua, uc, ud);
        }

        public RigidWeaponMesh ToMesh() => new()
        {
            Positions = _pos.ToArray(),
            Normals = _nrm.ToArray(),
            Uv0 = _uv.ToArray(),
            Indices = _idx.ToArray(),
            VertexIds = null,
            Material = new WeaponMaterial(),
            TriangleRegionIds = _regions.ToArray(),
        };

        private static Vector2 Clamp01(Vector2 v) => new(Math.Clamp(v.X, 0f, 1f), Math.Clamp(v.Y, 0f, 1f));
    }
}

/// <summary>Parameter schema for a generated sword. Defaults land inside the measured vanilla sword
/// envelope (grip at origin, blade along +X, ~200-400 triangles).</summary>
public sealed class SwordParams
{
    public float BladeLength { get; init; } = 0.75f;
    public float BladeWidth { get; init; } = 0.09f;   // broadside, along mesh Y (→ WoW Z, donor-measured)
    public float BladeThickness { get; init; } = 0.02f; // flat-to-flat, along mesh Z (→ WoW Y)
    public int BladeSegments { get; init; } = 10;
    public int BladeSides { get; init; } = 8;

    public float GuardWidth { get; init; } = 0.17f;   // along mesh Y (→ WoW Z)
    public float GuardThickness { get; init; } = 0.03f; // along mesh Z (→ WoW Y)
    public float GuardDepth { get; init; } = 0.035f;  // along X

    public float GripLength { get; init; } = 0.14f;
    public float GripRadius { get; init; } = 0.018f;
    public int GripSides { get; init; } = 8;

    public float PommelSize { get; init; } = 0.04f;
    public int Seed { get; init; } = 0;
}
