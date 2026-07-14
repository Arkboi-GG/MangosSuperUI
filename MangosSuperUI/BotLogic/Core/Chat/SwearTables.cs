using System.Text.RegularExpressions;
using MangosSuperUI.BotLogic.Chat.Core;

namespace MangosSuperUI.BotLogic.Chat.Engine;

/// <summary>
/// CHAT_ARCHITECTURE §10.4 step 6b — the REGISTER PASS (new, 2026-07-13).
///
/// WHY THIS EXISTS. Ordinary profanity was never floored (§10.4 step 8 is slurs and
/// sexual content only) — but no bot ever swore anyway, because an aligned small model
/// bowdlerizes itself. It writes "what the HECK", "FREAKING useless", "DARN it", "f***".
/// A person in 2005 Barrens chat does not talk like that, and the absence is more
/// conspicuous than the presence would be.
///
/// THE TRICK: the model already marks the slots. Every "heck" is a swear-shaped hole
/// with the swear filed off. Substituting at those slots is grammatical BY CONSTRUCTION
/// and reads native, because the model put the emphasis exactly where a person would.
/// Blind injection ("prepend 'fuck' at 12%") produces tourist swearing; substitution
/// produces the real thing. So this pass is, in order:
///
///   1. DE-CENSOR   — heck→hell, freaking→fuckin, f***→fuck. Fixes what the model broke.
///   2. ESCALATE    — stock words move up the register: noob→shitter, stuff→shit.
///   3. INTENSIFY   — really/very/super → damn/fuckin.
///   4. INTERJECT   — a frustrated line gets a lead-in: "ugh" / "shit" / "fuckin hell".
///
/// Strength = persona typing.swear_level (0–3) × voice.banter_intensity (0–1), via
/// EffectiveLevel(). banter 0.5 (default) is identity; banter 0 silences the pass
/// entirely; banter 1 doubles everyone. A per-line budget stops "fuckin fuck shit".
///
/// ORDERING IS LOAD-BEARING: this runs AFTER caps/abbrev/typo/tics and BEFORE the
/// anachronism scrub (step 7) and the CONTENT FLOOR (step 8). The floor still gets the
/// last word — nothing here can produce a slur, and if it somehow did, the floor eats
/// the whole line. Slurs and sexual content are NOT in scope here and never will be.
///
/// The primary channel for register is still the PROMPT (§10.3) and, above all, the
/// persona's example_lines — a card whose anchors read "lol what a shitter" produces a
/// bot that talks that way natively. This pass is the backstop for when the model
/// flinches anyway. RegisterLine() below is what §10.3 injects.
/// </summary>
public static class SwearTables
{
    // ==================== Strength ====================

    /// <summary>
    /// persona swear_level (0–3) scaled by voice.banter_intensity (0–1).
    /// banter 0 → 0 (nobody swears); banter 0.5 → identity; banter 1 → ×2, capped at 3.
    /// </summary>
    public static int EffectiveLevel(int swearLevel, float banterIntensity) =>
        Math.Clamp((int)Math.Round(Math.Clamp(swearLevel, 0, 3) * Math.Clamp(banterIntensity, 0f, 1f) * 2f), 0, 3);

    /// <summary>The §10.3 prompt line. The prompt is the primary channel; this pass is the backstop.</summary>
    public static string RegisterLine(int effLevel) => effLevel switch
    {
        0 => "You don't swear — you're the one person in the zone who says \"darn\".",
        1 => "You swear a little when something goes wrong (damn, crap, hell). Never censor yourself " +
             "with asterisks, and never type \"heck\" or \"darn\" — nobody typed that.",
        2 => "You swear casually like everyone did in 2005 (damn, shit, ass, bastard) and you'll call " +
             "a bad player a shitter or a scrub. It's normal, not edgy. Never censor yourself with " +
             "asterisks, and never type \"heck\", \"darn\" or \"freaking\" — nobody typed that.",
        _ => "You swear constantly and casually — it's punctuation to you. Never censor yourself with " +
             "asterisks, and never type \"heck\", \"darn\" or \"freaking\" — nobody typed that.",
    };

    // ==================== 1. De-censor ====================

