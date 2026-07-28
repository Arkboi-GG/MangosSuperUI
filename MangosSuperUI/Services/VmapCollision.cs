using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Numerics;
using System.Text;

namespace MangosSuperUI.Services;

// ═══════════════════════════════════════════════════════════════════════════
// VMAP COLLISION — the server's OWN collision meshes, served to the web client
// ═══════════════════════════════════════════════════════════════════════════
//
// PORTED FROM MSUIClient, reading the actual source rather than notes about it:
//   Formats/VmapFormat.cs                the .vmtile / .vmo readers + ToWorld
//   World/Collision/VmapCollisionLoader.cs   tile -> spawns -> world triangles
//   World/Collision/CollisionWorld.cs        the degenerate-triangle skip
//
// WHY THIS EXISTS (handoff §5.6 "Real collision — the vmap port")
//   The web character used to raycast the RENDER meshes (WMO/doodad
//   InstancedMeshes). That gives a crude outer shell and nothing else — no
//   interiors, doorways, or steps, because a render mesh has no such semantics.
//   VMaNGOS already extracted proper collision geometry (vmaps); reusing it
//   means the browser client and mangosd agree on where the walls are BY
//   CONSTRUCTION, and we never write a WMO/M2 collision parser.
//
// The reader FORMAT was verified against Nico's real vmap bytes on 2026-07-21
// (see the VmapFormat header). This file changes only what has to change to run
// server-side and emit a flat triangle buffer instead of a CollisionWorld:
//   - namespace MangosSuperUI.Services;
//   - the loader accumulates float triangles (9 floats/triangle, WoW world
//     space) instead of calling world.AddTriangle;
//   - process-lifetime caches for the file index and the parsed .vmo models,
//     the same shape the Foliage endpoint uses.
//
// Everything downstream that means a coordinate means WoW world space
// (+X north, +Y west, +Z up). The BROWSER converts to three.js scene space at
// the last moment, using the same centre-tile transform the .gps HUD uses, so
// collision lines up with the rendered terrain.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>Little-endian Vector3 as stored in vmap files.</summary>
public readonly record struct VmVec3(float X, float Y, float Z);

public static class VmapFormat
{
    public static readonly byte[] Magic = "VMAP_7.0"u8.ToArray();

    /// <summary>ADT grid cell size in world units.</summary>
    public const double GridSize = 533.33333;

    /// <summary>
    /// Half the map in world units: 0.5 * 64 * 533.33333 = 17066.67.
    ///
    /// VMAP FILES DO NOT STORE WORLD COORDINATES. Everything inside a .vmtile
    /// and .vmo is in VMAP's "internal representation":
    ///     internal.x = mid - world.x
    ///     internal.y = mid - world.y
    ///     internal.z = world.z          (Z is NOT shifted)
    /// It is an involution — <see cref="ToWorld"/> converts both directions —
    /// and diag(-1,-1,1) has determinant +1, a 180° rotation about Z, so
    /// triangle winding and normals survive untouched.
    /// </summary>
    public const double CoordShift = 32.0 * GridSize;

    /// <summary>vmap internal coordinates &lt;-&gt; WoW world space (its own inverse).</summary>
    public static VmVec3 ToWorld(VmVec3 v)
        => new((float)(CoordShift - v.X), (float)(CoordShift - v.Y), v.Z);

    // ModelSpawn flags (deduced from data, cross-checked against TileAssembler.cpp).
    public const uint ModM2 = 1;
    public const uint ModWorldSpawn = 2;
    public const uint ModHasBound = 4;
}

