using System.Text.RegularExpressions;

namespace MangosSuperUI.Services.Mpq;

/// <summary>
/// The single numeric MPQ patch-precedence comparator (WEAPON_GEN.md §2.5, §7.4). Vanilla resolves
/// a file from the highest-priority archive that contains it: numbered patches beat the base
/// <c>patch.MPQ</c>, higher patch numbers beat lower ones, and any patch beats the base data
/// archives (dbc/model/texture/…). Plain string ordering gets this wrong for two-digit patch
/// numbers (<c>patch-2</c> sorts after <c>patch-10</c>), which is the bug this replaces.
///
/// The rank is deliberately layered so that swapping it in for the old reverse-alphabetic sort is
/// behavior-preserving for the current single-digit archive set: base archives all share one rank
/// and keep tie-breaking by name exactly as before, while patch archives are ordered numerically.
/// </summary>
public static class MpqPatchOrder
{
    private static readonly Regex PatchN = new(@"^patch-(\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private const int BaseArchiveRank = 1_000_000;
    private const int PatchBaseRank = 2_000_000;

    /// <summary>Higher wins. patch-N → 2,000,000 + N; bare patch → 2,000,000; any other (base
    /// data) archive → 1,000,000. Non-numeric patch names fall back to the patch base rank.</summary>
    public static int Rank(string archiveName)
    {
        var stem = Path.GetFileNameWithoutExtension(archiveName);
        if (string.Equals(stem, "patch", StringComparison.OrdinalIgnoreCase))
            return PatchBaseRank;

        var m = PatchN.Match(stem);
        if (m.Success && int.TryParse(m.Groups[1].Value, out int n))
            return PatchBaseRank + n;

        return BaseArchiveRank;
    }

    /// <summary>
    /// Ascending order (lowest precedence first) suitable for a "sort ascending, then iterate in
    /// reverse" precedence walk — which is how <see cref="MpqReaderService"/> checks held archives.
    /// Ties (same rank, e.g. two base archives) fall back to case-insensitive name order, matching
    /// the previous reverse-alphabetic behavior for those archives exactly.
    /// </summary>
    public static int CompareAscending(string a, string b)
    {
        int ra = Rank(a), rb = Rank(b);
        if (ra != rb) return ra.CompareTo(rb);
        return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Descending order (highest precedence first) for a forward-iterated list such as the
    /// live-patch list.</summary>
    public static int CompareDescending(string a, string b) => -CompareAscending(a, b);
}
