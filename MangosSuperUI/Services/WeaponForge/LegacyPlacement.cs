using System.Numerics;

namespace MangosSuperUI.Services.WeaponForge;

/// <summary>
/// Owner hand-placement for the later-client (TBC / WotLK) import lanes. The fidelity import keeps
/// the source geometry exactly as the later client renders it at the hand (model origin = palm;
/// measured 2026-08-22: WotLK 2H weapons carry zero bone rest transforms and the same grip
/// statistics as TBC — axe2h origin at a median 32% of the length from the pommel in both), so by
/// default nothing moves. When the owner disagrees with a particular model, these controls apply a
/// rigid placement in mesh space (X = long axis toward the tip, Y = WoW up, Z = WoW −Y) to the
/// mesh AND to any carried points (enchant attachment points) so everything stays coherent.
///
/// Deliberately the same vocabulary as the GLB card (<see cref="GlbShapeControls"/>): size %,
/// length %, hand position % (from the back end), up/down + sideways cm, pitch/yaw degrees, flip
/// grip/tip, flip upside down, mirror left/right. Shape reshaping (head/haft/width/depth) is not
/// offered here — these are finished Blizzard meshes.
/// </summary>
public static class LegacyPlacement
{
    /// <summary>Fraction of the length that lies behind the hand (0 = origin at the pommel).</summary>
    public static float GripFraction(RigidWeaponMesh mesh)
    {
        if (mesh.Positions.Length == 0) return 0f;
        float minX = float.MaxValue, maxX = float.MinValue;
        foreach (var p in mesh.Positions) { minX = MathF.Min(minX, p.X); maxX = MathF.Max(maxX, p.X); }
        float ext = MathF.Max(maxX - minX, 1e-6f);
        return Math.Clamp(-minX / ext, -5f, 5f);
    }

    /// <summary>True when the controls request no change at all (grip −1 = keep the source).</summary>
    public static bool IsIdentity(GlbShapeControls? s, bool flipGripEnd)
    {
        if (s is null) return !flipGripEnd;
        return !flipGripEnd && !s.FlipUpsideDown && !s.MirrorSide
            && (s.SizePercent <= 0 || s.SizePercent == 100) && (s.LengthPercent <= 0 || s.LengthPercent == 100)
            && s.GripPercent < 0 && MathF.Abs(s.OffsetUpCm) < 1e-3f && MathF.Abs(s.OffsetSideCm) < 1e-3f
            && MathF.Abs(s.PitchDegrees) < 1e-3f && MathF.Abs(s.YawDegrees) < 1e-3f;
    }

