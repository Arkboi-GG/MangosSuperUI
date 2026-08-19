namespace MangosSuperUI.Services.WeaponForge.RawM2;

/// <summary>Serializable structural report for one M2 — the artifact the Phase-0 raw inspector
/// emits for a corpus/donor audit. All offsets/ranges are raw file bytes, no conversion.</summary>
public sealed record RawM2Report
{
    public required string Magic { get; init; }
    public required uint Version { get; init; }
    public required string Name { get; init; }
    public required int FileLength { get; init; }
    public required int HeaderSize { get; init; }
    public required int ViewCount { get; init; }

    public required IReadOnlyList<ArrayRow> Arrays { get; init; }
    public required IReadOnlyList<ViewRow> Views { get; init; }
    public required IReadOnlyList<float> BoundsFloats { get; init; }

    /// <summary>Bytes the structural map accounts for (header + known-stride arrays + view headers
    /// and their sub-arrays). Bytes belonging to variable-stride records (bones' TRS tracks,
    /// events, particles) are NOT counted and appear as gaps — expected for a raw map that does
    /// not chase nested animation data.</summary>
    public required long CoveredBytes { get; init; }
    public required long GapBytes { get; init; }

    /// <summary>Structural anomalies: out-of-bounds arrays/views, overlaps between known regions.</summary>
    public required IReadOnlyList<string> Anomalies { get; init; }

    public sealed record ArrayRow(string Name, int HeaderOffset, uint Count, uint Offset,
        int ElementSize, long? ByteLength, bool HasSubArrays, bool InBounds);

    public sealed record ViewRow(int Index, long HeaderOffset, bool InBounds,
        uint VertexLookupCount, uint TriangleCount, uint SubmeshCount, uint BatchCount, uint Lod);
}

/// <summary>Builds a <see cref="RawM2Report"/> from a parsed document and proves the byte-exact
/// round trip. Pure — testable against a synthetic MD20 with no proprietary client data.</summary>
public static class RawM2Inspector
{
    public static RawM2Report Inspect(RawM2Document doc)
    {
        var anomalies = new List<string>();
        var intervals = new List<(long start, long end)>();

        void Cover(long start, long len)
        {
            if (len > 0) intervals.Add((start, start + len));
        }

        // Header block.
        Cover(0, RawM2Document.VanillaHeaderSize);

        var arrayRows = new List<RawM2Report.ArrayRow>(doc.Arrays.Count);
        foreach (var a in doc.Arrays)
        {
            if (!a.InBounds)
                anomalies.Add($"array '{a.Name}' out of bounds: count={a.Count} offset=0x{a.Offset:X} len={a.ByteLength?.ToString() ?? "?"}");
            if (a.ByteLength is { } len && a.Count > 0)
                Cover(a.Offset, len);
            arrayRows.Add(new RawM2Report.ArrayRow(a.Name, a.HeaderOffset, a.Count, a.Offset,
                a.ElementSize, a.ByteLength, a.HasSubArrays, a.InBounds));
        }

        var viewRows = new List<RawM2Report.ViewRow>(doc.Views.Count);
        foreach (var v in doc.Views)
        {
            if (!v.HeaderInBounds)
                anomalies.Add($"view {v.Index} header out of bounds at 0x{v.HeaderOffset:X} (stride/count may be wrong)");
            else
            {
                Cover(v.HeaderOffset, RawM2View.HeaderStride);
                foreach (var sub in new[] { v.VertexLookup, v.Triangles, v.Properties, v.Submeshes, v.Batches })
                {
                    if (!sub.InBounds) anomalies.Add($"view {v.Index} sub-array out of bounds: count={sub.Count} offset=0x{sub.Offset:X}");
                    if (sub.Count > 0) Cover(sub.Offset, sub.ByteLength);
                }
            }
            viewRows.Add(new RawM2Report.ViewRow(v.Index, v.HeaderOffset, v.HeaderInBounds,
                v.VertexLookup.Count, v.Triangles.Count, v.Submeshes.Count, v.Batches.Count, v.Lod));
        }

        var (covered, overlaps) = UnionAndOverlaps(intervals, doc.FileLength);
        anomalies.AddRange(overlaps);

        return new RawM2Report
        {
            Magic = doc.Magic,
            Version = doc.Version,
            Name = doc.Name,
            FileLength = doc.FileLength,
            HeaderSize = RawM2Document.VanillaHeaderSize,
            ViewCount = doc.Views.Count,
            Arrays = arrayRows,
            Views = viewRows,
            BoundsFloats = doc.BoundsFloats,
            CoveredBytes = covered,
            GapBytes = doc.FileLength - covered,
            Anomalies = anomalies,
        };
    }

    /// <summary>Round-trip proof: a parse of <paramref name="original"/> must serialize back to the
    /// exact same bytes. This is the Phase-0 losslessness gate (and passes trivially for an
    /// unmodified document because the buffer is preserved — the point is to make regressions in a
    /// future rebuild path visible).</summary>
    public static bool RoundTripsExact(byte[] original)
    {
        var doc = RawM2Document.Parse(original, out _);
        if (doc is null) return false;
        var reemit = doc.Serialize();
        return original.AsSpan().SequenceEqual(reemit);
    }

    private static (long covered, List<string> overlaps) UnionAndOverlaps(
        List<(long start, long end)> intervals, int fileLength)
    {
        var overlaps = new List<string>();
        if (intervals.Count == 0) return (0, overlaps);

        intervals.Sort((x, y) => x.start.CompareTo(y.start));
        long covered = 0;
        long curStart = intervals[0].start, curEnd = intervals[0].end;

        for (int i = 1; i < intervals.Count; i++)
        {
            var (s, e) = intervals[i];
            if (s < curEnd)
            {
                // Overlap between two KNOWN regions is worth flagging (shared data is the only
                // legitimate case, e.g. a lookup reused across views).
                if (s < curEnd && e > curStart && !(curStart == s && curEnd == e))
                    overlaps.Add($"region overlap: [0x{s:X},0x{e:X}) intersects [0x{curStart:X},0x{curEnd:X})");
                curEnd = Math.Max(curEnd, e);
            }
            else
            {
                covered += curEnd - curStart;
                curStart = s; curEnd = e;
            }
        }
        covered += curEnd - curStart;
        return (Math.Min(covered, fileLength), overlaps);
    }
}
