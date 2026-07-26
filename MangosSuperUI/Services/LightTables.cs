// LightTables.cs — vanilla's authored exterior lighting chain.
//
// ═══════════════════════════════════════════════════════════════════════════
// PORTED FROM MSUIClient
//   Formats/DbcReader.cs        the four Light tables, the unit conversions
//   World/ExteriorLighting.cs   the coordinate convention and the falloff model
//   SYSTEM_EXTERIOR_LIGHTING.md why each of those is what it is
// ═══════════════════════════════════════════════════════════════════════════
//
//   Light.dbc            which lighting setup applies where, and how it fades
//     -> LightParams.dbc       one setting-set per weather state
//        -> LightIntBand.dbc     18 colour curves over the day
//        -> LightFloatBand.dbc    6 scalar curves over the day
//
// THIS FILE PARSES AND EMITS; IT DOES NOT RESOLVE. The blend across contributing
// zones and the sampling of a curve at a time of day both happen in the BROWSER
// (wwwroot/js/worldeditor/world-lighting.js), because both change continuously
// as the camera moves and the clock runs, and a round trip per frame is not a
// lighting model. What the server owns is the file format, the unit conversions
// and the coordinate convention — the three things that are settled facts.
//
// TWO UNIT TRAPS, both of which produce results that look like bad data rather
// than like a bug. Both are undone HERE, at the reader boundary, so nothing
// downstream has to remember:
//   * Positions, falloff radii and fog end are stored YARDS x 36.
//   * Band times are HALF-MINUTES from midnight, 0..2880. Emitted as hours.
//
// Field indices come from wowdev.wiki and are VERIFIED against the record size
// on load, not trusted. GroundEffectTexture records what a single wrong column
// costs: every recipe silently pinned to a fallback density, visible only as
// "the grass looks too thick". A wrong column here reads as slightly wrong
// colours, which is worse — so each table checks its shape and refuses to load
// rather than emit plausible nonsense.

using System;
using System.Collections.Generic;
using System.Linq;

namespace MangosSuperUI.Services;

/// <summary>One Light.dbc row: where a lighting setup applies, and how it fades.</summary>
public sealed class LightZone
{
    public uint Id;
    public uint MapId;

    /// <summary>Raw DBC position, x36 already undone. Still in DBC space.</summary>
    public float RawX, RawY, RawZ;

    /// <summary>Inside this radius the zone applies at full strength. Yards.</summary>
    public float FalloffStart;

    /// <summary>Past this radius the zone does not apply at all. Yards.</summary>
    public float FalloffEnd;

    public uint ParamsClear;
    public uint ParamsClearWater;
    public uint ParamsStorm;
    public uint ParamsStormWater;
    public uint ParamsDeath;

    /// <summary>
    /// A row at the origin with no radius is the MAP-WIDE DEFAULT rather than a
    /// zone at the map origin. Everything else blends on top of it.
    ///
    /// Worth stating loudly because it surprised MSUIClient's first reading:
    /// Elwynn Forest has NO dedicated Light.dbc row. At Northshire the map
    /// default is the only thing that applies, and that is correct, not a bug.
    /// The map default IS outdoor Azeroth's lighting; positioned rows are
    /// exceptions layered on top, and they are small — measured reaches near
    /// Northshire are 495, 250, 90, 85 and 76 yards.
    /// </summary>
    public bool IsMapDefault =>
        FalloffEnd <= 0f && (RawX * RawX + RawY * RawY + RawZ * RawZ) <= 0.001f;
}

/// <summary>Light.dbc — vanilla 1.12 layout is 12 fields / 48 bytes.</summary>
public sealed class LightTable
{
    public const string MpqPath = @"DBFilesClient\Light.dbc";

    /// <summary>Stored x36. Undone at parse so callers only ever see yards.</summary>
    public const float DbcDistanceScale = 36f;

    private readonly List<LightZone> _zones = new();
    public IReadOnlyList<LightZone> Zones => _zones;
    public int Count => _zones.Count;
    public string Shape { get; private set; } = "";

