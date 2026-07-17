namespace MangosSuperUI.BotLogic.Chat.Voice;

using MangosSuperUI.BotLogic.Chat.Core;

/// <summary>
/// The §6.3 stratified sampling axes. Diversity is STRUCTURAL: every skeleton axis is
/// sampled independently BEFORE any LLM call, so the library cannot cluster the way an
/// all-LLM (or scraped-corpus) approach would. The LLM only writes prose to fit.
///
/// AMENDED 2026-07-13 (a): swear_level is a sampled axis (§6.2 schema v2), keyed to age
/// band AND disposition. Disposition is sampled BEFORE typing, because typing reads it.
/// (Incidental fix: humor was being rolled twice into two different fields.)
///
/// AMENDED 2026-07-13 (b): GIVEN NAME IS NOW A SKELETON AXIS. It was the one identity
/// field left to the LLM, and the first 300-card build came back with 48 Dereks and 24
/// Dales — 76 distinct names across 300 cards. A small model has favorites; every other
/// identity axis is sampled here precisely so it cannot collapse one, and this was the
/// hole. Pools are region-grouped and age-cohort-gated: these are people born 1947–1992,
/// so a 14-year-old in 2005 is Tyler or Brandon and a 52-year-old is Gary or Cheryl,
/// never the reverse. The builder additionally caps any single name at 4 cards.
///
/// Axes: age bands (weighted 10/20/30/20/15/5), 12 regions with timezones, given names by
/// region × cohort, 40 era-appropriate occupations (age-gated), 60 interests, 10 gaming
/// backgrounds, per-age-band typing distributions (younger → lower caps, higher abbrev,
/// higher wpm, more typos), swear register, humor + tic pools.
/// </summary>
public static class VoiceTables
{
    // ==================== Skeleton ====================

    public sealed record Skeleton(int Age, string AgeBand, string Region, int TimezoneOffset,
        string GivenName, string OccupationCategory, string GamingBackground, string Humor,
        List<string> Interests, PersonaTyping Typing, PersonaDisposition Disposition);

    public static Skeleton Sample(Random rng)
    {
        var band = SampleAgeBand(rng);
        int age = rng.Next(band.Min, band.Max + 1);
        var (region, tz) = Regions[rng.Next(Regions.Length)];
        var occ = SampleOccupation(rng, age);
        var interests = SampleInterests(rng, age);
        var humor = Humors[rng.Next(Humors.Length)];

        // Disposition FIRST — typing (specifically swear_level) reads it.
        var disposition = new PersonaDisposition
        {
            Warmth = (float)rng.NextDouble(),
            Irritability = (float)rng.NextDouble(),
            Confidence = (float)rng.NextDouble(),
            Openness = (float)rng.NextDouble(),
            Humor = humor
        };

        return new Skeleton(
            Age: age,
            AgeBand: band.Name,
            Region: region,
            TimezoneOffset: tz,
            GivenName: SampleGivenName(rng, region, age),
            OccupationCategory: occ,
            GamingBackground: GamingBackgrounds[rng.Next(GamingBackgrounds.Length)],
            Humor: humor,
            Interests: interests,
            Typing: SampleTyping(rng, band, disposition),
            Disposition: disposition);
    }

    // ==================== Age bands (§6.3: weighted 10/20/30/20/15/5) ====================

    public sealed record AgeBand(string Name, int Min, int Max, float Weight);

    public static readonly AgeBand[] AgeBands =
    {
        new("13-15", 13, 15, 0.10f),
        new("16-18", 16, 18, 0.20f),
        new("19-23", 19, 23, 0.30f),
        new("24-30", 24, 30, 0.20f),
        new("31-45", 31, 45, 0.15f),
        new("46+",   46, 58, 0.05f),
    };

    private static AgeBand SampleAgeBand(Random rng)
    {
        double roll = rng.NextDouble(), acc = 0;
        foreach (var b in AgeBands) { acc += b.Weight; if (roll < acc) return b; }
        return AgeBands[^1];
    }

    // ==================== Regions (12, with 2005 realm-mix timezones) ====================

