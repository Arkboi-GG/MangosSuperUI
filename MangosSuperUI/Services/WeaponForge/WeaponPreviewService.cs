using System.Numerics;
using System.Security.Cryptography;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;

namespace MangosSuperUI.Services.WeaponForge;

/// <summary>
/// The content-hash direct preview (WEAPON_GEN.md §4.1, §7.4). Given raw generated (or
/// MPQ-extracted) M2 + BLP bytes, it renders a GLB by parsing the M2 and binding the display BLP to
/// the M2's Type-2 object-skin slot — WITHOUT any display-id/retexture resolution. This is the
/// piece the existing display-id-driven <c>EnsureGlb</c> cannot do: it resolves a custom display
/// back to a vanilla M2 and ignores custom-M2 bytes, so it can never preview a truly custom model.
///
/// Output GLBs are cached by the SHA-256 of every rendered input (M2, display/supplemental BLPs,
/// mounted effects, and source-graph mode). This is an output cache under wwwroot.
/// </summary>
public sealed class WeaponPreviewService
{
    /// <summary>Vanilla's runtime replacement for TEX_COMPONENT_WEAPON_BLADE (Type 3). This is
    /// loaded only into WebGL previews; it must never be packaged over the stock client member.</summary>
    internal const string WeaponBladePreviewTexturePath =
        @"ITEM\ObjectComponents\WEAPON\ArmorReflect4.BLP";

    private readonly IWebHostEnvironment _env;
    private readonly ILogger<WeaponPreviewService> _logger;

    public WeaponPreviewService(IWebHostEnvironment env, ILogger<WeaponPreviewService> logger)
    {
        _env = env;
        _logger = logger;
    }

    /// <param name="visualEffects">The item's ItemVisual effect models, already resolved and mounted
    /// (see <see cref="M2Fx.ItemVisualEffects"/>). A forged weapon's enchant glow lives entirely in
    /// these, not in its own bytes.</param>
    public WeaponPreviewResult RenderFromBytes(byte[] m2Bytes, byte[]? blpBytes,
        IReadOnlyDictionary<string, byte[]>? effectBlpsByPath = null,
        IReadOnlyList<M2Fx.ItemVisualEffects.Effect>? visualEffects = null,
        bool preserveSourceGraph = false)
    {
        if (m2Bytes is null || m2Bytes.Length == 0)
            return WeaponPreviewResult.Fail("No M2 bytes supplied.");

        string key = ContentKey(m2Bytes, blpBytes, effectBlpsByPath, visualEffects, preserveSourceGraph);
        var cacheDir = Path.Combine(_env.WebRootPath, "weapon_forge_cache");
        Directory.CreateDirectory(cacheDir);
        var fileName = $"wf_{key[..24]}.glb";
        var fullPath = Path.Combine(cacheDir, fileName);
        var webPath = $"/weapon_forge_cache/{fileName}";

        var m2 = M2Reader.Parse(m2Bytes);
        if (m2 is null)
            return WeaponPreviewResult.Fail("M2Reader rejected the bytes (bad magic, version >= 264, or malformed).");

        int triangleCount = m2.Indices.Count / 3;

        // Generated multi-pass weapons can be reconstructed into the same render IR used by the
        // authoring preview. This preserves batch order, rest colors and rest UV transforms that
        // the legacy general-purpose M2→GLB path does not model.
        // The pass-aware path builds its own scene and cannot host mounted effects, so an item that
        // actually has an ItemVisual goes down the general path instead — a correct glow beats a
        // slightly better-modelled set of passes.
        if (!preserveSourceGraph && (visualEffects is null || visualEffects.Count == 0) && CanUsePassAwarePreview(m2))
        {
            var passAware = TryRenderPassAware(m2, blpBytes, effectBlpsByPath);
            if (passAware is { Ok: true }) return passAware;
        }

        if (File.Exists(fullPath))
            return new WeaponPreviewResult(true, webPath, m2.Vertices.Count, triangleCount, key, Cached: true, Error: null);

        // Bind textures by slot: Type-2 (DBC-driven) slots get the base BLP; Type-0 hardcoded
        // slots and Type-3's stock runtime weapon-blade replacement get their exact bytes when the
        // caller supplied them. Replaceable slot types are semantics, not interchangeable images.
        var textures = new Dictionary<int, byte[]>();
        for (int ti = 0; ti < m2.Textures.Count; ti++)
        {
            var t = m2.Textures[ti];
            if (UsesDisplayTexture(t) && blpBytes is { Length: > 0 })
                textures[ti] = blpBytes;
            else if (StockPreviewTexturePath(t) is { } stockPath && effectBlpsByPath is not null &&
                     effectBlpsByPath.TryGetValue(stockPath, out var bytes))
                textures[ti] = bytes;
        }
        // Legacy/generated blobs with no parsed texture table still get the old slot-0 rescue.
        // When the M2 DOES declare slots, an absence of Type 2 is meaningful: binding the display
        // skin to Type 3 would overwrite the client-supplied weapon-blade/reflect texture.
        if (textures.Count == 0 && m2.Textures.Count == 0 && blpBytes is { Length: > 0 })
        {
            foreach (var idx in GlbWriter.SampledTextureIndices(m2)) textures[idx] = blpBytes;
            if (textures.Count == 0) textures[0] = blpBytes; // fall back to slot 0 if nothing resolved
        }

        bool ok = GlbWriter.SaveGlb(m2, textures, fullPath,
            doubleSided: false,
            visualEffects: visualEffects,
            strictTextureSlots: preserveSourceGraph);
        if (!ok)
            return WeaponPreviewResult.Fail("GlbWriter failed to emit a GLB for the parsed M2.");

        _logger.LogInformation("WeaponForge: previewed M2 ({Verts} verts, {Tris} tris) → {Web}",
            m2.Vertices.Count, triangleCount, webPath);
        return new WeaponPreviewResult(true, webPath, m2.Vertices.Count, triangleCount, key, Cached: false, Error: null);
    }