    public static LightTable? Parse(byte[] data, List<string> notes)
    {
        var dbc = WowDbcFile.Parse(data);
        if (dbc is null) { notes.Add("Light.dbc: not a WDBC file"); return null; }

        // 12 fields in 1.12; WotLK grew three more phase params on the end.
        // Fewer than 12 means the layout below is not this file's layout.
        if (dbc.FieldCount < 12)
        {
            notes.Add($"Light: {dbc.FieldCount} field(s) — expected at least 12 for the 1.12 " +
                      "layout. NOT LOADED; exterior lighting stays on its constants.");
            return null;
        }

        var table = new LightTable();
        for (int r = 0; r < dbc.RecordCount; r++)
        {
            var zone = new LightZone
            {
                Id = dbc.GetUInt(r, 0),
                MapId = dbc.GetUInt(r, 1),
                RawX = dbc.GetFloat(r, 2) / DbcDistanceScale,
                RawY = dbc.GetFloat(r, 3) / DbcDistanceScale,
                RawZ = dbc.GetFloat(r, 4) / DbcDistanceScale,
                FalloffStart = dbc.GetFloat(r, 5) / DbcDistanceScale,
                FalloffEnd = dbc.GetFloat(r, 6) / DbcDistanceScale,
                ParamsClear = dbc.GetUInt(r, 7),
                ParamsClearWater = dbc.GetUInt(r, 8),
                ParamsStorm = dbc.GetUInt(r, 9),
                ParamsStormWater = dbc.GetUInt(r, 10),
                ParamsDeath = dbc.GetUInt(r, 11),
            };
            if (zone.Id != 0) table._zones.Add(zone);
        }

        int defaults = table._zones.Count(z => z.IsMapDefault);
        table.Shape = $"Light: {dbc.RecordCount} record(s), {dbc.FieldCount} field(s), " +
                      $"{dbc.RecordSize} bytes; {table._zones.Count} zone(s), {defaults} map default(s)";
        notes.Add(table.Shape);
        return table;
    }

    /// <summary>Zones on one map, map default first so it can be the blend base.</summary>
    public List<LightZone> ForMap(uint mapId)
        => _zones.Where(z => z.MapId == mapId)
                 .OrderByDescending(z => z.IsMapDefault)
                 .ToList();

    public LightZone? ById(uint id) => _zones.FirstOrDefault(z => z.Id == id);
}

/// <summary>One LightParams.dbc row.</summary>
public sealed class LightParamsRow
{
    public uint Id;
    public bool HighlightSky;
    public uint SkyboxId;
    public float Glow;
    public float WaterShallowAlpha;
    public float WaterDeepAlpha;
    public float OceanShallowAlpha;
    public float OceanDeepAlpha;
    public uint Flags;
}

/// <summary>LightParams.dbc — 9 fields / 36 bytes in 1.12.</summary>
public sealed class LightParamsTable
{
    public const string MpqPath = @"DBFilesClient\LightParams.dbc";

    private readonly Dictionary<uint, LightParamsRow> _byId = new();
    public int Count => _byId.Count;
    public string Shape { get; private set; } = "";
    public LightParamsRow? Get(uint id) => _byId.TryGetValue(id, out var p) ? p : null;

    public static LightParamsTable? Parse(byte[] data, List<string> notes)
    {
        var dbc = WowDbcFile.Parse(data);
        if (dbc is null) { notes.Add("LightParams.dbc: not a WDBC file"); return null; }

        if (dbc.FieldCount < 9)
        {
            notes.Add($"LightParams: {dbc.FieldCount} field(s) — expected 9. NOT LOADED.");
            return null;
        }

        var table = new LightParamsTable();
        for (int r = 0; r < dbc.RecordCount; r++)
        {
            uint id = dbc.GetUInt(r, 0);
            if (id == 0) continue;
            table._byId[id] = new LightParamsRow
            {
                Id = id,
                HighlightSky = dbc.GetUInt(r, 1) != 0,
                SkyboxId = dbc.GetUInt(r, 2),
                Glow = dbc.GetFloat(r, 3),
                WaterShallowAlpha = dbc.GetFloat(r, 4),
                WaterDeepAlpha = dbc.GetFloat(r, 5),
                OceanShallowAlpha = dbc.GetFloat(r, 6),
                OceanDeepAlpha = dbc.GetFloat(r, 7),
                Flags = dbc.GetUInt(r, 8),
            };
        }

        table.Shape = $"LightParams: {dbc.RecordCount} record(s), {dbc.FieldCount} field(s), " +
                      $"{dbc.RecordSize} bytes; {table._byId.Count} usable";
        notes.Add(table.Shape);
        return table;
    }
}

