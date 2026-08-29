using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace MangosSuperUI.Services.WeaponForge.RawM2;

/// <summary>
/// Offset-preserving geometry surgery on a donor M2 (WEAPON_GEN.md Phase 2/3, fixed topology). It
/// overwrites the vertex block's position/normal/UV0 fields IN PLACE — same vertex count, same file
/// layout, no offset moves — and recomputes the fixed-width culling metadata (model bounds + each
/// view's submesh centers) so nothing stale is left behind. Because no section changes size, every
/// nested pointer in the donor (bones, animation tracks, attachments, events) stays valid by
/// construction. This is why fixed-topology generation does NOT need a from-scratch offset rebuild.
///
/// Inputs are in raw WoW model space (Z-up) already — the caller applies the §2.3 coordinate
/// contract. Vertex weights, bone indices, and UV1 are preserved untouched (they survive the clone).
/// </summary>
public static class M2GeometryPatcher
{
    /// <summary>Overwrite the donor's vertex geometry with new WoW-space positions/normals/UV0 and
    /// refresh bounds + submesh centers. The three arrays must each have exactly the donor's vertex
    /// count. Length-preserving: the returned buffer is the same size as the donor.</summary>
    public static M2PatchResult Patch(byte[] donor, IReadOnlyList<Vector3> posWoW,
        IReadOnlyList<Vector3> normalWoW, IReadOnlyList<Vector2> uv0)
    {
        var doc = RawM2Document.Parse(donor, out var err)
            ?? throw new InvalidOperationException($"Donor M2 parse failed: {err}");
        var va = doc.FindArray("vertices")
            ?? throw new InvalidOperationException("Donor has no vertices array.");

        int n = (int)va.Count;
        if (posWoW.Count != n || normalWoW.Count != n || uv0.Count != n)
            throw new ArgumentException($"Vertex update count mismatch: donor has {n}, got pos={posWoW.Count} nrm={normalWoW.Count} uv={uv0.Count}.");

        var outp = (byte[])donor.Clone();

        // Overwrite pos(+0), normal(+20), uv0(+32) per 48-byte vertex; leave weights/bones/uv1 intact.
        for (int i = 0; i < n; i++)
        {
            int o = (int)va.Offset + i * 48;
            WriteVec3(outp, o + 0, posWoW[i]);
            WriteVec3(outp, o + 20, normalWoW[i]);
            WriteFloat(outp, o + 32, uv0[i].X);
            WriteFloat(outp, o + 36, uv0[i].Y);
        }

        // Model bounds (WEAPON_GEN.md §2.3: radius encloses geometry relative to the bounds CENTER,
        // never origin-based). Both the vertex box and the bounding box are set to the vertex AABB.
        var (min, max, center, radius) = ComputeBounds(posWoW);
        WriteVec3(outp, 0x0B4, min);   // vertexBox.min
        WriteVec3(outp, 0x0C0, max);   // vertexBox.max
        WriteFloat(outp, 0x0CC, radius);
        WriteVec3(outp, 0x0D0, min);   // boundingBox.min
        WriteVec3(outp, 0x0DC, max);   // boundingBox.max
        WriteFloat(outp, 0x0E8, radius);

        // Submesh centers: the geometry centroid, written into every submesh of every view. For the
        // donor's single-submesh views this is exact; it is never stale (reflects the new geometry).
        var centroid = Centroid(posWoW);
        foreach (var view in doc.Views)
        {
            var sm = view.Submeshes;
            for (int s = 0; s < sm.Count; s++)
            {
                int off = (int)sm.Offset + s * sm.ElementSize + 20; // centerPosition at +20
                if (off + 12 <= outp.Length) WriteVec3(outp, off, centroid);
            }
        }

        return new M2PatchResult
        {
            Bytes = outp,
            BoundsMin = min,
            BoundsMax = max,
            BoundsCenter = center,
            Radius = radius,
            Centroid = centroid,
            VertexCount = n,
        };
    }

    /// <summary>
    /// Rewrite the M2's internal name to a canonical value by appending it at end-of-file (4-aligned)
    /// and repointing (nName, ofsName). This changes no existing offset — the old name bytes become
    /// harmless dead space — so it is the low-risk alternative to a full offset rebuild for giving a
    /// generated model its canonical <c>SUI_W_####</c> identity (WEAPON_GEN.md §2.3/Route 0 note).
    /// </summary>
    public static byte[] RewriteInternalName(byte[] input, string name)
    {
        var list = new List<byte>(input);
        while (list.Count % 4 != 0) list.Add(0);      // align the append point
        int ofs = list.Count;

        var nameBytes = Encoding.ASCII.GetBytes(name);
        list.AddRange(nameBytes);
        list.Add(0);                                   // null terminator (nName includes it)
        int nName = nameBytes.Length + 1;
        while (list.Count % 4 != 0) list.Add(0);       // keep the file 4-aligned

        var outp = list.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(outp.AsSpan(0x08), (uint)nName);
        BinaryPrimitives.WriteUInt32LittleEndian(outp.AsSpan(0x0C), (uint)ofs);
        return outp;
    }

