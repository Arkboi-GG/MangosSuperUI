using System.Buffers.Binary;

namespace MangosSuperUI.Services.WeaponForge.RawM2;

/// <summary>
/// Independent M2 binary validation (WEAPON_GEN.md §7.2). Re-parses emitted bytes and checks the
/// structural invariants a writer must not violate: magic/version, in-bounds arrays and views,
/// view triangle indices resolving through the view vertex-lookup to valid global vertices, 48-byte
/// vertices whose weights sum to 255, a Type-2 texture, and finite bounds. It is deliberately a
/// second opinion, not a round-trip through the same reader — a same-reader round trip can pass
/// while losing data the real client needs.
/// </summary>
public static class M2BinaryValidator
{
    public static ForgeDiagnostics Validate(byte[] m2, int? expectedVertexCount = null, int? expectedViews = null)
    {
        var d = new ForgeDiagnostics("m2");

        var doc = RawM2Document.Parse(m2, out var err);
        if (doc is null) { d.Error("m2.parse", err ?? "parse failed"); return d; }
        if (doc.Version != 256) d.Error("m2.version", $"Version {doc.Version} != 256.");

        var report = RawM2Inspector.Inspect(doc);
        foreach (var anomaly in report.Anomalies) d.Error("m2.anomaly", anomaly);

        int vc = doc.VertexCount;
        if (expectedVertexCount is { } ev && vc != ev)
            d.Error("m2.vertex.count", $"Vertex count {vc} != expected {ev}.");
        if (expectedViews is { } evv && doc.Views.Count != evv)
            d.Error("m2.view.count", $"View count {doc.Views.Count} != expected {evv}.");

        // Vertices: 48 bytes each, weights sum to 255.
        var va = doc.FindArray("vertices");
        if (va is null || va.Count == 0) d.Error("m2.vertices.missing", "No vertices.");
        else if (va.InBounds)
        {
            for (int i = 0; i < va.Count; i++)
            {
                int o = (int)va.Offset + i * 48;
                if (o + 48 > m2.Length) { d.Error("m2.vertex.oob", $"Vertex {i} out of bounds."); break; }
                int sum = m2[o + 12] + m2[o + 13] + m2[o + 14] + m2[o + 15];
                if (sum != 255) d.Error("m2.vertex.weights", $"Vertex {i} weights sum to {sum}, expected 255.");
            }
        }

        // Views: triangle indices resolve through the view's vertexLookup to valid global vertices.
        for (int vi = 0; vi < doc.Views.Count; vi++)
        {
            var view = doc.Views[vi];
            if (!view.HeaderInBounds) { d.Error("m2.view.oob", $"View {vi} header out of bounds."); continue; }

            uint lookupN = view.VertexLookup.Count;
            if (view.VertexLookup.InBounds && view.Triangles.InBounds)
            {
                for (int t = 0; t < view.Triangles.Count; t++)
                {
                    ushort local = BinaryPrimitives.ReadUInt16LittleEndian(m2.AsSpan((int)view.Triangles.Offset + t * 2, 2));
                    if (local >= lookupN) { d.Error("m2.view.tri", $"View {vi} triangle index {local} >= vertexLookup count {lookupN}."); break; }
                }
                for (int k = 0; k < lookupN; k++)
                {
                    ushort global = BinaryPrimitives.ReadUInt16LittleEndian(m2.AsSpan((int)view.VertexLookup.Offset + k * 2, 2));
                    if (global >= vc) { d.Error("m2.view.lookup", $"View {vi} vertexLookup[{k}] = {global} >= vertex count {vc}."); break; }
                }
            }
            if (view.Triangles.Count % 3 != 0)
                d.Warn("m2.view.trimul3", $"View {vi} has {view.Triangles.Count} triangle indices (not a multiple of 3).");

            ValidateSubmeshes(m2, view, vi, d);
            ValidateBatches(m2, doc, view, vi, d);
        }

        ValidateTransparencyTracks(m2, doc, d);

        // One Type-2 texture (empty embedded filename is expected; not enforced here).
        var tex = doc.FindArray("textures");
        if (tex is null || tex.Count == 0) d.Error("m2.texture.missing", "No texture slot.");
        else if (tex.InBounds)
        {
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(m2.AsSpan((int)tex.Offset + 0, 4));
            if (type != 2) d.Warn("m2.texture.type", $"First texture type is {type}, expected 2 (hardcoded item texture).");
        }

        // Bounds finite.
        foreach (var f in doc.BoundsFloats)
            if (!float.IsFinite(f)) { d.Error("m2.bounds.nonfinite", "Bounds contain non-finite values."); break; }

        return d;
    }

