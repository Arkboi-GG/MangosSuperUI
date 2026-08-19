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
        else
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
            if (view.Triangles.Count % 3 != 0)
                d.Warn("m2.view.trimul3", $"View {vi} has {view.Triangles.Count} triangle indices (not a multiple of 3).");
        }

        // One Type-2 texture (empty embedded filename is expected; not enforced here).
        var tex = doc.FindArray("textures");
        if (tex is null || tex.Count == 0) d.Error("m2.texture.missing", "No texture slot.");
        else
        {
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(m2.AsSpan((int)tex.Offset + 0, 4));
            if (type != 2) d.Warn("m2.texture.type", $"First texture type is {type}, expected 2 (hardcoded item texture).");
        }

        // Bounds finite.
        foreach (var f in doc.BoundsFloats)
            if (!float.IsFinite(f)) { d.Error("m2.bounds.nonfinite", "Bounds contain non-finite values."); break; }

        return d;
    }
}