    /// <summary>
    /// Repoint selected hardcoded (Type-0) texture slots to new MPQ paths without rebuilding the
    /// source model. Vanilla's 16-byte texture record is
    /// <c>{ uint type; uint flags; uint nFilename; uint ofsFilename; }</c>. Longer filenames are
    /// appended at EOF (each 4-aligned), then only <c>nFilename</c>/<c>ofsFilename</c> at +8/+12 in
    /// the selected records are changed. Every pre-existing byte outside those eight-byte fields,
    /// including all animation data and every other absolute offset, remains untouched.
    /// </summary>
    /// <remarks>
    /// Replacements are keyed by the M2 texture-table index, not by filename: two stock slots may
    /// legally name the same BLP while only one is selected by the render graph. The operation
    /// validates the complete request before producing output and never mutates
    /// <paramref name="input"/>. An empty replacement set is an exact no-op.
    /// </remarks>
    public static byte[] RewriteHardcodedTexturePaths(byte[] input,
        IReadOnlyDictionary<int, string> replacements)
        => RewriteHardcodedTexturePaths(input, replacements, expectedSourcePaths: null);

    /// <summary>Repoint selected Type-0 slots, additionally proving that every selected record still
    /// names the source member its prepared BLP came from. This prevents a stale slot map from
    /// pairing one effect's pixels with another effect's texture record.</summary>
    public static byte[] RewriteHardcodedTexturePaths(byte[] input,
        IReadOnlyDictionary<int, string> replacements,
        IReadOnlyDictionary<int, string>? expectedSourcePaths)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(replacements);
        if (replacements.Count == 0) return input;
        if (expectedSourcePaths is not null &&
            (expectedSourcePaths.Count != replacements.Count ||
             replacements.Keys.Any(key => !expectedSourcePaths.ContainsKey(key))))
            throw new ArgumentException(
                "Expected source paths must name exactly the texture slots being replaced.",
                nameof(expectedSourcePaths));

        var doc = RawM2Document.Parse(input, out var error)
            ?? throw new InvalidOperationException($"M2 parse failed: {error}");
        var textures = doc.FindArray("textures")
            ?? throw new InvalidOperationException("M2 has no texture array.");
        if (!textures.InBounds)
            throw new InvalidOperationException("M2 texture array runs past EOF.");

        var pending = new List<(int RecordOffset, byte[] PathBytes)>(replacements.Count);
        foreach (var (textureIndex, newPath) in replacements.OrderBy(pair => pair.Key))
        {
            if (textureIndex < 0 || (uint)textureIndex >= textures.Count)
                throw new ArgumentOutOfRangeException(nameof(replacements),
                    $"Texture slot {textureIndex} is outside the table of {textures.Count} slot(s).");
            if (string.IsNullOrWhiteSpace(newPath))
                throw new ArgumentException($"Texture slot {textureIndex} has an empty replacement path.",
                    nameof(replacements));
            if (newPath.Any(ch => ch == '\0' || ch > 0x7F))
                throw new ArgumentException(
                    $"Texture slot {textureIndex} replacement path must contain only non-NUL ASCII characters.",
                    nameof(replacements));

            int recordOffset = checked((int)textures.Offset + textureIndex * 16);
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(input.AsSpan(recordOffset, 4));
            if (type != 0)
                throw new InvalidOperationException(
                    $"Texture slot {textureIndex} is Type {type}; only hardcoded Type-0 slots may be repointed.");

            uint oldLength = BinaryPrimitives.ReadUInt32LittleEndian(input.AsSpan(recordOffset + 8, 4));
            uint oldOffset = BinaryPrimitives.ReadUInt32LittleEndian(input.AsSpan(recordOffset + 12, 4));
            if (oldLength < 2 || oldOffset == 0 || oldOffset + (long)oldLength > input.Length ||
                input[checked((int)(oldOffset + oldLength - 1))] != 0)
            {
                throw new InvalidOperationException(
                    $"Texture slot {textureIndex} has an invalid hardcoded filename range.");
            }

            if (expectedSourcePaths is not null)
            {
                string? expected = WeaponTexturePath.Canonicalize(expectedSourcePaths[textureIndex]);
                string actualRaw = Encoding.ASCII.GetString(input,
                    checked((int)oldOffset), checked((int)oldLength - 1));
                string? actual = WeaponTexturePath.Canonicalize(actualRaw);
                if (expected is null)
                    throw new ArgumentException(
                        $"Texture slot {textureIndex} has an empty expected source path.",
                        nameof(expectedSourcePaths));
                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        $"Texture slot {textureIndex} names '{actualRaw}', not the prepared source " +
                        $"'{expectedSourcePaths[textureIndex]}'; refusing to pair the wrong BLP with it.");
            }

