using Dapper;
using MangosSuperUI.Models;
using MangosSuperUI.Services;

namespace MangosSuperUI.BotLogic.Data;

/// <summary>
/// Zone metadata, NPC positions (vendors, trainers, flight masters),
/// and grind spot data from the mangos DB.
/// All data is cached after first load — zone geometry doesn't change at runtime.
///
/// Session 8 fix: VMaNGOS creature table has no 'zone' column. Vendors and
/// innkeepers are now indexed by map ID, and lookups use map + distance instead
/// of zone. GetNearestVendor searches all vendors on the same map within range.
///
/// 2026-06-22 (vendor_not_found fix): some cached vendors carry the vendor flag and
/// have a 'creature' spawn row, but the WORLD only materializes them while a game
/// event runs (Darkmoon Faire — Flik entry=14860, Professor Thaddeus Paleo entry=14847,
/// both gated by game_event_creature event 4/5). With the faire down, those creatures
/// are NOT in the world, so the C++ SELL handler finds no live creature near the bot
/// and emits SELL_FAIL reason=vendor_not_found. The planner WAITs on SELL_ACK, so that
/// mismatch just burns the 30s deadline — a wasted trip every time the phantom is the
/// nearest vendor. We now LEFT JOIN game_event_creature at load and tag each spawn's
/// EventGated; GetNearestVendor skips event-gated spawns when routing. The data stays
/// cached (not deleted) — only the routing pool excludes them. game_event_creature.event
/// is SIGNED: a POSITIVE event = "spawns only while the event is active" (the phantom
/// case); a NEGATIVE event = "present normally, despawns DURING the event" — those are
/// normally-present and MUST stay routable, so the gate is event > 0 only.
/// </summary>
public class ZoneDataLoader
{
    private readonly ConnectionFactory _db;
    private readonly ILogger<ZoneDataLoader> _logger;

    // Cache: mapId → vendor NPC locations
    private readonly Dictionary<int, List<NpcLocation>> _vendorsByMap = new();

    // Cache: mapId → innkeeper/flight master locations (town anchors)
    private readonly Dictionary<int, List<NpcLocation>> _townAnchorsByMap = new();

    private bool _loaded = false;

