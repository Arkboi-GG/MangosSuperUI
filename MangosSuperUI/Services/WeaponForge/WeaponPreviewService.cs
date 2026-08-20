using System.Numerics;
using System.Security.Cryptography;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;

namespace MangosSuperUI.Services.WeaponForge;

/// <summary>
/// The content-hash direct preview (WEAPON_GEN.md §4.1, §7.4). Given raw generated (or
/// MPQ-extracted) M2 + BLP bytes, it renders a GLB by parsing the M2 and binding the BLP to every
/// texture slot the geometry samples — WITHOUT any display-id/retexture resolution. This is the
/// piece the existing display-id-driven <c>EnsureGlb</c> cannot do: it resolves a custom display
/// back to a vanilla M2 and ignores custom-M2 bytes, so it can never preview a truly custom model.
///
/// Output GLBs are cached by the SHA-256 of (m2 ++ blp) so an identical byte pair is rendered once.
/// This is an output cache under wwwroot; it never reads pre-extracted game assets.
/// </summary>
public sealed class WeaponPreviewService
{
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<WeaponPreviewService> _logger;

    public WeaponPreviewService(IWebHostEnvironment env, ILogger<WeaponPreviewService> logger)
    {
        _env = env;
        _logger = logger;
    }

    public WeaponPreviewResult RenderFromBytes(byte[] m2Bytes, byte[]? blpBytes,
        IReadOnlyDictionary<string, byte[]>? effectBlpsByPath = null)
    {
        if (m2Bytes is null || m2Bytes.Length == 0)
            return WeaponPreviewResult.Fail("No M2 bytes supplied.");

        string key = ContentKey(m2Bytes, blpBytes, effectBlpsByPath);
        var cacheDir = Path.Combine(_env.WebRootPath, "weapon_forge_cache");
        Directory.CreateDirectory(cacheDir);
        var fileName = $"wf_{key[..24]}.glb";
        var fullPath = Path.Combine(cacheDir, fileName);
        var webPath = $"/weapon_forge_cache/{fileName}";

        var m2 = M2Reader.Parse(m2Bytes);
        if (m2 is null)
            return WeaponPreviewResult.Fail("M2Reader rejected the bytes (bad magic, version >= 264, or malformed).");

        int triangleCount = m2.Indices.Count / 3;

        if (File.Exists(fullPath))
            return new WeaponPreviewResult(true, webPath, m2.Vertices.Count, triangleCount, key, Cached: true, Error: null);

        // Bind textures by slot: Type-2 (DBC-driven) slots get the base BLP; Type-0 hardcoded
        // slots get their bytes when the caller supplied them by path (multi-pass glow).
        var textures = new Dictionary<int, byte[]>();
        for (int ti = 0; ti < m2.Textures.Count; ti++)
        {
            var t = m2.Textures[ti];
            if (t.Type != 0 && blpBytes is { Length: > 0 })
                textures[ti] = blpBytes;
            else if (t.Type == 0 && t.Filename.Length > 0 && effectBlpsByPath is not null &&
                     effectBlpsByPath.TryGetValue(t.Filename, out var bytes))
                textures[ti] = bytes;
        }
        if (textures.Count == 0 && blpBytes is { Length: > 0 })
        {
            foreach (var idx in GlbWriter.SampledTextureIndices(m2)) textures[idx] = blpBytes;
            if (textures.Count == 0) textures[0] = blpBytes; // fall back to slot 0 if nothing resolved
        }

        bool ok = GlbWriter.SaveGlb(m2, textures, fullPath, doubleSided: false);
        if (!ok)
            return WeaponPreviewResult.Fail("GlbWriter failed to emit a GLB for the parsed M2.");

        _logger.LogInformation("WeaponForge: previewed M2 ({Verts} verts, {Tris} tris) → {Web}",
            m2.Vertices.Count, triangleCount, webPath);
        return new WeaponPreviewResult(true, webPath, m2.Vertices.Count, triangleCount, key, Cached: false, Error: null);
    }