    private sealed record Swap(string From, string To, int MinLevel, bool LeadOnly = false);

    /// <summary>The model's self-censorship, undone. LeadOnly words are also real verbs.</summary>
    private static readonly Swap[] Bowdlerized =
    {
        new("heck", "hell", 1),
        new("darn", "damn", 1),
        new("darned", "damned", 1),
        new("dang", "damn", 1),
        new("gosh", "god", 1),
        new("golly", "god", 1),
        new("jeez", "christ", 1),
        new("geez", "christ", 1),
        new("crud", "crap", 1),
        new("shucks", "crap", 1),

        new("frick", "shit", 2),
        new("fudge", "shit", 2),
        new("shoot", "shit", 2, LeadOnly: true),   // "shoot the boar" must survive

        new("freaking", "fuckin", 3),
        new("freakin", "fuckin", 3),
        new("frickin", "fuckin", 3),
        new("friggin", "fuckin", 3),
        new("frigging", "fuckin", 3),
        new("flipping", "fuckin", 3),
        new("effing", "fuckin", 3),
        new("heckin", "fuckin", 3),
    };

    /// <summary>Asterisk-masked swears — f***, sh*t, a**. Restore the word.</summary>
    private static readonly (Regex Rx, string To, int MinLevel)[] Masked =
    {
        (Rx(@"(?<![a-z0-9])f[\*\-#@\$]ck(?![a-z0-9])"), "fuck", 3),
        (Rx(@"(?<![a-z0-9])f[\*\-#@\$]{2,4}k?(?![a-z0-9])"), "fuck", 3),
        (Rx(@"(?<![a-z0-9])sh[\*\-#@\$]t(?![a-z0-9])"), "shit", 2),
        (Rx(@"(?<![a-z0-9])s[\*\-#@\$]{2,3}t?(?![a-z0-9])"), "shit", 2),
        (Rx(@"(?<![a-z0-9])b[\*\-#@\$]{1,2}tch(?![a-z0-9])"), "bitch", 2),
        (Rx(@"(?<![a-z0-9])d[\*\-#@\$]mn(?![a-z0-9])"), "damn", 1),
        (Rx(@"(?<![a-z0-9])a[\*\-#@\$]{2}(?![a-z0-9])"), "ass", 1),
    };

    private static Regex Rx(string p) => new(p, RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // ==================== 2. Escalate ====================

    /// <summary>word → replacement indexed by effective level (index 0 = untouched).</summary>
    private static readonly Dictionary<string, string[]> Escalations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["noob"] = new[] { "noob", "noob", "shitter", "shitter" },
        ["newb"] = new[] { "newb", "newb", "scrub", "shitter" },
        ["newbie"] = new[] { "newbie", "newbie", "scrub", "shitter" },
        ["newbs"] = new[] { "newbs", "newbs", "shitters", "shitters" },
        ["noobs"] = new[] { "noobs", "noobs", "shitters", "shitters" },
        ["loser"] = new[] { "loser", "loser", "scrub", "shitter" },
        ["idiot"] = new[] { "idiot", "idiot", "dumbass", "dumbass" },
        ["jerk"] = new[] { "jerk", "jerk", "dick", "dick" },
        ["jerks"] = new[] { "jerks", "jerks", "dicks", "dicks" },
        ["screwed"] = new[] { "screwed", "screwed", "screwed", "fucked" },
        ["crap"] = new[] { "crap", "crap", "shit", "shit" },
        ["stuff"] = new[] { "stuff", "stuff", "shit", "shit" },
        ["nonsense"] = new[] { "nonsense", "nonsense", "bullshit", "bullshit" },
        ["annoying"] = new[] { "annoying", "annoying", "annoying as hell", "fuckin annoying" },
    };

    // ==================== 3. Intensify ====================

    private static readonly Regex Intensifiable =
        Rx(@"(?<![a-z0-9])(really|very|super|extremely|totally)(?![a-z0-9])");

    private static readonly string[][] Intensifiers =
    {
        Array.Empty<string>(),
        new[] { "damn", "real" },
        new[] { "damn", "freakin", "hella" },
        new[] { "fuckin", "goddamn", "fuckin" },
    };