/// <summary>
/// One band row: up to 16 (time, value) keys forming a curve over the day.
///
/// Times arrive as half-minutes 0..2880 and are stored here as HOURS, so
/// everything downstream speaks one unit. The curve WRAPS — the segment from the
/// last key to the first crosses midnight, and a band that does not wrap
/// produces a hard snap at 00:00 that reads as a rendering glitch.
///
/// COLOUR values stay PACKED. That separation is not tidiness, it is a bug fix:
/// interpolating a packed 0x00RRGGBB as a single number carries across the byte
/// boundaries and lands on a colour belonging to neither key. MSUIClient's
/// symptom at 11:11 was green ambient, cyan fog and a dark-purple sun, while
/// every scalar band in the same rows read perfectly — and that asymmetry WAS
/// the diagnosis. The browser decodes both bracketing keys and interpolates per
/// channel; nothing here or there may reintroduce a shared sampler.
/// </summary>
public sealed class LightBandRow
{
    public float[] Hours = Array.Empty<float>();
    public uint[] Raw = Array.Empty<uint>();          // packed colour, or float bits
    public bool HasData => Hours.Length > 0;
}

/// <summary>
/// Shared layout for LightIntBand and LightFloatBand: id, entry count, 16 times,
/// 16 values. 34 fields / 136 bytes. Only the value type differs.
/// </summary>
public static class LightBandLayout
{
    public const int FieldCount = 34;
    public const int MaxEntries = 16;
    public const int TimeField = 2;
    public const int ValueField = 18;

    /// <summary>Half-minutes from midnight, 0..2880, to hours.</summary>
    public const float HalfMinutesPerHour = 120f;

    public static LightBandRow ReadBand(WowDbcFile dbc, int row)
    {
        // numEntries is trusted only as far as the array allows; a row claiming
        // more than 16 keys is clamped rather than throwing — a corrupt count
        // should cost a band, not the request.
        int n = Math.Clamp(dbc.GetInt(row, 1), 0, MaxEntries);
        var hours = new float[n];
        var raw = new uint[n];
        for (int i = 0; i < n; i++)
        {
            hours[i] = dbc.GetInt(row, TimeField + i) / HalfMinutesPerHour;
            raw[i] = dbc.GetUInt(row, ValueField + i);
        }
        return new LightBandRow { Hours = hours, Raw = raw };
    }
}

/// <summary>
/// LightIntBand.dbc — the 18 colour curves per LightParams.
///
/// Band rows for LightParams P are ids `P*18-17 .. P*18`, i.e. band b is
/// id `P*18-17+b`. Looked up BY ID rather than by row index: the two usually
/// coincide and relying on that is exactly the assumption that breaks quietly.
///
/// The arithmetic is also the cheapest correctness check there is. In 1.12.1
/// data the shapes are 7668 int-band rows against 426 LightParams — and
/// 7668 = 426 x 18 exactly, as 2556 = 426 x 6 does for the float bands. Verify
/// that first if anything ever looks wrong.
/// </summary>
public sealed class LightIntBandTable
{
    public const string MpqPath = @"DBFilesClient\LightIntBand.dbc";

    /// <summary>Names for the 18 slots, so a readout reads as English.</summary>
    public static readonly string[] BandNames =
    {
        "global diffuse", "global ambient",
        "sky top", "sky middle", "sky band 1", "sky band 2", "sky smog",
        "fog", "sun", "cloud sun", "cloud emissive",
        "cloud L1 ambient", "cloud L2 ambient",
        "ocean close", "ocean far", "river close", "river far",
        "shadow opacity",
    };

    public const int BandsPerParams = 18;

    private readonly Dictionary<uint, LightBandRow> _byId = new();
    public int Count => _byId.Count;
    public string Shape { get; private set; } = "";

    /// <summary>Band b (0..17) for a LightParams id, or null when unauthored.</summary>
    public LightBandRow? Band(uint lightParamsId, int band)
    {
        if (lightParamsId == 0 || band < 0 || band >= BandsPerParams) return null;
        uint id = lightParamsId * BandsPerParams - 17 + (uint)band;
        return _byId.TryGetValue(id, out var b) && b.HasData ? b : null;
    }

