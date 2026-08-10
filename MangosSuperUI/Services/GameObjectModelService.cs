using System.Numerics;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;

namespace MangosSuperUI.Services;

using VERTEX = VertexBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty>;

/// <summary>
/// Generates game-object GLB previews ON DEMAND from the client MPQs —
/// the gameobject counterpart of ItemTextureService.EnsureGlb. There is
/// no extraction step: GameObjectDisplayInfo.dbc resolves displayId →
/// client model path, the model (M2 or WMO) plus its BLP textures are
/// read straight from the MPQ archives, and the resulting GLB is cached
/// under wwwroot/models with the standard version stamp so a writer
/// change invalidates prior output (swept by CacheVersionRegistry).
///
/// Model types:
///   .mdx/.mdl/.m2 — parsed with M2Reader, written with GlbWriter
///     (doubleSided: doodad props commonly have single-sided cloth/flap
///     geometry that vanishes under backface culling after the Z-up →
///     Y-up transform — same reasoning as armor attachments).
///   .wmo — root + group files parsed with WmoReader, written here with
///     SharpGLTF directly (GlbWriter is M2-shaped). All WMO materials are
///     double-sided: building interiors are hollow shells, and winding
///     conventions differ from M2, so culling would hide whole walls.
/// </summary>
public class GameObjectModelService
{
    private readonly MpqReaderService _mpq;
    private readonly DbcService _dbc;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<GameObjectModelService> _logger;

    public GameObjectModelService(
        MpqReaderService mpq,
        DbcService dbc,
        IWebHostEnvironment env,
        ILogger<GameObjectModelService> logger)
    {
        _mpq = mpq;
        _dbc = dbc;
        _env = env;
        _logger = logger;
    }

    /// <summary>True when the DBC defines a client model for this displayId —
    /// i.e. a GLB can (in principle) be generated on demand. Cheap; no I/O.</summary>
    public bool HasModel(uint displayId) =>
        _dbc.GetGameObjectModelPath(displayId) != null;

    /// <summary>Cached web path if the GLB was already generated, else null. No generation.</summary>
    public string? TryGetCachedWebPath(uint displayId)
    {
        var (glbPath, webPath) = CachePaths(displayId);
        return File.Exists(glbPath) ? webPath : null;
    }

    /// <summary>
    /// Ensure a GLB exists for the displayId, generating it from the MPQs if
    /// needed. Returns the web path, or null when the displayId has no model
    /// or generation failed (missing files, parse failure).
    /// </summary>
    public string? EnsureGlb(uint displayId)
    {
        if (displayId == 0) return null;

        var modelPath = _dbc.GetGameObjectModelPath(displayId);
        if (string.IsNullOrEmpty(modelPath)) return null;

        var (glbPath, webPath) = CachePaths(displayId);
        if (File.Exists(glbPath)) return webPath;

        try
        {
            bool ok = modelPath.EndsWith(".wmo", StringComparison.OrdinalIgnoreCase)
                ? BuildWmoGlb(modelPath, glbPath)
                : BuildM2Glb(modelPath, glbPath);

            if (ok)
            {
                _logger.LogInformation("GoModel: Generated GLB for displayId {Id} ({Model}, {Size}KB)",
                    displayId, modelPath, new FileInfo(glbPath).Length / 1024);
                return webPath;
            }

            _logger.LogWarning("GoModel: Generation failed for displayId {Id} ({Model})", displayId, modelPath);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GoModel: Exception generating displayId {Id} ({Model})", displayId, modelPath);
            return null;
        }
    }

    private (string glbPath, string webPath) CachePaths(uint displayId)
    {
        var versioned = CacheVersionRegistry.MakeVersioned(
            $"{displayId}.glb", CacheVersionRegistry.RigidGlbVersion);
        return (Path.Combine(_env.WebRootPath, "models", versioned), $"/models/{versioned}");
    }

    // ── M2 path ─────────────────────────────────────────────────────────

    private bool BuildM2Glb(string modelPath, string glbPath)
    {
        var m2Data = _mpq.ExtractModelFile(modelPath);
        if (m2Data == null)
        {
            _logger.LogDebug("GoModel: M2 not in MPQ: {Path}", modelPath);
            return false;
        }

        var m2Model = M2Reader.Parse(m2Data);
        if (m2Model == null || !m2Model.IsValid) return false;

        // GO M2s reference their textures by filename (unlike items, which
        // resolve skin slots via ItemDisplayInfo) — extract each directly.
        var textures = new Dictionary<int, byte[]>();
        for (int i = 0; i < m2Model.Textures.Count; i++)
        {
            var texRef = m2Model.Textures[i];
            if (string.IsNullOrEmpty(texRef.Filename)) continue;

            var blpData = _mpq.ExtractFile(texRef.Filename)
                       ?? _mpq.ExtractFile(texRef.Filename.ToLowerInvariant());
            if (blpData != null)
                textures[i] = blpData;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(glbPath)!);
        return GlbWriter.SaveGlb(m2Model, textures, glbPath, doubleSided: true);
    }

    // ── WMO path ────────────────────────────────────────────────────────