/// <summary>
/// World coords &lt;-&gt; vmap/ADT tile index. FIRST filename number = col (from
/// worldY), SECOND = row (from worldX). Northshire human start
/// (x=-8949.95, y=-132.49) -&gt; 000_32_48.vmtile.
/// </summary>
public readonly record struct VmapTileIndex(int Map, int Col, int Row)
{
    public static VmapTileIndex FromWorld(int map, double worldX, double worldY) => new(
        map,
        (int)Math.Floor(32.0 - worldY / VmapFormat.GridSize),
        (int)Math.Floor(32.0 - worldX / VmapFormat.GridSize));

    /// <summary>
    /// The vmtile for a web preset tile (gridX, gridY). The web's gridX is the
    /// ADT ROW (from worldX) and gridY is the ADT COL (from worldY) — the same
    /// order the ADT filename uses ({map}_{gridY}_{gridX}.adt) — so col=gridY,
    /// row=gridX. For Northshire (gridX=48, gridY=32) this is 000_32_48.vmtile.
    /// </summary>
    public static VmapTileIndex FromGrid(int map, int gridX, int gridY) => new(map, gridY, gridX);

    public string FileName => $"{Map:D3}_{Col:D2}_{Row:D2}.vmtile";

    public override string ToString() => $"map{Map}[{Col},{Row}]";
}

/// <summary>One model placement on a tile. Mirrors VMAP::ModelSpawn.</summary>
public sealed class VmapModelSpawn
{
    public uint Flags { get; init; }
    public ushort AdtId { get; init; }
    public uint Id { get; init; }
    public VmVec3 Position { get; init; }
    public VmVec3 Rotation { get; init; }   // Euler DEGREES, ZYX order
    public float Scale { get; init; }
    public VmVec3 BoundLo { get; init; }
    public VmVec3 BoundHi { get; init; }
    public bool HasBound => (Flags & VmapFormat.ModHasBound) != 0;
    public string Name { get; init; } = "";
    public uint NodeIndex { get; init; }

    public bool IsM2 => (Flags & VmapFormat.ModM2) != 0;

    /// <summary>
    /// Row-major 3x3 rotation, matching G3D::Matrix3::fromEulerAnglesZYX(
    ///   pi*iDir.y/180, pi*iDir.x/180, pi*iDir.z/180). Note the y/x/z argument
    /// order — NOT the obvious x/y/z; getting it wrong rotates every doodad by
    /// the wrong axis.
    /// </summary>
    public double[] BuildRotation()
    {
        double ez = Math.PI * Rotation.Y / 180.0;
        double ey = Math.PI * Rotation.X / 180.0;
        double ex = Math.PI * Rotation.Z / 180.0;

        double cz = Math.Cos(ez), sz = Math.Sin(ez);
        double cy = Math.Cos(ey), sy = Math.Sin(ey);
        double cx = Math.Cos(ex), sx = Math.Sin(ex);

        return new[]
        {
            cz * cy,  cz * sy * sx - sz * cx,  cz * sy * cx + sz * sx,
            sz * cy,  sz * sy * sx + cz * cx,  sz * sy * cx - cz * sx,
            -sy,      cy * sx,                 cy * cx,
        };
    }

    /// <summary>Model-space vertex -&gt; world space (still in vmap internal space).</summary>
    public VmVec3 TransformToWorld(VmVec3 v, double[] rot)
    {
        double sx = v.X * Scale, sy = v.Y * Scale, sz = v.Z * Scale;
        return new VmVec3(
            (float)(rot[0] * sx + rot[1] * sy + rot[2] * sz + Position.X),
            (float)(rot[3] * sx + rot[4] * sy + rot[5] * sz + Position.Y),
            (float)(rot[6] * sx + rot[7] * sy + rot[8] * sz + Position.Z));
    }
}

/// <summary>One collision mesh group. Mirrors VMAP::GroupModel.</summary>
public sealed class VmapGroupModel
{
    public uint MogpFlags { get; init; }
    public uint GroupWmoId { get; init; }
    public VmVec3[] Vertices { get; init; } = Array.Empty<VmVec3>();
    public uint[] Indices { get; init; } = Array.Empty<uint>();   // [i0,i1,i2, ...]
    public int TriangleCount => Indices.Length / 3;
}

