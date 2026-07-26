// GroundEffectTables.cs — the vanilla ground-effect (foliage) DBC chain.
//
// ═══════════════════════════════════════════════════════════════════════════
// PORTED FROM MSUIClient Formats/DbcReader.cs
//   DbcFile, GroundEffectDoodadTable, GroundEffectTextureTable
// See MSUIClient/SYSTEM_FOLIAGE.md for why each decision is what it is.
// ═══════════════════════════════════════════════════════════════════════════
//
// The authored chain, which the renderer must FOLLOW rather than approximate:
//
//   MCLY.EffectId                     (per texture layer, per MCNK chunk)
//     -> GroundEffectTexture.dbc      (up to 4 doodad IDs + weights + a density)
//        -> GroundEffectDoodad.dbc    (the grass M2 model path)
//
// The WDBC reader lives in WowDbcFile.cs, shared with the Light tables.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MangosSuperUI.Services;

/// <summary>
/// Broad clutter categories, read from a ground-effect model's name code — the
/// 2-3 letter tag just before the trailing number (ElwGra01 -> Grass,
/// ElwRoc01 -> Rock, ApkBus01 -> Bush). Retail hand-curated which of these
/// appeared where — most visibly, it kept road pebbles out of the starting
/// zones — and the raw DBCs don't encode that. A per-kind toggle lets that
/// curation be reproduced by hand instead of scattering everything the data
/// technically allows.
/// </summary>
public enum FoliageKind { Grass, Flower, Bush, Rock, Plant, Mushroom, Other }

/// <summary>
/// GroundEffectDoodad.dbc — maps a ground-effect doodad ID to its grass/flower
/// M2 model path. Vanilla layout has an ID, an internal tag, the model filename
/// (stringref), flags and a couple of floats, and the exact field order shifted
/// across versions — so rather than hard-code an offset we SCAN each field for
/// the one stringref that resolves to a model path (.mdx/.m2/.mdl). Robust and
/// self-verifying; the record size is reported so a wrong parse is visible.
/// </summary>
public sealed class GroundEffectDoodadTable
{
    public const string MpqPath = @"DBFilesClient\GroundEffectDoodad.dbc";

    private readonly Dictionary<uint, string> _models = new();
    public int Count => _models.Count;
    public string? Model(uint id) => _models.TryGetValue(id, out var m) ? m : null;

    /// <summary>Shape of the file that was parsed, for the endpoint's notes.</summary>
    public string Shape { get; private set; } = "";

    public static GroundEffectDoodadTable? Parse(byte[] data)
    {
        var dbc = WowDbcFile.Parse(data);
        if (dbc is null) return null;

        var table = new GroundEffectDoodadTable();
        for (int r = 0; r < dbc.RecordCount; r++)
        {
            uint id = dbc.GetUInt(r, 0);
            string model = "";
            for (int f = 1; f < dbc.FieldCount; f++)
            {
                string? s = dbc.GetStringIfStart(r, f);
                if (s is { Length: > 3 } &&
                    (s.EndsWith(".mdx", StringComparison.OrdinalIgnoreCase) ||
                     s.EndsWith(".m2", StringComparison.OrdinalIgnoreCase) ||
                     s.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase)))
                { model = s; break; }
            }
            if (id != 0 && model.Length > 0) table._models[id] = model;
        }

        table.Shape = $"GroundEffectDoodad: {dbc.RecordCount} record(s), {dbc.FieldCount} field(s), " +
                      $"{dbc.RecordSize} bytes; {table._models.Count} with a model path";
        return table;
    }
}

/// <summary>One GroundEffectTexture row, resolved to model paths + weights.</summary>
public sealed class GroundEffectRecipe
{
    public (string Model, int Weight)[] Doodads = Array.Empty<(string, int)>();
    public int Density = 1;
}

