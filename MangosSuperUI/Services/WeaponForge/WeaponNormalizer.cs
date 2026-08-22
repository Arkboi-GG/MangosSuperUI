using System.Numerics;

namespace MangosSuperUI.Services.WeaponForge;

/// <summary>
/// Cross-section placement facts measured from a weapon family's stock donor (WoW model space:
/// X = long axis toward the tip, origin in the hand, Z up). The normalizer uses them to land an
/// imported mesh the way the stock model sits in the hand:
///   • <see cref="WideAxisIsZ"/> — every melee donor is wide along WoW Z (blade flat faces the
///     viewer: the proven "wide on mesh +Y" convention); crossbows are the exception, their prod
///     spans WoW Y;
///   • <see cref="TipSkewY"/>/<see cref="TipSkewZ"/> — where the far (tip) end of the weapon sits
///     across the cross-section relative to the whole model's box centre, as a fraction of that
///     axis' extent. Silhouette-based (independent of vertex density), so it is comparable between
///     a hand-modelled stock M2 and a uniformly sampled reconstruction: a rifle's muzzle rides
///     above the box centre because the stock hangs below; a bow's limb tips sit on the string
///     side; a sword's tip is centred (no decision);
///   • <see cref="BoxCenterY"/>/<see cref="BoxCenterZ"/> — where the donor's vertex box centre
///     sits relative to the hand, so a rifle's barrel is raised and a bow's grip stays at the
///     back of the limbs exactly as on the stock model.
/// All fields are measured by <see cref="Measure"/> from vertex positions; nothing is tuned by hand.
/// </summary>
public sealed record WeaponOrientationHints
{
    /// <summary>Fraction of the long-axis extent treated as "the tip end" when measuring skew.</summary>
    public const float TipWindow = 0.15f;
    /// <summary>|skew| at or above this is a decision; below it the side is ambiguous.</summary>
    public const float DecisiveSkew = 0.08f;

    /// <summary>Above this the tip end is decisively the fatter end (axe/mace heads, thrown axes,
    /// crossbow prods, polearm blades); below its reciprocal the grip end is (sword guards/pommels,
    /// rifle stocks, daggers); between them the ends are alike (bows, plain staves).</summary>
    public const float DecisiveSpreadRatio = 1.15f;

    public required bool WideAxisIsZ { get; init; }
    public required float ExtentY { get; init; }
    public required float ExtentZ { get; init; }
    public required float BoxCenterY { get; init; }
    public required float BoxCenterZ { get; init; }
    public required float TipSkewY { get; init; }
    public required float TipSkewZ { get; init; }
    /// <summary>Same measure for the GRIP end window: a rifle's stock and a crossbow's butt hang
    /// below the box centre at the grip end even when the muzzle/prod is nearly centred.</summary>
    public required float GripSkewY { get; init; }
    public required float GripSkewZ { get; init; }

    /// <summary>Cross-section spread of the tip end window over that of the grip end window
    /// (each measured about its own centre, so it is independent of where the long axis runs).
    /// Tells the normalizer which end of an import is the grip for THIS family instead of assuming
    /// "the grip is the fatter end" (true for swords, wrong for axes, thrown axes and crossbows).</summary>
    public required float TipSpreadRatio { get; init; }

    /// <summary>True/false when the donor decisively says its tip (true) or grip (false) end is the
    /// fatter one; null when both ends are alike.</summary>
    public bool? TipIsFatter =>
        TipSpreadRatio >= DecisiveSpreadRatio ? true
        : TipSpreadRatio <= 1f / DecisiveSpreadRatio ? false
        : null;

    /// <summary>Mean distance of the vertices in the X window [xLo, xHi] from that window's own
    /// (Y,Z) centre — a translation-invariant "how fat is this end" measure.</summary>
    public static float EndSpreadLocal(IReadOnlyList<Vector3> pts, float xLo, float xHi)
    {
        float cy = 0, cz = 0; int count = 0;
        foreach (var p in pts)
            if (p.X >= xLo && p.X <= xHi) { cy += p.Y; cz += p.Z; count++; }
        if (count == 0) return 0f;
        cy /= count; cz /= count;
        float sum = 0;
        foreach (var p in pts)
            if (p.X >= xLo && p.X <= xHi)
                sum += MathF.Sqrt((p.Y - cy) * (p.Y - cy) + (p.Z - cz) * (p.Z - cz));
        return sum / count;
    }

