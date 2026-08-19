using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace MangosSuperUI.Services.WeaponForge.RawM2;

/// <summary>
/// A lossless, coordinate-conversion-free view of a vanilla (v256, "MD20") M2 file — the raw
/// all-array/all-view inspector that WEAPON_GEN.md §2.3 / §3 mark as the ABSENT Phase-0 blocker.
///
/// Unlike <c>M2Reader</c> (which is a render/preview reader: view-0 only, no UV1, no submesh
/// bounds, no collision/colors/events, drops batch tail bytes), this type:
///   • enumerates every top-level header M2Array by exact byte offset and range;
///   • parses ALL inline views, not just view 0;
///   • applies NO coordinate conversion — bytes are reported as they lie on disk;
///   • preserves the complete original byte buffer, so <see cref="Serialize"/> reproduces the
///     input byte-for-byte for an unmodified document (the round-trip proof Phase 0 requires).
///
/// This is deliberately a raw structural map, not a semantic decoder. It does not chase the
/// nested animation sub-arrays inside bones/colors/particles — for the simple-sword scaffold
/// those records are absent or few, and the byte-exact round trip does not depend on decoding
/// them. The Phase-3 donor-scaffold writer builds on this map to recompute offsets when geometry
/// actually changes; until then, holding the original buffer makes losslessness certain.
/// </summary>
public sealed class RawM2Document
{
    /// <summary>Vanilla v256 header size (bytes). The last header M2Array is particleEmitters at
    /// 0x13C/0x140, so the header ends at 0x144.</summary>
    public const int VanillaHeaderSize = 0x144;

    private readonly byte[] _raw;

    public string Magic { get; }
    public uint Version { get; }
    public string Name { get; }

    /// <summary>Every top-level header M2Array, in header order, with resolved count/offset/range.</summary>
    public IReadOnlyList<RawM2Array> Arrays { get; }

    /// <summary>The nViews inline view sub-structures (LOD levels). All are parsed, not just view 0.</summary>
    public IReadOnlyList<RawM2View> Views { get; }

    /// <summary>The 14 floats of the bounding-box / collision-box block at 0x0B4 (vertex box
    /// min/max/radius, then bounding box min/max/radius). Reported raw, no conversion.</summary>
    public IReadOnlyList<float> BoundsFloats { get; }

    public int FileLength => _raw.Length;

    private RawM2Document(byte[] raw, string magic, uint version, string name,
        List<RawM2Array> arrays, List<RawM2View> views, float[] bounds)
    {
        _raw = raw;
        Magic = magic;
        Version = version;
        Name = name;
        Arrays = arrays;
        Views = views;
        BoundsFloats = bounds;
    }

    /// <summary>Original bytes, byte-for-byte. For an unmodified document this IS the file.</summary>
    public byte[] Serialize() => (byte[])_raw.Clone();

    /// <summary>A raw slice of the original buffer (defensive copy), or null if out of range.</summary>
    public byte[]? Slice(long offset, long length)
    {
        if (offset < 0 || length < 0 || offset + length > _raw.Length) return null;
        var outp = new byte[length];
        Array.Copy(_raw, offset, outp, 0, length);
        return outp;
    }

    /// <summary>Convenience find by header field name (case-insensitive).</summary>
    public RawM2Array? FindArray(string name) =>
        Arrays.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Number of 48-byte vertex records.</summary>
    public int VertexCount => (int)(FindArray("vertices")?.Count ?? 0);

    // Vertex record layout (48 bytes): +0 position(3f), +12 weights(4b), +16 bones(4b),
    // +20 normal(3f), +32 uv0(2f), +40 uv1(2f). All reads are raw WoW space — no conversion.

    /// <summary>Read the raw WoW-space vertex positions (no coordinate conversion).</summary>
    public Vector3[] ReadVertexPositions() => ReadVec3PerVertex(0);

    /// <summary>Read the raw WoW-space vertex normals (no coordinate conversion).</summary>
    public Vector3[] ReadVertexNormals() => ReadVec3PerVertex(20);

