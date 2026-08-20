using System.Numerics;

namespace MangosSuperUI.Services.WeaponForge;

/// <summary>
/// Converts the visible render graph from a TBC weapon M2 into the Forge's rigid mesh IR.
/// TBC and vanilla v256 share the fixed-function weapon batch format, so batches are retained in
/// source order instead of being reduced to a guessed "base + glow" pair. Geometry is compacted
/// once per referenced source submesh; every source batch can then draw that range with its own
/// blend mode, flags, shader, texture units, UV source, and static transparency.
/// </summary>
public static class TbcWeaponMeshExtractor
{
    private sealed record SourceTexture(string? Path, uint Flags, uint Type);
    private sealed record SourceBinding(SourceTexture Texture, ushort Coordinate, float StaticAlpha, ushort Transform);
    private sealed record SourcePass(
        int SourceOrder,
        int SrcSubmesh,
        byte BatchFlags,
        sbyte PriorityPlane,
        ushort ShaderId,
        short ColorIndex,
        ushort RenderFlags,
        ushort BlendMode,
        int Layer,
        IReadOnlyList<SourceBinding> Bindings);
    private sealed record PassPlan(
        List<SourcePass> Passes,
        List<SourceTexture> Textures,
        bool Fatal = false);

    public static TbcExtractResult? Extract(M2Model m2, ForgeDiagnostics diag)
    {
        var plan = PlanPasses(m2, diag);
        if (plan is null) return ExtractSinglePass(m2, diag);
        if (plan.Fatal) return null;
        var passes = plan.Passes;
        var textureSlots = plan.Textures;

        // One contiguous block per source submesh. Shared source vertices are deliberately copied
        // into each block because vanilla skin-section vertex/index spans are UInt16 ranges.
        var slotBySrcSubmesh = new Dictionary<int, int>();
        var ranges = new List<WeaponSubmeshRange>();
        var pos = new List<Vector3>();
        var nrm = new List<Vector3>();
        var uv0 = new List<Vector2>();
        var uv1 = new List<Vector2>();
        var indices = new List<uint>();
        int dropped = 0, repairedUv = 0;

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
                if (a >= m2.Vertices.Count || b >= m2.Vertices.Count || c >= m2.Vertices.Count)
                { dropped++; continue; }
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

                    static float FiniteOrZero(float value) => float.IsFinite(value) ? value : 0f;
                    var t0 = new Vector2(FiniteOrZero(v.TexU), FiniteOrZero(v.TexV));
                    var t1 = new Vector2(FiniteOrZero(v.TexU2), FiniteOrZero(v.TexV2));
                    if (!float.IsFinite(v.TexU) || !float.IsFinite(v.TexV) ||
                        !float.IsFinite(v.TexU2) || !float.IsFinite(v.TexV2)) repairedUv++;
                    // Do not clamp: authored values outside [0,1] are how wrapped and reflected
                    // weapon materials address their texture.
                    uv0.Add(t0); uv1.Add(t1);

                    uint id = (uint)(pos.Count - 1);
                    remap[src] = id;
                    return id;
                }

