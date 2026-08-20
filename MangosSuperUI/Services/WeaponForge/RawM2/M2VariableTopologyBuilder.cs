using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace MangosSuperUI.Services.WeaponForge.RawM2;

/// <summary>
/// Emits a valid MD20 v256 for ARBITRARY (variable) topology by APPENDING, not rebuilding
/// (WEAPON_GEN.md Phase 5). It keeps the entire donor file intact — so every nested bone/animation/
/// event/attachment pointer stays valid by construction — then appends fresh data at end-of-file
/// and repoints only top-level header fields. The donor's original arrays become harmless dead space.
///
/// Two emission modes:
///
///  • SINGLE-PASS (mesh.Passes == null): one submesh + one batch per view, reusing the donor's
///    preserved lookup/bone/texture tables BY INDEX (the copied batch resolves against the untouched
///    donor arrays). Optionally patches the donor render-flag record in place for alpha-key /
///    two-sided materials (fixed-width, offset-preserving).
///
///  • MULTI-PASS (mesh.Passes set — TBC imports with glow layers): N submeshes + N batches per view,
///    plus APPENDED textures / texture-lookup / render-flags tables (headers 0x5C/0x60, 0x94/0x98,
///    0x84/0x88 repointed). Texture slot 0 stays Type-2 (DBC-driven); effect slots are Type-0 with
///    hardcoded SUI_W_####_E0N.blp filenames packaged alongside — exactly how stock glowing weapons
///    bind their effect layers, so the 1.12 client needs nothing new. Batch fields not overridden
///    (transparency weight, texture transform, shader) still copy the donor template, whose indices
///    resolve against the untouched donor tables.
///
/// Two deliberate policies still require reference-client proof (they are reported by the caller):
///   1. Four EQUIVALENT views (not real per-LOD structures).
///   2. Appended layout leaving the donor's original geometry as dead bytes.
///
/// New vertices are rigidly weighted (255,0,0,0) to bone 0, matching every measured stock sword.
/// </summary>
public static class M2VariableTopologyBuilder
{
    /// <summary>Build the M2. Inputs are WoW model space (Z-up); the caller applies the §2.3
    /// contract. Indices are a flat triangle list into the vertex arrays (UInt16-safe).
    /// <paramref name="material"/> adjusts the donor render flag for single-pass output;
    /// <paramref name="effectTexturePaths"/> supplies the packaged MPQ member paths for the mesh's
    /// effect texture slots (required when mesh.Passes references slots ≥ 1).</summary>
    public static byte[] Build(byte[] donor, IReadOnlyList<Vector3> posWoW, IReadOnlyList<Vector3> normalWoW,
        IReadOnlyList<Vector2> uv0, RigidWeaponMesh mesh, int viewCount = 4,
        WeaponMaterial? material = null, IReadOnlyList<string>? effectTexturePaths = null)
    {
        var doc = RawM2Document.Parse(donor, out var err)
            ?? throw new InvalidOperationException($"Donor parse failed: {err}");
        if (doc.Views.Count == 0) throw new InvalidOperationException("Donor has no views to template from.");

        var indices = mesh.Indices;
        int n = posWoW.Count;
        if (n == 0 || n > ushort.MaxValue) throw new ArgumentException($"Vertex count {n} invalid (1..65535).");
        if (normalWoW.Count != n || uv0.Count != n) throw new ArgumentException("Vertex attribute length mismatch.");
        int t3 = indices.Length;
        if (t3 == 0 || t3 % 3 != 0) throw new ArgumentException($"Index count {t3} is not a positive multiple of 3.");
        foreach (var ix in indices) if (ix >= n) throw new ArgumentException($"Index {ix} out of range (>= {n}).");

        // Pass plan: single pseudo-pass over everything, or the mesh's own multi-pass structure.
        bool multiPass = mesh.Passes is { Count: > 0 } && mesh.SubmeshRanges is { Count: > 0 };
        var ranges = multiPass
            ? mesh.SubmeshRanges!
            : new[] { new WeaponSubmeshRange { IndexStart = 0, IndexCount = t3, VertexStart = 0, VertexCount = n } };
        var passes = multiPass
            ? mesh.Passes!
            : new[] { new WeaponPass { SubmeshSlot = 0, RenderFlags = 0, BlendMode = 0, Layer = 0, TextureSlot = 0 } };
        int nSub = ranges.Count, nBatch = passes.Count;

        int effectSlots = passes.Max(p => p.TextureSlot);
        if (effectSlots > 0 && (effectTexturePaths is null || effectTexturePaths.Count < effectSlots))
            throw new InvalidOperationException(
                $"Mesh references {effectSlots} effect texture slot(s) but only {effectTexturePaths?.Count ?? 0} path(s) were supplied.");

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
            cursor = Align4(cursor); subOff[i] = cursor; cursor += nSub * 32;
            cursor = Align4(cursor); batchOff[i] = cursor; cursor += nBatch * 24;
        }