    /// <summary>Render a RigidWeaponMesh directly to a preview GLB (no M2 involved). This lets an
    /// imported/generated mesh be visualized in the weapon authoring space (Y-up glTF) before the
    /// M2 writer for its topology exists — the key to showing a sketch become 3D. Multi-pass meshes
    /// render one primitive per texture binding with per-slot textures (additive glow approximated with alpha
    /// blending — glTF has no additive mode). The material name carries the original WoW blend
    /// mode and static alpha so the three.js viewer can restore them after loading. Cached by
    /// content.</summary>
    public WeaponPreviewResult RenderMesh(RigidWeaponMesh mesh, byte[]? texturePng,
        IReadOnlyList<byte[]?>? effectPngs = null, bool forceDoubleSided = false)
    {
        if (mesh.VertexCount == 0 || mesh.Indices.Length < 3)
            return WeaponPreviewResult.Fail("Mesh is empty.");

        string key = MeshContentKey(mesh, texturePng, effectPngs, forceDoubleSided);
        var cacheDir = Path.Combine(_env.WebRootPath, "weapon_forge_cache");
        Directory.CreateDirectory(cacheDir);
        var fileName = $"mesh_{key[..24]}.glb";
        var fullPath = Path.Combine(cacheDir, fileName);
        var webPath = $"/weapon_forge_cache/{fileName}";
        int tris = mesh.Indices.Length / 3;

        if (File.Exists(fullPath))
            return new WeaponPreviewResult(true, webPath, mesh.VertexCount, tris, key, Cached: true, Error: null);

        try
        {
            MaterialBuilder BuildMaterial(string name, byte[]? png, ushort blend, float staticAlpha,
                bool twoSided)
            {
                if (!float.IsFinite(staticAlpha)) staticAlpha = 1f;
                staticAlpha = Math.Clamp(staticAlpha, 0f, 1f);
                int alphaPercent = Math.Clamp((int)MathF.Round(staticAlpha * 100f), 0, 100);

                // Keep this suffix at the very end of the name. blend-suffix.js uses it to
                // reconstruct WoW's blend modes (including additive/modulate, which glTF cannot
                // represent natively) and the source transparency-track alpha.
                name = $"{name}_blend{blend}_a{alphaPercent}";
                var m = new MaterialBuilder(name).WithUnlitShader();
                if (png is { Length: > 0 })
                    m.WithBaseColor(new SharpGLTF.Memory.MemoryImage(png))
                     .WithChannelParam(KnownChannel.BaseColor, KnownProperty.RGBA,
                         new Vector4(1f, 1f, 1f, staticAlpha));
                else
                    m.WithChannelParam(KnownChannel.BaseColor, KnownProperty.RGBA,
                        new Vector4(0.75f, 0.75f, 0.78f, staticAlpha));
                if (blend == 1) m.WithAlpha(AlphaMode.MASK, 0.5f);
                else if (blend >= 2 || staticAlpha < 0.999f)
                    m.WithAlpha(AlphaMode.BLEND); // additive/modulate are restored by the viewer
                if (twoSided) m.WithDoubleSide(true);
                return m;
            }

            var mb = new MeshBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty>("weapon");

            void AddRange(SharpGLTF.Geometry.PrimitiveBuilder<MaterialBuilder, VertexPositionNormal, VertexTexture1, VertexEmpty> prim,
                int indexStart, int indexCount, bool useUv1)
            {
                int end = Math.Min(indexStart + indexCount, mesh.Indices.Length);
                for (int i = indexStart; i + 2 < end; i += 3)
                {
                    uint a = mesh.Indices[i], b = mesh.Indices[i + 1], c = mesh.Indices[i + 2];
                    if (a >= mesh.VertexCount || b >= mesh.VertexCount || c >= mesh.VertexCount) continue;
                    prim.AddTriangle(MeshVertex(mesh, a, useUv1), MeshVertex(mesh, b, useUv1),
                        MeshVertex(mesh, c, useUv1));
                }
            }

            if (mesh.Passes is { Count: > 0 } && mesh.SubmeshRanges is { Count: > 0 })
            {
                int passIndex = 0;
                foreach (var pass in mesh.Passes)
                {
                    int sourcePassIndex = passIndex++;
                    if (pass.SubmeshSlot < 0 || pass.SubmeshSlot >= mesh.SubmeshRanges.Count) continue;
                    var range = mesh.SubmeshRanges[pass.SubmeshSlot];
                    bool twoSided = forceDoubleSided || (pass.RenderFlags & 0x04) != 0;

                    void EmitBinding(int bindingIndex, int textureSlot, int textureCoordinate,
                        float staticAlpha)
                    {
                        byte[]? png = TextureForSlot(textureSlot, texturePng, effectPngs);

                        // M2 texture-coordinate lookup uses the low value for UV0/UV1. The high
                        // bit requests an environment mapping mode that glTF cannot express; UV0
                        // is the least-surprising static fallback for that case.
                        bool environmentMapped = (textureCoordinate & 0x8000) != 0;
                        int coordinate = textureCoordinate & 0x7fff;
                        bool useUv1 = !environmentMapped && coordinate == 1 &&
                                      mesh.Uv1 is { Length: > 0 } &&
                                      mesh.Uv1.Length == mesh.VertexCount;

                        var material = BuildMaterial($"pass{sourcePassIndex}_tex{bindingIndex}", png,
                            pass.BlendMode, staticAlpha, twoSided);
                        AddRange(mb.UsePrimitive(material), range.IndexStart, range.IndexCount, useUv1);
                    }

                    if (pass.TextureBindings is { Count: > 0 })
                    {
                        int bindingIndex = 0;
                        foreach (var binding in pass.TextureBindings)
                        {
                            EmitBinding(bindingIndex++, Convert.ToInt32(binding.TextureSlot),
                                Convert.ToInt32(binding.TextureCoordinate),
                                Convert.ToSingle(binding.StaticAlpha));
                        }
                    }
                    else
                    {
                        EmitBinding(0, pass.TextureSlot, textureCoordinate: 0, staticAlpha: 1f);
                    }
                }
            }
            else
            {
                var material = BuildMaterial("weapon", texturePng,
                    mesh.Material.BlendMode == WeaponBlendMode.AlphaKey ? (ushort)1 : (ushort)0,
                    staticAlpha: 1f, twoSided: forceDoubleSided || mesh.Material.TwoSided);
                AddRange(mb.UsePrimitive(material), 0, mesh.Indices.Length, useUv1: false);
            }

            var scene = new SceneBuilder("weapon");
            scene.AddRigidMesh(mb, Matrix4x4.Identity);
            scene.ToGltf2().SaveGLB(fullPath);
        }
        catch (Exception ex)
        {
            return WeaponPreviewResult.Fail($"Mesh GLB build failed: {ex.Message}");
        }

        _logger.LogInformation("WeaponForge: previewed mesh ({V} verts, {T} tris) → {Web}", mesh.VertexCount, tris, webPath);
        return new WeaponPreviewResult(true, webPath, mesh.VertexCount, tris, key, Cached: false, Error: null);
    }

