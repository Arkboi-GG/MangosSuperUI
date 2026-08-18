using System.Buffers.Binary;

namespace MangosSuperUI.Services.WeaponForge.RawM2;

/// <summary>
/// One inline view (LOD skin) of a vanilla M2, parsed structurally. The vanilla inline view header
/// is 44 bytes: five (count, offset) M2Arrays followed by a trailing LOD dword —
///   +0  vertexLookup  (uint16 → global vertex index)
///   +8  triangles     (uint16 → index into vertexLookup)
///   +16 properties    (4-byte vertex properties / bone influences)
///   +24 submeshes     (32-byte records: id, bounds, vertex/index ranges)
///   +32 batches       (24-byte texture-unit records)
///   +40 lod           (uint32)
/// This matches the field offsets M2Reader.ParseInlinedView reads. WEAPON_GEN.md §2.3 confirms the
/// golden donor's four views are genuinely distinct (their vertexLookup / triangle map / submesh
/// records differ per view), so all four are parsed here rather than assumed duplicates.
/// </summary>
public sealed class RawM2View
{
    public const int HeaderStride = 44;

    public required int Index { get; init; }
    public required long HeaderOffset { get; init; }

    /// <summary>False if this view's header falls outside the file (e.g. a wrong stride guess for
    /// views beyond 0). Surfaced by the inspector rather than silently mis-parsed.</summary>
    public required bool HeaderInBounds { get; init; }

    public required RawM2SubArray VertexLookup { get; init; }
    public required RawM2SubArray Triangles { get; init; }
    public required RawM2SubArray Properties { get; init; }
    public required RawM2SubArray Submeshes { get; init; }
    public required RawM2SubArray Batches { get; init; }
    public required uint Lod { get; init; }

    internal static RawM2View Parse(byte[] data, long headerOffset, int index)
    {
        bool inBounds = headerOffset >= 0 && headerOffset + HeaderStride <= data.Length;
        if (!inBounds)
        {
            return new RawM2View
            {
                Index = index,
                HeaderOffset = headerOffset,
                HeaderInBounds = false,
                VertexLookup = RawM2SubArray.Empty,
                Triangles = RawM2SubArray.Empty,
                Properties = RawM2SubArray.Empty,
                Submeshes = RawM2SubArray.Empty,
                Batches = RawM2SubArray.Empty,
                Lod = 0,
            };
        }

        int h = (int)headerOffset;
        return new RawM2View
        {
            Index = index,
            HeaderOffset = headerOffset,
            HeaderInBounds = true,
            VertexLookup = RawM2SubArray.Read(data, h + 0, 2),
            Triangles = RawM2SubArray.Read(data, h + 8, 2),
            Properties = RawM2SubArray.Read(data, h + 16, 4),
            Submeshes = RawM2SubArray.Read(data, h + 24, 32),
            Batches = RawM2SubArray.Read(data, h + 32, 24),
            Lod = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(h + 40, 4)),
        };
    }
}

/// <summary>A (count, offset) M2Array nested inside a view header, with its computed byte range.</summary>
public sealed class RawM2SubArray
{
    public required uint Count { get; init; }
    public required uint Offset { get; init; }
    public required int ElementSize { get; init; }
    public long ByteLength => (long)Count * ElementSize;
    public long EndOffset => Offset + ByteLength;
    public required bool InBounds { get; init; }

    public static RawM2SubArray Empty { get; } = new() { Count = 0, Offset = 0, ElementSize = 0, InBounds = true };

    internal static RawM2SubArray Read(byte[] data, int headerPos, int elementSize)
    {
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(headerPos, 4));
        uint offset = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(headerPos + 4, 4));
        bool inBounds = count == 0 || (offset > 0 && offset + (long)count * elementSize <= data.Length);
        return new RawM2SubArray { Count = count, Offset = offset, ElementSize = elementSize, InBounds = inBounds };
    }
}