    public static LightIntBandTable? Parse(byte[] data, List<string> notes)
    {
        var dbc = WowDbcFile.Parse(data);
        if (dbc is null) { notes.Add("LightIntBand.dbc: not a WDBC file"); return null; }

        if (dbc.FieldCount < LightBandLayout.FieldCount)
        {
            notes.Add($"LightIntBand: {dbc.FieldCount} field(s) — expected " +
                      $"{LightBandLayout.FieldCount}. NOT LOADED.");
            return null;
        }

        var table = new LightIntBandTable();
        int withData = 0;
        for (int r = 0; r < dbc.RecordCount; r++)
        {
            uint id = dbc.GetUInt(r, 0);
            if (id == 0) continue;
            var band = LightBandLayout.ReadBand(dbc, r);
            table._byId[id] = band;
            if (band.HasData) withData++;
        }

        table.Shape = $"LightIntBand: {dbc.RecordCount} record(s), {dbc.FieldCount} field(s), " +
                      $"{dbc.RecordSize} bytes; {withData} band(s) with keys";
        notes.Add(table.Shape);
        return table;
    }
}

/// <summary>
/// LightFloatBand.dbc — the 6 scalar curves per LightParams.
/// Band rows for LightParams P are ids `P*6-5 .. P*6`.
///
///   0 fog end (x36)   1 fog start MULTIPLIER   2 celestial glow through
///   3 cloud density   4-5 unknown
///
/// Band 1 is NOT a distance. It is a 0..0.999 multiplier and fog start is
/// `end * mult` — Azeroth at noon is end 500, mult 0.25, so start 125. Keeping
/// it as a multiplier keeps the authored relationship between the two rather
/// than flattening them into two independent knobs.
/// </summary>
public sealed class LightFloatBandTable
{
    public const string MpqPath = @"DBFilesClient\LightFloatBand.dbc";

    public static readonly string[] BandNames =
    {
        "fog end", "fog start multiplier", "celestial glow through",
        "cloud density", "unknown 4", "unknown 5",
    };

    public const int BandsPerParams = 6;
    public const int FogEndBand = 0;
    public const int FogStartMultiplierBand = 1;

    private readonly Dictionary<uint, LightBandRow> _byId = new();
    public int Count => _byId.Count;
    public string Shape { get; private set; } = "";

    public LightBandRow? Band(uint lightParamsId, int band)
    {
        if (lightParamsId == 0 || band < 0 || band >= BandsPerParams) return null;
        uint id = lightParamsId * BandsPerParams - 5 + (uint)band;
        return _byId.TryGetValue(id, out var b) && b.HasData ? b : null;
    }

    public static LightFloatBandTable? Parse(byte[] data, List<string> notes)
    {
        var dbc = WowDbcFile.Parse(data);
        if (dbc is null) { notes.Add("LightFloatBand.dbc: not a WDBC file"); return null; }

        if (dbc.FieldCount < LightBandLayout.FieldCount)
        {
            notes.Add($"LightFloatBand: {dbc.FieldCount} field(s) — expected " +
                      $"{LightBandLayout.FieldCount}. NOT LOADED.");
            return null;
        }

        var table = new LightFloatBandTable();
        int withData = 0;
        for (int r = 0; r < dbc.RecordCount; r++)
        {
            uint id = dbc.GetUInt(r, 0);
            if (id == 0) continue;
            var band = LightBandLayout.ReadBand(dbc, r);
            table._byId[id] = band;
            if (band.HasData) withData++;
        }

        table.Shape = $"LightFloatBand: {dbc.RecordCount} record(s), {dbc.FieldCount} field(s), " +
                      $"{dbc.RecordSize} bytes; {withData} band(s) with keys";
        notes.Add(table.Shape);
        return table;
    }
}

/// <summary>
/// Loads the four Light DBCs once per process and hands out what a map needs.
/// </summary>
public static class ExteriorLightData
{
    private static readonly object _lock = new();
    private static bool _attempted;
    private static readonly List<string> _notes = new();

    public static LightTable? Lights { get; private set; }
    public static LightParamsTable? Params { get; private set; }
    public static LightIntBandTable? IntBands { get; private set; }
    public static LightFloatBandTable? FloatBands { get; private set; }