    private static VertexBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty> MeshVertex(
        RigidWeaponMesh m, uint i, bool useUv1)
    {
        Vector2 uv = useUv1 && m.Uv1 is { } uv1 && i < (uint)uv1.Length ? uv1[i] : m.Uv0[i];
        return new VertexBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty>(
            new VertexPositionNormal(m.Positions[i], m.Normals[i]), new VertexTexture1(uv));
    }

    private static byte[]? TextureForSlot(int textureSlot, byte[]? baseTexture,
        IReadOnlyList<byte[]?>? effectTextures)
    {
        if (textureSlot <= 0) return baseTexture;
        int effectIndex = textureSlot - 1;
        return effectTextures is not null && effectIndex < effectTextures.Count
            ? effectTextures[effectIndex]
            : null;
    }

    private static string MeshContentKey(RigidWeaponMesh m, byte[]? tex,
        IReadOnlyList<byte[]?>? effectPngs, bool forceDoubleSided)
    {
        // This deliberately hashes the full rendered state rather than a few low bytes of it.
        // Otherwise UV-only edits, high render-flag bits, pass-layer changes, or a null texture
        // could incorrectly reuse an older GLB from the output cache.
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("WeaponPreviewMesh/v2");
            writer.Write(forceDoubleSided);
            writer.Write((int)m.Material.BlendMode);
            writer.Write(m.Material.TwoSided);

            WriteVector3Array(writer, m.Positions);
            WriteVector3Array(writer, m.Normals);
            WriteVector2Array(writer, m.Uv0);
            if (m.Uv1 is null)
            {
                writer.Write(-1);
            }
            else
            {
                WriteVector2Array(writer, m.Uv1);
            }

            writer.Write(m.Indices.Length);
            foreach (uint index in m.Indices) writer.Write(index);

            if (m.SubmeshRanges is null)
            {
                writer.Write(-1);
            }
            else
            {
                writer.Write(m.SubmeshRanges.Count);
                foreach (var range in m.SubmeshRanges)
                {
                    writer.Write(range.IndexStart);
                    writer.Write(range.IndexCount);
                    writer.Write(range.VertexStart);
                    writer.Write(range.VertexCount);
                }
            }

            if (m.Passes is null)
            {
                writer.Write(-1);
            }
            else
            {
                writer.Write(m.Passes.Count);
                foreach (var pass in m.Passes)
                {
                    writer.Write(pass.SubmeshSlot);
                    writer.Write(pass.RenderFlags);
                    writer.Write(pass.BlendMode);
                    writer.Write(pass.Layer);
                    writer.Write(pass.TextureSlot);
                    writer.Write(pass.SourceOrder);
                    writer.Write(pass.BatchFlags);
                    writer.Write(pass.PriorityPlane);
                    writer.Write(pass.ShaderId);
                    writer.Write(pass.ColorIndex);

                    if (pass.TextureBindings is null)
                    {
                        writer.Write(-1);
                    }
                    else
                    {
                        writer.Write(pass.TextureBindings.Count);
                        foreach (var binding in pass.TextureBindings)
                        {
                            writer.Write(binding.TextureSlot);
                            writer.Write(binding.TextureCoordinate);
                            writer.Write(binding.StaticAlpha);
                            writer.Write(binding.TextureTransform);
                        }
                    }
                }
            }

            if (m.TextureSlots is null)
            {
                writer.Write(-1);
            }
            else
            {
                writer.Write(m.TextureSlots.Count);
                foreach (var textureSlot in m.TextureSlots) writer.Write(textureSlot.Flags);
            }

            WriteBytes(writer, tex);
            if (effectPngs is null)
            {
                writer.Write(-1);
            }
            else
            {
                writer.Write(effectPngs.Count);
                foreach (byte[]? effectPng in effectPngs) WriteBytes(writer, effectPng);
            }
        }