    public static readonly (string Region, int Tz)[] Regions =
    {
        ("US-East", -5), ("US-South", -6), ("US-Midwest", -6), ("US-Mountain", -7),
        ("US-West", -8), ("Canada", -5), ("UK", 0), ("Ireland", 0),
        ("Germany", 1), ("Scandinavia", 1), ("Netherlands", 1), ("Australia", 10),
    };

    // ==================== Given names (region × cohort; these people were born 1947–1992) ====================

    private static readonly string[] AngloTeenM = { "Tyler", "Brandon", "Austin", "Cody", "Dylan", "Jordan", "Zach", "Kyle", "Devin", "Trevor", "Corey", "Shane", "Colton", "Hunter", "Logan", "Bryce" };
    private static readonly string[] AngloTeenF = { "Kayla", "Brittany", "Ashley", "Megan", "Amber", "Jessica", "Courtney", "Chelsea", "Alyssa", "Danielle", "Brooke", "Kelsey" };
    private static readonly string[] AngloTwentiesM = { "Josh", "Ryan", "Justin", "Brian", "Eric", "Nick", "Matt", "Chris", "Dan", "Jeff", "Adam", "Sean", "Jason", "Aaron", "Derek", "Travis", "Marcus", "Andy" };
    private static readonly string[] AngloTwentiesF = { "Amanda", "Sarah", "Nicole", "Heather", "Melissa", "Erin", "Kristen", "Lindsay", "Rachel", "Katie", "Steph", "Jenna" };
    private static readonly string[] AngloAdultM = { "Mike", "Dave", "Scott", "Todd", "Greg", "Kevin", "Brad", "Doug", "Shawn", "Craig", "Rob", "Tim", "Mark", "Kurt", "Vince" };
    private static readonly string[] AngloAdultF = { "Tracy", "Michelle", "Lisa", "Kim", "Julie", "Angela", "Christine", "Wendy", "Dawn", "Stacy", "Renee" };
    private static readonly string[] AngloOlderM = { "Gary", "Dennis", "Randy", "Wayne", "Larry", "Ron", "Rick", "Steve", "Terry", "Glenn", "Dale", "Bruce", "Roger" };
    private static readonly string[] AngloOlderF = { "Cheryl", "Debbie", "Karen", "Sandra", "Nancy", "Linda", "Pam", "Sue", "Barb", "Donna" };

    private static readonly string[] BritYoungM = { "Liam", "Callum", "Connor", "Jack", "Aidan", "Niall", "Sean", "Ciaran", "Declan", "Lewis", "Ross", "Danny" };
    private static readonly string[] BritYoungF = { "Aoife", "Siobhan", "Niamh", "Chloe", "Emma", "Hannah", "Sinead", "Lauren", "Gemma" };
    private static readonly string[] BritAdultM = { "Gareth", "Stuart", "Neil", "Colin", "Alan", "Ian", "Gavin", "Eamon", "Padraig", "Dermot", "Graham", "Nigel", "Barry" };
    private static readonly string[] BritAdultF = { "Fiona", "Claire", "Orla", "Louise", "Bernadette", "Deirdre", "Helen", "Julie" };

    private static readonly string[] GerYoungM = { "Lukas", "Jonas", "Tobias", "Sebastian", "Florian", "Marcel", "Niklas", "Kevin", "Dennis", "Christoph" };
    private static readonly string[] GerYoungF = { "Lena", "Anna", "Julia", "Lisa", "Vanessa", "Nadine", "Katrin" };
    private static readonly string[] GerAdultM = { "Stefan", "Thomas", "Andreas", "Matthias", "Michael", "Jürgen", "Frank", "Uwe", "Ralf", "Dirk" };
    private static readonly string[] GerAdultF = { "Sabine", "Petra", "Claudia", "Andrea", "Kerstin", "Birgit", "Silke" };