        // Multi-pass: appended textures / texture-lookup / render-flags tables + filename strings.
        int texTableOff = 0, texLookupOff = 0, rfTableOff = 0;
        int texCount = 1 + effectSlots;
        var texNameOffs = new int[texCount];   // string offset per effect texture (0 = none)
        var texNameBytes = new byte[texCount][];
        List<(ushort Flags, ushort Blend)> rfEntries = new();
        if (multiPass)
        {
            for (int s = 1; s <= effectSlots; s++)
            {
                texNameBytes[s] = Encoding.ASCII.GetBytes(effectTexturePaths![s - 1] + "\0");
                cursor = Align4(cursor); texNameOffs[s] = cursor; cursor += texNameBytes[s].Length;
            }
            cursor = Align4(cursor); texTableOff = cursor; cursor += texCount * 16;
            cursor = Align4(cursor); texLookupOff = cursor; cursor += texCount * 2;

            foreach (var p in passes)
                if (!rfEntries.Contains((p.RenderFlags, p.BlendMode)))
                    rfEntries.Add((p.RenderFlags, p.BlendMode));
            cursor = Align4(cursor); rfTableOff = cursor; cursor += rfEntries.Count * 4;
        }

        int total = Align4(cursor);
        var outp = new byte[total];
        Array.Copy(donor, outp, donor.Length);

