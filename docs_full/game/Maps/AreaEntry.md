# AreaEntry

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Map — AreaEntry

## Purpose & Responsibilities

The `AreaEntry` struct, defined in `Map.h`, serves as the in-memory representation of area definitions within the WoW server emulation. It acts as a lightweight data container and lookup utility for geographic regions, zones, and exploration flags.

Its primary responsibilities are:
1.  **Data Storage:** Holding static attributes for each area ID, such as its associated Map ID, Zone ID, exploration flag value, area flags, level, name, team affiliation, and liquid type.
2.  **Lookup Services:** Providing static methods to retrieve `AreaEntry` instances or specific attributes (like exploration flags) by various keys (ID, Map ID, or Area Flag).
3.  **Zone Identification:** Determining whether a specific area ID represents a "Zone" (where `ZoneId == 0`) versus a sub-area.

This unit does not manage dynamic state, networking, or game logic directly. Instead, it provides the foundational geographic metadata required by other systems—such as the Grid Map system for terrain queries, the Player system for zone updates, and the Chat Handler for administrative commands—to determine where entities are located and what properties apply to those locations.

## Member-by-Member Behavior

The `AreaEntry` unit consists of five static or instance methods. These are grouped by their functional role in data retrieval and classification.

### Classification

**`IsZone`**
This instance method determines if the current `AreaEntry` represents a top-level zone. It returns `true` if the `ZoneId` member is `0`. In the context of the game's geography, areas with a `ZoneId` of `0` are typically the primary named zones (e.g., "Elwynn Forest"), whereas non-zero `ZoneId` values often indicate sub-zones or specific points of interest within a larger zone. This distinction is critical for UI display and certain gameplay mechanics that treat zones and sub-areas differently.

### Lookup by ID

**`GetFlagById`**
A static utility that retrieves the `ExploreFlag` for a given area ID. It uses `sAreaStorage.LookupEntry<AreaEntry>` to fetch the entry. If the entry does not exist, it returns `-1`. Otherwise, it returns the `ExploreFlag` value. This is primarily used to determine if a player has explored a specific area, as the `ExploreFlag` is compared against the player's exploration bitmask.

**`GetById`**
A static accessor that returns a pointer to the `AreaEntry` structure for a given ID. It delegates directly to `sAreaStorage.LookupEntry<AreaEntry>`. This is the most common way to access full area data, used extensively by systems needing to know the map, zone, or flags associated with an area ID.

### Lookup by Map and Flag

**`GetFlagByMapId`**
A static method that retrieves an area flag associated with a specific Map ID. It searches a global static map `sAreaFlagByMapId` (defined in `Map.h`). If the map ID is not found in this cache, it returns `0`. This method appears to provide a quick lookup for map-specific area flags, possibly for performance optimization or handling special cases where area flags are tied directly to the map rather than a specific area ID.

**`GetByAreaFlagAndMap`**
A static method designed to find an `AreaEntry` based on an `areaFlag` and a `mapId`. This method contains significant logic to handle data inconsistencies in the underlying database:
1.  It iterates through all entries in `sAreaStorage`.
2.  It looks for entries where `ExploreFlag` matches the provided `areaFlag`.
3.  If a match is found, it checks if the entry's `MapId` matches the provided `mapId`. If so, it returns that entry immediately.
4.  If the `MapId` does not match, it stores the entry in a temporary variable `areaEntry` as a fallback candidate. This handles cases where the database might have duplicate `ExploreFlag` values across different maps, preferring the one on the correct map but falling back to any match if necessary.
5.  If no direct match is found after iterating all entries, it attempts a secondary lookup: it retrieves the `MapEntry` for the given `mapId` and returns the `AreaEntry` associated with the `linkedZone` of that map.
6.  If all lookups fail, it returns `nullptr`.

This method is crucial for resolving area identities when only the exploration flag and map are known, such as during terrain or liquid status checks.

## Cross-Unit Boundaries

`AreaEntry` interacts with several other units, primarily serving as a data provider.

### Called By

