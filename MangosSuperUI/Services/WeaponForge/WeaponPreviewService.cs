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

    public WeaponPreviewResult RenderFromBytes(byte[] m2Bytes, byte[]? blpBytes)
    {
        if (m2Bytes is null || m2Bytes.Length == 0)
            return WeaponPreviewResult.Fail("No M2 bytes supplied.");

        string key = ContentKey(m2Bytes, blpBytes);
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

        // Bind the single BLP to every sampled texture index (v1 has one Type-2 texture).
        var textures = new Dictionary<int, byte[]>();
        if (blpBytes is { Length: > 0 })
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
    /// M2 writer for its topology exists — the key to showing a sketch become 3D. Cached by content.</summary>
    public WeaponPreviewResult RenderMesh(RigidWeaponMesh mesh, byte[]? texturePng)
    {
        if (mesh.VertexCount == 0 || mesh.Indices.Length < 3)
            return WeaponPreviewResult.Fail("Mesh is empty.");

        string key = MeshContentKey(mesh, texturePng);
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
            var material = new MaterialBuilder("weapon").WithUnlitShader();
            if (texturePng is { Length: > 0 })
                material.WithBaseColor(new SharpGLTF.Memory.MemoryImage(texturePng))
                        .WithChannelParam(KnownChannel.BaseColor, KnownProperty.RGBA, Vector4.One);
            else
                material.WithChannelParam(KnownChannel.BaseColor, KnownProperty.RGBA, new Vector4(0.75f, 0.75f, 0.78f, 1f));

            var mb = new MeshBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty>("weapon");
            var prim = mb.UsePrimitive(material);
            for (int i = 0; i + 2 < mesh.Indices.Length; i += 3)
            {
                uint a = mesh.Indices[i], b = mesh.Indices[i + 1], c = mesh.Indices[i + 2];
                if (a >= mesh.VertexCount || b >= mesh.VertexCount || c >= mesh.VertexCount) continue;
                prim.AddTriangle(MeshVertex(mesh, a), MeshVertex(mesh, b), MeshVertex(mesh, c));
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

    private static string MeshContentKey(RigidWeaponMesh m, byte[]? tex)
    {
        using var sha = SHA256.Create();
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

    private static string ContentKey(byte[] m2, byte[]? blp)
    {
        using var sha = SHA256.Create();
        sha.TransformBlock(m2, 0, m2.Length, null, 0);
        if (blp is { Length: > 0 }) sha.TransformBlock(blp, 0, blp.Length, null, 0);
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
    }
}

public sealed record WeaponPreviewResult(
    bool Ok, string? GlbWebPath, int VertexCount, int TriangleCount, string? ContentHash, bool Cached, string? Error)
{
    public static WeaponPreviewResult Fail(string error) => new(false, null, 0, 0, null, false, error);
}