        stream.TryGetBuffer(out ArraySegment<byte> buffer);
        byte[] hash = SHA256.HashData(buffer.AsSpan(0, checked((int)stream.Length)));
        return Convert.ToHexString(hash).ToLowerInvariant();

        static void WriteVector3Array(BinaryWriter writer, IReadOnlyList<Vector3> values)
        {
            writer.Write(values.Count);
            foreach (Vector3 value in values)
            {
                writer.Write(value.X);
                writer.Write(value.Y);
                writer.Write(value.Z);
            }
        }

        static void WriteVector2Array(BinaryWriter writer, IReadOnlyList<Vector2> values)
        {
            writer.Write(values.Count);
            foreach (Vector2 value in values)
            {
                writer.Write(value.X);
                writer.Write(value.Y);
            }
        }

        static void WriteBytes(BinaryWriter writer, byte[]? bytes)
        {
            writer.Write(bytes?.Length ?? -1);
            if (bytes is { Length: > 0 }) writer.Write(bytes);
        }
    }

    private static string ContentKey(byte[] m2, byte[]? blp, IReadOnlyDictionary<string, byte[]>? extras = null)
    {
        using var sha = SHA256.Create();
        sha.TransformBlock(m2, 0, m2.Length, null, 0);
        if (blp is { Length: > 0 }) sha.TransformBlock(blp, 0, blp.Length, null, 0);
        if (extras is not null)
            foreach (var kv in extras.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                sha.TransformBlock(kv.Value, 0, kv.Value.Length, null, 0);
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
    }
}

public sealed record WeaponPreviewResult(
    bool Ok, string? GlbWebPath, int VertexCount, int TriangleCount, string? ContentHash, bool Cached, string? Error)
{
    public static WeaponPreviewResult Fail(string error) => new(false, null, 0, 0, null, false, error);
}
