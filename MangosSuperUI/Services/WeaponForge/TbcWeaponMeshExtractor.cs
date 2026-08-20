using System.Numerics;

namespace MangosSuperUI.Services.WeaponForge;

/// <summary>
/// Converts a parsed TBC weapon M2 into the pipeline's multi-pass <see cref="RigidWeaponMesh"/>.
/// Measured TBC anatomy (Warglaive, 2026-08-19): several batches layer over shared submeshes —
/// an opaque BASE pass whose texture is often a HARDCODED (Type-0) file baked into the M2, plus
/// reflect/tint overlays and ADDITIVE GLOW shells sampling the DBC-driven Type-2 slot (that's how
/// one glaive model serves green/blue variants).
///
/// Selection rules:
///   • per submesh, ONE base pass — the lowest-layer visible batch with blend 0–2; its geometry,
///     blend/two-sided bits, and texture define the submesh;
///   • additive passes (blend 3/4) are KEPT as their own passes — this is the glow; their submesh
///     geometry is included and their texture (usually the DBC slot) becomes an effect texture;
///   • modulate/reflect passes (blend 5/6) are dropped — they depend on env-mapping the static
///     import can't reproduce;
///   • extra alpha overlays beyond the base (same submesh, higher layer, blend 1/2) are dropped;
///   • idle-invisible batches (static transparency &lt; 0.5) are dropped.
///
/// Texture slots are keyed by SOURCE IMAGE: slot 0 = whatever the dominant base pass samples
/// (packaged as the weapon's DBC-driven texture), each further distinct image = an effect slot
/// (packaged as a Type-0 hardcoded SUI_W_####_E0N.blp). <see cref="TbcExtractResult.SourceTextures"/>
/// maps slots to TBC archive paths (null = the display row's Type-2 texture).
///
/// Geometry is compacted per submesh into contiguous vertex/index blocks (shared vertices are
/// duplicated — weapons are small). Models with no usable batch tables fall back to a single
/// opaque pass over the whole triangle list. TBC geometry is already palm-at-origin in WoW units,
/// so positions pass through untouched apart from a degenerate sweep and a UV clamp.
/// </summary>
public static class TbcWeaponMeshExtractor
{
    /// <summary>Sanity cap — no stock weapon needs more; a runaway table won't inflate the M2.</summary>
    private const int MaxPasses = 6;

    private sealed record SourcePass(int SrcSubmesh, ushort Flags, ushort Blend, int Layer, string TexKey);