    private bool BuildWmoGlb(string wmoPath, string glbPath)
    {
        var rootData = ExtractEitherSlash(wmoPath);
        if (rootData == null)
        {
            _logger.LogDebug("GoModel: WMO root not in MPQ: {Path}", wmoPath);
            return false;
        }

        var root = WmoReader.ParseRoot(rootData);
        if (root == null) return false;

        // Decode each material's diffuse texture to PNG once.
        var pngByMaterial = new Dictionary<int, byte[]>();
        for (int mi = 0; mi < root.Materials.Count; mi++)
        {
            var texName = root.Materials[mi].Texture0Name;
            if (string.IsNullOrEmpty(texName)) continue;

            var blp = ExtractEitherSlash(texName);
            var png = blp != null ? GlbWriter.ConvertBlpToPngBytes(blp) : null;
            if (png != null) pngByMaterial[mi] = png;
        }

        var fallbackMat = new MaterialBuilder("default")
            .WithUnlitShader()
            .WithDoubleSide(true)
            .WithChannelParam(KnownChannel.BaseColor, KnownProperty.RGBA, new Vector4(0.7f, 0.7f, 0.7f, 1f));

        var matCache = new Dictionary<int, MaterialBuilder>();
        MaterialBuilder GetMaterial(int materialId)
        {
            if (matCache.TryGetValue(materialId, out var existing)) return existing;

            MaterialBuilder mat;
            if (pngByMaterial.TryGetValue(materialId, out var png))
            {
                mat = new MaterialBuilder($"wmo_mat_{materialId}")
                    .WithUnlitShader()
                    .WithBaseColor(new SharpGLTF.Memory.MemoryImage(png))
                    .WithDoubleSide(true);

                // WMO blend modes: 0 opaque, 1 alpha-key, 2+ alpha-blend.
                if (materialId < root.Materials.Count)
                {
                    var blend = root.Materials[materialId].BlendMode;
                    if (blend >= 2) mat.WithAlpha(AlphaMode.BLEND);
                    else if (blend == 1) mat.WithAlpha(AlphaMode.MASK, 0.5f);
                }
            }
            else
            {
                mat = fallbackMat;
            }

            matCache[materialId] = mat;
            return mat;
        }

        var scene = new SceneBuilder("scene");
        string basePath = wmoPath[..^4];
        int groupsBuilt = 0;

        for (int gi = 0; gi < (int)root.NGroups; gi++)
        {
            var groupData = ExtractEitherSlash($"{basePath}_{gi:D3}.wmo");
            if (groupData == null) continue;

            var group = WmoReader.ParseGroup(groupData);
            if (group == null || group.Vertices.Count == 0 || group.Indices.Count < 3) continue;

            var meshBuilder = new MeshBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty>($"Group{gi}");

            VERTEX MakeWmoVertex(int idx)
            {
                var (x, y, z) = group.Vertices[idx];
                // WMO is Z-up: (x, z, -y) is a proper rotation to glTF's Y-up.
                var pos = new Vector3(x, z, -y);

                Vector3 norm;
                if (idx < group.Normals.Count)
                {
                    var (nx, ny, nz) = group.Normals[idx];
                    norm = new Vector3(nx, nz, -ny);
                }
                else norm = Vector3.UnitY;

                var uv = idx < group.UVs.Count
                    ? new Vector2(group.UVs[idx].u, group.UVs[idx].v)
                    : Vector2.Zero;

                return new VERTEX(new VertexPositionNormal(pos, norm), new VertexTexture1(uv));
            }

            // Prefer MOBA batches (material + contiguous index range); fall back
            // to per-triangle MOPY materials when a group has no batches.
            if (group.Batches.Count > 0)
            {
                foreach (var batch in group.Batches)
                {
                    var prim = meshBuilder.UsePrimitive(GetMaterial(batch.MaterialId));
                    int end = (int)batch.IndexStart + batch.IndexCount;
                    for (int i = (int)batch.IndexStart; i + 2 < end && i + 2 < group.Indices.Count; i += 3)
                    {
                        int i0 = group.Indices[i], i1 = group.Indices[i + 1], i2 = group.Indices[i + 2];
                        if (i0 >= group.Vertices.Count || i1 >= group.Vertices.Count || i2 >= group.Vertices.Count)
                            continue;
                        prim.AddTriangle(MakeWmoVertex(i0), MakeWmoVertex(i1), MakeWmoVertex(i2));
                    }
                }
            }
            else
            {
                for (int t = 0; t * 3 + 2 < group.Indices.Count; t++)
                {
                    byte materialId = t < group.TriMaterials.Count ? group.TriMaterials[t].materialId : (byte)0;
                    if (materialId == 0xFF) continue; // collision-only triangle

                    var prim = meshBuilder.UsePrimitive(GetMaterial(materialId));
                    int i0 = group.Indices[t * 3], i1 = group.Indices[t * 3 + 1], i2 = group.Indices[t * 3 + 2];
                    if (i0 >= group.Vertices.Count || i1 >= group.Vertices.Count || i2 >= group.Vertices.Count)
                        continue;
                    prim.AddTriangle(MakeWmoVertex(i0), MakeWmoVertex(i1), MakeWmoVertex(i2));
                }
            }

            scene.AddRigidMesh(meshBuilder, Matrix4x4.Identity);
            groupsBuilt++;
        }

        if (groupsBuilt == 0)
        {
            _logger.LogWarning("GoModel: WMO {Path} — no group files found/parsed", wmoPath);
            return false;
        }

        var model = scene.ToGltf2();
        Directory.CreateDirectory(Path.GetDirectoryName(glbPath)!);
        model.SaveGLB(glbPath);
        return true;
    }

    /// <summary>ExtractFile trying the path with backslashes then forward slashes.</summary>
    private byte[]? ExtractEitherSlash(string path)
    {
        return _mpq.ExtractFile(path.Replace('/', '\\'))
            ?? _mpq.ExtractFile(path.Replace('\\', '/'));
    }
}