/// <summary>
/// GroundEffectTexture.dbc — a ground-effect ID (from MCLY.EffectId) gives up to
/// four GroundEffectDoodad IDs plus a density (doodads scattered per cell). Two
/// column layouts exist and the field count tells them apart:
///
///   7 fields  (vanilla 1.12): ID, DoodadId[4], Density, Sound   - NO weights
///   11 fields (WotLK+):       ID, DoodadId[4], Weight[4], Density, Sound
///
/// A 1.12 client loads the 7-field file, so density lives at field 5, not 9, and
/// there are no per-doodad weights (each doodad is equally likely). Reading the
/// wrong column left every recipe pinned to a fallback density, which scattered
/// grass far denser and more uniform than the data intends — visible only as
/// "the grass looks too thick", which is exactly why the shape is reported.
/// </summary>
public sealed class GroundEffectTextureTable
{
    public const string MpqPath = @"DBFilesClient\GroundEffectTexture.dbc";

    private readonly Dictionary<uint, GroundEffectRecipe> _byId = new();
    public int Count => _byId.Count;
    public IEnumerable<KeyValuePair<uint, GroundEffectRecipe>> All => _byId;

    public string Shape { get; private set; } = "";

    public GroundEffectRecipe? Get(int effectId)
        => effectId > 0 && _byId.TryGetValue((uint)effectId, out var r) ? r : null;

    public static GroundEffectTextureTable? Parse(byte[] data, GroundEffectDoodadTable doodads)
    {
        var dbc = WowDbcFile.Parse(data);
        if (dbc is null) return null;

        var table = new GroundEffectTextureTable();

        bool hasWeights = dbc.FieldCount >= 11;
        int densityField = hasWeights ? 9 : 5;

        for (int r = 0; r < dbc.RecordCount; r++)
        {
            uint id = dbc.GetUInt(r, 0);
            if (id == 0) continue;

            var list = new List<(string, int)>(4);
            for (int i = 0; i < 4; i++)
            {
                uint doodadId = dbc.GetUInt(r, 1 + i);
                if (doodadId == 0 || doodadId == 0xFFFFFFFF) continue;   // 0 / -1 = empty slot
                string? model = doodads.Model(doodadId);
                if (model is null) continue;
                int weight = hasWeights ? dbc.GetInt(r, 5 + i) : 1;
                list.Add((model, Math.Max(weight, 1)));
            }
            if (list.Count == 0) continue;

            int density = dbc.FieldCount > densityField ? dbc.GetInt(r, densityField) : 1;
            table._byId[id] = new GroundEffectRecipe
            {
                Doodads = list.ToArray(),
                Density = Math.Max(density, 1),
            };
        }

        table.Shape = $"GroundEffectTexture: {dbc.RecordCount} record(s), {dbc.FieldCount} field(s), " +
                      $"{dbc.RecordSize} bytes; {table._byId.Count} effect(s) with resolvable doodads";
        return table;
    }
}

/// <summary>
/// Loads and caches the two ground-effect DBCs, and resolves the bare model
/// filenames they store into real MPQ paths.
/// </summary>
public static class GroundEffectData
{
    private static readonly object _lock = new();
    private static bool _attempted;
    private static GroundEffectDoodadTable? _doodads;
    private static GroundEffectTextureTable? _recipes;
    private static readonly List<string> _notes = new();

    /// <summary>Diagnostic lines from the last load attempt.</summary>
    public static IReadOnlyList<string> Notes { get { lock (_lock) { return _notes.ToArray(); } } }

    /// <summary>
    /// The recipe table, loading it on first use. Null when the DBCs are not in
    /// the MPQs — foliage is then simply absent, not an error.
    /// </summary>
    public static GroundEffectTextureTable? Recipes(string clientDataPath)
    {
        lock (_lock)
        {
            if (_attempted) return _recipes;
            _attempted = true;

            var dd = AdtTerrainReader.ReadFileFromMpqs(clientDataPath, GroundEffectDoodadTable.MpqPath);
            var dt = AdtTerrainReader.ReadFileFromMpqs(clientDataPath, GroundEffectTextureTable.MpqPath);
            if (dd is null || dt is null)
            {
                _notes.Add("GroundEffect DBC(s) not found in the MPQs — foliage disabled");
                return null;
            }

            _doodads = GroundEffectDoodadTable.Parse(dd);
            if (_doodads is null) { _notes.Add("GroundEffectDoodad parse failed"); return null; }
            _notes.Add(_doodads.Shape);

            _recipes = GroundEffectTextureTable.Parse(dt, _doodads);
            if (_recipes is null) { _notes.Add("GroundEffectTexture parse failed"); return null; }
            _notes.Add(_recipes.Shape);

            if (_recipes.Count == 0)
                _notes.Add("no usable ground-effect recipes — foliage will be empty");

            return _recipes;
        }
    }