    public static TbcExtractResult? Extract(M2Model m2, ForgeDiagnostics diag)
    {
        var plan = PlanPasses(m2, diag);
        if (plan is null) return ExtractSinglePass(m2, diag);
        var (passes, texSlots) = plan.Value;

        // ── Compact geometry: one contiguous vertex+index block per distinct source submesh ──
        var slotBySrcSubmesh = new Dictionary<int, int>();
        var ranges = new List<WeaponSubmeshRange>();
        var pos = new List<Vector3>();
        var nrm = new List<Vector3>();
        var uv = new List<Vector2>();
        var indices = new List<uint>();
        int uvClamped = 0, dropped = 0;

        foreach (var sp in passes)
        {
            if (slotBySrcSubmesh.ContainsKey(sp.SrcSubmesh)) continue;
            var sub = m2.Submeshes[sp.SrcSubmesh];

            int vertexStart = pos.Count, indexStart = indices.Count;
            var remap = new Dictionary<int, uint>();
            int start = sub.IndexStart, count = sub.IndexCount;
            for (int k = 0; k + 2 < count && start + k + 2 < m2.Indices.Count; k += 3)
            {
                int a = m2.Indices[start + k], b = m2.Indices[start + k + 1], c = m2.Indices[start + k + 2];
                if (a >= m2.Vertices.Count || b >= m2.Vertices.Count || c >= m2.Vertices.Count) { dropped++; continue; }
                if (a == b || b == c || a == c) { dropped++; continue; }
                var va = m2.Vertices[a]; var vb = m2.Vertices[b]; var vc = m2.Vertices[c];
                var pa = new Vector3(va.PosX, va.PosY, va.PosZ);
                var e0 = new Vector3(vb.PosX, vb.PosY, vb.PosZ) - pa;
                var e1 = new Vector3(vc.PosX, vc.PosY, vc.PosZ) - pa;
                if (Vector3.Cross(e0, e1).LengthSquared() < 1e-14f) { dropped++; continue; }

                uint Map(int src)
                {
                    if (remap.TryGetValue(src, out var mapped)) return mapped;
                    var v = m2.Vertices[src];
                    pos.Add(new Vector3(v.PosX, v.PosY, v.PosZ));
                    var normal = new Vector3(v.NormX, v.NormY, v.NormZ);
                    nrm.Add(normal.LengthSquared() > 1e-10f ? Vector3.Normalize(normal) : Vector3.UnitY);
                    float uC = Math.Clamp(v.TexU, 0f, 1f), vC = Math.Clamp(v.TexV, 0f, 1f);
                    if (uC != v.TexU || vC != v.TexV) uvClamped++;
                    uv.Add(new Vector2(uC, vC));
                    uint id = (uint)(pos.Count - 1);
                    remap[src] = id;
                    return id;
                }
                indices.Add(Map(a)); indices.Add(Map(b)); indices.Add(Map(c));
            }

            if (indices.Count == indexStart) continue; // submesh contributed nothing usable
            slotBySrcSubmesh[sp.SrcSubmesh] = ranges.Count;
            ranges.Add(new WeaponSubmeshRange
            {
                IndexStart = indexStart,
                IndexCount = indices.Count - indexStart,
                VertexStart = vertexStart,
                VertexCount = pos.Count - vertexStart,
            });
        }

        if (indices.Count == 0) return ExtractSinglePass(m2, diag);
        if (dropped > 0) diag.Info("tbc.degenerate.dropped", $"{dropped} degenerate/invalid triangle(s) dropped.");
        if (uvClamped > 0) diag.Info("tbc.uv.clamped", $"{uvClamped} UV(s) outside [0,1] clamped to the vanilla policy.");

        var weaponPasses = new List<WeaponPass>();
        bool baseAlpha = false, baseTwoSided = false;
        foreach (var sp in passes)
        {
            if (!slotBySrcSubmesh.TryGetValue(sp.SrcSubmesh, out int slot)) continue;
            weaponPasses.Add(new WeaponPass
            {
                SubmeshSlot = slot,
                RenderFlags = sp.Flags,
                BlendMode = sp.Blend,
                Layer = sp.Layer,
                TextureSlot = texSlots.IndexOf(sp.TexKey),
            });
            if (sp.Blend is 1 or 2) { baseAlpha = true; }
            if ((sp.Flags & 0x04) != 0) baseTwoSided = true;
        }
        if (weaponPasses.Count == 0) return ExtractSinglePass(m2, diag);

        int glowCount = weaponPasses.Count(p => p.BlendMode >= 3);
        if (glowCount > 0)
            diag.Info("tbc.passes.glow", $"{glowCount} additive glow pass(es) carried over ({texSlots.Count - 1} effect texture(s)).");

        return new TbcExtractResult
        {
            Mesh = new RigidWeaponMesh
            {
                Positions = pos.ToArray(),
                Normals = nrm.ToArray(),
                Uv0 = uv.ToArray(),
                Indices = indices.ToArray(),
                VertexIds = null,
                // Material summarizes the BASE look (texture encode + preview defaults).
                Material = new WeaponMaterial
                {
                    BlendMode = baseAlpha ? WeaponBlendMode.AlphaKey : WeaponBlendMode.Opaque,
                    TwoSided = baseTwoSided,
                },
                SubmeshRanges = ranges,
                Passes = weaponPasses,
                Normalization = new MeshNormalizationRecord
                {
                    Scale = 1f,
                    Method = "tbc-import passthrough — WoW-authored geometry, palm at origin preserved",
                },
            },
            SourceTextures = texSlots.Select(k => k == DbcTextureKey ? null : k).ToList(),
        };
    }

    private const string DbcTextureKey = "\0DBC";

