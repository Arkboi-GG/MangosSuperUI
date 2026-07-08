using System.Text.RegularExpressions;

namespace MangosSuperUI.BotLogic.Chat.Engine;

/// <summary>
/// CHAT_ARCHITECTURE §10.4 step 8 — the non-configurable content floor. A hit discards
/// the ENTIRE line (never edits it); the coordinator logs [CHAT-ENGINE] floor. The
/// banter_intensity slider shapes prompt tone ABOVE this floor; the floor has no knob.
///
/// The shipped list covers explicit sexual content and a starter slur set by stem.
/// OPERATOR NOTE (Nico): extend BlockedStems below with the full slur list you want
/// enforced — kept deliberately short in generated code; matching is case-insensitive
/// substring-on-word-stem, so one stem catches plural/verb forms.
/// Ordinary profanity is intentionally NOT floored — 2005 chat swears; the floor is
/// for slurs and sexual content only (§10.4).
/// </summary>
public static class ContentFloor
{
    // Stems, lowercase. Substring match within word characters (catches suffixed forms).
    private static readonly string[] BlockedStems =
    {
        // explicit sexual content
        "porn", "hentai", "blowjob", "handjob", "cumshot", "deepthroat",
        "pedo", "loli", "rape",
        // slur stems — EXTEND ME (see class doc)
        "nigg", "fagg", "kike", "spic", "chink", "tranny", "retard",
    };

    private static readonly Regex WordScan = new(@"[a-z0-9]+", RegexOptions.Compiled);

    /// <summary>True → the line must be discarded (returns the matched stem for the log).</summary>
    public static bool IsBlocked(string line, out string matchedStem)
    {
        matchedStem = "";
        if (string.IsNullOrEmpty(line)) return false;
        var lower = line.ToLowerInvariant();
        foreach (Match word in WordScan.Matches(lower))
        {
            foreach (var stem in BlockedStems)
            {
                if (word.Value.Contains(stem))
                {
                    matchedStem = stem;
                    return true;
                }
            }
        }
        return false;
    }
}