    /// <summary>Validate every dependency of the 24-byte skin batch. This catches the class of
    /// bug where geometry is structurally valid but a pass samples donor/garbage combo tables.</summary>
    private static void ValidateBatches(byte[] m2, RawM2Document doc, RawM2View view, int viewIndex,
        ForgeDiagnostics d)
    {
        if (!view.Batches.InBounds) return;

        var textures = doc.FindArray("textures");
        var renderFlags = doc.FindArray("renderFlags");
        var textureCombos = doc.FindArray("textureLookup");
        var coordinateCombos = doc.FindArray("textureUnits");
        var transparencies = doc.FindArray("transparency");
        var weightCombos = doc.FindArray("transparencyLookup");
        var transforms = doc.FindArray("uvAnimations");
        var transformCombos = doc.FindArray("uvAnimationLookup");

        uint textureCount = textures?.Count ?? 0;
        uint renderFlagCount = renderFlags?.Count ?? 0;
        uint textureComboCount = textureCombos?.Count ?? 0;
        uint coordinateComboCount = coordinateCombos?.Count ?? 0;
        uint transparencyCount = transparencies?.Count ?? 0;
        uint weightComboCount = weightCombos?.Count ?? 0;
        uint transformCount = transforms?.Count ?? 0;
        uint transformComboCount = transformCombos?.Count ?? 0;
        uint colorCount = doc.FindArray("colors")?.Count ?? 0;
        uint textureComboOffset = textureCombos?.Offset ?? 0;
        uint weightComboOffset = weightCombos?.Offset ?? 0;
        uint transformComboOffset = transformCombos?.Offset ?? 0;

        for (int bi = 0; bi < view.Batches.Count; bi++)
        {
            int b = checked((int)view.Batches.Offset + bi * 24);
            ushort submesh = U16At(b + 4);
            short color = I16At(b + 8);
            ushort material = U16At(b + 10);
            ushort units = U16At(b + 14);
            ushort texStart = U16At(b + 16);
            ushort coordStart = U16At(b + 18);
            ushort weightStart = U16At(b + 20);
            ushort transformStart = U16At(b + 22);

            string label = $"View {viewIndex} batch {bi}";
            if (submesh >= view.Submeshes.Count)
                d.Error("m2.batch.submesh", $"{label} references submesh {submesh}, count {view.Submeshes.Count}.");
            if (material >= renderFlagCount)
                d.Error("m2.batch.material", $"{label} references render flag {material}, count {renderFlagCount}.");
            if (color >= 0 && color >= colorCount)
                d.Error("m2.batch.color", $"{label} references color {color}, count {colorCount}.");
            if (units == 0) d.Warn("m2.batch.units", $"{label} has zero texture units.");

            CheckSpan(texStart, units, textureComboCount, "texture", label);
            CheckSpan(coordStart, units, coordinateComboCount, "coordinate", label);
            CheckSpan(weightStart, units, weightComboCount, "weight", label);
            CheckSpan(transformStart, units, transformComboCount, "transform", label);

            for (int unit = 0; unit < units; unit++)
            {
                if (textureCombos?.InBounds == true && texStart + unit < textureComboCount)
                {
                    ushort texture = U16At(checked((int)textureComboOffset + (texStart + unit) * 2));
                    if (texture >= textureCount)
                        d.Error("m2.batch.texture", $"{label} unit {unit} resolves texture {texture}, count {textureCount}.");
                }
                if (weightCombos?.InBounds == true && weightStart + unit < weightComboCount)
                {
                    ushort weight = U16At(checked((int)weightComboOffset + (weightStart + unit) * 2));
                    if (weight != ushort.MaxValue && weight >= transparencyCount)
                        d.Error("m2.batch.weight", $"{label} unit {unit} resolves transparency {weight}, count {transparencyCount}.");
                }
                if (transformCombos?.InBounds == true && transformStart + unit < transformComboCount)
                {
                    ushort transform = U16At(checked((int)transformComboOffset + (transformStart + unit) * 2));
                    if (transform != ushort.MaxValue && transform >= transformCount)
                        d.Error("m2.batch.transform", $"{label} unit {unit} resolves UV transform {transform}, count {transformCount}.");
                }
            }
        }

        void CheckSpan(ushort start, ushort count, uint available, string kind, string label)
        {
            if ((uint)start + count > available)
                d.Error($"m2.batch.{kind}-span", $"{label} {kind} combo span [{start},{start + count}) exceeds count {available}.");
        }

        ushort U16At(int offset) => BinaryPrimitives.ReadUInt16LittleEndian(m2.AsSpan(offset, 2));
        short I16At(int offset) => BinaryPrimitives.ReadInt16LittleEndian(m2.AsSpan(offset, 2));
    }

