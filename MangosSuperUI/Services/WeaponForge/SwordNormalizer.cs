using System.Numerics;

namespace MangosSuperUI.Services.WeaponForge;

/// <summary>
/// Heuristic orientation + scale of an imported mesh into the sword authoring envelope: the blade
/// runs +X with the grip at the origin, scaled to the donor's blade extent (WEAPON_GEN.md §2.3, §5).
/// It finds the long axis by PCA (power iteration), aligns it to +X, decides which end is the grip by
/// comparing cross-section spread (the tip is the narrow end), and scales to fit. Every decision is
/// reported; genuinely ambiguous inputs (no dominant axis, symmetric ends) raise warnings rather than
/// being silently forced — the caller can then fall back to an explicit owner-set grip axis.
/// </summary>
public static class SwordNormalizer
{
    public static (Vector3[] Positions, Vector3[] Normals, MeshNormalizationRecord Record) Normalize(
        Vector3[] pos, Vector3[] nrm, float donorExtent, ForgeDiagnostics diag)
    {
        int n = pos.Length;
        if (n == 0) return (pos, nrm, MeshNormalizationRecord.Identity);

        // Center on the centroid.
        Vector3 centroid = Vector3.Zero;
        foreach (var p in pos) centroid += p;
        centroid /= n;
        var centered = new Vector3[n];
        for (int i = 0; i < n; i++) centered[i] = pos[i] - centroid;

        // PCA dominant axis = blade axis.
        var axis = DominantAxis(centered);
        var (u, w) = PerpBasis(axis);
        float extAxis = Extent(centered, axis), extU = Extent(centered, u), extW = Extent(centered, w);
        if (extAxis < MathF.Max(extU, extW) * 1.1f)
            diag.Warn("glb.orient.ambiguous", $"Blade axis is not clearly dominant (axis {extAxis:0.###} vs perp {extU:0.###}/{extW:0.###}); orientation is a best guess.");

        // Rotate blade axis → +X.
        var rot = RotationFromTo(axis, Vector3.UnitX);
        var oriented = new Vector3[n];
        for (int i = 0; i < n; i++) oriented[i] = Vector3.Transform(centered[i], rot);

        // Grip vs tip by end cross-section spread; grip is the wider end.
        float minX = float.MaxValue, maxX = float.MinValue;
        foreach (var p in oriented) { minX = MathF.Min(minX, p.X); maxX = MathF.Max(maxX, p.X); }
        float range = MathF.Max(maxX - minX, 1e-6f);
        float lowSpread = EndSpread(oriented, minX, minX + range * 0.15f);
        float highSpread = EndSpread(oriented, maxX - range * 0.15f, maxX);
        if (MathF.Abs(highSpread - lowSpread) < 0.1f * MathF.Max(highSpread, MathF.Max(lowSpread, 1e-6f)))
            diag.Warn("glb.grip.ambiguous", "Grip and tip ends have similar cross-sections; grip choice is a best guess.");

        var total = rot;
        if (highSpread > lowSpread)
        {
            // Grip is at high X — rotate 180° about Y so the grip ends up at low X (det +1, no winding flip).
            total = Matrix4x4.Multiply(rot, Matrix4x4.CreateRotationY(MathF.PI));
            for (int i = 0; i < n; i++) oriented[i] = Vector3.Transform(centered[i], total);
            minX = float.MaxValue; maxX = float.MinValue;
            foreach (var p in oriented) { minX = MathF.Min(minX, p.X); maxX = MathF.Max(maxX, p.X); }
            range = MathF.Max(maxX - minX, 1e-6f);
        }

        // Roll about X so the blade's WIDE cross-axis lies along +Y. Donor-measured convention
        // (InspectWeapon, 2026-08-18): stock swords are wide along WoW Z ⇒ mesh Y; a mesh imported
        // with arbitrary roll otherwise sits rotated 90° in the hand. 2D PCA over (Y,Z) gives the
        // major-axis angle; the result is verified by re-measuring extents (sign-convention proof)
        // and skipped with a warning for round cross-sections where roll is meaningless.
        {
            float cyy = 0, czz = 0, cyz = 0;
            foreach (var p in oriented) { cyy += p.Y * p.Y; czz += p.Z * p.Z; cyz += p.Y * p.Z; }
            float trace = cyy + czz;
            float disc = MathF.Sqrt(MathF.Max(0f, (cyy - czz) * (cyy - czz) + 4f * cyz * cyz));
            float lambdaMajor = (trace + disc) * 0.5f, lambdaMinor = (trace - disc) * 0.5f;
            if (lambdaMinor <= 1e-12f || lambdaMajor / MathF.Max(lambdaMinor, 1e-12f) < 1.3f)
            {
                diag.Warn("glb.roll.ambiguous", "Cross-section is near-round; blade roll left as imported.");
            }
            else
            {
                float phi = 0.5f * MathF.Atan2(2f * cyz, cyy - czz); // major-axis angle from +Y toward +Z
                var rolled = Matrix4x4.Multiply(total, Matrix4x4.CreateRotationX(phi));
                var trial = new Vector3[n];
                for (int i = 0; i < n; i++) trial[i] = Vector3.Transform(centered[i], rolled);
                // Verify: wide must now be along Y. If the sign convention put it on Z, roll 90° more.
                if (Extent(trial, Vector3.UnitZ) > Extent(trial, Vector3.UnitY))
                {
                    rolled = Matrix4x4.Multiply(rolled, Matrix4x4.CreateRotationX(MathF.PI / 2f));
                    for (int i = 0; i < n; i++) trial[i] = Vector3.Transform(centered[i], rolled);
                }
                total = rolled;
                Array.Copy(trial, oriented, n);
                diag.Info("glb.roll", $"Blade rolled {phi * 180f / MathF.PI:0.#}° about X so the wide cross-axis lies on +Y (WoW Z).");
                // X extents are unchanged by a roll about X, so minX/range stay valid.
            }
        }

        float scale = donorExtent / range;

        // Palm-at-origin convention (donor-measured): the client puts the model origin in the palm,
        // and the donor's pommel tip reaches 18.8% of its total length behind that (−0.206 of
        // 1.095). Place the scaled back end there so the hand lands on the grip, not the pommel.
        const float palmBackFraction = 0.188f;
        float backX = -palmBackFraction * donorExtent;

        var outPos = new Vector3[n];
        var outNrm = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            var p = oriented[i];
            outPos[i] = new Vector3((p.X - minX) * scale + backX, p.Y * scale, p.Z * scale);
            outNrm[i] = CoordinateContract.Normalize(Vector3.Transform(nrm[i], total));
        }