    private static readonly string[] ScanYoungM = { "Erik", "Lars", "Magnus", "Anders", "Henrik", "Jonas", "Mikkel", "Kasper", "Eirik", "Sindre", "Emil" };
    private static readonly string[] ScanYoungF = { "Ida", "Sofie", "Maja", "Linnea", "Hanne", "Elin" };
    private static readonly string[] ScanAdultM = { "Björn", "Sven", "Nils", "Ole", "Gunnar", "Torsten", "Rune", "Jarl", "Per" };
    private static readonly string[] ScanAdultF = { "Ingrid", "Kristin", "Astrid", "Marit", "Helle", "Bodil" };

    private static readonly string[] NlYoungM = { "Sander", "Bram", "Daan", "Ruben", "Thijs", "Jeroen", "Bas", "Rick", "Stijn" };
    private static readonly string[] NlYoungF = { "Femke", "Anouk", "Sanne", "Lotte", "Iris", "Marloes" };
    private static readonly string[] NlAdultM = { "Joost", "Willem", "Dirk", "Hendrik", "Maarten", "Pieter", "Kees", "Bert" };
    private static readonly string[] NlAdultF = { "Marieke", "Annelies", "Saskia", "Ellen", "Ineke" };

    /// <summary>2005 WoW skewed male — but keep women in the fleet or the Barrens reads wrong.</summary>
    private const double MaleShare = 0.72;

    public static string SampleGivenName(Random rng, string region, int age)
    {
        bool male = rng.NextDouble() < MaleShare;

        string[] pool = region switch
        {
            "UK" or "Ireland" => age <= 23
                ? (male ? BritYoungM : BritYoungF)
                : (male ? BritAdultM : BritAdultF),

            "Germany" => age <= 23
                ? (male ? GerYoungM : GerYoungF)
                : (male ? GerAdultM : GerAdultF),

            "Scandinavia" => age <= 23
                ? (male ? ScanYoungM : ScanYoungF)
                : (male ? ScanAdultM : ScanAdultF),

            "Netherlands" => age <= 23
                ? (male ? NlYoungM : NlYoungF)
                : (male ? NlAdultM : NlAdultF),

            // US-*, Canada, Australia — cohort-gated, because a first name is a birth-year stamp
            _ => age switch
            {
                <= 18 => male ? AngloTeenM : AngloTeenF,
                <= 30 => male ? AngloTwentiesM : AngloTwentiesF,
                <= 45 => male ? AngloAdultM : AngloAdultF,
                _ => male ? AngloOlderM : AngloOlderF,
            }
        };

        return pool[rng.Next(pool.Length)];
    }

    // ==================== Occupations (40, age-gated) ====================

    public sealed record Occupation(string Category, int MinAge, int MaxAge);

    public static readonly Occupation[] Occupations =
    {
        new("middle school student", 13, 15),
        new("high school student", 14, 18),
        new("high school student with a weekend job", 15, 18),
        new("community college student", 18, 23),
        new("university student", 18, 24),
        new("grad student", 22, 30),
        new("video rental store clerk", 16, 30),
        new("game store clerk", 16, 28),
        new("record store clerk", 17, 32),
        new("pizza delivery driver", 17, 28),
        new("fast food worker", 15, 25),
        new("grocery store worker", 15, 30),
        new("mall kiosk salesperson", 17, 28),
        new("electronics store salesperson", 18, 32),
        new("internet café staff", 17, 28),
        new("call center rep", 18, 35),
        new("IT helpdesk tech", 19, 40),
        new("sysadmin", 22, 45),
        new("web designer", 19, 38),
        new("factory shift worker", 18, 50),
        new("warehouse worker", 18, 45),
        new("construction worker", 18, 48),
        new("landscaper", 17, 40),
        new("auto mechanic", 18, 50),
        new("long-haul trucker", 24, 55),
        new("national guard / reserves", 18, 38),
        new("office temp", 19, 40),
        new("receptionist", 18, 45),
        new("dental office assistant", 20, 45),
        new("night-shift nurse", 23, 50),
        new("school teacher", 24, 55),
        new("bank teller", 20, 40),
        new("accountant", 25, 58),
        new("insurance adjuster", 25, 55),
        new("real estate agent", 26, 58),
        new("stay-at-home parent", 24, 45),
        new("night security guard", 21, 55),
        new("small business owner", 28, 58),
        new("between jobs right now", 18, 45),
        new("recently retired", 50, 58),
    };