    /// <summary>Read the per-vertex UV0 (top-left convention, copied verbatim).</summary>
    public Vector2[] ReadVertexUv0()
    {
        var a = FindArray("vertices");
        if (a is null || a.Count == 0) return Array.Empty<Vector2>();
        var r = new Vector2[a.Count];
        for (int i = 0; i < a.Count; i++)
        {
            int o = (int)a.Offset + i * 48 + 32;
            r[i] = new Vector2(F32(_raw, o), F32(_raw, o + 4));
        }
        return r;
    }

    /// <summary>Read a view's triangle list resolved to GLOBAL vertex indices (local triangle index
    /// → vertexLookup → global vertex). This is the flat triangle list a RigidWeaponMesh would carry
    /// for the donor topology.</summary>
    public uint[] ReadViewTrianglesGlobal(int viewIndex)
    {
        if (viewIndex < 0 || viewIndex >= Views.Count) return Array.Empty<uint>();
        var view = Views[viewIndex];
        var lookup = ReadViewVertexLookup(viewIndex);
        if (lookup.Length == 0 || view.Triangles.Count == 0) return Array.Empty<uint>();

        var tris = new uint[view.Triangles.Count];
        for (int i = 0; i < view.Triangles.Count; i++)
        {
            ushort local = BinaryPrimitives.ReadUInt16LittleEndian(_raw.AsSpan((int)view.Triangles.Offset + i * 2, 2));
            tris[i] = local < lookup.Length ? lookup[local] : 0u;
        }
        return tris;
    }

    /// <summary>Read the global vertex indices a view references (its vertexLookup array).</summary>
    public ushort[] ReadViewVertexLookup(int viewIndex)
    {
        if (viewIndex < 0 || viewIndex >= Views.Count) return Array.Empty<ushort>();
        var vl = Views[viewIndex].VertexLookup;
        if (vl.Count == 0) return Array.Empty<ushort>();
        var r = new ushort[vl.Count];
        for (int i = 0; i < vl.Count; i++)
            r[i] = BinaryPrimitives.ReadUInt16LittleEndian(_raw.AsSpan((int)vl.Offset + i * 2, 2));
        return r;
    }