    /// <summary>
    /// The pass-aware renderer rebuilds an M2 as a <see cref="RigidWeaponMesh"/>. That is useful for
    /// generated render graphs, but destructive for stock items whose visible geometry rides animated
    /// or camera-facing bones. Thunderfury is the canonical case: choosing this path strips its
    /// ItemArmature and every GlobalSequence clip before JavaScript ever sees the GLB.
    /// </summary>
    internal static bool CanUsePassAwarePreview(M2Model m2)
        => !SkinnedGlbWriter.RequiresItemSkin(m2);

    /// <summary>
    /// ItemDisplayInfo.TextureName1 fills only TEX_COMPONENT_OBJECT_SKIN (Type 2). Other empty
    /// replaceable slots have their own client semantics; notably Thunderfury's Type 3 weapon-blade
    /// sheen is not a second copy of its diffuse BLP and must never be bound to that display BLP.
    /// </summary>
    internal static bool UsesDisplayTexture(M2TextureRef texture) => texture.Type == 2;

    /// <summary>Every texture slot reached by every unit in the M2 batch graph. This deliberately
    /// differs from <see cref="GlbWriter.SampledTextureIndices"/>, whose first-batch-only result is
    /// used to identify a base recolor target in older authoring paths.</summary>
    internal static HashSet<int> SampledTextureSlots(M2Model m2)
    {
        var result = new HashSet<int>();
        foreach (var batch in m2.Batches)
        {
            for (int unit = 0; unit < batch.TextureCount; unit++)
            {
                long combo = (long)batch.TextureIndex + unit;
                if (combo < 0 || combo >= m2.TextureLookup.Count) continue;
                int slot = m2.TextureLookup[(int)combo];
                if (slot >= 0 && slot < m2.Textures.Count) result.Add(slot);
            }
        }
        return result;
    }

    /// <summary>The Warglaive's two-stage glow keeps a steady UV0 energy base while UV1
    /// supplies the moving modulation wave. Other two-unit materials remain pure modulation.</summary>
    internal static bool UsesSteadyModulatedGlow(ushort blendMode,
        WeaponTextureBinding first, WeaponTextureBinding second)
        => blendMode == 4 &&
           (first.TextureCoordinate & 0x7fff) == 0 &&
           (second.TextureCoordinate & 0x7fff) == 1;

    internal static bool SamplesDisplayTexture(M2Model m2)
        => SampledTextureSlots(m2).Any(slot => UsesDisplayTexture(m2.Textures[slot]));

    /// <summary>Stock bytes a WebGL preview may resolve for a non-display texture slot. Type 0
    /// carries its own filename; Type 3 is supplied by the 1.12 client at runtime.</summary>
    internal static string? StockPreviewTexturePath(M2TextureRef texture) => texture.Type switch
    {
        0 => WeaponTexturePath.Canonicalize(texture.Filename),
        3 => WeaponBladePreviewTexturePath,
        _ => null,
    };

    private WeaponPreviewResult? TryRenderPassAware(M2Model m2, byte[]? baseBlp,
        IReadOnlyDictionary<string, byte[]>? effectBlpsByPath)
    {
        if (m2.Batches.Count == 0 || m2.Submeshes.Count == 0) return null;
        var diagnostics = new ForgeDiagnostics("preview-reconstruct");
        var extracted = LegacyWeaponMeshExtractor.Extract(m2, diagnostics);
        if (extracted is null || diagnostics.HasErrors || extracted.SourceTextures.Count == 0) return null;

        byte[]? ResolveBlp(LegacySourceTexture source)
        {
            if (source.SourcePath is null) return baseBlp;
            return effectBlpsByPath is not null &&
                   effectBlpsByPath.TryGetValue(source.SourcePath, out byte[]? effect)
                ? effect
                : null;
        }

        byte[]? basePng = BlpToPng(ResolveBlp(extracted.SourceTextures[0]));
        if (basePng is null) return null;
        var effectPngs = new List<byte[]?>();
        for (int i = 1; i < extracted.SourceTextures.Count; i++)
        {
            byte[]? png = BlpToPng(ResolveBlp(extracted.SourceTextures[i]));
            if (png is null) return null;
            effectPngs.Add(png);
        }
        return RenderMesh(extracted.Mesh, basePng, effectPngs);
    }