    /// <summary>Decide which batches survive and how texture slots are assigned.
    /// Null → no usable tables; caller falls back to single-pass.</summary>
    private static (List<SourcePass> Passes, List<string> TexSlots)? PlanPasses(M2Model m2, ForgeDiagnostics diag)
    {
        if (m2.Batches.Count == 0 || m2.Submeshes.Count == 0 || m2.RenderFlags.Count == 0)
        {
            diag.Info("tbc.batches.none", "No batch/render-flag tables — importing the whole triangle list as opaque.");
            return null;
        }

        string TexKeyOf(M2Batch batch)
        {
            if (batch.TextureIndex < m2.TextureLookup.Count)
            {
                int ti = m2.TextureLookup[batch.TextureIndex];
                if (ti >= 0 && ti < m2.Textures.Count)
                {
                    var t = m2.Textures[ti];
                    if (t.Type == 0 && t.Filename.Length > 0) return t.Filename;
                    return DbcTextureKey; // any DBC-replaceable type — supplied by the display row
                }
            }
            return DbcTextureKey;
        }

        var basePasses = new Dictionary<int, SourcePass>();   // srcSubmesh → base
        var glowPasses = new List<SourcePass>();
        int droppedReflect = 0, droppedOverlay = 0, droppedInvisible = 0;

        foreach (var batch in m2.Batches)
        {
            var flag = batch.MaterialIndex < m2.RenderFlags.Count ? m2.RenderFlags[batch.MaterialIndex] : null;
            ushort blend = flag?.BlendingMode ?? 0;
            ushort bits = flag?.Flags ?? 0;
            if (batch.SubmeshIndex >= m2.Submeshes.Count) continue;
            if (m2.GetStaticAlphaForBatch(batch) < 0.5f) { droppedInvisible++; continue; }

            if (blend >= 5) { droppedReflect++; continue; }   // modulate/reflect env layers
            if (blend >= 3)
            {
                glowPasses.Add(new SourcePass(batch.SubmeshIndex, bits, blend, batch.MaterialLayer, TexKeyOf(batch)));
                continue;
            }

            var candidate = new SourcePass(batch.SubmeshIndex, bits, blend, batch.MaterialLayer, TexKeyOf(batch));
            if (basePasses.TryGetValue(batch.SubmeshIndex, out var existing))
            {
                if (candidate.Layer < existing.Layer) { basePasses[batch.SubmeshIndex] = candidate; }
                droppedOverlay++;
            }
            else basePasses[batch.SubmeshIndex] = candidate;
        }

        if (basePasses.Count == 0 && glowPasses.Count == 0)
        {
            diag.Warn("tbc.batches.empty", "Batch filtering left no geometry — importing the whole triangle list as opaque.");
            return null;
        }

        // Base passes first (submesh order), then glow passes (layer order), capped.
        var passes = basePasses.Values.OrderBy(p => p.SrcSubmesh)
            .Concat(glowPasses.OrderBy(p => p.Layer).ThenBy(p => p.SrcSubmesh))
            .Take(MaxPasses)
            .ToList();

        // Texture slots: the dominant base pass's image is slot 0 (the DBC-driven skin);
        // every other distinct image becomes an effect slot.
        string baseKey = basePasses.Count > 0
            ? basePasses.Values.OrderByDescending(p =>
                  p.SrcSubmesh < m2.Submeshes.Count ? m2.Submeshes[p.SrcSubmesh].IndexCount : 0)
              .First().TexKey
            : passes[0].TexKey;
        var texSlots = new List<string> { baseKey };
        foreach (var p in passes)
            if (!texSlots.Contains(p.TexKey))
                texSlots.Add(p.TexKey);

        if (baseKey != DbcTextureKey)
            diag.Info("tbc.texture.hardcoded", $"Base pass samples the hardcoded texture '{baseKey}' — using it as the skin.");
        if (droppedReflect > 0)
            diag.Info("tbc.batches.reflect", $"{droppedReflect} modulate/reflect pass(es) dropped (need env-mapping).");
        if (droppedOverlay > 0)
            diag.Info("tbc.batches.overlay", $"{droppedOverlay} overlay layer(s) dropped — one base pass per submesh.");
        if (droppedInvisible > 0)
            diag.Info("tbc.batches.invisible", $"{droppedInvisible} idle-invisible batch(es) dropped.");

        return (passes, texSlots);
    }

    /// <summary>Fallback: whole triangle list, one opaque pass — for models without usable tables.</summary>
    private static TbcExtractResult? ExtractSinglePass(M2Model m2, ForgeDiagnostics diag)
    {
        int n = m2.Vertices.Count;
        var pos = new Vector3[n];
        var nrm = new Vector3[n];
        var uv = new Vector2[n];
        int uvClamped = 0;
        for (int i = 0; i < n; i++)
        {
            var v = m2.Vertices[i];
            pos[i] = new Vector3(v.PosX, v.PosY, v.PosZ);
            var normal = new Vector3(v.NormX, v.NormY, v.NormZ);
            nrm[i] = normal.LengthSquared() > 1e-10f ? Vector3.Normalize(normal) : Vector3.UnitY;
            float uC = Math.Clamp(v.TexU, 0f, 1f), vC = Math.Clamp(v.TexV, 0f, 1f);
            if (uC != v.TexU || vC != v.TexV) uvClamped++;
            uv[i] = new Vector2(uC, vC);
        }
        if (uvClamped > 0)
            diag.Info("tbc.uv.clamped", $"{uvClamped} UV(s) outside [0,1] clamped to the vanilla policy.");

        var kept = new List<uint>(m2.Indices.Count);
        for (int t = 0; t + 2 < m2.Indices.Count; t += 3)
        {
            uint a = m2.Indices[t], b = m2.Indices[t + 1], c = m2.Indices[t + 2];
            if (a >= n || b >= n || c >= n) continue;
            if (a == b || b == c || a == c) continue;
            var e0 = pos[b] - pos[a];
            var e1 = pos[c] - pos[a];
            if (Vector3.Cross(e0, e1).LengthSquared() < 1e-14f) continue;
            kept.Add(a); kept.Add(b); kept.Add(c);
        }
        if (kept.Count == 0) return null;

        return new TbcExtractResult
        {
            Mesh = new RigidWeaponMesh
            {
                Positions = pos,
                Normals = nrm,
                Uv0 = uv,
                Indices = kept.ToArray(),
                VertexIds = null,
                Material = new WeaponMaterial(),
                Normalization = new MeshNormalizationRecord
                {
                    Scale = 1f,
                    Method = "tbc-import passthrough — WoW-authored geometry, palm at origin preserved",
                },
            },
            SourceTextures = new List<string?> { null },
        };
    }
}

/// <summary>Result of a TBC weapon extraction: the (possibly multi-pass) mesh plus the source
/// texture per slot — slot 0 is the base skin, further slots are effect/glow textures. A null
/// path means "the display row's Type-2 texture"; otherwise it is the hardcoded TBC MPQ path.</summary>
public sealed record TbcExtractResult
{
    public required RigidWeaponMesh Mesh { get; init; }
    public required List<string?> SourceTextures { get; init; }
}