    // ==================== 4. Interject ====================

    private static readonly string[][] Interjections =
    {
        Array.Empty<string>(),
        new[] { "ugh", "damn", "man", "crap" },
        new[] { "ugh", "damn", "shit", "christ", "ah hell" },
        new[] { "shit", "fuck", "fuckin hell", "god damn", "jesus christ" },
    };

    /// <summary>Positive-emphasis openers — L3 only, so bots aren't purely grumpy swearers.</summary>
    private static readonly string[] PositiveInterjections = { "hell yeah", "fuck yeah", "hell yes" };

    /// <summary>Frustration cues — the line has to EARN the interjection.</summary>
    private static readonly HashSet<string> NegativeCues = new(StringComparer.OrdinalIgnoreCase)
    {
        "died", "die", "dying", "dead", "death", "corpse", "wipe", "wiped", "ganked", "gank",
        "camped", "camping", "lag", "laggy", "lagging", "crash", "crashed", "dc", "disconnected",
        "stuck", "lost", "broke", "broken", "sucks", "suck", "hate", "stupid", "dumb", "annoying",
        "again", "seriously", "wtf", "ugh", "fail", "failed", "worst", "terrible", "awful",
        "aggro", "adds", "runners", "expensive", "poor", "waiting", "forever", "wasted", "ruined",
        "nothing", "never", "cant", "wont", "nope",
    };

    private static readonly HashSet<string> PositiveCues = new(StringComparer.OrdinalIgnoreCase)
    {
        "grats", "gratz", "congrats", "nice", "sweet", "awesome", "finally", "yes", "yeah",
        "dinged", "leveled", "got", "win", "won", "epic", "sick",
    };

    // ==================== The pass ====================

    /// <summary>
    /// Apply the register pass. Returns the line unchanged when the effective level is 0.
    /// Never produces slurs or sexual content — the content floor (step 8) still runs after.
    /// </summary>
    public static string Apply(PersonaCard card, string line, float banterIntensity,
                               float moodValence, Random rng)
    {
        int lvl = EffectiveLevel(card.Typing.SwearLevel, banterIntensity);
        if (lvl <= 0 || string.IsNullOrWhiteSpace(line)) return line;

        // Strength scalar: 1.0 at level 3, 0.33 at level 1. Times the banter slider.
        double strength = (lvl / 3.0) * Math.Clamp(banterIntensity, 0f, 1f) * 2.0;
        strength = Math.Clamp(strength, 0.0, 1.0);

        // Budget: how many NEW swears we're allowed to add (de-censor doesn't count —
        // it replaces a word the model already chose to put there).
        int budget = lvl >= 3 ? 2 : 1;

        line = DeCensor(line, lvl);
        line = Escalate(line, lvl, strength, rng, ref budget);
        line = Intensify(line, lvl, strength, rng, ref budget);
        line = Interject(card, line, lvl, strength, moodValence, rng, ref budget);

        return line;
    }

    // ---------- 1 ----------

    private static string DeCensor(string line, int lvl)
    {
        foreach (var (rx, to, min) in Masked)
        {
            if (lvl < min) continue;
            line = rx.Replace(line, to);
        }

        foreach (var s in Bowdlerized)
        {
            if (lvl < s.MinLevel) continue;
            if (s.LeadOnly)
            {
                // Only as an interjection: line-initial, or right after a comma.
                var lead = new Regex($@"^(\s*){Regex.Escape(s.From)}(?![a-z0-9])",
                                     RegexOptions.IgnoreCase);
                line = lead.Replace(line, m => m.Groups[1].Value + s.To, 1);
                continue;
            }
            line = Regex.Replace(line, $@"(?<![a-z0-9]){Regex.Escape(s.From)}(?![a-z0-9])",
                                 s.To, RegexOptions.IgnoreCase);
        }
        return line;
    }

    // ---------- 2 ----------