    /// <summary>Measure from positions whose long axis is +X with the grip at low X (WoW donor
    /// space, or the normalizer's oriented mesh space — the caller keeps the spaces straight).</summary>
    public static WeaponOrientationHints Measure(IReadOnlyList<Vector3> p)
    {
        if (p.Count == 0)
            return new WeaponOrientationHints
            {
                WideAxisIsZ = true, ExtentY = 0, ExtentZ = 0,
                BoxCenterY = 0, BoxCenterZ = 0, TipSkewY = 0, TipSkewZ = 0,
                GripSkewY = 0, GripSkewZ = 0, TipSpreadRatio = 1f,
            };

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var v in p) { min = Vector3.Min(min, v); max = Vector3.Max(max, v); }
        var ext = max - min;
        var center = (min + max) * 0.5f;

        float window = MathF.Max(ext.X, 1e-6f) * TipWindow;
        var (skewY, skewZ) = WindowSkew(p, max.X - window, max.X, center, ext);
        var (gripSkewY, gripSkewZ) = WindowSkew(p, min.X, min.X + window, center, ext);
        float gripSpread = EndSpreadLocal(p, min.X, min.X + window);
        float tipSpread = EndSpreadLocal(p, max.X - window, max.X);
        float ratio = gripSpread > 1e-6f ? tipSpread / gripSpread : (tipSpread > 1e-6f ? 99f : 1f);

        return new WeaponOrientationHints
        {
            WideAxisIsZ = ext.Z >= ext.Y,
            ExtentY = ext.Y,
            ExtentZ = ext.Z,
            BoxCenterY = center.Y,
            BoxCenterZ = center.Z,
            TipSkewY = skewY,
            TipSkewZ = skewZ,
            GripSkewY = gripSkewY,
            GripSkewZ = gripSkewZ,
            TipSpreadRatio = ratio,
        };
    }

    /// <summary>Centre of the vertices in the X window [xLo, xHi] relative to the whole box centre,
    /// as a fraction of the cross extents. (0,0) when the window is empty.</summary>
    private static (float SkewY, float SkewZ) WindowSkew(IReadOnlyList<Vector3> p, float xLo, float xHi, Vector3 center, Vector3 ext)
    {
        float tMinY = float.MaxValue, tMaxY = float.MinValue, tMinZ = float.MaxValue, tMaxZ = float.MinValue;
        int hits = 0;
        foreach (var v in p)
        {
            if (v.X < xLo || v.X > xHi) continue;
            hits++;
            tMinY = MathF.Min(tMinY, v.Y); tMaxY = MathF.Max(tMaxY, v.Y);
            tMinZ = MathF.Min(tMinZ, v.Z); tMaxZ = MathF.Max(tMaxZ, v.Z);
        }
        if (hits == 0) return (0f, 0f);
        float skewY = ext.Y > 1e-6f ? ((tMinY + tMaxY) * 0.5f - center.Y) / ext.Y : 0f;
        float skewZ = ext.Z > 1e-6f ? ((tMinZ + tMaxZ) * 0.5f - center.Z) / ext.Z : 0f;
        return (skewY, skewZ);
    }

    public override string ToString() =>
        $"wide={(WideAxisIsZ ? "Z" : "Y")} extent=({ExtentY:0.###},{ExtentZ:0.###}) center=({BoxCenterY:0.###},{BoxCenterZ:0.###}) tipSkew=({TipSkewY:+0.###;-0.###},{TipSkewZ:+0.###;-0.###}) gripSkew=({GripSkewY:+0.###;-0.###},{GripSkewZ:+0.###;-0.###}) tipSpreadRatio={TipSpreadRatio:0.##}";
}

