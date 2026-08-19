using System.Numerics;

namespace MangosSuperUI.Services.WeaponForge;

/// <summary>Geometry-facing QA that format validation cannot provide. Values are intentionally
/// dimensionless so they work before/after normalization and across providers.</summary>
public static class WeaponMeshQualityAnalyzer
{
    public static WeaponMeshQualityReport Analyze(RigidWeaponMesh mesh)
    {
        if (mesh.Positions.Length == 0) return new();
        float minX = mesh.Positions.Min(p => p.X), maxX = mesh.Positions.Max(p => p.X);
        float minY = mesh.Positions.Min(p => p.Y), maxY = mesh.Positions.Max(p => p.Y);
        float minZ = mesh.Positions.Min(p => p.Z), maxZ = mesh.Positions.Max(p => p.Z);
        float length = Math.Max(maxX - minX, 1e-6f);

        // Measure blade center drift only in front of the palm, where a sword should be straight.
        const int bins = 18;
        var sums = new Vector2[bins]; var counts = new int[bins];
        float bladeStart = minX + length * .28f;
        foreach (var p in mesh.Positions)
        {
            if (p.X < bladeStart) continue;
            int b = Math.Clamp((int)((p.X - bladeStart) / Math.Max(maxX - bladeStart, 1e-6f) * bins), 0, bins - 1);
            sums[b] += new Vector2(p.Y, p.Z); counts[b]++;
        }
        var centers = Enumerable.Range(0, bins).Where(i => counts[i] > 0)
            .Select(i => sums[i] / counts[i]).ToArray();
        float bend = 0;
        if (centers.Length > 2)
        {
            for (int i = 0; i < centers.Length; i++)
            {
                var straight = Vector2.Lerp(centers[0], centers[^1], i / (float)(centers.Length - 1));
                bend = Math.Max(bend, Vector2.Distance(centers[i], straight));
            }
            bend /= length;
        }
        float wideRatio = (maxY - minY) / length;
        float depthRatio = (maxZ - minZ) / length;
        return new WeaponMeshQualityReport
        {
            Length = length,
            WideRatio = wideRatio,
            DepthRatio = depthRatio,
            BladeCenterlineDeviation = bend,
            LooksPaperThin = depthRatio < .012f,
            LooksBent = bend > .018f,
        };
    }

    public static void AddDiagnostics(WeaponMeshQualityReport q, ForgeDiagnostics d)
    {
        d.Info("mesh.form.metrics",
            $"Form: depth/length {q.DepthRatio:P1}, width/length {q.WideRatio:P1}, blade-center deviation {q.BladeCenterlineDeviation:P1}.");
        if (q.LooksPaperThin)
            d.Warn("mesh.form.thin", "The reconstruction is nearly paper-thin. Increase depth scale, replace the side reference, or use multiview reconstruction.");
        if (q.LooksBent)
            d.Warn("mesh.form.bent", "The reconstructed blade centerline is visibly bent. Enable blade straightening or improve the reference views.");
    }
}

public sealed class WeaponMeshQualityReport
{
    public float Length { get; init; }
    public float WideRatio { get; init; }
    public float DepthRatio { get; init; }
    public float BladeCenterlineDeviation { get; init; }
    public bool LooksPaperThin { get; init; }
    public bool LooksBent { get; init; }
}