    private static byte[]? BlpToPng(byte[]? blp)
    {
        if (blp is not { Length: > 0 }) return null;
        try
        {
            byte[] bgra = BlpDecoder.GetPixels(blp, 0, out int width, out int height);
            var info = new SkiaSharp.SKImageInfo(width, height, SkiaSharp.SKColorType.Bgra8888,
                SkiaSharp.SKAlphaType.Unpremul);
            using var image = SkiaSharp.SKImage.FromPixelCopy(info, bgra);
            using var png = image?.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            return png?.ToArray();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Render a RigidWeaponMesh directly to a preview GLB (no M2 involved). This lets an
    /// imported/generated mesh be visualized in the weapon authoring space (Y-up glTF) before the
    /// M2 writer for its topology exists — the key to showing a sketch become 3D. Multi-pass meshes
    /// render one primitive per texture binding with per-slot textures (additive glow approximated with alpha
    /// blending — glTF has no additive mode). The material name carries the original WoW blend
    /// mode and static alpha so the three.js viewer can restore them after loading. Cached by
    /// content.</summary>
    /// <param name="emitters">Particle emitters the forge would graft onto this weapon, each with the
    /// decoded PNG of its sheet. The import preview renders an intermediate mesh rather than a
    /// packaged model, so nothing here can read emitters out of forged bytes — the caller hands over
    /// the planned grafts and this attaches them to the GLB the same way GlbWriter does for the
    /// post-build path. Without it, browse-and-preview is the one surface where a forged effect stays
    /// invisible, which is exactly where someone evaluating an import is looking.</param>
    public WeaponPreviewResult RenderMesh(RigidWeaponMesh mesh, byte[]? texturePng,
        IReadOnlyList<byte[]?>? effectPngs = null, bool forceDoubleSided = false,
        IReadOnlyList<PreviewEmitter>? emitters = null,
        IReadOnlyList<M2Fx.ItemVisualEffects.Effect>? visualEffects = null)
    {
        if (mesh.VertexCount == 0 || mesh.Indices.Length < 3)
            return WeaponPreviewResult.Fail("Mesh is empty.");

        string key = MeshContentKey(mesh, texturePng, effectPngs, forceDoubleSided, emitters, visualEffects);
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
            MaterialBuilder BuildMaterial(string name, byte[]? png, ushort blend, Vector3 tint,
                float staticAlpha, bool twoSided, bool environmentMapped, bool modCombine = false,
                bool steadyModulatedGlow = false)
            {
                if (!float.IsFinite(staticAlpha)) staticAlpha = 1f;
                staticAlpha = Math.Clamp(staticAlpha, 0f, 1f);
                tint = Vector3.Clamp(tint, Vector3.Zero, Vector3.One);
                int alphaPercent = Math.Clamp((int)MathF.Round(staticAlpha * 100f), 0, 100);

                // `_env` marks a WoW ENVIRONMENT-MAPPED (sphere/reflection) pass. glTF cannot express
                // it, so the source rest UVs render STATIC and a view-dependent effect (the Warglaive's
                // flowing blade energy) freezes. blend-suffix.js's applyEnvMapping renders these as a
                // matcap — three.js's equivalent of WoW's EnvMap — so the reflection moves as the model
                // turns. Placed BEFORE the blend suffix so blend-suffix.js's end-anchored regex still
                // resolves the blend mode.
                // `_mod` marks a multi-texture MODULATE pass (two energy copies multiplied — the wave).
                // blend-suffix.js applyMultiTexture reconstructs the second sample; kept before `_blend`
                // so the blend-mode regex still resolves.
                name = $"{name}{(environmentMapped ? "_env" : "")}{(modCombine ? "_mod" : "")}" +
                       $"{(steadyModulatedGlow ? "_steady" : "")}_blend{blend}_a{alphaPercent}";
                var m = new MaterialBuilder(name).WithUnlitShader();
                if (png is { Length: > 0 })
                    m.WithBaseColor(new SharpGLTF.Memory.MemoryImage(png))
                     .WithChannelParam(KnownChannel.BaseColor, KnownProperty.RGBA,
                         new Vector4(tint, staticAlpha));
                else
                    m.WithChannelParam(KnownChannel.BaseColor, KnownProperty.RGBA,
                        new Vector4(tint * new Vector3(0.75f, 0.75f, 0.78f), staticAlpha));
                if (blend == 1) m.WithAlpha(AlphaMode.MASK, 0.5f);
                else if (blend >= 2 || staticAlpha < 0.999f)
                    m.WithAlpha(AlphaMode.BLEND); // additive/modulate are restored by the viewer
                if (twoSided) m.WithDoubleSide(true);
                return m;
            }

            var mb = new MeshBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty>("weapon");
            // Second builder for multi-texture MODULATE passes: they need TWO UV sets on one primitive
            // (TEXCOORD_0 = the scrolling copy, TEXCOORD_1 = the static copy) so the client can multiply
            // them into the wave. VertexTexture1 can't carry two UVs, so these go in their own mesh.
            var mbMod = new MeshBuilder<VertexPositionNormal, VertexTexture2, VertexEmpty>("weapon_mod");
            bool usedMod = false;

            // Material animation for this path. The pass IR has carried the UV tracks all along —
            // WeaponRestTextureTransform.TranslationAnimation and friends — and they were hashed into
            // the cache key and then dropped on the floor, while the REST sample was baked into the
            // vertex UVs. That is why the Warglaive of Azzinoth previews dead: it has no particle
            // emitters and no colour records, and its entire effect is one animated UV transform
            // scrolling the energy along the blade.
            //
            // Keyed by MATERIAL name because this writer puts every pass into a single MeshBuilder,
            // so there is no per-pass mesh name to key on. m2fx.js falls back to the material name
            // when a mesh name is not in the manifest.
            var materialFx = new Dictionary<string, M2Fx.M2FxMesh>(StringComparer.Ordinal);

            void AddRange(SharpGLTF.Geometry.PrimitiveBuilder<MaterialBuilder, VertexPositionNormal, VertexTexture1, VertexEmpty> prim,
                int indexStart, int indexCount, bool useUv1, WeaponRestTextureTransform? restTransform)
            {
                int end = Math.Min(indexStart + indexCount, mesh.Indices.Length);
                for (int i = indexStart; i + 2 < end; i += 3)
                {
                    uint a = mesh.Indices[i], b = mesh.Indices[i + 1], c = mesh.Indices[i + 2];
                    if (a >= mesh.VertexCount || b >= mesh.VertexCount || c >= mesh.VertexCount) continue;
                    prim.AddTriangle(MeshVertex(mesh, a, useUv1, restTransform),
                        MeshVertex(mesh, b, useUv1, restTransform),
                        MeshVertex(mesh, c, useUv1, restTransform));
                }
            }

            // MODULATE emit: one primitive carrying both UV sets. TEXCOORD_0 = the scrolling copy's UV
            // (animated by m2fx via map.matrix), TEXCOORD_1 = the static copy's UV (multiplied in by
            // blend-suffix.js applyMultiTexture). Both sample the same texture, at different mappings.
            void AddRangeMod(SharpGLTF.Geometry.PrimitiveBuilder<MaterialBuilder, VertexPositionNormal, VertexTexture2, VertexEmpty> prim,
                int indexStart, int indexCount, bool scrollUsesUv1, bool baseUsesUv1, WeaponRestTextureTransform? scrollRest)
            {
                int end = Math.Min(indexStart + indexCount, mesh.Indices.Length);
                for (int i = indexStart; i + 2 < end; i += 3)
                {
                    uint a = mesh.Indices[i], b = mesh.Indices[i + 1], c = mesh.Indices[i + 2];
                    if (a >= mesh.VertexCount || b >= mesh.VertexCount || c >= mesh.VertexCount) continue;
                    prim.AddTriangle(MeshVertex2(mesh, a, scrollUsesUv1, baseUsesUv1, scrollRest),
                        MeshVertex2(mesh, b, scrollUsesUv1, baseUsesUv1, scrollRest),
                        MeshVertex2(mesh, c, scrollUsesUv1, baseUsesUv1, scrollRest));
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
                    Vector3 tint = pass.RestColor?.Rgb ?? Vector3.One;
                    float colorAlpha = pass.RestColor?.Alpha ?? 1f;

                    void EmitBinding(int bindingIndex, int textureSlot, int textureCoordinate,
                        float staticAlpha, WeaponRestTextureTransform? restTransform)
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
                            pass.BlendMode, tint, staticAlpha * colorAlpha, twoSided, environmentMapped);

                        // An ANIMATED transform must not be baked: the client composes the whole
                        // thing from the manifest, so baking the rest sample too would apply it
                        // twice. A static one still bakes, exactly as before.
                        var fx = BuildUvFx(restTransform, tint, staticAlpha * colorAlpha);
                        if (fx is not null) materialFx[material.Name] = fx;

                        AddRange(mb.UsePrimitive(material), range.IndexStart, range.IndexCount, useUv1,
                            fx is null ? restTransform : null);
                    }

                    if (pass.TextureBindings is { Count: 2 } modB)
                    {
                        // Two texture units on one pass = WoW's multi-texture MODULATE: a static copy of
                        // the energy times a scrolling copy. Their interference is the wave that grows,
                        // shifts and shrinks along the blade. Two separate additive passes can only ADD
                        // (the whole area lights up — the "full glow" bug), so emit ONE primitive the
                        // client multiplies. b0 = static base, b1 = scrolling overlay.
                        var b0 = modB[0];
                        var b1 = modB[1];
                        byte[]? png = TextureForSlot(Convert.ToInt32(b1.TextureSlot), texturePng, effectPngs)
                                      ?? TextureForSlot(Convert.ToInt32(b0.TextureSlot), texturePng, effectPngs);
                        float sa = Convert.ToSingle(b1.StaticAlpha);
                        // Force double-sided: the additive energy submesh is wound to be seen from both
                        // faces (its blade edges point away from a fixed camera), and single-sided
                        // FrontSide culls it entirely — which is why the wave never appeared and only the
                        // base blade showed. Additive energy reads correctly from either face.
                        bool steadyGlow = UsesSteadyModulatedGlow(pass.BlendMode, b0, b1);
                        var material = BuildMaterial($"pass{sourcePassIndex}_tex1", png, pass.BlendMode,
                            tint, sa * colorAlpha, twoSided: true, environmentMapped: false,
                            modCombine: true, steadyModulatedGlow: steadyGlow);

                        // The scroll rides the material's own map.matrix (m2fx), keyed by material name.
                        var fx = BuildUvFx(b1.RestTransform, tint, sa * colorAlpha);
                        if (fx is not null) materialFx[material.Name] = fx;

                        bool hasUv1 = mesh.Uv1 is { Length: > 0 } && mesh.Uv1.Length == mesh.VertexCount;
                        bool scrollUv1 = (Convert.ToInt32(b1.TextureCoordinate) & 0x7fff) == 1 && hasUv1;
                        bool baseUv1 = (Convert.ToInt32(b0.TextureCoordinate) & 0x7fff) == 1 && hasUv1;
                        AddRangeMod(mbMod.UsePrimitive(material), range.IndexStart, range.IndexCount,
                            scrollUv1, baseUv1, fx is null ? b1.RestTransform : null);
                        usedMod = true;
                    }
                    else if (pass.TextureBindings is { Count: > 0 })
                    {
                        int bindingIndex = 0;
                        foreach (var binding in pass.TextureBindings)
                        {
                            EmitBinding(bindingIndex++, Convert.ToInt32(binding.TextureSlot),
                                Convert.ToInt32(binding.TextureCoordinate),
                                Convert.ToSingle(binding.StaticAlpha), binding.RestTransform);
                        }
                    }
                    else
                    {
                        EmitBinding(0, pass.TextureSlot, textureCoordinate: 0, staticAlpha: 1f,
                            restTransform: null);
                    }
                }
            }
            else
            {
                var material = BuildMaterial("weapon", texturePng,
                    mesh.Material.BlendMode == WeaponBlendMode.AlphaKey ? (ushort)1 : (ushort)0,
                    Vector3.One, staticAlpha: 1f, twoSided: forceDoubleSided || mesh.Material.TwoSided,
                    environmentMapped: false);
                AddRange(mb.UsePrimitive(material), 0, mesh.Indices.Length, useUv1: false,
                    restTransform: null);
            }

            var scene = new SceneBuilder("weapon");
            scene.AddRigidMesh(mb, Matrix4x4.Identity);
            if (usedMod) scene.AddRigidMesh(mbMod, Matrix4x4.Identity);
            var model = scene.ToGltf2();
            AttachEmitterManifest(model, emitters, visualEffects, materialFx);
            model.SaveGLB(fullPath);
        }
        catch (Exception ex)
        {
            return WeaponPreviewResult.Fail($"Mesh GLB build failed: {ex.Message}");
        }