    private static string SampleOccupation(Random rng, int age)
    {
        var eligible = Occupations.Where(o => age >= o.MinAge && age <= o.MaxAge).ToArray();
        return eligible.Length > 0 ? eligible[rng.Next(eligible.Length)].Category : "between jobs right now";
    }

    // ==================== Interests (60, 2005-safe) ====================

    public static readonly string[] Interests =
    {
        "metal music", "punk rock", "emo bands", "classic rock", "rap", "country music",
        "playing guitar", "playing drums", "their garage band", "DDR at the arcade",
        "CS 1.6", "Halo 2", "Runescape", "Diablo 2", "Starcraft", "Warcraft 3 custom maps",
        "Madden", "Gran Turismo", "GameCube games", "emulators and ROMs",
        "skateboarding", "BMX", "basketball", "football", "baseball", "hockey", "soccer",
        "WWE wrestling", "UFC", "paintball", "airsoft", "fishing", "hunting", "camping",
        "working on their car", "import tuners", "motorcycles",
        "anime", "Dragonball Z", "Naruto", "manga", "comic books",
        "D&D", "Magic: The Gathering", "Warhammer minis", "Lord of the Rings",
        "Star Wars", "sci-fi novels", "fantasy novels", "horror movies", "kung fu movies",
        "Lost (the show)", "Family Guy", "South Park", "poker night", "pool at the bar",
        "bowling league", "lifting at the gym", "scrapbooking", "gardening",
    };

    private static List<string> SampleInterests(Random rng, int age)
    {
        int count = rng.Next(2, 5);   // 2–4 per §6.3
        var pool = Interests.OrderBy(_ => rng.Next()).ToList();
        return pool.Take(count).ToList();
    }

    /// <summary>Jitter support (§6.4: 20% chance drop-one/add-one at assignment).</summary>
    public static string RandomInterest(Random rng) => Interests[rng.Next(Interests.Length)];

    // ==================== Gaming backgrounds (skeleton-sampled — LLM doesn't invent them) ====================

    public static readonly string[] GamingBackgrounds =
    {
        "first MMO ever, still figuring everything out",
        "hardcore EverQuest refugee, compares everything to EQ",
        "came from Runescape and won't shut up about it",
        "Diablo 2 veteran, thinks in loot runs",
        "played all the Warcraft RTS games, here for the lore",
        "Ultima Online old-timer with strong opinions",
        "friend dragged them in, hooked despite themselves",
        "read about it in a PC magazine and got curious",
        "was in the beta and mentions it constantly",
        "mostly played console games before this",
    };

    public static readonly string[] Humors = { "dry", "goofy", "gentle", "sarcastic", "deadpan", "corny" };

    // ==================== Swear register (§6.2 v2) ====================

    /// <summary>P(level 0), P(1), P(2), P(3) per age band. Teenagers and young adults
    /// swore the most online in 2005; the 46+ band is where "darn" actually lived.</summary>
    private static readonly Dictionary<string, float[]> SwearWeights = new()
    {
        ["13-15"] = new[] { 0.10f, 0.30f, 0.40f, 0.20f },
        ["16-18"] = new[] { 0.05f, 0.25f, 0.40f, 0.30f },
        ["19-23"] = new[] { 0.10f, 0.30f, 0.40f, 0.20f },
        ["24-30"] = new[] { 0.15f, 0.40f, 0.35f, 0.10f },
        ["31-45"] = new[] { 0.20f, 0.45f, 0.30f, 0.05f },
        ["46+"] = new[] { 0.40f, 0.45f, 0.15f, 0.00f },
    };