        var record = new MeshNormalizationRecord
        {
            Scale = scale,
            Translation = new Vector3(-minX * scale + backX, 0, 0), // post-scale X mapping: x' = (x − minX)·scale + backX
            WindingReversed = false, // the 180° reorient is det +1; source-mirror winding is handled at bake
            Method = "PCA long-axis → +X; grip = wider-cross-section end; roll → wide cross-axis on +Y; scaled to donor extent; palm at origin (back end at −18.8%)",
        };
        return (outPos, outNrm, record);
    }

    private static Vector3 DominantAxis(Vector3[] centered)
    {
        // Symmetric covariance entries.
        float cxx = 0, cyy = 0, czz = 0, cxy = 0, cxz = 0, cyz = 0;
        foreach (var c in centered)
        {
            cxx += c.X * c.X; cyy += c.Y * c.Y; czz += c.Z * c.Z;
            cxy += c.X * c.Y; cxz += c.X * c.Z; cyz += c.Y * c.Z;
        }
        Vector3 Mul(Vector3 v) => new(
            cxx * v.X + cxy * v.Y + cxz * v.Z,
            cxy * v.X + cyy * v.Y + cyz * v.Z,
            cxz * v.X + cyz * v.Y + czz * v.Z);

        // Start from the largest-variance standard axis to avoid a null start direction.
        var v = cxx >= cyy && cxx >= czz ? Vector3.UnitX : (cyy >= czz ? Vector3.UnitY : Vector3.UnitZ);
        for (int it = 0; it < 48; it++)
        {
            var nv = Mul(v);
            float len = nv.Length();
            if (len < 1e-20f) break;
            v = nv / len;
        }
        return CoordinateContract.Normalize(v);
    }

    private static (Vector3 u, Vector3 w) PerpBasis(Vector3 axis)
    {
        var seed = MathF.Abs(axis.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY;
        var u = CoordinateContract.Normalize(Vector3.Cross(axis, seed));
        var w = CoordinateContract.Normalize(Vector3.Cross(axis, u));
        return (u, w);
    }

    private static float Extent(Vector3[] pts, Vector3 axis)
    {
        float min = float.MaxValue, max = float.MinValue;
        foreach (var p in pts) { float d = Vector3.Dot(p, axis); min = MathF.Min(min, d); max = MathF.Max(max, d); }
        return max - min;
    }

    private static float EndSpread(Vector3[] oriented, float xLo, float xHi)
    {
        float sum = 0; int count = 0;
        foreach (var p in oriented)
            if (p.X >= xLo && p.X <= xHi) { sum += MathF.Sqrt(p.Y * p.Y + p.Z * p.Z); count++; }
        return count > 0 ? sum / count : 0f;
    }

    private static Matrix4x4 RotationFromTo(Vector3 a, Vector3 b)
    {
        a = CoordinateContract.Normalize(a);
        b = CoordinateContract.Normalize(b);
        float d = Math.Clamp(Vector3.Dot(a, b), -1f, 1f);
        if (d > 0.99999f) return Matrix4x4.Identity;
        if (d < -0.99999f)
        {
            // Opposite: 180° about any axis perpendicular to a.
            var (perp, _) = PerpBasis(a);
            return Matrix4x4.CreateFromAxisAngle(perp, MathF.PI);
        }
        var axis = CoordinateContract.Normalize(Vector3.Cross(a, b));
        return Matrix4x4.CreateFromAxisAngle(axis, MathF.Acos(d));
    }
}
