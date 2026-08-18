namespace MangosSuperUI.Services.WeaponForge.RawM2;

/// <summary>A resolved top-level header M2Array: the (count, offset) pair read from the header,
/// plus its computed byte range and an in-bounds verdict. No conversion, no record decoding —
/// this is the exact structural map WEAPON_GEN.md §3 asks the raw inspector to produce.</summary>
public sealed class RawM2Array
{
    /// <summary>Header field name (e.g. "vertices", "attachments").</summary>
    public required string Name { get; init; }

    /// <summary>Byte offset of the (count, offset) pair within the file header.</summary>
    public required int HeaderOffset { get; init; }

    /// <summary>Record count as declared in the header.</summary>
    public required uint Count { get; init; }

    /// <summary>Absolute byte offset of the first record, or 0 for an empty array.</summary>
    public required uint Offset { get; init; }

    /// <summary>Size of one record in bytes, or 0 when the stride is variable/unknown (nested
    /// animation structures) — in which case <see cref="ByteLength"/> is null.</summary>
    public required int ElementSize { get; init; }

    /// <summary>True when records contain nested M2Arrays this raw map does not chase.</summary>
    public required bool HasSubArrays { get; init; }

    /// <summary>Computed byte length (Count × ElementSize) when the stride is known; null when
    /// <see cref="ElementSize"/> is 0.</summary>
    public long? ByteLength { get; init; }

    /// <summary>End offset (exclusive) of the record block when computable.</summary>
    public long? EndOffset => ByteLength is { } len ? Offset + len : null;

    /// <summary>False when a non-empty array's computed range falls outside the file — a corruption
    /// or a stride/offset mismatch worth surfacing.</summary>
    public required bool InBounds { get; init; }

    internal static RawM2Array Resolve(M2ArraySpec spec, uint count, uint offset, int fileLength)
    {
        long? byteLen = spec.ElementSize > 0 ? (long)count * spec.ElementSize : (long?)null;

        bool inBounds;
        if (count == 0)
            inBounds = true; // empty array — offset is conventionally 0 and not dereferenced
        else if (byteLen is { } len)
            inBounds = offset > 0 && offset + len <= fileLength;
        else
            inBounds = offset > 0 && offset < fileLength; // unknown stride: at least the start must be valid

        return new RawM2Array
        {
            Name = spec.Name,
            HeaderOffset = spec.HeaderOffset,
            Count = count,
            Offset = offset,
            ElementSize = spec.ElementSize,
            HasSubArrays = spec.HasSubArrays,
            ByteLength = byteLen,
            InBounds = inBounds,
        };
    }
}
