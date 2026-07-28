using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Collections.Concurrent;
using MangosSuperUI.Services;

namespace MangosSuperUI.Controllers;

// ═══════════════════════════════════════════════════════════════════════════
// REAL BUILDING/TREE COLLISION — the vmap port (handoff §5.6)
// ═══════════════════════════════════════════════════════════════════════════
//
// Serves VMaNGOS's extracted vmap geometry, per loaded tile block, as a flat
// WoW-world triangle buffer. The browser converts to scene space and builds a
// three-mesh-bvh CollisionWorld the character raycasts against — real walls with
// doorways, floors, stairs and interiors, instead of the render-mesh outer shell.
//
// This is a partial of WorldEditorController so it can reuse TryResolvePreset,
// MapIdToName and GetMapsDirectory without touching the 300 KB main file.
// ═══════════════════════════════════════════════════════════════════════════
public partial class WorldEditorController
{
    // Process-lifetime cache of the base64 payload, keyed by the request. A vmap
    // block is static, so re-loading the same preset is a dictionary hit rather
    // than a re-bake + re-encode. Same shape as _foliageCache.
    private static readonly ConcurrentDictionary<string, object> _collisionCache = new();

    /// <summary>
    /// GET /WorldEditor/Collision?preset=&amp;tileGridX=&amp;tileGridY=&amp;radius=1&amp;includeM2=true
    ///
    /// Returns the deduped collision triangles for the (2*radius+1)² ADT block
    /// centred on the preset (or the given tile), in WoW world space, packed as
    /// little-endian Float32 (9 floats per triangle).
    /// </summary>
    [HttpGet]
    public IActionResult Collision(string? preset, int tileGridX = -1, int tileGridY = -1,
        int radius = 1, bool includeM2 = true)
    {
        if (!TryResolvePreset(preset, out var p, out var error))
            return Json(new { success = false, error });

        int cgx = tileGridX >= 0 ? tileGridX : p.gridX;
        int cgy = tileGridY >= 0 ? tileGridY : p.gridY;
        radius = Math.Clamp(radius, 0, 3);

        string cacheKey = $"{p.mapId}_{cgx}_{cgy}_{radius}_{(includeM2 ? 1 : 0)}";
        if (_collisionCache.TryGetValue(cacheKey, out var cachedHit))
            return Json(cachedHit);

        string vmapDir = GetVmapsDirectory();
        if (string.IsNullOrEmpty(vmapDir))
            return Json(new
            {
                success = false,
                error = "Vmaps directory not found. Set Vmangos:VmapsDataPath (or Vmangos:ServerDataPath, " +
                        "whose 'vmaps' subfolder is used) to the VMaNGOS extracted vmaps.",
                triedServerDataPath = _config?.GetValue<string>("Vmangos:ServerDataPath")
            });

        try
        {
            // The tile block, matching how terrain loads its 3×3 neighbourhood.
            var tiles = new List<(int gridX, int gridY)>();
            for (int dgx = -radius; dgx <= radius; dgx++)
                for (int dgy = -radius; dgy <= radius; dgy++)
                    tiles.Add((cgx + dgx, cgy + dgy));

            var stats = new VmapBakeStats();
            float[] worldTris = VmapCollisionBaker.BakeTiles(vmapDir, p.mapId, tiles, includeM2, stats);

            // Little-endian on the box (x86-64 Linux); the browser's Float32Array
            // is little-endian too, so this round-trips without byte-swapping.
            var bytes = new byte[worldTris.Length * sizeof(float)];
            Buffer.BlockCopy(worldTris, 0, bytes, 0, bytes.Length);

            if (stats.TilesLoaded == 0)
                stats.Notes.Add($"No .vmtile found in {vmapDir} for map {p.mapId} tiles around " +
                                $"({cgx},{cgy}). Ocean/unextracted tiles are normal; a whole zone missing is not.");

            var payload = new
            {
                success = true,
                mapId = p.mapId,
                centerGridX = cgx,
                centerGridY = cgy,
                radius,
                includeM2,
                triangleCount = stats.TrianglesAdded,
                positionsBase64 = Convert.ToBase64String(bytes),
                vmapDir,
                stats = new
                {
                    stats.TilesRequested,
                    stats.TilesLoaded,
                    stats.SpawnsSeen,
                    stats.SpawnsUsed,
                    stats.SpawnsDuplicate,
                    stats.SpawnsSkippedM2,
                    stats.SpawnsUnresolved,
                    stats.DistinctUnresolved,
                    stats.TrianglesAdded,
                    stats.DegenerateSkipped
                },
                notes = stats.Notes
            };

            _collisionCache[cacheKey] = payload;
            return Json(payload);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Collision bake failed for {Preset} ({GX},{GY})", preset, cgx, cgy);
            return Json(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// Locate the VMaNGOS extracted vmaps directory. Mirrors
    /// ServerDataService.GetServerVmapsDir (ServerDataPath + "/vmaps"), with the
    /// same config-first, sensible-defaults pattern as GetMapsDirectory.
    /// </summary>
    private string GetVmapsDirectory()
    {
        var candidates = new List<string>();

        var configured = _config?.GetValue<string>("Vmangos:VmapsDataPath");
        if (!string.IsNullOrEmpty(configured)) candidates.Add(configured);

        var serverData = _config?.GetValue<string>("Vmangos:ServerDataPath");
        if (!string.IsNullOrEmpty(serverData)) candidates.Add(Path.Combine(serverData, "vmaps"));

        // Sibling of the maps directory (…/data/maps -> …/data/vmaps).
        string mapsDir = GetMapsDirectory();
        if (!string.IsNullOrEmpty(mapsDir))
        {
            string? parent = Path.GetDirectoryName(mapsDir.TrimEnd('/', '\\'));
            if (!string.IsNullOrEmpty(parent)) candidates.Add(Path.Combine(parent, "vmaps"));
        }

        candidates.Add("/home/wowvmangos/vmangos/run/data/vmaps");
        candidates.Add("/home/wowvmangos/wowclient/vmaps");

        foreach (var c in candidates)
            if (!string.IsNullOrEmpty(c) && Directory.Exists(c)) return c;

        return "";
    }
}