*   **`ChatHandler` (MiscCommands, TeleportCommands, CharacterCommands, UnitCommands):**
    *   `HandleLinkGraveCommand`, `HandleGoZoneXYCommand`, `HandleHideAreaCommand`, `HandleShowAreaCommand`, `HandleGPSCommand`: These administrative commands use `AreaEntry` methods to validate zone/area IDs, retrieve zone names, or determine area flags for teleportation and debugging purposes.
*   **`GridMap`:**
    *   `getLiquidStatus`, `GetZoneAndAreaIdByAreaFlag`, `GetZoneIdByAreaFlag`, `GetAreaFlag`, `GetAreaIdByAreaFlag`: The grid-based terrain system relies heavily on `AreaEntry` to determine liquid types, zone IDs, and area flags for specific coordinates. This is essential for rendering water, determining swimming vs. walking, and checking zone boundaries.
*   **`ObjectMgr`:**
    *   `LoadAreaTemplate`, `LoadAreaLocales`, `LoadFishingBaseSkillLevel`, `LoadGraveyardZones`, `LoadItemPrototypes`, `LoadQuests`, `LoadSpellAreas`: During server startup or data loading, `ObjectMgr` populates various caches and structures using `AreaEntry` data. For example, linking graveyard zones to areas or determining fishing skill requirements based on area levels.
*   **`Player` (Main):**
    *   `UpdateArea`, `UpdateZone`, `CheckAreaExploreAndOutdoor`: As a player moves, the `Player` class uses `AreaEntry` to determine if the player has entered a new zone or area, triggering exploration rewards, outdoor PvP checks, and UI updates.
*   **`Conditions`:**
    *   `Evaluate`, `IsValid`: The condition evaluation system uses `AreaEntry` to check if a player or unit is in a specific area or zone, allowing quests, spells, and items to have area-specific requirements.
*   **`Spell` (Effects):**
    *   `EffectDuel`: Dueling mechanics may use area data to enforce restrictions or determine valid duel locations.
*   **`WorldSession` (MiscHandler):**
    *   `operator()`: Handles various miscellaneous packets, potentially including zone/area-related client requests.
*   **`AiBotAI` (Main):**
    *   `UpdateAI`: AI bots may use area data to make decisions based on their current location, such as seeking cover or engaging in combat based on zone rules.

### Calls Out

`AreaEntry` itself does not call out to other units for logic execution. Its dependencies are limited to:
*   `sAreaStorage`: A global storage container (likely defined elsewhere, possibly in `ObjectMgr` or a dedicated storage header) that holds all `AreaEntry` instances. `AreaEntry` uses `LookupEntry` and iterators on this storage.
*   `sMapStorage`: Used in `GetByAreaFlagAndMap` to retrieve `MapEntry` data for fallback lookups.
*   `sAreaFlagByMapId`: A static global map defined in `Map.h` that caches map-to-flag associations.

## Data Model

The `AreaEntry` struct corresponds to the `areatable` (or similar) database table. While the exact schema is not provided in the prompt, the members of `AreaEntry` imply the following columns:

*   `Id`: Primary key for the area.
*   `MapId`: Foreign key to the map the area resides on.
*   `ZoneId`: Identifier for the zone, where `0` indicates the area is a zone itself.
*   `ExploreFlag`: Bitmask value used for tracking player exploration.
*   `Flags`: Area-specific flags (e.g., PvP, safe, etc.).
*   `AreaLevel`: Recommended level for the area.
*   `Name`: Localized name of the area.
*   `Team`: Faction affiliation (Alliance/Horde).
*   `LiquidTypeId`: Type of liquid (water, magma, etc.) associated with the area.

The code comments in `GetByAreaFlagAndMap` explicitly mention that "1.12.1 areatable have duplicates for areaflag," indicating that the database design allows multiple areas to share the same `ExploreFlag`, necessitating the complex lookup logic in that method.

## Notable Implementation Details

