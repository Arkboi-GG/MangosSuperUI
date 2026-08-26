using System.Text.RegularExpressions;

namespace MangosSuperUI.BotLogic.Chat.Engine;

/// <summary>
/// CHAT_ARCHITECTURE §10.4 step 8 — the non-configurable content floor. A hit discards
/// the ENTIRE line (never edits it); the coordinator logs [CHAT-ENGINE] floor. The
/// banter_intensity slider shapes prompt tone ABOVE this floor; the floor has no knob.
///
/// SCOPE, STATED PLAINLY: slurs and explicit sexual content. Ordinary profanity is NOT
/// floored and never was — as of 2026-07-13 it is actively PRODUCED (§10.4 step 6b,
/// SwearTables.cs), because 2005 chat swore and its absence was the conspicuous thing.
/// The floor is the line between "people cussing at each other in the Barrens" and
/// content this server will not emit under any settings. Nothing in step 6b can reach
/// this list, and this step runs after it regardless.
///
/// MATCHING (fixed 2026-07-13): the old implementation was substring-anywhere, so "spic"
/// matched SPICY and "rape" matched GRAPE / SCRAPE / DRAPE — those lines were being
/// silently discarded and logged as floor hits. Ambiguous stems now live in BlockedWords
/// (whole-word + inflections); only stems with no plausible innocent host stay in
/// BlockedStems.
///
/// OPERATOR NOTE (Nico): extend BlockedStems / BlockedWords with the full slur list you
/// want enforced — kept deliberately short in generated code. Matching is
/// case-insensitive.
/// </summary>
public static class ContentFloor
{
    /// <summary>
    /// Substring-within-word stems. ONLY put a stem here if no innocent English word can
    /// contain it — a stem here will match anywhere inside a token.
    /// </summary>
    private static readonly string[] BlockedStems =
    {
        // explicit sexual content
        "porn", "hentai", "blowjob", "handjob", "cumshot", "deepthroat",
        "pedo", "lolicon",
        // slur stems
        "nigg", "fagg", "kike", "chink", "tranny", "retard",
    };

    /// <summary>
    /// Whole-word (and inflection) matches. These live here BECAUSE a substring test
    /// false-positives on ordinary words:
    ///   "spic" → spicy, suspicion      "rape" → grape, scrape, drape
    ///   "loli" → lollipop (misspelled)
    /// </summary>
    private static readonly HashSet<string> BlockedWords = new(StringComparer.Ordinal)
    {
        "spic", "spics",
        "rape", "rapes", "raped", "raping", "rapist", "rapists",
        "loli", "lolis",
    };

    private static readonly Regex WordScan = new(@"[a-z0-9]+", RegexOptions.Compiled);

    /// <summary>True → the line must be discarded (returns the matched stem for the log).</summary>
    public static bool IsBlocked(string line, out string matchedStem)
    {
        matchedStem = "";
        if (string.IsNullOrEmpty(line)) return false;   // cb:fold pure text scan, floor verdict probed at caller

        var lower = line.ToLowerInvariant();
        foreach (Match word in WordScan.Matches(lower))
        {
            if (BlockedWords.Contains(word.Value))
            {   // cb:fold pure text scan, floor verdict probed at caller
                matchedStem = word.Value;
                return true;
            }

            foreach (var stem in BlockedStems)
            {
                if (word.Value.Contains(stem))
                {   // cb:fold pure text scan, floor verdict probed at caller
                    matchedStem = stem;
                    return true;
                }
            }
        }
        return false;
    }
}