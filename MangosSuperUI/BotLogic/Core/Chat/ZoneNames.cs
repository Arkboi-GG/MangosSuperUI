namespace MangosSuperUI.BotLogic.Chat.Core;

/// <summary>
/// Vanilla 1.12 zone names by AreaTable zone id — feeds the prompt's in-game snapshot
/// line (§10.3's example: "grinding murlocs in westfall"). Without a real zone name the
/// model invents a location (and with a realm literally named "Barrens Chat" in the
/// system frame, it invents THE BARRENS — observed live, 2026-07-07).
/// Static and self-contained on purpose; unknown ids return "" and the snapshot simply
/// omits the zone. Swap for a DBC-backed source later if preferred — one call site.
/// </summary>
public static class ZoneNames
{
    public static string Get(int zoneId) =>
        Names.TryGetValue(zoneId, out var n) ? n : "";

    private static readonly Dictionary<int, string> Names = new()
    {
        // ── Eastern Kingdoms ──
        [1] = "Dun Morogh",
        [3] = "the Badlands",
        [4] = "the Blasted Lands",
        [8] = "the Swamp of Sorrows",
        [10] = "Duskwood",
        [11] = "the Wetlands",
        [12] = "Elwynn Forest",
        [28] = "Western Plaguelands",
        [33] = "Stranglethorn Vale",
        [36] = "Alterac Mountains",
        [38] = "Loch Modan",
        [40] = "Westfall",
        [41] = "Deadwind Pass",
        [44] = "Redridge Mountains",
        [45] = "Arathi Highlands",
        [46] = "the Burning Steppes",
        [47] = "the Hinterlands",
        [51] = "Searing Gorge",
        [85] = "Tirisfal Glades",
        [130] = "Silverpine Forest",
        [139] = "Eastern Plaguelands",
        [267] = "Hillsbrad Foothills",
        [1377] = "Silithus",
        [1497] = "Undercity",
        [1519] = "Stormwind",
        [1537] = "Ironforge",

        // ── Kalimdor ──
        [14] = "Durotar",
        [15] = "Dustwallow Marsh",
        [16] = "Azshara",
        [17] = "the Barrens",
        [141] = "Teldrassil",
        [148] = "Darkshore",
        [215] = "Mulgore",
        [331] = "Ashenvale",
        [357] = "Feralas",
        [361] = "Felwood",
        [400] = "Thousand Needles",
        [405] = "Desolace",
        [406] = "the Stonetalon Mountains",
        [440] = "Tanaris",
        [490] = "Un'Goro Crater",
        [493] = "Moonglade",
        [618] = "Winterspring",
        [1637] = "Orgrimmar",
        [1638] = "Thunder Bluff",
        [1657] = "Darnassus",
    };
}