    private Vector3[] ReadVec3PerVertex(int fieldOffset)
    {
        var a = FindArray("vertices");
        if (a is null || a.Count == 0) return Array.Empty<Vector3>();
        var r = new Vector3[a.Count];
        for (int i = 0; i < a.Count; i++)
        {
            int o = (int)a.Offset + i * 48 + fieldOffset;
            r[i] = new Vector3(F32(_raw, o), F32(_raw, o + 4), F32(_raw, o + 8));
        }
        return r;
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────
    // Parse
    // ─────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parse a vanilla M2 into a raw document. Returns null with a reason for anything that is not
    /// a v256 MD20 of at least header size — this is strict on purpose; the writer path only ever
    /// deals with the canonical donor family.
    /// </summary>
    public static RawM2Document? Parse(byte[] data, out string? error)
    {
        error = null;
        if (data is null || data.Length < VanillaHeaderSize)
        { error = $"Too small ({data?.Length ?? 0} bytes) for a v256 M2 header ({VanillaHeaderSize})."; return null; }

        string magic = Encoding.ASCII.GetString(data, 0, 4);
        if (magic != "MD20") { error = $"Bad magic '{magic}' (expected MD20)."; return null; }

        uint version = U32(data, 0x04);
        if (version != 256)
        { error = $"Unsupported M2 version {version} (this inspector targets vanilla v256)."; return null; }

        // Name.
        uint nName = U32(data, 0x08), ofsName = U32(data, 0x0C);
        string name = "";
        if (nName > 0 && ofsName > 0 && ofsName + nName <= data.Length)
            name = Encoding.ASCII.GetString(data, (int)ofsName, (int)nName).TrimEnd('\0');

        // Top-level header arrays, in header order.
        var arrays = new List<RawM2Array>(HeaderArraySpecs.Length);
        foreach (var spec in HeaderArraySpecs)
        {
            uint count = U32(data, spec.HeaderOffset);
            uint offset = U32(data, spec.HeaderOffset + 4);
            arrays.Add(RawM2Array.Resolve(spec, count, offset, data.Length));
        }

        // Bounds/collision float block (0x0B4, 14 floats).
        var bounds = new float[14];
        for (int i = 0; i < 14; i++)
            bounds[i] = F32(data, 0x0B4 + i * 4);

        // Views (all of them). nViews/ofsViews at 0x4C/0x50; each inline view header is 44 bytes.
        uint nViews = U32(data, 0x4C), ofsViews = U32(data, 0x50);
        var views = new List<RawM2View>();
        for (uint i = 0; i < nViews; i++)
        {
            long vh = ofsViews + (long)i * RawM2View.HeaderStride;
            var view = RawM2View.Parse(data, vh, (int)i);
            views.Add(view);
        }

        return new RawM2Document(data, magic, version, name, arrays, views, bounds);
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────
    // Header array specification table (v256). Offsets match M2Reader's documented layout.
    // Element sizes are used for range computation and coverage; records marked HasSubArrays
    // contain nested animation M2Arrays that this raw map intentionally does not chase.
    // ─────────────────────────────────────────────────────────────────────────────────────────

    internal static readonly M2ArraySpec[] HeaderArraySpecs =
    {
        new("name",                0x08, 1,   false),
        new("globalLoops",         0x14, 4,   false),
        new("sequences",           0x1C, 68,  false),
        new("animationLookup",     0x24, 2,   false),
        new("playableAnimLookup",  0x2C, 2,   false),
        new("bones",               0x34, 108, true),  // TRS animation tracks
        new("keyBoneLookup",       0x3C, 2,   false),
        new("vertices",            0x44, 48,  false),
        new("colors",              0x54, 56,  true),  // 2 M2Tracks/record
        new("textures",            0x5C, 16,  false),
        new("transparency",        0x64, 28,  true),  // M2Track/record
        new("textureFlipbooks",    0x6C, 0,   true),  // variable; absent in swords
        new("uvAnimations",        0x74, 0,   true),  // variable; absent in swords
        new("textureReplace",      0x7C, 2,   false),
        new("renderFlags",         0x84, 4,   false), // {u16 flags, u16 blend}
        new("boneLookup",          0x8C, 2,   false),
        new("textureLookup",       0x94, 2,   false),
        new("textureUnits",        0x9C, 2,   false),
        new("transparencyLookup",  0xA4, 2,   false),
        new("uvAnimationLookup",   0xAC, 2,   false),
        new("collisionTriangles",  0xEC, 2,   false),
        new("collisionVertices",   0xF4, 12,  false),
        new("collisionNormals",    0xFC, 12,  false),
        new("attachments",         0x104, 48, true),  // animateAttached track
        new("attachmentLookup",    0x10C, 2,  false),
        new("events",              0x114, 0,  true),  // variable stride; a few in swords
        new("lights",              0x11C, 0,  true),  // variable; absent in swords
        new("cameras",             0x124, 0,  true),  // variable; absent in swords
        new("cameraLookup",        0x12C, 2,  false),
        new("ribbonEmitters",      0x134, 0,  true),  // variable; absent in swords
        new("particleEmitters",    0x13C, 504, true),
    };

    private static uint U32(byte[] d, int o) => BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(o, 4));
    private static float F32(byte[] d, int o) => BinaryPrimitives.ReadSingleLittleEndian(d.AsSpan(o, 4));
}

/// <summary>Static header-slot description: where an M2Array's (count, offset) pair lives and how
/// big one record is (0 = variable/unknown, reported but not range-computed).</summary>
internal readonly record struct M2ArraySpec(string Name, int HeaderOffset, int ElementSize, bool HasSubArrays);