                indices.Add(Map(a)); indices.Add(Map(b)); indices.Add(Map(c));
            }

            if (indices.Count == indexStart) continue;
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
        if (repairedUv > 0) diag.Warn("tbc.uv.nonfinite", $"{repairedUv} vertex UV pair(s) contained non-finite values and were replaced with zero.");

        var textureIndex = textureSlots.Select((t, i) => (t, i)).ToDictionary(x => x.t, x => x.i);
        var weaponPasses = new List<WeaponPass>(passes.Count);
        bool baseAlpha = false, anyTwoSided = false;
        foreach (var sp in passes)
        {
            if (!slotBySrcSubmesh.TryGetValue(sp.SrcSubmesh, out int slot)) continue;
            var bindings = sp.Bindings.Select(b => new WeaponTextureBinding
            {
                TextureSlot = textureIndex[b.Texture],
                TextureCoordinate = b.Coordinate,
                StaticAlpha = Math.Clamp(b.StaticAlpha, 0f, 1f),
                TextureTransform = b.Transform,
            }).ToArray();
            int primaryTexture = bindings.Length > 0 ? bindings[0].TextureSlot : 0;
            weaponPasses.Add(new WeaponPass
            {
                SubmeshSlot = slot,
                RenderFlags = sp.RenderFlags,
                BlendMode = sp.BlendMode,
                Layer = sp.Layer,
                TextureSlot = primaryTexture,
                SourceOrder = sp.SourceOrder,
                BatchFlags = sp.BatchFlags,
                PriorityPlane = sp.PriorityPlane,
                ShaderId = sp.ShaderId,
                ColorIndex = sp.ColorIndex,
                TextureBindings = bindings,
            });

            if (bindings.Any(b => b.TextureSlot == 0) && sp.BlendMode is 1 or 2 or 4) baseAlpha = true;
            if ((sp.RenderFlags & 0x04) != 0) anyTwoSided = true;
        }
        if (weaponPasses.Count == 0) return ExtractSinglePass(m2, diag);

        int layered = weaponPasses.Count - weaponPasses.Select(p => p.SubmeshSlot).Distinct().Count();
        int multiTexture = weaponPasses.Count(p => p.TextureBindings is { Count: > 1 });
        int glow = weaponPasses.Count(p => p.BlendMode is 3 or 4);
        diag.Info("tbc.passes.preserved",
            $"Preserved {weaponPasses.Count} source batch(es) over {ranges.Count} submesh(es) in source draw order " +
            $"({layered} overlay, {glow} additive, {multiTexture} multi-texture).");

        return new TbcExtractResult
        {
            Mesh = new RigidWeaponMesh
            {
                Positions = pos.ToArray(),
                Normals = nrm.ToArray(),
                Uv0 = uv0.ToArray(),
                Uv1 = uv1.ToArray(),
                Indices = indices.ToArray(),
                VertexIds = null,
                Material = new WeaponMaterial
                {
                    BlendMode = baseAlpha ? WeaponBlendMode.AlphaKey : WeaponBlendMode.Opaque,
                    TwoSided = anyTwoSided,
                },
                SubmeshRanges = ranges,
                Passes = weaponPasses,
                TextureSlots = textureSlots.Select(t => new WeaponTextureSlot { Flags = t.Flags }).ToArray(),
                Normalization = new MeshNormalizationRecord
                {
                    Scale = 1f,
                    Method = "tbc-import passthrough — source geometry and render-batch order preserved",
                },
            },
            SourceTextures = textureSlots.Select(t => new TbcSourceTexture
            {
                SourcePath = t.Path,
                Flags = t.Flags,
                SourceType = t.Type,
            }).ToList(),
        };
    }

    /// <summary>Build a lossless static batch plan. No visibility threshold, blend filter, pass cap,
    /// or base/overlay reordering is applied.</summary>
    private static PassPlan? PlanPasses(M2Model m2, ForgeDiagnostics diag)
    {
        if (m2.Batches.Count == 0 || m2.Submeshes.Count == 0)
        {
            diag.Info("tbc.batches.none", "No batch/submesh tables — importing the whole triangle list as opaque.");
            return null;
        }

        PassPlan Fatal(string code, string message)
        {
            diag.Error(code, message);
            return new PassPlan([], [], Fatal: true);
        }

        bool TryResolveTexture(M2Batch batch, int unit, out SourceTexture? texture, out string? error)
        {
            texture = null;
            error = null;

            int combo = batch.TextureIndex + unit;
            if ((uint)combo >= (uint)m2.TextureLookup.Count)
            {
                error = $"texture-combo entry {combo} is outside the texture lookup (count {m2.TextureLookup.Count})";
                return false;
            }

            int textureIndex = m2.TextureLookup[combo];
            if ((uint)textureIndex >= (uint)m2.Textures.Count)
            {
                error = $"texture-combo entry {combo} resolves texture {textureIndex}, outside the texture table (count {m2.Textures.Count})";
                return false;
            }

            var source = m2.Textures[textureIndex];
            switch (source.Type)
            {
                case 0 when !string.IsNullOrWhiteSpace(source.Filename):
                    texture = new SourceTexture(source.Filename, source.Flags, source.Type);
                    return true;
                case 0:
                    error = $"texture {textureIndex} is Type-0 but has no hardcoded filename";
                    return false;
                case 2:
                    // Type-2 is the one replaceable texture supplied by ItemDisplayInfo for an
                    // object/weapon. It is the only texture kind for which a null source path is
                    // meaningful to the controller.
                    texture = new SourceTexture(null, source.Flags, source.Type);
                    return true;
                default:
                    error = $"texture {textureIndex} uses unsupported replaceable type {source.Type}";
                    return false;
            }
        }

        static bool HasDeclaredSpan(IReadOnlyList<ushort> lookup, ushort start, int count)
        {
            // A direct 0xFFFF start is the old-format sentinel for an absent optional combo. A
            // sentinel inside a real span is data too (notably "no transform" and "no weight")
            // and must pass through unchanged.
            return start == ushort.MaxValue || (long)start + count <= lookup.Count;
        }

        static ushort ResolveOptionalLookup(
            IReadOnlyList<ushort> lookup,
            ushort start,
            int unit)
        {
            return start == ushort.MaxValue ? ushort.MaxValue : lookup[start + unit];
        }

        var passes = new List<SourcePass>(m2.Batches.Count);
        for (int sourceOrder = 0; sourceOrder < m2.Batches.Count; sourceOrder++)
        {
            var batch = m2.Batches[sourceOrder];
            if (batch.SubmeshIndex >= m2.Submeshes.Count)
                return Fatal("tbc.batch.submesh",
                    $"Batch {sourceOrder} references submesh {batch.SubmeshIndex}, outside the submesh table (count {m2.Submeshes.Count}).");

            if (batch.MaterialIndex >= m2.RenderFlags.Count)
                return Fatal("tbc.batch.material",
                    $"Batch {sourceOrder} references render flag {batch.MaterialIndex}, outside the render-flag table (count {m2.RenderFlags.Count}).");
            M2RenderFlag rf = m2.RenderFlags[batch.MaterialIndex];

            int unitCount = batch.TextureCount;
            if (unitCount == 0)
                return Fatal("tbc.texture.units.zero",
                    $"Batch {sourceOrder} declares zero texture units; the fidelity writer cannot represent an untextured batch without fabricating a binding.");

            if ((long)batch.TextureIndex + unitCount > m2.TextureLookup.Count)
                return Fatal("tbc.texture.span",
                    $"Batch {sourceOrder} declares {unitCount} texture unit(s) at texture-combo {batch.TextureIndex}, outside the texture lookup (count {m2.TextureLookup.Count}).");
            if (!HasDeclaredSpan(m2.TextureCoordinateLookup, batch.TextureCoordinateIndex, unitCount))
                return Fatal("tbc.texture.coordinate-span",
                    $"Batch {sourceOrder} declares {unitCount} texture unit(s) at coordinate-combo {batch.TextureCoordinateIndex}, outside the coordinate lookup (count {m2.TextureCoordinateLookup.Count}).");
            if (!HasDeclaredSpan(m2.TransparencyLookup, batch.TextureWeightIndex, unitCount))
                return Fatal("tbc.texture.weight-span",
                    $"Batch {sourceOrder} declares {unitCount} texture unit(s) at weight-combo {batch.TextureWeightIndex}, outside the transparency lookup (count {m2.TransparencyLookup.Count}).");
            if (!HasDeclaredSpan(m2.TextureTransformLookup, batch.TextureTransformIndex, unitCount))
                return Fatal("tbc.texture.transform-span",
                    $"Batch {sourceOrder} declares {unitCount} texture unit(s) at transform-combo {batch.TextureTransformIndex}, outside the transform lookup (count {m2.TextureTransformLookup.Count}).");

            var bindings = new List<SourceBinding>(unitCount);
            for (int unit = 0; unit < unitCount; unit++)
            {
                if (!TryResolveTexture(batch, unit, out var texture, out string? textureError))
                    return Fatal("tbc.texture.resolve",
                        $"Batch {sourceOrder}, texture unit {unit}: {textureError}.");

                ushort coordinate = ResolveOptionalLookup(
                    m2.TextureCoordinateLookup, batch.TextureCoordinateIndex, unit);
                ushort weight = ResolveOptionalLookup(
                    m2.TransparencyLookup, batch.TextureWeightIndex, unit);
                ushort transform = ResolveOptionalLookup(
                    m2.TextureTransformLookup, batch.TextureTransformIndex, unit);

                float staticAlpha = 1f;
                if (weight != ushort.MaxValue)
                {
                    if (weight >= m2.TransparencyStaticAlphas.Count)
                        return Fatal("tbc.texture.weight",
                            $"Batch {sourceOrder}, texture unit {unit}: transparency combo resolves track {weight}, outside the transparency table (count {m2.TransparencyStaticAlphas.Count}).");
                    staticAlpha = m2.TransparencyStaticAlphas[weight];
                    if (!float.IsFinite(staticAlpha))
                        return Fatal("tbc.texture.alpha",
                            $"Batch {sourceOrder}, texture unit {unit}: transparency track {weight} has a non-finite static alpha.");
                }

                bindings.Add(new SourceBinding(
                    texture!,
                    coordinate,
                    staticAlpha,
                    transform));
            }

            passes.Add(new SourcePass(
                sourceOrder,
                batch.SubmeshIndex,
                batch.Flags,
                batch.PriorityPlane,
                batch.ShaderId,
                batch.ColorIndex,
                rf.Flags,
                rf.BlendingMode,
                batch.MaterialLayer,
                bindings));
        }

        // Slot zero is the image used by the largest ordinary surface, because ItemDisplayInfo can
        // provide exactly one replaceable weapon texture. Remaining distinct source images/flag
        // combinations become hardcoded Type-0 effect slots in first-use order.
        var dominant = passes
            .Where(p => p.BlendMode <= 2 && p.Bindings.Count > 0)
            .OrderByDescending(p => m2.Submeshes[p.SrcSubmesh].IndexCount)
            .ThenBy(p => p.SourceOrder)
            .Select(p => p.Bindings[0].Texture)
            .FirstOrDefault()
            ?? passes.SelectMany(p => p.Bindings).Select(b => b.Texture).First();

        var textures = new List<SourceTexture> { dominant };
        foreach (var binding in passes.SelectMany(p => p.Bindings))
            if (!textures.Contains(binding.Texture)) textures.Add(binding.Texture);

        if (dominant.Path is not null)
            diag.Info("tbc.texture.hardcoded", $"Dominant surface samples hardcoded texture '{dominant.Path}'; its pixels become the display texture.");
        return new PassPlan(passes, textures);
    }

    /// <summary>Fallback for models with no batch table: whole triangle list, one opaque pass.</summary>
    private static TbcExtractResult? ExtractSinglePass(M2Model m2, ForgeDiagnostics diag)
    {
        int n = m2.Vertices.Count;
        var pos = new Vector3[n];
        var nrm = new Vector3[n];
        var uv0 = new Vector2[n];
        var uv1 = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            var v = m2.Vertices[i];
            pos[i] = new Vector3(v.PosX, v.PosY, v.PosZ);
            var normal = new Vector3(v.NormX, v.NormY, v.NormZ);
            nrm[i] = normal.LengthSquared() > 1e-10f ? Vector3.Normalize(normal) : Vector3.UnitY;
            uv0[i] = new Vector2(float.IsFinite(v.TexU) ? v.TexU : 0, float.IsFinite(v.TexV) ? v.TexV : 0);
            uv1[i] = new Vector2(float.IsFinite(v.TexU2) ? v.TexU2 : 0, float.IsFinite(v.TexV2) ? v.TexV2 : 0);
        }

        var kept = new List<uint>(m2.Indices.Count);
        for (int t = 0; t + 2 < m2.Indices.Count; t += 3)
        {
            uint a = m2.Indices[t], b = m2.Indices[t + 1], c = m2.Indices[t + 2];
            if (a >= n || b >= n || c >= n || a == b || b == c || a == c) continue;
            if (Vector3.Cross(pos[b] - pos[a], pos[c] - pos[a]).LengthSquared() < 1e-14f) continue;
            kept.Add(a); kept.Add(b); kept.Add(c);
        }
        if (kept.Count == 0) return null;

        return new TbcExtractResult
        {
            Mesh = new RigidWeaponMesh
            {
                Positions = pos,
                Normals = nrm,
                Uv0 = uv0,
                Uv1 = uv1,
                Indices = kept.ToArray(),
                VertexIds = null,
                Material = new WeaponMaterial { TwoSided = true },
                TextureSlots = [new WeaponTextureSlot { Flags = 0 }],
                Normalization = new MeshNormalizationRecord
                {
                    Scale = 1f,
                    Method = "tbc-import fallback — no usable batch table",
                },
            },
            SourceTextures = [new TbcSourceTexture { SourcePath = null, Flags = 0, SourceType = 2 }],
        };
    }
}

/// <summary>One source image used by an extracted TBC render graph. A null path means the item
/// display's replaceable weapon texture; otherwise it is a hardcoded TBC MPQ member.</summary>
public sealed record TbcSourceTexture
{
    public required string? SourcePath { get; init; }
    public required uint Flags { get; init; }
    public required uint SourceType { get; init; }
}

public sealed record TbcExtractResult
{
    public required RigidWeaponMesh Mesh { get; init; }
    public required List<TbcSourceTexture> SourceTextures { get; init; }
}