/// <summary>A whole .vmo file. Mirrors VMAP::WorldModel.</summary>
public sealed class VmapWorldModel
{
    public uint RootWmoId { get; init; }
    public VmapGroupModel[] Groups { get; init; } = Array.Empty<VmapGroupModel>();
}

/// <summary>Little-endian cursor over a byte[]. Fixed-layout structs, no alignment surprises.</summary>
internal sealed class VmapCursor
{
    private readonly byte[] _b;
    private readonly string _what;
    public int Offset { get; private set; }
    public int Remaining => _b.Length - Offset;
    public int Length => _b.Length;

    public VmapCursor(byte[] buffer, string what) { _b = buffer; _what = what; }

    private ReadOnlySpan<byte> Take(int n)
    {
        if (Offset + n > _b.Length)
            throw new InvalidDataException(
                $"{_what}: wanted {n} bytes at offset {Offset}, only {Remaining} remain");
        var s = _b.AsSpan(Offset, n);
        Offset += n;
        return s;
    }

    public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
    public ushort U16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
    public float F32() => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(Take(4)));
    public VmVec3 V3() => new(F32(), F32(), F32());
    public string Ascii(int n) => Encoding.ASCII.GetString(Take(n));
    public void Skip(int n) => Take(n);

    public void SeekTo(int offset)
    {
        if (offset < 0 || offset > _b.Length)
            throw new InvalidDataException($"{_what}: seek to {offset} is outside the file (length {_b.Length})");
        Offset = offset;
    }

    public uint U32At(int offset)
        => offset >= 0 && offset + 4 <= _b.Length
            ? BinaryPrimitives.ReadUInt32LittleEndian(_b.AsSpan(offset, 4))
            : 0;

    public bool MatchesAt(int offset, ReadOnlySpan<byte> magic)
        => offset >= 0 && offset + magic.Length <= _b.Length
           && _b.AsSpan(offset, magic.Length).SequenceEqual(magic);

    public void Expect(ReadOnlySpan<byte> magic, string label)
    {
        int at = Offset;
        var got = Take(magic.Length);
        if (!got.SequenceEqual(magic))
            throw new InvalidDataException(
                $"{_what}: expected '{Encoding.ASCII.GetString(magic)}' ({label}) at offset {at}, " +
                $"got '{Encoding.ASCII.GetString(got)}'");
    }

    public bool Peek(ReadOnlySpan<byte> magic)
        => Remaining >= magic.Length && _b.AsSpan(Offset, magic.Length).SequenceEqual(magic);

    public VmVec3[] V3Array(int count)
    {
        var a = new VmVec3[count];
        for (int i = 0; i < count; i++) a[i] = V3();
        return a;
    }

    public uint[] U32Array(int count)
    {
        var a = new uint[count];
        for (int i = 0; i < count; i++) a[i] = U32();
        return a;
    }
}

/// <summary>
/// Reads a .vmtile — the model placements for one ADT tile. Layout (TileAssembler.cpp):
///   "VMAP_7.0" | u32 nSpawns | nSpawns x {
///     u32 flags, u16 adtId, u32 id, Vector3 pos, Vector3 rot (Euler deg, ZYX),
///     f32 scale, [AABox lo,hi] iff MOD_HAS_BOUND, u32 nameLen, char[nameLen],
///     u32 nodeIndex }
/// The trailing nodeIndex is easy to miss — without it every spawn after the
/// first is misaligned by 4 bytes and the parse silently produces garbage.
/// </summary>
internal static class VmtileReader
{
    public static IReadOnlyList<VmapModelSpawn> Read(string path)
        => Parse(File.ReadAllBytes(path), Path.GetFileName(path));