1.  **Duplicate Handling in `GetByAreaFlagAndMap`:**
    The method `GetByAreaFlagAndMap` contains a linear scan of `sAreaStorage` to handle duplicate `ExploreFlag` values. This is a performance consideration, as it iterates over all areas in memory. The logic prioritizes an exact match on both `ExploreFlag` and `MapId`, but falls back to any match with the correct `ExploreFlag` if no map-specific match is found. Finally, it attempts to resolve via the map's `linkedZone`. This complexity arises from historical database inconsistencies and must be preserved to ensure correct area resolution.

2.  **Static Global State:**
    `AreaEntry` relies on global static variables (`sAreaStorage`, `sAreaFlagByMapId`) for data access. This implies that these storages must be fully populated before any `AreaEntry` lookups are performed, typically during server initialization.

3.  **Zone vs. Area Distinction:**
    The `IsZone()` method's definition (`ZoneId == 0`) is a critical semantic distinction. Code relying on this must understand that `ZoneId` being `0` does not mean "no zone," but rather that the area *is* the zone. Sub-areas will have a non-zero `ZoneId` pointing to their parent zone.

4.  **Pack Alignment:**
    The `AreaEntry` struct is defined within a `#pragma pack(1)` block. This ensures that the struct has no padding bytes between members, which is likely important for binary compatibility with database dumps or network protocols if the struct is ever serialized directly. However, since it's used primarily as a C++ object, this is less critical unless interfacing with external binary data.

## Member Reference

**`IsZone`**
Returns `true` if `ZoneId` is `0`, indicating the area is a top-level zone.

**`GetFlagById`**
Static method. Returns the `ExploreFlag` for the given area ID, or `-1` if not found. Uses `sAreaStorage`.

**`GetFlagByMapId`**
Static method. Returns the area flag cached for the given map ID in `sAreaFlagByMapId`, or `0` if not found.

**`GetById`**
Static method. Returns a pointer to the `AreaEntry` for the given ID from `sAreaStorage`.

**`GetByAreaFlagAndMap`**
Static method. Finds an `AreaEntry` by `areaFlag` and `mapId`. Handles duplicate `ExploreFlag` values by iterating `sAreaStorage`, preferring an exact map match, then falling back to any match, and finally to the map's `linkedZone`. Returns `nullptr` if not found.

---

<!-- machine-true, projected from graph.json -->

## Map — AreaEntry

*Source:* Map.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| IsZone | method | — | ChatHandler.MiscCommands/HandleLinkGraveCommand, ChatHandler.TeleportCommands/HandleGoZoneXYCommand, GridMap/getLiquidStatus, GridMap/GetZoneAndAreaIdByAreaFlag, GridMap/GetZoneIdByAreaFlag, ObjectMgr/LoadAreaTemplate | — |
| GetFlagById | method | — | ChatHandler.CharacterCommands/HandleHideAreaCommand, ChatHandler.CharacterCommands/HandleShowAreaCommand, Conditions/Evaluate | — |
| GetFlagByMapId | method | — | GridMap/GetAreaFlag | — |
| GetById | method | — | AiBotAI.Main/UpdateAI, ChatHandler.MiscCommands/HandleLinkGraveCommand, ChatHandler.TeleportCommands/HandleGoZoneXYCommand, ChatHandler.UnitCommands/HandleGPSCommand, Conditions/IsValid, GridMap/GetAreaFlag, GridMap/getLiquidStatus, GridMap/getLiquidStatus#2, ObjectMgr/LoadAreaLocales, ObjectMgr/LoadFishingBaseSkillLevel, ObjectMgr/LoadGraveyardZones, ObjectMgr/LoadItemPrototypes, ObjectMgr/LoadQuests, Player.Main/UpdateArea, Player.Main/UpdateZone, Spell.Effects/EffectDuel, SpellMgr/LoadSpellAreas, WorldSession.MiscHandler/operator() | — |
| GetByAreaFlagAndMap | method | — | GridMap/GetAreaIdByAreaFlag, GridMap/getLiquidStatus#2, GridMap/GetZoneAndAreaIdByAreaFlag, GridMap/GetZoneIdByAreaFlag, Player.Main/CheckAreaExploreAndOutdoor | — |
