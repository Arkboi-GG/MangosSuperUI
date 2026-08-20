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
    /// render one primitive per pass with per-slot textures (additive glow approximated with alpha
    /// blending — glTF has no additive mode). Cached by content.</summary>
    public WeaponPreviewResult RenderMesh(RigidWeaponMesh mesh, byte[]? texturePng,
        IReadOnlyList<byte[]?>? effectPngs = null)
    {
        if (mesh.VertexCount == 0 || mesh.Indices.Length < 3)
            return WeaponPreviewResult.Fail("Mesh is empty.");

        string key = MeshContentKey(mesh, texturePng, effectPngs);
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
            MaterialBuilder BuildMaterial(string name, byte[]? png, ushort blend, bool twoSided)
            {
                var m = new MaterialBuilder(name).WithUnlitShader();
                if (png is { Length: > 0 })
                    m.WithBaseColor(new SharpGLTF.Memory.MemoryImage(png))
                     .WithChannelParam(KnownChannel.BaseColor, KnownProperty.RGBA, Vector4.One);
                else
                    m.WithChannelParam(KnownChannel.BaseColor, KnownProperty.RGBA, new Vector4(0.75f, 0.75f, 0.78f, 1f));
                if (blend == 1) m.WithAlpha(AlphaMode.MASK, 0.5f);
                else if (blend >= 2) m.WithAlpha(AlphaMode.BLEND); // alpha + additive approximation
                if (twoSided) m.WithDoubleSide(true);
                return m;
            }

            var mb = new MeshBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty>("weapon");

            void AddRange(SharpGLTF.Geometry.PrimitiveBuilder<MaterialBuilder, VertexPositionNormal, VertexTexture1, VertexEmpty> prim,
                int indexStart, int indexCount)
            {
                int end = Math.Min(indexStart + indexCount, mesh.Indices.Length);
                for (int i = indexStart; i + 2 < end; i += 3)
                {
                    uint a = mesh.Indices[i], b = mesh.Indices[i + 1], c = mesh.Indices[i + 2];
                    if (a >= mesh.VertexCount || b >= mesh.VertexCount || c >= mesh.VertexCount) continue;
                    prim.AddTriangle(MeshVertex(mesh, a), MeshVertex(mesh, b), MeshVertex(mesh, c));
                }
            }

            if (mesh.Passes is { Count: > 0 } && mesh.SubmeshRanges is { Count: > 0 })
            {
                int pi = 0;
                foreach (var pass in mesh.Passes)
                {
                    if (pass.SubmeshSlot >= mesh.SubmeshRanges.Count) continue;
                    var range = mesh.SubmeshRanges[pass.SubmeshSlot];
                    byte[]? png = pass.TextureSlot == 0
                        ? texturePng
                        : (effectPngs is not null && pass.TextureSlot - 1 < effectPngs.Count
                            ? effectPngs[pass.TextureSlot - 1] : null);
                    var material = BuildMaterial($"pass{pi++}", png, pass.BlendMode, (pass.RenderFlags & 0x04) != 0);
                    AddRange(mb.UsePrimitive(material), range.IndexStart, range.IndexCount);
                }
            }
            else
            {
                var material = BuildMaterial("weapon", texturePng,
                    mesh.Material.BlendMode == WeaponBlendMode.AlphaKey ? (ushort)1 : (ushort)0,
                    mesh.Material.TwoSided);
                AddRange(mb.UsePrimitive(material), 0, mesh.Indices.Length);
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

    private static VertexBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty> MeshVertex(RigidWeaponMesh m, uint i) =>
        new(new VertexPositionNormal(m.Positions[i], m.Normals[i]), new VertexTexture1(m.Uv0[i]));

    private static string MeshContentKey(RigidWeaponMesh m, byte[]? tex, IReadOnlyList<byte[]?>? effectPngs = null)
    {
        using var sha = SHA256.Create();
        // Material/pass bits participate: the same geometry with different passes is a different GLB.
        var matBits = new byte[] { (byte)m.Material.BlendMode, m.Material.TwoSided ? (byte)1 : (byte)0,
            (byte)(m.Passes?.Count ?? 0) };
        sha.TransformBlock(matBits, 0, matBits.Length, null, 0);
        if (m.Passes is not null)
            foreach (var p in m.Passes)
            {
                var pb = new byte[] { (byte)p.SubmeshSlot, (byte)(p.RenderFlags & 0xFF), (byte)p.BlendMode, (byte)p.TextureSlot };
                sha.TransformBlock(pb, 0, pb.Length, null, 0);
            }
        if (effectPngs is not null)
            foreach (var e in effectPngs)
                if (e is { Length: > 0 }) sha.TransformBlock(e, 0, e.Length, null, 0);
        var buf = new byte[m.VertexCount * 12 + m.Indices.Length * 4];
        int o = 0;
        foreach (var p in m.Positions)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(o), p.X); o += 4;
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(o), p.Y); o += 4;
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(o), p.Z); o += 4;
        }
        foreach (var ix in m.Indices) { System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(o), ix); o += 4; }
        sha.TransformBlock(buf, 0, buf.Length, null, 0);
        if (tex is { Length: > 0 }) sha.TransformBlock(tex, 0, tex.Length, null, 0);
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
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