    private static void ValidateSubmeshes(byte[] m2, RawM2View view, int viewIndex, ForgeDiagnostics d)
    {
        if (!view.Submeshes.InBounds) return;

        for (int si = 0; si < view.Submeshes.Count; si++)
        {
            int s = checked((int)view.Submeshes.Offset + si * 32);
            ushort vertexStart = U16At(s + 4);
            ushort vertexCount = U16At(s + 6);
            ushort indexStart = U16At(s + 8);
            ushort indexCount = U16At(s + 10);
            string label = $"View {viewIndex} submesh {si}";

            if ((uint)vertexStart + vertexCount > view.VertexLookup.Count)
                d.Error("m2.submesh.vertex-span",
                    $"{label} vertex span [{vertexStart},{(uint)vertexStart + vertexCount}) exceeds vertexLookup count {view.VertexLookup.Count}.");
            if ((uint)indexStart + indexCount > view.Triangles.Count)
                d.Error("m2.submesh.index-span",
                    $"{label} index span [{indexStart},{(uint)indexStart + indexCount}) exceeds triangle-index count {view.Triangles.Count}.");
            if (indexStart % 3 != 0 || indexCount % 3 != 0)
                d.Error("m2.submesh.triangle-alignment",
                    $"{label} index start/count ({indexStart}/{indexCount}) is not triangle-aligned.");
        }

        ushort U16At(int offset) => BinaryPrimitives.ReadUInt16LittleEndian(m2.AsSpan(offset, 2));
    }

    private static void ValidateTransparencyTracks(byte[] m2, RawM2Document doc, ForgeDiagnostics d)
    {
        var tracks = doc.FindArray("transparency");
        if (tracks is null || tracks.Count == 0 || !tracks.InBounds) return;

        uint sequenceCount = doc.FindArray("sequences")?.Count ?? 0;
        uint globalSequenceCount = doc.FindArray("globalLoops")?.Count ?? 0;
        for (int ti = 0; ti < tracks.Count; ti++)
        {
            int track = checked((int)tracks.Offset + ti * 28);
            ushort interpolation = U16At(track);
            short globalSequence = I16At(track + 2);
            uint rangeCount = U32At(track + 4), rangeOffset = U32At(track + 8);
            uint timeCount = U32At(track + 12), timeOffset = U32At(track + 16);
            uint keyCount = U32At(track + 20), keyOffset = U32At(track + 24);
            string label = $"Transparency track {ti}";

            if (interpolation > 3)
                d.Error("m2.transparency.interpolation", $"{label} has invalid interpolation type {interpolation}.");
            if (globalSequence < -1 || globalSequence >= 0 && globalSequence >= globalSequenceCount)
                d.Error("m2.transparency.global-sequence",
                    $"{label} references global sequence {globalSequence}, count {globalSequenceCount}.");

            bool rangesInBounds = CheckArray(rangeCount, rangeOffset, 8, "ranges");
            bool timesInBounds = CheckArray(timeCount, timeOffset, 4, "timestamps");
            int keyStride = interpolation is 2 or 3 ? 6 : 2;
            bool keysInBounds = CheckArray(keyCount, keyOffset, keyStride, "keys");
            if (!rangesInBounds || !timesInBounds || !keysInBounds) continue;

            if (timeCount != keyCount)
                d.Error("m2.transparency.key-count",
                    $"{label} has {timeCount} timestamp(s) but {keyCount} key(s).");
            if (globalSequence == -1 && sequenceCount > 0 && rangeCount > 0 && rangeCount < sequenceCount)
                d.Error("m2.transparency.range-count",
                    $"{label} has {rangeCount} range(s) for {sequenceCount} animation sequence(s).");

            for (int ri = 0; ri < rangeCount; ri++)
            {
                int range = checked((int)rangeOffset + ri * 8);
                uint start = U32At(range), end = U32At(range + 4);
                if (start > end || end >= timeCount || end >= keyCount)
                    d.Error("m2.transparency.range",
                        $"{label} range {ri} [{start},{end}] is invalid for {timeCount} timestamp(s) and {keyCount} key(s).");
            }

            bool CheckArray(uint count, uint offset, int stride, string name)
            {
                bool valid = count == 0 || offset > 0 && offset + (long)count * stride <= m2.Length;
                if (!valid)
                    d.Error("m2.transparency.array-oob",
                        $"{label} {name} array count={count}, offset=0x{offset:X} is out of bounds.");
                return valid;
            }
        }

        ushort U16At(int offset) => BinaryPrimitives.ReadUInt16LittleEndian(m2.AsSpan(offset, 2));
        short I16At(int offset) => BinaryPrimitives.ReadInt16LittleEndian(m2.AsSpan(offset, 2));
        uint U32At(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(m2.AsSpan(offset, 4));
    }
}