    /// <summary>Band-weighted draw, nudged by temperament: hot-tempered people swear more,
    /// warm even-tempered people swear less. Clamped 0–3.</summary>
    public static int SampleSwearLevel(Random rng, AgeBand band, PersonaDisposition d)
    {
        var w = SwearWeights.TryGetValue(band.Name, out var found) ? found : SwearWeights["19-23"];

        double roll = rng.NextDouble(), acc = 0;
        int lvl = w.Length - 1;
        for (int i = 0; i < w.Length; i++)
        {
            acc += w[i];
            if (roll < acc) { lvl = i; break; }
        }

        if (d.Irritability > 0.65f && rng.NextDouble() < d.Irritability - 0.5f) lvl++;
        if (d.Warmth > 0.75f && d.Irritability < 0.30f && rng.NextDouble() < 0.35) lvl--;

        return Math.Clamp(lvl, 0, 3);
    }

    // ==================== Per-age typing distributions (§6.3: younger → sloppier + faster) ====================

    private static PersonaTyping SampleTyping(Random rng, AgeBand band, PersonaDisposition disposition)
    {
        // (capsWeights: lower/proper/mixed/CRUISE), abbrev range, wpm mean/spread, typo range
        var (caps, abMin, abMax, wpmMean, wpmSpread, tpMin, tpMax) = band.Name switch
        {
            "13-15" => (new[] { 0.75f, 0.00f, 0.15f, 0.10f }, 2, 3, 55f, 10f, 0.04f, 0.09f),
            "16-18" => (new[] { 0.60f, 0.10f, 0.22f, 0.08f }, 2, 3, 60f, 12f, 0.03f, 0.08f),
            "19-23" => (new[] { 0.48f, 0.25f, 0.24f, 0.03f }, 1, 3, 58f, 12f, 0.02f, 0.06f),
            "24-30" => (new[] { 0.25f, 0.45f, 0.28f, 0.02f }, 1, 2, 52f, 12f, 0.02f, 0.05f),
            "31-45" => (new[] { 0.10f, 0.68f, 0.20f, 0.02f }, 0, 2, 42f, 10f, 0.01f, 0.04f),
            _ => (new[] { 0.05f, 0.85f, 0.10f, 0.00f }, 0, 1, 32f, 8f, 0.01f, 0.04f),
        };

        string capsStyle = WeightedPick(rng, caps, new[] { "lower", "proper", "mixed", "CRUISE" });
        string punct = capsStyle == "proper"
            ? (rng.NextDouble() < 0.7 ? "normal" : "heavy")
            : (rng.NextDouble() < 0.7 ? "minimal" : "normal");

        int thinkMin = band.Min <= 18 ? rng.Next(1, 3) : rng.Next(2, 5);
        int thinkMax = thinkMin + (band.Min <= 18 ? rng.Next(2, 5) : rng.Next(4, 9));

        return new PersonaTyping
        {
            Caps = capsStyle,
            Punctuation = punct,
            AbbrevLevel = rng.Next(abMin, abMax + 1),
            SwearLevel = SampleSwearLevel(rng, band, disposition),
            TypoRate = tpMin + (float)rng.NextDouble() * (tpMax - tpMin),
            Wpm = Math.Max(18, (int)(wpmMean + ((float)rng.NextDouble() * 2 - 1) * wpmSpread)),
            ThinkMinS = thinkMin,
            ThinkMaxS = thinkMax,
            SplitThresholdChars = band.Min <= 18 ? rng.Next(60, 95) : rng.Next(90, 140),
            AltTabChance = 0.03f + (float)rng.NextDouble() * 0.09f,
            Tics = SampleTics(rng, band)
        };
    }

    private static readonly string[] YoungTics = { "lol", "dude", "man", "yo", "xD", "!!", "haha" };
    private static readonly string[] AdultTics = { "haha", "heh", ":)", "hah", "hm", "...", "lol" };

    private static List<string> SampleTics(Random rng, AgeBand band)
    {
        var pool = band.Min <= 20 ? YoungTics : AdultTics;
        return pool.OrderBy(_ => rng.Next()).Take(rng.Next(1, 4)).ToList();
    }

    private static string WeightedPick(Random rng, float[] weights, string[] values)
    {
        double roll = rng.NextDouble(), acc = 0;
        for (int i = 0; i < weights.Length; i++) { acc += weights[i]; if (roll < acc) return values[i]; }
        return values[0];
    }
}