            byte[] pathBytes = Encoding.ASCII.GetBytes(newPath + "\0");
            pending.Add((recordOffset, pathBytes));
        }

        var list = new List<byte>(input);
        var appendedOffsets = new int[pending.Count];
        for (int i = 0; i < pending.Count; i++)
        {
            while (list.Count % 4 != 0) list.Add(0);
            appendedOffsets[i] = list.Count;
            list.AddRange(pending[i].PathBytes);
        }
        while (list.Count % 4 != 0) list.Add(0);

        var output = list.ToArray();
        for (int i = 0; i < pending.Count; i++)
        {
            var (recordOffset, pathBytes) = pending[i];
            BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(recordOffset + 8, 4),
                checked((uint)pathBytes.Length));
            BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(recordOffset + 12, 4),
                checked((uint)appendedOffsets[i]));
        }
        return output;
    }

    /// <summary>
    /// Move the donor's attachment points (vanilla v256: header 0x104/0x108, 48-byte records, id at
    /// +0, position at +8) to new WoW-space positions, keyed by attachment id. Offset-preserving —
    /// only the 12 position bytes of matching records change. Weapon attachments 0..4 are where the
    /// client hangs enchant/ItemVisual effects along the blade, so an imported model whose blade
    /// runs somewhere else than the donor's gets its glow where its own geometry is.
    /// </summary>
    public static byte[] RewriteAttachmentPositions(byte[] input, IReadOnlyDictionary<uint, Vector3> positionsWoW)
    {
        if (positionsWoW.Count == 0) return input;
        var outp = (byte[])input.Clone();
        uint n = BinaryPrimitives.ReadUInt32LittleEndian(outp.AsSpan(0x104));
        uint ofs = BinaryPrimitives.ReadUInt32LittleEndian(outp.AsSpan(0x108));
        if (n == 0 || ofs == 0 || ofs + (long)n * 48 > outp.Length) return input;
        for (uint i = 0; i < n; i++)
        {
            int o = (int)(ofs + i * 48);
            uint id = BinaryPrimitives.ReadUInt32LittleEndian(outp.AsSpan(o));
            if (positionsWoW.TryGetValue(id, out var p)) WriteVec3(outp, o + 8, p);
        }
        return outp;
    }

    /// <summary>Read the donor's attachment points (id → WoW position) — see <see cref="RewriteAttachmentPositions"/>.</summary>
    public static Dictionary<uint, Vector3> ReadAttachmentPositions(byte[] m2)
    {
        var map = new Dictionary<uint, Vector3>();
        if (m2.Length < 0x110) return map;
        uint n = BinaryPrimitives.ReadUInt32LittleEndian(m2.AsSpan(0x104));
        uint ofs = BinaryPrimitives.ReadUInt32LittleEndian(m2.AsSpan(0x108));
        if (n == 0 || ofs == 0 || ofs + (long)n * 48 > m2.Length) return map;
        for (uint i = 0; i < n; i++)
        {
            int o = (int)(ofs + i * 48);
            uint id = BinaryPrimitives.ReadUInt32LittleEndian(m2.AsSpan(o));
            map[id] = new Vector3(BinaryPrimitives.ReadSingleLittleEndian(m2.AsSpan(o + 8)),
                BinaryPrimitives.ReadSingleLittleEndian(m2.AsSpan(o + 12)), BinaryPrimitives.ReadSingleLittleEndian(m2.AsSpan(o + 16)));
        }
        return map;
    }

    private static (Vector3 min, Vector3 max, Vector3 center, float radius) ComputeBounds(IReadOnlyList<Vector3> pts)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var p in pts) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
        var center = (min + max) * 0.5f;
        float radius = 0f;
        foreach (var p in pts) radius = MathF.Max(radius, Vector3.Distance(center, p));
        return (min, max, center, radius);
    }

    private static Vector3 Centroid(IReadOnlyList<Vector3> pts)
    {
        if (pts.Count == 0) return Vector3.Zero;
        var sum = Vector3.Zero;
        foreach (var p in pts) sum += p;
        return sum / pts.Count;
    }

    private static void WriteVec3(byte[] b, int o, Vector3 v)
    {
        WriteFloat(b, o + 0, v.X);
        WriteFloat(b, o + 4, v.Y);
        WriteFloat(b, o + 8, v.Z);
    }

    private static void WriteFloat(byte[] b, int o, float f) =>
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(o, 4), f);
}

/// <summary>Result of an offset-preserving geometry patch.</summary>
public sealed class M2PatchResult
{
    public required byte[] Bytes { get; init; }
    public required Vector3 BoundsMin { get; init; }
    public required Vector3 BoundsMax { get; init; }
    public required Vector3 BoundsCenter { get; init; }
    public required float Radius { get; init; }
    public required Vector3 Centroid { get; init; }
    public required int VertexCount { get; init; }
}