/// <summary>
/// Heuristic orientation + scale of an imported mesh into the weapon authoring envelope: the long
/// axis runs +X with the palm at the origin, scaled to the resolved donor's extent (WEAPON_GEN.md
/// §2.3, §5). It finds the long axis by PCA (power iteration), aligns it to +X, decides which end
/// is the grip by comparing cross-section spread (the tip is the narrow end), and scales to fit.
/// The palm-back fraction — how far the weapon reaches behind the origin — comes from the weapon
/// family's stock donor (0.188 for the golden sword, ~mid-shaft for a staff, mid-limb for a bow),
/// so each type lands in the hand where its vanilla counterparts do. When the donor's
/// <see cref="WeaponOrientationHints"/> are supplied, the roll also follows the donor's wide axis
/// (crossbow prods span WoW Y), the tip-side skew settles which way an asymmetric weapon hangs
/// (stock below a rifle, string side of a bow), and the cross-section is positioned like the
/// donor's. Every decision is reported; genuinely ambiguous inputs (no dominant axis, symmetric
/// ends) raise warnings rather than being silently forced — the caller can then fall back to an
/// explicit owner-set grip axis or roll.
/// </summary>
public static class WeaponNormalizer
{
    public static (Vector3[] Positions, Vector3[] Normals, MeshNormalizationRecord Record) Normalize(
        Vector3[] pos, Vector3[] nrm, float donorExtent, float palmBackFraction, ForgeDiagnostics diag,
        WeaponOrientationHints? hints = null)
    {
        int n = pos.Length;
        if (n == 0) return (pos, nrm, MeshNormalizationRecord.Identity);

        // Center on the centroid.
        Vector3 centroid = Vector3.Zero;
        foreach (var p in pos) centroid += p;
        centroid /= n;
        var centered = new Vector3[n];
        for (int i = 0; i < n; i++) centered[i] = pos[i] - centroid;

        // Principal axes (PCA); the blade/long axis is the principal direction with the largest
        // EXTENT — not the largest variance — so a crossbow's densely sampled prod or a rifle's
        // stock cannot out-vote the rail/barrel the way vertex density biases variance. Identical
        // to the variance choice for every blade-like import.
        var (a1, a2, a3) = PrincipalAxes(centered);
        float e1 = Extent(centered, a1), e2 = Extent(centered, a2), e3 = Extent(centered, a3);
        Vector3 axis; float extAxis, extU, extW;
        if (e1 >= e2 && e1 >= e3) { axis = a1; extAxis = e1; extU = e2; extW = e3; }
        else if (e2 >= e3) { axis = a2; extAxis = e2; extU = e1; extW = e3; }
        else { axis = a3; extAxis = e3; extU = e1; extW = e2; }
        if (extAxis < MathF.Max(extU, extW) * 1.1f)
            diag.Warn("glb.orient.ambiguous", $"Blade axis is not clearly dominant (axis {extAxis:0.###} vs perp {extU:0.###}/{extW:0.###}); orientation is a best guess.");

        // Rotate blade axis → +X.
        var rot = RotationFromTo(axis, Vector3.UnitX);
        var oriented = new Vector3[n];
        for (int i = 0; i < n; i++) oriented[i] = Vector3.Transform(centered[i], rot);

        // Grip vs tip by end cross-section spread. Legacy (no donor hints): the grip is the wider
        // end — true for swords (guard/pommel vs blade tip). With the family donor's hints the
        // donor says which of ITS ends is the fatter one, so an axe/mace/thrown head or a crossbow
        // prod lands at the tip and a rifle stock at the grip, instead of every fat end being
        // mistaken for a hilt.
        float minX = float.MaxValue, maxX = float.MinValue;
        foreach (var p in oriented) { minX = MathF.Min(minX, p.X); maxX = MathF.Max(maxX, p.X); }
        float range = MathF.Max(maxX - minX, 1e-6f);
        bool flipEnds;
        if (hints?.TipIsFatter is bool tipIsFatter)
        {
            float lowLocal = WeaponOrientationHints.EndSpreadLocal(oriented, minX, minX + range * WeaponOrientationHints.TipWindow);
            float highLocal = WeaponOrientationHints.EndSpreadLocal(oriented, maxX - range * WeaponOrientationHints.TipWindow, maxX);
            float importRatio = lowLocal > 1e-6f ? highLocal / lowLocal : (highLocal > 1e-6f ? 99f : 1f);
            bool importDecisive = importRatio >= WeaponOrientationHints.DecisiveSpreadRatio ||
                                  importRatio <= 1f / WeaponOrientationHints.DecisiveSpreadRatio;
            if (importDecisive)
            {
                bool importHighIsFatter = importRatio > 1f;
                // Donor tip fatter ⇒ the fat end belongs at high X (flip when it sits low);
                // donor grip fatter ⇒ the fat end belongs at low X (flip when it sits high).
                flipEnds = tipIsFatter ? !importHighIsFatter : importHighIsFatter;
                diag.Info("glb.grip", $"Grip end chosen from the stock donor: its {(tipIsFatter ? "tip" : "grip")} end is the fatter one (ratio {hints.TipSpreadRatio:0.##}); this mesh's fat end was at {(importHighIsFatter ? "high" : "low")} X (ratio {importRatio:0.##}){(flipEnds ? " — swapped ends" : "")}.");
            }
            else
            {
                float lowSpread = EndSpread(oriented, minX, minX + range * 0.15f);
                float highSpread = EndSpread(oriented, maxX - range * 0.15f, maxX);
                flipEnds = highSpread > lowSpread;
                diag.Warn("glb.grip.ambiguous", $"Grip and tip ends have similar cross-sections (ratio {importRatio:0.##}) while the stock donor's {(tipIsFatter ? "tip" : "grip")} end is the fatter one; grip choice is a best guess — use flip grip/tip if it is backwards.");
            }
        }
        else
        {
            float lowSpread = EndSpread(oriented, minX, minX + range * 0.15f);
            float highSpread = EndSpread(oriented, maxX - range * 0.15f, maxX);
            if (MathF.Abs(highSpread - lowSpread) < 0.1f * MathF.Max(highSpread, MathF.Max(lowSpread, 1e-6f)))
                diag.Warn("glb.grip.ambiguous", "Grip and tip ends have similar cross-sections; grip choice is a best guess.");
            flipEnds = highSpread > lowSpread;
        }

        var total = rot;
        if (flipEnds)
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
        // Families whose stock donor is wide along WoW Y instead (crossbow prods) target mesh +Z
        // (WoW −Y): the same roll, landing on the donor's own wide axis.
        bool wideOnMeshY = hints?.WideAxisIsZ ?? true;
        {
            // The roll is found by an extent search (the angle whose cross-section silhouette is
            // widest along the target axis), not by 2D PCA: extents are what the donor's wide axis
            // was measured from and they are independent of vertex density, whereas a T-shaped
            // cross-section (crossbow prod + hanging stock) pulls a variance-based major axis
            // tens of degrees off the prod. For a flat blade both agree to within a degree.
            var (phi, extWide, extNarrow) = BestRoll(oriented, wideOnMeshY);
            if (extNarrow <= 1e-6f || extWide / extNarrow < 1.3f)
            {
                diag.Warn("glb.roll.ambiguous", "Cross-section is near-round; blade roll left as imported.");
            }
            else
            {
                var rolled = Matrix4x4.Multiply(total, Matrix4x4.CreateRotationX(phi));
                for (int i = 0; i < n; i++) oriented[i] = Vector3.Transform(centered[i], rolled);
                total = rolled;
                diag.Info("glb.roll", wideOnMeshY
                    ? $"Blade rolled {phi * 180f / MathF.PI:0.#}° about X so the wide cross-axis lies on +Y (WoW Z)."
                    : $"Rolled {phi * 180f / MathF.PI:0.#}° about X so the wide cross-axis lies on mesh Z (WoW Y) — the family's stock donor is wide across Y.");
                // X extents are unchanged by a roll about X, so minX/range stay valid.
            }
        }

        // Side decision + cross-section placement from the donor's measured hints. Both are
        // silhouette-based (tip-end centre vs box centre) so a hand-modelled stock M2 and a densely
        // sampled reconstruction are comparable. Donor hints are WoW space; the import is measured
        // in mesh space (mesh Y = WoW Z, mesh Z = −WoW Y), so the donor's Z values map onto mesh Y
        // unchanged and its Y values onto mesh Z with the sign flipped.
        bool sidePlaced = false;
        if (hints is not null)
        {
            // Four candidate side signals, donor values converted to mesh axes; the strongest one
            // on the donor decides (a bow's tips and a rifle's stock are both unmistakable).
            var candidates = new (string End, string AxisName, float Donor, Func<WeaponOrientationHints, float> Import)[]
            {
                ("tip",  "mesh Y (WoW Z)",  hints.TipSkewZ,   m => m.TipSkewY),
                ("tip",  "mesh Z (WoW −Y)", -hints.TipSkewY,  m => m.TipSkewZ),
                ("grip", "mesh Y (WoW Z)",  hints.GripSkewZ,  m => m.GripSkewY),
                ("grip", "mesh Z (WoW −Y)", -hints.GripSkewY, m => m.GripSkewZ),
            };
            var best = candidates.OrderByDescending(c => MathF.Abs(c.Donor)).First();
            if (MathF.Abs(best.Donor) >= WeaponOrientationHints.DecisiveSkew)
            {
                var measured = WeaponOrientationHints.Measure(oriented);
                float importSkew = best.Import(measured);
                if (MathF.Abs(importSkew) < WeaponOrientationHints.DecisiveSkew)
                {
                    diag.Warn("glb.side.ambiguous",
                        $"The family's stock donor carries its {best.End} end {best.Donor:+0.##;-0.##} of the cross extent off-centre along {best.AxisName}, but this mesh is symmetric there ({importSkew:+0.##;-0.##}); which way it hangs is a best guess — use the roll control if it sits upside down.");
                }
                else
                {
                    if (MathF.Sign(importSkew) != MathF.Sign(best.Donor))
                    {
                        total = Matrix4x4.Multiply(total, Matrix4x4.CreateRotationX(MathF.PI));
                        for (int i = 0; i < n; i++) oriented[i] = Vector3.Transform(centered[i], total);
                        diag.Info("glb.side", $"Rolled 180° about X so the {best.End} end hangs to the same side as the stock donor along {best.AxisName} (donor {best.Donor:+0.##;-0.##}, import was {importSkew:+0.##;-0.##}).");
                    }
                    else
                    {
                        diag.Info("glb.side", $"{(best.End == "tip" ? "Tip" : "Grip")} end already hangs to the stock donor's side along {best.AxisName} (donor {best.Donor:+0.##;-0.##}, import {importSkew:+0.##;-0.##}).");
                    }
                    sidePlaced = true;
                }
            }
        }

        float scale = donorExtent / range;

        // Palm-at-origin convention (donor-measured): the client puts the model origin in the palm,
        // and the donor's back end reaches palmBackFraction of its length behind that (golden sword:
        // −0.206 of 1.095 ≈ 18.8%; a staff ~mid-shaft). Place the scaled back end there so the hand
        // lands where it does on the family's stock weapons.
        float backX = -palmBackFraction * donorExtent;

        // Cross-section placement: once the side is settled, put the imported box centre where the
        // donor's box centre sits relative to the hand (a rifle's barrel rides above the grip; a
        // bow's grip stays at the back of its limbs). Without a side decision the centroid stays on
        // the hand axis exactly as before; sub-centimetre donor offsets are ignored so symmetric
        // families are untouched.
        float shiftY = 0f, shiftZ = 0f;
        if (hints is not null && sidePlaced)
        {
            var measured = WeaponOrientationHints.Measure(oriented);
            float targetY = hints.BoxCenterZ;    // WoW Z → mesh Y
            float targetZ = -hints.BoxCenterY;   // WoW Y → mesh Z (sign flip)
            if (MathF.Abs(targetY) >= 0.01f) shiftY = targetY - measured.BoxCenterY * scale;
            if (MathF.Abs(targetZ) >= 0.01f) shiftZ = targetZ - measured.BoxCenterZ * scale;
            if (shiftY != 0f || shiftZ != 0f)
                diag.Info("glb.cross.place", $"Cross-section placed like the stock donor: box centre moved by ({shiftY:+0.###;-0.###}, {shiftZ:+0.###;-0.###}) in mesh (Y,Z) to sit at WoW (Y {-targetZ:0.###}, Z {targetY:0.###}) relative to the hand.");
        }

        var outPos = new Vector3[n];
        var outNrm = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            var p = oriented[i];
            outPos[i] = new Vector3((p.X - minX) * scale + backX, p.Y * scale + shiftY, p.Z * scale + shiftZ);
            outNrm[i] = CoordinateContract.Normalize(Vector3.Transform(nrm[i], total));
        }

