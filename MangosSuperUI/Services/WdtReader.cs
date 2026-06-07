// WdtReader.cs — WDT (World Data Table) parser for detecting instance/dungeon maps.
//
// WDT files define whether a map uses terrain tiles (ADT) or a single global WMO.
// Instance maps (Deadmines, Stockades, Wailing Caverns, etc.) set the
// wdt_uses_global_map_obj flag and contain a single MODF entry pointing at
// the root WMO file that IS the entire dungeon.
//
// Format (vanilla 1.12.1):
//   MVER → version (18)
//   MPHD → header flags (8 bytes used: uint32 flags, uint32 unused)
//   MAIN → 64×64 tile existence grid (each entry 8 bytes: uint32 flags, uint32 asyncId)
//   MWMO → null-terminated WMO path string (only present if global WMO)
//   MODF → single 64-byte WMO placement entry (only present if global WMO)

using System;
using System.Collections.Generic;
using System.Text;

namespace MangosSuperUI.Services
{
    public class WdtResult
    {
        /// <summary>True if the WDT has the wdt_uses_global_map_obj flag (0x01).</summary>
        public bool IsGlobalWmo { get; set; }

        /// <summary>MPHD flags raw value.</summary>
        public uint Flags { get; set; }

        /// <summary>Path to the global WMO root file (from MWMO chunk), or null.</summary>
        public string? GlobalWmoPath { get; set; }

        /// <summary>MODF placement entry for the global WMO, or null.</summary>
        public WdtModfEntry? GlobalWmoPlacement { get; set; }

        /// <summary>Which ADT tiles exist (64×64 grid). True = tile has terrain data.</summary>
        public bool[,] TileExists { get; set; } = new bool[64, 64];

        /// <summary>Number of ADT tiles that exist.</summary>
        public int TileCount { get; set; }
    }

    public class WdtModfEntry
    {
        public uint NameId { get; set; }
        public uint UniqueId { get; set; }
        public float PosX { get; set; }
        public float PosY { get; set; }
        public float PosZ { get; set; }
        public float RotX { get; set; }
        public float RotY { get; set; }
        public float RotZ { get; set; }
        public float BbMinX { get; set; }
        public float BbMinY { get; set; }
        public float BbMinZ { get; set; }
        public float BbMaxX { get; set; }
        public float BbMaxY { get; set; }
        public float BbMaxZ { get; set; }
        public ushort Flags { get; set; }
        public ushort DoodadSet { get; set; }
        public ushort NameSet { get; set; }
        public ushort Padding { get; set; }
    }

    public static class WdtReader
    {
        // MPHD flag: map uses a single global WMO instead of terrain tiles
        private const uint FLAG_GLOBAL_WMO = 0x0001;

        /// <summary>
        /// Parse a WDT file from raw bytes.
        /// Returns null if the data is too small or the MVER chunk is wrong.
        /// </summary>
        public static WdtResult? Parse(byte[] data)
        {
            if (data == null || data.Length < 20) return null;

            var result = new WdtResult();
            int pos = 0;

            while (pos + 8 <= data.Length)
            {
                uint fourcc = BitConverter.ToUInt32(data, pos);
                uint size = BitConverter.ToUInt32(data, pos + 4);
                int dataStart = pos + 8;
                int dataEnd = dataStart + (int)size;

                if (dataEnd > data.Length) break;

                // FourCC values (little-endian on disk, so "REVM" = MVER, etc.)
                string tag = ChunkTag(fourcc);

                switch (tag)
                {
                    case "MVER":
                        if (size >= 4)
                        {
                            uint version = BitConverter.ToUInt32(data, dataStart);
                            if (version != 18) return null; // not vanilla
                        }
                        break;

                    case "MPHD":
                        if (size >= 4)
                        {
                            result.Flags = BitConverter.ToUInt32(data, dataStart);
                            result.IsGlobalWmo = (result.Flags & FLAG_GLOBAL_WMO) != 0;
                        }
                        break;

                    case "MAIN":
                        // 64×64 grid, each entry 8 bytes: uint32 flags + uint32 asyncId
                        // Flag 0x01 = tile exists
                        ParseMainChunk(data, dataStart, (int)size, result);
                        break;

                    case "MWMO":
                        // Null-terminated WMO path string
                        if (size > 1)
                        {
                            int strEnd = dataStart;
                            while (strEnd < dataEnd && data[strEnd] != 0) strEnd++;
                            result.GlobalWmoPath = Encoding.ASCII.GetString(data, dataStart, strEnd - dataStart);
                        }
                        break;

                    case "MODF":
                        // Single 64-byte WMO placement entry
                        if (size >= 64)
                        {
                            result.GlobalWmoPlacement = ParseModfEntry(data, dataStart);
                        }
                        break;
                }

                pos = dataEnd;
            }

            return result;
        }

