using System.Buffers.Binary;
using System.Numerics;

namespace MangosSuperUI.Services.WeaponForge.RawM2;

/// <summary>
/// Emits a valid MD20 v256 for ARBITRARY (variable) topology by APPENDING, not rebuilding
/// (WEAPON_GEN.md Phase 5). It keeps the entire donor file intact — so every nested bone/animation/
/// event/attachment pointer stays valid by construction — then appends a fresh vertex block and four
/// self-contained view structures at end-of-file and repoints only the top-level header fields
/// (vertices, nViews/ofsViews, name). The donor's original vertices/views become harmless dead space.
///
/// The new views reuse the donor's preserved lookup/bone/texture tables BY INDEX (the copied batch
/// and submesh bone fields still resolve against the untouched donor arrays), so no offset in any
/// nested structure is ever moved. This sidesteps the from-scratch nested-pointer rebuild that would
/// otherwise be required — and unverifiable without the reference client.
///
/// Two deliberate policies still require reference-client proof (they are reported by the caller):
///   1. Four EQUIVALENT views (not real per-LOD structures). WEAPON_GEN.md §2.3 allows this only
///      once the client proves it.
///   2. Appended layout leaving the donor's original geometry as dead bytes.
///
/// New vertices are rigidly weighted (255,0,0,0) to bone 0, matching every measured stock sword.
/// </summary>
public static class M2VariableTopologyBuilder
{
    /// <summary>Build the M2. Inputs are WoW model space (Z-up); the caller applies the §2.3
    /// contract. Indices are a flat triangle list into the vertex arrays (UInt16-safe).</summary>
    public static byte[] Build(byte[] donor, IReadOnlyList<Vector3> posWoW, IReadOnlyList<Vector3> normalWoW,
        IReadOnlyList<Vector2> uv0, IReadOnlyList<uint> indices, int viewCount = 4)
    {
        var doc = RawM2Document.Parse(donor, out var err)
            ?? throw new InvalidOperationException($"Donor parse failed: {err}");
        if (doc.Views.Count == 0) throw new InvalidOperationException("Donor has no views to template from.");

        int n = posWoW.Count;
        if (n == 0 || n > ushort.MaxValue) throw new ArgumentException($"Vertex count {n} invalid (1..65535).");
        if (normalWoW.Count != n || uv0.Count != n) throw new ArgumentException("Vertex attribute length mismatch.");
        int t3 = indices.Count;
        if (t3 == 0 || t3 % 3 != 0) throw new ArgumentException($"Index count {t3} is not a positive multiple of 3.");
        foreach (var ix in indices) if (ix >= n) throw new ArgumentException($"Index {ix} out of range (>= {n}).");

        // Donor templates whose byte contents we reuse verbatim so their index references into the
        // donor's preserved tables stay valid. Measured donor evidence (InspectWeapon, 2026-08-18):
        // the four donor views are NOT interchangeable — each view's submesh carries a DIFFERENT
        // boneComboIndex (0/1/2/3 into the 4-entry bone lookup table) and each view header ends in a
        // DIFFERENT trailing dword (256/75/53/21). So each generated view copies its OWN donor
        // view's submesh template and trailing dword, mirroring the donor slot-for-slot.
        var v0 = doc.Views[0];
        byte[] batchTemplate = SliceOr(donor, (int)v0.Batches.Offset, 24, v0.Batches.Count > 0);
        var submeshTemplates = new byte[viewCount][];
        var viewTrailing = new uint[viewCount];
        for (int i = 0; i < viewCount; i++)
        {
            var dv = doc.Views[Math.Min(i, doc.Views.Count - 1)];
            submeshTemplates[i] = SliceOr(donor, (int)dv.Submeshes.Offset, 32, dv.Submeshes.Count > 0);
            viewTrailing[i] = dv.Lod;
        }

        // ── Layout: append after the donor, everything 4-aligned. ──────────────────────────────
        int cursor = Align4(donor.Length);
        int voff = cursor; cursor += n * 48;
        cursor = Align4(cursor);
        int hoff = cursor; cursor += viewCount * 44;

        var lookupOff = new int[viewCount];
        var triOff = new int[viewCount];
        var propOff = new int[viewCount];
        var subOff = new int[viewCount];
        var batchOff = new int[viewCount];
        for (int i = 0; i < viewCount; i++)
        {
            cursor = Align4(cursor); lookupOff[i] = cursor; cursor += n * 2;
            cursor = Align4(cursor); triOff[i] = cursor; cursor += t3 * 2;
            cursor = Align4(cursor); propOff[i] = cursor; cursor += n * 4;
            cursor = Align4(cursor); subOff[i] = cursor; cursor += 32;
            cursor = Align4(cursor); batchOff[i] = cursor; cursor += 24;
        }
        int total = Align4(cursor);

        var outp = new byte[total];
        Array.Copy(donor, outp, donor.Length);

        // Vertices.
        var centroid = Vector3.Zero;
        var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue);
        for (int i = 0; i < n; i++)
        {
            int o = voff + i * 48;
            WriteVec3(outp, o + 0, posWoW[i]);
            outp[o + 12] = 255; // weights (255,0,0,0)
            // bones (o+16..19) already zero
            WriteVec3(outp, o + 20, CoordinateContract.Normalize(normalWoW[i]));
            WriteF(outp, o + 32, uv0[i].X); WriteF(outp, o + 36, uv0[i].Y);
            // uv1 (o+40..47) zero
            centroid += posWoW[i];
            min = Vector3.Min(min, posWoW[i]); max = Vector3.Max(max, posWoW[i]);
        }
        centroid /= n;
        float radius = 0f;
        var center = (min + max) * 0.5f;
        for (int i = 0; i < n; i++) radius = MathF.Max(radius, Vector3.Distance(center, posWoW[i]));