    /// <summary>Apply the placement to a copy of <paramref name="mesh"/> and to <paramref name="points"/>
    /// (mesh space). Returns the new mesh and transformed points; diagnostics describe each step.</summary>
    public static (RigidWeaponMesh Mesh, Vector3[] Points) Apply(RigidWeaponMesh mesh, IReadOnlyList<Vector3> points,
        GlbShapeControls? shape, bool flipGripEnd, ForgeDiagnostics diag)
    {
        var pts = points.ToArray();
        if (IsIdentity(shape, flipGripEnd)) return (mesh, pts);
        shape ??= new GlbShapeControls();

        var pos = (Vector3[])mesh.Positions.Clone();
        var nrm = (Vector3[])mesh.Normals.Clone();
        var idx = (uint[])mesh.Indices.Clone();
        int n = pos.Length;

        float size = shape.SizePercent <= 0 ? 1f : Math.Clamp(shape.SizePercent, 25, 400) / 100f;
        if (MathF.Abs(size - 1f) > 0.001f)
        {
            for (int i = 0; i < n; i++) pos[i] *= size;
            for (int i = 0; i < pts.Length; i++) pts[i] *= size;
            diag.Info("place.size", $"Uniform size ×{size:0.##} about the hand.");
        }
        float length = shape.LengthPercent <= 0 ? 1f : Math.Clamp(shape.LengthPercent, 25, 400) / 100f;
        if (MathF.Abs(length - 1f) > 0.001f)
        {
            for (int i = 0; i < n; i++) { pos[i].X *= length; nrm[i] = CoordinateContract.Normalize(new Vector3(nrm[i].X / length, nrm[i].Y, nrm[i].Z)); }
            for (int i = 0; i < pts.Length; i++) pts[i].X *= length;
            diag.Info("place.length", $"Length ×{length:0.##} along the long axis about the hand.");
        }
        if (flipGripEnd)
        {
            // 180° about the up axis: grip and tip swap ends (a rotation — winding unchanged).
            for (int i = 0; i < n; i++) { pos[i].X = -pos[i].X; pos[i].Z = -pos[i].Z; nrm[i].X = -nrm[i].X; nrm[i].Z = -nrm[i].Z; }
            for (int i = 0; i < pts.Length; i++) { pts[i].X = -pts[i].X; pts[i].Z = -pts[i].Z; }
            diag.Info("place.flipgrip", "Flipped grip/tip (rotated 180° about the hand).");
        }
        if (shape.FlipUpsideDown)
        {
            for (int i = 0; i < n; i++) { pos[i].Y = -pos[i].Y; pos[i].Z = -pos[i].Z; nrm[i].Y = -nrm[i].Y; nrm[i].Z = -nrm[i].Z; }
            for (int i = 0; i < pts.Length; i++) { pts[i].Y = -pts[i].Y; pts[i].Z = -pts[i].Z; }
            diag.Info("place.flipup", "Rolled 180° about the long axis (upside down).");
        }
        if (shape.MirrorSide)
        {
            for (int i = 0; i < n; i++) { pos[i].Z = -pos[i].Z; nrm[i].Z = -nrm[i].Z; }
            for (int i = 0; i < pts.Length; i++) pts[i].Z = -pts[i].Z;
            for (int t = 0; t + 2 < idx.Length; t += 3) (idx[t + 1], idx[t + 2]) = (idx[t + 2], idx[t + 1]);
            diag.Info("place.mirror", "Mirrored left/right across the hand (winding reversed to keep faces outward).");
        }
        if (shape.GripPercent >= 0)
        {
            float g = Math.Clamp(shape.GripPercent, 0, 100) / 100f;
            float minX = float.MaxValue, maxX = float.MinValue;
            foreach (var p in pos) { minX = MathF.Min(minX, p.X); maxX = MathF.Max(maxX, p.X); }
            float ext = MathF.Max(maxX - minX, 1e-6f);
            float current = -minX / ext;
            if (MathF.Abs(g - current) > 0.002f)
            {
                float shift = -g * ext - minX;
                for (int i = 0; i < n; i++) pos[i].X += shift;
                for (int i = 0; i < pts.Length; i++) pts[i].X += shift;
                diag.Info("place.grip", $"Hand placed {g:P0} of the length from the back end (source {current:P0}).");
            }
        }
        float up = Math.Clamp(shape.OffsetUpCm, -200, 200) / 100f, side = Math.Clamp(shape.OffsetSideCm, -200, 200) / 100f;
        if (MathF.Abs(up) > 1e-4f || MathF.Abs(side) > 1e-4f)
        {
            for (int i = 0; i < n; i++) { pos[i].Y += up; pos[i].Z += side; }
            for (int i = 0; i < pts.Length; i++) { pts[i].Y += up; pts[i].Z += side; }
            diag.Info("place.offset", $"Shifted {up:+0.###;-0.###} up and {side:+0.###;-0.###} sideways relative to the hand (WoW units).");
        }
        float pitchDeg = Math.Clamp(shape.PitchDegrees, -90, 90), yawDeg = Math.Clamp(shape.YawDegrees, -90, 90);
        if (MathF.Abs(pitchDeg) > 0.01f || MathF.Abs(yawDeg) > 0.01f)
        {
            var tilt = Matrix4x4.CreateRotationZ(pitchDeg * MathF.PI / 180f) * Matrix4x4.CreateRotationY(yawDeg * MathF.PI / 180f);
            for (int i = 0; i < n; i++) { pos[i] = Vector3.Transform(pos[i], tilt); nrm[i] = CoordinateContract.Normalize(Vector3.TransformNormal(nrm[i], tilt)); }
            for (int i = 0; i < pts.Length; i++) pts[i] = Vector3.Transform(pts[i], tilt);
            diag.Info("place.tilt", $"Tilted about the hand: pitch {pitchDeg:+0.#;-0.#}°, yaw {yawDeg:+0.#;-0.#}°.");
        }

        var placed = new RigidWeaponMesh
        {
            Positions = pos,
            Normals = nrm,
            Uv0 = mesh.Uv0,
            Uv1 = mesh.Uv1,
            Indices = idx,
            VertexIds = mesh.VertexIds,
            Material = mesh.Material,
            TriangleRegionIds = mesh.TriangleRegionIds,
            Normalization = mesh.Normalization,
            SubmeshRanges = mesh.SubmeshRanges,
            Passes = mesh.Passes,
            TextureSlots = mesh.TextureSlots,
        };
        return (placed, pts);
    }
}