        private static void ParseMainChunk(byte[] data, int offset, int size, WdtResult result)
        {
            // 64×64 entries × 8 bytes = 32768 bytes expected
            int count = 0;
            for (int y = 0; y < 64; y++)
            {
                for (int x = 0; x < 64; x++)
                {
                    int entryOff = offset + (y * 64 + x) * 8;
                    if (entryOff + 4 > offset + size) break;

                    uint flags = BitConverter.ToUInt32(data, entryOff);
                    bool exists = (flags & 0x01) != 0;
                    result.TileExists[y, x] = exists;
                    if (exists) count++;
                }
            }
            result.TileCount = count;
        }

        private static WdtModfEntry ParseModfEntry(byte[] data, int offset)
        {
            return new WdtModfEntry
            {
                NameId = BitConverter.ToUInt32(data, offset + 0),
                UniqueId = BitConverter.ToUInt32(data, offset + 4),
                PosX = BitConverter.ToSingle(data, offset + 8),
                PosY = BitConverter.ToSingle(data, offset + 12),
                PosZ = BitConverter.ToSingle(data, offset + 16),
                RotX = BitConverter.ToSingle(data, offset + 20),
                RotY = BitConverter.ToSingle(data, offset + 24),
                RotZ = BitConverter.ToSingle(data, offset + 28),
                BbMinX = BitConverter.ToSingle(data, offset + 32),
                BbMinY = BitConverter.ToSingle(data, offset + 36),
                BbMinZ = BitConverter.ToSingle(data, offset + 40),
                BbMaxX = BitConverter.ToSingle(data, offset + 44),
                BbMaxY = BitConverter.ToSingle(data, offset + 48),
                BbMaxZ = BitConverter.ToSingle(data, offset + 52),
                Flags = BitConverter.ToUInt16(data, offset + 56),
                DoodadSet = BitConverter.ToUInt16(data, offset + 58),
                NameSet = BitConverter.ToUInt16(data, offset + 60),
                Padding = BitConverter.ToUInt16(data, offset + 62)
            };
        }

        /// <summary>
        /// Build the WDT MPQ path for a given mapId.
        /// Format: World\Maps\{MapName}\{MapName}.wdt
        /// </summary>
        public static string BuildWdtPath(string mapName)
        {
            return $"World\\Maps\\{mapName}\\{mapName}.wdt";
        }

        /// <summary>Convert a uint32 FourCC to its 4-char ASCII tag.</summary>
        private static string ChunkTag(uint fourcc)
        {
            byte[] bytes = BitConverter.GetBytes(fourcc);
            // Reverse because IFF stores as big-endian tag but we read little-endian
            return new string(new char[] {
                (char)bytes[3], (char)bytes[2], (char)bytes[1], (char)bytes[0]
            });
        }

        // ─────────────────────────────────────────────────────────────────
        // Well-known dungeon/instance maps for the preset catalog.
        // mapId → (internalName, displayName)
        // Only maps that use the global WMO flag.
        // ─────────────────────────────────────────────────────────────────

        public static readonly (int mapId, string internalName, string displayName)[] KnownDungeons =
        {
            // Global-WMO dungeons ONLY. These have the wdt_uses_global_map_obj
            // flag (0x01) set in their WDT's MPHD chunk. The entire dungeon is
            // a single WMO with no terrain tiles.
            //
            // Terrain-based instances (Deadmines, Shadowfang, Stratholme, BWL,
            // Scholomance, Scarlet Monastery, Zul'Farrak, Razorfen Kraul/Downs,
            // Zul'Gurub, AQ20, AQ40, Naxxramas) are in _terrainPresets instead
            // and load via the normal terrain+WMO streaming pipeline.
            //
            // PRIMARY NAMES are the exact Map.dbc Directory field values.
            (  34, "StormwindJail",        "The Stockade"),
            (  43, "WailingCaverns",       "Wailing Caverns"),
            (  48, "Blackfathom",          "Blackfathom Deeps"),
            (  70, "Uldaman",              "Uldaman"),
            (  90, "GnomeragonInstance",   "Gnomeregan"),
            ( 109, "SunkenTemple",         "Temple of Atal'Hakkar"),
            ( 229, "BlackRockSpire",       "Blackrock Spire"),
            ( 230, "BlackrockDepths",      "Blackrock Depths"),
            ( 249, "OnyxiaLairInstance",   "Onyxia's Lair"),
            ( 349, "Mauradon",             "Maraudon"),
            ( 389, "OrgrimmarInstance",    "Ragefire Chasm"),
            ( 409, "MoltenCore",           "Molten Core"),
            ( 429, "DireMaul",             "Dire Maul"),
        };

        /// <summary>
        /// Alternative folder names to try for each mapId.
        /// Blizzard was inconsistent with folder naming across patches and locales.
        /// The Presets endpoint tries the primary name first, then each alias.
        /// </summary>
        public static readonly Dictionary<int, string[]> KnownDungeonAliases = new()
        {
            { 48,  new[] { "BlackfathomDeeps" } },
            { 90,  new[] { "Gnomeragon", "Gnomeregan", "GnomereganInstance" } },
        };
    }
}