    private static string Escalate(string line, int lvl, double strength, Random rng, ref int budget)
    {
        if (budget <= 0) return line;

        var words = line.Split(' ');
        for (int i = 0; i < words.Length && budget > 0; i++)
        {
            var bare = Trim(words[i], out var lead, out var tail);
            if (bare.Length == 0) continue;
            if (!Escalations.TryGetValue(bare, out var byLevel)) continue;

            var repl = byLevel[Math.Clamp(lvl, 0, 3)];
            if (string.Equals(repl, bare, StringComparison.OrdinalIgnoreCase)) continue;
            if (rng.NextDouble() >= 0.55 * strength) continue;

            words[i] = lead + MatchCase(bare, repl) + tail;
            budget--;
        }
        return string.Join(' ', words);
    }

    // ---------- 3 ----------

    private static string Intensify(string line, int lvl, double strength, Random rng, ref int budget)
    {
        if (budget <= 0) return line;
        if (rng.NextDouble() >= 0.45 * strength) return line;

        var pool = Intensifiers[Math.Clamp(lvl, 0, 3)];
        if (pool.Length == 0) return line;

        bool hit = false;
        var result = Intensifiable.Replace(line, m =>
        {
            if (hit) return m.Value;      // one per line
            hit = true;
            return MatchCase(m.Value, pool[rng.Next(pool.Length)]);
        }, 1);

        if (hit) budget--;
        return result;
    }

    // ---------- 4 ----------

    private static string Interject(PersonaCard card, string line, int lvl, double strength,
                                    float moodValence, Random rng, ref int budget)
    {
        if (budget <= 0 || line.Length < 6) return line;

        var words = line.Split(new[] { ' ', ',', '.', '!', '?', ':', ';' },
                               StringSplitOptions.RemoveEmptyEntries);

        bool negative = words.Any(w => NegativeCues.Contains(w));
        bool positive = words.Any(w => PositiveCues.Contains(w));

        // Don't stack: if the line already swears (the model did it, or we just did), stop.
        if (AlreadySwears(line)) return line;

        string? interjection = null;

        if (negative)
        {
            // A sour mood makes the frustrated line more likely to open with one.
            double p = 0.40 * strength * (moodValence < -0.15f ? 1.5 : 1.0);
            if (rng.NextDouble() < p)
            {
                var pool = Interjections[Math.Clamp(lvl, 0, 3)];
                if (pool.Length > 0) interjection = pool[rng.Next(pool.Length)];
            }
        }
        else if (positive && lvl >= 3 && rng.NextDouble() < 0.18 * strength)
        {
            interjection = PositiveInterjections[rng.Next(PositiveInterjections.Length)];
        }

        if (interjection == null) return line;
        budget--;

        // Proper-caps typists get it as its own sentence so we don't have to re-case the
        // body ("Ugh. That was..."); lowercase typists just run it on ("ugh that was...").
        if (card.Typing.Caps == "proper")
        {
            var cap = char.ToUpperInvariant(interjection[0]) + interjection[1..];
            return $"{cap}. {line}";
        }
        return $"{interjection} {line}";
    }

    // ---------- helpers ----------

    private static readonly string[] SwearMarkers =
    {
        "fuck", "shit", "damn", "hell", "bitch", "bastard", "dick", "ass", "crap",
        "piss", "prick", "dumbass", "shitter", "christ", "god",
    };

    private static bool AlreadySwears(string line)
    {
        var lower = line.ToLowerInvariant();
        foreach (var m in SwearMarkers)
            if (lower.Contains(m)) return true;
        return false;
    }

    /// <summary>Strip leading/trailing punctuation off a token, keeping it for re-assembly.</summary>
    private static string Trim(string token, out string lead, out string tail)
    {
        int a = 0, b = token.Length;
        while (a < b && !char.IsLetterOrDigit(token[a])) a++;
        while (b > a && !char.IsLetterOrDigit(token[b - 1])) b--;
        lead = token[..a];
        tail = token[b..];
        return token[a..b];
    }

    /// <summary>Keep the original token's casing shape (lowercase chat mostly, but be safe).</summary>
    private static string MatchCase(string original, string replacement)
    {
        if (original.Length == 0 || replacement.Length == 0) return replacement;
        if (original.All(char.IsUpper) && original.Length > 1) return replacement.ToUpperInvariant();
        if (char.IsUpper(original[0]))
            return char.ToUpperInvariant(replacement[0]) + replacement[1..];
        return replacement;
    }
}