    public static IReadOnlyList<VmapModelSpawn> Parse(byte[] bytes, string what)
    {
        var c = new VmapCursor(bytes, what);
        c.Expect(VmapFormat.Magic, "file magic");

        uint n = c.U32();
        if (n > 100_000)
            throw new InvalidDataException($"{what}: implausible spawn count {n}");

        var list = new List<VmapModelSpawn>((int)n);
        for (uint i = 0; i < n; i++)
        {
            uint flags = c.U32();
            ushort adtId = c.U16();
            uint id = c.U32();
            VmVec3 pos = c.V3();
            VmVec3 rot = c.V3();
            float scale = c.F32();

            VmVec3 lo = default, hi = default;
            if ((flags & VmapFormat.ModHasBound) != 0)
            {
                lo = c.V3();
                hi = c.V3();
            }

            uint nameLen = c.U32();
            if (nameLen > 512)
                throw new InvalidDataException(
                    $"{what}: spawn {i} has implausible name length {nameLen} at offset {c.Offset} " +
                    "(usually means a misaligned read earlier in the file)");
            string name = c.Ascii((int)nameLen);
            uint nodeIndex = c.U32();

            list.Add(new VmapModelSpawn
            {
                Flags = flags, AdtId = adtId, Id = id, Position = pos, Rotation = rot,
                Scale = scale, BoundLo = lo, BoundHi = hi, Name = name, NodeIndex = nodeIndex,
            });
        }
        return list;
    }
}

/// <summary>
/// Reads a .vmo — the collision geometry for one model. Layout (WorldModel.cpp):
///   "VMAP_7.0" | "WMOD" u32 size u32 RootWMOID | "GMOD" u32 groupCount
///     per group: AABox(6f), u32 mogpFlags, u32 groupWMOID,
///       "VERT" u32 size u32 count Vector3[count]
///       "TRIM" u32 size u32 count MeshTriangle[count] (3 u32)
///       "MBIH" &lt;BIH&gt; | "LIQU" u32 size [WmoLiquid]
///   "GBIH" &lt;BIH&gt;
/// VERT/TRIM chunkSize includes the 4-byte count. BIH blobs are skipped
/// (three-mesh-bvh builds its own acceleration in the browser).
/// </summary>
internal static class VmoReader
{
    private static ReadOnlySpan<byte> Wmod => "WMOD"u8;
    private static ReadOnlySpan<byte> Gmod => "GMOD"u8;
    private static ReadOnlySpan<byte> Vert => "VERT"u8;
    private static ReadOnlySpan<byte> Trim => "TRIM"u8;
    private static ReadOnlySpan<byte> Mbih => "MBIH"u8;
    private static ReadOnlySpan<byte> Liqu => "LIQU"u8;
    private static ReadOnlySpan<byte> Gbih => "GBIH"u8;

    /// <summary>
    /// Read, keeping whatever parsed if a later group fails. Losing a whole model
    /// to one bad group is far worse than losing the group — Stormwind is a single
    /// .vmo, and a failure at group 95 of 100 would silently delete an entire
    /// city's collision. The error is reported, never swallowed.
    /// </summary>
    public static VmapWorldModel ReadTolerant(string path, out string? error)
        => ParseTolerant(File.ReadAllBytes(path), Path.GetFileName(path), out error);