    public static bool Ready => Lights is not null && Params is not null &&
                                IntBands is not null && FloatBands is not null;

    public static IReadOnlyList<string> Notes { get { lock (_lock) { return _notes.ToArray(); } } }

    public static bool Load(string clientDataPath)
    {
        lock (_lock)
        {
            if (_attempted) return Ready;
            _attempted = true;

            var light = AdtTerrainReader.ReadFileFromMpqs(clientDataPath, LightTable.MpqPath);
            var lparams = AdtTerrainReader.ReadFileFromMpqs(clientDataPath, LightParamsTable.MpqPath);
            var ints = AdtTerrainReader.ReadFileFromMpqs(clientDataPath, LightIntBandTable.MpqPath);
            var floats = AdtTerrainReader.ReadFileFromMpqs(clientDataPath, LightFloatBandTable.MpqPath);

            if (light is null || lparams is null || ints is null || floats is null)
            {
                _notes.Add("one or more Light DBCs not found in the MPQs — " +
                           "exterior lighting stays on its constants");
                return false;
            }

            Lights = LightTable.Parse(light, _notes);
            Params = LightParamsTable.Parse(lparams, _notes);
            IntBands = LightIntBandTable.Parse(ints, _notes);
            FloatBands = LightFloatBandTable.Parse(floats, _notes);

            if (!Ready)
            {
                _notes.Add("a Light DBC failed to parse — exterior lighting stays on its constants");
                return false;
            }

            // The row-count arithmetic is the cheapest possible check that the
            // id mapping is right. It is stated rather than assumed.
            if (Params!.Count > 0)
                _notes.Add($"band arithmetic: {IntBands!.Count} int rows / 18 = " +
                           $"{IntBands.Count / 18.0:F1} vs {Params.Count} LightParams; " +
                           $"{FloatBands!.Count} float rows / 6 = {FloatBands.Count / 6.0:F1}");

            _notes.Add(SelfTest());
            return true;
        }
    }

    // ── The coordinate convention ───────────────────────────────────────────
    //
    // SETTLED, and settled decisively — this is not re-derived here.
    //
    // The DBC stores positions Y-UP and in POSITIVE map space; ours are Z-up and
    // centred. The zone extent gave the first half away in one line: measured
    // X 3200..32800, Y -234..436, Z 13208..32800 — Y is the small axis, so Y is
    // height, and the horizontal range sits inside 0..34133 = 64 tiles x 533.33.
    //
    // MSUIClient scored six candidate mappings against a known player position
    // and this one won by 6.4x. But the margin is not the proof. The proof is:
    //
    //   Light 77 is at raw (16488, 0, 25868), which maps to world (-8801, 579).
    //   That is Stormwind City, within ~20 yards of where it actually stands.
    //
    // A wrong convention cannot land a named landmark on itself. SelfTest below
    // re-runs exactly that check against the user's own file every load, so a
    // different data set cannot silently inherit the conclusion.
    public const float MapHalfYards = 17066.666f;

    /// <summary>DBC space -> our WoW world yards (X north, Y west, Z up).</summary>
    public static (float X, float Y, float Z) ToWorld(LightZone z)
        => (MapHalfYards - z.RawZ, MapHalfYards - z.RawX, z.RawY);

    /// <summary>Stormwind is where Stormwind is, or the convention is wrong.</summary>
    private static string SelfTest()
    {
        var zone = Lights?.ById(77);
        if (zone is null) return "convention self-test: Light 77 not present in this data";

        var (x, y, _) = ToWorld(zone);
        // Stormwind City, from MSUIClient's own measurement.
        const float expectX = -8801f, expectY = 579f;
        float err = MathF.Sqrt((x - expectX) * (x - expectX) + (y - expectY) * (y - expectY));
        return err <= 60f
            ? $"convention self-test OK: Light 77 -> ({x:F0}, {y:F0}), {err:F0} yd from Stormwind"
            : $"convention self-test FAILED: Light 77 -> ({x:F0}, {y:F0}), {err:F0} yd from the " +
              "expected Stormwind position — the coordinate convention does not fit this data " +
              "and every zone light will apply in the wrong place";
    }
}
