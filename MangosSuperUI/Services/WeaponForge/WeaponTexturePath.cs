namespace MangosSuperUI.Services.WeaponForge;

/// <summary>Canonical spelling for M2/MPQ texture member paths used as dictionary keys and
/// provenance checks. MPQ paths are case-insensitive, but slash direction and incidental outer
/// whitespace must not make the same member look like two different assets.</summary>
internal static class WeaponTexturePath
{
    internal static string? Canonicalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        string canonical = path.Trim().Replace('/', '\\');
        return canonical.Length > 0 ? canonical : null;
    }
}