        // Four equivalent views.
        for (int vi = 0; vi < viewCount; vi++)
        {
            // vertexLookup = identity [0..n-1]; triangles reference it directly.
            for (int k = 0; k < n; k++) U16(outp, lookupOff[vi] + k * 2, (ushort)k);
            for (int k = 0; k < t3; k++) U16(outp, triOff[vi] + k * 2, (ushort)indices[k]);
            // properties: per-vertex 4-byte bone-index quads, all bone 0 (rigid). Left as zeros.

            // submesh: copy THIS view's donor template (preserves its per-view boneComboIndex),
            // override ranges + center.
            Array.Copy(submeshTemplates[vi], 0, outp, subOff[vi], 32);
            U16(outp, subOff[vi] + 4, 0);            // vertexStart
            U16(outp, subOff[vi] + 6, (ushort)n);    // vertexCount
            U16(outp, subOff[vi] + 8, 0);            // indexStart
            U16(outp, subOff[vi] + 10, (ushort)t3);  // indexCount
            WriteVec3(outp, subOff[vi] + 20, centroid);

            // batch: copy donor template verbatim (its index references resolve against donor tables).
            Array.Copy(batchTemplate, 0, outp, batchOff[vi], 24);

            // view header (44 bytes). Trailing dword mirrors the donor's own per-view value.
            int h = hoff + vi * 44;
            U32(outp, h + 0, (uint)n); U32(outp, h + 4, (uint)lookupOff[vi]);   // vertexLookup
            U32(outp, h + 8, (uint)t3); U32(outp, h + 12, (uint)triOff[vi]);     // triangles
            U32(outp, h + 16, (uint)n); U32(outp, h + 20, (uint)propOff[vi]);    // properties
            U32(outp, h + 24, 1); U32(outp, h + 28, (uint)subOff[vi]);           // submeshes
            U32(outp, h + 32, 1); U32(outp, h + 36, (uint)batchOff[vi]);         // batches
            U32(outp, h + 40, viewTrailing[vi]);
        }

        // Repoint header: vertices + views.
        U32(outp, 0x44, (uint)n); U32(outp, 0x48, (uint)voff);
        U32(outp, 0x4C, (uint)viewCount); U32(outp, 0x50, (uint)hoff);

        // Vertex bounding box + radius only (0xB4). The collision box at 0xD0 is deliberately LEFT
        // AS THE DONOR'S (all zeros for simple swords, which ship no collision geometry) — a nonzero
        // collision sphere with zero collision triangles is a state no stock weapon exhibits.
        WriteVec3(outp, 0x0B4, min); WriteVec3(outp, 0x0C0, max); WriteF(outp, 0x0CC, radius);

        return outp;
    }

    private static byte[] SliceOr(byte[] src, int off, int len, bool present)
    {
        var b = new byte[len];
        if (present && off > 0 && off + len <= src.Length) Array.Copy(src, off, b, 0, len);
        return b;
    }

    private static int Align4(int x) => (x + 3) & ~3;
    private static void U16(byte[] b, int o, ushort v) => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(o, 2), v);
    private static void U32(byte[] b, int o, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(o, 4), v);
    private static void WriteF(byte[] b, int o, float f) => BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(o, 4), f);
    private static void WriteVec3(byte[] b, int o, Vector3 v) { WriteF(b, o, v.X); WriteF(b, o + 4, v.Y); WriteF(b, o + 8, v.Z); }
}
