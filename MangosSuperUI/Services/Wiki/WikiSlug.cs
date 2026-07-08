using System.Text;

namespace MangosSuperUI.Services;

/// <summary>
/// The ONE anchor-slug rule for the wiki. Both the page renderer (WikiDocStore) and
/// the future search indexer (W2) must compute anchors through this class and nowhere
/// else — otherwise a search deep-link like <c>/Wiki?path=...#member-loadaura</c> can
/// drift from the id the page actually rendered. This is wiki-plan gotcha G4, made a
/// single choke point on purpose.
///
/// Rules (deliberately boring and stable):
///   Core(text)    lowercase; every run of non [a-z0-9] becomes a single '-'; trim '-'.
///   Heading(text) = Core(text)                    e.g. "Map — Aura"        -> "map-aura"
///   Member(name)  = "member-" + Core(name)        e.g. "GetModifier#2"     -> "member-getmodifier-2"
///
/// The '#N' disambiguator that the C++ extraction stamps on overloaded members is kept,
/// not stripped, so GetModifier and GetModifier#2 get distinct anchors — matching how the
/// MAP-table call cells reference them.
/// </summary>
public static class WikiSlug
{
    public static string Core(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new StringBuilder(text.Length);
        bool lastDash = false;
        foreach (var ch in text.Trim())
        {
            char c = char.ToLowerInvariant(ch);
            if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
            {
                sb.Append(c);
                lastDash = false;
            }
            else if (!lastDash && sb.Length > 0)
            {
                sb.Append('-');
                lastDash = true;
            }
        }
        // trim a trailing '-'
        if (sb.Length > 0 && sb[^1] == '-') sb.Length -= 1;
        return sb.ToString();
    }

    public static string Heading(string text) => Core(text);

    public static string Member(string name) => "member-" + Core(name);
}
