namespace MangosSuperUI.BotLogic.Chat.Engine;

/// <summary>
/// Fixed style tables (CHAT_ARCHITECTURE §10.4 step 4): the abbreviation dictionary by
/// abbrev_level (0 none → 3 aggressive; tiers are CUMULATIVE — level 3 applies 1+2+3),
/// applied probabilistically (60% per candidate) so it's a tendency, not a cipher.
/// Also the qwerty adjacency map for step-5 typo injection.
/// Doc-anchored entries: you→u, are→r, to→2 (level 3 only), be right back→brb,
/// though→tho, probably→prob, about→abt, laughing→lol-class. The rest are
/// era-appropriate fill in the same spirit.
/// </summary>
public static class StyleTables
{
    /// <summary>Longest-phrase-first within each tier so multi-word entries win.</summary>
    public static readonly IReadOnlyList<(string From, string To)>[] AbbrevTiers =
    {
        // Level 1 — light, everyone did these
        new List<(string, string)>
        {
            ("be right back", "brb"),
            ("laughing out loud", "lol"),
            ("hahaha", "lol"), ("haha", "lol"),
            ("though", "tho"),
            ("probably", "prob"),
            ("thanks", "thx"),
            ("nevermind", "nvm"),
        },
        // Level 2 — the classic MMO register
        new List<(string, string)>
        {
            ("see you later", "cya"),
            ("what the heck", "wth"),
            ("about", "abt"),
            ("you", "u"),
            ("your", "ur"),
            ("are", "r"),
            ("really", "rly"),
            ("people", "ppl"),
            ("because", "cuz"),
            ("right now", "atm"),
            ("i don't know", "idk"), ("i dont know", "idk"),
        },
        // Level 3 — aggressive, the 14-year-old register
        new List<(string, string)>
        {
            ("to", "2"),
            ("for", "4"),
            ("see", "c"),
            ("why", "y"),
            ("anyone", "any1"),
            ("someone", "some1"),
            ("later", "l8r"),
            ("okay", "k"), ("ok", "k"),
            ("please", "plz"),
            ("wait", "w8"),
        },
    };

    /// <summary>Adjacent qwerty keys for step-5 typo injection (lowercase only).</summary>
    public static readonly IReadOnlyDictionary<char, string> QwertyAdjacent = new Dictionary<char, string>
    {
        ['q'] = "wa", ['w'] = "qes", ['e'] = "wrd", ['r'] = "etf", ['t'] = "ryg",
        ['y'] = "tuh", ['u'] = "yij", ['i'] = "uok", ['o'] = "ipl", ['p'] = "ol",
        ['a'] = "qsz", ['s'] = "awdx", ['d'] = "sefc", ['f'] = "drgv", ['g'] = "fthb",
        ['h'] = "gyjn", ['j'] = "hukm", ['k'] = "jil", ['l'] = "kop",
        ['z'] = "asx", ['x'] = "zsc", ['c'] = "xdv", ['v'] = "cfb", ['b'] = "vgn",
        ['n'] = "bhm", ['m'] = "njk"
    };
}