    public static VmapWorldModel ParseTolerant(byte[] bytes, string what, out string? error)
    {
        error = null;
        uint rootWmoId = 0;
        var groups = new List<VmapGroupModel>();

        try
        {
            var c = new VmapCursor(bytes, what);
            c.Expect(VmapFormat.Magic, "file magic");
            c.Expect(Wmod, "root chunk");
            _ = c.U32();
            rootWmoId = c.U32();

            if (c.Peek(Gmod))
            {
                c.Expect(Gmod, "group chunk");
                uint count = c.U32();
                if (count > 100_000)
                    throw new InvalidDataException($"{what}: implausible group count {count}");
                for (uint g = 0; g < count; g++)
                    groups.Add(ReadGroup(c, what, g, g == count - 1));
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        return new VmapWorldModel { RootWmoId = rootWmoId, Groups = groups.ToArray() };
    }

    private static VmapGroupModel ReadGroup(VmapCursor c, string what, uint index, bool isLast)
    {
        _ = c.V3(); _ = c.V3();            // bound lo, hi — not needed
        uint mogpFlags = c.U32();
        uint groupWmoId = c.U32();

        c.Expect(Vert, $"group {index} vertices");
        uint vChunk = c.U32();
        uint vCount = c.U32();
        if (vChunk != vCount * 12 + 4)
            throw new InvalidDataException(
                $"{what}: group {index} VERT chunkSize {vChunk} disagrees with count {vCount} (expected {vCount * 12 + 4})");
        var vertices = c.V3Array((int)vCount);

        c.Expect(Trim, $"group {index} triangles");
        uint tChunk = c.U32();
        uint tCount = c.U32();
        if (tChunk != tCount * 12 + 4)
            throw new InvalidDataException(
                $"{what}: group {index} TRIM chunkSize {tChunk} disagrees with count {tCount} (expected {tCount * 12 + 4})");
        var indices = c.U32Array((int)tCount * 3);

        c.Expect(Mbih, $"group {index} mesh BIH");
        SkipBih(c, what, $"group {index} MBIH");

        c.Expect(Liqu, $"group {index} liquid");
        uint liqSize = c.U32();
        int liqStart = c.Offset;
        if (liqSize > 0)
        {
            int length = ResolveLiquidLength(c, what, index, liqStart, (int)liqSize, isLast);
            c.SeekTo(liqStart + length);
        }

        return new VmapGroupModel
        {
            MogpFlags = mogpFlags,
            GroupWmoId = groupWmoId,
            Vertices = vertices,
            Indices = indices,
        };
    }

    /// <summary>
    /// The declared LIQU size is wrong in the core (WmoLiquid::GetFileSize omits
    /// the u32 liquid type WmoLiquid::writeToFile then writes), so every liquid
    /// chunk on disk is exactly 4 bytes longer than its size field. Rather than
    /// hardcode +4, validate: whichever candidate length puts the following token
    /// ("VERT" for the next group, "GBIH"/EOF for the last) where it belongs wins.
    /// </summary>
    private static int ResolveLiquidLength(VmapCursor c, string what, uint index, int start, int declared, bool isLast)
    {
        int[] candidates = { declared, declared + 4 };
        foreach (int length in candidates)
        {
            int next = start + length;
            bool ok = isLast
                ? next == c.Length || c.MatchesAt(next, Gbih)
                : c.MatchesAt(next + 32, Vert);
            if (ok) return length;
        }
        throw new InvalidDataException(
            $"{what}: group {index} LIQU declares {declared} bytes at offset {start}, but neither " +
            $"{declared} nor {declared + 4} puts the following " +
            (isLast ? "\"GBIH\" or end of file" : "\"VERT\"") + " where it belongs");
    }

    /// <summary>
    /// Skip a BIH blob. From BIH::writeToFile:
    ///   float lo[3], hi[3], u32 treeSize, u32 tree[treeSize], u32 count, u32 objects[count].
    /// </summary>
    private static void SkipBih(VmapCursor c, string what, string label)
    {
        c.Skip(24);
        uint treeSize = c.U32();
        if (treeSize > 50_000_000)
            throw new InvalidDataException($"{what}: {label} implausible treeSize {treeSize}");
        c.Skip(checked((int)(treeSize * 4)));
        uint count = c.U32();
        if (count > 50_000_000)
            throw new InvalidDataException($"{what}: {label} implausible object count {count}");
        c.Skip(checked((int)(count * 4)));
    }
}

/// <summary>Stats for one bake, surfaced to the client console (loud, per the rule).</summary>
public sealed class VmapBakeStats
{
    public int TilesRequested { get; set; }
    public int TilesLoaded { get; set; }
    public int SpawnsSeen { get; set; }
    public int SpawnsUsed { get; set; }
    public int SpawnsDuplicate { get; set; }
    public int SpawnsSkippedM2 { get; set; }
    public int SpawnsUnresolved { get; set; }
    public int DistinctUnresolved { get; set; }
    public int TrianglesAdded { get; set; }
    public int DegenerateSkipped { get; set; }
    public List<string> Notes { get; } = new();
}

/// <summary>
/// Turns VMaNGOS's extracted vmaps into a flat WoW-world triangle buffer for a
/// block of ADT tiles, with cross-tile dedup. A faithful port of
/// VmapCollisionLoader — the difference is that it accumulates float triangles
/// instead of feeding a CollisionWorld, because the BVH is built in the browser.
///
/// Caches (process lifetime, keyed by vmaps dir) match the Foliage endpoint:
///   - the case-insensitive filename index of the vmaps directory
///   - parsed .vmo models
/// so a second preset load off the same block is nearly free.
/// </summary>
public static class VmapCollisionBaker
{
    // vmapDir -> (bare filename, case-insensitive) -> full path.
    private static readonly ConcurrentDictionary<string, Dictionary<string, string>> _indexCache = new();
    // "vmapDir|modelName" -> parsed model (null = looked for it, not there).
    private static readonly ConcurrentDictionary<string, VmapWorldModel?> _modelCache = new();

    private static Dictionary<string, string> FileIndex(string vmapDir)
        => _indexCache.GetOrAdd(vmapDir, dir =>
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly))
                map[Path.GetFileName(path)] = path;
            return map;
        });

    /// <summary>
    /// Bake all requested tiles into one WoW-world triangle buffer
    /// (9 floats per triangle: ax,ay,az, bx,by,bz, cx,cy,cz).
    /// </summary>
    public static float[] BakeTiles(
        string vmapDir, int mapId, IEnumerable<(int gridX, int gridY)> tiles,
        bool includeM2, VmapBakeStats stats)
    {
        var files = FileIndex(vmapDir);

        var tris = new List<float>(1 << 16);
        var seen = new HashSet<(uint, string)>();
        var unresolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (gridX, gridY) in tiles)
        {
            stats.TilesRequested++;
            var index = VmapTileIndex.FromGrid(mapId, gridX, gridY);

            if (!files.TryGetValue(index.FileName, out var tilePath))
            {
                // Normal for ocean / unextracted tiles — one quiet note.
                continue;
            }

            IReadOnlyList<VmapModelSpawn> spawns;
            try
            {
                spawns = VmtileReader.Read(tilePath);
            }
            catch (Exception ex)
            {
                // Loud, per the rule that cost 5,300 units of falling to learn:
                // a parse failure must never present as a physics bug later.
                stats.Notes.Add($"{index.FileName} FAILED to parse: {ex.Message}");
                continue;
            }

            stats.TilesLoaded++;

            foreach (var spawn in spawns)
            {
                stats.SpawnsSeen++;

                if (spawn.IsM2 && !includeM2) { stats.SpawnsSkippedM2++; continue; }

                // Deduped across the whole block: a model covering several tiles
                // (Stormwind.wmo is ONE .vmo for the whole city) is baked once.
                if (!seen.Add((spawn.Id, spawn.Name))) { stats.SpawnsDuplicate++; continue; }

                var model = ResolveModel(vmapDir, files, spawn, unresolved, stats);
                if (model is null) { stats.SpawnsUnresolved++; continue; }

                var rot = spawn.BuildRotation();

                foreach (var group in model.Groups)
                {
                    var verts = group.Vertices;
                    var idx = group.Indices;

                    for (int i = 0; i + 2 < idx.Length; i += 3)
                    {
                        uint i0 = idx[i], i1 = idx[i + 1], i2 = idx[i + 2];
                        if (i0 >= verts.Length || i1 >= verts.Length || i2 >= verts.Length) continue;

                        var a = VmapFormat.ToWorld(spawn.TransformToWorld(verts[i0], rot));
                        var b = VmapFormat.ToWorld(spawn.TransformToWorld(verts[i1], rot));
                        var c = VmapFormat.ToWorld(spawn.TransformToWorld(verts[i2], rot));

                        // Drop degenerate triangles here rather than at raycast
                        // time: a zero-area triangle produces a NaN normal, and
                        // one NaN in the ground normal makes the character fall
                        // through the floor for reasons that look nothing like
                        // the cause (CollisionWorld.AddTriangle).
                        var na = new Vector3(b.X - a.X, b.Y - a.Y, b.Z - a.Z);
                        var nb = new Vector3(c.X - a.X, c.Y - a.Y, c.Z - a.Z);
                        if (Vector3.Cross(na, nb).LengthSquared() < 1e-10f) { stats.DegenerateSkipped++; continue; }

                        tris.Add(a.X); tris.Add(a.Y); tris.Add(a.Z);
                        tris.Add(b.X); tris.Add(b.Y); tris.Add(b.Z);
                        tris.Add(c.X); tris.Add(c.Y); tris.Add(c.Z);
                        stats.TrianglesAdded++;
                    }
                }

                stats.SpawnsUsed++;
            }
        }

        stats.DistinctUnresolved = unresolved.Count;
        if (unresolved.Count > 0)
            stats.Notes.Add($"{unresolved.Count} model(s) with no .vmo: " +
                string.Join(", ", unresolved.Take(8)) + (unresolved.Count > 8 ? ", ..." : ""));

        return tris.ToArray();
    }

    private static VmapWorldModel? ResolveModel(
        string vmapDir, Dictionary<string, string> files, VmapModelSpawn spawn,
        HashSet<string> unresolved, VmapBakeStats stats)
    {
        string cacheKey = vmapDir + "|" + spawn.Name;
        if (_modelCache.TryGetValue(cacheKey, out var cached))
        {
            if (cached is null) unresolved.Add(spawn.Name);
            return cached;
        }

        string? path = null;
        foreach (var candidate in Candidates(spawn.Name))
        {
            if (files.TryGetValue(candidate, out path)) break;
            path = null;
        }

        if (path is null)
        {
            _modelCache[cacheKey] = null;
            // Most of these are decoration — torches, signposts, weapon racks —
            // that the extractor never wrote a .vmo for because they have no
            // collision geometry. Record the name once; the count is the signal.
            unresolved.Add(spawn.Name);
            return null;
        }

        var model = VmoReader.ReadTolerant(path, out string? error);
        if (error is not null)
            stats.Notes.Add($"{Path.GetFileName(path)} parsed {model.Groups.Length} group(s) then stopped: {error}");

        _modelCache[cacheKey] = model;
        return model;
    }

    /// <summary>Candidate .vmo filenames for a spawn name, most likely first.</summary>
    private static IEnumerable<string> Candidates(string name)
    {
        yield return name + ".vmo";                                  // documented convention

        if (name.EndsWith(".vmo", StringComparison.OrdinalIgnoreCase))
            yield return name;                                       // already carries the extension

        int slash = name.LastIndexOfAny(new[] { '\\', '/' });
        if (slash >= 0 && slash + 1 < name.Length)
        {
            string leaf = name[(slash + 1)..];
            yield return leaf + ".vmo";
            yield return leaf;
        }

        if (name.IndexOfAny(new[] { '\\', '/' }) >= 0)
        {
            string flat = name.Replace("\\", "").Replace("/", "");
            yield return flat + ".vmo";
            yield return flat;
        }

        int dot = name.LastIndexOf('.');
        if (dot > 0) yield return name[..dot] + ".vmo";              // extension swapped, not appended
    }
}
