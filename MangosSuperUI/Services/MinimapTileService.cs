namespace MangosSuperUI.Services;

/// <summary>
/// Serves world-map minimap tiles ON DEMAND from the client MPQs — no
/// extraction step. Vanilla stores minimap tiles as md5-hash-named BLPs
/// under textures\Minimap\, with textures\Minimap\md5translate.trs
/// mapping plain names ("Azeroth\map27_30.blp") to the hashed filenames.
///
/// On first use this service parses the .trs once, giving it the full
/// tile index per map (which also powers TileIndex/AvailableMaps without
/// touching disk). Individual tiles are decoded BLP → PNG on request and
/// disk-cached under wwwroot/minimap/{Map}/mapRR_CC.png — the same layout
/// the retired extractor used, so existing consumers of /minimap/... URLs
/// (Visual Lab, bot map dots) keep working once a tile has been served.
///
/// .trs format (vanilla 1.12):
///   dir: Azeroth
///   Azeroth\map27_30.blp	772d38f6600c9ef7e64444b1a54804f7.blp
///   ...
/// Lines whose plain name doesn't match map{row}_{col}.blp (WMO interior
/// minimaps etc.) are ignored — the world map only tiles ADT maps.
/// </summary>
public class MinimapTileService
{
    private readonly MpqReaderService _mpq;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<MinimapTileService> _logger;

    // mapFolderLower → (original-case folder name, (row, col) → hashed blp filename)
    private Dictionary<string, (string Name, Dictionary<(int Row, int Col), string> Tiles)>? _index;
    private readonly object _initLock = new();

    public MinimapTileService(MpqReaderService mpq, IWebHostEnvironment env, ILogger<MinimapTileService> logger)
    {
        _mpq = mpq;
        _env = env;
        _logger = logger;
    }

    /// <summary>True when md5translate.trs was found and parsed.</summary>
    public bool IsAvailable => Index.Count > 0;

    public List<(string Name, int TileCount)> GetAvailableMaps() =>
        Index.Values
            .Where(m => m.Tiles.Count > 0)
            .OrderBy(m => m.Name)
            .Select(m => (m.Name, m.Tiles.Count))
            .ToList();

    public List<(int Row, int Col)> GetTileIndex(string map)
    {
        if (!Index.TryGetValue(map.ToLowerInvariant(), out var entry))
            return new List<(int, int)>();
        return entry.Tiles.Keys.OrderBy(k => k.Row).ThenBy(k => k.Col).ToList();
    }

    /// <summary>
    /// PNG bytes for a tile, decoding from the MPQ and disk-caching on first
    /// request. Null when the map/tile is unknown or extraction fails.
    /// </summary>
    public byte[]? GetTilePng(string map, int row, int col)
    {
        if (!Index.TryGetValue(map.ToLowerInvariant(), out var entry))
            return null;

        // Disk cache first — same layout the extractor used.
        var cachePath = Path.Combine(_env.WebRootPath, "minimap", entry.Name, $"map{row:D2}_{col:D2}.png");
        if (File.Exists(cachePath))
        {
            try { return File.ReadAllBytes(cachePath); }
            catch { /* fall through to regeneration */ }
        }

        if (!entry.Tiles.TryGetValue((row, col), out var hashedName))
            return null;

        // The hash column is usually a bare filename; some clients carry a
        // subdir prefix. Try as-is, then the bare filename.
        var blp = _mpq.ExtractFile($"textures\\Minimap\\{hashedName}");
        if (blp == null && hashedName.Contains('\\'))
            blp = _mpq.ExtractFile($"textures\\Minimap\\{hashedName[(hashedName.LastIndexOf('\\') + 1)..]}");
        if (blp == null || blp.Length == 0)
        {
            _logger.LogDebug("Minimap: hashed BLP not in MPQ: {Name} ({Map} {Row},{Col})", hashedName, map, row, col);
            return null;
        }

        var png = GlbWriter.ConvertBlpToPngBytes(blp);
        if (png == null) return null;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            File.WriteAllBytes(cachePath, png);
        }
        catch (Exception ex)
        {
            // Cache write failure is non-fatal — still serve the bytes.
            _logger.LogWarning("Minimap: could not cache tile {Path}: {Err}", cachePath, ex.Message);
        }

        return png;
    }

    // ── Index building ──────────────────────────────────────────────────

    private Dictionary<string, (string Name, Dictionary<(int Row, int Col), string> Tiles)> Index
    {
        get
        {
            if (_index != null) return _index;
            lock (_initLock)
            {
                _index ??= BuildIndex();
            }
            return _index;
        }
    }

    private Dictionary<string, (string Name, Dictionary<(int Row, int Col), string> Tiles)> BuildIndex()
    {
        var index = new Dictionary<string, (string Name, Dictionary<(int Row, int Col), string> Tiles)>();

        var trs = _mpq.ExtractFile("textures\\Minimap\\md5translate.trs")
               ?? _mpq.ExtractFile("Textures\\Minimap\\md5translate.trs");
        if (trs == null)
        {
            _logger.LogWarning("Minimap: md5translate.trs not found in MPQs — minimap tiles unavailable");
            return index;
        }

        int parsed = 0;
        foreach (var rawLine in System.Text.Encoding.ASCII.GetString(trs)
                     .Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.TrimEnd('\r').Trim();
            if (line.Length == 0 || line.StartsWith("dir:", StringComparison.OrdinalIgnoreCase))
                continue;

            // "<MapName>\map<row>_<col>.blp \t <hash>.blp"
            var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;

            var plain = parts[0].Trim().Replace('/', '\\');
            var hashed = parts[1].Trim().Replace('/', '\\');

            int slash = plain.LastIndexOf('\\');
            if (slash <= 0) continue;

            var mapName = plain[..slash];
            var fileName = plain[(slash + 1)..];

            // Only ADT map tiles: "map{row}_{col}.blp". WMO minimaps have
            // other shapes and aren't drawn by the world map page.
            if (!fileName.StartsWith("map", StringComparison.OrdinalIgnoreCase) ||
                !fileName.EndsWith(".blp", StringComparison.OrdinalIgnoreCase))
                continue;
            var coords = fileName[3..^4].Split('_');
            if (coords.Length != 2 ||
                !int.TryParse(coords[0], out int row) ||
                !int.TryParse(coords[1], out int col))
                continue;

            var key = mapName.ToLowerInvariant();
            if (!index.TryGetValue(key, out var entry))
            {
                entry = (mapName, new Dictionary<(int, int), string>());
                index[key] = entry;
            }
            entry.Item2[(row, col)] = hashed;
            parsed++;
        }

        _logger.LogInformation("Minimap: indexed {Tiles} tiles across {Maps} maps from md5translate.trs",
            parsed, index.Count);
        return index;
    }
}