    // GroundEffectDoodad stores BARE model filenames ("ElwGra01.mdl"), but the
    // models live under these folders in the MPQs and are .m2 there — not .mdl
    // or .mdx. The overwhelming majority are in World\NoDXT\Detail; a handful
    // sit in World\Detail. Without prepending a folder, every lookup reads from
    // the archive root and misses, so nothing scatters.
    private static readonly string[] FoliageDirs =
    {
        @"World\NoDXT\Detail\",
        @"World\Detail\",
    };

    private static readonly Dictionary<string, string?> _resolved =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Turn a GroundEffectDoodad model name into a path that actually exists in
    /// the MPQs, or null. Cached for the process lifetime — resolution costs one
    /// MPQ read per candidate and the answer never changes.
    /// </summary>
    public static string? ResolveModelPath(string clientDataPath, string name)
    {
        lock (_resolved)
        {
            if (_resolved.TryGetValue(name, out var hit)) return hit;
        }

        string? found = null;
        foreach (var cand in Candidates(name))
        {
            if (AdtTerrainReader.ReadFileFromMpqs(clientDataPath, cand) is not null)
            { found = cand; break; }
        }

        lock (_resolved) { _resolved[name] = found; }
        return found;
    }

    private static IEnumerable<string> Candidates(string path)
    {
        // As-authored first, in case a DBC ever stores a full path.
        foreach (var p in ExtVariants(path)) yield return p;

        // Bare filename (the real case here): try it under each ground-effect
        // folder. ExtVariants also swaps .mdl/.mdx for the .m2 that is actually
        // in the archive.
        bool bare = !path.Contains('\\') && !path.Contains('/');
        if (bare)
            foreach (var dir in FoliageDirs)
                foreach (var p in ExtVariants(dir + path))
                    yield return p;
    }

    private static IEnumerable<string> ExtVariants(string path)
    {
        if (path.EndsWith(".mdx", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase))
            yield return path[..^4] + ".m2";
        yield return path;
    }

    /// <summary>
    /// Map a ground-effect model path to a broad clutter kind by its name code —
    /// the letters just before the trailing number ("ElwRoc01" -> "roc" -> Rock).
    /// Zone-prefixed variants that carry an extra letter (Durotar's "DurIRo01")
    /// still land on the right 3-letter tail. Anything unrecognised is Other.
    ///
    /// Ported verbatim from MSUIClient FoliageRenderer.Classify. It lives on the
    /// SERVER so both clients classify identically — the per-kind toggles are a
    /// hand reproduction of retail's curation and they have to agree.
    /// </summary>
    public static FoliageKind Classify(string modelPath)
    {
        string name = Path.GetFileNameWithoutExtension(modelPath);
        int end = name.Length;
        while (end > 0 && char.IsDigit(name[end - 1])) end--;
        int start = Math.Max(0, end - 3);
        string code = name[start..end].ToLowerInvariant();
        return code switch
        {
            "gra" or "igr" => FoliageKind.Grass,
            "flo" or "ifl" => FoliageKind.Flower,
            "bus" or "ibu" or "scr" or "shr" => FoliageKind.Bush,
            "roc" or "iro" => FoliageKind.Rock,
            "wea" or "pla" or "tho" or "cre" or "vin" or "sap" or "bra" => FoliageKind.Plant,
            "mus" or "fun" or "spo" => FoliageKind.Mushroom,
            _ => FoliageKind.Other,
        };
    }
}