        // Vertices.
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
            min = Vector3.Min(min, posWoW[i]); max = Vector3.Max(max, posWoW[i]);
        }
        float radius = 0f;
        var center = (min + max) * 0.5f;
        for (int i = 0; i < n; i++) radius = MathF.Max(radius, Vector3.Distance(center, posWoW[i]));

        // Per-submesh centers (raw WoW space) for the submesh records' culling fields.
        var subCenters = new Vector3[nSub];
        for (int si = 0; si < nSub; si++)
        {
            var r = ranges[si];
            var acc = Vector3.Zero;
            int cnt = Math.Max(1, r.VertexCount);
            for (int k = 0; k < r.VertexCount && r.VertexStart + k < n; k++)
                acc += posWoW[r.VertexStart + k];
            subCenters[si] = acc / cnt;
        }

        // Four equivalent views.
        for (int vi = 0; vi < viewCount; vi++)
        {
            // vertexLookup = identity [0..n-1]; triangles reference it directly.
            for (int k = 0; k < n; k++) U16(outp, lookupOff[vi] + k * 2, (ushort)k);
            for (int k = 0; k < t3; k++) U16(outp, triOff[vi] + k * 2, (ushort)indices[k]);
            // properties: per-vertex 4-byte bone-index quads, all bone 0 (rigid). Left as zeros.

            // Submeshes: copy THIS view's donor template (preserves its per-view boneComboIndex),
            // override ranges + center per submesh.
            for (int si = 0; si < nSub; si++)
            {
                int s = subOff[vi] + si * 32;
                Array.Copy(submeshTemplates[vi], 0, outp, s, 32);
                var r = ranges[si];
                U16(outp, s + 4, (ushort)r.VertexStart);   // vertexStart
                U16(outp, s + 6, (ushort)r.VertexCount);   // vertexCount
                U16(outp, s + 8, (ushort)r.IndexStart);    // indexStart
                U16(outp, s + 10, (ushort)r.IndexCount);   // indexCount
                WriteVec3(outp, s + 20, subCenters[si]);
            }

            // Batches: copy the donor template (its transparency/transform indices resolve against
            // donor tables), then override the per-pass fields.
            for (int bi = 0; bi < nBatch; bi++)
            {
                int t = batchOff[vi] + bi * 24;
                Array.Copy(batchTemplate, 0, outp, t, 24);
                var p = passes[bi];
                U16(outp, t + 4, (ushort)p.SubmeshSlot);   // submesh index
                U16(outp, t + 6, (ushort)p.SubmeshSlot);   // geoset index (mirrors submesh in stock weapons)
                if (multiPass)
                {
                    int rfIdx = rfEntries.IndexOf((p.RenderFlags, p.BlendMode));
                    U16(outp, t + 10, (ushort)rfIdx);          // render-flag (material) index → new table
                    U16(outp, t + 12, (ushort)p.Layer);        // material layer
                    U16(outp, t + 16, (ushort)p.TextureSlot);  // texture lookup index → new identity table
                }
            }

            // view header (44 bytes). Trailing dword mirrors the donor's own per-view value.
            int h = hoff + vi * 44;
            U32(outp, h + 0, (uint)n); U32(outp, h + 4, (uint)lookupOff[vi]);      // vertexLookup
            U32(outp, h + 8, (uint)t3); U32(outp, h + 12, (uint)triOff[vi]);        // triangles
            U32(outp, h + 16, (uint)n); U32(outp, h + 20, (uint)propOff[vi]);       // properties
            U32(outp, h + 24, (uint)nSub); U32(outp, h + 28, (uint)subOff[vi]);     // submeshes
            U32(outp, h + 32, (uint)nBatch); U32(outp, h + 36, (uint)batchOff[vi]); // batches
            U32(outp, h + 40, viewTrailing[vi]);
        }

        // Multi-pass appended tables + header repoints.
        if (multiPass)
        {
            // Textures: slot 0 = Type-2 (DBC-driven; copy the donor's Type-2 flags so wrap behavior
            // matches), slots 1.. = Type-0 hardcoded members.
            uint donorTexFlags = 0;
            uint nDonorTex = BinaryPrimitives.ReadUInt32LittleEndian(donor.AsSpan(0x5C, 4));
            uint ofsDonorTex = BinaryPrimitives.ReadUInt32LittleEndian(donor.AsSpan(0x60, 4));
            if (nDonorTex > 0 && ofsDonorTex + 16 <= donor.Length)
                donorTexFlags = BinaryPrimitives.ReadUInt32LittleEndian(donor.AsSpan((int)ofsDonorTex + 4, 4));

            for (int s = 0; s < texCount; s++)
            {
                int o = texTableOff + s * 16;
                U32(outp, o + 0, s == 0 ? 2u : 0u);          // type
                U32(outp, o + 4, donorTexFlags);             // flags (wrap bits)
                if (s > 0)
                {
                    Array.Copy(texNameBytes[s], 0, outp, texNameOffs[s], texNameBytes[s].Length);
                    U32(outp, o + 8, (uint)texNameBytes[s].Length);   // filename length (incl. NUL)
                    U32(outp, o + 12, (uint)texNameOffs[s]);          // filename offset
                }
                U16(outp, texLookupOff + s * 2, (ushort)s);  // identity texture lookup
            }
            U32(outp, 0x5C, (uint)texCount); U32(outp, 0x60, (uint)texTableOff);
            U32(outp, 0x94, (uint)texCount); U32(outp, 0x98, (uint)texLookupOff);

            // Render flags: distinct (flags, blend) pairs carried verbatim from the source passes.
            for (int i = 0; i < rfEntries.Count; i++)
            {
                U16(outp, rfTableOff + i * 4 + 0, rfEntries[i].Flags);
                U16(outp, rfTableOff + i * 4 + 2, rfEntries[i].Blend);
            }
            U32(outp, 0x84, (uint)rfEntries.Count); U32(outp, 0x88, (uint)rfTableOff);
        }

        // Repoint header: vertices + views.
        U32(outp, 0x44, (uint)n); U32(outp, 0x48, (uint)voff);
        U32(outp, 0x4C, (uint)viewCount); U32(outp, 0x50, (uint)hoff);

        // Vertex bounding box + radius only (0xB4). The collision box at 0xD0 is deliberately LEFT
        // AS THE DONOR'S (all zeros for simple swords, which ship no collision geometry) — a nonzero
        // collision sphere with zero collision triangles is a state no stock weapon exhibits.
        WriteVec3(outp, 0x0B4, min); WriteVec3(outp, 0x0C0, max); WriteF(outp, 0x0CC, radius);

        // Single-pass material carry-over: the copied batch references the donor's render-flag
        // record (index at batch +10). That record is 4 fixed-width bytes inside the preserved donor
        // region, and only OUR views' batch points at it — patching in place is offset-preserving.
        if (!multiPass && material is not null &&
            (material.BlendMode != WeaponBlendMode.Opaque || material.TwoSided))
        {
            ushort rfIndex = BinaryPrimitives.ReadUInt16LittleEndian(batchTemplate.AsSpan(10, 2));
            uint nRenderFlags = BinaryPrimitives.ReadUInt32LittleEndian(outp.AsSpan(0x84, 4));
            uint ofsRenderFlags = BinaryPrimitives.ReadUInt32LittleEndian(outp.AsSpan(0x88, 4));
            if (rfIndex < nRenderFlags && ofsRenderFlags + (rfIndex + 1L) * 4 <= outp.Length)
            {
                int rf = (int)(ofsRenderFlags + rfIndex * 4u);
                if (material.TwoSided)
                {
                    ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(outp.AsSpan(rf, 2));
                    U16(outp, rf, (ushort)(flags | 0x04));
                }
                if (material.BlendMode == WeaponBlendMode.AlphaKey)
                    U16(outp, rf + 2, 1); // GxBlend_AlphaKey
            }
            else
            {
                throw new InvalidOperationException(
                    $"Donor render-flag index {rfIndex} unresolvable (n={nRenderFlags}); cannot carry the alpha/two-sided material.");
            }
        }

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