    public ZoneDataLoader(ConnectionFactory db, ILogger<ZoneDataLoader> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Preload zone and NPC data. Called once at startup.
    /// </summary>
    public async Task LoadAsync()
    {
        if (_loaded) return;

        try
        {
            using var conn = _db.Mangos();

            // Load vendor NPC locations (npc_flags & 128 = vendor)
            // VMaNGOS creature table has: map, position_x/y/z but NO zone column.
            // We index by map and do distance-based lookups.
            //
            // LEFT JOIN game_event_creature (keyed by creature.guid, one row per gated
            // spawn) to learn whether THIS spawn is conditional. gec.event is signed:
            //   > 0  → spawns only while that event is active (Darkmoon vendors when the
            //          faire is DOWN are not in the world → phantom). Tag EventGated.
            //   < 0  → present normally, despawns DURING the event → still routable.
            //   NULL → no gate → routable.
            // We carry the raw event value through as event_gate and derive EventGated below.
            var vendors = await conn.QueryAsync<NpcLocationRow>(@"
                SELECT c.map, c.position_x, c.position_y, c.position_z,
                       ct.entry AS npc_entry, ct.name AS npc_name, ct.npc_flags,
                       gec.event AS event_gate
                FROM creature c
                INNER JOIN creature_template ct ON c.id = ct.entry
                LEFT JOIN game_event_creature gec ON gec.guid = c.guid
                WHERE ct.npc_flags & 128 = 128
                  AND ct.patch = (SELECT MAX(patch) FROM creature_template ct2 WHERE ct2.entry = ct.entry)");

            int vendorCount = 0;
            int vendorGated = 0;
            foreach (var v in vendors)
            {
                if (!_vendorsByMap.ContainsKey(v.map))
                    _vendorsByMap[v.map] = new List<NpcLocation>();

                // Positive event id = spawn-only-when-active → phantom while the event is down.
                bool gated = v.event_gate.HasValue && v.event_gate.Value > 0;
                if (gated) vendorGated++;

                _vendorsByMap[v.map].Add(new NpcLocation
                {
                    MapId = v.map,
                    ZoneId = 0, // not available from creature table
                    X = v.position_x,
                    Y = v.position_y,
                    Z = v.position_z,
                    NpcEntry = v.npc_entry,
                    NpcName = v.npc_name,
                    // VMaNGOS / 1.12-client npc_flags layout: REPAIR (armorer) = 0x4000 (16384).
                    // 0x1000 (4096) is AUCTIONEER in vanilla — the "repair = 4096" value is the
                    // TBC/WotLK convention and is WRONG here. Using 4096 tagged auctioneers as
                    // repair-capable and EVERY real armorer as CanRepair=false, so no bot ever
                    // repaired fleet-wide (durability latched <30 and looped the vendor errand).
                    // VENDOR=128 and INNKEEPER=65536 (above/below) are correct for this layout.
                    CanRepair = (v.npc_flags & 0x4000) != 0, // UNIT_NPC_FLAG_REPAIR (16384)
                    // Event-gated spawn (positive game_event_creature.event) — cached for
                    // visibility but excluded from GetNearestVendor routing (phantom while
                    // the event is down). See the class note + the 2026-06-22 vendor fix.
                    EventGated = gated
                });
                vendorCount++;
            }

            // Load innkeepers (npc_flags & 65536 = innkeeper) as town anchors.
            // Same event-gate join: a faire/event innkeeper would be a phantom town anchor.
            var innkeepers = await conn.QueryAsync<NpcLocationRow>(@"
                SELECT c.map, c.position_x, c.position_y, c.position_z,
                       ct.entry AS npc_entry, ct.name AS npc_name, ct.npc_flags,
                       gec.event AS event_gate
                FROM creature c
                INNER JOIN creature_template ct ON c.id = ct.entry
                LEFT JOIN game_event_creature gec ON gec.guid = c.guid
                WHERE ct.npc_flags & 65536 = 65536
                  AND ct.patch = (SELECT MAX(patch) FROM creature_template ct2 WHERE ct2.entry = ct.entry)");

            int innkeeperCount = 0;
            foreach (var i in innkeepers)
            {
                bool gated = i.event_gate.HasValue && i.event_gate.Value > 0;

                if (!_townAnchorsByMap.ContainsKey(i.map))
                    _townAnchorsByMap[i.map] = new List<NpcLocation>();

                _townAnchorsByMap[i.map].Add(new NpcLocation
                {
                    MapId = i.map,
                    ZoneId = 0,
                    X = i.position_x,
                    Y = i.position_y,
                    Z = i.position_z,
                    NpcEntry = i.npc_entry,
                    NpcName = i.npc_name,
                    EventGated = gated
                });
                innkeeperCount++;
            }

            _loaded = true;
            _logger.LogInformation(
                "ZoneDataLoader: cached {VCount} vendors ({VGated} event-gated, skipped when routing) across {VMaps} maps, {ICount} innkeepers across {IMaps} maps",
                vendorCount, vendorGated, _vendorsByMap.Count, innkeeperCount, _townAnchorsByMap.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ZoneDataLoader: failed to load NPC data");
        }
    }

    /// <summary>
    /// Get the nearest vendor to a position on the same map, within a level-appropriate
    /// distance cap. Prefers repair-capable vendors when one is available within 1.5x
    /// the distance of the absolute nearest vendor — this way bots get their gear
    /// repaired as part of normal vendoring without traveling much further.
    ///
    /// When <paramref name="requireRepair"/> is true the lookup is HARD-FILTERED to
    /// repair-capable vendors only (the 1.5x convenience preference is bypassed) and
    /// returns null if none are in range. Used by the durability errand: below a
    /// durability floor a sell-only vendor is useless, so a soft 1.5x preference (which
    /// loses whenever the bot is standing next to a closer food vendor) is not enough —
    /// the bot must be sent to an armorer or not vendor at all. Repair NPCs that are
    /// cached carry the vendor flag too, so selling greys still works at the chosen one.
    ///
    /// Session 26 fix: previously returned ANY vendor on same map with no distance cap.
    /// Session 32: repair vendor preference added.
    /// 2026-06-20: requireRepair hard-filter added (the 1.5x window never reached the
    /// armorer when a food vendor was closer; durability latched and looped the errand).
    /// 2026-06-22: event-gated spawns excluded from routing (Darkmoon phantoms — Flik /
    /// Thaddeus — that emit SELL_FAIL vendor_not_found and burn the 30s SELL_ACK deadline).
    /// </summary>
    public NpcLocation? GetNearestVendor(int zoneId, int mapId, float x, float y, int botLevel = 60, bool requireRepair = false)
    {
        if (!_vendorsByMap.TryGetValue(mapId, out var allVendors) || allVendors.Count == 0)
        {
            _logger.LogWarning("[VENDOR] lookup z={Zone} map={Map} lvl={Lvl} → NULL: no vendors loaded on this map", zoneId, mapId, botLevel);
            return null;
        }

        // Event-gated spawns are cached for visibility but the world only materializes them
        // while their game event runs. Routing to one that isn't spawned wastes the trip and
        // eats a 30s SELL_ACK deadline on vendor_not_found. Drop them from the routing pool —
        // the faire camp is no loss against the ~490 always-on vendors.
        var vendors = allVendors.Where(v => !v.EventGated).ToList();
        if (vendors.Count == 0)
        {
            _logger.LogWarning("[VENDOR] lookup z={Zone} map={Map} lvl={Lvl} → NULL: {Total} vendors on map but all event-gated (phantom)",
                zoneId, mapId, botLevel, allVendors.Count);
            return null;
        }

        float maxDist = ZoneSafetyMap.GetMaxTravelDistance(botLevel, zoneId);
        float maxDistSq = maxDist * maxDist;

        // Absolute closest on the map regardless of cap — so a cap-driven null can SAY how far
        // the nearest vendor actually was vs the cap that rejected it (the candidate-1 tell).
        NpcLocation? closest = null;
        float closestDistSq = float.MaxValue;
        foreach (var v in vendors)
        {
            float dsq = DistSq(v.X, v.Y, x, y);
            if (dsq < closestDistSq) { closestDistSq = dsq; closest = v; }
        }

        var inRange = vendors
            .Where(v => DistSq(v.X, v.Y, x, y) <= maxDistSq)
            .OrderBy(v => DistSq(v.X, v.Y, x, y))
            .ToList();

        if (inRange.Count == 0)
        {
            _logger.LogWarning(
                "[VENDOR] lookup z={Zone} map={Map} lvl={Lvl} cap={Cap:F0}yd mapVendors={N} → NULL: nothing in range; CLOSEST {Name} (entry={Entry}) @ {Dist:F0}yd > cap",
                zoneId, mapId, botLevel, maxDist, vendors.Count,
                closest?.NpcName ?? "?", closest?.NpcEntry ?? 0,
                closest != null ? MathF.Sqrt(closestDistSq) : -1f);
            return null;
        }

        // Durability-forced repair: a sell-only vendor can't fix cratered gear, so when the
        // caller demands repair we ignore the convenience preference entirely and hard-filter
        // to repair-capable vendors. inRange is already nearest-first, so the first CanRepair
        // is the nearest armorer; if there is none in range we return null (the errand gives up
        // and re-tries later rather than looping a vendor that can never repair).
        if (requireRepair)
        {
            var nearestRepairOnly = inRange.FirstOrDefault(v => v.CanRepair);
            if (nearestRepairOnly == null)
            {
                _logger.LogWarning(
                    "[VENDOR] lookup z={Zone} map={Map} lvl={Lvl} cap={Cap:F0}yd mapVendors={N} inRange={R} requireRepair → NULL: no repair-capable vendor in range",
                    zoneId, mapId, botLevel, maxDist, vendors.Count, inRange.Count);
                return null;
            }
            _logger.LogInformation(
                "[VENDOR] lookup z={Zone} map={Map} lvl={Lvl} cap={Cap:F0}yd mapVendors={N} inRange={R} requireRepair → {Name} (entry={Entry}) @ {Dist:F0}yd repair=Y",
                zoneId, mapId, botLevel, maxDist, vendors.Count, inRange.Count,
                nearestRepairOnly.NpcName, nearestRepairOnly.NpcEntry,
                MathF.Sqrt(DistSq(nearestRepairOnly.X, nearestRepairOnly.Y, x, y)));
            return nearestRepairOnly;
        }

        var nearest = inRange[0];
        float nearestDistSq = DistSq(nearest.X, nearest.Y, x, y);

        // If nearest can already repair, perfect
        if (nearest.CanRepair)
        {
            _logger.LogInformation(
                "[VENDOR] lookup z={Zone} map={Map} lvl={Lvl} cap={Cap:F0}yd mapVendors={N} inRange={R} → {Name} (entry={Entry}) @ {Dist:F0}yd repair=Y",
                zoneId, mapId, botLevel, maxDist, vendors.Count, inRange.Count,
                nearest.NpcName, nearest.NpcEntry, MathF.Sqrt(nearestDistSq));
            return nearest;
        }

        // Look for a repair vendor within 1.5x the distance of the nearest vendor.
        // A bot shouldn't walk 3x as far just for repair, but a small detour is worth it.
        float repairThresholdSq = nearestDistSq * 2.25f; // 1.5x distance → 2.25x squared
        var nearestRepair = inRange.FirstOrDefault(v => v.CanRepair && DistSq(v.X, v.Y, x, y) <= repairThresholdSq);
        var chosen = nearestRepair ?? nearest;

        _logger.LogInformation(
            "[VENDOR] lookup z={Zone} map={Map} lvl={Lvl} cap={Cap:F0}yd mapVendors={N} inRange={R} → {Name} (entry={Entry}) @ {Dist:F0}yd repair={Rep}",
            zoneId, mapId, botLevel, maxDist, vendors.Count, inRange.Count,
            chosen.NpcName, chosen.NpcEntry, MathF.Sqrt(DistSq(chosen.X, chosen.Y, x, y)), chosen.CanRepair ? "Y" : "N");
        return chosen;
    }

    /// <summary>
    /// Is the bot near a town? (Within 150 yards of an innkeeper on same map.)
    /// Event-gated innkeepers (phantom town anchors) are excluded.
    /// </summary>
    public bool IsNearTown(int zoneId, int mapId, float x, float y)
    {
        if (!_townAnchorsByMap.TryGetValue(mapId, out var anchors))
            return false;

        float nearRange = 150f * 150f; // 150 yards squared
        return anchors.Any(a => !a.EventGated && DistSq(a.X, a.Y, x, y) < nearRange);
    }

    /// <summary>
    /// Get a random interesting point near the bot's current position.
    /// Uses vendor/innkeeper locations on the same map as POIs.
    /// Falls back to all map POIs if mapId has entries.
    /// Event-gated spawns are excluded (a phantom POI sends the bot nowhere useful).
    /// </summary>
    public NpcLocation? GetRandomPointOfInterest(int zoneId, int mapId = -1)
    {
        var candidates = new List<NpcLocation>();

        if (mapId >= 0)
        {
            // Prefer same-map POIs
            if (_vendorsByMap.TryGetValue(mapId, out var vendors))
                candidates.AddRange(vendors.Where(v => !v.EventGated));
            if (_townAnchorsByMap.TryGetValue(mapId, out var anchors))
                candidates.AddRange(anchors.Where(a => !a.EventGated));
        }

        // Fallback: try all maps (original behavior for callers without mapId)
        if (candidates.Count == 0)
        {
            foreach (var list in _vendorsByMap.Values)
                candidates.AddRange(list.Where(v => !v.EventGated));
            foreach (var list in _townAnchorsByMap.Values)
                candidates.AddRange(list.Where(a => !a.EventGated));
        }

        if (candidates.Count == 0) return null;

        return candidates[Core.WeightedRoller.RangeInt(0, candidates.Count - 1)];
    }

    private static float DistSq(float x1, float y1, float x2, float y2)
    {
        float dx = x1 - x2, dy = y1 - y2;
        return dx * dx + dy * dy;
    }
}

// Internal row DTO for Dapper
internal class NpcLocationRow
{
    public int map { get; set; }
    public float position_x { get; set; }
    public float position_y { get; set; }
    public float position_z { get; set; }
    public int npc_entry { get; set; }
    public string npc_name { get; set; } = "";
    public int npc_flags { get; set; }
    // game_event_creature.event for this spawn's guid (NULL = no gate). Signed:
    // > 0 = spawn-only-when-active (phantom while down); < 0 = despawn-during-event.
    public int? event_gate { get; set; }
}

public class NpcLocation
{
    public int MapId { get; set; }
    public int ZoneId { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public int NpcEntry { get; set; }
    public string NpcName { get; set; } = "";
    public bool CanRepair { get; set; }
    // True when this spawn only exists while a game event runs (positive
    // game_event_creature.event). Cached but excluded from routing so the bot is
    // never sent to a creature the world hasn't materialized (the vendor_not_found
    // phantom — Darkmoon Flik/Thaddeus while the faire is down).
    public bool EventGated { get; set; }
}

public class ZoneInfo
{
    public int ZoneId { get; set; }
    public string Name { get; set; } = "";
    public int MapId { get; set; }
    public int MinLevel { get; set; }
    public int MaxLevel { get; set; }
}