        _logger.LogInformation("WeaponForge: previewed mesh ({V} verts, {T} tris) → {Web}", mesh.VertexCount, tris, webPath);
        return new WeaponPreviewResult(true, webPath, mesh.VertexCount, tris, key, Cached: false, Error: null);
    }

    private static VertexBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty> MeshVertex(
        RigidWeaponMesh m, uint i, bool useUv1, WeaponRestTextureTransform? restTransform)
    {
        Vector2 uv = useUv1 && m.Uv1 is { } uv1 && i < (uint)uv1.Length ? uv1[i] : m.Uv0[i];
        if (restTransform is not null)
        {
            // M2 UV rotations pivot around the texture center, not the origin.
            Vector3 center = new(0.5f, 0.5f, 0f);
            Vector3 transformed = (new Vector3(uv, 0f) - center) * restTransform.Scale;
            transformed = Vector3.Transform(transformed, restTransform.Rotation) + center +
                          restTransform.Translation;
            uv = new Vector2(transformed.X, transformed.Y);
        }
        return new VertexBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty>(
            new VertexPositionNormal(m.Positions[i], m.Normals[i]), new VertexTexture1(uv));
    }

    /// <summary>Vertex carrying BOTH UV sets for a multi-texture MODULATE pass: TEXCOORD_0 is the
    /// scrolling copy's mapping (animated on the client via map.matrix), TEXCOORD_1 the static copy's.
    /// A non-null <paramref name="scrollRest"/> means the scroll is NOT animated, so its rest transform
    /// is baked into TEXCOORD_0 exactly as the single-texture path bakes a static transform.</summary>
    private static VertexBuilder<VertexPositionNormal, VertexTexture2, VertexEmpty> MeshVertex2(
        RigidWeaponMesh m, uint i, bool scrollUsesUv1, bool baseUsesUv1, WeaponRestTextureTransform? scrollRest)
    {
        Vector2 scrollUv = scrollUsesUv1 && m.Uv1 is { } su && i < (uint)su.Length ? su[i] : m.Uv0[i];
        Vector2 baseUv = baseUsesUv1 && m.Uv1 is { } bu && i < (uint)bu.Length ? bu[i] : m.Uv0[i];
        if (scrollRest is not null)
        {
            Vector3 center = new(0.5f, 0.5f, 0f);
            Vector3 t = (new Vector3(scrollUv, 0f) - center) * scrollRest.Scale;
            t = Vector3.Transform(t, scrollRest.Rotation) + center + scrollRest.Translation;
            scrollUv = new Vector2(t.X, t.Y);
        }
        return new VertexBuilder<VertexPositionNormal, VertexTexture2, VertexEmpty>(
            new VertexPositionNormal(m.Positions[i], m.Normals[i]), new VertexTexture2(scrollUv, baseUv));
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
        IReadOnlyList<byte[]?>? effectPngs, bool forceDoubleSided,
        IReadOnlyList<PreviewEmitter>? emitters = null,
        IReadOnlyList<M2Fx.ItemVisualEffects.Effect>? visualEffects = null)
    {
        // This deliberately hashes the full rendered state rather than a few low bytes of it.
        // Otherwise UV-only edits, high render-flag bits, pass-layer changes, or a null texture
        // could incorrectly reuse an older GLB from the output cache.
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            // v5: the GLB now carries the suiFx manifest (material animation + particle emitters),
            // so a pre-change GLB in the cache is no longer an acceptable answer for the same mesh.
            // v7: env-mapped passes carry the `_env` marker for the matcap render path.
            // v8: multi-texture passes emit ONE 2-UV primitive (`_mod`) for the modulate/wave combine.
            // v9: the _mod (additive energy) pass is forced double-sided so it isn't backface-culled.
            // v10: blend-4 UV0+UV1 modulation carries a steady-base marker for glow + wave.
            writer.Write("WeaponPreviewMesh/v10");
            writer.Write(visualEffects?.Count ?? -1);
            foreach (var v in visualEffects ?? Array.Empty<M2Fx.ItemVisualEffects.Effect>())
            {
                writer.Write(v.ModelPath);
                writer.Write(v.MountMesh.X); writer.Write(v.MountMesh.Y); writer.Write(v.MountMesh.Z);
                WriteBytes(writer, v.M2);
                writer.Write(v.Textures.Count);
                foreach (var texture in v.Textures.OrderBy(kv => kv.Key))
                {
                    writer.Write(texture.Key);
                    WriteBytes(writer, texture.Value);
                }
            }
            writer.Write(emitters?.Count ?? -1);
            foreach (var e in emitters ?? Array.Empty<PreviewEmitter>())
            {
                writer.Write(e.PositionMesh.X); writer.Write(e.PositionMesh.Y); writer.Write(e.PositionMesh.Z);
                writer.Write(e.Graft.DonorEmitterIndex);
                writer.Write(e.Graft.TexturePath ?? "");
                writer.Write(e.Graft.Scale ?? -1f);
                writer.Write(e.Graft.ColorRgb?.ToString() ?? "");
                writer.Write(e.Graft.Motion?.ToString() ?? "");
                writer.Write(e.Png?.Length ?? -1);
                if (e.Png is not null) writer.Write(e.Png);
            }
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
                    WriteRestColor(writer, pass.RestColor);

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
                            WriteRestTransform(writer, binding.RestTransform);
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

        static void WriteRestColor(BinaryWriter writer, WeaponRestColor? color)
        {
            writer.Write(color is not null);
            if (color is null) return;
            writer.Write(color.Rgb.X); writer.Write(color.Rgb.Y); writer.Write(color.Rgb.Z);
            writer.Write(color.Alpha); writer.Write(color.AnimationFrozen);
        }

        static void WriteRestTransform(BinaryWriter writer, WeaponRestTextureTransform? transform)
        {
            writer.Write(transform is not null);
            if (transform is null) return;
            writer.Write(transform.Translation.X); writer.Write(transform.Translation.Y); writer.Write(transform.Translation.Z);
            writer.Write(transform.Rotation.X); writer.Write(transform.Rotation.Y);
            writer.Write(transform.Rotation.Z); writer.Write(transform.Rotation.W);
            writer.Write(transform.Scale.X); writer.Write(transform.Scale.Y); writer.Write(transform.Scale.Z);
            writer.Write(transform.AnimationFrozen);
            WriteGlobalVectorTrack(writer, transform.TranslationAnimation);
            WriteGlobalQuaternionTrack(writer, transform.RotationAnimation);
            WriteGlobalVectorTrack(writer, transform.ScaleAnimation);
        }

        static void WriteGlobalVectorTrack(BinaryWriter writer, WeaponGlobalVectorTrack? track)
        {
            writer.Write(track is not null);
            if (track is null) return;
            writer.Write(track.Interpolation);
            writer.Write(track.SourceGlobalSequence);
            writer.Write(track.DurationMs);
            WriteUInt32Array(writer, track.Timestamps);
            writer.Write(track.Keys?.Count ?? -1);
            if (track.Keys is null) return;
            foreach (Vector3 key in track.Keys)
            {
                writer.Write(key.X); writer.Write(key.Y); writer.Write(key.Z);
            }
        }

        static void WriteGlobalQuaternionTrack(BinaryWriter writer, WeaponGlobalQuaternionTrack? track)
        {
            writer.Write(track is not null);
            if (track is null) return;
            writer.Write(track.Interpolation);
            writer.Write(track.SourceGlobalSequence);
            writer.Write(track.DurationMs);
            WriteUInt32Array(writer, track.Timestamps);
            writer.Write(track.Keys?.Count ?? -1);
            if (track.Keys is null) return;
            foreach (Quaternion key in track.Keys)
            {
                writer.Write(key.X); writer.Write(key.Y); writer.Write(key.Z); writer.Write(key.W);
            }
        }

        static void WriteUInt32Array(BinaryWriter writer, IReadOnlyList<uint>? values)
        {
            writer.Write(values?.Count ?? -1);
            if (values is null) return;
            foreach (uint value in values) writer.Write(value);
        }
    }

    /// <summary>
    /// Turn a pass's rest texture transform into a manifest entry, or null when it does not animate.
    ///
    /// The IR only ever holds range-free GLOBAL-sequence tracks (the strict reader refuses anything
    /// else), so each one is already a self-contained loop of known period — exactly what the client
    /// wants. Rotation is reduced to the Z angle because texture space has one rotational degree of
    /// freedom and the client applies it with a single setUvTransform.
    /// </summary>
    private static M2Fx.M2FxMesh? BuildUvFx(WeaponRestTextureTransform? t, Vector3 tint, float alpha)
    {
        if (t is null) return null;
        if (t.TranslationAnimation is null && t.RotationAnimation is null && t.ScaleAnimation is null)
            return null;

        var uv = new M2Fx.M2FxUv(
            Base: new[]
            {
                t.Translation.X, t.Translation.Y, ZAngle(t.Rotation), t.Scale.X, t.Scale.Y,
            },
            Translate: VectorTrack(t.TranslationAnimation, components: 3),
            Rotate: QuaternionTrack(t.RotationAnimation),
            Scale: VectorTrack(t.ScaleAnimation, components: 3));

        if (!uv.Any) return null;
        return new M2Fx.M2FxMesh(null, null, null, uv,
            BaseRgb: new[] { tint.X, tint.Y, tint.Z },
            BaseAlpha: alpha,
            BaseWeight: 1f);
    }

    private static float ZAngle(Quaternion q) => 2f * MathF.Atan2(q.Z, q.W);

    private static M2Fx.M2FxTrack? VectorTrack(WeaponGlobalVectorTrack? track, int components)
    {
        if (track is null || track.Keys.Count < 2 || track.DurationMs == 0) return null;
        int n = Math.Min(track.Keys.Count, track.Timestamps.Count);
        if (n < 2) return null;

        var times = new uint[n];
        var keys = new float[n][];
        for (int i = 0; i < n; i++)
        {
            times[i] = track.Timestamps[i];
            var k = track.Keys[i];
            keys[i] = components == 1 ? new[] { k.X } : new[] { k.X, k.Y, k.Z };
        }
        return new M2Fx.M2FxTrack(track.DurationMs, components, track.Interpolation == 0, times, keys);
    }

    private static M2Fx.M2FxTrack? QuaternionTrack(WeaponGlobalQuaternionTrack? track)
    {
        if (track is null || track.Keys.Count < 2 || track.DurationMs == 0) return null;
        int n = Math.Min(track.Keys.Count, track.Timestamps.Count);
        if (n < 2) return null;

        var times = new uint[n];
        var keys = new float[n][];
        for (int i = 0; i < n; i++)
        {
            times[i] = track.Timestamps[i];
            keys[i] = new[] { ZAngle(track.Keys[i]) };
        }
        return new M2Fx.M2FxTrack(track.DurationMs, 1, track.Interpolation == 0, times, keys);
    }

    /// <summary>One planned emitter plus the decoded sheet it samples.</summary>
    /// <param name="Graft">The graft as the motion planner resolved it.</param>
    /// <param name="PositionMesh">Placed position in the preview's Y-up mesh space — the graft carries
    /// the WoW-space one, and the placement transform has already moved it.</param>
    /// <param name="Png">The emitter sheet, decoded. Null drops the emitter, because an untextured
    /// additive quad is a white blob.</param>
    public sealed record PreviewEmitter(
        RawM2.M2EmitterTransplanter.Graft Graft,
        System.Numerics.Vector3 PositionMesh,
        byte[]? Png);

    /// <summary>Embed each emitter's sheet and write the suiFx manifest onto the model root — the
    /// same contract GlbWriter uses, so one client module reads both paths.</summary>
    private static void AttachEmitterManifest(
        SharpGLTF.Schema2.ModelRoot model, IReadOnlyList<PreviewEmitter>? emitters,
        IReadOnlyList<M2Fx.ItemVisualEffects.Effect>? visualEffects = null,
        IReadOnlyDictionary<string, M2Fx.M2FxMesh>? materialFx = null)
    {
        bool anything = emitters is { Count: > 0 } || visualEffects is { Count: > 0 }
                        || materialFx is { Count: > 0 };
        if (!anything) return;

        try
        {
            var built = new List<M2Fx.M2FxEmitter>();
            foreach (var e in emitters ?? Array.Empty<PreviewEmitter>())
            {
                if (e.Png is not { Length: > 0 }) continue;

                var image = model.CreateImage();
                image.Content = new SharpGLTF.Memory.MemoryImage(e.Png);
                image.Name = $"EmitterSheet_{built.Count}";
                var texture = model.UseTexture(image);
                texture.Name = image.Name;

                var emitter = M2Fx.M2FxReader.FromGraft(e.Graft, texture.LogicalIndex, e.PositionMesh);
                if (emitter is not null) built.Add(emitter);
            }

            built.AddRange(GlbWriter.EmbedVisualEffects(model, visualEffects));

            var meshes = materialFx is null
                ? new Dictionary<string, M2Fx.M2FxMesh>()
                : new Dictionary<string, M2Fx.M2FxMesh>(materialFx, StringComparer.Ordinal);

            if (built.Count == 0 && meshes.Count == 0) return;
            var manifest = new M2Fx.M2FxManifest(Array.Empty<uint>(), meshes, built);
            model.Extras = manifest.ToExtras();
        }
        catch { /* the manifest is an enhancement; never fail a preview over one */ }
    }

    private static string ContentKey(byte[] m2, byte[]? blp, IReadOnlyDictionary<string, byte[]>? extras = null,
        IReadOnlyList<M2Fx.ItemVisualEffects.Effect>? visualEffects = null, bool preserveSourceGraph = false)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(preserveSourceGraph);
            writer.Write(visualEffects?.Count ?? -1);
            foreach (var v in visualEffects ?? Array.Empty<M2Fx.ItemVisualEffects.Effect>())
            {
                writer.Write(v.ModelPath);
                writer.Write(v.MountMesh.X); writer.Write(v.MountMesh.Y); writer.Write(v.MountMesh.Z);
                WriteSegment(v.M2);
                writer.Write(v.Textures.Count);
                foreach (var texture in v.Textures.OrderBy(kv => kv.Key))
                {
                    writer.Write(texture.Key);
                    WriteSegment(texture.Value);
                }
            }
            // v3: GlbWriter now bakes the M2Color rest tint into baseColorFactor and attaches the
            // suiFx material-animation manifest. This cache is keyed on the INPUT bytes only, so a
            // writer change is invisible to it — without the salt bump every already-previewed
            // weapon would keep serving its pre-change GLB and the fix would look like a no-op.
            // v4: GlbWriter now emits compositing overlay LAYERS (blend >= 3), `_env` / `_mod`
            // material markers and a TEXCOORD_1 set for fused MODULATE passes. This cache is keyed
            // on the INPUT bytes only and weapon_forge_cache is NOT swept by CacheVersionRegistry,
            // so without the salt bump the Forge keeps serving pre-change GLBs while the Items page
            // (which IS MVID-swept) regenerates — the same weapon rendering two different ways on
            // two pages, which is exactly the divergence this work exists to remove.
            // v5: source items that need an item skin bypass the rigid pass-aware reconstruction,
            // and the display BLP binds only to Type 2 rather than every non-Type-0 slot. Both are
            // load-bearing for Thunderfury; stale v4 GLBs have already lost its rig and Type-3 sheen.
            // v6: source-preserved previews require exact texture slots, bind the client's stock
            // Type-3 weapon-blade replacement explicitly, and hash resolved ItemVisual bytes.
            writer.Write("WeaponPreviewBytes/v6");
            WriteSegment(m2);
            WriteSegment(blp);
            writer.Write(extras?.Count ?? -1);
            if (extras is not null)
                foreach (var kv in extras.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                {
                    writer.Write(kv.Key);
                    WriteSegment(kv.Value);
                }

            void WriteSegment(byte[]? bytes)
            {
                writer.Write(bytes?.Length ?? -1);
                if (bytes is { Length: > 0 }) writer.Write(bytes);
            }
        }
        stream.TryGetBuffer(out ArraySegment<byte> buffer);
        return Convert.ToHexString(SHA256.HashData(buffer.AsSpan(0, checked((int)stream.Length))))
            .ToLowerInvariant();
    }
}

public sealed record WeaponPreviewResult(
    bool Ok, string? GlbWebPath, int VertexCount, int TriangleCount, string? ContentHash, bool Cached, string? Error)
{
    public static WeaponPreviewResult Fail(string error) => new(false, null, 0, 0, null, false, error);
}