        var record = new MeshNormalizationRecord
        {
            Scale = scale,
            Translation = new Vector3(-minX * scale + backX, shiftY, shiftZ), // post-scale mapping: x' = (x − minX)·scale + backX
            WindingReversed = false, // the 180° reorient is det +1; source-mirror winding is handled at bake
            Method = $"PCA long-axis → +X; grip = wider-cross-section end; roll → wide cross-axis on {(wideOnMeshY ? "+Y" : "+Z")}" +
                     (sidePlaced ? "; side + cross placement matched to the stock donor" : "") +
                     $"; scaled to donor extent {donorExtent:0.###}; palm at origin (back end at −{palmBackFraction:P1})",
        };
        return (outPos, outNrm, record);
    }

    /// <summary>The three principal (PCA) directions of the centred cloud, by descending variance:
    /// power iteration for the first, deflation + power iteration for the second, cross product
    /// for the third. Degenerate clouds fall back to an arbitrary perpendicular basis.</summary>
    private static (Vector3 First, Vector3 Second, Vector3 Third) PrincipalAxes(Vector3[] centered)
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
        var v1 = cxx >= cyy && cxx >= czz ? Vector3.UnitX : (cyy >= czz ? Vector3.UnitY : Vector3.UnitZ);
        for (int it = 0; it < 48; it++)
        {
            var nv = Mul(v1);
            float len = nv.Length();
            if (len < 1e-20f) break;
            v1 = nv / len;
        }
        v1 = CoordinateContract.Normalize(v1);

        // Deflate: C₂ = C − λ₁ v₁v₁ᵀ, then iterate in the plane perpendicular to v₁.
        float lambda1 = Vector3.Dot(v1, Mul(v1));
        Vector3 Mul2(Vector3 v) => Mul(v) - lambda1 * Vector3.Dot(v1, v) * v1;
        var (seedU, seedW) = PerpBasis(v1);
        var v2 = seedU;
        bool degenerate = true;
        for (int it = 0; it < 48; it++)
        {
            var nv = Mul2(v2);
            nv -= Vector3.Dot(nv, v1) * v1; // stay perpendicular to v₁ against round-off
            float len = nv.Length();
            if (len < 1e-20f) break;
            v2 = nv / len;
            degenerate = false;
        }
        if (degenerate || !float.IsFinite(v2.X)) v2 = seedU;
        v2 = CoordinateContract.Normalize(v2 - Vector3.Dot(v2, v1) * v1);
        if (v2.LengthSquared() < 0.5f) v2 = seedU;
        var v3 = CoordinateContract.Normalize(Vector3.Cross(v1, v2));
        if (v3.LengthSquared() < 0.5f) v3 = seedW;
        return (v1, v2, v3);
    }

    /// <summary>Roll about X (radians) that maximises the cross-section extent along the target
    /// axis (mesh Y, or mesh Z when <paramref name="wideOnMeshY"/> is false), by a 1° sweep over
    /// the half-turn refined to 0.1°. Also returns the widest and narrowest silhouette widths seen
    /// over the sweep, so the caller can tell a round cross-section (ratio ≈ 1) from a flat one.</summary>
    private static (float Angle, float ExtWide, float ExtNarrow) BestRoll(Vector3[] oriented, bool wideOnMeshY)
    {
        float WidthAlong(float theta)
        {
            // Extent along the target axis after rolling by theta about X.
            float c = MathF.Cos(theta), s = MathF.Sin(theta);
            float min = float.MaxValue, max = float.MinValue;
            foreach (var p in oriented)
            {
                // Row-vector Vector3.Transform(p, CreateRotationX(θ)): y' = y·c − z·s ; z' = y·s + z·c
                float d = wideOnMeshY ? p.Y * c - p.Z * s : p.Y * s + p.Z * c;
                min = MathF.Min(min, d); max = MathF.Max(max, d);
            }
            return max - min;
        }

        float bestTheta = 0f, best = float.MinValue, narrowest = float.MaxValue;
        for (int deg = 0; deg < 180; deg++)
        {
            float theta = deg * MathF.PI / 180f;
            float w = WidthAlong(theta);
            if (w > best) { best = w; bestTheta = theta; }
            if (w < narrowest) narrowest = w;
        }
        // Refine ±1° at 0.1° steps.
        float refinedTheta = bestTheta, refined = best;
        for (int tenth = -10; tenth <= 10; tenth++)
        {
            float theta = bestTheta + tenth * (MathF.PI / 1800f);
            float w = WidthAlong(theta);
            if (w > refined) { refined = w; refinedTheta = theta; }
        }
        // Keep the angle in (−90°, 90°] so reported rolls read as the small correction they are.
        if (refinedTheta > MathF.PI / 2f) refinedTheta -= MathF.PI;
        return (refinedTheta, refined, narrowest);
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
